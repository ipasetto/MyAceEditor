using MyAceEditor.PerfectWorld;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MyAceEditor.UI;

/// <summary>
/// Peças visuais comuns das abas: barra de comandos estilo Word (ícone em cima, texto embaixo), ícones desenhados com a
/// fonte de ícones do Windows (Segoe Fluent Icons no 11, Segoe MDL2 Assets no 10) e o cartão de estado fixo
/// (bolinha + título + detalhe). Tudo em código: nenhuma imagem a mais no programa.
/// </summary>
internal static class UiKit
{
    /// <summary>A pessoa está digitando num campo numérico desta tela? Ler .Value nessa hora valida o texto e joga o cursor para o início.</summary>
    public static bool TypingNumber(Control root)
    {
        for (Control? c = root.FindForm(); c is ContainerControl cc && cc.ActiveControl is { } a; c = a)
            if (a is NumericUpDown) return true;
        return false;
    }

    public static readonly Color Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128), Accent = Color.FromArgb(37, 99, 235),
        Ok = Color.FromArgb(22, 163, 74), Bad = Color.FromArgb(220, 38, 38), Warn = Color.FromArgb(217, 119, 6), Line = Color.FromArgb(218, 224, 232);

    // códigos da fonte de ícones (iguais na Fluent e na MDL2)
    public const string Play = "", Stop = "", Upload = "", Download = "", Refresh = "", Settings = "",
        People = "", Game = "", Folder = "", OpenFile = "", Save = "", Add = "", Copy = "",
        Delete = "", Undo = "", Redo = "", Left = "", Right = "", Back = "", Edit = "",
        Export = "", Picture = "", Swap = "", Cancel = "", Search = "", Server = "", Shop = "",
        Box = "", Down = "", Log = "";

    public const string Library = "", Flag = "", Cart = "", MapPin = "", Robot = "", CheckList = "", Globe = "", Forward = "", Bug = "", Paint = "", Zoom = "", ZoomOut = "", Fit = "", Hide = "", Layers = "";

    static string? _iconFamily;
    static string IconFamily => _iconFamily ??= new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    /// <summary>Ícone desenhado (glifo da fonte de ícones do Windows) num bitmap quadrado.</summary>
    public static Bitmap Icon(string glyph, Color color, int size = 20)
    {
        var key = (glyph, color.ToArgb(), size);
        if (Cache.TryGetValue(key, out var b)) return b;
        b = new Bitmap(size, size);
        using (var g = Graphics.FromImage(b))
        using (var f = new Font(IconFamily, size * 0.72f, GraphicsUnit.Pixel))
        using (var br = new SolidBrush(color))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph, f, br, new RectangleF(0, 0, size, size), sf);
        }
        return Cache[key] = b;
    }

    /// <summary>Barra de comandos estilo Word: botões grandes com ícone em cima e o nome embaixo.</summary>
    public static ToolStrip Ribbon()
    {
        var t = new ToolStrip
        {
            Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White, ImageScalingSize = new Size(22, 22),
            Padding = new Padding(6, 3, 6, 3), Renderer = new RibbonRenderer(), Font = new Font("Segoe UI", 9f), CanOverflow = true,
        };
        return t;
    }

    /// <summary>Botão da barra; o texto segue o idioma (key/pt) e a dica explica o que faz.</summary>
    public static ToolStripButton Cmd(ToolStrip bar, string glyph, string key, string pt, Action click, string? tipKey = null, string? tipPt = null, bool primary = false)
    {
        var b = new ToolStripButton
        {
            Image = Icon(glyph, primary ? Accent : Fg, 22), TextImageRelation = TextImageRelation.ImageAboveText, AutoSize = true,
            Padding = new Padding(4, 2, 4, 2), Margin = new Padding(1, 1, 1, 1), ForeColor = primary ? Accent : Fg,
        };
        if (primary) b.Font = new Font("Segoe UI Semibold", 9f);
        void T() { b.Text = Loc.T(key, pt); b.ToolTipText = tipKey is null ? b.Text : Loc.T(tipKey, tipPt ?? ""); }
        T(); AppLang.Changed += T;
        b.Click += (_, _) => click();
        bar.Items.Add(b);
        return b;
    }

    /// <summary>Botão com menu (▾) da barra.</summary>
    public static ToolStripDropDownButton Menu(ToolStrip bar, string glyph, string key, string pt)
    {
        var b = new ToolStripDropDownButton { Image = Icon(glyph, Fg, 22), TextImageRelation = TextImageRelation.ImageAboveText, Padding = new Padding(4, 2, 4, 2), ForeColor = Fg };
        void T() => b.Text = Loc.T(key, pt);
        T(); AppLang.Changed += T;
        bar.Items.Add(b);
        return b;
    }

    public static ToolStripMenuItem Item(ToolStripDropDownItem menu, string glyph, string key, string pt, Action click)
    {
        var i = new ToolStripMenuItem { Image = Icon(glyph, Fg, 16) };
        void T() => i.Text = Loc.T(key, pt);
        T(); AppLang.Changed += T;
        i.Click += (_, _) => click();
        menu.DropDownItems.Add(i);
        return i;
    }

    // palavra da chave/texto -> ícone; a 1ª que bater vale (a chave é igual em todos os idiomas)
    static readonly (string[] Words, string Glyph)[] ByWord =
    {
        (new[] { "escolher", "pick", "marcar", "mark" }, ""),   // "Escolher no mundo": alvo, não globo
        (new[] { "world", "mundo", "3d" }, "\uE774"),   // antes de "undo": "mundo" cont\u00E9m "undo"
        (new[] { "undo", "desfaz" }, "\uE7A7"), (new[] { "redo", "refaz" }, "\uE7A6"),
        (new[] { "saveSend", "send", "enviar", "upload" }, "\uE898"), (new[] { "save", "salvar", "gravar" }, "\uE74E"),
        (new[] { "clone", "clonar", "copy", "copiar", "duplic" }, "\uE8C8"), (new[] { "paste", "colar" }, "\uE77F"),
        (new[] { "delete", "remove", "excluir", "apagar", "remover", "kick", "ban" }, "\uE74D"),
        (new[] { "new", "add", "novo", "nova", "adicionar", "criar", "create", "＋", "+ " }, "\uE710"),
        (new[] { "teleport", "goto", "ir para", "ir até" }, "\uE707"),
        (new[] { "map", "mapa" }, "\uE826"), (new[] { "search", "find", "buscar", "procurar", "pesquis" }, "\uE721"),
        (new[] { "restart", "reinic", "reboot" }, ""), (new[] { "start", "iniciar", "ligar", "play" }, "\uE768"), (new[] { "stop", "parar", "desligar" }, "\uE71A"),
        (new[] { "refresh", "reload", "atualizar", "recarregar" }, "\uE72C"), (new[] { "export", "exportar", "extrair" }, "\uEDE1"),
        (new[] { "import", "importar", "download", "baixar" }, "\uE896"), (new[] { "check", "verif", "conferir" }, "\uE9D5"),
        (new[] { "fix", "corrigir", "repair" }, "\uE90F"), (new[] { "open", "abrir", "browse", "pasta", "folder" }, "\uE8B7"),
        (new[] { "account", "conta", "player", "jogador", "people" }, "\uE716"), (new[] { "password", "senha" }, "\uE8D7"),
        (new[] { "edit", "editar" }, "\uE70F"), (new[] { "translate", "traduz" }, "\uE8C1"), (new[] { "item" }, "\uE7B8"),
        (new[] { "gm", "admin" }, "\uE7EF"), (new[] { "event", "evento" }, "\uE787"), (new[] { "quest", "missão", "missao" }, "\uE7C1"),
        (new[] { "help", "atalho", "shortcut", "ajuda" }, "\uE765"),
        (new[] { "cancel", "cancelar", "close", "fechar" }, "\uE711"), (new[] { "back", "voltar" }, "\uE72B"), (new[] { "next", "próximo", "proximo" }, "\uE72A"),
    };

    /// <summary>Ícone para um botão pela chave do texto (ou o texto): null quando nenhuma palavra conhecida aparece.</summary>
    public static string? GlyphFor(string hint)
    {
        var h = hint.ToLowerInvariant();
        // "world.next Próximo": o prefixo da chave (área do programa) não diz nada sobre o botão
        int sp = h.IndexOf(' '), dot = h.IndexOf('.');
        if (dot > 0 && (sp < 0 || dot < sp)) h = h[(dot + 1)..];
        foreach (var (words, g) in ByWord) if (words.Any(w => h.Contains(w.ToLowerInvariant()))) return g;
        return null;
    }

    /// <summary>Botão comum no estilo das barras: plano, borda clara, ícone da função à esquerda; primary = azul.</summary>
    public static Button Decorate(Button b, string hint, bool primary = false, bool dark = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = primary ? Accent : dark ? Color.FromArgb(70, 80, 95) : Line;
        b.FlatAppearance.BorderSize = 1;
        if (primary) { b.BackColor = Accent; b.ForeColor = Color.White; }
        if (GlyphFor(hint) is { } g)
        {
            b.Image = Icon(g, primary || dark ? Color.White : Fg, 16);
            b.TextImageRelation = TextImageRelation.ImageBeforeText; b.ImageAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(6, 0, 6, 0);
            if (!b.AutoSize && b.Width > 0) b.Width += 18;
            // o ícone substitui o símbolo que o texto trazia na frente ("＋ Ponto", "↶ Desfazer", "🌐 Mundo", "✔")
            void Strip()
            {
                var t = b.Text; int k = 0;
                while (k < t.Length && !char.IsLetterOrDigit(t[k])) k++;
                if (k > 0 && k < t.Length && t[..k].Trim().Length > 0) b.Text = t[k..];
            }
            Strip(); b.TextChanged += (_, _) => Strip();
        }
        b.Cursor = Cursors.Hand;
        return b;
    }

    public static void Sep(ToolStrip bar) => bar.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 4, 6, 4) });

    sealed class RibbonRenderer : ToolStripProfessionalRenderer
    {
        public RibbonRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var p = new Pen(Line); e.Graphics.DrawLine(p, 0, e.AffectedBounds.Bottom - 1, e.AffectedBounds.Right, e.AffectedBounds.Bottom - 1);
        }
        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => Color.White;
            public override Color ToolStripGradientMiddle => Color.White;
            public override Color ToolStripGradientEnd => Color.White;
            public override Color ButtonSelectedHighlight => Color.FromArgb(232, 240, 254);
            public override Color ButtonSelectedGradientBegin => Color.FromArgb(232, 240, 254);
            public override Color ButtonSelectedGradientMiddle => Color.FromArgb(232, 240, 254);
            public override Color ButtonSelectedGradientEnd => Color.FromArgb(232, 240, 254);
            public override Color ButtonSelectedBorder => Color.FromArgb(191, 211, 250);
            public override Color ButtonPressedGradientBegin => Color.FromArgb(210, 226, 252);
            public override Color ButtonPressedGradientMiddle => Color.FromArgb(210, 226, 252);
            public override Color ButtonPressedGradientEnd => Color.FromArgb(210, 226, 252);
            public override Color ButtonCheckedGradientBegin => Color.FromArgb(210, 226, 252);
            public override Color ButtonCheckedGradientMiddle => Color.FromArgb(210, 226, 252);
            public override Color ButtonCheckedGradientEnd => Color.FromArgb(210, 226, 252);
            public override Color SeparatorDark => Line;
            public override Color SeparatorLight => Color.White;
        }
    }
}

