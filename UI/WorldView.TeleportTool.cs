using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta Teleportes (teste 30, docs/superpowers/specs/2026-11-06-teleportes-leigos-design.md). Para o usuário um teleporte é uma
/// coisa só: o lugar no mapa de viagem e o NPC que atende ali. O passo a passo monta tudo num rascunho e nada entra nos arquivos
/// até Salvar teleporte: o ponto vai para o par .txt e .sev, e NPC, serviço e Atlas para os dados abertos, com Ctrl+Z.
/// </summary>
internal sealed partial class WorldView
{
    const float TeleportPickRadius = 8f;

    /// <summary>NPC que leva até o ponto, com a taxa e o nível deste destino. Copy: mexer só nele, com cópia do serviço.</summary>
    sealed class Lead { public int Npc; public string Name = ""; public int Fee, Level; public bool Copy; }

    /// <summary>O que o usuário está montando. Point 0 = teleporte novo.</summary>
    sealed class TeleportDraft
    {
        public int Point;
        public Vector3? At;
        public bool NewNpc = true, Atlas = true, HadAtlas;
        public int BaseNpc, Npc, OldNpc;
        public string NpcName = "";
        public List<TeleportStop> Stops = [], OldStops = [];
        public List<Lead> Leads = [];
        public List<int> OldLeads = [];
    }

