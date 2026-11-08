using MyAceEditor.PerfectWorld;
using MyAceEditor.PerfectWorld.WorldEdit;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Luzes da cena (pílula Luzes da ferramenta Objetos do mundo; docs/superpowers/specs/2026-11-08-luzes-e-nomes-design.md):
/// lâmpadas de maps\&lt;mapa&gt;\scenelights.dat no mundo, escolher, mover pelas setas, cartão (posição, alcance, cor, força,
/// ambiente, só à noite), Pôr luz (uma por clique), Ctrl+C/V, Delete, Ctrl+Z por ação; grava no Salvar cenário.
/// No jogo elas iluminam personagens, NPCs e monstros perto (o chão e as construções já vêm com a luz assada).
/// </summary>
internal sealed partial class WorldView
{
    List<SceneLight>? _lights, _lightsSaved;   // rascunho e o que está no disco (null = ainda não aberto)
    readonly List<List<SceneLight>> _lUndo = new(), _lRedo = new();
    int _lightSel = -1;
    bool _lightLast, _lightPlacing;
    SceneLight? _lightClip, _lightPlaceSrc;
    int _lightAxis, _lightHover; Point _lightDown; System.Numerics.Vector3 _lightStart; System.Numerics.Vector3? _lightDrag;
    readonly Panel _lightPanel = new() { Width = 250, BackColor = Color.FromArgb(28, 32, 38), Visible = false, Padding = new Padding(6), Margin = new Padding(0, 4, 0, 4) };
    readonly Label _lightTitle = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9f) };
    readonly Panel _lightCard = new() { Dock = DockStyle.Top, Height = 250, BackColor = Color.FromArgb(28, 32, 38) };
    readonly NumericUpDown[] _lightXYZ = Enumerable.Range(0, 3).Select(_ => new NumericUpDown { Minimum = -100000, Maximum = 100000, DecimalPlaces = 1, Width = 66, Increment = 0.5m }).ToArray();
    readonly NumericUpDown _lightRange = new() { Minimum = 1, Maximum = 200, DecimalPlaces = 1, Width = 66, Increment = 1 };
    readonly NumericUpDown _lightPower = new() { Minimum = 0, Maximum = 10, DecimalPlaces = 2, Width = 66, Increment = 0.1m };
    readonly CheckBox _lightNight = new() { AutoSize = true, ForeColor = Color.White };
    Button? _lightColorBtn, _lightAmbBtn;
    bool _lightBinding;

    string LightsPath => Path.Combine(_element ?? "", "maps", _t.Name, "scenelights.dat");
    bool LightsDirty => _lights is not null && _lightsSaved is not null && !SceneLightsFile.Write(_lights).AsSpan().SequenceEqual(SceneLightsFile.Write(_lightsSaved));

    /// <summary>Lê as luzes do mapa (uma vez). Mapa sem arquivo = lista vazia (o Salvar cria). Falso = arquivo ilegível.</summary>
    bool LightsOpen()
    {
        if (_lights is not null) return true;
        try
        {
            var list = File.Exists(LightsPath) ? SceneLightsFile.Read(File.ReadAllBytes(LightsPath)) : new List<SceneLight>();
            _lights = list; _lightsSaved = [.. list];
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("luzes: abrir", ex);
            _status.Text = Loc.T("light.openFail", "Não deu para ler as luzes deste mapa (scenelights.dat); veja o log.");
            return false;
        }
    }

    /// <summary>Painel das luzes: botões Pôr luz (I) e Concluir; cartão da luz escolhida.</summary>
    void InitLightPanel()
    {
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, BackColor = _lightPanel.BackColor, Padding = new Padding(0, 4, 0, 8), WrapContents = false };
        var add = BigButton("", Loc.T("light.add", "Pôr luz"), "I", StartLightPlacing, 115);
        var done = BigButton("", Loc.T("grass.done", "Concluir"), "Esc", () => CloseLightPanel(), 115);
        _toolTip.SetToolTip(add, Loc.T("light.addTip", "A lâmpada segue o mouse; um clique põe (3 m acima do chão)."));
        modes.Controls.Add(add); modes.Controls.Add(done);
        // cartão: posição, alcance, força, cor, ambiente, só à noite, apagar
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 50, ColumnCount = 3, RowCount = 2, BackColor = _lightPanel.BackColor };
        string[] heads = { "X", Loc.T("world.objHeight", "altura"), "Z" };
        for (int i = 0; i < 3; i++)
        {
            grid.Controls.Add(new Label { Text = heads[i], ForeColor = Color.Silver, AutoSize = true }, i, 0);
            grid.Controls.Add(_lightXYZ[i], i, 1);
            NoWheel(_lightXYZ[i]);
            _lightXYZ[i].ValueChanged += (_, _) => { if (!_lightBinding) EditLight(l => l with { Pos = new System.Numerics.Vector3((float)_lightXYZ[0].Value, (float)_lightXYZ[1].Value, (float)_lightXYZ[2].Value) }); };
        }
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 2, RowCount = 2, BackColor = _lightPanel.BackColor, Padding = new Padding(0, 6, 0, 0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = Loc.T("light.range", "alcance (m)"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        row.Controls.Add(_lightRange, 1, 0);
        row.Controls.Add(new Label { Text = Loc.T("light.power", "força da luz"), ForeColor = Color.Silver, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        row.Controls.Add(_lightPower, 1, 1);
        NoWheel(_lightRange); NoWheel(_lightPower);
        _lightRange.ValueChanged += (_, _) => { if (!_lightBinding) EditLight(l => l with { Range = (float)_lightRange.Value }); };
        _lightPower.ValueChanged += (_, _) => { if (!_lightBinding) EditLight(l => l with { Diffuse = l.Diffuse with { W = (float)_lightPower.Value } }); };
        var colors = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = _lightPanel.BackColor, Padding = new Padding(0, 6, 0, 0) };
        _lightColorBtn = SmallButton("", Loc.T("light.color", "Cor da luz"), () => PickLightColor(ambient: false)); _lightColorBtn.Width = 232; _lightColorBtn.Margin = new Padding(0, 0, 0, 6);
        _lightAmbBtn = SmallButton("", Loc.T("light.ambient", "Cor da sombra (ambiente)"), () => PickLightColor(ambient: true)); _lightAmbBtn.Width = 232; _lightAmbBtn.Margin = new Padding(0, 0, 0, 6);
        _lightNight.Text = Loc.T("light.night", "Acende só à noite");
        _lightNight.CheckedChanged += (_, _) => { if (!_lightBinding) EditLight(l => l with { Flags = _lightNight.Checked ? l.Flags | SceneLight.NightOnly : l.Flags & ~SceneLight.NightOnly }); };
        var del = SmallButton("", Loc.T("world.objDelete", "Apagar"), DeleteLight); del.Width = 232; del.Margin = new Padding(0, 6, 0, 6);
        colors.Controls.Add(_lightColorBtn); colors.Controls.Add(_lightAmbBtn); colors.Controls.Add(_lightNight); colors.Controls.Add(del);
        foreach (var c in new Control[] { colors, row, grid }) _lightCard.Controls.Add(c);
        var note = new Label { Dock = DockStyle.Top, Height = 92, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f), Padding = new Padding(0, 6, 0, 0),
            Text = Loc.T("light.note2", "No jogo a luz pinta personagens, NPCs e monstros que passam perto (o brilho no chão aqui é só a prévia da cor e do alcance; com \"só à noite\" ela acende de noite). O chão e as construções já vêm com a luz pronta. Grava no Salvar cenário.") };
        foreach (var c in new Control[] { note, _lightCard, modes, _lightTitle }) _lightPanel.Controls.Add(c);
        FitLightPanel();
    }

    void FitLightPanel()
    {
        _lightCard.Height = _lightSel >= 0 ? _lightCard.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + 8 : 0;
        _lightCard.Visible = _lightSel >= 0;
        _lightPanel.Height = _lightPanel.Controls.Cast<Control>().Where(c => c.Visible || !_lightPanel.Visible).Sum(c => c.Visible ? c.Height : 0) + 16;
    }

    /// <summary>Pílula Luzes: abre o painel (fecha a grama e tira a seleção do objeto).</summary>
    void OpenLightPanel()
    {
        if (!LightsOpen()) return;
        StopPlanting(); StopBrush();
        _sceneSel = null; ShowObjPanel();
        _lightPanel.Visible = true; _lightSel = -1; ShowLightCard();
        _status.Text = string.Format(Loc.T("light.open", "Luzes da cena: {0}. Clique numa lâmpada para escolher; Pôr luz (I) insere."), _lights!.Count);
        _gl.Focus(); _gl.Invalidate();
    }

    bool CloseLightPanel()
    {
        if (!_lightPanel.Visible) return false;
        _lightPlacing = false; _lightSel = -1; _lightPanel.Visible = false;
        FitObjPanel(); _gl.Invalidate();
        return true;
    }

    void ShowLightCard()
    {
        if (_lights is null) return;
        _lightTitle.Text = _lightSel >= 0 && _lightSel < _lights.Count
            ? string.Format(Loc.T("light.sel", "Luz {0} de {1}"), _lightSel + 1, _lights.Count)
            : string.Format(Loc.T("light.count", "{0} luz(es) neste mapa"), _lights.Count);
        if (_lightSel >= 0 && _lightSel < _lights.Count)
        {
            var l = _lights[_lightSel];
            _lightBinding = true;
            _lightXYZ[0].Value = (decimal)Math.Clamp(l.Pos.X, -100000, 100000); _lightXYZ[1].Value = (decimal)Math.Clamp(l.Pos.Y, -100000, 100000); _lightXYZ[2].Value = (decimal)Math.Clamp(l.Pos.Z, -100000, 100000);
            _lightRange.Value = (decimal)Math.Clamp(l.Range, 1, 200); _lightPower.Value = (decimal)Math.Clamp(l.Diffuse.W, 0, 10);
            _lightNight.Checked = l.Night;
            if (_lightColorBtn is not null) _lightColorBtn.BackColor = ToColor(l.Diffuse);
            if (_lightAmbBtn is not null) _lightAmbBtn.BackColor = ToColor(l.Ambient);
            _lightBinding = false;
        }
        FitLightPanel(); LayoutTools();
    }

    static Color ToColor(System.Numerics.Vector4 c) => Color.FromArgb(Math.Clamp((int)(c.X * 255), 0, 255), Math.Clamp((int)(c.Y * 255), 0, 255), Math.Clamp((int)(c.Z * 255), 0, 255));

    void PickLightColor(bool ambient)
    {
        if (_lights is null || _lightSel < 0) return;
        var l = _lights[_lightSel];
        using var d = new ColorDialog { Color = ToColor(ambient ? l.Ambient : l.Diffuse), FullOpen = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var v = new System.Numerics.Vector3(d.Color.R / 255f, d.Color.G / 255f, d.Color.B / 255f);
        EditLight(x => ambient ? x with { Ambient = new(v, x.Ambient.W) } : x with { Diffuse = new(v, x.Diffuse.W) });
    }

    /// <summary>Uma ação nas luzes: guarda o antes (Ctrl+Z) e aplica.</summary>
    void LightStep(Action change)
    {
        if (_lights is null) return;
        _lUndo.Add([.. _lights]); if (_lUndo.Count > 100) _lUndo.RemoveAt(0);
        _lRedo.Clear();
        change();
        _lightLast = true;
        UpdateSideState(); ShowLightCard(); _gl.Invalidate();
    }

    void EditLight(Func<SceneLight, SceneLight> f)
    {
        if (_lights is null || _lightSel < 0 || _lightSel >= _lights.Count) return;
        int i = _lightSel;
        LightStep(() => _lights![i] = f(_lights[i]));
        _status.Text = Loc.T("light.edited", "Luz mudada (ainda não gravada; Ctrl+Z desfaz).");
    }

    void DeleteLight()
    {
        if (_lights is null || _lightSel < 0 || _lightSel >= _lights.Count) return;
        int i = _lightSel;
        LightStep(() => { _lights!.RemoveAt(i); _lightSel = -1; });
        _status.Text = Loc.T("light.deleted", "Luz apagada (Ctrl+Z traz de volta).");
    }

    bool LightUndo(bool redo)
    {
        if (_tool != Tool.Scene || _lights is null) return false;
        if (!_lightPanel.Visible && !_lightLast) return false;
        var from = redo ? _lRedo : _lUndo; var to = redo ? _lUndo : _lRedo;
        if (from.Count == 0) return false;
        to.Add([.. _lights]);
        _lights = from[^1]; from.RemoveAt(from.Count - 1);
        if (_lightSel >= _lights.Count) _lightSel = -1;
        if (!redo && _lUndo.Count == 0) _lightLast = false;
        _status.Text = redo ? Loc.T("light.redone", "Luz refeita.") : Loc.T("light.undone", "Luz desfeita (Ctrl+Y refaz).");
        UpdateSideState(); ShowLightCard(); _gl.Invalidate();
        return true;
    }

    static SceneLight NewLight(System.Numerics.Vector3 at) => new(at, 20, System.Numerics.Vector3.Zero, new(1f, 0.75f, 0.45f, 1.5f), new(0.2f, 0.15f, 0.1f, 0.3f), SceneLight.NightOnly);

    void StartLightPlacing()
    {
        if (!LightsOpen()) return;
        _lightPlacing = true; _lightPlaceSrc = null;
        _status.Text = Loc.T("light.placing", "Pôr luz: a lâmpada segue o mouse; clique onde ela vai ficar. Esc cancela.");
        _gl.Focus(); _gl.Invalidate();
    }

    System.Numerics.Vector3? LightGhostPos()
    {
        // ponto do mouse: o terreno ou uma construção, o que estiver mais perto (nas instâncias o chão é construção)
        System.Numerics.Vector3? hit = _hover is { } hv ? new System.Numerics.Vector3(hv.X, hv.Y, hv.Z) : null;
        try
        {
            var (ro, rd) = MouseRay(_gl.PointToClient(Cursor.Position));
            if (_buildings.RayPick(ro, rd) is { } bh)
            {
                var gp = ro + rd * bh.Dist; var bp = new System.Numerics.Vector3(gp.X, gp.Y, -gp.Z);
                if (hit is not { } th || (Gl(th.X, th.Y, th.Z) - ro).Length > bh.Dist) hit = bp;
            }
        }
        catch (InvalidOperationException) { }
        if (hit is not { } h) return null;
        float g = h.Y;
        float up = _lightPlaceSrc is { } s ? MathF.Max(0.5f, s.Pos.Y - (_t.HeightAt(s.Pos.X, s.Pos.Z) ?? s.Pos.Y)) : 3;
        return new System.Numerics.Vector3(h.X, g + up, h.Z);
    }

    /// <summary>Clique do mouse com o painel de luzes aberto. Falso = não era das luzes.</summary>
    bool LightMouseDown(MouseEventArgs e)
    {
        if (_tool != Tool.Scene || !_lightPanel.Visible || _lights is null) return false;
        if (e.Button == MouseButtons.Right) { if (_lightPlacing) { _lightPlacing = false; _gl.Invalidate(); return true; } return false; }
        if (e.Button != MouseButtons.Left) return false;
        if (_lightPlacing)
        {
            if (LightGhostPos() is not { } at) return true;
            var nl = (_lightPlaceSrc ?? NewLight(at)) with { Pos = at, Raw = null };
            LightStep(() => { _lights!.Add(nl); _lightSel = _lights.Count - 1; });
            _lightPlacing = false; _lightPlaceSrc = null;   // uma por clique
            _status.Text = Loc.T("light.added", "Luz posta (ainda não gravada; Ctrl+Z tira).");
            return true;
        }
        if (LightGizmoHit(e.Location) is > 0 and var ax && _lightSel >= 0)
        { _lightAxis = ax; _lightDown = e.Location; _lightStart = _lights[_lightSel].Pos; _lightDrag = null; return true; }
        // escolher a lâmpada mais perto na tela (até 14 px)
        int best = -1; float bd = 14;
        for (int i = 0; i < _lights.Count; i++)
        {
            var p = _lights[i].Pos;
            if ((Gl(p.X, p.Y, p.Z) - _cam).LengthSquared > 400 * 400) continue;
            if (Project(new Vector3(p.X, p.Y, p.Z)) is { } q && MathF.Sqrt((q.X - e.X) * (q.X - e.X) + (q.Y - e.Y) * (q.Y - e.Y)) is var dd && dd < bd) { bd = dd; best = i; }
        }
        if (best < 0) return false;   // clique no vazio: a câmera gira normalmente (a escolha sai no soltar, sem arrastar)
        _lightSel = best; _lightPicked = true; ShowLightCard(); _gl.Invalidate();
        return true;
    }

    bool LightMouseMove(Point p)
    {
        if (!_lightPanel.Visible) return false;
        if (_lightPlacing) _gl.Invalidate();
        if (_lightAxis > 0 && _lightSel >= 0)
        {
            var c = new Vector3(_lightStart.X, _lightStart.Y, _lightStart.Z); float L = GizmoSize(c);
            var dir = GizmoDirs[_lightAxis - 1];
            if (Project(c) is not { } a || Project(c + dir * L) is not { } b) return true;
            float sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
            if (len2 < 4) return true;
            float t = MathF.Round(((p.X - _lightDown.X) * sx + (p.Y - _lightDown.Y) * sy) / len2 * L * 10) / 10;
            var g = c + dir * t;
            _lightDrag = new System.Numerics.Vector3(g.X, g.Y, g.Z);
            _status.Text = string.Format(Loc.T("gizmo.moving", "Movendo: X {0:0.0}   altura {1:0.0}   Z {2:0.0}"), g.X, g.Y, g.Z);
            _gl.Invalidate();
            return true;
        }
        if (_lightSel >= 0 && LightGizmoHit(p) is var h && h != _lightHover) { _lightHover = h; _gl.Invalidate(); }
        return false;
    }

    bool _lightPicked;   // o apertar escolheu uma lâmpada: o soltar não pode tirar a escolha (usuário 08/11: não dava para clicar de novo)

    bool LightMouseUp()
    {
        if (_lightPicked) { _lightPicked = false; return true; }
        if (_lightAxis == 0) return false;
        if (_lightDrag is { } d && _lightSel >= 0) EditLight(l => l with { Pos = d });
        _lightAxis = 0; _lightDrag = null; _gl.Invalidate();
        return true;
    }

    int LightGizmoHit(Point p)
    {
        if (_lights is null || _lightSel < 0 || _lightSel >= _lights.Count) return 0;
        var lp = _lights[_lightSel].Pos; var c = new Vector3(lp.X, lp.Y, lp.Z); float L = GizmoSize(c);
        if (Project(c) is not { } a) return 0;
        for (int k = 0; k < 3; k++)
            if (Project(c + GizmoDirs[k] * L * 1.18f) is { } b && SegDist(p, a, b, 0.2f) < 9) return k + 1;
        return 0;
    }

    /// <summary>Teclas com o painel de luzes: I põe luz, Delete apaga. Falso = não era.</summary>
    bool LightKey(Keys k)
    {
        if (_tool != Tool.Scene || !_lightPanel.Visible) return false;
        if (k == Keys.I) { StartLightPlacing(); return true; }
        if (k == Keys.Delete && _lightSel >= 0) { DeleteLight(); return true; }
        return false;
    }

    bool LightCopy()
    {
        if (!_lightPanel.Visible || _lights is null || _lightSel < 0) return false;
        _lightClip = _lights[_lightSel];
        _status.Text = Loc.T("light.copied", "Luz copiada — Ctrl+V põe uma igual onde clicar.");
        return true;
    }

    bool LightPaste()
    {
        if (!_lightPanel.Visible || _lightClip is null) return false;
        _lightPlacing = true; _lightPlaceSrc = _lightClip;
        _status.Text = Loc.T("light.placing", "Pôr luz: a lâmpada segue o mouse; clique onde ela vai ficar. Esc cancela.");
        _gl.Invalidate();
        return true;
    }

    /// <summary>Esc com o painel de luzes: cancela o pôr, depois tira a escolha, depois fecha. Falso = não era.</summary>
    bool LightEscape()
    {
        if (!_lightPanel.Visible) return false;
        if (_lightPlacing) { _lightPlacing = false; _gl.Invalidate(); return true; }
        if (_lightSel >= 0) { _lightSel = -1; ShowLightCard(); _gl.Invalidate(); return true; }
        return CloseLightPanel();
    }

    /// <summary>Lâmpadas (cor da luz) até 400 m; a escolhida com o alcance no chão e as setas; prévia do Pôr luz.</summary>
    void DrawLights()
    {
        if (_tool != Tool.Scene || !_lightPanel.Visible || _lights is null) return;
        GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.PointSmooth);
        void Lamp(System.Numerics.Vector3 p, Color c, float size)
        {
            GL.PointSize(size + 4); GL.Color3(Color.Black); GL.Begin(PrimitiveType.Points); GL.Vertex3(Gl(p.X, p.Y, p.Z)); GL.End();
            GL.PointSize(size); GL.Color3(c); GL.Begin(PrimitiveType.Points); GL.Vertex3(Gl(p.X, p.Y, p.Z)); GL.End();
            // risco até o chão só quando o chão está perto embaixo (instância sem terreno: sem risco)
            if (_t.HeightAt(p.X, p.Z) is { } g && p.Y - g is > 0 and < 40)
            {
                GL.LineWidth(1); GL.Color4(c.R / 255f, c.G / 255f, c.B / 255f, 0.6f);
                GL.Begin(PrimitiveType.Lines); GL.Vertex3(Gl(p.X, p.Y, p.Z)); GL.Vertex3(Gl(p.X, g, p.Z)); GL.End();
            }
        }
        // prévia do efeito: brilho no chão com a cor e o alcance da luz (no jogo a luz da cena só ilumina personagens, NPCs
        // e monstros; o chão e as construções usam a luz gravada no mapa)
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(false);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        GL.Enable(EnableCap.PolygonOffsetFill); GL.PolygonOffset(-2, -2);
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i]; var p = i == _lightSel && _lightDrag is { } dd ? dd : l.Pos;
            if ((Gl(p.X, p.Y, p.Z) - _cam).LengthSquared > 400 * 400 || l.Range <= 0) continue;
            var c = ToColor(l.Diffuse);
            float r = MathF.Min(l.Range, 120);
            const int Seg = 32, Ring = 6;
            for (int k = 0; k < Ring; k++)
            {
                float r0 = r * k / Ring, r1 = r * (k + 1) / Ring;
                float a0 = 0.45f * (1 - (float)k / Ring), a1 = 0.45f * (1 - (float)(k + 1) / Ring);
                GL.Begin(PrimitiveType.QuadStrip);
                for (int s = 0; s <= Seg; s++)
                {
                    float t = s / (float)Seg * MathF.Tau, sx = MathF.Sin(t), cz = MathF.Cos(t);
                    float x1 = p.X + sx * r1, z1 = p.Z + cz * r1, x0 = p.X + sx * r0, z0 = p.Z + cz * r0;
                    GL.Color4(c.R / 255f, c.G / 255f, c.B / 255f, a1); GL.Vertex3(Gl(x1, (_t.HeightAt(x1, z1) ?? p.Y) + 0.1f, z1));
                    GL.Color4(c.R / 255f, c.G / 255f, c.B / 255f, a0); GL.Vertex3(Gl(x0, (_t.HeightAt(x0, z0) ?? p.Y) + 0.1f, z0));
                }
                GL.End();
            }
        }
        GL.Disable(EnableCap.PolygonOffsetFill); GL.DepthMask(true); GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i]; var p = i == _lightSel && _lightDrag is { } d ? d : l.Pos;
            if ((Gl(p.X, p.Y, p.Z) - _cam).LengthSquared > 400 * 400) continue;
            Lamp(p, i == _lightSel ? Color.White : ToColor(l.Diffuse), i == _lightSel ? 14 : 10);
            if (i != _lightSel) continue;
            // alcance: círculo no chão acompanhando o relevo
            GL.LineWidth(2); GL.Color3(ToColor(l.Diffuse));
            GL.Begin(PrimitiveType.LineLoop);
            for (int k = 0; k < 64; k++)
            {
                float t = k / 64f * MathF.Tau, x = p.X + MathF.Sin(t) * l.Range, z = p.Z + MathF.Cos(t) * l.Range;
                GL.Vertex3(Gl(x, (_t.HeightAt(x, z) ?? p.Y) + 0.2f, z));
            }
            GL.End();
            // setas de mover
            var c = new Vector3(p.X, p.Y, p.Z); float L = GizmoSize(c); int hot = _lightAxis > 0 ? _lightAxis : _lightHover;
            Color[] cols = { Color.Red, Color.LimeGreen, Color.DodgerBlue };
            GL.LineWidth(3);
            for (int k = 0; k < 3; k++)
            {
                var tip = c + GizmoDirs[k] * L;
                GL.Color3(hot == k + 1 ? Color.White : cols[k]);
                GL.Begin(PrimitiveType.Lines); GL.Vertex3(Gl(c.X, c.Y, c.Z)); GL.Vertex3(Gl(tip.X, tip.Y, tip.Z)); GL.End();
            }
        }
        if (_lightPlacing && LightGhostPos() is { } gp) Lamp(gp, Color.Yellow, 14);
        GL.Disable(EnableCap.PointSmooth); GL.LineWidth(1); GL.PointSize(1);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Depois de salvar, abandonar ou restaurar backup: as luzes saem do rascunho e são relidas.</summary>
    void ResetLightEdit()
    {
        CloseLightPanel();
        _lights = null; _lightsSaved = null; _lUndo.Clear(); _lRedo.Clear(); _lightLast = false;
    }

    /// <summary>Para a captura ("dftlight"): abre as luzes, põe uma, move, muda o alcance, desfaz, copia/cola e monta o plano.</summary>
    internal string DraftLightTest()
    {
        string load = DraftLoadTest();
        OpenLightPanel();
        if (_lights is null) return load + " | dftlight: luzes não abriram";
        int n0 = _lights.Count;
        var at = new System.Numerics.Vector3(_cam.X + Forward.X * 15, 0, -(_cam.Z + Forward.Z * 15));
        at.Y = (_t.HeightAt(at.X, at.Z) ?? 0) + 3;
        LightStep(() => { _lights!.Add(NewLight(at)); _lightSel = _lights.Count - 1; });
        bool added = _lights.Count == n0 + 1;
        EditLight(l => l with { Range = 35 });
        bool undo = LightUndo(false) && _lights[_lightSel].Range == 20;
        bool redo = LightUndo(true) && _lights[_lightSel].Range == 35;
        LightCopy(); LightPaste(); _hover = new Vector3(at.X + 6, at.Y, at.Z);
        LightMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        bool pasted = _lights.Count == n0 + 2 && !_lightPlacing;
        string sum = "";
        try
        {
            var plan = new SavePlan(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());
            AddLightsToPlan(plan);
            sum = string.Join(" / ", plan.Summary) + " " + string.Join(", ", plan.Files.Keys.Select(Path.GetFileName)) + $" relidas={(plan.Files.Values.FirstOrDefault() is { } b ? SceneLightsFile.Read(b).Count : -1)}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { sum = ex.Message; }
        using (var lp = ToolsShot()) lp.Save(Path.Combine(_shotDir ?? Path.GetTempPath(), "dftlight-painel.png"));
        _hover = new Vector3(at.X, at.Y, at.Z);
        return load + $" | dftlight: {n0} luzes; pôs={added} ctrlz={undo} ctrly={redo} colou-uma={pasted} sujo={LightsDirty} plano=\"{sum}\"";
    }

    /// <summary>Luzes no plano do Salvar dos objetos (tudo ou nada junto).</summary>
    void AddLightsToPlan(SavePlan plan)
    {
        if (_lights is null || !LightsDirty) return;
        var b = SceneLightsFile.Write(_lights);
        _ = SceneLightsFile.Read(b);   // relê
        plan.Files[LightsPath] = b;
        plan.Summary.Add(string.Format(Loc.T("light.summary", "Luzes: {0} (antes {1})"), _lights.Count, _lightsSaved?.Count ?? 0));
    }
}