/// <summary>Cartão de estado fixo no topo de uma aba: bolinha colorida, título grande e uma linha de detalhe.</summary>
internal sealed class StatusCard : Control
{
    public enum Kind { Off, On, Busy, Warn }
    Kind _kind = Kind.Off;
    string _title = "", _detail = "";
    int _pulse;
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 90 };

    public StatusCard()
    {
        DoubleBuffered = true; Height = 64; Dock = DockStyle.Top; BackColor = Color.White; Padding = new Padding(16, 0, 16, 0);
        _anim.Tick += (_, _) => { _pulse = (_pulse + 1) % 20; Invalidate(); };
    }

    public void Set(Kind kind, string title, string detail = "")
    {
        _kind = kind; _title = title; _detail = detail;
        _anim.Enabled = kind == Kind.Busy;
        Invalidate();
    }

    public Color Color => _kind switch { Kind.On => UiKit.Ok, Kind.Busy => UiKit.Accent, Kind.Warn => UiKit.Warn, _ => UiKit.Bad };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        // faixa colorida à esquerda + bolinha (pulsando enquanto carrega)
        using (var b = new SolidBrush(Color)) g.FillRectangle(b, 0, 0, 5, Height);
        int d = 18, x = 22, y = (Height - d) / 2;
        if (_kind == Kind.Busy)
        {
            int halo = d + _pulse;
            using var hb = new SolidBrush(Color.FromArgb(Math.Max(0, 90 - _pulse * 4), Color));
            g.FillEllipse(hb, x + d / 2 - halo / 2, y + d / 2 - halo / 2, halo, halo);
        }
        using (var b = new SolidBrush(Color)) g.FillEllipse(b, x, y, d, d);
        using var ft = new Font("Segoe UI Semibold", 13f);
        using var fd = new Font("Segoe UI", 9.5f);
        int tx = x + d + 14;
        TextRenderer.DrawText(g, _title, ft, new Point(tx, 9), UiKit.Fg);
        TextRenderer.DrawText(g, _detail, fd, new Rectangle(tx, 36, Width - tx - 10, 22), UiKit.Muted, TextFormatFlags.EndEllipsis);
        using var p = new Pen(UiKit.Line); g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    protected override void Dispose(bool disposing) { if (disposing) _anim.Dispose(); base.Dispose(disposing); }
}

