using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Árvores do cenário no Mundo 3D: posições do maps\&lt;mapa&gt;\&lt;mapa&gt;.ecwld (WorldScene) e tronco/galhos gerados pela
/// nossa versão do SpeedTree (SptGrower) a partir do .spt do trees.pck, com a casca do próprio jogo. Cada tipo é gerado
/// uma vez, na primeira vez que aparece perto da câmera (no máximo 2 por quadro); desenha até 900 m.
/// </summary>
internal sealed class WorldTrees : IDisposable
{
    sealed record Part(int Vbo, int Ibo, int Count, int Tex);
    sealed record Kind(Part? Wood, Part? Fronds, Part? Leaves, LeafSet? Set, float Radius);

    /// <summary>Folhas de um tipo: centro + canto de cada cartão; os vértices são refeitos quando a câmera gira (viram para ela).</summary>
    internal sealed class LeafSet
    {
        public float[] Center = [], Corner = [], Rest = [];   // por folha xyz; por vértice ox,oy; por vértice cor,s,t
        public float[] Buf = [];
        public Vector3 R, U;

        public void Fill(Vector3 right, Vector3 up)
        {
            int nv = Corner.Length / 2;
            if (Buf.Length != nv * 6) Buf = new float[nv * 6];
            for (int i = 0; i < nv; i++)
            {
                int c = i / 4 * 3;
                var p = new Vector3(Center[c], Center[c + 1], Center[c + 2]) + right * Corner[i * 2] + up * Corner[i * 2 + 1];
                int o = i * 6;
                Buf[o] = p.X; Buf[o + 1] = p.Y; Buf[o + 2] = p.Z;
                Buf[o + 3] = Rest[i * 3]; Buf[o + 4] = Rest[i * 3 + 1]; Buf[o + 5] = Rest[i * 3 + 2];
            }
            R = right; U = up;
        }
    }

    readonly PckArchive? _pck;
    readonly WorldScene? _scene;
    readonly Dictionary<int, Kind?> _kinds = new();
    readonly Dictionary<(int, int), List<int>> _cells = new();   // célula de 256 m → índices em _scene.Trees
    const float Cell = 256, Range = 900;
    /// <summary>Direção PARA o sol (OpenGL); o Mundo 3D põe a da área padrão do mapa (.ecbsd) antes de gerar as árvores.</summary>
    internal static Vector3 Sun = new Vector3(-0.4f, 1f, 0.3f).Normalized();

    public int Count => _scene?.Trees.Count ?? 0;

    public WorldTrees(string? elementDir, string map)
    {
        try
        {
            if (elementDir is null) return;
            var ecwld = Path.Combine(elementDir, "maps", map, map + ".ecwld");
            var pck = Path.Combine(elementDir, "trees.pck");
            if (!File.Exists(ecwld) || !File.Exists(pck)) return;
            _scene = new WorldScene(ecwld);
            _pck = new PckArchive(pck);
        }
        catch (Exception ex) { AppLog.Info($"árvores de {map}: {ex.Message}"); _scene = null; _pck?.Dispose(); _pck = null; return; }
        for (int i = 0; i < _scene.Trees.Count; i++)
        {
            var t = _scene.Trees[i];
            var key = ((int)MathF.Floor(t.X / Cell), (int)MathF.Floor(t.Z / Cell));
            if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new();
            l.Add(i);
        }
    }

