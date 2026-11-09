using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Construção do cenário (litmodels.pck: litmodels\&lt;mapa&gt;\&lt;pedaço&gt;\litmodel_N.bmd), já iluminada (cor nos vértices).
/// "MOXB" | u32 versão | escala 3f | direção 3f | cima 3f | posição no mundo 3f | u32 nMalhas |
/// malha: u32 versão, nome[64], textura[256], nVértices, nTriângulos, vértices de 24 B (pos local 3f, cor BGRA, uv 2f),
/// índices u16 ×3 por triângulo, normais 3f, cores de dia e de noite (BGRA, 1 por vértice) e dados extras de tamanho
/// variável — a próxima malha é achada pela versão. Desenho usa a cor de dia (a do vértice pode ser outra, ex.: a19).
/// Versão 0x80000001 (metade dos arquivos do 1.4.4): 1 byte + 0x10000002 depois da versão, resto 5 bytes adiante, malhas 0x10000005.
/// Esse byte é bCollideOnly (CELBuilding::Load): 1 = peça só de colisão, o jogo não desenha (SetVisibility(false)).
/// </summary>
public sealed class BmdModel
{
    public sealed record Mesh(string Texture, byte[] Vertices, ushort[] Indices)   // vértice 24 B como no arquivo
    {
        /// <summary>Cor de NOITE de cada vértice (BGRA, 4 B; alfa do vértice), ou null (A3DLitMesh m_pNightColors).</summary>
        public byte[]? Night { get; init; }
    }

    public float Scale;
    public float[] Dir = new float[3], Up = new float[3], Pos = new float[3];
    public List<Mesh> Meshes { get; } = new();
    public int Declared;
    /// <summary>Só colisão (invisível no jogo): não desenhar.</summary>
    public bool CollideOnly;

    static readonly Encoding Gbk;
    static BmdModel() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public static BmdModel? Read(byte[] d)
    {
        try { return Parse(d); } catch { return null; }
    }

    static BmdModel? Parse(byte[] d)
    {
        if (d.Length < 0x3C || Encoding.ASCII.GetString(d, 0, 4) != "MOXB") return null;
        // versão 0x80000001: 1 byte + a versão antiga (0x10000002) na frente; o resto igual, 5 bytes adiante
        int sh = BitConverter.ToUInt32(d, 4) == 0x80000001 ? 5 : 0;
        var m = new BmdModel { Scale = BitConverter.ToSingle(d, 8 + sh), CollideOnly = sh > 0 && d[8] != 0 };
        for (int i = 0; i < 3; i++)
        {
            m.Dir[i] = BitConverter.ToSingle(d, 0x14 + sh + i * 4);
            m.Up[i] = BitConverter.ToSingle(d, 0x20 + sh + i * 4);
            m.Pos[i] = BitConverter.ToSingle(d, 0x2C + sh + i * 4);
        }
        m.Declared = BitConverter.ToInt32(d, 0x38 + sh);
        if (m.Declared <= 0 || m.Declared > 10000) return m;
        int o = 0x3C + sh;
        // modelo 0x10000300+ (cliente 180: 0x80000001 + byte + 0x10000302): malhas em outro formato
        if (BitConverter.ToUInt32(d, sh > 0 ? 9 : 4) >= 0x10000300) { ParseV300(d, o, m); return m; }
        uint ver = BitConverter.ToUInt32(d, o);
        for (int k = 0; k < m.Declared && o > 0 && o + 4 + 64 + 256 + 8 <= d.Length; k++)
        {
            int tex = o + 4 + 64;
            int nv = BitConverter.ToInt32(d, tex + 256), nf = BitConverter.ToInt32(d, tex + 260);
            // malha 0x10000006 (região dos abissais, pedaço 70 do mundo 1.4.4): 1 byte a mais antes dos vértices;
            // sem pular, tudo saía deslocado e virava triângulos infinitos que cobriam a tela de preto/azul
            int extra = BitConverter.ToUInt32(d, o) >= 0x10000006 ? 1 : 0;
            int vo = tex + 264 + extra, io = vo + nv * 24, end = io + nf * 6;
            if (nv <= 0 || nf <= 0 || nv > 65536 || end > d.Length) break;
            int z = Array.IndexOf(d, (byte)0, tex, 256);
            var texName = Gbk.GetString(d, tex, (z < 0 ? tex + 256 : z) - tex);
            var verts = new byte[nv * 24];
            Buffer.BlockCopy(d, vo, verts, 0, verts.Length);
            // depois dos índices: normais (nv × 12) e as cores de DIA e de NOITE (nv × 4 cada; A3DLitMesh). A cor dentro do
            // vértice nem sempre é a iluminada (a19: tons verde/roxo); a de dia é a que o jogo mostra. Só usa se for opaca.
            int day = end + nv * 12;
            byte[]? night = null;
            if (day + nv * 8 <= d.Length)
            {
                int opaque = 0;
                for (int i = 0; i < nv; i++) if (d[day + i * 4 + 3] == 0xFF) opaque++;
                if (opaque >= nv * 9 / 10)
                {
                    for (int i = 0; i < nv; i++) Buffer.BlockCopy(d, day + i * 4, verts, i * 24 + 12, 3);   // BGR; alfa do vértice fica
                    night = new byte[nv * 4];
                    for (int i = 0; i < nv; i++) { Buffer.BlockCopy(d, day + nv * 4 + i * 4, night, i * 4, 3); night[i * 4 + 3] = verts[i * 24 + 15]; }
                }
            }
            var idx = new ushort[nf * 3];
            Buffer.BlockCopy(d, io, idx, 0, idx.Length * 2);
            // rede de segurança: formato que ainda não conhecemos não pode virar triângulo do tamanho do mundo
            bool sane = true;
            for (int i = 0; i < nv && sane; i++)
                for (int c = 0; c < 3; c++)
                {
                    float v = BitConverter.ToSingle(verts, i * 24 + c * 4);
                    if (!float.IsFinite(v) || Math.Abs(v) > 100_000) { sane = false; break; }
                }
            if (sane && idx.All(x => x < nv)) m.Meshes.Add(new Mesh(texName, verts, idx) { Night = night });
            o = k + 1 < m.Declared ? NextMesh(d, end + nv * 12, ver) : -1;
        }
        return m;
    }

