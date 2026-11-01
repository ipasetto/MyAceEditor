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
    const int Full = 260, Header = 44;
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Bg2 = Color.FromArgb(36, 41, 49), Fg = Color.Gainsboro, Muted = Color.Silver,
        Warn = Color.FromArgb(240, 190, 90);

    readonly Label _title = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 3, 4, 0) };
    readonly Button _create, _link, _edit, _delete, _min, _close;
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
    /// <summary>Editar a quest escolhida na lista (ID da quest). Null = botão desligado.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? EditQuest { get => _editQuest; set { _editQuest = value; ApplyText(); } }
    Action<int>? _editQuest;
    /// <summary>Excluir a quest escolhida na lista (ID da quest). Null = botão desligado.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? DeleteQuest { get => _deleteQuest; set { _deleteQuest = value; ApplyText(); } }
    Action<int>? _deleteQuest;
    NpcQuestRef? SelectedRef => new[] { _gives, _takes }.SelectMany(lv => lv.SelectedItems.Cast<ListViewItem>()).Select(i => i.Tag).OfType<NpcQuestRef>().FirstOrDefault();
    int SelectedQuest => SelectedRef?.Id ?? 0;
    /// <summary>A quest escolhida não existe no tasks.data (só o serviço do NPC aponta para ela).</summary>
    bool SelectedMissing => SelectedRef is { Name.Length: 0 };

    public WorldQuestPanel()
    {
        Dock = DockStyle.Bottom; Height = 0; Visible = false; BackColor = Bg; ForeColor = Fg;
        // botões com ícone desenhado; "Criar quest" é a ação principal (azul)
        Button Btn(Action a, string glyph, Color? back = null)
        {
            var b = new Button { AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = back ?? Bg2, Margin = new Padding(4, 0, 0, 0), Height = 32,
                Image = UiKit.Icon(glyph, Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(6, 0, 6, 0), Cursor = Cursors.Hand, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(34, 32) };
            b.FlatAppearance.BorderColor = back ?? Color.FromArgb(70, 78, 90);
            b.Click += (_, _) => a(); return b;
        }
        _create = Btn(() => { if (_info is not null) _createQuest?.Invoke(_info.NpcId); }, "\uE710", UiKit.Accent);
        _link = Btn(() => { if (_info is not null) _linkQuest?.Invoke(_info.NpcId); }, "\uE71B");
        // editar / excluir agem na quest escolhida nas listas (abrem a lista se estiver fechada)
        _edit = Btn(() => { if (SelectedQuest is var id and > 0) _editQuest?.Invoke(id); else if (_minimized) SetMinimized(false); }, "\uE70F");
        _delete = Btn(() => { if (SelectedQuest is var id and > 0) _deleteQuest?.Invoke(id); else if (_minimized) SetMinimized(false); }, "\uE74D");
        _min = Btn(() => SetMinimized(!_minimized), "\uE8FD");
        _close = Btn(HidePanel, "\uE711");
        _buttons.Controls.AddRange(new Control[] { _create, _link, _edit, _delete, _min, _close });
        _buttons.Padding = new Padding(0, 6, 6, 0);
        var head = new Panel { Dock = DockStyle.Top, Height = Header, BackColor = Bg2 };
        var npcIcon = new PictureBox { Dock = DockStyle.Left, Width = 40, Image = UiKit.Icon("\uE77B", Color.FromArgb(120, 190, 255), 22), SizeMode = PictureBoxSizeMode.CenterImage };
        head.Controls.Add(_title); head.Controls.Add(npcIcon); head.Controls.Add(_buttons);
        _title.Font = new Font("Segoe UI Semibold", 11f); _title.Padding = new Padding(0); _title.AutoEllipsis = true;
        head.DoubleClick += (_, _) => SetMinimized(!_minimized);
        _title.DoubleClick += (_, _) => SetMinimized(!_minimized);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(6, 4, 6, 6) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(_givesTitle, 0, 0); body.Controls.Add(_takesTitle, 1, 0);
        body.Controls.Add(_gives, 0, 1); body.Controls.Add(_takes, 1, 1);
        Controls.Add(body); Controls.Add(head);

        foreach (var lv in new[] { _gives, _takes })
        {
            lv.ItemActivate += (_, _) => { if (lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is NpcQuestRef q) OpenQuest?.Invoke(q.Id); };
            // escolher numa lista tira a escolha da outra: Editar/Excluir valem para uma quest só
            lv.SelectedIndexChanged += (_, _) => { if (lv.SelectedItems.Count > 0) (lv == _gives ? _takes : _gives).SelectedItems.Clear(); UpdateActions(); };
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

    static Label Caption() => new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9.5f), TextAlign = ContentAlignment.BottomLeft, ImageAlign = ContentAlignment.BottomLeft };

    static ListView List()
    {
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(22, 25, 30), ForeColor = Fg, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        lv.Columns.Add("", 70); lv.Columns.Add("", 220); lv.Columns.Add("", 130);
        return lv;
    }

    void ApplyText()
    {
        _create.Text = Loc.T("wq.create2", "Criar quest");
        _link.Text = Loc.T("wq.link", "Dar quest existente");
        _create.Enabled = _createQuest is not null; _link.Enabled = _linkQuest is not null;
        // sem ação ligada o botão some (no Mundo 3D as ações ficam na ferramenta Quests; o painel só mostra)
        _create.Visible = _createQuest is not null; _link.Visible = _linkQuest is not null;
        var soon = Loc.T("wq.soon", "Em construção: próximo passo.");
        _tip.SetToolTip(_create, _createQuest is null ? soon : Loc.T("wq.createTip", "Nova quest com este NPC como o NPC que dá a quest."));
        _tip.SetToolTip(_link, _linkQuest is null ? soon : Loc.T("wq.linkTip", "Escolher uma quest que já existe para este NPC dar (aplicado no Salvar)."));
        _edit.Text = Loc.T("wq.edit", "Editar quest");
        _delete.Text = Loc.T("wq.delete", "Excluir quest");
        UpdateActions();
        _min.Text = _info is null ? "" : ListText();
        _close.Text = "";
        _tip.SetToolTip(_min, Loc.T("wq.listTip", "Mostra ou esconde as quests que este NPC já dá e recebe."));
        _tip.SetToolTip(_close, Loc.T("wq.close", "Fechar"));
        foreach (var lv in new[] { _gives, _takes })
        {
            lv.Columns[0].Text = Loc.T("wq.colId", "ID"); lv.Columns[1].Text = Loc.T("wq.colName", "Quest"); lv.Columns[2].Text = Loc.T("wq.colSource", "Ligada por");
        }
    }

    void UpdateActions()
    {
        bool pick = SelectedQuest > 0;
        _edit.Enabled = _editQuest is not null; _delete.Enabled = _deleteQuest is not null;
        _edit.Visible = _editQuest is not null; _delete.Visible = _deleteQuest is not null;
        _tip.SetToolTip(_edit, _editQuest is null ? Loc.T("wq.soon", "Em construção: próximo passo.")
            : pick ? Loc.T("wq.editTip", "Abre a quest escolhida no assistente, com tudo preenchido, para mudar.") : Loc.T("wq.pickFirst", "Escolha uma quest na lista primeiro (Ver quests)."));
        _tip.SetToolTip(_delete, _deleteQuest is null ? Loc.T("wq.soon", "Em construção: próximo passo.")
            : !pick ? Loc.T("wq.pickFirst", "Escolha uma quest na lista primeiro (Ver quests).")
            : SelectedMissing ? Loc.T("wq.removeMissingTip", "Esta quest não existe mais: tira o ID dela do serviço dos NPCs (Ctrl+Z desfaz).")
            : Loc.T("wq.deleteTip", "Exclui a quest escolhida e tira ela dos NPCs (Ctrl+Z desfaz)."));
        _delete.Text = pick && SelectedMissing ? Loc.T("wq.removeMissing", "Tirar do NPC") : Loc.T("wq.delete", "Excluir quest");
    }

    /// <param name="expanded">já abre com as listas (clique no NPC sem ferramenta, como o painel da criatura)</param>
    public void ShowNpc(NpcQuestInfo info, bool expanded = true)
    {
        _info = info;
        ApplyText();
        _title.Text = string.Format(Loc.T("wq.title2", "{0}   ·   NPC {1}"), info.NpcName, info.NpcId);
        _givesTitle.Image = UiKit.Icon("\uE7C1", Color.FromArgb(120, 190, 255), 16); _takesTitle.Image = UiKit.Icon("\uE73E", Color.FromArgb(140, 220, 140), 16);
        _givesTitle.Text = "      " + string.Format(Loc.T("wq.gives", "Dá a quest ({0})"), info.Gives.Count);
        _takesTitle.Text = "      " + string.Format(Loc.T("wq.takes", "Recebe a quest ({0})"), info.Takes.Count);
        Fill(_gives, info.Gives, gives: true);
        Fill(_takes, info.Takes, gives: false);
        _tip.SetToolTip(_title, Loc.T("wq.hint", "Clique duas vezes numa quest para abrir. Clique duas vezes aqui para minimizar."));
        Visible = true; BringToFront();
        SetMinimized(!expanded);   // senão primeiro só a barra; a lista abre em "Ver quests"
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
        ? string.Format(Loc.T("wq.showList2", "Ver quests ({0} dá · {1} recebe)"), _info!.Gives.Count, _info.Takes.Count)
        : Loc.T("wq.hideList2", "Esconder quests");

    void SetMinimized(bool min)
    {
        _minimized = min;
        if (_info is not null) _min.Text = ListText();
        Animate(min ? Header : Full);
    }

    public void HidePanel() { _info = null; Animate(0); }

    void Animate(int target) { _target = target; _anim.Start(); }
}
