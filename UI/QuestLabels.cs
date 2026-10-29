using System.Text.RegularExpressions;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Nome de campo de quest no idioma da tela. O nome em português (TaskSchema93) continua sendo a identidade do
/// campo (QuestFieldHint etc.); aqui só se traduz para mostrar. Nomes montados ("Monstro 2: ID", "Prêmio 1.3: chance",
/// "Data de falha (ano)", "Destino X") são traduzidos por partes, cada parte pela chave <c>task.&lt;parte&gt;</c> do lang/*.json.
/// </summary>
public static partial class QuestLabels
{
    public static string Show(string name)
    {
        if (QuestFieldInfo.IsGroup(name)) return QuestFieldInfo.GroupLabel(name);
        if (QuestFieldInfo.IsKey(name)) return QuestFieldInfo.Label(name);   // chaves do TaskLayout: rótulo da tabela de campos
        if (AppLang.Current == "pt" || name.Length == 0) return name;
        var whole = Loc.T("task." + name, "");
        if (whole.Length > 0) return whole;
        int c = name.IndexOf(": ", StringComparison.Ordinal);
        if (c > 0) return Show(name[..c]) + ": " + Show(name[(c + 2)..]);
        if (Numbered().Match(name) is { Success: true } n) return Piece(n.Groups[1].Value) + " " + n.Groups[2].Value;
        if (Paren().Match(name) is { Success: true } p) return Piece(p.Groups[1].Value) + " (" + Piece(p.Groups[2].Value) + ")";
        return name;
    }

    /// <summary>Nome na linha da tabela: sem o escopo que já aparece no título do grupo.</summary>
    public static string Row(string name) => QuestFieldInfo.IsKey(name) ? QuestFieldInfo.Label(name, inGroup: true) : Show(name);

    /// <summary>Dica flutuante do campo (o que faz + nome original); vazio para nomes que não são chaves do TaskLayout.</summary>
    public static string Tip(string name) => QuestFieldInfo.IsKey(name) ? QuestFieldInfo.Tip(name) : "";

    static string Piece(string s) => Loc.T("task." + s, s);

    [GeneratedRegex(@"^(.+) (\d+(?:\.\d+)?|[XYZ])$")] private static partial Regex Numbered();
    [GeneratedRegex(@"^(.+) \(([^()]+)\)$")] private static partial Regex Paren();
}
