using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>Como os parâmetros de um tipo de addon viram texto.</summary>
public enum AddonMode
{
    Val,       // +p0
    Range,     // +p0~p1 (ou +p0 se iguais)
    Pct,       // p0 é float 0..1 em bits → +x%
    PctRange,  // +x%~y%
    PctNeg,    // -x% (tempo de conjuração)
    Float,     // p0 é float em bits (velocidade, alcance)
    Pair,      // A +p0, B +p1
    PairPct,   // A +x%, B +y%
    Label,     // só a legenda
}

/// <summary>
/// Descrição dos addons (atributos) em português (ENGINE-KB §E3e):
///  - tipo de cada ID de addon: configs\item_ext_prop.txt ("tipo { id id … }"), do próprio cliente;
///  - legenda/formato por tipo: tabela nossa, a partir da lista oficial de tipos que vem no
///    comentário do item_ext_prop.txt (0-88, 100-113 pedras de afiar, 120-146 gravações, 150-156, 200-212 refino).
/// </summary>
public sealed class AddonText
{
    public sealed record TypeInfo(string Caption, AddonMode Mode, string? Caption2 = null);

    const string M = "金", W = "木", A = "水", F = "火", T = "土";

    public static readonly Dictionary<int, TypeInfo> Types = new()
    {
        [0] = new("物理攻击", AddonMode.Val), [1] = new("最大物理攻击", AddonMode.Range), [2] = new("物理攻击", AddonMode.Pct),
        [3] = new("法术攻击", AddonMode.Val), [4] = new("最大法术攻击", AddonMode.Range), [5] = new("法术攻击", AddonMode.Pct),
        [6] = new("物理防御", AddonMode.Pair, "物理攻击"), [7] = new("物理攻击", AddonMode.Pair, "物理防御"),
        [8] = new("法术攻击", AddonMode.Pair, "法术防御"), [9] = new("攻击速度", AddonMode.Val),
        [10] = new("攻击距离", AddonMode.Float), [11] = new("吟唱时间", AddonMode.PctNeg),
        [12] = new("物理防御", AddonMode.Val), [13] = new("物理防御", AddonMode.Pct), [14] = new("全部法术防御", AddonMode.Val),
        [15] = new($"防御：{M}", AddonMode.Range), [16] = new($"防御：{M}", AddonMode.Pct),
        [17] = new($"防御：{W}", AddonMode.Range), [18] = new($"防御：{W}", AddonMode.Pct),
        [19] = new($"防御：{A}", AddonMode.Range), [20] = new($"防御：{A}", AddonMode.Pct),
        [21] = new($"防御：{F}", AddonMode.Range), [22] = new($"防御：{F}", AddonMode.Pct),
        [23] = new($"防御：{T}", AddonMode.Range), [24] = new($"防御：{T}", AddonMode.Pct),
        [25] = new($"防御：{M}", AddonMode.PairPct, $"防御：{F}"), [26] = new($"防御：{W}", AddonMode.PairPct, $"防御：{M}"),
        [27] = new($"防御：{A}", AddonMode.PairPct, $"防御：{T}"), [28] = new($"防御：{F}", AddonMode.PairPct, $"防御：{A}"),
        [29] = new($"防御：{T}", AddonMode.PairPct, $"防御：{W}"),
        [30] = new($"防御：{M}", AddonMode.Pair, $"防御：{F}"), [31] = new($"防御：{W}", AddonMode.Pair, $"防御：{M}"),
        [32] = new($"防御：{A}", AddonMode.Pair, $"防御：{T}"), [33] = new($"防御：{F}", AddonMode.Pair, $"防御：{A}"),
        [34] = new($"防御：{T}", AddonMode.Pair, $"防御：{W}"),
        [35] = new("生命", AddonMode.Val), [36] = new("真气", AddonMode.Val), [37] = new("生命", AddonMode.Pct), [38] = new("真气", AddonMode.Pct),
        [39] = new("生命恢复", AddonMode.Val), [40] = new("真气恢复", AddonMode.Val),
        [41] = new("力量", AddonMode.Range), [42] = new("敏捷", AddonMode.Range), [43] = new("灵力", AddonMode.Range), [44] = new("体质", AddonMode.Range),
        [45] = new("暴击率", AddonMode.Pct), [46] = new("命中", AddonMode.Range), [47] = new("命中", AddonMode.Pct),
        [48] = new("移动速度", AddonMode.Float), [49] = new("移动速度", AddonMode.Pct),
        [50] = new("闪避", AddonMode.Val), [51] = new("闪避", AddonMode.Pct), [52] = new("耐久度", AddonMode.Val), [53] = new("耐久度", AddonMode.Pct),
        [54] = new("物理伤害减免", AddonMode.Pct), [55] = new("附加技能", AddonMode.Label),
        [56] = new("装备需求", AddonMode.PctRange), [57] = new("随机属性", AddonMode.Label),
        [58] = new("经验加成", AddonMode.Pct), [59] = new("攻击等级", AddonMode.Val), [60] = new("防御等级", AddonMode.Val),
        [61] = new("全部法术防御", AddonMode.Pct), [62] = new("丛林之眼", AddonMode.Label), [63] = new("魂力", AddonMode.Val),
        [64] = new($"伤害减免：{M}", AddonMode.Pct), [65] = new($"伤害减免：{W}", AddonMode.Pct), [66] = new($"伤害减免：{A}", AddonMode.Pct),
        [67] = new($"伤害减免：{F}", AddonMode.Pct), [68] = new($"伤害减免：{T}", AddonMode.Pct), [69] = new("五行伤害减免", AddonMode.Pct),
        [70] = new("攻击等级（随机）", AddonMode.Range), [71] = new("防御等级（随机）", AddonMode.Range),
        [72] = new("暴击率（随机）", AddonMode.PctRange), [73] = new("生命（随机）", AddonMode.Range), [74] = new("真气（随机）", AddonMode.Range),
        [75] = new("命中（随机）", AddonMode.PctRange), [76] = new("物理防御（随机）", AddonMode.Range), [77] = new("法术防御（随机）", AddonMode.Range),
        [78] = new("物理伤害减免（随机）", AddonMode.PctRange), [79] = new("五行伤害减免（随机）", AddonMode.PctRange),
        [80] = new("吟唱时间（随机）", AddonMode.PctRange), [81] = new("攻击距离（随机）", AddonMode.Range),
        [82] = new("真气恢复（随机）", AddonMode.Range), [83] = new("物理防御 %（随机）", AddonMode.PctRange),
        [84] = new("法术防御 %（随机）", AddonMode.PctRange), [85] = new("生命恢复（随机）", AddonMode.Range),
        [86] = new("闪避（随机）", AddonMode.Range), [87] = new("最大物理攻击（随机）", AddonMode.Range), [88] = new("最大法术攻击（随机）", AddonMode.Range),
        [100] = new("磨刀石：物理攻击", AddonMode.Val), [101] = new("磨刀石：最大物理攻击", AddonMode.Val),
        [102] = new("磨刀石：法术攻击", AddonMode.Val), [103] = new("磨刀石：最大法术攻击", AddonMode.Val),
        [104] = new("磨刀石：物理防御", AddonMode.Val), [105] = new("磨刀石：生命", AddonMode.Val),
        [106] = new("磨刀石：力量", AddonMode.Val), [107] = new("磨刀石：敏捷", AddonMode.Val), [108] = new("磨刀石：灵力", AddonMode.Val),
        [109] = new("磨刀石：命中", AddonMode.Val), [110] = new("磨刀石：暴击率", AddonMode.Pct),
        [111] = new("磨刀石：攻击等级", AddonMode.Val), [112] = new("磨刀石：防御等级", AddonMode.Val),
        [113] = new("磨刀石：吟唱时间", AddonMode.PctNeg),
        [120] = new($"铭刻：防御：{M}", AddonMode.Val), [121] = new($"铭刻：防御：{W}", AddonMode.Val), [122] = new($"铭刻：防御：{A}", AddonMode.Val),
        [123] = new($"铭刻：防御：{F}", AddonMode.Val), [124] = new($"铭刻：防御：{T}", AddonMode.Val), [125] = new("铭刻：命中", AddonMode.Val),
        [126] = new("铭刻：闪避", AddonMode.Val), [127] = new("铭刻：元气", AddonMode.Val), [128] = new("铭刻：体质", AddonMode.Range),
        [129] = new("铭刻：力量", AddonMode.Range), [130] = new("铭刻：敏捷", AddonMode.Range), [131] = new("铭刻：灵力", AddonMode.Range),
        [132] = new("铭刻：生命", AddonMode.Val), [133] = new("铭刻：物理防御", AddonMode.Val), [134] = new("铭刻：法术防御", AddonMode.Val),
        [135] = new("铭刻：物理攻击", AddonMode.Val), [136] = new("铭刻：法术攻击", AddonMode.Val),
        [137] = new($"铭刻：伤害减免：{M}", AddonMode.Pct), [138] = new($"铭刻：伤害减免：{W}", AddonMode.Pct),
        [139] = new($"铭刻：伤害减免：{A}", AddonMode.Pct), [140] = new($"铭刻：伤害减免：{F}", AddonMode.Pct),
        [141] = new($"铭刻：伤害减免：{T}", AddonMode.Pct), [142] = new("铭刻：五行伤害减免", AddonMode.Pct),
        [143] = new("铭刻：暴击率", AddonMode.Pct), [144] = new("铭刻：攻击等级", AddonMode.Val),
        [145] = new("铭刻：防御等级", AddonMode.Val), [146] = new("铭刻：物理伤害减免", AddonMode.Pct),
        [150] = new("暴击率 +1%", AddonMode.Label), [151] = new("暴击率 +2%", AddonMode.Label), [152] = new("暴击率 +3%", AddonMode.Label),
        [153] = new("暴击率 +4%", AddonMode.Label), [154] = new("暴击率 +5%", AddonMode.Label), [155] = new("暴击率 +6%", AddonMode.Label),
        [156] = new("魂力（套装）", AddonMode.Val),
        [200] = new("精炼：物理攻击", AddonMode.Val), [201] = new("精炼：法术攻击", AddonMode.Val), [202] = new("精炼：物理防御", AddonMode.Val),
        [203] = new($"精炼：防御：{M}", AddonMode.Val), [204] = new($"精炼：防御：{W}", AddonMode.Val), [205] = new($"精炼：防御：{A}", AddonMode.Val),
        [206] = new($"精炼：防御：{F}", AddonMode.Val), [207] = new($"精炼：防御：{T}", AddonMode.Val), [208] = new("精炼：生命", AddonMode.Val),
        [209] = new("精炼：闪避", AddonMode.Val), [210] = new("精炼：五行防御", AddonMode.Val), [211] = new("精炼：物理与法术攻击", AddonMode.Val),
        [212] = new("精炼：物理与法术防御", AddonMode.Val),
    };

