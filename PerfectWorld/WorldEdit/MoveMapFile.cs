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
