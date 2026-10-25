using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Aba "Servidor": servidor local no WSL sem passos manuais (LocalServer). Os caminhos vêm da Verificação; o usuário só
/// escolhe login e senha da conta. Botões: Instalar/completar, Iniciar, Parar, Enviar meus arquivos e reiniciar, Criar
/// conta. Estado de cada serviço com luz verde/vermelha e log ao vivo.
/// </summary>
public sealed class ServerView : UserControl
{
    static string L(string pt, string en) => AppLang.T(pt, en);

    readonly Func<string> _client, _serverHome;
    readonly Func<string, string?> _load;
    readonly Action<string, string> _save;
    readonly TextBox _pkg = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _base = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _login = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _pass = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
    readonly CheckBox _gm = new() { AutoSize = true, Checked = true };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.5f), WordWrap = true };
    readonly Label _step = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly FlowLayoutPanel _lights = new() { Dock = DockStyle.Top, Height = 34, WrapContents = false, AutoScroll = true };
    readonly List<Button> _buttons = new();
    bool _busy;
    /// <summary>aipolicy.data aberto na aba AI Policy (é o que vai para o servidor ao enviar).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string?>? AiPolicyPath { get; set; }

    public ServerView(Color bg, Color fg, Color accent, Func<string> client, Func<string> serverHome, Func<string, string?> load, Action<string, string> save)
    {
        _client = client; _serverHome = serverHome; _load = load; _save = save;
        BackColor = bg; ForeColor = fg; Dock = DockStyle.Fill; Padding = new Padding(10);
        var intro = new Label
        {
            Dock = DockStyle.Top, Height = 46, ForeColor = fg,
            Text = L("Servidor de teste no seu PC (Linux do Windows, WSL). Clique em Instalar uma vez; depois é só Iniciar e entrar no jogo pelo servidor \"Local\". Seus arquivos originais do servidor não são alterados: o programa trabalha numa cópia.",
                     "Test server on your PC (Windows Subsystem for Linux). Click Install once; then just Start and join the \"Local\" server in the game. Your original server files are not changed: the program works on a copy."),
        };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 104, ColumnCount = 3, RowCount = 3 };
        for (int i = 0; i < 3; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        Label Lb(string t) => new() { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = fg };
        form.Controls.Add(Lb(L("Pacote do servidor (\"server files\")", "Server package (\"server files\")")), 0, 0); form.Controls.Add(_pkg, 1, 0);
        form.Controls.Add(Btn(L("Procurar…", "Browse…"), () => Pick(_pkg)), 2, 0);
        form.Controls.Add(Lb(L("Pasta do servidor local", "Local server folder")), 0, 1); form.Controls.Add(_base, 1, 1);
        form.Controls.Add(Btn(L("Procurar…", "Browse…"), () => Pick(_base)), 2, 1);
        var acc = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        acc.Controls.AddRange(new Control[] { Lb(L("Login", "Login")), _login, Lb(L("Senha", "Password")), _pass, _gm });
        _gm.Text = L("GM", "GM");
        form.Controls.Add(Lb(L("Sua conta no jogo", "Your game account")), 0, 2); form.Controls.Add(acc, 1, 2);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        bar.Controls.Add(Btn(L("⬇ Instalar / completar", "⬇ Install / complete"), Install, accent));
        bar.Controls.Add(Btn(L("▶ Iniciar", "▶ Start"), () => Run(L("Iniciando…", "Starting…"), s => s.Start()), accent));
        bar.Controls.Add(Btn(L("■ Parar", "■ Stop"), () => Run(L("Parando…", "Stopping…"), s => s.Stop())));
        bar.Controls.Add(Btn(L("⟳ Enviar meus arquivos e reiniciar", "⟳ Send my files and restart"), SendFiles));
        bar.Controls.Add(Btn(L("👤 Criar/trocar conta", "👤 Create/change account"), CreateAccount));
        bar.Controls.Add(Btn(L("Estado", "Status"), RefreshStatus));
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
    }

    Button Btn(string text, Action click, Color? back = null)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 8, 0), FlatStyle = back is null ? FlatStyle.Standard : FlatStyle.Flat };
        if (back is { } c) { b.BackColor = c; b.ForeColor = Color.White; }
        b.Click += (_, _) => { if (!_busy) click(); };
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
        s.Log = line => BeginInvoke(() => AppendLog(line));
        return s;
    }

    void AppendLog(string line)
    {
        if (line.StartsWith("##PASSO ")) line = "▶ " + line[8..];
        _log.AppendText(line + Environment.NewLine);
    }

    void Busy(bool on, string text = "")
    {
        _busy = on; _bar.Visible = on; _step.Text = text;
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
        if (!Directory.Exists(Path.Combine(pkg, "home"))) { Warn(L("Escolha a pasta \"server files\" do pacote (a que tem home, etc e lib).", "Pick the package's \"server files\" folder (with home, etc and lib).")); return; }
        if (SqlDir() is not { } sql) { Warn(L("Não achei a pasta SQL (PW_tabels.sql) ao lado do pacote.", "SQL folder (PW_tabels.sql) not found next to the package.")); return; }
        if (!Valid(_login.Text) || !Valid(_pass.Text)) { Warn(L("Login e senha: só letras e números.", "Login and password: letters and digits only.")); return; }
        var client = _client();
        if (!Directory.Exists(Path.Combine(client, "data"))) { Warn(L("Abra o cliente na aba Verificação (pasta element).", "Set the client (element folder) in the Verification tab.")); return; }
        _save("server_pkg", pkg); _save("server_base", _base.Text.Trim()); _save("server_login", _login.Text.Trim());
        var opt = new LocalServer.InstallOptions(pkg, sql, client, _login.Text.Trim(), _pass.Text, _gm.Checked);
        var s = NewServer();
        Busy(true, L("Instalando…", "Installing…"));
        var prog = new Progress<(string Step, double? Fraction)>(p =>
        {
            _step.Text = p.Step;
            if (p.Fraction is double f) { _bar.Style = ProgressBarStyle.Continuous; _bar.Value = (int)Math.Clamp(f * 100, 0, 100); } else _bar.Style = ProgressBarStyle.Marquee;
            AppendLog("▶ " + p.Step);
        });
        Task.Run(async () =>
        {
            try { await s.InstallAsync(opt, prog, CancellationToken.None); BeginInvoke(() => { Busy(false, L("Instalado. Clique em Iniciar.", "Installed. Click Start.")); }); }
            catch (Exception ex) { BeginInvoke(() => { Busy(false, L("Parou: ", "Stopped: ") + ex.Message); AppendLog("ERRO: " + ex.Message); }); }
        });
    }

    void Run(string text, Func<LocalServer, LocalServer.RunResult> act)
    {
        var s = NewServer();
        Busy(true, text);
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p) { BeginInvoke(() => { Busy(false, p); AppendLog(p); }); return; }
            try { act(s); } catch (Exception ex) { BeginInvoke(() => AppendLog("ERRO: " + ex.Message)); }
            BeginInvoke(() => { Busy(false, ""); RefreshStatus(); });
        });
    }

    /// <summary>Envia ao servidor local o que o editor gravou e reinicia (botão desta aba e "Salvar e enviar" da barra de cima).</summary>
    public void SendFiles()
    {
        if (_busy) return;
        Fill();
        var pkg = _pkg.Text.Trim(); var client = _client();
        if (!Directory.Exists(Path.Combine(pkg, "home"))) { Warn(L("Escolha a pasta \"server files\" na aba Servidor.", "Pick the \"server files\" folder in the Server tab.")); return; }
        var ai = AiPolicyPath?.Invoke();
        // tudo, sempre: config inteira do servidor + elements/tasks/loja/IA do cliente (LocalServer.SendAll)
        Run(L("Enviando todos os arquivos e reiniciando…", "Sending all files and restarting…"), s => s.SendAll(pkg, client, ai));
    }

    void CreateAccount()
    {
        if (!Valid(_login.Text) || !Valid(_pass.Text)) { Warn(L("Login e senha: só letras e números.", "Login and password: letters and digits only.")); return; }
        _save("server_login", _login.Text.Trim());
        Run(L("Criando a conta…", "Creating the account…"), s =>
        {
            var err = s.Account(_login.Text.Trim(), _pass.Text, _gm.Checked);
            s.Log(err ?? L("Conta pronta.", "Account ready."));
            return new LocalServer.RunResult(err is null ? 0 : 1, err ?? "");
        });
    }

    void RefreshStatus()
    {
        _step.Text = L("Consultando…", "Checking…");
        Task.Run(() =>
        {
            if (LocalServer.WslProblem() is { } p) { BeginInvoke(() => { _step.Text = p; AppendLog(p); }); return; }
            Dictionary<string, bool> st;
            try { st = LocalServer.DistroExists() ? NewServer().Status() : new(); } catch { st = new(); }
            BeginInvoke(() =>
            {
                _lights.Controls.Clear();
                if (st.Count == 0) { _step.Text = L("Servidor local ainda não instalado.", "Local server not installed yet."); return; }
                foreach (var (name, on) in st)
                    _lights.Controls.Add(new Label
                    {
                        AutoSize = true, Padding = new Padding(6, 6, 6, 6), Margin = new Padding(0, 0, 6, 0),
                        Text = (on ? "● " : "○ ") + name, ForeColor = on ? Color.LimeGreen : Color.IndianRed,
                    });
                _step.Text = st.TryGetValue("porta29000", out var up) && up
                    ? L("Servidor no ar: entre no jogo pelo servidor \"Local\".", "Server is up: join the \"Local\" server in the game.")
                    : L("Servidor parado ou subindo.", "Server stopped or starting.");
            });
        });
    }

    static bool Valid(string s) => s.Trim().Length is >= 3 and <= 20 && s.Trim().All(char.IsAsciiLetterOrDigit);
    void Warn(string msg) => MessageBox.Show(this, msg, L("Servidor", "Server"), MessageBoxButtons.OK, MessageBoxIcon.Information);
}
