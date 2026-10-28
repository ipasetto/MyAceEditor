using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MyAceEditor.PerfectWorld;

// aipolicy.data (PW-KB §S18). Binário LE:
//   i32 versão | i32 nPolicy | Policy{ i32 versão, i32 id, i32 nTrig, Trigger{ i32 dwVersion, i32 id, u8 ativo, u8 executa,
//   u8 ataqueValido, char[128] nome GBK, CondTree, i32 nOp, Op{ i32 tipo, <param SEM tamanho>, Alvo{ i32 tipo, <param> } } } }
// CondTree (pré-ordem): i32 tipo | i32 tamanhoParam | param | { 1 filho-esq | 2 filho-dir }* | 3 (folha) ou 4 (com filhos)
//
// RISCO (§S18): operação e alvo NÃO têm tamanho. Aqui o tamanho vem de AiLayout (tabela em AiSchema). Cada op guarda os
// bytes crus do parâmetro; ao gravar, só o que foi editado muda -> round-trip idêntico se os tamanhos estiverem certos.
// Tipo sem layout => o arquivo NÃO abre para edição (AiPolicyFile.Error diz policy/trigger/op/offset + hex).

public sealed class AiParseException : Exception
{
    public int Offset { get; }
    public AiParseException(string message, int offset) : base(message) => Offset = offset;
}

/// <summary>Um campo de parâmetro. Kind: i=int32 f=float32 b=byte(bool) 3=3 bytes de padding a=char[128] GBK t=i32 tamanho EM BYTES + UTF-16 com \0.</summary>
public sealed class AiField
{
    public string Name = "";
    public char Kind;
    public int MinVer;
    /// <summary>Só existe ANTES deste dwVersion (int.MaxValue = sem limite).</summary>
    public int MaxVer = int.MaxValue;
}

/// <summary>
/// Layout de parâmetro, escrito como texto: "i:uSkill i:uLevel b:bStop@1 3:pad@1" (@N = só a partir do dwVersion N;
/// &lt;N = só antes do dwVersion N, para as structs antigas que o Policy.cpp oficial ainda converte).
/// </summary>
public sealed class AiLayout
{
    public AiField[] Fields { get; }
    public string Spec { get; }
    /// <summary>true = layout inferido, ainda não provado contra um aipolicy.data real.</summary>
    public bool Guess { get; }

    public AiLayout(string spec, bool guess)
    {
        Spec = spec; Guess = guess;
        Fields = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(tok =>
        {
            int c = tok.IndexOf(':');
            if (c != 1) throw new FormatException($"campo inválido '{tok}' (use tipo:nome ou tipo:nome@versão)");
            var rest = tok[(c + 1)..]; int ver = 0, below = int.MaxValue;
            int lt = rest.IndexOf('<');
            if (lt >= 0) { below = int.Parse(rest[(lt + 1)..], CultureInfo.InvariantCulture); rest = rest[..lt]; }
            int at = rest.IndexOf('@');
            if (at >= 0) { ver = int.Parse(rest[(at + 1)..], CultureInfo.InvariantCulture); rest = rest[..at]; }
            if ("ifb3at".IndexOf(tok[0]) < 0) throw new FormatException($"tipo de campo '{tok[0]}' desconhecido");
            return new AiField { Kind = tok[0], Name = rest, MinVer = ver, MaxVer = below };
        }).ToArray();
    }

    public List<AiField> Active(int ver) => Fields.Where(f => ver >= f.MinVer && ver < f.MaxVer).ToList();

