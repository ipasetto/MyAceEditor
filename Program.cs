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
        if (args.Length >= 2 && args[0] is "--cfg-audit" or "--cfg-match" or "--cfg-batch")
        { CfgDiagnostic(args); return; }
        if (args.Length >= 2 && args[0] == "--inspect") { InspectElements(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--testcfg") { TestCfg(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--pck") { ListPck(args[1], args.Length >= 3 ? args[2] : ""); return; }
        if (args.Length >= 2 && args[0] == "--icons") { TestIcons(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--roundtrip") { RoundTrip(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--check") { CheckPair(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--kinds") { DumpKinds(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--tasks") { TestTasks(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--addons") { TestAddons(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--configs")
        {
            var gc = GameConfigs.Load(args[1])!;
            var d = gc.ItemDescs.TryGetValue(args.Length >= 3 ? int.Parse(args[2]) : 174, out var x) ? x : "（无）";
            File.WriteAllText("configs-report.txt", $"颜色：{gc.ItemColors.Count}  描述：{gc.ItemDescs.Count}\n{d}\n" +
                string.Join(" | ", GameConfigs.Runs(d, System.Drawing.Color.White).Select(r => $"#{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}:{r.Text.Replace("\n", "⏎")}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 4 && args[0] == "--refs") { DumpRefs(args[1], args[2], args[3]); return; }

        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--snapshot") { Snapshot(args[1], args.Length >= 3 ? args[2] : null, args.Length >= 5 ? (args[3], args[4]) : null); return; }
        Application.Run(new EditorForm());
    }

    // --snapshot <saida.png> [aba] [bloco id]: abre fora da tela; respeita a opcao de carregar a ultima pasta; salva um print e sai.
    private static void Snapshot(string outPng, string? tab, (string Block, string Id)? item)
    {
        using var f = new EditorForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000), ShowInTaskbar = false };
        f.Show();
        if (tab is not null && f.Controls.OfType<TabControl>().FirstOrDefault() is { } tc)
            foreach (TabPage tp in tc.TabPages) if (tp.Text.StartsWith(tab, StringComparison.OrdinalIgnoreCase)) tc.SelectedTab = tp;
        var until = DateTime.Now.AddSeconds(12);
        if (item is { } x) { var t0 = DateTime.Now.AddSeconds(8); while (DateTime.Now < t0) { Application.DoEvents(); Thread.Sleep(30); } f.GoToId(x.Block, x.Id); }
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
        var sb = new StringBuilder($"版本 {t.Version} · {t.Quests.Count:N0} 个主任务 · 读取耗时 {sw.ElapsedMilliseconds} ms\n");
        if (!t.FullSchema) { sb.AppendLine($"完整结构解析：失败（{t.SchemaError}）"); File.WriteAllText("tasks-report.txt", sb.ToString(), new UTF8Encoding(false)); return; }
        var all = t.AllNodes().ToList();
        sb.AppendLine($"完整结构解析：成功 · 共 {all.Count:N0} 个任务（含子任务）· 每个任务字段数：{all[0].Fields.Count}");
        int textBad = all.Count(q => !TaskSchema93.EncodeTexts(q).AsSpan().SequenceEqual(q.TextRaw));
        sb.AppendLine($"文本重新编码一致：{all.Count - textBad:N0} / {all.Count:N0}");
        var original = File.ReadAllBytes(path);
        sb.AppendLine($"无修改重新写入：{(t.Build().AsSpan().SequenceEqual(original) ? "一致" : "不同")}");
        var q0 = t.Roots![1];
        sb.AppendLine($"样例 {q0.Id}“{q0.Name}”：{q0.Children.Count} 个子任务");
        sb.AppendLine($"  描述：{q0.Texts[0][..Math.Min(120, q0.Texts[0].Length)]}");
        foreach (var f in q0.Fields.Take(12)) sb.AppendLine($"  [{f.Group}] {f.Name} = {q0.Get(f)}");
        var talk = q0.Talks.FirstOrDefault(x => x.Windows.Count > 0);
        if (talk is not null) sb.AppendLine($"  对话：{talk.Windows[0].Text[..Math.Min(100, talk.Windows[0].Text.Length)]}");
        // edição de texto: muda a descrição e confere a releitura
        q0.Texts[0] = "中文描述测试 ✓"; q0.TextsChanged = true;
        var edited = t.Build();
        var again = new TasksFile(edited);
        sb.AppendLine($"编辑描述：{(again.FullSchema && again.Roots![1].Texts[0] == "中文描述测试 ✓" ? "通过" : "失败 " + again.SchemaError)}（{edited.Length - original.Length:+#;-#;0} 字节）");
        File.WriteAllText("tasks-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --addons <elements.data>: descrição dos addons como no jogo (addons-report.txt).
    private static void TestAddons(string dataPath)
    {
        var module = LoadElementsData(dataPath, out _);
        using var pck = new PckArchive(GameConfigs.FindConfigsPck(dataPath)!);
        var at = AddonText.Load(pck)!;
        var addons = module.FindListByName("EQUIPMENT_ADDON")!.Items;
        int ok = addons.Count(a => at.Describe(a) is not null);
        var sb = new StringBuilder($"已知类型：{at.Count} 个 ID · 有描述的附加属性：{ok} / {addons.Count}\n");
        foreach (var a in addons.Where((_, i) => i % 97 == 0).Take(30))
            sb.AppendLine($"  {a.Id} {a.Name} [类型 {at.TypeOf(a.Id)}] p=({a.Fields["param1"].Value},{a.Fields["param2"].Value}) => {at.Describe(a)}");
        File.WriteAllText("addons-report.txt", sb.ToString(), new UTF8Encoding(false));
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
        var module = LoadElementsData(dataPath, out _);
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
            sb.AppendLine($"引用 {firstRef} 的记录数：{used.Count}");
            foreach (var u in used.Take(8)) sb.AppendLine($"  {u.Block.Name} {u.Item.Id} {u.Item.Name} · {u.Field} · {u.Probability}");
        }
        File.WriteAllText("refs.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --check <pasta element do cliente> <pasta home do servidor>: compara o par (check-report.txt).
    private static void CheckPair(string client, string server)
    {
        var sb = new StringBuilder();
        foreach (var r in VersionCheck.Run(client, server))
            sb.AppendLine($"[{r.Nivel}] {r.Item} | 客户端：{r.Cliente} | 服务端：{r.Servidor} | {r.Dica}");
        File.WriteAllText("check-report.txt", sb.ToString(), Encoding.UTF8);
    }

    // --roundtrip <elements.data>: prova a gravação (roundtrip-report.txt). Não altera o arquivo de entrada.
    //  1) ler e gravar sem mudanças => bytes idênticos;
    //  2) editar o nome de 1 item => só bytes daquele registro mudam, e a releitura mostra o nome novo.
    private static void RoundTrip(string dataPath)
    {
        static Model.Module Load(string path)
        {
            return LoadElementsData(path, out _);
        }
        var sb = new StringBuilder();
        var original = File.ReadAllBytes(dataPath);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var module = Load(dataPath);
        if (module.Source is ElementsLayout { CanWrite: false } readOnly)
        {
            File.WriteAllText("roundtrip-report.txt", "当前文件只读，未执行写回测试。" + readOnly.ReadOnlyReason, Encoding.UTF8);
            Environment.ExitCode = 2;
            return;
        }
        long tRead = sw.ElapsedMilliseconds; sw.Restart();
        var same = ElementsWriter.Build(module);
        long tWrite = sw.ElapsedMilliseconds;
        bool identical = same.AsSpan().SequenceEqual(original);
        sb.AppendLine($"物品：{module.Lists.Sum(l => l.Items.Count):N0}，数据块：{module.Lists.Count} | 读取 {tRead} ms | 写入 {tWrite} ms");
        sb.AppendLine($"1）无修改写入：{(identical ? "一致" : "不同")}（{same.Length:N0} × {original.Length:N0} 字节）");
        if (!identical)
        {
            int n = Math.Min(same.Length, original.Length), i = 0;
            while (i < n && same[i] == original[i]) i++;
            sb.AppendLine($"   首个差异位于字节 {i:N0}");
        }

        var weapons = module.FindListByName("WEAPON_ESSENCE")!;
        var item = weapons.Items[0];
        string oldName = item.Fields["Name"].Value, newName = "中文写入测试 ✓";
        item.Fields["Name"].Value = newName;
        var edited = ElementsWriter.Build(module);
        int diff = 0, first = -1, last = -1;
        for (int i = 0; i < Math.Min(edited.Length, original.Length); i++)
            if (edited[i] != original[i]) { diff++; if (first < 0) first = i; last = i; }
        var tmp = Path.Combine(Path.GetTempPath(), "myace-roundtrip.data");
        File.WriteAllBytes(tmp, edited);
        var reread = Load(tmp).FindListByName("WEAPON_ESSENCE")!.Items[0];
        File.Delete(tmp);
        sb.AppendLine($"2）物品 {item.Id} 的名称：“{oldName}” -> “{newName}”");
        sb.AppendLine($"   不同字节数：{diff}（范围 {first:N0}..{last:N0}，记录长度 {weapons.Items[0].Raw!.Length} 字节）| 文件大小相同：{edited.Length == original.Length}");
        sb.AppendLine($"   重新读取：“{reread.Fields["Name"].Value}” {(reread.Fields["Name"].Value == newName ? "通过" : "失败")}");
        File.WriteAllText("roundtrip-report.txt", sb.ToString(), Encoding.UTF8);
    }

    // --icons <elements.data>: cobertura dos ícones nativos (icons-report.txt + amostras icon_*.png).
    private static void TestIcons(string dataPath)
    {
        var pckPath = IconAtlas.FindSurfacesPck(dataPath) ?? throw new FileNotFoundException("surfaces.pck");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var atlas = IconAtlas.Load(pckPath) ?? throw new InvalidDataException("未找到图标图集");
        long tAtlas = sw.ElapsedMilliseconds;
        var module = LoadElementsData(dataPath, out var reader);
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
            $"图集：{atlas.Count} 个图标，加载耗时 {tAtlas} ms\n含 file_icon 字段的物品：{with}\n已匹配：{found}（{100.0 * found / Math.Max(1, with):0.0}%）\n缺失图标（样例）：\n{string.Join("\n", missing)}\n",
            Encoding.UTF8);
    }

    // --pck <arquivo.pck> [filtro]: lista as entradas em pck-list.txt (UTF-8).
    private static void ListPck(string pckPath, string filter)
    {
        using var pck = new PckArchive(pckPath);
        var sb = new StringBuilder($"v{pck.Version} 条目数={pck.Entries.Count}\n");
        foreach (var e in pck.Entries.Where(e => e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            sb.Append($"{e.Path}\t{e.Size}\t{e.CSize}\n");
        File.WriteAllText("pck-list.txt", sb.ToString(), Encoding.UTF8);
    }

    private static void TestCfg(string cfgPath, string dataPath)
    {
        var match = CfgCatalog.Load(dataPath, cfgPath);
        var r = match.Reader;
        var mod = match.Module;
        int items = mod.Lists.Sum(l => l.Items.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"cfg 数据块：{match.Result.TotalBlocks} · 列表：{mod.Lists.Count} · 物品：{items}");
        sb.AppendLine($"位置 {r.Position}/{r.Length}（{100.0 * r.Position / r.Length:0.00}%）停止于：{r.StoppedAt ?? "EOF"}");
        foreach (var l in mod.Lists.Where(x => x.Items.Count > 0).Take(6))
        {
            var it = l.Items[0];
            var sample = string.Join(", ", it.Fields.Take(4).Select(f => $"{f.Key}={f.Value.Value}"));
            sb.AppendLine($"  {l.Name}：{l.Items.Count} 个物品 | 示例 ID={it.Id}“{it.Name}”[{sample}]");
        }
        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "testcfg.txt"), sb.ToString());
        sb.AppendLine($"完整读取：{r.LastResult?.Complete}；布局：{r.LastResult?.Format}");
        if (mod.Source is ElementsLayout layout) sb.AppendLine(layout.ReadOnlyReason);
        File.WriteAllText("testcfg.txt", sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(sb.ToString());
        if (r.LastResult?.Complete != true) Environment.ExitCode = 2;
    }

    private static async Task RunMcpMode(string[] args)
    {
        var hb = Host.CreateApplicationBuilder(args);
        hb.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        hb.Services.AddSingleton<ModuleStore>();
        hb.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
        await hb.Build().RunAsync();
    }

    private static Model.Module LoadElementsData(string path, out CfgElementsReader reader)
    {
        var match = CfgCatalog.Load(path);
        reader = match.Reader;
        return match.Module;
    }

    private static void InspectElements(string path)
    {
        string report;
        try { report = CfgCatalog.Load(path).Report; }
        catch (Exception ex) when (ex is InvalidDataException or CfgChoiceRequiredException)
        { report = ex.Message; Environment.ExitCode = 2; }
        File.WriteAllText("inspect-report.txt", report, new UTF8Encoding(false));
        Console.WriteLine(report);
    }

    private static void CfgDiagnostic(string[] args)
    {
        string output = args[0] == "--cfg-audit" ? "cfg-audit.tsv" : args[0] == "--cfg-batch" ? "cfg-batch-report.txt" : "cfg-match-report.txt";
        string report;
        try
        {
            if (args[0] == "--cfg-audit")
            {
                report = CfgCatalog.Audit(args[1]);
                if (report.Contains("\tCFG_ERROR\t", StringComparison.Ordinal)) Environment.ExitCode = 2;
            }
            else if (args[0] == "--cfg-match")
                report = CfgCatalog.Load(args[1], cfgDirectory: args.Length >= 3 ? args[2] : null).Report;
            else
            {
                var sb = new StringBuilder("批量真实数据验证（只读，不修改输入文件）\n");
                var files = Directory.EnumerateFiles(args[1], "*.data", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToList();
                int passed = 0;
                foreach (var file in files)
                {
                    sb.AppendLine($"\n===== {file} =====");
                    try
                    {
                        var match = CfgCatalog.Load(file, cfgDirectory: args.Length >= 3 ? args[2] : null);
                        sb.AppendLine(match.Report); passed++;
                    }
                    catch (Exception ex) when (ex is InvalidDataException or CfgChoiceRequiredException)
                    { sb.AppendLine("失败：" + ex.Message); }
                }
                sb.AppendLine($"\n完整读取：{passed}/{files.Count} 个文件。");
                if (files.Count == 0 || passed != files.Count) Environment.ExitCode = 2;
                report = sb.ToString();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CfgChoiceRequiredException)
        { report = ex.Message; Environment.ExitCode = 2; }
        File.WriteAllText(output, report, new UTF8Encoding(false));
        Console.WriteLine(report);
    }
}
