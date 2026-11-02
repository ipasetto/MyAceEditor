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
    /// <summary>
    /// Catálogo: os plugins que existem (cada um com nome próprio fixo). "Pôr em execução" cria uma cópia na lista "Em
    /// execução no servidor" (pode haver várias do mesmo plugin, cada uma com nome e descrição; usuário 01/11).
    /// </summary>
    static readonly (string Type, string Glyph, string Key, string Pt, string DescKey, string DescPt)[] Catalog =
    {
        ("rare", "\uE734", "pl.tRare", "Aparição Rara", "pl.tRareDesc", "Um monstro escolhido aparece de vez em quando, por sorteio, num ponto do mapa, com aviso para todos. Se ninguém o matar a tempo, ele vai embora."),
    };
    static string TypeName(string type) => Catalog.FirstOrDefault(c => c.Type == type) is { Key: not null } c ? Loc.T(c.Key, c.Pt) : type;

    // as mensagens saem sempre como anúncio para todos (canal 9; usuário 02/11 tirou a escolha de canal)
    const int AnnounceChannel = 9;

    readonly StatusCard _card = new();
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, OwnerDraw = true };
    // topo do cartão nas cores da barra lateral (NavBar), botões no mesmo jeito dela: ícone em cima, nome embaixo (usuário 01/11)
    static readonly Color HeadBg = Color.FromArgb(24, 32, 44), HeadFg = Color.FromArgb(226, 232, 240), HeadMuted = Color.FromArgb(170, 182, 198),
        HeadHover = Color.FromArgb(32, 43, 59), HeadPress = Color.FromArgb(40, 54, 74),
        HeadBlue = Color.FromArgb(96, 165, 250), HeadGreen = Color.FromArgb(74, 222, 128), HeadRed = Color.FromArgb(248, 113, 113);
    // botão "Chamar": verde = pode chamar; vermelho = esperando o tempo do plugin; cinza = programa desligado ou mudança por enviar
    static readonly Color CallGreen = Color.FromArgb(22, 163, 74), CallRed = Color.FromArgb(220, 38, 38), CallGray = Color.FromArgb(185, 190, 198);
    readonly PictureBox _typeIcon = new() { Size = new Size(30, 30), Margin = new Padding(0, 2, 8, 0) };
    readonly Label _desc = new() { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = HeadFg, Margin = new Padding(0, 0, 0, 6) };
    readonly Label _now = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), ForeColor = HeadBlue, Margin = new Padding(0, 0, 0, 8) };
    readonly TextBox _description = new() { Multiline = true, Height = 54, ScrollBars = ScrollBars.Vertical };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(24, 28, 34), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    // formulário em blocos verticais (título, campo, explicação embaixo), como as outras telas; a grade de 3 colunas quebrava
    // as explicações numa palavra por linha (usuário 01/11)
    readonly FlowLayoutPanel _form = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20, 10, 20, 48), BackColor = Color.White };   // sobra embaixo: o último campo não cola na borda
    readonly TextBox _name = new() { Width = 260 };
    readonly CheckBox _enabled = new() { AutoSize = true }, _respawn = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 2) };

    /// <summary>Liga/desliga o renascer dos pontos do evento no npcgen (EditorForm → NpcGenView.SetEventRespawn): mapa, evento, ligar → null ou motivo.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, int, bool, string?>? RespawnSink { get; set; }

    /// <summary>
    /// Anúncio de morte do plugin na IA do monstro (EditorForm.SetPluginDeathAnnounce): id do plugin, ID do monstro, texto
    /// (null = tirar) → null ou motivo. Com renascer desligado a "Mensagem ao terminar" sai na morte (usuário 02/11).
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, int, string?, string?>? DeathSink { get; set; }
    readonly NumericUpDown _world = N(1, 1, 999), _ctrl = N(0, 0, 9_999_999), _every = N(30, 1, 100_000), _dur = N(20, 1, 100_000), _cool = N(0, 0, 2_592_000);
    readonly Label _point = new() { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(22, 128, 61), Font = new Font("Segoe UI Semibold", 9.5f) };

    /// <summary>
    /// O ponto escolhido no NPCGen/Mundo 3D vira ponto de evento (EditorForm → NpcGenView.EventControllerForSelected):
    /// devolve mapa, monstro e o ID do controlador; null + motivo.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<(Func<(string Map, string Name, int GmId, int RespawnSec)?> Pick, Func<string?> Why)>? PointSource { get; set; }
    readonly NumericUpDown _chance = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 100, Value = 10 };
    readonly TextBox _startText = new() { Width = 420 }, _endText = new() { Width = 420 };
    readonly FlowLayoutPanel _head = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 14, 20, 12), BackColor = HeadBg };
    readonly Panel _cardPanel = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    readonly Label _empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 11f), BackColor = Color.White };
    readonly ToolStripButton _send, _runOn, _runOff;
    // ações do plugin ficam no cartão dele (a barra de cima é só do servidor; a tela vai receber outros tipos de plugin)
    readonly Button _fire = Act(UiKit.Play, true);
    readonly HeadButton _stop = new(UiKit.Stop, HeadRed), _pauseToggle = new(PauseGlyph), _delete = new(UiKit.Delete) { Margin = new Padding(16, 0, 4, 0) };   // excluir um pouco afastado
    const string PauseGlyph = "";   // Segoe MDL2 "Pause"
    readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), ForeColor = HeadFg, Margin = new Padding(0, 0, 0, 2) };
    readonly Label _sub = new() { AutoSize = true, ForeColor = HeadMuted, Margin = new Padding(0, 0, 0, 8) };
    readonly Label _result = new() { AutoSize = false, Height = 34, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0), Font = new Font("Segoe UI Semibold", 9.5f), Visible = false };
    readonly Label _chanceInfo = new() { AutoSize = true, ForeColor = Color.FromArgb(37, 99, 235), Font = new Font("Segoe UI Semibold", 9.5f), Padding = new Padding(0, 4, 0, 0) };
    void ChanceInfo() => _chanceInfo.Text = _chance.Value <= 0 ? Loc.T("pl.chanceNever", "= nunca (só chamando)")
        : string.Format(Loc.T("pl.chanceEvery", "≈ 1 vez a cada {0:0.#} sorteios"), 100m / _chance.Value);
    // botão Chamar do cartão, igual ao da lista (usuário 02/11): verde = chamar; vermelho = ativo/esperando com o tempo; cinza = desligado
    readonly CallButton _call = new() { Margin = new Padding(14, 9, 0, 0) };
    readonly ToggleSwitch _onSwitch = new();
    readonly Label _onLabel = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(6, 4, 0, 0) };
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 5000 };
    readonly Stack<string> _undo = new(), _redo = new();
    List<PluginRule> _rules = new();
    Dictionary<string, string> _state = new();
    bool _binding, _dirty, _busy;

    static Button Act(string glyph, bool main = false)
    {
        var b = new Button { AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 0, 10, 0), Margin = new Padding(0, 0, 8, 0),
            Image = UiKit.Icon(glyph, main ? Color.White : Fg, 16), TextImageRelation = TextImageRelation.ImageBeforeText,
            BackColor = main ? UiKit.Accent : Color.White, ForeColor = main ? Color.White : Fg, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(203, 213, 225);
        return b;
    }

    /// <summary>
    /// Botão do topo do cartão no jeito da barra lateral: ícone em cima, nome embaixo, fundo claro ao passar o mouse.
    /// Desenhado aqui porque o botão desligado do Windows fica com texto escuro (some no fundo azul-escuro, usuário 01/11):
    /// desligado = ícone e nome apagados, iguais, sem mãozinha. <paramref name="tint"/> = cor do ícone quando dá para usar
    /// (Encerrar fica vermelho só quando há evento para encerrar).
    /// </summary>
    sealed class HeadButton : Button
    {
        static readonly Color Off = Color.FromArgb(78, 92, 110);
        readonly Color _tint;
        string _glyph;
        bool _hover, _down;
        public HeadButton(string glyph, Color? tint = null)
        {
            _glyph = glyph; _tint = tint ?? HeadMuted;
            Size = new Size(84, 58); Margin = new Padding(0, 0, 4, 0); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = HeadBg; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Enabled && _down ? HeadPress : Enabled && _hover ? HeadHover : HeadBg);
            var ic = UiKit.Icon(_glyph, !Enabled ? Off : _hover ? Color.White : _tint, 22);
            g.DrawImage(ic, (Width - 22) / 2, 8);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 34, Width, 20), !Enabled ? Off : _hover ? Color.White : HeadMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Botão cheio e arredondado com ▶ e texto; a cor diz se dá para chamar (mesmas cores do botão da lista).</summary>
    sealed class CallButton : Button
    {
        Color _fill = CallGray;
        bool _hover;
        public CallButton()
        {
            Size = new Size(170, 40); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = HeadBg; Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        public void Look(Color fill, string text, bool canCall)
        {
            if (_fill == fill && Text == text) return;
            _fill = fill; Text = text; Cursor = canCall ? Cursors.Hand : Cursors.Default; Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(HeadBg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = 10;
                path.AddArc(box.X, box.Y, d, d, 180, 90); path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
                path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90); path.AddArc(box.X, box.Bottom - d, d, d, 90, 90); path.CloseFigure();
                using var b = new SolidBrush(_hover && Cursor == Cursors.Hand ? ControlPaint.Light(_fill, 0.25f) : _fill);
                g.FillPath(b, path);
            }
            var ic = UiKit.Icon(UiKit.Play, Color.White, 16);
            var sz = TextRenderer.MeasureText(g, Text, Font);
            int x = (Width - 16 - 6 - sz.Width) / 2;
            g.DrawImage(ic, x, (Height - 16) / 2);
            TextRenderer.DrawText(g, Text, Font, new Point(x + 22, (Height - sz.Height) / 2), Color.White);
        }
    }

    static NumericUpDown N(int v, int min, int max) => new() { Minimum = min, Maximum = max, Value = v, Width = 100 };

    public PluginsView()
    {
        Dock = DockStyle.Fill; BackColor = Bg;
        // barra de cima: só o que fala com o servidor (o mesmo para qualquer plugin)
        var top = UiKit.Ribbon();
        _send = UiKit.Cmd(top, UiKit.Upload, "pl.rSend", "Salvar e enviar", () => { var note = SyncDeathAnnounces(); Work(() => SaveAndSend() + note); }, "pl.rSendTip", "Grava os plugins no servidor; o programa relê sozinho.", primary: true);
        UiKit.Sep(top);
        UiKit.Cmd(top, UiKit.Library, "pl.rInstalled", "Plugins instalados", ShowInstalled, "pl.rInstalledTip", "Abre os plugins que este editor tem; escolha um e ponha em execução (dá para pôr o mesmo várias vezes).");
        _runOn = UiKit.Cmd(top, UiKit.Server, "pl.rRunOn", "Ligar programa", () => Work(StartProgram), "pl.rRunOnTip", "Instala (se preciso) e liga o programa dos plugins no servidor local.");
        _runOff = UiKit.Cmd(top, UiKit.Cancel, "pl.rRunOff", "Desligar programa", () => Work(() => { Admin().PluginsStop(); return Loc.T("pl.offDone", "Programa desligado."); }));
        UiKit.Sep(top);
        UiKit.Cmd(top, UiKit.Download, "pl.rImport", "Importar plugin", ImportPlugin, "pl.rImportTip", "Traz plugins de um arquivo .json (exportado aqui ou feito por outra pessoa; docs/PLUGINS.md).");
        UiKit.Cmd(top, UiKit.Export, "pl.rExport", "Exportar plugin", ExportPlugin, "pl.rExportTip", "Grava o plugin escolhido num arquivo .json para usar em outro servidor.");

        // cartão do plugin: título, ações dele, resultado da última ação (verde/vermelho) e quando pode chamar de novo
        _fire.Text = Loc.T("pl.rFire", "Chamar agora"); _stop.Text = Loc.T("pl.rStop", "Encerrar"); _delete.Text = Loc.T("pl.rDelete", "Excluir");
        _fire.Click += (_, _) => Command("fire"); _stop.Click += (_, _) => Command("stop");
        _call.Click += (_, _) => { if (Sel is { } r && CanCall(r)) Command("fire"); else Status("⚠ " + Loc.T("pl.notReady2", "Ainda não dá para chamar: o botão vermelho mostra quanto falta.")); };
        _pauseToggle.Click += (_, _) => Command(_state.GetValueOrDefault(Sel?.Id ?? "", "") == "pausado" ? "resume" : "pause");
        _delete.Click += (_, _) => DeleteSel();
        var tips = new ToolTip();
        tips.SetToolTip(_call, Loc.T("pl.callTip", "Verde: chama o evento agora, sem sorteio. Vermelho: ainda ativo ou esperando o tempo do plugin (mostra quanto falta)."));
        tips.SetToolTip(_fire, Loc.T("pl.rFireTip", "Faz o evento acontecer agora, sem sorteio (também pelo painel do GM)."));
        tips.SetToolTip(_stop, Loc.T("pl.rStopTip", "Termina o evento ativo agora: os monstros somem e sai o aviso de fim."));
        tips.SetToolTip(_delete, Loc.T("pl.rDeleteTip", "Tira o plugin escolhido (vale ao Salvar e enviar)."));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        // liga/desliga o sorteio: interruptor com o nome ao lado (o mesmo da coluna "Ligado" da lista)
        var onBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(14, 16, 0, 0), BackColor = HeadBg };
        _onLabel.ForeColor = HeadFg; _onLabel.BackColor = HeadBg;
        onBox.Controls.Add(_onSwitch); onBox.Controls.Add(_onLabel);
        _onSwitch.Toggled += on => { if (Sel is { } r) SetEnabled(r, on); };
        tips.SetToolTip(_onSwitch, Loc.T("pl.onNote2", "Ligado: sorteia sozinho no tempo marcado. Desligado: só aparece quando chamado (aqui ou no painel do GM)."));
        actions.Controls.AddRange(new Control[] { _stop, _pauseToggle, _delete, _call, onBox });   // chamar agora fica na frente de cada linha da lista
        // cartão da cópia: nome, plugin · ponto, descrição, o que ela está fazendo agora (próximo sorteio, ativo...) e ações
        var titleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = HeadBg };
        titleRow.Controls.Add(_typeIcon); titleRow.Controls.Add(_title);
        _head.Controls.Add(titleRow); _head.Controls.Add(_sub); _head.Controls.Add(_desc); _head.Controls.Add(_now); _head.Controls.Add(actions);
        _tick.Tick += (_, _) => { Countdown(); if (Visible) foreach (ListViewItem it in _list.Items) _list.Invalidate(it.SubItems[CallCol].Bounds); };
        _tick.Start();

        // lista "em execução": ▶ (chamar agora) na frente de cada linha, nome, plugin e descrição
        // o botão Chamar fica na direita da linha (usuário 01/11); a descrição ocupa o que sobra
        _list.Columns.Add(Loc.T("pl.colName2", "Nome"), 160); _list.Columns.Add(Loc.T("pl.colPlugin", "Plugin"), 110); _list.Columns.Add(Loc.T("pl.colOn", "Ligado"), OnW); _list.Columns.Add(Loc.T("pl.colCall", "Chamar"), CallW);
        _list.Resize += (_, _) => _list.Columns[0].Width = Math.Max(100, _list.ClientSize.Width - CallW - OnW - 110 - 2);
        _list.SelectedIndexChanged += (_, _) => Bind();
        _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _list.DrawItem += (_, e) => { };
        _list.DrawSubItem += (_, e) =>
        {
            if (e.ColumnIndex == OnCol)
            {
                e.DrawBackground();
                if (e.Item?.Tag is not PluginRule pr) return;
                DrawSwitch(e.Graphics, e.Bounds, pr.Enabled);
                return;
            }
            if (e.ColumnIndex != CallCol) { e.DrawDefault = true; return; }
            e.DrawBackground();
            if (e.Item?.Tag is not PluginRule r) return;
            var (color, text) = CallLook(r);
            var box = Rectangle.Inflate(e.Bounds, -4, -2);
            using (var b = new SolidBrush(color)) e.Graphics.FillRectangle(b, box);
            var ic = UiKit.Icon(UiKit.Play, Color.White, 14);
            var sz = TextRenderer.MeasureText(text, _list.Font);
            int x = box.X + (box.Width - 14 - 4 - sz.Width) / 2;
            e.Graphics.DrawImage(ic, x, box.Y + (box.Height - 14) / 2);
            TextRenderer.DrawText(e.Graphics, text, _list.Font, new Point(x + 18, box.Y + (box.Height - sz.Height) / 2), Color.White);
        };
        _list.MouseClick += (_, e) =>
        {
            if (_list.HitTest(e.Location) is not { Item.Tag: PluginRule r } h) return;
            int col = h.Item.SubItems.IndexOf(h.SubItem);
            if (col == OnCol) { h.Item.Selected = true; SetEnabled(r, !r.Enabled); return; }
            if (col != CallCol) return;
            h.Item.Selected = true;
            if (CanCall(r)) Command("fire"); else Status("⚠ " + Loc.T("pl.notReady2", "Ainda não dá para chamar: o botão vermelho mostra quanto falta."));
        };
        var hint = new ToolTip();
        _list.MouseMove += (_, e) =>
        {
            var h = _list.HitTest(e.Location);
            int hc = h.Item is null ? -1 : h.Item.SubItems.IndexOf(h.SubItem);
            bool onPlay = hc == CallCol;
            var t = hc == OnCol ? Loc.T("pl.onNote2", "Ligado: sorteia sozinho no tempo marcado. Desligado: só aparece quando chamado (aqui ou no painel do GM).") : !onPlay ? "" : h.Item!.Tag is PluginRule pr && !CanCall(pr) ? Loc.T("pl.notReady2", "Ainda não dá para chamar: o botão vermelho mostra quanto falta.") : Loc.T("pl.rFire", "Chamar agora");
            if (hint.GetToolTip(_list) != t) hint.SetToolTip(_list, t);
            _list.Cursor = onPlay || hc == OnCol ? Cursors.Hand : Cursors.Default;
        };
        BuildForm();
        _empty.Text = Loc.T("pl.empty4", "Nada em execução ainda.\n\nClique em \"Plugins instalados\" (barra de cima), escolha um plugin e \"Pôr em execução\".");

        var left = new Panel { Dock = DockStyle.Left, Width = 520, Padding = new Padding(10, 6, 6, 6), BackColor = Bg };
        // em execução no servidor: as cópias, com ▶ chamar agora em cada linha (o catálogo abre por "Plugins instalados")
        var runTitle = new Label { Dock = DockStyle.Top, Height = 26, Text = Loc.T("pl.running", "Em execução no servidor"), Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Fg, BackColor = Bg, TextAlign = ContentAlignment.BottomLeft };
        left.Controls.Add(_list); left.Controls.Add(runTitle);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 10, 6), BackColor = Bg };
        _cardPanel.Controls.Add(_form); _cardPanel.Controls.Add(_result); _cardPanel.Controls.Add(_head);
        right.Controls.Add(_cardPanel); right.Controls.Add(_empty);
        var logBox = new Panel { Dock = DockStyle.Bottom, Height = 125, Padding = new Padding(10, 0, 10, 8), BackColor = Bg };
        logBox.Controls.Add(_log);
        logBox.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = Loc.T("pl.history", "Histórico do programa (servidor)"), ForeColor = Muted, Font = new Font("Segoe UI Semibold", 9f) });
        Controls.Add(right); Controls.Add(left); Controls.Add(logBox); Controls.Add(_card); Controls.Add(top);
        _poll.Tick += (_, _) => { if (Visible && !_busy) Work(Refresh, quiet: true); };
        VisibleChanged += (_, _) => { _poll.Enabled = Visible; if (Visible) Work(Load, quiet: true); };
        Disposed += (_, _) => { _poll.Dispose(); _tick.Dispose(); };
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

        Field("pl.name2", "Nome (só desta cópia)", _name);

        _form.Controls.Add(Section("pl.s1b", "1. Qual monstro ou NPC aparece"));
        _form.Controls.Add(Note("pl.s1Note", "Clique no monstro no NPCGen ou no Mundo 3D e depois no botão abaixo: o editor cria um evento desligado só daquele ponto. Depois use \"Salvar e enviar ao servidor\" (barra de cima) e reinicie o mapa."));
        var pick = new Button { Text = Loc.T("pl.usePoint", "Usar o ponto escolhido no NPCGen / Mundo 3D"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = UiKit.Accent, ForeColor = Color.White, Height = 32, Padding = new Padding(8, 0, 8, 0) };
        pick.FlatAppearance.BorderColor = UiKit.Accent;
        pick.Click += (_, _) => UsePoint();
        pick.Margin = new Padding(0, 2, 0, 6);
        _form.Controls.Add(pick);
        _point.Margin = new Padding(0, 0, 0, 6);
        _form.Controls.Add(_point);

        _form.Controls.Add(Section("pl.s2", "2. Quando ele aparece"));
        Field("pl.every", "Sortear a cada (minutos)", _every);
        Field("pl.chance", "Chance de aparecer em cada sorteio (%)", Line(_chance, _chanceInfo), "pl.chanceNote2", "Ex.: 10 = em média 1 vez a cada 10 sorteios; 20 = 1 em 5.");
        _chance.ValueChanged += (_, _) => ChanceInfo();
        Field("pl.dur", "Fica no mapa por (minutos)", _dur, "pl.durNote", "Depois disso o evento termina e os monstros que sobraram somem.");
        _respawn.Text = Loc.T("pl.respawn", "Renasce enquanto o evento durar");
        _form.Controls.Add(_respawn);
        _form.Controls.Add(Note("pl.respawnNote", "Ligado: quando morre, volta no tempo de Renascer do ponto até o evento acabar. Desligado: nasce uma vez só por evento. Muda o ponto no NPCGen: salve o npcgen e reinicie o mapa."));
        Field("pl.cool", "Espera mínima entre um evento e outro (segundos)", _cool, "pl.coolNote2", "Vem do Renascer do ponto (+15 s do jogo): nem o sorteio nem o \"Chamar agora\" fazem o evento voltar antes disso.");

        _form.Controls.Add(Section("pl.s3", "3. Avisos para todos os jogadores"));
        _form.Controls.Add(Note("pl.textNote", "Texto vazio = sem aviso."));
        Field("pl.msgStart", "Mensagem ao surgir", _startText);
        Field("pl.msgEnd", "Mensagem ao terminar", _endText);

        _form.Controls.Add(Section("pl.s4", "Detalhes (preenchidos sozinhos)"));
        Field("pl.world", "Mapa (número do gs: 1 = mundo)", _world);
        Field("pl.ctrl", "Evento do ponto (ID no npcgen)", _ctrl, "pl.ctrlNote2", "Preenchido sozinho: o controlador que liga e desliga o ponto.");
        _form.Controls.Add(new Panel { Width = 10, Height = 60, Margin = new Padding(0) });   // sobra embaixo (o padding não entra na rolagem)

        foreach (Control c in new Control[] { _name, _startText, _endText }) c.TextChanged += (_, _) => Edited();
        foreach (var n in new[] { _world, _ctrl, _every, _dur, _chance, _cool }) n.ValueChanged += (_, _) => Edited();
        _respawn.CheckedChanged += (_, _) => { if (!_binding && Sel is { } r) { Edited(); ApplyRespawn(r); } };
    }

    /// <summary>
    /// Põe (renascer desligado + mensagem) ou tira o anúncio de morte de cada plugin na IA do monstro. Roda na tela (mexe no
    /// elements/aipolicy abertos, com Ctrl+Z); vale depois de salvar e enviar o elements e o aipolicy e reiniciar o mapa.
    /// </summary>
    string SyncDeathAnnounces()
    {
        if (DeathSink is null) return "";
        int done = 0; var fails = new List<string>();
        foreach (var r in _rules)
        {
            if (MonsterOf(r) is not { } mid) continue;
            var why = DeathSink(r.Id, mid, !r.Respawn && r.EndText.Length > 0 ? r.EndText : null);
            if (why is null) done++; else fails.Add($"{r.Name}: {why}");
        }
        return (done > 0 ? " · " + Loc.T("pl.deathSynced", "Anúncio de morte atualizado na IA do monstro: salve e envie o elements e o aipolicy (barra principal) e reinicie o mapa.") : "")
            + (fails.Count > 0 ? " · ⚠ " + string.Join("; ", fails) : "");
    }

    // ID do monstro do ponto: "mapa · Nome (61648)"
    static int? MonsterOf(PluginRule r) =>
        System.Text.RegularExpressions.Regex.Match(PointText(r), @"\((\d+)\)\s*$") is { Success: true } m && int.TryParse(m.Groups[1].Value, out var id) ? id : null;

    /// <summary>Leva a opção Renasce para o ponto no npcgen (o gs só renasce pelo bAutoRevive do ponto, PW-KB §S22).</summary>
    void ApplyRespawn(PluginRule r)
    {
        if (r.Controller <= 0 || r.Point.Length == 0 || RespawnSink is null) return;
        var map = r.Point.Split(" · ")[0].Trim();
        var why = RespawnSink(map, r.Controller, r.Respawn);
        Status(why is null ? "✓ " + Loc.T("pl.respawnDone", "Ponto mudado no NPCGen: salve o npcgen (Salvar e enviar) e reinicie o mapa para valer.") : "⚠ " + why);
    }

    static ServerAdmin Admin() => new(LocalServer.Current);
    PluginRule? Sel => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as PluginRule : null;

    // ---------- lista / formulário ----------
    void Fill()
    {
        var keep = Sel?.Id;
        _list.BeginUpdate(); _list.Items.Clear();
        foreach (var r in _rules)
        {
            var it = new ListViewItem(new[] { r.Name.Length > 0 ? r.Name : r.Id, TypeName(r.Type), "", "" }) { Tag = r };
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
        _cardPanel.Visible = r is not null; _empty.Visible = r is null;
        if (r is null) return;
        _title.Text = r.Name.Length > 0 ? r.Name : r.Id;
        _typeIcon.Image = UiKit.Icon(Catalog.FirstOrDefault(c => c.Type == r.Type).Glyph ?? UiKit.Play, HeadBlue, 26);
        _sub.Text = TypeName(r.Type) + " · " + (r.Point.Length > 0 ? PointText(r) : Loc.T("pl.noPoint", "(nenhum ponto ainda)"));
        _desc.Text = r.Description; _desc.Visible = r.Description.Length > 0;
        _now.Text = _state.GetValueOrDefault(r.Id, "") is { Length: > 0 } st ? Loc.T("pl.nowLabel", "Agora: ") + st : _dirty ? Loc.T("pl.notSent", "não enviado") : "";
        ShowResult(r);
        _binding = true;
        _name.Text = r.Name; _onSwitch.SetQuiet(r.Enabled); _onLabel.Text = Loc.T("pl.onHead", "Sorteio") + ": " + OnText(r.Enabled); _respawn.Checked = r.Respawn; _world.Value = Math.Clamp(r.WorldTag, 1, 999); _ctrl.Value = Math.Max(0, r.Controller);
        _every.Value = Math.Clamp(r.EveryMin, 1, 100_000); _dur.Value = Math.Clamp(r.DurationMin, 1, 100_000); _chance.Value = (decimal)Math.Clamp(r.ChancePct, 0, 100);
        ChanceInfo();
        _startText.Text = r.StartText; _endText.Text = r.EndText; _cool.Value = Math.Clamp(r.CooldownSec, 0, 2_592_000);
        _point.Text = r.Point.Length > 0 ? PointText(r) : Loc.T("pl.noPoint", "(nenhum ponto ainda)");
        _point.ForeColor = r.Point.Length > 0 ? Color.FromArgb(22, 128, 61) : Muted;
        _binding = false;
    }

    void Edited()
    {
        if (_binding || Sel is not { } r) return;
        Snapshot();
        r.Name = _name.Text.Trim(); r.Respawn = _respawn.Checked; r.WorldTag = (int)_world.Value; r.Controller = (int)_ctrl.Value;
        r.EveryMin = (int)_every.Value; r.DurationMin = (int)_dur.Value; r.ChancePct = (double)_chance.Value;
        r.StartChannel = r.EndChannel = AnnounceChannel;
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
        if (r.Respawn) ApplyRespawn(r);   // o ponto novo nasce sem renascer; a opção ligada vale para ele também
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

    // resultado da última ação do programa (#last no estado): faixa verde (deu certo) ou vermelha (não deu)
    void ShowResult(PluginRule r)
    {
        var last = _state.GetValueOrDefault(r.Id + "#last", "");
        var p = last.Split('|', 3);
        if (p.Length < 3) { _result.Visible = false; }
        else
        {
            bool ok = p[0] == "ok";
            _result.Visible = true;
            _result.Text = (ok ? "✓  " : "✗  ") + p[1] + "  ·  " + p[2];
            _result.BackColor = ok ? Color.FromArgb(220, 245, 228) : Color.FromArgb(253, 226, 226);
            _result.ForeColor = ok ? Color.FromArgb(22, 101, 52) : Color.FromArgb(185, 28, 28);
        }
        bool paused = _state.GetValueOrDefault(r.Id, "") == "pausado";
        _pauseToggle.Text = paused ? Loc.T("pl.rResume", "Retomar") : Loc.T("pl.rPause", "Pausar");
        _pauseToggle.Glyph = paused ? UiKit.Play : PauseGlyph;
        _pauseToggle.Enabled = r.Enabled;
        Countdown();
    }

    // quando pode chamar de novo (#ready) / se está ativo (#active): conta em vermelho; pronto em verde (atualiza a cada 1 s)
    void Countdown()
    {
        if (Sel is not { } r || !Visible) return;
        bool active = _state.GetValueOrDefault(r.Id + "#active", "0") == "1";
        var left = DateTime.TryParse(_state.GetValueOrDefault(r.Id + "#ready", ""), out var ready) ? ready - DateTime.Now : TimeSpan.Zero;
        if (!_running) { _call.Look(CallGray, Loc.T("pl.cdOff", "Programa desligado"), false); _fire.Enabled = false; _stop.Enabled = false; return; }
        bool can = CanCall(r);
        if (left > TimeSpan.Zero)
            _call.Look(CallRed, string.Format(active ? Loc.T("pl.callActive", "Ativo · {0}") : Loc.T("pl.callWait", "Espera · {0}"),
                left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss")), false);
        else
            _call.Look(can ? CallGreen : CallGray, Loc.T("pl.rFire", "Chamar agora"), can);
        _fire.Enabled = can;
        _stop.Enabled = active;
    }

    void ImportPlugin()
    {
        using var dlg = new OpenFileDialog { Filter = "Plugins (*.json)|*.json", Title = Loc.T("pl.rImport", "Importar plugin") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        List<PluginRule> add;
        try { add = PluginFiles.Parse(File.ReadAllText(dlg.FileName)); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException) { Status("⚠ " + ex.Message); return; }
        if (add.Count == 0) { Status("⚠ " + Loc.T("pl.importNone", "O arquivo não tem plugins.")); return; }
        Snapshot();
        foreach (var r in add)
        {
            var id = r.Id.Length > 0 ? r.Id : "plugin";
            for (int n = 2; _rules.Any(x => x.Id == id); n++) id = $"{r.Id}{n}";
            r.Id = id; _rules.Add(r);
        }
        _dirty = true; Fill();
        Status(string.Format(Loc.T("pl.imported", "{0} plugin(s) importado(s). Confira o ponto de cada um e clique em Salvar e enviar."), add.Count));
    }

    void ExportPlugin()
    {
        if (Sel is not { } r) return;
        using var dlg = new SaveFileDialog { Filter = "Plugins (*.json)|*.json", FileName = (r.Name.Length > 0 ? r.Name : r.Id) + ".json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, PluginFiles.Write(new[] { r }));
        Status(string.Format(Loc.T("pl.exported", "Plugin gravado em {0}."), dlg.FileName));
    }

    /// <summary>Dá para chamar agora? (programa ligado, nada ativo, fora da espera do renascer, sem mudança por enviar)</summary>
    bool CanCall(PluginRule r) => _running && !_dirty && _state.GetValueOrDefault(r.Id + "#active", "0") != "1"
        && !(DateTime.TryParse(_state.GetValueOrDefault(r.Id + "#ready", ""), out var ready) && ready > DateTime.Now);

    /// <summary>Janela com os plugins instalados (catálogo): cada um com o botão de pôr uma cópia em execução.</summary>
    void ShowInstalled()
    {
        using var dlg = new Form { Text = Loc.T("pl.rInstalled", "Plugins instalados"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Bg, Font = Font, ClientSize = new Size(580, 400) };
        var tips = new ToolTip();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 10), BackColor = Bg };
        flow.Controls.Add(new Label { Text = Loc.T("pl.installedHint", "Escolha um plugin e ponha em execução. A cópia vai para a lista \"Em execução no servidor\", onde você dá nome, descrição e regras."),
            ForeColor = Muted, MaximumSize = new Size(520, 0), AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
        foreach (var c in Catalog)
        {
            var tile = new Panel { Width = 520, Height = 92, BackColor = Color.White, Margin = new Padding(0, 0, 0, 8) };
            tile.Controls.Add(new PictureBox { Image = UiKit.Icon(c.Glyph, UiKit.Accent, 30), Size = new Size(36, 36), Location = new Point(12, 14) });
            tile.Controls.Add(new Label { Text = Loc.T(c.Key, c.Pt), Font = new Font("Segoe UI Semibold", 11f), ForeColor = Fg, AutoSize = true, Location = new Point(58, 8) });
            var descLbl = new Label { Text = Loc.T(c.DescKey, c.DescPt), ForeColor = Muted, Location = new Point(58, 32), Size = new Size(290, 54), AutoEllipsis = true };
            tile.Controls.Add(descLbl); tips.SetToolTip(descLbl, descLbl.Text);
            var add = Act(UiKit.Add, true);
            add.Text = Loc.T("pl.addRun", "Pôr em execução"); add.Location = new Point(360, 28);
            var type = c.Type; add.Click += (_, _) => { NewInstance(type); dlg.Close(); };
            tips.SetToolTip(add, Loc.T("pl.addRunTip", "Cria uma cópia deste plugin na lista \"Em execução no servidor\"; dá para pôr várias, cada uma com seu nome e descrição."));
            tile.Controls.Add(add);
            flow.Controls.Add(tile);
        }
        dlg.Controls.Add(flow);
        dlg.ShowDialog(FindForm());
    }

    const int CallW = 96, CallCol = 3, OnW = 96, OnCol = 2;

    /// <summary>Liga/desliga o sorteio da cópia (interruptor da lista e do cartão), com Ctrl+Z; vale ao Salvar e enviar.</summary>
    void SetEnabled(PluginRule r, bool on)
    {
        if (r.Enabled == on) return;
        Snapshot();
        r.Enabled = on; _dirty = true;
        _list.Invalidate();
        if (Sel == r) { _onSwitch.SetQuiet(on); _onLabel.Text = Loc.T("pl.onHead", "Sorteio") + ": " + OnText(on); }
        Status((on ? "✓ " : "") + string.Format(Loc.T("pl.onChanged", "{0}: sorteio {1}. Clique em Salvar e enviar para valer no servidor."), r.Name, OnText(on).ToLowerInvariant()));
    }

    static string OnText(bool on) => on ? Loc.T("pl.on", "Ligado") : Loc.T("pl.off", "Desligado");

    // interruptor desenhado na célula: trilho verde (ligado) ou cinza, bolinha branca, e o nome ao lado
    void DrawSwitch(Graphics g, Rectangle cell, bool on)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var track = new Rectangle(cell.X + 6, cell.Y + (cell.Height - 16) / 2, 30, 16);
        using (var b = new SolidBrush(on ? CallGreen : CallGray))
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            path.AddArc(track.X, track.Y, 16, 16, 90, 180); path.AddArc(track.Right - 16, track.Y, 16, 16, 270, 180); path.CloseFigure();
            g.FillPath(b, path);
        }
        g.FillEllipse(Brushes.White, on ? track.Right - 14 : track.X + 2, track.Y + 2, 12, 12);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        TextRenderer.DrawText(g, OnText(on), _list.Font, new Point(track.Right + 6, cell.Y + (cell.Height - _list.Font.Height) / 2), on ? Color.FromArgb(22, 128, 61) : Muted);
    }

    // pontos gravados por versões antigas tinham o ID duas vezes: "Besouro (61648) (61648)"
    static string PointText(PluginRule r) => System.Text.RegularExpressions.Regex.Replace(r.Point, @"(\(\d+\))(\s*\1)+", "$1");

    /// <summary>Cor e texto do botão Chamar da linha: verde "Chamar"; vermelho com o tempo que falta; cinza desligado.</summary>
    (Color, string) CallLook(PluginRule r)
    {
        if (!_running) return (CallGray, Loc.T("pl.callOff", "Desligado"));
        if (CanCall(r)) return (CallGreen, Loc.T("pl.colCall", "Chamar"));
        var left = DateTime.TryParse(_state.GetValueOrDefault(r.Id + "#ready", ""), out var ready) ? ready - DateTime.Now : TimeSpan.Zero;
        if (left > TimeSpan.Zero) return (CallRed, left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
        return (CallGray, Loc.T("pl.colCall", "Chamar"));   // só falta enviar as mudanças
    }

    void NewInstance(string type)
    {
        Snapshot();
        int n = 1; while (_rules.Any(r => r.Id == $"{type}{n}")) n++;
        var r = new PluginRule { Id = $"{type}{n}", Type = type, Name = $"{TypeName(type)} {n}",
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
        foreach (var r in _rules) r.StartChannel = r.EndChannel = AnnounceChannel;   // regras antigas podiam ter outro canal
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