    /// <summary>
    /// Malhas do modelo 0x10000300+ (cliente 180; A3DLitMesh mais novo que o fonte do Angelica 2.2): u32, nome e
    /// textura com tamanho na frente (i32 + GBK), i32 vértices, i32 faces, vértices 24 B, índices de 32 BITS (12 B por
    /// face), normais, cores de dia e de noite (4 B cada) e 150 B fixos (caixa + material) até a próxima. Conferido no
    /// litmodels\world\88\litmodel_50.bmd: posições, índices e normais iguais aos do 1.5.5.
    /// </summary>
    static void ParseV300(byte[] d, int o, BmdModel m)
    {
        for (int k = 0; k < m.Declared && o + 16 <= d.Length; k++)
        {
            int q = o + 4;
            int ln = BitConverter.ToInt32(d, q);
            if (ln < 0 || ln > 256 || q + 4 + ln + 4 > d.Length) break;
            q += 4 + ln;
            int lt = BitConverter.ToInt32(d, q);
            if (lt < 0 || lt > 512 || q + 4 + lt + 8 > d.Length) break;
            var texName = Gbk.GetString(d, q + 4, lt).TrimEnd('\0');
            q += 4 + lt;
            int nv = BitConverter.ToInt32(d, q), nf = BitConverter.ToInt32(d, q + 4);
            q += 8;
            if (nv <= 0 || nf <= 0 || nv > 65536) break;
            int io = q + nv * 24, no = io + nf * 12, day = no + nv * 12, night = day + nv * 4, end = night + nv * 4;
            if (end > d.Length) break;
            var verts = new byte[nv * 24];
            Buffer.BlockCopy(d, q, verts, 0, verts.Length);
            var idx = new ushort[nf * 3];
            bool ok = true;
            for (int i = 0; i < idx.Length; i++)
            {
                uint v = BitConverter.ToUInt32(d, io + i * 4);
                if (v >= nv) { ok = false; break; }
                idx[i] = (ushort)v;
            }
            byte[]? nightCol = null;
            int opaque = 0;
            for (int i = 0; i < nv; i++) if (d[day + i * 4 + 3] == 0xFF) opaque++;
            if (opaque >= nv * 9 / 10)
            {
                for (int i = 0; i < nv; i++) Buffer.BlockCopy(d, day + i * 4, verts, i * 24 + 12, 3);
                nightCol = new byte[nv * 4];
                for (int i = 0; i < nv; i++) { Buffer.BlockCopy(d, night + i * 4, nightCol, i * 4, 3); nightCol[i * 4 + 3] = verts[i * 24 + 15]; }
            }
            for (int i = 0; i < nv && ok; i++)
                for (int c = 0; c < 3; c++)
                {
                    float v = BitConverter.ToSingle(verts, i * 24 + c * 4);
                    if (!float.IsFinite(v) || Math.Abs(v) > 100_000) { ok = false; break; }
                }
            if (ok) m.Meshes.Add(new Mesh(texName, verts, idx) { Night = nightCol });
            o = end + 150;
        }
    }