    /// <summary>Quantos bytes o parâmetro ocupa em data[pos..]. false = dados insuficientes/inválidos.</summary>
    public bool TryMeasure(int ver, byte[] d, int pos, out int len)
    {
        len = 0;
        foreach (var f in Active(ver))
        {
            switch (f.Kind)
            {
                case 'i': case 'f': len += 4; break;
                case 'b': len += 1; break;
                case '3': len += 3; break;
                case 'a': len += 128; break;
                case 't':
                    if (pos + len + 4 > d.Length) return false;
                    int n = BitConverter.ToInt32(d, pos + len);
                    if (n < 0 || n > 65535) return false;
                    len += 4 + n; break;      // n = BYTES do texto UTF-16 incluindo o \0 (provado no arquivo real)
            }
            if (pos + len > d.Length) return false;
        }
        return true;
    }

    public List<object> Decode(int ver, byte[] p)
    {
        var vals = new List<object>(); int o = 0;
        foreach (var f in Active(ver))
        {
            switch (f.Kind)
            {
                case 'i': vals.Add(BitConverter.ToInt32(p, o)); o += 4; break;
                case 'f': vals.Add(BitConverter.ToSingle(p, o)); o += 4; break;
                case 'b': vals.Add(p[o]); o += 1; break;
                case '3': vals.Add(p[o..(o + 3)]); o += 3; break;
                case 'a':
                    int z = Array.IndexOf(p, (byte)0, o, 128);
                    vals.Add(AiPolicyFile.Gbk.GetString(p, o, z >= 0 ? z - o : 128)); o += 128; break;
                case 't':
                    int n = BitConverter.ToInt32(p, o); o += 4;
                    var s = Encoding.Unicode.GetString(p, o, n & ~1); o += n;
                    int nul = s.IndexOf('\0'); vals.Add(nul >= 0 ? s[..nul] : s); break;
            }
        }
        return vals;
    }

    public byte[] Encode(int ver, IList<object> vals)
    {
        using var ms = new MemoryStream(); var w = new BinaryWriter(ms); int k = 0;
        foreach (var f in Active(ver))
        {
            var v = vals[k++];
            switch (f.Kind)
            {
                case 'i': w.Write(Convert.ToInt32(v, CultureInfo.InvariantCulture)); break;
                case 'f': w.Write(Convert.ToSingle(v, CultureInfo.InvariantCulture)); break;
                case 'b': w.Write(Convert.ToByte(v, CultureInfo.InvariantCulture)); break;
                case '3': w.Write((byte[])v); break;
                case 'a':
                    var g = AiPolicyFile.Gbk.GetBytes((string)v);
                    if (g.Length > 127) throw new InvalidDataException(string.Format(UI.Loc.T("aif.fieldTooLong", "'{0}' passa de 127 bytes em GBK."), f.Name));
                    w.Write(g); w.Write(new byte[128 - g.Length]); break;
                case 't':
                    var s = (string)v;
                    if (s.Length == 0) { w.Write(0); break; }
                    w.Write((s.Length + 1) * 2); w.Write(Encoding.Unicode.GetBytes(s)); w.Write((ushort)0); break;
            }
        }
        return ms.ToArray();
    }

    public List<object> Zero(int ver) => Active(ver).Select(f => f.Kind switch
    {
        'i' => (object)0, 'f' => 0f, 'b' => (byte)0, '3' => new byte[3], _ => ""
    }).ToList();
}

/// <summary>Nomes e layouts. Layouts extras/correções podem ser dados sem recompilar: aipolicy_layouts.json ao lado do .exe.</summary>
public static class AiSchema
{
    // nomes e números: enums _e_condition/_e_operation/_e_target do policy.h oficial (cgame/gs/ai, source 1.7.2)
    public static readonly string[] CondNames =
    {
        "time_come", "hp_less", "start_attack", "random", "kill_player", "NOT", "OR", "AND", "died", "+", "-", "*", "/",
        ">", "<", "==", "var_global", "const", "be_hurt", "reach_end", "history_stage", "history_value", "stop_fight",
        "local_var", "reach_end_2", "has_filter", "room_index", "players_in_radius", "players_in_region",
        "servertime_day", "servertime_week", "not_bind_carrier", "reach_start", "carrier_full", "time_come_2"
    };

