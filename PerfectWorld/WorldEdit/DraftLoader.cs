using System.Numerics;
using System.Text;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Arquivos de um mapa para o rascunho. O .ecwld/.ecbsd e as chaves do map.bht são lidos uma vez (Lazy, seguro entre
/// threads); o litmodels.pck é lido sob trava (o leitor de pacote não é seguro entre threads).
/// </summary>
public sealed class DraftSources(string elementDir, string map, string? serverConfigDir, Func<float, float, int> pieceAt, int pieceCount) : IDisposable
{
    public string ElementDir { get; } = elementDir;
    public string Map { get; } = map;
    public string? ServerConfigDir { get; } = serverConfigDir;
    /// <summary>Pedaço (1..PieceCount) de uma posição do jogo (x, z); 0 = fora do mapa (TerrainFile.PieceAt).</summary>
    public Func<float, float, int> PieceAt { get; } = pieceAt;
    public int PieceCount { get; } = pieceCount;
    public string EcwldPath { get; init; } = Path.Combine(elementDir, "maps", map, map + ".ecwld");
    public string EcbsdPath => Path.ChangeExtension(EcwldPath, ".ecbsd");
    public string LitPath => Path.Combine(ElementDir, "litmodels.pck");
    public string? BhtPath => ServerConfigDir is { } d && File.Exists(Path.Combine(d, Map, "map.bht")) ? Path.Combine(d, Map, "map.bht") : null;

    PckArchive? _lit; string? _litWhy; bool _litTried;
    internal readonly object LitLock = new();
    internal PckArchive? Lit(out string? why)
    {
        lock (LitLock)
        {
            if (!_litTried)
            {
                _litTried = true;
                try { _lit = PckArchive.Exists(LitPath) ? new PckArchive(LitPath) : null; _litWhy = _lit is null ? Loc.T("obj.noLit", "Não achei o litmodels.pck do cliente.") : null; }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("objetos: litmodels.pck", ex); _litWhy = Loc.T("obj.litBad", "Não deu para ler o litmodels.pck do cliente."); }
            }
            why = _litWhy; return _lit;
        }
    }

    internal Lazy<(List<DraftObject> Objects, string? Why)> Scene => _scene ??= new(() => DraftLoader.ReadScene(this));
    Lazy<(List<DraftObject>, string?)>? _scene;
    internal Lazy<Dictionary<string, List<int>>?> BhtKeys => _keys ??= new(() =>
    {
        try { return BhtPath is { } p ? new BhtFile(File.ReadAllBytes(p)).Keys() : null; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("objetos: map.bht", ex); return null; }
    });
    Lazy<Dictionary<string, List<int>>?>? _keys;
    /// <summary>.bmd que o cliente carrega (citados pelo .ecwld); null = vale a pasta inteira.</summary>
    internal Lazy<HashSet<string>?> Referenced => _refs ??= new(() => ObjectFormats.ReferencedBmds(EcwldPath, Map));
    Lazy<HashSet<string>?>? _refs;

    public void Dispose() { lock (LitLock) { _lit?.Dispose(); _lit = null; } }
}

/// <summary>Lê um pedaço do mapa para o rascunho. Nunca lança: o que não deu vira ReadOnlyWhy (do pedaço ou do objeto).</summary>
public static class DraftLoader
{
    public static string KeyOf(EcwldFile.Kind k, uint id) => $"ecwld:{k}:{id}";

    public static DraftPiece ReadPiece(DraftSources src, int piece)
    {
        var p = new DraftPiece(piece) { Loaded = true };
        try
        {
            var lit = src.Lit(out var litWhy);
            if (lit is null) p.ReadOnlyWhy = litWhy;
            else ReadBuildings(src, lit, piece, p);
            var (scene, why) = src.Scene.Value;
            if (why is not null) p.ReadOnlyWhy ??= why;
            p.Objects.AddRange(scene.Where(o => o.Piece == piece));
        }
        catch (Exception ex)
        {
            AppLog.Error($"objetos: pedaço {piece} de {src.Map}", ex);
            p.ReadOnlyWhy = Loc.T("obj.pieceBad", "Não deu para ler este pedaço do mapa; ele fica só para olhar.");
        }
        if (p.ReadOnlyWhy is not null) foreach (var o in p.Objects) o.ReadOnlyWhy ??= p.ReadOnlyWhy;
        return p;
    }

    /// <summary>Quantos objetos do cenário daquele tipo ficam fora do mapa (pedaço 0).</summary>
    public static int Outside(DraftSources src, ObjKind kind) => src.Scene.Value.Objects.Count(o => o.Kind == kind && o.Piece == 0);

