namespace MyAceEditor.UI;

/// <summary>Um item que o monstro deixa cair.</summary>
internal sealed record DropRow(int Id, string Name, Image? Icon, double Chance);
/// <summary>Drops do monstro: chance de cair 0, 1, 2 e 3 itens e a lista.</summary>
internal sealed record CreatureDrops(IReadOnlyList<double> Count, IReadOnlyList<DropRow> Items);

/// <summary>
/// Painel de baixo do Mundo 3D ao clicar num monstro: à esquerda os drops com ícone e chance (botão Editar drops) e, na pílula ao
/// lado, a ficha do ponto (quantidade, renascer, como anda, gatilho); à direita as quests que usam a criatura.
/// </summary>
internal sealed class WorldCreaturePanel : Panel
{
    const int Full = 268, Header = 44;
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Bg2 = Color.FromArgb(36, 41, 49), Card = Color.FromArgb(22, 25, 30), Muted = Color.FromArgb(150, 160, 175);
    readonly Label _title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11f), AutoEllipsis = true };
    readonly Button _edit, _announce, _close;
    readonly ToolTip _tip = new();
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 12 };
    readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, BackColor = Color.FromArgb(40, 46, 56), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly NumericUpDown _amount = Num(1, 1000), _respawn = Num(0, 864000), _min = Num(0, 864000), _died = Num(0, 100000);
    readonly Label _respawnText = Value(), _minText = Value(), _diedText = Value(), _walk = Value(), _trigger = Value(), _groupLabel, _questTitle = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f), TextAlign = ContentAlignment.MiddleLeft };
    readonly ListView _quests = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
        BackColor = Card, ForeColor = Color.Gainsboro, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true };
    readonly List<Label> _names = new();
    readonly ListView _drops = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
        BackColor = Card, ForeColor = Color.Gainsboro, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true };
    readonly ImageList _dropIcons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    readonly Label _dropCount = new() { Dock = DockStyle.Top, Height = 24, ForeColor = Color.Gainsboro, BackColor = Card, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), AutoEllipsis = true };
    readonly Button _pageDrops, _pagePoint, _editDrops;
    readonly Panel _pages = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(0, 2, 0, 4) };
    readonly Panel _dropsBox = new() { Dock = DockStyle.Fill, BackColor = Card };
    readonly TableLayoutPanel _sheet;
    Panel _questBox = null!;
    int _target, _token, _id;
    bool _filling;
    IReadOnlyList<SpawnRow> _rows = Array.Empty<SpawnRow>();

    /// <summary>Grava no npcgen (Ctrl+Z do NPCGen): grupo, quantidade, renascer (s), mortes até parar.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<SpawnRow, int, int, int, int>? Apply { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? OpenQuest { get; set; }
    /// <summary>Abrir a criatura no editor de elements (ID).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? EditCreature { get; set; }
    /// <summary>Anúncios do monstro (AnnounceDialog / AiAnnounce). Null = botão escondido.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? Announce { get; set; }
    /// <summary>Drops do monstro. Null ou sem resultado = o painel mostra só a ficha do ponto.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Func<int, CreatureDrops?>? DropsOf { get; set; }
    /// <summary>Editor de drops do MyAce para este monstro (ID).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? EditDrops { get; set; }

    static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 90, BackColor = Color.FromArgb(40, 46, 56), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 6, 2) };
    static Label Value() => new() { AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 0) };

    public WorldCreaturePanel()
    {
        Dock = DockStyle.Bottom; Height = 0; Visible = false; BackColor = Bg; ForeColor = Color.Gainsboro;
        Button Btn(Action a, string glyph)
        {
            var b = new Button { AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Bg2, Height = 32, Margin = new Padding(4, 0, 0, 0),
                Image = UiKit.Icon(glyph, Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(6, 0, 6, 0), Cursor = Cursors.Hand, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(34, 32) };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90);
            b.Click += (_, _) => a(); return b;
        }
        _edit = Btn(() => { if (_id > 0) EditCreature?.Invoke(_id); }, "");
        _announce = Btn(() => { if (_id > 0) Announce?.Invoke(_id); }, "\uE789");
        _close = Btn(HidePanel, "");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 6, 0) };
        buttons.Controls.AddRange(new Control[] { _announce, _edit, _close });
        var head = new Panel { Dock = DockStyle.Top, Height = Header, BackColor = Bg2 };
        var icon = new PictureBox { Dock = DockStyle.Left, Width = 40, Image = UiKit.Icon("", Color.FromArgb(240, 120, 110), 22), SizeMode = PictureBoxSizeMode.CenterImage };
        head.Controls.Add(_title); head.Controls.Add(icon); head.Controls.Add(buttons);

        // ficha do ponto: nome | valor | explicação, uma linha por informação
        var sheet = _sheet = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Card, Padding = new Padding(10, 6, 10, 6) };
        sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label Name() { var l = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 6, 0) }; _names.Add(l); return l; }
        _groupLabel = Name();
        void Row(Control name, Control value, Control? extra) { int r = sheet.RowCount++; sheet.Controls.Add(name, 0, r); sheet.Controls.Add(value, 1, r); if (extra is not null) sheet.Controls.Add(extra, 2, r); }
        Row(_groupLabel, _group, null); sheet.SetColumnSpan(_group, 2);
        Row(Name(), _amount, null);
        Row(Name(), _respawn, _respawnText);
        Row(Name(), _min, _minText);
        var walkName = Name(); Row(walkName, _walk, null); sheet.SetColumnSpan(_walk, 2);
        var trigName = Name(); Row(trigName, _trigger, null); sheet.SetColumnSpan(_trigger, 2);
        var sheetBox = new Panel { Dock = DockStyle.Left, Width = 470, Padding = new Padding(8, 8, 4, 8) };
        // drops primeiro: é o que se quer ver ao clicar num monstro; a ficha do ponto fica na outra pílula
        _pageDrops = Btn(() => ShowPage(true), "\uE7BF");
        _pagePoint = Btn(() => ShowPage(false), "\uE707");
        _editDrops = Btn(EditDropsNow, "\uE70F");
        _editDrops.Dock = DockStyle.Right; _pageDrops.Dock = _pagePoint.Dock = DockStyle.Left;
        _pages.Controls.Add(_editDrops); _pages.Controls.Add(_pagePoint); _pages.Controls.Add(_pageDrops);
        _drops.Columns.Add("", 300); _drops.Columns.Add("", 90, HorizontalAlignment.Right);
        _drops.SmallImageList = _dropIcons;
        _drops.Resize += (_, _) => _drops.Columns[0].Width = Math.Max(160, _drops.ClientSize.Width - _drops.Columns[1].Width - 4);
        _drops.HandleCreated += (_, _) => SetWindowTheme(_drops.Handle, "DarkMode_Explorer", null);
        _dropsBox.Controls.Add(_drops); _dropsBox.Controls.Add(_dropCount);
        sheetBox.Controls.Add(sheet); sheetBox.Controls.Add(_dropsBox); sheetBox.Controls.Add(_pages);

        var questBox = _questBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 2, 8, 8) };
        _quests.Columns.Add("", 64); _quests.Columns.Add("", 220); _quests.Columns.Add("", 150);
        _quests.Resize += (_, _) => _quests.Columns[1].Width = Math.Max(120, _quests.ClientSize.Width - _quests.Columns[0].Width - _quests.Columns[2].Width - 4);
        _quests.HandleCreated += (_, _) => SetWindowTheme(_quests.Handle, "DarkMode_Explorer", null);
        _quests.ItemActivate += (_, _) => { if (_quests.SelectedItems.Count > 0 && _quests.SelectedItems[0].Tag is QuestUse q) OpenQuest?.Invoke(q.Id); };
        questBox.Controls.Add(_quests); questBox.Controls.Add(_questTitle);

        Controls.Add(questBox); Controls.Add(sheetBox); Controls.Add(head);
        _group.SelectedIndexChanged += (_, _) => LoadGroup();
        foreach (var n in new[] { _amount, _respawn, _min, _died }) n.ValueChanged += (_, _) => Commit();
        _anim.Tick += (_, _) =>
        {
            int step = Math.Max(8, Math.Abs(_target - Height) / 3);
            Height = _target > Height ? Math.Min(_target, Height + step) : Math.Max(_target, Height - step);
            if (Height == _target) { _anim.Stop(); if (_target == 0) Visible = false; }
        };
        Disposed += (_, _) => { _anim.Dispose(); _tip.Dispose(); _dropIcons.Dispose(); };
        void Texts()
        {
            _minText.Text = Loc.T("wc.minNote2", "0 = sem sorteio; maior = sorteia o tempo");
            string[] n = { Loc.T("wc.group", "Criatura do ponto"), Loc.T("ci.amount", "Quantidade"), Loc.T("ci.respawn", "Renascer (s)"), Loc.T("ci.min", "Mínimo (s)"),
                Loc.T("wc.walk", "Como anda"), Loc.T("wc.trigger", "Quando nasce") };
            for (int i = 0; i < n.Length; i++) _names[i].Text = n[i];
            _edit.Text = Loc.T("wc.edit", "Abrir no editor");
            _announce.Text = Loc.T("wc.announce", "Anúncios");
            _tip.SetToolTip(_announce, Loc.T("wc.announceTip", "O que o jogo anuncia quando ele morre ou entra em combate (canal e texto)."));
            _tip.SetToolTip(_edit, Loc.T("wc.editTip", "Abre o monstro no editor de elements (nível, drops, modelo...)."));
            _tip.SetToolTip(_close, Loc.T("wq.close", "Fechar"));
            _pageDrops.Text = Loc.T("wc.pageDrops", "Drops");
            _pagePoint.Text = Loc.T("wc.pagePoint", "Ponto no mapa");
            _editDrops.Text = Loc.T("wc.editDrops", "Editar drops");
            _tip.SetToolTip(_editDrops, Loc.T("wc.editDropsTip", "Abre o editor de drops deste monstro. Vale para todos os pontos dele."));
            _drops.Columns[0].Text = Loc.T("wc.colItem", "Item"); _drops.Columns[1].Text = Loc.T("wc.colChance", "Chance");
            _quests.Columns[0].Text = Loc.T("wq.colId", "ID"); _quests.Columns[1].Text = Loc.T("wq.colName", "Quest"); _quests.Columns[2].Text = Loc.T("ci.how", "Como usa");
        }
        Texts();
        Loc.Changed += Texts; Disposed += (_, _) => Loc.Changed -= Texts;
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

    SpawnRow? Current => _group.SelectedIndex >= 0 && _group.SelectedIndex < _rows.Count ? _rows[_group.SelectedIndex] : null;

    /// <summary>Mostra o ponto escolhido; as quests vêm em segundo plano (a 1ª busca leva alguns segundos).</summary>
    /// <summary>Coletável (mina, planta): o que dá, com ícone e chance, e uma linha com quantidade e renascer. Editar abre no Elements.</summary>
    public void ShowMine(int id, string name, CreatureDrops? products, string about)
    {
        _announce.Visible = false;
        _id = id; _rows = [];
        _token++;
        _title.Text = string.Format(Loc.T("wc.title", "{0}   ·   ID {1}"), name, id);
        FillDrops(products, about);
        _pagePoint.Visible = _editDrops.Visible = false;
        _pageDrops.Text = Loc.T("wc.pageGives", "O que dá");
        _questBox.Visible = false;
        ShowPage(true);
        Visible = true; BringToFront();
        _target = Full; _anim.Start();
    }

    public void ShowCreature(int id, string name, IReadOnlyList<SpawnRow> rows, Func<IReadOnlyList<QuestUse>>? quests)
    {
        _pagePoint.Visible = _questBox.Visible = true;
        _pageDrops.Text = Loc.T("wc.pageDrops", "Drops");
        _announce.Visible = Announce is not null;
        _id = id; _rows = rows;
        _title.Text = string.Format(Loc.T("wc.title", "{0}   ·   ID {1}"), name, id);
        _group.BeginUpdate(); _group.Items.Clear();
        foreach (var r in rows) _group.Items.Add($"{r.Group.Id} × {r.Group.Amount}");
        _group.EndUpdate();
        _group.Visible = _groupLabel.Visible = rows.Count > 1;   // um grupo só: a linha não diz nada
        if (rows.Count > 0) _group.SelectedIndex = 0; else LoadGroup();
        ShowPage(LoadDrops());
        int token = ++_token;
        if (quests is null) SetQuests(Loc.T("ci.noTasks", "Sem tasks.data aberto."), Array.Empty<QuestUse>(), false);
        else
        {
            SetQuests(Loc.T("ci.questsLoading", "Procurando quests…"), Array.Empty<QuestUse>(), true);
            Task.Run(quests).ContinueWith(t =>
            {
                if (IsDisposed || t.IsFaulted || token != _token) return;
                BeginInvoke(() => { if (token == _token) SetQuests(string.Format(Loc.T("wc.questTitle", "Quests que usam ({0}) · duplo clique abre"), t.Result.Count), t.Result, false); });
            }, TaskScheduler.Default);
        }
        Visible = true; BringToFront();
        _target = Full; _anim.Start();
    }

    /// <summary>Preenche os drops; false = esta criatura não tem (a pílula some e fica a ficha do ponto).</summary>
    bool LoadDrops()
    {
        var d = DropsOf?.Invoke(_id);
        _editDrops.Visible = EditDrops is not null;
        FillDrops(d, null);
        return d is not null;
    }

    void FillDrops(CreatureDrops? d, string? about)
    {
        _pages.Visible = d is not null;
        _drops.BeginUpdate(); _drops.Items.Clear(); _dropIcons.Images.Clear();
        foreach (var r in d?.Items ?? [])
        {
            int icon = -1;
            if (r.Icon is { } img) { _dropIcons.Images.Add(img); icon = _dropIcons.Images.Count - 1; }
            _drops.Items.Add(new ListViewItem(new[] { r.Name, r.Chance.ToString("P2") }, icon) { ToolTipText = string.Format(Loc.T("wc.dropTip", "Item {0}"), r.Id) });
        }
        if (d is { Items.Count: 0 }) _drops.Items.Add(new ListViewItem(Loc.T("wc.dropNone", "(este monstro não deixa cair nada)")) { ForeColor = Color.Gray });
        _drops.EndUpdate();
        if (d is not null)
        {
            var parts = new List<string>();
            for (int n = 0; n < d.Count.Count; n++)
                if (d.Count[n] > 0)
                    parts.Add(string.Format(n == 0 ? Loc.T("wc.dropZero", "nada {0}") : n == 1 ? Loc.T("wc.dropOne", "1 item {0}") : Loc.T("wc.dropMany", "{1} itens {0}"), d.Count[n].ToString("P0"), n));
            _dropCount.Text = about ?? string.Format(Loc.T("wc.dropCount", "Ao morrer: {0}"), string.Join(" · ", parts));
        }
    }

    void ShowPage(bool drops)
    {
        _dropsBox.Visible = drops; _sheet.Visible = !drops;
        _pageDrops.BackColor = drops ? UiKit.Accent : Bg2; _pagePoint.BackColor = drops ? Bg2 : UiKit.Accent;
    }

    void EditDropsNow()
    {
        if (_id <= 0 || EditDrops is null) return;
        EditDrops(_id);
        LoadDrops();
    }

    void SetQuests(string title, IReadOnlyList<QuestUse> list, bool loading)
    {
        _questTitle.Text = title;
        _quests.BeginUpdate(); _quests.Items.Clear();
        foreach (var q in list.OrderBy(q => q.Id))
            _quests.Items.Add(new ListViewItem(new[] { q.Id.ToString(), q.Name, q.How }) { Tag = q, ToolTipText = Loc.T("wq.tipOpen", "Clique duas vezes para abrir a quest.") });
        if (list.Count == 0 && !loading) _quests.Items.Add(new ListViewItem(new[] { "", Loc.T("ci.noQuest", "(nenhuma quest usa)"), "" }) { ForeColor = Color.Gray });
        _quests.EndUpdate();
    }

    void LoadGroup()
    {
        _filling = true;
        try
        {
            var r = Current;
            foreach (var n in new[] { _amount, _respawn, _min, _died }) n.Enabled = r is not null && Apply is not null;
            if (r is null) { _respawnText.Text = _diedText.Text = _walk.Text = _trigger.Text = ""; return; }
            var g = r.Group; var s = r.Spawn;
            _amount.Value = Math.Clamp(g.Amount, 1, 1000);
            _respawn.Value = Math.Clamp(g.Respawn, 0, 864000);
            _died.Value = Math.Clamp(g.DiedTimes, 0, 100000);
            _min.Value = Math.Clamp(g.RefreshLower, 0, 864000);
            _respawnText.Text = CreatureInfoPanel.RealRespawn(g);
            _walk.Text = g.PathId > 0
                ? string.Format(Loc.T("wc.walkPath", "patrulha o caminho {0} · {1}"), g.PathId, g.PathLoop switch
                    { 0 => Loc.T("wc.loop0", "para no fim"), 1 => Loc.T("wc.loop1", "vai e volta"), _ => Loc.T("wc.loop2", "circuito") })
                : s.Spread.X > 0.25f || s.Spread.Z > 0.25f ? string.Format(Loc.T("wc.walkArea", "anda numa área de {0:0} × {1:0} m"), s.Spread.X, s.Spread.Z)
                : Loc.T("wc.walkStill", "fica parado no ponto");
            _trigger.Text = s.Trigger > 0 ? string.Format(Loc.T("wc.trig", "só com o gatilho {0} ligado (evento do mapa)"), s.Trigger) : Loc.T("wc.always", "sempre (sem gatilho)");
        }
        finally { _filling = false; }
    }

    void Commit()
    {
        if (_filling || Current is not { } r || Apply is null) return;
        int a = (int)_amount.Value, s = (int)_respawn.Value, m = (int)_min.Value, d = (int)_died.Value;
        if (a == r.Group.Amount && s == r.Group.Respawn && m == r.Group.RefreshLower && d == r.Group.DiedTimes) return;
        Apply(r, a, s, m, d);
        LoadGroup();
    }

    public void HidePanel() { _token++; _id = 0; _target = 0; _anim.Start(); }

    /// <summary>--creature-shot: painel com dados de exemplo, para conferir o layout sem abrir o 3D.</summary>
    internal static Form ShotForm()
    {
        var f = new Form { Width = 1100, Height = 300, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false, BackColor = Color.Black };
        var p = new WorldCreaturePanel { Apply = (_, _, _, _, _) => { }, EditDrops = _ => { },
            DropsOf = _ => new CreatureDrops([0.2, 0.7, 0.1, 0], [new(1, "Pedra do Trovão", null, 0.25), new(2, "Couro Grosso", null, 0.125), new(3, "Asa de Besouro", null, 0.0125)]) };
        f.Controls.Add(p);
        var s = new MyAceEditor.PerfectWorld.NpcGenSpawn { Spread = new MyAceEditor.PerfectWorld.Vec3(20, 0, 30) };
        var g = new MyAceEditor.PerfectWorld.NpcGenCreature { Id = 61648, Amount = 1, Respawn = 600, PathId = 12, PathLoop = 1 };
        s.Creatures.Add(g);
        p.ShowCreature(61648, "Besouro da Sorte", new[] { new SpawnRow("world", s, g) }, () => new[] { new QuestUse(1001, "Caçar o besouro", "Monstro a matar") });
        return f;
    }
}
