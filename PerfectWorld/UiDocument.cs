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
    static Regex StartTag(string name) => new(@"<(?!DIALOG\b)([A-Z]+)\s+Name=""" + Regex.Escape(name) + @"""([^>]*)>");

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

    public bool SetRect(string control, Rectangle r)
    {
        var ok = SetAttr(control, "x", r.X.ToString(CultureInfo.InvariantCulture)) & SetAttr(control, "y", r.Y.ToString(CultureInfo.InvariantCulture));
        if (r.Width > 0) ok &= SetAttr(control, "Width", r.Width.ToString(CultureInfo.InvariantCulture));
        if (r.Height > 0) ok &= SetAttr(control, "Height", r.Height.ToString(CultureInfo.InvariantCulture));
        return ok;
    }

    /// <summary>Texto do controle (String="..." do &lt;Text&gt; dentro dele). Aspas e &lt; &gt; viram entidades.</summary>
    public bool SetText(string control, string text)
    {
        var m = StartTag(control).Match(Xml);
        if (!m.Success) return false;
        // corpo do controle = até o próximo controle (os controles não se aninham, exceto em SUBDIALOG)
        var next = Regex.Match(Xml[(m.Index + m.Length)..], @"<[A-Z]+\s+Name=""");
        int end = next.Success ? m.Index + m.Length + next.Index : Xml.Length;
        var body = Xml[(m.Index + m.Length)..end];
        var esc = text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        var tm = Regex.Match(body, @"<Text\b[^>]*?\bString=""[^""]*""", RegexOptions.IgnoreCase);
        string newBody;
        if (tm.Success) newBody = body[..tm.Index] + Regex.Replace(tm.Value, @"\b(String)=""[^""]*""", s => $"{s.Groups[1].Value}=\"{esc}\"", RegexOptions.IgnoreCase) + body[(tm.Index + tm.Length)..];
        else
        {
            var t2 = Regex.Match(body, @"<Text\b");
            if (!t2.Success) return false;   // controle sem <Text>: não inventa formatação
            newBody = body[..(t2.Index + 5)] + $" String=\"{esc}\"" + body[(t2.Index + 5)..];
        }
        Xml = Xml[..(m.Index + m.Length)] + newBody + Xml[end..];
        Dialog = GameDialog.Parse(Xml);
        return true;
    }
}
