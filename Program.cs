using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyAceEditor.Store;
using MyAceEditor.Web;

// Dois modos no MESMO executável:
//   (padrão)   app web local: serve a UI (wwwroot) + API REST em http://localhost:5174
//   --mcp      servidor MCP por stdio, para um agente de IA (igual ao Angelica Core)

if (args.Contains("--mcp"))
{
    await RunMcpMode(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://localhost:5174");

builder.Services.AddSingleton<ModuleStore>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// Carrega o módulo de exemplo na partida, se existir ao lado do executável.
var store = app.Services.GetRequiredService<ModuleStore>();
var sample = Path.Combine(AppContext.BaseDirectory, "sample.data");
if (File.Exists(sample)) store.Load(sample);

app.UseDefaultFiles();   // serve wwwroot/index.html em "/"
app.UseStaticFiles();
app.MapEditorApi();

Console.WriteLine("MyAceEditor rodando em http://localhost:5174  (Ctrl+C para sair)");
app.Run();


static async Task RunMcpMode(string[] args)
{
    var hb = Host.CreateApplicationBuilder(args);
    hb.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace); // stdout = protocolo
    hb.Services.AddSingleton<ModuleStore>();
    hb.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
    await hb.Build().RunAsync();
}
