namespace MyAceEditor.PerfectWorld;

/// <summary>Tipo de entrada de uma opção de requisito/prêmio.</summary>
public enum QOType { Number, Range, Flag, Classes, Gender, Quests, Items, Field }

/// <summary>
/// Opção de requisito ou prêmio da quest (docs/QUESTS-3D.md): o usuário escolhe o tipo na lista e informa o valor.
/// Keys = chaves do TaskLayout que a opção grava (a 1ª decide se a versão tem a opção). Textos via qo.&lt;id&gt; / qo.&lt;id&gt;.h.
/// </summary>
public sealed record QuestOption(string Id, bool Reward, QOType Type, string Label, string Hint, params string[] Keys)
{
    public string Text => FieldKinds.Translate("qo." + Id, Label);
    public string Tip => FieldKinds.Translate("qo." + Id + ".h", Hint);
}

/// <summary>Opção escolhida e o valor (long, (long, long), bool, List&lt;int&gt;, List&lt;(int, int)&gt; ou (string chave, string valor)).</summary>
public sealed record QuestChoice(QuestOption Option, object Value);

public static class QuestOptions
{
    public const string AwardPrefix = "m_Award_S.";

    public static readonly QuestOption[] All =
    {
        // requisitos (para aceitar)
        new("classes", false, QOType.Classes, "Classes", "Só estas classes podem aceitar.", "m_ulOccupations", "m_Occupations"),
        new("gender", false, QOType.Gender, "Gênero", "Só este gênero pode aceitar.", "m_ulGender"),
        new("preTasks", false, QOType.Quests, "Quests anteriores", "Quests que precisam estar concluídas antes (IDs separados por vírgula).", "m_ulPremise_Task_Count", "m_ulPremise_Tasks"),
        new("mutex", false, QOType.Quests, "Quests exclusivas", "Quests que não podem estar ativas junto com esta.", "m_ulMutexTaskCount", "m_ulMutexTasks"),
        new("premItems", false, QOType.Items, "Itens exigidos", "Itens que o jogador precisa ter para aceitar.", "m_ulPremItems", "m_PremItems"),
        new("premAnyItem", false, QOType.Flag, "Basta um dos itens exigidos", "Ligado: ter qualquer um dos itens exigidos já basta.", "m_bPremItemsAnyOne"),
        new("givenItems", false, QOType.Items, "Itens dados ao aceitar", "O jogador recebe estes itens ao aceitar a quest.", "m_ulGivenItems", "m_GivenItems"),
        new("deposit", false, QOType.Number, "Ouro no inventário", "Ouro que o jogador precisa ter para aceitar.", "m_ulPremise_Deposit"),
        new("repu", false, QOType.Range, "Reputação", "Reputação mínima e máxima (0 = sem limite).", "m_lPremise_Reputation", "m_lPremise_RepuMax"),
        new("spouse", false, QOType.Flag, "Precisa ser casado", "Só jogadores casados podem aceitar.", "m_bPremise_Spouse"),
        new("gm", false, QOType.Flag, "Somente GM", "Só GMs podem aceitar (útil para testar).", "m_bGM"),
        new("timeLimit", false, QOType.Number, "Tempo limite (s)", "Segundos para concluir depois de aceitar.", "m_ulTimeLimit"),
        new("redo", false, QOType.Flag, "Pode refazer", "Pode ser feita de novo depois de concluída.", "m_bCanRedo"),
        new("reqField", false, QOType.Field, "Outro campo (avançado)", "Qualquer outro campo de requisito do formato; escolha o campo e o valor.", ""),
        // prêmios (além de ouro, experiência, espírito, reputação e itens)
        new("newTask", true, QOType.Quests, "Nova quest ao concluir", "Quest dada automaticamente ao concluir (cadeia). Só a 1ª conta.", AwardPrefix + "m_ulNewTask"),
        new("bank", true, QOType.Number, "Espaços no banco", "Espaços a mais no banco.", AwardPrefix + "m_ulStorehouseSize"),
        new("inventory", true, QOType.Number, "Espaços no inventário", "Espaços a mais no inventário.", AwardPrefix + "m_lInventorySize"),
        new("petInv", true, QOType.Number, "Espaços no inventário do mascote", "Espaços a mais no inventário do mascote.", AwardPrefix + "m_ulPetInventorySize"),
        new("relay", true, QOType.Number, "Estação de teleporte", "ID da estação de teleporte liberada.", AwardPrefix + "m_ulNewRelayStation"),
        new("factionContrib", true, QOType.Number, "Contribuição de clã", "Pontos de contribuição para o clã.", AwardPrefix + "m_iFactionContrib"),
        new("skill", true, QOType.Range, "Habilidade", "ID da habilidade e nível dados ao concluir.", AwardPrefix + "m_iAwardSkillID", AwardPrefix + "m_iAwardSkillLevel"),
        new("levCo", true, QOType.Flag, "Multiplicar pelo nível", "O prêmio é multiplicado por um fator do nível do jogador.", AwardPrefix + "m_bUseLevCo"),
        new("msg", true, QOType.Flag, "Anunciar no chat", "Mostra uma mensagem no chat quando alguém conclui.", AwardPrefix + "m_bSendMsg"),
        new("awardField", true, QOType.Field, "Outro campo (avançado)", "Qualquer outro campo de prêmio do formato; escolha o campo e o valor.", ""),
    };

