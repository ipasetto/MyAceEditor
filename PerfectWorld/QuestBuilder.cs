namespace MyAceEditor.PerfectWorld;

/// <summary>O que o usuário escolhe ao criar uma quest no Mundo 3D (docs/QUESTS-3D.md). Tipo 1 = recado.</summary>
public sealed class QuestDraft
{
    public int Id;
    public string Name = "";
    public int Giver, Receiver;
    /// <summary>Tipo da quest (m_ulType): escolhe o ícone sobre o NPC (cliente EC_NPCServer.cpp). 0 = mantém o do modelo.</summary>
    public int Type;
    /// <summary>Quest chave (m_bKeyTask): ícone laranja de cultivo sobre o NPC, acima de todos os outros (QI_OUT_K).</summary>
    public bool Key;
    public int MinLevel, MaxLevel;
    public long Gold, Exp, Sp, Reputation;
    public List<(int Id, int Count)> Items = new();
    /// <summary>true = o jogador ganha só um dos itens, sorteado; false = ganha todos.</summary>
    public bool RandomItem;
    public string Description = "", OfferText = "", RewardText = "";
    /// <summary>Requisitos e prêmios a mais, escolhidos na lista (<see cref="QuestOptions"/>).</summary>
    public List<QuestChoice> Choices = new();
    /// <summary>
    /// Etapas (tipo 2 em diante). Vazio ou só um "falar com NPC" = recado (quest sem subquests, NPC que recebe = Receiver).
    /// Senão vira quest principal (NPC que dá, prêmio) com uma subquest por etapa, como o jogo grava (PW-KB, perfil --tasks-site).
    /// </summary>
    public List<QuestStep> Steps = new();
    public QuestStepOrder Order;
    /// <summary>ID da quest principal que este rascunho substitui (editar quest); 0 = quest nova.</summary>
    public int Editing;
}

/// <summary>Falar com NPC, ir até um lugar, matar monstros, obter itens (ficam com o jogador), entregar itens a um NPC (ele fica com eles), esperar um tempo.</summary>
public sealed record QuestDrop(int Monster, int Item, int Count, float Prob, bool Common);

public enum QuestStepKind { Talk, Reach, Kill, Collect, Deliver, Wait }

/// <summary>Em ordem (uma de cada vez); qualquer ordem (todas ativas, termina quando a última acabar); basta uma.</summary>
public enum QuestStepOrder { InOrder, AnyOrder, AnyOne }

/// <summary>Caixa no mapa em coordenadas do jogo (o servidor conta quando o jogador está dentro: mín. &lt; pos &lt; máx.).</summary>
public sealed class QuestArea
{
    public float X0, Y0, Z0, X1, Y1, Z1;
}

public sealed class QuestStep
{
    public QuestStepKind Kind;
    /// <summary>Nome da etapa no diário (subquest).</summary>
    public string Name = "", Description = "";
    /// <summary>Falar com NPC: o NPC e o que ele diz ao concluir a etapa (diálogo do prêmio da subquest).</summary>
    public int Npc;
    public string Text = "";
    /// <summary>Ir até um lugar: tag do mapa (gs.conf; mundo = 1) e as áreas — chegar em qualquer uma conclui a etapa.</summary>
    public int MapTag;
    public List<QuestArea> Areas = new();
    /// <summary>Matar monstros: ID do monstro e quantos (m_MonsterWanted; a etapa acaba quando todos estiverem contados).</summary>
    public List<(int Id, int Count)> Monsters = new();
    /// <summary>
    /// Matar monstros até juntar itens que só caem na quest (m_MonsterWanted com m_ulDropItemId; TaskProcess conta os itens, não
    /// as mortes). Common = item comum (inventário) ou item de quest (TASKMATTER_ESSENCE, inventário de quest).
    /// </summary>
    public List<QuestDrop> Drops = new();
    /// <summary>Obter / entregar itens: ID do item e quantos (m_ItemsWanted). Entregar usa também Npc e Text (fala ao receber).</summary>
    public List<(int Id, int Count)> Items = new();
    /// <summary>Esperar um tempo: segundos depois de a etapa começar (m_ulWaitTime).</summary>
    public int WaitSeconds;
}

/// <summary>
/// Monta quest nova a partir de uma quest real do mesmo arquivo (modelo): assim herda o que muda por versão (tipo no diário,
/// opções em bits, estrutura dos diálogos com as opções de aceitar). Do modelo se limpa tudo que é de outra quest
/// (requisitos, cadeia, prêmios, chaves, regiões) e se aplica o rascunho; o bloco fixo é regravado pelo TaskLayout,
/// então listas (itens do prêmio) podem mudar de tamanho.
/// </summary>
public static class QuestBuilder
{
    /// <summary>Objetivo "falar com NPC" e conclusão ao falar com o NPC que recebe (perfil de 98% dos recados nas v93/v129/v198).</summary>
    const int MethodTalkToNpc = 3, FinishByTalk = 1, MethodNone = 0, MethodReachSite = 4, FinishDirect = 0, MethodKillMonster = 1, MethodCollectItems = 2, MethodWaitTime = 5;

    /// <summary>O rascunho é um recado simples (sem subquests): sem etapas ou só "falar com NPC".</summary>
    public static bool IsRecado(QuestDraft d) => d.Steps.Count == 0 || d.Steps.Count == 1 && d.Steps[0].Kind == QuestStepKind.Talk;

    /// <summary>Recado com etapa única: o NPC e a fala da etapa são o NPC que recebe e a fala do prêmio.</summary>
    public static void Normalize(QuestDraft d)
    {
        if (d.Steps.Count == 1 && d.Steps[0].Kind == QuestStepKind.Talk) { d.Receiver = d.Steps[0].Npc; d.RewardText = d.Steps[0].Text; }
    }

