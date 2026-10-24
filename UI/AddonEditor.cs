using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor de addons e chances do equipamento (como o "Editor de addons" do RAE, código nosso): aba "Chances" com
/// soquetes / quantidade de addons / chance de único, e uma aba por lista de addons (fabricação, aleatórios, únicos)
/// com barras arrastáveis, cadeado, adicionar/remover/ordenar/ajustar para 100%. Trabalha numa cópia do item;
/// <see cref="Changes"/> traz só o que mudou (a tela aplica pelo histórico).
/// </summary>
internal sealed class AddonEditor : Form
{
    static readonly Color Bg = Color.FromArgb(242, 245, 249), Fg = Color.FromArgb(30, 39, 51), Accent = Color.FromArgb(37, 99, 235);

    readonly Item _orig, _work;
    readonly HashSet<string> _locks = new();
    readonly Func<string, string> _label;
    readonly Func<string, string> _addonText;
    readonly Func<string, List<string>?> _pick;
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(100, 112, 128) };

    public Dictionary<string, string> Changes { get; } = new();

    public static string SectionTitle(string g) => g switch
    {
        _ when g.StartsWith("make_probability_socket") => Loc.T("addonEd.makeSocket", "Soquetes ao fabricar"),
        _ when g.StartsWith("drop_probability_socket") => Loc.T("addonEd.dropSocket", "Soquetes ao cair (drop)"),
        _ when g.StartsWith("probability_addon_num") => Loc.T("addonEd.addonNum", "Quantidade de addons"),
        "probability_unique" => Loc.T("addonEd.unique", "Chance de addon único"),
        _ when g.StartsWith("addons_") => Loc.T("addonEd.addons", "Addons (fabricação)"),
        _ when g.StartsWith("rands_") => Loc.T("addonEd.rands", "Addons aleatórios"),
        _ when g.StartsWith("uniques_") => Loc.T("addonEd.uniques", "Addons únicos"),
        _ => g,
    };

    /// <param name="label">nome do campo para mostrar</param>
    /// <param name="addonText">descrição do addon pelo ID ("" se não existe)</param>
    /// <param name="pick">abre o seletor de addons (vários) e devolve os IDs escolhidos</param>
    public AddonEditor(Item item, Func<string, string> label, Func<string, string> addonText, Func<string, List<string>?> pick)
    {
        _orig = item; _work = AddonGroups.Clone(item); _label = label; _addonText = addonText; _pick = pick;
        Text = string.Format(Loc.T("addonEd.title", "Editor de addons — {0} {1}"), item.Id, TasksFile.SplitColor(item.Name).Text);
        Width = 980; Height = 680; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);

        var sections = AddonGroups.Find(_work);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var chances = sections.Where(s => !s.HasRefs).ToList();
        if (chances.Count > 0) tabs.TabPages.Add(Page(Loc.T("addonEd.chances", "Chances"), chances, null));
        foreach (var s in sections.Where(s => s.HasRefs))
            tabs.TabPages.Add(Page($"{SectionTitle(s.Name)} ({AddonGroups.Filled(_work, s).Count()})", new() { s }, s));

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 46, ColumnCount = 3, Padding = new Padding(8, 6, 8, 6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var ok = new Button { Text = Loc.T("dlg.save", "Salvar"), Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            foreach (var (k, f) in _work.Fields)
                if (_orig.Fields.TryGetValue(k, out var o) && o.Value != f.Value) Changes[k] = f.Value;
            DialogResult = DialogResult.OK;
        };
        bottom.Controls.Add(_status, 0, 0); bottom.Controls.Add(ok, 1, 0); bottom.Controls.Add(cancel, 2, 0);
        _status.Text = Loc.T("addonEd.hint", "Arraste as barras; o cadeado fixa o slot. Duplo clique no nome troca o addon.");
        CancelButton = cancel;
        Controls.Add(tabs); Controls.Add(bottom);
    }

    TabPage Page(string title, List<ProbSection> sections, ProbSection? listSection)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        var view = new ProbListView(_work, sections, _locks, _label, _addonText) { Dock = DockStyle.Fill };
        view.Changed += () =>
        {
            if (listSection is not null) page.Text = $"{SectionTitle(listSection.Name)} ({AddonGroups.Filled(_work, listSection).Count()})";
        };
        view.PickFor += row =>
        {
            if (row.Ref is null || _pick(row.Ref) is not { Count: > 0 } ids) return;
            Put(row.Ref, ids[0]);
            view.Rebuild();
        };
        page.Controls.Add(view);
        if (listSection is null) return page;

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 0, 0), BackColor = Bg };
        Button B(string t, Action a) { var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 28 }; b.Click += (_, _) => a(); tools.Controls.Add(b); return b; }
        B(Loc.T("addonEd.add", "＋ Adicionar…"), () =>
        {
            var empty = listSection.Rows.Where(r => r.Ref is not null && AddonGroups.Empty(_work.Fields[r.Ref].Value) && ProbabilityGroups.Num(_work.Fields[r.Prob].Value) == 0).ToList();
            if (empty.Count == 0) { _status.Text = Loc.T("addonEd.full", "Não há slot livre nesta lista."); return; }
            if (_pick(empty[0].Ref!) is not { Count: > 0 } ids) return;
            int n = 0;
            foreach (var (row, id) in empty.Zip(ids)) { Put(row.Ref!, id); n++; }
            _status.Text = string.Format(Loc.T("addonEd.added", "{0} addon(s) adicionado(s) com 0% — arraste a barra ou use \"Ajustar para 100%\"."), n)
                + (ids.Count > n ? " " + string.Format(Loc.T("addonEd.noRoom", "{0} não couberam."), ids.Count - n) : "");
            view.Rebuild();
        });
        B(Loc.T("addonEd.remove", "✕ Remover"), () =>
        {
            if (view.Selected is not { } row) { _status.Text = Loc.T("addonEd.pickRow", "Selecione uma linha."); return; }
            if (row.Ref is not null) Put(row.Ref, "0");
            Put(row.Prob, "0");
            _locks.Remove(row.Prob);
            view.Rebuild();
        });
        B(Loc.T("addonEd.sort", "Ordenar"), () => { AddonGroups.Sort(_work, listSection); _locks.Clear(); view.Rebuild(); });
        B(Loc.T("addonEd.normalize", "Ajustar para 100%"), () => { AddonGroups.Normalize(_work, listSection, _locks); view.Rebuild(); });
        B(Loc.T("addonEd.equal", "Dividir igual"), () =>
        {
            foreach (var r in listSection.Rows.Where(r => !_locks.Contains(r.Prob))) Put(r.Prob, "0");
            AddonGroups.Normalize(_work, listSection, _locks);
            view.Rebuild();
        });
        page.Controls.Add(tools);
        return page;
    }

    void Put(string key, string v) => _work.Fields[key] = Field.Of(_work.Fields[key].Type, v);
}

