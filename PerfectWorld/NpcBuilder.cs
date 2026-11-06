using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public enum NpcKind { Creature, Npc, Seller, Forge, Teleporter }

/// <summary>Destino de um teleportador: ponto, taxa e nível mínimo.</summary>
public readonly record struct TeleportStop(int Point, int Fee, int Level);

/// <summary>Loja em rascunho: 8 páginas de 32 itens, como NPC_SELL_SERVICE.</summary>
public sealed class ShopDraft
{
    public string[] Titles = Enumerable.Repeat("", 8).ToArray();
    public int[][] Goods = Enumerable.Range(0, 8).Select(_ => new int[32]).ToArray();
    /// <summary>Preço novo por item (shop_price do item: vale em todas as lojas do jogo).</summary>
    public Dictionary<int, int> Prices = [];
    public int Count => Goods.Sum(p => p.Count(g => g > 0));
    public ShopDraft Copy() => new() { Titles = (string[])Titles.Clone(), Prices = new(Prices), Goods = Goods.Select(p => (int[])p.Clone()).ToArray() };
}

/// <summary>Receita de um espaço da fornalha: RecipeId &gt; 0 usa uma que já existe; 0 cria uma nova com os outros campos.</summary>
public sealed class RecipeDraft
{
    public int RecipeId, Product, Count = 1, Price, SkillLevel;
    /// <summary>Habilidade que a receita pede (id_skill): só para mostrar. Ao gravar vale a da fornalha.</summary>
    public int Skill;
    public float Duration = 2;
    public List<(int Id, int Num)> Materials = [];
    public RecipeDraft Copy() => new() { RecipeId = RecipeId, Product = Product, Count = Count, Price = Price, Duration = Duration, SkillLevel = SkillLevel, Skill = Skill, Materials = [.. Materials] };
}

/// <summary>Fabricação em rascunho: 8 páginas de 32 receitas, como NPC_MAKE_SERVICE.</summary>
public sealed class MakeDraft
{
    /// <summary>Habilidade que o jogador precisa ter (0 = nada): uma por NPC, como id_make_skill.</summary>
    public int Skill;
    public string[] Titles = Enumerable.Repeat("", 8).ToArray();
    public RecipeDraft?[][] Goods = Enumerable.Range(0, 8).Select(_ => new RecipeDraft?[32]).ToArray();
    public int Count => Goods.Sum(p => p.Count(g => g is not null));
    public MakeDraft Copy() => new() { Skill = Skill, Titles = (string[])Titles.Clone(), Goods = Goods.Select(p => p.Select(g => g?.Copy()).ToArray()).ToArray() };
}

public sealed class NpcDraft
{
    public NpcKind Kind;
    public int BaseId;
    public string Name = "";
    public ShopDraft? Shop;
    public MakeDraft? Make;
    public List<TeleportStop> Stops = [];
    /// <summary>Ponto que o jogador ativa ao falar com ele (id_to_discover); 0 = nenhum.</summary>
    public int Discover;
    /// <summary>Editar: ID do NPC que já existe. 0 = NPC novo.</summary>
    public int Editing;
    /// <summary>Tem teleporte (o NPC pode ter loja, fabricação e teleporte juntos).</summary>
    public bool Teleports;
    public bool HasShop => Shop is not null || Kind == NpcKind.Seller;
    public bool HasMake => Make is not null || Kind == NpcKind.Forge;
    public bool HasStops => Teleports || Kind == NpcKind.Teleporter;
}

/// <summary>
/// Cria NPC, loja, fabricação e receitas a partir do rascunho da ferramenta Criar do Mundo 3D. Regras do gs: serviço inválido
/// faz o gs pular o NPC (npcgenerator.cpp); chances da receita têm de somar 1, senão ele ignora a receita; material com
/// quantidade 0 é ignorado. Spec: docs/superpowers/specs/2026-11-06-criar-npc-3d-design.md.
/// </summary>
public static class NpcBuilder
{
    public const int Pages = 8, Slots = 32, MaxMaterials = 32, MaxStops = 32;
    /// <summary>combined_services 0x08: sem este bit o gs e o cliente ignoram id_to_discover (npcgenerator.cpp, CECNPCServer::GetWayPointID).</summary>
    public const long DiscoverBit = 8;

    static string? F(Item i, string k) => i.Fields.TryGetValue(k, out var f) ? f.Value : null;
    static void Set(Item i, string k, string v) { if (i.Fields.TryGetValue(k, out var f)) i.Fields[k] = Field.Of(f.Type, v); }

