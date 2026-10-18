using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MyAceEditor.Store;

namespace MyAceEditor.Tools;

/// <summary>Ferramentas de ciclo de vida do módulo: carregar, salvar, status.</summary>
[McpServerToolType]
public sealed class CommonTools
{
    [McpServerTool, Description("显示当前状态：已加载的模块、路径和待应用的修改。")]
    public static object get_status(ModuleStore store) => new
    {
        loaded = store.Current is not null,
        module = store.Current?.Name,
        path = store.CurrentPath,
        lists = store.Current?.Lists.Count ?? 0,
        pending_changes = store.Changes.Pending.Count
    };

    [McpServerTool, Description("从磁盘上的 JSON 文件加载模块。")]
    public static object load_module(
        ModuleStore store,
        [Description("模块 .json 文件的绝对路径")] string path)
    {
        var m = store.Load(path);
        return new { loaded = m.Name, lists = m.Lists.Count };
    }

    [McpServerTool, Description("在内存中创建一个空模块。")]
    public static object new_module(ModuleStore store,
        [Description("新模块名称")] string name)
    {
        var m = store.NewModule(name);
        return new { created = m.Name };
    }

    [McpServerTool, Description("将当前模块保存到原始加载路径。")]
    public static object save_module(ModuleStore store) => new { saved_to = store.Save() };

    [McpServerTool, Description("将当前模块另存到新路径。")]
    public static object save_module_as(ModuleStore store,
        [Description("目标绝对路径")] string path)
        => new { saved_to = store.Save(path) };
}
