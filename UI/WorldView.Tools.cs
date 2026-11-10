using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Menu FLUTUANTE de ferramentas do Mundo 3D (regra de layout do Mundo 3D, pw-engine §4b): solto à direita, nem em cima
/// nem embaixo; botões grandes com ícone + nome + o que fazem. Clicar numa ferramenta liga a visão do que ela mexe no mundo
/// e o painel dela toma o lugar do menu, com a seta "voltar" no topo. Os filtros "Mostrar" ficam na barra de cima.
/// </summary>
internal sealed partial class WorldView
{
    enum Tool { None, Scene, Paths, Quests, Create, Teleports, Zones, Domains, Terrain }
    /// <summary>Salvar tudo e enviar ao servidor (EditorForm.SaveAndSend).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] internal Action? SaveAndSendAll { get; set; }
    Tool _tool;
    readonly Panel _tools = new() { Width = 286, BackColor = Color.FromArgb(28, 32, 38), BorderStyle = BorderStyle.FixedSingle };
    readonly Panel _toolHead = new() { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(36, 41, 49) };
    readonly Label _toolTitle = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10.5f), TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _toolBack = new() { Dock = DockStyle.Left, Width = 40, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Visible = false };
    readonly Button _toolFold = new() { Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    readonly FlowLayoutPanel _toolMenu = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(9, 10, 0, 0) };
    readonly FlowLayoutPanel _scenePage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly FlowLayoutPanel _pathsPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly FlowLayoutPanel _createPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly Label _sceneState = new() { AutoSize = true, MaximumSize = new Size(240, 0), ForeColor = Color.Silver, Margin = new Padding(3, 4, 3, 4) };
    readonly ToolTip _toolTip = new();
    bool _toolsFolded = false;   // abre expandido (usuário 07/11 noite; antes recolhido)
    // menu recolhido sozinho porque não cabia acima do painel de baixo (criatura, quests); volta quando couber de novo.
    // Abrir na mão (clique no cabeçalho) vale até caber de novo.
    bool _toolsAutoFolded, _toolsForceOpen;

    internal readonly List<(MarkKind Kind, string Key, string Pt, string Tip, Color Color)> _filterDefs = new()
    {
        (MarkKind.Monster, "world.fMonster", "Monstros", "Pontos de monstro.", Color.FromArgb(239, 83, 80)),
        (MarkKind.Npc, "world.fNpc", "NPCs comuns", "NPCs sem loja.", Color.FromArgb(100, 181, 246)),
        (MarkKind.Merchant, "world.fMerchant", "Comerciantes", "NPCs com serviço de venda.", Color.FromArgb(255, 202, 40)),
        (MarkKind.Guard, "world.fGuard", "Guardas", "NPC em ponto de monstro (nome vermelho).", Color.FromArgb(198, 40, 40)),
        (MarkKind.Mine, "world.fMine", "Coletáveis", "Minas, plantas e outros coletáveis.", Color.FromArgb(102, 187, 106)),
        (MarkKind.Event, "world.fEvent", "Eventos", "Pontos ligados por gatilho com data/hora (nome verde). Aparecem se o tipo também estiver marcado.", Color.FromArgb(0, 230, 118)),
        (MarkKind.Plugin, "world.fPlugin", "Plugins", "Pontos de evento dos plugins (área Plugins): nome laranja. Aparecem se o tipo também estiver marcado.", Color.FromArgb(255, 152, 0)),
        (MarkKind.Trigger, "world.fTrigger", "Gatilhos", "Pontos ligados por gatilho sem agenda: fase de DG, GM (nome lilás). Aparecem se o tipo também estiver marcado.", Color.FromArgb(186, 104, 200)),
    };

    Button ToolButton(string glyph, string title, string sub, Action a)
    {
        // ladrilho RETANGULAR, um por linha, com METADE da altura (usuário 10/11: com o tempo entram mais ferramentas e
        // ficaria apertado): ícone e nome; "para que serve" vai para a dica do mouse. Nome longo (traduções) usa 2 linhas.
        var b = new NoFocusButton { Width = 250, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 45, 54), ForeColor = Color.White, Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 4), Text = "", AccessibleName = title };
        b.FlatAppearance.BorderColor = Color.FromArgb(62, 70, 84);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 60, 74);
        var icon = UiKit.Icon(glyph, Color.White, 18);
        var f1 = new Font("Segoe UI Semibold", 10f);
        b.Disposed += (_, _) => f1.Dispose();
        const int textX = 40;
        int tw = b.Width - textX - 8;
        if (TextRenderer.MeasureText(title, f1).Width > tw)
            b.Height = Math.Max(36, TextRenderer.MeasureText(title, f1, new Size(tw, 0), TextFormatFlags.WordBreak).Height + 12);
        b.Paint += (_, e) =>
        {
            e.Graphics.DrawImage(icon, 12, (b.Height - icon.Height) / 2);
            TextRenderer.DrawText(e.Graphics, title, f1, new Rectangle(textX, 0, tw, b.Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        };
        _toolTip.SetToolTip(b, title + Environment.NewLine + sub);
        b.Click += (_, _) => { a(); _gl.Focus(); };
        return b;
    }

    Button SmallButton(string glyph, string text, Action a, bool main = false)
    {
        var b = new NoFocusButton { Text = "  " + text, Width = 250, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(40, 45, 54),
            Image = UiKit.Icon(glyph, Color.White, 16), TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
            Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2) };
        b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(62, 70, 84);
        // texto longo (traduções): o botão cresce para 2 linhas em vez de cortar (usuário 09/11)
        void Fit()
        {
            int tw = b.Width - 16 - 22, th = TextRenderer.MeasureText(b.Text, b.Font, new Size(Math.Max(1, tw), 0), TextFormatFlags.WordBreak).Height;
            int want = Math.Max(32, th + 12);
            if (b.Height != want) b.Height = want;
        }
        b.SizeChanged += (_, _) => Fit(); b.TextChanged += (_, _) => Fit(); Fit();
        b.Click += (_, _) => { a(); _gl.Focus(); };
        return b;
    }

    static Label SectionLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 8f), Margin = new Padding(3, 8, 3, 3) };

    /// <summary>Quadro de uma ferramenta do Mundo 3D, no mesmo padrão do editor de quests.</summary>
    static FlowLayoutPanel ToolCard()
    {
        // todos com a mesma largura; quadro sem nada visível some (fica com altura 0)
        var card = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(39, 45, 54), MinimumSize = new Size(246, 0) };
        void Fit()
        {
            bool any = card.Controls.Cast<Control>().Any(c => c.Visible);
            card.Padding = any ? new Padding(9, 6, 9, 8) : Padding.Empty;
            card.Margin = any ? new Padding(0, 0, 0, 8) : Padding.Empty;
        }
        card.ControlAdded += (_, e) => { e.Control!.VisibleChanged += (_, _) => Fit(); Fit(); };
        return card;
    }

    /// <summary>Página que rola: a barra de rolagem toma largura, então nada pode encostar na borda do quadro.</summary>
    static void FitToScroll(Control page)
    {
        const int Width = 228;
        foreach (Control c in page.Controls)
        {
            if (c is Button or TextBox or ListBox) c.Width = Width;
            if (c is Label { AutoSize: true }) c.MaximumSize = new Size(Width - 8, 0);
            FitToScroll(c);
        }
    }

    /// <summary>Volta os controles à ordem certa: quadro que nasce escondido pode inverter a ordem ao aparecer.</summary>
    static void KeepOrder(Control parent, IReadOnlyList<Control> order)
    {
        for (int i = 0; i < order.Count; i++) parent.Controls.SetChildIndex(order[i], i);
    }

    static Label Hint(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Gainsboro, Margin = new Padding(3, 4, 3, 6) };

    void InitToolMenu()
    {
        Disposed += (_, _) => _toolTip.Dispose();
        _toolBack.FlatAppearance.BorderSize = 0; _toolFold.FlatAppearance.BorderSize = 0;
        _toolBack.Image = UiKit.Icon("", Color.White, 16);
        _toolTip.SetToolTip(_toolBack, Loc.T("tools.back", "Voltar ao menu de ferramentas"));
        _toolBack.Click += (_, _) => { if (WorldEditOpen && !AbandonWorld()) return; if (_tool == Tool.Zones && !ZoneLeave()) return; if (_tool == Tool.Domains && !CloseDomains()) return; if (_tool == Tool.Create && (_creating || _editing > 0)) { if (AskAbandon()) UpdateCreatePanel(); return; } if (_tool == Tool.Create ? AskAbandon() : _tool != Tool.Teleports || _tpd is null || AskLeaveTeleport()) CloseTool(); _gl.Focus(); };
        _toolFold.Click += (_, _) => { _toolsFolded = !_toolsFolded; _toolsForceOpen = !_toolsFolded && _toolsAutoFolded; _toolsAutoFolded = false; LayoutTools(); _gl.Focus(); };
        _toolHead.Controls.Add(_toolTitle); _toolHead.Controls.Add(_toolBack); _toolHead.Controls.Add(_toolFold);
        _toolTitle.Padding = new Padding(10, 0, 0, 0);
        _toolTitle.ImageAlign = ContentAlignment.MiddleLeft; _toolTitle.TextAlign = ContentAlignment.MiddleLeft;

        // ---- menu ----
        _toolMenu.Controls.Add(ToolButton("\uF158", Loc.T("tools.world", "Edição de Mundo"), Loc.T("tools.worldSub", "Objetos, terreno, água, grama e flores num lugar só, com um Salvar."), () => OpenTool(_worldPill)));
        _toolMenu.Controls.Add(ToolButton("\uE707", Loc.T("tools.paths2", "Caminhos e áreas"), Loc.T("tools.paths2Sub", "Como cada monstro ou NPC anda: área ou patrulha"), () => OpenTool(Tool.Paths)));
        _toolMenu.Controls.Add(ToolButton("\uE7C1", Loc.T("tools.quests", "Quests"), Loc.T("tools.questsSub", "Clique num NPC: ver, criar, dar, editar e excluir as quests dele"), () => OpenTool(Tool.Quests)));
        _toolMenu.Controls.Add(ToolButton("\uE710", Loc.T("tools.npcs", "NPCs e monstros"), Loc.T("tools.npcsSub", "Criar e editar NPC, vendedor, fornalha e monstro."), () => OpenTool(Tool.Create)));
        _toolMenu.Controls.Add(ToolButton("\uE81E", Loc.T("tools.domains", "Territórios"), Loc.T("tools.domainsSub", "Áreas da guerra de clãs: desenhar, criar e excluir no mapa."), () => OpenTool(Tool.Domains)));
        _toolMenu.Controls.Add(ToolButton("\uE909", Loc.T("tools.zones", "Zonas"), Loc.T("tools.zonesSub", "Nome, música e regras de cada lugar do mapa."), () => OpenTool(Tool.Zones)));
        _toolMenu.Controls.Add(ToolButton("\uE8AB", Loc.T("tools.teleports", "Teleportes"), Loc.T("tools.teleportsSub2", "Lugares do mapa de viagem e o NPC que atende em cada um."), () => OpenTool(Tool.Teleports)));
        // sempre por último: grava tudo e leva ao servidor (a janela principal abre na aba Servidor)
        var send = ToolButton("\uE898", Loc.T("tools.send", "Salvar e enviar ao servidor"), Loc.T("tools.sendSub", "Grava tudo o que mudou e reinicia o servidor local com isso"), () =>
        {
            if (PathDirty && MessageBox.Show(this, Loc.T("tools.sendPaths", "Há caminhos de patrulha não salvos. Salvar os caminhos também?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) SavePaths();
            SaveAndSendAll?.Invoke();
        });
        send.BackColor = Color.FromArgb(30, 70, 140);
        _toolMenu.Controls.Add(send);

        // ---- Objetos do mundo: só o que serve aqui ----
        _scenePage.Controls.Add(WorldPills());
        _scenePage.Controls.Add(Hint(Loc.T("obj.steps", "Clique em qualquer objeto à vista: construção, modelo, efeito (laranja), som (azul) ou bichos (verde). Mova pelos números ou com o botão direito no chão; gire pelo giro; Delete apaga. Nada grava até o Salvar.")));
        _showScenePoints.Text = Loc.T("obj.showPoints", "Mostrar efeitos e sons");
        _showScenePoints.CheckedChanged += (_, _) => { if (!_showScenePoints.Checked && _sceneSel is WorldScene.SceneEffect or WorldScene.SceneSound or WorldScene.Critter) { _sceneSel = null; ShowObjPanel(); } _gl.Invalidate(); };
        _scenePage.Controls.Add(_showScenePoints);
        _scenePage.Padding = new Padding(_scenePage.Padding.Left, _scenePage.Padding.Top, _scenePage.Padding.Right, 36);
        _scenePage.Controls.Add(SectionLabel(Loc.T("obj.insert", "Inserir no mundo")));
        foreach (var (g, k, pt) in new[] {
            ("\uE80F", "obj.pillBuildings", "Construções"), ("\uE913", "obj.pillTrees", "Árvores"),
            ("\uE7B8", "obj.pillModels", "Modelos") })
        {
            bool trees = k == "obj.pillTrees", blds = k == "obj.pillBuildings";
            var b = SmallButton(g, Loc.T(k, pt), trees ? ChooseTreeToPlant : blds ? ChooseBuildingToPlace : () => { });
            if (trees) _toolTip.SetToolTip(b, Loc.T("obj.pillTreesTip", "Escolher o tipo de árvore e clicar no chão para plantar."));
            else if (blds) _toolTip.SetToolTip(b, Loc.T("obj.pillBuildingsTip", "Escolher uma construção das que estão por perto e clicar no chão para pôr uma cópia."));
            else { b.Enabled = false; b.ForeColor = Color.Gray; _toolTip.SetToolTip(b, Loc.T("obj.pillSoon", "Chega na próxima etapa da ferramenta.")); }
            _scenePage.Controls.Add(b);
        }
        _objPanel.Visible = false; _objPanel.Width = 250; _objPanel.Height = 330; _objPanel.Margin = new Padding(0, 4, 0, 4);
        _scenePage.Controls.Add(_objPanel);
        _scenePage.Controls.Add(_sceneState);
        _scenePage.Controls.Add(SmallButton("\uE7A7", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        _scenePage.Controls.Add(SmallButton("\uE7A6", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        var sceneSave = SmallButton("\uE74E", Loc.T("world.save", "Salvar mundo"), () => SaveWorld(), main: true);
        _toolTip.SetToolTip(sceneSave, Loc.T("world.saveTip", "Grava tudo o que mudou na Edição de Mundo: objetos, grama, terreno e água."));
        _scenePage.Controls.Add(sceneSave);
        _scenePage.Controls.Add(new Panel { Width = 10, Height = 40, Margin = Padding.Empty });   // espaço livre embaixo (regra do usuário)

        // ---- Caminhos e áreas ----
        InitPathPage();
        InitQuestPage();
        InitCreatePage();
        InitZoneTool();
        _gl.Resize += (_, _) => { if (_domains is { IsDisposed: false } w) w.Bounds = _gl.Bounds; };
        FormClosing += (_, e) => { if (_tool == Tool.Domains && !CloseDomains()) e.Cancel = true; };
        InitTeleportPage();

        InitTerrainPage();
        _tools.Controls.Add(_terrainPage);
        _tools.Controls.Add(_zonePage); _tools.Controls.Add(_toolMenu); _tools.Controls.Add(_scenePage); _tools.Controls.Add(_pathsPage); _tools.Controls.Add(_questsPage); _tools.Controls.Add(_createPage); _tools.Controls.Add(_tpPage); _tools.Controls.Add(_toolHead);
        _tools.Controls.Add(_zFoot);
        Controls.Add(_tools);
        _tools.BringToFront();
        _gl.Resize += (_, _) => LayoutTools();
        foreach (Control pnl in new Control[] { _creature, _quests }) { pnl.VisibleChanged += (_, _) => LayoutTools(); pnl.SizeChanged += (_, _) => LayoutTools(); }   // painel de baixo abriu/fechou
        CloseTool();
    }

    /// <summary>Tamanho e lugar do menu: solto à direita, um pouco abaixo da barra de cima; recolhido = só o cabeçalho.</summary>
    void LayoutTools()
    {
        var page = _tool switch { Tool.Terrain => _terrainPage, Tool.Scene => (Control)_scenePage, Tool.Paths => _pathsPage, Tool.Quests => _questsPage, Tool.Create => _createPage, Tool.Teleports => _tpPage, Tool.Zones => _zonePage, _ => _toolMenu };
        int body = _tool switch { Tool.Domains => 0, Tool.Scene or Tool.Terrain => Math.Max(200, _gl.Height - 32 - _toolHead.Height) /* até embaixo (antes 640 e rolava) */, Tool.Zones => _gl.Height - 60, Tool.Paths => Math.Min(FitHeight(_pathsPage), _gl.Height - 60), Tool.Create => Math.Min(_createPage.PreferredSize.Height + 10, _gl.Height - 60), Tool.Teleports => Math.Min(_tpPage.PreferredSize.Height + 10, _gl.Height - 60), Tool.Quests => Math.Min(FitHeight(_questsPage), _gl.Height - 60), _ => _toolMenu.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + 14 };   // ladrilhos, um por linha
        if (_tool == Tool.None)
        {
            // o menu nunca cobre o painel de baixo (usuário 10/11): sem espaço acima dele, recolhe ao cabeçalho
            // a cena fica com a altura toda e os painéis de baixo (criatura, quests) por cima dela: o limite é o topo do
            // painel visível
            int bottom = _gl.Bottom;
            foreach (Control pnl in new Control[] { _creature, _quests }) if (pnl.Visible && pnl.Height > 0) bottom = Math.Min(bottom, pnl.Top);
            bool fits = _gl.Top + 16 + _toolHead.Height + body + 2 + 8 <= bottom;
            if (!fits && !_toolsFolded && !_toolsForceOpen) { _toolsFolded = true; _toolsAutoFolded = true; }
            else if (fits) { if (_toolsAutoFolded) _toolsFolded = false; _toolsAutoFolded = false; _toolsForceOpen = false; }
        }
        _tools.Height = _toolHead.Height + (_toolsFolded ? 0 : body) + 2;
        foreach (var p in new Control[] { _toolMenu, _scenePage, _pathsPage, _questsPage, _createPage, _tpPage, _zonePage, _terrainPage }) p.Visible = !_toolsFolded && p == page;
        _zFoot.Visible = _tool == Tool.Zones && !_toolsFolded && _zDrawing == ZoneDraw.None;
        _toolFold.Image = UiKit.Icon(_toolsFolded ? "" : "", Color.White, 16);
        _toolTip.SetToolTip(_toolFold, _toolsFolded ? Loc.T("tools.unfold", "Abrir o menu") : Loc.T("tools.fold", "Recolher o menu"));
        _tools.Location = _tool == Tool.Domains ? new Point(_gl.Right - _tools.Width - 2, _gl.Top) : new Point(Math.Max(0, _gl.Right - _tools.Width - 16), _gl.Top + 16);
        _tools.BringToFront();
    }

    /// <summary>Altura que o conteúdo visível da página pede (o painel não fica com sobra vazia embaixo).</summary>
    static int FitHeight(FlowLayoutPanel p) => p.Padding.Vertical + 10 + p.Controls.Cast<Control>().Where(c => c.Visible || !p.Visible).Sum(c => c.Height + c.Margin.Vertical);

    /// <summary>Painéis de baixo (quests do NPC, criatura) só sem ferramenta: com uma ferramenta aberta a tela mostra só o que
    /// ela edita (regra do usuário 01/11). Todo painel de baixo novo passa por aqui.</summary>
    bool BottomPanelsAllowed => _tool == Tool.None;
    void HideBottomPanels() { _quests.HidePanel(); _creature.HidePanel(); }

    /// <summary>Abre a ferramenta: liga a visão do que ela mexe e o painel dela toma o lugar do menu (seta para voltar).</summary>
    void OpenTool(Tool t)
    {
        _tool = t; _toolsFolded = false; _toolsAutoFolded = false; _toolsForceOpen = false;
        _toolBack.Visible = true;
        // só o que a ferramenta faz: sai o painel de baixo (quests/criatura) e a legenda
        HideBottomPanels();
        if (_legendOpen) { _legendOpen = false; _gl.Invalidate(); }
        _toolTitle.Image = UiKit.Icon(t == Tool.Scene ? "\uF158" : t == Tool.Quests ? "\uE7C1" : t == Tool.Create ? "\uE710" : t == Tool.Teleports ? "\uE8AB" : "\uE707", Color.White, 18);
        if (t == Tool.Quests)
        {
            _toolTitle.Text = "       " + Loc.T("tools.quests", "Quests");
            ShowQuestTool(_questNpc > 0 ? NpcQuests?.Invoke(_questNpc) : null);
        }
        if (t is Tool.Scene or Tool.Terrain) { _worldPill = t; _toolTitle.Text = "       " + Loc.T("tools.world", "Edição de Mundo"); _toolTitle.Image = UiKit.Icon("\uF158", Color.White, 18); PaintPills(); }
        if (t == Tool.Scene) SetSceneObjects(true);
        if (t == Tool.Terrain) TexLabels();
        if (t == Tool.Paths) { _toolTitle.Text = "       " + Loc.T("tools.paths2", "Caminhos e áreas"); UpdatePathPanel(); _gl.Invalidate(); }
        if (t == Tool.Create)
        {
            _toolTitle.Text = "       " + Loc.T("tools.npcs", "NPCs e monstros");
            if (SelectedMark() is { Id: > 0 } chosen) _target = chosen.Id;
            UpdateCreatePanel();
        }
        if (t == Tool.Teleports) { _toolTitle.Text = "       " + Loc.T("tools.teleports", "Teleportes"); OpenTeleports(); }
        if (t == Tool.Zones) { _toolTitle.Text = "       " + Loc.T("tools.zones", "Zonas"); OpenZones(); }
        if (t == Tool.Domains) { _toolTitle.Text = "       " + Loc.T("tools.domains", "Territórios"); if (!OpenDomains()) { CloseTool(); return; } }
        LayoutTools();
        UpdateInfoCard();
    }

    /// <summary>Sai da ferramenta para a navegação do Mundo 3D. Quem tem trabalho em andamento pergunta antes; falso na resposta, fica.</summary>
    void LeaveTool()
    {
        if (_tool == Tool.Zones && !ZoneLeave()) return;
        if (_tool == Tool.Domains && !CloseDomains()) return;
        if (WorldEditOpen && !AbandonWorld()) return;
        if (WorldEditOpen) { StopBrush(); StopPlanting(); }
        StopDropper();
        if (_tool == Tool.Create)
        {
            if (_editing > 0 && MessageBox.Show(this, Loc.T("create.stopEditAsk", "Sair sem salvar as mudanças deste NPC?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!AskAbandon()) return;
        }
        if (_tool == Tool.Teleports && _tpd is not null && !AskLeaveTeleport()) return;
        CloseTool();
        _gl.Focus();
    }

    /// <summary>--world-shot: Esc sai de toda ferramenta; passo em andamento cancela primeiro.</summary>
    internal string EscFlowTest()
    {
        var o = new List<string>();
        foreach (var tool in new[] { Tool.Scene, Tool.Paths, Tool.Quests, Tool.Create, Tool.Teleports, Tool.Zones, Tool.Domains })
        {
            OpenTool(tool);
            bool open = _tool == tool;
            bool used = EscapeStep();
            o.Add((open && used && _tool == Tool.None ? "OK " : "FALHA ") + "esc-" + tool);
        }
        o.Add((!EscapeStep() ? "OK " : "FALHA ") + "esc-sem-ferramenta");
        OpenTool(Tool.Zones);
        StartNewZone();
        bool first = EscapeStep() && _tool == Tool.Zones && _zDrawing == ZoneDraw.None;
        bool second = EscapeStep() && _tool == Tool.None;
        o.Add((first && second ? "OK " : "FALHA ") + "esc-passo-antes");
        OpenTool(Tool.Create);
        _creating = true;
        _placing = _ => { };
        first = EscapeStep() && _tool == Tool.Create && _placing is null;
        second = EscapeStep() && _tool == Tool.None;
        o.Add((first && second ? "OK " : "FALHA ") + "esc-lugar-antes");
        return string.Join(Environment.NewLine, o);
    }

    DomainEditorWindow? _domains;

    internal void DomainShot() => OpenTool(Tool.Domains);

    /// <summary>Territórios como as outras ferramentas: o editor cobre a cena e o título com a seta fica sobre o painel dele.</summary>
    bool OpenDomains()
    {
        _domains = DomainEditorWindow.Embedded(this, ElementOfMap, ServerConfigDir, _tpPrecinctDomains(), _toolHead.Height + 8);
        if (_domains is null) return false;
        _domains.SendToServer = SaveAndSendAll;
        _domains.Bounds = _gl.Bounds;
        Controls.Add(_domains);
        _domains.Show();
        _domains.BringToFront();
        _tools.BringToFront();
        return true;
    }

    bool CloseDomains()
    {
        if (_domains is not { IsDisposed: false } w) return true;
        if (!w.TryClose()) return false;
        _domains = null;
        return true;
    }

    /// <summary>Volta ao menu: desliga a visão da ferramenta (mudanças não salvas continuam guardadas).</summary>
    void CloseTool()
    {
        if (_tool == Tool.Scene) SetSceneObjects(false);
        if (_tool == Tool.Terrain) StopBrush();
        if (_tool == Tool.Paths) { _drawPts = null; _dragPt = -1; _gl.Invalidate(); }
        if (_tool == Tool.Teleports) { _placing = null; _tpd = null; _gl.Invalidate(); }
        if (_tool == Tool.Zones) CloseZones();
        if (_tool == Tool.Domains) CloseDomains();
        if (_tool == Tool.Create) { CancelPlacing(); if (_shopWin is not null) _shopWin.Visible = false; if (_forgeWin is not null) _forgeWin.Visible = false; }
        _tool = Tool.None;
        _toolBack.Visible = false;
        _toolTitle.Text = "       " + Loc.T("tools.title", "Ferramentas");
        _toolTitle.Image = UiKit.Icon("", Color.White, 18);   // chave inglesa antes do texto
        LayoutTools();
        UpdateInfoCard();
    }

    /// <summary>Liga/desliga a visão de edição do cenário (pontos de efeitos, sons e bichos; clique escolhe).</summary>
    void SetSceneObjects(bool on)
    {
        _sceneObjs = on; _gl.Invalidate();
        if (_sceneObjs) { LoadSceneForEdit(); DraftOpen(); } else { _sceneSel = null; ShowObjPanel(); }
        _status.Text = ObjScene is not { } sc ? Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente.")
            : _sceneObjs ? string.Format(Loc.T("world.objsOn2", "Edição do cenário ligada: {0} efeitos, {1} sons, {2} grupos de bichos — clique num ponto para escolher"), sc.Effects.Count, sc.Sounds.Count, sc.Critters.Count)
            : "";
        UpdateSideState();
    }

    /// <summary>Para a captura (--world-shot): abre a ferramenta Editar cenário ou volta ao menu.</summary>
    internal void ShotTool(int tool) { CloseTool(); if (tool == 5) { MapTag = MapTag == 0 ? 1 : MapTag; OpenTool(Tool.Teleports); return; } if (tool == 6) { MapTag = MapTag == 0 ? 1 : MapTag; OpenTool(Tool.Teleports); NewTeleport(); _tpFilling = true; _tpName.Text = "Vila Nova"; _tpFilling = false; _tpd!.At = new OpenTK.Mathematics.Vector3(100, 220, 100); _tpd.BaseNpc = 2171; _tpd.NpcName = "Teleportadora"; _tpd.Stops = [new TeleportStop(1001, 500, 0)]; UpdateTeleportPanel(); return; } if (tool == 4) { OpenTool(Tool.Create); PickKind(NpcKind.Seller); return; } if (tool == 7) { OpenTool(Tool.Terrain); return; } if (tool == 1) OpenTool(Tool.Scene); else if (tool == 2) OpenTool(Tool.Paths); else if (tool == 3) OpenTool(Tool.Quests); }

    /// <summary>Aviso de mudança não salva no painel da ferramenta.</summary>
    void UpdateSideState()
    {
        bool dirty = WorldDirty;
        _sceneState.Text = !_sceneObjs ? "" : _ecw is null ? (_ecwWhy ?? "") : dirty
            ? Loc.T("world.dirtyLine", "● Mudanças não salvas — use Salvar mundo.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _sceneState.ForeColor = dirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
    }
}

/// <summary>Botão que não pega o foco: o teclado (W/A/S/D, Espaço, Enter) continua indo para o 3D (usuário 01/11).</summary>
internal sealed class NoFocusButton : Button
{
    public NoFocusButton() { SetStyle(ControlStyles.Selectable, false); TabStop = false; }
}