    /// <summary>NPC novo nasce sem fala de boas-vindas e sem nenhum serviço; aparência, nível e monstro de origem vêm da base.</summary>
    public static void Clear(Item npc)
    {
        foreach (var (k, f) in npc.Fields.ToList())
            if (k == "hello_msg") npc.Fields[k] = Field.Of(f.Type, "");
            else if (k is "combined_services" or "combined_services2" || k.StartsWith("id_") && k.EndsWith("_service")) npc.Fields[k] = Field.Of(f.Type, "0");
    }

    static Item? Find(Module m, string block, int id) => m.FindListByName(block)?.Items.FirstOrDefault(i => i.Id == id.ToString());

    /// <summary>Habilidade da fabricação: a das receitas que já existem. Receitas novas usam a mesma (0 = não exige).</summary>
    static int SkillOf(Module m, MakeDraft d, out bool mixed)
    {
        var skills = d.Goods.SelectMany(p => p).Where(g => g is { RecipeId: > 0 })
            .Select(g => Find(m, "RECIPE_ESSENCE", g!.RecipeId) is { } r && int.TryParse(F(r, "id_skill"), out var s) ? s : 0).Distinct().ToList();
        if (d.Skill > 0 && !skills.Contains(d.Skill)) skills.Add(d.Skill);
        mixed = skills.Count > 1;
        return skills.FirstOrDefault();
    }

    /// <summary>Tudo o que impede o registro de funcionar no jogo; lista vazia = pode criar.</summary>
    public static List<string> Validate(Module m, NpcDraft d, Func<int, bool> isItem)
    {
        var e = new List<string>();
        string T(string k, string pt) => UI.Loc.T(k, pt);
        if (d.BaseId <= 0) e.Add(T("nb.noBase", "Escolha quem é (a aparência vem daí)."));
        if (d.Kind == NpcKind.Creature) return e;
        if (d.BaseId > 0 && Find(m, "NPC_ESSENCE", d.BaseId) is null) e.Add(T("nb.baseNpc", "A base precisa ser um NPC."));
        if (d.Name.Trim().Length == 0) e.Add(T("nb.noName", "Dê um nome para o NPC."));
        if (d.HasShop)
        {
            if (d.Shop is not { Count: > 0 } shop) e.Add(T("nb.emptyShop", "A loja está vazia: clique nos espaços para pôr os itens."));
            else foreach (int id in shop.Goods.SelectMany(p => p).Where(g => g > 0).Distinct())
                    if (!isItem(id)) e.Add(string.Format(T("nb.noItem", "O item {0} não existe no elements.data."), id));
            if (m.FindListByName("NPC_SELL_SERVICE") is null) e.Add(T("nb.noSellBlock", "Este elements.data não tem o bloco de lojas."));
        }
        if (d.HasStops)
        {
            if (d.Stops.Count == 0) e.Add(T("nb.noStops", "Escolha pelo menos um destino: sem destino o gs cai ao iniciar."));
            if (d.Stops.Count > MaxStops) e.Add(string.Format(T("nb.manyStops", "Mais de {0} destinos."), MaxStops));
        }
        if (d.HasMake)
        {
            if (d.Make is not { Count: > 0 } make) { e.Add(T("nb.emptyMake", "A fornalha está vazia: clique nos espaços para pôr as receitas.")); return e; }
            SkillOf(m, make, out bool mixed);
            if (mixed) e.Add(T("nb.mixedSkill", "As receitas escolhidas usam habilidades diferentes: o NPC só atende uma."));
            foreach (var r in make.Goods.SelectMany(p => p).Where(g => g is not null).Select(g => g!))
            {
                if (r.RecipeId > 0) { if (Find(m, "RECIPE_ESSENCE", r.RecipeId) is null) e.Add(string.Format(T("nb.noRecipe", "A receita {0} não existe."), r.RecipeId)); continue; }
                if (!isItem(r.Product)) e.Add(T("nb.noProduct", "Uma receita está sem o item que ela produz."));
                if (r.Count < 1) e.Add(T("nb.badCount", "A quantidade produzida precisa ser pelo menos 1."));
                if (r.Materials.Count == 0) e.Add(T("nb.noMaterials", "Uma receita está sem materiais."));
                if (r.Materials.Count > MaxMaterials) e.Add(string.Format(T("nb.manyMaterials", "Uma receita tem mais de {0} materiais."), MaxMaterials));
                if (r.Materials.Any(x => x.Num < 1)) e.Add(T("nb.zeroMaterial", "Material com quantidade 0: o jogo ignora."));
                if (r.Materials.Select(x => x.Id).Distinct().Count() != r.Materials.Count) e.Add(T("nb.dupMaterial", "Material repetido na mesma receita."));
                if (r.Materials.Any(x => !isItem(x.Id))) e.Add(T("nb.badMaterial", "Um material não existe no elements.data."));
            }
        }
        return e.Distinct().ToList();
    }

