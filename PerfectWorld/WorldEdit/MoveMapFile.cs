namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Passagem dos NPCs do servidor: config\&lt;mapa&gt;\movemap\N.rmap (CBitImage do editor oficial, PathMap/BitImage.h):
/// u32 versão 1, u32 tamanho (= 20 + largura × linhas), i32 largura em bytes (128), i32 linhas (1024), i32 largura e
/// altura da imagem (1024), f tamanho do pixel (1 m), bits: pixel (u, v) = byte[v × largura + u/8] bit (u % 8), 1 = pode
/// andar. u = metro a partir do oeste do pedaço, v = metro a partir do SUL (conferido: 77–81% dos trechos íngremes do .hmap
/// batem bloqueados assim; linha 0 ao norte não bate). Ao mudar a altura, refaz só os metros cujo chão mudou: pode andar
/// onde a subida para um vizinho a 1 m é de até 1 m (degrau do editor oficial, CPassMapGenerator m_fPFStepHeight); onde
/// já era bloqueado num chão plano (prédio, pedra, cerca) continua bloqueado. O .dhmap (altura sobre o chão) não muda.
/// </summary>
public static class MoveMapFile
{
    public const float MaxStep = 1.0f;

    public static (int Width, int Rows, int ImgW, int ImgH, float Pixel) Header(byte[] d)
    {
        if (d.Length < 28 || BitConverter.ToInt32(d, 0) != 1) throw new InvalidDataException(".rmap: versão desconhecida");
        int size = BitConverter.ToInt32(d, 4), w = BitConverter.ToInt32(d, 8), rows = BitConverter.ToInt32(d, 12), iw = BitConverter.ToInt32(d, 16), ih = BitConverter.ToInt32(d, 20);
        float px = BitConverter.ToSingle(d, 24);
        if (w <= 0 || rows <= 0 || w * 8 < iw || rows < ih || size != 20 + w * rows || d.Length < 8 + size || !(px > 0)) throw new InvalidDataException(".rmap: cabeçalho fora do esperado");
        return (w, rows, iw, ih, px);
    }

