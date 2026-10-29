namespace MyAceEditor.UI;

/// <summary>"Dar quest existente": procura uma quest (nome ou ID) para o NPC passar a dar.</summary>
internal sealed class QuestLinkDialog : Form
{
    readonly IQuestHost _host;
    readonly TextBox _search = new() { Dock = DockStyle.Top, BackColor = Color.Black, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f) };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = Color.FromArgb(22, 25, 30), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    readonly System.Windows.Forms.Timer _wait = new() { Interval = 250 };
    public int QuestId { get; private set; }

    public QuestLinkDialog(IQuestHost host, string npcName)
    {
        _host = host;
        Text = string.Format(Loc.T("ql.title", "Dar quest existente a {0}"), npcName);
        Size = new Size(640, 480); StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(28, 32, 38); ForeColor = Color.Gainsboro;
        MinimizeBox = false; ShowInTaskbar = false;
        _search.PlaceholderText = Loc.T("ql.search", "Procurar quest por nome ou ID");
        _list.Columns.Add(Loc.T("wq.colId", "ID"), 80); _list.Columns.Add(Loc.T("wq.colName", "Quest"), 330); _list.Columns.Add(Loc.T("ql.giver", "Dada hoje por"), 190);
        var hint = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4, 4, 4, 0), ForeColor = Color.Silver,
            Text = Loc.T("ql.hint", "A quest entra no serviço de dar quests deste NPC e o campo \"NPC que dá a quest\" passa a ser ele. Nada é gravado até o Salvar.") };
        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4, 6, 4, 4) };
        var ok = new Button { Text = Loc.T("ql.ok", "Dar ao NPC"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 110, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("qe.cancel", "Cancelar"), AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, DialogResult = DialogResult.Cancel };
        foot.Controls.Add(ok); foot.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(_list); Controls.Add(_search); Controls.Add(hint); Controls.Add(foot);
        ok.Click += (_, e) => { if (_list.SelectedItems.Count == 0) { DialogResult = DialogResult.None; return; } QuestId = (int)_list.SelectedItems[0].Tag!; };
        _list.ItemActivate += (_, _) => { if (_list.SelectedItems.Count > 0) { QuestId = (int)_list.SelectedItems[0].Tag!; DialogResult = DialogResult.OK; } };
        _wait.Tick += (_, _) => { _wait.Stop(); Fill(); };
        _search.TextChanged += (_, _) => { _wait.Stop(); _wait.Start(); };
        Shown += (_, _) => { _search.Focus(); Fill(); };
        Disposed += (_, _) => _wait.Dispose();
    }

    void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var (id, name, giver) in _host.SearchQuests(_search.Text, 500))
            _list.Items.Add(new ListViewItem(new[] { id.ToString(), name, giver <= 0 ? "—" : $"{_host.NameOf(giver) ?? "?"} ({giver})" }) { Tag = id });
        _list.EndUpdate();
    }
}
