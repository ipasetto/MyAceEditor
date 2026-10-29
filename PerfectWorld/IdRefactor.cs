using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>O que muda ao trocar o ID de um registro: o próprio ID e cada referência a ele.</summary>
public sealed record IdChangePlan(ItemList Block, Item Item, string OldId, string NewId,
    List<RefIndex.Ref> Elements, List<(QuestNode Quest, TaskField Field)> Quests, List<(GShopFile Shop, GShopItem Item, bool Gift)> Shop,
    List<(string Name, NpcGenFile File, int Id)> NpcGen)
{
    public int Total => 1 + Elements.Count + Quests.Count + Shop.Count + NpcGen.Count;
}

/// <summary>
/// Refatorar ID (como o "Refator" do RAE, código nosso): troca o ID do registro e atualiza quem aponta para ele no
/// elements.data (pelo índice de referências, respeitando os espaços de ID — PW-KB §S28), nas quests (campos de
/// item/monstro/NPC do tasks.data), na loja (item vendido e presente) e no npcgen.data (monstros e NPCs nos pontos de
/// criatura, minas nos pontos de coleta; mundo e instâncias).
/// </summary>
public static class IdRefactor
{
    /// <summary>Mensagem do problema, ou null se o novo ID serve.</summary>
    public static string? Validate(Module module, ItemList block, Item item, string newId)
    {
        if (!int.TryParse(newId, out var n) || n <= 0) return UI.Loc.T("idref.positive", "O novo ID precisa ser um número maior que zero.");
        if (newId == item.Id) return UI.Loc.T("idref.same", "O novo ID é igual ao atual.");
        var space = IdSpaces.Of(block.Name);
        foreach (var l in module.Lists.Where(l => IdSpaces.Of(l.Name) == space))
            if (l.Items.FirstOrDefault(i => i.Id == newId) is { } other)
                return string.Format(UI.Loc.T("idref.exists", "O ID {0} já existe em {1} ({2})."), newId, l.Name, other.Name);
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
        if (f.Name.StartsWith("m_") || f.Name.Contains(".m_"))   // chaves do TaskLayout (versões ≠ 93)
        {
            var leaf = f.Name[(f.Name.LastIndexOf('.') + 1)..];
            return blockName switch
            {
                "MONSTER_ESSENCE" => leaf == "m_ulMonsterTemplId",
                "NPC_ESSENCE" => leaf.Contains("NPC") && !leaf.Contains("Cnt"),
                _ => leaf is "m_ulItemTemplId" or "m_ulDropItemId",
            };
        }
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

    public static IdChangePlan Plan(RefIndex refs, ItemList block, Item item, string newId, TasksFile? tasks, IEnumerable<GShopFile> shops,
        IEnumerable<(string Name, NpcGenFile File)>? npcgens = null)
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
        var gen = new List<(string, NpcGenFile, int)>();
        if (int.TryParse(item.Id, out var oldId) && npcgens is not null)
            foreach (var (name, f) in npcgens)
                for (int k = CountInNpcGen(f, block.Name, oldId); k > 0; k--) gen.Add((name, f, oldId));
        return new IdChangePlan(block, item, item.Id, newId, elements, quests, shop, gen);
    }

    // npcgen: monstros e NPCs nos pontos de criatura (o tipo 0 do mundo 1.4.4 tem 615 NPCs), minas nos pontos de coleta
    static IEnumerable<Action<int>> NpcGenSlots(NpcGenFile f, string blockName, int id)
    {
        if (blockName == "MINE_ESSENCE")
        {
            foreach (var r in f.Resources) foreach (var x in r.Items) if (x.Id == id) yield return v => x.Id = v;
            yield break;
        }
        if (blockName is not ("MONSTER_ESSENCE" or "NPC_ESSENCE")) yield break;
        foreach (var s in f.Spawns) foreach (var c in s.Creatures) if (c.Id == id) yield return v => c.Id = v;
    }

    public static int CountInNpcGen(NpcGenFile f, string blockName, int id) => NpcGenSlots(f, blockName, id).Count();

    /// <summary>Troca o ID nos pontos do npcgen; devolve quantos mudaram.</summary>
    public static int ReplaceInNpcGen(NpcGenFile f, string blockName, int from, int to)
    {
        var slots = NpcGenSlots(f, blockName, from).ToList();
        foreach (var set in slots) set(to);
        return slots.Count;
    }
}
