using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Anúncios de um monstro (usuário 01/11): o que o jogo anuncia quando ele MORRE ou ENTRA EM COMBATE, em qual canal e com
/// qual texto. Lista os anúncios que ele já tem (gatilhos <see cref="AiAnnounce.Tag"/>), deixa tirar e pôr novos; quem
/// chama grava (clona a política se for compartilhada e troca o common_strategy do monstro), num passo do Ctrl+Z.
/// </summary>
internal sealed class AnnounceDialog : Form
{
    public sealed record NewAnn(AiAnnounce.When When, string Prefix, string Text, bool AppendName);
    public List<NewAnn> Added { get; } = new();
    public List<AiTrigger> Removed { get; } = new();

    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.FixedSingle };
    readonly RadioButton _died = Pill(), _combat = Pill();
    readonly ComboBox _channel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly TextBox _text = new() { Width = 460 };
    readonly CheckBox _name = new() { AutoSize = true };
    readonly Label _preview = new() { AutoSize = true, ForeColor = Color.FromArgb(37, 99, 235), Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(3, 6, 3, 6) };
    readonly bool _canName;

    static RadioButton Pill() => new() { Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 6, 0) };

    public AnnounceDialog(string monster, int policyId, int users, int triggerVersion, IReadOnlyList<AiAnnounce.Info> existing)
    {
        _canName = AiAnnounce.CanAppendName(triggerVersion);
        Text = string.Format(Loc.T("ann.title", "Anúncios — {0}"), monster);
        Width = 720; Height = 700; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        BackColor = Color.White; Font = new Font("Segoe UI", 9.5f);

        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 6) };
        Label Title(string key, string pt) => new() { Text = Loc.T(key, pt), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 10, 0, 4) };
        Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = Color.FromArgb(100, 112, 128), Margin = new Padding(0, 0, 0, 4) };

        body.Controls.Add(Note(users > 1
            ? string.Format(Loc.T("ann.shared", "A IA deste monstro (política {0}) é usada por {1} monstros. Ao salvar, ele ganha uma cópia só dele: os outros não anunciam nada."), policyId, users)
            : policyId > 0 ? string.Format(Loc.T("ann.own", "A IA deste monstro (política {0}) é só dele."), policyId)
            : Loc.T("ann.none", "Este monstro não tem IA: ao salvar ele ganha uma política nova só com os anúncios.")));

        body.Controls.Add(Title("ann.has", "Anúncios que ele já tem"));
        _list.Columns.Add(Loc.T("ann.colWhen", "Quando"), 150); _list.Columns.Add(Loc.T("ann.colChannel", "Canal"), 200); _list.Columns.Add(Loc.T("ann.colText", "Texto"), 290);
        foreach (var a in existing) _list.Items.Add(Row(a.When, a.Prefix, a.Text, a.AppendName, a.Trigger));
        var listBox = new Panel { Width = 660, Height = 130, Margin = new Padding(0) };
        listBox.Controls.Add(_list);
        body.Controls.Add(listBox);
        var remove = Btn(Loc.T("ann.remove", "Tirar o anúncio escolhido"), () =>
        {
            if (_list.SelectedItems.Count == 0) return;
            var it = _list.SelectedItems[0];
            if (it.Tag is AiTrigger t) Removed.Add(t); else if (it.Tag is NewAnn n) Added.Remove(n);
            _list.Items.Remove(it);
        });
        body.Controls.Add(remove);

        body.Controls.Add(Title("ann.new", "Novo anúncio"));
        body.Controls.Add(Note(Loc.T("ann.when", "1. Quando o jogo anuncia")));
        var pills = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        _died.Text = Loc.T("ann.died", "Quando ele morre"); _combat.Text = Loc.T("ann.combat", "Quando ele entra em combate");
        foreach (var p in new[] { _died, _combat }) { p.FlatAppearance.CheckedBackColor = Color.FromArgb(219, 232, 253); p.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225); pills.Controls.Add(p); p.CheckedChanged += (_, _) => Preview(); }
        _died.Checked = true;
        body.Controls.Add(pills);
        body.Controls.Add(Note(Loc.T("ann.channel", "2. Onde aparece")));
        foreach (var c in AiAnnounce.Channels) _channel.Items.Add(Loc.T(c.Key, c.Pt));
        _channel.SelectedIndex = 0;
        body.Controls.Add(_channel);
        body.Controls.Add(Note(Loc.T("ann.textLabel", "3. Texto")));
        _text.Text = Loc.T("ann.textDefault", "derrotou um monstro poderoso!");
        body.Controls.Add(_text);
        _name.Text = Loc.T("ann.name", "Começar com o nome do jogador (quem matou / o alvo)");
        _name.Checked = _canName; _name.Enabled = _canName;
        body.Controls.Add(_name);
        if (!_canName) body.Controls.Add(Note(Loc.T("ann.noName", "Nesta versão da IA o jogo não põe o nome sozinho: escreva no texto o que quiser mostrar.")));
        body.Controls.Add(_preview);
        body.Controls.Add(Btn(Loc.T("ann.add", "+ Pôr na lista"), () =>
        {
            var t = _text.Text.Trim();
            if (t.Length == 0) return;
            var n = new NewAnn(_died.Checked ? AiAnnounce.When.Died : AiAnnounce.When.CombatStart, AiAnnounce.Channels[Math.Max(0, _channel.SelectedIndex)].Prefix, t, _name.Checked && _canName);
            Added.Add(n);
            _list.Items.Add(Row(n.When, n.Prefix, n.Text, n.AppendName, n));
        }, main: true));
        foreach (var c in new Control[] { _channel, _text, _name }) c.Margin = new Padding(0, 0, 0, 6);
        _channel.SelectedIndexChanged += (_, _) => Preview(); _text.TextChanged += (_, _) => Preview(); _name.CheckedChanged += (_, _) => Preview();
        Preview();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 8, 10, 8), BackColor = Color.FromArgb(242, 245, 249) };
        var cancel = new Button { Text = Loc.T("dlg.cancel", "Cancelar"), DialogResult = DialogResult.Cancel, Width = 120, Height = 30, FlatStyle = FlatStyle.Flat };
        var ok = new Button { Text = Loc.T("ann.save", "Aplicar"), DialogResult = DialogResult.OK, Width = 140, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White };
        bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
        bottom.Controls.Add(new Label { Text = Loc.T("ann.saveHint", "Aplicar muda a IA e o monstro (Ctrl+Z desfaz); grave com Salvar."), AutoSize = true, ForeColor = Color.FromArgb(100, 112, 128), Padding = new Padding(0, 6, 10, 0) });
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(body); Controls.Add(bottom);
    }

    static Button Btn(string text, Action a, bool main = false)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 30, Margin = new Padding(0, 4, 0, 6), Padding = new Padding(8, 0, 8, 0) };
        if (main) { b.BackColor = UiKit.Accent; b.ForeColor = Color.White; }
        b.Click += (_, _) => a();
        return b;
    }

    ListViewItem Row(AiAnnounce.When w, string prefix, string text, bool name, object tag)
    {
        var ch = AiAnnounce.Channels.FirstOrDefault(c => c.Prefix == prefix);
        return new ListViewItem(new[] { w == AiAnnounce.When.Died ? Loc.T("ann.died", "Quando ele morre") : Loc.T("ann.combat", "Quando ele entra em combate"),
            Loc.T(ch.Key ?? "ann.chNormal", ch.Pt ?? ""), (name ? Loc.T("ann.playerName", "[nome do jogador] ") : "") + text }) { Tag = tag };
    }

    void Preview()
    {
        var ch = AiAnnounce.Channels[Math.Max(0, _channel.SelectedIndex)];
        _preview.Text = string.Format(Loc.T("ann.preview", "Prévia ({0}):  {1}"), Loc.T(ch.Key, ch.Pt),
            (_name.Checked && _canName ? Loc.T("ann.playerName", "[nome do jogador] ") : "") + _text.Text.Trim());
    }
}
