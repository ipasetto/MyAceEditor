using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Painel de administração do servidor local (o "iweb" num formulário): visão geral, contas, personagens, quem está no
/// jogo, mundo (EXP/drop em dobro, travas, aviso geral) e mapas/instâncias. Toda ida ao servidor roda fora da tela.
/// </summary>
public sealed class ServerAdminWindow : Form
{
    static string L(string pt, string en) => AppLang.T(pt, en);

    readonly LocalServer _srv;
    readonly ServerAdmin _adm;
    readonly string _gsConf, _client;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly Color _bg, _fg, _accent;
    int _busy;

    // visão geral
    readonly FlowLayoutPanel _lights = new() { Dock = DockStyle.Top, Height = 40, WrapContents = false, AutoScroll = true };
    readonly Label _summary = new() { Dock = DockStyle.Top, Height = 60, Padding = new Padding(4), Font = new Font("Segoe UI", 10.5f) };
    readonly ComboBox _logSvc = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _logText = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
    // contas
    readonly DataGridView _acc = Grid();
    List<ServerAdmin.Account> _accounts = new();
    // personagens
    readonly ComboBox _roleAcc = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ListBox _roles = new() { Dock = DockStyle.Left, Width = 240, IntegralHeight = false };
    readonly TextBox _roleInfo = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 10f), ScrollBars = ScrollBars.Vertical };
    // no jogo
    readonly DataGridView _online = Grid();
    // mundo
    readonly Dictionary<ServerAdmin.Attr, CheckBox> _attrs = new();
    bool _loadingAttrs;
    readonly TextBox _msg = new() { Width = 520 };
    // mapas
    readonly DataGridView _maps = Grid();
    readonly Label _ram = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };
    List<ServerAdmin.MapDef> _mapDefs = new();

    public ServerAdminWindow(LocalServer srv, string serverFiles, string clientElement, Color bg, Color fg, Color accent)
    {
        _srv = srv; _adm = new ServerAdmin(srv); _bg = bg; _fg = fg; _accent = accent;
        _gsConf = Path.Combine(serverFiles, "home", "gamed", "gs.conf");
        _client = clientElement;
        Text = L("Administrar servidor local", "Local server admin");
        Width = 1100; Height = 720; StartPosition = FormStartPosition.CenterParent; BackColor = bg; ForeColor = fg; Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(800, 500);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page(L("Visão geral", "Overview"), BuildOverview()));
        tabs.TabPages.Add(Page(L("Contas", "Accounts"), BuildAccounts()));
        tabs.TabPages.Add(Page(L("Personagens", "Characters"), BuildRoles()));
        tabs.TabPages.Add(Page(L("No jogo agora", "Online now"), BuildOnline()));
        tabs.TabPages.Add(Page(L("Mundo", "World"), BuildWorld()));
        tabs.TabPages.Add(Page(L("Mapas e instâncias", "Maps and instances"), BuildMaps()));
        tabs.SelectedIndexChanged += (_, _) => RefreshTab(tabs.SelectedIndex);
        Controls.Add(tabs); Controls.Add(_status);
        Shown += (_, _) => Work(L("Preparando…", "Preparing…"), () => _srv.EnsureTools(), () => RefreshTab(0));
    }

    // ---------- infraestrutura ----------
    static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.None,
    };

    TabPage Page(string title, Control content)
    {
        var p = new TabPage(title) { BackColor = _bg, ForeColor = _fg, Padding = new Padding(8) };
        p.Controls.Add(content);
        return p;
    }

    FlowLayoutPanel Bar(params Control[] items)
    {
        var f = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, AutoScroll = true };
        f.Controls.AddRange(items);
        return f;
    }

    Button Btn(string text, Action click, bool main = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(0, 4, 8, 0) };
        if (main) { b.FlatStyle = FlatStyle.Flat; b.BackColor = _accent; b.ForeColor = Color.White; }
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Roda <paramref name="job"/> fora da tela e depois <paramref name="then"/> na tela; erro vai para a barra.</summary>
    void Work(string what, Action job, Action? then = null)
    {
        Interlocked.Increment(ref _busy);
        _status.Text = what; UseWaitCursor = true;
        Task.Run(() =>
        {
            Exception? err = null;
            try { job(); } catch (Exception ex) { err = ex; }
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (Interlocked.Decrement(ref _busy) == 0) UseWaitCursor = false;
                if (err is not null) { _status.Text = L("Erro: ", "Error: ") + Friendly(err); return; }
                if (_status.Text == what) _status.Text = "";
                then?.Invoke();
            });
        });
    }

    static string Friendly(Exception ex) => ex is IOException or System.Net.Sockets.SocketException or TimeoutException
        ? L("o servidor não respondeu (está iniciado?) — ", "the server did not answer (is it started?) — ") + ex.Message
        : ex.Message;

    /// <summary>Resultado de uma ação (pode ser chamado de dentro do Work, fora da tela).</summary>
    void Done(string? error, string ok)
    {
        var text = error is null ? ok : L("Erro: ", "Error: ") + error;
        if (InvokeRequired) BeginInvoke(() => _status.Text = text); else _status.Text = text;
    }

    void RefreshTab(int i)
    {
        switch (i)
        {
            case 0: RefreshOverview(); break;
            case 1: case 2: RefreshAccounts(); break;
            case 3: RefreshOnline(); break;
            case 4: RefreshWorld(); break;
            case 5: RefreshMaps(); break;
        }
    }

    static string? Ask(IWin32Window owner, string title, string label, string value = "", bool password = false)
    {
        using var f = new Form { Text = title, Width = 420, Height = 160, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
        var lb = new Label { Text = label, Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 8, 8, 0) };
        var tb = new TextBox { Text = value, Dock = DockStyle.Top, UseSystemPasswordChar = password, Margin = new Padding(8) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        f.Controls.Add(tb); f.Controls.Add(lb); f.Controls.Add(ok); f.AcceptButton = ok;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Trim() : null;
    }

    static bool ValidLogin(string s) => s.Length is >= 3 and <= 20 && s.All(char.IsAsciiLetterOrDigit);

    // ---------- visão geral ----------
    Control BuildOverview()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        foreach (var s in new[] { "gs-gs01", "glinkd", "gdeliveryd", "gamedbd", "gfactiond", "gacd", "uniquenamed", "logservice" }) _logSvc.Items.Add(s);
        _logSvc.SelectedIndex = 0;
        var bar = Bar(
            Btn(L("▶ Iniciar", "▶ Start"), () => Work(L("Iniciando…", "Starting…"), () => _srv.Start(), RefreshOverview), true),
            Btn(L("■ Parar", "■ Stop"), () => { if (Confirm(L("Parar o servidor? Quem estiver no jogo cai.", "Stop the server? Players will be disconnected."))) Work(L("Parando…", "Stopping…"), () => _srv.Stop(), RefreshOverview); }),
            Btn(L("⟳ Reiniciar", "⟳ Restart"), () => { if (Confirm(L("Reiniciar o servidor? Quem estiver no jogo cai.", "Restart the server? Players will be disconnected."))) Work(L("Reiniciando…", "Restarting…"), () => { _srv.Stop(); _srv.Start(); }, RefreshOverview); }),
            Btn(L("Atualizar", "Refresh"), RefreshOverview),
            new Label { Text = L("   Log:", "   Log:"), AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _logSvc,
            Btn(L("Ver", "Show"), () => { var svc = (string)_logSvc.SelectedItem!; string t = ""; Work(L("Lendo log…", "Reading log…"), () => t = _srv.Bash($"pwctl log {svc} 200", timeoutMs: 60_000).Output, () => { _logText.Text = t.Replace("\n", "\r\n"); _logText.SelectionStart = _logText.TextLength; _logText.ScrollToCaret(); }); }));
        p.Controls.Add(_logText); p.Controls.Add(_summary); p.Controls.Add(_lights); p.Controls.Add(bar);
        return p;
    }

    bool Confirm(string q) => MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    void RefreshOverview()
    {
        Dictionary<string, bool> st = new(); (int, int)? online = null; (List<ServerAdmin.MapProc> Running, int Total, int Free) maps = (new(), 0, 0);
        Work(L("Consultando…", "Checking…"), () =>
        {
            st = _srv.Status();
            maps = _adm.Maps();
            try { online = _adm.OnlineCount(); } catch { }
        }, () =>
        {
            _lights.Controls.Clear();
            foreach (var (name, on) in st)
                _lights.Controls.Add(new Label { AutoSize = true, Padding = new Padding(6), Text = (on ? "● " : "○ ") + name, ForeColor = on ? Color.ForestGreen : Color.IndianRed });
            bool up = st.TryGetValue("porta29000", out var u) && u;
            _summary.Text = (up ? L("Servidor no ar.", "Server is up.") : L("Servidor parado ou subindo.", "Server stopped or starting."))
                + "   " + (online is { } o ? string.Format(L("Jogadores no jogo: {0}", "Players online: {0}"), o.Item1) : "")
                + "\n" + string.Format(L("Mapas rodando: {0} ({1}) · RAM do Linux: {2:N0} MB livres de {3:N0} MB", "Maps running: {0} ({1}) · Linux RAM: {2:N0} MB free of {3:N0} MB"),
                    maps.Running.Count, string.Join(", ", maps.Running.Select(m => $"{m.Tag} {m.RamMb} MB")), maps.Free, maps.Total);
        });
    }

    // ---------- contas ----------
    Control BuildAccounts()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(
            Btn(L("Nova conta…", "New account…"), NewAccount, true),
            Btn(L("Trocar senha…", "Change password…"), ChangePassword),
            Btn(L("GM liga/desliga", "Toggle GM"), ToggleGm),
            Btn(L("Enviar gold…", "Send gold…"), SendGold),
            Btn(L("Dar CASH por ID…", "Give CASH by ID…"), GiveCashById),
            Btn(L("Banir…", "Ban…"), BanAccount),
            Btn(L("Liberar", "Unban"), () => { if (Sel() is { } a) Work(L("Liberando…", "Unbanning…"), () => Done(_adm.Unban(a.Id), L("Conta liberada.", "Account unbanned.")), RefreshAccounts); }),
            Btn(L("Atualizar", "Refresh"), RefreshAccounts));
        var hint = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = Color.DimGray, Padding = new Padding(4),
            Text = L("Gold: 1 gold = 100 de cash; o servidor entrega na conta em alguns segundos (ou no próximo login). Banir também derruba a conta se ela estiver no jogo.",
                     "Gold: 1 gold = 100 cash; the server credits it within seconds (or on next login). Banning also disconnects the account if it is online.") };
        p.Controls.Add(_acc); p.Controls.Add(hint); p.Controls.Add(bar);
        return p;
    }

    ServerAdmin.Account? Sel()
    {
        if (_acc.CurrentRow?.Index is int i && i >= 0 && i < _accounts.Count) return _accounts[i];
        _status.Text = L("Escolha uma conta na lista.", "Pick an account in the list."); return null;
    }

    void RefreshAccounts()
    {
        List<ServerAdmin.Account> list = new();
        Work(L("Lendo contas…", "Reading accounts…"), () => list = _adm.Accounts(), () =>
        {
            _accounts = list;
            _acc.DataSource = list.Select(a => new
            {
                ID = a.Id, Login = a.Login, GM = a.Gm ? "sim" : "", Online = a.Online ? "● no jogo" : "",
                Criada = a.Created, UltimoLogin = a.LastLogin, BanidaAte = a.BannedUntil?.ToString("dd/MM/yyyy HH:mm") ?? "",
                GoldPendente = a.PendingGold > 0 ? a.PendingGold.ToString("N0") : "",
            }).ToList();
            var keep = _roleAcc.SelectedIndex;
            _roleAcc.Items.Clear();
            foreach (var a in list) _roleAcc.Items.Add($"{a.Login} (ID {a.Id})");
            if (_roleAcc.Items.Count > 0) _roleAcc.SelectedIndex = Math.Clamp(keep, 0, _roleAcc.Items.Count - 1);
        });
    }

    void NewAccount()
    {
        var login = Ask(this, L("Nova conta", "New account"), L("Login (letras e números):", "Login (letters and digits):"));
        if (login is null) return;
        if (!ValidLogin(login)) { _status.Text = L("Login: 3 a 20 letras ou números.", "Login: 3 to 20 letters or digits."); return; }
        var pass = Ask(this, L("Nova conta", "New account"), L("Senha (letras e números):", "Password (letters and digits):"), password: true);
        if (pass is null || !ValidLogin(pass)) { _status.Text = L("Senha: 3 a 20 letras ou números.", "Password: 3 to 20 letters or digits."); return; }
        bool gm = Confirm(L("Dar GM a esta conta?", "Make this account GM?"));
        Work(L("Criando conta…", "Creating account…"), () => Done(_srv.Account(login, pass, gm), L("Conta criada.", "Account created.")), RefreshAccounts);
    }

    void ChangePassword()
    {
        if (Sel() is not { } a) return;
        var pass = Ask(this, a.Login, L("Nova senha (letras e números):", "New password (letters and digits):"), password: true);
        if (pass is null) return;
        if (!ValidLogin(pass)) { _status.Text = L("Senha: 3 a 20 letras ou números.", "Password: 3 to 20 letters or digits."); return; }
        Work(L("Trocando senha…", "Changing password…"), () => Done(_srv.Account(a.Login, pass, a.Gm), L("Senha trocada.", "Password changed.")), RefreshAccounts);
    }

    void ToggleGm()
    {
        if (Sel() is not { } a) return;
        Work(L("Alterando GM…", "Changing GM…"), () => Done(_adm.SetGm(a.Id, !a.Gm),
            (a.Gm ? L("GM removido de ", "GM removed from ") : L("GM dado a ", "GM given to ")) + a.Login + L(" (vale no próximo login).", " (applies on next login).")), RefreshAccounts);
    }

    void SendGold()
    {
        if (Sel() is not { } a) return;
        var v = Ask(this, a.Login, L("Quantidade de gold:", "Amount of gold:"), "1000");
        if (v is null) return;
        if (!int.TryParse(v, out var gold) || gold <= 0 || gold > 2_000_000) { _status.Text = L("Gold: número entre 1 e 2.000.000.", "Gold: a number from 1 to 2,000,000."); return; }
        Work(L("Enviando gold…", "Sending gold…"), () => Done(_adm.Gold(a.Id, gold), string.Format(L("{0:N0} gold enviados para {1}.", "{0:N0} gold sent to {1}."), gold, a.Login)), RefreshAccounts);
    }

    // CASH por ID: aceita o ID da conta ou de um personagem (personagem → conta = ID & ~15, LOGICUID do servidor)
    void GiveCashById()
    {
        var sel = _acc.CurrentRow?.Index is int i && i >= 0 && i < _accounts.Count ? _accounts[i].Id.ToString() : "";
        var v = Ask(this, L("Dar CASH", "Give CASH"), L("ID da conta ou do personagem:", "Account or character ID:"), sel);
        if (v is null) return;
        if (!int.TryParse(v, out var id) || id < 16) { _status.Text = L("ID: só números.", "ID: digits only."); return; }
        var g = Ask(this, L("Dar CASH", "Give CASH"), L("Quantidade de gold (1 gold = 100 de cash):", "Amount of gold (1 gold = 100 cash):"), "1000");
        if (g is null) return;
        if (!int.TryParse(g, out var gold) || gold <= 0 || gold > 2_000_000) { _status.Text = L("Gold: número entre 1 e 2.000.000.", "Gold: a number from 1 to 2,000,000."); return; }
        ServerAdmin.Account? acc = null; ServerAdmin.RoleInfo? role = null;
        Work(L("Procurando o ID…", "Looking up the ID…"), () =>
        {
            var list = _adm.Accounts();
            acc = list.FirstOrDefault(a => a.Id == id);
            if (acc is not null) return;
            try { role = _adm.RoleDetails(id); } catch { }
            if (role is not null) acc = list.FirstOrDefault(a => a.Id == (id & ~0xF));
        }, () =>
        {
            if (acc is not { } a) { _status.Text = string.Format(L("Nenhuma conta ou personagem com o ID {0}.", "No account or character with ID {0}."), id); return; }
            var who = role is null ? $"{a.Login} (ID {a.Id})"
                : string.Format(L("{0} (personagem {1}), conta {2} (ID {3})", "{0} (character {1}), account {2} (ID {3})"), role.Name, role.Id, a.Login, a.Id);
            if (!Confirm(string.Format(L("Dar {0:N0} gold ({1:N0} de cash) para {2}?\n\nO cash fica na conta: todos os personagens dela usam o mesmo saldo.",
                    "Give {0:N0} gold ({1:N0} cash) to {2}?\n\nCash goes to the account: all its characters share the balance."), gold, gold * 100L, who))) return;
            Work(L("Enviando gold…", "Sending gold…"), () => Done(_adm.Gold(a.Id, gold), string.Format(L("{0:N0} gold enviados para {1}.", "{0:N0} gold sent to {1}."), gold, who)), RefreshAccounts);
        });
    }

    void BanAccount()
    {
        if (Sel() is not { } a) return;
        var v = Ask(this, a.Login, L("Banir por quantas horas? (0 = só derrubar)", "Ban for how many hours? (0 = just kick)"), "24");
        if (v is null || !double.TryParse(v, out var h) || h < 0) return;
        int secs = (int)Math.Min(int.MaxValue, h * 3600);
        Work(L("Banindo…", "Banning…"), () =>
        {
            if (secs == 0) { _adm.Kick(a.Id, 1, "MyAceEditor"); Done(null, L("Conta derrubada.", "Account kicked.")); }
            else Done(_adm.Ban(a.Id, secs, "MyAceEditor"), string.Format(L("{0} banida por {1} h.", "{0} banned for {1} h."), a.Login, h));
        }, RefreshAccounts);
    }

    // ---------- personagens ----------
    Control BuildRoles()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(new Label { Text = L("Conta:", "Account:"), AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _roleAcc,
            Btn(L("Ver personagens", "Show characters"), LoadRoles, true));
        _roleAcc.SelectedIndexChanged += (_, _) => LoadRoles();
        _roles.SelectedIndexChanged += (_, _) => LoadRole();
        var note = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.DimGray, Text = L("Leitura direta do banco do jogo (gamedbd). Editar personagem fica para uma próxima etapa.", "Read straight from the game database (gamedbd). Character editing comes in a later step.") };
        p.Controls.Add(_roleInfo); p.Controls.Add(_roles); p.Controls.Add(note); p.Controls.Add(bar);
        return p;
    }

    List<ServerAdmin.Role> _roleList = new();

    void LoadRoles()
    {
        if (_roleAcc.SelectedIndex < 0 || _roleAcc.SelectedIndex >= _accounts.Count) return;
        var a = _accounts[_roleAcc.SelectedIndex];
        List<ServerAdmin.Role> list = new();
        Work(L("Lendo personagens…", "Reading characters…"), () => list = _adm.Roles(a.Id), () =>
        {
            _roleList = list; _roles.Items.Clear(); _roleInfo.Text = "";
            foreach (var r in list) _roles.Items.Add($"{r.Name}  (ID {r.Id})");
            if (list.Count == 0) _roleInfo.Text = L("Esta conta não tem personagens.", "This account has no characters.");
            else _roles.SelectedIndex = 0;
        });
    }

    void LoadRole()
    {
        if (_roles.SelectedIndex < 0 || _roles.SelectedIndex >= _roleList.Count) return;
        var id = _roleList[_roles.SelectedIndex].Id;
        ServerAdmin.RoleInfo? info = null;
        if (_mapDefs.Count == 0) _mapDefs = ServerAdmin.MapList(_gsConf, _client);
        Work(L("Lendo personagem…", "Reading character…"), () => info = _adm.RoleDetails(id), () =>
        {
            if (info is not { } r) { _roleInfo.Text = L("Não consegui ler este personagem.", "Could not read this character."); return; }
            var map = _mapDefs.FirstOrDefault(m => m.WorldTag == r.WorldTag);
            _roleInfo.Text = string.Join("\r\n", new[]
            {
                $"{r.Name}   (ID {r.Id})",
                "",
                L("Classe:        ", "Class:         ") + ServerAdmin.ClassName(r.Cls) + "   " + (r.Gender == 0 ? L("masculino", "male") : L("feminino", "female")),
                L("Nível:         ", "Level:         ") + r.Level,
                L("Experiência:   ", "Experience:    ") + r.Exp.ToString("N0"),
                L("Espírito (SP): ", "Spirit (SP):   ") + r.Sp.ToString("N0"),
                $"HP / MP:       {r.Hp:N0} / {r.Mp:N0}",
                L("Reputação:     ", "Reputation:    ") + r.Reputation.ToString("N0"),
                L("Mapa:          ", "Map:           ") + (map is null ? $"tag {r.WorldTag}" : $"{map.Tag} {map.Name}".Trim()),
                L("Posição:       ", "Position:      ") + $"X {r.X:0}  Z {r.Z:0}  ({L("altura", "height")} {r.Y:0})",
                L("Criado em:     ", "Created:       ") + (r.Created?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
                L("Último login:  ", "Last login:    ") + (r.LastLogin?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
            });
        });
    }

    // ---------- no jogo agora ----------
    List<ServerAdmin.OnlineUser> _onlineList = new();

    Control BuildOnline()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(Btn(L("Atualizar", "Refresh"), RefreshOnline, true),
            Btn(L("Expulsar…", "Kick…"), () => OnlineAction(L("Expulsar e impedir de voltar por quantos minutos?", "Kick and block for how many minutes?"), "1", (u, s) => _adm.Kick(u.UserId, s, "MyAceEditor"))),
            Btn(L("Calar…", "Mute…"), () => OnlineAction(L("Proibir o chat por quantos minutos?", "Mute chat for how many minutes?"), "30", (u, s) => _adm.Mute(u.UserId, s, "MyAceEditor"))));
        p.Controls.Add(_online); p.Controls.Add(bar);
        return p;
    }

    void RefreshOnline()
    {
        List<ServerAdmin.OnlineUser> list = new();
        Work(L("Consultando quem está no jogo…", "Checking who is online…"), () => list = _adm.Online(), () =>
        {
            _onlineList = list;
            _online.DataSource = list.Select(u => new { Conta = u.UserId, Personagem = u.Name, IdPersonagem = u.RoleId, Gs = u.GsId }).ToList();
            _status.Text = string.Format(L("{0} no jogo.", "{0} online."), list.Count);
        });
    }

    void OnlineAction(string question, string def, Action<ServerAdmin.OnlineUser, int> act)
    {
        if (_online.CurrentRow?.Index is not int i || i < 0 || i >= _onlineList.Count) { _status.Text = L("Escolha um jogador na lista.", "Pick a player in the list."); return; }
        var u = _onlineList[i];
        var v = Ask(this, u.Name, question, def);
        if (v is null || !int.TryParse(v, out var min) || min < 0) return;
        Work(L("Enviando…", "Sending…"), () => act(u, Math.Max(1, min * 60)), RefreshOnline);
    }

    // ---------- mundo ----------
    Control BuildWorld()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        p.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Padding = new Padding(0, 0, 0, 8), ForeColor = Color.DimGray,
            Text = L("Valem na hora para todo o servidor e duram até ele reiniciar (como no iweb).", "Apply immediately to the whole server and last until it restarts (like iweb).") });
        foreach (var (a, pt, en) in new[]
        {
            (ServerAdmin.Attr.DoubleExp, "EXP em dobro", "Double EXP"), (ServerAdmin.Attr.DoubleSp, "Espírito (SP) em dobro", "Double SP"),
            (ServerAdmin.Attr.DoubleMoney, "Dinheiro em dobro", "Double money"), (ServerAdmin.Attr.DoubleDrop, "Drop em dobro", "Double drop"),
            (ServerAdmin.Attr.NoTrade, "Proibir troca entre jogadores", "Block trading"), (ServerAdmin.Attr.NoAuction, "Proibir leilão", "Block auction"),
            (ServerAdmin.Attr.NoMail, "Proibir correio", "Block mail"), (ServerAdmin.Attr.NoFaction, "Proibir ações de clã", "Block guild actions"),
        })
        {
            var cb = new CheckBox { Text = L(pt, en), AutoSize = true, Margin = new Padding(4, 4, 4, 4) };
            cb.CheckedChanged += (_, _) =>
            {
                if (_loadingAttrs) return;
                bool on = cb.Checked;
                Work(L("Aplicando…", "Applying…"), () => { if (!_adm.SetAttr(a, (byte)(on ? 1 : 0))) throw new InvalidOperationException(L("o servidor recusou", "the server refused")); },
                    () => _status.Text = $"{cb.Text}: " + (on ? L("ligado", "on") : L("desligado", "off")));
            };
            _attrs[a] = cb; p.Controls.Add(cb);
        }
        p.Controls.Add(new Label { AutoSize = true, Padding = new Padding(0, 16, 0, 4), Text = L("Aviso para todos os jogadores:", "Message to all players:"), Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(_msg);
        row.Controls.Add(Btn(L("Enviar aviso", "Send message"), () =>
        {
            var m = _msg.Text.Trim();
            if (m.Length == 0) return;
            if (m.Length > 200) { _status.Text = L("Aviso: até 200 letras.", "Message: up to 200 characters."); return; }
            Work(L("Enviando aviso…", "Sending message…"), () => _adm.Broadcast(m), () => { _status.Text = L("Aviso enviado.", "Message sent."); _msg.Clear(); });
        }, true));
        p.Controls.Add(row);
        return p;
    }

    void RefreshWorld()
    {
        var values = new Dictionary<ServerAdmin.Attr, byte?>();
        Work(L("Lendo o mundo…", "Reading world…"), () => { foreach (var a in _attrs.Keys) values[a] = _adm.GetAttr(a); }, () =>
        {
            _loadingAttrs = true;
            foreach (var (a, cb) in _attrs) cb.Checked = values.GetValueOrDefault(a) is > 0;
            _loadingAttrs = false;
        });
    }

    // ---------- mapas ----------
    Control BuildMaps()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(Btn(L("▶ Iniciar selecionado", "▶ Start selected"), () => MapAction(true), true),
            Btn(L("■ Parar selecionado", "■ Stop selected"), () => MapAction(false)), Btn(L("Atualizar", "Refresh"), RefreshMaps), _ram);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = Color.DimGray, Padding = new Padding(4),
            Text = L("Cada mapa/instância é um programa gs separado e usa RAM. O mundo principal é gs01; as instâncias só abrem para os jogadores se o gs delas estiver rodando.",
                     "Each map/instance is a separate gs program and uses RAM. The main world is gs01; instances only open to players while their gs is running.") };
        p.Controls.Add(_maps); p.Controls.Add(hint); p.Controls.Add(bar);
        return p;
    }

    void RefreshMaps()
    {
        if (_mapDefs.Count == 0) _mapDefs = ServerAdmin.MapList(_gsConf, _client);
        (List<ServerAdmin.MapProc> Running, int Total, int Free) st = (new(), 0, 0);
        Work(L("Lendo mapas…", "Reading maps…"), () => st = _adm.Maps(), () =>
        {
            var run = st.Running.ToDictionary(m => m.Tag, m => m.RamMb);
            _maps.DataSource = _mapDefs.Select(m => new
            {
                Mapa = m.Tag, Nome = m.Name, Tipo = m.Instance ? L("instância", "instance") : L("mundo", "world"),
                Rodando = run.ContainsKey(m.Tag) ? "●" : "", RAM_MB = run.TryGetValue(m.Tag, out var mb) ? mb.ToString("N0") : "",
            }).ToList();
            _ram.Text = string.Format(L("RAM do Linux: {0:N0} MB livres de {1:N0} MB", "Linux RAM: {0:N0} MB free of {1:N0} MB"), st.Free, st.Total);
        });
    }

    void MapAction(bool start)
    {
        if (_maps.CurrentRow?.Index is not int i || i < 0 || i >= _mapDefs.Count) { _status.Text = L("Escolha um mapa na lista.", "Pick a map in the list."); return; }
        var m = _mapDefs[i];
        if (!start && m.Tag == "gs01" && !Confirm(L("Parar o mundo principal derruba todos os jogadores. Continuar?", "Stopping the main world disconnects everyone. Continue?"))) return;
        Work(start ? L("Iniciando ", "Starting ") + m.Tag + "…" : L("Parando ", "Stopping ") + m.Tag + "…",
            () => { if (start) _adm.StartMap(m.Tag); else _adm.StopMap(m.Tag); }, RefreshMaps);
    }
}
