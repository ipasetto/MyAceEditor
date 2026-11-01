using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// O que o painel de criação pede ao Mundo 3D: escolher NPC clicando, marcar área arrastando no chão, mostrar as áreas da quest
/// (com a selecionada destacada; -1 = nenhuma), a tag do mapa aberto (0 = não sei) e escolher monstro clicando num ponto de monstro.
/// </summary>
internal sealed record QuestWorld(Action<Action<int>> PickNpc, Action<Action<QuestArea>> PickArea, Action<IReadOnlyList<QuestArea>, int> ShowAreas, Func<int> MapTag,
    Action<Action<int>> PickMonster, Action<Action<int>> PickMine);

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
    /// <summary>Rascunho com a quest que já existe (editar); Commit troca a quest no lugar. Null + motivo se o assistente não cobre essa quest.</summary>
    QuestDraft? EditDraft(int questId, out string? why);
    /// <summary>Texto da pergunta antes de excluir (quest, subquests, NPCs e quests que apontam para ela); null se a quest não existe.</summary>
    string? DeleteAsk(int questId);
    /// <summary>Exclui a quest (com as subquests) e tira o ID dos serviços dos NPCs, num passo do desfazer. Null = deu certo.</summary>
    string? Delete(int questId);
    /// <summary>Quest que não existe mais no tasks.data (apagada fora daqui): tira o ID dos serviços dos NPCs, num passo do desfazer. Null = deu certo.</summary>
    string? RemoveMissing(int questId);
    /// <summary>Nome de NPC, monstro ou item do elements; null se o ID não existe.</summary>
    string? NameOf(int id);
    bool IsNpc(int id);
    bool IsMonster(int id);
    /// <summary>O que um item de coleta (MINE_ESSENCE) dá: materiais com chance &gt; 0, do mais provável ao menos; vazio se não é coleta.</summary>
    IReadOnlyList<(int Item, float Prob)> MineProducts(int mineId);
    IReadOnlyList<(int Id, string Name, int Giver)> SearchQuests(string text, int max);
    /// <summary>Esta quest passa a ser dada pelo NPC (serviço do NPC + campo "NPC que dá"). Null = deu certo.</summary>
    string? LinkExisting(int npcId, int questId);
    /// <summary>Opções de requisito (reward false) ou prêmio que esta versão tem.</summary>
    IReadOnlyList<QuestOption> Options(bool reward);
    /// <summary>Campos para "Outro campo (avançado)": chave, rótulo e dica.</summary>
    IReadOnlyList<(string Key, string Label, string Tip)> FreeFields(bool reward);
    string? QuestName(int id);
    /// <summary>Desfaz / refaz o último passo do editor (criar ou ligar quest é um passo só). false = nada a fazer.</summary>
    bool Undo();
    bool Redo();
}
