using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta NPCs e monstros (teste 28). Começa com quem está selecionado no mundo e dois caminhos: Criar novo (passos com a
/// janela do jogo no centro) e Editar o selecionado (os mesmos passos, já preenchidos; monstro abre no Elements). Loja, fabricação
/// e teleporte são funções separadas: na edição cada uma entra e sai sozinha. Nada entra nos arquivos antes de confirmar.
/// Spec: docs/superpowers/specs/2026-11-06-criar-npc-3d-design.md.
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
    Button _whoBtn = null!, _goodsBtn = null!, _forgeBtn = null!, _stopsBtn = null!, _dropShop = null!, _dropForge = null!, _dropStops = null!, _placeBtn = null!, _confirmBtn = null!, _abandonBtn = null!, _editBtn = null!;
    Control[] _whereOrder = [];
    int _editing, _target;
    bool _creating, _offerShop, _offerForge, _offerStops;
    readonly Label _targetName = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 2, 0, 6) };
    FlowLayoutPanel _kindsCard = null!, _homeCard = null!, _whoCard = null!, _goodsCard = null!, _whereCard = null!;
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
        _homeCard = ToolCard();
        _homeCard.Controls.Add(SectionLabel(Loc.T("npc.selected", "Selecionado")));
        _homeCard.Controls.Add(_targetName);
        _homeCard.Controls.Add(SmallButton("\uE710", Loc.T("npc.new", "Criar novo"), () => { _creating = true; UpdateCreatePanel(); }, main: true));
        _editBtn = SmallButton("\uE70F", "", EditTarget);
        _homeCard.Controls.Add(_editBtn);
        _homeCard.Controls.Add(SmallButton("\uE721", Loc.T("tp.searchNpc", "Procurar na lista…"), () => { if (_host?.PickCreatureId(this, false) is { } id) SelectTarget(id); }));
        _createPage.Controls.Add(_homeCard);

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

        var who = _whoCard = ToolCard();
        _stepWho = SectionLabel(Loc.T("create.step2", "2. Quem é"));
        _whoBtn = SmallButton("", Loc.T("create.pick", "Escolher…"), PickWho);
        _nameTitle.Text = Loc.T("create.name", "Nome do NPC");
        _nameBox.KeyDown += (_, e) => e.SuppressKeyPress = e.KeyCode == Keys.Enter;
        foreach (var c in new Control[] { _stepWho, _whoName, _whoBtn, _nameTitle, _nameBox }) who.Controls.Add(c);
        _createPage.Controls.Add(who);

        var goods = _goodsCard = ToolCard();
        _stepGoods = SectionLabel(Loc.T("create.step3", "3. O que ele oferece"));
        _goodsBtn = SmallButton("\uE719", "", () => OpenService(forge: false));
        _dropShop = SmallButton("\uE711", Loc.T("npc.dropShop", "Tirar a loja"), () => { _offerShop = false; if (_shopWin is not null) _shopWin.Visible = false; UpdateCreatePanel(); });
        _forgeBtn = SmallButton("\uE945", "", () => OpenService(forge: true));
        _dropForge = SmallButton("\uE711", Loc.T("npc.dropForge", "Tirar a fabricação"), () => { _offerForge = false; if (_forgeWin is not null) _forgeWin.Visible = false; UpdateCreatePanel(); });
        _stopsBtn = SmallButton("\uE8AB", "", () => { _offerStops = true; OpenStops(); UpdateCreatePanel(); });
        _dropStops = SmallButton("\uE711", Loc.T("npc.dropStops", "Tirar o teleporte"), () => { _offerStops = false; UpdateCreatePanel(); });
        foreach (var c in new Control[] { _stepGoods, _goodsBtn, _dropShop, _forgeBtn, _dropForge, _stopsBtn, _dropStops }) goods.Controls.Add(c);
        _createPage.Controls.Add(goods);

        var where = _whereCard = ToolCard();
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

    GameServiceWindow? ActiveServiceWindow => _shopWin is { Visible: true } ? _shopWin : _forgeWin is { Visible: true } ? _forgeWin : null;

    /// <summary>Quem o botão Editar vai abrir: clique no mundo, lista ou o que acabou de ser criado. Durante os passos não muda.</summary>
    void SelectTarget(int id)
    {
        if (_creating || _editing > 0) return;
        _target = id;
        UpdateCreatePanel();
    }

    /// <summary>NPC abre nos passos; monstro abre no editor de elements, já selecionado.</summary>
    void EditTarget()
    {
        if (_target <= 0) return;
        if (_host?.IsNpc(_target) == true) LoadForEdit(_target);
        else EditCreature?.Invoke(_target);
    }

    void PickKind(NpcKind kind)
    {
        CancelPlacing();
        if (_createKind != kind) { _createBase = 0; if (_shopWin is not null) _shopWin.Visible = false; if (_forgeWin is not null) _forgeWin.Visible = false; }
        _createKind = kind;
        _creating = true;
        _offerShop = kind == NpcKind.Seller; _offerForge = kind == NpcKind.Forge; _offerStops = kind == NpcKind.Teleporter;
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
        _offerShop = d.Shop is not null; _offerForge = d.Make is not null; _offerStops = d.Teleports;
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
        _target = id;
        _status.Text = string.Format(Loc.T("create.edited", "{0} alterado. Ctrl+Z desfaz. Salve e envie ao servidor para ver no jogo."), _host.NameOf(id) ?? id.ToString());
        UpdateCreatePanel();
    }

    void OpenService(bool forge)
    {
        if (forge) _offerForge = true; else _offerShop = true;
        if (ServiceWindow(forge) is not { } win) return;
        bool show = !win.Visible;   // o mesmo botão fecha
        if (_shopWin is not null) _shopWin.Visible = false;
        if (_forgeWin is not null) _forgeWin.Visible = false;
        if (show)
        {
            win.Visible = true;
            CenterServiceWindow();
            win.BringToFront();
            _tools.BringToFront();
        }
        UpdateCreatePanel();
    }

    void CenterServiceWindow()
    {
        foreach (var w in new[] { _shopWin, _forgeWin })
            if (w is not null) w.Location = new Point(Math.Max(0, _gl.Left + (_gl.Width - w.Width) / 2 - 100), Math.Max(_gl.Top, _gl.Top + (_gl.Height - w.Height) / 2));
    }

    NpcDraft Draft() => new()
    {
        // editando vale o que está ligado nas linhas de serviço, não o tipo com que ele abriu
        Kind = _editing > 0 ? NpcKind.Npc : _createKind ?? NpcKind.Creature, BaseId = _createBase, Name = _nameBox.Text,
        Shop = _offerShop ? _shopWin?.Shop ?? new ShopDraft() : null, Make = _offerForge ? _forgeWin?.Make ?? new MakeDraft() : null,
        Teleports = _offerStops, Stops = _offerStops ? _stops : [], Discover = _offerStops ? _stopDiscover : 0, Editing = _editing,
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
        _creating = _offerShop = _offerForge = _offerStops = false;
        if (_shopWin is not null) { _shopWin.Visible = false; _shopWin.Load(new ShopDraft()); }
        if (_forgeWin is not null) { _forgeWin.Visible = false; _forgeWin.Load(new MakeDraft()); }
        _gl.Invalidate();
    }

    void StartPlacing()
    {
        if (_placing is not null) { CancelPlacing(); return; }
        _placing = at => { _placing = null; _placeAt = at; _status.Text = Loc.T("create.placed", "Lugar marcado. Confira e clique em Confirmar a criação."); UpdateCreatePanel(); _gl.Invalidate(); };
        if (ActiveServiceWindow is { } open) open.Visible = false;
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
        ResetCreate();
        _target = id;   // volta ao começo com ele selecionado: Editar já corrige o que saiu errado
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
        bool editing = _editing > 0, flow = _creating || editing, any = kind is not null, named = any && kind != NpcKind.Creature;
        bool npc = _target > 0 && _host?.IsNpc(_target) == true;
        _homeCard.Visible = !flow;
        _kindsCard.Visible = _creating;
        _targetName.Text = _target > 0
            ? $"{_host?.NameOf(_target) ?? _target.ToString()}\n{(npc ? Loc.T("npc.isNpc", "NPC") : Loc.T("npc.isMonster", "Monstro"))} · ID {_target}"
            : Loc.T("npc.noneSelected", "Nenhum. Clique num NPC ou monstro no mundo.");
        _editBtn.Enabled = _target > 0;
        _editBtn.Text = "  " + (_target > 0 && !npc ? Loc.T("npc.editElements", "Editar no Elements") : Loc.T("npc.edit", "Editar o selecionado"));
        _whoCard.Visible = any;
        _whoBtn.Visible = any && !editing;
        _stepWho.Text = kind == NpcKind.Creature ? Loc.T("create.step2", "2. Quem é") : Loc.T("create.step2Base", "2. Aparência e nome");
        _whoName.Text = _createBase > 0 ? _host?.NameOf(_createBase) ?? _createBase.ToString() : Loc.T("create.nobody", "Ninguém escolhido");
        _whoBtn.Text = "  " + (kind == NpcKind.Creature ? Loc.T("create.pick", "Escolher…") : Loc.T("create.pickBase", "Escolher o NPC de base…"));
        _nameTitle.Visible = _nameBox.Visible = named;
        // criando, só o serviço do tipo escolhido; editando, os três: um NPC comum pode ganhar loja, e quem tem loja e fornalha mexe nas duas
        bool shopRow = editing || kind == NpcKind.Seller, forgeRow = editing || kind == NpcKind.Forge, stopsRow = editing || kind == NpcKind.Teleporter;
        _goodsCard.Visible = shopRow || forgeRow || stopsRow;
        _goodsBtn.Visible = shopRow; _forgeBtn.Visible = forgeRow; _stopsBtn.Visible = stopsRow;
        _dropShop.Visible = editing && _offerShop; _dropForge.Visible = editing && _offerForge; _dropStops.Visible = editing && _offerStops;
        _goodsBtn.Text = "  " + (!_offerShop ? Loc.T("npc.addShop", "Pôr uma loja") : _shopWin is { Visible: true } ? Loc.T("create.closeShop", "Fechar a loja") : string.Format(Loc.T("create.openShop", "Montar a loja ({0} itens)"), _shopWin?.Shop.Count ?? 0));
        _forgeBtn.Text = "  " + (!_offerForge ? Loc.T("npc.addForge", "Pôr uma fabricação") : _forgeWin is { Visible: true } ? Loc.T("create.closeForge", "Fechar a fabricação") : string.Format(Loc.T("create.openForge", "Montar a fabricação ({0} receitas)"), _forgeWin?.Make.Count ?? 0));
        _stopsBtn.Text = "  " + (!_offerStops ? Loc.T("npc.addStops", "Pôr teleporte") : string.Format(Loc.T("create.openStops", "Escolher os destinos ({0})"), _stops.Count));
        _whereCard.Visible = flow;
        _stepWhere.Visible = any;
        _abandonBtn.Visible = flow;
        _placeBtn.Visible = any && !editing;
        _placeWhere.Visible = _placeAt is not null;
        _confirmBtn.Visible = _placeAt is not null || editing;
        _confirmBtn.Text = "  " + (editing ? Loc.T("create.saveEdit", "Salvar as mudanças") : Loc.T("create.confirm", "Confirmar a criação"));
        _abandonBtn.Text = "  " + (editing ? Loc.T("create.stopEdit", "Cancelar") : any ? Loc.T("create.abandon", "Abandonar a criação") : Loc.T("npc.back", "Voltar"));
        if (_placeAt is { } at) _placeWhere.Text = FormattableString.Invariant($"{at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}");
        _stepWhere.Text = editing ? Loc.T("create.stepSave", "Terminar") : (_goodsCard.Visible ? "4. " : "3. ") + Loc.T("create.stepWhere", "Onde fica");
        _placeBtn.Text = "  " + (_placing is not null ? Loc.T("create.placing", "Clique no chão… (cancelar)") : _placeAt is not null ? Loc.T("create.placeAgain", "Trocar o lugar") : Loc.T("create.place", "Escolher o lugar no mundo"));
        KeepOrder(_stepWhere.Parent!, _whereOrder);
        if (_tool == Tool.Create) LayoutTools();
    }
}
