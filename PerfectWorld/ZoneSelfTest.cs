namespace MyAceEditor.PerfectWorld;

/// <summary>
/// --zone-test &lt;maps&gt; &lt;config&gt; &lt;saída&gt;: confere o editor de zonas sem abrir janela. Lê os arquivos reais e só grava em pasta
/// temporária. Uma linha "OK nome" ou "FALHA nome: motivo" por checagem e "falhas: N" no fim.
/// </summary>
public static class ZoneSelfTest
{
    sealed class Report(List<string> lines)
    {
        public int Failed { get; private set; }
        public void Check(string name, bool ok, string why = "")
        {
            lines.Add(ok ? $"OK {name}" + (why.Length > 0 ? $": {why}" : "") : $"FALHA {name}: {why}");
            if (!ok) Failed++;
        }
    }

    public static void Run(string maps, string? config, string output)
    {
        var lines = new List<string>();
        var r = new Report(lines);
        Client(r, maps);
        if (config is not null) Server(r, maps, config);
        Rules(r, maps);
        Canvas(r);
        Editing(r);
        if (config is not null) Pair(r, maps, config);
        lines.Add($"falhas: {r.Failed}");
        File.WriteAllLines(output, lines);
    }

    static IEnumerable<string> ClientFiles(string maps) =>
        Directory.GetDirectories(maps).Select(d => Path.Combine(d, "precinct.clt")).Where(File.Exists);

    /// <summary>Cópia de um mapa em pasta temporária: (pasta do cliente, pasta do servidor).</summary>
    static (string Client, string Server) Copy(string temp, string maps, string config, string map, int n)
    {
        string c = Path.Combine(temp, $"c{n}", map), s = Path.Combine(temp, $"s{n}", map);
        Directory.CreateDirectory(c); Directory.CreateDirectory(s);
        File.Copy(Path.Combine(maps, map, "precinct.clt"), Path.Combine(c, "precinct.clt"));
        if (File.Exists(Path.Combine(config, map, "precinct.sev"))) File.Copy(Path.Combine(config, map, "precinct.sev"), Path.Combine(s, "precinct.sev"));
        return (c, s);
    }

    static byte[] Bytes(string dir, string file) => File.ReadAllBytes(Path.Combine(dir, file));
    static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
    /// <summary>Área como texto: dois valores iguais só se tudo que vai para o arquivo for igual.</summary>
    static string Line(PrecinctFile.Area a) { var f = new PrecinctFile(); f.Areas.Add(a); return System.Text.Encoding.Unicode.GetString(f.Render()); }
    static PrecinctFile.Area Moved(PrecinctFile.Area a) => a with { Points = [.. a.Points.Select((p, i) => i == 0 ? (p.X + 1, p.Z) : p)] };

