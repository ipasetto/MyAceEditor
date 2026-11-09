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
