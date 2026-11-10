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

    public BhtFile(byte[] data) : this(data, clone: true) { }

    /// <summary>map.bht sem nenhum brush (mapa sem colisão no servidor): a passagem usa só a malha.</summary>
    public static BhtFile Empty()
    {
        var b = new List<byte>(); b.AddRange(BitConverter.GetBytes(Magic)); b.AddRange(BitConverter.GetBytes(1)); b.AddRange(BitConverter.GetBytes(0));
        b.AddRange(new byte[17]); b.Add(1);   // um nó folha
        return new BhtFile([.. b], clone: false);
    }

    /// <summary>clone: false usa o vetor direto (só leitura; map.bht do world tem ~195 MB, evita mais uma cópia).</summary>
    public BhtFile(byte[] data, bool clone)
    {
        _data = clone ? (byte[])data.Clone() : data;
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

    /// <summary>Brushes inteiros dentro da caixa (coordenadas do jogo) — os de uma construção ficam dentro da caixa dela.
    /// touching = os que só encostam/cruzam a caixa também (pré-filtro dos raios de sombra e da passagem).</summary>
    public List<int> Inside(float x0, float y0, float z0, float x1, float y1, float z1, bool touching = false)
    {
        var r = new List<int>();
        for (int i = 0; i < _at.Length; i++)
        {
            var b = Box(i);
            if (touching ? b.X1 >= x0 && b.X0 <= x1 && b.Y1 >= y0 && b.Y0 <= y1 && b.Z1 >= z0 && b.Z0 <= z1
                         : b.X0 >= x0 && b.Y0 >= y0 && b.Z0 >= z0 && b.X1 <= x1 && b.Y1 <= y1 && b.Z1 <= z1) r.Add(i);
        }
        return r;
    }

    /// <summary>O segmento a→b entra no brush i (sólido convexo; Cyrus-Beck)?</summary>
    public bool SegmentHitOne(int i, float ax, float ay, float az, float bx, float by, float bz) => SegmentEnter(i, ax, ay, az, bx, by, bz) is not null;

    /// <summary>Fração (0..1) do segmento a→b em que ele entra no brush i; null = não entra.</summary>
    public float? SegmentEnter(int i, float ax, float ay, float az, float bx, float by, float bz)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az;
        int o = _at[i], sides = I(o + 52);
        float t0 = 0, t1 = 1;
        for (int k = 0; k < sides; k++)
        {
            int p = o + 56 + 17 * k;
            float nx = F(p), ny = F(p + 4), nz = F(p + 8), dist = F(p + 12);
            float side0 = nx * ax + ny * ay + nz * az - dist, den = nx * dx + ny * dy + nz * dz;
            if (MathF.Abs(den) < 1e-6f) { if (side0 > 0) return null; continue; }
            float t = -side0 / den;
            if (den < 0) t0 = MathF.Max(t0, t); else t1 = MathF.Min(t1, t);
            if (t0 > t1) return null;
        }
        return t0;
    }

    /// <summary>Desloca os brushes (caixa toda + dist de cada lado += normal·d). Sem girar: girar exige refazer os planos.</summary>
    public void Move(IEnumerable<int> brushes, float dx, float dy, float dz)
    {
        foreach (int i in brushes) MoveAt(_data, _at[i], dx, dy, dz);
    }

    /// <summary>Escala os brushes em volta de <paramref name="center"/> (ObjectFormats.ScaleBrush).</summary>
    public void Scale(IEnumerable<int> brushes, float k, System.Numerics.Vector3 center)
    {
        foreach (int i in brushes) WorldEdit.ObjectFormats.ScaleBrush(_data, _at[i], k, center);
    }

    /// <summary>Gira os brushes em torno de <paramref name="center"/> (planos e caixa; ObjectFormats.RotateBrush).</summary>
    public void Rotate(IEnumerable<int> brushes, System.Numerics.Quaternion rot, System.Numerics.Vector3 center)
    {
        foreach (int i in brushes) WorldEdit.ObjectFormats.RotateBrush(_data, _at[i], rot, center);
    }

    /// <summary>Bytes de um brush (caixa + reservado + lados), no formato que o .bmd do cliente também usa.</summary>
    public byte[] BrushBytes(int i) => _data[_at[i]..(i + 1 < Count ? _at[i + 1] : TreeStart)];

    /// <summary>
    /// Arquivo novo com brushes acrescentados no fim da lista (antes da árvore): a árvore é grade fixa e os brushes entram
    /// nela na carga do gs, então acrescentar só muda a contagem e a lista.
    /// </summary>
    public byte[] With(IEnumerable<byte[]> brushes)
    {
        var add = brushes.ToList();
        var ms = new MemoryStream(_data.Length + add.Sum(b => b.Length));
        ms.Write(_data, 0, 8);
        ms.Write(BitConverter.GetBytes(Count + add.Count));
        ms.Write(_data, 12, TreeStart - 12);
        foreach (var b in add) ms.Write(b);
        ms.Write(_data, TreeStart, _data.Length - TreeStart);
        return ms.ToArray();
    }

    /// <summary>
    /// Arquivo novo sem os brushes pedidos: a árvore é uma grade fixa e os brushes entram nela só na carga do gs
    /// (_AddBrush), então tirar brushes só muda a contagem e a lista; a árvore é copiada igual.
    /// </summary>
    public byte[] Without(IEnumerable<int> brushes)
    {
        var drop = brushes.ToHashSet();
        var ms = new MemoryStream(_data.Length);
        ms.Write(_data, 0, 8);
        ms.Write(BitConverter.GetBytes(Count - drop.Count(i => i >= 0 && i < Count)));
        for (int i = 0; i < Count; i++)
        {
            if (drop.Contains(i)) continue;
            int end = i + 1 < Count ? _at[i + 1] : TreeStart;
            ms.Write(_data, _at[i], end - _at[i]);
        }
        ms.Write(_data, TreeStart, _data.Length - TreeStart);
        return ms.ToArray();
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

/// <summary>Brushes perto (pré-filtro do map.bht para os testes de parede da passagem dos NPCs).</summary>
public sealed class BrushSet(BhtFile bht, IReadOnlyList<int> ids)
{
    public int Count => ids.Count;
    public bool Hit(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        foreach (int i in ids)
        {
            var bb = bht.Box(i);
            float lx = MathF.Min(a.X, b.X), hx = MathF.Max(a.X, b.X), ly = MathF.Min(a.Y, b.Y), hy = MathF.Max(a.Y, b.Y), lz = MathF.Min(a.Z, b.Z), hz = MathF.Max(a.Z, b.Z);
            if (bb.X1 < lx || bb.X0 > hx || bb.Y1 < ly || bb.Y0 > hy || bb.Z1 < lz || bb.Z0 > hz) continue;
            if (bht.SegmentHitOne(i, a.X, a.Y, a.Z, b.X, b.Y, b.Z)) return true;
        }
        return false;
    }
    /// <summary>Primeiro ponto (fração 0..1) em que o segmento a→b entra em algum brush; null = nenhum.</summary>
    public float? FirstHit(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        float? best = null;
        foreach (int i in ids)
        {
            var bb = bht.Box(i);
            float lx = MathF.Min(a.X, b.X), hx = MathF.Max(a.X, b.X), ly = MathF.Min(a.Y, b.Y), hy = MathF.Max(a.Y, b.Y), lz = MathF.Min(a.Z, b.Z), hz = MathF.Max(a.Z, b.Z);
            if (bb.X1 < lx || bb.X0 > hx || bb.Y1 < ly || bb.Y0 > hy || bb.Z1 < lz || bb.Z0 > hz) continue;
            if (bht.SegmentEnter(i, a.X, a.Y, a.Z, b.X, b.Y, b.Z) is float t && (best is null || t < best)) best = t;
        }
        return best;
    }

    public static BrushSet Near(BhtFile bht, float x0, float z0, float x1, float z1) => new(bht, bht.Inside(x0, -1e6f, z0, x1, 1e6f, z1, touching: true));
}
