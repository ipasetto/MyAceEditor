using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Janela do interfaces.pck (version01\*.xml) aberta para edição. O texto do arquivo é mantido como está e só
/// os atributos alterados são reescritos (posição, tamanho, texto), então o resto do arquivo não muda
/// (PW-KB §S20: não é XML válido, por isso nada de parser XML). Grava em UTF-16 com o mesmo BOM.
/// </summary>
public sealed class UiDocument
{
    public string Xml { get; private set; }
    public bool HadBom { get; }
    public GameDialog Dialog { get; private set; }

    public UiDocument(byte[] bytes)
    {
        HadBom = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE;
        Xml = HadBom ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.Unicode.GetString(bytes);
        Dialog = GameDialog.Parse(Xml);
    }

    public byte[] ToBytes()
    {
        var body = Encoding.Unicode.GetBytes(Xml);
        if (!HadBom) return body;
        var b = new byte[body.Length + 2]; b[0] = 0xFF; b[1] = 0xFE;
        Buffer.BlockCopy(body, 0, b, 2, body.Length);
        return b;
    }

    /// <summary>Volta a um texto anterior (desfazer).</summary>
    public void Restore(string xml) { Xml = xml; Dialog = GameDialog.Parse(Xml); }

    // nunca a tag da janela: há controle com o mesmo nome dela (gmfinishmsg.xml)
    static Regex StartTag(string name) => new(@"<(?!DIALOG\b)([A-Z]+)\s+Name=""" + Regex.Escape(name) + @"""((?:[^>""]|""[^""]*"")*)>");   // aspas podem ter < > crus

    /// <summary>Muda (ou cria) um atributo da tag de abertura do controle (x, y, Width, Height...).</summary>
    public bool SetAttr(string control, string attr, string value)
    {
        var m = StartTag(control).Match(Xml);
        if (!m.Success) return false;
        var tag = m.Value;
        var a = new Regex(@"\b" + Regex.Escape(attr) + @"=""[^""]*""", RegexOptions.IgnoreCase);   // há arquivos com "X", "string"…
        var newTag = a.IsMatch(tag) ? a.Replace(tag, $"{attr}=\"{value}\"", 1)
            : tag.Insert(tag.IndexOf('"', tag.IndexOf("Name=\"", StringComparison.Ordinal) + 6) + 1, $" {attr}=\"{value}\"");
        Xml = Xml[..m.Index] + newTag + Xml[(m.Index + m.Length)..];
        Dialog = GameDialog.Parse(Xml);
        return true;
    }

    // só o que mudou: há controles sem x/y/Width no XML (valem 0) e não se inventa atributo à toa
    public bool SetRect(string control, Rectangle r)
    {
        if (Dialog[control] is not { } c) return false;
        static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
        bool ok = true;
        if (r.X != c.Rect.X) ok &= SetAttr(control, "x", S(r.X));
        if (r.Y != c.Rect.Y) ok &= SetAttr(control, "y", S(r.Y));
        if (r.Width > 0 && r.Width != c.Rect.Width) ok &= SetAttr(control, "Width", S(r.Width));
        if (r.Height > 0 && r.Height != c.Rect.Height) ok &= SetAttr(control, "Height", S(r.Height));
        return ok;
    }

    /// <summary>Texto do controle (String="..." do &lt;Text&gt; dentro dele). Aspas e &lt; &gt; viram entidades.</summary>
    public bool SetText(string control, string text) => SetChildString(control, "Text", text, create: true);

    /// <summary>Dica do controle (String="..." do &lt;Hint&gt;). Só troca uma dica que já existe.</summary>
    public bool SetHint(string control, string text) => SetChildString(control, "Hint", text, create: false);

    bool SetChildString(string control, string child, string text, bool create)
    {
        var m = StartTag(control).Match(Xml);
        if (!m.Success) return false;
        // corpo do controle = até o próximo controle (os controles não se aninham, exceto em SUBDIALOG)
        var next = Regex.Match(Xml[(m.Index + m.Length)..], @"<[A-Z]+\s+Name=""");
        int end = next.Success ? m.Index + m.Length + next.Index : Xml.Length;
        var body = Xml[(m.Index + m.Length)..end];
        var esc = text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        var tm = Regex.Match(body, "<" + child + @"\b(?:[^>""]|""[^""]*"")*?\bString=""[^""]*""", RegexOptions.IgnoreCase);
        string newBody;
        if (tm.Success) newBody = body[..tm.Index] + Regex.Replace(tm.Value, @"\b(String)=""[^""]*""", s => $"{s.Groups[1].Value}=\"{esc}\"", RegexOptions.IgnoreCase) + body[(tm.Index + tm.Length)..];
        else
        {
            var t2 = Regex.Match(body, "<" + child + @"\b");
            if (!create || !t2.Success) return false;   // controle sem <Text>: não inventa formatação
            int at = t2.Index + 1 + child.Length;
            newBody = body[..at] + $" String=\"{esc}\"" + body[at..];
        }
        Xml = Xml[..(m.Index + m.Length)] + newBody + Xml[end..];
        Dialog = GameDialog.Parse(Xml);
        return true;
    }
}

