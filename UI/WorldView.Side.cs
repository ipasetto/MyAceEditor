namespace MyAceEditor.UI;

/// <summary>
/// Menu lateral do Mundo 3D (regra de layout: barra lateral, nada de filtros espremidos em cima): Mostrar (tipos de ponto,
/// caminhos, grama), Editar cenário (passo a passo + objeto escolhido + desfazer/salvar) e Atalhos. Recolhe numa faixa de ícones.
/// </summary>
internal sealed partial class WorldView
{
    const int SideOpen = 250, SideClosed = 44;
    readonly Panel _side = new() { Dock = DockStyle.Left, Width = SideOpen, BackColor = Color.FromArgb(24, 27, 33) };
    readonly FlowLayoutPanel _sideBody = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 4, 4, 8) };
    readonly Label _sceneState = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Silver, Margin = new Padding(3, 4, 3, 4) };
    Button? _editBtn;
    readonly List<(MarkKind Kind, string Key, string Pt, string Tip, Color Color)> _filterDefs = new()
    {
        (MarkKind.Monster, "world.fMonster", "Monstros", "Pontos de monstro.", Color.FromArgb(239, 83, 80)),
        (MarkKind.Npc, "world.fNpc", "NPCs comuns", "NPCs sem loja.", Color.FromArgb(100, 181, 246)),
        (MarkKind.Merchant, "world.fMerchant", "Comerciantes", "NPCs com serviço de venda.", Color.FromArgb(255, 202, 40)),
        (MarkKind.Guard, "world.fGuard", "Guardas", "NPC em ponto de monstro (nome vermelho).", Color.FromArgb(198, 40, 40)),
        (MarkKind.Mine, "world.fMine", "Coletáveis", "Minas, plantas e outros coletáveis.", Color.FromArgb(102, 187, 106)),
        (MarkKind.Event, "world.fEvent", "Eventos", "Pontos ligados por gatilho com data/hora (nome verde). Aparecem se o tipo também estiver marcado.", Color.FromArgb(0, 230, 118)),
        (MarkKind.Trigger, "world.fTrigger", "Gatilhos", "Pontos ligados por gatilho sem agenda: fase de DG, GM (nome lilás). Aparecem se o tipo também estiver marcado.", Color.FromArgb(186, 104, 200)),
    };

    void InitSidePanel()
    {
        var tip = new ToolTip();
        Disposed += (_, _) => tip.Dispose();
        // cabeçalho: título + botão de recolher
        var head = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(32, 36, 44) };
        var title = new Label { Text = Loc.T("world.side", "Ferramentas"), Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
        var fold = new Button { Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Image = UiKit.Icon("", Color.White, 16), Cursor = Cursors.Hand };
        fold.FlatAppearance.BorderSize = 0;
        tip.SetToolTip(fold, Loc.T("world.sideFold", "Recolher / abrir o menu"));
        head.Controls.Add(title); head.Controls.Add(fold);
        _side.Controls.Add(_sideBody); _side.Controls.Add(head);

        Label Section(string glyph, string key, string pt)
        {
            var l = new Label { Text = "    " + Loc.T(key, pt), AutoSize = false, Width = 225, Height = 26, ForeColor = UiKit.Accent, Font = new Font("Segoe UI Semibold", 9.5f),
                Image = UiKit.Icon(glyph, UiKit.Accent, 16), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 10, 0, 2) };
            _sideBody.Controls.Add(l);
            return l;
        }
        Button Toggle(string text, string tipText, Color dot, Func<bool> get, Action<bool> set)
        {
            var b = new Button { Text = "  " + text, Width = 225, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(32, 36, 44),
                TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Cursor = Cursors.Hand, Margin = new Padding(0, 1, 0, 1) };
            b.FlatAppearance.BorderSize = 0;
            void Paint() => b.Image = DarkBarRenderer.Dot(get() ? dot : Color.FromArgb(70, 70, 70));
            b.Click += (_, _) => { set(!get()); Paint(); _gl.Focus(); };
            Paint(); tip.SetToolTip(b, tipText);
            _sideBody.Controls.Add(b);
            return b;
        }
        Button Action(string glyph, string key, string pt, Action a, bool main = false)
        {
            var b = new Button { Text = "  " + Loc.T(key, pt), Width = 225, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(40, 45, 54),
                Image = UiKit.Icon(glyph, Color.White, 16), TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2) };
            b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(60, 66, 78);
            b.Click += (_, _) => { a(); _gl.Focus(); };
            _sideBody.Controls.Add(b);
            return b;
        }

        // ---- Mostrar ----
        Section("", "world.show2", "Mostrar");
        foreach (var f in _filterDefs)
            Toggle(Loc.T(f.Key, f.Pt), Loc.T(f.Key + ".h", f.Tip), f.Color, () => (_show & f.Kind) != 0, on => { _show = on ? _show | f.Kind : _show & ~f.Kind; RefreshMarkers(); });
        Toggle(Loc.T("world.fPaths", "Caminhos de patrulha (P)"), Loc.T("world.fPaths.h", "Todos os caminhos perto da câmera; o do ponto escolhido aparece sempre."), Color.FromArgb(205, 140, 255),
            () => _allPaths, on => { _allPaths = on; _gl.Invalidate(); });
        Toggle(Loc.T("world.fGrass", "Grama (G)"), Loc.T("world.fGrass.h", "Mostra ou esconde a grama."), Color.FromArgb(120, 200, 90),
            () => _grass.Visible, on => { _grass.Visible = on; _gl.Invalidate(); });

        // ---- Editar cenário ----
        Section("", "world.editScene", "Editar cenário");
        _editBtn = Action("", "world.editOn", "Ligar edição do cenário (O)", () => SetSceneObjects(!_sceneObjs), main: true);
        _sideBody.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Gainsboro, Margin = new Padding(3, 4, 3, 4),
            Text = Loc.T("world.editSteps", "1. Ligue a edição: aparecem pontos laranja (efeitos), azuis (sons) e verdes (bichos).\n2. Clique num ponto para escolher.\n3. Mude X / altura / Z abaixo, ou use Clonar e Apagar. Botão direito no chão: mover para lá.\n4. Salvar cenário grava (com backup). Ctrl+Z desfaz."),
        });
        _objPanel.Visible = false; _objPanel.Width = 225; _objPanel.Margin = new Padding(0, 4, 0, 4);
        _sideBody.Controls.Add(_objPanel);
        _sideBody.Controls.Add(_sceneState);
        Action("", "world.undo", "Desfazer (Ctrl+Z)", () => UndoAny());
        Action("", "world.redo", "Refazer (Ctrl+Y)", () => RedoAny());
        _sideBody.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Gray, Margin = new Padding(3, 6, 3, 4), Font = new Font("Segoe UI", 8f),
            Text = Loc.T("world.lockedNote", "Construções, modelos e árvores: próxima etapa (mexem no .bmd do litmodels, no .ecbsd e na colisão do servidor, que será refeita ao salvar)."),
        });

        // ---- Atalhos ----
        Section("", "world.keys", "Atalhos");
        Action("", "world.keysOpen", "Ver atalhos e legenda (H)", ToggleLegend);

        fold.Click += (_, _) =>
        {
            bool open = _side.Width == SideClosed;
            _side.Width = open ? SideOpen : SideClosed;
            _sideBody.Visible = open; title.Visible = open;
            fold.Image = UiKit.Icon(open ? "" : "", Color.White, 16);
            _gl.Focus();
        };
        Controls.Add(_side);
        UpdateSideState();
    }

    /// <summary>Liga/desliga a edição do cenário (botão do menu ou tecla O).</summary>
    void SetSceneObjects(bool on)
    {
        _sceneObjs = on; _gl.Invalidate();
        if (_sceneObjs) LoadSceneForEdit(); else { _sceneSel = null; ShowObjPanel(); }
        _status.Text = ObjScene is not { } sc ? Loc.T("world.noScene", "Este mapa não tem .ecwld no cliente.")
            : _sceneObjs ? string.Format(Loc.T("world.objsOn2", "Edição do cenário ligada: {0} efeitos, {1} sons, {2} grupos de bichos — clique num ponto para escolher"), sc.Effects.Count, sc.Sounds.Count, sc.Critters.Count)
            : Loc.T("world.objsOff", "Objetos do cenário escondidos — O mostra");
        UpdateSideState();
    }

    /// <summary>Texto do botão de edição e o aviso de mudança não salva no menu.</summary>
    void UpdateSideState()
    {
        if (_editBtn is not null)
        {
            _editBtn.Text = "  " + (_sceneObjs ? Loc.T("world.editOff", "Desligar edição do cenário (O)") : Loc.T("world.editOn", "Ligar edição do cenário (O)"));
            _editBtn.BackColor = _sceneObjs ? Color.FromArgb(40, 45, 54) : UiKit.Accent;
        }
        _sceneState.Text = !_sceneObjs ? "" : _ecw is null ? (_ecwWhy ?? "") : SceneDirty
            ? Loc.T("world.sceneDirtyLine", "● Mudanças não salvas — use Salvar cenário.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _sceneState.ForeColor = SceneDirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
    }
}
