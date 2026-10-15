using System.Drawing;
using System.Drawing.Imaging;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Carrega o ícone de um item a partir da pasta de ícones extraída do .pck (pelo sPCK).
/// Suporta formatos nativos (PNG/BMP/JPG/GIF) e TGA (comum no Perfect World).
/// </summary>
public static class IconLoader
{
    private static readonly string[] Exts = { "", ".tga", ".png", ".bmp", ".jpg", ".dds" };

    public static Image? Load(string iconsRoot, string fileIcon)
    {
        if (string.IsNullOrWhiteSpace(iconsRoot) || string.IsNullOrWhiteSpace(fileIcon)) return null;
        var rel = fileIcon.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var baseNoExt = Path.Combine(iconsRoot, Path.ChangeExtension(rel, null) ?? rel);
        foreach (var e in Exts)
        {
            var p = baseNoExt + e;
            if (!File.Exists(p)) continue;
            try
            {
                return e == ".tga" ? LoadTga(p)
                     : e == ".dds" ? null                 // DDS: ainda não suportado
                     : Image.FromFile(p);
            }
            catch { return null; }
        }
        return null;
    }

    // Decoder TGA mínimo: truecolor 24/32 bits, sem compressão (tipo 2) e RLE (tipo 10).
    private static Bitmap? LoadTga(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 18) return null;
        int idLen = d[0];
        int type = d[2];
        int w = d[12] | (d[13] << 8);
        int h = d[14] | (d[15] << 8);
        int bpp = d[16];
        int desc = d[17];
        if (w <= 0 || h <= 0 || (bpp != 24 && bpp != 32) || (type != 2 && type != 10)) return null;
        int bytespp = bpp / 8;
        int pos = 18 + idLen;
        var px = new byte[w * h * bytespp];

        if (type == 2)
        {
            if (pos + px.Length > d.Length) return null;
            Array.Copy(d, pos, px, 0, px.Length);
        }
        else // RLE
        {
            int o = 0;
            while (o < px.Length && pos < d.Length)
            {
                int packet = d[pos++];
                int count = (packet & 0x7F) + 1;
                if ((packet & 0x80) != 0) // run-length
                {
                    if (pos + bytespp > d.Length) break;
                    for (int i = 0; i < count && o < px.Length; i++, o += bytespp)
                        Array.Copy(d, pos, px, o, bytespp);
                    pos += bytespp;
                }
                else // raw
                {
                    for (int i = 0; i < count && o < px.Length; i++, o += bytespp)
                    {
                        if (pos + bytespp > d.Length) break;
                        Array.Copy(d, pos, px, o, bytespp);
                        pos += bytespp;
                    }
                }
            }
        }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bool topLeft = (desc & 0x20) != 0;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    int srcY = topLeft ? y : (h - 1 - y);
                    byte* row = (byte*)bd.Scan0 + y * bd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        int si = (srcY * w + x) * bytespp;
                        byte b = px[si], g = px[si + 1], r = px[si + 2];
                        byte a = bytespp == 4 ? px[si + 3] : (byte)255;
                        int di = x * 4;
                        row[di] = b; row[di + 1] = g; row[di + 2] = r; row[di + 3] = a;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(bd); }
        return bmp;
    }
}
