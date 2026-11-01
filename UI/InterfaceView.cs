using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor das janelas do jogo, aberto pelo Extrator PCK em qualquer pacote que tenha janelas XML (interfaces.pck...),
/// com a tela desenhada como no jogo (texturas do surfaces.pck e fontes do cliente). Clique seleciona um controle;
/// arrastar move; o quadradinho do canto redimensiona; setas movem 1 px (Shift = 10); duplo clique edita o texto.
/// Lista de controles (acha os escondidos), dica, "Esconder" (x = -2000, nunca apagar), busca nos textos de todas as
/// janelas e o .dcf que carrega a janela (ideias estudadas no PIE 2.0 e no AUIEditor; ENGINE-KB §E9b).
/// Grava na pasta do sPCK (&lt;pacote&gt;.files\&lt;caminho no pacote&gt;); gravar direto no .pck ainda não existe (issue #13).
/// </summary>
internal sealed class InterfaceView : UserControl
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128), Accent = Color.FromArgb(37, 99, 235);

    readonly Action _back;
    PckArchive? _pck, _surfaces;
    GameTextures? _tex;
    string? _pckPath;

    readonly TextBox _filter = new() { Dock = DockStyle.Top };
    readonly ListBox _files = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 38 };
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Padding = new Padding(4, 4, 0, 0) };
    readonly ToolStrip _bar = UiKit.Ribbon();
    readonly CheckBox _outlines = new() { AutoSize = true, Checked = true, Padding = new Padding(8, 6, 0, 0) };
    readonly Canvas _canvas;
    readonly Panel _props = new() { Dock = DockStyle.Right, Width = 300, BackColor = Color.White, Padding = new Padding(12, 8, 12, 8), AutoScroll = true };
    readonly Label _noSel = new() { Dock = DockStyle.Top, Height = 90, ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
    Control? _selBody;
    readonly Label _selName = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 10), MaximumSize = new Size(270, 0) };
    readonly NumericUpDown _x = N(), _y = N(), _w = N(), _h = N();
    readonly TextBox _text = new() { Multiline = true, Height = 60 };
    readonly TextBox _hint = new() { Multiline = true, Height = 44 };
    // fonte do texto (PW-KB §S20c): Align no controle (0 esquerda, 1 centro, 2 direita); FontName/FontSize/Bold/Outline no <Text>
    // barra de formatação estilo Word, em cima da tela
    readonly ToolStrip _fmt = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, Padding = new Padding(4, 2, 0, 2), BackColor = Color.White };
    readonly ToolStripComboBox _font = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly ToolStripComboBox _fontSize = new() { Width = 50 };
    readonly ToolStripButton _bold = new("B") { CheckOnClick = false, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = false, Width = 28 };
    readonly ToolStripButton _alL = new("⯇≡"), _alC = new("≡"), _alR = new("≡⯈");
    readonly ToolStripButton _color = new("A") { Font = new Font("Segoe UI", 10, FontStyle.Bold | FontStyle.Underline), AutoSize = false, Width = 28 };
    readonly ToolStripComboBox _outline = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    static readonly string[] FontNames = { "方正细黑一简体", "方正隶二简体", "方正隶变简体" };   // fzxh1jw, fzl2jw, fzlbjw
    readonly Label _imgs = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(270, 0) };
    readonly Button _hide = new() { AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
    readonly CheckBox _inTexts = new() { Dock = DockStyle.Top, Height = 24, Padding = new Padding(4, 0, 0, 0) };
    readonly TextBox _ctlFilter = new() { Dock = DockStyle.Top };
    readonly ListBox _ctlList = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    readonly ToolTip _tip = new();
    static NumericUpDown N() => new() { Minimum = -3000, Maximum = 5000, Width = 80 };

    /// <summary>Título de seção (cinza, maiúsculas pequenas), como nos painéis de propriedades dos editores modernos.</summary>
    static Label Header(string key, string pt, string glyph)
    {
        var l = new Label { Dock = DockStyle.Top, Height = 30, ForeColor = Fg, Font = new Font("Segoe UI Semibold", 9.5f), TextAlign = ContentAlignment.BottomLeft,
            Image = UiKit.Icon(glyph, Muted, 16), ImageAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 3) };
        void T() => l.Text = "      " + Loc.T(key, pt);
        T(); AppLang.Changed += T;
        return l;
    }

    UiDocument? _doc;
    string? _file;          // caminho dentro do pacote, ex.: interfaces\version01\qshop.xml
    bool _fromFolder, _dirty, _binding;
    string? _sel;
    readonly List<string> _undo = new(), _redo = new();
    Dictionary<string, List<ClientDialogs.DcfLine>> _layout = new();
    Dictionary<string, string>? _textIndex;   // janela → seus textos (busca "nos textos"), montado na 1ª busca

    // x que o próprio jogo usa para esconder um controle (PW-KB §S20); apagar controle pode derrubar o cliente
    const int HiddenX = -2000;

    public InterfaceView(Action back)
    {
        _back = back;
        BackColor = Bg; Dock = DockStyle.Fill;
        _canvas = new Canvas(this) { Dock = DockStyle.Fill };
        // esquerda: as janelas do pacote em cima e os controles da janela aberta embaixo (como camadas)
        var left = new SplitContainer { Dock = DockStyle.Left, Width = 290, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Bg, FixedPanel = FixedPanel.None };
        var winBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8, 0, 8, 6) };
        winBox.Controls.Add(_files); winBox.Controls.Add(_inTexts); winBox.Controls.Add(_filter); winBox.Controls.Add(Header("ui.hWindows", "Janelas do jogo", UiKit.Layers));
        var ctlBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8, 0, 8, 6) };
        ctlBox.Controls.Add(_ctlList); ctlBox.Controls.Add(_ctlFilter); ctlBox.Controls.Add(Header("ui.hControls", "Controles desta janela", UiKit.CheckList));
        left.Panel1.Controls.Add(winBox); left.Panel2.Controls.Add(ctlBox);
        left.HandleCreated += (_, _) => BeginInvoke(() => { try { left.SplitterDistance = Math.Max(120, left.Height * 55 / 100); } catch { } });
        _files.DrawItem += DrawDialogItem;
        BuildBar(); BuildProps();
        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_canvas); center.Controls.Add(_fmt);
        Controls.Add(center); Controls.Add(_props); Controls.Add(left); Controls.Add(_info); Controls.Add(_bar);
        _filter.TextChanged += (_, _) => FillList();
        _inTexts.CheckedChanged += (_, _) => FillList();
        _files.SelectedIndexChanged += (_, _) => { if (_files.SelectedItem is DialogItem f) OpenDialog(f.Path); };
        AppLang.Changed += Texts; Texts();
    }

    void Texts()
    {
        _filter.PlaceholderText = Loc.T("ui.filter", "Filtrar janelas (ex.: shop, inventory)");
        _outlines.Text = Loc.T("ui.outlines", "Mostrar contornos");
        _inTexts.Text = Loc.T("ui.inTexts", "Procurar também nos textos das janelas");
        _ctlFilter.PlaceholderText = Loc.T("ui.ctlFilter", "Procurar controle (nome ou texto)");
        _hide.Text = Loc.T("ui.hide", "Esconder controle");
        _tip.SetToolTip(_hide, Loc.T("ui.hideTip", "Leva o controle para x = -2000, como o próprio jogo faz, e ele some da janela. Não apague controles: o código do jogo procura cada um pelo nome e pode fechar se faltar. Para mostrar de novo, mude o X."));
    }

    // barra estilo Word: voltar | salvar | desfazer | arquivos | contornos
    void BuildBar()
    {
        UiKit.Cmd(_bar, UiKit.Back, "ui.rBack", "Voltar", () => { if (AskDiscard()) { _dirty = false; _back(); } }, "ui.back", "◀ Voltar ao extrator");
        UiKit.Cmd(_bar, UiKit.Save, "ui.rSave", "Salvar janela", Save, "ui.rSaveTip", "Grava a janela no pacote (com backup fora do cliente).", primary: true);
        UiKit.Sep(_bar);
        UiKit.Cmd(_bar, UiKit.Undo, "ui.rUndo", "Desfazer", () => Undo(), "ui.rUndoTip", "Desfaz a última mudança (Ctrl+Z).");
        UiKit.Cmd(_bar, UiKit.Redo, "ui.rRedo", "Refazer", () => Redo(), "ui.rRedoTip", "Refaz o que foi desfeito (Ctrl+Y).");
        UiKit.Sep(_bar);
        // arquivos: exportar/importar (conferido), trocar com outro cliente, telas de carregamento (UiTools)
        var files = UiKit.Menu(_bar, UiKit.Folder, "ui.rFiles", "Arquivos");
        UiKit.Item(files, UiKit.Export, "ui.mExport", "Exportar o pacote para uma pasta…", () => { if (_pckPath is not null) UiTools.Export(this, _pckPath); });
        UiKit.Item(files, UiKit.Download, "ui.mImport", "Importar pasta (confere antes de gravar)…", () => { if (_pckPath is not null && AskDiscard()) AfterWrite(UiTools.Import(this, _pckPath)); });
        UiKit.Item(files, UiKit.Save, "ui.packFolder", "Gravar pasta .files no pacote", PackFolder);
        UiKit.Item(files, UiKit.Folder, "ui.openFolder", "Abrir pasta .files", () => { if (_pckPath is not null) { var d = _pckPath + ".files"; Directory.CreateDirectory(d); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d) { UseShellExecute = true }); } });
        files.DropDownItems.Add(new ToolStripSeparator());
        UiKit.Item(files, UiKit.Swap, "ui.mSwap", "Trocar com a interface de outro cliente…", () => { if (_pckPath is not null && AskDiscard()) AfterWrite(UiTools.Swap(this, _pckPath)); });
        UiKit.Item(files, UiKit.Picture, "ui.mLoading", "Telas de carregamento…", () => { if (_pckPath is not null && AskDiscard()) AfterWrite(UiTools.LoadingScreens(this, Path.GetDirectoryName(_pckPath))); });
        UiKit.Sep(_bar);
        UiKit.Cmd(_bar, UiKit.Fit, "ui.rFit", "Ajustar", () => { _canvas.Zoom = null; _canvas.Invalidate(); }, "ui.rFitTip", "A janela ocupa a tela toda (Ctrl + roda do mouse muda o zoom).");
        UiKit.Cmd(_bar, UiKit.Zoom, "ui.rZoomIn", "Aumentar", () => { _canvas.Zoom = Math.Min(8f, (_canvas.Zoom ?? _canvas.Scale) * 1.25f); _canvas.Invalidate(); }, "ui.rZoomInTip", "Aproxima a janela.");
        UiKit.Cmd(_bar, UiKit.ZoomOut, "ui.rZoomOut", "Diminuir", () => { _canvas.Zoom = Math.Max(0.25f, (_canvas.Zoom ?? _canvas.Scale) / 1.25f); _canvas.Invalidate(); }, "ui.rZoomOutTip", "Afasta a janela.");
        UiKit.Cmd(_bar, UiKit.Picture, "ui.rReal", "Tamanho real", () => { _canvas.Zoom = 1f; _canvas.Invalidate(); }, "ui.rRealTip", "Mostra a janela no tamanho do jogo (100%).");
        UiKit.Sep(_bar);
        _outlines.BackColor = Color.White;
        _bar.Items.Add(new ToolStripControlHost(_outlines) { Margin = new Padding(4, 14, 0, 0) });
        _outlines.CheckedChanged += (_, _) => _canvas.Invalidate();
    }

    void BuildProps()
    {
        Control Pair(string k1, string p1, Control c1, string k2, string p2, Control c2)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 54, ColumnCount = 2, RowCount = 2 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Label L(string k, string pt) { var l = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.BottomLeft }; void T() => l.Text = Loc.T(k, pt); T(); AppLang.Changed += T; return l; }
            c1.Dock = c2.Dock = DockStyle.Fill;
            t.Controls.Add(L(k1, p1), 0, 0); t.Controls.Add(L(k2, p2), 1, 0); t.Controls.Add(c1, 0, 1); t.Controls.Add(c2, 1, 1);
            return t;
        }
        Control Box(Control c, int h) { c.Dock = DockStyle.Top; c.Height = h; return c; }
        _selName.AutoSize = false; _selName.Dock = DockStyle.Top; _selName.Height = 30; _selName.Font = new Font("Segoe UI Semibold", 11f);
        _hide.Dock = DockStyle.Top; _hide.Height = 30; _hide.Image = UiKit.Icon(UiKit.Hide, Fg, 16); _hide.TextImageRelation = TextImageRelation.ImageBeforeText;
        _imgs.Dock = DockStyle.Top; _imgs.AutoSize = true;
        // ordem de cima para baixo (Dock Top empilha ao contrário da ordem de inclusão)
        var parts = new Control[]
        {
            _selName,
            Header("ui.hPos", "Posição e tamanho", UiKit.Fit),
            Pair("ui.x", "X", _x, "ui.y", "Y", _y),
            Pair("ui.w", "Largura", _w, "ui.h", "Altura", _h),
            Header("ui.hText", "Texto que aparece", UiKit.Edit),
            Box(_text, 64),
            Header("ui.hHint", "Dica (ao passar o mouse)", UiKit.Search),
            Box(_hint, 48),
            Header("ui.hActions", "Ações", UiKit.Settings),
            _hide,
            new Label { Dock = DockStyle.Top, Height = 8 },
            _imgs,
        };
        var body = new Panel { Dock = DockStyle.Top, AutoSize = true };
        foreach (var c in parts.Reverse()) body.Controls.Add(c);
        _selBody = body;
        _props.Controls.Add(body); _props.Controls.Add(_noSel);
        void T() => _noSel.Text = Loc.T("ui.noSel", "Clique num controle da janela\n(ou na lista Controles, à esquerda)\npara editar aqui.");
        T(); AppLang.Changed += T;
        BuildFormatBar();
        _fmt.Renderer = UiKit.Ribbon().Renderer;
        foreach (var n in new[] { _x, _y, _w, _h }) n.ValueChanged += (_, _) => ApplyRect();
        _text.Leave += (_, _) => ApplyText();
        _text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { ApplyText(); e.SuppressKeyPress = true; } };
        _hint.Leave += (_, _) => ApplyHint();
        _hint.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { ApplyHint(); e.SuppressKeyPress = true; } };
        _hide.Click += (_, _) =>
        {
            if (_sel is null || _doc?.Dialog[_sel] is not { } c || c.Rect.X <= -1000) return;
            var name = _sel;
            Change(() => _doc!.SetAttr(name, "x", HiddenX.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            ShowProps();
        };
        _ctlFilter.TextChanged += (_, _) => FillControls();
        _ctlList.SelectedIndexChanged += (_, _) => { if (!_binding && _ctlList.SelectedItem is CtlItem it && it.Name != _sel) Select(it.Name); };
    }

    // fonte do texto do controle selecionado, como na barra do Word: fonte, tamanho, negrito, alinhamento, cor, contorno
    void BuildFormatBar()
    {
        static string S(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fontSize.Items.AddRange(new object[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36" });
        _fmt.Items.AddRange(new ToolStripItem[] { _font, _fontSize, new ToolStripSeparator(), _bold, _color, new ToolStripSeparator(), _alL, _alC, _alR, new ToolStripSeparator(), _outline });
        void Texts()
        {
            int f = _font.SelectedIndex, o = _outline.SelectedIndex;
            _font.Items.Clear(); _font.Items.AddRange(new object[] { Loc.T("ui.fontNormal", "Normal (fzxh1jw)"), Loc.T("ui.fontTitle", "Título (fzl2jw)"), Loc.T("ui.fontTitle2", "Título 2 (fzlbjw)") });
            _outline.Items.Clear(); _outline.Items.AddRange(new object[] { Loc.T("ui.outline0", "Sem contorno"), Loc.T("ui.outline1", "Contorno 1 px"), Loc.T("ui.outline2", "Contorno 2 px"), Loc.T("ui.outline3", "Contorno 3 px") });
            _binding = true; _font.SelectedIndex = f; _outline.SelectedIndex = o; _binding = false;
            _font.ToolTipText = Loc.T("ui.font", "Fonte (as 3 do jogo, pasta element\\font)");
            _fontSize.ToolTipText = Loc.T("ui.fontSize", "Tamanho da letra (FontSize ≈ altura em pixels)");
            _bold.ToolTipText = Loc.T("ui.bold", "Negrito");
            _color.ToolTipText = Loc.T("ui.color", "Cor do texto");
            _alL.ToolTipText = Loc.T("ui.alignLeft", "Alinhar à esquerda");
            _alC.ToolTipText = Loc.T("ui.alignCenter", "Centralizar");
            _alR.ToolTipText = Loc.T("ui.alignRight", "Alinhar à direita");
            _outline.ToolTipText = Loc.T("ui.outlineTip", "Borda escura em volta das letras");
        }
        Texts(); AppLang.Changed += Texts;
        _font.SelectedIndexChanged += (_, _) => { if (_font.SelectedIndex >= 0) ApplyFont("FontName", FontNames[_font.SelectedIndex]); };
        void Size()
        {
            if (int.TryParse(_fontSize.Text, out var v) && v is >= 6 and <= 72 && _sel is not null && _doc?.Dialog[_sel] is { } c && (int)c.FontSize != v)
                ApplyFont("FontSize", S(v));
        }
        _fontSize.SelectedIndexChanged += (_, _) => Size();
        _fontSize.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Size(); e.SuppressKeyPress = true; } };
        _fontSize.Leave += (_, _) => Size();
        _bold.Click += (_, _) => ApplyFont("Bold", _bold.Checked ? "false" : "true");
        _alL.Click += (_, _) => ApplyFont("Align", "0", onControl: true);
        _alC.Click += (_, _) => ApplyFont("Align", "1", onControl: true);
        _alR.Click += (_, _) => ApplyFont("Align", "2", onControl: true);
        _outline.SelectedIndexChanged += (_, _) => { if (_outline.SelectedIndex >= 0) ApplyFont("Outline", S(_outline.SelectedIndex)); };
        _color.Click += (_, _) =>
        {
            if (_sel is null || _doc?.Dialog[_sel] is not { Text: not null } c) return;
            using var dlg = new ColorDialog { Color = c.Color ?? Color.White, FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var k = dlg.Color;
            ApplyFont("Color", $"{k.R},{k.G},{k.B},{c.Color?.A ?? 255}");
        };
    }

    void ShowFormat(GameDialog.Control? c)
    {
        bool has = c?.Text is not null;
        foreach (ToolStripItem i in _fmt.Items) i.Enabled = c is not null && (has || i == _alL || i == _alC || i == _alR);
        if (c is null) return;
        int fi = Array.FindIndex(FontNames, n => c.FontName?.Contains(n[2..4]) == true);   // 细黑 / 隶二 / 隶变
        _font.SelectedIndex = fi < 0 ? 0 : fi;
        _fontSize.Text = ((int)c.FontSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _bold.Checked = c.Bold;
        _alL.Checked = c.Align is not (1 or 2); _alC.Checked = c.Align == 1; _alR.Checked = c.Align == 2;
        _color.ForeColor = c.Color is { } k ? Color.FromArgb(255, k) : Fg;
        _outline.SelectedIndex = Math.Clamp(c.Outline, 0, 3);
    }

    sealed record CtlItem(string Name, string Label) { public override string ToString() => Label; }

    // todos os controles na ordem do XML (a ordem é a de desenho: os de baixo ficam por cima)
    void FillControls()
    {
        var q = _ctlFilter.Text.Trim();
        _binding = true;
        try
        {
            _ctlList.BeginUpdate(); _ctlList.Items.Clear();
            if (_doc is null) return;
            foreach (var c in _doc.Dialog.Controls.Values)
            {
                if (q.Length > 0 && !c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && c.Text?.Contains(q, StringComparison.OrdinalIgnoreCase) != true
                    && c.Hint?.Contains(q, StringComparison.OrdinalIgnoreCase) != true) continue;
                var label = $"{c.Name}  ({c.Tag})" + (c.Rect.X <= -1000 ? "  " + Loc.T("ui.hidden", "[escondido]") : "")
                    + (c.Text is { Length: > 0 } t ? "  «" + (t.Length > 24 ? t[..24] + "…" : t) + "»" : "");
                int i = _ctlList.Items.Add(new CtlItem(c.Name, label));
                if (c.Name == _sel) _ctlList.SelectedIndex = i;
            }
        }
        finally { _ctlList.EndUpdate(); _binding = false; }
    }

    // ---------- arquivos ----------
    /// <summary>Quantas janelas XML o pacote tem (o extrator mostra o botão só quando há).</summary>
    public static int CountDialogs(PckArchive pck) => pck.Entries.Count(e => e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

    /// <summary>Abre um pacote com janelas. As texturas vêm do surfaces.pck e as fontes da pasta do jogo (mesma pasta do pacote).</summary>
    public void LoadPack(string pckPath)
    {
        if (_pckPath == pckPath && _pck is not null) return;
        _pck?.Dispose(); _surfaces?.Dispose(); _tex?.Dispose();
        _pckPath = pckPath; _doc = null; _file = null; _sel = null; _dirty = false; _textIndex = null;
        var dir = Path.GetDirectoryName(pckPath)!;
        try
        {
            _pck = new PckArchive(pckPath);   // leitura própria (o extrator pode estar lendo o dele)
            _layout = ClientDialogs.Layout(_pck);
            FillControls();
            var sp = Path.Combine(dir, "surfaces.pck");
            _surfaces = File.Exists(sp) ? new PckArchive(sp) : null;
            _tex = _surfaces is null ? null : new GameTextures(_surfaces);
            GameFonts.Load(dir);
        }
        catch (Exception ex) { _info.Text = ex.Message; return; }
        FillList(); ShowProps(); _canvas.Invalidate();
    }

    IEnumerable<string> AllDialogs()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_pck is not null)
            foreach (var e in _pck.Entries)
                if (e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) set.Add(e.Path);
        // cópias de trabalho que só existem na pasta .files (janelas novas)
        if (_pckPath is not null && Directory.Exists(_pckPath + ".files"))
            foreach (var f in Directory.EnumerateFiles(_pckPath + ".files", "*.xml", SearchOption.AllDirectories))
                set.Add(Path.GetRelativePath(_pckPath + ".files", f));
        return set;
    }

    // Item da lista: as janelas de version01 (interface nova) primeiro. A raiz do interfaces.pck é a interface clássica
    // (ingame.dcf), mas o ingame-v1.dcf também carrega ~100 janelas dela (bag.xml, map.xml, winmain.xml...): quem decide é
    // o .dcf (PW-KB §S20b). Sem .dcf no pacote, vale a regra antiga: raiz = antiga.
    sealed record DialogItem(string Path, string Label) { public override string ToString() => Label; }

    void DrawDialogItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _files.Items[e.Index] is not DialogItem it) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Color.FromArgb(219, 234, 254) : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
        if (sel) using (var b = new SolidBrush(Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 4, 3, e.Bounds.Height - 8);
        var name = System.IO.Path.GetFileNameWithoutExtension(it.Path);
        var rest = it.Label.Length > it.Path.Length ? it.Label[it.Path.Length..].Trim() : "";
        var folder = System.IO.Path.GetDirectoryName(it.Path) ?? "";
        using var f1 = new Font("Segoe UI Semibold", 10f);
        using var f2 = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, name, f1, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 2, e.Bounds.Width - 12, 20), Fg, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, folder + (rest.Length > 0 ? "  ·  " + rest : ""), f2, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 20, e.Bounds.Width - 12, 16),
            rest.Length > 0 ? Color.FromArgb(180, 83, 9) : Muted, TextFormatFlags.EndEllipsis);
    }

    static bool IsRoot(string path) => path.StartsWith(@"interfaces\", StringComparison.OrdinalIgnoreCase)
        && !path.Contains(@"\version", StringComparison.OrdinalIgnoreCase);

    // da raiz e fora da interface nova (nenhum *-v1.dcf a carrega)
    bool IsLegacy(string path) => IsRoot(path)
        && !(_layout.TryGetValue(path, out var l) && l.Any(x => x.Dcf.Contains("-v", StringComparison.OrdinalIgnoreCase)));

    string LegacyNote(string path) => _layout.Count > 0 && _layout.ContainsKey(path)
        ? Loc.T("ui.classic", "(só na interface clássica)") : Loc.T("ui.legacy", "(antiga, o jogo não usa)");

    // a cópia de trabalho (pasta .files) vale mais que o pacote
    byte[]? ReadDialog(string file, out bool fromFolder)
    {
        fromFolder = _pckPath is not null && File.Exists(FolderPath(file));
        if (fromFolder) return File.ReadAllBytes(FolderPath(file));
        return _pck?.Find(file) is { } e ? _pck.Read(e) : null;
    }

    // textos (e dicas) de cada janela, para achar em que janela está uma frase
    Dictionary<string, string> TextIndex()
    {
        if (_textIndex is not null) return _textIndex;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (var f in AllDialogs())
                try { if (ReadDialog(f, out _) is { } b) d[f] = string.Join("\n", UiTexts.Find(new UiDocument(b).Xml).Values.Select(i => i.Text)); }
                catch { }
        }
        finally { Cursor = Cursors.Default; }
        return _textIndex = d;
    }

    void FillList()
    {
        var q = _filter.Text.Trim();
        var idx = _inTexts.Checked && q.Length > 0 ? TextIndex() : null;
        string? Hit(string f)
        {
            if (idx is null || !idx.TryGetValue(f, out var all)) return null;
            int i = all.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int a = all.LastIndexOf('\n', i) + 1, b = all.IndexOf('\n', i); if (b < 0) b = all.Length;
            var line = all[a..b];
            return line.Length > 40 ? line[..40] + "…" : line;
        }
        _files.BeginUpdate(); _files.Items.Clear();
        foreach (var f in AllDialogs().OrderBy(IsLegacy).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var hit = Hit(f);
            if (q.Length > 0 && !f.Contains(q, StringComparison.OrdinalIgnoreCase) && hit is null) continue;
            _files.Items.Add(new DialogItem(f, (IsLegacy(f) ? f + "   " + LegacyNote(f) : f) + (hit is null ? "" : "   «" + hit + "»")));
        }
        _files.EndUpdate();
        _info.Text = string.Format(Loc.T("ui.count2", "{0} janelas · edite na tela; \"Salvar janela\" grava direto no {1} (com backup)."),
            _files.Items.Count, Path.GetFileName(_pckPath));
    }

    // convenção do sPCK: <pacote>.files\<caminho completo dentro do pacote>
    string FolderPath(string file) => Path.Combine(_pckPath! + ".files", file);

    void OpenDialog(string file)
    {
        if (file != _file && !AskDiscard()) return;
        try
        {
            var bytes = ReadDialog(file, out _fromFolder);
            if (bytes is null) return;
            var doc = new UiDocument(bytes);
            if (doc.Dialog.Controls.Count == 0 && doc.Dialog.Size.Width == 0)
            { _info.Text = string.Format(Loc.T("ui.notDialog", "{0} não é uma janela do jogo (sem <DIALOG>)."), file); return; }
            _doc = doc; _file = file; _sel = null; _dirty = false;
            _undo.Clear(); _redo.Clear();
            _ctlFilter.Text = ""; FillControls();
            ShowProps(); _canvas.Invalidate(); UpdateInfo();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("tab.interface", "Interface"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Diagnóstico (--snapshot ... ui:qshop.xml[?texto]): abre a janela e seleciona o 1º botão; com "?texto", procura o texto em todas as janelas.</summary>
    internal void OpenForSnapshot(string file)
    {
        if (file.IndexOf('?') is var qi and >= 0) { _inTexts.Checked = true; _filter.Text = file[(qi + 1)..]; file = file[..qi]; }
        var hit = AllDialogs().Where(f => f.EndsWith("\\" + file, StringComparison.OrdinalIgnoreCase) || f.Equals(file, StringComparison.OrdinalIgnoreCase))
            .OrderBy(IsLegacy).FirstOrDefault();
        if (hit is null) return;
        OpenDialog(hit);
        _sel = _doc?.Dialog.Controls.Values.FirstOrDefault(c => c.Tag == "STILLIMAGEBUTTON")?.Name;
        ShowProps(); _canvas.Invalidate();
    }

    void UpdateInfo()
    {
        if (_doc is null || _file is null) return;
        _info.Text = $"{_file} · {_doc.Dialog.Size.Width}×{_doc.Dialog.Size.Height} · {_doc.Dialog.Controls.Count} " + Loc.T("ui.controls", "controles") + " · "
            + (IsLegacy(_file) && !_layout.ContainsKey(_file) ? Loc.T("ui.legacyInfo", "INTERFACE ANTIGA: o jogo usa a cópia em version01") + " · " : "")
            + (_fromFolder ? Loc.T("ui.fromFolder", "cópia de trabalho (pasta .files)") : string.Format(Loc.T("ui.fromPck", "lido do {0}"), Path.GetFileName(_pckPath)))
            + DcfInfo()
            + (_dirty ? " · " + Loc.T("ui.unsaved", "não salvo") : "");
    }

    // em que .dcf a janela é carregada (o jogo só carrega o que está lá; PW-KB §S20b)
    string DcfInfo()
    {
        if (_file is null || _layout.Count == 0 || !_file.StartsWith(@"interfaces\", StringComparison.OrdinalIgnoreCase) || IsRoot(_file) && !_layout.ContainsKey(_file) && _pck?.Find(_file.Insert(@"interfaces\".Length, @"version01\")) is not null) return "";
        if (!_layout.TryGetValue(_file, out var lines))
            return " · " + Loc.T("ui.dcfNone", "fora dos .dcf: o jogo só usa esta janela se o código a abrir pelo nome");
        static string P(int v) => v == -1 ? Loc.T("ui.dcfCenter", "centro") : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var l = lines[0];
        return " · " + string.Format(Loc.T("ui.dcfIn", "{0}: posição na tela {1}, {2}; abre ao entrar: {3}"),
            string.Join(", ", lines.Select(x => x.Dcf).Distinct()), P(l.X), P(l.Y), l.Show ? Loc.T("ui.yes", "sim") : Loc.T("ui.no", "não"));
    }

    // grava direto no pacote (PckWriter); a cópia de trabalho na pasta .files, se houver, acompanha
    void Save()
    {
        if (_doc is null || _file is null || _pckPath is null) return;
        try
        {
            var bytes = _doc.ToBytes();
            if (File.Exists(FolderPath(_file))) AtomicFileSave.Write(FolderPath(_file), bytes);
            var bak = WritePack(new Dictionary<string, byte[]> { [_file] = bytes });
            _dirty = false; UpdateInfo();
            if (_textIndex is not null) _textIndex[_file] = string.Join("\n", UiTexts.Find(_doc.Xml).Values.Select(i => i.Text));
            MessageBox.Show(this, string.Format(Loc.T("ui.saved2", "Janela gravada no {0}.\nBackup do pacote anterior: {1}\n\nFeche e abra o jogo para ver a mudança."), Path.GetFileName(_pckPath), Path.GetFileName(bak)), Loc.T("tab.interface", "Interface"));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>
    /// Leva ao pacote o que está na pasta .files e é diferente do pacote (edições antigas, feitas antes de o editor gravar
    /// o .pck), agora com a mesma conferência do "Importar pasta".
    /// </summary>
    void PackFolder()
    {
        if (_pckPath is null || !AskDiscard()) return;
        AfterWrite(UiTools.Import(this, _pckPath, _pckPath + ".files"));
    }

    /// <summary>Mudanças não salvas: Salvar (grava e segue), Descartar (segue sem gravar) ou Cancelar (fica). true = pode seguir.</summary>
    bool AskDiscard()
    {
        if (!_dirty) return true;
        var save = new TaskDialogButton(Loc.T("ui.askSave", "Salvar"));
        var drop = new TaskDialogButton(Loc.T("ui.askDiscard", "Descartar"));
        var page = new TaskDialogPage
        {
            Caption = Loc.T("tab.interface", "Interface"), Icon = TaskDialogIcon.Warning,
            Heading = string.Format(Loc.T("ui.askHeading", "{0} tem mudanças não salvas"), Path.GetFileName(_file)),
            Text = Loc.T("ui.askText", "Salvar grava a janela no pacote (com backup). Descartar perde as mudanças desta janela."),
            Buttons = { save, drop, TaskDialogButton.Cancel }, DefaultButton = save,
        };
        var r = TaskDialog.ShowDialog(this, page);
        if (r == save) { Save(); return !_dirty; }   // falhou ao salvar: fica
        return r == drop;
    }

    // um pacote foi regravado por fora do editor: reabre tudo (o leitor antigo aponta para o backup) e a janela aberta
    void AfterWrite(bool changed)
    {
        if (!changed || _pckPath is null) return;
        var path = _pckPath; var file = _file;
        _pck?.Dispose(); _pck = null; _dirty = false;
        LoadPack(path);
        if (file is not null && AllDialogs().Contains(file, StringComparer.OrdinalIgnoreCase)) OpenDialog(file);
    }

    // o leitor é reaberto depois: o arquivo antigo virou o backup
    string WritePack(Dictionary<string, byte[]> files)
    {
        _pck?.Dispose(); _pck = null;
        try { return PckWriter.Write(_pckPath!, files); }
        finally { _pck = new PckArchive(_pckPath!); }
    }

    // ---------- edição ----------
    void Change(Action a)
    {
        if (_doc is null) return;
        _undo.Add(_doc.Xml); if (_undo.Count > 200) _undo.RemoveAt(0); _redo.Clear();
        a(); _dirty = true; UpdateInfo(); FillControls(); _canvas.Invalidate();
    }

    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);
    bool Swap(List<string> from, List<string> to)
    {
        if (_doc is null || from.Count == 0) return false;
        to.Add(_doc.Xml); _doc.Restore(from[^1]); from.RemoveAt(from.Count - 1);
        _dirty = true; FillControls(); ShowProps(); UpdateInfo(); _canvas.Invalidate();
        return true;
    }

    void Select(string? name) { _sel = name; ShowProps(); _canvas.Invalidate(); }

    void ShowProps()
    {
        _binding = true;
        try
        {
            var c = _sel is null ? null : _doc?.Dialog[_sel];
            foreach (var e in new Control[] { _x, _y, _w, _h, _text, _hint, _hide }) e.Enabled = c is not null;
            if (c is null) ShowFormat(null);
            if (_selBody is not null) { _selBody.Visible = c is not null; _noSel.Visible = c is null; }
            _selName.Text = c is null ? Loc.T("ui.pick", "Clique num controle da janela") : $"{c.Name}  ({c.Tag})";
            for (int i = 0; i < _ctlList.Items.Count; i++)
                if (_ctlList.Items[i] is CtlItem it && it.Name == _sel) { _ctlList.SelectedIndex = i; break; }
            if (c is null) { _ctlList.ClearSelected(); return; }
            _x.Value = Math.Clamp(c.Rect.X, -3000, 5000); _y.Value = Math.Clamp(c.Rect.Y, -3000, 5000);
            _w.Value = Math.Clamp(c.Rect.Width, -3000, 5000); _h.Value = Math.Clamp(c.Rect.Height, -3000, 5000);
            _text.Text = c.Text ?? ""; _text.Enabled = c.Text is not null;
            _hint.Text = c.Hint ?? ""; _hint.Enabled = c.Hint is not null;
            _hide.Enabled = c.Rect.X > -1000;
            ShowFormat(c);
            _imgs.Text = c.Images.Count == 0 ? "" : Loc.T("ui.images", "Imagens:") + "\n" + string.Join("\n", c.Images.Distinct().Take(6));
        }
        finally { _binding = false; }
    }

    void ApplyRect()
    {
        if (_binding || _sel is null) return;
        var r = new Rectangle((int)_x.Value, (int)_y.Value, (int)_w.Value, (int)_h.Value);
        Change(() => _doc!.SetRect(_sel, r));
    }

    void ApplyText()
    {
        if (_binding || _sel is null || _doc?.Dialog[_sel] is not { Text: { } old } || old == _text.Text) return;
        var t = _text.Text;
        Change(() => _doc!.SetText(_sel, t));
    }

    void ApplyFont(string attr, string value, bool onControl = false)
    {
        if (_binding || _sel is null || _doc?.Dialog[_sel] is not { } c) return;
        var name = _sel;
        Change(() => { if (onControl) _doc!.SetAttr(name, attr, value); else _doc!.SetTextAttr(name, attr, value); });
        ShowProps();
    }

    void ApplyHint()
    {
        if (_binding || _sel is null || _doc?.Dialog[_sel] is not { Hint: { } old } || old == _hint.Text) return;
        var t = _hint.Text;
        Change(() => _doc!.SetHint(_sel, t));
    }

    // =====================================================================================
    sealed class Canvas : Panel
    {
        readonly InterfaceView _v;
        float _s = 1; PointF _o;
        public float? Zoom;   // null = ajustar à tela
        public float Scale => _s;
        enum Drag { None, Move, Resize }
        Drag _drag; Point _start; Rectangle _orig, _live;
        TextBox? _inline;

        public Canvas(InterfaceView v) { _v = v; DoubleBuffered = true; BackColor = Color.FromArgb(24, 26, 32); TabStop = true; }

        GameDialog? D => _v._doc?.Dialog;
        Point ToGame(Point p) => new((int)Math.Floor((p.X - _o.X) / _s), (int)Math.Floor((p.Y - _o.Y) / _s));
        Rectangle ToScreen(Rectangle r) => Rectangle.Round(new RectangleF(_o.X + r.X * _s, _o.Y + r.Y * _s, r.Width * _s, r.Height * _s));

        static Rectangle Box(GameDialog.Control c) => GameDraw.Box(c);

        Rectangle RectOf(GameDialog.Control c) => _drag != Drag.None && c.Name == _v._sel ? _live : Box(c);

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (D is not { } d) { TextRenderer.DrawText(g, Loc.T("ui.choose", "Escolha uma janela na lista à esquerda."), Font, ClientRectangle, Color.Silver, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
            var size = d.Size.Width > 0 ? d.Size : new Size(400, 300);
            // ajustar: janela pequena cresce até 4x (antes ficava minúscula no meio da tela)
            _s = Zoom ?? Math.Min(4f, Math.Min((Width - 60f) / size.Width, (Height - 70f) / size.Height));
            _o = new PointF((Width - size.Width * _s) / 2, Math.Max(36, (Height - size.Height * _s) / 2));
            // nome e tamanho da janela em cima dela, e uma sombra para ela se destacar do fundo
            using (var fcap = new Font("Segoe UI", 9f))
                TextRenderer.DrawText(g, $"{System.IO.Path.GetFileName(_v._file)}  ·  {size.Width}×{size.Height}  ·  {_s * 100:0}%", fcap, new Point((int)_o.X, (int)_o.Y - 22), Color.FromArgb(160, 170, 185));
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(sh, _o.X + 6, _o.Y + 6, size.Width * _s, size.Height * _s);
            g.TranslateTransform(_o.X, _o.Y); g.ScaleTransform(_s, _s);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;

            GameDraw.Dialog(g, d, _v._tex, RectOf);
            // contornos e seleção
            using var pen = new Pen(Color.FromArgb(90, 120, 200, 255), 1 / _s);
            using var selPen = new Pen(Color.FromArgb(255, 255, 196, 0), 2 / _s) { DashStyle = DashStyle.Dash };
            foreach (var c in d.Controls.Values)
            {
                var r = RectOf(c);
                if (c.Name == _v._sel)
                {
                    g.DrawRectangle(selPen, r);
                    float hs = 8 / _s;
                    using var hb = new SolidBrush(Color.FromArgb(255, 196, 0));
                    g.FillRectangle(hb, r.Right - hs / 2, r.Bottom - hs / 2, hs, hs);
                }
                else if (_v._outlines.Checked && r.X > -1000) g.DrawRectangle(pen, r);
            }
        }

        GameDialog.Control? HitControl(Point game)
        {
            // o menor controle que contém o ponto (rótulos e botões vencem os fundos grandes)
            return D?.Controls.Values.Where(c => Box(c).Contains(game) && c.Rect.X > -1000)
                .OrderBy(c => (long)Box(c).Width * Box(c).Height).FirstOrDefault();
        }

        bool OnHandle(Point screen)
        {
            if (_v._sel is null || D?[_v._sel] is not { } c) return false;
            var r = ToScreen(Box(c));
            return Math.Abs(screen.X - r.Right) <= 6 && Math.Abs(screen.Y - r.Bottom) <= 6;
        }

        // Ctrl + roda = zoom
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) == 0) return;
            Zoom = Math.Clamp((Zoom ?? _s) * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.25f, 8f);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            if (e.Button != MouseButtons.Left || D is null) return;
            if (OnHandle(e.Location)) { _drag = Drag.Resize; }
            else
            {
                var hit = HitControl(ToGame(e.Location));
                _v.Select(hit?.Name);
                if (hit is null) return;
                _drag = Drag.Move;
            }
            _start = e.Location; _orig = _live = Box(D![_v._sel!]!);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _drag == Drag.Resize || OnHandle(e.Location) ? Cursors.SizeNWSE : _drag == Drag.Move ? Cursors.SizeAll : Cursors.Default;
            if (_drag == Drag.None) return;
            int dx = (int)Math.Round((e.X - _start.X) / _s), dy = (int)Math.Round((e.Y - _start.Y) / _s);
            _live = _drag == Drag.Move ? _orig with { X = _orig.X + dx, Y = _orig.Y + dy }
                : _orig with { Width = Math.Max(4, _orig.Width + dx), Height = Math.Max(4, _orig.Height + dy) };
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == Drag.None) return;
            var kind = _drag; _drag = Drag.None;
            if (_live == _orig || _v._sel is null) { Invalidate(); return; }
            var c = D![_v._sel]!;
            // rótulo sem tamanho no XML: move só a âncora, não inventa largura/altura
            var r = kind == Drag.Move ? c.Rect with { X = c.Rect.X + (_live.X - _orig.X), Y = c.Rect.Y + (_live.Y - _orig.Y) } : _live;
            var name = _v._sel;
            _v.Change(() => _v._doc!.SetRect(name, r));
            _v.ShowProps();
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_v._sel is null || D?[_v._sel] is not { } c) return;
            int step = e.Shift ? 10 : 1;
            var r = e.KeyCode switch
            {
                Keys.Left => c.Rect with { X = c.Rect.X - step }, Keys.Right => c.Rect with { X = c.Rect.X + step },
                Keys.Up => c.Rect with { Y = c.Rect.Y - step }, Keys.Down => c.Rect with { Y = c.Rect.Y + step },
                _ => c.Rect,
            };
            if (r == c.Rect) return;
            var name = _v._sel;
            _v.Change(() => _v._doc!.SetRect(name, r));
            _v.ShowProps(); e.Handled = true;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_v._sel is null || D?[_v._sel] is not { Text: { } t } c) return;
            _inline?.Dispose();
            var sr = ToScreen(Box(c));
            _inline = new TextBox { Bounds = new Rectangle(sr.X, sr.Y, Math.Max(sr.Width, 140), Math.Max(sr.Height, 22)), Text = t, BorderStyle = BorderStyle.FixedSingle };
            bool done = false;
            void Commit(bool ok)
            {
                if (done) return; done = true;
                var nt = _inline!.Text; var name = _v._sel!;
                var box = _inline; _inline = null; box.Dispose(); Focus();
                if (ok && nt != t) { _v.Change(() => _v._doc!.SetText(name, nt)); _v.ShowProps(); }
            }
            _inline.KeyDown += (_, k) => { if (k.KeyCode == Keys.Enter) { Commit(true); k.SuppressKeyPress = true; } else if (k.KeyCode == Keys.Escape) { Commit(false); k.SuppressKeyPress = true; } };
            _inline.LostFocus += (_, _) => Commit(true);
            Controls.Add(_inline); _inline.Focus(); _inline.SelectAll();
        }
    }
}

