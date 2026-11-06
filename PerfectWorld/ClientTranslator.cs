using System.Text;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// "Traduzir a partir de outro cliente": traz de outro cliente (mesma versão ou outra, mais alta ou mais baixa) os
/// textos que ESTE cliente tem, sem trazer estrutura nem versão. Regra: só entra o que o cliente de destino possui.
///   tasks.data   — nome, textos e diálogos de cada quest pelo ID, em qualquer versão (TaskTexts); se o bloco de textos
///                  de um dos dois não for achado, só os nomes.
///   aipolicy     — falas dos monstros (operação "talk") por política/trigger/posição; só o texto é trocado, o resto
///                  dos parâmetros do destino fica (versões de trigger diferentes funcionam).
///   configs.pck, interfaces.pck — texto por texto (PackTextTranslator), em qualquer versão.
///   surfaces.pck, help\script.pck e info\ — arquivo inteiro, só marcado quando os executáveis são o mesmo programa.
/// O elements.data é traduzido à parte, pela janela de revisão que já existe (casa por bloco/campo/ID).
///   guerra territorial — nomes dos territórios (data\domain*.data) e dos destinos (maps\world\world_targets.txt) pelo ID,
///                  e o GuildMap.dds se tiver o mesmo tamanho; imagens com texto escrito (lista TextImagePaths), só do mesmo tamanho.
/// Nunca: executável, elements inteiro, terreno e construções dos mapas, lista de servidores, patcher/launcher. Backup de tudo.
/// </summary>
public static class ClientTranslator
{
    public sealed class Step
    {
        public string Title = "", Detail = "";
        public bool Default, Enabled = true;
        public Func<Action<string>, string> Apply = _ => "";
    }

    static readonly string[] Packages = { "surfaces.pck", @"help\script.pck" };          // arquivo inteiro, só no mesmo programa
    static readonly string[] TextPackages = { "configs.pck", "interfaces.pck" };       // texto por texto, qualquer versão

    /// <summary>Executáveis do jogo são o mesmo programa (mesmo tamanho e no máximo 16 bytes diferentes, ex.: só o número de versão).</summary>
    public static (bool Same, string Why) SameProgram(string target, string source)
    {
        var a = Path.Combine(target, "elementclient.exe"); var b = Path.Combine(source, "elementclient.exe");
        if (!File.Exists(a) || !File.Exists(b)) return (false, Loc.T("ct.noExe", "elementclient.exe não encontrado nos dois clientes"));
        var da = File.ReadAllBytes(a); var db = File.ReadAllBytes(b);
        if (da.Length != db.Length) return (false, string.Format(Loc.T("ct.exeSizes", "executáveis de tamanhos diferentes ({0:N0} e {1:N0} bytes): versões diferentes do jogo"), da.Length, db.Length));
        int diff = 0;
        for (int i = 0; i < da.Length && diff <= 16; i++) if (da[i] != db[i]) diff++;
        return diff <= 16 ? (true, diff == 0 ? Loc.T("ct.exeSame", "executáveis idênticos") : string.Format(Loc.T("ct.exeAlmost", "mesmo programa ({0} bytes diferentes)"), diff))
            : (false, Loc.T("ct.exeDiffer", "executáveis diferentes: versões diferentes do jogo"));
    }

    public static List<Step> Plan(string target, string source, LoadContext? ctx = null)
    {
        var steps = new List<Step>();
        var (same, why) = SameProgram(target, source);

        ctx?.Report(0.02, Loc.T("ct.stTasks", "Quests (tasks.data)"), force: true);
        // ---- tasks.data ----
        var tT = Path.Combine(target, "data", "tasks.data"); var tS = Path.Combine(source, "data", "tasks.data");
        if (File.Exists(tT) && File.Exists(tS))
        {
            try
            {
                // textos de quest em qualquer versão (TaskTexts: nome, 4 textos e diálogos pelo ID); se o bloco de textos de
                // um dos dois não for achado, fica o modo antigo (só nomes entre versões diferentes)
                var dd = File.ReadAllBytes(tT); var sd = File.ReadAllBytes(tS);
                if (TaskTextsOf(dd) is { } rd && TaskTextsOf(sd) is { } rs)
                {
                    int n = TaskTexts.Translate(rd, rs, dryRun: true);
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.tasksTitle", "Quests (tasks.data): {0:N0} textos"), n),
                        Detail = string.Format(Loc.T("ct.modeAnyVersion", "nomes, textos e diálogos de cada quest pelo ID, subquests incluídas (tasks v{0} aqui e v{1} no outro); quest que só existe num dos lados fica como está"), rd.Version, rs.Version),
                        Default = n > 0, Enabled = n > 0,
                        Apply = log =>
                        {
                            var d = File.ReadAllBytes(tT);
                            var r = TaskTextsOf(d) ?? throw new InvalidDataException(Loc.T("ct.tasksChanged", "O tasks.data deste cliente mudou e não foi lido de novo."));
                            int k = TaskTexts.Translate(r, TaskTextsOf(File.ReadAllBytes(tS))!, dryRun: false);
                            var bk = AtomicFileSave.Write(tT, TaskTexts.Build(d, r));
                            log(string.Format(Loc.T("ct.tasksLog", "tasks.data: {0:N0} textos"), k) + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                            return tT;
                        },
                    });
                }
                else
                {
                    var dst = new TasksFile(dd); var src = new TasksFile(sd);
                    int n = TranslateTasks(dst, src, dryRun: true, out var mode);
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.tasksTitle", "Quests (tasks.data): {0:N0} textos"), n),
                        Detail = mode + (dst.CanWrite ? "" : Loc.T("ct.tasksReadOnly", " — o tasks deste cliente não pode ser gravado: ") + dst.ReadOnlyReason),
                        Default = n > 0 && dst.CanWrite, Enabled = n > 0 && dst.CanWrite,
                        Apply = log =>
                        {
                            var d = new TasksFile(tT); var s2 = new TasksFile(tS);
                            int k = TranslateTasks(d, s2, dryRun: false, out _);
                            var bk = d.Save(tT);
                            log(string.Format(Loc.T("ct.tasksLog", "tasks.data: {0:N0} textos"), k) + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                            return tT;
                        },
                    });
                }
            }
            catch (Exception ex) { steps.Add(new Step { Title = Loc.T("ct.tasks", "Quests (tasks.data)"), Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); }
        }

