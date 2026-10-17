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
    static string L(string pt, string en) => AppLang.T(pt, en);

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
    static readonly (string Pt, string En)[] CondTitles =
    {
        ("Timer disparou", "Timer fired"), ("Vida abaixo de", "HP below"), ("Começou a atacar (entrou em combate)", "Started attacking (entered combat)"),
        ("Chance aleatória", "Random chance"), ("Matou um jogador", "Killed a player"), ("NÃO (inverte)", "NOT (inverts)"),
        ("OU", "OR"), ("E", "AND"), ("Morreu", "Died"), ("Soma (+)", "Add (+)"), ("Subtração (−)", "Subtract (−)"),
        ("Multiplicação (×)", "Multiply (×)"), ("Divisão (÷)", "Divide (÷)"), ("Maior que (>)", "Greater than (>)"),
        ("Menor que (<)", "Less than (<)"), ("Igual a (==)", "Equal to (==)"), ("Variável global", "Global variable"),
        ("Valor constante", "Constant value"), ("Recebeu dano na faixa", "Took damage in range"),
        ("Chegou ao fim do caminho", "Reached end of path"), ("Estágio do histórico", "History stage"),
        ("Valor do histórico", "History value"), ("Saiu do combate", "Stopped fighting"), ("Variável local", "Local variable"),
        ("Chegou ao fim do caminho (v2)", "Reached end of path (v2)"), ("Possui filtro", "Has filter"), ("Índice da sala", "Room index"),
        ("Jogadores no raio", "Players in radius"), ("Jogadores na região", "Players in region"),
        ("Data (A/M/D h:m)", "Date (Y/M/D h:m)"), ("Dia da semana / hora", "Weekday / time"),
    };

    public static string CondTitle(int t) => t >= 0 && t < CondTitles.Length
        ? L(CondTitles[t].Pt, CondTitles[t].En)
        : L($"Condição {t} (sem descrição)", $"Condition {t} (no description)");

    static string? BinaryOp(int t) => t switch
    {
        6 => L(" OU ", " OR "), 7 => L(" E ", " AND "), 9 => " + ", 10 => " − ", 11 => " × ", 12 => " ÷ ",
        13 => " > ", 14 => " < ", 15 => " == ", _ => null,
    };

    /// <summary>Texto de uma condição folha (sem filhos), com o parâmetro já interpretado.</summary>
    public static string CondLeaf(AiCondNode n)
    {
        var i = Ints(n.Param);
        int a = i.Length > 0 ? i[0] : 0, b = i.Length > 1 ? i[1] : 0;
        return n.Type switch
        {
            0 => L($"timer {a} disparou", $"timer {a} fired"),
            1 => L($"vida < {Pct(F0(n.Param))}", $"HP < {Pct(F0(n.Param))}"),
            2 => L("começou a atacar", "started attacking"),
            3 => L($"chance de {Pct(F0(n.Param))}", $"{Pct(F0(n.Param))} chance"),
            4 => L("matou um jogador", "killed a player"),
            8 => L("morreu", "died"),
            16 => $"global[{a}]",
            17 => a.ToString(Inv),
            18 => L($"recebeu dano entre {a} e {b}", $"took {a}–{b} damage"),
            19 or 24 => L($"chegou ao fim do caminho {a}", $"reached end of path {a}"),
            22 => L("saiu do combate", "stopped fighting"),
            23 => $"local[{a}]",
            27 => L($"jogadores no raio {F0(n.Param).ToString("0.##", Inv)}", $"players within radius {F0(n.Param).ToString("0.##", Inv)}"),
            _ => CondTitle(n.Type) + (n.Param.Length > 0 ? " (" + ParamText(n.Param) + ")" : ""),
        };
    }

    /// <summary>Expressão completa da árvore, ex.: (vida < 50% E chance de 25%).</summary>
    public static string CondExpr(AiCondNode? n)
    {
        if (n is null) return "?";
        if (n.Type == 5) return L("NÃO (", "NOT (") + CondExpr(n.Left ?? n.Right) + ")";
        var op = BinaryOp(n.Type);
        if (op != null) return "(" + CondExpr(n.Left) + op + CondExpr(n.Right) + ")";
        return CondLeaf(n);
    }

    /// <summary>Rótulo curto para a árvore: operadores mostram o título, folhas mostram o significado.</summary>
    public static string CondLabel(AiCondNode n) => BinaryOp(n.Type) != null || n.Type == 5 ? CondTitle(n.Type) : CondLeaf(n);

    // ---------- operações ----------
    static readonly (string Pt, string En)[] OpTitles =
    {
        ("Atacar", "Attack"), ("Usar habilidade", "Use skill"), ("Falar", "Talk"), ("Zerar lista de ódio", "Reset hate list"),
        ("Executar trigger", "Run trigger"), ("Parar trigger", "Stop trigger"), ("Ativar trigger", "Activate trigger"),
        ("Criar timer", "Create timer"), ("Apagar timer", "Kill timer"), ("Fugir", "Flee"),
        ("Focar o 1º da lista de ódio", "Target first in hate list"), ("Focar o último da lista de ódio", "Target last in hate list"),
        ("Focar o do meio (50%) da lista de ódio", "Target the 50% hate entry"), ("Pular", "Skip"), ("Controlador", "Controller"),
        ("Definir variável global", "Set global variable"), ("Ajustar variável global", "Revise global variable"),
        ("Invocar monstro", "Summon monster"), ("Andar por um caminho", "Walk along path"), ("Tocar animação", "Play action"),
        ("Histórico (A)", "History (A)"), ("Histórico (B)", "History (B)"), ("Pontos PvP da facção", "Faction PvP points"),
        ("Calcular variável", "Calculate variable"),
    };

    public static string OpTitle(int t) => t >= 0 && t < OpTitles.Length
        ? L(OpTitles[t].Pt, OpTitles[t].En)
        : L($"Operação {t} (sem descrição)", $"Operation {t} (no description)");

    static readonly (string Pt, string En)[] Dispear =
    {
        ("nunca some", "never despawns"), ("some com quem invocou", "despawns with the summoner"),
        ("some com o alvo", "despawns with the target"), ("some com um dos dois", "despawns with either"),
    };
    static readonly (string Pt, string En)[] Patrol = { ("para no fim", "stops at the end"), ("volta pelo caminho", "returns"), ("em loop", "loops") };
    static readonly (string Pt, string En)[] Speed = { ("início", "begin"), ("lenta", "slow"), ("rápida", "fast"), ("fim", "end") };
    static readonly (string Pt, string En)[] VarType = { ("global", "global"), ("local", "local"), ("constante", "constant"), ("aleatório", "random") };
    static readonly string[] CalcOp = { "+", "−", "×", "÷", "%" };

    static string Pick((string Pt, string En)[] a, int i, string fallback) => i >= 0 && i < a.Length ? L(a[i].Pt, a[i].En) : fallback;

    static string Operand(int type, int n) => type switch
    {
        0 => $"global[{n}]", 1 => $"local[{n}]", 2 => n.ToString(Inv), 3 => L($"aleatório({n})", $"random({n})"), _ => n.ToString(Inv),
    };

    /// <summary>Frase da operação (sem o alvo).</summary>
    public static string OpSentence(AiOp o, int ver, AiContext c)
    {
        var d = Vals(o, ver);
        switch (o.Type)
        {
            case 0: return L($"Atacar (modo {Gi(d, "uType")})", $"Attack (mode {Gi(d, "uType")})");
            case 1:
            {
                int s = Gi(d, "uSkill"); var nm = c.SkillName(s);
                string who = nm is null ? s.ToString(Inv) : $"{nm} ({s})";
                return L($"Usar a habilidade {who}, nível {Gi(d, "uLevel")}", $"Use skill {who}, level {Gi(d, "uLevel")}");
            }
            case 2:
            {
                var (pre, body) = AiChannel.Split(Gs(d, "text"));
                string shown = c.Translate(body) ?? body;
                return L($"Dizer [{AiChannel.Name(pre)}]: “{shown}”", $"Say [{AiChannel.Name(pre)}]: “{shown}”");
            }
            case 3: return L("Zerar a lista de ódio", "Reset the hate list");
            case 4: return L($"Executar o trigger {Gi(d, "uID")}", $"Run trigger {Gi(d, "uID")}");
            case 5: return L($"Parar o trigger {Gi(d, "uID")}", $"Stop trigger {Gi(d, "uID")}");
            case 6: return L($"Ativar o trigger {Gi(d, "uID")}", $"Activate trigger {Gi(d, "uID")}");
            case 7: return L($"Criar o timer {Gi(d, "uID")} (período {Gi(d, "uPeriod")}, repetições {Gi(d, "uCounter")})",
                             $"Create timer {Gi(d, "uID")} (period {Gi(d, "uPeriod")}, repeats {Gi(d, "uCounter")})");
            case 8: return L($"Apagar o timer {Gi(d, "uID")}", $"Kill timer {Gi(d, "uID")}");
            case 9: return L("Fugir", "Flee");
            case 10: case 11: case 12: case 13: return OpTitle(o.Type);
            case 14:
                return Gb(d, "bStop") != 0
                    ? L($"Parar o controlador {Gi(d, "uID")}", $"Stop controller {Gi(d, "uID")}")
                    : L($"Ativar o controlador {Gi(d, "uID")}", $"Activate controller {Gi(d, "uID")}");
            case 15:
            {
                bool literal = !d.ContainsKey("bIsValue") || Gb(d, "bIsValue") != 0;
                string rhs = literal ? Gi(d, "iValue").ToString(Inv) : $"global[{Gi(d, "iValue")}]";
                return L($"Definir global[{Gi(d, "iKey")}] = {rhs}", $"Set global[{Gi(d, "iKey")}] = {rhs}");
            }
            case 16: return L($"Ajustar global[{Gi(d, "iKey")}] em {Gi(d, "iValue")} (?)", $"Revise global[{Gi(d, "iKey")}] by {Gi(d, "iValue")} (?)");
            case 17:
            {
                int id = Gi(d, "uMonsterID"); var nm = c.MonsterName(id);
                string who = nm is null ? $"#{id}" : $"{c.Translate(nm) ?? nm} (#{id})";
                int t = Gi(d, "iDispearTime");
                string life = t < 0 ? L("sem tempo limite", "no time limit") : L($"tempo {t}", $"time {t}");
                string cond = d.ContainsKey("iDispearCondition") ? ", " + Pick(Dispear, Gi(d, "iDispearCondition"), "?") : "";
                return L($"Invocar {Gi(d, "uMonsterNum")}× {who} ({life}{cond})", $"Summon {Gi(d, "uMonsterNum")}× {who} ({life}{cond})");
            }
            case 18:
                return L($"Andar pelo caminho {Gi(d, "uPathID")} do grupo {Gi(d, "uPathGroup")} ({Pick(Patrol, Gi(d, "iPatrolType"), "?")}, velocidade {Pick(Speed, Gi(d, "iSpeedType"), "?")})",
                         $"Walk along path {Gi(d, "uPathID")} of group {Gi(d, "uPathGroup")} ({Pick(Patrol, Gi(d, "iPatrolType"), "?")}, speed {Pick(Speed, Gi(d, "iSpeedType"), "?")})");
            case 19:
            {
                var raw = Gs(d, "szActionName");
                string shown = c.Translate(raw) ?? raw;
                return L($"Tocar a animação “{shown}” (repetições {Gi(d, "uLoop")}, intervalo {Gi(d, "uInterval")}, duração {Gi(d, "uPlayTime")})",
                         $"Play action “{shown}” (loops {Gi(d, "uLoop")}, interval {Gi(d, "uInterval")}, duration {Gi(d, "uPlayTime")})");
            }
            case 20: case 21: return L($"{OpTitle(o.Type)}: [{Gi(d, "uID")}] = {Gi(d, "iValue")}", $"{OpTitle(o.Type)}: [{Gi(d, "uID")}] = {Gi(d, "iValue")}");
            case 22: return L($"Pontos PvP da facção: {Gi(d, "uPoints")}", $"Faction PvP points: {Gi(d, "uPoints")}");
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
    static readonly (string Pt, string En)[] TargetTitles =
    {
        ("o que tem mais ódio", "top of the hate list"), ("o 2º da lista de ódio", "2nd in the hate list"),
        ("os demais da lista de ódio", "the rest of the hate list"), ("o com mais HP", "the one with most HP"),
        ("o com mais MP", "the one with most MP"), ("o com menos HP", "the one with least HP"),
        ("jogadores das classes da máscara", "players of the masked classes"), ("ele mesmo", "itself"),
        ("quem o matou", "its killer"), ("a facção do local de nascimento", "the birthplace faction"),
        ("alguém aleatório da lista de ódio", "a random one from the hate list"), ("o mais próximo", "the nearest"),
        ("o mais distante", "the farthest"), ("o 1º do ódio (redirecionado)", "top of hate list (redirected)"),
    };

    public static string TargetText(AiTarget a)
    {
        string t = a.Type >= 0 && a.Type < TargetTitles.Length ? L(TargetTitles[a.Type].Pt, TargetTitles[a.Type].En)
                                                               : L($"alvo {a.Type} (sem descrição)", $"target {a.Type} (no description)");
        if (a.Type == 6 && a.Param.Length >= 4) t += $" 0x{BitConverter.ToInt32(a.Param, 0):X}";
        return t;
    }

    public static string OpWithTarget(AiOp o, int ver, AiContext c) => OpSentence(o, ver, c) + "  →  " + TargetText(o.Target);

    // ---------- trigger / política ----------
    static string Cut(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public static string TriggerName(AiTrigger t, AiContext c, bool original = false)
    {
        var nm = string.IsNullOrWhiteSpace(t.Name) ? L("(sem nome)", "(no name)") : t.Name;
        if (original || string.IsNullOrWhiteSpace(t.Name)) return nm;
        var tr = c.Translate(nm);
        return tr is null ? nm : $"{tr}  ({nm})";
    }

    public static string TriggerSummary(AiTrigger t, AiContext c)
    {
        var sb = new StringBuilder();
        sb.Append(L("Trigger ", "Trigger ")).Append(t.Id).Append(": ").Append(TriggerName(t, c)).Append(NL).Append(NL);
        sb.Append(L("SE ", "IF ")).Append(CondExpr(t.Root)).Append(NL);
        sb.Append(L("ENTÃO:", "THEN:")).Append(NL);
        if (t.Ops.Count == 0) sb.Append("  ").Append(L("(nenhuma operação)", "(no operations)")).Append(NL);
        for (int i = 0; i < t.Ops.Count; i++)
        {
            var o = t.Ops[i];
            sb.Append("  ").Append(i + 1).Append(". ").Append(OpWithTarget(o, t.Version, c)).Append(NL);
            foreach (var raw in OpTexts(o, t.Version))
            {
                var body = AiChannel.Split(raw).Body;
                if (c.Translate(body) != null) sb.Append("       ").Append(L("original", "original")).Append(": “").Append(body).Append("”").Append(NL);
            }
        }
        sb.Append(NL).Append(L("Ativo no início: ", "Active at start: ")).Append(t.Active != 0 ? L("sim", "yes") : L("não", "no"))
          .Append("  ·  bRun=").Append(t.Run).Append("  ·  bAttackValid=").Append(t.AttackValid).Append("  ·  dwVersion ").Append(t.Version).Append(NL);
        return sb.ToString();
    }

    public static string PolicySummary(AiPolicy p, AiContext c)
    {
        var sb = new StringBuilder();
        sb.Append(L("Política ", "Policy ")).Append(p.Id).Append(" — ").Append(p.Triggers.Count).Append(" trigger(s)").Append(NL).Append(NL);
        var used = c.UsedBy(p.Id);
        if (used.Count == 0)
            sb.Append(L("Usada por: nenhum monstro conhecido (abra o elements.data para ver quem usa).", "Used by: no known monster (open elements.data to see who uses it).")).Append(NL);
        else
        {
            sb.Append(L("Usada por ", "Used by ")).Append(used.Count).Append(L(" monstro(s): ", " monster(s): "))
              .Append(string.Join(", ", used.Take(15).Select(m => $"{c.Translate(m.Name) ?? m.Name} (#{m.Id})")));
            if (used.Count > 15) sb.Append(L($" e mais {used.Count - 15}", $" and {used.Count - 15} more"));
            sb.Append(NL);
        }
        sb.Append(NL).Append(L("Triggers:", "Triggers:")).Append(NL);
        foreach (var t in p.Triggers.Take(40))
            sb.Append("  [").Append(t.Id).Append("] ").Append(TriggerName(t, c)).Append("  —  ").Append(L("SE ", "IF ")).Append(Cut(CondExpr(t.Root), 90))
              .Append("  (").Append(t.Ops.Count).Append(L(" op.)", " ops)")).Append(NL);
        if (p.Triggers.Count > 40) sb.Append("  …").Append(L($" e mais {p.Triggers.Count - 40}", $" and {p.Triggers.Count - 40} more")).Append(NL);
        return sb.ToString();
    }

    /// <summary>Explicação do nó selecionado na árvore do trigger (o resumo do trigger vem junto, para dar contexto).</summary>
    public static string Explain(object obj, AiTrigger t, AiContext c) => obj switch
    {
        AiTrigger tr => TriggerSummary(tr, c),
        AiCondNode n => L("Condição: ", "Condition: ") + CondTitle(n.Type) + NL + CondExpr(n) + NL + NL + TriggerSummary(t, c),
        AiOp o => OpTitle(o.Type) + NL + OpWithTarget(o, t.Version, c) + NL + NL + TriggerSummary(t, c),
        AiTarget a => L("Alvo: ", "Target: ") + TargetText(a) + NL + NL + TriggerSummary(t, c),
        _ => TriggerSummary(t, c),
    };

    // ---------- rótulos de campo e dicas de vínculo ----------
    static readonly Dictionary<string, (string Pt, string En)> FieldNames = new()
    {
        ["uType"] = ("Modo de ataque", "Attack mode"), ["uSkill"] = ("Habilidade (id)", "Skill (id)"), ["uLevel"] = ("Nível", "Level"),
        ["text"] = ("Texto da fala", "Spoken text"), ["uAppendDataMask"] = ("Dados anexados (máscara)", "Appended data (mask)"),
        ["uID"] = ("ID", "ID"), ["uPeriod"] = ("Período", "Period"), ["uCounter"] = ("Repetições", "Repeats"),
        ["bStop"] = ("Parar (0=ativar, 1=parar)", "Stop (0=activate, 1=stop)"), ["iKey"] = ("Variável global (índice)", "Global variable (index)"),
        ["iValue"] = ("Valor", "Value"), ["bIsValue"] = ("Valor literal (0=outra variável)", "Literal value (0=other variable)"),
        ["uMonsterID"] = ("Monstro (id)", "Monster (id)"), ["uMonsterNum"] = ("Quantidade", "Count"),
        ["iDispearTime"] = ("Tempo p/ sumir (-1=sem)", "Despawn time (-1=none)"), ["iDispearCondition"] = ("Condição de sumiço", "Despawn condition"),
        ["iUnk4"] = ("Desconhecido 4", "Unknown 4"), ["iUnk5"] = ("Desconhecido 5", "Unknown 5"),
        ["uPathGroup"] = ("Grupo do caminho", "Path group"), ["uPathID"] = ("Caminho (id)", "Path (id)"),
        ["iPatrolType"] = ("Tipo de patrulha", "Patrol type"), ["iSpeedType"] = ("Velocidade", "Speed"),
        ["szActionName"] = ("Nome da animação", "Action name"), ["uLoop"] = ("Repetições da animação", "Loop count"),
        ["uInterval"] = ("Intervalo", "Interval"), ["uPlayTime"] = ("Duração", "Play time"), ["uPoints"] = ("Pontos", "Points"),
        ["iDst"] = ("Destino", "Destination"), ["iDstType"] = ("Tipo do destino", "Destination type"),
        ["iSrc1"] = ("Origem 1", "Source 1"), ["iSrc1Type"] = ("Tipo da origem 1", "Source 1 type"), ["iOp"] = ("Operador", "Operator"),
        ["iSrc2"] = ("Origem 2", "Source 2"), ["iSrc2Type"] = ("Tipo da origem 2", "Source 2 type"),
        ["uOccupationMask"] = ("Classes (máscara)", "Classes (mask)"),
    };

    public static string FieldLabel(string name) => FieldNames.TryGetValue(name, out var v) ? $"{L(v.Pt, v.En)}  [{name}]" : name;

    /// <summary>Significado do valor de um campo (nome do monstro/habilidade, enum), ou null.</summary>
    public static string? Hint(string field, int value, AiContext c) => field switch
    {
        "uMonsterID" => c.MonsterName(value) is { } m ? c.Translate(m) ?? m : null,
        "uSkill" => c.SkillName(value),
        "iDispearCondition" => value >= 0 && value < Dispear.Length ? L(Dispear[value].Pt, Dispear[value].En) : null,
        "iPatrolType" => value >= 0 && value < Patrol.Length ? L(Patrol[value].Pt, Patrol[value].En) : null,
        "iSpeedType" => value >= 0 && value < Speed.Length ? L(Speed[value].Pt, Speed[value].En) : null,
        "iDstType" or "iSrc1Type" or "iSrc2Type" => value >= 0 && value < VarType.Length ? L(VarType[value].Pt, VarType[value].En) : null,
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
