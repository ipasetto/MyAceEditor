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
                return "O WSL está instalado, mas a virtualização ainda não está ativa. Reinicie o computador (a \"Plataforma de Máquina Virtual\" só vale depois de reiniciar).";
            if (t.Contains("não está instalado") || t.Contains("is not installed"))
                return "O WSL não está instalado. Rode \"wsl --install --no-distribution\" como administrador e reinicie.";
            return null;
        }
        catch (Exception ex) { return "Não consegui rodar o wsl.exe: " + ex.Message; }
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
            progress?.Report(("Criando o Linux do servidor (" + Distro + ")", null));
            var vm = Path.Combine(BaseDir, "vm");
            Directory.CreateDirectory(vm);
            var r = Wsl($"--import {Distro} \"{vm}\" \"{tar}\" --version 2", line: Log);
            if (r.Code != 0) throw new InvalidOperationException("wsl --import falhou:\n" + r.Output);
        }

        // scripts do programa → /root/pw na distro
        progress?.Report(("Preparando a instalação", null));
        var here = Path.Combine(AppContext.BaseDirectory, "Server");
        foreach (var f in new[] { "provision.sh", "pwctl.sh" })
        {
            var text = File.ReadAllText(Path.Combine(here, f));
            var w = Bash($"mkdir -p /root/pw && cat > /root/pw/{f} && chmod +x /root/pw/{f}", stdin: text, timeoutMs: 120_000);
            if (w.Code != 0) throw new InvalidOperationException("Não consegui copiar " + f + " para o Linux:\n" + w.Output);
        }

        var pass = DbPassword();
        string? error = null;
        var res = Bash($"/root/pw/provision.sh '{ToWslPath(o.ServerFiles)}' '{ToWslPath(o.SqlDir)}' '{pass}'", line =>
        {
            Log(line);
            if (line.StartsWith("##PASSO ")) progress?.Report((line[8..], null));
            else if (line.StartsWith("##ERRO ")) error = line[7..];
        });
        if (res.Code != 0 || error is not null) throw new InvalidOperationException("A instalação parou: " + (error ?? "código " + res.Code));

        // par cliente × servidor na CÓPIA do servidor (elements, tasks e loja iguais ao cliente; senão o login é recusado)
        progress?.Report(("Igualando o servidor ao seu cliente (elements, tasks, loja)", null));
        FixPair(o.ClientElement, null, Path.Combine(o.ServerFiles, "home", "gamed", "config"));

        progress?.Report(("Criando a sua conta", null));
        var acc = Account(o.Login, o.Password, o.Gm);
        if (acc is not null) throw new InvalidOperationException(acc);

        progress?.Report(("Colocando \"Local\" na lista de servidores do cliente", null));
        AddToServerList(o.ClientElement);
        progress?.Report(("Desligando a página de anúncios da internet que derruba o cliente", null));
        FixClientPages(o.ClientElement);
        progress?.Report(("Pronto", 1));
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
        if (expected is null) throw new InvalidOperationException("Não achei o SHA-256 da imagem do Ubuntu.");
        if (File.Exists(tar) && Sha256(tar) == expected) return tar;

        progress?.Report(("Baixando o Ubuntu 22.04 oficial (~234 MB)", 0));
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
                if (total > 0 && got - last > 4 << 20) { last = got; progress?.Report(($"Baixando o Ubuntu 22.04 oficial ({got >> 20} de {total >> 20} MB)", (double)got / total)); }
            }
        }
        if (Sha256(part) != expected) { File.Delete(part); throw new InvalidOperationException("O arquivo baixado não confere com o SHA-256 oficial; apaguei e nada foi instalado."); }
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
            if (File.Exists(Path.Combine(data, f))) { File.Copy(Path.Combine(data, f), Path.Combine(stage, f)); Log("enviando " + f + " do cliente"); }
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
            if (File.Exists(Path.Combine(data, cn)))
                try { File.WriteAllBytes(Path.Combine(stage, sn), GShopFile.ReadClient(File.ReadAllBytes(Path.Combine(data, cn))).WriteServer()); Log($"enviando {sn} (refeito do {cn} do cliente)"); }
                catch (Exception ex) { Log($"não consegui refazer {sn}: {ex.Message}"); }
        // IA: a aberta no editor; senão a mais nova entre cliente e servidor original (o servidor é quem usa)
        var ai = new[] { aiPolicy, Path.Combine(data, "aipolicy.data"), serverConfig is null ? null : Path.Combine(serverConfig, "aipolicy.data") }
            .Where(p => p is not null && File.Exists(p)).Select(p => p!).ToList();
        var pick = aiPolicy is not null && File.Exists(aiPolicy) ? aiPolicy : ai.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (pick is not null) { File.Copy(pick, Path.Combine(stage, "aipolicy.data")); Log("enviando aipolicy.data de " + pick); }
        Bash($"cp -f '{ToWslPath(stage)}'/*.data /home/gamed/config/ && chmod a+rw /home/gamed/config/*.data", Log, timeoutMs: 300_000);
    }

    /// <summary>
    /// "Salvar e enviar ao servidor": para, copia a pasta gamed\config INTEIRA do servidor original (npcgen, mapas,
    /// regiões…), põe por cima os arquivos que vêm do cliente (FixPair) e inicia. Nada é pulado por data ou tamanho.
    /// </summary>
    public RunResult SendAll(string serverFiles, string clientElement, string? aiPolicy = null)
    {
        Stop();
        var cfg = Path.Combine(serverFiles, "home", "gamed", "config");
        Bash($"cp -a '{ToWslPath(cfg)}/.' /home/gamed/config/ && chmod -R a+rwX /home/gamed/config", Log, timeoutMs: 1_800_000);
        FixPair(clientElement, aiPolicy, cfg);
        return Start();
    }

    /// <summary>null = ok; senão, o erro.</summary>
    public string? Account(string login, string password, bool gm)
    {
        var r = Bash($"pwctl account '{login}' '{password}'{(gm ? " gm" : "")}", Log, timeoutMs: 120_000);
        return r.Output.Contains("##ERRO") || r.Code != 0 ? "Não consegui criar a conta:\n" + r.Output : null;
    }

    public RunResult Start(IEnumerable<string>? maps = null)
    {
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
                    Log($"página de anúncios do login: {g.Value} → {local} (configs.pck, backup ao lado)");
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
                Log("cache de página trocado pela página local: " + f);
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
            Log("lista de servidores: " + f);
        }
    }
}