    static string Bit(string key) => key.StartsWith("m_b") ? key[3..] : key;

    /// <summary>Posição do bit em CombinedSwitches (v135+) ou -1 se o campo não é um bit nesta versão.</summary>
    public static int SwitchIndex(int version, string key)
    {
        if (version < 135 || key.Contains('.')) return -1;
        var name = Bit(key);
        int idx = 0;
        foreach (var s in TaskLayout.Switches)
        {
            if (!TaskVer.Has(version, s)) continue;
            if (s.Name == name) return idx;
            idx++;
        }
        return -1;
    }

    /// <summary>A versão tem a opção (o campo existe no bloco do modelo ou é um bit de CombinedSwitches).</summary>
    public static bool Available(QuestOption o, int version, TaskRec model) => o.Type == QOType.Field
        || model.V.ContainsKey(o.Keys[0]) || model.V.ContainsKey(o.Keys[0] + "[0]") || SwitchIndex(version, o.Keys[0]) >= 0;

    /// <summary>Liga/desliga um campo booleano: bit de CombinedSwitches na v135+ ou o próprio campo.</summary>
    public static void SetFlag(TaskRec rec, int version, string key, bool on)
    {
        int idx = SwitchIndex(version, key);
        if (idx >= 0 && rec.V.TryGetValue("CombinedSwitches", out var o) && o is byte[] b && idx / 8 < b.Length)
        {
            var c = (byte[])b.Clone();
            if (on) c[idx / 8] |= (byte)(1 << idx % 8); else c[idx / 8] &= (byte)~(1 << idx % 8);
            rec.V["CombinedSwitches"] = c;
            return;
        }
        rec.V[key] = on ? 1L : 0L;
    }

    /// <summary>Tamanho máximo das listas fixas por versão (0 = sem limite fixo).</summary>
    public static int MaxCount(QuestOption o, int version) => o.Id switch
    {
        "classes" => TaskVer.GetMaxOccupations(version),
        "preTasks" => TaskVer.GetMaxPremTaskCount(version),
        "mutex" => TaskVer.GetMaxMutexTasks(version),
        "newTask" => 1,
        _ => 0,
    };

    /// <summary>Grava a escolha no registro do bloco fixo.</summary>
    public static void Apply(TaskRec rec, int version, QuestChoice c)
    {
        var o = c.Option;
        switch (o.Type)
        {
            case QOType.Number: rec.V[o.Keys[0]] = (long)c.Value; break;
            case QOType.Range: { var (a, b) = ((long, long))c.Value; rec.V[o.Keys[0]] = a; rec.V[o.Keys[1]] = b; break; }
            case QOType.Flag: SetFlag(rec, version, o.Keys[0], true); break;
            case QOType.Gender: rec.V[o.Keys[0]] = (long)c.Value; break;
            case QOType.Classes or QOType.Quests:
            {
                var ids = (List<int>)c.Value;
                if (o.Keys.Length == 1) { rec.V[o.Keys[0]] = ids.Count > 0 ? ids[0] : 0L; break; }   // um só valor (nova quest)
                rec.V[o.Keys[0]] = (long)ids.Count;
                for (int i = 0; i < ids.Count; i++) rec.V[$"{o.Keys[1]}[{i}]"] = (long)ids[i];
                break;
            }
            case QOType.Items:
            {
                var items = (List<(int Id, int Count)>)c.Value;
                rec.V[o.Keys[0]] = (long)items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    var p = $"{o.Keys[1]}[{i}].";
                    rec.V[p + "m_ulItemTemplId"] = (long)items[i].Id; rec.V[p + "m_bCommonItem"] = 1L; rec.V[p + "m_ulItemNum"] = (long)items[i].Count;
                    rec.V[p + "m_fProb"] = 1f; rec.V[p + "m_lPeriod"] = 0L;
                }
                if (o.Id == "givenItems") { rec.V["m_ulGivenCmnCount"] = (long)items.Count; rec.V["m_ulGivenTskCount"] = 0L; }
                break;
            }
            case QOType.Field:
            {
                var (key, val) = ((string, string))c.Value;
                rec.V[key] = rec.V.TryGetValue(key, out var old) && old is float
                    ? float.Parse(val.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)
                    : long.Parse(val);
                break;
            }
        }
    }

    /// <summary>Campos simples (não contagem) que "Outro campo" pode mudar: requisitos = topo nas áreas de requisito; prêmios = m_Award_S.*.</summary>
    public static IEnumerable<string> FreeFields(TaskRec model, bool reward)
    {
        foreach (var (k, t) in model.Order)
        {
            if (model.Structural.Contains(k) || k.Contains('#') || t is TT.Bytes or TT.Str16 or TT.Str8 || k.StartsWith("_gap.")) continue;
            if (reward ? k.StartsWith(AwardPrefix) && k.Count(ch => ch == '.') == 1 : !k.Contains('.') && QuestFieldInfo.Group(k) is "qg:req" or "qg:req2" or "qg:team")
                yield return k;
        }
    }
}