    // campos de topo que o modelo traz de outra quest e a nova não deve herdar (requisitos, cadeia, regiões, chaves…)
    static readonly HashSet<string> ResetTop = new()
    {
        "m_ulTimeLimit", "m_bAbsFail", "m_bAbsTime", "m_ulTimetable", "m_tmType", "m_lAvailFrequency", "m_ulPremise_Task_Count", "m_ulPremise_Tasks", "m_ulPremise_Task_Least_Num",
        "m_ulPremise_Cotask", "m_ulCoTaskCond", "m_ulMutexTaskCount", "m_ulMutexTasks", "m_ulPremise_Faction", "m_iPremise_FactionRole", "m_ulGender",
        "m_ulOccupations", "m_Occupations", "m_bPremise_Spouse", "m_bGM", "m_bShieldUser", "m_ulPremise_Deposit", "m_lPremise_Reputation",
        "m_lPremise_RepuMax", "m_ulPremise_Period", "m_ulPremRMBMin", "m_ulPremRMBMax", "m_bCharTime", "m_lSkillLev", "m_iPremNeedRecordTasksNum",
        "m_iPremiseFactionContrib", "m_ulNeededTaskCount", "m_ulNeededTasks", "m_iNeededTasksLeastNum", "m_bPremCheckReincarnation",
        "m_bPremCheckRealmLevel", "m_bPremCheckForce", "m_iPremForce", "m_bPremIsKing", "m_bPremNotInTeam", "m_iPremTitleNumTotal",
        "m_iPremTitleNumRequired", "m_ulPremGeneralCardCount", "m_ulPremIconStateID", "m_iVIPLevelMin", "m_iVIPLevelMax", "m_bPremNoHome",
        "m_bPremNeedComp", "m_ulTeamMemsWanted", "m_bTeamwork", "m_ulMaxReceiver", "m_bAccountTaskLimit", "m_bRoleTaskLimit",
        "m_ulAccountTaskLimitCnt", "m_ulChangeKeyCnt", "m_ulChangePersonKeyCnt", "m_ulChangeHistoryKeyCnt", "m_bTransTo", "m_lMonsCtrl",
        "m_bTrigCtrl", "m_bDeliverySkill", "m_iDeliveredSkillID", "m_bAutoDeliver", "m_bDelvInZone", "m_ulDelvRegionCnt", "m_ulEnterRegionCnt",
        "m_ulLeaveRegionCnt", "m_bEnterRegionFail", "m_bLeaveRegionFail", "m_bPQTask", "m_ulPQExpCnt", "m_ulMonsterContribCnt", "m_ulExpCnt",
        "m_ulTaskCharCnt", "x_SpecialParametersCount", "m_bFinNeedComp", "m_ulGoldWanted", "m_iFactionContribWanted", "m_ulNPCToProtect",
        "m_ulNPCMoving", "m_ulWaitTime", "m_ulLeaveSiteCnt", "m_ulReachSiteCnt", "m_ulSpecialAward", "m_bIsLibraryTask", "m_DynTaskType",
        "m_ulPremItems", "m_ulGivenItems", "m_ulGivenCmnCount", "m_ulGivenTskCount", "m_ulGivenRelicCount", "m_ulPlayerWanted",
        "m_ulMonsterWanted", "m_ulItemsWanted", "m_ulMonsterUnwanted", "m_ulPersonExpCnt", "m_ulHistoryExpCnt", "m_ulPersonTaskCharCnt",
        "m_ulHistoryTaskCharCnt", "m_ulSubmitItemWantedNum", "m_uSpecialMethodType",
    };

    static string Leaf(string key)
    {
        int hash = key.IndexOf('#');
        if (hash >= 0) key = key[..hash];
        var last = key[(key.LastIndexOf('.') + 1)..];
        int b = last.IndexOf('[');
        return b < 0 ? last : last[..b];
    }

