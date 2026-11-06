using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Caminho desenhado num ponto com mais de uma criatura: um ponto do npcgen pode juntar vários tipos e dezenas de criaturas,
/// e o caminho vale para o ponto inteiro (teste 24: 120 vespas em fila). Aqui a pessoa escolhe se cria um ponto só para uma
/// criatura, o que deixa o grupo como está, ou se todas seguem o caminho.
/// </summary>
internal sealed class PathWhoDialog : Form
{
    /// <summary>Índice da criatura que ganha ponto próprio, ou -1 quando todas seguem o caminho.</summary>
    public int Only { get; private set; } = -1;

    public PathWhoDialog(NpcGenSpawn s, Func<int, string> name)
    {
        int total = s.Creatures.Sum(c => Math.Max(c.Amount, 1)), types = s.Creatures.Count;
        Text = Loc.T("path.whoTitle", "Quem anda neste caminho?");
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White; Font = new Font("Segoe UI", 9.5f);

        var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18, 16, 18, 20) };
        Label Note(string text, Color color) => new() { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = color, Margin = new Padding(0, 0, 0, 10) };
        var gray = Color.FromArgb(90, 96, 106);
        body.Controls.Add(Note(string.Format(Loc.T("path.whoBody", "Este ponto tem {0} criaturas de {1} tipo(s). O caminho do ponto vale para todas elas."), total, types), Color.FromArgb(30, 34, 40)));

        var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440, Margin = new Padding(0, 0, 0, 12), Visible = types > 1 };
        foreach (var c in s.Creatures) pick.Items.Add(name(c.Id));
        if (types > 0) pick.SelectedIndex = 0;
        body.Controls.Add(pick);

        var one = new Button { Text = Loc.T("path.whoOne", "Criar 1 criatura só para este caminho"), Width = 440, Height = 36, BackColor = UiKit.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, 4) };
        one.FlatAppearance.BorderSize = 0;
        one.Click += (_, _) => { Only = Math.Max(pick.SelectedIndex, 0); DialogResult = DialogResult.OK; };
        body.Controls.Add(one);
        body.Controls.Add(Note(Loc.T("path.whoOneTip", "Recomendado: faz um ponto novo com 1 criatura. O grupo continua como está."), gray));

        var all = new Button { Text = string.Format(Loc.T("path.whoAll", "Todas as {0} seguem o caminho"), total), Width = 440, Height = 32, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, 6) };
        all.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
        all.Click += (_, _) => { Only = -1; DialogResult = DialogResult.OK; };
        body.Controls.Add(all);

        var cancel = new Button { Text = Loc.T("path.whoBack", "Voltar ao desenho"), Width = 440, Height = 32, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
        body.Controls.Add(cancel);

        Controls.Add(body);
        AcceptButton = one; CancelButton = cancel;
    }
}
