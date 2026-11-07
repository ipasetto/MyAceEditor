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
            if (_draft is null) { _draft = new WorldDraft(_t.Name); _draft.Changed += () => { if (!IsDisposed) _gl.Invalidate(); }; _buildings.DraftOf = k => _draft?.Find(k); }
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
            foreach (var o in piece.Objects)
            {
                if (o.Kind != ObjKind.Model || o.Deleted || o.Source.Length == 0) continue;
                var t = o.Now;
                _models.Draw(o.Source, new OpenTK.Mathematics.Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z), MathF.Atan2(t.Dir.X, t.Dir.Z) * 180 / MathF.PI);
            }
    }

    // ---------------- tela: escolher, cartão, mexer, apagar, salvar (Task 7) ----------------
    sealed record ModelSel(string Key);
    readonly NumericUpDown _objYaw = new() { Minimum = -720, Maximum = 720, DecimalPlaces = 0, Width = 64, Increment = 15 };
    readonly CheckBox _stickGround = new() { Text = "", Checked = true, AutoSize = true, ForeColor = Color.White };
    readonly CheckBox _showCollision = new() { Text = "", Checked = false, AutoSize = true, ForeColor = Color.White };

    /// <summary>Objeto do rascunho por trás da seleção (construção ou modelo); null para efeito, som e bichos.</summary>
    DraftObject? SelDraft => _sceneSel switch
    {
        BuildingSel b => _draft?.Find(b.File),
        ModelSel m => _draft?.Find(m.Key),
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
        string kind = o.Kind == ObjKind.Building ? Loc.T("world.objBuilding", "Construção") : Loc.T("obj.model", "Modelo");
        var lines = new List<string> { kind + ": " + Path.GetFileName(o.Source) };
        if (size.LengthSquared() > 0) lines.Add(string.Format(Loc.T("obj.size", "tamanho {0:0.0} × {1:0.0} × {2:0.0} m (largura × altura × profundidade)"), size.X, size.Y, size.Z));
        lines.Add(string.Format(Loc.T("obj.count", "aparece {0} vez(es) nos pedaços abertos"), same));
        lines.Add(string.Format(Loc.T("obj.collision", "colisão: {0} · escala {1:0.##} (só leitura nesta etapa)"), o.Solid ? Loc.T("obj.yes", "sim") : Loc.T("obj.no", "não"), o.Now.Scale));
        if (o.ReadOnlyWhy is not null) lines.Add(o.ReadOnlyWhy);
        else if (o.Deleted) lines.Add(Loc.T("obj.deletedLine", "apagado (Ctrl+Z traz de volta)"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Leva o centro do objeto escolhido para (x, y, z) do jogo; com "Grudar no chão", a altura segue o terreno.</summary>
    void MoveSelectedCenter(float x, float y, float z)
    {
        if (SelDraft is not { } o) { _status.Text = Loc.T("obj.notLoaded", "Este objeto ainda está carregando; tente de novo em instantes."); return; }
        var c = CenterOf(o);
        var pos = o.Now.Pos + new System.Numerics.Vector3(x - c.X, y - c.Y, z - c.Z);
        if (_stickGround.Checked && _t.HeightAt(pos.X, pos.Z) is { } g && _t.HeightAt(o.Original.Pos.X, o.Original.Pos.Z) is { } g0)
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
        if (SelDraft is { Deleted: false } sel) Box(sel, Color.Gold, 2.5f);
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
        _sceneSel = null; ShowObjPanel();
        _draftSrc.Dispose(); _draftSrc = null; _draft = null; _draftAsked.Clear();
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
        DeleteSelected();
        bool deleted = o.Deleted;
        bool undo = ObjectUndo(false) && !o.Deleted;
        return load + $" | dftcard: {System.IO.Path.GetFileName(near.File)} girou={turned} moveu5={moved} apagou={deleted} ctrlz={undo} conferencias={checks.Count} plano=[{string.Join(" ", plan?.Summary ?? [])}] arquivos={plan?.Files.Count}+{plan?.PckFiles.Count} {planErr} cartão=\"{card}\"";
    }

    /// <summary>Leva a câmera até o objeto (conferência "levar até ele").</summary>
    void FlyTo(string key)
    {
        if (_draft?.Find(key) is not { } o) return;
        var c = CenterOf(o);
        SetCamera(new Vector3(c.X - 20, c.Y + 15, c.Z + 20), 45, -25);
    }

    void DraftDispose() { _draftSrc?.Dispose(); _draftSrc = null; }
}
