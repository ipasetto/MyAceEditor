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
    public string Description = "", OfferText = "", RewardText = "";
    /// <summary>Requisitos e prêmios a mais, escolhidos na lista (<see cref="QuestOptions"/>).</summary>
    public List<QuestChoice> Choices = new();
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
    const int MethodTalkToNpc = 3, FinishByTalk = 1;

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
                || q.Talks[0].Windows[0].Options.Count == 0 || q.Talks.Skip(1).Take(3).Any(tp => tp.Windows.Count > 0)) continue;
            var rec = ReadRec(q, t.Version);
            if (rec.Int("m_enumMethod") == MethodTalkToNpc && rec.Int("m_enumFinishType") == FinishByTalk && rec.Int("m_ulAwardType_S") == 0
                && rec.Int("m_ulDelvNPC") > 0 && rec.Int("m_ulAwardNPC") > 0 && rec.Int("m_ulDelvNPC") != rec.Int("m_ulAwardNPC")
                && rec.Int("m_ulMonsterWanted") == 0 && rec.Int("m_ulItemsWanted") == 0 && rec.Int("m_ulPremItems") == 0)
                return q;
        }
        return null;
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
        if (d.Giver <= 0 || !isNpc(d.Giver)) e.Add(T("qb.noGiver", "Falta o NPC que dá a quest."));
        if (d.Receiver <= 0 || !isNpc(d.Receiver)) e.Add(T("qb.noReceiver", "Falta o NPC que recebe a quest."));
        if (d.MinLevel < 0 || d.MaxLevel < 0 || d.MaxLevel > 0 && d.MaxLevel < d.MinLevel) e.Add(T("qb.levels", "O nível máximo é menor que o mínimo."));
        if (d.Gold < 0 || d.Exp < 0 || d.Sp < 0) e.Add(T("qb.negative", "Ouro, experiência e espírito não podem ser negativos."));
        foreach (var (id, n) in d.Items)
        {
            if (!itemExists(id)) e.Add(string.Format(T("qb.noItem", "O item {0} não existe no elements.data."), id));
            if (n <= 0) e.Add(string.Format(T("qb.itemCount", "A quantidade do item {0} precisa ser maior que zero."), id));
        }
        if (d.OfferText.Trim().Length == 0) e.Add(T("qb.noOffer", "Falta a fala do NPC ao dar a quest."));
        if (d.RewardText.Trim().Length == 0) e.Add(T("qb.noReward", "Falta a fala do NPC ao entregar o prêmio."));
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
        if (d.Choices.Any(c => c.Option.Id == "randItem") && d.Items.Count < 2) e.Add(T("qb.randNeedsItems", "Para sortear um item, coloque pelo menos 2 itens no prêmio."));
        return e;
    }

    /// <summary>Cria a quest (no fim da lista de quests principais). Chame <see cref="Validate"/> antes.</summary>
    public static QuestNode Build(TasksFile t, QuestDraft d)
    {
        var template = FindRecadoTemplate(t) ?? throw new InvalidOperationException("sem modelo de recado");
        var q = t.CloneRoot(template, d.Id, append: true);
        var rec = ReadRec(q, t.Version);
        foreach (var k in rec.V.Keys.ToList())
        {
            bool award = k.StartsWith("m_Award_") || k.StartsWith("m_AwBy") || k.StartsWith("AwBy");
            if (award || !k.Contains('.') && ResetTop.Contains(Leaf(k))) rec.V[k] = Zero(rec.V[k]);
        }
        // v135+: os requisitos liga/desliga do modelo ficam em bits de CombinedSwitches
        foreach (var k in ResetTop.Where(k => k.StartsWith("m_b") && QuestOptions.SwitchIndex(t.Version, k) >= 0)) QuestOptions.SetFlag(rec, t.Version, k, false);
        void S(string k, long v) => rec.V[k] = v;
        S("m_ID", d.Id);
        S("m_enumMethod", MethodTalkToNpc); S("m_enumFinishType", FinishByTalk); S("m_ulAwardType_S", 0);
        S("m_ulDelvNPC", d.Giver); S("m_ulAwardNPC", d.Receiver);
        S("m_ulPremise_Lev_Min", d.MinLevel); S("m_ulPremise_Lev_Max", d.MaxLevel);
        S("m_Award_S.m_ulGoldNum", d.Gold); S("m_Award_S.m_ulExp", d.Exp); S("m_Award_S.m_ulSP", d.Sp); S("m_Award_S.m_lReputation", d.Reputation);
        S("m_Award_S.m_ulCandItems", d.Items.Count > 0 ? 1 : 0);
        if (d.Items.Count > 0)
        {
            const string g = "m_Award_S.m_CandItems[0].";
            S(g + "m_bRandChoose", 0); S(g + "m_ulAwardItems", d.Items.Count);
            for (int i = 0; i < d.Items.Count; i++)
            {
                var it = g + $"m_AwardItems[{i}].";
                S(it + "m_ulItemTemplId", d.Items[i].Id); S(it + "m_bCommonItem", 1); S(it + "m_ulItemNum", d.Items[i].Count);
                rec.V[it + "m_fProb"] = 1f; S(it + "m_lPeriod", 0);
            }
        }
        foreach (var c in d.Choices) QuestOptions.Apply(rec, t.Version, c);
        using (var ms = new MemoryStream())
        {
            TaskLayout.WriteFixed(new BinaryWriter(ms), t.Version, rec);
            q.Fixed = ms.ToArray();
        }
        q.ResetFields(t.Version);
        q.Name = d.Name;
        q.Texts[0] = d.Description; q.Texts[1] = q.Texts[2] = q.Texts[3] = "";
        q.Talks[0].Windows[0].Text = d.OfferText;
        q.Talks[4].Windows[0].Text = d.RewardText;
        q.TextsChanged = true;
        t.InvalidateNpcIndex();
        return q;
    }

    static object Zero(object v) => v switch
    {
        float => 0f, double => 0d, byte[] b => new byte[b.Length], _ => 0L,
    };
}
