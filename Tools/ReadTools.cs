using System.ComponentModel;
using ModelContextProtocol.Server;
using MyAceEditor.Store;

namespace MyAceEditor.Tools;

/// <summary>Ferramentas somente-leitura: listar, buscar e inspecionar itens.</summary>
[McpServerToolType]
public sealed class ReadTools
{
    [McpServerTool, Description("列出模块内所有列表的名称、分组和物品数量。")]
    public static object get_all_lists(ModuleStore store) =>
        store.Require().Lists.Select((l, idx) => new
        {
            index = idx, name = l.Name, group = l.Group, count = l.Items.Count
        });

    [McpServerTool, Description("根据列表名称返回其中所有物品的 ID 和名称。")]
    public static object get_items_by_list(ModuleStore store,
        [Description("列表名称")] string listName)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"未找到列表“{listName}”。");
        return list.Items.Select(i => new { i.Id, i.Name });
    }

    [McpServerTool, Description("根据 ID 返回列表中物品的完整信息（所有字段）。")]
    public static object get_item_by_id(ModuleStore store,
        [Description("列表名称")] string listName,
        [Description("物品 ID")] string itemId)
    {
        var item = store.Require().FindListByName(listName)?.FindById(itemId)
                   ?? throw new InvalidOperationException($"在列表“{listName}”中未找到物品 {itemId}。");
        return new
        {
            item.Id,
            item.Name,
            fields = item.Fields.ToDictionary(f => f.Key, f => new { type = f.Value.Type.ToString(), f.Value.Value })
        };
    }

    [McpServerTool, Description("在列表中搜索名称包含指定文本的物品（不区分大小写）。")]
    public static object search_items_by_name(ModuleStore store,
        [Description("列表名称")] string listName,
        [Description("名称中要查找的文本")] string query)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"未找到列表“{listName}”。");
        return list.Items
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => new { i.Id, i.Name });
    }

    [McpServerTool, Description("搜索列表中指定字段与给定值完全相同的物品。")]
    public static object search_items_by_field_value(ModuleStore store,
        [Description("列表名称")] string listName,
        [Description("字段名称")] string fieldName,
        [Description("目标值")] string value)
    {
        var list = store.Require().FindListByName(listName)
                   ?? throw new InvalidOperationException($"未找到列表“{listName}”。");
        return list.Items
            .Where(i => i.Fields.TryGetValue(fieldName, out var f) && f.Value == value)
            .Select(i => new { i.Id, i.Name });
    }
}
