using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Área "Interfaces" (usuário 01/11: saiu do menu Ferramentas e do Extrator PCK, que agora só extrai): as janelas do
/// jogo no interfaces.pck do jogo aberto. Tela inicial = cartão de estado + o que dá para fazer (ladrilhos); editar
/// abre o <see cref="InterfaceView"/> ocupando a área, e "Voltar" retorna aqui.
/// </summary>
internal sealed class InterfacesView : Panel
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128),
        Tile = Color.White, TileHover = Color.FromArgb(219, 232, 253), Line = Color.FromArgb(203, 213, 225);

    readonly Func<string?> _elementDir;
    readonly StatusCard _card = new();
    readonly FlowLayoutPanel _tiles = new() { Dock = DockStyle.Fill, Padding = new Padding(20, 4, 20, 16), AutoScroll = true, BackColor = Bg };
    readonly Label _ask = new() { Dock = DockStyle.Top, Height = 48, ForeColor = Fg, Font = new Font("Segoe UI Semibold", 12f), Padding = new Padding(24, 18, 0, 0), BackColor = Bg };
    readonly List<Button> _tileButtons = new();
    readonly ToolStripButton _edit, _create, _import, _swap, _loading;
    readonly Control[] _home;
    InterfaceView? _editor;
    InterfaceDesigner? _designer;
    int _dialogs;

    /// <summary>Editor de janelas aberto (para o Ctrl+Z da janela principal ir para ele).</summary>
    public InterfaceView? Editor => _editor is { Visible: true } ? _editor : null;

    /// <summary>Tela "Criar interface" aberta (Ctrl+Z da janela principal vai para ela).</summary>
    public InterfaceDesigner? Designer => _designer is { Visible: true } ? _designer : null;

    string? PckPath => _elementDir() is { } d && PckArchive.Exists(Path.Combine(d, "interfaces.pck")) ? Path.Combine(d, "interfaces.pck") : null;

    public InterfacesView(Func<string?> elementDir)
    {
        _elementDir = elementDir;
        Dock = DockStyle.Fill; BackColor = Bg;

        var top = UiKit.Ribbon();
        _edit = UiKit.Cmd(top, UiKit.Edit, "uiA.rEdit", "Editar janelas", OpenEditor, "uiA.editSub", "Mudar posição, tamanho e textos das janelas do jogo, vendo na tela.", primary: true);
        _create = UiKit.Cmd(top, UiKit.Paint, "uiA.rCreate", "Criar interface", OpenDesigner, "uiA.createSub", "Desenhar uma janela sua com peças das janelas do jogo (como no Canva) e vincular no lugar de uma janela do jogo.");
        UiKit.Sep(top);
        _import = UiKit.Cmd(top, UiKit.Download, "uiA.rImport", "Importar pasta", Import, "uiA.importSub", "Trazer uma pasta de interface (exportada ou baixada); o programa confere antes de gravar.");
        _swap = UiKit.Cmd(top, UiKit.Swap, "uiA.rSwap", "Trocar com outro cliente", Swap, "uiA.swapSub", "Usar a interface de outro cliente do PW (tudo ou só uma parte, com as imagens).");
        _loading = UiKit.Cmd(top, UiKit.Picture, "uiA.rLoading", "Telas de carregamento", Loading, "uiA.loadingSub", "Trocar as imagens que aparecem enquanto o jogo carrega.");

        AddTile(UiKit.Edit, "uiA.rEdit", "Editar janelas", "uiA.editSub", "Mudar posição, tamanho e textos das janelas do jogo, vendo na tela.", OpenEditor);
        AddTile(UiKit.Paint, "uiA.rCreate", "Criar interface", "uiA.createSub", "Desenhar uma janela sua com peças das janelas do jogo (como no Canva) e vincular no lugar de uma janela do jogo.", OpenDesigner);
        AddTile(UiKit.Download, "uiA.rImport", "Importar pasta", "uiA.importSub", "Trazer uma pasta de interface (exportada ou baixada); o programa confere antes de gravar.", Import);
        AddTile(UiKit.Swap, "uiA.rSwap", "Trocar com outro cliente", "uiA.swapSub", "Usar a interface de outro cliente do PW (tudo ou só uma parte, com as imagens).", Swap);
        AddTile(UiKit.Picture, "uiA.rLoading", "Telas de carregamento", "uiA.loadingSub", "Trocar as imagens que aparecem enquanto o jogo carrega.", Loading);

        Controls.Add(_tiles); Controls.Add(_ask); Controls.Add(_card); Controls.Add(top);
        _home = new Control[] { _tiles, _ask, _card, top };
        VisibleChanged += (_, _) => { if (Visible && Editor is null && Designer is null) RefreshState(); };
        AppLang.Changed += Texts;
        Texts();
    }

    // ladrilho largo: ícone grande à esquerda, nome e para que serve (mesmo padrão das ferramentas do Mundo 3D, tema claro)
    void AddTile(string glyph, string key, string pt, string subKey, string subPt, Action click)
    {
        var b = new Button { Width = 380, Height = 84, FlatStyle = FlatStyle.Flat, BackColor = Tile, Cursor = Cursors.Hand, Margin = new Padding(4, 4, 12, 12), Text = "" };
        b.FlatAppearance.BorderColor = Line; b.FlatAppearance.MouseOverBackColor = TileHover;
        var icon = UiKit.Icon(glyph, UiKit.Accent, 32);
        var grey = UiKit.Icon(glyph, Color.FromArgb(170, 178, 190), 32);
        b.Paint += (_, e) =>
        {
            e.Graphics.DrawImage(b.Enabled ? icon : grey, 18, (b.Height - icon.Height) / 2);
            using var f1 = new Font("Segoe UI Semibold", 11f); using var f2 = new Font("Segoe UI", 9f);
            var c1 = b.Enabled ? Fg : Muted;
            TextRenderer.DrawText(e.Graphics, Loc.T(key, pt), f1, new Rectangle(66, 14, b.Width - 74, 24), c1, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Loc.T(subKey, subPt), f2, new Rectangle(66, 38, b.Width - 74, 40), Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        };
        b.Click += (_, _) => click();
        _tileButtons.Add(b);
        _tiles.Controls.Add(b);
    }

    void Texts()
    {
        _ask.Text = Loc.T("uiA.ask", "O que você quer fazer com as janelas do jogo?");
        RefreshState();
    }

    /// <summary>Cartão de estado e botões conforme o interfaces.pck do jogo aberto.</summary>
    void RefreshState()
    {
        var path = PckPath;
        _dialogs = 0;
        if (path is not null)
            try { using var p = new PckArchive(path); _dialogs = InterfaceView.CountDialogs(p); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        if (path is null)
            _card.Set(StatusCard.Kind.Off, Loc.T("uiA.noGame", "Nenhum jogo aberto"), Loc.T("uiA.noGameDetail", "Abra a pasta do PW (Arquivo > Abrir pasta) para trabalhar com as janelas dele."));
        else if (_dialogs == 0)
            _card.Set(StatusCard.Kind.Warn, Loc.T("uiA.noXml", "interfaces.pck sem janelas"), path);
        else
            _card.Set(StatusCard.Kind.On, string.Format(Loc.T("uiA.ready", "interfaces.pck: {0:N0} janelas"), _dialogs), path);
        bool has = path is not null;
        _edit.Enabled = _create.Enabled = _dialogs > 0; _import.Enabled = _swap.Enabled = has; _loading.Enabled = _elementDir() is not null;
        for (int i = 0; i < _tileButtons.Count; i++)
            _tileButtons[i].Enabled = i switch { 0 or 1 => _dialogs > 0, 4 => _elementDir() is not null, _ => has };
        foreach (var b in _tileButtons) b.Invalidate();
    }

    void OpenEditor()
    {
        if (PckPath is not { } path || _dialogs == 0) return;
        if (_editor is null)
        {
            _editor = new InterfaceView(CloseEditor) { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_editor);
        }
        foreach (var c in _home) c.Visible = false;
        _editor.Visible = true; _editor.BringToFront();
        _editor.LoadPack(path);
    }

    void CloseEditor()
    {
        if (_editor is not null) _editor.Visible = false;
        foreach (var c in _home) c.Visible = true;
        RefreshState();
    }

    void OpenDesigner()
    {
        if (PckPath is not { } path || _dialogs == 0) return;
        if (_designer is null)
        {
            _designer = new InterfaceDesigner(CloseDesigner) { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_designer);
        }
        foreach (var c in _home) c.Visible = false;
        _designer.Visible = true; _designer.BringToFront();
        _designer.Open(path);
    }

    void CloseDesigner()
    {
        if (_designer is not null) _designer.Visible = false;
        foreach (var c in _home) c.Visible = true;
        RefreshState();
    }

    /// <summary>Diagnóstico (--snapshot ... uicreate): abre a tela Criar interface.</summary>
    internal void OpenDesignerForSnapshot(string? template) { RefreshState(); OpenDesigner(); _designer?.SnapshotDemo(template); }

    void Import() { if (PckPath is { } p) { UiTools.Import(this, p); RefreshState(); } }
    void Swap() { if (PckPath is { } p) { UiTools.Swap(this, p); RefreshState(); } }
    void Loading() { UiTools.LoadingScreens(this, _elementDir()); RefreshState(); }

    /// <summary>Outra pasta do PW foi aberta: o editor (se aberto) passa para o interfaces.pck do jogo novo.</summary>
    public void GameFolderChanged()
    {
        bool editing = Editor is not null, designing = Designer is not null;
        CloseEditor();
        if (designing) { CloseDesigner(); if (_dialogs > 0) OpenDesigner(); }
        if (editing && _dialogs > 0) OpenEditor();
    }

    /// <summary>Diagnóstico (--snapshot ... ui:arquivo.xml): abre o editor e a janela pedida.</summary>
    internal void OpenUiForSnapshot(string file)
    {
        RefreshState();
        OpenEditor();
        _editor?.OpenForSnapshot(file);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) AppLang.Changed -= Texts;
        base.Dispose(disposing);
    }
}