/// <summary>
/// Lista desenhada de probabilidades (seções com título e Σ, linhas com nome, barra arrastável, % e cadeado).
/// Em seções com ID (listas de addons) mostra só os slots preenchidos.
/// </summary>
internal sealed class ProbListView : ScrollableControl
{
    const int RowH = 26, HeadH = 30;
    static readonly Color Green = Color.FromArgb(34, 160, 90), Red = Color.FromArgb(220, 38, 38), Muted = Color.FromArgb(100, 112, 128),
        Track = Color.FromArgb(226, 231, 238), Sel = Color.FromArgb(219, 234, 254), Accent = Color.FromArgb(37, 99, 235);

    readonly Item _item;
    readonly List<ProbSection> _sections;
    readonly HashSet<string> _locks;
    readonly Func<string, string> _label, _addonText;
    readonly List<(ProbSection Sec, ProbRow? Row, int Y)> _layout = new();
    ProbRow? _drag;
    Dictionary<string, Field>? _before;

    public ProbRow? Selected { get; private set; }
    public event Action? Changed;
    public event Action<ProbRow>? PickFor;

    public ProbListView(Item item, List<ProbSection> sections, HashSet<string> locks, Func<string, string> label, Func<string, string> addonText)
    {
        _item = item; _sections = sections; _locks = locks; _label = label; _addonText = addonText;
        DoubleBuffered = true; AutoScroll = true; BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Rebuild();
    }

    public void Rebuild()
    {
        _layout.Clear();
        int y = 4;
        foreach (var s in _sections)
        {
            _layout.Add((s, null, y)); y += HeadH;
            var rows = s.HasRefs ? AddonGroups.Filled(_item, s).ToList() : s.Rows;
            if (rows.Count == 0) { _layout.Add((s, new ProbRow("", null), y)); y += RowH; }   // linha "vazio"
            foreach (var r in rows) { _layout.Add((s, r, y)); y += RowH; }
            y += 8;
        }
        if (Selected is not null && !_layout.Any(l => l.Row == Selected)) Selected = null;
        AutoScrollMinSize = new Size(0, y);
        Invalidate();
        Changed?.Invoke();
    }

    // colunas: nome | barra | % | cadeado
    (Rectangle Name, Rectangle Bar, Rectangle Pct, Rectangle Lock) Cols(int y, bool refs)
    {
        int w = ClientSize.Width, nameW = refs ? Math.Min(460, w / 2) : 260;
        var name = new Rectangle(12, y, nameW, RowH);
        var lk = new Rectangle(w - 34, y, 24, RowH);
        var pct = new Rectangle(lk.X - 80, y, 76, RowH);
        var bar = new Rectangle(name.Right + 8, y + 6, Math.Max(40, pct.X - name.Right - 16), RowH - 12);
        return (name, bar, pct, lk);
    }

