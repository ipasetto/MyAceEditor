namespace MyAceEditor.UI;

/// <summary>Quest ligada a um NPC: pelo serviço do NPC no elements, pelo campo da quest, ou pelos dois.</summary>
public sealed record NpcQuestRef(int Id, string Name, bool InService, bool InField);

/// <summary>Quests que um NPC dá e recebe (docs/QUESTS-3D.md).</summary>
public sealed record NpcQuestInfo(int NpcId, string NpcName, IReadOnlyList<NpcQuestRef> Gives, IReadOnlyList<NpcQuestRef> Takes);

/// <summary>
/// Painel do NPC no Mundo 3D: ao clicar num NPC sobe só a barra (nome + Criar quest, Dar quest existente, Ver quests);
/// "Ver quests" abre as listas de quests que ele dá e recebe (duplo clique abre a quest na aba Quests).
/// </summary>
internal sealed class WorldQuestPanel : Panel
{
    const int Full = 240, Header = 32;
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Bg2 = Color.FromArgb(36, 41, 49), Fg = Color.Gainsboro, Muted = Color.Silver,
        Warn = Color.FromArgb(240, 190, 90);

    readonly Label _title = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 3, 4, 0) };
    readonly Button _create, _link, _min, _close;
    readonly Label _givesTitle = Caption(), _takesTitle = Caption();
    readonly ListView _gives = List(), _takes = List();
    readonly ToolTip _tip = new();
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 12 };
    int _target;
    bool _minimized;
    NpcQuestInfo? _info;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? OpenQuest { get; set; }
    /// <summary>Criar quest com o NPC como NPC que dá (ID do NPC). Null = botão desligado.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? CreateQuest { get => _createQuest; set { _createQuest = value; ApplyText(); } }
    Action<int>? _createQuest;
    /// <summary>Dar uma quest existente ao NPC (ID do NPC). Null = botão desligado.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? LinkQuest { get => _linkQuest; set { _linkQuest = value; ApplyText(); } }
    Action<int>? _linkQuest;

    public WorldQuestPanel()
    {
        Dock = DockStyle.Bottom; Height = 0; Visible = false; BackColor = Bg; ForeColor = Fg;
        Button Btn(Action a) { var b = new Button { AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Bg2, Margin = new Padding(3, 0, 0, 0), Height = 26 }; b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90); b.Click += (_, _) => a(); return b; }
        _create = Btn(() => { if (_info is not null) _createQuest?.Invoke(_info.NpcId); });
        _link = Btn(() => { if (_info is not null) _linkQuest?.Invoke(_info.NpcId); });
        _min = Btn(() => SetMinimized(!_minimized));
        _close = Btn(HidePanel);
        _buttons.Controls.AddRange(new Control[] { _create, _link, _min, _close });
        var head = new Panel { Dock = DockStyle.Top, Height = Header, BackColor = Bg2 };
        head.Controls.Add(_title); head.Controls.Add(_buttons);
        head.DoubleClick += (_, _) => SetMinimized(!_minimized);
        _title.DoubleClick += (_, _) => SetMinimized(!_minimized);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(6, 4, 6, 6) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(_givesTitle, 0, 0); body.Controls.Add(_takesTitle, 1, 0);
        body.Controls.Add(_gives, 0, 1); body.Controls.Add(_takes, 1, 1);
        Controls.Add(body); Controls.Add(head);

        foreach (var lv in new[] { _gives, _takes })
        {
            lv.ItemActivate += (_, _) => { if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is NpcQuestRef q) OpenQuest?.Invoke(q.Id); };
            lv.Resize += (_, _) => { if (lv.Columns.Count == 3) lv.Columns[1].Width = Math.Max(120, lv.ClientSize.Width - lv.Columns[0].Width - lv.Columns[2].Width - 4); };
        }
        _anim.Tick += (_, _) =>
        {
            int step = Math.Max(8, Math.Abs(_target - Height) / 3);
            Height = _target > Height ? Math.Min(_target, Height + step) : Math.Max(_target, Height - step);
            if (Height == _target) { _anim.Stop(); if (_target == 0) Visible = false; }
        };
        Disposed += (_, _) => { _anim.Dispose(); _tip.Dispose(); };
        ApplyText();
    }

    static Label Caption() => new() { Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };

    static ListView List()
    {
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(22, 25, 30), ForeColor = Fg, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        lv.Columns.Add("", 70); lv.Columns.Add("", 220); lv.Columns.Add("", 130);
        return lv;
    }

    void ApplyText()
    {
        _create.Text = Loc.T("wq.create", "+ Criar quest com este NPC");
        _link.Text = Loc.T("wq.link", "Dar quest existente");
        _create.Enabled = _createQuest is not null; _link.Enabled = _linkQuest is not null;
        var soon = Loc.T("wq.soon", "Em construção: próximo passo.");
        _tip.SetToolTip(_create, _createQuest is null ? soon : Loc.T("wq.createTip", "Nova quest com este NPC como o NPC que dá a quest."));
        _tip.SetToolTip(_link, _linkQuest is null ? soon : Loc.T("wq.linkTip", "Escolher uma quest que já existe para este NPC dar (aplicado no Salvar)."));
        _min.Text = _info is null ? "" : ListText();
        _close.Text = "✕";
        _tip.SetToolTip(_min, Loc.T("wq.listTip", "Mostra ou esconde as quests que este NPC já dá e recebe."));
        _tip.SetToolTip(_close, Loc.T("wq.close", "Fechar"));
        foreach (var lv in new[] { _gives, _takes })
        {
            lv.Columns[0].Text = Loc.T("wq.colId", "ID"); lv.Columns[1].Text = Loc.T("wq.colName", "Quest"); lv.Columns[2].Text = Loc.T("wq.colSource", "Ligada por");
        }
    }

    public void ShowNpc(NpcQuestInfo info)
    {
        _info = info;
        ApplyText();
        _title.Text = string.Format(Loc.T("wq.title", "Quests de {0}  (NPC {1})"), info.NpcName, info.NpcId);
        _givesTitle.Text = string.Format(Loc.T("wq.gives", "Dá a quest ({0})"), info.Gives.Count);
        _takesTitle.Text = string.Format(Loc.T("wq.takes", "Recebe a quest ({0})"), info.Takes.Count);
        Fill(_gives, info.Gives, gives: true);
        Fill(_takes, info.Takes, gives: false);
        _tip.SetToolTip(_title, Loc.T("wq.hint", "Clique duas vezes numa quest para abrir. Clique duas vezes aqui para minimizar."));
        Visible = true; BringToFront();
        SetMinimized(true);   // primeiro só a barra; a lista abre em "Ver quests"
        ApplyText();
    }

    void Fill(ListView lv, IReadOnlyList<NpcQuestRef> list, bool gives)
    {
        lv.BeginUpdate();
        lv.Items.Clear();
        foreach (var q in list.OrderBy(q => q.Id))
        {
            var src = q.InService && q.InField ? Loc.T("wq.srcBoth", "serviço e quest")
                : q.InService ? Loc.T("wq.srcService", "serviço do NPC") : Loc.T("wq.srcField", "só a quest");
            var it = new ListViewItem(new[] { q.Id.ToString(), q.Name.Length > 0 ? q.Name : Loc.T("wq.noQuest", "(quest não existe no tasks.data)"), src }) { Tag = q };
            if (!q.InService)
            {
                it.ForeColor = Warn;
                it.ToolTipText = gives
                    ? Loc.T("wq.tipFieldGives", "A quest diz que este NPC a dá, mas o NPC não tem a quest no serviço de dar quests: no jogo ele não a oferece.")
                    : Loc.T("wq.tipFieldTakes", "A quest manda entregar a este NPC, mas ele não tem a quest no serviço de receber: no jogo não dá para entregar aqui.");
            }
            else if (q.Name.Length == 0) { it.ForeColor = Warn; it.ToolTipText = Loc.T("wq.tipMissing", "O serviço do NPC lista um ID que não existe no tasks.data."); }
            else it.ToolTipText = Loc.T("wq.tipOpen", "Clique duas vezes para abrir a quest.");
            lv.Items.Add(it);
        }
        if (list.Count == 0) lv.Items.Add(new ListViewItem(new[] { "", Loc.T("wq.none", "(nenhuma)"), "" }) { ForeColor = Muted });
        lv.EndUpdate();
    }

    string ListText() => _minimized
        ? string.Format(Loc.T("wq.showList", "Ver quests ({0} dá / {1} recebe) ▴"), _info!.Gives.Count, _info.Takes.Count)
        : Loc.T("wq.hideList", "Esconder quests ▾");

    void SetMinimized(bool min)
    {
        _minimized = min;
        if (_info is not null) _min.Text = ListText();
        Animate(min ? Header : Full);
    }

    public void HidePanel() { _info = null; Animate(0); }

    void Animate(int target) { _target = target; _anim.Start(); }
}
