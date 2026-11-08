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
            if (_draft is null) { _draft = new WorldDraft(_t.Name); _draft.Changed += () => { DraftTreesCache(); if (!IsDisposed) _gl.Invalidate(); }; _buildings.DraftOf = k => _draft?.Find(k); _buildings.Extras = DraftExtras; _trees.Overridden = _treeMoved.Contains; _trees.Extras = DraftTreeExtras; }
            DraftWant(PiecesAroundCamera());
            StartBuildingIndex();   // catálogo de todos os mapas já vai ficando pronto (cache: segundos)
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
        if (_draft is null || _draftSrc is null || _saving) return;
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
        if (why is not null) _status.Text = why; else _grassLast = false;   // a última ação passou a ser um objeto
        UpdateSideState();
        return why;
    }

    /// <summary>Ctrl+Z/Ctrl+Y do rascunho: só quando o passo do topo foi a última ação (as outras pilhas não andaram depois).</summary>
    bool ObjectUndo(bool redo)
    {
        StopPlanting();   // Ctrl+Z/Ctrl+Y encerram a inserção (antes o próximo clique ainda plantava)
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
        if (_placeSrc is { Kind: ObjKind.Model } gm && _hover is { } hv)
        {
            var t = GhostOf(gm, new System.Numerics.Vector3(hv.X, hv.Y, hv.Z));
            _models.Draw(gm.Source, new OpenTK.Mathematics.Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z), MathF.Atan2(t.Dir.X, t.Dir.Z) * 180 / MathF.PI);
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
        foreach (var n in _objXYZ) NoWheel(n);
        NoWheel(_objYaw); NoWheel(_objScale);   // só clicar e digitar muda (a roda em cima do campo mudava o valor)
        _stickGround.Text = Loc.T("obj.stick", "Grudar no chão");
        _showCollision.Text = Loc.T("obj.showCollision", "Ver colisão");
        _showCollision.CheckedChanged += (_, _) => _gl.Invalidate();
        _objYaw.ValueChanged += (_, _) => { if (!_objBinding) TurnSelected((float)_objYaw.Value); };
        // giro e tamanho em duas linhas alinhadas (numa linha só o campo do tamanho saía cortado à direita)
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 60, BackColor = _objPanel.BackColor, Padding = new Padding(0, 2, 0, 0), ColumnCount = 2, RowCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = Loc.T("obj.turn", "giro (graus)"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        row.Controls.Add(_objYaw, 1, 0);
        row.Controls.Add(new Label { Text = Loc.T("obj.scaleLabel", "tamanho"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        row.Controls.Add(_objScale, 1, 1);
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
        // construção: nome (do usuário ou automático pela forma) e o arquivo entre parênteses
        string title = Path.GetFileName(o.Source);
        if (o.Kind == ObjKind.Building && _buildings.SigOf(o.CopyOf ?? o.Key) is { } sig)
            title = BuildingNames.User(sig) is { } un ? un + " (" + Path.GetFileName(o.Source) + ")" : Path.GetFileName(o.Source);
        var lines = new List<string> { kind + ": " + title };
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

    /// <summary>
    /// Assentar no chão: nenhuma parte de baixo pode ficar no ar. Com a malha carregada, mede o chão embaixo de cada ponto
    /// da parte de baixo do objeto (até 1,5 m acima do ponto mais baixo, ou 15% da altura) e desce até o ponto que mais
    /// sobra encostar, mais 20 cm enterrado (usuário 08/11: a 1ª versão media só 9 pontos da caixa e a borda morro abaixo
    /// ficava no ar). Sem malha (modelo), usa a grade da caixa. Um passo só (Ctrl+Z desfaz).
    /// </summary>
    void SeatSelected()
    {
        if (SelDraft is not { Kind: ObjKind.Building or ObjKind.Model, Deleted: false, ReadOnlyWhy: null } o) return;
        var t = o.Now;
        float? need = null;   // quanto mexer na altura (negativo = desce)
        if (o.Kind == ObjKind.Building && _buildings.MeshOf(o.Key) is { } m && m.Pos.Length >= 3)
        {
            var w = m.World;
            var pts = new List<System.Numerics.Vector3>(m.Pos.Length / 3);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i + 2 < m.Pos.Length; i += 3)
            {
                var gl = Vector3.TransformPosition(new Vector3(m.Pos[i], m.Pos[i + 1], m.Pos[i + 2]), w);
                var g = new System.Numerics.Vector3(gl.X, gl.Y, -gl.Z);   // OpenGL -> jogo
                pts.Add(g); minY = MathF.Min(minY, g.Y); maxY = MathF.Max(maxY, g.Y);
            }
            float band = MathF.Max(1.5f, (maxY - minY) * 0.15f);
            foreach (var g in pts)
                if (g.Y <= minY + band && _t.HeightAt(g.X, g.Z) is { } h)
                    need = need is { } n ? MathF.Min(n, h - g.Y) : h - g.Y;
        }
        if (need is null)
        {
            var up = System.Numerics.Vector3.Normalize(t.Up); var dir = System.Numerics.Vector3.Normalize(t.Dir);
            var right = System.Numerics.Vector3.Cross(up, dir);
            float s = t.Scale is > 0 and < 1000 ? t.Scale : 1;
            var (mn, mx) = o.LocalBox;
            bool flat = (mx - mn).LengthSquared() < 1e-6f;
            float baseY = flat ? t.Pos.Y : DraftChecks.WorldBox(o).Min.Y;
            for (int ix = 0; ix <= 6; ix++)
                for (int iz = 0; iz <= 6; iz++)
                {
                    var p = flat ? t.Pos : t.Pos + (right * (mn.X + (mx.X - mn.X) * ix / 6f) + up * mn.Y + dir * (mn.Z + (mx.Z - mn.Z) * iz / 6f)) * s;
                    if (_t.HeightAt(p.X, p.Z) is { } h) need = need is { } n ? MathF.Min(n, h - baseY) : h - baseY;
                    if (flat) break;
                }
        }
        if (need is null) { _status.Text = Loc.T("obj.seatNoGround", "Não há chão embaixo deste objeto para assentar."); return; }
        float dy = need.Value - 0.2f;   // 20 cm para dentro do chão: nada aparece flutuando
        if (MathF.Abs(dy) < 0.01f) { _status.Text = Loc.T("obj.seatAlready", "A base já está encostada no chão."); return; }
        if (DraftApply(new MoveOp(o.Key, t with { Pos = t.Pos + new System.Numerics.Vector3(0, dy, 0) })) is null)
            _status.Text = string.Format(Loc.T("obj.seated", "Objeto assentado no chão ({0:+0.0;-0.0} m; ainda não gravado)."), dy);
        ShowObjPanel(); _gl.Invalidate();
    }

    /// <summary>Gira o objeto escolhido até o giro pedido (graus, em volta do eixo vertical e do centro do desenho).</summary>
    void TurnSelected(float yaw)
    {
        if (SelDraft is not { } o || o.Deleted || o.Kind == ObjKind.Tree) return;
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
        StopPlanting();
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
            if (o.Kind == ObjKind.Tree)   // árvore: a caixa envolve tronco e copa (antes ficava só no pé)
            {
                if (_trees.BoxOf(o.Type) is { } tb) { mn = o.Now.Pos + tb.Min; mx = o.Now.Pos + tb.Max; }
                else { var r = new System.Numerics.Vector3(3, 0, 3); mn = o.Now.Pos - r; mx = o.Now.Pos + r + new System.Numerics.Vector3(0, 12, 0); }
            }
            if (mn == mx) { mn -= new System.Numerics.Vector3(1); mx += new System.Numerics.Vector3(1); }
            GL.LineWidth(w); GL.Color3(c);
            GL.Begin(PrimitiveType.Lines);
            foreach (int v in BoxEdges) GL.Vertex3((v & 1) == 0 ? mn.X : mx.X, (v & 2) == 0 ? mn.Y : mx.Y, -((v & 4) == 0 ? mn.Z : mx.Z));
            GL.End();
        }
        if (_showCollision.Checked)
            foreach (var o in _draft.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Solid && !o.Deleted))
                if ((Gl(o.Now.Pos.X, o.Now.Pos.Y, o.Now.Pos.Z) - _cam).LengthSquared < 150 * 150) Box(o, Color.FromArgb(255, 90, 90), 1);
        if (SelDraft is { Deleted: false } sel && _objMode != ObjMode.Turn) Box(sel, Color.Gold, 2.5f);   // no Girar fica só o anel (usuário 08/11)   // caixa para todos (usuário 08/11: a malha amarela da construção virou caixa, como a da árvore)
        GL.LineWidth(1); GL.Color3(Color.White); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// Salvar único (construções, modelos e o cenário do SceneEdit): resumo, conferências, grava tudo ou nada e oferece
    /// enviar ao servidor quando o map.bht mudou. Falso = nada gravado (o rascunho continua).
    /// </summary>
    bool SaveObjects(bool ask = true, bool wait = false)
    {
        if (_draft is null || _draftSrc is null || _saving) return false;
        SavePlan plan;
        try
        {
            plan = DraftSaver.Plan(_draft, _draftSrc, _ecw?.Write(), _bsd);
            if (_ecwPath is not null && _ecw is not null && !plan.Files.ContainsKey(_ecwPath) && _ecwSaved is not null && !_ecw.Write().AsSpan().SequenceEqual(_ecwSaved)) plan.Files[_ecwPath] = _ecw.Write();
            if (_bsdPath is not null && _bsd is not null && !plan.Files.ContainsKey(_bsdPath) && _bsdSaved is not null && !_bsd.AsSpan().SequenceEqual(_bsdSaved)) plan.Files[_bsdPath] = (byte[])_bsd.Clone();
            // grama e flores por último, em cima do que o cenário já mudou (efeitos, sons, objetos): tudo ou nada no mesmo salvar
            if (_grassDraft is { Dirty: true } gd) GrassSaver.Apply(plan, gd, _draftSrc);
            AddLightsToPlan(plan);   // luzes da cena no mesmo salvar
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
        if (!wait)
        {
            // o pacote do 1.5.5 é dividido e a gravação copia a parte .pkx inteira: em segundo plano, com aviso
            _saving = true; _gl.Enabled = false; _tools.Enabled = false;
            _status.Text = Loc.T("obj.saving", "Salvando… o pacote do jogo é grande, pode levar até um minuto.");
            var srcW = _draftSrc;
            ShowSaveProgress(Loc.T("obj.savingBar", "Salvando os objetos do mundo"), 0);
            System.Threading.Tasks.Task.Run(() => DraftSaver.Write(plan, srcW, pct => { try { BeginInvoke(() => ShowSaveProgress(null, pct)); } catch (InvalidOperationException) { } })).ContinueWith(t => BeginInvoke(() =>
            {
                _saving = false; _gl.Enabled = true; _tools.Enabled = true;
                HideSaveProgress(!t.IsFaulted && t.Result.Kind is SaveKind.Both or SaveKind.ClientOnly);
                var r2 = t.IsFaulted ? new SaveResult(SaveKind.Failed, t.Exception?.GetBaseException().Message ?? "?") : t.Result;
                AfterObjectsWrite(r2, plan, ask);
            }));
            return true;
        }
        return AfterObjectsWrite(DraftSaver.Write(plan, _draftSrc), plan, ask);
    }

    bool _saving;
    readonly System.Windows.Forms.Timer _saveBarHide = new() { Interval = 900 };

    /// <summary>Mostra a barra azul do salvar (label null = mantém o texto) com a porcentagem.</summary>
    internal void ShowSaveProgress(string? label, int pct)
    {
        _saveBarHide.Stop();
        if (label is not null) _saveBar.Label = label;
        _saveBar.Value = pct;
        if (!_saveBar.Visible) _saveBar.Visible = true;
        _saveBar.Update();
    }

    /// <summary>Fim do salvar: completa (deu certo) e some num instante; falha some na hora (a mensagem explica).</summary>
    internal void HideSaveProgress(bool ok)
    {
        if (!ok) { _saveBar.Visible = false; return; }
        _saveBar.Value = 100; _saveBar.Update();
        _saveBarHide.Tick -= SaveBarHideTick; _saveBarHide.Tick += SaveBarHideTick;
        _saveBarHide.Start();
    }

    void SaveBarHideTick(object? s, EventArgs e) { _saveBarHide.Stop(); _saveBar.Visible = false; }

    /// <summary>Salvar rápido (na thread da tela): a barra aparece cheia por um instante, para mostrar que gravou.</summary>
    internal void FlashSaveProgress(string label, bool ok) { ShowSaveProgress(label, ok ? 100 : 0); HideSaveProgress(ok); }

    /// <summary>Depois da gravação (na thread da tela): erro em frase simples, ou relê tudo do disco e acende o envio.</summary>
    bool AfterObjectsWrite(SaveResult res, SavePlan plan, bool ask)
    {
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
        try { if (_element is not null && (plan.PckFiles.Count > 0 || plan.Files.Count > 0)) { _buildings.Reopen(_element); _piece = -1; } }   // _piece = -1: pede os pedaços de novo já
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("objetos: reabrir litmodels.pck", ex); }
        if (server) ServerNotice.Notify("map.bht");   // acende o "Salvar e enviar ao servidor"
        ObjectsSaved();
        if (plan.Files.Keys.Any(k => k.Contains("bsdata", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".ecbsd", StringComparison.OrdinalIgnoreCase))) ResetGrassEdit();   // grama relida do disco
        if (plan.Files.Keys.Any(k => k.EndsWith("scenelights.dat", StringComparison.OrdinalIgnoreCase))) ResetLightEdit();   // luzes relidas do disco
        if (plan.PckFiles.Count > 0) _bindex = null;   // construções novas: o índice relê só os arquivos novos na próxima vez
        if (plan.Files.Keys.Any(k => k.EndsWith(".ecwld", StringComparison.OrdinalIgnoreCase)) && !_trees.Reload())
            _status.Text = Loc.T("obj.treesReloadFail", "Salvo, mas não deu para reler as árvores; feche e abra o Mundo 3D para vê-las.");
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
        // anel de girar: clique num ponto do anel, arrasta 90° em volta: um passo só; Ctrl+Z volta
        string ringTxt = "-";
        SetObjMode(ObjMode.Turn);
        bool ringOnlyInTurn = true;
        if (TurnRing() is { } ring)
        {
            float y0 = YawOf(o.Now); int u0 = _draft!.UndoCount;
            var p0 = Project(ring.C + new Vector3(1, 0, 0) * ring.R); var p1 = Project(ring.C + new Vector3(0, 0, 1) * ring.R); var pm = Project(ring.C + new Vector3(0.7071f, 0, 0.7071f) * ring.R);
            if (p0 is { } a0 && p1 is { } a1 && pm is { } am)
            {
                bool hitRing = ObjGizmoHit(Point.Round(a0)) == 4;
                _objAxis = 4; StartTurn(Point.Round(a0)); TurnDrag(Point.Round(am)); TurnDrag(Point.Round(a1)); ObjGizmoUp();
                float dy = ((YawOf(o.Now) - y0) % 360 + 540) % 360 - 180;
                bool oneStep = _draft.UndoCount == u0 + 1;
                bool back = ObjectUndo(false) && MathF.Abs(((YawOf(o.Now) - y0) % 360 + 540) % 360 - 180) < 0.5f;
                ringTxt = $"anel-acha={hitRing} girou={dy:0} um-passo={oneStep} ctrlz-volta={back}";
                SetObjMode(ObjMode.Move); ringOnlyInTurn = ObjGizmoHit(Point.Round(a0)) != 4;
            }
        }
        // tamanho pelas alças: arrasta o quadrado do X para fora: aumenta num passo só; Ctrl+Z volta
        string sizeTxt = "-";
        SetObjMode(ObjMode.Size);
        {
            var cc = V3(CenterOf(o)); float L = GizmoSize(cc);
            if (Project(cc) is { } pc && Project(cc + Vector3.UnitX * L) is { } px)
            {
                float s0 = o.Now.Scale; int u0 = _draft!.UndoCount;
                int hitBox = ObjGizmoHit(Point.Round(px));
                _objAxis = hitBox; StartScale(Point.Round(px)); ScaleDrag(Point.Round(new PointF(px.X + (px.X - pc.X) * 0.5f, px.Y + (px.Y - pc.Y) * 0.5f))); ObjGizmoUp();
                float s1 = o.Now.Scale; bool one = _draft.UndoCount == u0 + 1;
                bool back = ObjectUndo(false) && MathF.Abs(o.Now.Scale - s0) < 1e-4f;
                sizeTxt = $"alca={hitBox} tamanho {s0:0.00}->{s1:0.00} um-passo={one} ctrlz-volta={back}";
            }
        }
        using (var ms = ToolsShot()) ms.Save(System.IO.Path.Combine(outDir, "dftcard-modos.png"), System.Drawing.Imaging.ImageFormat.Png);
        SetObjMode(ObjMode.Move);
        ringTxt += $" anel-so-no-girar={ringOnlyInTurn} | {sizeTxt}";
        // assentar no chão: sobe 6 m e assenta: a base fica na menor altura do chão embaixo
        var cs = CenterOf(o);
        MoveSelectedCenter(cs.X, cs.Y + 6, cs.Z, stick: false);
        SeatSelected();
        var (smn, _) = DraftChecks.WorldBox(o);
        string seatTxt = $"assentou-base={smn.Y:0.0} chao-centro={_t.HeightAt(cs.X, cs.Z) ?? 0:0.0}";
        ObjectUndo(false); ObjectUndo(false);
        DeleteSelected();
        bool deleted = o.Deleted;
        bool undo = ObjectUndo(false) && !o.Deleted;
        return load + $" | {ringTxt} {seatTxt} | dftcard: {System.IO.Path.GetFileName(near.File)} girou={turned} moveu5={moved} apagou={deleted} ctrlz={undo} conferencias={checks.Count} plano=[{string.Join(" ", plan?.Summary ?? [])}] arquivos={plan?.Files.Count}+{plan?.PckFiles.Count} {planErr} cartão=\"{card}\" painel {_objPanel.Height} px para {inner} px de conteúdo";
    }

    /// <summary>Depois de gravar: solta a seleção e o rascunho (relido do disco no DraftOpen).</summary>
    void ObjectsSaved()
    {
        _sceneSel = null; ShowObjPanel();
        _draftSrc?.Dispose(); _draftSrc = null; _draft = null; _draftAsked.Clear();
        _treeMoved.Clear(); _treeDrawn.Clear();
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

    /// <summary>Para a captura ("dftesc"): Esc com seleção tira a seleção e fica; Esc sem seleção e sem mudança sai da ferramenta.</summary>
    internal string DraftEscTest()
    {
        string load = DraftLoadTest();
        if (_buildings.Nearest(_cam) is not { } nb) return load + " | dftesc: nada carregado";
        _sceneSel = new BuildingSel(nb.File, Vector3.Zero); ShowObjPanel();
        bool first = EscapeStep() && _sceneSel is null && _tool == Tool.Scene;
        bool second = EscapeStep() && _tool == Tool.None;
        return load + $" | dftesc: 1o Esc tira a seleção e fica={first} 2o Esc sai={second}";
    }

    /// <summary>Para a captura ("dfttree"): árvore mais perto — seta X, apagar e desfazer, copiar e colar, plantar e Esc; não grava.</summary>
    internal string DraftTreeTest()
    {
        string load = DraftLoadTest();
        var t = _draft?.Pieces.Values.SelectMany(p => p.Objects).Where(o => o.Kind == ObjKind.Tree).OrderBy(o => (Gl(o.Now.Pos.X, o.Now.Pos.Y, o.Now.Pos.Z) - _cam).LengthSquared).FirstOrDefault();
        if (t is null) return load + " | dfttree: sem árvore carregada";
        float sz = _trees.SizeOf(t.Type);
        string canopy = "-";
        if (Project(V3(t.Now.Pos) + new Vector3(0, Math.Clamp(sz * 0.3f, 4f, 15f), 0)) is { } cq) canopy = PickAt(Point.Round(cq)) == t.Key ? "ok" : "errado:" + PickAt(Point.Round(cq));
        _sceneSel = new TreeSel(t.Key); ShowObjPanel();
        var c0 = CenterOf(t); var cv = V3(c0); float L = GizmoSize(cv);
        bool moved = false;
        if (Project(cv) is { } pa && Project(cv + Vector3.UnitX * L) is { } pb)
        {
            var tip = Point.Round(new PointF(pa.X + (pb.X - pa.X) * 0.8f, pa.Y + (pb.Y - pa.Y) * 0.8f));
            float len = MathF.Sqrt((pb.X - pa.X) * (pb.X - pa.X) + (pb.Y - pa.Y) * (pb.Y - pa.Y));
            _objAxis = ObjGizmoHit(tip); _objStart = c0; _objDown = tip; _objPos = null;
            if (_objAxis == 1) ObjGizmoDrag(Point.Round(new PointF(tip.X + (pb.X - pa.X) / len * 60, tip.Y + (pb.Y - pa.Y) / len * 60)));
            ObjGizmoUp();
            moved = t.Now.Pos.X > c0.X + 0.5f;
        }
        bool turnRefused = _draft!.Apply(new MoveOp(t.Key, t.Now with { Dir = new System.Numerics.Vector3(1, 0, 0) })) is not null;
        DeleteSelected(); bool del = t.Deleted;
        bool undo = ObjectUndo(false) && !t.Deleted;
        _objClip = new TreeSel(t.Key);
        string? pasted = PasteAt(t.Now.Pos + new System.Numerics.Vector3(-6, 0, 0));
        bool copyOk = SelDraft is { Kind: ObjKind.Tree, CopyOf: not null } cp && cp.Type == t.Type;
        _plantType = t.Type;
        string? planted = PlantAt(t.Now.Pos + new System.Numerics.Vector3(0, 0, 6), _plantType, select: false);
        bool escStops = EscapeStep() && _plantType < 0 && _tool == Tool.Scene;
        // prévia da janela de escolher, clique na copa, Ctrl+V com prévia e Ctrl+Z encerrando
        using var thumb = TreeThumb(t.Type, 30) as Bitmap;
        bool thumbOk = thumb is not null && thumb.GetPixel(0, 0) != thumb.GetPixel(128, 150);
        thumb?.Save(System.IO.Path.Combine(_shotDir ?? System.IO.Path.GetTempPath(), "dfttree-thumb.png"));
        _objClip = new TreeSel(t.Key);
        PasteObject();
        _hover = new Vector3(t.Now.Pos.X + 3, t.Now.Pos.Y, t.Now.Pos.Z + 3);
        bool ghost = Placing && DraftTreeExtras().Any(x => MathF.Abs(x.Pos.X - _hover.Value.X) < 0.01f);
        _draft.Apply(new MoveOp(t.Key, t.Now));   // passo vazio não entra; o Ctrl+Z abaixo desfaz o plantio anterior
        bool undoStops = ObjectUndo(false) && !Placing;
        // estrela de favoritos na janela de escolher (lista própria por tipo e cliente; aqui um cliente falso de teste)
        string starOk = "-";
        string fakeEl = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "myace-startest");
        string favPath = JavFavorites.PathFor("tree", fakeEl);
        try
        {
            var trows = (_trees.Scene?.TreeTypes ?? []).Select((tt, i) => (Id: i, Name: System.IO.Path.GetFileNameWithoutExtension(tt.Spt), Block: "t")).ToList();
            using var jw = new ItemSearchWindow(trows, askQuantity: false, model: TreeThumb, fav: JavFavorites.ForFile("tree", fakeEl))
                { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            jw.Show(); Application.DoEvents();
            jw.StarTest(0); Application.DoEvents();
            string full = jw.StarText;
            bool saved = System.IO.File.Exists(favPath) && System.IO.File.ReadAllText(favPath).Trim() == trows[0].Id.ToString();
            using (var jb = new Bitmap(jw.Width, jw.Height)) { jw.DrawToBitmap(jb, new Rectangle(0, 0, jw.Width, jw.Height)); jb.Save(System.IO.Path.Combine(_shotDir ?? System.IO.Path.GetTempPath(), "dfttree-jav-star.png")); }
            jw.StarTest(0);
            starOk = $"cheia={full == "\u2605"} gravou={saved} contorno-depois={jw.StarText == "\u2606"}";
        }
        catch (Exception ex) { starOk = "erro " + ex.Message; }
        finally { try { System.IO.File.Delete(favPath); } catch (System.IO.IOException) { } }
        // um por vez: o clique de inserir põe um objeto, sai da inserção e deixa o novo escolhido (plantio e Ctrl+V)
        _placeSrc = null; _plantType = t.Type;
        PlaceClick(t.Now.Pos + new System.Numerics.Vector3(4, 0, -4));
        bool onePlant = !Placing && SelDraft is { CopyOf: "planted" };
        _objClip = new TreeSel(t.Key); PasteObject();
        PlaceClick(t.Now.Pos + new System.Numerics.Vector3(-4, 0, -4));
        bool onePaste = !Placing && SelDraft is { CopyOf: not null } && SelDraft.Key == "new:" + _newSeq;
        // caixa da árvore inteira (tronco e copa), não só o pé
        var tbox = _trees.BoxOf(t.Type);
        string boxTxt = tbox is { } bb ? $"{bb.Max.X - bb.Min.X:0}x{bb.Max.Y - bb.Min.Y:0}x{bb.Max.Z - bb.Min.Z:0}" : "nenhuma";
        string sum = "";
        try { if (_draftSrc is not null) sum = string.Join(" / ", DraftSaver.Plan(_draft!, _draftSrc, _ecw?.Write(), _bsd).Summary); } catch (InvalidDataException ex) { sum = ex.Message; }
        // backup restaurado com a ferramenta aberta: relê do disco e as plantadas (não salvas) somem sem fechar a janela
        int addedBefore = _draft!.Pieces.Values.Sum(p => p.Added.Count(a => !a.Deleted));
        OnBackupRestored([System.IO.Path.Combine(_element!, "maps", _t.Name, _t.Name + ".ecwld")]);
        int addedAfter = _draft?.Pieces.Values.Sum(p => p.Added.Count(a => !a.Deleted)) ?? 0;
        bool reloaded = addedBefore > 0 && addedAfter == 0 && _sceneSel is null && _status.Text.Length > 0;
        return load + $" | dfttree: previa-jav={thumbOk} copa={canopy} (raio {sz:0.0}) ctrlv-previa={ghost} ctrlz-encerra={undoStops} um-por-vez plantio={onePlant} ctrlv={onePaste} caixa={boxTxt} backup-recarrega={reloaded} ({addedBefore}->{addedAfter}) estrela: {starOk} | {System.IO.Path.GetFileName(t.Source)} seta-x={moved} giro-recusado={turnRefused} apagou={del} ctrlz={undo} colou={copyOk} plantou={planted?.Length > 0} esc-para-plantio={escStops} plano=\"{sum}\"";
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

    // ---------- modos do objeto escolhido: Mover (padrão), Girar, Tamanho (um botão por vez, como nos programas 3D) ----------
    enum ObjMode { Move, Turn, Size }
    ObjMode _objMode = ObjMode.Move;
    readonly Button[] _modeBtns = new Button[3];
    FlowLayoutPanel? _modeRow;

    /// <summary>Linha com os 3 botões de modo (ícone em cima, nome embaixo); fica acima dos campos digitáveis.</summary>
    Control ObjModeRow()
    {
        _modeRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, BackColor = _objPanel.BackColor, Padding = new Padding(0, 4, 0, 12), WrapContents = false };   // 12 px antes dos campos
        (string Glyph, string Key, string Pt, string TipKey, string Tip)[] defs =
        {
            ("\uE7C2", "obj.modeMove", "Mover", "obj.modeMoveTip", "Setas X, Y e Z: arraste uma seta para mover nesse eixo."),
            ("\uE7AD", "obj.modeTurn", "Girar", "obj.modeTurnTip", "Anel na base: arraste o anel ou a bolinha para girar (5 em 5°; Shift = 15°, Ctrl = livre)."),
            ("\uE740", "obj.modeSize", "Tamanho", "obj.modeSizeTip", "Alças com quadrado: arraste para aumentar ou diminuir. O jogo usa o mesmo tamanho nos três eixos."),
        };
        for (int i = 0; i < 3; i++)
        {
            var m = (ObjMode)i;
            var b = new Button { Width = 72, Height = 62, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Text = Loc.T(defs[i].Key, defs[i].Pt) + "\n" + "VRF"[i], Image = UiKit.Icon(defs[i].Glyph, Color.White, 18),
                TextImageRelation = TextImageRelation.ImageAboveText, Margin = new Padding(0, 0, 3, 0), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8.5f) };
            _toolTip.SetToolTip(b, Loc.T(defs[i].TipKey, defs[i].Tip));
            b.Click += (_, _) => { SetObjMode(m); _gl.Focus(); };
            _modeBtns[i] = b; _modeRow.Controls.Add(b);
        }
        UpdateModeButtons();
        return _modeRow;
    }

    void SetObjMode(ObjMode m)
    {
        if (!ModeAllowed(m)) return;
        _objMode = m; _objAxis = 0; _objHover = 0;
        UpdateModeButtons(); _gl.Invalidate();
        _status.Text = m switch
        {
            ObjMode.Turn => Loc.T("obj.modeTurnTip", "Anel na base: arraste o anel ou a bolinha para girar (5 em 5°; Shift = 15°, Ctrl = livre)."),
            ObjMode.Size => Loc.T("obj.modeSizeTip", "Alças com quadrado: arraste para aumentar ou diminuir. O jogo usa o mesmo tamanho nos três eixos."),
            _ => Loc.T("obj.modeMoveTip", "Setas X, Y e Z: arraste uma seta para mover nesse eixo."),
        };
    }

    /// <summary>Árvore só move (o jogo não gira nem muda o tamanho dela); modelo move e gira; construção faz tudo.</summary>
    bool ModeAllowed(ObjMode m) => m == ObjMode.Move || SelDraft is { } o && (m == ObjMode.Turn ? o.Kind is ObjKind.Building or ObjKind.Model : o.Kind == ObjKind.Building);

    void UpdateModeButtons()
    {
        if (_modeRow is null) return;
        _modeRow.Visible = SelDraft is { Deleted: false };
        if (!ModeAllowed(_objMode)) _objMode = ObjMode.Move;
        for (int i = 0; i < 3; i++)
        {
            var b = _modeBtns[i]; bool on = (int)_objMode == i, ok = ModeAllowed((ObjMode)i);
            b.Enabled = ok;
            b.BackColor = on ? UiKit.Accent : Color.FromArgb(40, 45, 54);
            b.FlatAppearance.BorderColor = on ? UiKit.Accent : Color.FromArgb(62, 70, 84);
            b.ForeColor = ok ? Color.White : Color.Gray;
        }
    }

    // tamanho pelas alças: relativo ao clique, um passo só no Ctrl+Z
    float _scale0; Point _scaleDown; System.Numerics.Vector3 _scaleC;

    void StartScale(Point p)
    {
        if (SelDraft is not { } o) return;
        _scale0 = o.Now.Scale > 0 ? o.Now.Scale : 1; _scaleDown = p; _scaleC = CenterOf(o);
        _turnUndoBase = _draft?.UndoCount ?? 0; _turnMoved = false;
    }

    void ScaleDrag(Point p)
    {
        if (SelDraft is not { Kind: ObjKind.Building } o || _draft is null) return;
        var c = V3(_scaleC); float L = GizmoSize(c);
        float f;
        if (_objAxis == 8) f = 1 + (_scaleDown.Y - p.Y) / 150f;   // quadrado do meio: para cima aumenta
        else
        {
            var dir = GizmoDirs[_objAxis - 5];
            if (Project(c) is not { } a || Project(c + dir * L) is not { } b) return;
            float sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
            if (len2 < 4) return;
            f = 1 + ((p.X - _scaleDown.X) * sx + (p.Y - _scaleDown.Y) * sy) / len2;
        }
        float step = (ModifierKeys & Keys.Control) != 0 ? 0.01f : (ModifierKeys & Keys.Shift) != 0 ? 0.25f : 0.05f;
        float sNew = Math.Clamp(MathF.Round(_scale0 * f / step) * step, 0.1f, 10f);
        if (_draft.UndoCount > _turnUndoBase) _draft.Undo();   // fica um passo só
        if (MathF.Abs(sNew - _scale0) > 1e-4f) DraftApply(new MoveOp(o.Key, o.Now with { Scale = sNew }));
        _turnMoved = true;
        ShowObjPanel();
        _status.Text = string.Format(Loc.T("obj.sizing", "Tamanho: {0:0.00}× (de 0,05 em 0,05; Shift = 0,25, Ctrl = livre)"), sNew);
    }

    static void Cube(Vector3 c, float h)
    {
        Vector3 V(int i) => new(c.X + ((i & 1) == 0 ? -h : h), c.Y + ((i & 2) == 0 ? -h : h), -(c.Z + ((i & 4) == 0 ? -h : h)));
        int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
        GL.Begin(PrimitiveType.Quads);
        foreach (var f in faces) foreach (int i in f) GL.Vertex3(V(i));
        GL.End();
    }

    bool ObjGizmoOn => _tool == Tool.Scene && SelDraft is { ReadOnlyWhy: null, Deleted: false, Kind: ObjKind.Building or ObjKind.Model or ObjKind.Tree };

    int ObjGizmoHit(Point p)
    {
        if (!ObjGizmoOn) return 0;
        var c = V3(CenterOf(SelDraft!)); float L = GizmoSize(c);
        if (Project(c) is not { } a) return 0;
        if (_objMode == ObjMode.Size)
        {
            if (MathF.Abs(a.X - p.X) < 10 && MathF.Abs(a.Y - p.Y) < 10) return 8;   // quadrado do meio
            for (int k = 0; k < 3; k++)
                if (Project(c + GizmoDirs[k] * L) is { } b && (SegDist(p, a, b, 0.2f) < 9 || MathF.Abs(b.X - p.X) < 11 && MathF.Abs(b.Y - p.Y) < 11)) return k + 5;
            return 0;
        }
        if (_objMode == ObjMode.Move)
        {
            for (int k = 0; k < 3; k++)
                if (Project(c + GizmoDirs[k] * L * 1.18f) is { } b && SegDist(p, a, b, 0.2f) < 9) return k + 1;
            return 0;
        }
        // anel de girar (construção e modelo; árvore não gira no jogo): na base, em volta do objeto
        if (TurnRing() is { } ring)
        {
            PointF? prev = null;
            for (int i = 0; i <= 64; i++)
            {
                float t = i / 64f * MathF.Tau;
                var q = Project(ring.C + new Vector3(MathF.Sin(t), 0, MathF.Cos(t)) * ring.R);
                if (q is { } qq && prev is { } pp && SegDist(p, pp, qq, 0) < 8) return 4;
                prev = q;
            }
            if (Project(ring.C + new Vector3(MathF.Sin(ring.Yaw), 0, MathF.Cos(ring.Yaw)) * ring.R) is { } kq && MathF.Abs(kq.X - p.X) < 11 && MathF.Abs(kq.Y - p.Y) < 11) return 4;   // bolinha
        }
        return 0;
    }

    /// <summary>Anel de girar do objeto escolhido: centro na base, raio que envolve a planta do objeto, giro atual (rad).</summary>
    (Vector3 C, float R, float Yaw)? TurnRing()
    {
        if (!ObjGizmoOn || SelDraft is not { Kind: ObjKind.Building or ObjKind.Model } o) return null;
        var (mn, mx) = DraftChecks.WorldBox(o);
        var c = V3((mn + mx) / 2); c.Y = mn.Y + 0.3f;
        float half = MathF.Sqrt((mx.X - mn.X) * (mx.X - mn.X) + (mx.Z - mn.Z) * (mx.Z - mn.Z)) / 2;
        float r = Math.Clamp(MathF.Max(half * 1.05f, GizmoSize(c) * 0.9f), 1.5f, 120f);
        return (c, r, YawOf(o.Now) * MathF.PI / 180);
    }

    /// <summary>Ângulo (graus, igual ao giro do objeto) do ponto do mouse no plano horizontal da altura y.</summary>
    float? MouseAngleAt(Point p, Vector3 c)
    {
        var (o, d) = MouseRay(p);
        if (MathF.Abs(d.Y) < 1e-4f) return null;
        float s = (c.Y - o.Y) / d.Y;
        if (s <= 0) return null;
        var hit = o + d * s;   // OpenGL: Z do jogo invertido
        return MathHelper.RadiansToDegrees(MathF.Atan2(hit.X - c.X, -hit.Z - c.Z));
    }

    // giro pelo anel: relativo ao ponto onde clicou (não pula), um passo só no Ctrl+Z
    float _turnMouse0, _turnYaw0; int _turnUndoBase; bool _turnMoved;

    void StartTurn(Point p)
    {
        if (TurnRing() is not { } ring || SelDraft is not { } o) return;
        _turnMouse0 = MouseAngleAt(p, ring.C) ?? 0;
        _turnYaw0 = YawOf(o.Now); _turnUndoBase = _draft?.UndoCount ?? 0; _turnMoved = false;
    }

    void TurnDrag(Point p)
    {
        if (TurnRing() is not { } ring || _draft is null) return;
        if (MouseAngleAt(p, ring.C) is not { } ang) return;
        float delta = ang - _turnMouse0;
        while (delta > 180) delta -= 360; while (delta < -180) delta += 360;
        float yaw = _turnYaw0 + delta;
        float step = (ModifierKeys & Keys.Control) != 0 ? 1 : (ModifierKeys & Keys.Shift) != 0 ? 15 : 5;
        yaw = MathF.Round(yaw / step) * step;
        yaw = ((yaw + 180) % 360 + 360) % 360 - 180;
        if (_draft.UndoCount > _turnUndoBase) _draft.Undo();   // tira o passo do arrasto anterior: fica um só
        TurnSelected(yaw);
        _turnMoved = true;
        _status.Text = string.Format(Loc.T("obj.turning", "Girando: {0:0}° (de 5 em 5°; Shift = 15°, Ctrl = livre)"), yaw);
    }

    void ObjGizmoDrag(Point p)
    {
        if (_objAxis == 4) { TurnDrag(p); _gl.Invalidate(); return; }
        if (_objAxis >= 5) { ScaleDrag(p); _gl.Invalidate(); return; }
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
        if (_objAxis >= 5 && _turnMoved && SelDraft is { } so) _status.Text = string.Format(Loc.T("obj.sized", "Tamanho {0:0.00}× (ainda não gravado; Ctrl+Z desfaz)"), so.Now.Scale);
        if (_objAxis == 4 && _turnMoved && SelDraft is { } to) _status.Text = string.Format(Loc.T("obj.turnedRing", "Girado para {0:0}° (ainda não gravado; Ctrl+Z desfaz)"), YawOf(to.Now));
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
        if (_objMode == ObjMode.Move)
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
        if (_objMode == ObjMode.Size)
        {
            // alças de tamanho: linha + quadrado na ponta (como no Blender) e um quadrado no meio
            for (int k = 0; k < 3; k++)
            {
                var tip = c + GizmoDirs[k] * L;
                Col(hot == k + 5 ? Color.White : cols[k]);
                GL.Begin(PrimitiveType.Lines); V(c); V(tip); GL.End();
                Cube(tip, L * 0.07f);
            }
            Col(hot == 8 ? Color.White : Color.FromArgb(220, 220, 220));
            Cube(c, L * 0.06f);
        }
        // anel de girar: dourado (branco com o mouse em cima ou girando), riscos a cada 45° e a bolinha na frente do objeto
        if (_objMode == ObjMode.Turn && TurnRing() is { } ring)
        {
            bool on = hot == 4;
            GL.LineWidth(on ? 4.5f : 3);
            Col(on ? Color.White : Color.Gold);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 96; i++) { float t = i / 96f * MathF.Tau; V(ring.C + new Vector3(MathF.Sin(t), 0, MathF.Cos(t)) * ring.R); }
            GL.End();
            GL.LineWidth(2);
            GL.Begin(PrimitiveType.Lines);
            for (int i = 0; i < 8; i++) { float t = i / 8f * MathF.Tau; var dd = new Vector3(MathF.Sin(t), 0, MathF.Cos(t)); V(ring.C + dd * ring.R * 0.93f); V(ring.C + dd * ring.R * 1.07f); }
            var f = new Vector3(MathF.Sin(ring.Yaw), 0, MathF.Cos(ring.Yaw));
            V(ring.C); V(ring.C + f * ring.R);
            GL.End();
            var knob = ring.C + f * ring.R; float kr = MathF.Max(0.35f, GizmoSize(knob) * 0.07f);
            Col(on ? Color.White : Color.Orange);
            GL.Begin(PrimitiveType.TriangleFan); V(knob);
            for (int i = 0; i <= 16; i++) { float t = i / 16f * MathF.Tau; V(knob + new Vector3(MathF.Sin(t), 0, MathF.Cos(t)) * kr); }
            GL.End();
        }
        GL.LineWidth(1); GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Número do giro em cima da bolinha enquanto gira.</summary>
    void ObjGizmoLabels()
    {
        if (_objAxis >= 5 && SelDraft is { } so) { DrawLabel($"{so.Now.Scale:0.00}×", V3(_scaleC) + new Vector3(0, GizmoSize(V3(_scaleC)) * 1.3f, 0), Color.White); return; }
        if (_objAxis != 4 || TurnRing() is not { } ring) return;
        var f = new Vector3(MathF.Sin(ring.Yaw), 0, MathF.Cos(ring.Yaw));
        DrawLabel($"{ring.Yaw * 180 / MathF.PI:0}°", ring.C + f * ring.R + new Vector3(0, GizmoSize(ring.C) * 0.25f, 0), Color.White);
    }

    // ---------- copiar e colar, painel na medida, moldura da ferramenta ----------
    object? _objClip;

    /// <summary>Ctrl+V na ferramenta Objetos: efeito ou som vira cópia onde está o mouse; construção, modelo e árvore chegam na etapa de inserir.</summary>
    void PasteObject()
    {
        if (_objClip is null) { _status.Text = Loc.T("obj.nothingCopied", "Nada copiado: escolha um objeto e use Ctrl+C."); return; }
        if (_objClip is BuildingSel or ModelSel or TreeSel)
        {
            var src = _objClip switch { BuildingSel b => _draft?.Find(b.File), ModelSel m => _draft?.Find(m.Key), TreeSel t => _draft?.Find(t.Key), _ => null };
            if (src is null || src.Deleted) { _status.Text = Loc.T("obj.nothingCopied", "Nada copiado: escolha um objeto e use Ctrl+C."); return; }
            StartPlacing(src);
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
    internal string? _shotDir;   // só captura: pasta das fotos
    int _plantType = -1;   // tipo de árvore em plantio (−1 = não está plantando)
    DraftObject? _placeSrc;   // objeto copiado em inserção (Ctrl+V): segue o mouse até o clique
    bool Placing => _plantType >= 0 || _placeSrc is not null;

    /// <summary>Lugar da prévia/cópia com o centro no ponto do chão (construção e modelo; com Grudar no chão, altura relativa ao terreno).</summary>
    ObjTransform GhostOf(DraftObject src, System.Numerics.Vector3 ground)
    {
        var c = CenterOf(src);
        var pos = src.Now.Pos + new System.Numerics.Vector3(ground.X - c.X, 0, ground.Z - c.Z);
        if (src.Piece == 0)
        {
            // veio do catálogo de outro mapa (ou de longe): a altura de origem é de OUTRO terreno; a base da caixa vai para o
            // chão do ponto (antes ficava na altura do mapa de origem, até 200 m abaixo do chão, e parecia invisível)
            float baseY = DraftChecks.WorldBox(src).Min.Y;
            pos.Y = (_t.HeightAt(ground.X, ground.Z) ?? ground.Y) + (src.Now.Pos.Y - baseY);
        }
        else if (_stickGround.Checked && _t.HeightAt(pos.X, pos.Z) is { } g && _t.HeightAt(src.Now.Pos.X, src.Now.Pos.Z) is { } g0) pos.Y = g + (src.Now.Pos.Y - g0);
        return src.Now with { Pos = pos };
    }

    /// <summary>Ctrl+V: entra na inserção do objeto copiado; a prévia segue o mouse e um clique fixa a cópia e sai.</summary>
    void StartPlacing(DraftObject src)
    {
        _plantType = -1; _placeSrc = src; _placeRecent = null;
        _status.Text = string.Format(Loc.T("obj.placingOne", "Inserindo {0}: a prévia segue o mouse; clique onde ele vai ficar. Esc ou botão direito cancela."), System.IO.Path.GetFileName(src.Source));
        _gl.Invalidate();
    }

    // árvores do arquivo mexidas/apagadas (saem do laço normal) e as que o desenho põe no lugar novo; refeitas só quando o
    // rascunho muda (antes cada quadro procurava "tree:N" de cada árvore e varria o rascunho: lixo de memória e travadinhas)
    readonly HashSet<int> _treeMoved = new();
    readonly List<(int Type, Vector3 Pos)> _treeDrawn = new();

    void DraftTreesCache()
    {
        _treeMoved.Clear(); _treeDrawn.Clear();
        if (_draft is null) return;
        foreach (var p in _draft.Pieces.Values)
            foreach (var o in p.Objects.Concat(p.Added))
            {
                if (o.Kind != ObjKind.Tree) continue;
                if (o.Changed && o.Key.StartsWith("tree:", StringComparison.Ordinal) && int.TryParse(o.Key.AsSpan(5), out int i)) _treeMoved.Add(i);
                if (!o.Deleted && (o.CopyOf is not null || o.Changed)) _treeDrawn.Add((o.Type, V3(o.Now.Pos)));
            }
    }

    /// <summary>Árvores mexidas (fora do laço normal) e plantadas, para o desenho.</summary>
    IEnumerable<(int Type, Vector3 Pos)> DraftTreeExtras()
    {
        if (_draft is null) yield break;
        foreach (var t in _treeDrawn) yield return t;
        if (Placing && _hover is { } hv)
        {
            int ghost = _plantType >= 0 ? _plantType : _placeSrc is { Kind: ObjKind.Tree } ts ? ts.Type : -1;
            if (ghost >= 0) yield return (ghost, new Vector3(hv.X, _t.HeightAt(hv.X, hv.Z) ?? hv.Y, hv.Z));   // prévia do plantio
        }
    }

    /// <summary>Planta (ou cola) uma árvore do tipo no ponto do chão. Árvore pode ir para qualquer pedaço que tenha bloco de cenário.</summary>
    internal string? PlantAt(System.Numerics.Vector3 ground, int type, bool select)
    {
        if (_draft is null || _draftSrc is null) return _status.Text = Loc.T("obj.noDraft", "Abra a ferramenta Objetos do mundo para mexer nos objetos.");
        var types = _trees.Scene?.TreeTypes;
        if (types is null || type < 0 || type >= types.Count) return _status.Text = Loc.T("obj.treeNoType", "Árvore nova sem tipo.");
        int piece = _draftSrc.PieceAt(ground.X, ground.Z);
        if (piece <= 0) return _status.Text = Loc.T("obj.chkOutShort", "Fora do mapa.");
        if (!_draft.Pieces.ContainsKey(piece)) return _status.Text = Loc.T("obj.notLoadedPiece", "Esse pedaço do mapa ainda não carregou; chegue mais perto.");
        if (_t.HeightAt(ground.X, ground.Z) is { } h) ground.Y = h;
        var tree = new DraftObject
        {
            Key = "new:" + (++_newSeq), Kind = ObjKind.Tree, Piece = piece, Type = type, Source = types[type].Spt, TreeComposite = types[type].CompositeMap, CopyOf = "planted",
            Original = new ObjTransform(ground, System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitY, 1),
        };
        if (DraftApply(new AddOp(tree.Key, tree)) is { } why) return why;
        if (select) { _sceneSel = new TreeSel(tree.Key); ShowObjPanel(); }
        _gl.Invalidate();
        return _status.Text = string.Format(Loc.T("obj.planted", "Árvore {0} plantada (só grava no Salvar; Ctrl+Z tira)."), System.IO.Path.GetFileNameWithoutExtension(tree.Source));
    }

    List<TreeKind>? _treeKinds;   // tipos de árvore de todos os mapas (lista leve, lida uma vez)

    /// <summary>
    /// Pílula Árvores: janela de escolher com os tipos deste mapa e os de todos os outros mapas e instâncias (agrupados pelo
    /// mapa), prévia 3D e estrela (pelo tipo, igual em todo mapa); depois a árvore segue o mouse e um clique planta.
    /// Tipo que este mapa não tem entra na lista do mapa no Salvar.
    /// </summary>
    void ChooseTreeToPlant()
    {
        var types = _trees.Scene?.TreeTypes;
        if (types is null) { _status.Text = Loc.T("obj.noTreeTypes", "Este mapa não tem tipos de árvore para plantar."); return; }
        try { if (_element is not null) _treeKinds ??= TreeTypeIndex.Build(_element); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("objetos: tipos de árvore dos mapas", ex); }
        // linhas: deste mapa primeiro; depois os outros (um por par .spt + folhas), no 1º mapa que usa
        var rowsK = new List<(string Spt, string Comp, string Block)>();
        foreach (var t in types.ToList()) rowsK.Add((t.Spt, t.CompositeMap, MapLabel(_t.Name)));
        foreach (var k in (_treeKinds ?? []).OrderBy(k => MapRank(k.Maps.OrderBy(MapRank).First())).ThenBy(k => k.Maps.OrderBy(MapRank).First(), StringComparer.OrdinalIgnoreCase))
            if (!rowsK.Any(r => r.Spt.Equals(k.Spt, StringComparison.OrdinalIgnoreCase) && r.Comp.Equals(k.Comp, StringComparison.OrdinalIgnoreCase)))
                rowsK.Add((k.Spt, k.Comp, MapLabel(k.Maps.OrderBy(MapRank).First())));
        if (rowsK.Count == 0) { _status.Text = Loc.T("obj.noTreeTypes", "Este mapa não tem tipos de árvore para plantar."); return; }
        var rows = rowsK.Select((r, i) => (Id: i, Name: System.IO.Path.GetFileNameWithoutExtension(r.Spt), Block: r.Block)).ToList();
        int TypeOfRow(int row) => row >= 0 && row < rowsK.Count ? _trees.TypeFor(rowsK[row].Spt, rowsK[row].Comp) : -1;
        int SigOf(int row) => new TreeKind(rowsK[row].Spt, rowsK[row].Comp, []).Sig;
        JavFavorites? fav = null;
        if (_element is not null)
        {
            var file = JavFavorites.ForFile("tree", _element);
            fav = new JavFavorites(() => file.Ids.Select(sig => Enumerable.Range(0, rowsK.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList(),
                i => i >= 0 && i < rowsK.Count && file.Has(SigOf(i)), i => file.Toggle(SigOf(i)));
        }
        var recentT = _element is null ? [] : JavRecents.Read("tree", _element).Select(sig => Enumerable.Range(0, rowsK.Count).FirstOrDefault(i => SigOf(i) == sig, -1)).Where(i => i >= 0).ToList();
        using var w = new ItemSearchWindow(rows, askQuantity: false, recent: recentT, model: (row, yaw) => TypeOfRow(row) is var t && t >= 0 ? TreeThumb(t, yaw) : null, fav: fav, keepBlockOrder: true)
            { Text = Loc.T("obj.chooseTree", "Escolher árvore para plantar") };
        if (w.ShowDialog(this) != DialogResult.OK || w.Picked < 0) return;
        int type = TypeOfRow(w.Picked);
        if (type < 0) return;
        _placeSrc = null; _plantType = type;
        _placeRecent = ("tree", SigOf(w.Picked));
        _status.Text = string.Format(Loc.T("obj.plantingOne", "Plantando {0}: clique no chão onde a árvore vai ficar. Esc ou botão direito cancela."), rows[w.Picked].Name);
        _gl.Focus();
    }

    /// <summary>Prévia da árvore do tipo para a janela de escolher (enquadrada pela caixa da árvore).</summary>
    Image? TreeThumb(int type, float yaw)
    {
        _trees.Prepare(type);
        Vector3 mn, mx;
        if (_trees.BoxOf(type) is { } bx) { mn = new Vector3(bx.Min.X, bx.Min.Y, -bx.Max.Z); mx = new Vector3(bx.Max.X, bx.Max.Y, -bx.Min.Z); }
        else { float r = MathF.Max(2, _trees.SizeOf(type) is var s && s > 0 ? s : 8); mn = new Vector3(-r * 0.45f, 0, -r * 0.45f); mx = new Vector3(r * 0.45f, r * 0.9f, r * 0.45f); }
        return OffscreenThumb(mn, mx, yaw, (right, up) => _trees.DrawThumb(type, right, up) is not null);
    }

    /// <summary>
    /// Desenha num quadro fora da tela (framebuffer) com o contexto do Mundo 3D e lê os pixels: a caixa (espaço do desenho)
    /// cabe na foto com folga, girada yaw graus. null = não deu (a janela mostra sem prévia).
    /// </summary>
    Image? OffscreenThumb(Vector3 mn, Vector3 mx, float yaw, Func<Vector3, Vector3, bool> draw)
    {
        const int Size = 256;
        int fbo = 0, color = 0, depth = 0;
        try
        {
            _gl.MakeCurrent();
            fbo = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            color = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, Size, Size);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, color);
            depth = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Size, Size);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) return null;
            GL.Viewport(0, 0, Size, Size);
            GL.ClearColor(0.12f, 0.13f, 0.16f, 1f); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.Lighting);
            var target = (mn + mx) / 2; var ext = mx - mn;
            float fit = MathF.Max(0.5f, MathF.Max(ext.Y, MathF.Max(ext.X, ext.Z)) * 0.5f);   // meia medida maior
            float a = MathHelper.DegreesToRadians(yaw);
            float dist = MathF.Max(1.5f, fit / MathF.Tan(MathHelper.DegreesToRadians(20)) * 1.45f);   // 40° de abertura, folga
            var eye = target + new Vector3(MathF.Sin(a), 0.2f, MathF.Cos(a)).Normalized() * dist;
            var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(40), 1, MathF.Max(0.1f, dist * 0.02f), (dist + fit) * 4);
            var view = Matrix4.LookAt(eye, target, Vector3.UnitY);
            GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadMatrix(ref proj);
            GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadMatrix(ref view);
            var fwd = (target - eye).Normalized();
            var right = Vector3.Cross(fwd, Vector3.UnitY).Normalized(); var up = Vector3.Cross(right, fwd).Normalized();
            bool drawn = draw(right, up);
            GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix(); GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
            if (!drawn) return null;
            var px = new byte[Size * Size * 4];
            GL.ReadPixels(0, 0, Size, Size, PixelFormat.Bgra, PixelType.UnsignedByte, px);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;   // opaco (o fundo já é a cor da janela)
            var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            for (int y = 0; y < Size; y++) System.Runtime.InteropServices.Marshal.Copy(px, (Size - 1 - y) * Size * 4, data.Scan0 + y * data.Stride, Size * 4);   // OpenGL vem de baixo para cima
            bmp.UnlockBits(data);
            return bmp;
        }
        catch (Exception ex) { AppLog.Error("objetos: prévia", ex); return null; }
        finally
        {
            try
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                if (color != 0) GL.DeleteRenderbuffer(color); if (depth != 0) GL.DeleteRenderbuffer(depth); if (fbo != 0) GL.DeleteFramebuffer(fbo);
                GL.Viewport(0, 0, _gl.Width, _gl.Height);
                _gl.Invalidate();
            }
            catch (Exception ex) { AppLog.Error("objetos: limpar a prévia", ex); }
        }
    }

    /// <summary>
    /// Para a captura ("dftcat"): catálogo das construções por perto, prévia, janela com estrela, pôr uma cópia em OUTRO
    /// pedaço de 1 km (um clique) e o plano do salvar (referências nos blocos do lugar novo); não grava.
    /// </summary>
    internal string DraftCatalogTest()
    {
        string load = DraftLoadTest();
        if (_draft is null || _draftSrc is null) return load + " | dftcat: sem rascunho";
        StartBuildingIndex();
        try { _bindex?.Wait(TimeSpan.FromMinutes(8)); } catch (AggregateException) { }
        _catRows = CatalogRows(out var note);
        var _catalog = _catRows.Where(c => c.Near).ToList();
        int total = _catalog.Sum(c => c.Count);
        int others = _catRows.Count(c => !c.Near), otherMaps = _catRows.Where(c => !c.Near).Select(c => c.Block).Distinct().Count();
        if (_catalog.Count == 0) return load + " | dftcat: catálogo vazio";
        using (var th = BuildingThumb(0, 30) as Bitmap) th?.Save(System.IO.Path.Combine(_shotDir ?? System.IO.Path.GetTempPath(), "dftcat-thumb.png"));
        // janela de escolher como o usuário vê (prévia + estrela), sem favoritos de verdade
        var rows = _catalog.Select((c, i) => (Id: i, Name: $"{System.IO.Path.GetFileNameWithoutExtension(c.Key)} · {c.Size.X:0}×{c.Size.Y:0}×{c.Size.Z:0} m · {c.Count}×", Block: "c")).ToList();
        var favSet = new List<int>();
        using (var jw = new ItemSearchWindow(rows, askQuantity: false, model: BuildingThumb, fav: new JavFavorites(() => favSet, favSet.Contains, i => { if (!favSet.Remove(i)) favSet.Add(i); return null; }))
            { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
        {
            jw.Show(); jw.ShotModel(0); jw.StarTest(0); Application.DoEvents();
            using var jb = new Bitmap(jw.Width, jw.Height); jw.DrawToBitmap(jb, new Rectangle(0, 0, jw.Width, jw.Height));
            jb.Save(System.IO.Path.Combine(_shotDir ?? System.IO.Path.GetTempPath(), "dftcat-jav.png"));
        }
        // a menor construção (cabe em qualquer lugar), posta num pedaço vizinho carregado
        int pick = Enumerable.Range(0, _catalog.Count).OrderBy(i => _catalog[i].Size.X * _catalog[i].Size.Z).First();
        // prévia de uma construção de OUTRO mapa (carregada na hora)
        var c0probe = CenterOf(_draft.Find(_catalog[pick].Key)!);
        int far = _catRows.FindIndex(c => !c.Near && !c.Block.StartsWith(Loc.T("obj.catThisMap", "Este mapa: {0}")[..4]));
        string farThumb = "-";
        string farGhost = "-";
        if (far >= 0 && CatalogSource(_catRows[far]) is { } fps)
        {
            var gp = new System.Numerics.Vector3(c0probe.X, 0, c0probe.Z);
            var gt = GhostOf(fps, gp);
            var probe = new DraftObject { Key = "probe", Kind = ObjKind.Building, Original = gt, LocalBox = fps.LocalBox };
            float baseY = DraftChecks.WorldBox(probe).Min.Y, gy = _t.HeightAt(gp.X, gp.Z) ?? 0;
            farGhost = $"base={baseY:0.0} chao={gy:0.0} origem-y={fps.Original.Pos.Y:0.0}";
        }
        if (far >= 0) using (var ft = BuildingThumb(far, 30) as Bitmap) { farThumb = ft is null ? "falhou" : "ok " + System.IO.Path.GetFileName(_catRows[far].Key); ft?.Save(System.IO.Path.Combine(_shotDir ?? System.IO.Path.GetTempPath(), "dftcat-thumb-outro-mapa.png")); }
        var src = _draft.Find(_catalog[pick].Key)!;
        var c0 = CenterOf(src);
        System.Numerics.Vector3? target = null;
        foreach (var dxz in new[] { (1100f, 0f), (-1100f, 0f), (0f, 1100f), (0f, -1100f), (800f, 800f), (-800f, -800f) })
        {
            var g = new System.Numerics.Vector3(c0.X + dxz.Item1, c0.Y, c0.Z + dxz.Item2);
            int pc = _draftSrc.PieceAt(g.X, g.Z);
            if (pc > 0 && pc != src.Piece && _draft.Pieces.ContainsKey(pc)) { if (_t.HeightAt(g.X, g.Z) is { } h) g.Y = h; target = g; break; }
        }
        if (target is null) return load + $" | dftcat: {_catalog.Count} formas ({total} construções) sem pedaço vizinho carregado";
        StartPlacing(src);
        PlaceClick(target.Value);
        var copy = SelDraft;
        bool one = !Placing && copy is { CopyOf: not null } && copy.Piece != src.Piece;
        var checks = DraftChecks.Run(_draft, _draftSrc, (x, z) => _t.HeightAt(x, z) ?? 0, []);
        bool blocked = checks.Any(c => c.Blocks && c.Key == copy?.Key);
        string sum = ""; int blocksOk = -1;
        try
        {
            var plan = DraftSaver.Plan(_draft, _draftSrc, _ecw?.Write(), _bsd);
            sum = string.Join(" / ", plan.Summary);
            if (_ecwPath is not null && plan.Files.TryGetValue(_ecwPath, out var ew))
            {
                var ef = new EcwldFile(ew);
                uint max = ef.MaxId();
                blocksOk = ef.Find(EcwldFile.Kind.Ornament, max).Count(h => ObjectFormats.BlockArea(ef, h.Block) is var a && target.Value.X >= a.X0 - 60 && target.Value.X <= a.X1 + 60 && target.Value.Z >= a.Z0 - 60 && target.Value.Z <= a.Z1 + 60);
            }
        }
        catch (InvalidDataException ex) { sum = ex.Message; }
        ObjectUndo(false);
        return load + $" | dftcat: indice={others} formas de {otherMaps} mapas {note} previa-outro-mapa={farThumb} altura-outro-mapa=[{farGhost}] | {_catalog.Count} formas ({total} construções) 1a={System.IO.Path.GetFileName(_catalog[0].Key)} x{_catalog[0].Count} outro-pedaco={one} (de {src.Piece} para {copy?.Piece}) bloqueou={blocked} blocos-do-lugar-novo={blocksOk} plano=\"{sum}\"";
    }

    /// <summary>Mapa onde a forma mais aparece (índice de todos os mapas), para o nome automático; sem índice = este mapa.</summary>
    string NameMapOf(int sig)
    {
        if (_bindex is { IsCompletedSuccessfully: true, Result: { } idx })
        {
            _nameMap ??= idx.ByMap.SelectMany(kv => kv.Value.Select(s => (kv.Key, s.Sig, s.Count))).GroupBy(x => x.Sig).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Count).First().Key);
            if (_nameMap.TryGetValue(sig, out var m)) return MapLabelPlain(m);
        }
        return MapLabelPlain(_t.Name);
    }
    Dictionary<int, string>? _nameMap;

    string MapLabelPlain(string map)
    {
        try { _mapNames ??= _element is null ? new() : MapTags.LocalMapNames(_element); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("objetos: nomes dos mapas", ex); _mapNames = new(); }
        return _mapNames.TryGetValue(map, out var n) && n.Length > 0 ? n : map;
    }

    /// <summary>Dar nome à construção escolhida (botão "Dar nome" do cartão): a mesma janela do Renomear da JAV.</summary>
    void RenameSelected()
    {
        if (SelDraft is not { Kind: ObjKind.Building } o || _buildings.SigOf(o.CopyOf ?? o.Key) is not { } sig) return;
        NameDialog(sig, Path.GetFileNameWithoutExtension(o.Source));
        ShowObjPanel(); _gl.Invalidate();
    }

    /// <summary>
    /// Janela de nome de uma forma de construção: o nome é só o que aparece no editor (JAV e cartão), não mexe no jogo; vale
    /// para todas as cópias da mesma forma em qualquer mapa e cliente (lista do programa). Tirar o nome volta ao do arquivo.
    /// Exportar/importar a lista na mesma janela. true = o nome mudou.
    /// </summary>
    bool NameDialog(int sig, string fileName)
    {
        using var f = new Form { Text = Loc.T("names.title2", "Renomear construção"), Width = 480, Height = 236, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f) };
        var info = new Label { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10, 8, 10, 0), ForeColor = Color.Silver,
            Text = string.Format(Loc.T("names.info2", "Só muda o nome que aparece no editor (o jogo não muda). Vale para todas as cópias desta forma, em qualquer mapa e cliente. Arquivo: {0}"), fileName) };
        var box = new TextBox { Dock = DockStyle.Top, Text = BuildingNames.User(sig) ?? "", PlaceholderText = fileName, BackColor = Color.FromArgb(20, 22, 26), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var boxWrap = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 6, 10, 6) }; boxWrap.Controls.Add(box);
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10, 10, 10, 12) };
        Button Btn(string key, string pt, Action a, bool main = false)
        {
            var b = new Button { Text = Loc.T(key, pt), AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(60, 66, 78), Margin = new Padding(6, 0, 0, 0) };
            b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => a(); return b;
        }
        string? why = null; bool changed = false;
        row.Controls.Add(Btn("names.save", "Salvar nome", () => { var t = box.Text.Trim(); why = BuildingNames.Set(sig, t.Length == 0 ? null : t); changed = why is null; f.DialogResult = DialogResult.OK; }, main: true));
        row.Controls.Add(Btn("names.clear", "Tirar o nome", () => { why = BuildingNames.Set(sig, null); changed = why is null; f.DialogResult = DialogResult.OK; }));
        row.Controls.Add(Btn("names.export", "Exportar lista…", () =>
        {
            using var sd = new SaveFileDialog { Filter = "JSON|*.json", FileName = "nomes-construcoes.json" };
            if (sd.ShowDialog(f) == DialogResult.OK) MessageBox.Show(f, BuildingNames.Export(sd.FileName) ?? Loc.T("names.exported", "Lista exportada."), f.Text);
        }));
        row.Controls.Add(Btn("names.import", "Importar lista…", () =>
        {
            using var od = new OpenFileDialog { Filter = "JSON|*.json" };
            if (od.ShowDialog(f) != DialogResult.OK) return;
            var (n, w) = BuildingNames.Import(od.FileName);
            MessageBox.Show(f, w ?? string.Format(Loc.T("names.imported", "{0} nome(s) importado(s)."), n), f.Text);
            changed |= w is null && n > 0;
        }));
        f.Controls.Add(row); f.Controls.Add(boxWrap); f.Controls.Add(info);
        f.AcceptButton = row.Controls[0] as Button;
        f.ShowDialog(this);
        if (why is not null) MessageBox.Show(this, why, f.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return changed;
    }

    // ---------- pílula Construções: por perto + todos os mapas e instâncias (índice) ----------
    /// <summary>Linha do catálogo: Near = construção carregada perto (objeto do rascunho); senão vem do índice.</summary>
    sealed record CatRow(string Key, int Sig, System.Numerics.Vector3 Min, System.Numerics.Vector3 Max, bool Solid, int Count, int Maps, string Block, bool Near)
    {
        public System.Numerics.Vector3 Size => Max - Min;
    }
    List<CatRow> _catRows = [];
    (string Kind, int Sig)? _placeRecent;   // o que a inserção em andamento grava nos recentes da JAV ao dar certo
    Task<BuildingIndex?>? _bindex;      // índice de todos os mapas (segundo plano, uma vez por janela)
    int _bxDone, _bxTotal;
    Dictionary<string, string>? _mapNames;

    /// <summary>Começa o índice de construções de todos os mapas em segundo plano (cache: depois da 1ª vez leva segundos).</summary>
    void StartBuildingIndex()
    {
        if (_element is null || _bindex is { IsFaulted: false, IsCanceled: false }) return;
        string el = _element;
        _bindex = Task.Run(() => BuildingIndex.Build(el, (d, t) => { Volatile.Write(ref _bxDone, d); Volatile.Write(ref _bxTotal, t); }));
    }

    /// <summary>Ordem dos grupos do catálogo: este mapa, depois o world (mundo aberto, usuário 08/11), depois os outros.</summary>
    int MapRank(string map) => map.Equals(_t.Name, StringComparison.OrdinalIgnoreCase) ? 0 : map.Equals("world", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    string MapLabel(string map)
    {
        try { _mapNames ??= _element is null ? new() : MapTags.LocalMapNames(_element); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { AppLog.Error("objetos: nomes dos mapas", ex); _mapNames = new(); }
        string name = _mapNames.TryGetValue(map, out var n) && n.Length > 0 ? $"{n} ({map})" : map;
        return map.Equals(_t.Name, StringComparison.OrdinalIgnoreCase) ? string.Format(Loc.T("obj.catThisMap", "Este mapa: {0}"), name) : name;
    }

    /// <summary>
    /// Linhas do catálogo: "Por perto" (carregadas) e, com o índice pronto, cada forma de todos os mapas uma vez, no mapa
    /// onde mais aparece ("aparece N× em M mapas"). note = andamento do índice enquanto ele não fica pronto.
    /// </summary>
    List<CatRow> CatalogRows(out string? note)
    {
        var rows = new List<CatRow>();
        string near = Loc.T("obj.catNear", "Por perto");
        var seen = new HashSet<int>();
        foreach (var c in _buildings.Catalog())
            if (_draft?.Find(c.Key) is { Deleted: false, ReadOnlyWhy: null } d) { rows.Add(new CatRow(c.Key, c.Sig, d.LocalBox.Min, d.LocalBox.Max, d.Solid, c.Count, 1, near, true)); seen.Add(c.Sig); }
        note = null;
        if (_bindex is { IsCompletedSuccessfully: true, Result: { } idx })
        {
            var best = new Dictionary<int, (string Map, BuildingShape S, int Total, int Maps)>();
            foreach (var (map, list) in idx.ByMap)
                foreach (var s in list)
                    best[s.Sig] = best.TryGetValue(s.Sig, out var b)
                        ? (s.Count > b.S.Count ? map : b.Map, s.Count > b.S.Count ? s : b.S, b.Total + s.Count, b.Maps + 1)
                        : (map, s, s.Count, 1);
            foreach (var (sig, b) in best.OrderBy(kv => MapRank(kv.Value.Map)).ThenBy(kv => kv.Value.Map, StringComparer.OrdinalIgnoreCase).ThenByDescending(kv => kv.Value.Total))
                rows.Add(new CatRow(b.S.Sample, sig, b.S.Min, b.S.Max, b.S.Solid, b.Total, b.Maps, MapLabel(b.Map), false));
        }
        else if (_bindex is { IsCompleted: true }) note = Loc.T("obj.catIndexFail", "não deu para ler os outros mapas (veja o log)");
        else note = string.Format(Loc.T("obj.catIndexing", "lendo os outros mapas… {0}%"), Volatile.Read(ref _bxTotal) > 0 ? 100 * Volatile.Read(ref _bxDone) / Volatile.Read(ref _bxTotal) : 0);
        return rows;
    }

    /// <summary>Prévia de uma linha do catálogo: a construção no espaço dela (sem giro do mundo), enquadrada pela caixa.</summary>
    Image? BuildingThumb(int row, float yaw)
    {
        if (row < 0 || row >= _catRows.Count) return null;
        var r = _catRows[row];
        if (!_buildings.EnsureLoaded(r.Key) || _buildings.LocalBoxOf(r.Key) is not { } lb) return null;   // de outro mapa: lê agora
        var m = WorldBuildings.MatrixOf(new ObjTransform(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitY, 1));   // Z espelhado como no mundo
        var mn = new Vector3(lb.Min.X, lb.Min.Y, -lb.Max.Z); var mx = new Vector3(lb.Max.X, lb.Max.Y, -lb.Min.Z);
        return OffscreenThumb(mn, mx, yaw, (_, _) => _buildings.DrawThumb(r.Key, m));
    }

    /// <summary>
    /// Objeto de origem da inserção: a construção carregada (rascunho) ou, vinda do índice, um objeto só de origem com o lugar
    /// e a caixa do .bmd (de outro mapa ou de longe). null = não deu (motivo no status).
    /// </summary>
    DraftObject? CatalogSource(CatRow r)
    {
        if (r.Near) return _draft?.Find(r.Key) is { Deleted: false } d ? d : null;
        if (_draft?.Find(r.Key) is { Deleted: false } loaded) return loaded;   // do índice, mas carregada agora
        try
        {
            if (_draftSrc?.Lit(out var why) is not { } lit) { _status.Text = Loc.T("obj.catNoPack", "Não deu para abrir o litmodels.pck."); return null; }
            byte[] bytes;
            lock (_draftSrc.LitLock) bytes = lit.Find(r.Key.ToLowerInvariant()) is { } e ? lit.Read(e) : [];
            if (bytes.Length == 0) { _status.Text = Loc.T("obj.catGone", "Essa construção não está mais carregada; abra a lista de novo."); return null; }
            if (!_buildings.EnsureLoaded(r.Key)) { _status.Text = Loc.T("obj.catGone", "Essa construção não está mais carregada; abra a lista de novo."); return null; }
            return new DraftObject { Key = r.Key, Kind = ObjKind.Building, Piece = 0, Source = r.Key, Original = ObjectFormats.ReadBmd(bytes), Solid = r.Solid, LocalBox = (r.Min, r.Max) };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("objetos: construção do catálogo", ex);
            _status.Text = Loc.T("obj.catGone", "Essa construção não está mais carregada; abra a lista de novo.");
            return null;
        }
    }

    /// <summary>
    /// Pílula Construções: janela de escolher com as construções por perto e as de todos os mapas e instâncias (cada forma
    /// uma vez, com tamanho e quantas aparecem), prévia 3D e estrela; a escolhida segue o mouse e um clique a põe.
    /// </summary>
    void ChooseBuildingToPlace()
    {
        if (_draft is null) { _status.Text = Loc.T("obj.noDraft", "Abra a ferramenta Objetos do mundo para mexer nos objetos."); return; }
        StartBuildingIndex();
        _catRows = CatalogRows(out var note);
        if (_catRows.Count == 0) { _status.Text = Loc.T("obj.noBuildingsNear", "Nenhuma construção carregada por perto; chegue mais perto de uma cidade ou ruína.") + (note is null ? "" : " (" + note + ")"); return; }
        string Size(System.Numerics.Vector3 s) => $"{s.X:0.#}×{s.Y:0.#}×{s.Z:0.#} m";
        // nome dado pelo usuário (se houver) + arquivo, tamanho e quantas vezes aparece
        string RowName(CatRow c) => (BuildingNames.User(c.Sig) is { } un ? un + " · " : "") + string.Format(Loc.T("obj.catRow", "{0} · {1} · aparece {2}×"), System.IO.Path.GetFileNameWithoutExtension(c.Key), Size(c.Size), c.Count)
            + (c.Maps > 1 ? " " + string.Format(Loc.T("obj.catMaps", "em {0} mapas"), c.Maps) : "");
        var rows = _catRows.Select((c, i) => (Id: i, Name: RowName(c), Block: c.Block)).ToList();
        // favoritos pela forma (número estável), não pela linha (a lista muda com o lugar da câmera)
        JavFavorites? fav = null;
        if (_element is not null)
        {
            var file = JavFavorites.ForFile("building", _element);
            var cat = _catRows;
            fav = new JavFavorites(() => file.Ids.Select(sig => cat.FindIndex(c => c.Sig == sig)).Where(i => i >= 0).ToList(), i => i >= 0 && i < cat.Count && file.Has(cat[i].Sig), i => file.Toggle(cat[i].Sig));
        }
        var recentB = _element is null ? [] : JavRecents.Read("building", _element).Select(sig => _catRows.FindIndex(c => c.Sig == sig)).Where(i => i >= 0).ToList();
        var catR = _catRows;
        using var w = new ItemSearchWindow(rows, askQuantity: false, recent: recentB, model: BuildingThumb, fav: fav, keepBlockOrder: true,
            rename: id => id >= 0 && id < catR.Count && NameDialog(catR[id].Sig, System.IO.Path.GetFileNameWithoutExtension(catR[id].Key)) ? RowName(catR[id]) : null)
            { Text = Loc.T("obj.chooseBuilding", "Escolher construção para pôr no mundo") + (note is null ? "" : " — " + note) };
        if (w.ShowDialog(this) != DialogResult.OK || w.Picked < 0 || w.Picked >= _catRows.Count) return;
        if (CatalogSource(_catRows[w.Picked]) is not { } src) return;
        StartPlacing(src);
        _placeRecent = ("building", _catRows[w.Picked].Sig);
        _gl.Focus();
    }

    /// <summary>
    /// Um clique de inserção: põe um objeto (árvore plantada ou cópia do Ctrl+V), sai da inserção e deixa o novo escolhido
    /// para ajustar. Um por vez (pedido do usuário 08/11: antes seguia pondo até o Esc). Se não deu, continua inserindo.
    /// </summary>
    internal string? PlaceClick(System.Numerics.Vector3 ground)
    {
        string? msg = _plantType >= 0 ? PlantAt(ground, _plantType, select: true)
            : _placeSrc is { Kind: ObjKind.Tree } ts ? PlantAt(ground, ts.Type, select: true)
            : PasteAt(ground);
        if (SelDraft?.Key == "new:" + _newSeq && SelDraft.CopyOf is not null)
        {
            if (_placeRecent is { } pr && _element is not null) JavRecents.Add(pr.Kind, _element, pr.Sig);   // aparece em Recentes da JAV
            _placeRecent = null;
            _plantType = -1; _placeSrc = null;
            _status.Text = msg; _gl.Invalidate();
        }
        return msg;
    }

    /// <summary>Sai do plantio (Esc ou botão direito). Falso = não estava plantando.</summary>
    bool StopPlanting()
    {
        if (!Placing) return false;
        _plantType = -1; _placeSrc = null; _placeRecent = null; _gl.Invalidate();
        _status.Text = Loc.T("obj.plantStop", "Plantio encerrado.");
        return true;
    }

    /// <summary>Cópias coladas (construções) para o desenho e o clique do WorldBuildings.</summary>
    IEnumerable<(string Key, string Src, Matrix4 World)> DraftExtras()
    {
        if (_draft is null) yield break;
        foreach (var p in _draft.Pieces.Values)
            foreach (var o in p.Added)
                if (!o.Deleted && o.Kind == ObjKind.Building && o.CopyOf is { } src) yield return (o.Key, src, WorldBuildings.MatrixOf(o.Now));
        if (_placeSrc is { Kind: ObjKind.Building } gs && _hover is { } hv)
            yield return ("ghost", gs.CopyOf ?? gs.Key, WorldBuildings.MatrixOf(GhostOf(gs, new System.Numerics.Vector3(hv.X, hv.Y, hv.Z))));
    }

    /// <summary>
    /// Cola a construção ou o modelo copiado com o centro no ponto do chão (jogo); com "Grudar no chão", a altura segue o
    /// terreno como a da origem. A cópia já fica escolhida (setas e cartão). Só no mesmo pedaço de 1 km da origem.
    /// </summary>
    internal string? PasteAt(System.Numerics.Vector3 ground)
    {
        var srcSel = _placeSrc ?? _objClip switch { BuildingSel b => _draft?.Find(b.File), ModelSel m => _draft?.Find(m.Key), TreeSel t => _draft?.Find(t.Key), _ => null };
        if (srcSel is { Kind: ObjKind.Tree, Deleted: false } tree) return PlantAt(ground, tree.Type, select: true);
        if (srcSel is null || srcSel.Deleted) return _status.Text = Loc.T("obj.nothingCopied", "Nada copiado: escolha um objeto e use Ctrl+C.");
        // pode colar em outro pedaço de 1 km: a cópia entra no pedaço onde fica (precisa estar carregado, perto da câmera)
        int toPiece = _draftSrc?.PieceAt(ground.X, ground.Z) ?? srcSel.Piece;
        if (toPiece <= 0) return _status.Text = Loc.T("obj.chkOutShort", "Fora do mapa.");
        if (_draft is not null && !_draft.Pieces.ContainsKey(toPiece)) return _status.Text = Loc.T("obj.notLoadedPiece", "Esse pedaço do mapa ainda não carregou; chegue mais perto.");
        var pos = GhostOf(srcSel, ground).Pos;
        var copy = new DraftObject
        {
            Key = "new:" + (++_newSeq), Kind = srcSel.Kind, Piece = toPiece, Source = srcSel.Source, CopyOf = srcSel.CopyOf ?? srcSel.Key,
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
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadIdentity(); GL.Ortho(0, _gl.Width, _gl.Height, 0, -1, 1);
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.Lighting);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.Texture2D);
        GL.LineWidth(4); GL.Color4(col.R / 255f, col.G / 255f, col.B / 255f, 0.85f);
        GL.Begin(PrimitiveType.LineLoop); GL.Vertex2(2, 2); GL.Vertex2(_gl.Width - 2, 2); GL.Vertex2(_gl.Width - 2, _gl.Height - 2); GL.Vertex2(2, _gl.Height - 2); GL.End();
        GL.LineWidth(1);
        GL.Disable(EnableCap.Blend); GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix(); GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
    }
    /// <summary>Sair da ferramenta com mudança não salva: pergunta; Sim descarta tudo (rascunho e cenário voltam ao disco).</summary>
    bool AbandonObjects()
    {
        if (!SceneDirty) return true;
        if (MessageBox.Show(this, Loc.T("obj.abandonAsk", "Há mudanças não salvas nos objetos do mundo. Abandonar e perder essas mudanças?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        _sceneSel = null; ShowObjPanel();
        _draftSrc?.Dispose(); _draftSrc = null; _draft = null; _draftAsked.Clear();
        ResetGrassEdit();   // pinceladas não salvas saem junto
        ResetLightEdit();
        _ecw = null; _bsd = null; _ecwWhy = null; _sUndo.Clear(); _sRedo.Clear();
        LoadSceneForEdit();
        UpdateSideState(); _gl.Invalidate();
        return true;
    }

    void DraftDispose() { _draftSrc?.Dispose(); _draftSrc = null; }
}
