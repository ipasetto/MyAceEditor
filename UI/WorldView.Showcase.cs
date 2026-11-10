namespace MyAceEditor.UI;

/// <summary>
/// Fotos da página do GitHub (--readme-shots): cada cena abre uma ferramenta com algo escolhido e a foto junta o 3D (que o
/// DrawToBitmap da janela não pega), o painel da ferramenta e o cartão de informação. Só olha: nada é mexido nem gravado.
/// </summary>
internal sealed partial class WorldView
{
    /// <summary>Espera o carregamento em volta da câmera acalmar (até 90 s).</summary>
    internal void ShotWait(double quietSeconds = 1.5)
    {
        var until = DateTime.Now.AddSeconds(90); var quiet = DateTime.MaxValue;
        while (DateTime.Now < until)
        {
            Application.DoEvents(); Thread.Sleep(30);
            if (Loading) quiet = DateTime.MaxValue; else if (quiet == DateTime.MaxValue) quiet = DateTime.Now; else if ((DateTime.Now - quiet).TotalSeconds > quietSeconds) break;
        }
    }

    /// <summary>Janela inteira com o 3D por baixo e os painéis flutuantes por cima, na ordem em que aparecem.</summary>
    internal Bitmap ComposeShot()
    {
        var full = new Bitmap(Width, Height);
        DrawToBitmap(full, new Rectangle(0, 0, Width, Height));
        var origin = PointToScreen(Point.Empty);
        int ox = origin.X - Left, oy = origin.Y - Top;
        using var g = Graphics.FromImage(full);
        Point At(Control c) => PointToClient(c.Parent!.PointToScreen(c.Location));
        using (var frame = CaptureFrame()) if (frame is not null) { var p = At(_gl); g.DrawImage(frame, ox + p.X, oy + p.Y, _gl.Width, _gl.Height); }
        void Over(Control? c)
        {
            if (c is null || !c.Visible || c.Width <= 0 || c.Height <= 0) return;
            using var b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
            var p = At(c); g.DrawImage(b, ox + p.X, oy + p.Y);
        }
        Over(_domains); Over(_info); Over(_tools);
        return full;
    }

    /// <summary>Monta uma cena das fotos. Devolve uma linha do que ficou na tela (para conferir).</summary>
    internal string ShotScene(string name)
    {
        Point center = new(_gl.Width / 2, _gl.Height * 3 / 5);
        string Hover() { _hover = Ray(center); return _hover is null ? "sem chão no meio" : "pincel no meio"; }
        if (_tool != Tool.None) CloseTool();
        switch (name)
        {
            case "hero":
                return "menu de ferramentas";
            case "objects":
            {
                DraftLoadTest(); ShotWait();   // abre Objetos e espera os pedaços em volta (o cartão completo precisa do rascunho)
                var ahead = Ray(center) is { } g ? new OpenTK.Mathematics.Vector3(g.X, g.Y, -g.Z) : _cam + Forward * 70;   // o que está no meio da tela
                if (_buildings.Nearest(ahead) is not { } near) return "nenhuma construção";
                _sceneSel = new BuildingSel(near.File, near.Center); ShowObjPanel(); _gl.Invalidate();
                return "construção " + Path.GetFileName(near.File);
            }
            case "terrain":
            {
                OpenTool(Tool.Terrain);
                string h = Hover();
                if (_hover is { } p) TexEyedropperAt(new System.Numerics.Vector3(p.X, p.Y, p.Z));
                return h + ", textura " + _texSel;
            }
            case "grass":
                OpenTool(Tool.Terrain); OpenGrassPanel();
                if (_grass.Scene is { GrassTypes.Count: > 0 }) { _brushType = 0; BrushPaint(); }
                return Hover() + ", grama " + _brushType;
            case "light":
                OpenTool(Tool.Terrain); SetLightMode(1);
                return Hover();
            case "paths":
                OpenTool(Tool.Paths); _gl.Invalidate();
                return "caminhos";
            case "quests":
            {
                OpenTool(Tool.Quests);
                var camGame = new System.Numerics.Vector3(_cam.X, _cam.Y, -_cam.Z);
                foreach (var m in _allMarks.Where(m => m.Id > 0).OrderBy(m => (m.X - camGame.X) * (m.X - camGame.X) + (m.Z - camGame.Z) * (m.Z - camGame.Z)).Take(80))
                    if (NpcQuests?.Invoke(m.Id) is { Gives.Count: > 0 } q) { _questNpc = m.Id; ShowQuestTool(q); return "NPC " + m.Id; }
                return "nenhum NPC com quest perto";
            }
            case "npcs":
                OpenTool(Tool.Create);
                return "criar";
            case "teleports":
                OpenTool(Tool.Teleports);
                return "teleportes";
            case "zones":
                ZoneShot(1);
                return "zona aberta";
            case "territories":
                OpenTool(Tool.Domains);
                return _domains is null ? "sem territórios" : "territórios";
        }
        return "cena desconhecida";
    }

