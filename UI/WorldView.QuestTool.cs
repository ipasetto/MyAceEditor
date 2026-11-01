namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta "Quests" do Mundo 3D (no lugar dos botões que ficavam embaixo): com ela aberta, clicar num NPC mostra só as
/// quests dele (dá / recebe) e as ações: criar, dar quest existente, editar e excluir. Mesmo padrão de Caminhos e áreas.
/// </summary>
internal sealed partial class WorldView
{
    readonly FlowLayoutPanel _questsPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly Label _qtRed = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(255, 95, 85), Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(3, 6, 3, 4) };
    readonly Label _qtName = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(3, 2, 3, 4) };
    readonly Label _qtHint = Hint(""), _qtGivesTitle = SectionLabel(""), _qtTakesTitle = SectionLabel("");
    readonly ListView _qtGives = QtList(), _qtTakes = QtList();
    Button _qtCreate = null!, _qtLink = null!, _qtEdit = null!, _qtDelete = null!;
    NpcQuestInfo? _qtInfo;

    static ListView QtList()
    {
        var lv = new ListView { Width = 250, Height = 120, View = System.Windows.Forms.View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(22, 25, 30), ForeColor = Color.Gainsboro, HeaderStyle = ColumnHeaderStyle.None, ShowItemToolTips = true, Margin = new Padding(0, 0, 0, 6) };
        lv.Columns.Add("", 58); lv.Columns.Add("", 172);
        return lv;
    }

    NpcQuestRef? QtSelected => new[] { _qtGives, _qtTakes }.SelectMany(l => l.SelectedItems.Cast<ListViewItem>()).Select(i => i.Tag).OfType<NpcQuestRef>().FirstOrDefault();

    void InitQuestPage()
    {
        _qtCreate = SmallButton("", Loc.T("wq.create2", "Criar quest"), () => { if (_qtInfo is not null) OpenCreator(_qtInfo.NpcId); }, main: true);
        _qtLink = SmallButton("", Loc.T("wq.link", "Dar quest existente"), () => { if (_qtInfo is not null) LinkExisting(_qtInfo.NpcId); });
        _qtEdit = SmallButton("", Loc.T("wq.edit", "Editar quest"), () => { if (QtSelected is { } q) OpenQuestEditor(q.Id); });
        _qtDelete = SmallButton("", Loc.T("wq.delete", "Excluir quest"), () => { if (QtSelected is { } q) DeleteQuest(q.Id); });
        foreach (var lv in new[] { _qtGives, _qtTakes })
        {
            lv.HandleCreated += (_, _) => SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
            lv.ItemActivate += (_, _) => { if (QtSelected is { Name.Length: > 0 } q) OpenQuestEditor(q.Id); };
            // escolher numa lista tira a escolha da outra: editar/excluir valem para uma quest só
            lv.SelectedIndexChanged += (_, _) => { if (lv.SelectedItems.Count > 0) (lv == _qtGives ? _qtTakes : _qtGives).SelectedItems.Clear(); UpdateQuestTool(); };
        }
        foreach (var c in new Control[] { _qtRed, _qtName, _qtHint, _qtCreate, _qtLink, _qtGivesTitle, _qtGives, _qtTakesTitle, _qtTakes, _qtEdit, _qtDelete })
            _questsPage.Controls.Add(c);
        UpdateQuestTool();
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

    /// <summary>Mostra as quests do NPC na ferramenta (null = nenhum NPC escolhido).</summary>
    void ShowQuestTool(NpcQuestInfo? info)
    {
        _qtInfo = info;
        void Fill(ListView lv, IReadOnlyList<NpcQuestRef> list)
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var q in list.OrderBy(q => q.Id))
            {
                var it = new ListViewItem(new[] { q.Id.ToString(), q.Name.Length > 0 ? q.Name : Loc.T("wq.noQuest", "(quest não existe no tasks.data)") }) { Tag = q, ToolTipText = Loc.T("wq.tipOpen", "Clique duas vezes para abrir a quest.") };
                if (!q.InService || q.Name.Length == 0) it.ForeColor = Color.FromArgb(240, 190, 90);
                lv.Items.Add(it);
            }
            if (list.Count == 0) lv.Items.Add(new ListViewItem(new[] { "", Loc.T("wq.none", "(nenhuma)") }) { ForeColor = Color.Gray });
            lv.EndUpdate();
        }
        Fill(_qtGives, info?.Gives ?? Array.Empty<NpcQuestRef>());
        Fill(_qtTakes, info?.Takes ?? Array.Empty<NpcQuestRef>());
        UpdateQuestTool();
    }

    void UpdateQuestTool()
    {
        var info = _qtInfo;
        bool has = info is not null, host = _host is not null, pick = QtSelected is not null;
        _qtRed.Text = Loc.T("qt.pickRed", "Clique em um NPC");
        _qtRed.Visible = !has;
        _qtName.Visible = has;
        _qtName.Text = has ? string.Format(Loc.T("wq.title2", "{0}   ·   NPC {1}"), info!.NpcName, info.NpcId) : "";
        _qtHint.Text = !has ? Loc.T("qt.pickHint", "no mundo para ver as quests que ele dá e recebe. Monstros não dão quest.")
            : !host ? Loc.T("wq.soon", "Em construção: próximo passo.")
            : Loc.T("qt.hint", "Criar ou dar uma quest a este NPC; para editar ou excluir, escolha a quest numa das listas (duplo clique abre).");
        _qtCreate.Visible = _qtLink.Visible = has;
        _qtCreate.Enabled = _qtLink.Enabled = host;
        _qtGivesTitle.Visible = _qtGives.Visible = _qtTakesTitle.Visible = _qtTakes.Visible = has;
        _qtGivesTitle.Text = string.Format(Loc.T("wq.gives", "Dá a quest ({0})"), info?.Gives.Count ?? 0);
        _qtTakesTitle.Text = string.Format(Loc.T("wq.takes", "Recebe a quest ({0})"), info?.Takes.Count ?? 0);
        _qtEdit.Visible = _qtDelete.Visible = has;
        _qtEdit.Enabled = _qtDelete.Enabled = host && pick;
        if (_tool == Tool.Quests && IsHandleCreated) BeginInvoke(LayoutTools);
    }
}
