using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Cena do mapa no cliente (maps\&lt;mapa&gt;\&lt;mapa&gt;.ecwld, "ECWD"): por enquanto só as árvores (tipo + posição) e a tabela
/// de tipos (.spt SpeedTree + mapa composto das folhas). Layout: PW-KB §S17 (CCommon/EC_WorldFile.h, EC_SceneBlock.cpp).
/// Leitura apenas; o arquivo não é regravado.
/// </summary>
public sealed class WorldScene
{
    public sealed record TreeType(string Spt, string CompositeMap);
    public readonly record struct Tree(int Type, float X, float Y, float Z, bool HasY);

    public int Version { get; }
    public float Width { get; }
    public float Length { get; }
    public List<TreeType> TreeTypes { get; } = new();
    public List<Tree> Trees { get; } = new();

    const uint Ident = ('E' << 24) | ('C' << 16) | ('W' << 8) | 'D';
    static readonly Encoding Gbk;
    static WorldScene() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public WorldScene(string path) : this(File.ReadAllBytes(path)) { }

    public WorldScene(byte[] b)
    {
        if (b.Length < 116 || BitConverter.ToUInt32(b, 0) != Ident) throw new InvalidDataException("não é um .ecwld");
        Version = BitConverter.ToInt32(b, 4);
        Width = BitConverter.ToSingle(b, 8); Length = BitConverter.ToSingle(b, 12);
        int nBlock = BitConverter.ToInt32(b, 24);
        uint treeOff = BitConverter.ToUInt32(b, 36);
        int nTreeType = BitConverter.ToInt32(b, 44);
        // cabeçalho = 56 B + 60 reservados; depois um u32 de offset por bloco
        int p = (int)treeOff;
        for (int i = 0; i < nTreeType; i++) TreeTypes.Add(new TreeType(Str(b, ref p), Str(b, ref p)));
        // bloco: u16 linha, u16 coluna, contagens (5 ints < v4, 8 ints < v7, 9 < v9, 10 a partir da v9); árvores vêm primeiro
        int counts = Version < 4 ? 5 : Version < 7 ? 8 : Version < 9 ? 9 : 10;
        bool hasY = Version >= 5;
        int rec = hasY ? 16 : 12;
        for (int i = 0; i < nBlock; i++)
        {
            int o = (int)BitConverter.ToUInt32(b, 116 + 4 * i);
            if (o <= 0 || o + 4 + 4 * counts > b.Length) continue;
            int n = BitConverter.ToInt32(b, o + 4);
            int t = o + 4 + 4 * counts;
            if (n <= 0 || t + (long)n * rec > b.Length) continue;
            for (int k = 0; k < n; k++, t += rec)
                Trees.Add(hasY
                    ? new Tree(BitConverter.ToInt32(b, t), BitConverter.ToSingle(b, t + 4), BitConverter.ToSingle(b, t + 8), BitConverter.ToSingle(b, t + 12), true)
                    : new Tree(BitConverter.ToInt32(b, t), BitConverter.ToSingle(b, t + 4), 0, BitConverter.ToSingle(b, t + 8), false));
        }
    }

    static string Str(byte[] b, ref int p)
    {
        int n = BitConverter.ToInt32(b, p); p += 4;
        var s = Gbk.GetString(b, p, n); p += n;
        return s.TrimEnd('\0');
    }
}
