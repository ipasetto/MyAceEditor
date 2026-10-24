using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>O que muda ao trocar o ID de um registro: o próprio ID e cada referência a ele.</summary>
public sealed record IdChangePlan(ItemList Block, Item Item, string OldId, string NewId,
    List<RefIndex.Ref> Elements, List<(QuestNode Quest, TaskField Field)> Quests, List<(GShopFile Shop, GShopItem Item, bool Gift)> Shop)
{
    public int Total => 1 + Elements.Count + Quests.Count + Shop.Count;
}

/// <summary>
/// Refatorar ID (como o "Refator" do RAE, código nosso): troca o ID do registro e atualiza quem aponta para ele no
/// elements.data (pelo índice de referências, respeitando os espaços de ID — PW-KB §S28), nas quests (campos de
/// item/monstro/NPC do tasks.data) e na loja (item vendido e presente). O npcgen.data ainda não é lido.
/// </summary>
public static class IdRefactor
{
    /// <summary>Mensagem do problema, ou null se o novo ID serve.</summary>
    public static string? Validate(Module module, ItemList block, Item item, string newId)
    {
        if (!int.TryParse(newId, out var n) || n <= 0) return "O novo ID precisa ser um número maior que zero.";
        if (newId == item.Id) return "O novo ID é igual ao atual.";
        var space = IdSpaces.Of(block.Name);
        foreach (var l in module.Lists.Where(l => IdSpaces.Of(l.Name) == space))
            if (l.Items.FirstOrDefault(i => i.Id == newId) is { } other)
                return $"O ID {newId} já existe em {l.Name} ({other.Name}).";
        return null;
    }

    /// <summary>Primeiro ID livre depois do maior do espaço de IDs do bloco.</summary>
    public static int NextFreeId(Module module, ItemList block)
    {
        var space = IdSpaces.Of(block.Name);
        int max = 0;
        foreach (var l in module.Lists.Where(l => IdSpaces.Of(l.Name) == space))
            foreach (var i in l.Items) if (int.TryParse(i.Id, out var v) && v > max) max = v;
        return max + 1;
    }

    // Campo de quest que guarda um ID do tipo do bloco (os nomes vêm do TaskSchema93)
    static bool QuestFieldPoints(string blockName, TaskField f)
    {
        var n = f.Name.ToLowerInvariant();
        bool monster = n.StartsWith("monstro") && n.EndsWith(": id");
        bool npc = n.Contains("npc");
        bool item = (n.EndsWith(": id") && !n.StartsWith("monstro")) || n.EndsWith(": item que cai");
        return blockName switch
        {
            "MONSTER_ESSENCE" => monster,
            "NPC_ESSENCE" => npc,
            _ => item,
        };
    }

    public static IdChangePlan Plan(RefIndex refs, ItemList block, Item item, string newId, TasksFile? tasks, IEnumerable<GShopFile> shops)
    {
        // o tipo do campo depende do bloco (NPC_MAKE_SERVICE goods = receita): FieldKinds.Of(bloco, campo)
        var elements = refs.UsedBy(item).ToList();
        var quests = new List<(QuestNode, TaskField)>();
        if (tasks is { FullSchema: true } && IdSpaces.Of(block.Name) == IdSpaces.Space.Essence)
            foreach (var q in tasks.AllNodes())
                foreach (var f in q.Fields)
                    if (f.Editable && QuestFieldPoints(block.Name, f) && q.Get(f) == item.Id) quests.Add((q, f));
        var shop = new List<(GShopFile, GShopItem, bool)>();
        if (IdSpaces.Of(block.Name) == IdSpaces.Space.Essence && block.Name is not ("MONSTER_ESSENCE" or "NPC_ESSENCE"))
            foreach (var s in shops)
                foreach (var it in s.Items)
                {
                    if (it.ItemId.ToString() == item.Id) shop.Add((s, it, false));
                    if (it.GiftId.ToString() == item.Id) shop.Add((s, it, true));
                }
        return new IdChangePlan(block, item, item.Id, newId, elements, quests, shop);
    }
}
