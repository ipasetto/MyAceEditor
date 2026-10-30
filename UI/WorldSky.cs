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
    readonly List<WorldScene.BoxArea> _areas = new(), _all = new();
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
                _all.AddRange(scene.Areas);
                _areas.AddRange(scene.Areas.Where(a => a.Sky.Any(s => s.Length > 0)));
            }
        }
        catch (Exception ex) { AppLog.Error("céu do Mundo 3D", ex); }
    }

    public bool HasSky => _areas.Count > 0 && _pck is not null;

    // área da câmera: a caixa (não padrão) que a contém; senão a área padrão mais perto (no mundo há uma por bloco de 1 km)
    WorldScene.BoxArea? AreaAt(Vector3 game) => AreaAt(_areas, game);

    static WorldScene.BoxArea? AreaAt(List<WorldScene.BoxArea> areas, Vector3 game)
    {
        foreach (var a in areas.Where(a => !a.Default))
        {
            var d = new Vector3(game.X - a.Pos[0], game.Y - a.Pos[1], game.Z - a.Pos[2]);
            if (MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisX[0], a.AxisX[1], a.AxisX[2]))) <= a.Ext[0]
                && MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisY[0], a.AxisY[1], a.AxisY[2]))) <= a.Ext[1]
                && MathF.Abs(Vector3.Dot(d, new Vector3(a.AxisZ[0], a.AxisZ[1], a.AxisZ[2]))) <= a.Ext[2]) return a;
        }
        return areas.Where(a => a.Default).MinBy(a => (a.Pos[0] - game.X) * (a.Pos[0] - game.X) + (a.Pos[2] - game.Z) * (a.Pos[2] - game.Z))
            ?? areas.FirstOrDefault();
    }

    float _fogStart = -1, _fogEnd;
    Vector3 _fogColor;

    /// <summary>
    /// Neblina de dia da área da câmera (início, fim em metros e cor), indo aos poucos de uma área para outra como no jogo
    /// (CECScnBoxArea: a troca leva dwTransTime; aqui ~1 s). null = mapa sem áreas.
    /// </summary>
    public (float Start, float End, Vector3 Color)? Fog(Vector3 camGl)
    {
        if (AreaAt(_all, new Vector3(camGl.X, camGl.Y, -camGl.Z)) is not { } a || a.FogEnd <= 0) return null;
        var col = new Vector3((a.FogColor >> 16 & 255) / 255f, (a.FogColor >> 8 & 255) / 255f, (a.FogColor & 255) / 255f);
        float end = MathF.Max(a.FogEnd, a.FogStart + 1);
        if (_fogStart < 0) (_fogStart, _fogEnd, _fogColor) = (a.FogStart, end, col);
        const float k = 0.15f;
        _fogStart += (a.FogStart - _fogStart) * k; _fogEnd += (end - _fogEnd) * k; _fogColor += (col - _fogColor) * k;
        return (_fogStart, _fogEnd, _fogColor);
    }

    /// <summary>Direção da luz do sol do mapa (área padrão no centro, coordenadas OpenGL, apontando PARA o sol).</summary>
    public Vector3? SunDir
    {
        get
        {
            if (AreaAt(_all, Vector3.Zero) is not { LightDir: var d } || d.Length < 3) return null;
            var v = -new Vector3(d[0], d[1], -d[2]);
            return v.LengthSquared > 1e-6f ? v.Normalized() : null;
        }
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

    // Geometria do A3DSkySphere (Angelica 2.2, a mesma API do cliente 1.5.3; só estudo): raio 800, 6 segmentos.
    //   lados: cilindro de raio R com 5 alturas h·{1, .5, .167, 0, -.27} (h = R·π/4); a de cima é transparente (funde no
    //     topo). Cada textura (frente e trás, a de trás girada 180°) cobre meia volta em dois quartos: a metade de CIMA da
    //     imagem é o quarto de 90° a 180°, a metade de BAIXO (de ponta-cabeça) o de 0° a 90°; u vai de 1 a 0 em cada quarto.
    //   topo: calota de 17,5° baixada até a borda ficar na altura da câmera; textura plana (x/r, z/r) repetida 8×.
    const float R = 800, H = R * 0.7854f, Pitch = 17.5f * MathF.PI / 180, Tile = 8;
    const int Seg = 6;
    static readonly float[] Heights = { 1f, 0.5f, 0.167f, 0f, -0.27f };

    /// <summary>Desenha o céu em volta da câmera (coordenadas OpenGL: Z do jogo invertido). false = sem céu.</summary>
    public bool Draw(Vector3 camGl)
    {
        if (!HasSky) return false;
        _cur = AreaAt(new Vector3(camGl.X, camGl.Y, -camGl.Z)) ?? _cur;
        if (_cur is null) return false;
        int cap = Texture(_cur.Sky[0]), front = Texture(_cur.Sky.Length > 1 ? _cur.Sky[1] : ""), back = Texture(_cur.Sky.Length > 2 ? _cur.Sky[2] : "");
        if (cap == 0 && front == 0 && back == 0) return false;

        GL.Disable(EnableCap.DepthTest); GL.DepthMask(false); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.Lighting);
        GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.AlphaTest);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Texture2D);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.PushMatrix();
        GL.Translate(camGl.X, camGl.Y, camGl.Z);
        static void V(float x, float y, float z) => GL.Vertex3(x, y, -z);   // ponto do jogo -> OpenGL

        // topo primeiro (os lados se misturam por cima dele)
        if (cap != 0)
        {
            GL.BindTexture(TextureTarget.Texture2D, cap);
            Wrap(TextureWrapMode.Repeat);
            GL.Color4(1f, 1f, 1f, 1f);
            float r = R * MathF.Sin(Pitch), drop = R * MathF.Cos(Pitch);
            void C(float pitch, float az)
            {
                float x = R * MathF.Cos(az) * MathF.Cos(pitch), z = R * MathF.Sin(az) * MathF.Cos(pitch);
                GL.TexCoord2((x / r + 1) * 0.5f * Tile, (z / r + 1) * 0.5f * Tile);
                V(x, R * MathF.Sin(pitch) - drop, z);
            }
            int n = 4 * Seg;
            for (int ring = 0; ring < Seg; ring++)
            {
                float p0 = MathF.PI / 2 - ring * Pitch / Seg, p1 = MathF.PI / 2 - (ring + 1) * Pitch / Seg;
                GL.Begin(PrimitiveType.QuadStrip);
                for (int i = 0; i <= n; i++) { float az = MathF.PI * 2 * i / n; C(p0, az); C(p1, az); }
                GL.End();
            }
        }
        // lados: frente (z > 0 no jogo) e trás (girada 180°)
        for (int half = 0; half < 2; half++)
        {
            int t = half == 0 ? front : back;
            if (t == 0) continue;
            GL.BindTexture(TextureTarget.Texture2D, t);
            Wrap(TextureWrapMode.ClampToEdge);
            float s = half == 0 ? 1 : -1;
            for (int quarter = 0; quarter < 2; quarter++)   // 0: de 90° a 180° (metade de cima), 1: de 0° a 90° (metade de baixo)
                for (int j = 0; j < Heights.Length - 1; j++)
                {
                    GL.Begin(PrimitiveType.QuadStrip);
                    for (int i = 0; i <= Seg; i++)
                    {
                        float a = (quarter == 0 ? MathF.PI / 2 : 0) + MathF.PI / 2 * i / Seg, u = 1f - (float)i / Seg;
                        for (int k = j; k <= j + 1; k++)
                        {
                            float y = H * Heights[k];
                            float v = k == Heights.Length - 1 ? 0.5f : quarter == 0 ? (1 - y / H) * 0.45f + 0.05f : (0.5f + y / H * 0.5f) * 0.9f + 0.05f;
                            GL.Color4(1f, 1f, 1f, k == 0 ? 0f : 1f);
                            GL.TexCoord2(u, v);
                            V(s * R * MathF.Cos(a), y, s * R * MathF.Sin(a));
                        }
                    }
                    GL.End();
                }
        }
        GL.PopMatrix();
        GL.Color4(1f, 1f, 1f, 1f);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(true); GL.Enable(EnableCap.Fog);
        return true;
    }

    static void Wrap(TextureWrapMode m)
    {
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)m);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)m);
    }

    public void Dispose()
    {
        foreach (var t in _tex.Values) if (t != 0) GL.DeleteTexture(t);
        _pck?.Dispose();
    }
}
