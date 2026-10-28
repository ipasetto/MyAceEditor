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

    public EditorForm()
    {
        Bind(() => _search.PlaceholderText = Loc.T("search.placeholder", "  Pesquisar item por nome ou ID…"));
        Bind(() => { if (_store.Current is null) _status.Text = Loc.T("status.start", "Abra a pasta do Perfect World para começar."); });
        Text = "MyAceEditor";
        Width = 1320; Height = 840;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        BackColor = CBg; ForeColor = CFg;
        Font = new Font("Segoe UI", 9.5f);

        Loc.Changed += () => { if (IsHandleCreated) BeginInvoke(ApplyLanguage); else ApplyLanguage(); };
        Controls.Add(BuildTabs());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatus());

        Shown += (_, _) =>
        {
            if (_split is not null) _split.SplitterDistance = 430;
            var last = LoadLast();
            if (last is not null && File.Exists(last)) LoadElements(last);
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
        var translateFrom = new ToolStripMenuItem { ForeColor = CFg }; translateFrom.Click += (_, _) => TranslateFromOther();
        tools.DropDownItems.Add(translateFrom);
        Bind(() => translateFrom.Text = Loc.T("menu.translateFrom", "&Traduzir com outro elements.data…"));
        var translateClient = new ToolStripMenuItem { ForeColor = CFg }; translateClient.Click += (_, _) => TranslateFromClient();
        tools.DropDownItems.Add(translateClient);
        Bind(() => translateClient.Text = Loc.T("menu.translateClient", "Traduzir a partir de outro &cliente…"));
        var showUnused = new ToolStripMenuItem { ForeColor = CFg, CheckOnClick = true, Checked = !_hideUnused };
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
        arq.DropDownItems.AddRange(new ToolStripItem[] { openPw, openEl, new ToolStripSeparator(), save, saveAs, convert, autoSave, new ToolStripSeparator(), exportCsv, exit });
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
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, view, tools, aj, lg });
        MainMenuStrip = menu;
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var tb = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = CSurface2, ForeColor = CFg, Renderer = new LightRenderer(), Padding = new Padding(6, 3, 6, 3) };
        ToolStripButton B(Action a, Action<ToolStripButton> text) { var b = new ToolStripButton { ForeColor = CFg, Font = new Font("Segoe UI", 9.5f) }; b.Click += (_, _) => a(); Bind(() => text(b)); return b; }
        tb.Items.Add(B(() => Navigate(-1), b => { b.Text = "◀"; b.ToolTipText = Loc.T("tb.back", "Voltar (item anterior)"); }));
        tb.Items.Add(B(() => Navigate(+1), b => { b.Text = "▶"; b.ToolTipText = Loc.T("tb.forward", "Avançar"); }));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B(OpenPwFolder, b => b.Text = Loc.T("tb.openPw", "Abrir pasta do PW")));
        tb.Items.Add(B(OpenElements, b => b.Text = Loc.T("tb.openElements", "Abrir elements.data")));
        tb.Items.Add(B(SaveCurrent, b => b.Text = Loc.T("tb.save", "Salvar")));
        tb.Items.Add(B(SaveAndSend, b => { b.Text = Loc.T("tb.saveSend", "💾 Salvar e enviar ao servidor"); b.ToolTipText = Loc.T("tb.saveSendTip", "Grava tudo o que mudou e leva para o servidor local (aba Servidor), reiniciando-o"); }));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B(AddItem, b => b.Text = Loc.T("tb.add", "＋ Adicionar")));
        tb.Items.Add(B(RemoveItem, b => b.Text = Loc.T("tb.delete", "🗑 Excluir")));
        tb.Items.Add(B(() => { if (_tabs is not null) _tabs.SelectedIndex = 0; CreateCreatureWizard(this, NpcGenView.Kind.Monster); },
            b => b.Text = Loc.T("tb.newCreature", "✚ Monstro/NPC novo")));
        tb.Items.Add(B(() => { if (_tabs is not null && _tabNpcGen is not null) _tabs.SelectedTab = _tabNpcGen; BeginInvoke(() => _npcgen?.OpenWorld()); },
            b => b.Text = Loc.T("tb.world", "🌐 Mundo 3D")));
        return tb;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(130, 30) };
        tabs.DrawItem += (s, e) =>
        {
            var tp = tabs.TabPages[e.Index]; bool sel = e.Index == tabs.SelectedIndex;
            e.Graphics.FillRectangle(new SolidBrush(sel ? CBg : CSurface2), e.Bounds);
            if (sel) e.Graphics.FillRectangle(new SolidBrush(CAccent), new Rectangle(e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3));
            TextRenderer.DrawText(e.Graphics, tp.Text, new Font("Segoe UI", 10, sel ? FontStyle.Bold : FontStyle.Regular),
                e.Bounds, sel ? CAccent : CMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
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
        // servidor escolhido no NPCGen (pasta gamed\config) vira o servidor do programa todo
        _npcgen.ServerFolderPicked = config => { if (Path.GetDirectoryName(config) is { } gamed) SetServerFolder(gamed); };
        var tAi = new TabPage() { BackColor = CBg };
        Bind(() => tAi.Text = Loc.T("tab.aipolicy", "AI Policy"));
        _aiView = new AiPolicyView(CBg, CFg, CTreeBg, CTreeFg, CAccent);
        tAi.Controls.Add(_aiView);
        tabs.TabPages.Add(tAi);
        var tChk = new TabPage() { BackColor = CBg };
        Bind(() => tChk.Text = Loc.T("tab.verify", "Verificação"));
        tChk.Controls.Add(BuildCheckView());
        tabs.TabPages.Add(tChk);
        var tSrv = new TabPage() { BackColor = CBg };
        _tabServer = tSrv;
        Bind(() => tSrv.Text = Loc.T("tab.server", "Servidor"));
        tSrv.Controls.Add(_serverView = new ServerView(CBg, CFg, CAccent,
            () => _chkClient.Text.Length > 0 ? VersionCheck.ClientElement(_chkClient.Text)
                : _dataPath is null ? "" : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "",
            () => _chkServer.Text, LoadSetting, SaveSetting) { AiPolicyPath = () => _aiView?.FilePath });
        tabs.TabPages.Add(tSrv);
        var tPck = new TabPage() { BackColor = CBg };
        _tabPck = tPck;
        Bind(() => tPck.Text = Loc.T("tab.pck", "Extrator PCK"));
        tPck.Controls.Add(BuildPckView());
        tabs.TabPages.Add(tPck);
        return tabs;
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
        var searchDelay = new System.Windows.Forms.Timer { Interval = 250 };
        searchDelay.Tick += (_, _) =>
        {
            searchDelay.Stop();
            bool had = _search.Focused;
            RefreshView();
            if (had && !_search.Focused) { _search.Focus(); _search.SelectionStart = _search.TextLength; }
        };
        _search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        _searchAll.ForeColor = CTreeFg; _searchAll.BackColor = CTreeBg;
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
        listHost.Controls.Add(_list);
        left.Controls.Add(listHost);
        left.Controls.Add(foot);
        left.Controls.Add(searchRow);
        left.Controls.Add(_typeCombo);
        left.Controls.Add(_blockCombo);
        _favorites.Clear();
        foreach (var k in LoadFavorites()) _favorites.Add(k);
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
        tVal.Controls.Add(_groupTabs);
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
        var tDesc = new TabPage() { BackColor = CTreeBg, Padding = new Padding(12) };
        Bind(() => tDesc.Text = Loc.T("detail.description", "Descrição e cor"));
        _descBox.BackColor = CTreeBg; _descBox.ForeColor = CTreeFg;
        tDesc.Controls.Add(_descBox);
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
        _grid.CellPainting += PaintExtras;
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
        var nv = ProbabilityGroups.Set(d.Item, d.Key, v, _probLocks, k => refOf.TryGetValue(k, out var rk) && !IsEmpty(d.Item.Fields[rk].Value));
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
    private void OpenChanceEditor(Item item, bool drops)
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
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Changes.Count == 0) return;
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
        bool dirty = ElementsDirty || _tasksDirty || _gshop?.HasUnsaved == true || _aiView?.HasUnsaved == true;
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
        _refs = new RefIndex(module);
        _aiView?.SetElements(module);   // nomes de monstros e "quem usa cada política" na aba AI Policy
        _atlasPck = r.AtlasPck; _atlas = r.Atlas;          // ícones: cache novo para este pacote
        _iconIdx.Clear(); _treeIcons.Images.Clear(); _thumbs?.Dispose(); _thumbs = null; _thumbBatch.Clear();
        _cfgs = r.Cfgs;
        _tasks = r.Tasks; _tasksPath = r.TasksPath; _tasksDirty = false;   // sem tasks.data no jogo novo, a aba fica vazia
        _qTree.Nodes.Clear(); _qCur = null; _qGrid.Rows.Clear();
        RefreshQuests();
        _qStatus.Text = r.TasksPath is { } tp ? TasksStatus(tp) : Loc.T("quest.openFirst", "Abra um tasks.data primeiro.");
        BuildTree();
        SaveLast(path);
        _dataPath = path;
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
            _gshop?.GameFolderChanged();
            _aiView?.GameFolderChanged(element);
            _pckView?.GameFolderChanged();
            _chkList.Items.Clear(); _chkSummary.Text = "";   // o resultado era do cliente anterior
        });
        int items = module.Lists.Sum(l => l.Items.Count);
        Status($"{module.Name} · {module.Lists.Count} {Loc.T("status.blocks", "blocos")} · {items:N0} {Loc.T("status.items", "itens")} · {100.0 * r.Position / r.Length:0.0}% · {r.Ms / 1000.0:0.0} s"
            + $" · {r.Cfg}"
            + (_atlas is null ? Loc.T("status.noIcons", " · ícones: surfaces.pck não encontrado") : " · " + string.Format(Loc.T("status.icons", "{0:N0} ícones do jogo"), _atlas.Count))
            + (module.Source is ElementsLayout { CanWrite: false } lay ? " · " + Loc.T("status.readOnly", "somente leitura") + ": " + lay.ReadOnlyReason : "")
            + (r.Warnings.Count > 0 ? " · " + string.Join(" · ", r.Warnings) : ""));
        Text = $"MyAceEditor — {module.Name}{HeaderInfo(path)}";
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
    private void SaveAll(bool quiet)
    {
        _grid.EndEdit(); _qGrid.EndEdit();
        var pending = new List<string>();
        if (ElementsDirty && _dataPath is not null) pending.Add(_dataPath);
        if (_tasksDirty && _tasksPath is not null) pending.Add(_tasksPath);
        if (_gshop?.HasUnsaved == true) pending.Add("gshop / gshopsev");
        if (_npcgen?.HasUnsaved == true) pending.Add("npcgen.data");
        if (_aiView?.HasUnsaved == true) pending.Add(_aiView.FilePath ?? "aipolicy.data");
        if (pending.Count == 0) { if (!quiet) Status(Loc.T("status.nothingToSave", "Nada para salvar.")); return; }
        if (!quiet && MessageBox.Show(string.Format(Loc.T("save.allAsk", "Gravar as alterações (com cópia de segurança ao lado de cada arquivo)?\n\n{0}"), string.Join("\n", pending)),
                Loc.T("save.allTitle", "Salvar tudo"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var done = new List<string>();
        if (ElementsDirty && _dataPath is not null && SaveElementsCore(_dataPath)) done.Add(Path.GetFileName(_dataPath));
        if (_tasksDirty && _tasksPath is not null && SaveTasksCore()) done.Add(Path.GetFileName(_tasksPath));
        if (_gshop is not null) done.AddRange(_gshop.SaveAll(quiet).Select(Path.GetFileName)!);
        if (_npcgen is not null) done.AddRange(_npcgen.SaveAll(quiet).Select(p => Path.GetFileName(Path.GetDirectoryName(p)) + "\\npcgen.data"));
        if (_aiView?.SaveIfDirty() is { } ai) done.Add(Path.GetFileName(ai));
        Status((quiet ? Loc.T("status.autoSaved", "Salvo automaticamente: ") : Loc.T("status.savedAll", "Salvo: ")) + (done.Count == 0 ? "—" : string.Join(", ", done)) + $" ({DateTime.Now:HH:mm})");
    }

    // "Salvar e enviar ao servidor": grava tudo sem perguntar e leva para o servidor local (aba Servidor mostra o andamento)
    private ServerView? _serverView;
    private TabPage? _tabServer;
    private void SaveAndSend()
    {
        if (_gshop?.ServerProblems() is { Count: > 0 } bad)
        {
            if (_tabs is not null && _tabGShop is not null) _tabs.SelectedTab = _tabGShop;
            MessageBox.Show(this, GShopView.ProblemText(bad), Loc.T("tb.saveSend", "💾 Salvar e enviar ao servidor"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SaveAll(quiet: true);
        if (_serverView is null) return;
        if (_tabs is not null && _tabServer is not null) _tabs.SelectedTab = _tabServer;
        _serverView.SendFiles();
    }

    // fechar com mudanças: oferece salvar tudo
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bool dirty = ElementsDirty || _tasksDirty || _gshop?.HasUnsaved == true || _npcgen?.HasUnsaved == true || _aiView?.HasUnsaved == true;
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

    private bool SaveElementsCore(string target)
    {
        if (_store.Current?.Source is not ElementsLayout) return false;
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
            _blockCombo.Items.Add(new BlockEntry(null, $"{Loc.T("block.favorites", "★ Favoritos")} ({_favorites.Count})",
                Dyn: () => $"{Loc.T("block.favorites", "★ Favoritos")} ({_favorites.Count})"));
            // "Todos": itens de todos os blocos numa lista só (pedido do usuário, logo abaixo dos favoritos)
            _blockCombo.Items.Add(new BlockEntry(null, "", All: true,
                Dyn: () => $"{Loc.T("block.all", "◆ Todos os blocos")} ({_store.Current?.Lists.Sum(l => l.Items.Count) ?? 0:N0})"));
            var entries = lists.Select((b, i) => (b, i)).Where(x => x.b.Items.Count > 0).ToList();
            foreach (var cat in CategoryOrder)
            {
                var inCat = entries.Where(x => BlockCategory(x.b.Name) == cat).ToList();
                if (inCat.Count == 0) continue;
                _blockCombo.Items.Add(new BlockEntry(null, $"── {cat} ──", Header: true, Dyn: () => $"── {CategoryName(cat)} ──"));
                foreach (var (b, i) in inCat) _blockCombo.Items.Add(new BlockEntry(b, $"   [{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})",
                Dyn: () => $"   [{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})"));
            }
            var last = LoadSetting("block") ?? "WEAPON_ESSENCE";
            int sel = 1;
            for (int i = 0; i < _blockCombo.Items.Count; i++)
                if (_blockCombo.Items[i] is BlockEntry { Block: { } bb } && bb.Name == last) sel = i;
            _blockCombo.SelectedIndex = Math.Min(sel, _blockCombo.Items.Count - 1);
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
        if (_curBlock is not null) SaveSetting("block", _curBlock.Name);
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
        _typeCombo.Visible = has;
        _typeCombo.EndUpdate();
    }

    // ---------- seletor de blocos (busca + categorias + ícone de cada bloco) ----------
    private void ShowBlockPicker()
    {
        if (_store.Current is null || _blockCombo.Items.Count == 0) return;
        var screen = _blockCombo.PointToScreen(new Point(0, _blockCombo.Height));
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, KeyPreview = true,
            Location = screen, Size = new Size(Math.Max(_blockCombo.Width, 440), Math.Min(620, Screen.FromControl(this).WorkingArea.Bottom - screen.Y - 10)),
            BackColor = CTreeBg, Padding = new Padding(1),
        };
        var search = new TextBox { Dock = DockStyle.Top, BackColor = CTreeInput, ForeColor = CTreeFg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f),
            PlaceholderText = Loc.T("blockPick.search", "Procurar bloco (ex.: arma, pet, receita, npc)…") };
        var lv = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = CTreeBg, ForeColor = CTreeFg, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), SmallImageList = _treeIcons, ShowGroups = true,
        };
        lv.Columns.Add("", f.Width - 24);
        f.Controls.Add(lv); f.Controls.Add(search);
        // ícone do bloco: o de um item dele (ou a miniatura do modelo 3D)
        int BlockIcon(ItemList b)
        {
            foreach (var it in b.Items.Take(40)) if (IconIndexFor(it) is var i && i >= 0) return i;
            return -1;
        }
        void Fill()
        {
            var q = search.Text.Trim();
            lv.BeginUpdate(); lv.Items.Clear(); lv.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            ListViewGroup G(string key, string text) { if (!groups.TryGetValue(key, out var g)) { g = new ListViewGroup(key, text); groups[key] = g; lv.Groups.Add(g); } return g; }
            for (int i = 0; i < _blockCombo.Items.Count; i++)
            {
                if (_blockCombo.Items[i] is not BlockEntry e || e.Header) continue;
                var text = e.ToString().Trim();
                if (q.Length > 0 && !text.Contains(q, StringComparison.OrdinalIgnoreCase) && !(e.Block?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                var cat = e.Block is null ? Loc.T("blockPick.top", "Atalhos") : CategoryName(BlockCategory(e.Block.Name));
                var item = new ListViewItem(text, e.Block is null ? -1 : BlockIcon(e.Block), G(cat, cat)) { Tag = i };
                if (i == _blockCombo.SelectedIndex) { item.Selected = true; item.Focused = true; }
                lv.Items.Add(item);
            }
            lv.EndUpdate();
            if (lv.SelectedItems.Count > 0) lv.SelectedItems[0].EnsureVisible();
            else if (lv.Items.Count > 0 && q.Length > 0) lv.Items[0].Selected = true;
        }
        void Choose()
        {
            if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is int idx) _blockCombo.SelectedIndex = idx;
            f.Close();
        }
        search.TextChanged += (_, _) => Fill();
        lv.ItemActivate += (_, _) => Choose();
        lv.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && lv.GetItemAt(e.X, e.Y) is not null) Choose(); };
        f.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) f.Close();
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Choose(); }
            else if (e.KeyCode is Keys.Down or Keys.Up && search.Focused) { lv.Focus(); }
        };
        f.Deactivate += (_, _) => f.Close();
        f.FormClosed += (_, _) => f.Dispose();
        Fill();
        f.Show(this);
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
        TextRenderer.DrawText(e.Graphics, o.ToString(), cb.Font, Rectangle.Inflate(e.Bounds, -4, 0), sel ? Color.White : col,
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
        if ((q.Length > 0 && _searchAll.Checked) || (_curBlock is null && _allBlocks))
            src = _store.Current.Lists.SelectMany(l => l.Items);
        else if (_curBlock is null)
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
        _list.VirtualListSize = _view.Count;
        _list.SelectedIndices.Clear();
        _list.Invalidate();
        if (_current is not null && _view.Contains(_current)) SelectItem(_current, focus: false);
        else if (_view.Count > 0) { _current = _view[0]; SelectItem(_current, focus: false); ShowItem(_current); }
    }

    private void OnRetrieveItem(object? s, RetrieveVirtualItemEventArgs e)
    {
        var it = _view[e.ItemIndex];
        e.Item = new ListViewItem(it.Id, IconIndexFor(it)) { Tag = it };
        e.Item.SubItems.Add(string.IsNullOrEmpty(it.Name) ? Loc.T("block.noName", "(sem nome)") : it.Name);
        e.Item.SubItems.Add("");
    }

    // sem ícone no jogo (monstros, NPCs…): miniatura do modelo 3D, feita em segundo plano
    private ModelThumbs? _thumbs;
    private readonly List<(string Ecm, Image? Img)> _thumbBatch = new();
    private readonly System.Windows.Forms.Timer _thumbFlush = new() { Interval = 300 };
    private bool _thumbFlushHooked;
    private string? _thumbsDir;
    private int ThumbIndexFor(Item it)
    {
        if (!it.Fields.TryGetValue("file_model", out var fm) || !fm.Value.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase)) return -1;
        var key = "ecm:" + fm.Value;
        if (_iconIdx.TryGetValue(key, out var idx)) return idx;
        var el = _dataPath is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(_dataPath));
        if (el is null || !File.Exists(Path.Combine(el, "models.pck"))) return -1;
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
                    _list.Refresh();
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
            var ni = new Item { Id = NextCode(block).ToString(), Name = src.Name, Fields = src.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)), Raw = src.Raw?.ToArray() };
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
    private void TranslateFromOther() => TranslateFromOther(null);

    private void TranslateFromOther(string? otherPath)
    {
        if (_store.Current is not { } mod || mod.Source is not ElementsLayout layout) { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        if (otherPath is null)
        {
            using var dlg = new OpenFileDialog { Filter = "elements.data (*.data)|*.data|*.*|*.*", Title = Loc.T("tr.pick", "elements.data já traduzido (qualquer versão)") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            otherPath = dlg.FileName;
        }
        Module other;
        try { Cursor = Cursors.WaitCursor; other = CfgCatalog.Load(otherPath).Module; }
        catch (Exception ex) { Error(Loc.T("tr.loadFail", "Não consegui ler o outro arquivo"), ex); return; }
        finally { Cursor = Cursors.Default; }
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
    private void TranslateFromClient()
    {
        if (_dataPath is null || Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) is not { } target)
        { Status(Loc.T("status.openFirst", "Abra um elements.data primeiro")); return; }
        if (_tasksDirty || _aiView?.HasUnsaved == true)
        {
            MessageBox.Show(this, Loc.T("trc.saveFirst", "Há quests ou AI Policy com alterações não salvas. Salve (ou desfaça) antes de traduzir."), Loc.T("trc.title", "Traduzir a partir de outro cliente"));
            return;
        }
        using var pick = new FolderBrowserDialog { Description = Loc.T("trc.pick", "Pasta do outro Perfect World (o já traduzido, de qualquer versão)"), SelectedPath = LoadSetting("translate_source") ?? "" };
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        SaveSetting("translate_source", pick.SelectedPath);
        var srcEl = FindFile(pick.SelectedPath, "elements.data");
        if (srcEl is null || Path.GetDirectoryName(Path.GetDirectoryName(srcEl)) is not { } source) { Status(Loc.T("trc.noElements", "Não achei elements.data nessa pasta.")); return; }
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) { Status(Loc.T("trc.same", "É a mesma pasta do cliente aberto.")); return; }

        List<ClientTranslator.Step> steps;
        try { Cursor = Cursors.WaitCursor; steps = ClientTranslator.Plan(target, source); }
        catch (Exception ex) { Error(Loc.T("trc.planFail", "Não consegui comparar os clientes"), ex); return; }
        finally { Cursor = Cursors.Default; }
        bool running = GameRunningFrom(target);

        using var f = new Form { Text = Loc.T("trc.title", "Traduzir a partir de outro cliente"), Width = 760, Height = 520, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, BackColor = CSurface, ForeColor = CFg, Font = new Font("Segoe UI", 9.5f) };
        // pelo nome da pasta do cliente (a que contém "element"); o caminho inteiro fica na dica
        static string ClientName(string element) => Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(element).TrimEnd('\\')) ?? element);
        var info = new Label { Dock = DockStyle.Top, Height = 84, Padding = new Padding(10), ForeColor = running ? Color.Firebrick : CMuted, AutoEllipsis = true,
            Text = string.Format(Loc.T("trc.info", "De: {0}\nPara: {1} (o cliente aberto)\nSó entra o que este cliente tem. Cada arquivo ganha cópia de segurança (.bak)."), ClientName(source), ClientName(target))
                + (running ? "\n" + Loc.T("trc.running", "Feche o jogo deste cliente antes de aplicar.") : "") };
        new ToolTip().SetToolTip(info, source + "\n→ " + target);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, BackColor = CSurface, ForeColor = CFg, IntegralHeight = false };
        var detail = new Label { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 6, 10, 6), ForeColor = CMuted };
        const string elementsRow = "elements";
        list.Items.Add(Loc.T("trc.elements", "Itens, monstros, NPCs e habilidades (elements.data) — revisar antes"), true);
        var rows = new List<object> { elementsRow };
        foreach (var s in steps) { list.Items.Add(s.Title, s.Default); rows.Add(s); }
        list.ItemCheck += (_, e) => { if (rows[e.Index] is ClientTranslator.Step { Enabled: false }) e.NewValue = CheckState.Unchecked; };
        list.SelectedIndexChanged += (_, _) => detail.Text = list.SelectedIndex < 0 ? "" : rows[list.SelectedIndex] is ClientTranslator.Step st ? st.Detail
            : Loc.T("trc.elementsDetail", "Nomes e textos pelo ID, bloco e campo, em qualquer versão; abre a janela de revisão e entra no Desfazer.");
        var ok = new Button { Text = Loc.T("trc.go", "Traduzir"), Dock = DockStyle.Bottom, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White, DialogResult = DialogResult.OK };
        f.Controls.Add(list); f.Controls.Add(info); f.Controls.Add(detail); f.Controls.Add(ok);
        f.AcceptButton = ok;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var chosen = list.CheckedIndices.Cast<int>().Select(i => rows[i]).ToList();
        if (chosen.Count == 0) return;
        if (chosen.Any(c => c is ClientTranslator.Step) && GameRunningFrom(target))
        {
            MessageBox.Show(this, Loc.T("trc.running", "Feche o jogo deste cliente antes de aplicar."), Loc.T("trc.title", "Traduzir a partir de outro cliente"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var log = new List<string>(); var touched = new List<string>();
        Cursor = Cursors.WaitCursor;
        foreach (var s in chosen.OfType<ClientTranslator.Step>())
            try { touched.Add(s.Apply(log.Add)); }
            catch (Exception ex) { log.Add($"{s.Title}: " + Loc.T("trc.failed", "falhou — ") + ex.Message); }
        Cursor = Cursors.Default;
        // o editor passa a mostrar o que foi gravado
        if (_tasksPath is not null && touched.Any(t => string.Equals(t, _tasksPath, StringComparison.OrdinalIgnoreCase))) LoadTasks(_tasksPath);
        if (touched.FirstOrDefault(t => t.EndsWith("aipolicy.data", StringComparison.OrdinalIgnoreCase)) is { } ai) _aiView?.LoadFile(ai);
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
    private TabPage? _tabGShop, _tabPck, _tabNpcGen;
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
            if (_tabs?.SelectedTab == _tabPck && _pckView?.Editor is { } ui) { if (!(undo ? ui.Undo() : ui.Redo())) Status(undo ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer")); return true; }
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

        // sub-abas por grupo (Básico, Habilidades, Queda, Modelo…), mantendo a aba escolhida se existir
        RebuildGroupTabs(item);
        FillGrid(item);
        FillDescription(item);
        FillFindFields(item);

        if (!_navigating)
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
        if (id.StartsWith("ui:", StringComparison.OrdinalIgnoreCase)) { if (_tabs is not null && _tabPck is not null) _tabs.SelectedTab = _tabPck; _pckView?.OpenUiForSnapshot(id[3..]); return; }
        if (id.Equals("links", StringComparison.OrdinalIgnoreCase)) { _detailTabs.SelectedTab = _tabLinks; if (_current is not null) FillLinks(_current); return; }
        foreach (TabPage t in _groupTabs.TabPages)
            if (string.Equals(t.Tag as string, id, StringComparison.OrdinalIgnoreCase)) { _groupTabs.SelectedTab = t; return; }
    }

    private void RebuildGroupTabs(Item item)
    {
        var groups = GroupsOf(item);
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
            if (vis.Count == 0) continue;
            if (sel == "Todos")
            {
                int hr = _grid.Rows.Add("  " + FieldLabels.GroupName(gname).ToUpperInvariant(), "", "");
                var hrow = _grid.Rows[hr]; hrow.ReadOnly = true;
                hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                hrow.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            foreach (var (k, f) in vis)
            {
                var desc = Describe(item, k, f.Value);
                int r = _grid.Rows.Add("  " + FieldLabels.Label(k), f.Value, desc);
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
                int hr = _grid.Rows.Add("  " + string.Format(Loc.T("grid.droppedBy", "DROPADO POR ({0} monstros)"), drops.Count), "", "");
                var hrow = _grid.Rows[hr]; hrow.ReadOnly = true;
                hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                hrow.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
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
                Loc.T("grid.hiddenHow", "Exibir > Mostrar campos sem uso no bloco"));
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
        try
        {
            using var a = new PckArchive(pck);
            // no pck o caminho começa com "models\" minúsculo; no elements.data é "Models\..."
            _models = a.Entries.Where(e => e.Path.EndsWith(".ecm", StringComparison.OrdinalIgnoreCase))
                .Select(e => "Models" + e.Path[(e.Path.IndexOf('\\') is var i && i > 0 ? i : 0)..]).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) { Status(Loc.T("status.modelsFail", "Não foi possível ler o models.pck: ") + ex.Message); }
        return _models;
    }

    private static string? FindClientPck(string elementsPath, string name)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
            foreach (var c in new[] { Path.Combine(dir, name), Path.Combine(dir, "element", name) })
                if (File.Exists(c)) return c;
        return null;
    }

    private bool EditFieldSpecial(Item item, string key)
    {
        var kind = KindOf(item, key);
        var cur = item.Fields[key].Value;
        if (key.Equals("file_icon", StringComparison.OrdinalIgnoreCase) && _atlas is not null)
        {
            // mantém a pasta do caminho atual (Surfaces\图标\物品\...) e troca só o arquivo
            if (FieldEditors.IconPicker(this, _atlas, cur) is { } name)
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
            Text = Loc.T("wiz.hint", "Copia tudo do modelo de base (aparência, nível, vida, ataques, habilidades, drops) com o ID e o nome novos. Depois do OK o item novo abre no editor para você ajustar o resto.\n\nO mesmo ID pode nascer em muitos pontos do mapa sem conflito; um ID novo é só para quando você quer um monstro/NPC diferente."),
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
            Fields = baseItem.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)),
            Raw = baseItem.Raw?.ToArray(),
        };
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status(string.Format(Loc.T("wiz.done", "{0} criado com ID {1} — ajuste os campos e salve."), ni.Name, ni.Id));
        return int.Parse(ni.Id);
    }

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
    private int? PickShopItem(IWin32Window owner, int current)
    {
        var src = PickSource(FieldKind.ItemRef);
        if (src.Count == 0) return null;
        var picked = FieldEditors.Pick(owner, Loc.T("pick.item", "Escolher item"), src, current.ToString(), IconIndexFor, _treeIcons,
            it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : "", it => _owner.TryGetValue(it, out var b) ? Pretty(b.Name) : null, false);
        return picked is { Count: > 0 } && int.TryParse(picked[0].Id, out var id) ? id : null;
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

    // Descrição do jogo com as cores (^RRGGBB) — como aparece no tooltip do cliente.
    private void FillDescription(Item item)
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
        if (_cfgs is not null && int.TryParse(item.Id, out var id) && _cfgs.ItemDescs.TryGetValue(id, out var desc))
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

    private void GoToItem(Item it)
    {
        if (!_owner.TryGetValue(it, out var b)) return;
        if (_curBlock != b)
            foreach (var o in _blockCombo.Items)
                if (o is BlockEntry be && be.Block == b) { _search.Text = ""; _blockCombo.SelectedItem = be; break; }
        if (_typeCombo.Visible && _typeCombo.SelectedIndex > 0) _typeCombo.SelectedIndex = 0;
        _current = it;
        SelectItem(it);
        ShowItem(it);
        _detailTabs.SelectedIndex = 0;
    }

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0 || _grid.Rows[row].Tag is not string key || !_current.Fields.ContainsKey(key)) return;
        var cell = _grid.Rows[row].Cells[1];
        var val = cell is DataGridViewCheckBoxCell ? (cell.Value is true ? "1" : "0") : cell.Value?.ToString() ?? "";
        _edits.SetField(_current, key, Field.Of(_current.Fields[key].Type, val), string.Format(Loc.T("hist.field", "{0} em {1}"), FieldLabels.Label(key), _current.Id));
        if (_owner.TryGetValue(_current, out var ob)) _unusedCache.Remove(ob);
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            _list.Invalidate();
            _header.Text = TasksFile.SplitColor(val).Text;   // o ID já aparece no quadro ao lado
        }
        Status(string.Format(Loc.T("status.fieldUpdated", "'{0}' atualizado"), FieldLabels.Label(key)));
    }

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

    private static long NextCode(ItemList block)
    {
        long max = 0;
        foreach (var it in block.Items) if (long.TryParse(it.Id, out var n) && n > max) max = n;
        return max + 1;
    }

    private static void SetIdName(Item it)
    {
        if (it.Fields.ContainsKey("ID")) it.Fields["ID"] = Field.Of(FieldType.Int, it.Id);
        if (it.Fields.ContainsKey("Name")) it.Fields["Name"] = Field.Of(FieldType.String, it.Name);
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
            Fields = _current.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)),
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
        if (_blockCombo.Items.Count > 0 && _blockCombo.Items[0] is BlockEntry fav)
            _blockCombo.Items[0] = fav with { Label = $"{Loc.T("block.favorites", "★ Favoritos")} ({_favorites.Count})" };
        RefreshView();
        Status(_favorites.Contains(key) ? Loc.T("status.favAdded", "Adicionado aos favoritos") : Loc.T("status.favRemoved", "Removido dos favoritos"));
    }
    private static string FavFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "favorites.txt");
    private void SaveFavorites()
    { try { Directory.CreateDirectory(Path.GetDirectoryName(FavFile)!); File.WriteAllLines(FavFile, _favorites); } catch { } }
    private static IEnumerable<string> LoadFavorites()
    { try { return File.Exists(FavFile) ? File.ReadAllLines(FavFile) : Array.Empty<string>(); } catch { return Array.Empty<string>(); } }

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
    internal Bitmap? RenderListTip()
    {
        if (_current is null) return null;
        BuildTip(_current);
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
        var editRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 6, Padding = new Padding(10, 6, 10, 6), BackColor = CSurface };
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        Button Btn(string t, Action a, bool accent = false)
        {
            var b = new Button { Text = t, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = accent ? CAccent : CSurface2, ForeColor = accent ? Color.White : CFg, Margin = new Padding(4, 0, 0, 0) };
            b.Click += (_, _) => a(); return b;
        }
        _qName.MaxLength = TasksFile.MaxNameLength;
        _qName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyQuestName(); } };
        var nameLbl = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CMuted };
        var applyName = Btn("", ApplyQuestName, accent: true);
        var save = Btn("", SaveTasks);
        var clone = Btn("", CloneQuest);
        var delete = Btn("", DeleteQuest);
        Bind(() =>
        {
            nameLbl.Text = Loc.T("quest.nameLbl", "Nome"); applyName.Text = Loc.T("quest.applyName", "Aplicar nome");
            save.Text = Loc.T("quest.save", "💾 Salvar tasks.data");
            clone.Text = Loc.T("quest.clone", "Clonar quest"); delete.Text = Loc.T("quest.delete", "Excluir quest");
        });
        editRow.Controls.Add(nameLbl, 0, 0);
        editRow.Controls.Add(_qName, 1, 0);
        editRow.Controls.Add(applyName, 2, 0);
        editRow.Controls.Add(save, 3, 0);
        editRow.Controls.Add(clone, 4, 0);
        editRow.Controls.Add(delete, 5, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        // Dados
        var tData = new TabPage { BackColor = CSurface };
        StyleQuestGrid();
        tData.Controls.Add(_qGrid);
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
        right.Controls.Add(editRow);
        right.Controls.Add(_qInfo);
        right.Controls.Add(_qStatus);
        split.Panel2.Controls.Add(right);
        split.SizeChanged += (_, _) => { if (split.Width > 700 && split.Tag is null) { split.SplitterDistance = 430; split.Tag = true; } };
        return split;
    }

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
        if (_tasks is not { FullSchema: true } || RootOfSelected() is not { } q) return;
        if (MessageBox.Show(this, string.Format(Loc.T("quest.confirmDelete", "Excluir a quest {0} \"{1}\" e as {2} subquests dela?\n\nNPCs que oferecem essa quest continuam apontando para o ID (confira na aba Elements)."),
                q.Id, TasksFile.SplitColor(q.Name).Text, q.Descendants().Count()), Loc.T("quest.delete", "Excluir quest"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (_tasks.DeleteRoot(q)) { RefreshQuests(); MarkTasksDirty(string.Format(Loc.T("quest.deleted", "Quest {0} excluída"), q.Id)); }
    }

    private void MarkTasksDirty(string msg) { _tasksDirty = true; _qStatus.Text = msg + Loc.T("quest.dirty", " — clique em Salvar tasks.data para gravar."); }

    private void LoadTasks(string path)
    {
        try
        {
            _tasks = new TasksFile(path);
            _tasksPath = path; _tasksDirty = false;
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
        foreach (var f in _qCur.Fields)
        {
            if (f.Group != group)
            {
                group = f.Group;
                int hr = _qGrid.Rows.Add("  " + QuestLabels.Show(group).ToUpperInvariant(), "", "");
                var hrow = _qGrid.Rows[hr]; hrow.ReadOnly = true;
                hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                hrow.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            var v = _qCur.Get(f);
            int r = _qGrid.Rows.Add("  " + QuestLabels.Show(f.Name), v, QuestFieldHint(f, v));
            _qGrid.Rows[r].Tag = f;
            if (!f.Editable) { _qGrid.Rows[r].ReadOnly = true; _qGrid.Rows[r].Cells[1].Style.ForeColor = CMuted; }
        }
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
    private readonly Label _chkSummary = new() { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 11), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };

    private Control BuildCheckView()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = CBg, Padding = new Padding(16) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 4 * 38 + 6, ColumnCount = 3, BackColor = CBg };   // 4 linhas: cliente, servidor, verificar, corrigir
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        // textos ligados ao idioma (Bind): trocar o idioma refaz rótulos, botões, colunas e o resultado já mostrado
        Label L(Func<string> t) { var l = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CFg }; Bind(() => l.Text = t()); return l; }
        Button B(Func<string> t, Action a) { var b = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = CSurface, ForeColor = CFg }; b.Click += (_, _) => a(); Bind(() => b.Text = t()); return b; }
        _chkClient.Dock = DockStyle.Fill; _chkServer.Dock = DockStyle.Fill;
        _chkClient.Text = LoadSetting("check_client") ?? "";
        _chkServer.Text = LoadSetting("check_server") ?? "";
        form.Controls.Add(L(() => Loc.T("chk.clientElementFolder", "Cliente (pasta element)")), 0, 0); form.Controls.Add(_chkClient, 1, 0);
        form.Controls.Add(B(() => Loc.T("chk.browse", "Procurar…"), () => PickFolder(_chkClient, Loc.T("chk.clientElementFolder2", "Pasta element do cliente"))), 2, 0);
        form.Controls.Add(L(() => Loc.T("chk.serverHomeFolder", "Servidor (pasta home)")), 0, 1); form.Controls.Add(_chkServer, 1, 1);
        form.Controls.Add(B(() => Loc.T("chk.browse", "Procurar…"), () => { if (PickFolder(_chkServer, Loc.T("chk.serverHomeFolderContainsGamed", "Pasta home do servidor (contém gamed, glinkd…)"))) SetServerFolder(_chkServer.Text); }), 2, 1);
        var run = B(() => Loc.T("chk.check", "✔ Verificar"), RunCheck); run.BackColor = CAccent; run.ForeColor = Color.White; run.Font = new Font("Segoe UI Semibold", 10);
        var runInfo = L(() => Loc.T("chk.comparesTheVersionsAndTimestamps", "Compara versões e timestamps que o login exige iguais (elements, tasks, loja), cópias por mapa e a criptografia de rede (sem mostrar as chaves).")); runInfo.ForeColor = CMuted;
        form.Controls.Add(runInfo, 0, 2);
        form.SetColumnSpan(runInfo, 2);
        form.Controls.Add(run, 2, 2);
        var fix = B(() => Loc.T("chk.fix", "Corrigir…"), FixPair); fix.BackColor = Color.FromArgb(255, 244, 214); fix.Font = new Font("Segoe UI Semibold", 10);
        var fixInfo = L(() => Loc.T("chk.fixRebuildsOnTheServer", "Corrigir: refaz no servidor, a partir do cliente, o que o login exige igual (elements, tasks, loja). Cada arquivo trocado ganha backup.")); fixInfo.ForeColor = CMuted;
        form.Controls.Add(fixInfo, 0, 3);
        form.SetColumnSpan(fixInfo, 2);
        form.Controls.Add(fix, 2, 3);
        for (int i = 0; i < 4; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

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
        _chkSummary.ForeColor = CFg;

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), BackColor = CBg };
        listHost.Controls.Add(_chkList);
        listHost.Controls.Add(_chkSummary);
        root.Controls.Add(listHost);
        root.Controls.Add(form);
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
        _chkList.Items.Clear(); _chkSummary.Text = "";   // o resultado era do servidor anterior
        _npcgen?.ServerFolderChanged(home);
    }

    private void RunCheck()
    {
        if (_chkClient.Text.Length == 0 && _dataPath is not null)
            _chkClient.Text = Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "";
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status(Loc.T("chk.chooseTheClientAndServer", "Escolha as pastas do cliente e do servidor.")); return; }
        // aceita element\data, a pasta do jogo, a raiz dos arquivos do servidor... e mostra a pasta que vale
        _chkClient.Text = VersionCheck.ClientElement(_chkClient.Text); _chkServer.Text = VersionCheck.ServerHome(_chkServer.Text);
        SaveSetting("check_client", _chkClient.Text); SaveSetting("check_server", _chkServer.Text);
        _npcgen?.ServerFolderChanged(_chkServer.Text);   // servidor digitado à mão também vale para o NPCGen
        var rows = VersionCheck.Run(_chkClient.Text, _chkServer.Text);
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
        _chkSummary.Text = nothing ? Loc.T("chk.filesNotFoundCheckThe", "✖ Não achei os arquivos: confira as pastas (cliente = element do jogo; servidor = pasta que tem gamed/)")
                         : err > 0 ? string.Format(Loc.T("chk.0ErrorSThatBlock", "✖ {0} erro(s) que impedem o login · {1} aviso(s)"), err, warn)
                         : warn > 0 ? string.Format(Loc.T("chk.noLoginErrors0Warning", "⚠ Sem erros de login · {0} aviso(s)"), warn) : Loc.T("chk.clientAndServerAreCompatible", "✔ Cliente e servidor compatíveis");
        _chkSummary.ForeColor = nothing || err > 0 ? Color.FromArgb(200, 30, 30) : warn > 0 ? Color.FromArgb(180, 110, 0) : Color.FromArgb(22, 128, 61);
        Status(Loc.T("chk.checkFinished", "Verificação concluída"));
    }

    // Correção automática do par (issue #26): o usuário escolhe o que trocar; o cliente é a referência.
    private void FixPair()
    {
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status(Loc.T("chk.pickBothFolders", "Escolha as pastas do cliente e do servidor.")); return; }
        var fixes = PairFixer.Plan(_chkClient.Text, _chkServer.Text, _serverView is { } sv ? sv.CreateServer : null);
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
        // troca de programas do servidor local: pergunta backup e banco novo; "Agora não" tira o item
        marked.RemoveAll(i => fixes[i].Swap is { } sw && !ServerView.AskSwap(this, sw));
        // fora da tela: trocar os programas do servidor local leva minutos
        ReadProgressDialog.Run(this, Loc.T("fix.title", "Corrigir o par"), ctx =>
        {
            for (int k = 0; k < marked.Count; k++)
            {
                var fx = fixes[marked[k]];
                ctx.Report((double)k / marked.Count, fx.Title, indeterminate: true, force: true);
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
    private void Error(string msg, Exception ex) { Status(msg + ": " + ex.Message); MessageBox.Show(ex.Message, msg, MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