/// <summary>
/// Navegação lateral da janela principal (no lugar das abas): ícone grande em cima, nome embaixo, destaque no escolhido.
/// Cada item aponta para uma página do TabControl (as abas continuam por baixo, só sem os cabeçalhos).
/// </summary>
internal sealed class NavBar : Control
{
    sealed record Entry(string Glyph, Func<string> Text, Func<string> Tip, TabPage Page);
    readonly List<Entry> _items = new();
    readonly TabControl _tabs;
    readonly ToolTip _tip = new();
    int _hover = -1;
    // altura do item acompanha a janela: todas as áreas sempre cabem (com Plugins o Extrator ficava escondido; usuário 01/11)
    int ItemH => Math.Clamp(Height / Math.Max(1, _items.Count), 50, 74);
    static readonly Color Back = Color.FromArgb(24, 32, 44), Sel = Color.FromArgb(40, 54, 74), Hov = Color.FromArgb(32, 43, 59);

    public NavBar(TabControl tabs)
    {
        _tabs = tabs; DoubleBuffered = true; Dock = DockStyle.Left; Width = 92; BackColor = Back;
        tabs.SelectedIndexChanged += (_, _) => Invalidate();
        MyAceEditor.PerfectWorld.AppLang.Changed += Invalidate;
    }

