using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Textos de help\script.pck entre clientes de versões diferentes. Os .script têm comandos que o executável tem de conhecer
/// (trocar o arquivo inteiro já derrubou cliente, AFAZERES 02/11), então só entram os textos: #name, #steps e #define NOME "...",
/// casados pelo número do arquivo e pelo nome. Comando nenhum muda.
/// </summary>
public static class HelpScripts
{
    static readonly Regex Text = new(@"^(?<k>#name|#steps|#define[ \t]+\S+)[ \t]+""(?<v>.*)""[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    static string Number(string path) => Path.GetFileName(path).Split('.')[0];
    static bool Chinese(string s) => s.Any(c => c is >= '⺀' and <= '鿿');

    /// <summary>Troca os textos deste script pelos do outro; null se nada muda.</summary>
    public static byte[]? Translate(byte[] mine, byte[] other, out int count)
    {
        count = 0;
        var utf8 = new UTF8Encoding(false);
        var theirs = new Dictionary<string, string>();
        foreach (Match m in Text.Matches(utf8.GetString(other))) theirs[Regex.Replace(m.Groups["k"].Value, @"\s+", " ")] = m.Groups["v"].Value;
        int n = 0;
        var res = Text.Replace(utf8.GetString(mine), m =>
        {
            var key = Regex.Replace(m.Groups["k"].Value, @"\s+", " ");
            if (!theirs.TryGetValue(key, out var v) || v == m.Groups["v"].Value || v.Length == 0 || Chinese(v) && !Chinese(m.Groups["v"].Value)) return m.Value;
            n++;
            int at = m.Groups["v"].Index - m.Index;
            return m.Value[..at] + v + m.Value[(at + m.Groups["v"].Length)..];
        });
        count = n;
        return n == 0 ? null : utf8.GetBytes(res);
    }

    /// <summary>Arquivos de help\script.pck a regravar e quantos textos mudam.</summary>
    public static (int Texts, Dictionary<string, byte[]> Files) Plan(string dstPck, string srcPck)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        using var dst = new PckArchive(dstPck);
        using var src = new PckArchive(srcPck);
        var theirs = src.Entries.Where(e => e.Path.EndsWith(".script", StringComparison.OrdinalIgnoreCase)).GroupBy(e => Number(e.Path)).ToDictionary(g => g.Key, g => g.First());
        foreach (var e in dst.Entries.Where(e => e.Path.EndsWith(".script", StringComparison.OrdinalIgnoreCase)))
            if (theirs.TryGetValue(Number(e.Path), out var o) && Translate(dst.Read(e), src.Read(o), out int n) is { } b) { files[e.Path] = b; total += n; }
        return (total, files);
    }
}
