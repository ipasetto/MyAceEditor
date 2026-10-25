using System.Diagnostics;
using MyAceEditor.PerfectWorld;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Mundo em 3D só com a câmera (como espectador), "no low": o mundo inteiro em malha leve (amostra a cada 32 m) com o
/// minimapa do jogo por cima, e detalhe total (2 m) nas peças de 512 m em volta da câmera; neblina no horizonte.
/// Marcadores dos pontos do npcgen; botão direito no chão copia a coordenada, cria um ponto ou move o selecionado.
/// W/A/S/D anda, Q/E desce/sobe, Shift corre, botão esquerdo arrastado gira, roda muda a velocidade,
/// M abre o mapa-múndi (botão direito no mapa leva a câmera), "Ir para" procura monstro/NPC pelo nome ou ID.
/// </summary>
internal sealed class WorldView : Form
{
    public sealed record Marker(float X, float Y, float Z, Color Color, string Name, object Tag);

    const float TileM = 512;          // peça do minimapa
    const int FarStep = 16;           // células por amostra na malha leve (16 × 2 m = 32 m)
    const int BuildPerFrame = 24;     // malhas novas por quadro (evita trancos ao andar)

    readonly TerrainFile _t;
    readonly Func<IReadOnlyList<Marker>> _markerSrc;
    readonly Action<Vector3> _newHere;
    readonly Action<object, Vector3>? _moveHere;
    readonly Func<object?> _selected;
    readonly Action<object> _select;
    readonly GLControl _gl;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0) };
    readonly ToolStrip _bar = new() { GripStyle = ToolStripGripStyle.Hidden };
    readonly ToolStripTextBox _find = new() { Width = 220 };
    readonly Dictionary<(int, int), (int Vbo, int Count)> _near = new(), _far = new();
    readonly Dictionary<(int, int), int> _tex = new();
    readonly PckArchive? _surf;
    readonly HashSet<Keys> _keys = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 10 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    List<Marker> _marks = new();
    Vector3 _cam;
    float _yaw = -90, _pitch = -25, _speed = 60;
    Point _drag; bool _dragging; double _last;
    Vector3? _hover;
    int _built, _findAt = -1;
    Bitmap? _worldMap;

    public WorldView(TerrainFile t, Vector3 start, Func<IReadOnlyList<Marker>> markers, Func<object?> selected, Action<object> select,
        Action<Vector3> newHere, Action<object, Vector3>? moveHere)
    {
        _t = t; _markerSrc = markers; _selected = selected; _select = select; _newHere = newHere; _moveHere = moveHere;
        Text = string.Format(Loc.T("world.title", "Mundo 3D — {0}"), t.Name);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(860, wa.Height - 40)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        _gl = new GLControl(new GLControlSettings { Profile = OpenTK.Windowing.Common.ContextProfile.Compatability, APIVersion = new Version(3, 3) }) { Dock = DockStyle.Fill };
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.goto", "Ir para:")));
        _bar.Items.Add(_find);
        _bar.Items.Add(new ToolStripButton(Loc.T("world.next", "Próximo ▶"), null, (_, _) => FindNext()));
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(new ToolStripButton(Loc.T("world.map", "🗺 Mapa (M)"), null, (_, _) => ShowMap()));
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.help", "   W/A/S/D anda · Q/E desce/sobe · Shift corre · arrastar gira · roda = velocidade")) { ForeColor = Color.DimGray });
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        _find.TextChanged += (_, _) => _findAt = -1;
        Controls.Add(_gl); Controls.Add(_bar); Controls.Add(_status);
        var element = Path.GetDirectoryName(Path.GetDirectoryName(t.Dir));
        try { if (element is not null && File.Exists(Path.Combine(element, "surfaces.pck"))) _surf = new PckArchive(Path.Combine(element, "surfaces.pck")); } catch { _surf = null; }
        RefreshMarkers();
        LookAt(start);
        _gl.Paint += (_, _) => Render();
        _gl.Resize += (_, _) => _gl.Invalidate();
        _gl.MouseDown += (_, e) => { _gl.Focus(); if (e.Button == MouseButtons.Left) { _dragging = true; _drag = e.Location; } };
        _gl.MouseUp += (_, e) => { _dragging = false; if (e.Button == MouseButtons.Right) RightClick(e.Location); else if (e.Button == MouseButtons.Left && _drag == e.Location) PickMarker(e.Location); };
        _gl.MouseMove += (_, e) =>
        {
            if (_dragging) { _yaw += (e.X - _drag.X) * 0.25f; _pitch = Math.Clamp(_pitch - (e.Y - _drag.Y) * 0.25f, -89, 89); _drag = e.Location; _gl.Invalidate(); return; }
            _hover = Ray(e.Location);
            var near = _hover is { } hv ? Nearest(hv) : null;
            _status.Text = _hover is { } h ? string.Format(Loc.T("world.coord", "X {0:0.0}   altura {1:0.0}   Z {2:0.0}   ·   botão direito: copiar / novo ponto aqui / mover o selecionado"), h.X, h.Y, h.Z)
                + (near is null ? "" : "   ·   " + near.Name) : "";
            _gl.Invalidate();
        };
        _gl.MouseWheel += (_, e) => _speed = Math.Clamp(_speed * (e.Delta > 0 ? 1.25f : 0.8f), 5, 3000);
        KeyDown += (_, e) =>
        {
            if (_find.Focused) return;
            if (e.KeyCode == Keys.M) { ShowMap(); return; }
            _keys.Add(e.KeyCode);
        };
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        Deactivate += (_, _) => _keys.Clear();
        _tick.Tick += (_, _) => Step();
        _tick.Start();
        FormClosed += (_, _) =>
        {
            _tick.Dispose(); _gl.MakeCurrent();
            foreach (var v in _near.Values.Concat(_far.Values)) if (v.Vbo != 0) GL.DeleteBuffer(v.Vbo);
            foreach (var x in _tex.Values) if (x != 0) GL.DeleteTexture(x);
            _surf?.Dispose(); _worldMap?.Dispose();
        };
    }

    /// <summary>Relê os pontos (depois de criar, mover ou trocar o arquivo).</summary>
    public void RefreshMarkers() { _marks = _markerSrc().ToList(); _gl?.Invalidate(); }

    /// <summary>Leva a câmera até o ponto (lista da aba NPCGen ou "Ir para").</summary>
    public void GoTo(object tag)
    {
        if (_marks.FirstOrDefault(m => m.Tag == tag) is { } m) { LookAt(new Vector3(m.X, m.Y, m.Z)); _gl.Invalidate(); }
    }

    // câmera 70 m ao sul e 45 m acima, olhando para o ponto
    void LookAt(Vector3 p) { _cam = Gl(p.X, p.Y + 45, p.Z - 70); _yaw = -90; _pitch = -30; }

    Vector3 Forward => new(MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)),
        MathF.Sin(MathHelper.DegreesToRadians(_pitch)), MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)));

    // movimento pelo tempo real do quadro (não pelo intervalo do timer); redesenha enquanto há malhas para montar
    void Step()
    {
        double now = _clock.Elapsed.TotalSeconds; float dt = (float)Math.Min(0.1, now - _last); _last = now;
        bool moving = _keys.Count > 0;
        if (moving)
        {
            float s = _speed * dt * (_keys.Contains(Keys.ShiftKey) ? 4 : 1);
            var f = Forward; var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
            if (_keys.Contains(Keys.W)) _cam += f * s;
            if (_keys.Contains(Keys.S)) _cam -= f * s;
            if (_keys.Contains(Keys.D)) _cam += right * s;
            if (_keys.Contains(Keys.A)) _cam -= right * s;
            if (_keys.Contains(Keys.E)) _cam.Y += s;
            if (_keys.Contains(Keys.Q)) _cam.Y -= s;
            // não atravessa o chão
            if (_t.HeightAt(_cam.X, -_cam.Z) is { } ground && _cam.Y < ground + 2) _cam.Y = ground + 2;
        }
        if (moving || _built > 0) _gl.Invalidate();
    }

    // o mundo do PW usa Z para o norte; aqui -Z do OpenGL vira +Z do jogo (espelha Z)
    static Vector3 Gl(float x, float y, float z) => new(x, y, -z);

    Matrix4 View => Matrix4.LookAt(_cam, _cam + Forward, Vector3.UnitY);
    Matrix4 Proj => Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), Math.Max(1, _gl.Width) / (float)Math.Max(1, _gl.Height), 1, 9000);

    int TileRows => (int)Math.Ceiling(_t.Height / TileM);
    int TileCols => (int)Math.Ceiling(_t.Width / TileM);

    void Render()
    {
        _gl.MakeCurrent();
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

        int budget = BuildPerFrame; _built = 0;
        var (cu, cv) = _t.ToGrid(_cam.X, -_cam.Z);
        float tileCells = TileM / _t.Cell;
        int ctr = (int)MathF.Floor(cv / tileCells), ctc = (int)MathF.Floor(cu / tileCells);
        int bpt = (int)(tileCells / _t.CellsPerBlock);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        for (int tr = 0; tr < TileRows; tr++)
            for (int tc = 0; tc < TileCols; tc++)
            {
                // peças vizinhas da câmera com detalhe total, se todas as malhas já estiverem prontas
                bool near = Math.Abs(tr - ctr) <= 1 && Math.Abs(tc - ctc) <= 1;
                if (near && NearReady(tr, tc, bpt, ref budget))
                {
                    for (int br = tr * bpt; br < (tr + 1) * bpt; br++)
                        for (int bc = tc * bpt; bc < (tc + 1) * bpt; bc++)
                            if (_near[(br, bc)] is { Vbo: not 0 } nb) Draw(nb, tr, tc);
                    continue;
                }
                if (!_far.TryGetValue((tr, tc), out var fb))
                {
                    if (budget-- <= 0) { _built++; continue; }
                    fb = _far[(tr, tc)] = Mesh(tr * (int)tileCells, tc * (int)tileCells, (int)tileCells, FarStep, tr, tc);
                }
                if (fb.Vbo != 0) Draw(fb, tr, tc);
            }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Texture2D);

        // marcadores: haste + cabeça (selecionado em amarelo, maior), até 1,5 km
        var sel = _selected();
        GL.LineWidth(3); GL.PointSize(7);
        GL.Begin(PrimitiveType.Lines);
        foreach (var m in _marks) if (Close(m)) { Col(m.Tag == sel ? Color.Yellow : m.Color); GL.Vertex3(Gl(m.X, m.Y, m.Z)); GL.Vertex3(Gl(m.X, m.Y + (m.Tag == sel ? 16 : 7), m.Z)); }
        if (_hover is { } h) { GL.Color3(1f, 1f, 1f); GL.Vertex3(Gl(h.X, h.Y, h.Z)); GL.Vertex3(Gl(h.X, h.Y + 4, h.Z)); }
        GL.End();
        GL.Begin(PrimitiveType.Points);
        foreach (var m in _marks) if (Close(m)) { Col(m.Tag == sel ? Color.Yellow : m.Color); GL.Vertex3(Gl(m.X, m.Y + (m.Tag == sel ? 16 : 7), m.Z)); }
        GL.End();
        GL.Disable(EnableCap.Fog);
        _gl.SwapBuffers();
    }

    bool Close(Marker m) => Math.Abs(m.X - _cam.X) < 1500 && Math.Abs(-m.Z - _cam.Z) < 1500;
    static void Col(Color c) => GL.Color3(c.R / 255f, c.G / 255f, c.B / 255f);

    bool NearReady(int tr, int tc, int bpt, ref int budget)
    {
        bool ready = true;
        for (int br = tr * bpt; br < (tr + 1) * bpt; br++)
            for (int bc = tc * bpt; bc < (tc + 1) * bpt; bc++)
                if (!_near.ContainsKey((br, bc)))
                {
                    if (budget-- <= 0) { ready = false; _built++; continue; }
                    int cpb = _t.CellsPerBlock;
                    _near[(br, bc)] = _t.Sample(bc * cpb, br * cpb) is null ? (0, 0) : Mesh(br * cpb, bc * cpb, cpb, 1, tr, tc);
                }
        return ready;
    }

    void Draw((int Vbo, int Count) b, int tr, int tc)
    {
        int tex = TileTex(tr, tc);
        GL.BindBuffer(BufferTarget.ArrayBuffer, b.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 20, 0);
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 20, 12);
        if (tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, tex); GL.Color3(1f, 1f, 1f); }
        else { GL.Disable(EnableCap.Texture2D); GL.Color3(0.45f, 0.55f, 0.35f); }
        GL.DrawArrays(PrimitiveType.Triangles, 0, b.Count);
    }

    // malha de (v0, u0) com n células de lado, amostrando a cada `step`; coordenadas de textura na peça (tr, tc)
    (int, int) Mesh(int v0, int u0, int n, int step, int tr, int tc)
    {
        float tileCells = TileM / _t.Cell, tu0 = tc * tileCells, tv0 = tr * tileCells;
        var data = new List<float>((n / step) * (n / step) * 30);
        void V(int u, int v)
        {
            float h = _t.Sample(u, v) ?? 0;
            var (x, z) = _t.FromGrid(u, v);
            data.Add(x); data.Add(h); data.Add(-z); data.Add((u - tu0) / tileCells); data.Add((v - tv0) / tileCells);
        }
        for (int r = 0; r < n; r += step)
            for (int c = 0; c < n; c += step)
            {
                int u = u0 + c, v = v0 + r;
                if (_t.Sample(u, v) is null) continue;
                V(u, v); V(u, v + step); V(u + step, v);
                V(u + step, v); V(u, v + step); V(u + step, v + step);
            }
        if (data.Count == 0) return (0, 0);
        int vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Count * 4, data.ToArray(), BufferUsageHint.StaticDraw);
        return (vbo, data.Count / 5);
    }

    // minimapa do jogo: surfaces\minimaps\<mapa>\LLCC.dds (peça de 512 m, linha 0 ao norte)
    (int[] Pixels, int Width, int Height)? TileImage(int row, int col) =>
        _surf?.Find($@"surfaces\minimaps\{_t.Name}\{row:00}{col:00}.dds") is { } e ? DdsDecoder.Decode(_surf.Read(e)) : null;

    int TileTex(int row, int col)
    {
        if (_tex.TryGetValue((row, col), out var id)) return id;
        id = 0;
        if (TileImage(row, col) is { } img)
        {
            id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
        }
        return _tex[(row, col)] = id;
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

    // marcador a até 6 m do ponto do chão
    Marker? Nearest(Vector3 g) => _marks.Where(m => Math.Abs(m.X - g.X) < 6 && Math.Abs(m.Z - g.Z) < 6)
        .OrderBy(m => (m.X - g.X) * (m.X - g.X) + (m.Z - g.Z) * (m.Z - g.Z)).FirstOrDefault();

    void PickMarker(Point p)
    {
        if (Ray(p) is { } g && Nearest(g) is { } best) { _select(best.Tag); _status.Text = best.Name; _gl.Invalidate(); }
    }

    void FindNext()
    {
        var q = _find.Text.Trim();
        if (q.Length == 0) return;
        var hits = _marks.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) { _status.Text = Loc.T("world.notFound", "Nenhum ponto com esse nome ou ID."); return; }
        _findAt = (_findAt + 1) % hits.Count;
        var m = hits[_findAt];
        _select(m.Tag); LookAt(new Vector3(m.X, m.Y, m.Z));
        _status.Text = $"{m.Name}   ({_findAt + 1}/{hits.Count})";
        _gl.Invalidate();
    }

    void RightClick(Point p)
    {
        if (Ray(p) is not { } g) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(string.Format(Loc.T("world.copy", "Copiar coordenada ({0:0.0}, {1:0.0}, {2:0.0})"), g.X, g.Y, g.Z), null,
            (_, _) => Clipboard.SetText(FormattableString.Invariant($"{g.X:0.##}, {g.Y:0.##}, {g.Z:0.##}")));
        menu.Items.Add(Loc.T("world.newHere", "Novo ponto aqui…"), null, (_, _) => { _newHere(g); RefreshMarkers(); });
        if (_moveHere is not null && _selected() is { } s)
            menu.Items.Add(Loc.T("world.moveHere", "Mover o ponto selecionado para cá"), null, (_, _) => { _moveHere(s, g); RefreshMarkers(); });
        menu.Show(_gl, p);
    }

    // ---------- mapa-múndi (M) ----------
    const int MapTilePx = 48;

    Bitmap WorldMap()
    {
        if (_worldMap is not null) return _worldMap;
        var bmp = new Bitmap(TileCols * MapTilePx, TileRows * MapTilePx);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 52, 64));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        for (int r = 0; r < TileRows; r++)
            for (int c = 0; c < TileCols; c++)
                if (TileImage(r, c) is { } img)
                {
                    using var tile = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var bd = tile.LockBits(new Rectangle(0, 0, img.Width, img.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, tile.PixelFormat);
                    System.Runtime.InteropServices.Marshal.Copy(img.Pixels, 0, bd.Scan0, img.Pixels.Length);
                    tile.UnlockBits(bd);
                    g.DrawImage(tile, c * MapTilePx, r * MapTilePx, MapTilePx, MapTilePx);
                }
        return _worldMap = bmp;
    }

    void ShowMap()
    {
        _keys.Clear();
        Cursor = Cursors.WaitCursor;
        var img = WorldMap();
        Cursor = Cursors.Default;
        var wa = Screen.FromControl(this).WorkingArea;
        float scale = Math.Min((wa.Height - 80f) / img.Height, (wa.Width - 80f) / img.Width);
        using var f = new Form { Text = Loc.T("world.mapTitle", "Mapa — botão direito leva a câmera · M ou Esc fecha"), StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size((int)(img.Width * scale), (int)(img.Height * scale)), FormBorderStyle = FormBorderStyle.FixedToolWindow, KeyPreview = true };
        float mPerPx = TileM / MapTilePx / scale;
        PointF ToPx(float x, float z) => new((x + _t.Width / 2) / mPerPx, (_t.Height / 2 - z) / mPerPx);
        var pic = new DoubleBufferedPanel { Dock = DockStyle.Fill };
        pic.Paint += (_, e) =>
        {
            e.Graphics.DrawImage(img, 0, 0, img.Width * scale, img.Height * scale);
            var c = ToPx(_cam.X, -_cam.Z);
            e.Graphics.FillEllipse(Brushes.Red, c.X - 6, c.Y - 6, 12, 12);
            e.Graphics.DrawEllipse(Pens.White, c.X - 6, c.Y - 6, 12, 12);
            if (_marks.FirstOrDefault(m => m.Tag == _selected()) is { } s) { var q = ToPx(s.X, s.Z); e.Graphics.FillRectangle(Brushes.Yellow, q.X - 4, q.Y - 4, 8, 8); }
        };
        pic.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            float x = e.X * mPerPx - _t.Width / 2, z = _t.Height / 2 - e.Y * mPerPx;
            if (_t.HeightAt(x, z) is not { } h) return;
            LookAt(new Vector3(x, h, z));
            f.Close();
        };
        pic.MouseMove += (_, e) => f.Text = string.Format(Loc.T("world.mapCoord", "Mapa — X {0:0}  Z {1:0} · botão direito leva a câmera · M ou Esc fecha"), e.X * mPerPx - _t.Width / 2, _t.Height / 2 - e.Y * mPerPx);
        f.KeyDown += (_, e) => { if (e.KeyCode is Keys.M or Keys.Escape) f.Close(); };
        f.Controls.Add(pic);
        f.ShowDialog(this);
        _gl.Invalidate();
    }

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; } }
}
