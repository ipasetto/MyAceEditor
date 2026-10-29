using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Painel de administração do servidor local (o "iweb" num formulário): visão geral, contas, personagens, quem está no
/// jogo, mundo (EXP/drop em dobro, travas, aviso geral) e mapas/instâncias. Toda ida ao servidor roda fora da tela.
/// </summary>
public sealed class ServerAdminWindow : Form
{

    readonly LocalServer _srv;
    readonly ServerAdmin _adm;
    readonly string _gsConf, _client;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Marquee, Visible = false };   // aparece enquanto algo roda
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
        Text = Loc.T("adm.localServerAdmin", "Administrar servidor local");
        Width = 1100; Height = 720; StartPosition = FormStartPosition.CenterParent; BackColor = bg; ForeColor = fg; Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(800, 500);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page(Loc.T("adm.overview", "Visão geral"), BuildOverview()));
        tabs.TabPages.Add(Page(Loc.T("adm.accounts", "Contas"), BuildAccounts()));
        tabs.TabPages.Add(Page(Loc.T("adm.characters", "Personagens"), BuildRoles()));
        tabs.TabPages.Add(Page(Loc.T("adm.onlineNow", "No jogo agora"), BuildOnline()));
        tabs.TabPages.Add(Page(Loc.T("adm.world", "Mundo"), BuildWorld()));
        tabs.TabPages.Add(Page(Loc.T("adm.mapsAndInstances", "Mapas e instâncias"), BuildMaps()));
        tabs.SelectedIndexChanged += (_, _) => RefreshTab(tabs.SelectedIndex);
        Controls.Add(tabs); Controls.Add(_bar); Controls.Add(_status);
        Shown += (_, _) => Work(Loc.T("adm.preparing", "Preparando…"), () => _srv.EnsureTools(), () => RefreshTab(0));
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
        _status.Text = what; UseWaitCursor = true; _bar.Visible = true;
        Task.Run(() =>
        {
            Exception? err = null;
            try { job(); } catch (Exception ex) { err = ex; }
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (Interlocked.Decrement(ref _busy) == 0) { UseWaitCursor = false; _bar.Visible = false; }
                if (err is not null) { _status.Text = Loc.T("adm.error", "Erro: ") + Friendly(err); return; }
                if (_status.Text == what) _status.Text = "";
                then?.Invoke();
            });
        });
    }

    static string Friendly(Exception ex) => ex is IOException or System.Net.Sockets.SocketException or TimeoutException
        ? Loc.T("adm.theServerDidNotAnswer", "o servidor não respondeu (está iniciado?) — ") + ex.Message
        : ex.Message;

    /// <summary>Resultado de uma ação (pode ser chamado de dentro do Work, fora da tela).</summary>
    void Done(string? error, string ok)
    {
        var text = error is null ? ok : Loc.T("adm.error", "Erro: ") + error;
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

    // títulos das colunas no idioma da interface (as grades vêm de tipos anônimos)
    static void Headers(DataGridView g, params (string Key, string Pt)[] h)
    {
        for (int i = 0; i < h.Length && i < g.Columns.Count; i++) g.Columns[i].HeaderText = Loc.T(h[i].Key, h[i].Pt);
    }

    static bool ValidLogin(string s) => s.Length is >= 3 and <= 20 && s.All(char.IsAsciiLetterOrDigit);

    // ---------- visão geral ----------
    Control BuildOverview()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        foreach (var s in new[] { "gs-gs01", "glinkd", "gdeliveryd", "gamedbd", "gfactiond", "gacd", "uniquenamed", "logservice" }) _logSvc.Items.Add(s);
        _logSvc.SelectedIndex = 0;
        var bar = Bar(
            Btn(Loc.T("adm.start", "▶ Iniciar"), () => Work(Loc.T("adm.starting", "Iniciando…"), () => _srv.Start(), RefreshOverview), true),
            Btn(Loc.T("adm.stop", "■ Parar"), () => { if (Confirm(Loc.T("adm.stopTheServerPlayersWill", "Parar o servidor? Quem estiver no jogo cai."))) Work(Loc.T("adm.stopping", "Parando…"), () => _srv.Stop(), RefreshOverview); }),
            Btn(Loc.T("adm.restart", "⟳ Reiniciar"), () => { if (Confirm(Loc.T("adm.restartTheServerPlayersWill", "Reiniciar o servidor? Quem estiver no jogo cai."))) Work(Loc.T("adm.restarting", "Reiniciando…"), () => { _srv.Stop(); _srv.Start(); }, RefreshOverview); }),
            Btn(Loc.T("adm.refresh", "Atualizar"), RefreshOverview),
            new Label { Text = Loc.T("adm.log", "   Log:"), AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _logSvc,
            Btn(Loc.T("adm.show", "Ver"), () => { var svc = (string)_logSvc.SelectedItem!; string t = ""; Work(Loc.T("adm.readingLog", "Lendo log…"), () => t = _srv.Bash($"pwctl log {svc} 200", timeoutMs: 60_000).Output, () => { _logText.Text = t.Replace("\n", "\r\n"); _logText.SelectionStart = _logText.TextLength; _logText.ScrollToCaret(); }); }));
        p.Controls.Add(_logText); p.Controls.Add(_summary); p.Controls.Add(_lights); p.Controls.Add(bar);
        return p;
    }

    bool Confirm(string q) => MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    void RefreshOverview()
    {
        Dictionary<string, bool> st = new(); (int, int)? online = null; (List<ServerAdmin.MapProc> Running, int Total, int Free) maps = (new(), 0, 0);
        Work(Loc.T("adm.checking", "Consultando…"), () =>
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
            _summary.Text = (up ? Loc.T("adm.serverIsUp", "Servidor no ar.") : Loc.T("adm.serverStoppedOrStarting", "Servidor parado ou subindo."))
                + "   " + (online is { } o ? string.Format(Loc.T("adm.playersOnline0", "Jogadores no jogo: {0}"), o.Item1) : "")
                + "\n" + string.Format(Loc.T("adm.mapsRunning01Linux", "Mapas rodando: {0} ({1}) · RAM do Linux: {2:N0} MB livres de {3:N0} MB"),
                    maps.Running.Count, string.Join(", ", maps.Running.Select(m => $"{m.Tag} {m.RamMb} MB")), maps.Free, maps.Total);
        });
    }

    // ---------- contas ----------
    Control BuildAccounts()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(
            Btn(Loc.T("adm.newAccount", "Nova conta…"), NewAccount, true),
            Btn(Loc.T("adm.changePassword", "Trocar senha…"), ChangePassword),
            Btn(Loc.T("adm.toggleGm", "GM liga/desliga"), ToggleGm),
            Btn(Loc.T("adm.sendGold", "Enviar gold…"), SendGold),
            Btn(Loc.T("adm.giveCashById", "Dar CASH por ID…"), GiveCashById),
            Btn(Loc.T("adm.ban", "Banir…"), BanAccount),
            Btn(Loc.T("adm.unban", "Liberar"), () => { if (Sel() is { } a) Work(Loc.T("adm.unbanning", "Liberando…"), () => Done(_adm.Unban(a.Id), Loc.T("adm.accountUnbanned", "Conta liberada.")), RefreshAccounts); }),
            Btn(Loc.T("adm.deleteAccount", "Excluir conta…"), DeleteAccount),
            Btn(Loc.T("adm.refresh", "Atualizar"), RefreshAccounts));
        var hint = new Label { Dock = DockStyle.Bottom, Height = 56, ForeColor = Color.DimGray, Padding = new Padding(4),
            Text = Loc.T("adm.gold1Gold100Cash", "Gold: 1 gold = 100 de cash. O authd entrega uns 4 minutos depois de enviar (e só começa 10 minutos depois de ligar o servidor); até lá aparece em GoldPendente. Banir também derruba a conta se ela estiver no jogo.") };
        p.Controls.Add(_acc); p.Controls.Add(hint); p.Controls.Add(bar);
        return p;
    }

    ServerAdmin.Account? Sel()
    {
        if (_acc.CurrentRow?.Index is int i && i >= 0 && i < _accounts.Count) return _accounts[i];
        _status.Text = Loc.T("adm.pickAnAccountInThe", "Escolha uma conta na lista."); return null;
    }

    void RefreshAccounts()
    {
        List<ServerAdmin.Account> list = new();
        Work(Loc.T("adm.readingAccounts", "Lendo contas…"), () => list = _adm.Accounts(), () =>
        {
            _accounts = list;
            _acc.DataSource = list.Select(a => new
            {
                ID = a.Id, Login = a.Login, GM = a.Gm ? Loc.T("adm.yes", "sim") : "", Online = a.Online ? Loc.T("adm.online", "● no jogo") : "",
                Criada = a.Created, UltimoLogin = a.LastLogin, BanidaAte = a.BannedUntil?.ToString("dd/MM/yyyy HH:mm") ?? "",
                GoldPendente = a.PendingGold > 0 ? a.PendingGold.ToString("N0") : "",
            }).ToList();
            Headers(_acc, ("adm.id", "ID"), ("adm.login", "Login"), ("adm.gm", "GM"), ("adm.online2", "No jogo"), ("adm.created2", "Criada"), ("adm.lastLogin2", "Último login"),
                ("adm.bannedUntil", "Banida até"), ("adm.pendingGold", "Gold pendente"));
            var keep = _roleAcc.SelectedIndex;
            _roleAcc.Items.Clear();
            foreach (var a in list) _roleAcc.Items.Add($"{a.Login} (ID {a.Id})");
            if (_roleAcc.Items.Count > 0) _roleAcc.SelectedIndex = Math.Clamp(keep, 0, _roleAcc.Items.Count - 1);
        });
    }

    void NewAccount()
    {
        var login = Ask(this, Loc.T("adm.newAccount2", "Nova conta"), Loc.T("adm.loginLettersAndDigits", "Login (letras e números):"));
        if (login is null) return;
        if (!ValidLogin(login)) { _status.Text = Loc.T("adm.login3To20Letters", "Login: 3 a 20 letras ou números."); return; }
        var pass = Ask(this, Loc.T("adm.newAccount2", "Nova conta"), Loc.T("adm.passwordLettersAndDigits", "Senha (letras e números):"), password: true);
        if (pass is null || !ValidLogin(pass)) { _status.Text = Loc.T("adm.password3To20Letters", "Senha: 3 a 20 letras ou números."); return; }
        bool gm = Confirm(Loc.T("adm.makeThisAccountGm", "Dar GM a esta conta?"));
        Work(Loc.T("adm.creatingAccount", "Criando conta…"), () => Done(_srv.Account(login, pass, gm), Loc.T("adm.accountCreated", "Conta criada.")), RefreshAccounts);
    }

    void ChangePassword()
    {
        if (Sel() is not { } a) return;
        var pass = Ask(this, a.Login, Loc.T("adm.newPasswordLettersAndDigits", "Nova senha (letras e números):"), password: true);
        if (pass is null) return;
        if (!ValidLogin(pass)) { _status.Text = Loc.T("adm.password3To20Letters", "Senha: 3 a 20 letras ou números."); return; }
        Work(Loc.T("adm.changingPassword", "Trocando senha…"), () => Done(_srv.Account(a.Login, pass, a.Gm), Loc.T("adm.passwordChanged", "Senha trocada.")), RefreshAccounts);
    }

    void ToggleGm()
    {
        if (Sel() is not { } a) return;
        Work(Loc.T("adm.changingGm", "Alterando GM…"), () => Done(_adm.SetGm(a.Id, !a.Gm),
            (a.Gm ? Loc.T("adm.gmRemovedFrom", "GM removido de ") : Loc.T("adm.gmGivenTo", "GM dado a ")) + a.Login + Loc.T("adm.appliesOnNextLogin", " (vale no próximo login).")), RefreshAccounts);
    }

    void SendGold()
    {
        if (Sel() is not { } a) return;
        var v = Ask(this, a.Login, Loc.T("adm.amountOfGold", "Quantidade de gold:"), "1000");
        if (v is null) return;
        if (!int.TryParse(v, out var gold) || gold <= 0 || gold > 2_000_000) { _status.Text = Loc.T("adm.goldANumberFrom1", "Gold: número entre 1 e 2.000.000."); return; }
        if (a.PendingGold > 0 && !Confirm(PendingWarning(a))) return;
        Work(Loc.T("adm.sendingGold", "Enviando gold…"), () => Done(_adm.Gold(a.Id, gold), string.Format(SentText, gold, a.Login)), RefreshAccounts);
    }

    // o authd só pega o pedido com 200 s de idade e o temporizador dele começa 10 min depois de ligar (PW-KB)
    static string SentText => Loc.T("adm.goldSent", "{0:N0} gold enviados para {1}: chegam em uns 4 minutos (coluna GoldPendente até lá).");

    string PendingWarning(ServerAdmin.Account a) => string.Format(Loc.T("adm.0N0GoldIsAlready", "Já há {0:N0} gold a caminho desta conta (o authd entrega uns 4 minutos depois). Enviar mais mesmo assim?"), a.PendingGold);

    // CASH por ID: aceita o ID da conta ou de um personagem (personagem → conta = ID & ~15, LOGICUID do servidor)
    void GiveCashById()
    {
        var sel = _acc.CurrentRow?.Index is int i && i >= 0 && i < _accounts.Count ? _accounts[i].Id.ToString() : "";
        var v = Ask(this, Loc.T("adm.giveCash", "Dar CASH"), Loc.T("adm.accountOrCharacterId", "ID da conta ou do personagem:"), sel);
        if (v is null) return;
        if (!int.TryParse(v, out var id) || id < 16) { _status.Text = Loc.T("adm.idDigitsOnly", "ID: só números."); return; }
        var g = Ask(this, Loc.T("adm.giveCash", "Dar CASH"), Loc.T("adm.amountOfGold1Gold", "Quantidade de gold (1 gold = 100 de cash):"), "1000");
        if (g is null) return;
        if (!int.TryParse(g, out var gold) || gold <= 0 || gold > 2_000_000) { _status.Text = Loc.T("adm.goldANumberFrom1", "Gold: número entre 1 e 2.000.000."); return; }
        ServerAdmin.Account? acc = null; ServerAdmin.RoleInfo? role = null;
        Work(Loc.T("adm.lookingUpTheId", "Procurando o ID…"), () =>
        {
            var list = _adm.Accounts();
            acc = list.FirstOrDefault(a => a.Id == id);
            if (acc is not null) return;
            try { role = _adm.RoleDetails(id); } catch { }
            if (role is not null) acc = list.FirstOrDefault(a => a.Id == (id & ~0xF));
        }, () =>
        {
            if (acc is not { } a) { _status.Text = string.Format(Loc.T("adm.noAccountOrCharacterWith", "Nenhuma conta ou personagem com o ID {0}."), id); return; }
            var who = role is null ? $"{a.Login} (ID {a.Id})"
                : string.Format(Loc.T("adm.0Character1Account2", "{0} (personagem {1}), conta {2} (ID {3})"), role.Name, role.Id, a.Login, a.Id);
            if (!Confirm(string.Format(Loc.T("adm.give0N0Gold1", "Dar {0:N0} gold ({1:N0} de cash) para {2}?\n\nO cash fica na conta: todos os personagens dela usam o mesmo saldo."), gold, gold * 100L, who)
                    + (a.PendingGold > 0 ? "\n\n" + PendingWarning(a) : ""))) return;
            Work(Loc.T("adm.sendingGold", "Enviando gold…"), () => Done(_adm.Gold(a.Id, gold), string.Format(SentText, gold, who)), RefreshAccounts);
        });
    }

    void BanAccount()
    {
        if (Sel() is not { } a) return;
        var v = Ask(this, a.Login, Loc.T("adm.banForHowManyHours", "Banir por quantas horas? (0 = só derrubar)"), "24");
        if (v is null || !double.TryParse(v, out var h) || h < 0) return;
        int secs = (int)Math.Min(int.MaxValue, h * 3600);
        Work(Loc.T("adm.banning", "Banindo…"), () =>
        {
            if (secs == 0) { _adm.Kick(a.Id, 1, "MyAceEditor"); Done(null, Loc.T("adm.accountKicked", "Conta derrubada.")); }
            else Done(_adm.Ban(a.Id, secs, "MyAceEditor"), string.Format(Loc.T("adm.0BannedFor1H", "{0} banida por {1} h."), a.Login, h));
        }, RefreshAccounts);
    }

    void DeleteAccount()
    {
        if (Sel() is not { } a) return;
        if (a.Online) { _status.Text = Loc.T("adm.deleteOnline", "Esta conta está no jogo: expulse-a antes de excluir."); return; }
        if (MessageBox.Show(this, string.Format(Loc.T("adm.deleteAccountConfirm", "Excluir a conta \"{0}\" (ID {1}) e todos os personagens dela?\n\nNão dá para desfazer."), a.Login, a.Id),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Work(Loc.T("adm.deletingAccount", "Excluindo conta…"), () => Done(_adm.DeleteAccount(a.Id), string.Format(Loc.T("adm.accountDeleted", "Conta {0} excluída."), a.Login)), RefreshAccounts);
    }

    void DeleteRole()
    {
        if (_roles.SelectedIndex < 0 || _roles.SelectedIndex >= _roleList.Count) { _status.Text = Loc.T("adm.pickACharacter", "Escolha um personagem na lista."); return; }
        var r = _roleList[_roles.SelectedIndex];
        if (MessageBox.Show(this, string.Format(Loc.T("adm.deleteRoleConfirm", "Excluir o personagem \"{0}\" (ID {1})?\n\nNão dá para desfazer. Se ele estiver no jogo, expulse a conta antes."), r.Name, r.Id),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Work(Loc.T("adm.deletingCharacter", "Excluindo personagem…"), () => Done(_adm.DeleteRole(r.Id), string.Format(Loc.T("adm.characterDeleted", "Personagem {0} excluído."), r.Name)), LoadRoles);
    }

    // ---------- personagens ----------
    Control BuildRoles()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(new Label { Text = Loc.T("adm.account", "Conta:"), AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, _roleAcc,
            Btn(Loc.T("adm.showCharacters", "Ver personagens"), LoadRoles, true),
            Btn(Loc.T("adm.deleteCharacter", "Excluir personagem…"), DeleteRole));
        _roleAcc.SelectedIndexChanged += (_, _) => LoadRoles();
        _roles.SelectedIndexChanged += (_, _) => LoadRole();
        var note = new Label { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.DimGray, Text = Loc.T("adm.readStraightFromTheGame", "Leitura direta do banco do jogo (gamedbd). Editar personagem fica para uma próxima etapa.") };
        p.Controls.Add(_roleInfo); p.Controls.Add(_roles); p.Controls.Add(note); p.Controls.Add(bar);
        return p;
    }

    List<ServerAdmin.Role> _roleList = new();

    void LoadRoles()
    {
        if (_roleAcc.SelectedIndex < 0 || _roleAcc.SelectedIndex >= _accounts.Count) return;
        var a = _accounts[_roleAcc.SelectedIndex];
        List<ServerAdmin.Role> list = new();
        Work(Loc.T("adm.readingCharacters", "Lendo personagens…"), () => list = _adm.Roles(a.Id), () =>
        {
            _roleList = list; _roles.Items.Clear(); _roleInfo.Text = "";
            foreach (var r in list) _roles.Items.Add($"{r.Name}  (ID {r.Id})");
            if (list.Count == 0) _roleInfo.Text = Loc.T("adm.thisAccountHasNoCharacters", "Esta conta não tem personagens.");
            else _roles.SelectedIndex = 0;
        });
    }

    void LoadRole()
    {
        if (_roles.SelectedIndex < 0 || _roles.SelectedIndex >= _roleList.Count) return;
        var id = _roleList[_roles.SelectedIndex].Id;
        ServerAdmin.RoleInfo? info = null;
        if (_mapDefs.Count == 0) _mapDefs = ServerAdmin.MapList(_gsConf, _client);
        Work(Loc.T("adm.readingCharacter", "Lendo personagem…"), () => info = _adm.RoleDetails(id), () =>
        {
            if (info is not { } r) { _roleInfo.Text = Loc.T("adm.couldNotReadThisCharacter", "Não consegui ler este personagem."); return; }
            var map = _mapDefs.FirstOrDefault(m => m.WorldTag == r.WorldTag);
            _roleInfo.Text = string.Join("\r\n", new[]
            {
                $"{r.Name}   (ID {r.Id})",
                "",
                Loc.T("adm.class", "Classe:        ") + ServerAdmin.ClassName(r.Cls) + "   " + (r.Gender == 0 ? Loc.T("adm.male", "masculino") : Loc.T("adm.female", "feminino")),
                Loc.T("adm.level", "Nível:         ") + r.Level,
                Loc.T("adm.experience", "Experiência:   ") + r.Exp.ToString("N0"),
                Loc.T("adm.spiritSp", "Espírito (SP): ") + r.Sp.ToString("N0"),
                $"HP / MP:       {r.Hp:N0} / {r.Mp:N0}",
                Loc.T("adm.reputation", "Reputação:     ") + r.Reputation.ToString("N0"),
                Loc.T("adm.map", "Mapa:          ") + (map is null ? $"tag {r.WorldTag}" : $"{map.Tag} {map.Name}".Trim()),
                Loc.T("adm.position", "Posição:       ") + $"X {r.X:0}  Z {r.Z:0}  ({Loc.T("adm.height", "altura")} {r.Y:0})",
                Loc.T("adm.created", "Criado em:     ") + (r.Created?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
                Loc.T("adm.lastLogin", "Último login:  ") + (r.LastLogin?.ToString("dd/MM/yyyy HH:mm") ?? "—"),
            });
        });
    }

    // ---------- no jogo agora ----------
    List<ServerAdmin.OnlineUser> _onlineList = new();

    Control BuildOnline()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var bar = Bar(Btn(Loc.T("adm.refresh", "Atualizar"), RefreshOnline, true),
            Btn(Loc.T("adm.kick", "Expulsar…"), () => OnlineAction(Loc.T("adm.kickAndBlockForHow", "Expulsar e impedir de voltar por quantos minutos?"), "1", (u, s) => _adm.Kick(u.UserId, s, "MyAceEditor"))),
            Btn(Loc.T("adm.mute", "Calar…"), () => OnlineAction(Loc.T("adm.muteChatForHowMany", "Proibir o chat por quantos minutos?"), "30", (u, s) => _adm.Mute(u.UserId, s, "MyAceEditor"))));
        p.Controls.Add(_online); p.Controls.Add(bar);
        return p;
    }

    void RefreshOnline()
    {
        List<ServerAdmin.OnlineUser> list = new();
        Work(Loc.T("adm.checkingWhoIsOnline", "Consultando quem está no jogo…"), () => list = _adm.Online(), () =>
        {
            _onlineList = list;
            _online.DataSource = list.Select(u => new { Conta = u.UserId, Personagem = u.Name, IdPersonagem = u.RoleId, Gs = u.GsId }).ToList();
            Headers(_online, ("adm.account2", "Conta"), ("adm.character", "Personagem"), ("adm.characterId", "ID do personagem"), ("adm.mapGs", "Mapa (gs)"));
            _status.Text = string.Format(Loc.T("adm.0Online", "{0} no jogo."), list.Count);
        });
    }

    void OnlineAction(string question, string def, Action<ServerAdmin.OnlineUser, int> act)
    {
        if (_online.CurrentRow?.Index is not int i || i < 0 || i >= _onlineList.Count) { _status.Text = Loc.T("adm.pickAPlayerInThe", "Escolha um jogador na lista."); return; }
        var u = _onlineList[i];
        var v = Ask(this, u.Name, question, def);
        if (v is null || !int.TryParse(v, out var min) || min < 0) return;
        Work(Loc.T("adm.sending", "Enviando…"), () => act(u, Math.Max(1, min * 60)), RefreshOnline);
    }

    // ---------- mundo ----------
    Control BuildWorld()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        p.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Padding = new Padding(0, 0, 0, 8), ForeColor = Color.DimGray,
            Text = Loc.T("adm.applyImmediatelyToTheWhole", "Valem na hora para todo o servidor e duram até ele reiniciar (como no iweb).") });
        foreach (var (a, text) in new[]
        {
            (ServerAdmin.Attr.DoubleExp, Loc.T("adm.doubleExp", "EXP em dobro")), (ServerAdmin.Attr.DoubleSp, Loc.T("adm.doubleSp", "Espírito (SP) em dobro")),
            (ServerAdmin.Attr.DoubleMoney, Loc.T("adm.doubleMoney", "Dinheiro em dobro")), (ServerAdmin.Attr.DoubleDrop, Loc.T("adm.doubleDrop", "Drop em dobro")),
            (ServerAdmin.Attr.NoTrade, Loc.T("adm.blockTrading", "Proibir troca entre jogadores")), (ServerAdmin.Attr.NoAuction, Loc.T("adm.blockAuction", "Proibir leilão")),
            (ServerAdmin.Attr.NoMail, Loc.T("adm.blockMail", "Proibir correio")), (ServerAdmin.Attr.NoFaction, Loc.T("adm.blockGuildActions", "Proibir ações de clã")),
        })
        {
            var cb = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(4, 4, 4, 4) };
            cb.CheckedChanged += (_, _) =>
            {
                if (_loadingAttrs) return;
                bool on = cb.Checked;
                Work(Loc.T("adm.applying", "Aplicando…"), () => { if (!_adm.SetAttr(a, (byte)(on ? 1 : 0))) throw new InvalidOperationException(Loc.T("adm.theServerRefused", "o servidor recusou")); },
                    () => _status.Text = $"{cb.Text}: " + (on ? Loc.T("adm.on", "ligado") : Loc.T("adm.off", "desligado")));
            };
            _attrs[a] = cb; p.Controls.Add(cb);
        }
        p.Controls.Add(new Label { AutoSize = true, Padding = new Padding(0, 16, 0, 4), Text = Loc.T("adm.messageToAllPlayers", "Aviso para todos os jogadores:"), Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(_msg);
        row.Controls.Add(Btn(Loc.T("adm.sendMessage", "Enviar aviso"), () =>
        {
            var m = _msg.Text.Trim();
            if (m.Length == 0) return;
            if (m.Length > 200) { _status.Text = Loc.T("adm.messageUpTo200Characters", "Aviso: até 200 letras."); return; }
            Work(Loc.T("adm.sendingMessage", "Enviando aviso…"), () => _adm.Broadcast(m), () => { _status.Text = Loc.T("adm.messageSent", "Aviso enviado."); _msg.Clear(); });
        }, true));
        p.Controls.Add(row);
        return p;
    }

    void RefreshWorld()
    {
        var values = new Dictionary<ServerAdmin.Attr, byte?>();
        Work(Loc.T("adm.readingWorld", "Lendo o mundo…"), () => { foreach (var a in _attrs.Keys) values[a] = _adm.GetAttr(a); }, () =>
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
        var bar = Bar(Btn(Loc.T("adm.startSelected", "▶ Iniciar selecionado"), () => MapAction(true), true),
            Btn(Loc.T("adm.stopSelected", "■ Parar selecionado"), () => MapAction(false)), Btn(Loc.T("adm.refresh", "Atualizar"), RefreshMaps), _ram);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 40, ForeColor = Color.DimGray, Padding = new Padding(4),
            Text = Loc.T("adm.eachMapInstanceIsA", "Cada mapa/instância é um programa gs separado e usa RAM. O mundo principal é gs01; as instâncias só abrem para os jogadores se o gs delas estiver rodando.") };
        p.Controls.Add(_maps); p.Controls.Add(hint); p.Controls.Add(bar);
        return p;
    }

    void RefreshMaps()
    {
        if (_mapDefs.Count == 0) _mapDefs = ServerAdmin.MapList(_gsConf, _client);
        (List<ServerAdmin.MapProc> Running, int Total, int Free) st = (new(), 0, 0);
        Work(Loc.T("adm.readingMaps", "Lendo mapas…"), () => st = _adm.Maps(), () =>
        {
            var run = st.Running.ToDictionary(m => m.Tag, m => m.RamMb);
            _maps.DataSource = _mapDefs.Select(m => new
            {
                Mapa = m.Tag, Nome = m.Name, Tipo = m.Instance ? Loc.T("adm.instance", "instância") : Loc.T("adm.world2", "mundo"),
                Rodando = run.ContainsKey(m.Tag) ? "●" : "", RAM_MB = run.TryGetValue(m.Tag, out var mb) ? mb.ToString("N0") : "",
            }).ToList();
            Headers(_maps, ("adm.map2", "Mapa"), ("adm.name", "Nome"), ("adm.type", "Tipo"), ("adm.running", "Rodando"), ("adm.ramMb", "RAM (MB)"));
            _ram.Text = string.Format(Loc.T("adm.linuxRam0N0Mb", "RAM do Linux: {0:N0} MB livres de {1:N0} MB"), st.Free, st.Total);
        });
    }

    void MapAction(bool start)
    {
        if (_maps.CurrentRow?.Index is not int i || i < 0 || i >= _mapDefs.Count) { _status.Text = Loc.T("adm.pickAMapInThe", "Escolha um mapa na lista."); return; }
        var m = _mapDefs[i];
        if (!start && m.Tag == "gs01" && !Confirm(Loc.T("adm.stoppingTheMainWorldDisconnects", "Parar o mundo principal derruba todos os jogadores. Continuar?"))) return;
        Work(start ? Loc.T("adm.starting2", "Iniciando ") + m.Tag + "…" : Loc.T("adm.stopping2", "Parando ") + m.Tag + "…",
            () => { if (start) _adm.StartMap(m.Tag); else _adm.StopMap(m.Tag); }, RefreshMaps);
    }
}
