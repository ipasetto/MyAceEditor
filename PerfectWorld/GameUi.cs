using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Janela do jogo descrita em interfaces.pck (version01\*.xml, PW-KB §S20): controles com nome, posição,
/// texto e imagens. Não é XML válido (atributos com &lt; &gt; crus), então a leitura é por expressão regular.
/// </summary>
public sealed class GameDialog
{
    public sealed record Control(string Tag, string Name, Rectangle Rect, string? Text, float FontSize, Color? Color, int Align, IReadOnlyList<string> Images,
        string? FontName = null, bool Bold = false, Point TextOffset = default, int Frames = 1);

    public string Name { get; init; } = "";
    public Size Size { get; init; }
    public string? Frame { get; init; }
    public Dictionary<string, Control> Controls { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Control? this[string name] => Controls.GetValueOrDefault(name);

    public static GameDialog? Load(PckArchive interfaces, string xmlPath)
    {
        if (interfaces.Find(xmlPath) is not { } e) return null;
        var b = interfaces.Read(e);
        var t = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
            : b.Length >= 2 && b[1] == 0 ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
        return Parse(t);
    }

    static readonly Regex Head = new(@"<DIALOG\s+Name=""([^""]*)""[^>]*?Width=""(\d+)""[^>]*?Height=""(\d+)""", RegexOptions.Compiled);
    static readonly Regex Ctl = new(@"<([A-Z]+)\s+Name=""([^""]+)""([^>]*)>", RegexOptions.Compiled);
    static readonly Regex Attr = new(@"\b(\w+)=""([^""]*)""", RegexOptions.Compiled);
    static readonly Regex File = new(@"FileName=""([^""]*)""", RegexOptions.Compiled);

    // atributo repetido na mesma tag ("String" e "string"): vale o primeiro
    static Dictionary<string, string> Attrs(string tag)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match x in Attr.Matches(tag)) d.TryAdd(x.Groups[1].Value, x.Groups[2].Value);
        return d;
    }

    public static GameDialog Parse(string xml)
    {
        var h = Head.Match(xml);
        var ms = Ctl.Matches(xml).Where(m => m.Groups[1].Value != "DIALOG").ToList();
        // moldura da janela: primeiro FileName antes do primeiro controle
        int firstCtl = ms.Count > 0 ? ms[0].Index : xml.Length;
        var frame = File.Match(xml[..firstCtl]);
        var dlg = new GameDialog { Name = h.Success ? h.Groups[1].Value : "", Size = h.Success ? new Size(int.Parse(h.Groups[2].Value), int.Parse(h.Groups[3].Value)) : Size.Empty, Frame = frame.Success ? frame.Groups[1].Value : null };
        for (int i = 0; i < ms.Count; i++)
        {
            var m = ms[i];
            var body = xml[(m.Index + m.Length)..(i + 1 < ms.Count ? ms[i + 1].Index : xml.Length)];
            var a = Attrs(m.Groups[3].Value);
            int N(string k) => a.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : 0;
            var textTag = Regex.Match(body, @"<Text\b([^>]*)/?>");
            var ta = textTag.Success ? Attrs(textTag.Groups[1].Value) : new();
            // cor "R,G,B,A"; há arquivos do jogo com valor malformado (ex.: "25color") — vira "sem cor"
            static int? C(string s) => int.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 255) : null;
            Color? col = ta.TryGetValue("Color", out var cs) && cs.Split(',') is { Length: >= 3 } c && C(c[0]) is { } cr && C(c[1]) is { } cg && C(c[2]) is { } cb
                ? System.Drawing.Color.FromArgb(c.Length >= 4 ? C(c[3]) ?? 255 : 255, cr, cg, cb) : null;
            float size = ta.TryGetValue("FontSize", out var fs) && float.TryParse(fs, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 10;
            dlg.Controls[m.Groups[2].Value] = new Control(m.Groups[1].Value, m.Groups[2].Value, new Rectangle(N("x"), N("y"), N("Width"), N("Height")),
                System.Net.WebUtility.HtmlDecode(ta.GetValueOrDefault("String")), size, col, N("Align"), File.Matches(body).Select(x => x.Groups[1].Value).ToList(),
                ta.GetValueOrDefault("FontName"), ta.TryGetValue("Bold", out var bo) && bo.Equals("true", StringComparison.OrdinalIgnoreCase),
                new Point(N("TextOffsetX"), N("TextOffsetY")),
                Regex.Match(body, @"FileName=""[^""]*""\s+Frames=""(\d+)""") is { Success: true } fr ? int.Parse(fr.Groups[1].Value) : 1);
        }
        return dlg;
    }
}

