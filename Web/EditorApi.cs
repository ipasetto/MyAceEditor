using MyAceEditor.Changes;
using MyAceEditor.Model;
using MyAceEditor.Store;

namespace MyAceEditor.Web;

/// <summary>
/// API REST que a UI consome. Espelha as tools MCP, mas numa interface HTTP:
/// a UI carrega o módulo real do disco e persiste mudanças de verdade.
/// </summary>
public static class EditorApi
{
    public static void MapEditorApi(this WebApplication app)
    {
        // Estado atual do módulo (carrega o de exemplo se nada estiver aberto).
        app.MapGet("/api/module", (ModuleStore store) => Results.Json(ToDto(store)));

        // Abrir um arquivo .data do disco.
        app.MapPost("/api/load", (ModuleStore store, LoadRequest req) =>
        {
            store.Load(req.Path);
            return Results.Json(ToDto(store));
        });

        // Aplicar um lote de mudanças (preview acumulado na UI) e salvar no disco.
        app.MapPost("/api/apply", (ModuleStore store, ApplyRequest req) =>
        {
            var module = store.Require();
            foreach (var c in req.Changes)
                store.Changes.Stage(Build(c));
            var result = store.Changes.Apply(module);
            var savedTo = store.CurrentPath is not null ? store.Save() : null;
            return Results.Json(new { applied = result.AppliedCount, savedTo, module = ToDto(store) });
        });

        // Salvar sem mudanças pendentes (ex: menu Arquivo > Salvar).
        app.MapPost("/api/save", (ModuleStore store, SaveRequest? req) =>
            Results.Json(new { savedTo = store.Save(req?.Path) }));

        // Abrir um elements.data do Perfect World (parse binário, 1ª metade já mapeada).
        app.MapPost("/api/load-elements", (ModuleStore store, LoadRequest req) =>
        {
            if (!File.Exists(req.Path))
                return Results.BadRequest(new { error = $"Arquivo não encontrado: {req.Path}" });
            using var reader = new PerfectWorld.ElementsReader(req.Path);
            if (!reader.SignatureValid)
                return Results.BadRequest(new { error = $"Assinatura inválida (0x{reader.Signature:X8}) — não parece um elements.data suportado." });
            var module = reader.ReadModule(PerfectWorld.ElementsProgram.Steps);
            module.Name = Path.GetFileName(req.Path);
            store.Adopt(module);
            return Results.Json(new { module = ToDto(store), lists = module.Lists.Count,
                items = module.Lists.Sum(l => l.Items.Count) });
        });
    }

    // ---- construção de mudanças a partir do DTO vindo da UI ----
    private static IChange Build(ChangeDto c) => c.Op switch
    {
        "add" => new AddItemChange(c.List, new Item
        {
            Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N")[..8] : c.Id!,
            Name = c.Name ?? "Novo item",
            Fields = MapFields(c.Fields)
        }),
        "set" => new SetFieldsChange(c.List, c.Id ?? "", MapFields(c.Fields)),
        "rem" => new RemoveItemChange(c.List, c.Id ?? ""),
        _ => throw new InvalidOperationException($"Operação desconhecida: {c.Op}")
    };

    private static Dictionary<string, Field> MapFields(Dictionary<string, FieldDto>? fields) =>
        fields?.ToDictionary(
            f => f.Key,
            f => Field.Of(Enum.Parse<FieldType>(f.Value.Type, ignoreCase: true), f.Value.Value))
        ?? new();

    // ---- projeção do módulo para o formato que a UI espera ----
    private static ModuleDto ToDto(ModuleStore store)
    {
        var m = store.Current ?? store.NewModule("Untitled");
        var lists = m.Lists.Select(l => new ListDto(l.Name, l.Group, l.Kind,
            l.Items.Select(i => new ItemDto(i.Id, i.Name,
                i.Fields.ToDictionary(f => f.Key, f => new FieldDto(f.Value.Type.ToString(), f.Value.Value)))
            ).ToList())).ToList();
        return new ModuleDto(m.Name, m.Version, store.CurrentPath, lists);
    }
}

// ---- DTOs (serializados em camelCase pela config do host) ----
public record ModuleDto(string Name, int Version, string? Path, List<ListDto> Lists);
public record ListDto(string Name, string Group, string Kind, List<ItemDto> Items);
public record ItemDto(string Id, string Name, Dictionary<string, FieldDto> Fields);
public record FieldDto(string Type, string Value);

public record LoadRequest(string Path);
public record SaveRequest(string? Path);
public record ApplyRequest(List<ChangeDto> Changes);
public record ChangeDto(string Op, string List, string? Id, string? Name, Dictionary<string, FieldDto>? Fields);
