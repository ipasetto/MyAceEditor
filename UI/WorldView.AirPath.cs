using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Marcador de caminho (usuário 01/11: para TODAS as criaturas). Na ferramenta Caminhos, em patrulha, o modelo da criatura
/// dá lugar a um CÍRCULO com setas X/Y/Z (como o gizmo de mover): clicar no chão leva o círculo até lá (mantendo a altura),
/// as setas movem (verde = altura; o gs leva quem voa ou nada até a altura de cada ponto: npcsession.cpp follow_target,
/// PW-KB §S22) e "Marcar ponto aqui" (E ou botão direito) põe o ponto onde ele está: os pontos vão se ligando no caminho; terminar é pelo botão
/// (Espaço é da câmera), depois é só salvar. Clicar num ponto do caminho o escolhe: as setas passam a mover esse ponto. Cada ponto tem uma haste
/// até o chão, para ver a altura.
/// </summary>
internal sealed partial class WorldView
{
    Vector3? _flyCursor;            // marcador (coordenadas do jogo)
    int _flySel = -1;               // ponto do caminho escolhido (as setas movem ele); -1 = movem o marcador
    int _flyAxis, _flyHover;        // seta em uso / sob o mouse (1 X, 2 Y altura, 3 Z)
    Point _flyDown; Vector3 _flyStart; byte[]? _flyBefore;
    NpcGenSpawn? _flyFor;
    Button _flyMark = null!;

    /// <summary>Criatura em patrulha (desenhando ou com caminho): marcador no lugar do modelo.</summary>
    bool AirPatrol => _tool == Tool.Paths && PathSpawn is { } ps && BlockOf(ps) is null && (_drawPts is not null || SelPath is not null);

    // PatrolBlock e InhabitOf procuram a criatura no elements: guardados por ponto, senão o 3D travava (chamados a cada
    // quadro, para cada marcador, e a cada movimento do mouse; usuário 01/11)
    NpcGenSpawn? _cacheFor; string? _cacheBlock; int _cacheInhabit;
    void CacheFor(NpcGenSpawn s)
    {
        if (ReferenceEquals(_cacheFor, s)) return;
        _cacheFor = s; _cacheBlock = PatrolBlock?.Invoke(s);
        _cacheInhabit = s.Creatures.Count > 0 ? InhabitOf?.Invoke(s.Creatures[0].Id) ?? 0 : 0;
    }
    string? BlockOf(NpcGenSpawn s) { CacheFor(s); return _cacheBlock; }
    int InhabitCached(NpcGenSpawn s) { CacheFor(s); return _cacheInhabit; }

    /// <summary>O modelo da criatura some enquanto o marcador está na tela (fica só o nome).</summary>
    bool HideModelFor(Marker m) => AirPatrol && ReferenceEquals(m.Tag, PathSpawn);
    /// <summary>Uma vez por quadro: o ponto cujo modelo some (null = nenhum).</summary>
    object? HiddenModelTag() => AirPatrol ? PathSpawn : null;

    void InitAirPath()
    {
        _flyMark = SmallButton("", Loc.T("path.flyMark3", "Marcar ponto aqui (E)"), FlyMark, main: true);
        _toolTip.SetToolTip(_flyMark, Loc.T("path.flyMarkTip3", "Põe um ponto do caminho onde está o círculo; cada ponto se liga ao anterior. Para acabar, clique em Terminar o desenho."));
    }

    /// <summary>Outra criatura escolhida: o marcador volta para onde ela nasce.</summary>
    void SyncFlyCursor()
    {
        if (ReferenceEquals(_flyFor, PathSpawn)) return;
        _flyFor = PathSpawn; _flySel = -1; _flyAxis = 0;
        _flyCursor = PathSpawn is { } s ? new Vector3(s.Position.X, s.Position.Y, s.Position.Z) : null;
    }

    Vector3 FlyAnchor => _flySel >= 0 && SelPath is { } sp && _flySel < sp.Points.Count ? G(sp.Points[_flySel].Pos) : _flyCursor ?? Vector3.Zero;

