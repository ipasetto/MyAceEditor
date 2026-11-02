using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Janela própria desenhada na tela "Criar interface" (usuário 02/11). O desenho é o próprio XML de janela do jogo
/// (mesmo formato do interfaces.pck, PW-KB §S20), montado com blocos de controle copiados de outras janelas; as edições de
/// posição/texto/fonte usam o <see cref="UiDocument"/>. Para ganhar função no jogo a janela é gravada POR CIMA de uma
/// existente e precisa manter o contrato dela: nome da janela, nomes + tipos dos controles e os Command dos botões
/// (o exe liga tudo pelo nome: MAP_FILE_TO_CREATOR, DDX_Control, AUI_ON_COMMAND; PW-KB §S20b).
/// </summary>
public static class UiDesign
{
    /// <summary>Fonte das janelas do jogo (方正细黑一简体).</summary>
    public const string GameFont = "方正细黑一简体";

    // controle = tag em MAIÚSCULAS com Name (nunca a DIALOG); aspas podem ter < > crus
    static readonly Regex Start = new(@"<(?!DIALOG\b)([A-Z]+)\s+Name=""([^""]+)""((?:[^>""]|""[^""]*"")*)>", RegexOptions.Compiled);
    static readonly Regex Head = new(@"<DIALOG\b(?:[^>""]|""[^""]*"")*>", RegexOptions.Compiled);
    static readonly Regex End = new(@"</DIALOG\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex CommandAttr = new(@"\bCommand=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Um controle no texto: tag, nome e o trecho (do começo da linha dele até o começo do próximo).</summary>
    public sealed record Piece(string Tag, string Name, int Start, int Length);

    static int LineStart(string xml, int i) { while (i > 0 && (xml[i - 1] == ' ' || xml[i - 1] == '\t')) i--; return i; }

    static int EndPos(string xml) => End.Match(xml) is { Success: true } m ? LineStart(xml, m.Index) : xml.Length;

    /// <summary>Controles da janela na ordem do arquivo (o último fica por cima).</summary>
    public static List<Piece> Pieces(string xml)
    {
        var ms = Start.Matches(xml);
        int stop = EndPos(xml);
        var list = new List<Piece>();
        for (int i = 0; i < ms.Count; i++)
        {
            int s = LineStart(xml, ms[i].Index);
            if (s >= stop) break;
            int e = i + 1 < ms.Count ? Math.Min(stop, LineStart(xml, ms[i + 1].Index)) : stop;
            list.Add(new Piece(ms[i].Groups[1].Value, ms[i].Groups[2].Value, s, e - s));
        }
        return list;
    }

    /// <summary>Trecho XML de um controle (vazio se não existe).</summary>
    public static string Block(string xml, string name) =>
        Pieces(xml).FirstOrDefault(p => p.Name == name) is { } p ? xml.Substring(p.Start, p.Length) : "";

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Janela vazia: nome, tamanho e moldura (imagem do surfaces.pck; null = sem moldura).</summary>
    public static string NewXml(string window, Size size, string? frame)
    {
        var sb = new StringBuilder();
        sb.Append($"<DIALOG Name=\"{Esc(window)}\" Width=\"{size.Width}\" Height=\"{size.Height}\">\r\n");
        if (frame is { Length: > 0 }) sb.Append($"\t<Resource>\r\n\t\t<FrameImage FileName=\"{Esc(frame)}\"/>\r\n\t</Resource>\r\n");
        sb.Append("</DIALOG>\r\n");
        return sb.ToString();
    }

    /// <summary>Rótulo de texto pronto (fonte do jogo).</summary>
    public static string LabelBlock(string name, string text, int w, int h, float size, bool bold, Color color, bool outline) =>
        $"\t<LABEL Name=\"{Esc(name)}\" x=\"0\" y=\"0\" Width=\"{w}\" Height=\"{h}\">\r\n" +
        $"\t\t<Text String=\"{Esc(text)}\" FontName=\"{GameFont}\" FontSize=\"{size.ToString(CultureInfo.InvariantCulture)}\"{(bold ? " Bold=\"true\"" : "")} Color=\"{color.R},{color.G},{color.B},{color.A}\"" +
        (outline ? " Outline=\"1\" OutlineColor=\"10,35,50,255\"" : "") + "/>\r\n\t</LABEL>\r\n";

    /// <summary>Põe um controle na janela, na posição <paramref name="index"/> da ordem (-1 = por cima de todos).</summary>
    public static string Insert(string xml, string block, int index = -1)
    {
        block = block.TrimEnd(' ', '\t');
        if (!block.EndsWith('\n')) block += "\r\n";
        if (block.Length > 0 && block[0] != '\t' && block[0] != ' ') block = "\t" + block;
        var ps = Pieces(xml);
        int at = index >= 0 && index < ps.Count ? ps[index].Start : EndPos(xml);
        return xml.Insert(at, block);
    }

    /// <summary>Troca o Name da tag de abertura do controle.</summary>
    public static string Rename(string block, string newName) =>
        Start.Replace(block, m => m.Value.Replace($"Name=\"{m.Groups[2].Value}\"", $"Name=\"{Esc(newName)}\""), 1);

    public static string Remove(string xml, string name) =>
        Pieces(xml).FirstOrDefault(p => p.Name == name) is { } p ? xml.Remove(p.Start, p.Length) : xml;

    /// <summary>Muda a camada (ordem no arquivo): o controle vai para a posição <paramref name="index"/>.</summary>
    public static string Reorder(string xml, string name, int index)
    {
        var b = Block(xml, name);
        if (b.Length == 0) return xml;
        xml = Remove(xml, name);
        return Insert(xml, b, index);
    }

    /// <summary>Nome livre na janela (o jogo procura os controles pelo nome: nada de repetido).</summary>
    public static string FreeName(string xml, string wanted)
    {
        var used = Pieces(xml).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(wanted)) return wanted;
        var stem = Regex.Replace(wanted, @"_?\d+$", "");
        for (int i = 2; ; i++) if (!used.Contains($"{stem}_{i}")) return $"{stem}_{i}";
    }

    public static string WindowName(string xml) => Regex.Match(Head.Match(xml).Value, @"\bName=""([^""]*)""") is { Success: true } m ? m.Groups[1].Value : "";

    /// <summary>Muda nome, tamanho e/ou moldura da janela (null = não muda; moldura "" = tira).</summary>
    public static string SetDialog(string xml, string? name = null, Size? size = null, string? frame = null)
    {
        var h = Head.Match(xml);
        if (!h.Success) return xml;
        var tag = h.Value;
        string Set(string t, string attr, string v)
        {
            var a = new Regex(@"\b" + attr + @"=""[^""]*""");
            return a.IsMatch(t) ? a.Replace(t, $"{attr}=\"{v}\"", 1) : t.Insert(t.Length - 1, $" {attr}=\"{v}\"");
        }
        if (name is not null) tag = Set(tag, "Name", Esc(name));
        if (size is { } s) { tag = Set(tag, "Width", s.Width.ToString(CultureInfo.InvariantCulture)); tag = Set(tag, "Height", s.Height.ToString(CultureInfo.InvariantCulture)); }
        xml = xml[..h.Index] + tag + xml[(h.Index + h.Length)..];
        if (frame is null) return xml;
        // moldura = 1º FileName antes do 1º controle (o mesmo que o GameDialog lê)
        int first = Pieces(xml).FirstOrDefault()?.Start ?? EndPos(xml);
        var head = xml[..first];
        var fm = Regex.Match(head, @"<FrameImage\s+FileName=""[^""]*""\s*/>");
        if (fm.Success)
        {
            var repl = frame.Length == 0 ? "" : $"<FrameImage FileName=\"{Esc(frame)}\"/>";
            return xml[..fm.Index] + repl + xml[(fm.Index + fm.Length)..];
        }
        if (frame.Length == 0) return xml;
        int afterHead = h.Index + tag.Length;
        int lineEnd = xml.IndexOf('\n', afterHead) is var n and >= 0 ? n + 1 : afterHead;
        return xml.Insert(lineEnd, $"\t<Resource>\r\n\t\t<FrameImage FileName=\"{Esc(frame)}\"/>\r\n\t</Resource>\r\n");
    }

    /// <summary>Texto da janela num documento editável (posição, texto, fonte).</summary>
    public static UiDocument Doc(string xml) => new(ToBytes(xml));

    /// <summary>UTF-16 com BOM, como as janelas do version01.</summary>
    public static byte[] ToBytes(string xml)
    {
        var body = Encoding.Unicode.GetBytes(xml);
        var b = new byte[body.Length + 2]; b[0] = 0xFF; b[1] = 0xFE;
        Buffer.BlockCopy(body, 0, b, 2, body.Length);
        return b;
    }

    public static string FromBytes(byte[] b) =>
        b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
        : b.Length >= 2 && b[1] == 0 ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);

    // ---------- contrato com a janela do jogo ----------

    /// <summary>O que o código do jogo pode procurar nesta janela: controle (tipo + nome) e o Command dele.</summary>
    public sealed record Need(string Tag, string Name, string? Command);

    public static List<Need> Contract(string xml) =>
        Start.Matches(xml).Select(m => new Need(m.Groups[1].Value, m.Groups[2].Value,
            CommandAttr.Match(m.Groups[3].Value) is { Success: true } c && c.Groups[1].Value.Length > 0 ? c.Groups[1].Value : null)).ToList();

    /// <summary>Resultado da conferência: o que falta na janela criada para ela funcionar no lugar da original.</summary>
    public sealed record Check(List<Need> Missing, List<(Need Want, string Has)> WrongType, List<string> MissingCommands, string WindowWanted)
    {
        public bool Ok => Missing.Count == 0 && WrongType.Count == 0 && MissingCommands.Count == 0;
    }

    /// <summary>
    /// Confere a janela criada contra a original: todo controle da original com o mesmo nome e tipo (DDX_Control usa
    /// dynamic_cast) e todo Command dos botões da original (AUI_ON_COMMAND). Nome exato: o jogo não ignora maiúsculas.
    /// </summary>
    public static Check Compare(string original, string design)
    {
        var mine = Contract(design).GroupBy(n => n.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var cmds = Contract(design).Select(n => n.Command).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var missing = new List<Need>(); var wrong = new List<(Need, string)>(); var mc = new List<string>();
        foreach (var n in Contract(original))
        {
            if (!mine.TryGetValue(n.Name, out var has)) missing.Add(n);
            else if (has.Tag != n.Tag) wrong.Add((n, has.Tag));
            if (n.Command is { } c && !cmds.Contains(c) && !mc.Contains(c) && mine.ContainsKey(n.Name)) mc.Add(c);
        }
        return new Check(missing, wrong, mc, WindowName(original));
    }

    /// <summary>Copia da original para a criada o que falta (mesmo nome, tipo, posição e Command).</summary>
    public static string BringMissing(string original, string design, Check check)
    {
        foreach (var n in check.Missing)
            if (Block(original, n.Name) is { Length: > 0 } b) design = Insert(design, b);
        foreach (var c in check.MissingCommands)
        {
            // o botão existe mas perdeu o Command: devolve o atributo da original
            var who = Contract(original).FirstOrDefault(n => n.Command == c);
            if (who is null) continue;
            var d = Doc(design);
            if (d.SetAttr(who.Name, "Command", c)) design = d.Xml;
        }
        return design;
    }

    /// <summary>Imagens que a janela usa (ficam no surfaces.pck do jogo).</summary>
    public static List<string> Images(string xml) =>
        Regex.Matches(xml, @"FileName=""([^""]+)""").Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Pacote para levar a outro lugar: a janela (.xml) e um LEIA-ME com o que falta para usar no jogo.</summary>
    public static byte[] Zip(string xmlName, string xml, string readme)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = z.CreateEntry(xmlName).Open()) s.Write(ToBytes(xml));
            using (var s = z.CreateEntry("LEIA-ME.txt").Open()) s.Write(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(readme)).ToArray());
        }
        return ms.ToArray();
    }

    /// <summary>Lê a janela de um .xml ou de um .zip exportado (o 1º .xml dentro).</summary>
    public static string? Load(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return FromBytes(File.ReadAllBytes(path));
        using var z = ZipFile.OpenRead(path);
        var e = z.Entries.FirstOrDefault(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (e is null) return null;
        using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m);
        return FromBytes(m.ToArray());
    }
}
