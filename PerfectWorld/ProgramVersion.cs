namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Versão do elements.data que um programa do jogo tem compilada (ELEMENTDATA_VERSION = 0x300000vv, exptypes.h).
/// O gs recusa um elements.data de outra versão (elementdataman::load_data) e cai; e o login compara a "edition" do
/// cliente com a do gs, que começa por essa constante (PW-KB §S17). Só leitura: procura a constante usada como
/// valor imediato de instrução x86, no formato de cada compilador:
///   elementclient.exe (MSVC, otimizado): "push imm32" (68) e "cmp eax, imm32" (3D) com o mesmo valor;
///   gs (gcc sem otimização): "mov [ebp+x], imm32" (C7 45/C7 85) e "cmp [ebp+x], imm32" (81 7D/81 BD).
/// Precisa das duas formas para aceitar; senão devolve null (não adivinha).
/// </summary>
public static class ProgramVersion
{
    static readonly Dictionary<string, (long Size, DateTime Time, int? Ver)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static int? ElementsVersion(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            lock (Cache)
                if (Cache.TryGetValue(path, out var c) && c.Size == fi.Length && c.Time == fi.LastWriteTimeUtc) return c.Ver;
            var d = File.ReadAllBytes(path);
            bool elf = d.Length > 4 && d[0] == 0x7F && d[1] == (byte)'E' && d[2] == (byte)'L' && d[3] == (byte)'F';
            var a = new HashSet<int>(); var b = new HashSet<int>();
            for (int i = 6; i + 3 < d.Length; i++)
            {
                if (d[i + 3] != 0x30 || d[i + 2] != 0 || d[i + 1] != 0 || d[i] == 0) continue;
                int v = d[i];
                if (elf)
                {
                    if ((d[i - 3] == 0xC7 && d[i - 2] == 0x45) || (d[i - 6] == 0xC7 && d[i - 5] == 0x85)) a.Add(v);
                    if ((d[i - 3] == 0x81 && d[i - 2] == 0x7D) || (d[i - 6] == 0x81 && d[i - 5] == 0xBD)) b.Add(v);
                }
                else
                {
                    if (d[i - 1] == 0x68) a.Add(v);
                    if (d[i - 1] == 0x3D) b.Add(v);
                }
            }
            var both = a.Intersect(b).ToList();
            int? ver = both.Count == 1 ? both[0] : null;
            lock (Cache) Cache[path] = (fi.Length, fi.LastWriteTimeUtc, ver);
            return ver;
        }
        catch { return null; }
    }

    /// <summary>Versão do jogo já confirmada para uma versão do elements (PW-KB §S17/§S30); "" quando não sabemos.</summary>
    public static string GameFor(int elementsVersion) => elementsVersion switch
    {
        60 => "1.4.4",      // pacote andrey.pw 1.4.4v60 + clientes 144
        127 => "1.5.2",     // fonte do cliente "153" (ExpTypes.h)
        128 => "1.5.3",     // fonte do servidor "153" (exptypes.h)
        _ => "",
    };
}
