using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Tipo primitivo de um campo de quest.</summary>
public enum TF : byte { I32, U8, Bool, F32 }

/// <summary>Campo escalar lido (posição relativa ao início da quest → editável no lugar).</summary>
public sealed record TaskField(string Group, string Name, TF Type, int Offset, int? PreservedValue = null)
{
    public int Size => Type switch { TF.I32 or TF.F32 => 4, _ => 1 };
    public bool Editable => PreservedValue is null;
}

/// <summary>Uma janela de diálogo (talk_proc → windows → options).</summary>
public sealed class TalkWindow
{
    public int Id, Parent;
    public string Text = "";
    public List<TalkOption> Options = new();
}

/// <summary>Opção de uma janela de diálogo. Raw = 128 bytes originais do texto (preserva o "lixo" após o \0).</summary>
public sealed class TalkOption
{
    public int Param, Id;
    public string Text = "", OriginalText = "";
    public byte[] Raw = Array.Empty<byte>();
}

public sealed class TalkProc
{
    public int IdTalk;
    public string Text = "", OriginalText = "";
    public byte[] Raw = Array.Empty<byte>();
    public List<TalkWindow> Windows = new();
}

/// <summary>
/// Quest lida do tasks.data (qualquer nível). Os bytes do bloco fixo (dados + prêmios) ficam em
/// <see cref="Fixed"/>; campos escalares são editados no lugar. Textos e diálogos são decodificados
/// (UTF-16 XOR ID) e só recodificados se mudarem — sem mudanças, a gravação é byte-idêntica.
/// </summary>
public sealed class QuestNode
{
    public int Id;
    public string Name = "", OriginalName = "";
    public byte[] Fixed = Array.Empty<byte>();
    public byte[] TextRaw = Array.Empty<byte>();                // textos + diálogos como estavam no arquivo
    public List<TaskField> Fields = new();
    public string[] Texts = new string[4];                      // descrição, aceitar(ok), recusar(no), tributo
    public TalkProc[] Talks = new TalkProc[5];                  // ao entregar, sem requisito, entregar item, em andamento, prêmio
    public bool TextsChanged;
    /// <summary>Para cada texto de tamanho variável (na ordem do arquivo): se terminava com \0.</summary>
    public List<bool> NullFlags = new();
    public List<QuestNode> Children = new();
    public QuestNode? Parent;

    public static readonly string[] TextNames = { "描述", "接受任务文本", "拒绝任务文本", "贡品文本" };
    public static readonly string[] TalkNames = { "交付任务对话", "条件不满足时的对话", "交付物品对话", "任务进行中对话", "奖励对话" };

    public string Get(TaskField f) => f.Type switch
    {
        TF.I32 => BitConverter.ToInt32(Fixed, f.Offset).ToString(),
        TF.F32 => BitConverter.ToSingle(Fixed, f.Offset).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        TF.Bool => Fixed[f.Offset] != 0 ? "1" : "0",
        _ => Fixed[f.Offset].ToString(),
    };

    public bool CanSet(TaskField f, string v) => TryEncode(f, v, out _);

    public bool Set(TaskField f, string v)
    {
        if (!f.Editable) return false;
        if (v == Get(f)) return true; // 未改动显示值时保留浮点NaN、非零布尔等原始位。
        if (!TryEncode(f, v, out var bytes)) return false;
        bytes.CopyTo(Fixed, f.Offset);
        return true;
    }

    private static bool TryEncode(TaskField f, string v, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!f.Editable) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (f.Type)
        {
            case TF.I32 when int.TryParse(v, out var i): bytes = BitConverter.GetBytes(i); return true;
            case TF.F32 when float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out var x): bytes = BitConverter.GetBytes(x); return true;
            case TF.Bool when v is "1" or "true" or "True" or "sim" or "是": bytes = new byte[] { 1 }; return true;
            case TF.Bool when v is "0" or "false" or "False" or "nao" or "não" or "否": bytes = new byte[] { 0 }; return true;
            case TF.U8 when byte.TryParse(v, out var b): bytes = new byte[] { b }; return true;
        }
        return false;
    }

    public IEnumerable<QuestNode> Descendants() { foreach (var c in Children) { yield return c; foreach (var d in c.Descendants()) yield return d; } }
}

/// <summary>
/// Esquema do tasks.data versão 93 (PKG144), escrito a partir do estudo do layout (PW-KB §S21, ENGINE-KB §E3c).
/// Corpo de cada quest: [58 fases do bloco fixo, incluindo arrays variáveis e prêmios] [4 textos pwstr]
/// [5 talk_proc] [i32 nSub + subquests].
/// </summary>
public sealed class TaskSchema93
{
    const int TimetableMax = 24, PremTaskMax = 20, OccupMax = 10, MutexMax = 5, SkillLevN = 4, AwardScales = 5;

