using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Rascunho do terreno (ferramenta Terreno do Mundo 3D; estudo em docs/superpowers/specs/2026-11-08-terreno-textura-estudo.md).
/// Textura do chão: as máscaras do &lt;mapa&gt;_N.t2mk (pedaço de 1024 m = 4×4 áreas de 256 m, máscara 256×256, 1 m/px) ficam
/// abertas na memória enquanto pinta; a mistura do jogo é camada sobre camada na ordem, então pintar uma textura sobe a
/// máscara dela e baixa as camadas acima. Cada arrastada é um passo de Ctrl+Z. Nada grava até o Salvar (TerrainSaver).
/// </summary>
public sealed class TerrainDraft(TerrainFile t)
{
    public TerrainFile Terrain { get; } = t;
    public const int MaxEntries = 16;   // camadas por área (os mapas do 1.5.5 chegam a 14)
    const int Side = 256;
    int Per => (int)(Terrain.CellsPerPiece * Terrain.Cell / TerrainFile.AreaM);

    readonly Dictionary<int, GroundMaskFile> _files = new();
    readonly Dictionary<(int Piece, int Area, int Entry), byte[]> _open = new();     // máscaras abertas (mexidas)
    readonly HashSet<int> _dirty = new();

    sealed record Snap(List<byte[]> Layers, List<List<(int Layer, int Raw, byte[]? Zlib)>> Areas);
    sealed class Stroke
    {
        public readonly Dictionary<int, Snap> Before = new(); public Dictionary<int, Snap> After = new();
        public readonly Dictionary<(int Br, int Bc), float[]> HBefore = new(); public Dictionary<(int Br, int Bc), float[]> HAfter = new();
        public List<WaterArea>? WBefore, WAfter;   // águas (foto inteira; são poucas)
    }
    /// <summary>Águas do mapa (null = mapa sem .ecwld/.ecbsd). Mexer nelas passa por WaterEdit para entrar no Ctrl+Z.</summary>
    public WaterDraft? Water { get; set; }
    /// <summary>Sombra e luz do chão (.t2lm/.t2lm1) pintadas à mão; null = não mexeu.</summary>
    public LightDraft? Light { get; set; }
    /// <summary>Alguma água mudou desde a última leitura (o desenho refaz e zera).</summary>
    public bool WaterChanged { get; set; }

    /// <summary>Tira a foto das águas no passo atual (uma vez por passo), antes de mexer nelas.</summary>
    void TouchWater()
    {
        if (_cur is not null && _cur.WBefore is null && Water is not null) _cur.WBefore = Water.Snapshot();
    }

    /// <summary>Uma ação nas águas como um passo de Ctrl+Z (criar, apagar, nível, cor, tamanho, colar).</summary>
    public bool WaterEdit(Action<WaterDraft> act)
    {
        if (Water is null) return false;
        bool own = _cur is null;
        if (own) BeginStroke();
        TouchWater();
        act(Water);
        WaterChanged = true;
        return !own || EndStroke();
    }
    readonly HashSet<(int Br, int Bc)> _hDirty = new();   // blocos de altura mexidos
    readonly Dictionary<(int Br, int Bc), float[]> _hOrig = new();   // alturas do arquivo dos blocos mexidos

    /// <summary>Amostra da altura como estava no arquivo (blocos mexidos guardados; o resto = o cache).</summary>
    float? OrigSample(int u, int v)
    {
        int cpb = Terrain.CellsPerBlock, br = Math.Min(v / cpb, Terrain.Rows * Terrain.BlocksPerPiece - 1), bc = Math.Min(u / cpb, Terrain.Cols * Terrain.BlocksPerPiece - 1);
        if (u < 0 || v < 0 || br < 0 || bc < 0) return null;
        return _hOrig.TryGetValue((br, bc), out var a) ? a[(v - br * cpb) * (cpb + 1) + (u - bc * cpb)] : Terrain.Sample(u, v);
    }

