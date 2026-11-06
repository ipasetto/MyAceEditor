using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Destinos do NPC teleportador na ferramenta Criar: pontos do jogo à esquerda, escolhidos com taxa e nível à direita, e o ponto que ele ativa.</summary>
internal sealed class TeleportStopsWindow : Form
{
    static readonly Color Back = Color.FromArgb(30, 34, 42), Box = Color.FromArgb(44, 50, 60);
    readonly IReadOnlyList<WorldTarget> _points;
    readonly List<WorldTarget> _shown = [];
    readonly ListBox _all = new() { Dock = DockStyle.Fill, BackColor = Box, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    readonly TextBox _find = new() { Dock = DockStyle.Top, BackColor = Box, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, BackgroundColor = Box, BorderStyle = BorderStyle.None,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = { BackColor = Back, ForeColor = Color.Gainsboro }, DefaultCellStyle = { BackColor = Box, ForeColor = Color.White, SelectionBackColor = UiKit.Accent },
    };
    readonly ComboBox _discover = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Box, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 8, 0, 0) };
    readonly List<List<TeleportStop>> _undo = [], _redo = [];

    public List<TeleportStop> Stops { get; private set; }
    public int Discover { get; private set; }

    public TeleportStopsWindow(IReadOnlyList<WorldTarget> points, IEnumerable<TeleportStop> stops, int discover)
    {
        _points = points;
        Stops = [.. stops];
        Discover = discover;
        Text = Loc.T("tp.stopsTitle", "Destinos do teleportador");
        ClientSize = new Size(760, 540); MinimumSize = new Size(640, 440);
        StartPosition = FormStartPosition.CenterParent; BackColor = Back; ForeColor = Color.White; KeyPreview = true; ShowInTaskbar = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(14, 14, 14, 22) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_all); left.Controls.Add(_find);
        left.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = Loc.T("tp.allPoints", "Pontos do jogo (busca)"), ForeColor = Color.Gainsboro });
        _find.TextChanged += (_, _) => Fill();
        _all.DoubleClick += (_, _) => AddSelected();
        layout.Controls.Add(left, 0, 0);

        var mid = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6, 60, 6, 0) };
        mid.Controls.Add(Btn(Loc.T("tp.addStop", "Adicionar"), AddSelected));
        mid.Controls.Add(Btn(Loc.T("tp.removeStop", "Tirar"), RemoveSelected));
        layout.Controls.Add(mid, 1, 0);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_grid);
        right.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = string.Format(Loc.T("tp.chosen", "Para onde ele leva (até {0})"), NpcBuilder.MaxStops), ForeColor = Color.Gainsboro });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("tp.stop", "Destino"), ReadOnly = true, FillWeight = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("tp.fee", "Taxa"), FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("tp.level", "Nível mínimo"), FillWeight = 34 });
        _grid.CellValidating += ValidateCell;
        _grid.CellEndEdit += (_, _) => ReadGrid();
        layout.Controls.Add(right, 2, 0);

        var find = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        find.Controls.Add(new Label { Text = Loc.T("tp.discovers", "Ao falar com ele, o jogador ativa:"), AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 8, 0) });
        find.Controls.Add(_discover);
        layout.Controls.Add(find, 0, 1); layout.SetColumnSpan(find, 3);
        _discover.Items.Add(Loc.T("tp.noDiscover", "(nenhum ponto)"));
        foreach (var p in _points) _discover.Items.Add($"{p.Name}  ·  {p.Id}");
        _discover.SelectedIndex = Math.Max(0, _points.ToList().FindIndex(p => p.Id == discover) + 1);

        var foot = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        foot.Controls.Add(Btn(Loc.T("common.cancel", "Cancelar"), () => { DialogResult = DialogResult.Cancel; Close(); }));
        foot.Controls.Add(Btn(Loc.T("tp.stopsOk", "Concluir"), Finish));
        foot.Controls.Add(_error);
        layout.Controls.Add(foot, 0, 2); layout.SetColumnSpan(foot, 3);
        Controls.Add(layout);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Step(_undo, _redo); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Y) { Step(_redo, _undo); e.Handled = true; }
        };
        Fill(); ShowStops();
    }

    Button Btn(string text, Action a)
    {
        var b = new Button { Text = text, Width = 100, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Box, ForeColor = Color.White, Margin = new Padding(4) };
        b.Click += (_, _) => a();
        return b;
    }

    void Fill()
    {
        var q = _find.Text.Trim();
        _shown.Clear();
        _shown.AddRange(_points.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Id.ToString() == q));
        _all.BeginUpdate();
        _all.Items.Clear();
        foreach (var p in _shown) _all.Items.Add(string.Format(Loc.T("tp.pointRow", "{0}  ·  {1}  ·  mapa {2}"), p.Name, p.Id, p.Tag));
        _all.EndUpdate();
    }

    string NameOf(int id) => _points.FirstOrDefault(p => p.Id == id)?.Name ?? id.ToString();

    void ShowStops()
    {
        _grid.Rows.Clear();
        foreach (var s in Stops) _grid.Rows.Add(NameOf(s.Point), s.Fee, s.Level);
    }

    void Remember() { _undo.Add([.. Stops]); _redo.Clear(); }

    void Step(List<List<TeleportStop>> from, List<List<TeleportStop>> to)
    {
        if (from.Count == 0) return;
        to.Add([.. Stops]);
        Stops = from[^1]; from.RemoveAt(from.Count - 1);
        ShowStops();
    }

    void AddSelected()
    {
        if (_all.SelectedIndex < 0 || _all.SelectedIndex >= _shown.Count) return;
        var p = _shown[_all.SelectedIndex];
        if (Stops.Exists(s => s.Point == p.Id)) return;
        if (Stops.Count >= NpcBuilder.MaxStops) { _error.Text = string.Format(T("nb.manyStops", "Mais de {0} destinos."), NpcBuilder.MaxStops); return; }
        Remember();
        Stops.Add(new TeleportStop(p.Id, 0, 0));
        _error.Text = "";
        ShowStops();
    }

    void RemoveSelected()
    {
        if (_grid.CurrentRow is not { Index: >= 0 } r || r.Index >= Stops.Count) return;
        Remember();
        Stops.RemoveAt(r.Index);
        ShowStops();
    }

    static string T(string key, string pt) => Loc.T(key, pt);

    void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex == 0) return;
        int max = e.ColumnIndex == 1 ? 100_000_000 : 150;
        e.Cancel = !int.TryParse(e.FormattedValue?.ToString(), out var n) || n < 0 || n > max;
    }

    void ReadGrid()
    {
        var read = new List<TeleportStop>();
        for (int i = 0; i < Stops.Count && i < _grid.Rows.Count; i++)
            read.Add(Stops[i] with { Fee = Number(_grid.Rows[i].Cells[1].Value), Level = Number(_grid.Rows[i].Cells[2].Value) });
        if (read.SequenceEqual(Stops)) return;
        Remember();
        Stops = read;
    }

    static int Number(object? v) => int.TryParse(v?.ToString(), out var n) ? n : 0;

    void Finish()
    {
        ReadGrid();
        if (Stops.Count == 0) { _error.Text = T("nb.noStops", "Escolha pelo menos um destino: sem destino o gs cai ao iniciar."); return; }
        Discover = _discover.SelectedIndex > 0 ? _points[_discover.SelectedIndex - 1].Id : 0;
        DialogResult = DialogResult.OK;
        Close();
    }
}
