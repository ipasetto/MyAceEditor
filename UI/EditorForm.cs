using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;

namespace MyAceEditor.UI;

/// <summary>Editor desktop nativo (WinForms): árvore de listas/itens + grade de campos.</summary>
public sealed class EditorForm : Form
{
    private const int MaxNodesPerList = 2000;

    private readonly ModuleStore _store = new();
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false };
    private readonly Label _header = new() { Dock = DockStyle.Top, Height = 48, Font = new Font("Segoe UI", 12, FontStyle.Bold), Padding = new Padding(10, 10, 10, 0) };
    private readonly ToolStripStatusLabel _status = new("Pronto");
    private readonly string[] _types = { "String", "Int", "Float", "Bool", "Reference" };

    private Item? _current;

    public EditorForm()
    {
        Text = "MyAceEditor";
        Width = 1200; Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);

        // ---- Menu ----
        var menu = new MenuStrip();
        var mArq = new ToolStripMenuItem("&Arquivo");
        mArq.DropDownItems.Add("&Novo módulo", null, (_, _) => NewModule());
        mArq.DropDownItems.Add("Abrir &módulo…", null, (_, _) => OpenModule());
        mArq.DropDownItems.Add("Abrir &elements.data (Perfect World)…", null, (_, _) => OpenElements());
        mArq.DropDownItems.Add(new ToolStripSeparator());
        mArq.DropDownItems.Add("&Salvar", null, (_, _) => Save(false));
        mArq.DropDownItems.Add("Salvar &como…", null, (_, _) => Save(true));
        mArq.DropDownItems.Add(new ToolStripSeparator());
        mArq.DropDownItems.Add("Sa&ir", null, (_, _) => Close());

        var mEdit = new ToolStripMenuItem("&Editar");
        mEdit.DropDownItems.Add("&Novo item", null, (_, _) => AddItem());
        mEdit.DropDownItems.Add("&Remover item", null, (_, _) => RemoveItem());

        var mView = new ToolStripMenuItem("E&xibir");
        mView.DropDownItems.Add("Expandir tudo", null, (_, _) => _tree.ExpandAll());
        mView.DropDownItems.Add("Recolher tudo", null, (_, _) => _tree.CollapseAll());

        var mHelp = new ToolStripMenuItem("A&juda");
        mHelp.DropDownItems.Add("Sobre", null, (_, _) =>
            MessageBox.Show("MyAceEditor — editor de dados de jogo.\nParser de elements.data (Perfect World) por engenharia reversa.",
                "Sobre", MessageBoxButtons.OK, MessageBoxIcon.Information));

        menu.Items.AddRange(new ToolStripItem[] { mArq, mEdit, mView, mHelp });

        // ---- Corpo: árvore | detalhes ----
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 320 };
        _tree.AfterSelect += (_, e) => OnSelect(e.Node);
        split.Panel1.Controls.Add(_tree);

        _grid.ColumnCount = 3;
        _grid.Columns[0].Name = "Campo"; _grid.Columns[0].ReadOnly = true; _grid.Columns[0].Width = 150;
        var tcol = new DataGridViewComboBoxColumn { Name = "Tipo", Width = 90 };
        tcol.Items.AddRange(_types);
        _grid.Columns.RemoveAt(1); _grid.Columns.Insert(1, tcol);
        _grid.Columns[2].Name = "Valor"; _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.CellEndEdit += (_, e) => OnFieldEdited(e.RowIndex);
        split.Panel2.Controls.Add(_grid);
        split.Panel2.Controls.Add(_header);

        // ---- Status ----
        var strip = new StatusStrip();
        strip.Items.Add(_status);

        Controls.Add(split);
        Controls.Add(strip);
        Controls.Add(menu);
        MainMenuStrip = menu;

        Load += (_, _) => TryLoadSample();
    }

    private void TryLoadSample()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "sample.data");
        if (File.Exists(sample)) { _store.Load(sample); PopulateTree(); Status($"Exemplo carregado: {sample}"); }
    }

    // ---------- carregar ----------
    private void NewModule() { _store.NewModule("Novo"); PopulateTree(); Status("Módulo novo criado"); }

    private void OpenModule()
    {
        using var dlg = new OpenFileDialog { Filter = "Módulo (*.data;*.json)|*.data;*.json|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { _store.Load(dlg.FileName); PopulateTree(); Status($"Aberto: {dlg.FileName}"); }
        catch (Exception ex) { Error("Falha ao abrir módulo", ex); }
    }

    private void OpenElements()
    {
        using var dlg = new OpenFileDialog { Filter = "Perfect World elements (*.data)|*.data|Todos (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var reader = new ElementsReader(dlg.FileName);
            if (!reader.SignatureValid)
            {
                MessageBox.Show($"Assinatura inválida (0x{reader.Signature:X8}). Não parece um elements.data suportado.",
                    "elements.data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UseWaitCursor = true;
            var module = reader.ReadModule(ElementsProgram.Steps);
            module.Name = Path.GetFileName(dlg.FileName);
            _store.Adopt(module);
            PopulateTree();
            int items = module.Lists.Sum(l => l.Items.Count);
            Status($"elements.data: {module.Lists.Count} listas · {items:N0} itens (1ª metade mapeada)");
        }
        catch (Exception ex) { Error("Falha ao ler elements.data", ex); }
        finally { UseWaitCursor = false; }
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

    // ---------- árvore ----------
    private void PopulateTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (_store.Current is not null)
        {
            foreach (var list in _store.Current.Lists)
            {
                var g = new TreeNode($"{list.Name}  ({list.Items.Count})");
                int n = 0;
                foreach (var it in list.Items)
                {
                    if (n++ >= MaxNodesPerList) { g.Nodes.Add($"… +{list.Items.Count - MaxNodesPerList} itens"); break; }
                    g.Nodes.Add(new TreeNode(string.IsNullOrEmpty(it.Name) ? $"#{it.Id}" : it.Name) { Tag = (list, it) });
                }
                _tree.Nodes.Add(g);
            }
        }
        _tree.EndUpdate();
        Text = _store.Current is null ? "MyAceEditor" : $"MyAceEditor — {_store.Current.Name}";
    }

    private void OnSelect(TreeNode? node)
    {
        if (node?.Tag is ValueTuple<ItemList, Item> t) ShowItem(t.Item1, t.Item2);
        else { _current = null; _header.Text = ""; _grid.Rows.Clear(); }
    }

    private void ShowItem(ItemList list, Item item)
    {
        _current = item;
        _header.Text = $"{item.Name}      [{list.Name} · id {item.Id} · {item.Fields.Count} campos]";
        _grid.Rows.Clear();
        if (item.Fields.Count == 0)
        {
            int r = _grid.Rows.Add("id", "Int", item.Id); _grid.Rows[r].ReadOnly = true;
            r = _grid.Rows.Add("nome", "String", item.Name); _grid.Rows[r].ReadOnly = true;
            return;
        }
        foreach (var (k, f) in item.Fields)
            _grid.Rows.Add(k, f.Type.ToString(), f.Value);
    }

    private void OnFieldEdited(int row)
    {
        if (_current is null || row < 0) return;
        var key = _grid.Rows[row].Cells[0].Value?.ToString();
        if (string.IsNullOrEmpty(key) || !_current.Fields.ContainsKey(key)) return;
        var type = _grid.Rows[row].Cells[1].Value?.ToString() ?? "String";
        var val = _grid.Rows[row].Cells[2].Value?.ToString() ?? "";
        if (Enum.TryParse<FieldType>(type, out var ft))
            _current.Fields[key] = Field.Of(ft, val);
        Status($"Campo '{key}' atualizado");
    }

    // ---------- editar itens ----------
    private void AddItem()
    {
        if (_tree.SelectedNode is null || _store.Current is null) { Status("Selecione uma lista"); return; }
        var node = _tree.SelectedNode;
        var listNode = (node.Tag is ValueTuple<ItemList, Item> ? node.Parent : node) ?? node;
        var list = _store.Current.Lists.FirstOrDefault(l => listNode.Text.StartsWith(l.Name));
        if (list is null) { Status("Selecione uma lista válida"); return; }
        var item = new Item { Name = "Novo item" };
        list.Items.Add(item);
        PopulateTree();
        Status($"Item adicionado em {list.Name}");
    }

    private void RemoveItem()
    {
        if (_current is null || _store.Current is null) { Status("Selecione um item"); return; }
        foreach (var l in _store.Current.Lists) l.Items.Remove(_current);
        _current = null;
        PopulateTree();
        Status("Item removido");
    }

    private void Status(string s) => _status.Text = s;
    private void Error(string msg, Exception ex)
    {
        Status(msg + ": " + ex.Message);
        MessageBox.Show(ex.Message, msg, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