    readonly byte[] _d;
    readonly CancellationToken _token;
    int _p, _base;
    QuestNode _q = null!;
    string _group = "";

    public TaskSchema93(byte[] data, CancellationToken cancellationToken = default) { _d = data; _token = cancellationToken; }

    public QuestNode Read(int offset, QuestNode? parent = null, int depth = 0)
    {
        _token.ThrowIfCancellationRequested();
        if (depth > 64) throw new InvalidDataException("子任务嵌套层数过多。");
        var q = new QuestNode { Parent = parent };
        var saveQ = _q; _q = q; _base = offset; _p = offset;

        ReadFixed();
        q.Fixed = _d.AsSpan(offset, _p - offset).ToArray();
        q.Id = BitConverter.ToInt32(q.Fixed, 0);
        q.Name = q.OriginalName = Xor(q.Fixed, 4, 30, q.Id);

        int textStart = _p;
        for (int i = 0; i < 4; i++) q.Texts[i] = PwStr(q.Id);
        for (int i = 0; i < 5; i++) q.Talks[i] = Talk(q.Id);
        q.TextRaw = _d.AsSpan(textStart, _p - textStart).ToArray();

        int nSub = I();
        if (nSub < 0 || nSub > 1000) throw new InvalidDataException($"任务 {q.Id}：子任务数量无效（{nSub}）。");
        for (int i = 0; i < nSub; i++)
        {
            int at = _p;
            var child = Read(at, q, depth + 1);   // recursão usa o mesmo cursor
            q.Children.Add(child);
        }
        _q = saveQ;
        if (saveQ is not null) _base = OffsetOf(saveQ);
        return q;
    }

    // a base de campos é relativa a cada quest; durante a recursão guardamos por quest
    readonly Dictionary<QuestNode, int> _bases = new(ReferenceEqualityComparer.Instance);
    int OffsetOf(QuestNode q) => _bases.TryGetValue(q, out var b) ? b : _base;

    public int Position => _p;

    // ---------- primitivas (registram campos escalares do bloco fixo) ----------
    int I(string? name = null, bool editable = true) { _token.ThrowIfCancellationRequested(); int v = BitConverter.ToInt32(_d, _p); Rec(name, TF.I32, editable ? null : v); _p += 4; return v; }
    bool B(string? name = null, bool editable = true) { bool v = _d[_p] != 0; Rec(name, TF.Bool, editable ? null : _d[_p]); _p += 1; return v; }
    byte U8(string? name = null) { byte v = _d[_p]; Rec(name, TF.U8); _p += 1; return v; }
    void F(string? name = null) { Rec(name, TF.F32); _p += 4; }
    void Skip(int n) { _token.ThrowIfCancellationRequested(); _p += n; if (_p > _d.Length) throw new EndOfStreamException(); }
    void Rec(string? name, TF t, int? preservedValue = null) { if (name is not null && _q is not null) _q.Fields.Add(new TaskField(_group, name, t, _p - _bases.GetValueOrDefault(_q, _base), preservedValue)); }
    void G(string g) { _group = g; }
    void Tm(string name) { foreach (var n in new[] { "年", "月", "日", "时", "分", "星期" }) I($"{name} ({n})"); }
    void Zone(string name) { F($"{name} X"); F($"{name} Y"); F($"{name} Z"); }
    void Kv(string name) { I($"{name}：左值类型"); I($"{name}：左值"); I($"{name}：运算符"); I($"{name}：右值类型"); I($"{name}：右值"); }

