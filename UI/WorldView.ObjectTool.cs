using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using MyAceEditor.PerfectWorld.WorldEdit;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta "Objetos do mundo" (spec docs/superpowers/specs/2026-11-07-objetos-do-mundo-design.md): rascunho em
/// memória dos 9 pedaços de 1 km em volta da câmera, lidos em segundo plano (DraftLoader) e soltos quando ficam longe e
/// sem mudança. Nada é gravado até o Salvar.
/// </summary>
internal sealed partial class WorldView
{
    WorldDraft? _draft;
    DraftSources? _draftSrc;
    readonly HashSet<int> _draftAsked = new();
    int[] _draftWanted = [];

    /// <summary>Pedaços 3x3 em volta da câmera (os mesmos das construções).</summary>
    List<int> PiecesAroundCamera()
    {
        var pieces = new List<int>();
        int piece = _t.PieceAt(_cam.X, -_cam.Z);
        if (piece <= 0) return pieces;
        int pr = (piece - 1) / _t.Cols, pc = (piece - 1) % _t.Cols;
        for (int r = pr - 1; r <= pr + 1; r++)
            for (int c = pc - 1; c <= pc + 1; c++)
                if (r >= 0 && c >= 0 && r < _t.Rows && c < _t.Cols) pieces.Add(r * _t.Cols + c + 1);
        return pieces;
    }

    /// <summary>Abre o rascunho (uma vez por mapa) e pede os pedaços em volta da câmera.</summary>
    void DraftOpen()
    {
        if (_element is null) { _status.Text = Loc.T("obj.noClient", "Pasta do cliente não encontrada; os objetos ficam só para olhar."); return; }
        try
        {
            _draftSrc ??= new DraftSources(_element, _t.Name, ServerConfigDir, _t.PieceAt, _t.Rows * _t.Cols);
            if (_draft is null) { _draft = new WorldDraft(_t.Name); _draft.Changed += () => { if (!IsDisposed) _gl.Invalidate(); }; _buildings.DraftOf = k => _draft?.Find(k); _buildings.Extras = DraftExtras; }
            DraftWant(PiecesAroundCamera());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("objetos: abrir", ex);
            _status.Text = Loc.T("obj.openFail", "Não deu para abrir os objetos deste mapa; veja o log.");
        }
    }

    /// <summary>Solta os pedaços longe e limpos e pede ao carregador os que faltam (resposta volta na thread da tela).</summary>
    void DraftWant(IReadOnlyCollection<int> pieces)
    {
        if (_draft is null || _draftSrc is null) return;
        _draftWanted = [.. pieces];
        _draft.Retain(pieces);
        var src = _draftSrc;
        foreach (int p in pieces)
        {
            if (_draft.Pieces.ContainsKey(p) || !_draftAsked.Add(p)) continue;
            int piece = p;
            _load.Work(() => DraftLoader.ReadPiece(src, piece), got =>
            {
                _draftAsked.Remove(piece);
                if (IsDisposed || _draft is null || !_draftWanted.Contains(got.Id)) return;
                _draft.Put(got);
                DraftStatus();
            });
        }
        DraftStatus();
    }

    void DraftStatus()
    {
        if (_draft is null || _tool != Tool.Scene) return;
        int ready = _draftWanted.Count(_draft.Pieces.ContainsKey), total = _draftWanted.Length;
        _status.Text = ready < total ? string.Format(Loc.T("obj.loading", "Carregando objetos… {0}/{1}"), ready, total)
            : string.Format(Loc.T("obj.ready", "Objetos prontos: {0} pedaços em volta da câmera."), total);
    }

    /// <summary>Para a captura (--world-shot, nome "dftload"): abre a ferramenta, espera os pedaços e resume.</summary>
    internal string DraftLoadTest()
    {
        ServerConfigDir ??= Environment.GetEnvironmentVariable("MYACE_SERVER");
        OpenTool(Tool.Scene);
        var until = DateTime.Now.AddSeconds(60);
        while (DateTime.Now < until && (_draft is null || _draftWanted.Any(p => !_draft.Pieces.ContainsKey(p)))) { Application.DoEvents(); Thread.Sleep(30); }
        int ready = _draft is null ? 0 : _draftWanted.Count(_draft.Pieces.ContainsKey);
        var objs = _draft?.Pieces.Values.Sum(p => p.Objects.Count) ?? 0;
        return $"dftload: pedaços {ready}/{_draftWanted.Length} ({string.Join(",", _draftWanted)}), objetos {objs}, aviso \"{_status.Text}\"";
    }

    /// <summary>Para a captura ("dftmove"): ergue 15 m a construção mais perto, confere o centro e o Ctrl+Z, e deixa erguida para a foto.</summary>
    internal string DraftMoveTest()
    {
        string load = DraftLoadTest();
        if (_buildings.Nearest(_cam) is not { } near || _draft?.Find(near.File) is not { } o) return load + " | dftmove: nenhuma construção carregada";
        var before = near.Center;
        string? why = DraftApply(new MoveOp(o.Key, o.Original with { Pos = o.Original.Pos + new System.Numerics.Vector3(0, 15, 0) }));
        var after = _buildings.Nearest(_cam + new OpenTK.Mathematics.Vector3(0, 15, 0));
        bool up = after is { } a && a.File == near.File && MathF.Abs(a.Center.Y - before.Y - 15) < 0.01f;
        bool undo = ObjectUndo(false) && _buildings.Nearest(_cam) is { } b && MathF.Abs(b.Center.Y - before.Y) < 0.01f;
        bool redo = ObjectUndo(true);
        return load + $" | dftmove: {System.IO.Path.GetFileName(near.File)} recusa={why ?? "nenhuma"} subiu15={up} ctrlz={undo} ctrly={redo} sujo={_draft.Dirty}";
    }

    /// <summary>Marca de ordem de um passo do rascunho: profundidade das outras pilhas de desfazer naquele momento.</summary>
    sealed record DraftMark(int Npc, int Quests, int Scene);

    /// <summary>Aplica no rascunho com a marca de ordem; recusa vira aviso no rodapé.</summary>
    string? DraftApply(DraftOp op)
    {
        if (_draft is null) return _status.Text = Loc.T("obj.noDraft", "Abra a ferramenta Objetos do mundo para mexer nos objetos.");
        var why = _draft.Apply(op, new DraftMark(UndoDepth?.Invoke() ?? 0, _qUndo.Count, _sUndo.Count));
        if (why is not null) _status.Text = why;
        UpdateSideState();
        return why;
    }

