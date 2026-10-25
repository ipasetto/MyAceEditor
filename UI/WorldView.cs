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
    /// <summary>Ponto no mundo; Model = .ecm do elements (modelo parado), Yaw em graus (0 = norte).</summary>
    public sealed record Marker(float X, float Y, float Z, Color Color, string Name, object Tag, string? Model = null, float Yaw = 0);

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
    float _yaw = -90, _pitch = -25;
    const float Speed = 40;           // m/s andando; Shift = ×4
    readonly WorldModels _models;
    readonly PckArchive? _texPck;                                       // textures.pck (texturas do chão)
    readonly Dictionary<string, int> _groundTex = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(int, int, int), int> _maskTex = new();
    readonly Dictionary<string, (int Tex, int W, int H)> _labels = new();
    object? _movingTag; Vector3? _movePos;   // arrastando um ponto com o botão esquerdo
    Point _drag, _down; bool _dragging; double _last;
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
        _bar.Items.Add(new ToolStripLabel(Loc.T("world.help", "   W/A/S/D anda · Q/E desce/sobe · Shift corre · roda aproxima · botão do meio gira · esquerdo arrasta o ponto · direito edita")) { ForeColor = Color.DimGray });
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        _find.TextChanged += (_, _) => _findAt = -1;
        Controls.Add(_gl); Controls.Add(_bar); Controls.Add(_status);
        var element = Path.GetDirectoryName(Path.GetDirectoryName(t.Dir));
        try { if (element is not null && File.Exists(Path.Combine(element, "surfaces.pck"))) _surf = new PckArchive(Path.Combine(element, "surfaces.pck")); } catch { _surf = null; }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _models = new WorldModels(element);
        try { if (element is not null && File.Exists(Path.Combine(element, "textures.pck"))) _texPck = new PckArchive(Path.Combine(element, "textures.pck")); } catch { _texPck = null; }
        RefreshMarkers();
        LookAt(start);
        _gl.Paint += (_, _) => Render();
        _gl.Resize += (_, _) => _gl.Invalidate();
        // girar: segurar o botão do meio (ou o esquerdo) e arrastar; clique esquerdo parado seleciona o marcador
        // esquerdo em cima de um ponto: arrasta o ponto; esquerdo no vazio ou botão do meio: gira a câmera
        _gl.MouseDown += (_, e) =>
        {
            _gl.Focus(); _down = _drag = e.Location;
            if (e.Button == MouseButtons.Left && Ray(e.Location) is { } g && Nearest(g) is { } mk) { _movingTag = mk.Tag; _movePos = null; return; }
            if (e.Button is MouseButtons.Left or MouseButtons.Middle) _dragging = true;
        };
        _gl.MouseUp += (_, e) =>
        {
            _dragging = false;
            if (_movingTag is { } tag)
            {
                bool moved = _movePos is not null && (Math.Abs(e.X - _down.X) > 3 || Math.Abs(e.Y - _down.Y) > 3);
                if (moved && _moveHere is not null) { _moveHere(tag, _movePos!.Value); RefreshMarkers(); }
                else { _select(tag); _status.Text = _marks.FirstOrDefault(m => m.Tag == tag)?.Name ?? ""; }
                _movingTag = null; _movePos = null; _gl.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right) RightClick(e.Location);
        };
        Shown += (_, _) => { Activate(); _gl.Focus(); };   // já abre pronta para W/A/S/D
        _gl.MouseMove += (_, e) =>
        {
            if (_dragging) { _yaw += (e.X - _drag.X) * 0.25f; _pitch = Math.Clamp(_pitch - (e.Y - _drag.Y) * 0.25f, -89, 89); _drag = e.Location; _gl.Invalidate(); return; }
            if (_movingTag is not null) { if (Ray(e.Location) is { } mp) _movePos = mp; _gl.Invalidate(); return; }
            _hover = Ray(e.Location);
            var near = _hover is { } hv ? Nearest(hv) : null;
            _status.Text = _hover is { } h ? string.Format(Loc.T("world.coord", "X {0:0.0}   altura {1:0.0}   Z {2:0.0}   ·   botão direito: copiar / novo ponto aqui / mover o selecionado"), h.X, h.Y, h.Z)
                + (near is null ? "" : "   ·   " + near.Name) : "";
            _gl.Invalidate();
        };
        // roda: aproxima/afasta a câmera para onde ela olha (Shift = 4×)
        _gl.MouseWheel += (_, e) => { _cam += Forward * (e.Delta / 120f) * 12f * ((ModifierKeys & Keys.Shift) != 0 ? 4 : 1); _gl.Invalidate(); };
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
            _surf?.Dispose(); _worldMap?.Dispose(); _models.Dispose(); _texPck?.Dispose();
            foreach (var x in _groundTex.Values.Concat(_maskTex.Values)) if (x != 0) GL.DeleteTexture(x);
            foreach (var l in _labels.Values) GL.DeleteTexture(l.Tex);
        };
    }

    /// <summary>Relê os pontos (depois de criar, mover ou trocar o arquivo).</summary>
    public void RefreshMarkers() { _marks = _markerSrc().ToList(); _gl?.Invalidate(); }

    /// <summary>Leva a câmera até o ponto (lista da aba NPCGen ou "Ir para").</summary>
    public void GoTo(object tag)
    {
        if (_marks.FirstOrDefault(m => m.Tag == tag) is { } m) { LookAt(new Vector3(m.X, m.Y, m.Z)); _gl.Invalidate(); }
    }

    // câmera 14 m ao sul e 7 m acima, olhando para o ponto
    void LookAt(Vector3 p) { _cam = Gl(p.X, p.Y + 7, p.Z - 14); _yaw = -90; _pitch = -22; }

    Vector3 Forward => new(MathF.Cos(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)),
        MathF.Sin(MathHelper.DegreesToRadians(_pitch)), MathF.Sin(MathHelper.DegreesToRadians(_yaw)) * MathF.Cos(MathHelper.DegreesToRadians(_pitch)));

    // movimento pelo tempo real do quadro (não pelo intervalo do timer); redesenha enquanto há malhas para montar
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
                            if (_near[(br, bc)] is { Vbo: not 0 } nb && !DrawGround(nb, br, bc)) Draw(nb, tr, tc);
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

        // modelos parados até 300 m (no máximo 2 carregados por quadro); haste para quem não tem modelo
        var sel = _selected();
        int loads = 2;
        GL.Enable(EnableCap.Lighting); GL.Enable(EnableCap.Light0); GL.Enable(EnableCap.Normalize); GL.Enable(EnableCap.ColorMaterial);
        GL.Light(LightName.Light0, LightParameter.Position, new Vector4(-0.4f, 1f, 0.3f, 0));
        GL.Light(LightName.Light0, LightParameter.Diffuse, new Color4(0.8f, 0.8f, 0.75f, 1f));
        GL.Light(LightName.Light0, LightParameter.Ambient, new Color4(0.5f, 0.5f, 0.55f, 1f));
        GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.3f);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.NormalArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        var withModel = new HashSet<Marker>();
        foreach (var m in _marks)
        {
            if (m.Model is null || Dist2(m) > 300 * 300) continue;
            if (!_models.Loaded(m.Model)) { if (loads-- <= 0) { _built++; continue; } _models.Load(m.Model); }
            GL.Color3(1f, 1f, 1f);
            if (_models.Draw(m.Model, PosOf(m), m.Yaw)) withModel.Add(m);
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.NormalArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.ColorMaterial); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.AlphaTest);

        GL.LineWidth(3); GL.PointSize(7);
        GL.Begin(PrimitiveType.Lines);
        foreach (var m in _marks)
        {
            if (!Close(m) || withModel.Contains(m)) continue;
            var mp = PosOf(m); float top = withModel.Contains(m) ? (_models.Top(m.Model!) ?? 0) + 1 : 0;
            Col(m.Tag == sel ? Color.Yellow : m.Color); GL.Vertex3(Gl(mp.X, mp.Y + top, mp.Z)); GL.Vertex3(Gl(mp.X, mp.Y + top + (m.Tag == sel ? 6 : 7), mp.Z));
        }
        if (_hover is { } h) { GL.Color3(1f, 1f, 1f); GL.Vertex3(Gl(h.X, h.Y, h.Z)); GL.Vertex3(Gl(h.X, h.Y + 4, h.Z)); }
        GL.End();

        // nomes e coordenadas sobre os pontos até 70 m (os 40 mais perto)
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); GL.DepthMask(false);
        GL.Enable(EnableCap.Texture2D); GL.Color3(1f, 1f, 1f);
        foreach (var m in _marks.Where(m => Dist2(m) < 70 * 70 || m.Tag == sel && Dist2(m) < 400 * 400).OrderBy(Dist2).Take(40))
        {
            var lp = PosOf(m);
            float top = m.Model is not null && _models.Top(m.Model) is { } t ? t + 1.2f : 8f;
            DrawLabel($"{m.Name}\n{lp.X:0}, {lp.Y:0}, {lp.Z:0}", new Vector3(lp.X, lp.Y + top, lp.Z), m.Tag == sel);
        }
        GL.Disable(EnableCap.Texture2D); GL.DepthMask(true); GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.Fog);
        _gl.SwapBuffers();
    }

    bool Close(Marker m) => Math.Abs(m.X - _cam.X) < 1500 && Math.Abs(-m.Z - _cam.Z) < 1500;
    float Dist2(Marker m) { float dx = m.X - _cam.X, dz = m.Z + _cam.Z, dy = m.Y - _cam.Y; return dx * dx + dy * dy + dz * dz; }
    Vector3 PosOf(Marker m) => m.Tag == _movingTag && _movePos is { } mp ? mp : new Vector3(m.X, m.Y, m.Z);

    // texto do nome numa textura (em cache) desenhada de frente para a câmera, do mesmo tamanho na tela
    void DrawLabel(string text, Vector3 at, bool selected)
    {
        if (!_labels.TryGetValue((selected ? "*" : "") + text, out var l))
        {
            using var font = new Font("Segoe UI", 11, FontStyle.Bold);
            var size = TextRenderer.MeasureText(text, font);
            using var bmp = new Bitmap(size.Width + 8, size.Height + 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var sf = new StringFormat { Alignment = StringAlignment.Center };
                var r = new RectangleF(0, 2, bmp.Width, bmp.Height);
                g.DrawString(text, font, Brushes.Black, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
                g.DrawString(text, font, selected ? Brushes.Yellow : Brushes.White, r, sf);
            }
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bd.Scan0);
            bmp.UnlockBits(bd);
            l = _labels[(selected ? "*" : "") + text] = (tex, bmp.Width, bmp.Height);
            if (_labels.Count > 3000) { foreach (var x in _labels.Values) GL.DeleteTexture(x.Tex); _labels.Clear(); }
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

    // ---------- chão com as texturas do jogo (perto da câmera) ----------
    // uma passada por camada: unidade 0 = textura repetida (escala da camada), unidade 1 = máscara da área (alfa);
    // coordenadas geradas pela posição (texgen), então a malha é a mesma do minimapa
    bool DrawGround((int Vbo, int Count) b, int br, int bc)
    {
        if (_texPck is null) return false;
        float blockM = _t.CellsPerBlock * _t.Cell;
        int ar = (int)(br * blockM / TerrainFile.AreaM), ac = (int)(bc * blockM / TerrainFile.AreaM);
        if (_t.Ground(ar, ac) is not { } layers) return false;
        int baseTex = GroundTex(layers[0].Texture);
        if (baseTex == 0) return false;
        float x0 = -_t.Width / 2 + ac * TerrainFile.AreaM, ztop = _t.Height / 2 - ar * TerrainFile.AreaM, inv = 1f / TerrainFile.AreaM;

        GL.BindBuffer(BufferTarget.ArrayBuffer, b.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 20, 0);
        GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Enable(EnableCap.Texture2D);
        GL.Enable(EnableCap.TextureGenS); GL.Enable(EnableCap.TextureGenT);
        GL.TexGen(TextureCoordName.S, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
        GL.TexGen(TextureCoordName.T, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
        // cor = textura, alfa = 1 (o alfa vem só da máscara)
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, 0x8570);   // COMBINE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8571, 0x2100);          // RGB = MODULATE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8580, 0x1702);          //   textura
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8581, 0x8577);          //   × cor (sombra leve)
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8572, 0x1E01);          // ALFA = REPLACE
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8588, 0x8577);          //   da cor
        GL.Color4(0.86f, 0.86f, 0.86f, 1f);
        for (int i = 0; i < layers.Length; i++)
        {
            var l = layers[i];
            int tex = i == 0 ? baseTex : GroundTex(l.Texture);
            if (tex == 0) continue;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexGen(TextureCoordName.S, TextureGenParameter.ObjectPlane, new[] { l.UScale, 0, 0, 0 });
            GL.TexGen(TextureCoordName.T, TextureGenParameter.ObjectPlane, new[] { 0, 0, l.VScale, 0 });
            bool masked = i > 0 && l.Mask is not null;
            if (masked)
            {
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, MaskTex(ar, ac, i, l));
                GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
                GL.Enable(EnableCap.TextureGenS); GL.Enable(EnableCap.TextureGenT);
                GL.TexGen(TextureCoordName.S, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
                GL.TexGen(TextureCoordName.T, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
                // s = (x − x0)/256, t = (zTopo − zJogo)/256 = (zGL + zTopo)/256
                GL.TexGen(TextureCoordName.S, TextureGenParameter.ObjectPlane, new[] { inv, 0, 0, -x0 * inv });
                GL.TexGen(TextureCoordName.T, TextureGenParameter.ObjectPlane, new[] { 0, 0, inv, ztop * inv });
                GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthFunc(DepthFunction.Lequal); GL.DepthMask(false);
            }
            GL.DrawArrays(PrimitiveType.Triangles, 0, b.Count);
            if (masked)
            {
                GL.Disable(EnableCap.TextureGenS); GL.Disable(EnableCap.TextureGenT); GL.Disable(EnableCap.Texture2D);
                GL.Disable(EnableCap.Blend); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
            }
        }
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.Disable(EnableCap.TextureGenS); GL.Disable(EnableCap.TextureGenT);
        GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.Color4(1f, 1f, 1f, 1f);
        return true;
    }

    int GroundTex(string path)
    {
        if (_groundTex.TryGetValue(path, out var id)) return id;
        id = 0;
        if (_texPck?.Find(path.ToLowerInvariant()) is { } e && DdsDecoder.Decode(_texPck.Read(e)) is { } img)
        {
            id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, img.Width, img.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, img.Pixels);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }
        return _groundTex[path] = id;
    }

    int MaskTex(int ar, int ac, int i, TerrainFile.GroundLayer l)
    {
        if (_maskTex.TryGetValue((ar, ac, i), out var id)) return id;
        if (_maskTex.Count > 600) { foreach (var x in _maskTex.Values) GL.DeleteTexture(x); _maskTex.Clear(); }
        id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Alpha8, l.MaskSide, l.MaskSide, 0, PixelFormat.Alpha, PixelType.UnsignedByte, l.Mask!);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return _maskTex[(ar, ac, i)] = id;
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

    // ---------- mapa-múndi (M): o mapa pintado do jogo ----------
    // surfaces\maps\LLC.dds = peças de 1 km (LL linha a partir do norte, C coluna); sem ele, o minimapa
    const int MapTilePx = 256;

    Bitmap WorldMap()
    {
        if (_worldMap is not null) return _worldMap;
        bool painted = _t.Name == "world" && _surf?.Find(@"surfaces\maps\000.dds") is not null;
        float tileM = painted ? 1024 : TileM;
        int rows = (int)Math.Ceiling(_t.Height / tileM), cols = (int)Math.Ceiling(_t.Width / tileM);
        var bmp = new Bitmap(cols * MapTilePx, rows * MapTilePx);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 52, 64));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var img = painted ? (_surf!.Find($@"surfaces\maps\{r:00}{c}.dds") is { } e ? DdsDecoder.Decode(_surf.Read(e)) : null) : TileImage(r, c);
                if (img is not { } im) continue;
                using var tile = new Bitmap(im.Width, im.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bd = tile.LockBits(new Rectangle(0, 0, im.Width, im.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, tile.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(im.Pixels, 0, bd.Scan0, im.Pixels.Length);
                tile.UnlockBits(bd);
                g.DrawImage(tile, c * MapTilePx, r * MapTilePx, MapTilePx, MapTilePx);
            }
        _mapMPerPx = tileM / MapTilePx;
        return _worldMap = bmp;
    }
    float _mapMPerPx = 2;

    void ShowMap()
    {
        _keys.Clear();
        Cursor = Cursors.WaitCursor;
        var img = WorldMap();
        Cursor = Cursors.Default;
        var wa = Screen.FromControl(this).WorkingArea;
        using var f = new Form { StartPosition = FormStartPosition.CenterParent, Size = new Size(Math.Min(1000, wa.Width - 60), Math.Min(900, wa.Height - 60)), KeyPreview = true,
            FormBorderStyle = FormBorderStyle.SizableToolWindow, Text = Loc.T("world.mapTitle", "Mapa — roda: zoom · arrastar: mover · botão direito leva a câmera · M ou Esc fecha") };
        var pic = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 52, 64) };
        f.Controls.Add(pic);
        // abre com zoom no ponto selecionado (senão na câmera)
        var sel = _marks.FirstOrDefault(m => m.Tag == _selected());
        float cx = sel?.X ?? _cam.X, cz = sel?.Z ?? -_cam.Z;
        float zoom = 1.5f;                              // px de tela por px do mapa
        PointF center = new((cx + _t.Width / 2) / _mapMPerPx, (_t.Height / 2 - cz) / _mapMPerPx);   // em px do mapa
        PointF ToScreen(float x, float z) => new((((x + _t.Width / 2) / _mapMPerPx) - center.X) * zoom + pic.Width / 2f, (((_t.Height / 2 - z) / _mapMPerPx) - center.Y) * zoom + pic.Height / 2f);
        (float X, float Z) World(Point p) => (((p.X - pic.Width / 2f) / zoom + center.X) * _mapMPerPx - _t.Width / 2, _t.Height / 2 - ((p.Y - pic.Height / 2f) / zoom + center.Y) * _mapMPerPx);
        pic.Paint += (_, e) =>
        {
            var gr = e.Graphics;
            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            gr.DrawImage(img, pic.Width / 2f - center.X * zoom, pic.Height / 2f - center.Y * zoom, img.Width * zoom, img.Height * zoom);
            var c = ToScreen(_cam.X, -_cam.Z);
            gr.FillEllipse(Brushes.Red, c.X - 6, c.Y - 6, 12, 12); gr.DrawEllipse(Pens.White, c.X - 6, c.Y - 6, 12, 12);
            if (sel is not null)
            {
                var q = ToScreen(sel.X, sel.Z);
                gr.FillEllipse(Brushes.Yellow, q.X - 5, q.Y - 5, 10, 10); gr.DrawEllipse(Pens.Black, q.X - 5, q.Y - 5, 10, 10);
                using var font = new Font("Segoe UI", 9, FontStyle.Bold);
                var txt = $"{sel.Name}  ({sel.X:0}, {sel.Z:0})";
                gr.DrawString(txt, font, Brushes.Black, q.X + 9, q.Y - 7); gr.DrawString(txt, font, Brushes.Yellow, q.X + 8, q.Y - 8);
            }
        };
        Point drag = Point.Empty; bool panning = false;
        pic.MouseDown += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Middle) { panning = true; drag = e.Location; } };
        pic.MouseMove += (_, e) =>
        {
            if (panning) { center = new(center.X - (e.X - drag.X) / zoom, center.Y - (e.Y - drag.Y) / zoom); drag = e.Location; pic.Invalidate(); }
            var (x, z) = World(e.Location);
            f.Text = string.Format(Loc.T("world.mapCoord", "Mapa — X {0:0}  Z {1:0} · roda: zoom · arrastar: mover · botão direito leva a câmera · M ou Esc fecha"), x, z);
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
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.25f : 0.8f), 0.15f, 6f);
            // mantém o ponto embaixo do mouse no lugar
            var after = World(e.Location);
            center = new(center.X - (after.X - before.X) / _mapMPerPx, center.Y + (after.Z - before.Z) / _mapMPerPx);
            pic.Invalidate();
        };
        f.KeyDown += (_, e) => { if (e.KeyCode is Keys.M or Keys.Escape) f.Close(); };
        f.Shown += (_, _) => pic.Focus();
        f.ShowDialog(this);
        _gl.Invalidate();
    }

    sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.Selectable, true); } }
}
