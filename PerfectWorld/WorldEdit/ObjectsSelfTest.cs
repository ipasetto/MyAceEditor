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
        try { TerrainHeights(r, element, config); }
        catch (Exception ex) { r.Check("altura-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
        try { TerrainPaint(r, element); }
        catch (Exception ex) { r.Check("terreno-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
        try { GroundMasks(r, element); }
        catch (Exception ex) { r.Check("t2mk-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Lights(r, element); }
        catch (Exception ex) { r.Check("luzes-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Grass(r, element); }
        catch (Exception ex) { r.Check("grama-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
        try { Index(r, element); }
        catch (Exception ex) { r.Check("indice-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
        try { Draft(r); }
        catch (Exception ex) { r.Check("rascunho-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Draft2(r); }
        catch (Exception ex) { r.Check("rascunho2-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Load(r, element, config); }
        catch (Exception ex) { r.Check("leitura-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Save(r, element, config); }
        catch (Exception ex) { r.Check("salvar-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault()); }
        try { Save3(r, element, config); }
        catch (Exception ex) { r.Check("arvores-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
        try { Study3(r, element, config); }
        catch (Exception ex) { r.Check("estudo3-sem-excecao", false, ex.GetType().Name + ": " + ex.Message); }
        try { Save2(r, element, config); }
        catch (Exception ex) { r.Check("salvar2-sem-excecao", false, ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split((char)10).FirstOrDefault()); }
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
        var refsW = ObjectFormats.ReferencedBmds(src.EcwldPath, "world");
        var perFolder = lit.Entries.Where(e => e.Path.StartsWith(@"litmodels\world\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase) && (refsW is null || refsW.Contains(e.Path)))
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
    /// <summary>Altura: todo .t2bk regrava idêntico sem mudança; bloco mexido relê; .hmap montado bate com o do servidor.</summary>
    static void TerrainHeights(Report r, string element, string? config)
    {
        int files = 0, same = 0; string? bad = null;
        foreach (var f in Directory.GetFiles(Path.Combine(element, "maps", "world"), "*.t2bk").Take(30))
        {
            var b = File.ReadAllBytes(f); files++;
            if (HeightFiles.RewriteT2bk(b, (_, _) => null).AsSpan().SequenceEqual(b)) same++; else bad ??= Path.GetFileName(f);
        }
        r.Check("t2bk-regrava-igual", files > 0 && same == files, $"{same} de {files}" + (bad is null ? "" : "; diferente: " + bad));
        var t = TerrainFile.Open(Path.Combine(element, "maps", "world"))!;
        // bloco mexido: as alturas novas voltam na releitura
        var p38 = Path.Combine(element, "maps", "world", "world_38.t2bk");
        var orig = File.ReadAllBytes(p38);
        float[]? changed = null; (int, int) key = default;
        var nb = HeightFiles.RewriteT2bk(orig, (row, col) => { if (changed is not null) return null; key = (row, col); changed = Enumerable.Range(0, 33 * 33).Select(i => 100f + i * 0.01f).ToArray(); return changed; });
        var tmp = Path.Combine(Path.GetTempPath(), "myace-t2bk-" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllBytes(Path.Combine(tmp, "world_38.t2bk"), nb);
            File.Copy(Path.Combine(element, "maps", "world", "world.trn2"), Path.Combine(tmp, "world.trn2"), true);
            var tt = TerrainFile.Open(tmp);
            int cpb = tt!.CellsPerBlock;
            float? got = tt.Sample(key.Item2 * cpb + 5, key.Item1 * cpb + 7);
            r.Check("t2bk-bloco-mexido-rele", got is { } g && MathF.Abs(g - changed![7 * 33 + 5]) < 1e-4f, $"bloco {key} amostra={got}");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
        // .hmap do servidor: o montado das alturas do cliente bate com o do servidor
        if (config is not null && File.Exists(Path.Combine(config, "world", "map", "38.hmap")))
        {
            var sv = File.ReadAllBytes(Path.Combine(config, "world", "map", "38.hmap"));
            var mine = HeightFiles.Hmap(t, 38);
            int n = Math.Min(sv.Length, mine.Length) / 4; float worst = 0;
            for (int i = 0; i < n; i += 97) worst = MathF.Max(worst, MathF.Abs(BitConverter.ToSingle(sv, i * 4) - BitConverter.ToSingle(mine, i * 4)) * 800);
            r.Check("hmap-bate-com-o-servidor", sv.Length == mine.Length && worst < 0.5f, $"tamanho {sv.Length}/{mine.Length}, maior diferença {worst:0.00} m");
        }
    }

    /// <summary>Pincel de textura do chão: pinta textura que a área já tem e uma nova, desfaz, remonta o .t2mk e relê.</summary>
    static void TerrainPaint(Report r, string element)
    {
        var t = TerrainFile.Open(Path.Combine(element, "maps", "world"));
        if (t is null) { r.Check("terreno-pinta", false, "sem terreno"); return; }
        var d = new TerrainDraft(t);
        float x = 1100, z = 1030;
        var a = d.AreaAt(x, z)!.Value;
        var before = d.Layers(a.Ar, a.Ac);   // ainda não aberto: null
        d.BeginStroke(); d.Paint(x, z, 0.1f, -1, 0, false); d.EndStroke();   // abre o pedaço sem mudar
        var l0 = d.Layers(a.Ar, a.Ac)!;
        string tex1 = l0[^1].Texture;
        int t1 = t.TextureNames.ToList().FindIndex(n => n == tex1);
        int px = (int)(x - a.X0), py = (int)(a.Z1 - z);
        d.BeginStroke(); d.Paint(x, z, 8, t1, 1, false); bool step = d.EndStroke();
        var l1 = d.Layers(a.Ar, a.Ac)!;
        bool up = l1[^1].Mask![py * 256 + px] > 200;
        // textura que a área não tem: entra como camada nova em cima
        int t2 = Enumerable.Range(0, t.TextureNames.Count).First(i => !l1.Any(l => l.Texture == t.TextureNames[i]));
        d.BeginStroke(); d.Paint(x + 3, z, 6, t2, 1, false); d.EndStroke();
        var l2 = d.Layers(a.Ar, a.Ac)!;
        bool added = l2.Length == l1.Length + 1 && l2[^1].Texture == t.TextureNames[t2];
        d.Undo(false); bool undone = d.Layers(a.Ar, a.Ac)!.Length == l1.Length;
        d.Undo(true); bool redone = d.Layers(a.Ar, a.Ac)!.Length == l2.Length;
        var files = d.Files();
        var back = files.Count == 1 ? GroundMaskFile.Read(files.Values.First()) : null;
        bool reread = back is not null && back.Mask(a.Local, back.Areas[a.Local].Count - 1)!.AsSpan().SequenceEqual(l2[^1].Mask);
        r.Check("terreno-pinta-textura", step && up && added && undone && redone && reread,
            $"área {a.Ar},{a.Ac} camadas {l1.Length}->{l2.Length} recusa={d.LastRefusal}: subiu={up} nova-camada={added} ({t.TextureNames[t2]}) ctrlz={undone} ctrly={redone} relida={reread} arquivos={files.Count}");
    }

    /// <summary>Terreno (textura do chão): todo .t2mk do cliente remonta idêntico; máscara trocada relê igual.</summary>
    static void GroundMasks(Report r, string element)
    {
        int files = 0, same = 0; string? bad = null;
        foreach (var f in Directory.GetFiles(Path.Combine(element, "maps"), "*.t2mk", SearchOption.AllDirectories))
        {
            var b = File.ReadAllBytes(f); files++;
            try { if (GroundMaskFile.Read(b).Write().AsSpan().SequenceEqual(b)) same++; else bad ??= Path.GetFileName(f); }
            catch (InvalidDataException ex) { bad ??= Path.GetFileName(f) + ": " + ex.Message; }
        }
        r.Check("t2mk-remonta-igual", files > 0 && same == files, $"{same} de {files} arquivos" + (bad is null ? "" : "; diferente: " + bad));
        var w38 = Path.Combine(element, "maps", "world", "world_38.t2mk");
        if (File.Exists(w38))
        {
            var g = GroundMaskFile.Read(File.ReadAllBytes(w38));
            int e = g.Areas[0].FindIndex(x => x.Zlib is not null);
            var m = g.Mask(0, e)!; m[1000] = (byte)(m[1000] ^ 0xFF);
            g.SetMask(0, e, m);
            var back = GroundMaskFile.Read(g.Write());
            r.Check("t2mk-mascara-trocada", back.Mask(0, e)!.AsSpan().SequenceEqual(m) && back.Areas.Count == g.Areas.Count, "área 0 entrada " + e);
        }
    }

    /// <summary>Luzes da cena: todo scenelights.dat do cliente lê e regrava idêntico; luz mexida grava a caixa = posição ± alcance.</summary>
    static void Lights(Report r, string element)
    {
        int files = 0, same = 0, lights = 0; string? bad = null;
        foreach (var f in Directory.GetFiles(Path.Combine(element, "maps"), "scenelights.dat", SearchOption.AllDirectories))
        {
            var b = File.ReadAllBytes(f); files++;
            var list = SceneLightsFile.Read(b); lights += list.Count;
            if (SceneLightsFile.Write(list).AsSpan().SequenceEqual(b)) same++; else bad ??= f;
        }
        r.Check("luzes-regravam-igual", files > 0 && same == files, $"{same} de {files} arquivos, {lights} luzes" + (bad is null ? "" : "; diferente: " + bad));
        // luz mexida: a caixa é refeita e relida igual ao que o jogo espera
        var a26 = Path.Combine(element, "maps", "a26", "scenelights.dat");
        if (File.Exists(a26))
        {
            var l = SceneLightsFile.Read(File.ReadAllBytes(a26));
            l[0] = l[0] with { Pos = l[0].Pos + new Vector3(5, 0, 0), Range = 22, Flags = l[0].Flags | SceneLight.NightOnly };
            var back = SceneLightsFile.Read(SceneLightsFile.Write(l));
            var raw = back[0].Raw!;
            bool box = BitConverter.ToSingle(raw, 84) == back[0].Pos.X - 22 && BitConverter.ToSingle(raw, 96) == back[0].Pos.X + 22 && BitConverter.ToSingle(raw, 72) == 22;
            r.Check("luz-mexida-caixa", box && back[0].Night && back.Count == l.Count && back[1].Raw!.AsSpan().SequenceEqual(l[1].Raw), $"caixa={box} noite={back[0].Night}");
        }
    }

    /// <summary>Grama: pinta, apaga, desfaz, área nova, tipo de outro mapa; salva numa cópia temporária e relê.</summary>
    static void Grass(Report r, string element)
    {
        var world = new DraftSources(element, "world", null, (x, z) => 0, 1);
        var ecwB = File.ReadAllBytes(world.EcwldPath);
        var ecw = new EcwldFile(ecwB);
        var scene = new WorldScene(ecwB); scene.LoadGrass(world.EcbsdPath);
        var terrain = TerrainFile.Open(Path.Combine(element, "maps", "world"));
        Func<float, float, int> pieceAt = (x, z) => terrain?.PieceAt(x, z) ?? 0;
        int subOk = scene.Grass.Count(g => pieceAt(g.X, g.Z) - 1 == g.Sub);
        r.Check("grama-subterreno-e-o-pedaco", scene.Grass.Count > 0 && subOk == scene.Grass.Count, $"{subOk} de {scene.Grass.Count} áreas: sub = pedaço − 1");
        var blocks = ecw.Blocks.Select(bl => ObjectFormats.BlockArea(ecw, bl)).Select(a => (a.X0, a.X1, a.Z0, a.Z1)).ToList();
        var gd = new GrassDraft(scene, blocks, pieceAt, (x, z) => terrain?.HeightAt(x, z));
        // pintar numa área que existe
        var g0 = scene.Grass.First(g => g.Bits.Any(b => b != 0xFF));
        float cx = g0.X, cz = g0.Z;
        int before = gd.Tufts([g0]);
        gd.BeginStroke(); gd.Paint(cx, cz, 6, g0.Type, 1, 1); bool step = gd.EndStroke();
        int painted = gd.Tufts([g0]);
        gd.Undo(); int undone = gd.Tufts([g0]);
        gd.Redo(); int redone = gd.Tufts([g0]);
        r.Check("grama-pinta-desfaz", step && painted > before && undone == before && redone == painted, $"tufos {before} -> {painted}, Ctrl+Z {undone}, Ctrl+Y {redone}");
        // área nova: tipo de OUTRO mapa num bloco sem área dele
        var kinds = GrassKind.All(element);
        var foreign = kinds.FirstOrDefault(k => !scene.GrassTypes.Any(t => t.Mox.Equals(k.Mox, StringComparison.OrdinalIgnoreCase)));
        string res = "sem tipo de fora";
        bool okNew = false;
        if (foreign is not null)
        {
            int t = gd.TypeFor(foreign);
            int areas0 = scene.Grass.Count;
            gd.BeginStroke(); gd.Paint(cx + 40, cz + 40, 5, t, 0.6f, 7); gd.EndStroke();
            int newA = scene.Grass.Count - areas0;
            var plan = new SavePlan(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());
            GrassSaver.Apply(plan, gd, world);
            // relê numa cópia temporária do mapa (só os arquivos do plano + os bsdata que eles citam)
            var tmp = Path.Combine(Path.GetTempPath(), "myace-grass-" + Environment.ProcessId);
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, "bsdata"));
                string te = Path.Combine(tmp, "world.ecwld"), tb = Path.Combine(tmp, "world.ecbsd");
                File.WriteAllBytes(te, plan.Files[world.EcwldPath]);
                File.WriteAllBytes(tb, plan.Files.TryGetValue(world.EcbsdPath, out var nb) ? nb : File.ReadAllBytes(world.EcbsdPath));
                foreach (var f in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(world.EcwldPath)!, "bsdata"))) File.Copy(f, Path.Combine(tmp, "bsdata", Path.GetFileName(f)), true);
                foreach (var (k, v) in plan.Files.Where(kv => string.Equals(Path.GetFileName(Path.GetDirectoryName(kv.Key)), "bsdata", StringComparison.OrdinalIgnoreCase))) File.WriteAllBytes(Path.Combine(tmp, "bsdata", Path.GetFileName(k)), v);
                var back = new WorldScene(plan.Files[world.EcwldPath]); back.LoadGrass(tb);
                bool typeOk = back.GrassTypes.Count == scene.GrassTypes.Count && back.GrassTypes[^1].Mox == foreign.Mox;
                var mine = gd.NewAreas.Where(a => a.Bits.Any(b => b != 0)).ToList();
                bool areasOk = mine.All(a => back.Grass.Any(b => MathF.Abs(b.X - a.X) < 0.01f && MathF.Abs(b.Z - a.Z) < 0.01f && b.Type == back.GrassTypes.Count - 1 && b.Bits.AsSpan().SequenceEqual(a.Bits)));
                bool changedOk = back.Grass.Any(b => b.Off == g0.Off && b.Bits.AsSpan().SequenceEqual(g0.Bits));
                bool same = new EcwldFile(plan.Files[world.EcwldPath]).Write().AsSpan().SequenceEqual(plan.Files[world.EcwldPath]);
                okNew = newA > 0 && typeOk && areasOk && changedOk && same;
                res = $"[relidas {back.Grass.Count} de {scene.Grass.Count}; {back.GrassStats}; 1a nova {mine.FirstOrDefault()?.X:0},{mine.FirstOrDefault()?.Z:0} sub {mine.FirstOrDefault()?.Sub}] {foreign.Mox} de {foreign.Maps[0]}: {newA} área(s) nova(s), tipo={typeOk} áreas-relidas={areasOk} mudada-relida={changedOk} regrava={same}; {string.Join(" / ", plan.Summary)}; arquivos: {string.Join(", ", plan.Files.Keys.Select(Path.GetFileName))}";
            }
            finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
        }
        r.Check("grama-area-nova-e-tipo-de-fora", okNew, res);
    }

    /// <summary>Índice de construções de todos os mapas: monta (1ª vez lê tudo; 2ª usa o cache) e confere o conteúdo.</summary>
    static void Index(Report r, string element)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var idx = BuildingIndex.Build(element);
        long first = sw.ElapsedMilliseconds; sw.Restart();
        var again = BuildingIndex.Build(element);
        long second = sw.ElapsedMilliseconds;
        int shapes = idx?.ByMap.Values.Sum(l => l.Count) ?? 0, uniq = idx?.ByMap.Values.SelectMany(l => l).Select(s => s.Sig).Distinct().Count() ?? 0;
        bool same = idx is not null && again is not null && idx.ByMap.Count == again.ByMap.Count && idx.ByMap.All(kv => again.ByMap.TryGetValue(kv.Key, out var l) && l.Count == kv.Value.Count);
        // o que o .bmd diz do objeto além do número: só os nomes das texturas (amostra de 15 construções do world)
        try
        {
            using var lit = new PckArchive(Path.Combine(element, "litmodels.pck"));
            var names = lit.Entries.Where(e => e.Path.StartsWith(@"litmodels\world\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd")).Where((e, i) => i % 1500 == 0).Take(15)
                .Select(e => BmdModel.Read(lit.Read(e)) is { } m ? Path.GetFileName(e.Path) + "=" + string.Join("+", m.Meshes.Select(x => x.Texture).Distinct().Take(3)) : "?").ToList();
            r.Check("texturas-exemplo", true, string.Join(" | ", names));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { r.Check("texturas-exemplo", true, ex.Message); }
        r.Check("indice-todos-os-mapas", idx is { ByMap.Count: > 1 } && same && idx.ByMap.ContainsKey("world"),
            $"{idx?.ByMap.Count} mapas, {idx?.Files} arquivos ({idx?.Failed} com erro), {shapes} formas por mapa, {uniq} formas diferentes; 1a vez {first} ms, com cache {second} ms");
    }

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
        // outro pedaço de 1 km agora vale (08/11): o salvar leva o item para os blocos do lugar novo e tira dos antigos
        bool farBlocked = DraftChecks.Run(dmp, world, null, []).Any(c => c.Blocks && c.Key == mo.Key);
        var pfar = DraftSaver.Plan(dmp, world);
        var efar = pfar.Files.TryGetValue(world.EcwldPath, out var wf) ? new EcwldFile(wf) : null;
        uint mid = uint.Parse(mo.Key[(mo.Key.LastIndexOf(':') + 1)..]);
        var inBlocks = efar?.Find(EcwldFile.Kind.EcModel, mid).Select(h => ObjectFormats.BlockArea(efar, h.Block)).ToList() ?? [];
        bool allCover = inBlocks.Count > 0 && inBlocks.All(a => far.X >= a.X0 - 20 && far.X <= a.X1 + 20 && far.Z >= a.Z0 - 20 && far.Z <= a.Z1 + 20);
        r.Check("modelo-outro-pedaco-vai-para-os-blocos", !farBlocked == (DraftSaver.BlockAt(efar ?? new EcwldFile(File.ReadAllBytes(world.EcwldPath)), far) is not null) && (efar is null ? farBlocked : allCover),
            $"bloqueou={farBlocked} blocos={inBlocks.Count} cobrem={allCover}");
        // construção levada para outro pedaço: .bmd novo na pasta do pedaço novo, ornamento novo no fim do .ecbsd, referências
        // (mesmo ID) no offset novo e nos blocos do lugar novo; o .bmd velho não é regravado (fica sem referência)
        {
            var db = DraftOf(world, mo.Piece);
            var ob = db.Pieces.Values.SelectMany(p => p.Objects).FirstOrDefault(o => o.Kind == ObjKind.Building && o.ReadOnlyWhy is null && DraftSaver.TwinsOf(world, o.Key).Count == 1);
            string res = "sem construção";
            bool okB = false;
            if (ob is not null)
            {
                var cb = (DraftChecks.WorldBox(ob).Min + DraftChecks.WorldBox(ob).Max) / 2;
                var tgt = new[] { new Vector3(1100, 0, 0), new Vector3(-1100, 0, 0), new Vector3(0, 0, 1100), new Vector3(0, 0, -1100) }
                    .Select(v => ob.Original.Pos + v).FirstOrDefault(v => world.PieceAt(v.X, v.Z) is > 0 and var pc && pc != ob.Piece);
                if (tgt != default)
                {
                    db.Apply(new MoveOp(ob.Key, ob.Original with { Pos = tgt }));
                    var pb = DraftSaver.Plan(db, world);
                    int tp = world.PieceAt(tgt.X, tgt.Z);
                    string? newPath = pb.PckFiles.Keys.FirstOrDefault(k => k.Contains("\\" + tp + "\\"));
                    var eb = pb.Files.TryGetValue(world.EcwldPath, out var ebw) ? new EcwldFile(ebw) : null;
                    var bb = pb.Files.GetValueOrDefault(world.EcbsdPath);
                    bool grew = bb is not null && bb.Length > new FileInfo(world.EcbsdPath).Length;
                    var refsNew = eb is null || bb is null || newPath is null ? [] : ObjectFormats.OrnamentRefsOf(eb, bb, newPath);
                    bool oldUntouched = !pb.PckFiles.ContainsKey(ob.Key);
                    okB = newPath is not null && grew && refsNew.Count > 0 && oldUntouched;
                    res = $"{Path.GetFileName(ob.Key)} pedaço {ob.Piece}->{tp}: novo={newPath} ecbsd-cresceu={grew} referencias-no-novo={refsNew.Count} velho-intacto={oldUntouched}";
                }
                else res = "sem pedaço vizinho";
            }
            r.Check("construcao-outro-pedaco-pasta-nova", okB, res);
        }
        // construção de OUTRO mapa (instância a05 -> world): .bmd novo na pasta do world, ornamento novo (molde do .ecbsd da a05),
        // referência nova nos blocos do lugar, brushes no map.bht do world se a original tinha colisão na a05
        {
            var a05b = true ? DraftOf(a05, all).Pieces.Values.SelectMany(p => p.Objects).FirstOrDefault(o => o.Kind == ObjKind.Building && o.ReadOnlyWhy is null && o.Solid) : null;
            string res = "sem construção na a05"; bool okX = false;
            if (a05b is not null)
            {
                var dx = DraftOf(world, mo.Piece);
                var at = dx.Pieces.Values.SelectMany(p => p.Objects).First(o => o.Kind == ObjKind.Building).Original.Pos + new Vector3(20, 0, 20);
                var cp = new DraftObject { Key = "new:x1", Kind = ObjKind.Building, Piece = mo.Piece, Source = a05b.Key, CopyOf = a05b.Key, Solid = a05b.Solid, LocalBox = a05b.LocalBox, Original = a05b.Original with { Pos = at } };
                string? addWhy = dx.Apply(new AddOp(cp.Key, cp));
                var px = DraftSaver.Plan(dx, world);
                string? np = px.PckFiles.Keys.FirstOrDefault(k => k.StartsWith(@"litmodels\world\", StringComparison.OrdinalIgnoreCase));
                var ex2 = px.Files.TryGetValue(world.EcwldPath, out var exw) ? new EcwldFile(exw) : null;
                var bx2 = px.Files.GetValueOrDefault(world.EcbsdPath);
                var rx = ex2 is null || bx2 is null || np is null ? [] : ObjectFormats.OrnamentRefsOf(ex2, bx2, np);
                bool bhtGrew = world.BhtPath is null || (px.Files.TryGetValue(world.BhtPath, out var bw2) && bw2.Length > new FileInfo(world.BhtPath).Length);
                bool reread = np is not null && BmdModel.Read(px.PckFiles[np]) is not null && ObjectFormats.ReadBmd(px.PckFiles[np]).Pos == at;
                okX = addWhy is null && np is not null && rx.Count > 0 && bhtGrew && reread;
                res = $"{a05b.Key} -> {np}: referencias={rx.Count} bht-cresceu={bhtGrew} relida={reread} {addWhy}";
            }
            r.Check("construcao-de-outro-mapa", okX, res);
        }
        // tipo de árvore de OUTRO mapa: entra na lista do mapa (fim do arquivo), a árvore ganha o número novo, regrava igual
        {
            var kinds = TreeTypeIndex.Build(world.ElementDir);
            string res = ""; bool okT = true;
            foreach (var (msrc, mpiece) in new[] { (world, mo.Piece), (a05, all.FirstOrDefault()) })
            {
                var oldBytes = File.ReadAllBytes(msrc.EcwldPath);
                var oldE = new EcwldFile(oldBytes);
                var have = new WorldScene(oldBytes).TreeTypes;
                var k = kinds.FirstOrDefault(kk => !have.Any(h => h.Spt.Equals(kk.Spt, StringComparison.OrdinalIgnoreCase) && h.CompositeMap.Equals(kk.Comp, StringComparison.OrdinalIgnoreCase)));
                if (k is null || mpiece <= 0) { res += $" {msrc.Map}: sem tipo de fora"; okT = false; continue; }
                var dt = DraftOf(msrc, mpiece);
                var block = oldE.Blocks.FirstOrDefault(bl => true);
                var anyObj = dt.Pieces.Values.SelectMany(p => p.Objects).FirstOrDefault();
                var pos = anyObj?.Original.Pos ?? Vector3.Zero;
                var tr = new DraftObject { Key = "new:t1", Kind = ObjKind.Tree, Piece = mpiece, Type = oldE.TreeTypeCount + 3, Source = k.Spt, TreeComposite = k.Comp, CopyOf = "planted",
                    Original = new ObjTransform(pos, Vector3.UnitZ, Vector3.UnitY, 1) };
                dt.Apply(new AddOp(tr.Key, tr));
                var pt = DraftSaver.Plan(dt, msrc);
                if (!pt.Files.TryGetValue(msrc.EcwldPath, out var nw)) { res += $" {msrc.Map}: nada gravado"; okT = false; continue; }
                var ne = new EcwldFile(nw);
                var ns = new WorldScene(nw);
                bool listOk = ne.TreeTypeCount == oldE.TreeTypeCount + 1 && ns.TreeTypes[^1].Spt == k.Spt && ns.TreeTypes[^1].CompositeMap == k.Comp
                    && ns.TreeTypes.Take(have.Count).Select(t => t.Spt).SequenceEqual(have.Select(t => t.Spt));
                bool treeOk = ne.Blocks.SelectMany(bl => bl.Items[(int)EcwldFile.Kind.Tree]).Any(it => BitConverter.ToInt32(it, 0) == oldE.TreeTypeCount);
                bool same = ne.Write().AsSpan().SequenceEqual(nw);
                okT &= listOk && treeOk && same;
                res += $" {msrc.Map}: tipos {oldE.TreeTypeCount}->{ne.TreeTypeCount} ({Path.GetFileName(k.Spt)} de {k.Maps[0]}) lista={listOk} arvore={treeOk} regrava={same};";
            }
            r.Check("arvore-tipo-de-outro-mapa", okT, $"{kinds.Count} tipos em todos os mapas;" + res);
        }
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
            BackupSets.FolderOverride = Path.Combine(temp, "conjuntos");
            int keep0 = BackupStore.Keep; BackupStore.Keep = 50;   // o teste grava várias vezes no mesmo segundo: com 5 a cópia do conjunto saía
            var ok = DraftSaver.Write(fake, tsrc);
            bool inPck; using (var reread = new PckArchive(pck)) inPck = reread.Find(@"litmodels\teste\1\x.bmd") is not null;
            r.Check("grava-tudo", ok.Kind == SaveKind.Both && Hash(ecwT) != before[1] && inPck, ok.Message);
            var set = BackupSets.List().FirstOrDefault(s => s.Map == "teste");
            string? why = set is null ? "conjunto não gravado" : BackupSets.Restore(set);
            r.Check("conjunto-restaura-tudo", why is null && targets.Select(Hash).SequenceEqual(before), (why ?? "") + $" {set?.Files.Count} arquivos no conjunto");
            BackupSets.FolderOverride = null; BackupStore.Keep = keep0;
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


    // ---------------- salvar novos e escala (etapa 2a) ----------------
    static DraftObject CopyOfObj(DraftObject src, string key, Vector3 move) => new()
    {
        Key = key, Kind = src.Kind, Piece = src.Piece, Source = src.Source, CopyOf = src.Key, Solid = src.Solid, LocalBox = src.LocalBox,
        Original = src.Now with { Pos = src.Now.Pos + move },
    };

    static void Save2(Report r, string element, string? config)
    {
        if (config is null) { r.Note("sem servidor: salvar 2a não conferido"); return; }
        var a05 = Sources(element, "a05", config);
        var all = Enumerable.Range(1, a05.PieceCount).ToArray();
        var d = DraftOf(a05, all);
        var buildings = d.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Kind == ObjKind.Building && o.ReadOnlyWhy is null).ToList();
        var solid = buildings.First(o => o.Solid);
        var loose = buildings.FirstOrDefault(o => !o.Solid);
        var bhtOld = new BhtFile(File.ReadAllBytes(a05.BhtPath!));
        int srcBrushes = BmdModel.CdBrushes(DraftSaver.ReadBmd(a05, solid.Key))!.Count;

        // cópia de uma construção com colisão
        var c1 = CopyOfObj(solid, "new:1", new Vector3(6, 0, 0));
        d.Apply(new AddOp(c1.Key, c1));
        var p1 = DraftSaver.Plan(d, a05);
        var newPaths = p1.PckFiles.Keys.Where(k => !k.Equals(solid.Key, StringComparison.OrdinalIgnoreCase)).ToList();
        bool one = newPaths.Count == 1 && ObjectFormats.ReadBmd(p1.PckFiles[newPaths[0]]) == c1.Now;
        var ecwNew = p1.Files.GetValueOrDefault(a05.EcwldPath); var bsdNew = p1.Files.GetValueOrDefault(a05.EcbsdPath);
        bool refOk = one && ecwNew is not null && bsdNew is not null && ObjectFormats.OrnamentRefsOf(new EcwldFile(ecwNew), bsdNew, newPaths[0]).Count >= 1;
        var bhtNew = p1.Files.TryGetValue(a05.BhtPath!, out var bb) ? new BhtFile(bb) : null;
        bool bhtOk = bhtNew is not null && bhtNew.Count == bhtOld.Count + srcBrushes
            && BmdModel.CdBrushes(p1.PckFiles[newPaths[0]])!.All(o => bhtNew.Keys().ContainsKey(BhtFile.KeyAt(p1.PckFiles[newPaths[0]], o)));
        r.Check("plano-novo-relido", one && refOk && bhtOk && p1.Summary.Any(s => s.Contains('1')), $"{string.Join(",", newPaths)} ref={refOk} bht={bhtOk} ({bhtOld.Count}->{bhtNew?.Count}) \"{string.Join(" / ", p1.Summary)}\"");

        // colar e desfazer: nada novo
        d.Undo();
        r.Check("colar-e-apagar-nada", DraftSaver.Plan(d, a05).Empty);

        // duas cópias: nomes e IDs diferentes
        var d2 = DraftOf(a05, all);
        var s2 = d2.Find(solid.Key)!;
        d2.Apply(new AddOp("new:a", CopyOfObj(s2, "new:a", new Vector3(5, 0, 0))));
        d2.Apply(new AddOp("new:b", CopyOfObj(s2, "new:b", new Vector3(-5, 0, 0))));
        var p2 = DraftSaver.Plan(d2, a05);
        var paths2 = p2.PckFiles.Keys.ToList();
        var e2 = new EcwldFile(p2.Files[a05.EcwldPath]);
        var ids = paths2.SelectMany(pp => ObjectFormats.OrnamentRefsOf(e2, p2.Files[a05.EcbsdPath], pp).Select(x => x.Id)).Distinct().ToList();
        r.Check("duas-copias-nomes-diferentes", paths2.Count == 2 && paths2.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 && ids.Count == 2, $"{string.Join(", ", paths2.Select(Path.GetFileName))}; IDs {string.Join(",", ids)}");

        // origem sem colisão: cópia sem brushes no map.bht
        if (loose is not null)
        {
            var d3 = DraftOf(a05, all);
            d3.Apply(new AddOp("new:c", CopyOfObj(d3.Find(loose.Key)!, "new:c", new Vector3(3, 0, 0))));
            var p3 = DraftSaver.Plan(d3, a05);
            r.Check("copia-sem-colisao", p3.PckFiles.Count == 1 && (!p3.Files.TryGetValue(a05.BhtPath!, out var b3) || new BhtFile(b3).Count == bhtOld.Count));
        }
        else r.Note("a05 sem construção sem colisão: copia-sem-colisao não conferido");

        // escala 2× de uma construção existente: chaves do .bmd e do map.bht juntas
        var d4 = DraftOf(a05, all);
        var s4 = d4.Find(solid.Key)!;
        d4.Apply(new MoveOp(s4.Key, s4.Original with { Scale = s4.Original.Scale * 2 }));
        var p4 = DraftSaver.Plan(d4, a05);
        var nb4 = p4.PckFiles[s4.Key];
        var keys4 = new BhtFile(p4.Files[a05.BhtPath!]).Keys();
        r.Check("escala-chave", ObjectFormats.ReadBmd(nb4).Scale == s4.Original.Scale * 2 && BmdModel.CdBrushes(nb4)!.All(o => keys4.ContainsKey(BhtFile.KeyAt(nb4, o))));

        // .bmd sem referência no .ecwld (órfão, ex.: apagado pelo editor) não aparece, como no jogo
        var tmpO = Path.Combine(Path.GetTempPath(), "myace-orfao-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(tmpO);
            var ecwO = new EcwldFile(File.ReadAllBytes(a05.EcwldPath));
            var bsdO = File.ReadAllBytes(a05.EcbsdPath);
            uint idO = ObjectFormats.OrnamentIdOf(ecwO, bsdO, solid.Key)!.Value;
            ecwO.Delete(EcwldFile.Kind.Ornament, idO);
            File.WriteAllBytes(Path.Combine(tmpO, "a05.ecwld"), ecwO.Write());
            File.WriteAllBytes(Path.Combine(tmpO, "a05.ecbsd"), bsdO);
            using var srcO = new DraftSources(element, "a05", config, a05.PieceAt, a05.PieceCount) { EcwldPath = Path.Combine(tmpO, "a05.ecwld") };
            var pO = DraftLoader.ReadPiece(srcO, solid.Piece);
            r.Check("orfao-nao-aparece", pO.Objects.All(o => !o.Key.Equals(solid.Key, StringComparison.OrdinalIgnoreCase)) && pO.Objects.Count(o => o.Kind == ObjKind.Building) == buildings.Count(o => o.Piece == solid.Piece) - 1);
        }
        finally { try { Directory.Delete(tmpO, true); } catch (IOException) { } }

        // cópia fora do pedaço bloqueia
        var d5 = DraftOf(a05, all);
        var s5 = d5.Find(solid.Key)!;
        var far = CopyOfObj(s5, "new:far", Vector3.Zero) is var f0 ? f0 : null;
        var farObj = new DraftObject { Key = "new:far", Kind = ObjKind.Building, Piece = s5.Piece, Source = s5.Source, CopyOf = s5.Key, Solid = s5.Solid, LocalBox = s5.LocalBox, Original = s5.Now with { Pos = new Vector3(99999, 0, 99999) } };
        d5.Apply(new AddOp(farObj.Key, farObj));
        r.Check("copia-fora-do-pedaco-bloqueia", DraftChecks.Run(d5, a05, null, []).Any(c => c.Blocks && c.Key == farObj.Key));

        // modelo novo (world)
        var world = Sources(element, "world", config);
        var mo = world.Scene.Value.Objects.First(o => o.Kind == ObjKind.Model && o.Piece > 0);
        var dm = DraftOf(world, mo.Piece);
        var cm = CopyOfObj(dm.Find(mo.Key)!, "new:m", new Vector3(2, 0, 2));
        dm.Apply(new AddOp(cm.Key, cm));
        var pm = DraftSaver.Plan(dm, world);
        var em = new EcwldFile(pm.Files[world.EcwldPath]);
        var models = em.Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.EcModel]).ToList();
        int oldIds = new EcwldFile(File.ReadAllBytes(world.EcwldPath)).Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.EcModel]).Select(EcwldFile.IdOf).Distinct().Count();
        r.Check("plano-modelo-novo", models.Select(EcwldFile.IdOf).Distinct().Count() == oldIds + 1 && models.Any(i => ObjectFormats.ReadEcModel(i) == cm.Now) && pm.PckFiles.Count == 0);
    }


    // ---------------- estudo da etapa 3: árvores ----------------
    static void Study3(Report r, string element, string? config)
    {
        var src = Sources(element, "world", config);
        var bytes = File.ReadAllBytes(src.EcwldPath);
        var ecw = new EcwldFile(bytes);
        var sizes = ecw.Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.Tree]).GroupBy(i => i.Length).ToDictionary(g => g.Key, g => g.Count());
        var sc = new WorldScene(bytes);
        r.Check("arvore-itens", sizes.Count > 0, $".ecwld v{ecw.Version}: itens de árvore por tamanho {string.Join(", ", sizes.Select(kv => $"{kv.Key} B x{kv.Value}"))}; {sc.Trees.Count} árvores, {sc.TreeTypes.Count} tipos");
        if (config is null) return;

        // brushes do map.bht que envolvem o tronco (1 m acima do pé), tirando os das construções do pedaço
        var bht = new BhtFile(File.ReadAllBytes(src.BhtPath!));
        var keys = bht.Keys();
        int piece = 38;
        var bld = new HashSet<int>();
        var lit = src.Lit(out _)!;
        var refs = src.Referenced.Value;
        foreach (var e in lit.Entries.Where(e => e.Path.StartsWith($@"litmodels\world\{piece}\", StringComparison.OrdinalIgnoreCase) && (refs is null || refs.Contains(e.Path))))
        {
            var b = lit.Read(e);
            if (BmdModel.CdBrushes(b) is { } at) foreach (int o in at) if (keys.TryGetValue(BhtFile.KeyAt(b, o), out var l)) bld.UnionWith(l);
        }
        var trees = sc.Trees.Where(t => src.PieceAt(t.X, t.Z) == piece).Take(300).ToList();
        var boxes = Enumerable.Range(0, bht.Count).Select(i => bht.Box(i)).ToArray();
        int Hits(float x, float y, float z, out float vol)
        {
            int n = 0; vol = 0;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (bld.Contains(i)) continue;
                var bx = boxes[i];
                if (x >= bx.X0 && x <= bx.X1 && y >= bx.Y0 && y <= bx.Y1 && z >= bx.Z0 && z <= bx.Z1) { n++; vol = (bx.X1 - bx.X0) * (bx.Y1 - bx.Y0) * (bx.Z1 - bx.Z0); }
            }
            return n;
        }
        int withHit = 0, baseHit = 0; var vols = new List<float>();
        foreach (var t in trees)
        {
            float y = (t.HasY ? t.Y : src.PieceAt(t.X, t.Z) > 0 ? t.Y : 0) + 1;
            if (Hits(t.X, y, t.Z, out var v) > 0) { withHit++; vols.Add(v); }
            if (Hits(t.X + 7, y, t.Z + 7, out _) > 0) baseHit++;
        }
        string vol = vols.Count == 0 ? "-" : $"caixa típica {vols.OrderBy(x => x).ElementAt(vols.Count / 2):0.0} m³";
        r.Check("arvore-colisao-bht", trees.Count > 0, $"pedaço {piece}: {withHit}/{trees.Count} árvores com brush (fora das construções) envolvendo o tronco; 7 m ao lado: {baseHit}; {vol}; árvores com altura no arquivo: {trees.Count(t => t.HasY)}");
    }


    // ---------------- árvores (etapa 3) ----------------
    static DraftObject NewTree(string key, int type, Vector3 pos, int piece) => new()
    {
        Key = key, Kind = ObjKind.Tree, Piece = piece, Type = type, Source = "teste.spt", CopyOf = "planted",
        Original = new ObjTransform(pos, Vector3.UnitZ, Vector3.UnitY, 1),
    };

    static void Save3(Report r, string element, string? config)
    {
        var world = Sources(element, "world", config);
        var trees = world.Scene.Value.Objects.Where(o => o.Kind == ObjKind.Tree && o.Piece > 0).ToList();
        var t1 = trees.First(); var t2 = trees.First(o => o.Key != t1.Key && o.Piece == t1.Piece);
        var d = DraftOf(world, t1.Piece);
        r.Check("arvore-editavel", d.Find(t1.Key) is { ReadOnlyWhy: null, Type: >= 0 }, d.Find(t1.Key)?.ReadOnlyWhy ?? "");
        var oldBytes = File.ReadAllBytes(world.EcwldPath);
        var oldEcw = new EcwldFile(oldBytes);
        int oldCount = oldEcw.Blocks.Sum(b => b.Items[(int)EcwldFile.Kind.Tree].Count);

        // mover 3 m, apagar outra, plantar uma nova do tipo da primeira
        var to = t1.Original with { Pos = t1.Original.Pos + new Vector3(3, 0, -2) };
        d.Apply(new MoveOp(t1.Key, to));
        d.Apply(new DeleteOp(t2.Key));
        var planted = NewTree("new:t", t1.Type, t1.Original.Pos + new Vector3(-4, 0, 4), t1.Piece);
        d.Apply(new AddOp(planted.Key, planted));
        var p = DraftSaver.Plan(d, world);
        var e = new EcwldFile(p.Files[world.EcwldPath]);
        var items = e.Blocks.SelectMany(b => b.Items[(int)EcwldFile.Kind.Tree]).ToList();
        bool At(Vector3 v, int type) => items.Any(i => BitConverter.ToInt32(i, 0) == type && MathF.Abs(BitConverter.ToSingle(i, 4) - v.X) < 1e-3f && MathF.Abs(BitConverter.ToSingle(i, 8) - v.Y) < 1e-3f && MathF.Abs(BitConverter.ToSingle(i, 12) - v.Z) < 1e-3f);
        bool moved = At(to.Pos, t1.Type) && !At(t1.Original.Pos, t1.Type);
        bool gone = !At(t2.Original.Pos, t2.Type);
        bool added = At(planted.Now.Pos, planted.Type);
        r.Check("arvore-plano", moved && gone && added && items.Count == oldCount && p.PckFiles.Count == 0 && !p.Files.Keys.Any(k => k.EndsWith("map.bht")),
            $"mexida={moved} apagada={gone} plantada={added} {oldCount}->{items.Count} \"{string.Join(" / ", p.Summary)}\"");
        // a árvore fica no bloco cuja área contém o lugar dela
        var blk = e.Blocks.First(b => b.Items[(int)EcwldFile.Kind.Tree].Any(i => BitConverter.ToInt32(i, 0) == planted.Type && MathF.Abs(BitConverter.ToSingle(i, 4) - planted.Now.Pos.X) < 1e-3f && MathF.Abs(BitConverter.ToSingle(i, 12) - planted.Now.Pos.Z) < 1e-3f));
        var area = ObjectFormats.BlockArea(e, blk);
        r.Check("arvore-no-bloco-certo", planted.Now.Pos.X >= area.X0 && planted.Now.Pos.X <= area.X1 && planted.Now.Pos.Z >= area.Z0 && planted.Now.Pos.Z <= area.Z1);

        // desfazer tudo: nada para salvar
        while (d.Undo()) { }
        r.Check("arvore-desfeito-nada", DraftSaver.Plan(d, world).Empty);

        // fora do mapa bloqueia; lugar sem bloco bloqueia
        var d2 = DraftOf(world, t1.Piece);
        d2.Apply(new MoveOp(t1.Key, t1.Original with { Pos = new Vector3(99999, 0, 99999) }));
        r.Check("arvore-fora-bloqueia", DraftChecks.Run(d2, world, null, []).Any(c => c.Blocks && c.Key == t1.Key));
        var noBlock = Enumerable.Range(0, 400).Select(k => new Vector3(-world.PieceCount + k * 37 % 8000 - 4000, 0, k * 53 % 10000 - 5000))
            .FirstOrDefault(v => world.PieceAt(v.X, v.Z) > 0 && !oldEcw.Blocks.Any(b => ObjectFormats.BlockArea(oldEcw, b) is var a && v.X >= a.X0 && v.X <= a.X1 && v.Z >= a.Z0 && v.Z <= a.Z1));
        if (noBlock != default)
        {
            var d3 = DraftOf(world, t1.Piece);
            d3.Apply(new MoveOp(t1.Key, t1.Original with { Pos = noBlock }));
            r.Check("arvore-sem-bloco-bloqueia", DraftChecks.Run(d3, world, null, []).Any(c => c.Blocks && c.Key == t1.Key));
        }
        else r.Note("world sem lugar no mapa sem bloco de cenário: arvore-sem-bloco-bloqueia não conferido");
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
