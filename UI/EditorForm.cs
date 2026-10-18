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
    private bool _loadingElements, _bindingElements, _buildingTypes, _updatingView, _updatingDetails, _updatingReferences;
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 180 };
    private readonly ToolStripMenuItem _autoLoadLast = new("启动时自动读取上次文件") { CheckOnClick = true };
    private TabControl _mainTabs = null!;
    private TabPage _questPage = null!;
    private bool _saving;

    private readonly ModuleStore _store = new();
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "  按名称或 ID 搜索物品…", BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox _blockCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _typeCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
    private readonly CheckBox _searchAll = new() { Dock = DockStyle.Right, Width = 22, Padding = new Padding(6, 0, 0, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false, MultiSelect = true, OwnerDraw = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Clickable };
    private readonly NumericUpDown _gotoId = new() { Maximum = 999999999, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
    private int _hoverIdx = -1;
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Microsoft YaHei UI", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly ToolStripStatusLabel _status = new("请打开《完美世界》文件夹以开始使用。");
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
    private readonly RichTextBox _descBox = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10.5f) };
    private readonly ComboBox _findField = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, DropDownWidth = 620 };
    private readonly ComboBox _findOp = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly TextBox _findValue = new() { Width = 160, BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _findList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None };
    private readonly Label _idLabel = new() { Dock = DockStyle.Left, Width = 92, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 11) };
    private readonly List<Item> _history = new();
    private int _histPos = -1;
    private bool _navigating;
    private readonly ToolStripStatusLabel _stTotal = new() { Spring = false }, _stBlock = new(), _stSel = new();
    private readonly ListView _links = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, HideSelection = false };
    private readonly TabControl _detailTabs = new() { Dock = DockStyle.Fill };
    private TabPage _tabLinks = null!;
    private readonly ContextMenuStrip _ctx = new();
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

    public EditorForm()
    {
        Text = "MyAceEditor 简体中文版";
        Width = 1320; Height = 840;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        BackColor = CBg; ForeColor = CFg;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        Controls.Add(BuildTabs());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatus());
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); if (!_updatingReferences) RefreshView(); };
        FormClosed += (_, _) =>
        {
            _searchDelay.Dispose(); _questSearchDelay.Dispose();
            _questSearchCts?.Cancel(); _questDetailCts?.Cancel();
            _questViewRevision++; _questDetailRevision++;
            _questGroupFont.Dispose(); _iconBox.Image?.Dispose();
        };

        Shown += (_, _) =>
        {
            if (_split is not null) _split.SplitterDistance = 430;
            // 默认手动打开；关闭自动读取时，不访问上次记录的数据文件。
            if (!_autoLoadLast.Checked) return;
            var last = LoadLast();
            if (last is not null && File.Exists(last)) LoadElements(last);
        };
    }

    // ---------- topo ----------
    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = CSurface, ForeColor = CFg, Padding = new Padding(4, 2, 0, 2) };
        ToolStripMenuItem M(string t) => new(t) { ForeColor = CFg };
        var arq = M("文件(&F)");
        arq.DropDownItems.Add("打开游戏文件夹(&P)…", null, (_, _) => OpenPwFolder());
        arq.DropDownItems.Add("打开 elements.data(&E)…", null, (_, _) => OpenElements());
        arq.DropDownItems.Add("打开元素数据（手动选择 CFG）…", null, (_, _) => OpenElementsWithCfg());
        _autoLoadLast.Checked = LoadSetting("auto_load_last") == "1";
        _autoLoadLast.CheckedChanged += (_, _) =>
        {
            SaveSetting("auto_load_last", _autoLoadLast.Checked ? "1" : "0");
            Status(_autoLoadLast.Checked ? "已开启启动自动读取，下次启动时生效。" : "已取消启动自动读取，下次启动请手动打开文件。");
        };
        arq.DropDownItems.Add(_autoLoadLast);
        arq.DropDownItems.Add(new ToolStripSeparator());
        arq.DropDownItems.Add(new ToolStripMenuItem("保存(&S)", null, (_, _) => SaveAs()) { ShortcutKeys = Keys.Control | Keys.S });
        arq.DropDownItems.Add("另存为(&A)…", null, (_, _) => SaveElementsAs());
        arq.DropDownItems.Add("退出(&X)", null, (_, _) => Close());
        var ed = M("编辑(&E)");
        ed.DropDownItems.Add("新建物品(&N)", null, (_, _) => AddItem());
        ed.DropDownItems.Add("删除物品(&D)", null, (_, _) => RemoveItem());
        var aj = M("帮助(&H)");
        aj.DropDownItems.Add("读取报告…", null, (_, _) => ShowElementsReport());
        aj.DropDownItems.Add("关于", null, (_, _) => ChineseDialogs.Show(this, "MyAceEditor 简体中文版 —《完美世界》数据编辑器。", "关于"));
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, aj });
        MainMenuStrip = menu;
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var tb = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = CSurface2, ForeColor = CFg, Renderer = new LightRenderer(), Padding = new Padding(6, 3, 6, 3) };
        ToolStripButton B(string t, Action a) { var b = new ToolStripButton(t) { ForeColor = CFg, Font = new Font("Microsoft YaHei UI", 9.5f) }; b.Click += (_, _) => a(); return b; }
        tb.Items.Add(B("◀", () => Navigate(-1)));
        tb.Items.Add(B("▶", () => Navigate(+1)));
        tb.Items[0].ToolTipText = "返回（上一个物品）"; tb.Items[1].ToolTipText = "前进";
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B("打开游戏文件夹", OpenPwFolder));
        tb.Items.Add(B("打开 elements.data", OpenElements));
        tb.Items.Add(B("保存", SaveAs));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B("＋ 添加", AddItem));
        tb.Items.Add(B("🗑 删除", RemoveItem));
        return tb;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(130, 30) };
        _mainTabs = tabs;
        tabs.DrawItem += (s, e) =>
        {
            var tp = tabs.TabPages[e.Index]; bool sel = e.Index == tabs.SelectedIndex;
            using var background = new SolidBrush(sel ? CBg : CSurface2);
            using var accent = new SolidBrush(CAccent);
            using var font = new Font("Microsoft YaHei UI", 10, sel ? FontStyle.Bold : FontStyle.Regular);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (sel) e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3));
            TextRenderer.DrawText(e.Graphics, tp.Text, font,
                e.Bounds, sel ? CAccent : CMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        var tEl = new TabPage("元素数据") { BackColor = CBg };
        tEl.Controls.Add(BuildElements());
        tabs.TabPages.Add(tEl);
        var tQ = new TabPage("任务") { BackColor = CBg };
        _questPage = tQ;
        tQ.Controls.Add(BuildQuestView());
        tabs.TabPages.Add(tQ);
        foreach (var (n, h) in new[] { ("商城", "商城编辑器"), ("AI 策略", "AI 编辑器") })
        {
            var tp = new TabPage(n) { BackColor = CBg };
            tp.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = CMuted, Text = $"{h}\n（尚未实现）", Font = new Font("Microsoft YaHei UI", 11) });
            tabs.TabPages.Add(tp);
        }
        var tChk = new TabPage("一致性检查") { BackColor = CBg };
        tChk.Controls.Add(BuildCheckView());
        tabs.TabPages.Add(tChk);
        var tPck = new TabPage("PCK 解包工具") { BackColor = CBg };
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
            cb.FlatStyle = FlatStyle.Flat; cb.BackColor = CTreeInput; cb.ForeColor = CTreeFg; cb.Font = new Font("Microsoft YaHei UI", 10);
            cb.MaxDropDownItems = 24;
            cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = 22;
            cb.DrawItem += DrawDarkCombo;
        }
        _blockCombo.SelectedIndexChanged += (_, _) => OnBlockChanged();
        _typeCombo.SelectedIndexChanged += (_, _) => { if (!_buildingTypes && !_bindingElements) RefreshView(); };

        var searchRow = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 4, 0, 0), BackColor = CTreeBg };
        _search.Dock = DockStyle.Fill; _search.BackColor = CTreeInput; _search.ForeColor = CTreeFg; _search.Font = new Font("Microsoft YaHei UI", 10);
        _search.TextChanged += (_, _) => { _searchDelay.Stop(); if (!_bindingElements) _searchDelay.Start(); };
        _searchAll.ForeColor = CTreeFg; _searchAll.BackColor = CTreeBg;
        _searchAll.CheckedChanged += (_, _) => RefreshView();
        _tip.SetToolTip(_searchAll, "搜索所有数据块");
        searchRow.Controls.Add(_search);
        searchRow.Controls.Add(_searchAll);

        _list.BackColor = CTreeBg; _list.ForeColor = CTreeFg; _list.Font = new Font("Microsoft YaHei UI", 10);
        _list.SmallImageList = _treeIcons;
        _list.Columns.Add("ID", 70, HorizontalAlignment.Right);
        _list.Columns.Add("名称", 240);
        _list.Columns.Add("∞", 58, HorizontalAlignment.Right);
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
        _tip.SetToolTip(_gotoId, "跳转到指定 ID（按 Enter）");
        foot.Controls.Add(FootBtn("＋ 添加", AddItem), 0, 0);
        foot.Controls.Add(_gotoId, 1, 0);
        foot.Controls.Add(FootBtn("✕ 删除", RemoveItem), 2, 0);

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
        var tVal = new TabPage("字段值") { BackColor = CSurface };
        tVal.Controls.Add(_grid);
        tVal.Controls.Add(_groupTabs);
        _groupTabs.SelectedIndexChanged += (_, _) => { if (!_updatingDetails && _current is not null) FillGrid(_current); };
        // Buscar por campo (no bloco atual)
        var tFind = new TabPage("查找") { BackColor = CSurface };
        var findBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0), BackColor = CSurface };
        _findOp.Items.AddRange(new object[] { "=", "≠", ">", "<", "包含" }); _findOp.SelectedIndex = 0;
        var findBtn = new Button { Text = "查找", Height = 26, FlatStyle = FlatStyle.Flat, BackColor = CAccent, ForeColor = Color.White };
        findBtn.Click += (_, _) => RunFind();
        _findValue.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunFind(); } };
        findBar.Controls.AddRange(new Control[] { new Label { Text = "字段", AutoSize = true, Padding = new Padding(0, 6, 0, 0), ForeColor = CMuted }, _findField, _findOp, _findValue, findBtn });
        _findList.Columns.Add("ID", 70, HorizontalAlignment.Right); _findList.Columns.Add("名称", 300); _findList.Columns.Add("值", 200);
        _findList.SmallImageList = _treeIcons; _findList.BackColor = CSurface; _findList.ForeColor = CFg;
        _findList.DoubleClick += (_, _) => { if (_findList.SelectedItems.Count > 0 && _findList.SelectedItems[0].Tag is Item it) GoToItem(it); };
        tFind.Controls.Add(_findList); tFind.Controls.Add(findBar);
        // Descrição e cor (item_ext_desc.txt do configs.pck)
        var tDesc = new TabPage("描述与颜色") { BackColor = CTreeBg, Padding = new Padding(12) };
        _descBox.BackColor = CTreeBg; _descBox.ForeColor = CTreeFg;
        tDesc.Controls.Add(_descBox);
        _tabLinks = new TabPage("关联引用") { BackColor = CSurface };
        _links.Columns.Add("数据块", 170); _links.Columns.Add("ID", 70, HorizontalAlignment.Right);
        _links.Columns.Add("名称", 280); _links.Columns.Add("字段", 220); _links.Columns.Add("%", 70, HorizontalAlignment.Right);
        _links.BackColor = CSurface; _links.ForeColor = CFg; _links.SmallImageList = _treeIcons;
        _links.DoubleClick += (_, _) => { if (_links.SelectedItems.Count > 0 && _links.SelectedItems[0].Tag is Item it) GoToItem(it); };
        _tabLinks.Controls.Add(_links);
        _detailTabs.TabPages.Add(tVal);
        _detailTabs.TabPages.Add(_tabLinks);
        _detailTabs.TabPages.Add(tFind);
        _detailTabs.TabPages.Add(tDesc);
        _detailTabs.SelectedIndexChanged += (_, _) => { if (_detailTabs.SelectedTab == _tabLinks && _current is not null && EnsureReferenceIndex()) FillLinks(_current); };
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
        _grid.Columns[0].Name = "字段"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 230;
        _grid.Columns[0].DefaultCellStyle.ForeColor = CMuted;
        _grid.Columns[1].Name = "值"; _grid.Columns[1].Width = 260;
        _grid.Columns[2].Name = "关联"; _grid.Columns[2].ReadOnly = true; _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
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
        _grid.Columns[1].DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5f);
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
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
        using var dlg = new FolderBrowserDialog { Description = "请选择《完美世界》文件夹（其中包含 element 文件夹）", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string root = dlg.SelectedPath;
        try
        {
            var found = ReadProgressDialog.Run(this, "查找元素数据", context =>
                new LocatedFile(FindFile(root, "elements.data", context)));
            if (found.Path is null) { Status("在此文件夹中未找到 elements.data。"); return; }
            LoadElements(found.Path);
        }
        catch (OperationCanceledException) { Status("文件查找已取消，原数据已保留。"); }
        catch (Exception ex) { Error("查找元素数据失败", ex); }
    }

    private sealed record LocatedFile(string? Path);
    private static string? FindFile(string root, string name, LoadContext context)
    {
        context.Report(0, "查找元素数据", root, indeterminate: true, force: true);
        foreach (var candidate in new[] { name, Path.Combine("element", "data", name), Path.Combine("data", name), Path.Combine("element", name) })
        {
            context.Check();
            string path = Path.Combine(root, candidate);
            if (File.Exists(path)) return path;
        }
        var pending = new Stack<string>(); pending.Push(root);
        int visited = 0;
        while (pending.Count > 0)
        {
            context.Check();
            string directory = pending.Pop();
            context.Report(0, "查找元素数据", $"已检查 {++visited:N0} 个文件夹 · {directory}", indeterminate: true);
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    context.Check();
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else if (Path.GetFileName(entry).Equals(name, StringComparison.OrdinalIgnoreCase)) return entry;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private void OpenElements()
    {
        using var dlg = new OpenFileDialog { Filter = "完美世界元素数据 (*.data)|*.data|所有文件 (*.*)|*.*", Title = "打开元素数据（elements.data）" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadElements(dlg.FileName);
    }

    private bool LoadElements(string path, string? cfgOverride = null, bool preserveSession = false)
    {
        if (_loadingElements) { Status("正在读取，请先完成或取消当前操作。"); return false; }
        _grid.EndEdit();
        bool resumeSearch = _searchDelay.Enabled, adopted = false, succeeded = false;
        _loadingElements = true; _searchDelay.Stop();
        string? retryCfg = null;
        try
        {
            // 捕获旧会话供只读复用；工作线程不读取或修改任何控件。
            var oldAtlas = _atlas; var oldAtlasPath = _atlasPck;
            var oldTasks = _tasks; var oldTasksPath = _tasksPath;
            var oldConfigs = _cfgs;
            string? preferredBlock = LoadSetting("block");
            var loaded = ReadProgressDialog.Run(this, "读取元素数据", context =>
                ElementsLoadBundle.Read(path, cfgOverride, context, oldAtlas, oldAtlasPath, oldTasks, oldTasksPath, preferredBlock, preserveSession, oldConfigs));
            var match = loaded.Match;
            var reader = match.Reader;
            var module = match.Module;
            _bindingElements = true;
            try
            {
                _current = null; _curBlock = null; _list.SelectedIndices.Clear();
                _list.VirtualListSize = 0; _view.Clear(); _history.Clear(); _histPos = -1;
                _grid.Rows.Clear(); _descBox.Clear(); _header.Text = "（未选择）"; _idLabel.Text = "";
                _iconBox.Image?.Dispose(); _iconBox.Image = null;
                _iconIdx.Clear(); _treeIcons.Images.Clear();
                module.Name = Path.GetFileName(path);
                _store.Adopt(module); adopted = true;
                _owner = loaded.Owners; _refs = loaded.References;
                _atlas = loaded.Atlas; _atlasPck = loaded.AtlasPath; _cfgs = loaded.Configs;
                _grid.ReadOnly = module.Source is not ElementsLayout { CanWrite: true };
                _elementsReport = match.Report;
                if (loaded.Warnings.Count > 0) _elementsReport += "\n\n附加资源提示：\n" + string.Join("\n", loaded.Warnings);
                if (!ReferenceEquals(_tasks, loaded.Tasks))
                {
                    ResetQuestSession();
                    _tasks = loaded.Tasks; _tasksPath = loaded.TasksPath; _tasksDirty = false;
                }
                else
                {
                    _questSelectionAfterLoad = _selectedQuest is null ? null : TaskIndex.IdOf(_selectedQuest);
                    ResetQuestDetails();
                }
                _qStatus.Text = _tasks is null ? "同目录没有可加载的任务文件。" : TaskLoadSummary(_tasks, loaded.TaskCount);
                _dataPath = path;
                BuildTree(ownersReady: true);
                SaveLast(path);
            }
            finally { _bindingElements = false; }
            OnBlockChanged();
            RefreshQuests();
            var result = match.Result;
            Status($"v{reader.Version} · {Path.GetFileName(match.ConfigPath)} · {ElementsFormatLabels.Label(result.Format)} · " +
                $"{result.BlocksRead}/{result.TotalBlocks} 个数据块 · {result.Records:N0} 条记录 · " +
                (result.Complete ? "完整读取" : $"未完整读取（{result.Position}/{result.Length} 字节）") +
                (_grid.ReadOnly ? " · 只读（详见帮助→读取报告）" : " · 写回校验通过"));
            _status.ToolTipText = _elementsReport;
            succeeded = true;
        }
        catch (OperationCanceledException) { Status(_store.Current is null ? "读取已取消。" : "读取已取消，原来已打开的数据已保留。"); }
        catch (CfgChoiceRequiredException ex)
        {
            using var choice = new Form { Text = "选择对应的游戏分支 CFG", Width = 720, Height = 190,
                StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false };
            var box = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Value" };
            foreach (var file in ex.Paths) box.Items.Add(new KeyValuePair<string, string>(file, Path.GetFileName(file)));
            box.SelectedIndex = 0;
            var ok = new Button { Text = "使用此 CFG", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
            choice.Controls.Add(box); choice.Controls.Add(new Label { Text = ex.Message, Dock = DockStyle.Top, Height = 42 }); choice.Controls.Add(ok);
            choice.AcceptButton = ok;
            if (choice.ShowDialog(this) == DialogResult.OK && box.SelectedItem is KeyValuePair<string, string> selected) retryCfg = selected.Key;
        }
        catch (Exception ex)
        {
            _elementsReport += "\n\n最近一次加载失败：" + ex.Message;
            Error("读取 elements.data 失败（可用“手动选择 CFG”查看）", ex);
        }
        finally
        {
            _loadingElements = false; UseWaitCursor = false;
            if (!adopted && resumeSearch) _searchDelay.Start();
        }
        return retryCfg is not null ? LoadElements(path, retryCfg, preserveSession) : succeeded;
    }

    private bool EnsureReferenceIndex()
    {
        var refs = _refs;
        if (refs is null || refs.IsPrepared) return true;
        if (_updatingReferences) return false;
        _updatingReferences = true; _searchDelay.Stop();
        try
        {
            ReadProgressDialog.Run(this, "更新引用索引", context => { refs.Prepare(context); return refs; });
            _list.Invalidate(); return true;
        }
        catch (OperationCanceledException) { Status("引用索引更新已取消，可重新选择物品后重试。"); return false; }
        catch (Exception ex) { Error("建立引用索引失败", ex); return false; }
        finally { _updatingReferences = false; }
    }

    private Image? IconFor(Item it)
    {
        if (!it.Fields.TryGetValue("file_icon", out var fi) || string.IsNullOrEmpty(fi.Value)) return null;
        Image? img = null;
        try { img = _atlas?.Get(fi.Value); } catch { }
        if (img is null && _iconsRoot is not null)
            try { img = IconLoader.Load(_iconsRoot, fi.Value); } catch { }
        return img;
    }

    private string _elementsReport = "尚未打开元素数据。";

    private void OpenElementsWithCfg()
    {
        using var data = new OpenFileDialog { Filter = "元素数据 (*.data)|*.data|所有文件 (*.*)|*.*", Title = "选择 elements.data" };
        if (data.ShowDialog(this) != DialogResult.OK) return;
        using var cfg = new OpenFileDialog { Filter = "CFG 定义 (*.cfg)|*.cfg", Title = "选择与此数据版本对应的 CFG",
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg") };
        if (cfg.ShowDialog(this) == DialogResult.OK) LoadElements(data.FileName, cfg.FileName);
    }

    private void ShowElementsReport()
    {
        using var dialog = new Form { Text = "元素数据读取报告", Width = 900, Height = 560,
            StartPosition = FormStartPosition.CenterParent, Font = Font };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, WordWrap = false, Text = _elementsReport.Replace("\n", Environment.NewLine) });
        dialog.ShowDialog(this);
    }

    private bool CanEditElements()
    {
        if (_loadingElements) { Status("正在读取，请先完成或取消当前操作。"); return false; }
        if (_store.Current?.Source is ElementsLayout { CanWrite: true }) return true;
        Status(_store.Current?.Source is ElementsLayout layout ? "只读：" + layout.ReadOnlyReason : "请先打开可编辑的元素数据。");
        return false;
    }

    // Salvar: grava o elements.data no próprio arquivo (backup com data/hora + escrita atômica).
    private void SaveAs() { if (_mainTabs.SelectedTab == _questPage) SaveTasks(); else SaveElements(saveAs: false); }
    private void SaveElementsAs() { if (_mainTabs.SelectedTab == _questPage) SaveTaskData(saveAs: true); else SaveElements(saveAs: true); }
    private sealed record SaveReceipt(string Path, string? Backup);

    private void SaveElements(bool saveAs)
    {
        if (_saving) return;
        if (_store.Current?.Source is not ElementsLayout || _dataPath is null) { Status("请先打开 elements.data"); return; }
        if (!CanEditElements())
        {
            ChineseDialogs.Show(this, (_store.Current.Source as ElementsLayout)?.ReadOnlyReason ?? "当前数据不可编辑。", "当前元素数据不能保存");
            return;
        }
        if (!_grid.EndEdit()) { Status("请先修正当前单元格的值，再保存。"); return; }
        var target = _dataPath;
        if (saveAs)
        {
            using var dlg = new SaveFileDialog { Filter = "元素数据 (*.data)|*.data", Title = "另存为元素数据", FileName = Path.GetFileName(_dataPath), InitialDirectory = Path.GetDirectoryName(_dataPath) };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            target = dlg.FileName;
        }
        else if (!ChineseDialogs.Confirm(this, $"将修改保存到：\n{target}\n\n将在同一目录中为当前文件创建备份。",
                     "保存 elements.data")) return;
        SaveReceipt? saved = null;
        try
        {
            _saving = true;
            string? editedId = _current?.Fields.FirstOrDefault(pair => pair.Key.Equals("ID", StringComparison.OrdinalIgnoreCase)).Value?.Value;
            string selectedId = editedId is not null && long.TryParse(editedId, out var numericId)
                ? numericId.ToString(System.Globalization.CultureInfo.InvariantCulture) : editedId ?? _current?.Id ?? "";
            (string? Block, string Id)? keep = _current is null ? null : (BlockContaining(_current)?.Name, selectedId);
            var module = _store.Current;
            string? configPath = (module.Source as ElementsLayout)?.ConfigPath;
            saved = ReadProgressDialog.Run(this, "保存元素数据", context =>
                new SaveReceipt(Path.GetFullPath(target), ElementsWriter.Save(module, target, context)), allowCancel: false, completedStage: "保存完成");
            _dataPath = saved.Path; SaveLast(saved.Path);
            bool refreshed = LoadElements(saved.Path, configPath, preserveSession: true);
            if (refreshed && keep is { } k && _store.Current?.FindListByName(k.Block ?? "")?.FindById(k.Id) is { } again)
            { _current = again; SelectItem(again); ShowItem(again); }
            Status($"磁盘已保存：{saved.Path}" + (saved.Backup is null ? "" : $" · 备份：{Path.GetFileName(saved.Backup)}") +
                (refreshed ? "" : " · 界面重读未完成，可手动重新打开；内存修改仍保留。"));
        }
        catch (Exception ex) { Error(saved is null ? "保存失败，内存修改仍保留" : "磁盘已保存，界面更新未完成", ex); }
        finally { _saving = false; UseWaitCursor = false; }
    }

    // ---------- lista de itens (estilo Rody: seletor de bloco + lista plana) ----------
    // Entrada do seletor de bloco. Block == null => Favoritos.
    private sealed record BlockEntry(ItemList? Block, string Label, bool Header = false)
    {
        public override string ToString() => Label;
    }

    // Categoria do bloco para organizar o seletor (como os grupos do Rody).
    private static string BlockCategory(string n) =>
        n.Contains("MONSTER") || n.StartsWith("NPC") || n.Contains("MINE") || n == "TALK_PROC" ? "怪物、NPC 与采集物"
        : n.Contains("PET") ? "宠物"
        : n.Contains("RECIPE") || n.Contains("PRODUCE") ? "配方与生产"
        : n is "WEAPON_ESSENCE" or "ARMOR_ESSENCE" or "DECORATION_ESSENCE" or "FASHION_ESSENCE" or "PROJECTILE_ESSENCE" or "QUIVER_ESSENCE"
              or "FLYSWORD_ESSENCE" or "WINGMANWING_ESSENCE" or "SUITE_ESSENCE" or "EQUIPMENT_ADDON" || n.Contains("WEAPON") || n.Contains("ARMOR") || n.Contains("DECORATION") || n.Contains("FASHION") ? "装备"
        : n.Contains("FACE") || n.Contains("CHARACTER") || n.Contains("PLAYER") || n.Contains("CONFIG") || n.Contains("LEVEL") ? "角色与配置"
        : n.EndsWith("_ESSENCE") ? "物品、消耗品与材料"
        : "其他";

    static readonly string[] CategoryOrder = { "装备", "物品、消耗品与材料", "怪物、NPC 与采集物", "宠物", "配方与生产", "角色与配置", "其他" };
    // Entrada do filtro de tipo (Major/Sub null => todos).
    private sealed record TypeEntry(string? Major, string? Sub, string Label)
    {
        public override string ToString() => Label;
    }

    private List<Item> _view = new();
    private Dictionary<Item, ItemList> _owner = new(ReferenceEqualityComparer.Instance);

    // Reconstrói o seletor de blocos (chamado ao carregar o elements.data).
    private void BuildTree(bool ownersReady = false)
    {
        _iconsRoot = LoadSetting("icons");
        if (!ownersReady) _owner.Clear();
        _blockCombo.BeginUpdate();
        _blockCombo.Items.Clear();
        if (_store.Current is not null)
        {
            var lists = _store.Current.Lists;
            if (!ownersReady) foreach (var l in lists) foreach (var it in l.Items) _owner[it] = l;
            _blockCombo.Items.Add(new BlockEntry(null, $"★ 收藏夹（{_favorites.Count}）"));
            var entries = lists.Select((b, i) => (b, i)).ToList();
            foreach (var cat in CategoryOrder)
            {
                var inCat = entries.Where(x => BlockCategory(x.b.Name) == cat).ToList();
                if (inCat.Count == 0) continue;
                _blockCombo.Items.Add(new BlockEntry(null, $"── {cat} ──", Header: true));
                foreach (var (b, i) in inCat) _blockCombo.Items.Add(new BlockEntry(b, $"   [{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})"));
            }
            var last = LoadSetting("block") ?? "WEAPON_ESSENCE";
            int sel = 1;
            for (int i = 0; i < _blockCombo.Items.Count; i++)
                if (_blockCombo.Items[i] is BlockEntry { Block: { } bb } && bb.Name == last) sel = i;
            _blockCombo.SelectedIndex = Math.Min(sel, _blockCombo.Items.Count - 1);
        }
        _blockCombo.EndUpdate();
        Text = _store.Current is null ? "MyAceEditor 简体中文版" : $"MyAceEditor 简体中文版 — {_store.Current.Name}";
        if (_store.Current is null) { _view.Clear(); _list.VirtualListSize = 0; }
    }

    private void OnBlockChanged()
    {
        if (_bindingElements) return;
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
        _buildingTypes = true;
        try
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
                _typeCombo.Items.Add(new TypeEntry(null, null, $"所有类型（{b.Items.Count:N0}）"));
                foreach (var byMajor in b.Items.GroupBy(i => Fv(i, "id_major_type")))
                {
                    var mName = majors.GetValueOrDefault(byMajor.Key, "类型 " + byMajor.Key);
                    _typeCombo.Items.Add(new TypeEntry(byMajor.Key, null, $"{mName} ({byMajor.Count()})"));
                    foreach (var bySub in byMajor.GroupBy(i => Fv(i, "id_sub_type")))
                        _typeCombo.Items.Add(new TypeEntry(byMajor.Key, bySub.Key,
                            $"     {mName} › {subs.GetValueOrDefault(bySub.Key, "子类型 " + bySub.Key)} ({bySub.Count()})"));
                }
                _typeCombo.SelectedIndex = 0;
            }
            _typeCombo.Visible = has;
        }
        finally { _typeCombo.EndUpdate(); _buildingTypes = false; }
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
        if (_bindingElements || _store.Current is null) return;
        _searchDelay.Stop();
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

        int total = _curBlock?.Items.Count ?? _view.Count;
        _stTotal.Text = $"元素总数：{_store.Current.Lists.Sum(l => l.Items.Count):N0}";
        _stBlock.Text = $"当前块：{total:N0} · 显示：{_view.Count:N0}";
        _list.Columns[1].Text = $"名称（{_view.Count:N0} / {total:N0}）";
        _updatingView = true;
        try
        {
            _list.SelectedIndices.Clear(); _list.VirtualListSize = _view.Count;
            if (_current is null || !_view.Contains(_current)) _current = _view.FirstOrDefault();
            if (_current is not null) SelectItem(_current, focus: false);
        }
        finally { _updatingView = false; }
        _list.Invalidate();
        if (_current is not null) ShowItem(_current);
    }

    private void OnRetrieveItem(object? s, RetrieveVirtualItemEventArgs e)
    {
        if ((uint)e.ItemIndex >= (uint)_view.Count) { e.Item = new ListViewItem(new[] { "", "", "" }); return; }
        var it = _view[e.ItemIndex];
        e.Item = new ListViewItem(it.Id, IconIndexFor(it)) { Tag = it };
        e.Item.SubItems.Add(string.IsNullOrEmpty(it.Name) ? "（未命名）" : it.Name);
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
            int uses = _refs?.CachedUsedBy(it.Id).Count ?? 0;
            TextRenderer.DrawText(g, _refs?.IsPrepared == false ? "…" : uses.ToString(), _list.Font, Rectangle.Inflate(e.Bounds, -6, 0), sel ? Color.White : uses > 0 ? GGold : Color.FromArgb(90, 96, 106), flags | TextFormatFlags.Right);
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
        Status($"已选择 {_view.Count:N0} 个物品");
    }

    private void BulkClone()
    {
        if (!CanEditElements()) return;
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        Item? last = null;
        foreach (var src in sel)
        {
            if (!_owner.TryGetValue(src, out var block)) continue;
            var ni = new Item { Id = NextCode(block).ToString(), Name = src.Name, Fields = src.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)), Raw = src.Raw?.ToArray() };
            SetIdName(ni);
            block.Items.Add(ni); _owner[ni] = block; last = ni;
        }
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        RefreshView();
        if (last is not null) { _current = last; SelectItem(last); ShowItem(last); }
        Status($"已克隆 {sel.Count} 个物品");
    }

    private void BulkDelete()
    {
        if (!CanEditElements()) return;
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        if (!ChineseDialogs.Confirm(this, $"确定删除选中的 {sel.Count} 个物品吗？", "删除")) return;
        foreach (var it in sel) if (_owner.TryGetValue(it, out var b)) { b.Items.Remove(it); _owner.Remove(it); _favorites.Remove(FavKey(b, it)); }
        SaveFavorites();
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status($"已删除 {sel.Count} 个物品");
    }

    private void BulkChange()
    {
        if (!CanEditElements()) return;
        var sel = SelectedItems();
        if (sel.Count == 0) return;
        var fields = sel[0].Fields.Keys.ToList();
        if (FieldEditors.BulkValue(this, fields, sel.Count, FieldLabels.Label) is not { } r) return;
        int n = 0;
        foreach (var it in sel)
            if (it.Fields.TryGetValue(r.Field, out var f)) { it.Fields[r.Field] = Field.Of(f.Type, r.Value); if (r.Field.Equals("Name", StringComparison.OrdinalIgnoreCase)) it.Name = r.Value; n++; }
        _list.Invalidate();
        if (_current is not null) ShowItem(_current);
        Status($"已将 {n} 个物品的“{FieldLabels.Label(r.Field)}”设为 {r.Value}");
    }

    private void OnListSelect()
    {
        if (_bindingElements || _updatingView) return;
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
        if (hit is null) { Status($"未找到 ID {id}"); return; }
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

    private static string Pretty(string block) => BlockLabels.Label(block);

    // ---------- detalhes ----------
    private void ShowItem(Item item)
    {
        EnsureReferenceIndex();
        _header.Text = string.IsNullOrEmpty(item.Name) ? "（未命名）" : TasksFile.SplitColor(item.Name).Text;
        _header.ForeColor = NameColor(item);
        _idLabel.Text = item.Id;
        LoadItemIcon(item);
        var uses = _refs?.CachedUsedBy(item.Id).Count ?? 0;
        _tabLinks.Text = _refs?.IsPrepared == false ? "关联引用（索引未完成）" : uses > 0 ? $"关联引用（{uses}）" : "关联引用";
        if (_detailTabs.SelectedTab == _tabLinks) FillLinks(item);

        // sub-abas por grupo (Básico, Habilidades, Queda, Modelo…), mantendo a aba escolhida se existir
        var groups = GroupsOf(item);
        var keep = _groupTabs.SelectedTab?.Text;
        _updatingDetails = true;
        try
        {
            if (!groups.SequenceEqual(_groupTabs.TabPages.Cast<TabPage>().Select(t => t.Text)))
            {
                _groupTabs.TabPages.Clear();
                foreach (var g in groups) _groupTabs.TabPages.Add(new TabPage(g));
                var again = _groupTabs.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == keep);
                if (again is not null) _groupTabs.SelectedTab = again;
            }
        }
        finally { _updatingDetails = false; }
        FillGrid(item);
        FillDescription(item);
        FillFindFields(item);

        if (!_navigating)
        {
            if (_histPos >= 0 && _histPos < _history.Count - 1) _history.RemoveRange(_histPos + 1, _history.Count - _histPos - 1);
            if (_history.Count == 0 || _history[^1] != item) { _history.Add(item); if (_history.Count > 200) _history.RemoveAt(0); }
            _histPos = _history.Count - 1;
        }
        _stSel.Text = $"已选择：{item.Id}";
    }

    // Grupo de exibição: drops → Queda, habilidades → Habilidades, arquivos → Modelo; resto pelo FieldLabels.
    private static string DisplayGroup(string key)
    {
        var k = key.ToLowerInvariant();
        var kind = FieldKinds.Of(key);
        if (k.Contains("drop") || (kind == FieldKind.ItemRef && k.Contains("matter") && !k.Contains("material"))) return "掉落";
        if (kind == FieldKind.SkillRef || k.Contains("skill")) return "技能";
        if (kind == FieldKind.File) return "模型";
        return FieldLabels.Group(key);
    }

    private static List<string> GroupsOf(Item item)
    {
        var present = item.Fields.Keys.Select(DisplayGroup).ToHashSet();
        var order = new List<string> { "全部" };
        foreach (var g in FieldLabels.GroupOrder.Concat(new[] { "技能", "掉落", "模型" }))
            if (present.Contains(g) && !order.Contains(g)) order.Add(g);
        foreach (var g in present) if (!order.Contains(g)) order.Add(g);
        return order;
    }

    private int _gridRevision;
    private async void FillGrid(Item item)
    {
        int revision = ++_gridRevision, bound = 0;
        _grid.Enabled = false;
        try
        {
            var sel = _groupTabs.SelectedTab?.Text ?? "全部";
            _grid.Rows.Clear();
            var byGroup = item.Fields.GroupBy(f => DisplayGroup(f.Key)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var gname in GroupsOf(item).Skip(1))
            {
                if (sel != "全部" && sel != gname) continue;
                if (!byGroup.TryGetValue(gname, out var fields)) continue;
                var vis = fields.Where(f => (gname != "属性 / 附加属性" && gname != "掉落" && gname != "技能") || !IsEmpty(f.Value.Value) || sel == gname).ToList();
                if (vis.Count == 0) continue;
                if (sel == "全部")
                {
                    int hr = _grid.Rows.Add("  " + gname.ToUpperInvariant(), "", "");
                    var hrow = _grid.Rows[hr]; hrow.ReadOnly = true;
                    hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                    hrow.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
                }
                foreach (var (k, f) in vis)
                {
                    var desc = Describe(item, k, f.Value);
                    int r = _grid.Rows.Add("  " + FieldLabels.Label(k), f.Value, desc);
                    _grid.Rows[r].Tag = k;
                    if (desc.StartsWith("→")) _grid.Rows[r].Cells[2] = LinkCell(desc);
                    _grid.Rows[r].Cells[0].ToolTipText = FieldLabels.Label(k) + "\n原始字段：" + k + (IsSpecialEditor(k) ? "\n双击选择" : "");
                    if ((++bound & 127) == 0)
                    {
                        _stSel.Text = $"正在显示字段：{bound:N0} 行";
                        await Task.Delay(1);
                        if (IsDisposed || revision != _gridRevision || !ReferenceEquals(item, _current)) return;
                    }
                }
            }
            // efeito do addon (bloco EQUIPMENT_ADDON) e "dropado por" (monstros que dropam o item)
            if (sel is "全部" or "基本" && CurrentItemBlock?.Name == "EQUIPMENT_ADDON" && _cfgs?.Addons?.Describe(item) is { } eff)
            {
                _grid.Rows.Insert(0, "  游戏内效果", eff, _cfgs.Addons.TypeCaption(item.Id) ?? "");
                _grid.Rows[0].ReadOnly = true;
                _grid.Rows[0].DefaultCellStyle.ForeColor = Color.FromArgb(22, 128, 61);
                _grid.Rows[0].DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10);
            }
            if (sel is "全部" or "掉落")
            {
                var drops = (_refs?.CachedUsedBy(item.Id) ?? Array.Empty<RefIndex.Ref>())
                    .Where(u => u.Field.Contains("drop", StringComparison.OrdinalIgnoreCase) && u.Block.Name == "MONSTER_ESSENCE").ToList();
                if (drops.Count > 0)
                {
                    int hr = _grid.Rows.Add($"  掉落来源（{drops.Count} 种怪物）", "", "");
                    var hrow = _grid.Rows[hr]; hrow.ReadOnly = true;
                    hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
                    hrow.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
                    foreach (var d in drops.OrderByDescending(d => ParsePct(d.Probability)).Take(300))
                    {
                        int r = _grid.Rows.Add("", ParsePct(d.Probability) is var p && p > 0 ? $"{p * 100:0.###}%" : "", "");
                        _grid.Rows[r].Cells[0] = LinkCell($"  {d.Item.Name}  (ID {d.Item.Id})");
                        _grid.Rows[r].Cells[2].Value = $"等级 {d.Item.Fields.GetValueOrDefault("level")?.Value} · {FieldLabels.Label(d.Field)}";
                        _grid.Rows[r].Tag = d.Item; _grid.Rows[r].ReadOnly = true;
                    }
                }
            }
        }
        catch (Exception ex) { if (!IsDisposed && revision == _gridRevision) Error("字段显示失败", ex); }
        finally
        {
            if (!IsDisposed && revision == _gridRevision)
            {
                _grid.Enabled = true;
                if (ReferenceEquals(item, _current)) _stSel.Text = $"已选择：{item.Id}";
            }
        }
    }

    private static float ParsePct(string? v) =>
        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;

    private DataGridViewLinkCell LinkCell(string text) => new()
    {
        Value = text, LinkColor = CAccent, ActiveLinkColor = CAccent, VisitedLinkColor = CAccent,
        LinkBehavior = LinkBehavior.HoverUnderline, TrackVisitedState = false,
    };

    // ---------- editores por duplo clique (estilo Rody) ----------
    private static bool IsSpecialEditor(string key) =>
        FieldKinds.Of(key) is FieldKind.ClassMask or FieldKind.AddonRef or FieldKind.ItemRef or FieldKind.MonsterRef or FieldKind.RecipeRef;

    private bool EditFieldSpecial(Item item, string key)
    {
        if (!CanEditElements()) return false;
        var kind = FieldKinds.Of(key);
        var cur = item.Fields[key].Value;
        switch (kind)
        {
            case FieldKind.ClassMask:
                if (FieldEditors.ClassMask(this, long.TryParse(cur, out var m) ? m : 0) is { } nm) SetField(item, key, nm.ToString());
                return true;
            case FieldKind.AddonRef or FieldKind.ItemRef or FieldKind.MonsterRef or FieldKind.RecipeRef:
            {
                var src = PickSource(kind);
                if (src.Count == 0) return false;
                bool multi = System.Text.RegularExpressions.Regex.IsMatch(key, @"_\d+_");
                var addons = _cfgs?.Addons;
                var picked = FieldEditors.Pick(this, kind switch
                    {
                        FieldKind.AddonRef => "选择附加属性", FieldKind.MonsterRef => "选择怪物",
                        FieldKind.RecipeRef => "选择配方", _ => "选择物品",
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
                Status(picked.Count > slots.Count ? $"已将 {picked.Count} 个选择中的 {slots.Count} 个填入可用字段" : $"已应用 {picked.Count} 个值");
                return true;
            }
        }
        return false;
    }

    private List<Item> PickSource(FieldKind kind)
    {
        var lists = _store.Current?.Lists ?? new List<ItemList>();
        IEnumerable<ItemList> src = kind switch
        {
            FieldKind.AddonRef => lists.Where(l => l.Name == "EQUIPMENT_ADDON"),
            FieldKind.MonsterRef => lists.Where(l => l.Name == "MONSTER_ESSENCE"),
            FieldKind.RecipeRef => lists.Where(l => l.Name == "RECIPE_ESSENCE"),
            _ => lists.Where(l => l.Name.EndsWith("_ESSENCE") && l.Name is not ("MONSTER_ESSENCE" or "NPC_ESSENCE" or "RECIPE_ESSENCE") && !l.Name.StartsWith("NPC_")),
        };
        return src.SelectMany(l => l.Items).ToList();
    }

    private void SetField(Item item, string key, string value, bool refresh = true)
    {
        if (!CanEditElements()) return;
        item.Fields[key] = Field.Of(item.Fields[key].Type, value);
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) item.Name = value;
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
            _descBox.SelectionFont = new Font("Microsoft YaHei UI", bold ? 12 : 10.5f, bold ? FontStyle.Bold : FontStyle.Regular);
            _descBox.AppendText(t);
        }
        Put((string.IsNullOrEmpty(item.Name) ? "（未命名）" : TasksFile.SplitColor(item.Name).Text) + "\n", NameColor(item), bold: true);
        Put($"ID {item.Id}\n\n", CMuted);
        if (_cfgs is not null && int.TryParse(item.Id, out var id) && _cfgs.ItemDescs.TryGetValue(id, out var desc))
            foreach (var (t, c) in GameConfigs.Runs(desc, CTreeFg)) Put(t, c);
        else Put(_cfgs is null ? "（未找到 configs.pck）" : "（configs.pck 中没有此物品的描述）", CMuted);
    }

    private void FillFindFields(Item item)
    {
        var keys = item.Fields.Keys.ToList();
        if (_findField.Items.Count == keys.Count && _findField.Tag == _curBlock) return;
        var keep = (_findField.SelectedItem as FieldEntry)?.Key;
        _findField.BeginUpdate();
        try
        {
            _findField.Items.Clear();
            foreach (var k in keys) _findField.Items.Add(new FieldEntry(k, FieldLabels.Label(k)));
        }
        finally { _findField.EndUpdate(); }
        _findField.Tag = _curBlock;
        string? selected = keep != null && keys.Contains(keep) ? keep : keys.FirstOrDefault(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase)) ?? keys.FirstOrDefault();
        _findField.SelectedIndex = keys.IndexOf(selected ?? "");
    }

    private sealed record FieldEntry(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private void RunFind()
    {
        if (_findField.SelectedItem is not FieldEntry selected) return;
        string key = selected.Key;
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null) return;
        string v = _findValue.Text.Trim(), op = _findOp.SelectedItem as string ?? "=";
        bool Match(string fv)
        {
            if (op == "包含") return fv.Contains(v, StringComparison.OrdinalIgnoreCase);
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
        Status($"查找：在 {Pretty(block.Name)} 中找到 {n}{(n >= 2000 ? "+" : "")} 个物品，条件为 {FieldLabels.Label(key)} {op} {v}");
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

    // Texto da coluna "关联": nome do item/addon/receita apontado, % e classes por extenso.
    private string Describe(Item item, string field, string value)
    {
        var kind = FieldKinds.Of(field);
        switch (kind)
        {
            case FieldKind.Probability:
                return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr) && pr > 0 && pr <= 1
                    ? $"{pr * 100:0.###}%" : "";
            case FieldKind.ClassMask: return FieldKinds.ClassNames(value);
            case FieldKind.SkillRef: return IsEmpty(value) ? "" : "技能";
            case FieldKind.TaskRef: return IsEmpty(value) ? "" : "任务";
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
    private int _linksRevision;
    private async void FillLinks(Item item)
    {
        int revision = ++_linksRevision;
        var refs = _refs;
        var uses = refs?.CachedUsedBy(item.Id) ?? Array.Empty<RefIndex.Ref>();
        try
        {
            _links.Items.Clear();
            for (int start = 0; start < uses.Count; start += 128)
            {
                if (IsDisposed || revision != _linksRevision || !ReferenceEquals(item, _current) || !ReferenceEquals(refs, _refs)) return;
                _links.BeginUpdate();
                try
                {
                    for (int i = start; i < Math.Min(start + 128, uses.Count); i++)
                    {
                        var r = uses[i];
                        string pct = r.Probability is not null && float.TryParse(r.Probability, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0 ? $"{p * 100:0.###}%" : "";
                        var li = new ListViewItem(new[] { Pretty(r.Block.Name), r.Item.Id, r.Item.Name, FieldLabels.Label(r.Field), pct }, IconIndexFor(r.Item)) { Tag = r.Item };
                        _links.Items.Add(li);
                    }
                }
                finally { _links.EndUpdate(); }
                if (start + 128 < uses.Count) await Task.Delay(1);
            }
        }
        catch (Exception ex) { if (!IsDisposed && revision == _linksRevision) Error("关联引用显示失败", ex); }
    }

    // Navega até um item de qualquer bloco (troca o seletor de bloco se precisar).
    /// <summary>Diagnóstico (--snapshot): abre o item de um bloco pelo ID.</summary>
    internal void GoToId(string block, string id)
    {
        if (_store.Current?.FindListByName(block)?.FindById(id) is { } it) GoToItem(it);
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
        if (_current is null || row < 0 || row >= _grid.Rows.Count || _grid.Rows[row].Tag is not string key || !_current.Fields.ContainsKey(key)) return;
        var val = _grid.Rows[row].Cells[1].Value?.ToString() ?? "";
        _current.Fields[key] = Field.Of(_current.Fields[key].Type, val);
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            _current.Name = val;
            _list.Invalidate();
            _header.Text = $"{val}     (ID {_current.Id})";
        }
        Status($"已更新“{FieldLabels.Label(key)}”");
    }

    // ---------- menu de contexto (clicar direito) ----------
    private ToolStripMenuItem _miFav = null!;
    private void BuildContextMenu()
    {
        _ctx.Items.Add("新建物品", null, (_, _) => NewItem());
        _ctx.Items.Add("克隆物品", null, (_, _) => CloneItem());
        _ctx.Items.Add("删除物品", null, (_, _) => RemoveItem());
        _ctx.Items.Add(new ToolStripSeparator());
        _ctx.Items.Add("克隆选中项", null, (_, _) => BulkClone());
        _ctx.Items.Add("删除选中项", null, (_, _) => BulkDelete());
        _ctx.Items.Add("批量修改字段…", null, (_, _) => BulkChange());
        _ctx.Items.Add("复制选中项 ID", null, (_, _) => { var ids = SelectedItems().Select(i => i.Id); Clipboard.SetText(string.Join(Environment.NewLine, ids)); Status("已复制 ID"); });
        _ctx.Items.Add("全选（Ctrl+A）", null, (_, _) => SelectAllItems());
        _ctx.Items.Add(new ToolStripSeparator());
        _miFav = new ToolStripMenuItem("添加收藏", null, (_, _) => ToggleFavorite());
        _ctx.Items.Add(_miFav);
        _ctx.Opening += (_, e) =>
        {
            bool isItem = _list.SelectedIndices.Count > 0 && _current is not null;
            int nSel = _list.SelectedIndices.Count;
            foreach (ToolStripItem i in _ctx.Items)
                if (i.Text is { } t && t.EndsWith("选中项") || i.Text?.StartsWith("批量修改字段") == true) i.Text = System.Text.RegularExpressions.Regex.Replace(i.Text!, @" \(\d+\)$", "") + (nSel > 1 ? $" ({nSel})" : "");
            foreach (ToolStripItem i in _ctx.Items) i.Enabled = isItem || i.Text == "新建物品";
            if (isItem && CurrentItemBlock is { } cb)
                _miFav.Text = _favorites.Contains(FavKey(cb, _current!)) ? "取消收藏" : "添加收藏";
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
        var idKey = it.Fields.Keys.FirstOrDefault(k => k.Equals("ID", StringComparison.OrdinalIgnoreCase));
        var nameKey = it.Fields.Keys.FirstOrDefault(k => k.Equals("Name", StringComparison.OrdinalIgnoreCase));
        if (idKey is not null) it.Fields[idKey] = Field.Of(FieldType.Int, it.Id);
        if (nameKey is not null) it.Fields[nameKey] = Field.Of(FieldType.String, it.Name);
    }

    private void InsertNewItem(ItemList block, Item ni)
    {
        block.Items.Add(ni);
        _owner[ni] = block;
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = ni;
        RefreshView();
        SelectItem(ni);
        ShowItem(ni);
    }

    private void NewItem()
    {
        if (!CanEditElements()) return;
        var block = _curBlock ?? CurrentItemBlock;
        if (block is null) { Status("请先选择一个数据块"); return; }
        long code = NextCode(block);
        var schema = (_store.Current?.Source as ElementsLayout)?.Blocks.FirstOrDefault(span => ReferenceEquals(span.List, block))?.Block;
        if (schema is null) { Status("此数据块缺少字段布局，无法新建。"); return; }
        var ni = CfgElementsReader.ReadRecord(schema, new byte[schema.RecordSize]);
        ni.Id = code.ToString(); ni.Name = "新建物品";
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status($"已在 {Pretty(block.Name)} 中创建物品 {code}，请填写右侧字段。");
    }

    private void CloneItem()
    {
        if (!CanEditElements()) return;
        if (_current is null || CurrentItemBlock is not { } block) { Status("请选择要克隆的物品"); return; }
        long code = NextCode(block);
        var ni = new Item
        {
            Id = code.ToString(),
            Name = _current.Name + "（副本）",
            Fields = _current.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)),
            Raw = _current.Raw?.ToArray()
        };
        SetIdName(ni);
        InsertNewItem(block, ni);
        Status($"已克隆物品，新 ID 为 {code}。");
    }

    private void AddItem() => NewItem();

    private void RemoveItem()
    {
        if (!CanEditElements()) return;
        if (_current is null || CurrentItemBlock is not { } block) { Status("请选择一个物品"); return; }
        if (!ChineseDialogs.Confirm(this, $"确定删除“{_current.Name}”（ID {_current.Id}）吗？", "删除物品")) return;
        _favorites.Remove(FavKey(block, _current)); SaveFavorites();
        block.Items.Remove(_current); _owner.Remove(_current);
        if (_store.Current is not null) _refs = new RefIndex(_store.Current);
        _current = null;
        RefreshView();
        Status("已删除物品");
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
            _blockCombo.Items[0] = fav with { Label = $"★ 收藏夹（{_favorites.Count}）" };
        RefreshView();
        Status(_favorites.Contains(key) ? "已添加到收藏夹" : "已从收藏夹移除");
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
        using var f = new Font("Microsoft YaHei UI", 9);
        int w = 120;
        foreach (var l in _tipLines) w = Math.Max(w, TextRenderer.MeasureText(l, f).Width);
        foreach (var d in _tipDrops) w = Math.Max(w, TextRenderer.MeasureText($"物品：{d.Text}  {d.Pct}", f).Width + 22);
        e.ToolTipSize = new Size(w + 22, _tipLines.Length * 18 + (_tipDrops.Count > 0 ? _tipDrops.Count * 20 + 8 : 0) + 16);
    }

    private void DrawTip(object? s, DrawToolTipEventArgs e)
    {
        e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(16, 17, 20)), e.Bounds);
        using (var pen = new Pen(Color.FromArgb(70, 74, 82))) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
        using var fName = new Font("Microsoft YaHei UI", 9.5f);
        using var f = new Font("Microsoft YaHei UI", 9);
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
            TextRenderer.DrawText(e.Graphics, "物品：", f, new Point(10, y + 1), Color.FromArgb(206, 211, 219));
            int x = 50;
            if (d.Icon is not null) e.Graphics.DrawImage(d.Icon, x, y, 18, 18);
            x += 22;
            var sz = TextRenderer.MeasureText(d.Text, f);
            TextRenderer.DrawText(e.Graphics, d.Text, f, new Point(x, y + 1), d.Color);
            TextRenderer.DrawText(e.Graphics, d.Pct, f, new Point(x + sz.Width + 4, y + 1), Color.FromArgb(232, 182, 78));
            y += 20;
        }
    }

    private static readonly string[] TipGroups = { "基本", "需求条件", "攻击", "防御", "耐久度", "经济" };
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
            lines.Add($"职业：{cls}");

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
    private string? _tasksPath;
    private QuestNode? _qCur;
    private object? _selectedQuest;
    private QuestNode? _questTextOwner, _questTalkOwner, _questGridOwner;
    private readonly string[] _questTextDisplay = new string[4];
    private string _talkDisplay = "";
    private bool _bindingQuestDetails, _bindingTalk, _bindingQuestTree;
    private readonly Font _questGroupFont = new("Microsoft YaHei UI", 9, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _questSearchDelay = new() { Interval = 200 };
    private CancellationTokenSource? _questSearchCts, _questDetailCts;
    private int _questDetailRevision;
    private QuestDetail? _questDetail;
    private TabControl _questDetailTabs = null!;
    private readonly TreeView _qTree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, BorderStyle = BorderStyle.None, ItemHeight = 22, ShowLines = false, DrawMode = TreeViewDrawMode.OwnerDrawText };
    private readonly TextBox _qSearch = new() { Dock = DockStyle.Top, PlaceholderText = "  按名称或 ID 搜索任务…", BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _qName = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 11) };
    private readonly Label _qInfo = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Microsoft YaHei UI", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly Label _qStatus = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _qGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly TextBox[] _qTexts = Enumerable.Range(0, 4).Select(_ => new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10) }).ToArray();
    private readonly TreeView _qTalkTree = new() { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
    private readonly TextBox _qTalkText = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10) };
    private object? _qTalkSel;   // TalkProc, TalkWindow ou TalkOption em edição
    private TreeNode? _questTalkNode;

    private Control BuildQuestView()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = CLine, Panel1MinSize = 300 };
        var left = new Panel { Dock = DockStyle.Fill, BackColor = CTreeBg, Padding = new Padding(8) };
        _qSearch.BackColor = CTreeInput; _qSearch.ForeColor = CTreeFg; _qSearch.Font = new Font("Microsoft YaHei UI", 10);
        _qSearch.TextChanged += (_, _) =>
        {
            _questSearchDelay.Stop(); _questSearchCts?.Cancel(); _questViewRevision++;
            _questSearchDelay.Start();
        };
        _questSearchDelay.Tick += (_, _) => { _questSearchDelay.Stop(); RefreshQuests(); };
        _qTree.BackColor = CTreeBg; _qTree.ForeColor = CTreeFg; _qTree.Font = new Font("Microsoft YaHei UI", 10);
        _qTree.DrawNode += DrawQuestNode;
        _qTree.BeforeExpand += (_, e) => ExpandQuestNode(e.Node!);
        _qTree.BeforeSelect += (_, e) => { if (!_bindingQuestTree && !_qGrid.EndEdit()) e.Cancel = true; };
        _qTree.AfterSelect += (_, e) =>
        {
            if (_bindingQuestTree) return;
            if (e.Node?.Tag is TreePage) { LoadMore(_qTree, e.Node); return; }
            ShowQuest(e.Node?.Tag);
        };
        _qTree.VisibleChanged += (_, _) =>
        {
            if (_qTree.Visible) ShowQuest(_qTree.SelectedNode?.Tag);
            else if (_selectedQuest is not null) ResetQuestDetails();
        };
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
        editRow.Controls.Add(new Label { Text = "名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = CMuted }, 0, 0);
        editRow.Controls.Add(_qName, 1, 0);
        editRow.Controls.Add(Btn("应用名称", ApplyQuestName, accent: true), 2, 0);
        editRow.Controls.Add(Btn("💾 保存 tasks.data", SaveTasks), 3, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        _questDetailTabs = tabs;
        tabs.SelectedIndexChanged += (_, _) => RenderQuestTab();
        // Dados
        var tData = new TabPage("数据") { BackColor = CSurface };
        StyleQuestGrid();
        tData.Controls.Add(_qGrid);
        // Textos
        var tText = new TabPage("文本") { BackColor = CSurface, Padding = new Padding(8) };
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
        textLayout.Controls.Add(new Label { Text = "颜色：在文本前添加 ^RRGGBB（例如 ^ffcc00）。游戏换行符：\\r。离开文本框后修改生效。", Dock = DockStyle.Fill, ForeColor = CMuted });
        tText.Controls.Add(textLayout);
        // Diálogos
        var tTalk = new TabPage("对话") { BackColor = CSurface };
        var talkSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, BackColor = CLine };
        _qTalkTree.BackColor = CSurface; _qTalkTree.ForeColor = CFg; _qTalkTree.Font = new Font("Microsoft YaHei UI", 9.5f);
        _qTalkTree.BeforeExpand += (_, e) => ExpandTalkNode(e.Node!);
        _qTalkTree.AfterSelect += (_, e) =>
        {
            if (_bindingTalk) return;
            if (e.Node?.Tag is TreePage) { LoadMore(_qTalkTree, e.Node); return; }
            SelectTalk(e.Node?.Tag);
        };
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
        _qGrid.Columns[1].Width = 180; _qGrid.Columns[1].DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.5f);
        _qGrid.Columns[2].ReadOnly = true; _qGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _qGrid.Columns[2].DefaultCellStyle.ForeColor = CAccent;
        _qGrid.CellValidating += (_, e) =>
        {
            if (_bindingQuestDetails || _qCur is null || !ReferenceEquals(_questGridOwner, _qCur)
                || e.ColumnIndex != 1 || e.RowIndex < 0 || e.RowIndex >= _qGrid.Rows.Count
                || _qGrid.Rows[e.RowIndex].Tag is not TaskField { Editable: true } f) return;
            var row = _qGrid.Rows[e.RowIndex];
            if (!_qCur.CanSet(f, e.FormattedValue?.ToString() ?? ""))
            {
                e.Cancel = true;
                row.ErrorText = $"“{f.Name}”的值无效，请修正后保存。";
                _qStatus.Text = row.ErrorText;
            }
            else row.ErrorText = "";
        };
        _qGrid.CellEndEdit += (_, e) =>
        {
            if (_bindingQuestDetails || _qCur is null || !ReferenceEquals(_questGridOwner, _qCur)
                || e.RowIndex < 0 || e.RowIndex >= _qGrid.Rows.Count || _qGrid.Rows[e.RowIndex].Tag is not TaskField f) return;
            var v = _qGrid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "";
            if (v == _qCur.Get(f)) { _qGrid.Rows[e.RowIndex].ErrorText = ""; return; }
            if (!_qCur.Set(f, v)) { _qStatus.Text = $"“{f.Name}”的值无效"; _qGrid.Rows[e.RowIndex].Cells[1].Value = _qCur.Get(f); return; }
            _qGrid.Rows[e.RowIndex].ErrorText = "";
            _qGrid.Rows[e.RowIndex].Cells[2].Value = QuestFieldHint(f, _qCur.Get(f));
            MarkTasksDirty($"已修改“{f.Name}”");
        };
    }

    // Texto auxiliar: nomes de item/monstro/NPC a partir do elements.data carregado.
    private string QuestFieldHint(TaskField f, string v) => QuestFieldHint(f, v, _tasks, _refs);

    private static string QuestFieldHint(TaskField f, string v, TasksFile? tasks, RefIndex? refs)
    {
        if (v is "0" or "") return "";
        var n = f.Name.ToLowerInvariant();
        FieldKind kind = n.Contains("npc") ? FieldKind.ItemRef : n.Contains("怪物") ? FieldKind.MonsterRef : n.Contains("物品") ? FieldKind.ItemRef : FieldKind.Plain;
        if (n.StartsWith("前置任务 ", StringComparison.Ordinal) || n.StartsWith("互斥任务 ", StringComparison.Ordinal) || n is "父任务" or "上一个同级任务" or "下一个同级任务" or "首个子任务" or "关联任务")
            return int.TryParse(v, out var id) && tasks?.Index.Find(id) is { } row ? "→ " + TasksFile.SplitColor(TaskIndex.NameOf(row)).Text : "";
        if (n.Contains("职业") && n.StartsWith("数量") is false && v.Length > 0 && n == "职业数量") return "";
        return kind != FieldKind.Plain && refs?.Resolve(kind, v) is { } hit ? $"→ {hit.Item.Name}  [{Pretty(hit.Block.Name)}]" : "";
    }

    private void OpenTasksDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "任务数据 (*.data)|*.data|所有文件 (*.*)|*.*", Title = "打开任务数据（tasks.data）" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadTasks(dlg.FileName);
    }

    private bool _tasksDirty;
    private void MarkTasksDirty(string msg) { _tasksDirty = true; _qStatus.Text = msg + " — 点击“保存 tasks.data”写入文件。"; }

    private static string TaskLoadSummary(TasksFile tasks, int count) => (tasks.FullSchema
        ? $"版本 {tasks.Version} · {tasks.Roots!.Count:N0} 个主任务（含子任务共 {count:N0} 个）· 完整任务结构"
        : $"版本 {tasks.Version} · {tasks.Quests.Count:N0} 个任务 · 名称模式" +
            (tasks.SchemaError is null ? "（尚不支持此版本的完整结构）" : "（完整结构校验未通过：" + tasks.SchemaError + "）")) +
        (tasks.CanWrite ? "" : " · 只读：" + tasks.ReadOnlyReason);

    private bool LoadTasks(string path)
    {
        try
        {
            var tasks = ReadProgressDialog.Run(this, "读取任务数据", context => new TasksFile(path, context));
            ResetQuestSession();
            _tasks = tasks; _tasksPath = path; _tasksDirty = false;
            RefreshQuests();
            _qStatus.Text = TaskLoadSummary(tasks, tasks.Index.Count);
            return true;
        }
        catch (OperationCanceledException) { _qStatus.Text = "任务读取已取消，原任务数据已保留。"; }
        catch (Exception ex) { _qStatus.Text = "读取 tasks.data 失败：" + ChineseDialogs.Explain(ex); }
        return false;
    }

    private int _questViewRevision;
    private int? _questSelectionAfterLoad;
    private async void RefreshQuests()
    {
        _questSearchDelay.Stop(); _questSearchCts?.Cancel();
        int revision = ++_questViewRevision;
        var tasks = _tasks;
        if (tasks is null) { ResetQuestSession(); return; }
        if (!CommitQuestEditors()) return;
        var cancellation = new CancellationTokenSource();
        _questSearchCts = cancellation;
        string query = _qSearch.Text.Trim();
        int? requestedId = _questSelectionAfterLoad;
        _questSelectionAfterLoad = null;
        try
        {
            object[] rows = await Task.Run(() => tasks.Index.Search(query, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsDisposed || revision != _questViewRevision || !ReferenceEquals(tasks, _tasks)) return;
            var previous = _selectedQuest ?? _qTree.SelectedNode?.Tag;
            int? selectedId = requestedId ?? (previous is QuestNode or QuestEntry ? (int?)TaskIndex.IdOf(previous) : null);
            if (query.Length == 0 && selectedId is { } selected && tasks.Index.Find(selected) is QuestNode selectedQuest)
            {
                while (selectedQuest.Parent is { } parent) selectedQuest = parent;
                selectedId = selectedQuest.Id;
            }
            int selectedRow = selectedId is { } keepId ? Array.FindIndex(rows, row => TaskIndex.IdOf(row) == keepId) : -1;
            int pageStart = selectedRow < 0 ? 0 : selectedRow / QuestPageSize * QuestPageSize;
            _bindingQuestTree = true; _qTree.BeginUpdate();
            try
            {
                _qTree.Nodes.Clear();
                if (pageStart > 0) _qTree.Nodes.Add(new TreeNode($"点击加载前面的任务（{pageStart:N0} 项）")
                    { Tag = new TreePage(rows, 0, QuestRowNode, pageStart) });
                AppendTreePage(_qTree.Nodes, new TreePage(rows, pageStart, QuestRowNode));
            }
            finally { _qTree.EndUpdate(); _bindingQuestTree = false; }
            TreeNode? preferred = selectedId is null ? null : _qTree.Nodes.Cast<TreeNode>().FirstOrDefault(n =>
                n.Tag is QuestNode q && q.Id == selectedId || n.Tag is QuestEntry entry && entry.Id == selectedId);
            if (preferred is not null) _qTree.SelectedNode = preferred;
            else if (_qTree.SelectedNode is null && _qTree.Nodes.Count > 0)
                _qTree.SelectedNode = _qTree.Nodes[0];
            else if (rows.Length == 0) ResetQuestDetails();
            if (_qTree.SelectedNode?.Tag is QuestNode or QuestEntry) ShowQuest(_qTree.SelectedNode.Tag);
            if (!_tasksDirty) _qStatus.Text = $"找到 {rows.Length:N0} 个任务 · 每次显示 {QuestPageSize} 个，点击列表末尾继续加载。" +
                (tasks.CanWrite ? "" : " · 只读：" + tasks.ReadOnlyReason);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed && revision == _questViewRevision) _qStatus.Text = "任务列表刷新失败：" + ChineseDialogs.Explain(ex); }
        finally
        {
            if (ReferenceEquals(_questSearchCts, cancellation)) _questSearchCts = null;
            cancellation.Dispose();
        }
    }

    private const int QuestPageSize = 200;
    private sealed record TreePage(IReadOnlyList<object> Items, int Start, Func<object, TreeNode> Create, int Stop = int.MaxValue);
    private static TreeNode QuestRowNode(object row) => row is QuestNode node ? QuestTreeNode(node)
        : new TreeNode(TaskIndex.NameOf(row)) { Tag = row };

    private static void AppendTreePage(TreeNodeCollection nodes, TreePage page, int? insertAt = null)
    {
        int stop = Math.Min(page.Stop, page.Items.Count);
        int end = Math.Min(page.Start + QuestPageSize, stop);
        var batch = new List<TreeNode>(end - page.Start + 1);
        for (int i = page.Start; i < end; i++) batch.Add(page.Create(page.Items[i]));
        if (end < stop)
            batch.Add(new TreeNode($"点击加载后续任务或对话（还剩 {stop - end:N0} 项）") { Tag = page with { Start = end } });
        if (insertAt is { } at) for (int i = 0; i < batch.Count; i++) nodes.Insert(at + i, batch[i]);
        else nodes.AddRange(batch.ToArray());
    }

    private void LoadMore(TreeView tree, TreeNode more)
    {
        if (more.Tag is not TreePage page) return;
        var nodes = more.Parent?.Nodes ?? tree.Nodes;
        int at = more.Index;
        bool quests = ReferenceEquals(tree, _qTree);
        if (quests) _bindingQuestTree = true; else _bindingTalk = true;
        tree.BeginUpdate();
        try { more.Remove(); AppendTreePage(nodes, page, at); }
        finally { tree.EndUpdate(); if (quests) _bindingQuestTree = false; else _bindingTalk = false; }
        if (at < nodes.Count)
        {
            tree.SelectedNode = nodes[at];
            if (quests) ShowQuest(nodes[at].Tag); else SelectTalk(nodes[at].Tag);
        }
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
        _qTree.BeginUpdate();
        try { tn.Nodes.Clear(); AppendTreePage(tn.Nodes, new TreePage(n.Children, 0, QuestRowNode)); }
        finally { _qTree.EndUpdate(); }
    }

    private void DrawQuestNode(object? s, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null) return;
        bool sel = (e.State & TreeNodeStates.Selected) != 0;
        int width = Math.Max(0, _qTree.ClientSize.Width - e.Bounds.X);
        if (width == 0) return;
        var r = new Rectangle(e.Bounds.X, e.Bounds.Y, width, e.Bounds.Height);
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
        if (r.Width <= 66) return;
        var fg = sel ? Color.White : changed ? GGold : col is { } c && c.GetBrightness() > 0.25f ? c : CTreeFg;
        TextRenderer.DrawText(e.Graphics, (changed ? "● " : "") + (text.Length == 0 ? "（未命名）" : text), _qTree.Font, new Rectangle(r.X + 64, r.Y, r.Width - 66, r.Height), fg, flags);
    }

    private sealed record QuestFieldView(TaskField Field, string Value, string Hint);
    private sealed record QuestDetail(QuestNode Owner, QuestFieldView[] Fields, string[] Texts);

    private static string DisplayQuestText(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    private static QuestDetail PrepareQuestDetail(QuestNode quest, TasksFile tasks, RefIndex? refs, CancellationToken token)
    {
        var fields = new QuestFieldView[quest.Fields.Count];
        for (int i = 0; i < fields.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var field = quest.Fields[i]; var value = quest.Get(field);
            fields[i] = new QuestFieldView(field, value, QuestFieldHint(field, value, tasks, refs));
        }
        var texts = new string[4];
        for (int i = 0; i < texts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            texts[i] = DisplayQuestText(quest.Texts[i] ?? "");
        }
        token.ThrowIfCancellationRequested();
        return new QuestDetail(quest, fields, texts);
    }

    private bool CommitQuestEditors()
    {
        bool committed = _qGrid.EndEdit();
        for (int i = 0; i < 4; i++) ApplyQuestText(i);
        ApplyTalkText();
        return committed;
    }

    private void ResetQuestDetails()
    {
        // 显式重置会话时取消未通过校验的单元格，不能让它阻止清空控件。
        if (!CommitQuestEditors()) _qGrid.CancelEdit();
        _questDetailCts?.Cancel(); _questDetailCts = null; _questDetailRevision++;
        _questDetail = null; _selectedQuest = null; _qCur = null;
        _bindingQuestDetails = true; _bindingTalk = true;
        try
        {
            _questTextOwner = null; _questTalkOwner = null; _questGridOwner = null; _qTalkSel = null; _questTalkNode = null;
            _qGrid.Rows.Clear(); _qGrid.Enabled = false;
            _qName.Clear(); _qName.ReadOnly = true; _qInfo.Text = "请选择任务";
            foreach (var box in _qTexts) { box.Clear(); box.ReadOnly = true; }
            _qTalkTree.Nodes.Clear(); _qTalkText.Clear(); _qTalkText.ReadOnly = true; _talkDisplay = "";
        }
        finally { _bindingQuestDetails = false; _bindingTalk = false; }
    }

    private void ResetQuestSession()
    {
        _questSearchDelay.Stop(); _questSearchCts?.Cancel(); _questViewRevision++;
        ResetQuestDetails();
        _bindingQuestTree = true;
        try { _qTree.Nodes.Clear(); }
        finally { _bindingQuestTree = false; }
    }

    private async void ShowQuest(object? tag, bool force = false)
    {
        if (tag is not (QuestNode or QuestEntry)) { if (tag is null) ResetQuestDetails(); return; }
        if (!force && ReferenceEquals(tag, _selectedQuest) && (_questDetail is not null || _questDetailCts is not null)) return;
        ResetQuestDetails();
        _selectedQuest = tag; _qCur = tag as QuestNode;
        string name = TaskIndex.NameOf(tag); int id = TaskIndex.IdOf(tag);
        var (text, col) = TasksFile.SplitColor(name);
        _qInfo.Text = $"{(text.Length == 0 ? "（未命名）" : text)}     (ID {id})" + (_qCur?.Children.Count > 0 ? $"   ·   {_qCur.Children.Count} 个子任务" : "");
        _qInfo.ForeColor = col is { } c && c.GetBrightness() < 0.85f ? c : CFg;
        _qName.Text = name; _qName.ReadOnly = _tasks?.CanWrite != true;
        if (_qCur is null) { _qStatus.Text = _tasks?.CanWrite == true ? "此版本仅支持任务名称编辑。" : "只读：" + _tasks?.ReadOnlyReason; return; }
        // 未切到任务页时，不创建字段控件，也不创建对话节点。
        if (!_qTree.Visible || _tasks is null) return;
        var quest = _qCur; var tasks = _tasks; var refs = _refs;
        int revision = _questDetailRevision;
        var cancellation = new CancellationTokenSource(); _questDetailCts = cancellation;
        bool complete = false;
        bool Current() => !IsDisposed && revision == _questDetailRevision && ReferenceEquals(quest, _qCur) && ReferenceEquals(tasks, _tasks);
        try
        {
            _qStatus.Text = $"正在准备任务 {id} 的字段和引用…";
            var detail = await Task.Run(() => PrepareQuestDetail(quest, tasks, refs, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            _questDetail = detail; RenderQuestTab();
            string? group = null;
            for (int start = 0; start < detail.Fields.Length; start += 32)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Current()) return;
                _bindingQuestDetails = true;
                try
                {
                    for (int i = start; i < Math.Min(start + 32, detail.Fields.Length); i++)
                    {
                        var row = detail.Fields[i]; var field = row.Field;
                        if (field.Group != group)
                        {
                            group = field.Group;
                            int hr = _qGrid.Rows.Add("  " + group, "", "");
                            var header = _qGrid.Rows[hr]; header.ReadOnly = true;
                            header.DefaultCellStyle.BackColor = CSurface2; header.DefaultCellStyle.ForeColor = CAccent;
                            header.DefaultCellStyle.Font = _questGroupFont;
                        }
                        int at = _qGrid.Rows.Add("  " + field.Name, row.Value, row.Hint);
                        _qGrid.Rows[at].Tag = field;
                        _qGrid.Rows[at].Cells[1].ReadOnly = !field.Editable || !tasks.CanWrite;
                        if (!field.Editable || !tasks.CanWrite)
                        {
                            _qGrid.Rows[at].Cells[1].Style.ForeColor = CMuted;
                            _qGrid.Rows[at].Cells[1].ToolTipText = tasks.CanWrite
                                ? "此字段控制任务结构，当前只允许查看。" : tasks.ReadOnlyReason;
                        }
                    }
                }
                finally { _bindingQuestDetails = false; }
                _qStatus.Text = $"正在显示任务 {id}：{Math.Min(start + 32, detail.Fields.Length):N0}/{detail.Fields.Length:N0} 个字段";
                if (start + 32 < detail.Fields.Length) await Task.Delay(1, cancellation.Token);
            }
            if (!Current()) return;
            complete = true;
            _qStatus.Text = $"任务 {id} · {detail.Fields.Length:N0} 个字段 · 对话按需展开" + (_tasksDirty ? " · 有未保存的修改" : "") +
                (tasks.CanWrite ? "" : " · 只读：" + tasks.ReadOnlyReason);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (Current()) _qStatus.Text = "任务详情显示失败：" + ChineseDialogs.Explain(ex); }
        finally
        {
            if (ReferenceEquals(_questDetailCts, cancellation)) _questDetailCts = null;
            if (Current()) { _qGrid.Enabled = complete; _questGridOwner = complete ? quest : null; }
            cancellation.Dispose();
        }
    }

    private void RenderQuestTab()
    {
        var detail = _questDetail;
        if (detail is null || !ReferenceEquals(detail.Owner, _qCur)) return;
        if (_questDetailTabs.SelectedIndex == 1 && !ReferenceEquals(_questTextOwner, detail.Owner))
        {
            _bindingQuestDetails = true;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    _qTexts[i].Text = detail.Texts[i]; _questTextDisplay[i] = detail.Texts[i];
                    _qTexts[i].ReadOnly = _tasks?.CanWrite != true;
                }
                _questTextOwner = detail.Owner;
            }
            finally { _bindingQuestDetails = false; }
        }
        if (_questDetailTabs.SelectedIndex == 2 && !ReferenceEquals(_questTalkOwner, detail.Owner))
        {
            _bindingTalk = true; _qTalkTree.BeginUpdate();
            try
            {
                _qTalkSel = null; _questTalkNode = null; _talkDisplay = ""; _qTalkText.Clear(); _qTalkText.ReadOnly = true;
                _qTalkTree.Nodes.Clear();
                for (int i = 0; i < detail.Owner.Talks.Length; i++)
                {
                    var proc = detail.Owner.Talks[i]; if (proc is null) continue;
                    var node = new TreeNode($"{QuestNode.TalkNames[i]}（{proc.Windows.Count} 个窗口）") { Tag = proc };
                    if (proc.Windows.Count > 0) node.Nodes.Add(new TreeNode("展开加载对话窗口…"));
                    _qTalkTree.Nodes.Add(node);
                }
                _questTalkOwner = detail.Owner;
            }
            finally { _qTalkTree.EndUpdate(); _bindingTalk = false; }
        }
    }

    private static TreeNode TalkWindowNode(object row)
    {
        var window = (TalkWindow)row;
        var node = new TreeNode($"对话窗口 {window.Id}：{Trunc(window.Text, 50)}") { Tag = window };
        if (window.Options.Count > 0) node.Nodes.Add(new TreeNode("展开加载对话选项…"));
        return node;
    }

    private static TreeNode TalkOptionNode(object row)
    {
        var option = (TalkOption)row;
        return new TreeNode($"→ {Trunc(option.Text, 50)}（跳转至 {option.Id}）") { Tag = option };
    }

    private void ExpandTalkNode(TreeNode node)
    {
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag is not null) return;
        TreePage? page = node.Tag switch
        {
            TalkProc proc => new TreePage(proc.Windows, 0, TalkWindowNode),
            TalkWindow window => new TreePage(window.Options, 0, TalkOptionNode), _ => null,
        };
        if (page is null) return;
        _bindingTalk = true; _qTalkTree.BeginUpdate();
        try { node.Nodes.Clear(); AppendTreePage(node.Nodes, page); }
        finally { _qTalkTree.EndUpdate(); _bindingTalk = false; }
    }

    private static string Trunc(string s, int n) =>
        s[..Math.Min(s.Length, n)].Replace('\r', ' ').Replace('\n', ' ') + (s.Length > n ? "…" : "");

    private void ApplyQuestName()
    {
        if (_qName.ReadOnly || _tasks?.CanWrite != true) return;
        switch (_qTree.SelectedNode?.Tag)
        {
            case QuestNode q: q.Name = _qName.Text; break;
            case QuestEntry qe: qe.Name = _qName.Text; break;
            default: return;
        }
        _qTree.SelectedNode!.Text = _qName.Text;
        _qTree.Invalidate();
        ShowQuest(_qTree.SelectedNode.Tag, force: true);
        MarkTasksDirty("已修改名称");
    }

    private void ApplyQuestText(int i)
    {
        if (_bindingQuestDetails || _tasks?.CanWrite != true || _questTextOwner is not { } owner || !ReferenceEquals(owner, _qCur)) return;
        if (_qTexts[i].Text == _questTextDisplay[i]) return;
        var v = _qTexts[i].Text.Replace("\r\n", "\r");
        _questTextDisplay[i] = _qTexts[i].Text;
        if (v == owner.Texts[i]) return;
        owner.Texts[i] = v; owner.TextsChanged = true;
        _qTree.Invalidate();
        MarkTasksDirty($"已修改{QuestNode.TextNames[i]}");
    }

    private void SelectTalk(object? tag)
    {
        if (_bindingTalk || _questTalkOwner is null || !ReferenceEquals(_questTalkOwner, _qCur)) return;
        if (ReferenceEquals(tag, _qTalkSel) && ReferenceEquals(_questTalkNode, _qTalkTree.SelectedNode)) return;
        ApplyTalkText();
        _bindingTalk = true;
        try
        {
            _qTalkSel = tag;
            _questTalkNode = _qTalkTree.SelectedNode;
            _qTalkText.ReadOnly = tag is null || _tasks?.CanWrite != true;
            _qTalkText.MaxLength = tag is TalkWindow ? 32767 : 63;
            _talkDisplay = DisplayQuestText(tag switch
            {
                TalkProc tp => tp.Text, TalkWindow w => w.Text, TalkOption o => o.Text, _ => "",
            });
            _qTalkText.Text = _talkDisplay;
        }
        finally { _bindingTalk = false; }
    }

    private void ApplyTalkText()
    {
        if (_bindingTalk || _tasks?.CanWrite != true || _questTalkOwner is not { } owner || !ReferenceEquals(owner, _qCur) || _qTalkSel is null) return;
        if (_qTalkText.Text == _talkDisplay) return;
        var v = _qTalkText.Text.Replace("\r\n", "\r");
        _talkDisplay = _qTalkText.Text;
        bool changed = false;
        switch (_qTalkSel)
        {
            case TalkProc tp when tp.Text != v: tp.Text = v; changed = true; break;
            case TalkWindow w when w.Text != v: w.Text = v; changed = true; break;
            case TalkOption o when o.Text != v: o.Text = v; changed = true; break;
        }
        if (!changed) return;
        owner.TextsChanged = true;
        if (_questTalkNode is { } sn && ReferenceEquals(sn.Tag, _qTalkSel))
            sn.Text = _qTalkSel switch { TalkWindow w => $"对话窗口 {w.Id}：{Trunc(w.Text, 50)}", TalkOption o => $"→ {Trunc(o.Text, 50)}（跳转至 {o.Id}）", _ => sn.Text };
        _qTree.Invalidate();
        MarkTasksDirty("已修改对话");
    }

    private void SaveTasks() => SaveTaskData(saveAs: false);

    private void SaveTaskData(bool saveAs)
    {
        if (_saving) return;
        if (_tasks is null || _tasksPath is null) { _qStatus.Text = "请先打开 tasks.data。"; return; }
        if (!_tasks.CanWrite) { ChineseDialogs.Show(this, _tasks.ReadOnlyReason ?? "任务写回校验未通过。", "当前任务数据不能保存"); return; }
        if (!CommitQuestEditors()) { _qStatus.Text = "请先修正当前任务字段的值，再保存。"; return; }
        if (!_qName.ReadOnly && _selectedQuest is { } selected && _qName.Text != TaskIndex.NameOf(selected))
        {
            if (selected is QuestNode node) node.Name = _qName.Text;
            else if (selected is QuestEntry entry) entry.Name = _qName.Text;
            if (_qTree.SelectedNode is { } treeNode) treeNode.Text = _qName.Text;
            MarkTasksDirty("已修改名称");
        }
        if (!_tasksDirty && !saveAs) { _qStatus.Text = "没有待保存的修改。"; return; }
        string target = _tasksPath;
        if (saveAs)
        {
            using var dialog = new SaveFileDialog { Filter = "任务数据 (*.data)|*.data", Title = "另存为任务数据", FileName = Path.GetFileName(_tasksPath), InitialDirectory = Path.GetDirectoryName(_tasksPath) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            target = dialog.FileName;
        }
        else if (!ChineseDialogs.Confirm(this, $"将修改保存到：\n{target}\n\n将在同一目录中创建备份。\n请确保服务端使用相同的 tasks.data（可在“一致性检查”页检查）。",
                "保存 tasks.data")) return;
        SaveReceipt? saved = null;
        try
        {
            _saving = true;
            int? keep = _selectedQuest is QuestNode or QuestEntry ? TaskIndex.IdOf(_selectedQuest) : null;
            var tasks = _tasks;
            saved = ReadProgressDialog.Run(this, "保存任务数据", context =>
                new SaveReceipt(Path.GetFullPath(target), tasks.Save(target, context)), allowCancel: false, completedStage: "保存完成");
            _tasksPath = saved.Path;
            _questSelectionAfterLoad = keep;
            bool refreshed = LoadTasks(saved.Path);
            _qStatus.Text = $"磁盘已保存：{saved.Path}" + (saved.Backup is null ? "" : $" · 备份：{Path.GetFileName(saved.Backup)}") +
                (refreshed ? "" : " · 界面重读未完成，可手动重新打开；内存修改仍保留。");
            Status(_qStatus.Text);
        }
        catch (Exception ex) { Error(saved is null ? "保存 tasks.data 失败，内存修改仍保留" : "任务文件已保存，界面更新未完成", ex); }
        finally { _saving = false; }
    }

    // ---------- aba Verificação (par cliente × servidor) ----------
    private readonly TextBox _chkClient = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _chkServer = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly ListView _chkList = new() { View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly Label _chkSummary = new() { Dock = DockStyle.Top, Height = 34, Font = new Font("Microsoft YaHei UI", 11), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };

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
        form.Controls.Add(L("客户端（element 文件夹）"), 0, 0); form.Controls.Add(_chkClient, 1, 0);
        form.Controls.Add(B("浏览…", () => PickFolder(_chkClient, "客户端的 element 文件夹")), 2, 0);
        form.Controls.Add(L("服务端（home 文件夹）"), 0, 1); form.Controls.Add(_chkServer, 1, 1);
        form.Controls.Add(B("浏览…", () => PickFolder(_chkServer, "服务端 home 文件夹（包含 gamed、glinkd 等）")), 2, 1);
        var run = B("✔ 开始检查", RunCheck); run.BackColor = CAccent; run.ForeColor = Color.White; run.Font = new Font("Microsoft YaHei UI", 10);
        form.Controls.Add(new Label { Text = "比较登录所需的文件版本和时间戳（元素、任务、商城），并检查各地图副本及网络加密配置（不显示密钥）。", Dock = DockStyle.Fill, ForeColor = CMuted, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        form.SetColumnSpan(form.GetControlFromPosition(0, 2)!, 2);
        form.Controls.Add(run, 2, 2);
        for (int i = 0; i < 3; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _chkList.Columns.Add("", 34);
        _chkList.Columns.Add("检查项", 300);
        _chkList.Columns.Add("客户端", 170);
        _chkList.Columns.Add("服务端", 170);
        _chkList.Columns.Add("说明", 520);
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
        if (!Directory.Exists(_chkClient.Text) || !Directory.Exists(_chkServer.Text)) { Status("请选择客户端和服务端文件夹。"); return; }
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
        _chkSummary.Text = err > 0 ? $"✖ {err} 个影响登录的错误 · {warn} 个警告"
                         : warn > 0 ? $"⚠ 未发现登录错误 · {warn} 个警告" : "✔ 客户端与服务端检查一致";
        _chkSummary.ForeColor = err > 0 ? Color.FromArgb(200, 30, 30) : warn > 0 ? Color.FromArgb(180, 110, 0) : Color.FromArgb(22, 128, 61);
        Status("检查完成");
    }

    // ---------- aba Extrator PCK (invólucro do sPCK) ----------
    private Label _lblSpck = null!, _lblIcons = null!;
    private Control BuildPckView()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = CBg, Padding = new Padding(20) };
        int y = 10;
        Label Title(string t) { var l = new Label { Text = t, Location = new Point(14, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), ForeColor = CFg }; y += 34; return l; }
        Label Info(string t) { var l = new Label { Text = t, Location = new Point(14, y), AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = CMuted }; y += l.PreferredHeight + 10; return l; }
        Button Btn(string t, Action a) { var b = new Button { Text = t, Location = new Point(14, y), Size = new Size(260, 30), FlatStyle = FlatStyle.Flat, BackColor = CSurface, ForeColor = CFg }; b.Click += (_, _) => a(); y += 40; return b; }

        p.Controls.Add(Title(".pck 资源包"));
        p.Controls.Add(Info("打开《完美世界》文件夹时，程序会自动从 surfaces.pck 直接读取物品图标，无需解包。如果部分图标未显示，可使用下方的 sPCK 与解包目录作为备用方式。"));
        p.Controls.Add(Btn("1）选择 sPCK-GUI.exe", LocateSpck));
        _lblSpck = new Label { Location = new Point(290, y - 34), AutoSize = true, ForeColor = CMuted, MaximumSize = new Size(520, 0) };
        p.Controls.Add(_lblSpck);
        p.Controls.Add(Btn("2）打开 sPCK 解包", OpenSpck));
        p.Controls.Add(Info("在 sPCK 中，将图标所在的 .pck 资源包（例如 interfaces.pck）解压到一个文件夹。"));
        p.Controls.Add(Btn("3）设置解包后的图标文件夹", SetIconsFolder));
        _lblIcons = new Label { Location = new Point(290, y - 34), AutoSize = true, ForeColor = CMuted, MaximumSize = new Size(520, 0) };
        p.Controls.Add(_lblIcons);
        p.Controls.Add(Info("设置完成后，在“元素数据”页选择物品，即可在名称旁显示图标（支持 PNG/BMP/TGA）。"));
        RefreshPckLabels();
        return p;
    }

    private void RefreshPckLabels()
    {
        _lblSpck.Text = LoadSetting("spck") ?? "（未设置，将尝试默认路径）";
        _lblIcons.Text = LoadSetting("icons") ?? "（未设置）";
    }
    private void LocateSpck()
    {
        using var dlg = new OpenFileDialog { Filter = "sPCK 解包程序 (*.exe)|*.exe", Title = "请选择 sPCK-GUI.exe" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { SaveSetting("spck", dlg.FileName); RefreshPckLabels(); }
    }
    private void OpenSpck()
    {
        var path = LoadSetting("spck") ?? DefaultSpck();
        if (path is null || !File.Exists(path)) { Status("未找到 sPCK，请先点击“选择 sPCK-GUI.exe”。"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! }); }
        catch (Exception ex) { Error("打开 sPCK 失败", ex); }
    }
    private static string? DefaultSpck()
    {
        foreach (var c in new[] { @"D:\Perfect World\PW\Outros Editores\sPCK GUI\sPCK-GUI.exe" })
            if (File.Exists(c)) return c;
        return null;
    }
    private void SetIconsFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "从 .pck 解包得到的图标文件夹", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SaveSetting("icons", dlg.SelectedPath); RefreshPckLabels();
            _iconsRoot = dlg.SelectedPath; _iconIdx.Clear(); _treeIcons.Images.Clear();
            RefreshView();
            Status("已设置图标文件夹，列表图标已启用。");
        }
    }

    // ---------- settings genéricas ----------
    private static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor");
    private static void SaveSetting(string k, string v) { try { Directory.CreateDirectory(SettingsDir); File.WriteAllText(Path.Combine(SettingsDir, k + ".txt"), v); } catch { } }
    private static string? LoadSetting(string k) { try { var p = Path.Combine(SettingsDir, k + ".txt"); return File.Exists(p) ? File.ReadAllText(p).Trim() : null; } catch { return null; } }

    private void Status(string s) => _status.Text = s;
    private void Error(string msg, Exception ex)
    {
        string explanation = ChineseDialogs.Explain(ex);
        Status(msg + "：" + explanation);
        ChineseDialogs.Show(this, explanation, msg, details: ex.Message);
    }
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
