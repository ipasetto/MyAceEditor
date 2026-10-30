using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Regras do servidor (gs) para cada tipo de registro do elements.data: o que faz o gs cair ao subir ou não criar o item
/// (memória do projeto "created-items-must-work"; PW-KB). Fontes: source_server_153 cgame/gs (generate_item_temp.h,
/// worldmanager.cpp) e o gs 1.5.5 desmontado. O editor recusa salvar só os problemas NOVOS (os que já vinham no arquivo
/// do jogo ficam como estão).
/// </summary>
public static class ItemRules
{
    public sealed record Problem(string Rule, string Block, string Id, string Name, string Detail)
    {
        public string Key => $"{Rule}|{Block}|{Id}";
        public override string ToString() => $"{Block} {Id} {Name}: {Detail}";
    }

    // generate_armor/decoration/fashion/poker: sem o subtipo na lista certa o gs devolve -1 (item não é criado)
    static readonly (string Block, string SubList)[] SubTypeLists =
    {
        ("ARMOR_ESSENCE", "ARMOR_SUB_TYPE"), ("DECORATION_ESSENCE", "DECORATION_SUB_TYPE"),
        ("FASHION_ESSENCE", "FASHION_SUB_TYPE"), ("POKER_ESSENCE", "POKER_SUB_TYPE"),
    };
    // generate_weapon: "estes tipos não têm animação, não são gerados"
    static readonly HashSet<string> WeaponNoAnim = new() { "300", "293", "76", "291" };

    /// <param name="cashLimit">false = versão cujo gs aceita item de loja acima de 65534 (o arquivo original já tem).</param>
    public static List<Problem> Check(Module m, bool cashLimit = true)
    {
        var list = new List<Problem>();
        string N(Item i) => TasksFile.SplitColor(i.Name).Text;

        foreach (var (k, who) in IdRefactor.Duplicates(m))
            foreach (var dup in who.Skip(1))
            {
                var p = dup.Split(' ', 3);
                list.Add(new("dup-id", p[0], p[1], p.Length > 2 ? p[2] : "", string.Format(UI.Loc.T("rule.dupId", "ID repetido com {0}: o gs acha só um dos dois"), who[0])));
            }

        foreach (var l in m.Lists.Where(l => IdSpaces.Of(l.Name) == IdSpaces.Space.Essence && l.Name is not ("MONSTER_ESSENCE" or "NPC_ESSENCE")))
            foreach (var i in l.Items)
            {
                if (!int.TryParse(i.Id, out var id)) continue;
                if (cashLimit && id > IdRefactor.MaxCashItemId && i.Fields.TryGetValue("proc_type", out var pt) && long.TryParse(pt.Value, out var p) && (p & IdRefactor.CashItemFlag) != 0)
                    list.Add(new("cash-id", l.Name, i.Id, N(i), string.Format(UI.Loc.T("rule.cashId", "item de loja com ID acima de {0}: o gs cai ao iniciar"), IdRefactor.MaxCashItemId)));
            }

        foreach (var (block, subName) in SubTypeLists)
        {
            if (m.FindListByName(block) is not { } l || m.FindListByName(subName) is not { } subs) continue;
            var ok = subs.Items.Select(s => s.Id).ToHashSet();
            foreach (var i in l.Items)
                if (i.Fields.TryGetValue("id_sub_type", out var st) && !ok.Contains(st.Value))
                    list.Add(new("sub-type", l.Name, i.Id, N(i), string.Format(UI.Loc.T("rule.subType", "subtipo {0} não existe em {1}: o gs não cria o item"), st.Value, subName)));
        }

        CheckNpcTemplates(m, list, N);

        // pedra: player.cpp faz ASSERT(level 1..20) ao herdar/encaixar — o gs cai na hora em que o jogador usa
        if (m.FindListByName("STONE_ESSENCE") is { } stones)
            foreach (var i in stones.Items)
                if (int.TryParse(F(i, "level"), out var lv) && (lv < 1 || lv > 20))
                    list.Add(new("stone-level", stones.Name, i.Id, N(i), string.Format(UI.Loc.T("rule.stoneLevel", "nível {0} (só 1 a 20): o gs cai quando a pedra é usada"), lv)));

        // registros de config que o gs busca por ID fixo (source 1.5.3): sem eles a função cai ou para de funcionar.
        // Versão que não tem o registro no original fica de fora (vira base no salvar).
        var configIds = m.Lists.Where(l => IdSpaces.Of(l.Name) == IdSpaces.Space.Config).SelectMany(l => l.Items).Select(i => i.Id).ToHashSet();
        foreach (var (fid, what) in FixedConfigs)
            if (!configIds.Contains(fid))
                list.Add(new("config-fixed", "CONFIG", fid, what, string.Format(UI.Loc.T("rule.configFixed", "o gs busca a config {0} ({1}) por este ID fixo: não mude o ID nem exclua"), fid, what)));

        if (m.FindListByName("WEAPON_ESSENCE") is { } w)
            foreach (var i in w.Items)
                if (i.Fields.TryGetValue("id_sub_type", out var st) && WeaponNoAnim.Contains(st.Value))
                    list.Add(new("weapon-anim", w.Name, i.Id, N(i), string.Format(UI.Loc.T("rule.weaponAnim", "arma do subtipo {0} (sem animação): o gs nunca cria"), st.Value)));
        return list;
    }

