using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Grama do Mundo 3D como a do jogo (CELGrassBlock::BuildGrasses): cada bit ligado da área de grama (.ecbsd + bsdata) é
/// uma cópia da malha .mox do tipo (grasses.pck), num ponto sorteado dentro da célula, girada e com tamanho ± variação,
/// assentada no chão. Gerada em pedaços de 32 × 32 células só perto da câmera (até a distância de visão do tipo),
/// poucos por quadro; pedaços sem uso há um tempo saem da memória.
/// </summary>
internal sealed class WorldGrass : IDisposable
{
    const int Patch = 32;
    const float MaxRange = 140;
    readonly WorldScene? _scene;
    readonly PckArchive? _pck;
    readonly TerrainFile _t;
    readonly Dictionary<int, Kind?> _kinds = new();
    readonly Dictionary<(int A, int X, int Y), Piece> _pieces = new();
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    sealed class Kind { public int Tex; public Vector3[] Pos = []; public Vector2[] Uv = []; public ushort[] Index = []; public float Size, Var, Range, AlphaRef; }
    sealed class Piece { public int Vbo, Ibo, Count; public int Tex; public float AlphaRef; public double Used; }

    public bool Pending { get; private set; }
    public bool Visible { get; set; } = true;

    public WorldGrass(string? elementDir, TerrainFile t)
    {
        _t = t;
        try
        {
            if (elementDir is null) return;
            var dir = Path.Combine(elementDir, "maps", t.Name);
            var ecwld = Path.Combine(dir, t.Name + ".ecwld");
            var pck = Path.Combine(elementDir, "grasses.pck");
            if (!File.Exists(ecwld) || !File.Exists(pck) || Directory.EnumerateFiles(dir, "*.ecbsd").FirstOrDefault() is not { } bsd) return;
            _scene = new WorldScene(ecwld);
            _scene.LoadGrass(bsd);
            _pck = new PckArchive(pck);
        }
        catch (Exception ex) { AppLog.Info($"grama de {t.Name}: {ex.Message}"); _scene = null; _pck?.Dispose(); _pck = null; }
    }

    Kind? KindOf(int type)
    {
        if (_kinds.TryGetValue(type, out var k)) return k;
        _kinds[type] = null;
        if (_scene is null || _pck is null || type < 0 || type >= _scene.GrassTypes.Count) return null;
        var gt = _scene.GrassTypes[type];
        var path = gt.Mox.Replace('/', '\\').ToLowerInvariant();
        var e = _pck.Find(path) ?? _pck.Find(@"grasses\badgrass.mox");   // o jogo usa a "grama ruim" quando falta o arquivo
        if (e is null || MoxModel.Read(_pck.Read(e)) is not { Meshes.Count: > 0 } mox) return null;
        var mesh = mox.Meshes[0];
        var texPath = Path.Combine(Path.GetDirectoryName(e.Path) ?? "", "textures", mesh.Texture).ToLowerInvariant();
        k = new Kind
        {
            Tex = Texture(texPath), Pos = mesh.Pos, Uv = mesh.Uv, Index = mesh.Index,
            Size = gt.Size > 0 ? gt.Size : 1, Var = Math.Clamp(gt.SizeVar, 0, 1),
            Range = Math.Clamp(gt.Sight > 0 ? gt.Sight : 60, 15, MaxRange),
            AlphaRef = gt.AlphaBlend ? 0.33f : Math.Clamp(gt.AlphaRef / 255f, 0.2f, 0.9f),   // sem mistura: recorte firme
        };
        _kinds[type] = k;
        return k;
    }

    int Texture(string path)
    {
        // recorte por alfa como as folhas das árvores: com mipmap comum o fundo branco da imagem vira triângulo branco
        if (_pck?.Find(path) is not { } te || DdsDecoder.DecodeMax(_pck.Read(te), 256) is not { } img) return 0;
        return WorldTrees.Upload(img.Item1, img.Item2, img.Item3, true);
    }

    // canto da área (coluna 0, linha 0): centro − meia largura em X, + meia altura em Z (as linhas andam para −Z)
    static (float X, float Z) Origin(WorldScene.GrassArea g) => (g.X - g.Cols * g.Cell / 2, g.Z + g.Rows * g.Cell / 2);