    readonly Dictionary<int, byte> _types = new();
    public int Count => _types.Count;

    public static AddonText? Load(PckArchive configs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var at = new AddonText();
        if (configs.Find(@"configs\item_ext_prop.txt") is { } p)
        {
            var txt = Regex.Replace(Decode(configs.Read(p)), @"/\*.*?\*/", " ", RegexOptions.Singleline);
            txt = Regex.Replace(txt, @"//[^\n]*", " ");
            foreach (Match m in Regex.Matches(txt, @"(\d+)\s*\{([^}]*)\}"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte type = (byte)int.Parse(m.Groups[1].Value);
                foreach (Match id in Regex.Matches(m.Groups[2].Value, @"\d+"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    at._types.TryAdd(int.Parse(id.Value), type);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return at._types.Count > 0 ? at : null;
    }

    static string Decode(byte[] b) =>
        b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2) : Encoding.GetEncoding(936).GetString(b);

    public int? TypeOf(string id) => int.TryParse(id, out var i) && _types.TryGetValue(i, out var t) ? t : null;

    /// <summary>Texto do addon (ex.: "Ataque físico +12", "Força +3~5", "Golpe crítico +2%"); null se o tipo for desconhecido.</summary>
    public string? Describe(Item addon)
    {
        if (TypeOf(addon.Id) is not { } type || !Types.TryGetValue(type, out var ti)) return null;
        int P(string k) => int.TryParse(addon.Fields.GetValueOrDefault(k)?.Value, out var v) ? v : 0;
        int p0 = P("param1"), p1 = P("param2");
        static string S(int v) => v >= 0 ? $"+{v}" : v.ToString();
        static int Pc(int bits) => (int)MathF.Round(BitConverter.Int32BitsToSingle(bits) * 100f);
        static string Fl(int bits) { var f = BitConverter.Int32BitsToSingle(bits); return (f >= 0 ? "+" : "") + f.ToString("0.##", CultureInfo.InvariantCulture); }
        return ti.Mode switch
        {
            AddonMode.Val => $"{ti.Caption} {S(p0)}",
            AddonMode.Range => p1 == 0 || p0 == p1 ? $"{ti.Caption} {S(p0)}" : $"{ti.Caption} {S(p0)}~{p1}",
            AddonMode.Pct => $"{ti.Caption} {S(Pc(p0))}%",
            AddonMode.PctRange => p1 == 0 || Pc(p0) == Pc(p1) ? $"{ti.Caption} {S(Pc(p0))}%" : $"{ti.Caption} {S(Pc(p0))}%~{Pc(p1)}%",
            AddonMode.PctNeg => $"{ti.Caption} -{Math.Abs(Pc(p0))}%",
            AddonMode.Float => $"{ti.Caption} {Fl(p0)}",
            AddonMode.Pair => $"{ti.Caption} {S(p0)}, {ti.Caption2} {S(p1)}",
            AddonMode.PairPct => $"{ti.Caption} {S(Pc(p0))}%, {ti.Caption2} {S(Pc(p1))}%",
            _ => p0 != 0 && type is 55 ? $"{ti.Caption} (ID {p0})" : ti.Caption,
        };
    }

    /// <summary>Legenda do tipo (para agrupar no seletor de addons).</summary>
    public string? TypeCaption(string id) => TypeOf(id) is { } t && Types.TryGetValue(t, out var ti) ? ti.Caption + (ti.Mode is AddonMode.Pct or AddonMode.PairPct ? " (%)" : "") : null;
}
