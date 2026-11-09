using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Pincel de sombra e luz do chão (usuário 09/11): a cor do chão no jogo = textura × mapa de luz × 2 (PW-KB §S22), então
/// retocar sombra = pintar no mapa de luz, não na textura. Edita <mapa>_N.t2lm (dia) e .t2lm1 (noite) juntos: cada área de
/// 256 m é um DDS DXT1 de lado×lado (128/256/512) sem mipmaps. Sombra escurece, Luz clareia (os dois pela força), Copiar
/// leva a luz para a do ponto pego (dia e noite cada um com a sua), Borracha volta à luz do arquivo. Uma pincelada vale pelo maior peso que cada ponto pegou
/// (passar de novo na mesma pincelada não acumula). Ctrl+Z por pincelada. Salvar regrava só os blocos 4×4 que mudaram
/// (o resto do arquivo fica byte a byte igual).
/// </summary>
public sealed class LightDraft(TerrainFile t)
{
    public enum Mode { Shadow, Light, Copy, Erase }
    public TerrainFile Terrain { get; } = t;

    sealed class Area { public required int[] Px; public required int[] Orig; public required int Side; }
    readonly Dictionary<(int Ar, int Ac, bool Night), Area?> _areas = new();
    Dictionary<(int, int, bool), int[]>? _start;     // pincelada: pontos de antes
    Dictionary<(int, int, bool), float[]>? _weight;  // pincelada: maior peso que cada ponto pegou
    readonly List<Dictionary<(int, int, bool), int[]>> _undo = new(), _redo = new();
    public int UndoCount => _undo.Count;
    public bool Dirty => _undo.Count > 0;
    /// <summary>Retângulo (metros do jogo) da última pincelada, para refazer a cor da grama em volta.</summary>
    public (float X0, float Z0, float X1, float Z1) LastBox { get; private set; }
    float _bx0, _bz0, _bx1, _bz1;

    Area? Get(int ar, int ac, bool night)
    {
        if (_areas.TryGetValue((ar, ac, night), out var a)) return a;
        Area? got = null;
        if (Terrain.LightmapDds(ar, ac, night) is { } dds && DdsDecoder.Decode(dds) is { } img && img.Width == img.Height && img.Width % 4 == 0)
            got = new Area { Px = (int[])img.Pixels.Clone(), Orig = (int[])img.Pixels.Clone(), Side = img.Width };
        return _areas[(ar, ac, night)] = got;
    }

    /// <summary>Pontos atuais de uma área (prévia na tela e na grama); null = área sem mapa de luz.</summary>
    public (int[] Px, int Side)? Pixels(int ar, int ac, bool night) => Get(ar, ac, night) is { } a ? (a.Px, a.Side) : null;

    float X0(int ac) => -Terrain.Width / 2 + ac * TerrainFile.AreaM;
    float ZTop(int ar) => Terrain.Height / 2 - ar * TerrainFile.AreaM;

    /// <summary>Luz (dia, noite) no ponto do jogo, para o conta-gotas de luz. null = sem mapa de luz ali.</summary>
    public (int Day, int Night)? Pick(float x, float z)
    {
        int ac = (int)MathF.Floor((x - X0(0)) / TerrainFile.AreaM), ar = (int)MathF.Floor((ZTop(0) - z) / TerrainFile.AreaM);
        int? At(bool night)
        {
            if (Get(ar, ac, night) is not { } a) return null;
            float m = TerrainFile.AreaM / a.Side;
            int u = Math.Clamp((int)((x - X0(ac)) / m), 0, a.Side - 1), v = Math.Clamp((int)((ZTop(ar) - z) / m), 0, a.Side - 1);
            return a.Px[v * a.Side + u];
        }
        return At(false) is int d ? (d, At(true) ?? d) : null;
    }

    public void BeginStroke() { _start = new(); _weight = new(); _bx0 = _bz0 = float.MaxValue; _bx1 = _bz1 = float.MinValue; }

