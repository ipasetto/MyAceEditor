using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;

namespace MyAceEditor.UI;

/// <summary>
/// Suíte de editores de Perfect World. Topo: abas de editores (Elements, GShop, Tasks, Extrator PCK).
/// Elements: categorias+ícones à esquerda, lista de itens, e todos os campos editáveis à direita.
/// </summary>
public sealed class EditorForm : Form
{
    private const int MaxRows = 8000;

    private readonly ModuleStore _store = new();
    private readonly TreeView _cats = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true, ItemHeight = 24 };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Buscar ID ou Nome…" };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None };
    private readonly ToolStripStatusLabel _status = new("Selecione a pasta do Perfect World para começar.");
    private ImageList _icons = new() { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit };

    private static readonly Color CBg = Color.FromArgb(24, 27, 33), CPanel = Color.FromArgb(32, 36, 44),
        CPanel2 = Color.FromArgb(40, 45, 55), CLine = Color.FromArgb(55, 61, 73), CFg = Color.FromArgb(232, 235, 240),
        CMuted = Color.FromArgb(150, 158, 172), CAccent = Color.FromArgb(79, 143, 247);

    private static readonly (string cat, Color color)[] Cats =
    {
        ("Equipamentos", Color.FromArgb(91, 155, 213)), ("Itens", Color.FromArgb(120, 170, 120)),
        ("Monstros", Color.FromArgb(210, 120, 120)), ("NPCs", Color.FromArgb(200, 170, 90)),
        ("Faces", Color.FromArgb(180, 130, 200)), ("Receitas", Color.FromArgb(120, 190, 190)),
        ("Pets", Color.FromArgb(220, 150, 110)), ("Tasks", Color.FromArgb(150, 160, 180)),
    };

    private ItemList? _block;
    private Item? _current;

    public EditorForm()
    {
        Text = "MyAceEditor";
        Width = 1300; Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 620);
        BackColor = CBg; ForeColor = CFg;
        BuildIcons();

        Controls.Add(BuildEditorsTabs());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatus());
    }

    private void BuildIcons()
    {
        foreach (var (_, color) in Cats)
        {
            var bmp = new Bitmap(18, 18);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(color);
            g.FillRectangle(b, 3, 3, 12, 12);
            using var p = new Pen(Color.FromArgb(220, 230, 240), 1);
            g.DrawRectangle(p, 3, 3, 12, 12);
            _icons.Images.Add(bmp);
        }
    }
    private int IconOf(string group)
    {
        for (int i = 0; i < Cats.Length; i++) if (Cats[i].cat == group) return i;
        return 1;
    }

    // ---------------- topo ----------------
    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = CPanel, ForeColor = CFg };
        ToolStripMenuItem M(string t) => new(t) { ForeColor = CFg };
        var arq = M("&Arquivo");
        arq.DropDownItems.Add("Abrir &pasta do Perfect World…", null, (_, _) => OpenPwFolder());
        arq.DropDownItems.Add("Abrir &elements.data…", null, (_, _) => OpenElements());
        arq.DropDownItems.Add(new ToolStripSeparator());
        arq.DropDownItems.Add("&Salvar", null, (_, _) => Save(true));
        arq.DropDownItems.Add("Sa&ir", null, (_, _) => Close());
        var ed = M("&Editar");
        ed.DropDownItems.Add("&Novo item", null, (_, _) => AddItem());
        ed.DropDownItems.Add("&Remover item", null, (_, _) => RemoveItem());
        var aj = M("A&juda");
        aj.DropDownItems.Add("Sobre", null, (_, _) => MessageBox.Show(
            "MyAceEditor — suíte de editores do Perfect World.", "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, aj });
        MainMenuStrip = menu;
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var tb = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = CPanel2, ForeColor = CFg, Renderer = new DarkRenderer() };
        ToolStripButton B(string t, Action a) { var b = new ToolStripButton(t) { ForeColor = CFg }; b.Click += (_, _) => a(); return b; }
        tb.Items.Add(B("📂 Abrir pasta do PW", OpenPwFolder));
        tb.Items.Add(B("Abrir elements.data", OpenElements));
        tb.Items.Add(B("Salvar", () => Save(true)));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B("+ Adicionar", AddItem));
        tb.Items.Add(B("Excluir", RemoveItem));
        return tb;
    }

    private TabControl BuildEditorsTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed };
        tabs.DrawItem += (s, e) =>
        {
            var tp = tabs.TabPages[e.Index]; bool sel = e.Index == tabs.SelectedIndex;
            e.Graphics.FillRectangle(new SolidBrush(sel ? CBg : CPanel2), e.Bounds);
            TextRenderer.DrawText(e.Graphics, tp.Text, new Font("Segoe UI", 10, sel ? FontStyle.Bold : FontStyle.Regular),
                e.Bounds, sel ? CAccent : CMuted);
        };
        var tElements = new TabPage("Elements") { BackColor = CBg };
        tElements.Controls.Add(BuildElementsView());
        tabs.TabPages.Add(tElements);
        foreach (var (name, hint) in new[] { ("GShop", "Editor de loja (gshop.data)"), ("Tasks", "Editor de tarefas (tasks.data)"),
            ("AI Policy", "Editor de IA (aipolicy.data)"), ("Extrator PCK", "Extrair/empacotar arquivos .pck") })
        {
            var tp = new TabPage(name) { BackColor = CBg };
            tp.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = CMuted,
                Text = $"{hint}\n\n(em breve)", Font = new Font("Segoe UI", 11) });
            tabs.TabPages.Add(tp);
        }
        return tabs;
    }

    private Control BuildElementsView()
    {
        var outer = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230, BackColor = CLine };
        // coluna 1: categorias
        _cats.BackColor = CPanel; _cats.ForeColor = CFg; _cats.BorderStyle = BorderStyle.None; _cats.ImageList = _icons;
        _cats.AfterSelect += (_, e) => OnCatSelected(e.Node);
        var catHost = new Panel { Dock = DockStyle.Fill, BackColor = CPanel, Padding = new Padding(4) };
        catHost.Controls.Add(_cats);
        catHost.Controls.Add(new Label { Dock = DockStyle.Top, Height = 26, Text = "  Categorias", ForeColor = CMuted, TextAlign = ContentAlignment.MiddleLeft });
        outer.Panel1.Controls.Add(catHost);

        var inner = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340, BackColor = CLine };
        // coluna 2: lista de itens
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = CPanel, Padding = new Padding(4) };
        _list.BackColor = CPanel2; _list.ForeColor = CFg; _list.BorderStyle = BorderStyle.None; _list.SmallImageList = _icons;
        _list.Columns.Add("ID", 80); _list.Columns.Add("Nome", 230);
        _list.SelectedIndexChanged += (_, _) => OnItemSelected();
        _search.BackColor = CPanel2; _search.ForeColor = CFg; _search.BorderStyle = BorderStyle.FixedSingle;
        _search.TextChanged += (_, _) => PopulateList();
        var lh = new Panel { Dock = DockStyle.Fill }; lh.Controls.Add(_list);
        listHost.Controls.Add(lh);
        listHost.Controls.Add(_search);
        inner.Panel1.Controls.Add(listHost);

        // coluna 3: detalhes (todos os campos editáveis)
        var right = new Panel { Dock = DockStyle.Fill, BackColor = CPanel };
        _header.BackColor = CPanel2; _header.ForeColor = CAccent;
        StyleGrid();
        right.Controls.Add(_grid);
        right.Controls.Add(_header);
        inner.Panel2.Controls.Add(right);

        outer.Panel2.Controls.Add(inner);
        return outer;
    }

    private void StyleGrid()
    {
        _grid.BackgroundColor = CPanel;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = CPanel2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = CFg;
        _grid.DefaultCellStyle.BackColor = CPanel;
        _grid.DefaultCellStyle.ForeColor = CFg;
        _grid.DefaultCellStyle.SelectionBackColor = CAccent;
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = CPanel2;
        _grid.GridColor = CLine;
        _grid.ColumnCount = 2;
        _grid.Columns[0].Name = "Campo"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 230;
        _grid.Columns[0].DefaultCellStyle.ForeColor = Color.FromArgb(120, 170, 255);
        _grid.Columns[1].Name = "Valor"; _grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
    }

    private StatusStrip BuildStatus()
    {
        var s = new StatusStrip { BackColor = CPanel, ForeColor = CMuted };
        s.Items.Add(_status);
        return s;
    }

    // ---------------- carregar ----------------
    private void OpenPwFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Selecione a pasta do Perfect World (que contém 'element')" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var root = dlg.SelectedPath;
        string? ele = FindFile(root, "elements.data");
        if (ele is null) { Status("Não achei elements.data dentro dessa pasta."); return; }
        LoadElements(ele);
    }

    private static string? FindFile(string root, string name)
    {
        foreach (var c in new[] { name, Path.Combine("element", "data", name), Path.Combine("data", name), Path.Combine("element", name) })
        {
            var p = Path.Combine(root, c);
            if (File.Exists(p)) return p;
        }
        try { return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
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
            var cfgPath = FindCfg(reader.Version);
            if (cfgPath is null) { Status($"Sem .cfg para a versão v{reader.Version & 0xFF}."); return; }
            var module = reader.Parse(ElementsCfg.Load(cfgPath));
            module.Name = Path.GetFileName(path);
            _store.Adopt(module);
            PopulateCats();
            int items = module.Lists.Sum(l => l.Items.Count);
            double pct = 100.0 * reader.Position / reader.Length;
            Status($"{module.Name} · {Path.GetFileName(cfgPath)} · {module.Lists.Count} blocos · {items:N0} itens · {pct:0.0}%");
        }
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
        finally { UseWaitCursor = false; }
    }

    private static string? FindCfg(uint version)
    {
        int v = (int)(version & 0xFF);
        var dir = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg");
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, $"*_v{v}.cfg").FirstOrDefault() ?? Directory.GetFiles(dir, $"*v{v}.cfg").FirstOrDefault();
    }

    private void Save(bool saveAs)
    {
        if (_store.Current is null) { Status("Nada para salvar"); return; }
        using var dlg = new SaveFileDialog { Filter = "Módulo (*.data)|*.data", FileName = _store.Current.Name };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { Status($"Salvo em {_store.Save(dlg.FileName)} (obs: escrita no formato binário do PW ainda não implementada)"); }
        catch (Exception ex) { Error("Falha ao salvar", ex); }
    }

    // ---------------- categorias / lista ----------------
    private void PopulateCats()
    {
        _cats.BeginUpdate();
        _cats.Nodes.Clear();
        if (_store.Current is not null)
        {
            foreach (var (cat, _) in Cats)
            {
                var blocks = _store.Current.Lists.Where(l => l.Group == cat).ToList();
                if (blocks.Count == 0) continue;
                var parent = new TreeNode($"{cat} ({blocks.Sum(b => b.Items.Count):N0})") { ImageIndex = IconOf(cat), SelectedImageIndex = IconOf(cat) };
                foreach (var b in blocks)
                    parent.Nodes.Add(new TreeNode($"{b.Name}  ({b.Items.Count})") { Tag = b, ImageIndex = IconOf(cat), SelectedImageIndex = IconOf(cat) });
                _cats.Nodes.Add(parent);
            }
            if (_cats.Nodes.Count > 0) _cats.Nodes[0].Expand();
        }
        _cats.EndUpdate();
        Text = _store.Current is null ? "MyAceEditor" : $"MyAceEditor — {_store.Current.Name}";
    }

    private void OnCatSelected(TreeNode? node)
    {
        _block = node?.Tag as ItemList;
        PopulateList();
    }

    private void PopulateList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_block is not null)
        {
            string q = _search.Text.Trim();
            int icon = IconOf(_block.Group);
            int n = 0;
            foreach (var it in _block.Items)
            {
                if (q.Length > 0 && !it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !it.Id.Contains(q)) continue;
                if (n++ >= MaxRows) break;
                var lvi = new ListViewItem(it.Id) { Tag = it, ImageIndex = icon };
                lvi.SubItems.Add(it.Name);
                _list.Items.Add(lvi);
            }
        }
        _list.EndUpdate();
    }

    private void OnItemSelected()
    {
        if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is Item it) ShowItem(it);
    }

    private void ShowItem(Item item)
    {
        _current = item;
        _header.Text = $"{(string.IsNullOrEmpty(item.Name) ? "#" + item.Id : item.Name)}      [id {item.Id} · {item.Fields.Count} campos]";
        _grid.Rows.Clear();
        foreach (var (k, f) in item.Fields)
            _grid.Rows.Add(k, f.Value);
    }

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0) return;
        var key = _grid.Rows[row].Cells[0].Value?.ToString();
        if (string.IsNullOrEmpty(key) || !_current.Fields.ContainsKey(key)) return;
        var val = _grid.Rows[row].Cells[1].Value?.ToString() ?? "";
        _current.Fields[key] = Field.Of(_current.Fields[key].Type, val);
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            _current.Name = val;
            if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].SubItems[1].Text = val;
        }
        Status($"Campo '{key}' atualizado");
    }

    private void AddItem()
    {
        if (_block is null) { Status("Selecione um bloco"); return; }
        _block.Items.Add(new Item { Name = "Novo item", Id = "0" });
        PopulateList();
        Status($"Item adicionado em {_block.Name}");
    }

    private void RemoveItem()
    {
        if (_current is null || _block is null) { Status("Selecione um item"); return; }
        _block.Items.Remove(_current); _current = null;
        PopulateList();
        Status("Item removido");
    }

    private void Status(string s) => _status.Text = s;
    private void Error(string msg, Exception ex)
    {
        Status(msg + ": " + ex.Message);
        MessageBox.Show(ex.Message, msg, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { }
}
internal sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Color.FromArgb(40, 45, 55);
    public override Color ToolStripGradientMiddle => Color.FromArgb(40, 45, 55);
    public override Color ToolStripGradientEnd => Color.FromArgb(40, 45, 55);
    public override Color ButtonSelectedHighlight => Color.FromArgb(55, 61, 73);
    public override Color ButtonSelectedGradientBegin => Color.FromArgb(55, 61, 73);
    public override Color ButtonSelectedGradientEnd => Color.FromArgb(55, 61, 73);
}