    public static readonly string[] OpNames =
    {
        "attack", "use_skill", "talk", "reset_hate", "run_trigger", "stop_trigger", "active_trigger", "create_timer",
        "kill_timer", "flee", "hate_first", "hate_last", "hate_50%", "skip", "active_controller", "set_global",
        "revise_global", "summon_monster", "walk_along", "play_action", "history_a", "history_b", "faction_pvp_points", "calc_var",
        "summon_monster_2", "walk_along_2", "use_skill_2", "active_controller_2", "deliver_task", "summon_mine", "summon_npc",
        "deliver_random_task_in_region", "deliver_task_in_hate_list", "clear_tower_task_in_region",
        "save_players_in_radius", "save_players_in_region", "skill_with_talk", "use_skill_3", "sort_num", "get_pos_num",
        "auto_bind_carrier", "add_range_to_hate", "save_alive_in_radius", "save_alive_in_region", "walk_along_3", "walk_along_4",
        "save_time", "random_assignment", "carrier_voting", "voting_result", "voting_show", "carrier_delivery_task",
        "carrier_noentry", "talk_2", "create_timer_2", "kill_timer_2", "change_fighting_state", "change_active_passive",
        "child_carrier_parent", "close_child_monster", "deliver_hate_targets", "change_attack_policy",
        "specify_failed_task", "specify_failed_task_regional"
    };

    public static readonly string[] TargetNames =
    {
        "hate_first", "hate_second", "hate_others", "most_hp", "most_mp", "least_hp", "occupation_list", "self", "killer",
        "birthplace_faction", "hate_random", "nearest", "farthest", "hate_first_redirected",
        "random_in_range", "nearest_in_range", "farthest_in_range"
    };

    public const int MaxOpType = 102, MaxTargetType = 20;

    public static string CondName(int t) => t >= 0 && t < CondNames.Length ? CondNames[t] : $"cond_{t}";
    public static string OpName(int t) => t >= 0 && t < OpNames.Length ? OpNames[t] : $"op_{t}";
    public static string TargetName(int t) => t >= 0 && t < TargetNames.Length ? TargetNames[t] : $"alvo_{t}";

    // Layouts = structs de policytype.h + CTriggerData::ReadOperationParam/ReadOperationTarget do Policy.cpp oficial
    // (source 1.7.2, F_TRIGGER_VERSION 40). Cada op é lida com sizeof(struct), alinhamento de 4 (bool + 3 B de padding),
    // exceto as convertidas por versão (play_action, summon_mine, set_global, active_controller, summon_monster) e as que
    // trazem texto (talk, skill_with_talk, talk_2, o_72). false = conferido com o código oficial.
    static readonly Dictionary<int, AiLayout> Ops = BuildOps();

