using System.Globalization;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Teto e piso de cada campo do elements: o do tipo gravado (int16, uint8, texto com N bytes) e o das regras do jogo
/// (gs elementdataman / docs/REGRAS-DO-JOGO.md). Valor digitado fora vira o limite, com o motivo para a barra de status.
/// </summary>
public static class FieldLimits
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // regras do jogo (bloco, campo, mínimo, máximo, motivo); a do tipo vem depois e vale sempre
    static readonly (string Block, string Field, double Min, double Max, string Key, string Why)[] Game =
    {
        ("MONSTER_ESSENCE", "level", 1, 150, "cap.mobLevel", "monstro acima de 150 fica invisível no jogo"),
        ("MONSTER_ESSENCE", "drop_times", 0, 10, "cap.dropTimes", "o gs usa no máximo 10 sorteios de drop"),
        ("STONE_ESSENCE", "level", 1, 20, "cap.stoneLevel", "pedra só de 1 a 20: o gs cai ao usar"),
        ("MONSTER_ADDON", "param1", 0, 9, "cap.mobAddon", "o gs cai ao iniciar com param1 acima de 9"),
    };

    /// <summary>Devolve o valor dentro dos limites (ou o mesmo) e o motivo quando mudou.</summary>
    public static string Clamp(string block, CfgField? field, string key, string value, out string? why)
    {
        why = null;
        foreach (var g in Game)
            if (g.Block == block && g.Field.Equals(key, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, Inv, out var d) && (d < g.Min || d > g.Max))
            {
                value = (d < g.Min ? g.Min : g.Max).ToString(Inv);
                why = UI.Loc.T(g.Key, g.Why);
            }
        // chance: sempre 0 a 1 (o gs divide pela soma, mas acima de 1 o editor mostraria mais de 100%)
        if (key.Contains("probab", StringComparison.OrdinalIgnoreCase) && field?.TypeKind is CfgField.Kind.Float
            && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, Inv, out var p) && (p < 0 || p > 1))
        {
            value = p < 0 ? "0" : "1";
            why = UI.Loc.T("cap.probability", "chance vai de 0 a 1 (100%)");
        }
        if (field is null) return value;
        var (min, max) = field.TypeKind switch
        {
            CfgField.Kind.I8 => (sbyte.MinValue, sbyte.MaxValue),
            CfgField.Kind.U8 => (0, byte.MaxValue),
            CfgField.Kind.I16 => (short.MinValue, short.MaxValue),
            CfgField.Kind.U16 => (0, ushort.MaxValue),
            CfgField.Kind.I32 => (int.MinValue, (double)uint.MaxValue),   // o gravador aceita uint num int32 (IDs altos)
            CfgField.Kind.U32 => (0, uint.MaxValue),
            _ => (double.NaN, double.NaN),
        };
        if (!double.IsNaN(min) && double.TryParse(value, NumberStyles.Float, Inv, out var n) && (n < min || n > max || n != Math.Floor(n)))
        {
            value = Math.Clamp(Math.Floor(n), min, max).ToString(Inv);
            why ??= string.Format(UI.Loc.T("cap.type", "este campo vai de {0} a {1}"), min.ToString(Inv), max.ToString(Inv));
        }
        if (field.TypeKind is CfgField.Kind.WString or CfgField.Kind.String)
        {
            bool uni = field.TypeKind == CfgField.Kind.WString;
            int limit = field.Size - (uni ? 2 : 1);
            int Bytes(string s) => uni ? Encoding.Unicode.GetByteCount(s) : AiPolicyFile.Gbk.GetByteCount(s);
            if (Bytes(value) > limit)
            {
                var sb = new StringBuilder(value);
                while (sb.Length > 0 && Bytes(sb.ToString()) > limit)
                    sb.Length -= sb.Length >= 2 && char.IsLowSurrogate(sb[^1]) ? 2 : 1;   // não corta par substituto ao meio
                value = sb.ToString();
                why = string.Format(UI.Loc.T("cap.text", "texto cortado: cabem {0} bytes"), limit);
            }
        }
        return value;
    }
}
