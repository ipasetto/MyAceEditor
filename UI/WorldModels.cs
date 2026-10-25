using System.Text.RegularExpressions;
using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Modelos parados dos monstros, NPCs e coletáveis no Mundo 3D. Caminho do jogo: file_model do elements (.ecm, texto) →
/// SkinModelPath (.SMD) → o .ski de mesmo nome na pasta (senão o primeiro .ski dela) → malhas na pose de ligação,
/// texturas .dds da pasta (ou de "textures\"). Lidos e decodificados em segundo plano (WorldLoader); aqui só sobem à GPU.
/// </summary>
internal sealed class WorldModels : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, int Tex);
    sealed record Model(List<Part> Parts, float Top);
    sealed record Cpu(SkiModel Ski, List<(int[] Px, int W, int H)?> Textures);

    readonly PckArchive? _pck;
    readonly WorldLoader _load;
    readonly Dictionary<string, Model?> _models = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _waiting = new(StringComparer.OrdinalIgnoreCase);
    readonly List<int> _textures = new();
    readonly HashSet<int> _cutout = new();   // alfa de recorte (cabelo, folhas); o resto sem teste de alfa
    Dictionary<string, PckEntry>? _ddsByName;

    public WorldModels(string? elementDir, WorldLoader load)
    {
        _load = load;
        try { if (elementDir is not null && File.Exists(Path.Combine(elementDir, "models.pck"))) _pck = new PckArchive(Path.Combine(elementDir, "models.pck")); } catch { _pck = null; }
    }

    /// <summary>Altura do topo do modelo (para o nome ficar em cima); null se ainda não carregado ou sem modelo.</summary>
    public float? Top(string ecm) => _models.TryGetValue(ecm, out var m) && m is not null ? m.Top : null;

    /// <summary>Pede o modelo (uma vez); fica pronto alguns quadros depois.</summary>
    public void Request(string ecm)
    {
        if (_pck is null || _models.ContainsKey(ecm) || !_waiting.Add(ecm)) return;
        _load.Work(() =>
        {
            (SkiModel, string)? s;
            try { lock (_pck) s = ResolveSki(_pck, ecm); } catch { s = null; }
            if (s is not var (ski, dir)) return null;
            var tex = new List<(int[], int, int)?>();
            foreach (var name in ski.Textures)
            {
                byte[]? d;
                lock (_pck) d = FindTexture(_pck, dir, name, ref _ddsByName) is { } e ? _pck.Read(e) : null;
                try { tex.Add(d is null ? null : DdsDecoder.Decode(d)); } catch { tex.Add(null); }
            }
            return new Cpu(ski, tex);
        }, cpu => { _waiting.Remove(ecm); _models[ecm] = cpu is null ? null : Upload(cpu); }, urgent: true);
    }

    Model? Upload(Cpu cpu)
    {
        var texIds = cpu.Textures.Select(t =>
        {
            if (t is not { } img) return 0;
            int id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.W, img.H, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Px);
            _textures.Add(id);
            if (DdsDecoder.IsCutout(img.Px)) _cutout.Add(id);
            return id;
        }).ToList();
        var parts = new List<Part>();
        float top = 0;
        foreach (var m in cpu.Ski.Meshes)
        {
            if (m.Indices.Length == 0) continue;
            for (int i = 1; i < m.Vertices.Length; i += 8) top = Math.Max(top, m.Vertices[i]);
            int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, m.Vertices.Length * 4, m.Vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, m.Indices.Length * 2, m.Indices, BufferUsageHint.StaticDraw);
            parts.Add(new Part(vbo, ibo, m.Indices.Length, m.Texture >= 0 && m.Texture < texIds.Count ? texIds[m.Texture] : 0));
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        return parts.Count > 0 ? new Model(parts, top) : null;
    }

    /// <summary>.ecm → SkinModelPath (.SMD) → .ski de mesmo nome (ou o primeiro da pasta); devolve a malha e a pasta.</summary>
    internal static (SkiModel Ski, string Dir)? ResolveSki(PckArchive pck, string ecm)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var ecmEntry = pck.Find(ecm.ToLowerInvariant());
        if (ecmEntry is null) return null;
        var text = System.Text.Encoding.GetEncoding(936).GetString(pck.Read(ecmEntry));
        var smd = Regex.Match(text, @"SkinModelPath:\s*(\S[^\r\n]*)").Groups[1].Value.Trim().ToLowerInvariant();
        if (smd.Length == 0) return null;
        var dir = Path.GetDirectoryName(smd) ?? "";
        var skiEntry = pck.Find(Path.ChangeExtension(smd, ".ski"))
            ?? pck.Entries.FirstOrDefault(e => e.Path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".ski", StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(e.Path)!.Equals(dir, StringComparison.OrdinalIgnoreCase));
        if (skiEntry is null || SkiModel.Read(pck.Read(skiEntry)) is not { } ski) return null;
        // malhas rígidas: pose do esqueleto (.bon de mesmo nome do .SMD, senão o da pasta)
        if (ski.Meshes.Any(m => m.Bone >= 0))
        {
            var bonEntry = pck.Find(Path.ChangeExtension(smd, ".bon"))
                ?? pck.Entries.FirstOrDefault(e => e.Path.EndsWith(".bon", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(e.Path)!.Equals(dir, StringComparison.OrdinalIgnoreCase));
            ski.PlaceRigid(bonEntry is null ? null : BonFile.Read(pck.Read(bonEntry)));
        }
        return (ski, dir);
    }

    /// <summary>Textura do modelo: na pasta, em "textures\" dela ou pelo nome em todo o pck.</summary>
    internal static PckEntry? FindTexture(PckArchive pck, string dir, string name, ref Dictionary<string, PckEntry>? byName)
    {
        var file = Path.GetFileName(name).ToLowerInvariant();
        var e = pck.Find(Path.Combine(dir, file)) ?? pck.Find(Path.Combine(dir, "textures", file));
        if (e is null)
        {
            byName ??= pck.Entries.Where(x => x.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            byName.TryGetValue(file, out e);
        }
        return e;
    }

    /// <summary>Desenha o modelo em pos (espaço do jogo), virado para yaw (graus, 0 = +Z/norte); false se ainda não carregou.</summary>
    public bool Draw(string ecm, Vector3 pos, float yaw)
    {
        if (!_models.TryGetValue(ecm, out var m)) { Request(ecm); return false; }
        if (m is null) return false;
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
            if (_cutout.Contains(p.Tex)) GL.Enable(EnableCap.AlphaTest); else GL.Disable(EnableCap.AlphaTest);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, p.Ibo);
            GL.DrawElements(PrimitiveType.Triangles, p.Count, DrawElementsType.UnsignedShort, 0);
        }
        GL.PopMatrix();
        return true;
    }

    public void Dispose()
    {
        foreach (var m in _models.Values) if (m is not null) foreach (var p in m.Parts) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); }
        foreach (var t in _textures) GL.DeleteTexture(t);
        _pck?.Dispose();
    }
}
