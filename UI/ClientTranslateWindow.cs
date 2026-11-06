namespace MyAceEditor.UI;

/// <summary>
/// Escolha do que traduzir de outro cliente (teste 27): quadro De → Para em cima, uma linha por item com o nome e, logo abaixo,
/// o que ele faz, sem precisar clicar para ler. Marcar e desmarcar é direto na linha.
/// </summary>
internal sealed class ClientTranslateWindow : Form
{
    public sealed record Row(string Title, string Detail, bool Checked, bool Enabled);

    readonly List<CheckBox> _boxes = [];
    readonly Button _go;
    static readonly Color Muted = Color.FromArgb(100, 112, 128), Line = Color.FromArgb(222, 227, 234);

    public IReadOnlyList<int> Chosen => [.. _boxes.Select((b, i) => (b, i)).Where(x => x.b.Checked).Select(x => x.i)];

    public ClientTranslateWindow(string from, string fromPath, string to, string toPath, bool running, IReadOnlyList<Row> rows)
    {
        Text = Loc.T("trc.title", "Traduzir a partir de outro cliente");
        Width = 780; Height = 640; MinimumSize = new Size(620, 460); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        BackColor = Color.FromArgb(242, 245, 249); Font = new Font("Segoe UI", 9.5f);

        var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, ColumnCount = 3, Padding = new Padding(14, 12, 14, 6) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40)); head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var tip = new ToolTip();
        Control Side(string title, string name, string path)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0) };
            var n = new Label { Text = name, Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI Semibold", 11f), AutoEllipsis = true };
            var t = new Label { Text = title, Dock = DockStyle.Top, Height = 20, ForeColor = Muted };
            p.Controls.Add(n); p.Controls.Add(t);
            tip.SetToolTip(n, path);
            return p;
        }
        head.Controls.Add(Side(Loc.T("trc.from", "Traduzido, de onde vêm os textos"), from, fromPath), 0, 0);
        head.Controls.Add(new PictureBox { Image = UiKit.Icon("", Color.FromArgb(37, 99, 235), 22), SizeMode = PictureBoxSizeMode.CenterImage, Dock = DockStyle.Fill }, 1, 0);
        head.Controls.Add(Side(Loc.T("trc.to", "Cliente aberto, que vai mudar"), to, toPath), 2, 0);

        var note = new Label { Dock = DockStyle.Top, Height = running ? 46 : 26, Padding = new Padding(16, 2, 14, 0), ForeColor = running ? Color.Firebrick : Muted,
            Text = Loc.T("trc.note", "Só entra o que este cliente tem. Cada arquivo ganha cópia de segurança.") + (running ? "\n" + Loc.T("trc.running", "Feche o jogo deste cliente antes de aplicar.") : "") };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(14, 4, 14, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = Loc.T("trc.what", "O que traduzir"), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 5, 18, 0) });
        var all = new Button { Text = Loc.T("tr.all", "Todos"), AutoSize = true, FlatStyle = FlatStyle.Flat };
        var none = new Button { Text = Loc.T("tr.noneBtn", "Nenhum"), AutoSize = true, FlatStyle = FlatStyle.Flat };
        UiKit.Decorate(all, all.Text); UiKit.Decorate(none, none.Text);
        all.Click += (_, _) => { foreach (var b in _boxes) if (b.Enabled) b.Checked = true; };
        none.Click += (_, _) => { foreach (var b in _boxes) b.Checked = false; };
        bar.Controls.Add(all); bar.Controls.Add(none);

        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 4, 14, 8) };
        foreach (var r in rows)
        {
            var card = new Panel { BackColor = Color.White, Width = 700, Margin = new Padding(0, 0, 0, 6), Padding = new Padding(10, 6, 10, 8) };
            var box = new CheckBox { Text = r.Title, Checked = r.Checked && r.Enabled, Enabled = r.Enabled, AutoSize = false, Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI Semibold", 9.5f) };
            var detail = new Label { Text = r.Detail, Dock = DockStyle.Top, ForeColor = Muted, Padding = new Padding(19, 0, 0, 0), AutoSize = true, MaximumSize = new Size(676, 0) };
            card.Controls.Add(detail); card.Controls.Add(box);
            card.Height = 24 + 16 + detail.PreferredHeight;
            detail.Click += (_, _) => { if (box.Enabled) box.Checked = !box.Checked; };
            box.CheckedChanged += (_, _) => Update();
            _boxes.Add(box);
            list.Controls.Add(card);
        }
        list.SizeChanged += (_, _) => { foreach (Control c in list.Controls) c.Width = list.ClientSize.Width - 30; };

        var foot = new Panel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(14, 8, 14, 18) };
        _go = new Button { Dock = DockStyle.Right, Width = 220, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White, DialogResult = DialogResult.OK };
        _go.FlatAppearance.BorderSize = 0;
        var cancel = new Button { Text = Loc.T("rw.cancel", "Cancelar"), Dock = DockStyle.Left, Width = 120, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        foot.Controls.Add(_go); foot.Controls.Add(cancel);
        Controls.Add(list); Controls.Add(bar); Controls.Add(note); Controls.Add(head); Controls.Add(foot);
        AcceptButton = _go; CancelButton = cancel;
        Disposed += (_, _) => tip.Dispose();
        Update();
    }

    new void Update()
    {
        int n = _boxes.Count(b => b.Checked);
        _go.Text = string.Format(Loc.T("trc.goN", "Traduzir {0} item(ns)"), n);
        _go.Enabled = n > 0;
    }
}
