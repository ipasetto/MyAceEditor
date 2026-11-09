using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;
using System.Globalization;

namespace MyAceEditor.UI;

/// <summary>Aba "Drops extras" do Administrar: edita o extra_drops.sev do servidor (formato em ExtraDropsFile).</summary>
public sealed class ExtraDropsPanel : UserControl
{
    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42, BorderStyle = BorderStyle.None };
    readonly TextBox _name = new() { Width = 320 };
    readonly ComboBox _type = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DataGridView _counts = MakeGrid(), _monsters = MakeGrid(), _items = MakeGrid();
    readonly Label _countSum = new() { AutoSize = true }, _itemSum = new() { AutoSize = true }, _hint = new() { Dock = DockStyle.Top, Height = 26, Padding = new Padding(10, 5, 0, 0) };
    readonly Panel _detail = new() { Dock = DockStyle.Fill };
    readonly Stack<byte[]> _undo = new(), _redo = new();
    Dictionary<uint, string> _monsterNames = [], _itemNames = [];
    ExtraDropsFile? _file;
    string _path = "";
    bool _loading, _dirty;

    public ExtraDropsPanel()
    {
        var bar = UiKit.Ribbon();
        UiKit.Cmd(bar, "", "xd.new", "Nova tabela", NewTable);
        UiKit.Cmd(bar, "", "xd.delete", "Excluir tabela", DeleteTable);
        bar.Items.Add(new ToolStripSeparator());
        UiKit.Cmd(bar, "", "xd.undo", "Desfazer", () => Step(_undo, _redo));
        UiKit.Cmd(bar, "", "xd.redo", "Refazer", () => Step(_redo, _undo));
        bar.Items.Add(new ToolStripSeparator());
        UiKit.Cmd(bar, "", "xd.save", "Salvar", Save, primary: true);

        _type.Items.AddRange([Loc.T("xd.typeAdd", "Soma ao drop normal do monstro"), Loc.T("xd.typeReplace", "Substitui o drop normal do monstro")]);
        _type.SelectedIndexChanged += (_, _) => Commit();
        _name.Leave += (_, _) => Commit();

        _counts.AllowUserToAddRows = _counts.AllowUserToDeleteRows = false;
        _counts.RowHeadersVisible = false; _counts.Height = 54; _counts.Dock = DockStyle.Top;
        for (int i = 0; i < ExtraDropsFile.CountSlots; i++) _counts.Columns.Add("c" + i, string.Format(Loc.T("xd.nItems", "{0} itens"), i));
        _counts.CellValueChanged += (_, _) => Commit();

        _monsters.Columns.Add("id", "ID"); _monsters.Columns.Add("name", Loc.T("xd.monster", "Monstro"));
        _items.Columns.Add("id", "ID"); _items.Columns.Add("name", Loc.T("xd.item", "Item")); _items.Columns.Add("chance", Loc.T("xd.chance", "Chance %"));
        foreach (var g in new[] { _monsters, _items })
        {
            g.Columns["id"]!.FillWeight = 25; g.Columns["name"]!.ReadOnly = true;
            g.CellValueChanged += (_, e) => { if (e.ColumnIndex != 1) Commit(); };
            g.UserDeletedRow += (_, _) => Commit();
        }

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        split.Panel1.Controls.Add(_monsters); split.Panel1.Controls.Add(Title(Loc.T("xd.monsters", "Monstros que usam esta tabela")));
        var balance = new Button { Text = Loc.T("xd.balance", "Ajustar para 100%"), AutoSize = true };
        UiKit.Decorate(balance, balance.Text);
        balance.Click += (_, _) => Balance();
        var itemsHead = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        itemsHead.Controls.AddRange([Title(Loc.T("xd.items", "Itens que podem cair")), _itemSum, balance]);
        foreach (Control c in itemsHead.Controls) c.Dock = DockStyle.None;
        split.Panel2.Controls.Add(_items); split.Panel2.Controls.Add(itemsHead);

        var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = 72, ColumnCount = 2, Padding = new Padding(6) };
        head.Controls.Add(new Label { Text = Loc.T("xd.name", "Nome da tabela"), AutoSize = true }, 0, 0); head.Controls.Add(_name, 0, 1);
        head.Controls.Add(new Label { Text = Loc.T("xd.type", "Como funciona"), AutoSize = true }, 1, 0); head.Controls.Add(_type, 1, 1);
        var countHead = Title(Loc.T("xd.counts", "Quantos itens caem de uma vez"));
        var countRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30 };
        countHead.Dock = DockStyle.None; countRow.Controls.AddRange([countHead, _countSum]);
        _detail.Controls.Add(split); _detail.Controls.Add(_counts); _detail.Controls.Add(countRow); _detail.Controls.Add(head);

        var left = new Panel { Dock = DockStyle.Left, Width = 270 };
        left.Controls.Add(_list);
        _list.DrawItem += DrawTable;
        _list.SelectedIndexChanged += (_, _) => ShowTable();
        Controls.Add(_detail); Controls.Add(left); Controls.Add(_hint); Controls.Add(bar);
    }

    static Label Title(string text) => new() { Text = text, Dock = DockStyle.Top, Height = 28, AutoSize = false, Width = 260, Font = new Font("Segoe UI Semibold", 10f), TextAlign = ContentAlignment.MiddleLeft };

    static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersWidth = 24, AllowUserToResizeRows = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    };

    ExtraDropsFile.Table? Current => _file is { } f && _list.SelectedIndex >= 0 && _list.SelectedIndex < f.Tables.Count ? f.Tables[_list.SelectedIndex] : null;

    public void Open(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) { Message(Loc.T("xd.none", "Este servidor não tem extra_drops.sev. Instale o servidor ou ligue-o e abra de novo.")); return; }
            _file = ExtraDropsFile.Parse(File.ReadAllBytes(path));
            _undo.Clear(); _redo.Clear(); _dirty = false;
            Message(Loc.T("xd.hint", "Só o servidor usa este arquivo. Depois de salvar, reinicie os mapas para valer."));
            Reload(0);
        }
        catch (Exception ex) { AppLog.Error("extra_drops.sev", ex); _file = null; Message(ex.Message); }
    }

    /// <summary>Nomes do cliente para mostrar ao lado dos IDs e conferir ao salvar.</summary>
    public void SetNames(Module? module)
    {
        if (module is null) return;
        foreach (var l in module.Lists)
            foreach (var it in l.Items)
                if (uint.TryParse(it.Id, out var id))
                    (l.Name == "MONSTER_ESSENCE" ? _monsterNames : _itemNames).TryAdd(id, it.Name);
        ShowTable();
    }

    void Message(string text) => _hint.Text = text;

    void Reload(int select)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var t in _file?.Tables ?? []) _list.Items.Add(t);
        _detail.Visible = _list.Items.Count > 0;
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(select, 0, _list.Items.Count - 1);
        _loading = false;
        ShowTable();
    }

    void DrawTable(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not ExtraDropsFile.Table t) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(sel ? Color.FromArgb(0, 103, 192) : Color.White);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var bold = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, t.Name, bold, new Point(e.Bounds.X + 8, e.Bounds.Y + 4), sel ? Color.White : Color.Black);
        TextRenderer.DrawText(e.Graphics, string.Format(Loc.T("xd.row", "{0} monstros · {1} itens"), t.Monsters.Count, t.ItemCount), Font,
            new Point(e.Bounds.X + 8, e.Bounds.Y + 22), sel ? Color.Gainsboro : Color.Gray);
    }

    static string Percent(float v) => (v * 100).ToString("0.####", CultureInfo.CurrentCulture);
    static bool ReadPercent(object? cell, out float v)
    {
        var ok = double.TryParse(cell?.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
        v = (float)(d / 100);
        return ok && d >= 0 && d <= 100;
    }

    void ShowTable()
    {
        if (_loading || Current is not { } t) return;
        _loading = true;
        _name.Text = t.Name;
        _type.SelectedIndex = t.Type == ExtraDropsFile.TypeReplace ? 1 : 0;
        _counts.Rows.Clear();
        _counts.Rows.Add(t.CountChances.Select(Percent).Cast<object>().ToArray());
        for (int i = 0; i < ExtraDropsFile.CountSlots; i++) _counts.Rows[0].Cells[i].Tag = t.CountChances[i];
        _monsters.Rows.Clear();
        foreach (var id in t.Monsters) _monsters.Rows.Add(id.ToString(), Name(_monsterNames, id));
        _items.Rows.Clear();
        for (int i = 0; i < ExtraDropsFile.Slots; i++)
            if (t.Items[i] != 0)
            {
                int r = _items.Rows.Add(t.Items[i].ToString(), Name(_itemNames, t.Items[i]), Percent(t.Chances[i]));
                _items.Rows[r].Cells[2].Tag = t.Chances[i];
            }
        _loading = false;
        ShowSums(t);
    }

    static new string Name(Dictionary<uint, string> names, uint id) => names.TryGetValue(id, out var n) ? n : "";

    void ShowSums(ExtraDropsFile.Table t)
    {
        static void Set(Label l, double sum)
        {
            l.Text = $"{sum * 100:0.##}%";
            l.ForeColor = Math.Abs(sum - 1) > 0.001 ? UiKit.Warn : Color.SeaGreen;
        }
        Set(_countSum, t.CountSum); Set(_itemSum, t.ItemSum);
    }

    void Remember()
    {
        if (_file is null) return;
        _undo.Push(_file.ToBytes()); _redo.Clear(); _dirty = true;
    }

    void Step(Stack<byte[]> from, Stack<byte[]> to)
    {
        if (_file is null || from.Count == 0) return;
        to.Push(_file.ToBytes());
        int at = _list.SelectedIndex;
        _file = ExtraDropsFile.Parse(from.Pop());
        _dirty = true;
        Reload(at);
    }

    // lê a tela para a tabela; valor que o usuário não mexeu fica com o número exato do arquivo
    void Commit()
    {
        if (_loading || Current is not { } t) return;
        var name = _name.Text.Trim();
        int type = _type.SelectedIndex == 1 ? ExtraDropsFile.TypeReplace : ExtraDropsFile.TypeAdd;
        var counts = new float[ExtraDropsFile.CountSlots];
        for (int i = 0; i < counts.Length; i++) counts[i] = Cell(_counts.Rows[0].Cells[i]);
        var monsters = _monsters.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => uint.TryParse(r.Cells[0].Value?.ToString(), out var v) ? v : 0).Where(v => v != 0).ToList();
        var items = _items.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
            .Select(r => (Id: uint.TryParse(r.Cells[0].Value?.ToString(), out var v) ? v : 0, Chance: Cell(r.Cells[2]))).Where(x => x.Id != 0).Take(ExtraDropsFile.Slots).ToList();

        bool same = name == t.Name && type == t.Type && counts.SequenceEqual(t.CountChances) && monsters.SequenceEqual(t.Monsters)
            && items.Select(x => x.Id).SequenceEqual(t.Items.Where(i => i != 0))
            && items.Select(x => x.Chance).SequenceEqual(Enumerable.Range(0, ExtraDropsFile.Slots).Where(i => t.Items[i] != 0).Select(i => t.Chances[i]));
        if (same) return;
        Remember();
        if (name != t.Name) t.Name = name;
        t.Type = type; t.CountChances = counts; t.Monsters = monsters;
        t.Items = new uint[ExtraDropsFile.Slots]; t.Chances = new float[ExtraDropsFile.Slots];
        for (int i = 0; i < items.Count; i++) { t.Items[i] = items[i].Id; t.Chances[i] = items[i].Chance; }
        _list.Invalidate();
        BeginInvoke(ShowTable);
    }

    static float Cell(DataGridViewCell c)
    {
        if (c.Tag is float exact && c.Value?.ToString() == Percent(exact)) return exact;
        return ReadPercent(c.Value, out var v) ? v : 0;
    }

    void NewTable()
    {
        if (_file is null) return;
        Remember();
        var t = new ExtraDropsFile.Table { Name = Loc.T("xd.newName", "Nova tabela") };
        t.CountChances[1] = 1;
        _file.Tables.Add(t);
        Reload(_file.Tables.Count - 1);
    }

    void DeleteTable()
    {
        if (_file is null || Current is not { } t) return;
        Remember();
        int at = _list.SelectedIndex;
        _file.Tables.Remove(t);
        Reload(at);
    }

    void Balance()
    {
        if (Current is not { } t || t.ItemSum <= 0) return;
        Remember();
        double sum = t.ItemSum;
        for (int i = 0; i < ExtraDropsFile.Slots; i++) if (t.Items[i] != 0) t.Chances[i] = (float)(t.Chances[i] / sum);
        ShowTable();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (key == (Keys.Control | Keys.Z)) { Step(_undo, _redo); return true; }
        if (key == (Keys.Control | Keys.Y)) { Step(_redo, _undo); return true; }
        return base.ProcessCmdKey(ref msg, key);
    }

    public bool HasChanges => _dirty;

    public void Save()
    {
        if (_file is null) return;
        Validate();
        Commit();
        var problems = _file.Problems(_monsterNames.Count > 0 ? _monsterNames.ContainsKey : null, _itemNames.Count > 0 ? _itemNames.ContainsKey : null);
        if (problems.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", problems.Take(12)), Loc.T("xd.fix", "Corrija antes de salvar"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            BackupStore.Save(_path);
            AtomicFileSave.Write(_path, _file.ToBytes());
            _dirty = false;
            Message(Loc.T("xd.saved", "Salvo. Reinicie os mapas para valer."));
        }
        catch (Exception ex) { AppLog.Error("extra_drops.sev", ex); MessageBox.Show(this, ex.Message, Loc.T("xd.saveFail", "Não consegui salvar")); }
    }
}