    // get_data_ptr(<ID fixo>, ID_SPACE_CONFIG) no gs (defines do source 1.5.3)
    static readonly (string Id, string What)[] FixedConfigs =
    {
        ("696", "ACC_STORAGE_BLACKLIST_CONFIG"), ("701", "MULTI_EXP_CONFIG"), ("801", "WEDDING_CONFIG"), ("854", "FACTION_FORTRESS_CONFIG"),
        ("1022", "PLAYER_DEATH_DROP_CONFIG"), ("1023", "ONLINE_AWARD_CONFIG"), ("1024", "CONSUME_POINTS_CONFIG"), ("1025", "CONSUME_POINTS_CONFIG"),
        ("1026", "CONSUME_POINTS_CONFIG"), ("1027", "COUNTRY_CONFIG"), ("1038", "PET_EVOLVE_CONFIG"), ("1064", "MERIDIAN_CONFIG"),
        ("1290", "TOUCH_SHOP_CONFIG"), ("1424", "PLAYER_REINCARNATION_CONFIG"), ("1425", "HISTORY_ADVANCE_CONFIG"), ("1444", "TRICKBATTLE_CONFIG"),
        ("1637", "GT_CONFIG"), ("1735", "PROFIT_TIME_CONFIG"), ("1740", "FACTION_PVP_CONFIG"), ("2013", "EQUIP_MAKE_HOLE_CONFIG"),
    };

    static string? F(Item i, string k) => i.Fields.TryGetValue(k, out var f) ? f.Value : null;
    static float Fl(string? v) => float.TryParse(v?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0f;

    // npc_stubs_manager::LoadTemplate (npcgenerator.cpp): ASSERTs que derrubam o gs ao subir
    static void CheckNpcTemplates(Module m, List<Problem> list, Func<Item, string> N)
    {
        var types = m.FindListByName("MONSTER_TYPE")?.Items.ToDictionary(i => i.Id) ?? new();
        var addons = m.FindListByName("MONSTER_ADDON")?.Items.ToDictionary(i => i.Id) ?? new();
        if (m.FindListByName("MONSTER_ESSENCE") is { } mobs && types.Count > 0)
            foreach (var mob in mobs.Items)
            {
                // mob.id_type tem de ser um MONSTER_TYPE
                if (F(mob, "id_type") is not { } tid || !types.TryGetValue(tid, out var mt))
                {
                    list.Add(new("mob-type", mobs.Name, mob.Id, N(mob), string.Format(UI.Loc.T("rule.mobType", "tipo {0} não existe em MONSTER_TYPE: o gs cai ao iniciar"), F(mob, "id_type"))));
                    continue;
                }
                // atributos do tipo: o param1 do MONSTER_ADDON fica em 0..9. As chances (dos atributos e de ódio) NÃO
                // entram: elementdataman::NormalizeRandom divide pela soma ao carregar (tudo 0 = a 1ª vale 1), o ASSERT
                // de soma 1 depois disso nunca falha
                for (int j = 1; j <= 16; j++)
                {
                    if (Fl(F(mt, $"addons_{j}_probability_addon")) <= 1e-8f) continue;
                    if (F(mt, $"addons_{j}_id_addon") is { } aid && addons.TryGetValue(aid, out var ad) && int.TryParse(F(ad, "param1"), out var p1) && (p1 < 0 || p1 > 9))
                        list.Add(new("mob-addon", "MONSTER_TYPE", mt.Id, N(mt), string.Format(UI.Loc.T("rule.mobAddon", "atributo {0} com param1 {1} (só 0 a 9): o gs cai ao iniciar"), aid, p1)));
                }
            }

        if (m.FindListByName("NPC_ESSENCE") is not { } npcs) return;
        var transmit = m.FindListByName("NPC_TRANSMIT_SERVICE")?.Items.ToDictionary(i => i.Id) ?? new();
        var reset = m.FindListByName("NPC_RESETPROP_SERVICE")?.Items.ToDictionary(i => i.Id) ?? new();
        foreach (var npc in npcs.Items)
        {
            // serviço de teleporte usado por NPC sem nenhum destino: ASSERT(service.num_targets)
            if (F(npc, "id_transmit_service") is { } ts && ts != "0" && transmit.TryGetValue(ts, out var t) && F(t, "num_targets") == "0")
                list.Add(new("npc-transmit", npcs.Name, npc.Id, N(npc), string.Format(UI.Loc.T("rule.npcTransmit", "serviço de teleporte {0} sem destino: o gs cai ao iniciar"), ts)));
            // serviço de redistribuir atributos com valor negativo: ASSERT(false)
            if (F(npc, "id_resetprop_service") is { } rs && rs != "0" && reset.TryGetValue(rs, out var r))
                for (int j = 1; j <= 15; j++)
                {
                    if (!int.TryParse(F(r, $"prop_entry_{j}_id_object_need"), out var need) || need <= 0) continue;
                    if (new[] { "strength", "agility", "vital", "energy" }.Any(s => int.TryParse(F(r, $"prop_entry_{j}_{s}_delta"), out var d) && d < 0))
                    {
                        list.Add(new("npc-resetprop", npcs.Name, npc.Id, N(npc), string.Format(UI.Loc.T("rule.npcReset", "serviço {0} com atributo negativo: o gs cai ao iniciar"), rs)));
                        break;
                    }
                }
        }
    }
}