        ctx?.Report(0.45, Loc.T("ct.stAi", "Falas dos monstros (aipolicy.data)"), force: true);
        // ---- aipolicy.data ----
        var aT = Path.Combine(target, "data", "aipolicy.data"); var aS = Path.Combine(source, "data", "aipolicy.data");
        if (File.Exists(aT) && File.Exists(aS))
        {
            try
            {
                var dst = new AiPolicyFile(aT); var src = new AiPolicyFile(aS);
                int n = dst.Ok && src.Ok ? TranslateTalk(dst, src, dryRun: true) : 0;
                steps.Add(new Step
                {
                    Title = string.Format(Loc.T("ct.aiTitle", "Falas dos monstros (aipolicy.data): {0:N0}"), n),
                    Detail = !dst.Ok || !src.Ok ? Loc.T("ct.aiIncomplete", "um dos aipolicy.data não foi lido por inteiro") : Loc.T("ct.aiDetail", "só o texto de cada fala; condições e ações do seu arquivo ficam"),
                    Default = n > 0, Enabled = n > 0,
                    Apply = log =>
                    {
                        var d = new AiPolicyFile(aT); var s2 = new AiPolicyFile(aS);
                        int k = TranslateTalk(d, s2, dryRun: false);
                        var bk = d.Save(aT);
                        log(string.Format(Loc.T("ct.aiLog", "aipolicy.data: {0:N0} falas"), k) + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                        return aT;
                    },
                });
            }
            catch (Exception ex) { steps.Add(new Step { Title = Loc.T("ct.ai", "Falas dos monstros (aipolicy.data)"), Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); }
        }

        ctx?.Report(0.55, Loc.T("ct.stShop", "Loja (gshop)"), force: true);
        // ---- loja do cliente: nomes e descrições pelo ID do item, qualquer formato (1380/1436 B) ----
        foreach (var name in new[] { "gshop.data", "gshop1.data" })
        {
            var dst = Path.Combine(target, "data", name); var src = Path.Combine(source, "data", name);
            if (!File.Exists(src) || !File.Exists(dst)) continue;
            int n;
            try { n = TranslateShop(GShopFile.ReadClient(File.ReadAllBytes(dst)), GShopFile.ReadClient(File.ReadAllBytes(src)), dryRun: true); }
            catch (Exception ex) { steps.Add(new Step { Title = name, Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); continue; }
            if (n == 0) continue;
            steps.Add(new Step
            {
                Title = string.Format(Loc.T("ct.pckTextsTitle", "{0}: {1:N0} textos"), name, n),
                Detail = Loc.T("ct.shopDetail", "nomes e descrições dos itens da loja pelo ID do item, e as categorias; preços, ordem e o par com o gshopsev do servidor ficam"),
                Default = true, Enabled = true,
                Apply = log =>
                {
                    var shop = GShopFile.ReadClient(File.ReadAllBytes(dst));
                    int k = TranslateShop(shop, GShopFile.ReadClient(File.ReadAllBytes(src)), dryRun: false);
                    var bk = AtomicFileSave.Write(dst, shop.WriteClient());
                    log(string.Format(Loc.T("ct.pckTextsLog", "{0}: {1:N0} textos em {2} arquivo(s)"), name, k, 1) + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                    return dst;
                },
            });
        }

        ctx?.Report(0.62, Loc.T("ct.stPacks", "Textos dos pacotes (configs, interfaces)"), force: true);
        // ---- configs.pck e interfaces.pck: texto por texto, qualquer versão (a estrutura e as imagens daqui ficam) ----
        foreach (var p in TextPackages)
        {
            var dst = Path.Combine(target, p); var src = Path.Combine(source, p);
            if (!File.Exists(src) || !File.Exists(dst) || SameFile(dst, src)) continue;
            int n;
            try { n = PackTextTranslator.Plan(dst, src, dryRun: true).Count; }
            catch (Exception ex) { steps.Add(new Step { Title = p, Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); continue; }
            if (n == 0) continue;
            steps.Add(new Step
            {
                Title = string.Format(Loc.T("ct.pckTextsTitle", "{0}: {1:N0} textos"), p, n),
                Detail = (p == "configs.pck" ? Loc.T("ct.pckConfigsTexts", "descrições de itens e habilidades, buffs e mensagens")
                                             : Loc.T("ct.pckInterfacesTexts2", "textos da interface: tabelas .stf e o texto e a dica de cada controle das janelas .xml (pela janela e pelo nome do controle; texto em chinês do outro cliente só troca texto em chinês daqui)"))
                    + Loc.T("ct.pckTextsDetail", " — pelo ID de cada texto, em qualquer versão; só troca quando os marcadores (%d, %s…) são os mesmos. Fontes e imagens daqui ficam."),
                Default = true, Enabled = true,
                Apply = log =>
                {
                    var (k, files) = PackTextTranslator.Plan(dst, src, dryRun: false);
                    if (files.Count > 0) PckWriter.Write(dst, files);
                    log(string.Format(Loc.T("ct.pckTextsLog", "{0}: {1:N0} textos em {2} arquivo(s)"), p, k, files.Count));
                    return dst;
                },
            });
        }

        // ---- regiões do minimapa: nomes do precinct.clt de cada mapa, pelo ID da área ----
        var mapsT = Path.Combine(target, "maps"); var mapsS = Path.Combine(source, "maps");
        if (Directory.Exists(mapsT) && Directory.Exists(mapsS))
        {
            var regions = new List<(string File, byte[] Bytes, int Count)>();
            foreach (var dir in Directory.GetDirectories(mapsT))
            {
                string mine = Path.Combine(dir, "precinct.clt"), other = Path.Combine(mapsS, Path.GetFileName(dir), "precinct.clt");
                if (!File.Exists(mine) || !File.Exists(other)) continue;
                try { if (PrecinctFile.Translate(File.ReadAllBytes(mine), File.ReadAllBytes(other), out int n) is { } b) regions.Add((mine, b, n)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("precinct.clt", ex); }
            }
            if (regions.Count > 0)
                steps.Add(new Step
                {
                    Title = string.Format(Loc.T("ct.regions", "Regiões do minimapa: {0:N0} nomes em {1} mapas"), regions.Sum(r => r.Count), regions.Count),
                    Detail = Loc.T("ct.regionsDetail", "os nomes que aparecem em cima do minimapa, pelo ID de cada área (precinct.clt)."),
                    Default = true, Enabled = true,
                    Apply = log =>
                    {
                        foreach (var (file, bytes, _) in regions) { BackupStore.Save(file); File.WriteAllBytes(file, bytes); }
                        log(string.Format(Loc.T("ct.regionsLog", "precinct.clt: {0} mapas"), regions.Count));
                        return regions[0].File;
                    },
                });
        }

        // ---- textos da ajuda: só os textos, em qualquer versão; os comandos ficam os do cliente ----
        var helpT = Path.Combine(target, "help", "script.pck"); var helpS = Path.Combine(source, "help", "script.pck");
        if (File.Exists(helpT) && File.Exists(helpS) && !SameFile(helpT, helpS))
        {
            try
            {
                var (texts, files) = HelpScripts.Plan(helpT, helpS);
                if (files.Count > 0)
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.helpTexts", "Ajuda: {0:N0} textos em {1} arquivos"), texts, files.Count),
                        Detail = Loc.T("ct.helpTextsDetail", "só os textos dos scripts de ajuda, pelo número do arquivo; os comandos continuam os deste cliente."),
                        Default = true, Enabled = true,
                        Apply = log =>
                        {
                            PckWriter.Write(helpT, files);
                            log(string.Format(Loc.T("ct.helpTextsLog", "help\\script.pck: {0} arquivos"), files.Count));
                            return helpT;
                        },
                    });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("help", ex); }
        }

        // ---- nomes das instâncias: tabela do MyAce (Assets/map_names.tsv), no idioma do próprio cliente ----
        var cfg = Path.Combine(target, "configs.pck");
        if (File.Exists(cfg))
        {
            try
            {
                byte[]? fixedNames = null; List<(string Tag, string From, string To)> names = [];
                using (var pck = new PckArchive(cfg))
                    if (pck.Find(@"configs\instance.txt") is { } ie) fixedNames = PackTextTranslator.RepairInstanceNames(pck.Read(ie), out names);
                if (fixedNames is { } bytes)
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.fixMaps", "Corrigir {0} nomes de instâncias"), names.Count),
                        Detail = string.Join(", ", names.Take(4).Select(n => $"{n.Tag}: {n.From} → {n.To}")) + (names.Count > 4 ? "…" : ""),
                        Default = true, Enabled = true,
                        Apply = log =>
                        {
                            PckWriter.Write(cfg, new Dictionary<string, byte[]> { [@"configs\instance.txt"] = bytes });
                            log(string.Format(Loc.T("ct.fixMapsLog", "configs.pck: {0} nomes de instâncias corrigidos"), names.Count));
                            return cfg;
                        },
                    });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("instance.txt", ex); }
        }

        ctx?.Report(0.9, Loc.T("ct.stCombat", "Imagens de batalha"), force: true);
        // ---- imagens com texto da batalha (surfaces\ingame, PW-KB §S20e): só elas, em qualquer versão; o resto do surfaces fica ----
        var sT = Path.Combine(target, "surfaces.pck"); var sS = Path.Combine(source, "surfaces.pck");
        if (File.Exists(sT) && File.Exists(sS) && !SameFile(sT, sS))
        {
            try
            {
                var diff = CombatImages(sT, sS);
                if (diff.Count > 0)
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.combatTitle", "Textos da batalha (surfaces.pck): {0} imagens"), diff.Count),
                        Detail = Loc.T("ct.combatDetail", "as imagens com palavra que sobem na tela: crítico, alma, roubo de vida, imune, errou, sem efeito, refletir, esquiva, subiu de nível, duelo, guerra territorial…; o resto das imagens daqui fica"),
                        Default = true, Enabled = !File.Exists(Path.ChangeExtension(sT, ".pkx")),
                        Apply = log =>
                        {
                            var files = CombatImages(sT, sS);
                            if (files.Count > 0) PckWriter.Write(sT, files);
                            log(string.Format(Loc.T("ct.pckTextsLog", "{0}: {1:N0} textos em {2} arquivo(s)"), "surfaces.pck", files.Count, files.Count));
                            return sT;
                        },
                    });
            }
            catch (Exception ex) { steps.Add(new Step { Title = "surfaces.pck", Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); }
        }

        // ---- mapas da tecla M (os nomes de cidades e regiões vêm desenhados na imagem): mundo em surfaces\maps\, instâncias em
        // surfaces\ingame\<pasta>.dds; marcado sozinho só se o mapa do mundo tem os mesmos pedaços nos dois clientes ----
        if (File.Exists(sT) && File.Exists(sS) && !SameFile(sT, sS))
        {
            try
            {
                var (maps, sameWorld) = MapImages(target, sT, sS);
                if (maps.Count > 0)
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.mapsTitle", "Mapas do jogo, tecla M (surfaces.pck): {0} imagens"), maps.Count),
                        Detail = Loc.T("ct.mapsDetail", "o mapa do mundo e os das instâncias, com os nomes de cidades e regiões no idioma do outro cliente")
                            + (sameWorld ? "" : Loc.T("ct.mapsOtherWorld", " — o mapa do mundo do outro cliente tem outro tamanho (outra versão): confira antes")),
                        Default = sameWorld, Enabled = !File.Exists(Path.ChangeExtension(sT, ".pkx")),
                        Apply = log =>
                        {
                            var files = MapImages(target, sT, sS).Files;
                            if (files.Count > 0) PckWriter.Write(sT, files);
                            log(string.Format(Loc.T("ct.pckTextsLog", "{0}: {1:N0} textos em {2} arquivo(s)"), "surfaces.pck", files.Count, files.Count));
                            return sT;
                        },
                    });
            }
            catch (Exception ex) { steps.Add(new Step { Title = "surfaces.pck", Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); }
        }

