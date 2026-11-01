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
    // atlas DXT guardado comprimido (Dds) e cada ícone decodificado na hora (DdsDecoder.DecodeRegion): 2 atlas 4096×4096
    // = 16 MB em vez de 128 MB. Px só para formatos sem blocos (raros), decodificados inteiros como antes.
    sealed record Sheet(int[]? Px, byte[]? Dds, int W, int H, int IconW, int IconH, int Cols);

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
            var bytes = pck.Read(dds);
            var lines = Encoding.GetEncoding(936).GetString(pck.Read(txt))
                .Split('\n').Select(l => l.Trim('\r', ' ', '\0')).ToArray();
            int iw = int.Parse(lines[0]), ih = int.Parse(lines[1]), cols = int.Parse(lines[3]);
            Sheet sheet;
            if (DdsDecoder.BlockFormat(bytes) is not null && iw % 4 == 0 && ih % 4 == 0)
                sheet = new Sheet(null, bytes, BitConverter.ToInt32(bytes, 16), BitConverter.ToInt32(bytes, 12), iw, ih, cols);
            else
            {
                var img = DdsDecoder.Decode(bytes, context?.Token ?? default);
                if (img is null) continue;
                sheet = new Sheet(img.Value.Pixels, null, img.Value.Width, img.Value.Height, iw, ih, cols);
            }
            atlas._sheets.Add(sheet);
            for (int i = 4; i < lines.Length; i++)
            {
                if ((i & 255) == 0) context?.Check();
                if (lines[i].Length > 0) atlas._index.TryAdd(lines[i], (sheet, i - 4));
            }
        }
        return atlas._sheets.Count > 0 ? atlas : null;
    }

    /// <summary>Teste (--icon-check): cada ícone do jeito novo (só os blocos dele) = recorte do atlas inteiro decodificado.</summary>
    public (int Icons, int Different) VerifyAgainstFullDecode()
    {
        int n = 0, bad = 0;
        var fullOf = new Dictionary<Sheet, int[]>(ReferenceEqualityComparer.Instance);
        foreach (var (name, (s, i)) in _index)
        {
            if (s.Dds is null) continue;
            if (!fullOf.TryGetValue(s, out var full)) fullOf[s] = full = DdsDecoder.Decode(s.Dds)!.Value.Pixels;
            int x0 = i % s.Cols * s.IconW, y0 = i / s.Cols * s.IconH;
            if (y0 + s.IconH > s.H) continue;
            var part = DdsDecoder.DecodeRegion(s.Dds, x0, y0, s.IconW, s.IconH)!;
            n++;
            for (int y = 0; y < s.IconH; y++)
                if (!full.AsSpan((y0 + y) * s.W + x0, s.IconW).SequenceEqual(part.AsSpan(y * s.IconW, s.IconW))) { bad++; break; }
        }
        return (n, bad);
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
        if (y0 + s.IconH > s.H || x0 + s.IconW > s.W) return null;
        // comprimido: só os blocos deste ícone; senão recorta da imagem inteira já decodificada
        int[]? px = s.Dds is { } dds ? DdsDecoder.DecodeRegion(dds, x0, y0, s.IconW, s.IconH) : s.Px;
        if (px is null) return null;
        int stride = s.Dds is null ? s.W : s.IconW, start = s.Dds is null ? y0 * s.W + x0 : 0;

        var bmp = new Bitmap(s.IconW, s.IconH, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, s.IconW, s.IconH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < s.IconH; y++)
            Marshal.Copy(px, start + y * stride, bd.Scan0 + y * bd.Stride, s.IconW);
        bmp.UnlockBits(bd);
        return bmp;
    }

    /// <summary>
    /// Acrescenta um ícone novo aos dois atlas (ivtrm e ivtrf), como o "Glue" do IconListEditor (PW-KB §S15), mas sem
    /// remontar: o ícone vai na próxima casa livre da grade (o .dds tem espaço até a altura dele; o .txt diz quantas linhas
    /// estão em uso) e só os blocos DXT1 daquela casa são escritos — o resto do atlas fica byte a byte. A imagem é
    /// redimensionada para o tamanho do ícone. Grava o surfaces.pck (com backup) e devolve o nome ("x.dds").
    /// </summary>
    public static string AddIcon(string surfacesPck, string name, Bitmap image)
    {
        name = Path.GetFileNameWithoutExtension(name.Trim()) + ".dds";
        if (name.Length <= 4 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException(UI.Loc.T("icon.badName", "Nome de ícone inválido."));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding(936);
        if (gbk.GetString(gbk.GetBytes(name)) != name) throw new ArgumentException(UI.Loc.T("icon.notGbk", "O nome do ícone só pode ter letras que o jogo grava (GBK): sem acentos."));
        var files = new Dictionary<string, byte[]>();
        using (var pck = new PckArchive(surfacesPck))
        {
            foreach (var set in new[] { "iconlist_ivtrm", "iconlist_ivtrf" })
            {
                if (pck.Find($@"surfaces\iconset\{set}.dds") is not { } de || pck.Find($@"surfaces\iconset\{set}.txt") is not { } te) continue;
                var dds = pck.Read(de);
                var rawTxt = gbk.GetString(pck.Read(te)); var nl = rawTxt.Contains("\r\n") ? "\r\n" : "\n";
                var lines = rawTxt.Split('\n').Select(l => l.Trim('\r', ' ', '\0')).ToList();
                int iw = int.Parse(lines[0]), ih = int.Parse(lines[1]), rows = int.Parse(lines[2]), cols = int.Parse(lines[3]);
                var names = lines.Skip(4).Where(l => l.Length > 0).ToList();
                if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException(string.Format(UI.Loc.T("icon.exists", "Já existe um ícone chamado {0}."), name));
                // cabeçalho DDS: altura @12, largura @16, FourCC @84; dados do nível 0 a partir de 128
                int h = BitConverter.ToInt32(dds, 12), w = BitConverter.ToInt32(dds, 16);
                if (Encoding.ASCII.GetString(dds, 84, 4) != "DXT1" || iw % 4 != 0 || ih % 4 != 0)
                    throw new NotSupportedException(string.Format(UI.Loc.T("icon.format", "{0}.dds não é DXT1 com ícones múltiplos de 4 px: não sei gravar."), set));
                int idx = names.Count;
                if (idx >= rows * cols) rows++;
                if ((idx / cols + 1) * ih > h || cols * iw > w) throw new InvalidOperationException(string.Format(UI.Loc.T("icon.full", "{0}.dds está cheio (sem casa livre)."), set));
                using var cell = new Bitmap(iw, ih, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(cell)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(image, 0, 0, iw, ih); }
                var px = new int[iw * ih];
                var bd = cell.LockBits(new Rectangle(0, 0, iw, ih), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(bd.Scan0, px, 0, px.Length); cell.UnlockBits(bd);
                int x0 = idx % cols * iw, y0 = idx / cols * ih, rowBytes = (w + 3) / 4 * 8;
                for (int by = 0; by < ih / 4; by++)
                    for (int bx = 0; bx < iw / 4; bx++)
                        Dxt1Block(px, iw, bx * 4, by * 4).CopyTo(dds, 128 + ((y0 / 4 + by) * rowBytes) + (x0 / 4 + bx) * 8);
                names.Add(name);
                var txt = string.Join(nl, new[] { iw.ToString(), ih.ToString(), rows.ToString(), cols.ToString() }.Concat(names)) + nl;
                files[de.Path] = dds; files[te.Path] = gbk.GetBytes(txt);
            }
        }
        if (files.Count == 0) throw new FileNotFoundException(UI.Loc.T("icon.noAtlas", "O surfaces.pck não tem iconset\\iconlist_ivtrm/ivtrf."));
        PckWriter.Write(surfacesPck, files);
        return name;
    }

    /// <summary>Bloco DXT1 de 4×4 (caixa mín/máx por canal; com pixel transparente usa o modo de 3 cores + transparente).</summary>
    static byte[] Dxt1Block(int[] px, int stride, int x, int y)
    {
        var c = new Color[16];
        bool alpha = false;
        int r0 = 255, g0 = 255, b0 = 255, r1 = 0, g1 = 0, b1 = 0;
        for (int i = 0; i < 16; i++)
        {
            c[i] = Color.FromArgb(px[(y + i / 4) * stride + x + i % 4]);
            if (c[i].A < 128) { alpha = true; continue; }
            r0 = Math.Min(r0, c[i].R); g0 = Math.Min(g0, c[i].G); b0 = Math.Min(b0, c[i].B);
            r1 = Math.Max(r1, c[i].R); g1 = Math.Max(g1, c[i].G); b1 = Math.Max(b1, c[i].B);
        }
        static ushort P(int r, int g, int b) => (ushort)((r >> 3) << 11 | (g >> 2) << 5 | b >> 3);
        static (int R, int G, int B) U(ushort v) { int r = v >> 11 & 31, g = v >> 5 & 63, b = v & 31; return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2); }
        ushort hi = r1 < r0 ? (ushort)0 : P(r1, g1, b1), lo = r1 < r0 ? (ushort)0 : P(r0, g0, b0);
        ushort e0, e1;
        if (alpha) { e0 = Math.Min(hi, lo); e1 = Math.Max(hi, lo); }       // e0 <= e1: 3 cores + transparente
        else { e0 = Math.Max(hi, lo); e1 = Math.Min(hi, lo); if (e0 == e1 && e0 < 0xFFFF) e0++; } // e0 > e1: 4 cores
        var (ar, ag, ab) = U(e0); var (br, bg, bb) = U(e1);
        var pal = alpha
            ? new[] { (ar, ag, ab), (br, bg, bb), ((ar + br) / 2, (ag + bg) / 2, (ab + bb) / 2) }
            : new[] { (ar, ag, ab), (br, bg, bb), ((2 * ar + br) / 3, (2 * ag + bg) / 3, (2 * ab + bb) / 3), ((ar + 2 * br) / 3, (ag + 2 * bg) / 3, (ab + 2 * bb) / 3) };
        uint idx = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 3;
            if (!alpha || c[i].A >= 128)
            {
                int bd = int.MaxValue;
                for (int k = 0; k < pal.Length; k++)
                {
                    int dr = c[i].R - pal[k].Item1, dg = c[i].G - pal[k].Item2, db = c[i].B - pal[k].Item3, d = dr * dr + dg * dg + db * db;
                    if (d < bd) { bd = d; best = k; }
                }
            }
            idx |= (uint)best << (2 * i);
        }
        var o = new byte[8];
        BitConverter.GetBytes(e0).CopyTo(o, 0); BitConverter.GetBytes(e1).CopyTo(o, 2); BitConverter.GetBytes(idx).CopyTo(o, 4);
        return o;
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
