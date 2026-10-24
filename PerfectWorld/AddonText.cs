using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.Model;
using MyAceEditor.UI;

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

    // Legenda no idioma atual: chave addon.t<tipo> (e addon.t<tipo>b para a 2ª legenda dos pares);
    // o texto em português da tabela abaixo é o padrão e o que aparece se faltar tradução.
    static string CaptionOf(int type, TypeInfo ti) => Loc.T("addon.t" + type, ti.Caption);
    static string Caption2Of(int type, TypeInfo ti) => Loc.T("addon.t" + type + "b", ti.Caption2 ?? "");

    const string M = "Metal", W = "Madeira", A = "Água", F = "Fogo", T = "Terra";

    public static readonly Dictionary<int, TypeInfo> Types = new()
    {
        [0] = new("Ataque físico", AddonMode.Val), [1] = new("Ataque físico máx.", AddonMode.Range), [2] = new("Ataque físico", AddonMode.Pct),
        [3] = new("Ataque mágico", AddonMode.Val), [4] = new("Ataque mágico máx.", AddonMode.Range), [5] = new("Ataque mágico", AddonMode.Pct),
        [6] = new("Defesa física", AddonMode.Pair, "Ataque físico"), [7] = new("Ataque físico", AddonMode.Pair, "Defesa física"),
        [8] = new("Ataque mágico", AddonMode.Pair, "Defesa mágica"), [9] = new("Velocidade de ataque", AddonMode.Val),
        [10] = new("Alcance de ataque", AddonMode.Float), [11] = new("Tempo de conjuração", AddonMode.PctNeg),
        [12] = new("Defesa física", AddonMode.Val), [13] = new("Defesa física", AddonMode.Pct), [14] = new("Defesa mágica (todas)", AddonMode.Val),
        [15] = new($"Defesa de {M}", AddonMode.Range), [16] = new($"Defesa de {M}", AddonMode.Pct),
        [17] = new($"Defesa de {W}", AddonMode.Range), [18] = new($"Defesa de {W}", AddonMode.Pct),
        [19] = new($"Defesa de {A}", AddonMode.Range), [20] = new($"Defesa de {A}", AddonMode.Pct),
        [21] = new($"Defesa de {F}", AddonMode.Range), [22] = new($"Defesa de {F}", AddonMode.Pct),
        [23] = new($"Defesa de {T}", AddonMode.Range), [24] = new($"Defesa de {T}", AddonMode.Pct),
        [25] = new($"Defesa de {M}", AddonMode.PairPct, $"Defesa de {F}"), [26] = new($"Defesa de {W}", AddonMode.PairPct, $"Defesa de {M}"),
        [27] = new($"Defesa de {A}", AddonMode.PairPct, $"Defesa de {T}"), [28] = new($"Defesa de {F}", AddonMode.PairPct, $"Defesa de {A}"),
        [29] = new($"Defesa de {T}", AddonMode.PairPct, $"Defesa de {W}"),
        [30] = new($"Defesa de {M}", AddonMode.Pair, $"Defesa de {F}"), [31] = new($"Defesa de {W}", AddonMode.Pair, $"Defesa de {M}"),
        [32] = new($"Defesa de {A}", AddonMode.Pair, $"Defesa de {T}"), [33] = new($"Defesa de {F}", AddonMode.Pair, $"Defesa de {A}"),
        [34] = new($"Defesa de {T}", AddonMode.Pair, $"Defesa de {W}"),
        [35] = new("HP", AddonMode.Val), [36] = new("MP", AddonMode.Val), [37] = new("HP", AddonMode.Pct), [38] = new("MP", AddonMode.Pct),
        [39] = new("Recuperação de HP", AddonMode.Val), [40] = new("Recuperação de MP", AddonMode.Val),
        [41] = new("Força", AddonMode.Range), [42] = new("Destreza", AddonMode.Range), [43] = new("Inteligência", AddonMode.Range), [44] = new("Constituição", AddonMode.Range),
        [45] = new("Golpe crítico", AddonMode.Pct), [46] = new("Precisão", AddonMode.Range), [47] = new("Precisão", AddonMode.Pct),
        [48] = new("Velocidade de movimento", AddonMode.Float), [49] = new("Velocidade de movimento", AddonMode.Pct),
        [50] = new("Esquiva", AddonMode.Val), [51] = new("Esquiva", AddonMode.Pct), [52] = new("Durabilidade", AddonMode.Val), [53] = new("Durabilidade", AddonMode.Pct),
        [54] = new("Redução de dano físico", AddonMode.Pct), [55] = new("Habilidade adicional", AddonMode.Label),
        [56] = new("Requisitos do equipamento", AddonMode.PctRange), [57] = new("Atributo aleatório", AddonMode.Label),
        [58] = new("Bônus de experiência", AddonMode.Pct), [59] = new("Nível de ataque", AddonMode.Val), [60] = new("Nível de defesa", AddonMode.Val),
        [61] = new("Defesa mágica (todas)", AddonMode.Pct), [62] = new("Olho da Selva", AddonMode.Label), [63] = new("Poder da alma", AddonMode.Val),
        [64] = new($"Redução de dano de {M}", AddonMode.Pct), [65] = new($"Redução de dano de {W}", AddonMode.Pct), [66] = new($"Redução de dano de {A}", AddonMode.Pct),
        [67] = new($"Redução de dano de {F}", AddonMode.Pct), [68] = new($"Redução de dano de {T}", AddonMode.Pct), [69] = new("Redução de dano elemental", AddonMode.Pct),
        [70] = new("Nível de ataque (aleatório)", AddonMode.Range), [71] = new("Nível de defesa (aleatório)", AddonMode.Range),
        [72] = new("Golpe crítico (aleatório)", AddonMode.PctRange), [73] = new("HP (aleatório)", AddonMode.Range), [74] = new("MP (aleatório)", AddonMode.Range),
        [75] = new("Precisão (aleatório)", AddonMode.PctRange), [76] = new("Defesa física (aleatório)", AddonMode.Range), [77] = new("Defesa mágica (aleatório)", AddonMode.Range),
        [78] = new("Redução de dano físico (aleatório)", AddonMode.PctRange), [79] = new("Redução de dano elemental (aleatório)", AddonMode.PctRange),
        [80] = new("Tempo de conjuração (aleatório)", AddonMode.PctRange), [81] = new("Alcance de ataque (aleatório)", AddonMode.Range),
        [82] = new("Recuperação de MP (aleatório)", AddonMode.Range), [83] = new("Defesa física % (aleatório)", AddonMode.PctRange),
        [84] = new("Defesa mágica % (aleatório)", AddonMode.PctRange), [85] = new("Recuperação de HP (aleatório)", AddonMode.Range),
        [86] = new("Esquiva (aleatório)", AddonMode.Range), [87] = new("Ataque físico máx. (aleatório)", AddonMode.Range), [88] = new("Ataque mágico máx. (aleatório)", AddonMode.Range),
        [100] = new("Pedra de afiar: ataque físico", AddonMode.Val), [101] = new("Pedra de afiar: ataque físico máx.", AddonMode.Val),
        [102] = new("Pedra de afiar: ataque mágico", AddonMode.Val), [103] = new("Pedra de afiar: ataque mágico máx.", AddonMode.Val),
        [104] = new("Pedra de afiar: defesa física", AddonMode.Val), [105] = new("Pedra de afiar: HP", AddonMode.Val),
        [106] = new("Pedra de afiar: força", AddonMode.Val), [107] = new("Pedra de afiar: destreza", AddonMode.Val), [108] = new("Pedra de afiar: inteligência", AddonMode.Val),
        [109] = new("Pedra de afiar: precisão", AddonMode.Val), [110] = new("Pedra de afiar: golpe crítico", AddonMode.Pct),
        [111] = new("Pedra de afiar: nível de ataque", AddonMode.Val), [112] = new("Pedra de afiar: nível de defesa", AddonMode.Val),
        [113] = new("Pedra de afiar: tempo de conjuração", AddonMode.PctNeg),
        [120] = new($"Gravação: defesa de {M}", AddonMode.Val), [121] = new($"Gravação: defesa de {W}", AddonMode.Val), [122] = new($"Gravação: defesa de {A}", AddonMode.Val),
        [123] = new($"Gravação: defesa de {F}", AddonMode.Val), [124] = new($"Gravação: defesa de {T}", AddonMode.Val), [125] = new("Gravação: precisão", AddonMode.Val),
        [126] = new("Gravação: esquiva", AddonMode.Val), [127] = new("Gravação: chi", AddonMode.Val), [128] = new("Gravação: constituição", AddonMode.Range),
        [129] = new("Gravação: força", AddonMode.Range), [130] = new("Gravação: destreza", AddonMode.Range), [131] = new("Gravação: inteligência", AddonMode.Range),
        [132] = new("Gravação: HP", AddonMode.Val), [133] = new("Gravação: defesa física", AddonMode.Val), [134] = new("Gravação: defesa mágica", AddonMode.Val),
        [135] = new("Gravação: ataque físico", AddonMode.Val), [136] = new("Gravação: ataque mágico", AddonMode.Val),
        [137] = new($"Gravação: redução de dano de {M}", AddonMode.Pct), [138] = new($"Gravação: redução de dano de {W}", AddonMode.Pct),
        [139] = new($"Gravação: redução de dano de {A}", AddonMode.Pct), [140] = new($"Gravação: redução de dano de {F}", AddonMode.Pct),
        [141] = new($"Gravação: redução de dano de {T}", AddonMode.Pct), [142] = new("Gravação: redução de dano elemental", AddonMode.Pct),
        [143] = new("Gravação: golpe crítico", AddonMode.Pct), [144] = new("Gravação: nível de ataque", AddonMode.Val),
        [145] = new("Gravação: nível de defesa", AddonMode.Val), [146] = new("Gravação: redução de dano físico", AddonMode.Pct),
        [150] = new("Golpe crítico +1%", AddonMode.Label), [151] = new("Golpe crítico +2%", AddonMode.Label), [152] = new("Golpe crítico +3%", AddonMode.Label),
        [153] = new("Golpe crítico +4%", AddonMode.Label), [154] = new("Golpe crítico +5%", AddonMode.Label), [155] = new("Golpe crítico +6%", AddonMode.Label),
        [156] = new("Poder da alma (conjunto)", AddonMode.Val),
        [200] = new("Refino: ataque físico", AddonMode.Val), [201] = new("Refino: ataque mágico", AddonMode.Val), [202] = new("Refino: defesa física", AddonMode.Val),
        [203] = new($"Refino: defesa de {M}", AddonMode.Val), [204] = new($"Refino: defesa de {W}", AddonMode.Val), [205] = new($"Refino: defesa de {A}", AddonMode.Val),
        [206] = new($"Refino: defesa de {F}", AddonMode.Val), [207] = new($"Refino: defesa de {T}", AddonMode.Val), [208] = new("Refino: HP", AddonMode.Val),
        [209] = new("Refino: esquiva", AddonMode.Val), [210] = new("Refino: defesa elemental", AddonMode.Val), [211] = new("Refino: ataque físico e mágico", AddonMode.Val),
        [212] = new("Refino: defesa física e mágica", AddonMode.Val),
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
                foreach (Match id in Regex.Matches(m.Groups[2].Value, @"\d+")) at._types.TryAdd(int.Parse(id.Value), type);
            }
        }
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
        string cap = CaptionOf(type, ti);
        // alguns addons de tipo inteiro guardam float em bits (ex.: velocidade de ataque 1036831949 = 0.1):
        // número gigante que vira um float pequeno e "redondo" é mostrado como float
        static string S(int v)
        {
            if (Math.Abs((long)v) > 10_000_000 && BitConverter.Int32BitsToSingle(v) is var f && float.IsFinite(f) && Math.Abs(f) is > 0.0001f and < 100_000f)
                return (f >= 0 ? "+" : "") + f.ToString("0.###", CultureInfo.InvariantCulture);
            return v >= 0 ? $"+{v}" : v.ToString();
        }
        static int Pc(int bits) => (int)MathF.Round(BitConverter.Int32BitsToSingle(bits) * 100f);
        static string Fl(int bits) { var f = BitConverter.Int32BitsToSingle(bits); return (f >= 0 ? "+" : "") + f.ToString("0.##", CultureInfo.InvariantCulture); }
        return ti.Mode switch
        {
            AddonMode.Val => $"{cap} {S(p0)}",
            AddonMode.Range => p1 == 0 || p0 == p1 ? $"{cap} {S(p0)}" : $"{cap} {S(p0)}~{p1}",
            AddonMode.Pct => $"{cap} {S(Pc(p0))}%",
            AddonMode.PctRange => p1 == 0 || Pc(p0) == Pc(p1) ? $"{cap} {S(Pc(p0))}%" : $"{cap} {S(Pc(p0))}%~{Pc(p1)}%",
            AddonMode.PctNeg => $"{cap} -{Math.Abs(Pc(p0))}%",
            AddonMode.Float => $"{cap} {Fl(p0)}",
            AddonMode.Pair => $"{cap} {S(p0)}, {Caption2Of(type, ti)} {S(p1)}",
            AddonMode.PairPct => $"{cap} {S(Pc(p0))}%, {Caption2Of(type, ti)} {S(Pc(p1))}%",
            _ => p0 != 0 && type is 55 ? $"{cap} (ID {p0})" : cap,
        };
    }

    /// <summary>Legenda do tipo (para agrupar no seletor de addons).</summary>
    public string? TypeCaption(string id) => TypeOf(id) is { } t && Types.TryGetValue(t, out var ti) ? CaptionOf(t, ti) + (ti.Mode is AddonMode.Pct or AddonMode.PairPct ? " (%)" : "") : null;
}
