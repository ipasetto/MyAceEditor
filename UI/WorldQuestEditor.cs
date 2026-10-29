using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Painel da direita do Mundo 3D para criar quest (docs/QUESTS-3D.md): passos como atalhos (NPCs, Requisitos, Prêmios,
/// Textos, Resumo), pode pular entre eles; vale até Concluir ou Cancelar. NPCs são escolhidos clicando no mundo.
/// Tipo 1 = recado (NPC que dá → NPC que recebe).
/// </summary>
internal sealed class WorldQuestEditor : Panel
{
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Bg2 = Color.FromArgb(36, 41, 49), Field = Color.FromArgb(18, 20, 24), Fg = Color.Gainsboro,
        Accent = Color.FromArgb(120, 190, 255), Bad = Color.FromArgb(255, 140, 120), Good = Color.FromArgb(140, 220, 140);

    readonly IQuestHost _host;
    readonly QuestWorld _world;
    QuestDraft _d;
    readonly Label _title = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly FlowLayoutPanel _steps = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Bg2, Padding = new Padding(4, 4, 4, 4) };
    readonly Panel _page = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 6, 14, 6) };
    readonly List<(Button Step, Control Page, Action? OnShow)> _pages = new();
    readonly ToolTip _tip = new() { AutoPopDelay = 15000 };
    int _at;

    // controles do rascunho
    readonly NumericUpDown _id = Num(1, int.MaxValue), _giver = Num(0, int.MaxValue), _stepNpc = Num(0, int.MaxValue),
        _minLv = Num(0, 1000), _maxLv = Num(0, 1000), _gold = Num(0, int.MaxValue), _exp = Num(0, int.MaxValue), _sp = Num(0, int.MaxValue),
        _rep = Num(0, int.MaxValue), _itemId = Num(0, int.MaxValue), _itemN = Num(1, 99999);
    readonly TextBox _name = Text(false), _desc = Text(true), _offer = Text(true), _summary = Text(true),
        _stepName = Text(false), _stepDesc = Text(true), _stepText = Text(true);
    readonly Label _giverName = Muted(), _stepNpcName = Muted(), _itemName = Muted(), _errors = new() { AutoSize = true, ForeColor = Bad, MaximumSize = new Size(330, 0) },
        _stepHint = new() { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 4, 3, 3) };

    // etapas (tipo 2+): lista, ordem e o detalhe da etapa selecionada
    readonly ComboBox _order = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly ListBox _stepList = new() { Width = 330, Height = 96, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    readonly FlowLayoutPanel _stepBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _talkBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _reachBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) },
        _nameBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    readonly ListView _areaList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 92, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    readonly NumericUpDown[] _areaNum = Enumerable.Range(0, 6).Select(_ => new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 1, Width = 100, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle }).ToArray();
    bool _loading;

    // rodapé: Concluir só fica ativo sem pendências; o rótulo diz quantas e quais (o botão desativado não mostra dica)
    readonly Button _finish;
    readonly Label _pending = new() { AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(3, 7, 8, 0) };
    readonly System.Windows.Forms.Timer _check = new() { Interval = 600 };
    readonly List<QuestChoiceBlock> _choices = new();
    readonly ComboBox _howItems = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 4, 3, 2) };
    readonly ListView _items = new() { View = View.Details, FullRowSelect = true, Height = 130, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };

    /// <summary>Quest criada (ID) — o Mundo 3D fecha o painel e mostra o NPC.</summary>
    public event Action<int>? Created;
    public event Action? Cancelled;

    public WorldQuestEditor(IQuestHost host, QuestDraft draft, QuestWorld world)
    {
        _host = host; _d = draft; _world = world;
        if (_d.Steps.Count == 0) _d.Steps.Add(new QuestStep { Kind = QuestStepKind.Talk, Npc = _d.Receiver, Text = _d.RewardText });   // começa como recado
        Dock = DockStyle.Right; Width = 380; BackColor = Bg; ForeColor = Fg;
        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Bg2 };
        var close = new Button { Dock = DockStyle.Right, Width = 34, Text = "✕", FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Cancel();
        head.Controls.Add(_title); head.Controls.Add(close);
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, BackColor = Bg2, Padding = new Padding(4, 6, 4, 4) };
        var finish = _finish = Btn(Loc.T("qe.finish", "Concluir"), Finish, Color.FromArgb(40, 110, 60));
        var cancel = Btn(Loc.T("qe.cancel", "Cancelar"), Cancel);
        var next = Btn(Loc.T("qe.next", "Avançar ▶"), () => Show(Math.Min(_pages.Count - 1, _at + 1)));
        var back = Btn(Loc.T("qe.back", "◀ Voltar"), () => Show(Math.Max(0, _at - 1)));
        foot.Height = 64; foot.WrapContents = true;
        foot.Controls.AddRange(new Control[] { finish, cancel, next, back, _pending });
        foot.SetFlowBreak(back, true);
        _tip.SetToolTip(finish, Loc.T("qe.finishTip", "Cria a quest e liga aos NPCs (só na memória: grava no Salvar)."));
        _pending.Click += (_, _) => Show(_pages.Count - 1);
        Controls.Add(_page); Controls.Add(_steps); Controls.Add(foot); Controls.Add(head);

        BuildPages();
        Load();
        _title.Text = Loc.T("qe.titleNew", "Nova quest");
        Show(0);
        // validação contínua: Concluir só ativo quando não falta nada (a quest criada tem que funcionar no jogo)
        _check.Tick += (_, _) => { if (Visible && !IsDisposed) CheckNow(); };
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

    static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 120, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = true };
    static TextBox Text(bool multi) => new() { Multiline = multi, Width = 330, Height = multi ? 90 : 24, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    static Label Muted() => new() { AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 0, 3, 6) };

    Button Btn(string text, Action a, Color? bg = null)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = bg ?? Bg, Height = 28, Margin = new Padding(3, 0, 3, 0) };
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90);
        b.Click += (_, _) => a();
        return b;
    }

    FlowLayoutPanel NewPage(string key, string pt, Action? onShow = null)
    {
        var page = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        int index = _pages.Count;
        var step = Btn($"{index + 1}. {Loc.T(key, pt)}", () => Show(index));
        _steps.Controls.Add(step);
        _pages.Add((step, page, onShow));
        return page;
    }

    void Caption(FlowLayoutPanel p, string key, string pt, string hintKey, string hint)
    {
        var l = new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Accent, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(3, 10, 3, 2) };
        _tip.SetToolTip(l, Loc.T(hintKey, hint));
        p.Controls.Add(l);
        p.Controls.Add(new Label { Text = Loc.T(hintKey, hint), AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.Gray, Margin = new Padding(3, 0, 3, 3) });
    }

    void NpcRow(FlowLayoutPanel p, NumericUpDown num, Label name)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(num);
        row.Controls.Add(Btn(Loc.T("qe.pick", "🎯 Escolher no mundo"), () =>
        {
            _title.Text = Loc.T("qe.picking", "Clique no NPC no mundo… (Esc cancela)");
            _world.PickNpc(id => { num.Value = id; _title.Text = Loc.T("qe.titleNew", "Nova quest"); });
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

        BuildStepsPage();

        var req = NewPage("qe.stepReq", "Requisitos");
        Caption(req, "qe.minLv", "Nível mínimo", "qe.minLvHint", "Abaixo deste nível o NPC não oferece a quest.");
        req.Controls.Add(_minLv);
        Caption(req, "qe.maxLv", "Nível máximo", "qe.maxLvHint", "0 = sem limite.");
        req.Controls.Add(_maxLv);
        AddChooser(req, reward: false);

        var award = NewPage("qe.stepAward", "Prêmios");
        foreach (var (num, key, pt, hk, hint) in new[]
        {
            (_gold, "qe.gold", "Ouro", "qe.goldHint", "Moedas ganhas ao entregar."),
            (_exp, "qe.exp", "Experiência", "qe.expHint", "Pontos de experiência."),
            (_sp, "qe.sp", "Espírito (SP)", "qe.spHint", "Pontos de espírito para aprender habilidades."),
            (_rep, "qe.rep", "Reputação", "qe.repHint", "Reputação ganha."),
        })
        { Caption(award, key, pt, hk, hint); award.Controls.Add(num); }
        Caption(award, "qe.items", "Itens", "qe.itemsHint", "Itens ganhos ao entregar. Abaixo da lista, escolha se o jogador ganha todos ou um sorteado.");
        _items.Columns.Add(Loc.T("wq.colId", "ID"), 70); _items.Columns.Add(Loc.T("qe.itemCol", "Item"), 190); _items.Columns.Add(Loc.T("qe.countCol", "Qtd."), 60);
        award.Controls.Add(_items);
        var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _itemId.Width = 96; _itemN.Width = 58;   // cabe na largura do painel com os dois botões
        addRow.Controls.Add(_itemId); addRow.Controls.Add(_itemN);
        addRow.Controls.Add(Btn(Loc.T("qe.add", "Adicionar"), AddItem));
        addRow.Controls.Add(Btn(Loc.T("qe.remove", "Remover"), () => { foreach (ListViewItem it in _items.SelectedItems) _items.Items.Remove(it); }));
        award.Controls.Add(addRow);
        award.Controls.Add(_itemName);
        _howItems.Items.AddRange(new object[] { Loc.T("qe.allItems", "O jogador ganha todos os itens"), Loc.T("qe.oneItem", "O jogador ganha um item sorteado da lista") });
        _howItems.SelectedIndex = 0;
        award.Controls.Add(_howItems);
        AddChooser(award, reward: true);
        _itemId.ValueChanged += (_, _) => _itemName.Text = _itemId.Value <= 0 ? "" : _host.NameOf((int)_itemId.Value) ?? Loc.T("qe.unknown", "(não existe no elements.data)");

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
        p.Controls.Add(_stepList);
        var add = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        add.Controls.Add(Btn(Loc.T("qe.addReach", "+ Ir até um lugar"), () => AddStep(QuestStepKind.Reach)));
        add.Controls.Add(Btn(Loc.T("qe.addTalk", "+ Falar com NPC"), () => AddStep(QuestStepKind.Talk)));
        p.Controls.Add(add);
        var move = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        move.Controls.Add(Btn("▲", () => MoveStep(-1)));
        move.Controls.Add(Btn("▼", () => MoveStep(1)));
        move.Controls.Add(Btn(Loc.T("qe.removeStep", "Remover etapa"), RemoveStep));
        p.Controls.Add(move);
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
        ar.Controls.Add(Btn(Loc.T("qe.removeArea", "Remover área"), RemoveArea));
        ar.Controls.Add(Btn(Loc.T("qe.anyHeight", "Qualquer altura"), () => { if (CurArea is { } a) { a.Y0 = -9999; a.Y1 = 9999; LoadArea(); RefreshAreas(); } }));
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
        p.Controls.Add(_stepBox);

        _stepList.SelectedIndexChanged += (_, _) => LoadStep();
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

    string StepText(int i)
    {
        var s = _d.Steps[i];
        string what = s.Kind == QuestStepKind.Talk
            ? string.Format(Loc.T("qe.stepTalkLine", "Falar com {0}"), s.Npc > 0 ? _host.NameOf(s.Npc) ?? s.Npc.ToString() : "?")
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
        bool recado = QuestBuilder.IsRecado(_d);
        _order.Enabled = _d.Steps.Count > 1;
        _stepHint.Text = recado
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
        if (s is not null)
        {
            _nameBox.Visible = !QuestBuilder.IsRecado(_d);   // recado: nome e descrição são os da quest
            _talkBox.Visible = s.Kind == QuestStepKind.Talk;
            _reachBox.Visible = s.Kind == QuestStepKind.Reach;
            _stepName.Text = s.Name; _stepDesc.Text = s.Description; _stepText.Text = s.Text;
            _stepNpc.Value = Math.Max(0, s.Npc);
            _stepNpcName.Text = s.Npc <= 0 ? "" : _host.NameOf(s.Npc) ?? Loc.T("qe.unknown", "(não existe no elements.data)");
        }
        _loading = false;
        RefreshAreas();
    }

    void AddStep(QuestStepKind kind)
    {
        _d.Steps.Add(new QuestStep { Kind = kind });
        RefreshStepList(_d.Steps.Count - 1);
        if (kind == QuestStepKind.Reach) ShowAreas();
    }

    void MoveStep(int by)
    {
        int i = _stepList.SelectedIndex, j = i + by;
        if (i < 0 || j < 0 || j >= _d.Steps.Count) return;
        (_d.Steps[i], _d.Steps[j]) = (_d.Steps[j], _d.Steps[i]);
        RefreshStepList(j);
    }

    void RemoveStep()
    {
        if (CurStep is null) return;
        if (_d.Steps.Count == 1) { MessageBox.Show(FindForm(), Loc.T("qe.lastStep", "A quest precisa de pelo menos uma etapa."), Loc.T("qe.stepSteps", "Etapas")); return; }
        int i = _stepList.SelectedIndex;
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
            _title.Text = Loc.T("qe.titleNew", "Nova quest");
            if (CurStep != s) return;
            s.MapTag = tag; s.Areas.Add(a);
            RefreshAreas(); RefreshStepText();
            _areaList.Items[^1].Selected = true;
        });
    }

    void RemoveArea()
    {
        if (CurStep is not { Kind: QuestStepKind.Reach } s || CurArea is not { } a) return;
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
        var opts = _host.Options(reward);
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DropDownWidth = 300 };
        foreach (var o in opts) box.Items.Add(o.Text);
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        var holder = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        // nada escolhido = sem requisitos / sem prêmios a mais (dito na tela)
        var none = new Label { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Good, Margin = new Padding(3, 6, 3, 3),
            Text = reward ? Loc.T("qe.noMoreAward", "Nenhum prêmio a mais: só o que está acima.") : Loc.T("qe.noReq", "Sem requisitos: qualquer jogador a partir do nível mínimo pode aceitar.") };
        holder.Controls.Add(none);
        void Sync() => none.Visible = !holder.Controls.OfType<QuestChoiceBlock>().Any();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(box);
        row.Controls.Add(Btn(Loc.T("qe.add", "Adicionar"), () =>
        {
            if (box.SelectedIndex < 0) return;
            var o = opts[box.SelectedIndex];
            if (o.Type != QOType.Field && _choices.Any(c => c.Option == o)) return;   // cada opção uma vez (menos "outro campo")
            var b = new QuestChoiceBlock(o, _host, _tip);
            b.Removed += x => { _choices.Remove(x); holder.Controls.Remove(x); x.Dispose(); Sync(); };
            _choices.Add(b); holder.Controls.Add(b); Sync();
        }));
        box.SelectedIndexChanged += (_, _) => { if (box.SelectedIndex >= 0) _tip.SetToolTip(box, opts[box.SelectedIndex].Tip); };
        page.Controls.Add(row);
        page.Controls.Add(holder);
    }

    void AddItem()
    {
        int id = (int)_itemId.Value;
        if (id <= 0 || _host.NameOf(id) is not { } name) { _itemName.Text = Loc.T("qe.unknown", "(não existe no elements.data)"); return; }
        _items.Items.Add(new ListViewItem(new[] { id.ToString(), name, ((int)_itemN.Value).ToString() }) { Tag = (id, (int)_itemN.Value) });
    }

    // ---------- rascunho ----------

    void Load()
    {
        _id.Value = Math.Max(1, _d.Id); _giver.Value = _d.Giver; _name.Text = _d.Name;
        _order.SelectedIndex = (int)_d.Order;
        RefreshStepList(0);
        _minLv.Value = _d.MinLevel; _maxLv.Value = _d.MaxLevel;
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
        for (int i = 0; i < _pages.Count; i++) _pages[i].Step.BackColor = i == index ? Color.FromArgb(50, 90, 140) : Bg;
        _pages[index].OnShow?.Invoke();
        _page.Controls.Add(_pages[index].Page);
    }

    void Finish()
    {
        var errors = _host.Validate(Collect());
        if (errors.Count > 0) { Show(_pages.Count - 1); return; }
        if (MessageBox.Show(FindForm(), _host.Summary(_d), Loc.T("qe.confirm", "Criar esta quest?"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        if (_host.Commit(_d) is { } err) { MessageBox.Show(FindForm(), err, Loc.T("qe.failed", "Não foi possível criar a quest"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Created?.Invoke(_d.Id);
    }

    void Cancel()
    {
        if (MessageBox.Show(FindForm(), Loc.T("qe.cancelAsk", "Descartar esta quest? Nada foi criado ainda."), Loc.T("qe.cancel", "Cancelar"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Cancelled?.Invoke();
    }
}
