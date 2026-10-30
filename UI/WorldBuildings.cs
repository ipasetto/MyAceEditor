using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Construções do cenário no Mundo 3D: litmodels\&lt;mapa&gt;\&lt;pedaço&gt;\*.bmd (BmdModel) dos pedaços de 1 km em volta da
/// câmera, com as texturas do building.pck e a luz que já vem nos vértices (cor ×2). Leitura e decodificação em segundo
/// plano (WorldLoader), em lotes; só desenha o que está a até 1,1 km, na frente da câmera e grande o bastante para aparecer.
/// </summary>
internal sealed class WorldBuildings : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, string Tex);
    sealed record Placed(Matrix4 World, List<Part> Parts, Vector3 Center, float Radius);
    sealed record Cpu(Matrix4 World, List<BmdModel.Mesh> Meshes, Vector3 Center, float Radius);

    readonly PckArchive? _lit, _tex;
    readonly WorldLoader _load;
    readonly string _map;
    readonly Dictionary<int, List<PckEntry>> _byPiece = new();
    readonly Dictionary<int, List<Placed>> _loaded = new();
    readonly HashSet<int> _wanted = new(), _requested = new();
    readonly Dictionary<int, int> _gen = new();   // pedaço → geração (lote de um pedido antigo é descartado)
    readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<int> _cutout = new();   // texturas com recorte de verdade (o resto desenha sem teste de alfa)
    readonly HashSet<string> _texSent = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Texturas de construção que não foram achadas no pacote (diagnóstico do --world-shot).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentBag<string> MissingTextures = new();
    const int Batch = 16;
    static readonly string HideTex = Environment.GetEnvironmentVariable("MYACE_HIDETEX") ?? "";   // teste: esconde malhas com esta textura
    static readonly string CullMode = Environment.GetEnvironmentVariable("MYACE_BCULL") ?? "cw";   // teste: cw | ccw | off

    public WorldBuildings(string? elementDir, string map, WorldLoader load)
    {
        _map = map.ToLowerInvariant(); _load = load;
        try
        {
            if (elementDir is null) return;
            if (File.Exists(Path.Combine(elementDir, "litmodels.pck"))) _lit = new PckArchive(Path.Combine(elementDir, "litmodels.pck"));
            if (File.Exists(Path.Combine(elementDir, "building.pck"))) _tex = new PckArchive(Path.Combine(elementDir, "building.pck"));
        }
        catch { _lit = null; }
        if (_lit is null) return;
        var prefix = $@"litmodels\{_map}\";
        foreach (var e in _lit.Entries)
        {
            if (!e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = e.Path.AsSpan(prefix.Length);
            int slash = rest.IndexOf('\\');
            if (slash > 0 && int.TryParse(rest[..slash], out var piece))
            {
                if (!_byPiece.TryGetValue(piece, out var l)) _byPiece[piece] = l = new();
                l.Add(e);
            }
        }
    }

    /// <summary>Pede os pedaços em volta da câmera e solta os outros.</summary>
    public void Update(IReadOnlyCollection<int> pieces)
    {
        if (_lit is null) return;
        foreach (var p in _loaded.Keys.Where(k => !pieces.Contains(k)).ToList()) Unload(p);
        _wanted.Clear(); _wanted.UnionWith(pieces);
        foreach (var p in pieces)
        {
            if (!_byPiece.TryGetValue(p, out var files) || !_requested.Add(p)) continue;
            if (!_loaded.ContainsKey(p)) _loaded[p] = new();
            int gen = _gen[p] = _gen.GetValueOrDefault(p) + 1;
            for (int i = 0; i < files.Count; i += Batch)
            {
                var batch = files.Skip(i).Take(Batch).ToList();
                int piece = p;
                _load.Work(() => Parse(batch), r => Arrive(piece, gen, r), urgent: true);
            }
        }
    }

    (List<Cpu> Models, Dictionary<string, (int[] Px, int W, int H)?> Textures) Parse(List<PckEntry> files)
    {
        var models = new List<Cpu>();
        var tex = new Dictionary<string, (int[], int, int)?>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in files)
        {
            byte[] d;
            try { lock (_lit!) d = _lit.Read(e); } catch { continue; }
            if (BmdModel.Read(d) is not { } m || m.Meshes.Count == 0) continue;
            var dir = new Vector3(m.Dir[0], m.Dir[1], m.Dir[2]); var up = new Vector3(m.Up[0], m.Up[1], m.Up[2]);
            if (dir.LengthSquared < 1e-6f || up.LengthSquared < 1e-6f) { dir = Vector3.UnitZ; up = Vector3.UnitY; }
            var right = Vector3.Cross(up, dir);
            float s = m.Scale is > 0 and < 1000 ? m.Scale : 1;
            // jogo: local × (direita, cima, frente)·escala + posição; depois espelha Z para o OpenGL
            var world = new Matrix4(new Vector4(right * s, 0), new Vector4(up * s, 0), new Vector4(dir * s, 0), new Vector4(m.Pos[0], m.Pos[1], m.Pos[2], 1))
                * Matrix4.CreateScale(1, 1, -1);
            // esfera envolvente (para cortar o que está longe ou atrás)
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var mesh in m.Meshes)
                for (int o = 0; o + 12 <= mesh.Vertices.Length; o += 24)
                {
                    var v = new Vector3(BitConverter.ToSingle(mesh.Vertices, o), BitConverter.ToSingle(mesh.Vertices, o + 4), BitConverter.ToSingle(mesh.Vertices, o + 8));
                    min = Vector3.ComponentMin(min, v); max = Vector3.ComponentMax(max, v);
                }
            var center = Vector3.TransformPosition((min + max) / 2, world);
            models.Add(new Cpu(world, m.Meshes, center, (max - min).Length / 2 * s));
            foreach (var mesh in m.Meshes)
            {
                bool need; lock (_texSent) need = _texSent.Add(mesh.Texture);
                if (!need || _tex is null) continue;
                byte[]? td; lock (_tex) td = _tex.Find(mesh.Texture.ToLowerInvariant()) is { } te ? _tex.Read(te) : null;
                try { tex[mesh.Texture] = td is null ? null : mesh.Texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? FromTga(td) : DdsDecoder.DecodeMax(td, 512); } catch { tex[mesh.Texture] = null; }
                if (tex[mesh.Texture] is null) MissingTextures.Add(mesh.Texture);
            }
        }
        return (models, tex);
    }

    void Arrive(int piece, int gen, (List<Cpu> Models, Dictionary<string, (int[] Px, int W, int H)?> Textures) r)
    {
        foreach (var (name, img) in r.Textures)
        {
            int id = img is { } i ? UploadTexture(i) : 0;
            _textures[name] = id;
            if (id != 0 && DdsDecoder.IsCutout(img!.Value.Px)) _cutout.Add(id);
        }
        if (!_wanted.Contains(piece) || _gen.GetValueOrDefault(piece) != gen || !_loaded.TryGetValue(piece, out var list)) return;   // a câmera já saiu de lá
        foreach (var c in r.Models)
        {
            var parts = new List<Part>();
            foreach (var mesh in c.Meshes)
            {
                int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length, mesh.Vertices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * 2, mesh.Indices, BufferUsageHint.StaticDraw);
                parts.Add(new Part(vbo, ibo, mesh.Indices.Length, mesh.Texture));
            }
            list.Add(new Placed(c.World, parts, c.Center, c.Radius));
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    // textura .tga (algumas construções e as superfícies .sdr.tga): mesmo leitor dos ícones, em BGRA
    static (int[] Px, int W, int H)? FromTga(byte[] d)
    {
        using var bmp = IconLoader.DecodeTga(d);
        if (bmp is null) return null;
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[bmp.Width * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
        bmp.UnlockBits(bd);
        return (px, bmp.Width, bmp.Height);
    }

    static int UploadTexture((int[] Px, int W, int H) img)
    {
        int id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.W, img.H, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Px);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return id;
    }

    void Unload(int piece)
    {
        if (_loaded.Remove(piece, out var list))
            foreach (var p in list) foreach (var part in p.Parts) { GL.DeleteBuffer(part.Vbo); GL.DeleteBuffer(part.Ibo); }
        _requested.Remove(piece);
        // texturas que nenhuma peça carregada usa saem da placa (andar pelo mapa acumulava todas)
        if (_textures.Count > 400)
        {
            var used = new HashSet<string>(_loaded.Values.SelectMany(l => l).SelectMany(p => p.Parts).Select(x => x.Tex));
            foreach (var name in _textures.Keys.Where(k => !used.Contains(k)).ToList())
            {
                int id = _textures[name];
                if (id != 0) { GL.DeleteTexture(id); _cutout.Remove(id); }
                _textures.Remove(name);
                lock (_texSent) _texSent.Remove(name);   // volta a ser pedida quando precisar
            }
        }
    }

    /// <summary>Desenha o que está perto e na frente da câmera (cam e forward no espaço do OpenGL).</summary>
    public void Draw(Vector3 cam, Vector3 forward)
    {
        if (_loaded.Count == 0) return;
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.AlphaFunc(AlphaFunction.Greater, 0.4f);
        // textura × cor do vértice × 2 (luz embutida)
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, 0x8570);   // COMBINE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8571, 0x2100);          // RGB = MODULATE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8580, 0x1702);          //   textura
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8581, 0x8577);          //   × cor
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 2f);              // RGB_SCALE = 2
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8572, 0x1E01);          // ALFA = REPLACE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8588, 0x1702);          //   da textura
        foreach (var list in _loaded.Values)
            foreach (var p in list)
            {
                var d = p.Center - cam;
                float dist = d.Length;
                if (dist - p.Radius > 1100 || Vector3.Dot(d, forward) < -p.Radius || p.Radius / Math.Max(dist, 1) < 0.004f) continue;
                GL.PushMatrix();
                var w = p.World; GL.MultMatrix(ref w);
                foreach (var part in p.Parts)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, part.Vbo);
                    GL.VertexPointer(3, VertexPointerType.Float, 24, 0);
                    GL.ColorPointer(0x80E1, ColorPointerType.UnsignedByte, 24, 12);   // GL_BGRA
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, 24, 16);
                    if (HideTex.Length > 0 && part.Tex.Contains(HideTex, StringComparison.OrdinalIgnoreCase)) continue;   // teste
                    int tex = _textures.GetValueOrDefault(part.Tex);
                    // superfície de shader (.sdr: escudo do tempo na a33, poça de sangue na a17) sem a textura: no jogo é
                    // efeito translúcido; desenhada opaca na cor do vértice virava domo/faixa sólida — fica de fora
                    if (tex == 0 && part.Tex.Contains(".sdr", StringComparison.OrdinalIgnoreCase)) continue;
                    // como o jogo (Direct3D descarta as faces de trás): planos de fundo vistos por trás somem (a08: um
                    // quadro de 51×25 m bege cobria a parede da sala). Recorte (folhas, grades) continua dos dois lados.
                    if (_cutout.Contains(tex) || CullMode == "off") GL.Disable(EnableCap.CullFace);
                    else { GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Back); GL.FrontFace(CullMode == "ccw" ? FrontFaceDirection.Ccw : FrontFaceDirection.Cw); }
                    if (_cutout.Contains(tex)) GL.Enable(EnableCap.AlphaTest); else GL.Disable(EnableCap.AlphaTest);
                    if (tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, tex); }
                    else GL.Disable(EnableCap.Texture2D);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, part.Ibo);
                    GL.DrawElements(PrimitiveType.Triangles, part.Count, DrawElementsType.UnsignedShort, 0);
                }
                GL.PopMatrix();
            }
        GL.Disable(EnableCap.CullFace); GL.FrontFace(FrontFaceDirection.Ccw);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 1f);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.ColorArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.AlphaTest); GL.Disable(EnableCap.Texture2D);
    }

    public void Dispose()
    {
        foreach (var p in _loaded.Keys.ToList()) Unload(p);
        foreach (var t in _textures.Values) if (t != 0) GL.DeleteTexture(t);
        _lit?.Dispose(); _tex?.Dispose();
    }
}
