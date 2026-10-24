using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Textos/configs do cliente lidos direto do configs.pck (PW-KB §S2):
///  - item_color.txt: "ID&lt;TAB&gt;índice" → cor oficial do nome (tabela l_aNameCols do cliente);
///  - item_ext_desc.txt: UTF-16, "ID&lt;TAB&gt;\"texto\"" → descrição com cores ^RRGGBB e \r como quebra.
/// </summary>
public sealed class GameConfigs
{
    // CECGame l_aNameCols (source_client EC_Game.cpp): 0 padrão, 1 azul-claro, 2 amarelo, 3 roxo,
    // 4 dourado-escuro, 5 branco, 6 cinza, 7 verde-água, 8 vermelho.
    public static readonly Color[] NameColors =
    {
        Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x80, 0x80, 0xFF), Color.FromArgb(0xFF, 0xDC, 0x50),
        Color.FromArgb(0xAA, 0x32, 0xFF), Color.FromArgb(0xFF, 0x60, 0x00), Color.FromArgb(0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xB0, 0xB0, 0xB0), Color.FromArgb(0x00, 0xFF, 0xAE), Color.FromArgb(0xFF, 0x00, 0x00),
        Color.FromArgb(0xFF, 0xFF, 0xFF),
    };

    public Dictionary<int, int> ItemColors { get; } = new();
    public Dictionary<int, string> ItemDescs { get; } = new();
    /// <summary>Nome da habilidade por ID (skillstr.txt: linha "ID*10 \"nome\""; ID*10+1 é a descrição).</summary>
    public Dictionary<int, string> SkillNames { get; } = new();
    /// <summary>Descrição da habilidade por ID (linha "ID*10+1"), com as cores ^RRGGBB do jogo.</summary>
    public Dictionary<int, string> SkillDescs { get; } = new();
    public AddonText? Addons { get; private set; }

    public static GameConfigs? Load(string configsPck, LoadContext? context = null)
    {
        using var pck = new PckArchive(configsPck, context?.Token ?? default);
        var gc = new GameConfigs();
        if (pck.Find(@"configs\item_color.txt") is { } c)
            foreach (var line in Decode(pck.Read(c)).Split('\n'))
            {
                context?.Check();
                var p = line.Trim().Split('\t', ' ');
                if (p.Length >= 2 && int.TryParse(p[0], out var id) && int.TryParse(p[^1], out var idx)) gc.ItemColors[id] = idx;
            }
        if (pck.Find(@"configs\item_ext_desc.txt") is { } d)
            foreach (var line in Decode(pck.Read(d)).Split('\n'))
            {
                context?.Check();
                var m = Regex.Match(line, "^(\\d+)\\s+\"(.*)\"\\s*$");
                if (m.Success) gc.ItemDescs[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
            }
        if (pck.Find(@"configs\skillstr.txt") is { } s)
            // "ID*10 \"nome\"" e "ID*10+1 \"descrição\"" (a descrição pode ocupar várias linhas até fechar as aspas)
            foreach (Match m in Regex.Matches(Decode(pck.Read(s)), "(?m)^(\\d+)([01])\\s+\"([^\"]*)\""))
            {
                context?.Check();
                if (m.Groups[3].Value.Length == 0) continue;
                (m.Groups[2].Value == "0" ? gc.SkillNames : gc.SkillDescs)[int.Parse(m.Groups[1].Value)] = m.Groups[3].Value.Trim();
            }
        context?.Check();
        gc.Addons = AddonText.Load(pck, context?.Token ?? default);
        context?.Check();
        return gc.ItemColors.Count + gc.ItemDescs.Count > 0 || gc.Addons is not null ? gc : null;
    }

    static string Decode(byte[] b) =>
        b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
        : Encoding.GetEncoding(936).GetString(b);

    public Color? NameColor(string id) =>
        int.TryParse(id, out var i) && ItemColors.TryGetValue(i, out var idx) && idx >= 0 && idx < NameColors.Length ? NameColors[idx] : null;

    /// <summary>Quebra um texto do jogo em trechos coloridos (^RRGGBB muda a cor; \r ou \n quebra a linha).</summary>
    public static List<(string Text, Color Color)> Runs(string s, Color start)
    {
        var runs = new List<(string, Color)>();
        var cur = start;
        s = s.Replace("\\r", "\n").Replace("\r", "\n");
        int last = 0;
        foreach (Match m in Regex.Matches(s, "\\^([0-9a-fA-F]{6})"))
        {
            if (m.Index > last) runs.Add((s[last..m.Index], cur));
            var v = Convert.ToInt32(m.Groups[1].Value, 16);
            cur = Color.FromArgb(v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF);
            last = m.Index + m.Length;
        }
        if (last < s.Length) runs.Add((s[last..], cur));
        return runs;
    }

    /// <summary>Procura o configs.pck a partir do caminho do elements.data.</summary>
    public static string? FindConfigsPck(string elementsPath)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
            foreach (var c in new[] { Path.Combine(dir, "configs.pck"), Path.Combine(dir, "element", "configs.pck") })
                if (File.Exists(c)) return c;
        return null;
    }
}
