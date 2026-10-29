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
    readonly TextBox _login = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _pass = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
    readonly CheckBox _gm = new() { AutoSize = true, Checked = true };
    readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, Font = new Font("Consolas", 9.5f), WordWrap = true, BorderStyle = BorderStyle.None };
    readonly Label _step = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly FlowLayoutPanel _lights = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false, AutoScroll = true };
    readonly List<Button> _buttons = new();
    bool _busy;
    readonly Color _accent;
    /// <summary>aipolicy.data aberto na aba AI Policy (é o que vai para o servidor ao enviar).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? AiPolicyPath { get; set; }

    public ServerView(Color bg, Color fg, Color accent, Func<string> client, Func<string> serverHome, Func<string, string?> load, Action<string, string> save)
    {
        _client = client; _serverHome = serverHome; _load = load; _save = save;
        BackColor = bg; ForeColor = fg; Dock = DockStyle.Fill; Padding = new Padding(10); _accent = accent;
        var intro = new Label { Dock = DockStyle.Top, Height = 46, ForeColor = fg };
        Bind(() => intro.Text = Loc.T("srv.testServerOnYourPc", "Servidor de teste no seu PC (Linux do Windows, WSL). Clique em Instalar uma vez; depois é só Iniciar e entrar no jogo pelo servidor \"Local\". Seus arquivos originais do servidor não são alterados: o programa trabalha numa cópia."));
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 3, RowCount = 3 };
        for (int i = 0; i < 3; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        Label Lb(Func<string> t) { var l = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = fg }; Bind(() => l.Text = t()); return l; }
        form.Controls.Add(Lb(() => Loc.T("srv.serverPackageServerFiles", "Pacote do servidor (\"server files\")")), 0, 0); form.Controls.Add(_pkg, 1, 0);
        form.Controls.Add(Btn(() => Loc.T("srv.browse", "Procurar…"), () => Pick(_pkg)), 2, 0);
        form.Controls.Add(Lb(() => Loc.T("srv.localServerFolder", "Pasta do servidor local")), 0, 1); form.Controls.Add(_base, 1, 1);
        form.Controls.Add(Btn(() => Loc.T("srv.browse", "Procurar…"), () => Pick(_base)), 2, 1);
        var acc = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        acc.Controls.AddRange(new Control[] { Lb(() => Loc.T("srv.login", "Login")), _login, Lb(() => Loc.T("srv.password", "Senha")), _pass, _gm });
        Bind(() => _gm.Text = Loc.T("srv.gm", "GM"));
        form.Controls.Add(Lb(() => Loc.T("srv.yourGameAccount", "Sua conta no jogo")), 0, 2); form.Controls.Add(acc, 1, 2);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        bar.Controls.Add(Btn(() => Loc.T("srv.installComplete", "⬇ Instalar / completar"), Install, accent));
        bar.Controls.Add(Btn(() => Loc.T("srv.start", "▶ Iniciar"), () => Run(Loc.T("srv.starting", "Iniciando…"), s => s.Start()), accent));
        bar.Controls.Add(Btn(() => Loc.T("srv.stop", "■ Parar"), () => Run(Loc.T("srv.stopping", "Parando…"), s => s.Stop())));
        bar.Controls.Add(Btn(() => Loc.T("srv.sendMyFilesAndRestart", "⟳ Enviar meus arquivos e reiniciar"), SendFiles));
        bar.Controls.Add(Btn(() => Loc.T("srv.createChangeAccount", "👤 Criar/trocar conta"), CreateAccount));
        bar.Controls.Add(Btn(() => Loc.T("srv.status", "Estado"), RefreshStatus));
        bar.Controls.Add(Btn(() => Loc.T("srv.admin", "🛠 Administrar…"), OpenAdmin, accent));
        // tudo numa tabela de linhas automáticas (o log ocupa o resto): nada depende da ordem de encaixe
        var root = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        intro.AutoSize = false; intro.Height = 46; form.Height = 104; bar.Height = 40; _step.Height = 30; _lights.Height = 34; _bar.Height = 8;
        foreach (var c in new Control[] { intro, form, bar, _step, _lights, _bar })
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, c.Height));
            c.Dock = DockStyle.Fill; root.Controls.Add(c);
        }
        Controls.Add(_log); Controls.Add(root);   // o log ocupa o resto
        _log.BackColor = Color.FromArgb(Math.Max(0, bg.R - 12), Math.Max(0, bg.G - 12), Math.Max(0, bg.B - 12)); _log.ForeColor = fg;

        _login.Text = _load("server_login") ?? "";
        VisibleChanged += (_, _) => { if (Visible) Fill(); };
        Loc.Changed += ApplyTexts;
    }

    // textos fixos da tela: refeitos quando o idioma muda
    readonly List<Action> _texts = new();
    void Bind(Action apply) { apply(); _texts.Add(apply); }
    void ApplyTexts() { if (IsHandleCreated) BeginInvoke(() => _texts.ForEach(a => a())); else _texts.ForEach(a => a()); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Loc.Changed -= ApplyTexts;
        base.Dispose(disposing);
    }

    Button Btn(Func<string> text, Action click, Color? back = null)
    {
        var b = new Button { AutoSize = true, Height = 32, Margin = new Padding(0, 0, 8, 0), FlatStyle = back is null ? FlatStyle.Standard : FlatStyle.Flat };
        if (back is { } c) { b.BackColor = c; b.ForeColor = Color.White; }
        b.Click += (_, _) => { if (!_busy) click(); };
        Bind(() => b.Text = text());
        _buttons.Add(b);
        return b;
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
        if (!Valid(_login.Text) || !Valid(_pass.Text)) { Warn(Loc.T("srv.loginAndPasswordLettersAnd", "Login e senha: só letras e números.")); return; }
        var client = _client();
        if (!Directory.Exists(Path.Combine(client, "data"))) { Warn(Loc.T("srv.setTheClientElementFolder", "Abra o cliente na aba Verificação (pasta element).")); return; }
        _save("server_pkg", pkg); _save("server_base", _base.Text.Trim()); _save("server_login", _login.Text.Trim());
        var opt = new LocalServer.InstallOptions(pkg, sql, client, _login.Text.Trim(), _pass.Text, _gm.Checked);
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

    void Run(string text, Func<LocalServer, LocalServer.RunResult> act)
    {
        var s = NewServer();
        Busy(true, text);
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p) { BeginInvoke(() => { Busy(false, p); AppendLog(p); }); return; }
            try { act(s); } catch (Exception ex) { BeginInvoke(() => AppendLog(Loc.T("srv.errorLog", "ERRO: ") + ex.Message)); }
            BeginInvoke(() => { Busy(false, ""); _announce = true; RefreshStatus(); });
        });
    }

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
                // tudo, sempre: config inteira do servidor + elements/tasks/loja/IA do cliente (LocalServer.SendAll)
                Run(Loc.T("srv.sendingAllFilesAndRestarting", "Enviando todos os arquivos e reiniciando…"), s => s.SendAll(pkg, client, ai, sw));
            });
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

    void CreateAccount()
    {
        if (!Valid(_login.Text) || !Valid(_pass.Text)) { Warn(Loc.T("srv.loginAndPasswordLettersAnd", "Login e senha: só letras e números.")); return; }
        _save("server_login", _login.Text.Trim());
        Run(Loc.T("srv.creatingTheAccount", "Criando a conta…"), s =>
        {
            var err = s.Account(_login.Text.Trim(), _pass.Text, _gm.Checked);
            s.Log(err ?? Loc.T("srv.accountReady", "Conta pronta."));
            return new LocalServer.RunResult(err is null ? 0 : 1, err ?? "");
        });
    }

    void RefreshStatus()
    {
        _step.Text = Loc.T("srv.checking", "Consultando…");
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p) { BeginInvoke(() => { _step.Text = p; AppendLog(p); }); return; }
            Dictionary<string, bool> st;
            try { st = LocalServer.DistroExists() ? NewServer().Status() : new(); } catch { st = new(); }
            BeginInvoke(() =>
            {
                _lights.Controls.Clear();
                if (st.Count == 0) { _step.Text = Loc.T("srv.localServerNotInstalledYet", "Servidor local ainda não instalado."); return; }
                foreach (var (name, on) in st)
                    _lights.Controls.Add(new Label
                    {
                        AutoSize = true, Padding = new Padding(6, 6, 6, 6), Margin = new Padding(0, 0, 6, 0),
                        Text = (on ? "● " : "○ ") + name, ForeColor = on ? Color.LimeGreen : Color.IndianRed,
                    });
                bool online = st.TryGetValue("porta29000", out var up) && up;
                _step.Text = online
                    ? Loc.T("srv.serverIsUpJoinThe", "Servidor no ar: entre no jogo pelo servidor \"Local\".")
                    : Loc.T("srv.serverStoppedOrStarting", "Servidor parado ou subindo.");
                if (_announce)   // depois de iniciar/enviar: o resultado em uma linha, verde ou vermelha
                {
                    _announce = false;
                    AppendLog(online ? Loc.T("srv.online", "● Servidor online") : Loc.T("srv.offline", "● Servidor offline"), online ? Color.LimeGreen : Color.IndianRed);
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

    static bool Valid(string s) => s.Trim().Length is >= 3 and <= 20 && s.Trim().All(char.IsAsciiLetterOrDigit);
    void Warn(string msg) => MessageBox.Show(this, msg, Loc.T("srv.server", "Servidor"), MessageBoxButtons.OK, MessageBoxIcon.Information);
}
