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
        ["id"] = "ID", ["name"] = "Nome", ["level"] = "Nível", ["lvlup"] = "Subida de nível", ["exp"] = "Experiência", ["sp"] = "SP", ["hp"] = "HP", ["mp"] = "MP",
        ["vit"] = "Vitalidade", ["vital"] = "Vitalidade", ["energy"] = "Magia",
        ["strength"] = "Força", ["agility"] = "Destreza", ["agi"] = "Agilidade", ["magic"] = "Mágico", ["require"] = "Requer", ["required"] = "Requerido", ["need"] = "Necessário", ["damage"] = "Dano", ["dmg"] = "Dano", ["attack"] = "Ataque", ["atk"] = "Ataque",
        ["defence"] = "Defesa", ["defense"] = "Defesa", ["defend"] = "Defesa", ["magicdefence"] = "Defesa mágica",
        ["enhance"] = "Extra", ["ext"] = "Extra", ["extra"] = "Extra",
        ["min"] = "mín", ["max"] = "máx", ["low"] = "mín", ["high"] = "máx", ["speed"] = "Velocidade", ["range"] = "Alcance", ["radius"] = "Raio", ["duration"] = "Duração", ["life"] = "Vida",
        ["price"] = "Preço", ["value"] = "Valor", ["cost"] = "Custo", ["fee"] = "Taxa", ["buy"] = "Comprar", ["shop"] = "Loja", ["money"] = "Dinheiro", ["decompose"] = "Decompor", ["material"] = "Material", ["matter"] = "Material", ["matters"] = "Materiais",
        ["probability"] = "Chance", ["prob"] = "Chance", ["rate"] = "Taxa", ["ratio"] = "Razão", ["percent"] = "Percentual", ["factor"] = "Fator",
        ["num"] = "Qtd", ["count"] = "Quantidade", ["amount"] = "Quantidade", ["total"] = "Total", ["max"] = "máx", ["addon"] = "Atributo", ["addons"] = "Atributos", ["prop"] = "Propriedade", ["props"] = "Propriedades", ["rand"] = "Aleatório", ["unique"] = "Único", ["skill"] = "Habilidade", ["skills"] = "Habilidades", ["recipe"] = "Receita", ["make"] = "Fabricar", ["produce"] = "Produzir", ["gen"] = "Gerar", ["item"] = "Item", ["items"] = "Itens", ["goods"] = "Produtos", ["equip"] = "Equipar", ["weapon"] = "Arma", ["armor"] = "Armadura", ["stone"] = "Pedra", ["task"] = "Quest", ["tasks"] = "Quests", ["taks"] = "Quest", ["target"] = "Alvo", ["targets"] = "Alvos", ["idtarget"] = "ID do alvo",
        ["monster"] = "Monstro", ["npc"] = "NPC", ["player"] = "Jogador", ["enemy"] = "Inimigo", ["pet"] = "Pet", ["egg"] = "Ovo", ["mine"] = "Mina",
        ["controller"] = "Controlador", ["choice"] = "Escolha", ["entry"] = "Entrada", ["action"] = "Ação", ["model"] = "Modelo", ["texture"] = "Textura", ["icon"] = "Ícone", ["sfx"] = "Som", ["head"] = "Cabeça", ["skin"] = "Pele", ["hair"] = "Cabelo", ["mask"] = "Máscara", ["color"] = "Cor", ["fashion"] = "Moda", ["time"] = "Tempo", ["day"] = "Dia", ["month"] = "Mês", ["year"] = "Ano", ["hour"] = "Hora", ["start"] = "Início", ["end"] = "Fim",
        ["wait"] = "Espera", ["refresh"] = "Atualizar", ["period"] = "Período",
        ["wedding"] = "Casamento", ["session"] = "Sessão", ["scene"] = "Cena", ["map"] = "Mapa", ["area"] = "Área", ["world"] = "Mundo", ["pos"] = "Posição", ["team"] = "Equipe", ["faction"] = "Facção", ["factions"] = "Facções", ["contrib"] = "Contribuição", ["contribution"] = "Contribuição",
        ["tech"] = "Técnica", ["profession"] = "Profissão", ["war"] = "Guerra", ["title"] = "Título",
        ["building"] = "Construção", ["build"] = "Construir", ["buildup"] = "Construção", ["storage"] = "Armazém", ["service"] = "Serviço",
        ["routes"] = "Rotas", ["fly"] = "Voo", ["swim"] = "Nadar", ["walk"] = "Andar", ["run"] = "Correr", ["death"] = "Morte", ["die"] = "Morte", ["fail"] = "Falha", ["open"] = "Abrir", ["close"] = "Fechar", ["add"] = "Adicionar", ["increase"] = "Aumento",
        ["reduce"] = "Redução", ["inc"] = "Incremento", ["adjust"] = "Ajuste", ["diff"] = "Diferença", ["delta"] = "Delta",
        ["prefix"] = "Prefixo", ["suffix"] = "Sufixo", ["lost"] = "Perdido", ["common"] = "Comum", ["normal"] = "Normal",
        ["default"] = "Padrão", ["combo"] = "Combo", ["base"] = "Base", ["init"] = "Inicial",
        ["data"] = "Dados", ["info"] = "Info", ["desc"] = "Descrição", ["description"] = "Descrição", ["page"] = "Página", ["pages"] = "Páginas", ["list"] = "Lista", ["lists"] = "Listas", ["blacklist"] = "Lista negra", ["whitelist"] = "Lista branca",
        ["type"] = "Tipo", ["mode"] = "Modo", ["sub"] = "Sub", ["group"] = "Grupo", ["index"] = "Índice", ["params"] = "Parâmetros", ["param"] = "Parâmetro", ["reserved"] = "Reservado",
        ["role"] = "Papel", ["character"] = "Personagem", ["gender"] = "Gênero", ["gender_id"] = "Gênero",
        ["reputation"] = "Reputação", ["talk"] = "Fala", ["dialog"] = "Diálogo", ["switch"] = "Alternar", ["hide"] = "Ocultar", ["show"] = "Mostrar", ["upgrade"] = "Melhoria", ["out"] = "Saída", ["in"] = "Entrada",
        ["egg"] = "Ovo", ["buff"] = "Bônus", ["bind"] = "Vincular", ["bound"] = "Vinculado", ["crit"] = "Crítico", ["critical"] = "Crítico", ["hit"] = "Acerto",
        ["dodge"] = "Esquiva", ["ask"] = "Pedir", ["use"] = "Uso", ["once"] = "Uma vez",
        ["gain"] = "Ganho", ["scale"] = "Escala", ["power"] = "Poder", ["cooldown"] = "Recarga", ["multi"] = "Múltiplo", ["demulti"] = "Demora", ["tool"] = "Ferramenta", ["gold"] = "Metal", ["wood"] = "Madeira", ["var"] = "Variável", ["variable"] = "Variável", ["auto"] = "Automático", ["object"] = "Objeto", ["idx"] = "Índice",
        ["on"] = "Ao", ["to"] = "para", ["after"] = "Depois",
        ["per"] = "Por", ["v"] = "V", ["x"] = "X", ["y"] = "Y", ["z"] = "Z",
        ["bonus"] = "Bônus", ["limit"] = "Limite", ["protected"] = "Protegido", ["essence"] = "Essência",
        ["point"] = "Ponto",
        ["materials"] = "Materiais",
        ["drop"] = "Queda",
        ["engrave"] = "Gravação",
        ["refine"] = "Refino",
        ["degree"] = "Grau",
        ["length"] = "Comprimento",
        ["equipment"] = "Equipamento",
        ["skillpoint"] = "Ponto de habilidade",
        ["transmit"] = "Transmitir",
        ["major"] = "Principal",
        ["eng"] = "Eng",
        ["proc"] = "Processamento",
        ["class"] = "Classe",
        ["angro"] = "Agressão",
        ["force"] = "Força",
        ["all"] = "Todos",
        ["levelup"] = "Subida de nível",
        ["second"] = "Segundo",
        ["can"] = "Pode",
        ["times"] = "Vezes",
        ["delay"] = "Atraso",
        ["domain"] = "Domínio",
        ["related"] = "Relacionado",
        ["src"] = "Origem",
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