    public void Add(string glyph, Func<string> text, Func<string> tip, TabPage page) { _items.Add(new(glyph, text, tip, page)); Invalidate(); }

    int Hit(Point p) => p.Y / ItemH is var i && i >= 0 && i < _items.Count ? i : -1;

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = Hit(e.Location);
        if (h == _hover) return;
        _hover = h; Invalidate();
        _tip.Hide(this);   // a dica antiga ficava na tela ao passar para outro item
        _tip.SetToolTip(this, h >= 0 ? _items[h].Tip() : "");
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Hit(e.Location) is var h and >= 0) _tabs.SelectedTab = _items[h].Page;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var f = new Font("Segoe UI", 8.5f);
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var r = new Rectangle(0, i * ItemH, Width, ItemH);
            bool sel = _tabs.SelectedTab == it.Page;
            if (sel || i == _hover) using (var b = new SolidBrush(sel ? Sel : Hov)) g.FillRectangle(b, r);
            if (sel) using (var b = new SolidBrush(UiKit.Accent)) g.FillRectangle(b, 0, r.Y + 6, 4, ItemH - 12);
            int h = ItemH, isz = h >= 66 ? 28 : 22;                       // mais apertado: ícone menor e texto numa linha
            int iy = r.Y + Math.Max(4, (h - isz - 18) / 2);
            var ic = UiKit.Icon(it.Glyph, sel ? Color.White : Color.FromArgb(170, 182, 198), isz);
            g.DrawImage(ic, (Width - isz) / 2, iy);
            TextRenderer.DrawText(g, it.Text(), f, new Rectangle(2, iy + isz + 3, Width - 4, h >= 66 ? 32 : 16), sel ? Color.White : Color.FromArgb(170, 182, 198),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | (h >= 66 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine) | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) { _tip.Dispose(); MyAceEditor.PerfectWorld.AppLang.Changed -= Invalidate; } base.Dispose(disposing); }
}

internal static class FlatTabsExt
{
    /// <summary>Abas planas (texto + sublinhado azul na escolhida), no lugar do visual 3D antigo do Windows.</summary>
    public static TabControl Flat(this TabControl t, Color back, Color fore, Color muted)
    {
        t.DrawMode = TabDrawMode.OwnerDrawFixed;
        t.Padding = new Point(14, 6);
        t.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= t.TabPages.Count) return;
            bool sel = e.Index == t.SelectedIndex;
            var r = e.Bounds; r.Inflate(2, 2);
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, r);
            if (sel) using (var b = new SolidBrush(UiKit.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X + 4, e.Bounds.Bottom - 3, e.Bounds.Width - 8, 3);
            using var f = new Font(t.Font, sel ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, t.TabPages[e.Index].Text, f, e.Bounds, sel ? UiKit.Accent : muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        return t;
    }
}