    int FlyHit(Point p)
    {
        if (!AirPatrol) return 0;
        var c = FlyAnchor; float L = GizmoSize(c);
        if (Project(c) is not { } a) return 0;
        for (int k = 0; k < 3; k++)
            if (Project(c + GizmoDirs[k] * L * 1.18f) is { } b && SegDist(p, a, b, 0.2f) < 9) return k + 1;
        return 0;
    }

    bool FlyMouseDown(MouseEventArgs e)
    {
        if (!AirPatrol || e.Button != MouseButtons.Left) return false;
        SyncFlyCursor();
        if (FlyHit(e.Location) is > 0 and var ax)
        {
            _flyAxis = ax; _flyDown = e.Location; _flyStart = FlyAnchor;
            _flyBefore = _flySel >= 0 ? _pathFile?.Write() : null;
            return true;
        }
        // fora do desenho, clicar num ponto do caminho o escolhe (as setas passam a mover ele)
        if (_drawPts is null && PathPointAt(e.Location) is >= 0 and var k)
        {
            _flySel = k; _gl.Invalidate();
            _status.Text = string.Format(Loc.T("path.flyPicked", "Ponto {0} escolhido: arraste as setas para mover (verde = altura)."), k + 1);
            return true;
        }
        return false;
    }

    bool FlyMouseMove(Point p)
    {
        if (!AirPatrol) return false;
        if (_flyAxis == 0)
        {
            int h = FlyHit(p);
            if (h != _flyHover) { _flyHover = h; _gl.Invalidate(); }
            return false;
        }
        // quanto o mouse andou ao longo da seta na tela vira metros no eixo (passo de 10 cm), como o gizmo de mover
        var c = _flyStart; float L = GizmoSize(c); var dir = GizmoDirs[_flyAxis - 1];
        if (Project(c) is not { } a || Project(c + dir * L) is not { } b) return true;
        float sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
        if (len2 < 4) return true;
        float t = MathF.Round(((p.X - _flyDown.X) * sx + (p.Y - _flyDown.Y) * sy) / len2 * L * 10) / 10;
        var np = c + dir * t;
        if (_flySel >= 0 && SelPath is { } sp && _flySel < sp.Points.Count) { MovePoint(sp, _flySel, np - G(sp.Points[_flySel].Pos)); ResamplePath(sp); }
        else _flyCursor = np;
        float ground = _t.HeightAt(np.X, np.Z) ?? np.Y;
        _status.Text = string.Format(Loc.T("path.flyMoving", "X {0:0.0}   altura {1:0.0} ({2:0.0} m acima do chão)   Z {3:0.0}"), np.X, np.Y, np.Y - ground, np.Z);
        _gl.Invalidate();
        return true;
    }

    bool FlyMouseUp()
    {
        if (_flyAxis == 0) return false;
        _flyAxis = 0;
        if (_flyBefore is { } bf && _pathFile is not null && !bf.AsSpan().SequenceEqual(_pathFile.Write())) { _pUndo.Add(new PathStep(bf, UndoDepth?.Invoke() ?? 0, false)); _pRedo.Clear(); }
        _flyBefore = null; UpdatePathPanel(); _gl.Invalidate();
        return true;
    }

    /// <summary>Clique no chão desenhando um voador: o marcador vai até lá, na mesma altura acima do chão.</summary>
    void FlyMoveTo(Vector3 ground)
    {
        SyncFlyCursor();
        var c = _flyCursor ?? ground;
        float lift = c.Y - (_t.HeightAt(c.X, c.Z) ?? c.Y);
        _flyCursor = new Vector3(ground.X, ground.Y + MathF.Max(0, lift), ground.Z);
        _flySel = -1; _gl.Invalidate();
    }

