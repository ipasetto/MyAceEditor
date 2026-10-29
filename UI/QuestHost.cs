using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>O que o Mundo 3D pede ao editor para criar e ligar quests (elements + tasks carregados, com desfazer).</summary>
internal interface IQuestHost
{
    /// <summary>Rascunho novo com o NPC como quem dá a quest e o próximo ID livre; null + motivo se não dá para criar.</summary>
    QuestDraft? NewDraft(int giverNpc, out string? why);
    List<string> Validate(QuestDraft d);
    /// <summary>Texto legível do que será criado e ligado.</summary>
    string Summary(QuestDraft d);
    /// <summary>Cria a quest e os vínculos (serviço de dar/receber dos NPCs) num passo do desfazer. Null = deu certo.</summary>
    string? Commit(QuestDraft d);
    /// <summary>Nome de NPC, monstro ou item do elements; null se o ID não existe.</summary>
    string? NameOf(int id);
    bool IsNpc(int id);
    IReadOnlyList<(int Id, string Name, int Giver)> SearchQuests(string text, int max);
    /// <summary>Esta quest passa a ser dada pelo NPC (serviço do NPC + campo "NPC que dá"). Null = deu certo.</summary>
    string? LinkExisting(int npcId, int questId);
}
