using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Traduz os nomes técnicos dos campos do elements.data (do .cfg) para rótulos
/// amigáveis e agrupa em seções, para a UI ficar legível. Os textos em português são o padrão do código;
/// os outros idiomas vêm de lang/*.json (chaves field.*, tok.*, group.*) via <see cref="Loc.T"/>.
/// Os IDs de grupo ("Básico", "Ataque"…) são identificadores estáveis — para exibir use <see cref="GroupName"/>.
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
        ["material_need"] = "Pedras por tentativa de refino", ["pile_num_max"] = "Empilhar (máx)",
        ["element_id"] = "Item ao decompor", ["element_num"] = "Qtd. ao decompor",
        // monstro (struct MONSTER_ESSENCE do gs; PW-KB §S26)
        ["id_type"] = "Tipo", ["prop"] = "Propriedade (texto)", ["desc"] = "Descrição (texto)",
        ["faction"] = "Facção", ["monster_faction"] = "Facção de monstro", ["size"] = "Tamanho", ["damage_delay"] = "Atraso do dano",
        ["id_strategy"] = "Política de IA", ["role_in_war"] = "Papel na guerra", ["show_level"] = "Nível exibido",
        ["id_pet_egg_captured"] = "Ovo de pet ao capturar", ["life"] = "HP", ["defence"] = "Defesa física",
        ["exp"] = "EXP", ["skillpoint"] = "SP", ["money_average"] = "Dinheiro (média)", ["money_var"] = "Dinheiro (variação)",
        ["short_range_mode"] = "Modo de alcance", ["sight_range"] = "Alcance de visão", ["attack"] = "Precisão", ["armor"] = "Esquiva",
        ["damage_min"] = "Dano físico (mín)", ["damage_max"] = "Dano físico (máx)",
        ["magic_damage_min"] = "Dano mágico (mín)", ["magic_damage_max"] = "Dano mágico (máx)",
        ["id_skill"] = "Habilidade", ["skill_level"] = "Nível da habilidade", ["hp_regenerate"] = "Regeneração de HP",
        ["aggressive_mode"] = "Agressivo", ["monster_faction_ask_help"] = "Pede ajuda à facção", ["monster_faction_can_help"] = "Ajuda a facção",
        ["aggro_range"] = "Alcance de agressão", ["aggro_time"] = "Tempo de agressão", ["inhabit_type"] = "Habitat",
        ["patroll_mode"] = "Modo de patrulha", ["stand_mode"] = "Modo parado",
        ["walk_speed"] = "Velocidade andando", ["run_speed"] = "Velocidade correndo", ["fly_speed"] = "Velocidade voando", ["swim_speed"] = "Velocidade nadando",
        ["attack_degree"] = "Grau de ataque", ["defend_degree"] = "Grau de defesa", ["common_strategy"] = "Estratégia comum",
        ["after_death"] = "Ação após morrer", ["drop_times"] = "Vezes que dropa", ["drop_protected"] = "Drop protegido",
        ["highest_frequency"] = "Frequência máxima", ["no_accept_player_buff"] = "Não aceita buff de jogador",
        ["invisible_lvl"] = "Nível de invisibilidade", ["uninvisible_lvl"] = "Nível de detecção", ["no_auto_fight"] = "Sem combate automático",
        ["fixed_direction"] = "Direção fixa", ["id_building"] = "Construção", ["combined_switch"] = "Chave combinada",
        ["hp_adjust_common_value"] = "Ajuste comum de HP", ["defence_adjust_common_value"] = "Ajuste comum de defesa",
        ["attack_adjust_common_value"] = "Ajuste comum de ataque",
        // NPC (struct NPC_ESSENCE)
        ["refresh_time"] = "Tempo para reaparecer", ["attack_rule"] = "Pode ser atacado", ["tax_rate"] = "Taxa",
        ["id_src_monster"] = "Monstro de origem", ["hello_msg"] = "Mensagem de saudação", ["id_to_discover"] = "Descoberta (ID)",
        ["domain_related"] = "Ligado a território", ["combined_services"] = "Serviços combinados", ["id_mine"] = "Mina", ["no_collision"] = "Sem colisão",
        ["use_on_pick"] = "Usar ao pegar", ["levelup_addon"] = "Atributo de refino", ["fixed_props"] = "Propriedades fixas",
        ["equip_location"] = "Local do equipamento", ["equip_mask"] = "Máscara de equipamento",
        ["proc_type"] = "Regras do item", ["immune_type"] = "Imunidades do monstro", ["has_guid"] = "Tem GUID",
        ["file_model"] = "Modelo", ["file_model_right"] = "Modelo (direita)", ["file_model_left"] = "Modelo (esquerda)",
        ["file_matter"] = "Textura", ["file_icon"] = "Ícone", ["gender_id"] = "Gênero",
    };

    static readonly string[] Elements = { "Metal", "Madeira", "Água", "Fogo", "Terra" };
    static readonly Dictionary<string, string> Services = new()
    {
        ["talk"] = "Conversa", ["sell"] = "Loja", ["buy"] = "Compra", ["repair"] = "Conserto", ["install"] = "Encaixar pedras",
        ["uninstall"] = "Remover pedras", ["task_out"] = "Dar quest", ["task_in"] = "Entregar quest", ["task_matter"] = "Itens de quest",
        ["skill"] = "Ensinar habilidade", ["heal"] = "Cura", ["transmit"] = "Teletransporte", ["transport"] = "Transporte",
        ["proxy"] = "Procurador", ["storage"] = "Armazém", ["make"] = "Fabricação", ["decompose"] = "Decompor", ["identify"] = "Identificar",
        ["war_towerbuild"] = "Torres de guerra", ["resetprop"] = "Redistribuir atributos", ["petname"] = "Nome de pet",
        ["petlearnskill"] = "Pet aprender habilidade", ["petforgetskill"] = "Pet esquecer habilidade", ["equipbind"] = "Vincular equipamento",
        ["equipdestroy"] = "Destruir vinculado", ["equipundestroy"] = "Cancelar destruição", ["goblin_skill"] = "Habilidade de gênio",
        ["engrave"] = "Gravação", ["randprop"] = "Atributo aleatório",
    };
    static string Element(int n) => Loc.T("elem." + n, Elements[n - 1]);

    public static string Label(string field)
    {
        if (Map.TryGetValue(field, out var l)) return Loc.T("field." + field.ToLowerInvariant(), l);
        // addons/rands/uniques: "addons_3_id_addon" -> "Atributo 3 (id)" / "..._probability..." -> "Atributo 3 (chance)"
        var m = System.Text.RegularExpressions.Regex.Match(field, @"^(addons|rands|uniques)_(\d+)_(id|probability)");
        if (m.Success)
        {
            string kind = m.Groups[1].Value switch
            {
                "addons" => Loc.T("field.kind.addon", "Atributo"),
                "rands" => Loc.T("field.kind.rand", "Aleatório"),
                _ => Loc.T("field.kind.unique", "Único"),
            };
            string what = m.Groups[3].Value == "id" ? Loc.T("field.what.id", "id") : Loc.T("field.what.chance", "chance");
            return $"{kind} {m.Groups[2].Value} ({what})";
        }
        // defesa e dano por elemento (struct do gs: magic_defences[5], magic_damages_ext[5]; 1..5 = metal, madeira, água, fogo, terra)
        var e = System.Text.RegularExpressions.Regex.Match(field, @"^(magic_defences|magic_damages_ext)_([1-5])(?:_(low|high|damage_min|damage_max))?$");
        if (e.Success)
        {
            string elem = Element(int.Parse(e.Groups[2].Value));
            string what = e.Groups[1].Value == "magic_defences" ? Loc.T("field.elemDefense", "Defesa de {0}") : Loc.T("field.elemDamage", "Dano extra de {0}");
            string range = e.Groups[3].Value switch
            {
                "low" or "damage_min" => $" ({Loc.T("field.min", "mín")})",
                "high" or "damage_max" => $" ({Loc.T("field.max", "máx")})",
                _ => "",
            };
            return string.Format(what, elem) + range;
        }
        // monstro: drops e habilidades numerados ("Drop 3 (item)", "Habilidade com HP 50% 2 (chance)")
        var dr = System.Text.RegularExpressions.Regex.Match(field, @"^(drop_matters|task_lists)_(\d+)_(id|probability)$");
        if (dr.Success)
        {
            bool quest = dr.Groups[1].Value == "task_lists";
            string head = quest ? Loc.T("field.kind.quest", "Quest") : Loc.T("field.kind.drop", "Drop");
            string what = dr.Groups[3].Value == "probability" ? Loc.T("field.what.chance", "chance") : quest ? Loc.T("field.what.id", "id") : Loc.T("field.what.item", "item");
            return $"{head} {dr.Groups[2].Value} ({what})";
        }
        var sk = System.Text.RegularExpressions.Regex.Match(field, @"^(?:skill_hp(\d+)|skills)_(\d+)_(id_skill|level|probability)$");
        if (sk.Success)
        {
            string head = sk.Groups[1].Success ? string.Format(Loc.T("field.kind.skillHp", "Habilidade com HP {0}%"), sk.Groups[1].Value) : Loc.T("field.kind.skill", "Habilidade");
            string what = sk.Groups[3].Value switch { "id_skill" => Loc.T("field.what.id", "id"), "level" => Loc.T("field.what.level", "nível"), _ => Loc.T("field.what.chance", "chance") };
            return $"{head} {sk.Groups[2].Value} ({what})";
        }
        // NPC: id_<serviço>_service -> "Serviço: Loja"
        var sv = System.Text.RegularExpressions.Regex.Match(field, @"^id_([a-z_]+)_service$");
        if (sv.Success && Services.TryGetValue(sv.Groups[1].Value, out var svc))
            return $"{Loc.T("field.kind.service", "Serviço")}: {Loc.T("svc." + sv.Groups[1].Value, svc)}";
        // equipamento: chance de vir com N slots (ao cair / ao fabricar) e com N atributos
        var so = System.Text.RegularExpressions.Regex.Match(field, @"^(drop|make)_probability_socket(\d)$");
        if (so.Success)
        {
            int n = int.Parse(so.Groups[2].Value);
            string slots = n == 1 ? Loc.T("field.slot", "slot") : Loc.T("field.slots", "slots");
            string when = so.Groups[1].Value == "drop" ? Loc.T("field.whenDrop", "ao cair") : Loc.T("field.whenMake", "fabricação");
            return string.Format(Loc.T("field.socketChance", "Chance de {0} {1} ({2})"), n, slots, when);
        }
        var an = System.Text.RegularExpressions.Regex.Match(field, @"^probability_addon_num(\d)$");
        if (an.Success) return string.Format(Loc.T("field.addonNum", "Chance de {0} atributo(s)"), an.Groups[1].Value);
        // monstro: probability_drop_num0..3 = chance de cair N itens por vez
        var dn = System.Text.RegularExpressions.Regex.Match(field, @"^probability_drop_num(\d)$");
        if (dn.Success) return string.Format(Loc.T("field.dropNum", "Chance de cair {0} item(ns)"), dn.Groups[1].Value);
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

    /// <summary>Nome de exibição (traduzido) de um ID de grupo. O ID em si nunca muda com o idioma.</summary>
    public static string GroupName(string id) => id switch
    {
        "Todos" => Loc.T("group.all", "Todos"),
        "Básico" => Loc.T("group.basic", "Básico"),
        "Requisitos" => Loc.T("group.requirements", "Requisitos"),
        "Ataque" => Loc.T("group.attack", "Ataque"),
        "Defesa" => Loc.T("group.defense", "Defesa"),
        "Durabilidade" => Loc.T("group.durability", "Durabilidade"),
        "Economia" => Loc.T("group.economy", "Economia"),
        "Atributos / Addons" => Loc.T("group.addons", "Atributos"),   // o ID continua "Atributos / Addons"; só a exibição mudou
        "Modelo / Arquivos" => Loc.T("group.files", "Modelo / Arquivos"),
        "Outros" => Loc.T("group.other", "Outros"),
        "Habilidades" => Loc.T("group.skills", "Habilidades"),
        "Queda" => Loc.T("group.drops", "Queda"),
        "Modelo" => Loc.T("group.model", "Modelo"),
        _ => id,
    };


    private static string Prettify(string field)
    {
        var parts = field.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(p =>
        {
            var lo = p.ToLowerInvariant();
            var pt = FieldTokens.Lookup(lo);
            // palavra do dicionário em português; se for a língua da interface e houver tradução própria, usa-a
            return pt is null ? p : Loc.T("tok." + lo, pt);
        });
        var s = string.Join(AppLang.Current == "zh" ? "" : " ", parts);
        return s.Length == 0 ? field : char.ToUpper(s[0]) + s[1..];
    }
}