    void ReadFixed()
    {
        _bases[_q] = _base;
        // 01
        G("基本"); I("ID", editable: false); Skip(60); var hasSign = B("含签名", editable: false); I(null); I("类型"); I("时间限制（秒）");
        // 02
        G("时间"); B("退出游戏时失败"); B("在指定日期失败"); Tm("失败日期"); B("物品不从背包移除");
        B("绝对时间"); int timetable = I("时间表数量", editable: false); for (int i = 0; i < TimetableMax; i++) U8(i < timetable ? $"时间表类型 {i + 1}" : null);
        I(null); I(null); I("可用频率"); I("次数");
        // 03
        G("基本"); B("选择一个子任务"); B("随机子任务"); B("按顺序完成子任务"); B("父任务同时失败"); B("父任务同时完成");
        B("允许放弃"); B("允许重复"); B("失败后允许重做"); B("放弃时清除"); B("记录任务历史"); B("死亡时失败");
        I("最大玩家数"); G("交付"); B("在指定区域交付"); I("交付地图");
        // 04-06
        int delvRegions = I("交付区域数量", editable: false); I(null);
        B("进入区域时失败"); I("进入区域地图"); int enterRegions = I("进入区域数量", editable: false); I(null);
        B("离开区域时失败"); I("离开区域地图"); int leaveRegions = I("离开区域数量", editable: false); I(null);
        // 07-08
        G("接受任务时"); B("传送"); I("传送地图"); Zone("目标位置"); I("怪物控制器 ID"); B("启用控制器");
        G("基本"); B("自动交付"); B("在专用界面显示"); B("通知服务端"); B("死亡时触发"); B("清除已获得物品");
        I("建议等级"); B("显示提示"); B("主线任务"); G("交付"); I("任务交付 NPC"); I("奖励 NPC");
        // 10
        G("基本"); B("技能任务"); B("允许查找"); B("显示方向"); B("结婚");
        int changeKeys = I("修改变量数量", editable: false); I(null); I(null); I(null);
        B("切换场景时失败"); B("隐藏"); B("授予技能"); I("授予技能 ID"); I("授予技能等级");
        B("完成时显示效果"); B("影响公共任务排名"); B("比较物品与背包"); I("背包空位");
        // 12-13
        G("公共任务 / 贡献"); B("公共任务"); int pqExp = I("公共任务表达式数量", editable: false); I(null); I(null); B("公共子任务"); B("清除贡献");
        int monContrib = I("贡献怪物数量", editable: false); I(null);
        G("需求条件"); I("所需已记录任务数"); B("按已记录任务显示"); I("帮派贡献"); B("按贡献显示");
        B("按账号限制"); I("每个账号次数上限");
        // 18-24
        I("最低等级"); I("最高等级"); B("按等级显示");
        int premItems = I("所需物品数量", editable: false); I(null); B("按物品显示");
        G("给予物品"); int givenItems = I("给予物品数量", editable: false); I("给予普通物品数"); I("给予任务物品数"); I(null);
        G("需求条件"); I("押金"); B("按押金显示"); I("最低声望"); I("最高声望"); B("按声望显示");
        I("前置任务数量"); for (int i = 0; i < PremTaskMax; i++) I($"前置任务 {i + 1}");
        B("按前置任务显示"); I("最少前置任务数"); I("修真阶段"); B("按修真阶段显示");
        I("帮派"); I("帮派职位"); B("按帮派显示"); I("性别"); B("按性别显示");
        I("职业数量"); for (int i = 0; i < OccupMax; i++) I($"职业 {i + 1}"); B("按职业显示"); B("需要配偶"); B("按配偶显示");
        B("仅限 GM"); B("限制用户"); B("按元宝条件显示"); I("最低元宝数"); I("最高元宝数"); B("角色时间条件"); B("按角色时间显示");
        I("角色时间起点"); I("角色时间终点"); Tm("角色结束日期"); I("角色时间大于");
        I("关联任务"); I("关联任务条件"); I("互斥任务数量"); for (int i = 0; i < MutexMax; i++) I($"互斥任务 {i + 1}");
        for (int i = 0; i < SkillLevN; i++) I($"技能等级 {i + 1}");
        U8("动态任务类型"); I("特殊奖励");
        // 25-27
        G("队伍"); B("需要组队"); B("由队伍接受"); B("共享任务"); B("共享完成状态"); B("检查队友"); F("队友距离");
        B("全员失败"); B("队长失败时失败"); B("队长完成时完成"); F("完成判定距离"); B("离队时失败"); B("接受时检查队员");
        F("接受时队员距离"); B("按队员位置计数"); F("计数距离"); B("全员完成"); B("仅限夫妻"); B("要求职业不同");
        int teamMems = I("队员条件数量", editable: false); I(null); B("按队伍显示");
        G("需求条件"); B("比较变量（条件）"); I("与 / 或（条件）"); Kv("变量 1（条件）"); Kv("变量 2（条件）");
        // 32-37
        G("完成条件"); I("完成方法"); I("完成类型"); int monWanted = I("击杀怪物数量", editable: false); I(null);
        int itemsWanted = I("收集物品数量", editable: false); I(null); I("所需金币"); I("所需帮派贡献"); I("所需帮派经验");
        I("保护目标 NPC"); I("保护时长"); I("移动 NPC"); I("NPC 目的地");
        I(null); int reachSites = I("需到达地点数量", editable: false); I("到达地图"); I("等待时间");
        I(null); int leaveSites = I("需离开地点数量", editable: false); I("离开地点地图");
        B("比较变量（完成）"); I("与 / 或（完成）"); Kv("变量 1（完成）"); Kv("变量 2（完成）");
        int expCnt = I("表达式数量", editable: false); I(null); I(null);
        int taskChars = I("任务文本数量", editable: false); I(null);
        // 43
        G("奖励"); I("成功奖励类型"); I("失败奖励类型"); for (int i = 0; i < 6; i++) I(null);
        G("任务树"); I("父任务"); I("上一个同级任务"); I("下一个同级任务"); I("首个子任务"); G("基本"); B("任务库任务"); F("任务库概率");
        // 45-54: partes variáveis
        G(""); if (hasSign) Skip(60);
        Skip(timetable * 2 * 24);
        Skip(changeKeys * 9);
        Skip(pqExp * (64 + 64 * 8)); Skip(monContrib * 16);
        Skip((delvRegions + enterRegions + leaveRegions) * 24);
        Skip(premItems * 17); Skip(givenItems * 17); Skip(teamMems * 32);
        Skip(monWanted * 30); Skip(itemsWanted * 17);
        Skip(expCnt * (64 + 64 * 8)); Skip(taskChars * 128);
        Skip((reachSites + leaveSites) * 24);
        // 58: prêmios (sucesso, falha) + por razão + por itens
        Award(); Award();
        for (int k = 0; k < 2; k++) { int sc = I(); Skip(AwardScales * 4); for (int i = 0; i < sc; i++) Award(); }
        for (int k = 0; k < 2; k++) { int sc = I(); Skip(4 + AwardScales * 4); for (int i = 0; i < sc; i++) Award(); }
    }

