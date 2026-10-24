using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Tradução com outro elements.data (o que o DataTranslator e o "Localization" do RAE fazem): para cada bloco
/// de mesmo nome e item de mesmo ID, os campos de texto UTF-16 (nomes, descrições, falas) que no outro arquivo
/// são diferentes e não vazios. Funciona entre versões diferentes (casa por nome de bloco e de campo).
/// </summary>
public static class ElementsTranslator
{
    public sealed record Change(ItemList Block, Item Item, string Field, string Value);

    /// <summary>Texto ainda na língua original do jogo (chinês, japonês, coreano ou russo) — ou seja, falta traduzir.</summary>
    public static bool LooksUntranslated(string s) =>
        s.Any(ch => ch is >= '\u3040' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\u0400' and <= '\u04FF' or >= '\uF900' and <= '\uFAFF');

    public static List<Change> Plan(Module current, ElementsCfg currentCfg, Module other)
    {
        var textFields = currentCfg.Blocks.ToDictionary(b => b.Name,
            b => b.Fields.Where(f => f.Type.StartsWith("wstring:", StringComparison.OrdinalIgnoreCase)).Select(f => f.Name).ToList(), StringComparer.OrdinalIgnoreCase);
        var changes = new List<Change>();
        foreach (var list in current.Lists)
        {
            if (other.FindListByName(list.Name) is not { } ol || !textFields.TryGetValue(list.Name, out var fields) || fields.Count == 0) continue;
            var byId = new Dictionary<string, Item>();
            foreach (var it in ol.Items) byId.TryAdd(it.Id, it);
            foreach (var it in list.Items)
            {
                if (!byId.TryGetValue(it.Id, out var oi)) continue;
                foreach (var f in fields)
                    if (oi.Fields.TryGetValue(f, out var ov) && it.Fields.TryGetValue(f, out var cv) && ov.Value.Length > 0 && ov.Value != cv.Value)
                        changes.Add(new(list, it, f, ov.Value));
            }
        }
        return changes;
    }
}
