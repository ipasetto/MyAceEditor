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
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ItemHeight = 22, ShowLines = true, ShowPlusMinus = true, BorderStyle = BorderStyle.None };
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 46, Font = new Font("Segoe UI Semibold", 13), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, ColumnHeadersVisible = false };
    private readonly ToolStripStatusLabel _status = new("Abra a pasta do Perfect World para começar.");
    private SplitContainer _split = null!;

    // Paleta clara "branco aço"
    private static readonly Color CBg = Color.FromArgb(231, 236, 242), CSurface = Color.FromArgb(255, 255, 255),
        CSurface2 = Color.FromArgb(242, 245, 249), CLine = Color.FromArgb(203, 211, 221),
        CFg = Color.FromArgb(30, 39, 51), CMuted = Color.FromArgb(92, 102, 117),
        CAccent = Color.FromArgb(37, 99, 235), CSel = Color.FromArgb(219, 232, 253), CSelFg = Color.FromArgb(15, 42, 94);

    // Cores de grade (sistema de cores do PW nos nomes)
    private static readonly Color GNormal = Color.FromArgb(43, 50, 64), G1 = Color.FromArgb(31, 143, 78),
        G2 = Color.FromArgb(29, 111, 184), G3 = Color.FromArgb(142, 63, 176), GGold = Color.FromArgb(194, 121, 26);

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

        Shown += (_, _) => { if (_split is not null) _split.SplitterDistance = 340; };
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
        foreach (var (n, h) in new[] { ("GShop", "Editor de loja"), ("Tasks", "Editor de tarefas"), ("AI Policy", "Editor de IA"), ("Extrator PCK", "Extrair .pck (ícones/texturas)") })
        {
            var tp = new TabPage(n) { BackColor = CBg };
            tp.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = CMuted, Text = $"{h}\n(em breve)", Font = new Font("Segoe UI", 11) });
            tabs.TabPages.Add(tp);
        }
        return tabs;
    }

    private Control BuildElements()
    {
        _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, BackColor = CLine, Panel1MinSize = 240, SplitterDistance = 340 };

        // ESQUERDA: busca + árvore
        var left = new Panel { Dock = DockStyle.Fill, BackColor = CSurface, Padding = new Padding(8) };
        _search.BackColor = CSurface; _search.ForeColor = CFg; _search.Font = new Font("Segoe UI", 10);
        _search.TextChanged += (_, _) => OnSearch();
        _tree.BackColor = CSurface; _tree.ForeColor = CFg; _tree.LineColor = CLine;
        _tree.BeforeExpand += (_, e) => LazyLoad(e.Node);
        _tree.AfterSelect += (_, e) => { if (e.Node?.Tag is Item it) { _current = it; _curBlock = FindBlockOf(e.Node); ShowItem(it); } };
        var treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        treeHost.Controls.Add(_tree);
        left.Controls.Add(treeHost);
        left.Controls.Add(_search);
        _split.Panel1.Controls.Add(left);

        // DIREITA: cabeçalho + campos
        var right = new Panel { Dock = DockStyle.Fill, BackColor = CSurface };
        _header.BackColor = CSurface2; _header.ForeColor = CFg;
        StyleGrid();
        right.Controls.Add(_grid);
        right.Controls.Add(_header);
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
            BuildTree();
            int items = module.Lists.Sum(l => l.Items.Count);
            Status($"{module.Name} · {module.Lists.Count} blocos · {items:N0} itens · {100.0 * reader.Position / reader.Length:0.0}%");
        }
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
        finally { UseWaitCursor = false; }
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

    // ---------- árvore (categoria → tipo → subtipo → itens) ----------
    private sealed record Container(ItemList Block, List<Item> Items);

    private static readonly (string cat, string[] groups)[] CatMap =
    {
        ("Equipamentos", new[] { "Equipamentos" }), ("Itens", new[] { "Itens" }),
        ("Monstros", new[] { "Monstros" }), ("NPCs", new[] { "NPCs" }),
        ("Faces", new[] { "Faces" }), ("Receitas", new[] { "Receitas" }),
        ("Pets", new[] { "Pets" }), ("Tarefas", new[] { "Tasks" }),
    };

    private void BuildTree()
    {
        _search.Text = "";
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (_store.Current is not null)
        {
            var lists = _store.Current.Lists;
            foreach (var (cat, groups) in CatMap)
            {
                var blocks = lists.Where(l => groups.Contains(l.Group)).ToList();
                if (blocks.Count == 0) continue;
                var catNode = new TreeNode($"{cat}  ({blocks.Sum(b => b.Items.Count):N0})") { ForeColor = CFg, NodeFont = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
                foreach (var b in blocks)
                {
                    if (b.Name.Contains("_MAJOR_TYPE") || b.Name.Contains("_SUB_TYPE")) continue;
                    if (!AddHierarchy(catNode, b, lists)) AddContainer(catNode, Pretty(b.Name), b, b.Items);
                }
                if (catNode.Nodes.Count > 0) _tree.Nodes.Add(catNode);
            }
            if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand();
        }
        _tree.EndUpdate();
        Text = _store.Current is null ? "MyAceEditor" : $"MyAceEditor — {_store.Current.Name}";
    }

    private bool AddHierarchy(TreeNode parent, ItemList essence, List<ItemList> all)
    {
        if (!essence.Name.EndsWith("_ESSENCE")) return false;
        var f = essence.Items.FirstOrDefault();
        if (f is null || !f.Fields.ContainsKey("id_major_type") || !f.Fields.ContainsKey("id_sub_type")) return false;
        string prefix = essence.Name[..^"_ESSENCE".Length];
        var majors = NameMap(all.FirstOrDefault(l => l.Name == prefix + "_MAJOR_TYPE"));
        var subs = NameMap(all.FirstOrDefault(l => l.Name == prefix + "_SUB_TYPE"));
        var node = new TreeNode($"{Pretty(essence.Name)}  ({essence.Items.Count})") { ForeColor = CFg };
        foreach (var byMajor in essence.Items.GroupBy(i => i.Fields.GetValueOrDefault("id_major_type")?.Value ?? "0"))
        {
            var mNode = new TreeNode($"{majors.GetValueOrDefault(byMajor.Key, "Tipo " + byMajor.Key)}  ({byMajor.Count()})") { ForeColor = CFg };
            foreach (var bySub in byMajor.GroupBy(i => i.Fields.GetValueOrDefault("id_sub_type")?.Value ?? "0"))
                AddContainer(mNode, subs.GetValueOrDefault(bySub.Key, "Subtipo " + bySub.Key), essence, bySub.ToList());
            node.Nodes.Add(mNode);
        }
        parent.Nodes.Add(node);
        return true;
    }

    private void AddContainer(TreeNode parent, string label, ItemList block, List<Item> items)
    {
        var n = new TreeNode($"{label}  ({items.Count})") { Tag = new Container(block, items), ForeColor = CFg };
        if (items.Count > 0) n.Nodes.Add(new TreeNode()); // dummy p/ lazy load
        parent.Nodes.Add(n);
    }

    private void LazyLoad(TreeNode node)
    {
        if (node.Tag is not Container c) return;
        if (node.Nodes.Count == 1 && node.Nodes[0].Tag is null && node.Nodes[0].Text == "")
        {
            node.Nodes.Clear();
            _tree.BeginUpdate();
            foreach (var it in c.Items)
                node.Nodes.Add(new TreeNode(string.IsNullOrEmpty(it.Name) ? $"#{it.Id}" : it.Name) { Tag = it, ForeColor = GradeColor(it.Name) });
            _tree.EndUpdate();
        }
    }

    private ItemList? FindBlockOf(TreeNode node)
    {
        var p = node.Parent;
        while (p is not null) { if (p.Tag is Container c) return c.Block; p = p.Parent; }
        return null;
    }

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
    };
    private static string Pretty(string block) =>
        BlockPt.TryGetValue(block, out var p) ? p
        : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(block.Replace("_ESSENCE", "").Replace("_", " ").ToLowerInvariant());

    // ---------- busca global ----------
    private void OnSearch()
    {
        string q = _search.Text.Trim();
        if (q.Length == 0) { BuildTree(); return; }
        if (_store.Current is null) return;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var root = new TreeNode("Resultados") { ForeColor = CFg, NodeFont = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        int n = 0;
        foreach (var b in _store.Current.Lists)
        {
            foreach (var it in b.Items)
            {
                if (!it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !it.Id.Contains(q)) continue;
                if (n++ >= MaxLeaves) break;
                root.Nodes.Add(new TreeNode($"{it.Name}   [{it.Id}]  ·  {Pretty(b.Name)}") { Tag = it, ForeColor = GradeColor(it.Name) });
            }
            if (n >= MaxLeaves) break;
        }
        root.Text = $"Resultados ({n}{(n >= MaxLeaves ? "+" : "")})";
        _tree.Nodes.Add(root); root.Expand();
        _tree.EndUpdate();
    }

    // ---------- detalhes ----------
    private void ShowItem(Item item)
    {
        _header.Text = string.IsNullOrEmpty(item.Name) ? $"#{item.Id}" : $"{item.Name}     (ID {item.Id})";
        _header.ForeColor = GradeColor(item.Name);
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
            if (_tree.SelectedNode is not null) { _tree.SelectedNode.Text = val; _tree.SelectedNode.ForeColor = GradeColor(val); }
            _header.Text = $"{val}     (ID {_current.Id})";
        }
        Status($"'{FieldLabels.Label(key)}' atualizado");
    }

    private void AddItem()
    {
        if (_curBlock is null) { Status("Selecione um item/bloco primeiro"); return; }
        _curBlock.Items.Add(new Item { Name = "Novo item", Id = "0" });
        Status($"Item adicionado em {Pretty(_curBlock.Name)} (reabra o nó para ver)");
    }

    private void RemoveItem()
    {
        if (_current is null || _curBlock is null) { Status("Selecione um item"); return; }
        _curBlock.Items.Remove(_current);
        _tree.SelectedNode?.Remove();
        _current = null;
        Status("Item removido");
    }

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
