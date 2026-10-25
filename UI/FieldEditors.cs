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
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), Width = 100, Height = 28, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
        p.Controls.Add(ok); p.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return p;
    }

    /// <summary>Máscara de classes: uma caixinha por classe (PW-KB §S24). Retorna null se cancelar.</summary>
    public static long? ClassMask(IWin32Window owner, long mask, int classCount = 10)
    {
        using var f = NewDialog(Loc.T("dlg.classesTitle", "Classes que podem usar"), 360, 420);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f) };
        for (int i = 0; i < classCount; i++) list.Items.Add(FieldKinds.ClassName(i), (mask & (1L << i)) != 0);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 0) };
        Button B(string t, bool v) { var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, v); }; return b; }
        top.Controls.Add(B(Loc.T("dlg.all", "Todas"), true)); top.Controls.Add(B(Loc.T("dlg.none", "Nenhuma"), false));
        var bottom = Buttons(f, out _);
        var maskLbl = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Padding = new Padding(8, 0, 0, 0) };
        void Upd() { long m = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) m |= 1L << i; maskLbl.Text = $"{Loc.T("dlg.mask", "máscara")} = {m}"; }
        list.ItemCheck += (_, _) => f.BeginInvoke(Upd);
        Upd();
        f.Controls.Add(list); f.Controls.Add(top); f.Controls.Add(maskLbl); f.Controls.Add(bottom);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        long r = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) r |= 1L << i;
        return r;
    }

    /// <summary>Máscara de bits com nomes (regras do item, imunidades): uma caixinha por bit. Null se cancelar.</summary>
    public static long? BitMask(IWin32Window owner, string title, long mask, (int Bit, string Name)[] table, string unknownPrefix = "Bit")
    {
        using var f = NewDialog(title, 420, 480);
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
        // opções: as conhecidas + qualquer bit ligado no valor que não tem nome (aparece como "Bit N")
        var bits = table.Select(t => t.Bit).ToList();
        for (int b = 0; b < 32; b++) if ((mask & (1L << b)) != 0 && !bits.Contains(b)) bits.Add(b);
        bits.Sort();
        foreach (var b in bits)
        {
            var name = table.FirstOrDefault(t => t.Bit == b).Name ?? $"{unknownPrefix} {b}";
            list.Items.Add(new BitItem(b, name), (mask & (1L << b)) != 0);
        }
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 0) };
        Button B(string t, bool v) { var bt = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat }; bt.Click += (_, _) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, v); }; return bt; }
        top.Controls.Add(B(Loc.T("dlg.all", "Todas"), true)); top.Controls.Add(B(Loc.T("dlg.none", "Nenhuma"), false));
        var bottom = Buttons(f, out _);
        var maskLbl = new Label { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, Padding = new Padding(8, 0, 0, 0) };
        void Upd() { long m = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) m |= 1L << ((BitItem)list.Items[i]).Bit; maskLbl.Text = $"{Loc.T("dlg.value", "valor")} = {m}"; }
        list.ItemCheck += (_, _) => f.BeginInvoke(Upd);
        Upd();
        f.Controls.Add(list); f.Controls.Add(top); f.Controls.Add(maskLbl); f.Controls.Add(bottom);
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        long r = 0; for (int i = 0; i < list.Items.Count; i++) if (list.GetItemChecked(i)) r |= 1L << ((BitItem)list.Items[i]).Bit;
        return r;
    }

    private sealed record BitItem(int Bit, string Name)
    {
        public override string ToString() => $"{Name}  (bit {Bit})";
    }

    /// <summary>
    /// Seletor de itens com busca, ícone e descrição (addons, itens, monstros, receitas).
    /// group != null mostra um filtro por grupo (ex.: tipo de addon). multi permite escolher vários.
    /// </summary>
    public static List<Item>? Pick(IWin32Window owner, string title, IReadOnlyList<Item> source, string? currentId,
        Func<Item, int> icon, ImageList images, Func<Item, string> describe, Func<Item, string?>? group, bool multi, string? initialGroup = null)
    {
        using var f = NewDialog(title, 860, 620);
        var search = new TextBox { Dock = DockStyle.Fill, PlaceholderText = Loc.T("dlg.pickSearch", "Pesquisar por nome, ID ou descrição…"), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f) };
        var groupBox = new ComboBox { Dock = DockStyle.Right, Width = 280, DropDownStyle = ComboBoxStyle.DropDownList, Visible = group is not null };
        var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 2) };
        top.Controls.Add(search); top.Controls.Add(groupBox);
        if (group is not null)
        {
            groupBox.Items.Add(Loc.T("dlg.allTypes", "(todos os tipos)"));
            foreach (var g in source.Select(group).Where(g => g is not null).Distinct().OrderBy(g => g)) groupBox.Items.Add(g!);
            groupBox.SelectedIndex = initialGroup is not null && groupBox.Items.IndexOf(initialGroup) is >= 0 and var gi ? gi : 0;
        }
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, MultiSelect = multi, HideSelection = false, SmallImageList = images, BorderStyle = BorderStyle.None };
        lv.Columns.Add(Loc.T("list.id", "ID"), 80, HorizontalAlignment.Right); lv.Columns.Add(Loc.T("list.name", "Nome"), 300); lv.Columns.Add(Loc.T("list.description", "Descrição"), 420);
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
            Text = multi ? Loc.T("dlg.hintMulti", "Ctrl/Shift para escolher vários: preenchem este campo e os seguintes do mesmo tipo.")
                         : Loc.T("dlg.hintSingle", "Duplo clique ou OK para escolher. Escolha \"0\" no campo para limpar.") };
        lv.DoubleClick += (_, _) => { if (lv.SelectedIndices.Count > 0) { f.DialogResult = DialogResult.OK; f.Close(); } };
        f.Controls.Add(lv); f.Controls.Add(top); f.Controls.Add(hint); f.Controls.Add(bottom);
        f.Shown += (_, _) =>
        {
            Refresh();
            int i = currentId is null ? -1 : view.FindIndex(x => x.Id == currentId);
            if (i >= 0) { lv.SelectedIndices.Add(i); lv.EnsureVisible(i); }
            search.Focus();
        };
        // a seleção da lista virtual é lida antes de fechar: depois de fechada (seletor aberto por outra janela modal)
        // SelectedIndices volta vazio e o "OK" não escolhia nada
        List<Item> chosen = new();
        f.FormClosing += (_, _) => chosen = lv.SelectedIndices.Cast<int>().OrderBy(i => i).Where(i => i < view.Count).Select(i => view[i]).ToList();
        if (f.ShowDialog(owner) != DialogResult.OK) return null;
        return chosen;
    }

    /// <summary>Operações de "Alterar campo nos selecionados" (como o "modificar valores" do RAE, código nosso).</summary>
    public enum BulkMode { Set, Add, Multiply, Sequence, Random, Replace }
    public sealed record BulkOp(string Field, BulkMode Mode, string A, string B, int Seed);

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    static bool Num(string s, out double v) => double.TryParse(s.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, Inv, out v);

    /// <summary>Novo valor do campo para o item de posição <paramref name="index"/> na seleção; null = parâmetro inválido.</summary>
    public static string? BulkApply(BulkOp op, Field old, int index, Random rng)
    {
        bool isInt = old.Type is FieldType.Int or FieldType.Bool or FieldType.Reference;
        bool isNum = isInt || old.Type == FieldType.Float;
        string Out(double v) => isInt ? ((long)Math.Clamp(Math.Round(v), int.MinValue, uint.MaxValue)).ToString(Inv) : ((float)v).ToString("0.######", Inv);
        Num(old.Value, out var cur);
        switch (op.Mode)
        {
            case BulkMode.Set: return isNum ? (Num(op.A, out var s) ? Out(s) : null) : op.A;
            case BulkMode.Replace: return op.A.Length == 0 ? null : old.Value.Replace(op.A, op.B);
        }
        if (!isNum || !Num(op.A, out var a)) return null;
        double b = 0;
        if (op.Mode is BulkMode.Sequence or BulkMode.Random && !Num(op.B, out b)) return null;
        return op.Mode switch
        {
            BulkMode.Add => Out(cur + a),
            BulkMode.Multiply => Out(cur * a),
            BulkMode.Sequence => Out(a + b * index),
            BulkMode.Random => isInt ? Out(rng.NextInt64((long)Math.Min(a, b), (long)Math.Max(a, b) + 1)) : Out(Math.Min(a, b) + rng.NextDouble() * Math.Abs(b - a)),
            _ => null,
        };
    }

    /// <summary>Alterar um campo em vários itens: definir, somar, multiplicar, sequência, aleatório ou substituir texto, com prévia.</summary>
    public static BulkOp? BulkValue(IWin32Window owner, IReadOnlyList<Item> items, IReadOnlyList<string> fields, Func<string, string> label, string? initialField = null)
    {
        using var f = NewDialog(string.Format(Loc.T("dlg.bulkTitle", "Alterar campo em {0} itens"), items.Count), 620, 420);
        var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var k in fields) cb.Items.Add(new KeyValuePair<string, string>(k, $"{label(k)}  ({k})"));
        cb.DisplayMember = "Value";
        cb.SelectedIndex = Math.Max(0, initialField is null ? 0 : fields.ToList().IndexOf(initialField));
        int seed = Environment.TickCount;   // a prévia do "aleatório" é o que será gravado
        var modes = new (BulkMode M, string Key, string Pt)[]
        {
            (BulkMode.Set, "dlg.bulkSet", "Definir valor"),
            (BulkMode.Add, "dlg.bulkAdd", "Somar (use negativo para subtrair)"),
            (BulkMode.Multiply, "dlg.bulkMul", "Multiplicar por"),
            (BulkMode.Sequence, "dlg.bulkSeq", "Sequência (início + passo)"),
            (BulkMode.Random, "dlg.bulkRand", "Aleatório entre (mín e máx)"),
            (BulkMode.Replace, "dlg.bulkRepl", "Substituir texto"),
        };
        var mode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var m in modes) mode.Items.Add(Loc.T(m.Key, m.Pt));
        mode.SelectedIndex = 0;
        var ta = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        var tbx = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        var la = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var lb = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var preview = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
        preview.Columns.Add(Loc.T("dlg.bulkItem", "Item"), 260); preview.Columns.Add(Loc.T("dlg.bulkBefore", "Antes"), 120); preview.Columns.Add(Loc.T("dlg.bulkAfter", "Depois"), 120);
        void Row(Control l, Control c, int r) { tl.Controls.Add(l, 0, r); tl.Controls.Add(c, 1, r); }
        Row(new Label { Text = Loc.T("list.field", "Campo"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, cb, 0);
        Row(new Label { Text = Loc.T("dlg.bulkOp", "Operação"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, mode, 1);
        Row(la, ta, 2); Row(lb, tbx, 3);
        tl.Controls.Add(preview, 0, 4); tl.SetColumnSpan(preview, 2);
        for (int i = 0; i < 4; i++) tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bottom = Buttons(f, out var ok);
        f.Controls.Add(tl); f.Controls.Add(bottom);

        BulkOp? Current() => cb.SelectedItem is KeyValuePair<string, string> kv ? new BulkOp(kv.Key, modes[mode.SelectedIndex].M, ta.Text, tbx.Text, seed) : null;
        void Refresh()
        {
            var m = modes[mode.SelectedIndex].M;
            (la.Text, lb.Text) = m switch
            {
                BulkMode.Sequence => (Loc.T("dlg.bulkStart", "Início"), Loc.T("dlg.bulkStep", "Passo")),
                BulkMode.Random => (Loc.T("dlg.bulkMin", "Mínimo"), Loc.T("dlg.bulkMax", "Máximo")),
                BulkMode.Replace => (Loc.T("dlg.bulkFind", "Procurar"), Loc.T("dlg.bulkWith", "Trocar por")),
                BulkMode.Multiply => (Loc.T("dlg.bulkFactor", "Fator"), ""),
                _ => (Loc.T("list.value", "Valor"), ""),
            };
            lb.Visible = tbx.Visible = m is BulkMode.Sequence or BulkMode.Random or BulkMode.Replace;
            preview.BeginUpdate(); preview.Items.Clear();
            var op = Current(); var rng = new Random(seed); bool valid = op is not null;
            int idx = 0;
            foreach (var it in items)
            {
                if (op is null || !it.Fields.TryGetValue(op.Field, out var fld)) continue;
                var nv = BulkApply(op, fld, idx++, rng);
                if (nv is null) valid = false;
                if (preview.Items.Count < 200)
                    preview.Items.Add(new ListViewItem(new[] { $"{it.Id}  {PerfectWorld.TasksFile.SplitColor(it.Name).Text}", fld.Value, nv ?? "?" }));
            }
            preview.EndUpdate();
            ok.Enabled = valid && idx > 0;
        }
        cb.SelectedIndexChanged += (_, _) => Refresh(); mode.SelectedIndexChanged += (_, _) => Refresh();
        ta.TextChanged += (_, _) => Refresh(); tbx.TextChanged += (_, _) => Refresh();
        Refresh();
        f.Shown += (_, _) => ta.Focus();
        return f.ShowDialog(owner) == DialogResult.OK ? Current() : null;
    }

    /// <summary>
    /// Grade com todos os ícones do jogo (atlas do surfaces.pck), como no "Icon selection" dos editores da comunidade.
    /// Clique escolhe, duplo clique confirma. Retorna o nome do arquivo do ícone (ex.: "x.dds") ou null.
    /// </summary>
    public static string? IconPicker(IWin32Window owner, PerfectWorld.IconAtlas atlas, string current)
    {
        using var f = NewDialog(Loc.T("dlg.iconTitle", "Escolher ícone"), 780, 620);
        var all = atlas.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var curName = Path.GetFileName(current.Replace('/', '\\'));
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = Loc.T("dlg.iconSearch", "Filtrar pelo nome do arquivo…"), BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f) };
        var info = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Gray, Padding = new Padding(8, 4, 0, 0) };
        var grid = new IconGrid(atlas) { Dock = DockStyle.Fill };
        void Refresh()
        {
            var q = search.Text.Trim();
            grid.Items = q.Length == 0 ? all : all.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            grid.Selected = grid.Items.FindIndex(n => n.Equals(curName, StringComparison.OrdinalIgnoreCase));
            grid.Reset();
            info.Text = string.Format(Loc.T("dlg.iconCount", "{0:N0} ícones"), grid.Items.Count);
        }
        grid.HoverChanged += n => info.Text = n ?? string.Format(Loc.T("dlg.iconCount", "{0:N0} ícones"), grid.Items.Count);
        grid.Accepted += () => { f.DialogResult = DialogResult.OK; f.Close(); };
        search.TextChanged += (_, _) => Refresh();
        var bottom = Buttons(f, out _);
        f.Controls.Add(grid); f.Controls.Add(search); f.Controls.Add(info); f.Controls.Add(bottom);
        f.Shown += (_, _) => { Refresh(); grid.EnsureVisible(grid.Selected); grid.Focus(); };
        if (f.ShowDialog(owner) != DialogResult.OK || grid.Selected < 0) return null;
        return grid.Items[grid.Selected];
    }

    /// <summary>Grade desenhada sob demanda: só recorta do atlas os ícones visíveis (abre rápido com milhares).</summary>
    private sealed class IconGrid : Panel
    {
        const int Cell = 40;
        readonly PerfectWorld.IconAtlas _atlas;
        readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
        int _hover = -1;
        public List<string> Items = new();
        public int Selected = -1;
        public event Action<string?>? HoverChanged;
        public event Action? Accepted;

        public IconGrid(PerfectWorld.IconAtlas atlas) { _atlas = atlas; DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(24, 28, 34); }
        int Cols => Math.Max(1, ClientSize.Width / Cell);
        public void Reset() { AutoScrollMinSize = new Size(0, (Items.Count + Cols - 1) / Cols * Cell); Invalidate(); }
        public void EnsureVisible(int i) { if (i >= 0) AutoScrollPosition = new Point(0, Math.Max(0, i / Cols * Cell - ClientSize.Height / 2)); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Reset(); }

        int HitTest(Point p)
        {
            int x = p.X / Cell, y = (p.Y - AutoScrollPosition.Y) / Cell, i = y * Cols + x;
            return x < Cols && i >= 0 && i < Items.Count ? i : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int cols = Cols, oy = AutoScrollPosition.Y;
            int first = Math.Max(0, -oy / Cell) * cols, last = Math.Min(Items.Count, ((-oy + ClientSize.Height) / Cell + 1) * cols);
            for (int i = first; i < last; i++)
            {
                var r = new Rectangle(i % cols * Cell + 4, i / cols * Cell + oy + 4, 32, 32);
                if (!_cache.TryGetValue(Items[i], out var bmp)) _cache[Items[i]] = bmp = _atlas.Get(Items[i]);
                if (bmp is not null) e.Graphics.DrawImage(bmp, r);
                if (i == Selected) { using var p = new Pen(Accent, 3); e.Graphics.DrawRectangle(p, Rectangle.Inflate(r, 2, 2)); }
                else if (i == _hover) e.Graphics.DrawRectangle(Pens.Gold, Rectangle.Inflate(r, 1, 1));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h == _hover) return;
            _hover = h; Invalidate();
            HoverChanged?.Invoke(h >= 0 ? Items[h] : null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (HitTest(e.Location) is var i && i >= 0) { Selected = i; Invalidate(); }
            Focus();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (HitTest(e.Location) is var i && i >= 0) { Selected = i; Accepted?.Invoke(); }
        }
    }
}
