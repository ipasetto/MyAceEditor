namespace MyAceEditor.PerfectWorld;

public enum PwType { Int32, UInt32, Float, WStr, Bytes }

/// <summary>Um campo de um registro. Count = nº de chars (WStr) ou nº de bytes (Bytes).</summary>
public sealed record PwField(string Name, PwType Type, int Count = 1)
{
    public int Size => Type switch
    {
        PwType.Int32 or PwType.UInt32 or PwType.Float => 4,
        PwType.WStr => Count * 2,
        PwType.Bytes => Count,
        _ => 0
    };
}

/// <summary>Definição de um tipo de bloco do elements.data (nome + layout do registro).</summary>
public sealed record PwBlock(string Name, IReadOnlyList<PwField> Fields)
{
    public int RecordSize => Fields.Sum(f => f.Size);
}

/// <summary>
/// Schemas conhecidos do elements.data. Começamos pelos blocos já mapeados do
/// elements.data v0x3000003C; novos blocos entram aqui conforme forem decifrados.
/// </summary>
public static class ElementsSchema
{
    // Bloco 0: lista de propriedades/efeitos (ex: "A_A11" ... "Aumenta o HP Máximo em 1%.")
    // Registro de 84 bytes: id + nome[32] + 4 inteiros.
    public static readonly PwBlock PropertyList = new("property_list", new[]
    {
        new PwField("id", PwType.Int32),
        new PwField("name", PwType.WStr, 32),
        new PwField("f1", PwType.Int32),
        new PwField("f2", PwType.Int32),
        new PwField("f3", PwType.Int32),
        new PwField("f4", PwType.Int32),
    });

    // Bloco 1: categorias de equipamento (ex: "Espada"). Layout ainda a confirmar
    // além do id + nome; marcado como parcial.
    public static readonly PwBlock EquipCategory = new("equip_category", new[]
    {
        new PwField("id", PwType.Int32),
        new PwField("name", PwType.WStr, 32),
    });
}
