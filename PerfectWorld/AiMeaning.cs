using MyAceEditor.UI;
using System.Globalization;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Tudo que o texto de "significado" precisa saber do mundo de fora do aipolicy.data. Todos opcionais:
/// sem elements.data carregado o nome do monstro vira "#id"; sem skillstr o nome da habilidade vira o número.
/// </summary>
public sealed class AiContext
{
    public Func<int, string?> MonsterName { get; set; } = _ => null;
    public Func<int, string?> SkillName { get; set; } = _ => null;
    /// <summary>Tradução do CORPO de um texto (sem prefixo de canal) no idioma atual, ou null se ainda não existe.</summary>
    public Func<string, string?> Translate { get; set; } = _ => null;
    /// <summary>Monstros (elements MONSTER_ESSENCE, campo id_strategy) que usam a política.</summary>
    public Func<int, IReadOnlyList<(int Id, string Name)>> UsedBy { get; set; } = _ => Array.Empty<(int, string)>();
}

/// <summary>
/// Textos legíveis para o aipolicy.data (PW-KB §S18). Idiomas: pt e en (zh/ru caem em en). Fontes dos significados:
/// nomes dos tipos e enums = §S18 [C]; semântica dos parâmetros conferida contra o arquivo real (hp_less/random = float em fração,
/// time_come = id de timer, summon/walk/play_action = campos do layout) [T]; o que não é certo está marcado "(?)" na tela.
/// </summary>
public static class AiMeaning
{
    const string NL = "\r\n";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- helpers de parâmetro ----------
    static int[] Ints(byte[] p) => p.Length % 4 == 0 ? Enumerable.Range(0, p.Length / 4).Select(i => BitConverter.ToInt32(p, i * 4)).ToArray() : Array.Empty<int>();
    static float F0(byte[] p) => p.Length >= 4 ? BitConverter.ToSingle(p, 0) : 0f;
    static string Pct(float f) => (f * 100f).ToString("0.##", Inv) + "%";
    static string ParamText(byte[] p) => p.Length % 4 == 0 ? string.Join(", ", Ints(p)) : AiPolicyFile.Hex(p, 0, p.Length);