/// <summary>
/// Fontes do próprio cliente (element\font, element\fonts ou element\data: fzxh1jw.ttf = 方正细黑一简体, fzl2jw.ttf = 方正隶二简体,
/// fzlbjw.ttf = 方正隶变简体), para o texto sair com a mesma largura do jogo.
/// </summary>
public static class GameFonts
{
    static readonly System.Drawing.Text.PrivateFontCollection Pfc = new();
    static readonly Dictionary<string, FontFamily> ByFile = new(StringComparer.OrdinalIgnoreCase);
    static string? _loadedFrom;

    public static void Load(string elementDir)
    {
        if (_loadedFrom == elementDir) return;
        _loadedFrom = elementDir;
        foreach (var sub in new[] { "font", "fonts", "data" })
        {
            var d = Path.Combine(elementDir, sub);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*.ttf"))
            {
                var key = Path.GetFileNameWithoutExtension(f);
                if (ByFile.ContainsKey(key)) continue;
                try
                {
                    int before = Pfc.Families.Length;
                    Pfc.AddFontFile(f);
                    if (Pfc.Families.Length > before) ByFile[key] = Pfc.Families[^1];
                }
                catch { }
            }
        }
    }

    /// <summary>Família para o FontName do XML; null = usar a fonte padrão do editor.</summary>
    public static FontFamily? For(string? fontName)
    {
        if (ByFile.Count == 0) return null;
        string file = fontName switch
        {
            { } n when n.Contains("隶二") => "fzl2jw",
            { } n when n.Contains("隶变") => "fzlbjw",
            _ => "fzxh1jw",
        };
        return ByFile.GetValueOrDefault(file) ?? ByFile.Values.FirstOrDefault();
    }
}

/// <summary>Texturas da interface lidas do surfaces.pck (.tga/.dds/.bmp), com cache. Caminho relativo à pasta surfaces.</summary>
public sealed class GameTextures : IDisposable
{
    readonly PckArchive _pck;
    readonly Dictionary<string, Image?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public GameTextures(PckArchive surfaces) => _pck = surfaces;

    /// <summary>"Version01\商城\x.tga" ou "Surfaces\百宝阁\x.dds"; <paramref name="frames"/> &gt; 1 pega o 1º quadro da tira.</summary>
    public Image? Get(string? path, int frames = 1)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var key = path + "|" + frames;
        if (_cache.TryGetValue(key, out var img)) return img;
        img = null;
        try
        {
            var rel = path.Replace('/', '\\').TrimStart('\\');
            if (!rel.StartsWith("surfaces\\", StringComparison.OrdinalIgnoreCase)) rel = "surfaces\\" + rel;
            if (_pck.Find(rel) is { } e)
            {
                var data = _pck.Read(e);
                var ext = Path.GetExtension(rel).ToLowerInvariant();
                img = ext switch
                {
                    ".tga" => IconLoader.DecodeTga(data),
                    ".dds" => FromDds(data),
                    _ => Image.FromStream(new MemoryStream(data)),
                };
                // .bmp da interface usa magenta (255,0,255) como transparente (o "?" de ajuda aparecia rosa)
                if (img is Bitmap bm && ext == ".bmp")
                {
                    var copy = new Bitmap(bm);
                    copy.MakeTransparent(Color.FromArgb(255, 0, 255));
                    img.Dispose(); img = copy;
                }
                if (img is not null && frames > 1)
                {
                    // tiras animadas: quadros empilhados na vertical (new/hot/gift) ou na horizontal
                    bool vertical = img.Height >= img.Width;
                    var r = vertical ? new Rectangle(0, 0, img.Width, img.Height / frames) : new Rectangle(0, 0, img.Width / frames, img.Height);
                    var first = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(first)) g.DrawImage(img, new Rectangle(Point.Empty, r.Size), r, GraphicsUnit.Pixel);
                    img.Dispose(); img = first;
                }
            }
        }
        catch { img = null; }
        _cache[key] = img;
        return img;
    }

    static Bitmap? FromDds(byte[] data)
    {
        if (DdsDecoder.Decode(data) is not { } d) return null;
        var bmp = new Bitmap(d.Width, d.Height, PixelFormat.Format32bppArgb);
        var bits = bmp.LockBits(new Rectangle(0, 0, d.Width, d.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        Marshal.Copy(d.Pixels, 0, bits.Scan0, d.Pixels.Length);
        bmp.UnlockBits(bits);
        return bmp;
    }

    /// <summary>Arquivos de uma pasta do pacote (ex.: as figuras da loja em surfaces\百宝阁).</summary>
    public IEnumerable<string> List(string folder) =>
        _pck.Entries.Where(e => e.Path.StartsWith("surfaces\\" + folder.TrimStart('\\'), StringComparison.OrdinalIgnoreCase)).Select(e => e.Path);

    public void Dispose()
    {
        foreach (var i in _cache.Values) i?.Dispose();
        _cache.Clear();
    }
}
