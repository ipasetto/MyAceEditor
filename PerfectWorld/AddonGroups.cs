using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma linha de probabilidade: o campo da chance e, se houver, o campo do ID ao qual ela se refere.</summary>
public sealed record ProbRow(string Prob, string? Ref);

/// <summary>Grupo de probabilidades do item (ex.: drop_probability_socket#, addons_#_probability_addon).</summary>
public sealed record ProbSection(string Name, List<ProbRow> Rows)
{
    public bool HasRefs => Rows.Any(r => r.Ref is not null);
}

/// <summary>
/// Grupos de chances de equipamento usados pelo editor de addons: soquetes (fabricar/cair), quantidade de addons,
/// chance de único e as listas de addons (fabricação, aleatórios, únicos). Descobertos pelos nomes dos campos,
/// então servem para arma, armadura e acessório de qualquer versão.
/// </summary>
public static class AddonGroups
{
    /// <summary>Grupos do editor de addons: soquetes, quantidade de addons, únicos, listas de addons.</summary>
    public static bool IsAddonGroup(string group) =>
        group.Contains("socket") || group.Contains("addon") || group.Contains("rand") || group.Contains("unique");

    /// <summary>Grupos do editor de drops do monstro: chance de cair N itens e a lista drop_matters.</summary>
    public static bool IsDropGroup(string group) => group.StartsWith("probability_drop_num") || group.StartsWith("drop_matters_");

    public static List<ProbSection> Find(Item item, Func<string, bool>? wanted = null)
    {
        wanted ??= IsAddonGroup;
        var refOf = new Dictionary<string, string>();
        foreach (var k in item.Fields.Keys)
            if (FieldKinds.IsReference(FieldKinds.Of(k)) && FieldKinds.ProbabilityFor(k, item.Fields) is { } p && !refOf.ContainsKey(p))
                refOf[p] = k;
        var sections = new List<ProbSection>();
        var byName = new Dictionary<string, ProbSection>();
        foreach (var k in item.Fields.Keys)
        {
            if (FieldKinds.Of(k) != FieldKind.Probability) continue;
            var g = ProbabilityGroups.GroupOf(k) ?? k;   // sem número (probability_unique): grupo de uma linha só
            if (!wanted(g)) continue;
            if (!byName.TryGetValue(g, out var sec)) { sec = new ProbSection(g, new()); byName[g] = sec; sections.Add(sec); }
            sec.Rows.Add(new ProbRow(k, refOf.GetValueOrDefault(k)));
        }
        return sections;
    }

    /// <summary>Linhas da lista com algo preenchido (ID ou chance), na ordem dos slots.</summary>
    public static IEnumerable<ProbRow> Filled(Item item, ProbSection s) => s.Rows.Where(r =>
        (r.Ref is not null && !Empty(item.Fields[r.Ref].Value)) || ProbabilityGroups.Num(item.Fields[r.Prob].Value) > 0);

    public static bool Empty(string v) => v is "" or "0" or "-1";

    /// <summary>Junta os preenchidos nos primeiros slots, do maior para o menor, e zera o resto.</summary>
    public static void Sort(Item item, ProbSection s)
    {
        var data = Filled(item, s).Select(r => (Ref: r.Ref is null ? null : item.Fields[r.Ref].Value, P: item.Fields[r.Prob].Value))
            .OrderByDescending(x => ProbabilityGroups.Num(x.P)).ToList();
        for (int i = 0; i < s.Rows.Count; i++)
        {
            var r = s.Rows[i];
            if (r.Ref is not null) Put(item, r.Ref, i < data.Count ? data[i].Ref ?? "0" : "0");
            Put(item, r.Prob, i < data.Count ? data[i].P : "0");
        }
    }

    /// <summary>Reescala o grupo para 100% mantendo a proporção; se tudo está em 0, divide igual entre os slots com ID.</summary>
    public static void Normalize(Item item, ProbSection s, ISet<string> locked)
    {
        var free = s.Rows.Where(r => !locked.Contains(r.Prob)).ToList();
        double lockedSum = s.Rows.Where(r => locked.Contains(r.Prob)).Sum(r => ProbabilityGroups.Num(item.Fields[r.Prob].Value));
        double rest = Math.Max(0, 1 - lockedSum), sum = free.Sum(r => ProbabilityGroups.Num(item.Fields[r.Prob].Value));
        var targets = sum > 0 ? free.Where(r => ProbabilityGroups.Num(item.Fields[r.Prob].Value) > 0).ToList()
            : free.Where(r => r.Ref is not null && !Empty(item.Fields[r.Ref].Value)).ToList();
        if (targets.Count == 0) return;
        double acc = 0;
        foreach (var r in targets)
        {
            double v = r == targets[^1] ? rest - acc
                : Math.Round(sum > 0 ? rest * ProbabilityGroups.Num(item.Fields[r.Prob].Value) / sum : rest / targets.Count, 6);
            acc += v;
            Put(item, r.Prob, ProbabilityGroups.Fmt(Math.Max(0, v)));
        }
    }

    static void Put(Item item, string key, string v) => item.Fields[key] = Field.Of(item.Fields[key].Type, v);

    public static Item Clone(Item item) => new()
    {
        Id = item.Id, Name = item.Name,
        Fields = item.Fields.ToDictionary(kv => kv.Key, kv => Field.Of(kv.Value.Type, kv.Value.Value)),
    };
}
