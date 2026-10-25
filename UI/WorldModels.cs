using System.Text.RegularExpressions;
using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Modelos parados dos monstros, NPCs e minas no Mundo 3D. Caminho do jogo: file_model do elements (.ecm, texto) →
/// SkinModelPath (.SMD) → o .ski de mesmo nome na pasta (senão o primeiro .ski dela) → malhas na pose de ligação,
/// texturas .dds da pasta (ou de "textures\"). Tudo do models.pck, carregado aos poucos e guardado por caminho.
/// </summary>
internal sealed class WorldModels : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, int Tex);
    sealed record Model(List<Part> Parts, float Top);

    readonly PckArchive? _pck;
    readonly Dictionary<string, Model?> _models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _textures = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, PckEntry>? _ddsByName;

    public WorldModels(string? elementDir)
    {
        try { if (elementDir is not null && File.Exists(Path.Combine(elementDir, "models.pck"))) _pck = new PckArchive(Path.Combine(elementDir, "models.pck")); } catch { _pck = null; }
    }

    public bool Available => _pck is not null;

    /// <summary>Altura do topo do modelo (para o nome ficar em cima); null se ainda não carregado ou sem modelo.</summary>
    public float? Top(string ecm) => _models.TryGetValue(ecm, out var m) && m is not null ? m.Top : null;

    public bool Loaded(string ecm) => _models.ContainsKey(ecm);

    /// <summary>Carrega (uma vez) o modelo do .ecm. Precisa do contexto GL atual.</summary>
    public void Load(string ecm)
    {
        if (_models.ContainsKey(ecm)) return;
        _models[ecm] = null;
        if (_pck is null) return;
        try
        {
            var ecmEntry = _pck.Find(ecm.ToLowerInvariant());
            if (ecmEntry is null) return;
            var text = System.Text.Encoding.GetEncoding(936).GetString(_pck.Read(ecmEntry));
            var smd = Regex.Match(text, @"SkinModelPath:\s*(\S[^\r\n]*)").Groups[1].Value.Trim().ToLowerInvariant();
            if (smd.Length == 0) return;
            var dir = Path.GetDirectoryName(smd) ?? "";
            var skiEntry = _pck.Find(Path.ChangeExtension(smd, ".ski"))
                ?? _pck.Entries.FirstOrDefault(e => e.Path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".ski", StringComparison.OrdinalIgnoreCase)
                    && Path.GetDirectoryName(e.Path)!.Equals(dir, StringComparison.OrdinalIgnoreCase));
            if (skiEntry is null || SkiModel.Read(_pck.Read(skiEntry)) is not { } ski) return;
            var parts = new List<Part>();
            float top = 0;
            foreach (var m in ski.Meshes)
            {
                if (m.Indices.Length == 0) continue;
                for (int i = 1; i < m.Vertices.Length; i += 8) top = Math.Max(top, m.Vertices[i]);
                int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, m.Vertices.Length * 4, m.Vertices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, m.Indices.Length * 2, m.Indices, BufferUsageHint.StaticDraw);
                int tex = m.Texture >= 0 && m.Texture < ski.Textures.Count ? Texture(dir, ski.Textures[m.Texture]) : 0;
                parts.Add(new Part(vbo, ibo, m.Indices.Length, tex));
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            if (parts.Count > 0) _models[ecm] = new Model(parts, top);
        }
        catch { _models[ecm] = null; }
    }

    int Texture(string dir, string name)
    {
        var key = dir + "|" + name;
        if (_textures.TryGetValue(key, out var id)) return id;
        id = 0;
        var file = Path.GetFileName(name).ToLowerInvariant();
        var e = _pck!.Find(Path.Combine(dir, file)) ?? _pck.Find(Path.Combine(dir, "textures", file));
        if (e is null)
        {
            _ddsByName ??= _pck.Entries.Where(x => x.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            _ddsByName.TryGetValue(file, out e);
        }
        if (e is not null && DdsDecoder.Decode(_pck.Read(e)) is { } img)
        {
            id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
        }
        return _textures[key] = id;
    }

    /// <summary>Desenha o modelo em pos (espaço do jogo), virado para yaw (graus, 0 = +Z/norte).</summary>
    public bool Draw(string ecm, Vector3 pos, float yaw)
    {
        if (!_models.TryGetValue(ecm, out var m) || m is null) return false;
        GL.PushMatrix();
        GL.Translate(pos.X, pos.Y, -pos.Z);   // jogo → GL (Z espelhado)
        GL.Scale(1f, 1f, -1f);
        GL.Rotate(yaw, 0, 1, 0);
        foreach (var p in m.Parts)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, p.Vbo);
            GL.VertexPointer(3, VertexPointerType.Float, 32, 0);
            GL.NormalPointer(NormalPointerType.Float, 32, 12);
            GL.TexCoordPointer(2, TexCoordPointerType.Float, 32, 24);
            if (p.Tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, p.Tex); }
            else GL.Disable(EnableCap.Texture2D);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, p.Ibo);
            GL.DrawElements(PrimitiveType.Triangles, p.Count, DrawElementsType.UnsignedShort, 0);
        }
        GL.PopMatrix();
        return true;
    }

    public void Dispose()
    {
        foreach (var m in _models.Values) if (m is not null) foreach (var p in m.Parts) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); }
        foreach (var t in _textures.Values) if (t != 0) GL.DeleteTexture(t);
        _pck?.Dispose();
    }
}
