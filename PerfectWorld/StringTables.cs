using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Tabela de textos dos pacotes do cliente (UTF-16 com BOM): .txt do configs.pck (depois de "#_begin": com "#_index",
/// entradas <c>id  "texto"</c>; sem, só <c>"texto"</c> e vale a posição) e .stf do interfaces.pck (<c>índice&lt;TAB&gt;"texto"</c>).
/// O texto vai até a aspa que fecha a linha (pode ter várias linhas). Regravar troca só o miolo das entradas pedidas;
/// comentários, espaços e quebras de linha ficam como estão.
/// </summary>
public sealed class StringTable
{
    public sealed record Entry(int Key, int Start, int Length, string Value);

    public List<Entry> Entries { get; } = new();
    string _text = "";

    public static StringTable? Parse(byte[] b)
    {
        if (b.Length < 2 || b[0] != 0xFF || b[1] != 0xFE) return null;
        var t = new StringTable { _text = Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1) };
        var s = t._text;
        int p = s.IndexOf("#_begin", StringComparison.Ordinal);
        p = p < 0 ? 0 : p + "#_begin".Length;
        int ordinal = 0;
        while (true)
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            if (p >= s.Length) break;
            if (s[p] == '/' && p + 1 < s.Length && s[p + 1] == '/') { p = s.IndexOf('\n', p); if (p < 0) break; continue; }   // comentário
            int key = ordinal;
            if (char.IsDigit(s[p]))
            {
                int k0 = p; while (p < s.Length && char.IsDigit(s[p])) p++;
                if (!int.TryParse(s.AsSpan(k0, p - k0), out key)) return null;
                while (p < s.Length && s[p] is ' ' or '\t') p++;
            }
            if (p >= s.Length || s[p] != '"') return null;   // formato inesperado: não é tabela de textos
            int start = ++p, end = -1;
            for (int q = start; q < s.Length; q++)
            {
                if (s[q] != '"' || s[q - 1] == '\\') continue;
                int r = q + 1; while (r < s.Length && s[r] is ' ' or '\t' or '\r') r++;
                if (r >= s.Length || s[r] == '\n') { end = q; break; }
            }
            if (end < 0) return null;
            t.Entries.Add(new Entry(key, start, end - start, s[start..end]));
            p = end + 1; ordinal++;
        }
        return t;
    }

    /// <summary>Bytes (UTF-16 com BOM) com os textos trocados: entrada (pela posição em <see cref="Entries"/>) → texto novo.</summary>
    public byte[] Encode(IReadOnlyDictionary<int, string> changes)
    {
        var sb = new StringBuilder(_text.Length + 1024);
        int at = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (!changes.TryGetValue(i, out var v)) continue;
            var e = Entries[i];
            sb.Append(_text, at, e.Start - at).Append(v);
            at = e.Start + e.Length;
        }
        sb.Append(_text, at, _text.Length - at);
        var body = Encoding.Unicode.GetBytes(sb.ToString());
        var o = new byte[body.Length + 2]; o[0] = 0xFF; o[1] = 0xFE; body.CopyTo(o, 2);
        return o;
    }
}

/// <summary>
/// "Traduzir a partir de outro cliente", pacotes de texto (configs.pck e interfaces.pck), em qualquer versão: casa cada
/// tabela pelo caminho dentro do pacote e cada texto pelo ID (ou posição); só troca quando os marcadores de formato
/// (%d, %s, %.1f…) são os mesmos — senão o jogo pode cair. Não mexe em fontes (###) nem na lista de palavras proibidas.
/// O que só existe num dos lados fica como está. Grava tudo de uma vez (PckWriter: backup do .pck).
/// </summary>
public static class PackTextTranslator
{
    static readonly Regex Format = new(@"%[-+ #0-9.]*[a-zA-Z%]", RegexOptions.CultureInvariant);
    static readonly string[] Skip = { "badwords.txt", "instance.txt" };   // instance.txt tem tradutor próprio (InstanceNames)

