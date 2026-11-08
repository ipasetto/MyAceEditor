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
    sealed class Stroke { public readonly Dictionary<int, Snap> Before = new(); public Dictionary<int, Snap> After = new(); }
    readonly List<Stroke> _undo = new(), _redo = new();
    Stroke? _cur;

    public bool Dirty => _dirty.Count > 0 && _undo.Count > 0;
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
        if (s.Before.Count == 0) return false;
        s.After = s.Before.Keys.ToDictionary(p => p, Take);
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
        return s.Before.Keys.SelectMany(AreasOf).ToList();
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

    /// <summary>Arquivos a gravar (pedaços mexidos): caminho → bytes do .t2mk remontado.</summary>
    public Dictionary<string, byte[]> Files()
    {
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
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
            foreach (var (f, b) in files) { AtomicFileSave.Write(f, b); written.Add(f); progress?.Invoke(100 * written.Count / files.Count); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileSaveException)
        {
            AppLog.Error("terreno: gravar", ex);
            foreach (var w in written)
                try { if (baks[w] is { } bk) AtomicFileSave.Write(w, BackupStore.Read(bk)); } catch (Exception rex) when (rex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException) { AppLog.Error("terreno: voltar " + w, rex); }
            return new SaveResult(SaveKind.RolledBack, Loc.T("obj.rolledBack", "Não deu para salvar; tudo voltou como estava. As mudanças continuam no editor para tentar de novo."));
        }
        BackupSets.Save(new BackupSet("terrain", map, DateTime.Now, baks.Where(kv => kv.Value is not null).Select(kv => new BackupSetEntry(kv.Key, kv.Value!)).ToList()));
        return new SaveResult(SaveKind.ClientOnly, string.Format(Loc.T("terrain.saved", "Terreno salvo: {0} arquivo(s) de textura do chão."), files.Count));
    }
}