    /// <summary>Altura como estava no arquivo num ponto (bilinear na grade de 2 m).</summary>
    public float? OrigHeightAt(float x, float z)
    {
        var (gu, gv) = Terrain.ToGrid(x, z);
        int u = (int)MathF.Floor(gu), v = (int)MathF.Floor(gv); float fu = gu - u, fv = gv - v;
        if (OrigSample(u, v) is not float a || OrigSample(u + 1, v) is not float b || OrigSample(u, v + 1) is not float c || OrigSample(u + 1, v + 1) is not float e) return null;
        return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + e * fu) * fv;
    }
    public enum SculptMode { Raise, Lower, Smooth, Flatten }
    readonly List<Stroke> _undo = new(), _redo = new();
    Stroke? _cur;

    public bool Dirty => (_dirty.Count > 0 || _hDirty.Count > 0) && _undo.Count > 0 || Water?.Dirty == true || Light?.Dirty == true;

    /// <summary>Tamanho da lista de texturas do arquivo (texturas novas vêm depois; −1 = nenhuma acrescentada).</summary>
    public int FileTextureCount { get; private set; } = -1;

    /// <summary>Textura de outro mapa do mesmo cliente: entra no fim da lista deste mapa (o .trn2 regrava no Salvar).</summary>
    public int AddTexture(string path)
    {
        if (FileTextureCount < 0) FileTextureCount = Terrain.TextureNames.Count;
        return Terrain.AddTexture(path);
    }
    public bool HeightsDirty => _hDirty.Count > 0 && _undo.Count > 0;
    public int UndoCount => _undo.Count;
    public IReadOnlyCollection<int> DirtyPieces => _dirty;
    public string MaskPath(int piece) => Path.Combine(Terrain.Dir, $"{Terrain.Name}_{piece}.t2mk");
    /// <summary>Motivo da última recusa (área com camadas demais), para o status.</summary>
    public string? LastRefusal { get; private set; }

    GroundMaskFile? File(int piece)
    {
        if (_files.TryGetValue(piece, out var f)) return f;
        var p = MaskPath(piece);
        if (!System.IO.File.Exists(p)) return null;
        return _files[piece] = GroundMaskFile.Read(System.IO.File.ReadAllBytes(p));
    }

    /// <summary>Área de 256 m do ponto: pedaço (1..), área local (0..15), linha/coluna globais e o canto noroeste.</summary>
    public (int Piece, int Local, int Ar, int Ac, float X0, float Z1)? AreaAt(float x, float z)
    {
        int ac = (int)MathF.Floor((x + Terrain.Width / 2) / TerrainFile.AreaM), ar = (int)MathF.Floor((Terrain.Height / 2 - z) / TerrainFile.AreaM);
        int per = Per;
        if (ar < 0 || ac < 0 || ar >= Terrain.Rows * per || ac >= Terrain.Cols * per) return null;
        int piece = ar / per * Terrain.Cols + ac / per + 1, local = ar % per * per + ac % per;
        return (piece, local, ar, ac, -Terrain.Width / 2 + ac * TerrainFile.AreaM, Terrain.Height / 2 - ar * TerrainFile.AreaM);
    }

    byte[] Open(int piece, int area, int entry, GroundMaskFile f)
    {
        if (_open.TryGetValue((piece, area, entry), out var m)) return m;
        m = f.Mask(area, entry, Side) ?? new byte[Side * Side];
        return _open[(piece, area, entry)] = m;
    }

    /// <summary>Grava as máscaras abertas do pedaço no arquivo da memória (antes de foto do Ctrl+Z e do Salvar).</summary>
    void Flush(int piece)
    {
        if (!_files.TryGetValue(piece, out var f)) return;
        foreach (var k in _open.Keys.Where(k => k.Piece == piece).ToList()) { f.SetMask(k.Area, k.Entry, _open[k]); _open.Remove(k); }
    }

    Snap Take(int piece)
    {
        Flush(piece);
        var f = _files[piece];
        return new Snap([.. f.Layers.Select(l => (byte[])l.Clone())], [.. f.Areas.Select(a => a.Select(e => (e.Layer, e.RawOffset, e.Zlib)).ToList())]);
    }

    void Restore(int piece, Snap s)
    {
        foreach (var k in _open.Keys.Where(k => k.Piece == piece).ToList()) _open.Remove(k);
        var f = _files[piece];
        f.Layers.Clear(); f.Layers.AddRange(s.Layers.Select(l => (byte[])l.Clone()));
        f.Areas.Clear();
        foreach (var a in s.Areas) f.Areas.Add([.. a.Select(e => new GroundMaskFile.Entry { Layer = e.Layer, RawOffset = e.Raw, Zlib = e.Zlib })]);
    }

    public void BeginStroke() { _cur = new Stroke(); LastRefusal = null; }

    public bool EndStroke()
    {
        if (_cur is null) return false;
        var s = _cur; _cur = null;
        if (s.Before.Count == 0 && s.HBefore.Count == 0 && s.WBefore is null) return false;
        s.After = s.Before.Keys.ToDictionary(p => p, Take);
        if (s.WBefore is not null) s.WAfter = Water!.Snapshot();
        s.HAfter = s.HBefore.Keys.ToDictionary(k => k, k => (float[])Terrain.BlockHeights(k.Br, k.Bc)!.Clone());
        _undo.Add(s); _redo.Clear();
        return true;
    }

    /// <summary>Ctrl+Z / Ctrl+Y: devolve as áreas (linha, coluna globais) a redesenhar; null = nada.</summary>
    public List<(int Ar, int Ac)>? Undo(bool redo)
    {
        var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0) return null;
        var s = from[^1]; from.RemoveAt(from.Count - 1); to.Add(s);
        foreach (var (p, snap) in redo ? s.After : s.Before) Restore(p, snap);
        foreach (var (k, h) in redo ? s.HAfter : s.HBefore) if (Terrain.BlockHeights(k.Br, k.Bc) is { } live) Array.Copy(h, live, h.Length);   // mesmo vetor do cache
        if ((redo ? s.WAfter : s.WBefore) is { } ws && Water is not null) { Water.Restore(ws); WaterChanged = true; }
        return s.Before.Keys.SelectMany(AreasOf).Concat(s.HBefore.Keys.Select(AreaOfBlock)).Distinct().ToList();
    }

    IEnumerable<(int Ar, int Ac)> AreasOf(int piece)
    {
        int per = Per, pr = (piece - 1) / Terrain.Cols, pc = (piece - 1) % Terrain.Cols;
        for (int r = 0; r < per; r++) for (int c = 0; c < per; c++) yield return (pr * per + r, pc * per + c);
    }

    /// <summary>
    /// Pinta (ou apaga) a textura (índice na lista do .trn2) num círculo: força 0..1 com borda suave. Apagar baixa a textura
    /// escolhida (sem textura: baixa todas as camadas de cima, voltando para a base). Devolve as áreas mexidas.
    /// </summary>
    public List<(int Ar, int Ac)> Paint(float cx, float cz, float radius, int tex, float strength, bool erase)
    {
        var touched = new List<(int, int)>();
        if (radius <= 0) return touched;
        for (float z = cz - radius; z <= cz + radius + TerrainFile.AreaM; z += TerrainFile.AreaM)
            for (float x = cx - radius; x <= cx + radius + TerrainFile.AreaM; x += TerrainFile.AreaM)
            {
                if (AreaAt(MathF.Min(x, cx + radius), MathF.Min(z, cz + radius)) is not { } a || touched.Contains((a.Ar, a.Ac))) continue;
                if (PaintArea(a.Piece, a.Local, a.X0, a.Z1, cx, cz, radius, tex, strength, erase)) touched.Add((a.Ar, a.Ac));
            }
        return touched;
    }

    bool PaintArea(int piece, int local, float x0, float z1, float cx, float cz, float r, int tex, float strength, bool erase)
    {
        int px0 = Math.Max(0, (int)MathF.Floor(cx - r - x0)), px1 = Math.Min(Side - 1, (int)MathF.Floor(cx + r - x0));
        int py0 = Math.Max(0, (int)MathF.Floor(z1 - (cz + r))), py1 = Math.Min(Side - 1, (int)MathF.Floor(z1 - (cz - r)));
        if (px0 > px1 || py0 > py1) return false;
        var f = File(piece);
        if (f is null || local >= f.Areas.Count) return false;
        var entries = f.Areas[local];
        if (entries.Count == 0) return false;
        int TexOf(int entry) => entries[entry].Layer is var li && li >= 0 && li < f.Layers.Count ? BitConverter.ToInt32(f.Layers[li], 0) : -1;
        int idx = tex < 0 ? -1 : Enumerable.Range(0, entries.Count).FirstOrDefault(i => TexOf(i) == tex, -1);
        if (_cur is not null && !_cur.Before.ContainsKey(piece)) _cur.Before[piece] = Take(piece);
        if (idx < 0 && !erase)
        {
            if (entries.Count >= MaxEntries) { LastRefusal = Loc.T("terrain.tooMany", "Esta área já tem o máximo de texturas; apague uma (Borracha) ou use uma que já está aqui."); return false; }
            int li = f.Layers.FindIndex(l => BitConverter.ToInt32(l, 0) == tex);
            if (li < 0)
            {
                var l = (byte[])f.Layers[entries[^1].Layer >= 0 && entries[^1].Layer < f.Layers.Count ? entries[^1].Layer : 0].Clone();
                BitConverter.GetBytes(tex).CopyTo(l, 0);
                f.Layers.Add(l); li = f.Layers.Count - 1;
            }
            entries.Add(new GroundMaskFile.Entry { Layer = li, RawOffset = 0, Zlib = null });
            idx = entries.Count - 1;
            _open[(piece, local, idx)] = new byte[Side * Side];
        }
        var masks = Enumerable.Range(0, entries.Count).Select(i => i == 0 ? null : Open(piece, local, i, f)).ToArray();
        bool changed = false;
        for (int py = py0; py <= py1; py++)
            for (int px = px0; px <= px1; px++)
            {
                float x = x0 + px + 0.5f, z = z1 - py - 0.5f;
                float d = MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
                if (d > r) continue;
                float t = 1 - d / r, a = strength * t * t * (3 - 2 * t);   // borda suave
                if (a <= 0) continue;
                int p = py * Side + px;
                if (erase)
                {
                    if (idx > 0) { masks[idx]![p] = (byte)(masks[idx]![p] * (1 - a)); changed = true; }
                    else if (idx < 0) for (int j = 1; j < masks.Length; j++) { masks[j]![p] = (byte)(masks[j]![p] * (1 - a)); changed = true; }
                    continue;
                }
                if (idx > 0) masks[idx]![p] = (byte)Math.Min(255, masks[idx]![p] + (255 - masks[idx]![p]) * a + 0.5f);
                for (int j = Math.Max(idx, 0) + 1; j < masks.Length; j++) masks[j]![p] = (byte)(masks[j]![p] * (1 - a));
                changed = true;
            }
        if (changed) _dirty.Add(piece);
        return changed;
    }

    (int Ar, int Ac) AreaOfBlock((int Br, int Bc) k)
    {
        int bpa = (int)(TerrainFile.AreaM / (Terrain.CellsPerBlock * Terrain.Cell));   // blocos por área de 256 m
        return (k.Br / Math.Max(1, bpa), k.Bc / Math.Max(1, bpa));
    }

    /// <summary>
    /// Pincel de altura num círculo (metros): subir, descer, alisar (média dos vizinhos) ou plainar (vai para target). Mexe
    /// no cache do terreno (o desenho, a grama e o "assentar" veem na hora) e em todos os blocos que repetem a amostra da
    /// borda. Devolve as áreas de 256 m mexidas.
    /// </summary>
    public List<(int Ar, int Ac)> Sculpt(float cx, float cz, float radius, SculptMode mode, float strength, float target)
    {
        var areas = new HashSet<(int, int)>();
        if (radius <= 0) return [];
        var (cu, cv) = Terrain.ToGrid(cx, cz);
        float rc = radius / Terrain.Cell;
        int u0 = (int)MathF.Floor(cu - rc), u1 = (int)MathF.Ceiling(cu + rc), v0 = (int)MathF.Floor(cv - rc), v1 = (int)MathF.Ceiling(cv + rc);
        var next = new List<(int U, int V, float H)>();
        float step = Math.Clamp(radius * 0.06f, 0.15f, 1.5f);   // metros por toque no máximo (subir/descer)
        for (int v = v0; v <= v1; v++)
            for (int u = u0; u <= u1; u++)
            {
                float du = (u - cu) * Terrain.Cell, dv = (v - cv) * Terrain.Cell, d = MathF.Sqrt(du * du + dv * dv);
                if (d > radius || Terrain.Sample(u, v) is not { } h) continue;
                float t = 1 - d / radius, a = strength * t * t * (3 - 2 * t);
                float nh = mode switch
                {
                    SculptMode.Raise => h + step * a,
                    SculptMode.Lower => h - step * a,
                    SculptMode.Flatten => h + (target - h) * a,
                    _ => h + (Avg(u, v, h) - h) * a,
                };
                if (MathF.Abs(nh - h) > 1e-5f) next.Add((u, v, nh));
            }
        foreach (var (u, v, nh) in next) Set(u, v, nh, areas);
        // a água aparece (ou some) onde o chão passou do nível
        if (next.Count > 0 && Water is not null && Water.Areas.Any(a => a.Right >= cx - radius && a.Left <= cx + radius && a.Top >= cz - radius && a.Bottom <= cz + radius))
        {
            TouchWater();
            if (Water.RefreshIn(cx - radius - Terrain.Cell, cz - radius - Terrain.Cell, cx + radius + Terrain.Cell, cz + radius + Terrain.Cell).Count > 0) WaterChanged = true;
        }
        return [.. areas];
    }

    float Avg(int u, int v, float h)
    {
        float s = 0; int n = 0;
        for (int dv = -1; dv <= 1; dv++) for (int du = -1; du <= 1; du++) if (Terrain.Sample(u + du, v + dv) is { } x) { s += x; n++; }
        return n > 0 ? s / n : h;
    }

    void Set(int u, int v, float h, HashSet<(int, int)> areas)
    {
        int cpb = Terrain.CellsPerBlock;
        foreach (int br in v % cpb == 0 && v > 0 ? new[] { v / cpb, v / cpb - 1 } : new[] { v / cpb })
            foreach (int bc in u % cpb == 0 && u > 0 ? new[] { u / cpb, u / cpb - 1 } : new[] { u / cpb })
            {
                if (Terrain.BlockHeights(br, bc) is not { } arr) continue;
                int i = (v - br * cpb) * (cpb + 1) + (u - bc * cpb);
                if (i < 0 || i >= arr.Length) continue;
                if (_cur is not null && !_cur.HBefore.ContainsKey((br, bc))) _cur.HBefore[(br, bc)] = (float[])arr.Clone();
                if (!_hOrig.ContainsKey((br, bc))) _hOrig[(br, bc)] = (float[])arr.Clone();   // como estava no arquivo (passagem dos NPCs)
                arr[i] = h;
                _hDirty.Add((br, bc));
                areas.Add(AreaOfBlock((br, bc)));
            }
    }

    /// <summary>Camadas da área para o desenho (máscaras abertas ou do arquivo); null = área sem dados.</summary>
    public TerrainFile.GroundLayer[]? Layers(int ar, int ac)
    {
        int per = Per, piece = ar / per * Terrain.Cols + ac / per + 1, local = ar % per * per + ac % per;
        if (!_files.TryGetValue(piece, out var f) || local >= f.Areas.Count) return null;
        var names = Terrain.TextureNames;
        var list = new List<TerrainFile.GroundLayer>();
        var entries = f.Areas[local];
        for (int i = 0; i < entries.Count; i++)
        {
            int li = entries[i].Layer;
            if (li < 0 || li >= f.Layers.Count) continue;
            var l = f.Layers[li];
            int tex = BitConverter.ToInt32(l, 0);
            if (tex < 0 || tex >= names.Count) continue;
            var mask = i == 0 ? null : _open.TryGetValue((piece, local, i), out var om) ? om : f.Mask(local, i, Side);
            list.Add(new TerrainFile.GroundLayer(names[tex], BitConverter.ToSingle(l, 16), BitConverter.ToSingle(l, 20), mask, Side));
        }
        return list.Count > 0 ? [.. list] : null;
    }

    /// <summary>
    /// Arquivos a gravar: .t2mk dos pedaços pintados; .t2bk (só a parte das alturas dos blocos mexidos) e, com a pasta do
    /// servidor, o map\N.hmap refeito das alturas (o mesmo cálculo que bate com o do servidor). serverMapDir = config\&lt;mapa&gt;.
    /// </summary>
    public Dictionary<string, byte[]> Files(string? serverMapDir = null, List<string>? summary = null)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (Water is { Dirty: true } w) foreach (var (k, v) in w.Files(serverMapDir, summary ?? new List<string>())) d[k] = v;
        if (Light is { Dirty: true } lt) foreach (var (k, v) in lt.Files(summary)) d[k] = v;   // sombra e luz pintadas: só os blocos mexidos
        // texturas novas usadas: entram no fim da lista do .trn2 (sem uso nenhum, o arquivo não muda)
        if (FileTextureCount >= 0 && Terrain.TextureNames.Count > FileTextureCount && _dirty.Count > 0)
        {
            var trn = Path.Combine(Terrain.Dir, Terrain.Name + ".trn2");
            var add = Terrain.TextureNames.Skip(FileTextureCount).ToList();
            var nb = TerrainFile.Trn2WithTextures(System.IO.File.ReadAllBytes(trn), add);
            if (TerrainFile.ReadTextureList(trn) is var before && before.Count != FileTextureCount) throw new InvalidDataException(Loc.T("terrain.trnChanged", "A lista de texturas do mapa mudou fora do editor; reabra o Mundo 3D antes de salvar."));
            d[trn] = nb;
            summary?.Add(string.Format(Loc.T("terrain.texAdded", "Texturas novas na lista do mapa: {0}"), add.Count));
        }
        int bpp = Terrain.BlocksPerPiece, rmapChanged = 0, dhChanged = 0;
        foreach (var piece in _hDirty.Select(k => k.Br / bpp * Terrain.Cols + k.Bc / bpp + 1).Distinct())
        {
            var path = Path.Combine(Terrain.Dir, $"{Terrain.Name}_{piece}.t2bk");
            var nb = HeightFiles.RewriteT2bk(System.IO.File.ReadAllBytes(path), (row, col) => _hDirty.Contains((row, col)) ? Terrain.BlockHeights(row, col) : null);
            d[path] = nb;
            if (serverMapDir is not null && Directory.Exists(Path.Combine(serverMapDir, "map"))) d[Path.Combine(serverMapDir, "map", $"{piece}.hmap")] = HeightFiles.Hmap(Terrain, piece);
            // passagem dos NPCs: refaz os metros cujo chão mudou (pedaço sem .rmap no servidor = sem passagem: nada a fazer)
            var rp = serverMapDir is null ? null : Path.Combine(serverMapDir, "movemap", $"{piece}.rmap");
            if (rp is not null && System.IO.File.Exists(rp))
            try
            {
                float pm = Terrain.CellsPerPiece * Terrain.Cell, bm = Terrain.CellsPerBlock * Terrain.Cell;
                int pr = (piece - 1) / Terrain.Cols, pc = (piece - 1) % Terrain.Cols;
                float px0 = -Terrain.Width / 2 + pc * pm, pzS = Terrain.Height / 2 - (pr + 1) * pm;
                var rects = _hDirty.Where(k => k.Br / bpp * Terrain.Cols + k.Bc / bpp + 1 == piece)
                    .Select(k => (X0: -Terrain.Width / 2 + k.Bc * bm, Z0: Terrain.Height / 2 - (k.Br + 1) * bm, X1: -Terrain.Width / 2 + (k.Bc + 1) * bm, Z1: Terrain.Height / 2 - k.Br * bm)).ToList();
                var (nr, n) = MoveMapFile.Update(System.IO.File.ReadAllBytes(rp), px0, pzS, rects, Terrain.HeightAt, OrigHeightAt);
                if (n > 0) { d[rp] = nr; rmapChanged += n; }
                // pontes/pisos acima do chão mexido: o piso continua na mesma altura (o gs põe o NPC em chão + delta)
                var dp = Path.ChangeExtension(rp, ".dhmap");
                if (System.IO.File.Exists(dp) && new FileInfo(dp).Length >= 36)
                {
                    var (nd, dn) = MoveMapFile.AdjustDeltaHeights(System.IO.File.ReadAllBytes(dp), px0, pzS, rects, Terrain.HeightAt, OrigHeightAt);
                    if (dn > 0) { d[dp] = nd; dhChanged += dn; }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {   // extra: a passagem ruim não impede salvar o terreno
                AppLog.Error($"terreno: passagem do pedaço {piece}", ex);
                summary?.Add(string.Format(Loc.T("terrain.rmapFail", "Passagem dos NPCs do pedaço {0} não refeita (arquivo do servidor ilegível; veja o log)."), piece));
            }
        }
        if (dhChanged > 0) summary?.Add(string.Format(Loc.T("terrain.dhSummary", "Pontes e pisos acima do chão mexido: {0} metro(s) ajustado(s) para os monstros pisarem na mesma altura"), dhChanged));
        if (rmapChanged > 0) summary?.Add(string.Format(Loc.T("terrain.rmapSummary", "Passagem dos NPCs: {0} metro(s) refeito(s) onde o chão mudou"), rmapChanged));
        foreach (int p in _dirty)
        {
            Flush(p);
            var b = _files[p].Write();
            _ = GroundMaskFile.Read(b);   // relê
            d[MaskPath(p)] = b;
        }
        return d;
    }
}

