namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Palavras dos nomes de campo do elements.data, em português (issue #9). Cada rótulo é montado palavra por
/// palavra: "require_strength" → "Requer Força". Palavras fora do dicionário aparecem como estão.
/// Chaves em minúsculas. Sufixo numérico (num1, hp50) usa a palavra base: "num1" → "Qtd 1".
/// </summary>
public static class FieldTokens
{
    public static readonly Dictionary<string, string> Pt = new()
    {
        ["id"] = "ID", ["realname"] = "Nome real", ["name"] = "Nome", ["level"] = "Nível", ["lvl"] = "Nível",
        ["lvlup"] = "Subida de nível", ["exp"] = "Experiência", ["sp"] = "SP", ["hp"] = "HP", ["mp"] = "MP",
        ["vit"] = "Vitalidade", ["vital"] = "Vitalidade", ["tili"] = "Constituição", ["energy"] = "Magia",
        ["strength"] = "Força", ["agility"] = "Destreza", ["agi"] = "Agilidade", ["magic"] = "Mágico", ["mag"] = "Mágico",
        ["require"] = "Requer", ["required"] = "Requerido", ["need"] = "Necessário", ["requirement"] = "Requisito",
        ["damage"] = "Dano", ["damages"] = "Danos", ["dmg"] = "Dano", ["attack"] = "Ataque", ["atk"] = "Ataque",
        ["defence"] = "Defesa", ["defense"] = "Defesa", ["defences"] = "Defesas", ["defend"] = "Defesa", ["magicdefence"] = "Defesa mágica",
        ["physic"] = "Físico", ["physical"] = "Físico", ["enhance"] = "Extra", ["ext"] = "Extra", ["extra"] = "Extra",
        ["min"] = "mín", ["max"] = "máx", ["low"] = "mín", ["high"] = "máx", ["highest"] = "maior", ["slow"] = "lento", ["slowest"] = "mais lento", ["fast"] = "rápido", ["fastest"] = "mais rápido",
        ["speed"] = "Velocidade", ["range"] = "Alcance", ["radius"] = "Raio", ["cool"] = "Recarga", ["duration"] = "Duração", ["life"] = "Vida",
        ["price"] = "Preço", ["value"] = "Valor", ["cost"] = "Custo", ["fee"] = "Taxa", ["buy"] = "Comprar", ["sell"] = "Vender",
        ["shop"] = "Loja", ["money"] = "Dinheiro", ["repair"] = "Reparo", ["repairfee"] = "Reparo",
        ["durability"] = "Durabilidade", ["decompose"] = "Decompor", ["material"] = "Material", ["matter"] = "Material", ["matters"] = "Materiais",
        ["probability"] = "Chance", ["prob"] = "Chance", ["rate"] = "Taxa", ["ratio"] = "Razão", ["percent"] = "Percentual", ["factor"] = "Fator",
        ["num"] = "Qtd", ["count"] = "Quantidade", ["amount"] = "Quantidade", ["total"] = "Total", ["max"] = "máx", ["pile"] = "Pilha",
        ["addon"] = "Atributo", ["addons"] = "Atributos", ["prop"] = "Propriedade", ["props"] = "Propriedades", ["properties"] = "Propriedades",
        ["rand"] = "Aleatório", ["rands"] = "Aleatórios", ["randprop"] = "Atributo aleatório", ["unique"] = "Único", ["uniques"] = "Únicos",
        ["skill"] = "Habilidade", ["skills"] = "Habilidades", ["skill2"] = "Habilidade 2", ["skill3"] = "Habilidade 3",
        ["recipe"] = "Receita", ["make"] = "Fabricar", ["produce"] = "Produzir", ["gen"] = "Gerar", ["generate"] = "Gerar",
        ["item"] = "Item", ["items"] = "Itens", ["goods"] = "Produtos", ["equip"] = "Equipar", ["equips"] = "Equipamentos", ["equipments"] = "Equipamentos",
        ["weapon"] = "Arma", ["armor"] = "Armadura", ["projectile"] = "Projétil", ["stone"] = "Pedra", ["socket"] = "Encaixe", ["socket0"] = "Encaixe 0", ["socket1"] = "Encaixe 1",
        ["task"] = "Quest", ["tasks"] = "Quests", ["taks"] = "Quest", ["target"] = "Alvo", ["targets"] = "Alvos", ["idtarget"] = "ID do alvo",
        ["monster"] = "Monstro", ["npc"] = "NPC", ["player"] = "Jogador", ["enemy"] = "Inimigo", ["pet"] = "Pet", ["egg"] = "Ovo", ["mine"] = "Mina",
        ["monsters"] = "Monstros", ["aggro"] = "Agressão", ["aggros"] = "Agressões", ["aggressive"] = "Agressivo", ["strategy"] = "Estratégia",
        ["controller"] = "Controlador", ["choice"] = "Escolha", ["entry"] = "Entrada", ["action"] = "Ação", ["trigger"] = "Gatilho",
        ["file"] = "Arquivo", ["files"] = "Arquivos", ["model"] = "Modelo", ["model1"] = "Modelo 1", ["texture"] = "Textura", ["tex"] = "Textura",
        ["icon"] = "Ícone", ["hitgfx"] = "Efeito de acerto", ["gfx"] = "Efeito", ["sfx"] = "Som", ["pllfiles"] = "Arquivos PLL",
        ["head"] = "Cabeça", ["skin"] = "Pele", ["hair"] = "Cabelo", ["part"] = "Parte", ["mask"] = "Máscara", ["shape"] = "Forma",
        ["color"] = "Cor", ["dye"] = "Tingir", ["moustache"] = "Bigode", ["fashion"] = "Moda", ["hatched"] = "Chocado",
        ["probability1"] = "Chance 1", ["hp25"] = "HP 25%", ["hp50"] = "HP 50%", ["hp75"] = "HP 75%",
        ["time"] = "Tempo", ["day"] = "Dia", ["month"] = "Mês", ["year"] = "Ano", ["hour"] = "Hora", ["start"] = "Início", ["end"] = "Fim",
        ["wait"] = "Espera", ["pertime"] = "Por tempo", ["frequency"] = "Frequência", ["refresh"] = "Atualizar", ["period"] = "Período",
        ["wedding"] = "Casamento", ["session"] = "Sessão", ["scene"] = "Cena", ["map"] = "Mapa", ["area"] = "Área", ["location"] = "Local",
        ["world"] = "Mundo", ["pos"] = "Posição", ["position"] = "Posição", ["direction"] = "Direção", ["left"] = "Esquerda", ["right"] = "Direita",
        ["team"] = "Equipe", ["faction"] = "Facção", ["factions"] = "Facções", ["contrib"] = "Contribuição", ["contribution"] = "Contribuição",
        ["tech"] = "Técnica", ["technology"] = "Tecnologia", ["profession"] = "Profissão", ["war"] = "Guerra", ["title"] = "Título",
        ["building"] = "Construção", ["build"] = "Construir", ["buildup"] = "Construção", ["storage"] = "Armazém", ["service"] = "Serviço",
        ["services"] = "Serviços", ["route"] = "Rota", ["routes"] = "Rotas", ["transport"] = "Transporte", ["congregate"] = "Congregar",
        ["fly"] = "Voo", ["swim"] = "Nadar", ["walk"] = "Andar", ["run"] = "Correr", ["launch"] = "Lançar", ["rush"] = "Investida",
        ["combat"] = "Combate", ["fight"] = "Luta", ["death"] = "Morte", ["die"] = "Morte", ["dead"] = "Morte", ["fail"] = "Falha", ["faling"] = "Falha",
        ["succeed"] = "Sucesso", ["accept"] = "Aceitar", ["open"] = "Abrir", ["close"] = "Fechar", ["add"] = "Adicionar", ["increase"] = "Aumento",
        ["reduce"] = "Redução", ["inc"] = "Incremento", ["adjust"] = "Ajuste", ["diff"] = "Diferença", ["delta"] = "Delta",
        ["discover"] = "Descobrir", ["prefix"] = "Prefixo", ["suffix"] = "Sufixo", ["lost"] = "Perdido", ["common"] = "Comum", ["normal"] = "Normal",
        ["default"] = "Padrão", ["fixed"] = "Fixo", ["combined"] = "Combinado", ["combo"] = "Combo", ["base"] = "Base", ["init"] = "Inicial",
        ["data"] = "Dados", ["info"] = "Info", ["help"] = "Ajuda", ["desc"] = "Descrição", ["description"] = "Descrição", ["title2"] = "Título",
        ["page"] = "Página", ["pages"] = "Páginas", ["list"] = "Lista", ["lists"] = "Listas", ["blacklist"] = "Lista negra", ["whitelist"] = "Lista branca",
        ["type"] = "Tipo", ["mode"] = "Modo", ["sub"] = "Sub", ["group"] = "Grupo", ["set"] = "Conjunto", ["sets"] = "Conjuntos",
        ["entries"] = "Entradas", ["index"] = "Índice", ["params"] = "Parâmetros", ["param"] = "Parâmetro", ["reserved"] = "Reservado",
        ["role"] = "Papel", ["user"] = "Usuário", ["character"] = "Personagem", ["gender"] = "Gênero", ["gender_id"] = "Gênero",
        ["food"] = "Comida", ["hornor"] = "Honra", ["honor"] = "Honra", ["reputation"] = "Reputação", ["death2"] = "Morte",
        ["wear"] = "Desgaste", ["talk"] = "Fala", ["dialog"] = "Diálogo", ["expression"] = "Expressão", ["emotion"] = "Emoção",
        ["camera"] = "Câmera", ["switch"] = "Alternar", ["hide"] = "Ocultar", ["show"] = "Mostrar", ["visualize"] = "Visualizar",
        ["uninstall"] = "Desinstalar", ["install"] = "Instalar", ["upgrade"] = "Melhoria", ["out"] = "Saída", ["in"] = "Entrada",
        ["egg"] = "Ovo", ["egg2"] = "Ovo", ["captured"] = "Capturado", ["hatch"] = "Chocar", ["inhabit"] = "Habitar", ["plant"] = "Planta",
        ["buff"] = "Bônus", ["bind"] = "Vincular", ["bound"] = "Vinculado", ["unbind"] = "Desvincular", ["accept2"] = "Aceitar",
        ["immune"] = "Imunidade", ["immunity"] = "Imunidade", ["crit"] = "Crítico", ["critical"] = "Crítico", ["hit"] = "Acerto",
        ["dodge"] = "Esquiva", ["ask"] = "Pedir", ["pick"] = "Pegar", ["use"] = "Uso", ["used"] = "Usado", ["once"] = "Uma vez",
        ["gain"] = "Ganho", ["spawn"] = "Gerar", ["patroll"] = "Patrulha", ["patrol"] = "Patrulha", ["amplify"] = "Ampliar",
        ["scale"] = "Escala", ["size"] = "Tamanho", ["power"] = "Poder", ["cooldown"] = "Recarga", ["repeat"] = "Repetir",
        ["multi"] = "Múltiplo", ["demulti"] = "Demora", ["ask2"] = "Pedir", ["hp2"] = "HP", ["npcgen"] = "NPCGen",
        ["dye2"] = "Tingir", ["tool"] = "Ferramenta", ["eliminate"] = "Eliminar", ["fire"] = "Fogo", ["water"] = "Água", ["earth"] = "Terra",
        ["gold"] = "Metal", ["wood"] = "Madeira", ["permenent"] = "Permanente", ["permanent"] = "Permanente", ["right2"] = "Direita",
        ["combat2"] = "Combate", ["uninvisible"] = "Visível", ["invisible"] = "Invisível", ["fixed2"] = "Fixo",
        ["var"] = "Variável", ["variable"] = "Variável", ["expression2"] = "Expressão", ["unsymmetrical"] = "Assimétrico",
        ["proxy"] = "Proxy", ["skin2"] = "Pele", ["pick2"] = "Pegar", ["sfx2"] = "Som", ["trigger2"] = "Gatilho", ["facepill"] = "Pílula facial",
        ["auto"] = "Automático", ["fight2"] = "Luta", ["petname"] = "Nome do pet", ["petlearnskill"] = "Habilidade aprendível", ["sub2"] = "Sub",
        ["build2"] = "Construção", ["object"] = "Objeto", ["objects"] = "Objetos", ["stone2"] = "Pedra", ["idx"] = "Índice",
        ["direction2"] = "Direção", ["walk2"] = "Andar", ["slow2"] = "Lento", ["pile2"] = "Pilha", ["hatch2"] = "Chocar",
        ["title2b"] = "Título", ["add2"] = "Adicionar", ["in2"] = "Em", ["on"] = "Ao", ["to"] = "para", ["after"] = "Depois",
        ["before"] = "Antes", ["per"] = "Por", ["by"] = "por", ["of"] = "de", ["no"] = "Nº", ["a"] = "A", ["b"] = "B", ["c"] = "C",
        ["d"] = "D", ["h"] = "H", ["s"] = "S", ["v"] = "V", ["x"] = "X", ["y"] = "Y", ["z"] = "Z",
        ["socket4"] = "Encaixe 4", ["socket3"] = "Encaixe 3", ["socket2"] = "Encaixe 2", ["num0"] = "Qtd 0", ["num2"] = "Qtd 2", ["num3"] = "Qtd 3", ["num4"] = "Qtd 4",
        ["icon2"] = "Ícone 2", ["icon3"] = "Ícone 3", ["icon4"] = "Ícone 4", ["model3"] = "Modelo 3", ["model2"] = "Modelo 2",
        ["id0"] = "ID 0", ["id1"] = "ID 1", ["addon0"] = "Atributo 0", ["addon2"] = "Atributo 2",
        ["discount"] = "Desconto", ["bonus"] = "Bônus", ["penalty"] = "Penalidade", ["limit"] = "Limite", ["limits"] = "Limites",
        ["rush2"] = "Investida", ["protect"] = "Proteção", ["protected"] = "Protegido", ["sell2"] = "Vender",
    };

    /// <summary>Palavra em português, ou null se não estiver no dicionário. Trata sufixo numérico (num1 → Qtd 1).</summary>
    public static string? Lookup(string lower)
    {
        if (Pt.TryGetValue(lower, out var t)) return t;
        var m = System.Text.RegularExpressions.Regex.Match(lower, @"^([a-z]+?)(\d+)$");
        if (m.Success && Pt.TryGetValue(m.Groups[1].Value, out var b)) return b + " " + m.Groups[2].Value;
        return null;
    }
}