        // ---- guerra territorial e de nação: nomes dos territórios (data\domain*.data), destinos de teleporte
        // (maps\world\world_targets.txt) e o mapa de territórios desenhado (surfaces\ingame\GuildMap.dds) ----
        var war = WarFiles(target, source, sT, sS);
        if (war.Count > 0)
            steps.Add(new Step
            {
                Title = string.Format(Loc.T("ct.warTitle", "Mapa da guerra territorial: {0} arquivo(s)"), war.Count),
                Detail = Loc.T("ct.warDetail", "nomes dos territórios (guerra territorial e de nação), nomes dos destinos de teleporte e o mapa de territórios com os nomes desenhados; só o texto muda, polígonos e IDs ficam")
                    + (war.Any(w => w.Note.Length > 0) ? " — " + string.Join("; ", war.Where(w => w.Note.Length > 0).Select(w => w.Note)) : ""),
                Default = true, Enabled = true,
                Apply = log =>
                {
                    foreach (var w in WarFiles(target, source, sT, sS)) if (w.Apply() is { Length: > 0 } msg) log(msg);
                    return Path.Combine(target, "data");
                },
            });

        // ---- outras imagens com texto escrito (canais do chat, avisos, base de clã, ajudas, calendário...): só as do mesmo
        // tamanho nos dois clientes (outro tamanho = outra arte/versão, desalinharia a janela) ----
        if (File.Exists(sT) && File.Exists(sS) && !SameFile(sT, sS))
        {
            try
            {
                var (imgs, skipped) = TextImages(sT, sS);
                if (imgs.Count > 0)
                    steps.Add(new Step
                    {
                        Title = string.Format(Loc.T("ct.textImgTitle", "Outras imagens com texto (surfaces.pck): {0} imagens"), imgs.Count),
                        Detail = Loc.T("ct.textImgDetail", "etiquetas dos canais do chat, avisos de HP/MP, \"em combate\", placas da base do clã, estados do login, ajudas com figura, calendário de eventos e selos de desconto")
                            + (skipped > 0 ? string.Format(Loc.T("ct.textImgSkipped", " — {0} ficam (tamanho diferente no outro cliente)"), skipped) : ""),
                        Default = true, Enabled = !File.Exists(Path.ChangeExtension(sT, ".pkx")),
                        Apply = log =>
                        {
                            var files = TextImages(sT, sS).Files;
                            if (files.Count > 0) PckWriter.Write(sT, files);
                            log(string.Format(Loc.T("ct.pckTextsLog", "{0}: {1:N0} textos em {2} arquivo(s)"), "surfaces.pck", files.Count, files.Count));
                            return sT;
                        },
                    });
            }
            catch (Exception ex) { steps.Add(new Step { Title = "surfaces.pck", Detail = Loc.T("ct.readFail", "não consegui ler: ") + ex.Message, Enabled = false }); }
        }

