using System.Diagnostics;
using MyAceEditor.PerfectWorld;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Mundo em 3D só com a câmera (como espectador): terreno (WorldTerrain), construções (WorldBuildings), água, modelos
/// parados dos pontos do npcgen (WorldModels) — tudo carregado em segundo plano (WorldLoader) para não travar.
/// Esquerdo em cima de um ponto arrasta; esquerdo no vazio ou botão do meio gira; direito abre o menu (novo, clonar,
/// mover, excluir, editar no software). Teclas: W/A/S/D, Espaço/Z (voo), Shift, roda (aproxima), M (mapa),
/// Ctrl+Z/Ctrl+Y, Ctrl+C/Ctrl+V, Delete, T (esconde o terreno), H (legenda e atalhos).
/// </summary>
internal sealed class WorldView : Form
{
    /// <summary>LabelColor = cor do nome (legenda); OnGround = "no chão" (desenha na altura do terreno quando perto dela).</summary>
    public sealed record Marker(float X, float Y, float Z, Color Color, string Name, object Tag, string? Model = null, float Yaw = 0,
        Color LabelColor = default, bool OnGround = false, int Id = 0);

    const System.ComponentModel.DesignerSerializationVisibility Hidden = System.ComponentModel.DesignerSerializationVisibility.Hidden;
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object, Vector3>? CloneHere { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object>? EditPoint { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<Vector3, int>? CreateNewHere { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<object>? DeletePoint { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<bool>? UndoAction { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Func<bool>? RedoAction { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public IReadOnlyList<string> Maps { get; set; } = Array.Empty<string>();
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public Action<string>? SwitchMap { get; set; }
    /// <summary>Pasta watermap do servidor: N.wmap = i32 versão, 2f lado, i32 n, n × (centro x, z relativos ao centro do pedaço, meia-largura x, z, altura).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(Hidden)] public string? WaterDir { get; set; }

    const float Speed = 40;           // m/s andando; Shift = ×4

    readonly TerrainFile _t;
    readonly Func<IReadOnlyList<Marker>> _markerSrc;
    readonly Action<Vector3, int> _newHere;
    readonly Action<object, Vector3>? _moveHere;
    readonly Func<object?> _selected;
    readonly Action<object> _select;
    readonly GLControl _gl;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0), BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.Gainsboro };
    readonly ToolStrip _bar = new() { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, BackColor = Color.FromArgb(28, 32, 38), ForeColor = Color.White, Padding = new Padding(4, 3, 4, 3) };
    readonly ToolStripTextBox _find = new() { Width = 220, BackColor = Color.Black, ForeColor = Color.White };
    readonly ToolStripComboBox _mapBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Color.Black, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly WorldLoader _load = new();
    readonly WorldTerrain _terrain;
    readonly WorldModels _models;
    readonly WorldBuildings _buildings;
    readonly HashSet<Keys> _keys = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 10 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Dictionary<string, (int Tex, int W, int H)> _labels = new();
    List<Marker> _marks = new(), _nearMarks = new();
    Vector3 _nearAt = new(float.MaxValue);
    bool _marksDirty; double _marksAt;
    Vector3 _cam;
    float _yaw = -90, _pitch = -25;
    Point _drag, _down; bool _dragging; double _last;
    Vector3? _hover;
    bool _busy;
    int _findAt = -1, _piece = -1;
    Bitmap? _worldMap; float _mapMPerPx = 2;
    object? _movingTag; Vector3? _movePos;   // arrastando um ponto com o botão esquerdo
    object? _clip;                            // Ctrl+C
    int _newLabels;
    bool _legendOpen = true; int _legendTex, _legendW, _legendH; bool _legendState;
    List<(float X0, float Z0, float X1, float Z1, float Y)>? _water;

    public WorldView(TerrainFile t, Vector3 start, Func<IReadOnlyList<Marker>> markers, Func<object?> selected, Action<object> select,
        Action<Vector3, int> newHere, Action<object, Vector3>? moveHere)
    {
        _t = t; _markerSrc = markers; _selected = selected; _select = select; _newHere = newHere; _moveHere = moveHere;
        Text = string.Format(Loc.T("world.title", "Mundo 3D — {0}"), t.Name);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(860, wa.Height - 40)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        _gl = new GLControl(new GLControlSettings { Profile = OpenTK.Windowing.Common.ContextProfile.Compatability, APIVersion = new Version(3, 3) }) { Dock = DockStyle.Fill };
        ToolStripButton Btn(string key, string pt, Action a) => new(Loc.T(key, pt), null, (_, _) => a()) { ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.goto", "Ir para:")) { ForeColor = Color.Silver });
        _bar.Items.Add(_find);
        _bar.Items.Add(Btn("world.next", "Próximo ▶", FindNext));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(Btn("world.map", "🗺 Mapa (M)", ShowMap));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.mapPick", "Mapa:")) { ForeColor = Color.Silver });
        _bar.Items.Add(_mapBox);
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(Btn("world.help2", "❔ Atalhos (H)", () => { _legendOpen = !_legendOpen; _gl.Invalidate(); }));
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        _find.TextChanged += (_, _) => _findAt = -1;
        Controls.Add(_gl); Controls.Add(_bar); Controls.Add(_status);
        var element = Path.GetDirectoryName(Path.GetDirectoryName(t.Dir));
        _terrain = new WorldTerrain(t, _load, element, start);
        _models = new WorldModels(element, _load);
        _buildings = new WorldBuildings(element, t.Name, _load);
        RefreshMarkers();
        LookAt(start);
        Shown += (_, _) =>
        {
            Activate(); _gl.Focus();   // já abre pronta para W/A/S/D
            _mapBox.Items.AddRange(Maps.Select(m => (object)new MapItem(m)).ToArray());
            _mapBox.SelectedItem = _mapBox.Items.Cast<MapItem>().FirstOrDefault(i => i.Key == Tag as string);
            _mapBox.DropDownWidth = 300;
            _mapBox.SelectedIndexChanged += (_, _) => { if (_mapBox.SelectedItem is MapItem m && m.Key != Tag as string) BeginInvoke(() => SwitchMap?.Invoke(m.Key)); };
        };
        _gl.Paint += (_, _) => Render();
        _gl.Resize += (_, _) => _gl.Invalidate();
        _gl.MouseDown += (_, e) =>
        {
            _gl.Focus(); _down = _drag = e.Location;
            if (e.Button == MouseButtons.Left && LegendRect().Contains(e.Location)) { _legendOpen = !_legendOpen; _gl.Invalidate(); return; }
            // esquerdo em cima de um ponto (na tela): arrasta o ponto; no vazio ou botão do meio: gira a câmera
            if (e.Button == MouseButtons.Left && PickScreen(e.Location) is { } mk) { _movingTag = mk.Tag; _movePos = null; return; }
            if (e.Button is MouseButtons.Left or MouseButtons.Middle) _dragging = true;
        };
        _gl.MouseUp += (_, e) =>
        {
            _dragging = false;
            if (_movingTag is { } tag)
            {
                bool moved = _movePos is not null && (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4);
                if (moved && _moveHere is not null) { _moveHere(tag, _movePos!.Value); RefreshMarkers(); }
                else { _select(tag); _status.Text = _marks.FirstOrDefault(m => m.Tag == tag)?.Name ?? ""; }
                _movingTag = null; _movePos = null; _gl.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right) RightClick(e.Location);
        };
        _gl.MouseMove += (_, e) =>
        {
            if (_dragging) { _yaw += (e.X - _drag.X) * 0.25f; _pitch = Math.Clamp(_pitch - (e.Y - _drag.Y) * 0.25f, -89, 89); _drag = e.Location; _gl.Invalidate(); return; }
            if (_movingTag is not null)
            {
                if (Math.Abs(e.X - _down.X) > 4 || Math.Abs(e.Y - _down.Y) > 4) { if (Ray(e.Location) is { } mp) _movePos = mp; }
                _gl.Invalidate(); return;
            }
            _hover = Ray(e.Location);
            var near = PickScreen(e.Location);
            _status.Text = _hover is { } h ? string.Format(Loc.T("world.coord", "X {0:0.0}   altura {1:0.0}   Z {2:0.0}"), h.X, h.Y, h.Z)
                + (near is null ? "" : "   ·   " + near.Name) : "";
        };
        // roda: aproxima/afasta a câmera para onde ela olha (Shift = 4×)
        _gl.MouseWheel += (_, e) => { _cam += Forward * (e.Delta / 120f) * 12f * ((ModifierKeys & Keys.Shift) != 0 ? 4 : 1); ClampCamera(); _gl.Invalidate(); };
        KeyDown += (_, e) => OnKey(e);
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        Deactivate += (_, _) => _keys.Clear();
        Activated += (_, _) => MarkDirty();   // mudanças feitas no editor aparecem ao voltar
        _tick.Tick += (_, _) => Step();
        _tick.Start();
        FormClosed += (_, _) =>
        {
            _tick.Dispose(); _load.Dispose(); _gl.MakeCurrent();
            _terrain.Dispose(); _models.Dispose(); _buildings.Dispose(); _worldMap?.Dispose();
            foreach (var l in _labels.Values) GL.DeleteTexture(l.Tex);
            if (_legendTex != 0) GL.DeleteTexture(_legendTex);
        };
    }

    void OnKey(KeyEventArgs e)
    {
        if (_find.Focused) return;
        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Z or Keys.Y:
                    bool ok = (e.KeyCode == Keys.Z ? UndoAction : RedoAction)?.Invoke() ?? false;
                    _status.Text = ok ? "" : e.KeyCode == Keys.Z ? Loc.T("status.nothingUndo", "Nada para desfazer") : Loc.T("status.nothingRedo", "Nada para refazer");
                    RefreshMarkers(); break;
                case Keys.C:
                    _clip = _selected();
                    _status.Text = _clip is null ? "" : Loc.T("world.copied", "Ponto copiado — Ctrl+V cola onde está o mouse");
                    break;
                case Keys.V:
                    if (_clip is not null && CloneHere is not null && (_hover ?? Ray(_gl.PointToClient(Cursor.Position))) is { } at) { CloneHere(_clip, at); RefreshMarkers(); }
                    break;
            }
            e.Handled = true; return;
        }
        switch (e.KeyCode)
        {
            case Keys.M: ShowMap(); return;
            case Keys.H or Keys.F1: _legendOpen = !_legendOpen; _gl.Invalidate(); return;
            case Keys.T: _terrain.Visible = !_terrain.Visible; _gl.Invalidate(); return;   // ver por dentro de cavernas
            case Keys.Delete:
                if (_selected() is { } s && DeletePoint is not null && Confirm()) { DeletePoint(s); RefreshMarkers(); }
                return;
            case Keys.Space: e.SuppressKeyPress = true; break;
        }
        _keys.Add(e.KeyCode);
    }

    bool Confirm() => MessageBox.Show(this, Loc.T("world.deleteAsk", "Excluir este ponto? (Ctrl+Z desfaz)"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Relê os pontos (depois de criar, mover, excluir ou trocar o arquivo).</summary>
    public void RefreshMarkers() { _marks = _markerSrc().ToList(); _nearAt = new(float.MaxValue); _marksDirty = false; _marksAt = _clock.Elapsed.TotalSeconds; _gl?.Invalidate(); }

    /// <summary>Algo mudou fora: relê os pontos no próximo quadro (no máximo ~3 vezes por segundo).</summary>
    public void MarkDirty() { _marksDirty = true; _gl?.Invalidate(); }

    /// <summary>Leva a câmera até o ponto (lista da aba NPCGen ou "Ir para").</summary>
    public void GoTo(object tag)
    {
        if (_marks.FirstOrDefault(m => m.Tag == tag) is { } m) { LookAt(PosOf(m)); _gl.Invalidate(); }
    }

    /// <summary>Câmera acima do ponto, olhando para baixo (abrir sem nada selecionado). high: vista alta do mapa inteiro.</summary>
    public void Overview(Vector3 p, bool high)
    {
        float ground = _t.HeightAt(p.X, p.Z) ?? p.Y;
        if (high) { _cam = Gl(p.X, ground + 400, p.Z - 400); _pitch = -40; }
        else { _cam = Gl(p.X, Math.Max(p.Y, ground) + 25, p.Z - 35); _pitch = -30; }
        _yaw = -90; ClampCamera(); _gl.Invalidate();
    }

    // câmera 14 m ao sul e 7 m acima, olhando para o ponto
    void LookAt(Vector3 p) { _cam = Gl(p.X, p.Y + 7, p.Z - 14); _yaw = -90; _pitch = -22; ClampCamera(); }

    // dentro do mapa e acima do chão
    void ClampCamera()
    {
        _cam.X = Math.Clamp(_cam.X, -_t.Width / 2, _t.Width / 2);
        _cam.Z = Math.Clamp(_cam.Z, -_t.Height / 2, _t.Height / 2);
        if (_terrain.Visible && _t.HeightAt(_cam.X, -_cam.Z) is { } ground && _cam.Y < ground + 2) _cam.Y = ground + 2;
    }

    Vector3 Forward => new(MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)),
        MathF.Sin(MathHelper.DegreesToRadians(_pitch)), MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)));

    // movimento pelo tempo real do quadro; redesenha enquanto há carga em andamento
    void Step()
    {
        double now = _clock.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - _last); _last = now;
        bool moving = _keys.Count > 0;
        if (moving)
        {
            float s = Speed * dt * (_keys.Contains(Keys.ShiftKey) ? 4 : 1);
            var f = Forward; var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
            if (_keys.Contains(Keys.W)) _cam += f * s;
            if (_keys.Contains(Keys.S)) _cam -= f * s;
            if (_keys.Contains(Keys.D)) _cam += right * s;
            if (_keys.Contains(Keys.A)) _cam -= right * s;
            if (_keys.Contains(Keys.E) || _keys.Contains(Keys.Space)) _cam.Y += s;
            if (_keys.Contains(Keys.Q) || (_keys.Contains(Keys.Z) && !_keys.Contains(Keys.ControlKey))) _cam.Y -= s;
            ClampCamera();
        }
        if (moving || _busy || _marksDirty) _gl.Invalidate();
    }

