using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Posição, direção (frente), cima e escala de um objeto, no espaço do jogo (Y para cima).</summary>
public readonly record struct ObjTransform(Vector3 Pos, Vector3 Dir, Vector3 Up, float Scale);

/// <summary>
/// Leitura e escrita dos campos de lugar dos objetos do mundo (PW-KB §S6 colisão, §S17 .ecwld/.ecbsd):
/// cabeçalho do .bmd (escala +8, frente +0x14, cima +0x20, posição +0x2C; +5 na versão 0x80000001), brushes de colisão
/// (mesmo formato no fim do .bmd e no map.bht) e itens de modelo EC do .ecwld (posição +4, frente +16, cima +28;
/// a escala só existe na versão 17+, que não é a do 1.5.5).
/// </summary>
public static class ObjectFormats
{
    static int Shift(byte[] bmd) => BitConverter.ToUInt32(bmd, 4) == 0x80000001 ? 5 : 0;
    static Vector3 V(byte[] d, int at) => new(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));
    static void W(byte[] d, int at, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(at), v.X);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(at + 4), v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(at + 8), v.Z);
    }

    public static ObjTransform ReadBmd(byte[] bmd)
    {
        if (bmd.Length < 0x3C + 5 || Encoding.ASCII.GetString(bmd, 0, 4) != "MOXB") throw new InvalidDataException(".bmd: cabeçalho não é MOXB");
        int sh = Shift(bmd);
        return new ObjTransform(V(bmd, 0x2C + sh), V(bmd, 0x14 + sh), V(bmd, 0x20 + sh), BitConverter.ToSingle(bmd, 8 + sh));
    }

    /// <summary>Cópia do .bmd com o lugar trocado só no cabeçalho (malhas e brushes iguais).</summary>
    public static byte[] WithBmdTransform(byte[] bmd, ObjTransform t)
    {
        _ = ReadBmd(bmd);
        var b = (byte[])bmd.Clone();
        int sh = Shift(b);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8 + sh), t.Scale);
        W(b, 0x14 + sh, t.Dir); W(b, 0x20 + sh, t.Up); W(b, 0x2C + sh, t.Pos);
        return b;
    }

    /// <summary>Centro do desenho no mundo (caixa dos vértices); sem malhas, a posição do cabeçalho.</summary>
    public static Vector3 BmdCenter(byte[] bmd)
    {
        var m = BmdModel.Read(bmd);
        var v = m is null ? [] : BmdModel.WorldVertices(m);
        if (v.Count == 0) return ReadBmd(bmd).Pos;
        var mn = new Vector3(v.Min(p => p[0]), v.Min(p => p[1]), v.Min(p => p[2]));
        var mx = new Vector3(v.Max(p => p[0]), v.Max(p => p[1]), v.Max(p => p[2]));
        return (mn + mx) / 2;
    }

    /// <summary>
    /// Gira o .bmd em torno de <paramref name="center"/>: frente, cima e posição do cabeçalho e os brushes de colisão do
    /// fim. Só giro em torno do eixo vertical é usado pela ferramenta (os planos "bevel" verticais continuam alinhados).
    /// </summary>
    public static byte[] RotateBmd(byte[] bmd, Quaternion rot, Vector3 center)
    {
        var t = ReadBmd(bmd);
        var at = BmdModel.CdBrushes(bmd) ?? throw new InvalidDataException(".bmd: colisão não lida; giro recusado");
        var b = WithBmdTransform(bmd, t with
        {
            Pos = Vector3.Transform(t.Pos - center, rot) + center,
            Dir = Vector3.Transform(t.Dir, rot),
            Up = Vector3.Transform(t.Up, rot),
        });
        foreach (int o in at) RotateBrush(b, o, rot, center);
        if (BmdModel.Read(b) is null || BmdModel.CdBrushes(b)?.Count != at.Count) throw new InvalidDataException(".bmd: a releitura depois do giro não bateu");
        return b;
    }

    public static (Vector3 Min, Vector3 Max) BrushBox(byte[] d, int at) => (V(d, at + 24), V(d, at + 36));

    /// <summary>
    /// Gira 1 brush (caixa A3DAABB 48 B + reservado 4 + nº de lados + lados de 17 B: normal, dist, bevel) em torno de
    /// <paramref name="c"/>: x' = R(x − c) + c, então n' = R·n e dist' = dist − n·c + n'·c. A caixa nova sai dos 8 cantos
    /// da caixa velha girados: o .bmd e o map.bht têm a MESMA caixa (é a chave que liga um ao outro) mas os planos em
    /// ordem diferente, então refazer pelos planos dava caixas com 1e-5 de diferença e a chave de 1 cm quebrava (world,
    /// 07/11). Em giro que não é múltiplo de 90° a caixa fica um pouco maior (só a busca rápida do gs usa a caixa).
    /// </summary>
    public static void RotateBrush(byte[] d, int at, Quaternion rot, Vector3 c)
    {
        int sides = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at + 52));
        for (int s = 0, p = at + 56; s < sides; s++, p += 17)
        {
            var n = V(d, p);
            float dist = BitConverter.ToSingle(d, p + 12);
            var n2 = Vector3.Transform(n, rot);
            W(d, p, n2); BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(p + 12), dist - Vector3.Dot(n, c) + Vector3.Dot(n2, c));
        }
        var (oldMin, oldMax) = BrushBox(d, at);
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        for (int k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) == 0 ? oldMin.X : oldMax.X, (k & 2) == 0 ? oldMin.Y : oldMax.Y, (k & 4) == 0 ? oldMin.Z : oldMax.Z);
            var q = Vector3.Transform(corner - c, rot) + c;
            mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
        }
        W(d, at, (mn + mx) / 2); W(d, at + 12, (mx - mn) / 2); W(d, at + 24, mn); W(d, at + 36, mx);
    }

    /// <summary>
    /// ID do ornamento (referência de 8 B no bloco do .ecwld: ID, offset no .ecbsd) que aponta para o .bmd: no offset há
    /// ECBSDFILEORNAMENT (40 B) + i32 tamanho + "pedaço\litmodel_N.lmd/.bmd" (EC_Ornament.cpp; o cliente carrega a
    /// construção por essa referência, CECSceneBlock::LoadOrnaments).
    /// </summary>
    public static uint? OrnamentIdOf(EcwldFile ecw, byte[] ecbsd, string bmdPath)
    {
        var parts = bmdPath.Split('\\');
        if (parts.Length < 2) return null;
        string want = parts[^2] + "\\" + Path.GetFileNameWithoutExtension(parts[^1]);
        foreach (var bl in ecw.Blocks)
            foreach (var it in bl.Items[(int)EcwldFile.Kind.Ornament])
            {
                if (it.Length < 8) continue;
                int off = BitConverter.ToInt32(it, 4);
                if (off < 0 || off + 44 > ecbsd.Length) continue;
                int len = BitConverter.ToInt32(ecbsd, off + 40);
                if (len <= 0 || len > 512 || off + 44 + len > ecbsd.Length) continue;
                var name = Encoding.ASCII.GetString(ecbsd, off + 44, len).TrimEnd('\0');
                int dot = name.LastIndexOf('.');
                if ((dot > 0 ? name[..dot] : name).Equals(want, StringComparison.OrdinalIgnoreCase)) return EcwldFile.IdOf(it);
            }
        return null;
    }

    public static ObjTransform ReadEcModel(byte[] item) =>
        item.Length < 40 ? throw new InvalidDataException("modelo do .ecwld menor que 40 bytes") : new(V(item, 4), V(item, 16), V(item, 28), 1);

    /// <summary>Escreve posição, frente e cima no item do .ecwld (a escala do 1.5.5 não existe no arquivo e é ignorada).</summary>
    public static void WriteEcModel(byte[] item, ObjTransform t)
    {
        if (item.Length < 40) throw new InvalidDataException("modelo do .ecwld menor que 40 bytes");
        W(item, 4, t.Pos); W(item, 16, t.Dir); W(item, 28, t.Up);
    }
}