/// <summary>Barra escura (Mundo 3D): realce azul suave no botão sob o mouse e no ligado.</summary>
internal sealed class DarkBarRenderer : ToolStripProfessionalRenderer
{
    public DarkBarRenderer() : base(new C()) { RoundedEdges = false; }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Color.White : Color.Gray; base.OnRenderItemText(e); }
    sealed class C : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(28, 32, 38), Hi = Color.FromArgb(50, 62, 80), On = Color.FromArgb(37, 64, 110);
        public override Color ToolStripGradientBegin => Bg; public override Color ToolStripGradientMiddle => Bg; public override Color ToolStripGradientEnd => Bg;
        public override Color ButtonSelectedHighlight => Hi; public override Color ButtonSelectedGradientBegin => Hi; public override Color ButtonSelectedGradientMiddle => Hi; public override Color ButtonSelectedGradientEnd => Hi;
        public override Color ButtonSelectedBorder => Color.FromArgb(80, 100, 130);
        public override Color ButtonPressedGradientBegin => On; public override Color ButtonPressedGradientMiddle => On; public override Color ButtonPressedGradientEnd => On;
        public override Color ButtonCheckedGradientBegin => On; public override Color ButtonCheckedGradientMiddle => On; public override Color ButtonCheckedGradientEnd => On;
        public override Color ButtonCheckedHighlight => On;
        public override Color SeparatorDark => Color.FromArgb(60, 66, 76); public override Color SeparatorLight => Bg;
    }

    /// <summary>Bolinha colorida (legenda de tipo de ponto).</summary>
    public static Bitmap Dot(Color c, int size = 14)
    {
        var b = new Bitmap(size, size);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var br = new SolidBrush(c)) g.FillEllipse(br, 1, 1, size - 3, size - 3);
        using (var p = new Pen(Color.FromArgb(200, 255, 255, 255))) g.DrawEllipse(p, 1, 1, size - 3, size - 3);
        return b;
    }
}

/// <summary>
/// Botões em pílula (segmentos) que trocam as páginas de um TabControl sem cabeçalho: mais claros que abas antigas.
/// Acompanha páginas que entram e saem (ex.: Loja do NPC) e textos que mudam ("Vínculos (16)").
/// </summary>
internal sealed class SegmentBar : Control
{
    readonly TabControl _tabs;
    readonly Func<TabPage, string?> _glyph;
    readonly List<(Rectangle R, TabPage P)> _hit = new();
    int _hover = -1;

    public SegmentBar(TabControl tabs, Func<TabPage, string?> glyph)
    {
        _tabs = tabs; _glyph = glyph; DoubleBuffered = true; Dock = DockStyle.Top; Height = 44; BackColor = Color.White; Padding = new Padding(10, 7, 10, 7);
        tabs.Appearance = TabAppearance.FlatButtons; tabs.SizeMode = TabSizeMode.Fixed; tabs.ItemSize = new Size(0, 1);
        tabs.SelectedIndexChanged += (_, _) => Invalidate();
        tabs.ControlAdded += (_, e) => { if (e.Control is TabPage p) p.TextChanged += (_, _) => Invalidate(); Invalidate(); };
        tabs.ControlRemoved += (_, _) => BeginInvokeSafe(Invalidate);
        foreach (TabPage p in tabs.TabPages) p.TextChanged += (_, _) => Invalidate();
    }

    void BeginInvokeSafe(Action a) { if (IsHandleCreated) BeginInvoke(a); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        _hit.Clear();
        using var f = new Font("Segoe UI", 9.5f); using var fb = new Font("Segoe UI Semibold", 9.5f);
        int x = Padding.Left, h = Height - Padding.Vertical;
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            var p = _tabs.TabPages[i];
            bool sel = _tabs.SelectedTab == p;
            var glyph = _glyph(p);
            int tw = TextRenderer.MeasureText(p.Text, sel ? fb : f).Width;
            var r = new Rectangle(x, Padding.Top, tw + 24 + (glyph is null ? 0 : 22), h);
            using (var path = Round(r, h / 2))
            {
                var back = sel ? UiKit.Accent : i == _hover ? Color.FromArgb(232, 240, 254) : Color.FromArgb(241, 245, 249);
                using var b = new SolidBrush(back); g.FillPath(b, path);
            }
            var fore = sel ? Color.White : UiKit.Fg;
            int tx = r.X + 12;
            if (glyph is not null) { g.DrawImage(UiKit.Icon(glyph, fore, 16), tx, r.Y + (h - 16) / 2); tx += 22; }
            TextRenderer.DrawText(g, p.Text, sel ? fb : f, new Rectangle(tx, r.Y, r.Right - tx, h), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            _hit.Add((r, p));
            x = r.Right + 6;
        }
    }

