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
        lines.Add($"falhas: {r.Failed}");
        File.WriteAllLines(output, lines);
    }

    static IEnumerable<string> ClientFiles(string maps) =>
        Directory.GetDirectories(maps).Select(d => Path.Combine(d, "precinct.clt")).Where(File.Exists);

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
