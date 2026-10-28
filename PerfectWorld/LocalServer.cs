using MyAceEditor.UI;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Servidor local sem mão do usuário (ENGINE-KB plano #27, PW-KB §S5 §S30): uma distro WSL própria ("PWServer", Ubuntu
/// 22.04 oficial importado numa pasta do usuário) recebe o pacote de servidor (pasta "server files" + SQL), instalado por
/// Server/provision.sh e controlado por pwctl (iniciar/parar/estado/conta). O par cliente × servidor é corrigido na
/// cópia do servidor (PairFixer) e o cliente ganha a entrada "Local" na lista de servidores. Nada nos arquivos
/// originais do servidor é alterado.
/// </summary>
public sealed class LocalServer
{
    public const string Distro = "PWServer";
    const string RootfsUrl = "https://cloud-images.ubuntu.com/wsl/releases/22.04/current/ubuntu-jammy-wsl-amd64-wsl.rootfs.tar.gz";
    const string SumsUrl = "https://cloud-images.ubuntu.com/wsl/releases/22.04/current/SHA256SUMS";

    /// <summary>Pasta da distro e do cache (ex.: D:\PWServer).</summary>
    public string BaseDir { get; }
    public Action<string> Log { get; set; } = _ => { };

    public LocalServer(string baseDir) { BaseDir = baseDir; }

    // ---------- WSL ----------
    public sealed record RunResult(int Code, string Output);

    /// <summary>wsl.exe com argumentos; WSL_UTF8=1 faz o próprio wsl responder em UTF-8, como os comandos do Linux.</summary>
    static RunResult Wsl(string args, string? stdin = null, Action<string>? line = null, int timeoutMs = 3_600_000)
    {
        var psi = new ProcessStartInfo("wsl.exe", args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = stdin is null ? null : new UTF8Encoding(false),
        };
        psi.Environment["WSL_UTF8"] = "1";
        using var p = Process.Start(psi)!;
        var sb = new StringBuilder();
        void On(string? s) { if (s is null) return; s = s.Replace("\0", ""); lock (sb) sb.AppendLine(s); line?.Invoke(s); }
        p.OutputDataReceived += (_, e) => On(e.Data); p.ErrorDataReceived += (_, e) => On(e.Data);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        if (stdin is not null) { p.StandardInput.Write(stdin.Replace("\r\n", "\n")); p.StandardInput.Close(); }
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return new(-1, sb + "\n(tempo esgotado)"); }
        // terminar de ler a saída, com prazo: um serviço iniciado que herde a saída não pode prender o programa
        Task.Run(() => p.WaitForExit()).Wait(5000);
        return new(p.ExitCode, sb.ToString());
    }

    /// <summary>Comando bash como root dentro da distro. --exec: sem ele o wsl passa a linha por um sh antes, que
    /// expande $variáveis do comando (ficavam vazias).</summary>
    public RunResult Bash(string command, Action<string>? line = null, string? stdin = null, int timeoutMs = 3_600_000) =>
        Wsl($"-d {Distro} -u root --exec bash -c \"{command.Replace("\"", "\\\"")}\"", stdin, line, timeoutMs);

    /// <summary>null = WSL pronto; senão, o que falta (texto para o usuário).</summary>
    public static string? WslProblem()
    {
        try
        {
            var r = Wsl("--status", timeoutMs: 60_000);
            var t = r.Output;
            if (t.Contains("virtualiza", StringComparison.OrdinalIgnoreCase) && (t.Contains("não está habilitada") || t.Contains("not enabled") || t.Contains("n\u00e3o est\u00e1")))
                return Loc.T("ls.wslNoVirt", "O WSL está instalado, mas a virtualização ainda não está ativa. Reinicie o computador (a \"Plataforma de Máquina Virtual\" só vale depois de reiniciar).");
            if (t.Contains("não está instalado") || t.Contains("is not installed"))
                return Loc.T("ls.wslMissing", "O WSL não está instalado. Rode \"wsl --install --no-distribution\" como administrador e reinicie.");
            return null;
        }
        catch (Exception ex) { return Loc.T("ls.wslRunFail", "Não consegui rodar o wsl.exe: ") + ex.Message; }
    }

    public static bool DistroExists()
    {
        var r = Wsl("-l -q", timeoutMs: 60_000);
        return r.Output.Split('\n').Any(l => l.Trim().Equals(Distro, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- instalação ----------
    public sealed record InstallOptions(string ServerFiles, string SqlDir, string ClientElement, string Login, string Password, bool Gm);

    /// <summary>Instala ou completa a instalação. Cada etapa vai para Log; exceção = parou (mensagem para o usuário).</summary>
    public async Task InstallAsync(InstallOptions o, IProgress<(string Step, double? Fraction)>? progress, CancellationToken ct)
    {
        if (WslProblem() is { } problem) throw new InvalidOperationException(problem);
        Directory.CreateDirectory(BaseDir);

        if (!DistroExists())
        {
            var tar = await DownloadRootfsAsync(progress, ct);
            progress?.Report((string.Format(Loc.T("ls.creatingLinux", "Criando o Linux do servidor ({0})"), Distro), null));
            var vm = Path.Combine(BaseDir, "vm");
            Directory.CreateDirectory(vm);
            var r = Wsl($"--import {Distro} \"{vm}\" \"{tar}\" --version 2", line: Log);
            if (r.Code != 0) throw new InvalidOperationException(Loc.T("ls.importFailed", "wsl --import falhou:") + "\n" + r.Output);
        }

        progress?.Report((Loc.T("ls.preparing", "Preparando a instalação"), null));
        Provision(o.ServerFiles, o.SqlDir, step => progress?.Report((step, null)));

        // par cliente × servidor na CÓPIA do servidor (elements, tasks e loja iguais ao cliente; senão o login é recusado)
        progress?.Report((Loc.T("ls.matching", "Igualando o servidor ao seu cliente (elements, tasks, loja)"), null));
        FixPair(o.ClientElement, null, Path.Combine(o.ServerFiles, "home", "gamed", "config"));

        progress?.Report((Loc.T("ls.creatingAccount", "Criando a sua conta"), null));
        var acc = Account(o.Login, o.Password, o.Gm);
        if (acc is not null) throw new InvalidOperationException(acc);

        progress?.Report((Loc.T("ls.addingLocal", "Colocando \"Local\" na lista de servidores do cliente"), null));
        AddToServerList(o.ClientElement);
        progress?.Report((Loc.T("ls.disablingAds", "Desligando a página de anúncios da internet que derruba o cliente"), null));
        FixClientPages(o.ClientElement);
        progress?.Report((Loc.T("ls.done", "Pronto"), 1));
    }

    /// <summary>
    /// Leva os scripts do programa para /root/pw (na distro) e roda o provision.sh com o pacote: programas, configs e
    /// endereços; o banco existente fica. Exceção = parou, com a mensagem para o usuário.
    /// </summary>
    void Provision(string serverFiles, string sqlDir, Action<string>? step = null)
    {
        var here = Path.Combine(AppContext.BaseDirectory, "Server");
        foreach (var f in new[] { "provision.sh", "pwctl.sh" })
        {
            var text = File.ReadAllText(Path.Combine(here, f));
            var w = Bash($"mkdir -p /root/pw && cat > /root/pw/{f} && chmod +x /root/pw/{f}", stdin: text, timeoutMs: 120_000);
            if (w.Code != 0) throw new InvalidOperationException(string.Format(Loc.T("ls.copyFailed", "Não consegui copiar {0} para o Linux:"), f) + "\n" + w.Output);
        }
        string? error = null;
        var res = Bash($"/root/pw/provision.sh '{ToWslPath(serverFiles)}' '{ToWslPath(sqlDir)}' '{DbPassword()}'", line =>
        {
            Log(line);
            if (line.StartsWith("##PASSO ")) step?.Invoke(line[8..]);
            else if (line.StartsWith("##ERRO ")) error = line[7..];
        });
        if (res.Code != 0 || error is not null) throw new InvalidOperationException(Loc.T("ls.installStopped", "A instalação parou: ") + (error ?? string.Format(Loc.T("ls.exitCode", "código {0}"), res.Code)));
    }

    // ---------- programas instalados × versão do cliente ----------
    /// <summary>Programas que fixam a versão do servidor: o glinkd manda a versão ao cliente no login; gs, gdeliveryd e gamedbd leem os dados.</summary>
    static readonly string[] Programs = { @"glinkd\glinkd", @"gdeliveryd\gdeliveryd", @"gamedbd\gamedbd", @"gamed\gs" };

    /// <summary>A home/ instalada no Linux, vista do Windows (null sem a distro ou sem servidor instalado).</summary>
    public static string? LiveHome => DistroExists() && Directory.Exists($@"\\wsl.localhost\{Distro}\home\glinkd") ? $@"\\wsl.localhost\{Distro}\home" : null;

    /// <summary>Versão do elements compilada no gs instalado no servidor local (null sem servidor ou não identificada).</summary>
    public static int? InstalledVersion() => LiveHome is { } h ? ProgramVersion.ElementsVersion(Path.Combine(h, "gamed", "gs")) : null;

    /// <summary>Programas instalados no servidor local diferentes dos da pasta do pacote. Vazio = iguais ou sem servidor local.</summary>
    public static List<string> ProgramsDiffering(string serverHome)
    {
        var diff = new List<string>();
        if (LiveHome is not { } live) return diff;
        foreach (var rel in Programs)
        {
            var pkg = Path.Combine(serverHome, rel); var inst = Path.Combine(live, rel);
            if (!File.Exists(pkg)) continue;
            if (!File.Exists(inst) || !SameEnds(pkg, inst)) diff.Add(Path.GetFileName(rel));
        }
        return diff;
    }

    // tamanho + 1º e último MB: o gs tem ~200 MB e é lido pela rede do WSL
    static bool SameEnds(string a, string b)
    {
        try
        {
            using var fa = File.OpenRead(a); using var fb = File.OpenRead(b);
            if (fa.Length != fb.Length) return false;
            const int n = 1 << 20;
            var ba = new byte[n]; var bb = new byte[n];
            foreach (long at in new[] { 0, Math.Max(0, fa.Length - n) })
            {
                fa.Position = at; fb.Position = at;
                int na = fa.ReadAtLeast(ba, n, false), nb = fb.ReadAtLeast(bb, n, false);
                if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Troca de programas do servidor local para a versão do cliente (atualizar ou voltar). Backup e Banco novo são
    /// escolhas do usuário: o banco de personagens de outra versão em geral não abre (o uniquenamed não inicia).
    /// </summary>
    public sealed class ProgramSwap
    {
        public required string Package { get; init; }   // pasta "server files" com os programas da versão do cliente
        public int? From { get; init; }                  // versão instalada (elements compilada no gs)
        public int? To { get; init; }                    // versão do cliente
        public bool Downgrade => From is { } f && To is { } t && t < f;
        public bool Backup { get; set; }
        public bool FreshDatabase { get; set; } = true;
    }

    /// <summary>
    /// O que fazer para o servidor local casar com o cliente: null se já casa, se não há servidor instalado ou se não deu
    /// para ler as versões. Procura o pacote com o gs da versão do cliente: primeiro <paramref name="preferred"/>, depois
    /// as pastas ao lado dele (ex.: D:\...\PW_Server\*), para cima ou para baixo de versão.
    /// </summary>
    public static ProgramSwap? SwapFor(string clientElement, string preferred, out string? missing)
    {
        missing = null;
        int? client = ProgramVersion.ElementsVersion(Path.Combine(clientElement, "elementclient.exe"));
        int? installed = InstalledVersion();
        if (client is null || installed is null || client == installed) return null;
        var pkg = FindPackage(client.Value, preferred);
        if (pkg is null) { missing = string.Format(Loc.T("ls.noPackageFor", "Nenhuma pasta de servidor com o gs v{0} perto de {1}."), client, preferred); return null; }
        return new ProgramSwap { Package = pkg, From = installed, To = client };
    }

    static string? FindPackage(int version, string preferred) => PackagesWith(version, preferred).FirstOrDefault();

    /// <summary>
    /// Pastas "server files" cujo gs tem a versão pedida: <paramref name="preferred"/> primeiro, depois as pastas ao lado
    /// dela e até 3 níveis abaixo (pacotes vêm como &lt;pasta&gt;\&lt;pacote&gt;\server files\home).
    /// </summary>
    static IEnumerable<string> PackagesWith(int version, string preferred)
    {
        static bool Is(string dir, int v) => File.Exists(Path.Combine(dir, "home", "gamed", "gs")) && ProgramVersion.ElementsVersion(Path.Combine(dir, "home", "gamed", "gs")) == v;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(preferred) && Is(preferred, version) && seen.Add(Path.GetFullPath(preferred))) yield return preferred;
        var roots = new[] { Path.GetDirectoryName(preferred), Path.GetDirectoryName(Path.GetDirectoryName(preferred) ?? "") }.Where(r => r is not null && Directory.Exists(r)).Distinct();
        foreach (var root in roots)
        {
            var level = new List<string> { root! };
            for (int depth = 0; depth < 4 && level.Count > 0; depth++)
            {
                var next = new List<string>();
                foreach (var dir in level)
                {
                    string[] subs;
                    try { subs = Directory.GetDirectories(dir); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
                    foreach (var d in subs)
                    {
                        if (Path.GetFileName(d).Equals("home", StringComparison.OrdinalIgnoreCase)) continue;   // dentro de um pacote
                        if (Is(d, version) && seen.Add(Path.GetFullPath(d))) yield return d;
                        next.Add(d);
                    }
                }
                level = next.Count > 400 ? next.Take(400).ToList() : next;
            }
        }
    }

    /// <summary>
    /// Troca os programas do servidor local pelos do pacote: para, (se pedido) guarda a home/ em /home.bak-&lt;data&gt;,
    /// roda o provision.sh e (se pedido) recomeça o banco do jogo (as contas de login, no MariaDB, ficam).
    /// Não inicia: quem chama envia os dados e inicia.
    /// </summary>
    public void InstallPrograms(ProgramSwap swap)
    {
        Stop();
        if (swap.Backup)
        {
            Log("##PASSO " + Loc.T("ls.backupHome", "Guardando a instalação atual (/home.bak-<data>)"));
            var bk = Bash("b=/home.bak-$(date +%Y%m%d-%H%M%S) && mkdir -p $b && cd /home && for d in *; do [ $d = logs ] || cp -a $d $b/ || exit 1; done && echo $b", timeoutMs: 1_800_000);
            if (bk.Code != 0) throw new InvalidOperationException(Loc.T("ls.backupFailed", "Não consegui guardar a instalação atual; nada foi trocado.") + "\n" + bk.Output);
            Log(string.Format(Loc.T("ls.backupAt", "backup em {0}"), bk.Output.Trim().Split('\n').Last()));
        }
        Provision(swap.Package, swap.Package);   // o banco de contas já existe: a pasta SQL não é usada
        if (swap.FreshDatabase)
        {
            Log("##PASSO " + Loc.T("ls.freshDb", "Recomeçando o banco do jogo (personagens e nomes)"));
            Bash("for d in /home/uniquenamed/uname /home/gamedbd/dbhome /home/gamedbd/dbhomewdb; do [ -d $d ] && rm -rf $d/dbdata/* $d/dblogs/* $d/__db.* ; done; true", Log, timeoutMs: 300_000);
        }
    }

    async Task<string> DownloadRootfsAsync(IProgress<(string, double?)>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(BaseDir, "download");
        Directory.CreateDirectory(dir);
        var tar = Path.Combine(dir, "ubuntu-jammy-wsl-amd64-wsl.rootfs.tar.gz");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MyAceEditor");
        var sums = await http.GetStringAsync(SumsUrl, ct);
        var expected = sums.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.EndsWith(Path.GetFileName(tar)))?.Split(' ')[0];
        if (expected is null) throw new InvalidOperationException(Loc.T("ls.noSha", "Não achei o SHA-256 da imagem do Ubuntu."));
        if (File.Exists(tar) && Sha256(tar) == expected) return tar;

        progress?.Report((Loc.T("ls.downloading", "Baixando o Ubuntu 22.04 oficial (~234 MB)"), 0));
        var part = tar + ".part";
        using (var resp = await http.GetAsync(RootfsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0, got = 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(part);
            var buf = new byte[1 << 20]; int n; long last = 0;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct); got += n;
                if (total > 0 && got - last > 4 << 20) { last = got; progress?.Report((string.Format(Loc.T("ls.downloadingMb", "Baixando o Ubuntu 22.04 oficial ({0} de {1} MB)"), got >> 20, total >> 20), (double)got / total)); }
            }
        }
        if (Sha256(part) != expected) { File.Delete(part); throw new InvalidOperationException(Loc.T("ls.shaMismatch", "O arquivo baixado não confere com o SHA-256 oficial; apaguei e nada foi instalado.")); }
        File.Move(part, tar, true);
        return tar;
    }

    static string Sha256(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }

    /// <summary>C:\x\y → /mnt/c/x/y</summary>
    public static string ToWslPath(string win)
    {
        var full = Path.GetFullPath(win);
        return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
    }

    /// <summary>Senha do banco: gerada uma vez e guardada na pasta do servidor local (só letras e números).</summary>
    string DbPassword()
    {
        var f = Path.Combine(BaseDir, "db-password.txt");
        if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: >= 12 } saved) return saved;
        const string abc = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var pass = new string(RandomNumberGenerator.GetItems<char>(abc, 20));
        File.WriteAllText(f, pass);
        return pass;
    }

    // ---------- uso ----------
    /// <summary>
    /// Leva SEMPRE para o servidor os arquivos que têm de ser iguais aos do cliente, sem comparar nada (comparar já
    /// deixou mudança de HP de fora): elements.data e tasks.data do cliente, gshopsev/gshopsev1 refeitos do gshop do
    /// cliente (mesmo timestamp) e o aipolicy.data (o aberto na aba AI Policy, senão o mais novo entre cliente e servidor).
    /// Montados numa pasta de trabalho no Windows e copiados para /home/gamed/config.
    /// </summary>
    public void FixPair(string clientElement, string? aiPolicy = null, string? serverConfig = null)
    {
        var stage = Path.Combine(BaseDir, "envio");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        var data = Path.Combine(clientElement, "data");
        foreach (var f in new[] { "elements.data", "tasks.data" })
            if (File.Exists(Path.Combine(data, f))) { File.Copy(Path.Combine(data, f), Path.Combine(stage, f)); Log(string.Format(Loc.T("ls.sendingClient", "enviando {0} do cliente"), f)); }
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
            if (File.Exists(Path.Combine(data, cn)))
                try
                {
                    var shop = GShopFile.ReadClient(File.ReadAllBytes(Path.Combine(data, cn)));
                    // item sem preço derruba o gs: fica o gshopsev que o servidor já tem (o login acusa versão até corrigir)
                    if (shop.ServerProblems() is { Count: > 0 } bad) { Log("##ERRO " + string.Format(Loc.T("ls.shopNotSent", "{0} NÃO enviado, o gs não subiria: "), cn) + string.Join("; ", bad)); continue; }
                    File.WriteAllBytes(Path.Combine(stage, sn), shop.WriteServer()); Log(string.Format(Loc.T("ls.sendingRebuilt", "enviando {0} (refeito do {1} do cliente)"), sn, cn));
                }
                catch (Exception ex) { Log(string.Format(Loc.T("ls.rebuildFailed", "não consegui refazer {0}: {1}"), sn, ex.Message)); }
        // IA: a aberta no editor; senão a mais nova entre cliente e servidor original (o servidor é quem usa)
        var ai = new[] { aiPolicy, Path.Combine(data, "aipolicy.data"), serverConfig is null ? null : Path.Combine(serverConfig, "aipolicy.data") }
            .Where(p => p is not null && File.Exists(p)).Select(p => p!).ToList();
        var pick = aiPolicy is not null && File.Exists(aiPolicy) ? aiPolicy : ai.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (pick is not null) { File.Copy(pick, Path.Combine(stage, "aipolicy.data")); Log(string.Format(Loc.T("ls.sendingAi", "enviando aipolicy.data de {0}"), pick)); }
        Bash($"cp -f '{ToWslPath(stage)}'/*.data /home/gamed/config/ && chmod a+rw /home/gamed/config/*.data", Log, timeoutMs: 300_000);
    }

    /// <summary>
    /// "Salvar e enviar ao servidor": para, copia a pasta gamed\config INTEIRA do servidor original (npcgen, mapas,
    /// regiões…), põe por cima os arquivos que vêm do cliente (FixPair) e inicia. Nada é pulado por data ou tamanho.
    /// </summary>
    public RunResult SendAll(string serverFiles, string clientElement, string? aiPolicy = null, ProgramSwap? swap = null)
    {
        Stop();
        // programas de outra versão que a do cliente: o glinkd recusaria o login ("versão baixa"); troca antes dos dados
        if (swap is not null) { InstallPrograms(swap); serverFiles = swap.Package; }
        var cfg = Path.Combine(serverFiles, "home", "gamed", "config");
        Bash($"cp -a '{ToWslPath(cfg)}/.' /home/gamed/config/ && chmod -R a+rwX /home/gamed/config", Log, timeoutMs: 1_800_000);
        FixPair(clientElement, aiPolicy, cfg);
        var r = Start();
        FixRejectedAiPolicy(serverFiles);
        return r;
    }

    /// <summary>
    /// O gs não inicia com aipolicy.data de versão mais nova que a dele ("Failed to Load AIPolicy": ex.: o do PWI 1.6,
    /// gatilhos v33, num gs 1.5.5 que lê até v23). Converte o enviado com os limites de um aipolicy.data que um gs da
    /// mesma versão lê (de outro pacote no disco) e reinicia o gs; os arquivos do usuário não mudam.
    /// </summary>
    void FixRejectedAiPolicy(string serverFiles)
    {
        var sent = Path.Combine(BaseDir, "envio", "aipolicy.data");
        if (!File.Exists(sent) || !GsRejectedAiPolicy()) return;
        Log("##PASSO " + Loc.T("ls.aiRejected", "O gs recusou o aipolicy.data (versão mais nova que a dele): convertendo"));
        int have;
        try { var f = new AiPolicyFile(sent); if (!f.Ok) return; have = f.Limits().TriggerVersion; } catch { return; }
        var refs = new List<(string Path, AiPolicyFile.AiLimits Limits)>();
        if (ProgramVersion.ElementsVersion(Path.Combine(serverFiles, "home", "gamed", "gs")) is int gsVer)
            foreach (var pkg in PackagesWith(gsVer, serverFiles))
            {
                var p = Path.Combine(pkg, "home", "gamed", "config", "aipolicy.data");
                try { if (File.Exists(p) && new AiPolicyFile(p) is { Ok: true } rf && rf.Limits() is { } lim && lim.TriggerVersion < have) refs.Add((p, lim)); } catch { }
            }
        foreach (var (refPath, lim) in refs.OrderByDescending(x => x.Limits.TriggerVersion))
        {
            var ai = new AiPolicyFile(sent);
            var rep = ai.ConvertTo(lim);
            var conv = Path.Combine(BaseDir, "envio", "aipolicy.convertido.data");
            File.WriteAllBytes(conv, ai.Build());
            Log(string.Format(Loc.T("ls.aiConverted", "aipolicy.data convertido para gatilhos v{0} (como o de {1}): {2:N0} políticas; saíram {3} gatilhos e {4} operações que este gs não conhece"),
                lim.TriggerVersion, refPath, ai.Policies.Count, rep.TriggersDropped, rep.OpsDropped));
            Bash($"cp -f '{ToWslPath(conv)}' /home/gamed/config/aipolicy.data && chmod a+rw /home/gamed/config/aipolicy.data; pkill -9 -x gs; true", Log, timeoutMs: 120_000);
            Start();
            if (!GsRejectedAiPolicy()) { Log(Loc.T("ls.aiAccepted", "O gs aceitou o aipolicy.data convertido.")); return; }
        }
        Log("##ERRO " + Loc.T("ls.aiNoReference", "O gs não lê este aipolicy.data e não achei um que ele leia para servir de referência (outro pacote com o gs da mesma versão). Abra na aba AI Policy um aipolicy.data da versão do servidor e envie de novo."));
    }

    /// <summary>Espera o gs terminar de carregar (até ~2 min): true se ele recusou o aipolicy.data.</summary>
    bool GsRejectedAiPolicy()
    {
        for (int i = 0; i < 24; i++)
        {
            var o = Bash("grep -a -c 'Failed to Load AIPolicy' /home/logs/gs-gs01.log; grep -a -c announceproviderid /home/logs/gs-gs01.log", timeoutMs: 60_000).Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (o.Length >= 2 && int.TryParse(o[0], out var failed) && int.TryParse(o[1], out var ready))
            {
                if (failed > 0) return true;
                if (ready > 0) return false;   // o gs já falou com os glinkd: carregou tudo
            }
            Thread.Sleep(5000);
        }
        return false;
    }

    /// <summary>null = ok; senão, o erro.</summary>
    public string? Account(string login, string password, bool gm)
    {
        var r = Bash($"pwctl account '{login}' '{password}'{(gm ? " gm" : "")}", Log, timeoutMs: 120_000);
        return r.Output.Contains("##ERRO") || r.Code != 0 ? Loc.T("ls.accountFailed", "Não consegui criar a conta:") + "\n" + r.Output : null;
    }

    /// <summary>
    /// Leva ao Linux o pwctl desta versão do programa quando o instalado é outro (comandos novos do painel de
    /// administração chegam sem reinstalar).
    /// </summary>
    public void EnsureTools()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Server", "pwctl.sh")).Replace("\r\n", "\n").TrimStart('﻿');
        var want = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var have = Bash("md5sum /usr/local/bin/pwctl 2>/dev/null", timeoutMs: 60_000).Output.Trim().Split(' ')[0];
        if (have == want) return;
        Bash("cat > /usr/local/bin/pwctl && chmod +x /usr/local/bin/pwctl", stdin: text, timeoutMs: 60_000);
        Log(Loc.T("ls.pwctlUpdated", "pwctl atualizado no Linux"));
    }

    public RunResult Start(IEnumerable<string>? maps = null)
    {
        EnsureTools();
        KeepAlive();
        return Bash("pwctl start " + string.Join(' ', maps ?? new[] { "gs01" }), Log, timeoutMs: 600_000);
    }

    public RunResult Stop()
    {
        var r = Bash("pwctl stop", Log, timeoutMs: 120_000);
        Bash("pkill -f 'sleep 214748364[7]'", timeoutMs: 60_000);   // solta o Linux (o WSL desliga sozinho depois)
        return r;
    }

    /// <summary>
    /// O WSL desliga a distro quando nenhum wsl.exe está ligado a ela, e os serviços morreriam. Um "sleep" infinito, num
    /// wsl.exe sem janela e independente do editor (fica mesmo se o programa fechar), segura o Linux enquanto o servidor roda.
    /// </summary>
    void KeepAlive()
    {
        // [7]: o padrão não casa com a própria linha de comando do pgrep
        if (Bash("pgrep -f 'sleep 214748364[7]' >/dev/null && echo SIM", timeoutMs: 60_000).Output.Contains("SIM")) return;
        Process.Start(new ProcessStartInfo("wsl.exe", $"-d {Distro} -u root --exec sleep 2147483647") { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>Estado de cada serviço (nome → ligado).</summary>
    public Dictionary<string, bool> Status()
    {
        var r = Bash("pwctl status", timeoutMs: 60_000);
        var d = new Dictionary<string, bool>();
        foreach (var l in r.Output.Split('\n'))
        {
            var p = l.Trim().Split(' ');
            if (p.Length == 2) d[p[0]] = p[1] == "on";
        }
        return d;
    }

    public string ServiceLog(string name) => Bash($"pwctl log {name}", timeoutMs: 60_000).Output;

    /// <summary>
    /// Página de anúncios do login (configs\loginpage.ini no configs.pck): o cliente baixa a URL e o leitor de páginas
    /// de 2009 quebra o jogo com páginas modernas (pwonline.ru devolve uma "verificação de segurança" com JavaScript;
    /// crash "Exception occurred in ParsePage thread" ao entrar no mundo). A URL vira http://127.0.0.1:1/ (recusa na
    /// hora), mesmo tamanho, e o cache BBSPages\temp*.htm baixado volta para a página local temp0.htm. Backup de tudo.
    /// </summary>
    public void FixClientPages(string clientElement) => FixClientPages(clientElement, Log);

    public static void FixClientPages(string clientElement, Action<string> Log)
    {
        var pck = Path.Combine(clientElement, "configs.pck");
        if (File.Exists(pck))
        {
            byte[]? ini;
            using (var a = new PckArchive(pck)) ini = a.Find(@"configs\loginpage.ini") is { } e ? a.Read(e) : null;
            if (ini is not null)
            {
                var text = Encoding.Latin1.GetString(ini);
                var m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^LoginPage\s*=\s*(https?://\S+)");
                const string local = "http://127.0.0.1:1/";
                if (m.Success && !m.Groups[1].Value.StartsWith(local) && m.Groups[1].Value.Length >= local.Length)
                {
                    var g = m.Groups[1];
                    var fixedText = text[..g.Index] + local.PadRight(g.Length) + text[(g.Index + g.Length)..];
                    PckPatch.ReplaceSameSize(pck, @"configs\loginpage.ini", Encoding.Latin1.GetBytes(fixedText));
                    Log(string.Format(Loc.T("ls.adsPage", "página de anúncios do login: {0} → {1} (configs.pck, backup ao lado)"), g.Value, local));
                }
            }
        }
        var bbs = Path.Combine(clientElement, "BBSPages");
        var safe = Path.Combine(bbs, "temp0.htm");
        if (File.Exists(safe))
            foreach (var f in Directory.GetFiles(bbs, "temp*.htm").Where(f => !f.Equals(safe, StringComparison.OrdinalIgnoreCase)))
            {
                if (File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(safe))) continue;
                AtomicFileSave.Write(f, File.ReadAllBytes(safe));
                Log(Loc.T("ls.pageCache", "cache de página trocado pela página local: ") + f);
            }
    }

    /// <summary>Entrada "Local" (127.0.0.1:29000) nas listas de servidores do cliente (UTF-16, com backup).</summary>
    public void AddToServerList(string clientElement)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(clientElement).TrimEnd('\\'))!;
        foreach (var f in new[] { Path.Combine(clientElement, "userdata", "server", "serverlist.txt"), Path.Combine(root, "patcher", "server", "serverlist.txt") })
        {
            if (!File.Exists(f)) continue;
            var text = File.ReadAllText(f, Encoding.Unicode);
            if (text.Contains("29000:127.0.0.1")) continue;
            var nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var add = "Servidor local" + nl + "Local\t\t29000:127.0.0.1" + nl;
            AtomicFileSave.Write(f, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(add + text.TrimStart('\uFEFF'))).ToArray());
            Log(Loc.T("ls.serverList", "lista de servidores: ") + f);
        }
    }
}