    readonly FlowLayoutPanel _tpPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly ListBox _tpList = new() { Width = 250, Height = 230, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42 };
    readonly ListBox _tpLeadList = new() { Width = 250, Height = 72, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    readonly TextBox _tpName = new() { Width = 250, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MaxLength = WorldTargets.MaxNameLength };
    readonly TextBox _tpNpcName = new() { Width = 250, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly NumericUpDown _tpFee = DarkNumber(100_000_000, 120), _tpLevel = DarkNumber(150, 86);
    readonly CheckBox _tpAtlas = new() { AutoSize = true, MaximumSize = new Size(230, 0), ForeColor = Color.White, Margin = new Padding(2, 8, 0, 4) };
    readonly Label _tpIntro = Hint(Loc.T("tp.intro", "Um teleporte é um lugar do mapa de viagem com um NPC que atende ali. Ao falar com esse NPC, o jogador descobre o lugar e pode viajar."));
    readonly Label _tpCount = Hint("");
    readonly Label _tpPlaceAt = Hint(""), _tpNpcNow = Hint(""), _tpStopsNow = Hint(""), _tpSummary = Hint("");
    readonly Label _tpError = new() { AutoSize = true, MaximumSize = new Size(226, 0), ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 4, 0, 4) };
    FlowLayoutPanel _tpListCard = null!, _tpStep1 = null!, _tpStep2 = null!, _tpStep3 = null!, _tpStep4 = null!, _tpEnd = null!, _tpNewBox = null!, _tpOldBox = null!;
    Label _tpT1 = null!, _tpT2 = null!, _tpT3 = null!, _tpT4 = null!;
    Button _tpCreateBtn = null!, _tpPlaceBtn = null!, _tpUseNew = null!, _tpUseOld = null!, _tpBaseBtn = null!, _tpSaveBtn = null!, _tpDeleteBtn = null!, _tpSendBtn = null!;
    WorldTargets? _tp;
    PrecinctFile? _tpPrecinct;
    List<WorldTarget> _tpPoints = [];
    readonly Dictionary<int, string> _tpLine = [];
    TeleportDraft? _tpd;
    int _tpSelected;
    bool _tpDirty, _tpFilling, _tpSavedHint;
    readonly List<(int Depth, WorldTargets.Snapshot Before, WorldTargets.Snapshot After)> _tpUndo = [], _tpRedo = [];

    static NumericUpDown DarkNumber(int max, int width) => new() { Maximum = max, Width = width, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = max > 9999 };

    string ElementOfMap => Path.GetDirectoryName(Path.GetDirectoryName(_t.Dir))!;

    void InitTeleportPage()
    {
        _tpListCard = ToolCard();
        _tpCreateBtn = SmallButton("", Loc.T("tp.create", "Criar um teleporte"), NewTeleport, main: true);
        _tpList.DrawItem += DrawPointRow;
        _tpList.SelectedIndexChanged += (_, _) => { if (!_tpFilling && _tpList.SelectedIndex >= 0 && _tpList.SelectedIndex < _tpPoints.Count) OpenPoint(_tpPoints[_tpList.SelectedIndex]); };
        foreach (var c in new Control[] { _tpIntro, _tpCreateBtn, SectionLabel(Loc.T("tp.points", "Teleportes deste mapa")), _tpList, _tpCount }) _tpListCard.Controls.Add(c);

        _tpStep1 = ToolCard();
        _tpT1 = SectionLabel("");
        _tpName.TextChanged += (_, _) => { if (!_tpFilling) { _tpDirty = true; UpdateTeleportPanel(); } };
        _tpName.KeyDown += (_, e) => e.SuppressKeyPress = e.KeyCode == Keys.Enter;
        _tpPlaceBtn = SmallButton("", "", PlacePoint);
        foreach (var c in new Control[] { _tpT1, Field(Loc.T("tp.name", "Nome no mapa de viagem")), _tpName, _tpPlaceBtn, _tpPlaceAt }) _tpStep1.Controls.Add(c);

        _tpStep2 = ToolCard();
        _tpT2 = SectionLabel("");
        _tpUseNew = SmallButton("", Loc.T("tp.useNew", "Criar um NPC novo aqui"), () => UseNpc(true));
        _tpUseOld = SmallButton("", Loc.T("tp.useOld", "Usar um NPC que já existe"), () => UseNpc(false));
        _tpNewBox = SubBox();
        _tpBaseBtn = SmallButton("", Loc.T("tp.pickLook", "Escolher a aparência…"), PickLook);
        _tpNpcName.TextChanged += (_, _) => { if (!_tpFilling && _tpd is { } d) { d.NpcName = _tpNpcName.Text; _tpDirty = true; UpdateTeleportPanel(); } };
        _tpNpcName.KeyDown += (_, e) => e.SuppressKeyPress = e.KeyCode == Keys.Enter;
        foreach (var c in new Control[] { _tpBaseBtn, Field(Loc.T("tp.npcName", "Nome do NPC")), _tpNpcName }) _tpNewBox.Controls.Add(c);
        _tpOldBox = SubBox();
        _tpOldBox.Controls.Add(SmallButton("", Loc.T("tp.clickNpc", "Clicar no NPC no mundo"), () => PickInWorld(ChooseNpc)));
        _tpOldBox.Controls.Add(SmallButton("", Loc.T("tp.searchNpc", "Procurar na lista…"), () => { if (_host?.PickCreatureId(this, true) is { } id) ChooseNpc(id); }));
        foreach (var c in new Control[] { _tpT2, _tpUseNew, _tpUseOld, _tpNewBox, _tpOldBox, _tpNpcNow }) _tpStep2.Controls.Add(c);

        _tpStep3 = ToolCard();
        _tpT3 = SectionLabel("");
        foreach (var c in new Control[] { _tpT3, SmallButton("", Loc.T("tp.pickStops", "Escolher para onde ele leva…"), PickStops), _tpStopsNow }) _tpStep3.Controls.Add(c);

        _tpStep4 = ToolCard();
        _tpT4 = SectionLabel("");
        var fees = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        foreach (var (key, pt, box) in new[] { ("tp.fee", "Taxa", _tpFee), ("tp.level", "Nível mínimo", _tpLevel) })
        {
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 2, 12, 2) };
            col.Controls.Add(new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 2, 0, 2) });
            col.Controls.Add(box);
            fees.Controls.Add(col);
        }
        _tpLeadList.SelectedIndexChanged += (_, _) => { if (SelectedLead() is { } l) { _tpFilling = true; _tpFee.Value = Math.Min(l.Fee, _tpFee.Maximum); _tpLevel.Value = Math.Min(l.Level, _tpLevel.Maximum); _tpFilling = false; } };
        _tpFee.ValueChanged += (_, _) => LeadValues();
        _tpLevel.ValueChanged += (_, _) => LeadValues();
        _tpAtlas.CheckedChanged += (_, _) => { if (!_tpFilling && _tpd is { } d) { d.Atlas = _tpAtlas.Checked; _tpDirty = true; UpdateTeleportPanel(); } };
        foreach (var c in new Control[]
        {
            _tpT4, Hint(Loc.T("tp.leadsHint", "Pode pular. Sem isso, só o NPC deste teleporte aparece ligado a ele.")),
            SmallButton("", Loc.T("tp.clickLead", "Clicar num NPC no mundo"), () => PickInWorld(AddLead)),
            SmallButton("", Loc.T("tp.searchNpc", "Procurar na lista…"), () => { if (_host?.PickCreatureId(this, true) is { } id) AddLead(id); }),
            _tpLeadList, fees,
            SmallButton("", Loc.T("tp.removeLead", "Tirar o NPC marcado"), RemoveLead),
            _tpAtlas,
        }) _tpStep4.Controls.Add(c);

        _tpEnd = ToolCard();
        _tpSaveBtn = SmallButton("", Loc.T("tp.save", "Salvar teleporte"), SaveTeleport, main: true);
        _toolTip.SetToolTip(_tpSaveBtn, "world_targets.txt\nworld_targets.sev\nelements.data\ntasks.data");
        _tpDeleteBtn = SmallButton("", Loc.T("tp.delete", "Excluir este teleporte"), DeleteTeleport);
        var leave = SmallButton("", Loc.T("tp.leave", "Voltar à lista"), () => { if (AskLeaveTeleport()) UpdateTeleportPanel(); });
        foreach (var c in new Control[] { SectionLabel(Loc.T("tp.summary", "Como vai ficar")), _tpSummary, _tpError, _tpSaveBtn, _tpDeleteBtn, leave }) _tpEnd.Controls.Add(c);

        var send = ToolCard();
        _tpSendBtn = SmallButton("", Loc.T("tools.send", "Salvar e enviar ao servidor"), () => SaveAndSendAll?.Invoke(), main: true);
        send.Controls.Add(_tpSendBtn);
        send.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        send.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        foreach (var c in new Control[] { _tpListCard, _tpStep1, _tpStep2, _tpStep3, _tpStep4, _tpEnd, send }) _tpPage.Controls.Add(c);
        FitToScroll(_tpPage);
    }

    static Label Field(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 2) };

    static FlowLayoutPanel SubBox() => new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };

    void DrawPointRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tpPoints.Count) return;
        var t = _tpPoints[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var back = new SolidBrush(sel ? UiKit.Accent : Color.FromArgb(32, 36, 44));
        e.Graphics.FillRectangle(back, e.Bounds);
        using var bold = new Font("Segoe UI Semibold", 9.5f);
        TextRenderer.DrawText(e.Graphics, t.Name, bold, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 3, e.Bounds.Width - 8, 19), Color.White, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, _tpLine.GetValueOrDefault(t.Id, ""), e.Font!, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 21, e.Bounds.Width - 8, 18), sel ? Color.White : Color.Silver, TextFormatFlags.EndEllipsis);
    }

    /// <summary>Abre os pontos do cliente e do servidor; sem o arquivo do cliente a ferramenta avisa e fica vazia.</summary>
    void OpenTeleports()
    {
        OpenTeleportsQuiet();
        _tpPrecinct ??= PrecinctFile.Read(Path.Combine(_t.Dir, "precinct.clt"));
        _tpd = null; _tpDirty = false; _tpSelected = 0;
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    void OpenTeleportsQuiet()
    {
        try { _tp ??= WorldTargets.Open(ElementOfMap, ServerConfigDir); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("world_targets", ex); _tp = null; }
    }

    void FillPointList()
    {
        _tpPoints = _tp?.Client.Where(t => t.Tag == MapTag).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];
        _tpLine.Clear();
        foreach (var t in _tpPoints)
        {
            var who = _host?.DiscoverersOf(t.Id) ?? [];
            _tpLine[t.Id] = who.Count > 0 ? string.Format(Loc.T("tp.rowNpc", "Atende: {0}"), who[0].Name) : Loc.T("tp.rowNoNpc", "Ninguém atende aqui");
        }
        _tpFilling = true;
        _tpList.BeginUpdate();
        _tpList.Items.Clear();
        foreach (var t in _tpPoints) _tpList.Items.Add(t.Name);
        _tpList.SelectedIndex = -1;
        _tpList.EndUpdate();
        _tpFilling = false;
    }

    void NewTeleport()
    {
        _tpd = new TeleportDraft { Atlas = _host?.AtlasName is not null };
        _tpDirty = false; _tpSelected = 0; _tpSavedHint = false; _tpError.Text = "";
        Bind();
        UpdateTeleportPanel();
    }

    /// <summary>Teleporte que já existe: o mesmo passo a passo, com tudo preenchido.</summary>
    void OpenPoint(WorldTarget t)
    {
        if (_tpd is not null && !AskLeaveTeleport()) return;
        var d = new TeleportDraft { Point = t.Id, At = new Vector3(t.X, t.Y, t.Z), NewNpc = false };
        var finders = _host?.DiscoverersOf(t.Id) ?? [];
        if (finders.Count > 0) { d.Npc = d.OldNpc = finders[0].Id; d.NpcName = finders[0].Name; }
        if (d.Npc > 0) d.Stops = [.. _host?.StopsOfNpc(d.Npc) ?? []];
        d.OldStops = [.. d.Stops];
        foreach (var l in _host?.TeleportersTo(t.Id) ?? [])
            if (l.Npc != d.Npc) { d.Leads.Add(new Lead { Npc = l.Npc, Name = l.Name, Fee = l.Fee, Level = l.Level }); d.OldLeads.Add(l.Npc); }
        d.Atlas = d.HadAtlas = _host?.AtlasHas(t.Id) == true;
        _tpd = d; _tpSelected = t.Id; _tpDirty = false; _tpSavedHint = false; _tpError.Text = "";
        Bind();
        _tpFilling = true; _tpName.Text = t.Name; _tpFilling = false;
        LookAt(new Vector3(t.X, t.Y, t.Z));
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    void Bind()
    {
        _tpFilling = true;
        _tpName.Text = ""; _tpNpcName.Text = _tpd?.NpcName ?? "";
        _tpAtlas.Checked = _tpd?.Atlas == true;
        _tpFee.Value = 0; _tpLevel.Value = 0;
        _tpFilling = false;
    }

    bool AskLeaveTeleport()
    {
        if (_tpDirty && MessageBox.Show(this, Loc.T("tp.leaveAsk", "Sair sem salvar? O que foi feito neste teleporte se perde."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        _placing = null; _pickNpc = null; _tpd = null; _tpDirty = false; _tpSelected = 0; _tpError.Text = "";
        _gl.Invalidate();
        return true;
    }

    static string Done(string text, bool ok) => (ok ? "✓  " : "") + text;

    void UpdateTeleportPanel()
    {
        var d = _tpd;
        bool list = d is null;
        _tpListCard.Visible = list;
        if (list)
        {
            FillPointList();
            bool canCreate = _tp is { HasServerFile: true } && MapTag != 0;
            _tpCreateBtn.Visible = canCreate;
            _tpCount.Text = _tp is null ? Loc.T("tp.noFile", "Este cliente não tem maps\\world\\world_targets.txt.")
                : !_tp.HasServerFile ? Loc.T("tp.noServer2", "Para criar teleportes, abra o Mundo 3D com a pasta do servidor configurada. Sem ela, dá só para ver.")
                : MapTag == 0 ? Loc.T("tp.noTag", "Não sei a tag deste mapa: abra a pasta do servidor.")
                : _tpPoints.Count == 0 ? Loc.T("tp.none", "Este mapa ainda não tem teleporte. Clique em Criar um teleporte.")
                : string.Format(Loc.T("tp.count2", "{0} teleportes neste mapa. Clique num deles para ver e mudar."), _tpPoints.Count);
        }
        bool named = d is not null && _tpName.Text.Trim().Length > 0 && WorldTargets.BadName(_tpName.Text) is null;
        bool placed = d?.At is not null;
        bool hasNpc = d is not null && (d.NewNpc ? d.BaseNpc > 0 && d.NpcName.Trim().Length > 0 : d.Npc > 0);
        bool hasStops = d is { Stops.Count: > 0 };
        bool editing = d is { Point: > 0 };
        _tpStep1.Visible = !list;
        _tpStep2.Visible = !list && (editing || named && placed);
        _tpStep3.Visible = _tpStep2.Visible && (editing || hasNpc);
        _tpStep4.Visible = _tpStep3.Visible && (editing || hasStops);
        _tpEnd.Visible = _tpStep4.Visible;
        _tpSendBtn.Visible = _tpSavedHint;
        if (d is not null)
        {
            _tpT1.Text = Done(Loc.T("tp.step1", "1. Onde o jogador chega"), named && placed);
            _tpT2.Text = Done(Loc.T("tp.step2", "2. Quem atende aqui"), hasNpc);
            _tpT3.Text = Done(Loc.T("tp.step3", "3. Para onde esse NPC leva"), hasStops);
            _tpT4.Text = Done(Loc.T("tp.step4", "4. Quem mais leva até aqui"), d.Leads.Count > 0);
            _tpPlaceBtn.Text = "  " + (_placing is not null ? Loc.T("create.placing", "Clique no chão… (cancelar)") : placed ? Loc.T("create.placeAgain", "Trocar o lugar") : Loc.T("create.place", "Escolher o lugar no mundo"));
            _tpPlaceAt.Visible = placed;
            if (d.At is { } at) _tpPlaceAt.Text = FormattableString.Invariant($"{at.X:0}, {at.Y:0}, {at.Z:0}") + "  ·  " + DomainText(DomainAt(at.X, at.Z));
            _tpUseNew.BackColor = d.NewNpc ? UiKit.Accent : Color.FromArgb(40, 45, 54);
            _tpUseOld.BackColor = !d.NewNpc ? UiKit.Accent : Color.FromArgb(40, 45, 54);
            _tpNewBox.Visible = d.NewNpc;
            _tpOldBox.Visible = !d.NewNpc;
            _tpNpcNow.Text = d.NewNpc ? d.BaseNpc > 0 ? string.Format(Loc.T("tp.lookOf", "Aparência de {0}."), _host?.NameOf(d.BaseNpc)) : Loc.T("tp.discoverHint", "Ao falar com ele, o jogador descobre este teleporte.")
                : d.Npc > 0 ? string.Format(Loc.T("tp.npcNow", "Atende aqui: {0}."), _host?.NameOf(d.Npc) ?? d.NpcName) : Loc.T("tp.noNpcYet", "Nenhum NPC escolhido: o jogador não teria como descobrir este lugar.");
            _tpStopsNow.Text = d.Stops.Count == 0 ? Loc.T("tp.noStopsYet", "Nenhum destino escolhido.")
                : string.Join("\n", d.Stops.Take(5).Select(StopLine)) + (d.Stops.Count > 5 ? "\n" + string.Format(Loc.T("tp.more", "e mais {0}"), d.Stops.Count - 5) : "");
            _tpLeadList.Items.Clear();
            foreach (var l in d.Leads) _tpLeadList.Items.Add(LeadLine(l));
            _tpAtlas.Visible = _host?.AtlasName is not null;
            _tpAtlas.Text = string.Format(Loc.T("tp.atlas", "O {0} também descobre este teleporte"), _host?.AtlasName);
            _tpSummary.Text = Summary(d);
            _tpSaveBtn.Text = "  " + (editing ? Loc.T("tp.saveChanges", "Salvar as mudanças") : Loc.T("tp.save", "Salvar teleporte"));
            _tpSaveBtn.Enabled = !editing || _tpDirty;
            _tpDeleteBtn.Visible = editing;
        }
        if (_tool == Tool.Teleports) LayoutTools();
    }

    string StopLine(TeleportStop s) => string.Format(Loc.T("tp.stopRow", "{0}: {1:N0} moedas, nível {2}"), PointName(s.Point), s.Fee, s.Level);

    static string LeadLine(Lead l) => string.Format(Loc.T("tp.linkRow", "{0}  ·  taxa {1:N0}  ·  nível {2}"), l.Name, l.Fee, l.Level);

    string PointName(int id) => _tp?.Client.Find(t => t.Id == id)?.Name ?? id.ToString();

    string Summary(TeleportDraft d)
    {
        var lines = new List<string> { string.Format(Loc.T("tp.sumPlace", "\"{0}\" aparece no mapa de viagem."), _tpName.Text.Trim()) };
        var who = d.NewNpc ? d.NpcName.Trim() : _host?.NameOf(d.Npc) ?? d.NpcName;
        lines.Add(d.NewNpc ? string.Format(Loc.T("tp.sumNew", "{0} é criado aqui. Falar com ele descobre o lugar."), who)
            : who.Length > 0 ? string.Format(Loc.T("tp.sumOld", "Falar com {0} descobre o lugar."), who) : Loc.T("tp.sumNobody", "Ninguém descobre o lugar: o jogador não consegue viajar até aqui."));
        lines.Add(string.Format(Loc.T("tp.sumStops", "Ele leva a {0} lugares."), d.Stops.Count));
        if (d.Leads.Count > 0) lines.Add(string.Format(Loc.T("tp.sumLeads", "Mais {0} NPCs levam até aqui."), d.Leads.Count));
        return string.Join("\n", lines);
    }

    /// <summary>Territórios usados pelas áreas deste mapa, para avisar antes de excluir um.</summary>
    IReadOnlySet<int> _tpPrecinctDomains()
    {
        _tpPrecinct ??= PrecinctFile.Read(Path.Combine(_t.Dir, "precinct.clt"));
        return _tpPrecinct?.Areas.Select(a => a.Domain).Where(d => d > 0).ToHashSet() ?? [];
    }

    int DomainAt(float x, float z) => (_tpPrecinct is { } p ? p.At(x, z, p.MainInstance)?.Domain ?? 0 : 0) is > 0 and var d ? d : -1;

    static string DomainText(int d) => d > 0 ? string.Format(Loc.T("tp.domain", "território {0}"), d) : Loc.T("tp.noDomain", "fora de território");

    void PlacePoint()
    {
        if (_placing is not null) { CancelPlacing(); return; }
        _placing = at => { _placing = null; if (_tpd is { } d) { d.At = at; _tpDirty = true; } _status.Text = ""; UpdateTeleportPanel(); _gl.Invalidate(); };
        _status.Text = Loc.T("tp.clickGround", "Clique no chão onde o jogador vai chegar (Esc cancela).");
        UpdateTeleportPanel();
    }

    void UseNpc(bool isNew)
    {
        if (_tpd is not { } d) return;
        d.NewNpc = isNew; _tpDirty = true;
        UpdateTeleportPanel();
    }

    void PickLook()
    {
        if (_tpd is not { } d || _host?.PickCreatureId(this, true) is not { } id) return;
        d.BaseNpc = id; _tpDirty = true;
        if (d.NpcName.Trim().Length == 0) { d.NpcName = _host.NameOf(id) ?? ""; _tpFilling = true; _tpNpcName.Text = d.NpcName; _tpFilling = false; }
        UpdateTeleportPanel();
    }

    /// <summary>Espera um clique num NPC do mundo; o cursor verde e o Esc são os da escolha de NPC das quests.</summary>
    void PickInWorld(Action<int> done)
    {
        _pickNpc = done; _pickArea = null; _pickMonster = null; _pickMine = null;
        _status.Text = Loc.T("qe.picking", "Clique no NPC no mundo… (Esc cancela)");
        _gl.Focus();
    }

    void ChooseNpc(int id)
    {
        if (_host is null || _tpd is not { } d) return;
        int now = _host.DiscoverOf(id);
        if (now > 0 && now != d.Point && MessageBox.Show(this, string.Format(Loc.T("tp.finderAsk2", "Esse NPC já atende em {0}, e cada NPC atende um lugar só. Trocar para este?"), PointName(now)), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        d.Npc = id; d.NpcName = _host.NameOf(id) ?? ""; _tpDirty = true;
        d.Stops = [.. _host.StopsOfNpc(id)];
        _status.Text = "";
        UpdateTeleportPanel();
    }

    void PickStops()
    {
        if (_tpd is not { } d) return;
        var points = UsablePoints().Where(t => t.Id != d.Point).ToList();
        if (points.Count == 0) { _tpError.Text = Loc.T("tp.noPoints", "Este cliente não tem pontos de teleporte para escolher."); return; }
        using var w = new TeleportStopsWindow(points, d.Stops, 0, showDiscover: false);
        if (w.ShowDialog(this) != DialogResult.OK) return;
        d.Stops = w.Stops; _tpDirty = true;
        UpdateTeleportPanel();
    }

    Lead? SelectedLead() => _tpd is { } d && _tpLeadList.SelectedIndex >= 0 && _tpLeadList.SelectedIndex < d.Leads.Count ? d.Leads[_tpLeadList.SelectedIndex] : null;

    void LeadValues()
    {
        if (_tpFilling || SelectedLead() is not { } l) return;
        l.Fee = (int)_tpFee.Value; l.Level = (int)_tpLevel.Value; _tpDirty = true;
        _tpLeadList.Items[_tpLeadList.SelectedIndex] = LeadLine(l);
    }

    /// <summary>Serviço usado por mais NPCs: true = cópia só para este, false = muda para todos, null = desistiu.</summary>
    bool? AskCopy(int npc)
    {
        int users = _host?.ServiceUsers(npc) ?? 0;
        if (users <= 1) return false;
        var r = MessageBox.Show(this, string.Format(Loc.T("tp.sharedAsk", "O serviço de teleporte deste NPC é usado por {0} NPCs.\n\nSim: muda só para este, com uma cópia do serviço.\nNão: muda para todos."), users), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r == DialogResult.Cancel ? null : r == DialogResult.Yes;
    }

    void AddLead(int npc)
    {
        if (_host is null || _tpd is not { } d) return;
        _status.Text = "";
        if (npc == d.Npc || d.Leads.Exists(l => l.Npc == npc)) return;
        if (AskCopy(npc) is not { } copy) return;
        d.Leads.Add(new Lead { Npc = npc, Name = _host.NameOf(npc) ?? npc.ToString(), Fee = (int)_tpFee.Value, Level = (int)_tpLevel.Value, Copy = copy });
        _tpDirty = true;
        UpdateTeleportPanel();
        _tpLeadList.SelectedIndex = d.Leads.Count - 1;
    }

    void RemoveLead()
    {
        if (SelectedLead() is not { } l || _tpd is not { } d) return;
        d.Leads.Remove(l); _tpDirty = true;
        UpdateTeleportPanel();
    }

    /// <summary>Muda os pontos, grava o par e guarda o passo do Ctrl+Z. Falha ao gravar volta como estava.</summary>
    bool ChangePoints(Action change)
    {
        if (_tp is not { } tp) return false;
        var before = tp.Take();
        try
        {
            change();
            tp.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            tp.Restore(before);
            _tpError.Text = ex.Message;
            AppLog.Error("world_targets", ex);
            UpdateTeleportPanel();
            return false;
        }
        _tpUndo.Add((UndoDepth?.Invoke() ?? 0, before, tp.Take()));
        _tpRedo.Clear();
        return true;
    }

    string? TeleportProblem(TeleportDraft d)
    {
        if (WorldTargets.BadName(_tpName.Text) is { } bad) return bad;
        if (d.At is null) return Loc.T("tp.needPlace", "Escolha o lugar no mundo.");
        if (d.NewNpc && d.BaseNpc <= 0) return Loc.T("tp.needLook", "Escolha a aparência do NPC.");
        if (d.NewNpc && d.NpcName.Trim().Length == 0) return Loc.T("nb.noName", "Dê um nome para o NPC.");
        if (!d.NewNpc && d.Npc <= 0 && d.Point == 0) return Loc.T("tp.needNpc", "Escolha quem atende aqui.");
        if (d.Stops.Count == 0 && (d.Npc > 0 || d.NewNpc)) return Loc.T("nb.noStops", "Escolha pelo menos um destino: sem destino o gs cai ao iniciar.");
        return null;
    }

    void SaveTeleport()
    {
        if (_host is null || _tp is not { } tp || _tpd is not { } d) return;
        _tpError.Text = TeleportProblem(d) ?? "";
        if (_tpError.Text.Length > 0) { UpdateTeleportPanel(); return; }
        bool? stopsCopy = false;
        bool stopsChanged = !d.NewNpc && !(d.Npc == d.OldNpc && d.Stops.SequenceEqual(d.OldStops));
        if (stopsChanged && d.Npc > 0 && (stopsCopy = AskCopy(d.Npc)) is null) return;
        var at = d.At!.Value;
        string name = _tpName.Text.Trim();
        var was = d.Point > 0 ? tp.Client.Find(t => t.Id == d.Point) : null;
        WorldTarget? point = null;
        if (!ChangePoints(() => point = was is null ? tp.Add(name, MapTag, at.X, at.Y, at.Z, DomainAt(at.X, at.Z))
                : tp.Update(was with { Name = name, X = at.X, Y = at.Y, Z = at.Z, Domain = DomainAt(at.X, at.Z) })) || point is null) return;
        string? error = null;
        int npc = d.Npc;
        if (d.NewNpc)
        {
            var nd = new NpcDraft { Kind = NpcKind.Teleporter, BaseId = d.BaseNpc, Name = d.NpcName, Stops = d.Stops, Discover = point.Id };
            var bad = _host.ValidateNpc(nd);
            if (bad.Count > 0) error = bad[0];
            else if (_host.BuildNpc(nd) is { } made)
            {
                npc = made;
                PlaceCreature?.Invoke(new Vector3(at.X + 3, at.Y, at.Z), made, true);   // ao lado de onde o jogador chega
                RefreshMarkers();
            }
            else error = Loc.T("create.failed", "Não foi possível criar: confira o elements.data aberto.");
        }
        else if (npc > 0)
        {
            if (d.OldNpc > 0 && d.OldNpc != npc) error = _host.SetDiscover(d.OldNpc, 0);
            error ??= _host.SetDiscover(npc, point.Id);
            if (stopsChanged) error ??= _host.SetStops(npc, d.Stops, stopsCopy == true);
        }
        foreach (int gone in d.OldLeads.Where(o => !d.Leads.Exists(l => l.Npc == o))) error ??= _host.RemoveTransmit(gone, point.Id, false);
        foreach (var l in d.Leads) error ??= _host.SetTransmit(l.Npc, point.Id, l.Fee, l.Level, l.Copy);
        if (d.Atlas != d.HadAtlas) error ??= _host.SetAtlas(point, d.Atlas);
        _tpError.Text = error ?? "";
        if (error is not null) { UpdateTeleportPanel(); return; }
        _tpd = null; _tpDirty = false; _tpSavedHint = true;
        _status.Text = string.Format(Loc.T("tp.saved", "Teleporte {0} salvo. Clique em Salvar e enviar ao servidor para ver no jogo. Ctrl+Z desfaz."), point.Name);
        ServerNotice.Notify("world_targets.sev");
        FlashSaveProgress(Loc.T("world.savedBar", "Salvo"), true);   // barra azul cheia por um instante (todo salvar do Mundo 3D)
        SavedDialog.Show(this, string.Format(Loc.T("tp.savedShort", "Teleporte {0} salvo."), point.Name), SaveAndSendAll);
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    /// <summary>Tira o ponto do par e de tudo que aponta para ele: sem isso o gs levaria para (0, 0, 0).</summary>
    /// <summary>Tira o ponto do par, dos destinos dos NPCs, de quem descobre e do Atlas.</summary>
    bool RemovePoint(int point, bool atlas)
    {
        if (_host is null || _tp is not { } tp) return false;
        var leads = _host.TeleportersTo(point);
        var finders = _host.DiscoverersOf(point);
        if (!ChangePoints(() => tp.Remove(point))) return false;
        foreach (var l in leads) _host.RemoveTransmit(l.Npc, point, false);
        foreach (var (id, _) in finders) _host.SetDiscover(id, 0);
        if (atlas) _host.SetAtlas(new WorldTarget(point, "", MapTag, 0, 0, 0, -1), false);
        return true;
    }

    void DeleteTeleport()
    {
        if (_host is null || _tp is null || _tpd is not { Point: > 0 } d) return;
        var leads = _host.TeleportersTo(d.Point);
        if (MessageBox.Show(this, string.Format(Loc.T("tp.deleteAsk2", "Excluir {0}? Ele sai do mapa de viagem, e {1} NPCs deixam de levar até aqui. Os NPCs continuam no mundo."), _tpName.Text, leads.Count), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!RemovePoint(d.Point, d.HadAtlas)) return;
        _tpd = null; _tpDirty = false; _tpSavedHint = true;
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    bool TeleportUndo(bool redo)
    {
        var from = redo ? _tpRedo : _tpUndo; var to = redo ? _tpUndo : _tpRedo;
        if (_tp is not { } tp || from.Count == 0 || from[^1].Depth != ((redo ? RedoDepth : UndoDepth)?.Invoke() ?? 0)) return false;
        var step = from[^1];
        var back = tp.Take();
        tp.Restore(redo ? step.After : step.Before);
        try { tp.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tp.Restore(back); _tpError.Text = ex.Message; AppLog.Error("world_targets", ex); return true; }
        from.RemoveAt(from.Count - 1);
        to.Add(step with { Depth = (redo ? UndoDepth : RedoDepth)?.Invoke() ?? 0 });
        _status.Text = redo ? Loc.T("tp.redone", "Ponto refeito nos dois arquivos.") : Loc.T("tp.undone", "Ponto desfeito nos dois arquivos (Ctrl+Y refaz).");
        UpdateTeleportPanel();
        _gl.Invalidate();
        return true;
    }

    /// <summary>Clique perto de um pilar, fora de qualquer NPC, abre o teleporte. Clique em NPC continua escolhendo e movendo ele.</summary>
    bool TeleportClick(Point at)
    {
        if (_tool != Tool.Teleports || _tpd is not null || Ray(at) is not { } g) return false;
        var near = _tpPoints.OrderBy(t => (t.X - g.X) * (t.X - g.X) + (t.Z - g.Z) * (t.Z - g.Z)).FirstOrDefault();
        if (near is null || MathF.Abs(near.X - g.X) > TeleportPickRadius || MathF.Abs(near.Z - g.Z) > TeleportPickRadius) return false;
        OpenPoint(near);
        return true;
    }

    /// <summary>Pilar em cada ponto do mapa, dourado no que está aberto.</summary>
    void DrawTeleports()
    {
        if (_tool != Tool.Teleports) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(3);
        foreach (var t in _tpPoints)
        {
            if (t.Id == _tpSelected) GL.Color3(1f, 0.8f, 0.25f); else GL.Color3(0.45f, 0.65f, 1f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 28; i++) { float a = i / 28f * MathF.Tau; GL.Vertex3(t.X + MathF.Cos(a) * 3f, t.Y + 0.2f, -(t.Z + MathF.Sin(a) * 3f)); }
            GL.End();
            GL.Begin(PrimitiveType.Lines); GL.Vertex3(t.X, t.Y, -t.Z); GL.Vertex3(t.X, t.Y + 14, -t.Z); GL.End();
        }
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }

    void DrawTeleportLabels()
    {
        if (_tool != Tool.Teleports) return;
        foreach (var t in _tpPoints.OrderBy(t => (Gl(t.X, t.Y, t.Z) - _cam).LengthSquared).Take(30))
            DrawLabel(t.Name, new Vector3(t.X, t.Y + 15, t.Z), t.Id == _tpSelected ? Color.Gold : Color.LightSkyBlue);
    }

    /// <summary>Pontos que o jogo acha nos dois lados: só esses servem de destino.</summary>
    List<WorldTarget> UsablePoints()
    {
        OpenTeleportsQuiet();
        if (_tp is not { } tp) return [];
        return tp.HasServerFile ? tp.Client.Where(t => tp.Server.Exists(s => s.Id == t.Id)).ToList() : [.. tp.Client];
    }
}
