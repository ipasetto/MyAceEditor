using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta Criar (teste 28): criatura, NPC, vendedor ou fornalha em passos, com a janela do jogo no centro para montar a
/// loja ou a fabricação. Nada entra nos arquivos até o clique no chão. Spec: docs/superpowers/specs/2026-11-06-criar-npc-3d-design.md.
/// </summary>
internal sealed partial class WorldView
{
    readonly (NpcKind Kind, string Glyph, string Key, string Pt)[] _kinds =
    [
        (NpcKind.Creature, "", "create.creature", "Criatura"), (NpcKind.Npc, "", "create.npc", "NPC"),
        (NpcKind.Seller, "", "create.seller", "Vendedor (com loja)"), (NpcKind.Forge, "", "create.forge", "Fornalha (fabricação)"), (NpcKind.Teleporter, "", "create.teleporter", "Teleportador"),
    ];
    readonly List<(NpcKind Kind, Button Btn)> _kindBtns = [];
    readonly Label _whoName = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 2, 0, 4) };
    readonly Label _nameTitle = new() { AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 2) };
    readonly TextBox _nameBox = new() { Width = 250, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly Label _placeWhere = new() { AutoSize = true, MaximumSize = new Size(245, 0), Margin = new Padding(0, 2, 0, 4) };
    readonly Label _createError = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 4, 0, 0) };
    Label _stepWho = null!, _stepGoods = null!, _stepWhere = null!;
    Button _whoBtn = null!, _goodsBtn = null!, _placeBtn = null!, _confirmBtn = null!, _abandonBtn = null!;
    Control[] _whereOrder = [];
    int _editing, _lastCreated;
    Button _fixBtn = null!;
    FlowLayoutPanel _kindsCard = null!, _editCard = null!;
    Vector3? _placeAt;
    NpcKind? _createKind;
    int _createBase;
    GameServiceWindow? _shopWin, _forgeWin;
    List<TeleportStop> _stops = [];
    int _stopDiscover;
    Action<Vector3>? _placing;

    /// <summary>Põe um ponto novo no mundo: lugar, ID e se é NPC.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] internal Action<Vector3, int, bool>? PlaceCreature { get; set; }

    void InitCreatePage()
    {
        var kinds = ToolCard();
        kinds.Controls.Add(SectionLabel(Loc.T("create.step1", "1. O que criar")));
        foreach (var (kind, glyph, key, pt) in _kinds)
        {
            var b = SmallButton(glyph, Loc.T(key, pt), () => PickKind(kind));
            _kindBtns.Add((kind, b));
            kinds.Controls.Add(b);
        }
        _kindsCard = kinds;
        _createPage.Controls.Add(kinds);

        _editCard = ToolCard();
        _fixBtn = SmallButton("\uE70F", "", () => LoadForEdit(_lastCreated));
        _editCard.Controls.Add(SectionLabel(Loc.T("create.editTitle", "Ou mudar um NPC que já existe")));
        _editCard.Controls.Add(_fixBtn);
        _editCard.Controls.Add(SmallButton("\uE7C9", Loc.T("tp.clickNpc", "Clicar no NPC no mundo"), () => PickInWorld(LoadForEdit)));
        _editCard.Controls.Add(SmallButton("\uE721", Loc.T("tp.searchNpc", "Procurar na lista…"), () => { if (_host?.PickCreatureId(this, true) is { } id) LoadForEdit(id); }));
        _createPage.Controls.Add(_editCard);

        var who = ToolCard();
        _stepWho = SectionLabel(Loc.T("create.step2", "2. Quem é"));
        _whoBtn = SmallButton("", Loc.T("create.pick", "Escolher…"), PickWho);
        _nameTitle.Text = Loc.T("create.name", "Nome do NPC");
        _nameBox.KeyDown += (_, e) => e.SuppressKeyPress = e.KeyCode == Keys.Enter;
        foreach (var c in new Control[] { _stepWho, _whoName, _whoBtn, _nameTitle, _nameBox }) who.Controls.Add(c);
        _createPage.Controls.Add(who);

        var goods = ToolCard();
        _stepGoods = SectionLabel(Loc.T("create.step3", "3. O que ele oferece"));
        _goodsBtn = SmallButton("", "", OpenGoods);
        goods.Controls.Add(_stepGoods); goods.Controls.Add(_goodsBtn);
        _createPage.Controls.Add(goods);

        var where = ToolCard();
        _stepWhere = SectionLabel(Loc.T("create.stepWhere", "Onde fica"));
        _placeBtn = SmallButton("\uE707", Loc.T("create.place", "Escolher o lugar no mundo"), StartPlacing);
        _placeWhere.ForeColor = Color.Gainsboro;
        _confirmBtn = SmallButton("\uE73E", Loc.T("create.confirm", "Confirmar a criação"), ConfirmCreate, main: true);
        _abandonBtn = SmallButton("\uE711", Loc.T("create.abandon", "Abandonar a criação"), () => { if (AskAbandon()) UpdateCreatePanel(); });
        _whereOrder = [_stepWhere, _placeBtn, _placeWhere, _confirmBtn, _abandonBtn, _createError];
        foreach (var c in _whereOrder) where.Controls.Add(c);
        _createPage.Controls.Add(where);

        var end = ToolCard();
        end.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        end.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        _createPage.Controls.Add(end);
        _gl.Resize += (_, _) => CenterServiceWindow();
        FitToScroll(_createPage);
        UpdateCreatePanel();
    }

    GameServiceWindow? ActiveServiceWindow => _createKind == NpcKind.Seller ? _shopWin : _createKind == NpcKind.Forge ? _forgeWin : null;

    void PickKind(NpcKind kind)
    {
        CancelPlacing();
        if (_createKind != kind) { _createBase = 0; if (_shopWin is not null) _shopWin.Visible = false; if (_forgeWin is not null) _forgeWin.Visible = false; }
        _createKind = kind;
        _createError.Text = "";
        UpdateCreatePanel();
    }

    void PickWho()
    {
        if (_host?.PickCreatureId(this, _createKind != NpcKind.Creature) is not { } id) return;
        _createBase = id;
        if (_createKind != NpcKind.Creature && _nameBox.Text.Trim().Length == 0) _nameBox.Text = _host.NameOf(id) ?? "";
        UpdateCreatePanel();
    }

    void OpenStops()
    {
        var points = UsablePoints();
        if (points.Count == 0) { _createError.Text = Loc.T("tp.noPoints", "Este cliente não tem pontos de teleporte para escolher."); UpdateCreatePanel(); return; }
        using var w = new TeleportStopsWindow(points, _stops, _stopDiscover);
        if (w.ShowDialog(this) != DialogResult.OK) return;
        _stops = w.Stops; _stopDiscover = w.Discover;
        UpdateCreatePanel();
    }

    GameServiceWindow? ServiceWindow(bool forge)
    {
        if (_host?.ElementDir is not { } dir) return null;
        var win = forge ? _forgeWin : _shopWin;
        if (win is not null) return win;
        win = new GameServiceWindow(dir, forge, _host) { Visible = false };
        win.Changed += UpdateCreatePanel;
        win.VisibleChanged += (_, _) => UpdateCreatePanel();
        if (forge) _forgeWin = win; else _shopWin = win;
        Controls.Add(win);
        return win;
    }

    /// <summary>Abre o NPC que já existe nos mesmos passos da criação (teste 8). Mudar não cria nada novo, só grava por cima.</summary>
    void LoadForEdit(int id)
    {
        if (_host?.LoadNpc(id) is not { } d) { _createError.Text = Loc.T("create.notNpc", "Esse ID não é de um NPC."); UpdateCreatePanel(); return; }
        if (!AskAbandon()) return;
        _editing = id; _createKind = d.Kind; _createBase = id; _nameBox.Text = d.Name; _stops = d.Stops; _stopDiscover = d.Discover;
        if (d.Shop is { } shop) ServiceWindow(false)?.Load(shop);
        if (d.Make is { } make) ServiceWindow(true)?.Load(make);
        _status.Text = "";
        UpdateCreatePanel();
    }

    void SaveEdit()
    {
        if (_host is null) return;
        var d = Draft();
        var errors = _host.ValidateNpc(d);
        _createError.Text = string.Join("\n", errors.Take(4));
        if (errors.Count > 0) { UpdateCreatePanel(); return; }
        bool copy = false;
        if (_host.ServiceUsersOf(d) is > 1 and var users)
        {
            var r = MessageBox.Show(this, string.Format(Loc.T("create.sharedAsk", "Este serviço é usado por {0} NPCs.\n\nSim: muda só este, com uma cópia.\nNão: muda todos."), users), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            copy = r == DialogResult.Yes;
        }
        if (_host.UpdateNpc(d, copy) is { } error) { _createError.Text = error; UpdateCreatePanel(); return; }
        int id = _editing;
        ResetCreate();
        _status.Text = string.Format(Loc.T("create.edited", "{0} alterado. Ctrl+Z desfaz. Salve e envie ao servidor para ver no jogo."), _host.NameOf(id) ?? id.ToString());
        UpdateCreatePanel();
    }

    void OpenGoods()
    {
        if (_createKind == NpcKind.Teleporter) { OpenStops(); return; }
        bool forge = _createKind == NpcKind.Forge;
        if (ServiceWindow(forge) is not { } win) return;
        if (win.Visible) { win.Visible = false; return; }   // o mesmo botão fecha
        win.Visible = true;
        CenterServiceWindow();
        win.BringToFront();
        _tools.BringToFront();
    }

    void CenterServiceWindow()
    {
        foreach (var w in new[] { _shopWin, _forgeWin })
            if (w is not null) w.Location = new Point(Math.Max(0, _gl.Left + (_gl.Width - w.Width) / 2 - 100), Math.Max(_gl.Top, _gl.Top + (_gl.Height - w.Height) / 2));
    }

    NpcDraft Draft() => new()
    {
        Kind = _createKind ?? NpcKind.Creature, BaseId = _createBase, Name = _nameBox.Text,
        Shop = _createKind == NpcKind.Seller ? _shopWin?.Shop : null, Make = _createKind == NpcKind.Forge ? _forgeWin?.Make : null,
        Stops = _createKind == NpcKind.Teleporter ? _stops : [], Discover = _createKind == NpcKind.Teleporter ? _stopDiscover : 0, Editing = _editing,
    };

    bool HasCreateDraft => _createKind is not null && _editing == 0 && (_createBase > 0 || _nameBox.Text.Length > 0 || _placeAt is not null || _shopWin?.Shop.Count > 0 || _forgeWin?.Make.Count > 0 || _stops.Count > 0);

    /// <summary>Abandonar o rascunho: pergunta se há algo feito. Falso = o usuário quis continuar.</summary>
    bool AskAbandon()
    {
        if (HasCreateDraft && MessageBox.Show(this, Loc.T("create.abandonAsk", "Abandonar esta criação? O que foi escolhido até aqui se perde."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        ResetCreate();
        return true;
    }

    void ResetCreate()
    {
        _placing = null; _placeAt = null; _createBase = 0; _createKind = null; _editing = 0; _nameBox.Text = ""; _createError.Text = ""; _stops = []; _stopDiscover = 0;
        if (_shopWin is not null) { _shopWin.Visible = false; _shopWin.Load(new ShopDraft()); }
        if (_forgeWin is not null) { _forgeWin.Visible = false; _forgeWin.Load(new MakeDraft()); }
        _gl.Invalidate();
    }

    void StartPlacing()
    {
        if (_placing is not null) { CancelPlacing(); return; }
        _placing = at => { _placing = null; _placeAt = at; _status.Text = Loc.T("create.placed", "Lugar marcado. Confira e clique em Confirmar a criação."); UpdateCreatePanel(); _gl.Invalidate(); };
        if (ActiveServiceWindow is { } win) win.Visible = false;
        _status.Text = Loc.T("create.clickGround", "Clique no chão onde ele vai ficar (Esc cancela).");
        UpdateCreatePanel();
    }

    void CancelPlacing()
    {
        if (_placing is null) return;
        _placing = null;
        _status.Text = "";
        UpdateCreatePanel();
        if (_tool == Tool.Teleports) UpdateTeleportPanel();
    }

    void ConfirmCreate()
    {
        if (_editing > 0) { SaveEdit(); return; }
        if (_host is null || _placeAt is not { } at) return;
        var errors = _host.ValidateNpc(Draft());
        _createError.Text = string.Join("\n", errors.Take(4));
        if (errors.Count > 0) { UpdateCreatePanel(); return; }
        var d = Draft();
        int id = d.BaseId;
        if (d.Kind != NpcKind.Creature)
        {
            if (_host.BuildNpc(d) is not { } made) { _createError.Text = Loc.T("create.failed", "Não foi possível criar: confira o elements.data aberto."); UpdateCreatePanel(); return; }
            id = made;
        }
        PlaceCreature?.Invoke(at, id, _host.IsNpc(id));
        RefreshMarkers();
        if (d.Kind != NpcKind.Creature) _lastCreated = id;
        var kind = _createKind;
        ResetCreate();
        _createKind = kind;
        _status.Text = string.Format(Loc.T("create.done", "{0} criado (ID {1}). Ctrl+Z tira o ponto. Salve e envie ao servidor para ver no jogo."), _host.NameOf(id) ?? id.ToString(), id);
        UpdateCreatePanel();
    }

    /// <summary>Lugar escolhido: anel verde no chão até confirmar.</summary>
    void DrawPlaceMark()
    {
        if ((_tool == Tool.Teleports ? _tpd?.At : _tool == Tool.Create ? _placeAt : null) is not { } p) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.Color3(0.16f, 0.86f, 0.35f); GL.LineWidth(3);
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < 40; i++) { float a = i / 40f * MathF.Tau; GL.Vertex3(p.X + MathF.Cos(a) * 2.5f, p.Y + 0.2f, -(p.Z + MathF.Sin(a) * 2.5f)); }
        GL.End();
        GL.Begin(PrimitiveType.Lines); GL.Vertex3(p.X, p.Y, -p.Z); GL.Vertex3(p.X, p.Y + 8, -p.Z); GL.End();
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }

    void UpdateCreatePanel()
    {
        var kind = _createKind;
        foreach (var (k, b) in _kindBtns) b.BackColor = k == kind ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        bool any = kind is not null, named = any && kind != NpcKind.Creature, goods = kind is NpcKind.Seller or NpcKind.Forge or NpcKind.Teleporter, editing = _editing > 0;
        _kindsCard.Visible = !editing;
        _editCard.Visible = !any;
        _fixBtn.Visible = _lastCreated > 0;
        if (_lastCreated > 0) _fixBtn.Text = "  " + string.Format(Loc.T("create.fix", "Corrigir {0}"), _host?.NameOf(_lastCreated) ?? _lastCreated.ToString());
        _stepWho.Visible = any;
        _whoBtn.Visible = any && !editing;
        _stepWho.Text = kind == NpcKind.Creature ? Loc.T("create.step2", "2. Quem é") : Loc.T("create.step2Base", "2. Aparência e nome");
        _whoName.Visible = any;
        _whoName.Text = _createBase > 0 ? _host?.NameOf(_createBase) ?? _createBase.ToString() : Loc.T("create.nobody", "Ninguém escolhido");
        _whoBtn.Text = "  " + (kind == NpcKind.Creature ? Loc.T("create.pick", "Escolher…") : Loc.T("create.pickBase", "Escolher o NPC de base…"));
        _nameTitle.Visible = _nameBox.Visible = named;
        _stepGoods.Visible = _goodsBtn.Visible = goods;
        _goodsBtn.Text = "  " + (kind == NpcKind.Teleporter ? string.Format(Loc.T("create.openStops", "Escolher os destinos ({0})"), _stops.Count)
            : kind == NpcKind.Forge
            ? ActiveServiceWindow is { Visible: true } ? Loc.T("create.closeForge", "Fechar a fabricação") : string.Format(Loc.T("create.openForge", "Montar a fabricação ({0} receitas)"), _forgeWin?.Make.Count ?? 0)
            : ActiveServiceWindow is { Visible: true } ? Loc.T("create.closeShop", "Fechar a loja") : string.Format(Loc.T("create.openShop", "Montar a loja ({0} itens)"), _shopWin?.Shop.Count ?? 0));
        _stepWhere.Visible = _abandonBtn.Visible = any;
        _placeBtn.Visible = any && !editing;
        _placeWhere.Visible = _placeAt is not null;
        _confirmBtn.Visible = _placeAt is not null || editing;
        _confirmBtn.Text = "  " + (editing ? Loc.T("create.saveEdit", "Salvar as mudanças") : Loc.T("create.confirm", "Confirmar a criação"));
        _abandonBtn.Text = "  " + (editing ? Loc.T("create.stopEdit", "Cancelar") : Loc.T("create.abandon", "Abandonar a criação"));
        if (_placeAt is { } at) _placeWhere.Text = FormattableString.Invariant($"{at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}");
        _stepWhere.Text = editing ? Loc.T("create.stepSave", "Terminar") : (goods ? "4. " : "3. ") + Loc.T("create.stepWhere", "Onde fica");
        _placeBtn.Text = "  " + (_placing is not null ? Loc.T("create.placing", "Clique no chão… (cancelar)") : _placeAt is not null ? Loc.T("create.placeAgain", "Trocar o lugar") : Loc.T("create.place", "Escolher o lugar no mundo"));
        KeepOrder(_stepWhere.Parent!, _whereOrder);
        if (_tool == Tool.Create) LayoutTools();
    }
}