    static void Pair(Report r, string maps, string config)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MyAceZoneTest" + Environment.ProcessId);
        Directory.CreateDirectory(temp);
        try
        {
            int n = 0;
            ZonePair Fresh(string map, out string c, out string s) { (c, s) = Copy(temp, maps, config, map, ++n); return ZonePair.Open(c, s); }

            var p = Fresh("a01", out var c, out var s);
            var sev0 = Bytes(s, "precinct.sev");
            uint stamp0 = p.Client.Stamp;
            var w = p.Working();
            w.Areas[1] = w.Areas[1] with { Name = "Nome novo" };
            var named = p.Save(w);
            r.Check("so-nome-nao-toca-servidor", named.Kind == SaveKind.ClientOnly && Same(sev0, Bytes(s, "precinct.sev")) && p.Client.Stamp == stamp0 && p.Client.Areas[1].Name == "Nome novo", named.Message);
            w = p.Working();
            w.Areas[1] = Moved(w.Areas[1]);
            var moved = p.Save(w);
            r.Check("carimbo-so-com-servidor", moved.Kind == SaveKind.Both && p.Client.Stamp != stamp0 && p.Server!.Stamp == p.Client.Stamp && p.State == PairState.Ok, moved.Message);

            p = Fresh("a01", out c, out s);
            var others = p.Client.Areas.Skip(1).Select(Line).ToList();
            w = p.Working();
            w.Areas[0] = Moved(w.Areas[0]) with { Name = "Mudada" };
            w.Areas.Add(w.Areas[0] with { Id = ZoneRules.FreeId(w), Name = "Nova" });
            p.Save(w);
            var again = ZonePair.Open(c, s);
            r.Check("outras-zonas-iguais", again.Client.Areas.Skip(1).Take(others.Count).Select(Line).SequenceEqual(others) && again.Client.Areas[^1].Name == "Nova" && again.State == PairState.Ok);

            foreach (var map in new[] { "a72", "b01" })
            {
                if (!File.Exists(Path.Combine(maps, map, "precinct.clt"))) continue;
                p = Fresh(map, out c, out s);
                int main = p.Client.MainInstance;
                var keep = p.Client.Areas.Where(a => a.SrcInstance != main).Select(Line).ToList();
                w = p.Working();
                w.Areas.Remove(w.Areas.First(a => a.SrcInstance == main));
                var res = p.Save(w);
                r.Check("origens-separadas-" + map, keep.Count > 0 && ZonePair.Open(c, s).Client.Areas.Where(a => a.SrcInstance != main).Select(Line).SequenceEqual(keep), $"{keep.Count} de outra origem; {res.Kind}");
            }

            p = Fresh("a01", out c, out s);
            var clt0 = Bytes(c, "precinct.clt");
            w = p.Working();
            w.Areas[0] = Moved(w.Areas[0]);
            SaveResult locked;
            using (new FileStream(Path.Combine(s, "precinct.sev"), FileMode.Open, FileAccess.Read, FileShare.None)) locked = p.Save(w);
            r.Check("falha-segundo-arquivo", locked.Kind is SaveKind.RolledBack or SaveKind.Locked && Same(clt0, Bytes(c, "precinct.clt")), locked.Kind.ToString());

            // o .sev recusa só na hora de gravar: o .clt, que já foi, tem de voltar
            p = Fresh("a01", out c, out s);
            clt0 = Bytes(c, "precinct.clt");
            var sevFile = Path.Combine(s, "precinct.sev");
            File.SetAttributes(sevFile, FileAttributes.ReadOnly);
            w = p.Working();
            w.Areas[0] = Moved(w.Areas[0]);
            var back = p.Save(w);
            File.SetAttributes(sevFile, FileAttributes.Normal);
            r.Check("volta-do-primeiro", back.Kind == SaveKind.RolledBack && Same(clt0, Bytes(c, "precinct.clt")), back.Kind + " " + back.Message);

            p = Fresh("a01", out c, out s);
            File.AppendAllText(Path.Combine(c, "precinct.clt"), " ");
            var outside = Bytes(c, "precinct.clt");
            w = p.Working();
            w.Areas[0] = w.Areas[0] with { Name = "X" };
            var stale = p.Save(w);
            r.Check("mudou-no-disco", stale.Kind == SaveKind.ChangedOnDisk && Same(outside, Bytes(c, "precinct.clt")), stale.Kind.ToString());
            r.Check("gravar-por-cima", p.Save(w, overwriteExternal: true).Kind == SaveKind.ClientOnly);

            p = Fresh("a01", out c, out s);
            var photo = p.Snapshot();
            clt0 = Bytes(c, "precinct.clt");
            w = p.Working();
            w.Areas[0] = Moved(w.Areas[0]);
            p.Save(w);
            var undo = p.Restore(photo);
            r.Check("restore-volta", undo.Kind == SaveKind.Both && p.Client.Areas[0].Points[0] == ZonePair.Open(c, s).Client.Areas[0].Points[0] && p.State == PairState.Ok
                && PrecinctFile.Parse(clt0).Areas[0].Points[0] == p.Client.Areas[0].Points[0], undo.Kind.ToString());
            w = p.Working();
            w.Areas[0] = Moved(w.Areas[0]);
            p.Save(w);
            var saved = Bytes(c, "precinct.clt");
            File.Delete(Path.Combine(s, "precinct.sev"));
            var gone = p.Restore(photo);
            r.Check("restore-sem-servidor", gone.Kind == SaveKind.Failed && Same(saved, Bytes(c, "precinct.clt")), gone.Kind.ToString());

            r.Check("estado-a23", Fresh("a23", out _, out _).State == PairState.StampDiffers);
            p = Fresh("a40", out c, out s);
            r.Check("estado-a40", p.State == PairState.ListsDiffer, p.State.ToString());
            w = p.Working(); w.Areas[0] = Moved(w.Areas[0]);
            r.Check("a40-barra-forma", p.Save(w).Kind == SaveKind.Failed);
            w = p.Working(); w.Areas[0] = w.Areas[0] with { Name = "So nome" };
            r.Check("a40-deixa-nome", p.Save(w).Kind == SaveKind.ClientOnly);
            r.Check("a40-cliente-certo", p.UseClientAsTruth().Kind == SaveKind.Both && p.State == PairState.Ok, p.State.ToString());

            p = Fresh("a23", out c, out s);
            r.Check("igualar-carimbo", p.Save(p.Working()).Kind == SaveKind.Both && p.State == PairState.Ok);

            // .sev v6 sobe para a v7 ao mudar o que ele guarda
            p = Fresh("a01", out c, out s);
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(6u); bw.Write(p.Client.Areas.Count); bw.Write(p.Client.Stamp);
                foreach (var a in p.Client.Areas)
                {
                    bw.Write(a.Points.Count); bw.Write(a.Priority); bw.Write(a.DstInstance); bw.Write(a.SrcInstance); bw.Write(a.Domain);
                    bw.Write(a.City.X); bw.Write(a.City.Y); bw.Write(a.City.Z);
                    for (int i = 0; i < a.Points.Count; i++) { bw.Write(a.Points[i].X); bw.Write(a.PointY[i]); bw.Write(a.Points[i].Z); }
                }
                bw.Flush();
                File.WriteAllBytes(Path.Combine(s, "precinct.sev"), ms.ToArray());
            }
            p = ZonePair.Open(c, s);
            w = p.Working(); w.Areas[0] = Moved(w.Areas[0]);
            p.Save(w);
            r.Check("v6-sobe", BitConverter.ToUInt32(Bytes(s, "precinct.sev"), 0) == 7 && p.State == PairState.Ok, p.State.ToString());