/// <summary>Desenho no estilo do jogo, em coordenadas da janela (respeita a escala da tela).</summary>
internal static class GameDraw
{
    /// <summary>Caixa clicável de um controle. Rótulo sem largura no XML: x é a âncora (Align 0 esquerda, 1 centro, 2 direita).</summary>
    public static Rectangle Box(GameDialog.Control c)
    {
        if (c.Rect.Width > 0) return c.Rect with { Height = Math.Max(c.Rect.Height, 16) };
        int w = 80;
        int x = c.Align switch { 1 => c.Rect.X - w / 2, 2 => c.Rect.X - w, _ => c.Rect.X };
        return new Rectangle(x, c.Rect.Y, w, Math.Max(c.Rect.Height, 16));
    }

    /// <summary>Janela inteira como no jogo (moldura + controles na ordem do XML).</summary>
    public static void Dialog(Graphics g, GameDialog d, GameTextures? tex, Func<GameDialog.Control, Rectangle> rectOf)
    {
        Frame(g, tex?.Get(d.Frame), new Rectangle(Point.Empty, d.Size));
        foreach (var c in d.Controls.Values)
        {
            var r = rectOf(c);
            if (r.X < -1000) continue;   // escondido fora da tela pelo próprio jogo
            var img = c.Images.Count > 0 ? tex?.Get(c.Images[0], c.Frames) : null;
            var col = c.Color is { A: > 0 } cc ? cc : Color.White;
            void T(string t, Rectangle area, ContentAlignment al) => Text(g, t, area, col, c.FontSize, c.Bold, al, c.FontName, noClip: true);
            var align = c.Align == 1 ? ContentAlignment.MiddleCenter : c.Align == 2 ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
            switch (c.Tag)
            {
                case "LABEL" or "TEXT" or "VERTICALTEXT":
                    // o jogo não corta o texto do rótulo: se a tradução é maior que a caixa, passa por cima (igual aqui)
                    if (c.Text is { Length: > 0 } t) T(t, c.Rect.Width == 0 ? r : r with { Height = Math.Max(c.Rect.Height, 14) }, align);
                    break;
                case "CHECK" or "RADIO":
                    var box = new Rectangle(r.X, r.Y + (r.Height - Math.Min(r.Height, 16)) / 2, Math.Min(r.Height, 16), Math.Min(r.Height, 16));
                    if (img is not null) Frame(g, img, c.Tag == "RADIO" ? r : box);
                    if (c.Text is { Length: > 0 } ct)
                        T(ct, c.Tag == "RADIO" ? Shift(r, c.TextOffset) : new Rectangle(r.X + 18 + c.TextOffset.X, r.Y + c.TextOffset.Y, Math.Max(10, r.Width - 18), r.Height),
                            c.Tag == "RADIO" ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft);
                    break;
                case "EDIT" or "LIST" or "TREE" or "COMBO" or "CONSOLE":
                    if (img is not null) Frame(g, img, r);
                    else using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(b, r);
                    if (c.Text is { Length: > 0 } et) T(et, Rectangle.Inflate(r, -3, 0), ContentAlignment.MiddleLeft);
                    break;
                default:   // IMAGEPICTURE, STILLIMAGEBUTTON, PROGRESS, SLIDER, SCROLL, WINDOWPICTURE, SUBDIALOG
                    if (img is not null) Frame(g, img, r);
                    // botão: texto centrado e deslocado por TextOffsetX/Y (ícone à esquerda, como nas abas da loja)
                    if (c.Text is { Length: > 0 } bt) T(bt, Shift(r, c.TextOffset), ContentAlignment.MiddleCenter);
                    break;
            }
        }
    }

