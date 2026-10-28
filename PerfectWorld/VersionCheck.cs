using MyAceEditor.UI;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

public enum CheckLevel { Ok, Info, Aviso, Erro }

public sealed record CheckRow(string Item, string Cliente, string Servidor, CheckLevel Nivel, string Dica);

/// <summary>
/// Compara o PAR cliente × servidor antes de publicar (ENGINE-KB §E5, PW-KB §S17/§S7).
/// O cliente envia ao servidor uma "edition" = versão do elements.data + versão do tasks.data +
/// timestamps do gshop/gshop1; qualquer diferença derruba o login ("versão errada").
/// Nunca exibe chaves de rede: só diz se batem.
/// </summary>
public static class VersionCheck
{

    /// <summary>Texto de arquivo ausente (a tela compara com ele para dizer que nada foi achado).</summary>
    public static string NotFound => Loc.T("chk.notFound", "não encontrado");

    /// <param name="clientElement">pasta element\ do cliente</param>
    /// <param name="serverHome">pasta home/ do servidor (contém gamed/, glinkd/…)</param>
    /// <summary>Aceita a pasta do jogo, a element ou a element\data e devolve a element.</summary>
    public static string ClientElement(string p)
    {
        p = p.TrimEnd('\\', '/');
        if (File.Exists(Path.Combine(p, "elements.data")) && Path.GetFileName(p).Equals("data", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(p)!;
        if (Directory.Exists(Path.Combine(p, "element", "data"))) return Path.Combine(p, "element");
        return p;
    }

    /// <summary>Aceita a raiz dos arquivos do servidor ou a home e devolve a pasta que contém gamed/ (até 2 níveis abaixo).</summary>
    public static string ServerHome(string p)
    {
        p = p.TrimEnd('\\', '/');
        if (Directory.Exists(Path.Combine(p, "gamed"))) return p;
        if (Path.GetFileName(p).Equals("gamed", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(p)!;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(p))
            {
                if (Directory.Exists(Path.Combine(d, "gamed"))) return d;
                foreach (var d2 in Directory.EnumerateDirectories(d))
                    if (Directory.Exists(Path.Combine(d2, "gamed"))) return d2;
            }
        }
        catch { }
        return p;
    }

    public static List<CheckRow> Run(string clientElement, string serverHome)
    {
        clientElement = ClientElement(clientElement);
        serverHome = ServerHome(serverHome);
        var rows = new List<CheckRow>();
        string cData = Path.Combine(clientElement, "data");
        string sCfg = Path.Combine(serverHome, "gamed", "config");

        // 1) elements.data — versão entra na edition
        var ce = Header(Path.Combine(cData, "elements.data"));
        var se = Header(Path.Combine(sCfg, "elements.data"));
        rows.Add(Compare(Loc.T("chk.elementsDataVersion", "elements.data — versão"), ce is null ? null : $"v{ce.Value.A & 0xFFFF}", se is null ? null : $"v{se.Value.A & 0xFFFF}",
            Loc.T("chk.clientAndServerNeedThe", "Cliente e servidor precisam da mesma versão de elements.data (exporte os dois juntos).")));
        if (ce is { } c1 && se is { } s1)
            rows.Add(new(Loc.T("chk.elementsDataExportDate", "elements.data — data de exportação"), Unix(c1.B), Unix(s1.B), CheckLevel.Info,
                Loc.T("chk.informativeTheDateIsNot", "Informativo: a data não entra na verificação de login.")));

        // 1b) cópias do elements.data por mapa (gamed/config/aNN/…) devem ser iguais à principal
        if (Directory.Exists(sCfg) && se is not null)
        {
            long mainSize = new FileInfo(Path.Combine(sCfg, "elements.data")).Length;
            foreach (var copy in Directory.EnumerateFiles(sCfg, "elements.data", SearchOption.AllDirectories))
            {
                if (Path.GetDirectoryName(copy) == Path.GetFullPath(sCfg).TrimEnd('\\', '/') || copy == Path.Combine(sCfg, "elements.data")) continue;
                var h = Header(copy);
                long size = new FileInfo(copy).Length;
                bool same = h is { } x && x.A == se.Value.A && size == mainSize;
                rows.Add(new(Loc.T("chk.elementsDataCopy", "elements.data — cópia ") + Path.GetRelativePath(sCfg, copy), "—",
                    h is null ? Loc.T("chk.unreadable", "ilegível") : $"v{h.Value.A & 0xFFFF}, {size:N0} B", same ? CheckLevel.Ok : CheckLevel.Aviso,
                    same ? Loc.T("chk.sameAsTheMainOne", "Igual à principal.")
                         : Loc.T("chk.copyDiffersFromTheMain", "Cópia diferente da principal (versão antiga). Só afeta o jogo se o gs.conf desse mapa apontar para ela; senão, é sobra e pode ser apagada.")));
            }
        }

        // 1b2) janelas que o elementclient.exe procura e o interfaces.pck não carrega
        var dlgItem = Loc.T("chk.interfacesPckWindowsTheGame", "interfaces.pck — janelas que o jogo procura");
        try
        {
            if (ClientDialogs.Find(clientElement) is { Count: > 0 } dlgs)
                rows.Add(new(dlgItem, Loc.T("chk.missing", "faltam ") + dlgs.Count + ": " + string.Join(", ", dlgs.Select(d => d.Dialog)), "—", CheckLevel.Erro,
                    Loc.T("chk.theyAreInThePackage", "Estão no pacote, mas fora da lista de janelas (.dcf): o jogo cai quando o código toca nelas (ex.: Win_Engrave ao fechar o inventário pelo X). Use Corrigir.")));
        }
        catch (Exception ex) { rows.Add(new(dlgItem, Loc.T("chk.couldNotRead", "não consegui ler: ") + ex.Message, "—", CheckLevel.Aviso, "")); }

        // 1c) versão que os PROGRAMAS têm compilada: cliente × gs (login) e cada programa × o elements que ele vai ler
        var cExe = ProgramVersion.ElementsVersion(Path.Combine(clientElement, "elementclient.exe"));
        var gsExe = ProgramVersion.ElementsVersion(Path.Combine(serverHome, "gamed", "gs"));
        string V(int? v) => v is null ? Loc.T("chk.unknown", "não identificada") : $"v{v}" + (ProgramVersion.GameFor(v.Value) is { Length: > 0 } g ? $" ({g})" : "");
        var progItem = Loc.T("chk.programsCompiledElementsVersion", "Programas — versão do elements compilada");
        if (cExe is not null || gsExe is not null)
        {
            rows.Add(cExe is null || gsExe is null
                ? new(progItem, V(cExe), V(gsExe), CheckLevel.Aviso, Loc.T("chk.couldNotReadTheVersion", "Não consegui ler a versão de um dos programas."))
                : cExe == gsExe
                    ? new(progItem, V(cExe), V(gsExe), CheckLevel.Ok, Loc.T("chk.elementclientExeAndGsAre", "elementclient.exe e gs são da mesma versão."))
                    : new(progItem, V(cExe), V(gsExe), CheckLevel.Erro,
                        string.Format(Loc.T("chk.clientAndGsAreDifferent", "O cliente e o gs são de versões diferentes: o login é recusado mesmo com os dados iguais. Use um pacote de servidor cujo gs seja v{0}"), cExe)
                        + (ProgramVersion.GameFor(cExe.Value) is { Length: > 0 } g ? string.Format(Loc.T("chk.game0", " (jogo {0})"), g) : "")
                        + Loc.T("chk.orAClientOfThe", ", ou um cliente da versão do servidor.")));
            if (cExe is { } cv && ce is { } cd && cv != (cd.A & 0xFFFF))
                rows.Add(new(Loc.T("chk.elementclientExeClientElementsData", "elementclient.exe × elements.data do cliente"), V(cExe), "—", CheckLevel.Erro,
                    string.Format(Loc.T("chk.elementclientExeExpectsElementsV", "O elementclient.exe espera elements v{0}, mas o elements.data do cliente é v{1}. Converta com Arquivo → Salvar em outra versão (v{0}) ou use o executável da versão dos dados."), cv, cd.A & 0xFFFF)));
            if (gsExe is { } gv && se is { } sd && gv != (sd.A & 0xFFFF))
                rows.Add(new(Loc.T("chk.gsServerElementsData", "gs × elements.data do servidor"), "—", string.Format(Loc.T("chk.gsV0FileV", "gs v{0}, arquivo v{1}"), gv, sd.A & 0xFFFF), CheckLevel.Erro,
                    string.Format(Loc.T("chk.gsOnlyOpensElementsV", "O gs só abre elements v{0}: com este arquivo ele cai ao iniciar. Converta para v{0} (Arquivo → Salvar em outra versão)."), gv)));
        }

        // 2) tasks.data — versão entra na edition
        var ct = Header(Path.Combine(cData, "tasks.data"));
        var st = Header(Path.Combine(sCfg, "tasks.data"));
        rows.Add(Compare(Loc.T("chk.tasksDataVersion", "tasks.data — versão"), ct is null ? null : $"v{ct.Value.B}", st is null ? null : $"v{st.Value.B}",
            Loc.T("chk.differentTasksDataVersionLogin", "Versão do tasks.data diferente: o login é recusado.")));
        rows.Add(Compare(Loc.T("chk.tasksDataQuestCount", "tasks.data — nº de quests"), ct is null ? null : $"{ct.Value.C:N0}", st is null ? null : $"{st.Value.C:N0}",
            Loc.T("chk.differentQuestCountClientAnd", "Quantidade de quests diferente: cliente e servidor estão com tasks.data de origens diferentes."), CheckLevel.Aviso));

        // 3) loja: gshop ↔ gshopsev (timestamps entram na edition)
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
        {
            var cg = Header(Path.Combine(cData, cn));
            var sg = Header(Path.Combine(sCfg, sn));
            rows.Add(Compare($"{cn} × {sn} — timestamp", cg is null ? null : Unix(cg.Value.A), sg is null ? null : Unix(sg.Value.A),
                Loc.T("chk.differentShopTimestampLoginIs", "Timestamp da loja diferente: o login é recusado (\"versão errada\"). Com o servidor local, o que vale são as linhas \"servidor local\" (o Salvar e enviar grava lá, não nesta pasta).")));
            rows.Add(Compare($"{cn} × {sn} — " + Loc.T("chk.itemCount", "nº de itens"), cg is null ? null : $"{cg.Value.B:N0}", sg is null ? null : $"{sg.Value.B:N0}",
                Loc.T("chk.differentNumberOfShopItems", "Quantidade de itens da loja diferente."), CheckLevel.Aviso));
        }

        // 3b) servidor local (WSL): é nele que o "Salvar e enviar" grava, não na pasta do pacote acima
        var live = LocalServer.DistroExists() ? $@"\\wsl.localhost\{LocalServer.Distro}\home\gamed\config" : null;
        if (live is not null && Directory.Exists(live))
        {
            var why = Loc.T("chk.theLocalServerHasA", "O servidor local está com outro arquivo: use \"Salvar e enviar ao servidor\" (a aba Servidor mostra o envio).");
            var local = Loc.T("chk.localServer", "servidor local — ");
            var le = Header(Path.Combine(live, "elements.data"));
            rows.Add(Compare(local + Loc.T("chk.elementsDataVersion2", "elements.data versão"), ce is null ? null : $"v{ce.Value.A & 0xFFFF}", le is null ? null : $"v{le.Value.A & 0xFFFF}", why));
            var lt = Header(Path.Combine(live, "tasks.data"));
            rows.Add(Compare(local + Loc.T("chk.tasksDataVersion2", "tasks.data versão"), ct is null ? null : $"v{ct.Value.B}", lt is null ? null : $"v{lt.Value.B}", why));
            foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
            {
                var cg = Header(Path.Combine(cData, cn));
                var lg = Header(Path.Combine(live, sn));
                rows.Add(Compare(local + $"{cn} × {sn} timestamp", cg is null ? null : Unix(cg.Value.A), lg is null ? null : Unix(lg.Value.A), why));
            }
        }

        // 3c) servidor local: os PROGRAMAS instalados (o glinkd manda a versão ao cliente no login) têm de ser os desta pasta;
        //     o "Enviar" leva só os dados, então um servidor instalado com outro pacote recusa o cliente ("versão baixa")
        if (LocalServer.LiveHome is { } liveHome)
        {
            var local = Loc.T("chk.localServer", "servidor local — ");
            string? Versions(string home) => File.Exists(Path.Combine(home, "glinkd", "gamesys.conf"))
                ? string.Join(", ", ParseIni(Path.Combine(home, "glinkd", "gamesys.conf")).Where(kv => kv.Key.StartsWith("GLinkServer", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Value.GetValueOrDefault("version")).Where(v => v is not null).Distinct().Select(HexVersion)) : null;
            rows.Add(Compare(local + Loc.T("chk.glinkdInstalled", "glinkd versão (desta pasta × instalado)"), Versions(serverHome), Versions(liveHome),
                Loc.T("chk.glinkdInstalledDiffers", "O servidor local roda programas de outra versão: o cliente ouve \"versão do cliente baixa\" e o login é recusado. Use Corrigir (ou Enviar ao servidor): instala os programas desta pasta.")));
            // o que decide: o gs instalado é da versão do elementclient.exe? (para cima ou para baixo, o Corrigir troca)
            var inst = LocalServer.InstalledVersion();
            if (cExe is not null && inst is not null)
                rows.Add(cExe == inst
                    ? new(local + Loc.T("chk.programs", "programas"), V(cExe), V(inst), CheckLevel.Ok, Loc.T("chk.programsMatchClient", "Os programas instalados são da versão do cliente."))
                    : new(local + Loc.T("chk.programs", "programas"), V(cExe), V(inst), CheckLevel.Erro,
                        (LocalServer.SwapFor(clientElement, Path.GetDirectoryName(serverHome.TrimEnd('\\', '/')) ?? serverHome, out var missing) is { } sw
                            ? string.Format(Loc.T(sw.Downgrade ? "chk.programsDowngrade" : "chk.programsUpgrade",
                                sw.Downgrade ? "O servidor local é de versão mais nova que o cliente: o login é recusado. Use Corrigir: volta os programas para os de {0}."
                                             : "O servidor local é de versão mais antiga que o cliente (\"versão do cliente baixa\"): o login é recusado. Use Corrigir: instala os programas de {0}."), sw.Package)
                            : missing ?? "")));
        }

        // 4) aipolicy.data
        var ca =Header(Path.Combine(cData, "aipolicy.data"));
        var sa = Header(Path.Combine(sCfg, "aipolicy.data"));
        rows.Add(Compare(Loc.T("chk.aipolicyDataVersionPolicyCount", "aipolicy.data — versão / nº de políticas"),
            ca is null ? null : $"v{ca.Value.A} / {ca.Value.B:N0}", sa is null ? null : $"v{sa.Value.A} / {sa.Value.B:N0}",
            Loc.T("chk.monsterAiDiffersBetweenClient", "IA de monstros diferente entre cliente e servidor."), CheckLevel.Aviso));

        // 5) rede: versão do glinkd e chaves isec/osec (sem mostrar valores)
        var glinkConf = Path.Combine(serverHome, "glinkd", "gamesys.conf");
        var dbConf = Path.Combine(clientElement, "dbserver.conf");
        if (File.Exists(glinkConf))
        {
            var links = ParseIni(glinkConf).Where(kv => kv.Key.StartsWith("GLinkServer", StringComparison.OrdinalIgnoreCase)).ToList();
            var versions = links.Select(l => l.Value.GetValueOrDefault("version")).Where(v => v is not null).Distinct().ToList();
            rows.Add(new(Loc.T("chk.glinkdGameVersion", "glinkd — versão do jogo"), "—", string.Join(", ", versions.Select(HexVersion)),
                versions.Count == 1 ? CheckLevel.Info : CheckLevel.Aviso,
                versions.Count == 1 ? Loc.T("chk.mustMatchTheElementclientExe", "Precisa casar com a versão do elementclient.exe.")
                                    : Loc.T("chk.glinkserversWithDifferentVersions", "GLinkServers com versões diferentes.")));

            var client = File.Exists(dbConf) ? ParseIni(dbConf).GetValueOrDefault("GameClient") : null;
            foreach (var (name, sect) in links)
            {
                var (lvl, msg, cDesc, sDesc) = KeysMatch(client, sect);
                rows.Add(new(Loc.T("chk.clientEncryption", "criptografia cliente × ") + name, cDesc, sDesc, lvl, msg));
            }
        }
        return rows;
    }

    // ----- helpers -----

    static (uint A, uint B, uint C)? Header(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var f = File.OpenRead(path);
            var b = new byte[12];
            if (f.Read(b, 0, 12) < 8) return null;   // loja vazia (gshopsev1) tem só 8 bytes: timestamp + 0 itens
            return (BitConverter.ToUInt32(b, 0), BitConverter.ToUInt32(b, 4), BitConverter.ToUInt32(b, 8));
        }
        catch { return null; }
    }

    static CheckRow Compare(string item, string? c, string? s, string dica, CheckLevel onDiff = CheckLevel.Erro)
    {
        if (c is null && s is null) return new(item, NotFound, NotFound, CheckLevel.Info, Loc.T("chk.fileMissingOnBothSides", "Arquivo ausente nos dois lados."));
        if (c is null || s is null) return new(item, c ?? NotFound, s ?? NotFound, CheckLevel.Aviso, Loc.T("chk.fileMissingOnOneSide", "Arquivo ausente em um dos lados."));
        return c == s ? new(item, c, s, CheckLevel.Ok, Loc.T("chk.same", "Iguais.")) : new(item, c, s, onDiff, dica);
    }

    static string Unix(uint t) => t == 0 ? "0" : DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    static string HexVersion(string? v)
    {
        if (v is null || !int.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out int x)) return v ?? "?";
        return $"{v} (= {x >> 16}.{(x >> 8) & 0xFF}.{x & 0xFF})";
    }

    // Chaves cruzadas: iseckey do cliente = oseckey do servidor e vice-versa (PW-KB §S24).
    static (CheckLevel, string, string, string) KeysMatch(Dictionary<string, string>? c, Dictionary<string, string> s)
    {
        bool cOn = c is not null && c.ContainsKey("isec");
        bool sOn = s.ContainsKey("isec");
        string Desc(bool on) => on ? Loc.T("chk.on", "ligada") : Loc.T("chk.off", "desligada");
        if (!cOn && !sOn) return (CheckLevel.Ok, Loc.T("chk.encryptionOffOnBothSides", "Criptografia desligada nos dois lados."), Desc(cOn), Desc(sOn));
        if (cOn != sOn) return (CheckLevel.Erro, Loc.T("chk.encryptionOnOnlyOneSide", "Criptografia ligada só de um lado: o cliente não conecta."), Desc(cOn), Desc(sOn));
        bool ok = c!.GetValueOrDefault("iseckey") == s.GetValueOrDefault("oseckey")
               && c.GetValueOrDefault("oseckey") == s.GetValueOrDefault("iseckey")
               && c.GetValueOrDefault("isec") == s.GetValueOrDefault("osec")
               && c.GetValueOrDefault("osec") == s.GetValueOrDefault("isec");
        return ok ? (CheckLevel.Ok, Loc.T("chk.keysCrossCorrectly", "Chaves cruzadas corretamente."), Desc(true), Desc(true))
                  : (CheckLevel.Erro, Loc.T("chk.keysDoNotCrossOne", "Chaves não cruzam (iseckey de um lado deve ser o oseckey do outro)."), Desc(true), Desc(true));
    }

    // .conf/.ini simples: [Seção] chave = valor ; linhas com ';' ou '#' são comentário
    static Dictionary<string, Dictionary<string, string>> ParseIni(string path)
    {
        var d = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? cur = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            var m = Regex.Match(line, @"^\[(.+)\]$");
            if (m.Success) { cur = new(StringComparer.OrdinalIgnoreCase); d[m.Groups[1].Value.Trim()] = cur; continue; }
            int eq = line.IndexOf('=');
            if (eq > 0 && cur is not null) cur[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return d;
    }
}
