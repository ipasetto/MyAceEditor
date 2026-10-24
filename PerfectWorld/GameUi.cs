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
    public sealed record Control(string Tag, string Name, Rectangle Rect, string? Text, float FontSize, Color? Color, int Align, IReadOnlyList<string> Images);

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
            var a = Attr.Matches(m.Groups[3].Value).ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
            int N(string k) => a.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? n : 0;
            var textTag = Regex.Match(body, @"<Text\b([^>]*)/?>");
            var ta = textTag.Success ? Attr.Matches(textTag.Groups[1].Value).ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value, StringComparer.OrdinalIgnoreCase) : new();
            Color? col = ta.TryGetValue("Color", out var cs) && cs.Split(',') is { Length: >= 3 } c
                ? System.Drawing.Color.FromArgb(c.Length >= 4 ? int.Parse(c[3]) : 255, int.Parse(c[0]), int.Parse(c[1]), int.Parse(c[2])) : null;
            float size = ta.TryGetValue("FontSize", out var fs) && float.TryParse(fs, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 10;
            dlg.Controls[m.Groups[2].Value] = new Control(m.Groups[1].Value, m.Groups[2].Value, new Rectangle(N("x"), N("y"), N("Width"), N("Height")),
                ta.GetValueOrDefault("String"), size, col, N("Align"), File.Matches(body).Select(x => x.Groups[1].Value).ToList());
        }
        return dlg;
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