    /// <summary>Tronco e galhos do tipo, prontos no OpenGL (null = tipo sem .spt legível).</summary>
    Kind? Build(int type)
    {
        if (_pck is null || _scene is null || type < 0 || type >= _scene.TreeTypes.Count) return null;
        try
        {
            var spt = _scene.TreeTypes[type].Spt.Replace('/', '\\').ToLowerInvariant();
            var e = _pck.Find(spt) ?? _pck.Find(@"trees\badtree.spt");   // o cliente usa a "árvore ruim" quando falta o arquivo
            if (e is null) return null;
            var f = new SptFile(_pck.Read(e));
            var m = SptGrower.Build(f);
            if (m.Index.Count == 0 && m.Leaves.Count == 0 && m.FrondIndex.Count == 0) return null;
            var v = new float[m.Pos.Count * 6];
            float radius = 1;
            for (int i = 0; i < m.Pos.Count; i++)
            {
                var p = m.Pos[i];
                radius = MathF.Max(radius, p.Length);
                v[i * 6] = p.X; v[i * 6 + 1] = p.Y; v[i * 6 + 2] = p.Z;
                v[i * 6 + 3] = Lit(Vector3.Dot(m.Normal[i], Sun));
                v[i * 6 + 4] = m.Uv[i].X; v[i * 6 + 5] = m.Uv[i].Y;
            }
            // casca: mesma pasta do .spt, extensão .dds (como CELTree::Init)
            var bark = Path.Combine(Path.GetDirectoryName(e.Path) ?? "", Path.ChangeExtension(f.BarkTexture, ".dds")).ToLowerInvariant();
            var wood = m.Index.Count == 0 ? null : Upload(v, m.Index.ToArray(), Texture(bark, 256));
            // mapa composto do tipo (.ecwld): folhas e frondes
            int comp = m.Leaves.Count > 0 || m.FrondIndex.Count > 0
                ? Texture(_scene.TreeTypes[type].CompositeMap.Replace('/', '\\').ToLowerInvariant(), 512, true) : 0;
            // frondes: fitas fixas com o recorte da fronde, dos dois lados
            Part? fronds = null;
            if (m.FrondIndex.Count > 0)
            {
                var fv = new float[m.FrondPos.Count * 6];
                // normal de cada vértice pelos triângulos da fronde; dois lados = |N·L| (a fronde é vista dos dois lados)
                var fn = new Vector3[m.FrondPos.Count];
                for (int t = 0; t + 2 < m.FrondIndex.Count; t += 3)
                {
                    int a = m.FrondIndex[t], b = m.FrondIndex[t + 1], c = m.FrondIndex[t + 2];
                    var n = Vector3.Cross(m.FrondPos[b] - m.FrondPos[a], m.FrondPos[c] - m.FrondPos[a]);
                    fn[a] += n; fn[b] += n; fn[c] += n;
                }
                for (int i = 0; i < m.FrondPos.Count; i++)
                {
                    var q = m.FrondPos[i]; radius = MathF.Max(radius, q.Length);
                    float fcol = Lit(fn[i].LengthSquared > 1e-12f ? MathF.Abs(Vector3.Dot(fn[i].Normalized(), Sun)) : 0.5f);
                    fv[i * 6] = q.X; fv[i * 6 + 1] = q.Y; fv[i * 6 + 2] = q.Z; fv[i * 6 + 3] = fcol; fv[i * 6 + 4] = m.FrondUv[i].X; fv[i * 6 + 5] = m.FrondUv[i].Y;
                }
                fronds = Upload(fv, m.FrondIndex.ToArray(), comp);
            }
            // folhas: um cartão por folha virado para a câmera (como o runtime)
            Part? leaves = null; LeafSet? set = null;
            if (m.Leaves.Count > 0 && f.LeafMaps.Count > 0)
            {
                (set, var li) = Leaves(f, m, ref radius);
                set.Fill(Vector3.UnitX, Vector3.UnitY);
                leaves = Upload(set.Buf, li, comp, BufferUsageHint.DynamicDraw);
            }
            return new Kind(wood, fronds, leaves, set, radius);
        }
        catch (Exception ex) { AppLog.Info($"árvore {type}: {ex.Message}"); return null; }
    }

