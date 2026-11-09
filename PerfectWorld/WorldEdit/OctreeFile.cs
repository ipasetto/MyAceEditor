namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Voo dos monstros: config\&lt;mapa&gt;\airmap\N.octr = CCompactSpacePassableOctree (CElementEditor/SPOctree; gs
/// pathfinding/GlobalSPMap): u32 0xCC000001, u8 número do pedaço, cubo (i32 centro x, y, z e meia-aresta, metros do mundo),
/// i32 meia-aresta da folha, u32 n, n nós de 4 B: 2 bits de estado (0 livre, 1 bloqueado, 2 borda de casca; nós internos
/// têm estado 2) e 26 bits do grupo dos 8 filhos (0 = folha; filhos em nós[grupo×8 + i]); os 8 primeiros nós são os filhos da
/// raiz; grupos distribuídos em largura. Filho i: bit 2 = +x, bit 1 = +y, bit 0 = +z. O editor oficial nem sempre junta
/// cubos iguais. Para o gs só a BORDA impede passar (CanPosPass); o chão é visto à parte.
/// Edição no lugar: mudar um cubo desce a árvore e só divide o nó que precisa (8 filhos novos no fim); o resto fica igual
/// byte a byte (sem mudança = arquivo idêntico).
/// </summary>
public sealed class OctreeFile
{
    public const uint Version = 0xCC000001;
    public const byte Free = 0, Blocked = 1, Border = 2;
    public byte Id; public int Cx, Cy, Cz, Half, Leaf;
    public int N => Half / Leaf;            // folhas por eixo
    readonly List<uint> _nodes;
    readonly byte[] _head;
    public bool Dirty { get; private set; }

    OctreeFile(byte[] head, List<uint> nodes) { _head = head; _nodes = nodes; }

    public static OctreeFile Read(byte[] d)
    {
        if (d.Length < 29 || BitConverter.ToUInt32(d, 0) != Version) throw new InvalidDataException(".octr: versão desconhecida");
        uint n = BitConverter.ToUInt32(d, 25);
        var arr = new uint[n];
        if (29 + 4L * n != d.Length) throw new InvalidDataException(".octr: tamanho não confere");
        Buffer.BlockCopy(d, 29, arr, 0, (int)(4 * n));
        var o = new OctreeFile(d[..25], [.. arr])
        {
            Id = d[4], Cx = BitConverter.ToInt32(d, 5), Cy = BitConverter.ToInt32(d, 9), Cz = BitConverter.ToInt32(d, 13), Half = BitConverter.ToInt32(d, 17), Leaf = BitConverter.ToInt32(d, 21),
        };
        if (n < 8 || o.Half <= 0 || o.Leaf <= 0 || o.Half % o.Leaf != 0 || (o.N & (o.N - 1)) != 0 || o.N > 4096) throw new InvalidDataException(".octr: cabeçalho fora do esperado");
        foreach (var v in arr) if (((long)(v & 0x03FFFFFF) << 3) + 7 >= n && (v & 0x03FFFFFF) != 0) throw new InvalidDataException(".octr: filho fora do arquivo");
        return o;
    }

    /// <summary>Cubo-folha de um ponto do mundo (null fora).</summary>
    public (int X, int Y, int Z)? CellOf(float wx, float wy, float wz)
    {
        float s = 2f * Leaf; int x = (int)MathF.Floor((wx - (Cx - Half)) / s), y = (int)MathF.Floor((wy - (Cy - Half)) / s), z = (int)MathF.Floor((wz - (Cz - Half)) / s);
        return x < 0 || y < 0 || z < 0 || x >= N || y >= N || z >= N ? null : (x, y, z);
    }
    /// <summary>Centro do cubo-folha em metros do mundo.</summary>
    public System.Numerics.Vector3 Center(int x, int y, int z) => new(Cx - Half + (2 * x + 1) * Leaf, Cy - Half + (2 * y + 1) * Leaf, Cz - Half + (2 * z + 1) * Leaf);

    // índice do nó que contém a folha (desce até achar folha); devolve também o tamanho (em folhas) do cubo desse nó
    (int Slot, int Size) Find(int x, int y, int z)
    {
        int size = N / 2, slot = ((x >= size ? 4 : 0) | (y >= size ? 2 : 0) | (z >= size ? 1 : 0));
        int x0 = x >= size ? size : 0, y0 = y >= size ? size : 0, z0 = z >= size ? size : 0;
        while (true)
        {
            uint g = _nodes[slot] & 0x03FFFFFF;
            if (g == 0 || size == 1) return (slot, size);
            int hs = size / 2, i = ((x >= x0 + hs ? 4 : 0) | (y >= y0 + hs ? 2 : 0) | (z >= z0 + hs ? 1 : 0));
            if ((i & 4) != 0) x0 += hs; if ((i & 2) != 0) y0 += hs; if ((i & 1) != 0) z0 += hs;
            slot = (int)(g * 8 + i); size = hs;
        }
    }

    public byte Get(int x, int y, int z) => (byte)(_nodes[Find(x, y, z).Slot] >> 30);

    /// <summary>Muda o estado de um cubo-folha, dividindo só o necessário (filhos novos no fim).</summary>
    public void Set(int x, int y, int z, byte state)
    {
        while (true)
        {
            var (slot, size) = Find(x, y, z);
            uint v = _nodes[slot];
            if ((byte)(v >> 30) == state && ((v & 0x03FFFFFF) == 0 || size == 1)) return;
            if (size == 1) { _nodes[slot] = (uint)state << 30; Dirty = true; return; }
            // folha maior que 1 cubo com outro estado: vira nó interno (estado 2) com 8 filhos iguais a ela
            int g = _nodes.Count / 8; if (g > 0x03FFFFFF) throw new InvalidDataException(".octr: árvore grande demais");
            for (int i = 0; i < 8; i++) _nodes.Add(v & 0xC0000000);
            _nodes[slot] = ((uint)Border << 30) | (uint)g;
            Dirty = true;
        }
    }

    /// <summary>
    /// Refaz o voo em volta das construções mexidas: cada cubo-folha das colunas dos retângulos (do chão −2 cubos até
    /// 160 m acima) que passou a encostar num brush vira borda; o que deixou de encostar fica livre. "Encostar" = algum
    /// dos 7 segmentos pelo centro (3 eixos + 4 diagonais) corta um brush. Devolve quantos cubos mudaram.
    /// </summary>
    public int Refresh(IEnumerable<(float X0, float Z0, float X1, float Z1)> rects, Func<float, float, float?> ground, BrushSet was, BrushSet now)
    {
        float L = Leaf, s2 = 2 * L; int changed = 0;
        var seen = new HashSet<(int, int, int)>();
        var dirs = new[] { new System.Numerics.Vector3(L, 0, 0), new(0, L, 0), new(0, 0, L), new(L, L, L), new(L, L, -L), new(L, -L, L), new(-L, L, L) };
        bool Touch(BrushSet set, System.Numerics.Vector3 c) { foreach (var dv in dirs) if (set.Hit(c - dv, c + dv)) return true; return false; }
        foreach (var r in rects)
            for (float wz = r.Z0; wz <= r.Z1 + s2; wz += s2)
                for (float wx = r.X0; wx <= r.X1 + s2; wx += s2)
                {
                    float gx = MathF.Min(wx, r.X1), gz = MathF.Min(wz, r.Z1);
                    if (ground(gx, gz) is not float g) continue;
                    for (float wy = g - 2 * s2; wy <= g + 160; wy += s2)
                    {
                        if (CellOf(gx, wy, gz) is not { } cell || !seen.Add(cell)) continue;
                        var c = Center(cell.X, cell.Y, cell.Z);
                        bool a = Touch(was, c), b = Touch(now, c);
                        if (a == b) continue;
                        Set(cell.X, cell.Y, cell.Z, b ? Border : Free);
                        changed++;
                    }
                }
        return changed;
    }

    public byte[] Write()
    {
        var o = new byte[29 + 4 * _nodes.Count];
        Buffer.BlockCopy(_head, 0, o, 0, 25);
        BitConverter.GetBytes((uint)_nodes.Count).CopyTo(o, 25);
        Buffer.BlockCopy(_nodes.ToArray(), 0, o, 29, 4 * _nodes.Count);
        return o;
    }
}
