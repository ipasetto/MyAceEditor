using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Mapa grande (tecla M) de uma instância: surfaces\ingame\&lt;pasta&gt;.dds, o pergaminho com o desenho da instância.
/// Conversão (cliente 1.5.3 DlgWorldMap.cpp, CECMapDlgsMgr::GetPixelsByPos/GetPosByPixels, e o comentário do
/// map_param.ini): a imagem vale 1024×1024; o desenho ocupa w×h (padrão 768×768) no centro, que corresponde a a×b peças de 1024 m (a, b = colunas e
/// linhas do configs\instance.txt), e o centro da imagem é o ponto (ox, oy) do jogo (padrão 0,0). configs\map_param.ini
/// ([MapConvertParamN] id a b ox oy w h) troca esses valores por instância.
/// </summary>
public sealed class InstanceMap : IDisposable
{
    public Bitmap Image { get; }
    readonly float _x0, _z0, _mx, _mz;   // ponto do jogo no pixel (0,0) e metros por pixel em X e em Z

    InstanceMap(Bitmap img, int a, int b, float ox, float oy, int w, int h)
    {
        Image = img;
        float contentW = img.Width * w / 1024f, contentH = img.Height * h / 1024f;
        _mx = a * 1024f / contentW; _mz = b * 1024f / contentH;
        _x0 = ox - img.Width / 2f * _mx; _z0 = oy + img.Height / 2f * _mz;
    }

    public PointF ToPixel(float x, float z) => new((x - _x0) / _mx, (_z0 - z) / _mz);
    public (float X, float Z) ToGame(float px, float py) => (_x0 + px * _mx, _z0 - py * _mz);

    public void Dispose() => Image.Dispose();

    /// <summary>Lê o mapa da instância da pasta element (null se o cliente não tiver imagem para ela).</summary>
    public static InstanceMap? Load(string elementDir, int tag)
    {
        if (tag <= 1 || MapTags.ClientMaps(elementDir) is not { } maps || !maps.TryGetValue(tag, out var cm)) return null;
        var surf = Path.Combine(elementDir, "surfaces.pck");
        if (!File.Exists(surf)) return null;
        byte[]? dds;
        using (var pck = new PckArchive(surf))
            dds = pck.Find($@"surfaces\ingame\{cm.Path}.dds") is { } e ? pck.Read(e) : null;
        if (dds is null || DdsDecoder.Decode(dds) is not { } im) return null;
        var bmp = new Bitmap(im.Width, im.Height, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, im.Width, im.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(im.Pixels, 0, bd.Scan0, im.Pixels.Length);
        bmp.UnlockBits(bd);
        var p = Params(elementDir).GetValueOrDefault(tag);
        // map_param.ini com a/b/w/h zerado daria escala infinita (NaN no desenho → OverflowException do GDI+): usa a grade do instance.txt
        return p is { } q && q.A > 0 && q.B > 0 && q.W > 0 && q.H > 0 ? new InstanceMap(bmp, q.A, q.B, q.Ox, q.Oy, q.W, q.H) : new InstanceMap(bmp, cm.Cols, cm.Rows, 0, 0, 768, 768);
    }

    /// <summary>configs\map_param.ini do configs.pck: id → (a, b, ox, oy, w, h). Lê as seções em ordem e para na primeira incompleta, como o cliente.</summary>
    static Dictionary<int, (int A, int B, float Ox, float Oy, int W, int H)> Params(string elementDir)
    {
        var res = new Dictionary<int, (int, int, float, float, int, int)>();
        try
        {
            using var pck = new PckArchive(Path.Combine(elementDir, "configs.pck"));
            if (pck.Find(@"configs\map_param.ini") is not { } e) return res;
            var bytes = pck.Read(e);
            var text = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
                : bytes.Length >= 2 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            var sections = Regex.Matches(text, @"(?ms)^\s*\[([^\]]+)\](.*?)(?=^\s*\[|\z)").ToDictionary(m => m.Groups[1].Value.Trim(), m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; sections.TryGetValue($"MapConvertParam{i}", out var body); i++)
            {
                string? V(string k) => Regex.Match(body, @"(?mi)^\s*" + k + @"\s*=\s*([-\d.]+)") is { Success: true } m ? m.Groups[1].Value : null;
                if (V("id") is not { } id || V("a") is not { } a || V("b") is not { } b || V("ox") is not { } ox || V("oy") is not { } oy || V("w") is not { } w || V("h") is not { } h) break;
                var inv = CultureInfo.InvariantCulture;
                res.TryAdd(int.Parse(id, inv), (int.Parse(a, inv), int.Parse(b, inv), float.Parse(ox, inv), float.Parse(oy, inv), int.Parse(w, inv), int.Parse(h, inv)));
            }
        }
        catch { }
        return res;
    }
}