    /// <summary>Ctrl+Z/Ctrl+Y do rascunho: só quando o passo do topo foi a última ação (as outras pilhas não andaram depois).</summary>
    bool ObjectUndo(bool redo)
    {
        if (_draft?.UndoTag is null && _draft?.RedoTag is null) return false;
        if ((redo ? _draft!.RedoTag : _draft!.UndoTag) is not DraftMark m) return false;
        if (m.Npc != (UndoDepth?.Invoke() ?? 0) || m.Quests != _qUndo.Count || m.Scene != _sUndo.Count) return false;
        bool ok = redo ? _draft.Redo() : _draft.Undo();
        if (ok) { _status.Text = redo ? Loc.T("obj.redone", "Objetos: refeito.") : Loc.T("obj.undone", "Objetos: desfeito (Ctrl+Y refaz)."); ShowObjPanel(); UpdateSideState(); _gl.Invalidate(); }
        return ok;
    }

    /// <summary>Modelos .ecm fixos do cenário (do .ecwld), parados, dos pedaços abertos no rascunho.</summary>
    void DrawDraftModels()
    {
        if (_draft is null || _tool != Tool.Scene) return;
        foreach (var piece in _draft.Pieces.Values)
            foreach (var o in piece.Objects.Concat(piece.Added))
            {
                if (o.Kind != ObjKind.Model || o.Deleted || o.Source.Length == 0) continue;
                var t = o.Now;
                _models.Draw(o.Source, new OpenTK.Mathematics.Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z), MathF.Atan2(t.Dir.X, t.Dir.Z) * 180 / MathF.PI);
            }
    }

    // ---------------- tela: escolher, cartão, mexer, apagar, salvar (Task 7) ----------------
    sealed record ModelSel(string Key);
    readonly NumericUpDown _objScale = new() { Minimum = 0.1m, Maximum = 10, DecimalPlaces = 2, Width = 64, Increment = 0.1m };
    readonly NumericUpDown _objYaw = new() { Minimum = -720, Maximum = 720, DecimalPlaces = 0, Width = 64, Increment = 15 };
    readonly CheckBox _stickGround = new() { Text = "", Checked = true, AutoSize = true, ForeColor = Color.White };
    readonly CheckBox _showCollision = new() { Text = "", Checked = false, AutoSize = true, ForeColor = Color.White };

    /// <summary>Objeto do rascunho por trás da seleção (construção ou modelo); null para efeito, som e bichos.</summary>
    DraftObject? SelDraft => _sceneSel switch
    {
        BuildingSel b => _draft?.Find(b.File),
        ModelSel m => _draft?.Find(m.Key),
        TreeSel t => _draft?.Find(t.Key),
        _ => null,
    };

    static System.Numerics.Vector3 CenterOf(DraftObject o) { var (mn, mx) = DraftChecks.WorldBox(o); return (mn + mx) / 2; }
    static float YawOf(ObjTransform t) => MathF.Atan2(t.Dir.X, t.Dir.Z) * 180 / MathF.PI;

    /// <summary>Monta os controles novos do cartão (giro, grudar no chão, ver colisão) dentro do painel do objeto.</summary>
    void InitObjectCard()
    {
        _stickGround.Text = Loc.T("obj.stick", "Grudar no chão");
        _showCollision.Text = Loc.T("obj.showCollision", "Ver colisão");
        _showCollision.CheckedChanged += (_, _) => _gl.Invalidate();
        _objYaw.ValueChanged += (_, _) => { if (!_objBinding) TurnSelected((float)_objYaw.Value); };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, BackColor = _objPanel.BackColor, Padding = new Padding(0, 2, 0, 0) };
        row.Controls.Add(new Label { Text = Loc.T("obj.turn", "giro (graus)"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        row.Controls.Add(_objYaw);
        row.Controls.Add(new Label { Text = Loc.T("obj.scaleLabel", "tamanho"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        row.Controls.Add(_objScale);
        _objScale.ValueChanged += (_, _) =>
        {
            if (_objBinding || SelDraft is not { Kind: ObjKind.Building, Deleted: false } o) return;
            if (DraftApply(new MoveOp(o.Key, o.Now with { Scale = (float)_objScale.Value })) is null)
                _status.Text = string.Format(Loc.T("obj.scaled", "Tamanho {0:0.##}× (ainda não gravado)"), _objScale.Value);
            ShowObjPanel(); _gl.Invalidate();
        };
        var opts = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, BackColor = _objPanel.BackColor };
        opts.Controls.Add(_stickGround); opts.Controls.Add(_showCollision);
        _objPanel.Controls.Add(opts); _objPanel.Controls.Add(row);
    }

    /// <summary>Texto do cartão de uma construção ou modelo: nome, tamanho, quantas vezes aparece, colisão, escala.</summary>
    string CardText(DraftObject o)
    {
        var (mn, mx) = DraftChecks.WorldBox(o);
        var size = mx - mn;
        int same = _draft?.Pieces.Values.SelectMany(p => p.Objects).Count(x => !x.Deleted && x.Kind == o.Kind && string.Equals(Path.GetFileName(x.Source), Path.GetFileName(o.Source), StringComparison.OrdinalIgnoreCase)) ?? 1;
        string kind = o.Kind == ObjKind.Building ? Loc.T("world.objBuilding", "Construção") : o.Kind == ObjKind.Tree ? Loc.T("obj.tree", "Árvore") : Loc.T("obj.model", "Modelo");
        var lines = new List<string> { kind + ": " + Path.GetFileName(o.Source) };
        if (size.LengthSquared() > 0) lines.Add(string.Format(Loc.T("obj.size", "tamanho {0:0.0} × {1:0.0} × {2:0.0} m (largura × altura × profundidade)"), size.X, size.Y, size.Z));
        lines.Add(string.Format(Loc.T("obj.count", "aparece {0} vez(es) nos pedaços abertos"), same));
        lines.Add(string.Format(Loc.T("obj.collision2", "colisão: {0}"), o.Solid ? Loc.T("obj.yes", "sim") : Loc.T("obj.no", "não")) + (o.CopyOf is not null ? " · " + Loc.T("obj.isNew", "nova (cópia)") : ""));
        if (o.ReadOnlyWhy is not null) lines.Add(o.ReadOnlyWhy);
        else if (o.Deleted) lines.Add(Loc.T("obj.deletedLine", "apagado (Ctrl+Z traz de volta)"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Leva o centro do objeto escolhido para (x, y, z) do jogo; com "Grudar no chão", a altura segue o terreno.</summary>
    void MoveSelectedCenter(float x, float y, float z, bool? stick = null)
    {
        if (SelDraft is not { } o) { _status.Text = Loc.T("obj.notLoaded", "Este objeto ainda está carregando; tente de novo em instantes."); return; }
        var c = CenterOf(o);
        var pos = o.Now.Pos + new System.Numerics.Vector3(x - c.X, y - c.Y, z - c.Z);
        if ((stick ?? _stickGround.Checked) && _t.HeightAt(pos.X, pos.Z) is { } g && _t.HeightAt(o.Original.Pos.X, o.Original.Pos.Z) is { } g0)
            pos.Y = g + (o.Original.Pos.Y - g0);
        if (DraftApply(new MoveOp(o.Key, o.Now with { Pos = pos })) is null)
            _status.Text = string.Format(Loc.T("obj.moved", "Objeto movido para X {0:0.0} Z {1:0.0} altura {2:0.0} (ainda não gravado)"), pos.X, pos.Z, pos.Y);
        ShowObjPanel(); _gl.Invalidate();
    }

    /// <summary>Gira o objeto escolhido até o giro pedido (graus, em volta do eixo vertical e do centro do desenho).</summary>
    void TurnSelected(float yaw)
    {
        if (SelDraft is not { } o || o.Deleted) return;
        float delta = (yaw - YawOf(o.Now)) * MathF.PI / 180;
        if (MathF.Abs(delta) < 1e-5f) return;
        var r = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, delta);
        var c = CenterOf(o);
        var t = o.Now with
        {
            Pos = System.Numerics.Vector3.Transform(o.Now.Pos - c, r) + c,
            Dir = System.Numerics.Vector3.Transform(o.Now.Dir, r),
            Up = System.Numerics.Vector3.Transform(o.Now.Up, r),
        };
        if (DraftApply(new MoveOp(o.Key, t)) is null) _status.Text = string.Format(Loc.T("obj.turned", "Objeto girado para {0:0}° (ainda não gravado)"), yaw);
        ShowObjPanel(); _gl.Invalidate();
    }

    void DeleteSelected()
    {
        if (SelDraft is not { } o) return;
        if (DraftApply(new DeleteOp(o.Key)) is null) _status.Text = string.Format(Loc.T("obj.deleted", "{0} apagado (Ctrl+Z traz de volta; só grava no Salvar)."), Path.GetFileName(o.Source));
        ShowObjPanel(); _gl.Invalidate();
    }

    /// <summary>Modelo do cenário mais perto do clique (até 14 px na tela).</summary>
    ModelSel? PickModel(Point p)
    {
        if (_draft is null) return null;
        ModelSel? best = null; float bd = 14;
        foreach (var o in _draft.Pieces.Values.SelectMany(x => x.Objects).Where(o => o.Kind == ObjKind.Model && !o.Deleted))
        {
            var g = new Vector3(o.Now.Pos.X, o.Now.Pos.Y, o.Now.Pos.Z);
            if ((Gl(g.X, g.Y, g.Z) - _cam).LengthSquared > 300 * 300 || Project(g) is not { } q) continue;
            float d = MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            if (d < bd) { bd = d; best = new ModelSel(o.Key); }
        }
        return best;
    }

    static readonly int[] BoxEdges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };

    /// <summary>Contorno (caixa amarela) do escolhido e, com "Ver colisão", as caixas das construções sólidas perto.</summary>
    void DrawObjectBoxes()
    {
        if (_draft is null || _tool != Tool.Scene) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        void Box(DraftObject o, Color c, float w)
        {
            var (mn, mx) = DraftChecks.WorldBox(o);
            if (mn == mx) { mn -= new System.Numerics.Vector3(1); mx += new System.Numerics.Vector3(1); }
            GL.LineWidth(w); GL.Color3(c);
            GL.Begin(PrimitiveType.Lines);
            foreach (int v in BoxEdges) GL.Vertex3((v & 1) == 0 ? mn.X : mx.X, (v & 2) == 0 ? mn.Y : mx.Y, -((v & 4) == 0 ? mn.Z : mx.Z));
            GL.End();
        }
        if (_showCollision.Checked)
            foreach (var o in _draft.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Solid && !o.Deleted))
                if ((Gl(o.Now.Pos.X, o.Now.Pos.Y, o.Now.Pos.Z) - _cam).LengthSquared < 150 * 150) Box(o, Color.FromArgb(255, 90, 90), 1);
        if (SelDraft is { Deleted: false } sel && sel.Kind != ObjKind.Building) Box(sel, Color.Gold, 2.5f);
        GL.LineWidth(1); GL.Color3(Color.White); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Salvar único (construções, modelos e o cenário do SceneEdit): resumo, conferências, grava tudo ou nada e oferece
    /// enviar ao servidor quando o map.bht mudou. Falso = nada gravado (o rascunho continua).
    /// </summary>
    bool SaveObjects(bool ask = true)
    {
        if (_draft is null || _draftSrc is null) return false;
        SavePlan plan;
        try
        {
            plan = DraftSaver.Plan(_draft, _draftSrc, _ecw?.Write(), _bsd);
            if (_ecwPath is not null && _ecw is not null && !plan.Files.ContainsKey(_ecwPath) && _ecwSaved is not null && !_ecw.Write().AsSpan().SequenceEqual(_ecwSaved)) plan.Files[_ecwPath] = _ecw.Write();
            if (_bsdPath is not null && _bsd is not null && !plan.Files.ContainsKey(_bsdPath) && _bsdSaved is not null && !_bsd.AsSpan().SequenceEqual(_bsdSaved)) plan.Files[_bsdPath] = (byte[])_bsd.Clone();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("objetos: montar o salvar", ex);
            if (ask) MessageBox.Show(this, ex.Message, Loc.T("obj.saveTitle", "Salvar objetos"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = ex.Message;
            return false;
        }
        if (plan.Empty) { _status.Text = Loc.T("obj.nothing", "Nada para salvar."); return false; }
        var points = _allMarks.Select(m => (new System.Numerics.Vector3(m.X, m.Y, m.Z), m.Name));
        var checks = DraftChecks.Run(_draft, _draftSrc, (x, z) => _t.HeightAt(x, z) ?? float.MinValue / 2, points);
        var blocks = checks.Where(c => c.Blocks).ToList();
        if (blocks.Count > 0)
        {
            string text = Loc.T("obj.blocked", "Não dá para salvar ainda:") + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", blocks.Select(c => c.Text));
            if (ask) MessageBox.Show(this, text, Loc.T("obj.saveTitle", "Salvar objetos"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = text;
            FlyTo(blocks[0].Key);
            return false;
        }
        if (ask)
        {
            string summary = string.Join(Environment.NewLine, plan.Summary.DefaultIfEmpty(Loc.T("obj.sceneOnly", "Mudanças nos efeitos, sons ou bichos do cenário.")));
            if (plan.PckFiles.Count > 0 || plan.Files.Keys.Any(k => k.EndsWith("map.bht", StringComparison.OrdinalIgnoreCase)))
                summary += Environment.NewLine + Loc.T("obj.shadowNote", "A sombra pintada no chão (mapa de luz do terreno) fica onde estava; ela será refeita com a ferramenta de terreno.");
            string warn = checks.Count == 0 ? "" : Environment.NewLine + Environment.NewLine + Loc.T("obj.warnings", "Avisos (Não = cancelar e ir até o primeiro):") + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", checks.Select(c => c.Text));
            var answer = MessageBox.Show(this, Loc.T("obj.confirm", "Vai gravar:") + Environment.NewLine + summary + warn + Environment.NewLine + Environment.NewLine + Loc.T("obj.confirmQ", "Salvar agora?"),
                Loc.T("obj.saveTitle", "Salvar objetos"), MessageBoxButtons.YesNo, checks.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) { if (checks.Count > 0) FlyTo(checks[0].Key); return false; }
        }
        var res = DraftSaver.Write(plan, _draftSrc);
        if (res.Kind is not (SaveKind.Both or SaveKind.ClientOnly))
        {
            if (ask) MessageBox.Show(this, res.Message, Loc.T("obj.saveTitle", "Salvar objetos"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = res.Message;
            return false;
        }
        // tudo gravado: o cenário do SceneEdit passa a ser o gravado; pacote e rascunho relidos do disco
        if (_ecwPath is not null && plan.Files.TryGetValue(_ecwPath, out var ew)) { _ecw = new EcwldFile(ew); _ecwSaved = ew; _objScene = ScanScene(ew); }
        if (_bsdPath is not null && plan.Files.TryGetValue(_bsdPath, out var bw)) { _bsd = (byte[])bw.Clone(); _bsdSaved = (byte[])bw.Clone(); }
        bool server = plan.Files.Keys.Any(k => k.EndsWith("map.bht", StringComparison.OrdinalIgnoreCase));
        try { if (_element is not null && plan.PckFiles.Count > 0) _buildings.Reopen(_element); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("objetos: reabrir litmodels.pck", ex); }
        ObjectsSaved();
        DraftOpen();
        UpdateSideState();
        _status.Text = res.Message;
        if (ask) BeginInvoke(() => SavedDialog.Show(this, res.Message, server ? SaveAndSendAll : null));
        return true;
    }

    /// <summary>Para a captura ("dftcard"): escolhe a construção mais perto e usa o cartão (girar, mover, apagar, desfazer); não grava.</summary>
    internal string DraftCardTest(string outDir)
    {
        string load = DraftLoadTest();
        if (_buildings.Nearest(_cam) is not { } near || _draft?.Find(near.File) is not { } o) return load + " | dftcard: nada carregado";
        _sceneSel = new BuildingSel(near.File, near.Center); ShowObjPanel();
        float yaw0 = YawOf(o.Now);
        _objYaw.Value = (decimal)Math.Round(yaw0 + 90);
        bool turned = MathF.Abs(((YawOf(o.Now) - yaw0 + 540) % 360) - 180 - 90) < 1 || MathF.Abs(YawOf(o.Now) - yaw0 - 90) < 1 || MathF.Abs(YawOf(o.Now) - yaw0 + 270) < 1;
        var c0 = CenterOf(o);
        MoveSelectedCenter(c0.X + 5, c0.Y, c0.Z);
        bool moved = MathF.Abs(CenterOf(o).X - c0.X - 5) < 0.05f;
        var checks = _draftSrc is null ? [] : DraftChecks.Run(_draft, _draftSrc, (x, z) => _t.HeightAt(x, z) ?? 0, _allMarks.Select(m => (new System.Numerics.Vector3(m.X, m.Y, m.Z), m.Name)));
        SavePlan? plan = null; string planErr = "";
        try { plan = _draftSrc is null ? null : DraftSaver.Plan(_draft, _draftSrc, _ecw?.Write(), _bsd); } catch (InvalidDataException ex) { planErr = ex.Message; }
        string card = _objTitle.Text.Replace(Environment.NewLine, " / ");
        using (var shot = ToolsShot()) shot.Save(System.IO.Path.Combine(outDir, "dftcard-panel.png"), System.Drawing.Imaging.ImageFormat.Png);
        _scenePage.AutoScrollPosition = new Point(0, 100000); Application.DoEvents();
        using (var shot2 = ToolsShot()) shot2.Save(System.IO.Path.Combine(outDir, "dftcard-panel-end.png"), System.Drawing.Imaging.ImageFormat.Png);
        int inner = _objPanel.Controls.Cast<Control>().Where(c => c.Visible).Sum(c => c.Height + c.Margin.Vertical);
        _scenePage.AutoScrollPosition = new Point(0, 0);
        DeleteSelected();
        bool deleted = o.Deleted;
        bool undo = ObjectUndo(false) && !o.Deleted;
        return load + $" | dftcard: {System.IO.Path.GetFileName(near.File)} girou={turned} moveu5={moved} apagou={deleted} ctrlz={undo} conferencias={checks.Count} plano=[{string.Join(" ", plan?.Summary ?? [])}] arquivos={plan?.Files.Count}+{plan?.PckFiles.Count} {planErr} cartão=\"{card}\" painel {_objPanel.Height} px para {inner} px de conteúdo";
    }

    /// <summary>Depois de gravar: solta a seleção e o rascunho (relido do disco no DraftOpen).</summary>
    void ObjectsSaved()
    {
        _sceneSel = null; ShowObjPanel();
        _draftSrc?.Dispose(); _draftSrc = null; _draft = null; _draftAsked.Clear();
        // as cópias do Ctrl+Z do cenário são de ANTES do salvar (traziam de volta construção apagada, modelo...): saem
        _sUndo.Clear(); _sRedo.Clear();
    }

    /// <summary>Para a captura ("dftfix"): Ctrl+Z do cenário depois de salvar e fechar a janela com salvar recusado; não grava.</summary>
    internal string DraftFixTest()
    {
        string load = DraftLoadTest();
        // 2) uma mudança no cenário guarda cópia; depois de "salvar", o Ctrl+Z do cenário não pode voltar a cópia velha
        string stale = "sem efeito";
        if (ObjScene?.Effects.FirstOrDefault() is { } fx)
        {
            _sceneSel = fx; ShowObjPanel();
            _objXYZ[0].Value += 1;
            ObjectsSaved(); DraftOpen();
            bool undid = SceneUndo(false);
            stale = $"ctrlz-depois-de-salvar={undid}";
        }
        // 3) fechar com Sim e o salvar recusado (construção fora do pedaço): o fechamento tem que ser cancelado
        string close = "sem construção";
        DraftLoadTest();
        if (_buildings.Nearest(_cam) is { } nb && _draft?.Find(nb.File) is { } o)
        {
            _draft.Apply(new MoveOp(o.Key, o.Original with { Pos = o.Original.Pos + new System.Numerics.Vector3(5000, 0, 0) }));
            close = $"cancela-fechar={CancelClose(DialogResult.Yes, interactive: false)}";
            _draft.Undo();
        }
        return load + $" | dftfix: {stale} {close}";
    }

    /// <summary>Para a captura ("dftpaste"): copia a construção mais perto, cola 10 m ao lado, clica na cópia, Ctrl+Z/Ctrl+Y, tamanho 1,5×; não grava.</summary>
    internal string DraftPasteTest()
    {
        string load = DraftLoadTest();
        if (_buildings.Nearest(_cam) is not { } nb || _draft?.Find(nb.File) is not { } o) return load + " | dftpaste: nada carregado";
        _sceneSel = new BuildingSel(nb.File, Vector3.Zero); _objClip = _sceneSel;
        var c = CenterOf(o);
        string? msg = PasteAt(c + new System.Numerics.Vector3(10, 0, 0));
        var copy = SelDraft;
        bool made = copy is { CopyOf: not null } && !copy.Deleted;
        string click = "-";
        if (made && Project(V3(CenterOf(copy!))) is { } q) { var r = MouseRay(Point.Round(q)); click = _buildings.RayPick(r.O, r.D)?.File ?? "nada"; }
        bool undo = ObjectUndo(false) && copy!.Deleted;
        bool redo = ObjectUndo(true) && !copy!.Deleted;
        _sceneSel = new BuildingSel(copy!.Key, Vector3.Zero); ShowObjPanel();
        var (mn0, mx0) = DraftChecks.WorldBox(copy); float s0 = copy.Now.Scale;
        _objScale.Value = 1.5m;
        var (mn1, mx1) = DraftChecks.WorldBox(copy);
        bool scaled = MathF.Abs((mx1.Y - mn1.Y) / MathF.Max(0.01f, mx0.Y - mn0.Y) - 1.5f / s0) < 0.02f && copy.Now.Scale == 1.5f;
        string sum = "";
        try { if (_draftSrc is not null) sum = string.Join(" / ", DraftSaver.Plan(_draft, _draftSrc, _ecw?.Write(), _bsd).Summary); } catch (InvalidDataException ex) { sum = ex.Message; }
        return load + $" | dftpaste: {msg} criou={made} clique={System.IO.Path.GetFileName(click)} (cópia {copy.Key}) ctrlz={undo} ctrly={redo} tamanho1,5={scaled} plano=\"{sum}\"";
    }

    /// <summary>Leva a câmera até o objeto (conferência "levar até ele").</summary>
    void FlyTo(string key)
    {
        if (_draft?.Find(key) is not { } o) return;
        var c = CenterOf(o);
        SetCamera(new Vector3(c.X - 20, c.Y + 15, c.Z + 20), 45, -25);
    }

    sealed record TreeSel(string Key);
    readonly CheckBox _showScenePoints = new() { Text = "", Checked = false, AutoSize = true, ForeColor = Color.White };

    /// <summary>Distância do raio (OpenGL) até um cilindro em pé (base, raio, altura); null se não bate.</summary>
    static float? RayCylinder(Vector3 o, Vector3 d, Vector3 b, float r, float h)
    {
        float ox = o.X - b.X, oz = o.Z - b.Z;
        float A = d.X * d.X + d.Z * d.Z, B = 2 * (ox * d.X + oz * d.Z), C = ox * ox + oz * oz - r * r;
        float best = float.MaxValue;
        if (A > 1e-8f)
        {
            float disc = B * B - 4 * A * C;
            if (disc >= 0)
                foreach (float t in new[] { (-B - MathF.Sqrt(disc)) / (2 * A), (-B + MathF.Sqrt(disc)) / (2 * A) })
                {
                    float y = o.Y + d.Y * t;
                    if (t > 0 && y >= b.Y && y <= b.Y + h) best = MathF.Min(best, t);
                }
        }
        if (MathF.Abs(d.Y) > 1e-8f)
            foreach (float cy in new[] { b.Y, b.Y + h })
            {
                float t = (cy - o.Y) / d.Y;
                float x = o.X + d.X * t - b.X, z = o.Z + d.Z * t - b.Z;
                if (t > 0 && x * x + z * z <= r * r) best = MathF.Min(best, t);
            }
        return best == float.MaxValue ? null : best;
    }



    // ---------------- clique pela forma real (pedido 07/11 noite) ----------------
    /// <summary>O que o clique escolheria nesse ponto da tela, como texto ("bld:arquivo", "tree:i", "fx:id"...); null = nada.</summary>
    internal string? PickAt(Point p)
    {
        PickSceneObject(p);
        return _sceneSel switch
        {
            BuildingSel b => "bld:" + b.File,
            ModelSel m => "model:" + m.Key,
            TreeSel t => t.Key,
            WorldScene.SceneEffect fx => "fx:" + fx.Id,
            WorldScene.SceneSound sd => "snd:" + sd.Id,
            WorldScene.Critter cr => "crit:" + cr.Offset,
            _ => null,
        };
    }

    /// <summary>Para a captura ("dftpick"): clica na malha da construção mais perto (longe do centro), numa árvore e num efeito na frente.</summary>
    internal string DraftPickTest()
    {
        string load = DraftLoadTest();
        var lines = new List<string>();
        _showScenePoints.Checked = true;   // os efeitos começam desligados; o teste liga
        // 1) três pontos da malha da construção mais perto, a mais de 40 px do centro dela na tela
        if (_buildings.Nearest(_cam) is { } near && _buildings.MeshOf(near.File) is { } mesh)
        {
            var center = Project(near.Center);
            int tried = 0, same = 0; var got = new List<string>();
            for (int t = 0; t + 2 < mesh.Tri.Length && tried < 3; t += 3 * Math.Max(1, mesh.Tri.Length / 3 / 400))
            {
                var c = Vector3.Zero;
                for (int k = 0; k < 3; k++) { int i = mesh.Tri[t + k] * 3; c += new Vector3(mesh.Pos[i], mesh.Pos[i + 1], mesh.Pos[i + 2]); }
                var gl = Vector3.TransformPosition(c / 3, mesh.World);
                var game = new Vector3(gl.X, gl.Y, -gl.Z);
                if (Project(game) is not { } q || q.X < 5 || q.Y < 5 || q.X > _gl.Width - 5 || q.Y > _gl.Height - 5) continue;
                if (center is { } cc && MathF.Abs(q.X - cc.X) + MathF.Abs(q.Y - cc.Y) < 40) continue;
                var hit = _buildings.RayPick(MouseRay(Point.Round(q)).O, MouseRay(Point.Round(q)).D);
                if (hit?.File != near.File) continue;   // outra coisa na frente desse ponto: não serve de prova
                tried++;
                var k2 = PickAt(Point.Round(q)); got.Add(k2 ?? "nada");
                if (k2 == "bld:" + near.File) same++;
            }
            lines.Add($"malha {System.IO.Path.GetFileName(near.File)}: {same}/{tried} cliques escolheram a construção ({string.Join(", ", got.Select(g => System.IO.Path.GetFileName(g)))})");
        }
        else lines.Add("malha: sem construção");
        // 2) árvore
        var trees = _draft?.Pieces.Values.SelectMany(x => x.Objects).Where(o => o.Kind == ObjKind.Tree).OrderBy(o => (Gl(o.Now.Pos.X, o.Now.Pos.Y, o.Now.Pos.Z) - _cam).LengthSquared).Take(8).ToList() ?? [];
        string treeOk = "nenhuma";
        foreach (var tr in trees)
            if (Project(new Vector3(tr.Now.Pos.X, tr.Now.Pos.Y + 3, tr.Now.Pos.Z)) is { } tq && PickAt(Point.Round(tq)) == tr.Key) { treeOk = tr.Key; break; }
        lines.Add($"árvore: {treeOk} (de {trees.Count} perto)");
        // 3) efeito na frente de tudo
        string fxOk = "nenhum";
        if (ObjScene is { } sc)
            foreach (var fx in sc.Effects.OrderBy(f => (Gl(f.X, f.Y, f.Z) - _cam).LengthSquared).Take(400))
            {
                if ((Gl(fx.X, fx.Y, fx.Z) - _cam).LengthSquared > 290 * 290) continue;
                if (Project(new Vector3(fx.X, fx.Y, fx.Z)) is not { } fq || fq.X < 0 || fq.Y < 0 || fq.X > _gl.Width || fq.Y > _gl.Height) continue;
                var r = MouseRay(Point.Round(fq));
                float dfx = (Gl(fx.X, fx.Y, fx.Z) - r.O).Length;
                if (_buildings.RayPick(r.O, r.D) is { } bh && bh.Dist < dfx - 3) continue;
                fxOk = PickAt(Point.Round(fq)) == "fx:" + fx.Id ? "fx:" + fx.Id : "errado:" + PickAt(Point.Round(fq));
                break;
            }
        int inView = 0; string firstFx = "";
        if (ObjScene is { } sc2)
            foreach (var fx in sc2.Effects)
            {
                var gfx = Gl(fx.X, fx.Y, fx.Z);
                if ((gfx - _cam).LengthSquared > 290 * 290 || Project(new Vector3(fx.X, fx.Y, fx.Z)) is not { } fq2 || fq2.X < 0 || fq2.Y < 0 || fq2.X > _gl.Width || fq2.Y > _gl.Height) continue;
                inView++;
                if (firstFx.Length == 0) { var r2 = MouseRay(Point.Round(fq2)); firstFx = $"efeito a {(gfx - r2.O).Length:0.0} m, construção a {_buildings.RayPick(r2.O, r2.D)?.Dist:0.0} m"; }
            }
        lines.Add($"efeito na frente: {fxOk} ({inView} efeitos na vista a até 290 m; 1o: {firstFx})");
        // 4) efeito de perto: câmera a ~17 m dele (como o "levar até ele"); o clique em cima do ponto escolhe o efeito
        string near4 = "sem efeito";
        if (ObjScene is { } sc4 && sc4.Effects.OrderBy(f => (Gl(f.X, f.Y, f.Z) - _cam).LengthSquared).FirstOrDefault() is { } ef)
        {
            SetCamera(new Vector3(ef.X - 10, ef.Y + 6, ef.Z + 10), 45, -20);
            for (int k = 0; k < 10; k++) { Application.DoEvents(); Thread.Sleep(30); }
            if (Project(new Vector3(ef.X, ef.Y, ef.Z)) is { } eq && eq.X > 0 && eq.Y > 0 && eq.X < _gl.Width && eq.Y < _gl.Height)
            {
                var r4 = MouseRay(Point.Round(eq));
                float d4 = (Gl(ef.X, ef.Y, ef.Z) - r4.O).Length;
                var bh4 = _buildings.RayPick(r4.O, r4.D);
                string want = bh4 is { } x4 && x4.Dist + 3 < d4 ? "bld:" + x4.File : "fx:" + ef.Id;
                var got4 = PickAt(Point.Round(eq));
                near4 = (got4 == want ? "ok " : "ERRADO ") + $"esperado {System.IO.Path.GetFileName(want)}, veio {System.IO.Path.GetFileName(got4 ?? "nada")} (efeito a {d4:0.0} m, construção a {bh4?.Dist:0.0} m)";
            }
            else near4 = "efeito fora da tela depois de levar a câmera";
        }
        lines.Add("efeito de perto: " + near4);
        // 5) setas: escolhe a construção mais perto, arrasta a seta X 60 px e solta; fica escolhida para a foto
        string giz = "sem construção";
        DraftLoadTest();
        if (_buildings.Nearest(_cam) is { } gb && _draft?.Find(gb.File) is { } go)
        {
            _sceneSel = new BuildingSel(gb.File, Vector3.Zero); ShowObjPanel();
            var c0 = CenterOf(go); var cv = V3(c0); float L = GizmoSize(cv);
            if (Project(cv) is { } pa && Project(cv + Vector3.UnitX * L) is { } pb)
            {
                var tip = Point.Round(new PointF(pa.X + (pb.X - pa.X) * 0.8f, pa.Y + (pb.Y - pa.Y) * 0.8f));
                int axis = ObjGizmoHit(tip);
                float len = MathF.Sqrt((pb.X - pa.X) * (pb.X - pa.X) + (pb.Y - pa.Y) * (pb.Y - pa.Y));
                var to = Point.Round(new PointF(tip.X + (pb.X - pa.X) / len * 60, tip.Y + (pb.Y - pa.Y) / len * 60));
                _objAxis = axis; _objStart = c0; _objDown = tip; _objPos = null;
                if (axis == 1) ObjGizmoDrag(to);
                ObjGizmoUp();
                var c1 = CenterOf(go);
                giz = $"seta={axis} X {c0.X:0.0}->{c1.X:0.0} Z {c0.Z:0.0}->{c1.Z:0.0} ok={axis == 1 && c1.X > c0.X + 0.5f && MathF.Abs(c1.Z - c0.Z) < 0.05f}";
            }
            lines.Add("setas: " + giz);
            return load + " | dftpick: " + string.Join(" · ", lines);
        }
        lines.Add("setas: " + giz);
        _sceneSel = null; ShowObjPanel();
        return load + " | dftpick: " + string.Join(" · ", lines);
    }

    /// <summary>Raio do mouse no espaço do OpenGL (origem e direção), o mesmo do Ray.</summary>
    (Vector3 O, Vector3 D) MouseRay(Point p)
    {
        float nx = 2f * p.X / _gl.Width - 1, ny = 1 - 2f * p.Y / _gl.Height;
        var inv = Matrix4.Invert(View * Proj);
        var a = new Vector4(nx, ny, -1, 1) * inv; var b = new Vector4(nx, ny, 1, 1) * inv;
        var o = a.Xyz / a.W;
        return (o, Vector3.Normalize(b.Xyz / b.W - o));
    }


    // ---------- setas X/Y/Z no objeto escolhido ----------
    int _objAxis, _objHover;
    System.Numerics.Vector3 _objStart;
    Point _objDown;
    System.Numerics.Vector3? _objPos;
    static Vector3 V3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    bool ObjGizmoOn => _tool == Tool.Scene && SelDraft is { ReadOnlyWhy: null, Deleted: false, Kind: ObjKind.Building or ObjKind.Model };

    int ObjGizmoHit(Point p)
    {
        if (!ObjGizmoOn) return 0;
        var c = V3(CenterOf(SelDraft!)); float L = GizmoSize(c);
        if (Project(c) is not { } a) return 0;
        for (int k = 0; k < 3; k++)
            if (Project(c + GizmoDirs[k] * L * 1.18f) is { } b && SegDist(p, a, b, 0.2f) < 9) return k + 1;
        return 0;
    }

    void ObjGizmoDrag(Point p)
    {
        var c = V3(_objStart); float L = GizmoSize(c);
        var dir = GizmoDirs[_objAxis - 1];
        if (Project(c) is not { } a || Project(c + dir * L) is not { } b) return;
        float sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
        if (len2 < 4) return;
        float t = MathF.Round(((p.X - _objDown.X) * sx + (p.Y - _objDown.Y) * sy) / len2 * L * 10) / 10;
        var g = c + dir * t;
        _objPos = new System.Numerics.Vector3(g.X, g.Y, g.Z);
        _status.Text = string.Format(Loc.T("gizmo.moving", "Movendo: X {0:0.0}   altura {1:0.0}   Z {2:0.0}"), g.X, g.Y, g.Z);
        _gl.Invalidate();
    }

    /// <summary>Soltou a seta: um passo só no rascunho (Ctrl+Z desfaz). Na seta da altura, "Grudar no chão" não vale.</summary>
    bool ObjGizmoUp()
    {
        if (_objAxis == 0) return false;
        if (_objPos is { } op) MoveSelectedCenter(op.X, op.Y, op.Z, stick: _objAxis != 2 && _stickGround.Checked);
        _objAxis = 0; _objPos = null; _gl.Invalidate();
        return true;
    }

    void DrawObjGizmo()
    {
        if (!ObjGizmoOn) return;
        var c = V3(_objPos ?? CenterOf(SelDraft!)); float L = GizmoSize(c);
        int hot = _objAxis > 0 ? _objAxis : _objHover;
        void V(Vector3 q) => GL.Vertex3(Gl(q.X, q.Y, q.Z));
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting);
        GL.LineWidth(3);
        Color[] cols = { Color.Red, Color.LimeGreen, Color.DodgerBlue };
        for (int k = 0; k < 3; k++)
        {
            var d = GizmoDirs[k]; var tip = c + d * L;
            Col(hot == k + 1 ? Color.White : cols[k]);
            GL.Begin(PrimitiveType.Lines); V(c); V(tip); GL.End();
            var u = Vector3.Normalize(Vector3.Cross(d, k == 1 ? Vector3.UnitX : Vector3.UnitY)); var w = Vector3.Cross(d, u);
            GL.Begin(PrimitiveType.TriangleFan); V(c + d * L * 1.18f);
            for (int i = 0; i <= 8; i++) { float t = i / 8f * MathF.Tau; V(tip + (u * MathF.Cos(t) + w * MathF.Sin(t)) * L * 0.06f); }
            GL.End();
        }
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Contorno amarelo da malha da construção escolhida (linhas dos triângulos), como nos programas de modelagem.</summary>
    void DrawSelectedMesh()
    {
        if (_tool != Tool.Scene || SelDraft is not { Kind: ObjKind.Building, Deleted: false } o || _buildings.MeshOf(o.Key) is not { } m) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
        GL.Color4(1f, 0.85f, 0.1f, 0.55f);
        GL.PushMatrix();
        var w = m.World; GL.MultMatrix(ref w);
        GL.Begin(PrimitiveType.Triangles);
        for (int i = 0; i + 2 < m.Tri.Length; i++) { int v = m.Tri[i] * 3; if (v + 2 < m.Pos.Length) GL.Vertex3(m.Pos[v], m.Pos[v + 1], m.Pos[v + 2]); else GL.Vertex3(0f, 0f, 0f); }
        GL.End();
        GL.PopMatrix();
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        GL.Disable(EnableCap.Blend); GL.Color3(Color.White); GL.Enable(EnableCap.DepthTest);
    }

    // ---------- copiar e colar, painel na medida, moldura da ferramenta ----------
    object? _objClip;

    /// <summary>Ctrl+V na ferramenta Objetos: efeito ou som vira cópia onde está o mouse; construção, modelo e árvore chegam na etapa de inserir.</summary>
    void PasteObject()
    {
        if (_objClip is null) { _status.Text = Loc.T("obj.nothingCopied", "Nada copiado: escolha um objeto e use Ctrl+C."); return; }
        if (_objClip is TreeSel) { _status.Text = Loc.T("obj.pasteTreeLater", "Copiar árvores chega na etapa das árvores."); return; }
        if (_objClip is BuildingSel or ModelSel)
        {
            if ((_hover ?? Ray(_gl.PointToClient(Cursor.Position))) is { } g0) PasteAt(new System.Numerics.Vector3(g0.X, g0.Y, g0.Z));
            return;
        }
        if (_objClip is WorldScene.Critter) { _status.Text = Loc.T("world.critterMoveOnly", "bichos: só mover por enquanto"); return; }
        (EcwldFile.Kind Kind, uint Id)? k = _objClip switch { WorldScene.SceneEffect fx => (EcwldFile.Kind.Effect, fx.Id), WorldScene.SceneSound sd => (EcwldFile.Kind.Sound, sd.Id), _ => null };
        if (k is not { } kk || (_hover ?? Ray(_gl.PointToClient(Cursor.Position))) is not { } at) return;
        SceneEdit(f =>
        {
            uint nid = f.Clone(kk.Kind, kk.Id, 0, 0);
            if (nid != 0) f.Move(kk.Kind, nid, at.X, at.Y + (kk.Kind == EcwldFile.Kind.Effect ? 0.5f : 0), at.Z);
            return nid;
        }, Loc.T("obj.pasted", "Cópia colada onde está o mouse"));
    }

    int _newSeq;

    /// <summary>Cópias coladas (construções) para o desenho e o clique do WorldBuildings.</summary>
    IEnumerable<(string Key, string Src, Matrix4 World)> DraftExtras()
    {
        if (_draft is null) yield break;
        foreach (var p in _draft.Pieces.Values)
            foreach (var o in p.Added)
                if (!o.Deleted && o.Kind == ObjKind.Building && o.CopyOf is { } src) yield return (o.Key, src, WorldBuildings.MatrixOf(o.Now));
    }

    /// <summary>
    /// Cola a construção ou o modelo copiado com o centro no ponto do chão (jogo); com "Grudar no chão", a altura segue o
    /// terreno como a da origem. A cópia já fica escolhida (setas e cartão). Só no mesmo pedaço de 1 km da origem.
    /// </summary>
    internal string? PasteAt(System.Numerics.Vector3 ground)
    {
        var srcSel = _objClip switch { BuildingSel b => _draft?.Find(b.File), ModelSel m => _draft?.Find(m.Key), _ => null };
        if (srcSel is null || srcSel.Deleted) return _status.Text = Loc.T("obj.nothingCopied", "Nada copiado: escolha um objeto e use Ctrl+C.");
        if (_draftSrc is { } ds && ds.PieceAt(ground.X, ground.Z) != srcSel.Piece)
            return _status.Text = Loc.T("obj.pastePiece", "A cópia tem que ficar no mesmo pedaço de 1 km da original; cole mais perto dela.");
        var c = CenterOf(srcSel);
        var pos = srcSel.Now.Pos + new System.Numerics.Vector3(ground.X - c.X, 0, ground.Z - c.Z);
        if (_stickGround.Checked && _t.HeightAt(pos.X, pos.Z) is { } g && _t.HeightAt(srcSel.Now.Pos.X, srcSel.Now.Pos.Z) is { } g0) pos.Y = g + (srcSel.Now.Pos.Y - g0);
        var copy = new DraftObject
        {
            Key = "new:" + (++_newSeq), Kind = srcSel.Kind, Piece = srcSel.Piece, Source = srcSel.Source, CopyOf = srcSel.CopyOf ?? srcSel.Key,
            Solid = srcSel.Solid, LocalBox = srcSel.LocalBox, Original = srcSel.Now with { Pos = pos },
        };
        if (DraftApply(new AddOp(copy.Key, copy)) is { } why) return why;
        _sceneSel = copy.Kind == ObjKind.Model ? new ModelSel(copy.Key) : new BuildingSel(copy.Key, Vector3.Zero);
        ShowObjPanel(); _gl.Invalidate();
        return _status.Text = string.Format(Loc.T("obj.pastedNew", "Cópia de {0} colada (nova; só grava no Salvar; Ctrl+Z tira)."), System.IO.Path.GetFileName(copy.Source));
    }

    /// <summary>Painel do objeto com a altura do que ele mostra (antes era fixo e cortava o fim).</summary>
    void FitObjPanel()
    {
        int h = _objPanel.Padding.Vertical + 8;
        foreach (Control c in _objPanel.Controls) if (c.Visible) h += c.Height + c.Margin.Vertical;
        if (_objPanel.Height != h) { _objPanel.Height = h; LayoutTools(); }
    }

    int _bannerTex; Tool _bannerFor = Tool.None; int _bannerW, _bannerH;
    static Color ToolColor(Tool t) => t switch
    {
        Tool.Scene => Color.FromArgb(255, 190, 60), Tool.Paths => Color.FromArgb(235, 120, 200), Tool.Quests => Color.FromArgb(110, 190, 255),
        Tool.Create => Color.FromArgb(110, 215, 120), Tool.Teleports => Color.FromArgb(170, 140, 255), Tool.Zones => Color.FromArgb(80, 210, 200),
        Tool.Domains => Color.FromArgb(255, 140, 90), _ => Color.White,
    };

    /// <summary>Com uma ferramenta aberta: moldura da cor dela em volta do 3D e o nome dela no alto, para saber onde se está.</summary>
    void DrawToolFrame()
    {
        if (_tool == Tool.None) return;
        var col = ToolColor(_tool);
        if (_bannerFor != _tool || _bannerTex == 0)
        {
            string text = "● " + _toolTitle.Text.Trim();
            using var font = new Font("Segoe UI Semibold", 11f);
            var size = TextRenderer.MeasureText(text, font);
            int w = size.Width + 28, h = size.Height + 10;
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                int r = h / 2;
                path.AddArc(0, 0, r * 2, h - 1, 90, 180); path.AddArc(w - r * 2 - 1, 0, r * 2, h - 1, 270, 180); path.CloseFigure();
                using var fill = new SolidBrush(Color.FromArgb(235, col)); g.FillPath(fill, path);
                TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, w, h), Color.FromArgb(20, 20, 24), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (_bannerTex != 0) GL.DeleteTexture(_bannerTex);
            _bannerTex = UploadBitmap(bmp); _bannerW = w; _bannerH = h; _bannerFor = _tool;
        }
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadIdentity(); GL.Ortho(0, _gl.Width, _gl.Height, 0, -1, 1);
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.Lighting);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.Texture2D);
        GL.LineWidth(4); GL.Color4(col.R / 255f, col.G / 255f, col.B / 255f, 0.85f);
        GL.Begin(PrimitiveType.LineLoop); GL.Vertex2(2, 2); GL.Vertex2(_gl.Width - 2, 2); GL.Vertex2(_gl.Width - 2, _gl.Height - 2); GL.Vertex2(2, _gl.Height - 2); GL.End();
        GL.LineWidth(1);
        GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, _bannerTex); GL.Color4(1f, 1f, 1f, 1f);
        float x = (_gl.Width - _bannerW) / 2f, y = 10;
        GL.Begin(PrimitiveType.Quads);
        GL.TexCoord2(0f, 0f); GL.Vertex2(x, y); GL.TexCoord2(1f, 0f); GL.Vertex2(x + _bannerW, y);
        GL.TexCoord2(1f, 1f); GL.Vertex2(x + _bannerW, y + _bannerH); GL.TexCoord2(0f, 1f); GL.Vertex2(x, y + _bannerH);
        GL.End();
        GL.Disable(EnableCap.Blend); GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix(); GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
    }
    void DraftDispose() { _draftSrc?.Dispose(); _draftSrc = null; }
}
