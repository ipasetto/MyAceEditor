using MyAceEditor.PerfectWorld;
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

    void DraftDispose() { _draftSrc?.Dispose(); _draftSrc = null; }
}