        ctx?.Report(0.95, Loc.T("ct.stFiles", "Imagens e ajuda"), force: true);
        // ---- imagens e ajuda (arquivo inteiro) ----
        foreach (var p in Packages)
        {
            var dst = Path.Combine(target, p); var src = Path.Combine(source, p);
            if (!File.Exists(src) || !File.Exists(dst) || SameFile(dst, src)) continue;
            string what = p switch
            {
                "surfaces.pck" => Loc.T("ct.pckSurfaces", "imagens da interface"),
                _ => Loc.T("ct.pckHelp", "textos de ajuda"),
            };
            steps.Add(new Step
            {
                Title = string.Format(Loc.T("ct.wholeFile", "{0} (arquivo inteiro)"), p),
                Detail = what + " — " + (same ? why : Loc.T("ct.notRecommended", "NÃO recomendado: ") + why),
                Default = false, Enabled = true,   // o que tem texto já vem nos passos acima: o arquivo inteiro só por escolha
                Apply = log =>
                {
                    var bk = BackupStore.Save(dst)!;
                    File.Copy(src, dst, overwrite: true);
                    log(string.Format(Loc.T("ct.copied", "{0}: copiado (backup {1})"), p, Path.GetFileName(bk)));
                    return dst;
                },
            });
        }

