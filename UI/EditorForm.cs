using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;

namespace MyAceEditor.UI;

/// <summary>
/// Editor desktop nativo (WinForms), no estilo dos editores de Perfect World:
/// seletor de bloco + busca + lista (ID/Nome) à esquerda, abas com grade de campos à direita.
/// </summary>
public sealed class EditorForm : Form
{
    private const int MaxRows = 5000;

    private readonly ModuleStore _store = new();
    private readonly ComboBox _blockCombo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Height = 26 };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Buscar ID ou Nome…" };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, GridLines = false };
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BorderStyle = BorderStyle.None, BackgroundColor = Color.FromArgb(32, 36, 44) };
    private readonly TextBox _desc = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly ToolStripStatusLabel _status = new("Pronto");

    // Paleta (cinza/azul escuro)
    private static readonly Color CBg = Color.FromArgb(24, 27, 33), CPanel = Color.FromArgb(32, 36, 44),
        CPanel2 = Color.FromArgb(40, 45, 55), CLine = Color.FromArgb(55, 61, 73), CFg = Color.FromArgb(232, 235, 240),
        CMuted = Color.FromArgb(150, 158, 172), CAccent = Color.FromArgb(79, 143, 247), CField = Color.FromArgb(44, 62, 94);

    private ItemList? _block;
    private Item? _current;
    private readonly string[] _types = { "String", "Int", "Float", "Bool", "Reference" };

    public EditorForm()
    {
        Text = "MyAceEditor";
        Width = 1240; Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        BackColor = CBg; ForeColor = CFg;

        Controls.Add(BuildBody());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildMenu());
        Controls.Add(BuildStatus());

        Load += (_, _) => TryLoadSample();
    }

    // ---------------- layout ----------------
    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = CPanel, ForeColor = CFg };
        ToolStripMenuItem M(string t) => new(t) { ForeColor = CFg };
        var arq = M("&Arquivo");
        arq.DropDownItems.Add("&Novo módulo", null, (_, _) => NewModule());
        arq.DropDownItems.Add("Abrir &módulo…", null, (_, _) => OpenModule());
        arq.DropDownItems.Add("Abrir &elements.data (Perfect World)…", null, (_, _) => OpenElements());
        arq.DropDownItems.Add(new ToolStripSeparator());
        arq.DropDownItems.Add("&Salvar", null, (_, _) => Save(false));
        arq.DropDownItems.Add("Salvar &como…", null, (_, _) => Save(true));
        arq.DropDownItems.Add(new ToolStripSeparator());
        arq.DropDownItems.Add("Sa&ir", null, (_, _) => Close());
        var ed = M("&Editar");
        ed.DropDownItems.Add("&Novo item", null, (_, _) => AddItem());
        ed.DropDownItems.Add("&Remover item", null, (_, _) => RemoveItem());
        var aj = M("A&juda");
        aj.DropDownItems.Add("Sobre", null, (_, _) => MessageBox.Show(
            "MyAceEditor — editor de dados do Perfect World (elements.data).\nParser orientado a .cfg, duas metades.",
            "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { arq, ed, aj });
        MainMenuStrip = menu;
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var tb = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, BackColor = CPanel2, ForeColor = CFg, Renderer = new DarkRenderer() };
        ToolStripButton B(string t, Action a) { var b = new ToolStripButton(t) { ForeColor = CFg }; b.Click += (_, _) => a(); return b; }
        tb.Items.Add(B("Abrir elements.data", OpenElements));
        tb.Items.Add(B("Abrir módulo", OpenModule));
        tb.Items.Add(B("Salvar", () => Save(false)));
        tb.Items.Add(new ToolStripSeparator());
        tb.Items.Add(B("+ Adicionar", AddItem));
        tb.Items.Add(B("Excluir", RemoveItem));
        return tb;
    }

    private Control BuildBody()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380, BackColor = CLine };

        // ESQUERDA: combo de bloco + busca + lista
        var left = new Panel { Dock = DockStyle.Fill, BackColor = CPanel, Padding = new Padding(6) };
        _blockCombo.FlatStyle = FlatStyle.Flat; _blockCombo.BackColor = CPanel2; _blockCombo.ForeColor = CFg;
        _blockCombo.SelectedIndexChanged += (_, _) => OnBlockChanged();
        _search.BackColor = CPanel2; _search.ForeColor = CFg; _search.BorderStyle = BorderStyle.FixedSingle;
        _search.TextChanged += (_, _) => PopulateList();
        _list.BackColor = CPanel2; _list.ForeColor = CFg; _list.BorderStyle = BorderStyle.None; _list.OwnerDraw = false;
        _list.Columns.Add("ID", 90); _list.Columns.Add("Nome", 270);
        _list.FullRowSelect = true;
        _list.SelectedIndexChanged += (_, _) => OnItemSelected();
        var listHost = new Panel { Dock = DockStyle.Fill }; listHost.Controls.Add(_list);
        left.Controls.Add(listHost);
        left.Controls.Add(_search);
        left.Controls.Add(_blockCombo);
        split.Panel1.Controls.Add(left);

        // DIREITA: header + abas
        var right = new Panel { Dock = DockStyle.Fill, BackColor = CPanel };
        _header.BackColor = CPanel2; _header.ForeColor = CAccent;
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed; _tabs.DrawItem += DrawTab;
        var tabVal = new TabPage("Valores") { BackColor = CPanel };
        StyleGrid();
        tabVal.Controls.Add(_grid);
        var tabDesc = new TabPage("Descrição") { BackColor = CPanel };
        _desc.BackColor = CPanel2; _desc.ForeColor = CFg;
        tabDesc.Controls.Add(_desc);
        _tabs.TabPages.Add(tabVal); _tabs.TabPages.Add(tabDesc);
        right.Controls.Add(_tabs);
        right.Controls.Add(_header);
        split.Panel2.Controls.Add(right);
        return split;
    }

    private StatusStrip BuildStatus()
    {
        var s = new StatusStrip { BackColor = CPanel, ForeColor = CMuted };
        s.Items.Add(_status);
        return s;
    }

    private void StyleGrid()
    {
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = CPanel2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = CFg;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.DefaultCellStyle.BackColor = CPanel;
        _grid.DefaultCellStyle.ForeColor = CFg;
        _grid.DefaultCellStyle.SelectionBackColor = CAccent;
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = CPanel2;
        _grid.GridColor = CLine;
        _grid.ColumnCount = 3;
        _grid.Columns[0].Name = "Campo"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 190;
        _grid.Columns[0].DefaultCellStyle.ForeColor = Color.FromArgb(120, 170, 255);
        _grid.Columns[1].Name = "Tipo"; _grid.Columns[1].ReadOnly = true; _grid.Columns[1].Width = 70;
        _grid.Columns[1].DefaultCellStyle.ForeColor = CMuted;
        _grid.Columns[2].Name = "Valor"; _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
    }

    private void DrawTab(object? s, DrawItemEventArgs e)
    {
        var tab = _tabs.TabPages[e.Index];
        bool sel = e.Index == _tabs.SelectedIndex;
        e.Graphics.FillRectangle(new SolidBrush(sel ? CPanel : CPanel2), e.Bounds);
        TextRenderer.DrawText(e.Graphics, tab.Text, e.Font, e.Bounds, sel ? CAccent : CMuted);
    }

    // ---------------- dados ----------------
    private void TryLoadSample()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "sample.data");
        if (File.Exists(sample)) { _store.Load(sample); PopulateBlocks(); Status("Exemplo carregado"); }
    }

    private void NewModule() { _store.NewModule("Novo"); PopulateBlocks(); Status("Módulo novo"); }

    private void OpenModule()
    {
        using var dlg = new OpenFileDialog { Filter = "Módulo (*.data;*.json)|*.data;*.json|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { _store.Load(dlg.FileName); PopulateBlocks(); Status($"Aberto: {dlg.FileName}"); }
        catch (Exception ex) { Error("Falha ao abrir módulo", ex); }
    }

    private void OpenElements()
    {
        using var dlg = new OpenFileDialog { Filter = "Perfect World elements (*.data)|*.data|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            var reader = new CfgElementsReader(dlg.FileName);
            if (!reader.SignatureValid)
            {
                MessageBox.Show($"Assinatura inválida (0x{reader.Signature:X8}).", "elements.data",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var cfgPath = FindCfg(reader.Version);
            if (cfgPath is null)
            {
                MessageBox.Show($"Não achei um .cfg para a versão 0x{reader.Version:X8} (v{reader.Version & 0xFF}).",
                    "Definição não encontrada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var module = reader.Parse(ElementsCfg.Load(cfgPath));
            module.Name = Path.GetFileName(dlg.FileName);
            _store.Adopt(module);
            PopulateBlocks();
            int items = module.Lists.Sum(l => l.Items.Count);
            double pct = 100.0 * reader.Position / reader.Length;
            Status($"{Path.GetFileName(cfgPath)} · {module.Lists.Count} blocos · {items:N0} itens · {pct:0.0}%");
        }
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
        finally { UseWaitCursor = false; }
    }

    private static string? FindCfg(uint version)
    {
        int v = (int)(version & 0xFF);
        var dir = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg");
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, $"*_v{v}.cfg").FirstOrDefault()
            ?? Directory.GetFiles(dir, $"*v{v}.cfg").FirstOrDefault();
    }

    private void Save(bool saveAs)
    {
        if (_store.Current is null) { Status("Nada para salvar"); return; }
        string? path = _store.CurrentPath;
        if (saveAs || path is null)
        {
            using var dlg = new SaveFileDialog { Filter = "Módulo (*.data)|*.data", FileName = _store.Current.Name };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        try { Status($"Salvo em {_store.Save(path)}"); }
        catch (Exception ex) { Error("Falha ao salvar", ex); }
    }

    // ---------------- árvore de blocos / lista ----------------
    private void PopulateBlocks()
    {
        _blockCombo.Items.Clear();
        if (_store.Current is not null)
            foreach (var l in _store.Current.Lists) _blockCombo.Items.Add($"{l.Name}  ({l.Items.Count})");
        Text = _store.Current is null ? "MyAceEditor" : $"MyAceEditor — {_store.Current.Name}";
        if (_blockCombo.Items.Count > 0) _blockCombo.SelectedIndex = 0; else { _block = null; _list.Items.Clear(); }
    }

    private void OnBlockChanged()
    {
        int i = _blockCombo.SelectedIndex;
        _block = (i >= 0 && _store.Current is not null && i < _store.Current.Lists.Count) ? _store.Current.Lists[i] : null;
        PopulateList();
    }

    private void PopulateList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        if (_block is not null)
        {
            string q = _search.Text.Trim();
            int n = 0;
            foreach (var it in _block.Items)
            {
                if (q.Length > 0 && !it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !it.Id.Contains(q))
                    continue;
                if (n++ >= MaxRows) break;
                var lvi = new ListViewItem(it.Id) { Tag = it };
                lvi.SubItems.Add(it.Name);
                _list.Items.Add(lvi);
            }
        }
        _list.EndUpdate();
    }

    private void OnItemSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is Item it) ShowItem(it);
    }

    private void ShowItem(Item item)
    {
        _current = item;
        _header.Text = $"{(string.IsNullOrEmpty(item.Name) ? "#" + item.Id : item.Name)}      [id {item.Id} · {item.Fields.Count} campos]";
        _grid.Rows.Clear();
        foreach (var (k, f) in item.Fields)
            _grid.Rows.Add(k, f.Type.ToString(), f.Value);
        // aba Descrição: concatena campos de texto longos
        _desc.Text = string.Join(Environment.NewLine,
            item.Fields.Where(f => f.Value.Type == FieldType.String && f.Value.Value.Length > 0)
                       .Select(f => $"{f.Key}: {f.Value.Value}"));
    }

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0) return;
        var key = _grid.Rows[row].Cells[0].Value?.ToString();
        if (string.IsNullOrEmpty(key) || !_current.Fields.ContainsKey(key)) return;
        var val = _grid.Rows[row].Cells[2].Value?.ToString() ?? "";
        var type = _current.Fields[key].Type;
        _current.Fields[key] = Field.Of(type, val);
        // reflete nome/lista se editou o Name
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
        var it = new Item { Name = "Novo item", Id = "0" };
        _block.Items.Add(it);
        PopulateList();
        Status($"Item adicionado em {_block.Name}");
    }

    private void RemoveItem()
    {
        if (_current is null || _block is null) { Status("Selecione um item"); return; }
        _block.Items.Remove(_current);
        _current = null;
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

/// <summary>Renderer escuro para a ToolStrip.</summary>
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