    static Dictionary<int, AiLayout> BuildOps()
    {
        const string Talk = "t:text i:uAppendDataMask@17";
        const string Region = "f:fMinX f:fMinY f:fMinZ f:fMaxX f:fMaxY f:fMaxZ";
        var o = new Dictionary<int, AiLayout>
        {
            [0] = new("i:uType", false),
            [1] = new("i:uSkill i:uLevel", false),
            [2] = new(Talk, false),                                   // i32 tamanho + UTF-16LE c/ \0; máscara >=17
            [3] = new("", false),
            [4] = new("i:uID", false), [5] = new("i:uID", false), [6] = new("i:uID", false),
            [7] = new("i:uID i:uPeriod i:uCounter", false),
            [8] = new("i:uID", false),
            [9] = new("", false), [10] = new("", false), [11] = new("", false), [12] = new("", false), [13] = new("", false),
            [14] = new("i:uID b:bStop@1 3:pad@1", false),
            [15] = new("i:iKey i:iValue b:bIsValue@4 3:pad@4", false),  // +3B de padding (com lixo) [T]
            [16] = new("i:iKey i:iValue", false),
            // <7: iMonsterID iRange iLife bDispear bUsePolicyTarget(+2B) iPathID iMonsterNum — mesmos 24 B
            [17] = new("i:uMonsterID i:iRange i:iDispearTime i:iDispearCondition i:uPathID i:uMonsterNum", false),
            [18] = new("i:uPathGroup i:uPathID i:iPatrolType i:iSpeedType", false),   // uPathGroup = iWorldID
            [19] = new("a:szActionName b:bLoop<9 3:pad<9 i:uLoop@9 i:uInterval i:uPlayTime@9 i:iUnknown@38", false),
            [20] = new("i:uID i:iValue", false),
            [21] = new("i:uID i:iValue b:bIsHistoryValue 3:pad", false),
            [22] = new("i:uPoints", false),
            [23] = new("i:iDst i:iDstType i:iSrc1 i:iSrc1Type i:iOp i:iSrc2 i:iSrc2Type", false),
            [24] = new("i:iDispearCondition i:uMonsterID i:iMonsterIDType i:iRange i:iDispearTime i:uPathID i:iPathIDType i:uMonsterNum i:iMonsterNumType", false),
            [25] = new("i:uPathGroup i:uPathID i:iPathIDType i:iPatrolType i:iSpeedType", false),
            [26] = new("i:uSkill i:uSkillType i:uLevel i:uLevelType", false),
            [27] = new("i:uID i:uIDType b:bStop 3:pad", false),
            [28] = new("i:uID i:uIDType", false),
            [29] = new("i:iLifeType i:iMineID i:iMineIDType i:iRange i:iLife i:iMineNum i:iMineNumType b:bSetOwner@24 3:pad@24", false),
            [30] = new("i:iLifeType i:iNPCID i:iNPCIDType i:iRange i:iLife i:uPathID i:iPathIDType i:iNPCNum i:iNPCNumType", false),
            [31] = new("i:uID " + Region, false),
            [32] = new("i:uID i:uIDType i:iRange i:iPlayerNum", false),
            [33] = new(Region, false),
            [34] = new("f:fRadius i:uRadiusType i:iTargetID i:uTargetType", false),
            [35] = new(Region + " i:iTargetID i:uTargetType", false),
            [36] = new("i:uSkill i:uSkillType i:uLevel i:uLevelType " + Talk, false),
            [37] = new("i:uSkill i:uSkillType i:uLevel i:uLevelType i:iTmpID i:iLife", false),
            [38] = new("i:uNum i:uNumType i:uVarID i:uVarType", false),
            [39] = new("i:uNum i:uNumType i:uPos i:uPosType i:uVarID i:uVarType", false),
            [40] = new("f:fRange", false),
            [41] = new("i:iRange i:uRangeType", false),
            [42] = new("f:fRadius i:uRadiusType i:iTargetID i:uTargetType", false),
            [43] = new(Region + " i:iTargetID i:uTargetType", false),
            [44] = new("i:uPathGroup i:iStartPathID i:iStartPathIDType i:iEndPathID i:iEndPathIDType i:iPatrolType i:iSpeedType", false),
            [45] = new("i:uPathGroup i:iStartPathID i:iStartPathIDType i:iEndPathID i:iEndPathIDType i:iMinPathID i:iMinPathIDType i:iMaxPathID i:iMaxPathIDType i:iPatrolType i:iSpeedType i:iOrientationType", false),
            [46] = new("i:iTimeType i:iParamID i:iParamIDType", false),
            [47] = new("i:iRandomMin i:iRandomMax i:iQuantity i:iTargetID", false),
            [48] = new("i:iVoteID i:iVoteIDType", false),
            [49] = new("i:iVoteID i:iVoteIDType i:iSelect i:iSelectType i:iSaveResult i:iSaveResultType", false),
            [50] = new("i:iStyle " + Ints("iShow", 16), false),
            [51] = new("i:iTaskID i:iTaskIDType", false),
            [52] = new("i:iOpen", false),
            [53] = new(Talk + " i:iChannelKinds i:iChannelKindsType i:iChannelID i:iChannelIDType", false),
            [54] = new("i:uID i:iIDType i:uPeriod i:iPeriodType i:uCounter i:iCounterType", false),
            [55] = new("i:uID i:iIDType", false),
            [56] = new("i:iChangeState", false),
            [57] = new("i:iChoose", false),
            [58] = new("f:fRange i:iPos", false),
            [59] = new("i:uMonsterID", false),
            [60] = new("i:uMonsterID", false),
            [61] = new("i:iAttackPolicyID", false),
            [62] = new("i:iTaskID i:iTaskIDType", false),
            [63] = new("i:iTaskID i:iTaskIDType " + Region, false),
            [72] = new(Talk + " " + Region, false),   // lido à parte no Policy.cpp: texto + 2 vértices
            [84] = new("", false), [97] = new("", false),
            [94] = new("b:bVal1", false), [95] = new("b:bVal1", false),   // struct { bool } = 1 B
        };
        // structs sem nome (O_64..O_102): só inteiros, nº de campos = sizeof/4
        var ints = new Dictionary<int, int>
        {
            [64] = 21, [65] = 11, [66] = 8, [67] = 7, [68] = 3, [69] = 2, [70] = 1, [71] = 2, [73] = 2, [74] = 17, [75] = 1,
            [76] = 4, [77] = 6, [78] = 10, [79] = 2, [80] = 6, [81] = 6, [82] = 7, [83] = 11, [85] = 7, [86] = 3, [87] = 11,
            [88] = 3, [89] = 10, [90] = 4, [91] = 6, [92] = 9, [93] = 10, [96] = 2, [98] = 1, [99] = 20, [100] = 2, [101] = 2, [102] = 2,
        };
        foreach (var (k, n) in ints) o[k] = new(Ints("iVal", n), false);
        return o;
    }

