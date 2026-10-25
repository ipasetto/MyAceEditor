using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Terreno do Mundo 3D. Longe: uma malha por peça de 512 m (amostra a cada 16 m) com o minimapa do jogo.
/// Perto (3×3 peças em volta da câmera): uma malha por área de 256 m com todas as amostras (2 m), as texturas do chão
/// misturadas pelas máscaras (.t2mk) e a luz do terreno (.t2lm). Tudo montado em segundo plano (WorldLoader); o que
/// está atrás da câmera não é desenhado.
/// </summary>
internal sealed class WorldTerrain : IDisposable
{
    public const float TileM = 512;
    const int FarStep = 8;     // células por amostra longe (8 × 2 m = 16 m)

    sealed record Mesh(int Vbo, int Ibo, int Count, float MinY, float MaxY);
    sealed record Grid(float[] Verts, ushort[] Idx, float MinY, float MaxY);
    sealed record GroundGpu((string Name, float U, float V, int Mask)[] Layers, int Light);
    sealed record GroundCpu(TerrainFile.GroundLayer[] Layers, Dictionary<string, (int[] Px, int W, int H)?> Textures, (int[] Px, int W, int H)? Light);

    readonly TerrainFile _t;
    readonly WorldLoader _load;
    readonly PckArchive? _surf, _texPck;
    readonly Dictionary<(int, int), Mesh?> _far = new(), _near = new();
    readonly Dictionary<(int, int), int> _minimap = new();
    readonly Dictionary<(int, int), GroundGpu?> _ground = new();
    readonly Dictionary<string, int> _groundTex = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<(int, int)> _farWait = new(), _nearWait = new(), _mapWait = new(), _groundWait = new();
    readonly HashSet<string> _decodedNames = new(StringComparer.OrdinalIgnoreCase);   // texturas do chão já enviadas ao segundo plano

    public bool Visible { get; set; } = true;
    public int Rows => (int)Math.Ceiling(_t.Height / TileM);
    public int Cols => (int)Math.Ceiling(_t.Width / TileM);

    public WorldTerrain(TerrainFile t, WorldLoader load, string? elementDir, Vector3 startGame)
    {
        _t = t; _load = load;
        try { if (elementDir is not null && File.Exists(Path.Combine(elementDir, "surfaces.pck"))) _surf = new PckArchive(Path.Combine(elementDir, "surfaces.pck")); } catch { _surf = null; }
        try { if (elementDir is not null && File.Exists(Path.Combine(elementDir, "textures.pck"))) _texPck = new PckArchive(Path.Combine(elementDir, "textures.pck")); } catch { _texPck = null; }
        // alturas de todos os pedaços em segundo plano, do mais perto para o mais longe
        int start = t.PieceAt(startGame.X, startGame.Z);
        float pm = t.CellsPerPiece * t.Cell;
        var order = Enumerable.Range(1, t.Rows * t.Cols).OrderBy(p =>
        {
            if (start == 0) return 0;
            int r = (p - 1) / t.Cols, c = (p - 1) % t.Cols, sr = (start - 1) / t.Cols, sc = (start - 1) % t.Cols;
            return (r - sr) * (r - sr) + (c - sc) * (c - sc);
        });
        foreach (var p in order) { int pp = p; _load.Work(() => { t.Prefetch(pp); return 0; }, _ => { }, urgent: pp == start); }
    }

    /// <summary>Lê um arquivo do surfaces.pck (minimapa, mapa pintado) com segurança entre threads.</summary>
    public byte[]? ReadSurface(string path)
    {
        if (_surf is null) return null;
        lock (_surf) return _surf.Find(path) is { } e ? _surf.Read(e) : null;
    }

    public (int[] Pixels, int Width, int Height)? MinimapImage(int row, int col) =>
        ReadSurface($@"surfaces\minimaps\{_t.Name}\{row:00}{col:00}.dds") is { } d ? DdsDecoder.Decode(d) : null;

