using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Ícones dos itens lidos direto do surfaces.pck: atlas iconlist_ivtrm/ivtrf.dds (DXT1) + índice .txt (GBK).
/// .txt: largura, altura, linhas, colunas; depois 1 nome de arquivo por ícone, na ordem da grade.
/// O campo file_icon do elements.data aponta para um desses nomes (casamos pelo nome do arquivo).
/// </summary>
public sealed class IconAtlas
{
    sealed record Sheet(int[] Px, int W, int IconW, int IconH, int Cols);

    readonly List<Sheet> _sheets = new();
    readonly Dictionary<string, (Sheet Sheet, int I)> _index = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _index.Count;

    /// <summary>Nomes de arquivo de todos os ícones do atlas (ex.: "x.dds"), para o seletor de ícone.</summary>
    public IEnumerable<string> Names => _index.Keys;

    /// <summary>
    /// Carrega os atlas de itens do surfaces.pck: ivtrm (masculino) primeiro, ivtrf (feminino) completa
    /// o que faltar. Retorna null se nenhum existir.
    /// </summary>
    public static IconAtlas? Load(string surfacesPck, params string[] sets) => Load(surfacesPck, null, sets);

    public static IconAtlas? Load(string surfacesPck, LoadContext? context, params string[] sets)
    {
        context?.Check();
        if (sets.Length == 0) sets = new[] { "iconlist_ivtrm", "iconlist_ivtrf" };
        using var pck = new PckArchive(surfacesPck, context?.Token ?? default);
        var atlas = new IconAtlas();
        foreach (var set in sets)
        {
            context?.Check();
            var dds = pck.Find($@"surfaces\iconset\{set}.dds");
            var txt = pck.Find($@"surfaces\iconset\{set}.txt");
            if (dds is null || txt is null) continue;
            var img = DdsDecoder.Decode(pck.Read(dds), context?.Token ?? default);
            if (img is null) continue;

            var lines = Encoding.GetEncoding(936).GetString(pck.Read(txt))
                .Split('\n').Select(l => l.Trim('\r', ' ', '\0')).ToArray();
            var sheet = new Sheet(img.Value.Pixels, img.Value.Width, int.Parse(lines[0]), int.Parse(lines[1]), int.Parse(lines[3]));
            atlas._sheets.Add(sheet);
            for (int i = 4; i < lines.Length; i++)
            {
                if ((i & 255) == 0) context?.Check();
                if (lines[i].Length > 0) atlas._index.TryAdd(lines[i], (sheet, i - 4));
            }
        }
        return atlas._sheets.Count > 0 ? atlas : null;
    }

    static string NameOf(string fileIcon) => Path.GetFileName(fileIcon.Replace('/', '\\'));

    /// <summary>O ícone existe de fato no atlas (sem contar o fallback unknown.dds).</summary>
    public bool Has(string fileIcon) => _index.ContainsKey(NameOf(fileIcon));

    /// <summary>
    /// Recorta o ícone para um file_icon (ex.: "Surfaces\图标\物品\x.dds"). Se o jogo não tiver o ícone,
    /// devolve unknown.dds (o mesmo que o client mostra). Null só se nem esse existir.
    /// </summary>
    public Bitmap? Get(string fileIcon)
    {
        if (!_index.TryGetValue(NameOf(fileIcon), out var hit) && !_index.TryGetValue("unknown.dds", out hit)) return null;
        var (s, i) = hit;
        int x0 = i % s.Cols * s.IconW, y0 = i / s.Cols * s.IconH;
        if ((y0 + s.IconH) * s.W > s.Px.Length) return null;

        var bmp = new Bitmap(s.IconW, s.IconH, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, s.IconW, s.IconH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < s.IconH; y++)
            Marshal.Copy(s.Px, (y0 + y) * s.W + x0, bd.Scan0 + y * bd.Stride, s.IconW);
        bmp.UnlockBits(bd);
        return bmp;
    }

    /// <summary>Procura o surfaces.pck a partir do caminho do elements.data (…\element\data\elements.data).</summary>
    public static string? FindSurfacesPck(string elementsPath)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            foreach (var c in new[] { Path.Combine(dir, "surfaces.pck"), Path.Combine(dir, "element", "surfaces.pck") })
                if (File.Exists(c)) return c;
        }
        return null;
    }
}
