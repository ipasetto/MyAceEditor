using System.Numerics;
using System.Runtime.CompilerServices;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Arquivos montados em memória para o Salvar: entradas do litmodels.pck, arquivos inteiros e o resumo em frases.</summary>
public sealed record SavePlan(Dictionary<string, byte[]> PckFiles, Dictionary<string, byte[]> Files, List<string> Summary)
{
    public bool Empty => PckFiles.Count == 0 && Files.Count == 0;
}

/// <summary>
/// Salvar da ferramenta Objetos do mundo. Plan monta tudo em memória (nada grava) a partir do rascunho: construção
/// mexida = cabeçalho + brushes do .bmd, as gêmeas juntas, ornamentos do .ecbsd e brushes do map.bht (uma vez só);
/// construção apagada = referência do .ecwld + brushes do map.bht; modelo = item do .ecwld. Write grava tudo ou nada.
/// Efeitos, sons e bichos são do SceneEdit: entram pelos bytes-base (ecwldBase/ecbsdBase), não pelo rascunho.
/// </summary>
public static class DraftSaver
{
    /// <summary>Só teste: lança depois de N arquivos gravados (-1 = nunca).</summary>
    internal static int FailAfter = -1;
    /// <summary>Gravações em andamento (o programa espera antes de encerrar, para não deixar arquivo meio gravado).</summary>
    public static int Writing;
    /// <summary>Só teste: erro lançado no lugar da gravação do pacote (null = nenhum).</summary>
    internal static Func<Exception>? FailWith;

    static readonly ConditionalWeakTable<DraftSources, Dictionary<string, Dictionary<string, List<string>>>> Twins = new();

    public static byte[] ReadBmd(DraftSources src, string key)
    {
        var lit = src.Lit(out var why) ?? throw new InvalidDataException(why);
        lock (src.LitLock) return lit.Find(key) is { } e ? lit.Read(e) : throw new InvalidDataException(string.Format(Loc.T("obj.bmdMissing", "{0} não está mais no litmodels.pck."), key));
    }

