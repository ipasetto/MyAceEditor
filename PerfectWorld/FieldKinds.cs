using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>O que um campo do elements.data representa (além do número cru).</summary>
public enum FieldKind
{
    Plain,
    ItemRef,     // ID de qualquer item/essence (espaço de IDs ESSENCE, PW-KB §S17)
    AddonRef,    // ID em EQUIPMENT_ADDON
    RecipeRef,   // ID em RECIPE_ESSENCE
    MonsterRef,  // ID em MONSTER_ESSENCE
    SkillRef,    // ID de skill (elementskill; ainda sem nomes)
    TaskRef,     // ID de quest (tasks.data)
    Probability, // float 0..1 exibido em %
    ClassMask,   // máscara de classes (PW-KB §S24)
    File,        // caminho de arquivo do cliente
    Color,       // cor ARGB
}

/// <summary>
/// Classifica campos pelo nome (regras nossas, a partir dos padrões do .cfg). Ver ENGINE-KB §E3b.
/// </summary>
public static class FieldKinds
{
    static readonly Dictionary<string, FieldKind> Cache = new();

    public static FieldKind Of(string field)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(field, out var k)) return k;
            return Cache[field] = Classify(field);
        }
    }

    static FieldKind Classify(string field)
    {
        var n = field.ToLowerInvariant();
        if (n is "id" or "id_major_type" or "id_sub_type" or "id_type") return FieldKind.Plain;
        if (n.StartsWith("file_") || n.StartsWith("pllfiles")) return FieldKind.File;
        if (n.Contains("color")) return FieldKind.Color;
        if (n is "character_combo_id" || n.Contains("occupation") || n.EndsWith("class_mask")) return FieldKind.ClassMask;
        if (n.Contains("probability") || Regex.IsMatch(n, @"(^|_)prob(_|\d|$)")) return FieldKind.Probability;

        bool idLike = n.StartsWith("id_") || n.EndsWith("_id") || Regex.IsMatch(n, @"(^|_)id(_|\d|$)") || n.Contains("_id_");
        if (n.Contains("level") || n.Contains("num") || n.Contains("count") || n.Contains("time")) idLike = n.StartsWith("id_") && !n.Contains("level");

        if (n.Contains("monster")) return idLike ? FieldKind.MonsterRef : FieldKind.Plain;
        if (n.Contains("addon") || n.Contains("unique") || Regex.IsMatch(n, @"rands_\d+_id_rand")) return idLike || n.StartsWith("id_addon") || n == "levelup_addon" ? FieldKind.AddonRef : FieldKind.Plain;
        if (n.Contains("recipe")) return idLike || Regex.IsMatch(n, @"^recipe_?\d+$") ? FieldKind.RecipeRef : FieldKind.Plain;
        if (n.Contains("skill") && !n.Contains("level") && !n.Contains("hp"))
            return idLike || Regex.IsMatch(n, @"^(id_)?skills?_?\d+$|^skill_map_\d+_\d+$") ? FieldKind.SkillRef : FieldKind.Plain;
        if (n.Contains("skill") && n.Contains("id_skill")) return FieldKind.SkillRef;
        if (n.Contains("task") && !n.Contains("matter"))
            return idLike || Regex.IsMatch(n, @"^task_?\d+$") ? FieldKind.TaskRef : FieldKind.Plain;
        if (idLike && (n.Contains("matter") || n.Contains("goods") || n.Contains("material") || n.Contains("equip")
                       || n.Contains("item") || n.Contains("object_need") || n.Contains("to_make") || n.Contains("drop")
                       || n.Contains("element_id") || n.Contains("product") || n.Contains("egg") || n.Contains("pet"))) return FieldKind.ItemRef;
        return FieldKind.Plain;
    }

    public static bool IsReference(FieldKind k) =>
        k is FieldKind.ItemRef or FieldKind.AddonRef or FieldKind.RecipeRef or FieldKind.MonsterRef;

    /// <summary>
    /// Campo de probabilidade "irmão" de uma referência (ex.: drop_matters_3_id → drop_matters_3_probability,
    /// addons_2_id_addon → addons_2_probability_addon). Null se não houver.
    /// </summary>
    public static string? ProbabilityFor(string refField, IReadOnlyDictionary<string, Field> fields)
    {
        foreach (var cand in new[]
        {
            Regex.Replace(refField, @"id_?(\w*)$", "probability_$1").TrimEnd('_'),
            Regex.Replace(refField, @"_id$", "_probability"),
            Regex.Replace(refField, @"_id_(\w+)$", "_probability_$1"),
            refField.Replace("id_", "probability_"),
        })
            if (cand != refField && fields.ContainsKey(cand)) return cand;
        return null;
    }

    // Classes na ordem dos bits (PW-KB §S24).
    // bit i: BM=1 WIZ=2 PSY=4 VEN=8 BAR=16 AS=32 AR=64 CLE=128 SE=256 MY=512 (PW-KB §S24), nomes da versão BR
    static readonly string[] Classes = { "Guerreiro", "Mago", "Espiritualista", "Feiticeira", "Bárbaro", "Mercenário", "Arqueiro", "Sacerdote", "Arcano", "Místico", "Lâmina Sombria", "Tempestade" };

    public static string ClassName(int bit) => bit < Classes.Length ? Classes[bit] : $"Classe {bit + 1}";

    public static string ClassNames(string value)
    {
        if (!long.TryParse(value, out var m) || m <= 0) return "";
        var names = Classes.Where((_, i) => (m & (1L << i)) != 0).ToList();
        return names.Count == 0 ? "" : names.Count >= 10 ? "Todas as classes" : string.Join(", ", names);
    }
}

