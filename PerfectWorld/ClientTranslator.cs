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
/// Nunca: executável, elements inteiro, mapas, construções, lista de servidores, patcher/launcher. Backup de tudo.
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
                                             : Loc.T("ct.pckInterfacesTexts", "textos das janelas da interface"))
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
                Default = same, Enabled = true,
                Apply = log =>
                {
                    var bk = $"{dst}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                    File.Copy(dst, bk);
                    File.Copy(src, dst, overwrite: true);
                    log(string.Format(Loc.T("ct.copied", "{0}: copiado (backup {1})"), p, Path.GetFileName(bk)));
                    return dst;
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
                        var bkDir = Path.Combine(iT, $"_backup_{DateTime.Now:yyyyMMdd-HHmmss}");
                        Directory.CreateDirectory(bkDir);
                        foreach (var f in files) { File.Copy(Path.Combine(iT, f!), Path.Combine(bkDir, f!)); File.Copy(Path.Combine(iS, f!), Path.Combine(iT, f!), true); }
                        log(string.Format(Loc.T("ct.infoLog", "info: {0} arquivos (backup em {1})"), files.Count, Path.GetFileName(bkDir)));
                        return iT;
                    },
                });
        }
        return steps;
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
