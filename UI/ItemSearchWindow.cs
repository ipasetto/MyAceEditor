using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela de escolher item, com os filtros do editor de elements: tipo à esquerda, busca por nome ou ID e lista com ícone.
/// Usar sempre que um campo pedir um item, em vez de campo de ID.
/// </summary>
internal sealed class ItemSearchWindow : Form
{
    static readonly Color Bg = Color.FromArgb(21, 24, 29), Side = Color.FromArgb(28, 32, 38), Field = Color.FromArgb(13, 15, 18), Fg = Color.Gainsboro;

    readonly IReadOnlyList<(int Id, string Name, string Block)> _all;
    readonly Func<int, Image?> _icon;
    readonly Dictionary<int, Image?> _icons = [];
    readonly ListBox _blocks = new() { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = Side, ForeColor = Fg, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28 };
    readonly TextBox _search = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f), BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, MultiSelect = false, BorderStyle = BorderStyle.None, HideSelection = false, OwnerDraw = true, BackColor = Bg, ForeColor = Fg, SmallImageList = new ImageList { ImageSize = new Size(1, 34) } };
    readonly NumericUpDown _qty = new() { Minimum = 1, Maximum = 99999, Value = 1, Width = 90, BackColor = Field, ForeColor = Color.White };
    readonly Label _count = new() { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 9, 16, 0) };
    List<(int Id, string Name, string Block)> _rows = [];

    public int Picked { get; private set; }
    public int Quantity => (int)_qty.Value;

    public ItemSearchWindow(IReadOnlyList<(int Id, string Name, string Block)> items, Func<int, Image?>? icon = null, bool askQuantity = true)
    {
        _all = items; _icon = icon ?? (_ => null);
        Text = Loc.T("is.title", "Escolher item");
        Width = 900; Height = 640; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f); BackColor = Bg; ForeColor = Fg; KeyPreview = true;

        _blocks.Items.Add(Loc.T("is.all", "Todos os itens"));
        foreach (var b in _all.Select(i => i.Block).Distinct().OrderBy(b => b)) _blocks.Items.Add(b);
        _blocks.SelectedIndex = 0;
        _blocks.DrawItem += DrawBlock;
        _blocks.SelectedIndexChanged += (_, _) => Fill();

        _search.PlaceholderText = Loc.T("is.hint", "Procurar por nome ou ID");
        _search.TextChanged += (_, _) => Fill();
        var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 6), BackColor = Bg };
        top.Controls.Add(_search);

        _list.Columns.Add("", 44); _list.Columns.Add("ID", 80); _list.Columns.Add(Loc.T("is.name", "Nome"), 320); _list.Columns.Add(Loc.T("is.block", "Tipo"), 190);
        _list.RetrieveVirtualItem += (_, e) => { var r = _rows[e.ItemIndex]; e.Item = new ListViewItem(["", r.Id.ToString(), r.Name, r.Block]); };
        _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _list.DrawSubItem += DrawCell;
        _list.DoubleClick += (_, _) => Accept();

        var add = new Button { Text = Loc.T("is.add", "Adicionar"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White, Margin = new Padding(12, 0, 0, 0) };
        add.FlatAppearance.BorderSize = 0;
        add.Click += (_, _) => Accept();
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = Side };
        foot.Controls.Add(add);
        if (askQuantity)
        {
            foot.Controls.Add(_qty);
            foot.Controls.Add(new Label { Text = Loc.T("is.qty", "Quantidade"), AutoSize = true, Margin = new Padding(16, 7, 6, 0) });
        }
        foot.Controls.Add(_count);

        var left = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Side };
        left.Controls.Add(_blocks);
        Controls.Add(_list); Controls.Add(top); Controls.Add(left); Controls.Add(foot);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down && _search.Focused && _rows.Count > 0) { _list.Focus(); _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(0); e.Handled = true; }
        };
        Shown += (_, _) => _search.Focus();
        Fill();
    }

    void DrawBlock(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(sel ? UiKit.Accent : Side);
        e.Graphics.FillRectangle(back, e.Bounds);
        TextRenderer.DrawText(e.Graphics, _blocks.Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
            sel ? Color.White : Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void DrawCell(object? s, DrawListViewSubItemEventArgs e)
    {
        bool sel = e.Item?.Selected == true;
        using var back = new SolidBrush(sel ? UiKit.Accent : Bg);
        e.Graphics.FillRectangle(back, e.Bounds);
        if (e.ColumnIndex == 0 && e.ItemIndex < _rows.Count)
        {
            int id = _rows[e.ItemIndex].Id;
            if (!_icons.TryGetValue(id, out var img)) _icons[id] = img = _icon(id);
            if (img is not null) e.Graphics.DrawImage(img, e.Bounds.X + 6, e.Bounds.Y + 1, 32, 32);
            return;
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            sel ? Color.White : e.ColumnIndex == 2 ? Color.White : Color.Silver, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void Fill()
    {
        var q = _search.Text.Trim();
        bool number = int.TryParse(q, out var id);
        string? block = _blocks.SelectedIndex > 0 ? _blocks.SelectedItem?.ToString() : null;
        _rows = _all.Where(r => (block is null || r.Block == block)
            && (q.Length == 0 || (number && r.Id.ToString().StartsWith(q)) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        _list.SelectedIndices.Clear();
        _list.VirtualListSize = _rows.Count;
        _list.Invalidate();
        _count.Text = string.Format(Loc.T("is.count", "{0:N0} itens"), _rows.Count);
    }

    void Accept()
    {
        int i = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : _rows.Count == 1 ? 0 : -1;
        if (i < 0) return;
        Picked = _rows[i].Id;
        DialogResult = DialogResult.OK;
    }
}