/// <summary>
/// Textos de uma janela XML do interfaces.pck como estão escritos no arquivo (sem decodificar entidades): o String do
/// &lt;Text&gt; e do &lt;Hint&gt; de cada controle, pela chave "TAG|nome|Text" ou "TAG|nome|Hint". Usado para trazer os textos
/// de outro cliente sem mexer em mais nada da janela (PW-KB §S20c). Nome repetido na mesma janela = ambíguo, fica de fora.
/// </summary>
public static class UiTexts
{
    public sealed record Item(string Key, int Start, int Length, string Raw)
    {
        public string Text => System.Net.WebUtility.HtmlDecode(Raw);
    }

    static readonly Regex Ctl = new(@"<(?!DIALOG\b)([A-Z]+)\s+Name=""([^""]+)""", RegexOptions.Compiled);
    static readonly (string Kind, Regex Re)[] Child =
    {
        ("Text", new(@"<Text\b(?:[^>""]|""[^""]*"")*?\bString=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("Hint", new(@"<Hint\b(?:[^>""]|""[^""]*"")*?\bString=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    };

    public static Dictionary<string, Item> Find(string xml)
    {
        var d = new Dictionary<string, Item>(StringComparer.Ordinal);
        var dup = new HashSet<string>(StringComparer.Ordinal);
        var ms = Ctl.Matches(xml);
        for (int i = 0; i < ms.Count; i++)
        {
            var m = ms[i];
            int start = m.Index + m.Length, end = i + 1 < ms.Count ? ms[i + 1].Index : xml.Length;
            foreach (var (kind, re) in Child)
            {
                var c = re.Match(xml, start, end - start);
                if (!c.Success) continue;
                var key = $"{m.Groups[1].Value}|{m.Groups[2].Value}|{kind}";
                var g = c.Groups[1];
                if (!d.TryAdd(key, new Item(key, g.Index, g.Length, g.Value))) dup.Add(key);
            }
        }
        foreach (var k in dup) d.Remove(k);
        return d;
    }

    /// <summary>Troca o miolo dos textos pedidos (item → texto cru, já escrito como no arquivo); o resto fica igual.</summary>
    public static string Replace(string xml, IEnumerable<(Item Item, string Raw)> changes)
    {
        var sb = new StringBuilder(xml.Length + 256);
        int at = 0;
        foreach (var (it, raw) in changes.OrderBy(c => c.Item.Start))
        {
            sb.Append(xml, at, it.Start - at).Append(raw);
            at = it.Start + it.Length;
        }
        return sb.Append(xml, at, xml.Length - at).ToString();
    }
}
