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
        if (args.Length >= 2 && args[0] is "--cfg-audit" or "--cfg-match" or "--cfg-batch") { CfgDiagnostic(args); return; }
        if (args.Length >= 2 && args[0] == "--inspect") { InspectElements(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--testcfg") { TestCfg(args[1], args[2]); return; }
        if (args.Length >= 2 && args[0] == "--pck") { ListPck(args[1], args.Length >= 3 ? args[2] : ""); return; }
        if (args.Length >= 4 && args[0] == "--pck-extract")   // --pck-extract <pck> <filtro> <pasta>
        {
            using var pck = new PckArchive(args[1]);
            var list = pck.Entries.Where(e => e.Path.Contains(args[2], StringComparison.OrdinalIgnoreCase)).ToList();
            var (done, failed) = MyAceEditor.UI.PckView.ExtractTo(pck, args[3], list, CancellationToken.None);
            File.WriteAllText("pck-extract.txt", $"{done - failed} extraídos, {failed} falhas");
            return;
        }
        if (args.Length >= 2 && args[0] == "--icons") { TestIcons(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--roundtrip") { RoundTrip(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--check") { CheckPair(args[1], args[2]); return; }
        if (args.Length >= 4 && args[0] == "--local-send") { LocalSend(args[1], args[2], args[3]); return; }
        if (args.Length >= 2 && args[0] == "--tasks-texts") { TestTaskTexts(args[1]); return; }   // <tasks.data>: textos em qualquer versão
        if (args.Length >= 4 && args[0] == "--tasks-translate") { TasksTranslate(args[1], args[2], args[3]); return; }   // <destino> <traduzido> <saída>
        if (args.Length >= 4 && args[0] == "--pck-texts")   // <pck destino> <pck traduzido> <cópia de saída>: textos por ID (pck-texts.txt)
        {
            File.Copy(args[1], args[3], true);
            var (n, files) = PackTextTranslator.Plan(args[3], args[2], dryRun: false);
            if (files.Count > 0) PckWriter.Write(args[3], files);
            File.WriteAllText("pck-texts.txt", $"{n:N0} textos em {files.Count} arquivo(s)\n" + string.Join("\n", files.Keys.Order().Take(40)), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 4 && args[0] == "--adm")   // <pasta do servidor local> deleterole|deleteaccount <id>: o mesmo do painel (adm.txt)
        {
            var adm = new ServerAdmin(new LocalServer(args[1]));
            string? err = args[2] == "deleterole" ? adm.DeleteRole(int.Parse(args[3])) : args[2] == "deleteaccount" ? adm.DeleteAccount(int.Parse(args[3]))
                : args[2] == "role" ? adm.RoleDetails(int.Parse(args[3]))?.ToString() ?? "não achei"
                : args[2] == "broadcast" ? Run(() => adm.Broadcast(args[3]))
                : args[2] == "start" ? new LocalServer(args[1]).Start().Output : "comando?";
            static string? Run(Action a) { a(); return null; }
            File.WriteAllText("adm.txt", err ?? "OK", new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--el-translate")   // <elements destino> <elements traduzido>: quantos textos o plano troca (el-translate.txt)
        {
            var cur = CfgCatalog.Load(args[1]); var oth = CfgCatalog.Load(args[2]).Module;
            var plan = ElementsTranslator.Plan(cur.Module, ElementsCfg.Load(cur.ConfigPath), oth);
            File.WriteAllText("el-translate.txt", $"{plan.Count:N0} textos\n" + string.Join("\n", plan.Take(8).Select(c => $"{c.Block.Name} {c.Item.Id} {c.Field}: {c.Item.Fields[c.Field].Value} -> {c.Value}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--shop-translate")   // <gshop destino> <gshop traduzido>: quantos textos (shop-translate.txt)
        {
            var dst = GShopFile.ReadClient(File.ReadAllBytes(args[1])); var src = GShopFile.ReadClient(File.ReadAllBytes(args[2]));
            int n = ClientTranslator.TranslateShop(dst, src, dryRun: false);
            var back = GShopFile.ReadClient(dst.WriteClient());
            File.WriteAllText("shop-translate.txt", $"{n:N0} textos · relido {back.Items.Count:N0} itens\n" + string.Join("\n", back.Items.Take(3).Select(i => $"{i.ItemId} {i.Name}: {i.Description[..Math.Min(80, i.Description.Length)]}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--world-test")   // <pasta element> <mapa>: terreno, construções e modelos (world-test.txt)
        {
            var sb = new StringBuilder();
            var t = TerrainFile.Open(Path.Combine(args[1], "maps", args[2]));
            sb.AppendLine(t is null ? "terreno: NÃO ABRIU" : $"terreno: {t.Width}×{t.Height} m · {t.Rows}×{t.Cols} pedaços · altura em (0,0) = {t.HeightAt(0, 0)}");
            using (var lit = new PckArchive(Path.Combine(args[1], "litmodels.pck")))
            {
                var bmds = lit.Entries.Where(e => e.Path.StartsWith($@"litmodels\{args[2]}\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)).ToList();
                int ok = 0, bad = 0; string? firstBad = null;
                foreach (var e in bmds.Take(400)) { if (BmdModel.Read(lit.Read(e)) is not null) ok++; else { bad++; firstBad ??= e.Path; } }
                sb.AppendLine($"construções: {bmds.Count:N0} .bmd · lidas {ok} de {Math.Min(400, bmds.Count)} · falharam {bad}" + (firstBad is null ? "" : $" (ex.: {firstBad})"));
            }
            File.WriteAllText("world-test.txt", sb.ToString(), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 2 && args[0] == "--npcgen")   // <npcgen.data>: lê e regrava (npcgen.txt)
        {
            string rep;
            try
            {
                var d = File.ReadAllBytes(args[1]); var f = NpcGenFile.Read(d);
                rep = $"v{f.Version} · {f.Spawns.Count:N0} pontos · {f.Resources.Count:N0} recursos · regrava {(f.Write().AsSpan().SequenceEqual(d) ? "IDÊNTICO" : "DIFERENTE")}";
            }
            catch (Exception ex) { rep = "FALHOU: " + ex.Message; }
            File.WriteAllText("npcgen.txt", rep, new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--model")   // <pasta element> <.ecm>: acha o .ski do modelo (model.txt)
        {
            using var packs = new MyAceEditor.UI.ModelPacks(args[1]);
            var r = packs.For(args[2]) is { } mp ? MyAceEditor.UI.WorldModels.ResolveSki(mp, args[2]) : null;
            File.WriteAllText("model.txt", r is { } x ? $"OK {x.Dir}" : "NÃO ACHEI", new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 4 && args[0] == "--field")   // <elements.data> <bloco> <campo>: valor nos 8 primeiros itens (field.txt)
        {
            var list = CfgCatalog.Load(args[1]).Module.FindListByName(args[2]);
            File.WriteAllText("field.txt", string.Join("\n", (list?.Items ?? new()).Take(8).Select(i => $"{i.Id}\t{i.Name}\t{(i.Fields.TryGetValue(args[3], out var f) ? $"{f.Type}: {f.Value}" : "(sem o campo)")}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--names")   // <elements.data> <bloco>: ID e nome dos 15 primeiros itens (names.txt)
        {
            var list = CfgCatalog.Load(args[1]).Module.FindListByName(args[2]);
            File.WriteAllText("names.txt", string.Join("\n", (list?.Items ?? new()).Take(15).Select(i => $"{i.Id}\t{i.Name}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 3 && args[0] == "--fix-pair") { FixPairCli(args[1], args[2]); return; }   // <cliente> <servidor>: aplica as correções recomendadas
        if (args.Length >= 4 && args[0] == "--aipolicy-convert") { AiConvert(args[1], args[2], args[3]); return; }   // <entrada> <referência que o gs lê> <saída>
        if (args.Length >= 2 && args[0] == "--kinds") { DumpKinds(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--tasks") { TestTasks(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--aipolicy") { TestAiPolicy(args[1]); return; }
        if (args.Length >= 1 && args[0] == "--translate-selftest") { TranslateSelfTest().GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--addons") { TestAddons(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--bools") { TestBools(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--masks") { TestMasks(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--configs")
        {
            var gc = GameConfigs.Load(args[1])!;
            var d = gc.ItemDescs.TryGetValue(args.Length >= 3 ? int.Parse(args[2]) : 174, out var x) ? x : "(sem)";
            File.WriteAllText("configs-report.txt", $"cores: {gc.ItemColors.Count}  descrições: {gc.ItemDescs.Count}\n{d}\n" +
                string.Join(" | ", GameConfigs.Runs(d, System.Drawing.Color.White).Select(r => $"#{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}:{r.Text.Replace("\n", "⏎")}")), new UTF8Encoding(false));
            return;
        }
        if (args.Length >= 2 && args[0] == "--errors") { FindErrors(args[1]); return; }
        if (args.Length >= 4 && args[0] == "--ui-render")   // --ui-render <pasta element> <filtro> <pasta de saída>: janelas em PNG
        {
            var el = args[1];
            using var ip = new PckArchive(Path.Combine(el, "interfaces.pck"));
            using var sp = new PckArchive(Path.Combine(el, "surfaces.pck"));
            using var tex = new GameTextures(sp);
            GameFonts.Load(el);
            Directory.CreateDirectory(args[3]);
            foreach (var e in ip.Entries.Where(x => x.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && x.Path.Contains(args[2], StringComparison.OrdinalIgnoreCase)).Take(60))
                if (GameDialog.Load(ip, e.Path) is { } d)
                    using (var b = MyAceEditor.UI.GameDraw.Render(d, tex, outlines: true)) b.Save(Path.Combine(args[3], Path.GetFileNameWithoutExtension(e.Path) + ".png"));
            return;
        }
        if (args.Length >= 4 && args[0] == "--convert") { ConvertElements(args[1], args[2], args[3]); return; }   // --convert <elements.data> <cfg destino> <saída>
        if (args.Length >= 3 && args[0] == "--gshop") { TestGShop(args[1], args[2]); return; }
        if (args.Length >= 4 && args[0] == "--refs") { DumpRefs(args[1], args[2], args[3]); return; }

        ApplicationConfiguration.Initialize();
        AppLog.Hook(); AppLog.Prune();
        Application.ThreadException += (_, e) =>
        {
            AppLog.Error("erro na tela", e.Exception);
            MessageBox.Show(e.Exception.Message, "MyAceEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        if (args.Length >= 2 && args[0] == "--snapshot") { Snapshot(args[1], args.Length >= 3 ? args[2] : null, args.Length >= 5 ? (args[3], args[4]) : null, args.Length >= 6 ? args[5] : null); return; }
        AppLog.Info($"início · {Application.ProductVersion} · {Environment.OSVersion} · idioma {AppLang.Current}");
        Application.Run(new EditorForm());
    }

    // --snapshot <saida.png> [aba] [bloco id] [grupo]: abre o editor fora da tela (com a última pasta), salva um print e sai.
    private static void Snapshot(string outPng, string? tab, (string Block, string Id)? item, string? group = null)
    {
        using var f = new EditorForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000), ShowInTaskbar = false };
        f.Show();
        if (tab is not null && f.Controls.OfType<TabControl>().FirstOrDefault() is { } tc)
            foreach (TabPage tp in tc.TabPages) if (tp.Text.StartsWith(tab, StringComparison.OrdinalIgnoreCase)) tc.SelectedTab = tp;
        var until = DateTime.Now.AddSeconds(12);
        if (item is { } x) { var t0 = DateTime.Now.AddSeconds(8); while (DateTime.Now < t0) { Application.DoEvents(); Thread.Sleep(30); } f.GoToId(x.Block, x.Id); if (group is not null) f.SelectGroup(group); }
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(30); }
        if (group == "tip" && f.RenderListTip() is { } tip) { tip.Save(outPng, System.Drawing.Imaging.ImageFormat.Png); return; }   // tooltip da lista
        using var bmp = new System.Drawing.Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
    }


    // --translate-selftest: prova a fila do tradutor sem internet (limite de uso, falhas, retomada).
    private static async Task TranslateSelfTest()
    {
        var sb = new StringBuilder();
        var texts = Enumerable.Range(1, 40).Select(i => $"texto {i}").ToList();

        // 1) limite de uso nas 6 primeiras chamadas (Retry-After 1 s), depois tudo certo
        var fake = new FlakyProvider(rateLimitFirst: 6, failFirst: 0);
        var tr = new AiTranslator(new TranslationCache(Path.Combine(Path.GetTempPath(), "myace-t1.json")), fake, baseBackoffSeconds: 1);
        var r = await tr.TranslateAsync(texts, "pt", null, CancellationToken.None);
        sb.AppendLine($"1) limite de uso: traduzidos {r.Translated}/40 · falhas {r.Failed} · não tentados {r.NotTried} · chamadas {fake.Calls}");

        // 2) falhas de rede poucas: cada item tenta 3 vezes e depois desiste só daquele item
        fake = new FlakyProvider(0, failFirst: 4);
        tr = new AiTranslator(new TranslationCache(Path.Combine(Path.GetTempPath(), "myace-t2.json")), fake, baseBackoffSeconds: 1);
        r = await tr.TranslateAsync(texts, "pt", null, CancellationToken.None);
        sb.AppendLine($"2) falhas pontuais: traduzidos {r.Translated}/40 · falhas definitivas {r.Failed} · não tentados {r.NotTried}");

        // 3) serviço fora do ar: para depois de 20 falhas seguidas e deixa o resto para depois (retomada)
        fake = new FlakyProvider(0, failFirst: int.MaxValue);
        var cache3 = new TranslationCache(Path.Combine(Path.GetTempPath(), "myace-t3.json"));
        tr = new AiTranslator(cache3, fake, baseBackoffSeconds: 1);
        r = await tr.TranslateAsync(texts, "pt", null, CancellationToken.None);
        sb.AppendLine($"3) fora do ar: traduzidos {r.Translated} · falhas {r.Failed} · não tentados {r.NotTried} (para a fila sobrar)");
        fake = new FlakyProvider(0, 0);
        tr = new AiTranslator(cache3, fake, baseBackoffSeconds: 1);
        r = await tr.TranslateAsync(texts, "pt", null, CancellationToken.None);
        sb.AppendLine($"   ao voltar: traduzidos {r.Translated} · não tentados {r.NotTried} (o cache continua de onde parou)");
        File.WriteAllText("translate-selftest.txt", sb.ToString(), new UTF8Encoding(false));
    }

    sealed class FlakyProvider : ITranslationProvider
    {
        readonly int _rl, _fail; int _calls, _rlSeen, _failSeen;
        public FlakyProvider(int rateLimitFirst, int failFirst) { _rl = rateLimitFirst; _fail = failFirst; }
        public int Calls => _calls;
        public string Name => "fake";
        public Task<string> TranslateAsync(string text, string targetLang, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (Interlocked.Increment(ref _rlSeen) <= _rl)
                throw new RateLimitedException(429, TimeSpan.FromMilliseconds(300));
            if (Interlocked.Increment(ref _failSeen) <= _fail)
                throw new HttpRequestException("falha simulada");
            return Task.FromResult("[" + targetLang + "] " + text);
        }
    }

    // --aipolicy <aipolicy.data>: lê, prova o round-trip byte a byte e aponta onde o esquema para (aipolicy-report.txt).
    private static void TestAiPolicy(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var f = new AiPolicyFile(path);
        var sb = new StringBuilder($"versão {f.Version} · {f.Policies.Count:N0} políticas · {f.Policies.Sum(p => p.Triggers.Count):N0} triggers · leitura {sw.ElapsedMilliseconds} ms\n");
        if (!f.Ok)
        {
            sb.AppendLine("LEITURA INCOMPLETA — não edite este arquivo ainda:");
            sb.AppendLine(f.Error);
            sb.AppendLine("Complemente o layout em aipolicy_layouts.json (ao lado do .exe) e rode de novo.");
        }
        else
        {
            sb.AppendLine($"round-trip (reler→gravar sem mudar): {(f.RoundTripIdentical() ? "IDÊNTICO" : "DIFERENTE")}");
            sb.AppendLine("operações usadas (tipo nome × qtd [layout]):");
            foreach (var (t, n) in f.OpHistogram)
                sb.AppendLine($"  {t,2} {AiSchema.OpName(t),-20} × {n,6}  {(AiSchema.Op(t)?.Guess == true ? "[inferido]" : "[KB/arquivo]")}");
            var dv = f.Policies.SelectMany(p => p.Triggers).GroupBy(t => t.Version).OrderBy(g => g.Key);
            sb.AppendLine("dwVersion dos triggers: " + string.Join(", ", dv.Select(g => $"v{g.Key}×{g.Count()}")));
            static IEnumerable<AiCondNode> Nodes(AiCondNode? n) => n is null ? Enumerable.Empty<AiCondNode>() : new[] { n }.Concat(Nodes(n.Left)).Concat(Nodes(n.Right));
            var trg = f.Policies.SelectMany(p => p.Triggers).ToList();
            sb.AppendLine("condições (tipo×qtd): " + string.Join(", ", trg.SelectMany(t => Nodes(t.Root)).GroupBy(n => n.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}")));
            sb.AppendLine("alvos (tipo×qtd): " + string.Join(", ", trg.SelectMany(t => t.Ops).GroupBy(o => o.Target.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}")));
            var ctx = new AiContext();
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            int nt = 0;
            foreach (var p in f.Policies) foreach (var t in p.Triggers) { _ = AiMeaning.TriggerName(t, ctx); nt++; }
            sb.AppendLine($"texto de {nt:N0} triggers: {t0.ElapsedMilliseconds} ms");
            t0.Restart();
            foreach (var p in f.Policies) foreach (var t in p.Triggers) { _ = AiMeaning.TriggerSummary(t, ctx); }
            sb.AppendLine($"resumo detalhado de {nt:N0} triggers: {t0.ElapsedMilliseconds} ms");
        }
        File.WriteAllText("aipolicy-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --tasks-texts <tasks.data>: acha os textos de todas as quests (qualquer versão), regrava sem mudar (tem de sair
    // idêntico) e, na v93, confere com o esquema completo. Relatório em tasks-texts.txt.
    private static void TestTaskTexts(string path)
    {
        var sb = new StringBuilder();
        var d = File.ReadAllBytes(path);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var r = TaskTexts.Read(d);
            var all = TaskTexts.All(r.Roots).ToList();
            sb.AppendLine($"versão {r.Version} · {r.Roots.Count:N0} quests de topo · {all.Count:N0} com subquests · {sw.ElapsedMilliseconds} ms");
            sb.AppendLine($"com descrição: {all.Count(q => q.Texts[0].Text.Length > 0):N0} · janelas de diálogo: {all.Sum(q => q.Talks.Sum(t => t.Windows.Count)):N0}");
            sb.AppendLine($"regravar sem mudar: {(TaskTexts.Build(d, r).AsSpan().SequenceEqual(d) ? "IDÊNTICO" : "DIFERENTE")}");
            // reescreve todos os textos (codificados de novo): tem de sair igual também
            static IEnumerable<TaskText> Texts(TaskTextNode q) => new[] { q.Name }.Concat(q.Texts).Concat(q.Talks.SelectMany(t => new[] { t.Title }
                .Concat(t.Windows.SelectMany(w => new[] { w.Text }.Concat(w.Options.Select(o => o.Text))))));
            foreach (var t in all.SelectMany(Texts)) t.Orig = "\u0001";
            var again = TaskTexts.Build(d, r);
            int diff = again.Length == d.Length ? Enumerable.Range(0, d.Length).Count(i => again[i] != d[i]) : -1;
            sb.AppendLine($"reescrever todos os textos: {(diff == 0 ? "IDÊNTICO" : diff < 0 ? $"tamanho {again.Length:N0} × {d.Length:N0}" : $"{diff:N0} bytes diferentes (lixo depois do \0 em campos fixos)")}");
            if (r.Version == 93 && new TasksFile(d) is { FullSchema: true } full)
            {
                var byId = full.AllNodes().GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());
                int bad = all.Count(q => !byId.TryGetValue(q.Id, out var f) || !f.Texts.SequenceEqual(q.Texts.Select(t => t.Text))
                    || f.Talks.Zip(q.Talks).Any(z => z.First.Windows.Count != z.Second.Windows.Count || z.First.Windows.Zip(z.Second.Windows).Any(w => w.First.Text.TrimEnd('\0') != w.Second.Text.Text)));
                sb.AppendLine($"v93 × esquema completo: {all.Count - bad:N0} iguais, {bad:N0} diferentes · esquema completo tem {full.AllNodes().Count():N0} nós");
                foreach (var q in all.Where(q => !byId.TryGetValue(q.Id, out var f) || !f.Texts.SequenceEqual(q.Texts.Select(t => t.Text))).Take(3))
                    sb.AppendLine(byId.TryGetValue(q.Id, out var f)
                        ? $"  {q.Id}: textos [{string.Join(" | ", q.Texts.Select(t => t.Text.Length > 30 ? t.Text[..30] : t.Text))}] × [{string.Join(" | ", f.Texts.Select(t => t.Length > 30 ? t[..30] : t))}]"
                        : $"  {q.Id}: não existe no esquema completo (início {q.Start:N0}, textos em {q.TextStart:N0})");
            }
            var ex = all.FirstOrDefault(q => q.Texts[0].Text.Length > 20);
            if (ex is not null) sb.AppendLine($"exemplo {ex.Id} \"{ex.Name.Text}\": {ex.Texts[0].Text[..Math.Min(100, ex.Texts[0].Text.Length)]}");
        }
        catch (Exception e) { sb.AppendLine($"FALHOU ({sw.ElapsedMilliseconds} ms): {e.Message}"); }
        File.WriteAllText("tasks-texts.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --tasks-translate <destino> <traduzido> <saída>: traz os textos do traduzido (qualquer versão) pelo ID.
    private static void TasksTranslate(string dst, string src, string output)
    {
        var sb = new StringBuilder();
        var dd = File.ReadAllBytes(dst);
        var rd = TaskTexts.Read(dd); var rs = TaskTexts.Read(File.ReadAllBytes(src));
        int n = TaskTexts.Translate(rd, rs, dryRun: false);
        var o = TaskTexts.Build(dd, rd);
        File.WriteAllBytes(output, o);
        var back = TaskTexts.Read(o);
        sb.AppendLine($"destino v{rd.Version} · traduzido v{rs.Version} · {n:N0} textos trocados · relido: {back.Roots.Count:N0} quests · TasksFile: {(new TasksFile(o) is { } tf ? $"{tf.Quests.Count:N0} quests, regrava {(tf.CanWrite ? "OK" : tf.ReadOnlyReason)}" : "")}");
        File.WriteAllText("tasks-translate.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --fix-pair <pasta element do cliente> <pasta home do servidor>: aplica as correções recomendadas do "Corrigir o par"
    // (sem a troca de programas do servidor local, que pergunta antes) e grava fix-pair.txt.
    private static void FixPairCli(string client, string server)
    {
        var sb = new StringBuilder();
        foreach (var f in PairFixer.Plan(client, server).Where(f => f.Recommended))
            try { f.Apply(); sb.AppendLine("OK    " + f.Title); }
            catch (Exception ex) { sb.AppendLine($"FALHOU {f.Title}: {ex.Message}"); }
        File.WriteAllText("fix-pair.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --aipolicy-convert <entrada> <referência> <saída>: converte para o que o gs da referência lê (aipolicy-convert.txt).
    private static void AiConvert(string input, string reference, string output)
    {
        var sb = new StringBuilder();
        var src = new AiPolicyFile(input); var refFile = new AiPolicyFile(reference);
        if (!src.Ok || !refFile.Ok) { sb.AppendLine("leitura incompleta: " + (src.Error ?? refFile.Error)); File.WriteAllText("aipolicy-convert.txt", sb.ToString(), new UTF8Encoding(false)); return; }
        var lim = refFile.Limits();
        sb.AppendLine($"origem: {src.Limits()}\nreferência: {lim}");
        var rep = src.ConvertTo(lim);
        File.WriteAllBytes(output, src.Build());
        var back = new AiPolicyFile(output);
        sb.AppendLine($"{rep}\nresultado: {(back.Ok ? back.Limits().ToString() : "NÃO RELÊ: " + back.Error)} · {back.Policies.Count:N0} políticas · {back.Policies.Sum(p => p.Triggers.Count):N0} gatilhos");
        File.WriteAllText("aipolicy-convert.txt", sb.ToString(), new UTF8Encoding(false));
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

    // --masks <elements.data>: quais bits de proc_type/immune_type aparecem nos dados (masks-report.txt).
    private static void TestMasks(string dataPath)
    {
        var r = new CfgElementsReader(dataPath);
        var cfg = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"), $"*_v{r.Version & 0xFF}.cfg")[0];
        var module = r.Parse(ElementsCfg.Load(cfg));
        var sb = new StringBuilder();
        foreach (var field in new[] { "proc_type", "immune_type" })
        {
            var bitCount = new int[32]; int n = 0;
            foreach (var l in module.Lists)
                foreach (var it in l.Items)
                    if (it.Fields.TryGetValue(field, out var f) && long.TryParse(f.Value, out var v) && v > 0)
                    {
                        n++;
                        for (int b = 0; b < 32; b++) if ((v & (1L << b)) != 0) bitCount[b]++;
                    }
            sb.AppendLine($"{field}: {n:N0} itens com valor > 0");
            for (int b = 0; b < 32; b++) if (bitCount[b] > 0) sb.AppendLine($"  bit {b,2} (0x{1L << b:X}): {bitCount[b]:N0}");
        }
        File.WriteAllText("masks-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --bools <elements.data>: campos cujos valores são só 0/1 em todos os itens do bloco (bools.tsv).
    private static void TestBools(string dataPath)
    {
        var r = new CfgElementsReader(dataPath);
        var cfg = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"), $"*_v{r.Version & 0xFF}.cfg")[0];
        var module = r.Parse(ElementsCfg.Load(cfg));
        var sb = new StringBuilder();
        foreach (var l in module.Lists)
        {
            if (l.Items.Count == 0) continue;
            foreach (var key in l.Items[0].Fields.Keys)
            {
                bool allBin = true, anyOne = false;
                foreach (var it in l.Items)
                {
                    if (!it.Fields.TryGetValue(key, out var f)) { allBin = false; break; }
                    if (f.Value == "1") anyOne = true;
                    else if (f.Value != "0") { allBin = false; break; }
                }
                if (allBin && anyOne) sb.Append(l.Name).Append('\t').Append(key).Append('\n');
            }
        }
        File.WriteAllText("bools.tsv", sb.ToString(), new UTF8Encoding(false));
    }

    // --addons <elements.data>: descrição dos addons como no jogo (addons-report.txt).
    private static void TestAddons(string dataPath)
    {
        var module = CfgCatalog.Load(dataPath).Module;
        using var pck = new PckArchive(GameConfigs.FindConfigsPck(dataPath)!);
        var at = AddonText.Load(pck)!;
        var addons = module.FindListByName("EQUIPMENT_ADDON")!.Items;
        int ok = addons.Count(a => at.Describe(a) is not null);
        var sb = new StringBuilder($"tipos conhecidos: {at.Count} ids · addons com descrição: {ok} de {addons.Count}\n");
        foreach (var a in addons.Where((_, i) => i % 97 == 0).Take(30))
            sb.AppendLine($"  {a.Id} {a.Name} [tipo {at.TypeOf(a.Id)}] p=({a.Fields["param1"].Value},{a.Fields["param2"].Value}) => {at.Describe(a)}");
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
    // --convert: converte o elements.data para a versão do .cfg de destino e prova relendo (convert-report.txt).
    private static void ConvertElements(string dataPath, string targetCfg, string outPath)
    {
        var src = CfgCatalog.Load(dataPath).Module;
        var cfg = ElementsCfg.Load(targetCfg);
        int ver = CfgCatalog.VersionFromName(targetCfg) ?? throw new InvalidDataException("versão não está no nome do .cfg");
        var (data, rep) = ElementsConverter.Convert(src, cfg, ver);
        var err = ElementsConverter.Verify(data, cfg, src);
        File.WriteAllBytes(outPath, data);
        var sb = new StringBuilder($"v{src.Version & 0xFFFF} -> v{ver}: {rep.Items:N0} itens em {rep.Blocks} blocos; campos descartados {rep.FieldsDropped}, criados {rep.FieldsAdded}; blocos descartados {rep.BlocksDropped}, criados {rep.BlocksAdded}; {data.Length:N0} bytes\n");
        sb.AppendLine("prova (reler com o .cfg de destino): " + (err ?? "OK"));
        foreach (var n in rep.Notes.Take(20)) sb.AppendLine("  " + n);
        File.AppendAllText("convert-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --gshop <gshop.data> <gshopsev.data>: prova de leitura/gravação da loja (gshop-report.txt).
    private static void TestGShop(string clientPath, string serverPath)
    {
        var c = File.ReadAllBytes(clientPath);
        var f = GShopFile.ReadClient(c);
        var sb = new StringBuilder($"{Path.GetFileName(clientPath)}: ts {f.Timestamp}, {f.Items.Count} itens, categorias: {string.Join(" | ", f.Categories.Select(k => $"{k.Name} ({k.Subs.Count})"))}\n");
        sb.AppendLine($"cliente regravado idêntico: {f.WriteClient().AsSpan().SequenceEqual(c)}");
        if (File.Exists(serverPath))
        {
            var sv = File.ReadAllBytes(serverPath);
            var diffs = f.AttachServer(sv);
            sb.AppendLine($"servidor {Path.GetFileName(serverPath)}: diferenças cliente x servidor = {diffs.Count}");
            foreach (var x in diffs.Take(15)) sb.AppendLine("  " + x);
            sb.AppendLine($"servidor regravado (a partir do cliente) idêntico: {f.WriteServer().AsSpan().SequenceEqual(sv)}");
        }
        foreach (var it in f.Items.Take(3))
            sb.AppendLine($"  [{it.ShopId}] {it.Name} item {it.ItemId} x{it.Amount} cat {it.MainType}/{it.SubType} op0 {it.GetOption(0)} fig {it.Surface}");
        var st = f.Items.SelectMany(i => Enumerable.Range(0, 4).Select(i.GetOption)).Where(o => !o.IsEmpty).ToList();
        sb.AppendLine($"opções usadas {st.Count}; control_type: {string.Join(",", st.GroupBy(o => o.ControlType).Select(g => $"{g.Key}x{g.Count()}"))}; status: {string.Join(",", st.GroupBy(o => o.Status).Select(g => $"{g.Key}x{g.Count()}"))}; flags: {string.Join(",", st.GroupBy(o => o.Flags).Select(g => $"{g.Key}x{g.Count()}"))}; day: {string.Join(",", st.GroupBy(o => o.Day).Select(g => $"{g.Key}x{g.Count()}"))}");
        sb.AppendLine($"duração (s): {string.Join(",", st.GroupBy(o => o.Duration).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"))}");
        sb.AppendLine($"presentes: {f.Items.Count(i => i.GiftId != 0)}; log_price != 0: {f.Items.Count(i => i.LogPrice != 0)}");
        File.AppendAllText("gshop-report.txt", sb.ToString() + "\n", new UTF8Encoding(false));
    }

    // --errors <elements.data>: localizador de erros (errors-report.txt).
    private static void FindErrors(string dataPath)
    {
        var module = CfgCatalog.Load(dataPath).Module;
        var found = ErrorFinder.Find(module);
        var sb = new StringBuilder($"{found.Count} problemas\n");
        foreach (var g in found.GroupBy(p => (p.Kind, p.Block.Name, Group: System.Text.RegularExpressions.Regex.Replace(p.Field, @"\d+", "#"))).OrderByDescending(g => g.Count()))
            sb.AppendLine($"{g.Count(),6} {g.Key.Kind} {g.Key.Name} {g.Key.Group}  ex: {g.First().Item.Id} {g.First().Item.Name} = {g.First().Detail}");
        sb.AppendLine("\ndistribuições (devem somar 100%):"); foreach (var d in ErrorFinder.Distributions) sb.AppendLine("  " + d);
        File.WriteAllText("errors-report.txt", sb.ToString(), new UTF8Encoding(false));
    }

    private static void DumpRefs(string dataPath, string block, string id)
    {
        var module = CfgCatalog.Load(dataPath).Module;
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

    // --local-send <server files> <pasta element do cliente> <pasta do servidor local>: o mesmo "Enviar ao servidor" da aba
    // Servidor; programas de outra versão que a do cliente são trocados sem perguntar (banco novo, sem backup). Log em local-send.txt.
    private static void LocalSend(string serverFiles, string client, string baseDir)
    {
        var log = new StringBuilder();
        var s = new LocalServer(baseDir) { Log = line => { lock (log) log.AppendLine(line); } };
        try
        {
            client = VersionCheck.ClientElement(client);
            var swap = LocalServer.SwapFor(client, serverFiles, out var missing);   // sem perguntar: banco novo, sem backup
            if (missing is not null) log.AppendLine("== " + missing);
            var r = s.SendAll(swap?.Package ?? serverFiles, client, null, swap);
            log.AppendLine($"== código {r.Code}");
        }
        catch (Exception ex) { log.AppendLine("== ERRO " + ex.Message); }
        File.WriteAllText("local-send.txt", log.ToString(), Encoding.UTF8);
    }

    // --roundtrip <elements.data>: prova a gravação (roundtrip-report.txt). Não altera o arquivo de entrada.
    //  1) ler e gravar sem mudanças => bytes idênticos;
    //  2) editar o nome de 1 item => só bytes daquele registro mudam, e a releitura mostra o nome novo.
    private static void RoundTrip(string dataPath)
    {
        static Model.Module Load(string path)
        {
            return CfgCatalog.Load(path).Module;
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
        var module = CfgCatalog.Load(dataPath).Module;
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

    // --cfg-audit <pasta cfg> | --cfg-match <elements.data> [pasta cfg] | --cfg-batch <pasta com .data> [pasta cfg]
    // Diagnóstico só-leitura dos .cfg e do casamento de versão (contribuição LAOCHEN).
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
                var sb = new StringBuilder("Teste em lote de dados reais (só leitura, não altera os arquivos)\n");
                var files = Directory.EnumerateFiles(args[1], "*.data", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToList();
                int passed = 0;
                foreach (var file in files)
                {
                    sb.AppendLine($"\n===== {file} =====");
                    try { sb.AppendLine(CfgCatalog.Load(file, cfgDirectory: args.Length >= 3 ? args[2] : null).Report); passed++; }
                    catch (Exception ex) when (ex is InvalidDataException or CfgChoiceRequiredException) { sb.AppendLine("Falha: " + ex.Message); }
                }
                sb.AppendLine($"\nLeitura completa: {passed}/{files.Count} arquivos.");
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
