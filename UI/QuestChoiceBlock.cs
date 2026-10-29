using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Um requisito ou prêmio escolhido no painel de criar quest: título, ✕ e os campos do tipo da opção.</summary>
internal sealed class QuestChoiceBlock : FlowLayoutPanel
{
    static readonly Color Field = Color.FromArgb(18, 20, 24);
    public QuestOption Option { get; }
    readonly Func<object> _value;
    public event Action<QuestChoiceBlock>? Removed;

    public QuestChoiceBlock(QuestOption o, IQuestHost host, ToolTip tip)
    {
        Option = o;
        FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoSize = true;
        BackColor = Color.FromArgb(34, 39, 46); Padding = new Padding(6, 4, 6, 6); Margin = new Padding(3, 4, 3, 2); Width = 334;
        var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var title = new Label { Text = o.Text, AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 4, 6, 0) };
        tip.SetToolTip(title, o.Tip);
        var x = new Button { Text = "✕", Width = 26, Height = 22, FlatStyle = FlatStyle.Flat, ForeColor = Color.Silver, Margin = new Padding(0) };
        x.FlatAppearance.BorderSize = 0;
        tip.SetToolTip(x, Loc.T("qe.removeOpt", "Tirar esta opção"));
        x.Click += (_, _) => Removed?.Invoke(this);
        head.Controls.Add(title); head.Controls.Add(x);
        Controls.Add(head);
        Controls.Add(new Label { Text = o.Tip, AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 3) });

        NumericUpDown Num(long max = int.MaxValue) => new() { Minimum = 0, Maximum = max, Width = 120, BackColor = Field, ForeColor = Color.White, ThousandsSeparator = true };
        Label Small(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 6, 3, 0) };
        switch (o.Type)
        {
            case QOType.Number:
            {
                var n = Num(); Controls.Add(n); _value = () => (long)n.Value; break;
            }
            case QOType.Range:
            {
                var (la, lb) = o.Id == "skill" ? (Loc.T("qe.skillId", "ID"), Loc.T("qe.skillLv", "nível")) : (Loc.T("qe.min", "mín."), Loc.T("qe.max", "máx."));
                var a = Num(); var b = Num();
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                row.Controls.AddRange(new Control[] { Small(la), a, Small(lb), b });
                Controls.Add(row);
                _value = () => ((long)a.Value, (long)b.Value);
                break;
            }
            case QOType.Flag:
                Controls.Add(Small(Loc.T("qe.flagOn", "✓ ligado")));
                _value = () => true;
                break;
            case QOType.Gender:
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                c.Items.AddRange(new object[] { Loc.T("qe.male", "Masculino"), Loc.T("qe.female", "Feminino") });
                c.SelectedIndex = 0;
                Controls.Add(c);
                _value = () => (long)(c.SelectedIndex + 1);   // 1 = masculino, 2 = feminino (0 = qualquer)
                break;
            }
            case QOType.Classes:
            {
                var list = new CheckedListBox { Width = 320, Height = 110, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.None, CheckOnClick = true, MultiColumn = true, ColumnWidth = 150 };
                for (int i = 0; i < 12; i++) list.Items.Add(FieldKinds.ClassName(i));
                Controls.Add(list);
                _value = () => list.CheckedIndices.Cast<int>().ToList();
                break;
            }
            case QOType.Quests:
            {
                var box = new TextBox { Width = 320, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = Loc.T("qe.questIdsPh", "IDs das quests, separados por vírgula") };
                var names = new Label { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.Silver };
                List<int> Ids() => box.Text.Split(',', ';', ' ').Select(p => int.TryParse(p.Trim(), out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();
                box.TextChanged += (_, _) => names.Text = string.Join("\n", Ids().Select(id => $"{id}: {host.QuestName(id) ?? Loc.T("qe.noQuest", "(quest não existe)")}"));
                Controls.Add(box); Controls.Add(names);
                _value = () => Ids();
                break;
            }
            case QOType.Items:
            {
                var lb = new ListBox { Width = 320, Height = 70, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.None };
                var id = Num(); var n = Num(99999); n.Value = 1; n.Width = 70;
                var nm = new Label { AutoSize = true, ForeColor = Color.Silver };
                id.ValueChanged += (_, _) => nm.Text = id.Value <= 0 ? "" : host.NameOf((int)id.Value) ?? Loc.T("qe.unknown", "(não existe no elements.data)");
                var add = new Button { Text = Loc.T("qe.add", "Adicionar"), AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
                var items = new List<(int, int)>();
                add.Click += (_, _) =>
                {
                    if (id.Value <= 0 || host.NameOf((int)id.Value) is not { } name) return;
                    items.Add(((int)id.Value, (int)n.Value)); lb.Items.Add($"{n.Value} × {name} ({id.Value})");
                };
                lb.DoubleClick += (_, _) => { if (lb.SelectedIndex >= 0) { items.RemoveAt(lb.SelectedIndex); lb.Items.RemoveAt(lb.SelectedIndex); } };
                tip.SetToolTip(lb, Loc.T("qe.itemRemoveTip", "Clique duas vezes num item para tirar."));
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                row.Controls.AddRange(new Control[] { id, n, add });
                Controls.Add(lb); Controls.Add(row); Controls.Add(nm);
                _value = () => items.ToList();
                break;
            }
            default:   // Field: qualquer campo simples da área
            {
                var fields = host.FreeFields(o.Reward);
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DropDownWidth = 420 };
                foreach (var f in fields) c.Items.Add(f.Label);
                var v = new TextBox { Width = 120, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Text = "0" };
                var hint = new Label { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.Gray };
                c.SelectedIndexChanged += (_, _) => hint.Text = c.SelectedIndex >= 0 ? fields[c.SelectedIndex].Tip : "";
                Controls.Add(c); Controls.Add(v); Controls.Add(hint);
                _value = () => (c.SelectedIndex >= 0 ? fields[c.SelectedIndex].Key : "", v.Text.Trim());
                break;
            }
        }
    }

    public QuestChoice Choice => new(Option, _value());
}
