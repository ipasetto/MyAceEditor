using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela de escolher item, com os filtros do editor de elements: tipo à esquerda, busca por nome ou ID e lista com ícone.
/// Usar sempre que um campo pedir um item, em vez de campo de ID.
/// </summary>
internal sealed class ItemSearchWindow : Form
{
    static readonly Color Bg = Color.FromArgb(21, 24, 29), Side = Color.FromArgb(28, 32, 38), Field = Color.FromArgb(13, 15, 18), Fg = Color.Gainsboro;

    readonly List<(int Id, string Name, string Block)> _all;
    readonly Func<int, string?>? _rename;   // renomear a linha (só o nome que aparece; devolve o nome novo ou null)
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
    bool _missing, _hasIcon;
    Bitmap? _noModel;   // ícone ampliado quando o item não tem modelo 3D
    const int NoModel = int.MinValue;   // nenhuma prévia (0 é uma linha válida: construção, árvore e textura começam no 0)
    int _modelId = NoModel;
    float _yaw;

    public int Picked { get; private set; }
    public int Quantity => (int)_qty.Value;

    readonly IReadOnlyList<int> _favList, _recent;
    readonly HashSet<int> _known;
    readonly JavFavorites? _fav;
    // estrela no canto de cima à direita da prévia (sem prévia: ao lado da busca): contorno = não é favorito, cheia = é
    readonly Label _star = new() { AutoSize = false, Size = new Size(34, 34), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Symbol", 17f), ForeColor = Color.Gold, Cursor = Cursors.Hand, Visible = false };
    readonly ToolTip _starTip = new();
    IReadOnlyList<int> _favorites => _fav is null ? _favList : _fav.Ids.Where(_known.Contains).ToList();
    const int AllRow = 0, FavRow = 1, RecentRow = 2, FirstBlockRow = 3;

    public ItemSearchWindow(IReadOnlyList<(int Id, string Name, string Block)> items, Func<int, Image?>? icon = null, bool askQuantity = true, Func<int, Bitmap?>? tip = null, IReadOnlyList<int>? favorites = null, IReadOnlyList<int>? recent = null, Func<int, float, Image?>? model = null, JavFavorites? fav = null, bool keepBlockOrder = false, Func<int, string?>? rename = null)
    {
        _fav = fav; _rename = rename;
        _all = [.. items]; _icon = icon ?? (_ => null); _hasIcon = icon is not null; _model = model;
        // sem piscar: a lista redesenha num quadro fora da tela (o ListView não liga isso sozinho)
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_list, true);
        if (tip is not null) _tip = new ItemTipHost(this, tip);
        var known = _known = items.Select(i => i.Id).ToHashSet();
        _favList = (favorites ?? []).Where(known.Contains).ToList();
        _recent = (recent ?? []).Where(known.Contains).Distinct().ToList();
        Text = Loc.T("is.title", "Escolher item");
        Width = 900; Height = 640; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f); BackColor = Bg; ForeColor = Fg; KeyPreview = true;

