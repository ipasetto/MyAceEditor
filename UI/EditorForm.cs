using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;

namespace MyAceEditor.UI;

/// <summary>
/// Suíte de editores do Perfect World. Tema claro profissional.
/// Elements: busca global + árvore Categoria → Tipo → Subtipo → itens (coloridos por grade),
/// e à direita todos os campos do item em seções, com rótulos amigáveis e editáveis.
/// </summary>
public sealed class EditorForm : Form
{
    private const int MaxLeaves = 4000;

    private readonly ModuleStore _store = new();
    private readonly TextBox _search = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox _blockCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _typeCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
    private readonly CheckBox _searchAll = new() { Dock = DockStyle.Right, Width = 22, Padding = new Padding(6, 0, 0, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false, MultiSelect = true, OwnerDraw = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Clickable };
    private readonly NumericUpDown _gotoId = new() { Maximum = 999999999, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
    private int _hoverIdx = -1;
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI Semibold", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };
    private readonly FlowLayoutPanel _editorBar = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 5, 0, 0), Visible = false, WrapContents = false };
    private readonly Button _btnAddonEd = new(), _btnDropEd = new();
    private bool _hasAddonEd, _hasDropEd;
    // tooltip da lista: linhas de trechos coloridos (como o tooltip do jogo), drops com ícone e vínculos
    private readonly List<List<(string Text, Color Color, bool Bold)>> _tipRuns = new();
    private readonly List<(Image? Icon, string Text, string Pct, Color Color)> _tipDrops = new();
    private readonly PictureBox _iconBox = new() { Dock = DockStyle.Left, Width = 44, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(6) };
    private readonly ImageList _treeIcons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Dictionary<string, int> _iconIdx = new();
    private string? _iconsRoot;
    private IconAtlas? _atlas;
    private string? _atlasPck;
    private string? _dataPath;
    private RefIndex? _refs;
    private GameConfigs? _cfgs;
    private readonly TabControl _groupTabs = new() { Dock = DockStyle.Top, Height = 26 };
    private readonly RichTextBox _descBox = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f) };
    private readonly ComboBox _findField = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _findOp = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly TextBox _findValue = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _findValue2 = new() { Width = 110, BorderStyle = BorderStyle.FixedSingle, Visible = false };
    private readonly ComboBox _findScope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _findKeep = new() { AutoSize = true, Padding = new Padding(8, 4, 0, 0) };
    private readonly Label _findCount = new() { AutoSize = true, Padding = new Padding(10, 7, 0, 0) };
    // opção do combo de campos: Key null = qualquer campo
    private sealed record FindFieldOpt(string? Key, string Text) { public override string ToString() => Text; }
    private static readonly (SearchOp Op, string Key, string Pt)[] FindOps =
    {
        (SearchOp.Eq, "find.eq", "="), (SearchOp.Ne, "find.ne", "≠"), (SearchOp.Gt, "find.gt", ">"), (SearchOp.Lt, "find.lt", "<"),
        (SearchOp.Ge, "find.ge", "≥"), (SearchOp.Le, "find.le", "≤"), (SearchOp.Between, "find.between", "entre"),
        (SearchOp.Contains, "find.contains", "contém"), (SearchOp.NotContains, "find.notContains", "não contém"), (SearchOp.StartsWith, "find.startsWith", "começa com"),
    };
    private readonly ListView _findList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None };
    private readonly Label _idLabel = new() { Dock = DockStyle.Left, Width = 92, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 11) };
    private readonly List<Item> _history = new();
    private int _histPos = -1;
    private bool _navigating;
    private Item? _autoShown;   // 1ª linha selecionada sozinha ao trocar de filtro
    private readonly ToolStripStatusLabel _stTotal = new() { Spring = false }, _stBlock = new(), _stSel = new();
    private readonly ListView _links = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, HideSelection = false };
    private readonly TabControl _detailTabs = new() { Dock = DockStyle.Fill };
    private TabPage _tabLinks = null!;
    private readonly TabPage _tabShop = new();   // só aparece em NPC_SELL_SERVICE
    private ShopPreview _shop = null!;
    private readonly TabPage _tabMake = new();   // só aparece em NPC_MAKE_SERVICE
    private MakePreview _make = null!;
    private TabControl? _tabs;
    private TabPage? _tabQuests;
    private readonly ContextMenuStrip _ctx = new();
    private readonly ContextMenuStrip _gridCtx = new();
    // campos zerados em todos os itens do bloco ficam ocultos (Exibir > Mostrar campos sem uso no bloco)
    private bool _hideUnused = LoadSetting("hide_unused") != "0";
    private readonly Dictionary<ItemList, HashSet<string>> _unusedCache = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _favorites = new();
    private SplitContainer _split = null!;
    private enum SortMode { Codigo, Nome }
    private SortMode _sortMode = SortMode.Codigo;

    // Paleta clara "branco aço"
    private static readonly Color CBg = Color.FromArgb(231, 236, 242), CSurface = Color.FromArgb(255, 255, 255),
        CSurface2 = Color.FromArgb(242, 245, 249), CLine = Color.FromArgb(203, 211, 221),
        CFg = Color.FromArgb(30, 39, 51), CMuted = Color.FromArgb(92, 102, 117),
        CAccent = Color.FromArgb(37, 99, 235), CSel = Color.FromArgb(219, 232, 253), CSelFg = Color.FromArgb(15, 42, 94);

    // Painel de itens: fundo preto, estilo janela de item do jogo
    private static readonly Color CTreeBg = Color.FromArgb(18, 20, 24), CTreeFg = Color.FromArgb(214, 219, 226),
        CTreeInput = Color.FromArgb(32, 35, 41), CTreeAlt = Color.FromArgb(24, 26, 31),
        CListSel = Color.FromArgb(37, 99, 235), CListId = Color.FromArgb(120, 160, 230);

    // Cores de grade (sistema de cores do PW nos nomes), claras sobre fundo preto
    private static readonly Color GNormal = Color.FromArgb(216, 220, 226), G1 = Color.FromArgb(111, 220, 148),
        G2 = Color.FromArgb(99, 174, 242), G3 = Color.FromArgb(201, 155, 240), GGold = Color.FromArgb(232, 182, 78);

    private Item? _current;
    private ItemList? _curBlock;
    // cada entrada reaplica um texto no idioma atual (ver Loc.T); chamada ao criar e ao trocar o idioma
    private readonly List<Action> _texts = new();
    private void Bind(Action apply) { apply(); _texts.Add(apply); }
    private void ApplyLanguage() { foreach (var a in _texts) a(); RefreshDetailLanguage(); }

    // Reaplica o idioma no que já está na tela (abas de grupo, grade de atributos, vínculos): esses textos
    // são montados a cada item, então não passam pelo Bind.
    private void RefreshDetailLanguage()
    {
        _blockCombo.Invalidate(); _typeCombo.Invalidate(); _list.Invalidate();   // textos calculados na hora de desenhar
        UpdateCounters();
        if (_current is not { } item) return;
        var uses = LinkCount(item);
        _tabLinks.Text = uses > 0 ? $"{Loc.T("detail.links", "Vínculos")} ({uses})" : Loc.T("detail.links", "Vínculos");
        _stSel.Text = string.Format(Loc.T("status.selected", "Selecionado: {0}"), item.Id);
        RebuildGroupTabs(item);
        FillGrid(item);
        if (_detailTabs.SelectedTab == _tabLinks) FillLinks(item);
    }
    private AiPolicyView? _aiView;

    private readonly string? _startFile;   // elements.data passado na linha de comando (ou arrastado sobre o exe)

    public EditorForm(string? startFile = null)
    {
        _startFile = startFile;
        Bind(() => _search.PlaceholderText = Loc.T("search.placeholder", "  Pesquisar item por nome ou ID…"));
        Bind(() => { if (_store.Current is null) _status.Text = Loc.T("status.start", "Abra a pasta do Perfect World para começar."); });
        Text = "MyAceEditor";
        Width = 1320; Height = 840;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        BackColor = CBg; ForeColor = CFg;
        Font = new Font("Segoe UI", 9.5f);

        Loc.Changed += () => { if (IsHandleCreated) BeginInvoke(ApplyLanguage); else ApplyLanguage(); };
        var tabs = BuildTabs();
        Controls.Add(tabs);
        Controls.Add(BuildNav(tabs));
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatus());

        Shown += (_, _) =>
        {
            if (_split is not null) _split.SplitterDistance = 430;
            var last = StartClean ? null : _startFile ?? LoadLast();
            if (last is not null && File.Exists(last)) LoadElements(last);
            CheckServer(ask: true);   // o servidor local ficou para trás desde a última vez? (ex.: salvo e não enviado)
        };
    }

    // ---------- topo ----------
    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = CSurface, ForeColor = CFg, Padding = new Padding(4, 2, 0, 2) };
        ToolStripMenuItem M() => new() { ForeColor = CFg };
        var arq = M(); var ed = M(); var aj = M(); var lg = M(); var view = M(); var tools = M();
        var findErrors = new ToolStripMenuItem { ForeColor = CFg, ShortcutKeys = Keys.Control | Keys.E }; findErrors.Click += (_, _) => FindErrors();
        tools.DropDownItems.Add(findErrors);
        var backupServer = new ToolStripMenuItem { ForeColor = CFg }; backupServer.Click += (_, _) => BackupServer();
        tools.DropDownItems.Add(backupServer);
        Bind(() => backupServer.Text = Loc.T("menu.backupServer", "Salvar cópia do servidor local…"));
        var translateFrom = new ToolStripMenuItem { ForeColor = CFg }; translateFrom.Click += (_, _) => TranslateFromOther();
        tools.DropDownItems.Add(translateFrom);
        Bind(() => translateFrom.Text = Loc.T("menu.translateFrom", "&Traduzir com outro elements.data…"));
        var translateClient = new ToolStripMenuItem { ForeColor = CFg }; translateClient.Click += (_, _) => TranslateFromClient();
        tools.DropDownItems.Add(translateClient);
        Bind(() => translateClient.Text = Loc.T("menu.translateClient", "Traduzir a partir de outro &cliente…"));
        var showUnused = _miShowUnused = new ToolStripMenuItem { ForeColor = CFg, CheckOnClick = true, Checked = !_hideUnused };
        showUnused.CheckedChanged += (_, _) =>
        {
            _hideUnused = !showUnused.Checked;
            SaveSetting("hide_unused", _hideUnused ? "1" : "0");
            if (_current is not null) FillGrid(_current);
        };
        view.DropDownItems.Add(showUnused);
        var openPw = new ToolStripMenuItem { ForeColor = CFg }; openPw.Click += (_, _) => OpenPwFolder();
        var openEl = new ToolStripMenuItem { ForeColor = CFg }; openEl.Click += (_, _) => OpenElements();
        var save = new ToolStripMenuItem { ForeColor = CFg, ShortcutKeys = Keys.Control | Keys.S }; save.Click += (_, _) => SaveCurrent();
        var saveAs = new ToolStripMenuItem { ForeColor = CFg }; saveAs.Click += (_, _) => SaveElementsAs();
        // outro MyAce ao lado deste, sem nada aberto: para editar mais de um elements ao mesmo tempo
        var clean = new ToolStripMenuItem { ForeColor = CFg };
        clean.Click += (_, _) => System.Diagnostics.Process.Start(Environment.ProcessPath!, "--clean");
        var exit = new ToolStripMenuItem { ForeColor = CFg }; exit.Click += (_, _) => Close();
        var exportCsv = new ToolStripMenuItem { ForeColor = CFg }; exportCsv.Click += (_, _) => ExportBlockCsv();
        var convert = new ToolStripMenuItem { ForeColor = CFg }; convert.Click += (_, _) => SaveOtherVersion();
        Bind(() => convert.Text = Loc.T("menu.convert", "Salvar em &outra versão…"));
        // salvar automático: grava tudo o que mudou a cada 5 minutos, sem perguntar (cada arquivo ganha cópia de segurança)
        var autoSave = new ToolStripMenuItem { ForeColor = CFg, CheckOnClick = true, Checked = LoadSetting("autosave") == "1" };
        autoSave.CheckedChanged += (_, _) => { SaveSetting("autosave", autoSave.Checked ? "1" : "0"); _autoSave.Enabled = autoSave.Checked; };
        _autoSave.Tick += (_, _) => SaveAll(quiet: true);
        _autoSave.Enabled = autoSave.Checked;
        Bind(() => autoSave.Text = Loc.T("menu.autoSave", "Salvar &automaticamente (a cada 5 min)"));
        // restaurar backup (usuário 01/11): os 5 últimos dos arquivos do jogo aberto, com data e hora; ou um de outro lugar
        var restore = new ToolStripMenuItem { ForeColor = CFg };
        restore.DropDownItems.Add(new ToolStripMenuItem("…"));   // a setinha aparece; a lista é feita ao abrir
        restore.DropDownOpening += (_, _) => FillRestoreMenu(restore);
        var openBak = new ToolStripMenuItem { ForeColor = CFg }; openBak.Click += (_, _) => OpenBackup();
        Bind(() => restore.Text = Loc.T("menu.restore", "&Restaurar backup"));
        Bind(() => openBak.Text = Loc.T("menu.openBackup", "A&brir backup…"));
        arq.DropDownItems.AddRange(new ToolStripItem[] { openPw, openEl, new ToolStripSeparator(), save, saveAs, convert, autoSave, new ToolStripSeparator(), restore, openBak, new ToolStripSeparator(), exportCsv, clean, exit });
        Bind(() => exportCsv.Text = Loc.T("menu.exportCsv", "&Exportar bloco atual (planilha CSV)…"));
        var newItem = new ToolStripMenuItem { ForeColor = CFg }; newItem.Click += (_, _) => AddItem();
        var removeItem = new ToolStripMenuItem { ForeColor = CFg }; removeItem.Click += (_, _) => RemoveItem();
        var undo = new ToolStripMenuItem { ForeColor = CFg, ShortcutKeyDisplayString = "Ctrl+Z" }; undo.Click += (_, _) => UndoRedo(undo: true);
        var redo = new ToolStripMenuItem { ForeColor = CFg, ShortcutKeyDisplayString = "Ctrl+Y" }; redo.Click += (_, _) => UndoRedo(undo: false);
        var history = new ToolStripMenuItem { ForeColor = CFg }; history.Click += (_, _) => ShowHistory();
        ed.DropDownItems.AddRange(new ToolStripItem[] { undo, redo, history, new ToolStripSeparator(), newItem, removeItem });
        void UpdateUndoMenu()
        {
            undo.Enabled = _edits.CanUndo; redo.Enabled = _edits.CanRedo;
            undo.Text = Loc.T("menu.undo", "&Desfazer") + (_edits.UndoLabel is { Length: > 0 } u ? $": {u}" : "");
            redo.Text = Loc.T("menu.redo", "&Refazer") + (_edits.RedoLabel is { Length: > 0 } r ? $": {r}" : "");
            history.Text = Loc.T("menu.history", "&Histórico de ações…");
        }
        _edits.Changed += UpdateUndoMenu;
        _edits.Changed += () => _typeCache.Clear();   // tipo/subtipo de um item pode ter mudado
        var about = new ToolStripMenuItem { ForeColor = CFg };
        about.Click += (_, _) => MessageBox.Show(Loc.T("menu.aboutText", "MyAceEditor — editor dos dados do Perfect World."), Loc.T("menu.about", "Sobre"));
        aj.DropDownItems.Add(about);
        foreach (var (code, name) in AppLang.All)
        {
            var mi = new ToolStripMenuItem(name) { ForeColor = CFg, Checked = AppLang.Current == code, Tag = code, Image = FlagIcons.Get(code) };
            mi.Click += (_, _) => AppLang.Current = code;
            lg.DropDownItems.Add(mi);
        }
        AppLang.Changed += () => { foreach (ToolStripMenuItem x in lg.DropDownItems) x.Checked = (string?)x.Tag == AppLang.Current; };
        // seletor de idioma: só a bandeira do idioma atual (reconhecível em qualquer língua), depois de Ajuda;
        // "Idioma" traduzido fica na dica, e cada língua aparece pelo próprio nome e bandeira
        lg.DisplayStyle = ToolStripItemDisplayStyle.Image;
        Bind(() =>
        {
            lg.Text = lg.ToolTipText = Loc.T("lang.menu", "Idioma");
            lg.Image = FlagIcons.Get(AppLang.Current);
            arq.Text = Loc.T("menu.file", "&Arquivo");
            openPw.Text = Loc.T("menu.openPw", "Abrir &pasta do Perfect World…");
            openEl.Text = Loc.T("menu.openElements", "Abrir &elements.data…");
            save.Text = Loc.T("menu.saveAll", "&Salvar tudo (elements, quests, loja, npcgen)");
            saveAs.Text = Loc.T("menu.saveAs", "Salvar &como…");
            clean.Text = Loc.T("menu.newWindow", "&Nova janela do MyAce");
            exit.Text = Loc.T("menu.exit", "Sa&ir");
            ed.Text = Loc.T("menu.edit", "&Editar");
            newItem.Text = Loc.T("menu.newItem", "&Novo item");
            removeItem.Text = Loc.T("menu.removeItem", "&Remover item");
            UpdateUndoMenu();
            aj.Text = Loc.T("menu.help", "A&juda");
            about.Text = Loc.T("menu.about", "Sobre");
            view.Text = Loc.T("menu.view", "E&xibir");
            showUnused.Text = Loc.T("menu.showUnused", "Mostrar campos sem uso no bloco");
            tools.Text = Loc.T("menu.tools", "&Ferramentas");
            findErrors.Text = Loc.T("menu.findErrors", "Procurar &erros…");
        });
        // Exportar log: isolado na ponta direita da linha dos menus (quem testa manda o .zip para nós)
        var exportLog = new ToolStripMenuItem { ForeColor = CAccent, Font = new Font("Segoe UI Semibold", 9f), Alignment = ToolStripItemAlignment.Right, Margin = new Padding(0, 0, 8, 0) };
        exportLog.Click += (_, _) => ExportLog();
        Bind(() => { exportLog.Text = Loc.T("menu.exportLog", "Exportar log"); exportLog.ToolTipText = Loc.T("menu.exportLogTip", "Junta o log de erros do programa num .zip para enviar a quem desenvolve"); });
        // Preferências: abre a tela de preferências (uma linha com interruptor por opção; PreferencesWindow)
        var prefs = M();
        prefs.Click += (_, _) => OpenPreferences();
        Bind(() => prefs.Text = Loc.T("menu.prefs", "&Preferências"));
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, view, tools, prefs, aj, exportLog });   // idioma: botão grande na barra de cima (BuildToolbar)
        MainMenuStrip = menu;
        return menu;
    }

    // Preferências → trava de velocidade do gs (pwctl speedcap / speedcap-set): o estado vem do próprio gs instalado
    private ServerAdmin? PrefsAdmin() => _serverView is null || !LocalServer.DistroExists() ? null : new ServerAdmin(_serverView.CreateServer());

    /// <summary>Tela de Preferências. Preferência nova = mais um Pref na lista (seção, ícone, nome, explicação, ler, aplicar).</summary>
    private void OpenPreferences()
    {
        var server = Loc.T("prefs.secServer2", "Servidor");
        var editor = Loc.T("prefs.secEditor2", "MyAce");
        var list = new List<PreferencesWindow.Pref>
        {
            // ligado = trava REMOVIDA (o que o usuário liga é "velocidade sem limite"); o gs instalado diz o estado real
            new(server, "\uE945",
                () => Loc.T("prefs.speedCapTitle", "Velocidade de movimento sem a trava de 3 / 5 m/s"),
                () => Loc.T("prefs.speedCapDetail", "Ligado: os bônus fixos de movimento dos equipamentos somam sem limite (até os 15 m/s do jogo). Desligado: regra original (acima de 3 vira 3; acima de 5 vira 0). Reinicia os mapas."),
                () => PrefsAdmin()?.SpeedCap() switch
                {
                    "off" => (true, null), "on" => (false, null),
                    "none" => (null, Loc.T("prefs.speedCapNone", "O gs deste servidor é de uma versão em que o trecho da trava não foi encontrado: nada é alterado.")),
                    _ => (null, Loc.T("srv.localServerNotInstalledYet", "Servidor local ainda não instalado.")),
                },
                on => PrefsAdmin()?.SetSpeedCap(on: !on) is { } r ? (r.Output.Contains("##OK") ? null : Loc.T("prefs.speedCapFail", "Não consegui alterar a trava: ") + r.Output.Trim())
                    : Loc.T("srv.localServerNotInstalledYet", "Servidor local ainda não instalado."),
                on => on ? Loc.T("prefs.speedCapAskOff", "Remover a trava de velocidade do servidor local?\n\nO gs é alterado (2 bytes; o original fica guardado) e os mapas reiniciam: quem estiver no jogo cai.")
                         : Loc.T("prefs.speedCapAskOn", "Voltar a trava original de velocidade?\n\nOs mapas reiniciam: quem estiver no jogo cai.")),
            new(editor, "\uE8F4",
                () => Loc.T("prefs.unusedTitle", "Mostrar campos sem uso"),
                () => Loc.T("prefs.unusedDetail", "Ligado: o editor de elements mostra todos os campos, até os que estão zerados em todos os itens do bloco. Desligado: esses campos ficam recolhidos (uma linha avisa quantos)."),
                () => (!_hideUnused, null),
                on => { if (_miShowUnused is { } mi) mi.Checked = on; return null; }),
            new(editor, "",
                () => Loc.T("prefs.srvAskTitle", "Perguntar se quer enviar ao servidor"),
                () => Loc.T("prefs.srvAskDetail", "Ligado: ao abrir e ao salvar, se o servidor local estiver diferente do cliente, pergunta se quer enviar agora. Desligado: não pergunta; o botão \"Salvar e enviar ao servidor\" fica verde e a dica dele lista o que falta."),
                () => (LoadSetting("srvdiff-ask") == "1", null),
                on => { SaveSetting("srvdiff-ask", on ? "1" : "0"); return null; }),
        };
        // editor primeiro (o que muda só o MyAce), depois o que mexe no servidor
        list = list.OrderBy(p => p.Section == editor ? 0 : 1).ToList();
        // PCK Key (chaves dos pacotes) entre MyAce e Servidor (usuário 10/11)
        using var w = new PreferencesWindow(list, new Dictionary<string, string> { [editor] = UiKit.Settings, [server] = UiKit.Server },
            [(Loc.T("prefs.secPck", "PCK Key"), "", new PckKeysPage())], editor);
        w.ShowDialog(this);
    }

    // Ferramentas → Salvar cópia do servidor local: pasta no formato "server files" (backup ou base para outro servidor)
    private void BackupServer()
    {
        if (!LocalServer.DistroExists() || _serverView is null) { Status(Loc.T("srv.localServerNotInstalledYet", "Servidor local ainda não instalado.")); return; }
        using var pick = new FolderBrowserDialog { Description = Loc.T("bk.pick", "Pasta onde salvar a cópia do servidor (vira a pasta \"server files\": home, etc, lib, SQL)"), UseDescriptionForTitle = true };
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        var folder = Path.Combine(pick.SelectedPath, $"servidor-local-{DateTime.Now:yyyyMMdd-HHmm}");
        if (MessageBox.Show(this, string.Format(Loc.T("bk.confirm", "Salvar o servidor local em:\n{0}\n\nO servidor para durante a cópia (alguns minutos) e volta ao fim. Quem estiver no jogo cai."), folder),
                Loc.T("menu.backupServer", "Salvar cópia do servidor local…"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var srv = _serverView.CreateServer();
        try
        {
            ReadProgressDialog.Run(this, Loc.T("menu.backupServer", "Salvar cópia do servidor local…"), ctx =>
            {
                srv.Backup(folder, st => ctx.Report(0, st, indeterminate: true, force: true));
                return folder;
            }, allowCancel: false);
            MessageBox.Show(this, string.Format(Loc.T("bk.done", "Cópia salva em:\n{0}\n\nPara usar em outro servidor: aba Servidor → Pacote do servidor = esta pasta (a pasta SQL está dentro dela)."), folder),
                Loc.T("menu.backupServer", "Salvar cópia do servidor local…"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Error(Loc.T("bk.failed", "Não consegui salvar a cópia do servidor"), ex); }
    }

    private void ExportLog()
    {
        using var dlg = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = $"myaceeditor-log-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var env = new System.Text.StringBuilder()
            .AppendLine($"MyAceEditor {Application.ProductVersion}")
            .AppendLine($"Windows: {Environment.OSVersion} · {(Environment.Is64BitOperatingSystem ? "64" : "32")} bits · .NET {Environment.Version}")
            .AppendLine($"idioma: {AppLang.Current}")
            .AppendLine($"elements aberto: {(_dataPath is null ? "-" : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)))))} · v{(_store.Current?.Version ?? 0) & 0xFFFF}")
            .AppendLine($"tasks: v{_tasks?.Version}")
            .AppendLine($"servidor local: {(LocalServer.DistroExists() ? $"instalado, gs v{LocalServer.InstalledVersion()}" : "não instalado")}");
        try { AppLog.Export(dlg.FileName, env.ToString()); Status(string.Format(Loc.T("status.logExported", "Log exportado: {0}"), Path.GetFileName(dlg.FileName))); }
        catch (Exception ex) { Error(Loc.T("status.logExportFail", "Não consegui exportar o log"), ex); }
    }

    private ToolStrip BuildToolbar()
    {
        var tb = UiKit.Ribbon();
        UiKit.Cmd(tb, UiKit.Back, "tb.rBack", "Voltar", () => Navigate(-1), "tb.back", "Voltar (item anterior)");
        UiKit.Cmd(tb, UiKit.Forward, "tb.rForward", "Avançar", () => Navigate(+1), "tb.forward", "Avançar");
        UiKit.Sep(tb);
        UiKit.Cmd(tb, UiKit.Folder, "tb.rOpenPw", "Abrir pasta do PW", OpenPwFolder, "tb.rOpenPwTip", "Abre a pasta do jogo: todas as áreas passam a usar ela.");
        UiKit.Cmd(tb, UiKit.OpenFile, "tb.rOpenEl", "Abrir elements", OpenElements, "tb.openElements", "Abrir elements.data");
        UiKit.Cmd(tb, UiKit.Save, "tb.rSave", "Salvar", SaveCurrent, "tb.rSaveTip", "Grava tudo o que mudou (Ctrl+S), com backup fora do cliente.");
        _sendBtn = UiKit.Cmd(tb, UiKit.Upload, "tb.rSaveSend", "Salvar e enviar", SaveAndSend, "tb.saveSendTip", "Grava tudo o que mudou e leva para o servidor local (aba Servidor), reiniciando-o", primary: true);
        Bind(UpdateSendGlow);
        _glowTimer.Tick += (_, _) => UpdateSendGlow();
        _glowTimer.Start();
        UiKit.Sep(tb);
        UiKit.Cmd(tb, UiKit.Add, "tb.rAdd", "Adicionar", AddItem, "tb.rAddTip", "Novo registro no bloco aberto.");
        UiKit.Cmd(tb, UiKit.Delete, "tb.rDelete", "Excluir", RemoveItem, "tb.rDeleteTip", "Exclui o registro escolhido (Ctrl+Z desfaz).");
        UiKit.Cmd(tb, UiKit.People, "tb.rCreature", "Monstro/NPC novo", () => { if (_tabs is not null) _tabs.SelectedIndex = 0; CreateCreatureWizard(this, NpcGenView.Kind.Monster); }, "tb.rCreatureTip", "Cria um monstro ou NPC a partir de um modelo, com ID livre.");
        // idioma do programa: bandeira grande à direita, com o nome do idioma; a lista mostra cada um com a sua bandeira
        var lang = new ToolStripDropDownButton { Alignment = ToolStripItemAlignment.Right, TextImageRelation = TextImageRelation.ImageAboveText, Padding = new Padding(6, 2, 6, 2), ForeColor = CFg };
        static Image Big(Image? small)
        {
            var b = new Bitmap(30, 20);
            if (small is null) return b;
            using var g = Graphics.FromImage(b); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, new Rectangle(0, 0, 30, 20)); using var p = new Pen(Color.FromArgb(60, 0, 0, 0)); g.DrawRectangle(p, 0, 0, 29, 19);
            return b;
        }
        foreach (var (code, name) in AppLang.All)
        {
            var mi = new ToolStripMenuItem(name) { Image = FlagIcons.Get(code), Tag = code };
            mi.Click += (_, _) => AppLang.Current = code;
            lang.DropDownItems.Add(mi);
        }
        Bind(() =>
        {
            lang.Image = Big(FlagIcons.Get(AppLang.Current));
            lang.Text = AppLang.All.FirstOrDefault(x => x.Item1 == AppLang.Current).Item2 ?? AppLang.Current;
            lang.ToolTipText = Loc.T("tb.langTip", "Idioma do programa (traduzir a tela)");
            foreach (ToolStripMenuItem x in lang.DropDownItems) x.Checked = (string?)x.Tag == AppLang.Current;
        });
        tb.Items.Add(lang);
        // servidor e pastas do cliente aberto: ao lado da bandeira (à esquerda dela), um lugar só para os caminhos
        var serverBtn = UiKit.Cmd(tb, UiKit.Server, "tb.rServerSetup", "Servidor", () => { if (_tabs is not null && _tabServer is not null) { _tabs.SelectedTab = _tabServer; if (_serverPages is not null) _serverPages.SelectedIndex = 0; } }, "tb.rServerTip", "Servidor de teste: ligar, enviar arquivos, pastas, verificar e corrigir");
        serverBtn.Alignment = ToolStripItemAlignment.Right;   // à esquerda da bandeira, com leve destaque
        serverBtn.BackColor = Color.FromArgb(222, 233, 251); serverBtn.ForeColor = UiKit.Accent;
        serverBtn.Font = new Font("Segoe UI Semibold", 9f); serverBtn.Image = UiKit.Icon(UiKit.Server, UiKit.Accent, 22);
        UiKit.Cmd(tb, UiKit.Globe, "tb.rWorld", "Mundo 3D", () => { if (_npcgen?.OpenWorldOverviewHere() == true) return; if (_tabs is not null && _tabNpcGen is not null) _tabs.SelectedTab = _tabNpcGen; BeginInvoke(() => _npcgen?.OpenWorldOverview()); }, "tb.rWorldTip", "Abre o mapa do jogo em 3D com os pontos do NPCGen.");
        return tb;
    }

    private TabControl BuildTabs()
    {
        // as páginas continuam num TabControl, mas sem cabeçalhos: quem troca de página é a navegação lateral (BuildNav)
        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1) };
        var tEl = new TabPage() { BackColor = CBg };
        Bind(() => tEl.Text = Loc.T("tab.elements", "Elements"));
        tEl.Controls.Add(BuildElements());
        tabs.TabPages.Add(tEl);
        var tQ = new TabPage() { BackColor = CBg };
        _tabs = tabs; _tabQuests = tQ;
        Bind(() => tQ.Text = Loc.T("tab.quests", "Quests"));
        tQ.Controls.Add(BuildQuestView());
        tabs.TabPages.Add(tQ);
        var tG = new TabPage() { BackColor = CBg };
        _tabGShop = tG;
        Bind(() => tG.Text = Loc.T("tab.gshop", "GShop"));
        tG.Controls.Add(_gshop = new GShopView(
            () => _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)),   // pasta element
            id => _refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? IconFor(h.Item) : null,
            id => _refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? (TasksFile.SplitColor(h.Item.Name).Text, NameColor(h.Item)) : null,
            PickShopItem));
        _gshop.ItemDescription = id => _cfgs is not null && _cfgs.ItemDescs.TryGetValue(id, out var d) ? d : null;
        tabs.TabPages.Add(tG);
        var tN = new TabPage() { BackColor = CBg };
        _tabNpcGen = tN;
        Bind(() => tN.Text = Loc.T("tab.npcgen", "NPCGen"));
        _edits.Changed += () => _creatureCache = null;   // nome/modelo mudados no editor aparecem no 3D na hora
        tN.Controls.Add(_npcgen = new NpcGenView(
            id => CreatureItem(id) is { } c ? IconFor(c.Item) : null,
            id => CreatureItem(id) is { } c ? (TasksFile.SplitColor(c.Item.Name).Text, NameColor(c.Item)) : null,
            PickSpawn,
            () => _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)),   // pasta element
            id => CreatureItem(id) is { } c && c.Item.Fields.TryGetValue("file_model", out var fm)
                && fm.Value.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase) ? fm.Value : null,
            id => CreatureItem(id)?.Block.Name,
            id =>
            {
                // "Editar no software": mostra o registro na aba Elements e traz a janela principal
                if (CreatureItem(id) is not { } he) return;
                if (_tabs is not null) _tabs.SelectedIndex = 0;
                GoToItem(he.Item);
                Activate();
            }));
        tabs.TabPages.Add(tN);
        _npcgen.CreateCreature = (owner, kind) => { var id = CreateCreatureWizard(owner, kind); return id; };
        _npcgen.QuestsOfNpc = NpcQuests;
        // Mundo 3D, fim do menu de ferramentas: salvar tudo e enviar, com a janela principal na aba Servidor
        // Mundo 3D gravou algo que o servidor precisa: entra na lista do brilho
        PerfectWorld.ServerNotice.Saved += file => BeginInvoke(() => { _unsent.Add(file); UpdateSendGlow(); });
        _npcgen.SaveAndSendAll = () =>
        {
            SaveAndSend();
            // depois que o clique no Mundo 3D termina (senão ele volta a ficar na frente): editor na aba Servidor
            BeginInvoke(() =>
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (_tabs is not null && _tabServer is not null) _tabs.SelectedTab = _tabServer;
                Activate(); BringToFront();
            });
        };
        // painel da criatura no Mundo 3D: quests que usam o monstro/NPC (roda fora da tela)
        // NPC comum não tem IA no gs (npcgenerator.cpp: id_type 3214 = normal, aipolicy 0): não anda nem patrulha; só guarda (3216)
        _npcgen.PatrolProblem = id => CreatureItem(id) is { Block.Name: "NPC_ESSENCE" } c && c.Item.Fields.GetValueOrDefault("id_type")?.Value != "3216"
            ? Loc.T("path.npcNoAi", "NPC comum fica parado no jogo: o servidor não dá IA a ele, então não anda na área nem patrulha. Só NPC do tipo guarda (tipo 3216) anda.") : null;
        _npcgen.PickCreature = PickCreature;
        _npcgen.CreatureQuests = id => _tasks is { } tq && CreatureItem(id) is { } ci ? QuestUsesOf(tq, ci.Block.Name == "NPC_ESSENCE" ? 'N' : 'M', id.ToString()) : null;
        _npcgen.DeletedMany += n => Status(string.Format(Loc.T("npcgen.deletedMany", "{0} ponto(s) excluído(s). Ctrl+Z desfaz."), n));
        _npcgen.QuestHost = new QuestHostImpl(this);
        _npcgen.InhabitOf = id => CreatureItem(id) is { Block.Name: "MONSTER_ESSENCE" } c && int.TryParse(c.Item.Fields.GetValueOrDefault("inhabit_type")?.Value, out var ih) ? ih : 0;
        _npcgen.Announce = OpenAnnouncements;
        _npcgen.DropsOf = DropsOfCreature;
        _npcgen.EditDrops = (owner, id) => { if (CreatureItem(id) is { Block.Name: "MONSTER_ESSENCE" } c) OpenChanceEditor(c.Item, drops: true, owner); };
        _npcgen.IsMerchant = id => CreatureItem(id) is { } c && c.Item.Fields.TryGetValue("id_sell_service", out var sv) && sv.Value is not ("0" or "");
        _npcgen.OpenQuest = id => { GoToQuest(id); Activate(); };
        // servidor escolhido no NPCGen (pasta gamed\config) vira o servidor do programa todo
        _npcgen.ServerFolderPicked = config => { if (Path.GetDirectoryName(config) is { } gamed) SetServerFolder(gamed); };
        var tAi = new TabPage() { BackColor = CBg };
        Bind(() => tAi.Text = Loc.T("tab.aipolicy", "AI Policy"));
        _aiView = new AiPolicyView(CBg, CFg, CTreeBg, CTreeFg, CAccent);
        tAi.Controls.Add(_aiView);
        tabs.TabPages.Add(tAi);
        var tChk = new TabPage() { BackColor = CBg };
        _tabCheck = tChk;
        Bind(() => tChk.Text = Loc.T("tab.verify", "Verificação"));
        tChk.Controls.Add(BuildCheckView());
        var tSrv = new TabPage() { BackColor = CBg };
        _tabServer = tSrv;
        Bind(() => tSrv.Text = Loc.T("tab.server", "Servidor"));
        tSrv.Controls.Add(_serverView = new ServerView(CBg, CFg, CAccent,
            () => _chkClient.Text.Length > 0 ? VersionCheck.ClientElement(_chkClient.Text)
                : _dataPath is null ? "" : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "",
            () => _chkServer.Text, LoadSetting, SaveSetting) { AiPolicyPath = () => _aiView?.FilePath });
        // servidor recém-instalado: Verificação automática e, com erro ou aviso, oferece o Corrigir
        _serverView.Installed += () => BeginInvoke(() =>
        {
            if (_serverView.GameFolderChanged() is { } pkgHome) { _chkServer.Text = pkgHome; SaveSetting("check_server", pkgHome); }
            ShowCheck();
            var (err, warn) = RunCheck(0);
            if (err + warn > 0 && MessageBox.Show(this, string.Format(Loc.T("chk.afterInstall", "Servidor instalado. A Verificação achou {0} erro(s) e {1} aviso(s).\n\nCorrigir agora?"), err, warn),
                    Loc.T("fix.title", "Corrigir o par"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) FixPair();
        });
        ServerPaths.Changed += _ => BeginInvoke(ApplyServerPaths);
        _serverView.FilesSent += ok => { if (ok) { _unsent.Clear(); _serverDiff = null; } UpdateSendGlow(); };   // servidor recebeu tudo: apaga o brilho
        tabs.TabPages.Add(tSrv);
        // Servidor e Verificação numa tela só, em pílulas; a lateral fica só com as ferramentas (teste 15)
        var serverPages = new TabControl { Dock = DockStyle.Fill };
        var pSrv = new TabPage() { BackColor = CBg };
        Bind(() => pSrv.Text = Loc.T("tab.server", "Servidor"));
        pSrv.Controls.Add(_serverView);
        serverPages.TabPages.Add(pSrv); serverPages.TabPages.Add(tChk);
        tSrv.Controls.Add(serverPages);
        tSrv.Controls.Add(new SegmentBar(serverPages, pg => pg == tChk ? UiKit.CheckList : UiKit.Server));
        _serverPages = serverPages;
        _serverView.VerifyRequested += () => { ShowCheck(); RunCheck(0); };
        _serverView.PluginsRequested += () => { if (_tabs is not null && _tabPlugins is not null) _tabs.SelectedTab = _tabPlugins; };
        // Plugins de evento (docs/PLUGINS.md): programa do servidor que sorteia/chama eventos (usuário 01/11)
        var tPl = new TabPage() { BackColor = CBg };
        _tabPlugins = tPl;
        Bind(() => tPl.Text = Loc.T("tab.plugins", "Plugins"));
        tPl.Controls.Add(_pluginsView = new PluginsView());
        string? pickWhy = null;
        _pluginsView.PointSource = () => (() => _npcgen?.EventControllerForSelected(out pickWhy), () => pickWhy);
        _pluginsView.DeathSink = SetPluginDeathAnnounce;
        _pluginsView.RespawnSink = (map, gmId, on) => _npcgen is { } ng ? ng.SetEventRespawn(map, gmId, on) : Loc.T("pl.rsNoNpcgen", "Abra a pasta do PW antes (o NPCGen guarda o ponto).");
        tabs.TabPages.Add(tPl);
        // Interfaces (janelas do jogo) fica acima do Extrator PCK, que só extrai (usuário 01/11)
        var tUi = new TabPage() { BackColor = CBg };
        _tabUi = tUi;
        Bind(() => tUi.Text = Loc.T("tab.interfaces", "Interfaces"));
        tUi.Controls.Add(_uiArea = new InterfacesView(() => _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath))));
        tabs.TabPages.Add(tUi);
        var tPck = new TabPage() { BackColor = CBg };
        _tabPck = tPck;
        Bind(() => tPck.Text = Loc.T("tab.pck", "Extrator PCK"));
        tPck.Controls.Add(BuildPckView());
        tabs.TabPages.Add(tPck);
        return tabs;
    }

    /// <summary>Barra lateral com ícone e nome de cada área do programa; a dica diz o que se faz ali.</summary>
    private NavBar BuildNav(TabControl tabs)
    {
        var nav = new NavBar(tabs);
        (string Glyph, string Key, string Tip, string TipPt)[] pages =
        {
            (UiKit.Library, "tab.elements", "nav.elementsTip", "Itens, monstros, NPCs, habilidades: o elements.data"),
            (UiKit.Flag, "tab.quests", "nav.questsTip", "Missões (tasks.data)"),
            (UiKit.Cart, "tab.gshop", "nav.gshopTip", "Loja de cash, na mesma tela do jogo"),
            (UiKit.MapPin, "tab.npcgen", "nav.npcgenTip", "Onde nascem monstros, NPCs e recursos; Mundo 3D"),
            (UiKit.Robot, "tab.aipolicy", "nav.aiTip", "Comportamento dos monstros (aipolicy.data)"),
            (UiKit.Layers, "tab.interfaces", "nav.uiTip", "Janelas do jogo: editar na tela, importar, trocar com outro cliente, telas de carregamento"),
            (UiKit.Box, "tab.pck", "nav.pckTip2", "Pacotes .pck: abrir, ver e extrair arquivos"),
        };
        var tools = tabs.TabPages.Cast<TabPage>().Where(p => p != _tabServer && p != _tabPlugins).ToList();   // o Servidor abre pelo botão de cima, e os Plugins por dentro dele
        for (int i = 0; i < pages.Length && i < tools.Count; i++)
        {
            var (g, key, tip, tipPt) = pages[i];
            var page = tools[i];
            nav.Add(g, () => page.Text, () => Loc.T(tip, tipPt), page);
        }
        return nav;
    }

    private Control BuildElements()
    {
        _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = CLine, Panel1MinSize = 300, SplitterDistance = 430 };

        // ESQUERDA (estilo Rody): seletor de bloco, filtro de tipo, busca e lista plana — fundo preto como no jogo
        var left = new Panel { Dock = DockStyle.Fill, BackColor = CTreeBg, Padding = new Padding(8) };
        foreach (var c in new Control[] { _blockCombo, _typeCombo })
        {
            var cb = (ComboBox)c;
            cb.FlatStyle = FlatStyle.Flat; cb.BackColor = CTreeInput; cb.ForeColor = CTreeFg; cb.Font = new Font("Segoe UI", 10);
            cb.MaxDropDownItems = 24;
            cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = 22;
            cb.DrawItem += DrawDarkCombo;
        }
        _blockCombo.SelectedIndexChanged += (_, _) => OnBlockChanged();
        // abrir a lista de blocos mostra o seletor com busca, categorias e ícones (mais fácil de achar)
        _blockCombo.DropDownHeight = 1;
        _blockCombo.DropDown += (_, _) => BeginInvoke(() => { _blockCombo.DroppedDown = false; ShowBlockPicker(); });
        _typeCombo.SelectedIndexChanged += (_, _) => RefreshView();

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 4, 0, 0), BackColor = CTreeBg };
        _search.Dock = DockStyle.Fill; _search.BackColor = CTreeInput; _search.ForeColor = CTreeFg; _search.Font = new Font("Segoe UI", 10);
        // busca só depois de uma pausa na digitação, e a caixa continua com o foco (antes cada letra abria o 1º
        // resultado, a caixa perdia o foco e "8617" virava "88")
        var searchDelay = _searchDelay;
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            bool had = _search.Focused;
            RefreshView();
            if (had && !_search.Focused) { _search.Focus(); _search.SelectionStart = _search.TextLength; }
        };
        _search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        _searchAll.ForeColor = CTreeFg; _searchAll.BackColor = CTreeBg; _searchAll.AutoSize = true; _searchAll.Dock = DockStyle.Right; _searchAll.Padding = new Padding(6, 3, 0, 0);
        Bind(() => _searchAll.Text = Loc.T("list.searchAllShort", "em todos os blocos"));
        _searchAll.CheckedChanged += (_, _) => RefreshView();
        Bind(() => _tip.SetToolTip(_searchAll, Loc.T("list.searchAll", "Buscar em todos os blocos")));
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(_searchAll);

        _list.BackColor = CTreeBg; _list.ForeColor = CTreeFg; _list.Font = new Font("Segoe UI", 10);
        _list.SmallImageList = _treeIcons;
        _list.Columns.Add("ID", 70, HorizontalAlignment.Right);
        _list.Columns.Add("Nome", 218);   // cabe ao lado de ID e Vínculos sem rolagem horizontal
        _list.Columns.Add(Loc.T("list.links", "Vínculos"), 84, HorizontalAlignment.Right);   // quantos itens usam este
        // a coluna Nome ocupa o que sobra (sem barra de rolagem horizontal)
        _list.ClientSizeChanged += (_, _) =>
        {
            int w = _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[2].Width - 2;
            if (w > 80 && _list.Columns[1].Width != w) _list.Columns[1].Width = w;
        };
        Bind(UpdateCounters);
        _list.RetrieveVirtualItem += OnRetrieveItem;
        _list.DrawColumnHeader += OnDrawHeader;
        _list.DrawSubItem += OnDrawSubItem;
        _list.ColumnClick += (_, e) => OnColumnClick(e.Column);
        _list.SelectedIndexChanged += (_, _) => OnListSelect();
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_list, true);   // sem piscar
        _list.ItemActivate += (_, _) => { if (_current is not null && TypeMembers(_current) is not null) OpenTypeFilter(_current); };
        _list.VirtualItemsSelectionRangeChanged += (_, _) => OnListSelect();
        _list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A) { SelectAllItems(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { if (_list.SelectedIndices.Count > 1) BulkDelete(); else RemoveItem(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.N) { BulkClone(); e.Handled = true; }
        };
        _list.Resize += (_, _) => _list.Columns[1].Width = Math.Max(120, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[2].Width - 1);
        _list.HandleCreated += (_, _) => SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);
        _tip.OwnerDraw = true; _tip.Draw += DrawTip; _tip.Popup += SizeTip;
        _list.MouseMove += OnListHover;
        _list.MouseLeave += (_, _) => { _tip.Hide(_list); _hoverIdx = -1; };
        BuildContextMenu();
        _list.ContextMenuStrip = _ctx;

        // rodapé: Adicionar | ir para ID | Excluir
        var foot = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 38, ColumnCount = 3, BackColor = CTreeBg, Padding = new Padding(0, 6, 0, 0) };
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        Button FootBtn(string t, Action a) { var b = new Button { Text = t, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = CTreeInput, ForeColor = CTreeFg, Margin = new Padding(0, 0, 4, 0) }; b.FlatAppearance.BorderColor = Color.FromArgb(60, 64, 72); b.Click += (_, _) => a(); return b; }
        _gotoId.BackColor = CTreeInput; _gotoId.ForeColor = CTreeFg; _gotoId.Dock = DockStyle.Fill; _gotoId.Margin = new Padding(0, 2, 4, 0);
        _gotoId.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GoToId(); } };
        Bind(() => _tip.SetToolTip(_gotoId, Loc.T("footer.goto", "Ir para o ID (Enter)")));
        var footAdd = FootBtn("＋ Adicionar", AddItem); foot.Controls.Add(footAdd, 0, 0);
        Bind(() => footAdd.Text = Loc.T("footer.add", "＋ Adicionar"));
        foot.Controls.Add(_gotoId, 1, 0);
        var footDel = FootBtn("✕ Excluir", RemoveItem); foot.Controls.Add(footDel, 2, 0);
        Bind(() => footDel.Text = Loc.T("footer.delete", "✕ Excluir"));

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = CTreeBg };
        listHost.Controls.Add(_list); listHost.Controls.Add(_recentList);
        BuildRecentList();
        // "Todos os itens" (bloco + busca) ou "Recentes" (o que você abriu por último, de qualquer bloco)
        var mode = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 3, BackColor = CTreeBg, Padding = new Padding(0, 0, 0, 6) };
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        RadioButton Mode(string glyph, Func<string> t)
        {
            var r = new RadioButton { Appearance = Appearance.Button, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, ForeColor = CTreeFg, BackColor = CTreeInput, Margin = new Padding(0, 0, 4, 0),
                TextAlign = ContentAlignment.MiddleCenter, Image = UiKit.Icon(glyph, CTreeFg, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Cursor = Cursors.Hand };
            r.FlatAppearance.BorderColor = CTreeInput; r.FlatAppearance.CheckedBackColor = CAccent;
            Bind(() => r.Text = t());
            return r;
        }
        var all = Mode("\uE8FD", () => Loc.T("list.modeAll", "Todos os itens"));
        _favModeBtn = Mode("\uE734", FavModeText);
        _recentMode = Mode("\uE81C", () => _recentKeys.Count > 0 ? string.Format(Loc.T("list.modeRecentN", "Recentes ({0})"), _recentKeys.Count) : Loc.T("list.modeRecent", "Recentes"));
        all.Checked = true;
        mode.Controls.Add(all, 0, 0); mode.Controls.Add(_favModeBtn, 1, 0); mode.Controls.Add(_recentMode, 2, 0);
        void ApplyMode()
        {
            bool rec = _recentMode.Checked;
            _favMode = _favModeBtn.Checked;
            _list.Visible = !rec; _recentList.Visible = rec;
            searchRow.Visible = !rec; _blockCombo.Visible = !rec && !_favMode; _typeCombo.Visible = !rec && !_favMode && _typeCombo.Tag is true;
            if (rec) FillRecent(); else RefreshView();
        }
        foreach (var r in new[] { all, _favModeBtn, _recentMode }) r.CheckedChanged += (_, _) => { if (r.Checked) ApplyMode(); };
        left.Controls.Add(listHost);
        left.Controls.Add(foot);
        left.Controls.Add(searchRow);
        left.Controls.Add(_typeCombo);
        left.Controls.Add(_blockCombo);
        left.Controls.Add(mode);
        _favModeBtn.Text = FavModeText();   // o Bind acima rodou antes de carregar: mostrava "Favoritos (0)"
        _split.Panel1.Controls.Add(left);

        // DIREITA: cabeçalho (ícone + nome) + campos
        var right = new Panel { Dock = DockStyle.Fill, BackColor = CSurface };
        // cabeçalho estilo Rody: ícone | ID | nome na cor do jogo sobre fundo escuro
        _header.BackColor = CTreeBg; _header.ForeColor = CTreeFg;
        var headPanel = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = CTreeBg };
        _iconBox.BackColor = CTreeBg;
        _idLabel.BackColor = CTreeInput; _idLabel.ForeColor = CListId;
        headPanel.Controls.Add(_header);
        headPanel.Controls.Add(_idLabel);
        headPanel.Controls.Add(_iconBox);
        StyleGrid();
        var tVal = new TabPage() { BackColor = CSurface };
        Bind(() => tVal.Text = Loc.T("detail.values", "Valores"));
        tVal.Controls.Add(_grid);
        tVal.Controls.Add(_creaturePanel);   // secoes Spawn e Quests (no lugar da grade)
        _creaturePanel.Visible = false;
        _creaturePanel.OpenQuest = id => GoToQuest(id);
        _creaturePanel.Apply = ApplySpawn;
        // botões dos editores em janela, visíveis só quando o item tem o que editar
        _editorBar.BackColor = CSurface;
        foreach (var b in new[] { _btnAddonEd, _btnDropEd })
        {
            b.FlatStyle = FlatStyle.Flat; b.BackColor = CAccent; b.ForeColor = Color.White; b.AutoSize = true; b.Height = 28; b.Margin = new Padding(0, 0, 8, 0);
            b.FlatAppearance.BorderSize = 0; b.Cursor = Cursors.Hand;
            _editorBar.Controls.Add(b);
        }
        _btnAddonEd.Click += (_, _) => { if (_current is not null) OpenChanceEditor(_current, drops: false); };
        _btnDropEd.Click += (_, _) => { if (_current is not null) OpenChanceEditor(_current, drops: true); };
        Bind(() =>
        {
            _btnAddonEd.Text = Loc.T("btn.addonEditor", "✎ Editor de addons e chances");
            _btnDropEd.Text = Loc.T("btn.dropEditor", "✎ Editor de drops");
            _tip.SetToolTip(_btnAddonEd, Loc.T("tip.addonEditor", "Soquetes, quantidade de addons e listas de addons com barras de chance (também no clique direito da grade)"));
            _tip.SetToolTip(_btnDropEd, Loc.T("tip.dropEditor", "Chance de cair 0–3 itens e a lista de drops com ícones e barras (também no clique direito da grade)"));
        });
        tVal.Controls.Add(_editorBar);
        // seções do item (Básico, Requisitos, Ataque…) numa coluna à esquerda: cabem todas, com ícone e contagem
        _sections.GlyphOf = o => o is SectionItem si ? SectionGlyph(si.Page.Tag as string ?? "") : null;
        tVal.Controls.Add(_sections);
        _sections.SelectedIndexChanged += (_, _) =>
        {
            if (_syncSections || _sections.SelectedItem is not SectionItem si) return;
            _groupTabs.SelectedTab = si.Page;
            if (_current is not null) FillGrid(_current);
        };
        _groupTabs.SelectedIndexChanged += (_, _) => { if (_current is not null) FillGrid(_current); };
        // Buscar por campo (no bloco atual)
        var tFind = new TabPage() { BackColor = CSurface };
        Bind(() => tFind.Text = Loc.T("detail.search", "Buscar"));
        // Pesquisa avançada: onde (bloco/todos), campo (ou qualquer), condição, valor(es); ações sobre os resultados
        var findBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0), BackColor = CSurface, WrapContents = false };
        var findBtn = new Button { Height = 26, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White };
        findBtn.Click += (_, _) => RunFind();
        foreach (var tb in new[] { _findValue, _findValue2 })
            tb.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunFind(); } };
        _findOp.SelectedIndexChanged += (_, _) => _findValue2.Visible = _findOp.SelectedIndex >= 0 && FindOps[_findOp.SelectedIndex].Op == SearchOp.Between;
        _findScope.SelectedIndexChanged += (_, _) => { if (_current is not null) FillFindFields(_current, force: true); };
        _findField.DropDownWidth = 360; _findField.Width = 240;
        findBar.Controls.AddRange(new Control[] { _findScope, _findField, _findOp, _findValue, _findValue2, findBtn, _findKeep });
        var findActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(6, 5, 6, 0), BackColor = CSurface, WrapContents = false };
        Button FindAct(Action a) { var b = new Button { Height = 26, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = CSurface2, ForeColor = CFg }; b.Click += (_, _) => a(); findActions.Controls.Add(b); return b; }
        var actBulk = FindAct(() => { var hits = FindSelectedHits(); if (hits.Count > 0) BulkChangeOn(hits.Select(h => h.Item).Distinct().ToList(), hits[0].Field); });
        var actIds = FindAct(() => { var hits = FindSelectedHits(); if (hits.Count > 0) { Clipboard.SetText(string.Join(Environment.NewLine, hits.Select(h => h.Item.Id).Distinct())); Status(string.Format(Loc.T("find.copied", "{0} ID(s) copiados"), hits.Select(h => h.Item.Id).Distinct().Count())); } });
        var actClear = FindAct(() => { _findList.Items.Clear(); _findCount.Text = ""; });
        findActions.Controls.Add(_findCount);
        _findList.Columns.Add("Bloco", 150); _findList.Columns.Add("ID", 70, HorizontalAlignment.Right); _findList.Columns.Add("Nome", 260);
        _findList.Columns.Add("Campo", 200); _findList.Columns.Add("Valor", 200);
        Bind(() =>
        {
            findBtn.Text = Loc.T("detail.search", "Buscar");
            _findKeep.Text = Loc.T("find.keep", "Manter resultados");
            _tip.SetToolTip(_findKeep, Loc.T("find.keepTip", "Soma a nova busca aos resultados que já estão na lista"));
            int si = Math.Max(0, _findScope.SelectedIndex);
            _findScope.Items.Clear(); _findScope.Items.AddRange(new object[] { Loc.T("find.scopeBlock", "Bloco atual"), Loc.T("find.scopeAll", "Todos os blocos") }); _findScope.SelectedIndex = si;
            int oi = Math.Max(0, _findOp.SelectedIndex);
            _findOp.Items.Clear(); foreach (var o in FindOps) _findOp.Items.Add(Loc.T(o.Key, o.Pt)); _findOp.SelectedIndex = oi;
            actBulk.Text = Loc.T("find.bulk", "Alterar campo nos selecionados…");
            actIds.Text = Loc.T("find.copyIds", "Copiar IDs");
            actClear.Text = Loc.T("find.clear", "Limpar");
            _findList.Columns[0].Text = Loc.T("find.colBlock", "Bloco"); _findList.Columns[1].Text = Loc.T("list.id", "ID");
            _findList.Columns[2].Text = Loc.T("list.name", "Nome"); _findList.Columns[3].Text = Loc.T("list.field", "Campo"); _findList.Columns[4].Text = Loc.T("list.value", "Valor");
        });
        _findList.MultiSelect = true; _findList.HideSelection = false;
        _findList.SmallImageList = _treeIcons; _findList.BackColor = CSurface; _findList.ForeColor = CFg;
        _findList.DoubleClick += (_, _) => { if (_findList.SelectedItems.Count > 0 && _findList.SelectedItems[0].Tag is SearchHit h) GoToItem(h.Item); };
        _findList.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem li in _findList.Items) li.Selected = true; e.SuppressKeyPress = true; } };
        tFind.Controls.Add(_findList); tFind.Controls.Add(findBar); tFind.Controls.Add(findActions);
        // Descrição e cor (item_ext_desc.txt do configs.pck)
        var tDesc = _tabDesc = new TabPage() { BackColor = CTreeBg, Padding = new Padding(12) };
        Bind(() => tDesc.Text = Loc.T("detail.description", "Descrição e cor"));
        _descBox.BackColor = CTreeBg; _descBox.ForeColor = CTreeFg;
        BuildDescEditor(tDesc);
        _tabLinks = new TabPage() { BackColor = CSurface };
        Bind(() => _tabLinks.Text = Loc.T("detail.links", "Vínculos"));
        _links.Columns.Add("Bloco", 170); _links.Columns.Add("ID", 70, HorizontalAlignment.Right);
        _links.Columns.Add("Nome", 280); _links.Columns.Add("Campo", 220); _links.Columns.Add("%", 70, HorizontalAlignment.Right);
        Bind(() =>
        {
            _links.Columns[0].Text = Loc.T("list.block", "Bloco"); _links.Columns[1].Text = Loc.T("list.id", "ID");
            _links.Columns[2].Text = Loc.T("list.name", "Nome"); _links.Columns[3].Text = Loc.T("list.field", "Campo");
        });
        _links.BackColor = CSurface; _links.ForeColor = CFg; _links.SmallImageList = _treeIcons;
        _links.DoubleClick += (_, _) => { if (_links.SelectedItems.Count > 0 && _links.SelectedItems[0].Tag is Item it) GoToItem(it); };
        _tabLinks.Controls.Add(_links);
        _detailTabs.TabPages.Add(tVal);
        _detailTabs.TabPages.Add(_tabLinks);
        _detailTabs.TabPages.Add(tFind);
        _detailTabs.TabPages.Add(tDesc);
        _shop = new ShopPreview(id => _refs?.Resolve(FieldKind.ItemRef, id)?.Item, IconFor, GoToItem) { Dock = DockStyle.Fill };
        _tabShop.Controls.Add(_shop);
        Bind(() => _tabShop.Text = Loc.T("detail.shop", "Loja (como no jogo)"));
        _make = new MakePreview(() => _dataPath is null ? null : FindClientPck(_dataPath, "interfaces.pck") is { } ip ? Path.GetDirectoryName(ip) : null,
            id => _refs?.Resolve(FieldKind.RecipeRef, id)?.Item, id => _refs?.Resolve(FieldKind.ItemRef, id)?.Item, IconFor,
            sid => _cfgs?.SkillNames.TryGetValue(sid, out var sn) == true ? TasksFile.SplitColor(sn!).Text : null, GoToItem) { Dock = DockStyle.Fill };
        _tabMake.Controls.Add(_make);
        Bind(() => _tabMake.Text = Loc.T("detail.make", "Fabricação (como no jogo)"));
        _detailTabs.SelectedIndexChanged += (_, _) => { if (_detailTabs.SelectedTab == _tabLinks && _current is not null) FillLinks(_current); };
        right.Controls.Add(_detailTabs);
        // Valores / Vínculos / Buscar / Descrição viram botões em pílula com ícone
        right.Controls.Add(new SegmentBar(_detailTabs, pg => pg == tVal ? "\uE9D9" : pg == _tabLinks ? "\uE71B" : pg == tFind ? "\uE721" : pg == tDesc ? "\uE70F"
            : pg == _tabShop ? "\uE719" : pg == _tabMake ? "\uE90F" : null));
        right.Controls.Add(headPanel);
        _split.Panel2.Controls.Add(right);
        return _split;
    }

    private void StyleGrid()
    {
        _grid.BackgroundColor = CSurface;
        _grid.EnableHeadersVisualStyles = false;
        _grid.DefaultCellStyle.BackColor = CSurface;
        _grid.DefaultCellStyle.ForeColor = CFg;
        _grid.DefaultCellStyle.SelectionBackColor = CSel;
        _grid.DefaultCellStyle.SelectionForeColor = CSelFg;
        _grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        _grid.GridColor = Color.FromArgb(226, 231, 238);
        _grid.RowTemplate.Height = 26;
        _grid.ColumnCount = 3;
        _grid.Columns[0].Name = "Campo"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 230;
        _grid.Columns[0].DefaultCellStyle.ForeColor = CMuted;
        _grid.Columns[1].Name = "Valor"; _grid.Columns[1].Width = 260;
        _grid.Columns[2].Name = "Vínculo"; _grid.Columns[2].ReadOnly = true; _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns[2].DefaultCellStyle.ForeColor = CAccent;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _current is null || _grid.Rows[e.RowIndex].Tag is not string k) return;
            if (EditFieldSpecial(_current, k)) { _grid.CancelEdit(); _grid.EndEdit(); }
        };
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2 && e.ColumnIndex != 0 || _grid.Rows[e.RowIndex].Cells[e.ColumnIndex] is not DataGridViewLinkCell) return;
            switch (_grid.Rows[e.RowIndex].Tag)
            {
                case string k when _current is not null && FieldKinds.Of(k) == FieldKind.TaskRef && int.TryParse(_current.Fields[k].Value, out var qid): GoToQuest(qid); break;
                case string k when _current is not null && _refs?.Resolve(KindOf(_current, k), _current.Fields[k].Value) is { } hit: GoToItem(hit.Item); break;
                case Item it: GoToItem(it); break;
            }
        };
        _grid.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f);
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
        // caixa de seleção: confirma o clique na hora (senão só grava ao sair da célula)
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellPainting += PaintSection;
        _grid.CellPainting += PaintExtras;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
        _grid.RowTemplate.Height = 28;
        _grid.Columns[2].DefaultCellStyle.Font = new Font("Segoe UI", 9f);
        // colunas proporcionais: nome do campo | valor | o que significa (antes as duas primeiras eram fixas e a 3ª sumia)
        _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _grid.Columns[0].FillWeight = 32; _grid.Columns[0].MinimumWidth = 150;
        _grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _grid.Columns[1].FillWeight = 28; _grid.Columns[1].MinimumWidth = 110;
        _grid.Columns[2].FillWeight = 40; _grid.Columns[2].MinimumWidth = 120;
        _grid.MouseDown += ProbMouseDown; _grid.MouseMove += ProbMouseMove; _grid.MouseUp += ProbMouseUp;
        // clique direito: seleciona a linha antes de abrir o menu
        _grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[0]; };
        var balance = new ToolStripMenuItem();
        balance.Click += (_, _) => { if (_current is not null && _grid.CurrentRow?.Tag is string k) BalanceProbabilities(_current, k); };
        Bind(() => balance.Text = Loc.T("ctx.balance", "Distribuir 100% entre as probabilidades preenchidas"));
        _gridCtx.Items.Add(balance);
        var bulk = new ToolStripMenuItem();
        bulk.Click += (_, _) => BulkChange();
        Bind(() => bulk.Text = Loc.T("ctx.bulkField", "Alterar este campo nos itens selecionados…"));
        _gridCtx.Items.Add(bulk);
        var addonEd = new ToolStripMenuItem();
        addonEd.Click += (_, _) => { if (_current is not null) OpenChanceEditor(_current, drops: false); };
        Bind(() => addonEd.Text = Loc.T("ctx.addonEditor", "Editor de addons e chances…"));
        _gridCtx.Items.Add(addonEd);
        var dropEd = new ToolStripMenuItem();
        dropEd.Click += (_, _) => { if (_current is not null) OpenChanceEditor(_current, drops: true); };
        Bind(() => dropEd.Text = Loc.T("ctx.dropEditor", "Editor de drops…"));
        _gridCtx.Items.Add(dropEd);
        _gridCtx.Opening += (_, e) =>
        {
            if (_grid.CurrentRow?.Tag is not string k) { e.Cancel = true; return; }
            balance.Visible = FieldKinds.Of(k) == FieldKind.Probability;
            addonEd.Visible = _hasAddonEd;
            dropEd.Visible = _hasDropEd;
            int n = _list.SelectedIndices.Count;
            bulk.Text = Loc.T("ctx.bulkField", "Alterar este campo nos itens selecionados…") + (n > 1 ? $" ({n})" : "");
        };
        _grid.ContextMenuStrip = _gridCtx;
    }

    // Coluna "Vínculo": barra para probabilidade (0..1) e amostra para cor (ARGB), como nos editores da comunidade.
    private void PaintExtras(object? s, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 2 || e.Graphics is null || _current is null
            || _grid.Rows[e.RowIndex].Tag is not string key || !_current.Fields.TryGetValue(key, out var f)) return;
        var kind = FieldKinds.Of(key);
        var r = e.CellBounds;
        var fg = (e.State & DataGridViewElementStates.Selected) != 0 ? CSelFg : CFg;
        if (kind == FieldKind.Probability && (e.Value as string ?? "") is var desc && !desc.StartsWith("→"))
        {
            double p = ProbabilityGroups.Num(f.Value);
            e.PaintBackground(r, true);
            var (bar, lockBox) = ProbRects(r);
            bool bad = p < 0 || p > 1.0005;
            using (var back = new SolidBrush(Color.FromArgb(226, 231, 238))) e.Graphics.FillRectangle(back, bar);
            int w = (int)Math.Round(bar.Width * Math.Clamp(p, 0, 1));
            using (var fill = new SolidBrush(bad ? Color.FromArgb(220, 38, 38) : Color.FromArgb(34, 160, 90))) if (w > 0) e.Graphics.FillRectangle(fill, bar with { Width = w });
            e.Graphics.DrawRectangle(Pens.Silver, bar);
            TextRenderer.DrawText(e.Graphics, $"{p * 100:0.###}%", _grid.Font, new Rectangle(r.X + 6, r.Y, 68, r.Height), bad ? Color.FromArgb(220, 38, 38) : fg, TextFormatFlags.VerticalCenter);
            // cadeado: slot fixo (não muda quando outra barra do grupo é arrastada)
            bool locked = ReferenceEquals(_probLockItem, _current) && _probLocks.Contains(key);
            TextRenderer.DrawText(e.Graphics, locked ? "🔒" : "🔓", _grid.Font, lockBox, locked ? CAccent : Color.FromArgb(160, 170, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            // soma do grupo, na primeira linha do grupo
            var sib = ProbabilityGroups.Siblings(_current, key);
            if (sib.Count > 1 && sib[0] == key)
            {
                double sum = ProbabilityGroups.Sum(_current, sib);
                var col = ProbabilityGroups.SumsToOne(sum, sib.Count) ? Color.FromArgb(22, 128, 61) : sum <= 0.0005 ? CMuted : Color.FromArgb(220, 38, 38);
                TextRenderer.DrawText(e.Graphics, $"Σ {sum * 100:0.###}%", _grid.Font, new Rectangle(lockBox.Right + 6, r.Y, 110, r.Height), col, TextFormatFlags.VerticalCenter);
            }
            e.Handled = true;
        }
        else if (kind == FieldKind.Color && long.TryParse(f.Value, out var c))
        {
            e.PaintBackground(r, true);
            var sw = new Rectangle(r.X + 6, r.Y + 6, 28, r.Height - 12);
            using (var b = new SolidBrush(Color.FromArgb(255, (int)(c >> 16) & 255, (int)(c >> 8) & 255, (int)c & 255))) e.Graphics.FillRectangle(b, sw);
            e.Graphics.DrawRectangle(Pens.Gray, sw);
            TextRenderer.DrawText(e.Graphics, $"#{c & 0xFFFFFF:X6}", _grid.Font, new Rectangle(sw.Right + 8, r.Y, 120, r.Height), fg, TextFormatFlags.VerticalCenter);
            e.Handled = true;
        }
    }

    // ---------- barras de probabilidade: clicar/arrastar muda o valor; cadeado fixa o slot ----------
    private readonly HashSet<string> _probLocks = new();
    private Item? _probLockItem;
    private (Item Item, string Key, Dictionary<string, Field> Before)? _probDrag;

    private static (Rectangle Bar, Rectangle Lock) ProbRects(Rectangle cell)
    {
        var bar = new Rectangle(cell.X + 76, cell.Y + 7, Math.Max(20, Math.Min(180, cell.Width - 200)), cell.Height - 14);
        return (bar, new Rectangle(bar.Right + 4, cell.Y, 22, cell.Height));
    }

    // linha de probabilidade sob o mouse (coluna Vínculo) e a área da célula
    private bool ProbHit(Point pt, out string key, out Rectangle cell)
    {
        key = ""; cell = default;
        var hit = _grid.HitTest(pt.X, pt.Y);
        if (_current is null || hit.RowIndex < 0 || hit.ColumnIndex != 2 || _grid.Rows[hit.RowIndex].Tag is not string k
            || FieldKinds.Of(k) != FieldKind.Probability || (_grid.Rows[hit.RowIndex].Cells[2].Value as string ?? "").StartsWith("→")) return false;
        key = k; cell = _grid.GetCellDisplayRectangle(2, hit.RowIndex, false);
        return true;
    }

    private void ProbMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !ProbHit(e.Location, out var key, out var cell) || _current is null) return;
        if (!ReferenceEquals(_probLockItem, _current)) { _probLocks.Clear(); _probLockItem = _current; }
        var (bar, lockBox) = ProbRects(cell);
        if (lockBox.Contains(e.Location))
        {
            if (!_probLocks.Remove(key)) _probLocks.Add(key);
            _grid.InvalidateColumn(2);
            return;
        }
        if (e.X < bar.X - 4 || e.X > bar.Right + 4) return;
        var sib = ProbabilityGroups.Siblings(_current, key);
        _probDrag = (_current, key, sib.ToDictionary(k => k, k => _current.Fields[k]));
        ProbDragTo(e.X, bar);
    }

    private void ProbMouseMove(object? s, MouseEventArgs e)
    {
        if (_probDrag is not { } d || e.Button != MouseButtons.Left) return;
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag as string == d.Key) { ProbDragTo(e.X, ProbRects(_grid.GetCellDisplayRectangle(2, row.Index, false)).Bar); return; }
    }

    // Durante o arraste os valores vão direto no item (prévia); o histórico recebe só o resultado no MouseUp.
    private void ProbDragTo(int x, Rectangle bar)
    {
        if (_probDrag is not { } d) return;
        double v = Math.Round(Math.Clamp((x - bar.X) / (double)bar.Width, 0, 1), 3);   // passo de 0,1%
        foreach (var (k, f) in d.Before) d.Item.Fields[k] = f;                         // parte sempre do original
        var refOf = d.Item.Fields.Keys.Where(k => FieldKinds.IsReference(FieldKinds.Of(k)))
            .Select(k => (Ref: k, Prob: FieldKinds.ProbabilityFor(k, d.Item.Fields))).Where(t => t.Prob is not null)
            .GroupBy(t => t.Prob!).ToDictionary(g => g.Key, g => g.First().Ref);
        // o grupo continua somando 100% (o gs normaliza mesmo; usuário 01/11 pediu de volta por estética)
        var nv = ProbabilityGroups.Set(d.Item, d.Key, v, _probLocks, k => !refOf.TryGetValue(k, out var rk) || !IsEmpty(d.Item.Fields[rk].Value));
        foreach (var (k, val) in nv) d.Item.Fields[k] = Field.Of(d.Before[k].Type, ProbabilityGroups.Fmt(val));
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is string rk2 && nv.ContainsKey(rk2)) row.Cells[1].Value = d.Item.Fields[rk2].Value;
        _grid.InvalidateColumn(2);
    }

    private void ProbMouseUp(object? s, MouseEventArgs e)
    {
        if (_probDrag is not { } d) return;
        _probDrag = null;
        var after = d.Before.Keys.ToDictionary(k => k, k => d.Item.Fields[k].Value);
        foreach (var (k, f) in d.Before) d.Item.Fields[k] = f;
        var changed = after.Where(kv => kv.Value != d.Before[kv.Key].Value).ToList();
        if (changed.Count == 0) return;
        using (_edits.Batch(string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(d.Key), d.Item.Id)))
            foreach (var (k, v) in changed) SetField(d.Item, k, v, refresh: false);
        if (ReferenceEquals(_current, d.Item)) FillGrid(d.Item);
        var s2 = ProbabilityGroups.Sum(d.Item, d.Before.Keys);
        Status(string.Format(Loc.T("status.probSet", "{0} = {1:0.###}% · soma do grupo {2:0.###}%"), FieldLabels.Label(d.Key), ProbabilityGroups.Num(after[d.Key]) * 100, s2 * 100));
    }

    // Editor de addons / de drops (janela): trabalha numa cópia; ao salvar, as mudanças entram no histórico como uma ação só.
    /// <summary>Drops do monstro para o painel do Mundo 3D: chance de cair N itens e os itens, do mais provável ao menos. Null = não é monstro.</summary>
    private CreatureDrops? DropsOfCreature(int id)
    {
        if (_store.Current?.FindListByName("MINE_ESSENCE")?.Items.FirstOrDefault(i => i.Id == id.ToString()) is { } mine)
        {
            var host = new QuestHostImpl(this);
            var gives = host.MineProducts(id).Select(p =>
            {
                string name = p.Item.ToString(); Image? icon = null;
                if (_refs?.Resolve(FieldKind.ItemRef, p.Item.ToString()) is { } hit)
                {
                    name = TasksFile.SplitColor(hit.Item.Name).Text;
                    if (IconIndexFor(hit.Item) is >= 0 and var at) icon = _treeIcons.Images[at];
                }
                return new DropRow(p.Item, name, icon, p.Prob);
            }).ToList();
            return new([], gives);
        }
        if (CreatureItem(id) is not { Block.Name: "MONSTER_ESSENCE" } c) return null;
        double Num(string key) => c.Item.Fields.TryGetValue(key, out var f) && double.TryParse(f.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var rows = new List<DropRow>();
        for (int i = 1; c.Item.Fields.TryGetValue($"drop_matters_{i}_id", out var f); i++)
            if (int.TryParse(f.Value, out var item) && item > 0)
            {
                string name = item.ToString(); Image? icon = null;
                if (_refs?.Resolve(FieldKind.ItemRef, f.Value) is { } hit)
                {
                    name = TasksFile.SplitColor(hit.Item.Name).Text;
                    if (IconIndexFor(hit.Item) is >= 0 and var at) icon = _treeIcons.Images[at];
                }
                rows.Add(new(item, name, icon, Num($"drop_matters_{i}_probability")));
            }
        return new([.. Enumerable.Range(0, 4).Select(n => Num($"probability_drop_num{n}"))], [.. rows.OrderByDescending(r => r.Chance)]);
    }

    private void OpenChanceEditor(Item item, bool drops, IWin32Window? owner = null)
    {
        _grid.EndEdit();
        var addons = _cfgs?.Addons;
        var kind = drops ? FieldKind.ItemRef : FieldKind.AddonRef;
        string Text(string id) => _refs?.Resolve(kind, id) is not { } hit ? ""
            : drops ? $"{TasksFile.SplitColor(hit.Item.Name).Text}   [{Pretty(hit.Block.Name)}]"
            : addons?.Describe(hit.Item) is { Length: > 0 } d ? d : hit.Item.Name;
        Image? Icon(string id) => _refs?.Resolve(kind, id) is { } hit && IconIndexFor(hit.Item) is >= 0 and var i ? _treeIcons.Images[i] : null;
        List<string>? Pick(IWin32Window owner, string refKey)
        {
            var src = PickSource(kind, refKey);
            if (!drops) src = CompatibleAddons(item, src);
            if (src.Count == 0) return null;
            var picked = drops
                ? FieldEditors.Pick(owner, Loc.T("pick.item", "Escolher item"), src, null, IconIndexFor, _treeIcons,
                    it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : "", it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : null, true)
                : FieldEditors.Pick(owner, Loc.T("pick.addonCompatible", "Escolher atributo (só os usados por itens deste bloco)"), src, null, IconIndexFor, _treeIcons,
                    it => addons?.Describe(it) ?? "", addons is not null ? it => addons.TypeCaption(it.Id) : _ => null, true);
            return picked?.Select(p => p.Id).ToList();
        }
        using var dlg = new ChanceEditor(item, drops, FieldLabels.Label, Text, Pick, drops ? Icon : null);
        if (dlg.ShowDialog(owner ?? this) != DialogResult.OK || dlg.Changes.Count == 0) return;
        var what = drops ? Loc.T("dropEd.name", "Editor de drops") : Loc.T("addonEd.name", "Editor de addons");
        using (_edits.Batch($"{what} {item.Id}"))
            foreach (var (k, v) in dlg.Changes) SetField(item, k, v, refresh: false);
        if (ReferenceEquals(_current, item)) ShowItem(item);
        Status(string.Format(Loc.T("status.chanceEditor", "{0}: {1} campo(s) alterado(s) em {2}"), what, dlg.Changes.Count, item.Id));
    }

    // Addons compatíveis: só os que algum item do mesmo bloco (armas, armaduras, acessórios…) já usa em qualquer
    // lista de addons. Addon de outro tipo de equipamento pode travar o cliente; o próprio jogo serve de referência.
    private List<Item> CompatibleAddons(Item item, List<Item> src)
    {
        if (!_owner.TryGetValue(item, out var block)) return src;
        var used = new HashSet<string>();
        foreach (var it in block.Items)
            foreach (var sec in AddonGroups.Find(it, AddonGroups.IsAddonGroup))
                foreach (var r in sec.Rows)
                    if (r.Ref is not null && it.Fields.TryGetValue(r.Ref, out var f) && !AddonGroups.Empty(f.Value)) used.Add(f.Value);
        var ok = src.Where(a => used.Contains(a.Id)).ToList();
        return ok.Count > 0 ? ok : src;
    }

    // Barra acima da grade com os editores que valem para o item atual (addons em equipamento, drops em monstro).
    private void UpdateEditorBar(Item item)
    {
        // (Visible de um filho lê false enquanto o pai está oculto: guardar o estado em variáveis)
        _hasAddonEd = AddonGroups.Find(item, AddonGroups.IsAddonGroup).Count > 0;
        _hasDropEd = AddonGroups.Find(item, AddonGroups.IsDropGroup).Any(s => s.HasRefs);
        _btnAddonEd.Visible = _hasAddonEd; _btnDropEd.Visible = _hasDropEd;
        _editorBar.Visible = _hasAddonEd || _hasDropEd;
    }

    // Probabilidades irmãs (mesmo nome trocando o número): divide 100% entre os slots com referência preenchida.
    private void BalanceProbabilities(Item item, string probKey)
    {
        var m = System.Text.RegularExpressions.Regex.Match(probKey, @"^(.*?_)(\d+)(_.*|)$");
        if (!m.Success) return;
        var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(m.Groups[1].Value) + @"\d+" + System.Text.RegularExpressions.Regex.Escape(m.Groups[3].Value) + "$");
        var siblings = item.Fields.Keys.Where(k => pattern.IsMatch(k) && FieldKinds.Of(k) == FieldKind.Probability).ToList();
        // referência de cada probabilidade (drop_matters_3_id -> drop_matters_3_probability)
        var refOf = item.Fields.Keys.Where(k => FieldKinds.IsReference(FieldKinds.Of(k)))
            .Select(k => (Ref: k, Prob: FieldKinds.ProbabilityFor(k, item.Fields))).Where(x => x.Prob is not null)
            .GroupBy(x => x.Prob!).ToDictionary(g => g.Key, g => g.First().Ref);
        var filled = siblings.Where(pk => refOf.TryGetValue(pk, out var rk) ? !IsEmpty(item.Fields[rk].Value) : !IsEmpty(item.Fields[pk].Value)).ToList();
        if (filled.Count == 0) { Status(Loc.T("status.balanceNone", "Nenhum slot preenchido para distribuir.")); return; }
        double each = Math.Round(1.0 / filled.Count, 6), sum = 0;
        using var batch = _edits.Batch(Loc.T("hist.balance", "Distribuir probabilidades") + " " + item.Id);
        foreach (var pk in siblings)
        {
            double v = !filled.Contains(pk) ? 0 : pk == filled[^1] ? Math.Round(1 - sum, 6) : each;
            sum += filled.Contains(pk) && pk != filled[^1] ? each : 0;
            SetField(item, pk, v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), refresh: false);
        }
        FillGrid(item);
        Status(string.Format(Loc.T("status.balanced", "Probabilidades: {0} slots com {1:0.###}% cada"), filled.Count, each * 100));
    }

    private StatusStrip BuildStatus()
    {
        var s = new StatusStrip { BackColor = CSurface2, ForeColor = CMuted };
        _status.Spring = true; _status.TextAlign = ContentAlignment.MiddleLeft;
        s.Items.Add(_status);
        foreach (var l in new[] { _stTotal, _stBlock, _stSel }) { l.BorderSides = ToolStripStatusLabelBorderSides.Left; l.BorderStyle = Border3DStyle.Etched; l.ForeColor = CMuted; s.Items.Add(l); }
        return s;
    }

    // ---------- carregar ----------
    private void OpenPwFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("dialog.openFolder", "Selecione a pasta do Perfect World (que contém 'element')") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var ele = FindFile(dlg.SelectedPath, "elements.data");
        if (ele is null) { Status(Loc.T("status.noElements", "Não achei elements.data nessa pasta.")); return; }
        if (!ConfirmLeaveGame()) return;
        LoadElements(ele);
    }

    /// <summary>
    /// Antes de abrir outro elements.data (outro PW): todas as abas do cliente vão trocar de arquivo, então o que está
    /// pendente nelas (elements, quests, loja, AI Policy) é salvo ou descartado agora, de uma vez. false = cancelou.
    /// </summary>
    private bool ConfirmLeaveGame()
    {
        bool dirty = ElementsDirty || _tasksDirty || _gshop?.HasUnsaved == true || _aiView?.HasUnsaved == true || _cfgDirty;
        if (!dirty) return true;
        var r = MessageBox.Show(this, Loc.T("load.unsavedAsk", "Há alterações não salvas no PW aberto. Salvar tudo antes de abrir o outro?\n\nSim: salva e abre · Não: descarta e abre · Cancelar: continua aqui"),
            Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) SaveAll(quiet: true);
        return true;
    }

    private static string? FindFile(string root, string name)
    {
        foreach (var c in new[] { name, Path.Combine("element", "data", name), Path.Combine("data", name), Path.Combine("element", name) })
            if (File.Exists(Path.Combine(root, c))) return Path.Combine(root, c);
        try { return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault(); } catch { return null; }
    }

    private void OpenElements()
    {
        using var dlg = new OpenFileDialog { Filter = "Perfect World elements (*.data)|*.data|" + Loc.T("dlg.allFiles", "Todos") + " (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK && ConfirmLeaveGame()) LoadElements(dlg.FileName);
    }

    // Carregamento em segundo plano com janela de progresso e cancelamento (contribuição LAOCHEN):
    // a leitura pesada (elements, ícones, configs, quests) roda fora da tela; a tela só recebe o resultado pronto.
    private sealed record Loaded(Module Module, string? AtlasPck, IconAtlas? Atlas, GameConfigs? Cfgs,
        TasksFile? Tasks, string? TasksPath, int Position, int Length, long Ms, string Cfg, IReadOnlyList<string> Warnings);

    private void LoadElements(string path, string? cfgOverride = null)
    {
        try
        {
            var r = ReadProgressDialog.Run(this, Loc.T("progress.title", "Lendo elements.data"), ctx => ReadAll(path, cfgOverride, ctx));
            ApplyLoaded(path, r);
        }
        catch (OperationCanceledException) { Status(Loc.T("status.loadCancelled", "Leitura cancelada; os dados anteriores foram mantidos.")); }
        catch (CfgChoiceRequiredException ex)
        {
            // mesma versão com mais de um ramo de .cfg compatível: o usuário escolhe
            using var dlg = new OpenFileDialog
            {
                Title = ex.Message,
                InitialDirectory = Path.GetDirectoryName(ex.Paths[0]),
                Filter = "CFG (*.cfg)|" + string.Join(";", ex.Paths.Select(Path.GetFileName)),
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) LoadElements(path, dlg.FileName);
        }
        catch (Exception ex) { Error(Loc.T("status.readFail", "Falha ao ler elements.data"), ex); }
    }

    // Só leitura de arquivos: não toca em nenhum controle (roda em outra thread).
    private static Loaded ReadAll(string path, string? cfgOverride, LoadContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var match = CfgCatalog.Load(path, cfgOverride, context: ctx.Slice(0, .70));
        var module = match.Module;
        module.Name = Path.GetFileName(path);
        var warnings = new List<string>();

        var pck = IconAtlas.FindSurfacesPck(path);
        IconAtlas? atlas = null;
        ctx.Report(.70, Loc.T("load.icons", "Lendo ícones"), indeterminate: true, force: true);
        if (pck is not null)
            try { atlas = IconAtlas.Load(pck, ctx.Slice(.70, .85)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add(Loc.T("load.iconsFail", "Ícones não carregados: ") + ex.Message); }

        GameConfigs? cfgs = null;
        ctx.Report(.85, Loc.T("load.descriptions", "Lendo descrições"), indeterminate: true, force: true);
        try { cfgs = GameConfigs.FindConfigsPck(path) is { } cp ? GameConfigs.Load(cp, ctx.Slice(.85, .92)) : null; }
        catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("configs.pck: " + ex.Message); }

        TasksFile? tasks = null; string? tasksPath = null;
        var tp = Path.Combine(Path.GetDirectoryName(path) ?? "", "tasks.data");
        if (File.Exists(tp))
            try { tasks = new TasksFile(tp, ctx.Slice(.92, 1)); tasksPath = tp; }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("tasks.data: " + ex.Message); }

        return new Loaded(module, pck, atlas, cfgs, tasks, tasksPath, match.Result.Position, match.Result.Length,
            sw.ElapsedMilliseconds, Path.GetFileName(match.ConfigPath), warnings);
    }

    // Aplicação na tela (thread da interface).
    private void ApplyLoaded(string path, Loaded r)
    {
        var module = r.Module;
        _store.Adopt(module);
        _unusedCache.Clear();
        _typeCache.Clear();
        _edits.Clear();
        var atLoad = ItemRules.Check(module, cashLimit: true);
        _cashLimit = !atLoad.Any(p => p.Rule == "cash-id");
        _rulesAtLoad = atLoad.Where(p => _cashLimit || p.Rule != "cash-id").Select(p => p.Key).ToHashSet();
        _refs = new RefIndex(module);
        _aiView?.SetElements(module);   // nomes de monstros e "quem usa cada política" na aba AI Policy
        _atlasPck = r.AtlasPck; _atlas = r.Atlas;          // ícones: cache novo para este pacote
        _iconIdx.Clear(); _treeIcons.Images.Clear(); _thumbs?.Dispose(); _thumbs = null; _thumbBatch.Clear();
        _cfgs = r.Cfgs;
        _cfgColorIds.Clear(); _cfgDescIds.Clear(); _cfgDirty = false; FillDescPalette();   // cor/descrição: nada pendente no jogo novo
        _tasks = r.Tasks; _tasksPath = r.TasksPath; _tasksDirty = false;
        // base das regras das quests (o jogo original já tem referências quebradas): em segundo plano, ~10-50 s
        _questBase = r.Tasks is { } tk ? Task.Run(() => QuestRules.Check(tk, module).Select(QKey).ToHashSet()) : null;
        if (r.Tasks is { } tl) _ = Task.Run(() => QuestBuilder.WarmLinks(tl));   // excluir/ligar quest sem esperar a leitura de tudo   // sem tasks.data no jogo novo, a aba fica vazia
        _qTree.Nodes.Clear(); _qCur = null; _qGrid.Rows.Clear();
        RefreshQuests();
        _qStatus.Text = r.TasksPath is { } tp ? TasksStatus(tp) : Loc.T("quest.openFirst", "Abra um tasks.data primeiro.");
        BuildTree();
        BeginInvoke(CleanMissingQuestsInNpcs);
        SaveLast(path);
        _dataPath = path;
        LoadRecent(path);
        LoadFavoritesFor();
        // volta ao último item em que você estava neste elements.data
        if (_recentKeys.Count > 0 && ResolveRecent(_recentKeys[0]) is { } lastItem) BeginInvoke(() => GoToItem(lastItem));
        // o cliente aberto passa a ser o da aba Verificação (e o que o "Salvar e enviar" leva ao servidor)
        var element = Path.GetDirectoryName(Path.GetDirectoryName(path));
        if (element is not null)
        {
            _chkClient.Text = VersionCheck.ClientElement(element);
            SaveSetting("check_client", _chkClient.Text);
        }
        // regra: o programa é um só — toda aba que lê arquivos do cliente passa para os da pasta nova
        BeginInvoke(() =>
        {
            ApplyServerPaths();
            _gshop?.GameFolderChanged();
            _aiView?.GameFolderChanged(element);
            _pckView?.GameFolderChanged(); _uiArea?.GameFolderChanged();
            _chkList.Items.Clear(); CheckNotRun();   // o resultado era do cliente anterior
            // 1ª vez desta versão: pergunta se configura o servidor local
            if (_serverView?.OfferSetup(this) == true && _tabs is not null && _tabServer is not null) _tabs.SelectedTab = _tabServer;
        });
        int items = module.Lists.Sum(l => l.Items.Count);
        Status($"{module.Name} · {module.Lists.Count} {Loc.T("status.blocks", "blocos")} · {items:N0} {Loc.T("status.items", "itens")} · {100.0 * r.Position / r.Length:0.0}% · {r.Ms / 1000.0:0.0} s"
            + $" · {r.Cfg}"
            + (_atlas is null ? Loc.T("status.noIcons", " · ícones: surfaces.pck não encontrado") : " · " + string.Format(Loc.T("status.icons", "{0:N0} ícones do jogo"), _atlas.Count))
            + (module.Source is ElementsLayout { CanWrite: false } lay ? " · " + Loc.T("status.readOnly", "somente leitura") + ": " + lay.ReadOnlyReason : "")
            + (r.Warnings.Count > 0 ? " · " + string.Join(" · ", r.Warnings) : ""));
        Text = $"MyAceEditor — {module.Name}{HeaderInfo(path)}";
        AppLog.Info($"aberto {path} · v{module.Version & 0xFFFF} · {r.Warnings.Count} aviso(s)" + (r.Warnings.Count > 0 ? ": " + string.Join(" · ", r.Warnings) : ""));
        UseWaitCursor = false;
        // CFG adaptado ao arquivo (versão trocada ou tamanho de registro diferente): avisa o que foi feito
        if (module.Source is ElementsLayout { Adjustments.Count: > 0 } adj)
        {
            Status(Loc.T("status.cfgAdjusted", "CFG adaptado a este arquivo: ") + string.Join(" · ", adj.Adjustments));
            MessageBox.Show(this, Loc.T("cfgAdjust.body", "Este elements.data não segue exatamente o CFG da versão que ele declara. O MyAceEditor adaptou a leitura sozinho:") + "\n\n• "
                + string.Join("\n• ", adj.Adjustments) + "\n\n" + (adj.CanWrite
                    ? Loc.T("cfgAdjust.ok", "A leitura foi até o último byte e a regravação de teste saiu idêntica ao original: pode editar e salvar normalmente.")
                    : Loc.T("cfgAdjust.ro", "Aberto como somente leitura: ") + adj.ReadOnlyReason),
                Loc.T("cfgAdjust.title", "Leitura adaptada"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // " · v60 · 14/03/2012 23:05" lido do cabeçalho do elements.data (u16 versão, u16 assinatura, u32 data de exportação).
    private static string HeaderInfo(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var b = new byte[8];
            if (fs.Read(b, 0, 8) < 8) return "";
            int ver = BitConverter.ToUInt16(b, 0);
            long ts = BitConverter.ToUInt32(b, 4);
            return $" · v{ver}" + (ts > 0 ? $" · {DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime:dd/MM/yyyy HH:mm}" : "");
        }
        catch { return ""; }
    }

    // Ícones nativos: lê o atlas direto do surfaces.pck (sem extrair nada).
    private Image? IconFor(Item it)
    {
        if (!it.Fields.TryGetValue("file_icon", out var fi) || string.IsNullOrEmpty(fi.Value)) return null;
        Image? img = null;
        try { img = _atlas?.Get(fi.Value); } catch { }
        if (img is null && _iconsRoot is not null)
            try { img = IconLoader.Load(_iconsRoot, fi.Value); } catch { }
        return img;
    }


    // Salvar: grava o elements.data no próprio arquivo (backup com data/hora + escrita atômica).
    private void SaveAs() => SaveAll(quiet: false);
    // Salvar do menu/Ctrl+S/barra: grava o arquivo da aba aberta (Quests → tasks.data, senão elements.data).
    private void SaveCurrent() => SaveAll(quiet: false);

    // ---------- salvar tudo ----------
    // elements.data alterado = o passo mais recente do histórico não é o do último salvar/abrir
    private MyAceEditor.Changes.EditHistory.Step? _savedStep;
    private bool ElementsDirty => _store.Current is not null && !ReferenceEquals(_edits.UndoSteps.FirstOrDefault(), _savedStep);
    private void MarkElementsSaved() => _savedStep = _edits.UndoSteps.FirstOrDefault();

    /// <summary>Grava de uma vez tudo o que mudou: elements.data, tasks.data, lojas (cliente + servidor) e npcgen.</summary>
    private void SaveAll(bool quiet, bool checkServer = true)
    {
        CommitDescEdit();   // descrição digitada e não aplicada também vai
        _grid.EndEdit(); _qGrid.EndEdit();
        var pending = new List<string>();
        if (ElementsDirty && _dataPath is not null) pending.Add(_dataPath);
        if (_tasksDirty && _tasksPath is not null) pending.Add(_tasksPath);
        if (_gshop?.HasUnsaved == true) pending.Add("gshop / gshopsev");
        if (_npcgen?.HasUnsaved == true) pending.Add("npcgen.data");
        if (_aiView?.HasUnsaved == true) pending.Add(_aiView.FilePath ?? "aipolicy.data");
        if (_cfgDirty) pending.Add(Loc.T("save.itemTexts", "configs.pck (cor do nome / descrição)"));
        if (pending.Count == 0) { if (!quiet) Status(Loc.T("status.nothingToSave", "Nada para salvar.")); return; }
        if (!quiet && MessageBox.Show(string.Format(Loc.T("save.allAsk", "Gravar as alterações (com cópia de segurança ao lado de cada arquivo)?\n\n{0}"), string.Join("\n", pending)),
                Loc.T("save.allTitle", "Salvar tudo"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var done = new List<string>();
        if (ElementsDirty && _dataPath is not null && SaveElementsCore(_dataPath)) done.Add(Path.GetFileName(_dataPath));
        if (_tasksDirty && _tasksPath is not null && SaveTasksCore()) done.Add(Path.GetFileName(_tasksPath));
        if (_gshop is not null) done.AddRange(_gshop.SaveAll(quiet).Select(Path.GetFileName)!);
        if (_npcgen is not null) done.AddRange(_npcgen.SaveAll(quiet).Select(p => Path.GetFileName(Path.GetDirectoryName(p)) + "\\npcgen.data"));
        if (_aiView?.SaveIfDirty() is { } ai) done.Add(Path.GetFileName(ai));
        if (SaveItemTexts(quiet) is { } ct) done.Add(ct);
        foreach (var d in done) if (!d.Equals("configs.pck", StringComparison.OrdinalIgnoreCase)) _unsent.Add(d);   // configs.pck é só do cliente
        UpdateSendGlow();
        if (checkServer && done.Count > 0) CheckServer(ask: !quiet);
        Status((quiet ? Loc.T("status.autoSaved", "Salvo automaticamente: ") : Loc.T("status.savedAll", "Salvo: ")) + (done.Count == 0 ? "—" : string.Join(", ", done)) + $" ({DateTime.Now:HH:mm})");
        if (!quiet && checkServer && done.Count > 0) SavedDialog.Show(this, string.Join("\n", done.Select(d => "• " + d)), done.Any(d => !d.Equals("configs.pck", StringComparison.OrdinalIgnoreCase)) ? SaveAndSend : null);
    }

    // ---------- brilho do "Salvar e enviar ao servidor" (docs/QUESTS-3D.md) ----------
    // verde sutil enquanto houver mudança que o servidor ainda não tem: não salva (memória) ou salva só no cliente
    private ToolStripButton? _sendBtn;
    private readonly System.Windows.Forms.Timer _glowTimer = new() { Interval = 1000 };
    private readonly SortedSet<string> _unsent = new(StringComparer.OrdinalIgnoreCase);   // gravados no cliente desde o último envio

    private List<string> PendingForServer()
    {
        var l = new List<string>();
        if (ElementsDirty) l.Add(Loc.T("glow.elements", "elements.data (não salvo)"));
        if (_tasksDirty) l.Add(Loc.T("glow.tasks", "tasks.data (não salvo)"));
        if (_gshop?.HasUnsaved == true) l.Add(Loc.T("glow.gshop", "loja (não salva)"));
        if (_npcgen?.HasUnsaved == true) l.Add(Loc.T("glow.npcgen", "npcgen.data (não salvo)"));
        if (_aiView?.HasUnsaved == true) l.Add(Loc.T("glow.ai", "aipolicy.data (não salvo)"));
        foreach (var f in _unsent) l.Add(string.Format(Loc.T("glow.saved", "{0} (salvo só no cliente)"), f));
        if (_serverDiff is { Count: > 0 } sd)
        {
            var extra = sd.Where(f => !_unsent.Contains(Path.GetFileName(f))).ToList();
            l.AddRange(extra.Take(8).Select(f => string.Format(Loc.T("glow.differs", "{0} (diferente no servidor)"), f)));
            if (extra.Count > 8) l.Add($"… +{extra.Count - 8}");
        }
        return l;
    }

    // ---------- servidor local diferente do cliente (conferido de verdade, não só nesta sessão) ----------
    private List<string>? _serverDiff;   // arquivos que o envio levaria; null = não conferido / sem servidor local
    private bool _checkingServer;

    /// <summary>Compara em segundo plano o servidor local com o que o envio levaria; com <paramref name="ask"/>, oferece enviar.</summary>
    private async void CheckServer(bool ask)
    {
        if (_serverView is null || _checkingServer) return;
        _checkingServer = true;
        try { _serverDiff = await _serverView.ServerDiffAsync(); }
        finally { _checkingServer = false; }
        if (IsDisposed) return;
        UpdateSendGlow();
        // pergunta só com a preferência ligada (padrão: não; o botão verde já mostra o que falta enviar)
        if (!ask || LoadSetting("srvdiff-ask") != "1" || _serverDiff is not { Count: > 0 } d) return;
        var list = string.Join("\n• ", d.Take(10)) + (d.Count > 10 ? $"\n… +{d.Count - 10}" : "");
        if (MessageBox.Show(this, string.Format(Loc.T("srvdiff.ask", "O servidor local está diferente do seu cliente em {0} arquivo(s):\n• {1}\n\nEnviar agora? Só o que mudou é copiado e o servidor reinicia."), d.Count, list),
                Loc.T("srvdiff.title", "Servidor local desatualizado"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            SaveAndSend();
    }

    private void UpdateSendGlow()
    {
        if (_sendBtn is null) return;
        var pending = PendingForServer();
        _sendBtn.BackColor = pending.Count > 0 ? Color.FromArgb(214, 240, 214) : Color.White;
        _sendBtn.ForeColor = pending.Count > 0 ? Color.FromArgb(20, 90, 30) : CAccent;
        _sendBtn.ToolTipText = Loc.T("tb.saveSendTip", "Grava tudo o que mudou e leva para o servidor local (aba Servidor), reiniciando-o")
            + (pending.Count == 0 ? "\n\n" + Loc.T("glow.none", "O servidor já tem tudo.") : "\n\n" + Loc.T("glow.pending", "Falta enviar ao servidor:") + "\n• " + string.Join("\n• ", pending));
    }

    // "Salvar e enviar ao servidor": grava tudo sem perguntar e leva para o servidor local (aba Servidor mostra o andamento)
    private ServerView? _serverView;
    private TabPage? _tabServer, _tabCheck;
    /// <summary>Aberto com --clean: não reabre o último arquivo.</summary>
    public static bool StartClean { get; set; }
    private TabControl? _serverPages;
    private void ShowCheck()
    {
        if (_tabs is not null && _tabServer is not null) _tabs.SelectedTab = _tabServer;
        if (_serverPages is not null && _tabCheck is not null) _serverPages.SelectedTab = _tabCheck;
    }
    private void SaveAndSend()
    {
        if (_gshop?.ServerProblems() is { Count: > 0 } bad)
        {
            if (_tabs is not null && _tabGShop is not null) _tabs.SelectedTab = _tabGShop;
            MessageBox.Show(this, GShopView.ProblemText(bad), Loc.T("tb.saveSend", "💾 Salvar e enviar ao servidor"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SaveAll(quiet: true, checkServer: false);
        if (_serverView is null) return;
        if (_tabs is not null && _tabServer is not null) _tabs.SelectedTab = _tabServer;
        _serverView.SendFiles();
    }

    // fechar com mudanças: oferece salvar tudo
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bool dirty = ElementsDirty || _tasksDirty || _gshop?.HasUnsaved == true || _npcgen?.HasUnsaved == true || _aiView?.HasUnsaved == true || _cfgDirty;
        if (dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var r = MessageBox.Show(this, Loc.T("save.onClose", "Há alterações não salvas. Salvar tudo antes de sair?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes) SaveAll(quiet: true);
        }
        base.OnFormClosing(e);
    }

    // salvar automático (Arquivo → Salvar automaticamente a cada 5 minutos)
    private readonly System.Windows.Forms.Timer _autoSave = new() { Interval = 5 * 60 * 1000 };
    private void SaveElementsAs() => SaveElements(saveAs: true);

    // Exporta o bloco aberto (ou o bloco do item selecionado) como planilha CSV: ID, nome e todos os campos.
    private void ExportBlockCsv()
    {
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null || block.Items.Count == 0) { Status(Loc.T("status.exportNoBlock", "Escolha um bloco com itens para exportar.")); return; }
        using var dlg = new SaveFileDialog { Filter = Loc.T("dlg.csvFilter", "Planilha CSV") + " (*.csv)|*.csv", FileName = block.Name + ".csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var keys = block.Items.SelectMany(i => i.Fields.Keys).Distinct().ToList();
        string Q(string v) => v.Contains(';') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(";", new[] { "ID", Loc.T("list.name", "Nome") }.Concat(keys).Select(Q)));
        foreach (var it in block.Items)
            sb.AppendLine(string.Join(";", new[] { it.Id, it.Name }.Concat(keys.Select(k => it.Fields.TryGetValue(k, out var f) ? f.Value : "")).Select(Q)));
        File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));   // BOM: abre certo no Excel
        Status(Loc.T("status.exported", "Exportado") + $": {block.Items.Count:N0} " + Loc.T("status.items", "itens") + $" → {Path.GetFileName(dlg.FileName)}");
    }

    private void SaveElements(bool saveAs)
    {
        _grid.EndEdit();                                // célula em edição entra no item antes de gravar
        if (_store.Current?.Source is not ElementsLayout || _dataPath is null) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        var target = _dataPath;
        if (saveAs)
        {
            using var dlg = new SaveFileDialog { Filter = "elements.data (*.data)|*.data", FileName = Path.GetFileName(_dataPath), InitialDirectory = Path.GetDirectoryName(_dataPath) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            target = dlg.FileName;
        }
        else if (MessageBox.Show(string.Format(Loc.T("save.confirm", "Gravar as alterações em:\n{0}\n\nUma cópia de segurança do arquivo atual será criada ao lado dele."), target),
                     Loc.T("save.title", "Salvar elements.data"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SaveElementsCore(target);
    }

    // regras do gs (ItemRules) já quebradas no arquivo aberto: alguns jogos vêm assim (ex.: 180 tem itens de loja acima
    // de 65534 e o gs dele aceita); só problemas NOVOS impedem salvar
    private HashSet<string> _rulesAtLoad = new();
    private bool _cashLimit = true;   // o limite de 65534 só vale se o arquivo do jogo não tiver item de loja acima dele

    // regras das quests (QuestRules): referência nova a item/monstro/NPC que não existe ou ID de quest repetido.
    // Vale ao salvar o tasks.data e o elements.data (excluir um item que uma quest dá também quebra a quest).
    private Task<HashSet<string>>? _questBase;
    private static string QKey(ItemRules.Problem p) => p.Key + "|" + p.Detail;

    private bool QuestRulesBlock()
    {
        if (_tasks is null || _store.Current is null) return false;
        HashSet<string> baseSet;
        try { UseWaitCursor = true; baseSet = _questBase?.Result ?? new(); }
        catch (Exception ex) { AppLog.Error("regras das quests (base)", ex); return false; }
        finally { UseWaitCursor = false; }
        var fresh = QuestRules.Check(_tasks, _store.Current).Where(p => !baseSet.Contains(QKey(p))).ToList();
        if (fresh.Count == 0) return false;
        MessageBox.Show(this, Loc.T("save.questRules", "Não salvei: estas quests deixariam de funcionar no jogo. Corrija a quest ou não exclua/troque o ID do que ela usa:") + "\n\n"
            + string.Join("\n", fresh.Take(12).Select(p => "• " + p)) + (fresh.Count > 12 ? $"\n… +{fresh.Count - 12}" : ""),
            Loc.T("save.rulesTitle", "O jogo não aceitaria"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    /// <summary>
    /// Confere os pontos dos NPCs com o world_targets.sev antes de salvar. Pergunta em vez de barrar: o par do cliente e do servidor
    /// pode já vir diferente de fábrica, e os NPCs do jogo não são culpa da edição. Sem a pasta do servidor não há o que conferir.
    /// </summary>
    private bool TeleportRulesOk(Module m)
    {
        if (_npcgen?.ConfigDir is not { } dir || Path.Combine(dir, "world_targets.sev") is var sev && !File.Exists(sev)) return true;
        List<ItemRules.Problem> bad;
        try { bad = ItemRules.CheckTeleports(m, WorldTargets.ReadServer(File.ReadAllBytes(sev)).Select(t => t.Id).ToHashSet()); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("world_targets.sev", ex); return true; }
        if (bad.Count == 0) return true;
        return MessageBox.Show(this, Loc.T("save.teleports", "Estes NPCs têm teleporte com problema no jogo:") + "\n\n" + string.Join("\n", bad.Take(12).Select(p => "• " + p)) + (bad.Count > 12 ? $"\n… +{bad.Count - 12}" : "")
            + "\n\n" + Loc.T("save.teleportsAsk", "Salvar mesmo assim?"), Loc.T("save.teleportsTitle", "Teleportes"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private bool SaveElementsCore(string target)
    {
        if (_store.Current?.Source is not ElementsLayout) return false;
        var fresh = ItemRules.Check(_store.Current, _cashLimit).Where(p => !_rulesAtLoad.Contains(p.Key)).ToList();
        if (fresh.Count > 0)
        {
            MessageBox.Show(this, Loc.T("save.rules", "Não salvei: o servidor (gs) não aceitaria isto. Corrija (ID: \"Mudar ID\"; subtipo: campo id_sub_type) e salve de novo:") + "\n\n"
                + string.Join("\n", fresh.Take(12).Select(p => "• " + p)) + (fresh.Count > 12 ? $"\n… +{fresh.Count - 12}" : ""),
                Loc.T("save.rulesTitle", "O jogo não aceitaria"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (QuestRulesBlock()) return false;
        if (!TeleportRulesOk(_store.Current)) return false;
        try
        {
            UseWaitCursor = true;
            (string? Block, string Id)? keep = _current is null ? null : (BlockContaining(_current)?.Name, _current.Id);
            var backup = ElementsWriter.Save(_store.Current, target);
            // mesmo arquivo: não relê — o gravador sempre parte do original lido + itens atuais, e assim o
            // histórico (Ctrl+Z) continua valendo depois de salvar. Outro arquivo (Salvar como): passa a ser o aberto.
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(_dataPath ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                LoadElements(target);
                if (keep is { } k && _store.Current?.FindListByName(k.Block ?? "")?.FindById(k.Id) is { } again)
                { _current = again; SelectItem(again); ShowItem(again); }
            }
            Status(string.Format(Loc.T("status.savedTo", "Salvo em {0}"), Path.GetFileName(target)) + (backup is null ? "" : $" · backup: {Path.GetFileName(backup)}"));
            MarkElementsSaved();
            AppLog.Info($"salvo {target}" + (backup is null ? "" : $" (backup {Path.GetFileName(backup)})"));
            return true;
        }
        catch (Exception ex) { Error(Loc.T("status.saveFail", "Falha ao salvar"), ex); return false; }
        finally { UseWaitCursor = false; }
    }

    // ---------- lista de itens (estilo Rody: seletor de bloco + lista plana) ----------
    // Entrada do seletor de bloco. Block == null => Favoritos (ou Todos, com All).
    // Dyn calcula o texto na hora de desenhar, então acompanha o idioma sem recriar o combo.
    private sealed record BlockEntry(ItemList? Block, string Label, bool Header = false, Func<string>? Dyn = null, bool All = false)
    {
        public override string ToString() => Dyn?.Invoke() ?? Label;
    }

    // Categoria do bloco para organizar o seletor (como os grupos do Rody).
    private static string BlockCategory(string n) =>
        n.Contains("MONSTER") || n.StartsWith("NPC") || n.Contains("MINE") || n == "TALK_PROC" ? "Monstros, NPCs e minas"
        : n.Contains("PET") ? "Pets"
        : n.Contains("RECIPE") || n.Contains("PRODUCE") ? "Receitas e produção"
        : n is "WEAPON_ESSENCE" or "ARMOR_ESSENCE" or "DECORATION_ESSENCE" or "FASHION_ESSENCE" or "PROJECTILE_ESSENCE" or "QUIVER_ESSENCE"
              or "FLYSWORD_ESSENCE" or "WINGMANWING_ESSENCE" or "SUITE_ESSENCE" or "EQUIPMENT_ADDON" || n.Contains("WEAPON") || n.Contains("ARMOR") || n.Contains("DECORATION") || n.Contains("FASHION") ? "Equipamentos"
        : n.Contains("FACE") || n.Contains("CHARACTER") || n.Contains("PLAYER") || n.Contains("CONFIG") || n.Contains("LEVEL") ? "Personagem e configurações"
        : n.EndsWith("_ESSENCE") ? "Itens, consumíveis e materiais"
        : "Outros";

    // O nome em português é o ID da categoria (BlockCategory/CategoryOrder); só a exibição é traduzida.
    private static string CategoryName(string cat) => cat switch
    {
        "Equipamentos" => Loc.T("cat.equipment", cat),
        "Itens, consumíveis e materiais" => Loc.T("cat.items", cat),
        "Monstros, NPCs e minas" => Loc.T("cat.mobs", cat),
        "Pets" => Loc.T("cat.pets", cat),
        "Receitas e produção" => Loc.T("cat.recipes", cat),
        "Personagem e configurações" => Loc.T("cat.character", cat),
        "Outros" => Loc.T("cat.other", cat),
        _ => cat,
    };

    static readonly string[] CategoryOrder = { "Equipamentos", "Itens, consumíveis e materiais", "Monstros, NPCs e minas", "Pets", "Receitas e produção", "Personagem e configurações", "Outros" };
    // Entrada do filtro de tipo (Major/Sub null => todos).
    private sealed record TypeEntry(string? Major, string? Sub, string Label, Func<string>? Dyn = null)
    {
        public override string ToString() => Dyn?.Invoke() ?? Label;
    }

    private List<Item> _view = new();
    private readonly Dictionary<Item, ItemList> _owner = new(ReferenceEqualityComparer.Instance);

    // Reconstrói o seletor de blocos (chamado ao carregar o elements.data).
    private void BuildTree()
    {
        _iconsRoot = LoadSetting("icons");
        _owner.Clear();
        _blockCombo.BeginUpdate();
        _blockCombo.Items.Clear();
        if (_store.Current is not null)
        {
            var lists = _store.Current.Lists;
            foreach (var l in lists) foreach (var it in l.Items) _owner[it] = l;
            var entries = lists.Select((b, i) => (b, i)).Where(x => x.b.Items.Count > 0).ToList();
            foreach (var cat in CategoryOrder)
            {
                var inCat = entries.Where(x => BlockCategory(x.b.Name) == cat).ToList();
                if (inCat.Count == 0) continue;
                _blockCombo.Items.Add(new BlockEntry(null, $"── {cat} ──", Header: true, Dyn: () => $"── {CategoryName(cat)} ──"));
                foreach (var (b, i) in inCat) _blockCombo.Items.Add(new BlockEntry(b, $"   [{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})",
                Dyn: () => $"   [{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})"));
            }
            // como o Angelica (RAE): ao abrir o programa, a 1ª lista do elements ([000]); depois, a lista em que se está
            // (reabrir ou trocar de PW mantém). Antes lembrava a última de outra sessão, o que parecia aleatório.
            var last = _curBlock?.Name ?? entries.MinBy(x => x.i).b.Name;
            int sel = -1;
            for (int i = 0; i < _blockCombo.Items.Count; i++)
                if (_blockCombo.Items[i] is BlockEntry { Block: { } bb } && (sel < 0 || bb.Name == last)) sel = i;
            _blockCombo.SelectedIndex = Math.Clamp(sel, 0, _blockCombo.Items.Count - 1);
        }
        _blockCombo.EndUpdate();
        Text = _store.Current is null ? "MyAceEditor" : $"MyAceEditor — {_store.Current.Name}";
        if (_store.Current is null) { _view.Clear(); _list.VirtualListSize = 0; }
    }

    private void OnBlockChanged()
    {
        if (_blockCombo.SelectedItem is BlockEntry { Header: true } && _blockCombo.SelectedIndex + 1 < _blockCombo.Items.Count)
        { _blockCombo.SelectedIndex++; return; }
        _curBlock = (_blockCombo.SelectedItem as BlockEntry)?.Block;
        _allBlocks = _blockCombo.SelectedItem is BlockEntry { All: true };
        BuildTypeFilter();
        RefreshView();
    }

    // Filtro de tipo/subtipo: só aparece em blocos *_ESSENCE com id_major_type/id_sub_type.
    private void BuildTypeFilter()
    {
        _typeCombo.BeginUpdate();
        _typeCombo.Items.Clear();
        var b = _curBlock;
        var f = b?.Items.FirstOrDefault();
        bool has = b is not null && b.Name.EndsWith("_ESSENCE") && f is not null
                   && f.Fields.ContainsKey("id_major_type") && f.Fields.ContainsKey("id_sub_type");
        if (has)
        {
            var all = _store.Current!.Lists;
            string prefix = b!.Name[..^"_ESSENCE".Length];
            var majors = NameMap(all.FirstOrDefault(l => l.Name == prefix + "_MAJOR_TYPE"));
            var subs = NameMap(all.FirstOrDefault(l => l.Name == prefix + "_SUB_TYPE"));
            int nAll = b.Items.Count;
            _typeCombo.Items.Add(new TypeEntry(null, null, string.Format(Loc.T("filter.allTypes", "Todos os tipos ({0:N0})"), nAll),
                () => string.Format(Loc.T("filter.allTypes", "Todos os tipos ({0:N0})"), nAll)));
            foreach (var byMajor in b.Items.GroupBy(i => Fv(i, "id_major_type")))
            {
                var mName = majors.GetValueOrDefault(byMajor.Key, Loc.T("filter.type", "Tipo") + " " + byMajor.Key);
                _typeCombo.Items.Add(new TypeEntry(byMajor.Key, null, $"{mName} ({byMajor.Count()})"));
                foreach (var bySub in byMajor.GroupBy(i => Fv(i, "id_sub_type")))
                    _typeCombo.Items.Add(new TypeEntry(byMajor.Key, bySub.Key,
                        $"     {mName} › {subs.GetValueOrDefault(bySub.Key, Loc.T("filter.subtype", "Subtipo") + " " + bySub.Key)} ({bySub.Count()})"));
            }
            _typeCombo.SelectedIndex = 0;
        }
        _typeCombo.Tag = has;
        _typeCombo.Visible = has && _recentMode?.Checked != true && !_favMode;   // em Recentes a coluna mostra só a lista de recentes
        _typeCombo.EndUpdate();
    }

    // ---------- seletor de blocos (busca + categorias + ícone de cada bloco) ----------
    internal Form? LastPicker;   // --snapshot ... picker: print do seletor de bloco
    // seções abertas no seletor de bloco (chave da categoria), guardadas entre sessões
    private readonly HashSet<string> _pickerOpenCats = new((LoadSetting("blockPickOpen") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private bool _pickerOpen;   // evita abrir de novo enquanto o seletor está aberto (piscava abrindo/fechando)
    private readonly Dictionary<ItemList, int> _blockIconCache = new();
    private void ShowBlockPicker()
    {
        if (_store.Current is null || _blockCombo.Items.Count == 0 || _pickerOpen) return;
        _pickerOpen = true;
        var screen = _blockCombo.PointToScreen(new Point(0, _blockCombo.Height));
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, KeyPreview = true,
            Location = screen, Size = new Size(_blockCombo.Width, Math.Min(620, Screen.FromControl(this).WorkingArea.Bottom - screen.Y - 10)),
            BackColor = CTreeBg, Padding = new Padding(1),
        };
        var search = new TextBox { Dock = DockStyle.Top, BackColor = CTreeInput, ForeColor = CTreeFg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f),
            PlaceholderText = Loc.T("blockPick.search", "Procurar bloco (ex.: arma, pet, receita, npc)…") };
        var lv = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = CTreeBg, ForeColor = CTreeFg, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), SmallImageList = _treeIcons, OwnerDraw = true,
        };
        // títulos de seção em faixa (barra azul + negrito + contagem), como na grade; os grupos do Windows ficavam apagados
        // no fundo escuro (teste 11, usuário 01/11). Bloco = Tag int; título = Tag string (chave da categoria); espaço = Tag null.
        // Seções começam recolhidas; clicar no título abre/fecha e fica guardado (blockPickOpen); com busca, tudo aberto.
        lv.DrawItem += (_, e) =>
        {
            if (e.Item.Tag is int) { e.DrawDefault = true; return; }
            var r = e.Bounds;
            using (var bg = new SolidBrush(CTreeBg)) e.Graphics.FillRectangle(bg, r);
            if (e.Item.Tag is not string key) return;   // espaço entre seções
            bool open = search.Text.Trim().Length > 0 || _pickerOpenCats.Contains(key);
            using (var b = new SolidBrush(Color.FromArgb(32, 44, 62))) e.Graphics.FillRectangle(b, r);
            using (var b = new SolidBrush(CAccent)) e.Graphics.FillRectangle(b, r.Left + 2, r.Top + 4, 4, r.Height - 8);
            var chev = UiKit.Icon(open ? "" : "", Color.FromArgb(130, 190, 255), 12);
            e.Graphics.DrawImage(chev, r.Left + 12, r.Top + (r.Height - chev.Height) / 2);
            using var hf = new Font("Segoe UI Semibold", 10f);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, hf, new Rectangle(r.Left + 30, r.Top, r.Width - 32, r.Height), Color.FromArgb(130, 190, 255),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.ItemSelectionChanged += (_, e) => { if (e.IsSelected && e.Item?.Tag is not int) e.Item!.Selected = false; };
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(lv, true);
        // a coluna ocupa a largura útil (sem a barra de rolagem): nada passa da borda da lista (usuário 01/11)
        lv.Columns.Add("", f.Width - 24);
        lv.ClientSizeChanged += (_, _) => { if (lv.Columns.Count > 0 && lv.ClientSize.Width > 40) lv.Columns[0].Width = lv.ClientSize.Width - 2; };
        f.Controls.Add(lv); f.Controls.Add(search);
        // ícone do bloco: o de um item dele (ou a miniatura do modelo 3D)
        // guardado: procurar a cada abertura deixava a lista lenta e os ícones aparecendo aos poucos
        int BlockIcon(ItemList b)
        {
            if (_blockIconCache.TryGetValue(b, out var c)) return c;
            c = -1;
            foreach (var it in b.Items.Take(40)) if (IconIndexFor(it) is var i && i >= 0) { c = i; break; }
            return _blockIconCache[b] = c;
        }
        ListViewItem? target = null;
        void Fill()
        {
            var q = search.Text.Trim();
            target = null;
            lv.BeginUpdate(); lv.Items.Clear();
            var cats = new List<(string Key, string Cat, List<ListViewItem> Items)>();
            for (int i = 0; i < _blockCombo.Items.Count; i++)
            {
                if (_blockCombo.Items[i] is not BlockEntry e || e.Header) continue;
                var text = e.ToString().Trim();
                if (q.Length > 0 && !text.Contains(q, StringComparison.OrdinalIgnoreCase) && !(e.Block?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                var key = e.Block is null ? "top" : BlockCategory(e.Block.Name);
                var cat = e.Block is null ? Loc.T("blockPick.top", "Atalhos") : CategoryName(key);
                var item = new ListViewItem(text, e.Block is null ? -1 : BlockIcon(e.Block)) { Tag = i };
                if (cats.Count == 0 || cats[^1].Key != key) cats.Add((key, cat, new()));
                cats[^1].Items.Add(item);
                if (i == _blockCombo.SelectedIndex) target = item;
            }
            foreach (var (key, cat, items) in cats)
            {
                if (lv.Items.Count > 0) lv.Items.Add(new ListViewItem("") { Tag = null });   // espaço antes da próxima seção
                lv.Items.Add(new ListViewItem($"{cat.ToUpperInvariant()}   ({items.Count})") { Tag = key });
                if (q.Length > 0 || _pickerOpenCats.Contains(key)) lv.Items.AddRange(items.ToArray());
            }
            lv.EndUpdate();
            if (target is null && q.Length > 0) target = lv.Items.Cast<ListViewItem>().FirstOrDefault(x => x.Tag is int);
            if (lv.IsHandleCreated) MarkTarget();
        }
        // a seleção feita antes de a lista existir se perdia: abria no topo sem nada marcado (print do --snapshot)
        void MarkTarget()
        {
            if (target is null || target.ListView != lv) return;
            target.Selected = true; target.Focused = true;
            // rola um pouco além para o bloco ficar no meio da lista, não colado embaixo
            lv.Items[Math.Min(lv.Items.Count - 1, target.Index + 10)].EnsureVisible();
            target.EnsureVisible();
        }
        void Choose()
        {
            if (lv.SelectedItems.Count == 0 || lv.SelectedItems[0].Tag is not int idx) return;   // título/espaço: não fecha
            _blockCombo.SelectedIndex = idx;
            f.Close();
        }
        search.TextChanged += (_, _) => Fill();
        lv.ItemActivate += (_, _) => Choose();
        lv.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || lv.GetItemAt(e.X, e.Y) is not { } hit) return;
            if (hit.Tag is string key && search.Text.Trim().Length == 0)
            {
                if (!_pickerOpenCats.Remove(key)) _pickerOpenCats.Add(key);
                SaveSetting("blockPickOpen", string.Join("|", _pickerOpenCats));
                int top = lv.TopItem?.Index ?? 0;
                Fill();
                if (lv.Items.Count > 0) lv.TopItem = lv.Items[Math.Min(top, lv.Items.Count - 1)];
                return;
            }
            if (hit.Tag is int) Choose();
        };
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) f.Close();
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Choose(); }
            else if (e.KeyCode is Keys.Down or Keys.Up && search.Focused) { lv.Focus(); }
        };
        f.Deactivate += (_, _) => f.Close();
        f.FormClosed += (_, _) => { f.Dispose(); BeginInvoke(() => _pickerOpen = false); };
        // rolar até o bloco atual só depois de a lista ter tamanho (antes abria sempre no topo)
        f.Shown += (_, _) => MarkTarget();
        Fill();
        LastPicker = f;
        f.Show(this);
        MarkTarget();
        search.Focus();
    }

    private void DrawDarkCombo(object? s, DrawItemEventArgs e)
    {
        if (s is not ComboBox cb) return;
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var bg = new SolidBrush(sel ? CListSel : CTreeInput)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0) return;
        var o = cb.Items[e.Index];
        var col = o is BlockEntry { Header: true } ? CListId : o is BlockEntry { Block: null } ? GGold : CTreeFg;
        // filtro de tipo: tipo principal = título de seção (faixa + barra azul + negrito), subtipos recuados embaixo
        if (o is TypeEntry { Major: not null, Sub: null } && (e.State & DrawItemState.ComboBoxEdit) == 0)
        {
            if (!sel) using (var b = new SolidBrush(Color.FromArgb(32, 44, 62))) e.Graphics.FillRectangle(b, e.Bounds);
            using (var b = new SolidBrush(CAccent)) e.Graphics.FillRectangle(b, e.Bounds.Left + 2, e.Bounds.Top + 4, 4, e.Bounds.Height - 8);
            using var hf = new Font(cb.Font, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, o.ToString(), hf, new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, e.Bounds.Width - 14, e.Bounds.Height),
                sel ? Color.White : Color.FromArgb(130, 190, 255), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        if (o is TypeEntry { Major: null }) col = GGold;
        TextRenderer.DrawText(e.Graphics, o?.ToString(), cb.Font, Rectangle.Inflate(e.Bounds, -4, 0), sel ? Color.White : col,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

    private bool _allBlocks;   // seletor de bloco em "Todos os blocos"

    private static string Fv(Item it, string k) => it.Fields.GetValueOrDefault(k)?.Value ?? "0";

    // Recalcula os itens visíveis (bloco/favoritos + filtro de tipo + busca + ordenação).
    private void RefreshView()
    {
        if (_store.Current is null) return;
        string q = _search.Text.Trim();
        IEnumerable<Item> src;
        if ((q.Length > 0 && _searchAll.Checked && !_favMode) || (_curBlock is null && _allBlocks))
            src = _store.Current.Lists.SelectMany(l => l.Items);
        else if (_favMode || _curBlock is null)
            src = _store.Current.Lists.SelectMany(l => l.Items.Where(it => _favorites.Contains(FavKey(l, it))));
        else
        {
            src = _curBlock.Items;
            if (_typeCombo.Visible && _typeCombo.SelectedItem is TypeEntry { Major: { } mj } te)
                src = src.Where(i => Fv(i, "id_major_type") == mj && (te.Sub is null || Fv(i, "id_sub_type") == te.Sub));
        }
        if (q.Length > 0)
            src = src.Where(it => it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || it.Id.Contains(q));
        _view = Sorted(src).ToList();

        UpdateCounters();
        // a lista virtual guarda a rolagem antiga ao encolher (linhas vazias no topo até clicar): zera antes
        // tamanho 0 ANTES de limpar a seleção: limpar com o tamanho antigo pede a linha selecionada, que pode não
        // existir mais na lista nova (ex.: o item excluído era o último)
        _list.BeginUpdate();
        _list.VirtualListSize = 0;
        _list.SelectedIndices.Clear();
        _list.VirtualListSize = _view.Count;
        if (_view.Count > 0) _list.EnsureVisible(0);
        _list.EndUpdate();
        _list.Invalidate();
        if (_current is not null && _view.Contains(_current)) SelectItem(_current, focus: false);
        else if (_view.Count > 0) { _current = _autoShown = _view[0]; SelectItem(_current, focus: false); ShowItem(_current); }
    }

    private void OnRetrieveItem(object? s, RetrieveVirtualItemEventArgs e)
    {
        // a lista pode pedir uma linha antiga no meio da troca de tamanho: linha vazia em vez de erro
        if (e.ItemIndex < 0 || e.ItemIndex >= _view.Count) { e.Item = new ListViewItem(""); e.Item.SubItems.Add(""); e.Item.SubItems.Add(""); return; }
        var it = _view[e.ItemIndex];
        e.Item = new ListViewItem(it.Id, IconIndexFor(it)) { Tag = it };
        e.Item.SubItems.Add(string.IsNullOrEmpty(it.Name) ? Loc.T("block.noName", "(sem nome)") : it.Name);
        e.Item.SubItems.Add("");
    }

    // sem ícone no jogo (monstros, NPCs…): miniatura do modelo 3D, feita em segundo plano
    private ModelThumbs? _thumbs;
    private readonly List<(string Ecm, Image? Img)> _thumbBatch = new();
    private readonly System.Windows.Forms.Timer _thumbFlush = new() { Interval = 1000 };
    private bool _thumbFlushHooked;
    private string? _thumbsDir;
    private int ThumbIndexFor(Item it)
    {
        if (!it.Fields.TryGetValue("file_model", out var fm) || !fm.Value.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase)) return -1;
        var key = "ecm:" + fm.Value;
        if (_iconIdx.TryGetValue(key, out var idx)) return idx;
        var el = _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath));
        if (el is null || !PckArchive.Exists(Path.Combine(el, "models.pck"))) return -1;
        if (_thumbs is null || _thumbsDir != el)
        {
            _thumbs?.Dispose(); _thumbsDir = el;
            // chegam uma a uma do segundo plano; entram na lista de imagens em lote (cada adição refaz a lista na tela)
            _thumbs = new ModelThumbs(el, this, 48, (ecm, img) => { _thumbBatch.Add((ecm, img)); if (!_thumbFlush.Enabled) _thumbFlush.Start(); });
            if (!_thumbFlushHooked)
            {
                _thumbFlushHooked = true;
                _thumbFlush.Tick += (_, _) =>
                {
                    _thumbFlush.Stop();
                    if (_thumbBatch.Count == 0) return;
                    var imgs = _thumbBatch.Where(t => t.Img is not null).Select(t => t.Img!).ToArray();
                    int first = _treeIcons.Images.Count;
                    if (imgs.Length > 0) _treeIcons.Images.AddRange(imgs);
                    int k = 0;
                    foreach (var (ecm, img) in _thumbBatch) _iconIdx["ecm:" + ecm] = img is null ? -1 : first + k++;
                    _thumbBatch.Clear();
                    _list.Invalidate();
                };
            }
        }
        _thumbs.Request(fm.Value);
        return -1;
    }

    private int IconIndexFor(Item it)
    {
        if (!it.Fields.TryGetValue("file_icon", out var fi) || string.IsNullOrEmpty(fi.Value)) return ThumbIndexFor(it);
        if (_atlas is null && _iconsRoot is null) return -1;
        if (_iconIdx.TryGetValue(fi.Value, out var idx)) return idx;
        var img = IconFor(it);
        if (img is null) { _iconIdx[fi.Value] = -1; return -1; }
        _treeIcons.Images.Add(img);
        int i = _treeIcons.Images.Count - 1;
        _iconIdx[fi.Value] = i;
        return i;
    }

    // Desenho das linhas: fundo preto, ícone, nome na cor de grade do PW.
    private void OnDrawSubItem(object? s, DrawListViewSubItemEventArgs e)
    {
        if (e.Item?.Tag is not Item it) return;
        bool sel = e.Item.Selected;
        var g = e.Graphics;
        using (var bg = new SolidBrush(sel ? CListSel : (e.ItemIndex % 2 == 0 ? CTreeBg : CTreeAlt))) g.FillRectangle(bg, e.Bounds);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (e.ColumnIndex == 0)
        {
            TextRenderer.DrawText(g, it.Id, _list.Font, Rectangle.Inflate(e.Bounds, -6, 0), sel ? Color.White : CListId, flags | TextFormatFlags.Right);
            return;
        }
        if (e.ColumnIndex == 2)
        {
            int uses = LinkCount(it);
            TextRenderer.DrawText(g, uses.ToString(), _list.Font, Rectangle.Inflate(e.Bounds, -6, 0), sel ? Color.White : uses > 0 ? GGold : Color.FromArgb(90, 96, 106), flags | TextFormatFlags.Right);
            return;
        }
        int x = e.Bounds.X + 4;
        int img = IconIndexFor(it);
        if (img >= 0)
        {
            int sz = _treeIcons.ImageSize.Height;
            g.DrawImage(_treeIcons.Images[img], x, e.Bounds.Y + (e.Bounds.Height - sz) / 2, sz, sz);
            x += sz + 6;
        }
        else x += _treeIcons.ImageSize.Width + 6;
        var r = new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height);
        TextRenderer.DrawText(g, e.SubItem!.Text, _list.Font, r, sel ? Color.White : NameColor(it), flags);
    }

    private void OnDrawHeader(object? s, DrawListViewColumnHeaderEventArgs e)
    {
        using (var bg = new SolidBrush(CTreeInput)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var p = new Pen(Color.FromArgb(55, 59, 67))) e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Y, e.Bounds.Right - 1, e.Bounds.Bottom);
        string arrow = (e.ColumnIndex == 0 && _sortMode == SortMode.Codigo) || (e.ColumnIndex == 1 && _sortMode == SortMode.Nome) ? "  ▾" : "";
        TextRenderer.DrawText(e.Graphics, e.Header!.Text + arrow, _list.Font, Rectangle.Inflate(e.Bounds, -6, 0), CTreeFg,
            TextFormatFlags.VerticalCenter | (e.ColumnIndex == 0 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
    }

    private void OnColumnClick(int col)
    {
        _sortMode = col == 0 ? SortMode.Codigo : SortMode.Nome;
        RefreshView();
    }

    // Rodapé e cabeçalho da lista (total, no bloco, visíveis); também reaplicado ao trocar o idioma.
    private void UpdateCounters()
    {
        if (_store.Current is null) { _list.Columns[1].Text = Loc.T("list.name", "Nome"); return; }
        int total = _curBlock?.Items.Count ?? _view.Count;
        _stTotal.Text = string.Format(Loc.T("status.total", "Total de elementos: {0:N0}"), _store.Current.Lists.Sum(l => l.Items.Count));
        _stBlock.Text = string.Format(Loc.T("status.inBlock", "No bloco: {0:N0} · visíveis: {1:N0}"), total, _view.Count);
        _list.Columns[1].Text = string.Format(Loc.T("list.nameCount", "Nome ({0:N0} de {1:N0})"), _view.Count, total);
    }

    private IEnumerable<Item> Sorted(IEnumerable<Item> items) =>
        _sortMode == SortMode.Nome
            ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(i => long.TryParse(i.Id, out var n) ? n : long.MaxValue);

    private List<Item> SelectedItems() => _list.SelectedIndices.Cast<int>().Where(i => i < _view.Count).Select(i => _view[i]).ToList();

    private void SelectAllItems()
    {
        _list.BeginUpdate();
        for (int i = 0; i < _view.Count; i++) _list.SelectedIndices.Add(i);
        _list.EndUpdate();
        Status(string.Format(Loc.T("status.selectedCount", "{0:N0} itens selecionados"), _view.Count));
    }

    private void BulkClone()
    {
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        Item? last = null;
        using var batch = _edits.Batch(string.Format(Loc.T("hist.clone", "Clonar {0} item(ns)"), sel.Count));
        foreach (var src in sel)
        {
            if (!_owner.TryGetValue(src, out var block)) continue;
            var ni = new Item { Id = NextCode(block).ToString(), Name = src.Name, Fields = src.Fields.Clone(), Raw = src.Raw?.ToArray() };
            SetIdName(ni);
            _edits.AddItem(block, ni, ""); _owner[ni] = block; last = ni;
        }
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        RefreshView();
        if (last is not null) { _current = last; SelectItem(last); ShowItem(last); }
        Status(string.Format(Loc.T("status.clonedCount", "{0} item(ns) clonado(s)"), sel.Count));
    }

    private void BulkDelete()
    {
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        if (MessageBox.Show(string.Format(Loc.T("confirm.deleteSelected", "Excluir {0} item(ns) selecionado(s)?"), sel.Count), Loc.T("confirm.delete", "Excluir"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        using (_edits.Batch(string.Format(Loc.T("hist.delete", "Excluir {0} item(ns)"), sel.Count)))
            foreach (var it in sel) if (_owner.TryGetValue(it, out var b)) { _edits.RemoveItem(b, it, ""); _owner.Remove(it); _favorites.Remove(FavKey(b, it)); }
        SaveFavorites();
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status(string.Format(Loc.T("status.deletedCount", "{0} item(ns) excluído(s)"), sel.Count));
    }

    private void BulkChange() => BulkChangeOn(SelectedItems(), _grid.CurrentRow?.Tag as string);

    // Itens de vários blocos (pesquisa em todos): só os campos que todos têm
    private void BulkChangeOn(List<Item> sel, string? start)
    {
        if (sel.Count == 0) return;
        // mesmos campos da grade (sem realname nem os sem uso, se estiverem ocultos); começa no campo selecionado na grade
        var hidden = _hideUnused && BlockContaining(sel[0]) is { } blk ? UnusedFields(blk) : new HashSet<string>();
        var fields = sel[0].Fields.Keys.Where(k => k != "realname" && !hidden.Contains(k) && sel.All(it => it.Fields.ContainsKey(k))).ToList();
        if (FieldEditors.BulkValue(this, sel, fields, FieldLabels.Label, start) is not { } op) return;
        var rng = new Random(op.Seed);   // mesma semente da prévia
        int n = 0, idx = 0;
        using (_edits.Batch(string.Format(Loc.T("hist.bulk", "{0} em {1} itens"), FieldLabels.Label(op.Field), sel.Count)))
            foreach (var it in sel)
            {
                if (!it.Fields.TryGetValue(op.Field, out var f) || FieldEditors.BulkApply(op, f, idx++, rng) is not { } v) continue;
                SetField(it, op.Field, v, refresh: false);
                n++;
            }
        if (_current is not null) ShowItem(_current);
        Status(string.Format(Loc.T("status.bulkDone", "'{0}' alterado em {1} item(ns)"), FieldLabels.Label(op.Field), n));
    }

    private void OnListSelect()
    {
        if (_list.SelectedIndices.Count == 0) return;
        var it = _view[_list.SelectedIndices[0]];
        _current = it;
        if (_owner.TryGetValue(it, out var b) && _curBlock is null) { /* favoritos/busca global: bloco do item */ }
        ShowItem(it);
    }

    // Bloco dono do item atual (no modo Favoritos/busca global o bloco vem do item).
    private ItemList? CurrentItemBlock => _current is not null && _owner.TryGetValue(_current, out var b) ? b : _curBlock;

    private void SelectItem(Item it, bool focus = true)
    {
        int i = _view.IndexOf(it);
        if (i < 0) return;
        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(i);
        _list.EnsureVisible(i);
        if (focus) _list.Focus();
    }

    // "Ir para ID" (rodapé): procura no bloco atual; se não achar, em todos.
    private void GoToId()
    {
        var id = ((long)_gotoId.Value).ToString();
        var hit = _view.FirstOrDefault(i => i.Id == id);
        if (hit is null && _store.Current is not null)
        {
            var any = _store.Current.Lists.SelectMany(l => l.Items).FirstOrDefault(i => i.Id == id);
            if (any is not null && _owner.TryGetValue(any, out var b))
            {
                foreach (var o in _blockCombo.Items) if (o is BlockEntry be && be.Block == b) { _blockCombo.SelectedItem = be; break; }
                hit = any;
            }
        }
        if (hit is null) { Status(string.Format(Loc.T("status.idNotFound", "ID {0} não encontrado"), id)); return; }
        _current = hit; SelectItem(hit); ShowItem(hit);
    }

    private ItemList? BlockContaining(Item it) => _owner.GetValueOrDefault(it);

    private static Dictionary<string, string> NameMap(ItemList? b)
    {
        var d = new Dictionary<string, string>();
        if (b is not null) foreach (var it in b.Items) d[it.Id] = it.Name;
        return d;
    }

    // Cor do nome: tabela oficial do cliente (item_color.txt) quando houver; senão, pelos símbolos do nome.
    private Color NameColor(Item it)
    {
        if (_cfgs?.NameColor(it.Id) is { } c && !(c.R == 255 && c.G == 255 && c.B == 255)) return c;
        var g = GradeColor(it.Name);
        return g == GNormal && _cfgs is not null && _cfgs.ItemColors.Count > 0 ? GNormal : g;
    }

    private static Color GradeColor(string name)
    {
        int g = 0;
        foreach (var ch in name)
        {
            if (ch == '*' || ch == '★' || ch == '☆') g++;
            else if (ch == '»' || ch == '«') return GGold;
            else break;
        }
        return g switch { 0 => GNormal, 1 => G1, 2 => G2, 3 => G3, _ => GGold };
    }

    private static readonly Dictionary<string, string> BlockPt = new()
    {
        ["WEAPON_ESSENCE"] = "Armas", ["ARMOR_ESSENCE"] = "Armaduras", ["DECORATION_ESSENCE"] = "Acessórios",
        ["MEDICINE_ESSENCE"] = "Poções", ["MATERIAL_ESSENCE"] = "Materiais", ["PROJECTILE_ESSENCE"] = "Projéteis",
        ["QUIVER_ESSENCE"] = "Aljavas", ["STONE_ESSENCE"] = "Pedras", ["FASHION_ESSENCE"] = "Moda",
        ["PET_ESSENCE"] = "Pets", ["PET_EGG_ESSENCE"] = "Ovos de Pet", ["PET_FOOD_ESSENCE"] = "Comida de Pet",
        ["RECIPE_ESSENCE"] = "Receitas", ["MONSTER_ESSENCE"] = "Monstros", ["NPC_ESSENCE"] = "NPCs",
        ["FACE_TEXTURE_ESSENCE"] = "Texturas de Face", ["FACE_HAIR_ESSENCE"] = "Cabelos",
        ["FIREWORKS_ESSENCE"] = "Fogos", ["SUITE_ESSENCE"] = "Conjuntos", ["REFINE_TICKET_ESSENCE"] = "Tíquetes de Refino",
        ["ELEMENT_ESSENCE"] = "Elementos", ["TOWNSCROLL_ESSENCE"] = "Pergaminhos de Cidade",
        ["EQUIPMENT_ADDON"] = "Atributos de Equipamento", ["MINE_ESSENCE"] = "Minas", ["SKILLTOME_ESSENCE"] = "Livros de Habilidade",
        ["FLYSWORD_ESSENCE"] = "Montarias Voadoras", ["TASKMATTER_ESSENCE"] = "Itens de Missão", ["TALK_PROC"] = "Diálogos de NPC",
        // demais blocos do v60 (nomes nossos; ingles em lang/en.json, chave block.*)
        ["WEAPON_MAJOR_TYPE"] = "Tipos de Arma", ["WEAPON_SUB_TYPE"] = "Subtipos de Arma", ["ARMOR_MAJOR_TYPE"] = "Tipos de Armadura",
        ["ARMOR_SUB_TYPE"] = "Subtipos de Armadura", ["DECORATION_MAJOR_TYPE"] = "Tipos de Acessório", ["DECORATION_SUB_TYPE"] = "Subtipos de Acessório",
        ["MEDICINE_MAJOR_TYPE"] = "Tipos de Poção", ["MEDICINE_SUB_TYPE"] = "Subtipos de Poção", ["MATERIAL_MAJOR_TYPE"] = "Tipos de Material",
        ["MATERIAL_SUB_TYPE"] = "Subtipos de Material", ["DAMAGERUNE_SUB_TYPE"] = "Subtipos de Runa de Ataque", ["DAMAGERUNE_ESSENCE"] = "Runas de Ataque",
        ["ARMORRUNE_SUB_TYPE"] = "Subtipos de Runa de Defesa", ["ARMORRUNE_ESSENCE"] = "Runas de Defesa", ["SKILLTOME_SUB_TYPE"] = "Subtipos de Livro de Habilidade",
        ["WINGMANWING_ESSENCE"] = "Asas", ["UNIONSCROLL_ESSENCE"] = "Pergaminhos de Clã", ["REVIVESCROLL_ESSENCE"] = "Pergaminhos de Ressurreição",
        ["TOSSMATTER_ESSENCE"] = "Itens de Arremesso", ["PROJECTILE_TYPE"] = "Tipos de Munição", ["QUIVER_SUB_TYPE"] = "Subtipos de Aljava",
        ["STONE_SUB_TYPE"] = "Subtipos de Pedra", ["MONSTER_ADDON"] = "Atributos de Monstro", ["MONSTER_TYPE"] = "Tipos de Monstro",
        ["NPC_TALK_SERVICE"] = "NPC: Conversa", ["NPC_SELL_SERVICE"] = "NPC: Loja", ["NPC_BUY_SERVICE"] = "NPC: Compra",
        ["NPC_REPAIR_SERVICE"] = "NPC: Conserto", ["NPC_INSTALL_SERVICE"] = "NPC: Encaixar Pedras", ["NPC_UNINSTALL_SERVICE"] = "NPC: Remover Pedras",
        ["NPC_TASK_IN_SERVICE"] = "NPC: Entregar Quest", ["NPC_TASK_OUT_SERVICE"] = "NPC: Dar Quest", ["NPC_TASK_MATTER_SERVICE"] = "NPC: Itens de Quest",
        ["NPC_SKILL_SERVICE"] = "NPC: Ensinar Habilidade", ["NPC_HEAL_SERVICE"] = "NPC: Cura", ["NPC_TRANSMIT_SERVICE"] = "NPC: Teletransporte",
        ["NPC_TRANSPORT_SERVICE"] = "NPC: Transporte", ["NPC_PROXY_SERVICE"] = "NPC: Procurador", ["NPC_STORAGE_SERVICE"] = "NPC: Armazém",
        ["NPC_MAKE_SERVICE"] = "NPC: Fabricação", ["NPC_DECOMPOSE_SERVICE"] = "NPC: Decompor", ["NPC_TYPE"] = "Tipos de NPC",
        ["FACE_SHAPE_ESSENCE"] = "Formatos de Rosto", ["FACE_EMOTION_TYPE"] = "Tipos de Emoção", ["FACE_EXPRESSION_ESSENCE"] = "Expressões",
        ["FACE_MOUSTACHE_ESSENCE"] = "Bigodes", ["COLORPICKER_ESSENCE"] = "Paletas de Cor", ["CUSTOMIZEDATA_ESSENCE"] = "Personalização",
        ["RECIPE_MAJOR_TYPE"] = "Tipos de Receita", ["RECIPE_SUB_TYPE"] = "Subtipos de Receita", ["ENEMY_FACTION_CONFIG"] = "Facções Inimigas",
        ["CHARRACTER_CLASS_CONFIG"] = "Classes de Personagem", ["PARAM_ADJUST_CONFIG"] = "Ajustes por Nível e Grupo", ["PLAYER_ACTION_INFO_CONFIG"] = "Ações do Personagem",
        ["TASKDICE_ESSENCE"] = "Bilhetes de Quest", ["TASKNORMALMATTER_ESSENCE"] = "Itens Comuns de Quest", ["FACE_FALING_ESSENCE"] = "Feições",
        ["PLAYER_LEVELEXP_CONFIG"] = "EXP por Nível", ["MINE_TYPE"] = "Tipos de Mina", ["NPC_IDENTIFY_SERVICE"] = "NPC: Identificar",
        ["FASHION_MAJOR_TYPE"] = "Tipos de Moda", ["FASHION_SUB_TYPE"] = "Subtipos de Moda", ["FACETICKET_MAJOR_TYPE"] = "Tipos de Cupom de Rosto",
        ["FACETICKET_SUB_TYPE"] = "Subtipos de Cupom de Rosto", ["FACETICKET_ESSENCE"] = "Cupons de Rosto", ["FACEPILL_MAJOR_TYPE"] = "Tipos de Pílula de Rosto",
        ["FACEPILL_SUB_TYPE"] = "Subtipos de Pílula de Rosto", ["FACEPILL_ESSENCE"] = "Pílulas de Transformação", ["GM_GENERATOR_TYPE"] = "Tipos de Gerador GM",
        ["GM_GENERATOR_ESSENCE"] = "Geradores GM", ["PET_TYPE"] = "Tipos de Pet", ["PET_FACETICKET_ESSENCE"] = "Cupons de Aparência de Pet",
        ["WAR_TANKCALLIN_ESSENCE"] = "Convocação de Catapulta", ["NPC_WAR_TOWERBUILD_SERVICE"] = "NPC: Torres de Guerra", ["PLAYER_SECONDLEVEL_CONFIG"] = "Cultivo",
        ["NPC_RESETPROP_SERVICE"] = "NPC: Redistribuir Atributos", ["NPC_PETNAME_SERVICE"] = "NPC: Nome de Pet", ["NPC_PETLEARNSKILL_SERVICE"] = "NPC: Pet Aprender Habilidade",
        ["NPC_PETFORGETSKILL_SERVICE"] = "NPC: Pet Esquecer Habilidade", ["SKILLMATTER_ESSENCE"] = "Itens de Habilidade", ["DESTROYING_ESSENCE"] = "Itens em Destruição",
        ["NPC_EQUIPBIND_SERVICE"] = "NPC: Vincular Equipamento", ["NPC_EQUIPDESTROY_SERVICE"] = "NPC: Destruir Vinculado", ["NPC_EQUIPUNDESTROY_SERVICE"] = "NPC: Cancelar Destruição",
        ["BIBLE_ESSENCE"] = "Tomos", ["SPEAKER_ESSENCE"] = "Megafones", ["AUTOHP_ESSENCE"] = "Recuperação Automática de HP",
        ["AUTOMP_ESSENCE"] = "Recuperação Automática de MP", ["DOUBLE_EXP_ESSENCE"] = "EXP Dupla", ["TRANSMITSCROLL_ESSENCE"] = "Pergaminhos de Teletransporte",
        ["DYE_TICKET_ESSENCE"] = "Corantes", ["GOBLIN_ESSENCE"] = "Gênios", ["GOBLIN_EQUIP_TYPE"] = "Tipos de Equipamento de Gênio",
        ["GOBLIN_EQUIP_ESSENCE"] = "Equipamentos de Gênio", ["GOBLIN_EXPPILL_ESSENCE"] = "Pílulas de EXP de Gênio", ["SELL_CERTIFICATE_ESSENCE"] = "Certificados de Venda",
        ["TARGET_ITEM_ESSENCE"] = "Itens de Alvo", ["LOOK_INFO_ESSENCE"] = "Itens de Inspeção", ["UPGRADE_PRODUCTION_CONFIG"] = "Melhoria de Produção",
        ["ACC_STORAGE_BLACKLIST_CONFIG"] = "Proibidos no Armazém da Conta", ["FACE_HAIR_TEXTURE_MAP"] = "Texturas de Cabelo", ["MULTI_EXP_CONFIG"] = "Multiplicadores de EXP",
        ["INC_SKILL_ABILITY_ESSENCE"] = "Aumento de Habilidade", ["GOD_EVIL_CONVERT_CONFIG"] = "Conversão Sábio/Demônio", ["WEDDING_CONFIG"] = "Casamento",
        ["WEDDING_BOOKCARD_ESSENCE"] = "Reservas de Casamento", ["WEDDING_INVITECARD_ESSENCE"] = "Convites de Casamento", ["SHARPENER_ESSENCE"] = "Amoladores",
        ["FACE_THIRDEYE_ESSENCE"] = "Terceiro Olho", ["FACTION_FORTRESS_CONFIG"] = "Fortaleza do Clã", ["FACTION_BUILDING_SUB_TYPE"] = "Subtipos de Construção do Clã",
        ["FACTION_BUILDING_ESSENCE"] = "Construções do Clã", ["FACTION_MATERIAL_ESSENCE"] = "Materiais do Clã", ["CONGREGATE_ESSENCE"] = "Convocação",
        ["ENGRAVE_MAJOR_TYPE"] = "Tipos de Gravação", ["ENGRAVE_SUB_TYPE"] = "Subtipos de Gravação", ["ENGRAVE_ESSENCE"] = "Gravações",
        ["NPC_ENGRAVE_SERVICE"] = "NPC: Gravação", ["NPC_RANDPROP_SERVICE"] = "NPC: Atributo Aleatório", ["RANDPROP_TYPE"] = "Tipos de Atributo Aleatório",
        ["RANDPROP_ESSENCE"] = "Atributos Aleatórios", ["WIKI_TABOO_CONFIG"] = "Wiki: Proibidos",
    };
    private static string Pretty(string block) =>
        BlockPt.TryGetValue(block, out var p) ? Loc.T("block." + block, p)
        : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(block.Replace("_ESSENCE", "").Replace("_", " ").ToLowerInvariant());

    // ---------- traduzir com outro elements.data (como o DataTranslator / "Localization" do RAE) ----------
    // Copia nomes e textos (campos de texto UTF-16) pelo ID, bloco a bloco, de outro elements.data já traduzido,
    // de qualquer versão. Só troca o que é diferente e não vazio no outro arquivo. Um passo de desfazer.
    // O elements.data escolhido dentro de outro Perfect World (element\data\) traduz tudo o que aquele PW tiver
    // (regra: o que está aberto é de um PW só): vai para a tradução a partir do cliente, com o elements incluído.
    private void TranslateFromOther()
    {
        if (_store.Current?.Source is not ElementsLayout) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        using var dlg = new OpenFileDialog { Filter = "elements.data (*.data)|*.data|*.*|*.*", Title = Loc.T("tr.pick", "elements.data já traduzido (qualquer versão)") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (Path.GetDirectoryName(Path.GetDirectoryName(dlg.FileName)) is { } other && IsClientElement(other)) TranslateFromClient(other);
        else TranslateFromOther(dlg.FileName);
    }

    /// <summary>Pasta element\ de um cliente: tem o executável ou algum dos arquivos de texto do jogo além do elements.</summary>
    private static bool IsClientElement(string dir) =>
        File.Exists(Path.Combine(dir, "elementclient.exe")) || File.Exists(Path.Combine(dir, "data", "tasks.data"))
        || File.Exists(Path.Combine(dir, "configs.pck")) || File.Exists(Path.Combine(dir, "interfaces.pck"));

    private void TranslateFromOther(string otherPath)
    {
        if (_store.Current is not { } mod || mod.Source is not ElementsLayout layout) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        Module other;
        // com andamento: ler e conferir outro elements.data leva alguns segundos
        try { other = ReadProgressDialog.Run(this, Loc.T("tr.reading", "Lendo o elements.data traduzido"), ctx => CfgCatalog.Load(otherPath, context: ctx).Module); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Error(Loc.T("tr.loadFail", "Não consegui ler o outro arquivo"), ex); return; }
        var plan = ElementsTranslator.Plan(mod, ElementsCfg.Load(layout.ConfigPath), other);
        if (plan.Count == 0) { MessageBox.Show(this, Loc.T("tr.none", "Nada a traduzir: os textos já são iguais ao outro arquivo."), Loc.T("tr.title", "Traduzir")); return; }
        using var win = new TranslateWindow(plan, other.Version & 0xFFFF, Pretty);
        if (win.ShowDialog(this) != DialogResult.OK || win.Selected.Count == 0) return;
        var changes = win.Selected.Select(c => (c.Item, c.Field, c.Value)).ToList();
        int skipped = 0;
        using (_edits.Batch(string.Format(Loc.T("hist.translate", "Traduzir {0:N0} textos"), changes.Count)))
            foreach (var (it, f, v) in changes)
            {
                try { _edits.SetField(it, f, Field.Of(it.Fields[f].Type, v), ""); }
                catch { skipped++; }
            }
        _unusedCache.Clear(); _list.Invalidate();
        if (_current is not null) ShowItem(_current);
        Status(string.Format(Loc.T("tr.done", "{0:N0} textos traduzidos (salve para gravar)"), changes.Count - skipped));
    }

    // ---------- traduzir a partir de outro cliente (mesma versão, mais alta ou mais baixa) ----------
    // Tudo o que é texto: elements (janela de revisão), quests, falas dos monstros e, se o jogo for o mesmo programa,
    // os pacotes de interface/descrições e a ajuda. O cliente aberto só recebe o que ele tem; estrutura e versão ficam.
    private void TranslateFromClient() => TranslateFromClient(null);

    /// <param name="sourceElement">pasta element\ do outro PW já escolhida (pelo elements.data); null = perguntar a pasta</param>
    private void TranslateFromClient(string? sourceElement)
    {
        if (_dataPath is null || Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) is not { } target)
        { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        if (_tasksDirty || _aiView?.HasUnsaved == true)
        {
            MessageBox.Show(this, Loc.T("trc.saveFirst", "Há quests ou AI Policy com alterações não salvas. Salve (ou desfaça) antes de traduzir."), Loc.T("trc.title", "Traduzir a partir de outro cliente"));
            return;
        }
        string folder;
        if (sourceElement is not null) folder = sourceElement;
        else
        {
            using var pick = new FolderBrowserDialog { Description = Loc.T("trc.pick", "Pasta do outro Perfect World (o já traduzido, de qualquer versão)"), SelectedPath = LoadSetting("translate_source") ?? "" };
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            folder = pick.SelectedPath;
        }
        SaveSetting("translate_source", folder);
        var srcEl = FindFile(folder, "elements.data");
        if (srcEl is null || Path.GetDirectoryName(Path.GetDirectoryName(srcEl)) is not { } source) { Status(Loc.T("trc.noElements", "Não achei elements.data nessa pasta.")); return; }
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) { Status(Loc.T("trc.same", "É a mesma pasta do cliente aberto.")); return; }

        List<ClientTranslator.Step> steps;
        // quests de qualquer versão e pacotes levam alguns segundos: fora da tela
        try { steps = ReadProgressDialog.Run(this, Loc.T("trc.comparing", "Comparando os dois Perfect World"), ctx => ClientTranslator.Plan(target, source, ctx), allowCancel: false); }
        catch (Exception ex) { Error(Loc.T("trc.planFail", "Não consegui comparar os clientes"), ex); return; }
        bool running = GameRunningFrom(target);

        // pelo nome da pasta do cliente (a que contém "element"); o caminho inteiro fica na dica
        static string ClientName(string element) => Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(element).TrimEnd('\\')) ?? element);
        const string elementsRow = "elements";
        var rows = new List<object> { elementsRow };
        var lines = new List<ClientTranslateWindow.Row> { new(Loc.T("trc.elements", "Itens, monstros, NPCs e habilidades (elements.data) — revisar antes"),
            Loc.T("trc.elementsDetail", "Nomes e textos pelo ID, bloco e campo, em qualquer versão; abre a janela de revisão e entra no Desfazer."), true, true) };
        foreach (var s in steps) { rows.Add(s); lines.Add(new(s.Title, s.Detail, s.Default, s.Enabled)); }
        using var f = new ClientTranslateWindow(ClientName(source), source, ClientName(target), target, running, lines);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var chosen = f.Chosen.Select(i => rows[i]).ToList();
        if (chosen.Count == 0) return;
        if (chosen.Any(c => c is ClientTranslator.Step) && GameRunningFrom(target))
        {
            MessageBox.Show(this, Loc.T("trc.running", "Feche o jogo deste cliente antes de aplicar."), Loc.T("trc.title", "Traduzir a partir de outro cliente"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var log = new List<string>(); var touched = new List<string>();
        var todo = chosen.OfType<ClientTranslator.Step>().ToList();
        if (todo.Count > 0)
            ReadProgressDialog.Run(this, Loc.T("trc.title", "Traduzir a partir de outro cliente"), ctx =>
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    ctx.Report((double)i / todo.Count, todo[i].Title, string.Format(Loc.T("trc.stepOf", "{0} de {1}"), i + 1, todo.Count), force: true);
                    try { touched.Add(todo[i].Apply(m => { lock (log) log.Add(m); })); }
                    catch (Exception ex) { lock (log) log.Add($"{todo[i].Title}: " + Loc.T("trc.failed", "falhou — ") + ex.Message); }
                }
                return touched;
            }, allowCancel: false);
        // o editor passa a mostrar o que foi gravado
        if (_tasksPath is not null && touched.Any(t => string.Equals(t, _tasksPath, StringComparison.OrdinalIgnoreCase))) LoadTasks(_tasksPath);
        if (touched.FirstOrDefault(t => t.EndsWith("aipolicy.data", StringComparison.OrdinalIgnoreCase)) is { } ai) _aiView?.LoadFile(ai);
        // pacotes regravados (interfaces, configs, surfaces): o extrator e o editor de interface releem sem fechar o programa
        if (touched.Any(t => t.EndsWith(".pck", StringComparison.OrdinalIgnoreCase))) { _pckView?.GameFolderChanged(); _uiArea?.GameFolderChanged(); }
        if (log.Count > 0)
            MessageBox.Show(this, string.Join("\n", log), Loc.T("trc.title", "Traduzir a partir de outro cliente"));
        if (chosen.Contains(elementsRow)) TranslateFromOther(srcEl);
    }

    private static bool GameRunningFrom(string element)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("elementclient"))
            try { if (p.MainModule?.FileName is { } exe && exe.StartsWith(element, StringComparison.OrdinalIgnoreCase)) return true; }
            catch { return true; }   // sem acesso ao processo: melhor avisar
            finally { p.Dispose(); }
        return false;
    }

    // ---------- salvar em outra versão (issue #24) ----------
    // Estratégia: trabalhar na versão mais nova e descer (ou subir) só na hora de salvar para outro cliente/servidor.
    private void SaveOtherVersion()
    {
        if (_store.Current is not { } mod || mod.Source is not ElementsLayout) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        var dir = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg");
        var cfgs = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cfg")
            .Select(f => (Path: f, Ver: CfgCatalog.VersionFromName(f))).Where(x => x.Ver is not null).OrderByDescending(x => x.Ver).ThenBy(x => x.Path).ToList() : new();
        if (cfgs.Count == 0) { Status(Loc.T("status.noCfg", "Nenhum .cfg encontrado.")); return; }
        int cur = mod.Version & 0xFFFF;
        using var f = new Form { Text = Loc.T("conv.title", "Salvar em outra versão"), Width = 520, Height = 200, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, BackColor = CSurface, ForeColor = CFg, Font = new Font("Segoe UI", 9.5f) };
        var info = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10), ForeColor = CMuted,
            Text = string.Format(Loc.T("conv.info", "Arquivo atual: v{0}. Campos e listas que a outra versão não tem são descartados; os que ela tem a mais nascem zerados. Os dados atuais não mudam."), cur) };
        var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in cfgs) combo.Items.Add($"v{c.Ver}  —  {Path.GetFileNameWithoutExtension(c.Path)}" + (c.Ver == cur ? "  " + Loc.T("conv.current", "(atual)") : ""));
        combo.SelectedIndex = 0;
        var ok = new Button { Text = Loc.T("conv.go", "Converter e salvar…"), Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White, DialogResult = DialogResult.OK };
        f.Controls.Add(combo); f.Controls.Add(info); f.Controls.Add(ok);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var (cfgPath, ver) = cfgs[combo.SelectedIndex];
        using var dlg = new SaveFileDialog { Filter = "elements.data (*.data)|*.data", FileName = $"elements_v{ver}.data", InitialDirectory = _dataPath is null ? "" : Path.GetDirectoryName(_dataPath) };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var target = ElementsCfg.Load(cfgPath);
            var (data, rep) = ElementsConverter.Convert(mod, target, ver!.Value);
            if (ElementsConverter.Verify(data, target, mod) is { } err) throw new InvalidDataException(Loc.T("conv.verifyFail", "O arquivo convertido não passou na releitura: ") + err);
            AtomicFileSave.Write(dlg.FileName, data);
            MessageBox.Show(this, string.Format(Loc.T("conv.done", "Salvo em v{0}: {1:N0} itens.\nCampos descartados: {2} · criados (zerados): {3}\nListas descartadas: {4} · criadas (vazias): {5}\n\nReleitura com o .cfg da v{0}: OK.\n{6}"),
                ver, rep.Items, rep.FieldsDropped, rep.FieldsAdded, rep.BlocksDropped, rep.BlocksAdded, dlg.FileName), Loc.T("conv.title", "Salvar em outra versão"));
        }
        catch (Exception ex) { Error(Loc.T("conv.fail", "Falha ao converter"), ex); }
        finally { Cursor = Cursors.Default; }
    }

    // ---------- procurar erros ----------
    private ErrorsWindow? _errors;
    private GShopView? _gshop;
    private NpcGenView? _npcgen;
    private TabPage? _tabGShop, _tabPck, _tabNpcGen, _tabUi, _tabPlugins;
    private PluginsView? _pluginsView;
    private InterfacesView? _uiArea;
    private PckView? _pckView;
    private readonly MyAceEditor.Changes.EditHistory _edits = new();

    private void FindErrors()
    {
        if (_store.Current is null) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        if (_errors is { IsDisposed: false }) { _errors.Activate(); return; }
        var module = _store.Current;
        _errors = new ErrorsWindow(() => ErrorFinder.Find(module), p => { GoToItem(p.Item); FocusField(p.Field); }, FixErrors, b => Pretty(b.Name));
        _errors.Show(this);
    }

    private IReadOnlyList<ErrorFinder.Problem> FixErrors(IReadOnlyList<ErrorFinder.Problem> targets)
    {
        List<ErrorFinder.Problem> fixedOnes;
        using (_edits.Batch(string.Format(Loc.T("hist.fixErrors", "Corrigir {0} erro(s)"), targets.Count)))
            fixedOnes = targets.Where(p => ErrorFinder.Fix(p, (it, k, v) => _edits.SetField(it, k, v, ""))).ToList();
        foreach (var b in fixedOnes.Select(p => p.Block).Distinct()) _unusedCache.Remove(b);
        if (_current is not null && fixedOnes.Any(p => p.Item == _current)) FillGrid(_current);
        _list.Invalidate();
        Status(string.Format(Loc.T("status.errorsFixed", "{0} problema(s) corrigido(s) — salve para gravar"), fixedOnes.Count));
        return fixedOnes;
    }

    // ---------- refatorar ID ----------
    // Troca o ID do item e de tudo que aponta para ele (elements, quests, loja) num passo só do histórico.
    private void ChangeIdRefactor()
    {
        if (_current is not { } item || !_owner.TryGetValue(item, out var block) || _store.Current is not { } module || _refs is null) return;
        var shops = _gshop?.LoadedShops.ToList() ?? new();
        // npcgen do servidor (mundo e instâncias): só monstros, NPCs e minas nascem nele
        var npcgens = block.Name is "MONSTER_ESSENCE" or "NPC_ESSENCE" or "MINE_ESSENCE" ? _npcgen?.AllFiles().ToList() ?? new() : new();
        using var f = new Form
        {
            Text = string.Format(Loc.T("chid.title", "Mudar ID — {0} {1}"), item.Id, TasksFile.SplitColor(item.Name).Text),
            Width = 560, Height = 330, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = new Font("Segoe UI", 9.5f), BackColor = Color.FromArgb(242, 245, 249),
        };
        var lbl = new Label { Text = Loc.T("chid.newId", "Novo ID:"), AutoSize = true, Location = new Point(16, 20) };
        var box = new TextBox { Text = IdRefactor.NextFreeId(module, block).ToString(), Location = new Point(100, 16), Width = 140 };
        var hint = new Label { Text = Loc.T("chid.hint", "(sugestão: o primeiro ID livre depois do maior)"), AutoSize = true, Location = new Point(250, 20), ForeColor = CMuted };
        var info = new Label { Location = new Point(16, 56), Size = new Size(510, 180) };
        var ok = new Button { Text = Loc.T("chid.ok", "Mudar ID"), Location = new Point(300, 244), Size = new Size(110, 30), FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), Location = new Point(416, 244), Size = new Size(110, 30), FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { lbl, box, hint, info, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        IdChangePlan? plan = null;
        void Preview()
        {
            var nid = box.Text.Trim();
            if (IdRefactor.Validate(module, block, item, nid) is { } err) { plan = null; ok.Enabled = false; info.ForeColor = Color.FromArgb(185, 28, 28); info.Text = "⚠ " + err; return; }
            plan = IdRefactor.Plan(_refs, block, item, nid, _tasks, shops, npcgens);
            ok.Enabled = true; info.ForeColor = CFg;
            info.Text = string.Format(Loc.T("chid.preview",
                "Vai mudar:\n  • o ID do item ({0} → {1})\n  • {2} campo(s) no elements.data que apontam para ele\n  • {3} campo(s) em quests (tasks.data)\n  • {4} referência(s) na loja (gshop)\n  • {5} ponto(s) de nascimento no npcgen.data{6}\n\nNão atualiza ainda: a descrição/cor do nome no configs.pck (ficam no ID antigo). Ctrl+Z desfaz tudo de uma vez."),
                item.Id, nid, plan.Elements.Count, plan.Quests.Count, plan.Shop.Count, plan.NpcGen.Count,
                plan.NpcGen.Count == 0 ? "" : " (" + string.Join(", ", plan.NpcGen.GroupBy(x => x.Name).Select(g => $"{g.Key} {g.Count()}").Take(8)) + (plan.NpcGen.Select(x => x.Name).Distinct().Count() > 8 ? "…" : "") + ")");
        }
        // a prévia percorre elements e quests: espera a digitação parar (antes cada tecla travava ~1 s)
        var wait = new System.Windows.Forms.Timer { Interval = 300 };
        wait.Tick += (_, _) => { wait.Stop(); Preview(); };
        box.TextChanged += (_, _) => { ok.Enabled = false; wait.Stop(); wait.Start(); };
        f.FormClosed += (_, _) => wait.Dispose();
        Preview();
        if (f.ShowDialog(this) != DialogResult.OK || plan is null) return;
        var p = plan;
        var label = string.Format(Loc.T("hist.changeId", "Mudar ID {0} → {1}"), p.OldId, p.NewId);
        void Quests(string v) { foreach (var (q, tf) in p.Quests) q.Set(tf, v); if (p.Quests.Count > 0) { _tasksDirty = true; if (_qCur is not null) ShowQuest(_qTree.SelectedNode?.Tag); } }
        void Shop(string v)
        {
            foreach (var (s, it, gift) in p.Shop) { if (gift) it.GiftId = int.Parse(v); else it.ItemId = int.Parse(v); }
            foreach (var s in p.Shop.Select(x => x.Shop).Distinct()) _gshop?.ExternalEdit(s);
        }
        // procura de novo pelo ID (os pontos podem ter sido trocados por cópias no desfazer da aba NPCGen)
        void NpcGen(string from, string to)
        {
            foreach (var (n, nf, _) in p.NpcGen.DistinctBy(x => x.Name)) { IdRefactor.ReplaceInNpcGen(nf, p.Block.Name, int.Parse(from), int.Parse(to)); _npcgen?.ExternalEdit(n); }
        }
        using (_edits.Batch(label))
        {
            SetField(item, "ID", p.NewId, refresh: false);   // primeiro: ao desfazer, a tela volta para este item
            foreach (var r in p.Elements) SetField(r.Item, r.Field, p.NewId, refresh: false);
            if (p.Quests.Count > 0) _edits.Custom(label, () => Quests(p.NewId), () => Quests(p.OldId));
            if (p.Shop.Count > 0) _edits.Custom(label, () => Shop(p.NewId), () => Shop(p.OldId));
            if (p.NpcGen.Count > 0) _edits.Custom(label, () => NpcGen(p.OldId, p.NewId), () => NpcGen(p.NewId, p.OldId));
        }
        _refs = new RefIndex(module);
        RefreshView();
        GoToItem(item);
        Status(string.Format(Loc.T("status.changeId", "ID {0} → {1}: {2} referência(s) atualizada(s) (elements {3}, quests {4}, loja {5}, npcgen {6}). Salve os arquivos para gravar."),
            p.OldId, p.NewId, p.Total - 1, p.Elements.Count, p.Quests.Count, p.Shop.Count, p.NpcGen.Count));
    }

    // ---------- desfazer / refazer ----------
    private void UndoRedo(bool undo)
    {
        var step = undo ? _edits.Undo() : _edits.Redo();
        if (step is null) { Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return; }
        AfterHistory(step);
        Status(string.Format(undo ? Loc.T("status.undone", "Desfeito: {0}") : Loc.T("status.redone", "Refeito: {0}"), step.Label));
    }

    // Depois de desfazer/refazer: dono dos itens, índices, lista e o item afetado na tela.
    private void AfterHistory(MyAceEditor.Changes.EditHistory.Step step)
    {
        bool structural = false;
        Item? show = null;
        foreach (var (block, item) in step.Touched)
        {
            if (block is not null)
            {
                structural = true;
                if (block.Items.Contains(item)) { _owner[item] = block; show ??= item; }
                else { _owner.Remove(item); if (_current == item) _current = null; }
            }
            else show ??= item;
            if (_owner.TryGetValue(item, out var ob)) _unusedCache.Remove(ob);
        }
        if (structural || step.ChangesIds)
        {
            if (_store.Current is not null) _refs = new RefIndex(_store.Current);
            RefreshView();
        }
        _list.Invalidate();
        if (show is not null && _owner.ContainsKey(show))
        {
            if (show != _current) GoToItem(show); else ShowItem(show);
        }
        else if (_current is not null) ShowItem(_current);
    }

    private void ShowHistory()
    {
        using var f = new Form
        {
            Text = Loc.T("hist.title", "Histórico de ações"), Width = 520, Height = 460, StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, BackColor = CSurface, ForeColor = CFg, Font = new Font("Segoe UI", 9.5f),
        };
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        lv.Columns.Add(Loc.T("hist.colWhen", "Hora"), 70); lv.Columns.Add(Loc.T("hist.colAction", "Ação"), 330); lv.Columns.Add(Loc.T("hist.colChanges", "Mudanças"), 80);
        void Fill()
        {
            lv.Items.Clear();
            // desfeitos (dá para refazer) em cinza no topo, depois os feitos do mais novo ao mais antigo
            foreach (var st in _edits.RedoSteps.Reverse())
                lv.Items.Add(new ListViewItem(new[] { st.When.ToString("HH:mm:ss"), "↷ " + st.Label, st.Count.ToString() }) { ForeColor = CMuted, Tag = (st, false) });
            foreach (var st in _edits.UndoSteps)
                lv.Items.Add(new ListViewItem(new[] { st.When.ToString("HH:mm:ss"), st.Label, st.Count.ToString() }) { Tag = (st, true) });
        }
        var go = new Button { Text = Loc.T("hist.goTo", "Voltar/avançar até aqui"), Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat };
        go.Click += (_, _) =>
        {
            if (lv.SelectedItems.Count == 0 || lv.SelectedItems[0].Tag is not ValueTuple<MyAceEditor.Changes.EditHistory.Step, bool> t) return;
            var (target, done) = t;
            // passo feito: desfaz os mais novos até ele ficar no topo; passo desfeito: refaz até ele
            if (done) { while (_edits.UndoSteps.Count > 0 && _edits.UndoSteps[0] != target && _edits.Undo() is { } u) AfterHistory(u); }
            else { while (_edits.Redo() is { } r) { AfterHistory(r); if (r == target) break; } }
            Fill();
        };
        lv.ItemActivate += (_, _) => go.PerformClick();
        f.Controls.Add(lv); f.Controls.Add(go);
        Fill();
        f.ShowDialog(this);
    }

    // Ctrl+Z / Ctrl+Y: dentro de uma caixa de texto (busca, célula em edição) ficam com a própria caixa.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y) or (Keys.Control | Keys.Shift | Keys.Z)
            && ActiveControlDeep() is not TextBoxBase && !_grid.IsCurrentCellInEditMode)
        {
            bool undo = keyData == (Keys.Control | Keys.Z);
            if (_tabs?.SelectedTab == _tabPlugins && _pluginsView is { } pv) { if (!(undo ? pv.Undo() : pv.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
            if (_tabs?.SelectedTab == _tabUi && _uiArea?.Designer is { } ud) { if (!(undo ? ud.Undo() : ud.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
            if (_tabs?.SelectedTab == _tabUi && _uiArea?.Editor is { } ui) { if (!(undo ? ui.Undo() : ui.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
            if (_tabs?.SelectedTab == _tabGShop && _gshop is not null) { if (!(undo ? _gshop.Undo() : _gshop.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
            if (_tabs?.SelectedTab == _tabNpcGen && _npcgen is not null) { if (!(undo ? _npcgen.Undo() : _npcgen.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
            UndoRedo(undo);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control? ActiveControlDeep()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl is not null) c = cc.ActiveControl;
        return c;
    }

    // Mostra o campo na grade: troca para a sub-aba do grupo dele e seleciona a linha.
    private void FocusField(string key)
    {
        SelectGroup(DisplayGroup(key));
        foreach (DataGridViewRow r in _grid.Rows)
            if (r.Tag as string == key)
            {
                _grid.CurrentCell = r.Cells[1];
                _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, r.Index - 3);
                return;
            }
    }

    // ---------- detalhes ----------
    private void ShowItem(Item item)
    {
        _header.Text = string.IsNullOrEmpty(item.Name) ? Loc.T("block.noName", "(sem nome)") : TasksFile.SplitColor(item.Name).Text;
        _header.ForeColor = NameColor(item);
        _idLabel.Text = item.Id;
        LoadItemIcon(item);
        UpdateEditorBar(item);
        var uses = LinkCount(item);
        _tabLinks.Text = (uses > 0 ? $"{Loc.T("detail.links", "Vínculos")} ({uses})" : Loc.T("detail.links", "Vínculos"));
        if (_detailTabs.SelectedTab == _tabLinks) FillLinks(item);

        // loja do NPC: aba extra com a prévia como no jogo
        bool isShop = _owner.TryGetValue(item, out var ib) && ib.Name == "NPC_SELL_SERVICE";
        if (isShop && !_detailTabs.TabPages.Contains(_tabShop)) _detailTabs.TabPages.Insert(1, _tabShop);
        else if (!isShop && _detailTabs.TabPages.Contains(_tabShop)) _detailTabs.TabPages.Remove(_tabShop);
        if (isShop) _shop.ShowShop(item);
        bool isMake = _owner.TryGetValue(item, out var mb) && mb.Name == "NPC_MAKE_SERVICE";
        if (isMake && !_detailTabs.TabPages.Contains(_tabMake)) _detailTabs.TabPages.Insert(1, _tabMake);
        else if (!isMake && _detailTabs.TabPages.Contains(_tabMake)) _detailTabs.TabPages.Remove(_tabMake);
        if (isMake) _make.ShowService(item);
        // descrição e cor do nome são do item (configs.pck); monstro/NPC/mina não usam: a aba some para não dar a entender que dá
        bool creature = _owner.TryGetValue(item, out var db) && db.Name is "MONSTER_ESSENCE" or "NPC_ESSENCE" or "MINE_ESSENCE";
        if (creature && _detailTabs.TabPages.Contains(_tabDesc)) _detailTabs.TabPages.Remove(_tabDesc);
        else if (!creature && !_detailTabs.TabPages.Contains(_tabDesc)) _detailTabs.TabPages.Add(_tabDesc);
        // sobra de antes (cor/descrição de ITEM gravada no ID de uma criatura deixava o nome amarelo no editor): tira, com Ctrl+Z
        if (creature && _cfgs is { } cc && int.TryParse(item.Id, out var cid) && (cc.ItemColors.ContainsKey(cid) || cc.ItemDescs.ContainsKey(cid)))
            BeginInvoke(() =>
            {
                if (_current != item) return;
                if (cc.ItemColors.ContainsKey(cid)) { _descBinding = true; _nameColor.SelectedIndex = 0; _descBinding = false; ApplyItemTexts(color: true, target: item); }
                if (cc.ItemDescs.ContainsKey(cid)) ApplyItemTexts(color: false, remove: true, target: item);
                Status(Loc.T("desc.creatureCleaned", "Cor/descrição de item tirada desta criatura (monstro/NPC não usa) — Ctrl+Z desfaz; Salvar grava no configs.pck."));
            });

        // sub-abas por grupo (Básico, Habilidades, Queda, Modelo…), mantendo a aba escolhida se existir
        RebuildGroupTabs(item);
        FillGrid(item);
        FillDescription(item);
        FillFindFields(item);

        RememberRecent(item);
        if (item != _autoShown) _autoShown = null;
        if (!_navigating && _autoShown is null)
        {
            if (_histPos >= 0 && _histPos < _history.Count - 1) _history.RemoveRange(_histPos + 1, _history.Count - _histPos - 1);
            if (_history.Count == 0 || _history[^1] != item) { _history.Add(item); if (_history.Count > 200) _history.RemoveAt(0); }
            _histPos = _history.Count - 1;
        }
        _stSel.Text = string.Format(Loc.T("status.selected", "Selecionado: {0}"), item.Id)
            + $" · {item.Fields.Count} {Loc.T("status.fields", "campos")}" + (item.Raw is { } raw ? $" · {raw.Length} bytes" : "");
    }

    // O ID do grupo ("Todos", "Básico"…) fica em TabPage.Tag e nunca muda; só o Text (exibição) segue o idioma.
    private string SelectedGroupId => _groupTabs.SelectedTab?.Tag as string ?? "Todos";

    /// <summary>Seleciona a sub-aba de grupo pelo ID (usado pelo --snapshot).</summary>
    internal void SelectGroup(string id)
    {
        if (id.Equals("shop", StringComparison.OrdinalIgnoreCase)) { if (_detailTabs.TabPages.Contains(_tabShop)) _detailTabs.SelectedTab = _tabShop; return; }
        if (id.Equals("make", StringComparison.OrdinalIgnoreCase)) { if (_detailTabs.TabPages.Contains(_tabMake)) _detailTabs.SelectedTab = _tabMake; return; }
        if (id.Equals("desc", StringComparison.OrdinalIgnoreCase)) { _detailTabs.SelectedIndex = _detailTabs.TabCount - 1; return; }
        if (id.StartsWith("uicreate", StringComparison.OrdinalIgnoreCase)) { if (_tabs is not null && _tabUi is not null) _tabs.SelectedTab = _tabUi; _uiArea?.OpenDesignerForSnapshot(id.Length > 9 ? id[9..] : null); return; }
        if (id.StartsWith("ui:", StringComparison.OrdinalIgnoreCase)) { if (_tabs is not null && _tabUi is not null) _tabs.SelectedTab = _tabUi; _uiArea?.OpenUiForSnapshot(id[3..]); return; }
        if (id.Equals("picker", StringComparison.OrdinalIgnoreCase)) { ShowBlockPicker(); return; }
        if (id.Equals("links", StringComparison.OrdinalIgnoreCase)) { _detailTabs.SelectedTab = _tabLinks; if (_current is not null) FillLinks(_current); return; }
        foreach (TabPage t in _groupTabs.TabPages)
            if (string.Equals(t.Tag as string, id, StringComparison.OrdinalIgnoreCase))
            {
                _groupTabs.SelectedTab = t;   // sem handle (abas escondidas) o SelectedIndexChanged não dispara
                SyncSections(); if (_current is not null) FillGrid(_current);
                return;
            }
    }

    private void RebuildGroupTabs(Item item)
    {
        var groups = GroupsOf(item);
        // secoes que nao sao campos do elements: onde nasce (npcgen) e quests que usam
        if (_owner.TryGetValue(item, out var gb))
        {
            if (gb.Name is "MONSTER_ESSENCE" or "NPC_ESSENCE") groups.Add("Spawn");
            if (QuestKind(gb) != ' ') groups.Add("Quests");
        }
        var keep = SelectedGroupId;
        if (!groups.SequenceEqual(_groupTabs.TabPages.Cast<TabPage>().Select(t => t.Tag as string ?? t.Text)))
        {
            _groupTabs.TabPages.Clear();
            foreach (var g in groups) _groupTabs.TabPages.Add(new TabPage(FieldLabels.GroupName(g)) { Tag = g });
            var again = _groupTabs.TabPages.Cast<TabPage>().FirstOrDefault(t => (string?)t.Tag == keep);
            if (again is not null) _groupTabs.SelectedTab = again;
        }
        foreach (TabPage t in _groupTabs.TabPages)
            if (t.Tag is string id && FieldLabels.GroupName(id) + SlotCount(item, id) is var txt && t.Text != txt) t.Text = txt;
        SyncSections();
    }

    private readonly SectionList _sections = new();
    private bool _syncSections;
    private sealed record SectionItem(TabPage Page) { public override string ToString() => Page.Text; }

    private void SyncSections()
    {
        _syncSections = true;
        try
        {
            _sections.BeginUpdate();
            _sections.Items.Clear();
            foreach (TabPage t in _groupTabs.TabPages) _sections.Items.Add(new SectionItem(t));
            _sections.SelectedIndex = Math.Max(0, _groupTabs.SelectedIndex);
            _sections.EndUpdate();
        }
        finally { _syncSections = false; }
    }

    // ícone de cada seção (desenhado com a fonte de ícones do Windows)
    private static string SectionGlyph(string group) => group switch
    {
        "Todos" => "\uE8FD", "Básico" => "\uE946", "Requisitos" => "\uE72E", "Ataque" => "\uECAD", "Defesa" => "\uEA18",
        "Durabilidade" => "\uE90F", "Economia" => "\uE8EC", "Atributos / Addons" => "\uE734", "Habilidades" => "\uE945",
        "Queda" => "\uE7B8", "Modelo" or "Modelo / Arquivos" => "\uE81E", "Outros" => "\uF0E2",
        "Spawn" => "\uE916", "Quests" => "\uE8A5", _ => "\uE7C1",
    };

    /// <summary>Linha de título de seção na grade (não é campo: Tag deste tipo, nunca string).</summary>
    private sealed record SectionTag(string Group, string Text);

    private void PaintSection(object? s, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics is null || s is not DataGridView dgv || dgv.Rows[e.RowIndex].Tag is not SectionTag st) return;
        var g = e.Graphics; var r = e.CellBounds;
        using (var b = new SolidBrush(Color.FromArgb(238, 243, 250))) g.FillRectangle(b, r);
        using (var pen = new Pen(Color.FromArgb(203, 213, 225))) g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        if (e.ColumnIndex == 0)
        {
            using (var b = new SolidBrush(CAccent)) g.FillRectangle(b, r.Left, r.Top + 6, 4, r.Height - 12);
            g.DrawImage(UiKit.Icon(SectionGlyph(st.Group), CAccent, 18), r.Left + 12, r.Top + (r.Height - 18) / 2);
            using var f = new Font("Segoe UI Semibold", 10.5f);
            // o título pode passar da 1ª coluna: as outras só pintam o fundo
            var clip = g.Clip; g.ResetClip();
            TextRenderer.DrawText(g, st.Text, f, new Rectangle(r.Left + 38, r.Top, 600, r.Height), CAccent, TextFormatFlags.VerticalCenter | TextFormatFlags.NoClipping);
            g.Clip = clip;
        }
        e.Handled = true;
    }

    // " (7/32)": slots preenchidos nas abas de addons, queda e habilidades (ocupação, como nos editores da comunidade).
    private static string SlotCount(Item item, string group)
    {
        if (group is not ("Atributos / Addons" or "Queda" or "Habilidades")) return "";
        int total = 0, filled = 0;
        foreach (var (k, f) in item.Fields)
        {
            var kind = FieldKinds.Of(k);
            if (DisplayGroup(k) != group || !(FieldKinds.IsReference(kind) || kind == FieldKind.SkillRef)) continue;
            total++;
            if (!IsEmpty(f.Value)) filled++;
        }
        return total > 1 ? $" ({filled}/{total})" : "";
    }

    // Grupo de exibição: drops → Queda, habilidades → Habilidades, arquivos → Modelo; resto pelo FieldLabels.
    private static string DisplayGroup(string key)
    {
        var k = key.ToLowerInvariant();
        var kind = FieldKinds.Of(key);
        if (k.StartsWith("money")) return "Economia";   // moedas do monstro não são item de queda (teste 18)
        if (k.Contains("drop") || (kind == FieldKind.ItemRef && k.Contains("matter") && !k.Contains("material"))) return "Queda";
        if (kind == FieldKind.SkillRef || (k.Contains("skill") && k != "skillpoint")) return "Habilidades";
        if (kind == FieldKind.File) return "Modelo";
        return FieldLabels.Group(key);
    }

    private static List<string> GroupsOf(Item item)
    {
        var present = item.Fields.Keys.Select(DisplayGroup).ToHashSet();
        var order = new List<string> { "Todos" };
        foreach (var g in FieldLabels.GroupOrder.Concat(new[] { "Habilidades", "Queda", "Modelo" }))
            if (present.Contains(g) && !order.Contains(g)) order.Add(g);
        foreach (var g in present) if (!order.Contains(g)) order.Add(g);
        return order;
    }

    // mesmo item e mesma aba: depois de editar, a grade volta para a mesma posição (antes pulava para o topo)
    private Item? _gridItem;
    private string? _gridGroup;

    private void FillGrid(Item item)
    {
        var sel = SelectedGroupId;
        bool info = sel is "Spawn" or "Quests";
        _creaturePanel.Visible = info; _grid.Visible = !info;
        _editorBar.Visible = !info && (_hasAddonEd || _hasDropEd);   // "Editor de drops" ficava por cima da dica do Spawn
        if (info) { FillCreaturePanel(item, sel); return; }
        bool same = ReferenceEquals(_gridItem, item) && sel == _gridGroup;
        int keepTop = same ? _grid.FirstDisplayedScrollingRowIndex : -1;
        var keepKey = same ? _grid.CurrentRow?.Tag as string : null;
        _gridItem = item; _gridGroup = sel;
        _grid.Rows.Clear();
        var byGroup = item.Fields.GroupBy(f => DisplayGroup(f.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var unused = _hideUnused ? UnusedFields(_owner.TryGetValue(item, out var blk) ? blk : null) : new HashSet<string>();
        int hidden = 0;
        foreach (var gname in GroupsOf(item).Skip(1))
        {
            if (sel != "Todos" && sel != gname) continue;
            if (!byGroup.TryGetValue(gname, out var fields)) continue;
            // realname: nome original em chinês, sem efeito no jogo; fica no arquivo e some da grade.
            hidden += fields.Count(f => unused.Contains(f.Key));
            var vis = fields.Where(f => f.Key != "realname" && !unused.Contains(f.Key)
                && ((gname != "Atributos / Addons" && gname != "Queda" && gname != "Habilidades") || !IsEmpty(f.Value.Value) || sel == gname)).ToList();
            bool slotted0 = gname is "Queda" or "Habilidades" or "Atributos / Addons";
            int hiddenSlots = 0;
            if (slotted0 && sel == gname)
            {
                // slots vazios: só o primeiro aparece (para preencher); o resto só enchia a tela
                static string? SlotOf(string key) => System.Text.RegularExpressions.Regex.Match(key, @"_(\d+)(_|$)") is { Success: true } m ? m.Groups[1].Value : null;
                var empty = vis.GroupBy(f => SlotOf(f.Key)).Where(g => g.Key is not null && g.All(f => IsEmpty(f.Value.Value))).Select(g => g.Key!).ToList();
                var drop = empty.Skip(1).ToHashSet();
                hiddenSlots = drop.Count;
                if (drop.Count > 0) vis = vis.Where(f => SlotOf(f.Key) is not { } sk || !drop.Contains(sk)).ToList();
            }
            if (vis.Count == 0) continue;
            {
                // título da seção (também com uma só seção escolhida: a tela diz onde você está)
                int hr = _grid.Rows.Add("", "", "");
                var hrow = _grid.Rows[hr]; hrow.ReadOnly = true; hrow.Height = 36;
                hrow.Tag = new SectionTag(gname, FieldLabels.GroupName(gname) + SlotCount(item, gname));   // sem contador de campos (pedido do usuário)
            }
            // listas de slots (drops, habilidades, atributos): cada slot com fundo alternado, para ver onde um termina e o outro começa
            bool slotted = gname is "Queda" or "Habilidades" or "Atributos / Addons";
            string? lastSlot = null; bool shade = false;
            foreach (var (k, f) in vis)
            {
                var desc = Describe(item, k, f.Value);
                int r = _grid.Rows.Add("  " + FieldLabels.Label(k), f.Value, desc);
                if (slotted && System.Text.RegularExpressions.Regex.Match(k, @"_(\d+)(_|$)") is { Success: true } sm)
                {
                    if (sm.Groups[1].Value != lastSlot) { shade = !shade; lastSlot = sm.Groups[1].Value; }
                    if (shade) _grid.Rows[r].DefaultCellStyle.BackColor = Color.FromArgb(240, 245, 252);
                }
                _grid.Rows[r].Tag = k;
                if (FieldKinds.IsFlag(_owner.TryGetValue(item, out var ownerBlock) ? ownerBlock.Name : "", k, f.Value))
                {
                    // sim/não: caixa de seleção (grava 1/0)
                    _grid.Rows[r].Cells[1] = new DataGridViewCheckBoxCell { Value = f.Value == "1", TrueValue = true, FalseValue = false };
                    _grid.Rows[r].Cells[2].Value = Loc.T("grid.flag", "sim / não");
                }
                if (desc.StartsWith("→")) _grid.Rows[r].Cells[2] = LinkCell(desc);
                var help = FieldHelp(k);
                if (IsSpecialEditor(k)) _grid.Rows[r].Cells[0].ToolTipText = (help is null ? "" : help + "\n") + Loc.T("grid.dblClick", "Duplo clique para escolher");
                else if (help is not null) _grid.Rows[r].Cells[0].ToolTipText = help;
            }
        }
        // efeito do addon (bloco EQUIPMENT_ADDON) e "dropado por" (monstros que dropam o item)
        if (sel is "Todos" or "Básico" && CurrentItemBlock?.Name == "EQUIPMENT_ADDON" && _cfgs?.Addons?.Describe(item) is { } eff)
        {
            _grid.Rows.Insert(0, "  " + Loc.T("grid.effect", "Efeito no jogo"), eff, _cfgs.Addons.TypeCaption(item.Id) ?? "");
            _grid.Rows[0].ReadOnly = true;
            _grid.Rows[0].DefaultCellStyle.ForeColor = Color.FromArgb(22, 128, 61);
            _grid.Rows[0].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10);
        }
        if (sel is "Todos" or "Queda")
        {
            var drops = (_refs?.UsedBy(item) ?? Array.Empty<RefIndex.Ref>())
                .Where(u => u.Field.Contains("drop", StringComparison.OrdinalIgnoreCase) && u.Block.Name == "MONSTER_ESSENCE").ToList();
            if (drops.Count > 0)
            {
                int hr = _grid.Rows.Add("", "", "");
                var hrow = _grid.Rows[hr]; hrow.ReadOnly = true; hrow.Height = 36;
                hrow.Tag = new SectionTag("Queda", string.Format(Loc.T("grid.droppedBy", "DROPADO POR ({0} monstros)"), drops.Count));
                foreach (var d in drops.OrderByDescending(d => ParsePct(d.Probability)).Take(300))
                {
                    int r = _grid.Rows.Add("", ParsePct(d.Probability) is var p && p > 0 ? $"{p * 100:0.###}%" : "", "");
                    _grid.Rows[r].Cells[0] = LinkCell($"  {d.Item.Name}  (ID {d.Item.Id})");
                    _grid.Rows[r].Cells[2].Value = $"{Loc.T("grid.levelShort", "nível")} {d.Item.Fields.GetValueOrDefault("level")?.Value} · {FieldLabels.Label(d.Field)}";
                    _grid.Rows[r].Tag = d.Item; _grid.Rows[r].ReadOnly = true;
                }
            }
        }
        if (hidden > 0)
        {
            int r = _grid.Rows.Add("  " + string.Format(Loc.T("grid.hiddenUnused", "{0} campos sem uso neste bloco estão ocultos"), hidden), "",
                Loc.T("grid.hiddenHow2", "Preferências > MyAce > Mostrar campos sem uso"));
            var row = _grid.Rows[r]; row.ReadOnly = true;
            row.DefaultCellStyle.ForeColor = CMuted; row.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Italic);
        }
        if (keepTop > 0 && keepTop < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = keepTop;
        if (keepKey is not null)
            foreach (DataGridViewRow r in _grid.Rows)
                if (r.Tag as string == keepKey) { _grid.CurrentCell = r.Cells[0]; break; }
        if (keepTop > 0 && keepTop < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = keepTop;
    }

    // Campos sem valor em NENHUM item do bloco (0, vazio, -1) e os reservados: não têm efeito no jogo com este arquivo.
    private HashSet<string> UnusedFields(ItemList? block)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (block is null || block.Items.Count < 2) return set;
        if (_unusedCache.TryGetValue(block, out var hit)) return hit;
        foreach (var key in block.Items[0].Fields.Keys)
        {
            if (key is "ID" or "id" or "Name" or "id_major_type" or "id_sub_type") continue;
            if (key.StartsWith("reserved", StringComparison.OrdinalIgnoreCase)
                || block.Items.All(it => !it.Fields.TryGetValue(key, out var f) || IsEmpty(f.Value)))
                set.Add(key);
        }
        return _unusedCache[block] = set;
    }

    private static float ParsePct(string? v) =>
        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;

    private DataGridViewLinkCell LinkCell(string text) => new()
    {
        Value = text, LinkColor = CAccent, ActiveLinkColor = CAccent, VisitedLinkColor = CAccent,
        LinkBehavior = LinkBehavior.HoverUnderline, TrackVisitedState = false,
    };

    // ---------- editores por duplo clique (estilo Rody) ----------
    // O que o campo faz no jogo (dica ao passar o mouse no nome); null = sem explicação ainda
    private static string? FieldHelp(string key) => key.ToLowerInvariant() switch
    {
        "inhabit_type" => Loc.T("help.inhabit", "Onde o monstro/mascote vive e consegue andar: terra, água, ar ou combinações. Monstro de água não sai da água, voador fica no ar."),
        "id_strategy" => Loc.T("help.strategy", "Como o monstro luta: corpo a corpo, à distância (parado ou recuando), combo ou sem se mover."),
        "aggressive_mode" => Loc.T("help.aggressive", "Agressivo ataca quem chega perto; passivo só revida."),
        "food_mask" => Loc.T("help.food", "Comidas que o mascote aceita (cada caixinha é um tipo de ração)."),
        "combined_services" => Loc.T("help.services", "Serviços extras do NPC que não têm registro próprio: correio, leilão, reparar, refinar, pintar roupa…"),
        "id_to_discover" => Loc.T("help.discover", "Ponto de teleporte que o jogador libera ao falar com este NPC."),
        "file_model" => Loc.T("help.model", "Modelo 3D (.ecm) que o jogo desenha para este item, monstro ou NPC."),
        "attack_speed" => Loc.T("help.attackSpeed", "Segundos entre um golpe e outro (menor = ataca mais rápido)."),
        _ when System.Text.RegularExpressions.Regex.IsMatch(key, @"^targets_\d+_idTarget$") => Loc.T("help.target", "Destino deste teleporte (ponto do mapa)."),
        _ => null,
    };

    // Tipo do campo no bloco do item (ver FieldKinds.Of(bloco, campo))
    private FieldKind KindOf(Item? item, string key) => FieldKinds.Of(item is not null && _owner.TryGetValue(item, out var b) ? b.Name : null, key);

    private bool IsSpecialEditor(string key) =>
        KindOf(_current, key) is FieldKind.ClassMask or FieldKind.ProcMask or FieldKind.ImmuneMask or FieldKind.AddonRef or FieldKind.ItemRef or FieldKind.MonsterRef or FieldKind.RecipeRef or FieldKind.ServiceRef
            or FieldKind.FoodMask or FieldKind.ServiceMask
        || (FieldKinds.Of(key) == FieldKind.Waypoint && Waypoints().Count > 0)
        || (FieldKinds.Of(key) == FieldKind.SkillRef && _cfgs?.SkillNames.Count > 0)
        || (key.Equals("file_icon", StringComparison.OrdinalIgnoreCase) && _atlas is not null)
        || key.Equals("file_model", StringComparison.OrdinalIgnoreCase)
        || (_current is not null && _owner.TryGetValue(_current, out var ob) && FieldValues.Options(ob.Name, key) is not null
            && _current.Fields.TryGetValue(key, out var of) && of.Type != FieldType.Bool);

    // Lista "Escolha um valor": pseudo-itens no seletor comum (ID = valor gravado, Nome = significado).
    // group: filtro do topo (opcional); initialGroup: filtro já escolhido ao abrir
    private string? PickValue(string title, IEnumerable<(string Value, string Text, string Desc)> options, string current,
        Func<string, string?>? group = null, string? initialGroup = null, Func<string, float, Image?>? preview = null)
    {
        var items = options.Select(o => new Item { Id = o.Value, Name = o.Text, Fields = new() { ["desc"] = Field.Of(FieldType.String, o.Desc) } }).ToList();
        var picked = FieldEditors.Pick(this, title, items, current, _ => -1, _treeIcons, it => it.Fields["desc"].Value,
            group is null ? null : it => group(it.Id), false, initialGroup, preview is null ? null : (it, yaw) => preview(it.Id, yaw));
        return picked is { Count: > 0 } ? picked[0].Id : null;
    }

    // Pontos de teleporte com nome (maps\world\world_targets.txt do cliente), lidos uma vez por pasta do jogo
    private Dictionary<int, WorldTarget>? _waypoints;
    private string? _waypointsFor;
    private Dictionary<int, WorldTarget> Waypoints()
    {
        var file = _dataPath is null ? null : WorldTargets.FindFile(_dataPath);
        if (_waypoints is not null && _waypointsFor == file) return _waypoints;
        _waypointsFor = file;
        try { _waypoints = file is null ? new() : WorldTargets.Load(file); } catch { _waypoints = new(); }
        return _waypoints;
    }

    // Modelos do cliente (.ecm do models.pck), lidos uma vez por pasta do jogo
    private List<string>? _models;
    private string? _modelsFor;
    private List<string> ClientModels()
    {
        var pck = _dataPath is null ? null : FindClientPck(_dataPath, "models.pck");
        if (_models is not null && _modelsFor == pck) return _models;
        _models = new(); _modelsFor = pck;
        if (pck is null) return _models;
        // .ecm do models.pck ("Models\..." como no elements) e, no PWI 1.6, do textures.pck ("textures\models\...": o jogo
        // acha o pacote pela 1ª pasta do caminho)
        foreach (var (file, prefix) in new[] { (pck, "Models"), (Path.Combine(Path.GetDirectoryName(pck)!, "textures.pck"), "") })
        {
            if (!File.Exists(file)) continue;
            try
            {
                using var a = new PckArchive(file);
                _models.AddRange(a.Entries.Where(e => e.Path.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase))
                    .Select(e => prefix.Length == 0 ? e.Path : prefix + e.Path[(e.Path.IndexOf('\\') is var i && i > 0 ? i : 0)..]));
            }
            catch (Exception ex) { Status(Loc.T("status.modelsFail", "Não foi possível ler o models.pck: ") + ex.Message); }
        }
        _models.Sort(StringComparer.OrdinalIgnoreCase);
        return _models;
    }

    private static string? FindClientPck(string elementsPath, string name)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
            foreach (var c in new[] { Path.Combine(dir, name), Path.Combine(dir, "element", name) })
                if (PckArchive.Locate(c) is { } f) return f;   // aceita as partes com número (models-005.pck)
        return null;
    }

    /// <summary>
    /// Imagem do PC (png/jpg/bmp) vira ícone novo nos dois atlas de itens do surfaces.pck (IconAtlas.AddIcon). Ícone é só do
    /// cliente: o servidor não usa. Devolve o nome do arquivo do ícone ou null.
    /// </summary>
    private string? AddNewIcon()
    {
        if (_atlasPck is null) return null;
        using var ofd = new OpenFileDialog { Filter = Loc.T("icon.filter", "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos|*.*"), Title = Loc.T("icon.add", "➕ Adicionar ícone novo…") };
        if (ofd.ShowDialog(this) != DialogResult.OK) return null;
        var suggested = new string(Path.GetFileNameWithoutExtension(ofd.FileName).Select(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch is '_' or '-') ? ch : '_').ToArray());
        if (ServerAdminWindow.Ask(this, Loc.T("icon.add", "➕ Adicionar ícone novo…"), Loc.T("icon.name", "Nome do ícone no jogo (sem acentos; vira <nome>.dds):"), suggested) is not { Length: > 0 } name) return null;
        if (GameRunningFrom(Path.GetDirectoryName(_atlasPck)!))
        { MessageBox.Show(this, Loc.T("trc.running", "Feche o jogo deste cliente antes de aplicar."), Loc.T("icon.add", "➕ Adicionar ícone novo…"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
        try
        {
            using var img = new Bitmap(ofd.FileName);
            var pck = _atlasPck;
            var done = ReadProgressDialog.Run(this, Loc.T("icon.adding", "Gravando o ícone no surfaces.pck"), ctx =>
            {
                ctx.Report(0, Path.GetFileName(pck), indeterminate: true, force: true);
                var n = IconAtlas.AddIcon(pck, name, img);
                return Tuple.Create(n, IconAtlas.Load(pck));
            }, allowCancel: false);
            _atlas = done.Item2;
            Status(string.Format(Loc.T("icon.added", "Ícone {0} gravado nos atlas de itens do surfaces.pck (backup fora do cliente)."), done.Item1));
            return done.Item1;
        }
        catch (Exception ex) { Error(Loc.T("icon.addFail", "Não consegui adicionar o ícone"), ex); return null; }
    }

    private bool EditFieldSpecial(Item item, string key)
    {
        var kind = KindOf(item, key);
        var cur = item.Fields[key].Value;
        if (key.Equals("file_icon", StringComparison.OrdinalIgnoreCase) && _atlas is not null)
        {
            // mantém a pasta do caminho atual (Surfaces\图标\物品\...) e troca só o arquivo
            if (FieldEditors.IconPicker(this, _atlas, cur, _atlasPck is null ? null : AddNewIcon) is { } name)
            {
                var dir = Path.GetDirectoryName(cur.Replace('/', '\\'));
                SetField(item, key, string.IsNullOrEmpty(dir) ? name : dir + "\\" + name);
                LoadItemIcon(item);
            }
            return true;
        }
        if (_owner.TryGetValue(item, out var vb) && FieldValues.Options(vb.Name, key) is { } opts && item.Fields[key].Type != FieldType.Bool)
        {
            if (PickValue(string.Format(Loc.T("pick.value", "Escolha um valor — {0}"), FieldLabels.Label(key)),
                    opts.Select(o => (o.Value.ToString(), o.Text, "")), cur) is { } v) SetField(item, key, v);
            return true;
        }
        if (key.Equals("file_model", StringComparison.OrdinalIgnoreCase))
        {
            var models = ClientModels();
            if (models.Count == 0) { Status(Loc.T("status.noModels", "models.pck não encontrado ao lado do elements.data.")); return true; }
            // quantos itens usam cada modelo (ajuda a achar um livre ou o mesmo de outro monstro)
            var uses = (_store.Current?.Lists ?? new()).SelectMany(l => l.Items)
                .Where(i => i.Fields.TryGetValue("file_model", out var f) && f.Value.Length > 1)
                .GroupBy(i => i.Fields["file_model"].Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            // caminho na coluna Nome (a coluna ID é estreita); o ID é a posição na lista
            var opts2 = models.Select((mp, i) => (i.ToString(), mp,
                uses.TryGetValue(mp, out var n) ? string.Format(Loc.T("pick.modelUses", "usado por {0}"), n) : Loc.T("pick.modelFree", "sem uso")));
            int curIdx = models.FindIndex(mp => mp.Equals(cur, StringComparison.OrdinalIgnoreCase));
            // prévia 3D do modelo à direita (arrastar gira)
            if (PickValue(Loc.T("pick.model", "Escolher modelo (.ecm do models.pck)"), opts2, curIdx.ToString(), null, null,
                    (id, yaw) => int.TryParse(id, out var pi) && pi >= 0 && pi < models.Count ? ModelPreviewer()?.Render(models[pi], 240, yaw) : null) is { } mv && int.TryParse(mv, out var mi))
                SetField(item, key, models[mi]);
            return true;
        }
        switch (kind)
        {
            case FieldKind.Waypoint when Waypoints() is { Count: > 0 } wps:
            {
                var opts4 = wps.Values.OrderBy(w => w.Id).Select(w => (w.Id.ToString(), w.Name,
                    string.Format(Loc.T("pick.waypointDesc", "mapa {0} · posição {1}"), w.World, w.Position)));
                if (PickValue(Loc.T("pick.waypoint", "Escolher ponto de teleporte"), opts4, cur) is { } wv) SetField(item, key, wv);
                return true;
            }
            case FieldKind.SkillRef when _cfgs is { SkillNames.Count: > 0 } gc:
            {
                // descrição do skillstr: tira cores, troca %d por N e junta numa linha: a frase do efeito primeiro e depois
                // os dados "Alcance: ..." (habilidades de personagem trazem a dica inteira do jogo, com o nome e "Nv. %d" no topo)
                static string Plain(string t)
                {
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"\^[0-9a-fA-F]{6}", "").Replace("\\r", "\n").Replace("\\n", "\n").Replace("%d", "N").Replace("%%", "%");
                    var lines = t.Split('\r', '\n').Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0).ToList();
                    if (lines.Count > 1 && System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"Nv\.|Lv\.|Nível", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) lines.RemoveAt(0);
                    var stats = lines.Where(l => l.Contains(':')).ToList();
                    return string.Join(" · ", lines.Where(l => !l.Contains(':')).Concat(stats));
                }
                var opts3 = gc.SkillNames.OrderBy(k => k.Key).Select(k => (k.Key.ToString(), TasksFile.SplitColor(k.Value).Text,
                    gc.SkillDescs.TryGetValue(k.Key, out var d) ? Plain(d) : ""));
                // compatíveis: as já usadas por itens do mesmo bloco (os monstros oficiais usam 379 e nenhuma das 693 de classe);
                // as de personagem (descrição com "Nv. %d"/"Custo em MP") e as outras ficam num filtro à parte
                var usedSk = new HashSet<string>();
                if (_owner.TryGetValue(item, out var skb))
                    foreach (var it2 in skb.Items)
                        foreach (var (k2, f2) in it2.Fields)
                            if (KindOf(it2, k2) == FieldKind.SkillRef && !IsEmpty(f2.Value)) usedSk.Add(f2.Value);
                string gUsed = Loc.T("pick.skillUsed", "Usadas neste bloco (compatíveis)"), gPlayer = Loc.T("pick.skillPlayer", "De personagem (classes)"), gOther = Loc.T("pick.skillOther", "Outras");
                string SkGroup(string id) => usedSk.Contains(id) ? gUsed
                    : int.TryParse(id, out var sid) && gc.SkillDescs.TryGetValue(sid, out var sd)
                      && System.Text.RegularExpressions.Regex.IsMatch(sd, @"Nv\.\s*%d|Custo em MP|Restri", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? gPlayer : gOther;
                if (PickValue(Loc.T("pick.skill", "Escolher habilidade"), opts3, cur, SkGroup, usedSk.Count > 0 ? gUsed : null) is { } sv) SetField(item, key, sv);
                return true;
            }
            case FieldKind.FoodMask:
                if (FieldEditors.BitMask(this, Loc.T("dialog.foodTitle", "Comida do mascote"), long.TryParse(cur, out var fm) ? fm : 0, BitMasks.Food) is { } nf) SetField(item, key, nf.ToString());
                return true;
            case FieldKind.ServiceMask:
                // bit 31 ligado: o campo é int com sinal, então grava o valor negativo equivalente
                if (FieldEditors.BitMask(this, Loc.T("dialog.servicesTitle", "Serviços combinados do NPC"), long.TryParse(cur, out var sm) ? sm & 0xFFFFFFFFL : 0, BitMasks.Services) is { } ns)
                    SetField(item, key, (item.Fields[key].Type == FieldType.Int ? unchecked((int)(uint)ns) : ns).ToString());
                return true;
            case FieldKind.ClassMask:
                if (FieldEditors.ClassMask(this, long.TryParse(cur, out var m) ? m : 0) is { } nm) SetField(item, key, nm.ToString());
                return true;
            case FieldKind.ProcMask:
                if (FieldEditors.BitMask(this, Loc.T("dialog.procTitle", "Regras do item"), long.TryParse(cur, out var pm) ? pm : 0, BitMasks.Proc) is { } np) SetField(item, key, np.ToString());
                return true;
            case FieldKind.ImmuneMask:
                if (FieldEditors.BitMask(this, Loc.T("dialog.immuneTitle", "Imunidades do monstro"), long.TryParse(cur, out var im) ? im : 0, BitMasks.Immune) is { } ni) SetField(item, key, ni.ToString());
                return true;
            case FieldKind.AddonRef or FieldKind.ItemRef or FieldKind.MonsterRef or FieldKind.RecipeRef or FieldKind.ServiceRef:
            {
                if (kind == FieldKind.ServiceRef && !_pickingService) { ServiceMenu(item, key); return true; }
                var src = PickSource(kind, key);
                if (src.Count == 0) return false;
                bool multi = System.Text.RegularExpressions.Regex.IsMatch(key, @"_\d+_");
                var addons = _cfgs?.Addons;
                var picked = FieldEditors.Pick(this, kind switch
                    {
                        FieldKind.AddonRef => Loc.T("pick.addon", "Escolher atributo"), FieldKind.MonsterRef => Loc.T("pick.monster", "Escolher monstro"),
                        FieldKind.RecipeRef => Loc.T("pick.recipe", "Escolher receita"), FieldKind.ServiceRef => Loc.T("pick.service", "Escolher serviço"),
                        _ => Loc.T("pick.item", "Escolher item"),
                    }, src, cur, IconIndexFor, _treeIcons,
                    it => kind == FieldKind.AddonRef ? addons?.Describe(it) ?? "" : _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : "",
                    kind == FieldKind.AddonRef && addons is not null ? it => addons.TypeCaption(it.Id) : it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : null,
                    multi);
                if (picked is null || picked.Count == 0) return true;
                // vários: preenche este campo e os próximos do mesmo padrão (addons_1_id, addons_2_id…)
                var slots = new List<string> { key };
                var mm = System.Text.RegularExpressions.Regex.Match(key, @"^(.*_)(\d+)(_.*)$");
                if (picked.Count > 1 && mm.Success)
                    for (int n = int.Parse(mm.Groups[2].Value) + 1; slots.Count < picked.Count; n++)
                    {
                        var k2 = mm.Groups[1].Value + n + mm.Groups[3].Value;
                        if (!item.Fields.ContainsKey(k2)) break;
                        slots.Add(k2);
                    }
                for (int i = 0; i < slots.Count && i < picked.Count; i++) SetField(item, slots[i], picked[i].Id, refresh: false);
                FillGrid(item);
                Status(picked.Count > slots.Count
                    ? string.Format(Loc.T("status.pickedFit", "{0} de {1} escolhidos couberam nos campos disponíveis"), slots.Count, picked.Count)
                    : string.Format(Loc.T("status.pickedApplied", "{0} valor(es) aplicado(s)"), picked.Count));
                return true;
            }
        }
        return false;
    }

    // prévia 3D dos modelos no seletor (models.pck da pasta do jogo aberta)
    private ModelPreview? _modelPreview;
    private string? _modelPreviewDir;
    private ModelPreview? ModelPreviewer()
    {
        var el = _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath));
        if (el is null) return null;
        if (_modelPreview is null || _modelPreviewDir != el) { _modelPreview?.Dispose(); _modelPreview = new ModelPreview(el); _modelPreviewDir = el; }
        return _modelPreview;
    }

    // Monstro/NPC/coletável pelo ID, procurando só nesses blocos (o índice geral pode achar outro item com o mesmo
    // número num bloco diferente); refeito a cada mudança no histórico e ao trocar de arquivo.
    private Dictionary<string, (ItemList Block, Item Item)>? _creatureCache;
    private Module? _creatureCacheFor;
    // ---------- criar e ligar quests pelo Mundo 3D (docs/QUESTS-3D.md) ----------

    sealed class QuestHostImpl(EditorForm f) : IQuestHost
    {
        Bitmap? _pate; bool _pateTried;
        public Image? QuestIcon(int index)
        {
            if (!_pateTried)
            {
                _pateTried = true;
                try
                {
                    if (f._dataPath is { } dp && IconAtlas.FindSurfacesPck(dp) is { } sp)
                    {
                        using var pck = new PckArchive(sp);
                        if ((pck.Find(@"surfaces\ingame\pateQuest.tga") ?? pck.Find(@"ingame\pateQuest.tga")) is { } e) _pate = IconLoader.DecodeTga(pck.Read(e));
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Info("PateQuest.tga: " + ex.Message); }
            }
            if (_pate is null || index < 0 || (index + 1) * 30 > _pate.Width) return null;
            return _pate.Clone(new Rectangle(index * 30, 0, 30, Math.Min(30, _pate.Height)), _pate.PixelFormat);
        }
        public QuestDraft? NewDraft(int giverNpc, out string? why) => f.NewQuestDraft(giverNpc, out why);
        public List<string> Validate(QuestDraft d)
        {
            if (f._tasks is not { } t) return new() { Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data.") };
            var e = QuestBuilder.Validate(t, d, IsNpc, id => f._refs?.Resolve(FieldKind.ItemRef, id.ToString()) is not null);
            if (d.Keep) return e;   // NPCs e etapas não mudam
            // matar monstros: o ID tem que ser um monstro do elements (senão a etapa nunca conta)
            foreach (var (mid, _) in d.Steps.Where(s => s.Kind == QuestStepKind.Kill).SelectMany(s => s.Monsters).Distinct())
                if (mid > 0 && f.CreatureItem(mid) is not { Block.Name: "MONSTER_ESSENCE" })
                    e.Add(string.Format(Loc.T("qb.noMonster", "O monstro {0} não existe no elements.data."), mid));
            // os vínculos também têm que caber antes de aplicar qualquer coisa (nada pela metade)
            foreach (var (npc, quest, give) in QuestLinks(d))
                if (IsNpc(npc) && f.AddQuestToService(f.CreatureItem(npc)!.Value.Item, quest, give, dryRun: true) is { } g) e.Add(g);
            return e;
        }
        /// <summary>
        /// Serviços de NPC que a quest precisa (como nos dados reais, --tasks-svc): o NPC que dá guarda o ID da principal; no
        /// recado o NPC que recebe guarda o mesmo ID; numa etapa "falar com NPC" o NPC guarda o ID da etapa (subquest).
        /// </summary>
        public static List<(int Npc, int Quest, bool Give)> QuestLinks(QuestDraft d)
        {
            QuestBuilder.Normalize(d);
            var l = new List<(int, int, bool)> { (d.Giver, d.Id, true) };
            if (QuestBuilder.IsRecado(d)) l.Add((d.Receiver, d.Id, false));
            else
                for (int i = 0; i < d.Steps.Count; i++)
                    if (d.Steps[i].Kind is QuestStepKind.Talk or QuestStepKind.Deliver) l.Add((d.Steps[i].Npc, d.Id + 1 + i, false));
            return l;
        }
        public string Summary(QuestDraft d) => f.QuestSummary(d);
        public string? Commit(QuestDraft d) => f.CommitQuestDraft(d);
        public QuestDraft? EditDraft(int questId, out string? why)
        {
            why = null;
            if (f._tasks is not { FullSchema: true } t) { why = Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data."); return null; }
            if (!t.CanWrite) { why = Loc.T("qb.readOnly", "O tasks.data está só para leitura: ") + t.ReadOnlyReason; return null; }
            if (t.Index.Find(questId) is not QuestNode q) { why = Loc.T("qb.noQuest", "Quest não encontrada no tasks.data."); return null; }
            while (q.Parent is { } p) q = p;   // subquest (etapa): edita a principal
            // quest do jogo que o assistente não remonta: abre mudando só o que a tela mostra
            var draft = QuestBuilder.FromQuest(t, q, out why);
            if (draft is not null) return draft;
            why = null;
            return QuestBuilder.FromQuestKeep(t, q);
        }
        public string? DeleteAsk(int questId) => f.DeleteQuestAsk(questId);
        public string? Delete(int questId, bool withNext = false) => f.DeleteQuestById(questId, withNext);
        public IReadOnlyList<(int Id, string Name)> NextQuests(int questId) => f.NextQuestsOf(questId);
        public string? RemoveMissing(int questId) => f.RemoveMissingQuest(questId);
        public string? NameOf(int id) => f.CreatureItem(id) is { } c ? TasksFile.SplitColor(c.Item.Name).Text
            : f._refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? TasksFile.SplitColor(h.Item.Name).Text : null;
        public string? ElementDir => f._dataPath is { } dp ? Path.GetDirectoryName(Path.GetDirectoryName(dp)) : null;
        public Bitmap? TipImage(int id) => f.TipImage(id);
        public Image? ItemModel(int id, float yaw) => f.ItemModel(id, yaw);
        public (int Id, int Quantity)? PickItemId(IWin32Window owner, bool quantity)
        {
            using var w = new ItemSearchWindow(AllItems(), ItemIcon, quantity, f.TipImage, f.FavoriteIds(), f.RecentIds(), model: f.ItemModel, fav: f.ItemFavorites());
            if (w.ShowDialog(owner) != DialogResult.OK) return null;
            f.NotePicked(w.Picked);
            return (w.Picked, w.Quantity);
        }
        public int? PickCreatureId(IWin32Window owner, bool npcOnly) => f.PickCreature(owner, npcOnly);
        public int? PickRecipeId(IWin32Window owner) => f.PickRecipe(owner);
        public IReadOnlyList<(int Id, string Name)> MakeSkills() => f._store.Current?.FindListByName("RECIPE_ESSENCE")?.Items
            .Select(r => r.Fields.TryGetValue("id_skill", out var v) && int.TryParse(v.Value, out var n) ? n : 0).Where(n => n > 0).Distinct().OrderBy(n => n)
            .Select(n => (n, f._cfgs?.SkillNames.TryGetValue(n, out var sn) == true ? TasksFile.SplitColor(sn!).Text : n.ToString())).ToList() ?? [];
        public int RecipeProduct(int recipe) => f.RecipeOf(recipe) is { } r && r.Fields.TryGetValue("targets_1_id_to_make", out var v) && int.TryParse(v.Value, out var n) ? n : 0;
        public List<(int Id, int Num)> RecipeMaterials(int recipe)
        {
            var l = new List<(int, int)>();
            if (f.RecipeOf(recipe) is not { } r) return l;
            for (int i = 1; r.Fields.TryGetValue($"materials_{i}_id", out var id); i++)
                if (int.TryParse(id.Value, out var mid) && mid > 0) l.Add((mid, int.TryParse(r.Fields[$"materials_{i}_num"].Value, out var n) ? n : 1));
            return l;
        }
        public IReadOnlyList<string> ValidateNpc(NpcDraft draft) => f._store.Current is { } m
            ? NpcBuilder.Validate(m, draft, id => f._refs?.Resolve(FieldKind.ItemRef, id.ToString()) is not null) : [Loc.T("status.openFirst", "Abra um elements.data primeiro")];
        public int? BuildNpc(NpcDraft draft) => f.BuildNpc(draft);
        public int? ShopPrice(int id) => f._refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h && h.Item.Fields.TryGetValue("shop_price", out var v) && int.TryParse(v.Value, out var n) ? n : null;
        public IReadOnlyList<TeleportLink> TeleportersTo(int point) => f.TeleportersTo(point);
        public IReadOnlyList<(int Id, string Name)> DiscoverersOf(int point) => f.DiscoverersOf(point);
        public int DiscoverOf(int npc) => f.NpcById(npc) is { } n ? NpcNumber(n, "id_to_discover") : 0;
        public string? SetDiscover(int npc, int point) => f.SetDiscover(npc, point);
        public int ServiceUsers(int npc) => f.ServiceUsers(npc);
        public string? SetTransmit(int npc, int point, int fee, int level, bool copy) => f.SetTransmit(npc, point, fee, level, copy);
        public string? RemoveTransmit(int npc, int point, bool copy) => f.RemoveTransmit(npc, point, copy);
        public IReadOnlyList<TeleportStop> StopsOfNpc(int npc) => f.NpcById(npc) is { } n && f.ServiceById(NpcNumber(n, "id_transmit_service")) is { } s ? NpcBuilder.StopsOf(s) : [];
        public string? SetStops(int npc, IReadOnlyList<TeleportStop> stops, bool copy) => f.SetStops(npc, stops, copy);
        public string? AtlasName => f.AtlasRoot() > 0 ? f.CreatureNameOrItem(TeleportAtlas.AtlasItem) : null;
        public bool AtlasHas(int point) => f.AtlasRoot() is > 0 and var root && f._tasks is { } t && TeleportAtlas.Find(t, root, point) is not null;
        public string? SetAtlas(WorldTarget point, bool on) => f.SetAtlas(point, on);
        public bool IsDomainNpc(int npc) => f.NpcById(npc) is { } n && NpcNumber(n, "domain_related") != 0;
        public NpcDraft? LoadNpc(int id) => f._store.Current is { } m ? NpcBuilder.Load(m, id) : null;
        public int ServiceUsersOf(NpcDraft draft) => f.ServiceUsersOf(draft);
        public string? UpdateNpc(NpcDraft draft, bool copy) => f.UpdateNpc(draft, copy);
        public RecipeDraft? ReadRecipe(int id) => f._store.Current is { } m ? NpcBuilder.ReadRecipe(m, id) : null;
        public Image? ItemIcon(int id) => f._refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? f.IconFor(h.Item) : null;
        public IReadOnlyList<int> FavoriteIds() => f.FavoriteIds();
        public JavFavorites? ItemFavorites() => f.ItemFavorites();
        public IReadOnlyList<int> RecentIds() => f.RecentIds();
        public void ItemPicked(int id) => f.NotePicked(id);
        public IReadOnlyList<(int Id, string Name, string Block)> AllItems()
        {
            var rows = new List<(int, string, string)>();
            if (f._store.Current is not { } module) return rows;
            foreach (var l in module.Lists)
            {
                if (l.Name is "MONSTER_ESSENCE" or "NPC_ESSENCE" || !l.Items.Any(i => i.Fields.ContainsKey("file_icon"))) continue;
                foreach (var it in l.Items)
                    if (int.TryParse(it.Id, out var id)) rows.Add((id, TasksFile.SplitColor(it.Name).Text, Pretty(l.Name)));
            }
            return rows;
        }
        public bool IsNpc(int id) => f.CreatureItem(id) is { Block.Name: "NPC_ESSENCE" };
        public bool IsMonster(int id) => f.CreatureItem(id) is { Block.Name: "MONSTER_ESSENCE" };
        public bool IsQuestItem(int id) => f._store.Current?.FindListByName("TASKMATTER_ESSENCE")?.Items.Any(i => i.Id == id.ToString()) == true;
        public IReadOnlyList<(int Item, float Prob)> MineProducts(int mineId)
        {
            // MINE_ESSENCE: materials_N_id + materials_N_probability (PW-KB, item de coleta)
            if (f._store.Current?.FindListByName("MINE_ESSENCE")?.Items.FirstOrDefault(i => i.Id == mineId.ToString()) is not { } mine) return Array.Empty<(int, float)>();
            var list = new List<(int, float)>();
            for (int n = 1; mine.Fields.TryGetValue($"materials_{n}_id", out var idf); n++)
                if (int.TryParse(idf.Value, out var item) && item > 0 && mine.Fields.TryGetValue($"materials_{n}_probability", out var pf)
                    && float.TryParse(pf.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var prob) && prob > 0)
                    list.Add((item, prob));
            return list.OrderByDescending(x => x.Item2).ToList();
        }
        public IReadOnlyList<(int Id, string Name, int Giver)> SearchQuests(string text, int max)
        {
            if (f._tasks is not { FullSchema: true } t) return Array.Empty<(int, string, int)>();
            var list = new List<(int, string, int)>();
            foreach (var row in t.Index.Search(text.Trim()))
            {
                if (row is not QuestNode q) continue;
                var giver = q.Field("m_ulDelvNPC") is { } g && int.TryParse(q.Get(g), out var gv) ? gv : 0;
                list.Add((q.Id, TasksFile.SplitColor(q.Name).Text, giver));
                if (list.Count >= max) break;
            }
            return list;
        }
        public string? LinkExisting(int npcId, int questId) => f.LinkQuestToNpc(npcId, questId);
        public IReadOnlyList<QuestOption> Options(bool reward) => f._tasks is { } t && QuestBuilder.ModelRec(t) is { } m
            ? QuestOptions.All.Where(o => o.Reward == reward && QuestOptions.Available(o, t.Version, m)).ToList() : Array.Empty<QuestOption>();
        public IReadOnlyList<(string Key, string Label, string Tip)> FreeFields(bool reward) => f._tasks is { } t && QuestBuilder.ModelRec(t) is { } m
            ? QuestOptions.FreeFields(m, reward).Select(k => (k, QuestLabels.Show(k), QuestLabels.Tip(k))).OrderBy(x => x.Item2).ToList() : Array.Empty<(string, string, string)>();
        public string? QuestName(int id) => f._tasks?.Index.Find(id) is { } row ? TasksFile.SplitColor(TaskIndex.NameOf(row)).Text : null;
        public bool Undo() => f._edits.CanUndo && f._edits.Undo() is not null;
        public bool Redo() => f._edits.CanRedo && f._edits.Redo() is not null;
    }

    private QuestDraft? NewQuestDraft(int giverNpc, out string? why)
    {
        why = null;
        if (_tasks is not { FullSchema: true } t) { why = Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data."); return null; }
        if (!t.CanWrite) { why = Loc.T("qb.readOnly", "O tasks.data está só para leitura: ") + t.ReadOnlyReason; return null; }
        if (QuestBuilder.FindRecadoTemplate(t) is null) { why = Loc.T("qb.noTemplate", "Não há uma quest de recado neste tasks.data para servir de modelo."); return null; }
        // como o Angelica (sTASKedit TASKS.GetFreeID): maior ID + 1; apagar a última e criar de novo reaproveita o ID
        // (usuário 01/11: só ~5 mil IDs até 65535; o personagem que já fez a antiga se resolve com "Pode refazer")
        return new QuestDraft { Id = t.NextFreeId(), Giver = giverNpc };
    }

    private string QuestSummary(QuestDraft d)
    {
        string N(int id) => id <= 0 ? "—" : $"{new QuestHostImpl(this).NameOf(id) ?? "?"} ({id})";
        var sb = new System.Text.StringBuilder();
        QuestBuilder.Normalize(d);
        bool recado = QuestBuilder.IsRecado(d);
        sb.AppendLine(recado ? string.Format(Loc.T("qb.sumTitle", "Quest {0} — \"{1}\" (recado)"), d.Id, d.Name) : string.Format(Loc.T("qb.sumTitleSteps", "Quest {0} — \"{1}\" ({2} etapas)"), d.Id, d.Name, d.Steps.Count));
        sb.AppendLine(string.Format(Loc.T("qb.sumGiver", "Dada por: {0}"), N(d.Giver)));
        if (recado) sb.AppendLine(string.Format(Loc.T("qb.sumReceiver", "Entregue a: {0}"), N(d.Receiver)));
        else
        {
            sb.AppendLine(d.Order switch
            {
                QuestStepOrder.InOrder => Loc.T("qb.sumInOrder", "Etapas em ordem (uma depois da outra):"),
                QuestStepOrder.AnyOrder => Loc.T("qb.sumAnyOrder", "Etapas em qualquer ordem (todas precisam ser feitas):"),
                _ => Loc.T("qb.sumAnyOne", "Basta uma das etapas:"),
            });
            for (int i = 0; i < d.Steps.Count; i++)
            {
                var s = d.Steps[i];
                sb.AppendLine(s.Kind == QuestStepKind.Talk
                    ? string.Format(Loc.T("qb.sumStepTalk", "  {0}. [{1}] \"{2}\": falar com {3}"), i + 1, d.Id + 1 + i, s.Name, N(s.Npc))
                    : s.Kind == QuestStepKind.Collect
                    ? string.Format(Loc.T("qb.sumStepCollect", "  {0}. [{1}] \"{2}\": obter {3}"), i + 1, d.Id + 1 + i, s.Name, string.Join(", ", s.Items.Select(m => $"{m.Count} × {N(m.Id)}")))
                    : s.Kind == QuestStepKind.Deliver
                    ? string.Format(Loc.T("qb.sumStepDeliver", "  {0}. [{1}] \"{2}\": entregar {3} a {4}"), i + 1, d.Id + 1 + i, s.Name, string.Join(", ", s.Items.Select(m => $"{m.Count} × {N(m.Id)}")), N(s.Npc))
                    : s.Kind == QuestStepKind.Wait
                    ? string.Format(Loc.T("qb.sumStepWait", "  {0}. [{1}] \"{2}\": esperar {3}"), i + 1, d.Id + 1 + i, s.Name, TimeSpan.FromSeconds(s.WaitSeconds).ToString(@"h\:mm\:ss"))
                    : s.Kind == QuestStepKind.Kill
                    ? string.Format(Loc.T("qb.sumStepKill", "  {0}. [{1}] \"{2}\": matar {3}"), i + 1, d.Id + 1 + i, s.Name, string.Join(", ", s.Monsters.Select(m => $"{m.Count} × {N(m.Id)}")
                        .Concat(s.Drops.Select(x => string.Format(Loc.T("qb.sumDrop", "{0} até juntar {1} × {2} ({3:P0})"), N(x.Monster), x.Count, N(x.Item), x.Prob)))))
                    : string.Format(Loc.T("qb.sumStepReach", "  {0}. [{1}] \"{2}\": chegar a uma de {3} área(s) no mapa {4}"), i + 1, d.Id + 1 + i, s.Name, s.Areas.Count, s.MapTag));
            }
            if (d.Steps.Count > 0 && d.Steps[^1].Kind is not (QuestStepKind.Talk or QuestStepKind.Deliver) || d.Order != QuestStepOrder.InOrder)
                sb.AppendLine(Loc.T("qb.sumAutoAward", "  O prêmio é dado assim que a quest termina (sem voltar a um NPC), a não ser pela etapa de falar com NPC."));
        }
        sb.AppendLine(d.MaxLevel > 0 ? string.Format(Loc.T("qb.sumLevels", "Nível: {0} a {1}"), d.MinLevel, d.MaxLevel) : string.Format(Loc.T("qb.sumLevelMin", "Nível: a partir de {0}"), d.MinLevel));
        sb.AppendLine(string.Format(Loc.T("qb.sumAward", "Prêmio: {0:N0} de ouro, {1:N0} de experiência, {2:N0} de espírito, {3:N0} de reputação"), d.Gold, d.Exp, d.Sp, d.Reputation));
        if (d.Items.Count > 0) sb.AppendLine(d.RandomItem ? Loc.T("qb.sumOneItem", "Itens (ganha um, sorteado):") : Loc.T("qb.sumAllItems", "Itens (ganha todos):"));
        foreach (var (id, n) in d.Items) sb.AppendLine(string.Format(Loc.T("qb.sumItem", "  + {0} × {1}"), n, N(id)));
        string Val(QuestChoice c) => c.Value switch
        {
            bool => Loc.T("qe.flagOn", "✓ ligado"),
            long v when c.Option.Type == QOType.Gender => v == 1 ? Loc.T("qe.male", "Masculino") : Loc.T("qe.female", "Feminino"),
            long v when c.Option.Type == QOType.Repeat => QuestChoiceBlock.RepeatName((int)v),
            QuestSchedule sc => QuestChoiceBlock.ScheduleText(sc),
            long v => v.ToString("N0"),
            ValueTuple<long, long> r => $"{r.Item1:N0} – {r.Item2:N0}",
            List<int> ids when c.Option.Type == QOType.Classes => string.Join(", ", ids.Select(FieldKinds.ClassName)),
            List<int> ids => string.Join(", ", ids.Select(i => $"{new QuestHostImpl(this).QuestName(i) ?? "?"} ({i})")),
            List<(int Id, int Count)> its => string.Join(", ", its.Select(x => $"{x.Count} × {N(x.Id)}")),
            ValueTuple<string, string> f => $"{QuestLabels.Show(f.Item1)} = {f.Item2}",
            _ => "",
        };
        foreach (var grp in d.Choices.GroupBy(c => c.Option.Reward))
        {
            sb.AppendLine(grp.Key ? Loc.T("qb.sumMoreAward", "Mais prêmios:") : Loc.T("qb.sumMoreReq", "Requisitos:"));
            foreach (var c in grp) sb.AppendLine($"  • {c.Option.Text}: {Val(c)}");
        }
        sb.AppendLine();
        sb.AppendLine(Loc.T("qb.sumLinks", "Ao concluir:"));
        foreach (var (npc, quest, give) in QuestHostImpl.QuestLinks(d))
            sb.AppendLine(give ? string.Format(Loc.T("qb.sumLinkGive", "  • a quest entra no serviço de dar quests de {0}"), N(npc))
                : quest == d.Id ? string.Format(Loc.T("qb.sumLinkTake", "  • e no serviço de receber quests de {0}"), N(npc))
                : string.Format(Loc.T("qb.sumLinkStep", "  • a etapa {0} entra no serviço de receber quests de {1}"), quest, N(npc)));
        sb.AppendLine(Loc.T("qb.sumSave", "Nada é gravado até você clicar em Salvar (só o cliente; o servidor só com \"Salvar e enviar ao servidor\"). Ctrl+Z desfaz."));
        return sb.ToString();
    }

    private string? CommitQuestDraft(QuestDraft d)
    {
        if (_tasks is not { FullSchema: true } t) return Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data.");
        var host = new QuestHostImpl(this);
        var errors = host.Validate(d);
        if (errors.Count > 0) return string.Join("\n", errors);
        if (d.Keep)
        {
            if (t.Index.Find(d.Editing) is not QuestNode node) return Loc.T("qb.noQuest", "Quest não encontrada no tasks.data.");
            var photo = QuestBuilder.Capture(node);
            var title = string.Format(Loc.T("qb.histEdit", "Editar quest {0}"), d.Editing);
            string? failed = null;
            _edits.Custom(title,
                () => { failed = QuestBuilder.Patch(t, node, d); MarkTasksDirty(title); },
                () => { QuestBuilder.Restore(t, node, photo); MarkTasksDirty(title); });
            RefreshQuests();
            return failed;
        }
        var links = QuestHostImpl.QuestLinks(d);
        if (links.Any(l => CreatureItem(l.Npc) is null)) return Loc.T("qb.noNpc", "NPC não encontrado no elements.data.");
        // editar: a quest antiga sai (no mesmo lugar entra a nova) e os NPCs que não fazem mais parte dela perdem o ID no serviço
        var old = d.Editing > 0 ? t.Index.Find(d.Editing) as QuestNode : null;
        if (d.Editing > 0 && old is null) return Loc.T("qb.noQuest", "Quest não encontrada no tasks.data.");
        var oldLinks = old is not null && QuestBuilder.FromQuest(t, old, out _) is { } before ? QuestHostImpl.QuestLinks(before) : new();
        int oldAt = old is null ? -1 : t.Roots!.IndexOf(old);
        var label = old is null ? string.Format(Loc.T("qb.histCreate", "Criar quest {0}"), d.Id) : string.Format(Loc.T("qb.histEdit", "Editar quest {0}"), d.Editing);
        string? err = null;
        try
        {
        using (_edits.Batch(label))
        {
            foreach (var (npc, quest, give) in oldLinks.Except(links))
                if (CreatureItem(npc) is { } c) RemoveQuestFromServices(new HashSet<int> { quest }, c.Item, give);
            QuestNode? made = null;
            _edits.Custom(label,
                () => { made = old is null ? QuestBuilder.Build(t, d) : QuestBuilder.Replace(t, old, d); MarkTasksDirty(label); },
                () => { if (made is not null) t.DeleteRoot(made); if (old is not null) t.MoveRoot(old, oldAt); MarkTasksDirty(label); });
            foreach (var (npc, quest, give) in links)
                if ((err = AddQuestToService(CreatureItem(npc)!.Value.Item, quest, give)) is not null) break;
        }
        if (err is not null) _edits.Undo();   // algo não coube: desfaz a quest e o que já foi ligado
        RefreshQuests();
        return err;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { return ex.Message; }
    }

    /// <summary>
    /// Tira os IDs das vagas dos serviços de dar/receber quests (NPC_TASK_OUT/IN_SERVICE). Com <paramref name="npc"/>, só
    /// o serviço desse NPC (do lado <paramref name="give"/>); sem, de todos os serviços. Cada vaga é um passo do desfazer.
    /// </summary>
    private void RemoveQuestFromServices(IReadOnlySet<int> ids, Item? npc = null, bool give = true)
    {
        if (_store.Current is not { } m) return;
        foreach (var (field, blockName) in new[] { ("id_task_out_service", "NPC_TASK_OUT_SERVICE"), ("id_task_in_service", "NPC_TASK_IN_SERVICE") })
        {
            if (npc is not null && field != (give ? "id_task_out_service" : "id_task_in_service")) continue;
            if (m.FindListByName(blockName) is not { } block) continue;
            var svcs = npc is null ? block.Items
                : block.Items.Where(i => npc.Fields.TryGetValue(field, out var sf) && sf.Value != "0" && i.Id == sf.Value).ToList();
            foreach (var svc in svcs)
                foreach (var k in svc.Fields.Keys.Where(k => k.Contains("id_tasks") && !k.Contains("space") && int.TryParse(svc.Fields[k].Value, out var v) && ids.Contains(v)).ToList())
                    _edits.SetField(svc, k, Field.Of(svc.Fields[k].Type, "0"), "");
        }
        _refs = new RefIndex(m);
    }

    private string? DeleteQuestAsk(int questId)
    {
        if (_tasks is not { FullSchema: true } t || t.Index.Find(questId) is not QuestNode q) return null;
        while (q.Parent is { } p) q = p;
        var ids = q.Descendants().Select(n => n.Id).Append(q.Id).ToHashSet();
        var sb = new System.Text.StringBuilder(string.Format(Loc.T("quest.askDelete2", "Excluir a quest {0} \"{1}\" e as {2} subquests dela?"), q.Id, TasksFile.SplitColor(q.Name).Text, ids.Count - 1));
        sb.AppendLine().AppendLine().Append(Loc.T("quest.askDeleteSvc", "Os NPCs deixam de oferecer e de receber esta quest (o ID sai dos serviços deles)."));
        var refs = QuestBuilder.ReferencedBy(t, ids);
        if (refs.Count > 0)
            sb.AppendLine().AppendLine().Append(string.Format(Loc.T("quest.askDeleteRefs", "Atenção: {0} outra(s) quest(s) apontam para ela (quest anterior, exclusiva ou nova ao concluir) e vão esperar uma quest que não existe: {1}"),
                refs.Count, string.Join(", ", refs.Take(12)) + (refs.Count > 12 ? "…" : "")));
        sb.AppendLine().AppendLine().Append(Loc.T("quest.askDeleteUndo", "Ctrl+Z desfaz."));
        return sb.ToString();
    }

    private IReadOnlyList<(int Id, string Name)> NextQuestsOf(int questId)
    {
        if (_tasks is not { FullSchema: true } t || t.Index.Find(questId) is not QuestNode q) return Array.Empty<(int, string)>();
        while (q.Parent is { } p) q = p;
        return QuestBuilder.NextQuests(t, q.Id).Select(id => (id, t.Index.Find(id) is QuestNode n ? TasksFile.SplitColor(n.Name).Text : "")).ToList();
    }

    /// <summary>Exclui a quest principal (de qualquer ID da árvore) e tira os IDs dos serviços dos NPCs, num passo do desfazer.
    /// <paramref name="withNext"/>: também as quests seguintes da cadeia (QuestBuilder.NextQuests), no mesmo passo.</summary>
    private string? DeleteQuestById(int questId, bool withNext = false)
    {
        if (_tasks is not { FullSchema: true } t || t.Index.Find(questId) is not QuestNode q) return Loc.T("qb.noQuest", "Quest não encontrada no tasks.data.");
        if (!t.CanWrite) return Loc.T("qb.readOnly", "O tasks.data está só para leitura: ") + t.ReadOnlyReason;
        while (q.Parent is { } p) q = p;
        var roots = new List<QuestNode> { q };
        if (withNext) roots.AddRange(QuestBuilder.NextQuests(t, q.Id).Select(id => t.Index.Find(id)).OfType<QuestNode>());
        var ids = roots.SelectMany(r => r.Descendants().Select(n => n.Id).Append(r.Id)).ToHashSet();
        var label = roots.Count == 1 ? string.Format(Loc.T("quest.deleted", "Quest {0} excluída"), q.Id)
            : string.Format(Loc.T("quest.deletedChain", "Quest {0} e {1} seguinte(s) excluídas"), q.Id, roots.Count - 1);
        using (_edits.Batch(label))
        {
            RemoveQuestFromServices(ids);
            // posições guardadas antes; o desfazer devolve na ordem inversa para cada uma voltar ao lugar
            var at = roots.Select(r => t.Roots!.IndexOf(r)).ToList();
            _edits.Custom(label,
                () => { foreach (var r in roots) t.DeleteRoot(r); MarkTasksDirty(label); },
                () => { for (int i = roots.Count - 1; i >= 0; i--) t.MoveRoot(roots[i], at[i]); MarkTasksDirty(label); });
        }
        RefreshQuests();
        return null;
    }

    /// <summary>
    /// Quest apagada fora do editor (outro programa, ou a exclusão antiga que não limpava os serviços): o NPC continua com o
    /// ID no serviço de dar/receber e no jogo oferece uma quest que não existe. Tira o ID de todos os serviços.
    /// </summary>
    /// <summary>
    /// Ao abrir: serviço de dar/receber quest dos NPCs que aponta para quest que não existe no tasks.data (apagada por fora)
    /// sai sozinho, num passo do desfazer (pedido do usuário; antes só pelo botão "Tirar do NPC").
    /// </summary>
    private void CleanMissingQuestsInNpcs()
    {
        if (_tasks is not { FullSchema: true } t || _store.Current is not { } m) return;
        var missing = new HashSet<int>();
        foreach (var block in new[] { "NPC_TASK_OUT_SERVICE", "NPC_TASK_IN_SERVICE" })
            foreach (var svc in m.FindListByName(block)?.Items ?? new())
                foreach (var (k, f) in svc.Fields)
                    if (k.Contains("id_tasks") && !k.Contains("space") && int.TryParse(f.Value, out var q) && q > 0 && t.Index.Find(q) is null) missing.Add(q);
        if (missing.Count == 0) return;
        using (_edits.Batch(string.Format(Loc.T("qb.histCleanMissing", "Tirar dos NPCs {0} quest(s) que não existem"), missing.Count))) RemoveQuestFromServices(missing);
        Status(string.Format(Loc.T("qb.cleanedMissing", "{0} quest(s) que não existem no tasks.data saíram dos NPCs (Ctrl+Z desfaz; Salvar grava)."), missing.Count));
    }

    private string? RemoveMissingQuest(int questId)
    {
        if (_tasks is not { } t) return Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data.");
        if (t.Index.Find(questId) is not null) return string.Format(Loc.T("qb.questExists", "A quest {0} existe: use Excluir quest."), questId);
        var label = string.Format(Loc.T("qb.histRemoveMissing", "Tirar dos NPCs a quest {0} (não existe)"), questId);
        using (_edits.Batch(label)) RemoveQuestFromServices(new HashSet<int> { questId });
        return null;
    }

    private string? LinkQuestToNpc(int npcId, int questId)
    {
        if (_tasks?.Index.Find(questId) is not QuestNode q || q.Field("m_ulDelvNPC") is not { } f) return Loc.T("qb.noQuest", "Quest não encontrada no tasks.data.");
        if (!_tasks.CanWrite) return Loc.T("qb.readOnly", "O tasks.data está só para leitura: ") + _tasks.ReadOnlyReason;
        if (CreatureItem(npcId) is not { Block.Name: "NPC_ESSENCE" } npc) return Loc.T("qb.noNpc", "NPC não encontrado no elements.data.");
        if (AddQuestToService(npc.Item, questId, give: true, dryRun: true) is { } full) return full;
        var old = q.Get(f);
        var label = string.Format(Loc.T("qb.histLink", "Dar quest {0} ao NPC {1}"), questId, npcId);
        string? err;
        using (_edits.Batch(label))
        {
            _edits.Custom(label,
                () => { q.Set(f, npcId.ToString()); _tasks.InvalidateNpcIndex(); MarkTasksDirty(label); },
                () => { q.Set(f, old); _tasks.InvalidateNpcIndex(); MarkTasksDirty(label); });
            err = AddQuestToService(npc.Item, questId, give: true);
        }
        if (err is not null) _edits.Undo();
        return err;
    }

    /// <summary>
    /// Põe a quest numa vaga livre do serviço de dar (NPC_TASK_OUT_SERVICE) ou receber (NPC_TASK_IN_SERVICE) do NPC. Se o
    /// NPC não tem serviço, ou divide o serviço com outros NPCs, ganha um serviço próprio (cópia) para não mexer nos outros.
    /// </summary>
    private string? AddQuestToService(Item npc, int questId, bool give, bool dryRun = false)
    {
        if (_store.Current is not { } m) return Loc.T("qb.noElements", "Abra o elements.data.");
        string field = give ? "id_task_out_service" : "id_task_in_service", blockName = give ? "NPC_TASK_OUT_SERVICE" : "NPC_TASK_IN_SERVICE";
        if (!npc.Fields.TryGetValue(field, out var sf) || m.FindListByName(blockName) is not { } block || block.Items.Count == 0)
            return string.Format(Loc.T("qb.noService", "Este elements.data não tem {0}."), blockName);
        static bool Slot(string k) => k.Contains("id_tasks") && !k.Contains("space");
        var svc = block.Items.FirstOrDefault(i => i.Id == sf.Value && sf.Value != "0");
        bool shared = svc is not null && m.FindListByName("NPC_ESSENCE")?.Items.Count(n => n != npc && n.Fields.TryGetValue(field, out var o) && o.Value == svc.Id) > 0;
        if (svc is not null && svc.Fields.Any(kv => Slot(kv.Key) && kv.Value.Value == questId.ToString())) return null;   // já está
        if (svc is not null && !svc.Fields.Any(kv => Slot(kv.Key) && kv.Value.Value is "0" or ""))
            return string.Format(Loc.T("qb.serviceFull", "O serviço {0} do NPC está cheio."), svc.Id);
        if (dryRun) return null;   // só conferir: tudo ou nada
        if (svc is null || shared)
        {
            var src = svc ?? block.Items[0];
            var ni = new Item { Id = IdRefactor.NextFreeId(m, block).ToString(), Name = TasksFile.SplitColor(npc.Name).Text, Fields = src.Fields.Clone(), Raw = src.Raw?.ToArray() };
            if (svc is null) foreach (var k in ni.Fields.Keys.Where(Slot).ToList()) ni.Fields[k] = Field.Of(ni.Fields[k].Type, "0");
            SetIdName(ni);
            _edits.AddItem(block, ni, "");
            _owner[ni] = block;
            _edits.SetField(npc, field, Field.Of(sf.Type, ni.Id), "");
            svc = ni;
        }
        var free = svc.Fields.Keys.FirstOrDefault(k => Slot(k) && svc.Fields[k].Value is "0" or "");
        if (free is null) return string.Format(Loc.T("qb.serviceFull", "O serviço {0} do NPC está cheio."), svc.Id);
        _edits.SetField(svc, free, Field.Of(svc.Fields[free].Type, questId.ToString()), "");
        _refs = new RefIndex(m);
        return null;
    }

    /// <summary>
    /// Quests que o NPC dá e recebe (Mundo 3D): serviços do NPC no elements (NPC_TASK_OUT/IN_SERVICE, campos id_tasks_N)
    /// e campos das quests (m_ulDelvNPC / m_ulAwardNPC). Null se o ID não é um NPC.
    /// </summary>
    private NpcQuestInfo? NpcQuests(int npcId)
    {
        if (CreatureItem(npcId) is not { } npc || npc.Block.Name != "NPC_ESSENCE") return null;
        var m = _store.Current;
        List<int> Service(string field, string block)
        {
            var ids = new List<int>();
            if (m is null || !npc.Item.Fields.TryGetValue(field, out var sf) || sf.Value is "0" or "" || m.FindListByName(block) is not { } bl) return ids;
            if (bl.Items.FirstOrDefault(i => i.Id == sf.Value) is not { } svc) return ids;
            foreach (var (k, f) in svc.Fields)
                if (k.Contains("id_tasks") && !k.Contains("space") && int.TryParse(f.Value, out var q) && q > 0) ids.Add(q);
            return ids;
        }
        List<NpcQuestRef> Merge(List<int> service, IReadOnlyList<QuestNode> byField)
        {
            var list = new List<NpcQuestRef>();
            var fieldIds = byField.Select(q => q.Id).ToHashSet();
            foreach (var id in service.Distinct())
                list.Add(new(id, _tasks?.Index.Find(id) is { } row ? TasksFile.SplitColor(TaskIndex.NameOf(row)).Text : "", true, fieldIds.Contains(id)));
            foreach (var q in byField)
                if (!service.Contains(q.Id)) list.Add(new(q.Id, TasksFile.SplitColor(q.Name).Text, false, true));
            return list;
        }
        var gives = Merge(Service("id_task_out_service", "NPC_TASK_OUT_SERVICE"), _tasks?.QuestsOfNpc(npcId, true) ?? Array.Empty<QuestNode>());
        var takes = Merge(Service("id_task_in_service", "NPC_TASK_IN_SERVICE"), _tasks?.QuestsOfNpc(npcId, false) ?? Array.Empty<QuestNode>());
        return new NpcQuestInfo(npcId, TasksFile.SplitColor(npc.Item.Name).Text, gives, takes);
    }

    /// <summary>
    /// Anúncios do monstro (AiAnnounce + AnnounceDialog): política compartilhada vira cópia só dele (common_strategy no elements,
    /// par cliente + servidor no Salvar); tudo num passo do Ctrl+Z (usuário 01/11).
    /// </summary>
    private void OpenAnnouncements(IWin32Window owner, int monsterId)
    {
        if (CreatureItem(monsterId) is not { Block.Name: "MONSTER_ESSENCE" } ci) { Status(Loc.T("ann.onlyMonster", "Anúncio só para monstro.")); return; }
        if (_aiView?.OpenFile is not { } f) { MessageBox.Show(owner, Loc.T("ann.noAi", "Abra o aipolicy.data na área AI Policy primeiro (ele carrega sozinho ao abrir a pasta do PW)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var item = ci.Item;
        int pid = int.TryParse(item.Fields.GetValueOrDefault("common_strategy")?.Value, out var v) ? v : 0;
        int users = pid <= 0 ? 0 : ci.Block.Items.Count(i => i.Fields.GetValueOrDefault("common_strategy")?.Value == pid.ToString());
        var pol = f.Policies.FirstOrDefault(p => p.Id == pid);
        int ver = pol is null ? AiAnnounce.TriggerVersion(f, new AiPolicy()) : AiAnnounce.TriggerVersion(f, pol);
        using var dlg = new AnnounceDialog(TasksFile.SplitColor(item.Name).Text, pid, users, ver, pol is null ? new List<AiAnnounce.Info>() : AiAnnounce.Find(pol),
            pol is null ? null : AiAnnounce.GameTalks(pol));
        if (dlg.ShowDialog(owner) != DialogResult.OK || dlg.Added.Count + dlg.Removed.Count == 0) return;

        var target = AiAnnounce.OwnPolicy(f, pid, pol is null ? 2 : users, out bool cloned);
        var before = target.Triggers.ToList();
        foreach (var t in dlg.Removed) target.Triggers.RemoveAll(x => x.Id == t.Id);
        foreach (var a in dlg.Added) target.Triggers.Add(AiAnnounce.Make(f, target, a.When, a.Prefix, a.Text, a.AppendName));
        var after = target.Triggers.ToList();
        target.Triggers = before;   // o Custom abaixo aplica (e o Ctrl+Z volta)
        if (cloned) f.Policies.Remove(target);
        var label = string.Format(Loc.T("ann.hist", "Anúncios de {0}"), monsterId);
        using (_edits.Batch(label))
        {
            _edits.Custom(label,
                () => { if (cloned && !f.Policies.Contains(target)) f.Policies.Add(target); target.Triggers = after.ToList(); _aiView?.ChangedOutside(_store.Current); },
                () => { target.Triggers = before.ToList(); if (cloned) f.Policies.Remove(target); _aiView?.ChangedOutside(_store.Current); });
            if (cloned && item.Fields.TryGetValue("common_strategy", out var fs)) _edits.SetField(item, "common_strategy", Field.Of(fs.Type, target.Id.ToString()), "");
        }
        Status(string.Format(Loc.T("ann.done", "Anúncios de {0} aplicados{1} (Ctrl+Z desfaz). Salve e envie ao servidor."), item.Name,
            cloned ? string.Format(Loc.T("ann.clonedNote", " · IA própria: política {0}"), target.Id) : ""));
    }

    /// <summary>
    /// Anúncio de morte de um plugin (tela Plugins, renascer desligado): gatilho "MyAce anuncio plugin &lt;id&gt;" na IA do
    /// monstro com "$B" + texto; null tira. Mesma montagem do <see cref="OpenAnnouncements"/> (IA própria se a política é
    /// dividida, Ctrl+Z). Não mexe se já está igual. null = ok; senão o motivo.
    /// </summary>
    internal string? SetPluginDeathAnnounce(string pluginId, int monsterId, string? text)
    {
        if (CreatureItem(monsterId) is not { Block.Name: "MONSTER_ESSENCE" } ci) return Loc.T("ann.onlyMonster", "Anúncio só para monstro.");
        if (_aiView?.OpenFile is not { } f) return Loc.T("ann.noAi", "Abra o aipolicy.data na área AI Policy primeiro (ele carrega sozinho ao abrir a pasta do PW).");
        var item = ci.Item;
        var name = $"{AiAnnounce.Tag} plugin {pluginId}";
        int pid = int.TryParse(item.Fields.GetValueOrDefault("common_strategy")?.Value, out var v) ? v : 0;
        var pol = f.Policies.FirstOrDefault(p => p.Id == pid);
        var mine = pol is null ? null : AiAnnounce.Find(pol).FirstOrDefault(a => a.Trigger.Name == name);
        // $S = canal 9, o mesmo da "Mensagem ao surgir" do plugin (PublicChat 9)
        if (text is null ? mine is null : mine is { Prefix: "$S" } && mine.Text == text && mine.Trigger.Run == 0) return null;   // já está como deve
        if (text is null && pol is null) return null;
        int users = pid <= 0 ? 0 : ci.Block.Items.Count(i => i.Fields.GetValueOrDefault("common_strategy")?.Value == pid.ToString());
        var target = AiAnnounce.OwnPolicy(f, pid, pol is null ? 2 : users, out bool cloned);
        var before = target.Triggers.ToList();
        target.Triggers.RemoveAll(x => x.Name == name);
        if (text is not null)
        {
            var t = AiAnnounce.Make(f, target, AiAnnounce.When.Died, "$S", text, false);
            t.Name = name;
            target.Triggers.Add(t);
        }
        var after = target.Triggers.ToList();
        target.Triggers = before;
        if (cloned) f.Policies.Remove(target);
        var label = string.Format(Loc.T("ann.hist", "Anúncios de {0}"), monsterId);
        using (_edits.Batch(label))
        {
            _edits.Custom(label,
                () => { if (cloned && !f.Policies.Contains(target)) f.Policies.Add(target); target.Triggers = after.ToList(); _aiView?.ChangedOutside(_store.Current); },
                () => { target.Triggers = before.ToList(); if (cloned) f.Policies.Remove(target); _aiView?.ChangedOutside(_store.Current); });
            if (cloned && item.Fields.TryGetValue("common_strategy", out var fs)) _edits.SetField(item, "common_strategy", Field.Of(fs.Type, target.Id.ToString()), "");
        }
        return null;
    }

    private (ItemList Block, Item Item)? CreatureItem(int id)
    {
        if (_store.Current is not { } m) return null;
        if (_creatureCache is null || _creatureCacheFor != m)
        {
            _creatureCacheFor = m;
            _creatureCache = new();
            foreach (var bn in new[] { "MONSTER_ESSENCE", "NPC_ESSENCE", "MINE_ESSENCE" })
                if (m.FindListByName(bn) is { } l)
                    foreach (var it in l.Items) _creatureCache.TryAdd(it.Id, (l, it));
        }
        return _creatureCache.TryGetValue(id.ToString(), out var r) ? r : (_refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? h : null);
    }

    // ---------- assistente: monstro/NPC novo a partir de um modelo ----------
    // Copia um monstro/NPC existente (modelo 3D, atributos, habilidades…) com ID novo — o primeiro livre em todos os
    // blocos que dividem IDs, para não colidir — e nome novo. O resto se ajusta depois no editor (abre no item novo).
    private int? CreateCreatureWizard(IWin32Window owner, NpcGenView.Kind kind)
    {
        if (_store.Current is not { } module || _refs is null) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return null; }
        using var f = new Form
        {
            Text = Loc.T("wiz.title", "Novo monstro ou NPC"), Size = new Size(620, 430), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = new Font("Segoe UI", 9.5f),
        };
        Label L(string key, string pt, int x, int y) { var l = new Label { Text = Loc.T(key, pt), AutoSize = true, Location = new Point(x, y) }; f.Controls.Add(l); return l; }
        L("wiz.type", "Tipo:", 16, 20);
        var typeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(120, 16), Width = 160 };
        typeBox.Items.AddRange(new object[] { Loc.T("wiz.monster", "Monstro"), Loc.T("wiz.npc", "NPC") });
        typeBox.SelectedIndex = kind == NpcGenView.Kind.Npc ? 1 : 0;
        L("wiz.base", "Modelo de base:", 16, 58);
        var pickBtn = new Button { Text = Loc.T("wiz.pick", "Escolher…"), Location = new Point(120, 52), Width = 110, Height = 28 };
        var baseLbl = new Label { Location = new Point(240, 58), AutoSize = true, ForeColor = CMuted, Text = Loc.T("wiz.noBase", "(nenhum)") };
        L("wiz.name", "Nome:", 16, 100);
        var nameBox = new TextBox { Location = new Point(120, 96), Width = 230 };
        L("wiz.id", "ID novo:", 16, 138);
        var idBox = new NumericUpDown { Location = new Point(120, 134), Width = 110, Maximum = int.MaxValue, Enabled = false };   // automático: o próximo livre
        var hint = new Label
        {
            Location = new Point(16, 180), Size = new Size(340, 150), ForeColor = CMuted,
            Text = Loc.T("wiz.hint", "Copia tudo do modelo de base (aparência, nível, vida, ataques, habilidades, drops) com o ID e o nome novos. Depois do OK o item novo abre no editor para você ajustar o resto.\nNPC novo nasce limpo: nome \"NPC - ID\", sem fala e sem serviços (conversa, quests, loja…).\n\nO mesmo ID pode nascer em muitos pontos do mapa sem conflito; um ID novo é só para quando você quer um monstro/NPC diferente."),
        };
        var pic = new PictureBox { Location = new Point(370, 16), Size = new Size(220, 300), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 38, 46) };
        var ok = new Button { Text = Loc.T("wiz.create", "Criar"), Location = new Point(390, 340), Size = new Size(95, 30), DialogResult = DialogResult.OK, Enabled = false, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), Location = new Point(495, 340), Size = new Size(95, 30), DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { typeBox, pickBtn, baseLbl, nameBox, idBox, hint, pic, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        Item? baseItem = null;
        // o próximo ID livre já aparece ao abrir (e ao trocar o tipo), antes de escolher a base
        void SuggestId()
        {
            if (module.FindListByName(typeBox.SelectedIndex == 1 ? "NPC_ESSENCE" : "MONSTER_ESSENCE") is { } l)
                idBox.Value = Math.Min(idBox.Maximum, IdRefactor.NextFreeId(module, l));
        }
        SuggestId();
        typeBox.SelectedIndexChanged += (_, _) => { if (baseItem is null) SuggestId(); };
        pickBtn.Click += (_, _) =>
        {
            var k = typeBox.SelectedIndex == 1 ? NpcGenView.Kind.Npc : NpcGenView.Kind.Monster;
            if (PickSpawn(f, k, baseItem is null ? 0 : int.Parse(baseItem.Id)) is not int id || _refs.Resolve(FieldKind.ItemRef, id.ToString()) is not { } h) return;
            baseItem = h.Item;
            var nm = TasksFile.SplitColor(h.Item.Name).Text;
            baseLbl.Text = $"{id} — {nm} ({Pretty(h.Block.Name)})";
            if (nameBox.Text.Length == 0 || nameBox.Tag as string == nameBox.Text) nameBox.Text = nameBox.Tag as string ?? "";
            nameBox.Text = nm + " " + Loc.T("wiz.newSuffix", "(novo)"); nameBox.Tag = nameBox.Text;
            idBox.Value = Math.Min(idBox.Maximum, IdRefactor.NextFreeId(module, h.Block));
            if (h.Block.Name == "NPC_ESSENCE") { nameBox.Text = $"NPC - {(long)idBox.Value}"; nameBox.Tag = nameBox.Text; }   // NPC novo nasce sem o nome da base
            var fm = h.Item.Fields.FirstOrDefault(x => x.Key.Contains("file_model")).Value?.Value;
            pic.Image = ModelPreviewer()?.Render(fm is not null && fm.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase) ? fm : null, 220, 0);
            ok.Enabled = true;
        };
        if (f.ShowDialog(owner) != DialogResult.OK || baseItem is null || !_owner.TryGetValue(baseItem, out var block)) return null;
        var newId = ((long)idBox.Value).ToString();
        if (IdRefactor.Validate(module, block, baseItem, newId) is { } err) { MessageBox.Show(this, err, f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
        var ni = new Item
        {
            Id = newId, Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : baseItem.Name,
            Fields = baseItem.Fields.Clone(),
            Raw = baseItem.Raw?.ToArray(),
        };
        if (block.Name == "NPC_ESSENCE") ClearNpc(ni);
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status(string.Format(Loc.T("wiz.done2", "{0} criado com ID {1} — ajuste os campos e salve. Para ele nascer no mundo, ponha um ponto no NPCGen ou no Mundo 3D (quantidade e tempo de renascer; o jogo pode sortear o tempo, mas não tem chance de não nascer)."), ni.Name, ni.Id));
        return int.Parse(ni.Id);
    }

    /// <summary>NPC novo nasce limpo (pedido de 30/10): sem fala de boas-vindas e sem NENHUM serviço (conversa, quests,
    /// loja, teleporte…), para o usuário definir depois; aparência, nível, monstro de origem etc. vêm da base.</summary>
    private static void ClearNpc(Item npc) => NpcBuilder.Clear(npc);

    // NPCGen: escolher o monstro, NPC ou coletável de um ponto (mesma janela de escolha, com prévia do modelo)
    private int? PickSpawn(IWin32Window owner, NpcGenView.Kind kind, int current)
    {
        var block = kind switch { NpcGenView.Kind.Npc => "NPC_ESSENCE", NpcGenView.Kind.Mine => "MINE_ESSENCE", _ => "MONSTER_ESSENCE" };
        // pontos de criatura aceitam monstro e NPC (há NPCs em pontos de monstro); abre no filtro do tipo do ponto
        var blocks = kind == NpcGenView.Kind.Mine ? new[] { block } : new[] { "MONSTER_ESSENCE", "NPC_ESSENCE" };
        var src = _store.Current?.Lists.Where(l => blocks.Contains(l.Name)).SelectMany(l => l.Items).ToList() ?? new();
        if (src.Count == 0) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return null; }
        var picked = FieldEditors.Pick(owner, kind switch
            {
                NpcGenView.Kind.Npc => Loc.T("pick.npc", "Escolher NPC"), NpcGenView.Kind.Mine => Loc.T("pick.mine", "Escolher coletável"),
                _ => Loc.T("pick.monster", "Escolher monstro"),
            }, src, current > 0 ? current.ToString() : null, IconIndexFor, _treeIcons,
            it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : "", it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : null, false,
            blocks.Length > 1 ? Pretty(block) : null,
            (it, yaw) => ModelPreviewer()?.Render(it.Fields.FirstOrDefault(x => x.Key.Contains("file_model")).Value?.Value is { } fm && fm.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase) ? fm : null, 240, yaw));
        return picked is { Count: > 0 } && int.TryParse(picked[0].Id, out var id) ? id : null;
    }

    // Loja: escolher o item vendido (mesma janela de escolha dos campos de item)
    // janela de escolher item (a mesma do Mundo 3D): a loja usa em "Trocar item"
    private int? PickShopItem(IWin32Window owner, int current)
    {
        IQuestHost host = new QuestHostImpl(this);
        using var w = new ItemSearchWindow(host.AllItems(), host.ItemIcon, askQuantity: false, tip: TipImage, favorites: FavoriteIds(), recent: RecentIds(), model: ItemModel, fav: ItemFavorites());
        if (w.ShowDialog(owner) != DialogResult.OK) return null;
        NotePicked(w.Picked);
        return w.Picked;
    }

    // monstro e NPC não têm ícone no jogo: a miniatura do modelo 3D chega depois, em segundo plano
    private Image? CreatureIcon(int id)
    {
        if (CreatureItem(id) is not { } c) return null;
        int at = IconIndexFor(c.Item);
        return at >= 0 ? _treeIcons.Images[at] : null;
    }

    /// <summary>
    /// Prévia 3D de um item na janela de escolher: arma na mão (file_model_right/left), depois o modelo do item (file_model)
    /// e por fim o que cai no chão (file_matter). Só .ecm abre; sem modelo = null (a janela mostra o ícone grande).
    /// </summary>
    internal Image? ItemModel(int id, float yaw)
    {
        if (_refs?.Resolve(FieldKind.ItemRef, id.ToString()) is not { } h) return null;
        foreach (var key in new[] { "file_model_right", "file_model_left", "file_model", "file_matter" })
            if (h.Item.Fields.FirstOrDefault(x => x.Key == key).Value?.Value is { Length: > 0 } fm && fm.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase)
                && ModelPreviewer()?.Render(fm, 240, yaw) is { } img) return img;
        return null;
    }

    private Image? CreatureModel(int id, float yaw) =>
        CreatureItem(id) is { } c && c.Item.Fields.FirstOrDefault(x => x.Key.Contains("file_model")).Value?.Value is { } fm && fm.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase)
            ? ModelPreviewer()?.Render(fm, 240, yaw) : null;

    /// <summary>Favoritos da JAV de itens e de criaturas: os mesmos do editor de elements (marcar num lugar aparece no outro).</summary>
    internal JavFavorites? ItemFavorites() => _favFile is null || _store.Current is null ? null
        : new JavFavorites(FavoriteIds, id => FavoriteIds().Contains(id), ToggleFavoriteId);

    /// <summary>
    /// Favoritos de criaturas (monstros e NPCs): os MESMOS do editor de elements (usuário 08/11). A JAV de criaturas só mostra
    /// os favoritos que são criatura e a de itens só os que são item (cada uma filtra pelas linhas dela); a estrela grava no
    /// favoritos do elements.
    /// </summary>
    internal JavFavorites? CreatureFavorites() => ItemFavorites();

    /// <summary>Estrela da JAV num item: marca/desmarca nos favoritos do elements. null = deu certo.</summary>
    private string? ToggleFavoriteId(int id)
    {
        if (_store.Current is not { } m) return Loc.T("is.favNoData", "Abra um elements.data primeiro.");
        string sid = id.ToString();
        foreach (var l in m.Lists)
            if (l.FindById(sid) is { } it)
            {
                var key = FavKey(l, it);
                if (!_favorites.Remove(key)) _favorites.Add(key);
                SaveFavorites();
                if (_favModeBtn is not null) _favModeBtn.Text = FavModeText();
                if (_favMode) RefreshView();
                return null;
            }
        return Loc.T("is.favNoItem", "Esse item não está neste elements.data.");
    }

    private List<int> FavoriteIds() => _favorites.Select(k => k[(k.LastIndexOf('|') + 1)..]).Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();
    private List<int> RecentIds() => _recentKeys.Select(k => int.TryParse(k.Id, out var n) ? n : 0).Where(n => n > 0).ToList();
    private void NotePicked(int id)
    {
        var text = id.ToString();
        if (_store.Current?.Lists.SelectMany(l => l.Items).FirstOrDefault(i => i.Id == text) is { } it) RememberRecent(it);
    }

    /// <summary>Diagnóstico (--create-shot): janelas da ferramenta Criar e dicas com os dados do cliente aberto, fora da tela.</summary>
    /// <summary>--create-shot: as mudanças da ferramenta Teleportes na memória, com Ctrl+Z, sem gravar nada.</summary>
    private void TeleportHostCheck(string file)
    {
        var o = new List<string>();
        var npcs = _store.Current!.FindListByName("NPC_ESSENCE")!.Items;
        var shared = npcs.GroupBy(n => NpcNumber(n, "id_transmit_service")).First(g => g.Key > 0 && ServiceById(g.Key) is not null && g.Count() > 1).ToList();
        int a = int.Parse(shared[0].Id), b = int.Parse(shared[1].Id), service = NpcNumber(shared[0], "id_transmit_service");
        var free = npcs.First(n => NpcNumber(n, "id_transmit_service") == 0);
        int c = int.Parse(free.Id), point = 9999;
        int Stops(int npc) => ServiceById(NpcNumber(NpcById(npc)!, "id_transmit_service")) is { } s ? NpcBuilder.StopsOf(s).Count : 0;
        string Line() => $"A serviço {NpcNumber(NpcById(a)!, "id_transmit_service")} ({Stops(a)})  B serviço {NpcNumber(NpcById(b)!, "id_transmit_service")} ({Stops(b)})  C serviço {NpcNumber(free, "id_transmit_service")} ({Stops(c)})  itens de serviço {_store.Current!.FindListByName("NPC_TRANSMIT_SERVICE")!.Items.Count}";
        o.Add($"início, {shared.Count} NPCs no serviço {service}: " + Line());
        o.Add("cópia só para A: " + (SetTransmit(a, point, 700, 20, copy: true) ?? "ok") + "  " + Line());
        o.Add("A leva: " + string.Join(", ", TeleportersTo(point)));
        _edits.Undo();
        o.Add("Ctrl+Z: " + Line() + "  leva " + TeleportersTo(point).Count);
        o.Add("para todos: " + (SetTransmit(a, point, 700, 20, copy: false) ?? "ok") + "  " + Line() + "  leva " + TeleportersTo(point).Count);
        o.Add("muda taxa: " + (SetTransmit(a, point, 900, 30, copy: false) ?? "ok") + "  " + string.Join(", ", TeleportersTo(point).Take(1)));
        _edits.Undo(); _edits.Undo();
        o.Add("Ctrl+Z duas vezes: " + Line());
        o.Add("C sem serviço: " + (SetTransmit(c, point, 0, 0, copy: false) ?? "ok") + "  " + Line());
        o.Add("tira o último de C: " + (RemoveTransmit(c, point, copy: false) ?? "ok") + "  " + Line());
        _edits.Undo(); _edits.Undo();
        o.Add("Ctrl+Z duas vezes: " + Line());
        o.Add("descobre: " + (SetDiscover(c, point) ?? "ok") + "  " + string.Join(", ", DiscoverersOf(point)));
        _edits.Undo();
        o.Add("Ctrl+Z: " + DiscoverersOf(point).Count + ", bit " + NpcNumber(free, "combined_services"));
        var host = new QuestHostImpl(this);
        o.Add("Atlas: " + host.AtlasName + " tem o ponto " + host.AtlasHas(point));
        o.Add("põe: " + (host.SetAtlas(new WorldTarget(point, "Ponto X", 1, 10, 220, 30, -1), true) ?? "ok") + " tem " + host.AtlasHas(point));
        _edits.Undo();
        o.Add("Ctrl+Z: tem " + host.AtlasHas(point));
        var seller = npcs.First(n => NpcNumber(n, "id_sell_service") > 0);
        // serviços separados: NPC comum ganha loja, Ctrl+Z tira; quem tem loja e fornalha abre com as duas
        var plain = npcs.First(n => NpcNumber(n, "id_sell_service") == 0 && NpcNumber(n, "id_make_service") == 0 && NpcNumber(n, "id_transmit_service") == 0);
        var pd = host.LoadNpc(int.Parse(plain.Id))!;
        pd.Shop = new ShopDraft(); pd.Shop.Goods[0][0] = host.LoadNpc(int.Parse(seller.Id))!.Shop!.Goods[0].First(g => g > 0);
        o.Add($"comum {plain.Id} ganha loja: " + (host.UpdateNpc(pd, false) ?? "ok") + $" serviço {NpcNumber(plain, "id_sell_service")} compra {NpcNumber(plain, "id_buy_service")} itens {host.LoadNpc(int.Parse(plain.Id))!.Shop?.Count}");
        var pd2 = host.LoadNpc(int.Parse(plain.Id))!; pd2.Shop = null; pd2.Kind = NpcKind.Npc;
        o.Add("tira a loja: " + (host.UpdateNpc(pd2, false) ?? "ok") + $" serviço {NpcNumber(plain, "id_sell_service")} compra {NpcNumber(plain, "id_buy_service")}");
        _edits.Undo(); _edits.Undo();
        o.Add($"Ctrl+Z duas vezes: serviço {NpcNumber(plain, "id_sell_service")} compra {NpcNumber(plain, "id_buy_service")}");
        if (npcs.FirstOrDefault(n => NpcNumber(n, "id_sell_service") > 0 && NpcNumber(n, "id_make_service") > 0) is { } both)
        {
            var bd = host.LoadNpc(int.Parse(both.Id))!;
            o.Add($"loja e fornalha {both.Id}: loja {bd.Shop?.Count} fabricação {bd.Make?.Count} usuários que mudam {host.ServiceUsersOf(bd)}");
        }
        var loaded = host.LoadNpc(int.Parse(seller.Id))!;
        int first = loaded.Shop!.Goods[0][0];
        o.Add($"editar {seller.Id} {loaded.Name}: {loaded.Kind}, {loaded.Shop.Count} itens, usuários do serviço {host.ServiceUsersOf(loaded)}");
        loaded.Shop.Goods[0][0] = 0; loaded.Name = "Nome novo";
        o.Add("grava: " + (host.UpdateNpc(loaded, copy: true) ?? "ok") + " nome " + seller.Name + " agora itens " + host.LoadNpc(int.Parse(seller.Id))!.Shop!.Count);
        _edits.Undo();
        o.Add("Ctrl+Z: " + seller.Name + " item1 " + host.LoadNpc(int.Parse(seller.Id))!.Shop!.Goods[0][0] + " era " + first);
        var forge = npcs.FirstOrDefault(n => NpcNumber(n, "id_make_service") > 0);
        if (forge is not null && host.LoadNpc(int.Parse(forge.Id)) is { Make: { } fm } fl)
        {
            var r0 = fm.Goods.SelectMany(p => p).First(g => g is not null)!;
            var rd = host.ReadRecipe(r0.RecipeId);
            o.Add($"fornalha {forge.Id}: {fm.Count} receitas, profissão {fm.Skill}; 1ª {r0.RecipeId}: faz {rd?.Product} x{rd?.Count}, {rd?.Materials.Count} materiais, nível {rd?.SkillLevel}");
        }
        File.WriteAllLines(file, o);
    }

    internal void CreateShots(string dir)
    {
        Directory.CreateDirectory(dir);
        if (_store.Current is not { } m || _dataPath is null || Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) is not { } element) return;
        TeleportHostCheck(Path.Combine(dir, "teleport-host.txt"));
        var host = new QuestHostImpl(this);
        var ids = host.AllItems().Where(r => host.ItemIcon(r.Id) is not null).Take(12).Select(r => r.Id).ToList();
        void Save(Control c, string name)
        {
            using var f = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = c.Size };
            f.Controls.Add(c); f.Show(); Application.DoEvents();
            using var b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
            b.Save(Path.Combine(dir, name));
            f.Controls.Remove(c);
        }
        if (WorldTargets.Open(element, null) is { Client.Count: > 2 } targets)
        {
            using var stops = new TeleportStopsWindow(targets.Client, [new TeleportStop(targets.Client[0].Id, 500, 10), new TeleportStop(targets.Client[1].Id, 0, 0)], targets.Client[2].Id)
                { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            stops.Show(); Application.DoEvents();
            using var b = new Bitmap(stops.Width, stops.Height);
            stops.DrawToBitmap(b, new Rectangle(0, 0, stops.Width, stops.Height));
            b.Save(Path.Combine(dir, "teleport-stops.png"));
        }
        var shop = new ShopDraft(); shop.Titles[0] = "Armas"; shop.Titles[1] = "Poções";
        for (int i = 0; i < ids.Count; i++) shop.Goods[0][i * 2] = ids[i];
        using (var w = new GameServiceWindow(element, false, host)) { w.Load(shop); Save(w, "shop.png"); }
        var make = new MakeDraft();
        make.Goods[0][0] = new RecipeDraft { Product = ids[0], Count = 1, Price = 1500, Materials = [(ids[1], 3), (ids[2], 1)] };
        make.Goods[0][1] = new RecipeDraft { Product = ids[3], Materials = [(ids[4], 2)] };
        using (var w = new GameServiceWindow(element, true, host)) { w.Load(make); w.ShowSlot(0); Save(w, "forge.png"); }
        TipImage(ids[0])?.Save(Path.Combine(dir, "tip-item.png"));
        var mob = m.FindListByName("MONSTER_ESSENCE")?.Items.FirstOrDefault(i => i.Name.Length > 0);
        if (mob is not null) TipImage(int.Parse(mob.Id))?.Save(Path.Combine(dir, "tip-monster.png"));
        var rows = (m.FindListByName("MONSTER_ESSENCE")?.Items ?? []).Take(40).Select(i => (int.Parse(i.Id), TasksFile.SplitColor(i.Name).Text, Pretty("MONSTER_ESSENCE"))).ToList();
        using (var w = new ItemSearchWindow(rows, CreatureIcon, askQuantity: false, tip: TipImage, recent: RecentIds(), model: CreatureModel, fav: CreatureFavorites()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
        {
            w.Show();
            var stop = DateTime.Now.AddSeconds(35);
            using (var warm = new Bitmap(w.Width, w.Height))   // fora da tela a lista não se pinta sozinha: cada foto pede as miniaturas
                while (DateTime.Now < stop) { w.DrawToBitmap(warm, new Rectangle(0, 0, w.Width, w.Height)); for (int k = 0; k < 40; k++) { Application.DoEvents(); Thread.Sleep(25); } }
            w.ShotModel(rows[3].Item1);
            Application.DoEvents();
            using var b = new Bitmap(w.Width, w.Height);
            w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
            b.Save(Path.Combine(dir, "jav-creature.png"));
        }
        // JAV de itens: prévia 3D (arma) e item sem modelo (ícone grande + aviso), estrela no mesmo lugar das outras JAVs
        var irows = (m.FindListByName("WEAPON_ESSENCE")?.Items ?? []).Take(20).Concat((m.FindListByName("MATERIAL_ESSENCE")?.Items ?? []).Take(5))
            .Select(i => (int.Parse(i.Id), TasksFile.SplitColor(i.Name).Text, "x")).ToList();
        if (irows.Count > 0)
            using (var w = new ItemSearchWindow(irows, host.ItemIcon, askQuantity: false, model: ItemModel, fav: ItemFavorites()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
            {
                w.Show(); Application.DoEvents();
                foreach (var (row, file) in new[] { (2, "jav-item-model.png"), (irows.Count - 1, "jav-item-nomodel.png") })
                {
                    w.ShotModel(irows[row].Item1); w.SelectRow(row); Application.DoEvents();
                    using var b = new Bitmap(w.Width, w.Height);
                    w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
                    b.Save(Path.Combine(dir, file));
                }
            }
        using (var w = new RecipeWindow(host, make.Goods[0][0]) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
        {
            w.Show(); Application.DoEvents();
            using var b = new Bitmap(w.Width, w.Height);
            w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
            b.Save(Path.Combine(dir, "recipe.png"));
        }
        using (var w = new ClientTranslateWindow("PW 155 BR", element, "PWI", element, false, [new("Itens, monstros e NPCs (elements.data)", "Nomes e textos pelo ID.", true, true), new("Corrigir 7 nomes de instâncias", "a05: Toca dos Lobos → Caverna de Fogo, a15: 楼兰古都 → Trincheira Apodrecida", true, true), new("configs.pck: 4.210 textos", "Descrições de itens e habilidades.", false, true)]) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
        {
            w.Show(); Application.DoEvents();
            using var b = new Bitmap(w.Width, w.Height);
            w.DrawToBitmap(b, new Rectangle(0, 0, w.Width, w.Height));
            b.Save(Path.Combine(dir, "translate.png"));
        }
    }

    private Item? RecipeOf(int id) => _store.Current?.FindListByName("RECIPE_ESSENCE")?.Items.FirstOrDefault(i => i.Id == id.ToString());

    private int? PickRecipe(IWin32Window owner)
    {
        if (_store.Current?.FindListByName("RECIPE_ESSENCE") is not { } list) return null;
        int Product(Item r) => r.Fields.TryGetValue("targets_1_id_to_make", out var v) && int.TryParse(v.Value, out var n) ? n : 0;
        var map = new Dictionary<int, int>();
        var rows = new List<(int Id, string Name, string Block)>();
        foreach (var r in list.Items)
            if (int.TryParse(r.Id, out var id)) { rows.Add((id, TasksFile.SplitColor(r.Name).Text, Pretty(list.Name))); map[id] = Product(r); }
        var host = new QuestHostImpl(this);
        using var w = new ItemSearchWindow(rows, id => map.TryGetValue(id, out var p) ? host.ItemIcon(p) : null, askQuantity: false, tip: id => map.TryGetValue(id, out var p) ? TipImage(p) : null) { Text = Loc.T("is.titleRecipe", "Escolher receita") };
        return w.ShowDialog(owner) == DialogResult.OK ? w.Picked : null;
    }

    private Item BlankRecord(ItemList block)
    {
        Item ni;
        if ((_store.Current?.Source as ElementsLayout)?.Blocks.FirstOrDefault(b => ReferenceEquals(b.List, block)) is { } span)
        {
            ni = CfgElementsReader.ReadRecord(span.Block, new byte[span.RecordSize]);
            ni.Raw = null;
        }
        else
        {
            ni = new Item();
            if (block.Items.FirstOrDefault() is { } template)
                foreach (var (k, f) in template.Fields) ni.Fields[k] = Field.Of(f.Type, f.Type == FieldType.String ? "" : "0");
        }
        return ni;
    }

    // ---------- ferramenta Teleportes do Mundo 3D: id_to_discover e NPC_TRANSMIT_SERVICE ----------
    private static int NpcNumber(Item i, string key) => i.Fields.TryGetValue(key, out var v) && int.TryParse(v.Value, out var n) ? n : 0;
    private Item? NpcById(int id) => _store.Current?.FindListByName("NPC_ESSENCE")?.Items.FirstOrDefault(i => i.Id == id.ToString());
    private Item? ServiceById(int id) => id <= 0 ? null : _store.Current?.FindListByName("NPC_TRANSMIT_SERVICE")?.Items.FirstOrDefault(i => i.Id == id.ToString());
    private static string CleanName(Item i) => TasksFile.SplitColor(i.Name).Text;

    private List<TeleportLink> TeleportersTo(int point)
    {
        var o = new List<TeleportLink>();
        if (_store.Current is not { } m || m.FindListByName("NPC_ESSENCE") is not { } npcs) return o;
        var stops = new Dictionary<string, TeleportStop>();
        foreach (var s in m.FindListByName("NPC_TRANSMIT_SERVICE")?.Items ?? [])
            if (NpcBuilder.StopsOf(s).FirstOrDefault(t => t.Point == point) is { Point: > 0 } hit) stops[s.Id] = hit;
        foreach (var n in npcs.Items)
            if (stops.TryGetValue(NpcNumber(n, "id_transmit_service").ToString(), out var t)) o.Add(new TeleportLink(int.Parse(n.Id), CleanName(n), t.Fee, t.Level));
        return o;
    }

    private List<(int Id, string Name)> DiscoverersOf(int point) => _store.Current?.FindListByName("NPC_ESSENCE")?.Items
        .Where(n => NpcNumber(n, "id_to_discover") == point).Select(n => (int.Parse(n.Id), CleanName(n))).ToList() ?? [];

    private string? SetDiscover(int npc, int point)
    {
        if (NpcById(npc) is not { } n) return Loc.T("status.openFirst", "Abra um elements.data primeiro.");
        using (_edits.Batch(string.Format(Loc.T("hist.discover", "Ponto {0} em {1}"), point, npc)))
        {
            SetField(n, "id_to_discover", point.ToString(), refresh: false);
            SetField(n, "combined_services", NpcBuilder.WithDiscover(n.Fields["combined_services"].Value, point > 0), refresh: false);
        }
        return null;
    }

    private string? SetStops(int npc, IReadOnlyList<TeleportStop> stops, bool copy)
    {
        if (_store.Current is not { } m || NpcById(npc) is not { } n) return Loc.T("status.openFirst", "Abra um elements.data primeiro.");
        if (stops.Count > NpcBuilder.MaxStops) return string.Format(Loc.T("nb.manyStops", "Mais de {0} destinos."), NpcBuilder.MaxStops);
        using (_edits.Batch(string.Format(Loc.T("hist.stops", "Destinos de {0}"), npc)))
        {
            if (stops.Count == 0) SetField(n, "id_transmit_service", "0", refresh: false);
            else WriteStops(TransmitFor(m, n, copy), stops);
        }
        _refs = new RefIndex(m);
        RefreshView();
        return null;
    }

    /// <summary>Quest do Atlas Geográfico: a primeira da lista do item (TASKDICE). 0 = este cliente não tem.</summary>
    private int AtlasRoot()
    {
        if (_store.Current?.FindListByName("TASKDICE_ESSENCE")?.Items.FirstOrDefault(i => i.Id == TeleportAtlas.AtlasItem.ToString()) is not { } it) return 0;
        int root = NpcNumber(it, "task_lists_1_id");
        return _tasks is { FullSchema: true, CanWrite: true } t && t.Index.Find(root) is QuestNode ? root : 0;
    }

    private string? CreatureNameOrItem(int id) => _refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? TasksFile.SplitColor(h.Item.Name).Text : null;

    /// <summary>Põe ou tira o ponto do Atlas, com Ctrl+Z.</summary>
    private string? SetAtlas(WorldTarget point, bool on)
    {
        if (_tasks is not { } t || AtlasRoot() is not (> 0 and var root)) return Loc.T("qb.noTasks", "Abra a pasta do PW com o tasks.data.");
        var label = string.Format(Loc.T("hist.atlas", "Atlas: ponto {0}"), point.Id);
        var old = TeleportAtlas.Find(t, root, point.Id);
        var group = old?.Parent;
        int at = group?.Children.IndexOf(old!) ?? -1;
        QuestNode? made = null;
        _edits.Custom(label,
            () =>
            {
                if (old is not null) TeleportAtlas.Remove(t, old);
                if (on) made = TeleportAtlas.Add(t, root, point.Name, point.Id);
                MarkTasksDirty(label);
            },
            () =>
            {
                if (made is not null) TeleportAtlas.Remove(t, made);
                if (old is not null) TeleportAtlas.Restore(t, group!, old, at);
                MarkTasksDirty(label);
            });
        return on && made is null ? Loc.T("tp.atlasFail", "Não achei uma subquest do Atlas para copiar.") : null;
    }

    private static string ServiceField(NpcKind kind) => kind switch { NpcKind.Seller => "id_sell_service", NpcKind.Forge => "id_make_service", _ => "id_transmit_service" };
    private static string ServiceBlock(NpcKind kind) => kind switch { NpcKind.Seller => "NPC_SELL_SERVICE", NpcKind.Forge => "NPC_MAKE_SERVICE", _ => "NPC_TRANSMIT_SERVICE" };

    private static readonly NpcKind[] ServiceKinds = [NpcKind.Seller, NpcKind.Forge, NpcKind.Teleporter];
    private static bool Wants(NpcDraft d, NpcKind kind) => kind == NpcKind.Seller ? d.HasShop : kind == NpcKind.Forge ? d.HasMake : d.HasStops;

    private int UsersOf(NpcKind kind, int svc) => svc == 0 ? 0 : _store.Current!.FindListByName("NPC_ESSENCE")!.Items.Count(i => NpcNumber(i, ServiceField(kind)) == svc);

    /// <summary>Maior número de NPCs que dividem um serviço que esta edição muda (0 ou 1 = não há o que perguntar).</summary>
    private int ServiceUsersOf(NpcDraft d)
    {
        if (_store.Current is not { } m || NpcById(d.Editing) is not { } n) return 0;
        int most = 0;
        foreach (var kind in ServiceKinds)
        {
            if (!Wants(d, kind) || m.FindListByName(ServiceBlock(kind)) is not { } block) continue;
            int id = NpcNumber(n, ServiceField(kind));
            if (block.Items.FirstOrDefault(i => i.Id == id.ToString()) is not { } svc) continue;
            if (NpcBuilder.ServiceChanges(m, d, kind, svc, BlankRecord, (_, _) => { }, i => i.ToString()).Count > 0) most = Math.Max(most, UsersOf(kind, id));
        }
        return most;
    }

    /// <summary>Grava o rascunho sobre o NPC que já existe, num passo do Ctrl+Z.</summary>
    private string? UpdateNpc(NpcDraft d, bool copy)
    {
        if (_store.Current is not { } m || NpcById(d.Editing) is not { } n) return Loc.T("status.openFirst", "Abra um elements.data primeiro.");
        void Insert(ItemList block, Item it)
        {
            _edits.AddItem(block, it, string.Format(Loc.T("hist.add", "Novo item {0}"), it.Id));
            _owner[it] = block;
        }
        string NameOf(int id) => _refs?.Resolve(FieldKind.ItemRef, id.ToString()) is { } h ? TasksFile.SplitColor(h.Item.Name).Text : id.ToString();
        using (_edits.Batch(string.Format(Loc.T("hist.editNpc", "Editar NPC {0}"), n.Id)))
        {
            if (TasksFile.SplitColor(n.Name).Text != d.Name.Trim()) { SetField(n, "Name", d.Name.Trim(), refresh: false); n.Name = d.Name.Trim(); }
            foreach (var kind in ServiceKinds)
            {
                if (m.FindListByName(ServiceBlock(kind)) is not { } block) continue;
                string field = ServiceField(kind);
                int id = NpcNumber(n, field);
                if (!Wants(d, kind))
                {
                    // tirar o serviço só desliga este NPC: o registro fica para quem mais usa
                    if (id != 0) SetField(n, field, "0", refresh: false);
                    if (kind == NpcKind.Seller && NpcNumber(n, "id_buy_service") != 0) SetField(n, "id_buy_service", "0", refresh: false);
                    continue;
                }
                var svc = block.Items.FirstOrDefault(i => i.Id == id.ToString());
                bool added = svc is null;
                if (svc is null)
                {
                    svc = NpcBuilder.NewService(m, ServiceBlock(kind), n.Name, BlankRecord);
                    Insert(block, svc);
                    SetField(n, field, svc.Id, refresh: false);
                    if (kind == NpcKind.Seller && NpcNumber(n, "id_buy_service") == 0 && m.FindListByName("NPC_BUY_SERVICE")?.Items.FirstOrDefault() is { } buy) SetField(n, "id_buy_service", buy.Id, refresh: false);
                }
                var changes = NpcBuilder.ServiceChanges(m, d, kind, svc, BlankRecord, Insert, NameOf);
                if (changes.Count == 0) continue;
                if (!added && copy && UsersOf(kind, id) > 1)
                {
                    svc = NpcBuilder.CopyService(m, block, svc);
                    Insert(block, svc);
                    SetField(n, field, svc.Id, refresh: false);
                }
                foreach (var (k, v) in changes) SetField(svc, k, v, refresh: false);
            }
            if (d.HasStops && NpcNumber(n, "id_to_discover") != d.Discover)
            {
                SetField(n, "id_to_discover", d.Discover.ToString(), refresh: false);
                SetField(n, "combined_services", NpcBuilder.WithDiscover(n.Fields["combined_services"].Value, d.Discover > 0), refresh: false);
            }
            foreach (var (item, price) in d.Shop?.Prices ?? [])
                if (_refs?.Resolve(FieldKind.ItemRef, item.ToString()) is { } hit && hit.Item.Fields.ContainsKey("shop_price")) SetField(hit.Item, "shop_price", price.ToString(), refresh: false);
        }
        _refs = new RefIndex(m);
        RefreshView();
        return null;
    }

    private int ServiceUsers(int npc)
    {
        int svc = NpcById(npc) is { } n ? NpcNumber(n, "id_transmit_service") : 0;
        return svc == 0 ? 0 : _store.Current?.FindListByName("NPC_ESSENCE")?.Items.Count(i => NpcNumber(i, "id_transmit_service") == svc) ?? 0;
    }

    /// <summary>Serviço que vai mudar: o do NPC, uma cópia só dele ou um novo se ele não tem. Faz parte do mesmo passo do Ctrl+Z.</summary>
    private Item TransmitFor(Module m, Item npc, bool copy)
    {
        var block = m.FindListByName("NPC_TRANSMIT_SERVICE")!;
        var now = ServiceById(NpcNumber(npc, "id_transmit_service"));
        if (now is not null && !copy) return now;
        var s = now is null ? NpcBuilder.NewService(m, "NPC_TRANSMIT_SERVICE", npc.Name, BlankRecord) : NpcBuilder.CopyService(m, block, now);
        _edits.AddItem(block, s, string.Format(Loc.T("hist.add", "Novo item {0}"), s.Id));
        _owner[s] = block;
        SetField(npc, "id_transmit_service", s.Id, refresh: false);
        return s;
    }

    private void WriteStops(Item svc, IReadOnlyList<TeleportStop> stops)
    {
        foreach (var (k, v) in NpcBuilder.StopFields(svc, stops))
            if (svc.Fields.TryGetValue(k, out var f) && f.Value != v) SetField(svc, k, v, refresh: false);
    }

    private string? SetTransmit(int npc, int point, int fee, int level, bool copy)
    {
        if (_store.Current is not { } m || NpcById(npc) is not { } n) return Loc.T("status.openFirst", "Abra um elements.data primeiro.");
        var stops = ServiceById(NpcNumber(n, "id_transmit_service")) is { } now ? NpcBuilder.StopsOf(now) : [];
        var stop = new TeleportStop(point, fee, level);
        int at = stops.FindIndex(s => s.Point == point);
        if (at >= 0) stops[at] = stop;
        else if (stops.Count >= NpcBuilder.MaxStops) return string.Format(Loc.T("nb.manyStops", "Mais de {0} destinos."), NpcBuilder.MaxStops);
        else stops.Add(stop);
        using (_edits.Batch(string.Format(Loc.T("hist.transmit", "Destino {0} em {1}"), point, npc))) WriteStops(TransmitFor(m, n, copy), stops);
        _refs = new RefIndex(m);
        RefreshView();
        return null;
    }

    private string? RemoveTransmit(int npc, int point, bool copy)
    {
        if (_store.Current is not { } m || NpcById(npc) is not { } n) return Loc.T("status.openFirst", "Abra um elements.data primeiro.");
        if (ServiceById(NpcNumber(n, "id_transmit_service")) is not { } now) return null;
        var stops = NpcBuilder.StopsOf(now).Where(s => s.Point != point).ToList();
        using (_edits.Batch(string.Format(Loc.T("hist.transmitOut", "Tira o destino {0} de {1}"), point, npc)))
        {
            // sem destino o gs cai (num_targets 0): o NPC só deixa de ter o serviço, que continua igual para quem mais usa
            if (stops.Count == 0) SetField(n, "id_transmit_service", "0", refresh: false);
            else WriteStops(TransmitFor(m, n, copy), stops);
        }
        _refs = new RefIndex(m);
        RefreshView();
        return null;
    }

    /// <summary>Ferramenta Criar do Mundo 3D: NPC, loja, fabricação e receitas, cada um com o seu passo no Ctrl+Z do editor.</summary>
    private int? BuildNpc(NpcDraft d)
    {
        if (_store.Current is not { } m) return null;
        int id = NpcBuilder.Build(m, d, BlankRecord, (block, it) =>
        {
            _edits.AddItem(block, it, string.Format(Loc.T("hist.add", "Novo item {0}"), it.Id));
            _owner[it] = block;
        }, n => _refs?.Resolve(FieldKind.ItemRef, n.ToString()) is { } h ? TasksFile.SplitColor(h.Item.Name).Text : n.ToString());
        _refs = new RefIndex(m);
        foreach (var (item, price) in d.Shop?.Prices ?? [])
            if (_refs.Resolve(FieldKind.ItemRef, item.ToString()) is { } hit && hit.Item.Fields.ContainsKey("shop_price")) SetField(hit.Item, "shop_price", price.ToString(), refresh: false);
        RefreshView();
        return id;
    }

    private int? PickCreature(IWin32Window owner) => PickCreature(owner, false);

    private int? PickCreature(IWin32Window owner, bool npcOnly)
    {
        var rows = new List<(int Id, string Name, string Block)>();
        foreach (var name in npcOnly ? new[] { "NPC_ESSENCE" } : new[] { "MONSTER_ESSENCE", "NPC_ESSENCE" })
            if (_store.Current?.FindListByName(name) is { } l)
                foreach (var it in l.Items)
                    if (int.TryParse(it.Id, out var id)) rows.Add((id, TasksFile.SplitColor(it.Name).Text, Pretty(l.Name)));
        using var w = new ItemSearchWindow(rows, CreatureIcon, askQuantity: false, tip: TipImage, recent: RecentIds(), model: CreatureModel, fav: CreatureFavorites()) { Text = Loc.T("is.titleCreature", "Escolher criatura") };
        return w.ShowDialog(owner) == DialogResult.OK ? w.Picked : null;
    }


    private List<Item> PickSource(FieldKind kind, string key = "")
    {
        var lists = _store.Current?.Lists ?? new List<ItemList>();
        // serviço: o bloco certo pelo nome do campo (id_sell_service -> NPC_SELL_SERVICE); se não existir, todos os serviços
        var svcBlock = "NPC_" + key.ToUpperInvariant().Replace("ID_", "");
        IEnumerable<ItemList> src = kind switch
        {
            FieldKind.ServiceRef when lists.Any(l => l.Name == svcBlock) => lists.Where(l => l.Name == svcBlock),
            FieldKind.ServiceRef => lists.Where(l => l.Name.StartsWith("NPC_") && l.Name.EndsWith("_SERVICE")),
            FieldKind.AddonRef => lists.Where(l => l.Name == "EQUIPMENT_ADDON"),
            FieldKind.MonsterRef => lists.Where(l => l.Name == "MONSTER_ESSENCE"),
            FieldKind.RecipeRef => lists.Where(l => l.Name == "RECIPE_ESSENCE"),
            _ => lists.Where(l => l.Name.EndsWith("_ESSENCE") && l.Name is not ("MONSTER_ESSENCE" or "NPC_ESSENCE" or "RECIPE_ESSENCE") && !l.Name.StartsWith("NPC_")),
        };
        return src.SelectMany(l => l.Items).ToList();
    }

    private void SetField(Item item, string key, string value, bool refresh = true)
    {
        _edits.SetField(item, key, Field.Of(item.Fields[key].Type, value), string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(key), item.Id));
        if (_owner.TryGetValue(item, out var ob)) _unusedCache.Remove(ob);
        if (refresh) FillGrid(item);
        _list.Invalidate();
        Status($"'{FieldLabels.Label(key)}' = {value}");
    }

    // ---------- cor do nome e descrição (configs.pck: item_color.txt e item_ext_desc.txt, ItemTexts) ----------
    private readonly ComboBox _nameColor = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, Width = 220, ItemHeight = 20 };
    private readonly TextBox _descEdit = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), AcceptsReturn = true };
    private readonly FlowLayoutPanel _descPalette = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private readonly HashSet<int> _cfgColorIds = new(), _cfgDescIds = new();   // itens com cor/descrição mudada (gravar)
    private bool _cfgDirty, _descBinding;
    private readonly ToolTip _descTips = new();

    // nomes das cores oficiais do nome (GameConfigs.NameColors, índice do item_color.txt)
    private static string NameColorLabel(int i) => i switch
    {
        0 => Loc.T("ncol.0", "0 · Branco (comum)"), 1 => Loc.T("ncol.1", "1 · Azul"), 2 => Loc.T("ncol.2", "2 · Amarelo"),
        3 => Loc.T("ncol.3", "3 · Roxo"), 4 => Loc.T("ncol.4", "4 · Laranja"), 5 => Loc.T("ncol.5", "5 · Branco"),
        6 => Loc.T("ncol.6", "6 · Cinza"), 7 => Loc.T("ncol.7", "7 · Verde-água"), 8 => Loc.T("ncol.8", "8 · Vermelho"),
        _ => Loc.T("ncol.none", "Sem cor definida"),
    };

    private void BuildDescEditor(TabPage page)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = CTreeBg };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        Label L(Func<string> t) { var l = new Label { AutoSize = true, ForeColor = CTreeFg, Margin = new Padding(0, 6, 8, 0) }; Bind(() => l.Text = t()); return l; }
        Button B(Func<string> t, Action a) { var b = new Button { AutoSize = true, Height = 28, Margin = new Padding(0, 2, 6, 2), BackColor = CSurface, ForeColor = CFg, FlatStyle = FlatStyle.Flat }; UiKit.Decorate(b, t()); Bind(() => b.Text = t()); b.Click += (_, _) => a(); return b; }

        // cor do nome (índice do item_color.txt)
        _nameColor.Items.Add(-1); for (int i = 0; i < 9; i++) _nameColor.Items.Add(i);
        _nameColor.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            int idx = (int)_nameColor.Items[e.Index]!;
            var sw = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 3, 22, e.Bounds.Height - 6);
            if (idx >= 0) { using var br = new SolidBrush(GameConfigs.NameColors[idx]); e.Graphics.FillRectangle(br, sw); e.Graphics.DrawRectangle(Pens.Gray, sw); }
            TextRenderer.DrawText(e.Graphics, NameColorLabel(idx), e.Font, new Point(sw.Right + 6, e.Bounds.Y + 2), e.ForeColor);
        };
        _nameColor.SelectedIndexChanged += (_, _) => { if (!_descBinding) ApplyItemTexts(color: true); };
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        top.Controls.Add(L(() => Loc.T("desc.nameColor", "Cor do nome")));
        top.Controls.Add(_nameColor);
        top.Controls.Add(L(() => Loc.T("desc.colorsHint", "Descrição: clique numa cor para pôr no cursor (cores já usadas no jogo)")));

        // botões de baixo: aplicar, sem descrição
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(B(() => Loc.T("desc.apply", "Aplicar descrição"), () => ApplyItemTexts(color: false)));
        actions.Controls.Add(B(() => Loc.T("desc.remove", "Sem descrição"), () => { _descEdit.Clear(); ApplyItemTexts(color: false, remove: true); }));
        actions.Controls.Add(L(() => Loc.T("desc.saveHint", "Grava no configs.pck do cliente ao Salvar (com backup).")));
        _descEdit.BackColor = CSurface; _descEdit.ForeColor = CFg;
        _descEdit.TextChanged += (_, _) => { if (!_descBinding && _current is not null) FillDescription(_current, _descEdit.Text); };

        grid.Controls.Add(top, 0, 0); grid.Controls.Add(_descPalette, 0, 1); grid.Controls.Add(_descEdit, 0, 2);
        grid.Controls.Add(actions, 0, 3); grid.Controls.Add(_descBox, 0, 4);
        page.Controls.Add(grid);
    }

    // cores ^RRGGBB mais usadas nas descrições do jogo (paleta), + "Outra cor…" e quebra de linha
    private void FillDescPalette()
    {
        _descPalette.Controls.Clear();
        var used = _cfgs?.ItemDescs.Values.SelectMany(d => System.Text.RegularExpressions.Regex.Matches(d, "\\^([0-9a-fA-F]{6})").Select(m => m.Groups[1].Value.ToLowerInvariant()))
            .GroupBy(c => c).OrderByDescending(g => g.Count()).Take(16).Select(g => g.Key).ToList() ?? new();
        void Insert(string s) { _descEdit.SelectedText = s; _descEdit.Focus(); }
        foreach (var hex in used)
        {
            var c = Color.FromArgb(Convert.ToInt32(hex, 16) | unchecked((int)0xFF000000));
            var b = new Button { Width = 28, Height = 24, BackColor = c, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 4, 2), Tag = hex };
            _descTips.SetToolTip(b, "^" + hex);
            b.Click += (_, _) => Insert("^" + hex);
            _descPalette.Controls.Add(b);
        }
        var other = new Button { AutoSize = true, Height = 24, Text = Loc.T("desc.otherColor", "Outra cor…"), FlatStyle = FlatStyle.Flat, BackColor = CSurface, ForeColor = CFg, Margin = new Padding(6, 2, 4, 2) };
        other.Click += (_, _) => { using var d = new ColorDialog { FullOpen = true }; if (d.ShowDialog(this) == DialogResult.OK) Insert($"^{d.Color.R:x2}{d.Color.G:x2}{d.Color.B:x2}"); };
        _descPalette.Controls.Add(other);
    }

    // texto do jogo (\r literal) ↔ caixa de edição (quebra de linha de verdade)
    private static string DescToEdit(string s) => s.Replace("\\r", Environment.NewLine);
    private static string EditToDesc(string s) => s.Replace("\r\n", "\\r").Replace("\n", "\\r").Replace("\r", "\\r");

    // item cuja descrição está na caixa: o texto digitado vai para ele ao trocar de item ou ao salvar, mesmo sem
    // clicar em "Aplicar" (antes ficava só na caixa e se perdia; a 61366 saiu sem descrição)
    private Item? _descItem;

    private void CommitDescEdit()
    {
        if (_descBinding || _descItem is not { } it || _cfgs is null || !int.TryParse(it.Id, out var id)) return;
        string? typed = _descEdit.Text.Trim().Length == 0 ? null : EditToDesc(_descEdit.Text.TrimEnd());
        if (typed != (_cfgs.ItemDescs.TryGetValue(id, out var cur) ? cur : null)) ApplyItemTexts(color: false, target: it);
    }

    private void BindItemTexts(Item item)
    {
        if (!ReferenceEquals(item, _descItem)) CommitDescEdit();
        _descItem = item;
        _descBinding = true;
        try
        {
            bool ok = _cfgs is not null && int.TryParse(item.Id, out _);
            _nameColor.Enabled = _descEdit.Enabled = ok;
            int id = ok ? int.Parse(item.Id) : 0;
            int idx = ok && _cfgs!.ItemColors.TryGetValue(id, out var ci) && ci is >= 0 and < 9 ? ci : -1;
            _nameColor.SelectedIndex = idx + 1;
            _descEdit.Text = ok && _cfgs!.ItemDescs.TryGetValue(id, out var d) ? DescToEdit(d) : "";
        }
        finally { _descBinding = false; }
    }

    /// <summary>Passa a cor escolhida ou o texto da caixa para o item atual (um passo do histórico; grava ao Salvar).</summary>
    private void ApplyItemTexts(bool color, bool remove = false, Item? target = null)
    {
        if (_cfgs is null || (target ?? _current) is not { } item || !int.TryParse(item.Id, out var id)) return;
        var cfgs = _cfgs;
        if (color)
        {
            int? now = _nameColor.SelectedIndex <= 0 ? null : _nameColor.SelectedIndex - 1;
            int? was = cfgs.ItemColors.TryGetValue(id, out var w) ? w : null;
            if (now == was) return;
            void Set(int? v) { if (v is { } x) cfgs.ItemColors[id] = x; else cfgs.ItemColors.Remove(id); _cfgColorIds.Add(id); _cfgDirty = true; _list.Invalidate(); if (_current == item) FillDescription(item); }
            _edits.Custom(string.Format(Loc.T("hist.nameColor", "Cor do nome de {0}"), item.Id), () => Set(now), () => Set(was));
        }
        else
        {
            string? now = remove || _descEdit.Text.Trim().Length == 0 ? null : EditToDesc(_descEdit.Text.TrimEnd());
            string? was = cfgs.ItemDescs.TryGetValue(id, out var w) ? w : null;
            if (now == was) return;
            void Set(string? v) { if (v is not null) cfgs.ItemDescs[id] = v; else cfgs.ItemDescs.Remove(id); _cfgDescIds.Add(id); _cfgDirty = true; if (_current == item) { BindItemTexts(item); FillDescription(item); } }
            _edits.Custom(string.Format(Loc.T("hist.itemDesc", "Descrição de {0}"), item.Id), () => Set(now), () => Set(was));
        }
        UpdateSendGlow();
        Status(Loc.T("desc.pending", "Cor/descrição alteradas — salve para gravar no configs.pck."));
    }

    /// <summary>Grava no configs.pck as cores/descrições mudadas (só as linhas desses itens). Nome do arquivo ou null.</summary>
    private string? SaveItemTexts(bool quiet)
    {
        if (!_cfgDirty || _cfgs?.PckPath is not { } pck) return null;
        try
        {
            var colors = _cfgColorIds.ToDictionary(i => i, i => _cfgs.ItemColors.TryGetValue(i, out var v) ? (int?)v : null);
            var descs = _cfgDescIds.ToDictionary(i => i, i => _cfgs.ItemDescs.TryGetValue(i, out var v) ? v : null);
            var bak = ItemTexts.Save(pck, colors, descs);
            AppLog.Info($"salvo {pck} (cor/descrição de {colors.Count + descs.Count} item(ns); backup {Path.GetFileName(bak)})");
            _cfgColorIds.Clear(); _cfgDescIds.Clear(); _cfgDirty = false;
            return Path.GetFileName(pck);
        }
        catch (Exception ex)
        {
            AppLog.Error("salvar cor/descrição", ex);
            if (!quiet) MessageBox.Show(this, ex.Message, Loc.T("desc.saveFail", "Não gravei a cor/descrição"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    // Descrição do jogo com as cores (^RRGGBB) — como aparece no tooltip do cliente. editing = texto da caixa (prévia).
    private void FillDescription(Item item) { BindItemTexts(item); FillDescription(item, null); }

    private void FillDescription(Item item, string? editing)
    {
        _descBox.Clear();
        void Put(string t, Color c, bool bold = false)
        {
            _descBox.SelectionStart = _descBox.TextLength; _descBox.SelectionLength = 0;
            _descBox.SelectionColor = c;
            _descBox.SelectionFont = new Font("Segoe UI", bold ? 12 : 10.5f, bold ? FontStyle.Bold : FontStyle.Regular);
            _descBox.AppendText(t);
        }
        Put((string.IsNullOrEmpty(item.Name) ? Loc.T("block.noName", "(sem nome)") : TasksFile.SplitColor(item.Name).Text) + "\n", NameColor(item), bold: true);
        Put($"ID {item.Id}\n\n", CMuted);
        PutTooltipSummary(item, Put);
        if (editing is not null)
            foreach (var (t, c) in GameConfigs.Runs(EditToDesc(editing), CTreeFg)) Put(t, c);
        else if (_cfgs is not null && int.TryParse(item.Id, out var id) && _cfgs.ItemDescs.TryGetValue(id, out var desc))
            foreach (var (t, c) in GameConfigs.Runs(desc, CTreeFg)) Put(t, c);
        else Put(_cfgs is null ? Loc.T("desc.noConfigs", "(configs.pck não encontrado)") : Loc.T("desc.none", "(este item não tem descrição no configs.pck)"), CMuted);
        _descBox.SelectionStart = 0; _descBox.SelectionLength = 0; _descBox.ScrollToCaret();   // AppendText rola até o fim
    }

    // Resumo no estilo do tooltip do jogo (como o preview do VAE): faixas min-max numa linha, requisitos, preço e atributos possíveis.
    private void PutTooltipSummary(Item item, Action<string, Color, bool> put, int maxPerGroup = 64)
    {
        string? V(string k) => item.Fields.TryGetValue(k, out var f) && !IsEmpty(f.Value) ? f.Value : null;
        var stat = Color.FromArgb(255, 203, 74); var val = CTreeFg; var blue = Color.FromArgb(108, 180, 255);
        int lines = 0;
        void Line(string label, string? value) { if (value is null) return; put(label + ": ", stat, false); put(value + "\n", val, false); lines++; }
        string? Range(string lo, string hi) => (V(lo), V(hi)) switch
        {
            (null, null) => null, (var a, null) => a, (null, var b) => b,
            var (a, b) => a == b ? a : $"{a} - {b}",
        };
        Line(FieldLabels.Label("level"), V("level"));
        if (V("character_combo_id") is { } cc && FieldKinds.ClassNames(cc) is { Length: > 0 } cn) Line(FieldLabels.Label("character_combo_id"), cn);
        Line(Loc.T("tip.physAttack", "Ataque físico"), Range("damage_low", "damage_high_max"));
        Line(Loc.T("tip.magicAttack", "Ataque mágico"), Range("magic_damage_low", "magic_damage_high_max"));
        Line(Loc.T("tip.physDefense", "Defesa física"), Range("defence_low", "defence_high"));
        for (int e = 1; e <= 5; e++) Line(FieldLabels.Label($"magic_defences_{e}"), Range($"magic_defences_{e}_low", $"magic_defences_{e}_high"));
        Line(Loc.T("tip.durability", "Durabilidade"), Range("durability_min", "durability_max"));
        foreach (var k in new[] { "require_level", "require_strength", "require_agility", "require_energy", "require_tili", "require_reputation" })
            Line(FieldLabels.Label(k), V(k));
        Line(FieldLabels.Label("price"), V("price") is { } p && long.TryParse(p, out var pv) ? pv.ToString("N0") : null);
        // monstro (como o tooltip do RAE): vida, dano, defesas por elemento, imunidades, recompensas, precisão/esquiva
        if (item.Fields.ContainsKey("life") && item.Fields.ContainsKey("money_average"))
        {
            string? N(string k) => V(k) is { } x && long.TryParse(x, out var n) ? n.ToString("N0") : V(k);
            Line(FieldLabels.Label("life"), N("life"));
            Line(Loc.T("tip.physAttack", "Ataque físico"), Range("damage_min", "damage_max"));
            Line(Loc.T("tip.magicAttack", "Ataque mágico"), Range("magic_damage_min", "magic_damage_max"));
            Line(FieldLabels.Label("defence"), N("defence"));
            for (int e = 1; e <= 5; e++) Line(FieldLabels.Label($"magic_defences_{e}"), N($"magic_defences_{e}"));
            if (V("immune_type") is { } im && long.TryParse(im, out var imv)) Line(FieldLabels.Label("immune_type"), BitMasks.Describe(BitMasks.Immune, imv));
            foreach (var k in new[] { "exp", "skillpoint", "money_average", "attack", "armor" }) Line(FieldLabels.Label(k), N(k));
            if (_cfgs?.SkillNames is { Count: > 0 } sn)
            {
                var skills = item.Fields.Where(kv => FieldKinds.Of(kv.Key) == FieldKind.SkillRef && !IsEmpty(kv.Value.Value))
                    .Select(kv => int.TryParse(kv.Value.Value, out var sid) && sn.TryGetValue(sid, out var nm) ? nm : null)
                    .Where(x => x is not null).Distinct().Take(4).ToList();
                foreach (var sk in skills) Line(Loc.T("tip.skill", "Habilidade"), sk);
            }
        }
        // atributos possíveis: addons/aleatórios/únicos com o efeito do configs.pck e a chance
        var addons = _cfgs?.Addons;
        var groups = new Dictionary<string, List<string>>();   // título -> linhas, na ordem em que aparecem
        if (addons is not null)
            foreach (var (k, f) in item.Fields)
            {
                if (FieldKinds.Of(k) != FieldKind.AddonRef || IsEmpty(f.Value) || _refs?.Resolve(FieldKind.AddonRef, f.Value) is not { } hit) continue;
                var eff = addons.Describe(hit.Item) ?? $"#{f.Value}";
                var prob = FieldKinds.ProbabilityFor(k, item.Fields);
                var pct = prob is not null && float.TryParse(item.Fields[prob].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pp) && pp > 0 ? $"  ({pp * 100:0.##}%)" : "";
                var title = k.StartsWith("addons_") ? Loc.T("tip.addonsCraft", "Atributos de fabricação")
                    : k.StartsWith("rands_") ? Loc.T("tip.addonsRand", "Atributos aleatórios")
                    : k.StartsWith("uniques_") ? Loc.T("tip.addonsUnique", "Atributos únicos")
                    : k == "levelup_addon" ? Loc.T("tip.refine", "Refino")
                    : Loc.T("tip.addonsOther", "Outros atributos");
                if (!groups.TryGetValue(title, out var list)) groups[title] = list = new();
                list.Add(eff + pct);
            }
        foreach (var (title, list) in groups)
        {
            put($"\n{title} ({list.Count})\n", stat, true);
            foreach (var p2 in list.Take(maxPerGroup)) put("  " + p2 + "\n", blue, false);
            if (list.Count > maxPerGroup) put("  …\n", blue, false);
            lines++;
        }
        if (lines > 0) put("\n", val, false);
    }

    private bool FindAllBlocks => _findScope.SelectedIndex == 1;

    // Campos do combo: os do bloco atual, ou todos os nomes de campo de todos os blocos; "(qualquer campo)" no topo
    private void FillFindFields(Item item, bool force = false)
    {
        object tag = FindAllBlocks ? "all" : (object?)_curBlock ?? "none";
        if (!force && Equals(_findField.Tag, tag) && _findField.Items.Count > 0) return;
        var keys = FindAllBlocks
            ? (_store.Current?.Lists ?? new()).Where(l => l.Items.Count > 0).SelectMany(l => l.Items[0].Fields.Keys).Distinct().OrderBy(k => FieldLabels.Label(k)).ToList()
            : item.Fields.Keys.ToList();
        var keep = (_findField.SelectedItem as FindFieldOpt)?.Key;
        _findField.BeginUpdate(); _findField.Items.Clear();
        _findField.Items.Add(new FindFieldOpt(null, Loc.T("find.anyField", "(qualquer campo)")));
        foreach (var k in keys) _findField.Items.Add(new FindFieldOpt(k, $"{FieldLabels.Label(k)}  ·  {k}"));
        _findField.EndUpdate();
        _findField.Tag = tag;
        var want = keep ?? keys.FirstOrDefault(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase));
        _findField.SelectedItem = _findField.Items.Cast<FindFieldOpt>().FirstOrDefault(o => o.Key == want) ?? _findField.Items[0];
    }

    private List<SearchHit> FindSelectedHits() =>
        (_findList.SelectedItems.Count > 0 ? _findList.SelectedItems.Cast<ListViewItem>() : _findList.Items.Cast<ListViewItem>())
        .Select(li => li.Tag).OfType<SearchHit>().ToList();

    private void RunFind()
    {
        if (_findField.SelectedItem is not FindFieldOpt fo || _findOp.SelectedIndex < 0) return;
        var block = _curBlock ?? CurrentItemBlock;
        IEnumerable<ItemList> blocks = FindAllBlocks ? _store.Current?.Lists ?? new() : block is null ? Array.Empty<ItemList>() : new[] { block };
        var op = FindOps[_findOp.SelectedIndex].Op;
        const int max = 5000;
        var hits = AdvancedSearch.Run(blocks, fo.Key, op, _findValue.Text.Trim(), _findValue2.Text.Trim(), max);
        _findList.BeginUpdate();
        if (!_findKeep.Checked) _findList.Items.Clear();
        var already = _findKeep.Checked ? _findList.Items.Cast<ListViewItem>().Select(li => li.Tag).OfType<SearchHit>().Select(h => (h.Item, h.Field)).ToHashSet() : new();
        foreach (var h in hits)
            if (already.Add((h.Item, h.Field)))
                _findList.Items.Add(new ListViewItem(new[] { Pretty(h.Block.Name), h.Item.Id, TasksFile.SplitColor(h.Item.Name).Text, FieldLabels.Label(h.Field), h.Value }, IconIndexFor(h.Item)) { Tag = h });
        _findList.EndUpdate();
        _findCount.Text = string.Format(Loc.T("find.count", "{0} resultado(s)"), _findList.Items.Count) + (hits.Count >= max ? " " + string.Format(Loc.T("find.max", "(parou em {0})"), max) : "");
        Status(string.Format(Loc.T("find.status", "Buscar: {0} encontrado(s) — duplo clique abre o item"), hits.Count));
    }

    private void Navigate(int delta)
    {
        int np = _histPos + delta;
        // a 1ª linha de um filtro não entra no histórico: o Voltar sai dela direto para o último item visitado
        if (delta < 0 && _histPos >= 0 && _histPos < _history.Count && _history[_histPos] != _current) np = _histPos;
        while (np >= 0 && np < _history.Count && _history[np] == _current) np += delta;   // o próprio item aberto não conta como passo
        if (np < 0 || np >= _history.Count) return;
        _histPos = np;
        _navigating = true;
        try { GoToItem(_history[np]); } finally { _navigating = false; }
    }

    private static bool IsEmpty(string v) => v is "0" or "" or "0.0" or "-1";

    // Texto da coluna "Vínculo": nome do item/addon/receita apontado, % e classes por extenso.
    private string Describe(Item item, string field, string value)
    {
        var kind = KindOf(item, field);
        switch (kind)
        {
            case FieldKind.Probability:
                return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) && pr > 0 && pr <= 1
                    ? $"{pr * 100:0.###}%" : "";
            case FieldKind.ClassMask: return FieldKinds.ClassNames(value);
            case FieldKind.ProcMask: return BitMasks.Describe(BitMasks.Proc, long.TryParse(value, out var pv) ? pv : 0);
            case FieldKind.ImmuneMask: return BitMasks.Describe(BitMasks.Immune, long.TryParse(value, out var iv) ? iv : 0);
            case FieldKind.FoodMask: return BitMasks.Describe(BitMasks.Food, long.TryParse(value, out var fv) ? fv : 0);
            case FieldKind.Waypoint:
                if (IsEmpty(value)) return "";
                return int.TryParse(value, out var wid) && Waypoints().TryGetValue(wid, out var wp)
                    ? string.Format(Loc.T("grid.waypoint", "{0}  (ponto de teleporte, mapa {1})"), wp.Name, wp.World) : "";
            case FieldKind.ServiceMask: return BitMasks.Describe(BitMasks.Services, long.TryParse(value, out var sv) ? sv & 0xFFFFFFFFL : 0);
            case FieldKind.SkillRef:
                if (IsEmpty(value)) return "";
                return int.TryParse(value, out var sid) && _cfgs?.SkillNames.TryGetValue(sid, out var sname) == true
                    ? $"{TasksFile.SplitColor(sname!).Text}  ({Loc.T("grid.skill", "habilidade")} {sid})" : Loc.T("grid.skill", "habilidade");
            case FieldKind.TaskRef:
                if (IsEmpty(value)) return "";
                return int.TryParse(value, out var qid) && QuestName(qid) is { } qname
                    ? $"→ {qname}  ({Loc.T("grid.quest", "quest")} {qid})" : Loc.T("grid.quest", "quest");   // → vira link para a aba Quests
            default:
                if (kind == FieldKind.Plain)   // código com significado (tipo de combate, habitat...)
                    return FieldValues.Text(_owner.TryGetValue(item, out var vb) ? vb.Name : "", field, value) ?? "";
                if (!FieldKinds.IsReference(kind) || _refs?.Resolve(kind, value) is not { } hit) return "";
                var name = string.IsNullOrEmpty(hit.Item.Name) ? $"#{hit.Item.Id}" : hit.Item.Name;
                if (kind == FieldKind.AddonRef && _cfgs?.Addons?.Describe(hit.Item) is { } eff) return $"→ {eff}   ({name})";
                var prob = FieldKinds.ProbabilityFor(field, item.Fields);
                var pct = prob is not null && float.TryParse(item.Fields[prob].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0
                    ? $"  ·  {p * 100:0.###}%" : "";
                return $"→ {name}  [{Pretty(hit.Block.Name)}]{pct}";
        }
    }

    // Aba Vínculos: quem referencia o item atual (drops, receitas, loja, addons…).
    private void FillLinks(Item item)
    {
        _links.BeginUpdate();
        _links.Items.Clear();
        if (TypeMembers(item) is { } tm)
        {
            // tipo/subtipo: subtipos que aparecem nos itens do tipo e os itens dele (como abrir a pasta)
            foreach (var (sub, n) in tm.Subs)
                _links.Items.Add(new ListViewItem(new[] { Pretty(tm.SubBlock!.Name), sub.Id, sub.Name, Loc.T("links.subtype", "Subtipo"), string.Format(Loc.T("links.items", "{0} itens"), n) }, IconIndexFor(sub)) { Tag = sub });
            foreach (var it in tm.Items.Take(3000))
                _links.Items.Add(new ListViewItem(new[] { Pretty(tm.Essence.Name), it.Id, it.Name, FieldLabels.Label(tm.Field), "" }, IconIndexFor(it)) { Tag = it });
        }
        foreach (var r in _refs?.UsedBy(item) ?? Array.Empty<RefIndex.Ref>())
        {
            string pct = r.Probability is not null && float.TryParse(r.Probability, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0 ? $"{p * 100:0.###}%" : "";
            var li = new ListViewItem(new[] { Pretty(r.Block.Name), r.Item.Id, r.Item.Name, FieldLabels.Label(r.Field), pct }, IconIndexFor(r.Item)) { Tag = r.Item };
            _links.Items.Add(li);
        }
        _links.EndUpdate();
    }

    // ---------- tipos e subtipos (WEAPON_MAJOR_TYPE, WEAPON_SUB_TYPE...) ----------
    // O elements.data não liga o subtipo ao tipo: a ligação vem dos itens (id_major_type + id_sub_type).
    private sealed record TypeInfo(ItemList Essence, string Field, List<Item> Items, ItemList? SubBlock, List<(Item Sub, int Count)> Subs);
    private readonly Dictionary<Item, TypeInfo?> _typeCache = new(ReferenceEqualityComparer.Instance);

    private TypeInfo? TypeMembers(Item item)
    {
        if (_typeCache.TryGetValue(item, out var cached)) return cached;
        TypeInfo? info = null;
        if (_owner.TryGetValue(item, out var block) && _store.Current is { } mod
            && (block.Name.EndsWith("_MAJOR_TYPE") || block.Name.EndsWith("_SUB_TYPE")))
        {
            bool major = block.Name.EndsWith("_MAJOR_TYPE");
            var prefix = block.Name[..^(major ? "_MAJOR_TYPE".Length : "_SUB_TYPE".Length)];
            var field = major ? "id_major_type" : "id_sub_type";
            // bloco dos itens: PREFIX_ESSENCE ou, se não houver, o bloco da família que tem o campo
            var essence = mod.FindListByName(prefix + "_ESSENCE")
                ?? mod.Lists.FirstOrDefault(l => l.Name.StartsWith(prefix + "_") && l != block && !l.Name.EndsWith("_TYPE")
                    && l.Items.FirstOrDefault()?.Fields.ContainsKey(field) == true);
            if (essence is not null)
            {
                var items = essence.Items.Where(i => Fv(i, field) == item.Id).ToList();
                var subBlock = major ? mod.FindListByName(prefix + "_SUB_TYPE") : null;
                var subs = new List<(Item, int)>();
                if (subBlock is not null)
                    foreach (var g in items.GroupBy(i => Fv(i, "id_sub_type")))
                        if (subBlock.FindById(g.Key) is { } sub) subs.Add((sub, g.Count()));
                info = new TypeInfo(essence, field, items, subBlock, subs);
            }
        }
        _typeCache[item] = info;
        return info;
    }

    private int LinkCount(Item item) => (_refs?.UsedBy(item).Count ?? 0) + (TypeMembers(item) is { } t ? t.Items.Count + t.Subs.Count : 0);

    // Duplo clique num tipo/subtipo na lista: abre o bloco dos itens já filtrado por ele.
    private void OpenTypeFilter(Item typeItem)
    {
        if (TypeMembers(typeItem) is not { } tm) return;
        foreach (var o in _blockCombo.Items)
            if (o is BlockEntry be && be.Block == tm.Essence) { _search.Text = ""; _blockCombo.SelectedItem = be; break; }
        if (!_typeCombo.Visible) return;
        bool major = tm.Field == "id_major_type";
        for (int i = 0; i < _typeCombo.Items.Count; i++)
            if (_typeCombo.Items[i] is TypeEntry te && (major ? te.Major == typeItem.Id && te.Sub is null : te.Sub == typeItem.Id))
            { _typeCombo.SelectedIndex = i; break; }
        Status(string.Format(Loc.T("status.typeOpened", "{0}: {1} itens"), TasksFile.SplitColor(typeItem.Name).Text, tm.Items.Count));
    }

    // Navega até um item de qualquer bloco (troca o seletor de bloco se precisar).
    /// <summary>Diagnóstico (--snapshot): abre o item de um bloco pelo ID.</summary>
    /// <summary>Abre o item pelo ID; "*" escolhe o item mais preenchido do bloco (usado pelo --snapshot).</summary>
    internal void GoToId(string block, string id)
    {
        var list = _store.Current?.FindListByName(block);
        var it = id == "*" ? list?.Items.OrderByDescending(x => x.Fields.Values.Count(f => !IsEmpty(f.Value))).FirstOrDefault() : list?.FindById(id);
        if (it is not null) GoToItem(it);
    }

    // busca adiada (250 ms): limpar a busca ao navegar a disparava DEPOIS, abrindo outro item e gravando-o no
    // histórico, e o Voltar precisava de 2 cliques
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 250 };
    private void GoToItem(Item it)
    {
        if (!_owner.TryGetValue(it, out var b)) return;
        // trocar de bloco seleciona a 1ª linha dele: isso não é item visitado (o Voltar parava nele e pedia dois cliques)
        bool was = _navigating;
        _navigating = true;
        try
        {
            if (_curBlock != b)
                foreach (var o in _blockCombo.Items)
                    if (o is BlockEntry be && be.Block == b) { _search.Text = ""; _searchDelay.Stop(); _blockCombo.SelectedItem = be; break; }
            if (_typeCombo.Visible && _typeCombo.SelectedIndex > 0) _typeCombo.SelectedIndex = 0;
            _current = it;
            SelectItem(it);
        }
        finally { _navigating = was; }
        ShowItem(it);
        _detailTabs.SelectedIndex = 0;
    }

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0 || _grid.Rows[row].Tag is not string key || !_current.Fields.ContainsKey(key)) return;
        var cell = _grid.Rows[row].Cells[1];
        var val = cell is DataGridViewCheckBoxCell ? (cell.Value is true ? "1" : "0") : cell.Value?.ToString() ?? "";
        // acima do teto (do tipo gravado ou da regra do jogo): vira o máximo permitido, e a barra de status diz por quê
        string? capWhy = null;
        if (cell is not DataGridViewCheckBoxCell && _owner.TryGetValue(_current, out var capBlock))
        {
            var capped = FieldLimits.Clamp(capBlock.Name, CfgFieldOf(capBlock, key), key, val, out capWhy);
            if (capped != val) { val = capped; BeginInvoke(() => { if (row < _grid.Rows.Count && _grid.Rows[row].Tag as string == key) _grid.Rows[row].Cells[1].Value = capped; }); }
        }
        // várias células de valor selecionadas: o mesmo valor vai para todas do mesmo tipo de campo, num passo só do desfazer
        var type = _current.Fields[key].Type;
        var also = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(c => c.ColumnIndex == 1 && c.RowIndex != row && !c.ReadOnly && c.GetType() == cell.GetType()
                        && _grid.Rows[c.RowIndex].Tag is string k && _current.Fields.TryGetValue(k, out var f) && f.Type == type)
            .Select(c => c.RowIndex).Distinct().ToList();
        using (_edits.Batch(also.Count > 0 ? string.Format(Loc.T("hist.fieldMany", "{0} campos em {1}"), also.Count + 1, _current.Id)
                   : string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(key), _current.Id)))
        {
            _edits.SetField(_current, key, Field.Of(type, val), string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(key), _current.Id));
            foreach (var r in also)
            {
                var k = (string)_grid.Rows[r].Tag!;
                _edits.SetField(_current, k, Field.Of(type, val), string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(k), _current.Id));
                _grid.Rows[r].Cells[1].Value = cell is DataGridViewCheckBoxCell ? val == "1" : val;
            }
        }
        if (also.Count > 0) { Status(string.Format(Loc.T("status.fieldsUpdated", "{0} campos atualizados para {1}"), also.Count + 1, val)); if (_owner.TryGetValue(_current, out var o2)) _unusedCache.Remove(o2); return; }
        if (_owner.TryGetValue(_current, out var ob)) _unusedCache.Remove(ob);
        // a coluna "significado" seguia mostrando o valor antigo (ex.: zerar "Ovo de pet ao capturar" ainda dizia o ovo)
        if (cell is not DataGridViewCheckBoxCell)
        {
            var desc = Describe(_current, key, val);
            BeginInvoke(() =>   // fora do evento da grade (trocar a célula dentro dele é reentrante)
            {
                if (row < _grid.Rows.Count && _grid.Rows[row].Tag as string == key)
                    _grid.Rows[row].Cells[2] = desc.StartsWith("→") ? LinkCell(desc) : new DataGridViewTextBoxCell { Value = desc };
            });
        }
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            _list.Invalidate();
            _header.Text = TasksFile.SplitColor(val).Text;   // o ID já aparece no quadro ao lado
        }
        Status(capWhy is not null ? string.Format(Loc.T("status.fieldCapped", "'{0}' = {1} (limite: {2})"), FieldLabels.Label(key), val, capWhy)
            : string.Format(Loc.T("status.fieldUpdated", "'{0}' atualizado"), FieldLabels.Label(key)));
    }

    // ---------- seções Spawn (npcgen) e Quests (quem usa) ----------
    private readonly CreatureInfoPanel _creaturePanel = new(dark: false) { Dock = DockStyle.Fill };
    private int _questFillToken;

    /// <summary>Tipo de referência das quests para o bloco: M monstro, N NPC, I item, ' ' nenhum.</summary>
    private static char QuestKind(ItemList b) => b.Name switch
    {
        "MONSTER_ESSENCE" => 'M', "NPC_ESSENCE" => 'N',
        _ => IdSpaces.Of(b.Name) == IdSpaces.Space.Essence && b.Name.EndsWith("_ESSENCE") ? 'I' : ' ',
    };

    /// <summary>Pontos do npcgen (todos os mapas do servidor) onde a criatura nasce.</summary>
    private List<SpawnRow> SpawnsOf(string id)
    {
        var rows = new List<SpawnRow>();
        if (_npcgen is null || !int.TryParse(id, out var cid)) return rows;
        foreach (var (map, f) in _npcgen.AllFiles())
            foreach (var s in f.Spawns)
                foreach (var g in s.Creatures)
                    if (g.Id == cid) rows.Add(new SpawnRow(map, s, g));
        return rows;
    }

    private void FillCreaturePanel(Item item, string sel)
    {
        if (!_owner.TryGetValue(item, out var b)) return;
        _creaturePanel.Show(spawns: sel == "Spawn", quests: sel == "Quests");
        if (sel == "Spawn")
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var rows = SpawnsOf(item.Id);
                _creaturePanel.SetSpawns(_npcgen is null ? Loc.T("ci.noServer", "Sem npcgen: abra a pasta do servidor (aba NPCGen).")
                    : string.Format(Loc.T("ci.spawnTitle", "Onde nasce: {0} ponto(s) em {1} mapa(s)"), rows.Count, rows.Select(r => r.Map).Distinct().Count()), rows);
            }
            finally { Cursor = Cursors.Default; }
            return;
        }
        // quests: o índice das referências pode levar segundos na 1ª vez -> segundo plano
        var kind = QuestKind(b);
        if (_tasks is not { } tasks) { _creaturePanel.SetQuests(Loc.T("ci.noTasks", "Sem tasks.data aberto."), Array.Empty<QuestUse>()); return; }
        _creaturePanel.SetQuests(Loc.T("ci.questsLoading", "Procurando quests…"), Array.Empty<QuestUse>(), loading: true);
        int token = ++_questFillToken;
        var npc = kind == 'N' && int.TryParse(item.Id, out var nid) ? NpcQuests(nid) : null;
        Task.Run(() => QuestUsesOf(tasks, kind, item.Id)).ContinueWith(t =>
        {
            if (IsDisposed || token != _questFillToken || t.IsFaulted) return;
            BeginInvoke(() =>
            {
                if (token != _questFillToken) return;
                var uses = t.Result;
                // NPC: também o que o serviço do NPC lista (dar / receber), mesmo sem campo na quest
                if (npc is not null)
                    foreach (var (list, how) in new[] { (npc.Gives, Loc.T("ci.npcGives", "serviço: dá")), (npc.Takes, Loc.T("ci.npcTakes", "serviço: recebe")) })
                        foreach (var q in list.Where(q => q.InService))
                        {
                            int i = uses.FindIndex(u => u.Id == q.Id);
                            if (i >= 0) uses[i] = uses[i] with { How = uses[i].How + ", " + how };
                            else uses.Add(new QuestUse(q.Id, q.Name.Length > 0 ? q.Name : Loc.T("wq.noQuest", "(quest não existe no tasks.data)"), how));
                        }
                _creaturePanel.SetQuests(string.Format(Loc.T("ci.questTitle", "Quests que usam: {0} (duplo clique abre no editor de quests)"), uses.Count), uses);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Quests que apontam para o ID (M/N/I), uma linha por quest com os campos que usam. Fora da tela (lento na 1ª vez).</summary>
    private static List<QuestUse> QuestUsesOf(TasksFile tasks, char kind, string id) =>
        QuestRules.UsersOf(tasks, kind, id).GroupBy(u => u.Quest.Id)
            .Select(g => new QuestUse(g.Key, TasksFile.SplitColor(g.First().Quest.Name).Text, string.Join(", ", g.Select(u => QuestLabels.Row(u.Field)).Distinct())))
            .ToList();

    /// <summary>Muda quantidade/renascer/mortes de um grupo do npcgen (Ctrl+Z no editor de elements desfaz).</summary>
    private void ApplySpawn(SpawnRow r, int amount, int respawn, int min, int died)
    {
        var g = r.Group;
        var old = (g.Amount, g.Respawn, g.RefreshLower, g.DiedTimes);
        void Set((int A, int R, int M, int D) v) { g.Amount = v.A; g.Respawn = v.R; g.RefreshLower = v.M; g.DiedTimes = v.D; _npcgen?.ExternalEdit(r.Map); UpdateSendGlow(); }
        _edits.Custom(string.Format(Loc.T("hist.spawn", "Spawn de {0} em {1}"), g.Id, r.Map), () => Set((amount, respawn, min, died)), () => Set(old));
        Status(string.Format(Loc.T("status.spawnSet", "{0}: {1} · renasce em {2} · npcgen de {3} (Salvar grava)"), g.Id, amount, CreatureInfoPanel.Time(respawn), r.Map));
    }

    private TabPage _tabDesc = null!;

    /// <summary>Editor de interfaces (interfaces.pck do PW aberto) numa janela própria; Voltar/fechar pergunta se há mudança.</summary>
    private ToolStripMenuItem? _miShowUnused;   // Exibir > Mostrar campos sem uso (também nas Preferências)

    private CfgField? CfgFieldOf(ItemList block, string key) =>
        _store.Current?.Source is ElementsLayout lay ? lay.Blocks.FirstOrDefault(s => s.List == block)?.Block.Fields.FirstOrDefault(f => f.Name == key) : null;

    // ---------- menu de contexto (clicar direito) ----------
    private ToolStripMenuItem _miFav = null!;
    // textos padrão (pt) do menu de clique direito; o idioma escolhido vem de lang/*.json (chave ctx.<marca>)
    static readonly Dictionary<string, string> CtxPt = new()
    {
        ["new"] = "Novo item", ["clone"] = "Clonar item", ["delete"] = "Excluir item",
        ["bulkClone"] = "Clonar selecionados", ["bulkDelete"] = "Excluir selecionados", ["bulkChange"] = "Alterar campo nos selecionados…",
        ["copyIds"] = "Copiar IDs selecionados", ["selectAll"] = "Selecionar todos (Ctrl+A)",
    };

    private void BuildContextMenu()
    {
        ToolStripMenuItem Item(string tag, Action run, string key, string pt)
        {
            var mi = new ToolStripMenuItem { Tag = tag };
            mi.Click += (_, _) => run();
            _ctx.Items.Add(mi);
            Bind(() => mi.Text = Loc.T(key, pt));
            return mi;
        }
        Item("new", NewItem, "ctx.new", "Novo item");
        Item("clone", CloneItem, "ctx.clone", "Clonar item");
        Item("delete", RemoveItem, "ctx.delete", "Excluir item");
        Item("changeId", ChangeIdRefactor, "ctx.changeId", "Mudar ID (atualiza referências)…");
        _ctx.Items.Add(new ToolStripSeparator());
        Item("bulkClone", BulkClone, "ctx.bulkClone", "Clonar selecionados");
        Item("bulkDelete", BulkDelete, "ctx.bulkDelete", "Excluir selecionados");
        Item("bulkChange", BulkChange, "ctx.bulkChange", "Alterar campo nos selecionados…");
        Item("copyIds", () => { var ids = SelectedItems().Select(i => i.Id); Clipboard.SetText(string.Join(Environment.NewLine, ids)); Status(Loc.T("status.idsCopied", "IDs copiados")); }, "ctx.copyIds", "Copiar IDs selecionados");
        Item("selectAll", SelectAllItems, "ctx.selectAll", "Selecionar todos (Ctrl+A)");
        _ctx.Items.Add(new ToolStripSeparator());
        _miFav = new ToolStripMenuItem { Tag = "fav" };
        _miFav.Click += (_, _) => ToggleFavorite();
        _ctx.Items.Add(_miFav);
        Bind(() => _miFav.Text = Loc.T("ctx.fav", "Favoritar"));
        _ctx.Opening += (_, e) =>
        {
            bool isItem = _list.SelectedIndices.Count > 0 && _current is not null;
            int nSel = _list.SelectedIndices.Count;
            foreach (ToolStripItem i in _ctx.Items)
            {
                if (i.Tag is not string tag) continue;
                i.Enabled = isItem || tag == "new";
                if (CtxPt.TryGetValue(tag, out var pt))
                {
                    var t = Loc.T("ctx." + tag, pt);
                    i.Text = tag is "bulkClone" or "bulkDelete" or "bulkChange" or "copyIds" && nSel > 1 ? $"{t} ({nSel})" : t;
                }
            }
            if (isItem && CurrentItemBlock is { } cb)
                _miFav.Text = _favorites.Contains(FavKey(cb, _current!)) ? Loc.T("ctx.unfav", "Desfavoritar") : Loc.T("ctx.fav", "Favoritar");
        };
    }

    // ID de item novo/clonado: livre no ESPAÇO de IDs todo (itens, NPCs, monstros… dividem o "essence"), não só no
    // bloco. O gs acha o registro pelo ID em todas as listas: a asa clonada 61334 caía em cima do NPC 61334 e não
    // era criada (PW-KB §S28).
    private long NextCode(ItemList block)
    {
        if (_store.Current is { } m) return IdRefactor.NextFreeId(m, block);
        long max = 0;
        foreach (var it in block.Items) if (long.TryParse(it.Id, out var n) && n > max) max = n;
        return max + 1;
    }

    private static void SetIdName(Item it)
    {
        if (it.Fields.ContainsKey("ID")) it.Fields["ID"] = Field.Of(FieldType.Int, it.Id);
        if (it.Fields.ContainsKey("Name")) it.Fields["Name"] = Field.Of(FieldType.String, it.Name);
    }

    private bool _pickingService;

    // serviço de NPC: escolher um que já existe ou criar um novo só para este NPC
    private void ServiceMenu(Item item, string key)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Loc.T("svc.pick", "Escolher serviço que já existe…"), null, (_, _) =>
        {
            _pickingService = true;
            try { EditFieldSpecial(item, key); } finally { _pickingService = false; }
        });
        menu.Items.Add(Loc.T("svc.new", "Criar serviço novo só para este NPC"), null, (_, _) => NewService(item, key));
        menu.Show(Cursor.Position);
    }

    private void NewService(Item npc, string key)
    {
        var name = "NPC_" + key.ToUpperInvariant().Replace("ID_", "");
        if (_store.Current?.FindListByName(name) is not { } block) { Status(string.Format(Loc.T("svc.noBlock", "Este elements.data não tem o bloco {0}."), name)); return; }
        long code = NextCode(block);
        var ni = BlankRecord(block);
        ni.Id = code.ToString(); ni.Name = TasksFile.SplitColor(npc.Name).Text;
        SetIdName(ni);
        SetField(npc, key, ni.Id, refresh: false);
        InsertNewItem(block, ni);
        Status(string.Format(Loc.T("svc.created", "Serviço {0} criado e ligado a este NPC. Preencha os campos à direita."), code));
    }

    private void InsertNewItem(ItemList block, Item ni)
    {
        _edits.AddItem(block, ni, string.Format(Loc.T("hist.add", "Novo item {0}"), ni.Id));
        _owner[ni] = block;
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = ni;
        RefreshView();
        SelectItem(ni);
        ShowItem(ni);
    }

    private void NewItem()
    {
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null) { Status(Loc.T("status.pickBlock", "Escolha um bloco primeiro")); return; }
        long code = NextCode(block);
        Item ni;
        if ((_store.Current?.Source as ElementsLayout)?.Blocks.FirstOrDefault(b => ReferenceEquals(b.List, block)) is { } span)
        {
            ni = CfgElementsReader.ReadRecord(span.Block, new byte[span.RecordSize]);
            ni.Raw = null;                              // registro novo: gravado inteiro a partir dos campos
            ni.Id = code.ToString(); ni.Name = "Novo item";
        }
        else
        {
            ni = new Item { Id = code.ToString(), Name = "Novo item" };
            if (block.Items.FirstOrDefault() is { } template)
                foreach (var (k, f) in template.Fields)
                    ni.Fields[k] = Field.Of(f.Type, f.Type == FieldType.String ? "" : "0");
        }
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status(string.Format(Loc.T("status.newItem", "Novo item {0} criado em {1} — preencha os campos à direita."), code, Pretty(block.Name)));
    }

    private void CloneItem()
    {
        if (_current is null || CurrentItemBlock is not { } block) { Status(Loc.T("status.pickToClone", "Selecione um item para clonar")); return; }
        long code = NextCode(block);
        var ni = new Item
        {
            Id = code.ToString(),
            Name = _current.Name + " (cópia)",
            Fields = _current.Fields.Clone(),
            Raw = _current.Raw?.ToArray()
        };
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status(string.Format(Loc.T("status.clonedAs", "Item clonado como código {0}."), code));
    }

    private void AddItem() => NewItem();

    private void RemoveItem()
    {
        if (_current is null || CurrentItemBlock is not { } block) { Status(Loc.T("status.pickItem", "Selecione um item")); return; }
        if (MessageBox.Show(string.Format(Loc.T("confirm.deleteOne", "Excluir \"{0}\" (ID {1})?"), _current.Name, _current.Id), Loc.T("ctx.delete", "Excluir item"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _favorites.Remove(FavKey(block, _current)); SaveFavorites();
        _edits.RemoveItem(block, _current, string.Format(Loc.T("hist.deleteOne", "Excluir item {0}"), _current.Id)); _owner.Remove(_current);
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status(Loc.T("status.itemRemoved", "Item removido"));
    }

    // ---------- favoritos ----------
    private static string FavKey(ItemList b, Item it) => $"{b.Name}|{it.Id}";
    private void ToggleFavorite()
    {
        if (_current is null || CurrentItemBlock is not { } block) return;
        var key = FavKey(block, _current);
        if (!_favorites.Remove(key)) _favorites.Add(key);
        SaveFavorites();
        if (_favModeBtn is not null) _favModeBtn.Text = FavModeText();
        RefreshView();
        Status(_favorites.Contains(key) ? Loc.T("status.favAdded", "Adicionado aos favoritos") : Loc.T("status.favRemoved", "Removido dos favoritos"));
    }
    // cada cliente tem o seu arquivo (mesma chave dos recentes); favorites.txt é o formato antigo, de todos os clientes juntos
    private string? _favFile;
    private static string OldFavFile => Path.Combine(SettingsDir, "favorites.txt");
    private void SaveFavorites()
    {
        if (_favFile is null) return;
        try { Directory.CreateDirectory(SettingsDir); File.WriteAllLines(_favFile, _favorites); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Info("favoritos: " + ex.Message); Status(Loc.T("status.favSaveFail", "Não consegui salvar os favoritos.")); }
    }

    /// <summary>Só as chaves (bloco|id) de itens que existem neste elements.data.</summary>
    internal static IEnumerable<string> ExistingFavorites(IEnumerable<string> saved, Module? module) => saved.Where(k =>
    {
        var part = k.Split('|');
        return part.Length == 2 && module?.FindListByName(part[0])?.Items.Any(i => i.Id == part[1]) == true;
    });

    /// <summary>Favoritos do cliente aberto; item que não existe neste elements.data não entra (nem na contagem).</summary>
    private void LoadFavoritesFor()
    {
        if (_recentKey is null) return;   // nenhum cliente aberto ainda
        _favFile = Path.Combine(SettingsDir, _recentKey.Replace("recent-", "favorites-") + ".txt");
        var from = File.Exists(_favFile) ? _favFile : OldFavFile;
        string[] saved = [];
        try { if (File.Exists(from)) saved = File.ReadAllLines(from); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Info("favoritos: " + ex.Message); }
        _favorites.Clear();
        foreach (var k in ExistingFavorites(saved, _store.Current)) _favorites.Add(k);
        AppLog.Info($"favoritos: {_favorites.Count} de {saved.Length} em {Path.GetFileName(from)}");
        if (_favModeBtn is not null) _favModeBtn.Text = FavModeText();
        if (_favMode) RefreshView();
    }

    // ---------- tooltip estilo jogo ----------
    private void OnListHover(object? s, MouseEventArgs e)
    {
        var hit = _list.HitTest(e.Location).Item;
        int idx = hit?.Index ?? -1;
        if (idx == _hoverIdx) return;
        _hoverIdx = idx;
        if (idx < 0 || idx >= _view.Count) { _tip.Hide(_list); return; }
        BuildTip(_view[idx]);
        _tip.Show(" ", _list, e.X + 22, e.Y + 16, 15000);
    }

    static readonly Font TipFont = new("Segoe UI", 9), TipBold = new("Segoe UI Semibold", 9.5f);
    const TextFormatFlags TipFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
    static readonly Color TipText = Color.FromArgb(206, 211, 219), TipGold = Color.FromArgb(232, 182, 78), TipLink = Color.FromArgb(150, 120, 230);

    private void SizeTip(object? s, PopupEventArgs e)
    {
        if (e.AssociatedControl != _list) return;   // os outros controles usam o tooltip comum
        int w = 160, h = 16;
        foreach (var line in _tipRuns)
        {
            int lw = line.Sum(r => TextRenderer.MeasureText(r.Text, r.Bold ? TipBold : TipFont, Size.Empty, TipFlags).Width);
            w = Math.Max(w, lw); h += 18;
        }
        foreach (var d in _tipDrops) w = Math.Max(w, TextRenderer.MeasureText($"{Loc.T("tip.item", "Item:")} {d.Text}  {d.Pct}", TipFont, Size.Empty, TipFlags).Width + 26);
        if (_tipDrops.Count > 0) h += _tipDrops.Count * 20 + 6;
        e.ToolTipSize = new Size(Math.Min(w + 22, 560), h);
    }

    private void DrawTip(object? s, DrawToolTipEventArgs e)
    {
        if (e.AssociatedControl != _list) { e.DrawBackground(); e.DrawBorder(); e.DrawText(); return; }
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(16, 17, 20)), e.Bounds);
        using (var pen = new Pen(Color.FromArgb(70, 74, 82))) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
        int y = 8;
        void Line(List<(string Text, Color Color, bool Bold)> line)
        {
            int x = 10;
            foreach (var (t, c, bold) in line)
            {
                var f = bold ? TipBold : TipFont;
                TextRenderer.DrawText(e.Graphics, t, f, new Point(x, y), c, TipFlags);
                x += TextRenderer.MeasureText(t, f, Size.Empty, TipFlags).Width;
            }
            y += 18;
        }
        // drops entram depois do resumo e antes dos vínculos
        int dropsAt = _tipRuns.FindIndex(l => l.Count > 0 && l[0].Text == "\u0001");
        for (int i = 0; i < _tipRuns.Count; i++)
        {
            if (i == dropsAt)
            {
                y += 3;
                foreach (var d in _tipDrops)
                {
                    TextRenderer.DrawText(e.Graphics, Loc.T("tip.item", "Item:"), TipFont, new Point(10, y + 1), TipText, TipFlags);
                    int x = 46;
                    if (d.Icon is not null) e.Graphics.DrawImage(d.Icon, x, y, 18, 18);
                    x += 22;
                    TextRenderer.DrawText(e.Graphics, d.Text, TipFont, new Point(x, y + 1), d.Color, TipFlags);
                    x += TextRenderer.MeasureText(d.Text, TipFont, Size.Empty, TipFlags).Width + 6;
                    TextRenderer.DrawText(e.Graphics, d.Pct, TipFont, new Point(x, y + 1), TipGold, TipFlags);
                    y += 20;
                }
                y += 3;
                continue;
            }
            Line(_tipRuns[i]);
        }
    }

    /// <summary>Diagnóstico (--snapshot ... tip): desenha o tooltip da lista do item atual num bitmap.</summary>
    internal Bitmap? RenderListTip() => _current is null ? null : RenderTip(_current);

    /// <summary>A mesma dica da lista do editor, pronta para as janelas de itens, loja e fornalha. Com a descrição do jogo no fim.</summary>
    internal Bitmap? TipImage(int id)
    {
        var it = CreatureItem(id)?.Item ?? _refs?.Resolve(FieldKind.ItemRef, id.ToString())?.Item;
        return it is null ? null : RenderTip(it, id);
    }

    private Bitmap RenderTip(Item item, int descId = 0)
    {
        BuildTip(item);
        if (descId > 0 && _cfgs is not null && _cfgs.ItemDescs.TryGetValue(descId, out var desc))
        {
            _tipRuns.Add(new());
            var line = new List<(string Text, Color Color, bool Bold)>(); int width = 0;
            foreach (var (t, c) in GameConfigs.Runs(desc, CTreeFg))
                foreach (var part in System.Text.RegularExpressions.Regex.Split(t, "(?<=[ \\n])"))
                {
                    bool end = part.EndsWith('\n'), wrap = width + part.Length > 58;
                    if (wrap && line.Count > 0) { _tipRuns.Add(line); line = new(); width = 0; }
                    var text = part.TrimEnd('\n', '\r');
                    if (text.Length > 0) { line.Add((text, c, false)); width += text.Length; }
                    if (end) { _tipRuns.Add(line); line = new(); width = 0; }
                }
            if (line.Count > 0) _tipRuns.Add(line);
        }
        var pe = new PopupEventArgs(_list, _list, false, Size.Empty);
        SizeTip(_tip, pe);
        var bmp = new Bitmap(pe.ToolTipSize.Width, pe.ToolTipSize.Height);
        using var g = Graphics.FromImage(bmp);
        DrawTip(_tip, new DrawToolTipEventArgs(g, _list, _list, new Rectangle(Point.Empty, pe.ToolTipSize), " ", Color.Black, Color.White, TipFont));
        return bmp;
    }

    // Nome, resumo do jogo (o mesmo da aba Descrição), drops com ícone e "Vínculos" (quem usa o item), como no RAE/sELedit.
    private void BuildTip(Item it)
    {
        _tipRuns.Clear();
        var cur = new List<(string, Color, bool)>();
        void Put(string text, Color c, bool bold)
        {
            var parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) { if (cur.Count > 0) _tipRuns.Add(cur); cur = new(); }
                if (parts[i].Length > 0) cur.Add((parts[i], c, bold));
            }
        }
        Put((string.IsNullOrEmpty(it.Name) ? $"#{it.Id}" : TasksFile.SplitColor(it.Name).Text) + "  ", NameColor(it), true);
        Put($"ID {it.Id}\n", CMuted, false);
        PutTooltipSummary(it, Put, maxPerGroup: 3);
        if (cur.Count > 0) { _tipRuns.Add(cur); cur = new(); }

        _tipDrops.Clear();
        foreach (var (k, f) in it.Fields)
        {
            if (_tipDrops.Count >= 10) break;
            if (FieldKinds.Of(k) != FieldKind.ItemRef || IsEmpty(f.Value)) continue;
            var prob = FieldKinds.ProbabilityFor(k, it.Fields);
            if (prob is null || _refs?.Resolve(FieldKind.ItemRef, f.Value) is not { } hit) continue;
            if (!float.TryParse(it.Fields[prob].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) || p <= 0) continue;
            var ico = IconIndexFor(hit.Item);
            _tipDrops.Add((ico >= 0 ? _treeIcons.Images[ico] : null, TasksFile.SplitColor(hit.Item.Name).Text, $"{p * 100:0.##}%", NameColor(hit.Item)));
        }
        if (_tipDrops.Count > 0) _tipRuns.Add(new() { ("\u0001", Color.Empty, false) });   // marcador: os drops entram aqui

        // vínculos: quem aponta para este ID (drop de monstro, loja, receita...)
        var used = _refs?.UsedBy(it) ?? Array.Empty<RefIndex.Ref>();
        if (used.Count > 0)
        {
            Put(string.Format(Loc.T("tip.links", "Vínculos ({0}):"), used.Count) + "\n", TipLink, true);
            foreach (var g in used.GroupBy(r => r.Block).Take(4))
            {
                Put("  " + Pretty(g.Key.Name) + $" ({g.Count()})\n", TipGold, false);
                foreach (var r in g.Take(3))
                {
                    var pct = r.Probability is { } pk && r.Item.Fields.TryGetValue(pk, out var pf)
                        && float.TryParse(pf.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pv) && pv > 0 ? $"  {pv * 100:0.##}%" : "";
                    Put($"    {r.Item.Id}  ", CMuted, false);
                    Put(TasksFile.SplitColor(r.Item.Name).Text, NameColor(r.Item), false);
                    Put(pct + "\n", TipGold, false);
                }
                if (g.Count() > 3) Put("    …\n", CMuted, false);
            }
            if (cur.Count > 0) _tipRuns.Add(cur);
        }
    }

    // ---------- recentes: itens abertos, por elements.data (mais novo primeiro) ----------
    private readonly List<(string Block, string Id)> _recentKeys = new();
    private readonly ListView _recentList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, Visible = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly ImageList _recentIcons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private RadioButton? _recentMode, _favModeBtn;
    private bool _favMode;   // lista mostra os favoritos de todos os blocos
    private string FavModeText() => string.Format(Loc.T("list.modeFav", "Favoritos ({0})"), _favorites.Count);
    private string? _recentKey;
    private const int RecentMax = 40;

    private void BuildRecentList()
    {
        _recentList.BackColor = CTreeBg; _recentList.ForeColor = CTreeFg; _recentList.Font = new Font("Segoe UI", 10);
        _recentList.SmallImageList = _recentIcons;
        _recentList.Columns.Add("", 220); _recentList.Columns.Add("", 140);
        Bind(() => { _recentList.Columns[0].Text = Loc.T("list.name", "Nome"); _recentList.Columns[1].Text = Loc.T("list.recentBlock", "Bloco · ID"); });
        _recentList.Resize += (_, _) => _recentList.Columns[0].Width = Math.Max(120, _recentList.ClientSize.Width - _recentList.Columns[1].Width - 2);
        _recentList.HandleCreated += (_, _) => SetWindowTheme(_recentList.Handle, "DarkMode_Explorer", null);
        _recentList.ItemActivate += (_, _) => OpenRecentSelected();
        _recentList.MouseClick += (_, _) => OpenRecentSelected();
    }

    private void OpenRecentSelected()
    {
        if (_recentList.SelectedItems.Count == 1 && _recentList.SelectedItems[0].Tag is Item it) GoToItem(it);
    }

    private Item? ResolveRecent((string Block, string Id) k) => _store.Current?.FindListByName(k.Block)?.FindById(k.Id);

    private void LoadRecent(string elementsPath)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(elementsPath).ToLowerInvariant())))[..10];
        _recentKey = "recent-" + hash;
        _recentKeys.Clear();
        foreach (var line in (LoadSetting(_recentKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.Split('\t') is [var b, var id]) _recentKeys.Add((b.Trim(), id.Trim()));
        if (_recentMode is not null) _recentMode.Text = _recentKeys.Count > 0 ? string.Format(Loc.T("list.modeRecentN", "Recentes ({0})"), _recentKeys.Count) : Loc.T("list.modeRecent", "Recentes");
        if (_recentList.Visible) FillRecent();
    }

    private void RememberRecent(Item item)
    {
        if (_recentKey is null || !_owner.TryGetValue(item, out var b)) return;
        var k = (b.Name, item.Id);
        int at = _recentKeys.IndexOf(k);
        if (at == 0) return;
        if (at > 0) _recentKeys.RemoveAt(at);
        _recentKeys.Insert(0, k);
        if (_recentKeys.Count > RecentMax) _recentKeys.RemoveRange(RecentMax, _recentKeys.Count - RecentMax);
        SaveSetting(_recentKey, string.Join("\n", _recentKeys.Select(x => x.Block + "\t" + x.Id)));
        if (_recentMode is not null) _recentMode.Text = string.Format(Loc.T("list.modeRecentN", "Recentes ({0})"), _recentKeys.Count);
        if (_recentList.Visible) FillRecent();
    }

    private void FillRecent()
    {
        _recentList.BeginUpdate();
        try
        {
            _recentList.Items.Clear(); _recentIcons.Images.Clear();
            foreach (var k in _recentKeys)
            {
                if (ResolveRecent(k) is not { } it) continue;
                int img = -1;
                if (IconFor(it) is { } ic) { _recentIcons.Images.Add(ic); img = _recentIcons.Images.Count - 1; }
                var name = string.IsNullOrEmpty(it.Name) ? Loc.T("block.noName", "(sem nome)") : TasksFile.SplitColor(it.Name).Text;
                var lvi = new ListViewItem(new[] { name, $"{Pretty(k.Block)} · {it.Id}" }, img) { Tag = it, ForeColor = NameColor(it) };
                if (it == _current) lvi.Selected = true;
                _recentList.Items.Add(lvi);
            }
            if (_recentList.Items.Count == 0)
                _recentList.Items.Add(new ListViewItem(Loc.T("list.recentEmpty", "Os itens que você abrir aparecem aqui.")) { ForeColor = CMuted });
        }
        finally { _recentList.EndUpdate(); }
    }

    // ---------- memória da última pasta ----------
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "last.txt");
    private static void SaveLast(string p)
    { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, p); } catch { } }
    private static string? LoadLast()
    { try { return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null; } catch { return null; } }

    // ---------- ícones (atlas nativo do surfaces.pck; pasta extraída = alternativa) ----------
    private void LoadItemIcon(Item item)
    {
        _iconBox.Image?.Dispose(); _iconBox.Image = null;
        _iconBox.Image = IconFor(item);
    }

    // ---------- aba Quests (tasks.data) ----------
    // v93: esquema completo (árvore + campos + textos + diálogos). Outras versões: só ID/nome.
    private TasksFile? _tasks;
    private (TasksFile Src, Dictionary<int, string> Names)? _questNames;

    // Nome da quest pelo ID (quests de topo e subquests), refeito quando o tasks.data muda.
    private string? QuestName(int id)
    {
        if (_tasks is null) return null;
        if (_questNames?.Src != _tasks)
            _questNames = (_tasks, (_tasks.FullSchema ? _tasks.AllNodes().Select(n => (n.Id, n.Name)) : _tasks.Quests.Select(q => (q.Id, q.Name)))
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => TasksFile.SplitColor(g.First().Name).Text));
        return _questNames.Value.Names.TryGetValue(id, out var n) ? n : null;
    }
    private string? _tasksPath;
    private QuestNode? _qCur;
    private readonly TreeView _qTree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, BorderStyle = BorderStyle.None, ItemHeight = 22, ShowLines = false, DrawMode = TreeViewDrawMode.OwnerDrawText };
    private readonly TextBox _qSearch = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _qName = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11) };
    private readonly Label _qInfo = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI Semibold", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly Label _qStatus = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _qGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly TextBox[] _qTexts = Enumerable.Range(0, 4).Select(_ => new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) }).ToArray();
    private readonly TreeView _qTalkTree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
    private readonly TextBox _qTalkText = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
    private object? _qTalkSel;   // TalkProc, TalkWindow ou TalkOption em edição

    private Control BuildQuestView()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = CLine, Panel1MinSize = 300 };
        var left = new Panel { Dock = DockStyle.Fill, BackColor = CTreeBg, Padding = new Padding(8) };
        Bind(() => _qSearch.PlaceholderText = Loc.T("quest.search", "  Pesquisar quest por nome ou ID…"));
        _qSearch.BackColor = CTreeInput; _qSearch.ForeColor = CTreeFg; _qSearch.Font = new Font("Segoe UI", 10);
        _qSearch.TextChanged += (_, _) => RefreshQuests();
        _qTree.BackColor = CTreeBg; _qTree.ForeColor = CTreeFg; _qTree.Font = new Font("Segoe UI", 10);
        _qTree.DrawNode += DrawQuestNode;
        _qTree.BeforeExpand += (_, e) => ExpandQuestNode(e.Node!);
        _qTree.AfterSelect += (_, e) => ShowQuest(e.Node?.Tag);
        _qTree.HandleCreated += (_, _) => SetWindowTheme(_qTree.Handle, "DarkMode_Explorer", null);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = CTreeBg };
        host.Controls.Add(_qTree);
        left.Controls.Add(host);
        left.Controls.Add(_qSearch);
        split.Panel1.Controls.Add(left);

        // direita: cabeçalho, nome, ferramentas, abas
        var right = new Panel { Dock = DockStyle.Fill, BackColor = CSurface };
        _qInfo.BackColor = CSurface2; _qInfo.ForeColor = CFg;
        _qStatus.ForeColor = CMuted; _qStatus.BackColor = CSurface2;
        // comandos da quest numa barra com ícones (como as outras áreas); o nome fica numa linha própria
        var qbar = UiKit.Ribbon();
        UiKit.Cmd(qbar, UiKit.Save, "quest.rSave", "Salvar tasks.data", SaveTasks, "quest.rSaveTip", "Grava as quests (com backup fora do cliente).", primary: true);
        UiKit.Sep(qbar);
        UiKit.Cmd(qbar, UiKit.Copy, "quest.rClone", "Clonar quest", CloneQuest, "quest.rCloneTip", "Copia a quest escolhida com um ID livre.");
        UiKit.Cmd(qbar, UiKit.Delete, "quest.rDelete", "Excluir quest", DeleteQuest, "quest.rDeleteTip", "Tira a quest do tasks.data (Ctrl+Z desfaz).");
        var editRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 3, Padding = new Padding(10, 6, 10, 6), BackColor = CSurface };
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        Button Btn(string t, Action a, bool accent = false)
        {
            var b = new Button { Text = t, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = accent ? CAccent : CSurface2, ForeColor = accent ? Color.White : CFg, Margin = new Padding(4, 0, 0, 0) };
            UiKit.Decorate(b, t, primary: accent);
            b.Click += (_, _) => a(); return b;
        }
        _qName.MaxLength = TasksFile.MaxNameLength;
        _qName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyQuestName(); } };
        var nameLbl = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CMuted };
        var applyName = Btn("", ApplyQuestName, accent: true);
        Bind(() => { nameLbl.Text = Loc.T("quest.nameLbl", "Nome"); applyName.Text = Loc.T("quest.applyName", "Aplicar nome"); });
        editRow.Controls.Add(nameLbl, 0, 0);
        editRow.Controls.Add(_qName, 1, 0);
        editRow.Controls.Add(applyName, 2, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        // Dados: seções numa coluna à esquerda (clique leva até a seção), títulos desenhados na grade
        var tData = new TabPage { BackColor = CSurface };
        StyleQuestGrid();
        tData.Controls.Add(_qGrid);
        // \u00EDcone pela palavra do grupo (o ID do grupo \u00E9 em portugu\u00EAs e n\u00E3o muda com o idioma)
        _qSections.GlyphOf = o => o is not QSection qs ? null : qs.Group.ToLowerInvariant() switch
        {
            var g when g.Contains("b\u00E1sico") => "\uE946", var g when g.Contains("tempo") => "\uE823", var g when g.Contains("subquest") => "\uE8FD",
            var g when g.Contains("npc") => "\uE77B", var g when g.Contains("regi") => "\uE707", var g when g.Contains("requisit") => "\uE72E",
            var g when g.Contains("equipe") => "\uE716", var g when g.Contains("objetivo") => "\uE7C1", var g when g.Contains("pr\u00EAmio") || g.Contains("premio") => "\uE734",
            var g when g.Contains("chave") => "\uE8D7", var g when g.Contains("aceitar") => "\uE73E", var g when g.Contains("torre") || g.Contains("casa") => "\uE80F",
            _ => "\uE8A5",
        };
        tData.Controls.Add(_qSections);
        _qSections.SelectedIndexChanged += (_, _) =>
        {
            if (_qSections.SelectedItem is not QSection qs) return;
            for (int i = 0; i < _qGrid.Rows.Count; i++)
                if (_qGrid.Rows[i].Tag is SectionTag st && st.Group == qs.Group) { _qGrid.FirstDisplayedScrollingRowIndex = i; break; }
        };
        // Textos
        var tText = new TabPage { BackColor = CSurface, Padding = new Padding(8) };
        var textLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = CSurface };
        for (int i = 0; i < 4; i++)
        {
            textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, i == 0 ? 40 : 20));
            var lbl = new Label { Dock = DockStyle.Fill, ForeColor = CMuted, TextAlign = ContentAlignment.BottomLeft };
            int k = i;
            Bind(() => lbl.Text = QuestLabels.Show(QuestNode.TextNames[k]));
            textLayout.Controls.Add(lbl);
            _qTexts[i].Leave += (_, _) => ApplyQuestText(k);
            textLayout.Controls.Add(_qTexts[i]);
        }
        textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var colorsHint = new Label { Dock = DockStyle.Fill, ForeColor = CMuted };
        Bind(() => colorsHint.Text = Loc.T("quest.colorsHint", "Cores: ^RRGGBB antes do texto (ex.: ^ffcc00). Quebra de linha do jogo: \\r. As alterações valem ao sair do campo."));
        textLayout.Controls.Add(colorsHint);
        tText.Controls.Add(textLayout);
        // Diálogos
        var tTalk = new TabPage { BackColor = CSurface };
        Bind(() =>
        {
            tData.Text = Loc.T("quest.tabData", "Dados"); tText.Text = Loc.T("quest.tabTexts", "Textos"); tTalk.Text = Loc.T("quest.tabTalks", "Diálogos");
            if (_qCur is not null) { ApplyTalkText(); ShowQuest(_qTree.SelectedNode?.Tag); }   // nomes de campo no idioma novo
        });
        var talkSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, BackColor = CLine };
        _qTalkTree.BackColor = CSurface; _qTalkTree.ForeColor = CFg; _qTalkTree.Font = new Font("Segoe UI", 9.5f);
        _qTalkTree.AfterSelect += (_, e) => SelectTalk(e.Node?.Tag);
        _qTalkText.Leave += (_, _) => ApplyTalkText();
        talkSplit.Panel1.Controls.Add(_qTalkTree);
        talkSplit.Panel2.Controls.Add(_qTalkText);
        talkSplit.HandleCreated += (_, _) => { try { talkSplit.SplitterDistance = 340; } catch { } };
        tTalk.Controls.Add(talkSplit);
        tabs.TabPages.Add(tData); tabs.TabPages.Add(tText); tabs.TabPages.Add(tTalk);

        right.Controls.Add(tabs);
        right.Controls.Add(new SegmentBar(tabs, pg => pg == tData ? "\uE9D9" : pg == tText ? "\uE70F" : "\uE8BD"));
        right.Controls.Add(editRow);
        right.Controls.Add(_qInfo);
        right.Controls.Add(qbar);
        right.Controls.Add(_qStatus);
        split.Panel2.Controls.Add(right);
        split.SizeChanged += (_, _) => { if (split.Width > 700 && split.Tag is null) { split.SplitterDistance = 430; split.Tag = true; } };
        return split;
    }

    private readonly SectionList _qSections = new() { Width = 210 };
    private sealed record QSection(string Group, string Text) { public override string ToString() => Text; }

    private void StyleQuestGrid()
    {
        _qGrid.BackgroundColor = CSurface; _qGrid.EnableHeadersVisualStyles = false;
        _qGrid.DefaultCellStyle.BackColor = CSurface; _qGrid.DefaultCellStyle.ForeColor = CFg;
        _qGrid.DefaultCellStyle.SelectionBackColor = CSel; _qGrid.DefaultCellStyle.SelectionForeColor = CSelFg;
        _qGrid.GridColor = Color.FromArgb(226, 231, 238); _qGrid.RowTemplate.Height = 24;
        _qGrid.ColumnCount = 3;
        _qGrid.Columns[0].ReadOnly = true; _qGrid.Columns[0].Width = 300; _qGrid.Columns[0].DefaultCellStyle.ForeColor = CMuted;
        _qGrid.Columns[1].Width = 180; _qGrid.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f);
        _qGrid.Columns[2].ReadOnly = true; _qGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _qGrid.Columns[2].DefaultCellStyle.ForeColor = CAccent;
        _qGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _qGrid.Columns[0].FillWeight = 36; _qGrid.Columns[0].MinimumWidth = 160;
        _qGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _qGrid.Columns[1].FillWeight = 24; _qGrid.Columns[1].MinimumWidth = 100;
        _qGrid.Columns[2].FillWeight = 40;
        _qGrid.RowTemplate.Height = 28;
        _qGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 252);
        _qGrid.CellPainting += PaintSection;
        _qGrid.CellEndEdit += (_, e) =>
        {
            if (_qCur is null || e.RowIndex < 0 || _qGrid.Rows[e.RowIndex].Tag is not TaskField f) return;
            var v = _qGrid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "";
            if (!_qCur.Set(f, v)) { _qStatus.Text = string.Format(Loc.T("quest.invalid", "Valor inválido para '{0}'"), QuestLabels.Show(f.Name)); _qGrid.Rows[e.RowIndex].Cells[1].Value = _qCur.Get(f); return; }
            _qGrid.Rows[e.RowIndex].Cells[2].Value = QuestFieldHint(f, _qCur.Get(f));
            MarkTasksDirty(string.Format(Loc.T("quest.fieldChanged", "'{0}' alterado"), QuestLabels.Show(f.Name)));
        };
    }

    // Texto auxiliar: nomes de item/monstro/NPC a partir do elements.data carregado.
    private string QuestFieldHint(TaskField f, string v)
    {
        if (v is "0" or "" || _refs is null) return "";
        if (f.Name.StartsWith("m_") || f.Name.Contains(".m_"))   // chaves do TaskLayout (versões ≠ 93)
        {
            var leaf = f.Name[(f.Name.LastIndexOf('.') + 1)..];
            if (leaf is "m_ulParent" or "m_ulPrevSibling" or "m_ulNextSibling" or "m_ulFirstChild" or "m_ulNewTask" || leaf.Contains("Task") && leaf.EndsWith("Id"))
                return _tasks?.Index.Find(int.TryParse(v, out var tid) ? tid : 0) is QuestNode tq ? "→ " + TasksFile.SplitColor(tq.Name).Text : "";
            FieldKind lk = leaf == "m_ulMonsterTemplId" ? FieldKind.MonsterRef
                : leaf is "m_ulItemTemplId" or "m_ulDropItemId" || leaf.Contains("NPC") && !leaf.Contains("Cnt") ? FieldKind.ItemRef : FieldKind.Plain;
            return lk != FieldKind.Plain && _refs.Resolve(lk, v) is { } lh ? $"→ {lh.Item.Name}  [{Pretty(lh.Block.Name)}]" : "";
        }
        var n = f.Name.ToLowerInvariant();
        FieldKind kind = n.Contains("npc") ? FieldKind.ItemRef : n.Contains("monstro") ? FieldKind.MonsterRef : n.Contains("item") ? FieldKind.ItemRef : FieldKind.Plain;
        // entradas de lista ("Monstro 1: ID", "Prêmio 2.1: ID", "Item dado 3: quantidade"): o sufixo diz o que é
        if (n.Contains(':'))
        {
            var suffix = n[(n.LastIndexOf(':') + 1)..].Trim();
            kind = suffix == "item que cai" ? FieldKind.ItemRef
                : suffix == "id" ? (n.StartsWith("monstro") ? FieldKind.MonsterRef : FieldKind.ItemRef)
                : FieldKind.Plain;
        }
        else if (n.StartsWith("controle de monstros")) kind = FieldKind.Plain;
        if (n == "nova quest") return _tasks?.AllNodes().FirstOrDefault(x => x.Id.ToString() == v) is { } nq ? "→ " + TasksFile.SplitColor(nq.Name).Text : "";
        if (n.StartsWith("quest") || n.Contains("quest anterior") || n.Contains("quest exclusiva") || n is "pai" or "irmão anterior" or "próximo irmão" or "primeiro filho" or "quest conjunta")
            return _tasks?.AllNodes().FirstOrDefault(x => x.Id.ToString() == v) is { } qq ? "→ " + TasksFile.SplitColor(qq.Name).Text : "";
        if (n.Contains("classe") && n.StartsWith("nº") is false && v.Length > 0 && n == "nº de classes") return "";
        return kind != FieldKind.Plain && _refs.Resolve(kind, v) is { } hit ? $"→ {hit.Item.Name}  [{Pretty(hit.Block.Name)}]" : "";
    }

    // tasks.data ao lado do elements.data (data\tasks.data)
    private void OpenTasksDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "tasks.data (*.data)|*.data|" + Loc.T("dlg.allFiles", "Todos") + " (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadTasks(dlg.FileName);
    }

    private bool _tasksDirty;
    // Clonar/excluir quest principal (issue #16). A cópia ganha IDs novos e fica logo depois da original.
    private QuestNode? RootOfSelected()
    {
        var q = _qTree.SelectedNode?.Tag as QuestNode;
        while (q?.Parent is not null) q = q.Parent;
        return q;
    }

    private void CloneQuest()
    {
        if (_tasks is not { FullSchema: true } || RootOfSelected() is not { } src) { _qStatus.Text = Loc.T("quest.pickRoot", "Escolha uma quest (com o tasks.data completo carregado)."); return; }
        var copy = _tasks.CloneRoot(src);
        MarkTasksDirty(string.Format(Loc.T("quest.cloned", "Quest {0} clonada como {1} ({2} subquests)"), src.Id, copy.Id, copy.Descendants().Count()));
        _qSearch.Text = copy.Id.ToString();
        foreach (TreeNode n in _qTree.Nodes) if (n.Tag is QuestNode qn && qn.Id == copy.Id) { _qTree.SelectedNode = n; break; }
    }

    private void DeleteQuest()
    {
        if (_tasks is not { FullSchema: true } || RootOfSelected() is not { } q || DeleteQuestAsk(q.Id) is not { } ask) return;
        if (MessageBox.Show(this, ask, Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (DeleteQuestById(q.Id) is { } err) MessageBox.Show(this, err, Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void MarkTasksDirty(string msg) { _tasksDirty = true; _qStatus.Text = msg + Loc.T("quest.dirty", " — clique em Salvar tasks.data para gravar."); }

    // ---------- restaurar backup (BackupStore) ----------
    /// <summary>Arquivos do jogo aberto que o editor grava (os backups deles aparecem no menu).</summary>
    IEnumerable<string> RestorableFiles() => new[] { _dataPath, _tasksPath, _aiView?.FilePath }.OfType<string>().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    // raízes do jogo aberto e do servidor (pasta acima de element\ / gamed\): os backups de path.sev, npcgen... são do servidor
    IEnumerable<string> BackupRoots()
    {
        if (_dataPath is not null && Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_dataPath))) is { } client) yield return client;
        if (LocalServer.LiveHome is { } live) yield return live;
        if (_chkServer.Text.Length > 0 && Directory.Exists(_chkServer.Text)) yield return _chkServer.Text;
    }

    void FillRestoreMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        // um submenu por tipo de arquivo, cada um com os 5 mais novos (usuário 07/11); cliente e servidor juntos
        var all = BackupStore.Recent(BackupRoots(), 2000).Select(b => (File: b.Original, Path: b.Bak, b.When)).ToList();
        if (all.Count == 0) { menu.DropDownItems.Add(new ToolStripMenuItem(Loc.T("menu.noBackups", "(nenhum backup deste jogo ainda)")) { Enabled = false }); return; }
        // 1º nível: categoria (dados do jogo, mapas, pacotes, interface e textos); 2º: pasta (mapa / pasta do pacote) quando há muitos
        static string Cat(string f)
        {
            var n = Path.GetFileName(f).ToLowerInvariant(); var ext = Path.GetExtension(n);
            if (ext is ".pck" or ".pkx" || n.Contains(".pkx")) return Loc.T("bak.catPacks", "Pacotes do cliente (.pck)");
            if (n is "elements.data" or "tasks.data" or "aipolicy.data" or "npcgen.data" or "gshop.data" or "gshop1.data" or "gshop2.data" or "gshopsev.data" or "gshopsev1.data" or "gshopsev2.data" or "configs.pck" or "extra_drops.sev" or "domain.sev")
                return Loc.T("bak.catData", "Dados do jogo");
            if (ext is ".ecwld" or ".ecbsd" or ".bht" or ".sev" or ".clt" || n == "npcgen.data") return Loc.T("bak.catMaps", "Mapas e cenário");
            return Loc.T("bak.catOther", "Interface, textos e outros");
        }
        // 1º: um Salvar inteiro de uma ferramenta (todos os arquivos juntos)
        var sets = BackupSets.List().Where(s => s.Files.Any(f => RestorableRoots().Any(r => f.Original.StartsWith(r, StringComparison.OrdinalIgnoreCase)))).Take(15).ToList();
        if (sets.Count > 0)
        {
            var tools = new ToolStripMenuItem(Loc.T("bak.catTools", "Ferramentas (um salvar inteiro)")) { ForeColor = CFg };
            foreach (var set in sets)
            {
                string when = set.Started is { } st ? $"{st:dd/MM/yyyy HH:mm}–{set.When:HH:mm}" : $"{set.When:dd/MM/yyyy HH:mm:ss}";
                var it = new ToolStripMenuItem($"{BackupSets.ToolName(set.Tool)} · {set.Map} · {when} · " + string.Format(Loc.T("bak.files", "{0} arquivos"), set.Files.Count))
                    { ForeColor = CFg, ToolTipText = string.Join("\n", set.Files.Select(f => f.Original)) };
                it.Click += (_, _) => RestoreSet(set);
                tools.DropDownItems.Add(it);
            }
            menu.DropDownItems.Add(tools);
        }
        foreach (var cat in all.GroupBy(b => Cat(b.File)).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var catMenu = new ToolStripMenuItem($"{cat.Key}  ({cat.Select(b => b.File).Distinct(StringComparer.OrdinalIgnoreCase).Count()})") { ForeColor = CFg };
            var byName = cat.GroupBy(b => Path.GetFileName(b.File), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
            bool folders = byName.Count > 25;   // muitos arquivos: mais um nível pela pasta
            var parents = new Dictionary<string, ToolStripMenuItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in byName)
            {
                var sub = new ToolStripMenuItem(group.Key) { ForeColor = CFg };
            foreach (var (file, bak, when) in group.OrderByDescending(b => b.When).Take(5))
            {
                var where = Path.GetFileName(Path.GetDirectoryName(file)) is { Length: > 0 } d && d is not ("data" or "config" or "element") ? d + "\\" : "";   // mapa (world\path.sev)
                var it = new ToolStripMenuItem($"{where}{Path.GetFileName(file)}   ·   {when:dd/MM/yyyy HH:mm:ss}") { ForeColor = CFg, ToolTipText = file + "\n← " + bak };
                it.Click += (_, _) => RestoreBackup(bak, file);
                sub.DropDownItems.Add(it);
            }
                if (!folders) { catMenu.DropDownItems.Add(sub); continue; }
                var dir = Path.GetFileName(Path.GetDirectoryName(group.First().File)) is { Length: > 0 } dd ? dd : "—";
                if (!parents.TryGetValue(dir, out var pm)) { pm = new ToolStripMenuItem(dir) { ForeColor = CFg }; parents[dir] = pm; catMenu.DropDownItems.Add(pm); }
                pm.DropDownItems.Add(sub);
            }
            menu.DropDownItems.Add(catMenu);
        }
    }

    /// <summary>Pastas do jogo e do servidor abertos (para mostrar só os conjuntos deles).</summary>
    IEnumerable<string> RestorableRoots() => BackupRoots().Where(r => !string.IsNullOrEmpty(r)).Select(r => Path.GetFullPath(r));

    /// <summary>Fotos da página (--readme-shots): abre o Mundo 3D com os pontos do NPCGen e devolve a janela. null = não abriu.</summary>
    internal WorldView? OpenWorldForShots() => _npcgen?.OpenWorldOverviewHere() == true ? _npcgen.World : null;

    void RestoreSet(BackupSet set)
    {
        string list = string.Join(Environment.NewLine, set.Files.Select(f => "• " + f.Original));
        var ask = (set.Tool == "restore"
                ? string.Format(Loc.T("bak.setAskUndo", "Desfazer a restauração de {0:dd/MM/yyyy HH:mm} (voltar estes arquivos como estavam antes dela)?"), set.When)
                : set.Tool == "world" && set.Started is { } started
                ? string.Format(Loc.T("bak.setAskWorld", "Voltar o mapa {0} como estava ao abrir o Mundo 3D em {1:dd/MM/yyyy HH:mm} (desfaz tudo o que foi salvo nessa vez, até {2:HH:mm})?"), set.Map, started, set.When)
                : string.Format(Loc.T("bak.setAsk", "Voltar estes arquivos como estavam antes do Salvar de {0} ({1:dd/MM/yyyy HH:mm})?"), BackupSets.ToolName(set.Tool), set.When))
            + Environment.NewLine + Environment.NewLine + list + Environment.NewLine + Environment.NewLine
            + Loc.T("bak.setReload", "Feche o jogo antes. O Mundo 3D aberto relê os arquivos sozinho (mudanças não salvas nele se perdem); o servidor precisa receber os arquivos de novo depois.");
        if (MessageBox.Show(this, ask, Loc.T("menu.restore", "&Restaurar backup").Replace("&", ""), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var why = BackupSets.Restore(set);
        // arquivos do servidor que voltaram (fora da pasta do cliente): o servidor ainda roda com os mexidos; acende o
        // "enviar" e oferece mandar agora (usuário 08/11: o terreno voltou no Mundo 3D, mas os monstros andavam no relevo mexido)
        string? client = _dataPath is not null ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_dataPath))) : null;
        var server = why is null ? set.Files.Select(f => f.Original).Where(f => client is null || !Path.GetFullPath(f).StartsWith(Path.GetFullPath(client) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList() : [];
        foreach (var f in server)
        {
            int at = f.Replace('/', '\\').LastIndexOf(@"\config\", StringComparison.OrdinalIgnoreCase);
            ServerNotice.Notify(at >= 0 ? f.Replace('/', '\\')[(at + 8)..] : Path.GetFileName(f));
        }
        string done = why ?? Loc.T("bak.setDone", "Pronto: todos os arquivos voltaram.");
        if (why is null && server.Count > 0)
            SavedDialog.Show(this, done + Environment.NewLine + Loc.T("bak.setSend", "Alguns arquivos são do servidor: envie ao servidor para ele usar a versão restaurada (até lá ele continua com a mexida)."), SaveAndSend);
        else
            MessageBox.Show(this, done, Loc.T("menu.restore", "&Restaurar backup").Replace("&", ""), MessageBoxButtons.OK, why is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    void OpenBackup()
    {
        using var dlg = new OpenFileDialog { Title = Loc.T("menu.openBackupTitle", "Escolha um backup (.gz ou .bak)"), Filter = Loc.T("menu.backupFilter", "Backups") + " (*.gz;*.bak)|*.gz;*.bak|*.*|*.*",
            InitialDirectory = Directory.Exists(BackupStore.Root) ? BackupStore.Root : "" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var name = BackupStore.OriginalName(dlg.FileName) ?? Path.GetFileName(dlg.FileName);
        // vai para o arquivo de mesmo nome do jogo aberto; senão o usuário escolhe onde
        var target = RestorableFiles().FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            using var sd = new SaveFileDialog { Title = Loc.T("menu.restoreWhere", "Restaurar para qual arquivo?"), FileName = name };
            if (sd.ShowDialog(this) != DialogResult.OK) return;
            target = sd.FileName;
        }
        RestoreBackup(dlg.FileName, target);
    }

    void RestoreBackup(string bak, string target)
    {
        var name = Path.GetFileName(target);
        bool isEl = string.Equals(target, _dataPath, StringComparison.OrdinalIgnoreCase), isTasks = string.Equals(target, _tasksPath, StringComparison.OrdinalIgnoreCase);
        bool unsaved = isEl && ElementsDirty || isTasks && _tasksDirty;
        var ask = string.Format(Loc.T("menu.restoreAsk", "Restaurar {0} do backup de {1:dd/MM/yyyy HH:mm:ss}?\n\nO arquivo atual ganha um backup antes (dá para voltar pelo mesmo menu)."), name, File.GetLastWriteTime(bak))
            + (unsaved ? "\n\n" + Loc.T("menu.restoreUnsaved", "As mudanças não salvas deste arquivo serão perdidas.") : "");
        if (MessageBox.Show(this, ask, Loc.T("menu.restore", "&Restaurar backup").Replace("&", ""), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            AtomicFileSave.Write(target, BackupStore.Read(bak));   // guarda o atual no armazém antes
            AppLog.Info($"backup restaurado: {bak} -> {target}");
            BackupSets.NotifyRestored([target]);   // janelas abertas (Mundo 3D) releem do disco
            if (isEl) LoadElements(target);
            else if (isTasks) LoadTasks(target);
            else if (string.Equals(target, _aiView?.FilePath, StringComparison.OrdinalIgnoreCase)) _aiView?.LoadFile(target);
            Status(string.Format(Loc.T("menu.restored", "{0} restaurado (backup de {1:dd/MM HH:mm}). Para o servidor, use Salvar e enviar."), name, File.GetLastWriteTime(bak)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FileSaveException) { Error(Loc.T("menu.restoreFail", "Não consegui restaurar o backup"), ex); }
    }

    private void LoadTasks(string path)
    {
        try
        {
            _tasks = new TasksFile(path);
            _tasksPath = path; _tasksDirty = false;
            // tasks novo (outro arquivo, tradutor, recarga): a base das regras é a deste tasks. Antes ficava a do tasks
            // anterior e quests já quebradas no arquivo original (ex.: 36354 pede o item 51636, que não existe) travavam o Salvar
            if (_store.Current is { } module && _tasks is { } tk) _questBase = Task.Run(() => QuestRules.Check(tk, module).Select(QKey).ToHashSet());
            RefreshQuests();
            _qStatus.Text = TasksStatus(path);
        }
        catch (Exception ex) { _tasks = null; _qStatus.Text = Loc.T("quest.readFail", "Falha ao ler tasks.data: ") + ex.Message; }
    }

    private string TasksStatus(string path) => _tasks is not { } t ? "" : t.FullSchema
        ? string.Format(Loc.T("quest.statusFull", "{0} · versão {1} · {2:N0} quests ({3:N0} com subquests) · edição completa"),
            Path.GetFileName(path), t.Version, t.Roots!.Count, t.AllNodes().Count())
        : string.Format(Loc.T("quest.statusNames", "{0} · versão {1} · {2:N0} quests · só nomes (esquema desta versão ainda não suportado{3})"),
            Path.GetFileName(path), t.Version, t.Quests.Count, t.SchemaError is null ? "" : ": " + t.SchemaError);

    // Link de quest (coluna Vínculo): abre a aba Quests já filtrada e selecionada no ID.
    private void GoToQuest(int id)
    {
        if (_tasks is null || _tabs is null) return;
        _tabs.SelectedTab = _tabQuests;
        _qSearch.Text = id.ToString();   // RefreshQuests filtra pelo ID
        foreach (TreeNode n in _qTree.Nodes)
            if (n.Tag is QuestNode qn && qn.Id == id || n.Tag is QuestEntry qe && qe.Id == id) { _qTree.SelectedNode = n; n.EnsureVisible(); return; }
    }

    private void RefreshQuests()
    {
        if (_tasks is null) return;
        string q = _qSearch.Text.Trim();
        _qTree.BeginUpdate();
        _qTree.Nodes.Clear();
        if (_tasks.FullSchema)
        {
            IEnumerable<QuestNode> src = q.Length == 0
                ? _tasks.Roots!.OrderBy(x => x.Id)
                : _tasks.AllNodes().Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Id.ToString().Contains(q)).OrderBy(x => x.Id).Take(3000);
            foreach (var n in src) _qTree.Nodes.Add(QuestTreeNode(n));
        }
        else
        {
            foreach (var e in _tasks.Quests.Where(x => q.Length == 0 || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Id.ToString().Contains(q)).OrderBy(x => x.Id))
                _qTree.Nodes.Add(new TreeNode(e.Name) { Tag = e });
        }
        _qTree.EndUpdate();
        if (_qTree.SelectedNode is null && _qTree.Nodes.Count > 0) _qTree.SelectedNode = _qTree.Nodes[Math.Min(1, _qTree.Nodes.Count - 1)];
    }

    private static TreeNode QuestTreeNode(QuestNode n)
    {
        var tn = new TreeNode(n.Name) { Tag = n };
        if (n.Children.Count > 0) tn.Nodes.Add(new TreeNode("…"));   // carregado ao expandir
        return tn;
    }

    private void ExpandQuestNode(TreeNode tn)
    {
        if (tn.Tag is not QuestNode n || tn.Nodes.Count != 1 || tn.Nodes[0].Tag is not null) return;
        tn.Nodes.Clear();
        foreach (var c in n.Children) tn.Nodes.Add(QuestTreeNode(c));
    }

    private void DrawQuestNode(object? s, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null) return;
        bool sel = (e.State & TreeNodeStates.Selected) != 0;
        var r = new Rectangle(e.Bounds.X, e.Bounds.Y, _qTree.ClientSize.Width - e.Bounds.X, e.Bounds.Height);
        using (var bg = new SolidBrush(sel ? CListSel : CTreeBg)) e.Graphics.FillRectangle(bg, r);
        int id; string name; bool changed;
        switch (e.Node.Tag)
        {
            case QuestNode q: id = q.Id; name = q.Name; changed = q.Name != q.OriginalName || q.TextsChanged; break;
            case QuestEntry qe: id = qe.Id; name = qe.Name; changed = qe.Changed; break;
            default: TextRenderer.DrawText(e.Graphics, e.Node.Text, _qTree.Font, r, CMuted, TextFormatFlags.VerticalCenter); return;
        }
        var (text, col) = TasksFile.SplitColor(name);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics, id.ToString(), _qTree.Font, new Rectangle(r.X + 2, r.Y, 60, r.Height), sel ? Color.White : CListId, flags);
        var fg = sel ? Color.White : changed ? GGold : col is { } c && c.GetBrightness() > 0.25f ? c : CTreeFg;
        TextRenderer.DrawText(e.Graphics, (changed ? "● " : "") + (text.Length == 0 ? Loc.T("quest.noName", "(sem nome)") : text), _qTree.Font, new Rectangle(r.X + 64, r.Y, r.Width - 66, r.Height), fg, flags);
    }

    private void ShowQuest(object? tag)
    {
        _qCur = tag as QuestNode;
        string name = tag switch { QuestNode q => q.Name, QuestEntry qe => qe.Name, _ => "" };
        int id = tag switch { QuestNode q => q.Id, QuestEntry qe => qe.Id, _ => 0 };
        var (text, col) = TasksFile.SplitColor(name);
        _qInfo.Text = $"{(text.Length == 0 ? Loc.T("quest.noName", "(sem nome)") : text)}     (ID {id})" + (_qCur?.Children.Count > 0 ? "   ·   " + string.Format(Loc.T("quest.subquests", "{0} subquest(s)"), _qCur.Children.Count) : "");
        _qInfo.ForeColor = col is { } c && c.GetBrightness() < 0.85f ? c : CFg;
        _qName.Text = name;

        _qGrid.Rows.Clear();
        foreach (var t in _qTexts) t.Text = "";
        _qTalkTree.Nodes.Clear(); _qTalkText.Text = ""; _qTalkSel = null;
        if (_qCur is null) return;

        // Dados: campos agrupados (como na tela de itens)
        string? group = null;
        var counts = _qCur.Fields.GroupBy(f => f.Group).ToDictionary(g => g.Key ?? "", g => g.Count());
        _qSections.BeginUpdate(); _qSections.Items.Clear();
        foreach (var f in _qCur.Fields)
        {
            if (f.Group != group)
            {
                group = f.Group;
                var title = QuestLabels.Show(group ?? "");
                int hr = _qGrid.Rows.Add("", "", "");
                var hrow = _qGrid.Rows[hr]; hrow.ReadOnly = true; hrow.Height = 36;
                hrow.Tag = new SectionTag(group ?? "", title + "   ·   " + string.Format(Loc.T("grid.fieldsN", "{0} campos"), counts.GetValueOrDefault(group ?? "")));
                _qSections.Items.Add(new QSection(group ?? "", $"{title} ({counts.GetValueOrDefault(group ?? "")})"));
            }
            var v = _qCur.Get(f);
            int r = _qGrid.Rows.Add("  " + QuestLabels.Row(f.Name), v, QuestFieldHint(f, v));
            _qGrid.Rows[r].Tag = f;
            _qGrid.Rows[r].Cells[0].ToolTipText = QuestLabels.Tip(f.Name) + (f.Editable ? "" : "\n" + Loc.T("quest.structural", "Contagem ou condição: muda pelas telas de lista (mudar só o número desalinharia o arquivo)."));
            if (!f.Editable) { _qGrid.Rows[r].ReadOnly = true; _qGrid.Rows[r].Cells[1].Style.ForeColor = CMuted; }
        }
        _qSections.EndUpdate();
        // Textos
        for (int i = 0; i < 4; i++) _qTexts[i].Text = _qCur.Texts[i].Replace("\r", "\r\n");
        // Diálogos
        for (int i = 0; i < 5; i++)
        {
            var tp = _qCur.Talks[i];
            var tn = new TreeNode($"{QuestLabels.Show(QuestNode.TalkNames[i])}  ({tp.Windows.Count})") { Tag = tp };
            foreach (var w in tp.Windows)
            {
                var wn = new TreeNode(TalkWindowText(w)) { Tag = w };
                foreach (var o in w.Options) wn.Nodes.Add(new TreeNode(TalkOptionText(o)) { Tag = o });
                tn.Nodes.Add(wn);
            }
            _qTalkTree.Nodes.Add(tn);
        }
        _qTalkTree.ExpandAll();
        if (_qTalkTree.Nodes.Count > 0) _qTalkTree.Nodes[0].EnsureVisible();
    }

    private static string TalkWindowText(TalkWindow w) => string.Format(Loc.T("quest.window", "Janela {0}: {1}"), w.Id, Trunc(w.Text, 50));
    private static string TalkOptionText(TalkOption o) => string.Format(Loc.T("quest.option", "→ {0}  (vai p/ {1})"), Trunc(o.Text, 50), o.Id);

    private static string Trunc(string s, int n) { s = s.Replace("\r", " ").Replace("\n", " "); return s.Length <= n ? s : s[..n] + "…"; }

    private void ApplyQuestName()
    {
        switch (_qTree.SelectedNode?.Tag)
        {
            case QuestNode q: q.Name = _qName.Text; break;
            case QuestEntry qe: qe.Name = _qName.Text; break;
            default: return;
        }
        _qTree.Invalidate();
        ShowQuest(_qTree.SelectedNode.Tag);
        MarkTasksDirty(Loc.T("quest.nameChanged", "Nome alterado"));
    }

    private void ApplyQuestText(int i)
    {
        if (_qCur is null) return;
        var v = _qTexts[i].Text.Replace("\r\n", "\r");
        if (v == _qCur.Texts[i]) return;
        _qCur.Texts[i] = v; _qCur.TextsChanged = true;
        _qTree.Invalidate();
        MarkTasksDirty(string.Format(Loc.T("quest.textChanged", "{0} alterado"), QuestLabels.Show(QuestNode.TextNames[i])));
    }

    private void SelectTalk(object? tag)
    {
        ApplyTalkText();
        _qTalkSel = tag;
        _qTalkText.ReadOnly = tag is null;
        _qTalkText.Text = tag switch
        {
            TalkProc tp => tp.Text, TalkWindow w => w.Text, TalkOption o => o.Text, _ => "",
        };
        _qTalkText.Text = _qTalkText.Text.Replace("\r", "\r\n");
        _qTalkText.MaxLength = tag is TalkWindow ? 32767 : 63;
    }

    private void ApplyTalkText()
    {
        if (_qCur is null || _qTalkSel is null) return;
        var v = _qTalkText.Text.Replace("\r\n", "\r");
        bool changed = false;
        switch (_qTalkSel)
        {
            case TalkProc tp when tp.Text != v: tp.Text = v; changed = true; break;
            case TalkWindow w when w.Text != v: w.Text = v; changed = true; break;
            case TalkOption o when o.Text != v: o.Text = v; changed = true; break;
        }
        if (!changed) return;
        _qCur.TextsChanged = true;
        if (_qTalkTree.SelectedNode is { } sn)
            sn.Text = _qTalkSel switch { TalkWindow w => TalkWindowText(w), TalkOption o => TalkOptionText(o), _ => sn.Text };
        _qTree.Invalidate();
        MarkTasksDirty(Loc.T("quest.talkChanged", "Diálogo alterado"));
    }

    private void SaveTasks()
    {
        _qGrid.EndEdit();
        if (_tasks is null || _tasksPath is null) { _qStatus.Text = Loc.T("quest.openFirst", "Abra um tasks.data primeiro."); return; }
        if (!_tasks.CanWrite) { _qStatus.Text = Loc.T("quest.readOnly", "Somente leitura: ") + _tasks.ReadOnlyReason; return; }
        ApplyTalkText();
        for (int i = 0; i < 4; i++) ApplyQuestText(i);
        if (!_tasksDirty) { _qStatus.Text = Loc.T("quest.nothing", "Nada alterado."); return; }
        if (MessageBox.Show(string.Format(Loc.T("quest.confirmSave", "Gravar as alterações em:\n{0}\n\nUma cópia de segurança será criada ao lado.\nLembre: o servidor precisa do MESMO tasks.data (aba Verificação)."), _tasksPath),
                "tasks.data", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SaveTasksCore();
    }

    private bool SaveTasksCore()
    {
        if (_tasks is null || _tasksPath is null || !_tasks.CanWrite) return false;
        if (QuestRulesBlock()) return false;
        ApplyTalkText();
        for (int i = 0; i < 4; i++) ApplyQuestText(i);
        try
        {
            var keep = _qCur?.Id;
            var backup = _tasks.Save(_tasksPath);
            LoadTasks(_tasksPath);
            _qStatus.Text = string.Format(Loc.T("quest.saved", "Salvo · backup: {0}"), Path.GetFileName(backup));
            if (keep is { } id) foreach (TreeNode n in _qTree.Nodes) if (n.Tag is QuestNode qn && qn.Id == id) { _qTree.SelectedNode = n; break; }
            return true;
        }
        catch (Exception ex) { Error(Loc.T("quest.saveFail", "Falha ao salvar tasks.data"), ex); return false; }
    }

    // ---------- aba Verificação (par cliente × servidor) ----------
    private readonly TextBox _chkClient = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _chkServer = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _chkList = new() { View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly StatusCard _chkCard = new();   // resumo da Verificação (padrão UiKit: bolinha + título + detalhe)

    /// <summary>Cartão da Verificação antes de verificar (ou depois de trocar de cliente/servidor).</summary>
    private void CheckNotRun() => _chkCard.Set(StatusCard.Kind.Warn, Loc.T("chk.notRun", "Ainda não verificado"),
        Loc.T("chk.notRunDetail", "Clique em Verificar: compara o cliente aberto com o servidor dele no que o login exige igual."));

    private Control BuildCheckView()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = CBg };
        // comandos (padrão UiKit): Verificar em azul, Corrigir e o servidor deste cliente ("Servidor e pastas")
        var rb = UiKit.Ribbon();
        UiKit.Cmd(rb, UiKit.CheckList, "chk.rCheck", "Verificar", RunCheck, "chk.comparesTheVersionsAndTimestamps", "Compara versões e timestamps que o login exige iguais (elements, tasks, loja), cópias por mapa e a criptografia de rede (sem mostrar as chaves).", primary: true);
        UiKit.Cmd(rb, "\uE90F", "chk.rFix", "Corrigir", FixPair, "chk.fixRebuildsOnTheServer", "Corrigir: refaz no servidor, a partir do cliente, o que o login exige igual (elements, tasks, loja). Cada arquivo trocado ganha backup.");
        UiKit.Sep(rb);
        UiKit.Cmd(rb, UiKit.Server, "tb.rServerSetup", "Servidor", OpenServerSetup, "tb.rServerSetupTip", "Servidor e pastas: onde roda o servidor desta versão do jogo, o pacote dele e a máquina do servidor local.");
        // o que é comparado: nome em destaque + caminho em cinza (os caminhos vêm do elements aberto e de "Servidor e pastas")
        _chkClient.Text = LoadSetting("check_client") ?? "";
        _chkServer.Text = LoadSetting("check_server") ?? "";
        var paths = new TableLayoutPanel { Dock = DockStyle.Top, Height = 66, ColumnCount = 2, RowCount = 2, BackColor = CBg, Padding = new Padding(16, 8, 16, 4) };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void Item(int col, Func<string> title, TextBox source, Func<string> empty)
        {
            var t = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 10), ForeColor = CFg, Image = UiKit.Icon(col == 0 ? UiKit.Game : UiKit.Server, CMuted, 16), ImageAlign = ContentAlignment.MiddleLeft };
            var p = new Label { AutoSize = true, ForeColor = CMuted, MaximumSize = new Size(600, 0), Padding = new Padding(22, 2, 0, 0) };
            void Show() => p.Text = source.Text.Length > 0 ? source.Text : empty();
            Bind(() => { t.Text = "     " + title(); Show(); });   // espaços: lugar do ícone (como o título do registro na aba Servidor)
            source.TextChanged += (_, _) => Show();
            paths.Controls.Add(t, col, 0); paths.Controls.Add(p, col, 1);
        }
        Item(0, () => Loc.T("chk.clientElementFolder", "Cliente (pasta element)"), _chkClient, () => Loc.T("ss.noClient", "Nenhum: abra um elements.data primeiro."));
        Item(1, () => Loc.T("chk.serverHomeFolder", "Servidor (pasta home)"), _chkServer, () => Loc.T("chk.noServer", "Nenhum: escolha em \"Servidor e pastas\"."));
        CheckNotRun();

        _chkList.Columns.Add("", 34);
        _chkList.Columns.Add("", 300);
        _chkList.Columns.Add("", 170);
        _chkList.Columns.Add("", 170);
        _chkList.Columns.Add("", 520);
        Bind(() =>
        {
            _chkList.Columns[1].Text = Loc.T("chk.item", "Item");
            _chkList.Columns[2].Text = Loc.T("chk.client", "Cliente");
            _chkList.Columns[3].Text = Loc.T("chk.server", "Servidor");
            _chkList.Columns[4].Text = Loc.T("chk.whatItMeans", "O que significa");
            if (_chkList.Items.Count > 0) RunCheck();   // as linhas do resultado são texto: refaz no idioma novo
        });
        _chkList.BackColor = CSurface; _chkList.ForeColor = CFg;

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 16), BackColor = CBg };
        listHost.Controls.Add(_chkList);
        root.Controls.Add(listHost);
        root.Controls.Add(paths);
        root.Controls.Add(rb);
        root.Controls.Add(_chkCard);
        return root;
    }

    private bool PickFolder(TextBox target, string title)
    {
        using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, SelectedPath = target.Text };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        target.Text = dlg.SelectedPath;
        return true;
    }

    /// <summary>
    /// Regra: um servidor só para o programa todo. Escolher a pasta do servidor (aqui ou no NPCGen) vale para
    /// Verificação, Servidor e NPCGen de uma vez.
    /// </summary>
    private void SetServerFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var home = VersionCheck.ServerHome(dir);
        if (string.Equals(home, _chkServer.Text, StringComparison.OrdinalIgnoreCase) && LoadSetting("check_server") == home) return;
        _chkServer.Text = home;
        SaveSetting("check_server", home);
        _chkList.Items.Clear(); CheckNotRun();   // o resultado era do servidor anterior
        _npcgen?.ServerFolderChanged(home);
    }

    private void RunCheck() => RunCheck(0);

    /// <summary>Cliente aberto (pasta element), o mesmo da aba Servidor.</summary>
    private string CurrentClient() => _chkClient.Text.Length > 0 ? VersionCheck.ClientElement(_chkClient.Text)
        : _dataPath is null ? "" : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "";

    /// <summary>Janela "Servidor e pastas" do cliente aberto; salvar repontua Verificação, Loja, NPCGen e Servidor.</summary>
    private void OpenServerSetup()
    {
        using var w = new ServerSetupWindow(CurrentClient(), CBg, CFg);
        if (w.ShowDialog(this) == DialogResult.OK) _gshop?.GameFolderChanged();
    }

    /// <summary>Servidor de cada versão (um por cliente, ServerPaths): a Verificação, o NPCGen e a aba Servidor passam a ele.</summary>
    private void ApplyServerPaths()
    {
        _serverView?.GameFolderChanged();
        var e = ServerPaths.Get(CurrentClient());
        if (e.Home.Length == 0) return;
        _chkServer.Text = e.Home; SaveSetting("check_server", e.Home);
        if (e.Config.Length > 0) SaveSetting("gshop_server", e.Config);
        _npcgen?.ServerFolderChanged(e.Home);
    }

    /// <summary>Roda a Verificação; devolve (erros, avisos), (0, 0) se faltam as pastas.</summary>
    private (int Err, int Warn) RunCheck(int _)
    {
        if (_chkClient.Text.Length == 0 && _dataPath is not null)
            _chkClient.Text = Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "";
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status(Loc.T("chk.chooseTheClientAndServer", "Escolha as pastas do cliente e do servidor.")); return (0, 0); }
        // aceita element\data, a pasta do jogo, a raiz dos arquivos do servidor... e mostra a pasta que vale
        _chkClient.Text = VersionCheck.ClientElement(_chkClient.Text); _chkServer.Text = VersionCheck.ServerHome(_chkServer.Text);
        SaveSetting("check_client", _chkClient.Text); SaveSetting("check_server", _chkServer.Text);
        _npcgen?.ServerFolderChanged(_chkServer.Text);   // servidor digitado à mão também vale para o NPCGen
        string cl = _chkClient.Text, sv = _chkServer.Text;   // lê o gs do servidor local (200 MB) e os pacotes: fora da tela
        var rows = ReadProgressDialog.Run(this, Loc.T("chk.running", "Verificando cliente e servidor"), _ => VersionCheck.Run(cl, sv), allowCancel: false);
        _chkList.BeginUpdate(); _chkList.Items.Clear();
        foreach (var r in rows)
        {
            var (sym, col) = r.Nivel switch
            {
                CheckLevel.Ok => ("✔", Color.FromArgb(22, 128, 61)),
                CheckLevel.Info => ("ℹ", CMuted),
                CheckLevel.Aviso => ("⚠", Color.FromArgb(180, 110, 0)),
                _ => ("✖", Color.FromArgb(200, 30, 30)),
            };
            var li = new ListViewItem(new[] { sym, r.Item, r.Cliente, r.Servidor, r.Dica }) { ForeColor = col, UseItemStyleForSubItems = true };
            _chkList.Items.Add(li);
        }
        _chkList.EndUpdate();
        int err = rows.Count(r => r.Nivel == CheckLevel.Erro), warn = rows.Count(r => r.Nivel == CheckLevel.Aviso);
        bool nothing = rows.All(r => r.Cliente == VersionCheck.NotFound && r.Servidor == VersionCheck.NotFound);
        var when = string.Format(Loc.T("chk.checkedAt", "Verificado às {0}"), DateTime.Now.ToString("HH:mm:ss"));
        var fixHint = Loc.T("chk.useFix", "Use Corrigir para refazer no servidor o que o login exige igual ao cliente.");
        if (nothing) _chkCard.Set(StatusCard.Kind.Off, Loc.T("chk.filesNotFoundCheckThe", "✖ Não achei os arquivos: confira as pastas (cliente = element do jogo; servidor = pasta que tem gamed/)").TrimStart('✖', ' '), Loc.T("chk.openSetup", "Confira o servidor deste cliente em \"Servidor e pastas\"."));
        else if (err > 0) _chkCard.Set(StatusCard.Kind.Off, string.Format(Loc.T("chk.0ErrorSThatBlock", "✖ {0} erro(s) que impedem o login · {1} aviso(s)"), err, warn).TrimStart('✖', ' '), when + " · " + fixHint);
        else if (warn > 0) _chkCard.Set(StatusCard.Kind.Warn, string.Format(Loc.T("chk.noLoginErrors0Warning", "⚠ Sem erros de login · {0} aviso(s)"), warn).TrimStart('⚠', ' '), when + " · " + fixHint);
        else _chkCard.Set(StatusCard.Kind.On, Loc.T("chk.clientAndServerAreCompatible", "✔ Cliente e servidor compatíveis").TrimStart('✔', ' '), when);
        Status(Loc.T("chk.checkFinished", "Verificação concluída"));
        return (err, warn);
    }

    // Correção automática do par (issue #26): o usuário escolhe o que trocar; o cliente é a referência.
    private void FixPair()
    {
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status(Loc.T("chk.pickBothFolders", "Escolha as pastas do cliente e do servidor.")); return; }
        var fixes = PairFixer.Plan(_chkClient.Text, _chkServer.Text, () => ServerPaths.Get(CurrentClient()) is { Kind: ServerPaths.Kind.Local } e ? e.Package : "");
        if (fixes.Count == 0) { MessageBox.Show(this, Loc.T("fix.none", "Nada para corrigir: o servidor já está igual ao cliente no que o login exige."), Loc.T("fix.title", "Corrigir o par"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var f = new Form
        {
            Text = Loc.T("fix.title", "Corrigir o par"), Width = 760, Height = 420, StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, BackColor = CSurface, ForeColor = CFg, Font = new Font("Segoe UI", 9.5f),
        };
        var info = new Label { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), ForeColor = CMuted,
            Text = Loc.T("fix.info", "O cliente é a referência (é o que os jogadores têm). Marque o que deve ser refeito no servidor. Cada arquivo do servidor trocado é salvo antes como backup (.bak).") };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
        foreach (var x in fixes) list.Items.Add($"{x.Title}  —  {x.Detail}", x.Recommended);
        var ok = new Button { Text = Loc.T("fix.apply", "Corrigir marcados"), Dock = DockStyle.Bottom, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White, DialogResult = DialogResult.OK };
        f.Controls.Add(list); f.Controls.Add(info); f.Controls.Add(ok);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var done = new List<string>(); var failed = new List<string>();
        var marked = Enumerable.Range(0, fixes.Count).Where(list.GetItemChecked).ToList();
        // fora da tela: trocar os programas do servidor local leva minutos
        ReadProgressDialog.Run(this, Loc.T("fix.title", "Corrigir o par"), ctx =>
        {
            for (int k = 0; k < marked.Count; k++)
            {
                var fx = fixes[marked[k]];
                ctx.Report((double)k / marked.Count, fx.Title, string.Format(Loc.T("trc.stepOf", "{0} de {1}"), k + 1, marked.Count), force: true);
                try { fx.Apply(); done.Add(fx.Title); }
                catch (Exception ex) { failed.Add($"{fx.Title}: {ex.Message}"); }
            }
            return done;
        }, allowCancel: false);
        RunCheck();
        MessageBox.Show(this, (done.Count > 0 ? Loc.T("fix.done", "Corrigido:") + "\n• " + string.Join("\n• ", done) : "")
            + (failed.Count > 0 ? "\n\n" + Loc.T("fix.failed", "Falhou:") + "\n• " + string.Join("\n• ", failed) : "")
            + "\n\n" + Loc.T("fix.restart", "Reinicie o servidor para ele ler os arquivos novos."), Loc.T("fix.title", "Corrigir o par"),
            MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // ---------- aba Extrator PCK (nativo: UI/PckView.cs) ----------
    private Control BuildPckView() =>
        _pckView = new PckView(() => _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)));   // element\data\elements.data -> element

    // ---------- settings genéricas ----------
    private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor");
    private static void SaveSetting(string k, string v) { try { Directory.CreateDirectory(SettingsDir); File.WriteAllText(Path.Combine(SettingsDir, k + ".txt"), v); } catch { } }
    private static string? LoadSetting(string k) { try { var p = Path.Combine(SettingsDir, k + ".txt"); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; } catch { return null; } }

    private void Status(string s) => _status.Text = s;
    private void Error(string msg, Exception ex) { AppLog.Error(msg, ex); Status(msg + ": " + ex.Message); MessageBox.Show(ex.Message, msg, MessageBoxButtons.OK, MessageBoxIcon.Error); }
}

internal sealed class LightRenderer : ToolStripProfessionalRenderer
{
    public LightRenderer() : base(new LightColors()) { }
}
internal sealed class LightColors : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Color.FromArgb(242, 245, 249);
    public override Color ToolStripGradientMiddle => Color.FromArgb(242, 245, 249);
    public override Color ToolStripGradientEnd => Color.FromArgb(242, 245, 249);
    public override Color ButtonSelectedHighlight => Color.FromArgb(219, 232, 253);
    public override Color ButtonSelectedGradientBegin => Color.FromArgb(219, 232, 253);
    public override Color ButtonSelectedGradientEnd => Color.FromArgb(219, 232, 253);
    public override Color ButtonSelectedBorder => Color.FromArgb(37, 99, 235);
}
