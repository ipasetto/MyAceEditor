using System.Drawing;
using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Céu do Mundo 3D como o do jogo: cada área de ambiente do mapa (CECScnBoxArea, .ecbsd) traz 3 texturas de céu
/// (textures\sky\…: topo + as duas metades do lado, A3DSkySphere). Nas instâncias fechadas essas texturas são pedra/teto
/// (a35 Roof.bmp, a24 魔域\roog.bmp, a27 神月岛\顶.bmp) — o "teto" que o jogador vê. Só o céu de dia (luz fixa do Mundo 3D).
/// A cúpula fica em volta da câmera, desenhada antes de tudo, sem neblina e sem profundidade.
/// </summary>
internal sealed class WorldSky : IDisposable
{
    readonly PckArchive? _pck;
    readonly List<WorldScene.BoxArea> _areas = new();
    readonly Dictionary<string, int> _tex = new(StringComparer.OrdinalIgnoreCase);
    WorldScene.BoxArea? _cur;

    public WorldSky(string? elementDir, string mapDir)
    {
        try
        {
            if (elementDir is not null && File.Exists(Path.Combine(elementDir, "textures.pck"))) _pck = new PckArchive(Path.Combine(elementDir, "textures.pck"));
            if (Directory.EnumerateFiles(mapDir, "*.ecwld").FirstOrDefault() is { } ecwld)
            {
                var scene = new WorldScene(ecwld);
                if (Directory.EnumerateFiles(mapDir, "*.ecbsd").FirstOrDefault() is { } bsd) scene.LoadAreas(bsd);
                _areas.AddRange(scene.Areas.Where(a => a.Sky.Any(s => s.Length > 0)));
            }
        }
        catch (Exception ex) { AppLog.Error("céu do Mundo 3D", ex); }
    }

    public bool HasSky => _areas.Count > 0 && _pck is not null;

    // área da câmera: a caixa (não padrão) que a contém; senão a área padrão mais perto (no mundo há uma por bloco de 1 km)
    WorldScene.BoxArea? AreaAt(Vector3 game)
    {
        foreach (var a in _areas.Where(a => !a.Default))
        {
            var d = new Vector3(game.X - a.Pos[0], game.Y - a.Pos[1], game.Z - a.Pos[2]);
            if (MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisX[0], a.AxisX[1], a.AxisX[2]))) <= a.Ext[0]
                && MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisY[0], a.AxisY[1], a.AxisY[2]))) <= a.Ext[1]
                && MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisZ[0], a.AxisZ[1], a.AxisZ[2]))) <= a.Ext[2]) return a;
        }
        return _areas.Where(a => a.Default).MinBy(a => (a.Pos[0] - game.X) * (a.Pos[0] - game.X) + (a.Pos[2] - game.Z) * (a.Pos[2] - game.Z))
            ?? _areas.FirstOrDefault();
    }

    int Texture(string name)
    {
        if (name.Length == 0 || _pck is null) return 0;
        if (_tex.TryGetValue(name, out var id)) return id;
        id = 0;
        try
        {
            var path = (@"textures\sky\" + name.Replace('/', '\\')).ToLowerInvariant();
            if (_pck.Find(path) is { } e)
            {
                var d = _pck.Read(e);
                using var ms = new MemoryStream(d);
                using var src = path.EndsWith(".dds") ? null : new Bitmap(ms);
                (int[] Px, int W, int H)? img = src is null ? DdsDecoder.DecodeMax(d, 1024) : FromBitmap(src);
                if (img is { } im)
                {
                    id = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, id);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, im.W, im.H, 0, PixelFormat.Bgra, PixelType.UnsignedByte, im.Px);
                    GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                }
            }
        }
        catch (Exception ex) { AppLog.Info($"céu: textura {name}: {ex.Message}"); }
        return _tex[name] = id;
    }

    static (int[] Px, int W, int H) FromBitmap(Bitmap src)
    {
        using var bmp = src.Clone(new Rectangle(0, 0, src.Width, src.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
        bmp.UnlockBits(bd);
        return (px, bmp.Width, bmp.Height);
    }

    const float R = 1000, CapFrom = 30, BandBottom = -25;   // raio, início do topo e fim da faixa (graus de elevação)
    const int Seg = 48;

    /// <summary>Desenha a cúpula em volta da câmera (coordenadas OpenGL: Z do jogo invertido). false = sem céu.</summary>
    public bool Draw(Vector3 camGl)
    {
        if (!HasSky) return false;
        _cur = AreaAt(new Vector3(camGl.X, camGl.Y, -camGl.Z)) ?? _cur;
        if (_cur is null) return false;
        int cap = Texture(_cur.Sky[0]), side1 = Texture(_cur.Sky.Length > 1 ? _cur.Sky[1] : ""), side2 = Texture(_cur.Sky.Length > 2 ? _cur.Sky[2] : "");
        if (cap == 0 && side1 == 0 && side2 == 0) return false;

        GL.Disable(EnableCap.DepthTest); GL.DepthMask(false); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.Lighting);
        GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.AlphaTest); GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.Texture2D);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.Color4(1f, 1f, 1f, 1f);
        GL.PushMatrix();
        GL.Translate(camGl.X, camGl.Y, camGl.Z);
        static Vector3 P(float azDeg, float elDeg)
        {
            float az = MathHelper.DegreesToRadians(azDeg), el = MathHelper.DegreesToRadians(elDeg);
            return new Vector3(MathF.Cos(el) * MathF.Cos(az) * R, MathF.Sin(el) * R, MathF.Cos(el) * MathF.Sin(az) * R);
        }
        // lados: cada textura cobre meia volta; v = 0 no alto (início do topo), 1 embaixo
        for (int half = 0; half < 2; half++)
        {
            int t = half == 0 ? side1 : side2;
            if (t == 0) continue;
            GL.BindTexture(TextureTarget.Texture2D, t);
            GL.Begin(PrimitiveType.QuadStrip);
            for (int i = 0; i <= Seg / 2; i++)
            {
                float az = half * 180 + i * 360f / Seg, u = i / (Seg / 2f);
                GL.TexCoord2(u, 0f); GL.Vertex3(P(az, CapFrom));
                GL.TexCoord2(u, 1f); GL.Vertex3(P(az, BandBottom));
            }
            GL.End();
        }
        // topo: projeção de cima (o disco do topo vira o quadrado da textura)
        if (cap != 0)
        {
            GL.BindTexture(TextureTarget.Texture2D, cap);
            float rimR = MathF.Cos(MathHelper.DegreesToRadians(CapFrom)) * R;
            for (int ring = 0; ring < 6; ring++)
            {
                float e0 = CapFrom + (90 - CapFrom) * ring / 6f, e1 = CapFrom + (90 - CapFrom) * (ring + 1) / 6f;
                GL.Begin(PrimitiveType.QuadStrip);
                for (int i = 0; i <= Seg; i++)
                {
                    float az = i * 360f / Seg;
                    var a = P(az, e0); var b = P(az, e1);
                    GL.TexCoord2(0.5f + a.X / rimR * 0.5f, 0.5f + a.Z / rimR * 0.5f); GL.Vertex3(a);
                    GL.TexCoord2(0.5f + b.X / rimR * 0.5f, 0.5f + b.Z / rimR * 0.5f); GL.Vertex3(b);
                }
                GL.End();
            }
        }
        GL.PopMatrix();
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(true); GL.Enable(EnableCap.Fog);
        return true;
    }

    public void Dispose()
    {
        foreach (var t in _tex.Values) if (t != 0) GL.DeleteTexture(t);
        _pck?.Dispose();
    }
}
