using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Regras do servidor para o tasks.data (source_server_153 cgame/gs/task, ATaskTemplMan::LoadTasksFromPack /
/// AddOneTaskTempl): quest com ID repetido é ignorada ("Dup Task Found"); item, monstro ou NPC que a quest aponta e não
/// existe no elements.data não derruba o gs, mas a quest não entrega a recompensa, não conta a morte ou não aparece no NPC.
/// Mesmos campos que o "Mudar ID" segue (IdRefactor.QuestFieldPoints). Salvar recusa só problemas NOVOS (ItemRules).
/// </summary>
public static class QuestRules
{
    public static List<ItemRules.Problem> Check(TasksFile tasks, Module m)
    {
        var list = new List<ItemRules.Problem>();
        if (!tasks.FullSchema) return list;
        string QName(QuestNode q) => TasksFile.SplitColor(q.Name).Text;

        foreach (var g in tasks.AllNodes().GroupBy(q => q.Id).Where(g => g.Count() > 1))
            list.Add(new("quest-dup", "TASKS", g.Key.ToString(), QName(g.First()),
                string.Format(UI.Loc.T("rule.questDup", "ID de quest repetido ({0}×): o gs ignora a segunda"), g.Count())));

        HashSet<string> Ids(params string[] blocks) => blocks.SelectMany(b => m.FindListByName(b)?.Items ?? new()).Select(i => i.Id).ToHashSet();
        var mobs = Ids("MONSTER_ESSENCE");
        var npcs = Ids("NPC_ESSENCE");
        var items = m.Lists.Where(l => IdSpaces.Of(l.Name) == IdSpaces.Space.Essence && l.Name is not ("MONSTER_ESSENCE" or "NPC_ESSENCE"))
            .SelectMany(l => l.Items).Select(i => i.Id).ToHashSet();
        if (mobs.Count + npcs.Count + items.Count == 0) return list;

        foreach (var q in tasks.AllNodes())
            foreach (var (kind, v, field) in Refs(q))
            {
                var ok = kind switch { 'M' => mobs, 'N' => npcs, _ => items };
                if (ok.Contains(v)) continue;
                list.Add(new("quest-ref", "TASKS", q.Id.ToString(), QName(q),
                    string.Format(UI.Loc.T("rule.questRef", "{0} {1} (campo {2}) não existe no elements.data: a quest não funciona"),
                        kind switch { 'M' => UI.Loc.T("rule.kMonster", "monstro"), 'N' => "NPC", _ => UI.Loc.T("rule.kItem", "item") }, v, field)));
            }
        return list;
    }

    // IDs que a quest aponta (M monstro, N NPC, I item), guardados por nó e refeitos só quando os bytes dele mudam:
    // montar os campos das ~36 mil quests leva ~30 s; com o cache, salvar confere só as quests editadas
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QuestNode, Cached> Cache = new();
    sealed record Cached(int Hash, List<(char Kind, string Value, string Field)> Refs);

    static List<(char Kind, string Value, string Field)> Refs(QuestNode q)
    {
        var h = new HashCode(); h.AddBytes(q.Fixed); int hash = h.ToHashCode();
        if (Cache.TryGetValue(q, out var c) && c.Hash == hash) return c.Refs;
        // campos montados à parte (sem guardar no nó): roda em segundo plano sem mexer na quest aberta na tela
        var fields = q.FieldsLoaded ? q.Fields.ToList() : q.FieldsFactory?.Invoke(q) ?? q.Fields;
        var refs = new List<(char, string, string)>();
        foreach (var f in fields)
        {
            if (!f.Editable) continue;
            char kind = IdRefactor.QuestFieldPoints("MONSTER_ESSENCE", f) ? 'M' : IdRefactor.QuestFieldPoints("NPC_ESSENCE", f) ? 'N'
                : IdRefactor.QuestFieldPoints("ITEM", f) ? 'I' : ' ';
            if (kind == ' ' || q.Get(f) is not { } v || v is "" or "0" || v.StartsWith('-')) continue;
            refs.Add((kind, v, f.Name));
        }
        lock (Cache) Cache.AddOrUpdate(q, new Cached(hash, refs));
        return refs;
    }
}
