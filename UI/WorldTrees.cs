using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Árvores do cenário no Mundo 3D: posições do maps\&lt;mapa&gt;\&lt;mapa&gt;.ecwld (WorldScene) e tronco/galhos gerados pela
/// nossa versão do SpeedTree (SptGrower) a partir do .spt do trees.pck, com a casca do próprio jogo. Cada tipo é gerado
/// uma vez, na primeira vez que aparece perto da câmera (no máximo 2 por quadro); desenha até 900 m.
/// </summary>
internal sealed class WorldTrees : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, int Tex);
    sealed record Kind(Part? Wood, Part? Leaves, float Radius);

    readonly PckArchive? _pck;
    readonly WorldScene? _scene;
    readonly Dictionary<int, Kind?> _kinds = new();
    readonly Dictionary<(int, int), List<int>> _cells = new();   // célula de 256 m → índices em _scene.Trees
    const float Cell = 256, Range = 900;
    static readonly Vector3 Sun = new Vector3(-0.4f, 1f, 0.3f).Normalized();

    public int Count => _scene?.Trees.Count ?? 0;

    public WorldTrees(string? elementDir, string map)
    {
        try
        {
            if (elementDir is null) return;
            var ecwld = Path.Combine(elementDir, "maps", map, map + ".ecwld");
            var pck = Path.Combine(elementDir, "trees.pck");
            if (!File.Exists(ecwld) || !File.Exists(pck)) return;
            _scene = new WorldScene(ecwld);
            _pck = new PckArchive(pck);
        }
        catch (Exception ex) { AppLog.Info($"árvores de {map}: {ex.Message}"); _scene = null; _pck?.Dispose(); _pck = null; return; }
        for (int i = 0; i < _scene.Trees.Count; i++)
        {
            var t = _scene.Trees[i];
            var key = ((int)MathF.Floor(t.X / Cell), (int)MathF.Floor(t.Z / Cell));
            if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new();
            l.Add(i);
        }
    }

    /// <summary>Tronco e galhos do tipo, prontos no OpenGL (null = tipo sem .spt legível).</summary>
    Kind? Build(int type)
    {
        if (_pck is null || _scene is null || type < 0 || type >= _scene.TreeTypes.Count) return null;
        try
        {
            var spt = _scene.TreeTypes[type].Spt.Replace('/', '\\').ToLowerInvariant();
            var e = _pck.Find(spt) ?? _pck.Find(@"trees\badtree.spt");   // o cliente usa a "árvore ruim" quando falta o arquivo
            if (e is null) return null;
            var f = new SptFile(_pck.Read(e));
            var m = SptGrower.Build(f);
            if (m.Index.Count == 0 && m.Leaves.Count == 0) return null;
            var v = new float[m.Pos.Count * 6];
            float radius = 1;
            for (int i = 0; i < m.Pos.Count; i++)
            {
                var p = m.Pos[i];
                radius = MathF.Max(radius, p.Length);
                float lit = 0.30f + 0.22f * MathF.Max(0, Vector3.Dot(m.Normal[i], Sun));   // × 2 no desenho
                byte c = (byte)Math.Clamp(lit * 255, 0, 255);
                v[i * 6] = p.X; v[i * 6 + 1] = p.Y; v[i * 6 + 2] = p.Z;
                v[i * 6 + 3] = BitConverter.Int32BitsToSingle(unchecked((int)(0xFF000000u | (uint)(c << 16) | (uint)(c << 8) | c)));
                v[i * 6 + 4] = m.Uv[i].X; v[i * 6 + 5] = m.Uv[i].Y;
            }
            // casca: mesma pasta do .spt, extensão .dds (como CELTree::Init)
            var bark = Path.Combine(Path.GetDirectoryName(e.Path) ?? "", Path.ChangeExtension(f.BarkTexture, ".dds")).ToLowerInvariant();
            var wood = m.Index.Count == 0 ? null : Upload(v, m.Index.ToArray(), Texture(bark, 256));
            // folhas: dois cartões cruzados por folha, recorte do mapa composto do tipo (.ecwld)
            Part? leaves = null;
            if (m.Leaves.Count > 0 && f.LeafMaps.Count > 0)
            {
                var (lv, li) = LeafCards(f, m, ref radius);
                leaves = Upload(lv, li, Texture(_scene.TreeTypes[type].CompositeMap.Replace('/', '\\').ToLowerInvariant(), 512));
            }
            return new Kind(wood, leaves, radius);
        }
        catch (Exception ex) { AppLog.Info($"árvore {type}: {ex.Message}"); return null; }
    }

    int Texture(string path, int max)
    {
        if (_pck?.Find(path) is not { } te || DdsDecoder.DecodeMax(_pck.Read(te), max) is not { } img) return 0;
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return tex;
    }

    static Part Upload(float[] v, int[] idx, int tex)
    {
        int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, v.Length * 4, v, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        return new Part(vbo, ibo, idx.Length, tex);
    }

    /// <summary>
    /// Cartões das folhas: cada folha sorteia uma textura de folha do .spt; o cartão tem o tamanho dela (unidades da árvore,
    /// já em metros pela escala da árvore), preso pela âncora, com os 4 cantos (s,t) do mapa composto. O runtime vira as
    /// folhas para a câmera; aqui são dois cartões em pé cruzados, com giro sorteado.
    /// </summary>
    internal static (float[] V, int[] I) LeafCards(SptFile f, SptMesh m, ref float radius)
    {
        var v = new float[m.Leaves.Count * 8 * 6]; var idx = new int[m.Leaves.Count * 12];
        var rng = new SptRandom(f.Seed + 7);
        int n = 0, q = 0;
        foreach (var (pos, _) in m.Leaves)
        {
            var map = f.LeafMaps[Math.Min((int)(rng.Uniform() * f.LeafMaps.Count), f.LeafMaps.Count - 1)];
            float w = map.Size.X * m.Scale, h = map.Size.Y * m.Scale;
            float s0 = MathF.Min(MathF.Min(map.Uv[0], map.Uv[2]), MathF.Min(map.Uv[4], map.Uv[6])), s1 = MathF.Max(MathF.Max(map.Uv[0], map.Uv[2]), MathF.Max(map.Uv[4], map.Uv[6]));
            float t0 = MathF.Min(MathF.Min(map.Uv[1], map.Uv[3]), MathF.Min(map.Uv[5], map.Uv[7])), t1 = MathF.Max(MathF.Max(map.Uv[1], map.Uv[3]), MathF.Max(map.Uv[5], map.Uv[7]));
            float yaw = rng.Range(0, MathF.PI);
            byte c = (byte)Math.Clamp((0.40f + rng.Range(-0.06f, 0.06f)) * 255, 0, 255);
            float col = BitConverter.Int32BitsToSingle(unchecked((int)(0xFF000000u | (uint)(c << 16) | (uint)(c << 8) | c)));
            for (int card = 0; card < 2; card++)
            {
                float a = yaw + card * MathF.PI / 2;
                var right = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                int first = n;
                for (int k = 0; k < 4; k++)
                {
                    float su = map.Uv[k * 2], tv = map.Uv[k * 2 + 1];
                    float ax = s1 > s0 ? (su - s0) / (s1 - s0) : 0, by = t1 > t0 ? (tv - t0) / (t1 - t0) : 0;   // by = 0 no alto da imagem
                    var p = pos + right * ((ax - map.Origin.X) * w) + Vector3.UnitY * ((map.Origin.Y - by) * h);
                    radius = MathF.Max(radius, p.Length);
                    int o = n * 6;
                    v[o] = p.X; v[o + 1] = p.Y; v[o + 2] = p.Z; v[o + 3] = col; v[o + 4] = su; v[o + 5] = tv;
                    n++;
                }
                idx[q++] = first; idx[q++] = first + 1; idx[q++] = first + 2;
                idx[q++] = first; idx[q++] = first + 2; idx[q++] = first + 3;
            }
        }
        return (v, idx);
    }

    /// <summary>Desenha as árvores perto e na frente da câmera (cam e forward no espaço do OpenGL: z = −z do jogo).</summary>
    public void Draw(Vector3 cam, Vector3 forward)
    {
        if (_scene is null) return;
        int built = 0;
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, 0x8570);   // COMBINE: textura × cor × 2
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8571, 0x2100);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8580, 0x1702);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8581, 0x8577);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 2f);
        float gx = cam.X, gz = -cam.Z;
        int c0 = (int)MathF.Floor((gx - Range) / Cell), c1 = (int)MathF.Floor((gx + Range) / Cell);
        int r0 = (int)MathF.Floor((gz - Range) / Cell), r1 = (int)MathF.Floor((gz + Range) / Cell);
        for (int cx = c0; cx <= c1; cx++)
            for (int cz = r0; cz <= r1; cz++)
            {
                if (!_cells.TryGetValue((cx, cz), out var list)) continue;
                foreach (int i in list)
                {
                    var t = _scene.Trees[i];
                    var at = new Vector3(t.X, t.Y, -t.Z);
                    var d = at - cam;
                    float dist = d.Length;
                    if (dist > Range || Vector3.Dot(d, forward) < -40) continue;
                    if (!_kinds.TryGetValue(t.Type, out var k))
                    {
                        if (built >= 2) continue;   // gera aos poucos para não travar a câmera
                        _kinds[t.Type] = k = Build(t.Type); built++;
                        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
                    }
                    if (k is null || k.Radius / Math.Max(dist, 1) < 0.003f) continue;
                    GL.PushMatrix();
                    GL.Translate(at);
                    DrawPart(k.Wood, false);
                    DrawPart(k.Leaves, true);
                    GL.PopMatrix();
                }
            }
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 1f);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.ColorArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Texture2D);
    }

    static void DrawPart(Part? p, bool cutout)
    {
        if (p is null) return;
        GL.BindBuffer(BufferTarget.ArrayBuffer, p.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 24, 0);
        GL.ColorPointer(0x80E1, ColorPointerType.UnsignedByte, 24, 12);   // GL_BGRA
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 24, 16);
        if (p.Tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, p.Tex); } else GL.Disable(EnableCap.Texture2D);
        if (cutout) { GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.33f); }
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, p.Ibo);
        GL.DrawElements(PrimitiveType.Triangles, p.Count, DrawElementsType.UnsignedInt, 0);
        if (cutout) GL.Disable(EnableCap.AlphaTest);
    }

    public void Dispose()
    {
        foreach (var k in _kinds.Values)
            foreach (var p in new[] { k?.Wood, k?.Leaves })
                if (p is not null) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); if (p.Tex != 0) GL.DeleteTexture(p.Tex); }
        _kinds.Clear();
        _pck?.Dispose();
    }
}