    static string Ints(string prefix, int n) => string.Join(" ", Enumerable.Range(1, n).Select(i => $"i:{prefix}{i}"));

    // ReadOperationTarget: 6 = máscara de classes; 14..16 = alcance em metros; 18 = 12 B; 20 = 16 B; o resto sem parâmetro
    static readonly Dictionary<int, AiLayout> Targets = new()
    {
        [0] = new("", false), [1] = new("", false), [2] = new("", false), [3] = new("", false), [4] = new("", false),
        [5] = new("", false), [6] = new("i:uOccupationMask", false), [7] = new("", false), [8] = new("", false), [9] = new("", false),
        [10] = new("", false), [11] = new("", false), [12] = new("", false), [13] = new("", false),
        [14] = new("i:uMeter", false), [15] = new("i:uMeter", false), [16] = new("i:uMeter", false),
        [17] = new("", false), [18] = new("i:iVal1 i:iVal2 i:iVal3", false), [19] = new("", false),
        [20] = new("i:iVal1 i:iVal2 i:iVal3 i:iVal4", false),
    };

    static AiSchema()
    {
        TryLoadOverrides(Path.Combine(AppContext.BaseDirectory, "aipolicy_layouts.json"));
    }

    public static AiLayout? Op(int type) => Ops.GetValueOrDefault(type);
    public static AiLayout? Target(int type) => Targets.GetValueOrDefault(type);
    public static IEnumerable<int> KnownOps => Ops.Keys.OrderBy(k => k);

    /// <summary>{"ops":{"17":"i:uMonsterID i:uNum ..."},"targets":{"14":""}} — sobrescreve/complementa a tabela.</summary>
    public static string? TryLoadOverrides(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var (section, table) in new[] { ("ops", Ops), ("targets", Targets) })
                if (doc.RootElement.TryGetProperty(section, out var el))
                    foreach (var p in el.EnumerateObject()) table[int.Parse(p.Name)] = new AiLayout(p.Value.GetString() ?? "", false);
            return null;
        }
        catch (Exception ex) { return $"aipolicy_layouts.json inválido: {ex.Message}"; }
    }
}

