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
        lines.Add($"falhas: {r.Failed}");
        File.WriteAllLines(output, lines);
    }

    static IEnumerable<string> ClientFiles(string maps) =>
        Directory.GetDirectories(maps).Select(d => Path.Combine(d, "precinct.clt")).Where(File.Exists);

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