    Piece? Build(int a, int bx, int by, Kind k)
    {
        var g = _scene!.Grass[a];
        var (ox, oz) = Origin(g);
        var rnd = new Random(HashCode.Combine(a, bx, by));
        var verts = new List<float>(); var idx = new List<uint>();
        for (int row = by * Patch; row < Math.Min(g.Rows, by * Patch + Patch); row++)
            for (int col = bx * Patch; col < Math.Min(g.Cols, bx * Patch + Patch); col++)
            {
                float rx = (float)rnd.NextDouble() - 0.5f, rz = (float)rnd.NextDouble() - 0.5f;
                float rot = (float)rnd.NextDouble() * MathF.Tau, sc = k.Size * (1 - k.Var + 2 * k.Var * (float)rnd.NextDouble());
                if (!g.On(col, row)) continue;
                float x = ox + (col + rx) * g.Cell, z = oz - (row + rz) * g.Cell;
                if (_t.HeightAt(x, z) is not float y) continue;
                y -= 0.02f;
                uint b0 = (uint)(verts.Count / 5);
                float c = MathF.Cos(rot), s = MathF.Sin(rot);
                for (int i = 0; i < k.Pos.Length; i++)
                {
                    var q = k.Pos[i] * sc;
                    float gx = x + q.X * c + q.Z * s, gz = z - q.X * s + q.Z * c;
                    verts.Add(gx); verts.Add(y + q.Y); verts.Add(-gz); verts.Add(k.Uv[i].X); verts.Add(k.Uv[i].Y);
                }
                foreach (var ix in k.Index) idx.Add(b0 + ix);
            }
        var piece = new Piece { Count = idx.Count, Tex = k.Tex, AlphaRef = k.AlphaRef, Used = _clock.Elapsed.TotalSeconds };
        if (idx.Count == 0) return piece;
        piece.Vbo = GL.GenBuffer(); piece.Ibo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, piece.Vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * 4, verts.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, piece.Ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Count * 4, idx.ToArray(), BufferUsageHint.StaticDraw);
        return piece;
    }

    /// <summary>Desenha a grama em volta da câmera (coordenadas OpenGL). Gera no máximo alguns pedaços por quadro.</summary>
    public void Draw(Vector3 camGl)
    {
        Pending = false;
        if (!Visible || _scene is null || _scene.Grass.Count == 0) return;
        float cx = camGl.X, cz = -camGl.Z;
        double now = _clock.Elapsed.TotalSeconds;
        int built = 0;
        GL.Enable(EnableCap.Texture2D); GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.Lighting);
        GL.Enable(EnableCap.AlphaTest);
        GL.Color3(0.82f, 0.84f, 0.8f);
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        for (int a = 0; a < _scene.Grass.Count; a++)
        {
            var g = _scene.Grass[a];
            float half = MathF.Max(g.Cols, g.Rows) * g.Cell / 2;
            if (MathF.Abs(g.X - cx) > half + MaxRange || MathF.Abs(g.Z - cz) > half + MaxRange) continue;
            if (KindOf(g.Type) is not { } k) continue;
            var (ox, oz) = Origin(g);
            float ps = Patch * g.Cell;
            int bx0 = Math.Max(0, (int)((cx - k.Range - ox) / ps)), bx1 = Math.Min((g.Cols - 1) / Patch, (int)((cx + k.Range - ox) / ps));
            int by0 = Math.Max(0, (int)((oz - (cz + k.Range)) / ps)), by1 = Math.Min((g.Rows - 1) / Patch, (int)((oz - (cz - k.Range)) / ps));
            for (int by = by0; by <= by1; by++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    float px = ox + (bx + 0.5f) * ps - cx, pz = oz - (by + 0.5f) * ps - cz;
                    if (px * px + pz * pz > (k.Range + ps) * (k.Range + ps)) continue;
                    if (!_pieces.TryGetValue((a, bx, by), out var piece))
                    {
                        if (built >= 6) { Pending = true; continue; }
                        built++;
                        if (Build(a, bx, by, k) is not { } np) continue;
                        _pieces[(a, bx, by)] = piece = np;
                    }
                    piece.Used = now;
                    if (piece.Count == 0) continue;
                    GL.AlphaFunc(AlphaFunction.Greater, piece.AlphaRef);
                    GL.BindTexture(TextureTarget.Texture2D, piece.Tex);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, piece.Vbo);
                    GL.VertexPointer(3, VertexPointerType.Float, 20, 0);
                    GL.TexCoordPointer(2, TexCoordPointerType.Float, 20, 12);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, piece.Ibo);
                    GL.DrawElements(PrimitiveType.Triangles, piece.Count, DrawElementsType.UnsignedInt, 0);
                }
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.AlphaTest); GL.Disable(EnableCap.Texture2D);
        GL.Color3(1f, 1f, 1f);
        // pedaços longe há mais de 20 s: libera
        foreach (var key in _pieces.Where(p => now - p.Value.Used > 20).Select(p => p.Key).ToList())
        {
            var p = _pieces[key];
            if (p.Vbo != 0) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); }
            _pieces.Remove(key);
        }
    }

    public void Dispose()
    {
        foreach (var p in _pieces.Values) if (p.Vbo != 0) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); }
        foreach (var k in _kinds.Values) if (k is { Tex: > 0 }) GL.DeleteTexture(k.Tex);
        _pieces.Clear(); _pck?.Dispose();
    }
}
