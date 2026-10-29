namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Água do cliente (element\maps\&lt;mapa&gt;\bsdata\N.dat, A3DWaterArea): "MOXB", i32 versão, i32 ?, entradas, tabela de
/// n offsets + i32 n no fim. Entrada de água: u32 versão (0x1000xxxx), i32 área, u32 cor ARGB (versão &gt; 0x10000000),
/// f32 altura, i32 (versão ≥ 0x10002000), i32 colunas, i32 linhas, f32 lado da célula, f32 centro x/y/z, [1 B se
/// 0x10003000], colunas × linhas bytes por célula. Outras entradas do arquivo (dados compactados de outros objetos) são
/// ignoradas. Mapa sem água no cliente (a09: a lava é textura do chão) = lista vazia; sem pasta bsdata = null.
/// </summary>
public static class ClientWater
{
    public sealed record Area(int Argb, float Height, int Cols, int Rows, float Cell, float X, float Z);

    public static List<Area>? Read(string mapDir)
    {
        var dir = Path.Combine(mapDir, "bsdata");
        if (!Directory.Exists(dir)) return null;
        var list = new List<Area>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.dat"))
        {
            byte[] b;
            try { b = File.ReadAllBytes(f); } catch (IOException) { continue; }
            if (b.Length < 20 || BitConverter.ToUInt32(b, 0) != 0x42584F4D) continue;   // "MOXB"
            int n = BitConverter.ToInt32(b, b.Length - 4);
            long table = b.Length - 4 - 4L * n;
            if (n <= 0 || table < 12) continue;
            for (int i = 0; i < n; i++)
                if (Entry(b, BitConverter.ToInt32(b, (int)table + i * 4), (int)table) is { } a) list.Add(a);
        }
        return list;
    }

    static Area? Entry(byte[] b, int o, int end)
    {
        if (o < 12 || o + 44 > end) return null;
        uint ver = BitConverter.ToUInt32(b, o);
        if (ver < 0x10000000 || ver > 0x1000FFFF) return null;
        int p = o + 8;
        int argb = unchecked((int)0xFF4D5A9D);
        if (ver > 0x10000000) { argb = BitConverter.ToInt32(b, p); p += 4; }
        float h = BitConverter.ToSingle(b, p); p += 4;
        if (ver >= 0x10002000) p += 4;
        int cols = BitConverter.ToInt32(b, p), rows = BitConverter.ToInt32(b, p + 4);
        float cell = BitConverter.ToSingle(b, p + 8), x = BitConverter.ToSingle(b, p + 12), z = BitConverter.ToSingle(b, p + 20);
        if (cols <= 0 || rows <= 0 || cols > 4096 || rows > 4096 || !(cell > 0 && cell < 10000) || !float.IsFinite(h) || !float.IsFinite(x) || !float.IsFinite(z)) return null;
        return new Area(argb, h, cols, rows, cell, x, z);
    }
}
