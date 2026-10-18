using System.Text.Json;
using MyAceEditor.Changes;
using MyAceEditor.Model;

namespace MyAceEditor.Store;

/// <summary>
/// Estado da sessão: o módulo carregado, de onde veio, e o change set atual.
/// Um único serviço injetado nas tools (singleton). Persiste em JSON.
/// </summary>
public sealed class ModuleStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public Module? Current { get; private set; }
    public string? CurrentPath { get; private set; }
    public ChangeSet Changes { get; } = new();

    public Module Require() =>
        Current ?? throw new InvalidOperationException("尚未加载模块，请先使用 load_module。");

    public Module Load(string path)
    {
        var json = File.ReadAllText(path);
        Current = JsonSerializer.Deserialize<Module>(json, JsonOpts)
                  ?? throw new InvalidOperationException("模块反序列化失败。");
        CurrentPath = Path.GetFullPath(path);
        Changes.Discard();
        return Current;
    }

    public Module NewModule(string name)
    {
        Current = new Module { Name = name };
        CurrentPath = null;
        Changes.Discard();
        return Current;
    }

    /// <summary>Adota um módulo já construído (ex: parseado de um elements.data).
    /// CurrentPath fica nulo de propósito, para não sobrescrever o arquivo original num Salvar.</summary>
    public Module Adopt(Module module)
    {
        Current = module;
        CurrentPath = null;
        Changes.Discard();
        return Current;
    }

    public string Save(string? path = null)
    {
        var module = Require();
        var target = path ?? CurrentPath
            ?? throw new InvalidOperationException("尚未设置目标路径，请使用 save_module_as。");
        var json = JsonSerializer.Serialize(module, JsonOpts);
        File.WriteAllText(target, json);
        CurrentPath = Path.GetFullPath(target);
        return CurrentPath;
    }
}
