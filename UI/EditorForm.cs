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
    private string[] _tipLines = Array.Empty<string>();
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
        var uses = _refs?.UsedBy(item.Id).Count ?? 0;
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
        arq.DropDownItems.AddRange(new ToolStripItem[] { openPw, openEl, new ToolStripSeparator(), save, saveAs, new ToolStripSeparator(), exportCsv, exit });
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
        var about = new ToolStripMenuItem { ForeColor = CFg };
        about.Click += (_, _) => MessageBox.Show(Loc.T("menu.aboutText", "MyAceEditor — editor dos dados do Perfect World."), Loc.T("menu.about", "Sobre"));
        aj.DropDownItems.Add(about);
        foreach (var (code, name) in AppLang.All)
        {
            var mi = new ToolStripMenuItem(name) { ForeColor = CFg, Checked = AppLang.Current == code, Tag = code };
            mi.Click += (_, _) => AppLang.Current = code;
            lg.DropDownItems.Add(mi);
        }
        AppLang.Changed += () => { foreach (ToolStripMenuItem x in lg.DropDownItems) x.Checked = (string?)x.Tag == AppLang.Current; };
        // o seletor de idioma fica sempre em "Idioma / Language" (universal), com cada língua pelo próprio nome
        lg.Text = "Idioma / Language";
        Bind(() =>
        {
            arq.Text = Loc.T("menu.file", "&Arquivo");
            openPw.Text = Loc.T("menu.openPw", "Abrir &pasta do Perfect World…");
            openEl.Text = Loc.T("menu.openElements", "Abrir &elements.data…");
            save.Text = Loc.T("menu.save", "&Salvar");
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
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, view, tools, lg, aj });
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
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B(AddItem, b => b.Text = Loc.T("tb.add", "＋ Adicionar")));
        tb.Items.Add(B(RemoveItem, b => b.Text = Loc.T("tb.delete", "🗑 Excluir")));
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
        Bind(() => { tG.Text = Loc.T("tab.gshop", "GShop"); foreach (Control c in tG.Controls) c.Text = Loc.T("tab.gshopSoon", "Editor de loja\n(em breve)"); });
        tG.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = CMuted, Text = "Editor de loja\n(em breve)", Font = new Font("Segoe UI", 11) });
        tabs.TabPages.Add(tG);
        var tAi = new TabPage() { BackColor = CBg };
        Bind(() => tAi.Text = Loc.T("tab.aipolicy", "AI Policy"));
        _aiView = new AiPolicyView(CBg, CFg, CTreeBg, CTreeFg, CAccent);
        tAi.Controls.Add(_aiView);
        tabs.TabPages.Add(tAi);
        var tChk = new TabPage() { BackColor = CBg };
        Bind(() => tChk.Text = Loc.T("tab.verify", "Verificação"));
        tChk.Controls.Add(BuildCheckView());
        tabs.TabPages.Add(tChk);
        var tPck = new TabPage() { BackColor = CBg };
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
        _typeCombo.SelectedIndexChanged += (_, _) => RefreshView();

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 4, 0, 0), BackColor = CTreeBg };
        _search.Dock = DockStyle.Fill; _search.BackColor = CTreeInput; _search.ForeColor = CTreeFg; _search.Font = new Font("Segoe UI", 10);
        _search.TextChanged += (_, _) => RefreshView();
        _searchAll.ForeColor = CTreeFg; _searchAll.BackColor = CTreeBg;
        _searchAll.CheckedChanged += (_, _) => RefreshView();
        _tip.SetToolTip(_searchAll, "Buscar em todos os blocos");
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
        tVal.Controls.Add(_groupTabs);
        _groupTabs.SelectedIndexChanged += (_, _) => { if (_current is not null) FillGrid(_current); };
        // Buscar por campo (no bloco atual)
        var tFind = new TabPage() { BackColor = CSurface };
        Bind(() => tFind.Text = Loc.T("detail.search", "Buscar"));
        var findBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0), BackColor = CSurface };
        _findOp.Items.AddRange(new object[] { "=", "≠", ">", "<", "contém" }); _findOp.SelectedIndex = 0;
        var findBtn = new Button { Text = "Buscar", Height = 26, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White };
        findBtn.Click += (_, _) => RunFind();
        _findValue.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunFind(); } };
        var findLbl = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0), ForeColor = CMuted };
        Bind(() => { findLbl.Text = Loc.T("list.field", "Campo"); findBtn.Text = Loc.T("detail.search", "Buscar"); });
        findBar.Controls.AddRange(new Control[] { findLbl, _findField, _findOp, _findValue, findBtn });
        _findList.Columns.Add("ID", 70, HorizontalAlignment.Right); _findList.Columns.Add("Nome", 300); _findList.Columns.Add("Valor", 200);
        Bind(() => { _findList.Columns[0].Text = Loc.T("list.id", "ID"); _findList.Columns[1].Text = Loc.T("list.name", "Nome"); _findList.Columns[2].Text = Loc.T("list.value", "Valor"); });
        _findList.SmallImageList = _treeIcons; _findList.BackColor = CSurface; _findList.ForeColor = CFg;
        _findList.DoubleClick += (_, _) => { if (_findList.SelectedItems.Count > 0 && _findList.SelectedItems[0].Tag is Item it) GoToItem(it); };
        tFind.Controls.Add(_findList); tFind.Controls.Add(findBar);
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
                case string k when _current is not null && _refs?.Resolve(FieldKinds.Of(k), _current.Fields[k].Value) is { } hit: GoToItem(hit.Item); break;
                case Item it: GoToItem(it); break;
            }
        };
        _grid.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f);
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
        // caixa de seleção: confirma o clique na hora (senão só grava ao sair da célula)
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellPainting += PaintExtras;
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
        _gridCtx.Opening += (_, e) =>
        {
            if (_grid.CurrentRow?.Tag is not string k) { e.Cancel = true; return; }
            balance.Visible = FieldKinds.Of(k) == FieldKind.Probability;
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
        if (kind == FieldKind.Probability && float.TryParse(f.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0 && p <= 1)
        {
            e.PaintBackground(r, true);
            var bar = new Rectangle(r.X + 76, r.Y + 8, Math.Max(20, Math.Min(180, r.Width - 90)), r.Height - 16);
            using (var back = new SolidBrush(Color.FromArgb(226, 231, 238))) e.Graphics.FillRectangle(back, bar);
            using (var fill = new SolidBrush(Color.FromArgb(34, 160, 90))) e.Graphics.FillRectangle(fill, bar with { Width = Math.Max(1, (int)Math.Round(bar.Width * p)) });
            TextRenderer.DrawText(e.Graphics, $"{p * 100:0.###}%", _grid.Font, new Rectangle(r.X + 6, r.Y, 68, r.Height), fg, TextFormatFlags.VerticalCenter);
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
        using var dlg = new FolderBrowserDialog { Description = "Selecione a pasta do Perfect World (que contém 'element')" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var ele = FindFile(dlg.SelectedPath, "elements.data");
        if (ele is null) { Status("Não achei elements.data nessa pasta."); return; }
        LoadElements(ele);
    }

    private static string? FindFile(string root, string name)
    {
        foreach (var c in new[] { name, Path.Combine("element", "data", name), Path.Combine("data", name), Path.Combine("element", name) })
            if (File.Exists(Path.Combine(root, c))) return Path.Combine(root, c);
        try { return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault(); } catch { return null; }
    }

    private void OpenElements()
    {
        using var dlg = new OpenFileDialog { Filter = "Perfect World elements (*.data)|*.data|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadElements(dlg.FileName);
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
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
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
        ctx.Report(.70, "Lendo ícones", indeterminate: true, force: true);
        if (pck is not null)
            try { atlas = IconAtlas.Load(pck, ctx.Slice(.70, .85)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("Ícones: " + ex.Message); }

        GameConfigs? cfgs = null;
        ctx.Report(.85, "Lendo descrições", indeterminate: true, force: true);
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
        _edits.Clear();
        _refs = new RefIndex(module);
        _aiView?.SetElements(module);   // nomes de monstros e "quem usa cada política" na aba AI Policy
        _aiView?.LoadRememberedIfNeeded();
        _atlasPck = r.AtlasPck; _atlas = r.Atlas;          // ícones: cache novo para este pacote
        _iconIdx.Clear(); _treeIcons.Images.Clear();
        _cfgs = r.Cfgs;
        if (r.Tasks is { } t)
        {
            _tasks = t; _tasksPath = r.TasksPath; _tasksDirty = false;
            RefreshQuests();
            _qStatus.Text = t.FullSchema
                ? $"{Path.GetFileName(r.TasksPath)} · versão {t.Version} · {t.Roots!.Count:N0} quests ({t.AllNodes().Count():N0} com subquests) · edição completa"
                : $"{Path.GetFileName(r.TasksPath)} · versão {t.Version} · {t.Quests.Count:N0} quests · só nomes";
        }
        BuildTree();
        SaveLast(path);
        _dataPath = path;
        int items = module.Lists.Sum(l => l.Items.Count);
        Status($"{module.Name} · {module.Lists.Count} blocos · {items:N0} itens · {100.0 * r.Position / r.Length:0.0}% · {r.Ms / 1000.0:0.0} s"
            + $" · {r.Cfg}"
            + (_atlas is null ? " · ícones: surfaces.pck não encontrado" : $" · {_atlas.Count:N0} ícones do jogo")
            + (module.Source is ElementsLayout { CanWrite: false } lay ? " · " + Loc.T("status.readOnly", "somente leitura") + ": " + lay.ReadOnlyReason : "")
            + (r.Warnings.Count > 0 ? " · " + string.Join(" · ", r.Warnings) : ""));
        Text = $"MyAceEditor — {module.Name}{HeaderInfo(path)}";
        UseWaitCursor = false;
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
    private void SaveAs() => SaveElements(saveAs: false);
    // Salvar do menu/Ctrl+S/barra: grava o arquivo da aba aberta (Quests → tasks.data, senão elements.data).
    private void SaveCurrent() { if (_tabs?.SelectedTab == _tabQuests) SaveTasks(); else SaveAs(); }
    private void SaveElementsAs() => SaveElements(saveAs: true);

    // Exporta o bloco aberto (ou o bloco do item selecionado) como planilha CSV: ID, nome e todos os campos.
    private void ExportBlockCsv()
    {
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null || block.Items.Count == 0) { Status(Loc.T("status.exportNoBlock", "Escolha um bloco com itens para exportar.")); return; }
        using var dlg = new SaveFileDialog { Filter = "Planilha CSV (*.csv)|*.csv", FileName = block.Name + ".csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var keys = block.Items.SelectMany(i => i.Fields.Keys).Distinct().ToList();
        string Q(string v) => v.Contains(';') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(";", new[] { "ID", "Nome" }.Concat(keys).Select(Q)));
        foreach (var it in block.Items)
            sb.AppendLine(string.Join(";", new[] { it.Id, it.Name }.Concat(keys.Select(k => it.Fields.TryGetValue(k, out var f) ? f.Value : "")).Select(Q)));
        File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));   // BOM: abre certo no Excel
        Status(Loc.T("status.exported", "Exportado") + $": {block.Items.Count:N0} " + Loc.T("status.items", "itens") + $" → {Path.GetFileName(dlg.FileName)}");
    }

    private void SaveElements(bool saveAs)
    {
        _grid.EndEdit();                                // célula em edição entra no item antes de gravar
        if (_store.Current?.Source is not ElementsLayout || _dataPath is null) { Status("Abra um elements.data primeiro"); return; }
        var target = _dataPath;
        if (saveAs)
        {
            using var dlg = new SaveFileDialog { Filter = "elements.data (*.data)|*.data", FileName = Path.GetFileName(_dataPath), InitialDirectory = Path.GetDirectoryName(_dataPath) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            target = dlg.FileName;
        }
        else if (MessageBox.Show($"Gravar as alterações em:\n{target}\n\nUma cópia de segurança do arquivo atual será criada ao lado dele.",
                     "Salvar elements.data", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            (string? Block, string Id)? keep = _current is null ? null : (BlockContaining(_current)?.Name, _current.Id);
            var backup = ElementsWriter.Save(_store.Current, target);
            LoadElements(target);                      // recarrega: o layout passa a refletir o arquivo novo
            if (keep is { } k && _store.Current?.FindListByName(k.Block ?? "")?.FindById(k.Id) is { } again)
            { _current = again; SelectItem(again); ShowItem(again); }
            Status($"Salvo em {Path.GetFileName(target)}" + (backup is null ? "" : $" · backup: {Path.GetFileName(backup)}"));
        }
        catch (Exception ex) { Error("Falha ao salvar", ex); }
        finally { UseWaitCursor = false; }
    }

    // ---------- lista de itens (estilo Rody: seletor de bloco + lista plana) ----------
    // Entrada do seletor de bloco. Block == null => Favoritos.
    // Dyn calcula o texto na hora de desenhar, então acompanha o idioma sem recriar o combo.
    private sealed record BlockEntry(ItemList? Block, string Label, bool Header = false, Func<string>? Dyn = null)
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
            _blockCombo.Items.Add(new BlockEntry(null, $"★ Favoritos ({_favorites.Count})",
                Dyn: () => $"{Loc.T("block.favorites", "★ Favoritos")} ({_favorites.Count})"));
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
            _typeCombo.Items.Add(new TypeEntry(null, null, $"Todos os tipos ({nAll:N0})",
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

    private static string Fv(Item it, string k) => it.Fields.GetValueOrDefault(k)?.Value ?? "0";

    // Recalcula os itens visíveis (bloco/favoritos + filtro de tipo + busca + ordenação).
    private void RefreshView()
    {
        if (_store.Current is null) return;
        string q = _search.Text.Trim();
        IEnumerable<Item> src;
        if (q.Length > 0 && _searchAll.Checked)
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

    private int IconIndexFor(Item it)
    {
        if (_atlas is null && _iconsRoot is null) return -1;
        if (!it.Fields.TryGetValue("file_icon", out var fi) || string.IsNullOrEmpty(fi.Value)) return -1;
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
            int uses = _refs?.UsedBy(it.Id).Count ?? 0;
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
        Status($"{_view.Count:N0} itens selecionados");
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
        Status($"{sel.Count} item(ns) clonado(s)");
    }

    private void BulkDelete()
    {
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"Excluir {sel.Count} item(ns) selecionado(s)?", "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        using (_edits.Batch(string.Format(Loc.T("hist.delete", "Excluir {0} item(ns)"), sel.Count)))
            foreach (var it in sel) if (_owner.TryGetValue(it, out var b)) { _edits.RemoveItem(b, it, ""); _owner.Remove(it); _favorites.Remove(FavKey(b, it)); }
        SaveFavorites();
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status($"{sel.Count} item(ns) excluído(s)");
    }

    private void BulkChange()
    {
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        // mesmos campos da grade (sem realname nem os sem uso, se estiverem ocultos); começa no campo selecionado na grade
        var hidden = _hideUnused && BlockContaining(sel[0]) is { } blk ? UnusedFields(blk) : new HashSet<string>();
        var fields = sel[0].Fields.Keys.Where(k => k != "realname" && !hidden.Contains(k)).ToList();
        var start = _grid.CurrentRow?.Tag as string;
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
        if (hit is null) { Status($"ID {id} não encontrado"); return; }
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

    // ---------- procurar erros ----------
    private ErrorsWindow? _errors;
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
        if (structural)
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
            UndoRedo(undo: keyData == (Keys.Control | Keys.Z));
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
        var uses = _refs?.UsedBy(item.Id).Count ?? 0;
        _tabLinks.Text = (uses > 0 ? $"{Loc.T("detail.links", "Vínculos")} ({uses})" : Loc.T("detail.links", "Vínculos"));
        if (_detailTabs.SelectedTab == _tabLinks) FillLinks(item);

        // loja do NPC: aba extra com a prévia como no jogo
        bool isShop = _owner.TryGetValue(item, out var ib) && ib.Name == "NPC_SELL_SERVICE";
        if (isShop && !_detailTabs.TabPages.Contains(_tabShop)) _detailTabs.TabPages.Insert(1, _tabShop);
        else if (!isShop && _detailTabs.TabPages.Contains(_tabShop)) _detailTabs.TabPages.Remove(_tabShop);
        if (isShop) _shop.ShowShop(item);

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
        if (id.Equals("desc", StringComparison.OrdinalIgnoreCase)) { _detailTabs.SelectedIndex = _detailTabs.TabCount - 1; return; }
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

    private void FillGrid(Item item)
    {
        var sel = SelectedGroupId;
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
                if (IsSpecialEditor(k)) _grid.Rows[r].Cells[0].ToolTipText = Loc.T("grid.dblClick", "Duplo clique para escolher");
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
            var drops = (_refs?.UsedBy(item.Id) ?? Array.Empty<RefIndex.Ref>())
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
    private bool IsSpecialEditor(string key) =>
        FieldKinds.Of(key) is FieldKind.ClassMask or FieldKind.ProcMask or FieldKind.ImmuneMask or FieldKind.AddonRef or FieldKind.ItemRef or FieldKind.MonsterRef or FieldKind.RecipeRef or FieldKind.ServiceRef
        || (key.Equals("file_icon", StringComparison.OrdinalIgnoreCase) && _atlas is not null);

    private bool EditFieldSpecial(Item item, string key)
    {
        var kind = FieldKinds.Of(key);
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
        switch (kind)
        {
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
                Status(picked.Count > slots.Count ? $"{slots.Count} de {picked.Count} escolhidos couberam nos campos disponíveis" : $"{picked.Count} valor(es) aplicado(s)");
                return true;
            }
        }
        return false;
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
        else Put(_cfgs is null ? "(configs.pck não encontrado)" : "(este item não tem descrição no configs.pck)", CMuted);
        _descBox.SelectionStart = 0; _descBox.SelectionLength = 0; _descBox.ScrollToCaret();   // AppendText rola até o fim
    }

    // Resumo no estilo do tooltip do jogo (como o preview do VAE): faixas min-max numa linha, requisitos, preço e atributos possíveis.
    private void PutTooltipSummary(Item item, Action<string, Color, bool> put)
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
            foreach (var p2 in list.Take(64)) put("  " + p2 + "\n", blue, false);
            lines++;
        }
        if (lines > 0) put("\n", val, false);
    }

    private void FillFindFields(Item item)
    {
        var keys = item.Fields.Keys.ToList();
        if (_findField.Items.Count == keys.Count && _findField.Tag == _curBlock) return;
        var keep = _findField.SelectedItem as string;
        _findField.Items.Clear();
        foreach (var k in keys) _findField.Items.Add(k);
        _findField.Tag = _curBlock;
        _findField.SelectedItem = keep != null && keys.Contains(keep) ? keep : keys.FirstOrDefault(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase)) ?? keys.FirstOrDefault();
    }

    private void RunFind()
    {
        if (_findField.SelectedItem is not string key) return;
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null) return;
        string v = _findValue.Text.Trim(), op = _findOp.SelectedItem as string ?? "=";
        bool Match(string fv)
        {
            if (op == "contém") return fv.Contains(v, StringComparison.OrdinalIgnoreCase);
            if (op is ">" or "<" && double.TryParse(fv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
                && double.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
                return op == ">" ? a > b : a < b;
            return op == "=" ? fv == v : op == "≠" && fv != v;
        }
        _findList.BeginUpdate(); _findList.Items.Clear();
        int n = 0;
        foreach (var it in block.Items)
        {
            if (!it.Fields.TryGetValue(key, out var f) || !Match(f.Value)) continue;
            _findList.Items.Add(new ListViewItem(new[] { it.Id, it.Name, f.Value }, IconIndexFor(it)) { Tag = it });
            if (++n >= 2000) break;
        }
        _findList.EndUpdate();
        Status($"Buscar: {n}{(n >= 2000 ? "+" : "")} item(ns) em {Pretty(block.Name)} com {key} {op} {v}");
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
        var kind = FieldKinds.Of(field);
        switch (kind)
        {
            case FieldKind.Probability:
                return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) && pr > 0 && pr <= 1
                    ? $"{pr * 100:0.###}%" : "";
            case FieldKind.ClassMask: return FieldKinds.ClassNames(value);
            case FieldKind.ProcMask: return BitMasks.Describe(BitMasks.Proc, long.TryParse(value, out var pv) ? pv : 0);
            case FieldKind.ImmuneMask: return BitMasks.Describe(BitMasks.Immune, long.TryParse(value, out var iv) ? iv : 0);
            case FieldKind.SkillRef:
                if (IsEmpty(value)) return "";
                return int.TryParse(value, out var sid) && _cfgs?.SkillNames.TryGetValue(sid, out var sname) == true
                    ? $"{TasksFile.SplitColor(sname!).Text}  ({Loc.T("grid.skill", "habilidade")} {sid})" : Loc.T("grid.skill", "habilidade");
            case FieldKind.TaskRef:
                if (IsEmpty(value)) return "";
                return int.TryParse(value, out var qid) && QuestName(qid) is { } qname
                    ? $"{qname}  ({Loc.T("grid.quest", "quest")} {qid})" : Loc.T("grid.quest", "quest");
            default:
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
        foreach (var r in _refs?.UsedBy(item.Id) ?? Array.Empty<RefIndex.Ref>())
        {
            string pct = r.Probability is not null && float.TryParse(r.Probability, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0 ? $"{p * 100:0.###}%" : "";
            var li = new ListViewItem(new[] { Pretty(r.Block.Name), r.Item.Id, r.Item.Name, FieldLabels.Label(r.Field), pct }, IconIndexFor(r.Item)) { Tag = r.Item };
            _links.Items.Add(li);
        }
        _links.EndUpdate();
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
        Status($"'{FieldLabels.Label(key)}' atualizado");
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
        if (block is null) { Status("Escolha um bloco primeiro"); return; }
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
        Status($"Novo item {code} criado em {Pretty(block.Name)} — preencha os campos à direita.");
    }

    private void CloneItem()
    {
        if (_current is null || CurrentItemBlock is not { } block) { Status("Selecione um item para clonar"); return; }
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
        Status($"Item clonado como código {code}.");
    }

    private void AddItem() => NewItem();

    private void RemoveItem()
    {
        if (_current is null || CurrentItemBlock is not { } block) { Status("Selecione um item"); return; }
        if (MessageBox.Show($"Excluir \"{_current.Name}\" (ID {_current.Id})?", "Excluir item",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _favorites.Remove(FavKey(block, _current)); SaveFavorites();
        _edits.RemoveItem(block, _current, string.Format(Loc.T("hist.deleteOne", "Excluir item {0}"), _current.Id)); _owner.Remove(_current);
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status("Item removido");
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
            _blockCombo.Items[0] = fav with { Label = $"★ Favoritos ({_favorites.Count})" };
        RefreshView();
        Status(_favorites.Contains(key) ? "Adicionado aos favoritos" : "Removido dos favoritos");
    }
    private static string FavFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "favorites.txt");
    private void SaveFavorites()
    { try { Directory.CreateDirectory(Path.GetDirectoryName(FavFile)!); File.WriteAllLines(FavFile, _favorites); } catch { } }
    private static IEnumerable<string> LoadFavorites()
    { try { return File.Exists(FavFile) ? File.ReadAllLines(FavFile) : Array.Empty<string>(); } catch { return Array.Empty<string>(); } }

    // ---------- tooltip estilo jogo ----------
    private Color _tipColor = Color.White;
    private void OnListHover(object? s, MouseEventArgs e)
    {
        var hit = _list.HitTest(e.Location).Item;
        int idx = hit?.Index ?? -1;
        if (idx == _hoverIdx) return;
        _hoverIdx = idx;
        if (idx < 0 || idx >= _view.Count) { _tip.Hide(_list); return; }
        var it = _view[idx];
        _tipLines = BuildTipLines(it);
        _tipColor = NameColor(it);
        _tip.Show(" ", _list, e.X + 22, e.Y + 16, 15000);
    }

    private void SizeTip(object? s, PopupEventArgs e)
    {
        using var f = new Font("Segoe UI", 9);
        int w = 120;
        foreach (var l in _tipLines) w = Math.Max(w, TextRenderer.MeasureText(l, f).Width);
        foreach (var d in _tipDrops) w = Math.Max(w, TextRenderer.MeasureText($"Item: {d.Text}  {d.Pct}", f).Width + 22);
        e.ToolTipSize = new Size(w + 22, _tipLines.Length * 18 + (_tipDrops.Count > 0 ? _tipDrops.Count * 20 + 8 : 0) + 16);
    }

    private void DrawTip(object? s, DrawToolTipEventArgs e)
    {
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(16, 17, 20)), e.Bounds);
        using (var pen = new Pen(Color.FromArgb(70, 74, 82))) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
        using var fName = new Font("Segoe UI Semibold", 9.5f);
        using var f = new Font("Segoe UI", 9);
        int y = 8;
        for (int i = 0; i < _tipLines.Length; i++)
        {
            var col = i == 0 ? _tipColor : Color.FromArgb(206, 211, 219);
            TextRenderer.DrawText(e.Graphics, _tipLines[i], i == 0 ? fName : f, new Point(10, y), col);
            y += 18;
        }
        if (_tipDrops.Count > 0) y += 6;
        foreach (var d in _tipDrops)
        {
            TextRenderer.DrawText(e.Graphics, "Item:", f, new Point(10, y + 1), Color.FromArgb(206, 211, 219));
            int x = 50;
            if (d.Icon is not null) e.Graphics.DrawImage(d.Icon, x, y, 18, 18);
            x += 22;
            var sz = TextRenderer.MeasureText(d.Text, f);
            TextRenderer.DrawText(e.Graphics, d.Text, f, new Point(x, y + 1), d.Color);
            TextRenderer.DrawText(e.Graphics, d.Pct, f, new Point(x + sz.Width + 4, y + 1), Color.FromArgb(232, 182, 78));
            y += 20;
        }
    }

    private static readonly string[] TipGroups = { "Básico", "Requisitos", "Ataque", "Defesa", "Durabilidade", "Economia" };
    private string[] BuildTipLines(Item it)
    {
        var lines = new List<string> { string.IsNullOrEmpty(it.Name) ? $"#{it.Id}" : it.Name, $"ID: {it.Id}" };
        foreach (var g in TipGroups)
            foreach (var (k, f) in it.Fields)
            {
                if (lines.Count >= 16) break;
                if (FieldLabels.Group(k) != g) continue;
                if (k is "ID" or "Name" || k.StartsWith("id_") || IsEmpty(f.Value) || FieldKinds.Of(k) != FieldKind.Plain) continue;
                lines.Add($"{FieldLabels.Label(k)}: {f.Value}");
            }
        if (FieldKinds.ClassNames(it.Fields.GetValueOrDefault("character_combo_id")?.Value ?? "") is { Length: > 0 } cls)
            lines.Add($"{Loc.T("tip.classes", "Classes")}: {cls}");

        // drops/itens referenciados com probabilidade (como o tooltip do Rody)
        _tipDrops.Clear();
        foreach (var (k, f) in it.Fields)
        {
            if (_tipDrops.Count >= 10) break;
            if (FieldKinds.Of(k) != FieldKind.ItemRef || IsEmpty(f.Value)) continue;
            var prob = FieldKinds.ProbabilityFor(k, it.Fields);
            if (prob is null || _refs?.Resolve(FieldKind.ItemRef, f.Value) is not { } hit) continue;
            if (!float.TryParse(it.Fields[prob].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) || p <= 0) continue;
            var ico = IconIndexFor(hit.Item);
            _tipDrops.Add((ico >= 0 ? _treeIcons.Images[ico] : null, hit.Item.Name, $"{p * 100:0.##}%", NameColor(hit.Item)));
        }
        return lines.ToArray();
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
    private readonly TextBox _qSearch = new() { Dock = DockStyle.Top, PlaceholderText = "  Pesquisar quest por nome ou ID…", BorderStyle = BorderStyle.FixedSingle };
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
        var editRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 4, Padding = new Padding(10, 6, 10, 6), BackColor = CSurface };
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        editRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        Button Btn(string t, Action a, bool accent = false)
        {
            var b = new Button { Text = t, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = accent ? CAccent : CSurface2, ForeColor = accent ? Color.White : CFg, Margin = new Padding(4, 0, 0, 0) };
            b.Click += (_, _) => a(); return b;
        }
        _qName.MaxLength = TasksFile.MaxNameLength;
        _qName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyQuestName(); } };
        editRow.Controls.Add(new Label { Text = "Nome", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CMuted }, 0, 0);
        editRow.Controls.Add(_qName, 1, 0);
        editRow.Controls.Add(Btn("Aplicar nome", ApplyQuestName, accent: true), 2, 0);
        editRow.Controls.Add(Btn("💾 Salvar tasks.data", SaveTasks), 3, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        // Dados
        var tData = new TabPage("Dados") { BackColor = CSurface };
        StyleQuestGrid();
        tData.Controls.Add(_qGrid);
        // Textos
        var tText = new TabPage("Textos") { BackColor = CSurface, Padding = new Padding(8) };
        var textLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = CSurface };
        for (int i = 0; i < 4; i++)
        {
            textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            textLayout.RowStyles.Add(new RowStyle(SizeType.Percent, i == 0 ? 40 : 20));
            textLayout.Controls.Add(new Label { Text = QuestNode.TextNames[i], Dock = DockStyle.Fill, ForeColor = CMuted, TextAlign = ContentAlignment.BottomLeft });
            int k = i;
            _qTexts[i].Leave += (_, _) => ApplyQuestText(k);
            textLayout.Controls.Add(_qTexts[i]);
        }
        textLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        textLayout.Controls.Add(new Label { Text = "Cores: ^RRGGBB antes do texto (ex.: ^ffcc00). Quebra de linha do jogo: \\r. As alterações valem ao sair do campo.", Dock = DockStyle.Fill, ForeColor = CMuted });
        tText.Controls.Add(textLayout);
        // Diálogos
        var tTalk = new TabPage("Diálogos") { BackColor = CSurface };
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
            if (!_qCur.Set(f, v)) { _qStatus.Text = $"Valor inválido para '{f.Name}'"; _qGrid.Rows[e.RowIndex].Cells[1].Value = _qCur.Get(f); return; }
            _qGrid.Rows[e.RowIndex].Cells[2].Value = QuestFieldHint(f, _qCur.Get(f));
            MarkTasksDirty($"'{f.Name}' alterado");
        };
    }

    // Texto auxiliar: nomes de item/monstro/NPC a partir do elements.data carregado.
    private string QuestFieldHint(TaskField f, string v)
    {
        if (v is "0" or "" || _refs is null) return "";
        var n = f.Name.ToLowerInvariant();
        FieldKind kind = n.Contains("npc") ? FieldKind.ItemRef : n.Contains("monstro") ? FieldKind.MonsterRef : n.Contains("item") ? FieldKind.ItemRef : FieldKind.Plain;
        if (n.StartsWith("quest") || n.Contains("quest anterior") || n.Contains("quest exclusiva") || n is "pai" or "irmão anterior" or "próximo irmão" or "primeiro filho" or "quest conjunta")
            return _tasks?.AllNodes().FirstOrDefault(x => x.Id.ToString() == v) is { } qq ? "→ " + TasksFile.SplitColor(qq.Name).Text : "";
        if (n.Contains("classe") && n.StartsWith("nº") is false && v.Length > 0 && n == "nº de classes") return "";
        return kind != FieldKind.Plain && _refs.Resolve(kind, v) is { } hit ? $"→ {hit.Item.Name}  [{Pretty(hit.Block.Name)}]" : "";
    }

    // tasks.data ao lado do elements.data (data\tasks.data)
    private void OpenTasksDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "tasks.data (*.data)|*.data|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadTasks(dlg.FileName);
    }

    private bool _tasksDirty;
    private void MarkTasksDirty(string msg) { _tasksDirty = true; _qStatus.Text = msg + " — clique em Salvar tasks.data para gravar."; }

    private void LoadTasks(string path)
    {
        try
        {
            _tasks = new TasksFile(path);
            _tasksPath = path; _tasksDirty = false;
            RefreshQuests();
            _qStatus.Text = _tasks.FullSchema
                ? $"{Path.GetFileName(path)} · versão {_tasks.Version} · {_tasks.Roots!.Count:N0} quests ({_tasks.AllNodes().Count():N0} com subquests) · edição completa"
                : $"{Path.GetFileName(path)} · versão {_tasks.Version} · {_tasks.Quests.Count:N0} quests · só nomes (esquema desta versão ainda não suportado{(_tasks.SchemaError is null ? "" : ": " + _tasks.SchemaError)})";
        }
        catch (Exception ex) { _tasks = null; _qStatus.Text = "Falha ao ler tasks.data: " + ex.Message; }
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
        TextRenderer.DrawText(e.Graphics, (changed ? "● " : "") + (text.Length == 0 ? "(sem nome)" : text), _qTree.Font, new Rectangle(r.X + 64, r.Y, r.Width - 66, r.Height), fg, flags);
    }

    private void ShowQuest(object? tag)
    {
        _qCur = tag as QuestNode;
        string name = tag switch { QuestNode q => q.Name, QuestEntry qe => qe.Name, _ => "" };
        int id = tag switch { QuestNode q => q.Id, QuestEntry qe => qe.Id, _ => 0 };
        var (text, col) = TasksFile.SplitColor(name);
        _qInfo.Text = $"{(text.Length == 0 ? "(sem nome)" : text)}     (ID {id})" + (_qCur?.Children.Count > 0 ? $"   ·   {_qCur.Children.Count} subquest(s)" : "");
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
                int hr = _qGrid.Rows.Add("  " + group.ToUpperInvariant(), "", "");
                var hrow = _qGrid.Rows[hr]; hrow.ReadOnly = true;
                hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                hrow.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            var v = _qCur.Get(f);
            int r = _qGrid.Rows.Add("  " + f.Name, v, QuestFieldHint(f, v));
            _qGrid.Rows[r].Tag = f;
            if (!f.Editable) { _qGrid.Rows[r].ReadOnly = true; _qGrid.Rows[r].Cells[1].Style.ForeColor = CMuted; }
        }
        // Textos
        for (int i = 0; i < 4; i++) _qTexts[i].Text = _qCur.Texts[i].Replace("\r", "\r\n");
        // Diálogos
        for (int i = 0; i < 5; i++)
        {
            var tp = _qCur.Talks[i];
            var tn = new TreeNode($"{QuestNode.TalkNames[i]}  ({tp.Windows.Count})") { Tag = tp };
            foreach (var w in tp.Windows)
            {
                var wn = new TreeNode($"Janela {w.Id}: {Trunc(w.Text, 50)}") { Tag = w };
                foreach (var o in w.Options) wn.Nodes.Add(new TreeNode($"→ {Trunc(o.Text, 50)}  (vai p/ {o.Id})") { Tag = o });
                tn.Nodes.Add(wn);
            }
            _qTalkTree.Nodes.Add(tn);
        }
        _qTalkTree.ExpandAll();
        if (_qTalkTree.Nodes.Count > 0) _qTalkTree.Nodes[0].EnsureVisible();
    }

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
        MarkTasksDirty("Nome alterado");
    }

    private void ApplyQuestText(int i)
    {
        if (_qCur is null) return;
        var v = _qTexts[i].Text.Replace("\r\n", "\r");
        if (v == _qCur.Texts[i]) return;
        _qCur.Texts[i] = v; _qCur.TextsChanged = true;
        _qTree.Invalidate();
        MarkTasksDirty($"{QuestNode.TextNames[i]} alterado");
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
            sn.Text = _qTalkSel switch { TalkWindow w => $"Janela {w.Id}: {Trunc(w.Text, 50)}", TalkOption o => $"→ {Trunc(o.Text, 50)}  (vai p/ {o.Id})", _ => sn.Text };
        _qTree.Invalidate();
        MarkTasksDirty("Diálogo alterado");
    }

    private void SaveTasks()
    {
        _qGrid.EndEdit();
        if (_tasks is null || _tasksPath is null) { _qStatus.Text = "Abra um tasks.data primeiro."; return; }
        if (!_tasks.CanWrite) { _qStatus.Text = "Somente leitura: " + _tasks.ReadOnlyReason; return; }
        ApplyTalkText();
        for (int i = 0; i < 4; i++) ApplyQuestText(i);
        if (!_tasksDirty) { _qStatus.Text = "Nada alterado."; return; }
        if (MessageBox.Show($"Gravar as alterações em:\n{_tasksPath}\n\nUma cópia de segurança será criada ao lado.\nLembre: o servidor precisa do MESMO tasks.data (aba Verificação).",
                "Salvar tasks.data", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            var keep = _qCur?.Id;
            var backup = _tasks.Save(_tasksPath);
            LoadTasks(_tasksPath);
            _qStatus.Text = $"Salvo · backup: {Path.GetFileName(backup)}";
            if (keep is { } id) foreach (TreeNode n in _qTree.Nodes) if (n.Tag is QuestNode qn && qn.Id == id) { _qTree.SelectedNode = n; break; }
        }
        catch (Exception ex) { Error("Falha ao salvar tasks.data", ex); }
    }

    // ---------- aba Verificação (par cliente × servidor) ----------
    private readonly TextBox _chkClient = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _chkServer = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _chkList = new() { View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Label _chkSummary = new() { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 11), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };

    private Control BuildCheckView()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = CBg, Padding = new Padding(16) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 118, ColumnCount = 3, BackColor = CBg };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        Label L(string t) => new() { Text = t, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CFg };
        Button B(string t, Action a) { var b = new Button { Text = t, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = CSurface, ForeColor = CFg }; b.Click += (_, _) => a(); return b; }
        _chkClient.Dock = DockStyle.Fill; _chkServer.Dock = DockStyle.Fill;
        _chkClient.Text = LoadSetting("check_client") ?? "";
        _chkServer.Text = LoadSetting("check_server") ?? "";
        form.Controls.Add(L("Cliente (pasta element)"), 0, 0); form.Controls.Add(_chkClient, 1, 0);
        form.Controls.Add(B("Procurar…", () => PickFolder(_chkClient, "Pasta element do cliente")), 2, 0);
        form.Controls.Add(L("Servidor (pasta home)"), 0, 1); form.Controls.Add(_chkServer, 1, 1);
        form.Controls.Add(B("Procurar…", () => PickFolder(_chkServer, "Pasta home do servidor (contém gamed, glinkd…)")), 2, 1);
        var run = B("✔ Verificar", RunCheck); run.BackColor = CAccent; run.ForeColor = Color.White; run.Font = new Font("Segoe UI Semibold", 10);
        form.Controls.Add(new Label { Text = "Compara versões e timestamps que o login exige iguais (elements, tasks, loja), cópias por mapa e a criptografia de rede (sem mostrar as chaves).", Dock = DockStyle.Fill, ForeColor = CMuted, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        form.SetColumnSpan(form.GetControlFromPosition(0, 2)!, 2);
        form.Controls.Add(run, 2, 2);
        for (int i = 0; i < 3; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _chkList.Columns.Add("", 34);
        _chkList.Columns.Add("Item", 300);
        _chkList.Columns.Add("Cliente", 170);
        _chkList.Columns.Add("Servidor", 170);
        _chkList.Columns.Add("O que significa", 520);
        _chkList.BackColor = CSurface; _chkList.ForeColor = CFg;
        _chkSummary.ForeColor = CFg;

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), BackColor = CBg };
        listHost.Controls.Add(_chkList);
        listHost.Controls.Add(_chkSummary);
        root.Controls.Add(listHost);
        root.Controls.Add(form);
        return root;
    }

    private void PickFolder(TextBox target, string title)
    {
        using var dlg = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, SelectedPath = target.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    private void RunCheck()
    {
        if (_chkClient.Text.Length == 0 && _dataPath is not null)
            _chkClient.Text = Path.GetDirectoryName(Path.GetDirectoryName(_dataPath)) ?? "";
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status("Escolha as pastas do cliente e do servidor."); return; }
        SaveSetting("check_client", _chkClient.Text); SaveSetting("check_server", _chkServer.Text);
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
        _chkSummary.Text = err > 0 ? $"✖ {err} erro(s) que impedem o login · {warn} aviso(s)"
                         : warn > 0 ? $"⚠ Sem erros de login · {warn} aviso(s)" : "✔ Cliente e servidor compatíveis";
        _chkSummary.ForeColor = err > 0 ? Color.FromArgb(200, 30, 30) : warn > 0 ? Color.FromArgb(180, 110, 0) : Color.FromArgb(22, 128, 61);
        Status("Verificação concluída");
    }

    // ---------- aba Extrator PCK (invólucro do sPCK) ----------
    private Label _lblSpck = null!, _lblIcons = null!;
    private Control BuildPckView()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = CBg, Padding = new Padding(20) };
        int y = 10;
        Label Title(string t) { var l = new Label { Text = t, Location = new Point(14, y), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = CFg }; y += 34; return l; }
        Label Info(string t) { var l = new Label { Text = t, Location = new Point(14, y), AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = CMuted }; y += l.PreferredHeight + 10; return l; }
        Button Btn(string t, Action a) { var b = new Button { Text = t, Location = new Point(14, y), Size = new Size(260, 30), FlatStyle = FlatStyle.Flat, BackColor = CSurface, ForeColor = CFg }; b.Click += (_, _) => a(); y += 40; return b; }

        p.Controls.Add(Title("Pacotes .pck"));
        p.Controls.Add(Info("Os ícones dos itens agora são lidos DIRETO do surfaces.pck, automaticamente, ao abrir a pasta do Perfect World — não precisa extrair nada. As opções abaixo (sPCK + pasta extraída) ficam só como alternativa, caso algum ícone não apareça."));
        p.Controls.Add(Btn("1) Localizar o sPCK (sPCK-GUI.exe)", LocateSpck));
        _lblSpck = new Label { Location = new Point(290, y - 34), AutoSize = true, ForeColor = CMuted, MaximumSize = new Size(520, 0) };
        p.Controls.Add(_lblSpck);
        p.Controls.Add(Btn("2) Abrir o sPCK para extrair", OpenSpck));
        p.Controls.Add(Info("No sPCK, extraia o .pck dos ícones (ex.: interfaces.pck / o pacote de ícones) para uma pasta."));
        p.Controls.Add(Btn("3) Definir a pasta de ícones extraída", SetIconsFolder));
        _lblIcons = new Label { Location = new Point(290, y - 34), AutoSize = true, ForeColor = CMuted, MaximumSize = new Size(520, 0) };
        p.Controls.Add(_lblIcons);
        p.Controls.Add(Info("Pronto: ao selecionar um item na aba Elements, o ícone aparece ao lado do nome (formatos PNG/BMP/TGA)."));
        RefreshPckLabels();
        return p;
    }

    private void RefreshPckLabels()
    {
        _lblSpck.Text = LoadSetting("spck") ?? "(não definido — ou deixe que eu procuro no padrão)";
        _lblIcons.Text = LoadSetting("icons") ?? "(não definida)";
    }
    private void LocateSpck()
    {
        using var dlg = new OpenFileDialog { Filter = "sPCK (*.exe)|*.exe", Title = "Selecione o sPCK-GUI.exe" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { SaveSetting("spck", dlg.FileName); RefreshPckLabels(); }
    }
    private void OpenSpck()
    {
        var path = LoadSetting("spck") ?? DefaultSpck();
        if (path is null || !File.Exists(path)) { Status("sPCK não encontrado — use 'Localizar o sPCK'."); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! }); }
        catch (Exception ex) { Error("Falha ao abrir o sPCK", ex); }
    }
    private static string? DefaultSpck()
    {
        foreach (var c in new[] { @"D:\Perfect World\PW\Outros Editores\sPCK GUI\sPCK-GUI.exe" })
            if (File.Exists(c)) return c;
        return null;
    }
    private void SetIconsFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pasta com os ícones extraídos do .pck" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SaveSetting("icons", dlg.SelectedPath); RefreshPckLabels();
            _iconsRoot = dlg.SelectedPath; _iconIdx.Clear(); _treeIcons.Images.Clear();
            RefreshView();
            Status("Pasta de ícones definida — ícones ativados na lista.");
        }
    }

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