public sealed class AiCondNode
{
    public int Type;
    public byte[] Param = Array.Empty<byte>();
    public AiCondNode? Left, Right;
    /// <summary>3 = folha, 4 = com filhos (guardado para gravar idêntico).</summary>
    public int EndMarker = 3;
    public IEnumerable<AiCondNode> Walk() { yield return this; if (Left != null) foreach (var n in Left.Walk()) yield return n; if (Right != null) foreach (var n in Right.Walk()) yield return n; }
}

public sealed class AiTarget { public int Type = 7; public byte[] Param = Array.Empty<byte>(); }

public sealed class AiOp { public int Type; public byte[] Param = Array.Empty<byte>(); public AiTarget Target = new(); }

public sealed class AiTrigger
{
    /// <summary>Cópia completa com outro ID (os bytes de parâmetros são copiados, nada é recodificado).</summary>
    public AiTrigger Clone(int newId)
    {
        static AiCondNode C(AiCondNode n) => new() { Type = n.Type, Param = (byte[])n.Param.Clone(), EndMarker = n.EndMarker, Left = n.Left is null ? null : C(n.Left), Right = n.Right is null ? null : C(n.Right) };
        return new AiTrigger
        {
            Version = Version, Id = newId, Active = Active, Run = Run, AttackValid = AttackValid, NameRaw = (byte[])NameRaw.Clone(), Root = C(Root),
            Ops = Ops.Select(o => new AiOp { Type = o.Type, Param = (byte[])o.Param.Clone(), Target = new AiTarget { Type = o.Target.Type, Param = (byte[])o.Target.Param.Clone() } }).ToList(),
        };
    }

    public int Version, Id;
    public byte Active = 1, Run = 1, AttackValid = 1;
    public byte[] NameRaw = new byte[128];
    public AiCondNode Root = new();
    public List<AiOp> Ops = new();

    public string Name
    {
        get { int z = Array.IndexOf(NameRaw, (byte)0); return AiPolicyFile.Gbk.GetString(NameRaw, 0, z >= 0 ? z : NameRaw.Length); }
        set
        {
            var g = AiPolicyFile.Gbk.GetBytes(value);
            if (g.Length > 127) throw new InvalidDataException(UI.Loc.T("aif.nameTooLong", "Nome passa de 127 bytes em GBK."));
            var raw = new byte[128]; g.CopyTo(raw, 0); NameRaw = raw;
        }
    }
}

public sealed class AiPolicy
{
    public int Version, Id;
    public List<AiTrigger> Triggers = new();

    /// <summary>Cópia completa (gatilhos, condições e operações) com outro ID de política.</summary>
    public AiPolicy Clone(int newId) => new() { Version = Version, Id = newId, Triggers = Triggers.Select(t => t.Clone(t.Id)).ToList() };
}

public sealed class AiPolicyFile
{
    public static readonly Encoding Gbk;
    static AiPolicyFile() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public byte[] Data { get; private set; }
    public int Version { get; private set; }
    public List<AiPolicy> Policies { get; } = new();
    public byte[] Tail { get; private set; } = Array.Empty<byte>();

    /// <summary>Preenchido quando o esquema não cobre o arquivo inteiro. Nesse caso NÃO edite nem salve.</summary>
    public string? Error { get; private set; }
    public bool Ok => Error is null;
    public SortedDictionary<int, int> OpHistogram { get; } = new();

    public AiPolicyFile(string path) : this(File.ReadAllBytes(path)) { }

