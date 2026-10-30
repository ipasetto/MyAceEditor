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
        if (args.Length >= 2 && args[0] == "--tasks-fixdiff") { TaskFixDiff(args[1]); return; }   // <tasks.data>: bloco fixo × início dos textos (tasks-fixdiff.txt)
        if (args.Length >= 2 && args[0] == "--tasks-layout") { TestTaskLayout(args.Skip(1).ToArray()); return; }   // <tasks.data>...: lê e regrava cada quest (tasks-layout.txt)
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
            if (t is not null)
                using (var tp = new PckArchive(Path.Combine(args[1], "textures.pck")))
                {
                    // camadas do chão das primeiras áreas e se cada textura existe no textures.pck
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int ar = 0; ar < 4; ar++)
                        for (int ac = 0; ac < 4; ac++)
                        {
                            TerrainFile.GroundLayer[]? ls = null;
                            try { ls = t.Ground(ar, ac); } catch (Exception ex) { sb.AppendLine($"área {ar},{ac}: ERRO {ex.Message}"); }
                            if (ls is null) continue;
                            sb.AppendLine($"área {ar},{ac}: {ls.Length} camadas");
                            foreach (var l in ls)
                                if (seen.Add(l.Texture)) sb.AppendLine($"  {l.Texture} ×{l.UScale}/{l.VScale} máscara {(l.Mask is null ? "-" : l.MaskSide.ToString())} · {(tp.Find(l.Texture.ToLowerInvariant()) is null ? "NÃO EXISTE" : "ok")}");
                        }
                }
            using (var lit = new PckArchive(Path.Combine(args[1], "litmodels.pck")))
            {
                var bmds = lit.Entries.Where(e => e.Path.StartsWith($@"litmodels\{args[2]}\", StringComparison.OrdinalIgnoreCase) && e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)).ToList();
                int ok = 0, bad = 0; string? firstBad = null;
                var texNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in bmds)
                {
                    if (BmdModel.Read(lit.Read(e)) is { } bm) { ok++; foreach (var mesh in bm.Meshes) texNames[mesh.Texture] = texNames.GetValueOrDefault(mesh.Texture) + 1; }
                    else { bad++; firstBad ??= e.Path; }
                }
                sb.AppendLine($"construções: {bmds.Count:N0} .bmd · lidas {ok} de {Math.Min(400, bmds.Count)} · falharam {bad}" + (firstBad is null ? "" : $" (ex.: {firstBad})"));
                // texturas das construções que não estão no building.pck (saem só com a cor do vértice)
                using var bp = new PckArchive(Path.Combine(args[1], "building.pck"));
                var others = new[] { "textures", "gfx", "models", "surfaces", "grasses", "trees" }
                    .Where(n => File.Exists(Path.Combine(args[1], n + ".pck"))).Select(n => (n, p: new PckArchive(Path.Combine(args[1], n + ".pck")))).ToList();
                foreach (var (tn, cnt) in texNames.OrderByDescending(k => k.Value))
                {
                    if (bp.Find(tn.ToLowerInvariant()) is not null) continue;
                    var where = others.Where(o => o.p.Find(tn.ToLowerInvariant()) is not null).Select(o => o.n).ToList();
                    sb.AppendLine($"  sem textura no building.pck: {tn} ({cnt} malhas){(where.Count > 0 ? " · está em " + string.Join(", ", where) : "")}");
                }
                foreach (var o in others) o.p.Dispose();
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
        if (args.Length >= 4 && args[0] == "--item-diff")   // <elements.data> <id> <id>: campos que diferem entre os dois itens (item-diff.txt)
        {
            var m = CfgCatalog.Load(args[1]).Module;
            (Model.Item It, string List)? Find(string id) => m.Lists.SelectMany(l => l.Items.Select(i => (i, l.Name))).FirstOrDefault(x => x.i.Id == id) is var r && r.i is not null ? r : null;
            var (a, b) = (Find(args[2]), Find(args[3]));
            var sb = new StringBuilder($"{args[2]}: {a?.List ?? "não existe"}\n{args[3]}: {b?.List ?? "não existe"}\n");
            foreach (var id in new[] { args[2], args[3] })   // o mesmo ID em mais de uma lista (o gs acha pelo ID em todas)
                sb.AppendLine($"{id} em: " + string.Join(", ", m.Lists.Where(l => l.Items.Any(i => i.Id == id)).Select(l => $"{l.Name} ({string.Join(" / ", l.Items.Where(i => i.Id == id).Select(i => i.Name))})")));
            if (a is { } x && b is { } y)
                foreach (var k in x.It.Fields.Keys.Union(y.It.Fields.Keys))
                    if (x.It.Fields.GetValueOrDefault(k)?.Value != y.It.Fields.GetValueOrDefault(k)?.Value)
                        sb.AppendLine($"{k}: {x.It.Fields.GetValueOrDefault(k)?.Value} | {y.It.Fields.GetValueOrDefault(k)?.Value}");
            File.WriteAllText("item-diff.txt", sb.ToString(), new UTF8Encoding(false));
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
        if (args.Length >= 2 && args[0] == "--tasks-recado") { TaskRecado(args[1]); return; }
        if (args.Length >= 2 && args[0] == "--tasks-site") { TaskSite(args[1]); return; }
        if (args.Length >= 3 && args[0] == "--tasks-svc") { TaskSvc(args[1], args[2]); return; }   // <elements.data> <tasks.data>: que ID (pai ou filha) os NPCs guardam no serviço de dar/receber   // <tasks.data>: perfil das quests "ir até um lugar" (tasks-site.txt)   // <tasks.data>: perfil das quests de recado (tasks-recado.txt)
        if (args.Length >= 2 && args[0] == "--quest-create") { TestQuestCreate(args[1]); return; }   // <tasks.data>: cria recado, relê e confere (quest-create.txt)
        if (args.Length >= 3 && args[0] == "--spt") { SptTest(args[1], args[2]); return; }   // <trees.pck> <pasta>: gera todas as árvores, spt-test.txt + PNG de lado
        if (args.Length >= 2 && args[0] == "--tasks-labels") { TaskLabels(args[1]); return; }   // <tasks.data>: rótulo, valor e dica de cada campo de uma quest com prêmio (tasks-labels.txt)
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
        if (args.Length >= 3 && args[0] == "--entrance")   // <gamed\config> <mapa>: portais do mapa e quem leva até ele
        {
            var sb = new StringBuilder();
            foreach (var d in Directory.GetDirectories(args[1]))
                if (RegionFile.Load(Path.Combine(d, "region.sev")) is { } rf)
                    foreach (var tr in rf.Transports)
                        if (Path.GetFileName(d).Equals(args[2], StringComparison.OrdinalIgnoreCase) || tr.TargetTag is var tt && sb.Length >= 0)
                            sb.AppendLine($"{Path.GetFileName(d)}: de {tr.SourceTag} para {tr.TargetTag} em {tr.Position.X:0},{tr.Position.Y:0},{tr.Position.Z:0} → destino {tr.Target.X:0},{tr.Target.Y:0},{tr.Target.Z:0}");
            sb.AppendLine("EntranceOf: " + RegionFile.EntranceOf(args[1], args[2]));
            File.WriteAllText("entrance.txt", sb.ToString());
            return;
        }
        if (args.Length >= 3 && args[0] == "--server-diff")   // <base do servidor local> <pasta server files> [element do cliente]: o que o envio copiaria (não para nada)
        {
            var ls = new LocalServer(args[1]);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var diff = args.Length >= 4 ? ls.PendingDiff(args[2], args[3], args.Length >= 5 ? args[4] : null) ?? new()
                : ls.ConfigDiff(Path.Combine(args[2], "home", "gamed", "config"), null);
            File.WriteAllText("server-diff.txt", $"{diff.Count} diferentes em {sw.Elapsed.TotalSeconds:0.0} s\n" + string.Join("\n", diff.Select(d => d.Rel)));
            return;
        }
        if (args.Length >= 4 && args[0] == "--world-shot") { WorldShot(args[1], args[2], args.Skip(3).ToArray()); return; }   // <pasta do mapa> <saída> x,y,z,yaw,pitch[,nome] ...
        if (args.Length >= 2 && args[0] == "--snapshot") { Snapshot(args[1], args.Length >= 3 ? args[2] : null, args.Length >= 5 ? (args[3], args[4]) : null, args.Length >= 6 ? args[5] : null); return; }
        AppLog.Info($"início · {Application.ProductVersion} · {Environment.OSVersion} · idioma {AppLang.Current}");
        UseAppIcon();
        // Painel do GM: programa à parte (--gm [pasta element], ou o exe renomeado para MyAceGM.exe) — docs/GM-PAINEL.md
        if ((args.Length >= 1 && args[0] == "--gm") || Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").StartsWith("MyAceGM", StringComparison.OrdinalIgnoreCase))
        {
            Application.Run(new GmPanel(args.Length >= 2 && args[0] == "--gm" ? args[1] : null));
            return;
        }
        Application.Run(new EditorForm());
    }

    /// <summary>
    /// Logo do programa (Assets/app.ico, embutido) como ícone padrão de todas as janelas: o WinForms usa Form.DefaultIcon
    /// quando a janela não define o seu, e esse padrão fica no campo estático s_defaultIcon. Se o campo mudar de nome numa
    /// versão futura do .NET, as janelas só voltam ao ícone genérico (o .exe continua com a logo via ApplicationIcon).
    /// </summary>
    static void UseAppIcon()
    {
        try
        {
            using var s = typeof(Program).Assembly.GetManifestResourceStream("MyAceEditor.app.ico");
            if (s is null) return;
            var icon = new Icon(s);
            typeof(Form).GetField("s_defaultIcon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, icon);
        }
        catch (Exception e) { AppLog.Info($"ícone do programa não carregou: {e.Message}"); }
    }

    // --snapshot <saida.png> [aba] [bloco id] [grupo]: abre o editor fora da tela (com a última pasta), salva um print e sai.
    // --world-shot <element\maps\aXX> <pasta de saída> x,y,z,yaw,pitch[,nome] ...: fotos do Mundo 3D (coordenadas do jogo; yaw
    // -90 = olhando para o norte, pitch negativo = para baixo), cada uma depois de terreno, construções e árvores carregarem
    private static void WorldShot(string mapDir, string outDir, string[] shots)
    {
        var t = TerrainFile.Open(mapDir) ?? throw new FileNotFoundException("terreno não encontrado", mapDir);
        Directory.CreateDirectory(outDir);
        // diagnóstico: água do cliente e alturas do chão numa grade de 9 × 9 (world-shot.txt)
        var diag = new StringBuilder($"{t.Name} {t.Width}×{t.Height} m\n");
        foreach (var a in ClientWater.Read(mapDir) ?? new()) diag.AppendLine($"água: cor {a.Argb:X8} altura {a.Height:0.0} {a.Cols}×{a.Rows}×{a.Cell} centro {a.X:0},{a.Z:0}");
        for (int i = 0; i <= 8; i++)
            diag.AppendLine(string.Join(" ", Enumerable.Range(0, 9).Select(j => t.HeightAt(-t.Width / 2 + t.Width * j / 8f, t.Height / 2 - t.Height * i / 8f) is float h ? $"{h,6:0}" : "     -")));
        // "dense[N]": câmera diante do N-ésimo pedaço de 128 m com mais árvores do .ecwld (para conferir árvores)
        if (Directory.EnumerateFiles(mapDir, "*.ecwld").FirstOrDefault() is { } ecwld)
        {
            var scene = new WorldScene(ecwld);
            var cells = scene.Trees.GroupBy(tr => ((int)MathF.Floor(tr.X / 128), (int)MathF.Floor(tr.Z / 128))).OrderByDescending(g => g.Count()).ToList();
            shots = shots.Select(s =>
            {
                if (!s.StartsWith("dense")) return s;
                int n = s.Length > 5 ? int.Parse(s[5..]) : 0;
                var g = cells[Math.Min(n, cells.Count - 1)];
                float cx = g.Average(tr => tr.X), cz = g.Average(tr => tr.Z);
                foreach (var ty in g.GroupBy(tr => tr.Type))
                    diag.AppendLine($"{s}: tipo {ty.Key} ×{ty.Count()} {scene.TreeTypes[ty.Key].Spt} | {scene.TreeTypes[ty.Key].CompositeMap}");
                float ground = t.HeightAt(cx, cz - 70) ?? g.Average(tr => tr.Y);
                return FormattableString.Invariant($"{cx},{ground + 25},{cz - 70},-90,-12,{s}");
            }).ToArray();
        }
        var cams = shots.Select(s => s.Split(',')).Select(p => (Pos: new OpenTK.Mathematics.Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)),
            Yaw: float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), Pitch: float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture), Name: p.Length > 5 ? p[5] : null)).ToList();
        File.WriteAllText(Path.Combine(outDir, "world-shot.txt"), diag.ToString());
        using var w = new MyAceEditor.UI.WorldView(t, cams[0].Pos, () => Array.Empty<MyAceEditor.UI.WorldView.Marker>(), () => null, _ => { }, (_, _) => { }, null)
        { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0), Size = new System.Drawing.Size(1280, 820), ShowInTaskbar = false };
        w.Show();
        for (int i = 0; i < cams.Count; i++)
        {
            var c = cams[i];
            w.SetCamera(c.Pos, c.Yaw, c.Pitch);
            var until = DateTime.Now.AddSeconds(90); var quiet = DateTime.MaxValue;
            while (DateTime.Now < until)
            {
                Application.DoEvents(); Thread.Sleep(30);
                if (w.Loading) quiet = DateTime.MaxValue; else if (quiet == DateTime.MaxValue) quiet = DateTime.Now; else if ((DateTime.Now - quiet).TotalSeconds > 1.5) break;
            }
            using var bmp = w.CaptureFrame();
            bmp?.Save(Path.Combine(outDir, (c.Name ?? $"shot{i + 1}") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        w.Close();
    }

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
    private static void TaskFixDiff(string path)
    {
        var d = File.ReadAllBytes(path);
        int vv = BitConverter.ToInt32(d, 4);
        var empty = new MemoryStream();
        using (var ew = new BinaryWriter(empty, Encoding.UTF8, true)) TaskLayout.WriteFixed(ew, vv, new TaskRec());
        var res = TaskTexts.Read(d, skipMissing: true, max: int.TryParse(Environment.GetEnvironmentVariable("TL_MAX"), out int mx) ? mx : int.MaxValue);
        var hist = new Dictionary<string, int>();
        var parentOf = new Dictionary<int, int>();
        foreach (var p in TaskTexts.All(res.Roots)) foreach (var c in p.Children) parentOf[c.Id] = p.Id;
        int dumped = 0;
        var samples = new List<string>();
        using var ms = new MemoryStream(d);
        using var r = new BinaryReader(ms);
        foreach (var n in TaskTexts.All(res.Roots))
        {
            string key;
            TaskRec? rec = null;
            try { using var qs = new MemoryStream(d, n.Start, n.End - n.Start); using var qr = new BinaryReader(qs); rec = TaskLayout.ReadFixed(qr, res.Version); key = (qs.Position - (n.TextStart - n.Start)).ToString(); }
            catch (Exception ex) { key = "erro " + ex.GetType().Name; }
            hist[key] = hist.GetValueOrDefault(key) + 1;
            if (rec != null && parentOf.ContainsKey(n.Id) && Environment.GetEnvironmentVariable("TL_DUMP") is { } dk && dk == key && dumped++ < 6)
            {
                File.WriteAllBytes($"real_{n.Id}.bin", d[n.Start..n.TextStart]);
                File.WriteAllLines($"ours_{n.Id}.txt", rec.Order.Select((f, i) => $"{rec.Pos[i]}\t{f.Key}\t{f.Type}\t{(rec.V[f.Key] is byte[] ? "" : rec.V[f.Key])}")
                    .Prepend($"parent\t{parentOf[n.Id]}"));
            }
            if (key != "0" && samples.Count < 12)
                samples.Add($"{n.Id} fixo={n.TextStart - n.Start} dif={key}: " + (rec == null ? "" : string.Join(" ", rec.Order.Where(o => (o.Key.Contains("Cnt") || o.Key.Contains("Num") || o.Key.StartsWith("m_ul")) && rec.Int(o.Key) != 0 && !o.Key.Contains('[')).Take(40).Select(o => $"{o.Key}={rec.Int(o.Key)}"))));
        }
        File.WriteAllText("tasks-fixdiff.txt", $"v{res.Version} vazio={empty.Length} menores={string.Join(",", TaskTexts.All(res.Roots).Select(n => n.TextStart - n.Start).Distinct().Order().Take(8))}\n" + string.Join("\n", hist.OrderByDescending(h => h.Value).Select(h => $"{h.Key}: {h.Value}")) + "\n\n" + string.Join("\n", samples), new UTF8Encoding(false));
    }

    private static void TestTaskLayout(string[] paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = File.ReadAllBytes(path);
            int ver = BitConverter.ToInt32(data, 4), count = BitConverter.ToInt32(data, 8);
            int at = 12 + (ver >= 223 ? 4 : 0);
            if (int.TryParse(Environment.GetEnvironmentVariable("TL_MAX"), out int max)) count = Math.Min(count, max);
            var offs = Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(data, at + i * 4)).ToArray();
            int ok = 0, bad = 0, fields = 0;
            var errs = new List<string>();
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            for (int i = 0; i < count; i++)
            {
                int end = i + 1 < offs.Length ? offs[i + 1] : BitConverter.ToInt32(data, at + (i + 1) * 4) is int nx && i + 1 < BitConverter.ToInt32(data, 8) ? nx : data.Length;
                try
                {
                    ms.Position = offs[i];
                    var q = TaskLayout.Read(r, ver);
                    if (ms.Position != end) throw new InvalidDataException($"terminou em {ms.Position - offs[i]} de {end - offs[i]} bytes");
                    using var o = new MemoryStream();
                    using (var w = new BinaryWriter(o, Encoding.UTF8, true)) TaskLayout.Write(w, ver, q);
                    var a = o.ToArray();
                    int diff = Enumerable.Range(0, Math.Min(a.Length, end - offs[i])).FirstOrDefault(k => a[k] != data[offs[i] + k], -1);
                    if (a.Length != end - offs[i] || diff >= 0) throw new InvalidDataException($"regravado {a.Length} B, 1ª diferença em {diff}");
                    fields += q.Fixed.Order.Count;
                    ok++;
                }
                catch (Exception ex)
                {
                    bad++; if (errs.Count < 8) errs.Add($"  quest #{i} @{offs[i]}: {ex.Message}");
                    if (bad == 1 && TaskLayout.LastRead is { } lr)
                        File.WriteAllLines("tasks-layout-dump.txt", lr.Order.Select(o => $"{o.Key}	{o.Type}	{(lr.V[o.Key] is byte[] b ? Convert.ToHexString(b.AsSpan(0, Math.Min(b.Length, 16))) : lr.V[o.Key])}"));
                }
            }
            sb.AppendLine($"{path}\n  v{ver}: {count:N0} quests, {ok:N0} idênticas, {bad:N0} com erro, {fields / Math.Max(ok, 1)} campos/quest, {sw.Elapsed.TotalSeconds:0.0}s");
            errs.ForEach(e => sb.AppendLine(e));
        }
        File.WriteAllText("tasks-layout.txt", sb.ToString(), new UTF8Encoding(false));
    }

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

    // --tasks-recado <tasks.data>: como são as quests de recado reais (NPC que dá ≠ NPC que recebe, sem matar/coletar/lugar):
    // histograma dos campos-chave e dos diálogos, para escolher o modelo de quest nova (tasks-recado.txt)
    private static void TaskRecado(string path)
    {
        var t = new TasksFile(path);
        var keys = new[] { "m_enumMethod", "m_enumFinishType", "m_ulAwardType_S", "m_ulType", "m_bCanGiveUp", "m_bCanRedo", "m_bNeedRecord", "m_bCanSeekOut", "m_bShowPrompt", "m_bAutoDeliver" };
        var hist = keys.ToDictionary(k => k, _ => new Dictionary<string, int>());
        var talks = new Dictionary<string, int>();
        var samples = new List<string>();
        int n = 0;
        foreach (var q in t.Roots!)
        {
            if (q.Children.Count > 0) continue;
            string V(string k) => q.Field(k) is { } f ? q.Get(f) : "-";
            if (V("m_ulDelvNPC") is "0" or "-" || V("m_ulAwardNPC") is "0" or "-" || V("m_ulDelvNPC") == V("m_ulAwardNPC")) { q.Fields = null!; continue; }
            if (V("m_ulMonsterWanted") != "0" || V("m_ulItemsWanted") != "0" || V("m_ulReachSiteCnt") is not ("0" or "-") || V("m_ulPremItems") != "0") { q.Fields = null!; continue; }
            n++;
            foreach (var k in keys) { var v = V(k); hist[k][v] = hist[k].GetValueOrDefault(v) + 1; }
            var sig = string.Join(",", q.Talks.Select(tp => tp.Windows.Count));
            talks[sig] = talks.GetValueOrDefault(sig) + 1;
            if (samples.Count < 8) samples.Add($"{q.Id} \"{TasksFile.SplitColor(q.Name).Text}\" dá {V("m_ulDelvNPC")} recebe {V("m_ulAwardNPC")} método {V("m_enumMethod")} conclusão {V("m_enumFinishType")} diálogos {sig} textos {string.Join("|", q.Texts.Select(x => x.Length))}");
            q.Fields = null!;
        }
        var sb = new StringBuilder($"v{t.Version}: {n:N0} quests de recado (de {t.Roots.Count:N0} principais)\n");
        foreach (var k in keys) sb.AppendLine($"{k}: " + string.Join("  ", hist[k].OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key}×{x.Value}")));
        sb.AppendLine("janelas por diálogo (5 diálogos): " + string.Join("  ", talks.OrderByDescending(x => x.Value).Take(6).Select(x => $"[{x.Key}]×{x.Value}")));
        sb.AppendLine(string.Join("\n", samples));
        File.WriteAllText("tasks-recado.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --tasks-site <tasks.data>: como são as quests "ir até um lugar" reais (m_ulReachSiteCnt > 0, principais e subquests):
    // método/conclusão, posição na árvore (pai em ordem, irmãos), tamanho e mapa das caixas (tasks-site.txt)
    private static void TaskSite(string path)
    {
        var t = new TasksFile(path);
        var hist = new Dictionary<string, Dictionary<string, int>>();
        void Add(string k, string v) { var h = hist.TryGetValue(k, out var x) ? x : hist[k] = new(); h[v] = h.GetValueOrDefault(v) + 1; }
        var samples = new List<string>();
        int n = 0;
        foreach (var q in t.Roots!.SelectMany(r => r.Descendants().Prepend(r)))
        {
            var rec = TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(q.Fixed, false)), t.Version);
            int sites = (int)rec.Int("m_ulReachSiteCnt");
            if (sites == 0) continue;
            n++;
            Add("método", rec.Int("m_enumMethod").ToString());
            Add("conclusão", rec.Int("m_enumFinishType").ToString());
            Add("locais", sites.ToString());
            Add("subquest", q.Parent is null ? "principal" : "filha");
            Add("filhas", q.Children.Count.ToString());
            Add("NPC que dá>0", (rec.Int("m_ulDelvNPC") > 0).ToString());
            Add("NPC que recebe>0", (rec.Int("m_ulAwardNPC") > 0).ToString());
            Add("prêmio S", rec.Int("m_ulAwardType_S").ToString());
            if (q.Parent is { } p)
            {
                var pr = TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(p.Fixed, false)), t.Version);
                Add("pai em ordem", pr.Int("m_bExeChildInOrder").ToString());
                Add("pai escolhe 1", pr.Int("m_bChooseOne").ToString());
                Add("pai método", pr.Int("m_enumMethod").ToString());
                Add("irmãos (métodos)", string.Join(",", p.Children.Select(c => TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(c.Fixed, false)), t.Version).Int("m_enumMethod"))));
            }
            float F(string k) => rec.V.TryGetValue(k, out var o) ? Convert.ToSingle(o) : float.NaN;
            var b = "m_pReachSite[0].";
            float dx = F(b + "zvMax.v[0]") - F(b + "zvMin.v[0]"), dy = F(b + "zvMax.v[1]") - F(b + "zvMin.v[1]"), dz = F(b + "zvMax.v[2]") - F(b + "zvMin.v[2]");
            Add("largura x (m)", (MathF.Round(dx / 10) * 10).ToString());
            Add("altura y (m)", (MathF.Round(dy / 10) * 10).ToString());
            Add("mapa", rec.Int(b + "MapID").ToString());
            Add("mundo (m_ulReachSiteId)", rec.Int("m_ulReachSiteId").ToString());
            if (samples.Count < 12)
                samples.Add($"{q.Id} \"{TasksFile.SplitColor(q.Name).Text}\" {(q.Parent is null ? "" : $"filha de {q.Parent.Id} ")}método {rec.Int("m_enumMethod")} conclusão {rec.Int("m_enumFinishType")} "
                    + $"dá {rec.Int("m_ulDelvNPC")} recebe {rec.Int("m_ulAwardNPC")} mapa {rec.Int(b + "MapID")} min ({F(b + "zvMin.v[0]"):0},{F(b + "zvMin.v[1]"):0},{F(b + "zvMin.v[2]"):0}) tam ({dx:0},{dy:0},{dz:0})");
        }
        // pais das etapas "ir até": como o pai e as filhas dividem NPCs, prêmio e diálogos
        long Flag(TaskRec r, string k) => QuestOptions.SwitchIndex(t.Version, k) is >= 0 and var i
            ? r.V.TryGetValue("CombinedSwitches", out var o) && o is byte[] cb && i / 8 < cb.Length ? (cb[i / 8] >> (i % 8)) & 1 : 0 : r.Int(k);
        TaskRec R(QuestNode x) => TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(x.Fixed, false)), t.Version);
        string Talks(QuestNode x) => string.Join(",", x.Talks.Select(tp => tp.Windows.Count));
        var parentSamples = new List<string>();
        foreach (var p in t.Roots!.SelectMany(r => r.Descendants().Prepend(r)).Where(p => p.Parent is null && p.Children.Count is >= 2 and <= 4))
        {
            var kids = p.Children.Select(R).ToList();
            if (!kids.Any(k => k.Int("m_enumMethod") == 4)) continue;
            var pr = R(p);
            var sig = string.Join(",", kids.Select(k => k.Int("m_enumMethod")));
            Add("P pai: método/conclusão", $"{pr.Int("m_enumMethod")}/{pr.Int("m_enumFinishType")}");
            Add("P pai: em ordem", Flag(pr, "m_bExeChildInOrder").ToString());
            Add("P pai: dá/recebe>0", $"{pr.Int("m_ulDelvNPC") > 0}/{pr.Int("m_ulAwardNPC") > 0}");
            Add("P pai: ouro/exp>0", $"{pr.Int("m_Award_S.m_ulGoldNum") > 0}/{pr.Int("m_Award_S.m_ulExp") > 0}");
            Add("P pai: diálogos", Talks(p));
            Add("P filhas: métodos", sig);
            Add("P filhas: conclusão", string.Join(",", kids.Select(k => k.Int("m_enumFinishType"))));
            Add("P filhas: recebe>0", string.Join(",", kids.Select(k => k.Int("m_ulAwardNPC") > 0 ? 1 : 0)));
            Add("P filhas: exp>0", string.Join(",", kids.Select(k => k.Int("m_Award_S.m_ulExp") > 0 ? 1 : 0)));
            Add("P filhas: diálogos", string.Join(" ", p.Children.Select(Talks)));
            Add("P filhas: textos", string.Join(" ", p.Children.Select(c => string.Join("|", c.Texts.Select(x => x.Length > 0 ? 1 : 0)))));
            Add("P ligações pai/filha/irmã", $"{pr.Int("m_ulFirstChild") == p.Children[0].Id}/{kids[0].Int("m_ulParent") == p.Id}/{kids[0].Int("m_ulNextSibling") == p.Children[1].Id}");
            if (parentSamples.Count < 6 && sig is "4,3" or "4,1" or "3,4")
                parentSamples.Add($"{p.Id} \"{TasksFile.SplitColor(p.Name).Text}\" pai método {pr.Int("m_enumMethod")} conclusão {pr.Int("m_enumFinishType")} dá {pr.Int("m_ulDelvNPC")} recebe {pr.Int("m_ulAwardNPC")} · filhas "
                    + string.Join(" | ", p.Children.Zip(kids).Select(z => $"{z.First.Id} \"{TasksFile.SplitColor(z.First.Name).Text}\" m{z.Second.Int("m_enumMethod")} c{z.Second.Int("m_enumFinishType")} dá {z.Second.Int("m_ulDelvNPC")} recebe {z.Second.Int("m_ulAwardNPC")} diál {Talks(z.First)}")));
        }
        var sb = new StringBuilder($"v{t.Version}: {n:N0} quests com local a alcançar\n");
        foreach (var (k, h) in hist) sb.AppendLine($"{k}: " + string.Join("  ", h.OrderByDescending(x => x.Value).Take(8).Select(x => $"{x.Key}×{x.Value}")));
        sb.AppendLine(string.Join("\n", samples));
        sb.AppendLine(string.Join("\n", parentSamples));
        File.WriteAllText("tasks-site.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --tasks-svc <elements.data> <tasks.data>: nas quests com etapas, o NPC que dá guarda o ID do pai ou da 1ª filha no serviço de
    // dar? E o NPC de uma etapa "falar com NPC" (filha m3 c1) guarda o ID da filha ou do pai no serviço de receber? (tasks-svc.txt)
    private static void TaskSvc(string dataPath, string tasksPath)
    {
        var module = CfgCatalog.Load(dataPath).Module;
        var t = new TasksFile(tasksPath);
        HashSet<string> Svc(int npc, bool give)
        {
            var set = new HashSet<string>();
            var n = module.FindListByName("NPC_ESSENCE")?.FindById(npc.ToString());
            string field = give ? "id_task_out_service" : "id_task_in_service";
            if (n is null || !n.Fields.TryGetValue(field, out var sf)) return set;
            var svc = module.FindListByName(give ? "NPC_TASK_OUT_SERVICE" : "NPC_TASK_IN_SERVICE")?.FindById(sf.Value);
            if (svc is null) return set;
            foreach (var (k, f) in svc.Fields) if (k.Contains("id_tasks") && !k.Contains("space") && f.Value != "0") set.Add(f.Value);
            return set;
        }
        TaskRec R(QuestNode x) => TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(x.Fixed, false)), t.Version);
        var hist = new Dictionary<string, int>();
        void Add(string k) => hist[k] = hist.GetValueOrDefault(k) + 1;
        foreach (var p in t.Roots!.Where(r => r.Children.Count >= 2))
        {
            var pr = R(p);
            if (pr.Int("m_ulDelvNPC") is var g and > 0)
            {
                var o = Svc((int)g, true);
                Add($"dar: pai {o.Contains(p.Id.ToString())} / 1a filha {o.Contains(p.Children[0].Id.ToString())}");
            }
            foreach (var c in p.Children)
            {
                var cr = R(c);
                if (cr.Int("m_enumMethod") != 3 || cr.Int("m_enumFinishType") != 1 || cr.Int("m_ulAwardNPC") is not (var a and > 0)) continue;
                var i = Svc((int)a, false);
                Add($"receber (etapa falar): filha {i.Contains(c.Id.ToString())} / pai {i.Contains(p.Id.ToString())}");
            }
        }
        File.WriteAllText("tasks-svc.txt", string.Join("\n", hist.OrderByDescending(x => x.Value).Select(x => $"{x.Key}: {x.Value}")), new UTF8Encoding(false));
    }

    // --quest-create <tasks.data>: cria uma quest de recado pelo QuestBuilder, grava, relê e confere; tirando a quest nova
    // a regravação tem que ser idêntica ao original (quest-create.txt)
    private static void TestQuestCreate(string path)
    {
        var original = File.ReadAllBytes(path);
        var t = new TasksFile(original);
        var sb = new StringBuilder($"v{t.Version}\n");
        var tpl = QuestBuilder.FindRecadoTemplate(t);
        sb.AppendLine($"modelo: {(tpl is null ? "NENHUM" : $"{tpl.Id} \"{tpl.Name}\"")}");
        if (tpl is null) { File.WriteAllText("quest-create.txt", sb.ToString(), new UTF8Encoding(false)); return; }
        string V(QuestNode q, string k) => q.Field(k) is { } f ? q.Get(f) : "-";
        var d = new QuestDraft
        {
            Id = t.NextFreeId(), Name = "Recado de teste", Giver = int.Parse(V(tpl, "m_ulAwardNPC")), Receiver = int.Parse(V(tpl, "m_ulDelvNPC")),
            MinLevel = 10, MaxLevel = 50, Gold = 1234, Exp = 5678, Sp = 90, Items = { (11208, 3), (11209, 1) },
            Description = "Leve esta carta.", OfferText = "Pode levar esta carta para mim?", RewardText = "Obrigado pela carta!",
        };
        QuestOption Opt(string id) => QuestOptions.All.First(o => o.Id == id);
        var model = QuestBuilder.ModelRec(t)!;
        foreach (var (id, val) in new (string, object)[]
        {
            ("classes", new List<int> { 0, 6 }), ("gender", 2L), ("preTasks", new List<int> { tpl.Id }), ("premItems", new List<(int, int)> { (11208, 1) }),
            ("spouse", true), ("redo", true), ("newTask", new List<int> { tpl.Id }), ("bank", 2L), ("levCo", true),
        })
            if (QuestOptions.Available(Opt(id), t.Version, model)) d.Choices.Add(new QuestChoice(Opt(id), val));
        sb.AppendLine("opções: " + string.Join(", ", d.Choices.Select(c => c.Option.Id)) + " · indisponíveis nesta versão: "
            + string.Join(", ", QuestOptions.All.Where(o => !QuestOptions.Available(o, t.Version, model)).Select(o => o.Id)));
        d.RandomItem = true;
        var errors = QuestBuilder.Validate(t, d, _ => true, _ => true);
        sb.AppendLine("validação: " + (errors.Count == 0 ? "OK" : string.Join("; ", errors)));
        var q = QuestBuilder.Build(t, d);
        var bytes = t.Build();
        var back = new TasksFile(bytes);
        var nq = back.Index.Find(d.Id) as QuestNode;
        sb.AppendLine($"relido: {(back.FullSchema ? "OK" : back.SchemaError)} · {back.Roots?.Count:N0} principais (antes {t.Roots!.Count - 1:N0}) · regrava {(back.CanWrite ? "OK" : back.ReadOnlyReason)}");
        if (nq is not null)
        {
            foreach (var k in new[] { "m_ID", "m_ulDelvNPC", "m_ulAwardNPC", "m_enumMethod", "m_enumFinishType", "m_ulPremise_Lev_Min", "m_ulPremise_Lev_Max",
                         "m_ulPremise_Task_Count", "m_Award_S.m_ulGoldNum", "m_Award_S.m_ulExp", "m_Award_S.m_ulSP", "m_Award_S.m_ulCandItems",
                         "m_Award_S.m_CandItems[0].m_ulAwardItems", "m_Award_S.m_CandItems[0].m_AwardItems[0].m_ulItemTemplId",
                         "m_Award_S.m_CandItems[0].m_AwardItems[0].m_ulItemNum", "m_Award_S.m_CandItems[0].m_AwardItems[1].m_ulItemTemplId", "m_Award_S.m_ulNewTask",
                         "m_ulOccupations", "m_Occupations[0]", "m_Occupations[1]", "m_ulGender", "m_ulPremise_Tasks[0]", "m_ulPremItems",
                         "m_PremItems[0].m_ulItemTemplId", "m_bCanRedo", "m_Award_S.m_ulStorehouseSize", "m_Award_S.m_CandItems[0].m_bRandChoose", "m_Award_S.m_bUseLevCo" })
                sb.AppendLine($"  {k} = {V(nq, k)}");
            // bits de CombinedSwitches (v135+) ou campo
            var rr = TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(nq.Fixed)), back.Version);
            foreach (var k in new[] { "m_bPremise_Spouse", "m_bGM", "m_bShowByLev", "m_bCanRedo" })
            {
                int bi = QuestOptions.SwitchIndex(back.Version, k);
                var on = bi >= 0 ? (((byte[])rr.V["CombinedSwitches"])[bi / 8] >> bi % 8 & 1).ToString() : V(nq, k);
                sb.AppendLine($"  {k} = {on}{(bi >= 0 ? $" (bit {bi})" : "")}");
            }
            sb.AppendLine($"  nome \"{nq.Name}\" · descrição \"{nq.Texts[0]}\" · ao dar \"{nq.Talks[0].Windows[0].Text}\" · prêmio \"{nq.Talks[4].Windows[0].Text}\"");
            sb.AppendLine("  opções " + string.Join(" ", nq.Talks.SelectMany((tp, ti) => tp.Windows.SelectMany(w => w.Options.Select(o => $"t{ti}:{o.Param}/{o.Id}")))));
            back.DeleteRoot(nq);
            sb.AppendLine($"sem a quest nova, regravação: {(back.Build().AsSpan().SequenceEqual(original) ? "IDÊNTICA ao original" : "DIFERENTE")}");
        }
        else sb.AppendLine("quest nova NÃO encontrada no arquivo relido");

        // quest com etapas (tipo 2): ir a 2 lugares (o 1º com 2 áreas) e depois falar com um NPC; uma vez em ordem, outra "basta uma"
        foreach (var order in new[] { QuestStepOrder.InOrder, QuestStepOrder.AnyOne })
        {
            var t2 = new TasksFile(original);
            int npc = int.Parse(V(tpl, "m_ulAwardNPC"));
            var d2 = new QuestDraft
            {
                Id = t2.NextFreeId(), Name = "Etapas de teste", Giver = npc, MinLevel = 5, Exp = 777, Order = order,
                Description = "Vá aos dois lugares e volte.", OfferText = "Pode explorar para mim?",
                Steps =
                {
                    new QuestStep { Kind = QuestStepKind.Reach, Name = "Primeiro lugar", MapTag = 1, Areas = { new QuestArea { X0 = -10, Y0 = -50, Z0 = -20, X1 = 10, Y1 = 150, Z1 = 20 }, new QuestArea { X0 = 100, Y0 = 0, Z0 = 100, X1 = 130, Y1 = 60, Z1 = 140 } } },
                    new QuestStep { Kind = QuestStepKind.Reach, Name = "Segundo lugar", Description = "Agora o outro.", MapTag = 1, Areas = { new QuestArea { X0 = 500, Y0 = -9999, Z0 = 600, X1 = 520, Y1 = 9999, Z1 = 640 } } },
                    new QuestStep { Kind = QuestStepKind.Talk, Name = "Volte ao NPC", Npc = npc, Text = "Você voltou! Tome o prêmio." },
                },
            };
            var err2 = QuestBuilder.Validate(t2, d2, _ => true, _ => true);
            sb.AppendLine($"\n[etapas, {order}] validação: " + (err2.Count == 0 ? "OK" : string.Join("; ", err2)));
            if (err2.Count > 0) continue;
            QuestBuilder.Build(t2, d2);
            var back2 = new TasksFile(t2.Build());
            sb.AppendLine($"relido: {(back2.FullSchema ? "OK" : back2.SchemaError)} · regrava {(back2.CanWrite ? "OK" : back2.ReadOnlyReason)}");
            if (back2.Index.Find(d2.Id) is not QuestNode p2) { sb.AppendLine("principal NÃO encontrada"); continue; }
            long Flag(QuestNode x, string k)
            {
                var r = TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(x.Fixed)), back2.Version);
                int bi = QuestOptions.SwitchIndex(back2.Version, k);
                return bi >= 0 ? ((byte[])r.V["CombinedSwitches"])[bi / 8] >> bi % 8 & 1 : r.Int(k);
            }
            foreach (var x in p2.Children.Prepend(p2))
            {
                sb.Append($"  {x.Id} \"{x.Name}\" pai {(x.Parent?.Id ?? 0)}:");
                foreach (var k in new[] { "m_enumMethod", "m_enumFinishType", "m_ulDelvNPC", "m_ulAwardNPC", "m_ulParent", "m_ulPrevSibling", "m_ulNextSibling", "m_ulFirstChild",
                             "m_ulPremise_Lev_Min", "m_Award_S.m_ulExp", "m_ulReachSiteCnt", "m_ulReachSiteId", "m_pReachSite[0].zvMin.v[0]", "m_pReachSite[0].zvMax.v[2]", "m_pReachSite[1].zvMin.v[0]", "m_pReachSite[0].MapID" })
                    sb.Append($" {k.Replace("m_", "")}={V(x, k)}");
                sb.AppendLine($" · ordem {Flag(x, "m_bExeChildInOrder")} paiTambém {Flag(x, "m_bParentAlsoSucc")} · desc \"{x.Texts[0]}\" · diálogos {string.Join(",", x.Talks.Select(tp => tp.Windows.Count))}"
                    + (x.Talks[0].Windows.Count > 0 ? $" ao dar \"{x.Talks[0].Windows[0].Text}\"" : "") + (x.Talks[4].Windows.Count > 0 ? $" prêmio \"{x.Talks[4].Windows[0].Text}\"" : "")
                    + " · opções " + string.Join(" ", x.Talks.SelectMany((tp, ti) => tp.Windows.SelectMany(w => w.Options.Select(o => $"t{ti}:{o.Param}/{o.Id}")))));
            }
            back2.DeleteRoot(p2);
            sb.AppendLine($"sem a quest nova, regravação: {(back2.Build().AsSpan().SequenceEqual(original) ? "IDÊNTICA ao original" : "DIFERENTE")}");
        }
        File.WriteAllText("quest-create.txt", sb.ToString(), new UTF8Encoding(false));
    }

    // --tasks-labels: revisar rótulos/dicas da tabela de campos (PerfectWorld/cfg/quest_fields.pt.tsv)
    private static void TaskLabels(string path)
    {
        var t = new TasksFile(path);
        var q = t.AllNodes().First(n => n.Fields.Count(f => f.Name.Contains("m_CandItems")) > 4);
        File.WriteAllLines("tasks-labels.txt", q.Fields.Select(f => $"{QuestFieldInfo.GroupLabel(f.Group)} | {QuestFieldInfo.Label(f.Name, true)} = {q.Get(f)}   [{f.Name}]   {QuestFieldInfo.Tip(f.Name).Replace('\n', '/')}"));
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
        // NPC que dá / recebe: mesma posição em todas as quests (índice por NPC do Mundo 3D lê direto do bloco)
        foreach (var key in new[] { "m_ulDelvNPC", "m_ulAwardNPC" })
        {
            int off0 = all[0].Field(key)!.Offset;
            int bad = all.Count(n => n.Field(key)?.Offset != off0);
            sb.AppendLine($"posição de {key}: {(bad == 0 ? $"fixa em {off0} em todas" : $"VARIA em {bad:N0} quests")}");
            foreach (var n in all) n.Fields = null!;   // solta a memória dos campos montados
        }
        var giver = all.Select(n => n.Get(n.Field("m_ulDelvNPC")!)).Where(v => v != "0").GroupBy(v => v).OrderByDescending(g => g.Count()).First();
        sb.AppendLine($"NPC que mais dá quests: {giver.Key} ({giver.Count()}); índice por NPC: {t.QuestsOfNpc(int.Parse(giver.Key), true).Count}");
        // campo escalar (mesmo nome em todas as versões via QuestKeys) e clonar
        var fAward = again.Roots![1].Field("NPC do prêmio")!;
        again.Roots[1].Set(fAward, "12345");
        var clone = again.CloneRoot(again.Roots[1]);
        var third = new TasksFile(again.Build());
        sb.AppendLine($"editar campo + clonar: {(third.FullSchema && third.Roots![1].Get(third.Roots[1].Field("NPC do prêmio")!) == "12345" && third.Index.Find(clone.Id) is QuestNode c && c.Name == q0.Name ? "OK" : "FALHOU " + third.SchemaError)} (clone {clone.Id}, regrava {(third.CanWrite ? "OK" : third.ReadOnlyReason)})");
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
    private static void SptTest(string pckPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        using var pck = new PckArchive(pckPath);
        var sb = new StringBuilder();
        foreach (var e in pck.Entries.Where(e => e.Path.EndsWith(".spt", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var f = new SptFile(pck.Read(e));
                var m = SptGrower.Build(f);
                static string Mat(float[]? x) => x is null ? "-" : string.Join(" ", x.Take(6).Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                sb.Append($"mat galho [{Mat(f.BranchMaterial)}] fronde [{Mat(f.FrondMaterial)}] folha [{Mat(f.LeafMaterial)}] ajuste {f.LeafLightAdjust:0.##}\t");
                sb.Append($"{e.Path}\tníveis {f.Levels.Count}\tvértices {m.Pos.Count}\ttriângulos {m.Index.Count / 3}\tfolhas {m.Leaves.Count}\tfrondes {m.FrondIndex.Count / 6}\taltura {m.Height:0.0} m\tcasca {f.BarkTexture}\n");
                // vista de lado (X, Y) para conferir a forma
                float minX = m.Pos.Count == 0 ? 0 : m.Pos.Min(p => p.X), maxX = m.Pos.Count == 0 ? 1 : m.Pos.Max(p => p.X);
                float h = Math.Max(1, m.Height), w = Math.Max(maxX - minX, 1);
                float k = 380 / Math.Max(h, w);
                using var bmp = new System.Drawing.Bitmap(400, 400);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.Clear(System.Drawing.Color.White);
                using var br = new System.Drawing.SolidBrush(System.Drawing.Color.SaddleBrown);
                System.Drawing.PointF P(OpenTK.Mathematics.Vector3 v) => new(200 + v.X * k, 390 - v.Y * k);
                for (int i = 0; i + 2 < m.Index.Count; i += 3)
                    g.FillPolygon(br, new[] { P(m.Pos[m.Index[i]]), P(m.Pos[m.Index[i + 1]]), P(m.Pos[m.Index[i + 2]]) });
                using (var fb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(90, 120, 110, 30)))
                    for (int i = 0; i + 2 < m.FrondIndex.Count; i += 3)
                        g.FillPolygon(fb, new[] { P(m.FrondPos[m.FrondIndex[i]]), P(m.FrondPos[m.FrondIndex[i + 1]]), P(m.FrondPos[m.FrondIndex[i + 2]]) });
                if (f.LeafMaps.Count > 0 && m.Leaves.Count > 0)
                {
                    float rad = 0;
                    var (set, li) = MyAceEditor.UI.WorldTrees.Leaves(f, m, ref rad);
                    set.Fill(OpenTK.Mathematics.Vector3.UnitX, OpenTK.Mathematics.Vector3.UnitY);
                    var lv = set.Buf;
                    using var lb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 0, 140, 0));
                    for (int i = 0; i + 2 < li.Length; i += 3)
                    {
                        OpenTK.Mathematics.Vector3 V(int j) => new(lv[li[j] * 6], lv[li[j] * 6 + 1], lv[li[j] * 6 + 2]);
                        g.FillPolygon(lb, new[] { P(V(i)), P(V(i + 1)), P(V(i + 2)) });
                    }
                }
                bmp.Save(Path.Combine(outDir, Path.GetFileNameWithoutExtension(e.Path) + ".png"));
            }
            catch (Exception ex) { sb.Append($"{e.Path}\tERRO {ex.Message}\n"); }
        }
        File.WriteAllText(Path.Combine(outDir, "spt-test.txt"), sb.ToString(), Encoding.UTF8);
    }

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
