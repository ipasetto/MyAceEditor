using MyAceEditor.PerfectWorld;
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
        (NpcKind.Seller, "", "create.seller", "Vendedor (com loja)"), (NpcKind.Forge, "", "create.forge", "Fornalha (fabricação)"),
    ];
    readonly List<(NpcKind Kind, Button Btn)> _kindBtns = [];
    readonly Label _whoName = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 2, 0, 4) };
    readonly Label _nameTitle = new() { AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 2) };
    readonly TextBox _nameBox = new() { Width = 250, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly Label _createError = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 4, 0, 0) };
    Label _stepWho = null!, _stepGoods = null!, _stepWhere = null!;
    Button _whoBtn = null!, _goodsBtn = null!, _placeBtn = null!;
    NpcKind? _createKind;
    int _createBase;
    GameServiceWindow? _shopWin, _forgeWin;
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
        _createPage.Controls.Add(kinds);

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
        _placeBtn = SmallButton("", Loc.T("create.place", "Escolher o lugar no mundo"), StartPlacing, main: true);
        where.Controls.Add(_stepWhere); where.Controls.Add(_placeBtn); where.Controls.Add(_createError);
        _createPage.Controls.Add(where);

        var end = ToolCard();
        end.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        end.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        _createPage.Controls.Add(end);
        _gl.Resize += (_, _) => CenterServiceWindow();
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

    void OpenGoods()
    {
        if (_host?.ElementDir is not { } dir) return;
        bool forge = _createKind == NpcKind.Forge;
        var win = forge ? _forgeWin : _shopWin;
        if (win is null)
        {
            win = new GameServiceWindow(dir, forge, _host);
            win.Changed += UpdateCreatePanel;
            win.VisibleChanged += (_, _) => UpdateCreatePanel();
            if (forge) _forgeWin = win; else _shopWin = win;
            Controls.Add(win);
        }
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
    };

    void StartPlacing()
    {
        if (_placing is not null) { CancelPlacing(); return; }
        if (_host is null) return;
        var errors = _host.ValidateNpc(Draft());
        _createError.Text = string.Join("\n", errors.Take(4));
        LayoutTools();
        if (errors.Count > 0) return;
        _placing = FinishCreate;
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
    }

    void FinishCreate(Vector3 at)
    {
        _placing = null;
        var d = Draft();
        int id = d.BaseId;
        if (d.Kind != NpcKind.Creature)
        {
            if (_host?.BuildNpc(d) is not { } made) { _createError.Text = Loc.T("create.failed", "Não foi possível criar: confira o elements.data aberto."); UpdateCreatePanel(); return; }
            id = made;
        }
        PlaceCreature?.Invoke(at, id, _host?.IsNpc(id) == true);
        RefreshMarkers(); _gl.Invalidate();
        _status.Text = string.Format(Loc.T("create.done", "{0} criado (ID {1}). Ctrl+Z tira o ponto. Salve e envie ao servidor para ver no jogo."), _host?.NameOf(id) ?? id.ToString(), id);
        _shopWin?.Load(new ShopDraft()); _forgeWin?.Load(new MakeDraft());
        _nameBox.Text = ""; _createBase = 0;
        UpdateCreatePanel();
    }

    void UpdateCreatePanel()
    {
        var kind = _createKind;
        foreach (var (k, b) in _kindBtns) b.BackColor = k == kind ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        bool any = kind is not null, named = any && kind != NpcKind.Creature, goods = kind is NpcKind.Seller or NpcKind.Forge;
        _stepWho.Visible = _whoBtn.Visible = any;
        _stepWho.Text = kind == NpcKind.Creature ? Loc.T("create.step2", "2. Quem é") : Loc.T("create.step2Base", "2. Aparência e nome");
        _whoName.Visible = any;
        _whoName.Text = _createBase > 0 ? _host?.NameOf(_createBase) ?? _createBase.ToString() : Loc.T("create.nobody", "Ninguém escolhido");
        _whoBtn.Text = "  " + (kind == NpcKind.Creature ? Loc.T("create.pick", "Escolher…") : Loc.T("create.pickBase", "Escolher o NPC de base…"));
        _nameTitle.Visible = _nameBox.Visible = named;
        _stepGoods.Visible = _goodsBtn.Visible = goods;
        _goodsBtn.Text = "  " + (kind == NpcKind.Forge
            ? string.Format(Loc.T("create.openForge", "Montar a fabricação ({0} receitas)"), _forgeWin?.Make.Count ?? 0)
            : string.Format(Loc.T("create.openShop", "Montar a loja ({0} itens)"), _shopWin?.Shop.Count ?? 0));
        _stepWhere.Visible = _placeBtn.Visible = any;
        _stepWhere.Text = (goods ? "4. " : "3. ") + Loc.T("create.stepWhere", "Onde fica");
        _placeBtn.Text = "  " + (_placing is not null ? Loc.T("create.placing", "Clique no chão… (cancelar)") : Loc.T("create.place", "Escolher o lugar no mundo"));
        if (_tool == Tool.Create) LayoutTools();
    }
}
