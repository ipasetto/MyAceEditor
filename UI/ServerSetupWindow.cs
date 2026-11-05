using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// "Servidor e pastas" (botão ao lado da bandeira do idioma): o servidor do cliente aberto num lugar só — onde ele fica
/// (WSL deste PC ou outro Linux: máquina virtual, rede, hospedagem), o pacote "server files" e, só para o local, a
/// máquina do WSL (pasta/distro, disco máximo, memória). Grava em ServerPaths; Verificação, Servidor, Loja e NPCGen leem
/// de lá. Padrão UiKit: cartão de estado no topo, barra lateral, textos via Loc.T.
/// </summary>
internal sealed class ServerSetupWindow : Form
{
    readonly string _client;
    readonly StatusCard _card = new();
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1) };
    readonly TabPage _pageGame, _pageWsl;
    NavBar? _nav;
    readonly RadioButton _local = new() { AutoSize = true }, _other = new() { AutoSize = true };
    readonly TextBox _pkg = new() { Width = 520 }, _base = new() { Width = 520 };
    readonly Label _pkgHint = Hint(), _otherHint = Hint(), _baseHint = Hint(), _diskHint = Hint(), _ramHint = Hint(), _wslHint = Hint();
    readonly NumericUpDown _disk = new() { Minimum = 16, Maximum = 2048, Width = 90 }, _ram = new() { Minimum = 2, Width = 90 };
    readonly Button _installWsl = new() { AutoSize = true };
    readonly FlowLayoutPanel _deleteBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0), Visible = false };
    readonly int _ramWas;
    readonly Color _bg, _fg;

    static Label Hint() => new() { AutoSize = true, ForeColor = UiKit.Muted, MaximumSize = new Size(700, 0), Margin = new Padding(3, 0, 3, 10) };

    public ServerSetupWindow(string client, Color bg, Color fg)
    {
        _client = client; _bg = bg; _fg = fg;
        Text = Loc.T("ss.title", "Servidor e pastas");
        Width = 980; Height = 600; MinimumSize = new Size(800, 480); StartPosition = FormStartPosition.CenterParent;
        BackColor = bg; ForeColor = fg; Font = new Font("Segoe UI", 9.5f); ShowInTaskbar = false; MinimizeBox = false;
        var e = ServerPaths.Get(client);

        // ---- servidor do jogo ----
        _local.Text = Loc.T("ss.kindLocal", "Servidor local neste computador (Linux do Windows, WSL)");
        _other.Text = Loc.T("ss.kindOther", "Outro Linux: máquina virtual, outro PC da rede ou hospedagem");
        (e.Kind == ServerPaths.Kind.Other ? _other : _local).Checked = true;
        _otherHint.Text = Loc.T("ss.otherHint", "O editor grava nos arquivos do pacote abaixo; leve-os para o seu servidor. Conectar direto por IP (enviar, iniciar, parar) ainda não existe.");
        _pkg.Text = e.Package;
        var game = Page(Loc.T("ss.pageGame", "Servidor do jogo"),
            Section(Loc.T("ss.client", "Cliente aberto (pasta element)")), Line(new Label { AutoSize = true, Text = client.Length > 0 ? client : Loc.T("ss.noClient", "Nenhum: abra um elements.data primeiro.") }),
            Section(Loc.T("ss.where", "Onde o servidor roda")), _local, _other, _otherHint,
            Section(Loc.T("srv.serverPackageServerFiles", "Pacote do servidor (\"server files\")")), Line(_pkg, Browse(_pkg)), _pkgHint);
        _pageGame = game;

        // ---- máquina local (WSL) ----
        _base.Text = e.Base;
        _disk.Value = Math.Clamp(e.DiskGb, 16, 2048);
        int machine = Math.Max(2, LocalServer.MachineMemoryGb());
        _ram.Maximum = machine;
        _ramWas = Math.Clamp(LocalServer.WslMemoryGb() ?? Math.Max(4, machine / 2), 2, machine);
        _ram.Value = _ramWas;
        _installWsl.Text = Loc.T("fix.wslTitle", "Instalar o Linux do Windows (WSL) para o servidor local");
        _installWsl.Click += (_, _) => { if (ServerView.EnsureWsl(this)) RefreshState(); };
        _diskHint.Text = Loc.T("srv.setupDiskNote", "O disco do servidor cresce conforme o uso até o máximo escolhido. Vários servidores, até da mesma versão: uma pasta para cada.");
        _ramHint.Text = string.Format(Loc.T("srv.setupRamNote", "A memória vale para TODOS os servidores locais (o Linux do Windows é uma máquina só; este PC tem {0} GB). Recomendado: 4 GB ou mais. Mudar desliga o Linux uma vez (servidores no ar param)."), machine);
        _pageWsl = Page(Loc.T("ss.pageWsl", "Máquina do servidor local"),
            Section(Loc.T("srv.wslTitle", "Linux do Windows (WSL)")), _wslHint, _installWsl,
            Section(Loc.T("srv.localServerFolder", "Pasta do servidor local")), Line(_base, Browse(_base)), _baseHint,
            Section(Loc.T("srv.diskLimit", "Disco máximo deste servidor (GB)")), _disk, _diskHint,
            Section(Loc.T("srv.ramLimit", "Memória do Linux (GB)")), _ram, _ramHint, _deleteBox);

        _deleteBox.Controls.Add(Section(Loc.T("ss.deleteTitle", "Apagar servidor")));
        var deleteHint = Hint();
        deleteHint.Text = Loc.T("ss.deleteHint", "Para o servidor e apaga o Linux dele, com contas e personagens. O pacote do servidor e o cliente ficam.");
        _deleteBox.Controls.Add(deleteHint);
        var delete = new Button { Text = Loc.T("ss.deleteBtn", "Apagar servidor"), AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(196, 43, 28), ForeColor = Color.White, Image = UiKit.Icon("\uE74D", Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(8, 0, 8, 0) };
        delete.FlatAppearance.BorderSize = 0;
        delete.Click += (_, _) => DeleteServer(delete);
        _deleteBox.Controls.Add(delete);

        // ---- botões ----
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 8, 10, 8) };
        var save = new Button { Text = Loc.T("ss.save", "Salvar"), AutoSize = true };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), AutoSize = true, DialogResult = DialogResult.Cancel };
        UiKit.Decorate(save, "save", primary: true); UiKit.Decorate(cancel, "cancel");
        save.Click += (_, _) => Save();
        bar.Controls.Add(save); bar.Controls.Add(cancel);
        CancelButton = cancel;

        Controls.Add(_tabs); Controls.Add(bar); Controls.Add(_card);
        _local.CheckedChanged += (_, _) => Rebuild();
        _pkg.TextChanged += (_, _) => PkgHint();
        _base.TextChanged += (_, _) => BaseHint();
        Rebuild(); PkgHint(); BaseHint();
        Shown += (_, _) => RefreshState();
    }

    // páginas e barra lateral: a da máquina WSL só aparece com o servidor local (outro Linux não precisa dela)
    void Rebuild()
    {
        _tabs.TabPages.Clear();
        _tabs.TabPages.Add(_pageGame);
        if (_local.Checked) _tabs.TabPages.Add(_pageWsl);
        if (_nav is not null) { Controls.Remove(_nav); _nav.Dispose(); }
        _nav = new NavBar(_tabs);
        _nav.Add(UiKit.Server, () => _pageGame.Text, () => Loc.T("ss.pageGameTip", "Onde o servidor roda e o pacote dele"), _pageGame);
        if (_local.Checked) _nav.Add(UiKit.Settings, () => _pageWsl.Text, () => Loc.T("ss.pageWslTip", "Pasta, disco e memória do servidor no Linux do Windows"), _pageWsl);
        Controls.Add(_nav);
        _nav.BringToFront(); _tabs.BringToFront();
        _otherHint.Visible = _other.Checked;
        RefreshState();
    }

    TabPage Page(string title, params Control[] rows)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 10, 16, 10), BackColor = _bg };
        flow.Controls.AddRange(rows);
        var p = new TabPage(title) { BackColor = _bg, ForeColor = _fg };
        p.Controls.Add(flow);
        return p;
    }

    static Label Section(string t) => new() { Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiKit.Fg, Margin = new Padding(0, 10, 0, 4) };

    static FlowLayoutPanel Line(params Control[] items)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        f.Controls.AddRange(items);
        return f;
    }

    Button Browse(TextBox box)
    {
        var b = new Button { Text = Loc.T("srv.browse", "Procurar…"), AutoSize = true };
        UiKit.Decorate(b, "folder");
        b.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "" }; if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath; };
        return b;
    }

    int? PackageVersion() => Directory.Exists(Path.Combine(_pkg.Text.Trim(), "home"))
        ? ProgramVersion.ElementsVersion(Path.Combine(_pkg.Text.Trim(), "home", "gamed", "gs")) : null;
    int? ClientVersion() => _client.Length > 0 ? ProgramVersion.ElementsVersion(Path.Combine(_client, "elementclient.exe")) : null;

    void PkgHint()
    {
        var p = _pkg.Text.Trim();
        if (p.Length == 0) { _pkgHint.Text = Loc.T("ss.pkgEmpty", "Escolha a pasta \"server files\" (a que tem home, etc e lib)."); _pkgHint.ForeColor = UiKit.Warn; return; }
        if (!Directory.Exists(Path.Combine(p, "home"))) { _pkgHint.Text = Loc.T("srv.pickThePackageSServer", "Escolha a pasta \"server files\" do pacote (a que tem home, etc e lib)."); _pkgHint.ForeColor = UiKit.Bad; return; }
        int? pv = PackageVersion(), cv = ClientVersion();
        bool same = pv is null || cv is null || pv == cv;
        _pkgHint.Text = same ? string.Format(Loc.T("ss.pkgOk", "gs do pacote: v{0} · cliente: v{1}"), pv?.ToString() ?? "?", cv?.ToString() ?? "?")
                             : string.Format(Loc.T("ss.pkgOtherVersion", "gs do pacote: v{0} · cliente: v{1}. Versões diferentes: o login é recusado. Escolha o pacote da versão do cliente."), pv, cv);
        _pkgHint.ForeColor = same ? UiKit.Muted : UiKit.Bad;
    }

    void BaseHint()
    {
        var b = _base.Text.Trim();
        _baseHint.Text = b.Length == 0 || !Path.IsPathRooted(b) ? Loc.T("srv.pickBase", "Escolha a pasta do servidor local (ex.: D:\\PWServer144v60).")
            : string.Format(Loc.T("ss.baseHint", "Linux deste servidor: {0}. Uma pasta = um servidor; outro servidor (até da mesma versão) = outra pasta."), LocalServer.DistroFor(b));
    }

    // cartão do topo e estado do WSL (fora da tela: wsl.exe demora)
    void DeleteServer(Button button)
    {
        var server = new LocalServer(_base.Text.Trim());
        var ask = string.Format(Loc.T("ss.deleteAsk", "Apagar o servidor local {0}?\n\nO servidor será parado e o Linux dele será apagado, com programas, banco de dados, contas e personagens.\n\nO pacote do servidor, o cliente e os backups continuam no PC. Isto não pode ser desfeito."), server.Distro);
        if (MessageBox.Show(this, ask, Loc.T("ss.deleteTitle", "Apagar servidor"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        button.Enabled = false; UseWaitCursor = true;
        _card.Set(StatusCard.Kind.Busy, Loc.T("ss.deleting", "Apagando o servidor…"));
        Task.Run(() =>
        {
            Exception? error = null;
            try { server.Delete(); } catch (Exception ex) { error = ex; AppLog.Error("apagar servidor", ex); }
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                button.Enabled = true; UseWaitCursor = false;
                if (error is null) MessageBox.Show(this, Loc.T("ss.deleted", "Servidor apagado."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshState();
            });
        });
    }

    void RefreshState()
    {
        if (_other.Checked) { _card.Set(StatusCard.Kind.Off, Loc.T("ss.cardOther", "Servidor em outro Linux"), Loc.T("ss.cardOtherDetail", "O editor trabalha nos arquivos do pacote; a máquina do WSL não é usada.")); return; }
        _card.Set(StatusCard.Kind.Busy, Loc.T("srv.checking", "Consultando…"));
        var b = _base.Text.Trim();
        Task.Run(() =>
        {
            var problem = LocalServer.WslProblem();
            bool installed = problem is null && b.Length > 0 && new LocalServer(b).Exists();
            long vhdx = 0; try { var v = Path.Combine(b, "vm", "ext4.vhdx"); if (File.Exists(v)) vhdx = new FileInfo(v).Length; } catch { }
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _installWsl.Visible = problem is not null;
                _deleteBox.Visible = installed;
                _wslHint.Text = problem ?? Loc.T("ss.wslOk", "Instalado.");
                _wslHint.ForeColor = problem is null ? UiKit.Muted : UiKit.Bad;
                if (vhdx > 0) _diskHint.Text = string.Format(Loc.T("ss.diskUsed", "Em uso agora: {0:0.0} GB. O máximo só muda numa instalação nova. "), vhdx / (1024.0 * 1024 * 1024)) + Loc.T("srv.setupDiskNote", "O disco do servidor cresce conforme o uso até o máximo escolhido. Vários servidores, até da mesma versão: uma pasta para cada.");
                if (problem is not null) _card.Set(StatusCard.Kind.Warn, Loc.T("srv.cardNoWsl", "Linux do Windows (WSL) indisponível"), problem);
                else if (installed) _card.Set(StatusCard.Kind.On, Loc.T("ss.cardInstalled", "Servidor local instalado"), string.Format(Loc.T("ss.cardInstalledDetail", "{0} · ligar e parar na aba Servidor"), b));
                else _card.Set(StatusCard.Kind.Off, Loc.T("srv.cardNotInstalled", "Servidor local não instalado"), Loc.T("ss.cardNotInstalledDetail", "Salve e clique em Instalar na aba Servidor."));
            });
        });
    }

    void Save()
    {
        var p = _pkg.Text.Trim(); var b = _base.Text.Trim();
        if (p.Length > 0 && !Directory.Exists(Path.Combine(p, "home"))) { MessageBox.Show(this, Loc.T("srv.pickThePackageSServer", "Escolha a pasta \"server files\" do pacote (a que tem home, etc e lib)."), Text); return; }
        if (_local.Checked && b.Length > 0 && !Path.IsPathRooted(b)) { MessageBox.Show(this, Loc.T("srv.pickBase", "Escolha a pasta do servidor local (ex.: D:\\PWServer144v60)."), Text); return; }
        if (PackageVersion() is { } pv && ClientVersion() is { } cv && pv != cv
            && MessageBox.Show(this, string.Format(Loc.T("ss.pkgOtherVersion", "gs do pacote: v{0} · cliente: v{1}. Versões diferentes: o login é recusado. Escolha o pacote da versão do cliente."), pv, cv) + "\n\n" + Loc.T("ss.saveAnyway", "Salvar mesmo assim?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (_local.Checked && (int)_ram.Value != _ramWas)
        {
            if (MessageBox.Show(this, string.Format(Loc.T("ss.ramConfirm", "Mudar a memória do Linux para {0} GB desliga o Linux do Windows agora (servidores locais no ar param). Continuar?"), (int)_ram.Value),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { LocalServer.SetWslMemoryGb((int)_ram.Value); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); return; }
        }
        ServerPaths.Set(_client, new(_local.Checked ? ServerPaths.Kind.Local : ServerPaths.Kind.Other, p, b, (int)_disk.Value));
        DialogResult = DialogResult.OK;
    }
}
