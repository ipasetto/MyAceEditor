namespace MyAceEditor.Model;

/// <summary>
/// Modelo de dados genérico, no mesmo espírito do Angelica Core:
/// Módulo -> Listas -> Itens -> Campos (tipados).
/// Em vez de classes fixas (NPC, Quest...), tudo é um "Item" = saco de campos.
/// É isso que permite editar qualquer coisa do jogo com as mesmas ferramentas.
/// </summary>
public sealed class Module
{
    public string Name { get; set; } = "Untitled";
    public int Version { get; set; } = 1;
    public List<ItemList> Lists { get; set; } = new();

    /// <summary>Origem binária (ex.: layout do elements.data) para gravar de volta. Não vai para o JSON.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public object? Source { get; set; }

    public ItemList? FindListByName(string name) =>
        Lists.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public ItemList? FindListByIndex(int idx) =>
        idx >= 0 && idx < Lists.Count ? Lists[idx] : null;
}

/// <summary>Uma lista agrupa itens do mesmo "tipo" (ex: NPCs, Tasks, Triggers).</summary>
public sealed class ItemList
{
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";   // agrupamento lógico (ex: "AI", "World")
    public string Kind { get; set; } = "res";  // tipo visual/ícone: npc, task, trigger, res...
    public List<Item> Items { get; set; } = new();

    public Item? FindById(string id) =>
        Items.FirstOrDefault(i => i.Id == id);
}

/// <summary>Um item é só um id + um conjunto de campos tipados.</summary>
public sealed class Item
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public Dictionary<string, Field> Fields { get; set; } = new();

    /// <summary>
    /// Bytes originais do registro (formatos binários). Ao gravar, partimos deles e só recodificamos
    /// os campos alterados — garante round-trip byte-idêntico. Não vai para o JSON.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? Raw { get; set; }
}

public enum FieldType { String, Int, Float, Bool, Reference }

/// <summary>Um campo carrega o tipo e o valor. "Reference" aponta para o Id de outro item.</summary>
public sealed class Field
{
    public FieldType Type { get; set; } = FieldType.String;
    public string Value { get; set; } = "";

    public static Field Of(FieldType type, string value) => new() { Type = type, Value = value };
}
