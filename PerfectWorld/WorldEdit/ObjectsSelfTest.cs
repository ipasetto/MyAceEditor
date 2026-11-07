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
        try { Load(r, element, config); }
        catch (Exception ex) { r.Check("leitura-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
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