    static bool IsTable(string path) =>
        (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".stf", StringComparison.OrdinalIgnoreCase))
        && !Skip.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    static bool Translatable(string mine, string other) =>
        other.Length > 0 && other != mine && !mine.Contains("###") && !other.Contains("###")
        && Format.Matches(mine).Select(m => m.Value).SequenceEqual(Format.Matches(other).Select(m => m.Value));

    // janelas do interfaces.pck (texto e dica de cada controle; PW-KB §S20c)
    static bool IsWindow(string path) =>
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && path.StartsWith(@"interfaces\", StringComparison.OrdinalIgnoreCase);

    static readonly Regex Cjk = new(@"[㐀-鿿豈-﫿]", RegexOptions.CultureInvariant);

    // texto em chinês só substitui texto em chinês: um cliente parcialmente traduzido não desfaz a tradução do destino
    static bool WindowTranslatable(string mine, string other) =>
        Translatable(mine, other) && (Cjk.IsMatch(mine) || !Cjk.IsMatch(other));

    /// <summary>Textos que mudam e os arquivos novos do pacote (vazio na simulação: só conta).</summary>
    public static (int Count, Dictionary<string, byte[]> Files) Plan(string dstPck, string srcPck, bool dryRun)
    {
        var (tables, windows, files) = PlanDetailed(dstPck, srcPck, dryRun);
        return (tables + windows, files);
    }

    /// <summary>Como <see cref="Plan"/>, separando os textos das tabelas (.txt/.stf) e os das janelas (.xml).</summary>
    public static (int Tables, int Windows, Dictionary<string, byte[]> Files) PlanDetailed(string dstPck, string srcPck, bool dryRun)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int total = 0, windows = 0;
        using var dst = new PckArchive(dstPck);
        using var src = new PckArchive(srcPck);
        foreach (var e in dst.Entries.Where(e => IsWindow(e.Path)))
        {
            if (src.Find(e.Path) is not { } se) continue;
            byte[]? b;
            try { b = WindowTexts(dst.Read(e), src.Read(se), out int n); if (n == 0) continue; windows += n; }
            catch { continue; }
            if (!dryRun && b is not null) files[e.Path] = b;
        }
        foreach (var e in dst.Entries.Where(e => e.Path.EndsWith(".htf", StringComparison.OrdinalIgnoreCase)))
        {
            if (src.Find(e.Path) is not { } se) continue;
            byte[]? b;
            try { b = HintTexts(dst.Read(e), src.Read(se), out int n); if (n == 0) continue; windows += n; }
            catch { continue; }
            if (!dryRun && b is not null) files[e.Path] = b;
        }
        // nomes dos mapas (configs\instance.txt), pela sigla do mapa
        if (dst.Find(@"configs\instance.txt") is { } ie && src.Find(@"configs\instance.txt") is { } ise)
            try
            {
                var b = InstanceNames(dst.Read(ie), src.Read(ise), out int n);
                if (n > 0) { total += n; if (!dryRun && b is not null) files[ie.Path] = b; }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
        foreach (var e in dst.Entries.Where(e => IsTable(e.Path)))
        {
            if (src.Find(e.Path) is not { } se) continue;
            StringTable? a, b;
            try { a = StringTable.Parse(dst.Read(e)); b = StringTable.Parse(src.Read(se)); } catch { continue; }
            if (a is null || b is null || a.Entries.Count == 0) continue;
            var other = b.Entries.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().Value);
            var changes = new Dictionary<int, string>();
            for (int i = 0; i < a.Entries.Count; i++)
                if (other.TryGetValue(a.Entries[i].Key, out var v) && Translatable(a.Entries[i].Value, v)) changes[i] = v;
            if (changes.Count == 0) continue;
            total += changes.Count;
            if (!dryRun) files[e.Path] = a.Encode(changes);
        }
        return (total, windows, files);
    }

    // configs\instance.txt (UTF-16 com BOM): "nome" { n  // ID  "sigla" ... por mapa (comentário entre o número e a sigla); a mesma sigla pode vir 2 vezes (1a em chinês)
    static readonly Regex InstanceEntry = new(@"""(?<n>[^""\r\n]*)""(?<mid>\s*\{\s*\d+[^""\r\n]*\s*"")(?<t>[^""\r\n]+)""", RegexOptions.CultureInvariant);

    /// <summary>
    /// Nomes dos mapas: o nome do outro cliente pela sigla (a01, world...), o traduzido dele quando a sigla vem 2 vezes. O
    /// Mundo 3D mostra o nome do instance.txt do cliente aberto (MapTags.ClientMapNames), então traduzir aqui deixa os dois
    /// iguais ao jogo (usuário 01/11).
    /// </summary>
    public static byte[]? InstanceNames(byte[] mine, byte[] other, out int count)
    {
        count = 0;
        static string? T(byte[] b) => b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1) : null;
        if (T(mine) is not { } a || T(other) is not { } o) return null;
        // sigla + ID do mapa iguais nos dois (a numeração pode mudar entre versões)
        static string Key(Match m) => m.Groups["t"].Value.Trim() + "|" + Regex.Match(m.Groups["mid"].Value, @"\d+").Value;
        var theirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in InstanceEntry.Matches(o))
        {
            var tag = Key(m); var name = m.Groups["n"].Value;
            if (name.Trim().Length == 0) continue;
            if (!theirs.TryGetValue(tag, out var had) || Cjk.IsMatch(had) && !Cjk.IsMatch(name)) theirs[tag] = name;
        }
        // o outro cliente pode trazer nome errado (BR repete Toca dos Lobos no a05): vale a tabela do MyAce no idioma dele
        int lang = MapTags.LanguageOf(theirs.Select(kv => (kv.Key.Split('|')[0], kv.Value)));
        foreach (var key in theirs.Keys.ToList())
            if (MapTags.BaseName(key.Split('|')[0], lang) is { } right) theirs[key] = right;
        int c = 0;
        var res = InstanceEntry.Replace(a, m =>
        {
            if (!theirs.TryGetValue(Key(m), out var name) || !WindowTranslatable(m.Groups["n"].Value, name)) return m.Value;   // chinês só troca chinês
            c++;
            return "\"" + name + "\"" + m.Groups["mid"].Value + m.Groups["t"].Value + "\"";
        });
        count = c;
        return c == 0 ? null : new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(res)).ToArray();
    }

    /// <summary>Corrige os nomes do próprio cliente pela tabela do MyAce, no idioma dele. Devolve o que mudou, para mostrar antes.</summary>
    public static byte[]? RepairInstanceNames(byte[] mine, out List<(string Tag, string From, string To)> changes)
    {
        var list = new List<(string, string, string)>();
        changes = list;
        if (mine.Length < 2 || mine[0] != 0xFF || mine[1] != 0xFE) return null;
        var a = Encoding.Unicode.GetString(mine, 2, mine.Length - 2 & ~1);
        int lang = MapTags.LanguageOf(InstanceEntry.Matches(a).Select(m => (m.Groups["t"].Value.Trim(), m.Groups["n"].Value)));
        if (lang == 0) return null;
        // mesma sigla com nomes diferentes (b30 = Guerra Territorial 1 a 4, random03): é de propósito, fica como está
        var shared = InstanceEntry.Matches(a).Where(m => !Cjk.IsMatch(m.Groups["n"].Value)).GroupBy(m => m.Groups["t"].Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(m => m.Groups["n"].Value.Trim()).Distinct().Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var res = InstanceEntry.Replace(a, m =>
        {
            var tag = m.Groups["t"].Value.Trim(); var name = m.Groups["n"].Value;
            if (shared.Contains(tag) || MapTags.BaseName(tag, lang) is not { } right || right == name.Trim() || name.Trim().Length == 0) return m.Value;
            list.Add((tag, name, right));
            return "\"" + right + "\"" + m.Groups["mid"].Value + m.Groups["t"].Value + "\"";
        });
        return list.Count == 0 ? null : new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(res)).ToArray();
    }

    // .htf (dicas da interface antiga): UTF-16 com BOM, uma linha "controle"<TAB>"dica" (PW-KB §S20c)
    static readonly Regex HintLine = new(@"^""(?<k>[^""\r\n]*)""[ \t]+""(?<v>[^\r\n]*)""[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Tabela de dicas: troca o texto de cada controle pelo do outro cliente (mesmo nome), como está escrito lá.</summary>
    public static byte[]? HintTexts(byte[] mine, byte[] other, out int count)
    {
        count = 0;
        static string? T(byte[] b) => b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1) : null;
        if (T(mine) is not { } a || T(other) is not { } o) return null;
        var theirs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in HintLine.Matches(o)) theirs.TryAdd(m.Groups["k"].Value, m.Groups["v"].Value);
        var sb = new StringBuilder(a.Length + 256);
        int at = 0, n = 0;
        foreach (Match m in HintLine.Matches(a))
        {
            var v = m.Groups["v"];
            if (!theirs.TryGetValue(m.Groups["k"].Value, out var nv) || !WindowTranslatable(v.Value, nv)) continue;
            sb.Append(a, at, v.Index - at).Append(nv); at = v.Index + v.Length; n++;
        }
        if (n == 0) return null;
        sb.Append(a, at, a.Length - at);
        count = n;
        return new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>
    /// Uma janela: troca o String do &lt;Text&gt;/&lt;Hint&gt; de cada controle pelo do outro cliente (mesma janela, mesmo tipo e nome
    /// de controle), copiado como está escrito lá. Só mexe se a janela volta byte a byte igual sem mudança e se, depois,
    /// relê com os mesmos controles e exatamente os textos pedidos. Devolve os bytes novos (null se nada muda).
    /// </summary>
    public static byte[]? WindowTexts(byte[] mine, byte[] other, out int count)
    {
        count = 0;
        var a = new UiDocument(mine);
        if (!a.ToBytes().AsSpan().SequenceEqual(mine)) return null;   // não volta igual: não arrisca
        var b = new UiDocument(other);
        var ta = UiTexts.Find(a.Xml); var tb = UiTexts.Find(b.Xml);
        var changes = new List<(UiTexts.Item, string)>();
        foreach (var (k, it) in ta)
            if (tb.TryGetValue(k, out var o) && o.Raw != it.Raw && !o.Raw.Contains('"') && WindowTranslatable(it.Text, o.Text)) changes.Add((it, o.Raw));
        if (changes.Count == 0) return null;
        var xml = UiTexts.Replace(a.Xml, changes);
        var check = UiTexts.Find(xml);
        var before = GameDialog.Parse(a.Xml); var after = GameDialog.Parse(xml);
        if (check.Count != ta.Count || after.Controls.Count != before.Controls.Count || after.Name != before.Name || after.Size != before.Size
            || changes.Any(c => !check.TryGetValue(c.Item1.Key, out var x) || x.Raw != c.Item2)) return null;
        a.Restore(xml);
        count = changes.Count;
        return a.ToBytes();
    }
}