    static void FillShop(Item svc, ShopDraft d)
    {
        for (int p = 0; p < Pages; p++)
        {
            Set(svc, $"pages_{p + 1}_page_title", d.Titles[p]);
            for (int g = 0; g < Slots; g++)
            {
                var v = d.Goods[p][g].ToString();
                Set(svc, $"pages_{p + 1}_goods_{g + 1}_id", v);   // nome do campo muda entre versões: vale o que existir
                Set(svc, $"pages_{p + 1}_id_goods_{g + 1}", v);
            }
        }
    }

    /// <summary>Serviço em branco com o próximo ID livre do bloco.</summary>
    public static Item NewService(Module m, string block, string name, Func<ItemList, Item> blank)
    {
        var list = m.FindListByName(block)!;
        var s = blank(list);
        s.Id = IdRefactor.NextFreeId(m, list).ToString(); s.Name = name;
        Set(s, "ID", s.Id); Set(s, "Name", name);
        return s;
    }

    /// <summary>Cópia do serviço com ID novo, para mudar um NPC sem mexer nos outros que usam o mesmo.</summary>
    public static Item CopyService(Module m, ItemList block, Item from)
    {
        var s = new Item { Id = IdRefactor.NextFreeId(m, block).ToString(), Name = from.Name, Fields = from.Fields.Clone(), Raw = from.Raw?.ToArray() };
        Set(s, "ID", s.Id);
        return s;
    }

    /// <summary>Campos do serviço de teleporte para estes destinos: num_targets e a lista, o resto zerado.</summary>
    public static IEnumerable<(string Key, string Value)> StopFields(Item svc, IReadOnlyList<TeleportStop> stops)
    {
        yield return ("num_targets", stops.Count.ToString());
        for (int i = 1; svc.Fields.ContainsKey($"targets_{i}_idTarget"); i++)
        {
            bool has = i <= stops.Count;
            yield return ($"targets_{i}_idTarget", has ? stops[i - 1].Point.ToString() : "0");
            yield return ($"targets_{i}_fee", has ? stops[i - 1].Fee.ToString() : "0");
            yield return ($"targets_{i}_required_level", has ? stops[i - 1].Level.ToString() : "0");
        }
    }

    public static void FillStops(Item svc, IReadOnlyList<TeleportStop> stops)
    {
        foreach (var (k, v) in StopFields(svc, stops)) Set(svc, k, v);
    }

    /// <summary>Destinos que o serviço tem hoje.</summary>
    public static List<TeleportStop> StopsOf(Item svc)
    {
        var o = new List<TeleportStop>();
        int n = int.TryParse(F(svc, "num_targets"), out var c) ? c : 0;
        for (int i = 1; i <= n && svc.Fields.ContainsKey($"targets_{i}_idTarget"); i++)
            o.Add(new TeleportStop(int.Parse(F(svc, $"targets_{i}_idTarget") ?? "0"), int.Parse(F(svc, $"targets_{i}_fee") ?? "0"), int.Parse(F(svc, $"targets_{i}_required_level") ?? "0")));
        return o;
    }