    static void ReadBuildings(DraftSources src, PckArchive lit, int piece, DraftPiece p)
    {
        string prefix = $@"litmodels\{src.Map}\{piece}\";
        List<PckEntry> files;
        lock (src.LitLock) files = lit.Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) && e.Path.IndexOf('\\', prefix.Length) < 0).ToList();
        var keys = src.BhtKeys.Value;
        var refs = src.Referenced.Value;
        foreach (var e in files)
        {
            if (refs is not null && !refs.Contains(e.Path)) continue;   // órfão: o jogo não carrega
            byte[] b;
            lock (src.LitLock) b = lit.Read(e);
            try
            {
                var t = ObjectFormats.ReadBmd(b);
                var m = BmdModel.Read(b);
                var box = (Vector3.Zero, Vector3.Zero);
                if (m is not null && m.Meshes.Count > 0)
                {
                    var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
                    foreach (var mesh in m.Meshes)
                        for (int o = 0; o + 12 <= mesh.Vertices.Length; o += 24)
                        {
                            var v = new Vector3(BitConverter.ToSingle(mesh.Vertices, o), BitConverter.ToSingle(mesh.Vertices, o + 4), BitConverter.ToSingle(mesh.Vertices, o + 8));
                            mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
                        }
                    box = (mn, mx);
                }
                var cd = BmdModel.CdBrushes(b);
                bool solid = cd is { Count: > 0 } && (keys is null || cd.Any(o => keys.ContainsKey(BhtFile.KeyAt(b, o))));
                p.Objects.Add(new DraftObject
                {
                    Key = e.Path, Kind = ObjKind.Building, Piece = piece, Source = e.Path, Original = t, Solid = solid, LocalBox = box,
                    ReadOnlyWhy = cd is null ? Loc.T("obj.noCollision", "Não consegui ler a colisão desta construção; ela fica só para olhar.") : null,
                });
            }
            catch (InvalidDataException ex)
            {
                AppLog.Error("objetos: " + e.Path, ex);
                p.Objects.Add(new DraftObject { Key = e.Path, Kind = ObjKind.Building, Piece = piece, Source = e.Path, ReadOnlyWhy = Loc.T("obj.bmdBad", "Arquivo da construção ilegível; fica só para olhar.") });
            }
        }
    }

    /// <summary>Objetos do .ecwld/.ecbsd do mapa inteiro (modelos, efeitos, sons, bichos, árvores), já com o pedaço.</summary>
    internal static (List<DraftObject>, string?) ReadScene(DraftSources src)
    {
        var list = new List<DraftObject>();
        try
        {
            var bytes = File.ReadAllBytes(src.EcwldPath);
            var ecw = new EcwldFile(bytes);
            var sc = new WorldScene(bytes);
            if (File.Exists(src.EcbsdPath)) sc.LoadCrittersAndCurves(src.EcbsdPath);
            var seen = new HashSet<string>();
            int Piece(Vector3 v) => src.PieceAt(v.X, v.Z);
            foreach (var bl in ecw.Blocks)
            {
                foreach (var it in bl.Items[(int)EcwldFile.Kind.EcModel])
                {
                    var key = KeyOf(EcwldFile.Kind.EcModel, EcwldFile.IdOf(it));
                    if (!seen.Add(key)) continue;
                    var t = ObjectFormats.ReadEcModel(it);
                    list.Add(new DraftObject { Key = key, Kind = ObjKind.Model, Piece = Piece(t.Pos), Source = Text(it, 40), Original = t });
                }
                foreach (var it in bl.Items[(int)EcwldFile.Kind.Effect])
                {
                    var key = KeyOf(EcwldFile.Kind.Effect, EcwldFile.IdOf(it));
                    if (!seen.Add(key) || it.Length < 44) continue;
                    var t = new ObjTransform(V(it, 8), V(it, 20), V(it, 32), BitConverter.ToSingle(it, 4));
                    var gfx = sc.Effects.FirstOrDefault(x => x.Id == EcwldFile.IdOf(it))?.Gfx ?? "";
                    list.Add(new DraftObject { Key = key, Kind = ObjKind.Effect, Piece = Piece(t.Pos), Source = gfx, Original = t });
                }
                foreach (var it in bl.Items[(int)EcwldFile.Kind.Sound])
                {
                    var key = KeyOf(EcwldFile.Kind.Sound, EcwldFile.IdOf(it));
                    if (!seen.Add(key) || it.Length < 16) continue;
                    var t = new ObjTransform(V(it, 4), Vector3.UnitZ, Vector3.UnitY, 1);
                    var file = sc.Sounds.FirstOrDefault(x => x.Id == EcwldFile.IdOf(it))?.File ?? "";
                    list.Add(new DraftObject { Key = key, Kind = ObjKind.Sound, Piece = Piece(t.Pos), Source = file, Original = t });
                }
                foreach (var it in bl.Items[(int)EcwldFile.Kind.Critter])
                {
                    uint id = EcwldFile.IdOf(it);
                    var key = KeyOf(EcwldFile.Kind.Critter, id);
                    if (!seen.Add(key)) continue;
                    var c = sc.Critters.FirstOrDefault(x => x.Id == id);
                    var t = new ObjTransform(c is null ? Vector3.Zero : new Vector3(c.X, c.Y, c.Z), Vector3.UnitZ, Vector3.UnitY, 1);
                    list.Add(new DraftObject { Key = key, Kind = ObjKind.Critter, Piece = c is null ? 0 : Piece(t.Pos), Source = c?.Model ?? "", Original = t,
                        ReadOnlyWhy = c is null ? Loc.T("obj.critterBad", "Grupo de bichos sem dados no .ecbsd.") : null });
                }
            }
            for (int i = 0; i < sc.Trees.Count; i++)
            {
                var tr = sc.Trees[i];
                var t = new ObjTransform(new Vector3(tr.X, tr.Y, tr.Z), Vector3.UnitZ, Vector3.UnitY, 1);
                string spt = tr.Type >= 0 && tr.Type < sc.TreeTypes.Count ? sc.TreeTypes[tr.Type].Spt : "";
                list.Add(new DraftObject { Key = $"tree:{i}", Kind = ObjKind.Tree, Piece = Piece(t.Pos), Source = spt, Original = t, Type = tr.Type });
            }
            return (list, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            AppLog.Error("objetos: " + src.EcwldPath, ex);
            return (list, Loc.T("obj.ecwldBad", "Não deu para ler os objetos do cenário deste mapa (.ecwld); tudo fica só para olhar."));
        }
    }

    static Vector3 V(byte[] d, int at) => new(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));

    static string Text(byte[] it, int at)
    {
        if (at + 4 > it.Length) return "";
        int n = BitConverter.ToInt32(it, at);
        if (n <= 0 || at + 4 + n > it.Length) return "";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936).GetString(it, at + 4, n).TrimEnd('\0');
    }
}