    public AiPolicyFile(byte[] data)
    {
        Data = data;
        var r = new Reader(data);
        try
        {
            Version = r.I32();
            int nPol = r.I32();
            if (nPol < 0 || nPol > 1_000_000) throw new AiParseException($"nº de políticas inválido ({nPol})", 4);
            for (int p = 0; p < nPol; p++)
            {
                r.Ctx = $"política #{p} (início {r.Pos})";
                var pol = new AiPolicy { Version = r.I32(), Id = r.I32() };
                int nTrig = r.I32();
                if (nTrig < 0 || nTrig > 100_000) throw new AiParseException($"nº de triggers inválido ({nTrig})", r.Pos - 4);
                for (int t = 0; t < nTrig; t++)
                {
                    r.Ctx = $"política #{p} (id {pol.Id}), trigger #{t} (início {r.Pos})";
                    var trg = new AiTrigger { Version = r.I32(), Id = r.I32(), Active = r.U8(), Run = r.U8(), AttackValid = r.U8(), NameRaw = r.Bytes(128) };
                    trg.Root = ReadCond(r, 0);
                    int nOp = r.I32();
                    if (nOp < 0 || nOp > 100_000) throw new AiParseException($"nº de operações inválido ({nOp})", r.Pos - 4);
                    for (int o = 0; o < nOp; o++)
                    {
                        r.Ctx = $"política #{p} (id {pol.Id}), trigger #{t} (id {trg.Id}, dwVersion {trg.Version}), op #{o}";
                        trg.Ops.Add(ReadOp(r, trg.Version));
                    }
                    pol.Triggers.Add(trg);
                }
                Policies.Add(pol);
            }
            Tail = r.Bytes(data.Length - r.Pos);
            if (Tail.Length > 0) Error = $"sobraram {Tail.Length} bytes depois da última política (offset {r.Pos}) — esquema provavelmente desalinhado.";
        }
        catch (AiParseException ex)
        {
            Error = $"{ex.Message}\n  onde: {r.Ctx}\n  offset: {ex.Offset} (0x{ex.Offset:X})\n  próximos bytes: {Hex(data, ex.Offset, 48)}"
                + (r.LastOp is { } lo ? $"\n  última operação lida antes: {lo} — se o erro vem logo depois, o layout dela é o suspeito" : "");
        }
    }

    static AiCondNode ReadCond(Reader r, int depth)
    {
        if (depth > 256) throw new AiParseException("árvore de condições profunda demais", r.Pos);
        var n = new AiCondNode { Type = r.I32() };
        int size = r.I32();
        if (size < 0 || size > 4096) throw new AiParseException($"tamanho de parâmetro de condição inválido ({size})", r.Pos - 4);
        n.Param = r.Bytes(size);
        while (true)
        {
            int m = r.I32();
            if (m == 1) n.Left = ReadCond(r, depth + 1);
            else if (m == 2) n.Right = ReadCond(r, depth + 1);
            else if (m == 3 || m == 4) { n.EndMarker = m; return n; }
            else throw new AiParseException($"marcador de árvore inválido ({m}); esperado 1, 2, 3 ou 4", r.Pos - 4);
        }
    }

    AiOp ReadOp(Reader r, int ver)
    {
        int start = r.Pos;
        int type = r.I32();
        if (type < 0 || type > AiSchema.MaxOpType) throw new AiParseException($"tipo de operação inválido ({type}); esperado 0..{AiSchema.MaxOpType}", start);
        var lay = AiSchema.Op(type) ?? throw new AiParseException($"operação {type} ({AiSchema.OpName(type)}) sem layout — veja aipolicy_layouts.json / PW-KB §S18", start);
        if (!lay.TryMeasure(ver, r.Data, r.Pos, out int len)) throw new AiParseException($"parâmetro da operação {type} ({AiSchema.OpName(type)}) passa do fim do arquivo", r.Pos);
        var op = new AiOp { Type = type, Param = r.Bytes(len) };
        OpHistogram[type] = OpHistogram.GetValueOrDefault(type) + 1;

        int tPos = r.Pos;
        int tt = r.I32();
        if (tt < 0 || tt > AiSchema.MaxTargetType) throw new AiParseException($"tipo de alvo inválido ({tt}) após a operação {type} ({AiSchema.OpName(type)}); layout da operação provavelmente errado", tPos);
        var tl = AiSchema.Target(tt) ?? throw new AiParseException($"alvo {tt} ({AiSchema.TargetName(tt)}) sem layout", tPos);
        if (!tl.TryMeasure(ver, r.Data, r.Pos, out int tlen)) throw new AiParseException($"parâmetro do alvo {tt} passa do fim do arquivo", r.Pos);
        op.Target = new AiTarget { Type = tt, Param = r.Bytes(tlen) };
        r.LastOp = $"tipo {type} ({AiSchema.OpName(type)}), {len} B de parâmetro, alvo {tt} ({tlen} B), em {start}..{r.Pos} ({r.Ctx})";
        return op;
    }

