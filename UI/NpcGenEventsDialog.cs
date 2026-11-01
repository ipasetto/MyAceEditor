using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Eventos do mapa = controladores do npcgen.data (PW-KB §S22/§S31; editor oficial "怪物控制器"): ligam e desligam os pontos
/// que apontam para eles (campo Gatilho), sozinhos por data/hora, por espera ou pelo GM (Ctrl+G, ID do controlador).
/// Edita uma cópia; Ctrl+Z/Ctrl+Y dentro da janela; OK devolve a lista nova (um passo do desfazer no NPCGen).
/// </summary>
internal sealed class NpcGenEventsDialog : Form
{
    readonly List<NpcGenTrigger> _list;
    readonly Func<int, int> _pointsUsing;   // ID do evento → quantos pontos usam
    readonly int _version;
    readonly ListView _lv = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
    readonly TextBox _name = new() { Width = 300 };
    readonly NumericUpDown _id = N(0, int.MaxValue), _gmId = N(0, int.MaxValue), _waitStart = N(0, int.MaxValue), _waitStop = N(0, int.MaxValue), _duration = N(0, int.MaxValue);
    readonly CheckBox _auto = new() { AutoSize = true }, _useStart = new() { AutoSize = true }, _useStop = new() { AutoSize = true };
    readonly NumericUpDown[] _start = Enumerable.Range(0, 6).Select(_ => N(-1, 9999)).ToArray(), _stop = Enumerable.Range(0, 6).Select(_ => N(-1, 9999)).ToArray();
    readonly Label _uses = new() { AutoSize = true, ForeColor = Color.DimGray };
    readonly List<List<NpcGenTrigger>> _undo = new(), _redo = new();
    bool _binding;
    readonly HashSet<int> _origDupIds, _origDupGm;   // repetições que já vinham no arquivo não travam o OK

    public List<NpcGenTrigger> Result => _list;

    static NumericUpDown N(int min, int max) => new() { Minimum = min, Maximum = max, Width = 90 };

