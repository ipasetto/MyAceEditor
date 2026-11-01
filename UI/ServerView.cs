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
    readonly Panel _setup = new() { Dock = DockStyle.Top, Height = 118, Visible = false, Padding = new Padding(0, 6, 0, 6) };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 15000 };
    readonly List<ToolStripItem> _buttons = new();
    ToolStripButton? _startBtn, _stopBtn, _sendBtn, _setupBtn;
    bool _busy, _online, _anyUp, _installed;
    readonly Color _accent;
    /// <summary>aipolicy.data aberto na aba AI Policy (é o que vai para o servidor ao enviar).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? AiPolicyPath { get; set; }

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
        Cmd(rb, UiKit.Game, "srv.rGm", "Painel do GM", OpenGmPanel, "srv.rGmTip", "Comandos de GM no jogo aberto (programa à parte).");
        UiKit.Sep(rb);
        Cmd(rb, UiKit.Download, "srv.rInstall", "Instalar", Install, "srv.rInstallTip", "Instala ou completa o servidor local no Linux do Windows (WSL). Só precisa uma vez.");
        _setupBtn = Cmd(rb, UiKit.Settings, "srv.rSetup", "Pastas", () => { _setup.Visible = !_setup.Visible; _setupBtn!.Checked = _setup.Visible; }, "srv.rSetupTip", "Onde estão o pacote do servidor e a cópia local.");

        // pastas (recolhidas: só aparecem quando faltam ou quando o usuário pede)
        var intro = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = UiKit.Muted };
        Bind(() => intro.Text = Loc.T("srv.testServerOnYourPc", "Servidor de teste no seu PC (Linux do Windows, WSL). Clique em Instalar uma vez; depois é só Iniciar e entrar no jogo pelo servidor \"Local\". Seus arquivos originais do servidor não são alterados: o programa trabalha numa cópia."));
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 70, ColumnCount = 3, RowCount = 2 };
        for (int i = 0; i < 2; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        Label Lb(Func<string> t) { var l = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = fg }; Bind(() => l.Text = t()); return l; }
        Button Browse(TextBox box) { var b = new Button { AutoSize = true, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Image = UiKit.Icon(UiKit.Folder, UiKit.Fg, 16), TextImageRelation = TextImageRelation.ImageBeforeText }; Bind(() => b.Text = Loc.T("srv.browse", "Procurar…")); b.Click += (_, _) => { if (!_busy) Pick(box); }; return b; }
        form.Controls.Add(Lb(() => Loc.T("srv.serverPackageServerFiles", "Pacote do servidor (\"server files\")")), 0, 0); form.Controls.Add(_pkg, 1, 0); form.Controls.Add(Browse(_pkg), 2, 0);
        form.Controls.Add(Lb(() => Loc.T("srv.localServerFolder", "Pasta do servidor local")), 0, 1); form.Controls.Add(_base, 1, 1); form.Controls.Add(Browse(_base), 2, 1);
        _setup.Controls.Add(form); _setup.Controls.Add(intro);

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
        body.Controls.Add(logBox); body.Controls.Add(logHead); body.Controls.Add(_bar); body.Controls.Add(_step); body.Controls.Add(_lights); body.Controls.Add(_setup);
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
            _setup.Visible = !Directory.Exists(Path.Combine(_pkg.Text.Trim(), "home"));
            _setupBtn.Checked = _setup.Visible;
            RefreshStatus(quiet: true);
        };
        Loc.Changed += ApplyTexts;
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

    /// <summary>Preenche os caminhos a partir da Verificação (pasta home do servidor → "server files" ao redor dela).</summary>
    void Fill()
    {
        if (_pkg.Text.Length == 0)
        {
            var saved = _load("server_pkg");
            if (saved is not null && Directory.Exists(Path.Combine(saved, "home"))) _pkg.Text = saved;
            else if (_serverHome() is { Length: > 0 } h && Directory.Exists(h))
            {
                var home = VersionCheck.ServerHome(h);
                var parent = Path.GetDirectoryName(home.TrimEnd('\\'));
                if (parent is not null && Directory.Exists(Path.Combine(parent, "etc"))) _pkg.Text = parent;
            }
        }
        if (_base.Text.Length == 0)
            _base.Text = _load("server_base") ?? (_pkg.Text.Length > 0 ? Path.Combine(Path.GetPathRoot(_pkg.Text)!, "PWServer") : @"C:\PWServer");
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

    string? SqlDir()
    {
        // pasta SQL: ao lado da "server files" (pacote andrey.pw) ou dentro dela
        var pkg = _pkg.Text.Trim();
        foreach (var c in new[] { Path.Combine(Path.GetDirectoryName(pkg) ?? pkg, "SQL"), Path.Combine(pkg, "SQL") })
            if (File.Exists(Path.Combine(c, "PW_tabels.sql"))) return c;
        return null;
    }

    void Install()
    {
        var pkg = _pkg.Text.Trim();
        if (!Directory.Exists(Path.Combine(pkg, "home"))) { Warn(Loc.T("srv.pickThePackageSServer", "Escolha a pasta \"server files\" do pacote (a que tem home, etc e lib).")); return; }
        if (SqlDir() is not { } sql) { Warn(Loc.T("srv.sqlFolderPwTabelsSql", "Não achei a pasta SQL (PW_tabels.sql) ao lado do pacote.")); return; }
        var client = _client();
        if (!Directory.Exists(Path.Combine(client, "data"))) { Warn(Loc.T("srv.setTheClientElementFolder", "Abra o cliente na aba Verificação (pasta element).")); return; }
        _save("server_pkg", pkg); _save("server_base", _base.Text.Trim());
        var opt = new LocalServer.InstallOptions(pkg, sql, client, "", "", false);   // conta: no Administrar, depois
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
            try { await s.InstallAsync(opt, prog, CancellationToken.None); BeginInvoke(() => { Busy(false, Loc.T("srv.installedClickStart", "Instalado. Clique em Iniciar.")); }); }
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
        // antes: os programas instalados são da versão do cliente? (lê o gs do Linux, fora da tela)
        Busy(true, Loc.T("srv.checkingPrograms", "Conferindo a versão dos programas do servidor…"));
        Task.Run(() =>
        {
            string? missing = null; LocalServer.ProgramSwap? sw = null;
            try { sw = LocalServer.SwapFor(client, pkg, out missing); } catch { }
            BeginInvoke(() =>
            {
                Busy(false);
                if (missing is not null) AppendLog("⚠ " + missing);
                if (sw is not null && !AskSwap(this, sw)) { AppendLog("⚠ " + Loc.T("srv.swapSkipped", "Programas de outra versão mantidos: o login continuará recusado.")); sw = null; }
                // config inteira do servidor + elements/tasks/loja/IA do cliente, comparados pelo conteúdo: só o que difere vai (LocalServer.SendAll)
                Run(Loc.T("srv.sendingChanged", "Enviando o que mudou e reiniciando…"), s => s.SendAll(pkg, client, ai, sw), ok => FilesSent?.Invoke(ok));
            });
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

    /// <summary>
    /// Avisa que o servidor local instalado é de outra versão que o cliente e pergunta se troca os programas, se guarda a
    /// instalação atual e se recomeça o banco do jogo. false = não trocar.
    /// </summary>
    public static bool AskSwap(IWin32Window owner, LocalServer.ProgramSwap s)
    {
        string V(int? v) => v is null ? "?" : $"v{v}" + (ProgramVersion.GameFor(v.Value) is { Length: > 0 } g ? $" ({g})" : "");
        using var f = new Form
        {
            Text = Loc.T("srv.swapTitle", "Servidor local de outra versão"), Width = 640, Height = 360, StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, Font = new Font("Segoe UI", 9.5f), Padding = new Padding(14),
        };
        var text = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Format(Loc.T("srv.swapBody", "O servidor local instalado é {0} e o seu cliente é {1}. Com versões diferentes o jogo recusa o login (\"versão do cliente baixa\").\n\nO MyAceEditor vai {2} os programas do servidor para os de:\n{3}\n\nAs contas de login continuam. Personagens criados em outra versão em geral não abrem nesta."),
                V(s.From), V(s.To), s.Downgrade ? Loc.T("srv.swapDown", "voltar") : Loc.T("srv.swapUp", "atualizar"), s.Package),
        };
        var fresh = new CheckBox { Dock = DockStyle.Bottom, Height = 28, Checked = s.FreshDatabase, Text = Loc.T("srv.swapFreshDb", "Recomeçar o banco do jogo (personagens e nomes) — recomendado ao trocar de versão") };
        var backup = new CheckBox { Dock = DockStyle.Bottom, Height = 28, Checked = s.Backup, Text = Loc.T("srv.swapBackup", "Guardar a instalação atual antes (/home.bak-<data>, alguns GB)") };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = Loc.T("srv.swapOk", "Trocar os programas"), AutoSize = true, DialogResult = DialogResult.OK };
        var no = new Button { Text = Loc.T("srv.swapNo", "Agora não"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(no);
        f.Controls.Add(text); f.Controls.Add(fresh); f.Controls.Add(backup); f.Controls.Add(buttons);
        f.AcceptButton = ok; f.CancelButton = no;
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        s.Backup = backup.Checked; s.FreshDatabase = fresh.Checked;
        return true;
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
            try { exists = LocalServer.DistroExists(); st = exists ? NewServer().Status() : new(); } catch { st = new(); }
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
        if (!LocalServer.DistroExists()) { Warn(Loc.T("srv.installTheLocalServerFirst", "Instale o servidor local primeiro.")); return; }
        var w = new ServerAdminWindow(NewServer(), pkg, _client(), BackColor, ForeColor, _accent);
        w.Show(FindForm());
    }

    /// <summary>Painel do GM em outro processo (é um programa à parte: só usa o cliente do jogo, não o editor).</summary>
    void OpenGmPanel()
    {
        var client = _client();
        var args = Directory.Exists(client) ? $"--gm \"{client.TrimEnd('\\')}\"" : "--gm";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false }); }
        catch (Exception ex) { Warn(ex.Message); }
    }

    void Warn(string msg) => MessageBox.Show(this, msg, Loc.T("srv.server", "Servidor"), MessageBoxButtons.OK, MessageBoxIcon.Information);
}