    /// <summary>Serializa o estado atual.</summary>
    public byte[] Build()
    {
        using var ms = new MemoryStream(Data.Length + 1024);
        var w = new BinaryWriter(ms);
        w.Write(Version); w.Write(Policies.Count);
        foreach (var p in Policies)
        {
            w.Write(p.Version); w.Write(p.Id); w.Write(p.Triggers.Count);
            foreach (var t in p.Triggers)
            {
                w.Write(t.Version); w.Write(t.Id); w.Write(t.Active); w.Write(t.Run); w.Write(t.AttackValid);
                w.Write(t.NameRaw.Length == 128 ? t.NameRaw : ResizeName(t.NameRaw));
                WriteCond(w, t.Root);
                w.Write(t.Ops.Count);
                foreach (var o in t.Ops) { w.Write(o.Type); w.Write(o.Param); w.Write(o.Target.Type); w.Write(o.Target.Param); }
            }
        }
        w.Write(Tail);
        return ms.ToArray();
    }

    static byte[] ResizeName(byte[] b) { var r = new byte[128]; Array.Copy(b, r, Math.Min(127, b.Length)); return r; }

    static void WriteCond(BinaryWriter w, AiCondNode n)
    {
        w.Write(n.Type); w.Write(n.Param.Length); w.Write(n.Param);
        if (n.Left != null) { w.Write(1); WriteCond(w, n.Left); }
        if (n.Right != null) { w.Write(2); WriteCond(w, n.Right); }
        w.Write(n.Left != null || n.Right != null ? 4 : 3);
    }

    /// <summary>Prova do round-trip: reler->gravar sem mudar nada tem que dar o MESMO arquivo.</summary>
    public bool RoundTripIdentical() => Ok && Build().AsSpan().SequenceEqual(Data);

    /// <summary>Backup com data/hora + escrita atômica (mesma regra de elements.data/tasks.data).</summary>
    public string? Save(string path)
    {
        if (!Ok) throw new InvalidOperationException(UI.Loc.T("aif.incomplete", "Arquivo não foi lido por inteiro; salvar poderia corromper o aipolicy.data."));
        var bytes = Build();
        string? backup = null;
        if (File.Exists(path)) { backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak"; File.Copy(path, backup, overwrite: false); }
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        Data = bytes;
        return backup;
    }

    public static string Hex(byte[] d, int pos, int n)
    {
        if (pos < 0 || pos > d.Length) return "";
        return string.Join(" ", d.Skip(pos).Take(n).Select(b => b.ToString("X2")));
    }

    sealed class Reader
    {
        public readonly byte[] Data; public int Pos; public string Ctx = "cabeçalho"; public string? LastOp;
        public Reader(byte[] d) => Data = d;
        void Need(int n) { if (n < 0 || Pos + n > Data.Length) throw new AiParseException("fim inesperado do arquivo", Pos); }
        public int I32() { Need(4); var v = BitConverter.ToInt32(Data, Pos); Pos += 4; return v; }
        public byte U8() { Need(1); return Data[Pos++]; }
        public byte[] Bytes(int n) { Need(n); var b = Data[Pos..(Pos + n)]; Pos += n; return b; }
    }
}
