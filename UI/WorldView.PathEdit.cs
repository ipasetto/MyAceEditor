using System.ComponentModel;
using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Ferramenta "Caminhos e áreas" do Mundo 3D: como um monstro/NPC anda. Anda numa ÁREA (campo Caminho = 0; o quadrado da
/// área do npcgen, alça amarela) ou PATRULHA um caminho do path.sev (servidor; PW-KB §S22): desenhar um caminho novo
/// clicando no chão, arrastar os pontos de um caminho, apagar. path.sev é gravado aqui (backup + releitura); a ligação do
/// monstro ao caminho fica no npcgen.data (Salvar do NPCGen). Ctrl+Z/Ctrl+Y em tudo.
/// </summary>
internal sealed partial class WorldView
{
    PathFile? _pathFile; string? _pathSev; byte[]? _pathSaved;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Action<object, int>? SetCreaturePath { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Func<int, int>? PathUsers { get; set; }

    /// <summary>Passo de desfazer: path.sev antes; Npc = profundidade do desfazer do NPCGen depois do passo; WithNpc = o passo também mudou o npcgen.</summary>
    sealed record PathStep(byte[] Path, int Npc, bool WithNpc);
    readonly List<PathStep> _pUndo = new(), _pRedo = new();
    List<Vector3>? _drawPts;            // desenho de caminho em andamento (jogo)
    Vector3? _drawHover;
    int _dragPt = -1, _hoverPt = -1; byte[]? _dragBefore;
    object? _pathPanelFor = new();       // ponto que o painel está mostrando

    readonly Label _pathHint = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Gainsboro, Margin = new Padding(3, 2, 3, 6) };
    readonly Label _pathName = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(3, 2, 3, 4) };
    readonly Label _pathState = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.Silver, Margin = new Padding(3, 4, 3, 4) };
    Button _modeArea = null!, _modePath = null!, _pathDraw = null!, _pathDone = null!, _pathDel = null!;

    internal void SetPathFile(string file, PathFile pf) { _pathSev = file; _pathFile = pf; _pathSaved = pf.Write(); RebuildPaths(); }
    void RebuildPaths() => Paths = _pathFile?.Paths.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Sample());
    bool PathDirty => _pathFile is not null && _pathSaved is not null && !_pathFile.Write().AsSpan().SequenceEqual(_pathSaved);

    NpcGenSpawn? PathSpawn => _tool == Tool.Paths && SelectedMark()?.Tag is NpcGenSpawn s ? s : null;
    static int PathIdOf(NpcGenSpawn s) => s.Creatures.Select(c => c.PathId).FirstOrDefault(id => id > 0);
    SevPath? SelPath => PathSpawn is { } s && PathIdOf(s) is > 0 and var id ? _pathFile?.Paths.FirstOrDefault(p => p.Id == id) : null;

    // passos da ferramenta (texto em cima de cada parte do painel)
    readonly Label _pathStep1 = SectionLabel(""), _pathStep2 = SectionLabel(""), _pathLoopTitle = SectionLabel("");
    Button _areaNone = null!, _pathCancel = null!, _loop0 = null!, _loop1 = null!, _loop2 = null!, _pathUndo = null!, _pathRedo = null!, _pathSave = null!;
    readonly FlowLayoutPanel _loopBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    // sem criatura escolhida: só o aviso (vermelho) e a explicação, nenhum botão
    readonly Label _pathPickRed = new() { AutoSize = true, MaximumSize = new Size(245, 0), ForeColor = Color.FromArgb(255, 95, 85), Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(3, 6, 3, 4) };
    /// <summary>Caminho que o ponto tinha antes de passar a andar na área (voltar a Patrulhar religa, sem redesenhar).</summary>
    readonly Dictionary<NpcGenSpawn, int> _lastPathOf = new();
    BhtFile? _pathBht; bool _pathBhtTried; Vector3? _blockedClick;
    /// <summary>Colisão do servidor (map.bht) para conferir o caminho; null se não há servidor.</summary>
    BhtFile? PathCollision()
    {
        if (_pathBhtTried) return _pathBht;
        _pathBhtTried = true;
        try { if (ServerConfigDir is { } sd && Path.Combine(sd, _t.Name, "map.bht") is var f && File.Exists(f)) _pathBht = new BhtFile(File.ReadAllBytes(f)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Info("map.bht (caminho): " + ex.Message); }
        return _pathBht;
    }

    /// <summary>Primeiro ponto do trecho a→b (seguindo o chão, à altura do corpo) que entra num brush sólido; null = livre.</summary>
    Vector3? BlockedBetween(Vector3 a, Vector3 b)
    {
        if (PathCollision() is not { } bht) return null;
        float dx = b.X - a.X, dz = b.Z - a.Z;
        int n = Math.Clamp((int)(MathF.Sqrt(dx * dx + dz * dz) / 3), 1, 300);
        Vector3 P(int k) { float t = k / (float)n, x = a.X + dx * t, z = a.Z + dz * t; return new(x, MathF.Max(a.Y + (b.Y - a.Y) * t, _t.HeightAt(x, z) ?? a.Y) + 1.2f, z); }
        var prev = P(0);
        for (int k = 1; k <= n; k++)
        {
            var cur = P(k);
            if (bht.SegmentHit(prev.X, prev.Y, prev.Z, cur.X, cur.Y, cur.Z) >= 0) return cur;
            prev = cur;
        }
        return null;
    }

    /// <summary>Motivo de o ponto não andar no jogo (NPC comum sem IA); null = anda.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Func<object, string?>? PatrolBlock { get; set; }
    /// <summary>No fim do caminho (npcgen iLoopType, gs patrol_agent.h): 0 para, 1 volta pelo mesmo caminho, 2 recomeça do início.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Action<object, int>? SetCreaturePathLoop { get; set; }

    void InitPathPage()
    {
        _modeArea = SmallButton("\uE81E", Loc.T("path.modeArea", "Andar numa área"), () => SetMode(false));
        _modePath = SmallButton("\uE707", Loc.T("path.modePath", "Patrulhar um caminho"), () => SetMode(true));
        _areaNone = SmallButton("\uE7B7", Loc.T("path.areaNone", "Sem área (fica parado no ponto)"), () =>
        {
            if (SelectedMark() is { } m && AreaOf(m.Tag) is { } ar && (ar.Sx > 0 || ar.Sz > 0)) { ResizeArea?.Invoke(m.Tag, 0, 0); RefreshMarkers(); UpdatePathPanel(); _gl.Invalidate(); }
        });
        _pathDraw = SmallButton("\uE70F", Loc.T("path.draw", "Desenhar caminho novo"), StartDraw);
        _pathsPage.Controls.Add(_pathPickRed);
        _pathDone = SmallButton("\uE73E", Loc.T("path.done", "Terminar o desenho (Enter)"), FinishDraw, main: true);
        _pathCancel = SmallButton("\uE711", Loc.T("path.cancel", "Cancelar o desenho (Esc)"), CancelDraw);
        _pathDel = SmallButton("\uE74D", Loc.T("path.delete3", "Apagar o caminho"), DeletePath);
        _toolTip.SetToolTip(_pathDel, Loc.T("path.delete3Tip", "Tira o caminho do ponto (e do path.sev se ninguém mais usa); ele volta a andar na área."));
        _loop0 = SmallButton("\uE71A", Loc.T("path.loop0", "Para no último ponto"), () => SetLoop(0));
        _loop1 = SmallButton("\uE8AB", Loc.T("path.loop1b", "Vai e volta"), () => SetLoop(1));
        _loop2 = SmallButton("\uE8EE", Loc.T("path.loop2b", "Circuito (volta ao 1º ponto)"), () => SetLoop(2));
        _toolTip.SetToolTip(_loop1, Loc.T("path.loop1", "Volta pelo mesmo caminho (vai e volta)"));
        _toolTip.SetToolTip(_loop2, Loc.T("path.loop2Tip", "Do último ponto anda direto ao primeiro e recomeça. Para fechar o desenho, termine clicando perto do 1º ponto."));
        _loopBox.Controls.Add(_pathLoopTitle); _loopBox.Controls.Add(_loop0); _loopBox.Controls.Add(_loop1); _loopBox.Controls.Add(_loop2);
        _pathUndo = SmallButton("\uE7A7", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => UndoAny());
        _pathRedo = SmallButton("\uE7A6", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => RedoAny());
        _pathSave = SmallButton("\uE74E", Loc.T("path.save", "Salvar caminhos (path.sev)"), SavePaths, main: true);
        foreach (var c in new Control[] { _pathName, _pathStep1, _modeArea, _modePath, _pathStep2, _pathHint, _areaNone, _pathDraw, _pathDone, _pathCancel,
                     _loopBox, _pathDel, _pathState, _pathUndo, _pathRedo, _pathSave })
            _pathsPage.Controls.Add(c);
        FormClosing += (_, e) =>
        {
            if (!PathDirty) return;
            var r = MessageBox.Show(this, Loc.T("path.askSave", "Os caminhos de patrulha têm mudanças não salvas. Salvar antes de fechar?"), Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) e.Cancel = true; else if (r == DialogResult.Yes) SavePaths();
        };
        UpdatePathPanel();
    }

    /// <summary>Modo pedido agora: patrulha quando tem caminho ou está desenhando um.</summary>
    bool PatrolMode => SelPath is not null || _drawPts is not null;
    static int LoopOf(NpcGenSpawn s) => s.Creatures.FirstOrDefault()?.PathLoop ?? 0;

    void SetLoop(int type)
    {
        if (PathSpawn is not { } s || LoopOf(s) == type) return;
        SetCreaturePathLoop?.Invoke(s, type);
        UpdatePathPanel();
        _status.Text = Loc.T("path.loopSet", "Fim do caminho mudado (Ctrl+Z desfaz). Salve o NPCGen e envie ao servidor.");
    }

    /// <summary>
    /// Painel em passos, só com o que serve agora: 1) escolher o monstro; 2) área OU patrulha (um ou outro);
    /// 3) o que fazer no modo escolhido (área: alça / sem área; patrulha: desenhar, terminar, fim do caminho, apagar).
    /// </summary>
    void UpdatePathPanel()
    {
        var s = PathSpawn;
        _pathPanelFor = s;
        bool drawing = _drawPts is not null, hasPath = SelPath is not null, patrol = PatrolMode;
        var area = s is not null && SelectedMark() is { } m ? AreaOf(m.Tag) : null;
        bool hasArea = area is { } a && (a.Sx > 0.25f || a.Sz > 0.25f);
        _pathName.Visible = s is not null;
        _pathName.Text = s is null ? "" : SelectedMark()?.Name ?? "";
        _pathStep1.Text = Loc.T("path.step1", "1. Como ele anda");
        _pathStep2.Text = patrol ? Loc.T("path.step2Path", "2. Caminho de patrulha") : Loc.T("path.step2Area", "2. Área onde anda");
        _pathStep1.Visible = _pathStep2.Visible = s is not null;
        _pathHint.Text = s is null ? Loc.T("path.pick3", "no mundo, para escolher quem vai mudar.\n\nDepois você escolhe como ele anda: numa área (azul) OU num caminho de patrulha (laranja). É um ou outro.")
            : drawing ? Loc.T("path.drawing2", "Clique no chão para pôr os pontos, na ordem em que ele anda. Enter ou botão direito termina; Esc cancela. Terminar perto do 1º ponto fecha um circuito.")
            : hasPath ? Loc.T("path.editHint", "Arraste os quadrados laranja para mudar o caminho. O ponto grande é o começo.")
            : hasArea ? Loc.T("path.areaHint", "Ele anda solto dentro da área azul. Arraste a alça amarela para mudar o tamanho.")
            : Loc.T("path.noAreaHint", "Sem área: ele fica parado no ponto. Para dar uma área, arraste a alça amarela.");
        // NPC comum: o jogo não move, então nada de área/caminho; só o aviso
        if (s is not null && PatrolBlock?.Invoke(s) is { } why) { _pathHint.Text = why; drawing = false; }
        bool blocked = s is not null && PatrolBlock?.Invoke(s) is not null;
        _modeArea.Visible = _modePath.Visible = s is not null && !drawing && !blocked;
        _modeArea.BackColor = s is not null && !patrol ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        _modePath.BackColor = patrol ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        _areaNone.Visible = s is not null && !patrol && hasArea && !blocked;
        _pathStep2.Visible &= !blocked;
        _pathDraw.Visible = s is not null && patrol && !drawing && _pathFile is not null && !blocked;
        _pathDraw.Text = "  " + (hasPath ? Loc.T("path.redraw2", "Redesenhar o caminho") : Loc.T("path.draw", "Desenhar caminho novo"));
        _pathPickRed.Text = Loc.T("path.pickRed", "Clique em uma criatura ou um NPC");
        _pathPickRed.Visible = s is null;
        // sem criatura: nada de botões (só salvar, se ficou caminho sem salvar)
        _pathUndo.Visible = _pathRedo.Visible = s is not null;
        _pathSave.Visible = s is not null || PathDirty;
        _pathDone.Visible = _pathCancel.Visible = drawing;
        _pathLoopTitle.Text = Loc.T("path.loopTitle", "3. No fim do caminho");
        _loopBox.Visible = hasPath && !drawing;
        int loop = s is null ? 0 : LoopOf(s);
        _loop0.BackColor = loop == 0 ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        _loop1.BackColor = loop == 1 ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        _loop2.BackColor = loop is not (0 or 1) ? UiKit.Accent : Color.FromArgb(40, 45, 54);
        _pathDel.Visible = hasPath && !drawing;
        _pathState.Text = _pathFile is null ? Loc.T("path.noFile", "Este mapa não tem path.sev no servidor.")
            : PathDirty ? Loc.T("path.dirty", "● Caminhos não salvos.") : Loc.T("world.sceneCleanLine", "✓ Tudo salvo.");
        _pathState.ForeColor = PathDirty ? Color.FromArgb(255, 200, 90) : Color.Silver;
        if (_tool == Tool.Paths) BeginInvoke(LayoutTools);   // altura do painel acompanha o que está visível
    }

    void SetMode(bool patrol)
    {
        if (PathSpawn is not { } s) return;
        if (!patrol)
        {
            if (_drawPts is not null) CancelDraw();
            if (PathIdOf(s) is > 0 and var id)
            {
                _lastPathOf[s] = id;
                LinkPath(s, 0, null);   // só desliga o ponto do caminho; o caminho continua no path.sev
                _status.Text = string.Format(Loc.T("path.toArea", "Agora anda na área. O caminho {0} ficou guardado: \"Patrulhar um caminho\" volta a ele (Ctrl+Z desfaz)."), id);
            }
            return;
        }
        if (SelPath is not null) return;
        if (_lastPathOf.TryGetValue(s, out var last) && _pathFile?.Paths.Any(p => p.Id == last) == true)
        {
            LinkPath(s, last, null);
            _status.Text = string.Format(Loc.T("path.back", "Voltou a patrulhar o caminho {0} (Ctrl+Z desfaz)."), last);
        }
        else StartDraw();
    }

    void StartDraw() { if (PathSpawn is null || _pathFile is null) return; _drawPts = new(); UpdatePathPanel(); _gl.Focus(); _gl.Invalidate(); }

    void CancelDraw() { _drawPts = null; _drawHover = null; UpdatePathPanel(); _gl.Invalidate(); }

    /// <summary>Caminho desenhado vira um caminho novo do path.sev (ID livre) e o monstro passa a patrulhá-lo.</summary>
    void FinishDraw()
    {
        if (_drawPts is not { } pts || PathSpawn is not { } s || _pathFile is null) { CancelDraw(); return; }
        if (pts.Count < 2) { _status.Text = Loc.T("path.needTwo", "Um caminho precisa de pelo menos 2 pontos."); return; }
        bool circuit = pts.Count >= 3 && new Vector2(pts[^1].X - pts[0].X, pts[^1].Z - pts[0].Z).Length < 5;
        if (circuit) pts[^1] = pts[0];   // fechou no 1º ponto: o último vira o começo
        int id = _pathFile.Paths.Count == 0 ? 1 : _pathFile.Paths.Max(p => p.Id) + 1;
        var np = BuildPath(id, _pathFile.Paths.FirstOrDefault()?.Version ?? 3, pts);
        _drawPts = null; _drawHover = null;
        // já tinha caminho: o novo toma o lugar (o antigo sai do path.sev se ninguém mais usa) — nada de 2 caminhos no mesmo monstro
        var old = SelPath;
        LinkPath(s, id, np, remove: old is not null && (PathUsers?.Invoke(old.Id) ?? 1) <= 1 ? old : null);
        if (circuit) SetCreaturePathLoop?.Invoke(s, 2);
        _status.Text = string.Format(Loc.T("path.created", "Caminho {0} criado com {1} pontos e ligado ao ponto (Ctrl+Z desfaz). Salve os caminhos e o NPCGen."), id, pts.Count)
            + (circuit ? "  " + Loc.T("path.circuit", "Circuito fechado: ele dá voltas.") : "");
        UpdatePathPanel();
    }

    /// <summary>Caminho novo de retas (âncoras a 1/3 e 2/3), versão e IDs globais como os do arquivo.</summary>
    static SevPath BuildPath(int id, int version, List<Vector3> pts)
    {
        var np = new SevPath { Version = version, Id = id, GlobalId = -1, NextGlobalId = -1 };
        for (int i = 0; i < pts.Count; i++)
        {
            var t = pts[Math.Min(i + 1, pts.Count - 1)] - pts[Math.Max(i - 1, 0)];   // direção: tangente (só referência do editor)
            np.Points.Add((V(pts[i]), V(t)));
        }
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 a = pts[i], d = pts[i + 1];
            np.Segments.Add(new SevSegment(V(a + (d - a) / 3), V(a + (d - a) * 2 / 3), i, i + 1, (d - a).Length));
        }
        return np;
    }

    /// <summary>--path-edit-test: cria caminho, move ponto e regrava (nada é gravado em disco).</summary>
    internal static string PathSelfTest(byte[] sev)
    {
        var f = PathFile.Read(sev);
        int id = f.Paths.Max(p => p.Id) + 1, n0 = f.Paths.Count;
        f.Paths.Add(BuildPath(id, f.Paths[0].Version, new() { new(0, 10, 0), new(30, 10, 0), new(30, 10, 40) }));
        var back = PathFile.Read(f.Write());
        var np = back.Paths.Single(p => p.Id == id);
        var s0 = back.Paths[0]; var before = s0.Segments[0]; var p1 = s0.Points[1].Pos;
        MovePoint(s0, 1, new Vector3(2, 0, 1));
        var after = s0.Segments[0];
        bool anchor = MathF.Abs(after.AnchorTail.X - before.AnchorTail.X - 2) < 1e-3f && MathF.Abs(after.AnchorHead.X - before.AnchorHead.X) < 1e-3f;
        bool same = PathFile.Read(back.Write()).Write().AsSpan().SequenceEqual(back.Write());
        return $"caminhos {n0} -> {back.Paths.Count}; novo {id}: {np.Points.Count} pontos, {np.Segments.Count} segmentos, comprimento {np.Segments.Sum(x => x.Length):0.0} m (esperado 70); ponto 1 {p1} -> {s0.Points[1].Pos}; âncora segue o ponto {anchor}; comprimento {before.Length:0.0} -> {after.Length:0.0}; regrava igual {same}";
    }

    /// <summary>Apaga o caminho do monstro: ele volta a andar na área; o caminho sai do path.sev se ninguém mais usa.</summary>
    void DeletePath()
    {
        if (PathSpawn is not { } s || SelPath is not { } p) return;
        bool others = (PathUsers?.Invoke(p.Id) ?? 1) > 1;
        LinkPath(s, 0, null, remove: others ? null : p);
        _status.Text = others ? Loc.T("path.unlinked", "O ponto deixou o caminho (outros pontos ainda usam ele, então o caminho fica no path.sev).")
            : Loc.T("path.deleted", "Caminho apagado; o ponto volta a andar na área (Ctrl+Z desfaz).");
    }

    /// <summary>Uma ação do usuário = 1 passo: muda o path.sev (adiciona/remove) e liga o monstro ao ID.</summary>
    void LinkPath(NpcGenSpawn s, int id, SevPath? add, SevPath? remove = null)
    {
        if (_pathFile is null) return;
        var before = _pathFile.Write();
        if (add is not null) _pathFile.Paths.Add(add);
        if (remove is not null) _pathFile.Paths.Remove(remove);
        bool npc = PathIdOf(s) != id;
        if (npc) SetCreaturePath?.Invoke(s, id);
        _pUndo.Add(new PathStep(before, UndoDepth?.Invoke() ?? 0, npc)); _pRedo.Clear();
        RebuildPaths(); RefreshMarkers(); UpdatePathPanel(); _gl.Invalidate();
    }

    /// <summary>Ctrl+Z/Ctrl+Y dos caminhos: só quando foi a última ação (o NPCGen não andou depois).</summary>
    bool PathUndo(bool redo)
    {
        var from = redo ? _pRedo : _pUndo; var to = redo ? _pUndo : _pRedo;
        if (_pathFile is null || from.Count == 0) return false;
        var top = from[^1];
        if (top.Npc != (UndoDepth?.Invoke() ?? 0)) return false;
        from.RemoveAt(from.Count - 1);
        var now = _pathFile.Write();
        if (top.WithNpc) { if (redo) RedoAction?.Invoke(); else UndoAction?.Invoke(); }
        to.Add(new PathStep(now, UndoDepth?.Invoke() ?? 0, top.WithNpc));
        _pathFile = PathFile.Read(top.Path);
        RebuildPaths(); RefreshMarkers(); UpdatePathPanel(); _gl.Invalidate();
        _status.Text = redo ? Loc.T("path.redone", "Caminho: refeito.") : Loc.T("path.undone", "Caminho: desfeito (Ctrl+Y refaz).");
        return true;
    }

    void SavePaths()
    {
        if (_pathFile is null || _pathSev is null) return;
        var bytes = _pathFile.Write();
        if (_pathSaved is not null && bytes.AsSpan().SequenceEqual(_pathSaved)) { _status.Text = Loc.T("path.nothing", "Caminhos: nada a salvar."); return; }
        try
        {
            if (!PathFile.Read(bytes).Write().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException(Loc.T("world.sceneBad", "a releitura não bateu; nada foi gravado"));
            AtomicFileSave.Write(_pathSev, bytes);   // backup fora do servidor (BackupStore)
            _pathSaved = bytes;
            _status.Text = Loc.T("path.saved", "path.sev salvo (backup feito). A ligação do monstro ao caminho vai no npcgen.data: salve também no NPCGen. Reinicie o mapa no servidor.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FileSaveException)
        { MessageBox.Show(this, ex.Message, Loc.T("path.save", "Salvar caminhos (path.sev)"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        UpdatePathPanel();
    }

    // ---------- mouse (ligado nos eventos do _gl) ----------

    int PathPointAt(Point p)
    {
        if (SelPath is not { } sp) return -1;
        for (int i = 0; i < sp.Points.Count; i++)
            if (Project(G(sp.Points[i].Pos)) is { } q && MathF.Abs(q.X - p.X) < 9 && MathF.Abs(q.Y - p.Y) < 9) return i;
        return -1;
    }

    bool PathMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Paths || e.Button != MouseButtons.Left) return false;
        if (_drawPts is not null) { _dragging = true; return true; }   // desenhando: arrastar gira a câmera, clicar põe ponto
        if (PathPointAt(e.Location) is >= 0 and var k) { _dragPt = k; _dragBefore = _pathFile!.Write(); return true; }
        return false;
    }

    bool PathMouseMove(Point p)
    {
        if (_tool != Tool.Paths) return false;
        if (_dragPt >= 0 && SelPath is { } sp && Ray(p) is { } at)
        {
            MovePoint(sp, _dragPt, at - G(sp.Points[_dragPt].Pos));
            RebuildPaths(); _gl.Invalidate();
            return true;
        }
        if (_drawPts is not null && !_dragging) { _drawHover = Ray(p); _gl.Invalidate(); }
        int h = _drawPts is null ? PathPointAt(p) : -1;
        if (h != _hoverPt) { _hoverPt = h; _gl.Invalidate(); }
        return false;
    }

    bool PathMouseUp(MouseEventArgs e)
    {
        if (_tool != Tool.Paths) return false;
        if (_dragPt >= 0)
        {
            _dragPt = -1;
            if (_dragBefore is { } b && !b.AsSpan().SequenceEqual(_pathFile!.Write())) { _pUndo.Add(new PathStep(b, UndoDepth?.Invoke() ?? 0, false)); _pRedo.Clear(); }
            _dragBefore = null; UpdatePathPanel(); _gl.Invalidate();
            return true;
        }
        if (_drawPts is null) return false;
        bool click = Math.Abs(e.X - _down.X) <= 4 && Math.Abs(e.Y - _down.Y) <= 4;
        if (e.Button == MouseButtons.Right && click) { FinishDraw(); return true; }
        if (e.Button == MouseButtons.Left && click && Ray(e.Location) is { } at)
        {
            // não atravessa nada sólido (pedra, árvore, construção): o monstro não passaria, igual ao jogador
            // 2º clique no mesmo lugar põe assim mesmo (chão de construção, ponte: o terreno fica embaixo do brush)
            bool insist = _blockedClick is { } bc && (bc - at).Length < 1.5f;
            _blockedClick = null;
            if (!insist && _drawPts.Count > 0 && BlockedBetween(_drawPts[^1], at) is { } where)
            {
                _blockedClick = at;
                _status.Text = string.Format(Loc.T("path.blocked2", "Esse trecho passa por dentro de algo sólido (perto de {0:0}, {1:0}): ponha um ponto antes, contornando. Se for chão de ponte ou construção, clique de novo no mesmo lugar."), where.X, where.Z);
                System.Media.SystemSounds.Beep.Play();
                return true;
            }
            _drawPts.Add(at); _gl.Invalidate();
        }
        return e.Button == MouseButtons.Left;
    }

    /// <summary>Teclas da ferramenta: Enter termina o desenho, Esc cancela. true = tecla usada.</summary>
    bool PathKey(Keys k)
    {
        if (_tool != Tool.Paths || _drawPts is null) return false;
        if (k == Keys.Enter) { FinishDraw(); return true; }
        if (k == Keys.Escape) { CancelDraw(); return true; }
        if (k == Keys.Back && _drawPts.Count > 0) { _drawPts.RemoveAt(_drawPts.Count - 1); _gl.Invalidate(); return true; }
        return false;
    }

    /// <summary>Move 1 ponto e as âncoras ligadas a ele (a curva segue o ponto); refaz o comprimento dos segmentos.</summary>
    static void MovePoint(SevPath sp, int k, Vector3 d)
    {
        var (pos, dir) = sp.Points[k];
        sp.Points[k] = (V(G(pos) + d), dir);
        for (int i = 0; i < sp.Segments.Count; i++)
        {
            var s = sp.Segments[i];
            if (s.Head == k) s = s with { AnchorHead = V(G(s.AnchorHead) + d) };
            if (s.Tail == k) s = s with { AnchorTail = V(G(s.AnchorTail) + d) };
            if (s.Head == k || s.Tail == k) sp.Segments[i] = s with { Length = SegLength(sp, s) };
        }
    }

    static float SegLength(SevPath sp, SevSegment s)
    {
        if (s.Head < 0 || s.Tail < 0 || s.Head >= sp.Points.Count || s.Tail >= sp.Points.Count) return s.Length;
        Vector3 a = G(sp.Points[s.Head].Pos), b = G(s.AnchorHead), c = G(s.AnchorTail), d = G(sp.Points[s.Tail].Pos), prev = a;
        float len = 0;
        for (int i = 1; i <= 24; i++)
        {
            float t = i / 24f, u = 1 - t;
            var q = a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t);
            len += (q - prev).Length; prev = q;
        }
        return len;
    }

    static Vec3 V(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 G(Vec3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Pontos do caminho escolhido (quadrados, o começo maior) e o desenho em andamento.</summary>
    void DrawPathEdit()
    {
        if (_tool != Tool.Paths) return;
        if (!ReferenceEquals(_pathPanelFor, PathSpawn)) BeginInvoke(UpdatePathPanel);
        GL.Disable(EnableCap.DepthTest);
        if (_drawPts is null && SelPath is { } sp)
            for (int i = 0; i < sp.Points.Count; i++)
            {
                bool hot = i == _hoverPt || i == _dragPt;
                GL.PointSize(i == 0 ? 14 : hot ? 12 : 9);
                GL.Color3(hot ? 1f : 1f, hot ? 0.95f : 0.6f, hot ? 0.4f : 0.15f);
                GL.Begin(PrimitiveType.Points); var p = sp.Points[i].Pos; GL.Vertex3(Gl(p.X, p.Y + 0.5f, p.Z)); GL.End();
            }
        if (_drawPts is { } pts)
        {
            GL.LineWidth(3); GL.Color3(0.3f, 1f, 0.5f);
            GL.Begin(PrimitiveType.LineStrip);
            foreach (var q in pts) GL.Vertex3(Gl(q.X, q.Y + 0.5f, q.Z));
            if (_drawHover is { } h) GL.Vertex3(Gl(h.X, h.Y + 0.5f, h.Z));
            GL.End();
            GL.PointSize(10); GL.Begin(PrimitiveType.Points);
            foreach (var q in pts) GL.Vertex3(Gl(q.X, q.Y + 0.5f, q.Z));
            GL.End();
            // perto do 1º ponto: ele fica maior (soltar ali fecha o circuito)
            if (pts.Count >= 2 && _drawHover is { } hv && new Vector2(hv.X - pts[0].X, hv.Z - pts[0].Z).Length < 5)
            { GL.PointSize(18); GL.Color3(1f, 0.9f, 0.3f); GL.Begin(PrimitiveType.Points); GL.Vertex3(Gl(pts[0].X, pts[0].Y + 0.5f, pts[0].Z)); GL.End(); }
            // aviso sem fundo perto do cursor: confirma com Enter
            if (_drawHover is { } at)
                DrawLabel("↵  " + Loc.T("path.enterHint", "Pressione Enter para confirmar"), new Vector3(at.X, at.Y + 3, at.Z), Color.FromArgb(255, 230, 120));
        }
        GL.PointSize(1); GL.LineWidth(1); GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }
}
