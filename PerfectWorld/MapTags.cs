using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Liga a pasta de mapa do servidor (gamed\config\&lt;pasta&gt;) ao mapa do cliente (element\maps\&lt;mapa&gt;), PW-KB §S22:
/// gs.conf dá a tag de cada pasta ([World_x]/[Instance_x]: tag + base_path) e o configs\instance.txt do cliente
/// (configs.pck, UTF-16, CECInstance::Load) dá o mapa e o tamanho em km de cada tag. Ex.: b30..b35 = tags 230..235,
/// todas desenhadas no mapa b30 do cliente.
/// </summary>
public static class MapTags
{
    public sealed record ClientMap(int Tag, string Path, int Rows, int Cols);

    /// <summary>pasta do servidor → tag, lido do gs.conf ao lado de gamed\config (null se não achar).</summary>
    public static Dictionary<string, int>? ServerTags(string configDir)
    {
        var conf = new[] { Path.Combine(configDir, "..", "gs.conf"), Path.Combine(configDir, "gs.conf") }.FirstOrDefault(File.Exists);
        if (conf is null) return null;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int? tag = null; string? path = null;
        void Flush() { if (tag is int t && path is { Length: > 0 } p) map.TryAdd(p, t); tag = null; path = null; }
        foreach (var raw in File.ReadLines(conf, Encoding.Latin1))
        {
            var line = raw.Split('#', ';')[0].Trim();
            if (line.StartsWith('[')) { Flush(); continue; }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim(); var val = line[(eq + 1)..].Trim();
            if (key.Equals("tag", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var t)) tag = t;
            else if (key.Equals("base_path", StringComparison.OrdinalIgnoreCase)) path = val.TrimEnd('/', '\\').Split('/', '\\')[^1];
        }
        Flush();
        return map;
    }

    /// <summary>tag → mapa do cliente, do configs\instance.txt dentro do configs.pck (null se não achar).</summary>
    public static Dictionary<int, ClientMap>? ClientMaps(string elementDir)
    {
        try
        {
            var pckPath = Path.Combine(elementDir, "configs.pck");
            if (!File.Exists(pckPath)) return null;
            using var pck = new PckArchive(pckPath);
            if (pck.Find(@"configs\instance.txt") is not { } e) return null;
            var bytes = pck.Read(e);
            var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.Unicode.GetString(bytes);
            text = Regex.Replace(text, @"//[^\n]*", "");
            var map = new Dictionary<int, ClientMap>();
            foreach (Match m in Regex.Matches(text, @"""[^""]*""\s*\{\s*(\d+)\s*""([^""]+)""\s*(\d+)\s*,\s*(\d+)"))
            {
                int tag = int.Parse(m.Groups[1].Value);
                map.TryAdd(tag, new ClientMap(tag, m.Groups[2].Value, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)));
            }
            return map;
        }
        catch { return null; }
    }

    /// <summary>Mapa do cliente para a pasta do servidor: a mesma pasta, se existir; senão pela tag (gs.conf → instance.txt).</summary>
    public static string ClientMapFor(string? configDir, string elementDir, string serverMap)
    {
        if (Directory.Exists(Path.Combine(elementDir, "maps", serverMap))) return serverMap;
        if (configDir is not null && ServerTags(configDir) is { } tags && tags.TryGetValue(serverMap, out var tag)
            && ClientMaps(elementDir) is { } maps && maps.TryGetValue(tag, out var cm))
            return cm.Path;
        return serverMap;
    }
}
