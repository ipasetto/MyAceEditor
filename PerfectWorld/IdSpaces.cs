namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Espaço de IDs de cada bloco do elements.data (PW-KB §S28): o jogo indexa os registros em tabelas
/// separadas, então o mesmo número pode existir em espaços diferentes sem conflito. Itens, tipos,
/// monstros e serviços de NPC dividem o espaço "essence"; receitas, gravação e atributo aleatório
/// o "recipe"; addons de equipamento e de monstro o "addon"; configs, rosto e casa têm o próprio.
/// </summary>
public static class IdSpaces
{
    public enum Space { Essence, Addon, Recipe, Config, Face, Home, Talk }

    public static Space Of(string block) => block switch
    {
        "TALK_PROC" => Space.Talk,
        _ when block.EndsWith("_ADDON") => Space.Addon,
        _ when block.StartsWith("FACE_") || block.StartsWith("HIGH_POLY_") || block is "COLORPICKER_ESSENCE" or "CUSTOMIZEDATA_ESSENCE" => Space.Face,
        "EXTR_PROP_SKILL_CONFIG" => Space.Essence,   // exceção: config no espaço dos itens
        // tipos de tabelas de config (a "essence" deles é um *_CONFIG)
        "STORY_BOOK_MAJOR_TYPE" or "BLACK_WHITE_LIST_MAJOR_TYPE" or "SPECIAL_CAMP_REGION_MAJOR_TYPE" => Space.Config,
        _ when block.EndsWith("_CONFIG") => Space.Config,
        _ when block.StartsWith("HOME_ITEM_") || block.StartsWith("HOME_") && block.EndsWith("_ENTITY") => Space.Home,
        _ when block.StartsWith("RECIPE_") || block.StartsWith("ENGRAVE_") || block.StartsWith("RANDPROP_")
            || block.StartsWith("STONE_CHANGE_RECIPE") || block.StartsWith("HOME_FORMULAS_PRODUCE_") || block.StartsWith("LOANS_PRODUCE_")
            || block == "LIB_PRODUCE_RECIPE" => Space.Recipe,
        _ => Space.Essence,
    };
}