    /// <summary>Vértices no mundo (coordenadas do jogo): local × (direita = cima×frente, cima, frente)·escala + posição.</summary>
    public static List<float[]> WorldVertices(BmdModel m)
    {
        float[] f = m.Dir, u = m.Up;
        if (f[0] * f[0] + f[1] * f[1] + f[2] * f[2] < 1e-6f || u[0] * u[0] + u[1] * u[1] + u[2] * u[2] < 1e-6f) { f = new[] { 0f, 0, 1 }; u = new[] { 0f, 1, 0 }; }
        float[] r = { u[1] * f[2] - u[2] * f[1], u[2] * f[0] - u[0] * f[2], u[0] * f[1] - u[1] * f[0] };
        float s = m.Scale is > 0 and < 1000 ? m.Scale : 1;
        var list = new List<float[]>();
        foreach (var mesh in m.Meshes)
            for (int o = 0; o + 12 <= mesh.Vertices.Length; o += 24)
            {
                float x = BitConverter.ToSingle(mesh.Vertices, o) * s, y = BitConverter.ToSingle(mesh.Vertices, o + 4) * s, z = BitConverter.ToSingle(mesh.Vertices, o + 8) * s;
                list.Add(new[] { r[0] * x + u[0] * y + f[0] * z + m.Pos[0], r[1] * x + u[1] * y + f[1] * z + m.Pos[1], r[2] * x + u[2] * y + f[2] * z + m.Pos[2] });
            }
        return list;
    }

    /// <summary>
    /// Brushes de colisão no FIM do .bmd (CELBrushBuilding::Load, EL_BrushBuilding.cpp): depois do lit model, i32 nHulls,
    /// nHulls listas (i32 n + n ids de malha) e nHulls CCDBrush no formato do map.bht (caixa 48 B, reservado, nLados, lados
    /// de 17 B). Coordenadas do mundo, iguais às do servidor. Como o fim do lit model não é fixo, procura de trás para a
    /// frente o começo que lê tudo certinho até o último byte. Devolve o início de cada brush, ou null se não achar.
    /// </summary>
    public static List<int>? CdBrushes(byte[] d)
    {
        if (d.Length >= 4 && BitConverter.ToInt32(d, d.Length - 4) == 0) return new();   // nHulls = 0 no fim
        for (int o = d.Length - 8; o >= 0x3C; o--)
        {
            int n = BitConverter.ToInt32(d, o);
            if (n <= 0 || n > 4096 || o + 4 + n * 4 > d.Length) continue;
            if (TryBrushes(d, o, n) is { } r) return r;
        }
        return null;
    }