    /// <summary>Construções do mesmo pedaço com exatamente os mesmos brushes (cópias "gêmeas", ex. a05), incluindo a própria.</summary>
    public static List<string> TwinsOf(DraftSources src, string key)
    {
        int cut = key.LastIndexOf('\\');
        string folder = key[..(cut + 1)];
        var byFolder = Twins.GetValue(src, _ => new(StringComparer.OrdinalIgnoreCase));
        Dictionary<string, List<string>> map;
        lock (byFolder)
        {
            if (!byFolder.TryGetValue(folder, out map!))
            {
                map = new(StringComparer.OrdinalIgnoreCase);
                var lit = src.Lit(out var why) ?? throw new InvalidDataException(why);
                List<PckEntry> files;
                lock (src.LitLock) files = lit.Entries.Where(e => e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) && e.Path.IndexOf('\\', folder.Length) < 0).ToList();
                var sig = new Dictionary<string, List<string>>();
                foreach (var e in files)
                {
                    byte[] b; lock (src.LitLock) b = lit.Read(e);
                    var cd = BmdModel.CdBrushes(b);
                    string s = cd is { Count: > 0 } ? string.Join("|", cd.Select(o => BhtFile.KeyAt(b, o)).Order()) : "solo:" + e.Path;
                    if (!sig.TryGetValue(s, out var l)) sig[s] = l = new();
                    l.Add(e.Path);
                }
                foreach (var l in sig.Values) foreach (var f in l) map[f] = l;
                byFolder[folder] = map;
            }
        }
        return map.TryGetValue(key, out var g) ? g : [key];
    }

    /// <summary>Giro R e as posições de origem e destino que levam o lugar <paramref name="from"/> ao <paramref name="to"/>: x' = R(x − p0) + p1.</summary>
    internal static (Quaternion R, Vector3 P0, Vector3 P1) Rigid(ObjTransform from, ObjTransform to)
    {
        static Matrix4x4 Frame(ObjTransform t)
        {
            var f = Vector3.Normalize(t.Dir); var u = Vector3.Normalize(t.Up);
            var r = Vector3.Normalize(Vector3.Cross(u, f)); u = Vector3.Cross(f, r);
            return new Matrix4x4(r.X, r.Y, r.Z, 0, u.X, u.Y, u.Z, 0, f.X, f.Y, f.Z, 0, 0, 0, 0, 1);
        }
        var m = Matrix4x4.Transpose(Frame(from)) * Frame(to);
        var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        return (q, from.Pos, to.Pos);
    }

    static bool IsIdentity(Quaternion q) => MathF.Abs(q.W) > 1 - 1e-7f;

    static void TransformBrush(byte[] d, int at, Quaternion r, float k, Vector3 p0, Vector3 p1)
    {
        if (MathF.Abs(k - 1) > 1e-6f) ObjectFormats.ScaleBrush(d, at, k, p0);
        if (!IsIdentity(r)) ObjectFormats.RotateBrush(d, at, r, p0);
        var t = p1 - p0;
        BhtFile.MoveAt(d, at, t.X, t.Y, t.Z);
    }

    /// <summary>Blocos do .ecwld cuja área cobre a caixa (x, z); nenhum = os da origem.</summary>
    static IEnumerable<EcwldFile.Block> BlocksFor(EcwldFile ecw, Vector3 mn, Vector3 mx, IEnumerable<EcwldFile.Block> fallback)
    {
        var list = ecw.Blocks.Where(bl => ObjectFormats.BlockArea(ecw, bl) is var a && mx.X >= a.X0 && mn.X <= a.X1 && mx.Z >= a.Z0 && mn.Z <= a.Z1).ToList();
        if (list.Count == 0) list = fallback.Distinct().ToList();
        if (list.Count == 0) throw new InvalidDataException(Loc.T("obj.noBlock", "Não há bloco do cenário nesse lugar do mapa."));
        return list;
    }

    /// <summary>
    /// Leva as cópias do item (referência de construção ou modelo) para os blocos que cobrem o lugar novo. O cliente carrega
    /// cada construção pela referência no bloco (CECSceneBlock::LoadOrnaments) e o caminho do .bmd vem do nome no .ecbsd,
    /// sem olhar o pedaço: mudando os blocos, a construção pode ir para qualquer lugar do mapa (estudo 08/11). Só mexe se
    /// faltar bloco ou sobrar bloco longe (20 m de folga, para não trocar os blocos do exportador num ajuste pequeno).
    /// </summary>
    static bool Rehome(EcwldFile ecw, EcwldFile.Kind k, uint id, Vector3 mn, Vector3 mx)
    {
        var hits = ecw.Find(k, id).ToList();
        if (hits.Count == 0) return false;
        var now = hits.Select(h => h.Block).Distinct().ToList();
        var want = BlocksFor(ecw, mn, mx, now).ToList();
        const float M = 20;
        bool Near(EcwldFile.Block bl) => ObjectFormats.BlockArea(ecw, bl) is var a && mx.X + M >= a.X0 && mn.X - M <= a.X1 && mx.Z + M >= a.Z0 && mn.Z - M <= a.Z1;
        var keep = now.Where(Near).Union(want).ToList();
        if (keep.Count == now.Count && keep.All(now.Contains)) return false;
        var item = (byte[])hits[0].Block.Items[(int)k][hits[0].Index].Clone();
        ecw.Delete(k, id);
        foreach (var bl in keep) bl.Items[(int)k].Add((byte[])item.Clone());
        return true;
    }

    /// <summary>Bloco do .ecwld cuja área contém o ponto (x, z); null = nenhum (o jogo não carregaria nada ali).</summary>
    internal static EcwldFile.Block? BlockAt(EcwldFile ecw, Vector3 p) =>
        ecw.Blocks.FirstOrDefault(bl => ObjectFormats.BlockArea(ecw, bl) is var a && p.X >= a.X0 && p.X <= a.X1 && p.Z >= a.Z0 && p.Z <= a.Z1);

    /// <summary>Item da árvore no .ecwld: mesmo tipo e o lugar original exato (a árvore não tem ID).</summary>
    static (EcwldFile.Block, int)? FindTree(EcwldFile ecw, DraftObject o)
    {
        var p = o.Original.Pos;
        foreach (var bl in ecw.Blocks)
        {
            var list = bl.Items[(int)EcwldFile.Kind.Tree];
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it.Length < 12 || BitConverter.ToInt32(it, 0) != o.Type) continue;
                bool hasY = it.Length >= 16;
                float x = BitConverter.ToSingle(it, 4), z = BitConverter.ToSingle(it, hasY ? 12 : 8);
                if (x == p.X && z == p.Z && (!hasY || BitConverter.ToSingle(it, 8) == p.Y)) return (bl, i);
            }
        }
        return null;
    }

    static void WriteTree(byte[] it, Vector3 p)
    {
        BitConverter.GetBytes(p.X).CopyTo(it, 4);
        if (it.Length >= 16) { BitConverter.GetBytes(p.Y).CopyTo(it, 8); BitConverter.GetBytes(p.Z).CopyTo(it, 12); }
        else BitConverter.GetBytes(p.Z).CopyTo(it, 8);
    }

    /// <summary>Pedaço pela pasta do .bmd (litmodels\mapa\N\...); 0 = não deu.</summary>
    static int FolderPiece(string key) { var parts = key.Split('\\'); return parts.Length >= 2 && int.TryParse(parts[^2], out int n) ? n : 0; }

    /// <summary>Pasta do pedaço no litmodels.pck, no formato da chave de origem (litmodels\mapa\N\).</summary>
    static string FolderFor(string srcKey, int piece, string? map = null) { var parts = srcKey.Split('\\'); parts[^2] = piece.ToString(); if (map is not null && parts.Length >= 4) parts[1] = map; return string.Join('\\', parts[..^1]) + "\\"; }

    /// <summary>
    /// Nome livre na pasta do pedaço onde o objeto FICA (piece; 0 = pasta da origem): litmodel_N.bmd com N acima do maior
    /// que existe. O editor acha a construção pela pasta; o jogo pela referência (qualquer pasta serve para ele).
    /// </summary>
    static string NewBmdPath(DraftSources src, string srcKey, HashSet<string> used, int piece = 0)
    {
        string folder = piece > 0 && FolderPiece(srcKey) > 0 ? FolderFor(srcKey, piece, src.Map) : srcKey[..(srcKey.LastIndexOf('\\') + 1)];
        var lit = src.Lit(out var why) ?? throw new InvalidDataException(why);
        int max = 0;
        lock (src.LitLock)
            foreach (var e in lit.Entries)
                if (e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && e.Path.IndexOf('\\', folder.Length) < 0)
                {
                    var n = Path.GetFileNameWithoutExtension(e.Path);
                    int u = n.LastIndexOf('_');
                    if (u >= 0 && int.TryParse(n[(u + 1)..], out var v)) max = Math.Max(max, v);
                }
        for (int i = max + 1; ; i++)
        {
            string p = folder + "litmodel_" + i + ".bmd";
            if (used.Add(p)) return p;
        }
    }

    public static SavePlan Plan(WorldDraft d, DraftSources src, byte[]? ecwldBase = null, byte[]? ecbsdBase = null)
    {
        var changed = d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Changed && o.ReadOnlyWhy is null && o.Kind is ObjKind.Building or ObjKind.Model).ToList();
        var added = d.Pieces.Values.SelectMany(p => p.Added).Where(o => !o.Deleted && o.CopyOf is not null && o.Kind is ObjKind.Building or ObjKind.Model).ToList();
        var plan = new SavePlan(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // nomes novos de .bmd deste salvar
        var treeEdits = d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Kind == ObjKind.Tree && o.Changed && o.ReadOnlyWhy is null).ToList();
        var treeNew = d.Pieces.Values.SelectMany(p => p.Added).Where(o => o.Kind == ObjKind.Tree && !o.Deleted).ToList();
        if (changed.Count == 0 && added.Count == 0 && treeEdits.Count == 0 && treeNew.Count == 0) return plan;

        var ecw = new EcwldFile(ecwldBase ?? File.ReadAllBytes(src.EcwldPath));
        var bsd = (byte[])(ecbsdBase ?? File.ReadAllBytes(src.EcbsdPath)).Clone();
        bool ecwDirty = false, bsdDirty = false, bhtDirty = false;
        BhtFile? bht = null; Dictionary<string, List<int>>? keys = null; byte[]? bhtOrig = null;
        var drop = new HashSet<int>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = new Dictionary<(int Piece, string What), int>();
        void Count(int piece, string what) => count[(piece, what)] = count.GetValueOrDefault((piece, what)) + 1;

        foreach (var o in changed.Where(o => o.Kind == ObjKind.Building))
        {
            if (done.Contains(o.Key)) continue;
            var group = TwinsOf(src, o.Key);
            var b0 = ReadBmd(src, o.Key);
            var cd = BmdModel.CdBrushes(b0) ?? throw new InvalidDataException(string.Format(Loc.T("obj.noBrushSave", "{0}: não consegui ler a colisão; nada foi salvo."), Path.GetFileName(o.Key)));
            var mine = cd.Select(at => BhtFile.KeyAt(b0, at)).ToList();
            if (mine.Count > 0 && bht is null)
            {
                var path = src.BhtPath ?? throw new InvalidDataException(Loc.T("world.bldNoServer", "Não achei o map.bht do servidor deste mapa. Abra a pasta do servidor (aba NPCGen ou Verificação) e salve de novo."));
                bhtOrig = File.ReadAllBytes(path);
                bht = new BhtFile(bhtOrig); keys = bht.Keys();
            }
            var sel = keys is null ? new List<int>() : mine.Where(keys.ContainsKey).SelectMany(k => keys[k]).Distinct().ToList();

            if (o.Deleted)
            {
                foreach (var f in group)
                {
                    var refs = ObjectFormats.OrnamentRefsOf(ecw, bsd, f);
                    if (refs.Count == 0) throw new InvalidDataException(string.Format(Loc.T("obj.noRef", "{0}: não achei a referência dela no .ecwld; não dá para apagar com segurança."), Path.GetFileName(f)));
                    foreach (var id in refs.Select(x => x.Id).Distinct()) ecw.Delete(EcwldFile.Kind.Ornament, id);
                    done.Add(f);
                }
                ecwDirty = true;
                foreach (int i in sel) drop.Add(i);
                bhtDirty |= sel.Count > 0;
                Count(o.Piece, "del");
                continue;
            }

            var (r, p0, p1) = Rigid(o.Original, o.Now);
            float k = o.Original.Scale > 0 ? o.Now.Scale / o.Original.Scale : 1;
            Vector3 Map(Vector3 p) => (IsIdentity(r) ? (p - p0) * k : Vector3.Transform((p - p0) * k, r)) + p1;
            Vector3 Turn(Vector3 v) => IsIdentity(r) ? v : Vector3.Transform(v, r);
            foreach (var f in group)
            {
                if (f != o.Key && d.Find(f) is { Changed: true } other)
                {
                    var tf0 = ObjectFormats.ReadBmd(ReadBmd(src, f));
                    if (other.Deleted || Vector3.Distance(other.Now.Pos, Map(tf0.Pos)) > 0.05f)
                        throw new InvalidDataException(Loc.T("obj.twinConflict", "Duas construções gêmeas (mesma colisão) foram mexidas de jeitos diferentes; desfaça uma delas."));
                }
                var bf = f == o.Key ? b0 : ReadBmd(src, f);
                var t = ObjectFormats.ReadBmd(bf);
                var nb = ObjectFormats.WithBmdTransform(bf, f == o.Key ? o.Now : t with { Pos = Map(t.Pos), Dir = Turn(t.Dir), Up = Turn(t.Up), Scale = t.Scale * k });
                var at = BmdModel.CdBrushes(nb)!;
                foreach (int a in at) TransformBrush(nb, a, r, k, p0, p1);
                if (BmdModel.Read(nb) is null || BmdModel.CdBrushes(nb)?.Count != at.Count) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
                var frefs = ObjectFormats.OrnamentRefsOf(ecw, bsd, f);
                int toPiece = src.PieceAt(o.Now.Pos.X, o.Now.Pos.Z);
                if (group.Count == 1 && toPiece > 0 && FolderPiece(f) > 0 && toPiece != FolderPiece(f) && frefs.Count > 0)
                {
                    // foi para outro pedaço de 1 km: o editor acha a construção pela pasta, então ela ganha um .bmd na pasta
                    // nova e um ornamento novo no fim do .ecbsd; as referências (mesmo ID) apontam para ele. O .bmd e o
                    // ornamento velhos ficam sem referência (o jogo não mostra, como uma construção apagada).
                    string np = NewBmdPath(src, f, usedNames, toPiece);
                    plan.PckFiles[np] = nb;
                    int srcOff = frefs[0].Offset;
                    var ot = new ObjTransform(Map(ObjectFormats.OrnamentPos(bsd, srcOff)), Turn(ObjectFormats.OrnamentDir(bsd, srcOff)), Turn(ObjectFormats.OrnamentUp(bsd, srcOff)), 1);
                    var np2 = np.Split('\\');
                    (bsd, int newOff) = ObjectFormats.NewOrnament(bsd, srcOff, np2[^2] + "\\" + np2[^1], ot);
                    foreach (uint rid in frefs.Select(x => x.Id).Distinct())
                        foreach (var (bl, i) in ecw.Find(EcwldFile.Kind.Ornament, rid).ToList())
                            BitConverter.GetBytes(newOff).CopyTo(bl.Items[(int)EcwldFile.Kind.Ornament][i], 4);
                    bsdDirty = true; ecwDirty = true;
                }
                else
                {
                    plan.PckFiles[f] = nb;
                    foreach (int off in frefs.Select(x => x.Offset).Distinct())
                    {
                        ObjectFormats.MoveOrnament(bsd, off, Map, Turn);
                        bsdDirty = true;
                    }
                }
                // referências nos blocos do lugar novo (gêmeas têm a mesma caixa)
                var (bmn, bmx) = DraftChecks.WorldBox(o);
                foreach (uint rid in frefs.Select(x => x.Id).Distinct()) ecwDirty |= Rehome(ecw, EcwldFile.Kind.Ornament, rid, bmn, bmx);
                done.Add(f);
            }
            if (sel.Count > 0)
            {
                if (MathF.Abs(k - 1) > 1e-6f) bht!.Scale(sel, k, p0);
                if (!IsIdentity(r)) bht!.Rotate(sel, r, p0);
                var mv = p1 - p0;
                bht!.Move(sel, mv.X, mv.Y, mv.Z);
                bhtDirty = true;
            }
            Count(o.Piece, "move");
        }

        foreach (var o in changed.Where(o => o.Kind == ObjKind.Model))
        {
            uint id = uint.Parse(o.Key[(o.Key.LastIndexOf(':') + 1)..]);
            var hits = ecw.Find(EcwldFile.Kind.EcModel, id).ToList();
            if (hits.Count == 0) throw new InvalidDataException(string.Format(Loc.T("obj.modelGone", "Modelo {0} não está mais no .ecwld."), id));
            if (o.Deleted) { ecw.Delete(EcwldFile.Kind.EcModel, id); Count(o.Piece, "mdel"); }
            else
            {
                foreach (var (bl, i) in hits) ObjectFormats.WriteEcModel(bl.Items[(int)EcwldFile.Kind.EcModel][i], o.Now);
                Rehome(ecw, EcwldFile.Kind.EcModel, id, o.Now.Pos, o.Now.Pos);   // blocos do lugar novo
                Count(o.Piece, "mmove");
            }
            ecwDirty = true;
        }

        // ---- objetos novos (colados): construção = .bmd novo + ornamento no fim do .ecbsd + referência nos blocos + brushes; modelo = item novo ----
        var newBrushes = new List<byte[]>();
        uint nextId = ecw.MaxId() + 1;
        foreach (var o in added)
        {
            var (mn, mx) = DraftChecks.WorldBox(o);
            if (o.Kind == ObjKind.Model)
            {
                uint srcId = uint.Parse(o.CopyOf![(o.CopyOf!.LastIndexOf(':') + 1)..]);
                var hits = ecw.Find(EcwldFile.Kind.EcModel, srcId).ToList();
                if (hits.Count == 0) throw new InvalidDataException(string.Format(Loc.T("obj.modelGone", "Modelo {0} não está mais no .ecwld."), srcId));
                var item = (byte[])hits[0].Block.Items[(int)EcwldFile.Kind.EcModel][hits[0].Index].Clone();
                BitConverter.GetBytes(nextId++).CopyTo(item, 0);
                ObjectFormats.WriteEcModel(item, o.Now);
                foreach (var bl in BlocksFor(ecw, o.Now.Pos, o.Now.Pos, hits.Select(h => h.Block))) bl.Items[(int)EcwldFile.Kind.EcModel].Add((byte[])item.Clone());
                ecwDirty = true; Count(o.Piece, "mnew");
                continue;
            }
            var b0 = ReadBmd(src, o.CopyOf!);
            var t0 = ObjectFormats.ReadBmd(b0);
            var cd0 = BmdModel.CdBrushes(b0) ?? throw new InvalidDataException(string.Format(Loc.T("obj.noBrushSave", "{0}: não consegui ler a colisão; nada foi salvo."), Path.GetFileName(o.CopyOf)));
            var (r, p0, p1) = Rigid(t0, o.Now);
            float k = t0.Scale > 0 ? o.Now.Scale / t0.Scale : 1;
            Vector3 Map(Vector3 p) => (IsIdentity(r) ? (p - p0) * k : Vector3.Transform((p - p0) * k, r)) + p1;
            Vector3 Turn(Vector3 v) => IsIdentity(r) ? v : Vector3.Transform(v, r);
            var nb = ObjectFormats.WithBmdTransform(b0, o.Now);
            var at = BmdModel.CdBrushes(nb)!;
            foreach (int a in at) TransformBrush(nb, a, r, k, p0, p1);
            if (BmdModel.Read(nb) is null || BmdModel.CdBrushes(nb)?.Count != cd0.Count) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
            string path = NewBmdPath(src, o.CopyOf!, usedNames, src.PieceAt(o.Now.Pos.X, o.Now.Pos.Z));   // pasta do pedaço onde a cópia fica
            plan.PckFiles[path] = nb;
            // de onde vem: este mapa ou OUTRO mapa/instância (catálogo de todos os mapas; litmodels\<mapa>\<pedaço>\...)
            var cparts = o.CopyOf!.Split('\\');
            string fromMap = cparts.Length >= 4 ? cparts[1] : src.Map;
            bool otherMap = !fromMap.Equals(src.Map, StringComparison.OrdinalIgnoreCase);
            EcwldFile refEcw = ecw; byte[] refBsd = bsd;
            if (otherMap)
            {
                string fe = Path.Combine(src.ElementDir, "maps", fromMap, fromMap + ".ecwld");
                if (!File.Exists(fe) || !File.Exists(Path.ChangeExtension(fe, ".ecbsd")))
                    throw new InvalidDataException(string.Format(Loc.T("obj.fromMapGone", "{0}: o cenário do mapa de origem ({1}) não está no cliente; nada foi salvo."), Path.GetFileName(o.CopyOf), fromMap));
                refEcw = new EcwldFile(File.ReadAllBytes(fe)); refBsd = File.ReadAllBytes(Path.ChangeExtension(fe, ".ecbsd"));
            }
            var refs = ObjectFormats.OrnamentRefsOf(refEcw, refBsd, o.CopyOf!);
            if (refs.Count == 0) throw new InvalidDataException(string.Format(Loc.T("obj.noRef", "{0}: não achei a referência dela no .ecwld; não dá para apagar com segurança."), Path.GetFileName(o.CopyOf)));
            int srcOff = refs[0].Offset;
            var ot = new ObjTransform(Map(ObjectFormats.OrnamentPos(refBsd, srcOff)), Turn(ObjectFormats.OrnamentDir(refBsd, srcOff)), Turn(ObjectFormats.OrnamentUp(refBsd, srcOff)), 1);
            var parts = path.Split('\\');
            (bsd, int newOff) = ObjectFormats.NewOrnament(bsd, srcOff, parts[^2] + "\\" + parts[^1], ot, otherMap ? refBsd : null);
            bsdDirty = true;
            // referência nova (8 B: ID, offset) nos blocos que a caixa cobre; molde = uma referência deste mapa, se houver
            byte[] refItem;
            if (!otherMap) { var hit0 = ecw.Find(EcwldFile.Kind.Ornament, refs[0].Id).First(); refItem = (byte[])hit0.Block.Items[(int)EcwldFile.Kind.Ornament][hit0.Index].Clone(); }
            else refItem = ecw.Blocks.SelectMany(bl => bl.Items[(int)EcwldFile.Kind.Ornament]).FirstOrDefault(it => it.Length >= 8) is { } any ? (byte[])any.Clone() : new byte[8];
            BitConverter.GetBytes(nextId++).CopyTo(refItem, 0);
            BitConverter.GetBytes(newOff).CopyTo(refItem, 4);
            var fallback = otherMap ? [] : ecw.Find(EcwldFile.Kind.Ornament, refs[0].Id).Select(h => h.Block).ToList();
            foreach (var bl in BlocksFor(ecw, mn, mx, fallback)) bl.Items[(int)EcwldFile.Kind.Ornament].Add((byte[])refItem.Clone());
            ecwDirty = true;
            // colisão: a original tem brushes no map.bht do mapa DELA? então a cópia leva os dela para o map.bht deste mapa
            var mine0 = cd0.Select(a => BhtFile.KeyAt(b0, a)).ToList();
            if (mine0.Count > 0)
            {
                if (bht is null && src.BhtPath is { } bp) { bhtOrig = File.ReadAllBytes(bp); bht = new BhtFile(bhtOrig); keys = bht.Keys(); }   // guarda o de antes: a passagem compara velho × novo
                var srcKeys = keys;
                if (otherMap)
                {
                    string? fb = src.ServerConfigDir is { } sd && File.Exists(Path.Combine(sd, fromMap, "map.bht")) ? Path.Combine(sd, fromMap, "map.bht") : null;
                    srcKeys = fb is null ? null : new BhtFile(File.ReadAllBytes(fb)).Keys();
                }
                if (bht is not null && (srcKeys is null ? otherMap : mine0.Any(srcKeys.ContainsKey)))   // sem o map.bht de origem: leva (tem brushes)
                    foreach (int a in at) newBrushes.Add(nb[a..(a + 56 + 17 * BitConverter.ToInt32(nb, a + 52))]);
            }
            Count(o.Piece, "new");
        }
        if (newBrushes.Count > 0) bhtDirty = true;

        // ---- árvores: só o .ecwld (16 B: tipo, x, y, z). Sem colisão no servidor no 1.5.5: o map.bht não tem paredes de árvore e o trees.pck não tem .tbh (0 de 57 tipos; estudo 09/11) ----
        foreach (var o in treeEdits)
        {
            var (bl, idx) = FindTree(ecw, o) ?? throw new InvalidDataException(string.Format(Loc.T("obj.treeGone", "Árvore {0} não está mais no .ecwld; reabra o Mundo 3D."), o.Key));
            var list = bl.Items[(int)EcwldFile.Kind.Tree];
            if (o.Deleted) { list.RemoveAt(idx); Count(o.Piece, "tdel"); ecwDirty = true; continue; }
            var item = list[idx];
            WriteTree(item, o.Now.Pos);
            var target = BlockAt(ecw, o.Now.Pos) ?? throw new InvalidDataException(Loc.T("obj.noBlock", "Não há bloco do cenário nesse lugar do mapa."));
            if (target != bl) { list.RemoveAt(idx); target.Items[(int)EcwldFile.Kind.Tree].Add(item); }
            Count(o.Piece, "tmove"); ecwDirty = true;
        }
        // tipos de árvore que este mapa não tem (vindos de outro mapa): entram na lista do mapa, na ordem, e as árvores
        // novas ganham o número certo (o número do editor pode ter buracos de prévias que não foram plantadas)
        int fileTypes = ecw.TreeTypeCount;
        var extraTypes = new List<(string Spt, string Comp)>();
        var typeOf = new Dictionary<DraftObject, int>();
        foreach (var o in treeNew.Where(o => o.Type >= fileTypes))
        {
            if (string.IsNullOrEmpty(o.Source) || string.IsNullOrEmpty(o.TreeComposite)) throw new InvalidDataException(Loc.T("obj.treeNoType", "Árvore nova sem tipo."));
            var key = (o.Source, o.TreeComposite!);
            int k = extraTypes.FindIndex(t => t.Spt.Equals(key.Source, StringComparison.OrdinalIgnoreCase) && t.Comp.Equals(key.Item2, StringComparison.OrdinalIgnoreCase));
            if (k < 0) { extraTypes.Add(key); k = extraTypes.Count - 1; }
            typeOf[o] = fileTypes + k;
        }
        if (extraTypes.Count > 0) { ecw.AddTreeTypes(extraTypes); ecwDirty = true; }
        foreach (var o in treeNew)
        {
            if (o.Type < 0) throw new InvalidDataException(Loc.T("obj.treeNoType", "Árvore nova sem tipo."));
            var target = BlockAt(ecw, o.Now.Pos) ?? throw new InvalidDataException(Loc.T("obj.noBlock", "Não há bloco do cenário nesse lugar do mapa."));
            var sample = ecw.Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.Tree]).FirstOrDefault();
            var item = new byte[sample?.Length ?? 16];
            BitConverter.GetBytes(typeOf.TryGetValue(o, out int nt) ? nt : o.Type).CopyTo(item, 0);
            WriteTree(item, o.Now.Pos);
            target.Items[(int)EcwldFile.Kind.Tree].Add(item);
            Count(o.Piece, "tnew"); ecwDirty = true;
        }

        if (ecwDirty) { var e = ecw.Write(); _ = new EcwldFile(e); plan.Files[src.EcwldPath] = e; }
        if (bsdDirty) plan.Files[src.EcbsdPath] = bsd;
        if (bhtDirty && bht is not null)
        {
            var bb = drop.Count > 0 ? bht.Without(drop) : bht.Write();
            if (newBrushes.Count > 0) bb = new BhtFile(bb).With(newBrushes);
            _ = new BhtFile(bb);   // relê: formato inteiro ainda certo
            plan.Files[src.BhtPath!] = bb;
        }
        // passagem dos monstros (.rmap/.dhmap) e voo (.octr) no lugar antigo e no novo de TODA construção posta, mexida ou
        // apagada, com ou sem colisão própria (usuário 09/11: as cópias e as sem brush não refaziam a passagem e os monstros
        // atravessavam a pedra e a ponte; antes só rodava com o map.bht mudado e o de antes guardado)
        var bldFx = changed.Concat(added).Where(o => o.Kind == ObjKind.Building).ToList();
        if (bldFx.Count > 0 && src.ServerConfigDir is not null)
            try
            {
                var oldB = bhtOrig is not null ? new BhtFile(bhtOrig, clone: false)
                    : src.BhtPath is { } bp0 && File.Exists(bp0) ? new BhtFile(File.ReadAllBytes(bp0)) : BhtFile.Empty();
                var newB = src.BhtPath is { } bp1 && plan.Files.TryGetValue(bp1, out var nbb) ? new BhtFile(nbb) : oldB;
                DraftChecks.BuildingEffects(plan, src, bldFx, oldB, newB);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { AppLog.Error("objetos: passagem das construções", ex); plan.Summary.Add(Loc.T("obj.effectsFail2", "A passagem dos monstros em volta das construções não foi refeita (veja o log); as construções gravam assim mesmo.")); }
        // árvores na passagem dos monstros: a copiada/movida fecha o círculo dela, a apagada reabre (estudo 09/11)
        if ((treeEdits.Count > 0 || treeNew.Count > 0) && src.ServerConfigDir is not null)
            try { DraftChecks.TreeEffects(plan, src, treeEdits, treeNew, ecwldBase ?? File.ReadAllBytes(src.EcwldPath), bht); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { AppLog.Error("objetos: passagem das árvores", ex); plan.Summary.Add(Loc.T("obj.treePassFail", "A passagem dos monstros em volta das árvores não foi refeita (veja o log); as árvores gravam assim mesmo.")); }
        foreach (var g in count.GroupBy(k => k.Key.Piece).OrderBy(g => g.Key))
        {
            var parts = new List<string>();
            void Say(string what, string one, string many) { if (count.GetValueOrDefault((g.Key, what)) is var n and > 0) parts.Add(string.Format(n == 1 ? one : many, n)); }
            Say("move", Loc.T("obj.sumMove1", "1 construção mexida"), Loc.T("obj.sumMoveN", "{0} construções mexidas"));
            Say("del", Loc.T("obj.sumDel1", "1 construção apagada"), Loc.T("obj.sumDelN", "{0} construções apagadas"));
            Say("mmove", Loc.T("obj.sumMMove1", "1 modelo mexido"), Loc.T("obj.sumMMoveN", "{0} modelos mexidos"));
            Say("mdel", Loc.T("obj.sumMDel1", "1 modelo apagado"), Loc.T("obj.sumMDelN", "{0} modelos apagados"));
            Say("new", Loc.T("obj.sumNew1", "1 construção nova"), Loc.T("obj.sumNewN", "{0} construções novas"));
            Say("tmove", Loc.T("obj.sumTMove1", "1 árvore mexida"), Loc.T("obj.sumTMoveN", "{0} árvores mexidas"));
            Say("tdel", Loc.T("obj.sumTDel1", "1 árvore apagada"), Loc.T("obj.sumTDelN", "{0} árvores apagadas"));
            Say("tnew", Loc.T("obj.sumTNew1", "1 árvore plantada"), Loc.T("obj.sumTNewN", "{0} árvores plantadas"));
            Say("mnew", Loc.T("obj.sumMNew1", "1 modelo novo"), Loc.T("obj.sumMNewN", "{0} modelos novos"));
            plan.Summary.Add(string.Format(Loc.T("obj.sumPiece", "Pedaço {0}: {1}."), g.Key, string.Join(", ", parts)));
        }
        return plan;
    }

    /// <summary>
    /// Grava tudo ou nada: confere que nenhum arquivo está preso; cópia de segurança dos arquivos; grava os arquivos e
    /// por último o pacote (o maior e mais demorado); se algo falhar, os já gravados voltam das cópias.
    /// </summary>
    public static SaveResult Write(SavePlan p, DraftSources src, Action<int>? progress = null)
    {
        Interlocked.Increment(ref Writing);
        try { return WriteCore(p, src, progress); }
        finally { Interlocked.Decrement(ref Writing); }
    }

    static SaveResult WriteCore(SavePlan p, DraftSources src, Action<int>? progress)
    {
        // andamento em % para a barra azul: cópias 0-10, arquivos pequenos 10-20, pacote 20-98 (é o grosso), fim 100
        int last = -1;
        void Report(int pct) { pct = Math.Clamp(pct, 0, 100); if (pct != last) { last = pct; progress?.Invoke(pct); } }
        Report(0);
        if (p.Empty) return new SaveResult(SaveKind.ClientOnly, Loc.T("obj.nothing", "Nada para salvar."));
        var pckPath = PckArchive.Locate(src.LitPath) ?? src.LitPath;
        var targets = p.Files.Keys.ToList();
        if (p.PckFiles.Count > 0) { targets.Add(pckPath); targets.AddRange(PckArchive.PartPaths(pckPath)); }
        foreach (var t in targets.Where(File.Exists))
        {
            try { using var fs = new FileStream(t, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("objetos: preso " + t, ex);
                return new SaveResult(SaveKind.Locked, string.Format(Loc.T("obj.locked", "{0} está aberto em outro programa (o jogo?). Feche e salve de novo; nada foi gravado."), Path.GetFileName(t)));
            }
        }
        var baks = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var f in p.Files.Keys) baks[f] = File.Exists(f) ? BackupStore.Save(f) : null; Report(10); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("objetos: cópia de segurança", ex);
            return new SaveResult(SaveKind.Failed, Loc.T("obj.noBackup", "Não deu para fazer a cópia de segurança; nada foi gravado."));
        }
        var written = new List<string>();
        var setFiles = baks.Where(kv => kv.Value is not null).Select(kv => new BackupSetEntry(kv.Key, kv.Value!)).ToList();
        string? pckHead = null;
        try
        {
            foreach (var (f, bytes) in p.Files)
            {
                if (FailAfter >= 0 && written.Count >= FailAfter) throw new IOException("falha simulada (teste)");
                AtomicFileSave.Write(f, bytes, backup: false);   // a cópia já está no conjunto (baks)
                written.Add(f);
                Report(10 + 10 * written.Count / Math.Max(1, p.Files.Count));
            }
            if (FailAfter >= 0 && written.Count >= FailAfter) throw new IOException("falha simulada (teste)");
            if (FailWith is { } fw) throw fw();
            if (p.PckFiles.Count > 0)
            {
                var parts = PckArchive.PartPaths(pckPath);
                var target = parts.Count > 0 ? parts[^1] : pckPath;
                if (parts.Count > 0)
                {
                    using var fs = new FileStream(pckPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var head = new byte[4]; fs.Position = 4; fs.ReadExactly(head);
                    pckHead = Convert.ToHexString(head);
                }
                var pbak = PckWriter.Write(src.LitPath, p.PckFiles, f => Report(20 + (int)(78 * f)));
                setFiles.Add(new BackupSetEntry(target, pbak));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("objetos: gravar", ex);
            var stuck = new List<string>();
            foreach (var w in written)
            {
                try { if (baks[w] is { } bk) AtomicFileSave.Write(w, BackupStore.Read(bk)); }
                catch (Exception rex) when (rex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException) { AppLog.Error("objetos: voltar " + w, rex); stuck.Add(Path.GetFileName(w)); }
            }
            return stuck.Count == 0
                ? new SaveResult(SaveKind.RolledBack, Loc.T("obj.rolledBack", "Não deu para salvar; tudo voltou como estava. As mudanças continuam no editor para tentar de novo."))
                : new SaveResult(SaveKind.Failed, string.Format(Loc.T("obj.rollbackFail", "Não deu para salvar e {0} não voltou sozinho; use Ferramentas → Backups para restaurar."), string.Join(", ", stuck)));
        }
        // um item único "Objetos do mundo" no Restaurar backup volta todos os arquivos deste Salvar juntos
        BackupSets.Save(new BackupSet("objects", src.Map, DateTime.Now, setFiles, pckHead is null ? null : pckPath, pckHead));
        Report(100);
        return new SaveResult(SaveKind.Both, string.Format(Loc.T("obj.saved", "Salvo: {0}"), string.Join(" ", p.Summary)));
    }
}

public sealed record CheckItem(bool Blocks, string Text, string Key);

/// <summary>Conferências antes do Salvar (spec seção 5): o que bloqueia e o que só avisa.</summary>
public static class DraftChecks
{
    public static List<CheckItem> Run(WorldDraft d, DraftSources src, Func<float, float, float>? groundAt, IEnumerable<(Vector3 Pos, string What)> points)
    {
        var list = new List<CheckItem>();
        var pts = points.ToList();
        EcwldFile? ecw = null;
        foreach (var o in d.Pieces.Values.SelectMany(p => p.Objects.Where(o => o.Changed).Concat(p.Added)).Where(o => !o.Deleted && o.Kind is ObjKind.Building or ObjKind.Model or ObjKind.Tree))
        {
            string name = Path.GetFileName(o.Source.Length > 0 ? o.Source : o.Key);
            var t = o.Now;
            int piece = src.PieceAt(t.Pos.X, t.Pos.Z);
            if (piece == 0) { list.Add(new(true, string.Format(Loc.T("obj.chkOut", "{0}: ficou fora do mapa."), name), o.Key)); continue; }
            // qualquer objeto pode ir para outro pedaço de 1 km (o salvar leva as referências aos blocos novos); só precisa de
            // bloco de cenário no lugar, senão o jogo não carrega nada ali
            try { ecw ??= new EcwldFile(File.ReadAllBytes(src.EcwldPath)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { list.Add(new(true, ex.Message, o.Key)); continue; }
            if (DraftSaver.BlockAt(ecw, t.Pos) is null)
            {
                list.Add(new(true, o.Kind == ObjKind.Tree
                    ? string.Format(Loc.T("obj.chkNoBlock", "{0}: nesse lugar não há bloco de cenário; o jogo não mostraria a árvore."), name)
                    : string.Format(Loc.T("obj.chkNoBlockObj", "{0}: nesse lugar não há bloco de cenário; o jogo não mostraria o objeto."), name), o.Key));
                continue;
            }
            var (mn, mx) = WorldBox(o);
            if (groundAt is not null)
            {
                float g = groundAt(t.Pos.X, t.Pos.Z);
                if (mn.Y > g + 2) list.Add(new(false, string.Format(Loc.T("obj.chkFloat", "{0}: parece flutuando ({1:0.0} m acima do chão)."), name, mn.Y - g), o.Key));
                else if (mx.Y < g - 0.5f) list.Add(new(false, string.Format(Loc.T("obj.chkBuried", "{0}: parece enterrado."), name), o.Key));
            }
            if (o.Solid)
            {
                // uma linha por objeto, agrupada pelo nome (usuário 09/11: uma pedra em cima de um acampamento dava 70 linhas
                // iguais e a janela passava da tela, sem os botões)
                var hits = pts.Where(p => p.Pos.X >= mn.X - 0.5f && p.Pos.X <= mx.X + 0.5f && p.Pos.Y >= mn.Y - 0.5f && p.Pos.Y <= mx.Y + 0.5f && p.Pos.Z >= mn.Z - 0.5f && p.Pos.Z <= mx.Z + 0.5f)
                    .GroupBy(p => p.What).OrderByDescending(g => g.Count()).ToList();
                if (hits.Count == 1 && hits[0].Count() == 1)
                    list.Add(new(false, string.Format(Loc.T("obj.chkOnTop", "{0}: está em cima de {1}."), name, hits[0].Key), o.Key));
                else if (hits.Count > 0)
                {
                    string names = string.Join(", ", hits.Take(4).Select(g => g.Count() > 1 ? g.Key + " ×" + g.Count() : g.Key)) + (hits.Count > 4 ? ", …" : "");
                    list.Add(new(false, string.Format(Loc.T("obj.chkOnTopN", "{0}: está em cima de {1} criatura(s) ({2})."), name, hits.Sum(g => g.Count()), names), o.Key));
                }
            }
        }
        return list;
    }

    /// <summary>Caixa no mundo (8 cantos da caixa local girados e escalados); sem caixa, o próprio ponto.</summary>
    /// <summary>
    /// Construções postas, mexidas ou apagadas: no lugar antigo e no novo (caixa + 4 m), refaz o movemap\N.rmap do servidor
    /// (parede da construção na altura de um personagem = bloqueado; saiu e o chão não é íngreme = livre). A sombra do chão fica com o usuário (pintura da textura; refazer a luz gravada destoava: teste do usuário 09/11).
    /// </summary>
    /// <summary>
    /// Árvores mexidas na passagem dos monstros (.rmap; ver TreePassage): raio de cada tipo aprendido das originais do
    /// mapa; apagada/lugar antigo reabre, nova/lugar novo fecha. <paramref name="ecwBase"/> = .ecwld antes deste salvar.
    /// </summary>
    internal static void TreeEffects(SavePlan plan, DraftSources src, List<DraftObject> edits, List<DraftObject> news, byte[] ecwBase, BhtFile? bht)
    {
        string mapDir = Path.GetDirectoryName(src.EcwldPath)!;
        if (src.ServerConfigDir is not { } sd || TerrainFile.Open(mapDir) is not { } t) return;
        var sc = new WorldScene(ecwBase);
        float pm = t.CellsPerPiece * t.Cell;
        var rm = new Dictionary<int, byte[]?>();
        string RmPath(int piece) => Path.Combine(sd, src.Map, "movemap", $"{piece}.rmap");
        byte[]? Rm(int piece)
        {
            if (rm.TryGetValue(piece, out var b)) return b;
            var path = RmPath(piece);
            b = plan.Files.TryGetValue(path, out var mem) ? mem : File.Exists(path) ? File.ReadAllBytes(path) : null;
            return rm[piece] = b;
        }
        (float X0, float ZS) Origin(int piece) { int pr = (piece - 1) / t.Cols, pc = (piece - 1) % t.Cols; return (-t.Width / 2 + pc * pm, t.Height / 2 - (pr + 1) * pm); }
        bool? Open(float x, float z)
        {
            if (t.PieceAt(x, z) is not (> 0 and var piece) || Rm(piece) is not { } b) return null;
            var (w, _, iw, ih, px) = MoveMapFile.Header(b); var (x0, zs) = Origin(piece);
            int u = (int)MathF.Floor((x - x0) / px), v = (int)MathF.Floor((z - zs) / px);
            return u < 0 || v < 0 || u >= iw || v >= ih ? null : MoveMapFile.Reach(b, w, u, v);
        }
        // raio por tipo (.spt), das árvores originais do mapa (até 120 de cada)
        var byType = new Dictionary<string, List<(float X, float Z)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tr in sc.Trees)
        {
            if (tr.Type < 0 || tr.Type >= sc.TreeTypes.Count) continue;
            var k = sc.TreeTypes[tr.Type].Spt;
            if (!byType.TryGetValue(k, out var l)) byType[k] = l = new();
            if (l.Count < 120) l.Add((tr.X, tr.Z));
        }
        var radius = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        float R(string spt) => radius.TryGetValue(spt, out var r) ? r : radius[spt] = TreePassage.Radius(byType.TryGetValue(spt, out var l) ? l : [], Open);

        // pegada do tronco e das raízes de cada tipo (só das árvores mexidas; as que ficam usam o círculo, como estão no .rmap)
        // tipo que as árvores originais deixam passar (raio 0: bambu) continua sem pegada
        IReadOnlyList<(float Dx, float Dz)>? Foot(string spt) => R(spt) > 0 ? MeshFootprint.Tree(src.ElementDir, spt) : null;
        var gone = new List<(float X, float Z, float R, IReadOnlyList<(float Dx, float Dz)>? Foot)>(); var come = new List<(float X, float Z, float R, IReadOnlyList<(float Dx, float Dz)>? Foot)>();
        var editedKeys = new HashSet<string>(edits.Select(o => o.Key));
        foreach (var o in edits)
        {
            float r = R(o.Source);
            bool moved = (o.Original.Pos - o.Now.Pos).LengthSquared() > 1e-4f;
            if (o.Deleted || moved) gone.Add((o.Original.Pos.X, o.Original.Pos.Z, r, Foot(o.Source)));
            if (!o.Deleted && moved) come.Add((o.Now.Pos.X, o.Now.Pos.Z, r, Foot(o.Source)));
        }
        foreach (var o in news) come.Add((o.Now.Pos.X, o.Now.Pos.Z, R(o.Source), Foot(o.Source)));
        if (gone.Count == 0 && come.Count == 0) return;
        // árvores que ficam perto (o círculo delas não reabre)
        var all = gone.Concat(come).ToList();
        static float Reach(float r, IReadOnlyList<(float Dx, float Dz)>? foot) => foot is { Count: > 0 } ? MathF.Max(r, foot.Max(f => MathF.Max(MathF.Abs(f.Dx), MathF.Abs(f.Dz)))) : r;
        float far = all.Max(c => Reach(c.R, c.Foot));
        float bx0 = all.Min(c => c.X) - 10 - far, bx1 = all.Max(c => c.X) + 10 + far, bz0 = all.Min(c => c.Z) - 10 - far, bz1 = all.Max(c => c.Z) + 10 + far;
        var stay = new List<(float X, float Z, float R, IReadOnlyList<(float Dx, float Dz)>? Foot)>();
        for (int i = 0; i < sc.Trees.Count; i++)
        {
            var tr = sc.Trees[i];
            if (tr.X < bx0 || tr.X > bx1 || tr.Z < bz0 || tr.Z > bz1 || editedKeys.Contains($"tree:{i}") || tr.Type < 0 || tr.Type >= sc.TreeTypes.Count) continue;
            stay.Add((tr.X, tr.Z, R(sc.TreeTypes[tr.Type].Spt), null));
        }
        // paredes das construções (só para reabrir): o map.bht já aberto pelo salvar, ou lido agora
        BrushSet? walls = null;
        if (gone.Count > 0 && (bht ?? (src.BhtPath is { } bp && File.Exists(bp) ? new BhtFile(File.ReadAllBytes(bp)) : null)) is { } bf)
            walls = BrushSet.Near(bf, bx0, bz0, bx1, bz1);
        var pieces = new HashSet<int>();
        foreach (var (x, z, r0, foot) in all)
            foreach (var (dx, dz) in Reach(r0, foot) is var r ? new[] { (-r - 1, -r - 1), (r + 1, -r - 1), (-r - 1, r + 1), (r + 1, r + 1) } : [])
                if (t.PieceAt(x + dx, z + dz) is > 0 and var p) pieces.Add(p);
        int total = 0;
        foreach (int piece in pieces)
        {
            if (Rm(piece) is not { } b) continue;
            var (x0, zs) = Origin(piece);
            var (data, n) = TreePassage.Apply(b, x0, zs, gone, come, stay, t.HeightAt, walls);
            if (n == 0) continue;
            MoveMapFile.Header(data);   // relê o cabeçalho: arquivo inteiro ainda certo
            plan.Files[RmPath(piece)] = data; rm[piece] = data; total += n;
        }
        if (total > 0) plan.Summary.Add(string.Format(Loc.T("obj.treePassSummary", "Passagem dos monstros em volta das árvores refeita: {0} metro(s) (árvore nova os monstros contornam; apagada eles passam)."), total));
    }

    internal static void BuildingEffects(SavePlan plan, DraftSources src, List<DraftObject> objs, BhtFile oldB, BhtFile newB)
    {
        if (objs.Count == 0) return;
        string mapDir = Path.GetDirectoryName(src.EcwldPath)!;
        var t = TerrainFile.Open(mapDir);
        if (t is null) return;
        var rects = new List<(float X0, float Z0, float X1, float Z1)>();
        foreach (var o in objs)
        {
            var (mn, mx) = WorldBox(o);
            var d = o.Original.Pos - o.Now.Pos;   // lugar antigo (mesma caixa deslocada; giro/tamanho ficam na folga)
            rects.Add((mn.X - 4, mn.Z - 4, mx.X + 4, mx.Z + 4));
            if (o.CopyOf is null && d.LengthSquared() > 1e-4f) rects.Add((mn.X + d.X - 4, mn.Z + d.Z - 4, mx.X + d.X + 4, mx.Z + d.Z + 4));
        }
        float pm = t.CellsPerPiece * t.Cell;
        HashSet<int> PiecesOf(IEnumerable<(float X0, float Z0, float X1, float Z1)> rs)
        {
            var set = new HashSet<int>();
            foreach (var r in rs)
                for (float z = r.Z0; z <= r.Z1 + pm; z += pm) for (float x = r.X0; x <= r.X1 + pm; x += pm)
                    if (t.PieceAt(MathF.Min(x, r.X1), MathF.Min(z, r.Z1)) is > 0 and var p) set.Add(p);
            return set;
        }
        // malha das construções mexidas: lugar antigo (as que já existiam) e lugar novo (as que ficam); ver MeshObstacles
        var oldM = new MeshObstacles(); var newM = new MeshObstacles();
        foreach (var o in objs.Where(o => o.Kind == ObjKind.Building))
        {
            try
            {
                var bytes = DraftSaver.ReadBmd(src, o.CopyOf ?? o.Key);
                if (o.CopyOf is null && !o.Key.StartsWith("new:", StringComparison.Ordinal)) oldM.AddBuilding(bytes, o.Original);
                if (!o.Deleted) newM.AddBuilding(bytes, o.Now);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
            {
                AppLog.Error("passagem: malha de " + (o.CopyOf ?? o.Key), ex);   // fica só com os brushes, como antes
            }
        }
        // passagem (servidor)
        int moved = 0, floors = 0;
        if (src.ServerConfigDir is { } sd)
            foreach (int piece in PiecesOf(rects))
            {
                var rp = Path.Combine(sd, src.Map, "movemap", $"{piece}.rmap");
                if (!File.Exists(rp)) continue;
                int pr = (piece - 1) / t.Cols, pc = (piece - 1) % t.Cols;
                float px0 = -t.Width / 2 + pc * pm, pzS = t.Height / 2 - (pr + 1) * pm;
                var bx = rects.Aggregate((a, b) => (Math.Min(a.X0, b.X0), Math.Min(a.Z0, b.Z0), Math.Max(a.X1, b.X1), Math.Max(a.Z1, b.Z1)));
                var ob = BrushSet.Near(oldB, bx.X0, bx.Z0, bx.X1, bx.Z1); var nb = BrushSet.Near(newB, bx.X0, bx.Z0, bx.X1, bx.Z1);
                // passagem com pisos (pontes, escadas, andares da construção): .rmap + .dhmap juntos
                var dp = Path.ChangeExtension(rp, ".dhmap");
                byte[]? dhOld = File.Exists(dp) && new FileInfo(dp).Length >= 36 ? File.ReadAllBytes(dp) : null;
                var (data, dhNew, n) = MoveMapFile.UpdateWithFloors(File.ReadAllBytes(rp), dhOld, px0, pzS, rects, t.HeightAt, ob, nb, oldM, newM);
                if (n > 0) { plan.Files[rp] = data; moved += n; }
                if (dhNew is not null) { plan.Files[dp] = dhNew; floors++; }
            }
        if (moved > 0) plan.Summary.Add(string.Format(Loc.T("obj.rmapSummary", "Passagem dos NPCs refeita em volta das construções: {0} metro(s)."), moved));
        if (floors > 0) plan.Summary.Add(Loc.T("obj.floorsSummary", "Pisos das construções (pontes, escadas, andares) atualizados para os monstros."));
        // voo dos monstros (airmap/N.octr): cubo que passou a encostar numa parede vira borda (não se voa por ele); o que
        // deixou de encostar fica livre. Só construções mexem: o gs vê o chão à parte (GlobalSPMap CanPosPass).
        int flyCells = 0;
        if (src.ServerConfigDir is { } sd2)
            foreach (int piece in PiecesOf(rects))
            {
                var op = Path.Combine(sd2, src.Map, "airmap", $"{piece}.octr");
                if (!File.Exists(op) || new FileInfo(op).Length <= 29) continue;   // pedaço sem voo
                try
                {
                    var oct = OctreeFile.Read(File.ReadAllBytes(op));
                    var ab = rects.Aggregate((a, b) => (Math.Min(a.X0, b.X0), Math.Min(a.Z0, b.Z0), Math.Max(a.X1, b.X1), Math.Max(a.Z1, b.Z1)));
                    var ob = BrushSet.Near(oldB, ab.X0 - 8, ab.Z0 - 8, ab.X1 + 8, ab.Z1 + 8); var nb = BrushSet.Near(newB, ab.X0 - 8, ab.Z0 - 8, ab.X1 + 8, ab.Z1 + 8);
                    flyCells += oct.Refresh(rects, (x, z) => t.HeightAt(x, z), ob, nb);
                    if (oct.Dirty) { var nbytes = oct.Write(); _ = OctreeFile.Read(nbytes); plan.Files[op] = nbytes; }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    AppLog.Error("objetos: voo do pedaço " + piece, ex);
                    plan.Summary.Add(string.Format(Loc.T("obj.flyFail", "Voo dos monstros do pedaço {0} não refeito (arquivo do servidor ilegível; veja o log)."), piece));
                }
            }
        if (flyCells > 0) plan.Summary.Add(string.Format(Loc.T("obj.flySummary", "Voo dos monstros refeito em volta das construções: {0} cubo(s) de 4 m."), flyCells));
    }

    public static (Vector3 Min, Vector3 Max) WorldBox(DraftObject o)
    {
        var t = o.Now;
        var (lmn, lmx) = o.LocalBox;
        if (lmn == lmx) return (t.Pos, t.Pos);
        var f = Vector3.Normalize(t.Dir); var u = Vector3.Normalize(t.Up); var r = Vector3.Cross(u, f);
        float s = t.Scale is > 0 and < 1000 ? t.Scale : 1;
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        for (int k = 0; k < 8; k++)
        {
            var c = new Vector3((k & 1) == 0 ? lmn.X : lmx.X, (k & 2) == 0 ? lmn.Y : lmx.Y, (k & 4) == 0 ? lmn.Z : lmx.Z) * s;
            var w = r * c.X + u * c.Y + f * c.Z + t.Pos;
            mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
        }
        return (mn, mx);
    }
}
