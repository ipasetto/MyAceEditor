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
        var strings = ExeStrings(exe);
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
    /// Registra as janelas nos .dcf, de uma vez (PckWriter: um backup do .pck): nos dois de jogo sempre, e nos de login
    /// quando eles já usam a pasta do XML (ex.: faces\, as janelas de aparência da criação de personagem). O caminho vai
    /// relativo a interfaces\ (faces\chooseear.xml); tira as linhas que a versão antiga desta correção gravou só com o
    /// nome do arquivo, que o jogo não acha.
    /// </summary>
    public static void Fix(string clientElement, List<Missing> missing)
    {
        static string Rel(string pckPath) => pckPath[@"interfaces\".Length..];
        static string Dir(string rel) => Path.GetDirectoryName(rel.StartsWith(@"version01\", StringComparison.OrdinalIgnoreCase) ? rel[@"version01\".Length..] : rel) ?? "";
        var pck = Path.Combine(clientElement, "interfaces.pck");
        var changed = new Dictionary<string, byte[]>();
        var gbk = Encoding.GetEncoding(936);
        using (var a = new PckArchive(pck))
        {
            var all = a.Entries.Select(x => x.Path.ToLowerInvariant()).ToHashSet();
            foreach (var dcf in Dcfs.Concat(LoginDcfs))
            {
                if (a.Find(dcf) is not { } e) continue;
                var old = a.Read(e);
                var files = DcfFiles(old).ToList();
                var have = files.Select(f => f.ToLowerInvariant()).ToHashSet();
                bool v1 = dcf.EndsWith("-v1.dcf"), login = LoginDcfs.Contains(dcf);
                // linhas antigas sem a subpasta: o arquivo não existe em interfaces\ (só em interfaces\faces\…)
                var wrong = missing.Where(x => Path.GetDirectoryName(Rel(x.File)) is { Length: > 0 })
                    .Select(x => Path.GetFileName(x.File).ToLowerInvariant())
                    .Where(n => have.Contains(n) && !all.Contains(@"interfaces\" + n)).ToHashSet();
                var add = new StringBuilder();
                foreach (var x in missing)
                {
                    var rel = Rel(x.File);
                    if (login && !files.Any(f => Dir(f).Equals(Path.GetDirectoryName(rel) ?? "", StringComparison.OrdinalIgnoreCase))) continue;
                    // -v1 usa a cópia de Version01 quando o pacote tem
                    if (v1 && all.Contains((@"interfaces\version01\" + rel).ToLowerInvariant())) rel = @"Version01\" + rel;
                    if (have.Contains(rel.ToLowerInvariant())) continue;
                    add.Append(rel).Append("\t-1\t-1\t0\r\n");
                }
                if (add.Length == 0 && wrong.Count == 0) continue;
                var text = gbk.GetString(old);
                if (wrong.Count > 0)
                    text = string.Join('\n', text.Split('\n')
                        .Where(l => !wrong.Contains(l.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "")));
                if (add.Length > 0) text += (text.Length > 0 && !text.EndsWith('\n') ? "\r\n" : "") + add;
                changed[dcf] = gbk.GetBytes(text);
            }
        }
        if (changed.Count > 0) PckWriter.Write(pck, changed);
    }

    static HashSet<string> ExeStrings(string exe) =>
        Regex.Matches(Encoding.Latin1.GetString(File.ReadAllBytes(exe)), @"(?<=\0)[\x20-\x7e]{4,}(?=\0)").Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nomes de janela (Win_...) que o elementclient.exe procura; vazio sem executável.</summary>
    public static HashSet<string> ExeWindows(string clientElement)
    {
        var exe = Path.Combine(clientElement, "elementclient.exe");
        return File.Exists(exe) ? ExeStrings(exe).Where(s => s.StartsWith("Win_") && Regex.IsMatch(s, @"^Win_[A-Za-z0-9_]+$")).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Textos ASCII do elementclient.exe (nomes de janela e de controle que o código procura); vazio sem executável.</summary>
    public static HashSet<string> ExeNames(string clientElement)
    {
        var exe = Path.Combine(clientElement, "elementclient.exe");
        return File.Exists(exe) ? ExeStrings(exe) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nome da janela (DIALOG Name) de um XML.</summary>
    public static string? DialogNameOf(byte[] xml) => DialogName(xml);

    /// <summary>
    /// Linha de um .dcf: "arquivo x y mostrar" (PW-KB §S20b). x/y -1 = centralizada; outros negativos = medida a partir da
    /// direita/de baixo [I]; mostrar 1 = a janela abre ao entrar (chat, mapa, barras).
    /// </summary>
    public sealed record DcfLine(string Dcf, string File, int X, int Y, bool Show);

    /// <summary>Onde cada janela do pacote é carregada: caminho no pacote (interfaces\...) → linhas dos .dcf que a citam.</summary>
    public static Dictionary<string, List<DcfLine>> Layout(PckArchive a)
    {
        var map = new Dictionary<string, List<DcfLine>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dcf in Dcfs.Concat(LoginDcfs))
        {
            if (a.Find(dcf) is not { } e) continue;
            foreach (var l in Encoding.GetEncoding(936).GetString(a.Read(e)).Split('\n'))
            {
                var p = l.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0 || !p[0].EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                static int I(string[] p, int i) => p.Length > i && int.TryParse(p[i], out var v) ? v : -1;
                var path = @"interfaces\" + p[0];
                if (!map.TryGetValue(path, out var list)) map[path] = list = new();
                list.Add(new DcfLine(Path.GetFileName(dcf), p[0], I(p, 1), I(p, 2), I(p, 3) == 1));
            }
        }
        return map;
    }

    /// <summary>Arquivos listados num .dcf (1ª coluna de cada linha).</summary>
    static IEnumerable<string> DcfFiles(byte[] dcf) =>
        Encoding.GetEncoding(936).GetString(dcf).Split('\n')
            .Select(l => l.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(f => f is not null && f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))!;

    static string? DialogName(byte[] b)
    {
        var t = b.Length >= 2 && (b[0] == 0xFF && b[1] == 0xFE || b[1] == 0) ? Encoding.Unicode.GetString(b) : Encoding.GetEncoding(936).GetString(b);
        var m = Regex.Match(t, @"<DIALOG\b[^>]*?\bName=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