    /// <summary>Textura do trees.pck. Recorte (folhas): mipmaps próprios com alfa 0/255 e cor só dos pixels opacos — a média
    /// comum mistura o fundo transparente e as folhas viram quadrados claros de longe.</summary>
    /// <summary>
    /// Cor do vértice pelo material do .spt (seção 8000: difusa e ambiente por canal) com luz ambiente 0,8 e do sol 0,5 × N·L;
    /// sem material = cinza neutro. Metade do valor: o desenho dobra (RGB_SCALE 2).
    /// </summary>
    // Luz das árvores como CELForest::Render (EL_Forest.cpp, source do cliente): material BRANCO (o material do .spt não
    // entra no desenho com LIGHT_DYNAMIC), ambiente × 0,5 + sol × 0,5 × N·L, textura × 2 (MODULATE2X = nosso RGB_SCALE 2).
    // Ambiente e sol = luz fixa de dia do Mundo 3D.
    const float SceneAmbient = 0.55f, SceneSun = 0.8f;
    static float Lit(float nl)
    {
        byte v = (byte)Math.Clamp((SceneAmbient * 0.5f + SceneSun * 0.5f * MathF.Max(0, nl)) * 255, 0, 255);
        return BitConverter.Int32BitsToSingle(unchecked((int)(0xFF000000u | (uint)(v << 16) | (uint)(v << 8) | v)));
    }

    int Texture(string path, int max, bool cutout = false)
    {
        if (_pck?.Find(path) is not { } te || DdsDecoder.DecodeMax(_pck.Read(te), max) is not { } img) return 0;
        return Upload(img.Item1, img.Item2, img.Item3, cutout);
    }

