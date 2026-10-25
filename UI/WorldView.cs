using MyAceEditor.PerfectWorld;
using OpenTK.GLControl;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Mundo em 3D só com a câmera (como espectador): terreno das alturas do cliente (TerrainFile), marcadores dos pontos
/// do npcgen e botão direito no chão para copiar a coordenada, criar um ponto ali ou mover o ponto selecionado.
/// W/A/S/D anda, Q/E desce/sobe, Shift corre, botão esquerdo arrastado gira a câmera, roda muda a velocidade.
/// </summary>
internal sealed class WorldView : Form
{
    public sealed record Marker(float X, float Y, float Z, Color Color, string Name, object Tag);

    readonly TerrainFile _t;
    readonly Func<IReadOnlyList<Marker>> _markers;
    readonly Action<Vector3> _newHere;
    readonly Action<object, Vector3>? _moveHere;
    readonly Func<object?> _selected;
    readonly Action<object> _select;
    readonly GLControl _gl;
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(6, 3, 0, 0) };
    readonly Dictionary<(int, int), (int Vbo, int Count)> _vbos = new();
    readonly HashSet<Keys> _keys = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 16 };
    Vector3 _cam;
    float _yaw = -90, _pitch = -25, _speed = 60;
    Point _drag; bool _dragging;
    Vector3? _hover;

    public WorldView(TerrainFile t, Vector3 start, Func<IReadOnlyList<Marker>> markers, Func<object?> selected, Action<object> select,
        Action<Vector3> newHere, Action<object, Vector3>? moveHere)
    {
        _t = t; _markers = markers; _selected = selected; _select = select; _newHere = newHere; _moveHere = moveHere;
        Text = string.Format(Loc.T("world.title", "Mundo 3D — {0}"), t.Name);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(860, wa.Height - 40)); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        _gl = new GLControl(new GLControlSettings { Profile = OpenTK.Windowing.Common.ContextProfile.Compatability, APIVersion = new Version(3, 3) }) { Dock = DockStyle.Fill };
        Controls.Add(_gl); Controls.Add(_status);
        _cam = Gl(start.X, start.Y + 60, start.Z) + new Vector3(0, 0, 80);
        _gl.Paint += (_, _) => Render();
        _gl.Resize += (_, _) => _gl.Invalidate();
        _gl.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _drag = e.Location; } };
        _gl.MouseUp += (_, e) => { _dragging = false; if (e.Button == MouseButtons.Right) RightClick(e.Location); else if (e.Button == MouseButtons.Left && _drag == e.Location) PickMarker(e.Location); };
        _gl.MouseMove += (_, e) =>
        {
            if (_dragging) { _yaw += (e.X - _drag.X) * 0.25f; _pitch = Math.Clamp(_pitch - (e.Y - _drag.Y) * 0.25f, -89, 89); _drag = e.Location; }
            _hover = Ray(e.Location);
            var near = _hover is { } hv ? Nearest(hv) : null;
            _status.Text = _hover is { } h ? string.Format(Loc.T("world.coord", "X {0:0.0}   altura {1:0.0}   Z {2:0.0}   ·   botão direito: copiar / novo ponto aqui / mover o selecionado"), h.X, h.Y, h.Z)
                + (near is null ? "" : "   ·   " + near.Name) : "";
            _gl.Invalidate();
        };
        _gl.MouseWheel += (_, e) => _speed = Math.Clamp(_speed * (e.Delta > 0 ? 1.25f : 0.8f), 5, 2000);
        KeyDown += (_, e) => _keys.Add(e.KeyCode);
        KeyUp += (_, e) => _keys.Remove(e.KeyCode);
        _tick.Tick += (_, _) => Move();
        _tick.Start();
        FormClosed += (_, _) => { _tick.Dispose(); _gl.MakeCurrent(); foreach (var v in _vbos.Values) GL.DeleteBuffer(v.Vbo); };
    }

    Vector3 Forward => new(MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)),
        MathF.Sin(MathHelper.DegreesToRadians(_pitch)), MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)));

    void Move()
    {
        if (_keys.Count == 0) return;
        float s = _speed * 0.016f * (_keys.Contains(Keys.ShiftKey) ? 4 : 1);
        var f = Forward; var right = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
        if (_keys.Contains(Keys.W)) _cam += f * s;
        if (_keys.Contains(Keys.S)) _cam -= f * s;
        if (_keys.Contains(Keys.D)) _cam += right * s;
        if (_keys.Contains(Keys.A)) _cam -= right * s;
        if (_keys.Contains(Keys.E)) _cam.Y += s;
        if (_keys.Contains(Keys.Q)) _cam.Y -= s;
        _gl.Invalidate();
    }

    // o mundo do PW usa Z para o norte; aqui -Z do OpenGL vira +Z do jogo (espelha Z)
    static Vector3 Gl(float x, float y, float z) => new(x, y, -z);

    Matrix4 View => Matrix4.LookAt(_cam, _cam + Forward, Vector3.UnitY);
    Matrix4 Proj => Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), Math.Max(1, _gl.Width) / (float)Math.Max(1, _gl.Height), 1, 4000);

    void Render()
    {
        _gl.MakeCurrent();
        GL.Viewport(0, 0, _gl.Width, _gl.Height);
        GL.ClearColor(0.55f, 0.72f, 0.9f, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Enable(EnableCap.DepthTest);
        GL.MatrixMode(MatrixMode.Projection); var p = Proj; GL.LoadMatrix(ref p);
        GL.MatrixMode(MatrixMode.Modelview); var v = View; GL.LoadMatrix(ref v);

        // blocos de 64 m perto da câmera
        var (cu, cv) = _t.ToGrid(_cam.X, -_cam.Z);
        int cpb = _t.CellsPerBlock, rad = 14;
        int br0 = (int)(cv / cpb), bc0 = (int)(cu / cpb);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray);
        for (int br = br0 - rad; br <= br0 + rad; br++)
            for (int bc = bc0 - rad; bc <= bc0 + rad; bc++)
            {
                if (br < 0 || bc < 0 || br >= _t.Rows * _t.BlocksPerPiece || bc >= _t.Cols * _t.BlocksPerPiece) continue;
                if (BlockVbo(br, bc) is not { } b) continue;
                GL.BindBuffer(BufferTarget.ArrayBuffer, b.Vbo);
                GL.VertexPointer(3, VertexPointerType.Float, 24, 0);
                GL.ColorPointer(3, ColorPointerType.Float, 24, 12);
                GL.DrawArrays(PrimitiveType.Triangles, 0, b.Count);
            }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.ColorArray);

        // marcadores: haste + ponto (selecionado em amarelo, maior)
        var sel = _selected();
        GL.LineWidth(2);
        GL.Begin(PrimitiveType.Lines);
        foreach (var m in _markers())
        {
            if (Math.Abs(m.X - _cam.X) > 900 || Math.Abs(-m.Z - _cam.Z) > 900) continue;
            var c = m.Tag == sel ? Color.Yellow : m.Color;
            GL.Color3(c.R / 255f, c.G / 255f, c.B / 255f);
            GL.Vertex3(Gl(m.X, m.Y, m.Z)); GL.Vertex3(Gl(m.X, m.Y + (m.Tag == sel ? 14 : 6), m.Z));
        }
        if (_hover is { } h) { GL.Color3(1f, 1f, 1f); GL.Vertex3(Gl(h.X, h.Y, h.Z)); GL.Vertex3(Gl(h.X, h.Y + 4, h.Z)); }
        GL.End();
        _gl.SwapBuffers();
    }

    (int Vbo, int Count)? BlockVbo(int br, int bc)
    {
        if (_vbos.TryGetValue((br, bc), out var b)) return b.Vbo == 0 ? null : b;
        int cpb = _t.CellsPerBlock;
        var data = new List<float>(cpb * cpb * 36);
        void V(int u, int v)
        {
            float h = _t.Sample(u, v) ?? 0;
            var (x, z) = _t.FromGrid(u, v);
            // cor pela altura + sombra pela inclinação (luz vinda do noroeste)
            float dx = (_t.Sample(u + 1, v) ?? h) - h, dz = (_t.Sample(u, v + 1) ?? h) - h;
            float shade = Math.Clamp(0.85f - (dx - dz) * 0.06f, 0.45f, 1.15f);
            float k = Math.Clamp((h - 50) / 600, 0, 1);
            var c = new Vector3(0.32f + 0.4f * k, 0.5f + 0.2f * k - 0.15f * k * k, 0.25f + 0.45f * k * k) * shade;
            data.AddRange(new[] { x, h, -z, c.X, c.Y, c.Z });
        }
        if (_t.Sample(bc * cpb, br * cpb) is null) { _vbos[(br, bc)] = (0, 0); return null; }
        for (int r = 0; r < cpb; r++)
            for (int c = 0; c < cpb; c++)
            {
                int u = bc * cpb + c, v = br * cpb + r;
                V(u, v); V(u, v + 1); V(u + 1, v);
                V(u + 1, v); V(u, v + 1); V(u + 1, v + 1);
            }
        int vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Count * 4, data.ToArray(), BufferUsageHint.StaticDraw);
        return _vbos[(br, bc)] = (vbo, data.Count / 6);
    }

    // raio do mouse até o chão (passos de 1 m, até 2 km)
    Vector3? Ray(Point p)
    {
        if (_gl.Width < 2 || _gl.Height < 2) return null;
        float nx = 2f * p.X / _gl.Width - 1, ny = 1 - 2f * p.Y / _gl.Height;
        var inv = Matrix4.Invert(View * Proj);
        var a = new Vector4(nx, ny, -1, 1) * inv; var b = new Vector4(nx, ny, 1, 1) * inv;
        var o = a.Xyz / a.W; var d = Vector3.Normalize(b.Xyz / b.W - o);
        for (float s = 0; s < 2000; s += 1)
        {
            var q = o + d * s;
            if (_t.HeightAt(q.X, -q.Z) is { } h && q.Y <= h) return new Vector3(q.X, h, -q.Z);
        }
        return null;
    }

    // marcador a até 6 m do ponto do chão
    Marker? Nearest(Vector3 g) => _markers().Where(m => Math.Abs(m.X - g.X) < 6 && Math.Abs(m.Z - g.Z) < 6)
        .OrderBy(m => (m.X - g.X) * (m.X - g.X) + (m.Z - g.Z) * (m.Z - g.Z)).FirstOrDefault();

    void PickMarker(Point p)
    {
        if (Ray(p) is { } g && Nearest(g) is { } best) { _select(best.Tag); _status.Text = best.Name; _gl.Invalidate(); }
    }

    void RightClick(Point p)
    {
        if (Ray(p) is not { } g) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(string.Format(Loc.T("world.copy", "Copiar coordenada ({0:0.0}, {1:0.0}, {2:0.0})"), g.X, g.Y, g.Z), null,
            (_, _) => Clipboard.SetText(FormattableString.Invariant($"{g.X:0.##}, {g.Y:0.##}, {g.Z:0.##}")));
        menu.Items.Add(Loc.T("world.newHere", "Novo ponto aqui…"), null, (_, _) => { _newHere(g); _gl.Invalidate(); });
        if (_moveHere is not null && _selected() is { } s)
            menu.Items.Add(Loc.T("world.moveHere", "Mover o ponto selecionado para cá"), null, (_, _) => { _moveHere(s, g); _gl.Invalidate(); });
        menu.Show(_gl, p);
    }
}
