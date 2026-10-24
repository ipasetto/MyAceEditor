using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Ponto de teleporte do mundo (maps\world\world_targets.txt do cliente).</summary>
public sealed record WorldTarget(int Id, string Name, int World, string Position, int Region);

/// <summary>
/// Pontos de teleporte com nome, lidos do cliente: element\maps\world\world_targets.txt (UTF-16), uma linha por ponto:
/// ID &lt;TAB&gt; "nome" &lt;TAB&gt; mapa &lt;TAB&gt; "x,y,z" &lt;TAB&gt; região. São os IDs de id_to_discover (NPC) e
/// targets_N_idTarget (NPC_TRANSMIT_SERVICE). O servidor tem o par world_targets.sev.
/// </summary>
public static class WorldTargets
{
    public static string? FindFile(string elementsPath)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
            foreach (var c in new[] { Path.Combine(dir, "maps", "world", "world_targets.txt"), Path.Combine(dir, "element", "maps", "world", "world_targets.txt") })
                if (File.Exists(c)) return c;
        return null;
    }

    public static Dictionary<int, WorldTarget> Load(string file)
    {
        var b = File.ReadAllBytes(file);
        var text = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
            : b.Length >= 2 && b[1] == 0 ? Encoding.Unicode.GetString(b) : Encoding.GetEncoding(936).GetString(b);
        var d = new Dictionary<int, WorldTarget>();
        foreach (Match m in Regex.Matches(text, "(?m)^(\\d+)\\t\"([^\"]*)\"\\t(\\d+)\\t\"([^\"]*)\"\\t(\\d+)"))
            d[int.Parse(m.Groups[1].Value)] = new(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), int.Parse(m.Groups[3].Value), m.Groups[4].Value, int.Parse(m.Groups[5].Value));
        return d;
    }
}
