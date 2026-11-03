using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Servidor de cada cliente (pasta element), num lugar só (janela "Servidor e pastas"): tipo (local no WSL ou outro
/// Linux), pacote "server files", pasta do servidor local e disco máximo. Cada versão tem o seu (regra do usuário
/// 02/11). Verificação, Servidor, Loja e NPCGen leem daqui; nada de caminho repetido nas telas.
/// Gravado como os outros ajustes (%AppData%\MyAceEditor\&lt;chave&gt;@&lt;md5 do cliente&gt;.txt).
/// </summary>
internal static class ServerPaths
{
    public enum Kind { Local, Other }

    /// <param name="Kind">Local = servidor no WSL deste PC; Other = Linux em máquina virtual, na rede ou hospedado</param>
    public sealed record Entry(Kind Kind, string Package, string Base, int DiskGb)
    {
        /// <summary>home/ do pacote (Verificação, NPCGen) ou "" sem pacote.</summary>
        public string Home => Package.Length > 0 && Directory.Exists(Path.Combine(Package, "home")) ? Path.Combine(Package, "home") : "";
        /// <summary>home/gamed/config do pacote (loja, npcgen) ou "".</summary>
        public string Config => Home.Length > 0 ? Path.Combine(Home, "gamed", "config") : "";
    }

    /// <summary>As pastas de algum cliente mudaram (o editor repontua Verificação, Loja, NPCGen e Servidor).</summary>
    public static event Action<string>? Changed;

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor");
    static string? Load(string k) { try { var p = Path.Combine(Dir, k + ".txt"); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; } catch { return null; } }
    static void Save(string k, string v) { try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, k + ".txt"), v); } catch { } }

    public static string Key(string k, string client) => client.Length == 0 ? k
        : k + "@" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(client.TrimEnd('\\').ToLowerInvariant())))[..10];

    /// <summary>Já foi configurado para este cliente (ou o usuário recusou configurar)?</summary>
    public static bool Known(string client) => Load(Key("server_base", client)) is not null || Load(Key("server_pkg", client)) is not null;
    public static bool Declined(string client) => Load(Key("server_ask", client)) == "no";
    public static void Decline(string client) => Save(Key("server_ask", client), "no");

    /// <summary>
    /// O servidor deste cliente. Sem nada gravado: o pacote com o gs da versão do cliente perto do último usado (ou da
    /// Verificação) e a pasta sugerida &lt;disco&gt;\PWServer&lt;jogo&gt;v&lt;elements&gt;.
    /// </summary>
    public static Entry Get(string client)
    {
        var pkg = Load(Key("server_pkg", client));
        var bas = Load(Key("server_base", client));
        int? v = client.Length > 0 ? ProgramVersion.ElementsVersion(Path.Combine(client, "elementclient.exe")) : null;
        if (pkg is null || !Directory.Exists(Path.Combine(pkg, "home")))
        {
            string? hint = Load("server_pkg");
            if (hint is null && Load("check_server") is { Length: > 0 } h && Directory.Exists(h))
                hint = Path.GetDirectoryName(VersionCheck.ServerHome(h).TrimEnd('\\'));
            pkg = hint is null ? "" : v is { } x ? LocalServer.FindPackage(x, hint) ?? "" : hint;
        }
        bas ??= client.Length > 0 ? LocalServer.SuggestedBaseDir(client, pkg.Length > 0 ? Path.GetPathRoot(pkg)! : @"C:\") : "";
        var kind = Load(Key("server_kind", client)) == "other" ? Kind.Other : Kind.Local;
        int disk = int.TryParse(Load(Key("server_disk", client)), out var d) && d >= 16 ? d : 64;
        return new(kind, pkg, bas, disk);
    }

    /// <summary>Grava o servidor do cliente; o pacote também vira o da Verificação e da Loja (um servidor só por cliente).</summary>
    public static void Set(string client, Entry e)
    {
        Save(Key("server_kind", client), e.Kind == Kind.Other ? "other" : "local");
        Save(Key("server_pkg", client), e.Package);
        Save(Key("server_base", client), e.Base);
        Save(Key("server_disk", client), e.DiskGb.ToString());
        if (e.Package.Length > 0) Save("server_pkg", e.Package);
        if (e.Home.Length > 0) { Save("check_server", e.Home); Save("gshop_server", e.Config); }
        Changed?.Invoke(client);
    }
}