    /// <summary>
    /// Construções mexidas: nos retângulos, onde a colisão mudou na altura de um personagem: parede nova = bloqueado; parede
    /// que saiu = livre se o chão não é íngreme (degrau de 1 m). Onde a colisão não mudou, fica como está.
    /// </summary>
    public static (byte[] Data, int Changed) UpdateBuildings(byte[] rmap, float x0, float zSouth, IEnumerable<(float X0, float Z0, float X1, float Z1)> rects,
        Func<float, float, float?> h, Func<float, float, float, bool> oldWall, Func<float, float, float, bool> newWall)
    {
        var (w, _, iw, ih, px) = Header(rmap);
        var d = (byte[])rmap.Clone();
        int changed = 0;
        var done = new HashSet<int>();
        foreach (var (rx0, rz0, rx1, rz1) in rects)
        {
            int u0 = Math.Max(0, (int)MathF.Floor((rx0 - x0) / px)), u1 = Math.Min(iw - 1, (int)MathF.Ceiling((rx1 - x0) / px));
            int v0 = Math.Max(0, (int)MathF.Floor((rz0 - zSouth) / px)), v1 = Math.Min(ih - 1, (int)MathF.Ceiling((rz1 - zSouth) / px));
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    if (!done.Add(v * iw + u)) continue;
                    float x = x0 + (u + 0.5f) * px, z = zSouth + (v + 0.5f) * px;
                    if (h(x, z) is not float y) continue;
                    bool ow = oldWall(x, y, z), nw = newWall(x, y, z);
                    if (ow == nw) continue;
                    bool now;
                    if (nw) now = false;
                    else
                    {
                        float s = 0;
                        foreach (var (dx, dz) in new[] { (px, 0f), (-px, 0f), (0f, px), (0f, -px) }) if (h(x + dx, z + dz) is float n) s = MathF.Max(s, MathF.Abs(n - y));
                        now = s <= MaxStep;
                    }
                    bool was = Reach(d, w, u, v);
                    if (now == was) continue;
                    ref byte b = ref d[28 + v * w + (u >> 3)];
                    b = now ? (byte)(b | (1 << (u & 7))) : (byte)(b & ~(1 << (u & 7)));
                    changed++;
                }
        }
        return (d, changed);
    }

    /// <summary>
    /// .dhmap (CBlockImage&lt;FIX16&gt;, PathMap/BlockImage.h): u32 1, u32 tamanho, i32 blocos na largura e no comprimento,
    /// i32 expoente do bloco, i32 imagem 1024 × 1024, f pixel, i32[blocos] índice do bloco (−1 = tudo zero), i32 n, n blocos
    /// de lado² u16; valor/64 = metros acima do chão onde o NPC pisa (o gs põe o NPC em chão + valor). Mesmo sentido do
    /// .rmap (v do sul). Chão mexido embaixo de ponte/piso: soma (chão velho − chão novo) para o piso ficar na mesma altura
    /// (≤ 0 vira 0: o chão subiu acima do piso). Blocos não mexidos ficam iguais; bloco mexido vira um bloco novo no fim
    /// (os índices dos outros não mudam). ~1024 m (marcador) não mexe.
    /// </summary>
    public static (byte[] Data, int Changed) AdjustDeltaHeights(byte[] dh, float x0, float zSouth, IEnumerable<(float X0, float Z0, float X1, float Z1)> rects,
        Func<float, float, float?> newH, Func<float, float, float?> oldH)
    {
        if (dh.Length < 36 || BitConverter.ToInt32(dh, 0) != 1) throw new InvalidDataException(".dhmap: versão desconhecida");
        int bw = BitConverter.ToInt32(dh, 8), bl = BitConverter.ToInt32(dh, 12), exp = BitConverter.ToInt32(dh, 16), iw = BitConverter.ToInt32(dh, 20), ih = BitConverter.ToInt32(dh, 24);
        float px = BitConverter.ToSingle(dh, 28);
        if (bw <= 0 || bl <= 0 || bw * bl > 1 << 20 || exp < 0 || exp > 10 || !(px > 0)) throw new InvalidDataException(".dhmap: cabeçalho fora do esperado");
        int bs = 1 << exp, cells = bs * bs, idsAt = 32, nAt = idsAt + 4 * bw * bl;
        if (nAt + 4 > dh.Length) throw new InvalidDataException(".dhmap: curto");
        int n = BitConverter.ToInt32(dh, nAt), blocksAt = nAt + 4;
        if (n < 0 || blocksAt + 2L * cells * n > dh.Length) throw new InvalidDataException(".dhmap: blocos fora do arquivo");
        var ids = new int[bw * bl];
        for (int i = 0; i < ids.Length; i++) ids[i] = BitConverter.ToInt32(dh, idsAt + 4 * i);
        var blocks = new List<ushort[]>(n);
        for (int b = 0; b < n; b++) { var a = new ushort[cells]; Buffer.BlockCopy(dh, blocksAt + 2 * cells * b, a, 0, 2 * cells); blocks.Add(a); }
        var edited = new Dictionary<int, ushort[]>();   // posição do bloco -> dados novos
        int changed = 0;
        var done = new HashSet<int>();
        foreach (var (rx0, rz0, rx1, rz1) in rects)
        {
            int u0 = Math.Max(0, (int)MathF.Floor((rx0 - x0) / px)), u1 = Math.Min(iw - 1, (int)MathF.Ceiling((rx1 - x0) / px));
            int v0 = Math.Max(0, (int)MathF.Floor((rz0 - zSouth) / px)), v1 = Math.Min(ih - 1, (int)MathF.Ceiling((rz1 - zSouth) / px));
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    if (!done.Add(v * iw + u)) continue;
                    int bx = u >> exp, by = v >> exp; if (bx >= bw || by >= bl) continue;
                    int pos = by * bw + bx, k = (v - (by << exp)) * bs + (u - (bx << exp));
                    var cur = edited.TryGetValue(pos, out var e) ? e : ids[pos] >= 0 && ids[pos] < n ? blocks[ids[pos]] : null;
                    if (cur is null || cur[k] == 0 || cur[k] >= 65000) continue;   // sem piso aqui (ou marcador)
                    float x = x0 + (u + 0.5f) * px, z = zSouth + (v + 0.5f) * px;
                    if (newH(x, z) is not float hn || oldH(x, z) is not float ho || MathF.Abs(hn - ho) < 0.02f) continue;
                    float m = cur[k] / 64f + (ho - hn);
                    ushort nv = m <= 0.02f ? (ushort)0 : (ushort)Math.Min(64999, (int)MathF.Round(m * 64));
                    if (nv == cur[k]) continue;
                    if (!edited.ContainsKey(pos)) { cur = (ushort[])cur.Clone(); edited[pos] = cur; }
                    cur[k] = nv; changed++;
                }
        }
        if (changed == 0) return (dh, 0);
        foreach (var (pos, data) in edited)
        {
            if (data.All(x => x == 0)) { ids[pos] = -1; continue; }
            blocks.Add(data); ids[pos] = blocks.Count - 1;
        }
        int size = 5 * 4 + 4 + 4 * ids.Length + 4 + 2 * cells * blocks.Count;
        var o = new byte[8 + size];
        Buffer.BlockCopy(dh, 0, o, 0, 32);
        BitConverter.GetBytes(size).CopyTo(o, 4);
        for (int i = 0; i < ids.Length; i++) BitConverter.GetBytes(ids[i]).CopyTo(o, idsAt + 4 * i);
        BitConverter.GetBytes(blocks.Count).CopyTo(o, nAt);
        for (int b = 0; b < blocks.Count; b++) Buffer.BlockCopy(blocks[b], 0, o, blocksAt + 2 * cells * b, 2 * cells);
        return (o, changed);
    }

    /// <summary>.dhmap aberto para ler/mudar ponto a ponto (blocos mexidos viram blocos novos no fim ao gravar).</summary>
    public sealed class DeltaImage
    {
        readonly byte[] _src; readonly int _bw, _bl, _exp, _bs, _cells, _idsAt;
        readonly int[] _ids; readonly List<ushort[]> _blocks; readonly Dictionary<int, ushort[]> _edited = new();
        public int Width { get; } public int Height { get; }
        public DeltaImage(byte[] dh)
        {
            if (dh.Length < 36 || BitConverter.ToInt32(dh, 0) != 1) throw new InvalidDataException(".dhmap: versão desconhecida");
            _src = dh; _bw = BitConverter.ToInt32(dh, 8); _bl = BitConverter.ToInt32(dh, 12); _exp = BitConverter.ToInt32(dh, 16);
            Width = BitConverter.ToInt32(dh, 20); Height = BitConverter.ToInt32(dh, 24);
            if (_bw <= 0 || _bl <= 0 || _bw * _bl > 1 << 20 || _exp < 0 || _exp > 10) throw new InvalidDataException(".dhmap: cabeçalho fora do esperado");
            _bs = 1 << _exp; _cells = _bs * _bs; _idsAt = 32;
            int nAt = _idsAt + 4 * _bw * _bl; if (nAt + 4 > dh.Length) throw new InvalidDataException(".dhmap: curto");
            int n = BitConverter.ToInt32(dh, nAt), at = nAt + 4;
            if (n < 0 || at + 2L * _cells * n > dh.Length) throw new InvalidDataException(".dhmap: blocos fora do arquivo");
            _ids = new int[_bw * _bl]; for (int i = 0; i < _ids.Length; i++) _ids[i] = BitConverter.ToInt32(dh, _idsAt + 4 * i);
            _blocks = new List<ushort[]>(n);
            for (int b = 0; b < n; b++) { var a = new ushort[_cells]; Buffer.BlockCopy(dh, at + 2 * _cells * b, a, 0, 2 * _cells); _blocks.Add(a); }
        }
        (int Pos, int K) Ix(int u, int v) => ((v >> _exp) * _bw + (u >> _exp), (v - ((v >> _exp) << _exp)) * _bs + (u - ((u >> _exp) << _exp)));
        /// <summary>Metros acima do chão (0 = no chão; ~1024 = marcador).</summary>
        public float Get(int u, int v)
        {
            var (pos, k) = Ix(u, v); if (pos < 0 || pos >= _ids.Length) return 0;
            var cur = _edited.TryGetValue(pos, out var e) ? e : _ids[pos] >= 0 && _ids[pos] < _blocks.Count ? _blocks[_ids[pos]] : null;
            return cur is null ? 0 : cur[k] / 64f;
        }
        public void Set(int u, int v, float meters)
        {
            var (pos, k) = Ix(u, v); if (pos < 0 || pos >= _ids.Length) return;
            ushort nv = meters <= 0.02f ? (ushort)0 : (ushort)Math.Min(64999, (int)MathF.Round(meters * 64));
            if (!_edited.TryGetValue(pos, out var cur))
            {
                var orig = _ids[pos] >= 0 && _ids[pos] < _blocks.Count ? _blocks[_ids[pos]] : null;
                if ((orig?[k] ?? 0) == nv) return;
                cur = orig is null ? new ushort[_cells] : (ushort[])orig.Clone(); _edited[pos] = cur;
            }
            cur[k] = nv;
        }
        public bool Dirty => _edited.Count > 0;
        public byte[] Save()
        {
            if (_edited.Count == 0) return _src;
            var ids = (int[])_ids.Clone(); var blocks = new List<ushort[]>(_blocks);
            foreach (var (pos, data) in _edited) { if (data.All(x => x == 0)) { ids[pos] = -1; continue; } blocks.Add(data); ids[pos] = blocks.Count - 1; }
            int size = 5 * 4 + 4 + 4 * ids.Length + 4 + 2 * _cells * blocks.Count, nAt = _idsAt + 4 * ids.Length;
            var o = new byte[8 + size];
            Buffer.BlockCopy(_src, 0, o, 0, 32); BitConverter.GetBytes(size).CopyTo(o, 4);
            for (int i = 0; i < ids.Length; i++) BitConverter.GetBytes(ids[i]).CopyTo(o, _idsAt + 4 * i);
            BitConverter.GetBytes(blocks.Count).CopyTo(o, nAt);
            for (int b = 0; b < blocks.Count; b++) Buffer.BlockCopy(blocks[b], 0, o, nAt + 4 + 2 * _cells * b, 2 * _cells);
            return o;
        }
    }

    /// <summary>
    /// Construções postas, mexidas ou apagadas (passagem com pisos, como o editor oficial: PassMapGenerator ExpandPassRegion).
    /// Nos retângulos, os metros cuja coluna (do chão a +80 m) mudou de colisão (velha × nova) são refeitos: partindo dos
    /// vizinhos onde já dá para andar (altura = chão + delta), anda metro a metro; em cada metro desce um raio a partir de
    /// 1 m (degrau) + 0,3 m acima da altura atual até o chão e o 1º topo achado é o piso (de construção ou o chão). Parede na
    /// altura de um personagem (piso + 0,5 até + 1,8 m) bloqueia. Metro mudado que ninguém alcança fica bloqueado e no chão.
    /// Grava .rmap (pode andar) e .dhmap (piso − chão). Metros cuja colisão não mudou ficam como estavam.
    /// </summary>
    public static (byte[] Rmap, byte[]? Dhmap, int Changed) UpdateWithFloors(byte[] rmap, byte[]? dhmap, float x0, float zSouth,
        IEnumerable<(float X0, float Z0, float X1, float Z1)> rects, Func<float, float, float?> h, BrushSet oldB, BrushSet newB,
        MeshObstacles? oldM = null, MeshObstacles? newM = null)
    {
        var (w, _, iw, ih, px) = Header(rmap);
        var d = (byte[])rmap.Clone();
        var dimg = dhmap is { Length: >= 36 } ? new DeltaImage(dhmap) : null;
        var rect = new HashSet<int>(); var changedCol = new HashSet<int>();
        System.Numerics.Vector3 P(int u, int v, float y) => new(x0 + (u + 0.5f) * px, y, zSouth + (v + 0.5f) * px);
        // corpo do monstro (usuário 09/11: atravessavam peças finas das construções coladas): centro do metro + 4 pontos a
        // 0,45 m (o editor oficial anda com esfera de 0,3 m; a folga cobre o canto entre metros)
        var body = new[] { System.Numerics.Vector3.Zero, new(0.45f, 0, 0), new(-0.45f, 0, 0), new(0, 0, 0.45f), new(0, 0, -0.45f) };
        bool AnyHit(BrushSet set, System.Numerics.Vector3 a, System.Numerics.Vector3 b) { foreach (var o in body) if (set.Hit(a + o, b + o)) return true; return false; }
        // malha das construções mexidas (MeshObstacles): coluna do metro e corpo do monstro como caixas
        static bool MeshCol(MeshObstacles? m, System.Numerics.Vector3 c, float y0, float y1, float half) =>
            m is { Count: > 0 } && m.Overlaps(new(c.X - half, y0, c.Z - half), new(c.X + half, y1, c.Z + half));
        foreach (var (rx0, rz0, rx1, rz1) in rects)
        {
            int u0 = Math.Max(0, (int)MathF.Floor((rx0 - x0) / px)), u1 = Math.Min(iw - 1, (int)MathF.Ceiling((rx1 - x0) / px));
            int v0 = Math.Max(0, (int)MathF.Floor((rz0 - zSouth) / px)), v1 = Math.Min(ih - 1, (int)MathF.Ceiling((rz1 - zSouth) / px));
            for (int v = v0; v <= v1; v++) for (int u = u0; u <= u1; u++)
                {
                    int id = v * iw + u; if (!rect.Add(id)) continue;
                    var c = P(u, v, 0); if (h(c.X, c.Z) is not float g) continue;
                    var lo = c with { Y = g - 1 }; var hi = c with { Y = g + 80 };
                    if (AnyHit(oldB, lo, hi) != AnyHit(newB, lo, hi) || AnyHit(oldB, hi, lo) != AnyHit(newB, hi, lo)
                        || MeshCol(oldM, c, g - 1, g + 80, 0.5f) != MeshCol(newM, c, g - 1, g + 80, 0.5f)
                        || MeshCol(newM, c, g - 1, g + 80, 0.5f) && MeshCol(oldM, c, g - 1, g + 80, 0.5f)) changedCol.Add(id);
                }
        }
        if (changedCol.Count == 0) return (rmap, null, 0);
        // altura em que se anda em cada metro já alcançado (chão + delta); sementes = vizinhos não mudados onde dá para andar
        var height = new Dictionary<int, float>(); var queue = new Queue<int>();
        int[] du = { 1, -1, 0, 0 }, dv = { 0, 0, 1, -1 };
        foreach (int id in changedCol)
        {
            int u = id % iw, v = id / iw;
            for (int k = 0; k < 4; k++)
            {
                int nu = u + du[k], nv = v + dv[k]; if (nu < 0 || nv < 0 || nu >= iw || nv >= ih) continue;
                int nid = nv * iw + nu; if (changedCol.Contains(nid) || height.ContainsKey(nid) || !Reach(d, w, nu, nv)) continue;
                var c = P(nu, nv, 0); if (h(c.X, c.Z) is not float g) continue;
                float dh = dimg?.Get(nu, nv) ?? 0; if (dh >= 1000) dh = 0;
                height[nid] = g + dh; queue.Enqueue(nid);
            }
        }
        var reached = new Dictionary<int, float>();
        while (queue.Count > 0)
        {
            int id = queue.Dequeue(); int u = id % iw, v = id / iw; float y = height[id];
            for (int k = 0; k < 4; k++)
            {
                int nu = u + du[k], nv = v + dv[k]; if (nu < 0 || nv < 0 || nu >= iw || nv >= ih) continue;
                int nid = nv * iw + nu; if (!changedCol.Contains(nid) || reached.ContainsKey(nid)) continue;
                var c = P(nu, nv, 0); if (h(c.X, c.Z) is not float g) continue;
                float top = MathF.Max(y, g) + MaxStep + 0.3f;
                var a = c with { Y = top }; var b = c with { Y = g - 0.05f };
                float floor = newB.FirstHit(a, b) is float t ? MathF.Max(g, top + (g - 0.05f - top) * t) : g;
                if (newM?.FloorBelow(c.X, c.Z, top, g - 0.05f) is float mf) floor = MathF.Max(floor, mf);   // piso da malha (ponte)
                if (floor > y + MaxStep + 0.01f) continue;   // degrau alto demais
                if (AnyHit(newB, c with { Y = floor + 0.5f }, c with { Y = floor + 1.8f })) continue;   // parede na altura de um personagem (com o corpo)
                if (MeshCol(newM, c, floor + 0.5f, floor + 1.8f, 0.5f)) continue;   // parede da malha (lado da ponte, peça sem brush)
                if (floor <= g + 0.02f)
                {   // no chão: a inclinação vale (degrau de 1 m para os vizinhos)
                    float s = 0;
                    foreach (var (ex, ez) in new[] { (px, 0f), (-px, 0f), (0f, px), (0f, -px) }) if (h(c.X + ex, c.Z + ez) is float n) s = MathF.Max(s, MathF.Abs(n - g));
                    if (s > MaxStep) continue;
                }
                reached[nid] = floor; height[nid] = floor; queue.Enqueue(nid);
            }
        }
        int changed = 0;
        foreach (int id in changedCol)
        {
            int u = id % iw, v = id / iw;
            bool now = reached.TryGetValue(id, out float floor);
            var c = P(u, v, 0); float g = h(c.X, c.Z) ?? 0;
            if (Reach(d, w, u, v) != now)
            {
                ref byte bt = ref d[28 + v * w + (u >> 3)];
                bt = now ? (byte)(bt | (1 << (u & 7))) : (byte)(bt & ~(1 << (u & 7)));
                changed++;
            }
            dimg?.Set(u, v, now ? floor - g : 0);
        }
        return (d, dimg is { Dirty: true } ? dimg.Save() : null, changed);
    }

    public static bool Reach(byte[] d, int w, int u, int v) => (d[28 + v * w + (u >> 3)] & (1 << (u & 7))) != 0;

    /// <summary>
    /// Refaz os pixels dentro dos retângulos (metros do mundo) cujo chão mudou. x0/zSouth = canto sudoeste do pedaço.
    /// Devolve o arquivo novo (cópia) e quantos metros mudaram.
    /// </summary>
    public static (byte[] Data, int Changed) Update(byte[] rmap, float x0, float zSouth, IEnumerable<(float X0, float Z0, float X1, float Z1)> rects,
        Func<float, float, float?> newH, Func<float, float, float?> oldH)
    {
        var (w, _, iw, ih, px) = Header(rmap);
        var d = (byte[])rmap.Clone();
        int changed = 0;
        float Step(Func<float, float, float?> h, float x, float z, float c)
        {
            float s = 0;
            foreach (var (dx, dz) in new[] { (px, 0f), (-px, 0f), (0f, px), (0f, -px) })
                if (h(x + dx, z + dz) is float n) s = MathF.Max(s, MathF.Abs(n - c));
            return s;
        }
        var done = new HashSet<int>();
        foreach (var (rx0, rz0, rx1, rz1) in rects)
        {
            int u0 = Math.Max(0, (int)MathF.Floor((rx0 - x0) / px)), u1 = Math.Min(iw - 1, (int)MathF.Ceiling((rx1 - x0) / px));
            int v0 = Math.Max(0, (int)MathF.Floor((rz0 - zSouth) / px)), v1 = Math.Min(ih - 1, (int)MathF.Ceiling((rz1 - zSouth) / px));
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    if (!done.Add(v * iw + u)) continue;
                    float x = x0 + (u + 0.5f) * px, z = zSouth + (v + 0.5f) * px;
                    if (newH(x, z) is not float hn || oldH(x, z) is not float ho || MathF.Abs(hn - ho) < 0.05f) continue;   // chão não mudou
                    bool was = Reach(d, w, u, v);
                    if (!was && Step(oldH, x, z, ho) <= MaxStep) continue;   // obstáculo num chão plano: continua bloqueado
                    bool now = Step(newH, x, z, hn) <= MaxStep;
                    if (now == was) continue;
                    ref byte b = ref d[28 + v * w + (u >> 3)];
                    b = now ? (byte)(b | (1 << (u & 7))) : (byte)(b & ~(1 << (u & 7)));
                    changed++;
                }
        }
        return (d, changed);
    }
}
