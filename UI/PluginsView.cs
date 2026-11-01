using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Área "Plugins" (docs/PLUGINS.md, etapa 1): eventos automáticos feitos pelo programa myaceplugins no servidor. Lista os
/// plugins, edita o "Monstro raro" (a cada X min sorteia N%; se cair, liga o controlador do npcgen por Y min e avisa todos),
/// grava no servidor, liga/desliga o programa, chama/encerra/pausa um evento e mostra o estado e o histórico ao vivo.
/// </summary>
internal sealed class PluginsView : Panel
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128);
    static readonly (int Ch, string Key, string Pt)[] Channels = { (9, "pl.ch9", "Anúncio (todos)"), (8, "pl.ch8", "Sistema"), (1, "pl.ch1", "Mundo"), (12, "pl.ch12", "Super mundo") };

    readonly StatusCard _card = new();
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(24, 28, 34), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    readonly TableLayoutPanel _form = new() { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12, 8, 12, 8), AutoScroll = true, BackColor = Color.White };
    readonly TextBox _name = new() { Width = 260 };
    readonly CheckBox _enabled = new() { AutoSize = true };
    readonly NumericUpDown _world = N(1, 1, 999), _ctrl = N(0, 0, 9_999_999), _every = N(30, 1, 100_000), _dur = N(20, 1, 100_000);
    readonly NumericUpDown _chance = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 100, Value = 10 };
    readonly ComboBox _startCh = Ch(), _endCh = Ch();
    readonly TextBox _startText = new() { Width = 420 }, _endText = new() { Width = 420 };
    readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 11f), BackColor = Color.White };
    readonly ToolStripButton _send, _fire, _stop, _pause, _resume, _runOn, _runOff;
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 5000 };
    readonly Stack<string> _undo = new(), _redo = new();
    List<PluginRule> _rules = new();
    Dictionary<string, string> _state = new();
    bool _binding, _dirty, _busy;

    static NumericUpDown N(int v, int min, int max) => new() { Minimum = min, Maximum = max, Value = v, Width = 100 };
    static ComboBox Ch() { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 }; foreach (var x in Channels) c.Items.Add(Loc.T(x.Key, x.Pt)); c.SelectedIndex = 0; return c; }

    public PluginsView()
    {
        Dock = DockStyle.Fill; BackColor = Bg;
        var top = UiKit.Ribbon();
        UiKit.Cmd(top, UiKit.Add, "pl.rNew", "Monstro raro novo", NewRare, "pl.rNewTip", "Plugin que de tempos em tempos sorteia se um monstro (ou grupo) aparece.");
        UiKit.Cmd(top, UiKit.Delete, "pl.rDelete", "Excluir", DeleteSel, "pl.rDeleteTip", "Tira o plugin escolhido (vale ao Salvar e enviar).");
        UiKit.Sep(top);
        _send = UiKit.Cmd(top, UiKit.Upload, "pl.rSend", "Salvar e enviar", () => Work(SaveAndSend), "pl.rSendTip", "Grava os plugins no servidor; o programa relê sozinho.", primary: true);
        UiKit.Sep(top);
        _fire = UiKit.Cmd(top, UiKit.Play, "pl.rFire", "Chamar agora", () => Command("fire"), "pl.rFireTip", "Faz o evento acontecer agora, sem sorteio (também pelo painel do GM).");
        _stop = UiKit.Cmd(top, UiKit.Stop, "pl.rStop", "Encerrar", () => Command("stop"), "pl.rStopTip", "Termina o evento ativo agora: os monstros somem e sai o aviso de fim.");
        _pause = UiKit.Cmd(top, UiKit.Hide, "pl.rPause", "Pausar", () => Command("pause"), "pl.rPauseTip", "Para de sortear este plugin (não apaga).");
        _resume = UiKit.Cmd(top, UiKit.Refresh, "pl.rResume", "Retomar", () => Command("resume"), "pl.rResumeTip", "Volta a sortear.");
        UiKit.Sep(top);
        _runOn = UiKit.Cmd(top, UiKit.Server, "pl.rRunOn", "Ligar programa", () => Work(StartProgram), "pl.rRunOnTip", "Instala (se preciso) e liga o programa dos plugins no servidor local.");
        _runOff = UiKit.Cmd(top, UiKit.Cancel, "pl.rRunOff", "Desligar programa", () => Work(() => { Admin().PluginsStop(); return Loc.T("pl.offDone", "Programa desligado."); }));

        _list.Columns.Add(Loc.T("pl.colName", "Plugin"), 220); _list.Columns.Add(Loc.T("pl.colState", "Agora"), 200);
        _list.SelectedIndexChanged += (_, _) => Bind();
        BuildForm();
        _empty.Text = Loc.T("pl.empty", "Nenhum plugin escolhido.\n\nClique em \"Monstro raro novo\" para criar um evento que aparece por sorteio.\nAntes, crie o evento (controlador) no NPCGen → Eventos e ligue os pontos do monstro a ele.");

        var left = new Panel { Dock = DockStyle.Left, Width = 440, Padding = new Padding(10, 6, 6, 6), BackColor = Bg };
        left.Controls.Add(_list);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 10, 6), BackColor = Bg };
        right.Controls.Add(_form); right.Controls.Add(_empty);
        var logBox = new Panel { Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(10, 0, 10, 8), BackColor = Bg };
        logBox.Controls.Add(_log);
        logBox.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = Loc.T("pl.history", "Histórico do programa (servidor)"), ForeColor = Muted, Font = new Font("Segoe UI Semibold", 9f) });
        Controls.Add(right); Controls.Add(left); Controls.Add(logBox); Controls.Add(_card); Controls.Add(top);
        _poll.Tick += (_, _) => { if (Visible && !_busy) Work(Refresh, quiet: true); };
        VisibleChanged += (_, _) => { _poll.Enabled = Visible; if (Visible) Work(Load, quiet: true); };
        Disposed += (_, _) => _poll.Dispose();
        Bind();
    }

    void BuildForm()
    {
        _form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); _form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string key, string pt, Control c, string noteKey = "", string notePt = "")
        {
            int r = _form.RowCount++;
            _form.Controls.Add(new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Fg, Margin = new Padding(3, 7, 6, 3) }, 0, r);
            c.Margin = new Padding(3, 3, 6, 3); _form.Controls.Add(c, 1, r);
            if (noteKey.Length > 0) _form.Controls.Add(new Label { Text = Loc.T(noteKey, notePt), AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Muted, Margin = new Padding(3, 7, 3, 3) }, 2, r);
        }
        void Title(string key, string pt) { int r = _form.RowCount++; var l = new Label { Text = Loc.T(key, pt), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiKit.Accent, Margin = new Padding(3, 12, 3, 4) }; _form.Controls.Add(l, 0, r); _form.SetColumnSpan(l, 3); }
        _enabled.Text = Loc.T("pl.enabled", "Ligado (sorteia sozinho)");
        Title("pl.tWhat", "Monstro raro");
        Row("pl.name", "Nome", _name);
        Row("pl.on", "Sorteio", _enabled, "pl.onNote", "Desligado: só aparece quando chamado (aqui ou no painel do GM).");
        Row("pl.world", "Mapa (worldtag)", _world, "pl.worldNote", "1 = mundo; instâncias pelo número do gs.conf.");
        Row("pl.ctrl", "Evento do npcgen (ID)", _ctrl, "pl.ctrlNote", "O ID do jogo do controlador (NPCGen → Eventos). Os pontos do monstro precisam estar ligados a ele.");
        Title("pl.tWhen", "Quando aparece");
        Row("pl.every", "A cada (min)", _every, "pl.everyNote", "De quanto em quanto tempo o programa sorteia.");
        Row("pl.chance", "Chance (%)", _chance, "pl.chanceNote", "Ex.: 10 = em média 1 vez a cada 10 sorteios.");
        Row("pl.dur", "Fica (min)", _dur, "pl.durNote", "Depois disso o evento termina e os monstros que sobraram somem.");
        Title("pl.tMsg", "Avisos para todos");
        Row("pl.startCh", "Ao aparecer: canal", _startCh); Row("pl.startText", "Texto", _startText, "pl.textNote", "Vazio = sem aviso.");
        Row("pl.endCh", "Ao terminar: canal", _endCh); Row("pl.endText", "Texto", _endText);
        foreach (Control c in new Control[] { _name, _startText, _endText }) c.TextChanged += (_, _) => Edited();
        foreach (var n in new[] { _world, _ctrl, _every, _dur, _chance }) n.ValueChanged += (_, _) => Edited();
        foreach (var cb in new[] { _startCh, _endCh }) cb.SelectedIndexChanged += (_, _) => Edited();
        _enabled.CheckedChanged += (_, _) => Edited();
    }

    static ServerAdmin Admin() => new(new LocalServer(""));
    PluginRule? Sel => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as PluginRule : null;

    // ---------- lista / formulário ----------
    void Fill()
    {
        var keep = Sel?.Id;
        _list.BeginUpdate(); _list.Items.Clear();
        foreach (var r in _rules)
        {
            var it = new ListViewItem(new[] { r.Name.Length > 0 ? r.Name : r.Id, _state.GetValueOrDefault(r.Id, _dirty ? Loc.T("pl.notSent", "não enviado") : "") }) { Tag = r };
            if (_state.GetValueOrDefault(r.Id, "").StartsWith("ATIVO", StringComparison.Ordinal)) it.ForeColor = Color.FromArgb(22, 128, 61);
            _list.Items.Add(it);
            if (r.Id == keep) it.Selected = true;
        }
        _list.EndUpdate();
        Bind();
    }

    void Bind()
    {
        var r = Sel;
        _form.Visible = r is not null; _empty.Visible = r is null;
        foreach (var b in new[] { _fire, _stop, _pause, _resume }) b.Enabled = r is not null;
        if (r is null) return;
        _binding = true;
        _name.Text = r.Name; _enabled.Checked = r.Enabled; _world.Value = Math.Clamp(r.WorldTag, 1, 999); _ctrl.Value = Math.Max(0, r.Controller);
        _every.Value = Math.Clamp(r.EveryMin, 1, 100_000); _dur.Value = Math.Clamp(r.DurationMin, 1, 100_000); _chance.Value = (decimal)Math.Clamp(r.ChancePct, 0, 100);
        _startCh.SelectedIndex = Math.Max(0, Array.FindIndex(Channels, c => c.Ch == r.StartChannel)); _endCh.SelectedIndex = Math.Max(0, Array.FindIndex(Channels, c => c.Ch == r.EndChannel));
        _startText.Text = r.StartText; _endText.Text = r.EndText;
        _binding = false;
    }

    void Edited()
    {
        if (_binding || Sel is not { } r) return;
        Snapshot();
        r.Name = _name.Text.Trim(); r.Enabled = _enabled.Checked; r.WorldTag = (int)_world.Value; r.Controller = (int)_ctrl.Value;
        r.EveryMin = (int)_every.Value; r.DurationMin = (int)_dur.Value; r.ChancePct = (double)_chance.Value;
        r.StartChannel = Channels[Math.Max(0, _startCh.SelectedIndex)].Ch; r.EndChannel = Channels[Math.Max(0, _endCh.SelectedIndex)].Ch;
        r.StartText = _startText.Text.Trim(); r.EndText = _endText.Text.Trim();
        _dirty = true;
        if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].Text = r.Name.Length > 0 ? r.Name : r.Id;
        Status();
    }

    // Ctrl+Z / Ctrl+Y (regra do usuário): fotos das regras antes de cada mudança
    void Snapshot() { _undo.Push(PluginFiles.Write(_rules)); _redo.Clear(); }
    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);
    bool Swap(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return false;
        to.Push(PluginFiles.Write(_rules)); _rules = PluginFiles.Parse(from.Pop()); _dirty = true; Fill(); Status();
        return true;
    }

    void NewRare()
    {
        Snapshot();
        int n = 1; while (_rules.Any(r => r.Id == $"raro{n}")) n++;
        var r = new PluginRule { Id = $"raro{n}", Name = string.Format(Loc.T("pl.newName", "Monstro raro {0}"), n),
            StartText = Loc.T("pl.newStart", "Um monstro raro apareceu! Corra antes que ele suma."), EndText = Loc.T("pl.newEnd", "O monstro raro foi embora.") };
        _rules.Add(r); _dirty = true; Fill();
        foreach (ListViewItem it in _list.Items) it.Selected = it.Tag == r;
        Status();
    }

    void DeleteSel()
    {
        if (Sel is not { } r) return;
        Snapshot(); _rules.Remove(r); _dirty = true; Fill(); Status();
    }

    // ---------- servidor (sempre fora da tela: o WSL demora) ----------
    void Work(Func<string?> job, bool quiet = false)
    {
        if (_busy) return;
        _busy = true;
        if (!quiet) _card.Set(StatusCard.Kind.Busy, Loc.T("pl.working", "Falando com o servidor…"), "");
        Task.Run(() => { try { return job(); } catch (Exception ex) { return "⚠ " + ex.Message; } })
            .ContinueWith(t => BeginInvoke(() => { _busy = false; Fill(); Status(t.Result); }));
    }

    bool _running;
    string? Load()
    {
        if (!LocalServer.DistroExists()) { _running = false; return Loc.T("pl.noServer", "Servidor local não instalado (área Servidor)."); }
        var a = Admin();
        var rules = a.PluginsLoad();
        if (!_dirty) _rules = rules;
        return Refresh();
    }

    string? Refresh()
    {
        if (!LocalServer.DistroExists()) return null;
        var a = Admin();
        _running = a.PluginsRunning();
        _state = a.PluginsState();
        var log = a.PluginsLog();
        BeginInvoke(() => { _log.Text = log.Replace("\n", Environment.NewLine); _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); });
        return null;
    }

    string? SaveAndSend()
    {
        var a = Admin();
        a.PluginsSave(_rules);
        _dirty = false;
        Refresh();
        return _running ? Loc.T("pl.sent", "Plugins gravados no servidor; o programa já está usando.")
            : Loc.T("pl.sentOff", "Plugins gravados. O programa está desligado: clique em Ligar programa.");
    }

    string? StartProgram()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "plugins", "myaceplugins");
        var a = Admin();
        if (File.Exists(exe) && a.PluginsInstall(exe) is { } err) return "⚠ " + err;
        if (!File.Exists(exe) && !a.PluginsRunning()) return "⚠ " + Loc.T("pl.noExe", "Programa dos plugins não encontrado junto do editor (pasta plugins).");
        a.PluginsStart();
        Thread.Sleep(1500);
        Refresh();
        return _running ? Loc.T("pl.onDone", "Programa ligado.") : "⚠ " + Loc.T("pl.onFail", "O programa não ficou ligado; veja o histórico.");
    }

    void Command(string cmd)
    {
        if (Sel is not { } r) return;
        if (_dirty) { Status("⚠ " + Loc.T("pl.sendFirst", "Clique em Salvar e enviar antes: o programa só conhece o que está no servidor.")); return; }
        Work(() =>
        {
            var a = Admin();
            if (!a.PluginsRunning()) return "⚠ " + Loc.T("pl.runFirst", "Ligue o programa primeiro (Ligar programa).");
            a.PluginsCommand(cmd, r.Id);
            Thread.Sleep(2500);
            Refresh();
            return string.Format(Loc.T("pl.cmdDone", "{0}: comando enviado ({1})."), r.Name, cmd);
        });
    }

    void Status(string? msg = null)
    {
        _send.Enabled = _dirty;
        _runOn.Enabled = !_running; _runOff.Enabled = _running;
        var title = _running ? Loc.T("pl.cardOn", "Programa dos plugins ligado no servidor") : Loc.T("pl.cardOff", "Programa dos plugins desligado");
        var detail = msg ?? (_dirty ? Loc.T("pl.dirty", "● Mudanças não enviadas: clique em Salvar e enviar.")
            : string.Format(Loc.T("pl.cardDetail", "{0} plugin(s) · última batida {1}"), _rules.Count, _state.GetValueOrDefault("_running", "—")));
        _card.Set(_dirty ? StatusCard.Kind.Warn : _running ? StatusCard.Kind.On : StatusCard.Kind.Off, title, detail);
    }
}
