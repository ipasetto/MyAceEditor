using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// "Traduzir a partir de outro cliente": traz de outro cliente (mesma versão ou outra, mais alta ou mais baixa) os
/// textos que ESTE cliente tem, sem trazer estrutura nem versão. Regra: só entra o que o cliente de destino possui.
///   tasks.data   — nome da quest pelo ID (qualquer versão); textos e diálogos quando os dois são da mesma versão
///                  lida por inteiro e o diálogo tem a mesma forma (janelas/opções).
///   aipolicy     — falas dos monstros (operação "talk") por política/trigger/posição; só o texto é trocado, o resto
///                  dos parâmetros do destino fica (versões de trigger diferentes funcionam).
///   pacotes      — interfaces.pck, surfaces.pck, help\script.pck, configs.pck e info\: arquivo inteiro, e só marcado
///                  quando os executáveis são o mesmo programa (interface de outra versão não funciona); configs.pck
///                  recebe de novo a correção da página de anúncios.
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

    static readonly string[] Packages = { "interfaces.pck", "surfaces.pck", @"help\script.pck", "configs.pck" };

    /// <summary>Executáveis do jogo são o mesmo programa (mesmo tamanho e no máximo 16 bytes diferentes, ex.: só o número de versão).</summary>
    public static (bool Same, string Why) SameProgram(string target, string source)
    {
        var a = Path.Combine(target, "elementclient.exe"); var b = Path.Combine(source, "elementclient.exe");
        if (!File.Exists(a) || !File.Exists(b)) return (false, "elementclient.exe não encontrado nos dois clientes");
        var da = File.ReadAllBytes(a); var db = File.ReadAllBytes(b);
        if (da.Length != db.Length) return (false, $"executáveis de tamanhos diferentes ({da.Length:N0} e {db.Length:N0} bytes): versões diferentes do jogo");
        int diff = 0;
        for (int i = 0; i < da.Length && diff <= 16; i++) if (da[i] != db[i]) diff++;
        return diff <= 16 ? (true, diff == 0 ? "executáveis idênticos" : $"mesmo programa ({diff} bytes diferentes)") : (false, "executáveis diferentes: versões diferentes do jogo");
    }

    public static List<Step> Plan(string target, string source)
    {
        var steps = new List<Step>();
        var (same, why) = SameProgram(target, source);

        // ---- tasks.data ----
        var tT = Path.Combine(target, "data", "tasks.data"); var tS = Path.Combine(source, "data", "tasks.data");
        if (File.Exists(tT) && File.Exists(tS))
        {
            try
            {
                var dst = new TasksFile(tT); var src = new TasksFile(tS);
                int n = TranslateTasks(dst, src, dryRun: true, out var mode);
                steps.Add(new Step
                {
                    Title = $"Quests (tasks.data): {n:N0} textos",
                    Detail = mode + (dst.CanWrite ? "" : " — o tasks deste cliente não pode ser gravado: " + dst.ReadOnlyReason),
                    Default = n > 0 && dst.CanWrite, Enabled = n > 0 && dst.CanWrite,
                    Apply = log =>
                    {
                        var d = new TasksFile(tT); var s2 = new TasksFile(tS);
                        int k = TranslateTasks(d, s2, dryRun: false, out _);
                        var bk = d.Save(tT);
                        log($"tasks.data: {k:N0} textos" + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                        return tT;
                    },
                });
            }
            catch (Exception ex) { steps.Add(new Step { Title = "Quests (tasks.data)", Detail = "não consegui ler: " + ex.Message, Enabled = false }); }
        }

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
                    Title = $"Falas dos monstros (aipolicy.data): {n:N0}",
                    Detail = !dst.Ok || !src.Ok ? "um dos aipolicy.data não foi lido por inteiro" : "só o texto de cada fala; condições e ações do seu arquivo ficam",
                    Default = n > 0, Enabled = n > 0,
                    Apply = log =>
                    {
                        var d = new AiPolicyFile(aT); var s2 = new AiPolicyFile(aS);
                        int k = TranslateTalk(d, s2, dryRun: false);
                        var bk = d.Save(aT);
                        log($"aipolicy.data: {k:N0} falas" + (bk is null ? "" : $" (backup {Path.GetFileName(bk)})"));
                        return aT;
                    },
                });
            }
            catch (Exception ex) { steps.Add(new Step { Title = "Falas dos monstros (aipolicy.data)", Detail = "não consegui ler: " + ex.Message, Enabled = false }); }
        }

        // ---- pacotes de interface e textos (arquivo inteiro) ----
        foreach (var p in Packages)
        {
            var dst = Path.Combine(target, p); var src = Path.Combine(source, p);
            if (!File.Exists(src) || !File.Exists(dst) || SameFile(dst, src)) continue;
            string what = p switch
            {
                "interfaces.pck" => "janelas e textos da interface",
                "surfaces.pck" => "imagens da interface",
                "configs.pck" => "descrições de itens e habilidades, mensagens (a página de anúncios é desligada de novo)",
                _ => "textos de ajuda",
            };
            steps.Add(new Step
            {
                Title = $"{p} (arquivo inteiro)",
                Detail = what + " — " + (same ? why : "NÃO recomendado: " + why),
                Default = same, Enabled = true,
                Apply = log =>
                {
                    var bk = $"{dst}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                    File.Copy(dst, bk);
                    File.Copy(src, dst, overwrite: true);
                    log($"{p}: copiado (backup {Path.GetFileName(bk)})");
                    if (p == "configs.pck") LocalServer.FixClientPages(target, log);
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
                    Title = $"Ajuda (pasta info): {files.Count} arquivos",
                    Detail = "textos e imagens da ajuda do jogo" + (same ? "" : " — de outra versão do jogo"),
                    Default = same, Enabled = true,
                    Apply = log =>
                    {
                        var bkDir = Path.Combine(iT, $"_backup_{DateTime.Now:yyyyMMdd-HHmmss}");
                        Directory.CreateDirectory(bkDir);
                        foreach (var f in files) { File.Copy(Path.Combine(iT, f!), Path.Combine(bkDir, f!)); File.Copy(Path.Combine(iS, f!), Path.Combine(iT, f!), true); }
                        log($"info: {files.Count} arquivos (backup em {Path.GetFileName(bkDir)})");
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

    // ---------- tasks ----------
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
            mode = $"nomes, textos e diálogos de cada quest pelo ID (tasks v{dst.Version} nos dois)";
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
            mode = dst.Version == src.Version ? "só os nomes das quests (um dos tasks não foi lido por inteiro)"
                : $"só os nomes das quests, pelo ID (tasks v{dst.Version} aqui e v{src.Version} no outro: textos e diálogos têm formatos diferentes)";
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
