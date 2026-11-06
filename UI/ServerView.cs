using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Aba "Servidor": servidor local no WSL sem passos manuais (LocalServer). Os caminhos vêm da Verificação; o usuário só
/// escolhe login e senha da conta. Botões: Instalar/completar, Iniciar, Parar, Enviar meus arquivos e reiniciar, Criar
/// conta. Estado de cada serviço com luz verde/vermelha e log ao vivo.
/// </summary>
public sealed class ServerView : UserControl
{

    readonly Func<string> _client, _serverHome;
    readonly Func<string, string?> _load;
    readonly Action<string, string> _save;
    readonly TextBox _pkg = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _base = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Font = new Font("Consolas", 9.5f), WordWrap = true, BorderStyle = BorderStyle.None };
    readonly Label _step = new() { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f), ForeColor = UiKit.Muted, Padding = new Padding(4, 0, 0, 0) };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 6, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly FlowLayoutPanel _lights = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
    readonly StatusCard _card = new();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 15000 };
    readonly List<ToolStripItem> _buttons = new();
    ToolStripButton? _startBtn, _stopBtn, _sendBtn, _setupBtn;
    bool _busy, _online, _anyUp, _installed;
    string? _filledFor;   // cliente (pasta element) para o qual as pastas foram preenchidas
    readonly Color _accent;
    /// <summary>aipolicy.data aberto na aba AI Policy (é o que vai para o servidor ao enviar).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? AiPolicyPath { get; set; }

    /// <summary>Botão Verificar e corrigir: a janela principal abre a Verificação nesta mesma tela.</summary>
    public event Action? VerifyRequested;
    public event Action? PluginsRequested;

    public ServerView(Color bg, Color fg, Color accent, Func<string> client, Func<string> serverHome, Func<string, string?> load, Action<string, string> save)
    {
        _client = client; _serverHome = serverHome; _load = load; _save = save;
        BackColor = bg; ForeColor = fg; Dock = DockStyle.Fill; Padding = new Padding(0); _accent = accent;

        // barra de comandos: o que se faz no dia a dia primeiro, instalar e pastas no fim
        var rb = UiKit.Ribbon();
        _startBtn = Cmd(rb, UiKit.Play, "srv.rStart", "Iniciar", () => Run(Loc.T("srv.starting", "Iniciando…"), s => s.Start()), "srv.rStartTip", "Liga o servidor local (todos os serviços e os mapas padrão).", primary: true);
        _stopBtn = Cmd(rb, UiKit.Stop, "srv.rStop", "Parar", () => Run(Loc.T("srv.stopping", "Parando…"), s => s.Stop()), "srv.rStopTip", "Desliga o servidor local.");
        _sendBtn = Cmd(rb, UiKit.Upload, "srv.rSend", "Enviar e reiniciar", SendFiles, "srv.rSendTip", "Leva para o servidor o que você mudou no editor (itens, quests, loja, IA) e reinicia.");
        Cmd(rb, UiKit.Refresh, "srv.rRefresh", "Atualizar", RefreshStatus, "srv.rRefreshTip", "Confere agora se o servidor está no ar.");
        UiKit.Sep(rb);
        Cmd(rb, UiKit.People, "srv.rAdmin", "Administrar", OpenAdmin, "srv.rAdminTip", "Contas, personagens, quem está no jogo, mapas e instâncias.");
        Cmd(rb, UiKit.Play, "srv.rPlugins", "Plugins", () => PluginsRequested?.Invoke(), "srv.rPluginsTip", "Eventos automáticos no servidor: monstro raro por sorteio, chamar pelo GM.");
        Cmd(rb, UiKit.Game, "srv.rGm", "Painel do GM", OpenGmPanel, "srv.rGmTip", "Comandos de GM no jogo aberto (programa à parte).");
        UiKit.Sep(rb);
        Cmd(rb, UiKit.Download, "srv.rInstall", "Instalar", Install, "srv.rInstallTip", "Instala ou completa o servidor local no Linux do Windows (WSL). Só precisa uma vez.");
        Cmd(rb, UiKit.CheckList, "srv.rVerify", "Verificar e corrigir", () => VerifyRequested?.Invoke(), "srv.rVerifyTip", "Confere se cliente e servidor combinam e mostra o que corrigir");
        _setupBtn = Cmd(rb, UiKit.Settings, "srv.rSetup", "Pastas", () => Configure(), "srv.rSetupTip", "Servidor e pastas: pacote, pasta, disco e memória do servidor desta versão.");


        // registro (log) com título e "limpar"
        var logHead = new Panel { Dock = DockStyle.Top, Height = 28 };
        var logTitle = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = UiKit.Fg, Image = UiKit.Icon(UiKit.Log, UiKit.Muted, 16), ImageAlign = ContentAlignment.MiddleLeft };
        Bind(() => logTitle.Text = "      " + Loc.T("srv.logTitle", "Registro do que o servidor fez"));
        var clear = new LinkLabel { Dock = DockStyle.Right, Width = 80, TextAlign = ContentAlignment.MiddleRight };
        Bind(() => clear.Text = Loc.T("srv.logClear", "Limpar"));
        clear.LinkClicked += (_, _) => _log.Clear();
        logHead.Controls.Add(logTitle); logHead.Controls.Add(clear);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 10) };
        var logBox = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        logBox.Controls.Add(_log);
        body.Controls.Add(logBox); body.Controls.Add(logHead); body.Controls.Add(_bar); body.Controls.Add(_step); body.Controls.Add(_lights);
        Controls.Add(body); Controls.Add(rb); Controls.Add(_card);
        _log.BackColor = Color.FromArgb(Math.Max(0, bg.R - 12), Math.Max(0, bg.G - 12), Math.Max(0, bg.B - 12)); _log.ForeColor = fg;
        _card.Set(StatusCard.Kind.Busy, Loc.T("srv.checking", "Consultando…"));

        // o estado se mantém sozinho enquanto a aba está aberta
        _poll.Tick += (_, _) => { if (!_busy && Visible) RefreshStatus(quiet: true); };
        VisibleChanged += (_, _) =>
        {
            _poll.Enabled = Visible;
            if (!Visible) return;
            Fill();
            RefreshStatus(quiet: true);
        };
        Loc.Changed += ApplyTexts;
        _base.TextChanged += (_, _) => LocalServer.Current = new LocalServer(_base.Text.Trim());
        ServerPaths.Changed += c => { if (string.Equals(c, _filledFor, StringComparison.OrdinalIgnoreCase)) { _filledFor = null; Fill(); if (Visible) RefreshStatus(quiet: true); } };
    }

    ToolStripButton Cmd(ToolStrip bar, string glyph, string key, string pt, Action click, string tipKey, string tipPt, bool primary = false)
    {
        var b = UiKit.Cmd(bar, glyph, key, pt, () => { if (!_busy) click(); }, tipKey, tipPt, primary);
        _buttons.Add(b);
        return b;
    }

    // textos fixos da tela: refeitos quando o idioma muda
    readonly List<Action> _texts = new();
    void Bind(Action apply) { apply(); _texts.Add(apply); }
    void ApplyTexts() { if (IsHandleCreated) BeginInvoke(() => _texts.ForEach(a => a())); else _texts.ForEach(a => a()); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Loc.Changed -= ApplyTexts; _poll.Dispose(); }
        base.Dispose(disposing);
    }

    void Pick(TextBox box)
    {
        using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "" };
        if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
    }

    /// <summary>Preenche as pastas para o cliente aberto (outro cliente = outras pastas: as de cada versão).</summary>
    void Fill()
    {
        var client = _client();
        if (!string.Equals(client, _filledFor, StringComparison.OrdinalIgnoreCase)) { _pkg.Text = ""; _base.Text = ""; _filledFor = client; }
        if (_pkg.Text.Length > 0 && _base.Text.Length > 0) return;
        var e = ServerPaths.Get(client);   // as pastas de cada cliente ficam na janela "Servidor e pastas"
        if (_pkg.Text.Length == 0) _pkg.Text = e.Package;
        if (_base.Text.Length == 0) _base.Text = e.Base;
    }

    /// <summary>
    /// O editor abriu outro cliente: as pastas do servidor passam para as dessa versão. Devolve a home do pacote (para a
    /// Verificação comparar com o mesmo servidor), ou null.
    /// </summary>
    public string? GameFolderChanged()
    {
        Fill();
        if (Visible) RefreshStatus(quiet: true);
        var home = Path.Combine(_pkg.Text.Trim(), "home");
        return _pkg.Text.Length > 0 && Directory.Exists(home) ? home : null;
    }

    LocalServer NewServer()
    {
        var s = new LocalServer(_base.Text.Trim());
        s.Log = line => { if (IsHandleCreated) BeginInvoke(() => AppendLog(line)); };
        return s;
    }

    /// <summary>Servidor local com as pastas desta aba (para o Corrigir da Verificação); o andamento vai para o log daqui.</summary>
    public LocalServer CreateServer() { Fill(); return NewServer(); }

    /// <summary>Linha do log com cor: etapa em destaque, erro em vermelho, aviso em laranja, o resto na cor normal.</summary>
    void AppendLog(string line, Color? color = null)
    {
        if (line.StartsWith("##PCT "))   // andamento do envio: barra cheia até n% e a etapa ao lado
        {
            var parts = line[6..].Split(' ', 2);
            if (int.TryParse(parts[0], out int n))
            {
                _bar.Style = ProgressBarStyle.Continuous; _bar.Value = Math.Clamp(n, 0, 100);
                _step.Text = $"{n}% · " + (parts.Length > 1 ? parts[1] : "");
            }
            return;
        }
        Color c = color ?? (line.StartsWith("##ERRO") || line.StartsWith(Loc.T("srv.errorLog", "ERRO: ")) ? Color.IndianRed
            : line.StartsWith("##AVISO") || line.StartsWith("⚠") ? Color.DarkOrange
            : line.StartsWith("##PASSO ") ? _accent : _log.ForeColor);
        if (line.StartsWith("##PASSO ")) line = "▶ " + line[8..];
        _log.SelectionStart = _log.TextLength; _log.SelectionLength = 0;
        _log.SelectionColor = c;
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    void Busy(bool on, string text = "")
    {
        _busy = on; _bar.Visible = on; _step.Text = text;
        if (on) { _bar.Style = ProgressBarStyle.Marquee; _bar.Value = 0; }   // até a 1ª etapa com %
        foreach (var b in _buttons) b.Enabled = !on;
        if (on) _card.Set(StatusCard.Kind.Busy, text.Length > 0 ? text : Loc.T("srv.working", "Trabalhando…"), Loc.T("srv.workingDetail", "Acompanhe abaixo o que está sendo feito."));
        else UpdateButtons();
    }

    // só oferece o que faz sentido agora: Iniciar com o servidor parado, Parar com algo ligado
    void UpdateButtons()
    {
        if (_busy) return;
        if (_startBtn is not null) _startBtn.Enabled = _installed && !_online;
        if (_stopBtn is not null) _stopBtn.Enabled = _installed && _anyUp;
        if (_sendBtn is not null) _sendBtn.Enabled = _installed;
    }

    /// <summary>Pasta com o SQL do banco do pacote (null = não achei).</summary>
    static string? SqlDir(string pkg)
    {
        // pasta SQL: ao lado da "server files" (pacote andrey.pw) ou dentro dela
        // ou um dump único (.sql com CREATE TABLE): root/pwa.sql do 1.5.5, Sql/dump.sql do 1.5.5v156, pw..sql do 1.3.6
        var parent = Path.GetDirectoryName(pkg) ?? pkg;
        var dirs = new[] { Path.Combine(parent, "SQL"), Path.Combine(pkg, "SQL"), Path.Combine(pkg, "root"), parent };
        foreach (var c in dirs)
            if (File.Exists(Path.Combine(c, "PW_tabels.sql"))) return c;
        foreach (var c in dirs.Where(Directory.Exists))
            if (Directory.GetFiles(c, "*.sql").OrderBy(f => f).FirstOrDefault() is { } f && File.ReadLines(f).Take(4000).Any(l => l.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase))) return c;
        return null;
    }

    /// <summary>Servidor instalado com sucesso (o editor roda a Verificação e oferece o Corrigir).</summary>
    public event Action? Installed;

    /// <summary>Abre "Servidor e pastas" do cliente aberto. true = salvou.</summary>
    public bool Configure()
    {
        var client = _client();
        using var w = new ServerSetupWindow(client, BackColor, ForeColor);
        return w.ShowDialog(FindForm()) == DialogResult.OK;
    }

    void Install()
    {
        Fill();
        var client = _client();
        if (!Directory.Exists(Path.Combine(client, "data"))) { Warn(Loc.T("srv.setTheClientElementFolder", "Abra o cliente na aba Verificação (pasta element).")); return; }
        if (!EnsureWsl(FindForm() ?? (IWin32Window)this)) return;
        if (Options(client) is { } opt) StartInstall(opt);
    }

    /// <summary>
    /// Opções de instalação a partir de "Servidor e pastas"; faltando algo, abre a janela. null = não dá para instalar
    /// (cancelado, servidor em outro Linux, pacote sem SQL ou pasta com servidor de outra versão).
    /// </summary>
    LocalServer.InstallOptions? Options(string client)
    {
        var e = ServerPaths.Get(client);
        if ((e.Home.Length == 0 || !Path.IsPathRooted(e.Base) || !ServerPaths.Known(client)) && !Configure()) return null;
        e = ServerPaths.Get(client);
        _filledFor = null; Fill();
        if (e.Kind == ServerPaths.Kind.Other) { Warn(Loc.T("srv.otherNoInstall", "O servidor deste cliente está em outro Linux (\"Servidor e pastas\"): a instalação local não se aplica.")); return null; }
        if (e.Home.Length == 0) { Warn(Loc.T("srv.pickThePackageSServer", "Escolha a pasta \"server files\" do pacote (a que tem home, etc e lib).")); return null; }
        if (SqlDir(e.Package) is not { } sql) { Warn(Loc.T("srv.sqlFolderPwTabelsSql", "Não achei o SQL do banco ao lado do pacote (SQL/PW_tabels.sql, ou um dump .sql único em SQL, root ou ao lado do pacote).")); return null; }
        var probe = new LocalServer(e.Base);   // pasta com servidor de outra versão: nunca por cima
        if (probe.Exists() && probe.VersionMismatch(client) is { } other) { Warn(other); return null; }
        return new LocalServer.InstallOptions(e.Package, sql, client, "", "", false, e.DiskGb);   // conta: no Administrar, depois
    }

    /// <summary>
    /// O WSL roda o servidor local. Ausente: pergunta se instala (o Windows pede administrador; depois reiniciar o PC).
    /// true = WSL pronto para seguir agora.
    /// </summary>
    public static bool EnsureWsl(IWin32Window owner)
    {
        if (!LocalServer.WslMissing()) return true;
        var title = Loc.T("srv.wslTitle", "Linux do Windows (WSL)");
        if (MessageBox.Show(owner, Loc.T("srv.wslAsk", "O servidor local roda no Linux do Windows (WSL), que não está instalado neste computador.\n\nInstalar agora? O Windows vai pedir permissão de administrador e, no fim, é preciso reiniciar o computador."),
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        bool ok = LocalServer.InstallWsl();
        MessageBox.Show(owner, ok ? Loc.T("srv.wslInstalled", "WSL instalado. Reinicie o computador e depois clique em Instalar na aba Servidor.")
                                  : Loc.T("srv.wslFailed", "Não consegui instalar o WSL (permissão recusada ou erro). Tente de novo ou rode \"wsl --install --no-distribution\" como administrador."),
            title, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return false;
    }

    /// <summary>
    /// 1ª vez deste cliente (nada gravado e não recusado antes): pergunta se configura o servidor local e, se sim, abre
    /// "Servidor e pastas" e instala. true = o usuário quis configurar (o editor mostra a aba Servidor).
    /// </summary>
    public bool OfferSetup(IWin32Window owner)
    {
        var client = _client();
        if (client.Length == 0 || !Directory.Exists(Path.Combine(client, "data"))) return false;
        if (ServerPaths.Known(client) || ServerPaths.Declined(client)) return false;
        if (MessageBox.Show(owner, Loc.T("srv.offerSetup", "Esta versão do jogo ainda não tem servidor local.\n\nQuer configurar um agora? Você escolhe o pacote do servidor, a pasta, o disco e a memória; o programa instala tudo."),
                Loc.T("srv.offerTitle", "Servidor local"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            ServerPaths.Decline(client);   // não pergunta de novo para este cliente (botão Instalar continua lá)
            return false;
        }
        if (!Configure()) return true;
        if (ServerPaths.Get(client).Kind == ServerPaths.Kind.Local && EnsureWsl(owner) && Options(client) is { } opt) StartInstall(opt);
        return true;
    }

    void StartInstall(LocalServer.InstallOptions opt)
    {
        var s = NewServer();
        Busy(true, Loc.T("srv.installing", "Instalando…"));
        var prog = new Progress<(string Step, double? Fraction)>(p =>
        {
            _step.Text = p.Step;
            if (p.Fraction is double f) { _bar.Style = ProgressBarStyle.Continuous; _bar.Value = (int)Math.Clamp(f * 100, 0, 100); } else _bar.Style = ProgressBarStyle.Marquee;
            AppendLog("▶ " + p.Step);
        });
        Task.Run(async () =>
        {
            try { await s.InstallAsync(opt, prog, CancellationToken.None); BeginInvoke(() => { Busy(false, Loc.T("srv.installedClickStart", "Instalado. Clique em Iniciar.")); RefreshStatus(quiet: true); Installed?.Invoke(); }); }
            catch (Exception ex) { BeginInvoke(() => { Busy(false, Loc.T("srv.stopped", "Parou: ") + ex.Message); AppendLog(Loc.T("srv.errorLog", "ERRO: ") + ex.Message); }); }
        });
    }

    void Run(string text, Func<LocalServer, LocalServer.RunResult> act, Action<bool>? done = null)
    {
        var s = NewServer();
        Busy(true, text);
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p) { BeginInvoke(() => { Busy(false, p); AppendLog(p); done?.Invoke(false); }); return; }
            bool ok = false;
            try { ok = act(s).Code == 0; } catch (Exception ex) { BeginInvoke(() => AppendLog(Loc.T("srv.errorLog", "ERRO: ") + ex.Message)); }
            BeginInvoke(() => { Busy(false, ""); _announce = true; RefreshStatus(); done?.Invoke(ok); });
        });
    }

    /// <summary>Fim do envio ao servidor (true = enviado sem erro): o editor apaga o brilho do "Salvar e enviar".</summary>
    public event Action<bool>? FilesSent;

    bool _announce;

    /// <summary>Envia ao servidor local o que o editor gravou e reinicia (botão desta aba e "Salvar e enviar" da barra de cima).</summary>
    public void SendFiles()
    {
        if (_busy) return;
        Fill();
        var pkg = _pkg.Text.Trim(); var client = _client();
        if (!Directory.Exists(Path.Combine(pkg, "home"))) { Warn(Loc.T("srv.pickTheServerFilesFolder", "Escolha a pasta \"server files\" na aba Servidor.")); return; }
        var ai = AiPolicyPath?.Invoke();
        // config inteira do servidor + elements/tasks/loja/IA do cliente, comparados pelo conteúdo: só o que difere vai
        // (LocalServer.SendAll); servidor desta pasta de outra versão que o cliente: nada vai e o log diz o que fazer
        Run(Loc.T("srv.sendingChanged", "Enviando o que mudou e reiniciando…"), s => s.SendAll(pkg, client, ai), ok =>
        {
            FilesSent?.Invoke(ok);
            if (ok) MessageBox.Show(this, Loc.T("srv.sentDone", "Todos os arquivos subiram. Reinicie o cliente do jogo."), Loc.T("srv.sentTitle", "Envio concluído"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    /// <summary>
    /// Arquivos em que o servidor local está diferente do que o envio levaria (segundo plano: MD5 dos dois lados, alguns
    /// segundos). null = não deu para comparar (servidor não instalado, pasta não escolhida, WSL ausente).
    /// </summary>
    public Task<List<string>?> ServerDiffAsync()
    {
        Fill();
        var pkg = _pkg.Text.Trim(); var client = _client(); var ai = AiPolicyPath?.Invoke(); var baseDir = _base.Text.Trim();
        return Task.Run(() =>
        {
            try
            {
                if (_busy || !Directory.Exists(Path.Combine(pkg, "home")) || !Directory.Exists(baseDir) || LocalServer.WslProblem() is not null) return null;
                return new LocalServer(baseDir).PendingDiff(pkg, client, ai)?.Select(d => d.Rel).ToList();
            }
            catch (Exception ex) { AppLog.Error("conferir o servidor local", ex); return null; }
        });
    }

    void RefreshStatus() => RefreshStatus(quiet: false);

    void RefreshStatus(bool quiet)
    {
        if (!quiet) _step.Text = Loc.T("srv.checking", "Consultando…");
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p)
            {
                BeginInvoke(() => { _installed = false; _card.Set(StatusCard.Kind.Warn, Loc.T("srv.cardNoWsl", "Linux do Windows (WSL) indisponível"), p); UpdateButtons(); if (!quiet) AppendLog(p); });
                return;
            }
            Dictionary<string, bool> st;
            bool exists = false;
            try { var s = NewServer(); exists = s.Exists(); st = exists ? s.Status() : new(); } catch { st = new(); }
            BeginInvoke(() =>
            {
                if (_busy) return;
                _lights.Controls.Clear();
                _installed = exists && st.Count > 0;
                if (!_installed)
                {
                    _online = _anyUp = false;
                    _card.Set(StatusCard.Kind.Off, Loc.T("srv.cardNotInstalled", "Servidor local não instalado"), Loc.T("srv.cardNotInstalledDetail", "Escolha as pastas em \"Pastas\" e clique em Instalar (só uma vez)."));
                    _step.Text = "";
                    UpdateButtons();
                    return;
                }
                foreach (var (name, on) in st)
                    _lights.Controls.Add(new Label
                    {
                        AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 6, 0), BackColor = on ? Color.FromArgb(220, 252, 231) : Color.FromArgb(254, 226, 226),
                        Text = (on ? "● " : "○ ") + name, ForeColor = on ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28),
                    });
                _online = st.TryGetValue("porta29000", out var up) && up;
                _anyUp = st.Any(x => x.Value && x.Key != "mariadb");   // o banco sozinho ligado não é servidor subindo
                int n = st.Count(x => x.Value);
                var when = DateTime.Now.ToString("HH:mm:ss");
                if (_online) _card.Set(StatusCard.Kind.On, Loc.T("srv.cardOnline", "Servidor online"), string.Format(Loc.T("srv.cardOnlineDetail", "Entre no jogo pelo servidor \"Local\" · {0} de {1} serviços no ar · conferido às {2}"), n, st.Count, when));
                else if (_anyUp) _card.Set(StatusCard.Kind.Warn, Loc.T("srv.cardPartial", "Servidor subindo ou incompleto"), string.Format(Loc.T("srv.cardPartialDetail", "{0} de {1} serviços no ar; o login (porta 29000) ainda não · conferido às {2}"), n, st.Count, when));
                else _card.Set(StatusCard.Kind.Off, Loc.T("srv.cardOffline", "Servidor offline"), string.Format(Loc.T("srv.cardOfflineDetail", "Clique em Iniciar para ligar · conferido às {0}"), when));
                _step.Text = "";
                UpdateButtons();
                if (_announce)   // depois de iniciar/enviar: o resultado em uma linha, verde ou vermelha
                {
                    _announce = false;
                    AppendLog(_online ? Loc.T("srv.online", "● Servidor online") : Loc.T("srv.offline", "● Servidor offline"), _online ? Color.LimeGreen : Color.IndianRed);
                }
            });
        });
    }

    /// <summary>Painel de administração (contas, personagens, quem está no jogo, mundo, mapas).</summary>
    void OpenAdmin()
    {
        Fill();
        var pkg = _pkg.Text.Trim();
        if (!Directory.Exists(Path.Combine(pkg, "home"))) { Warn(Loc.T("srv.pickThePackageSServer2", "Escolha a pasta \"server files\" do pacote.")); return; }
        if (!NewServer().Exists()) { Warn(Loc.T("srv.installTheLocalServerFirst", "Instale o servidor local primeiro.")); return; }
        var w = new ServerAdminWindow(NewServer(), pkg, _client(), BackColor, ForeColor, _accent);
        w.Show(FindForm());
    }

    /// <summary>Painel do GM em outro processo (é um programa à parte: só usa o cliente do jogo, não o editor).</summary>
    void OpenGmPanel()
    {
        var client = _client();
        var server = LocalServer.Current.BaseDir.TrimEnd('\\');
        var args = (Directory.Exists(client) ? $"--gm \"{client.TrimEnd('\\')}\"" : "--gm \"\"") + (server.Length > 0 ? $" \"{server}\"" : "");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false }); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    void Warn(string msg) => MessageBox.Show(this, msg, Loc.T("srv.server", "Servidor"), MessageBoxButtons.OK, MessageBoxIcon.Information);
}
