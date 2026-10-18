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
    [McpServerTool, Description("预览：将创建物品的操作加入队列，返回临时 ID。")]
    public static object preview_add_item(ModuleStore store,
        [Description("要添加物品的列表名称")] string listName,
        [Description("新物品名称")] string name)
    {
        store.Require(); // garante módulo carregado
        var item = new Item { Name = name };
        var preview = store.Changes.Stage(new AddItemChange(listName, item));
        return new { staged = preview.Description, new_item_id = item.Id, preview.PendingCount };
    }

    [McpServerTool, Description("预览：将设置或更新物品字段的操作加入队列。类型：String、Int、Float、Bool、Reference。")]
    public static object preview_set_fields(ModuleStore store,
        [Description("列表名称")] string listName,
        [Description("物品 ID")] string itemId,
        [Description("要设置的字段：名称 -> { type, value }")] Dictionary<string, FieldInput> fields)
    {
        store.Require();
        var converted = fields.ToDictionary(
            f => f.Key,
            f => Field.Of(Enum.Parse<FieldType>(f.Value.Type, ignoreCase: true), f.Value.Value));
        var preview = store.Changes.Stage(new SetFieldsChange(listName, itemId, converted));
        return new { staged = preview.Description, preview.PendingCount };
    }

    [McpServerTool, Description("预览：将删除列表内物品的操作加入队列。")]
    public static object preview_remove_item(ModuleStore store,
        [Description("列表名称")] string listName,
        [Description("要删除的物品 ID")] string itemId)
    {
        store.Require();
        var preview = store.Changes.Stage(new RemoveItemChange(listName, itemId));
        return new { staged = preview.Description, preview.PendingCount };
    }

    [McpServerTool, Description("将全部待应用修改写入内存模块；随后使用 save_module 保存到磁盘。")]
    public static object apply_change_set(ModuleStore store)
    {
        var result = store.Changes.Apply(store.Require());
        return new { applied = result.AppliedCount, details = result.Applied };
    }

    [McpServerTool, Description("丢弃全部尚未应用的修改。")]
    public static object discard_change_set(ModuleStore store)
        => new { discarded = store.Changes.Discard() };
}

/// <summary>Formato de entrada de um campo vindo da IA.</summary>
public sealed class FieldInput
{
    [Description("字段类型：String、Int、Float、Bool 或 Reference")]
    public string Type { get; set; } = "String";

    [Description("以文本表示的字段值")]
    public string Value { get; set; } = "";
}
