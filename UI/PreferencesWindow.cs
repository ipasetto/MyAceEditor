using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Tela de Preferências: seções (Servidor local, …) com uma linha por preferência — nome, explicação, estado e um
/// interruptor liga/desliga. Cada preferência diz como ler o estado atual e como aplicar (em segundo plano); uma nova
/// preferência é só mais um <see cref="Pref"/> na lista. Estado lido de verdade ao abrir (ex.: o gs instalado), não lembrado.
/// </summary>
internal sealed class PreferencesWindow : Form
{
    /// <summary>
    /// Get: estado atual (true = ligado; null = indisponível, com o motivo em Why). Set: aplica e devolve erro ou null.
    /// Confirm: pergunta antes de aplicar (ex.: reinicia mapas); null = aplica direto.
    /// </summary>
    internal sealed record Pref(string Section, string Glyph, Func<string> Title, Func<string> Detail,
        Func<(bool? On, string? Why)> Get, Func<bool, string?> Set, Func<bool, string?>? Confirm = null);

    /// <param name="sectionGlyphs">ícone de cada seção na barra lateral (seção = página: editor, servidor...)</param>
    public PreferencesWindow(IEnumerable<Pref> prefs, IReadOnlyDictionary<string, string>? sectionGlyphs = null)
    {
        Text = Loc.T("prefs.title", "Preferências");
        Width = 820; Height = 560; StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.5f); BackColor = Color.FromArgb(244, 246, 249);
        var head = new Label { Dock = DockStyle.Top, Height = 58, Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14f), ForeColor = UiKit.Fg, Image = UiKit.Icon(UiKit.Settings, UiKit.Accent, 26), ImageAlign = ContentAlignment.MiddleLeft };
        head.Text = "       " + Loc.T("prefs.title", "Preferências");
        var sub = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(22, 0, 0, 4), BackColor = Color.White, ForeColor = UiKit.Muted,
            Text = Loc.T("prefs.sub", "Cada opção liga e desliga; o estado mostrado é o real (lido agora)."), TextAlign = ContentAlignment.TopLeft };
        // uma página por seção (MyAce / servidor), trocada pela barra lateral (padrão UiKit: nada de abas em cima)
        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1) };
        var nav = new NavBar(tabs);
        var lists = new List<FlowLayoutPanel>();
        foreach (var g in prefs.GroupBy(p => p.Section))
        {
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 8, 16, 16), BackColor = Color.FromArgb(244, 246, 249) };
            list.Controls.Add(new Label { AutoSize = true, Text = g.Key, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiKit.Accent, Margin = new Padding(2, 14, 0, 6) });
            foreach (var p in g) list.Controls.Add(new PrefRow(p, this));
            var page = new TabPage { BackColor = list.BackColor };
            page.Controls.Add(list); tabs.TabPages.Add(page); lists.Add(list);
            var name = g.Key;
            nav.Add(sectionGlyphs?.GetValueOrDefault(name) ?? UiKit.Settings, () => name, () => name, page);
        }
        Controls.Add(tabs); Controls.Add(nav); Controls.Add(sub); Controls.Add(head);
        void Fit() { foreach (var l in lists) foreach (Control c in l.Controls) if (c is PrefRow r) r.Width = Math.Max(300, l.ClientSize.Width - l.Padding.Horizontal - 4); }
        foreach (var l in lists) l.Resize += (_, _) => Fit();
        Shown += (_, _) => Fit();
        Fit();
    }

    /// <summary>Linha de uma preferência: ícone, nome, explicação, estado e o interruptor.</summary>
    sealed class PrefRow : Panel
    {
        readonly Pref _p; readonly Form _owner;
        readonly ToggleSwitch _sw = new();
        readonly Label _state = new() { AutoSize = false, Height = 20, ForeColor = UiKit.Muted, Font = new Font("Segoe UI", 8.5f) };
        bool _busy;

        public PrefRow(Pref p, Form owner)
        {
            _p = p; _owner = owner;
            Height = 92; Width = 700; BackColor = Color.White; Margin = new Padding(0, 0, 0, 8); Padding = new Padding(14, 10, 14, 10);
            var icon = new PictureBox { Image = UiKit.Icon(p.Glyph, UiKit.Accent, 26), Size = new Size(30, 30), Location = new Point(14, 14) };
            var title = new Label { AutoSize = false, Location = new Point(56, 10), Height = 22, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiKit.Fg, Text = p.Title(), AutoEllipsis = true };
            var detail = new Label { AutoSize = false, Location = new Point(56, 32), Height = 36, ForeColor = UiKit.Muted, Text = p.Detail() };
            _state.Location = new Point(56, 68);
            Controls.AddRange(new Control[] { icon, title, detail, _state, _sw });
            void Place()
            {
                _sw.Location = new Point(Width - _sw.Width - 18, (Height - _sw.Height) / 2);
                title.Width = detail.Width = _state.Width = Math.Max(100, _sw.Left - 70);
            }
            Resize += (_, _) => Place();
            Place();
            _sw.Toggled += Apply;
            HandleCreated += (_, _) => Load();
        }

        // estado real em segundo plano (ex.: consulta o gs no WSL)
        void Load()
        {
            _sw.Enabled = false; _state.Text = Loc.T("prefs.reading", "Conferindo o estado atual…");
            Task.Run(() => { try { return _p.Get(); } catch (Exception ex) { return ((bool?)null, ex.Message); } }).ContinueWith(t => BeginInvoke(() => Show(t.Result)));
        }

        void Show((bool? On, string? Why) st)
        {
            _sw.SetQuiet(st.On == true);
            _sw.Enabled = st.On is not null && !_busy;
            _state.ForeColor = st.On is null ? UiKit.Warn : st.On.Value ? UiKit.Ok : UiKit.Muted;
            _state.Text = st.On is null ? "⚠ " + (st.Why ?? Loc.T("prefs.unavailable", "Indisponível agora."))
                : st.On.Value ? "● " + Loc.T("prefs.isOn", "Ligado") : "○ " + Loc.T("prefs.isOff", "Desligado");
        }

        void Apply(bool on)
        {
            if (_p.Confirm?.Invoke(on) is { } ask && MessageBox.Show(_owner, ask, Loc.T("prefs.title", "Preferências"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            { _sw.SetQuiet(!on); return; }
            _busy = true; _sw.Enabled = false; _state.ForeColor = UiKit.Accent; _state.Text = Loc.T("prefs.applying", "Aplicando…");
            Task.Run(() => { try { return _p.Set(on); } catch (Exception ex) { return ex.Message; } }).ContinueWith(t => BeginInvoke(() =>
            {
                _busy = false;
                if (t.Result is { } err) MessageBox.Show(_owner, err, Loc.T("prefs.failed", "Não deu para aplicar"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Load();   // mostra o que ficou de verdade
            }));
        }
    }
}

/// <summary>Interruptor liga/desliga (estilo Windows 11): clique ou Espaço troca; Toggled só vem do usuário.</summary>
internal sealed class ToggleSwitch : Control
{
    bool _on;
    public event Action<bool>? Toggled;
    public ToggleSwitch() { Size = new Size(52, 26); DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool On => _on;
    public void SetQuiet(bool on) { _on = on; Invalidate(); }
    void Flip() { if (!Enabled) return; _on = !_on; Invalidate(); Toggled?.Invoke(_on); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Flip(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Flip(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Color.White);
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = new GraphicsPath();
        int d = r.Height; path.AddArc(r.X, r.Y, d, d, 90, 180); path.AddArc(r.Right - d, r.Y, d, d, 270, 180); path.CloseFigure();
        var track = !Enabled ? Color.FromArgb(215, 220, 228) : _on ? UiKit.Accent : Color.White;
        using (var b = new SolidBrush(track)) g.FillPath(b, path);
        using (var p = new Pen(_on && Enabled ? UiKit.Accent : Color.FromArgb(140, 150, 165), 1.5f)) g.DrawPath(p, path);
        int k = d - 8, x = _on ? r.Right - k - 4 : r.X + 4;
        using (var b = new SolidBrush(_on && Enabled ? Color.White : Color.FromArgb(110, 120, 135))) g.FillEllipse(b, x, r.Y + 4, k, k);
    }
}
