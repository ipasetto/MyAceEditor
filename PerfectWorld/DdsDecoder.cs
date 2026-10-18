namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Decodificador DDS mínimo: DXT1, DXT3, DXT5 e RGB(A) sem compressão (24/32bpp).
/// Saída: pixels ARGB 32bpp (formato de Bitmap Format32bppArgb), linha a linha.
/// </summary>
public static class DdsDecoder
{
    public static (int[] Pixels, int Width, int Height)? Decode(byte[] d, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (d.Length < 128 || BitConverter.ToUInt32(d, 0) != 0x20534444) return null; // "DDS "
        int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
        uint pfFlags = BitConverter.ToUInt32(d, 80), fourCC = BitConverter.ToUInt32(d, 84);
        int bpp = BitConverter.ToInt32(d, 88);
        var px = new int[w * h];
        const int data = 128;

        if ((pfFlags & 0x4) != 0)
        {
            switch (fourCC)
            {
                case 0x31545844: Blocks(d, data, w, h, px, 8, alphaMode: 0, cancellationToken); break;  // DXT1
                case 0x33545844: Blocks(d, data, w, h, px, 16, alphaMode: 3, cancellationToken); break; // DXT3
                case 0x35545844: Blocks(d, data, w, h, px, 16, alphaMode: 5, cancellationToken); break; // DXT5
                default: return null;
            }
            return (px, w, h);
        }

        if (bpp != 32 && bpp != 24) return null;
        uint rM = BitConverter.ToUInt32(d, 92), gM = BitConverter.ToUInt32(d, 96), bM = BitConverter.ToUInt32(d, 100), aM = BitConverter.ToUInt32(d, 104);
        bool hasAlpha = (pfFlags & 0x1) != 0 && aM != 0;
        int step = bpp / 8;
        for (int i = 0, p = data; i < px.Length && p + step <= d.Length; i++, p += step)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            uint v = step == 4 ? BitConverter.ToUInt32(d, p) : (uint)(d[p] | d[p + 1] << 8 | d[p + 2] << 16);
            px[i] = (int)((hasAlpha ? Chan(v, aM) : 255u) << 24 | Chan(v, rM) << 16 | Chan(v, gM) << 8 | Chan(v, bM));
        }
        return (px, w, h);
    }

    static uint Chan(uint v, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift;
        return ((v & mask) >> shift) * 255 / max;
    }

    static void Blocks(byte[] d, int off, int w, int h, int[] px, int blockSize, int alphaMode, CancellationToken cancellationToken)
    {
        Span<int> colors = stackalloc int[4];
        Span<byte> alphas = stackalloc byte[8];
        for (int by = 0; by < (h + 3) / 4; by++)
        for (int bx = 0; bx < (w + 3) / 4; bx++, off += blockSize)
        {
            if ((bx & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (off + blockSize > d.Length) return;
            int c = alphaMode == 0 ? off : off + 8;
            ushort c0 = BitConverter.ToUInt16(d, c), c1 = BitConverter.ToUInt16(d, c + 2);
            colors[0] = Rgb565(c0); colors[1] = Rgb565(c1);
            if (c0 > c1 || alphaMode != 0)
            {
                colors[2] = Mix(colors[0], colors[1], 2, 1, 3);
                colors[3] = Mix(colors[0], colors[1], 1, 2, 3);
            }
            else
            {
                colors[2] = Mix(colors[0], colors[1], 1, 1, 2);
                colors[3] = 0; // transparente (DXT1 1-bit alpha)
            }
            uint idx = BitConverter.ToUInt32(d, c + 4);

            if (alphaMode == 5)
            {
                byte a0 = d[off], a1 = d[off + 1];
                alphas[0] = a0; alphas[1] = a1;
                for (int k = 2; k < 8; k++)
                    alphas[k] = a0 > a1 ? (byte)(((8 - k) * a0 + (k - 1) * a1) / 7)
                              : k < 6 ? (byte)(((6 - k) * a0 + (k - 1) * a1) / 5) : (byte)(k == 6 ? 0 : 255);
            }
            ulong aBits = alphaMode == 5 ? BitConverter.ToUInt64(d, off) >> 16 : 0;

            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int X = bx * 4 + x, Y = by * 4 + y, k = y * 4 + x;
                int col = colors[(int)(idx >> (2 * k)) & 3];
                if (alphaMode == 3)
                {
                    int a4 = (d[off + k / 2] >> (k % 2 * 4)) & 0xF;
                    col = (col & 0xFFFFFF) | (a4 * 17) << 24;
                }
                else if (alphaMode == 5)
                    col = (col & 0xFFFFFF) | alphas[(int)(aBits >> (3 * k)) & 7] << 24;
                if (X < w && Y < h) px[Y * w + X] = col;
            }
        }
    }

    static int Rgb565(ushort c)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        return unchecked((int)0xFF000000) | (r << 3 | r >> 2) << 16 | (g << 2 | g >> 4) << 8 | (b << 3 | b >> 2);
    }

    static int Mix(int a, int b, int wa, int wb, int div)
    {
        int Ch(int s) => (((a >> s) & 0xFF) * wa + ((b >> s) & 0xFF) * wb) / div;
        return unchecked((int)0xFF000000) | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
