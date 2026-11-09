using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Painel da direita do Mundo 3D para criar quest (docs/QUESTS-3D.md): passos como atalhos (NPCs, Requisitos, Prêmios,
/// Textos, Resumo), pode pular entre eles; vale até Concluir ou Cancelar. NPCs são escolhidos clicando no mundo.
/// Tipo 1 = recado (NPC que dá → NPC que recebe).
/// </summary>
internal sealed class WorldQuestEditor : Panel
{
    static readonly Color Card = Color.FromArgb(39, 45, 54), Bg = Color.FromArgb(28, 32, 38), Bg2 = Color.FromArgb(36, 41, 49), Field = Color.FromArgb(18, 20, 24), Fg = Color.Gainsboro,
        Accent = Color.FromArgb(120, 190, 255), Bad = Color.FromArgb(255, 140, 120), Good = Color.FromArgb(140, 220, 140);

    readonly IQuestHost _host;
    readonly QuestWorld _world;
    QuestDraft _d;
    readonly List<RadioButton> _typeButtons = new();   // tipo da quest = ícone sobre o NPC
    // repetição (usuário 01/11): Tag 0 = uma vez, -1 = pode refazer (m_bCanRedo), 1..3 = 1 vez por dia/semana/mês (m_lAvailFrequency)
    readonly List<RadioButton> _repeatButtons = new();
    QuestOption? _redoOpt, _repeatOpt;
    static bool IsRepeatOption(QuestOption o) => o.Id is "redo" or "repeat";
    readonly Label _title = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly StepperBar _stepper = new();
    readonly Label _sub = new() { Dock = DockStyle.Bottom, Height = 18, ForeColor = Color.Silver, Font = new Font("Segoe UI", 8.5f), Padding = new Padding(36, 0, 0, 0) };
    readonly Panel _page = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 6, 14, 6) };
    readonly List<(Control Page, Action? OnShow)> _pages = new();
    Button _next = null!, _back = null!;
    readonly ToolTip _tip = new() { AutoPopDelay = 15000 };
    int _at;

    // controles do rascunho
    readonly NumericUpDown _id = Num(1, int.MaxValue), _giver = Num(0, int.MaxValue), _stepNpc = Num(0, int.MaxValue),
        _minLv = Num(0, 1000), _maxLv = Num(0, 1000), _gold = Num(0, int.MaxValue), _exp = Num(0, int.MaxValue), _sp = Num(0, int.MaxValue),
        _rep = Num(0, int.MaxValue), _itemId = Num(0, int.MaxValue), _itemN = Num(1, 99999), _monId = Num(0, int.MaxValue), _monN = Num(1, 99999), _dropItem = Num(0, int.MaxValue), _dropN = Num(1, 99999), _dropPct = Num(1, 100),
        _wItemId = Num(0, int.MaxValue), _wItemN = Num(1, 99999), _waitSec = Num(0, 30 * 24 * 3600);
    readonly ListView _monList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 92, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly ListView _dropList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 76, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly Label _monName = Muted(), _dropName = Muted(), _wItemName = Muted(), _waitText = Muted(), _itemsHint = new() { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 0, 3, 3) };
    readonly ListView _wItemList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 92, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly TextBox _name = Text(false), _desc = Text(true), _offer = Text(true), _summary = Text(true),
        _stepName = Text(false), _stepDesc = Text(true), _stepText = Text(true);
    readonly Label _giverName = Muted(), _stepNpcName = Muted(), _itemName = Muted(), _errors = new() { AutoSize = true, ForeColor = Bad, MaximumSize = new Size(330, 0) },
        _stepHint = new() { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 4, 3, 3) };

    // etapas (tipo 2+): lista, ordem e o detalhe da etapa selecionada
    readonly ComboBox _order = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly Label _stepEditing = new() { AutoSize = true, MaximumSize = new Size(340, 0), ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(3, 10, 3, 4) };
    readonly FlowLayoutPanel _stepTabs = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 4, 0, 0) },
        _stepMove = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0), Visible = false };
    readonly List<Button> _tabs = [];
    Button? _addStep;
    readonly ListBox _stepList = new() { Width = 330, Height = 130, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    readonly FlowLayoutPanel _stepBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _talkBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _reachBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _killBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _itemsBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _waitBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _nameBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    readonly ListView _areaList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 92, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly NumericUpDown[] _areaNum = Enumerable.Range(0, 6).Select(_ => new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 1, Width = 100, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle }).ToArray();
    bool _loading;
    readonly HashSet<FlowLayoutPanel> _boxes = [];

    // rodapé: Concluir só fica ativo sem pendências; o rótulo diz quantas e quais (o botão desativado não mostra dica)
    readonly Button _finish;
    readonly Label _pending = new() { AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(3, 7, 8, 0) };
    readonly System.Windows.Forms.Timer _check = new() { Interval = 600 };
    readonly List<QuestChoiceBlock> _choices = new();
    readonly ComboBox _howItems = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 4, 3, 2) };
    readonly ListView _items = new() { View = View.Details, FullRowSelect = true, Height = 130, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };

    /// <summary>Quest criada (ID) — o Mundo 3D fecha o painel e mostra o NPC.</summary>
    public new event Action<int>? Created;
    public event Action? Cancelled;

    public WorldQuestEditor(IQuestHost host, QuestDraft draft, QuestWorld world)
    {
        _host = host; _d = draft; _world = world;
        // quest nova começa SEM etapas: o usuário escolhe (antes vinha "Falar com NPC" e era preciso apagar; usuário 01/11)
        Dock = DockStyle.Right; Width = 400; BackColor = Bg; ForeColor = Fg;
        // cabeçalho: ícone + "Nova quest" e, embaixo, de quem é; X fecha (pergunta antes)
        var head = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Bg2 };
        var close = new Button { Dock = DockStyle.Right, Width = 40, FlatStyle = FlatStyle.Flat, Image = UiKit.Icon("\uE711", Color.White, 16), Cursor = Cursors.Hand };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Cancel();
        _tip.SetToolTip(close, Loc.T("qe.cancel", "Cancelar"));
        var icon = new PictureBox { Dock = DockStyle.Left, Width = 34, Image = UiKit.Icon("\uE7C1", Accent, 22), SizeMode = PictureBoxSizeMode.CenterImage };
        head.Controls.Add(_title); head.Controls.Add(icon); head.Controls.Add(close);
        _title.Padding = new Padding(2, 4, 0, 0); _title.TextAlign = ContentAlignment.BottomLeft;
        // passos: círculos numerados (o atual em azul, os já vistos com ✓); clique pula
        _stepper.Picked += i => Show(i);
        // rodapé: pendências numa linha; Voltar / Avançar / Concluir na outra (Concluir só sem pendências)
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Bg2, Padding = new Padding(8, 6, 8, 8) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var finish = _finish = Btn(Loc.T("qe.finish", "Concluir"), Finish, Color.FromArgb(34, 140, 70));
        _next = Btn(Loc.T("qe.next", "Avançar ▶"), () => Show(Math.Min(_pages.Count - 1, _at + 1)), UiKit.Accent);
        _back = Btn(Loc.T("qe.back", "◀ Voltar"), () => Show(Math.Max(0, _at - 1)));
        var cancel = Btn(Loc.T("qe.cancel", "Cancelar"), Cancel);
        row.Controls.AddRange(new Control[] { finish, _next, _back, cancel });
        _pending.Dock = DockStyle.Top; _pending.AutoSize = false; _pending.Height = 24; _pending.TextAlign = ContentAlignment.MiddleLeft;
        foot.Controls.Add(_pending); foot.Controls.Add(row);
        _tip.SetToolTip(finish, Loc.T("qe.finishTip", "Cria a quest e liga aos NPCs (só na memória: grava no Salvar)."));
        _pending.Click += (_, _) => Show(_pages.Count - 1);
        Controls.Add(_page); Controls.Add(_stepper); Controls.Add(foot); Controls.Add(head);

        BuildPages();
        Load();
        _title.Text = Title();
        if (Editing) _tip.SetToolTip(finish, Loc.T("qe.finishEditTip", "Grava as mudanças na quest e nos NPCs (só na memória: grava no Salvar)."));
        _tip.SetToolTip(_stepper, Loc.T("qe.undoTip", "Ctrl+Z desfaz etapas, áreas e itens; Ctrl+Y refaz."));
        _sub.Text = _d.Giver > 0 && _host.NameOf(_d.Giver) is { } gn ? string.Format(Loc.T("qe.givenBy2", "dada por {0}"), gn) : "";
        Show(0);
        // validação contínua: Concluir só ativo quando não falta nada (a quest criada tem que funcionar no jogo)
        _check.Tick += (_, _) => { if (Visible && !IsDisposed && !UiKit.TypingNumber(this)) CheckNow(); };   // ler .Value durante a digitação joga o cursor para o início
        _boxes.UnionWith([_stepBox, _nameBox, _talkBox, _reachBox, _killBox, _itemsBox, _waitBox]);
        foreach (var pg in _pages) if (pg.Item1 is FlowLayoutPanel page) Cardify(page);
        foreach (var b in _boxes) Cardify(b);
        if (_order.Parent is { } orderCard && orderCard.Parent is { } stepsPage) stepsPage.Controls.SetChildIndex(orderCard, stepsPage.Controls.Count - 1);   // a etapa aberta fica logo abaixo das abas
        _check.Start();
        CheckNow();
        Disposed += (_, _) => { _check.Dispose(); _tip.Dispose(); };
    }

    void CheckNow()
    {
        var errors = _host.Validate(Collect());
        _finish.Enabled = errors.Count == 0;
        _pending.ForeColor = errors.Count == 0 ? Good : Bad;
        _pending.Text = errors.Count == 0 ? Loc.T("qe.ready", "✓ Pronta para concluir") : string.Format(Loc.T("qe.pendingN", "⚠ {0} pendência(s) — clique para ver"), errors.Count);
        var tip = errors.Count == 0 ? "" : string.Join("\n", errors.Take(12).Select(x => "• " + x));
        if (_tip.GetToolTip(_pending) != tip) _tip.SetToolTip(_pending, tip);
    }

    // ---------- montagem ----------

    static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 120, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    static new TextBox Text(bool multi) => new() { Multiline = multi, Width = 330, Height = multi ? 90 : 24, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    static Label Muted() => new() { AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 0, 3, 6) };

    // todo campo de item passa por aqui: a janela escolhe o item e a quantidade, o resto vai pelo "adicionar" de cada lista
    void PickItem(NumericUpDown id, NumericUpDown? qty, Action add)
    {
        using var w = new ItemSearchWindow(_host.AllItems(), _host.ItemIcon, qty is not null, _host.TipImage, _host.FavoriteIds(), _host.RecentIds(), model: _host.ItemModel, fav: _host.ItemFavorites());
        if (w.ShowDialog(this) != DialogResult.OK) return;
        _host.ItemPicked(w.Picked);
        id.Value = w.Picked;
        if (qty is not null) qty.Value = Math.Clamp(w.Quantity, (int)qty.Minimum, (int)qty.Maximum);
        add();
    }

    // agrupa cada título com os campos dele num quadro, para o olho não se perder de um campo para outro
    void Cardify(FlowLayoutPanel box)
    {
        FlowLayoutPanel? card = null;
        foreach (var c in box.Controls.Cast<Control>().ToList())
        {
            if (c is FlowLayoutPanel f && _boxes.Contains(f)) { card = null; continue; }
            if (c.Tag as string == "cap")
            {
                card = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Card, Padding = new Padding(8, 2, 8, 10), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(354, 0) };
                int at = box.Controls.IndexOf(c);
                box.Controls.Add(card);
                box.Controls.SetChildIndex(card, at);
            }
            if (card is null) continue;
            box.Controls.Remove(c);
            card.Controls.Add(c);
        }
    }

    Button Btn(string text, Action a, Color? bg = null)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = bg ?? Bg, Height = 28, Margin = new Padding(0, 3, 6, 3) };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90);
        UiKit.Decorate(b, text, dark: true);
        if (bg is { } c) { b.BackColor = c; b.FlatAppearance.BorderColor = c; }
        b.Click += (_, _) => a();
        return b;
    }

    FlowLayoutPanel NewPage(string key, string pt, Action? onShow = null)
    {
        // folga no fim: rolando até o final, o último campo não fica colado na borda
        var page = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 0, 28) };
        _stepper.Add(() => Loc.T(key, pt));
        _pages.Add((page, onShow));
        return page;
    }

    void Caption(FlowLayoutPanel p, string key, string pt, string hintKey, string hint)
    {
        var l = new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Accent, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Tag = "cap", Margin = new Padding(3, 10, 3, 2) };
        _tip.SetToolTip(l, Loc.T(hintKey, hint));
        p.Controls.Add(l);
        p.Controls.Add(new Label { Text = Loc.T(hintKey, hint), AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 0, 3, 3) });
    }

    void NpcRow(FlowLayoutPanel p, NumericUpDown num, Label name)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0) };
        row.Controls.Add(num);
        row.Controls.Add(Btn(Loc.T("qe.pick", "🎯 Escolher no mundo"), () =>
        {
            _title.Text = Loc.T("qe.picking", "Clique no NPC no mundo… (Esc cancela)");
            _world.PickNpc(id => { num.Value = id; _title.Text = Title(); });
        }));
        p.Controls.Add(row);
        p.Controls.Add(name);
        num.ValueChanged += (_, _) => name.Text = num.Value <= 0 ? "" : _host.NameOf((int)num.Value) is { } n ? n + (_host.IsNpc((int)num.Value) ? "" : "  " + Loc.T("qe.notNpc", "(não é NPC)")) : Loc.T("qe.unknown", "(não existe no elements.data)");
    }

    void BuildPages()
    {
        var npcs = NewPage("qe.stepNpcs", "NPCs");
        Caption(npcs, "qe.id", "ID da quest", "qe.idHint", "Próximo ID livre. Pode trocar, desde que não seja de outra quest.");
        npcs.Controls.Add(_id);
        Caption(npcs, "qe.name", "Nome", "qe.nameHint", "Aparece no diário de quests (até 29 caracteres). Ex.: Carta para o ferreiro.");
        _name.MaxLength = TasksFile.MaxNameLength; _name.PlaceholderText = Loc.T("qe.namePh", "Nome da quest");
        npcs.Controls.Add(_name);
        Caption(npcs, "qe.giver", "NPC que dá a quest", "qe.giverHint", "Onde o jogador aceita a quest.");
        NpcRow(npcs, _giver, _giverName);
        // tipo da quest = ícone que aparece sobre o NPC no jogo (cliente EC_NPCServer.cpp UpdateCurQuestIcon)
        Caption(npcs, "qe.type", "Ícone sobre o NPC (tipo da quest)", "qe.typeHint", "É o símbolo que o jogador vê em cima do NPC quando pode pegar a quest.");
        var types = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 2, 0, 6) };
        foreach (var (type, icon, key, pt, isKey) in new[] { (101, 0, "qe.tNormal", "Comum", false), (102, 12, "qe.tMain", "Principal", false), (100, 10, "qe.tDaily", "Diária", false),
                     (104, 10, "qe.tEvent", "Evento", false), (106, 6, "qe.tFaction", "Clã", false), (105, 8, "qe.tHunt", "Lista de caça", false),
                     (101, 4, "qe.tKey", "Cultivo (chave)", true) })
        {
            var b = new RadioButton { Appearance = Appearance.Button, AutoSize = true, Text = Loc.T(key, pt), Tag = type, Image = _host.QuestIcon(icon), TextImageRelation = TextImageRelation.ImageBeforeText,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(40, 45, 54), Margin = new Padding(0, 0, 6, 6), Padding = new Padding(4, 2, 8, 2), MinimumSize = new Size(0, 36) };
            b.FlatAppearance.CheckedBackColor = UiKit.Accent; b.FlatAppearance.BorderColor = Color.FromArgb(62, 70, 84);
            b.CheckedChanged += (_, _) => { if (b.Checked) { _d.Type = type; _d.Key = isKey; } };
            b.Name = isKey ? "key" : "";
            _typeButtons.Add(b); types.Controls.Add(b);
        }
        npcs.Controls.Add(types);
        var reqOpts = _host.Options(false);
        _redoOpt = reqOpts.FirstOrDefault(o => o.Id == "redo"); _repeatOpt = reqOpts.FirstOrDefault(o => o.Id == "repeat");
        if (_redoOpt is not null || _repeatOpt is not null)
        {
            Caption(npcs, "qe.repeat", "Repetição", "qe.repeatHint2", "Quantas vezes o mesmo jogador pode fazer a quest. Para testar, use 'Pode refazer': um personagem que já concluiu uma quest com este ID não a vê de novo.");
            var reps = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 2, 0, 6) };
            foreach (var (code, key, pt) in new[] { (0, "qe.rOnce", "Uma vez só"), (-1, "qe.rRedo", "Pode refazer sempre"), (1, "qe.rDaily", "1 vez por dia"),
                         (2, "qe.rWeekly", "1 vez por semana"), (3, "qe.rMonthly", "1 vez por mês") })
            {
                if (code == -1 && _redoOpt is null || code > 0 && _repeatOpt is null) continue;   // a versão não tem o campo
                var b = new RadioButton { Appearance = Appearance.Button, AutoSize = true, Text = Loc.T(key, pt), Tag = code, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
                    BackColor = Color.FromArgb(40, 45, 54), Margin = new Padding(0, 0, 6, 6), Padding = new Padding(8, 2, 8, 2), MinimumSize = new Size(0, 32) };
                b.FlatAppearance.CheckedBackColor = UiKit.Accent; b.FlatAppearance.BorderColor = Color.FromArgb(62, 70, 84);
                _repeatButtons.Add(b); reps.Controls.Add(b);
            }
            npcs.Controls.Add(reps);
        }

        BuildStepsPage();

        var req = NewPage("qe.stepReq", "Requisitos");
        Caption(req, "qe.minLv", "Nível mínimo", "qe.minLvHint", "Abaixo deste nível o NPC não oferece a quest.");
        req.Controls.Add(_minLv);
        Caption(req, "qe.maxLv", "Nível máximo", "qe.maxLvHint", "0 = sem limite.");
        req.Controls.Add(_maxLv);
        AddChooser(req, reward: false);

        var award = NewPage("qe.stepAward", "Prêmios");
        Caption(award, "qe.points", "Moedas e pontos", "qe.pointsHint", "O que o jogador ganha ao entregar. Em branco = nada.");
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 4, Margin = new Padding(0, 2, 0, 0) };
        int cell = 0;
        foreach (var (num, key, pt, hk, hint) in new[]
        {
            (_gold, "qe.gold", "Ouro", "qe.goldHint", "Moedas ganhas ao entregar."),
            (_exp, "qe.exp", "Experiência", "qe.expHint", "Pontos de experiência."),
            (_sp, "qe.sp", "Espírito (SP)", "qe.spHint", "Pontos de espírito para aprender habilidades."),
            (_rep, "qe.rep", "Reputação", "qe.repHint", "Reputação ganha."),
        })
        {
            var (glyph, tint) = num == _gold ? ("\uE8C7", Color.Gold) : num == _exp ? ("\uE734", Color.FromArgb(96, 165, 250)) : num == _sp ? ("\uE945", Color.FromArgb(167, 139, 250)) : ("\uE8E1", Color.FromArgb(74, 222, 128));
            var label = new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Fg, Margin = new Padding(2, 3, 3, 0) };
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 2) };
            head.Controls.Add(new PictureBox { Image = UiKit.Icon(glyph, tint, 18), Size = new Size(20, 20), SizeMode = PictureBoxSizeMode.CenterImage, Margin = new Padding(3, 0, 0, 0) });
            head.Controls.Add(label);
            _tip.SetToolTip(label, Loc.T(hk, hint)); _tip.SetToolTip(num, Loc.T(hk, hint));
            num.Width = 158; num.Margin = new Padding(3, 0, 8, 2);
            grid.Controls.Add(head, cell % 2, cell / 2 * 2);
            grid.Controls.Add(num, cell % 2, cell / 2 * 2 + 1);
            cell++;
        }
        award.Controls.Add(grid);
        Caption(award, "qe.items", "Itens", "qe.itemsHint", "Itens ganhos ao entregar. Abaixo da lista, escolha se o jogador ganha todos ou um sorteado.");
        _items.Columns.Add(Loc.T("wq.colId", "ID"), 70); _items.Columns.Add(Loc.T("qe.itemCol", "Item"), 190); _items.Columns.Add(Loc.T("qe.countCol", "Qtd."), 60);
        award.Controls.Add(_items);
        var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 4) };
        _itemN.Width = 70; _itemN.Margin = new Padding(3, 3, 3, 0);
        addRow.Controls.Add(Btn(Loc.T("qe.addItem", "Adicionar item"), () => { _items.SelectedItems.Clear(); PickItem(_itemId, _itemN, AddItem); }, UiKit.Accent));
        addRow.Controls.Add(Btn(Loc.T("qe.remove", "Remover"), () => { if (_items.SelectedItems.Count == 0) return; Remember(); foreach (ListViewItem it in _items.SelectedItems) _items.Items.Remove(it); }));
        addRow.Controls.Add(new Label { Text = Loc.T("qe.countCol", "Qtd."), AutoSize = true, ForeColor = Fg, Margin = new Padding(10, 7, 0, 0) });
        addRow.Controls.Add(_itemN);
        award.Controls.Add(addRow);
        // quantidade do item escolhido na lista
        _items.SelectedIndexChanged += (_, _) => { if (_items.SelectedItems.Count == 1 && _items.SelectedItems[0].Tag is ValueTuple<int, int> t) { _loading = true; _itemN.Value = t.Item2; _loading = false; } };
        _itemN.ValueChanged += (_, _) =>
        {
            if (_loading || _items.SelectedItems.Count != 1 || _items.SelectedItems[0].Tag is not ValueTuple<int, int> t || t.Item2 == (int)_itemN.Value) return;
            Remember();
            var it = _items.SelectedItems[0];
            it.Tag = (t.Item1, (int)_itemN.Value); it.SubItems[2].Text = ((int)_itemN.Value).ToString();
        };
        _howItems.Items.AddRange(new object[] { Loc.T("qe.allItems", "O jogador ganha todos os itens"), Loc.T("qe.oneItem", "O jogador ganha um item sorteado da lista") });
        _howItems.SelectedIndex = 0;
        award.Controls.Add(_howItems);
        AddChooser(award, reward: true);

        var texts = NewPage("qe.stepTexts", "Textos");
        Caption(texts, "qe.desc", "Descrição", "qe.descHint", "Texto da quest no diário: o que o jogador precisa fazer.");
        _desc.PlaceholderText = Loc.T("qe.descPh", "Ex.: Leve a carta do guarda até o ferreiro da vila.");
        texts.Controls.Add(_desc);
        Caption(texts, "qe.offer", "Fala ao dar a quest", "qe.offerHint", "O que o NPC que dá a quest diz ao jogador, com a opção de aceitar.");
        _offer.PlaceholderText = Loc.T("qe.offerPh", "Ex.: Você pode levar esta carta ao ferreiro para mim?");
        texts.Controls.Add(_offer);
        texts.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 8, 3, 3),
            Text = Loc.T("qe.rewardInStep", "A fala do NPC ao entregar o prêmio fica na etapa \"Falar com NPC\" (passo Etapas).") });

        var sum = NewPage("qe.stepSummary", "Resumo", RefreshSummary);
        sum.Controls.Add(_errors);
        _summary.ReadOnly = true; _summary.Height = 300;
        sum.Controls.Add(_summary);
    }

    // ---------- etapas ----------

    QuestStep? CurStep => _stepList.SelectedIndex >= 0 && _stepList.SelectedIndex < _d.Steps.Count ? _d.Steps[_stepList.SelectedIndex] : null;
    QuestArea? CurArea => CurStep is { Kind: QuestStepKind.Reach } s && _areaList.SelectedIndices.Count > 0 && _areaList.SelectedIndices[0] < s.Areas.Count ? s.Areas[_areaList.SelectedIndices[0]] : null;

    /// <summary>
    /// Passo "Etapas": o que o jogador faz depois de aceitar, em ordem ou não. Uma etapa só de falar com NPC = recado; com mais
    /// etapas (ou ir até um lugar) a quest vira principal + subquests, como o jogo grava.
    /// </summary>
    void BuildStepsPage()
    {
        var p = NewPage("qe.stepSteps", "Etapas", () => ShowAreas());
        Caption(p, "qe.steps", "Etapas da quest", "qe.stepsHint", "O que o jogador faz depois de aceitar. Só \"Falar com NPC\" = recado. Ir até um lugar, depois falar com um NPC, ir a outro lugar… cada etapa aparece no diário.");
        _ = _stepList.Handle;   // a lista segue guardando a seleção; quem aparece são as abas
        p.Controls.Add(_stepTabs);
        _stepMove.Controls.Add(Btn("◀", () => MoveStep(-1)));
        _stepMove.Controls.Add(Btn("▶", () => MoveStep(1)));
        _stepMove.Controls.Add(Btn(Loc.T("qe.removeStep", "Remover etapa"), RemoveStep));
        p.Controls.Add(_stepMove);
        Caption(p, "qe.order", "Ordem das etapas", "qe.orderHint", "Em ordem: a próxima só aparece depois da anterior. Qualquer ordem: todas aparecem juntas e a quest termina quando a última for feita. Basta uma: a primeira etapa feita conclui a quest.");
        _order.Items.AddRange(new object[] { Loc.T("qe.inOrder", "Em ordem (uma depois da outra)"), Loc.T("qe.anyOrder", "Em qualquer ordem (todas precisam ser feitas)"), Loc.T("qe.anyOne", "Basta uma das etapas") });
        p.Controls.Add(_order);
        p.Controls.Add(_stepHint);

        // detalhe da etapa selecionada
        Caption(_nameBox, "qe.stepName", "Nome da etapa", "qe.stepNameHint", "Aparece no diário de quests enquanto a etapa está ativa (até 29 caracteres).");
        _stepName.MaxLength = TasksFile.MaxNameLength;
        _nameBox.Controls.Add(_stepName);
        Caption(_nameBox, "qe.stepDesc", "Descrição da etapa (opcional)", "qe.stepDescHint", "Texto do diário para esta etapa. Em branco = só a descrição da quest.");
        _stepDesc.Height = 50;
        _nameBox.Controls.Add(_stepDesc);
        _stepBox.Controls.Add(_stepEditing);
        _stepBox.Controls.Add(_nameBox);

        Caption(_talkBox, "qe.stepNpc", "NPC com quem falar", "qe.stepNpcHint", "O jogador fala com este NPC para concluir a etapa (no recado, é quem recebe e entrega o prêmio).");
        NpcRow(_talkBox, _stepNpc, _stepNpcName);
        Caption(_talkBox, "qe.reward", "Fala ao entregar", "qe.rewardHint", "O que o NPC diz quando o jogador conclui a etapa com ele (no recado, ao entregar o prêmio).");
        _stepText.PlaceholderText = Loc.T("qe.rewardPh", "Ex.: Ah, uma carta do guarda! Obrigado, tome sua recompensa.");
        _talkBox.Controls.Add(_stepText);
        _stepBox.Controls.Add(_talkBox);

        Caption(_reachBox, "qe.areas", "Áreas (chegar em qualquer uma conclui)", "qe.areasHint", "Marque arrastando no chão do Mundo 3D; a altura é preenchida sozinha. Todas as áreas de uma etapa ficam no mesmo mapa. Para exigir vários lugares, crie uma etapa para cada.");
        _areaList.Columns.Add("#", 26); _areaList.Columns.Add("X", 96); _areaList.Columns.Add("Z", 96); _areaList.Columns.Add(Loc.T("qe.height", "Altura"), 96);
        _reachBox.Controls.Add(_areaList);
        var ar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 4, 0, 0) };
        ar.Controls.Add(Btn(Loc.T("qe.markArea", "🎯 Marcar no mundo"), MarkArea));
        // botões da área do mesmo tamanho (alinhados em grade quando quebram a linha; usuário 01/11)
        ar.Controls.Add(Btn(Loc.T("qe.removeArea", "Remover área"), RemoveArea));
        ar.Controls.Add(Btn(Loc.T("qe.anyHeight", "Qualquer altura"), () => { if (CurArea is { } a) { Remember(); a.Y0 = -9999; a.Y1 = 9999; LoadArea(); RefreshAreas(); } }));
        foreach (Control c in ar.Controls) if (c is Button b) { b.AutoSize = false; b.Width = 145; b.Height = 30; }
        _reachBox.Controls.Add(ar);
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 0) };
        string[] rows = { Loc.T("qe.axisX", "X (mín. / máx.)"), Loc.T("qe.axisY", "Altura (mín. / máx.)"), Loc.T("qe.axisZ", "Z (mín. / máx.)") };
        for (int i = 0; i < 3; i++)
        {
            grid.Controls.Add(new Label { Text = rows[i], AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(0, 6, 4, 0) }, 0, i);
            grid.Controls.Add(_areaNum[i], 1, i); grid.Controls.Add(_areaNum[i + 3], 2, i);
        }
        _reachBox.Controls.Add(grid);
        _stepBox.Controls.Add(_reachBox);

        // matar monstros: lista (monstro, quantos); escolher clicando num ponto de monstro no mundo ou pelo ID
        Caption(_killBox, "qe.monsters", "Monstros para matar", "qe.monstersHint", "A etapa acaba quando o jogador matar a quantidade de cada monstro da lista. Clique em \"Escolher no mundo\" e depois num ponto de monstro, ou digite o ID.");
        _monList.Columns.Add(Loc.T("wq.colId", "ID"), 70); _monList.Columns.Add(Loc.T("qe.monsterCol", "Monstro"), 190); _monList.Columns.Add(Loc.T("qe.countCol", "Qtd."), 60);
        _killBox.Controls.Add(_monList);
        var mr = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 4, 0, 0) };
        _monId.Width = 96; _monN.Width = 58; _monN.Value = 10;
        mr.Controls.Add(_monId); mr.Controls.Add(_monN);
        mr.Controls.Add(Btn(Loc.T("qe.add", "Adicionar"), () => AddMonster((int)_monId.Value)));
        mr.Controls.Add(Btn(Loc.T("qe.pick", "🎯 Escolher no mundo"), () =>
        {
            _title.Text = Loc.T("qe.pickingMonster", "Clique num ponto de monstro no mundo… (Esc cancela)");
            _world.PickMonster(id => { _title.Text = Title(); _monId.Value = id; AddMonster(id); });
        }));
        mr.Controls.Add(Btn(Loc.T("qe.remove", "Remover"), RemoveMonster));
        _killBox.Controls.Add(mr);
        _killBox.Controls.Add(_monName);
        // itens que só caem na quest: o monstro escolhido acima deixa cair o item com a chance dada; a etapa conta os itens
        Caption(_killBox, "qe.drops", "Itens que caem dos monstros (só na quest)", "qe.dropsHint", "Opcional. Escolha o monstro acima (ID ou no mundo), depois o item, quantos juntar e a chance de cair. A etapa conta os itens juntados, não as mortes; ao concluir os itens somem.");
        _dropList.Columns.Add(Loc.T("qe.monsterCol", "Monstro"), 110); _dropList.Columns.Add(Loc.T("qe.itemCol", "Item"), 130); _dropList.Columns.Add(Loc.T("qe.countCol", "Qtd."), 40); _dropList.Columns.Add("%", 40);
        _killBox.Controls.Add(_dropList);
        var dr = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 4, 0, 0) };
        _dropItem.Width = 90; _dropN.Width = 54; _dropPct.Width = 50; _dropPct.Value = 50;
        dr.Controls.Add(_dropN); dr.Controls.Add(_dropPct); dr.Controls.Add(new Label { Text = "%", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(0, 6, 6, 0) });
        dr.Controls.Add(Btn(Loc.T("qe.addDrop", "Adicionar item que cai"), () => PickItem(_dropItem, null, AddDrop), UiKit.Accent));
        dr.Controls.Add(Btn(Loc.T("qe.remove", "Remover"), RemoveDrop));
        _killBox.Controls.Add(dr);
        _killBox.Controls.Add(_dropName);
        _dropItem.ValueChanged += (_, _) => _dropName.Text = _dropItem.Value <= 0 ? "" : _host.NameOf((int)_dropItem.Value) is { } dn
            ? dn + (_host.IsQuestItem((int)_dropItem.Value) ? "  " + Loc.T("qe.questItem", "(item de quest)") : "") : Loc.T("qe.unknown", "(não existe no elements.data)");
        _monId.ValueChanged += (_, _) => _monName.Text = _monId.Value <= 0 ? "" : _host.NameOf((int)_monId.Value) is { } mn
            ? mn + (_host.IsMonster((int)_monId.Value) ? "" : "  " + Loc.T("qe.notMonster", "(não é monstro)")) : Loc.T("qe.unknown", "(não existe no elements.data)");
        _stepBox.Controls.Add(_killBox);

        // obter / entregar itens: lista (item, quantos); entregar mostra também o NPC e a fala (bloco de falar com NPC)
        Caption(_itemsBox, "qe.wantItems", "Itens", "qe.wantItemsHint", "Itens que o jogador precisa ter no inventário.");
        _itemsBox.Controls.Add(_itemsHint);
        _wItemList.Columns.Add(Loc.T("wq.colId", "ID"), 70); _wItemList.Columns.Add(Loc.T("qe.itemCol", "Item"), 190); _wItemList.Columns.Add(Loc.T("qe.countCol", "Qtd."), 60);
        _itemsBox.Controls.Add(_wItemList);
        var ir = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0, 4, 0, 0) };
        _wItemId.Width = 96; _wItemN.Width = 58;
        ir.Controls.Add(Btn(Loc.T("qe.addItem", "Adicionar item"), () => PickItem(_wItemId, _wItemN, AddWantedItem), UiKit.Accent));
        ir.Controls.Add(Btn(Loc.T("qe.remove", "Remover"), RemoveWantedItem));
        // o item que um ponto de coleta dá (MINE_ESSENCE): clica na mina no mundo e entra o material mais provável
        ir.Controls.Add(Btn(Loc.T("qe.pickMine", "🎯 Coleta no mundo"), () =>
        {
            _title.Text = Loc.T("qe.pickingMine", "Clique num ponto de coleta (minério, erva…) no mundo… (Esc cancela)");
            _world.PickMine(mine =>
            {
                _title.Text = Title();
                var prods = _host.MineProducts(mine);
                if (prods.Count == 0) return;
                _wItemId.Value = prods[0].Item;
                AddWantedItem();
                _wItemName.Text = string.Format(Loc.T("qe.mineGives", "{0} dá: {1}"), _host.NameOf(mine) ?? mine.ToString(),
                    string.Join(", ", prods.Select(p => $"{_host.NameOf(p.Item) ?? p.Item.ToString()} ({p.Prob:P0})")));
            });
        }));
        _itemsBox.Controls.Add(ir);
        _itemsBox.Controls.Add(_wItemName);
        _wItemId.ValueChanged += (_, _) => _wItemName.Text = _wItemId.Value <= 0 ? "" : _host.NameOf((int)_wItemId.Value) ?? Loc.T("qe.unknown", "(não existe no elements.data)");
        _stepBox.Controls.Add(_itemsBox);

        // esperar um tempo (segundos depois de a etapa começar)
        Caption(_waitBox, "qe.wait", "Tempo de espera (segundos)", "qe.waitHint", "A etapa acaba sozinha depois deste tempo. Ex.: 90 = 1 minuto e meio; 3600 = 1 hora.");
        _waitBox.Controls.Add(_waitSec);
        _waitBox.Controls.Add(_waitText);
        _waitSec.ValueChanged += (_, _) =>
        {
            var ts = TimeSpan.FromSeconds((double)_waitSec.Value);
            _waitText.Text = "= " + (ts.Days > 0 ? string.Format(Loc.T("qe.days", "{0} dia(s) e {1}"), ts.Days, ts.ToString(@"hh\:mm\:ss")) : ts.ToString(@"h\:mm\:ss"));
            if (_loading || CurStep is not { Kind: QuestStepKind.Wait } s || s.WaitSeconds == (int)_waitSec.Value) return;
            Remember(); s.WaitSeconds = (int)_waitSec.Value; RefreshStepText();
        };
        _stepBox.Controls.Add(_waitBox);
        p.Controls.Add(_stepBox);

        _stepList.SelectedIndexChanged += (_, _) => { LoadStep(); RefreshTabs(); };
        _order.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshStepList(_stepList.SelectedIndex); };
        _stepName.TextChanged += (_, _) => { if (!_loading && CurStep is { } s) { s.Name = _stepName.Text; RefreshStepText(); } };
        _stepDesc.TextChanged += (_, _) => { if (!_loading && CurStep is { } s) s.Description = _stepDesc.Text; };
        _stepText.TextChanged += (_, _) => { if (!_loading && CurStep is { } s) s.Text = _stepText.Text; };
        _stepNpc.ValueChanged += (_, _) => { if (!_loading && CurStep is { } s) { s.Npc = (int)_stepNpc.Value; RefreshStepText(); } };
        _areaList.SelectedIndexChanged += (_, _) => { LoadArea(); ShowAreas(); };
        for (int i = 0; i < 6; i++)
        {
            int k = i;
            _areaNum[i].ValueChanged += (_, _) =>
            {
                if (_loading || CurArea is not { } a) return;
                float v = (float)_areaNum[k].Value;
                switch (k) { case 0: a.X0 = v; break; case 1: a.Y0 = v; break; case 2: a.Z0 = v; break; case 3: a.X1 = v; break; case 4: a.Y1 = v; break; default: a.Z1 = v; break; }
                RefreshAreas(keepSelection: true);
            };
        }
    }

    static string KindGlyph(QuestStepKind k) => k switch
    {
        QuestStepKind.Talk => "\uE8BD", QuestStepKind.Reach => "\uE707", QuestStepKind.Kill => "\uE945",
        QuestStepKind.Collect => "\uE7B8", QuestStepKind.Deliver => "\uE724", _ => "\uE823",
    };

    static string KindName(QuestStepKind k) => (k switch
    {
        QuestStepKind.Talk => Loc.T("qe.addTalk", "+ Falar com NPC"), QuestStepKind.Reach => Loc.T("qe.addReach", "+ Ir até um lugar"),
        QuestStepKind.Kill => Loc.T("qe.addKill", "+ Matar monstros"), QuestStepKind.Collect => Loc.T("qe.addCollect", "+ Obter itens"),
        QuestStepKind.Deliver => Loc.T("qe.addDeliver", "+ Entregar itens"), _ => Loc.T("qe.addWait", "+ Esperar"),
    }).TrimStart('+', ' ');

    // abas das etapas: número + ícone do tipo + nome; a etapa atual em azul; o último botão cria uma etapa sem sair desta
    void RefreshTabs()
    {
        if (_addStep is null || _tabs.Count != _d.Steps.Count)
        {
            foreach (var old in _stepTabs.Controls.Cast<Control>().ToList()) { _stepTabs.Controls.Remove(old); old.Dispose(); }
            _tabs.Clear();
            for (int i = 0; i < _d.Steps.Count; i++)
            {
                int at = i;
                var b = Btn("", () => _stepList.SelectedIndex = at);
                b.AutoSize = false; b.Size = new Size(162, 36); b.Margin = new Padding(0, 0, 6, 6); b.TextAlign = ContentAlignment.MiddleLeft; b.TextImageRelation = TextImageRelation.ImageBeforeText; b.AutoEllipsis = true;
                _tabs.Add(b); _stepTabs.Controls.Add(b);
            }
            _addStep = Btn(Loc.T("qe.addStep", "Adicionar etapa"), () =>
            {
                var menu = new ContextMenuStrip();
                foreach (var k in new[] { QuestStepKind.Talk, QuestStepKind.Reach, QuestStepKind.Kill, QuestStepKind.Collect, QuestStepKind.Deliver, QuestStepKind.Wait })
                    menu.Items.Add(KindName(k), UiKit.Icon(KindGlyph(k), Color.FromArgb(50, 60, 75), 18), (_, _) => AddStep(k));
                menu.Show(_addStep!, new Point(0, _addStep!.Height));
            }, UiKit.Accent);
            _addStep.AutoSize = false; _addStep.Size = new Size(162, 36); _addStep.Margin = new Padding(0, 0, 6, 6);
            _stepTabs.Controls.Add(_addStep);
        }
        for (int i = 0; i < _tabs.Count; i++)
        {
            var s = _d.Steps[i]; var b = _tabs[i];
            bool cur = i == _stepList.SelectedIndex;
            b.Text = $"{i + 1}. {(s.Name.Trim().Length > 0 ? s.Name.Trim() : KindName(s.Kind))}";
            b.Image = UiKit.Icon(KindGlyph(s.Kind), Color.White, 16);
            b.BackColor = cur ? UiKit.Accent : Field;
            b.FlatAppearance.BorderColor = cur ? UiKit.Accent : Color.FromArgb(70, 78, 90);
            _tip.SetToolTip(b, StepText(i));
        }
        _stepMove.Visible = _d.Steps.Count > 0;
    }

    string StepText(int i)
    {
        var s = _d.Steps[i];
        string what = s.Kind == QuestStepKind.Talk
            ? string.Format(Loc.T("qe.stepTalkLine", "Falar com {0}"), s.Npc > 0 ? _host.NameOf(s.Npc) ?? s.Npc.ToString() : "?")
            : s.Kind is QuestStepKind.Collect or QuestStepKind.Deliver
            ? string.Format(s.Kind == QuestStepKind.Collect ? Loc.T("qe.stepCollectLine", "Obter itens ({0})") : Loc.T("qe.stepDeliverLine", "Entregar itens ({0}) a {1}"),
                s.Items.Count == 0 ? "?" : string.Join(", ", s.Items.Select(m => $"{m.Count} × {_host.NameOf(m.Id) ?? m.Id.ToString()}")), s.Npc > 0 ? _host.NameOf(s.Npc) ?? s.Npc.ToString() : "?")
            : s.Kind == QuestStepKind.Wait
            ? string.Format(Loc.T("qe.stepWaitLine", "Esperar {0}"), TimeSpan.FromSeconds(s.WaitSeconds).ToString(@"h\:mm\:ss"))
            : s.Kind == QuestStepKind.Kill
            ? string.Format(Loc.T("qe.stepKillLine", "Matar monstros ({0})"), s.Monsters.Count + s.Drops.Count == 0 ? "?" : string.Join(", ", s.Monsters.Select(m => $"{m.Count} × {_host.NameOf(m.Id) ?? m.Id.ToString()}")
                .Concat(s.Drops.Select(x => $"{x.Count} × {_host.NameOf(x.Item) ?? x.Item.ToString()}"))))
            : string.Format(Loc.T("qe.stepReachLine", "Ir até um lugar ({0} área(s))"), s.Areas.Count);
        return $"{i + 1}. {what}" + (s.Name.Trim().Length > 0 ? $" — {s.Name.Trim()}" : "");
    }

    void RefreshStepText()
    {
        int i = _stepList.SelectedIndex;
        if (i < 0 || i >= _d.Steps.Count) return;
        _loading = true;
        _stepList.Items[i] = StepText(i);
        _loading = false;
        RefreshTabs();
    }

    void RefreshStepList(int select)
    {
        _loading = true;
        _stepList.BeginUpdate();
        _stepList.Items.Clear();
        for (int i = 0; i < _d.Steps.Count; i++) _stepList.Items.Add(StepText(i));
        _stepList.EndUpdate();
        _loading = false;
        _stepList.SelectedIndex = _d.Steps.Count == 0 ? -1 : Math.Clamp(select, 0, _d.Steps.Count - 1);
        RefreshTabs();
        bool recado = QuestBuilder.IsRecado(_d);
        _order.Enabled = _d.Steps.Count > 1;
        _stepHint.Text = _d.Steps.Count == 0 ? Loc.T("qe.noStepsHint", "Ainda sem etapas: escolha abaixo o que o jogador faz depois de aceitar (pelo menos uma).")
            : recado
            ? Loc.T("qe.recadoHint", "Uma etapa só de falar com NPC: a quest é um recado (sem subquests) e esse NPC entrega o prêmio.")
            : (QuestStepOrder)Math.Max(0, _order.SelectedIndex) == QuestStepOrder.InOrder && _d.Steps[^1].Kind == QuestStepKind.Talk
                ? Loc.T("qe.stepsHintTalkLast", "O prêmio é entregue ao concluir a última etapa, falando com o NPC dela.")
                : Loc.T("qe.stepsHintAuto", "O prêmio é dado sozinho quando a quest termina. Para entregar num NPC, ponha \"Falar com NPC\" por último, em ordem.");
        LoadStep();
    }

    void LoadStep()
    {
        if (_loading) return;
        var s = CurStep;
        _loading = true;
        _stepBox.Visible = s is not null;
        if (s is not null) _stepEditing.Text = string.Format(Loc.T("qe.stepHeader", "Etapa {0}: {1}"), _stepList.SelectedIndex + 1, KindName(s.Kind));
        if (s is not null)
        {
            _nameBox.Visible = !QuestBuilder.IsRecado(_d);   // recado: nome e descrição são os da quest
            _talkBox.Visible = s.Kind is QuestStepKind.Talk or QuestStepKind.Deliver;
            _itemsBox.Visible = s.Kind is QuestStepKind.Collect or QuestStepKind.Deliver;
            _waitBox.Visible = s.Kind == QuestStepKind.Wait;
            _waitSec.Value = Math.Clamp(s.WaitSeconds, 0, (int)_waitSec.Maximum);
            _itemsHint.Text = s.Kind == QuestStepKind.Deliver
                ? Loc.T("qe.deliverHint", "O jogador leva estes itens ao NPC escolhido acima e conclui a etapa falando com ele; o NPC fica com os itens.")
                : Loc.T("qe.collectHint", "A etapa acaba quando o jogador tiver todos estes itens; os itens ficam com ele (uma etapa seguinte pode pedir para entregar).");
            _reachBox.Visible = s.Kind == QuestStepKind.Reach;
            _killBox.Visible = s.Kind == QuestStepKind.Kill;
            _stepName.Text = s.Name; _stepDesc.Text = s.Description; _stepText.Text = s.Text;
            _stepNpc.Value = Math.Max(0, s.Npc);
            _stepNpcName.Text = s.Npc <= 0 ? "" : _host.NameOf(s.Npc) ?? Loc.T("qe.unknown", "(não existe no elements.data)");
        }
        _loading = false;
        RefreshAreas();
        RefreshMonsters();
        RefreshWantedItems();
        RefreshDrops();
    }

    void RefreshDrops()
    {
        _dropList.BeginUpdate();
        _dropList.Items.Clear();
        if (CurStep is { Kind: QuestStepKind.Kill } s)
            foreach (var x in s.Drops)
                _dropList.Items.Add(new ListViewItem(new[] { _host.NameOf(x.Monster) ?? x.Monster.ToString(), _host.NameOf(x.Item) ?? x.Item.ToString(), x.Count.ToString(), $"{x.Prob * 100:0}" }));
        _dropList.EndUpdate();
    }

    void AddDrop()
    {
        int mon = (int)_monId.Value, item = (int)_dropItem.Value;
        if (CurStep is not { Kind: QuestStepKind.Kill } s) return;
        if (mon <= 0 || !_host.IsMonster(mon)) { _monName.Text = Loc.T("qe.dropPickMonster", "Escolha o monstro acima (ID ou no mundo) antes do item que cai."); return; }
        if (item <= 0 || _host.NameOf(item) is null || _host.IsNpc(item) || _host.IsMonster(item)) { _dropName.Text = Loc.T("qe.unknown", "(não existe no elements.data)"); return; }
        Remember();
        var d = new QuestDrop(mon, item, (int)_dropN.Value, (float)_dropPct.Value / 100f, !_host.IsQuestItem(item));
        int at = s.Drops.FindIndex(x => x.Monster == mon && x.Item == item);
        if (at >= 0) s.Drops[at] = d; else s.Drops.Add(d);   // mesmo monstro e item: troca quantidade e chance
        RefreshDrops(); RefreshStepText();
    }

    void RemoveDrop()
    {
        if (CurStep is not { Kind: QuestStepKind.Kill } s || _dropList.SelectedIndices.Count == 0) return;
        Remember();
        s.Drops.RemoveAt(_dropList.SelectedIndices[0]);
        RefreshDrops(); RefreshStepText();
    }

    void RefreshWantedItems()
    {
        _wItemList.BeginUpdate();
        _wItemList.Items.Clear();
        if (CurStep is { Kind: QuestStepKind.Collect or QuestStepKind.Deliver } s)
            foreach (var (id, n) in s.Items)
                _wItemList.Items.Add(new ListViewItem(new[] { id.ToString(), _host.NameOf(id) ?? "?", n.ToString() }) { Tag = (id, n) });
        _wItemList.EndUpdate();
    }

    void AddWantedItem()
    {
        int id = (int)_wItemId.Value;
        if (CurStep is not { Kind: QuestStepKind.Collect or QuestStepKind.Deliver } s || id <= 0) return;
        if (_host.NameOf(id) is null || _host.IsNpc(id) || _host.IsMonster(id)) { _wItemName.Text = Loc.T("qe.unknown", "(não existe no elements.data)"); return; }
        Remember();
        int at = s.Items.FindIndex(m => m.Id == id);
        if (at >= 0) s.Items[at] = (id, (int)_wItemN.Value);   // mesmo item de novo: troca a quantidade
        else s.Items.Add((id, (int)_wItemN.Value));
        RefreshWantedItems(); RefreshStepText();
    }

    void RemoveWantedItem()
    {
        if (CurStep is not { Kind: QuestStepKind.Collect or QuestStepKind.Deliver } s || _wItemList.SelectedIndices.Count == 0) return;
        Remember();
        s.Items.RemoveAt(_wItemList.SelectedIndices[0]);
        RefreshWantedItems(); RefreshStepText();
    }

    void RefreshMonsters()
    {
        _monList.BeginUpdate();
        _monList.Items.Clear();
        if (CurStep is { Kind: QuestStepKind.Kill } s)
            foreach (var (id, n) in s.Monsters)
                _monList.Items.Add(new ListViewItem(new[] { id.ToString(), _host.NameOf(id) ?? "?", n.ToString() }) { Tag = (id, n) });
        _monList.EndUpdate();
    }

    void AddMonster(int id)
    {
        if (CurStep is not { Kind: QuestStepKind.Kill } s || id <= 0) return;
        if (!_host.IsMonster(id)) { _monName.Text = (_host.NameOf(id) ?? id.ToString()) + "  " + Loc.T("qe.notMonster", "(não é monstro)"); return; }
        Remember();
        int at = s.Monsters.FindIndex(m => m.Id == id);
        if (at >= 0) s.Monsters[at] = (id, (int)_monN.Value);   // mesmo monstro de novo: troca a quantidade
        else s.Monsters.Add((id, (int)_monN.Value));
        RefreshMonsters(); RefreshStepText();
    }

    void RemoveMonster()
    {
        if (CurStep is not { Kind: QuestStepKind.Kill } s || _monList.SelectedIndices.Count == 0) return;
        Remember();
        s.Monsters.RemoveAt(_monList.SelectedIndices[0]);
        RefreshMonsters(); RefreshStepText();
    }

    void AddStep(QuestStepKind kind)
    {
        Remember();
        _d.Steps.Add(new QuestStep { Kind = kind });
        // com uma etapa só de falar com NPC não existe nome de etapa; a partir da segunda, cada etapa aparece no diário e
        // precisa de nome: abre a primeira que está sem nome (na 2ª etapa é a 1ª; depois, a nova) com o cursor no campo
        int open = QuestBuilder.IsRecado(_d) ? 0 : Math.Max(0, _d.Steps.FindIndex(x => x.Name.Trim().Length == 0));
        RefreshStepList(open);
        if (_d.Steps[open].Kind == QuestStepKind.Reach) ShowAreas();
        if (!QuestBuilder.IsRecado(_d)) BeginInvoke(() => { _page.ScrollControlIntoView(_stepName); _stepName.Focus(); });
    }

    void MoveStep(int by)
    {
        int i = _stepList.SelectedIndex, j = i + by;
        if (i < 0 || j < 0 || j >= _d.Steps.Count) return;
        Remember();
        (_d.Steps[i], _d.Steps[j]) = (_d.Steps[j], _d.Steps[i]);
        RefreshStepList(j);
    }

    void RemoveStep()
    {
        if (CurStep is null) return;
        if (_d.Steps.Count == 1) { MessageBox.Show(FindForm(), Loc.T("qe.lastStep", "A quest precisa de pelo menos uma etapa."), Loc.T("qe.stepSteps", "Etapas")); return; }
        int i = _stepList.SelectedIndex;
        Remember();
        _d.Steps.RemoveAt(i);
        RefreshStepList(i);
    }

    void MarkArea()
    {
        if (CurStep is not { Kind: QuestStepKind.Reach } s) return;
        int tag = _world.MapTag();
        if (s.Areas.Count > 0 && s.MapTag != tag)
        {
            MessageBox.Show(FindForm(), Loc.T("qe.areaOtherMap", "As áreas desta etapa são de outro mapa. Todas as áreas de uma etapa precisam ser do mesmo mapa: crie outra etapa para este."), Loc.T("qe.areas", "Áreas (chegar em qualquer uma conclui)"));
            return;
        }
        _title.Text = Loc.T("qe.areaPicking", "Arraste no chão para marcar a área (clique sem arrastar = 10 × 10 m; Esc cancela)…");
        _world.PickArea(a =>
        {
            _title.Text = Title();
            if (CurStep != s) return;
            Remember();
            s.MapTag = tag; s.Areas.Add(a);
            RefreshAreas(); RefreshStepText();
            _areaList.Items[^1].Selected = true;
        });
    }

    void RemoveArea()
    {
        if (CurStep is not { Kind: QuestStepKind.Reach } s || CurArea is not { } a) return;
        Remember();
        s.Areas.Remove(a);
        RefreshAreas(); RefreshStepText();
    }

    void RefreshAreas(bool keepSelection = false)
    {
        var s = CurStep;
        int sel = _areaList.SelectedIndices.Count > 0 ? _areaList.SelectedIndices[0] : -1;
        if (keepSelection && s is not null && sel >= 0 && sel < s.Areas.Count && sel < _areaList.Items.Count)
        {
            var a = s.Areas[sel];
            _areaList.Items[sel].SubItems[1].Text = $"{a.X0:0} … {a.X1:0}"; _areaList.Items[sel].SubItems[2].Text = $"{a.Z0:0} … {a.Z1:0}"; _areaList.Items[sel].SubItems[3].Text = $"{a.Y0:0} … {a.Y1:0}";
            ShowAreas();
            return;
        }
        _areaList.BeginUpdate();
        _areaList.Items.Clear();
        if (s is { Kind: QuestStepKind.Reach })
            for (int i = 0; i < s.Areas.Count; i++)
            {
                var a = s.Areas[i];
                _areaList.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), $"{a.X0:0} … {a.X1:0}", $"{a.Z0:0} … {a.Z1:0}", $"{a.Y0:0} … {a.Y1:0}" }));
            }
        _areaList.EndUpdate();
        if (_areaList.Items.Count > 0) _areaList.Items[Math.Clamp(sel, 0, _areaList.Items.Count - 1)].Selected = true;
        LoadArea();
        ShowAreas();
    }

    void LoadArea()
    {
        var a = CurArea;
        _loading = true;
        var v = a is null ? new float[6] : new[] { a.X0, a.Y0, a.Z0, a.X1, a.Y1, a.Z1 };
        for (int i = 0; i < 6; i++) { _areaNum[i].Enabled = a is not null; _areaNum[i].Value = (decimal)Math.Clamp(v[i], -100000, 100000); }
        _loading = false;
    }

    /// <summary>Só as áreas desta quest no mapa aberto aparecem no mundo; as da etapa selecionada, mais fortes.</summary>
    void ShowAreas()
    {
        int tag = _world.MapTag();
        var list = new List<QuestArea>(); int sel = -1;
        foreach (var s in _d.Steps.Where(s => s.Kind == QuestStepKind.Reach && (s.Areas.Count == 0 || s.MapTag == tag)))
            foreach (var a in s.Areas)
            {
                if (a == CurArea) sel = list.Count;
                list.Add(a);
            }
        _world.ShowAreas(list, sel);
    }

    /// <summary>"Adicionar requisito/prêmio": lista das opções da versão; cada escolha vira um bloco com os campos dela.</summary>
    void AddChooser(FlowLayoutPanel page, bool reward)
    {
        Caption(page, reward ? "qe.moreAward" : "qe.moreReq", reward ? "Mais prêmios" : "Mais requisitos",
            reward ? "qe.moreAwardHint" : "qe.moreReqHint", reward ? "Escolha o tipo e clique em Adicionar; pode pôr vários." : "Escolha o tipo e clique em Adicionar; pode pôr vários ao mesmo tempo.");
        var opts = _host.Options(reward).Where(o => !IsRepeatOption(o)).ToList();   // repetição tem linha própria na 1ª página
        // 1ª linha da lista = "escolha um tipo": nada vem pré-escolhido (antes "Classes" parecia já ser um requisito)
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DropDownWidth = 300 };
        box.Items.Add(reward ? Loc.T("qe.pickAward", "— escolha um tipo de prêmio —") : Loc.T("qe.pickReq", "— escolha um tipo de requisito —"));
        foreach (var o in opts) box.Items.Add(o.Text);
        box.SelectedIndex = 0;
        var holder = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        // o estado vem primeiro, num quadro: sem nada escolhido = sem requisitos / sem prêmios a mais
        var none = new Label { AutoSize = true, MaximumSize = new Size(330, 0), MinimumSize = new Size(330, 0), ForeColor = Color.White, BackColor = Color.FromArgb(30, 90, 50),
            Padding = new Padding(8, 6, 8, 6), Margin = new Padding(3, 6, 3, 6), Font = new Font("Segoe UI Semibold", 9.5f),
            Text = reward ? Loc.T("qe.noMoreAward2", "✓ Nenhum prêmio a mais — só o que está acima. Para dar mais, escolha um tipo abaixo e clique em Adicionar.")
                          : Loc.T("qe.noReq2", "✓ Sem requisitos — qualquer jogador a partir do nível mínimo pode aceitar. Para exigir algo, escolha um tipo abaixo e clique em Adicionar.") };
        var count = new Label { AutoSize = true, ForeColor = Accent, Margin = new Padding(3, 6, 3, 3), Font = new Font("Segoe UI Semibold", 9.5f) };
        page.Controls.Add(none); page.Controls.Add(count);
        void Sync()
        {
            int n = holder.Controls.OfType<QuestChoiceBlock>().Count();
            none.Visible = n == 0; count.Visible = n > 0;
            count.Text = reward ? string.Format(Loc.T("qe.awardsN", "{0} prêmio(s) a mais:"), n) : string.Format(Loc.T("qe.reqsN", "{0} requisito(s):"), n);
        }
        Sync();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(340, 0), Margin = new Padding(0) };
        row.Controls.Add(box);
        void AddBlock(QuestOption o, object? initial)
        {
            var b = new QuestChoiceBlock(o, _host, _tip, initial);
            b.Removed += x => { _choices.Remove(x); holder.Controls.Remove(x); x.Dispose(); Sync(); };
            _choices.Add(b); holder.Controls.Add(b); Sync();
        }
        // editar quest: as opções que ela já tem entram preenchidas
        foreach (var c in _d.Choices.Where(c => c.Option.Reward == reward && !IsRepeatOption(c.Option))) AddBlock(c.Option, c.Value);
        var addBtn = Btn(Loc.T("qe.add", "Adicionar"), () =>
        {
            if (box.SelectedIndex <= 0) return;
            var o = opts[box.SelectedIndex - 1];
            if (o.Type != QOType.Field && _choices.Any(c => c.Option == o)) return;   // cada opção uma vez (menos "outro campo")
            AddBlock(o, null);
            box.SelectedIndex = 0;
        });
        row.Controls.Add(addBtn);
        addBtn.Enabled = false;
        box.SelectedIndexChanged += (_, _) => { addBtn.Enabled = box.SelectedIndex > 0; _tip.SetToolTip(box, box.SelectedIndex > 0 ? opts[box.SelectedIndex - 1].Tip : ""); };
        page.Controls.Add(holder);
        page.Controls.Add(row);
    }

    void AddItem()
    {
        int id = (int)_itemId.Value;
        if (id <= 0 || _host.NameOf(id) is not { } name) { _itemName.Text = Loc.T("qe.unknown", "(não existe no elements.data)"); return; }
        Remember();
        _items.Items.Add(new ListViewItem(new[] { id.ToString(), name, ((int)_itemN.Value).ToString() }) { Tag = (id, (int)_itemN.Value) });
    }

    // ---------- desfazer do rascunho ----------
    // cada mudança de estrutura (etapa, área, item do prêmio) guarda o estado antes; texto digitado usa o desfazer da caixa
    sealed record Snap(List<QuestStep> Steps, List<(int Id, int N)> Items, int Step);
    readonly List<Snap> _dUndo = new(), _dRedo = new();

    static QuestStep CloneStep(QuestStep x) => new()
    {
        Kind = x.Kind, Name = x.Name, Description = x.Description, Npc = x.Npc, Text = x.Text, MapTag = x.MapTag,
        Areas = x.Areas.Select(a => new QuestArea { X0 = a.X0, Y0 = a.Y0, Z0 = a.Z0, X1 = a.X1, Y1 = a.Y1, Z1 = a.Z1 }).ToList(),
        Monsters = x.Monsters.ToList(), Drops = x.Drops.ToList(), Items = x.Items.ToList(), WaitSeconds = x.WaitSeconds,
    };
    Snap Take() => new(_d.Steps.Select(CloneStep).ToList(), _items.Items.Cast<ListViewItem>().Select(i => ((int, int))i.Tag!).ToList(), _stepList.SelectedIndex);
    void Remember() { _dUndo.Add(Take()); if (_dUndo.Count > 100) _dUndo.RemoveAt(0); _dRedo.Clear(); }

    void Restore(Snap s)
    {
        _d.Steps.Clear(); _d.Steps.AddRange(s.Steps.Select(CloneStep));
        _items.Items.Clear();
        foreach (var (id, n) in s.Items) _items.Items.Add(new ListViewItem(new[] { id.ToString(), _host.NameOf(id) ?? "?", n.ToString() }) { Tag = (id, n) });
        RefreshStepList(Math.Max(0, s.Step));
    }

    bool UndoDraft(bool redo)
    {
        var from = redo ? _dRedo : _dUndo; var to = redo ? _dUndo : _dRedo;
        if (from.Count == 0) return false;
        to.Add(Take()); var s = from[^1]; from.RemoveAt(from.Count - 1);
        Restore(s);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // nas caixas de texto vale o desfazer da própria caixa
        if ((keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y)) && ActiveControlDeep() is not TextBoxBase)
        {
            if (!UndoDraft(redo: keyData.HasFlag(Keys.Y))) System.Media.SystemSounds.Beep.Play();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    Control? ActiveControlDeep()
    {
        Control? c = FindForm()?.ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl is { } inner) c = inner;
        return c;
    }

    // ---------- rascunho ----------

    /// <summary>Quest do jogo: NPC, tipo e etapas ficam só para ver. Mudar isso exige a aba Quests.</summary>
    void LockForKeep()
    {
        // valor do jogo pode passar do limite do campo: cortar aqui mudaria a quest sem ninguém pedir
        foreach (var n in new[] { _minLv, _maxLv, _gold, _exp, _sp, _rep }) { n.Minimum = int.MinValue; n.Maximum = uint.MaxValue; }
        var note = Loc.T("qe.keepNote", "Quest do jogo: aqui mudam nome, textos, requisitos e prêmios. O resto fica como está; NPC, tipo e etapas se mudam na aba Quests.");
        foreach (int i in new[] { 0, 1 })
        {
            var page = (FlowLayoutPanel)_pages[i].Page;
            foreach (Control c in page.Controls) if (c != _name) c.Enabled = false;
            var label = new Label { Text = note, AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.FromArgb(250, 204, 21), Margin = new Padding(0, 0, 0, 8) };
            page.Controls.Add(label);
            page.Controls.SetChildIndex(label, 0);
        }
    }

    void Load()
    {
        if (_d.Keep) LockForKeep();
        _id.Value = Math.Max(1, _d.Id); _giver.Value = _d.Giver; _name.Text = _d.Name;
        foreach (var tb in _typeButtons) tb.Checked = _d.Key ? tb.Name == "key" : tb.Name != "key" && (int)tb.Tag! == (_d.Type is 0 or < 100 ? 101 : _d.Type);
        int rep = _d.Choices.FirstOrDefault(c => c.Option.Id == "repeat")?.Value is long f and >= 1 and <= 3 ? (int)f : _d.Choices.Any(c => c.Option.Id == "redo") ? -1 : 0;
        foreach (var rb in _repeatButtons) rb.Checked = (int)rb.Tag! == rep;
        if (!_repeatButtons.Any(rb => rb.Checked) && _repeatButtons.Count > 0) _repeatButtons[0].Checked = true;
        _order.SelectedIndex = (int)_d.Order;
        RefreshStepList(0);
        _minLv.Value = Math.Clamp(_d.MinLevel, _minLv.Minimum, _minLv.Maximum); _maxLv.Value = Math.Clamp(_d.MaxLevel, _maxLv.Minimum, _maxLv.Maximum);
        // editar quest: prêmio, itens e textos vêm da quest (na criação estão vazios)
        _gold.Value = Math.Clamp(_d.Gold, (long)_gold.Minimum, (long)_gold.Maximum); _exp.Value = Math.Clamp(_d.Exp, (long)_exp.Minimum, (long)_exp.Maximum);
        _sp.Value = Math.Clamp(_d.Sp, (long)_sp.Minimum, (long)_sp.Maximum); _rep.Value = Math.Clamp(_d.Reputation, (long)_rep.Minimum, (long)_rep.Maximum);
        _items.Items.Clear();
        foreach (var (id, n) in _d.Items) _items.Items.Add(new ListViewItem(new[] { id.ToString(), _host.NameOf(id) ?? "?", n.ToString() }) { Tag = (id, n) });
        _howItems.SelectedIndex = _d.RandomItem ? 1 : 0;
        _desc.Text = _d.Description; _offer.Text = _d.OfferText;
        _giverName.Text = _d.Giver > 0 ? _host.NameOf(_d.Giver) ?? "" : "";
    }

    QuestDraft Collect()
    {
        _d.Id = (int)_id.Value; _d.Name = _name.Text.Trim(); _d.Giver = (int)_giver.Value;
        _d.Order = (QuestStepOrder)Math.Max(0, _order.SelectedIndex);
        foreach (var s in _d.Steps) { s.Name = s.Name.Trim(); s.Description = s.Description.Trim(); s.Text = s.Text.Trim(); }
        _d.MinLevel = (int)_minLv.Value; _d.MaxLevel = (int)_maxLv.Value;
        _d.Gold = (long)_gold.Value; _d.Exp = (long)_exp.Value; _d.Sp = (long)_sp.Value; _d.Reputation = (long)_rep.Value;
        _d.Items = _items.Items.Cast<ListViewItem>().Select(i => ((int, int))i.Tag!).ToList();
        _d.Description = _desc.Text.Trim(); _d.OfferText = _offer.Text.Trim();
        _d.Choices = _choices.Select(c => c.Choice).ToList();
        switch (_repeatButtons.FirstOrDefault(rb => rb.Checked)?.Tag)
        {
            case -1 when _redoOpt is not null: _d.Choices.Add(new QuestChoice(_redoOpt, true)); break;
            case int f when f > 0 && _repeatOpt is not null: _d.Choices.Add(new QuestChoice(_repeatOpt, (long)f)); break;   // liga também m_bCanRedo
        }
        _d.RandomItem = _howItems.SelectedIndex == 1;
        return _d;
    }

    void RefreshSummary()
    {
        var errors = _host.Validate(Collect());
        _errors.ForeColor = errors.Count == 0 ? Good : Bad;
        _errors.Text = errors.Count == 0 ? Loc.T("qe.ok", "✓ Tudo certo para concluir.") : Loc.T("qe.missing", "Falta resolver:") + "\n• " + string.Join("\n• ", errors);
        _summary.Text = _host.Summary(_d).Replace("\n", "\r\n");
    }

    void Show(int index)
    {
        _at = index;
        _page.Controls.Clear();
        _stepper.Current = index;
        _back.Enabled = index > 0; _next.Visible = index < _pages.Count - 1;
        _pages[index].OnShow?.Invoke();
        ShowPreview(index == _pages.Count - 1);
        _page.Controls.Add(_pages[index].Page);
    }

    // no Resumo: a janela de quests do jogo à esquerda, só com esta quest
    QuestPreview? _preview;
    // no centro do mapa: a maior área ao lado do editor, sem contar as barras
    void CenterPreview()
    {
        if (_preview?.Parent is not { } host) return;
        var map = host.Controls.Cast<Control>().Where(c => c != this && c != _preview && c.Visible).OrderByDescending(c => c.Width * c.Height).FirstOrDefault()?.Bounds ?? host.ClientRectangle;
        if (map.Right > Left) map.Width = Math.Max(0, Left - map.Left);
        _preview.Location = new Point(map.Left + Math.Max(0, (map.Width - _preview.Width) / 2), map.Top + Math.Max(0, (map.Height - _preview.Height) / 2));
    }
    void ShowPreview(bool on)
    {
        if (!on) { if (_preview is not null) _preview.Visible = false; return; }
        if (_preview is null && Parent is { } host && _host.ElementDir is { } element && QuestPreview.Create(element) is { } p)
        {
            _preview = p;
            host.Resize += (_, _) => CenterPreview();
            host.Controls.Add(p);
            Disposed += (_, _) => p.Dispose();
        }
        if (_preview is null) return;
        var d = Collect();
        string Goal(int i)
        {
            var t = StepText(i);
            t = t[(t.IndexOf(". ") + 2)..];
            int cut = t.LastIndexOf(" — ");
            return cut > 0 ? t[..cut] : t;
        }
        var steps = Enumerable.Range(0, d.Steps.Count).Select(i => new QuestPreview.Step(d.Steps[i].Name.Trim().Length > 0 ? d.Steps[i].Name.Trim() : KindName(d.Steps[i].Kind), d.Steps[i].Description, Goal(i))).ToList();
        _preview.Set(new QuestPreview.Data(d.Name, d.Description, steps.Count > 0 ? steps[0].Goal : "", steps, d.Gold, d.Exp, d.Sp, d.Items.Select(i => _host.ItemIcon(i.Id)).ToList()));
        CenterPreview();
        _preview.Visible = true; _preview.BringToFront();
    }

    bool Editing => _d.Editing > 0;
    string Title() => Editing ? string.Format(Loc.T("qe.titleEdit", "Editar quest {0}"), _d.Editing) : Loc.T("qe.titleNew", "Nova quest");

    void Finish()
    {
        var errors = _host.Validate(Collect());
        if (errors.Count > 0) { Show(_pages.Count - 1); return; }
        if (MessageBox.Show(FindForm(), _host.Summary(_d), Editing ? Loc.T("qe.confirmEdit", "Gravar as mudanças nesta quest?") : Loc.T("qe.confirm", "Criar esta quest?"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        if (_host.Commit(_d) is { } err) { MessageBox.Show(FindForm(), err, Editing ? Loc.T("qe.failedEdit", "Não foi possível mudar a quest") : Loc.T("qe.failed", "Não foi possível criar a quest"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Created?.Invoke(_d.Id);
    }

    /// <summary>Esc no Mundo 3D: a mesma pergunta do botão de fechar.</summary>
    internal void AskCancel() => Cancel();

    void Cancel()
    {
        if (MessageBox.Show(FindForm(), Editing ? Loc.T("qe.cancelEditAsk", "Descartar as mudanças? A quest fica como estava.") : Loc.T("qe.cancelAsk", "Descartar esta quest? Nada foi criado ainda."), Loc.T("qe.cancel", "Cancelar"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Cancelled?.Invoke();
    }
}