        // ---- font\: as fontes do jogo (qualquer versão). Os clientes BR trocam as 3 chinesas (fzl2jw, fzlbjw, fzxh1jw) por
        // uma latina com o mesmo nome de arquivo; sem isso o PWI desenha ã/õ como □ e espaça as letras acentuadas ----
        var fT = Path.Combine(target, "font"); var fS = Path.Combine(source, "font");
        if (Directory.Exists(fT) && Directory.Exists(fS))
        {
            var fonts = Directory.GetFiles(fS, "*.ttf").Select(Path.GetFileName)
                .Where(f => File.Exists(Path.Combine(fT, f!)) && !SameFile(Path.Combine(fT, f!), Path.Combine(fS, f!))).ToList();
            if (fonts.Count > 0)
                steps.Add(new Step
                {
                    Title = string.Format(Loc.T("ct.fontsTitle", "Fontes (font\\): {0} arquivos"), fonts.Count),
                    Detail = Loc.T("ct.fontsDetail", "as fontes do outro cliente, para as letras do idioma dele (acentos, ç, ã/õ) aparecerem certas; fonte não depende da versão do jogo"),
                    Default = true, Enabled = true,
                    Apply = log =>
                    {
                        // backup fora do cliente (BackupStore); o jogo aberto prende as fontes
                        foreach (var f in fonts) { BackupStore.Save(Path.Combine(fT, f!)); CopyOver(Path.Combine(fS, f!), Path.Combine(fT, f!)); }
                        log(string.Format(Loc.T("ct.fontsLog", "font\\: {0} arquivos (backup em {1})"), fonts.Count, BackupStore.Root));
                        return fT;
                    },
                });
        }

