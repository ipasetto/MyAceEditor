namespace MyAceEditor.UI;

/// <summary>
/// Menu FLUTUANTE de ferramentas do Mundo 3D (regra de layout do Mundo 3D, pw-engine §4b): solto à direita, nem em cima
/// nem embaixo; botões grandes com ícone + nome + o que fazem. Clicar numa ferramenta liga a visão do que ela mexe no mundo
/// e o painel dela toma o lugar do menu, com a seta "voltar" no topo. Os filtros "Mostrar" ficam na barra de cima.
/// </summary>
internal sealed partial class WorldView
{
    enum Tool { None, Scene, Paths }
    Tool _tool;
    readonly Panel _tools = new() { Width = 270, BackColor = Color.FromArgb(28, 32, 38), BorderStyle = BorderStyle.FixedSingle };
    readonly Panel _toolHead = new() { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(36, 41, 49) };
    readonly Label _toolTitle = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f), TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _toolBack = new() { Dock = DockStyle.Left, Width = 40, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Visible = false };
    readonly Button _toolFold = new() { Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    readonly FlowLayoutPanel _toolMenu = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(9, 10, 0, 0) };
    readonly FlowLayoutPanel _scenePage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly FlowLayoutPanel _pathsPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly Label _sceneState = new() { AutoSize = true, MaximumSize = new Size(240, 0), ForeColor = Color.Silver, Margin = new Padding(3, 4, 3, 4) };
    readonly ToolTip _toolTip = new();
    bool _toolsFolded;

    internal readonly List<(MarkKind Kind, string Key, string Pt, string Tip, Color Color)> _filterDefs = new()
    {
        (MarkKind.Monster, "world.fMonster", "Monstros", "Pontos de monstro.", Color.FromArgb(239, 83, 80)),
        (MarkKind.Npc, "world.fNpc", "NPCs comuns", "NPCs sem loja.", Color.FromArgb(100, 181, 246)),
        (MarkKind.Merchant, "world.fMerchant", "Comerciantes", "NPCs com serviço de venda.", Color.FromArgb(255, 202, 40)),
        (MarkKind.Guard, "world.fGuard", "Guardas", "NPC em ponto de monstro (nome vermelho).", Color.FromArgb(198, 40, 40)),
        (MarkKind.Mine, "world.fMine", "Coletáveis", "Minas, plantas e outros coletáveis.", Color.FromArgb(102, 187, 106)),
        (MarkKind.Event, "world.fEvent", "Eventos", "Pontos ligados por gatilho com data/hora (nome verde). Aparecem se o tipo também estiver marcado.", Color.FromArgb(0, 230, 118)),
        (MarkKind.Trigger, "world.fTrigger", "Gatilhos", "Pontos ligados por gatilho sem agenda: fase de DG, GM (nome lilás). Aparecem se o tipo também estiver marcado.", Color.FromArgb(186, 104, 200)),
    };

    Button ToolButton(string glyph, string title, string sub, Action a)
    {
        // ladrilho RETANGULAR, um por linha (regra do usuário 01/11): ícone grande à esquerda, nome e para que serve
        var b = new Button { Width = 250, Height = 72, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 45, 54), ForeColor = Color.White, Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 8), Text = "" };
        b.FlatAppearance.BorderColor = Color.FromArgb(62, 70, 84);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 60, 74);
        var icon = UiKit.Icon(glyph, Color.White, 32);
        b.Paint += (_, e) =>
        {
            e.Graphics.DrawImage(icon, 14, (b.Height - icon.Height) / 2);
            using var f1 = new Font("Segoe UI Semibold", 10.5f); using var f2 = new Font("Segoe UI", 8.5f);
            TextRenderer.DrawText(e.Graphics, title, f1, new Rectangle(60, 12, b.Width - 66, 22), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, sub, f2, new Rectangle(60, 34, b.Width - 66, 32), Color.Silver, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        };
        _toolTip.SetToolTip(b, sub);
        b.Click += (_, _) => { a(); _gl.Focus(); };
        return b;
    }

    Button SmallButton(string glyph, string text, Action a, bool main = false)
    {
        var b = new Button { Text = "  " + text, Width = 250, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(40, 45, 54),
            Image = UiKit.Icon(glyph, Color.White, 16), TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
            Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2) };
        b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(62, 70, 84);
        b.Click += (_, _) => { a(); _gl.Focus(); };
        return b;
    }

    static Label SectionLabel(string text) => new() { Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = Color.FromArgb(140, 150, 165), Font = new Font("Segoe UI Semibold", 8f), Margin = new Padding(3, 8, 3, 3) };

    static Label Hint(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Gainsboro, Margin = new Padding(3, 4, 3, 6) };

    void InitToolMenu()
    {
        Disposed += (_, _) => _toolTip.Dispose();
        _toolBack.FlatAppearance.BorderSize = 0; _toolFold.FlatAppearance.BorderSize = 0;
        _toolBack.Image = UiKit.Icon("", Color.White, 16);
        _toolTip.SetToolTip(_toolBack, Loc.T("tools.back", "Voltar ao menu de ferramentas"));
        _toolBack.Click += (_, _) => { CloseTool(); _gl.Focus(); };
        _toolFold.Click += (_, _) => { _toolsFolded = !_toolsFolded; LayoutTools(); _gl.Focus(); };
        _toolHead.Controls.Add(_toolTitle); _toolHead.Controls.Add(_toolBack); _toolHead.Controls.Add(_toolFold);
        _toolTitle.Padding = new Padding(10, 0, 0, 0);
        _toolTitle.ImageAlign = ContentAlignment.MiddleLeft; _toolTitle.TextAlign = ContentAlignment.MiddleLeft;

        // ---- menu ----
        _toolMenu.Controls.Add(ToolButton("\uF158", Loc.T("tools.objects", "Objetos do mundo"), Loc.T("tools.objectsSub", "Escolher, mover, clonar e apagar o que há no mapa"), () => OpenTool(Tool.Scene)));
        _toolMenu.Controls.Add(ToolButton("\uE707", Loc.T("tools.paths2", "Caminhos e áreas"), Loc.T("tools.paths2Sub", "Como cada monstro ou NPC anda: área ou patrulha"), () => OpenTool(Tool.Paths)));

        // ---- Objetos do mundo: só o que serve aqui ----
        _scenePage.Controls.Add(Hint(Loc.T("tools.objSteps", "Clique num objeto para escolher: construção, efeito (laranja), som (azul) ou bichos (verde). Arraste pelos números ou botão direito no chão: mover para lá.")));
        _scenePage.Controls.Add(SectionLabel(Loc.T("tools.objSoon", "Próximas ferramentas")));
        foreach (var (g, k, pt) in new[] {
            ("\uE913", "tools.trees", "Plantar e mover árvores"), ("\uE7B8", "tools.rocks", "Pôr pedras e objetos"),
            ("\uE809", "tools.wall", "Muro e muralha"), ("\uE790", "tools.grassBrush", "Pincel de grama, flores e pedrinhas"),
            ("\uE771", "tools.texBrush", "Pincel de textura do chão") })
        {
            var b = SmallButton(g, Loc.T(k, pt), () => { });
            b.Enabled = false; b.ForeColor = Color.Gray;
            _toolTip.SetToolTip(b, Loc.T("tools.soonTip", "Em breve: precisa da colisão e do formato desse objeto prontos."));
            _scenePage.Controls.Add(b);
        }
        _objPanel.Visible = false; _objPanel.Width = 250; _objPanel.Height = 210; _objPanel.Margin = new Padding(0, 4, 0, 4);
        _scenePage.Controls.Add(_objPanel);
        _scenePage.Controls.Add(_sceneState);
        _scenePage.Controls.Add(SmallButton("\uE7A7", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        _scenePage.Controls.Add(SmallButton("\uE7A6", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        _scenePage.Controls.Add(SmallButton("\uE74E", Loc.T("world.objSave", "Salvar cenário"), SaveScene, main: true));

        // ---- Caminhos e áreas ----
        InitPathPage();

        _tools.Controls.Add(_toolMenu); _tools.Controls.Add(_scenePage); _tools.Controls.Add(_pathsPage); _tools.Controls.Add(_toolHead);
        Controls.Add(_tools);
        _tools.BringToFront();
        _gl.Resize += (_, _) => LayoutTools();
        CloseTool();
    }

    /// <summary>Tamanho e lugar do menu: solto à direita, um pouco abaixo da barra de cima; recolhido = só o cabeçalho.</summary>
    void LayoutTools()
    {
        var page = _tool switch { Tool.Scene => (Control)_scenePage, Tool.Paths => _pathsPage, _ => _toolMenu };
        int body = _tool switch { Tool.Scene => Math.Min(640, _gl.Height - 60), Tool.Paths => Math.Min(640, _gl.Height - 60), _ => _toolMenu.Controls.Count * 80 + 14 };   // ladrilhos, um por linha
        _tools.Height = _toolHead.Height + (_toolsFolded ? 0 : body) + 2;
        foreach (var p in new Control[] { _toolMenu, _scenePage, _pathsPage }) p.Visible = !_toolsFolded && p == page;
        _toolFold.Image = UiKit.Icon(_toolsFolded ? "" : "", Color.White, 16);
        _toolTip.SetToolTip(_toolFold, _toolsFolded ? Loc.T("tools.unfold", "Abrir o menu") : Loc.T("tools.fold", "Recolher o menu"));
        _tools.Location = new Point(Math.Max(0, _gl.Right - _tools.Width - 16), _gl.Top + 16);
        _tools.BringToFront();
    }

    /// <summary>Abre a ferramenta: liga a visão do que ela mexe e o painel dela toma o lugar do menu (seta para voltar).</summary>
    void OpenTool(Tool t)
    {
        _tool = t; _toolsFolded = false;
        _toolBack.Visible = true;
        // só o que a ferramenta faz: sai o painel de baixo (quests/criatura) e a legenda
        _quests.HidePanel(); _creature.HidePanel();
        if (_legendOpen) { _legendOpen = false; _gl.Invalidate(); }
        _toolTitle.Image = UiKit.Icon(t == Tool.Scene ? "" : "", Color.White, 18);
        if (t == Tool.Scene) { _toolTitle.Text = "       " + Loc.T("tools.objects", "Objetos do mundo"); SetSceneObjects(true); }
        if (t == Tool.Paths) { _toolTitle.Text = "       " + Loc.T("tools.paths2", "Caminhos e áreas"); UpdatePathPanel(); _gl.Invalidate(); }
        LayoutTools();
    }

    /// <summary>Volta ao menu: desliga a visão da ferramenta (mudanças não salvas continuam guardadas).</summary>
    void CloseTool()
    {
        if (_tool == Tool.Scene) SetSceneObjects(false);
        if (_tool == Tool.Paths) { _drawPts = null; _dragPt = -1; _gl.Invalidate(); }
        _tool = Tool.None;
        _toolBack.Visible = false;
        _toolTitle.Text = "       " + Loc.T("tools.title", "Ferramentas");
        _toolTitle.Image = UiKit.Icon("", Color.White, 18);   // chave inglesa antes do texto
        LayoutTools();
    }

    /// <summary>Liga/desliga a visão de edição do cenário (pontos de efeitos, sons e bichos; clique escolhe).</summary>
    void SetSceneObjects(bool on)
    {
        _sceneObjs = on; _gl.Invalidate();
        if (_sceneObjs) LoadSceneForEdit(); else { _sceneSel = null; ShowObjPanel(); }
        _status.Text = ObjScene is not { } sc ? Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente.")
            : _sceneObjs ? string.Format(Loc.T("world.objsOn2", "Edição do cenário ligada: {0} efeitos, {1} sons, {2} grupos de bichos — clique num ponto para escolher"), sc.Effects.Count, sc.Sounds.Count, sc.Critters.Count)
            : "";
        UpdateSideState();
    }

    /// <summary>Para a captura (--world-shot): abre a ferramenta Editar cenário ou volta ao menu.</summary>
    internal void ShotTool(int tool) { CloseTool(); if (tool == 1) OpenTool(Tool.Scene); else if (tool == 2) OpenTool(Tool.Paths); }

    /// <summary>Aviso de mudança não salva no painel da ferramenta.</summary>
    void UpdateSideState()
    {
        bool dirty = SceneDirty;
        _sceneState.Text = !_sceneObjs ? "" : _ecw is null ? (_ecwWhy ?? "") : dirty
            ? Loc.T("world.sceneDirtyLine", "● Mudanças não salvas — use Salvar cenário.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _sceneState.ForeColor = dirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
    }
}
