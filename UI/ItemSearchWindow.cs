using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Janela de busca de item por nome ou ID, com todos os itens do elements.data (teste 21).</summary>
internal sealed class ItemSearchWindow : Form
{
    readonly IReadOnlyList<(int Id, string Name, string Block)> _all;
    readonly TextBox _search = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f) };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, BorderStyle = BorderStyle.None, HideSelection = false };
    readonly Label _count = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 5, 0, 0), ForeColor = Color.Gray };
    List<(int Id, string Name, string Block)> _rows = [];

    public int Picked { get; private set; }

    public ItemSearchWindow(IReadOnlyList<(int Id, string Name, string Block)> items)
    {
        _all = items;
        Text = Loc.T("is.title", "Procurar item");
        Width = 720; Height = 560; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f); BackColor = Color.White; KeyPreview = true;
        _search.PlaceholderText = Loc.T("is.hint", "Nome ou ID do item");
        _list.Columns.Add("ID", 90); _list.Columns.Add(Loc.T("is.name", "Nome"), 340); _list.Columns.Add(Loc.T("is.block", "Tipo"), 240);
        _list.RetrieveVirtualItem += (_, e) =>
        {
            var r = _rows[e.ItemIndex];
            e.Item = new ListViewItem([r.Id.ToString(), r.Name, r.Block]);
        };
        _list.DoubleClick += (_, _) => Accept();
        _search.TextChanged += (_, _) => Fill();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down && _search.Focused && _rows.Count > 0) { _list.Focus(); _list.SelectedIndices.Clear(); _list.SelectedIndices.Add(0); e.Handled = true; }
        };
        Controls.Add(_list); Controls.Add(_search); Controls.Add(_count);
        Fill();
    }

    void Fill()
    {
        var q = _search.Text.Trim();
        bool number = int.TryParse(q, out var id);
        _rows = _all.Where(r => q.Length == 0 || (number && r.Id == id) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (number && r.Id.ToString().StartsWith(q))).Take(20000).ToList();
        _list.SelectedIndices.Clear();
        _list.VirtualListSize = _rows.Count;
        _list.Invalidate();
        _count.Text = string.Format(Loc.T("is.count", "{0:N0} itens. Duplo clique ou Enter escolhe."), _rows.Count);
    }

    void Accept()
    {
        int i = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : _rows.Count == 1 ? 0 : -1;
        if (i < 0) return;
        Picked = _rows[i].Id;
        DialogResult = DialogResult.OK;
    }
}
