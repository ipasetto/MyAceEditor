using System.ComponentModel;
using ModelContextProtocol.Server;
using MyAceEditor.Changes;
using MyAceEditor.Model;
using MyAceEditor.Store;

namespace MyAceEditor.Tools;

/// <summary>
/// Ferramentas de escrita. TODAS são "preview_*": elas só ENFILEIRAM a mudança
/// no change set. Nada é gravado no módulo até apply_change_set().
/// Esse é o mesmo padrão de segurança do Angelica Core.
/// </summary>
[McpServerToolType]
public sealed class MutationTools
{
    [McpServerTool, Description("PREVIEW: enfileira a criação de um item novo numa lista. Retorna o id provisório.")]
    public static object preview_add_item(ModuleStore store,
        [Description("Nome da lista onde adicionar")] string listName,
        [Description("Nome do novo item")] string name)
    {
        store.Require(); // garante módulo carregado
        var item = new Item { Name = name };
        var preview = store.Changes.Stage(new AddItemChange(listName, item));
        return new { staged = preview.Description, new_item_id = item.Id, preview.PendingCount };
    }

    [McpServerTool, Description("PREVIEW: enfileira a definição/atualização de campos de um item. Tipos: String, Int, Float, Bool, Reference.")]
    public static object preview_set_fields(ModuleStore store,
        [Description("Nome da lista")] string listName,
        [Description("Id do item")] string itemId,
        [Description("Campos a definir: nome -> { type, value }")] Dictionary<string, FieldInput> fields)
    {
        store.Require();
        var converted = fields.ToDictionary(
            f => f.Key,
            f => Field.Of(Enum.Parse<FieldType>(f.Value.Type, ignoreCase: true), f.Value.Value));
        var preview = store.Changes.Stage(new SetFieldsChange(listName, itemId, converted));
        return new { staged = preview.Description, preview.PendingCount };
    }

    [McpServerTool, Description("PREVIEW: enfileira a remoção de um item de uma lista.")]
    public static object preview_remove_item(ModuleStore store,
        [Description("Nome da lista")] string listName,
        [Description("Id do item a remover")] string itemId)
    {
        store.Require();
        var preview = store.Changes.Stage(new RemoveItemChange(listName, itemId));
        return new { staged = preview.Description, preview.PendingCount };
    }

    [McpServerTool, Description("Aplica TODAS as mudanças pendentes ao módulo (não salva em disco; use save_module depois).")]
    public static object apply_change_set(ModuleStore store)
    {
        var result = store.Changes.Apply(store.Require());
        return new { applied = result.AppliedCount, details = result.Applied };
    }

    [McpServerTool, Description("Descarta todas as mudanças pendentes sem aplicá-las.")]
    public static object discard_change_set(ModuleStore store)
        => new { discarded = store.Changes.Discard() };
}

/// <summary>Formato de entrada de um campo vindo da IA.</summary>
public sealed class FieldInput
{
    [Description("Tipo do campo: String, Int, Float, Bool ou Reference")]
    public string Type { get; set; } = "String";

    [Description("Valor do campo como texto")]
    public string Value { get; set; } = "";
}
