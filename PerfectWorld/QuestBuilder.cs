namespace MyAceEditor.PerfectWorld;

/// <summary>O que o usuário escolhe ao criar uma quest no Mundo 3D (docs/QUESTS-3D.md). Tipo 1 = recado.</summary>
public sealed class QuestDraft
{
    public int Id;
    public string Name = "";
    public int Giver, Receiver;
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
}

public enum QuestStepKind { Talk, Reach }

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
    const int MethodTalkToNpc = 3, FinishByTalk = 1, MethodNone = 0, MethodReachSite = 4, FinishDirect = 0;

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
        "m_ulTimeLimit", "m_bAbsFail", "m_bAbsTime", "m_ulTimetable", "m_ulPremise_Task_Count", "m_ulPremise_Tasks", "m_ulPremise_Task_Least_Num",
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
    sealed record StepTemplates(QuestNode? Parent, QuestNode? Reach, QuestNode? Talk);

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
        if (_stepTpl.TryGetValue(t, out var c) && new[] { c.Parent, c.Reach, c.Talk }.All(q => q is null || t.Index.Find(q.Id) == q)) return c;
        QuestNode? parent = null, reach = null, talk = null;
        foreach (var q in t.Roots ?? new())
        {
            if (parent is null && q.Children.Count > 0 && q.Talks.Length >= 5 && q.Talks[0].Windows.Count == 1 && OwnOptions(q, 0, TasksFile.NpcGiveTask) && NoTalks(q, 1, 4))
            {
                var r = ReadRec(q, t.Version);
                if (r.Int("m_enumMethod") == MethodNone && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulDelvNPC") > 0) parent = q;
            }
            foreach (var k in q.Descendants())
            {
                if (reach is not null && talk is not null) break;
                if (k.Children.Count > 0 || k.Talks.Length < 5) continue;
                if (reach is null && NoTalks(k, 0, 4))
                {
                    var r = ReadRec(k, t.Version);
                    if (r.Int("m_enumMethod") == MethodReachSite && r.Int("m_enumFinishType") == FinishDirect && r.Int("m_ulReachSiteCnt") >= 1) reach = k;
                }
                if (talk is null && NoTalks(k, 0, 3) && k.Talks[4].Windows.Count == 1 && OwnOptions(k, 4, TasksFile.NpcCompleteTask))
                {
                    var r = ReadRec(k, t.Version);
                    if (r.Int("m_enumMethod") == MethodTalkToNpc && r.Int("m_enumFinishType") == FinishByTalk && r.Int("m_ulAwardNPC") > 0) talk = k;
                }
            }
            if (parent is not null && reach is not null && talk is not null) break;
        }
        c = new StepTemplates(parent, reach, talk);
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
        if (d.Id <= 0) e.Add(T("qb.idPositive", "O ID precisa ser maior que zero."));
        else if (t.Index.Find(d.Id) is not null) e.Add(string.Format(T("qb.idUsed", "O ID {0} já é de outra quest."), d.Id));
        if (d.Name.Trim().Length == 0) e.Add(T("qb.noName", "Falta o nome da quest."));
        else if (d.Name.Length > TasksFile.MaxNameLength) e.Add(string.Format(T("qb.nameLong", "O nome passa de {0} caracteres."), TasksFile.MaxNameLength));
        Normalize(d);
        bool recado = IsRecado(d);
        if (d.Giver <= 0 || !isNpc(d.Giver)) e.Add(T("qb.noGiver", "Falta o NPC que dá a quest."));
        if (recado && (d.Receiver <= 0 || !isNpc(d.Receiver))) e.Add(T("qb.noReceiver", "Falta o NPC que recebe a quest."));
        if (!recado) ValidateSteps(t, d, isNpc, e);
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

    /// <summary>Etapas: IDs seguidos livres, cada etapa completa e coerente com a versão, modelos reais no arquivo.</summary>
    static void ValidateSteps(TasksFile t, QuestDraft d, Func<int, bool> isNpc, List<string> e)
    {
        string T(string k, string pt) => FieldKinds.Translate(k, pt);
        var tpl = Templates(t);
        if (tpl.Parent is null) e.Add(T("qb.noParentTpl", "Não há neste tasks.data uma quest com etapas (subquests) para servir de modelo."));
        for (int i = 0; i < d.Steps.Count; i++)
        {
            var s = d.Steps[i];
            string at = string.Format(T("qb.stepN", "Etapa {0}"), i + 1) + ": ";
            int id = d.Id + 1 + i;
            if (d.Id > 0 && t.Index.Find(id) is not null) e.Add(at + string.Format(T("qb.stepIdUsed", "o ID {0} (usado pela etapa) já é de outra quest; troque o ID da quest."), id));
            if (s.Name.Trim().Length == 0) e.Add(at + T("qb.stepNoName", "falta o nome (aparece no diário)."));
            else if (s.Name.Length > TasksFile.MaxNameLength) e.Add(at + string.Format(T("qb.nameLong", "O nome passa de {0} caracteres."), TasksFile.MaxNameLength));
            switch (s.Kind)
            {
                case QuestStepKind.Talk:
                    if (tpl.Talk is null) e.Add(at + T("qb.noTalkTpl", "não há neste tasks.data uma etapa \"falar com NPC\" para servir de modelo."));
                    if (s.Npc <= 0 || !isNpc(s.Npc)) e.Add(at + T("qb.stepNoNpc", "falta o NPC com quem falar."));
                    if (s.Text.Trim().Length == 0) e.Add(at + T("qb.stepNoText", "falta a fala do NPC ao concluir a etapa."));
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
    static QuestNode BuildSteps(TasksFile t, QuestDraft d)
    {
        var tpl = Templates(t);
        if (tpl.Parent is null) throw new InvalidOperationException("sem modelo de quest com etapas");
        var q = t.CloneRoot(tpl.Parent, d.Id, append: true);
        q.Children.Clear();
        var kids = new List<QuestNode>();
        for (int i = 0; i < d.Steps.Count; i++)
        {
            var proto = d.Steps[i].Kind == QuestStepKind.Reach ? tpl.Reach : tpl.Talk;
            kids.Add(t.AddChild(q, proto ?? throw new InvalidOperationException("sem modelo de etapa"), d.Id + 1 + i));
        }
        void S(TaskRec r, string k, long v) => r.V[k] = v;
        var rec = ReadRec(q, t.Version);
        Clean(rec, t.Version);
        S(rec, "m_ID", d.Id);
        S(rec, "m_enumMethod", MethodNone); S(rec, "m_enumFinishType", FinishDirect); S(rec, "m_ulAwardType_S", 0);
        S(rec, "m_ulDelvNPC", d.Giver); S(rec, "m_ulAwardNPC", 0); S(rec, "m_ulReachSiteId", 0);
        S(rec, "m_ulParent", 0); S(rec, "m_ulPrevSibling", 0); S(rec, "m_ulNextSibling", 0); S(rec, "m_ulFirstChild", kids[0].Id);
        foreach (var (k, on) in new[] { ("m_bExeChildInOrder", d.Order == QuestStepOrder.InOrder), ("m_bChooseOne", false), ("m_bRandOne", false), ("m_bParentAlsoFail", false), ("m_bParentAlsoSucc", false) })
            QuestOptions.SetFlag(rec, t.Version, k, on);
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
            S(r, "m_ID", k.Id);
            S(r, "m_ulParent", q.Id); S(r, "m_ulPrevSibling", i > 0 ? kids[i - 1].Id : 0); S(r, "m_ulNextSibling", i + 1 < kids.Count ? kids[i + 1].Id : 0); S(r, "m_ulFirstChild", 0);
            S(r, "m_ulDelvNPC", 0); S(r, "m_ulAwardType_S", 0); S(r, "m_ulPremise_Lev_Min", 0); S(r, "m_ulPremise_Lev_Max", 0);
            foreach (var (key, on) in new[] { ("m_bExeChildInOrder", false), ("m_bChooseOne", false), ("m_bRandOne", false), ("m_bParentAlsoFail", false), ("m_bParentAlsoSucc", d.Order == QuestStepOrder.AnyOne) })
                QuestOptions.SetFlag(r, t.Version, key, on);
            if (s.Kind == QuestStepKind.Talk)
            {
                S(r, "m_enumMethod", MethodTalkToNpc); S(r, "m_enumFinishType", FinishByTalk); S(r, "m_ulAwardNPC", s.Npc); S(r, "m_ulReachSiteId", 0);
            }
            else
            {
                S(r, "m_enumMethod", MethodReachSite); S(r, "m_enumFinishType", FinishDirect); S(r, "m_ulAwardNPC", 0);
                S(r, "m_ulReachSiteCnt", s.Areas.Count); S(r, "m_ulReachSiteId", s.MapTag);
                for (int a = 0; a < s.Areas.Count; a++)
                {
                    var z = s.Areas[a]; var b = $"m_pReachSite[{a}].";
                    r.V[b + "zvMin.v[0]"] = z.X0; r.V[b + "zvMin.v[1]"] = z.Y0; r.V[b + "zvMin.v[2]"] = z.Z0;
                    r.V[b + "zvMax.v[0]"] = z.X1; r.V[b + "zvMax.v[1]"] = z.Y1; r.V[b + "zvMax.v[2]"] = z.Z1;
                    if (t.Version >= 157) S(r, b + "MapID", s.MapTag);
                    if (t.Version >= 191) S(r, b + "RegionID", 0);
                }
            }
            WriteRec(k, r, t.Version);
            k.Name = s.Name;
            k.Texts[0] = s.Description; k.Texts[1] = k.Texts[2] = k.Texts[3] = "";
            if (s.Kind == QuestStepKind.Talk) k.Talks[4].Windows[0].Text = s.Text;
            k.TextsChanged = true;
        }
        t.InvalidateNpcIndex();
        return q;
    }

    /// <summary>Cria a quest (no fim da lista de quests principais). Chame <see cref="Validate"/> antes.</summary>
    public static QuestNode Build(TasksFile t, QuestDraft d)
    {
        Normalize(d);
        if (!IsRecado(d)) return BuildSteps(t, d);
        var template = FindRecadoTemplate(t) ?? throw new InvalidOperationException("sem modelo de recado");
        var q = t.CloneRoot(template, d.Id, append: true);
        var rec = ReadRec(q, t.Version);
        Clean(rec, t.Version);
        void S(string k, long v) => rec.V[k] = v;
        S("m_ID", d.Id);
        S("m_enumMethod", MethodTalkToNpc); S("m_enumFinishType", FinishByTalk); S("m_ulAwardType_S", 0);
        S("m_ulDelvNPC", d.Giver); S("m_ulAwardNPC", d.Receiver);
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
        foreach (var c in d.Choices) QuestOptions.Apply(rec, version, c);
    }

    static object Zero(object v) => v switch
    {
        float => 0f, double => 0d, byte[] b => new byte[b.Length], _ => 0L,
    };
}
