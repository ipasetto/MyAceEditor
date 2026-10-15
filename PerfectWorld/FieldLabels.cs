namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Traduz os nomes técnicos dos campos do elements.data (do .cfg) para rótulos
/// amigáveis em português e agrupa em seções, para a UI ficar legível.
/// </summary>
public static class FieldLabels
{
    // Rótulos curados dos campos mais importantes.
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ID"] = "ID", ["Name"] = "Nome", ["realname"] = "Nome real",
        ["id_major_type"] = "Tipo principal", ["id_sub_type"] = "Subtipo",
        ["require_strength"] = "Requer Força", ["require_agility"] = "Requer Destreza",
        ["require_energy"] = "Requer Magia", ["require_tili"] = "Requer Constituição",
        ["require_level"] = "Requer Nível", ["require_reputation"] = "Requer Reputação",
        ["level"] = "Nível", ["character_combo_id"] = "Classe",
        ["damage_low"] = "Ataque físico (mín)", ["damage_high_min"] = "Ataque físico (máx mín)",
        ["damage_high_max"] = "Ataque físico (máx)", ["magic_damage_low"] = "Ataque mágico (mín)",
        ["magic_damage_high_min"] = "Ataque mágico (máx mín)", ["magic_damage_high_max"] = "Ataque mágico (máx)",
        ["attack_range"] = "Alcance", ["attack_speed"] = "Velocidade de ataque",
        ["defence_low"] = "Defesa física (mín)", ["defence_high"] = "Defesa física (máx)",
        ["armor_enhance_low"] = "Armadura (mín)", ["armor_enhance_high"] = "Armadura (máx)",
        ["hp_enhance_low"] = "HP extra (mín)", ["hp_enhance_high"] = "HP extra (máx)",
        ["mp_enhance_low"] = "MP extra (mín)", ["mp_enhance_high"] = "MP extra (máx)",
        ["durability_min"] = "Durabilidade (mín)", ["durability_max"] = "Durabilidade (máx)",
        ["price"] = "Preço", ["shop_price"] = "Preço na loja", ["repairfee"] = "Custo de reparo",
        ["material_need"] = "Material necessário", ["pile_num_max"] = "Empilhar (máx)",
        ["element_id"] = "Elemento", ["element_num"] = "Qtd. elemento",
        ["equip_location"] = "Local do equipamento", ["equip_mask"] = "Máscara de equipamento",
        ["file_model"] = "Modelo", ["file_model_right"] = "Modelo (direita)", ["file_model_left"] = "Modelo (esquerda)",
        ["file_matter"] = "Textura", ["file_icon"] = "Ícone", ["gender_id"] = "Gênero",
    };

    public static string Label(string field)
    {
        if (Map.TryGetValue(field, out var l)) return l;
        // addons/rands/uniques: "addons_3_id_addon" -> "Atributo 3 (id)" / "..._probability..." -> "Atributo 3 (chance)"
        var m = System.Text.RegularExpressions.Regex.Match(field, @"^(addons|rands|uniques)_(\d+)_(id|probability)");
        if (m.Success)
        {
            string kind = m.Groups[1].Value switch { "addons" => "Addon", "rands" => "Aleatório", _ => "Único" };
            string what = m.Groups[3].Value == "id" ? "id" : "chance";
            return $"{kind} {m.Groups[2].Value} ({what})";
        }
        return Prettify(field);
    }

    public static string Group(string field)
    {
        string f = field.ToLowerInvariant();
        if (f is "id" or "name" or "realname" || f.StartsWith("id_") || f == "level" || f == "character_combo_id") return "Básico";
        if (f.StartsWith("require")) return "Requisitos";
        if (f.StartsWith("damage") || f.Contains("attack")) return "Ataque";
        if (f.StartsWith("defence") || f.StartsWith("defense") || f.Contains("magic_defence") || f.Contains("enhance") || f.StartsWith("armor")) return "Defesa";
        if (f.StartsWith("addons_") || f.StartsWith("rands_") || f.StartsWith("uniques_") || f.Contains("probability_addon")) return "Atributos / Addons";
        if (f.Contains("price") || f.Contains("repair") || f.Contains("decompose") || f.Contains("material")) return "Economia";
        if (f.Contains("durability")) return "Durabilidade";
        if (f.StartsWith("file_")) return "Modelo / Arquivos";
        return "Outros";
    }

    public static readonly string[] GroupOrder =
        { "Básico", "Requisitos", "Ataque", "Defesa", "Durabilidade", "Economia", "Atributos / Addons", "Modelo / Arquivos", "Outros" };

    private static readonly Dictionary<string, string> Tokens = new()
    {
        ["require"] = "Requer", ["strength"] = "Força", ["agility"] = "Destreza", ["energy"] = "Magia",
        ["level"] = "Nível", ["damage"] = "Dano", ["magic"] = "Mágico", ["low"] = "mín", ["high"] = "máx",
        ["min"] = "mín", ["max"] = "máx", ["defence"] = "Defesa", ["defense"] = "Defesa", ["price"] = "Preço",
        ["shop"] = "Loja", ["durability"] = "Durabilidade", ["probability"] = "Chance", ["num"] = "Qtd",
        ["file"] = "Arquivo", ["icon"] = "Ícone", ["model"] = "Modelo", ["enhance"] = "Extra",
        ["id"] = "ID", ["hp"] = "HP", ["mp"] = "MP", ["reputation"] = "Reputação",
    };

    private static string Prettify(string field)
    {
        var parts = field.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Tokens.TryGetValue(p.ToLowerInvariant(), out var t) ? t : p);
        var s = string.Join(" ", parts);
        return s.Length == 0 ? field : char.ToUpper(s[0]) + s[1..];
    }
}
