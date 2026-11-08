using System.Text.Json;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Nome das texturas do chão no idioma do MyAce, só para mostrar (JAV e painel): o arquivo continua com o nome chinês do
/// textures.pck (mudar quebraria o mapa). O nome vem do glossário de palavras lang/texture-words.json (chinês → pt, en, ru;
/// tipo p = lugar, x = ignorar; cobre os 680 nomes dos clientes 144/155/180 e PWI) montado em ordem: en/ru como o chinês,
/// pt em lista com o lugar no fim ("Rocha, neve · Cidade da Neve"). zh mostra o original. O usuário pode renomear: o nome
/// dele fica no glossário do cliente, por idioma (%AppData%\MyAceEditor\texture-names-&lt;cliente&gt;.json).
/// </summary>
public static class TextureNameGlossary
{
    static Dictionary<string, string[]>? _words;
    static List<string>? _keys;
    static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _user = new(StringComparer.OrdinalIgnoreCase);

    public static string Base(string path) => Path.GetFileNameWithoutExtension(path.Replace('/', '\\').Split('\\')[^1]);

    static Dictionary<string, string[]> Words()
    {
        if (_words is not null) return _words;
        _words = new();
        try
        {
            var f = Path.Combine(Loc.LangDir, "texture-words.json");
            if (File.Exists(f)) _words = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(f)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("glossário das texturas: ler", ex); }
        _keys = [.. _words.Keys.OrderByDescending(k => k.Length)];
        return _words;
    }

    /// <summary>Monta o nome no idioma (pt, en, ru); zh ou sem chinês = o original.</summary>
    public static string Compose(string baseName, string lang)
    {
        int li = lang switch { "pt" => 0, "en" => 1, "ru" => 2, _ => -1 };
        if (li < 0 || !TextScript.HasCjk(baseName)) return baseName;
        var w = Words(); var keys = _keys!;
        var places = new List<string>(); var words = new List<string>(); var nums = new List<string>();
        for (int i = 0; i < baseName.Length;)
        {
            char c = baseName[i];
            if (c is '_' or ' ' or '-') { i++; continue; }
            string? k = keys.FirstOrDefault(x => string.CompareOrdinal(baseName, i, x, 0, x.Length) == 0);
            if (k is null && (char.IsAsciiLetterOrDigit(c) || c is '(' or ')'))
            {
                int j = i; while (j < baseName.Length && (char.IsAsciiLetterOrDigit(baseName[j]) || baseName[j] is '(' or ')')) j++;
                nums.Add(baseName[i..j]); i = j; continue;
            }
            if (k is null) { words.Add(c.ToString()); i++; continue; }   // fora do glossário: fica o caractere
            i += k.Length;
            var e = w[k];
            string kind = e.Length > 3 ? e[3] : "w";
            if (kind == "x") continue;
            (kind == "p" ? places : words).Add(e[li]);
        }
        string s;
        if (li == 0)
        {
            words.Reverse();
            s = string.Join(", ", words) + (nums.Count > 0 ? " " + string.Join(" ", nums) : "");
            if (places.Count > 0) s = (s.Trim().Length > 0 ? s + " · " : "") + string.Join(" ", places);
        }
        else s = string.Join(" ", places.Concat(words).Concat(nums));
        s = string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length == 0 ? baseName : char.ToUpper(s[0]) + s[1..];
    }

    static string UserPath(string elementDir) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", $"texture-names-{JavFavorites.ClientKey(elementDir)}.json");

    static Dictionary<string, Dictionary<string, string>> User(string elementDir)
    {
        var p = UserPath(elementDir);
        if (_user.TryGetValue(p, out var d)) return d;
        d = new(StringComparer.Ordinal);
        try { if (File.Exists(p)) d = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(p)) ?? d; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("glossário das texturas do cliente: ler " + p, ex); }
        return _user[p] = d;
    }

    /// <summary>Nome renomeado pelo usuário neste cliente e idioma; null = não tem.</summary>
    public static string? UserName(string path, string? elementDir) =>
        elementDir is not null && User(elementDir).TryGetValue(Base(path), out var m) && m.TryGetValue(AppLang.Current, out var n) ? n : null;

    /// <summary>Nome que aparece: o do usuário (cliente + idioma), senão o do glossário.</summary>
    public static string Display(string path, string? elementDir) => UserName(path, elementDir) ?? Compose(Base(path), AppLang.Current);

    /// <summary>Renomeia no glossário do cliente, no idioma atual (null ou vazio = volta ao automático). null = deu certo.</summary>
    public static string? Rename(string path, string elementDir, string? name)
    {
        var d = User(elementDir); string b = Base(path), lang = AppLang.Current;
        if (string.IsNullOrWhiteSpace(name)) { if (d.TryGetValue(b, out var m)) { m.Remove(lang); if (m.Count == 0) d.Remove(b); } }
        else { if (!d.TryGetValue(b, out var m)) d[b] = m = new(); m[lang] = name.Trim(); }
        try
        {
            var p = UserPath(elementDir);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            AtomicFileSave.Write(p, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileSaveException)
        {
            AppLog.Error("glossário das texturas do cliente: gravar", ex);
            return Loc.T("texnames.saveFail", "Não deu para guardar o nome (pasta de configurações sem acesso?).");
        }
    }
}
