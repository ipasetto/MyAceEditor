using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Revisão antes de gravar arquivos de interface nos pacotes (importar pasta / trocar com outro cliente): cada arquivo
/// com a situação (ok, aviso, erro) e o motivo. Erro não pode ser marcado. Grava só os marcados (UiImport.Apply).
/// </summary>
internal sealed class UiImportDialog : Form
{
    static readonly Color Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128), Accent = Color.FromArgb(37, 99, 235);

    readonly List<UiImport.Item> _items;
    readonly ListView _lv = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, BorderStyle = BorderStyle.None };
    readonly TextBox _filter = new() { Width = 260 };
    readonly Label _sum = new() { Dock = DockStyle.Top, Height = 40, ForeColor = Muted, Padding = new Padding(4, 6, 4, 0) };
    readonly Button _ok = new() { Width = 200, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
    bool _filling;

    /// <summary>Linhas do que foi gravado (vazio se nada).</summary>
    public List<string> Result { get; } = new();

    public UiImportDialog(string title, string intro, List<UiImport.Item> items)
    {
        _items = items;
        Text = title; Width = 1100; Height = 680; StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = new Font("Segoe UI", 9);
        _sum.Text = intro;
        _lv.Columns.Add(Loc.T("uim.colState", "Situação"), 90);
        _lv.Columns.Add(Loc.T("uim.colPack", "Pacote"), 110);
        _lv.Columns.Add(Loc.T("uim.colFile", "Arquivo"), 380);
        _lv.Columns.Add(Loc.T("uim.colWhy", "Motivo"), 480);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(2, 4, 0, 0) };
        Button B(string key, string pt, Action a, int w = 130) { var b = new Button { Text = Loc.T(key, pt), Width = w, Height = 28, FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => a(); top.Controls.Add(b); return b; }
        _filter.PlaceholderText = Loc.T("uim.filter", "Filtrar (ex.: bag, .dcf, version01)");
        top.Controls.Add(_filter);
        B("uim.all", "Marcar todos", () => Mark(_ => true));
        B("uim.none", "Desmarcar todos", () => Mark(_ => false));
        B("uim.onlyXml", "Só janelas (.xml)", () => Mark(i => i.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)), 140);
        B("uim.onlyVisible", "Só os filtrados", () => { var vis = Visible().ToHashSet(); Mark(vis.Contains); }, 140);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 8, 0) };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), Width = 110, Height = 30, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(cancel); bottom.Controls.Add(_ok);
        _ok.Click += (_, _) => Save();
        CancelButton = cancel;

        Controls.Add(_lv); Controls.Add(top); Controls.Add(_sum); Controls.Add(bottom);
        _filter.TextChanged += (_, _) => Fill();
        _lv.ItemCheck += (_, e) =>
        {
            if (_filling || _lv.Items[e.Index].Tag is not UiImport.Item it) return;
            if (it.Level == UiImport.Level.Error) { e.NewValue = CheckState.Unchecked; return; }   // erro não grava
            it.Selected = e.NewValue == CheckState.Checked; UpdateOk();
        };
        Fill();
    }

    IEnumerable<UiImport.Item> Visible()
    {
        var q = _filter.Text.Trim();
        return _items.Where(i => q.Length == 0 || i.Path.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Why.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    void Mark(Func<UiImport.Item, bool> on)
    {
        foreach (var i in _items) i.Selected = i.Level != UiImport.Level.Error && on(i);
        Fill();
    }

    void Fill()
    {
        _filling = true;
        _lv.BeginUpdate(); _lv.Items.Clear();
        foreach (var i in Visible().OrderByDescending(i => i.Level).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).Take(5000))
        {
            var state = i.Level switch { UiImport.Level.Error => Loc.T("uim.error", "ERRO"), UiImport.Level.Warn => Loc.T("uim.warn", "aviso"), _ => "ok" };
            var row = new ListViewItem(new[] { state, Path.GetFileName(i.Package), i.Path, (i.IsNew ? Loc.T("uim.newTag", "[novo] ") : "") + i.Why })
            {
                Tag = i, Checked = i.Selected,
                ForeColor = i.Level == UiImport.Level.Error ? Color.Firebrick : i.Level == UiImport.Level.Warn ? Color.DarkGoldenrod : Fg,
            };
            _lv.Items.Add(row);
        }
        _lv.EndUpdate();
        _filling = false;
        UpdateOk();
    }

    void UpdateOk()
    {
        int n = _items.Count(i => i.Selected && i.Level != UiImport.Level.Error);
        int err = _items.Count(i => i.Level == UiImport.Level.Error), warn = _items.Count(i => i.Level == UiImport.Level.Warn);
        _ok.Text = string.Format(Loc.T("uim.write", "Gravar {0} arquivo(s)"), n);
        _ok.Enabled = n > 0;
        Text = Text.Split(" — ")[0] + " — " + string.Format(Loc.T("uim.counts", "{0} diferentes · {1} avisos · {2} erros"), _items.Count, warn, err);
    }

    void Save()
    {
        var n = _items.Count(i => i.Selected && i.Level != UiImport.Level.Error);
        if (MessageBox.Show(this, string.Format(Loc.T("uim.confirm", "Gravar {0} arquivo(s) nos pacotes?\n\nCada pacote alterado ganha um backup antes (pasta de backups do MyAceEditor). Feche o jogo antes."), n),
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            Result.AddRange(UiImport.Apply(_items));
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; }
    }
}