    // AWARD_DATA v93 (campos não registrados: prêmios aninhados ficam preservados como bytes)
    void Award()
    {
        Skip(7 * 4 + 6 * 4);                                  // ouro, exp, nova quest, SP, reputação, período, estação; armazéns/inventário
        Skip(4 + 4 + 12 + 4 + 4 * 1 + 4);                     // fúria, mundo, ponto, ctrl monstros, 4 bools, canal
        int cand = I(); Skip(4); int summoned = I(); Skip(4); Skip(2);
        Skip(4 + 1 + 4 + 4 + 8 * 4);                          // dividendo, habilidade, contribuições
        int ranking = I(); Skip(4); int keys = I(); Skip(12);
        Skip(1 + 4 + 4); int display = I(); Skip(4); int exp = I(); Skip(8); int chars = I(); Skip(4);
        for (int i = 0; i < cand; i++) { Skip(1); int n = I(); Skip(n * 17); }
        if (summoned > 0) Skip(1 + 4 + 1 + summoned * 16);
        if (ranking > 0) Skip(1 + ranking * 21);
        Skip(keys * 9); Skip(display * 4); Skip(exp * (64 + 64 * 8)); Skip(chars * 128);
    }

    // ---------- textos ----------
    string PwStr(int key)
    {
        int n = BitConverter.ToInt32(_d, _p); _p += 4;
        if (n < 0 || _p + n * 2 > _d.Length) throw new InvalidDataException($"文本长度无效（{n}）。");
        _q.NullFlags.Add(n > 0 && (BitConverter.ToUInt16(_d, _p + (n - 1) * 2) ^ unchecked((ushort)key)) == 0);
        var s = Xor(_d, _p, n, key, stopAtNull: false); _p += n * 2; return s;
    }

    TalkProc Talk(int key)
    {
        var t = new TalkProc { IdTalk = BitConverter.ToInt32(_d, _p) }; _p += 4;
        t.Text = t.OriginalText = Xor(_d, _p, 64, key); t.Raw = _d.AsSpan(_p, 128).ToArray(); _p += 128;
        int nw = BitConverter.ToInt32(_d, _p); _p += 4;
        if (nw < 0 || nw > 4096) throw new InvalidDataException($"对话窗口数量无效（{nw}）。");
        for (int i = 0; i < nw; i++)
        {
            var w = new TalkWindow { Id = BitConverter.ToInt32(_d, _p), Parent = BitConverter.ToInt32(_d, _p + 4) }; _p += 8;
            int len = BitConverter.ToInt32(_d, _p); _p += 4;
            if (len < 0 || _p + len * 2 > _d.Length) throw new InvalidDataException($"对话文本长度无效（{len}）。");
            _q.NullFlags.Add(len > 0 && (BitConverter.ToUInt16(_d, _p + (len - 1) * 2) ^ unchecked((ushort)key)) == 0);
            w.Text = Xor(_d, _p, len, key, stopAtNull: false); _p += len * 2;
            int no = BitConverter.ToInt32(_d, _p); _p += 4;
            if (no < 0 || no > 4096) throw new InvalidDataException($"对话选项数量无效（{no}）。");
            for (int j = 0; j < no; j++)
            {
                int param = BitConverter.ToInt32(_d, _p); _p += 4;
                var txt = Xor(_d, _p, 64, key); var raw = _d.AsSpan(_p, 128).ToArray(); _p += 128;
                int id = BitConverter.ToInt32(_d, _p); _p += 4;
                w.Options.Add(new TalkOption { Param = param, Id = id, Text = txt, OriginalText = txt, Raw = raw });
            }
            t.Windows.Add(w);
        }
        return t;
    }