/// <summary>
/// Salvar do terreno: tudo ou nada (cópia de cada arquivo no armazém de backup antes; falhou = volta tudo), conjunto de
/// backup da ferramenta ("Terreno") para o Restaurar backup voltar o Salvar inteiro. Só cliente (textura do chão).
/// </summary>
public static class TerrainSaver
{
    public static SaveResult Write(Dictionary<string, byte[]> files, string map, Action<int>? progress = null)
    {
        if (files.Count == 0) return new SaveResult(SaveKind.Failed, Loc.T("obj.nothing", "Nada para salvar."));
        var baks = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var f in files.Keys) baks[f] = File.Exists(f) ? BackupStore.Save(f) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("terreno: backup", ex);
            return new SaveResult(SaveKind.Failed, Loc.T("terrain.bakFail", "Não deu para fazer a cópia de segurança; nada foi gravado."));
        }
        var written = new List<string>();
        try
        {
            foreach (var (f, b) in files) { AtomicFileSave.Write(f, b, backup: false); written.Add(f); progress?.Invoke(100 * written.Count / files.Count); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileSaveException)
        {
            AppLog.Error("terreno: gravar", ex);
            foreach (var w in written)
                try { if (baks[w] is { } bk) AtomicFileSave.Write(w, BackupStore.Read(bk)); } catch (Exception rex) when (rex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException) { AppLog.Error("terreno: voltar " + w, rex); }
            return new SaveResult(SaveKind.RolledBack, Loc.T("obj.rolledBack", "Não deu para salvar; tudo voltou como estava. As mudanças continuam no editor para tentar de novo."));
        }
        BackupSets.Save(new BackupSet("terrain", map, DateTime.Now, baks.Where(kv => kv.Value is not null).Select(kv => new BackupSetEntry(kv.Key, kv.Value!)).ToList()));
        bool hmap = files.Keys.Any(k => k.EndsWith(".hmap", StringComparison.OrdinalIgnoreCase));
        bool wmap = files.Keys.Any(k => k.EndsWith(".wmap", StringComparison.OrdinalIgnoreCase));
        bool rmap = files.Keys.Any(k => k.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase));
        return new SaveResult(hmap || wmap ? SaveKind.Both : SaveKind.ClientOnly, string.Format(Loc.T("terrain.saved2", "Terreno salvo: {0} arquivo(s)."), files.Count)
            + (wmap ? " " + Loc.T("water.savedServer", "A água do servidor (watermap) também mudou: envie ao servidor. A passagem dos monstros de água não foi refeita.") : "")
            + (hmap ? " " + Loc.T("terrain.savedServer2", "A altura do servidor (map\\N.hmap) também mudou: envie ao servidor.")
                + " " + (rmap ? Loc.T("terrain.rmapDone", "A passagem dos NPCs foi refeita onde o chão mudou (movemap\\N.rmap).") : Loc.T("terrain.rmapSame", "A passagem dos NPCs não precisou mudar (nenhum trecho ficou íngreme demais ou plano de novo).")) : ""));
    }
}
