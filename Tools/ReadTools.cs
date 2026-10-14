using System.ComponentModel;
using ModelContextProtocol.Server;
using MyAceEditor.Store;

namespace MyAceEditor.Tools;

/// <summary>Ferramentas somente-leitura: listar, buscar e inspecionar itens.</summary>
[McpServerToolType]
public sealed class ReadTools
{
    [McpServerTool, Description("Lista todas as listas do módulo com nome, grupo e quantidade de itens.")]
    public static object get_all_lists(ModuleStore store) =>
        store.Require().Lists.Select((l, idx) => new
        {
            index = idx, name = l.Name, group = l.Group, count = l.Items.Count
        });

    [McpServerTool, Description("Retorna os itens de uma lista pelo nome (id e nome de cada item).")]
    public static object get_items_by_list(ModuleStore store,
        [Description("Nome da lista")] string listName)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"Lista '{listName}' não encontrada.");
        return list.Items.Select(i => new { i.Id, i.Name });
    }

    [McpServerTool, Description("Retorna um item completo (todos os campos) por id dentro de uma lista.")]
    public static object get_item_by_id(ModuleStore store,
        [Description("Nome da lista")] string listName,
        [Description("Id do item")] string itemId)
    {
        var item = store.Require().FindListByName(listName)?.FindById(itemId)
                   ?? throw new InvalidOperationException($"Item {itemId} não encontrado em '{listName}'.");
        return new
        {
            item.Id,
            item.Name,
            fields = item.Fields.ToDictionary(f => f.Key, f => new { type = f.Value.Type.ToString(), f.Value.Value })
        };
    }

    [McpServerTool, Description("Busca itens em uma lista cujo nome contém o texto informado (case-insensitive).")]
    public static object search_items_by_name(ModuleStore store,
        [Description("Nome da lista")] string listName,
        [Description("Texto a procurar no nome")] string query)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"Lista '{listName}' não encontrada.");
        return list.Items
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => new { i.Id, i.Name });
    }

    [McpServerTool, Description("Busca itens em uma lista cujo campo informado tem exatamente o valor dado.")]
    public static object search_items_by_field_value(ModuleStore store,
        [Description("Nome da lista")] string listName,
        [Description("Nome do campo")] string fieldName,
        [Description("Valor esperado")] string value)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"Lista '{listName}' não encontrada.");
        return list.Items
            .Where(i => i.Fields.TryGetValue(fieldName, out var f) && f.Value == value)
            .Select(i => new { i.Id, i.Name });
    }
}
