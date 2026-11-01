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
    // formulário em blocos verticais (título, campo, explicação embaixo), como as outras telas; a grade de 3 colunas quebrava
    // as explicações numa palavra por linha (usuário 01/11)
    readonly FlowLayoutPanel _form = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20, 10, 20, 48), BackColor = Color.White };   // sobra embaixo: o último campo não cola na borda
    readonly TextBox _name = new() { Width = 260 };
    readonly CheckBox _enabled = new() { AutoSize = true };
    readonly NumericUpDown _world = N(1, 1, 999), _ctrl = N(0, 0, 9_999_999), _every = N(30, 1, 100_000), _dur = N(20, 1, 100_000), _cool = N(0, 0, 2_592_000);
    readonly Label _point = new() { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(22, 128, 61), Font = new Font("Segoe UI Semibold", 9.5f) };

    /// <summary>
    /// O ponto escolhido no NPCGen/Mundo 3D vira ponto de evento (EditorForm → NpcGenView.EventControllerForSelected):
    /// devolve mapa, monstro e o ID do controlador; null + motivo.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<(Func<(string Map, string Name, int GmId, int RespawnSec)?> Pick, Func<string?> Why)>? PointSource { get; set; }
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

        _list.Columns.Add(Loc.T("pl.colName", "Plugin"), 160); _list.Columns.Add(Loc.T("pl.colState", "Agora"), 150);
        _list.SelectedIndexChanged += (_, _) => Bind();
        BuildForm();
        _empty.Text = Loc.T("pl.empty2", "Nenhum plugin ainda.\n\nClique em \"Monstro raro novo\" (barra de cima) para criar um monstro que aparece por sorteio.");

        var left = new Panel { Dock = DockStyle.Left, Width = 330, Padding = new Padding(10, 6, 6, 6), BackColor = Bg };
        left.Controls.Add(_list);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 10, 6), BackColor = Bg };
        right.Controls.Add(_form); right.Controls.Add(_empty);
        var logBox = new Panel { Dock = DockStyle.Bottom, Height = 125, Padding = new Padding(10, 0, 10, 8), BackColor = Bg };
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
        const int W = 560;
        Label Section(string key, string pt) => new() { Text = Loc.T(key, pt), AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = UiKit.Accent, Margin = new Padding(0, 14, 0, 2) };
        Label Note(string key, string pt) => new() { Text = Loc.T(key, pt), AutoSize = true, MaximumSize = new Size(W, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 8) };
        void Field(string key, string pt, Control c, string noteKey = "", string notePt = "")
        {
            _form.Controls.Add(new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Fg, Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(0, 6, 0, 2) });
            c.Margin = new Padding(0, 0, 0, noteKey.Length > 0 ? 2 : 8);
            _form.Controls.Add(c);
            if (noteKey.Length > 0) _form.Controls.Add(Note(noteKey, notePt));
        }
        FlowLayoutPanel Line(params Control[] cs) { var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) }; foreach (var c in cs) { c.Margin = new Padding(0, 0, 8, 0); f.Controls.Add(c); } return f; }
        _name.Width = 320; _startText.Width = W - 180; _endText.Width = W - 180;
        _enabled.Text = Loc.T("pl.enabled", "Ligado (sorteia sozinho)");

        Field("pl.name", "Nome do plugin", _name);

        _form.Controls.Add(Section("pl.s1", "1. Qual monstro aparece"));
        _form.Controls.Add(Note("pl.s1Note", "Clique no monstro no NPCGen ou no Mundo 3D e depois no botão abaixo: o editor cria um evento desligado só daquele ponto. Depois use \"Salvar e enviar ao servidor\" (barra de cima) e reinicie o mapa."));
        var pick = new Button { Text = Loc.T("pl.usePoint", "Usar o ponto escolhido no NPCGen / Mundo 3D"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White, Height = 32, Padding = new Padding(8, 0, 8, 0) };
        pick.FlatAppearance.BorderColor = UiKit.Accent;
        pick.Click += (_, _) => UsePoint();
        pick.Margin = new Padding(0, 2, 0, 6);
        _form.Controls.Add(pick);
        _point.Margin = new Padding(0, 0, 0, 6);
        _form.Controls.Add(_point);

        _form.Controls.Add(Section("pl.s2", "2. Quando ele aparece"));
        _enabled.Margin = new Padding(0, 4, 0, 2);
        _form.Controls.Add(_enabled);
        _form.Controls.Add(Note("pl.onNote", "Desligado: só aparece quando chamado (aqui ou no painel do GM)."));
        Field("pl.every", "Sortear a cada (minutos)", _every);
        Field("pl.chance", "Chance de aparecer em cada sorteio (%)", _chance, "pl.chanceNote", "Ex.: 10 = em média 1 vez a cada 10 sorteios.");
        Field("pl.dur", "Fica no mapa por (minutos)", _dur, "pl.durNote", "Depois disso o evento termina e os monstros que sobraram somem.");
        Field("pl.cool", "Espera mínima entre um evento e outro (segundos)", _cool, "pl.coolNote", "Vem do Renascer do ponto (+15 s do jogo): nem o sorteio nem o \"Chamar agora\" fazem ele voltar antes. Ele nasce uma vez só por evento.");

        _form.Controls.Add(Section("pl.s3", "3. Avisos para todos os jogadores"));
        _form.Controls.Add(Note("pl.textNote", "Texto vazio = sem aviso."));
        Field("pl.startText", "Quando aparece", Line(_startCh, _startText));
        Field("pl.endText", "Quando termina", Line(_endCh, _endText));

        _form.Controls.Add(Section("pl.s4", "Detalhes (preenchidos sozinhos)"));
        Field("pl.world", "Mapa (número do gs: 1 = mundo)", _world);
        Field("pl.ctrl", "Evento do ponto (ID no npcgen)", _ctrl, "pl.ctrlNote2", "Preenchido sozinho: o controlador que liga e desliga o ponto.");

        foreach (Control c in new Control[] { _name, _startText, _endText }) c.TextChanged += (_, _) => Edited();
        foreach (var n in new[] { _world, _ctrl, _every, _dur, _chance, _cool }) n.ValueChanged += (_, _) => Edited();
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
        if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0) _list.Items[0].Selected = true;   // já abre num plugin
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
        _startText.Text = r.StartText; _endText.Text = r.EndText; _cool.Value = Math.Clamp(r.CooldownSec, 0, 2_592_000);
        _point.Text = r.Point.Length > 0 ? r.Point : Loc.T("pl.noPoint", "(nenhum ponto ainda)");
        _point.ForeColor = r.Point.Length > 0 ? Color.FromArgb(22, 128, 61) : Muted;
        _binding = false;
    }

    void Edited()
    {
        if (_binding || Sel is not { } r) return;
        Snapshot();
        r.Name = _name.Text.Trim(); r.Enabled = _enabled.Checked; r.WorldTag = (int)_world.Value; r.Controller = (int)_ctrl.Value;
        r.EveryMin = (int)_every.Value; r.DurationMin = (int)_dur.Value; r.ChancePct = (double)_chance.Value;
        r.StartChannel = Channels[Math.Max(0, _startCh.SelectedIndex)].Ch; r.EndChannel = Channels[Math.Max(0, _endCh.SelectedIndex)].Ch;
        r.StartText = _startText.Text.Trim(); r.EndText = _endText.Text.Trim(); r.CooldownSec = (int)_cool.Value;
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

    void UsePoint()
    {
        if (Sel is not { } r || PointSource?.Invoke() is not { } src) return;
        if (src.Pick() is not { } p) { Status("⚠ " + (src.Why() ?? "")); return; }
        Snapshot();
        r.Controller = p.GmId; r.Point = $"{p.Map} · {p.Name}"; r.CooldownSec = p.RespawnSec;
        if (r.Name.StartsWith(Loc.T("pl.newName", "Monstro raro {0}").Split('{')[0], StringComparison.Ordinal)) r.Name = p.Name;
        _dirty = true;
        var map = p.Map;
        Work(() =>
        {
            // mapa -> worldtag do gs.conf: world = 1; aNN = isNN (docs/kb/MAPAS.md)
            int tag = map.Equals("world", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (tag == 0 && map.Length > 1 && map[0] == 'a')
                tag = Admin().LiveMapList(null).FirstOrDefault(d => d.Tag.Equals("is" + map[1..], StringComparison.OrdinalIgnoreCase))?.WorldTag ?? 0;
            if (tag > 0) r.WorldTag = tag;
            return string.Format(Loc.T("pl.pointDone", "Ponto ligado ao evento {0}. Agora: Salvar e enviar ao servidor (o NPCGen mudou) e reiniciar o mapa; depois Salvar e enviar aqui."), p.GmId)
                + (tag == 0 ? " " + Loc.T("pl.noTag", "Confira o mapa (worldtag): não achei no gs.conf.") : "");
        });
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