        // ---- info\ (textos e imagens da ajuda) ----
        var iT = Path.Combine(target, "info"); var iS = Path.Combine(source, "info");
        if (Directory.Exists(iT) && Directory.Exists(iS))
        {
            var files = Directory.GetFiles(iS).Select(Path.GetFileName).Where(f => f is not null && File.Exists(Path.Combine(iT, f!)) && !SameFile(Path.Combine(iT, f!), Path.Combine(iS, f!))).ToList();
            if (files.Count > 0)
                steps.Add(new Step
                {
                    Title = string.Format(Loc.T("ct.infoTitle", "Ajuda (pasta info): {0} arquivos"), files.Count),
                    Detail = Loc.T("ct.infoDetail", "textos e imagens da ajuda do jogo") + (same ? "" : Loc.T("ct.otherVersion", " — de outra versão do jogo")),
                    Default = same, Enabled = true,
                    Apply = log =>
                    {
                        foreach (var f in files) { BackupStore.Save(Path.Combine(iT, f!)); CopyOver(Path.Combine(iS, f!), Path.Combine(iT, f!)); }
                        log(string.Format(Loc.T("ct.infoLog", "info: {0} arquivos (backup em {1})"), files.Count, BackupStore.Root));
                        return iT;
                    },
                });
        }
        return steps;
    }

    // imagens com palavra escrita que o elementclient carrega de surfaces\ingame (CECImageRes + strings do exe 1.5.5)
    static readonly string[] CombatNames =
    {
        "爆击", "元神", "吸血", "免疫", "未命中", "无效", "反震", "状态闪避", "复仇惩戒", "复仇镜像", "升级了", "经验", "金钱",
        "失败", "成功", "决斗一", "决斗二", "决斗三", "决斗开始", "决斗胜利", "决斗失败", "中立", "中立偏守方", "中立偏攻方",
        "守方占领", "攻方占领", "有矿提示", "反击", "反噬", "反弹", "复仇反噬", "复仇封印", "复仇震荡",
    };

    // outras imagens com palavra escrita (achadas comparando o surfaces.pck do 155 BR com o do PWI 1.5.5: o que difere entre
    // dois clientes traduzidos é o que tem texto); caminho exato ou pasta inteira (termina em \)
    static readonly string[] TextImagePaths =
    {
        @"surfaces\ingame\journeymap.dds", @"surfaces\ingame\hp_warn.tga", @"surfaces\ingame\mp_warn.tga", @"surfaces\ingame\minimapdir.tga",
        // etiquetas dos canais do chat
        @"surfaces\ingame\世界.tga", @"surfaces\ingame\交易.tga", @"surfaces\ingame\普通.tga", @"surfaces\ingame\组队.tga", @"surfaces\ingame\帮派.tga",
        @"surfaces\ingame\密语.tga", @"surfaces\ingame\系统.tga", @"surfaces\ingame\号角.tga", @"surfaces\ingame\国家.tga", @"surfaces\ingame\战场.tga",
        @"surfaces\ingame\跨服.tga",
        // placas da base do clã
        @"surfaces\ingame\兵械库.tga", @"surfaces\ingame\炼丹房.tga", @"surfaces\ingame\破魔堂.tga", @"surfaces\ingame\聚义厅.tga", @"surfaces\ingame\天元宝号.tga",
        @"surfaces\number\战斗中.tga", @"surfaces\2013\转生\一二三四五六七八九十.dds",
        @"surfaces\version01\登陆\删除中.tga", @"surfaces\version01\登陆\寄卖中.tga", @"surfaces\version01\登陆\跨服中.tga",
        @"surfaces\scripthelp\", @"surfaces\version02\系统\家园\家园帮助\", @"surfaces\version02\系统\按键指示图\", @"surfaces\活动日历\", @"surfaces\打折促销\",
    };

    /// <summary>Largura × altura de DDS, TGA ou BMP (null se não reconhece): imagem de outro tamanho não entra.</summary>
    static (int W, int H)? ImageSize(byte[] d)
    {
        if (d.Length >= 20 && d[0] == 'D' && d[1] == 'D' && d[2] == 'S' && d[3] == ' ') return (BitConverter.ToInt32(d, 16), BitConverter.ToInt32(d, 12));
        if (d.Length >= 26 && d[0] == 'B' && d[1] == 'M') return (BitConverter.ToInt32(d, 18), Math.Abs(BitConverter.ToInt32(d, 22)));
        if (d.Length >= 18 && d[1] <= 1 && d[2] is 1 or 2 or 3 or 9 or 10 or 11) return (BitConverter.ToUInt16(d, 12), BitConverter.ToUInt16(d, 14));   // TGA
        return null;
    }

    static bool SameSize(byte[] a, byte[] b) => ImageSize(a) is { } sa && ImageSize(b) is { } sb && sa == sb;

    /// <summary>Imagens com texto (lista acima) diferentes nos dois pacotes e do mesmo tamanho; skipped = diferentes mas de outro tamanho.</summary>
    static (Dictionary<string, byte[]> Files, int Skipped) TextImages(string targetPck, string sourcePck)
    {
        var r = new Dictionary<string, byte[]>(); int skipped = 0;
        using var a = new PckArchive(targetPck); using var b = new PckArchive(sourcePck);
        foreach (var ea in a.Entries)
        {
            if (!TextImagePaths.Any(p => p.EndsWith('\\') ? ea.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase) : ea.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (b.Find(ea.Path) is not { } eb) continue;
            byte[] da = a.Read(ea), db = b.Read(eb);
            if (da.AsSpan().SequenceEqual(db)) continue;
            if (SameSize(da, db)) r[ea.Path] = db; else skipped++;
        }
        return (r, skipped);
    }

    sealed record WarFile(string Note, Func<string> Apply);

    /// <summary>Arquivos da guerra territorial/de nação com texto diferente no outro cliente (cada um sabe se aplicar).</summary>
    static List<WarFile> WarFiles(string target, string source, string targetPck, string sourcePck)
    {
        var list = new List<WarFile>();
        foreach (var (name, d2) in DomainFile.Files)
        {
            var dst = Path.Combine(target, "data", name); var src = Path.Combine(source, "data", name);
            if (!File.Exists(dst) || !File.Exists(src)) continue;
            if (DomainFile.Translate(File.ReadAllBytes(dst), File.ReadAllBytes(src), d2) is not { Changed: > 0 }) continue;
            list.Add(new WarFile("", () =>
            {
                var res = DomainFile.Translate(File.ReadAllBytes(dst), File.ReadAllBytes(src), d2)!.Value;
                AtomicFileSave.Write(dst, res.Bytes);   // backup fora do cliente (BackupStore)
                return string.Format(Loc.T("ct.warNamesLog", "{0}: {1} nomes de território"), name, res.Changed);
            }));
        }
        var wT = Path.Combine(target, "maps", "world", "world_targets.txt"); var wS = Path.Combine(source, "maps", "world", "world_targets.txt");
        if (File.Exists(wT) && File.Exists(wS) && WorldTargets(File.ReadAllBytes(wT), File.ReadAllBytes(wS)) is { Changed: > 0 })
            list.Add(new WarFile("", () =>
            {
                var res = WorldTargets(File.ReadAllBytes(wT), File.ReadAllBytes(wS))!.Value;
                AtomicFileSave.Write(wT, res.Bytes);
                return string.Format(Loc.T("ct.warTargetsLog", "world_targets.txt: {0} destinos de teleporte"), res.Changed);
            }));
        if (File.Exists(targetPck) && File.Exists(sourcePck) && !SameFile(targetPck, sourcePck) && !File.Exists(Path.ChangeExtension(targetPck, ".pkx")))
        {
            const string gm = @"surfaces\ingame\guildmap.dds";
            using var a = new PckArchive(targetPck); using var b = new PckArchive(sourcePck);
            if (a.Find(gm) is { } ea && b.Find(gm) is { } eb && a.Read(ea) is var da && b.Read(eb) is var db && !da.AsSpan().SequenceEqual(db))
            {
                // os polígonos dos territórios (domain.data) são em pixels desta imagem: outro tamanho = outro mapa
                if (SameSize(da, db))
                    list.Add(new WarFile("", () =>
                    {
                        using (var b2 = new PckArchive(sourcePck)) PckWriter.Write(targetPck, new Dictionary<string, byte[]> { [ea.Path] = b2.Read(b2.Find(gm)!) });
                        return Loc.T("ct.warMapLog", "surfaces.pck: mapa de territórios (GuildMap.dds)");
                    }));
                else list.Add(new WarFile(Loc.T("ct.warMapSize", "o mapa de territórios do outro cliente tem outro tamanho e fica"), () => ""));
            }
        }
        return list.Any(w => w.Note.Length == 0) ? list : new();
    }

    static readonly System.Text.RegularExpressions.Regex TargetLine = new("^(\\s*)(\\d+)(\\s+)\"([^\"]*)\"", System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// world_targets.txt (UTF-16, uma linha por destino: id "nome" mapa "x,y,z" território): troca só o nome entre aspas
    /// pelo do mesmo ID no outro cliente; coordenadas e o resto ficam. Null se não é UTF-16.
    /// </summary>
    static (byte[] Bytes, int Changed)? WorldTargets(byte[] target, byte[] source)
    {
        static string? Text(byte[] d) => d.Length >= 2 && d[0] == 0xFF && d[1] == 0xFE ? Encoding.Unicode.GetString(d, 2, d.Length - 2) : null;
        if (Text(target) is not { } t || Text(source) is not { } s) return null;
        var names = new Dictionary<string, string>();
        foreach (System.Text.RegularExpressions.Match m in TargetLine.Matches(s)) names.TryAdd(m.Groups[2].Value, m.Groups[4].Value);
        int changed = 0;
        var outText = TargetLine.Replace(t, m =>
        {
            if (!names.TryGetValue(m.Groups[2].Value, out var n) || n.Length == 0 || n == m.Groups[4].Value) return m.Value;
            changed++;
            return $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}\"{n}\"";
        });
        return (new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(outText)).ToArray(), changed);
    }

    /// <summary>Imagens de texto da batalha que existem nos dois surfaces.pck e são diferentes (caminho no pacote → conteúdo do outro).</summary>
    static Dictionary<string, byte[]> CombatImages(string targetPck, string sourcePck)
    {
        var r = new Dictionary<string, byte[]>();
        using var a = new PckArchive(targetPck); using var b = new PckArchive(sourcePck);
        foreach (var n in CombatNames)
            foreach (var ext in new[] { ".tga", ".dds" })
            {
                var rel = $@"surfaces\ingame\{n}{ext}";
                if (a.Find(rel) is not { } ea || b.Find(rel) is not { } eb) continue;
                var nb = b.Read(eb);
                if (!a.Read(ea).AsSpan().SequenceEqual(nb)) r[ea.Path] = nb;
            }
        return r;
    }

    static void CopyOver(string src, string dst)
    {
        try { File.Copy(src, dst, true); }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020))   // arquivo em uso
        { throw new IOException(string.Format(Loc.T("ct.inUse", "{0} está aberto em outro programa (o jogo?). Feche o jogo e tente de novo."), Path.GetFileName(dst)), ex); }
    }

    /// <summary>Imagens de mapa diferentes nos dois surfaces.pck (caminho → conteúdo do outro) e se o mundo tem os mesmos pedaços.</summary>
    static (Dictionary<string, byte[]> Files, bool SameWorld) MapImages(string targetElement, string targetPck, string sourcePck)
    {
        var r = new Dictionary<string, byte[]>();
        var paths = (MapTags.ClientMaps(targetElement)?.Values.Select(m => m.Path) ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var a = new PckArchive(targetPck); using var b = new PckArchive(sourcePck);
        bool IsMap(string p)
        {
            if (!p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return false;
            if (p.StartsWith(@"surfaces\maps\", StringComparison.OrdinalIgnoreCase)) return true;
            if (!p.StartsWith(@"surfaces\ingame\", StringComparison.OrdinalIgnoreCase) || p.Count(c => c == '\\') != 2) return false;
            var n = Path.GetFileNameWithoutExtension(p);
            var dash = n.LastIndexOf('-');
            return paths.Contains(n) || dash > 0 && int.TryParse(n[(dash + 1)..], out _) && paths.Contains(n[..dash]);   // a05.dds, a05-1.dds
        }
        int tilesA = a.Entries.Count(e => e.Path.StartsWith(@"surfaces\maps\", StringComparison.OrdinalIgnoreCase));
        int tilesB = b.Entries.Count(e => e.Path.StartsWith(@"surfaces\maps\", StringComparison.OrdinalIgnoreCase));
        foreach (var ea in a.Entries)
        {
            if (!IsMap(ea.Path) || b.Find(ea.Path) is not { } eb) continue;
            var nb = b.Read(eb);
            if (!a.Read(ea).AsSpan().SequenceEqual(nb)) r[ea.Path] = nb;
        }
        return (r, tilesA == tilesB);
    }

    static bool SameFile(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = fa.OpenRead(); using var sb = fb.OpenRead();
        var ba = new byte[1 << 20]; var bb = new byte[1 << 20];
        while (true)
        {
            int na = sa.ReadAtLeast(ba, ba.Length, false), nb = sb.ReadAtLeast(bb, bb.Length, false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            if (na == 0) return true;
        }
    }

    // ---------- loja (gshop.data / gshop1.data do cliente) ----------
    /// <summary>
    /// Nome e descrição de cada item da loja pelo ID do item (a 2ª vez que o mesmo ID aparece casa com a 2ª do outro…) e
    /// os nomes das categorias pela posição. Só texto: preço, ordem e timestamp ficam (o par com o gshopsev não muda).
    /// </summary>
    public static int TranslateShop(GShopFile dst, GShopFile src, bool dryRun)
    {
        int n = 0;
        void Take(string cur, string other, Action<string> set) { if (other.Length == 0 || other == cur) return; n++; if (!dryRun) set(other); }
        var byId = src.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.ToList());
        var seen = new Dictionary<int, int>();
        foreach (var it in dst.Items)
        {
            int k = seen[it.ItemId] = seen.GetValueOrDefault(it.ItemId) + 1;
            if (!byId.TryGetValue(it.ItemId, out var same)) continue;
            var s = same[Math.Min(k, same.Count) - 1];
            Take(it.Name, s.Name, v => it.Name = v);
            Take(it.Description, s.Description, v => it.Description = v);
        }
        for (int c = 0; c < dst.Categories.Count && c < src.Categories.Count; c++)
        {
            var (a, b) = (dst.Categories[c], src.Categories[c]);
            Take(a.Name, b.Name, v => a.Name = v);
            for (int i = 0; i < a.Subs.Count && i < b.Subs.Count; i++) { int j = i; Take(a.Subs[j], b.Subs[j], v => a.Subs[j] = v); }
        }
        return n;
    }

    // ---------- tasks ----------
    /// <summary>Textos das quests (qualquer versão), só se acha todos e a regravação sem mudança sai idêntica; senão null.</summary>
    static TaskTexts.Result? TaskTextsOf(byte[] d)
    {
        try { var r = TaskTexts.Read(d); return TaskTexts.Build(d, r).AsSpan().SequenceEqual(d) ? r : null; }
        catch (InvalidDataException) { return null; }
    }

    static IEnumerable<QuestNode> Flatten(IEnumerable<QuestNode> roots)
    {
        foreach (var r in roots) { yield return r; foreach (var c in Flatten(r.Children)) yield return c; }
    }

    /// <summary>Quantos textos mudam (e aplica, se não for simulação). mode explica o que foi possível.</summary>
    public static int TranslateTasks(TasksFile dst, TasksFile src, bool dryRun, out string mode)
    {
        int n = 0;
        bool full = dst.FullSchema && src.FullSchema && dst.Version == src.Version;
        if (full)
        {
            mode = string.Format(Loc.T("ct.modeFull", "nomes, textos e diálogos de cada quest pelo ID (tasks v{0} nos dois)"), dst.Version);
            var map = Flatten(src.Roots!).GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var t in Flatten(dst.Roots!))
            {
                if (!map.TryGetValue(t.Id, out var s)) continue;
                if (s.Name.Length > 0 && s.Name != t.Name && s.Name.Length <= TasksFile.MaxNameLength) { n++; if (!dryRun) t.Name = s.Name; }
                bool changed = false;
                for (int i = 0; i < t.Texts.Length && i < s.Texts.Length; i++)
                    if (!string.IsNullOrEmpty(s.Texts[i]) && s.Texts[i] != t.Texts[i]) { n++; changed = true; if (!dryRun) t.Texts[i] = s.Texts[i]; }
                for (int i = 0; i < t.Talks.Length && i < s.Talks.Length; i++)
                {
                    var a = t.Talks[i]; var b = s.Talks[i];
                    if (a is null || b is null || a.Windows.Count != b.Windows.Count) continue;
                    if (a.Windows.Zip(b.Windows).Any(w => w.First.Options.Count != w.Second.Options.Count)) continue;   // forma diferente: pula
                    for (int w = 0; w < a.Windows.Count; w++)
                    {
                        var wa = a.Windows[w]; var wb = b.Windows[w];
                        if (wb.Text.Length > 0 && wb.Text != wa.Text) { n++; changed = true; if (!dryRun) wa.Text = wb.Text; }
                        for (int o = 0; o < wa.Options.Count; o++)
                            if (wb.Options[o].Text.Length > 0 && wb.Options[o].Text != wa.Options[o].Text) { n++; changed = true; if (!dryRun) wa.Options[o].Text = wb.Options[o].Text; }
                    }
                }
                if (changed && !dryRun) t.TextsChanged = true;
            }
        }
        else
        {
            mode = dst.Version == src.Version ? Loc.T("ct.modeNamesIncomplete", "só os nomes das quests (um dos tasks não foi lido por inteiro)")
                : string.Format(Loc.T("ct.modeNamesVersion", "só os nomes das quests, pelo ID (tasks v{0} aqui e v{1} no outro: textos e diálogos têm formatos diferentes)"), dst.Version, src.Version);
            var names = src.Quests.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First().Name);
            if (dst.FullSchema)
            {
                foreach (var t in Flatten(dst.Roots!))
                    if (names.TryGetValue(t.Id, out var nm) && nm.Length > 0 && nm != t.Name && nm.Length <= TasksFile.MaxNameLength) { n++; if (!dryRun) t.Name = nm; }
            }
            else
                foreach (var q in dst.Quests)
                    if (names.TryGetValue(q.Id, out var nm) && nm.Length > 0 && nm != q.Name && nm.Length <= TasksFile.MaxNameLength) { n++; if (!dryRun) q.Name = nm; }
        }
        return n;
    }

    // ---------- aipolicy: falas (operação 2 = talk: i32 bytes + UTF-16 com \0 [+ resto conforme a versão]) ----------
    const int OpTalk = 2;

    static (string Text, int End)? TalkText(byte[] p)
    {
        if (p.Length < 4) return null;
        int len = BitConverter.ToInt32(p, 0);
        if (len < 0 || len > p.Length - 4 || len % 2 != 0) return null;
        return (Encoding.Unicode.GetString(p, 4, len).TrimEnd('\0'), 4 + len);
    }

    public static int TranslateTalk(AiPolicyFile dst, AiPolicyFile src, bool dryRun)
    {
        int n = 0;
        var pol = src.Policies.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var p in dst.Policies)
        {
            if (!pol.TryGetValue(p.Id, out var sp)) continue;
            var trig = sp.Triggers.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var t in p.Triggers)
            {
                if (!trig.TryGetValue(t.Id, out var st)) continue;
                var srcTalks = st.Ops.Where(o => o.Type == OpTalk).ToList();
                var dstTalks = t.Ops.Where(o => o.Type == OpTalk).ToList();
                if (dstTalks.Count != srcTalks.Count) continue;   // falas diferentes no mesmo trigger: não dá para casar com segurança
                for (int i = 0; i < dstTalks.Count; i++)
                {
                    if (TalkText(dstTalks[i].Param) is not { } a || TalkText(srcTalks[i].Param) is not { } b) continue;
                    if (b.Text.Length == 0 || b.Text == a.Text) continue;
                    n++;
                    if (dryRun) continue;
                    var text = Encoding.Unicode.GetBytes(b.Text + "\0");
                    var rest = dstTalks[i].Param.AsSpan(a.End).ToArray();   // o que vem depois do texto na versão do destino
                    var np = new byte[4 + text.Length + rest.Length];
                    BitConverter.GetBytes(text.Length).CopyTo(np, 0); text.CopyTo(np, 4); rest.CopyTo(np, 4 + text.Length);
                    dstTalks[i].Param = np;
                }
            }
        }
        return n;
    }
}
