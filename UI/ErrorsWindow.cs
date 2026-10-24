using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Resultado do "Procurar erros": lista de problemas, duplo clique leva ao item e ao campo,
/// "Corrigir selecionados" ajusta o que dá para ajustar sozinho (probabilidades). Janela não modal.
/// </summary>
internal sealed class ErrorsWindow : Form
{
    static readonly Color Bg = Color.FromArgb(242, 245, 249), Fg = Color.FromArgb(30, 39, 51), Accent = Color.FromArgb(37, 99, 235);

    readonly Func<List<ErrorFinder.Problem>> _scan;
    readonly Action<ErrorFinder.Problem> _go;
    readonly Action<IReadOnlyList<ErrorFinder.Problem>> _fixed;
    readonly Func<ItemList, string> _blockName;
    readonly ListView _lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None };
    readonly Label _summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly Button _fix = new() { Width = 170, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
    List<ErrorFinder.Problem> _problems = new();

    public static string KindText(ErrorFinder.Kind k) => k switch
    {
        ErrorFinder.Kind.MissingRef => Loc.T("err.missingRef", "Aponta para ID que não existe"),
        ErrorFinder.Kind.ProbRange => Loc.T("err.probRange", "Probabilidade fora de 0% a 100%"),
        ErrorFinder.Kind.ProbSum => Loc.T("err.probSum", "Probabilidades não somam 100%"),
        ErrorFinder.Kind.DuplicateId => Loc.T("err.duplicateId", "ID repetido"),
        _ => k.ToString(),
    };

    string DetailText(ErrorFinder.Problem p) => p.Kind switch
    {
        ErrorFinder.Kind.MissingRef => string.Format(Loc.T("err.detailRef", "ID {0}"), p.Detail),
        ErrorFinder.Kind.ProbSum => string.Format(Loc.T("err.detailSum", "soma {0}"), p.Detail),
        ErrorFinder.Kind.DuplicateId => string.Format(Loc.T("err.detailDup", "também em {0}"), p.Detail),
        _ => p.Detail,
    };

    public ErrorsWindow(Func<List<ErrorFinder.Problem>> scan, Action<ErrorFinder.Problem> go,
        Action<IReadOnlyList<ErrorFinder.Problem>> onFixed, Func<ItemList, string> blockName)
    {
        _scan = scan; _go = go; _fixed = onFixed; _blockName = blockName;
        Text = Loc.T("err.title", "Procurar erros");
        Width = 980; Height = 520; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f); MinimizeBox = false;
        _lv.Columns.Add(Loc.T("err.colKind", "Problema"), 230);
        _lv.Columns.Add(Loc.T("err.colBlock", "Bloco"), 170);
        _lv.Columns.Add("ID", 70);
        _lv.Columns.Add(Loc.T("list.name", "Nome"), 200);
        _lv.Columns.Add(Loc.T("list.field", "Campo"), 160);
        _lv.Columns.Add(Loc.T("err.colDetail", "Detalhe"), 140);
        _lv.ItemActivate += (_, _) => { if (_lv.SelectedItems.Count > 0 && _lv.SelectedItems[0].Tag is ErrorFinder.Problem p) _go(p); };
        _lv.SelectedIndexChanged += (_, _) => UpdateFixButton();

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 3, Padding = new Padding(8, 6, 8, 6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var again = new Button { Text = Loc.T("err.again", "Procurar de novo"), Width = 140, Height = 28, FlatStyle = FlatStyle.Flat };
        again.Click += (_, _) => Scan();
        _fix.Click += (_, _) => FixSelected();
        top.Controls.Add(_summary, 0, 0); top.Controls.Add(again, 1, 0); top.Controls.Add(_fix, 2, 0);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.FromArgb(100, 112, 128), Padding = new Padding(8, 4, 0, 0),
            Text = Loc.T("err.hint", "Duplo clique abre o item no campo do problema. \"Corrigir\" só ajusta probabilidades; o resto precisa da sua decisão.") };
        Controls.Add(_lv); Controls.Add(top); Controls.Add(hint);
        Shown += (_, _) => Scan();
    }

    void Scan()
    {
        Cursor = Cursors.WaitCursor;
        try { _problems = _scan(); } finally { Cursor = Cursors.Default; }
        Fill();
    }

    void Fill()
    {
        _lv.BeginUpdate(); _lv.Items.Clear();
        foreach (var p in _problems)
            _lv.Items.Add(new ListViewItem(new[]
            {
                KindText(p.Kind), _blockName(p.Block), p.Item.Id, TasksFile.SplitColor(p.Item.Name).Text,
                p.Field == "ID" ? "ID" : FieldLabels.Label(p.Field), DetailText(p),
            }) { Tag = p, ForeColor = ErrorFinder.CanFix(p) ? Color.FromArgb(180, 83, 9) : Color.FromArgb(185, 28, 28) });
        _lv.EndUpdate();
        _summary.Text = _problems.Count == 0
            ? Loc.T("err.none", "Nenhum problema encontrado.")
            : string.Format(Loc.T("err.count", "{0} problema(s): "), _problems.Count)
              + string.Join(" · ", _problems.GroupBy(p => p.Kind).Select(g => $"{KindText(g.Key)} {g.Count()}"));
        UpdateFixButton();
    }

    IEnumerable<ErrorFinder.Problem> FixTargets() =>
        (_lv.SelectedItems.Count > 0 ? _lv.SelectedItems.Cast<ListViewItem>().Select(i => (ErrorFinder.Problem)i.Tag!) : _problems).Where(ErrorFinder.CanFix);

    void UpdateFixButton()
    {
        int n = FixTargets().Count();
        _fix.Enabled = n > 0;
        _fix.BackColor = n > 0 ? Accent : Color.FromArgb(203, 213, 225);
        _fix.Text = string.Format(_lv.SelectedItems.Count > 0 ? Loc.T("err.fixSel", "Corrigir selecionados ({0})") : Loc.T("err.fixAll", "Corrigir todos ({0})"), n);
    }

    void FixSelected()
    {
        var done = FixTargets().Where(ErrorFinder.Fix).ToList();
        _fixed(done);
        _problems.RemoveAll(done.Contains);
        Fill();
    }
}
