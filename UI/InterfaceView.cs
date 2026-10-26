using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor das janelas do jogo, aberto pelo Extrator PCK em qualquer pacote que tenha janelas XML (interfaces.pck...),
/// com a tela desenhada como no jogo (texturas do surfaces.pck e fontes do cliente). Clique seleciona um controle;
/// arrastar move; o quadradinho do canto redimensiona; setas movem 1 px (Shift = 10); duplo clique edita o texto.
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
    readonly ListBox _files = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Padding = new Padding(4, 4, 0, 0) };
    readonly FlowLayoutPanel _bar = new() { Dock = DockStyle.Top, Height = 38, WrapContents = false, Padding = new Padding(2, 4, 0, 0) };
    readonly CheckBox _outlines = new() { AutoSize = true, Checked = true, Padding = new Padding(8, 6, 0, 0) };
    readonly Canvas _canvas;
    readonly Panel _props = new() { Dock = DockStyle.Right, Width = 300, BackColor = Color.White, Padding = new Padding(10) };
    readonly Label _selName = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 10), MaximumSize = new Size(270, 0) };
    readonly NumericUpDown _x = N(), _y = N(), _w = N(), _h = N();
    readonly TextBox _text = new() { Multiline = true, Height = 60 };
    readonly Label _imgs = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(270, 0) };
    static NumericUpDown N() => new() { Minimum = -3000, Maximum = 5000, Width = 90 };

    UiDocument? _doc;
    string? _file;          // caminho dentro do pacote, ex.: interfaces\version01\qshop.xml
    bool _fromFolder, _dirty, _binding;
    string? _sel;
    readonly List<string> _undo = new(), _redo = new();

    public InterfaceView(Action back)
    {
        _back = back;
        BackColor = Bg; Dock = DockStyle.Fill;
        _canvas = new Canvas(this) { Dock = DockStyle.Fill };
        var left = new Panel { Dock = DockStyle.Left, Width = 280, BackColor = Color.White };
        left.Controls.Add(_files); left.Controls.Add(_filter);
        BuildBar(); BuildProps();
        Controls.Add(_canvas); Controls.Add(_props); Controls.Add(left); Controls.Add(_info); Controls.Add(_bar);
        _filter.TextChanged += (_, _) => FillList();
        _files.SelectedIndexChanged += (_, _) => { if (_files.SelectedItem is DialogItem f) OpenDialog(f.Path); };
        AppLang.Changed += Texts; Texts();
    }

    void Texts()
    {
        _filter.PlaceholderText = Loc.T("ui.filter", "Filtrar janelas (ex.: shop, inventory)");
        _outlines.Text = Loc.T("ui.outlines", "Mostrar contornos");
    }

    void BuildBar()
    {
        Button B(string key, string pt, Action a, int w = 120)
        {
            var b = new Button { Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Fg };
            b.Click += (_, _) => a();
            b.Text = Loc.T(key, pt); AppLang.Changed += () => b.Text = Loc.T(key, pt);
            _bar.Controls.Add(b);
            return b;
        }
        B("ui.back", "◀ Voltar ao extrator", () => { if (!_dirty || MessageBox.Show(this, Loc.T("ui.discard", "A janela atual tem mudanças não salvas. Descartar?"), "Interface", MessageBoxButtons.YesNo) == DialogResult.Yes) { _dirty = false; _back(); } }, 150);
        var save = B("ui.save", "Salvar janela", Save);
        save.BackColor = Accent; save.ForeColor = Color.White;
        B("ui.undo", "↶ Desfazer", () => Undo(), 100);
        B("ui.redo", "↷ Refazer", () => Redo(), 100);
        B("ui.packFolder", "Gravar pasta .files no pacote", PackFolder, 200);
        B("ui.openFolder", "Abrir pasta .files", () => { if (_pckPath is not null) { var d = _pckPath + ".files"; Directory.CreateDirectory(d); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d) { UseShellExecute = true }); } }, 140);
        _bar.Controls.Add(_outlines);
        _outlines.CheckedChanged += (_, _) => _canvas.Invalidate();
    }

    void BuildProps()
    {
        var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tl.Controls.Add(_selName); tl.SetColumnSpan(_selName, 2);
        void Row(string key, string pt, Control c) { var l = new Label { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0), Text = Loc.T(key, pt) }; AppLang.Changed += () => l.Text = Loc.T(key, pt); tl.Controls.Add(l); tl.Controls.Add(c); }
        Row("ui.x", "X", _x); Row("ui.y", "Y", _y); Row("ui.w", "Largura", _w); Row("ui.h", "Altura", _h);
        Row("ui.text", "Texto", _text); _text.Dock = DockStyle.Fill;
        tl.Controls.Add(_imgs); tl.SetColumnSpan(_imgs, 2);
        _props.Controls.Add(tl);
        foreach (var n in new[] { _x, _y, _w, _h }) n.ValueChanged += (_, _) => ApplyRect();
        _text.Leave += (_, _) => ApplyText();
        _text.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { ApplyText(); e.SuppressKeyPress = true; } };
    }

    // ---------- arquivos ----------
    /// <summary>Quantas janelas XML o pacote tem (o extrator mostra o botão só quando há).</summary>
    public static int CountDialogs(PckArchive pck) => pck.Entries.Count(e => e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

    /// <summary>Abre um pacote com janelas. As texturas vêm do surfaces.pck e as fontes da pasta do jogo (mesma pasta do pacote).</summary>
    public void LoadPack(string pckPath)
    {
        if (_pckPath == pckPath && _pck is not null) return;
        _pck?.Dispose(); _surfaces?.Dispose(); _tex?.Dispose();
        _pckPath = pckPath; _doc = null; _file = null; _sel = null; _dirty = false;
        var dir = Path.GetDirectoryName(pckPath)!;
        try
        {
            _pck = new PckArchive(pckPath);   // leitura própria (o extrator pode estar lendo o dele)
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

    // Item da lista: as janelas de version01 (as que o jogo usa desde a 1.4.4) primeiro; as da raiz do
    // interfaces.pck são a interface antiga e aparecem marcadas (o jogo não lê, por isso parecem "quebradas").
    sealed record DialogItem(string Path, string Label) { public override string ToString() => Label; }

    static bool IsLegacy(string path) => path.StartsWith(@"interfaces\", StringComparison.OrdinalIgnoreCase)
        && !path.Contains(@"\version", StringComparison.OrdinalIgnoreCase);

    void FillList()
    {
        var q = _filter.Text.Trim();
        _files.BeginUpdate(); _files.Items.Clear();
        foreach (var f in AllDialogs().Where(f => q.Length == 0 || f.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(IsLegacy).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            _files.Items.Add(new DialogItem(f, IsLegacy(f) ? f + "   " + Loc.T("ui.legacy", "(antiga, o jogo não usa)") : f));
        _files.EndUpdate();
        _info.Text = string.Format(Loc.T("ui.count2", "{0} janelas · edite na tela; \"Salvar janela\" grava direto no {1} (com backup)."),
            _files.Items.Count, Path.GetFileName(_pckPath));
    }

    // convenção do sPCK: <pacote>.files\<caminho completo dentro do pacote>
    string FolderPath(string file) => Path.Combine(_pckPath! + ".files", file);

    void OpenDialog(string file)
    {
        if (_dirty && file != _file && MessageBox.Show(this, Loc.T("ui.discard", "A janela atual tem mudanças não salvas. Descartar?"), "Interface", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            byte[]? bytes = null;
            // a cópia de trabalho (pasta .files) vale mais que o pacote
            _fromFolder = _pckPath is not null && File.Exists(FolderPath(file));
            if (_fromFolder) bytes = File.ReadAllBytes(FolderPath(file));
            else if (_pck?.Find(file) is { } e) bytes = _pck.Read(e);
            if (bytes is null) return;
            var doc = new UiDocument(bytes);
            if (doc.Dialog.Controls.Count == 0 && doc.Dialog.Size.Width == 0)
            { _info.Text = string.Format(Loc.T("ui.notDialog", "{0} não é uma janela do jogo (sem <DIALOG>)."), file); return; }
            _doc = doc; _file = file; _sel = null; _dirty = false;
            _undo.Clear(); _redo.Clear();
            ShowProps(); _canvas.Invalidate(); UpdateInfo();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Interface", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Diagnóstico (--snapshot ... ui:qshop.xml): abre a janela e seleciona o 1º botão.</summary>
    internal void OpenForSnapshot(string file)
    {
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
            + (IsLegacy(_file) ? Loc.T("ui.legacyInfo", "INTERFACE ANTIGA: o jogo usa a cópia em version01") + " · " : "")
            + (_fromFolder ? Loc.T("ui.fromFolder", "cópia de trabalho (pasta .files)") : string.Format(Loc.T("ui.fromPck", "lido do {0}"), Path.GetFileName(_pckPath)))
            + (_dirty ? " · " + Loc.T("ui.unsaved", "não salvo") : "");
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
            MessageBox.Show(this, string.Format(Loc.T("ui.saved2", "Janela gravada no {0}.\nBackup do pacote anterior: {1}\n\nFeche e abra o jogo para ver a mudança."), Path.GetFileName(_pckPath), Path.GetFileName(bak)), "Interface");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Leva ao pacote o que está na pasta .files e é diferente do pacote (edições antigas, feitas antes de o editor gravar o .pck).</summary>
    void PackFolder()
    {
        if (_pckPath is null || _pck is null) return;
        var dir = _pckPath + ".files";
        var changed = new Dictionary<string, byte[]>();
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dir, f);
                var bytes = File.ReadAllBytes(f);
                if (_pck.Find(rel) is { } e && _pck.Read(e).AsSpan().SequenceEqual(bytes)) continue;
                changed[rel] = bytes;
            }
        if (changed.Count == 0) { MessageBox.Show(this, Loc.T("ui.packNone", "A pasta .files não tem nada diferente do pacote."), "Interface"); return; }
        if (MessageBox.Show(this, string.Format(Loc.T("ui.packAsk", "Gravar no {0} estes {1} arquivo(s) da pasta .files?\n\n{2}\n\nO pacote atual fica como backup."),
                Path.GetFileName(_pckPath), changed.Count, string.Join("\n", changed.Keys.Take(20))), "Interface", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            var bak = WritePack(changed);
            FillList();
            MessageBox.Show(this, string.Format(Loc.T("ui.saved2", "Janela gravada no {0}.\nBackup do pacote anterior: {1}\n\nFeche e abra o jogo para ver a mudança."), Path.GetFileName(_pckPath), Path.GetFileName(bak)), "Interface");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
        a(); _dirty = true; UpdateInfo(); _canvas.Invalidate();
    }

    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);
    bool Swap(List<string> from, List<string> to)
    {
        if (_doc is null || from.Count == 0) return false;
        to.Add(_doc.Xml); _doc.Restore(from[^1]); from.RemoveAt(from.Count - 1);
        _dirty = true; ShowProps(); UpdateInfo(); _canvas.Invalidate();
        return true;
    }

    void Select(string? name) { _sel = name; ShowProps(); _canvas.Invalidate(); }

    void ShowProps()
    {
        _binding = true;
        try
        {
            var c = _sel is null ? null : _doc?.Dialog[_sel];
            _props.Enabled = c is not null;
            _selName.Text = c is null ? Loc.T("ui.pick", "Clique num controle da janela") : $"{c.Name}  ({c.Tag})";
            if (c is null) return;
            _x.Value = Math.Clamp(c.Rect.X, -3000, 5000); _y.Value = Math.Clamp(c.Rect.Y, -3000, 5000);
            _w.Value = Math.Clamp(c.Rect.Width, -3000, 5000); _h.Value = Math.Clamp(c.Rect.Height, -3000, 5000);
            _text.Text = c.Text ?? ""; _text.Enabled = c.Text is not null;
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

    // =====================================================================================
    sealed class Canvas : Panel
    {
        readonly InterfaceView _v;
        float _s = 1; PointF _o;
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
            _s = Math.Min(2f, Math.Min((Width - 20f) / size.Width, (Height - 20f) / size.Height));
            _o = new PointF((Width - size.Width * _s) / 2, (Height - size.Height * _s) / 2);
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
