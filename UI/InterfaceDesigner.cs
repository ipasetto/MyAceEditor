using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Tela "Criar interface" (usuário 02/11, "como o Canva"): à esquerda a barra de ícones abre um painel com o que dá para
/// pôr (modelos = janelas do jogo, elementos tirados das janelas do jogo com as imagens reais, textos, molduras, camadas);
/// no meio a janela desenhada como no jogo, onde se arrasta, estica e alinha (guias); em cima uma barra que muda conforme
/// o que está selecionado. "Vincular ao jogo" grava por cima de uma janela existente depois de conferir o contrato dela
/// (<see cref="UiDesign.Compare"/>; PW-KB §S20b); "Exportar .zip" leva a janela com o aviso de que ela precisa ser vinculada.
/// </summary>
internal sealed class InterfaceDesigner : UserControl
{
    static readonly Color Bg = Color.FromArgb(229, 231, 235), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128),
        RailBg = Color.FromArgb(24, 32, 44), RailHover = Color.FromArgb(32, 43, 59), RailSel = Color.FromArgb(40, 54, 74), RailFg = Color.FromArgb(170, 182, 198),
        SelColor = Color.FromArgb(139, 92, 246), Guide = Color.FromArgb(236, 72, 153), ThumbBg = Color.FromArgb(38, 42, 52);
    const string LinkGlyph = "", TextGlyph = "", TemplateGlyph = "", UpGlyph = "", DownGlyph = "";

    readonly Action _back;
    string _xml = UiDesign.NewXml("Win_MyWindow", new Size(360, 260), null);
    GameDialog _dlg;
    string? _sel, _projPath, _pckPath, _lastKey;
    DateTime _lastAt;
    bool _dirty, _binding;
    readonly List<(string Xml, string? Sel)> _undo = new(), _redo = new();
    PckArchive? _pck, _surf;
    GameTextures? _tex;

    /// <summary>Peça para pôr na janela: categoria, nome mostrado, trecho XML e de qual janela veio.</summary>
    sealed record Sample(string Cat, string Label, string Block, string From);
    List<Sample> _lib = new();
    List<string> _windows = new(), _frames = new();
    bool _scanning;
    readonly Dictionary<string, Bitmap?> _thumbs = new();

    readonly Rail _rail;
    readonly Panel _fly = new() { Dock = DockStyle.Left, Width = 292, BackColor = Color.White, Visible = false, Padding = new Padding(12, 10, 8, 8) };
    readonly Label _flyTitle = new() { Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Fg };
    readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "" };
    readonly FlowLayoutPanel _tiles = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 8) };
    readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34 };
    readonly FlowLayoutPanel _layerButtons = new() { Dock = DockStyle.Bottom, Height = 40, Visible = false };
    // quebra em mais linhas quando não cabe (janela estreita): nada fica escondido
    readonly FlowLayoutPanel _ctx = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 46), BackColor = Color.White, Padding = new Padding(10, 7, 10, 4), WrapContents = true };
    readonly DesignCanvas _canvas;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, ForeColor = Muted, BackColor = Color.White, Padding = new Padding(10, 5, 0, 0) };
    readonly ToolTip _tips = new();
    int _flyKind = -1;

    /// <summary>Itens da barra de ícones: glifo, chave, texto pt, dica.</summary>
    static readonly (string Glyph, string Key, string Pt, string TipKey, string TipPt)[] RailItems =
    {
        (TemplateGlyph, "uiD.tTemplates", "Modelos", "uiD.tTemplatesTip", "Começar de uma janela do jogo (ela vem inteira, com tudo vinculado)."),
        (UiKit.Box, "uiD.tElements", "Elementos", "uiD.tElementsTip", "Botões, campos, listas, caixas de marcar, barras e imagens tirados das janelas do jogo."),
        (TextGlyph, "uiD.tText", "Texto", "uiD.tTextTip", "Títulos e textos com a fonte do jogo."),
        (UiKit.Picture, "uiD.tFrames", "Molduras", "uiD.tFramesTip", "O fundo da janela (as molduras das janelas do jogo)."),
        (UiKit.Layers, "uiD.tLayers", "Camadas", "uiD.tLayersTip", "Tudo o que está na janela, de cima para baixo; escolha para selecionar."),
    };

    public InterfaceDesigner(Action back)
    {
        _back = back;
        _dlg = GameDialog.Parse(_xml);
        BackColor = Bg; Font = new Font("Segoe UI", 9f);

        var top = UiKit.Ribbon();
        UiKit.Cmd(top, UiKit.Back, "uiD.rBack", "Voltar", () => { if (AskDiscard()) _back(); }, "uiD.rBackTip", "Volta para as interfaces.");
        UiKit.Sep(top);
        UiKit.Cmd(top, UiKit.Add, "uiD.rNew", "Nova", NewDesign, "uiD.rNewTip", "Começa uma janela em branco.");
        UiKit.Cmd(top, UiKit.OpenFile, "uiD.rOpen", "Abrir", OpenDesign, "uiD.rOpenTip", "Abre uma janela criada antes (.xml ou .zip exportado).");
        UiKit.Cmd(top, UiKit.Save, "uiD.rSave", "Salvar", () => SaveDesign(false), "uiD.rSaveTip", "Grava a janela criada num arquivo .xml no seu computador (não mexe no jogo).");
        UiKit.Cmd(top, UiKit.Export, "uiD.rZip", "Exportar .zip", ExportZip, "uiD.rZipTip", "Leva a janela num .zip (com um LEIA-ME de como usar no jogo).");
        UiKit.Sep(top);
        UiKit.Cmd(top, LinkGlyph, "uiD.rLink", "Vincular ao jogo", LinkToGame, "uiD.rLinkTip", "Grava a janela por cima de uma janela do jogo, depois de conferir se ela tem tudo o que o jogo procura.", primary: true);
        UiKit.Sep(top);
        UiKit.Cmd(top, UiKit.Undo, "uiD.rUndo", "Desfazer", () => Undo(), "uiD.rUndoTip", "Ctrl+Z");
        UiKit.Cmd(top, UiKit.Redo, "uiD.rRedo", "Refazer", () => Redo(), "uiD.rRedoTip", "Ctrl+Y");
        UiKit.Cmd(top, UiKit.Fit, "uiD.rFit", "Caber na tela", () => { _canvas!.Zoom = null; _canvas.Invalidate(); }, "uiD.rFitTip", "Mostra a janela inteira (Ctrl + roda do mouse muda o zoom).");

        _rail = new Rail(this) { Dock = DockStyle.Left, Width = 78 };
        _canvas = new DesignCanvas(this) { Dock = DockStyle.Fill };

        _search.TextChanged += (_, _) => FillFly();
        _list.DrawItem += DrawListItem;
        _list.SelectedIndexChanged += (_, _) => { if (_flyKind == 4) ListPicked(); };   // camadas: escolher = selecionar
        _list.MouseClick += (_, _) => { if (_flyKind == 0) ListPicked(); };              // modelos: só no clique (setas não trocam a janela)
        var up = SmallButton(UpGlyph, "uiD.layerUp", "Para frente", () => Layer(+1));
        var down = SmallButton(DownGlyph, "uiD.layerDown", "Para trás", () => Layer(-1));
        _layerButtons.Controls.AddRange(new Control[] { up, down });
        _fly.Controls.Add(_tiles); _fly.Controls.Add(_list); _fly.Controls.Add(_layerButtons); _fly.Controls.Add(_search); _fly.Controls.Add(_flyTitle);

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_canvas); center.Controls.Add(_ctx); center.Controls.Add(_status);
        Controls.Add(center); Controls.Add(_fly); Controls.Add(_rail); Controls.Add(top);

        AppLang.Changed += Texts;
        Texts();
        BuildContext();
    }

    void Texts()
    {
        _search.PlaceholderText = Loc.T("uiD.search", "Buscar...");
        _status.Text = Loc.T("uiD.hint", "Clique ou arraste do painel da esquerda para pôr na janela. Arraste para mover, puxe os quadradinhos para esticar. Setas: 1 px (Shift: 10) · Ctrl+D duplica · Delete apaga.");
        if (_flyKind >= 0) _flyTitle.Text = Loc.T(RailItems[_flyKind].Key, RailItems[_flyKind].Pt);
        _rail.Invalidate();
        BuildContext();
    }

    // ---------- pacote do jogo ----------

    /// <summary>Abre o interfaces.pck do jogo (modelos, elementos e molduras) e o surfaces.pck (imagens).</summary>
    public void Open(string pckPath)
    {
        if (_pckPath == pckPath && _pck is not null) { _canvas.Invalidate(); return; }
        ClosePacks();
        _pckPath = pckPath;
        var dir = Path.GetDirectoryName(pckPath)!;
        try
        {
            _pck = new PckArchive(pckPath);
            var sp = Path.Combine(dir, "surfaces.pck");
            _surf = File.Exists(sp) ? new PckArchive(sp) : null;
            _tex = _surf is null ? null : new GameTextures(_surf);
            GameFonts.Load(dir);
        }
        catch (Exception ex) { Status("⚠ " + ex.Message); return; }
        foreach (var t in _thumbs.Values) t?.Dispose();
        _thumbs.Clear();
        Scan();
        _canvas.Invalidate();
    }

    void ClosePacks() { _tex?.Dispose(); _surf?.Dispose(); _pck?.Dispose(); _tex = null; _surf = null; _pck = null; }

    // lê todas as janelas numa leitura própria, fora da tela: modelos, molduras e uma amostra de cada aparência de controle
    void Scan()
    {
        if (_pckPath is null) return;
        var path = _pckPath;
        _scanning = true; FillFly();
        Task.Run(() =>
        {
            var (lib, wins, frames) = ScanCore(path);
            BeginInvoke(() => { _lib = lib; _windows = wins; _frames = frames; _scanning = false; FillFly(); });
        });
    }

    static (List<Sample>, List<string>, List<string>) ScanCore(string path)
    {
        {
            var lib = new List<Sample>(); var wins = new List<string>(); var frames = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perCat = new Dictionary<string, int>();
            try
            {
                using var pck = new PckArchive(path);
                // interface nova (version01) primeiro: é a que o jogo usa por padrão (PW-KB §S20b)
                var xmls = pck.Entries.Where(e => e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.Path.Contains("version01", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var e in xmls)
                {
                    string x; GameDialog d;
                    try { x = UiDesign.FromBytes(pck.Read(e)); d = GameDialog.Parse(x); } catch (Exception) { continue; }
                    if (d.Name.Length == 0) continue;
                    wins.Add(e.Path);
                    if (d.Frame is { Length: > 0 } f && seen.Add("frame|" + f) && frames.Count < 120) frames.Add(f);
                    foreach (var p in UiDesign.Pieces(x))
                    {
                        if (d[p.Name] is not { } c || c.Images.Count == 0) continue;
                        var cat = c.Tag switch
                        {
                            "STILLIMAGEBUTTON" => "btn", "EDIT" => "edit", "LIST" or "COMBO" or "TREE" => "list", "CHECK" or "RADIO" => "check",
                            "PROGRESS" or "SLIDER" or "SCROLL" => "bar", "IMAGEPICTURE" => "img", _ => null,
                        };
                        if (cat is null || c.Rect.Width < 6 || c.Rect.Height < 6 || c.Rect.Width > 700 || c.Rect.Height > 500) continue;
                        if (!seen.Add(cat + "|" + c.Images[0])) continue;
                        if (perCat.GetValueOrDefault(cat) >= 80) continue;
                        perCat[cat] = perCat.GetValueOrDefault(cat) + 1;
                        var from = Path.GetFileNameWithoutExtension(e.Path);
                        lib.Add(new Sample(cat, c.Text is { Length: > 0 } t ? t : from, x.Substring(p.Start, p.Length), e.Path));
                    }
                }
            }
            catch (Exception) { }
            return (lib, wins, frames);
        }
    }

    /// <summary>Diagnóstico (--snapshot ... uicreate[:janela]): lê as janelas na hora, abre o modelo e o painel Elementos.</summary>
    internal void SnapshotDemo(string? template)
    {
        if (_pckPath is null) return;
        (_lib, _windows, _frames) = ScanCore(_pckPath);
        _scanning = false;
        if (template is { Length: > 0 } && _windows.FirstOrDefault(w => w.Contains("version01", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(w).Equals(template, StringComparison.OrdinalIgnoreCase)) is { } w && _pck?.Find(w) is { } e)
        {
            _xml = UiDesign.FromBytes(_pck.Read(e)); Reparse();
            Select(_dlg.Controls.Values.FirstOrDefault(c => c.Tag == "STILLIMAGEBUTTON")?.Name);
        }
        ShowFly(1);
    }

    // ---------- desfazer ----------

    void PushUndo(string? key = null)
    {
        var now = DateTime.Now;
        // a mesma mudança repetida (digitar, girar o número) vira um passo só
        if (key is not null && key == _lastKey && (now - _lastAt).TotalSeconds < 1.5) { _lastAt = now; return; }
        _undo.Add((_xml, _sel)); if (_undo.Count > 300) _undo.RemoveAt(0);
        _redo.Clear(); _lastKey = key; _lastAt = now;
    }

    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);

    bool Swap(List<(string, string?)> from, List<(string, string?)> to)
    {
        if (from.Count == 0) return false;
        to.Add((_xml, _sel));
        (_xml, _sel) = from[^1]; from.RemoveAt(from.Count - 1);
        _lastKey = null; _dirty = true;
        Reparse(); Select(_sel);
        return true;
    }

    void Reparse()
    {
        _dlg = GameDialog.Parse(_xml);
        if (_sel is not null && _dlg[_sel] is null) _sel = null;
        _canvas.Invalidate();
        if (_flyKind == 4) FillFly();
    }

    /// <summary>Muda o XML num passo do desfazer (<paramref name="key"/> junta mudanças seguidas do mesmo campo).</summary>
    void Change(Func<string, string> f, string? key = null)
    {
        var nx = f(_xml);
        if (nx == _xml) return;
        PushUndo(key);
        _xml = nx; _dirty = true;
        Reparse();
    }

    /// <summary>Muda um controle pelo UiDocument (posição, texto, fonte).</summary>
    void ChangeDoc(Func<UiDocument, bool> f, string? key = null) => Change(x => { var d = UiDesign.Doc(x); return f(d) ? d.Xml : x; }, key);

    // ---------- seleção e barra de cima ----------

    GameDialog.Control? SelCtl => _sel is null ? null : _dlg[_sel];

    void Select(string? name)
    {
        _sel = name is not null && _dlg[name] is not null ? name : null;
        _canvas.Invalidate();
        BuildContext();
        if (_flyKind == 4) SyncLayerSelection();
    }

    void BuildContext()
    {
        _binding = true;
        _ctx.SuspendLayout();
        foreach (Control c in _ctx.Controls) c.Dispose();
        _ctx.Controls.Clear();
        if (SelCtl is not { } c0)
        {
            Caption("uiD.window", "Janela");
            var name = Box(UiDesign.WindowName(_xml), 150);
            name.Leave += (_, _) => RenameWindow(name.Text);
            name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { RenameWindow(name.Text); e.SuppressKeyPress = true; } };
            _tips.SetToolTip(name, Loc.T("uiD.windowNameTip", "Nome da janela no jogo (Win_...). Ao vincular, vira o nome da janela original."));
            Caption("uiD.size", "Tamanho");
            var w = Num(_dlg.Size.Width, 60, 2000); var h = Num(_dlg.Size.Height, 40, 2000);
            w.ValueChanged += (_, _) => { if (!_binding) Change(x => UiDesign.SetDialog(x, size: new Size((int)w.Value, (int)h.Value)), "size"); };
            h.ValueChanged += (_, _) => { if (!_binding) Change(x => UiDesign.SetDialog(x, size: new Size((int)w.Value, (int)h.Value)), "size"); };
            Btn(UiKit.Picture, "uiD.frame", "Moldura", () => ShowFly(3));
            if (_dlg.Frame is not null) Btn(UiKit.Delete, "uiD.noFrame", "Tirar moldura", () => Change(x => UiDesign.SetDialog(x, frame: "")));
        }
        else
        {
            var c = c0;
            Caption("uiD.ctlName", "Nome");
            var name = Box(c.Name, 120);
            name.Leave += (_, _) => RenameControl(name.Text);
            name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { RenameControl(name.Text); e.SuppressKeyPress = true; } };
            _tips.SetToolTip(name, Loc.T("uiD.ctlNameTip", "O jogo acha os controles pelo nome: para funcionar no lugar de uma janela, use o mesmo nome do controle original."));
            if (HasText(c))
            {
                var text = Box(c.Text ?? "", 170);
                text.Name = "text";
                text.TextChanged += (_, _) => { if (!_binding && _sel is { } s) ChangeDoc(d => d.SetText(s, text.Text), "text:" + s); };
                var size = Num((decimal)Math.Clamp(c.FontSize, 6, 40), 6, 40);
                size.ValueChanged += (_, _) => { if (!_binding && _sel is { } s) ChangeDoc(d => d.SetTextAttr(s, "FontSize", ((int)size.Value).ToString()), "fs:" + s); };
                _tips.SetToolTip(size, Loc.T("uiD.fontSize", "Tamanho da letra"));
                var bold = new CheckBox { Text = Loc.T("uiD.bold", "Negrito"), Checked = c.Bold, AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
                bold.CheckedChanged += (_, _) => { if (!_binding && _sel is { } s) ChangeDoc(d => d.SetTextAttr(s, "Bold", bold.Checked ? "true" : "false")); };
                _ctx.Controls.Add(bold);
                Btn(UiKit.Paint, "uiD.color", "Cor", PickColor);
            }
            Caption("uiD.pos", "X Y L A");
            var r = c.Rect;
            var nx = Num(r.X, -2000, 4000); var ny = Num(r.Y, -2000, 4000); var nw = Num(r.Width, 0, 4000); var nh = Num(r.Height, 0, 4000);
            foreach (var n in new[] { nx, ny, nw, nh })
                n.ValueChanged += (_, _) => { if (!_binding && _sel is { } s) ChangeDoc(d => d.SetRect(s, new Rectangle((int)nx.Value, (int)ny.Value, (int)nw.Value, (int)nh.Value)), "rect:" + s); };
            Btn(UpGlyph, "uiD.layerUp", "Para frente", () => Layer(+1));
            Btn(DownGlyph, "uiD.layerDown", "Para trás", () => Layer(-1));
            Btn(UiKit.Copy, "uiD.dup", "Duplicar", Duplicate);
            Btn(UiKit.Delete, "uiD.del", "Excluir", DeleteSel);
        }
        _ctx.ResumeLayout();
        _binding = false;

        void Caption(string key, string pt) => _ctx.Controls.Add(new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Muted, Margin = new Padding(6, 8, 4, 0) });
        TextBox Box(string v, int w) { var t = new TextBox { Text = v, Width = w, Margin = new Padding(0, 4, 4, 0) }; _ctx.Controls.Add(t); return t; }
        NumericUpDown Num(decimal v, int min, int max) { var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(v, min, max), Width = 58, Margin = new Padding(0, 4, 2, 0) }; _ctx.Controls.Add(n); return n; }
        void Btn(string glyph, string key, string pt, Action a)
        {
            var b = new Button { Text = Loc.T(key, pt), AutoSize = true, FlatStyle = FlatStyle.Flat, Image = UiKit.Icon(glyph, Fg, 16), TextImageRelation = TextImageRelation.ImageBeforeText,
                Height = 30, Margin = new Padding(4, 2, 0, 0), Cursor = Cursors.Hand, BackColor = Color.White };
            b.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            b.Click += (_, _) => a();
            _ctx.Controls.Add(b);
        }
    }

    static bool HasText(GameDialog.Control c) => c.Tag is "LABEL" or "TEXT" or "STILLIMAGEBUTTON" or "CHECK" or "RADIO" || c.Text is not null;

    void RenameWindow(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == UiDesign.WindowName(_xml)) return;
        Change(x => UiDesign.SetDialog(x, name: name));
    }

    void RenameControl(string name)
    {
        name = name.Trim();
        if (_sel is not { } old || name.Length == 0 || name == old) return;
        var free = UiDesign.FreeName(_xml, name);
        if (free != name) Status("⚠ " + string.Format(Loc.T("uiD.nameUsed", "Já existe um controle {0}; ficou {1}."), name, free));
        Change(x => ReplaceBlock(x, old, UiDesign.Rename(UiDesign.Block(x, old), free)));
        Select(free);
    }

    static string ReplaceBlock(string xml, string name, string block) =>
        UiDesign.Pieces(xml).FirstOrDefault(p => p.Name == name) is { } p ? xml.Remove(p.Start, p.Length).Insert(p.Start, block) : xml;

    void PickColor()
    {
        if (SelCtl is not { } c) return;
        using var dlg = new ColorDialog { Color = c.Color ?? Color.White, FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var k = dlg.Color;
        ChangeDoc(d => d.SetTextAttr(c.Name, "Color", $"{k.R},{k.G},{k.B},255"));
    }

    void Layer(int d)
    {
        if (_sel is not { } s) return;
        var ps = UiDesign.Pieces(_xml);
        int i = ps.FindIndex(p => p.Name == s);
        int j = i + d;
        if (i < 0 || j < 0 || j >= ps.Count) return;
        // Insert no índice j depois de tirar o controle: para frente = depois do vizinho de cima
        Change(x => UiDesign.Reorder(x, s, j));
        Select(s);
    }

    void Duplicate()
    {
        if (_sel is not { } s || SelCtl is not { } c) return;
        var name = UiDesign.FreeName(_xml, s);
        var ps = UiDesign.Pieces(_xml);
        int i = ps.FindIndex(p => p.Name == s);
        Change(x =>
        {
            var nx = UiDesign.Insert(x, UiDesign.Rename(UiDesign.Block(x, s), name), i + 1);
            var d = UiDesign.Doc(nx);
            d.SetRect(name, c.Rect with { X = c.Rect.X + 10, Y = c.Rect.Y + 10 });
            return d.Xml;
        });
        Select(name);
    }

    void DeleteSel()
    {
        if (_sel is not { } s) return;
        Change(x => UiDesign.Remove(x, s));
        Select(null);
    }

    void Nudge(int dx, int dy)
    {
        if (_sel is not { } s || SelCtl is not { } c) return;
        ChangeDoc(d => d.SetRect(s, c.Rect with { X = c.Rect.X + dx, Y = c.Rect.Y + dy }), "nudge:" + s);
        BuildContext();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool typing = FindFocused() is TextBoxBase or NumericUpDown;   // a caixa do NumericUpDown também é TextBoxBase
        switch (keyData)
        {
            case Keys.Control | Keys.Z when !typing: Undo(); return true;
            case Keys.Control | Keys.Y when !typing: Redo(); return true;
            case Keys.Control | Keys.D: Duplicate(); return true;
            case Keys.Delete when !typing: DeleteSel(); return true;
            case Keys.Left when !typing: Nudge(-1, 0); return true;
            case Keys.Right when !typing: Nudge(1, 0); return true;
            case Keys.Up when !typing: Nudge(0, -1); return true;
            case Keys.Down when !typing: Nudge(0, 1); return true;
            case Keys.Shift | Keys.Left when !typing: Nudge(-10, 0); return true;
            case Keys.Shift | Keys.Right when !typing: Nudge(10, 0); return true;
            case Keys.Shift | Keys.Up when !typing: Nudge(0, -10); return true;
            case Keys.Shift | Keys.Down when !typing: Nudge(0, 10); return true;
            case Keys.Escape when !typing: Select(null); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    Control? FindFocused()
    {
        Control? c = this;
        while (c is ContainerControl cc && cc.ActiveControl is { } a) c = a;
        return c;
    }

    void Status(string msg) => _status.Text = msg;

    // ---------- painel da esquerda ----------

    void ShowFly(int kind)
    {
        if (_flyKind == kind && _fly.Visible) { _fly.Visible = false; _flyKind = -1; _rail.Invalidate(); return; }
        _flyKind = kind;
        _flyTitle.Text = Loc.T(RailItems[kind].Key, RailItems[kind].Pt);
        _search.Text = "";
        _fly.Visible = true;
        _rail.Invalidate();
        FillFly();
    }

    void FillFly()
    {
        if (_flyKind < 0) return;
        bool listMode = _flyKind is 0 or 4;
        _list.Visible = listMode; _tiles.Visible = !listMode;
        _search.Visible = _flyKind is 0 or 1;
        _layerButtons.Visible = _flyKind == 4;
        var q = _search.Text.Trim();
        if (listMode)
        {
            _binding = true;
            _list.BeginUpdate(); _list.Items.Clear();
            if (_flyKind == 0)
            {
                _list.Items.Add(new ListRow("", Loc.T("uiD.blank", "Janela em branco"), Loc.T("uiD.blankSub", "começar do zero")));
                if (_pck is null) _list.Items.Add(new ListRow(null, Loc.T("uiD.noPack", "Abra a pasta do PW para ver as janelas do jogo."), ""));
                else if (_scanning) _list.Items.Add(new ListRow(null, Loc.T("uiD.loading", "Lendo as janelas do jogo..."), ""));
                foreach (var w in _windows)
                    if (q.Length == 0 || w.Contains(q, StringComparison.OrdinalIgnoreCase))
                        _list.Items.Add(new ListRow(w, Path.GetFileNameWithoutExtension(w), Path.GetDirectoryName(w) ?? ""));
            }
            else
            {
                // camadas: a de cima primeiro (último no arquivo = desenhado por cima)
                foreach (var p in Enumerable.Reverse(UiDesign.Pieces(_xml)))
                    _list.Items.Add(new ListRow(p.Name, p.Name, TagName(p.Tag)));
                SyncLayerSelection();
            }
            _list.EndUpdate();
            _binding = false;
            return;
        }
        _tiles.SuspendLayout();
        foreach (Control c in _tiles.Controls) c.Dispose();
        _tiles.Controls.Clear();
        switch (_flyKind)
        {
            case 1:
                if (_pck is null) { Note(Loc.T("uiD.noPack", "Abra a pasta do PW para ver as janelas do jogo.")); break; }
                if (_scanning) { Note(Loc.T("uiD.loading", "Lendo as janelas do jogo...")); break; }
                foreach (var (cat, key, pt) in new[] { ("btn", "uiD.cBtn", "Botões"), ("edit", "uiD.cEdit", "Campos de digitar"), ("list", "uiD.cList", "Listas"),
                    ("check", "uiD.cCheck", "Marcar / escolher"), ("bar", "uiD.cBar", "Barras"), ("img", "uiD.cImg", "Imagens e enfeites") })
                {
                    var items = _lib.Where(s => s.Cat == cat && (q.Length == 0 || s.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || s.From.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (items.Count == 0) continue;
                    Section(Loc.T(key, pt));
                    foreach (var s in items) _tiles.Controls.Add(new Tile(this, s.Block, s.Label, Path.GetFileNameWithoutExtension(s.From), () => AddPiece(s.Block, null)));
                }
                break;
            case 2:
                foreach (var (key, pt, size, bold, col, outline) in new[]
                {
                    ("uiD.title", "Título", 14f, true, Color.FromArgb(255, 251, 202), true),
                    ("uiD.body", "Texto", 10f, false, Color.White, false),
                    ("uiD.small", "Texto pequeno", 9f, false, Color.FromArgb(200, 200, 200), false),
                })
                {
                    var block = UiDesign.LabelBlock("Txt", Loc.T(key, pt), (int)(size * 12), (int)(size * 2), size, bold, col, outline);
                    _tiles.Controls.Add(new Tile(this, block, Loc.T(key, pt), "", () => AddPiece(block, null)) { Width = 240, Height = 64 });
                }
                break;
            case 3:
                if (_pck is null) { Note(Loc.T("uiD.noPack", "Abra a pasta do PW para ver as janelas do jogo.")); break; }
                if (_scanning) { Note(Loc.T("uiD.loading", "Lendo as janelas do jogo...")); break; }
                foreach (var f in _frames)
                    _tiles.Controls.Add(new Tile(this, null, Path.GetFileNameWithoutExtension(f), "", () => Change(x => UiDesign.SetDialog(x, frame: f))) { FramePath = f });
                break;
        }
        _tiles.ResumeLayout();

        void Note(string t) => _tiles.Controls.Add(new Label { Text = t, AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = Muted, Margin = new Padding(0, 8, 0, 0) });
        void Section(string t) => _tiles.Controls.Add(new Label { Text = t, AutoSize = false, Width = 240, Height = 24, Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Fg, Margin = new Padding(0, 8, 0, 2), TextAlign = ContentAlignment.BottomLeft });
    }

    static string TagName(string tag) => tag switch
    {
        "STILLIMAGEBUTTON" => Loc.T("uiD.kBtn", "Botão"), "LABEL" or "TEXT" => Loc.T("uiD.kText", "Texto"), "EDIT" => Loc.T("uiD.kEdit", "Campo"),
        "LIST" or "COMBO" or "TREE" => Loc.T("uiD.kList", "Lista"), "CHECK" or "RADIO" => Loc.T("uiD.kCheck", "Marcar"), "IMAGEPICTURE" => Loc.T("uiD.kImg", "Imagem"),
        "PROGRESS" or "SLIDER" or "SCROLL" => Loc.T("uiD.kBar", "Barra"), _ => tag,
    };

    sealed record ListRow(string? Key, string Title, string Sub) { public override string ToString() => Title; }

    void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not ListRow r) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Color.FromArgb(237, 233, 254) : Color.White)) e.Graphics.FillRectangle(b, e.Bounds);
        using var f1 = new Font("Segoe UI Semibold", 9.5f); using var f2 = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, r.Title, f1, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 1, e.Bounds.Width - 8, 18), r.Key is null ? Muted : Fg, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, r.Sub, f2, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 17, e.Bounds.Width - 8, 16), Muted, TextFormatFlags.EndEllipsis);
    }

    void ListPicked()
    {
        if (_binding || _list.SelectedItem is not ListRow { Key: { } key }) return;
        if (_flyKind == 4) { Select(key); return; }
        if (!AskDiscard()) return;
        if (key.Length == 0) { NewDesign(); return; }
        if (_pck?.Find(key) is not { } e) return;
        _xml = UiDesign.FromBytes(_pck.Read(e));
        _undo.Clear(); _redo.Clear(); _projPath = null; _dirty = false; _sel = null;
        Reparse(); _canvas.Zoom = null; Select(null);
        Status(string.Format(Loc.T("uiD.fromTemplate", "Começou de {0}: os nomes e comandos são os da janela original (ao vincular a ela, tudo funciona)."), Path.GetFileName(key)));
    }

    void SyncLayerSelection()
    {
        _binding = true;
        _list.SelectedIndex = -1;
        for (int i = 0; i < _list.Items.Count; i++) if (_list.Items[i] is ListRow r && r.Key == _sel) { _list.SelectedIndex = i; break; }
        _binding = false;
    }

    static Button SmallButton(string glyph, string key, string pt, Action a)
    {
        var b = new Button { Text = Loc.T(key, pt), AutoSize = true, FlatStyle = FlatStyle.Flat, Image = UiKit.Icon(glyph, Color.FromArgb(30, 39, 51), 16),
            TextImageRelation = TextImageRelation.ImageBeforeText, Height = 30, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Põe um controle na janela: no ponto <paramref name="at"/> (coordenadas da janela) ou no meio.</summary>
    void AddPiece(string block, Point? at)
    {
        var tmp = GameDialog.Parse(UiDesign.Insert(UiDesign.NewXml("t", new Size(1, 1), null), block));
        if (tmp.Controls.Values.FirstOrDefault() is not { } c) return;
        var name = UiDesign.FreeName(_xml, c.Name);
        var box = GameDraw.Box(c);
        var p = at ?? new Point((_dlg.Size.Width - box.Width) / 2, (_dlg.Size.Height - box.Height) / 2);
        Change(x =>
        {
            var nx = UiDesign.Insert(x, UiDesign.Rename(block, name));
            var d = UiDesign.Doc(nx);
            d.SetRect(name, c.Rect with { X = c.Rect.X + p.X - box.X, Y = c.Rect.Y + p.Y - box.Y });
            return d.Xml;
        });
        Select(name);
        _canvas.Focus();
    }

    // miniatura de um controle (ou de uma moldura) como fica no jogo, guardada
    Bitmap? Thumb(string? block, string? frame)
    {
        var key = block ?? "frame|" + frame;
        if (_thumbs.TryGetValue(key, out var bmp)) return bmp;
        try
        {
            if (frame is not null)
            {
                bmp = new Bitmap(120, 80);
                using var g = Graphics.FromImage(bmp);
                g.Clear(ThumbBg);
                GameDraw.Frame(g, _tex?.Get(frame), new Rectangle(0, 0, 120, 80));
            }
            else if (block is not null)
            {
                var d = GameDialog.Parse(UiDesign.Insert(UiDesign.NewXml("t", new Size(1, 1), null), block));
                var c = d.Controls.Values.First();
                var box = GameDraw.Box(c);
                bmp = new Bitmap(Math.Clamp(box.Width, 4, 700), Math.Clamp(box.Height, 4, 500));
                using var g = Graphics.FromImage(bmp);
                g.Clear(ThumbBg);
                g.TranslateTransform(-box.X, -box.Y);
                GameDraw.Dialog(g, d, _tex, GameDraw.Box);
            }
        }
        catch (Exception) { bmp = null; }
        _thumbs[key] = bmp;
        return bmp;
    }

    /// <summary>Ladrilho do painel: miniatura + nome; clique põe na janela, arrastar leva até o ponto.</summary>
    sealed class Tile : Control
    {
        readonly InterfaceDesigner _d;
        readonly string? _block;
        readonly string _label;
        readonly Action _click;
        public string? FramePath;
        bool _hover; Point _down; bool _pressed;
        public Tile(InterfaceDesigner d, string? block, string label, string from, Action click)
        {
            _d = d; _block = block; _label = label; _click = click;
            Size = new Size(116, 96); Margin = new Padding(0, 0, 8, 8); Cursor = Cursors.Hand; DoubleBuffered = true;
            if (from.Length > 0) d._tips.SetToolTip(this, string.Format(Loc.T("uiD.fromWin", "{0} · da janela {1}"), label, from));
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; _down = e.Location; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_pressed || _block is null || Math.Abs(e.X - _down.X) + Math.Abs(e.Y - _down.Y) < 6) return;
            _pressed = false;
            DoDragDrop(new DataObject("MyAceUiPiece", _block), DragDropEffects.Copy);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (_pressed && ClientRectangle.Contains(e.Location)) _click(); _pressed = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            var area = new Rectangle(2, 2, Width - 4, Height - 24);
            using (var b = new SolidBrush(ThumbBg)) g.FillRectangle(b, area);
            if (_d.Thumb(_block, FramePath) is { } bmp)
            {
                float s = Math.Min(1.5f, Math.Min((area.Width - 8f) / bmp.Width, (area.Height - 8f) / bmp.Height));
                int w = (int)(bmp.Width * s), h = (int)(bmp.Height * s);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            }
            if (_hover) using (var p = new Pen(SelColor, 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            TextRenderer.DrawText(g, _label, Font, new Rectangle(2, Height - 21, Width - 4, 18), Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Barra de ícones da esquerda, no estilo da barra lateral do editor (ícone em cima, nome embaixo).</summary>
    sealed class Rail : Control
    {
        readonly InterfaceDesigner _d;
        int _hover = -1;
        const int ItemH = 64;
        public Rail(InterfaceDesigner d) { _d = d; DoubleBuffered = true; BackColor = RailBg; Cursor = Cursors.Hand; }
        int At(Point p) => p.Y >= 8 && (p.Y - 8) / ItemH < RailItems.Length ? (p.Y - 8) / ItemH : -1;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            if (h == _hover) return;
            _hover = h; Invalidate();
            _d._tips.SetToolTip(this, h >= 0 ? Loc.T(RailItems[h].TipKey, RailItems[h].TipPt) : "");
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { if (At(e.Location) is var i and >= 0) _d.ShowFly(i); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(RailBg);
            for (int i = 0; i < RailItems.Length; i++)
            {
                var r = new Rectangle(0, 8 + i * ItemH, Width, ItemH);
                bool sel = _d._flyKind == i && _d._fly.Visible;
                if (sel || i == _hover) using (var b = new SolidBrush(sel ? RailSel : RailHover)) g.FillRectangle(b, r);
                if (sel) using (var b = new SolidBrush(Color.FromArgb(96, 165, 250))) g.FillRectangle(b, 0, r.Y, 3, r.Height);
                var col = sel || i == _hover ? Color.White : RailFg;
                g.DrawImage(UiKit.Icon(RailItems[i].Glyph, col, 22), (Width - 22) / 2, r.Y + 10);
                TextRenderer.DrawText(g, Loc.T(RailItems[i].Key, RailItems[i].Pt), Font, new Rectangle(0, r.Y + 36, Width, 20), col, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // ---------- arquivo ----------

    /// <summary>Mudanças não salvas: Salvar / Descartar / Cancelar. true = pode seguir.</summary>
    internal bool AskDiscard()
    {
        if (!_dirty) return true;
        var save = new TaskDialogButton(Loc.T("ui.askSave", "Salvar"));
        var drop = new TaskDialogButton(Loc.T("ui.askDiscard", "Descartar"));
        var page = new TaskDialogPage
        {
            Caption = Loc.T("uiD.title2", "Criar interface"), Icon = TaskDialogIcon.Warning,
            Heading = Loc.T("uiD.askHeading", "A janela criada tem mudanças não salvas"),
            Text = Loc.T("uiD.askText", "Salvar grava a janela num arquivo .xml no seu computador. Descartar perde as mudanças."),
            Buttons = { save, drop, TaskDialogButton.Cancel }, DefaultButton = save,
        };
        var r = TaskDialog.ShowDialog(this, page);
        if (r == save) return SaveDesign(false);
        return r == drop;
    }

    void NewDesign()
    {
        if (!AskDiscard()) return;
        _xml = UiDesign.NewXml("Win_MyWindow", new Size(360, 260), _frames.FirstOrDefault());
        _undo.Clear(); _redo.Clear(); _projPath = null; _dirty = false; _sel = null;
        Reparse(); _canvas.Zoom = null; Select(null);
    }

    void OpenDesign()
    {
        if (!AskDiscard()) return;
        using var dlg = new OpenFileDialog { Filter = Loc.T("uiD.filter", "Janela criada (*.xml;*.zip)|*.xml;*.zip") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var x = UiDesign.Load(dlg.FileName);
            if (x is null || GameDialog.Parse(x).Name.Length == 0) { Status("⚠ " + Loc.T("uiD.notWindow", "Esse arquivo não tem uma janela do jogo.")); return; }
            _xml = x; _projPath = dlg.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : null;
            _undo.Clear(); _redo.Clear(); _dirty = false; _sel = null;
            Reparse(); _canvas.Zoom = null; Select(null);
        }
        catch (Exception ex) { Status("⚠ " + ex.Message); }
    }

    bool SaveDesign(bool saveAs)
    {
        var path = _projPath;
        if (saveAs || path is null)
        {
            using var dlg = new SaveFileDialog { Filter = Loc.T("uiD.filterXml", "Janela (*.xml)|*.xml"), FileName = UiDesign.WindowName(_xml) + ".xml" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            path = dlg.FileName;
        }
        try { AtomicFileSave.Write(path, UiDesign.ToBytes(_xml)); }
        catch (Exception ex) { Status("⚠ " + ex.Message); return false; }
        _projPath = path; _dirty = false;
        Status("✓ " + string.Format(Loc.T("uiD.saved", "Janela gravada em {0}."), path));
        return true;
    }

    void ExportZip()
    {
        var page = new TaskDialogPage
        {
            Caption = Loc.T("uiD.rZip", "Exportar .zip"), Icon = TaskDialogIcon.Information,
            Heading = Loc.T("uiD.zipHeading", "Para usar no jogo, a janela precisa ser vinculada"),
            Text = Loc.T("uiD.zipText", "O jogo só abre janelas que ele conhece. Esta janela funciona gravada por cima de uma janela do jogo, com os mesmos nomes de controles e os mesmos comandos dos botões (use \"Vincular ao jogo\" aqui no editor). O .zip leva a janela e um LEIA-ME explicando isso."),
            Buttons = { TaskDialogButton.OK, TaskDialogButton.Cancel },
        };
        if (TaskDialog.ShowDialog(this, page) != TaskDialogButton.OK) return;
        var name = UiDesign.WindowName(_xml);
        using var dlg = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = name + ".zip" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var readme = string.Format(Loc.T("uiD.readme",
            "Janela {0} criada no MyAceEditor (Interfaces > Criar interface).\r\n\r\n" +
            "COMO USAR NO JOGO\r\n" +
            "O jogo só abre as janelas que ele já conhece, e liga cada uma ao código dele pelos nomes:\r\n" +
            "1. o nome do arquivo (ex.: version01\\gmconsole.xml) escolhe o código da janela;\r\n" +
            "2. o nome da janela (Win_...) tem que ser o da janela original;\r\n" +
            "3. cada controle que o jogo usa tem que existir com o mesmo nome e o mesmo tipo;\r\n" +
            "4. cada botão tem que ter o mesmo Command da original.\r\n" +
            "Faltando algo, a janela não funciona ou o jogo fecha.\r\n\r\n" +
            "O jeito seguro: abra este .zip no MyAceEditor (Criar interface > Abrir) e use \"Vincular ao jogo\": ele confere tudo, " +
            "traz o que falta e grava no interfaces.pck com backup.\r\n\r\n" +
            "Imagens usadas (ficam no surfaces.pck do jogo):\r\n{1}\r\n"), name, string.Join("\r\n", UiDesign.Images(_xml).Select(i => "  " + i)));
        try { AtomicFileSave.Write(dlg.FileName, UiDesign.Zip(name + ".xml", _xml, readme)); Status("✓ " + string.Format(Loc.T("uiD.zipped", "Exportado: {0}"), dlg.FileName)); }
        catch (Exception ex) { Status("⚠ " + ex.Message); }
    }

    // ---------- vincular ao jogo ----------

    void LinkToGame()
    {
        if (_pckPath is null || _pck is null) { Status("⚠ " + Loc.T("uiD.noPack", "Abra a pasta do PW para ver as janelas do jogo.")); return; }
        if (PickWindow() is not { } target || _pck.Find(target) is not { } entry) return;
        var original = UiDesign.FromBytes(_pck.Read(entry));
        while (true)
        {
            var check = UiDesign.Compare(original, _xml);
            var r = ShowCheck(target, check);
            if (r == LinkAnswer.Cancel) return;
            if (r == LinkAnswer.Bring)
            {
                Change(x =>
                {
                    // controle com o nome certo e tipo errado: o seu fica com outro nome e o original volta
                    foreach (var (want, _) in check.WrongType)
                        x = ReplaceBlock(x, want.Name, UiDesign.Rename(UiDesign.Block(x, want.Name), UiDesign.FreeName(x, want.Name + "_meu")));
                    var again = UiDesign.Compare(original, x);
                    return UiDesign.BringMissing(original, x, again);
                });
                continue;
            }
            WriteToGame(target, original, check.WindowWanted);
            return;
        }
    }

    enum LinkAnswer { Cancel, Bring, Write }

    LinkAnswer ShowCheck(string target, UiDesign.Check check)
    {
        var lines = new List<string>();
        foreach (var n in check.Missing) lines.Add("✗ " + string.Format(Loc.T("uiD.missing", "Falta {0} \"{1}\""), TagName(n.Tag), n.Name) + (n.Command is { } c ? $" (Command {c})" : ""));
        foreach (var (w, has) in check.WrongType) lines.Add("✗ " + string.Format(Loc.T("uiD.wrongType", "\"{0}\" é {1}; no jogo é {2}"), w.Name, TagName(has), TagName(w.Tag)));
        foreach (var c in check.MissingCommands) lines.Add("✗ " + string.Format(Loc.T("uiD.missingCmd", "Nenhum botão com o comando \"{0}\""), c));
        var bring = new TaskDialogButton(Loc.T("uiD.bring", "Trazer o que falta"));
        var write = new TaskDialogButton(Loc.T("uiD.write", "Gravar no jogo"));
        var page = new TaskDialogPage
        {
            Caption = Loc.T("uiD.rLink", "Vincular ao jogo"),
            Icon = check.Ok ? TaskDialogIcon.ShieldSuccessGreenBar : TaskDialogIcon.Warning,
            Heading = check.Ok ? string.Format(Loc.T("uiD.okHeading", "Tudo certo para ficar no lugar de {0}"), Path.GetFileName(target))
                : string.Format(Loc.T("uiD.badHeading", "Faltam {0} coisa(s) que o jogo procura em {1}"), lines.Count, Path.GetFileName(target)),
            Text = (check.Ok
                ? string.Format(Loc.T("uiD.okText", "A janela vai se chamar {0} (o nome da original) e será gravada no interfaces.pck, com backup. Feche e abra o jogo para ver."), check.WindowWanted)
                : Loc.T("uiD.badText", "Sem isso a janela não funciona ou o jogo fecha. \"Trazer o que falta\" copia da janela original (mesmo nome, tipo, lugar e comando); depois você arruma onde quiser.") + "\n\n" + string.Join("\n", lines.Take(20)) + (lines.Count > 20 ? "\n…" : "")),
            Buttons = { check.Ok ? write : bring, TaskDialogButton.Cancel },
        };
        var r = TaskDialog.ShowDialog(this, page);
        return r == bring ? LinkAnswer.Bring : r == write ? LinkAnswer.Write : LinkAnswer.Cancel;
    }

    void WriteToGame(string target, string original, string windowName)
    {
        Change(x => UiDesign.SetDialog(x, name: windowName));
        // as janelas do jogo começam com um BOM a mais dentro do texto: mantém igual à original
        var xml = original.StartsWith('﻿') && !_xml.StartsWith('﻿') ? "﻿" + _xml : _xml;
        var bytes = UiDesign.ToBytes(xml);
        var path = _pckPath!;
        try
        {
            _tex?.Dispose(); _surf?.Dispose(); _pck?.Dispose(); _tex = null; _surf = null; _pck = null;
            var work = Path.Combine(path + ".files", target);
            if (File.Exists(work)) AtomicFileSave.Write(work, bytes);   // cópia de trabalho do editor de janelas, se houver
            var bak = PckWriter.Write(path, new Dictionary<string, byte[]> { [target] = bytes });
            Status("✓ " + string.Format(Loc.T("uiD.written", "Gravada por cima de {0}. Backup: {1}. Feche e abra o jogo para ver."), target, Path.GetFileName(bak)));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _pckPath = null; Open(path); }
    }

    /// <summary>Escolher a janela do jogo que vai ser trocada (busca pelo nome).</summary>
    string? PickWindow()
    {
        using var dlg = new Form { Text = Loc.T("uiD.pickTitle", "Ficar no lugar de qual janela do jogo?"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(460, 480), BackColor = Color.White, Font = Font };
        var hint = new Label { Dock = DockStyle.Top, Height = 54, ForeColor = Muted, Padding = new Padding(10, 8, 10, 0),
            Text = Loc.T("uiD.pickHint", "A janela criada vai ser gravada por cima desta. Na interface nova do jogo valem as do version01.") };
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = Loc.T("uiD.search", "Buscar...") };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var ok = new Button { Text = Loc.T("uiD.pickOk", "Conferir"), Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
        void Fill() { list.BeginUpdate(); list.Items.Clear(); foreach (var w in _windows) if (search.Text.Length == 0 || w.Contains(search.Text, StringComparison.OrdinalIgnoreCase)) list.Items.Add(w); list.EndUpdate(); }
        search.TextChanged += (_, _) => Fill();
        list.DoubleClick += (_, _) => { if (list.SelectedItem is not null) { dlg.DialogResult = DialogResult.OK; dlg.Close(); } };
        var guess = UiDesign.WindowName(_xml);
        Fill();
        // começa na janela que tem o mesmo nome (quem partiu de um modelo já cai nela)
        for (int i = 0; i < list.Items.Count; i++)
            if (_pck?.Find((string)list.Items[i]) is { } e && (string)list.Items[i] is var p && p.Contains("version01", StringComparison.OrdinalIgnoreCase)
                && UiDesign.WindowName(UiDesign.FromBytes(_pck.Read(e))) == guess) { list.SelectedIndex = i; break; }
        dlg.Controls.Add(list); dlg.Controls.Add(search); dlg.Controls.Add(hint); dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        return dlg.ShowDialog(this) == DialogResult.OK ? list.SelectedItem as string : null;
    }

    // ---------- tela do meio ----------

    sealed class DesignCanvas : Panel
    {
        readonly InterfaceDesigner _d;
        public float? Zoom;
        float _s = 1; PointF _o;
        enum Mode { None, Move, Resize, Window }
        Mode _mode; int _handle; Point _start; Rectangle _startRect, _startBox; Size _startWin; bool _pushed;
        readonly List<(bool Vertical, int At)> _guides = new();

        public DesignCanvas(InterfaceDesigner d)
        {
            _d = d; DoubleBuffered = true; BackColor = Bg; AllowDrop = true; TabStop = true;
            ResizeRedraw = true;
        }

        void Layout2()
        {
            var size = _d._dlg.Size.IsEmpty ? new Size(300, 200) : _d._dlg.Size;
            float fit = Math.Min((Width - 80f) / size.Width, (Height - 80f) / size.Height);
            _s = Math.Clamp(Zoom ?? Math.Min(2f, fit), 0.2f, 4f);
            _o = new PointF((Width - size.Width * _s) / 2, (Height - size.Height * _s) / 2);
        }

        Point ToWin(Point p) => new((int)Math.Round((p.X - _o.X) / _s), (int)Math.Round((p.Y - _o.Y) / _s));
        Rectangle ToScreen(Rectangle r) => new((int)(_o.X + r.X * _s), (int)(_o.Y + r.Y * _s), (int)(r.Width * _s), (int)(r.Height * _s));

        Rectangle[] Handles(Rectangle r)
        {
            int x0 = r.Left, x1 = r.Left + r.Width / 2, x2 = r.Right, y0 = r.Top, y1 = r.Top + r.Height / 2, y2 = r.Bottom;
            var pts = new[] { (x0, y0), (x1, y0), (x2, y0), (x2, y1), (x2, y2), (x1, y2), (x0, y2), (x0, y1) };
            return pts.Select(p => new Rectangle(p.Item1 - 5, p.Item2 - 5, 10, 10)).ToArray();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Bg);
            Layout2();
            var d = _d._dlg;
            var win = ToScreen(new Rectangle(Point.Empty, d.Size));
            using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(sh, win.X + 4, win.Y + 6, win.Width, win.Height);
            using (var wb = new SolidBrush(Color.FromArgb(30, 34, 42))) g.FillRectangle(wb, win);
            var state = g.Save();
            g.TranslateTransform(_o.X, _o.Y); g.ScaleTransform(_s, _s);
            g.SetClip(new Rectangle(-2000, -2000, 6000, 6000));
            GameDraw.Dialog(g, d, _d._tex, GameDraw.Box);
            g.Restore(state);
            using (var cap = new Font("Segoe UI", 9f))
                TextRenderer.DrawText(g, $"{d.Name}  ·  {d.Size.Width}×{d.Size.Height}  ·  {_s * 100:0}%", cap, new Point(win.X, win.Y - 22), Muted);
            using var selPen = new Pen(SelColor, 2);
            if (_d.SelCtl is { } c)
            {
                var r = ToScreen(GameDraw.Box(c));
                g.DrawRectangle(selPen, r);
                foreach (var h in Handles(r)) { g.FillRectangle(Brushes.White, h); g.DrawRectangle(Pens.MediumPurple, h); }
            }
            else
            {
                // sem seleção: a própria janela, com os puxadores de tamanho (direita, embaixo, canto)
                using var dash = new Pen(SelColor, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(dash, win);
                foreach (var h in WinHandles(win)) { g.FillRectangle(Brushes.White, h); g.DrawRectangle(Pens.MediumPurple, h); }
            }
            using var gp = new Pen(Guide, 1);
            foreach (var (v, at) in _guides)
            {
                if (v) { int x = (int)(_o.X + at * _s); g.DrawLine(gp, x, win.Top - 20, x, win.Bottom + 20); }
                else { int y = (int)(_o.Y + at * _s); g.DrawLine(gp, win.Left - 20, y, win.Right + 20, y); }
            }
            if (d.Controls.Count == 0)
                TextRenderer.DrawText(g, Loc.T("uiD.empty", "Janela vazia: escolha Elementos ou Texto à esquerda (ou um Modelo para começar de uma janela do jogo)."),
                    Font, new Rectangle(win.X, win.Bottom + 10, Math.Max(win.Width, 360), 40), Muted, TextFormatFlags.WordBreak);
        }

        static Rectangle[] WinHandles(Rectangle w) => new[]
        {
            new Rectangle(w.Right - 5, w.Top + w.Height / 2 - 5, 10, 10), new Rectangle(w.Left + w.Width / 2 - 5, w.Bottom - 5, 10, 10), new Rectangle(w.Right - 5, w.Bottom - 5, 10, 10),
        };

        string? HitControl(Point screen)
        {
            var p = ToWin(screen);
            foreach (var c in _d._dlg.Controls.Values.Reverse())
                if (c.Rect.X > -1000 && GameDraw.Box(c).Contains(p)) return c.Name;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location; _pushed = false; _guides.Clear();
            if (_d.SelCtl is { } sc)
            {
                var hs = Handles(ToScreen(GameDraw.Box(sc)));
                for (int i = 0; i < hs.Length; i++)
                    if (hs[i].Contains(e.Location)) { _mode = Mode.Resize; _handle = i; _startRect = sc.Rect; _startBox = GameDraw.Box(sc); return; }
            }
            else
            {
                var hs = WinHandles(ToScreen(new Rectangle(Point.Empty, _d._dlg.Size)));
                for (int i = 0; i < hs.Length; i++)
                    if (hs[i].Contains(e.Location)) { _mode = Mode.Window; _handle = i; _startWin = _d._dlg.Size; return; }
            }
            var hit = HitControl(e.Location);
            _d.Select(hit);
            if (hit is not null && _d.SelCtl is { } c) { _mode = Mode.Move; _startRect = c.Rect; _startBox = GameDraw.Box(c); }
            else _mode = Mode.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_mode == Mode.None)
            {
                bool onHandle = _d.SelCtl is { } sc ? Handles(ToScreen(GameDraw.Box(sc))).Any(h => h.Contains(e.Location))
                    : WinHandles(ToScreen(new Rectangle(Point.Empty, _d._dlg.Size))).Any(h => h.Contains(e.Location));
                Cursor = onHandle ? Cursors.SizeNWSE : HitControl(e.Location) is not null ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            int dx = (int)Math.Round((e.X - _start.X) / _s), dy = (int)Math.Round((e.Y - _start.Y) / _s);
            if (dx == 0 && dy == 0 && !_pushed) return;
            if (!_pushed) { _d.PushUndo(); _pushed = true; _d._dirty = true; }
            _guides.Clear();
            if (_mode == Mode.Window)
            {
                var ns = new Size(Math.Max(60, _startWin.Width + (_handle != 1 ? dx : 0)), Math.Max(40, _startWin.Height + (_handle != 0 ? dy : 0)));
                _d._xml = UiDesign.SetDialog(_d._xml, size: ns);
                _d._dlg = GameDialog.Parse(_d._xml);
                Invalidate();
                return;
            }
            if (_d._sel is not { } name) return;
            Rectangle box;
            if (_mode == Mode.Move)
            {
                box = _startBox with { X = _startBox.X + dx, Y = _startBox.Y + dy };
                var (sx, sy) = Snap(box, name);
                box.Offset(sx, sy);
            }
            else
            {
                int l = _startBox.Left, t = _startBox.Top, r = _startBox.Right, b = _startBox.Bottom;
                if (_handle is 0 or 6 or 7) l = Math.Min(r - 4, l + dx);
                if (_handle is 2 or 3 or 4) r = Math.Max(l + 4, r + dx);
                if (_handle is 0 or 1 or 2) t = Math.Min(b - 4, t + dy);
                if (_handle is 4 or 5 or 6) b = Math.Max(t + 4, b + dy);
                box = Rectangle.FromLTRB(l, t, r, b);
            }
            // o controle guarda o retângulo do XML; rótulo sem largura usa x como âncora (GameDraw.Box)
            var rect = _mode == Mode.Move
                ? _startRect with { X = _startRect.X + box.X - _startBox.X, Y = _startRect.Y + box.Y - _startBox.Y }
                : box;
            var doc = UiDesign.Doc(_d._xml);
            if (doc.SetRect(name, rect)) { _d._xml = doc.Xml; _d._dlg = GameDialog.Parse(_d._xml); }
            Invalidate();
        }

        // guias como no Canva: borda e meio da janela e dos outros controles, a até 6 px da tela
        (int, int) Snap(Rectangle box, string self)
        {
            int th = Math.Max(2, (int)(6 / _s));
            var xs = new List<int> { 0, _d._dlg.Size.Width / 2, _d._dlg.Size.Width };
            var ys = new List<int> { 0, _d._dlg.Size.Height / 2, _d._dlg.Size.Height };
            foreach (var c in _d._dlg.Controls.Values)
            {
                if (c.Name == self || c.Rect.X < -1000) continue;
                var b = GameDraw.Box(c);
                xs.AddRange(new[] { b.Left, b.Left + b.Width / 2, b.Right }); ys.AddRange(new[] { b.Top, b.Top + b.Height / 2, b.Bottom });
            }
            int Best(int[] mine, List<int> targets, bool vertical)
            {
                int best = int.MaxValue, at = 0;
                foreach (var m in mine) foreach (var t in targets) if (Math.Abs(t - m) <= th && Math.Abs(t - m) < Math.Abs(best)) { best = t - m; at = t; }
                if (best == int.MaxValue) return 0;
                _guides.Add((vertical, at));
                return best;
            }
            int sx = Best(new[] { box.Left, box.Left + box.Width / 2, box.Right }, xs, true);
            int sy = Best(new[] { box.Top, box.Top + box.Height / 2, box.Bottom }, ys, false);
            return (sx, sy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool changed = _pushed;
            _mode = Mode.None; _guides.Clear(); _pushed = false;
            Invalidate();
            if (changed) { _d._lastKey = null; _d.BuildContext(); if (_d._flyKind == 4) _d.FillFly(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (_d.SelCtl is not null && _d._ctx.Controls.Find("text", false).FirstOrDefault() is TextBox t) { t.Focus(); t.SelectAll(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0) return;
            Zoom = Math.Clamp(_s * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.2f, 4f);
            Invalidate();
        }

        protected override void OnDragEnter(DragEventArgs e) => e.Effect = e.Data?.GetDataPresent("MyAceUiPiece") == true ? DragDropEffects.Copy : DragDropEffects.None;

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data?.GetData("MyAceUiPiece") is not string block) return;
            _d.AddPiece(block, ToWin(PointToClient(new Point(e.X, e.Y))));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppLang.Changed -= Texts;
            ClosePacks();
            foreach (var t in _thumbs.Values) t?.Dispose();
        }
        base.Dispose(disposing);
    }
}
