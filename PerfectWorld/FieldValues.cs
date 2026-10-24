using MyAceEditor.UI;
using System.Globalization;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Significado de valores de campos que são um código (0, 1, 2...) e não uma quantidade, mostrado na coluna
/// "Vínculo" da grade, como o RAE/sELedit fazem ("Melee (assassin)"). Fonte: guias da comunidade (PW-KB §S29).
/// </summary>
public static class FieldValues
{
    static readonly Dictionary<string, (int Value, string Key, string Pt)[]> Codes = new()
    {
        ["id_strategy"] = new[]
        {
            (0, "val.fight0", "Corpo a corpo"), (1, "val.fight1", "À distância, parado"), (2, "val.fight2", "À distância, recua"),
            (3, "val.fight3", "Combo"), (4, "val.fight4", "Não se move"),
        },
        ["aggressive_mode"] = new[] { (0, "val.passive", "Passivo (só ataca se for atacado)"), (1, "val.aggressive", "Agressivo (ataca quem se aproxima)") },
        ["inhabit_type"] = new[] { (0, "val.land", "Terra"), (1, "val.water", "Água"), (2, "val.air", "Ar") },
    };

    /// <summary>Texto do valor, ou null quando o campo não é um código conhecido.</summary>
    public static string? Text(string block, string field, string value)
    {
        if (block != "MONSTER_ESSENCE" && block != "") return null;
        if (field == "attack_speed" && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0)
            return string.Format(Loc.T("val.attackSpeed", "1 golpe a cada {0} s"), s.ToString("0.##", CultureInfo.CurrentCulture));
        if (!Codes.TryGetValue(field, out var table) || !int.TryParse(value, out var v)) return null;
        foreach (var (val, key, pt) in table) if (val == v) return Loc.T(key, pt);
        return null;
    }
}
