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
    /// <param name="clientElement">pasta element\ do cliente</param>
    /// <param name="serverHome">pasta home/ do servidor (contém gamed/, glinkd/…)</param>
    public static List<CheckRow> Run(string clientElement, string serverHome)
    {
        var rows = new List<CheckRow>();
        string cData = Path.Combine(clientElement, "data");
        string sCfg = Path.Combine(serverHome, "gamed", "config");

        // 1) elements.data — versão entra na edition
        var ce = Header(Path.Combine(cData, "elements.data"));
        var se = Header(Path.Combine(sCfg, "elements.data"));
        rows.Add(Compare("elements.data — 版本", ce is null ? null : $"v{ce.Value.A & 0xFFFF}", se is null ? null : $"v{se.Value.A & 0xFFFF}",
            "客户端和服务端必须使用相同版本的 elements.data，请同步导出两端文件。"));
        if (ce is { } c1 && se is { } s1)
            rows.Add(new("elements.data — 导出时间", Unix(c1.B), Unix(s1.B), CheckLevel.Info,
                "提示：导出时间不参与登录校验。"));

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
                rows.Add(new($"elements.data — 副本 {Path.GetRelativePath(sCfg, copy)}", "—",
                    h is null ? "无法读取" : $"v{h.Value.A & 0xFFFF}, {size:N0} B", same ? CheckLevel.Ok : CheckLevel.Aviso,
                    same ? "与主文件一致。" : "此副本与主文件不同，可能是旧版。仅当该地图的 gs.conf 指向此副本时才会影响游戏；否则可能是残留文件，删除前请确认引用关系。"));
            }
        }

        // 2) tasks.data — versão entra na edition
        var ct = Header(Path.Combine(cData, "tasks.data"));
        var st = Header(Path.Combine(sCfg, "tasks.data"));
        rows.Add(Compare("tasks.data — 版本", ct is null ? null : $"v{ct.Value.B}", st is null ? null : $"v{st.Value.B}",
            "tasks.data 版本不同会导致登录被拒绝。"));
        rows.Add(Compare("tasks.data — 任务数量", ct is null ? null : $"{ct.Value.C:N0}", st is null ? null : $"{st.Value.C:N0}",
            "任务数量不同：客户端和服务端的 tasks.data 来源不一致。", CheckLevel.Aviso));

        // 3) loja: gshop ↔ gshopsev (timestamps entram na edition)
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
        {
            var cg = Header(Path.Combine(cData, cn));
            var sg = Header(Path.Combine(sCfg, sn));
            rows.Add(Compare($"{cn} × {sn} — 时间戳", cg is null ? null : Unix(cg.Value.A), sg is null ? null : Unix(sg.Value.A),
                "商城时间戳不同会导致登录被拒绝（“版本错误”），请同步导出客户端和服务端商城文件。"));
            rows.Add(Compare($"{cn} × {sn} — 物品数量", cg is null ? null : $"{cg.Value.B:N0}", sg is null ? null : $"{sg.Value.B:N0}",
                "商城物品数量不同。", CheckLevel.Aviso));
        }

        // 4) aipolicy.data
        var ca = Header(Path.Combine(cData, "aipolicy.data"));
        var sa = Header(Path.Combine(sCfg, "aipolicy.data"));
        rows.Add(Compare("aipolicy.data — 版本 / 策略数量",
            ca is null ? null : $"v{ca.Value.A} / {ca.Value.B:N0}", sa is null ? null : $"v{sa.Value.A} / {sa.Value.B:N0}",
            "客户端和服务端的怪物 AI 不一致。", CheckLevel.Aviso));

        // 5) rede: versão do glinkd e chaves isec/osec (sem mostrar valores)
        var glinkConf = Path.Combine(serverHome, "glinkd", "gamesys.conf");
        var dbConf = Path.Combine(clientElement, "dbserver.conf");
        if (File.Exists(glinkConf))
        {
            var links = ParseIni(glinkConf).Where(kv => kv.Key.StartsWith("GLinkServer", StringComparison.OrdinalIgnoreCase)).ToList();
            var versions = links.Select(l => l.Value.GetValueOrDefault("version")).Where(v => v is not null).Distinct().ToList();
            rows.Add(new("glinkd — 游戏版本", "—", string.Join(", ", versions.Select(HexVersion)),
                versions.Count == 1 ? CheckLevel.Info : CheckLevel.Aviso,
                versions.Count == 1 ? "必须与 elementclient.exe 的版本一致。" : "各 GLinkServer 的版本不同。"));

            var client = File.Exists(dbConf) ? ParseIni(dbConf).GetValueOrDefault("GameClient") : null;
            foreach (var (name, sect) in links)
            {
                var (lvl, msg, cDesc, sDesc) = KeysMatch(client, sect);
                rows.Add(new($"客户端加密 × {name}", cDesc, sDesc, lvl, msg));
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
            if (f.Read(b, 0, 12) < 12) return null;
            return (BitConverter.ToUInt32(b, 0), BitConverter.ToUInt32(b, 4), BitConverter.ToUInt32(b, 8));
        }
        catch { return null; }
    }

    static CheckRow Compare(string item, string? c, string? s, string dica, CheckLevel onDiff = CheckLevel.Erro)
    {
        if (c is null && s is null) return new(item, "未找到", "未找到", CheckLevel.Info, "两端均缺少此文件。");
        if (c is null || s is null) return new(item, c ?? "未找到", s ?? "未找到", CheckLevel.Aviso, "其中一端缺少此文件。");
        return c == s ? new(item, c, s, CheckLevel.Ok, "一致。") : new(item, c, s, onDiff, dica);
    }

    static string Unix(uint t) => t == 0 ? "0" : DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

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
        string Desc(bool on) => on ? "已启用" : "未启用";
        if (!cOn && !sOn) return (CheckLevel.Ok, "两端均未启用加密。", Desc(cOn), Desc(sOn));
        if (cOn != sOn) return (CheckLevel.Erro, "仅有一端启用加密，客户端无法连接。", Desc(cOn), Desc(sOn));
        bool ok = c!.GetValueOrDefault("iseckey") == s.GetValueOrDefault("oseckey")
               && c.GetValueOrDefault("oseckey") == s.GetValueOrDefault("iseckey")
               && c.GetValueOrDefault("isec") == s.GetValueOrDefault("osec")
               && c.GetValueOrDefault("osec") == s.GetValueOrDefault("isec");
        return ok ? (CheckLevel.Ok, "双向密钥配置正确。", "已启用", "已启用")
                  : (CheckLevel.Erro, "双向密钥不匹配：一端的 iseckey 应对应另一端的 oseckey。", "已启用", "已启用");
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
