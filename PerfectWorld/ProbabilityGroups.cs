using System.Globalization;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Grupos de probabilidade de um item (drop_matters_1..32_probability, probability_socket0..2…): os campos que
/// mudam só no número do slot. Arrastar uma barra muda só ela; somar 100% é só pelo botão (o gs normaliza o
/// grupo ao carregar: PW-KB §S23 drop).
/// </summary>
public static class ProbabilityGroups
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double Eps = 0.0005;

    // drop_matters_3_probability -> "drop_matters_#_probability"; probability_socket2 -> "probability_socket#"
    static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>Nome do grupo (último número trocado por #) ou null se o campo não tem número.</summary>
    public static string? GroupOf(string key)
    {
        var ms = Digits.Matches(key);
        if (ms.Count == 0) return null;
        var m = ms[^1];   // o último número é o índice do slot (skill_hp75_2_probability -> skill_hp75_#_probability)
        return key[..m.Index] + "#" + key[(m.Index + m.Length)..];
    }

    public static double Num(string v) => double.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out var d) ? d : 0;
    public static string Fmt(double v) => Math.Round(v, 6).ToString("0.######", Inv);

    /// <summary>Probabilidades do mesmo grupo (na ordem do item), incluindo a própria; só ela se não tem grupo.</summary>
    public static List<string> Siblings(Item item, string key)
    {
        if (GroupOf(key) is not { } g) return new() { key };
        var list = item.Fields.Keys.Where(k => GroupOf(k) == g && FieldKinds.Of(k) == FieldKind.Probability).ToList();
        return list.Count == 0 ? new() { key } : list;
    }

    public static double Sum(Item item, IEnumerable<string> keys) => keys.Sum(k => item.Fields.TryGetValue(k, out var f) ? Num(f.Value) : 0);

    public static bool SumsToOne(double s, int n) => Math.Abs(s - 1) <= Eps * Math.Max(1, n);
}
