using System.IO.Compression;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Altura do chão: &lt;mapa&gt;_N.t2bk do cliente ("BKBT" v5/v6: cabeçalho 16 B, offsets[256], blocos em sequência; bloco =
/// cabeçalho 32/36 B com i16 linha e coluna GLOBAIS e 4 tamanhos em +16, depois 4 partes zlib: 0 = alturas 33×33 floats em
/// metros, 1..3 = outros dados do bloco que o editor não mexe) e map\&lt;N&gt;.hmap do servidor (513×513 floats, metros / 800).
/// Regravar troca só a parte 0 dos blocos mexidos; o resto vai byte a byte (sem mudança = arquivo idêntico).
/// </summary>
public static class HeightFiles
{
    public static byte[] RewriteT2bk(byte[] d, Func<int, int, float[]?> heightsOf)
    {
        if (d.Length < 16 || BitConverter.ToUInt32(d, 0) != 0x54424B42) throw new InvalidDataException(".t2bk: não é BKBT");
        int ver = BitConverter.ToInt32(d, 4), count = BitConverter.ToInt32(d, 8);
        int head = ver > 5 ? 36 : 32;
        if (count <= 0 || count > 4096 || 16 + count * 4 > d.Length) throw new InvalidDataException(".t2bk: cabeçalho fora do esperado");
        var o = new List<byte>(d.Length + 1024);
        o.AddRange(d.AsSpan(0, 16).ToArray());
        int table = o.Count; o.AddRange(new byte[count * 4]);
        int prevEnd = 16 + count * 4;
        var offs = new int[count];
        for (int i = 0; i < count; i++)
        {
            int b = BitConverter.ToInt32(d, 16 + i * 4);
            if (b < prevEnd || b + head > d.Length) throw new InvalidDataException($".t2bk: bloco {i} fora do lugar esperado");
            int s0 = BitConverter.ToInt32(d, b + 16), s1 = BitConverter.ToInt32(d, b + 20), s2 = BitConverter.ToInt32(d, b + 24), s3 = BitConverter.ToInt32(d, b + 28);
            int len = head + s0 + s1 + s2 + s3;
            if (s0 < 0 || s1 < 0 || s2 < 0 || s3 < 0 || b + len > d.Length) throw new InvalidDataException($".t2bk: bloco {i} ilegível");
            if (b > prevEnd) o.AddRange(d.AsSpan(prevEnd, b - prevEnd).ToArray());   // folga (não há nos arquivos do 1.5.5; preserva)
            offs[i] = o.Count;
            int row = BitConverter.ToInt16(d, b + 4), col = BitConverter.ToInt16(d, b + 6);
            var h = heightsOf(row, col);
            if (h is null) o.AddRange(d.AsSpan(b, len).ToArray());
            else
            {
                var raw = new byte[h.Length * 4];
                Buffer.BlockCopy(h, 0, raw, 0, raw.Length);
                byte[] z;
                using (var ms = new MemoryStream()) { using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, true)) zs.Write(raw); z = ms.ToArray(); }
                var hb = d.AsSpan(b, head).ToArray();
                BitConverter.GetBytes(z.Length).CopyTo(hb, 16);
                o.AddRange(hb); o.AddRange(z); o.AddRange(d.AsSpan(b + head + s0, s1 + s2 + s3).ToArray());
            }
            prevEnd = b + len;
        }
        if (prevEnd < d.Length) o.AddRange(d.AsSpan(prevEnd).ToArray());
        var arr = o.ToArray();
        for (int i = 0; i < count; i++) BitConverter.GetBytes(offs[i]).CopyTo(arr, table + i * 4);
        return arr;
    }

    /// <summary>.hmap do servidor de um pedaço: 513×513 floats (metros / 800), linha 0 ao norte (conferido no teste).</summary>
    public static byte[] Hmap(TerrainFile t, int piece)
    {
        int n = t.CellsPerPiece + 1, pr = (piece - 1) / t.Cols, pc = (piece - 1) % t.Cols;
        var f = new float[n * n];
        for (int v = 0; v < n; v++)
            for (int u = 0; u < n; u++)
                f[v * n + u] = (t.Sample(pc * t.CellsPerPiece + u, pr * t.CellsPerPiece + v) ?? 0) / 800f;
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return b;
    }
}
