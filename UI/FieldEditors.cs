using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Diálogos de edição de campos (duplo clique na grade), no estilo do Rody.</summary>
internal static class FieldEditors
{
    static readonly Color Bg = Color.FromArgb(242, 245, 249), Fg = Color.FromArgb(30, 39, 51), Accent = Color.FromArgb(37, 99, 235);

    static Form NewDialog(string title, int w, int h) => new()
    {
        Text = title, Width = w, Height = h, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
        MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Bg, ForeColor = Fg, Font = new Font("Segoe UI", 9.5f),
    };

    static FlowLayoutPanel Buttons(Form f, out Button ok)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        ok = new Button { Text = "OK", Width = 100, Height = 28, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
        var cancel = new Button { Text = "Cancelar", Width = 100, Height = 28, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
        p.Controls.Add(ok); p.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return p;
    }

    /// <summary>Máscara de classes: uma caixinha por classe (PW-KB §S24). Retorna null se cancelar.</summary>
    public static long? ClassMask(IWin32Window owner, long mask, int classCount = 10)
    {
        using var f = NewDialog("Classes que podem usar", 360, 420);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f) };
        for (int i = 0; i < classCount; i++) list.Items.Add(FieldKinds.ClassName(i), (mask & (1L << i)) != 0);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 0) };
        Button B(string t, bool v) { var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, v); }; return b; }
        top.Controls.Add(B("Todas", true)); top.Controls.Add(B("Nenhuma", false));
        var bottom = Buttons(f, out _);
        var maskLbl = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Padding = new Padding(8, 0, 0, 0) };
        void Upd() { long m = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) m |= 1L << i; maskLbl.Text = $"máscara = {m}"; }
        list.ItemCheck += (_, _) => f.BeginInvoke(Upd);
        Upd();
        f.Controls.Add(list); f.Controls.Add(top); f.Controls.Add(maskLbl); f.Controls.Add(bottom);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        long r = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) r |= 1L << i;
        return r;
    }

    /// <summary>
    /// Seletor de itens com busca, ícone e descrição (addons, itens, monstros, receitas).
    /// group != null mostra um filtro por grupo (ex.: tipo de addon). multi permite escolher vários.
    /// </summary>
    public static List<Item>? Pick(IWin32Window owner, string title, IReadOnlyList<Item> source, string? currentId,
        Func<Item, int> icon, ImageList images, Func<Item, string> describe, Func<Item, string?>? group, bool multi)
    {
        using var f = NewDialog(title, 860, 620);
        var search = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Pesquisar por nome, ID ou descrição…", BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f) };
        var groupBox = new ComboBox { Dock = DockStyle.Right, Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, Visible = group is not null };
        var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 2) };
        top.Controls.Add(search); top.Controls.Add(groupBox);
        if (group is not null)
        {
            groupBox.Items.Add("(todos os tipos)");
            foreach (var g in source.Select(group).Where(g => g is not null).Distinct().OrderBy(g => g)) groupBox.Items.Add(g!);
            groupBox.SelectedIndex = 0;
        }
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, MultiSelect = multi, HideSelection = false, SmallImageList = images, BorderStyle = BorderStyle.None };
        lv.Columns.Add("ID", 80, HorizontalAlignment.Right); lv.Columns.Add("Nome", 300); lv.Columns.Add("Descrição", 420);
        List<Item> view = new();
        void Refresh()
        {
            var q = search.Text.Trim();
            var g = group is not null && groupBox.SelectedIndex > 0 ? groupBox.SelectedItem as string : null;
            view = source.Where(it => (g is null || group!(it) == g) &&
                                      (q.Length == 0 || it.Id.Contains(q) || it.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || describe(it).Contains(q, StringComparison.OrdinalIgnoreCase)))
                         .Take(20000).ToList();
            lv.VirtualListSize = view.Count;
            lv.Invalidate();
            f.Text = $"{title} — {view.Count:N0}";
        }
        lv.RetrieveVirtualItem += (_, e) =>
        {
            var it = view[e.ItemIndex];
            e.Item = new ListViewItem(it.Id, icon(it));
            e.Item.SubItems.Add(it.Name);
            e.Item.SubItems.Add(describe(it));
        };
        search.TextChanged += (_, _) => Refresh();
        groupBox.SelectedIndexChanged += (_, _) => Refresh();
        var bottom = Buttons(f, out var ok);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Padding = new Padding(8, 0, 0, 0),
            Text = multi ? "Ctrl/Shift para escolher vários: preenchem este campo e os seguintes do mesmo tipo." : "Duplo clique ou OK para escolher. Escolha \"0\" no campo para limpar." };
        lv.DoubleClick += (_, _) => { if (lv.SelectedIndices.Count > 0) { f.DialogResult = DialogResult.OK; f.Close(); } };
        f.Controls.Add(lv); f.Controls.Add(top); f.Controls.Add(hint); f.Controls.Add(bottom);
        f.Shown += (_, _) =>
        {
            Refresh();
            int i = currentId is null ? -1 : view.FindIndex(x => x.Id == currentId);
            if (i >= 0) { lv.SelectedIndices.Add(i); lv.EnsureVisible(i); }
            search.Focus();
        };
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return lv.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => view[i]).ToList();
    }

    /// <summary>Alterar o mesmo campo em vários itens. Retorna (campo, valor) ou null.</summary>
    public static (string Field, string Value)? BulkValue(IWin32Window owner, IReadOnlyList<string> fields, int count, Func<string, string> label)
    {
        using var f = NewDialog($"Alterar campo em {count} itens", 520, 200);
        var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var k in fields) cb.Items.Add(new KeyValuePair<string, string>(k, $"{label(k)}  ({k})"));
        cb.DisplayMember = "Value"; if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        var tb = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        tl.Controls.Add(new Label { Text = "Campo", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); tl.Controls.Add(cb, 1, 0);
        tl.Controls.Add(new Label { Text = "Valor", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); tl.Controls.Add(tb, 1, 1);
        var bottom = Buttons(f, out _);
        f.Controls.Add(tl); f.Controls.Add(bottom);
        if (f.ShowDialog(owner) != DialogResult.OK || cb.SelectedItem is not KeyValuePair<string, string> kv) return null;
        return (kv.Key, tb.Text);
    }
}