    /// <summary>UTF-16 com XOR (ushort)chave; para no \0 (campos de tamanho fixo).</summary>
    public static string Xor(byte[] d, int p, int chars, int key, bool stopAtNull = true)
    {
        var sb = new StringBuilder(chars);
        ushort k = unchecked((ushort)key);
        for (int i = 0; i < chars; i++)
        {
            char c = (char)(BitConverter.ToUInt16(d, p + i * 2) ^ k);
            if (c == '\0') { if (stopAtNull) break; else continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------- gravação dos textos (só quando mudaram) ----------
    public static byte[] EncodeTexts(QuestNode q)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void Fixed(string s, int chars)
        {
            if (s.Length >= chars) throw new InvalidDataException($"任务 {q.Id} 的对话标题或选项超过 {chars - 1} 个字符。");
            for (int i = 0; i < chars; i++) w.Write((ushort)((i < s.Length ? s[i] : '\0') ^ (ushort)q.Id));
        }
        // texto vazio = tamanho 0; terminador \0 só se o original tinha (NullFlags, na ordem do arquivo)
        int flag = 0;
        void Pw(string s)
        {
            bool nul = flag < q.NullFlags.Count && q.NullFlags[flag]; flag++;
            if (s.Length == 0 && !nul) { w.Write(0); return; }
            w.Write(s.Length + (nul ? 1 : 0));
            foreach (var c in s) w.Write((ushort)(c ^ (ushort)q.Id));
            if (nul) w.Write((ushort)(0 ^ (ushort)q.Id));
        }
        foreach (var t in q.Texts) Pw(t);
        foreach (var tp in q.Talks)
        {
            w.Write(tp.IdTalk);
            if (tp.Text == tp.OriginalText && tp.Raw.Length == 128) w.Write(tp.Raw); else Fixed(tp.Text, 64);
            w.Write(tp.Windows.Count);
            foreach (var win in tp.Windows)
            {
                w.Write(win.Id); w.Write(win.Parent); Pw(win.Text); w.Write(win.Options.Count);
                foreach (var o in win.Options)
                {
                    w.Write(o.Param);
                    if (o.Text == o.OriginalText && o.Raw.Length == 128) w.Write(o.Raw); else Fixed(o.Text, 64);
                    w.Write(o.Id);
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>Serializa a quest (e subquests) — bloco fixo + textos (originais se não mudaram) + filhos.</summary>
    public static void Write(BinaryWriter w, QuestNode q)
    {
        foreach (var f in q.Fields.Where(f => !f.Editable))
        {
            int current = f.Type == TF.I32 ? BitConverter.ToInt32(q.Fixed, f.Offset) : q.Fixed[f.Offset];
            if (current != f.PreservedValue)
                throw new InvalidDataException($"任务 {q.Id} 的结构字段“{f.Name}”不能直接修改。");
        }
        if (q.Fixed.Length >= 4 && BitConverter.ToInt32(q.Fixed, 0) != q.Id)
            throw new InvalidDataException($"任务 {q.Id} 的 ID 与原始结构不一致，不能直接修改。");
        var fixedBytes = (byte[])q.Fixed.Clone();
        if (q.Name != q.OriginalName && q.Name.Length > TasksFile.MaxNameLength)
            throw new InvalidDataException($"任务 {q.Id} 的名称超过 {TasksFile.MaxNameLength} 个字符。");
        if (q.Name != q.OriginalName)
            for (int i = 0; i < 30; i++)
                BitConverter.GetBytes((ushort)((i < q.Name.Length && i < 29 ? q.Name[i] : '\0') ^ (ushort)q.Id)).CopyTo(fixedBytes, 4 + i * 2);
        w.Write(fixedBytes);
        w.Write(q.TextsChanged ? EncodeTexts(q) : q.TextRaw);
        w.Write(q.Children.Count);
        foreach (var c in q.Children) Write(w, c);
    }
}
