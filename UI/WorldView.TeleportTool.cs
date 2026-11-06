using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta Teleportes (teste 30, docs/TELEPORTE-3D.md): os pontos do mapa aberto, ponto novo com o domínio tirado do
/// precinct.clt, quem descobre e quem leva. Ponto grava o par .txt e .sev na hora, com cópia; NPC e serviço ficam no elements
/// aberto, com o Ctrl+Z do editor, até Salvar.
/// </summary>
internal sealed partial class WorldView
{
    const float TeleportPickRadius = 8f;
    readonly FlowLayoutPanel _tpPage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8), Visible = false };
    readonly ListBox _tpList = DarkList(150), _tpDiscover = DarkList(60), _tpLinks = DarkList(80);
    readonly TextBox _tpName = new() { Width = 250, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MaxLength = WorldTargets.MaxNameLength };
    readonly NumericUpDown _tpFee = DarkNumber(100_000_000, 120), _tpLevel = DarkNumber(150, 86);
    readonly Label _tpCount = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Gainsboro, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _tpInfo = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Margin = new Padding(0, 2, 0, 4) };
    readonly Label _tpWarn = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(250, 204, 21), Margin = new Padding(0, 2, 0, 4) };
    readonly Label _tpError = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 4, 0, 0) };
    readonly Label _tpPlaceAt = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Gainsboro, Margin = new Padding(0, 2, 0, 4) };
    FlowLayoutPanel _tpNewCard = null!, _tpWhoCard = null!;
    Button _tpNewBtn = null!, _tpPlaceBtn = null!, _tpConfirmBtn = null!;
    WorldTargets? _tp;
    PrecinctFile? _tpPrecinct;
    List<WorldTarget> _tpPoints = [];
    WorldTarget? _tpSel;
    IReadOnlyList<TeleportLink> _tpLeads = [];
    IReadOnlyList<(int Id, string Name)> _tpFinders = [];
    Vector3? _tpAt;
    bool _tpNew, _tpFilling;
    readonly List<(int Depth, WorldTargets.Snapshot Before, WorldTargets.Snapshot After)> _tpUndo = [], _tpRedo = [];

    static ListBox DarkList(int height) => new() { Width = 250, Height = height, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };

    static NumericUpDown DarkNumber(int max, int width) => new() { Maximum = max, Width = width, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

    string ElementOfMap => Path.GetDirectoryName(Path.GetDirectoryName(_t.Dir))!;

    void InitTeleportPage()
    {
        var points = ToolCard();
        points.Controls.Add(SectionLabel(Loc.T("tp.points", "Pontos deste mapa")));
        _tpList.SelectedIndexChanged += (_, _) => SelectPoint();
        _tpNewBtn = SmallButton("", Loc.T("tp.new", "Novo ponto"), () => { _tpNew = true; _tpError.Text = ""; UpdateTeleportPanel(); }, main: true);
        foreach (var c in new Control[] { _tpList, _tpCount, _tpNewBtn }) points.Controls.Add(c);
        _tpPage.Controls.Add(points);

        _tpNewCard = ToolCard();
        var nameTitle = new Label { Text = Loc.T("tp.name", "Nome no mapa de viagem"), AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 2) };
        _tpName.KeyDown += (_, e) => e.SuppressKeyPress = e.KeyCode == Keys.Enter;
        _tpPlaceBtn = SmallButton("", Loc.T("create.place", "Escolher o lugar no mundo"), PlacePoint);
        _tpConfirmBtn = SmallButton("", Loc.T("tp.confirm", "Criar o ponto"), ConfirmPoint, main: true);
        var cancel = SmallButton("", Loc.T("tp.cancel", "Cancelar"), () => { _placing = null; _tpNew = false; _tpAt = null; _tpName.Text = ""; _tpError.Text = ""; UpdateTeleportPanel(); _gl.Invalidate(); });
        foreach (var c in new Control[] { SectionLabel(Loc.T("tp.newTitle", "Ponto novo")), nameTitle, _tpName, _tpPlaceBtn, _tpPlaceAt, _tpConfirmBtn, cancel }) _tpNewCard.Controls.Add(c);
        _tpPage.Controls.Add(_tpNewCard);

        _tpWhoCard = ToolCard();
        var fees = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        foreach (var (key, pt, box) in new[] { ("tp.fee", "Taxa", _tpFee), ("tp.level", "Nível mínimo", _tpLevel) })
        {
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 2, 12, 2) };
            col.Controls.Add(new Label { Text = Loc.T(key, pt), AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(0, 2, 0, 2) });
            col.Controls.Add(box);
            fees.Controls.Add(col);
        }
        _tpLinks.SelectedIndexChanged += (_, _) => { if (LinkSelected() is { } l) { _tpFee.Value = Math.Min(l.Fee, _tpFee.Maximum); _tpLevel.Value = Math.Min(l.Level, _tpLevel.Maximum); } };
        foreach (var c in new Control[]
        {
            SectionLabel(Loc.T("tp.selected", "Este ponto")), _tpInfo, _tpWarn,
            SectionLabel(Loc.T("tp.finders", "Quem descobre")), _tpDiscover,
            SmallButton("", Loc.T("tp.addFinder", "Escolher NPC que descobre…"), AddFinder),
            SmallButton("", Loc.T("tp.removeFinder", "Tirar o NPC marcado"), RemoveFinder),
            SectionLabel(Loc.T("tp.leads", "Quem leva até aqui")), _tpLinks, fees,
            SmallButton("", Loc.T("tp.addLead", "Escolher NPC que leva…"), AddLead),
            SmallButton("", Loc.T("tp.saveLead", "Aplicar taxa e nível ao marcado"), SaveLead),
            SmallButton("", Loc.T("tp.removeLead", "Tirar o NPC marcado"), RemoveLead),
            SmallButton("", Loc.T("tp.delete", "Excluir este ponto"), DeleteTeleport),
        }) _tpWhoCard.Controls.Add(c);
        _tpPage.Controls.Add(_tpWhoCard);

        var end = ToolCard();
        end.Controls.Add(_tpError);
        end.Controls.Add(SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()));
        end.Controls.Add(SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()));
        _tpPage.Controls.Add(end);
        FitToScroll(_tpPage);
    }

    /// <summary>Abre os pontos do cliente e do servidor; sem o arquivo do cliente a ferramenta avisa e fica vazia.</summary>
    void OpenTeleports()
    {
        try { _tp ??= WorldTargets.Open(ElementOfMap, ServerConfigDir); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("world_targets", ex); _tp = null; }
        _tpPrecinct ??= PrecinctFile.Read(Path.Combine(_t.Dir, "precinct.clt"));
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    void FillPointList()
    {
        _tpPoints = _tp?.Client.Where(t => t.Tag == MapTag).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];
        _tpSel = _tpSel is null ? null : _tpPoints.FirstOrDefault(t => t.Id == _tpSel.Id);
        _tpFilling = true;
        _tpList.BeginUpdate();
        _tpList.Items.Clear();
        foreach (var t in _tpPoints) _tpList.Items.Add($"{t.Name}  ·  {t.Id}");
        _tpList.SelectedIndex = _tpSel is null ? -1 : _tpPoints.IndexOf(_tpSel);
        _tpList.EndUpdate();
        _tpFilling = false;
    }

    void SelectPoint()
    {
        if (_tpFilling || _tpList.SelectedIndex < 0 || _tpList.SelectedIndex >= _tpPoints.Count) return;
        ChoosePoint(_tpPoints[_tpList.SelectedIndex]);
        if (_tpSel is { } t) LookAt(new Vector3(t.X, t.Y, t.Z));
        _gl.Invalidate();
    }

    void ChoosePoint(WorldTarget t) { _tpSel = t; _tpError.Text = ""; UpdateTeleportPanel(); _gl.Invalidate(); }

    void UpdateTeleportPanel()
    {
        FillPointList();
        var (onlyClient, onlyServer) = _tp?.Differences() ?? ([], []);
        _tpCount.Text = _tp is null ? Loc.T("tp.noFile", "Este cliente não tem maps\\world\\world_targets.txt.")
            : !_tp.HasServerFile ? Loc.T("tp.noServer", "Sem world_targets.sev: abra a pasta do servidor para criar pontos. Só dá para ver.")
            : MapTag == 0 ? Loc.T("tp.noTag", "Não sei a tag deste mapa: abra a pasta do servidor.")
            : onlyClient.Count + onlyServer.Count == 0 ? string.Format(Loc.T("tp.count", "{0} pontos neste mapa, {1} no jogo."), _tpPoints.Count, _tp.Client.Count)
            : string.Format(Loc.T("tp.countDiff", "{0} pontos neste mapa. Fora de par: {1} só no cliente, {2} só no servidor."), _tpPoints.Count, onlyClient.Count, onlyServer.Count);
        _tpNewBtn.Visible = !_tpNew && _tp is { HasServerFile: true } && MapTag != 0;
        _tpNewCard.Visible = _tpNew;
        foreach (var c in _tpNewCard.Controls.Cast<Control>()) c.Visible = true;
        _tpPlaceAt.Visible = _tpConfirmBtn.Visible = _tpNew && _tpAt is not null;
        _tpPlaceBtn.Text = "  " + (_placing is not null ? Loc.T("create.placing", "Clique no chão… (cancelar)") : _tpAt is not null ? Loc.T("create.placeAgain", "Trocar o lugar") : Loc.T("create.place", "Escolher o lugar no mundo"));
        if (_tpAt is { } at) _tpPlaceAt.Text = FormattableString.Invariant($"{at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}") + "  ·  " + DomainText(DomainAt(at.X, at.Z));

        bool sel = _tpSel is not null && !_tpNew;
        _tpWhoCard.Visible = sel;
        if (sel && _tpSel is { } t)
        {
            _tpLeads = _host?.TeleportersTo(t.Id) ?? [];
            _tpFinders = _host?.DiscoverersOf(t.Id) ?? [];
            _tpInfo.Text = FormattableString.Invariant($"{t.Name}\nID {t.Id}  ·  {t.X:0.0}, {t.Y:0.0}, {t.Z:0.0}\n") + DomainText(t.Domain);
            var warn = new List<string>();
            if (_tp is { HasServerFile: true } && !_tp.Server.Exists(s => s.Id == t.Id)) warn.Add(Loc.T("tp.warnServer", "O servidor não tem este ponto: o gs leva para (0, 0, 0)."));
            if (_tpFinders.Count == 0) warn.Add(Loc.T("tp.warnFinder", "Ninguém descobre este ponto: o jogador não consegue viajar para cá."));
            if (_tpLeads.Count == 0) warn.Add(Loc.T("tp.warnLead", "Nenhum NPC leva até aqui."));
            _tpWarn.Text = string.Join("\n", warn);
            _tpWarn.Visible = warn.Count > 0;
            _tpDiscover.Items.Clear();
            foreach (var (id, name) in _tpFinders) _tpDiscover.Items.Add($"{name}  ·  {id}");
            _tpLinks.Items.Clear();
            foreach (var l in _tpLeads) _tpLinks.Items.Add(string.Format(Loc.T("tp.linkRow", "{0}  ·  taxa {1:N0}  ·  nível {2}"), l.Name, l.Fee, l.Level));
        }
        if (_tool == Tool.Teleports) LayoutTools();
    }

    int DomainAt(float x, float z) => (_tpPrecinct?.At(x, z)?.Domain ?? 0) is > 0 and var d ? d : -1;

    static string DomainText(int d) => d > 0 ? string.Format(Loc.T("tp.domain", "território {0}"), d) : Loc.T("tp.noDomain", "fora de território");

    void PlacePoint()
    {
        if (_placing is not null) { CancelPlacing(); return; }
        _placing = at => { _placing = null; _tpAt = at; _status.Text = Loc.T("tp.placed", "Lugar marcado. Confira e clique em Criar o ponto."); UpdateTeleportPanel(); _gl.Invalidate(); };
        _status.Text = Loc.T("tp.clickGround", "Clique no chão onde o jogador vai chegar (Esc cancela).");
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

    void ConfirmPoint()
    {
        if (_tp is not { } tp || _tpAt is not { } at) return;
        _tpError.Text = WorldTargets.BadName(_tpName.Text) ?? "";
        if (_tpError.Text.Length > 0) { UpdateTeleportPanel(); return; }
        WorldTarget? made = null;
        if (!ChangePoints(() => made = tp.Add(_tpName.Text, MapTag, at.X, at.Y, at.Z, DomainAt(at.X, at.Z))) || made is null) return;
        _tpNew = false; _tpAt = null; _tpName.Text = ""; _tpSel = made;
        _status.Text = string.Format(Loc.T("tp.created", "Ponto {0} criado nos dois arquivos, ID {1}. Falta quem descobre e quem leva. Ctrl+Z desfaz."), made.Name, made.Id);
        UpdateTeleportPanel();
        _gl.Invalidate();
    }

    void DeleteTeleport()
    {
        if (_tp is not { } tp || _tpSel is not { } t) return;
        if (_tpLeads.Count + _tpFinders.Count > 0) { _tpError.Text = Loc.T("tp.inUse", "Tire antes quem descobre e quem leva: o gs levaria para (0, 0, 0)."); UpdateTeleportPanel(); return; }
        if (MessageBox.Show(this, string.Format(Loc.T("tp.deleteAsk", "Excluir o ponto {0} do cliente e do servidor?"), t.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!ChangePoints(() => tp.Remove(t.Id))) return;
        _tpSel = null;
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

    void Report(string? error, string done)
    {
        _tpError.Text = error ?? "";
        if (error is null) _status.Text = done + " " + Loc.T("tp.saveHint", "Salve o elements.data e envie ao servidor.");
        UpdateTeleportPanel();
    }

    void AddFinder()
    {
        if (_host is null || _tpSel is not { } t || _host.PickCreatureId(this, true) is not { } npc) return;
        int now = _host.DiscoverOf(npc);
        if (now > 0 && now != t.Id && MessageBox.Show(this, string.Format(Loc.T("tp.finderAsk", "Esse NPC já descobre o ponto {0}, e cada NPC descobre um só. Trocar?"), now), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Report(_host.SetDiscover(npc, t.Id), string.Format(Loc.T("tp.finderDone", "{0} agora descobre este ponto."), _host.NameOf(npc)));
    }

    void RemoveFinder()
    {
        if (_host is null || _tpSel is null || _tpDiscover.SelectedIndex < 0 || _tpDiscover.SelectedIndex >= _tpFinders.Count) return;
        var (id, name) = _tpFinders[_tpDiscover.SelectedIndex];
        Report(_host.SetDiscover(id, 0), string.Format(Loc.T("tp.finderOut", "{0} não descobre mais este ponto."), name));
    }

    TeleportLink? LinkSelected() => _tpLinks.SelectedIndex >= 0 && _tpLinks.SelectedIndex < _tpLeads.Count ? _tpLeads[_tpLinks.SelectedIndex] : null;

    /// <summary>Serviço usado por mais NPCs: true = cópia só para este, false = muda para todos, null = desistiu.</summary>
    bool? AskCopy(int npc)
    {
        int users = _host?.ServiceUsers(npc) ?? 0;
        if (users <= 1) return false;
        var r = MessageBox.Show(this, string.Format(Loc.T("tp.sharedAsk", "O serviço de teleporte deste NPC é usado por {0} NPCs.\n\nSim: muda só para este, com uma cópia do serviço.\nNão: muda para todos."), users), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return r == DialogResult.Cancel ? null : r == DialogResult.Yes;
    }

    void WriteLead(int npc)
    {
        if (_host is null || _tpSel is not { } t || AskCopy(npc) is not { } copy) return;
        Report(_host.SetTransmit(npc, t.Id, (int)_tpFee.Value, (int)_tpLevel.Value, copy), string.Format(Loc.T("tp.leadDone", "{0} leva até este ponto."), _host.NameOf(npc)));
    }

    void AddLead() { if (_host?.PickCreatureId(this, true) is { } npc) WriteLead(npc); }

    void SaveLead() { if (LinkSelected() is { } l) WriteLead(l.Npc); }

    void RemoveLead()
    {
        if (_host is null || _tpSel is not { } t || LinkSelected() is not { } l || AskCopy(l.Npc) is not { } copy) return;
        Report(_host.RemoveTransmit(l.Npc, t.Id, copy), string.Format(Loc.T("tp.leadOut", "{0} não leva mais até aqui."), l.Name));
    }

    /// <summary>Clique perto de um pilar escolhe o ponto.</summary>
    bool TeleportMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Teleports || e.Button != MouseButtons.Left || Ray(e.Location) is not { } g) return false;
        var near = _tpPoints.OrderBy(t => (t.X - g.X) * (t.X - g.X) + (t.Z - g.Z) * (t.Z - g.Z)).FirstOrDefault();
        if (near is null || MathF.Abs(near.X - g.X) > TeleportPickRadius || MathF.Abs(near.Z - g.Z) > TeleportPickRadius) return false;
        ChoosePoint(near);
        return true;
    }

    /// <summary>Pilar em cada ponto do mapa, dourado no escolhido.</summary>
    void DrawTeleports()
    {
        if (_tool != Tool.Teleports) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(3);
        foreach (var t in _tpPoints)
        {
            if (t == _tpSel) GL.Color3(1f, 0.8f, 0.25f); else GL.Color3(0.45f, 0.65f, 1f);
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
            DrawLabel(t.Name, new Vector3(t.X, t.Y + 15, t.Z), t == _tpSel ? Color.Gold : Color.LightSkyBlue);
    }

    /// <summary>Pontos que o jogo acha nos dois lados: só esses servem de destino.</summary>
    List<WorldTarget> UsablePoints()
    {
        OpenTeleportsQuiet();
        if (_tp is not { } tp) return [];
        return tp.HasServerFile ? tp.Client.Where(t => tp.Server.Exists(s => s.Id == t.Id)).ToList() : [.. tp.Client];
    }

    void OpenTeleportsQuiet()
    {
        try { _tp ??= WorldTargets.Open(ElementOfMap, ServerConfigDir); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("world_targets", ex); }
    }
}
