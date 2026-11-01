using System.Buffers.Binary;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// map.bht do servidor = colisão do gs (CBrushTree, cgame/collision/BrushTree.cpp; PW-KB §S6 colisão): 'brtf' + versão 1 +
/// N brushes (A3DAABB 48 B + reservado 4 + nº de lados 4 + lados de 17 B: normal 12, dist 4, bevel 1; plano = normal·p = dist)
/// + árvore quádrupla completa em largura (nó 18 B: centro x,z, meia-largura x,z, RoI, folha). A árvore é uma grade fixa
/// (os brushes entram nos nós na carga, _AddBrush), então mover brushes não muda a árvore. Guarda os bytes e só reescreve
/// os brushes deslocados: sem mudança, Write() é byte a byte igual (--bht-check).
/// </summary>
public sealed class BhtFile
{
    public const uint Magic = ('b' << 24) | ('t' << 16) | ('r' << 8) | 'f';
    readonly byte[] _data;
    readonly int[] _at;   // início de cada brush

    public int Count => _at.Length;
    public int TreeStart { get; }

    public BhtFile(byte[] data)
    {
        _data = (byte[])data.Clone();
        if (data.Length < 12 || U(0) != Magic || U(4) != 1) throw new InvalidDataException("map.bht: cabeçalho não é 'brtf' versão 1");
        int n = I(8), o = 12;
        if (n < 0) throw new InvalidDataException("map.bht: número de brushes inválido");
        _at = new int[n];
        for (int i = 0; i < n; i++)
        {
            _at[i] = o;
            int sides = I(o + 52);
            if (sides <= 0 || o + 56 + 17L * sides > data.Length) throw new InvalidDataException($"map.bht: brush {i} com {sides} lados");
            o += 56 + 17 * sides;
        }
        TreeStart = o;
        // árvore: nível a nível; cada nó que não é folha abre 4 filhos no próximo nível
        for (int level = 1; level > 0;)
        {
            int next = 0;
            for (int k = 0; k < level; k++, o += 18)
            {
                if (o + 18 > data.Length) throw new InvalidDataException("map.bht: árvore cortada");
                if (data[o + 17] == 0) next += 4;
            }
            level = next;
        }
        if (o != data.Length) throw new InvalidDataException($"map.bht: sobram {data.Length - o} bytes depois da árvore");
    }

    public byte[] Write() => (byte[])_data.Clone();

    /// <summary>Caixa (mínimo e máximo, coordenadas do jogo) do brush i.</summary>
    public (float X0, float Y0, float Z0, float X1, float Y1, float Z1) Box(int i)
    {
        int o = _at[i];
        return (F(o + 24), F(o + 28), F(o + 32), F(o + 36), F(o + 40), F(o + 44));
    }

    /// <summary>
    /// O segmento a→b (coordenadas do jogo) entra em algum brush sólido? Brush = sólido convexo (dentro de todos os planos
    /// normal·p &lt;= dist); recorte de Cyrus-Beck. Devolve o índice do brush ou -1. Caminho de patrulha não pode atravessar
    /// pedra, árvore ou construção, igual ao jogador (gs trace_manager2).
    /// </summary>
    public int SegmentHit(float ax, float ay, float az, float bx, float by, float bz)
    {
        float lx = MathF.Min(ax, bx), hx = MathF.Max(ax, bx), ly = MathF.Min(ay, by), hy = MathF.Max(ay, by), lz = MathF.Min(az, bz), hz = MathF.Max(az, bz);
        float dx = bx - ax, dy = by - ay, dz = bz - az;
        for (int i = 0; i < _at.Length; i++)
        {
            var bb = Box(i);
            if (bb.X1 < lx || bb.X0 > hx || bb.Y1 < ly || bb.Y0 > hy || bb.Z1 < lz || bb.Z0 > hz) continue;
            int o = _at[i], sides = I(o + 52);
            float t0 = 0, t1 = 1; bool hit = true;
            for (int k = 0; k < sides && hit; k++)
            {
                int p = o + 56 + 17 * k;
                float nx = F(p), ny = F(p + 4), nz = F(p + 8), dist = F(p + 12);
                float side0 = nx * ax + ny * ay + nz * az - dist, den = nx * dx + ny * dy + nz * dz;
                if (MathF.Abs(den) < 1e-6f) { if (side0 > 0) hit = false; continue; }
                float t = -side0 / den;
                if (den < 0) t0 = MathF.Max(t0, t); else t1 = MathF.Min(t1, t);
                if (t0 > t1) hit = false;
            }
            if (hit) return i;
        }
        return -1;
    }

    /// <summary>Brushes inteiros dentro da caixa (coordenadas do jogo) — os de uma construção ficam dentro da caixa dela.</summary>
    public List<int> Inside(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        var r = new List<int>();
        for (int i = 0; i < _at.Length; i++)
        {
            var b = Box(i);
            if (b.X0 >= x0 && b.Y0 >= y0 && b.Z0 >= z0 && b.X1 <= x1 && b.Y1 <= y1 && b.Z1 <= z1) r.Add(i);
        }
        return r;
    }

    /// <summary>Desloca os brushes (caixa toda + dist de cada lado += normal·d). Sem girar: girar exige refazer os planos.</summary>
    public void Move(IEnumerable<int> brushes, float dx, float dy, float dz)
    {
        foreach (int i in brushes) MoveAt(_data, _at[i], dx, dy, dz);
    }

    /// <summary>Desloca 1 brush que começa em <paramref name="at"/> (mesmo formato no map.bht e no fim do .bmd do cliente).</summary>
    public static void MoveAt(byte[] d, int at, float dx, float dy, float dz)
    {
        float[] t = { dx, dy, dz };
        for (int v = 0; v < 3; v++)
            foreach (int k in new[] { 0, 24, 36 })   // centro, mínimo, máximo (a meia-extensão não muda)
                SetF(d, at + k + v * 4, F(d, at + k + v * 4) + t[v]);
        int sides = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at + 52));
        for (int s = 0, p = at + 56; s < sides; s++, p += 17)
            SetF(d, p + 12, F(d, p + 12) + F(d, p) * dx + F(d, p + 4) * dy + F(d, p + 8) * dz);
    }

    /// <summary>Chave da caixa de 1 brush (mínimo e máximo, 1 cm): a mesma no map.bht e no .bmd que gerou o brush.</summary>
    public static string KeyAt(byte[] d, int at) => string.Join(" ", Enumerable.Range(0, 6).Select(k => F(d, at + 24 + k * 4).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Brushes por chave da caixa.</summary>
    public Dictionary<string, List<int>> Keys()
    {
        var r = new Dictionary<string, List<int>>();
        for (int i = 0; i < _at.Length; i++)
        {
            var k = KeyAt(_data, _at[i]);
            if (!r.TryGetValue(k, out var l)) r[k] = l = new();
            l.Add(i);
        }
        return r;
    }

    static float F(byte[] d, int o) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o));
    static void SetF(byte[] d, int o, float v) => BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(o), v);
    uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(o));
    int I(int o) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(o));
    float F(int o) => BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(o));
}
