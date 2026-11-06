using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// O que o painel de criação pede ao Mundo 3D: escolher NPC clicando, marcar área arrastando no chão, mostrar as áreas da quest
/// (com a selecionada destacada; -1 = nenhuma), a tag do mapa aberto (0 = não sei) e escolher monstro clicando num ponto de monstro.
/// </summary>
internal sealed record QuestWorld(Action<Action<int>> PickNpc, Action<Action<QuestArea>> PickArea, Action<IReadOnlyList<QuestArea>, int> ShowAreas, Func<int> MapTag,
    Action<Action<int>> PickMonster, Action<Action<int>> PickMine);

/// <summary>NPC que leva a um ponto de teleporte, com a taxa e o nível do destino.</summary>
internal readonly record struct TeleportLink(int Npc, string Name, int Fee, int Level);

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
    /// <summary>Exclui a quest (com as subquests) e tira o ID dos serviços dos NPCs, num passo do desfazer. Null = deu certo.
    /// <paramref name="withNext"/>: também as quests seguintes da cadeia (<see cref="NextQuests"/>), no mesmo passo.</summary>
    string? Delete(int questId, bool withNext = false);
    /// <summary>Quests seguintes da cadeia desta (ID + nome), para perguntar se saem junto ao excluir.</summary>
    IReadOnlyList<(int Id, string Name)> NextQuests(int questId);
    /// <summary>Quest que não existe mais no tasks.data (apagada fora daqui): tira o ID dos serviços dos NPCs, num passo do desfazer. Null = deu certo.</summary>
    string? RemoveMissing(int questId);
    /// <summary>Nome de NPC, monstro ou item do elements; null se o ID não existe.</summary>
    string? NameOf(int id);
    /// <summary>Todos os itens do elements.data, para a janela de busca.</summary>
    IReadOnlyList<(int Id, string Name, string Block)> AllItems() => [];
    /// <summary>Janela de itens: ID e quantidade escolhidos.</summary>
    (int Id, int Quantity)? PickItemId(IWin32Window owner, bool quantity) => null;
    /// <summary>Janela de criaturas (ícone, dica e modelo 3D); npcOnly mostra só NPCs.</summary>
    int? PickCreatureId(IWin32Window owner, bool npcOnly) => null;
    int? PickRecipeId(IWin32Window owner) => null;
    int RecipeProduct(int recipe) => 0;
    /// <summary>Habilidades de fabricação do jogo (ferreiro, alfaiate...): as que as receitas usam.</summary>
    IReadOnlyList<(int Id, string Name)> MakeSkills() => [];
    List<(int Id, int Num)> RecipeMaterials(int recipe) => [];
    /// <summary>Ferramenta Criar: o que impede de criar (vazio = pode) e criação dos registros; devolve o ID do NPC.</summary>
    IReadOnlyList<string> ValidateNpc(PerfectWorld.NpcDraft draft) => [];
    int? BuildNpc(PerfectWorld.NpcDraft draft) => null;
    /// <summary>Ferramenta Teleportes: quem leva e quem descobre o ponto, e as mudanças no elements (com Ctrl+Z). Null = deu certo.</summary>
    IReadOnlyList<TeleportLink> TeleportersTo(int point) => [];
    IReadOnlyList<(int Id, string Name)> DiscoverersOf(int point) => [];
    int DiscoverOf(int npc) => 0;
    string? SetDiscover(int npc, int point) => null;
    /// <summary>Quantos NPCs usam o serviço de teleporte deste NPC.</summary>
    int ServiceUsers(int npc) => 0;
    /// <summary>Acrescenta o destino ou muda a taxa e o nível dele. <paramref name="copy"/>: cópia do serviço só para este NPC.</summary>
    string? SetTransmit(int npc, int point, int fee, int level, bool copy) => null;
    string? RemoveTransmit(int npc, int point, bool copy) => null;
    IReadOnlyList<PerfectWorld.TeleportStop> StopsOfNpc(int npc) => [];
    /// <summary>Troca todos os destinos do NPC.</summary>
    string? SetStops(int npc, IReadOnlyList<PerfectWorld.TeleportStop> stops, bool copy) => null;
    /// <summary>Nome do item Atlas Geográfico; null se este cliente não tem o item ou a quest dele.</summary>
    string? AtlasName => null;
    bool AtlasHas(int point) => false;
    string? SetAtlas(PerfectWorld.WorldTarget point, bool on) => null;
    /// <summary>Ferramenta Criar: rascunho do NPC que já existe e gravação das mudanças. Null = deu certo.</summary>
    PerfectWorld.NpcDraft? LoadNpc(int id) => null;
    int ServiceUsersOf(PerfectWorld.NpcDraft draft) => 0;
    string? UpdateNpc(PerfectWorld.NpcDraft draft, bool copy) => null;
    PerfectWorld.RecipeDraft? ReadRecipe(int id) => null;
    /// <summary>Favoritos e recentes do editor de elements, para a janela de busca.</summary>
    IReadOnlyList<int> FavoriteIds() => [];
    IReadOnlyList<int> RecentIds() => [];
    /// <summary>Item escolhido na janela de busca: entra nos recentes.</summary>
    void ItemPicked(int id) { }
    /// <summary>Preço de venda do item nas lojas (shop_price); null se o item não tem.</summary>
    int? ShopPrice(int id) => null;
    /// <summary>Ícone do item, para a janela de escolher item.</summary>
    Image? ItemIcon(int id) => null;
    /// <summary>Linhas da dica do item, como na lista do editor de elements.</summary>
    Bitmap? TipImage(int id) => null;
    /// <summary>Pasta element do cliente aberto.</summary>
    string? ElementDir => null;
    bool IsNpc(int id);
    /// <summary>Ícone de quest sobre o NPC (surfaces\ingame\PateQuest.tga, 14 ícones de 30 px, ordem QI_ do cliente); null se não achou.</summary>
    Image? QuestIcon(int index);
    bool IsMonster(int id);
    /// <summary>Item de quest (TASKMATTER_ESSENCE: vai para o inventário de quest), não item comum.</summary>
    bool IsQuestItem(int id);
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
