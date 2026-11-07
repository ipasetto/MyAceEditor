namespace MyAceEditor.UI;

/// <summary>
/// Escolher e ouvir as músicas da zona. Com single, escolhe um som só. As músicas ficam com "Music\" no texto, como no precinct.clt;
/// os sons ficam sem o prefixo, porque o cliente põe "music\" na frente sozinho.
/// </summary>
internal sealed class ZoneMusicWindow : Form
{
    readonly string _element;
    readonly bool _single;
    readonly ListBox _all = new() { Dock = DockStyle.Fill, IntegralHeight = false }, _chosen = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20, IntegralHeight = false };
    readonly TextBox _find = new() { Dock = DockStyle.Top };
    readonly List<string> _files = [];

    public List<string> Music => [.. _chosen.Items.Cast<string>()];

    public ZoneMusicWindow(string element, IEnumerable<string> now, bool single)
    {
        _element = element; _single = single;
        Text = single ? Loc.T("zm.titleSound", "Escolher o som") : Loc.T("zm.title", "Escolher a música");
        ClientSize = new Size(720, 460); MinimumSize = new Size(560, 360); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Font = new Font("Segoe UI", 9.5f);
        var root = Path.Combine(element, "music");
        if (Directory.Exists(root))
            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".ogg" or ".wav").Order())
                _files.Add(single ? Path.GetRelativePath(root, f) : "Music\\" + Path.GetRelativePath(root, f));
        foreach (var m in now.Where(m => m.Length > 0)) _chosen.Items.Add(m);

        _find.PlaceholderText = Loc.T("zm.find", "Procurar…");
        _find.TextChanged += (_, _) => Fill();
        _all.DoubleClick += (_, _) => Add();
        _chosen.DrawItem += DrawChosen;

        Button Btn(string text, Action a) { var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 6) }; UiKit.Decorate(b, text); b.Click += (_, _) => a(); return b; }
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 5, 0) };
        left.Controls.Add(_all); left.Controls.Add(_find);
        var right = new Panel { Dock = DockStyle.Right, Width = 330, Padding = new Padding(5, 10, 10, 0) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        actions.Controls.Add(Btn(Loc.T("zm.add", "Pôr →"), Add));
        actions.Controls.Add(Btn(Loc.T("zm.remove", "Tirar"), Remove));
        if (!single)
        {
            actions.Controls.Add(Btn(Loc.T("zm.up", "Subir"), () => Shift(-1)));
            actions.Controls.Add(Btn(Loc.T("zm.down", "Descer"), () => Shift(1)));
        }
        actions.Controls.Add(Btn(Loc.T("zm.listen", "Ouvir"), Listen));
        actions.Controls.Add(Btn(Loc.T("zm.stop", "Parar"), WorldMusic.StopPreview));
        right.Controls.Add(_chosen); right.Controls.Add(actions);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 12, 10, 8) };
        var cancel = new Button { Text = Loc.T("zm.cancel", "Cancelar"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = Loc.T("zm.ok", "Usar"), AutoSize = true, DialogResult = DialogResult.OK, Margin = new Padding(0, 0, 8, 0) };
        UiKit.Decorate(ok, ok.Text, primary: true);
        bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(left); Controls.Add(right); Controls.Add(bottom);
        FormClosed += (_, _) => WorldMusic.StopPreview();
        Fill();
    }

    string PathOf(string item) => _single ? Path.Combine(_element, "music", item) : Path.Combine(_element, item);

    void Fill()
    {
        var find = _find.Text.Trim();
        _all.BeginUpdate(); _all.Items.Clear();
        foreach (var f in _files.Where(f => find.Length == 0 || f.Contains(find, StringComparison.CurrentCultureIgnoreCase))) _all.Items.Add(f);
        _all.EndUpdate();
    }

    void Add()
    {
        if (_all.SelectedItem is not string s) return;
        if (_single) _chosen.Items.Clear();
        _chosen.Items.Add(s);
    }

    void Remove() { if (_chosen.SelectedIndex >= 0) _chosen.Items.RemoveAt(_chosen.SelectedIndex); }

    void Shift(int by)
    {
        int i = _chosen.SelectedIndex, to = i + by;
        if (i < 0 || to < 0 || to >= _chosen.Items.Count) return;
        var s = _chosen.Items[i];
        _chosen.Items.RemoveAt(i); _chosen.Items.Insert(to, s);
        _chosen.SelectedIndex = to;
    }

    void Listen()
    {
        if ((_all.Focused ? _all.SelectedItem : _chosen.SelectedItem ?? _all.SelectedItem) is string s) WorldMusic.Preview(PathOf(s));
    }

    /// <summary>Linha vermelha: o arquivo não existe no cliente, e a zona fica muda.</summary>
    void DrawChosen(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.DrawBackground();
        var s = (string)_chosen.Items[e.Index];
        var color = File.Exists(PathOf(s)) ? e.ForeColor : Color.FromArgb(220, 38, 38);
        TextRenderer.DrawText(e.Graphics, s, e.Font, e.Bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