    // ---------- desenho ----------
    public bool Draw(Vector3 cam, Vector3 forward)
    {
        if (!Visible) return false;
        bool busy = false;
        float tileCells = TileM / _t.Cell, areaCells = TerrainFile.AreaM / _t.Cell;
        int per = (int)(TileM / TerrainFile.AreaM);
        var (cu, cv) = _t.ToGrid(cam.X, -cam.Z);
        int ctr = (int)MathF.Floor(cv / tileCells), ctc = (int)MathF.Floor(cu / tileCells);
        if (_near.Count > 150) Evict(ctr, ctc, per);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.Color4(1f, 1f, 1f, 1f);
        for (int tr = 0; tr < Rows; tr++)
            for (int tc = 0; tc < Cols; tc++)
            {
                bool near = Math.Abs(tr - ctr) <= 1 && Math.Abs(tc - ctc) <= 1;
                // atrás da câmera (com folga do tamanho da peça): não desenha
                float cx = -_t.Width / 2 + (tc + 0.5f) * TileM, cz = _t.Height / 2 - (tr + 0.5f) * TileM;
                var toTile = new Vector3(cx - cam.X, 0, -cz - cam.Z);
                if (!near && Vector3.Dot(toTile, new Vector3(forward.X, 0, forward.Z)) < -TileM) continue;
                if (near)
                {
                    bool ready = true;
                    for (int ar = tr * per; ar < (tr + 1) * per; ar++)
                        for (int ac = tc * per; ac < (tc + 1) * per; ac++)
                        {
                            if (!_near.ContainsKey((ar, ac))) { ready = false; RequestNear(ar, ac, (int)areaCells); }
                            if (!_ground.ContainsKey((ar, ac))) { ready = false; RequestGround(ar, ac); }
                        }
                    if (ready)
                    {
                        for (int ar = tr * per; ar < (tr + 1) * per; ar++)
                            for (int ac = tc * per; ac < (tc + 1) * per; ac++)
                                if (_near[(ar, ac)] is { } m && !(_ground[(ar, ac)] is { } g && DrawGround(m, g, ar, ac))) DrawMinimap(m, tr, tc);
                        continue;
                    }
                    busy = true;
                }
                if (!_far.TryGetValue((tr, tc), out var fm)) { RequestFar(tr, tc, (int)tileCells, near); busy = true; continue; }
                if (fm is not null) DrawMinimap(fm, tr, tc);
            }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Texture2D);
        return busy;
    }

    // solta malhas, máscaras e luz das áreas a mais de 2 peças da câmera
    void Evict(int ctr, int ctc, int per)
    {
        bool Far((int, int) k) => Math.Abs(k.Item1 / per - ctr) > 2 || Math.Abs(k.Item2 / per - ctc) > 2;
        foreach (var k in _near.Keys.Where(Far).ToList())
        {
            if (_near[k] is { } m) { GL.DeleteBuffer(m.Vbo); GL.DeleteBuffer(m.Ibo); }
            _near.Remove(k);
        }
        foreach (var k in _ground.Keys.Where(Far).ToList())
        {
            if (_ground[k] is { } g) { foreach (var l in g.Layers) if (l.Mask != 0) GL.DeleteTexture(l.Mask); if (g.Light != 0) GL.DeleteTexture(g.Light); }
            _ground.Remove(k);
        }
    }

    void DrawMinimap(Mesh m, int tr, int tc)
    {
        int tex = MinimapTex(tr, tc);
        if (tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, tex); GL.Color3(1f, 1f, 1f); }
        else { GL.Disable(EnableCap.Texture2D); GL.Color3(0.45f, 0.55f, 0.35f); }
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 20, 0);
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 20, 12);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.Ibo);
        GL.DrawElements(PrimitiveType.Triangles, m.Count, DrawElementsType.UnsignedShort, 0);
    }

    // uma passada por camada (textura repetida × máscara da área) + a luz do terreno ×2
    bool DrawGround(Mesh m, GroundGpu g, int ar, int ac)
    {
        if (g.Layers.Length == 0 || _groundTex.GetValueOrDefault(g.Layers[0].Name) == 0) return false;
        float x0 = -_t.Width / 2 + ac * TerrainFile.AreaM, ztop = _t.Height / 2 - ar * TerrainFile.AreaM, inv = 1f / TerrainFile.AreaM;
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 20, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.Ibo);
        GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Enable(EnableCap.Texture2D);
        GL.Enable(EnableCap.TextureGenS); GL.Enable(EnableCap.TextureGenT);
        GL.TexGen(TextureCoordName.S, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
        GL.TexGen(TextureCoordName.T, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, 0x8570);   // COMBINE: cor = textura, alfa = 1
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8571, 0x2100);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8580, 0x1702);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8581, 0x8577);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8572, 0x1E01);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8588, 0x8577);
        GL.Color4(1f, 1f, 1f, 1f);
        for (int i = 0; i < g.Layers.Length; i++)
        {
            var l = g.Layers[i];
            int ltex = _groundTex.GetValueOrDefault(l.Name);
            if (ltex == 0 || i > 0 && l.Mask == 0) continue;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, ltex);
            GL.TexGen(TextureCoordName.S, TextureGenParameter.ObjectPlane, new[] { l.U, 0, 0, 0 });
            GL.TexGen(TextureCoordName.T, TextureGenParameter.ObjectPlane, new[] { 0, 0, l.V, 0 });
            bool masked = i > 0;
            if (masked)
            {
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, l.Mask);
                GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
                GL.Enable(EnableCap.TextureGenS); GL.Enable(EnableCap.TextureGenT);
                GL.TexGen(TextureCoordName.S, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
                GL.TexGen(TextureCoordName.T, TextureGenParameter.TextureGenMode, (int)TextureGenMode.ObjectLinear);
                GL.TexGen(TextureCoordName.S, TextureGenParameter.ObjectPlane, new[] { inv, 0, 0, -x0 * inv });
                GL.TexGen(TextureCoordName.T, TextureGenParameter.ObjectPlane, new[] { 0, 0, inv, ztop * inv });
                GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthFunc(DepthFunction.Lequal); GL.DepthMask(false);
            }
            GL.DrawElements(PrimitiveType.Triangles, m.Count, DrawElementsType.UnsignedShort, 0);
            if (masked)
            {
                GL.Disable(EnableCap.TextureGenS); GL.Disable(EnableCap.TextureGenT); GL.Disable(EnableCap.Texture2D);
                GL.Disable(EnableCap.Blend); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
            }
        }
        GL.ActiveTexture(TextureUnit.Texture0);
        if (g.Light != 0)
        {
            GL.BindTexture(TextureTarget.Texture2D, g.Light);
            GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Replace);
            GL.TexGen(TextureCoordName.S, TextureGenParameter.ObjectPlane, new[] { inv, 0, 0, -x0 * inv });
            GL.TexGen(TextureCoordName.T, TextureGenParameter.ObjectPlane, new[] { 0, 0, inv, ztop * inv });
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.SrcColor);
            GL.DepthFunc(DepthFunction.Lequal); GL.DepthMask(false);
            GL.DrawElements(PrimitiveType.Triangles, m.Count, DrawElementsType.UnsignedShort, 0);
            GL.Disable(EnableCap.Blend); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
        }
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.Disable(EnableCap.TextureGenS); GL.Disable(EnableCap.TextureGenT);
        GL.EnableClientState(ArrayCap.TextureCoordArray);
        return true;
    }

    // ---------- pedidos ao segundo plano ----------
    void RequestFar(int tr, int tc, int tileCells, bool urgent)
    {
        if (!_farWait.Add((tr, tc))) return;
        _load.Work(() => BuildGrid(tr * tileCells, tc * tileCells, tileCells, FarStep, tr, tc), g => { _farWait.Remove((tr, tc)); _far[(tr, tc)] = Upload(g); }, urgent);
    }

    void RequestNear(int ar, int ac, int areaCells)
    {
        if (!_nearWait.Add((ar, ac))) return;
        int per = (int)(TileM / TerrainFile.AreaM);
        _load.Work(() => BuildGrid(ar * areaCells, ac * areaCells, areaCells, 1, ar / per, ac / per), g => { _nearWait.Remove((ar, ac)); _near[(ar, ac)] = Upload(g); }, true);
    }

    void RequestGround(int ar, int ac)
    {
        if (_texPck is null) { _ground[(ar, ac)] = null; return; }
        if (!_groundWait.Add((ar, ac))) return;
        _load.Work(() =>
        {
            TerrainFile.GroundLayer[]? layers;
            try { layers = _t.Ground(ar, ac); } catch { layers = null; }
            if (layers is null) return null;
            var tex = new Dictionary<string, (int[], int, int)?>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in layers)
            {
                bool need;
                lock (_decodedNames) need = _decodedNames.Add(l.Texture);
                if (!need) continue;
                byte[]? d; lock (_texPck) d = _texPck.Find(l.Texture.ToLowerInvariant()) is { } e ? _texPck.Read(e) : null;
                try { tex[l.Texture] = d is null ? null : DdsDecoder.Decode(d); } catch { tex[l.Texture] = null; }
            }
            (int[], int, int)? light;
            try { light = _t.LightmapDds(ar, ac) is { } ld ? DdsDecoder.Decode(ld) : null; } catch { light = null; }
            return new GroundCpu(layers, tex, light);
        }, cpu =>
        {
            _groundWait.Remove((ar, ac));
            if (cpu is null) { _ground[(ar, ac)] = null; return; }
            foreach (var (name, img) in cpu.Textures) _groundTex[name] = img is { } i ? UploadTexture(i.Px, i.W, i.H, true, true) : 0;
            var layers = cpu.Layers.Select((l, i) => (l.Texture, l.UScale, l.VScale,
                i > 0 && l.Mask is not null ? UploadMask(l.Mask, l.MaskSide) : 0)).ToArray();
            int light = cpu.Light is { } li ? UploadTexture(li.Px, li.W, li.H, false, false) : 0;
            _ground[(ar, ac)] = new GroundGpu(layers, light);
        }, true);
    }

    int MinimapTex(int row, int col)
    {
        if (_minimap.TryGetValue((row, col), out var id)) return id;
        if (_mapWait.Add((row, col)))
            _load.Work(() => MinimapImage(row, col), img => { _mapWait.Remove((row, col)); _minimap[(row, col)] = img is { } i ? UploadTexture(i.Pixels, i.Width, i.Height, false, false) : 0; });
        return 0;
    }

    // grade (n/step + 1)² com índices u16; coordenada de textura dentro da peça de 512 m (tr, tc)
    Grid? BuildGrid(int v0, int u0, int n, int step, int tr, int tc)
    {
        int side = n / step + 1;
        float tileCells = TileM / _t.Cell, tu0 = tc * tileCells, tv0 = tr * tileCells;
        var verts = new float[side * side * 5];
        float minY = float.MaxValue, maxY = float.MinValue; bool any = false;
        for (int r = 0; r < side; r++)
            for (int c = 0; c < side; c++)
            {
                int u = u0 + c * step, v = v0 + r * step, k = (r * side + c) * 5;
                float? hs = _t.Sample(u, v);
                float h = hs ?? 0;
                if (hs is not null) { any = true; minY = Math.Min(minY, h); maxY = Math.Max(maxY, h); }
                var (x, z) = _t.FromGrid(u, v);
                verts[k] = x; verts[k + 1] = h; verts[k + 2] = -z; verts[k + 3] = (u - tu0) / tileCells; verts[k + 4] = (v - tv0) / tileCells;
            }
        if (!any) return null;
        var idx = new ushort[(side - 1) * (side - 1) * 6];
        int p = 0;
        for (int r = 0; r < side - 1; r++)
            for (int c = 0; c < side - 1; c++)
            {
                ushort a = (ushort)(r * side + c), b = (ushort)(a + 1), d = (ushort)(a + side), e = (ushort)(d + 1);
                idx[p++] = a; idx[p++] = d; idx[p++] = b; idx[p++] = b; idx[p++] = d; idx[p++] = e;
            }
        return new Grid(verts, idx, minY, maxY);
    }

    static Mesh? Upload(Grid? g)
    {
        if (g is null) return null;
        int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, g.Verts.Length * 4, g.Verts, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, g.Idx.Length * 2, g.Idx, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        return new Mesh(vbo, ibo, g.Idx.Length, g.MinY, g.MaxY);
    }

    static int UploadTexture(int[] px, int w, int h, bool repeat, bool mip)
    {
        int id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, w, h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, px);
        if (mip) GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)(mip ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        var wrap = (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        return id;
    }

    static int UploadMask(byte[] mask, int side)
    {
        int id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Alpha8, side, side, 0, PixelFormat.Alpha, PixelType.UnsignedByte, mask);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return id;
    }

    public void Dispose()
    {
        foreach (var m in _far.Values.Concat(_near.Values)) if (m is not null) { GL.DeleteBuffer(m.Vbo); GL.DeleteBuffer(m.Ibo); }
        foreach (var t in _minimap.Values.Concat(_groundTex.Values)) if (t != 0) GL.DeleteTexture(t);
        foreach (var g in _ground.Values) if (g is not null) { foreach (var l in g.Layers) if (l.Mask != 0) GL.DeleteTexture(l.Mask); if (g.Light != 0) GL.DeleteTexture(g.Light); }
        _surf?.Dispose(); _texPck?.Dispose();
    }
}
