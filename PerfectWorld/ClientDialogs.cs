using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Janelas que o elementclient.exe procura pelo nome (GetDialog("Win_…") ao entrar no jogo) e que o interfaces.pck
/// tem em XML mas não lista no .dcf: o ponteiro fica nulo e o jogo cai na primeira vez que o código toca nela (ex.:
/// Win_Engrave ao fechar o inventário pelo X, PW-KB §S20). A correção registra esses XML nos .dcf do jogo.
/// </summary>
public static class ClientDialogs
{
    public sealed record Missing(string Dialog, string File);

    static readonly string[] Dcfs = { @"interfaces\ingame.dcf", @"interfaces\ingame-v1.dcf" };
    static readonly string[] LoginDcfs = { @"interfaces\loginui.dcf", @"interfaces\loginui-v1.dcf" };

    static ClientDialogs() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Janelas pedidas pelo executável, presentes no pacote e fora dos .dcf (vazio = nada a fazer).</summary>
    public static List<Missing> Find(string clientElement)
    {
        var exe = Path.Combine(clientElement, "elementclient.exe");
        var pck = Path.Combine(clientElement, "interfaces.pck");
        if (!File.Exists(exe) || !File.Exists(pck)) return new();
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(exe));
        var strings = Regex.Matches(text, @"(?<=\0)[\x20-\x7e]{4,}(?=\0)").Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = strings.Where(s => s.StartsWith("Win_") && Regex.IsMatch(s, @"^Win_[A-Za-z0-9_]+$")).ToHashSet();

        using var a = new PckArchive(pck);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dcf in Dcfs.Concat(LoginDcfs))
            if (a.Find(dcf) is { } e) foreach (var f in DcfFiles(a.Read(e))) listed.Add(@"interfaces\" + f);
        var loaded = new HashSet<string>();
        var spare = new Dictionary<string, string>();   // janela → XML do pacote que não está em nenhum .dcf
        foreach (var e in a.Entries.Where(e => e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.Path.StartsWith(@"interfaces\", StringComparison.OrdinalIgnoreCase)))
        {
            bool isListed = listed.Contains(e.Path);
            var name = DialogName(a.Read(e));
            if (name is null) continue;
            if (isListed) { loaded.Add(name); continue; }
            if (!wanted.Contains(name)) continue;
            // a cópia fora de Version01 tem preferência (o ingame.dcf aponta para a pasta interfaces\)
            else if (!spare.TryGetValue(name, out var cur) || cur.Contains(@"\version01\", StringComparison.OrdinalIgnoreCase)) spare[name] = e.Path;
        }
        // fora: o código abre o XML pelo nome (Loading.xml, CWindow.xml…), é modelo copiado em tempo de jogo
        // ("Win_FriendChat%d") ou o nome é só prefixo de janelas carregadas (Win_FittingRoom → Win_FittingRoomM)
        return spare.Where(kv => !loaded.Contains(kv.Key)
                && !strings.Contains(Path.GetFileName(kv.Value)) && !strings.Contains(kv.Key + "%d")
                && !loaded.Any(l => l.Length > kv.Key.Length && l.StartsWith(kv.Key, StringComparison.Ordinal)))
            .Select(kv => new Missing(kv.Key, kv.Value)).OrderBy(m => m.Dialog).ToList();
    }

    /// <summary>
    /// Acrescenta as janelas nos dois .dcf de jogo. O pacote só aceita troca do MESMO tamanho (PckPatch): os espaços
    /// repetidos entre as colunas viram um só, e a sobra no fim vira espaço. Backup do .pck a cada troca.
    /// </summary>
    public static void Fix(string clientElement, List<Missing> missing)
    {
        var pck = Path.Combine(clientElement, "interfaces.pck");
        foreach (var dcf in Dcfs)
        {
            byte[] old;
            HashSet<string> have;
            Func<string, bool> exists;
            using (var a = new PckArchive(pck))
            {
                if (a.Find(dcf) is not { } e) continue;
                old = a.Read(e);
                have = DcfFiles(old).Select(f => f.ToLowerInvariant()).ToHashSet();
                var all = a.Entries.Select(x => x.Path.ToLowerInvariant()).ToHashSet();
                exists = p => all.Contains(p.ToLowerInvariant());
            }
            bool v1 = dcf.EndsWith("-v1.dcf");
            var add = new StringBuilder();
            foreach (var m in missing)
            {
                var file = Path.GetFileName(m.File);
                // ingame-v1 usa a cópia de Version01 quando o pacote tem
                var rel = v1 && exists(@"interfaces\version01\" + file) ? @"Version01\" + file : file;
                if (have.Contains(rel.ToLowerInvariant())) continue;
                add.Append(rel).Append("\t-1\t-1\t0\r\n");
            }
            if (add.Length == 0) continue;
            var extra = Encoding.ASCII.GetBytes((old.Length > 0 && old[^1] != '\n' ? "\r\n" : "") + add);
            var packed = Compact(old);
            if (packed.Length + extra.Length > old.Length)
                throw new InvalidOperationException($"{dcf}: não há espaço para acrescentar {missing.Count} linha(s) sem refazer o pacote.");
            var neu = new byte[old.Length];
            packed.CopyTo(neu, 0);
            extra.CopyTo(neu, packed.Length);
            neu.AsSpan(packed.Length + extra.Length).Fill((byte)' ');
            PckPatch.ReplaceSameSize(pck, dcf, neu);
        }
    }

    /// <summary>Arquivos listados num .dcf (1ª coluna de cada linha).</summary>
    static IEnumerable<string> DcfFiles(byte[] dcf) =>
        Encoding.GetEncoding(936).GetString(dcf).Split('\n')
            .Select(l => l.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(f => f is not null && f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))!;

    // espaço/tab repetidos viram um (0x20/0x09 nunca aparecem dentro de um caractere GBK de 2 bytes)
    static byte[] Compact(byte[] b)
    {
        var o = new List<byte>(b.Length);
        foreach (var c in b)
        {
            bool blank = c is (byte)' ' or (byte)'\t';
            if (blank && o.Count > 0 && o[^1] is (byte)' ' or (byte)'\t') continue;
            o.Add(c);
        }
        return o.ToArray();
    }

    static string? DialogName(byte[] b)
    {
        var t = b.Length >= 2 && (b[0] == 0xFF && b[1] == 0xFE || b[1] == 0) ? Encoding.Unicode.GetString(b) : Encoding.GetEncoding(936).GetString(b);
        var m = Regex.Match(t, @"<DIALOG\b[^>]*?\bName=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
