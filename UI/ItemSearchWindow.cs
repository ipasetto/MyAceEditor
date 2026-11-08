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
    readonly ItemTipHost? _tip;
    readonly Func<int, float, Image?>? _model;
    readonly PictureBox _pic = new() { Dock = DockStyle.Top, Height = 250, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.FromArgb(14, 16, 20), Cursor = Cursors.SizeWE };
    readonly Label _picName = new() { Dock = DockStyle.Top, Height = 44, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.Silver, Padding = new Padding(6, 6, 6, 0) };
    readonly System.Windows.Forms.Timer _later = new() { Interval = 700 };
    readonly System.Windows.Forms.Timer _modelWait = new() { Interval = 180 };
    readonly Dictionary<int, int> _tries = [];
    bool _missing;
    int _modelId;
    float _yaw;

    public int Picked { get; private set; }
    public int Quantity => (int)_qty.Value;

    readonly IReadOnlyList<int> _favorites, _recent;
    const int AllRow = 0, FavRow = 1, RecentRow = 2, FirstBlockRow = 3;

    public ItemSearchWindow(IReadOnlyList<(int Id, string Name, string Block)> items, Func<int, Image?>? icon = null, bool askQuantity = true, Func<int, Bitmap?>? tip = null, IReadOnlyList<int>? favorites = null, IReadOnlyList<int>? recent = null, Func<int, float, Image?>? model = null)
    {
        _all = items; _icon = icon ?? (_ => null); _model = model;
        if (tip is not null) _tip = new ItemTipHost(this, tip);
        var known = items.Select(i => i.Id).ToHashSet();
        _favorites = (favorites ?? []).Where(known.Contains).ToList();
        _recent = (recent ?? []).Where(known.Contains).Distinct().ToList();
        Text = Loc.T("is.title", "Escolher item");
        Width = 900; Height = 640; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f); BackColor = Bg; ForeColor = Fg; KeyPreview = true;

        _blocks.Items.Add(Loc.T("is.all", "Todos os itens"));
        _blocks.Items.Add(string.Format(Loc.T("is.favorites", "Favoritos ({0})"), _favorites.Count));
        _blocks.Items.Add(string.Format(Loc.T("is.recent", "Recentes ({0})"), _recent.Count));
        foreach (var b in _all.Select(i => i.Block).Distinct().OrderBy(b => b)) _blocks.Items.Add(b);
        _blocks.SelectedIndex = 0;
        _blocks.DrawItem += DrawBlock;
        _blocks.SelectedIndexChanged += (_, _) => Fill();

        _search.PlaceholderText = Loc.T("is.hint", "Procurar por nome ou ID");
        _search.TextChanged += (_, _) => Fill();
        var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 6), BackColor = Bg };
        top.Controls.Add(_search);

        _list.Columns.Add("", 44); _list.Columns.Add("ID", 80); _list.Columns.Add(Loc.T("is.name", "Nome"), 320); _list.Columns.Add(Loc.T("is.block", "Tipo"), 190);
        _list.RetrieveVirtualItem += (_, e) =>
        {
            if (e.ItemIndex >= _rows.Count) { e.Item = new ListViewItem(["", "", "", ""]); return; }
            var r = _rows[e.ItemIndex];
            e.Item = new ListViewItem(["", r.Id.ToString(), r.Name, r.Block]);
        };
        _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _list.DrawSubItem += DrawCell;
        _list.DoubleClick += (_, _) => Accept();
        _list.MouseMove += (_, e) =>
        {
            int i = _list.GetItemAt(e.X, e.Y)?.Index ?? -1;
            int id = i >= 0 && i < _rows.Count ? _rows[i].Id : 0;
            _tip?.Hover(id);
        };
        _list.MouseLeave += (_, _) => _tip?.Hide();
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count) ShowModelLater(_rows[_list.SelectedIndices[0]].Id); };
        FormClosed += (_, _) => { _tip?.Dispose(); _later.Dispose(); _modelWait.Dispose(); };
        // miniatura de monstro e NPC é feita em segundo plano: pede de novo as que ainda não chegaram
        _later.Tick += (_, _) => { if (!_missing) return; _missing = false; _list.Invalidate(); };
        _later.Start();
        _modelWait.Tick += (_, _) => { _modelWait.Stop(); DrawModel(); };

        var add = new Button { Text = Loc.T("is.add", "Adicionar"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White, Margin = new Padding(12, 0, 0, 0) };
        add.FlatAppearance.BorderSize = 0;
        add.Click += (_, _) => Accept();
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = Side };
        foot.Controls.Add(add);
        var close = new Button { Text = Loc.T("is.close", "Fechar"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60, 66, 78), Margin = new Padding(12, 0, 0, 0), DialogResult = DialogResult.Cancel };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        foot.Controls.Add(close);
        if (askQuantity)
        {
            foot.Controls.Add(_qty);
            foot.Controls.Add(new Label { Text = Loc.T("is.qty", "Quantidade"), AutoSize = true, Margin = new Padding(16, 7, 6, 0) });
        }
        foot.Controls.Add(_count);

        var left = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Side };
        left.Controls.Add(_blocks);
        Controls.Add(_list); Controls.Add(top); Controls.Add(left);
        if (_model is not null)
        {
            Width += 250;
            var right = new Panel { Dock = DockStyle.Right, Width = 250, BackColor = Side };
            right.Controls.Add(_picName); right.Controls.Add(_pic);
            Controls.Add(right);
            int from = -1;
            _pic.MouseDown += (_, e) => from = e.X;
            _pic.MouseUp += (_, _) => from = -1;
            _pic.MouseMove += (_, e) => { if (from < 0 || Math.Abs(e.X - from) < 6) return; _yaw += (e.X - from) * 1.2f; from = e.X; DrawModel(); };
        }
        Controls.Add(foot);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down && _search.Focused && _rows.Count > 0) { _list.Focus(); _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(0); e.Handled = true; }
        };
        Shown += (_, _) => _search.Focus();
        Fill();
    }

    /// <summary>Diagnóstico: desenha o modelo sem esperar o mouse.</summary>
    internal void ShotModel(int id) { _modelId = id; DrawModel(); }

    void ShowModelLater(int id)
    {
        if (_model is null || id == _modelId) return;
        _modelId = id; _yaw = 0;
        _modelWait.Stop(); _modelWait.Start();
    }

    void DrawModel()
    {
        if (_model is null || _modelId <= 0) return;
        _pic.Image = _model(_modelId, _yaw);   // a imagem é da cache do ModelPreview: não descartar aqui
        _picName.Text = _all.FirstOrDefault(r => r.Id == _modelId).Name;
    }

    void DrawBlock(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(sel ? UiKit.Accent : Side);
        e.Graphics.FillRectangle(back, e.Bounds);
        // atalhos em cima, com ícone, e uma linha separando os blocos
        if (e.Index < FirstBlockRow)
        {
            using var glyph = new Font("Segoe MDL2 Assets", 10f);
            TextRenderer.DrawText(e.Graphics, e.Index == AllRow ? "\uE8FD" : e.Index == FavRow ? "\uE734" : "\uE81C", glyph, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, 22, e.Bounds.Height),
                sel ? Color.White : e.Index == FavRow ? Color.Gold : Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, _blocks.Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 34, e.Bounds.Y, e.Bounds.Width - 40, e.Bounds.Height),
                sel ? Color.White : Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (e.Index == RecentRow) { using var line = new Pen(Color.FromArgb(70, 76, 86)); e.Graphics.DrawLine(line, e.Bounds.X + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1); }
            return;
        }
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
            if (!_icons.TryGetValue(id, out var img))
            {
                img = _icon(id);
                _tries[id] = _tries.GetValueOrDefault(id) + 1;
                if (img is not null || _tries[id] >= 90) _icons[id] = img; else _missing = true;   // cerca de 1 minuto: sem modelo, fica sem ícone
            }
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
        int row = _blocks.SelectedIndex;
        string? block = row >= FirstBlockRow ? _blocks.SelectedItem?.ToString() : null;
        _list.SelectedIndices.Clear();
        _list.VirtualListSize = 0;   // a lista não pode pedir uma linha que não existe mais
        bool Match((int Id, string Name, string Block) r) => q.Length == 0 || (number && r.Id.ToString().StartsWith(q)) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
        if (row is FavRow or RecentRow)
        {
            var byId = new Dictionary<int, (int Id, string Name, string Block)>();
            foreach (var r in _all) byId.TryAdd(r.Id, r);
            _rows = (row == FavRow ? _favorites : _recent).Select(i => byId[i]).Where(Match).ToList();   // recentes: o último primeiro
        }
        else _rows = _all.Where(r => (block is null || r.Block == block) && Match(r)).ToList();
        _list.VirtualListSize = _rows.Count;
        _list.Invalidate();
        _count.Text = _rows.Count > 0 || q.Length > 0 || row is not (FavRow or RecentRow)
            ? string.Format(Loc.T("is.count", "{0:N0} itens"), _rows.Count)
            : row == FavRow ? Loc.T("is.noFav", "Sem favoritos: marque no editor de elements (botão direito > Favoritar)")
            : Loc.T("is.noRecent", "Os itens que você abrir ou escolher aparecem aqui");
    }

    void Accept()
    {
        int i = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : _rows.Count == 1 ? 0 : -1;
        if (i < 0) return;
        Picked = _rows[i].Id;
        DialogResult = DialogResult.OK;
    }
}
