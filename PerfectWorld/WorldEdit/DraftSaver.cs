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

    static void TransformBrush(byte[] d, int at, Quaternion r, Vector3 p0, Vector3 p1)
    {
        if (!IsIdentity(r)) ObjectFormats.RotateBrush(d, at, r, p0);
        var t = p1 - p0;
        BhtFile.MoveAt(d, at, t.X, t.Y, t.Z);
    }

    public static SavePlan Plan(WorldDraft d, DraftSources src, byte[]? ecwldBase = null, byte[]? ecbsdBase = null)
    {
        var changed = d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Changed && o.ReadOnlyWhy is null && o.Kind is ObjKind.Building or ObjKind.Model).ToList();
        var plan = new SavePlan(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());
        if (changed.Count == 0) return plan;

        var ecw = new EcwldFile(ecwldBase ?? File.ReadAllBytes(src.EcwldPath));
        var bsd = (byte[])(ecbsdBase ?? File.ReadAllBytes(src.EcbsdPath)).Clone();
        bool ecwDirty = false, bsdDirty = false, bhtDirty = false;
        BhtFile? bht = null; Dictionary<string, List<int>>? keys = null;
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
                bht = new BhtFile(File.ReadAllBytes(path)); keys = bht.Keys();
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

            if (MathF.Abs(o.Now.Scale - o.Original.Scale) > 1e-6f) throw new InvalidDataException(Loc.T("obj.scaleLater", "Mudar o tamanho chega na próxima etapa da ferramenta."));
            var (r, p0, p1) = Rigid(o.Original, o.Now);
            Vector3 Map(Vector3 p) => (IsIdentity(r) ? p - p0 : Vector3.Transform(p - p0, r)) + p1;
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
                var nb = ObjectFormats.WithBmdTransform(bf, f == o.Key ? o.Now : t with { Pos = Map(t.Pos), Dir = Turn(t.Dir), Up = Turn(t.Up) });
                var at = BmdModel.CdBrushes(nb)!;
                foreach (int a in at) TransformBrush(nb, a, r, p0, p1);
                if (BmdModel.Read(nb) is null || BmdModel.CdBrushes(nb)?.Count != at.Count) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
                plan.PckFiles[f] = nb;
                foreach (int off in ObjectFormats.OrnamentRefsOf(ecw, bsd, f).Select(x => x.Offset).Distinct())
                {
                    ObjectFormats.MoveOrnament(bsd, off, Map, Turn);
                    bsdDirty = true;
                }
                done.Add(f);
            }
            if (sel.Count > 0)
            {
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
            else { foreach (var (bl, i) in hits) ObjectFormats.WriteEcModel(bl.Items[(int)EcwldFile.Kind.EcModel][i], o.Now); Count(o.Piece, "mmove"); }
            ecwDirty = true;
        }

        if (ecwDirty) { var e = ecw.Write(); _ = new EcwldFile(e); plan.Files[src.EcwldPath] = e; }
        if (bsdDirty) plan.Files[src.EcbsdPath] = bsd;
        if (bhtDirty && bht is not null)
        {
            var bb = drop.Count > 0 ? bht.Without(drop) : bht.Write();
            _ = new BhtFile(bb);   // relê: formato inteiro ainda certo
            plan.Files[src.BhtPath!] = bb;
        }
        foreach (var g in count.GroupBy(k => k.Key.Piece).OrderBy(g => g.Key))
        {
            var parts = new List<string>();
            void Say(string what, string one, string many) { if (count.GetValueOrDefault((g.Key, what)) is var n and > 0) parts.Add(string.Format(n == 1 ? one : many, n)); }
            Say("move", Loc.T("obj.sumMove1", "1 construção mexida"), Loc.T("obj.sumMoveN", "{0} construções mexidas"));
            Say("del", Loc.T("obj.sumDel1", "1 construção apagada"), Loc.T("obj.sumDelN", "{0} construções apagadas"));
            Say("mmove", Loc.T("obj.sumMMove1", "1 modelo mexido"), Loc.T("obj.sumMMoveN", "{0} modelos mexidos"));
            Say("mdel", Loc.T("obj.sumMDel1", "1 modelo apagado"), Loc.T("obj.sumMDelN", "{0} modelos apagados"));
            plan.Summary.Add(string.Format(Loc.T("obj.sumPiece", "Pedaço {0}: {1}."), g.Key, string.Join(", ", parts)));
        }
        return plan;
    }

    /// <summary>
    /// Grava tudo ou nada: confere que nenhum arquivo está preso; cópia de segurança dos arquivos; grava os arquivos e
    /// por último o pacote (o maior e mais demorado); se algo falhar, os já gravados voltam das cópias.
    /// </summary>
    public static SaveResult Write(SavePlan p, DraftSources src)
    {
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
        try { foreach (var f in p.Files.Keys) baks[f] = File.Exists(f) ? BackupStore.Save(f) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("objetos: cópia de segurança", ex);
            return new SaveResult(SaveKind.Failed, Loc.T("obj.noBackup", "Não deu para fazer a cópia de segurança; nada foi gravado."));
        }
        var written = new List<string>();
        try
        {
            foreach (var (f, bytes) in p.Files)
            {
                if (FailAfter >= 0 && written.Count >= FailAfter) throw new IOException("falha simulada (teste)");
                AtomicFileSave.Write(f, bytes);
                written.Add(f);
            }
            if (FailAfter >= 0 && written.Count >= FailAfter) throw new IOException("falha simulada (teste)");
            if (FailWith is { } fw) throw fw();
            if (p.PckFiles.Count > 0) PckWriter.Write(src.LitPath, p.PckFiles);
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
        foreach (var o in d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Changed && !o.Deleted && o.Kind is ObjKind.Building or ObjKind.Model))
        {
            string name = Path.GetFileName(o.Source.Length > 0 ? o.Source : o.Key);
            var t = o.Now;
            int piece = src.PieceAt(t.Pos.X, t.Pos.Z);
            if (piece == 0) { list.Add(new(true, string.Format(Loc.T("obj.chkOut", "{0}: ficou fora do mapa."), name), o.Key)); continue; }
            if (piece != o.Piece)
            { list.Add(new(true, string.Format(Loc.T("obj.chkPiece", "{0}: saiu do pedaço de 1 km dela; o jogo só carrega a construção no pedaço onde ela nasceu."), name), o.Key)); continue; }
            var (mn, mx) = WorldBox(o);
            if (groundAt is not null)
            {
                float g = groundAt(t.Pos.X, t.Pos.Z);
                if (mn.Y > g + 2) list.Add(new(false, string.Format(Loc.T("obj.chkFloat", "{0}: parece flutuando ({1:0.0} m acima do chão)."), name, mn.Y - g), o.Key));
                else if (mx.Y < g - 0.5f) list.Add(new(false, string.Format(Loc.T("obj.chkBuried", "{0}: parece enterrado."), name), o.Key));
            }
            if (o.Solid)
                foreach (var (pos, what) in pts)
                    if (pos.X >= mn.X - 0.5f && pos.X <= mx.X + 0.5f && pos.Y >= mn.Y - 0.5f && pos.Y <= mx.Y + 0.5f && pos.Z >= mn.Z - 0.5f && pos.Z <= mx.Z + 0.5f)
                        list.Add(new(false, string.Format(Loc.T("obj.chkOnTop", "{0}: está em cima de {1}."), name, what), o.Key));
        }
        return list;
    }

    /// <summary>Caixa no mundo (8 cantos da caixa local girados e escalados); sem caixa, o próprio ponto.</summary>
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
