using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyAceEditor.Desktop;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;
using MyAceEditor.Web;

namespace MyAceEditor;

internal static class Program
{
    private const string Url = "http://localhost:5174";

    // Modos do mesmo executável:
    //   (padrão)   janela desktop (WebView2) com a UI, servidor web embutido
    //   --server   só o servidor web (sem janela) em http://localhost:5174
    //   --mcp      servidor MCP por stdio, para um agente de IA
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--mcp")) { RunMcpMode(args).GetAwaiter().GetResult(); return; }

        if (args.Length >= 2 && args[0] == "--inspect") { InspectElements(args[1]); return; }

        var app = BuildWebApp(args);
        app.StartAsync().GetAwaiter().GetResult();   // garante o Kestrel ouvindo antes de abrir a janela

        if (args.Contains("--server"))
        {
            Console.WriteLine($"MyAceEditor (servidor) em {Url}  (Ctrl+C para sair)");
            app.WaitForShutdown();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(Url));

        app.StopAsync().GetAwaiter().GetResult();
    }

    private static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(Url);

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

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapEditorApi();
        return app;
    }

    // Diagnóstico: valida o parser do elements.data contra um arquivo real.
    // Uso: MyAceEditor.exe --inspect "D:\...\elements.data"  (gera inspect-report.txt)
    private static void InspectElements(string path)
    {
        var sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); Console.WriteLine(s); }

        using var r = new ElementsReader(path);
        Log($"arquivo      : {path}");
        Log($"tamanho      : {r.Length:N0} bytes");
        Log($"version      : 0x{r.Version:X8}");
        Log($"signature    : 0x{r.Signature:X8}  valida={r.SignatureValid}");

        var b0 = r.ReadBlock(ElementsSchema.PropertyList);
        Log($"\nbloco 0 '{b0.Name}': {b0.Count} registros de {ElementsSchema.PropertyList.RecordSize} bytes");
        foreach (var rec in b0.Records.Take(3))
            Log($"  id={rec["id"]} name=\"{rec["name"]}\" ({rec["f1"]},{rec["f2"]},{rec["f3"]},{rec["f4"]})");
        var last = b0.Records[^1];
        Log($"  … último: id={last["id"]} name=\"{last["name"]}\"");

        var (c1, _) = r.SkipBlock(ElementsSchema.EquipCategory); // só conta por enquanto
        Log($"\nbloco 1 '{ElementsSchema.EquipCategory.Name}': {c1} registros (layout parcial)");
        Log($"posição após 2 blocos: {r.Position:N0} / {r.Length:N0}");

        var outPath = Path.Combine(Directory.GetCurrentDirectory(), "inspect-report.txt");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"\nrelatório salvo em {outPath}");
    }

    private static async Task RunMcpMode(string[] args)
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
}