    /// <summary>Um toque do pincel (círculo de raio r em metros). Devolve as áreas (linha, coluna, noite) que mudaram.</summary>
    public List<(int Ar, int Ac, bool Night)> Paint(float cx, float cz, float r, Mode mode, float strength, (int Day, int Night)? pick = null)
    {
        var touched = new List<(int, int, bool)>();
        if (_start is null || _weight is null || r <= 0) return touched;
        if (mode == Mode.Copy && pick is null) return touched;
        _bx0 = MathF.Min(_bx0, cx - r); _bz0 = MathF.Min(_bz0, cz - r); _bx1 = MathF.Max(_bx1, cx + r); _bz1 = MathF.Max(_bz1, cz + r);
        int ac0 = (int)MathF.Floor((cx - r - X0(0)) / TerrainFile.AreaM), ac1 = (int)MathF.Floor((cx + r - X0(0)) / TerrainFile.AreaM);
        int ar0 = (int)MathF.Floor((ZTop(0) - (cz + r)) / TerrainFile.AreaM), ar1 = (int)MathF.Floor((ZTop(0) - (cz - r)) / TerrainFile.AreaM);
        foreach (bool night in new[] { false, true })
            for (int ar = ar0; ar <= ar1; ar++)
                for (int ac = ac0; ac <= ac1; ac++)
                {
                    if (Get(ar, ac, night) is not { } a) continue;
                    var key = (ar, ac, night);
                    if (!_start.TryGetValue(key, out var start)) { _start[key] = start = (int[])a.Px.Clone(); _weight[key] = new float[a.Px.Length]; }
                    var wt = _weight[key];
                    float m = TerrainFile.AreaM / a.Side, x0 = X0(ac), zt = ZTop(ar);
                    int u0 = Math.Max(0, (int)((cx - r - x0) / m)), u1 = Math.Min(a.Side - 1, (int)((cx + r - x0) / m));
                    int v0 = Math.Max(0, (int)((zt - (cz + r)) / m)), v1 = Math.Min(a.Side - 1, (int)((zt - (cz - r)) / m));
                    bool any = false;
                    for (int v = v0; v <= v1; v++)
                        for (int u = u0; u <= u1; u++)
                        {
                            float x = x0 + (u + 0.5f) * m, z = zt - (v + 0.5f) * m;
                            float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (r * r);
                            if (d2 >= 1) continue;
                            float w = (1 - d2) * (1 - d2);   // borda macia
                            int i = v * a.Side + u;
                            if (w <= wt[i]) continue;
                            wt[i] = w;
                            int s = start[i], sr = (s >> 16) & 255, sg = (s >> 8) & 255, sb = s & 255;
                            int nr, ng, nb;
                            if (mode is Mode.Copy or Mode.Erase)
                            {
                                // copiar = a luz pega; borracha = a luz do arquivo (como era antes de qualquer pincelada)
                                int tgt = mode == Mode.Erase ? a.Orig[i] : night ? pick!.Value.Night : pick!.Value.Day;
                                float k = MathF.Min(1, w * (0.5f + strength));
                                nr = (int)(sr + (((tgt >> 16) & 255) - sr) * k); ng = (int)(sg + (((tgt >> 8) & 255) - sg) * k); nb = (int)(sb + ((tgt & 255) - sb) * k);
                            }
                            else
                            {
                                float f = mode == Mode.Shadow ? 1 - 0.65f * strength * w : 1 + 1.0f * strength * w;
                                nr = (int)(sr * f); ng = (int)(sg * f); nb = (int)(sb * f);
                            }
                            a.Px[i] = unchecked((int)0xFF000000) | (Math.Clamp(nr, 0, 255) << 16) | (Math.Clamp(ng, 0, 255) << 8) | Math.Clamp(nb, 0, 255);
                            any = true;
                        }
                    if (any) touched.Add(key);
                }
        return touched;
    }

    /// <summary>Fim da pincelada: entra no Ctrl+Z se mudou algo.</summary>
    public bool EndStroke()
    {
        var s = _start; _start = null; _weight = null;
        if (s is null || s.Count == 0) return false;
        LastBox = (_bx0, _bz0, _bx1, _bz1);
        _undo.Add(s); if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear();
        return true;
    }

