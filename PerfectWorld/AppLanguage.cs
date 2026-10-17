namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Idioma do programa (global). Códigos: pt, en, zh, ru. Guardado em %AppData%\MyAceEditor\lang.txt.
/// Textos de interface escritos só em pt e en: zh/ru usam o inglês como reserva (T). O idioma também é o
/// idioma-alvo das traduções do conteúdo do jogo (AiTranslator).
/// </summary>
public static class AppLang
{
    public static readonly (string Code, string Name)[] All =
    {
        ("pt", "Português"), ("en", "English"), ("zh", "中文"), ("ru", "Русский"),
    };

    static string _current = "pt";

    public static event Action? Changed;

    static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "lang.txt");

    static AppLang()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var c = File.ReadAllText(SettingsFile).Trim();
                if (All.Any(a => a.Code == c)) _current = c;
            }
        }
        catch { /* sem arquivo de configuração: fica em pt */ }
    }

    public static string Current
    {
        get => _current;
        set
        {
            if (_current == value || All.All(a => a.Code != value)) return;
            _current = value;
            try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, value); } catch { }
            Changed?.Invoke();
        }
    }

    /// <summary>Texto de interface: português se o programa está em pt; senão inglês.</summary>
    public static string T(string pt, string en) => _current == "pt" ? pt : en;

    public static string NameOf(string code) => All.FirstOrDefault(a => a.Code == code).Name ?? code;
}

/// <summary>Detecta em que escrita um texto está (suficiente para decidir se precisa de tradução).</summary>
public static class TextScript
{
    public static bool HasCjk(string s) => s.Any(c => (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u3040' && c <= '\u30FF'));
    public static bool HasCyrillic(string s) => s.Any(c => c >= '\u0400' && c <= '\u04FF');

    /// <summary>
    /// true se o texto está em escrita diferente da do idioma-alvo (chinês/cirílico quando o alvo é pt/en etc.).
    /// Texto latino nunca é enviado: não dá para saber se já está no idioma certo (limitação conhecida).
    /// </summary>
    public static bool NeedsTranslation(string s, string target)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return (HasCjk(s) && target != "zh") || (HasCyrillic(s) && target != "ru");
    }
}
