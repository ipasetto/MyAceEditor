using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;
using MyAceEditor.UI;
using System.Text;

namespace MyAceEditor;

internal static class Program
{
    // Modos:
    //   (padrão)   editor desktop nativo (WinForms)
    //   --mcp      servidor MCP por stdio, para um agente de IA
    //   --inspect <elements.data>   diagnóstico do parser do Perfect World
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--mcp")) { RunMcpMode(args).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--inspect") { InspectElements(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--testcfg") { TestCfg(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--pck") { ListPck(args[1], args.Length >= 3 ? args[2] : ""); return; }
        if (args.Length >= 2 && args[0] == "--icons") { TestIcons(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--roundtrip") { RoundTrip(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--check") { CheckPair(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--kinds") { DumpKinds(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--tasks") { TestTasks(args[1]); return; }
        if (args.Length >= 4 && args[0] == "--refs") { DumpRefs(args[1], args[2], args[3]); return; }

        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--snapshot") { Snapshot(args[1], args.Length >= 3 ? args[2] : null); return; }
        Application.Run(new EditorForm());
    }

    // --snapshot <saida.png> [aba]: abre o editor fora da tela (com a última pasta), salva um print e sai.
    private static void Snapshot(string outPng, string? tab)
    {
        using var f = new EditorForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000), ShowInTaskbar = false };
        f.Show();
        if (tab is not null && f.Controls.OfType<TabControl>().FirstOrDefault() is { } tc)
            foreach (TabPage tp in tc.TabPages) if (tp.Text.StartsWith(tab, StringComparison.OrdinalIgnoreCase)) tc.SelectedTab = tp;
        var until = DateTime.Now.AddSeconds(12);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(30); }
        using var bmp = new System.Drawing.Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
    }

    // --tasks <tasks.data>: lista amostra de quests e prova a gravação de nomes (tasks-report.txt).
    private static void TestTasks(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = new TasksFile(path);
        var sb = new StringBuilder($"versão {t.Version} · {t.Quests.Count:N0} quests de topo · leitura {sw.ElapsedMilliseconds} ms\n");
        if (!t.FullSchema) { sb.AppendLine($"esquema completo: FALHOU ({t.SchemaError})"); File.WriteAllText("tasks-report.txt", sb.ToString(), new UTF8Encoding(false)); return; }
        var all = t.AllNodes().ToList();
        sb.AppendLine($"esquema completo: OK · {all.Count:N0} quests no total (com subquests) · campos por quest: {all[0].Fields.Count}");
        int textBad = all.Count(q => !TaskSchema93.EncodeTexts(q).AsSpan().SequenceEqual(q.TextRaw));
        sb.AppendLine($"textos recodificados idênticos: {all.Count - textBad:N0} de {all.Count:N0}");
        var original = File.ReadAllBytes(path);
        sb.AppendLine($"regravar sem mudanças: {(t.Build().AsSpan().SequenceEqual(original) ? "IDÊNTICO" : "DIFERENTE")}");
        var q0 = t.Roots![1];
        sb.AppendLine($"amostra {q0.Id} \"{q0.Name}\": {q0.Children.Count} subquests");
        sb.AppendLine($"  descrição: {q0.Texts[0][..Math.Min(120, q0.Texts[0].Length)]}");
        foreach (var f in q0.Fields.Take(12)) sb.AppendLine($"  [{f.Group}] {f.Name} = {q0.Get(f)}");
        var talk = q0.Talks.FirstOrDefault(x => x.Windows.Count > 0);
        if (talk is not null) sb.AppendLine($"  diálogo: {talk.Windows[0].Text[..Math.Min(100, talk.Windows[0].Text.Length)]}");
        // edição de texto: muda a descrição e confere a releitura
        q0.Texts[0] = "Descrição de teste ✓"; q0.TextsChanged = true;
        var edited = t.Build();
        var again = new TasksFile(edited);
        sb.AppendLine($"editar descrição: {(again.FullSchema && again.Roots![1].Texts[0] == "Descrição de teste ✓" ? "OK" : "FALHOU " + again.SchemaError)} ({edited.Length - original.Length:+#;-#;0} bytes)");
        File.WriteAllText("tasks-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --kinds <cfg>: classificação de cada campo (kinds.tsv: bloco, campo, tipo) para conferência.
    private static void DumpKinds(string cfgPath)
    {
        var sb = new StringBuilder();
        foreach (var b in ElementsCfg.Load(cfgPath).Blocks)
            foreach (var f in b.Fields)
                sb.AppendLine($"{b.Name}	{f.Name}	{FieldKinds.Of(f.Name)}");
        File.WriteAllText("kinds.tsv", sb.ToString(), new UTF8Encoding(false));
    }

    // --refs <elements.data> <bloco> <id>: vínculos resolvidos de um item + quem usa o 1o item referenciado (refs.txt).
    private static void DumpRefs(string dataPath, string block, string id)
    {
        var r = new CfgElementsReader(dataPath);
        var cfg = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"), $"*_v{r.Version & 0xFF}.cfg")[0];
        var module = r.Parse(ElementsCfg.Load(cfg));
        var refs = new RefIndex(module);
        var item = module.FindListByName(block)!.FindById(id)!;
        var sb = new StringBuilder($"{block} {id} {item.Name}\n");
        string? firstRef = null;
        foreach (var (k, f) in item.Fields)
        {
            var kind = FieldKinds.Of(k);
            if (kind == FieldKind.Plain || f.Value is "0" or "" or "-1") continue;
            var hit = refs.Resolve(kind, f.Value);
            var prob = FieldKinds.ProbabilityFor(k, item.Fields);
            sb.AppendLine($"  {k} [{kind}] = {f.Value}" + (hit is { } h ? $" -> {h.Item.Name} ({h.Block.Name})" : "") + (prob is null ? "" : $" | {prob}={item.Fields[prob].Value}"));
            if (hit is not null && kind == FieldKind.ItemRef) firstRef ??= f.Value;
        }
        if (firstRef is not null)
        {
            var used = refs.UsedBy(firstRef);
            sb.AppendLine($"quem usa {firstRef}: {used.Count}");
            foreach (var u in used.Take(8)) sb.AppendLine($"  {u.Block.Name} {u.Item.Id} {u.Item.Name} · {u.Field} · {u.Probability}");
        }
        File.WriteAllText("refs.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --check <pasta element do cliente> <pasta home do servidor>: compara o par (check-report.txt).
    private static void CheckPair(string client, string server)
    {
        var sb = new StringBuilder();
        foreach (var r in VersionCheck.Run(client, server))
            sb.AppendLine($"[{r.Nivel}] {r.Item} | cliente: {r.Cliente} | servidor: {r.Servidor} | {r.Dica}");
        File.WriteAllText("check-report.txt", sb.ToString(), Encoding.UTF8);
    }

    // --roundtrip <elements.data>: prova a gravação (roundtrip-report.txt). Não altera o arquivo de entrada.
    //  1) ler e gravar sem mudanças => bytes idênticos;
    //  2) editar o nome de 1 item => só bytes daquele registro mudam, e a releitura mostra o nome novo.
    private static void RoundTrip(string dataPath)
    {
        static Model.Module Load(string path)
        {
            var r = new CfgElementsReader(path);
            var cfg = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"), $"*_v{r.Version & 0xFF}.cfg")[0];
            return r.Parse(ElementsCfg.Load(cfg));
        }
        var sb = new StringBuilder();
        var original = File.ReadAllBytes(dataPath);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var module = Load(dataPath);
        long tRead = sw.ElapsedMilliseconds; sw.Restart();
        var same = ElementsWriter.Build(module);
        long tWrite = sw.ElapsedMilliseconds;
        bool identical = same.AsSpan().SequenceEqual(original);
        sb.AppendLine($"itens: {module.Lists.Sum(l => l.Items.Count):N0} em {module.Lists.Count} blocos | leitura {tRead} ms | gravação {tWrite} ms");
        sb.AppendLine($"1) sem mudanças: {(identical ? "IDÊNTICO" : "DIFERENTE")} ({same.Length:N0} x {original.Length:N0} bytes)");
        if (!identical)
        {
            int n = Math.Min(same.Length, original.Length), i = 0;
            while (i < n && same[i] == original[i]) i++;
            sb.AppendLine($"   primeira diferença no byte {i:N0}");
        }

        var weapons = module.FindListByName("WEAPON_ESSENCE")!;
        var item = weapons.Items[0];
        string oldName = item.Fields["Name"].Value, newName = "Teste Gravação ✓";
        item.Fields["Name"].Value = newName;
        var edited = ElementsWriter.Build(module);
        int diff = 0, first = -1, last = -1;
        for (int i = 0; i < Math.Min(edited.Length, original.Length); i++)
            if (edited[i] != original[i]) { diff++; if (first < 0) first = i; last = i; }
        var tmp = Path.Combine(Path.GetTempPath(), "myace-roundtrip.data");
        File.WriteAllBytes(tmp, edited);
        var reread = Load(tmp).FindListByName("WEAPON_ESSENCE")!.Items[0];
        File.Delete(tmp);
        sb.AppendLine($"2) nome do item {item.Id}: \"{oldName}\" -> \"{newName}\"");
        sb.AppendLine($"   bytes diferentes: {diff} (faixa {first:N0}..{last:N0}, registro de {weapons.Items[0].Raw!.Length} bytes) | mesmo tamanho: {edited.Length == original.Length}");
        sb.AppendLine($"   releitura: \"{reread.Fields["Name"].Value}\" {(reread.Fields["Name"].Value == newName ? "OK" : "FALHOU")}");
        File.WriteAllText("roundtrip-report.txt", sb.ToString(), Encoding.UTF8);
    }

    // --icons <elements.data>: cobertura dos ícones nativos (icons-report.txt + amostras icon_*.png).
    private static void TestIcons(string dataPath)
    {
        var pckPath = IconAtlas.FindSurfacesPck(dataPath) ?? throw new FileNotFoundException("surfaces.pck");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var atlas = IconAtlas.Load(pckPath) ?? throw new InvalidDataException("atlas não encontrado");
        long tAtlas = sw.ElapsedMilliseconds;
        var reader = new CfgElementsReader(dataPath);
        var cfg = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"), $"*_v{reader.Version & 0xFF}.cfg")[0];
        var module = reader.Parse(ElementsCfg.Load(cfg));
        int with = 0, found = 0, saved = 0;
        var missing = new List<string>();
        foreach (var it in module.Lists.SelectMany(l => l.Items))
        {
            if (!it.Fields.TryGetValue("file_icon", out var fi) || string.IsNullOrEmpty(fi.Value)) continue;
            with++;
            if (!atlas.Has(fi.Value)) { if (missing.Count < 20) missing.Add($"{it.Id} {fi.Value}"); continue; }
            using var bmp = atlas.Get(fi.Value)!;
            found++;
            if (saved < 6 && found % 997 == 1) bmp.Save($"icon_{it.Id}.png", System.Drawing.Imaging.ImageFormat.Png);
            if (found % 997 == 1) saved++;
        }
        File.WriteAllText("icons-report.txt",
            $"atlas: {atlas.Count} ícones em {tAtlas} ms\nitens com file_icon: {with}\nresolvidos: {found} ({100.0 * found / Math.Max(1, with):0.0}%)\nfaltando (amostra):\n{string.Join("\n", missing)}\n",
            Encoding.UTF8);
    }

    // --pck <arquivo.pck> [filtro]: lista as entradas em pck-list.txt (UTF-8).
    private static void ListPck(string pckPath, string filter)
    {
        using var pck = new PckArchive(pckPath);
        var sb = new StringBuilder($"v{pck.Version} entradas={pck.Entries.Count}\n");
        foreach (var e in pck.Entries.Where(e => e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            sb.Append($"{e.Path}\t{e.Size}\t{e.CSize}\n");
        File.WriteAllText("pck-list.txt", sb.ToString(), Encoding.UTF8);
    }

    private static void TestCfg(string cfgPath, string dataPath)
    {
        var cfg = ElementsCfg.Load(cfgPath);
        var r = new CfgElementsReader(dataPath);
        var mod = r.Parse(cfg);
        int items = mod.Lists.Sum(l => l.Items.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"cfg blocos: {cfg.Blocks.Count}  ·  listas com dados: {mod.Lists.Count}  ·  itens: {items}");
        sb.AppendLine($"pos {r.Position}/{r.Length}  ({100.0 * r.Position / r.Length:0.00}%)  parou: {r.StoppedAt ?? "EOF"}");
        foreach (var l in mod.Lists.Take(6))
        {
            var it = l.Items[0];
            var sample = string.Join(", ", it.Fields.Take(4).Select(f => $"{f.Key}={f.Value.Value}"));
            sb.AppendLine($"  {l.Name}: {l.Items.Count} itens | ex id={it.Id} \"{it.Name}\" [{sample}]");
        }
        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "testcfg.txt"), sb.ToString());
        Console.WriteLine(sb.ToString());
    }

    private static async Task RunMcpMode(string[] args)
    {
        var hb = Host.CreateApplicationBuilder(args);
        hb.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        hb.Services.AddSingleton<ModuleStore>();
        hb.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
        await hb.Build().RunAsync();
    }

    private static void InspectElements(string path)
    {
        var sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); Console.WriteLine(s); }

        using var r = new ElementsReader(path);
        Log($"arquivo   : {path}");
        Log($"tamanho   : {r.Length:N0} bytes");
        Log($"version   : 0x{r.Version:X8}");
        Log($"signature : 0x{r.Signature:X8}  valida={r.SignatureValid}");

        var steps = r.ExecuteProgram(ElementsProgram.Steps);
        var comDados = steps.Count(s => s.Count > 0);
        Log($"\npassos: {steps.Count}  ·  blocos com dados: {comDados}  ·  posição {r.Position:N0}/{r.Length:N0}");

        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "inspect-report.txt"), sb.ToString());
    }
}
