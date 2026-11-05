using MyAceEditor.UI;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Linhas da Verificação tiradas dos registros do cliente (element\logs\EC.log e arquivos .dmp, PW-KB §S35):
/// se a última sessão fechou direito, que arquivos faltaram e onde foi o último crash. Só leitura.
/// </summary>
public static class ClientLogCheck
{
    const string Closed = "Log file closed successfully";
    const string LoadFail = "failed to load file:";

    public static IEnumerable<CheckRow> Rows(string clientElement)
    {
        var log = Path.Combine(clientElement, "logs", "EC.log");
        DateTime? logTime = File.Exists(log) ? File.GetLastWriteTime(log) : null;
        if (logTime is { } when)
        {
            string[] lines;
            try
            {
                using var s = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(s, Encoding.Latin1);
                lines = r.ReadToEnd().Split('\n');
            }
            catch (Exception ex) { lines = []; AppLog.Error("EC.log: " + ex.Message); }
            if (lines.Length > 0)
            {
                bool closed = lines.Any(l => l.Contains(Closed));
                yield return new(Loc.T("chk.clientSession", "cliente — última sessão do jogo"), when.ToString("g"), "—",
                    closed ? CheckLevel.Ok : CheckLevel.Aviso,
                    closed ? Loc.T("chk.clientClosedOk", "O jogo fechou normalmente.")
                           : Loc.T("chk.clientClosedBad", "O registro do jogo terminou sem fechar: o jogo caiu ou ainda está aberto. Se você jogou em outra pasta, abra essa pasta aqui."));
                var missing = lines.Select(MissingFile).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (missing.Count > 0)
                    yield return new(Loc.T("chk.clientMissing", "cliente — arquivos que o jogo não achou"), missing.Count.ToString(), "—", CheckLevel.Info,
                        string.Format(Loc.T("chk.clientMissingLast", "Último: {0}"), missing[^1]));
            }
        }
        if (LatestDump(clientElement) is { } dump)
        {
            var time = File.GetLastWriteTime(dump);
            bool recent = logTime is null || time >= logTime.Value.AddMinutes(-2);
            yield return new(Loc.T("chk.clientCrash", "cliente — último crash"), time.ToString("g"), "—", recent ? CheckLevel.Erro : CheckLevel.Info,
                (DumpSummary(dump) ?? Path.GetFileName(dump))
                + (recent ? "" : " " + Loc.T("chk.clientCrashOld", "(antes da última sessão)")));
        }
    }

    static string? MissingFile(string line)
    {
        int i = line.IndexOf(LoadFail, StringComparison.Ordinal);
        return i < 0 ? null : line[(i + LoadFail.Length)..].Trim();
    }

    static string? LatestDump(string clientElement)
    {
        var found = new List<string>();
        foreach (var dir in new[] { clientElement, Path.Combine(clientElement, "logs") })
            try { if (Directory.Exists(dir)) found.AddRange(Directory.GetFiles(dir, "*.dmp")); }
            catch (Exception ex) { AppLog.Error("dmp: " + ex.Message); }
        return found.OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
    }

    /// <summary>Minidump: stream 6 = exceção (código, endereço), stream 4 = módulos. Devolve "código em módulo+offset".</summary>
    public static string? DumpSummary(string path)
    {
        try
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 32 || BitConverter.ToUInt32(b, 0) != 0x504D444D) return null;   // "MDMP"
            int count = BitConverter.ToInt32(b, 8), dir = BitConverter.ToInt32(b, 12), exc = 0, mods = 0;
            for (int i = 0; i < count; i++)
            {
                int e = dir + i * 12, type = BitConverter.ToInt32(b, e), rva = BitConverter.ToInt32(b, e + 8);
                if (type == 6) exc = rva; else if (type == 4) mods = rva;
            }
            if (exc == 0) return null;
            uint code = BitConverter.ToUInt32(b, exc + 8);
            ulong addr = BitConverter.ToUInt64(b, exc + 24);
            string where = $"0x{addr:X}";
            if (mods != 0)
            {
                int n = BitConverter.ToInt32(b, mods);
                for (int i = 0; i < n; i++)
                {
                    int m = mods + 4 + i * 108;
                    ulong start = BitConverter.ToUInt64(b, m);
                    uint size = BitConverter.ToUInt32(b, m + 8);
                    if (addr < start || addr >= start + size) continue;
                    int name = BitConverter.ToInt32(b, m + 20), len = BitConverter.ToInt32(b, name);
                    where = $"{Path.GetFileName(Encoding.Unicode.GetString(b, name + 4, len))}+0x{addr - start:X}";
                    break;
                }
            }
            return $"0x{code:X8} {Loc.T("chk.clientCrashAt", "em")} {where}";
        }
        catch (Exception ex) { AppLog.Error("dmp: " + ex.Message); return null; }
    }
}
