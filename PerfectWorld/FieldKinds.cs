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
    ServiceRef,  // ID de registro de serviço do NPC (id_sell_service -> NPC_SELL_SERVICE); não é quest
    Probability, // float 0..1 exibido em %
    ClassMask,   // máscara de classes (PW-KB §S24)
    ProcMask,    // regras do item (proc_type): cair, vender, trocar… (BitMasks.Proc)
    ImmuneMask,  // imunidades do monstro (immune_type) (BitMasks.Immune)
    FoodMask,    // comida do mascote (food_mask) (BitMasks.Food)
    ServiceMask, // serviços combinados do NPC (combined_services) (BitMasks.Services)
    Waypoint,    // ponto de teleporte (id_to_discover, targets_N_idTarget) (WorldTargets)
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

    /// <summary>
    /// Tipo do campo levando o bloco em conta, quando o nome sozinho engana: no NPC_MAKE_SERVICE os
    /// pages_N_id_goods_M são IDs de RECEITA (em 22 versões antigas o NPC_SELL_SERVICE usa o mesmo nome para itens).
    /// </summary>
    public static FieldKind Of(string? block, string field) =>
        block == "NPC_MAKE_SERVICE" && Regex.IsMatch(field, @"^pages_\d+_id_goods_\d+$") ? FieldKind.RecipeRef : Of(field);

    static FieldKind Classify(string field)
    {
        var n = field.ToLowerInvariant();
        if (n is "id" or "id_major_type" or "id_sub_type" or "id_type") return FieldKind.Plain;
        if (n.StartsWith("file_") || n.StartsWith("pllfiles")) return FieldKind.File;
        if (n.Contains("color")) return FieldKind.Color;
        if (n is "character_combo_id" || n.Contains("occupation") || n.EndsWith("class_mask")) return FieldKind.ClassMask;
        // NPC_ESSENCE: id_<serviço>_service aponta para o registro do bloco NPC_<SERVIÇO>_SERVICE (antes das regras de quest/skill)
        if (Regex.IsMatch(n, @"^id_[a-z_]+_service$")) return FieldKind.ServiceRef;
        if (n == "proc_type") return FieldKind.ProcMask;
        if (n == "immune_type") return FieldKind.ImmuneMask;
        if (n == "food_mask") return FieldKind.FoodMask;
        if (n == "combined_services") return FieldKind.ServiceMask;
        if (n == "id_to_discover" || Regex.IsMatch(n, @"^targets_\d+_idtarget$")) return FieldKind.Waypoint;
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

    /// <summary>
    /// Sim/não (flag): o campo é ft_bool no bloco (lista do Rody, FlagFields) e o valor é 0 ou 1.
    /// </summary>
    public static bool IsFlag(string block, string field, string value) =>
        (value == "0" || value == "1") && FlagFields.Contains(block, field);

    public static bool IsReference(FieldKind k) =>
        k is FieldKind.ItemRef or FieldKind.AddonRef or FieldKind.RecipeRef or FieldKind.MonsterRef or FieldKind.ServiceRef;

    /// <summary>
    /// Campo de probabilidade "irmão" de uma referência (ex.: drop_matters_3_id → drop_matters_3_probability,
    /// addons_2_id_addon → addons_2_probability_addon). Null se não houver.
    /// </summary>
    public static string? ProbabilityFor(string refField, IReadOnlyDictionary<string, Field> fields)
    {
        foreach (var cand in ProbabilityCandidates(refField))
            if (fields.ContainsKey(cand)) return cand;
        return null;
    }

    internal static string[] ProbabilityCandidates(string refField) => new[]
        {
            Regex.Replace(refField, @"id_?(\w*)$", "probability_$1").TrimEnd('_'),
            Regex.Replace(refField, @"_id$", "_probability"),
            Regex.Replace(refField, @"_id_(\w+)$", "_probability_$1"),
            refField.Replace("id_", "probability_"),
        }.Where(cand => cand != refField).Distinct(StringComparer.Ordinal).ToArray();

    // Classes na ordem dos bits (PW-KB §S24).
    // bit i: BM=1 WIZ=2 PSY=4 VEN=8 BAR=16 AS=32 AR=64 CLE=128 SE=256 MY=512 (PW-KB §S24), nomes da versão BR
    static readonly string[] Classes = { "Guerreiro", "Mago", "Espiritualista", "Feiticeira", "Bárbaro", "Mercenário", "Arqueiro", "Sacerdote", "Arcano", "Místico", "Lâmina Sombria", "Tempestade" };

    /// <summary>
    /// Tradutor de textos de interface. A UI liga isto ao Loc.T (ver Loc); o padrão devolve o português.
    /// Gancho em vez de chamar o Loc direto porque este arquivo também compila no projeto de testes.
    /// </summary>
    public static Func<string, string, string> Translate { get; set; } = static (_, pt) => pt;

    public static string ClassName(int bit) => bit >= 0 && bit < Classes.Length
        ? Translate("class." + bit, Classes[bit])
        : $"{Translate("class.generic", "Classe")} {bit + 1}";

    public static string ClassNames(string value)
    {
        if (!long.TryParse(value, out var m) || m <= 0) return "";
        var names = Enumerable.Range(0, 64).Where(i => (m & (1L << i)) != 0).Select(ClassName).ToList();
        return names.Count == 0 ? "" : names.Count >= 10 ? Translate("class.all", "Todas as classes") : string.Join(", ", names);
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

    public RefIndex(Module module, LoadContext? context = null)
    {
        _module = module;
        long done = 0, total = module.Lists.Sum(l => (long)l.Items.Count);
        foreach (var l in module.Lists)
        {
            context?.Check();
            // espaço de IDs do jogo (IdSpaces): addon de monstro conta como addon; gravação/atributo aleatório como receita
            var target = l.Name == "MONSTER_ESSENCE" ? _monster
                : l.Name.EndsWith("_MAJOR_TYPE") || l.Name.EndsWith("_SUB_TYPE") ? null
                : IdSpaces.Of(l.Name) switch
                {
                    IdSpaces.Space.Addon => _addon,
                    IdSpaces.Space.Recipe => _recipe,
                    IdSpaces.Space.Essence => _essence,
                    _ => null,
                };
            if (target is null) { done += l.Items.Count; continue; }
            foreach (var it in l.Items)
            {
                if ((done++ & 255) == 0)
                {
                    context?.Check();
                    context?.Report(done / (double)Math.Max(1, total), "Indexando IDs", $"{done:N0}/{total:N0} registros");
                }
                if (it.Id.Length > 0 && it.Id != "0") target.TryAdd(it.Id, (l, it));
            }
            if (l.Name == "MONSTER_ESSENCE")
                foreach (var it in l.Items) _essence.TryAdd(it.Id, (l, it));
        }
        context?.Report(1, "Indexando IDs", $"{total:N0} registros", force: true);
    }

    public (ItemList Block, Item Item)? Resolve(FieldKind kind, string id)
    {
        if (id is "" or "0" or "-1") return null;
        var d = kind switch
        {
            FieldKind.AddonRef => _addon,
            FieldKind.RecipeRef => _recipe,
            FieldKind.MonsterRef => _monster,
            FieldKind.ItemRef or FieldKind.ServiceRef => _essence,   // serviços de NPC também estão no espaço de IDs essence
            _ => null,
        };
        return d is not null && d.TryGetValue(id, out var hit) ? hit : null;
    }

    /// <summary>Itens que referenciam o ID (índice reverso construído na 1ª chamada).</summary>
    public IReadOnlyList<Ref> UsedBy(string id)
    {
        if (_usedBy is null) Prepare();
        return CachedUsedBy(id);
    }

    /// <summary>
    /// Quem referencia este registro. O índice é por número, e o mesmo número pode existir em espaços de ID
    /// diferentes (PW-KB §S28: o item 503 e o addon de monstro 503); aqui só ficam as referências cujo campo
    /// aponta mesmo para este registro.
    /// </summary>
    public IReadOnlyList<Ref> UsedBy(Item target) =>
        UsedBy(target.Id).Where(r => Resolve(FieldKinds.Of(r.Block.Name, r.Field), target.Id)?.Item == target).ToList();

    public bool IsPrepared => _usedBy is not null;
    public IReadOnlyList<Ref> CachedUsedBy(string id) =>
        _usedBy is not null && _usedBy.TryGetValue(id, out var found) ? found : Array.Empty<Ref>();

    /// <summary>Pré-compila por bloco do CFG os campos de referência e nomes de probabilidade; publica o índice só quando completo, nunca parcial ao cancelar.</summary>
    public void Prepare(LoadContext? context = null)
    {
        context?.Check();
        if (_usedBy is not null) return;
        var d = new Dictionary<string, List<Ref>>();
        long done = 0, total = _module.Lists.Sum(l => (long)l.Items.Count);
        foreach (var list in _module.Lists)
        {
            context?.Check();
            var schema = (_module.Source as ElementsLayout)?.Blocks.FirstOrDefault(b => ReferenceEquals(b.List, list))?.Block;
            IEnumerable<string> names = schema is not null ? schema.Fields.Select(f => f.Name)
                : list.Items.SelectMany(it => it.Fields.Keys).Distinct(StringComparer.Ordinal);
            var plans = names.Where(n => { context?.Check(); return FieldKinds.IsReference(FieldKinds.Of(list.Name, n)); })
                .Select(n => (Name: n, Probabilities: FieldKinds.ProbabilityCandidates(n))).ToArray();
            foreach (var item in list.Items)
            {
                if ((done++ & 127) == 0)
                {
                    context?.Check();
                    context?.Report(done / (double)Math.Max(1, total), "Indexando referências", $"{done:N0}/{total:N0} registros · {list.Name}");
                }
                foreach (var plan in plans)
                {
                    if (!item.Fields.TryGetValue(plan.Name, out var field) || field.Value is "" or "0" or "-1") continue;
                    string? probability = null;
                    foreach (var name in plan.Probabilities)
                        if (item.Fields.TryGetValue(name, out var candidate)) { probability = candidate.Value; break; }
                    if (!d.TryGetValue(field.Value, out var uses)) d[field.Value] = uses = new();
                    uses.Add(new Ref(list, item, plan.Name, probability));
                }
            }
        }
        context?.Check();
        context?.Report(1, "Indexando referências", $"{total:N0} registros", force: true);
        context?.Check();
        _usedBy = d;
    }

    public void Invalidate() => _usedBy = null;
}