    // o mundo do PW usa Z para o norte; aqui -Z do OpenGL vira +Z do jogo (espelha Z)
    static Vector3 Gl(float x, float y, float z) => new(x, y, -z);

    Matrix4 View => Matrix4.LookAt(_cam, _cam + Forward, Vector3.UnitY);
    Matrix4 Proj => Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), Math.Max(1, _gl.Width) / (float)Math.Max(1, _gl.Height), 1, 9000);

    void Render()
    {
        _gl.MakeCurrent();
        bool loading = _load.Pump(6);   // resultados do segundo plano: no máximo 6 ms por quadro
        GL.Viewport(0, 0, _gl.Width, _gl.Height);
        GL.ClearColor(0.62f, 0.76f, 0.9f, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); var p = Proj; GL.LoadMatrix(ref p);
        GL.MatrixMode(MatrixMode.Modelview); var v = View; GL.LoadMatrix(ref v);
        GL.Enable(EnableCap.Fog);
        GL.Fog(FogParameter.FogMode, (int)FogMode.Linear);
        GL.Fog(FogParameter.FogStart, 1500f); GL.Fog(FogParameter.FogEnd, 8000f);
        GL.Fog(FogParameter.FogColor, new[] { 0.62f, 0.76f, 0.9f, 1f });

        bool terrainBusy = _terrain.Draw(_cam, Forward);

        // construções dos pedaços de 1 km em volta da câmera
        int piece = _t.PieceAt(_cam.X, -_cam.Z);
        if (piece != _piece)
        {
            _piece = piece;
            var pieces = new List<int>();
            if (piece > 0)
            {
                int pr = (piece - 1) / _t.Cols, pc = (piece - 1) % _t.Cols;
                for (int r = pr - 1; r <= pr + 1; r++)
                    for (int c = pc - 1; c <= pc + 1; c++)
                        if (r >= 0 && c >= 0 && r < _t.Rows && c < _t.Cols) pieces.Add(r * _t.Cols + c + 1);
            }
            _buildings.Update(pieces);
        }
        _buildings.Draw(_cam, Forward);
        DrawWater();

        // pontos perto da câmera (refeitos a cada 100 m andados ou quando algo mudou)
        if (_marksDirty && _clock.Elapsed.TotalSeconds - _marksAt > 0.3) RefreshMarkers();
        if ((_cam - _nearAt).LengthSquared > 100 * 100) { _nearAt = _cam; _nearMarks = _marks.Where(m => Math.Abs(m.X - _cam.X) < 1500 && Math.Abs(-m.Z - _cam.Z) < 1500).ToList(); }

        // modelos parados até 300 m; quem não tem modelo vira um ponto pequeno
        var sel = _selected();
        GL.Enable(EnableCap.Lighting); GL.Enable(EnableCap.Light0); GL.Enable(EnableCap.Normalize); GL.Enable(EnableCap.ColorMaterial);
        GL.Light(LightName.Light0, LightParameter.Position, new Vector4(-0.4f, 1f, 0.3f, 0));
        GL.Light(LightName.Light0, LightParameter.Diffuse, new Color4(0.8f, 0.8f, 0.75f, 1f));
        GL.Light(LightName.Light0, LightParameter.Ambient, new Color4(0.5f, 0.5f, 0.55f, 1f));
        GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.3f);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.NormalArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.Color3(1f, 1f, 1f);
        var noModel = new List<Marker>();
        foreach (var m in _nearMarks)
        {
            if (Dist2(m) > 300 * 300) continue;
            if (m.Model is null || !_models.Draw(m.Model, PosOf(m), m.Yaw)) noModel.Add(m);
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.NormalArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.ColorMaterial); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.AlphaTest);
        GL.PointSize(7);
        GL.Begin(PrimitiveType.Points);
        foreach (var m in noModel) { var mp = PosOf(m); Col(m.Tag == sel ? Color.Yellow : m.Color); GL.Vertex3(Gl(mp.X, mp.Y + 1.2f, mp.Z)); }
        GL.End();

        // nomes e coordenadas sobre os pontos até 70 m (os 40 mais perto)
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        GL.Enable(EnableCap.Texture2D); GL.Color3(1f, 1f, 1f);
        _newLabels = 0;
        foreach (var m in _nearMarks.Where(m => Dist2(m) < 70 * 70 || m.Tag == sel && Dist2(m) < 400 * 400).OrderBy(Dist2).Take(40))
        {
            var lp = PosOf(m);
            float top = m.Model is not null && _models.Top(m.Model) is { } tp ? tp + 1.2f : 3f;
            DrawLabel($"{m.Name}\n{lp.X:0}, {lp.Y:0}, {lp.Z:0}", new Vector3(lp.X, lp.Y + top, lp.Z), m.Tag == sel ? Color.Yellow : m.LabelColor.IsEmpty ? Color.White : m.LabelColor);
        }
        DrawLegend();
        GL.Disable(EnableCap.Texture2D); GL.DepthMask(true); GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.Fog);
        _gl.SwapBuffers();
        _busy = loading || terrainBusy;
    }

    float Dist2(Marker m) { float dx = m.X - _cam.X, dz = m.Z + _cam.Z, dy = m.Y - _cam.Y; return dx * dx + dy * dy + dz * dz; }
    static void Col(Color c) => GL.Color3(c.R / 255f, c.G / 255f, c.B / 255f);

    // "no chão": o servidor põe na altura do terreno; só segue o terreno se estiver perto dele (em cima de construção
    // ou dentro de caverna, fica na altura gravada)
    Vector3 PosOf(Marker m)
    {
        if (m.Tag == _movingTag && _movePos is { } mp) return mp;
        float y = m.Y;
        if (m.OnGround && _t.HeightAt(m.X, m.Z) is { } h && Math.Abs(h - m.Y) < 3) y = h;
        return new Vector3(m.X, y, m.Z);
    }

    void DrawWater()
    {
        if (Water() is not { Count: > 0 } water) return;
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        GL.Color4(0.18f, 0.42f, 0.55f, 0.62f);
        GL.Begin(PrimitiveType.Quads);
        foreach (var (x0, z0, x1, z1, y) in water) { GL.Vertex3(x0, y, -z0); GL.Vertex3(x1, y, -z0); GL.Vertex3(x1, y, -z1); GL.Vertex3(x0, y, -z1); }
        GL.End();
        GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Color4(1f, 1f, 1f, 1f);
    }

    List<(float, float, float, float, float)> Water()
    {
        if (_water is not null) return _water;
        _water = new();
        if (WaterDir is null || !Directory.Exists(WaterDir)) return _water;
        float pieceM = _t.CellsPerPiece * _t.Cell;
        for (int r = 0; r < _t.Rows; r++)
            for (int c = 0; c < _t.Cols; c++)
            {
                var f = Path.Combine(WaterDir, $"{r * _t.Cols + c + 1}.wmap");
                if (!File.Exists(f)) continue;
                var d = File.ReadAllBytes(f);
                if (d.Length < 16) continue;
                int n = BitConverter.ToInt32(d, 12);
                float cx = -_t.Width / 2 + (c + 0.5f) * pieceM, cz = _t.Height / 2 - (r + 0.5f) * pieceM;
                for (int i = 0; i < n && 16 + i * 20 + 20 <= d.Length; i++)
                {
                    int o = 16 + i * 20;
                    float x = cx + BitConverter.ToSingle(d, o), z = cz + BitConverter.ToSingle(d, o + 4), hx = BitConverter.ToSingle(d, o + 8), hz = BitConverter.ToSingle(d, o + 12), y = BitConverter.ToSingle(d, o + 16);
                    _water.Add((x - hx, z - hz, x + hx, z + hz, y));
                }
            }
        return _water;
    }

    // texto do nome numa textura (em cache) desenhada de frente para a câmera, do mesmo tamanho na tela
    void DrawLabel(string text, Vector3 at, Color color)
    {
        var key = color.ToArgb() + text;
        if (!_labels.TryGetValue(key, out var l))
        {
            // poucas texturas de nome novas por quadro (muitos NPCs juntos não travam)
            if (_newLabels++ >= 4) { _busy = true; return; }
            if (_labels.Count > 3000) { foreach (var x in _labels.Values) GL.DeleteTexture(x.Tex); _labels.Clear(); }
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            var size = TextRenderer.MeasureText(text, font);
            using var bmp = new Bitmap(size.Width + 8, size.Height + 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var sf = new StringFormat { Alignment = StringAlignment.Center };
                var r = new RectangleF(0, 2, bmp.Width, bmp.Height);
                g.DrawString(text, font, Brushes.Black, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, r, sf);
            }
            l = _labels[key] = (UploadBitmap(bmp), bmp.Width, bmp.Height);
        }
        var right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY)); var up = Vector3.Cross(right, Forward);
        var c = Gl(at.X, at.Y, at.Z);
        float k = (c - _cam).Length * 0.0011f, hw = l.W * k / 2, hh = l.H * k;
        GL.BindTexture(TextureTarget.Texture2D, l.Tex);
        GL.Begin(PrimitiveType.Quads);
        GL.TexCoord2(0f, 1f); GL.Vertex3(c - right * hw);
        GL.TexCoord2(1f, 1f); GL.Vertex3(c + right * hw);
        GL.TexCoord2(1f, 0f); GL.Vertex3(c + right * hw + up * hh);
        GL.TexCoord2(0f, 0f); GL.Vertex3(c - right * hw + up * hh);
        GL.End();
    }

    static int UploadBitmap(Bitmap bmp)
    {
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bd.Scan0);
        bmp.UnlockBits(bd);
        return tex;
    }

    // ---------- legenda e atalhos (canto superior esquerdo; H ou clique abre/fecha) ----------
    Rectangle LegendRect() => new(10, 10, _legendW, _legendH);

    void DrawLegend()
    {
        if (_legendTex == 0 || _legendState != _legendOpen)
        {
            if (_legendTex != 0) GL.DeleteTexture(_legendTex);
            var rows = new List<(Color C, string T)>();
            if (_legendOpen)
            {
                rows.Add((Color.White, Loc.T("legend.title", "▾ Legenda (clique ou H para fechar)")));
                rows.Add((Color.White, Loc.T("legend.normal", "Branco — monstro ou NPC normal (nasce sempre)")));
                rows.Add((Color.LimeGreen, Loc.T("legend.event", "Verde — evento com data/hora (gatilho agendado)")));
                rows.Add((Color.Violet, Loc.T("legend.trigger", "Lilás — ativado por gatilho (fase de DG, GM)")));
                rows.Add((Color.OrangeRed, Loc.T("legend.guard", "Vermelho — guarda (NPC em ponto de monstro)")));
                rows.Add((Color.Cyan, Loc.T("legend.gather", "Ciano — coletável")));
                rows.Add((Color.Yellow, Loc.T("legend.selected", "Amarelo — selecionado")));
                rows.Add((Color.Empty, ""));
                rows.Add((Color.Silver, Loc.T("legend.keys1", "W/A/S/D andar · Espaço/Z subir/descer · Shift correr")));
                rows.Add((Color.Silver, Loc.T("legend.keys2", "Roda aproxima · botão do meio gira · M mapa · T esconde o terreno")));
                rows.Add((Color.Silver, Loc.T("legend.keys3", "Esquerdo no ponto arrasta · direito: novo, clonar, mover, excluir, editar")));
                rows.Add((Color.Silver, Loc.T("legend.keys4", "Ctrl+C/Ctrl+V copia e cola o ponto · Delete exclui · Ctrl+Z/Ctrl+Y")));
            }
            else rows.Add((Color.White, Loc.T("legend.closed", "▸ Legenda e atalhos (H)")));
            using var font = new Font("Segoe UI", 9.5f);
            int w = rows.Max(r => TextRenderer.MeasureText(r.T, font).Width) + 34, h = rows.Count * 19 + 12;
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                g.FillRectangle(bg, 0, 0, w, h);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].C.IsEmpty) continue;
                    using var b = new SolidBrush(rows[i].C);
                    bool swatch = _legendOpen && i is >= 1 and <= 6;
                    if (swatch) g.FillRectangle(b, 8, 8 + i * 19 + 4, 10, 10);
                    g.DrawString(rows[i].T, font, b, swatch ? 24 : 8, 6 + i * 19);
                }
            }
            _legendTex = UploadBitmap(bmp); _legendW = w; _legendH = h; _legendState = _legendOpen;
        }
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadIdentity(); GL.Ortho(0, _gl.Width, _gl.Height, 0, -1, 1);
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Fog);
        GL.BindTexture(TextureTarget.Texture2D, _legendTex); GL.Color4(1f, 1f, 1f, 1f);
        GL.Begin(PrimitiveType.Quads);
        GL.TexCoord2(0f, 0f); GL.Vertex2(10, 10); GL.TexCoord2(1f, 0f); GL.Vertex2(10 + _legendW, 10);
        GL.TexCoord2(1f, 1f); GL.Vertex2(10 + _legendW, 10 + _legendH); GL.TexCoord2(0f, 1f); GL.Vertex2(10, 10 + _legendH);
        GL.End();
        GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix(); GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
    }

    // ---------- escolha na tela ----------
    // ponto da tela de uma posição do jogo; null se atrás da câmera
    PointF? Project(Vector3 game)
    {
        var c = new Vector4(Gl(game.X, game.Y, game.Z), 1) * View * Proj;
        if (c.W <= 0.1f) return null;
        return new PointF((c.X / c.W + 1) / 2 * _gl.Width, (1 - c.Y / c.W) / 2 * _gl.Height);
    }

    /// <summary>Ponto do npcgen debaixo do mouse: a até 12 px do segmento pé–cabeça do modelo na tela (o mais perto da câmera).</summary>
    Marker? PickScreen(Point p)
    {
        Marker? best = null; float bestD = float.MaxValue;
        foreach (var m in _nearMarks)
        {
            if (Dist2(m) > 400 * 400) continue;
            var pos = PosOf(m);
            float top = m.Model is not null && _models.Top(m.Model) is { } t ? Math.Max(t, 1) : 2;
            if (Project(pos) is not { } a || Project(pos + new Vector3(0, top, 0)) is not { } b) continue;
            float abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
            float u = len2 < 1e-3f ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
            float dx = a.X + u * abx - p.X, dy = a.Y + u * aby - p.Y;
            float pix = MathF.Sqrt(dx * dx + dy * dy);
            float halfW = Math.Max(10, Math.Min(40, MathF.Sqrt(len2) * 0.3f));   // modelo mais largo de perto
            if (pix > halfW) continue;
            float score = pix + MathF.Sqrt(Dist2(m)) * 0.05f;
            if (score < bestD) { bestD = score; best = m; }
        }
        return best;
    }

    // raio do mouse até o chão: passos de 4 m e depois refina
    Vector3? Ray(Point p)
    {
        if (_gl.Width < 2 || _gl.Height < 2) return null;
        float nx = 2f * p.X / _gl.Width - 1, ny = 1 - 2f * p.Y / _gl.Height;
        var inv = Matrix4.Invert(View * Proj);
        var a = new Vector4(nx, ny, -1, 1) * inv; var b = new Vector4(nx, ny, 1, 1) * inv;
        var o = a.Xyz / a.W; var d = Vector3.Normalize(b.Xyz / b.W - o);
        float prev = 0;
        for (float s = 0; s < 4000; s += 4)
        {
            var q = o + d * s;
            if (_t.HeightAt(q.X, -q.Z) is { } h && q.Y <= h)
            {
                float lo = prev, hi = s;
                for (int i = 0; i < 12; i++) { float m = (lo + hi) / 2; var qm = o + d * m; if (_t.HeightAt(qm.X, -qm.Z) is { } hm && qm.Y <= hm) hi = m; else lo = m; }
                var r = o + d * hi;
                return new Vector3(r.X, _t.HeightAt(r.X, -r.Z) ?? r.Y, -r.Z);
            }
            prev = s;
        }
        return null;
    }

    void FindNext()
    {
        var q = _find.Text.Trim();
        if (q.Length == 0) return;
        // coordenadas: "x z" ou "x y z" (com vírgula, ponto e vírgula ou espaço)
        var nums = q.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split(", ", StringSplitOptions.RemoveEmptyEntries)).ToList();
        if (nums.Count is 2 or 3 && nums.All(n => float.TryParse(n.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            var f = nums.Select(n => float.Parse(n.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)).ToList();
            float x = f[0], z = f[^1];
            float y = f.Count == 3 ? f[1] : _t.HeightAt(x, z) ?? 0;
            LookAt(new Vector3(x, y, z)); _gl.Invalidate();
            _status.Text = string.Format(Loc.T("world.wentTo", "Câmera em X {0:0} Z {1:0}"), x, z);
            return;
        }
        var hits = _marks.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) { _status.Text = Loc.T("world.notFound", "Nenhum ponto com esse nome ou ID."); return; }
        _findAt = (_findAt + 1) % hits.Count;
        var m = hits[_findAt];
        _select(m.Tag); LookAt(PosOf(m));
        _status.Text = $"{m.Name}   ({_findAt + 1}/{hits.Count})";
        _gl.Invalidate();
    }

    void RightClick(Point p)
    {
        var under = PickScreen(p);
        var g = Ray(p) ?? (under is not null ? PosOf(under) : null);
        if (g is not { } at) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(string.Format(Loc.T("world.copy", "Copiar coordenada ({0:0.0}, {1:0.0}, {2:0.0})"), at.X, at.Y, at.Z), null,
            (_, _) => Clipboard.SetText(FormattableString.Invariant($"{at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("world.newMob", "Novo monstro aqui…"), null, (_, _) => { _newHere(at, 0); RefreshMarkers(); });
        menu.Items.Add(Loc.T("world.newNpc", "Novo NPC aqui…"), null, (_, _) => { _newHere(at, 1); RefreshMarkers(); });
        menu.Items.Add(Loc.T("world.newGather", "Novo coletável aqui…"), null, (_, _) => { _newHere(at, 2); RefreshMarkers(); });
        if (CreateNewHere is not null)
            menu.Items.Add(Loc.T("world.createHere", "Criar monstro/NPC NOVO (a partir de um modelo) aqui…"), null, (_, _) => CreateNewHere(at, 0));
        // em cima de um ponto: ele vira o selecionado e dá para editar no software ou excluir
        if (under is not null)
        {
            _select(under.Tag);
            menu.Items.Add(new ToolStripSeparator());
            if (EditPoint is not null)
                menu.Items.Add(string.Format(Loc.T("world.edit", "Editar {0} no software…"), under.Name), null, (_, _) => EditPoint(under.Tag));
            if (DeletePoint is not null)
                menu.Items.Add(string.Format(Loc.T("world.delete", "Excluir {0}"), under.Name), null, (_, _) => { if (Confirm()) { DeletePoint(under.Tag); RefreshMarkers(); } });
            menu.Items.Add(Loc.T("world.copyPoint", "Copiar ponto (Ctrl+C)"), null, (_, _) => _clip = under.Tag);
        }
        if (_selected() is { } s)
        {
            if (_moveHere is not null)
                menu.Items.Add(Loc.T("world.moveHere", "Mover o ponto selecionado para cá"), null, (_, _) => { _moveHere(s, at); RefreshMarkers(); });
            if (CloneHere is not null)
                menu.Items.Add(Loc.T("world.cloneHere", "Clonar o ponto selecionado aqui"), null, (_, _) => { CloneHere(s, at); RefreshMarkers(); });
        }
        if (_clip is not null && CloneHere is not null)
            menu.Items.Add(Loc.T("world.paste", "Colar ponto copiado aqui (Ctrl+V)"), null, (_, _) => { CloneHere(_clip, at); RefreshMarkers(); });
        menu.Show(_gl, p);
    }

    // ---------- mapa-múndi (M): o mapa pintado do jogo (mundo) ou o minimapa (instâncias) ----------
    const int MapTilePx = 256;

    Bitmap WorldMap()
    {
        if (_worldMap is not null) return _worldMap;
        bool painted = _t.Name == "world" && _terrain.ReadSurface(@"surfaces\maps\000.dds") is not null;
        float tileM = painted ? 1024 : WorldTerrain.TileM;
        int rows = (int)Math.Ceiling(_t.Height / tileM), cols = (int)Math.Ceiling(_t.Width / tileM);
        var bmp = new Bitmap(cols * MapTilePx, rows * MapTilePx);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 52, 64));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var img = painted ? (_terrain.ReadSurface($@"surfaces\maps\{r:00}{c}.dds") is { } d ? DdsDecoder.Decode(d) : null) : _terrain.MinimapImage(r, c);
                if (img is not { } im) continue;
                using var tile = new Bitmap(im.Width, im.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = tile.LockBits(new Rectangle(0, 0, im.Width, im.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, tile.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(im.Pixels, 0, bd.Scan0, im.Pixels.Length);
                tile.UnlockBits(bd);
                g.DrawImage(tile, c * MapTilePx, r * MapTilePx, MapTilePx, MapTilePx);
            }
        // só a área do mapa (o resto da última linha/coluna fica de fora)
        _mapMPerPx = tileM / MapTilePx;
        int w = (int)(_t.Width / _mapMPerPx), h = (int)(_t.Height / _mapMPerPx);
        if (w < bmp.Width || h < bmp.Height) { var crop = bmp.Clone(new Rectangle(0, 0, Math.Min(w, bmp.Width), Math.Min(h, bmp.Height)), bmp.PixelFormat); bmp.Dispose(); bmp = crop; }
        return _worldMap = bmp;
    }

    void ShowMap()
    {
        _keys.Clear();
        Cursor = Cursors.WaitCursor;
        var img = WorldMap();
        Cursor = Cursors.Default;
        var wa = Screen.FromControl(this).WorkingArea;
        using var f = new Form { StartPosition = FormStartPosition.CenterParent, Size = new Size(Math.Min(1000, wa.Width - 60), Math.Min(900, wa.Height - 60)), KeyPreview = true,
            FormBorderStyle = FormBorderStyle.SizableToolWindow, Text = Loc.T("world.mapTitle", "Mapa — roda: zoom · arrastar: mover · M ou Esc fecha") };
        var pic = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 34) };
        f.Controls.Add(pic);
        float minZoom = 1, zoom = 1;
        PointF center = new((_cam.X + _t.Width / 2) / _mapMPerPx, (_t.Height / 2 + _cam.Z) / _mapMPerPx);   // em px do mapa, começa na câmera
        // o mapa não sai da janela: zoom mínimo = cabe inteiro; o centro fica preso às bordas
        void Fit()
        {
            minZoom = Math.Min(pic.Width / (float)img.Width, pic.Height / (float)img.Height);
            zoom = Math.Clamp(zoom, minZoom, 6);
            float hw = pic.Width / 2f / zoom, hh = pic.Height / 2f / zoom;
            center = new(img.Width <= hw * 2 ? img.Width / 2f : Math.Clamp(center.X, hw, img.Width - hw),
                         img.Height <= hh * 2 ? img.Height / 2f : Math.Clamp(center.Y, hh, img.Height - hh));
        }
        PointF ToScreen(float x, float z) => new(((x + _t.Width / 2) / _mapMPerPx - center.X) * zoom + pic.Width / 2f, ((_t.Height / 2 - z) / _mapMPerPx - center.Y) * zoom + pic.Height / 2f);
        (float X, float Z) World(Point p) => (((p.X - pic.Width / 2f) / zoom + center.X) * _mapMPerPx - _t.Width / 2, _t.Height / 2 - ((p.Y - pic.Height / 2f) / zoom + center.Y) * _mapMPerPx);
        pic.Paint += (_, e) =>
        {
            var gr = e.Graphics;
            gr.InterpolationMode = zoom < 1 ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear : System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            gr.DrawImage(img, pic.Width / 2f - center.X * zoom, pic.Height / 2f - center.Y * zoom, img.Width * zoom, img.Height * zoom);
            var c = ToScreen(_cam.X, -_cam.Z);
            gr.FillEllipse(Brushes.Red, c.X - 6, c.Y - 6, 12, 12); gr.DrawEllipse(Pens.White, c.X - 6, c.Y - 6, 12, 12);
            // aviso fixo embaixo
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            var tip = Loc.T("world.mapTip", "Clique com o botão direito para teleportar");
            var sz = gr.MeasureString(tip, font);
            using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            var box = new RectangleF((pic.Width - sz.Width) / 2 - 10, pic.Height - sz.Height - 16, sz.Width + 20, sz.Height + 8);
            gr.FillRectangle(bg, box);
            gr.DrawString(tip, font, Brushes.White, box.X + 10, box.Y + 4);
        };
        pic.Resize += (_, _) => { Fit(); pic.Invalidate(); };
        Point drag = Point.Empty; bool panning = false;
        pic.MouseDown += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Middle) { panning = true; drag = e.Location; } };
        pic.MouseMove += (_, e) =>
        {
            if (panning) { center = new(center.X - (e.X - drag.X) / zoom, center.Y - (e.Y - drag.Y) / zoom); drag = e.Location; Fit(); pic.Invalidate(); }
            var (x, z) = World(e.Location);
            f.Text = string.Format(Loc.T("world.mapCoord", "Mapa — X {0:0}  Z {1:0} · roda: zoom · arrastar: mover · M ou Esc fecha"), x, z);
        };
        pic.MouseUp += (_, e) =>
        {
            panning = false;
            if (e.Button != MouseButtons.Right) return;
            var (x, z) = World(e.Location);
            if (_t.HeightAt(x, z) is not { } h) return;
            LookAt(new Vector3(x, h, z));
            f.Close();
        };
        pic.MouseWheel += (_, e) =>
        {
            var before = World(e.Location);
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.25f : 0.8f), minZoom, 6f);
            var after = World(e.Location);
            center = new(center.X - (after.X - before.X) / _mapMPerPx, center.Y + (after.Z - before.Z) / _mapMPerPx);
            Fit(); pic.Invalidate();
        };
        f.KeyDown += (_, e) => { if (e.KeyCode is Keys.M or Keys.Escape) f.Close(); };
        f.Shown += (_, _) => { zoom = Math.Max(1.5f, Math.Min(pic.Width / (float)img.Width, pic.Height / (float)img.Height)); Fit(); pic.Focus(); pic.Invalidate(); };
        f.ShowDialog(this);
        _gl.Invalidate();
    }

    // nome das instâncias (lista da comunidade; a numeração do servidor pode variar entre versões)
    static readonly Dictionary<string, string> MapNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["world"] = "Mainland", ["a01"] = "City of Abominations", ["a02"] = "Secret Passage", ["a03"] = "Silk Cave", ["a04"] = "Earth Cave",
        ["a05"] = "Firecrag Grotto", ["a06"] = "Den of Rabid Wolves", ["a07"] = "Cave of the Vicious", ["a08"] = "Hall of Deception",
        ["a09"] = "Gate of Delirium", ["a10"] = "Secret Frostcover Grounds", ["a11"] = "Valley of Disaster", ["a12"] = "Forest Ruins",
        ["a13"] = "Cave of Sadistic Glee", ["a14"] = "Wraithgate", ["a15"] = "Hallucinatory Trench", ["a16"] = "Eden", ["a17"] = "Brimstone Pit",
        ["a18"] = "Temple of the Dragon", ["a19"] = "Nightscream Island", ["a20"] = "Snake Isle", ["a21"] = "Lothranis (Heaven)",
        ["a22"] = "Momaganon (Hell)", ["a23"] = "Seat of Torment", ["a24"] = "Abaddon", ["a25"] = "Warsong City", ["a26"] = "Palace of Nirvana",
        ["a27"] = "Lunar Glade", ["a28"] = "Valley of Reciprocity", ["a29"] = "Frostcover City", ["a31"] = "Twilight Temple", ["a32"] = "Cube of Fate",
        ["a33"] = "Chrono City", ["a34"] = "Perfect Chapel", ["a35"] = "Faction Base", ["a37"] = "Morai", ["a38"] = "Phoenix Valley",
        ["a39"] = "Endless Universe", ["a40"] = "Rat Lurk Pits",
    };
    sealed record MapItem(string Key) { public override string ToString() => MapNames.TryGetValue(Key, out var n) ? $"{Key} — {n}" : Key; }

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.Selectable, true); } }
}
