namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Máscaras de bits do elements.data com significado conhecido (issue #8).
/// Fonte: definições do próprio cliente (EC_IvtrItem.h, enum PROC_*; ExpTypes.h, immune_type).
/// Os nomes são nossa tradução. Bits sem definição aparecem como "Bit N" (não inventamos significado).
/// </summary>
public static class BitMasks
{
    /// <summary>Regras do item (proc_type): bits do cliente.</summary>
    public static readonly (int Bit, string Name)[] Proc =
    {
        // bits 0-2 e 4 são RESTRIÇÕES (ligado = não pode): soulbound 32791 = bits 0,1,2,4,15; item de missão 19 = bits 0,1,4
        (0, "Não cai ao morrer"),
        (1, "Não pode largar no chão"),
        (2, "Não pode vender ao NPC"),
        (3, "Registrar log detalhado"),
        (4, "Não pode trocar com jogadores"),
        (5, "Item de missão"),
        (6, "Vincula ao equipar"),
        (7, "Pode desvincular"),
        (8, "Some ao sair da área"),
        (9, "Usar ao pegar"),
        (10, "Sempre cai ao morrer"),
        (11, "Cai ao desconectar"),
        (12, "Não pode ser consertado"),
        (13, "Em destruição"),
        (14, "Não pode guardar no armazém da conta"),
        (15, "Vinculação celestial"),
        (16, "Pode negociar no site de leilão"),
    };

    /// <summary>
    /// Imunidades do monstro (immune_type). Nomes do editor oficial de dados (CElementData, _immune_type),
    /// traduzidos por nós. Os bits 16 e 19 são internos no editor oficial ("não marcar") e ficam de fora.
    /// </summary>
    public static readonly (int Bit, string Name)[] Immune =
    {
        (0, "Ataque físico"), (1, "Metal"), (2, "Madeira"), (3, "Água"), (4, "Fogo"), (5, "Terra"),
        (6, "Atordoamento"), (7, "Sono"), (8, "Lentidão"), (9, "Imobilização"), (10, "Selamento"),
        (11, "Dano dobrado"), (12, "Empurrão"), (13, "Redução de HP máximo"), (14, "Interromper habilidades"),
        (15, "Dano que ignora defesa"), (17, "Sangramento"), (18, "Todos os estados negativos"),
    };

    /// <summary>
    /// Comida do mascote (food_mask). Ordem conferida nos dados v60: cavalos 29 (sem carne), lobos 26 (carne, fruta,
    /// água), caranguejos mecânicos 16 (só água/óleo) — e igual à tela do RAE.
    /// </summary>
    public static readonly (int Bit, string Name)[] Food =
    {
        (0, "Grama"), (1, "Carne"), (2, "Fungos"), (3, "Frutas"), (4, "Água ou óleo"),
    };

    /// <summary>
    /// Serviços combinados do NPC (combined_services). Nomes como no RAE, conferidos com os NPCs do v60
    /// (bit 4 Gerente de Clã, 6 Caixa de Correio, 7 Leiloeira, 9/10 Domesticadoras, 14 Anciões, 15 Tecelãs,
    /// 23 ferreiros/alfaiates, 26 Casamenteiro); bits 2 e 8 não têm uso conhecido.
    /// </summary>
    public static readonly (int Bit, string Name)[] Services =
    {
        (0, "Aprender habilidades de produção (1)"), (1, "Aprender habilidades de produção (2)"), (2, "(só versão chinesa)"),
        (3, "Entrar no campo de batalha"), (4, "Criar clã"), (5, "Roupas (estilista)"), (6, "Correio"), (7, "Leilão"),
        (8, "(não disponível)"), (9, "Incubar mascote"), (10, "Restaurar mascote"), (11, "Batalha territorial (resultado)"),
        (12, "Sair do campo de batalha"), (13, "Troca de gold"), (14, "Refinar equipamento"), (15, "Pintar roupa"),
        (16, "Transferir refino"), (17, "Remover atributos do daimon"), (18, "Remover habilidades do daimon (1)"),
        (19, "Remover habilidades do daimon (2)"), (20, "Pontos / transferência de daimon"), (21, "Separar daimon"),
        (22, "Equipamento de daimon"), (23, "Reparar equipamento"), (24, "Caça ao tesouro"), (25, "Troca de sexo"),
        (26, "Casamento"), (27, "Construir torre"), (28, "Pagamento"), (29, "Pontos de contribuição"),
        (30, "Redefinir senha do banqueiro"), (31, "Ver intervalo de ataque"),
    };

    /// <summary>Nomes dos bits ligados em <paramref name="value"/> (ex.: "Não cai ao morrer, Não pode vender ao NPC").</summary>
    public static string Describe((int Bit, string Name)[] table, long value, string unknownPrefix = "Bit")
    {
        if (value == 0) return "";
        var names = new List<string>();
        for (int b = 0; b < 32; b++)
        {
            if ((value & (1L << b)) == 0) continue;
            var hit = table.FirstOrDefault(t => t.Bit == b);
            names.Add(hit.Name ?? $"{unknownPrefix} {b}");
        }
        return string.Join(", ", names);
    }

    public static bool TryParse(string v, out long value) => long.TryParse(v, out value) && value >= 0;
}