    public NpcGenEventsDialog(IEnumerable<NpcGenTrigger> triggers, int version, Func<int, int> pointsUsing)
    {
        _list = triggers.Select(t => t.Clone()).ToList(); _version = version; _pointsUsing = pointsUsing;
        _origDupIds = _list.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        _origDupGm = _list.Where(t => t.GmId != 0).GroupBy(t => t.GmId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        Text = Loc.T("ev.title", "Eventos do mapa (controladores)");
        Size = new Size(980, 640); StartPosition = FormStartPosition.CenterParent; KeyPreview = true; BackColor = Color.White;
        _lv.Columns.Add(Loc.T("ev.colId", "ID"), 60); _lv.Columns.Add(Loc.T("ev.colGm", "ID do GM"), 70); _lv.Columns.Add(Loc.T("ev.colName", "Nome"), 200);
        _lv.Columns.Add(Loc.T("ev.colAuto", "Ativo ao abrir"), 90); _lv.Columns.Add(Loc.T("ev.colPoints", "Pontos"), 60);
        var left = new Panel { Dock = DockStyle.Left, Width = 500, Padding = new Padding(8) };
        var lbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 4, 0, 0) };
        Button Btn(string key, string pt, Action a, bool main = false)
        {
            var b = new Button { AutoSize = true, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = main ? UiKit.Accent : Color.White, ForeColor = main ? Color.White : Color.Black };
            UiKit.Decorate(b, key + " " + pt); b.Text = Loc.T(key, pt); b.Click += (_, _) => a(); return b;
        }
        lbar.Controls.AddRange(new Control[] { Btn("ev.new", "＋ Evento", AddEvent), Btn("ev.clone", "Clonar", CloneEvent), Btn("ev.delete", "Excluir", DeleteEvent) });
        left.Controls.Add(_lv); left.Controls.Add(lbar);

        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
        Label Cap(string key, string pt) => new() { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Color.FromArgb(40, 90, 160), Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(3, 10, 3, 2) };
        Label Hint(string key, string pt) => new() { Text = Loc.T(key, pt), AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Color.DimGray };
        FlowLayoutPanel Row(params Control[] c) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) }; r.Controls.AddRange(c); return r; }
        right.Controls.Add(Cap("ev.name", "Nome")); right.Controls.Add(_name);
        right.Controls.Add(Cap("ev.ids", "ID do evento  /  ID do GM (controlador)"));
        right.Controls.Add(Row(_id, _gmId)); right.Controls.Add(Hint("ev.idsHint", "Os pontos apontam para o ID do evento (campo Gatilho). O GM liga/desliga pelo ID do controlador (Ctrl+G, atividade)."));
        right.Controls.Add(_uses);
        _auto.Text = Loc.T("ev.auto", "Ativo quando o mapa abre"); right.Controls.Add(_auto);
        right.Controls.Add(Cap("ev.waits", "Espera para ligar  /  desliga sozinho depois de (s)"));
        right.Controls.Add(Row(_waitStart, _waitStop)); right.Controls.Add(Hint("ev.waitsHint", "Segundos depois de ativado até os pontos nascerem; e quanto tempo fica ativo (0 = não desliga sozinho)."));
        string[] cols = { Loc.T("ev.year", "ano"), Loc.T("ev.month", "mês"), Loc.T("ev.wday", "dia sem."), Loc.T("ev.day", "dia"), Loc.T("ev.hour", "hora"), Loc.T("ev.min", "min") };
        Control Sched(CheckBox use, string key, string pt, NumericUpDown[] f)
        {
            use.Text = Loc.T(key, pt);
            var heads = Row(cols.Select(c => (Control)new Label { Text = c, Width = 60, ForeColor = Color.DimGray }).ToArray());
            foreach (var n in f) n.Width = 60;
            var box = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            box.Controls.AddRange(new Control[] { use, heads, Row(f) });
            return box;
        }
        right.Controls.Add(Cap("ev.schedule", "Agenda"));
        right.Controls.Add(Sched(_useStart, "ev.useStart", "Liga numa data/hora", _start));
        right.Controls.Add(Sched(_useStop, "ev.useStop", "Desliga numa data/hora", _stop));
        right.Controls.Add(Hint("ev.schedHint", "-1 = qualquer. Ex.: ano -1, mês -1, dia sem. 6, dia -1, hora 20, min 0 = todo sábado às 20:00 (dia da semana 0 = domingo [?])."));
        right.Controls.Add(Cap("ev.duration", "Duração (s) — só npcgen v8+")); right.Controls.Add(_duration);
        _duration.Enabled = version >= 8;

        var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var ok = Btn("ev.ok", "OK", () => { DialogResult = DialogResult.OK; Close(); }, main: true);
        var cancel = Btn("ev.cancel", "Cancelar", () => { DialogResult = DialogResult.Cancel; Close(); });
        foot.Controls.AddRange(new Control[] { ok, cancel, new Label { Text = Loc.T("ev.undoHint", "Ctrl+Z / Ctrl+Y desfazem aqui dentro"), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 20, 0) } });
        Controls.Add(right); Controls.Add(left); Controls.Add(foot);
        AcceptButton = null; CancelButton = null;

        _lv.SelectedIndexChanged += (_, _) => ShowCur();
        _name.TextChanged += (_, _) => Edited(t => { if (t.Name != _name.Text) t.Name = _name.Text; });
        _id.ValueChanged += (_, _) => Edited(t => t.Id = (int)_id.Value);
        _gmId.ValueChanged += (_, _) => Edited(t => t.GmId = (int)_gmId.Value);
        _waitStart.ValueChanged += (_, _) => Edited(t => t.WaitStart = (int)_waitStart.Value);
        _waitStop.ValueChanged += (_, _) => Edited(t => t.WaitStop = (int)_waitStop.Value);
        _duration.ValueChanged += (_, _) => Edited(t => t.Duration = (int)_duration.Value);
        _auto.CheckedChanged += (_, _) => Edited(t => t.AutoStart = (byte)(_auto.Checked ? 1 : 0));
        _useStart.CheckedChanged += (_, _) => Edited(t => t.NoStartSchedule = (byte)(_useStart.Checked ? 0 : 1));
        _useStop.CheckedChanged += (_, _) => Edited(t => t.NoStopSchedule = (byte)(_useStop.Checked ? 0 : 1));
        for (int i = 0; i < 6; i++)
        {
            int k = i;
            _start[i].ValueChanged += (_, _) => Edited(t => t.Start[k] = (int)_start[k].Value);
            _stop[i].ValueChanged += (_, _) => Edited(t => t.Stop[k] = (int)_stop[k].Value);
        }
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z && ActiveControl is not TextBox) { e.Handled = UndoStep(_undo, _redo); }
            else if (e.Control && e.KeyCode == Keys.Y && ActiveControl is not TextBox) { e.Handled = UndoStep(_redo, _undo); }
        };
        FillList(0);
    }

    NpcGenTrigger? Cur => _lv.SelectedIndices.Count > 0 && _lv.SelectedIndices[0] < _list.Count ? _list[_lv.SelectedIndices[0]] : null;

    void FillList(int select)
    {
        _lv.BeginUpdate(); _lv.Items.Clear();
        foreach (var t in _list)
            _lv.Items.Add(new ListViewItem(new[] { t.Id.ToString(), t.GmId.ToString(), t.Name, t.AutoStart != 0 ? "✓" : "", _pointsUsing(t.Id).ToString() }));
        _lv.EndUpdate();
        if (_list.Count > 0) _lv.Items[Math.Clamp(select, 0, _list.Count - 1)].Selected = true; else ShowCur();
    }

    void ShowCur()
    {
        var t = Cur;
        _binding = true;
        foreach (Control c in new Control[] { _name, _id, _gmId, _waitStart, _waitStop, _auto, _useStart, _useStop }.Concat(_start).Concat(_stop)) c.Enabled = t is not null;
        if (t is not null)
        {
            _name.Text = t.Name; _id.Value = Math.Max(0, t.Id); _gmId.Value = Math.Max(0, t.GmId); _waitStart.Value = Math.Max(0, t.WaitStart); _waitStop.Value = Math.Max(0, t.WaitStop);
            _duration.Value = Math.Max(0, t.Duration); _auto.Checked = t.AutoStart != 0; _useStart.Checked = t.NoStartSchedule == 0; _useStop.Checked = t.NoStopSchedule == 0;
            for (int i = 0; i < 6; i++) { _start[i].Value = Math.Clamp(t.Start[i], -1, 9999); _stop[i].Value = Math.Clamp(t.Stop[i], -1, 9999); }
            int n = _pointsUsing(t.Id);
            _uses.Text = n == 0 ? Loc.T("ev.noPoints", "Nenhum ponto usa este evento ainda (escolha o evento no campo Gatilho do ponto).") : string.Format(Loc.T("ev.points", "{0} ponto(s) usam este evento."), n);
            foreach (var x in _start) x.Enabled = _useStart.Checked;
            foreach (var x in _stop) x.Enabled = _useStop.Checked;
        }
        _binding = false;
    }

    List<NpcGenTrigger> Copy() => _list.Select(t => t.Clone()).ToList();
    string? _lastEdit; DateTime _lastAt;

    void Remember(string key)
    {
        // mudanças seguidas no mesmo campo viram um passo só
        if (key == _lastEdit && (DateTime.Now - _lastAt).TotalSeconds < 2) { _lastAt = DateTime.Now; return; }
        _undo.Add(Copy()); _redo.Clear(); _lastEdit = key; _lastAt = DateTime.Now;
    }

    void Edited(Action<NpcGenTrigger> set, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(set))] string key = "")
    {
        if (_binding || Cur is not { } t) return;
        Remember(key + _lv.SelectedIndices[0]);
        set(t);
        int i = _lv.SelectedIndices[0];
        var it = _lv.Items[i];
        it.SubItems[0].Text = t.Id.ToString(); it.SubItems[1].Text = t.GmId.ToString(); it.SubItems[2].Text = t.Name; it.SubItems[3].Text = t.AutoStart != 0 ? "✓" : "";
        it.SubItems[4].Text = _pointsUsing(t.Id).ToString();
        ShowCur();
    }

    bool UndoStep(List<List<NpcGenTrigger>> from, List<List<NpcGenTrigger>> to)
    {
        if (from.Count == 0) return false;
        int sel = _lv.SelectedIndices.Count > 0 ? _lv.SelectedIndices[0] : 0;
        to.Add(Copy());
        _list.Clear(); _list.AddRange(from[^1]); from.RemoveAt(from.Count - 1);
        _lastEdit = null;
        FillList(sel);
        return true;
    }

    int NextId(Func<NpcGenTrigger, int> of) => _list.Count == 0 ? 1 : _list.Max(of) + 1;

    void AddEvent()
    {
        Remember("add" + DateTime.Now.Ticks);
        var t = new NpcGenTrigger { Id = NextId(x => x.Id), GmId = NextId(x => x.GmId), NoStartSchedule = 1, NoStopSchedule = 1, Name = Loc.T("ev.newName", "Evento novo") };
        for (int i = 0; i < 6; i++) { t.Start[i] = -1; t.Stop[i] = -1; }
        _list.Add(t);
        FillList(_list.Count - 1);
    }

    void CloneEvent()
    {
        if (Cur is not { } c) return;
        Remember("clone" + DateTime.Now.Ticks);
        var t = c.Clone(); t.Id = NextId(x => x.Id); t.GmId = NextId(x => x.GmId);
        _list.Add(t);
        FillList(_list.Count - 1);
    }

    void DeleteEvent()
    {
        if (Cur is not { } t) return;
        // ponto apontando para evento que não existe: não excluir enquanto houver pontos ligados (troque o Gatilho deles antes)
        if (_pointsUsing(t.Id) is var n and > 0)
        {
            MessageBox.Show(this, string.Format(Loc.T("ev.inUse", "{0} ponto(s) usam este evento. Troque o Gatilho desses pontos antes de excluir."), n), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Remember("del" + DateTime.Now.Ticks);
        int i = _lv.SelectedIndices[0];
        _list.RemoveAt(i);
        FillList(i);
    }

    /// <summary>Problemas que impedem gravar: ID de evento ou de GM repetido.</summary>
    public string? Problem()
    {
        if (_list.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1 && !_origDupIds.Contains(g.Key)) is { } d) return string.Format(Loc.T("ev.dupId", "ID de evento repetido: {0}."), d.Key);
        if (_list.Where(t => t.GmId != 0).GroupBy(t => t.GmId).FirstOrDefault(g => g.Count() > 1 && !_origDupGm.Contains(g.Key)) is { } g2) return string.Format(Loc.T("ev.dupGm", "ID do GM repetido: {0}."), g2.Key);
        return null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && Problem() is { } p) { MessageBox.Show(this, p, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); e.Cancel = true; }
        base.OnFormClosing(e);
    }
}