/// <summary>
/// Índices de IDs do módulo: resolve referências e encontra "quem usa este item" (aba Vínculos).
/// </summary>
public sealed class RefIndex
{
    public sealed record Ref(ItemList Block, Item Item, string Field, string? Probability);

    readonly Dictionary<string, (ItemList, Item)> _essence = new(), _addon = new(), _recipe = new(), _monster = new();
    Dictionary<string, List<Ref>>? _usedBy;
    readonly Module _module;

    public RefIndex(Module module)
    {
        _module = module;
        foreach (var l in module.Lists)
        {
            var target = l.Name switch
            {
                "EQUIPMENT_ADDON" => _addon,
                "RECIPE_ESSENCE" => _recipe,
                "MONSTER_ESSENCE" => _monster,
                _ => l.Name.EndsWith("_MAJOR_TYPE") || l.Name.EndsWith("_SUB_TYPE") || l.Name == "TALK_PROC" ? null : _essence,
            };
            if (target is null) continue;
            foreach (var it in l.Items)
                if (it.Id.Length > 0 && it.Id != "0") target.TryAdd(it.Id, (l, it));
            if (l.Name == "MONSTER_ESSENCE")
                foreach (var it in l.Items) _essence.TryAdd(it.Id, (l, it));
        }
    }

    public (ItemList Block, Item Item)? Resolve(FieldKind kind, string id)
    {
        if (id is "" or "0" or "-1") return null;
        var d = kind switch
        {
            FieldKind.AddonRef => _addon,
            FieldKind.RecipeRef => _recipe,
            FieldKind.MonsterRef => _monster,
            FieldKind.ItemRef => _essence,
            _ => null,
        };
        return d is not null && d.TryGetValue(id, out var hit) ? hit : null;
    }

    /// <summary>Itens que referenciam o ID (índice reverso construído na 1ª chamada).</summary>
    public IReadOnlyList<Ref> UsedBy(string id)
    {
        if (_usedBy is null)
        {
            var d = new Dictionary<string, List<Ref>>();
            foreach (var l in _module.Lists)
                foreach (var it in l.Items)
                    foreach (var (name, f) in it.Fields)
                    {
                        if (f.Value is "" or "0" or "-1" || !FieldKinds.IsReference(FieldKinds.Of(name))) continue;
                        var prob = FieldKinds.ProbabilityFor(name, it.Fields);
                        if (!d.TryGetValue(f.Value, out var list)) d[f.Value] = list = new();
                        list.Add(new Ref(l, it, name, prob is null ? null : it.Fields[prob].Value));
                    }
            _usedBy = d;
        }
        return _usedBy.TryGetValue(id, out var r) ? r : Array.Empty<Ref>();
    }

    public void Invalidate() => _usedBy = null;
}