    /// <summary>
    /// Sobe a imagem ARGB ao OpenGL. cutout = recorte por alfa: os níveis menores (mipmap) só misturam a cor dos pixels
    /// opacos e o alfa vira 0 ou 1 — senão a cor do fundo transparente (branca, preta) aparece ao longe.
    /// </summary>
    internal static int Upload(int[] px, int w, int h, bool cutout)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, w, h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, px);
        if (!cutout) GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        else
            for (int level = 1; w > 1 || h > 1; level++)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                var o = new int[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        int r = 0, g = 0, b = 0, n = 0, a = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int sx = Math.Min(w - 1, x * 2 + (k & 1)), sy = Math.Min(h - 1, y * 2 + (k >> 1));
                            int c = px[sy * w + sx]; int ca = (c >> 24) & 255;
                            a += ca;
                            if (ca > 127) { r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; n++; }
                        }
                        o[y * nw + x] = n == 0 ? 0 : (a / 4 >= 100 ? unchecked((int)0xFF000000) : 0) | (r / n << 16) | (g / n << 8) | b / n;
                    }
                px = o; w = nw; h = nh;
                GL.TexImage2D(TextureTarget.Texture2D, level, PixelInternalFormat.Rgba, w, h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, px);
            }
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return tex;
    }

    static Part Upload(float[] v, int[] idx, int tex, BufferUsageHint hint = BufferUsageHint.StaticDraw)
    {
        int vbo = GL.GenBuffer(), ibo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, v.Length * 4, v, hint);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        return new Part(vbo, ibo, idx.Length, tex);
    }

    /// <summary>
    /// Folhas: cada folha sorteia uma textura de folha do .spt; o cartão tem o tamanho dela (unidades da árvore, já em metros
    /// pela escala da árvore), preso pela âncora, com os 4 cantos (s,t) do mapa composto e um giro pequeno no plano.
    /// </summary>
    internal static (LeafSet Set, int[] Index) Leaves(SptFile f, SptMesh m, ref float radius)
    {
        int n = m.Leaves.Count;
        var set = new LeafSet { Center = new float[n * 3], Corner = new float[n * 8], Rest = new float[n * 12] };
        var idx = new int[n * 6];
        var rng = new SptRandom(f.Seed + 7);
        // normal da folha: do centro da copa para fora (o runtime acende cada folha pela sua normal: lado do sol claro,
        // lado de trás só com o ambiente)
        var crown = Vector3.Zero;
        foreach (var lf in m.Leaves) crown += lf.Pos;
        crown /= Math.Max(1, n);
        for (int l = 0; l < n; l++)
        {
            var pos = m.Leaves[l].Pos;
            var map = f.LeafMaps[l < m.LeafMap.Count ? m.LeafMap[l] : Math.Min((int)(rng.Uniform() * f.LeafMaps.Count), f.LeafMaps.Count - 1)];
            float w = map.Size.X * m.Scale, h = map.Size.Y * m.Scale;
            float s0 = MathF.Min(MathF.Min(map.Uv[0], map.Uv[2]), MathF.Min(map.Uv[4], map.Uv[6])), s1 = MathF.Max(MathF.Max(map.Uv[0], map.Uv[2]), MathF.Max(map.Uv[4], map.Uv[6]));
            float t0 = MathF.Min(MathF.Min(map.Uv[1], map.Uv[3]), MathF.Min(map.Uv[5], map.Uv[7])), t1 = MathF.Max(MathF.Max(map.Uv[1], map.Uv[3]), MathF.Max(map.Uv[5], map.Uv[7]));
            float spin = MathHelper.DegreesToRadians(rng.Range(-15, 15)), cs = MathF.Cos(spin), sn = MathF.Sin(spin);
            var ln = m.Leaves[l].Pos - crown;
            float col = Lit(ln.LengthSquared > 1e-8f ? Vector3.Dot(ln.Normalized(), Sun) : 0.5f);
            set.Center[l * 3] = pos.X; set.Center[l * 3 + 1] = pos.Y; set.Center[l * 3 + 2] = pos.Z;
            radius = MathF.Max(radius, pos.Length + MathF.Max(w, h));
            for (int k = 0; k < 4; k++)
            {
                float su = map.Uv[k * 2], tv = map.Uv[k * 2 + 1];
                float ax = s1 > s0 ? (su - s0) / (s1 - s0) : 0, by = t1 > t0 ? (tv - t0) / (t1 - t0) : 0;   // by = 0 no alto da imagem
                float ox = (ax - map.Origin.X) * w, oy = (map.Origin.Y - by) * h;
                int v = l * 4 + k;
                set.Corner[v * 2] = ox * cs - oy * sn; set.Corner[v * 2 + 1] = ox * sn + oy * cs;
                set.Rest[v * 3] = col; set.Rest[v * 3 + 1] = su; set.Rest[v * 3 + 2] = tv;
            }
            int b = l * 4;
            idx[l * 6] = b; idx[l * 6 + 1] = b + 1; idx[l * 6 + 2] = b + 2;
            idx[l * 6 + 3] = b; idx[l * 6 + 4] = b + 2; idx[l * 6 + 5] = b + 3;
        }
        return (set, idx);
    }

    /// <summary>Ainda há tipos de árvore perto da câmera esperando para serem gerados (foto de diagnóstico espera).</summary>
    public bool Pending { get; private set; }

    /// <summary>Desenha as árvores perto e na frente da câmera (cam e forward no espaço do OpenGL: z = −z do jogo).</summary>
    public void Draw(Vector3 cam, Vector3 forward)
    {
        if (_scene is null) return;
        int built = 0; Pending = false;
        // folhas viram para a câmera: base da tela (direita, cima) no espaço do OpenGL
        var camRight = Vector3.Cross(forward, Vector3.UnitY);
        camRight = camRight.LengthSquared > 1e-6f ? camRight.Normalized() : Vector3.UnitX;
        var camUp = Vector3.Cross(camRight, forward).Normalized();
        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, 0x8570);   // COMBINE: textura × cor × 2
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8571, 0x2100);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8580, 0x1702);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8581, 0x8577);
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 2f);
        float gx = cam.X, gz = -cam.Z;
        int c0 = (int)MathF.Floor((gx - Range) / Cell), c1 = (int)MathF.Floor((gx + Range) / Cell);
        int r0 = (int)MathF.Floor((gz - Range) / Cell), r1 = (int)MathF.Floor((gz + Range) / Cell);
        for (int cx = c0; cx <= c1; cx++)
            for (int cz = r0; cz <= r1; cz++)
            {
                if (!_cells.TryGetValue((cx, cz), out var list)) continue;
                foreach (int i in list)
                {
                    var t = _scene.Trees[i];
                    var at = new Vector3(t.X, t.Y, -t.Z);
                    var d = at - cam;
                    float dist = d.Length;
                    if (dist > Range || Vector3.Dot(d, forward) < -40) continue;
                    if (!_kinds.TryGetValue(t.Type, out var k))
                    {
                        if (built >= 2) { Pending = true; continue; }   // gera aos poucos para não travar a câmera
                        _kinds[t.Type] = k = Build(t.Type); built++;
                        GL.EnableClientState(ArrayCap.VertexArray); GL.EnableClientState(ArrayCap.ColorArray); GL.EnableClientState(ArrayCap.TextureCoordArray);
                    }
                    if (k is null || k.Radius / Math.Max(dist, 1) < 0.003f) continue;
                    GL.PushMatrix();
                    GL.Translate(at);
                    DrawPart(k.Wood, false);
                    DrawPart(k.Fronds, true);
                    if (k.Set is { } ls && k.Leaves is { } lp && (Vector3.Dot(ls.R, camRight) < 0.9995f || Vector3.Dot(ls.U, camUp) < 0.9995f))
                    {
                        ls.Fill(camRight, camUp);   // uma vez por tipo quando a câmera gira (todas as árvores do tipo usam)
                        GL.BindBuffer(BufferTarget.ArrayBuffer, lp.Vbo);
                        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, ls.Buf.Length * 4, ls.Buf);
                    }
                    DrawPart(k.Leaves, true);
                    GL.PopMatrix();
                }
            }
        GL.TexEnv(TextureEnvTarget.TextureEnv, (TextureEnvParameter)0x8573, 1f);
        GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Modulate);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        GL.DisableClientState(ArrayCap.VertexArray); GL.DisableClientState(ArrayCap.ColorArray); GL.DisableClientState(ArrayCap.TextureCoordArray);
        GL.Disable(EnableCap.Texture2D);
    }

    static void DrawPart(Part? p, bool cutout)
    {
        if (p is null) return;
        GL.BindBuffer(BufferTarget.ArrayBuffer, p.Vbo);
        GL.VertexPointer(3, VertexPointerType.Float, 24, 0);
        GL.ColorPointer(0x80E1, ColorPointerType.UnsignedByte, 24, 12);   // GL_BGRA
        GL.TexCoordPointer(2, TexCoordPointerType.Float, 24, 16);
        if (p.Tex != 0) { GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, p.Tex); } else GL.Disable(EnableCap.Texture2D);
        if (cutout) { GL.Enable(EnableCap.AlphaTest); GL.AlphaFunc(AlphaFunction.Greater, 0.33f); }
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, p.Ibo);
        GL.DrawElements(PrimitiveType.Triangles, p.Count, DrawElementsType.UnsignedInt, 0);
        if (cutout) GL.Disable(EnableCap.AlphaTest);
    }

    public void Dispose()
    {
        foreach (var k in _kinds.Values)
        {
            foreach (var p in new[] { k?.Wood, k?.Fronds, k?.Leaves })
                if (p is not null) { GL.DeleteBuffer(p.Vbo); GL.DeleteBuffer(p.Ibo); }
            if (k?.Wood is { Tex: not 0 } w) GL.DeleteTexture(w.Tex);
            var comp = k?.Leaves?.Tex ?? k?.Fronds?.Tex ?? 0;   // folhas e frondes dividem o mapa composto
            if (comp != 0) GL.DeleteTexture(comp);
        }
        _kinds.Clear();
        _pck?.Dispose();
    }
}