    static Rectangle Shift(Rectangle r, Point o) => new(r.X + o.X, r.Y + o.Y, r.Width, r.Height);

    /// <summary>Diagnóstico: janela num bitmap (com ou sem contornos).</summary>
    public static Bitmap Render(GameDialog d, GameTextures? tex, bool outlines)
    {
        var size = d.Size.Width > 0 ? d.Size : new Size(400, 300);
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(24, 26, 32));
        g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
        Dialog(g, d, tex, Box);
        if (outlines)
        {
            using var pen = new Pen(Color.FromArgb(120, 120, 200, 255));
            foreach (var c in d.Controls.Values) if (c.Rect.X > -1000) g.DrawRectangle(pen, Box(c));
        }
        return bmp;
    }

    static readonly Color Outline = Color.FromArgb(10, 20, 30);
    static Font GameFont(float size, bool bold) => new("Microsoft YaHei UI", Math.Max(8, size * 1.05f), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>Moldura 9 partes (cantos fixos, bordas e miolo esticados) quando a textura é menor que o espaço.</summary>
    public static void Frame(Graphics g, Image? img, Rectangle r)
    {
        if (img is null || r.Width <= 0 || r.Height <= 0) return;
        if (img.Width >= r.Width && img.Height >= r.Height || img.Width < 6 || img.Height < 6) { g.DrawImage(img, r); return; }
        int cw = Math.Min(img.Width / 3, r.Width / 2), ch = Math.Min(img.Height / 3, r.Height / 2);
        int sx = img.Width - 2 * cw, sy = img.Height - 2 * ch;
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);   // sem costura nas bordas esticadas
        void P(int dx, int dy, int dw, int dh, int ix, int iy, int iw, int ih) { if (dw > 0 && dh > 0 && iw > 0 && ih > 0) g.DrawImage(img, new Rectangle(dx, dy, dw, dh), ix, iy, iw, ih, GraphicsUnit.Pixel, ia); }
        int mw = r.Width - 2 * cw, mh = r.Height - 2 * ch;
        P(r.X, r.Y, cw, ch, 0, 0, cw, ch); P(r.X + cw, r.Y, mw, ch, cw, 0, sx, ch); P(r.Right - cw, r.Y, cw, ch, img.Width - cw, 0, cw, ch);
        P(r.X, r.Y + ch, cw, mh, 0, ch, cw, sy); P(r.X + cw, r.Y + ch, mw, mh, cw, ch, sx, sy); P(r.Right - cw, r.Y + ch, cw, mh, img.Width - cw, ch, cw, sy);
        P(r.X, r.Bottom - ch, cw, ch, 0, img.Height - ch, cw, ch); P(r.X + cw, r.Bottom - ch, mw, ch, cw, img.Height - ch, sx, ch); P(r.Right - cw, r.Bottom - ch, cw, ch, img.Width - cw, img.Height - ch, cw, ch);
    }

    // tamanho do XML ~ altura da fonte em pixels: o cliente BR abreviou "C. Tudo" para caber em 48 px e manteve "Consertar"
    // (FontSize 10) no mesmo botão, o que só cabe com ~10 px. Texto com acento: as fontes chinesas do cliente não têm
    // á/í/ó/ú e o Windows completaria letra a letra com outra fonte (buracos); usa uma fonte só para a palavra toda.
    static Font GameFontFor(string? fontName, float size, bool bold, string? text = null)
    {
        float px = Math.Max(8, size * 1.05f);
        var fam = text is not null && text.Any(ch => ch is >= 'À' and <= 'ÿ') ? null : GameFonts.For(fontName);
        if (fam is null && text is not null && text.Any(ch => ch is >= 'À' and <= 'ÿ'))
            return new Font("Arial", px * 0.95f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        if (fam is not null)
            try { return new Font(fam, px, bold && fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
        return GameFont(size, bold);
    }

    public static void Text(Graphics g, string t, Rectangle r, Color c, float size, bool bold = false, ContentAlignment align = ContentAlignment.MiddleCenter,
        string? fontName = null, bool noClip = false)
    {
        var f = GameFontFor(fontName, size, bold, t);
        // sem noClip (telas montadas pelo editor, ex. loja com nomes do arquivo): diminui até caber
        if (!noClip && r.Width > 0)
            for (float k = 0.95f; k >= 0.7f && g.MeasureString(t, f).Width > r.Width + 2; k -= 0.05f) { f.Dispose(); f = GameFontFor(fontName, size * k, bold, t); }
        using var fontScope = f;
        using var sf = new StringFormat(StringFormatFlags.NoWrap | (noClip ? StringFormatFlags.NoClip : 0))
        {
            Trimming = noClip ? StringTrimming.None : StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center,
            Alignment = align switch { ContentAlignment.MiddleLeft => StringAlignment.Near, ContentAlignment.MiddleRight => StringAlignment.Far, _ => StringAlignment.Center },
        };
        var rf = r.Width > 0 ? (RectangleF)r : new RectangleF(r.X - 150, r.Y, 300, Math.Max(r.Height, 18));
        if (rf.Height <= 0) rf.Height = 18;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var ob = new SolidBrush(Outline))
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                g.DrawString(t, f, ob, new RectangleF(rf.X + dx, rf.Y + dy, rf.Width, rf.Height), sf);
        using var b = new SolidBrush(c.A == 0 ? Color.White : c);
        g.DrawString(t, f, b, rf, sf);
    }
}
