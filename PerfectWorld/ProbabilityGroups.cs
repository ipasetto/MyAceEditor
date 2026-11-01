using System.Globalization;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Grupos de probabilidade de um item (drop_matters_1..32_probability, probability_socket0..2…): os campos que
/// mudam só no número do slot. Usado pelas barras da grade: ao arrastar uma, as outras livres (sem cadeado) se
/// ajustam para o grupo continuar somando 100% — só quando o grupo já somava 100% antes.
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

    /// <summary>
    /// Novos valores do grupo depois de pôr <paramref name="value"/> em <paramref name="key"/>. Se o grupo somava 100%,
    /// a diferença vai para os outros slots livres (fora de <paramref name="locked"/>), na proporção que já tinham;
    /// se todos estavam em 0, vai para os slots com referência preenchida (<paramref name="hasRef"/>). Os travados não mudam.
    /// Sem para onde ir, só o próprio campo muda (o grupo fica fora de 100% e a soma aparece em vermelho).
    /// </summary>
    public static Dictionary<string, double> Set(Item item, string key, double value, ISet<string> locked, Func<string, bool>? hasRef = null)
    {
        value = Math.Clamp(value, 0, 1);
        var sib = Siblings(item, key);
        var cur = sib.ToDictionary(k => k, k => item.Fields.TryGetValue(k, out var f) ? Num(f.Value) : 0);
        if (sib.Count < 2 || !SumsToOne(cur.Values.Sum(), sib.Count)) return new() { [key] = value };

        double lockedSum = sib.Where(k => k != key && locked.Contains(k)).Sum(k => cur[k]);
        value = Math.Min(value, Math.Max(0, 1 - lockedSum));
        var free = sib.Where(k => k != key && !locked.Contains(k)).ToList();
        double rest = 1 - lockedSum - value;
        var result = new Dictionary<string, double> { [key] = value };

        var weighted = free.Where(k => cur[k] > 0).ToList();
        if (weighted.Count == 0 && rest > Eps) weighted = free.Where(k => hasRef?.Invoke(k) == true).ToList();
        if (weighted.Count == 0)
        {
            if (rest > Eps) return result;              // nada livre para receber: só este campo muda
            foreach (var k in free) result[k] = 0;
            return result;
        }
        double baseSum = weighted.Sum(k => cur[k]);
        double acc = 0;
        foreach (var k in free)
        {
            if (!weighted.Contains(k)) { result[k] = 0; continue; }
            double nv = k == weighted[^1] ? Math.Max(0, rest - acc)
                : Math.Round(rest * (baseSum > 0 ? cur[k] / baseSum : 1.0 / weighted.Count), 6);
            acc += nv;
            result[k] = nv;
        }
        return result;
    }
}
