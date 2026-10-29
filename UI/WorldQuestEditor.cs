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
    readonly Action<Action<int>> _pickNpc;
    QuestDraft _d;
    readonly Label _title = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly FlowLayoutPanel _steps = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Bg2, Padding = new Padding(4, 4, 4, 4) };
    readonly Panel _page = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 6, 14, 6) };
    readonly List<(Button Step, Control Page, Action? OnShow)> _pages = new();
    readonly ToolTip _tip = new() { AutoPopDelay = 15000 };
    int _at;

    // controles do rascunho
    readonly NumericUpDown _id = Num(1, int.MaxValue), _giver = Num(0, int.MaxValue), _receiver = Num(0, int.MaxValue),
        _minLv = Num(0, 1000), _maxLv = Num(0, 1000), _gold = Num(0, int.MaxValue), _exp = Num(0, int.MaxValue), _sp = Num(0, int.MaxValue),
        _rep = Num(0, int.MaxValue), _itemId = Num(0, int.MaxValue), _itemN = Num(1, 99999);
    readonly TextBox _name = Text(false), _desc = Text(true), _offer = Text(true), _reward = Text(true), _summary = Text(true);
    readonly Label _giverName = Muted(), _receiverName = Muted(), _itemName = Muted(), _errors = new() { AutoSize = true, ForeColor = Bad, MaximumSize = new Size(330, 0) };
    readonly List<QuestChoiceBlock> _choices = new();
    readonly ComboBox _howItems = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 4, 3, 2) };
    readonly ListView _items = new() { View = View.Details, FullRowSelect = true, Height = 130, Width = 330, BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };

    /// <summary>Quest criada (ID) — o Mundo 3D fecha o painel e mostra o NPC.</summary>
    public event Action<int>? Created;
    public event Action? Cancelled;

    public WorldQuestEditor(IQuestHost host, QuestDraft draft, Action<Action<int>> pickNpc)
    {
        _host = host; _d = draft; _pickNpc = pickNpc;
        Dock = DockStyle.Right; Width = 380; BackColor = Bg; ForeColor = Fg;
        var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Bg2 };
        var close = new Button { Dock = DockStyle.Right, Width = 34, Text = "✕", FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Cancel();
        head.Controls.Add(_title); head.Controls.Add(close);
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, BackColor = Bg2, Padding = new Padding(4, 6, 4, 4) };
        var finish = Btn(Loc.T("qe.finish", "Concluir"), Finish, Color.FromArgb(40, 110, 60));
        var cancel = Btn(Loc.T("qe.cancel", "Cancelar"), Cancel);
        var next = Btn(Loc.T("qe.next", "Avançar ▶"), () => Show(Math.Min(_pages.Count - 1, _at + 1)));
        var back = Btn(Loc.T("qe.back", "◀ Voltar"), () => Show(Math.Max(0, _at - 1)));
        foot.Controls.AddRange(new Control[] { finish, cancel, next, back });
        _tip.SetToolTip(finish, Loc.T("qe.finishTip", "Cria a quest e liga aos NPCs (só na memória: grava no Salvar)."));
        Controls.Add(_page); Controls.Add(_steps); Controls.Add(foot); Controls.Add(head);

        BuildPages();
        Load();
        _title.Text = Loc.T("qe.title", "Nova quest — recado");
        Show(0);
        Disposed += (_, _) => _tip.Dispose();
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
            _pickNpc(id => { num.Value = id; _title.Text = Loc.T("qe.title", "Nova quest — recado"); });
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
        Caption(npcs, "qe.receiver", "NPC que recebe a quest", "qe.receiverHint", "Para quem o jogador leva o recado e onde ganha o prêmio. Escolha clicando no NPC no mundo.");
        NpcRow(npcs, _receiver, _receiverName);

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
        Caption(texts, "qe.reward", "Fala ao entregar", "qe.rewardHint", "O que o NPC que recebe diz ao entregar o prêmio.");
        _reward.PlaceholderText = Loc.T("qe.rewardPh", "Ex.: Ah, uma carta do guarda! Obrigado, tome sua recompensa.");
        texts.Controls.Add(_reward);

        var sum = NewPage("qe.stepSummary", "Resumo", RefreshSummary);
        sum.Controls.Add(_errors);
        _summary.ReadOnly = true; _summary.Height = 300;
        sum.Controls.Add(_summary);
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
        _id.Value = Math.Max(1, _d.Id); _giver.Value = _d.Giver; _receiver.Value = _d.Receiver; _name.Text = _d.Name;
        _minLv.Value = _d.MinLevel; _maxLv.Value = _d.MaxLevel;
        _giverName.Text = _d.Giver > 0 ? _host.NameOf(_d.Giver) ?? "" : "";
    }

    QuestDraft Collect()
    {
        _d.Id = (int)_id.Value; _d.Name = _name.Text.Trim(); _d.Giver = (int)_giver.Value; _d.Receiver = (int)_receiver.Value;
        _d.MinLevel = (int)_minLv.Value; _d.MaxLevel = (int)_maxLv.Value;
        _d.Gold = (long)_gold.Value; _d.Exp = (long)_exp.Value; _d.Sp = (long)_sp.Value; _d.Reputation = (long)_rep.Value;
        _d.Items = _items.Items.Cast<ListViewItem>().Select(i => ((int, int))i.Tag!).ToList();
        _d.Description = _desc.Text.Trim(); _d.OfferText = _offer.Text.Trim(); _d.RewardText = _reward.Text.Trim();
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
