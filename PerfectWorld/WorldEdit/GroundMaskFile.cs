using System.IO.Compression;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// &lt;mapa&gt;_N.t2mk ("BKMT" v2): texturas do chão de um pedaço de 512 m, para o pincel de textura do terreno (estudo em
/// docs/superpowers/specs/2026-11-08-terreno-textura-estudo.md). Layout conferido: cabeçalho de 20 B (BKMT, versão, nÁreas
/// 16, nCamadas, ?), offsets[nÁreas], camadas de 28 B (textura na lista do .trn2, −1, lado da máscara 256, 1, escala u,
/// escala v, ?), e depois, área por área, o bloco (nUsadas + (camada, offset absoluto, tamanho)) seguido das máscaras dela
/// em ordem, sem folgas. Máscara = zlib de lado×lado bytes (1 m/px); a 1ª camada da área é a base (sem máscara).
/// Remontar na mesma ordem com as máscaras não mexidas byte a byte devolve o arquivo idêntico.
/// </summary>
public sealed class GroundMaskFile
{
    public sealed class Entry
    {
        public int Layer;          // índice na lista de camadas do arquivo
        public int RawOffset;      // offset original (vale só para entrada sem máscara)
        public byte[]? Zlib;       // máscara comprimida como está no arquivo (null = sem máscara)
    }

    byte[] _head = [];             // 20 B
    public List<byte[]> Layers { get; } = new();          // 28 B cada
    public List<List<Entry>> Areas { get; } = new();

    public static GroundMaskFile Read(byte[] d)
    {
        if (d.Length < 0x14 || BitConverter.ToUInt32(d, 0) != 0x544D4B42) throw new InvalidDataException(".t2mk: não é BKMT");
        var f = new GroundMaskFile { _head = d[..0x14] };
        int na = BitConverter.ToInt32(d, 8), nl = BitConverter.ToInt32(d, 12);
        if (na <= 0 || na > 1024 || nl < 0 || nl > 4096 || 0x14 + na * 4 + nl * 28 > d.Length) throw new InvalidDataException(".t2mk: cabeçalho fora do esperado");
        int lo = 0x14 + na * 4;
        for (int i = 0; i < nl; i++) f.Layers.Add(d[(lo + i * 28)..(lo + i * 28 + 28)]);
        for (int a = 0; a < na; a++)
        {
            int o = BitConverter.ToInt32(d, 0x14 + a * 4);
            int n = BitConverter.ToInt32(d, o);
            if (n < 0 || n > 64 || o + 4 + n * 12 > d.Length) throw new InvalidDataException($".t2mk: área {a} ilegível");
            var list = new List<Entry>();
            for (int i = 0; i < n; i++)
            {
                int li = BitConverter.ToInt32(d, o + 4 + i * 12), mo = BitConverter.ToInt32(d, o + 8 + i * 12), ms = BitConverter.ToInt32(d, o + 12 + i * 12);
                if (ms < 0 || ms > 0 && mo + ms > d.Length) throw new InvalidDataException($".t2mk: máscara da área {a} fora do arquivo");
                list.Add(new Entry { Layer = li, RawOffset = mo, Zlib = ms > 0 ? d[mo..(mo + ms)] : null });
            }
            f.Areas.Add(list);
        }
        return f;
    }

    /// <summary>Remonta: cabeçalho, offsets, camadas; cada área = bloco + máscaras dela.</summary>
    public byte[] Write()
    {
        var head = (byte[])_head.Clone();
        BitConverter.GetBytes(Areas.Count).CopyTo(head, 8);
        BitConverter.GetBytes(Layers.Count).CopyTo(head, 12);
        var o = new List<byte>(head);
        int offTable = o.Count;
        o.AddRange(new byte[Areas.Count * 4]);
        foreach (var l in Layers) o.AddRange(l);
        var span = new List<(int Pos, int Value)>();
        for (int a = 0; a < Areas.Count; a++)
        {
            var list = Areas[a];
            span.Add((offTable + a * 4, o.Count));
            int block = o.Count;
            o.AddRange(BitConverter.GetBytes(list.Count));
            o.AddRange(new byte[list.Count * 12]);
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                int mo = e.Zlib is { Length: > 0 } ? o.Count : e.RawOffset, ms = e.Zlib?.Length ?? 0;
                if (e.Zlib is { Length: > 0 } z) o.AddRange(z);
                span.Add((block + 4 + i * 12, e.Layer)); span.Add((block + 8 + i * 12, mo)); span.Add((block + 12 + i * 12, ms));
            }
        }
        var arr = o.ToArray();
        foreach (var (p, v) in span) BitConverter.GetBytes(v).CopyTo(arr, p);
        return arr;
    }

    /// <summary>Máscara aberta (lado×lado bytes) de uma entrada; null = sem máscara (camada base).</summary>
    public byte[]? Mask(int area, int entry, int side = 256)
    {
        var z = Areas[area][entry].Zlib;
        if (z is null || z.Length <= 2) return null;
        using var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress);
        var m = new byte[side * side]; int got = 0, r;
        while (got < m.Length && (r = ds.Read(m, got, m.Length - got)) > 0) got += r;
        return m;
    }

    /// <summary>Troca a máscara de uma entrada (comprime como o jogo: zlib).</summary>
    public void SetMask(int area, int entry, byte[] mask)
    {
        using var ms = new MemoryStream();
        using (var zs = new ZLibStream(ms, CompressionLevel.Fastest, true)) zs.Write(mask);
        Areas[area][entry].Zlib = ms.ToArray();
    }
}