    string RowName(ProbRow r)
    {
        if (r.Prob == "") return Loc.T("addonEd.empty", "(vazio — use Adicionar)");
        if (r.Ref is null) return _label(r.Prob);
        var id = _item.Fields[r.Ref].Value;
        var d = _addonText(id);
        return d.Length > 0 ? $"{id}   {d}" : id;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        foreach (var (sec, row, y) in _layout)
        {
            if (row is null)
            {
                var sib = sec.Rows.Select(r => r.Prob).ToList();
                double s = ProbabilityGroups.Sum(_item, sib);
                using var hf = new Font(Font, FontStyle.Bold);
                TextRenderer.DrawText(g, AddonEditor.SectionTitle(sec.Name).ToUpperInvariant(), hf, new Rectangle(8, y, ClientSize.Width - 160, HeadH), Accent, TextFormatFlags.VerticalCenter);
                if (sec.Rows.Count > 1)
                {
                    var col = ProbabilityGroups.SumsToOne(s, sib.Count) ? Green : s <= 0.0005 ? Muted : Red;
                    TextRenderer.DrawText(g, $"Σ {s * 100:0.###}%", hf, new Rectangle(ClientSize.Width - 150, y, 140, HeadH), col, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
                using var line = new Pen(Track); g.DrawLine(line, 8, y + HeadH - 2, ClientSize.Width - 8, y + HeadH - 2);
                continue;
            }
            var c = Cols(y, sec.HasRefs);
            if (row == Selected) using (var sb = new SolidBrush(Sel)) g.FillRectangle(sb, 4, y, ClientSize.Width - 8, RowH);
            if (row.Prob == "") { TextRenderer.DrawText(g, RowName(row), Font, c.Name, Muted, TextFormatFlags.VerticalCenter); continue; }
            TextRenderer.DrawText(g, RowName(row), Font, c.Name, Fg(row), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            double p = ProbabilityGroups.Num(_item.Fields[row.Prob].Value);
            bool bad = p < 0 || p > 1.0005;
            using (var tb = new SolidBrush(Track)) g.FillRectangle(tb, c.Bar);
            int bw = (int)Math.Round(c.Bar.Width * Math.Clamp(p, 0, 1));
            if (bw > 0) using (var fb = new SolidBrush(bad ? Red : Green)) g.FillRectangle(fb, c.Bar with { Width = bw });
            g.DrawRectangle(Pens.Silver, c.Bar);
            TextRenderer.DrawText(g, $"{p * 100:0.###}%", Font, c.Pct, bad ? Red : ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            bool locked = _locks.Contains(row.Prob);
            TextRenderer.DrawText(g, locked ? "🔒" : "🔓", Font, c.Lock, locked ? Accent : Color.FromArgb(160, 170, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    Color Fg(ProbRow r) => r.Ref is not null && _addonText(_item.Fields[r.Ref].Value).Length == 0 ? Red : ForeColor;   // ID que não existe

    (ProbSection Sec, ProbRow Row)? Hit(Point pt, out (Rectangle Name, Rectangle Bar, Rectangle Pct, Rectangle Lock) cols)
    {
        cols = default;
        int y = pt.Y - AutoScrollPosition.Y;
        foreach (var (sec, row, ry) in _layout)
            if (row is { Prob.Length: > 0 } && y >= ry && y < ry + RowH) { cols = Cols(ry, sec.HasRefs); return (sec, row); }
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || Hit(e.Location, out var c) is not { } h) return;
        Selected = h.Row;
        var x = e.X - AutoScrollPosition.X;
        if (c.Lock.Contains(x, c.Lock.Y + 1)) { if (!_locks.Remove(h.Row.Prob)) _locks.Add(h.Row.Prob); Invalidate(); return; }
        if (x >= c.Bar.X - 4 && x <= c.Bar.Right + 4)
        {
            _drag = h.Row;
            _before = ProbabilityGroups.Siblings(_item, h.Row.Prob).ToDictionary(k => k, k => _item.Fields[k]);
            DragTo(x, c.Bar);
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag is null || e.Button != MouseButtons.Left) return;
        foreach (var (sec, row, ry) in _layout)
            if (row == _drag) { DragTo(e.X - AutoScrollPosition.X, Cols(ry, sec.HasRefs).Bar); return; }
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (_drag is not null) { _drag = null; _before = null; Changed?.Invoke(); } }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (Hit(e.Location, out var c) is { } h && h.Row.Ref is not null && c.Name.Contains(e.X - AutoScrollPosition.X, c.Name.Y + 1)) PickFor?.Invoke(h.Row);
    }

    void DragTo(int x, Rectangle bar)
    {
        if (_drag is null || _before is null) return;
        double v = Math.Round(Math.Clamp((x - bar.X) / (double)bar.Width, 0, 1), 3);
        foreach (var (k, f) in _before) _item.Fields[k] = f;
        var refOf = _sections.SelectMany(s => s.Rows).Where(r => r.Ref is not null).ToDictionary(r => r.Prob, r => r.Ref!);
        var nv = ProbabilityGroups.Set(_item, _drag.Prob, v, _locks, k => refOf.TryGetValue(k, out var rk) && !AddonGroups.Empty(_item.Fields[rk].Value));
        foreach (var (k, val) in nv) _item.Fields[k] = Field.Of(_before[k].Type, ProbabilityGroups.Fmt(val));
        Invalidate();
    }
}
