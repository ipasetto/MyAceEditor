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
        foreach (int i in brushes)
        {
            int o = _at[i];
            for (int v = 0; v < 3; v++) { Add(o + v * 4, dx, dy, dz, v); Add(o + 24 + v * 4, dx, dy, dz, v); Add(o + 36 + v * 4, dx, dy, dz, v); }
            int sides = I(o + 52);
            for (int s = 0, p = o + 56; s < sides; s++, p += 17)
                SetF(p + 12, F(p + 12) + F(p) * dx + F(p + 4) * dy + F(p + 8) * dz);
        }
    }

    void Add(int at, float dx, float dy, float dz, int axis) => SetF(at, F(at) + (axis == 0 ? dx : axis == 1 ? dy : dz));
    uint U(int o) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(o));
    int I(int o) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(o));
    float F(int o) => BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(o));
    void SetF(int o, float v) => BinaryPrimitives.WriteSingleLittleEndian(_data.AsSpan(o), v);
}
