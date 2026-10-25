using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Construções do cenário no Mundo 3D: litmodels\&lt;mapa&gt;\&lt;pedaço&gt;\*.bmd (BmdModel) dos pedaços de 1 km em volta da
/// câmera, com as texturas do building.pck e a luz que já vem nos vértices (cor ×2, como o jogo faz com a luz embutida).
/// Carrega poucos arquivos por quadro; pedaços longe são descarregados.
/// </summary>
internal sealed class WorldBuildings : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, int Tex);
    sealed record Placed(Matrix4 World, List<Part> Parts);

    readonly PckArchive? _lit, _tex;
    readonly string _map;
    readonly Dictionary<int, List<PckEntry>> _byPiece = new();
    readonly Dictionary<int, List<Placed>> _loaded = new();
    readonly Dictionary<int, int> _progress = new();   // pedaço → quantos arquivos já carregados
    readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);

    public WorldBuildings(string? elementDir, string map)
    {
        _map = map.ToLowerInvariant();
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

    /// <summary>Carrega (aos poucos) os pedaços pedidos e solta os outros. Devolve true se ainda falta carregar.</summary>
    public bool Update(IReadOnlyCollection<int> pieces, int budget)
    {
        if (_lit is null) return false;
        foreach (var p in _loaded.Keys.Where(k => !pieces.Contains(k)).ToList()) Unload(p);
        bool pending = false;
        foreach (var p in pieces)
        {
            if (!_byPiece.TryGetValue(p, out var files)) continue;
            if (!_loaded.TryGetValue(p, out var list)) _loaded[p] = list = new();
            int done = _progress.GetValueOrDefault(p);
            while (done < files.Count && budget-- > 0) { Load(files[done++], list); }
            _progress[p] = done;
            if (done < files.Count) pending = true;
        }
        return pending;
    }

    void Load(PckEntry e, List<Placed> into)
    {
        if (BmdModel.Read(_lit!.Read(e)) is not { } m || m.Meshes.Count == 0) return;
        var dir = new Vector3(m.Dir[0], m.Dir[1], m.Dir[2]); var up = new Vector3(m.Up[0], m.Up[1], m.Up[2]);
        if (dir.LengthSquared < 1e-6f || up.LengthSquared < 1e-6f) { dir = Vector3.UnitZ; up = Vector3.UnitY; }
        var right = Vector3.Cross(up, dir);
        float s = m.Scale is > 0 and < 1000 ? m.Scale : 1;
        // jogo: local × (direita, cima, frente)·escala + posição; depois espelha Z para o OpenGL
        var world = new Matrix4(new Vector4(right * s, 0), new Vector4(up * s, 0), new Vector4(dir * s, 0), new Vector4(m.Pos[0], m.Pos[1], m.Pos[2], 1))
            * Matrix4.CreateScale(1, 1, -1);
        var parts = new List<Part>();
        foreach (var mesh in m.Meshes)
        {
            int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length, mesh.Vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * 2, mesh.Indices, BufferUsageHint.StaticDraw);
            parts.Add(new Part(vbo, ibo, mesh.Indices.Length, Texture(mesh.Texture)));
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        into.Add(new Placed(world, parts));
    }

    int Texture(string path)
    {
        if (_textures.TryGetValue(path, out var id)) return id;
        id = 0;
        if (_tex?.Find(path.ToLowerInvariant()) is { } e && DdsDecoder.Decode(_tex.Read(e)) is { } img)
        {
            id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }
        return _textures[path] = id;
    }

    void Unload(int piece)
    {
        if (_loaded.Remove(piece, out var list))
            foreach (var p in list) foreach (var part in p.Parts) { GL.DeleteBuffer(part.Vbo); GL.DeleteBuffer(part.Ibo); }
        _progress.Remove(piece);
    }

    public void Draw()
    {
        if (_loaded.Count == 0) return;
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.4f);
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
                GL.PushMatrix();
                var w = p.World; GL.MultMatrix(ref w);
                foreach (var part in p.Parts)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, part.Vbo);
                    GL.VertexPointer(3, VertexPointerType.Float, 24, 0);
                    GL.ColorPointer(0x80E1, ColorPointerType.UnsignedByte, 24, 12);   // GL_BGRA
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, 24, 16);
                    if (part.Tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, part.Tex); }
                    else GL.Disable(EnableCap.Texture2D);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, part.Ibo);
                    GL.DrawElements(PrimitiveType.Triangles, part.Count, DrawElementsType.UnsignedShort, 0);
                }
                GL.PopMatrix();
            }
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