            var dirBad = Path.Combine(temp, "ruim"); Directory.CreateDirectory(dirBad);
            File.WriteAllText(Path.Combine(dirBad, "precinct.clt"), "lixo");
            var bad = ZonePair.Open(dirBad, null);
            r.Check("quebrado-so-leitura", bad.State == PairState.ReadOnly && bad.Save(bad.Working()).Kind == SaveKind.Failed && File.ReadAllText(Path.Combine(dirBad, "precinct.clt")) == "lixo");

            string d1 = Path.Combine(temp, "novo-c"), d2 = Path.Combine(temp, "novo-s");
            Directory.CreateDirectory(d1); Directory.CreateDirectory(d2);
            p = ZonePair.Open(d1, d2);
            w = p.Working();
            w.Areas.Add(new PrecinctFile.Area("Primeira", 0, 0, 1, "", "", [], [(0, 0), (5, 0), (5, 5)]) { Id = 1, SrcInstance = 1, City = (1, 0, 1) });
            var made = p.Save(w);
            r.Check("sem-arquivo-cria-par", p.Empty == false && File.Exists(Path.Combine(d1, "precinct.clt")) && File.Exists(Path.Combine(d2, "precinct.sev")) && p.State == PairState.Ok, made.Message);
        }
        finally { Directory.Delete(temp, true); }
    }

    static void Editing(Report r)
    {
        static PrecinctFile.Area Box(int id, float x, int priority = 0) => new("Z" + id, priority, 0, 1, "s", "n", ["Music\\a.mp3"], [(x, 0), (x + 10, 0), (x + 10, 10), (x, 10)], 3)
            { Id = id, SrcInstance = 1, DstInstance = 1, City = (7, 1, 7), PointY = [0, 0, 0, 0] };
        var f = new PrecinctFile();
        f.Areas.Add(Box(1, 0, 4)); f.Areas.Add(Box(2, 5, 4)); f.Areas.Add(Box(2, 50, 4)); f.Areas.Add(Box(0, 100));

        var edge = ZoneEdit.MoveCorner(f.Areas[0], 0, 9999, -9999, 500, 400);
        r.Check("canto-na-borda", edge.Points[0] == (500f, -400f) && f.Areas[0].Points[0] == (0f, 0f));
        r.Check("canto-novo-e-tirado", ZoneEdit.AddCorner(f.Areas[0], 1, 12, 3, 5).Points[2] == (12f, 5f) && ZoneEdit.RemoveCorner(f.Areas[0], 0).Points.Count == 3);

        r.Check("vizinhas", ZoneEdit.Neighbors(f, 0).SequenceEqual([1]));
        r.Check("partes", ZoneEdit.Parts(f, 1).SequenceEqual([1, 2]) && ZoneEdit.Parts(f, 3).SequenceEqual([3]));

        var top = new PrecinctFile(); top.Areas.AddRange(f.Areas);
        int at = ZoneEdit.OnTop(top, 0);
        r.Check("por-cima", at == 3 && ReferenceEquals(top.At(7, 5, 1), top.Areas[at]));
        var low = new PrecinctFile(); low.Areas.AddRange(f.Areas);
        low.Areas[0] = low.Areas[0] with { Priority = 0 };
        ZoneEdit.Below(low, 0, ZoneEdit.Neighbors(low, 0));
        r.Check("por-baixo", low.Areas[0].Priority == 5 && low.At(7, 5, 1)?.Id == 2);

        var names = new PrecinctFile(); names.Areas.AddRange(f.Areas);
        ZoneEdit.Rename(names, 1, "Bosque", allParts: true);
        r.Check("nome-nas-partes", names.Areas[1].Name == "Bosque" && names.Areas[2].Name == "Bosque" && names.Areas[0].Name == "Z1");
        ZoneEdit.Rename(names, 2, "Outro", allParts: false);
        r.Check("nome-numa-parte", names.Areas[1].Name == "Bosque" && names.Areas[2].Name == "Outro");
        ZoneEdit.Rename(names, 3, "Com nome", allParts: true);
        r.Check("id0-ganha-id", names.Areas[3].Id == 3);

        var made = new PrecinctFile(); made.Areas.Add(Box(1, 0));
        int n = ZoneEdit.New(made, "Nova", [(2, 1, 2), (8, 1, 2), (8, 1, 8), (2, 1, 8)], 1);
        var fresh = made.Areas[n];
        r.Check("nova-herda", fresh.Music.SequenceEqual(["Music\\a.mp3"]) && fresh.City == (7, 1, 7) && fresh.Domain == 3 && fresh.Id == 2 && ReferenceEquals(made.At(5, 5, 1), fresh));
        int part = ZoneEdit.NewPart(made, n, [(20, 0, 20), (30, 0, 20), (30, 0, 30)]);
        r.Check("nova-parte", made.Areas[part].Id == fresh.Id && ZoneEdit.Parts(made, n).Count == 2);
    }

    static void Canvas(Report r)
    {
        using var c = new UI.MapCanvas { Size = new System.Drawing.Size(500, 400) };
        c.WorldSize = new System.Drawing.SizeF(1000, 800);
        var spot = new System.Drawing.Point(120, 90);
        bool back = true, still = true;
        foreach (float z in new[] { 1f, 4, 16 })
        {
            c.FitAll(); c.ZoomAt(spot, z);
            var map = c.ToMap(spot);
            var screen = c.ToScreen(map);
            back &= MathF.Abs(screen.X - spot.X) < 0.5f && MathF.Abs(screen.Y - spot.Y) < 0.5f;
            var before = c.ToMap(spot);
            c.ZoomAt(spot, 1.25f);
            var after = c.ToMap(spot);
            still &= c.Zoom >= 16 || MathF.Abs(before.X - after.X) * z < 0.5f && MathF.Abs(before.Y - after.Y) * z < 0.5f;
        }
        r.Check("canvas-ida-e-volta", back);
        r.Check("canvas-cursor-parado", still);
        c.ZoomAt(spot, 1000);
        bool top = c.Zoom == UI.MapCanvas.MaxZoom;
        c.ZoomAt(spot, 1e-4f);
        r.Check("canvas-limites", top && c.Zoom == 1);
        c.Size = new System.Drawing.Size(1, 1);
        c.ZoomAt(new System.Drawing.Point(0, 0), 3);
        var tiny = c.ToMap(new System.Drawing.Point(0, 0));
        r.Check("canvas-janela-minima", float.IsFinite(tiny.X) && float.IsFinite(tiny.Y) && float.IsFinite(c.ToScreen(tiny).X));
        c.Size = new System.Drawing.Size(500, 400);
        c.Frame(new System.Drawing.RectangleF(900, 700, 40, 40));
        var mid = c.ToScreen(new System.Drawing.PointF(920, 720));
        r.Check("canvas-enquadra", MathF.Abs(mid.X - 250) < 1 && MathF.Abs(mid.Y - 200) < 1 && c.Zoom > 1);
    }

    static void Rules(Report r, string maps)
    {
        var ok = new ZoneContext(false, false, _ => true, _ => true, (_, _) => true);
        static PrecinctFile.Area Square(string name) => new(name, 0, 0, 1, "", "", [], [(0, 0), (10, 0), (10, 10), (0, 10)]) { Id = 5, SrcInstance = 1, City = (5, 0, 5) };
        var file = new PrecinctFile();
        var good = Square("Vila");
        bool Has(PrecinctFile.Area a, ZoneContext c, string key) => ZoneRules.Check(file, a, c).Any(p => p.Key == key);
        r.Check("regra-limpa", ZoneRules.Check(file, good, ok).Count == 0);
        r.Check("regra-nome-vazio", Has(good with { Name = " " }, ok, "zone.noName"));
        r.Check("regra-3-cantos", Has(good with { Points = [(0, 0), (1, 1)] }, ok, "zone.fewCorners"));
        var bow = good with { Points = [(0, 0), (10, 10), (10, 0), (0, 10)] };
        r.Check("regra-cruzada-so-mexida", !Has(bow, ok, "zone.crossed") && ZoneRules.Check(file, bow, ok with { ShapeTouched = true }).Any(p => p.Key == "zone.crossed" && p.Corner >= 0));
        int crossed = 0, blocked = 0;
        foreach (var f in ClientFiles(maps))
            foreach (var a in PrecinctFile.Parse(File.ReadAllBytes(f)).Areas)
            {
                if (ZoneRules.Crosses(a.Points, out _)) crossed++;
                if (ZoneRules.Check(file, a, ok).Any(p => p.Key == "zone.crossed")) blocked++;
            }
        r.Check("regra-antigas", crossed > 0 && blocked == 0, $"{crossed} cruzadas nos arquivos, {blocked} barradas");
        r.Check("regra-cidade-nova", Has(good with { City = (0, 0, 0) }, ok with { IsNew = true }, "zone.noCity") && !Has(good with { City = (0, 0, 0) }, ok, "zone.noCity"));
        r.Check("regra-sem-terreno", Has(good with { DstInstance = 1 }, ok with { HasGround = (_, _) => false }, "zone.noCity"));
        r.Check("regra-cidade-noutro-mapa", !Has(good with { DstInstance = 2 }, ok with { HasGround = (_, _) => false }, "zone.noCity"));
        r.Check("regra-territorio", Has(good with { Domain = 9 }, ok with { DomainExists = _ => false }, "zone.noDomain"));
        var noMusic = ZoneRules.Check(file, good with { Music = ["Music\\x.mp3"] }, ok with { MusicExists = _ => false });
        r.Check("regra-musica-pergunta", noMusic.Count == 1 && noMusic[0].Kind == ZoneProblemKind.Ask);
        file.Areas.Add(good); file.Areas.Add(good with { Id = 40 });
        r.Check("free-id", ZoneRules.FreeId(file) == 41 && ZoneRules.FreeId(new PrecinctFile()) == 1);
        var gone = new PrecinctFile();
        gone.Areas.Add(good with { Points = [(0, 0), (5, 0), (5, 10), (0, 10)] });
        var one = new PrecinctFile(); one.Areas.Add(good);
        float lost = ZoneRules.Holes(one, gone, 1);
        r.Check("buraco", lost is > 0.4f and < 0.6f && ZoneRules.Holes(one, one, 1) == 0, lost.ToString());
    }

    static void Server(Report r, string maps, string config)
    {
        int same = 0, total = 0, pairs = 0, match = 0;
        string first = "";
        foreach (var dir in Directory.GetDirectories(config))
        {
            var sev = Path.Combine(dir, "precinct.sev");
            if (!File.Exists(sev)) continue;
            total++;
            var b = File.ReadAllBytes(sev);
            var read = PrecinctServer.Parse(b);
            if (read.Render().AsSpan().SequenceEqual(b)) same++; else if (first.Length == 0) first = dir;
            var clt = Path.Combine(maps, Path.GetFileName(dir), "precinct.clt");
            if (!File.Exists(clt) || read.Version != 7) continue;
            var client = PrecinctFile.Parse(File.ReadAllBytes(clt));
            if (client.Stamp != read.Stamp) continue;
            pairs++;
            if (PrecinctServer.From(client, read.Stamp).Render().AsSpan().SequenceEqual(b)) match++;
            else if (first.Length == 0) first = "par " + dir;
        }
        r.Check("sev-identico", total > 0 && same == total, $"{same}/{total} {first}");
        r.Check("sev-do-clt", pairs > 0 && match == pairs, $"{match}/{pairs} {first}");
    }

    static void Client(Report r, string maps)
    {
        int same = 0, total = 0;
        string first = "";
        foreach (var f in ClientFiles(maps))
        {
            total++;
            var b = File.ReadAllBytes(f);
            var again = PrecinctFile.Parse(b).Render();
            if (again.AsSpan().SequenceEqual(b)) { same++; continue; }
            if (first.Length > 0) continue;
            int at = 0;
            while (at < b.Length && at < again.Length && b[at] == again[at]) at++;
            string Around(byte[] x) => System.Text.Encoding.Unicode.GetString(x, Math.Max(2, at - 40) & ~1, Math.Min(80, x.Length - Math.Max(2, at - 40) & ~1)).Replace((char)10, '|');
            first = $"{Path.GetFileName(Path.GetDirectoryName(f))} em {at}: era [{Around(b)}] saiu [{Around(again)}]";
        }
        r.Check("clt-identico", total > 0 && same == total, $"{same}/{total} {first}");

        // a regra do jogo escrita à parte: CELPrecinctSet::IsPointIn percorre em ordem e troca com <=
        var world = PrecinctFile.Parse(File.ReadAllBytes(Path.Combine(maps, "world", "precinct.clt")));
        int instance = world.MainInstance, wrong = 0;
        var random = new Random(7);
        for (int i = 0; i < 40000; i++)
        {
            float x = random.Next(-4096, 4096), z = random.Next(-5632, 5632);
            PrecinctFile.Area? want = null;
            foreach (var a in world.Areas)
                if (a.SrcInstance == instance && a.Contains(x, z) && (want is null || a.Priority <= want.Priority)) want = a;
            if (!ReferenceEquals(want, world.At(x, z, instance))) wrong++;
        }
        r.Check("at-regra", wrong == 0, $"{wrong} de 40000");

        var bad = PrecinctFile.Parse(File.ReadAllBytes(Path.Combine(maps, "world", "precinct.clt")));
        bool refused(string name)
        {
            bad.Areas[0] = bad.Areas[0] with { Name = name };
            try { bad.Render(); return false; } catch (ArgumentException) { return true; }
        }
        r.Check("nome-proibido", refused("a\"b") && refused("a\nb"), "aspas e quebra de linha");
    }
}