    static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath(); int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = _hit.FindIndex(x => x.R.Contains(e.Location));
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach (var (r, p) in _hit) if (r.Contains(e.Location)) { _tabs.SelectedTab = p; break; }
    }
}

/// <summary>Lista vertical de seções (ícone + nome + contagem), para escolher o grupo de campos de um item.</summary>
internal sealed class SectionList : ListBox
{
    public Func<object, string>? GlyphOf;
    public SectionList()
    {
        DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 34; BorderStyle = BorderStyle.None; IntegralHeight = false;
        BackColor = Color.FromArgb(248, 250, 252); Dock = DockStyle.Left; Width = 190; Font = new Font("Segoe UI", 9.5f);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        using (var b = new SolidBrush(sel ? Color.FromArgb(219, 234, 254) : BackColor)) g.FillRectangle(b, e.Bounds);
        if (sel) using (var b = new SolidBrush(UiKit.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
        var item = Items[e.Index];
        var color = sel ? UiKit.Accent : UiKit.Fg;
        if (GlyphOf?.Invoke(item) is { } gl) g.DrawImage(UiKit.Icon(gl, sel ? UiKit.Accent : UiKit.Muted, 18), e.Bounds.X + 12, e.Bounds.Y + (e.Bounds.Height - 18) / 2);
        using var f = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, item.ToString(), f, new Rectangle(e.Bounds.X + 38, e.Bounds.Y, e.Bounds.Width - 42, e.Bounds.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Passos de um assistente: círculos numerados ligados por uma linha, nome embaixo; clique pula para o passo.</summary>
internal sealed class StepperBar : Control
{
    readonly List<Func<string>> _names = new();
    int _current, _hover = -1;
    public event Action<int>? Picked;
    public Func<int, bool>? HasProblem;   // passo com pendência: círculo laranja
    static readonly Color Line = Color.FromArgb(70, 78, 90), Done = Color.FromArgb(80, 140, 220), Text = Color.Gainsboro;

    public StepperBar() { DoubleBuffered = true; Dock = DockStyle.Top; Height = 62; BackColor = Color.FromArgb(36, 41, 49); }
    public void Add(Func<string> name) { _names.Add(name); Invalidate(); }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Current { get => _current; set { _current = value; Invalidate(); } }

    Rectangle Slot(int i) { int w = Width / Math.Max(1, _names.Count); return new Rectangle(i * w, 0, w, Height); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        int n = _names.Count, d = 24, cy = 8 + d / 2;
        using (var p = new Pen(Line, 2))
            for (int i = 0; i < n - 1; i++) { var a = Slot(i); var b = Slot(i + 1); p.Color = i < _current ? Done : Line; g.DrawLine(p, a.X + a.Width / 2 + d / 2, cy, b.X + b.Width / 2 - d / 2, cy); }
        using var f = new Font("Segoe UI", 8.5f); using var fb = new Font("Segoe UI", 8.5f, FontStyle.Bold); using var fn = new Font("Segoe UI Semibold", 9f);
        for (int i = 0; i < n; i++)
        {
            var s = Slot(i); var c = new Rectangle(s.X + (s.Width - d) / 2, 8, d, d);
            bool cur = i == _current, past = i < _current, bad = HasProblem?.Invoke(i) == true && !cur;
            var fill = cur ? UiKit.Accent : past ? Done : Color.FromArgb(28, 32, 38);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, c);
            using (var p = new Pen(bad ? UiKit.Warn : cur || past ? fill : i == _hover ? Color.Silver : Line, 2)) g.DrawEllipse(p, c);
            TextRenderer.DrawText(g, past && !bad ? "✓" : (i + 1).ToString(), fn, c, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _names[i](), cur ? fb : f, new Rectangle(s.X, 36, s.Width, 20), cur ? Color.White : Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = _names.Count == 0 ? -1 : Math.Clamp(e.X * _names.Count / Math.Max(1, Width), 0, _names.Count - 1);
        if (h != _hover) { _hover = h; Cursor = Cursors.Hand; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (_hover >= 0) Picked?.Invoke(_hover); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}