    /// <summary>Desfaz/refaz uma pincelada. Devolve as áreas que mudaram (null = nada para fazer).</summary>
    public List<(int Ar, int Ac, bool Night)>? Undo(bool redo)
    {
        var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0) return null;
        var snap = from[^1]; from.RemoveAt(from.Count - 1);
        var back = new Dictionary<(int, int, bool), int[]>();
        foreach (var (key, px) in snap)
        {
            if (Get(key.Item1, key.Item2, key.Item3) is not { } a) continue;
            back[key] = (int[])a.Px.Clone();
            Array.Copy(px, a.Px, px.Length);
        }
        to.Add(back);
        return [.. snap.Keys];
    }

    /// <summary>Áreas já abertas (para voltar a tela ao disco ao abandonar).</summary>
    public IEnumerable<(int Ar, int Ac, bool Night)> OpenAreas => _areas.Where(kv => kv.Value is not null).Select(kv => kv.Key);

    /// <summary>
    /// Arquivos .t2lm/.t2lm1 regravados: só os blocos DXT1 4×4 cujos pontos mudaram (comparado com o arquivo do disco).
    /// Área com formato fora do esperado (com mipmaps, outro FourCC) fica de fora com aviso no resumo.
    /// </summary>
    public Dictionary<string, byte[]> Files(List<string>? summary = null)
    {
        var res = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int per = (int)(Terrain.CellsPerPiece * Terrain.Cell / TerrainFile.AreaM), changedBlocks = 0;
        foreach (var grp in _areas.Where(kv => kv.Value is not null).GroupBy(kv => (Piece: kv.Key.Ar / per * Terrain.Cols + kv.Key.Ac / per + 1, Night: kv.Key.Night)))
        {
            var path = Path.Combine(Terrain.Dir, $"{Terrain.Name}_{grp.Key.Piece}.t2lm" + (grp.Key.Night ? "1" : ""));
            if (!File.Exists(path)) continue;
            var d = File.ReadAllBytes(path);
            if (d.Length < 0x18 || BitConverter.ToUInt32(d, 0) != 0x54324C4D) throw new InvalidDataException(Path.GetFileName(path) + ": " + Loc.T("light.badFile", "mapa de luz com cabeçalho desconhecido"));
            int n = BitConverter.ToInt32(d, 8);
            byte[]? outB = null;
            foreach (var (key, area) in grp)
            {
                int local = key.Ar % per * per + key.Ac % per;
                if (local >= n || area is null) continue;
                int o = BitConverter.ToInt32(d, 0x18 + local * 4), end = local + 1 < n ? BitConverter.ToInt32(d, 0x18 + (local + 1) * 4) : d.Length;
                int side = area.Side;
                bool plain = o > 0 && end <= d.Length && end - o == 128 + side * side / 2 && d[o] == (byte)'D' && d[o + 1] == (byte)'D' && d[o + 2] == (byte)'S'
                    && d[o + 84] == (byte)'D' && d[o + 85] == (byte)'X' && d[o + 86] == (byte)'T' && d[o + 87] == (byte)'1' && BitConverter.ToInt32(d, o + 28) <= 1;
                if (!plain) { summary?.Add(string.Format(Loc.T("light.skipArea", "Luz de uma área do pedaço {0} não regravada (formato diferente)."), grp.Key.Piece)); continue; }
                if (DdsDecoder.Decode(d.AsSpan(o, end - o).ToArray()) is not { } orig || orig.Width != side) continue;
                int bs = side / 4; var diff = new bool[bs * bs]; bool any = false;
                for (int i = 0; i < area.Px.Length; i++)
                    if ((area.Px[i] & 0xFFFFFF) != (orig.Pixels[i] & 0xFFFFFF)) { int u = i % side, v = i / side; diff[(v >> 2) * bs + (u >> 2)] = true; any = true; }
                if (!any) continue;
                outB ??= (byte[])d.Clone();
                Dxt1.Encode(area.Px, side, side, outB, o + 128, (bx, by) => diff[(by >> 2) * bs + (bx >> 2)]);
                changedBlocks += diff.Count(b => b);
            }
            if (outB is not null) res[path] = outB;
        }
        if (res.Count > 0) summary?.Add(string.Format(Loc.T("light.summary", "Sombra e luz do chão: {0} arquivo(s) de luz, {1} bloco(s) de 4×4 m regravados."), res.Count, changedBlocks));
        return res;
    }
}

/// <summary>Compressor DXT1 simples (cores 565, extremos pela caixa de cores do bloco, 4 tons) para regravar a luz do chão.</summary>
public static class Dxt1
{
    static int To565(int r, int g, int b) => (r * 31 / 255 << 11) | (g * 63 / 255 << 5) | (b * 31 / 255);
    static (int R, int G, int B) From565(int c) => (((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31);

    /// <summary>Comprime ARGB (w×h, múltiplos de 4) para DXT1 em dst a partir de off; only = só os blocos escolhidos (os outros ficam).</summary>
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