    static Dictionary<string, object> Vals(AiOp o, int ver)
    {
        var d = new Dictionary<string, object>();
        var lay = AiSchema.Op(o.Type);
        if (lay is null) return d;
        try
        {
            var f = lay.Active(ver); var v = lay.Decode(ver, o.Param);
            for (int k = 0; k < f.Count && k < v.Count; k++) d[f[k].Name] = v[k];
        }
        catch { /* parâmetro fora do layout: devolve o que deu */ }
        return d;
    }
    static int Gi(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is int i ? i : 0;
    static byte Gb(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is byte b ? b : (byte)0;
    static string Gs(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is string s ? s : "";

    // ---------- condições ----------
    static readonly (string Key, string Pt)[] CondTitles =
    {
        ("aim.timerFired", "Timer disparou"), ("aim.hpBelow", "Vida abaixo de"), ("aim.startedAttackingEnteredCombat", "Começou a atacar (entrou em combate)"),
        ("aim.randomChance", "Chance aleatória"), ("aim.killedAPlayer2", "Matou um jogador"), ("aim.notInverts", "NÃO (inverte)"),
        ("aim.or2", "OU"), ("aim.and2", "E"), ("aim.died2", "Morreu"), ("aim.add", "Soma (+)"), ("aim.subtract", "Subtração (−)"),
        ("aim.multiply", "Multiplicação (×)"), ("aim.divide", "Divisão (÷)"), ("aim.greaterThan", "Maior que (>)"),
        ("aim.lessThan", "Menor que (<)"), ("aim.equalTo", "Igual a (==)"), ("aim.globalVariable", "Variável global"),
        ("aim.constantValue", "Valor constante"), ("aim.tookDamageInRange", "Recebeu dano na faixa"),
        ("aim.reachedEndOfPath", "Chegou ao fim do caminho"), ("aim.historyStage", "Estágio do histórico"),
        ("aim.historyValue", "Valor do histórico"), ("aim.stoppedFighting2", "Saiu do combate"), ("aim.localVariable", "Variável local"),
        ("aim.reachedEndOfPathV2", "Chegou ao fim do caminho (v2)"), ("aim.hasFilter", "Possui filtro"), ("aim.roomIndex", "Índice da sala"),
        ("aim.playersInRadius", "Jogadores no raio"), ("aim.playersInRegion", "Jogadores na região"),
        ("aim.dateYMDH", "Data (A/M/D h:m)"), ("aim.weekdayTime", "Dia da semana / hora"),
    };

    public static string CondTitle(int t) => t >= 0 && t < CondTitles.Length
        ? Loc.T(CondTitles[t].Key, CondTitles[t].Pt)
        : string.Format(Loc.T("aim.condition0NoDescription", "Condição {0} (sem descrição)"), t);

    static string? BinaryOp(int t) => t switch
    {
        6 => Loc.T("aim.or", " OU "), 7 => Loc.T("aim.and", " E "), 9 => " + ", 10 => " − ", 11 => " × ", 12 => " ÷ ",
        13 => " > ", 14 => " < ", 15 => " == ", _ => null,
    };

    /// <summary>Texto de uma condição folha (sem filhos), com o parâmetro já interpretado.</summary>
    public static string CondLeaf(AiCondNode n)
    {
        var i = Ints(n.Param);
        int a = i.Length > 0 ? i[0] : 0, b = i.Length > 1 ? i[1] : 0;
        return n.Type switch
        {
            0 => string.Format(Loc.T("aim.timer0Fired", "timer {0} disparou"), a),
            1 => string.Format(Loc.T("aim.hp0", "vida < {0}"), Pct(F0(n.Param))),
            2 => Loc.T("aim.startedAttacking", "começou a atacar"),
            3 => string.Format(Loc.T("aim.0Chance", "chance de {0}"), Pct(F0(n.Param))),
            4 => Loc.T("aim.killedAPlayer", "matou um jogador"),
            8 => Loc.T("aim.died", "morreu"),
            16 => $"global[{a}]",
            17 => a.ToString(Inv),
            18 => string.Format(Loc.T("aim.took01Damage", "recebeu dano entre {0} e {1}"), a, b),
            19 or 24 => string.Format(Loc.T("aim.reachedEndOfPath0", "chegou ao fim do caminho {0}"), a),
            22 => Loc.T("aim.stoppedFighting", "saiu do combate"),
            23 => $"local[{a}]",
            27 => string.Format(Loc.T("aim.playersWithinRadius0", "jogadores no raio {0}"), F0(n.Param).ToString("0.##", Inv)),
            _ => CondTitle(n.Type) + (n.Param.Length > 0 ? " (" + ParamText(n.Param) + ")" : ""),
        };
    }

    /// <summary>Expressão completa da árvore, ex.: (vida < 50% E chance de 25%).</summary>
    public static string CondExpr(AiCondNode? n)
    {
        if (n is null) return "?";
        if (n.Type == 5) return Loc.T("aim.not", "NÃO (") + CondExpr(n.Left ?? n.Right) + ")";
        var op = BinaryOp(n.Type);
        if (op != null) return "(" + CondExpr(n.Left) + op + CondExpr(n.Right) + ")";
        return CondLeaf(n);
    }

    /// <summary>Rótulo curto para a árvore: operadores mostram o título, folhas mostram o significado.</summary>
    public static string CondLabel(AiCondNode n) => BinaryOp(n.Type) != null || n.Type == 5 ? CondTitle(n.Type) : CondLeaf(n);

    // ---------- operações ----------
    static readonly (string Key, string Pt)[] OpTitles =
    {
        ("aim.attack", "Atacar"), ("aim.useSkill", "Usar habilidade"), ("aim.talk", "Falar"), ("aim.resetHateList", "Zerar lista de ódio"),
        ("aim.runTrigger", "Executar trigger"), ("aim.stopTrigger", "Parar trigger"), ("aim.activateTrigger", "Ativar trigger"),
        ("aim.createTimer", "Criar timer"), ("aim.killTimer", "Apagar timer"), ("aim.flee2", "Fugir"),
        ("aim.targetFirstInHateList", "Focar o 1º da lista de ódio"), ("aim.targetLastInHateList", "Focar o último da lista de ódio"),
        ("aim.targetThe50HateEntry", "Focar o do meio (50%) da lista de ódio"), ("aim.skip", "Pular"), ("aim.controller", "Controlador"),
        ("aim.setGlobalVariable", "Definir variável global"), ("aim.reviseGlobalVariable", "Ajustar variável global"),
        ("aim.summonMonster", "Invocar monstro"), ("aim.walkAlongPath", "Andar por um caminho"), ("aim.playAction", "Tocar animação"),
        ("aim.historyA", "Histórico (A)"), ("aim.historyB", "Histórico (B)"), ("aim.factionPvpPoints", "Pontos PvP da facção"),
        ("aim.calculateVariable", "Calcular variável"),
    };

    public static string OpTitle(int t) => t >= 0 && t < OpTitles.Length
        ? Loc.T(OpTitles[t].Key, OpTitles[t].Pt)
        : string.Format(Loc.T("aim.operation0NoDescription", "Operação {0} (sem descrição)"), t);

    static readonly (string Key, string Pt)[] Dispear =
    {
        ("aim.neverDespawns", "nunca some"), ("aim.despawnsWithTheSummoner", "some com quem invocou"),
        ("aim.despawnsWithTheTarget", "some com o alvo"), ("aim.despawnsWithEither", "some com um dos dois"),
    };
    static readonly (string Key, string Pt)[] Patrol = { ("aim.stopsAtTheEnd", "para no fim"), ("aim.returns", "volta pelo caminho"), ("aim.loops", "em loop") };
    static readonly (string Key, string Pt)[] Speed = { ("aim.begin", "início"), ("aim.slow", "lenta"), ("aim.fast", "rápida"), ("aim.end", "fim") };
    static readonly (string Key, string Pt)[] VarType = { ("aim.global", "global"), ("aim.local", "local"), ("aim.constant", "constante"), ("aim.random", "aleatório") };
    static readonly string[] CalcOp = { "+", "−", "×", "÷", "%" };

    static string Pick((string Key, string Pt)[] a, int i, string fallback) => i >= 0 && i < a.Length ? Loc.T(a[i].Key, a[i].Pt) : fallback;

    static string Operand(int type, int n) => type switch
    {
        0 => $"global[{n}]", 1 => $"local[{n}]", 2 => n.ToString(Inv), 3 => string.Format(Loc.T("aim.random0", "aleatório({0})"), n), _ => n.ToString(Inv),
    };

    /// <summary>Frase da operação (sem o alvo).</summary>
    public static string OpSentence(AiOp o, int ver, AiContext c)
    {
        var d = Vals(o, ver);
        switch (o.Type)
        {
            case 0: return string.Format(Loc.T("aim.attackMode0", "Atacar (modo {0})"), Gi(d, "uType"));
            case 1:
            {
                int s = Gi(d, "uSkill"); var nm = c.SkillName(s);
                string who = nm is null ? s.ToString(Inv) : $"{nm} ({s})";
                return string.Format(Loc.T("aim.useSkill0Level1", "Usar a habilidade {0}, nível {1}"), who, Gi(d, "uLevel"));
            }
            case 2:
            {
                var (pre, body) = AiChannel.Split(Gs(d, "text"));
                string shown = c.Translate(body) ?? body;
                return string.Format(Loc.T("aim.say01", "Dizer [{0}]: “{1}”"), AiChannel.Name(pre), shown);
            }
            case 3: return Loc.T("aim.resetTheHateList", "Zerar a lista de ódio");
            case 4: return string.Format(Loc.T("aim.runTrigger0", "Executar o trigger {0}"), Gi(d, "uID"));
            case 5: return string.Format(Loc.T("aim.stopTrigger0", "Parar o trigger {0}"), Gi(d, "uID"));
            case 6: return string.Format(Loc.T("aim.activateTrigger0", "Ativar o trigger {0}"), Gi(d, "uID"));
            case 7: return string.Format(Loc.T("aim.createTimer0Period1", "Criar o timer {0} (período {1}, repetições {2})"), Gi(d, "uID"), Gi(d, "uPeriod"), Gi(d, "uCounter"));
            case 8: return string.Format(Loc.T("aim.killTimer0", "Apagar o timer {0}"), Gi(d, "uID"));
            case 9: return Loc.T("aim.flee", "Fugir");
            case 10: case 11: case 12: case 13: return OpTitle(o.Type);
            case 14:
                return Gb(d, "bStop") != 0
                    ? string.Format(Loc.T("aim.stopController0", "Parar o controlador {0}"), Gi(d, "uID"))
                    : string.Format(Loc.T("aim.activateController0", "Ativar o controlador {0}"), Gi(d, "uID"));
            case 15:
            {
                bool literal = !d.ContainsKey("bIsValue") || Gb(d, "bIsValue") != 0;
                string rhs = literal ? Gi(d, "iValue").ToString(Inv) : $"global[{Gi(d, "iValue")}]";
                return string.Format(Loc.T("aim.setGlobal01", "Definir global[{0}] = {1}"), Gi(d, "iKey"), rhs);
            }
            case 16: return string.Format(Loc.T("aim.reviseGlobal0By1", "Ajustar global[{0}] em {1} (?)"), Gi(d, "iKey"), Gi(d, "iValue"));
            case 17:
            {
                int id = Gi(d, "uMonsterID"); var nm = c.MonsterName(id);
                string who = nm is null ? $"#{id}" : $"{c.Translate(nm) ?? nm} (#{id})";
                int t = Gi(d, "iDispearTime");
                string life = t < 0 ? Loc.T("aim.noTimeLimit", "sem tempo limite") : string.Format(Loc.T("aim.time0", "tempo {0}"), t);
                string cond = d.ContainsKey("iDispearCondition") ? ", " + Pick(Dispear, Gi(d, "iDispearCondition"), "?") : "";
                return string.Format(Loc.T("aim.summon0123", "Invocar {0}× {1} ({2}{3})"), Gi(d, "uMonsterNum"), who, life, cond);
            }
            case 18:
                return string.Format(Loc.T("aim.walkAlongPath0Of", "Andar pelo caminho {0} do grupo {1} ({2}, velocidade {3})"), Gi(d, "uPathID"), Gi(d, "uPathGroup"), Pick(Patrol, Gi(d, "iPatrolType"), "?"), Pick(Speed, Gi(d, "iSpeedType"), "?"));
            case 19:
            {
                var raw = Gs(d, "szActionName");
                string shown = c.Translate(raw) ?? raw;
                return string.Format(Loc.T("aim.playAction0Loops1", "Tocar a animação “{0}” (repetições {1}, intervalo {2}, duração {3})"), shown, Gi(d, "uLoop"), Gi(d, "uInterval"), Gi(d, "uPlayTime"));
            }
            case 20: case 21: return string.Format(Loc.T("aim.012", "{0}: [{1}] = {2}"), OpTitle(o.Type), Gi(d, "uID"), Gi(d, "iValue"));
            case 22: return string.Format(Loc.T("aim.factionPvpPoints0", "Pontos PvP da facção: {0}"), Gi(d, "uPoints"));
            case 23:
            {
                int op = Gi(d, "iOp");
                string sym = op >= 0 && op < CalcOp.Length ? CalcOp[op] : "?";
                return $"{Operand(Gi(d, "iDstType"), Gi(d, "iDst"))} = {Operand(Gi(d, "iSrc1Type"), Gi(d, "iSrc1"))} {sym} {Operand(Gi(d, "iSrc2Type"), Gi(d, "iSrc2"))}";
            }
            default: return OpTitle(o.Type);
        }
    }

    // ---------- alvos ----------
    static readonly (string Key, string Pt)[] TargetTitles =
    {
        ("aim.topOfTheHateList", "o que tem mais ódio"), ("aim.2ndInTheHateList", "o 2º da lista de ódio"),
        ("aim.theRestOfTheHate", "os demais da lista de ódio"), ("aim.theOneWithMostHp", "o com mais HP"),
        ("aim.theOneWithMostMp", "o com mais MP"), ("aim.theOneWithLeastHp", "o com menos HP"),
        ("aim.playersOfTheMaskedClasses", "jogadores das classes da máscara"), ("aim.itself", "ele mesmo"),
        ("aim.itsKiller", "quem o matou"), ("aim.theBirthplaceFaction", "a facção do local de nascimento"),
        ("aim.aRandomOneFromThe", "alguém aleatório da lista de ódio"), ("aim.theNearest", "o mais próximo"),
        ("aim.theFarthest", "o mais distante"), ("aim.topOfHateListRedirected", "o 1º do ódio (redirecionado)"),
    };

    public static string TargetText(AiTarget a)
    {
        string t = a.Type >= 0 && a.Type < TargetTitles.Length ? Loc.T(TargetTitles[a.Type].Key, TargetTitles[a.Type].Pt)
                                                               : string.Format(Loc.T("aim.target0NoDescription", "alvo {0} (sem descrição)"), a.Type);
        if (a.Type == 6 && a.Param.Length >= 4) t += $" 0x{BitConverter.ToInt32(a.Param, 0):X}";
        return t;
    }

    public static string OpWithTarget(AiOp o, int ver, AiContext c) => OpSentence(o, ver, c) + "  →  " + TargetText(o.Target);

    // ---------- trigger / política ----------
    static string Cut(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public static string TriggerName(AiTrigger t, AiContext c, bool original = false)
    {
        var nm = string.IsNullOrWhiteSpace(t.Name) ? Loc.T("aim.noName", "(sem nome)") : t.Name;
        if (original || string.IsNullOrWhiteSpace(t.Name)) return nm;
        var tr = c.Translate(nm);
        return tr is null ? nm : $"{tr}  ({nm})";
    }

    public static string TriggerSummary(AiTrigger t, AiContext c)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.T("aim.trigger", "Trigger ")).Append(t.Id).Append(": ").Append(TriggerName(t, c)).Append(NL).Append(NL);
        sb.Append(Loc.T("aim.if", "SE ")).Append(CondExpr(t.Root)).Append(NL);
        sb.Append(Loc.T("aim.then", "ENTÃO:")).Append(NL);
        if (t.Ops.Count == 0) sb.Append("  ").Append(Loc.T("aim.noOperations", "(nenhuma operação)")).Append(NL);
        for (int i = 0; i < t.Ops.Count; i++)
        {
            var o = t.Ops[i];
            sb.Append("  ").Append(i + 1).Append(". ").Append(OpWithTarget(o, t.Version, c)).Append(NL);
            foreach (var raw in OpTexts(o, t.Version))
            {
                var body = AiChannel.Split(raw).Body;
                if (c.Translate(body) != null) sb.Append("       ").Append(Loc.T("aim.original", "original")).Append(": “").Append(body).Append("”").Append(NL);
            }
        }
        sb.Append(NL).Append(Loc.T("aim.activeAtStart", "Ativo no início: ")).Append(t.Active != 0 ? Loc.T("aim.yes", "sim") : Loc.T("aim.no", "não"))
          .Append("  ·  bRun=").Append(t.Run).Append("  ·  bAttackValid=").Append(t.AttackValid).Append("  ·  dwVersion ").Append(t.Version).Append(NL);
        return sb.ToString();
    }

    public static string PolicySummary(AiPolicy p, AiContext c)
    {
        var sb = new StringBuilder();
        sb.Append(Loc.T("aim.policy", "Política ")).Append(p.Id).Append(" — ").Append(string.Format(Loc.T("aim.triggerCount", "{0} trigger(s)"), p.Triggers.Count)).Append(NL).Append(NL);
        var used = c.UsedBy(p.Id);
        if (used.Count == 0)
            sb.Append(Loc.T("aim.usedByNoKnownMonster", "Usada por: nenhum monstro conhecido (abra o elements.data para ver quem usa).")).Append(NL);
        else
        {
            sb.Append(Loc.T("aim.usedBy", "Usada por ")).Append(used.Count).Append(Loc.T("aim.monsterS", " monstro(s): "))
              .Append(string.Join(", ", used.Take(15).Select(m => $"{c.Translate(m.Name) ?? m.Name} (#{m.Id})")));
            if (used.Count > 15) sb.Append(string.Format(Loc.T("aim.and0More", " e mais {0}"), used.Count - 15));
            sb.Append(NL);
        }
        sb.Append(NL).Append(Loc.T("aim.triggers", "Triggers:")).Append(NL);
        foreach (var t in p.Triggers.Take(40))
            sb.Append("  [").Append(t.Id).Append("] ").Append(TriggerName(t, c)).Append("  —  ").Append(Loc.T("aim.if", "SE ")).Append(Cut(CondExpr(t.Root), 90))
              .Append("  (").Append(t.Ops.Count).Append(Loc.T("aim.ops", " op.)")).Append(NL);
        if (p.Triggers.Count > 40) sb.Append("  …").Append(string.Format(Loc.T("aim.and0More", " e mais {0}"), p.Triggers.Count - 40)).Append(NL);
        return sb.ToString();
    }

    /// <summary>Explicação do nó selecionado na árvore do trigger (o resumo do trigger vem junto, para dar contexto).</summary>
    public static string Explain(object obj, AiTrigger t, AiContext c) => obj switch
    {
        AiTrigger tr => TriggerSummary(tr, c),
        AiCondNode n => Loc.T("aim.condition", "Condição: ") + CondTitle(n.Type) + NL + CondExpr(n) + NL + NL + TriggerSummary(t, c),
        AiOp o => OpTitle(o.Type) + NL + OpWithTarget(o, t.Version, c) + NL + NL + TriggerSummary(t, c),
        AiTarget a => Loc.T("aim.target", "Alvo: ") + TargetText(a) + NL + NL + TriggerSummary(t, c),
        _ => TriggerSummary(t, c),
    };

    // ---------- rótulos de campo e dicas de vínculo ----------
    static readonly Dictionary<string, (string Key, string Pt)> FieldNames = new()
    {
        ["uType"] = ("aim.attackMode", "Modo de ataque"), ["uSkill"] = ("aim.skillId", "Habilidade (id)"), ["uLevel"] = ("aim.level", "Nível"),
        ["text"] = ("aim.spokenText", "Texto da fala"), ["uAppendDataMask"] = ("aim.appendedDataMask", "Dados anexados (máscara)"),
        ["uID"] = ("aim.id", "ID"), ["uPeriod"] = ("aim.period", "Período"), ["uCounter"] = ("aim.repeats", "Repetições"),
        ["bStop"] = ("aim.stop0Activate1Stop", "Parar (0=ativar, 1=parar)"), ["iKey"] = ("aim.globalVariableIndex", "Variável global (índice)"),
        ["iValue"] = ("aim.value", "Valor"), ["bIsValue"] = ("aim.literalValue0OtherVariable", "Valor literal (0=outra variável)"),
        ["uMonsterID"] = ("aim.monsterId", "Monstro (id)"), ["uMonsterNum"] = ("aim.count", "Quantidade"),
        ["iDispearTime"] = ("aim.despawnTime1None", "Tempo p/ sumir (-1=sem)"), ["iDispearCondition"] = ("aim.despawnCondition", "Condição de sumiço"),
        ["iUnk4"] = ("aim.unknown4", "Desconhecido 4"), ["iUnk5"] = ("aim.unknown5", "Desconhecido 5"),
        ["uPathGroup"] = ("aim.pathGroup", "Grupo do caminho"), ["uPathID"] = ("aim.pathId", "Caminho (id)"),
        ["iPatrolType"] = ("aim.patrolType", "Tipo de patrulha"), ["iSpeedType"] = ("aim.speed", "Velocidade"),
        ["szActionName"] = ("aim.actionName", "Nome da animação"), ["uLoop"] = ("aim.loopCount", "Repetições da animação"),
        ["uInterval"] = ("aim.interval", "Intervalo"), ["uPlayTime"] = ("aim.playTime", "Duração"), ["uPoints"] = ("aim.points", "Pontos"),
        ["iDst"] = ("aim.destination", "Destino"), ["iDstType"] = ("aim.destinationType", "Tipo do destino"),
        ["iSrc1"] = ("aim.source1", "Origem 1"), ["iSrc1Type"] = ("aim.source1Type", "Tipo da origem 1"), ["iOp"] = ("aim.operator", "Operador"),
        ["iSrc2"] = ("aim.source2", "Origem 2"), ["iSrc2Type"] = ("aim.source2Type", "Tipo da origem 2"),
        ["uOccupationMask"] = ("aim.classesMask", "Classes (máscara)"),
    };

    public static string FieldLabel(string name) => FieldNames.TryGetValue(name, out var v) ? $"{Loc.T(v.Key, v.Pt)}  [{name}]" : name;

    /// <summary>Significado do valor de um campo (nome do monstro/habilidade, enum), ou null.</summary>
    public static string? Hint(string field, int value, AiContext c) => field switch
    {
        "uMonsterID" => c.MonsterName(value) is { } m ? c.Translate(m) ?? m : null,
        "uSkill" => c.SkillName(value),
        "iDispearCondition" => value >= 0 && value < Dispear.Length ? Loc.T(Dispear[value].Key, Dispear[value].Pt) : null,
        "iPatrolType" => value >= 0 && value < Patrol.Length ? Loc.T(Patrol[value].Key, Patrol[value].Pt) : null,
        "iSpeedType" => value >= 0 && value < Speed.Length ? Loc.T(Speed[value].Key, Speed[value].Pt) : null,
        "iDstType" or "iSrc1Type" or "iSrc2Type" => value >= 0 && value < VarType.Length ? Loc.T(VarType[value].Key, VarType[value].Pt) : null,
        "iOp" => value >= 0 && value < CalcOp.Length ? CalcOp[value] : null,
        _ => null,
    };

    // ---------- textos traduzíveis ----------
    /// <summary>Falas (op 2) e nomes de animação (op 19) de uma operação.</summary>
    public static IEnumerable<string> OpTexts(AiOp o, int ver)
    {
        if (o.Type != 2 && o.Type != 19) yield break;
        var d = Vals(o, ver);
        var s = o.Type == 2 ? Gs(d, "text") : Gs(d, "szActionName");
        if (s.Length > 0) yield return s;
    }

    public static IEnumerable<string> TriggerTexts(AiTrigger t)
    {
        if (!string.IsNullOrWhiteSpace(t.Name)) yield return t.Name;
        foreach (var o in t.Ops) foreach (var s in OpTexts(o, t.Version)) yield return s;
    }

    public static IEnumerable<string> PolicyTexts(AiPolicy p) => p.Triggers.SelectMany(TriggerTexts);
    public static IEnumerable<string> FileTexts(AiPolicyFile f) => f.Policies.SelectMany(PolicyTexts);
}
