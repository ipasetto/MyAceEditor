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
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "  Pesquisar item por nome ou ID…", BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox _blockCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _typeCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
    private readonly CheckBox _searchAll = new() { Dock = DockStyle.Right, Width = 22, Padding = new Padding(6, 0, 0, 0) };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, OwnerDraw = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Clickable };
    private readonly NumericUpDown _gotoId = new() { Maximum = 999999999, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center };
    private int _hoverIdx = -1;
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI Semibold", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly ToolStripStatusLabel _status = new("Abra a pasta do Perfect World para começar.");
    private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };
    private string[] _tipLines = Array.Empty<string>();
    private readonly PictureBox _iconBox = new() { Dock = DockStyle.Left, Width = 44, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(6) };
    private readonly ImageList _treeIcons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Dictionary<string, int> _iconIdx = new();
    private string? _iconsRoot;
    private IconAtlas? _atlas;
    private string? _atlasPck;
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
        Text = "MyAceEditor";
        Width = 1320; Height = 840;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        BackColor = CBg; ForeColor = CFg;
        Font = new Font("Segoe UI", 9.5f);

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
        ToolStripMenuItem M(string t) => new(t) { ForeColor = CFg };
        var arq = M("&Arquivo");
        arq.DropDownItems.Add("Abrir &pasta do Perfect World…", null, (_, _) => OpenPwFolder());
        arq.DropDownItems.Add("Abrir &elements.data…", null, (_, _) => OpenElements());
        arq.DropDownItems.Add(new ToolStripSeparator());
        arq.DropDownItems.Add("&Salvar…", null, (_, _) => SaveAs());
        arq.DropDownItems.Add("Sa&ir", null, (_, _) => Close());
        var ed = M("&Editar");
        ed.DropDownItems.Add("&Novo item", null, (_, _) => AddItem());
        ed.DropDownItems.Add("&Remover item", null, (_, _) => RemoveItem());
        var aj = M("A&juda");
        aj.DropDownItems.Add("Sobre", null, (_, _) => MessageBox.Show("MyAceEditor — editor dos dados do Perfect World.", "Sobre"));
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, aj });
        MainMenuStrip = menu;
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var tb = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = CSurface2, ForeColor = CFg, Renderer = new LightRenderer(), Padding = new Padding(6, 3, 6, 3) };
        ToolStripButton B(string t, Action a) { var b = new ToolStripButton(t) { ForeColor = CFg, Font = new Font("Segoe UI", 9.5f) }; b.Click += (_, _) => a(); return b; }
        tb.Items.Add(B("Abrir pasta do PW", OpenPwFolder));
        tb.Items.Add(B("Abrir elements.data", OpenElements));
        tb.Items.Add(B("Salvar", SaveAs));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B("＋ Adicionar", AddItem));
        tb.Items.Add(B("🗑 Excluir", RemoveItem));
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
        var tEl = new TabPage("Elements") { BackColor = CBg };
        tEl.Controls.Add(BuildElements());
        tabs.TabPages.Add(tEl);
        foreach (var (n, h) in new[] { ("GShop", "Editor de loja"), ("Tasks", "Editor de tarefas"), ("AI Policy", "Editor de IA") })
        {
            var tp = new TabPage(n) { BackColor = CBg };
            tp.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = CMuted, Text = $"{h}\n(em breve)", Font = new Font("Segoe UI", 11) });
            tabs.TabPages.Add(tp);
        }
        var tPck = new TabPage("Extrator PCK") { BackColor = CBg };
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
        _list.Columns.Add("Nome", 240);
        _list.RetrieveVirtualItem += OnRetrieveItem;
        _list.DrawColumnHeader += OnDrawHeader;
        _list.DrawSubItem += OnDrawSubItem;
        _list.ColumnClick += (_, e) => OnColumnClick(e.Column);
        _list.SelectedIndexChanged += (_, _) => OnListSelect();
        _list.Resize += (_, _) => _list.Columns[1].Width = Math.Max(120, _list.ClientSize.Width - _list.Columns[0].Width - 1);
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
        _tip.SetToolTip(_gotoId, "Ir para o ID (Enter)");
        foot.Controls.Add(FootBtn("＋ Adicionar", AddItem), 0, 0);
        foot.Controls.Add(_gotoId, 1, 0);
        foot.Controls.Add(FootBtn("✕ Excluir", RemoveItem), 2, 0);

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
        _header.BackColor = CSurface2; _header.ForeColor = CFg;
        var headPanel = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = CSurface2 };
        _iconBox.BackColor = CSurface2;
        headPanel.Controls.Add(_header);
        headPanel.Controls.Add(_iconBox);
        StyleGrid();
        right.Controls.Add(_grid);
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
        _grid.ColumnCount = 2;
        _grid.Columns[0].Name = "Campo"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 230;
        _grid.Columns[0].DefaultCellStyle.ForeColor = CMuted;
        _grid.Columns[1].Name = "Valor"; _grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f);
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
    }

    private StatusStrip BuildStatus()
    {
        var s = new StatusStrip { BackColor = CSurface2, ForeColor = CMuted };
        s.Items.Add(_status);
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

    private void LoadElements(string path)
    {
        try
        {
            UseWaitCursor = true;
            var reader = new CfgElementsReader(path);
            if (!reader.SignatureValid) { Status($"Assinatura inválida (0x{reader.Signature:X8})."); return; }
            var cfg = FindCfg(reader.Version);
            if (cfg is null) { Status($"Sem definição .cfg para a versão v{reader.Version & 0xFF}."); return; }
            var module = reader.Parse(ElementsCfg.Load(cfg));
            module.Name = Path.GetFileName(path);
            _store.Adopt(module);
            LoadAtlas(path);
            BuildTree();
            SaveLast(path);
            int items = module.Lists.Sum(l => l.Items.Count);
            Status($"{module.Name} · {module.Lists.Count} blocos · {items:N0} itens · {100.0 * reader.Position / reader.Length:0.0}%"
                + (_atlas is null ? " · ícones: surfaces.pck não encontrado" : $" · {_atlas.Count:N0} ícones do jogo"));
        }
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
        finally { UseWaitCursor = false; }
    }

    // Ícones nativos: lê o atlas direto do surfaces.pck (sem extrair nada).
    private void LoadAtlas(string elementsPath)
    {
        var pck = IconAtlas.FindSurfacesPck(elementsPath);
        if (pck == _atlasPck && _atlas is not null) return;
        _atlas = null; _atlasPck = pck;
        _iconIdx.Clear(); _treeIcons.Images.Clear();
        if (pck is null) return;
        try { _atlas = IconAtlas.Load(pck); } catch { _atlas = null; }
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

    private static string? FindCfg(uint version)
    {
        int v = (int)(version & 0xFF);
        var dir = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg");
        return Directory.Exists(dir) ? (Directory.GetFiles(dir, $"*_v{v}.cfg").FirstOrDefault() ?? Directory.GetFiles(dir, $"*v{v}.cfg").FirstOrDefault()) : null;
    }

    private void SaveAs()
    {
        if (_store.Current is null) { Status("Nada para salvar"); return; }
        using var dlg = new SaveFileDialog { Filter = "Módulo (*.data)|*.data", FileName = _store.Current.Name };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { Status($"Salvo em {_store.Save(dlg.FileName)} (escrita binária do PW: em breve)"); }
        catch (Exception ex) { Error("Falha ao salvar", ex); }
    }

    // ---------- lista de itens (estilo Rody: seletor de bloco + lista plana) ----------
    // Entrada do seletor de bloco. Block == null => Favoritos.
    private sealed record BlockEntry(ItemList? Block, string Label)
    {
        public override string ToString() => Label;
    }
    // Entrada do filtro de tipo (Major/Sub null => todos).
    private sealed record TypeEntry(string? Major, string? Sub, string Label)
    {
        public override string ToString() => Label;
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
            _blockCombo.Items.Add(new BlockEntry(null, $"★ Favoritos ({_favorites.Count})"));
            for (int i = 0; i < lists.Count; i++)
            {
                var b = lists[i];
                if (b.Items.Count == 0) continue;
                _blockCombo.Items.Add(new BlockEntry(b, $"[{i:000}] {Pretty(b.Name)} ({b.Items.Count:N0})"));
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
            _typeCombo.Items.Add(new TypeEntry(null, null, $"Todos os tipos ({b.Items.Count:N0})"));
            foreach (var byMajor in b.Items.GroupBy(i => Fv(i, "id_major_type")))
            {
                var mName = majors.GetValueOrDefault(byMajor.Key, "Tipo " + byMajor.Key);
                _typeCombo.Items.Add(new TypeEntry(byMajor.Key, null, $"{mName} ({byMajor.Count()})"));
                foreach (var bySub in byMajor.GroupBy(i => Fv(i, "id_sub_type")))
                    _typeCombo.Items.Add(new TypeEntry(byMajor.Key, bySub.Key,
                        $"     {mName} › {subs.GetValueOrDefault(bySub.Key, "Subtipo " + bySub.Key)} ({bySub.Count()})"));
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
        var col = o is BlockEntry { Block: null } ? GGold : CTreeFg;
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

        int total = _curBlock?.Items.Count ?? _view.Count;
        _list.Columns[1].Text = $"Nome ({_view.Count:N0} de {total:N0})";
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
        e.Item.SubItems.Add(string.IsNullOrEmpty(it.Name) ? "(sem nome)" : it.Name);
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
        TextRenderer.DrawText(g, e.SubItem!.Text, _list.Font, r, sel ? Color.White : GradeColor(it.Name), flags);
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
        ["EQUIPMENT_ADDON"] = "Addons de Equipamento", ["MINE_ESSENCE"] = "Minas", ["SKILLTOME_ESSENCE"] = "Livros de Habilidade",
        ["FLYSWORD_ESSENCE"] = "Montarias Voadoras", ["TASKMATTER_ESSENCE"] = "Itens de Missão", ["TALK_PROC"] = "Diálogos de NPC",
    };
    private static string Pretty(string block) =>
        BlockPt.TryGetValue(block, out var p) ? p
        : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(block.Replace("_ESSENCE", "").Replace("_", " ").ToLowerInvariant());

    // ---------- detalhes ----------
    private void ShowItem(Item item)
    {
        _header.Text = string.IsNullOrEmpty(item.Name) ? $"#{item.Id}" : $"{item.Name}     (ID {item.Id})";
        _header.ForeColor = Color.FromArgb(30, 39, 51);
        LoadItemIcon(item);
        _grid.Rows.Clear();
        var groups = item.Fields.GroupBy(f => FieldLabels.Group(f.Key)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var gname in FieldLabels.GroupOrder)
        {
            if (!groups.TryGetValue(gname, out var fields)) continue;
            var vis = fields.Where(f => gname != "Atributos / Addons" || !IsEmpty(f.Value.Value)).ToList();
            if (vis.Count == 0) continue;
            int hr = _grid.Rows.Add("  " + gname.ToUpperInvariant(), "");
            var hrow = _grid.Rows[hr]; hrow.ReadOnly = true;
            hrow.DefaultCellStyle.BackColor = CSurface2; hrow.DefaultCellStyle.ForeColor = CAccent;
            hrow.DefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            foreach (var (k, f) in vis)
            {
                int r = _grid.Rows.Add("  " + FieldLabels.Label(k), f.Value);
                _grid.Rows[r].Tag = k;
            }
        }
    }

    private static bool IsEmpty(string v) => v is "0" or "" or "0.0" or "-1";

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0 || _grid.Rows[row].Tag is not string key || !_current.Fields.ContainsKey(key)) return;
        var val = _grid.Rows[row].Cells[1].Value?.ToString() ?? "";
        _current.Fields[key] = Field.Of(_current.Fields[key].Type, val);
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            _current.Name = val;
            _list.Invalidate();
            _header.Text = $"{val}     (ID {_current.Id})";
        }
        Status($"'{FieldLabels.Label(key)}' atualizado");
    }

    // ---------- menu de contexto (clicar direito) ----------
    private ToolStripMenuItem _miFav = null!;
    private void BuildContextMenu()
    {
        _ctx.Items.Add("Novo item", null, (_, _) => NewItem());
        _ctx.Items.Add("Clonar item", null, (_, _) => CloneItem());
        _ctx.Items.Add("Excluir item", null, (_, _) => RemoveItem());
        _ctx.Items.Add(new ToolStripSeparator());
        _miFav = new ToolStripMenuItem("Favoritar", null, (_, _) => ToggleFavorite());
        _ctx.Items.Add(_miFav);
        _ctx.Opening += (_, e) =>
        {
            bool isItem = _list.SelectedIndices.Count > 0 && _current is not null;
            foreach (ToolStripItem i in _ctx.Items) i.Enabled = isItem || i.Text == "Novo item";
            if (isItem && CurrentItemBlock is { } cb)
                _miFav.Text = _favorites.Contains(FavKey(cb, _current!)) ? "Desfavoritar" : "Favoritar";
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
        block.Items.Add(ni);
        _owner[ni] = block;
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
        var ni = new Item { Id = code.ToString(), Name = "Novo item" };
        var template = block.Items.FirstOrDefault();
        if (template is not null)
            foreach (var (k, f) in template.Fields)
                ni.Fields[k] = Field.Of(f.Type, f.Type == FieldType.String ? "" : "0");
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
            Fields = _current.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value))
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
        block.Items.Remove(_current); _owner.Remove(_current);
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
        _tipColor = GradeColor(it.Name);
        _tip.Show(" ", _list, e.X + 22, e.Y + 16, 15000);
    }

    private void SizeTip(object? s, PopupEventArgs e)
    {
        using var f = new Font("Segoe UI", 9);
        int w = 120;
        foreach (var l in _tipLines) w = Math.Max(w, TextRenderer.MeasureText(l, f).Width);
        e.ToolTipSize = new Size(w + 22, _tipLines.Length * 18 + 16);
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
    }

    private static readonly string[] TipGroups = { "Básico", "Requisitos", "Ataque", "Defesa", "Durabilidade", "Economia" };
    private static string[] BuildTipLines(Item it)
    {
        var lines = new List<string> { string.IsNullOrEmpty(it.Name) ? $"#{it.Id}" : it.Name, $"ID: {it.Id}" };
        foreach (var g in TipGroups)
            foreach (var (k, f) in it.Fields)
            {
                if (FieldLabels.Group(k) != g) continue;
                if (k is "ID" or "Name" || k.StartsWith("id_") || IsEmpty(f.Value)) continue;
                lines.Add($"{FieldLabels.Label(k)}: {f.Value}");
                if (lines.Count >= 20) return lines.ToArray();
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
