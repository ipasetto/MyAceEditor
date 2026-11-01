using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Nomes dos territórios da guerra territorial (element\data\domain.data) e da guerra de nação (domain2.data,
/// domain2_cross.data), PW-KB §S6/§S32. O nome é fixo, 16 caracteres UTF-16 por território; o resto (polígono no mapa
/// GuildMap.dds, vizinhos, horários) não muda. Leitura como CCommon/globaldataman.cpp: o arquivo tem que acabar exatamente
/// no fim do último campo, senão é outro formato e nada é tocado.
/// </summary>
public sealed class DomainFile
{
    public const int NameChars = 16;
    public byte[] Bytes { get; }
    /// <summary>ID do território → posição do nome no arquivo.</summary>
    public IReadOnlyDictionary<int, int> NameAt => _nameAt;
    readonly Dictionary<int, int> _nameAt = new();

    DomainFile(byte[] b) => Bytes = b;

    public string Name(int id) => ReadName(Bytes, _nameAt[id]);

    static string ReadName(byte[] b, int at)
    {
        var s = Encoding.Unicode.GetString(b, at, NameChars * 2);
        int z = s.IndexOf('\0');
        return z < 0 ? s : s[..z];
    }

    /// <summary>Troca o nome (corta em 15 caracteres + \0, o resto zerado, como o editor oficial grava).</summary>
    public void SetName(int id, string name)
    {
        int at = _nameAt[id];
        if (name.Length > NameChars - 1) name = name[..(NameChars - 1)];
        Array.Clear(Bytes, at, NameChars * 2);
        Encoding.Unicode.GetBytes(name, 0, name.Length, Bytes, at);
    }

    /// <summary>domain2: true = domain2.data / domain2_cross.data (cabeçalho timestamp + velocidade e mais campos por território).</summary>
    public static DomainFile? Parse(byte[] b, bool domain2)
    {
        var f = new DomainFile(b);
        int p = 0;
        bool Need(int n) => p + n <= b.Length;
        int I() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
        try
        {
            if (domain2) { if (!Need(8)) return null; p += 8; }   // u32 timestamp, float speed
            else if (b.Length >= 4 && b[0] == 'M' && b[1] == 'O' && b[2] == 'X' && b[3] == 'B') p = 4;   // clientes 1.5.5+: "MOXB" antes da contagem (o fonte 1.5.3 não tem)
            if (!Need(4)) return null;
            int n = I();
            if (n < 0 || n > 10000) return null;
            for (int i = 0; i < n; i++)
            {
                int nameAt = p;
                if (!Need(NameChars * 2 + 4)) return null;
                p += NameChars * 2;
                int id = I();
                // domain: tipo, prêmio, centro (2 int) | domain2: pontos, tipo de guerra, nação, capital, mappos (4×3 float), centro
                int fixedRest = domain2 ? 4 * 4 + 4 * 12 + 8 : 4 * 2 + 8;
                if (!Need(fixedRest + 4)) return null;
                p += fixedRest;
                int verts = I(); if (verts < 0 || !Need(verts * 8 + 4)) return null; p += verts * 8;
                int faces = I(); if (faces < 0 || !Need(faces * 12 + 4)) return null; p += faces * 12;
                int nb = I(); if (nb < 0 || !Need(nb * 4)) return null; p += nb * 4;
                if (!f._nameAt.TryAdd(id, nameAt)) return null;   // ID repetido: não é o formato esperado
            }
            // horários de batalha: n + n × (dia, hora, minuto) + tempo máximo. O fonte 1.5.3 tem um bloco; os domain.data
            // "MOXB" (155 BR, PWI 1.5.5, 180 BR) têm dois seguidos — aceita blocos até o fim exato do arquivo
            int blocks = 0;
            do
            {
                if (!Need(4)) return null;
                int times = I(); if (times < 0 || !Need(times * 12 + 4)) return null;
                p += times * 12 + 4;
                blocks++;
            } while (p < b.Length && blocks < 8);
            return p == b.Length ? f : null;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static readonly (string File, bool Domain2)[] Files = { ("domain.data", false), ("domain2.data", true), ("domain2_cross.data", true) };

    /// <summary>
    /// Traduz pelo ID os nomes de <paramref name="target"/> com os de <paramref name="source"/> (mesmo território = mesmo ID).
    /// Devolve o arquivo novo e quantos nomes mudaram; null se um dos dois não é do formato.
    /// </summary>
    public static (byte[] Bytes, int Changed)? Translate(byte[] target, byte[] source, bool domain2)
    {
        if (Parse((byte[])target.Clone(), domain2) is not { } t || Parse(source, domain2) is not { } s) return null;
        int changed = 0;
        foreach (var id in t.NameAt.Keys)
        {
            if (!s.NameAt.ContainsKey(id)) continue;
            var name = s.Name(id);
            if (name.Length == 0 || name == t.Name(id)) continue;
            t.SetName(id, name); changed++;
        }
        return (t.Bytes, changed);
    }
}