        _blocks.Items.Add(Loc.T("is.all", "Todos os itens"));
        _blocks.Items.Add(string.Format(Loc.T("is.favorites", "Favoritos ({0})"), _favorites.Count));
        _blocks.Items.Add(string.Format(Loc.T("is.recent", "Recentes ({0})"), _recent.Count));
        // grupos em ordem alfabética; keepBlockOrder = na ordem em que aparecem (catálogo: por perto, este mapa, world, ...)
        foreach (var b in keepBlockOrder ? _all.Select(i => i.Block).Distinct() : _all.Select(i => i.Block).Distinct().OrderBy(b => b)) _blocks.Items.Add(b);
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
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count) ShowModelLater(_rows[_list.SelectedIndices[0]].Id); UpdateStar(); };
        _star.Click += (_, _) => ToggleStar();
        FormClosed += (_, _) => { _noModel?.Dispose(); _starTip.Dispose(); _tip?.Dispose(); _later.Dispose(); _modelWait.Dispose(); };
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
        if (_rename is not null)
        {
            var ren = new Button { Text = Loc.T("is.rename", "Renomear (F2)"), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60, 66, 78), Margin = new Padding(12, 0, 0, 0) };
            ren.FlatAppearance.BorderSize = 0;
            ren.Click += (_, _) => RenameSelected();
            foot.Controls.Add(ren);
        }
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
            _star.BackColor = _pic.BackColor; _star.Location = new Point(_pic.Width - _star.Width - 4, 4); _star.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _pic.Controls.Add(_star);
            Controls.Add(right);
            int from = -1;
            _pic.MouseDown += (_, e) => from = e.X;
            _pic.MouseUp += (_, _) => from = -1;
            _pic.MouseMove += (_, e) => { if (from < 0 || Math.Abs(e.X - from) < 6) return; _yaw += (e.X - from) * 1.2f; from = e.X; DrawModel(); };
        }
        Controls.Add(foot);
        if (_model is null)   // sem prévia: a estrela fica no fim da barra de busca
        {
            _star.BackColor = Bg; _star.Dock = DockStyle.Right;
            top.Controls.Add(_star);
        }

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F2 && _rename is not null) { RenameSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down && _search.Focused && _rows.Count > 0) { _list.Focus(); _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(0); e.Handled = true; }
        };
        Shown += (_, _) => _search.Focus();
        Fill();
    }

    int? SelectedId => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]].Id : null;   // árvore tem tipo 0

    /// <summary>Estrela da linha escolhida: ☆ não é favorito, ★ é favorito (só aparece com a lista de favoritos ligada).</summary>
    void UpdateStar()
    {
        _star.Visible = _fav is not null && SelectedId is not null;
        if (_fav is null || SelectedId is not { } id) return;
        bool on = _fav.Has(id);
        _star.Text = on ? "\u2605" : "\u2606";
        _starTip.SetToolTip(_star, on ? Loc.T("is.unstar", "Tirar dos favoritos") : Loc.T("is.star", "Pôr nos favoritos"));
    }

    /// <summary>Teste: escolhe a linha e clica na estrela.</summary>
    internal void SelectRow(int row) { _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(row); UpdateStar(); }
    internal void StarTest(int row) { _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(row); UpdateStar(); ToggleStar(); }
    internal string StarText => _star.Visible ? _star.Text : "";

    void ToggleStar()
    {
        if (_fav is null || SelectedId is not { } id) return;
        if (_fav.Toggle(id) is { } why) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _blocks.Items[FavRow] = string.Format(Loc.T("is.favorites", "Favoritos ({0})"), _favorites.Count);
        if (_blocks.SelectedIndex == FavRow) Fill();   // na lista de favoritos: some ou aparece na hora
        UpdateStar();
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
        if (_model is null || _modelId == NoModel) return;
        Image? img = null;
        try { img = _model(_modelId, _yaw); }   // a imagem é da cache do ModelPreview: não descartar aqui
        catch (Exception ex) { MyAceEditor.PerfectWorld.AppLog.Error("JAV: prévia 3D " + _modelId, ex); }
        string name = _all.FirstOrDefault(r => r.Id == _modelId).Name;
        if (img is null)
        {
            // sem modelo 3D (ou não deu para abrir): mostra o ícone grande e avisa, em vez de deixar a prévia vazia
            _noModel?.Dispose(); _noModel = null;
            if (_icon(_modelId) is { } ic)
            {
                _noModel = new Bitmap(128, 128);
                using var g = Graphics.FromImage(_noModel);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(ic, 0, 0, 128, 128);
            }
            _pic.Image = _noModel;
            _picName.Text = name + Environment.NewLine + Loc.T("is.noModel", "(sem modelo 3D)");
            return;
        }
        _pic.Image = img;
        _picName.Text = name;
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
        if (e.ColumnIndex == 0 && !_hasIcon) return;   // sem ícones (ex.: árvores): nada a pedir de novo (antes redesenhava a lista a cada 0,7 s por 1 minuto)
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
        UpdateStar();
        _count.Text = _rows.Count > 0 || q.Length > 0 || row is not (FavRow or RecentRow)
            ? string.Format(Loc.T("is.count", "{0:N0} itens"), _rows.Count)
            : row == FavRow ? (_fav is not null ? Loc.T("is.noFavStar", "Sem favoritos: escolha uma linha e clique na estrela \u2606") : Loc.T("is.noFav", "Sem favoritos: marque no editor de elements (botão direito > Favoritar)"))
            : Loc.T("is.noRecent", "Os itens que você abrir ou escolher aparecem aqui");
    }

    /// <summary>Renomear a linha escolhida (só o nome que aparece); a lista se refaz e a linha continua escolhida.</summary>
    void RenameSelected()
    {
        if (_rename is null) return;
        if (SelectedId is not { } id) { MessageBox.Show(this, Loc.T("is.renamePick", "Escolha uma linha antes de renomear."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_rename(id) is not { } name) return;
        for (int k = 0; k < _all.Count; k++) if (_all[k].Id == id) _all[k] = (id, name, _all[k].Block);
        Fill();
        int at = _rows.FindIndex(r => r.Id == id);
        if (at >= 0) { _list.SelectedIndices.Add(at); _list.EnsureVisible(at); }
        _list.Focus();
    }

    void Accept()
    {
        int i = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : _rows.Count == 1 ? 0 : -1;
        if (i < 0) return;
        Picked = _rows[i].Id;
        DialogResult = DialogResult.OK;
    }
}
