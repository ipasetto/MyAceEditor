using System.Globalization;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public enum SearchOp { Eq, Ne, Gt, Lt, Ge, Le, Between, Contains, NotContains, StartsWith }

/// <summary>Um resultado: item, bloco e o campo que bateu (com o valor).</summary>
public sealed record SearchHit(ItemList Block, Item Item, string Field, string Value);

/// <summary>
/// Pesquisa avançada (como a aba Pesquisar do RAE, código nosso): um campo ou qualquer campo, num bloco ou em todos,
/// com comparação de número (0.1 = 0.10) ou texto (sem diferenciar maiúsculas).
/// </summary>
public static class AdvancedSearch
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static bool Num(string s, out double d) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out d);

    public static bool Matches(string value, SearchOp op, string v, string v2 = "")
    {
        bool bothNum = Num(value, out var a) & Num(v, out var b);
        switch (op)
        {
            case SearchOp.Eq: return bothNum ? a == b : value.Equals(v, StringComparison.OrdinalIgnoreCase);
            case SearchOp.Ne: return bothNum ? a != b : !value.Equals(v, StringComparison.OrdinalIgnoreCase);
            case SearchOp.Gt: return bothNum && a > b;
            case SearchOp.Lt: return bothNum && a < b;
            case SearchOp.Ge: return bothNum && a >= b;
            case SearchOp.Le: return bothNum && a <= b;
            case SearchOp.Between: return bothNum && Num(v2, out var c) && a >= Math.Min(b, c) && a <= Math.Max(b, c);
            case SearchOp.Contains: return value.Contains(v, StringComparison.OrdinalIgnoreCase);
            case SearchOp.NotContains: return !value.Contains(v, StringComparison.OrdinalIgnoreCase);
            case SearchOp.StartsWith: return value.StartsWith(v, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <param name="field">campo; null = qualquer campo (o primeiro que bater em cada item)</param>
    public static List<SearchHit> Run(IEnumerable<ItemList> blocks, string? field, SearchOp op, string v, string v2 = "", int max = 5000)
    {
        var hits = new List<SearchHit>();
        foreach (var block in blocks)
            foreach (var it in block.Items)
            {
                if (field is not null)
                {
                    if (it.Fields.TryGetValue(field, out var f) && Matches(f.Value, op, v, v2)) hits.Add(new(block, it, field, f.Value));
                }
                else
                    foreach (var (k, f) in it.Fields)
                        if (Matches(f.Value, op, v, v2)) { hits.Add(new(block, it, k, f.Value)); break; }
                if (hits.Count >= max) return hits;
            }
        return hits;
    }
}
