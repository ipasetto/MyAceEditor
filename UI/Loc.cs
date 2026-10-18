using System.Text.Json;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Textos da interface. O português é o padrão no próprio código (argumento <c>pt</c>); os outros idiomas
/// vêm de arquivos <c>lang/&lt;código&gt;.json</c> ao lado do executável (chave → texto). Assim qualquer pessoa
/// traduz copiando <c>lang/en.json</c> para <c>lang/&lt;seu código&gt;.json</c> e editando — sem mexer em código.
/// Chave que não existe no arquivo do idioma cai para o português (nunca fica vazia).
/// </summary>
public static class Loc
{
    static Dictionary<string, string> _map = new();
    static string _loaded = "pt";

    /// <summary>Disparado depois que o idioma muda: cada tela reaplica seus textos (ver ApplyLanguage).</summary>
    public static event Action? Changed;

    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    static Loc()
    {
        AppLang.Changed += () => { Load(AppLang.Current); Changed?.Invoke(); };
        Load(AppLang.Current);
    }

    /// <summary>Lê o catálogo do idioma (pt = padrão do código, sem arquivo).</summary>
    static void Load(string code)
    {
        _loaded = code;
        _map = new Dictionary<string, string>();
        if (code == "pt") return;
        try
        {
            // <código>.json; senão a primeira variante regional (ex.: zh → zh-CN.json)
            var file = Path.Combine(LangDir, code + ".json");
            if (!File.Exists(file) && Directory.Exists(LangDir))
                file = Directory.GetFiles(LangDir, code + "-*.json").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() ?? file;
            if (File.Exists(file))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (d is not null) _map = d;
            }
        }
        catch { /* arquivo de idioma com erro: usa o português e segue */ }
    }

    /// <summary>Texto no idioma atual; <paramref name="pt"/> é o padrão e o que aparece se faltar tradução.</summary>
    public static string T(string key, string pt) =>
        _loaded != "pt" && _map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : pt;

    /// <summary>Chaves que o catálogo do idioma não cobre (para o tradutor/comunidade ver o que falta).</summary>
    public static int Coverage(IEnumerable<string> keys) => keys.Count(k => _map.ContainsKey(k));
}
