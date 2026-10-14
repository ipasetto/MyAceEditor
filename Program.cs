using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyAceEditor.PerfectWorld;
using MyAceEditor.Store;
using MyAceEditor.UI;
using System.Text;

namespace MyAceEditor;

internal static class Program
{
    // Modos:
    //   (padrão)   editor desktop nativo (WinForms)
    //   --mcp      servidor MCP por stdio, para um agente de IA
    //   --inspect <elements.data>   diagnóstico do parser do Perfect World
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--mcp")) { RunMcpMode(args).GetAwaiter().GetResult(); return; }
        if (args.Length >= 2 && args[0] == "--inspect") { InspectElements(args[1]); return; }

        ApplicationConfiguration.Initialize();
        Application.Run(new EditorForm());
    }

    private static async Task RunMcpMode(string[] args)
    {
        var hb = Host.CreateApplicationBuilder(args);
        hb.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        hb.Services.AddSingleton<ModuleStore>();
        hb.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
        await hb.Build().RunAsync();
    }

    private static void InspectElements(string path)
    {
        var sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); Console.WriteLine(s); }

        using var r = new ElementsReader(path);
        Log($"arquivo   : {path}");
        Log($"tamanho   : {r.Length:N0} bytes");
        Log($"version   : 0x{r.Version:X8}");
        Log($"signature : 0x{r.Signature:X8}  valida={r.SignatureValid}");

        var steps = r.ExecuteProgram(ElementsProgram.Steps);
        var comDados = steps.Count(s => s.Count > 0);
        Log($"\npassos: {steps.Count}  ·  blocos com dados: {comDados}  ·  posição {r.Position:N0}/{r.Length:N0}");

        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "inspect-report.txt"), sb.ToString());
    }
}
