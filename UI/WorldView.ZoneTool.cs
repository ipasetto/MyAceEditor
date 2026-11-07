using System.Drawing.Drawing2D;
using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Area = MyAceEditor.PerfectWorld.PrecinctFile.Area;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta Zonas: o mapa cobre a cena e o painel toma o lugar do menu (spec 2026-11-06-editor-de-zonas-design.md). Tudo muda numa
/// cópia (_zw); nada grava antes de Salvar zona. Ver no chão mostra a zona aberta sobre o terreno.
/// </summary>
internal sealed partial class WorldView
{
    enum ZoneDraw { None, NewZone, Redo, NewPart }

    readonly MapCanvas _zCanvas = new() { Visible = false };
    readonly Label _zBar = new() { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.Gainsboro, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), AutoEllipsis = true };
    readonly FlowLayoutPanel _zonePage = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(9, 10, 0, 16) };
    readonly Label _zState = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.White, Margin = new Padding(0, 2, 0, 4) };
    readonly TextBox _zSearch = new() { BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "" };
    readonly ListBox _zList = new() { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40, Height = 300, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly ComboBox _zInstances = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly TextBox _zName = new() { BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MaxLength = ZoneRules.MaxName };
    readonly CheckBox _zNameAll = new() { AutoSize = true, ForeColor = Color.Gainsboro }, _zRulesAll = new() { AutoSize = true, ForeColor = Color.Gainsboro }, _zMusicAll = new() { AutoSize = true, ForeColor = Color.Gainsboro }, _zPk = new() { AutoSize = true, ForeColor = Color.White };
    readonly Label _zShape = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Silver }, _zTouch = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Silver, Margin = new Padding(0, 4, 0, 2) },
        _zMusicText = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Gainsboro }, _zCity = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Gainsboro },
        _zSummary = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.Gainsboro }, _zWhy = new() { AutoSize = true, MaximumSize = new Size(225, 0), ForeColor = Color.FromArgb(248, 113, 113), Margin = new Padding(0, 4, 0, 4) };
    readonly ComboBox _zLoop = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat },
        _zCities = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat },
        _zDomain = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly ListBox _zMarks = new() { Height = 70, BackColor = Color.FromArgb(32, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.None };
    readonly FlowLayoutPanel _zParts = new() { AutoSize = true, MaximumSize = new Size(228, 0), WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
    FlowLayoutPanel _zFoot = null!, _zHead = null!, _zListCard = null!, _zNameCard = null!, _zAreaCard = null!, _zMusicCard = null!, _zRulesCard = null!, _zMarksCard = null!, _zEndCard = null!;
    Label _zStepName = null!, _zStepArea = null!, _zStepMusic = null!, _zStepRules = null!, _zStepMarks = null!;
    Button _zFix = null!, _zSave = null!, _zDelete = null!, _zAbove = null!, _zUnder = null!, _zBackList = null!, _zGroundBtn = null!, _zNewBtn = null!;
    readonly Button _zBackMap = new() { Visible = false, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(32, 36, 44), AutoSize = true, Location = new Point(14, 14), Cursor = Cursors.Hand };

    ZonePair? _zones;
    PrecinctFile _zw = new();
    int _zInstance = 1, _zOpen = -1, _zDrag = -1, _zBadCorner = -1, _zCycle;
    bool _zDirty, _zFilling, _zGround, _zHatch, _zMoved;
    HashSet<string> _zClean = [];
    Dictionary<int, string> _zDomains = [];
    ZoneDraw _zDrawing;
    List<(float X, float Y, float Z)> _zPoints = [];
    Action<float, float>? _zPick;
    PointF _zLastClick = new(float.NaN, 0);
    readonly List<(byte[] File, int Open)> _zUndo = [], _zRedo = [];
    readonly List<byte[]> _zSavedUndo = [], _zSavedRedo = [];

    // ---------- montagem ----------
    void InitZoneTool()
    {
        Controls.Add(_zCanvas); Controls.Add(_zBackMap);
        _zCanvas.Controls.Add(_zBar);
        _gl.Resize += (_, _) => _zCanvas.Bounds = _gl.Bounds;
        _zCanvas.PaintOver += PaintZones;
        _zCanvas.BottomSpace = _zBar.Height;
        _zCanvas.Legend = [(Color.FromArgb(109, 183, 255), Loc.T("zone.legOpen", "Zona aberta")), (Color.FromArgb(0, 255, 252), Loc.T("zone.protected", "novatos protegidos")),
            (Color.FromArgb(216, 222, 232), Loc.T("zone.legOther", "Outras zonas")), (Color.FromArgb(255, 200, 90), Loc.T("zone.legCity", "Onde renasce")), (Color.Gold, Loc.T("zone.legMark", "Lugar do minimapa"))];
        _zCanvas.HitSomething = p => _zDrawing != ZoneDraw.None || _zPick is not null || ZoneCorner(p) >= 0;
        _zCanvas.MapDown += ZoneDown;
        _zCanvas.MapMove += ZoneMove;
        _zCanvas.MapUp += (_, _) => { _zDrag = -1; _zMoved = false; };
        _zCanvas.MapDoubleClick += ZoneDouble;
        _zCanvas.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _zDrawing != ZoneDraw.None) FinishDrawing();
            if (e.KeyCode == Keys.Escape) { _zDrawing = ZoneDraw.None; _zPick = null; _zPoints = []; UpdateZonePanel(); _zCanvas.Invalidate(); }
        };
        _zBackMap.Click += (_, _) => ShowZoneGround(false);
        FormClosing += (_, e) => { if (_tool == Tool.Zones && !ZoneLeave()) e.Cancel = true; };

        _zHead = ToolCard();
        _zFix = SmallButton("", "", FixPair);
        foreach (var c in new Control[] { _zState, _zFix }) _zHead.Controls.Add(c);
        _zonePage.Controls.Add(_zHead);

        _zListCard = ToolCard();
        _zNewBtn = SmallButton("", Loc.T("zone.new", "Nova zona"), StartNewZone, main: true);
        _zSearch.TextChanged += (_, _) => FillZoneList();
        _zSearch.PlaceholderText = Loc.T("zone.search", "Procurar zona…");
        _zList.DrawItem += DrawZoneRow;
        _zList.SelectedIndexChanged += (_, _) => { if (!_zFilling && _zList.SelectedItem is int i) { OpenZone(i); FrameZone(); } };
        _zInstances.SelectedIndexChanged += (_, _) => { if (_zFilling || _zones is null || _zInstances.SelectedIndex < 0) return; _zInstance = _zones.Instances[_zInstances.SelectedIndex]; _zOpen = -1; UpdateZonePanel(); _zCanvas.Invalidate(); };
        foreach (var c in new Control[] { _zNewBtn, _zInstances, _zSearch, _zList, Hint(Loc.T("zone.hint", "Clique numa zona no mapa ou aqui. A roda do mouse aproxima, arrastar move o mapa.")) }) _zListCard.Controls.Add(c);
        _zonePage.Controls.Add(_zListCard);

        _zBackList = SmallButton("", Loc.T("zone.backList", "Voltar à lista"), () => { _zOpen = -1; _zDrawing = ZoneDraw.None; UpdateZonePanel(); _zCanvas.Invalidate(); });
        _zHead.Controls.Add(_zBackList);

        _zNameCard = ToolCard();
        _zStepName = SectionLabel("");
        _zName.KeyPress += (_, e) => { if (e.KeyChar is '"' or '\r' or '\n') e.Handled = true; };
        _zName.TextChanged += (_, _) => { if (!_zFilling) ZoneChange(() => ZoneEdit.Rename(_zw, _zOpen, _zName.Text.Replace("\"", "").Replace("\n", "").Replace("\r", ""), _zNameAll.Checked)); };
        foreach (var c in new Control[] { _zStepName, _zName, _zNameAll }) _zNameCard.Controls.Add(c);
        _zonePage.Controls.Add(_zNameCard);

        _zAreaCard = ToolCard();
        _zStepArea = SectionLabel("");
        _zAbove = SmallButton("", Loc.T("zone.onTop", "Fica por cima"), () => ZoneChange(() => _zOpen = ZoneEdit.OnTop(_zw, _zOpen)));
        _zUnder = SmallButton("", Loc.T("zone.below", "Fica por baixo"), () => ZoneChange(() => ZoneEdit.Below(_zw, _zOpen, ZoneEdit.Neighbors(_zw, _zOpen))));
        foreach (var c in new Control[] { _zStepArea, _zShape, _zParts,
            SmallButton("", Loc.T("zone.redraw", "Desenhar de novo"), () => StartDrawing(ZoneDraw.Redo)),
            SmallButton("", Loc.T("zone.morePart", "Mais uma parte"), () => StartDrawing(ZoneDraw.NewPart)), _zTouch, _zAbove, _zUnder }) _zAreaCard.Controls.Add(c);
        _zonePage.Controls.Add(_zAreaCard);

        _zMusicCard = ToolCard();
        _zStepMusic = SectionLabel("");
        _zLoop.Items.AddRange([Loc.T("zone.loopNever", "Repetir: nunca"), Loc.T("zone.loopAlways", "Repetir: sempre"), Loc.T("zone.loopSkip", "Repetir: sem a primeira")]);
        _zLoop.SelectedIndexChanged += (_, _) => { if (!_zFilling && _zLoop.SelectedIndex >= 0) ZoneSet(a => a with { Loop = _zLoop.SelectedIndex }, _zMusicAll); };
        foreach (var c in new Control[] { _zStepMusic, _zMusicText,
            SmallButton("", Loc.T("zone.pickMusic", "Escolher a música…"), PickZoneMusic), _zLoop,
            SmallButton("", Loc.T("zone.pickDay", "Som de dia…"), () => PickZoneSound(night: false)),
            SmallButton("", Loc.T("zone.pickNight", "Som de noite…"), () => PickZoneSound(night: true)), _zMusicAll }) _zMusicCard.Controls.Add(c);
        _zonePage.Controls.Add(_zMusicCard);

        _zRulesCard = ToolCard();
        _zStepRules = SectionLabel("");
        _zCities.SelectedIndexChanged += (_, _) =>
        {
            if (_zFilling || _zCities.SelectedItem is not ZoneCity c) return;
            ZoneSet(a => a with { City = c.At }, _zRulesAll);
        };
        _zDomain.SelectedIndexChanged += (_, _) => { if (!_zFilling && _zDomain.SelectedItem is ZoneDomain d) ZoneSet(a => a with { Domain = d.Id }, _zRulesAll); };
        _zPk.CheckedChanged += (_, _) => { if (!_zFilling) ZoneSet(a => a with { PkProtected = _zPk.Checked }, _zRulesAll); };
        foreach (var c in new Control[] { _zStepRules, _zCity, _zCities,
            SmallButton("", Loc.T("zone.cityMap", "Marcar no mapa onde renasce"), () => PickOnMap(Loc.T("zone.clickCity", "Clique no mapa onde o jogador renasce."), (x, z) => ZoneSet(a => a with { City = (x, GroundAt(x, z), z) }, _zRulesAll))),
            _zDomain, _zPk, _zRulesAll }) _zRulesCard.Controls.Add(c);
        _zonePage.Controls.Add(_zRulesCard);

        _zMarksCard = ToolCard();
        _zStepMarks = SectionLabel("");
        foreach (var c in new Control[] { _zStepMarks, _zMarks,
            SmallButton("", Loc.T("zone.addMark", "Marcar um lugar"), AddZoneMark),
            SmallButton("", Loc.T("zone.dropMark", "Tirar o marcado"), DropZoneMark) }) _zMarksCard.Controls.Add(c);
        _zonePage.Controls.Add(_zMarksCard);

        // Como vai ficar e Salvar zona ficam presos embaixo do painel: à vista mesmo com os passos rolando
        _zFoot = ToolCard();
        _zFoot.Dock = DockStyle.Bottom;
        _zFoot.Visible = false;
        _zSave = SmallButton("\uE74E", Loc.T("zone.save", "Salvar zona"), SaveZones, main: true);
        foreach (var c in new Control[] { SectionLabel(Loc.T("zone.summary", "Como vai ficar")), _zSummary, _zWhy, _zSave }) _zFoot.Controls.Add(c);
        FitToScroll(_zFoot);
        _zSave.Width = 250;
        _zSummary.MaximumSize = _zWhy.MaximumSize = new Size(250, 0);
        _zEndCard = ToolCard();
        _zDelete = SmallButton("", Loc.T("zone.delete", "Excluir esta zona"), DeleteZone);
        _zGroundBtn = SmallButton("", Loc.T("zone.ground", "Ver no chão"), () => ShowZoneGround(true));
        foreach (var c in new Control[] { _zGroundBtn, _zDelete,
            SmallButton("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny()), SmallButton("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny()) }) _zEndCard.Controls.Add(c);
        _zonePage.Controls.Add(_zEndCard);
        FitToScroll(_zonePage);
        foreach (var c in new Control[] { _zInstances, _zLoop, _zCities, _zDomain, _zMarks }) c.Width = 228;
        _zSearch.Width = _zName.Width = _zList.Width = 228;
    }

    sealed record ZoneCity(string Name, (float X, float Y, float Z) At) { public override string ToString() => Name; }
    sealed record ZoneDomain(int Id, string Name) { public override string ToString() => Name; }

    // ---------- abrir e fechar ----------
    void OpenZones()
    {
        var folder = Path.GetFileName(_t.Dir.TrimEnd('\\', '/'));
        _zones = ZonePair.Open(_t.Dir, ServerConfigDir is { } config ? Path.Combine(config, folder) : null);
        ZoneReload();
        _zDomains = [];
        try
        {
            var data = Path.Combine(ElementOfMap, "data", "domain.data");
            if (_t.Name == "world" && File.Exists(data)) _zDomains = DomainMap.Parse(File.ReadAllBytes(data)).Areas.ToDictionary(a => a.Id, a => a.Name);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { AppLog.Error("zonas: domain.data", ex); }
        var img = WorldMap();
        var unit = MapPx(100, 0).X - MapPx(0, 0).X;
        _zCanvas.Map = img;
        _zCanvas.WorldSize = img.Size;
        _zCanvas.MetersPerPixel = unit > 0 ? 100 / unit : 0;
        _zCanvas.Bounds = _gl.Bounds;
        _zCanvas.Visible = true;
        _zCanvas.BringToFront();
        _tools.BringToFront();
        _zUndo.Clear(); _zRedo.Clear(); _zSavedUndo.Clear(); _zSavedRedo.Clear();
        UpdateZonePanel();
    }

    /// <summary>Recomeça do que está no disco.</summary>
    void ZoneReload()
    {
        _zw = _zones!.Working();
        _zInstance = _zw.Areas.Count > 0 ? _zw.MainInstance : Math.Max(1, MapTag);
        _zOpen = -1; _zDirty = false;
        _zClean = [.. _zw.Areas.Select(ZoneKey)];
    }

    void CloseZones()
    {
        ShowZoneGround(false);
        _zCanvas.Visible = false;
        _zFoot.Visible = false;
        _zDrawing = ZoneDraw.None; _zPick = null; _zOpen = -1;
        _gl.Invalidate();
    }

    static string ZoneKey(Area a)
    {
        var one = new PrecinctFile();
        one.Areas.Add(a);
        try { return Convert.ToBase64String(one.Render()); }
        catch (ArgumentException) { return a.Name + "!"; }
    }

    bool Writable => _zones is { State: not PairState.ReadOnly };
    IEnumerable<int> ZoneIndexes => Enumerable.Range(0, _zw.Areas.Count).Where(i => _zw.Areas[i].SrcInstance == _zInstance);
    Area? OpenArea => _zOpen >= 0 && _zOpen < _zw.Areas.Count ? _zw.Areas[_zOpen] : null;

    /// <summary>Saindo com mudança: Salvar, Descartar ou Cancelar. Falso = a pessoa quer continuar aqui.</summary>
    bool ZoneLeave()
    {
        if (!_zDirty) return true;
        var save = new TaskDialogButton(Loc.T("zone.leaveSave", "Salvar"));
        var drop = new TaskDialogButton(Loc.T("zone.leaveDrop", "Descartar"));
        var answer = TaskDialog.ShowDialog(this, new TaskDialogPage { Caption = Text, Heading = Loc.T("zone.leaveAsk", "Há mudanças nas zonas que não foram salvas."), Buttons = { save, drop, TaskDialogButton.Cancel }, DefaultButton = save });
        if (answer == drop) { _zDirty = false; return true; }
        if (answer != save) return false;
        SaveZones();
        return !_zDirty;
    }

    // ---------- mudar ----------
    void ZoneRemember()
    {
        _zUndo.Add((_zw.Render(), _zOpen));
        _zRedo.Clear();
        if (_zUndo.Count > 200) _zUndo.RemoveAt(0);
    }

    void ZoneChange(Action change)
    {
        if (OpenArea is null || !Writable) return;
        ZoneRemember();
        change();
        _zDirty = true;
        UpdateZonePanel();
        _zCanvas.Invalidate();
    }

    /// <summary>Muda a zona aberta, ou todas as partes quando a caixa Vale para as N partes está marcada.</summary>
    void ZoneSet(Func<Area, Area> change, CheckBox all) => ZoneChange(() =>
    {
        if (all.Checked) ZoneEdit.SetForParts(_zw, _zOpen, change);
        else _zw.Areas[_zOpen] = change(_zw.Areas[_zOpen]);
    });

    /// <summary>Ctrl+Z da ferramenta: passo a passo antes de salvar; depois, regrava o par como estava.</summary>
    bool ZoneUndo(bool redo)
    {
        if (_tool != Tool.Zones || _zones is null) return false;
        var from = redo ? _zRedo : _zUndo; var to = redo ? _zUndo : _zRedo;
        if (from.Count > 0)
        {
            to.Add((_zw.Render(), _zOpen));
            var (file, open) = from[^1];
            from.RemoveAt(from.Count - 1);
            _zw = PrecinctFile.Parse(file);
            _zOpen = open < _zw.Areas.Count ? open : -1;
            _zDirty = !_zw.Render().AsSpan().SequenceEqual(_zones.Working().Render());
            UpdateZonePanel(); _zCanvas.Invalidate();
            return true;
        }
        var saved = redo ? _zSavedRedo : _zSavedUndo; var other = redo ? _zSavedUndo : _zSavedRedo;
        if (saved.Count == 0) return false;
        var now = _zones.Snapshot();
        var result = _zones.Restore(saved[^1]);
        if (result.Kind is not (SaveKind.Both or SaveKind.ClientOnly)) { _status.Text = result.Message; return true; }
        saved.RemoveAt(saved.Count - 1);
        other.Add(now);
        AfterZoneSave(result);
        return true;
    }

    void FixPair()
    {
        if (_zones is null) return;
        if (_zones.State == PairState.ListsDiffer)
        {
            int gone = (_zones.Server?.Areas.Count ?? 0) - _zones.Client.Areas.Count;
            var text = string.Format(Loc.T("zone.truthAsk", "O servidor passa a ter as {0} zonas do cliente ({1} a mais ou a menos). Continuar?"), _zones.Client.Areas.Count, Math.Abs(gone));
            if (MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _zSavedUndo.Add(_zones.Snapshot());
            AfterZoneSave(_zones.UseClientAsTruth());
            return;
        }
        _zSavedUndo.Add(_zones.Snapshot());
        AfterZoneSave(_zones.Save(_zones.Working()));   // igualar: grava o mesmo carimbo nos dois
    }

    // ---------- passos ----------
    void StartNewZone() { _zOpen = -1; StartDrawing(ZoneDraw.NewZone); }

    void StartDrawing(ZoneDraw kind)
    {
        if (!Writable) return;
        _zDrawing = kind; _zPoints = []; _zPick = null;
        _status.Text = Loc.T("zone.drawHint", "Clique para pôr cada canto. Clique no primeiro ou aperte Enter para fechar (Esc cancela).");
        _zCanvas.Focus();
        UpdateZonePanel(); _zCanvas.Invalidate();
    }

    void FinishDrawing()
    {
        if (_zPoints.Count < 3) { _status.Text = Loc.T("zone.fewCorners", "Uma zona precisa de pelo menos 3 cantos."); return; }
        var kind = _zDrawing; var corners = _zPoints;
        _zDrawing = ZoneDraw.None; _zPoints = [];
        _status.Text = "";
        ZoneRemember();
        if (kind == ZoneDraw.NewZone) _zOpen = ZoneEdit.New(_zw, Loc.T("zone.newName", "Zona nova"), corners, _zInstance);
        else if (kind == ZoneDraw.NewPart) _zOpen = ZoneEdit.NewPart(_zw, _zOpen, corners);
        else _zw.Areas[_zOpen] = _zw.Areas[_zOpen] with { Points = [.. corners.Select(p => (p.X, p.Z))], PointY = [.. corners.Select(p => p.Y)] };
        _zDirty = true;
        UpdateZonePanel(); _zCanvas.Invalidate();
    }

    void PickOnMap(string hint, Action<float, float> done)
    {
        _zPick = done; _status.Text = hint;
        if (_zGround) ShowZoneGround(false);
        _zCanvas.Focus();
    }

    float GroundAt(float x, float z) => _t.HeightAt(x, z) ?? 0;

    void PickZoneMusic()
    {
        if (OpenArea is not { } a) return;
        using var w = new ZoneMusicWindow(ElementOfMap, a.Music, single: false);
        if (w.ShowDialog(this) == DialogResult.OK) ZoneSet(x => x with { Music = [.. w.Music] }, _zMusicAll);
    }

    void PickZoneSound(bool night)
    {
        if (OpenArea is not { } a) return;
        using var w = new ZoneMusicWindow(ElementOfMap, [night ? a.NightSound : a.Sound], single: true);
        if (w.ShowDialog(this) != DialogResult.OK) return;
        var sound = w.Music.FirstOrDefault() ?? "";
        ZoneSet(x => night ? x with { NightSound = sound } : x with { Sound = sound }, _zMusicAll);
    }

    void AddZoneMark()
    {
        if (OpenArea is null) return;
        using var ask = new Form { Text = Loc.T("zone.addMark", "Marcar um lugar"), ClientSize = new Size(320, 96), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        var box = new TextBox { Location = new Point(12, 14), Width = 296, MaxLength = ZoneRules.MaxName, PlaceholderText = Loc.T("zone.markName", "Nome do lugar") };
        box.KeyPress += (_, e) => { if (e.KeyChar == '"') e.Handled = true; };
        var ok = new Button { Text = Loc.T("zone.ok", "Escolher no mapa"), DialogResult = DialogResult.OK, Location = new Point(150, 52), Width = 158 };
        ask.Controls.Add(box); ask.Controls.Add(ok); ask.AcceptButton = ok;
        if (ask.ShowDialog(this) != DialogResult.OK || box.Text.Trim().Length == 0) return;
        var name = box.Text.Trim();
        PickOnMap(Loc.T("zone.clickMark", "Clique no mapa onde fica o lugar."), (x, z) => ZoneChange(() =>
        {
            var a = _zw.Areas[_zOpen];
            _zw.Areas[_zOpen] = a with { Marks = [.. a.Marks, new PrecinctFile.Mark(name, x, GroundAt(x, z), z)] };
        }));
    }

    void DropZoneMark()
    {
        if (_zMarks.SelectedIndex < 0 || OpenArea is not { } a) return;
        int at = _zMarks.SelectedIndex;
        ZoneChange(() => _zw.Areas[_zOpen] = a with { Marks = [.. a.Marks.Where((_, i) => i != at)] });
    }

    // ---------- salvar e excluir ----------
    ZoneContext ZoneCtx(Area a)
    {
        bool isNew = _zones is null || _zones.Client.Areas.All(c => c.Id != a.Id);
        bool shape = _zones is null || _zones.Client.Areas.All(c => !c.Points.SequenceEqual(a.Points));
        return new(isNew, shape, m => File.Exists(Path.Combine(ElementOfMap, m)), id => _zDomains.Count == 0 || _zDomains.ContainsKey(id), (x, z) => MathF.Abs(x) <= _t.Width / 2f && MathF.Abs(z) <= _t.Height / 2f);
    }

    List<(Area Area, ZoneProblem Problem)> ZoneProblems() =>
        [.. _zw.Areas.Where(a => !_zClean.Contains(ZoneKey(a))).SelectMany(a => ZoneRules.Check(_zw, a, ZoneCtx(a)).Select(p => (a, p)))];

    void SaveZones()
    {
        if (_zones is null || !_zDirty) return;
        var problems = ZoneProblems();
        if (problems.Any(p => p.Problem.Kind == ZoneProblemKind.Block)) { UpdateZonePanel(); return; }
        var asks = problems.Select(p => p.Problem.Text).ToList();
        float holes = ZoneRules.Holes(_zones.Client, _zw, _zInstance);
        if (holes > 0.0005f) asks.Add(Loc.T("zone.holeAsk", "Uma parte do mapa fica sem zona: ali a música para, quem morre renasce no lugar e voltar à cidade falha."));
        bool serverChange = ZonePair.ServerChanged(_zones.Client, _zw);
        if (serverChange && _zones.ServerUnreachable) asks.Add(Loc.T("zone.clientOnlyAsk", "A forma ou as regras mudaram, mas o servidor não vai receber: cliente e servidor ficam diferentes."));
        var moved = TerritoryNpcs();
        if (moved.Count > 0) asks.Add(string.Format(Loc.T("zone.npcAsk", "Estes NPCs de território passam a outro território: {0}"), string.Join(", ", moved.Take(8))));
        if (asks.Count > 0 && MessageBox.Show(this, string.Join("\n\n", asks) + "\n\n" + Loc.T("zone.saveAnyway", "Salvar mesmo assim?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var before = _zones.Snapshot();
        var result = _zones.Save(_zw);
        if (result.Kind == SaveKind.ChangedOnDisk)
        {
            var reload = new TaskDialogButton(Loc.T("zone.reload", "Recarregar"));
            var over = new TaskDialogButton(Loc.T("zone.overwrite", "Gravar por cima"));
            var answer = TaskDialog.ShowDialog(this, new TaskDialogPage { Caption = Text, Heading = result.Message, Buttons = { reload, over, TaskDialogButton.Cancel }, DefaultButton = reload });
            if (answer == reload) { _zones = ZonePair.Open(_t.Dir, _zones.ServerPath is { } s ? Path.GetDirectoryName(s) : null); ZoneReload(); UpdateZonePanel(); _zCanvas.Invalidate(); return; }
            if (answer != over) return;
            result = _zones.Save(_zw, overwriteExternal: true);
        }
        if (result.Kind == SaveKind.Locked)
        {
            if (MessageBox.Show(this, result.Message, Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry) SaveZones();
            return;
        }
        if (result.Kind is SaveKind.Failed or SaveKind.RolledBack) { MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        _zSavedUndo.Add(before); _zSavedRedo.Clear();
        _zUndo.Clear(); _zRedo.Clear();
        AfterZoneSave(result);
    }

    void AfterZoneSave(SaveResult result)
    {
        _status.Text = result.Message;
        if (result.Kind is SaveKind.Failed or SaveKind.RolledBack or SaveKind.Locked) { MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        int open = _zOpen;
        ZoneReload();
        _zOpen = open < _zw.Areas.Count ? open : -1;
        _music?.Reload(_t.Dir);
        _tpPrecinct = null;
        UpdateZonePanel(); _zCanvas.Invalidate();
    }

    /// <summary>NPCs ligados a território que estão em zona cujo território muda.</summary>
    List<string> TerritoryNpcs()
    {
        var list = new List<string>();
        if (_zones is null || _host is null) return list;
        foreach (var m in _allMarks.Where(m => m.Id > 0 && _host.IsDomainNpc(m.Id)))
        {
            int was = _zones.Client.At(m.X, m.Z, _zInstance)?.Domain ?? 0, now = _zw.At(m.X, m.Z, _zInstance)?.Domain ?? 0;
            if (was != now && !list.Contains(m.Name)) list.Add(m.Name);
        }
        return list;
    }

    void DeleteZone()
    {
        if (OpenArea is null) return;
        var parts = ZoneEdit.Parts(_zw, _zOpen);
        var gone = new List<int> { _zOpen };
        if (parts.Count > 1)
        {
            var one = new TaskDialogButton(Loc.T("zone.delPart", "Só esta parte"));
            var all = new TaskDialogButton(Loc.T("zone.delAll", "A zona inteira"));
            var answer = TaskDialog.ShowDialog(this, new TaskDialogPage { Caption = Text, Heading = string.Format(Loc.T("zone.delAsk", "Esta zona tem {0} partes."), parts.Count), Buttons = { one, all, TaskDialogButton.Cancel }, DefaultButton = one });
            if (answer == all) gone = parts; else if (answer != one) return;
        }
        var trial = PrecinctFile.Parse(_zw.Render());
        foreach (int i in gone.OrderDescending()) trial.Areas.RemoveAt(i);
        float holes = ZoneRules.Holes(_zw, trial, _zInstance);
        var text = holes > 0.0005f
            ? Loc.T("zone.delHole", "Uma parte do mapa fica sem zona: ali a música para, quem morre renasce no lugar e voltar à cidade falha. Excluir mesmo assim?")
            : Loc.T("zone.delPlain", "Excluir? O jogador passa a ver a zona de baixo neste lugar.");
        _zHatch = holes > 0.0005f;
        _zCanvas.Refresh();
        bool yes = MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, holes > 0.0005f ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes;
        _zHatch = false;
        if (!yes) { _zCanvas.Invalidate(); return; }
        ZoneChange(() => { foreach (int i in gone.OrderDescending()) _zw.Areas.RemoveAt(i); _zOpen = -1; });
        _zDirty = true;
        UpdateZonePanel(); _zCanvas.Invalidate();
    }

    // ---------- painel ----------
    void UpdateZonePanel()
    {
        if (_zones is null) return;
        _zFilling = true;
        var open = OpenArea;
        bool drawing = _zDrawing != ZoneDraw.None;
        _zState.Text = "● " + _zones.StateText + "\n" + string.Format(Loc.T("zone.count", "{0} zonas neste mapa"), ZoneIndexes.Count());
        _zState.ForeColor = _zones.State switch { PairState.Ok => Color.FromArgb(88, 214, 141), PairState.ListsDiffer or PairState.ReadOnly or PairState.ServerBroken => Color.FromArgb(248, 113, 113), _ => Color.FromArgb(250, 204, 21) };
        _zFix.Visible = _zones.State is PairState.StampDiffers or PairState.ListsDiffer;
        _zFix.Text = "  " + (_zones.State == PairState.ListsDiffer ? Loc.T("zone.useClient", "Usar o cliente como certo") : Loc.T("zone.match", "Igualar"));
        _zBackList.Visible = open is not null || drawing;
        _zListCard.Visible = open is null && !drawing;
        _zNewBtn.Enabled = Writable;
        _zInstances.Visible = _zones.Instances.Count > 1;
        if (_zInstances.Items.Count != _zones.Instances.Count)
        {
            _zInstances.Items.Clear();
            foreach (int i in _zones.Instances) _zInstances.Items.Add(string.Format(Loc.T("zone.instance", "Instância {0}"), i));
        }
        int atInstance = _zones.Instances.ToList().IndexOf(_zInstance);
        if (atInstance >= 0 && _zInstances.Items.Count > atInstance) _zInstances.SelectedIndex = atInstance;
        foreach (var card in new[] { _zNameCard, _zAreaCard, _zMusicCard, _zRulesCard, _zMarksCard }) { card.Visible = open is not null && !drawing; card.Enabled = Writable; }
        _zEndCard.Visible = !drawing;
        _zDelete.Visible = _zGroundBtn.Visible = open is not null;
        if (open is null) FillZoneList();
        else FillOpenZone(open);
        var problems = ZoneProblems();
        var blocks = problems.Where(p => p.Problem.Kind == ZoneProblemKind.Block).ToList();
        _zBadCorner = open is not null ? blocks.FirstOrDefault(b => ReferenceEquals(b.Area, open)).Problem?.Corner ?? -1 : -1;
        _zWhy.Text = string.Join("\n", blocks.Select(b => b.Problem.Text).Distinct());
        _zWhy.Visible = _zWhy.Text.Length > 0;
        _zSummary.Text = ZoneSummary(open);
        _zSave.Enabled = _zDirty && blocks.Count == 0 && Writable;
        _zSave.BackColor = _zSave.Enabled ? UiKit.Accent : Color.FromArgb(52, 58, 68);
        _zFilling = false;
        LayoutTools();
    }

    string Cleanup(string name) => name.Length > 0 ? name : Loc.T("zone.noNameShown", "(sem nome)");

    void FillZoneList()
    {
        if (_zones is null) return;
        var was = _zFilling; _zFilling = true;
        var find = _zSearch.Text.Trim();
        _zList.BeginUpdate(); _zList.Items.Clear();
        var seen = new HashSet<int>();
        foreach (int i in ZoneIndexes.OrderBy(i => _zw.Areas[i].Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var a = _zw.Areas[i];
            if (a.Id != 0 && !seen.Add(a.Id)) continue;   // zona em partes: uma linha só
            if (find.Length == 0 || a.Name.Contains(find, StringComparison.CurrentCultureIgnoreCase)) _zList.Items.Add(i);
        }
        _zList.SelectedIndex = -1;
        _zList.EndUpdate();
        _zFilling = was;
    }

    void DrawZoneRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _zList.Items.Count || _zList.Items[e.Index] is not int i || i >= _zw.Areas.Count) return;
        var a = _zw.Areas[i];
        using var back = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(44, 62, 90) : _zList.BackColor);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var bold = new Font("Segoe UI Semibold", 9.5f); using var small = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, Cleanup(a.Name), bold, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 3, e.Bounds.Width - 10, 18), a.PkProtected ? Color.FromArgb(0, 255, 252) : Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var info = new List<string>();
        int parts = ZoneEdit.Parts(_zw, i).Count;
        if (a.Id == 0) info.Add(Loc.T("zone.hidden", "não mostra nome ao jogador"));
        if (parts > 1) info.Add(string.Format(Loc.T("zone.parts", "{0} partes"), parts));
        if (_zDomains.TryGetValue(a.Domain, out var d)) info.Add(string.Format(Loc.T("zone.domainOf", "território {0}"), d));
        if (a.PkProtected) info.Add(Loc.T("zone.protected", "novatos protegidos"));
        TextRenderer.DrawText(e.Graphics, string.Join(" · ", info), small, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 20, e.Bounds.Width - 10, 16), Color.Silver, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void FillOpenZone(Area a)
    {
        var parts = ZoneEdit.Parts(_zw, _zOpen);
        bool Same<T>(Func<Area, T> f) => parts.Select(i => f(_zw.Areas[i])).Distinct().Count() == 1;
        string all = string.Format(Loc.T("zone.forParts", "Vale para as {0} partes"), parts.Count);
        foreach (var box in new[] { _zNameAll, _zMusicAll, _zRulesAll }) { box.Text = all; box.Visible = parts.Count > 1; }
        _zNameAll.Checked = Same(x => x.Name);
        _zMusicAll.Checked = Same(x => string.Join("|", x.Music));
        _zRulesAll.Checked = Same(x => x.City) && Same(x => x.Domain) && Same(x => x.PkProtected);

        string Done(bool ok, int n, string title) => (ok ? "✓ " : "") + $"{n}. {title}";
        _zStepName.Text = Done(a.Name.Trim().Length > 0, 1, Loc.T("zone.step1", "Nome"));
        _zStepArea.Text = Done(a.Points.Count >= 3, 2, Loc.T("zone.step2", "Área"));
        _zStepMusic.Text = Done(a.Music.Count > 0, 3, Loc.T("zone.step3", "Música e som"));
        _zStepRules.Text = Done(a.City is not { X: 0, Y: 0, Z: 0 }, 4, Loc.T("zone.step4", "Regras do lugar"));
        _zStepMarks.Text = $"5. " + Loc.T("zone.step5", "Lugares no minimapa (opcional)");
        if (_zName.Text != a.Name) _zName.Text = a.Name;

        float width = a.Points.Count > 0 ? a.Points.Max(p => p.X) - a.Points.Min(p => p.X) : 0;
        _zShape.Text = string.Format(Loc.T("zone.shape", "{0} cantos · {1:0} m de largura"), a.Points.Count, width);
        _zParts.Controls.Clear();
        if (parts.Count > 1)
            for (int k = 0; k < parts.Count; k++)
            {
                int index = parts[k];
                var pill = new Button { Text = string.Format(Loc.T("zone.part", "Parte {0}"), k + 1), AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = index == _zOpen ? UiKit.Accent : Color.FromArgb(40, 45, 54), Margin = new Padding(0, 0, 4, 4) };
                pill.Click += (_, _) => { OpenZone(index); FrameZone(); };
                _zParts.Controls.Add(pill);
            }
        _zParts.Visible = parts.Count > 1;
        var near = ZoneEdit.Neighbors(_zw, _zOpen);
        _zTouch.Visible = _zAbove.Visible = _zUnder.Visible = near.Count > 0;
        _zTouch.Text = string.Format(Loc.T("zone.touches", "Encosta em: {0}"), string.Join(", ", near.Select(i => Cleanup(_zw.Areas[i].Name)).Distinct().Take(4)));

        var names = a.Music.Select(Path.GetFileName).ToList();
        _zMusicText.Text = (names.Count > 0 ? "▶ " + string.Join(", ", names) : Loc.T("zone.noMusicYet", "Sem música")) + "\n" + string.Format(Loc.T("zone.sounds", "Dia: {0} · Noite: {1}"), Cleanup(Path.GetFileName(a.Sound)), Cleanup(Path.GetFileName(a.NightSound)));
        _zLoop.SelectedIndex = Math.Clamp(a.Loop, 0, 2);

        var cities = _zw.Areas.Where(x => x.City is not { X: 0, Y: 0, Z: 0 }).GroupBy(x => x.City).Select(g => new ZoneCity(Cleanup(g.First().Name), g.Key)).OrderBy(c => c.Name).ToList();
        _zCities.Items.Clear();
        foreach (var c in cities) _zCities.Items.Add(c);
        _zCities.SelectedIndex = cities.FindIndex(c => c.At == a.City);
        _zCity.Text = a.City is { X: 0, Y: 0, Z: 0 } ? Loc.T("zone.noCityYet", "Renasce em: não definido") : string.Format(Loc.T("zone.cityAt", "Renasce em: {0} ({1:0}, {2:0})"), _zCities.SelectedItem?.ToString() ?? Loc.T("zone.onMap", "ponto no mapa"), a.City.X, a.City.Z);
        _zDomain.Visible = _zDomains.Count > 0;
        if (_zDomains.Count > 0)
        {
            _zDomain.Items.Clear();
            _zDomain.Items.Add(new ZoneDomain(0, Loc.T("zone.noDomainPick", "Sem território")));
            foreach (var (id, name) in _zDomains.OrderBy(d => d.Value)) _zDomain.Items.Add(new ZoneDomain(id, name));
            _zDomain.SelectedIndex = Math.Max(0, _zDomain.Items.Cast<ZoneDomain>().ToList().FindIndex(d => d.Id == a.Domain));
        }
        _zPk.Text = Loc.T("zone.pk", "Novatos protegidos de PK");
        _zPk.Checked = a.PkProtected;
        _zMarks.Items.Clear();
        foreach (var m in a.Marks) _zMarks.Items.Add(m.Name);
    }

    string ZoneSummary(Area? open)
    {
        if (_zones is null) return "";
        var lines = new List<string>();
        if (open is not null)
        {
            lines.Add(open.Id == 0 ? Loc.T("zone.sumHidden", "• O jogador não vê nome aqui.") : string.Format(Loc.T("zone.sumName", "• Ao entrar, o jogador lê \"{0}\"."), open.Name));
            if (open.PkProtected) lines.Add(Loc.T("zone.sumPk", "• Até o nível 29, sem nome rosa ou vermelho, ninguém ataca aqui."));
            if (open.City is not { X: 0, Y: 0, Z: 0 }) lines.Add(string.Format(Loc.T("zone.sumCity", "• Quem morre aqui renasce em {0:0}, {1:0}."), open.City.X, open.City.Z));
        }
        if (!_zDirty) { lines.Add(Loc.T("zone.sumClean", "• Nada mudou ainda.")); return string.Join("\n", lines); }
        lines.Add(ZonePair.ServerChanged(_zones.Client, _zw) && !_zones.ServerUnreachable
            ? Loc.T("zone.sumBoth", "• Muda cliente e servidor. Depois de salvar, reinicie o mapa e o jogo.")
            : Loc.T("zone.sumClient", "• Muda só o cliente. Depois de salvar, feche e abra o jogo."));
        lines.Add(Loc.T("zone.sumUndo", "Ctrl+Z desfaz até depois de salvar."));
        return string.Join("\n", lines);
    }

    void OpenZone(int index)
    {
        if (index < 0 || index >= _zw.Areas.Count) return;
        _zOpen = index; _zDrawing = ZoneDraw.None;
        UpdateZonePanel();
        _zCanvas.Invalidate();
        _gl.Invalidate();
    }

    void FrameZone()
    {
        if (OpenArea is not { Points.Count: > 0 } a) return;
        var px = a.Points.Select(p => MapPx(p.X, p.Z)).ToList();
        float x0 = px.Min(p => p.X), y0 = px.Min(p => p.Y);
        _zCanvas.Frame(new RectangleF(x0, y0, px.Max(p => p.X) - x0, px.Max(p => p.Y) - y0));
    }

    // ---------- mapa ----------
    PointF ZoneScreen(float x, float z) => _zCanvas.ToScreen(MapPx(x, z));

    int ZoneCorner(Point p)
    {
        if (OpenArea is not { } a) return -1;
        for (int i = 0; i < a.Points.Count; i++)
        {
            var s = ZoneScreen(a.Points[i].X, a.Points[i].Z);
            if (MathF.Abs(s.X - p.X) <= 8 && MathF.Abs(s.Y - p.Y) <= 8) return i;
        }
        return -1;
    }

    void ZoneDown(MouseEventArgs e, PointF map)
    {
        var (x, z) = MapGame(map.X, map.Y);
        if (_zPick is { } pick && e.Button == MouseButtons.Left) { _zPick = null; _status.Text = ""; pick(x, z); return; }
        if (_zDrawing != ZoneDraw.None)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_zPoints.Count >= 3)
            {
                var first = ZoneScreen(_zPoints[0].X, _zPoints[0].Z);
                if (MathF.Abs(first.X - e.X) <= 9 && MathF.Abs(first.Y - e.Y) <= 9) { FinishDrawing(); return; }
            }
            _zPoints.Add((Math.Clamp(x, -_t.Width / 2f, _t.Width / 2f), GroundAt(x, z), Math.Clamp(z, -_t.Height / 2f, _t.Height / 2f)));
            _zCanvas.Invalidate();
            return;
        }
        int corner = ZoneCorner(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (corner < 0 || OpenArea is not { } a) return;
            if (a.Points.Count <= 3) { _status.Text = Loc.T("zone.fewCorners", "Uma zona precisa de pelo menos 3 cantos."); return; }
            ZoneChange(() => _zw.Areas[_zOpen] = ZoneEdit.RemoveCorner(a, corner));
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (corner >= 0) { if (Writable) _zDrag = corner; return; }
        ZonePickAt(x, z);
    }

    /// <summary>Escolhe a zona que o jogador vê ali; clicar de novo no mesmo ponto passa para a de baixo.</summary>
    void ZonePickAt(float x, float z)
    {
        var here = ZoneIndexes.Where(i => _zw.Areas[i].Contains(x, z)).OrderBy(i => _zw.Areas[i].Priority).ThenByDescending(i => i).ToList();
        if (here.Count == 0) { _zOpen = -1; UpdateZonePanel(); _zCanvas.Invalidate(); return; }
        bool again = MathF.Abs(_zLastClick.X - x) < 1 && MathF.Abs(_zLastClick.Y - z) < 1;
        _zCycle = again ? _zCycle + 1 : 0;
        _zLastClick = new PointF(x, z);
        OpenZone(here[_zCycle % here.Count]);
    }

    void ZoneMove(MouseEventArgs e, PointF map)
    {
        var (x, z) = MapGame(map.X, map.Y);
        if (_zDrag >= 0 && OpenArea is { } a && e.Button == MouseButtons.Left)
        {
            if (!_zMoved) { ZoneRemember(); _zMoved = true; _zDirty = true; }
            _zw.Areas[_zOpen] = ZoneEdit.MoveCorner(a, _zDrag, x, z, _t.Width / 2f, _t.Height / 2f);
            _zCanvas.Invalidate();
            return;
        }
        var at = _zw.At(x, z, _zInstance);
        _zBar.Text = at is null ? Loc.T("zone.barNone", "Aqui não há zona: a música para e quem morre renasce no lugar.")
            : string.Format(Loc.T("zone.bar", "Aqui o jogador vê: {0}"), at.Id == 0 ? Loc.T("zone.barHidden", "nenhum nome") : Cleanup(at.Name))
              + (at.City is not { X: 0, Y: 0, Z: 0 } ? " · " + string.Format(Loc.T("zone.barCity", "renasce em {0:0}, {1:0}"), at.City.X, at.City.Z) : "")
              + (_zDomains.TryGetValue(at.Domain, out var d) ? " · " + string.Format(Loc.T("zone.domainOf", "território {0}"), d) : "")
              + " · " + (at.PkProtected ? Loc.T("zone.protected", "novatos protegidos") : Loc.T("zone.barOpen", "sem proteção"));
        if (_zDrawing != ZoneDraw.None) _zCanvas.Invalidate();
    }

    void ZoneDouble(MouseEventArgs e, PointF map)
    {
        if (e.Button != MouseButtons.Left || OpenArea is not { } a || _zDrawing != ZoneDraw.None || ZoneCorner(e.Location) >= 0) return;
        int best = -1; float near = 12;
        for (int i = 0; i < a.Points.Count; i++)
        {
            var p = ZoneScreen(a.Points[i].X, a.Points[i].Z); var q = ZoneScreen(a.Points[(i + 1) % a.Points.Count].X, a.Points[(i + 1) % a.Points.Count].Z);
            float dx = q.X - p.X, dy = q.Y - p.Y, t = Math.Clamp(((e.X - p.X) * dx + (e.Y - p.Y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
            float d = MathF.Sqrt(MathF.Pow(p.X + t * dx - e.X, 2) + MathF.Pow(p.Y + t * dy - e.Y, 2));
            if (d < near) { near = d; best = i; }
        }
        if (best < 0) return;
        var (x, z) = MapGame(map.X, map.Y);
        ZoneChange(() => _zw.Areas[_zOpen] = ZoneEdit.AddCorner(a, best, x, GroundAt(x, z), z));
    }

    void PaintZones(Graphics g)
    {
        using var font = new Font("Segoe UI", 9f); using var bold = new Font("Segoe UI Semibold", 10f);
        foreach (int i in ZoneIndexes)
        {
            var a = _zw.Areas[i];
            if (a.Points.Count < 3 || i == _zOpen) continue;
            var pts = a.Points.Select(p => ZoneScreen(p.X, p.Z)).ToArray();
            var edge = a.PkProtected ? Color.FromArgb(0, 255, 252) : Color.FromArgb(150, 216, 222, 232);
            using var fill = new SolidBrush(Color.FromArgb(a.PkProtected ? 40 : 14, a.PkProtected ? Color.FromArgb(0, 255, 252) : Color.White));
            using var pen = new Pen(edge, 1.3f);
            g.FillPolygon(fill, pts); g.DrawPolygon(pen, pts);
            ZoneLabel(g, a, pts, font, edge.A < 255 ? Color.White : edge);
        }
        if (OpenArea is { Points.Count: >= 3 } open)
        {
            var pts = open.Points.Select(p => ZoneScreen(p.X, p.Z)).ToArray();
            using Brush fill = _zHatch ? new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(220, 248, 113, 113), Color.FromArgb(60, 0, 0, 0)) : new SolidBrush(Color.FromArgb(70, 74, 163, 255));
            using var pen = new Pen(Color.FromArgb(109, 183, 255), 2.6f);
            g.FillPolygon(fill, pts); g.DrawPolygon(pen, pts);
            ZoneLabel(g, open, pts, bold, Color.White);
            for (int i = 0; i < pts.Length; i++)
            {
                using var dot = new SolidBrush(i == _zBadCorner ? Color.FromArgb(248, 80, 80) : Color.White);
                g.FillEllipse(dot, pts[i].X - 5, pts[i].Y - 5, 10, 10);
                g.DrawEllipse(Pens.RoyalBlue, pts[i].X - 5, pts[i].Y - 5, 10, 10);
            }
            if (open.City is not { X: 0, Y: 0, Z: 0 })
            {
                var c = ZoneScreen(open.City.X, open.City.Z);
                using var gold = new SolidBrush(Color.FromArgb(255, 200, 90));
                g.FillEllipse(gold, c.X - 9, c.Y - 9, 18, 18); g.DrawEllipse(Pens.White, c.X - 9, c.Y - 9, 18, 18);
                TextRenderer.DrawText(g, "+", bold, new Rectangle((int)c.X - 9, (int)c.Y - 10, 18, 18), Color.FromArgb(42, 32, 0), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            foreach (var m in open.Marks)
            {
                var c = ZoneScreen(m.X, m.Z);
                g.FillEllipse(Brushes.Gold, c.X - 4, c.Y - 4, 8, 8);
                TextRenderer.DrawText(g, m.Name, font, new Point((int)c.X + 6, (int)c.Y - 8), Color.White);
            }
        }
        if (_zDrawing != ZoneDraw.None && _zPoints.Count > 0)
        {
            var pts = _zPoints.Select(p => ZoneScreen(p.X, p.Z)).ToArray();
            using var pen = new Pen(Color.FromArgb(109, 183, 255), 2.4f);
            if (pts.Length > 1) g.DrawLines(pen, pts);
            var mouse = _zCanvas.PointToClient(Cursor.Position);
            g.DrawLine(pen, pts[^1], mouse);
            foreach (var p in pts) { g.FillEllipse(Brushes.White, p.X - 5, p.Y - 5, 10, 10); g.DrawEllipse(Pens.RoyalBlue, p.X - 5, p.Y - 5, 10, 10); }
        }
    }

    /// <summary>O nome só aparece quando cabe dentro da zona naquele zoom.</summary>
    static void ZoneLabel(Graphics g, Area a, PointF[] pts, Font font, Color color)
    {
        if (a.Name.Length == 0) return;
        float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        var size = TextRenderer.MeasureText(a.Name, font);
        if (x1 - x0 < size.Width + 6 || y1 - y0 < size.Height + 6) return;
        var r = new Rectangle((int)((x0 + x1) / 2 - size.Width / 2), (int)((y0 + y1) / 2 - size.Height / 2), size.Width, size.Height);
        TextRenderer.DrawText(g, a.Name, font, r with { X = r.X + 1, Y = r.Y + 1 }, Color.Black, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, a.Name, font, r, color, TextFormatFlags.NoPrefix);
    }

    // ---------- ver no chão ----------
    void ShowZoneGround(bool on)
    {
        _zGround = on;
        _zCanvas.Visible = !on && _tool == Tool.Zones;
        _zBackMap.Visible = on;
        if (on)
        {
            _zBackMap.Text = Loc.T("zone.backMap", "Voltar ao mapa");
            _zBackMap.BringToFront();
            if (OpenArea is { Points.Count: > 0 } a)
            {
                float x = a.Points.Average(p => p.X), z = a.Points.Average(p => p.Z);
                _cam = Gl(x, GroundAt(x, z) + 300, z - 300); _pitch = -40; _yaw = -90;
                ClampCamera();
            }
        }
        _tools.BringToFront();
        _gl.Invalidate();
    }

    /// <summary>Borda da zona aberta acompanhando o terreno.</summary>
    void DrawZoneGround()
    {
        if (!_zGround || OpenArea is not { Points.Count: >= 3 } a) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.Color3(0.43f, 0.72f, 1f); GL.LineWidth(3);
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < a.Points.Count; i++)
        {
            var p = a.Points[i]; var q = a.Points[(i + 1) % a.Points.Count];
            int steps = Math.Clamp((int)(MathF.Sqrt(MathF.Pow(q.X - p.X, 2) + MathF.Pow(q.Z - p.Z, 2)) / 8), 1, 200);
            for (int k = 0; k < steps; k++)
            {
                float x = p.X + (q.X - p.X) * k / steps, z = p.Z + (q.Z - p.Z) * k / steps;
                GL.Vertex3(x, GroundAt(x, z) + 1, -z);
            }
        }
        GL.End();
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }

    internal void CloseToolForShot() { _zDirty = false; CloseTool(); }

    /// <summary>--world-shot: o caminho da tela numa cópia em pasta temporária. Não abre pergunta: usa uma zona sem nada a perguntar.</summary>
    internal string ZoneFlowTest(string folder)
    {
        var o = new List<string>();
        void Check(string name, bool ok) => o.Add((ok ? "OK " : "FALHA ") + "tela-" + name);
        var real = _t.Dir;
        File.Copy(Path.Combine(real, "precinct.clt"), Path.Combine(folder, "precinct.clt"), true);
        OpenTool(Tool.Zones);
        _zones = ZonePair.Open(folder, null);
        ZoneReload();
        int i = ZoneIndexes.First(k => _zw.Areas[k].Name.Length > 0 && _zw.Areas[k].Music.All(m => File.Exists(Path.Combine(ElementOfMap, m))) && ZoneEdit.Parts(_zw, k).Count == 1);
        var was = _zw.Areas[i].Name;
        OpenZone(i);
        Check("abre", _zName.Text == was && _zNameCard.Visible == _zFoot.Visible);
        _zName.Text = "Teste de tela";
        Check("muda", _zw.Areas[i].Name == "Teste de tela" && _zDirty && _zSave.Enabled);
        UndoAny();
        Check("desfaz", _zw.Areas[i].Name == was && !_zDirty);
        RedoAny();
        Check("refaz", _zw.Areas[i].Name == "Teste de tela" && _zDirty);
        SaveZones();
        Check("salva", !_zDirty && PrecinctFile.Read(Path.Combine(folder, "precinct.clt"))!.Areas[i].Name == "Teste de tela");
        UndoAny();
        Check("desfaz-salvo", PrecinctFile.Read(Path.Combine(folder, "precinct.clt"))!.Areas[i].Name == was && _zw.Areas[i].Name == was);
        RedoAny();
        Check("refaz-salvo", PrecinctFile.Read(Path.Combine(folder, "precinct.clt"))!.Areas[i].Name == "Teste de tela");
        int before = _zw.Areas.Count;
        StartNewZone();
        _zPoints = [(10, 0, 10), (60, 0, 10), (60, 0, 60)];
        FinishDrawing();
        Check("nova", _zw.Areas.Count == before + 1 && _zOpen == before && _zNameCard.Visible == _zFoot.Visible);
        UndoAny();
        Check("desfaz-nova", _zw.Areas.Count == before);
        _zOpen = -1;
        UpdateZonePanel();
        Check("lista", _zList.Items.Count > 0);
        CloseToolForShot();
        return string.Join(Environment.NewLine, o);
    }

    /// <summary>--world-shot: o DrawToBitmap da janela não respeita a ordem dos filhos, então o painel sai em imagem própria.</summary>
    internal Bitmap ToolsShot()
    {
        var b = new Bitmap(_tools.Width, _tools.Height);
        _tools.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
        return b;
    }

    internal string ZoneOrder() => $"painel {Controls.GetChildIndex(_tools)} mapa {Controls.GetChildIndex(_zCanvas)} (menor fica por cima)";

    /// <summary>Capturas do --world-shot: 0 lista, 1 zona aberta com zoom.</summary>
    internal void ZoneShot(int kind)
    {
        OpenTool(Tool.Zones);
        if (kind == 0) return;
        var named = ZoneIndexes.Where(i => _zw.Areas[i].Name.Length > 0).OrderByDescending(i => ZoneEdit.Parts(_zw, i).Count).FirstOrDefault();
        OpenZone(named);
        FrameZone();
    }
}
