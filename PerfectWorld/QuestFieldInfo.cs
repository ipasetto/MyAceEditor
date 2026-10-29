using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Rótulo, dica e área de cada campo de quest pela chave do TaskLayout ("m_Award_S.m_CandItems[0].m_ulItemTemplId").
/// Português em cfg/quest_fields.pt.tsv; outros idiomas pelas chaves qf.&lt;campo&gt;(.h), qs.&lt;escopo&gt;(.h), qg.&lt;área&gt;.
/// Nome exibido = escopos e campo com o número da posição ("Prêmio (sucesso) › Grupo de itens 1 › Item 2 › Item").
/// </summary>
public static partial class QuestFieldInfo
{
    sealed record Entry(string Area, string Label, string Hint);

    static readonly Dictionary<string, Entry> Leaves = new(), Scopes = new();
    static readonly List<string> Areas = new();
    static readonly Dictionary<string, string> AreaLabels = new();

    static QuestFieldInfo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg", "quest_fields.pt.tsv");
        if (!File.Exists(path)) return;
        string area = "";
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('\t');
            string Col(int i) => i < p.Length ? p[i] : "";
            if (line[0] == '@') { area = p[0][1..]; Areas.Add(area); AreaLabels[area] = Col(1); }
            else if (line[0] == '$') Scopes[p[0][1..]] = new Entry(area, Col(1), Col(2));
            else Leaves[p[0]] = new Entry(area, Col(1), Col(2));
        }
    }

    static string T(string key, string pt) => FieldKinds.Translate(key, pt);

    /// <summary>Partes do caminho: nome sem índices + índices ("m_CandItems[0][1]" → m_CandItems, [0, 1]).</summary>
    static IEnumerable<(string Name, int[] Index)> Parts(string key)
    {
        int hash = key.IndexOf('#');
        if (hash >= 0) key = key[..hash];
        foreach (var seg in key.Split('.'))
        {
            int b = seg.IndexOf('[');
            var name = b < 0 ? seg : seg[..b];
            var idx = b < 0 ? Array.Empty<int>() : Index().Matches(seg).Select(m => int.Parse(m.Groups[1].Value)).ToArray();
            yield return (name, idx);
        }
    }

    static string LeafName(string name) => ParamN().IsMatch(name) ? "Param" : name;

    static string LeafLabel(string name)
    {
        name = LeafName(name);
        if (Leaves.TryGetValue(name, out var e)) return T("qf." + name, e.Label);
        if (name.EndsWith("Pointer"))
            return string.Format(T("qf._pointer", "{0} (ponteiro interno)"), Scopes.ContainsKey(name[..^7]) ? ScopeLabel(name[..^7]) : LeafLabel(name[..^7]));
        return Humanize(name);
    }

    static string ScopeLabel(string name) => Scopes.TryGetValue(name, out var e) ? T("qs." + name, e.Label) : LeafLabel(name);

    static string Numbered(string label, int[] idx) => idx.Length == 0 ? label : label + " " + string.Join(".", idx.Select(i => i + 1));

    /// <summary>"m_ulPremise_Lev_Min" → "Premise Lev Min" (campos ainda sem rótulo).</summary>
    static string Humanize(string name)
    {
        var s = Prefix().Replace(name, "").Replace('_', ' ');
        return Camel().Replace(s, " $1").Trim();
    }

    /// <summary>A chave é do TaskLayout (e não um nome do esquema antigo em português).</summary>
    public static bool IsKey(string name) => name.Length > 0 && !name.Contains(' ') && (name.Contains('.') || name.Contains('[') || Leaves.ContainsKey(LeafName(name)) || name.StartsWith("m_"));

    /// <summary>Nome completo ("Prêmio (sucesso) › Ouro"). <paramref name="inGroup"/>: sem o escopo que já é o título do grupo.</summary>
    public static string Label(string key, bool inGroup = false)
    {
        var parts = Parts(key).ToList();
        var last = parts[^1];
        var sb = new StringBuilder();
        for (int i = inGroup && OwnGroup(parts[0]) ? 1 : 0; i < parts.Count - 1; i++) sb.Append(Numbered(ScopeLabel(parts[i].Name), parts[i].Index)).Append(" › ");
        // coordenada: v[0..2] = X, Y, Z
        if (last.Name == "v" && last.Index.Length == 1 && last.Index[0] < 3) return sb + "XYZ"[last.Index[0]].ToString();
        return sb + Numbered(LeafLabel(last.Name), last.Index);
    }

    /// <summary>Dica flutuante: o que o campo faz (quando se sabe) e, no fim, o nome original do campo.</summary>
    public static string Tip(string key)
    {
        var parts = Parts(key).ToList();
        var leaf = LeafName(parts[^1].Name);
        var lines = new List<string>();
        foreach (var (name, _) in parts.Take(parts.Count - 1))
            if (Scopes.TryGetValue(name, out var s) && s.Hint.Length > 0) lines.Add(ScopeLabel(name) + ": " + T("qs." + name + ".h", s.Hint));
        if (Leaves.TryGetValue(leaf, out var e) && e.Hint.Length > 0) lines.Add(T("qf." + leaf + ".h", e.Hint));
        else if (leaf.EndsWith("Pointer")) lines.Add(T("qf._pointer.h", "Endereço de memória gravado pelo editor original; o jogo não usa."));
        lines.Add("");
        lines.Add(T("qf._original", "Campo original:") + " " + key);
        return string.Join("\n", lines).TrimStart('\n');
    }

    /// <summary>Código do grupo de um campo (área da tabela ou escopo de primeiro nível). Exibir com <see cref="GroupLabel"/>.</summary>
    public static string Group(string key)
    {
        var parts = Parts(key).ToList();
        var first = parts[0];
        if (parts.Count > 1 && OwnGroup(first))
            return "qs:" + first.Name + string.Concat(first.Index.Select(i => $"[{i}]"));
        if (parts.Count > 1 && Scopes.TryGetValue(first.Name, out var sc)) return "qg:" + sc.Area;
        return "qg:" + (Leaves.TryGetValue(LeafName(first.Name), out var e) ? e.Area : "other");
    }

    /// <summary>Escopo com grupo próprio na tela: prêmios e itens de lista (monstro 1, item 2…); datas, comparações e pontos ficam na área.</summary>
    static bool OwnGroup((string Name, int[] Index) p) => p.Index.Length > 0 || Scopes.TryGetValue(p.Name, out var s) && s.Area == "award";

    public static bool IsGroup(string code) => code.StartsWith("qg:") || code.StartsWith("qs:");

    public static string GroupLabel(string code)
    {
        if (code.StartsWith("qs:")) { var g = Parts(code[3..]).First(); return Numbered(ScopeLabel(g.Name), g.Index); }
        var a = code[3..];
        return AreaLabels.TryGetValue(a, out var l) ? T("qg." + a, l) : T("qg.other", "Outros");
    }

    /// <summary>Ordem de exibição dos grupos: áreas na ordem da tabela, depois os escopos (na ordem do arquivo).</summary>
    public static int GroupRank(string code) => code.StartsWith("qg:") && Areas.IndexOf(code[3..]) is var i and >= 0 ? i : Areas.Count;

    [GeneratedRegex(@"\[(\d+)\]")] private static partial Regex Index();
    [GeneratedRegex(@"^Param\d+$")] private static partial Regex ParamN();
    [GeneratedRegex(@"^(m_(ul|us|uc|ui|u|l|n|b|i|f|c|psz|p|tm|sz|pl|pb)?)")] private static partial Regex Prefix();
    [GeneratedRegex(@"(?<=[a-z])([A-Z])")] private static partial Regex Camel();
}
