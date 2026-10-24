using System.Globalization;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Procura erros comuns no elements.data (como o "achar e corrigir erros" do RAE, código nosso):
/// referência para ID que não existe, probabilidade fora de 0..1, grupo de probabilidades que não soma 100%,
/// e ID repetido dentro do mesmo espaço de IDs (IdSpaces). Os grupos que "devem somar 100%" são aprendidos dos próprios dados:
/// conta como distribuição o grupo que soma 1 na grande maioria dos itens do bloco.
/// </summary>
public static class ErrorFinder
{
    public enum Kind { MissingRef, ProbRange, ProbSum, DuplicateId }

    public sealed record Problem(Kind Kind, ItemList Block, Item Item, string Field, string Detail);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double Eps = 0.0005;

    static bool Empty(string v) => v is "" or "0" or "-1" or "0.0";
    static double Num(string v) => double.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out var d) ? d : 0;

    static string? GroupOf(string key) => ProbabilityGroups.GroupOf(key);

    /// <summary>Grupos de probabilidade tratados como distribuição (bloco|grupo), para diagnóstico.</summary>
    public static List<string> Distributions { get; } = new();

    public static List<Problem> Find(Module module, Action<double>? progress = null)
    {
        var found = new List<Problem>();
        Distributions.Clear();
        // referência quebrada = ID que não existe em bloco nenhum (o tipo do campo nem sempre diz o bloco:
        // NPC_MAKE_SERVICE aponta receitas, MONSTER_TYPE aponta MONSTER_ADDON...)
        var allIds = new HashSet<string>(module.Lists.SelectMany(l => l.Items).Select(i => i.Id));
        long total = Math.Max(1, module.Lists.Sum(l => (long)l.Items.Count)), done = 0;
        var seen = new Dictionary<(IdSpaces.Space, string), (ItemList, Item)>();
        foreach (var block in module.Lists)
        {
            if (block.Items.Count == 0) { continue; }
            var sample = block.Items[0].Fields.Keys.ToList();
            var probKeys = sample.Where(k => FieldKinds.Of(k) == FieldKind.Probability).ToList();
            var groups = probKeys.GroupBy(GroupOf).Where(g => g.Key is not null && g.Count() > 1)
                .ToDictionary(g => g.Key!, g => g.ToList());
            var refKeys = sample.Where(k => FieldKinds.IsReference(FieldKinds.Of(k))).ToList();

            // grupo é distribuição se soma ~1 em >= 90% dos itens em que tem algum valor
            var distribution = new HashSet<string>();
            foreach (var (g, keys) in groups)
            {
                int used = 0, one = 0;
                foreach (var it in block.Items)
                {
                    double s = keys.Sum(k => it.Fields.TryGetValue(k, out var f) ? Num(f.Value) : 0);
                    if (s <= Eps) continue;
                    used++;
                    if (Math.Abs(s - 1) <= Eps * keys.Count) one++;
                }
                if (used >= 3 && one >= used * 0.9) { distribution.Add(g); Distributions.Add($"{block.Name}|{g} ({one}/{used})"); }
            }

            var space = IdSpaces.Of(block.Name);   // ID só conflita dentro do mesmo espaço
            foreach (var it in block.Items)
            {
                if ((++done & 1023) == 0) progress?.Invoke(done / (double)total);
                if (!Empty(it.Id))
                {
                    if (seen.TryGetValue((space, it.Id), out var first))
                        found.Add(new(Kind.DuplicateId, block, it, "ID", $"{first.Item1.Name} {first.Item2.Id} {first.Item2.Name}"));
                    else seen[(space, it.Id)] = (block, it);
                }
                foreach (var k in refKeys)
                {
                    if (!it.Fields.TryGetValue(k, out var f) || Empty(f.Value)) continue;
                    if (!allIds.Contains(f.Value))
                        found.Add(new(Kind.MissingRef, block, it, k, f.Value));
                }
                foreach (var k in probKeys)
                {
                    if (!it.Fields.TryGetValue(k, out var f)) continue;
                    var p = Num(f.Value);
                    if (p < -Eps || p > 1 + Eps) found.Add(new(Kind.ProbRange, block, it, k, f.Value));
                }
                foreach (var g in distribution)
                {
                    var keys = groups[g];
                    double s = keys.Sum(k => it.Fields.TryGetValue(k, out var f) ? Num(f.Value) : 0);
                    if (s > Eps && Math.Abs(s - 1) > Eps * keys.Count)
                        found.Add(new(Kind.ProbSum, block, it, keys[0], (s * 100).ToString("0.###", Inv) + "%"));
                }
            }
        }
        progress?.Invoke(1);
        return found;
    }

    /// <summary>Dá para corrigir sozinho? (probabilidade fora de 0..1 ou grupo que não soma 100%).</summary>
    public static bool CanFix(Problem p) => p.Kind is Kind.ProbRange or Kind.ProbSum;

    /// <summary>
    /// Corrige no lugar: fora de 0..1 vira o limite mais próximo; grupo que não soma 100% é reescalado
    /// mantendo a proporção entre os slots (o último slot preenchido absorve o arredondamento).
    /// </summary>
    public static bool Fix(Problem p, Action<Item, string, Field>? set = null)
    {
        var f = p.Item.Fields;
        set ??= (it, k, v) => it.Fields[k] = v;   // a tela passa o histórico (desfazer)
        string Fmt(double v) => v.ToString("0.######", Inv);
        if (p.Kind == Kind.ProbRange && f.TryGetValue(p.Field, out var one))
        {
            set(p.Item, p.Field, Field.Of(one.Type, Fmt(Math.Clamp(Num(one.Value), 0, 1))));
            return true;
        }
        if (p.Kind != Kind.ProbSum || GroupOf(p.Field) is not { } g) return false;
        var keys = f.Keys.Where(k => GroupOf(k) == g && FieldKinds.Of(k) == FieldKind.Probability).ToList();
        double sum = keys.Sum(k => Num(f[k].Value));
        if (sum <= Eps) return false;
        var lastFilled = keys.Last(k => Num(f[k].Value) > 0);
        double acc = 0;
        foreach (var k in keys)
        {
            double v = Num(f[k].Value);
            if (v <= 0) continue;
            double nv = k == lastFilled ? Math.Round(1 - acc, 6) : Math.Round(v / sum, 6);
            acc += nv;
            set(p.Item, k, Field.Of(f[k].Type, Fmt(nv)));
        }
        return true;
    }
}
