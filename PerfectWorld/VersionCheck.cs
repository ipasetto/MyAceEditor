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
    static string T(string pt, string en) => AppLang.T(pt, en);

    /// <summary>Texto de arquivo ausente (a tela compara com ele para dizer que nada foi achado).</summary>
    public static string NotFound => T("não encontrado", "not found");

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
        rows.Add(Compare(T("elements.data — versão", "elements.data — version"), ce is null ? null : $"v{ce.Value.A & 0xFFFF}", se is null ? null : $"v{se.Value.A & 0xFFFF}",
            T("Cliente e servidor precisam da mesma versão de elements.data (exporte os dois juntos).", "Client and server need the same elements.data version (export both together).")));
        if (ce is { } c1 && se is { } s1)
            rows.Add(new(T("elements.data — data de exportação", "elements.data — export date"), Unix(c1.B), Unix(s1.B), CheckLevel.Info,
                T("Informativo: a data não entra na verificação de login.", "Informative: the date is not part of the login check.")));

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
                rows.Add(new(T("elements.data — cópia ", "elements.data — copy ") + Path.GetRelativePath(sCfg, copy), "—",
                    h is null ? T("ilegível", "unreadable") : $"v{h.Value.A & 0xFFFF}, {size:N0} B", same ? CheckLevel.Ok : CheckLevel.Aviso,
                    same ? T("Igual à principal.", "Same as the main one.")
                         : T("Cópia diferente da principal (versão antiga). Só afeta o jogo se o gs.conf desse mapa apontar para ela; senão, é sobra e pode ser apagada.",
                             "Copy differs from the main one (old version). It only matters if that map's gs.conf points to it; otherwise it is a leftover and can be deleted.")));
            }
        }

        // 1b2) janelas que o elementclient.exe procura e o interfaces.pck não carrega
        var dlgItem = T("interfaces.pck — janelas que o jogo procura", "interfaces.pck — windows the game looks for");
        try
        {
            if (ClientDialogs.Find(clientElement) is { Count: > 0 } dlgs)
                rows.Add(new(dlgItem, T("faltam ", "missing ") + dlgs.Count + ": " + string.Join(", ", dlgs.Select(d => d.Dialog)), "—", CheckLevel.Erro,
                    T("Estão no pacote, mas fora da lista de janelas (.dcf): o jogo cai quando o código toca nelas (ex.: Win_Engrave ao fechar o inventário pelo X). Use Corrigir.",
                      "They are in the package but not in the window list (.dcf): the game crashes when the code touches them (e.g. Win_Engrave when closing the inventory with X). Use Fix.")));
        }
        catch (Exception ex) { rows.Add(new(dlgItem, T("não consegui ler: ", "could not read: ") + ex.Message, "—", CheckLevel.Aviso, "")); }

        // 1c) versão que os PROGRAMAS têm compilada: cliente × gs (login) e cada programa × o elements que ele vai ler
        var cExe = ProgramVersion.ElementsVersion(Path.Combine(clientElement, "elementclient.exe"));
        var gsExe = ProgramVersion.ElementsVersion(Path.Combine(serverHome, "gamed", "gs"));
        string V(int? v) => v is null ? T("não identificada", "unknown") : $"v{v}" + (ProgramVersion.GameFor(v.Value) is { Length: > 0 } g ? $" ({g})" : "");
        var progItem = T("Programas — versão do elements compilada", "Programs — compiled elements version");
        if (cExe is not null || gsExe is not null)
        {
            rows.Add(cExe is null || gsExe is null
                ? new(progItem, V(cExe), V(gsExe), CheckLevel.Aviso, T("Não consegui ler a versão de um dos programas.", "Could not read the version of one of the programs."))
                : cExe == gsExe
                    ? new(progItem, V(cExe), V(gsExe), CheckLevel.Ok, T("elementclient.exe e gs são da mesma versão.", "elementclient.exe and gs are the same version."))
                    : new(progItem, V(cExe), V(gsExe), CheckLevel.Erro,
                        T($"O cliente e o gs são de versões diferentes: o login é recusado mesmo com os dados iguais. Use um pacote de servidor cujo gs seja v{cExe}",
                          $"Client and gs are different versions: login is refused even with identical data. Use a server package whose gs is v{cExe}")
                        + (ProgramVersion.GameFor(cExe.Value) is { Length: > 0 } g ? T($" (jogo {g})", $" (game {g})") : "")
                        + T(", ou um cliente da versão do servidor.", ", or a client of the server's version.")));
            if (cExe is { } cv && ce is { } cd && cv != (cd.A & 0xFFFF))
                rows.Add(new(T("elementclient.exe × elements.data do cliente", "elementclient.exe × client elements.data"), V(cExe), "—", CheckLevel.Erro,
                    T($"O elementclient.exe espera elements v{cv}, mas o elements.data do cliente é v{cd.A & 0xFFFF}. Converta com Arquivo → Salvar em outra versão (v{cv}) ou use o executável da versão dos dados.",
                      $"elementclient.exe expects elements v{cv}, but the client's elements.data is v{cd.A & 0xFFFF}. Convert it with File → Save as another version (v{cv}) or use the executable of the data's version.")));
            if (gsExe is { } gv && se is { } sd && gv != (sd.A & 0xFFFF))
                rows.Add(new(T("gs × elements.data do servidor", "gs × server elements.data"), "—", T($"gs v{gv}, arquivo v{sd.A & 0xFFFF}", $"gs v{gv}, file v{sd.A & 0xFFFF}"), CheckLevel.Erro,
                    T($"O gs só abre elements v{gv}: com este arquivo ele cai ao iniciar. Converta para v{gv} (Arquivo → Salvar em outra versão).",
                      $"gs only opens elements v{gv}: with this file it crashes on start. Convert to v{gv} (File → Save as another version).")));
        }

        // 2) tasks.data — versão entra na edition
        var ct = Header(Path.Combine(cData, "tasks.data"));
        var st = Header(Path.Combine(sCfg, "tasks.data"));
        rows.Add(Compare(T("tasks.data — versão", "tasks.data — version"), ct is null ? null : $"v{ct.Value.B}", st is null ? null : $"v{st.Value.B}",
            T("Versão do tasks.data diferente: o login é recusado.", "Different tasks.data version: login is refused.")));
        rows.Add(Compare(T("tasks.data — nº de quests", "tasks.data — quest count"), ct is null ? null : $"{ct.Value.C:N0}", st is null ? null : $"{st.Value.C:N0}",
            T("Quantidade de quests diferente: cliente e servidor estão com tasks.data de origens diferentes.", "Different quest count: client and server have tasks.data from different sources."), CheckLevel.Aviso));

        // 3) loja: gshop ↔ gshopsev (timestamps entram na edition)
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
        {
            var cg = Header(Path.Combine(cData, cn));
            var sg = Header(Path.Combine(sCfg, sn));
            rows.Add(Compare($"{cn} × {sn} — timestamp", cg is null ? null : Unix(cg.Value.A), sg is null ? null : Unix(sg.Value.A),
                T("Timestamp da loja diferente: o login é recusado (\"versão errada\"). Com o servidor local, o que vale são as linhas \"servidor local\" (o Salvar e enviar grava lá, não nesta pasta).",
                  "Different shop timestamp: login is refused (\"wrong version\"). With the local server, the \"local server\" rows are what counts (Save and send writes there, not to this folder).")));
            rows.Add(Compare($"{cn} × {sn} — " + T("nº de itens", "item count"), cg is null ? null : $"{cg.Value.B:N0}", sg is null ? null : $"{sg.Value.B:N0}",
                T("Quantidade de itens da loja diferente.", "Different number of shop items."), CheckLevel.Aviso));
        }

        // 3b) servidor local (WSL): é nele que o "Salvar e enviar" grava, não na pasta do pacote acima
        var live = LocalServer.DistroExists() ? $@"\\wsl.localhost\{LocalServer.Distro}\home\gamed\config" : null;
        if (live is not null && Directory.Exists(live))
        {
            var why = T("O servidor local está com outro arquivo: use \"Salvar e enviar ao servidor\" (a aba Servidor mostra o envio).",
                        "The local server has a different file: use \"Save and send to server\" (the Server tab shows the transfer).");
            var local = T("servidor local — ", "local server — ");
            var le = Header(Path.Combine(live, "elements.data"));
            rows.Add(Compare(local + T("elements.data versão", "elements.data version"), ce is null ? null : $"v{ce.Value.A & 0xFFFF}", le is null ? null : $"v{le.Value.A & 0xFFFF}", why));
            var lt = Header(Path.Combine(live, "tasks.data"));
            rows.Add(Compare(local + T("tasks.data versão", "tasks.data version"), ct is null ? null : $"v{ct.Value.B}", lt is null ? null : $"v{lt.Value.B}", why));
            foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
            {
                var cg = Header(Path.Combine(cData, cn));
                var lg = Header(Path.Combine(live, sn));
                rows.Add(Compare(local + $"{cn} × {sn} timestamp", cg is null ? null : Unix(cg.Value.A), lg is null ? null : Unix(lg.Value.A), why));
            }
        }

        // 4) aipolicy.data
        var ca = Header(Path.Combine(cData, "aipolicy.data"));
        var sa = Header(Path.Combine(sCfg, "aipolicy.data"));
        rows.Add(Compare(T("aipolicy.data — versão / nº de políticas", "aipolicy.data — version / policy count"),
            ca is null ? null : $"v{ca.Value.A} / {ca.Value.B:N0}", sa is null ? null : $"v{sa.Value.A} / {sa.Value.B:N0}",
            T("IA de monstros diferente entre cliente e servidor.", "Monster AI differs between client and server."), CheckLevel.Aviso));

        // 5) rede: versão do glinkd e chaves isec/osec (sem mostrar valores)
        var glinkConf = Path.Combine(serverHome, "glinkd", "gamesys.conf");
        var dbConf = Path.Combine(clientElement, "dbserver.conf");
        if (File.Exists(glinkConf))
        {
            var links = ParseIni(glinkConf).Where(kv => kv.Key.StartsWith("GLinkServer", StringComparison.OrdinalIgnoreCase)).ToList();
            var versions = links.Select(l => l.Value.GetValueOrDefault("version")).Where(v => v is not null).Distinct().ToList();
            rows.Add(new(T("glinkd — versão do jogo", "glinkd — game version"), "—", string.Join(", ", versions.Select(HexVersion)),
                versions.Count == 1 ? CheckLevel.Info : CheckLevel.Aviso,
                versions.Count == 1 ? T("Precisa casar com a versão do elementclient.exe.", "Must match the elementclient.exe version.")
                                    : T("GLinkServers com versões diferentes.", "GLinkServers with different versions.")));

            var client = File.Exists(dbConf) ? ParseIni(dbConf).GetValueOrDefault("GameClient") : null;
            foreach (var (name, sect) in links)
            {
                var (lvl, msg, cDesc, sDesc) = KeysMatch(client, sect);
                rows.Add(new(T("criptografia cliente × ", "client encryption × ") + name, cDesc, sDesc, lvl, msg));
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
        if (c is null && s is null) return new(item, NotFound, NotFound, CheckLevel.Info, T("Arquivo ausente nos dois lados.", "File missing on both sides."));
        if (c is null || s is null) return new(item, c ?? NotFound, s ?? NotFound, CheckLevel.Aviso, T("Arquivo ausente em um dos lados.", "File missing on one side."));
        return c == s ? new(item, c, s, CheckLevel.Ok, T("Iguais.", "Same.")) : new(item, c, s, onDiff, dica);
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
        string Desc(bool on) => on ? T("ligada", "on") : T("desligada", "off");
        if (!cOn && !sOn) return (CheckLevel.Ok, T("Criptografia desligada nos dois lados.", "Encryption off on both sides."), Desc(cOn), Desc(sOn));
        if (cOn != sOn) return (CheckLevel.Erro, T("Criptografia ligada só de um lado: o cliente não conecta.", "Encryption on only one side: the client cannot connect."), Desc(cOn), Desc(sOn));
        bool ok = c!.GetValueOrDefault("iseckey") == s.GetValueOrDefault("oseckey")
               && c.GetValueOrDefault("oseckey") == s.GetValueOrDefault("iseckey")
               && c.GetValueOrDefault("isec") == s.GetValueOrDefault("osec")
               && c.GetValueOrDefault("osec") == s.GetValueOrDefault("isec");
        return ok ? (CheckLevel.Ok, T("Chaves cruzadas corretamente.", "Keys cross correctly."), Desc(true), Desc(true))
                  : (CheckLevel.Erro, T("Chaves não cruzam (iseckey de um lado deve ser o oseckey do outro).", "Keys do not cross (one side's iseckey must be the other's oseckey)."), Desc(true), Desc(true));
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
