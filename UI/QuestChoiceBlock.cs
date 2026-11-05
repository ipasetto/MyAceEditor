using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Um requisito ou prêmio escolhido no painel de criar quest: título, ✕ e os campos do tipo da opção.</summary>
internal sealed class QuestChoiceBlock : FlowLayoutPanel
{
    static readonly Color Field = Color.FromArgb(18, 20, 24);
    public QuestOption Option { get; }
    readonly Func<object> _value;
    public event Action<QuestChoiceBlock>? Removed;

    /// <param name="initial">Valor que a quest já tem (editar quest); null = vazio.</param>
    public QuestChoiceBlock(QuestOption o, IQuestHost host, ToolTip tip, object? initial = null)
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

        NumericUpDown Num(long max = int.MaxValue) => new() { Minimum = 0, Maximum = max, Width = 120, BackColor = Field, ForeColor = Color.White };
        Label Small(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 6, 3, 0) };
        switch (o.Type)
        {
            case QOType.Number:
            {
                var n = Num(); if (initial is long iv) n.Value = Math.Clamp(iv, 0, int.MaxValue); Controls.Add(n); _value = () => (long)n.Value; break;
            }
            case QOType.Range:
            {
                var (la, lb) = o.Id == "skill" ? (Loc.T("qe.skillId", "ID"), Loc.T("qe.skillLv", "nível")) : (Loc.T("qe.min", "mín."), Loc.T("qe.max", "máx."));
                var a = Num(); var b = Num();
                if (initial is ValueTuple<long, long> r0) { a.Value = Math.Clamp(r0.Item1, 0, int.MaxValue); b.Value = Math.Clamp(r0.Item2, 0, int.MaxValue); }
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
            case QOType.Repeat:
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                for (int i = 1; i <= 4; i++) c.Items.Add(RepeatName(i));
                c.SelectedIndex = initial is long r0 && r0 is >= 1 and <= 4 ? (int)r0 - 1 : 0;
                Controls.Add(c);
                _value = () => (long)(c.SelectedIndex + 1);   // m_lAvailFrequency: 1 dia, 2 semana, 3 mês, 4 ano
                break;
            }
            case QOType.Schedule:
            {
                var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                type.Items.AddRange(new object[] { Loc.T("qe.schDate", "Entre duas datas"), Loc.T("qe.schMonth", "Todo mês, entre dois dias"), Loc.T("qe.schWeek", "Toda semana, entre dois dias da semana"), Loc.T("qe.schDay", "Todo dia, entre duas horas") });
                Controls.Add(type);
                // início e fim: data/hora (o tipo decide o que vale: data inteira, só o dia do mês, só o dia da semana ou só a hora)
                (Control Row, Func<TaskTime> Get, Action<TaskTime> Set) Moment(string label)
                {
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
                    row.Controls.Add(Small(label));
                    var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Width = 150 };
                    var day = Num(31); day.Width = 50; day.Minimum = 1;
                    var wday = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                    for (int i = 1; i <= 7; i++) wday.Items.Add(WeekdayName(i));
                    wday.SelectedIndex = 0;
                    var time = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
                    row.Controls.AddRange(new Control[] { date, day, wday, time });
                    void Show() { int k = Math.Max(0, type.SelectedIndex); date.Visible = k == 0; day.Visible = k == 1; wday.Visible = k == 2; time.Visible = k != 0; }
                    type.SelectedIndexChanged += (_, _) => Show();
                    TaskTime Get() => type.SelectedIndex switch
                    {
                        0 => new(date.Value.Year, date.Value.Month, date.Value.Day, date.Value.Hour, date.Value.Minute, 0),
                        1 => new(0, 0, (int)day.Value, time.Value.Hour, time.Value.Minute, 0),
                        2 => new(0, 0, 0, time.Value.Hour, time.Value.Minute, wday.SelectedIndex + 1),
                        _ => new(0, 0, 0, time.Value.Hour, time.Value.Minute, 0),
                    };
                    void Set(TaskTime t)
                    {
                        if (t.Year > 0) try { date.Value = new DateTime(t.Year, Math.Max(1, t.Month), Math.Max(1, t.Day), t.Hour, t.Min, 0); } catch (ArgumentException) { }
                        day.Value = Math.Clamp(t.Day, 1, 31); wday.SelectedIndex = Math.Clamp(t.WDay, 1, 7) - 1;
                        time.Value = DateTime.Today.AddHours(Math.Clamp(t.Hour, 0, 23)).AddMinutes(Math.Clamp(t.Min, 0, 59));
                    }
                    Show();
                    return (row, Get, Set);
                }
                var start = Moment(Loc.T("qe.schFrom", "de")); var end = Moment(Loc.T("qe.schTo", "até"));
                Controls.Add(start.Row); Controls.Add(end.Row);
                type.SelectedIndex = initial is QuestSchedule s0 ? Math.Clamp(s0.Type, 0, 3) : 3;
                if (initial is QuestSchedule s1) { start.Set(s1.Start); end.Set(s1.End); }
                _value = () => new QuestSchedule(type.SelectedIndex, start.Get(), end.Get());
                break;
            }
            case QOType.Gender:
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                c.Items.AddRange(new object[] { Loc.T("qe.male", "Masculino"), Loc.T("qe.female", "Feminino") });
                c.SelectedIndex = initial is 2L ? 1 : 0;
                Controls.Add(c);
                _value = () => (long)(c.SelectedIndex + 1);   // 1 = masculino, 2 = feminino (0 = qualquer)
                break;
            }
            case QOType.Classes:
            {
                var list = new CheckedListBox { Width = 320, Height = 110, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.None, CheckOnClick = true, MultiColumn = true, ColumnWidth = 150 };
                for (int i = 0; i < 12; i++) list.Items.Add(FieldKinds.ClassName(i), initial is List<int> on && on.Contains(i));
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
                if (initial is List<int> q0) box.Text = string.Join(", ", q0);
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
                if (initial is List<(int Id, int Count)> i0)
                    foreach (var (iid, cnt) in i0) { items.Add((iid, cnt)); lb.Items.Add($"{cnt} × {host.NameOf(iid) ?? "?"} ({iid})"); }
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

    /// <summary>m_lAvailFrequency 1-4.</summary>
    public static string RepeatName(int f) => f switch
    {
        1 => Loc.T("qe.repDay", "Uma vez por dia"), 2 => Loc.T("qe.repWeek", "Uma vez por semana"),
        3 => Loc.T("qe.repMonth", "Uma vez por mês"), _ => Loc.T("qe.repYear", "Uma vez por ano"),
    };

    /// <summary>wday 1-7 do task_tm (1 = segunda … 7 = domingo [?]).</summary>
    public static string WeekdayName(int w) => System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName((DayOfWeek)(w % 7));

    public static string ScheduleText(QuestSchedule s)
    {
        string At(TaskTime t) => s.Type switch
        {
            0 => $"{t.Day:00}/{t.Month:00}/{t.Year} {t.Hour:00}:{t.Min:00}",
            1 => string.Format(Loc.T("qe.schDayN", "dia {0}"), t.Day) + $" {t.Hour:00}:{t.Min:00}",
            2 => $"{WeekdayName(t.WDay)} {t.Hour:00}:{t.Min:00}",
            _ => $"{t.Hour:00}:{t.Min:00}",
        };
        return $"{At(s.Start)} → {At(s.End)}";
    }
}