    static void FillRecipe(Item rec, RecipeDraft r, int skill, string name)   // recipe_level dá a experiência da habilidade; skill_level é o mínimo exigido
    {
        rec.Name = name;
        Set(rec, "Name", name);
        Set(rec, "id_skill", skill.ToString()); Set(rec, "skill_level", (skill > 0 ? r.SkillLevel : 0).ToString()); Set(rec, "recipe_level", Math.Max(1, r.SkillLevel).ToString()); Set(rec, "bind_type", "0");
        Set(rec, "targets_1_id_to_make", r.Product.ToString()); Set(rec, "targets_1_probability", "1");
        for (int t = 2; t <= 4; t++) { Set(rec, $"targets_{t}_id_to_make", "0"); Set(rec, $"targets_{t}_probability", "0"); }
        Set(rec, "fail_probability", "0"); Set(rec, "num_to_make", r.Count.ToString()); Set(rec, "price", r.Price.ToString());
        Set(rec, "duration", r.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(rec, "exp", "0"); Set(rec, "skillpoint", "0");
        for (int i = 1; i <= MaxMaterials; i++)
        {
            bool has = i <= r.Materials.Count;
            Set(rec, $"materials_{i}_id", has ? r.Materials[i - 1].Id.ToString() : "0");
            Set(rec, $"materials_{i}_num", has ? r.Materials[i - 1].Num.ToString() : "0");
        }
    }

    /// <summary>Cria os registros. blank = registro zerado do bloco; insert = entra no arquivo. Devolve o ID do NPC.</summary>
    public static int Build(Module m, NpcDraft d, Func<ItemList, Item> blank, Action<ItemList, Item> insert, Func<int, string> nameOf)
    {
        var npcs = m.FindListByName("NPC_ESSENCE")!;
        var from = Find(m, "NPC_ESSENCE", d.BaseId)!;
        var npc = new Item { Id = IdRefactor.NextFreeId(m, npcs).ToString(), Name = d.Name.Trim(), Fields = from.Fields.Clone(), Raw = from.Raw?.ToArray() };
        Clear(npc);
        Set(npc, "ID", npc.Id); Set(npc, "Name", npc.Name);
        insert(npcs, npc);

        Item Service(string block, string name) => NewService(m, block, name, blank);

        if (d.HasShop)
        {
            var svc = Service("NPC_SELL_SERVICE", npc.Name);
            insert(m.FindListByName("NPC_SELL_SERVICE")!, svc);
            FillShop(svc, d.Shop!);
            Set(npc, "id_sell_service", svc.Id);
            if (m.FindListByName("NPC_BUY_SERVICE")?.Items.FirstOrDefault() is { } buy) Set(npc, "id_buy_service", buy.Id);   // o jogador também vende para ele
        }
        if (d.Discover > 0) { Set(npc, "id_to_discover", d.Discover.ToString()); Set(npc, "combined_services", WithDiscover(F(npc, "combined_services"), true)); }
        if (d.HasStops)
        {
            var svc = Service("NPC_TRANSMIT_SERVICE", npc.Name);
            insert(m.FindListByName("NPC_TRANSMIT_SERVICE")!, svc);
            FillStops(svc, d.Stops);
            Set(npc, "id_transmit_service", svc.Id);
        }
        if (d.HasMake)
        {
            var svc = Service("NPC_MAKE_SERVICE", npc.Name);
            insert(m.FindListByName("NPC_MAKE_SERVICE")!, svc);
            FillMake(m, d.Make!, svc, blank, insert, nameOf);
            Set(npc, "id_make_service", svc.Id);
        }
        return int.Parse(npc.Id);
    }

    /// <summary>combined_services com o bit de descobrir ligado ou desligado.</summary>
    public static string WithDiscover(string? now, bool on)
    {
        long v = long.TryParse(now, out var n) ? n : 0;
        return (on ? v | DiscoverBit : v & ~DiscoverBit).ToString();
    }

    /// <summary>Rascunho com o que o NPC tem hoje, para a ferramenta Criar editar. Null se o ID não é de NPC.</summary>
    public static NpcDraft? Load(Module m, int id)
    {
        if (Find(m, "NPC_ESSENCE", id) is not { } n) return null;
        int Num(Item i, string k) => int.TryParse(F(i, k), out var v) ? v : 0;
        var d = new NpcDraft { Editing = id, BaseId = id, Kind = NpcKind.Npc, Name = TasksFile.SplitColor(n.Name).Text, Discover = Num(n, "id_to_discover") };
        // os três serviços são lidos: um NPC pode ter loja e fornalha juntas, e a edição mostra os dois
        if (Find(m, "NPC_TRANSMIT_SERVICE", Num(n, "id_transmit_service")) is { } tr)
        {
            d.Kind = NpcKind.Teleporter;
            d.Teleports = true;
            d.Stops = StopsOf(tr);
        }
        if (Find(m, "NPC_MAKE_SERVICE", Num(n, "id_make_service")) is { } mk)
        {
            d.Kind = NpcKind.Forge;
            var make = new MakeDraft { Skill = Num(mk, "id_make_skill") };
            for (int p = 0; p < Pages; p++)
            {
                make.Titles[p] = F(mk, $"pages_{p + 1}_page_title") ?? "";
                for (int g = 0; g < Slots; g++)
                    if (Num(mk, $"pages_{p + 1}_id_goods_{g + 1}") is > 0 and var r) make.Goods[p][g] = new RecipeDraft { RecipeId = r };
            }
            d.Make = make;
        }
        if (Find(m, "NPC_SELL_SERVICE", Num(n, "id_sell_service")) is { } sell)
        {
            d.Kind = NpcKind.Seller;
            var shop = new ShopDraft();
            for (int p = 0; p < Pages; p++)
            {
                shop.Titles[p] = F(sell, $"pages_{p + 1}_page_title") ?? "";
                for (int g = 0; g < Slots; g++)
                    shop.Goods[p][g] = int.TryParse(F(sell, $"pages_{p + 1}_goods_{g + 1}_id") ?? F(sell, $"pages_{p + 1}_id_goods_{g + 1}"), out var v) ? v : 0;
            }
            d.Shop = shop;
        }
        return d;
    }

    /// <summary>Receita que já existe como rascunho, para abrir na janela e mudar: ao gravar vira uma receita nova, a antiga fica para quem usa.</summary>
    public static RecipeDraft? ReadRecipe(Module m, int id)
    {
        if (Find(m, "RECIPE_ESSENCE", id) is not { } r) return null;
        int Num(string k) => int.TryParse(F(r, k), out var v) ? v : 0;
        var d = new RecipeDraft { Product = Num("targets_1_id_to_make"), Count = Math.Max(1, Num("num_to_make")), Price = Num("price"), SkillLevel = Num("skill_level"), Skill = Num("id_skill"),
            Duration = float.TryParse(F(r, "duration"), System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 2 };
        for (int i = 1; r.Fields.ContainsKey($"materials_{i}_id"); i++)
            if (Num($"materials_{i}_id") is > 0 and var id2) d.Materials.Add((id2, Math.Max(1, Num($"materials_{i}_num"))));
        return d;
    }

    /// <summary>Campos que mudam neste serviço para ficar como o rascunho. Receitas novas entram pelo insert.</summary>
    public static List<(string Key, string Value)> ServiceChanges(Module m, NpcDraft d, NpcKind kind, Item svc, Func<ItemList, Item> blank, Action<ItemList, Item> insert, Func<int, string> nameOf)
    {
        var after = new Item { Id = svc.Id, Name = svc.Name, Fields = svc.Fields.Clone() };
        if (kind == NpcKind.Seller) FillShop(after, d.Shop!);
        if (kind == NpcKind.Forge) FillMake(m, d.Make!, after, blank, insert, nameOf);
        if (kind == NpcKind.Teleporter) FillStops(after, d.Stops);
        return after.Fields.Where(f => F(svc, f.Key) != f.Value.Value).Select(f => (f.Key, f.Value.Value)).ToList();
    }

    static void FillMake(Module m, MakeDraft make, Item svc, Func<ItemList, Item> blank, Action<ItemList, Item> insert, Func<int, string> nameOf)
    {
        {
            int skill = SkillOf(m, make, out _);
            var recipes = m.FindListByName("RECIPE_ESSENCE")!;
            Set(svc, "id_make_skill", skill.ToString()); Set(svc, "produce_type", "0");
            var type = recipes.Items.FirstOrDefault();
            for (int p = 0; p < Pages; p++)
            {
                Set(svc, $"pages_{p + 1}_page_title", make.Titles[p]);
                for (int g = 0; g < Slots; g++)
                {
                    int id = 0;
                    if (make.Goods[p][g] is { } r)
                    {
                        id = r.RecipeId;
                        if (id <= 0)
                        {
                            var rec = blank(recipes);
                            rec.Id = IdRefactor.NextFreeId(m, recipes).ToString();
                            Set(rec, "ID", rec.Id);
                            if (type is not null) { Set(rec, "id_major_type", F(type, "id_major_type") ?? "0"); Set(rec, "id_sub_type", F(type, "id_sub_type") ?? "0"); }
                            FillRecipe(rec, r, skill, nameOf(r.Product));
                            insert(recipes, rec);
                            id = int.Parse(rec.Id);
                        }
                    }
                    Set(svc, $"pages_{p + 1}_id_goods_{g + 1}", id.ToString());
                }
            }
        }
    }
}
