using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MyAceEditor.Store;

namespace MyAceEditor.Tools;

/// <summary>Ferramentas de ciclo de vida do módulo: carregar, salvar, status.</summary>
[McpServerToolType]
public sealed class CommonTools
{
    [McpServerTool, Description("Mostra o estado atual: módulo carregado, caminho e mudanças pendentes.")]
    public static object get_status(ModuleStore store) => new
    {
        loaded = store.Current is not null,
        module = store.Current?.Name,
        path = store.CurrentPath,
        lists = store.Current?.Lists.Count ?? 0,
        pending_changes = store.Changes.Pending.Count
    };

    [McpServerTool, Description("Carrega um módulo a partir de um arquivo JSON no disco.")]
    public static object load_module(
        ModuleStore store,
        [Description("Caminho absoluto do arquivo .json do módulo")] string path)
    {
        var m = store.Load(path);
        return new { loaded = m.Name, lists = m.Lists.Count };
    }

    [McpServerTool, Description("Cria um módulo novo e vazio em memória.")]
    public static object new_module(ModuleStore store,
        [Description("Nome do novo módulo")] string name)
    {
        var m = store.NewModule(name);
        return new { created = m.Name };
    }

    [McpServerTool, Description("Salva o módulo atual no caminho de onde foi carregado.")]
    public static object save_module(ModuleStore store) => new { saved_to = store.Save() };

    [McpServerTool, Description("Salva o módulo atual em um novo caminho.")]
    public static object save_module_as(ModuleStore store,
        [Description("Caminho absoluto de destino")] string path)
        => new { saved_to = store.Save(path) };
}
