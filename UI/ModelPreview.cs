using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Prévia do modelo de um monstro/NPC/coletável numa imagem (desenho por software, sem OpenGL), para a janela de escolha:
/// .ecm → .ski (WorldModels.ResolveSki), visto de 3/4, com textura e luz simples. Guarda as imagens já feitas.
/// </summary>
internal sealed class ModelPreview : IDisposable
{
    readonly PckArchive? _pck;
    readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, PckEntry>? _ddsByName;
    readonly Dictionary<string, (int[] Px, int W, int H)?> _texCache = new(StringComparer.OrdinalIgnoreCase);   // texturas já decodificadas

    public ModelPreview(string? elementDir)
    {
        try { if (elementDir is not null && File.Exists(Path.Combine(elementDir, "models.pck"))) _pck = new PckArchive(Path.Combine(elementDir, "models.pck")); } catch { _pck = null; }
    }

    readonly Dictionary<string, (SkiModel Ski, string Dir)?> _skis = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Imagem do modelo girado yaw graus (arrastar na prévia gira). Guarda as imagens por ângulo de 10°.</summary>
    public Image? Render(string? ecm, int size = 240, float yaw = 0, bool transparent = false)
    {
        if (ecm is null || _pck is null) return null;
        int step = ((int)MathF.Round(yaw / 10f) % 36 + 36) % 36;
        var key = ecm + "|" + step + "|" + size + (transparent ? "|t" : "");
        if (_cache.TryGetValue(key, out var b)) return b;
        Bitmap? bmp = null;
        try
        {
            if (!_skis.TryGetValue(ecm, out var s)) _skis[ecm] = s = WorldModels.ResolveSki(_pck, ecm);
            if (s is var (ski, dir)) bmp = Draw(ski, dir, size, step * 10, transparent);
        }
        catch { bmp = null; }
        if (_cache.Count > 400) { foreach (var x in _cache.Values) x?.Dispose(); _cache.Clear(); }
        return _cache[key] = bmp;
    }

    Bitmap Draw(SkiModel ski, string dir, int size, float extraYaw, bool transparent)
    {
        // de frente (os modelos olham para −Z), giro de 35° e leve inclinação; projeção ortográfica ajustada ao tamanho
        float yaw = (215 + extraYaw) * MathF.PI / 180, pitch = 12 * MathF.PI / 180;
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw), cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        (float X, float Y, float Z) T(float x, float y, float z)
        {
            float x1 = x * cy - z * sy, z1 = x * sy + z * cy;
            return (x1, y * cp - z1 * sp, y * sp + z1 * cp);
        }
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var m in ski.Meshes)
            for (int i = 0; i < m.Vertices.Length; i += 8)
            {
                var p = T(m.Vertices[i], m.Vertices[i + 1], m.Vertices[i + 2]);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
        float scale = (size - 16) / Math.Max(1e-3f, Math.Max(maxX - minX, maxY - minY));
        float ox = size / 2f - (minX + maxX) / 2 * scale, oy = size / 2f + (minY + maxY) / 2 * scale;
        var color = new int[size * size];
        var depth = new float[size * size];
        Array.Fill(depth, float.MaxValue);
        Array.Fill(color, transparent ? 0 : unchecked((int)0xFF20262E));   // miniatura da lista: fundo transparente
        foreach (var m in ski.Meshes)
        {
            (int[] Px, int W, int H)? tex = null;
            if (m.Texture >= 0 && m.Texture < ski.Textures.Count && WorldModels.FindTexture(_pck!, dir, ski.Textures[m.Texture], ref _ddsByName) is { } te)
            {
                if (!_texCache.TryGetValue(te.Path, out tex))
                {
                    if (_texCache.Count > 128) _texCache.Clear();
                    tex = _texCache[te.Path] = DdsDecoder.Decode(_pck!.Read(te)) is { } img ? (img.Pixels, img.Width, img.Height) : null;
                }
            }
            var v = m.Vertices;
            for (int t = 0; t + 2 < m.Indices.Length; t += 3)
            {
                int a = m.Indices[t] * 8, b = m.Indices[t + 1] * 8, c = m.Indices[t + 2] * 8;
                if (c + 7 >= v.Length || a + 7 >= v.Length || b + 7 >= v.Length) continue;
                var pa = T(v[a], v[a + 1], v[a + 2]); var pb = T(v[b], v[b + 1], v[b + 2]); var pc = T(v[c], v[c + 1], v[c + 2]);
                // luz: normal média do triângulo contra uma luz de frente/cima
                var na = T(v[a + 3] + v[b + 3] + v[c + 3], v[a + 4] + v[b + 4] + v[c + 4], v[a + 5] + v[b + 5] + v[c + 5]);
                float len = MathF.Sqrt(na.X * na.X + na.Y * na.Y + na.Z * na.Z) + 1e-6f;
                float light = Math.Clamp(0.45f + 0.65f * Math.Abs((na.Y * 0.6f - na.Z * 0.8f) / len), 0.3f, 1.15f);
                float ax = ox + pa.X * scale, ay = oy - pa.Y * scale, bx = ox + pb.X * scale, by = oy - pb.Y * scale, cx = ox + pc.X * scale, cyy = oy - pc.Y * scale;
                float area = (bx - ax) * (cyy - ay) - (cx - ax) * (by - ay);
                if (Math.Abs(area) < 1e-6f) continue;
                int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(size - 1, (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, cyy)))), y1 = Math.Min(size - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, cyy))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((bx - px) * (cyy - py) - (cx - px) * (by - py)) / area;
                        float w1 = ((cx - px) * (ay - py) - (ax - px) * (cyy - py)) / area;
                        float w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = w0 * pa.Z + w1 * pb.Z + w2 * pc.Z;
                        int k = y * size + x;
                        if (z >= depth[k]) continue;
                        int rgb = unchecked((int)0xFFB4B4B4);
                        if (tex is { } tx)
                        {
                            float u = w0 * v[a + 6] + w1 * v[b + 6] + w2 * v[c + 6], vv = w0 * v[a + 7] + w1 * v[b + 7] + w2 * v[c + 7];
                            int tu = (int)((u - MathF.Floor(u)) * tx.W) % tx.W, tv = (int)((vv - MathF.Floor(vv)) * tx.H) % tx.H;
                            rgb = tx.Px[tv * tx.W + tu];
                            if (((rgb >> 24) & 0xFF) < 80) continue;   // partes transparentes (cabelo, folhas)
                        }
                        depth[k] = z;
                        int r = Math.Min(255, (int)(((rgb >> 16) & 0xFF) * light)), g = Math.Min(255, (int)(((rgb >> 8) & 0xFF) * light)), bl = Math.Min(255, (int)((rgb & 0xFF) * light));
                        color[k] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
                    }
            }
        }
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(color, 0, bd.Scan0, color.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    public void Dispose()
    {
        foreach (var x in _cache.Values) x?.Dispose();
        _pck?.Dispose();
    }
}
