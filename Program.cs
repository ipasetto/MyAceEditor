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
        if (args.Length >= 3 && args[0] == "--testcfg") { TestCfg(args[1], args[2]); return; }

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001
        try { Application.SetColorMode(SystemColorMode.Dark); } catch { }
#pragma warning restore WFO5001
        Application.Run(new EditorForm());
    }

    private static void TestCfg(string cfgPath, string dataPath)
    {
        var cfg = ElementsCfg.Load(cfgPath);
        var r = new CfgElementsReader(dataPath);
        var mod = r.Parse(cfg);
        int items = mod.Lists.Sum(l => l.Items.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"cfg blocos: {cfg.Blocks.Count}  ·  listas com dados: {mod.Lists.Count}  ·  itens: {items}");
        sb.AppendLine($"pos {r.Position}/{r.Length}  ({100.0 * r.Position / r.Length:0.00}%)  parou: {r.StoppedAt ?? "EOF"}");
        foreach (var l in mod.Lists.Take(6))
        {
            var it = l.Items[0];
            var sample = string.Join(", ", it.Fields.Take(4).Select(f => $"{f.Key}={f.Value.Value}"));
            sb.AppendLine($"  {l.Name}: {l.Items.Count} itens | ex id={it.Id} \"{it.Name}\" [{sample}]");
        }
        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "testcfg.txt"), sb.ToString());
        Console.WriteLine(sb.ToString());
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
