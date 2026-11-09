using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Luz e sombra do chão (&lt;mapa&gt;_N.t2lm de dia e .t2lm1 de noite: "ML2T" v2, nÁreas 16, lado 256, ?, tamanho de cada,
/// offsets[16]; cada área de 256 m é um DDS DXT1 256×256, 1 m por ponto, tamanho fixo → regravar no lugar).
/// Refaz só onde mudou, sem apagar o que o editor oficial gravou: a luz de cada ponto é multiplicada por
/// (a + b·luzNova) / (a + b·luzVelha), com luz = max(0, normal·sol) × (sem sombra ? 1 : 0); a direção do sol e a, b saem
/// da própria luz gravada (a direção que melhor explica o claro/escuro do relevo). Sombra = o raio até o sol bate no relevo
/// ou num brush do map.bht (construções sólidas). Usado no Salvar terreno (alturas velhas × novas) e no Salvar dos Objetos
/// do mundo (map.bht velho × novo).
/// </summary>
public sealed class LightmapBaker
{
    readonly TerrainFile _t;
    readonly Func<float, float, float?> _oldH, _newH;
    readonly BrushSet? _oldB, _newB;
    public Vector3 Sun { get; private set; } = Vector3.Normalize(new Vector3(-0.5f, 0.75f, 0.4f));   // para o sol (y para cima)
    float _a = 0.35f, _b = 0.65f;

    /// <summary>Brushes perto (pré-filtro do map.bht para os raios de sombra).</summary>
    public sealed class BrushSet(BhtFile bht, IReadOnlyList<int> ids)
    {
        public bool Hit(Vector3 a, Vector3 b)
        {
            foreach (int i in ids)
            {
                var bb = bht.Box(i);
                float lx = MathF.Min(a.X, b.X), hx = MathF.Max(a.X, b.X), ly = MathF.Min(a.Y, b.Y), hy = MathF.Max(a.Y, b.Y), lz = MathF.Min(a.Z, b.Z), hz = MathF.Max(a.Z, b.Z);
                if (bb.X1 < lx || bb.X0 > hx || bb.Y1 < ly || bb.Y0 > hy || bb.Z1 < lz || bb.Z0 > hz) continue;
                if (bht.SegmentHitOne(i, a.X, a.Y, a.Z, b.X, b.Y, b.Z)) return true;
            }
            return false;
        }
        public static BrushSet Near(BhtFile bht, float x0, float z0, float x1, float z1) => new(bht, bht.Inside(x0, -1e6f, z0, x1, 1e6f, z1, touching: true));
    }

    public LightmapBaker(TerrainFile t, Func<float, float, float?> oldH, Func<float, float, float?> newH, BrushSet? oldB = null, BrushSet? newB = null)
    {
        _t = t; _oldH = oldH; _newH = newH; _oldB = oldB; _newB = newB;
    }

    const float Reach = 160;   // metros até onde um morro/construção faz sombra

    Vector3 Normal(Func<float, float, float?> h, float x, float z)
    {
        float hl = h(x - 1, z) ?? 0, hr = h(x + 1, z) ?? 0, hd = h(x, z - 1) ?? 0, hu = h(x, z + 1) ?? 0;
        return Vector3.Normalize(new Vector3(hl - hr, 2, hd - hu));
    }

    float Light(Func<float, float, float?> h, BrushSet? brushes, float x, float z)
    {
        if (h(x, z) is not float y) return 1;
        float lam = MathF.Max(0, Vector3.Dot(Normal(h, x, z), Sun));
        if (lam <= 0) return 0;
        // sombra do relevo: anda na direção do sol (passos de 2 m)
        var dir = Sun; float horiz = MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        if (horiz > 1e-3f)
        {
            float rise = dir.Y / horiz;
            for (float d = 2; d < Reach; d += 2)
            {
                float px = x + dir.X / horiz * d, pz = z + dir.Z / horiz * d;
                if (h(px, pz) is float g && g > y + 0.3f + rise * d) return 0;
                if (y + rise * d > 900) break;
            }
        }
        if (brushes is not null)
        {
            var p = new Vector3(x, y + 0.3f, z);
            if (brushes.Hit(p, p + Sun * Reach)) return 0;
        }
        return lam;
    }

    /// <summary>
    /// Acha a direção do sol e a, b pela luz gravada da área (correlação do brilho com normal·sol no relevo velho, sem os
    /// pontos escuros que são sombra). Falso = área sem relevo que dê para ler (fica o sol padrão).
    /// </summary>
    public bool Calibrate(int[] px, int side, float x0, float zTop)
    {
        var pts = new List<(Vector3 N, float L)>();
        float m = TerrainFile.AreaM / side;   // metros por ponto (lado 128/256/512 conforme o mapa)
        int step = Math.Max(1, side / 42);
        for (int v = 2; v < side; v += step)
            for (int u = 2; u < side; u += step)
            {
                float x = x0 + (u + 0.5f) * m, z = zTop - (v + 0.5f) * m;
                if (_oldH(x, z) is null) continue;
                int c = px[v * side + u];
                float l = (((c >> 16) & 255) * 0.3f + ((c >> 8) & 255) * 0.59f + (c & 255) * 0.11f) / 255f;
                pts.Add((Normal(_oldH, x, z), l));
            }
        if (pts.Count < 200 || pts.Select(p => p.N.Y).Min() > 0.995f) return false;
        double best = -2; Vector3 bestSun = Sun;
        for (int el = 15; el <= 85; el += 5)
            for (int az = 0; az < 360; az += 6)
            {
                float e = el * MathF.PI / 180, a = az * MathF.PI / 180;
                var s = new Vector3(MathF.Cos(e) * MathF.Cos(a), MathF.Sin(e), MathF.Cos(e) * MathF.Sin(a));
                double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0; int n = 0;
                foreach (var (N, L) in pts)
                {
                    double lam = Math.Max(0, Vector3.Dot(N, s));
                    sx += lam; sy += L; sxx += lam * lam; syy += L * L; sxy += lam * L; n++;
                }
                double cov = sxy - sx * sy / n, vx = sxx - sx * sx / n, vy = syy - sy * sy / n;
                double r = vx > 1e-9 && vy > 1e-9 ? cov / Math.Sqrt(vx * vy) : -1;
                if (r > best) { best = r; bestSun = s; }
            }
        if (best < 0.15) return false;
        Sun = bestSun;
        // a + b·lam pelos mínimos quadrados
        double tx = 0, ty = 0, txx = 0, txy = 0; int cnt = 0;
        foreach (var (N, L) in pts) { double lam = Math.Max(0, Vector3.Dot(N, Sun)); tx += lam; ty += L; txx += lam * lam; txy += lam * L; cnt++; }
        double den = cnt * txx - tx * tx;
        if (den > 1e-9) { _b = (float)Math.Clamp((cnt * txy - tx * ty) / den, 0.05, 2); _a = (float)Math.Clamp((ty - _b * tx) / cnt, 0.02, 1.5); }
        return true;
    }

    /// <summary>
    /// Refaz uma área (DDS DXT1 256×256 da luz) nos pontos dentro dos retângulos: devolve o DDS novo (mesmo tamanho) e quantos
    /// pontos mudaram. soft = 0..1 quanto da mudança vale (noite: metade).
    /// </summary>
    public (byte[] Dds, int Changed) Bake(byte[] dds, float x0, float zTop, IReadOnlyList<(float X0, float Z0, float X1, float Z1)> rects, float soft = 1)
    {
        if (DdsDecoder.Decode(dds) is not { } img || img.Width != img.Height || img.Width % 4 != 0 || img.Width < 16) return (dds, 0);
        var px = img.Pixels; int side = img.Width, bs = side / 4;
        float m = TerrainFile.AreaM / side;
        int changed = 0;
        var blocks = new bool[bs * bs];
        for (int v = 0; v < side; v++)
            for (int u = 0; u < side; u++)
            {
                float x = x0 + (u + 0.5f) * m, z = zTop - (v + 0.5f) * m;
                bool inside = false;
                foreach (var r in rects) if (x >= r.X0 && x <= r.X1 && z >= r.Z0 && z <= r.Z1) { inside = true; break; }
                if (!inside) continue;
                float lo = _a + _b * Light(_oldH, _oldB, x, z), ln = _a + _b * Light(_newH, _newB ?? _oldB, x, z);
                float k = ln / MathF.Max(lo, 0.02f);
                k = 1 + (k - 1) * soft;
                if (MathF.Abs(k - 1) < 0.02f) continue;
                k = Math.Clamp(k, 0.15f, 4f);
                int c = px[v * side + u];
                int rr = Math.Clamp((int)(((c >> 16) & 255) * k), 0, 255), gg = Math.Clamp((int)(((c >> 8) & 255) * k), 0, 255), bb = Math.Clamp((int)((c & 255) * k), 0, 255);
                px[v * side + u] = unchecked((int)0xFF000000) | (rr << 16) | (gg << 8) | bb;
                blocks[(v >> 2) * bs + (u >> 2)] = true;
                changed++;
            }
        if (changed == 0) return (dds, 0);
        var o = (byte[])dds.Clone();
        Dxt1.Encode(px, side, side, o, 128, (bx, by) => blocks[(by >> 2) * bs + (bx >> 2)]);   // só os blocos mexidos; o resto fica igual
        return (o, changed);
    }

    /// <summary>
    /// Refaz o .t2lm (e o .t2lm1, à metade) de um pedaço nos retângulos (metros do mundo, já com a folga da sombra). Devolve
    /// arquivo → bytes dos que mudaram e quantos pontos mudaram.
    /// </summary>
    public static (Dictionary<string, byte[]> Files, int Changed) Piece(TerrainFile t, int piece, IReadOnlyList<(float X0, float Z0, float X1, float Z1)> rects,
        Func<float, float, float?> oldH, Func<float, float, float?> newH, BrushSet? oldB = null, BrushSet? newB = null)
    {
        var res = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        int per = (int)(t.CellsPerPiece * t.Cell / TerrainFile.AreaM), pr = (piece - 1) / t.Cols, pc = (piece - 1) % t.Cols;
        float pm = t.CellsPerPiece * t.Cell;
        var baker = new LightmapBaker(t, oldH, newH, oldB, newB);
        bool calibrated = false;
        foreach (var (suffix, soft) in new[] { ("", 1f), ("1", 0.5f) })
        {
            var path = Path.Combine(t.Dir, $"{t.Name}_{piece}.t2lm{suffix}");
            if (!File.Exists(path)) continue;
            var d = File.ReadAllBytes(path);
            if (d.Length < 0x18 || BitConverter.ToUInt32(d, 0) != 0x54324C4D) continue;
            int n = BitConverter.ToInt32(d, 8);
            var outB = d; bool any = false;
            for (int k = 0; k < n && k < per * per; k++)
            {
                int ar = k / per, ac = k % per;
                float x0 = -t.Width / 2 + pc * pm + ac * TerrainFile.AreaM, zTop = t.Height / 2 - pr * pm - ar * TerrainFile.AreaM;
                var mine = rects.Where(r => r.X1 >= x0 && r.X0 <= x0 + TerrainFile.AreaM && r.Z1 >= zTop - TerrainFile.AreaM && r.Z0 <= zTop).ToList();
                if (mine.Count == 0) continue;
                int o = BitConverter.ToInt32(d, 0x18 + k * 4), end = k + 1 < n ? BitConverter.ToInt32(d, 0x18 + (k + 1) * 4) : d.Length;
                if (o <= 0 || end <= o || end > d.Length) continue;
                var dds = d.AsSpan(o, end - o).ToArray();
                if (!calibrated && DdsDecoder.Decode(dds) is { } cal && cal.Width == cal.Height) calibrated = baker.Calibrate(cal.Pixels, cal.Width, x0, zTop);
                var (nd, ch) = baker.Bake(dds, x0, zTop, mine, soft);
                if (ch == 0 || nd.Length != dds.Length) continue;
                if (!any) { outB = (byte[])d.Clone(); any = true; }
                nd.CopyTo(outB, o);
                total += ch;
            }
            if (any) res[path] = outB;
        }
        return (res, total);
    }
}

/// <summary>Compressor DXT1 simples (cores 565, extremos pela caixa de cores do bloco, 4 tons) para regravar a luz do chão.</summary>
public static class Dxt1
{
    static int To565(int r, int g, int b) => (r * 31 / 255 << 11) | (g * 63 / 255 << 5) | (b * 31 / 255);
    static (int R, int G, int B) From565(int c) => (((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31);

    /// <summary>Comprime ARGB (w×h, múltiplos de 4) para DXT1 em dst a partir de off.</summary>
    public static void Encode(int[] px, int w, int h, byte[] dst, int off, Func<int, int, bool>? only = null)
    {
        int o = off;
        Span<int> rr = stackalloc int[16], gg = stackalloc int[16], bb = stackalloc int[16];
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
            {
                if (only is not null && !only(bx, by)) { o += 8; continue; }
                int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
                for (int i = 0; i < 16; i++)
                {
                    int c = px[(by + i / 4) * w + bx + i % 4];
                    rr[i] = (c >> 16) & 255; gg[i] = (c >> 8) & 255; bb[i] = c & 255;
                    minR = Math.Min(minR, rr[i]); minG = Math.Min(minG, gg[i]); minB = Math.Min(minB, bb[i]);
                    maxR = Math.Max(maxR, rr[i]); maxG = Math.Max(maxG, gg[i]); maxB = Math.Max(maxB, bb[i]);
                }
                int c0 = To565(maxR, maxG, maxB), c1 = To565(minR, minG, minB);
                if (c0 < c1) (c0, c1) = (c1, c0);
                uint idx = 0;
                if (c0 != c1)
                {
                    var p0 = From565(c0); var p1 = From565(c1);
                    var pal = new (int R, int G, int B)[] { p0, p1, ((2 * p0.R + p1.R) / 3, (2 * p0.G + p1.G) / 3, (2 * p0.B + p1.B) / 3), ((p0.R + 2 * p1.R) / 3, (p0.G + 2 * p1.G) / 3, (p0.B + 2 * p1.B) / 3) };
                    for (int i = 0; i < 16; i++)
                    {
                        int best = 0, bd = int.MaxValue;
                        for (int k = 0; k < 4; k++)
                        {
                            int dr = rr[i] - pal[k].R, dg = gg[i] - pal[k].G, db = bb[i] - pal[k].B, dd = dr * dr + dg * dg + db * db;
                            if (dd < bd) { bd = dd; best = k; }
                        }
                        idx |= (uint)best << (2 * i);
                    }
                }
                BitConverter.GetBytes((ushort)c0).CopyTo(dst, o); BitConverter.GetBytes((ushort)c1).CopyTo(dst, o + 2); BitConverter.GetBytes(idx).CopyTo(dst, o + 4);
                o += 8;
            }
    }
}
