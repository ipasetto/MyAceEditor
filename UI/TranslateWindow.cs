using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela do "Traduzir com outro elements.data": escolhe os blocos, filtra (só nomes; só o que ainda não foi
/// traduzido, ou seja, texto em chinês/russo) e mostra antes → depois antes de aplicar.
/// </summary>
internal sealed class TranslateWindow : Form
{
    static readonly Color Bg = Color.FromArgb(242, 245, 249), Fg = Color.FromArgb(30, 39, 51), Accent = Color.FromArgb(37, 99, 235);

    readonly List<ElementsTranslator.Change> _all;
    readonly Func<string, string> _blockName;
    readonly CheckedListBox _blocks = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
    readonly CheckBox _onlyNames = new() { AutoSize = true }, _onlyUntranslated = new() { AutoSize = true };   // desmarcado: o cliente pode estar em inglês (só pegaria chinês/russo)
    readonly ListView _preview = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None };
    readonly Button _ok = new() { Height = 34, Dock = DockStyle.Bottom, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White, DialogResult = DialogResult.OK };
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8), ForeColor = Color.FromArgb(100, 112, 128) };
    List<string> _order = new();

    public List<ElementsTranslator.Change> Selected { get; private set; } = new();

    public TranslateWindow(List<ElementsTranslator.Change> plan, int otherVersion, Func<string, string> blockName)
    {
        _all = plan; _blockName = blockName;
        Text = Loc.T("tr.title", "Traduzir com outro elements.data");
        Width = 1100; Height = 680; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);
        _info.Text = string.Format(Loc.T("tr.winInfo", "{0:N0} textos diferentes do outro arquivo (v{1}). Marque os blocos; a prévia mostra o que vai mudar. Dá para desfazer com Ctrl+Z."), plan.Count, otherVersion);
        _onlyNames.Text = Loc.T("tr.onlyNames", "Só nomes (campo Name)");
        _onlyUntranslated.Text = Loc.T("tr.onlyUntranslated", "Só textos ainda não traduzidos (chinês, russo…)");
        var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 6, 0, 0) };
        opts.Controls.Add(_onlyUntranslated); opts.Controls.Add(_onlyNames);
        var all = new Button { Text = Loc.T("tr.all", "Todos"), AutoSize = true, FlatStyle = FlatStyle.Flat };
        var none = new Button { Text = Loc.T("tr.noneBtn", "Nenhum"), AutoSize = true, FlatStyle = FlatStyle.Flat };
        all.Click += (_, _) => SetAll(true); none.Click += (_, _) => SetAll(false);
        opts.Controls.Add(all); opts.Controls.Add(none);
        _preview.Columns.Add(Loc.T("tr.colBlock", "Bloco"), 170); _preview.Columns.Add("ID", 70); _preview.Columns.Add(Loc.T("list.field", "Campo"), 120);
        _preview.Columns.Add(Loc.T("tr.colBefore", "Antes"), 320); _preview.Columns.Add(Loc.T("tr.colAfter", "Depois"), 320);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        Load += (_, _) => split.SplitterDistance = 300;   // só depois que a janela tem tamanho
        split.Panel1.Controls.Add(_blocks); split.Panel2.Controls.Add(_preview);
        Controls.Add(split); Controls.Add(opts); Controls.Add(_info); Controls.Add(_ok);
        _onlyNames.CheckedChanged += (_, _) => Refill(); _onlyUntranslated.CheckedChanged += (_, _) => Refill();
        _blocks.ItemCheck += (_, _) => { if (IsHandleCreated) BeginInvoke(Update); };   // marcação muda depois do evento
        Refill();
    }

    IEnumerable<ElementsTranslator.Change> Filtered() => _all.Where(c =>
        (!_onlyNames.Checked || c.Field.Equals("Name", StringComparison.OrdinalIgnoreCase))
        && (!_onlyUntranslated.Checked || ElementsTranslator.LooksUntranslated(c.Item.Fields[c.Field].Value)));

    void Refill()
    {
        var keep = Enumerable.Range(0, _blocks.Items.Count).Where(_blocks.GetItemChecked).Select(i => _order[i]).ToHashSet();
        bool first = _order.Count == 0;
        var groups = Filtered().GroupBy(c => c.Block.Name).OrderByDescending(g => g.Count()).ToList();
        _order = groups.Select(g => g.Key).ToList();
        _blocks.BeginUpdate(); _blocks.Items.Clear();
        foreach (var g in groups) _blocks.Items.Add($"{_blockName(g.Key)}  ({g.Count():N0})", first || keep.Contains(g.Key));
        _blocks.EndUpdate();
        Update();
    }

    void SetAll(bool on) { for (int i = 0; i < _blocks.Items.Count; i++) _blocks.SetItemChecked(i, on); Update(); }

    new void Update()
    {
        var on = Enumerable.Range(0, _blocks.Items.Count).Where(_blocks.GetItemChecked).Select(i => _order[i]).ToHashSet();
        Selected = Filtered().Where(c => on.Contains(c.Block.Name)).ToList();
        _preview.BeginUpdate(); _preview.Items.Clear();
        foreach (var c in Selected.Take(2000))
            _preview.Items.Add(new ListViewItem(new[] { _blockName(c.Block.Name), c.Item.Id, FieldLabels.Label(c.Field), c.Item.Fields[c.Field].Value, c.Value }));
        _preview.EndUpdate();
        _ok.Text = string.Format(Loc.T("tr.apply", "Aplicar {0:N0} traduções"), Selected.Count);
        _ok.Enabled = Selected.Count > 0;
    }
}
