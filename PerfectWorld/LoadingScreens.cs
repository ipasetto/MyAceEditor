using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Telas de carregamento do cliente (surfaces.pck; PW-KB §S20d). Duas famílias, decididas pelo elementclient.exe:
///   clássica (1.4.4–1.5.5): surfaces\载入1024-N.jpg (1024×768), 载入1280-N.jpg (1280×1024), 载入16-9-N.jpg (1280×720);
///            o jogo usa a do tamanho da tela e sorteia N a partir de 1 (a 1 é obrigatória). Uma "tela" = as 3 do mesmo N.
///   1600 (1.7.x): surfaces\1600-N.jpg (1600×1024) + seção [1600-N.jpg] no surfaces\procbar.ini (posição da barra),
///            e telas por mapa em surfaces\loading\&lt;pasta&gt;\N.jpg.
/// background.jpg (fundo do login) entra como uma tela à parte. Trocar = a imagem nova é recortada/ajustada ao tamanho
/// da que sai e gravada em JPG; acrescentar = próximo N (nas 3 resoluções, ou com a seção do procbar.ini).
/// </summary>
public static class LoadingScreens
{
    public sealed class Screen
    {
        public string Set = "";             // "classic", "1600", "loading\a101", "background"
        public int N;                       // 0 no background
        public List<string> Files = new();          // caminhos no pacote (clássica: as 3 resoluções)
        public string Label => Set switch
        {
            "background" => "background.jpg",
            "classic" => $"#{N}",
            "1600" => $"1600-{N}",
            _ => $"{Set}\\{N}",
        };
    }

    public static readonly string[] Classic = { "载入1024", "载入1280", "载入16-9" };

    /// <summary>"1600" se o executável usa 1600-%d.jpg; senão "classic" (载入...). Sem executável, pelo que o pacote tem.</summary>
    public static string Family(string elementDir, PckArchive surfaces)
    {
        var exe = Path.Combine(elementDir, "elementclient.exe");
        if (File.Exists(exe))
        {
            var b = File.ReadAllBytes(exe);
            if (b.AsSpan().IndexOf("surfaces\\1600-%d.jpg"u8) >= 0) return "1600";
            if (b.AsSpan().IndexOf(Encoding.GetEncoding(936).GetBytes("载入1024-%d.jpg")) >= 0) return "classic";
        }
        return surfaces.Find(@"surfaces\1600-1.jpg") is not null && surfaces.Find(@"surfaces\载入1024-1.jpg") is null ? "1600" : "classic";
    }

    static LoadingScreens() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static readonly Regex Numbered = new(@"^surfaces\\(?:(?<set>载入1024|载入1280|载入16-9|1600)-(?<n>\d+)|loading\\(?<dir>[^\\]+)\\(?<n>\d+))\.jpg$", RegexOptions.IgnoreCase);

    /// <summary>Telas que esta família usa, na ordem (N só conta enquanto é seguido: 1, 2, 3...).</summary>
    public static List<Screen> List(PckArchive surfaces, string family)
    {
        var by = new Dictionary<(string, int), Screen>();
        foreach (var e in surfaces.Entries)
        {
            var m = Numbered.Match(e.Path);
            if (!m.Success) continue;
            string set = m.Groups["dir"].Success ? "loading\\" + m.Groups["dir"].Value : m.Groups["set"].Value;
            bool classic = Classic.Contains(set);
            if (family == "classic" ? !classic : classic) continue;
            var key = (classic ? "classic" : set, int.Parse(m.Groups["n"].Value));
            if (!by.TryGetValue(key, out var s)) by[key] = s = new Screen { Set = key.Item1, N = key.Item2 };
            s.Files.Add(e.Path);
        }
        var list = by.Values.OrderBy(s => s.Set == "classic" || s.Set == "1600" ? 0 : 1).ThenBy(s => s.Set).ThenBy(s => s.N).ToList();
        foreach (var s in list) s.Files.Sort(string.CompareOrdinal);
        if (surfaces.Find(@"surfaces\background.jpg") is not null)
            list.Add(new Screen { Set = "background", Files = { @"surfaces\background.jpg" } });
        return list;
    }

    /// <summary>Conjunto e N de um caminho de tela (null se não é tela de carregamento numerada).</summary>
    public static (string Set, int N)? Parse(string path)
    {
        var m = Numbered.Match(path);
        if (!m.Success) return null;
        var set = m.Groups["dir"].Success ? "loading\\" + m.Groups["dir"].Value : m.Groups["set"].Value;
        return (Classic.Contains(set) ? "classic" : set, int.Parse(m.Groups["n"].Value));
    }

    /// <summary>Próximo N livre do conjunto (o jogo para no primeiro que falta).</summary>
    public static int NextN(List<Screen> list, string set)
    {
        var have = list.Where(s => s.Set == set).Select(s => s.N).ToHashSet();
        int n = 1; while (have.Contains(n)) n++;
        return n;
    }

    /// <summary>Caminhos das imagens de um N novo no conjunto.</summary>
    public static List<string> PathsFor(string set, int n) => set switch
    {
        "classic" => Classic.Select(c => $@"surfaces\{c}-{n}.jpg").ToList(),
        "1600" => new() { $@"surfaces\1600-{n}.jpg" },
        _ => new() { $@"surfaces\{set}\{n}.jpg" },
    };

    /// <summary>Imagem ajustada ao tamanho pedido (preenche e corta o que sobra, centrado) e gravada em JPG.</summary>
    public static byte[] Fit(Image src, Size size, long quality = 90)
    {
        using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Black);
            float k = Math.Max((float)size.Width / src.Width, (float)size.Height / src.Height);
            float w = src.Width * k, h = src.Height * k;
            g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);
        }
        var jpg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        using var ms = new MemoryStream();
        bmp.Save(ms, jpg, ep);
        return ms.ToArray();
    }

    /// <summary>
    /// procbar.ini com a seção [1600-N.jpg] acrescentada (mesma barra da última tela), ou null se já tem / não há arquivo.
    /// UTF-16 com BOM como o original.
    /// </summary>
    public static byte[]? ProcbarWith(byte[]? ini, int n)
    {
        if (ini is null || ini.Length < 2 || ini[0] != 0xFF || ini[1] != 0xFE) return null;
        var t = Encoding.Unicode.GetString(ini, 2, ini.Length - 2 & ~1);
        if (t.Contains($"[1600-{n}.jpg]", StringComparison.OrdinalIgnoreCase)) return null;
        var last = Regex.Matches(t, @"\[1600-\d+\.jpg\][^\[]*").LastOrDefault();
        string body = last is null ? "\r\nleft\t=\t10\r\ntop\t\t=\t20\r\n" : last.Value[(last.Value.IndexOf(']') + 1)..].TrimEnd() + "\r\n";
        if (!body.StartsWith("\r\n")) body = "\r\n" + body;
        var nl = t.EndsWith("\n") ? "" : "\r\n";
        var add = $"{nl}\r\n[1600-{n}.jpg]{body}";
        var bytes = Encoding.Unicode.GetBytes(t + add);
        return new byte[] { 0xFF, 0xFE }.Concat(bytes).ToArray();
    }
}