    static TaskRec ReadRec(QuestNode q, int version) => TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(q.Fixed, false)), version);

    /// <summary>Uma quest de recado comum do arquivo para servir de modelo (sem subquests, 1 janela ao dar e 1 ao premiar).</summary>
    public static QuestNode? FindRecadoTemplate(TasksFile t)
    {
        if (t.Roots is null) return null;
        foreach (var q in t.Roots)
        {
            if (q.Children.Count > 0 || q.Talks.Length < 5 || q.Talks[0].Windows.Count != 1 || q.Talks[4].Windows.Count != 1
                || !OwnOptions(q, 0, TasksFile.NpcGiveTask) || !OwnOptions(q, 4, TasksFile.NpcCompleteTask) || q.Talks.Skip(1).Take(3).Any(tp => tp.Windows.Count > 0)) continue;
            var rec = ReadRec(q, t.Version);
            if (rec.Int("m_enumMethod") == MethodTalkToNpc && rec.Int("m_enumFinishType") == FinishByTalk && rec.Int("m_ulAwardType_S") == 0
                && rec.Int("m_ulDelvNPC") > 0 && rec.Int("m_ulAwardNPC") > 0 && rec.Int("m_ulDelvNPC") != rec.Int("m_ulAwardNPC")
                && rec.Int("m_ulMonsterWanted") == 0 && rec.Int("m_ulItemsWanted") == 0 && rec.Int("m_ulPremItems") == 0)
                return q;
        }
        return null;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TasksFile, StepTemplates> _stepTpl = new();
    sealed record StepTemplates(QuestNode? Parent, QuestNode? Reach, QuestNode? Talk, QuestNode? Kill, QuestNode? Collect, QuestNode? Deliver, QuestNode? Wait);

    /// <summary>
    /// As opções de quest do diálogo (dar, concluir, entregar item) são exatamente as da própria quest: uma ou mais, todas com
    /// a função esperada e o ID dela (o cliente manda esse ID ao servidor). Modelo com opção de outra quest não serve.
    /// </summary>
    static bool OwnOptions(QuestNode q, int talk, int function)
    {
        var opts = q.Talks[talk].Windows.SelectMany(w => w.Options).Where(o => TasksFile.IsTaskOption(o.Param)).ToList();
        return opts.Count > 0 && opts.All(o => o.Param == function && o.Id == q.Id);
    }

    static bool NoTalks(QuestNode q, int from, int to) => q.Talks.Skip(from).Take(to - from + 1).All(tp => tp.Windows.Count == 0);

    /// <summary>
    /// Modelos reais para quest com etapas: principal (sem objetivo, conclui direto, NPC que dá, 1 janela ao dar com opção) e
    /// subquests "ir até um lugar" (método 4, conclui ao chegar, sem diálogos) e "falar com NPC" (método 3, conclui no NPC,
    /// 1 janela no prêmio). Guardados por arquivo; refeitos se o modelo sumir (quest excluída).
    /// </summary>
    static StepTemplates Templates(TasksFile t)
    {
        if (_stepTpl.TryGetValue(t, out var c) && new[] { c.Parent, c.Reach, c.Talk, c.Kill, c.Collect, c.Deliver, c.Wait }.All(q => q is null || t.Index.Find(q.Id) == q)) return c;
        QuestNode? parent = null, reach = null, talk = null, kill = null, collect = null, deliver = null, wait = null;
        foreach (var q in t.Roots ?? new())
        {
            if (parent is null && q.Children.Count > 0 && q.Talks.Length >= 5 && q.Talks[0].Windows.Count == 1 && OwnOptions(q, 0, TasksFile.NpcGiveTask) && NoTalks(q, 1, 4))
            {
                var r = ReadRec(q, t.Version);
                if (r.Int("m_enumMethod") == MethodNone && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulDelvNPC") > 0) parent = q;
            }
            foreach (var k in q.Descendants())
            {
                if (reach is not null && talk is not null && kill is not null && collect is not null && deliver is not null && wait is not null) break;
                if (k.Children.Count > 0 || k.Talks.Length < 5) continue;
                if (reach is null && NoTalks(k, 0, 4))
                {
                    var r = ReadRec(k, t.Version);
                    if (r.Int("m_enumMethod") == MethodReachSite && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulReachSiteCnt") >= 1) reach = k;
                }
                if (kill is null && NoTalks(k, 0, 4))
                {
                    var r = ReadRec(k, t.Version);
                    if (r.Int("m_enumMethod") == MethodKillMonster && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulMonsterWanted") >= 1) kill = k;
                }
                if ((talk is null || deliver is null) && NoTalks(k, 0, 3) && k.Talks[4].Windows.Count == 1 && OwnOptions(k, 4, TasksFile.NpcCompleteTask))
                {
                    var r = ReadRec(k, t.Version);
                    if (talk is null && r.Int("m_enumMethod") == MethodTalkToNpc && r.Int("m_enumFinishType") == FinishByTalk && r.Int("m_ulAwardNPC") > 0) talk = k;
                    if (deliver is null && r.Int("m_enumMethod") == MethodCollectItems && r.Int("m_enumFinishType") == FinishByTalk && r.Int("m_ulAwardNPC") > 0 && r.Int("m_ulItemsWanted") >= 1) deliver = k;
                }
                if ((collect is null || wait is null) && NoTalks(k, 0, 4))
                {
                    var r = ReadRec(k, t.Version);
                    if (collect is null && r.Int("m_enumMethod") == MethodCollectItems && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulItemsWanted") >= 1) collect = k;
                    if (wait is null && r.Int("m_enumMethod") == MethodWaitTime && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulWaitTime") > 0) wait = k;
                }
            }
            if (parent is not null && reach is not null && talk is not null && kill is not null && collect is not null && deliver is not null && wait is not null) break;
        }
        c = new StepTemplates(parent, reach, talk, kill, collect, deliver, wait);
        _stepTpl.AddOrUpdate(t, c);
        return c;
    }

    /// <summary>Bloco fixo do modelo (para saber quais opções e campos a versão tem).</summary>
    public static TaskRec? ModelRec(TasksFile t) => FindRecadoTemplate(t) is { } q ? ReadRec(q, t.Version) : null;

    /// <summary>Problemas que impedem criar (vazio = pode criar).</summary>
    public static List<string> Validate(TasksFile t, QuestDraft d, Func<int, bool> isNpc, Func<int, bool> itemExists)
    {
        string T(string k, string pt) => FieldKinds.Translate(k, pt);
        var e = new List<string>();
        if (!t.CanWrite) e.Add(T("qb.readOnly", "O tasks.data está só para leitura: ") + t.ReadOnlyReason);
        if (d.Steps.Count == 0) e.Add(T("qb.noSteps", "Ponha pelo menos uma etapa (o que o jogador faz depois de aceitar)."));
        if (d.Id <= 0) e.Add(T("qb.idPositive", "O ID precisa ser maior que zero."));
        else if (Taken(t, d, d.Id)) e.Add(string.Format(T("qb.idUsed", "O ID {0} já é de outra quest."), d.Id));
        if (d.Name.Trim().Length == 0) e.Add(T("qb.noName", "Falta o nome da quest."));
        else if (d.Name.Length > TasksFile.MaxNameLength) e.Add(string.Format(T("qb.nameLong", "O nome passa de {0} caracteres."), TasksFile.MaxNameLength));
        Normalize(d);
        bool recado = IsRecado(d);
        if (d.Giver <= 0 || !isNpc(d.Giver)) e.Add(T("qb.noGiver", "Falta o NPC que dá a quest."));
        if (recado && (d.Receiver <= 0 || !isNpc(d.Receiver))) e.Add(T("qb.noReceiver", "Falta o NPC que recebe a quest."));
        if (!recado) ValidateSteps(t, d, isNpc, itemExists, e);
        if (d.MinLevel < 0 || d.MaxLevel < 0 || d.MaxLevel > 0 && d.MaxLevel < d.MinLevel) e.Add(T("qb.levels", "O nível máximo é menor que o mínimo."));
        if (d.Gold < 0 || d.Exp < 0 || d.Sp < 0) e.Add(T("qb.negative", "Ouro, experiência e espírito não podem ser negativos."));
        foreach (var (id, n) in d.Items)
        {
            if (!itemExists(id)) e.Add(string.Format(T("qb.noItem", "O item {0} não existe no elements.data."), id));
            if (n <= 0) e.Add(string.Format(T("qb.itemCount", "A quantidade do item {0} precisa ser maior que zero."), id));
        }
        if (d.OfferText.Trim().Length == 0) e.Add(T("qb.noOffer", "Falta a fala do NPC ao dar a quest."));
        if (recado && d.RewardText.Trim().Length == 0) e.Add(T("qb.noReward", "Falta a fala do NPC ao entregar o prêmio."));
        if (FindRecadoTemplate(t) is null) e.Add(T("qb.noTemplate", "Não há uma quest de recado neste tasks.data para servir de modelo."));
        foreach (var c in d.Choices)
        {
            var name = c.Option.Text;
            int max = QuestOptions.MaxCount(c.Option, t.Version);
            switch (c.Value)
            {
                case List<int> ids:
                    if (ids.Count == 0) e.Add(string.Format(T("qb.optEmpty", "{0}: escolha pelo menos um."), name));
                    if (max > 0 && ids.Count > max) e.Add(string.Format(T("qb.optMax", "{0}: no máximo {1} nesta versão."), name, max));
                    if (c.Option.Type == QOType.Quests)
                        foreach (var id in ids.Where(id => t.Index.Find(id) is null)) e.Add(string.Format(T("qb.optNoQuest", "{0}: a quest {1} não existe."), name, id));
                    break;
                case List<(int Id, int Count)> items:
                    if (items.Count == 0) e.Add(string.Format(T("qb.optEmpty", "{0}: escolha pelo menos um."), name));
                    foreach (var (id, n) in items)
                    {
                        if (!itemExists(id)) e.Add(string.Format(T("qb.noItem", "O item {0} não existe no elements.data."), id));
                        if (n <= 0) e.Add(string.Format(T("qb.itemCount", "A quantidade do item {0} precisa ser maior que zero."), id));
                    }
                    break;
                case ValueTuple<long, long> r when c.Option.Id == "repu" && r.Item2 > 0 && r.Item2 < r.Item1:
                    e.Add(string.Format(T("qb.optRange", "{0}: o máximo é menor que o mínimo."), name)); break;
                case ValueTuple<string, string> f when f.Item1.Length == 0 || !double.TryParse(f.Item2.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _):
                    e.Add(string.Format(T("qb.optField", "{0}: escolha o campo e um valor numérico."), name)); break;
            }
        }
        if (d.RandomItem && d.Items.Count < 2) e.Add(T("qb.randNeedsItems", "Para sortear um item, coloque pelo menos 2 itens no prêmio."));
        return e;
    }

    /// <summary>O ID já é de outra quest (ao editar, os IDs da própria quest e das subquests dela ficam livres).</summary>
    static bool Taken(TasksFile t, QuestDraft d, int id) => t.Index.Find(id) is { } row
        && !(d.Editing > 0 && t.Index.Find(d.Editing) is QuestNode old && row is QuestNode n && (n == old || old.Descendants().Contains(n)));

    /// <summary>Etapas: IDs seguidos livres, cada etapa completa e coerente com a versão, modelos reais no arquivo.</summary>
    static void ValidateSteps(TasksFile t, QuestDraft d, Func<int, bool> isNpc, Func<int, bool> itemExists, List<string> e)
    {
        string T(string k, string pt) => FieldKinds.Translate(k, pt);
        var tpl = Templates(t);
        if (tpl.Parent is null) e.Add(T("qb.noParentTpl", "Não há neste tasks.data uma quest com etapas (subquests) para servir de modelo."));
        for (int i = 0; i < d.Steps.Count; i++)
        {
            var s = d.Steps[i];
            string at = string.Format(T("qb.stepN", "Etapa {0}"), i + 1) + ": ";
            int id = d.Id + 1 + i;
            if (d.Id > 0 && Taken(t, d, id)) e.Add(at + string.Format(T("qb.stepIdUsed", "o ID {0} (usado pela etapa) já é de outra quest; troque o ID da quest."), id));
            if (s.Name.Trim().Length == 0) e.Add(at + T("qb.stepNoName", "falta o nome (aparece no diário)."));
            else if (s.Name.Length > TasksFile.MaxNameLength) e.Add(at + string.Format(T("qb.nameLong", "O nome passa de {0} caracteres."), TasksFile.MaxNameLength));
            switch (s.Kind)
            {
                case QuestStepKind.Talk:
                    if (tpl.Talk is null) e.Add(at + T("qb.noTalkTpl", "não há neste tasks.data uma etapa \"falar com NPC\" para servir de modelo."));
                    if (s.Npc <= 0 || !isNpc(s.Npc)) e.Add(at + T("qb.stepNoNpc", "falta o NPC com quem falar."));
                    if (s.Text.Trim().Length == 0) e.Add(at + T("qb.stepNoText", "falta a fala do NPC ao concluir a etapa."));
                    break;
                case QuestStepKind.Kill:
                    if (tpl.Kill is null) e.Add(at + T("qb.noKillTpl", "não há neste tasks.data uma etapa \"matar monstros\" para servir de modelo."));
                    if (s.Monsters.Count + s.Drops.Count == 0) e.Add(at + T("qb.stepNoMonster", "escolha pelo menos um monstro."));
                    else if (s.Monsters.Count + s.Drops.Count > TaskVer.GetMaxMonsterWanted(t.Version)) e.Add(at + string.Format(T("qb.stepMonstersMax", "no máximo {0} monstros nesta versão."), TaskVer.GetMaxMonsterWanted(t.Version)));
                    foreach (var (mid, n) in s.Monsters)
                        if (mid <= 0 || n <= 0) e.Add(at + string.Format(T("qb.stepBadMonster", "monstro {0}: ID e quantidade precisam ser maiores que zero."), mid));
                    foreach (var dr in s.Drops)
                    {
                        if (dr.Monster <= 0 || dr.Item <= 0 || dr.Count <= 0) e.Add(at + string.Format(T("qb.stepBadDrop", "item que cai {0}: monstro, item e quantidade precisam ser maiores que zero."), dr.Item));
                        else if (!itemExists(dr.Item)) e.Add(at + string.Format(T("qb.noItem", "O item {0} não existe no elements.data."), dr.Item));
                        if (!(dr.Prob > 0 && dr.Prob <= 1)) e.Add(at + string.Format(T("qb.stepBadProb", "item que cai {0}: a chance precisa ser de 1% a 100%."), dr.Item));
                    }
                    break;
                case QuestStepKind.Collect or QuestStepKind.Deliver:
                {
                    bool give = s.Kind == QuestStepKind.Deliver;
                    if ((give ? tpl.Deliver : tpl.Collect) is null)
                        e.Add(at + (give ? T("qb.noDeliverTpl", "não há neste tasks.data uma etapa \"entregar itens a um NPC\" para servir de modelo.")
                                         : T("qb.noCollectTpl", "não há neste tasks.data uma etapa \"obter itens\" para servir de modelo.")));
                    if (s.Items.Count == 0) e.Add(at + T("qb.stepNoItems", "escolha pelo menos um item."));
                    else if (s.Items.Count > TaskVer.GetMaxItemsWanted(t.Version)) e.Add(at + string.Format(T("qb.stepItemsMax", "no máximo {0} itens nesta versão."), TaskVer.GetMaxItemsWanted(t.Version)));
                    foreach (var (iid, n) in s.Items)
                    {
                        if (iid <= 0 || n <= 0) e.Add(at + string.Format(T("qb.stepBadItem", "item {0}: ID e quantidade precisam ser maiores que zero."), iid));
                        else if (!itemExists(iid)) e.Add(at + string.Format(T("qb.noItem", "O item {0} não existe no elements.data."), iid));
                    }
                    if (give && (s.Npc <= 0 || !isNpc(s.Npc))) e.Add(at + T("qb.stepNoNpcDeliver", "falta o NPC que recebe os itens."));
                    if (give && s.Text.Trim().Length == 0) e.Add(at + T("qb.stepNoText", "falta a fala do NPC ao concluir a etapa."));
                    break;
                }
                case QuestStepKind.Wait:
                    if (tpl.Wait is null) e.Add(at + T("qb.noWaitTpl", "não há neste tasks.data uma etapa \"esperar um tempo\" para servir de modelo."));
                    if (s.WaitSeconds <= 0) e.Add(at + T("qb.stepNoWait", "o tempo de espera precisa ser maior que zero."));
                    break;
                case QuestStepKind.Reach:
                    if (tpl.Reach is null) e.Add(at + T("qb.noReachTpl", "não há neste tasks.data uma etapa \"ir até um lugar\" para servir de modelo."));
                    if (s.MapTag <= 0) e.Add(at + T("qb.stepNoMap", "não sei o número (tag) deste mapa no servidor: abra a pasta do servidor (gs.conf) ou do cliente com o configs.pck."));
                    int max = TaskVer.GetMaxTaskRegion(t.Version);
                    if (s.Areas.Count == 0) e.Add(at + T("qb.stepNoArea", "marque pelo menos uma área no mundo."));
                    else if (s.Areas.Count > max) e.Add(at + string.Format(T("qb.stepAreasMax", "no máximo {0} áreas nesta versão."), max));
                    for (int a = 0; a < s.Areas.Count; a++)
                    {
                        var r = s.Areas[a];
                        if (new[] { r.X0, r.Y0, r.Z0, r.X1, r.Y1, r.Z1 }.Any(v => !float.IsFinite(v)) || !(r.X1 - r.X0 >= 1 && r.Z1 - r.Z0 >= 1 && r.Y1 - r.Y0 >= 1))
                            e.Add(at + string.Format(T("qb.stepBadArea", "a área {0} precisa ter pelo menos 1 m em X, altura e Z (máximo maior que o mínimo)."), a + 1));
                    }
                    break;
            }
        }
        if (d.Order == QuestStepOrder.AnyOne && d.Steps.Count < 2) e.Add(T("qb.anyOneNeeds2", "\"Basta uma etapa\" precisa de pelo menos 2 etapas."));
    }

    /// <summary>Zera no bloco o que o modelo trouxe de outra quest (requisitos, cadeia, regiões, prêmios).</summary>
    static void Clean(TaskRec rec, int version)
    {
        foreach (var k in rec.V.Keys.ToList())
        {
            bool award = k.StartsWith("m_Award_") || k.StartsWith("m_AwBy") || k.StartsWith("AwBy");
            if (award || !k.Contains('.') && ResetTop.Contains(Leaf(k))) rec.V[k] = Zero(rec.V[k]);
        }
        // v135+: os requisitos liga/desliga do modelo ficam em bits de CombinedSwitches
        foreach (var k in ResetTop.Where(k => k.StartsWith("m_b") && QuestOptions.SwitchIndex(version, k) >= 0)) QuestOptions.SetFlag(rec, version, k, false);
    }

    static void WriteRec(QuestNode q, TaskRec rec, int version)
    {
        using var ms = new MemoryStream();
        TaskLayout.WriteFixed(new BinaryWriter(ms), version, rec);
        q.Fixed = ms.ToArray();
        q.ResetFields(version);
    }

    /// <summary>
    /// Quest com etapas, como o jogo grava: principal (sem objetivo, conclui quando as filhas acabam; NPC que dá, requisitos,
    /// prêmio e fala ao dar) + uma subquest por etapa, com os vínculos pai/irmãos/primeiro filho. Em ordem = m_bExeChildInOrder;
    /// basta uma = m_bParentAlsoSucc nas filhas (a primeira concluída conclui a principal).
    /// </summary>
    static QuestNode BuildSteps(TasksFile t, QuestDraft d, QuestNode? old = null)
    {
        var tpl = Templates(t);
        if ((old ?? tpl.Parent) is not { } parentProto) throw new InvalidOperationException("sem modelo de quest com etapas");
        var q = t.CloneRoot(parentProto, d.Id, append: true);
        q.Children.Clear();
        var kids = new List<QuestNode>();
        for (int i = 0; i < d.Steps.Count; i++)
        {
            var proto = old is not null && i < old.Children.Count && StepKind(t, old.Children[i]) == d.Steps[i].Kind ? old.Children[i]
                : d.Steps[i].Kind switch { QuestStepKind.Reach => tpl.Reach, QuestStepKind.Kill => tpl.Kill, QuestStepKind.Collect => tpl.Collect, QuestStepKind.Deliver => tpl.Deliver, QuestStepKind.Wait => tpl.Wait, _ => tpl.Talk };
            kids.Add(t.AddChild(q, proto ?? throw new InvalidOperationException("sem modelo de etapa"), d.Id + 1 + i));
        }
        var rec = ReadRec(q, t.Version);
        Clean(rec, t.Version);
        SetParent(rec, t.Version, d, kids[0].Id);
        ApplyCommon(rec, t.Version, d);
        WriteRec(q, rec, t.Version);
        q.Name = d.Name;
        q.Texts[0] = d.Description; q.Texts[1] = q.Texts[2] = q.Texts[3] = "";
        q.Talks[0].Windows[0].Text = d.OfferText;
        q.TextsChanged = true;
        for (int i = 0; i < kids.Count; i++)
        {
            var s = d.Steps[i]; var k = kids[i];
            var r = ReadRec(k, t.Version);
            Clean(r, t.Version);
            SetStep(r, t.Version, d, i, k.Id, q.Id, i > 0 ? kids[i - 1].Id : 0, i + 1 < kids.Count ? kids[i + 1].Id : 0);
            WriteRec(k, r, t.Version);
            k.Name = s.Name;
            k.Texts[0] = s.Description; k.Texts[1] = k.Texts[2] = k.Texts[3] = "";
            if (s.Kind is QuestStepKind.Talk or QuestStepKind.Deliver) k.Talks[4].Windows[0].Text = s.Text;
            k.TextsChanged = true;
        }
        t.InvalidateNpcIndex();
        return q;
    }

    /// <summary>Cria a quest (no fim da lista de quests principais). Chame <see cref="Validate"/> antes.</summary>
    public static QuestNode Build(TasksFile t, QuestDraft d) => Build(t, d, null);

    /// <summary>
    /// <paramref name="old"/> (editar quest): a quest antiga serve de modelo de si mesma quando o formato bate (recado continua
    /// recado, etapa do mesmo tipo na mesma posição), assim campos que o assistente não mostra ficam como estavam.
    /// </summary>
    static QuestNode Build(TasksFile t, QuestDraft d, QuestNode? old)
    {
        Normalize(d);
        if (!IsRecado(d)) return BuildSteps(t, d, old is { Children.Count: > 0 } ? old : null);
        var template = (old is { Children.Count: 0 } ? old : null) ?? FindRecadoTemplate(t) ?? throw new InvalidOperationException("sem modelo de recado");
        var q = t.CloneRoot(template, d.Id, append: true);
        var rec = ReadRec(q, t.Version);
        Clean(rec, t.Version);
        SetRecado(rec, d);
        ApplyCommon(rec, t.Version, d);
        WriteRec(q, rec, t.Version);
        q.Name = d.Name;
        q.Texts[0] = d.Description; q.Texts[1] = q.Texts[2] = q.Texts[3] = "";
        q.Talks[0].Windows[0].Text = d.OfferText;
        q.Talks[4].Windows[0].Text = d.RewardText;
        q.TextsChanged = true;
        t.InvalidateNpcIndex();
        return q;
    }

    /// <summary>Tipo da subquest (falar com NPC / ir até um lugar) como o assistente grava; null se não é nenhum dos dois.</summary>
    static QuestStepKind? StepKind(TasksFile t, QuestNode k)
    {
        if (k.Children.Count > 0 || k.Talks.Length < 5) return null;
        var r = ReadRec(k, t.Version);
        if (r.Int("m_enumMethod") == MethodReachSite && r.Int("m_enumFinishType") == FinishDirect && NoTalks(k, 0, 4)) return QuestStepKind.Reach;
        if (r.Int("m_enumMethod") == MethodKillMonster && r.Int("m_enumFinishType") == FinishDirect && NoTalks(k, 0, 4)) return QuestStepKind.Kill;
        if (r.Int("m_enumMethod") == MethodCollectItems && r.Int("m_enumFinishType") == FinishDirect && NoTalks(k, 0, 4)) return QuestStepKind.Collect;
        if (r.Int("m_enumMethod") == MethodWaitTime && r.Int("m_enumFinishType") == FinishDirect && NoTalks(k, 0, 4)) return QuestStepKind.Wait;
        if (r.Int("m_enumMethod") == MethodCollectItems && r.Int("m_enumFinishType") == FinishByTalk && NoTalks(k, 0, 3) && k.Talks[4].Windows.Count == 1
            && OwnOptions(k, 4, TasksFile.NpcCompleteTask)) return QuestStepKind.Deliver;
        if (r.Int("m_enumMethod") == MethodTalkToNpc && r.Int("m_enumFinishType") == FinishByTalk && NoTalks(k, 0, 3) && k.Talks[4].Windows.Count == 1
            && OwnOptions(k, 4, TasksFile.NpcCompleteTask)) return QuestStepKind.Talk;
        return null;
    }

    static bool SameValue(object a, object b) => a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y)
        : a is not byte[] && b is not byte[] && Convert.ToDouble(a) == Convert.ToDouble(b);

    /// <summary>
    /// Rascunho a partir de uma quest que já existe (editar pelo Mundo 3D). Só quests no formato que o assistente grava
    /// (recado, ou etapas "falar com NPC" / "ir até um lugar" com IDs seguidos) e sem nada que ele não mostre: a prova é
    /// regravar o bloco a partir do rascunho e comparar campo a campo com o original. Null + motivo se não dá.
    /// </summary>
    public static QuestDraft? FromQuest(TasksFile t, QuestNode q, out string? why)
    {
        string T(string k, string pt) => FieldKinds.Translate(k, pt);
        why = null;
        if (q.Parent is not null) { why = T("qb.editNotRoot", "Esta é uma subquest: edite a quest principal."); return null; }
        var rec = ReadRec(q, t.Version);
        var d = new QuestDraft
        {
            Id = q.Id, Editing = q.Id, Name = q.Name, Giver = (int)rec.Int("m_ulDelvNPC"), Type = (int)rec.Int("m_ulType"), Key = rec.Int("m_bKeyTask") != 0,
            MinLevel = (int)rec.Int("m_ulPremise_Lev_Min"), MaxLevel = (int)rec.Int("m_ulPremise_Lev_Max"),
            Gold = rec.Int("m_Award_S.m_ulGoldNum"), Exp = rec.Int("m_Award_S.m_ulExp"), Sp = rec.Int("m_Award_S.m_ulSP"), Reputation = rec.Int("m_Award_S.m_lReputation"),
            Description = q.Texts[0] ?? "", OfferText = q.Talks.Length > 0 && q.Talks[0].Windows.Count == 1 ? q.Talks[0].Windows[0].Text : "",
        };
        if (rec.Int("m_Award_S.m_ulCandItems") > 0)
        {
            const string g = "m_Award_S.m_CandItems[0].";
            d.RandomItem = rec.Int(g + "m_bRandChoose") != 0;
            for (int i = 0; i < rec.Int(g + "m_ulAwardItems"); i++)
                d.Items.Add(((int)rec.Int(g + $"m_AwardItems[{i}].m_ulItemTemplId"), (int)rec.Int(g + $"m_AwardItems[{i}].m_ulItemNum")));
        }
        d.Choices = QuestOptions.Read(rec, t.Version);
        bool shape = q.Talks.Length >= 5 && q.Talks[0].Windows.Count == 1 && OwnOptions(q, 0, TasksFile.NpcGiveTask) && q.Texts.Skip(1).All(string.IsNullOrEmpty);
        if (shape && q.Children.Count == 0)
        {
            shape = q.Talks[4].Windows.Count == 1 && OwnOptions(q, 4, TasksFile.NpcCompleteTask) && NoTalks(q, 1, 3)
                && rec.Int("m_enumMethod") == MethodTalkToNpc && rec.Int("m_enumFinishType") == FinishByTalk;
            d.Receiver = (int)rec.Int("m_ulAwardNPC");
            if (shape) d.RewardText = q.Talks[4].Windows[0].Text;
        }
        else if (shape)
        {
            shape = NoTalks(q, 1, 4) && rec.Int("m_enumMethod") == MethodNone && rec.Int("m_enumFinishType") == FinishDirect;
            d.Order = QuestOptions.GetFlag(rec, t.Version, "m_bExeChildInOrder") ? QuestStepOrder.InOrder
                : QuestOptions.GetFlag(ReadRec(q.Children[0], t.Version), t.Version, "m_bParentAlsoSucc") ? QuestStepOrder.AnyOne : QuestStepOrder.AnyOrder;
            for (int i = 0; shape && i < q.Children.Count; i++)
            {
                var k = q.Children[i];
                if (k.Id != q.Id + 1 + i || StepKind(t, k) is not { } kind || k.Texts.Skip(1).Any(x => !string.IsNullOrEmpty(x))) { shape = false; break; }
                var r = ReadRec(k, t.Version);
                var s = new QuestStep { Kind = kind, Name = k.Name, Description = k.Texts[0] ?? "" };
                if (kind is QuestStepKind.Talk or QuestStepKind.Deliver) { s.Npc = (int)r.Int("m_ulAwardNPC"); s.Text = k.Talks[4].Windows[0].Text; }
                if (kind is QuestStepKind.Collect or QuestStepKind.Deliver)
                    for (int m = 0; m < r.Int("m_ulItemsWanted"); m++)
                        s.Items.Add(((int)r.Int($"m_ItemsWanted[{m}].m_ulItemTemplId"), (int)r.Int($"m_ItemsWanted[{m}].m_ulItemNum")));
                else if (kind == QuestStepKind.Wait) s.WaitSeconds = (int)r.Int("m_ulWaitTime");
                else if (kind == QuestStepKind.Talk) { }
                else if (kind == QuestStepKind.Kill)
                    for (int m = 0; m < r.Int("m_ulMonsterWanted"); m++)
                    {
                        var b = $"m_MonsterWanted[{m}].";
                        if (r.Int(b + "m_ulDropItemId") > 0)
                            s.Drops.Add(new QuestDrop((int)r.Int(b + "m_ulMonsterTemplId"), (int)r.Int(b + "m_ulDropItemId"), (int)r.Int(b + "m_ulDropItemCount"),
                                r.V.TryGetValue(b + "m_fDropProb", out var pr) ? Convert.ToSingle(pr) : 0f, r.Int(b + "m_bDropCmnItem") != 0));
                        else s.Monsters.Add(((int)r.Int(b + "m_ulMonsterTemplId"), (int)r.Int(b + "m_ulMonsterNum")));
                    }
                else
                {
                    s.MapTag = (int)r.Int("m_ulReachSiteId");
                    for (int a = 0; a < r.Int("m_ulReachSiteCnt"); a++)
                    {
                        var b = $"m_pReachSite[{a}].";
                        float F(string key) => r.V.TryGetValue(b + key, out var o) ? Convert.ToSingle(o) : 0f;
                        s.Areas.Add(new QuestArea { X0 = F("zvMin.v[0]"), Y0 = F("zvMin.v[1]"), Z0 = F("zvMin.v[2]"), X1 = F("zvMax.v[0]"), Y1 = F("zvMax.v[1]"), Z1 = F("zvMax.v[2]") });
                    }
                }
                d.Steps.Add(s);
            }
        }
        if (!shape) { why = T("qb.editShape", "Esta quest tem partes que o assistente do Mundo 3D ainda não sabe mostrar (outro tipo de objetivo, mais diálogos ou textos). Edite na aba Quests."); return null; }
        // prova: regravar o bloco a partir do rascunho tem que dar o mesmo bloco (nada some ao gravar)
        bool Same(QuestNode node, Action<TaskRec> set)
        {
            var orig = ReadRec(node, t.Version); var redo = ReadRec(node, t.Version);
            Clean(redo, t.Version); set(redo);
            return orig.V.Count == redo.V.Count && orig.V.All(kv => redo.V.TryGetValue(kv.Key, out var v) && SameValue(kv.Value, v));
        }
        bool ok = q.Children.Count == 0
            ? Same(q, r => { SetRecado(r, d); ApplyCommon(r, t.Version, d); })
            : Same(q, r => { SetParent(r, t.Version, d, q.Children[0].Id); ApplyCommon(r, t.Version, d); })
              && q.Children.Select((k, i) => Same(k, r => SetStep(r, t.Version, d, i, k.Id, q.Id, i > 0 ? q.Children[i - 1].Id : 0, i + 1 < q.Children.Count ? q.Children[i + 1].Id : 0))).All(x => x);
        if (!ok) { why = T("qb.editExtra", "Esta quest usa campos que o assistente do Mundo 3D não mostra; gravar por aqui apagaria esses campos. Edite na aba Quests."); return null; }
        return d;
    }

    static readonly System.Text.RegularExpressions.Regex QuestRefKey = new(@"^(m_ulPremise_Tasks|m_ulMutexTasks|m_ulNeededTasks)\[\d+\]$|^m_ulPremise_Cotask$|^m_Award_S\.m_ulNewTask$");

    // Ligações de cada nó para outras quests (campo, ID), guardadas por nó e refeitas só quando os bytes dele mudam: ler os
    // ~21-36 mil registros leva segundos (excluir quest ficava lento). WarmLinks monta em segundo plano ao abrir.
    // Só campos inteiros: o registro tem floats enormes/NaN (Convert.ToInt64 estourava ao excluir, log 01/11 14:35).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<QuestNode, LinkCache> Links = new();
    sealed record LinkCache(int Hash, (string Key, int Id)[] Refs);

    static (string Key, int Id)[] LinksOf(QuestNode n, int version)
    {
        var h = new HashCode(); h.AddBytes(n.Fixed); int hash = h.ToHashCode();
        if (Links.TryGetValue(n, out var c) && c.Hash == hash) return c.Refs;
        var refs = new List<(string, int)>();
        foreach (var (k, v) in ReadRec(n, version).V)
            if (v is long or int or uint or ulong or short or ushort or byte or sbyte && QuestRefKey.IsMatch(k)
                && Convert.ToDecimal(v) is > 0 and <= int.MaxValue and var x)
                refs.Add((k, (int)x));
        var arr = refs.ToArray();
        lock (Links) Links.AddOrUpdate(n, new LinkCache(hash, arr));
        return arr;
    }

    /// <summary>Monta o cache de ligações entre quests (chamar em segundo plano ao abrir o tasks.data).</summary>
    public static void WarmLinks(TasksFile t)
    {
        if (!t.FullSchema) return;
        try { foreach (var n in t.AllNodes().ToList()) LinksOf(n, t.Version); }
        catch (InvalidOperationException) { }   // a lista mudou enquanto montava: o resto é feito quando pedir
    }

    /// <summary>Quests (fora de <paramref name="ids"/>) que apontam para algum desses IDs: anterior, exclusiva, necessária, conjunta ou nova ao concluir.</summary>
    public static List<int> ReferencedBy(TasksFile t, IReadOnlySet<int> ids)
    {
        var res = new List<int>();
        foreach (var n in t.AllNodes())
            if (!ids.Contains(n.Id) && LinksOf(n, t.Version).Any(r => ids.Contains(r.Id))) res.Add(n.Id);
        return res;
    }

    /// <summary>
    /// Quests seguintes da cadeia de <paramref name="rootId"/> (IDs das principais, em ordem): a "nova quest ao concluir" de
    /// qualquer nó da árvore e as quests que têm esta como quest anterior; em cascata (a seguinte da seguinte...).
    /// </summary>
    public static List<int> NextQuests(TasksFile t, int rootId)
    {
        int RootOf(int id) { var n = t.Index.Find(id) as QuestNode; while (n?.Parent is { } p) n = p; return n?.Id ?? 0; }
        var next = new Dictionary<int, List<int>>();   // principal -> principais que vêm depois dela
        void Link(int from, int to) { if (from > 0 && to > 0 && from != to) { if (!next.TryGetValue(from, out var l)) next[from] = l = new(); if (!l.Contains(to)) l.Add(to); } }
        foreach (var root in t.Roots!)
            foreach (var n in root.Descendants().Prepend(root))
                foreach (var (k, x) in LinksOf(n, t.Version))
                {
                    if (k.StartsWith("m_ulPremise_Tasks[")) Link(RootOf(x), root.Id);
                    else if (k == QuestOptions.AwardPrefix + "m_ulNewTask") Link(root.Id, RootOf(x));
                }
        var res = new List<int>(); var seen = new HashSet<int> { rootId }; var queue = new Queue<int>(); queue.Enqueue(rootId);
        while (queue.Count > 0)
            foreach (var id in next.GetValueOrDefault(queue.Dequeue()) ?? new())
                if (seen.Add(id)) { res.Add(id); queue.Enqueue(id); }
        return res;
    }

    /// <summary>
    /// Troca a quest <paramref name="old"/> pela do rascunho na mesma posição da lista (editar quest); a antiga sai do arquivo.
    /// Quem chama guarda a antiga e a posição para o desfazer. Chame <see cref="Validate"/> antes.
    /// </summary>
    public static QuestNode Replace(TasksFile t, QuestNode old, QuestDraft d)
    {
        int at = t.Roots!.IndexOf(old);
        t.DeleteRoot(old);
        var made = Build(t, d, old);
        t.MoveRoot(made, at);
        return made;
    }

    static void SetRecado(TaskRec rec, QuestDraft d)
    {
        void S(string k, long v) => rec.V[k] = v;
        S("m_ID", d.Id);
        S("m_enumMethod", MethodTalkToNpc); S("m_enumFinishType", FinishByTalk); S("m_ulAwardType_S", 0);
        S("m_ulDelvNPC", d.Giver); S("m_ulAwardNPC", d.Receiver);
        if (d.Type > 0) S("m_ulType", d.Type);
        S("m_bKeyTask", d.Key ? 1 : 0);
    }

    static void SetParent(TaskRec rec, int version, QuestDraft d, int firstChild)
    {
        void S(string k, long v) => rec.V[k] = v;
        S("m_ID", d.Id);
        S("m_enumMethod", MethodNone); S("m_enumFinishType", FinishDirect); S("m_ulAwardType_S", 0);
        S("m_ulDelvNPC", d.Giver); S("m_ulAwardNPC", 0); S("m_ulReachSiteId", 0);
        if (d.Type > 0) S("m_ulType", d.Type);
        S("m_bKeyTask", d.Key ? 1 : 0);
        S("m_ulParent", 0); S("m_ulPrevSibling", 0); S("m_ulNextSibling", 0); S("m_ulFirstChild", firstChild);
        foreach (var (k, on) in new[] { ("m_bExeChildInOrder", d.Order == QuestStepOrder.InOrder), ("m_bChooseOne", false), ("m_bRandOne", false), ("m_bParentAlsoFail", false), ("m_bParentAlsoSucc", false) })
            QuestOptions.SetFlag(rec, version, k, on);
    }

    static void SetStep(TaskRec r, int version, QuestDraft d, int i, int id, int parent, int prev, int next)
    {
        var s = d.Steps[i];
        void S(string k, long v) => r.V[k] = v;
        S("m_ID", id);
        S("m_ulParent", parent); S("m_ulPrevSibling", prev); S("m_ulNextSibling", next); S("m_ulFirstChild", 0);
        S("m_ulDelvNPC", 0); S("m_ulAwardType_S", 0); S("m_ulPremise_Lev_Min", 0); S("m_ulPremise_Lev_Max", 0);
        foreach (var (key, on) in new[] { ("m_bExeChildInOrder", false), ("m_bChooseOne", false), ("m_bRandOne", false), ("m_bParentAlsoFail", false), ("m_bParentAlsoSucc", d.Order == QuestStepOrder.AnyOne) })
            QuestOptions.SetFlag(r, version, key, on);
        if (s.Kind == QuestStepKind.Talk)
        {
            S("m_enumMethod", MethodTalkToNpc); S("m_enumFinishType", FinishByTalk); S("m_ulAwardNPC", s.Npc); S("m_ulReachSiteId", 0);
            return;
        }
        if (s.Kind is QuestStepKind.Collect or QuestStepKind.Deliver)
        {
            bool give = s.Kind == QuestStepKind.Deliver;
            S("m_enumMethod", MethodCollectItems); S("m_enumFinishType", give ? FinishByTalk : FinishDirect); S("m_ulAwardNPC", give ? s.Npc : 0); S("m_ulReachSiteId", 0);
            // a lista do modelo não é zerada pelo Clean (chave com ponto): zera e grava a nossa (item comum, chance 1)
            foreach (var k in r.V.Keys.Where(k => k.StartsWith("m_ItemsWanted[")).ToList()) r.V[k] = Zero(r.V[k]);
            S("m_ulItemsWanted", s.Items.Count);
            for (int m = 0; m < s.Items.Count; m++)
            {
                var b = $"m_ItemsWanted[{m}].";
                S(b + "m_ulItemTemplId", s.Items[m].Id); S(b + "m_bCommonItem", 1); S(b + "m_ulItemNum", s.Items[m].Count); r.V[b + "m_fProb"] = 1f;
            }
            // m_bClearAcquired (TaskProcess: tira os itens ao concluir): entregar = o NPC fica com eles; obter = ficam com o jogador
            // (uma etapa seguinte pode pedir os mesmos itens)
            if (r.V.ContainsKey("m_bClearAcquired") || QuestOptions.SwitchIndex(version, "m_bClearAcquired") >= 0) QuestOptions.SetFlag(r, version, "m_bClearAcquired", give);
            return;
        }
        if (s.Kind == QuestStepKind.Wait)
        {
            S("m_enumMethod", MethodWaitTime); S("m_enumFinishType", FinishDirect); S("m_ulAwardNPC", 0); S("m_ulReachSiteId", 0);
            S("m_ulWaitTime", s.WaitSeconds);
            return;
        }
        if (s.Kind == QuestStepKind.Kill)
        {
            S("m_enumMethod", MethodKillMonster); S("m_enumFinishType", FinishDirect); S("m_ulAwardNPC", 0); S("m_ulReachSiteId", 0);
            // a lista do modelo (drop, nível de quem mata...) não é zerada pelo Clean (chave com ponto): zera e grava a nossa
            foreach (var k in r.V.Keys.Where(k => k.StartsWith("m_MonsterWanted[")).ToList()) r.V[k] = Zero(r.V[k]);
            S("m_ulMonsterWanted", s.Monsters.Count + s.Drops.Count);
            for (int m = 0; m < s.Monsters.Count; m++)
            {
                var b = $"m_MonsterWanted[{m}].";
                S(b + "m_ulMonsterTemplId", s.Monsters[m].Id); S(b + "m_ulMonsterNum", s.Monsters[m].Count);
            }
            // itens que caem: depois das mortes contadas (a ordem não importa para o gs; o cliente separa pelo m_ulDropItemId)
            for (int j = 0; j < s.Drops.Count; j++)
            {
                var dr = s.Drops[j]; var b = $"m_MonsterWanted[{s.Monsters.Count + j}].";
                S(b + "m_ulMonsterTemplId", dr.Monster); S(b + "m_ulMonsterNum", 0);
                S(b + "m_ulDropItemId", dr.Item); S(b + "m_ulDropItemCount", dr.Count); S(b + "m_bDropCmnItem", dr.Common ? 1 : 0); r.V[b + "m_fDropProb"] = dr.Prob;
            }
            // itens que caem na quest são tirados ao concluir (m_bClearAcquired, TaskProcess RemoveAcquiredItem)
            if (r.V.ContainsKey("m_bClearAcquired") || QuestOptions.SwitchIndex(version, "m_bClearAcquired") >= 0) QuestOptions.SetFlag(r, version, "m_bClearAcquired", s.Drops.Count > 0);
            return;
        }
        S("m_enumMethod", MethodReachSite); S("m_enumFinishType", FinishDirect); S("m_ulAwardNPC", 0);
        S("m_ulReachSiteCnt", s.Areas.Count); S("m_ulReachSiteId", s.MapTag);
        for (int a = 0; a < s.Areas.Count; a++)
        {
            var z = s.Areas[a]; var b = $"m_pReachSite[{a}].";
            r.V[b + "zvMin.v[0]"] = z.X0; r.V[b + "zvMin.v[1]"] = z.Y0; r.V[b + "zvMin.v[2]"] = z.Z0;
            r.V[b + "zvMax.v[0]"] = z.X1; r.V[b + "zvMax.v[1]"] = z.Y1; r.V[b + "zvMax.v[2]"] = z.Z1;
            if (version >= 157) S(b + "MapID", s.MapTag);
            if (version >= 191) S(b + "RegionID", 0);
        }
    }

    /// <summary>Nível, prêmio e opções escolhidas (requisitos e prêmios a mais) — vão na quest principal.</summary>
    static void ApplyCommon(TaskRec rec, int version, QuestDraft d)
    {
        void S(string k, long v) => rec.V[k] = v;
        S("m_ulPremise_Lev_Min", d.MinLevel); S("m_ulPremise_Lev_Max", d.MaxLevel);
        S("m_Award_S.m_ulGoldNum", d.Gold); S("m_Award_S.m_ulExp", d.Exp); S("m_Award_S.m_ulSP", d.Sp); S("m_Award_S.m_lReputation", d.Reputation);
        S("m_Award_S.m_ulCandItems", d.Items.Count > 0 ? 1 : 0);
        if (d.Items.Count > 0)
        {
            const string g = "m_Award_S.m_CandItems[0].";
            S(g + "m_bRandChoose", d.RandomItem ? 1 : 0); S(g + "m_ulAwardItems", d.Items.Count);
            for (int i = 0; i < d.Items.Count; i++)
            {
                var it = g + $"m_AwardItems[{i}].";
                S(it + "m_ulItemTemplId", d.Items[i].Id); S(it + "m_bCommonItem", 1); S(it + "m_ulItemNum", d.Items[i].Count);
                rec.V[it + "m_fProb"] = 1f; S(it + "m_lPeriod", 0);
            }
        }
        // liga/desliga que o modelo trouxe ligado (ex.: "pode refazer" não é zerado no Clean): desligado, a não ser que escolhido
        foreach (var o in QuestOptions.All.Where(o => o.Type == QOType.Flag && (rec.V.ContainsKey(o.Keys[0]) || QuestOptions.SwitchIndex(version, o.Keys[0]) >= 0)))
            QuestOptions.SetFlag(rec, version, o.Keys[0], false);
        foreach (var c in d.Choices) QuestOptions.Apply(rec, version, c);
    }

    static object Zero(object v) => v switch
    {
        float => 0f, double => 0d, byte[] b => new byte[b.Length], _ => 0L,
    };
}