    /// <summary>Marca o ponto onde está o marcador (começa um desenho novo se não houver).</summary>
    void FlyMark()
    {
        if (_tool != Tool.Paths || PathSpawn is null || _pathFile is null) return;
        SyncFlyCursor();
        if (_drawPts is null) { var keep = _flyCursor; StartDraw(); _flyFor = PathSpawn; _flyCursor = keep; }   // começa o desenho sem perder o círculo
        if (_drawPts is null || _flyCursor is not { } c) return;
        // quem anda no chão não atravessa nada sólido (pedra, árvore, construção); marcar de novo no mesmo lugar põe assim mesmo
        bool insist = _blockedClick is { } bc && (bc - c).Length < 1.5f;
        _blockedClick = null;
        if (!PathAir && !insist && _drawPts.Count > 0 && BlockedBetween(_drawPts[^1], c) is { } where)
        {
            _blockedClick = c;
            _status.Text = string.Format(Loc.T("path.blocked3", "Esse trecho passa por dentro de algo sólido (perto de {0:0}, {1:0}): mova o círculo para contornar. Se for chão de ponte ou construção, marque de novo."), where.X, where.Z);
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        _drawPts.Add(c); _flySel = -1;
        _status.Text = string.Format(Loc.T("path.flyMarked3", "Ponto {0} marcado. Mova o círculo e marque o próximo (E ou botão direito); para acabar, clique em Terminar o desenho."), _drawPts.Count);
        UpdatePathPanel(); _gl.Invalidate();
    }

    void DrawAirPath()
    {
        if (!AirPatrol) return;
        SyncFlyCursor();
        void V(Vector3 q) => GL.Vertex3(Gl(q.X, q.Y, q.Z));
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting);
        // haste de cada ponto até o chão (para ver a altura) e uma marquinha no chão
        var pts = _drawPts ?? SelPath?.Points.Select(p => G(p.Pos)).ToList() ?? new List<Vector3>();
        GL.LineWidth(1); GL.Color4(1f, 0.75f, 0.3f, 0.7f);
        GL.Begin(PrimitiveType.Lines);
        foreach (var q in pts) { float g = _t.HeightAt(q.X, q.Z) ?? q.Y; V(q); V(new Vector3(q.X, g + 0.2f, q.Z)); }
        GL.End();
        if (_flySel >= 0 && _flySel < pts.Count)
        { GL.PointSize(16); GL.Color3(1f, 1f, 1f); GL.Begin(PrimitiveType.Points); V(pts[_flySel] + new Vector3(0, 0.5f, 0)); GL.End(); }
        // marcador: losango (octaedro em linhas) + haste até o chão + setas X/Y/Z (só move o marcador quando nenhum ponto está escolhido)
        var c = FlyAnchor; float L = GizmoSize(c);
        if (_flySel < 0)
        {
            // círculo deitado (onde o ponto vai cair) + haste até o chão
            float r = L * 0.45f;
            GL.LineWidth(3); GL.Color3(0.3f, 0.9f, 1f);
            GL.Begin(PrimitiveType.LineLoop);
            for (int i = 0; i < 40; i++) { float t = i / 40f * MathF.Tau; V(c + new Vector3(MathF.Sin(t) * r, 0, MathF.Cos(t) * r)); }
            GL.End();
            GL.PointSize(8); GL.Begin(PrimitiveType.Points); V(c); GL.End();
            float g = _t.HeightAt(c.X, c.Z) ?? c.Y;
            GL.LineWidth(1); GL.Begin(PrimitiveType.Lines); V(c); V(new Vector3(c.X, g, c.Z)); GL.End();
        }
        int hot = _flyAxis > 0 ? _flyAxis : _flyHover;
        Color[] cols = { Color.Red, Color.LimeGreen, Color.DodgerBlue };
        GL.LineWidth(3);
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
        GL.LineWidth(1); GL.PointSize(1);
        // rótulo da seta de altura e do atalho (textura ligada, cor branca: ver DrawPathEdit)
        GL.Enable(EnableCap.Texture2D); GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.Color4(1f, 1f, 1f, 1f);
        DrawLabel(Loc.T("gizmo.y", "Y · altura"), c + Vector3.UnitY * L * 1.35f, Color.LimeGreen);
        if (_flySel < 0) DrawLabel(Loc.T("path.flyE", "E ou botão direito = marcar ponto"), c - Vector3.UnitY * L * 0.45f, Color.FromArgb(150, 230, 255));
        GL.BindTexture(TextureTarget.Texture2D, 0); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest); GL.Color3(1f, 1f, 1f);
    }
}
