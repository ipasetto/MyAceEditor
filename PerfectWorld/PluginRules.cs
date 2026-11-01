using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Regra de um plugin de evento (docs/PLUGINS.md). Etapa 1: "Monstro raro" — a cada <see cref="EveryMin"/> minutos o
/// programa do servidor sorteia <see cref="ChancePct"/>%; se cair, liga o controlador do npcgen (GMControlGame
/// active_npc_generator) no mapa <see cref="WorldTag"/> por <see cref="DurationMin"/> minutos, avisa todos (ChatBroadCast)
/// e no fim desliga (os monstros que sobraram somem) e avisa de novo. Mesmo código no editor e no programa (Linux).
/// </summary>
public sealed class PluginRule
{
    public string Id = "", Name = "", Type = "rare";
    public bool Enabled = true;
    public int WorldTag = 1, Controller, EveryMin = 30, DurationMin = 20;
    public double ChancePct = 10;
    public int StartChannel = 9, EndChannel = 9;          // GN_CHAT_CHANNEL: 9 anúncio, 8 sistema, 1 mundo (PW-KB §S31)
    public string StartText = "", EndText = "";
}

/// <summary>Arquivos do programa dos plugins no servidor (Linux/WSL) e leitura/gravação das regras em JSON.</summary>
public static class PluginFiles
{
    public const string Dir = "/home/gamed/myace";
    public const string Exe = Dir + "/myaceplugins", Config = Dir + "/plugins.json", Commands = Dir + "/plugins.cmd",
        State = Dir + "/plugins-state.json", Log = Dir + "/plugins.log";

    public static List<PluginRule> Parse(string json)
    {
        var list = new List<PluginRule>();
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json)?["plugins"] is not JsonArray arr) return list;
        foreach (var n in arr.OfType<JsonObject>())
        {
            int I(string k, int d) => n[k] is JsonValue v && v.TryGetValue(out int x) ? x : d;
            string S(string k) => n[k]?.GetValue<string>() ?? "";
            list.Add(new PluginRule
            {
                Id = S("id"), Name = S("name"), Type = S("type") is { Length: > 0 } t ? t : "rare",
                Enabled = n["enabled"] is not JsonValue e || !e.TryGetValue(out bool b) || b,
                WorldTag = I("worldTag", 1), Controller = I("controller", 0), EveryMin = I("everyMin", 30), DurationMin = I("durationMin", 20),
                ChancePct = n["chancePct"] is JsonValue c && c.TryGetValue(out double cp) ? cp : 10,
                StartChannel = I("startChannel", 9), EndChannel = I("endChannel", 9), StartText = S("startText"), EndText = S("endText"),
            });
        }
        return list;
    }

    public static string Write(IEnumerable<PluginRule> rules)
    {
        var arr = new JsonArray();
        foreach (var r in rules)
            arr.Add(new JsonObject
            {
                ["id"] = r.Id, ["name"] = r.Name, ["type"] = r.Type, ["enabled"] = r.Enabled, ["worldTag"] = r.WorldTag, ["controller"] = r.Controller,
                ["everyMin"] = r.EveryMin, ["chancePct"] = r.ChancePct, ["durationMin"] = r.DurationMin,
                ["startChannel"] = r.StartChannel, ["startText"] = r.StartText, ["endChannel"] = r.EndChannel, ["endText"] = r.EndText,
            });
        return new JsonObject { ["version"] = 1, ["plugins"] = arr }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Estado que o programa grava (id -> texto curto: "ativo até 21:40", "próximo sorteio 21:10", "pausado").</summary>
    public static Dictionary<string, string> ParseState(string json)
    {
        var d = new Dictionary<string, string>();
        try { if (JsonNode.Parse(json) is JsonObject o) foreach (var (k, v) in o) d[k] = v?.ToString() ?? ""; } catch (JsonException) { }
        return d;
    }
}
