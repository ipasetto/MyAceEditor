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
