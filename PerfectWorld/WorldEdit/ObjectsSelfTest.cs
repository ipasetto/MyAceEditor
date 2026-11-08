using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Objetos do mundo sem janela (--objects-test &lt;element&gt; &lt;config do servidor&gt; &lt;saída&gt;): estudo dos formatos
/// (girar e apagar construção, modelos do .ecwld, limites) e, depois, o rascunho e o salvar. Grava só em cópias.
/// </summary>
public static class ObjectsSelfTest
{
    sealed class Report(List<string> lines)
    {
        public int Failed { get; private set; }
        public void Check(string name, bool ok, string why = "")
        {
            lines.Add(ok ? $"OK {name}" + (why.Length > 0 ? $": {why}" : "") : $"FALHA {name}: {why}");
            if (!ok) Failed++;
        }
        public void Note(string s) => lines.Add("   " + s);
    }

    static readonly string[] StudyMaps = ["world", "a05", "a26"];

    public static void Run(string element, string? config, string output)
    {
        var lines = new List<string>();
        var r = new Report(lines);
        try { Draft(r); }
        catch (Exception ex) { r.Check("rascunho-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Draft2(r); }
        catch (Exception ex) { r.Check("rascunho2-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Load(r, element, config); }
        catch (Exception ex) { r.Check("leitura-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Save(r, element, config); }
        catch (Exception ex) { r.Check("salvar-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()); }
        try { Study2(r, element, config); }
        catch (Exception ex) { r.Check("estudo2-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Study(r, element, config); }
        catch (Exception ex) { r.Check("estudo-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        lines.Add($"falhas: {r.Failed}");
        File.WriteAllLines(output, lines);
    }


    // ---------------- rascunho (Task 2) ----------------
    static DraftObject Obj(string key, int piece = 1) => new()
    {
        Key = key, Kind = ObjKind.Building, Piece = piece, Source = key,
        Original = new ObjTransform(new Vector3(10, 20, 30), Vector3.UnitZ, Vector3.UnitY, 1),
    };

    static WorldDraft OneObject(out DraftObject o)
    {
        var d = new WorldDraft("teste");
        o = Obj("a");
        d.Put(new DraftPiece(1) { Loaded = true, Objects = { o } });
        return d;
    }

    static void Draft(Report r)
    {
        var d = OneObject(out var o);
        var to = o.Original with { Pos = new Vector3(11, 20, 31) };
        string? why = d.Apply(new MoveOp("a", to));
        r.Check("draft-aplica", why is null && o.Now == to && d.Dirty && d.UndoCount == 1, why ?? "");
        r.Check("draft-desfaz", d.Undo() && o.Now == o.Original && !d.Dirty && d.RedoCount == 1);
        r.Check("draft-refaz", d.Redo() && o.Now == to && d.Dirty);

        why = d.Apply(new DeleteOp("a"));
        bool del = why is null && o.Deleted;
        r.Check("draft-apaga-e-volta", del && d.Undo() && !o.Deleted && d.Apply(new DeleteOp("a")) is null && d.Apply(new RestoreOp("a")) is null && !o.Deleted);

        var ro = OneObject(out var o2); o2.ReadOnlyWhy = "pedaço só para olhar";
        why = ro.Apply(new MoveOp("a", to));
        r.Check("draft-recusa-so-olhar", why is not null && ro.UndoCount == 0 && o2.Now == o2.Original, why ?? "aceitou");

        var f = OneObject(out var o3);
        string? nan = f.Apply(new MoveOp("a", o3.Original with { Pos = new Vector3(float.NaN, 0, 0) }));
        string? zero = f.Apply(new MoveOp("a", o3.Original with { Scale = 0 }));
        string? flat = f.Apply(new MoveOp("a", o3.Original with { Dir = Vector3.Zero }));
        string? none = f.Apply(new MoveOp("zz", o3.Original));
        r.Check("draft-recusa-faixa", nan is not null && zero is not null && flat is not null && none is not null && f.UndoCount == 0 && o3.Now == o3.Original);

        var u = OneObject(out var o4);
        var rnd = new Random(7); ObjTransform last = o4.Original;
        for (int i = 0; i < 50; i++)
        {
            last = o4.Original with { Pos = new Vector3(rnd.Next(-500, 500), rnd.Next(0, 300), rnd.Next(-500, 500)) };
            u.Apply(new MoveOp("a", last));
        }
        while (u.Undo()) { }
        bool back = o4.Now == o4.Original;
        while (u.Redo()) { }
        r.Check("undo-redo-salva-igual", back && o4.Now == last && u.UndoCount == 50);

        int fired = 0; var ev = OneObject(out _); ev.Changed += () => fired++;
        ev.Apply(new MoveOp("a", to)); ev.Undo(); ev.Redo();
        r.Check("draft-avisa-mudanca", fired == 3, $"{fired} avisos");

        var k = new WorldDraft("teste");
        var dirty = Obj("suja", 1); var clean = Obj("limpa", 2);
        k.Put(new DraftPiece(1) { Loaded = true, Objects = { dirty } });
        k.Put(new DraftPiece(2) { Loaded = true, Objects = { clean } });
        k.Apply(new MoveOp("suja", dirty.Original with { Pos = new Vector3(1, 2, 3) }));
        k.Apply(new MoveOp("limpa", clean.Original with { Pos = new Vector3(4, 5, 6) })); k.Undo();   // limpa de novo, com refazer
        k.Retain([3]);
        r.Check("pedaco-sujo-fica", k.Pieces.ContainsKey(1) && !k.Pieces.ContainsKey(2) && k.Find("suja") is not null && k.Find("limpa") is null && k.RedoCount == 0 && k.UndoCount == 1,
            $"pedaços {string.Join(",", k.Pieces.Keys)}, desfazer {k.UndoCount}, refazer {k.RedoCount}");
    }


    // ---------------- leitura dos pedaços (Task 3) ----------------
    static DraftSources Sources(string element, string map, string? config)
    {
        var t = TerrainFile.Open(Path.Combine(element, "maps", map)) ?? throw new InvalidDataException("terreno de " + map);
        return new DraftSources(element, map, config, t.PieceAt, t.Rows * t.Cols);
    }

    static void Load(Report r, string element, string? config)
    {
        var src = Sources(element, "world", config);
        using var lit = new PckArchive(Path.Combine(element, "litmodels.pck"));
        var perFolder = lit.Entries.Where(e => e.Path.StartsWith(@"litmodels\world\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Path.Split('\\')).Where(p => p.Length == 4 && int.TryParse(p[2], out _)).GroupBy(p => int.Parse(p[2])).ToDictionary(g => g.Key, g => g.Count());
        var pieces = new List<DraftPiece>();
        int wrong = 0; string? firstWrong = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= src.PieceCount; i++)
        {
            var p = DraftLoader.ReadPiece(src, i);
            pieces.Add(p);
            int b = p.Objects.Count(o => o.Kind == ObjKind.Building), want = perFolder.GetValueOrDefault(i);
            if (b != want || p.ReadOnlyWhy is not null) { wrong++; firstWrong ??= $"pedaço {i}: {b} de {want} construções, {p.ReadOnlyWhy}"; }
        }
        r.Check("le-pedacos-world", wrong == 0, $"{src.PieceCount} pedaços, {pieces.Sum(p => p.Objects.Count)} objetos em {sw.ElapsedMilliseconds} ms" + (firstWrong is null ? "" : "; " + firstWrong));

        var ecw = new EcwldFile(File.ReadAllBytes(src.EcwldPath));
        int Distinct(EcwldFile.Kind k) => ecw.Blocks.SelectMany(b => b.Items[(int)k]).Select(EcwldFile.IdOf).Distinct().Count();
        var counts = new[] { (ObjKind.Model, EcwldFile.Kind.EcModel), (ObjKind.Effect, EcwldFile.Kind.Effect), (ObjKind.Sound, EcwldFile.Kind.Sound), (ObjKind.Critter, EcwldFile.Kind.Critter) }
            .Select(x => (x.Item1, Got: pieces.Sum(p => p.Objects.Count(o => o.Kind == x.Item1)) + DraftLoader.Outside(src, x.Item1), Want: Distinct(x.Item2))).ToList();
        r.Check("le-modelos-e-cenario", counts.All(c => c.Got == c.Want), string.Join(", ", counts.Select(c => $"{c.Item1} {c.Got}/{c.Want}")));

        var temp = Path.Combine(Path.GetTempPath(), "myace-objtest-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(temp);
            var bad = Path.Combine(temp, "world.ecwld");
            var bytes = File.ReadAllBytes(src.EcwldPath);
            File.WriteAllBytes(bad, bytes[..(bytes.Length / 2)]);
            var badSrc = new DraftSources(element, "world", config, src.PieceAt, src.PieceCount) { EcwldPath = bad };
            int busy = perFolder.OrderByDescending(kv => kv.Value).First().Key;
            DraftPiece? p = null; Exception? ex = null;
            try { p = DraftLoader.ReadPiece(badSrc, busy); } catch (Exception e) { ex = e; }
            r.Check("pedaco-ruim-so-olhar", ex is null && p is { Loaded: true, ReadOnlyWhy: not null } && p.Objects.Count(o => o.Kind == ObjKind.Building) == perFolder[busy],
                ex?.Message ?? $"pedaço {busy}: {p?.ReadOnlyWhy}");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }

        var a05 = Sources(element, "a05", config);
        var all = Enumerable.Range(1, a05.PieceCount).SelectMany(i => DraftLoader.ReadPiece(a05, i).Objects).Where(o => o.Kind == ObjKind.Building).ToList();
        r.Check("solido-pela-colisao", config is null || (all.Any(o => o.Solid) && all.Where(o => o.Solid).All(o => o.ReadOnlyWhy is null)), $"a05: {all.Count(o => o.Solid)} de {all.Count} construções com colisão no map.bht");
    }


    // ---------------- salvar (Task 6) ----------------
    static WorldDraft DraftOf(DraftSources src, params int[] pieces)
    {
        var d = new WorldDraft(src.Map);
        foreach (int i in pieces) d.Put(DraftLoader.ReadPiece(src, i));
        return d;
    }

    /// <summary>Faixas de bytes diferentes entre dois arrays do mesmo tamanho.</summary>
    static List<(int From, int To)> Diff(byte[] a, byte[] b)
    {
        var l = new List<(int, int)>();
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) { int j = i; while (j + 1 < a.Length && a[j + 1] != b[j + 1]) j++; l.Add((i, j)); i = j; }
        return l;
    }

    static string Hash(string f) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f)));

    static void Save(Report r, string element, string? config)
    {
        if (config is null) { r.Note("sem servidor: salvar não conferido"); return; }
        var a05 = Sources(element, "a05", config);
        var all = Enumerable.Range(1, a05.PieceCount).ToArray();

        var empty = DraftOf(a05, all);
        r.Check("plano-sem-mudanca-vazio", DraftSaver.Plan(empty, a05) is { PckFiles.Count: 0, Files.Count: 0 });

        // mover + girar uma construção sem gêmea
        var d = DraftOf(a05, all);
        var bmdKeys = d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Kind == ObjKind.Building && o.Solid && o.ReadOnlyWhy is null).Select(o => o.Key).ToList();
        string? single = null, twin = null; List<string>? twinGroup = null;
        foreach (var k in bmdKeys) { var g = DraftSaver.TwinsOf(a05, k); if (g.Count == 1) single ??= k; else if (twin is null) { twin = k; twinGroup = g; } }
        var o1 = d.Find(single!)!;
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var to1 = o1.Original with { Pos = o1.Original.Pos + new Vector3(4, 0, -3), Dir = Vector3.Transform(o1.Original.Dir, rot), Up = Vector3.Transform(o1.Original.Up, rot) };
        d.Apply(new MoveOp(single!, to1));
        var plan = DraftSaver.Plan(d, a05);
        var nb = plan.PckFiles.GetValueOrDefault(single!);
        var bhtOld = File.ReadAllBytes(a05.BhtPath!);
        var bhtNew = plan.Files.GetValueOrDefault(a05.BhtPath!);
        bool headerOk = nb is not null && ObjectFormats.ReadBmd(nb) == to1;
        var newKeys = nb is null ? new List<string>() : BmdModel.CdBrushes(nb)!.Select(o => BhtFile.KeyAt(nb, o)).ToList();
        bool brushesOk = bhtNew is not null && newKeys.Count > 0 && newKeys.All(new BhtFile(bhtNew).Keys().ContainsKey) && bhtNew.Length == bhtOld.Length;
        var bsdOld = File.ReadAllBytes(a05.EcbsdPath);
        var bsdNew = plan.Files.GetValueOrDefault(a05.EcbsdPath);
        var bsdDiff = bsdNew is null ? new List<(int From, int To)>() : Diff(bsdOld, bsdNew);
        bool bsdOk = bsdNew is not null && bsdNew.Length == bsdOld.Length && bsdDiff.Count > 0 && bsdDiff[^1].To - bsdDiff[0].From < 40;
        r.Check("plano-relido-igual", headerOk && brushesOk && bsdOk && !plan.Files.ContainsKey(a05.EcwldPath) && plan.Summary.Count > 0,
            $"{Path.GetFileName(single)}: cabeçalho={headerOk} brushes={brushesOk} ornamento={bsdOk} ({bsdDiff.Count} faixas) resumo=\"{string.Join(" / ", plan.Summary)}\"");

        // gêmeas: mexer uma leva as duas, e cada brush do map.bht anda uma vez só (procura no a05 e em pedaços do world)
        DraftSources twinSrc = a05; int[] twinPieces = all;
        if (twin is null)
            foreach (var (srcT, pcs) in new[] { (a05, all), (Sources(element, "world", config), new[] { 29, 30, 31, 37, 38, 39, 45, 46, 47, 20, 21, 22 }) })
            {
                var keysT = new BhtFile(File.ReadAllBytes(srcT.BhtPath!)).Keys();
                foreach (var pc in pcs)
                    foreach (var ob in DraftLoader.ReadPiece(srcT, pc).Objects.Where(x => x.Kind == ObjKind.Building && x.ReadOnlyWhy is null))
                    {
                        var g = DraftSaver.TwinsOf(srcT, ob.Key);
                        if (g.Count < 2) continue;
                        var bb = DraftSaver.ReadBmd(srcT, ob.Key);
                        if (BmdModel.CdBrushes(bb) is not { Count: > 0 } at || !at.All(o => keysT.ContainsKey(BhtFile.KeyAt(bb, o)))) continue;
                        twin = ob.Key; twinGroup = g; twinSrc = srcT; twinPieces = [pc]; break;
                    }
                if (twin is not null) break;
            }
        if (twin is not null)
        {
            var a05s = a05; a05 = twinSrc;
            bhtOld = File.ReadAllBytes(a05.BhtPath!);
            var dt = DraftOf(a05, twinPieces);
            var ot = dt.Find(twin)!;
            dt.Apply(new MoveOp(twin, ot.Original with { Pos = ot.Original.Pos + new Vector3(5, 0, 0) }));
            var pt = DraftSaver.Plan(dt, a05);
            var oldKeys = new BhtFile(bhtOld).Keys();
            var nk = new BhtFile(pt.Files[a05.BhtPath!]).Keys();
            var src0 = DraftSaver.ReadBmd(a05, twin);
            var orig = BmdModel.CdBrushes(src0)!.Select(o => BhtFile.KeyAt(src0, o)).ToList();
            var tw = pt.PckFiles[twin];
            var moved = BmdModel.CdBrushes(tw)!.Select(o => BhtFile.KeyAt(tw, o)).ToList();
            bool sameCount = orig.Count == moved.Count && orig.Zip(moved).All(x => nk.TryGetValue(x.Second, out var l) && l.Count == oldKeys[x.First].Count);
            r.Check("gemeas-uma-vez", sameCount && twinGroup!.All(pt.PckFiles.ContainsKey), $"{a05.Map} {Path.GetFileName(twin)}: {twinGroup!.Count} gêmeas, {moved.Count} brushes");
            a05 = a05s; bhtOld = File.ReadAllBytes(a05.BhtPath!);
        }
        else
        {
            var groups = Enumerable.Range(1, a05.PieceCount).SelectMany(i => DraftLoader.ReadPiece(a05, i).Objects).Where(x => x.Kind == ObjKind.Building)
                .Select(x => DraftSaver.TwinsOf(a05, x.Key)).Where(g => g.Count > 1).Select(g => string.Join("+", g.Select(Path.GetFileName))).Distinct().ToList();
            r.Note($"sem gêmeas com colisão no map.bht (a05 + 12 pedaços do world); grupos de gêmeas no a05 sem exigir colisão: {groups.Count} {string.Join("; ", groups.Take(3))}");
        }

        // apagar: some a referência do .ecwld e os brushes do map.bht
        var dd = DraftOf(a05, all);
        dd.Apply(new DeleteOp(single!));
        var pd = DraftSaver.Plan(dd, a05);
        var ecwNew = pd.Files.GetValueOrDefault(a05.EcwldPath);
        bool refGone = ecwNew is not null && ObjectFormats.OrnamentIdOf(new EcwldFile(ecwNew), bsdOld, single!) is null;
        bool brushGone = pd.Files.TryGetValue(a05.BhtPath!, out var bd) && new BhtFile(bd).Count < new BhtFile(bhtOld).Count;
        r.Check("plano-apaga", refGone && brushGone && !pd.PckFiles.ContainsKey(single!), $"referência saiu={refGone}, brushes saíram={brushGone}");

        // modelos do .ecwld (world tem 76)
        var world = Sources(element, "world", config);
        var mo = world.Scene.Value.Objects.First(o => o.Kind == ObjKind.Model && o.Piece > 0);
        var dm = DraftOf(world, mo.Piece);
        var om = dm.Find(mo.Key)!;
        var tom = om.Original with { Pos = om.Original.Pos + new Vector3(2, 0, 2) };
        dm.Apply(new MoveOp(mo.Key, tom));
        var pm = DraftSaver.Plan(dm, world);
        var wNew = pm.Files.GetValueOrDefault(world.EcwldPath);
        var hit = wNew is null ? null : new EcwldFile(wNew).Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.EcModel]).FirstOrDefault(i => DraftLoader.KeyOf(EcwldFile.Kind.EcModel, EcwldFile.IdOf(i)) == mo.Key);
        var dmp = DraftOf(world, mo.Piece); var omp = dmp.Find(mo.Key)!;
        int other = Enumerable.Range(1, world.PieceCount).First(i => i != mo.Piece && Enumerable.Range(0, 100).Any(k => world.PieceAt(-5000 + k * 100, 0) == i));
        var far = Enumerable.Range(0, 100).Select(k => new Vector3(-5000 + k * 100, omp.Original.Pos.Y, 0)).First(v => world.PieceAt(v.X, v.Z) == other);
        dmp.Apply(new MoveOp(mo.Key, omp.Original with { Pos = far }));
        r.Check("modelo-fora-do-pedaco-bloqueia", DraftChecks.Run(dmp, world, null, []).Any(c => c.Blocks && c.Key == mo.Key));
        r.Check("plano-modelo", hit is not null && ObjectFormats.ReadEcModel(hit) == tom && wNew!.Length == new FileInfo(world.EcwldPath).Length && pm.PckFiles.Count == 0);

        // conferências
        var dc = DraftOf(a05, all);
        var oc = dc.Find(single!)!;
        dc.Apply(new MoveOp(single!, oc.Original with { Pos = new Vector3(99999, 0, 99999) }));
        r.Check("fora-do-mapa-bloqueia", DraftChecks.Run(dc, a05, null, []).Any(c => c.Blocks && c.Key == single));
        var df = DraftOf(a05, all);
        var of = df.Find(single!)!;
        df.Apply(new MoveOp(single!, of.Original with { Pos = of.Original.Pos + new Vector3(0, 60, 0) }));
        var floats = DraftChecks.Run(df, a05, (x, z) => of.Original.Pos.Y - 1, []);
        r.Check("flutuando-avisa", floats.Any(c => !c.Blocks && c.Key == single), string.Join(" | ", floats.Select(c => c.Text)));
        var dn = DraftOf(a05, all);
        var on = dn.Find(single!)!;
        dn.Apply(new MoveOp(single!, on.Original with { Pos = on.Original.Pos + new Vector3(1, 0, 0) }));
        var npc = DraftChecks.Run(dn, a05, null, [(on.Original.Pos + new Vector3(1, 0.5f, 0), "Guarda da vila")]);
        r.Check("em-cima-de-npc-avisa", npc.Any(c => !c.Blocks && c.Key == single && c.Text.Contains("Guarda da vila")));

        // gravar: preso = nada muda; falha no meio = tudo volta; sem falha = grava
        var temp = Path.Combine(Path.GetTempPath(), "myace-objsave-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(temp);
            string pck = Path.Combine(temp, "litmodels.pck"), ecwT = Path.Combine(temp, "a05.ecwld"), bhtT = Path.Combine(temp, "map.bht");
            File.Copy(Path.Combine(element, "configs.pck"), pck);
            File.Copy(a05.EcwldPath, ecwT);
            File.Copy(a05.BhtPath!, bhtT);
            var fake = new SavePlan(new() { [@"litmodels\teste\1\x.bmd"] = [1, 2, 3] }, new() { [ecwT] = ecwNew!, [bhtT] = bd! }, ["teste"]);
            var tsrc = new DraftSources(temp, "teste", null, (_, _) => 1, 1);
            string[] targets = [pck, ecwT, bhtT];
            var before = targets.Select(Hash).ToList();
            SaveResult locked;
            using (new FileStream(pck, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) locked = DraftSaver.Write(fake, tsrc);
            r.Check("pck-preso-volta-tudo", locked.Kind == SaveKind.Locked && targets.Select(Hash).SequenceEqual(before), locked.Message);
            DraftSaver.FailAfter = 1;
            SaveResult rb;
            try { rb = DraftSaver.Write(fake, tsrc); } finally { DraftSaver.FailAfter = -1; }
            r.Check("falha-no-meio-volta-tudo", rb.Kind == SaveKind.RolledBack && targets.Select(Hash).SequenceEqual(before), rb.Message);
            DraftSaver.FailWith = () => new NotSupportedException("pacote acima de 4 GB (teste)");
            SaveResult anyErr;
            try { anyErr = DraftSaver.Write(fake, tsrc); } catch (Exception ex) { anyErr = new SaveResult(SaveKind.Failed, "escapou: " + ex.GetType().Name); } finally { DraftSaver.FailWith = null; }
            r.Check("falha-qualquer-erro-volta-tudo", anyErr.Kind == SaveKind.RolledBack && targets.Select(Hash).SequenceEqual(before), anyErr.Message);
            var ok = DraftSaver.Write(fake, tsrc);
            bool inPck; using (var reread = new PckArchive(pck)) inPck = reread.Find(@"litmodels\teste\1\x.bmd") is not null;
            r.Check("grava-tudo", ok.Kind == SaveKind.Both && Hash(ecwT) != before[1] && inPck, ok.Message);
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }


    // ---------------- estudo da etapa 2a: construção nova e escala ----------------
    static void Study2(Report r, string element, string? config)
    {
        foreach (var map in new[] { "world", "a05" })
        {
            var ecwPath = Path.Combine(element, "maps", map, map + ".ecwld");
            var ecwBytes = File.ReadAllBytes(ecwPath);
            var ecw = new EcwldFile(ecwBytes);
            var bsd = File.ReadAllBytes(Path.ChangeExtension(ecwPath, ".ecbsd"));
            // 1) área de cada bloco: os ornamentos de um bloco têm o lugar dentro dela (os que atravessam blocos ficam fora)
            int inside = 0, total = 0, swapped = 0;
            var exts = new Dictionary<string, int>();
            foreach (var bl in ecw.Blocks)
            {
                var a = ObjectFormats.BlockArea(ecw, bl);
                var aSwap = ObjectFormats.BlockArea(ecw, bl, swapRowCol: true);
                foreach (var it in bl.Items[(int)EcwldFile.Kind.Ornament])
                {
                    int off = BitConverter.ToInt32(it, 4);
                    if (off < 0 || off + 44 > bsd.Length) continue;
                    float x = BitConverter.ToSingle(bsd, off), z = BitConverter.ToSingle(bsd, off + 8);
                    total++;
                    if (x >= a.X0 && x <= a.X1 && z >= a.Z0 && z <= a.Z1) inside++;
                    if (x >= aSwap.X0 && x <= aSwap.X1 && z >= aSwap.Z0 && z <= aSwap.Z1) swapped++;
                    int len = BitConverter.ToInt32(bsd, off + 40);
                    if (len > 0 && len < 512 && off + 44 + len <= bsd.Length)
                    {
                        var name = System.Text.Encoding.ASCII.GetString(bsd, off + 44, len);
                        var ext = Path.GetExtension(name.TrimEnd('\0')).ToLowerInvariant() + (name.EndsWith('\0') ? "+nul" : "");
                        exts[ext] = exts.GetValueOrDefault(ext) + 1;
                    }
                }
            }
            // por construção (ID): algum bloco que a lista contém o lugar dela? (quem atravessa blocos é listado em vários)
            var hitById = new Dictionary<uint, bool>(); var hitSwap = new Dictionary<uint, bool>();
            foreach (var bl in ecw.Blocks)
            {
                var a1 = ObjectFormats.BlockArea(ecw, bl); var a2 = ObjectFormats.BlockArea(ecw, bl, swapRowCol: true);
                foreach (var it in bl.Items[(int)EcwldFile.Kind.Ornament])
                {
                    int off = BitConverter.ToInt32(it, 4);
                    if (off < 0 || off + 44 > bsd.Length) continue;
                    float x = BitConverter.ToSingle(bsd, off), z = BitConverter.ToSingle(bsd, off + 8);
                    uint id = EcwldFile.IdOf(it);
                    hitById[id] = hitById.GetValueOrDefault(id) || (x >= a1.X0 && x <= a1.X1 && z >= a1.Z0 && z <= a1.Z1);
                    hitSwap[id] = hitSwap.GetValueOrDefault(id) || (x >= a2.X0 && x <= a2.X1 && z >= a2.Z0 && z <= a2.Z1);
                }
            }
            int ids = hitById.Count, ok = hitById.Count(kv => kv.Value), okSwap = hitSwap.Count(kv => kv.Value);
            r.Check($"bloco-area {map}", ids > 0 && ok >= ids * 0.98 && ok > okSwap, $"{ok}/{ids} construções têm um bloco que contém o lugar delas (trocando linha/coluna: {okSwap}); {inside}/{total} referências dentro do próprio bloco");
            r.Check($"nome-ornamento {map}", exts.Count > 0, string.Join(", ", exts.Select(kv => $"{kv.Key} {kv.Value}")));

            // 2) ornamento novo no fim do .ecbsd, relido pelo offset
            var src = ecw.Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.Ornament]).First();
            int srcOff = BitConverter.ToInt32(src, 4);
            var t = new ObjTransform(new Vector3(1, 2, 3), Vector3.UnitZ, Vector3.UnitY, 1);
            var (nb, off2) = ObjectFormats.NewOrnament(bsd, srcOff, @"9\litmodel_99999.bmd", t);
            int len2 = BitConverter.ToInt32(nb, off2 + 40);
            var name2 = System.Text.Encoding.ASCII.GetString(nb, off2 + 44, len2).TrimEnd('\0');
            r.Check($"ecbsd-acrescenta {map}", off2 == bsd.Length && nb.AsSpan(0, bsd.Length).SequenceEqual(bsd) && name2 == @"9\litmodel_99999.bmd" && BitConverter.ToSingle(nb, off2 + 4) == 2,
                $"offset {off2}, nome \"{name2}\"");
        }

        // 3) map.bht: acrescentar brushes (cópia de um brush existente) e reler
        if (config is not null)
        {
            var bhtPath = Path.Combine(config, "a05", "map.bht");
            var bytes = File.ReadAllBytes(bhtPath);
            var bht = new BhtFile(bytes);
            var one = bht.BrushBytes(0);
            var more = new BhtFile(bht.With([one, one]));
            r.Check("bht-acrescenta", more.Count == bht.Count + 2 && more.BrushBytes(bht.Count).AsSpan().SequenceEqual(one)
                && more.Write().AsSpan(more.TreeStart).SequenceEqual(bytes.AsSpan(bht.TreeStart)), $"{bht.Count} -> {more.Count} brushes, árvore igual");

            // 4) escala 2× de um brush em volta do centro: caixa dobra e os planos passam pelos cantos novos
            var b2 = (byte[])one.Clone();
            var (mn, mx) = ObjectFormats.BrushBox(b2, 0);
            var c = (mn + mx) / 2;
            ObjectFormats.ScaleBrush(b2, 0, 2, c);
            var (mn2, mx2) = ObjectFormats.BrushBox(b2, 0);
            bool boxOk = Vector3.Distance(mx2 - mn2, (mx - mn) * 2) < 0.01f && Vector3.Distance((mn2 + mx2) / 2, c) < 0.01f;
            int sides = BitConverter.ToInt32(b2, 52);
            bool planesOk = true;
            for (int s = 0, p = 56; s < sides; s++, p += 17)
            {
                var n = new Vector3(BitConverter.ToSingle(b2, p), BitConverter.ToSingle(b2, p + 4), BitConverter.ToSingle(b2, p + 8));
                var n0 = new Vector3(BitConverter.ToSingle(one, p), BitConverter.ToSingle(one, p + 4), BitConverter.ToSingle(one, p + 8));
                float d0 = BitConverter.ToSingle(one, p + 12), d1 = BitConverter.ToSingle(b2, p + 12);
                // um ponto do plano velho (o mais perto do centro) escalado tem que cair no plano novo
                var q = c + n0 * (d0 - Vector3.Dot(n0, c));
                var q2 = c + (q - c) * 2;
                if (n != n0 || MathF.Abs(Vector3.Dot(n, q2) - d1) > 0.01f) planesOk = false;
            }
            r.Check("escala-brush", boxOk && planesOk, $"caixa {mx - mn} -> {mx2 - mn2}, planos ok={planesOk}");
        }
    }


    // ---------------- colar e escala no rascunho (etapa 2a) ----------------
    static void Draft2(Report r)
    {
        var d = OneObject(out var src);
        var copy = new DraftObject { Key = "new:1", Kind = ObjKind.Building, Piece = 1, Source = src.Source, CopyOf = src.Key, Original = src.Original with { Pos = src.Original.Pos + new Vector3(10, 0, 0) } };
        string? why = d.Apply(new AddOp(copy.Key, copy));
        r.Check("draft-cola", why is null && d.Find("new:1") == copy && d.Pieces[1].Added.Count == 1 && d.Dirty && !copy.Deleted, why ?? "");
        bool cycle = true;
        for (int i = 0; i < 5; i++) { cycle &= d.Undo() && copy.Deleted && !d.Dirty; cycle &= d.Redo() && !copy.Deleted && d.Dirty; }
        r.Check("colar-desfaz-refaz-uma", cycle && d.Pieces[1].Added.Count == 1);
        r.Check("draft-cola-repetido-recusa", d.Apply(new AddOp(copy.Key, copy)) is not null);

        var dm = new WorldDraft("teste");
        var m = new DraftObject { Key = "ecwld:EcModel:5", Kind = ObjKind.Model, Piece = 1, Source = "x.ecm", Original = new ObjTransform(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 1) };
        dm.Put(new DraftPiece(1) { Loaded = true, Objects = { m } });
        r.Check("draft-escala-modelo-recusa", dm.Apply(new MoveOp(m.Key, m.Original with { Scale = 2 })) is not null && m.Now.Scale == 1);
        var db = OneObject(out var b);
        r.Check("draft-escala-construcao", db.Apply(new MoveOp(b.Key, b.Original with { Scale = 1.5f })) is null && b.Now.Scale == 1.5f);
    }

    // ---------------- estudo dos formatos (Task 1) ----------------
    static void Study(Report r, string element, string? config)
    {
        using var lit = new PckArchive(Path.Combine(element, "litmodels.pck"));
        foreach (var map in StudyMaps)
        {
            var entries = lit.Entries.Where(e => e.Path.StartsWith($@"litmodels\{map}\", StringComparison.OrdinalIgnoreCase)
                && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)).ToList();
            // 1) cabeçalho: escrever os mesmos valores dá os mesmos bytes
            int same = 0, bad = 0; string? firstBad = null;
            foreach (var e in entries)
            {
                var b = lit.Read(e);
                var t = ObjectFormats.ReadBmd(b);
                if (ObjectFormats.WithBmdTransform(b, t).AsSpan().SequenceEqual(b)) same++; else { bad++; firstBad ??= e.Path; }
            }
            r.Check($"bmd-cabecalho-volta-igual {map}", entries.Count > 0 && bad == 0, $"{same}/{entries.Count}" + (firstBad is null ? "" : $", 1o diferente {firstBad}"));

            // 2) girar 90 graus: brushes do .bmd e do map.bht juntos, caixa nova cobre os vértices
            var bhtPath = config is null ? null : Path.Combine(config, map, "map.bht");
            if (bhtPath is null || !File.Exists(bhtPath)) { r.Note($"{map}: sem map.bht, giro do servidor não conferido"); continue; }
            var bht = new BhtFile(File.ReadAllBytes(bhtPath));
            var keys = bht.Keys();
            var pick = entries.Select(e => (e, b: lit.Read(e))).FirstOrDefault(x => BmdModel.CdBrushes(x.b) is { Count: > 0 } at
                && at.All(o => keys.ContainsKey(BhtFile.KeyAt(x.b, o))) && BmdModel.Read(x.b) is { Meshes.Count: > 0 });
            if (pick.b is null) { r.Check($"bmd-giro-brushes {map}", false, "nenhuma construção com brushes achados no map.bht"); continue; }
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            var center = ObjectFormats.BmdCenter(pick.b);
            var oldKeys = BmdModel.CdBrushes(pick.b)!.Select(o => BhtFile.KeyAt(pick.b, o)).ToList();
            var sel = oldKeys.SelectMany(k => keys[k]).Distinct().ToList();
            var turned = ObjectFormats.RotateBmd(pick.b, rot, center);
            bht.Rotate(sel, rot, center);
            var newBht = bht.Write();
            var bht2 = new BhtFile(newBht);
            var newAt = BmdModel.CdBrushes(turned);
            var newKeys = newAt?.Select(o => BhtFile.KeyAt(turned, o)).ToHashSet() ?? [];
            var bhtKeys2 = bht2.Keys();
            bool allInBht = newAt is { Count: > 0 } && newKeys.All(bhtKeys2.ContainsKey);
            var m = BmdModel.Read(turned)!;
            var verts = BmdModel.WorldVertices(m);
            var boxes = newAt!.Select(o => ObjectFormats.BrushBox(turned, o)).ToList();
            var (mn, mx) = (new Vector3(boxes.Min(b => b.Min.X), boxes.Min(b => b.Min.Y), boxes.Min(b => b.Min.Z)), new Vector3(boxes.Max(b => b.Max.X), boxes.Max(b => b.Max.Y), boxes.Max(b => b.Max.Z)));
            var (vmn, vmx) = (new Vector3(verts.Min(v => v[0]), verts.Min(v => v[1]), verts.Min(v => v[2])), new Vector3(verts.Max(v => v[0]), verts.Max(v => v[1]), verts.Max(v => v[2])));
            // os brushes não precisam cobrir o desenho inteiro (só o que bate); confere que giraram junto: centro dos brushes
            // girado = centro novo, com folga de 1 cm por 100 m
            var oldBoxes = BmdModel.CdBrushes(pick.b)!.Select(o => ObjectFormats.BrushBox(pick.b, o)).ToList();
            var oldC = oldBoxes.Aggregate(Vector3.Zero, (s, b) => s + (b.Min + b.Max) / 2) / oldBoxes.Count;
            var newC = boxes.Aggregate(Vector3.Zero, (s, b) => s + (b.Min + b.Max) / 2) / boxes.Count;
            var expect = Vector3.Transform(oldC - center, rot) + center;
            bool centerOk = Vector3.Distance(expect, newC) < 0.05f + expect.Length() * 1e-4f;
            r.Check($"bmd-giro-brushes {map}", allInBht && centerOk && sel.Count > 0,
                $"{Path.GetFileName(pick.e.Path)}: {newAt.Count} brushes no .bmd, {sel.Count} no map.bht, todos achados={allInBht}, centro ok={centerOk} (esperado {expect}, veio {newC}); caixa dos brushes {mn}..{mx}, desenho {vmn}..{vmx}");

            // 3) apagar construção: sem a referência no .ecwld e sem os brushes no map.bht, os dois arquivos releem
            var ecwPath = Path.Combine(element, "maps", map, map + ".ecwld");
            var ecwBytes = File.ReadAllBytes(ecwPath);
            var ecw = new EcwldFile(ecwBytes);
            var bsd = File.ReadAllBytes(Path.ChangeExtension(ecwPath, ".ecbsd"));
            uint? oid = ObjectFormats.OrnamentIdOf(ecw, bsd, pick.e.Path);
            if (oid is not { } id) { r.Check($"apagar-construcao {map}", false, $"não achei o ornamento de {pick.e.Path} no .ecwld"); continue; }
            int refs = ecw.Delete(EcwldFile.Kind.Ornament, id);
            var ecwOut = ecw.Write();
            var ecwBack = new EcwldFile(ecwOut);
            bool goneEcw = !ecwBack.Find(EcwldFile.Kind.Ornament, id).Any();
            var bhtCut = new BhtFile(File.ReadAllBytes(bhtPath)).Without(sel);
            var bht3 = new BhtFile(bhtCut);
            bool goneBht = bht3.Count == bht.Count - sel.Count && oldKeys.All(k => !bht3.Keys().ContainsKey(k) || keys[k].Count > sel.Count(i => keys[k].Contains(i)));
            r.Check($"apagar-construcao {map}", refs > 0 && goneEcw && goneBht, $"ornamento {id}: {refs} referência(s) tiradas do .ecwld, {sel.Count} brushes tirados do map.bht ({bht.Count} -> {bht3.Count})");

            // 4) modelos do .ecwld: ler e escrever posição/direção/cima dá os mesmos bytes; regravar sem mudança é igual
            var ecw2 = new EcwldFile(ecwBytes);
            int models = 0, mbad = 0;
            foreach (var bl in ecw2.Blocks)
                foreach (var it in bl.Items[(int)EcwldFile.Kind.EcModel])
                {
                    var before = (byte[])it.Clone();
                    ObjectFormats.WriteEcModel(it, ObjectFormats.ReadEcModel(it));
                    models++; if (!it.AsSpan().SequenceEqual(before)) mbad++;
                }
            r.Check($"ecwld-modelo-campos {map}", mbad == 0 && ecw2.Write().AsSpan().SequenceEqual(ecwBytes), $"{models} modelos, {mbad} diferentes; .ecwld v{ecw2.Version} sem escala de modelo (escala só na v17+)");

            // 5) limites: maior contagem por bloco e por pedaço (o cliente 1.5.3 usa listas dinâmicas, sem teto fixo)
            int maxBlock = ecw2.Blocks.Max(b => b.Items.Sum(l => l.Count));
            int maxPiece = entries.GroupBy(e => e.Path.Split('\\')[2]).Max(g => g.Count());
            r.Check($"limite-pedaco {map}", true, $"maior bloco do .ecwld: {maxBlock} itens; maior pedaço: {maxPiece} .bmd; map.bht: {bht.Count} brushes");
        }
    }
}