    /// <summary>
    /// Teste do clique nas criaturas (usuário 09/11: não dava para escolher criatura/NPC para o caminho nem mover): em cada
    /// ferramenta, acha a criatura mais perto à vista, simula apertar e soltar o botão em cima dela e diz o que aconteceu.
    /// </summary>
    internal string ClickTest()
    {
        var r = new List<string>();
        void Raise(string ev, MouseEventArgs e) => typeof(Control).GetMethod(ev, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(_gl, [e]);
        foreach (var tool in new[] { Tool.None, Tool.Paths, Tool.Create })
        {
            if (tool == Tool.None) { if (_tool != Tool.None) LeaveTool(); } else OpenTool(tool);
            ShotWait(0.5);
            Marker? target = null; PointF at = default;
            foreach (var m in _nearMarks.OrderBy(Dist2))
            {
                if (Dist2(m) > 80 * 80) break;
                var top = m.Model is not null && _models.Top(m.Model) is { } tt ? Math.Max(tt, 1) : 2;
                if (Project(PosOf(m) + new OpenTK.Mathematics.Vector3(0, top / 2, 0)) is { } p && p.X > 40 && p.Y > 40 && p.X < _gl.Width - 300 && p.Y < _gl.Height - 60) { target = m; at = p; break; }
            }
            if (target is null) { r.Add($"{tool}: nenhuma criatura à vista (marcas perto {_nearMarks.Count})"); continue; }
            var pt = new Point((int)at.X, (int)at.Y);
            var pick = PickScreen(pt);
            _status.Text = "";
            Raise("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, pt.X, pt.Y, 0));
            object? moving = _movingTag; bool drag = _dragging; int oax = _objAxis, gax = _gizmoAxis;
            Raise("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, pt.X, pt.Y, 0));
            var sel = SelectedMark();
            r.Add($"{tool}: alvo \"{target.Name}\" em {pt.X},{pt.Y}; PickScreen={(pick is null ? "nada" : pick.Name)}; apertou: movendo={(moving is null ? "nada" : "sim")} girando-câmera={drag} setaObj={oax} seta={gax}; escolhido depois={(sel is null ? "nada" : sel.Name)} (igual={ReferenceEquals(sel?.Tag, target.Tag) || Equals(sel?.Tag, target.Tag)}); barra=\"{_status.Text}\"" + (tool == Tool.Paths ? $"; painel do caminho=\"{_pathName.Text}\" criatura do caminho={PathSpawn is not null}" : ""));
        }
        if (_tool != Tool.None) LeaveTool();
        r.Add(NpcModeTest());
        return string.Join(" | ", r);
    }

    /// <summary>
    /// Mover/Girar da criatura (usuário 10/11): com a criatura escolhida e o painel de baixo aberto, V mostra só as setas e
    /// R só o anel; arrastar o anel gira (Ctrl+Z volta); o menu de ferramentas não cobre o painel; ladrilhos baixos.
    /// </summary>
    string NpcModeTest()
    {
        if (SelectedMark() is not { } m) return "modo: nada escolhido";
        if (!_creature.Visible) ShowCreatureInfo?.Invoke(m.Tag, _creature);   // painel de baixo aberto, como ao clicar na criatura
        ShotWait(0.5);
        try
        {
            using var pb = new Bitmap(_creature.Width, Math.Max(1, _creature.Height));
            _creature.DrawToBitmap(pb, new Rectangle(0, 0, pb.Width, pb.Height));
            pb.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "creature-panel.png"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { MyAceEditor.PerfectWorld.AppLog.Error("teste: foto do painel da criatura", ex); }
        var c = PosOf(m); float L = GizmoSize(c);
        Point P(OpenTK.Mathematics.Vector3 q) => Project(q) is { } f ? new Point((int)f.X, (int)f.Y) : Point.Empty;
        var arrow = P(c + GizmoDirs[0] * L * 1.12f);   // perto da ponta, longe do anel (raio 0,8)
        var ring = P(c + new OpenTK.Mathematics.Vector3(MathF.Sin(1.2f), 0, MathF.Cos(1.2f)) * L * 0.8f);
        SetNpcMode(0); int moveArrow = GizmoHit(arrow), moveRing = GizmoHit(ring);
        SetNpcMode(1); int turnArrow = GizmoHit(arrow), turnRing = GizmoHit(ring);
        // girar arrastando o anel
        void Raise(string ev, MouseEventArgs e) => typeof(Control).GetMethod(ev, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(_gl, [e]);
        float yaw0 = m.Yaw;
        var to = P(c + new OpenTK.Mathematics.Vector3(MathF.Sin(2.2f), 0, MathF.Cos(2.2f)) * L * 0.8f);
        Raise("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, ring.X, ring.Y, 0));
        Raise("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, (ring.X + to.X) / 2, (ring.Y + to.Y) / 2, 0));
        Raise("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0));
        Raise("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
        RefreshMarkers(); ShotWait(0.3);
        float yaw1 = SelectedMark()?.Yaw ?? yaw0;
        bool undo = UndoAny(); RefreshMarkers(); ShotWait(0.3);
        float yaw2 = SelectedMark()?.Yaw ?? yaw1;
        // menu x painel de baixo (coordenadas da janela)
        bool panel = _creature.Visible;
        bool noCover = !panel || _tools.Bottom <= _creature.Top || _toolsFolded;
        int tile = _toolMenu.Controls.Cast<Control>().Max(t => t.Height);
        // janela baixa com o painel aberto: o menu recolhe sozinho; volta ao tamanho normal: abre de novo
        var ws0 = WindowState; WindowState = FormWindowState.Normal; int h0 = Height; Height = 640; Application.DoEvents(); LayoutTools();
        bool foldedSmall = _toolsFolded, coverSmall = _tools.Bottom > _creature.Top && !_toolsFolded; string small = $"janela {Height} cena {_gl.Height} painel {_creature.Visible}/{_creature.Top}";
        Height = h0; WindowState = ws0; Application.DoEvents(); LayoutTools();
        bool reopened = !_toolsFolded;
        SetNpcMode(0);
        return $"modo: Mover -> seta={moveArrow} anel={moveRing}; Girar -> seta={turnArrow} anel={turnRing}; girou {yaw0:0}->{yaw1:0}, Ctrl+Z={undo} voltou {yaw2:0}; painel aberto={panel} menu não cobre={noCover} (menu {_tools.Top}-{_tools.Bottom}, painel em {_creature.Top}, recolhido={_toolsFolded}); ladrilho maior {tile} px; janela baixa ({small}): recolheu={foldedSmall} cobriu={coverSmall}, voltou aberto={reopened}"
            + $"; ok={moveArrow is >= 1 and <= 3 && moveRing == 0 && turnArrow == 0 && turnRing == 4 && MathF.Abs(yaw1 - yaw0) > 1 && undo && MathF.Abs(yaw2 - yaw0) < 0.5f && noCover && tile <= 52 && foldedSmall && !coverSmall && reopened}";
    }

    /// <summary>
    /// Teste ("dftpathhover"): desenhando uma patrulha, 300 movimentos do mouse não recalculam a ponta; o quadro seguinte
    /// recalcula uma vez, e a ponta cai no ponto do chão embaixo do último movimento.
    /// </summary>
    internal string PathHoverTest()
    {
        OpenTool(Tool.Paths); ShotWait(0.3);
        _drawPts = new();
        void Raise(MouseEventArgs e) => typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(_gl, [e]);
        int before = PathRays;
        var last = new Point(_gl.Width / 2, _gl.Height * 2 / 3);
        for (int i = 0; i < 300; i++) Raise(new MouseEventArgs(MouseButtons.None, 0, last.X - 150 + i, last.Y, 0));
        last = new Point(last.X + 149, last.Y);
        int during = PathRays - before;
        if (_mouseDirty) { _mouseDirty = false; HoverUpdate(_mousePend); }   // um quadro
        int frame = PathRays - before;
        var want = Ray(last);
        bool same = want is { } w && _drawHover is { } d && (w - d).Length < 0.05f;
        _drawPts = null; LeaveTool();
        return $"dftpathhover: raios durante 300 movimentos={during} (esperado 0); depois de um quadro={frame} (esperado 1); ponta no ponto certo={same}; ok={during == 0 && frame == 1 && same}";
    }
}
