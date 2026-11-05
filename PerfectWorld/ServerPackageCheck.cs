using MyAceEditor.UI;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Linhas da Verificação sobre o pacote de servidor antes de instalar (PW-KB §S34/§S35): 32 ou 64 bits, tipo de login
/// e bibliotecas antigas que precisam vir no pacote. Só leitura; servidor já instalado no WSL fica de fora das bibliotecas.
/// </summary>
public static class ServerPackageCheck
{
    public static IEnumerable<CheckRow> Rows(string serverHome)
    {
        var gs = Path.Combine(serverHome, "gamed", "gs");
        if (!File.Exists(gs)) yield break;
        var (bits, needed) = ReadElf(gs);
        if (bits == 0) yield break;

        yield return new(Loc.T("chk.pkgBits", "servidor — programas"), "—", $"{bits} bits", bits == 32 ? CheckLevel.Ok : CheckLevel.Aviso,
            bits == 32 ? "" : Loc.T("chk.pkg64", "Servidor de 64 bits (1.7.x): o instalador ainda não sabe montar este tipo."));

        var (login, known) = Login(serverHome);
        yield return new(Loc.T("chk.pkgLogin", "servidor — login"), "—", login, known ? CheckLevel.Ok : CheckLevel.Aviso,
            known ? "" : Loc.T("chk.pkgLoginNew", "Tipo de login que o instalador ainda não sabe configurar."));

        if (serverHome.StartsWith(@"\\")) yield break;   // instalado: as bibliotecas já estão no Linux
        var parent = Path.GetDirectoryName(serverHome.TrimEnd('\\', '/')) ?? serverHome;
        var missing = needed.Where(FromPackage).Where(l => !File.Exists(Path.Combine(parent, "lib", l))
            && !File.Exists(Path.Combine(serverHome, "gamed", l))).ToList();
        if (missing.Count > 0)
            yield return new(Loc.T("chk.pkgLibs", "servidor — bibliotecas do pacote"), "—", string.Join(", ", missing), CheckLevel.Erro,
                Loc.T("chk.pkgLibsMissing", "O gs não sobe sem elas e o pacote não as traz. Copie da mesma versão do servidor."));
    }

    // o Linux não fornece estas: precisam vir no pacote
    static bool FromPackage(string lib) => lib.StartsWith("libtask") || lib == "libstdc++.so.5" || lib == "libpcre.so.0";

    static (string Name, bool Known) Login(string home)
    {
        if (File.Exists(Path.Combine(home, "authd", "authd.class"))) return ("authd (Java)", true);
        if (Directory.Exists(Path.Combine(home, "mauthd", "build"))) return ("mauthd (Java)", true);
        if (Directory.Exists(Path.Combine(home, "auth", "build"))) return ("auth (Java)", true);
        if (File.Exists(Path.Combine(home, "gauthd", "gauthd"))) return ("gauthd", false);
        return (VersionCheck.NotFound, false);
    }

    /// <summary>Classe do ELF (32/64) e bibliotecas pedidas (DT_NEEDED). Arquivo que não é ELF: bits = 0.</summary>
    public static (int Bits, List<string> Needed) ReadElf(string path)
    {
        var needed = new List<string>();
        try
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 64 || b[0] != 0x7F || b[1] != 'E' || b[2] != 'L' || b[3] != 'F') return (0, needed);
            bool x64 = b[4] == 2;
            long shoff = x64 ? BitConverter.ToInt64(b, 40) : BitConverter.ToUInt32(b, 32);
            int shes = BitConverter.ToUInt16(b, x64 ? 58 : 46), shn = BitConverter.ToUInt16(b, x64 ? 60 : 48);
            long Off(int i) => BitConverter.ToUInt32(b, (int)(shoff + i * shes) + 4) == 0 ? 0 : x64
                ? BitConverter.ToInt64(b, (int)(shoff + i * shes) + 24) : BitConverter.ToUInt32(b, (int)(shoff + i * shes) + 16);
            for (int i = 0; i < shn; i++)
            {
                int s = (int)(shoff + i * shes);
                if (BitConverter.ToUInt32(b, s + 4) != 6) continue;   // SHT_DYNAMIC
                long off = Off(i), size = x64 ? BitConverter.ToInt64(b, s + 32) : BitConverter.ToUInt32(b, s + 20);
                int link = BitConverter.ToInt32(b, s + (x64 ? 40 : 24)), entry = x64 ? 16 : 8;
                long strings = Off(link);
                for (long e = off; e + entry <= off + size; e += entry)
                {
                    long tag = x64 ? BitConverter.ToInt64(b, (int)e) : BitConverter.ToInt32(b, (int)e);
                    if (tag == 0) break;
                    if (tag != 1) continue;
                    long val = x64 ? BitConverter.ToInt64(b, (int)e + 8) : BitConverter.ToUInt32(b, (int)e + 4);
                    int p = (int)(strings + val), end = Array.IndexOf(b, (byte)0, p);
                    needed.Add(Encoding.ASCII.GetString(b, p, end - p));
                }
            }
            return (x64 ? 64 : 32, needed);
        }
        catch (Exception ex) { AppLog.Error("ELF: " + ex.Message); return (0, needed); }
    }
}