    /// <summary>
    /// Arestas dos brushes de colisão (para desenhar "Ver colisão"): cada brush é a interseção dos semiespaços n·x &lt;= dist
    /// dos lados; os cantos são os pontos onde 3 lados se cruzam dentro de todos, e uma aresta liga dois cantos que dividem 2
    /// lados. Coordenadas do mundo do arquivo. Brush estranho (muitos lados ou sem cantos) fica de fora.
    /// </summary>
    public static List<(System.Numerics.Vector3 A, System.Numerics.Vector3 B)> BrushEdges(byte[] d)
    {
        var r = new List<(System.Numerics.Vector3, System.Numerics.Vector3)>();
        if (CdBrushes(d) is not { } all) return r;
        foreach (int at in all)
        {
            int n = BitConverter.ToInt32(d, at + 52);
            if (n > 64) continue;
            var pl = new (System.Numerics.Vector3 N, float D)[n];
            for (int k = 0; k < n; k++)
            {
                int p = at + 56 + 17 * k;
                pl[k] = (new(BitConverter.ToSingle(d, p), BitConverter.ToSingle(d, p + 4), BitConverter.ToSingle(d, p + 8)), BitConverter.ToSingle(d, p + 12));
            }
            var corners = new List<(System.Numerics.Vector3 P, List<int> Planes)>();
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) for (int k = j + 1; k < n; k++)
                    {
                        var (a, b, c) = (pl[i].N, pl[j].N, pl[k].N);
                        var bc = System.Numerics.Vector3.Cross(b, c);
                        float det = System.Numerics.Vector3.Dot(a, bc);
                        if (MathF.Abs(det) < 1e-6f) continue;
                        var x = (pl[i].D * bc + pl[j].D * System.Numerics.Vector3.Cross(c, a) + pl[k].D * System.Numerics.Vector3.Cross(a, b)) / det;
                        bool inside = true;
                        for (int m = 0; m < n && inside; m++) if (System.Numerics.Vector3.Dot(pl[m].N, x) - pl[m].D > 0.01f) inside = false;
                        if (!inside || float.IsNaN(x.X)) continue;
                        if (corners.FirstOrDefault(q => System.Numerics.Vector3.DistanceSquared(q.P, x) < 1e-4f) is { Planes: not null } same)
                        { foreach (int q in new[] { i, j, k }) if (!same.Planes.Contains(q)) same.Planes.Add(q); }
                        else corners.Add((x, new List<int> { i, j, k }));
                    }
            for (int i = 0; i < corners.Count; i++)
                for (int j = i + 1; j < corners.Count; j++)
                    if (corners[i].Planes.Count(corners[j].Planes.Contains) >= 2) r.Add((corners[i].P, corners[j].P));
        }
        return r;
    }

    static List<int>? TryBrushes(byte[] d, int o, int n)
    {
        int p = o + 4;
        for (int i = 0; i < n; i++)   // listas de malhas
        {
            if (p + 4 > d.Length) return null;
            int k = BitConverter.ToInt32(d, p);
            if (k < 0 || k > 4096 || p + 4 + k * 4L > d.Length) return null;
            p += 4 + k * 4;
        }
        var at = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            if (p + 56 > d.Length) return null;
            int sides = BitConverter.ToInt32(d, p + 52);
            if (sides < 4 || sides > 512 || p + 56 + 17L * sides > d.Length) return null;
            // caixa coerente: mínimo <= máximo e centro no meio
            for (int c = 0; c < 3; c++)
            {
                float mn = BitConverter.ToSingle(d, p + 24 + c * 4), mx = BitConverter.ToSingle(d, p + 36 + c * 4), ce = BitConverter.ToSingle(d, p + c * 4);
                if (!(mn <= mx) || Math.Abs((mn + mx) / 2 - ce) > 0.01f + Math.Abs(ce) * 1e-5f) return null;
            }
            at.Add(p);
            p += 56 + 17 * sides;
        }
        return p == d.Length ? at : null;
    }

    /// <summary>
    /// Cascas de colisão da construção (depois do lit model: i32 nHulls, listas de malhas, e cada casca CConvexHullData:
    /// marca "V0.2======Convex_Hull_Data_Start=========" (ou V0.1 / "==========..." sem flags), i32 flags, i32 tamanho,
    /// i32 nVértices + vértices 3f, faces...; CCHBasedCD/ConvexHullData.cpp). Achadas pela marca. Devolve, por casca, o
    /// início dos vértices no arquivo e a quantidade (o servidor recebe cada casca como 1 brush do map.bht).
    /// </summary>
    public static List<(int At, int Count)> Hulls(byte[] d)
    {
        var r = new List<(int, int)>();
        var tag = "Convex_Hull_Data_Start========="u8;
        for (int i = d.AsSpan().IndexOf(tag); i >= 0;)
        {
            int start = i - 10;   // "V0.2======" ou "==========" antes do nome
            if (start >= 0)
            {
                bool flags = d[start] == (byte)'V';
                int o = i + tag.Length + (flags ? 4 : 0) + 4;   // flags, tamanho do buffer
                if (o + 4 <= d.Length)
                {
                    int n = BitConverter.ToInt32(d, o);
                    if (n > 0 && n < 100_000 && o + 4 + n * 12 <= d.Length) r.Add((o + 4, n));
                }
            }
            int next = d.AsSpan(i + tag.Length).IndexOf(tag);
            i = next < 0 ? -1 : i + tag.Length + next;
        }
        return r;
    }

    // próxima malha: mesma versão, nome legível (ou vazio) e contagens plausíveis
    static int NextMesh(byte[] d, int from, uint ver)
    {
        for (int o = Math.Max(from, 0); o + 4 + 64 + 256 + 8 <= d.Length; o++)
        {
            if (BitConverter.ToUInt32(d, o) != ver) continue;
            int tex = o + 4 + 64;
            int nv = BitConverter.ToInt32(d, tex + 256), nf = BitConverter.ToInt32(d, tex + 260);
            if (nv <= 0 || nf <= 0 || nv > 65536 || tex + 264 + (ver >= 0x10000006 ? 1 : 0) + nv * 24 + nf * 6 > d.Length) continue;
            if (d[tex] < 0x20) continue;   // textura começa com texto
            return o;
        }
        return -1;
    }
}
