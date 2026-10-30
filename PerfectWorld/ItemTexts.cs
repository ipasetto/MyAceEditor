using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Gravação da cor do nome (configs\item_color.txt) e da descrição (configs\item_ext_desc.txt) de um item, dentro do
/// configs.pck (PW-KB §S2). Só a linha do item muda; o resto do arquivo sai byte a byte igual (mesma codificação, BOM e
/// quebra de linha). Antes de editar confere o round-trip: arquivo que não volta idêntico ao ser relido não é tocado.
///  - item_color.txt: "ID&lt;TAB&gt;índice" (GBK/ASCII, CRLF); índice = GameConfigs.NameColors; sem linha = cor padrão.
///  - item_ext_desc.txt: UTF-16 com BOM, "ID  \"texto\"" (^RRGGBB muda a cor, \r literal quebra a linha); algumas
///    entradas continuam na linha seguinte até fechar as aspas.
/// </summary>
public static class ItemTexts
{
    public const string ColorFile = @"configs\item_color.txt", DescFile = @"configs\item_ext_desc.txt";

    /// <summary>Texto do arquivo e como gravá-lo de volta; null = não volta idêntico (não editar).</summary>
    public static (string Text, Func<string, byte[]> Encode)? Decode(byte[] b)
    {
        Func<string, byte[]> enc;
        string text;
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(b, 2, b.Length - 2);
            enc = t => new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(t)).ToArray();
        }
        else
        {
            var gbk = Encoding.GetEncoding(936);
            text = gbk.GetString(b);
            enc = gbk.GetBytes;
        }
        return enc(text).AsSpan().SequenceEqual(b) ? (text, enc) : null;
    }

    static string NewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    static string Append(string text, string line)
    {
        var nl = NewLine(text);
        return text + (text.Length > 0 && !text.EndsWith('\n') ? nl : "") + line + nl;
    }

    /// <summary>Troca (ou tira, com null) a cor do nome do item.</summary>
    public static string SetColor(string text, int id, int? index)
    {
        var m = Regex.Match(text, $@"(?m)^{id}([ \t]+)\d+[ \t]*(\r?\n|$)");
        if (index is null) return m.Success ? text.Remove(m.Index, m.Length) : text;
        if (!m.Success) return Append(text, $"{id}\t{index}");
        return text[..m.Index] + $"{id}{m.Groups[1].Value}{index}{m.Groups[2].Value}" + text[(m.Index + m.Length)..];
    }

    // início "ID<espaços>\"" até a aspa que fecha no fim de uma linha (a descrição pode ocupar mais de uma linha)
    static Match FindDesc(string text, int id) => Regex.Match(text, $"(?ms)^{id}([ \\t]+)\"(.*?)\"[ \\t]*(\\r?\\n|\\z)");

    /// <summary>Troca (ou tira, com null) a descrição do item. Quebra de linha vira o \r literal do jogo.</summary>
    public static string SetDesc(string text, int id, string? desc)
    {
        var m = FindDesc(text, id);
        if (desc is null) return m.Success ? text.Remove(m.Index, m.Length) : text;
        desc = desc.Replace("\r\n", "\\r").Replace("\n", "\\r").Replace("\r", "\\r").Replace("\"", "”");
        if (!m.Success) return Append(text, $"{id}  \"{desc}\"");
        var end = m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : "";
        return text[..m.Index] + $"{id}{m.Groups[1].Value}\"{desc}\"{end}" + text[(m.Index + m.Length)..];
    }

    /// <summary>Todas as descrições (ID → texto), inclusive as de mais de uma linha.</summary>
    public static Dictionary<int, string> ReadDescs(string text)
    {
        var d = new Dictionary<int, string>();
        foreach (Match m in Regex.Matches(text, "(?ms)^(\\d+)[ \\t]+\"(.*?)\"[ \\t]*(?:\\r?\\n|\\z)"))
            d[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Replace("\r", "").Replace("\n", "");
        return d;
    }

    /// <summary>
    /// Grava no configs.pck a cor e a descrição atuais dos itens pedidos (null = sem linha). Devolve o backup do pacote.
    /// </summary>
    public static string? Save(string configsPck, IReadOnlyDictionary<int, int?> colors, IReadOnlyDictionary<int, string?> descs)
    {
        var files = new Dictionary<string, byte[]>();
        using (var pck = new PckArchive(configsPck))
        {
            byte[] Patch(string path, Func<string, string> edit)
            {
                var e = pck.Find(path) ?? throw new InvalidOperationException(string.Format(UI.Loc.T("itx.missing", "{0} não existe neste configs.pck."), path));
                var dec = Decode(pck.Read(e)) ?? throw new InvalidOperationException(string.Format(UI.Loc.T("itx.noRoundTrip", "{0} não volta idêntico ao ser relido: não vou gravar nele."), path));
                return dec.Encode(edit(dec.Text));
            }
            if (colors.Count > 0) files[ColorFile] = Patch(ColorFile, t => colors.Aggregate(t, (acc, kv) => SetColor(acc, kv.Key, kv.Value)));
            if (descs.Count > 0) files[DescFile] = Patch(DescFile, t => descs.Aggregate(t, (acc, kv) => SetDesc(acc, kv.Key, kv.Value)));
        }
        return files.Count == 0 ? null : PckWriter.Write(configsPck, files);
    }
}
