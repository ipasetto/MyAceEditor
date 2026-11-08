using System.Text;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Uma água do cliente (A3DWaterArea, versão 0x10003000, em bytes crus no bsdata\N.dat; A3DWaterArea.cpp Save): u32 versão,
/// u32 ID, u32 cor ARGB, f nível, f borda, i32 colunas, i32 linhas, f lado da célula, f centro x/y/z, 1 B "água menor",
/// colunas × linhas bytes (bit 1 = célula visível, linha 0 ao norte), i32 ondas + ondas (espuma da margem).
/// O jogo chega nela por: referência de 8 B no bloco do .ecwld (ID de exportação, offset no .ecbsd) → ECBSDFILEWATER
/// (i32 sub-terreno, i32 índice) → tabela de offsets do bsdata\(sub+1).dat (EC_Scene.cpp LoadWaterArea).
/// </summary>
public sealed class WaterArea
{
    public const uint Version = 0x10003000;
    public uint ExportId;          // 0 = nova
    public int Sub = -1, Idx = -1; // sub-terreno (pedaço − 1) e índice no bsdata
    public byte[]? Raw;            // bytes do arquivo (água não mexida grava igual)
    public byte[]? OrigRect;       // retângulo do servidor da versão do arquivo (para trocar no .wmap)
    public uint AreaId;
    public int Argb;
    public float Height, Edge = 3;
    public int Cols, Rows;
    public float Cell = 32;
    public float X, Y, Z;
    public byte Minor;
    public byte[] Flags = [];
    public bool Touched;

    public float Left => X - Cols * Cell / 2;
    public float Right => X + Cols * Cell / 2;
    public float Top => Z + Rows * Cell / 2;
    public float Bottom => Z - Rows * Cell / 2;
    public bool Contains(float x, float z) => x >= Left && x <= Right && z >= Bottom && z <= Top;
    public int Wet => Flags.Count(f => (f & 1) != 0);

    public WaterArea Copy()
    {
        var a = (WaterArea)MemberwiseClone();
        a.Flags = (byte[])Flags.Clone();
        return a;
    }

    public ClientWater.Area ToView() => new(Argb, Height, Cols, Rows, Cell, X, Z, Flags);

    /// <summary>Lê a água no offset; end = onde começa a próxima entrada (ou a tabela). null se não é água.</summary>
    public static WaterArea? Parse(byte[] b, int o, int end)
    {
        if (o < 12 || o + 45 > end || end > b.Length || BitConverter.ToUInt32(b, o) != Version) return null;
        float F(int x) => BitConverter.ToSingle(b, o + x);
        var a = new WaterArea
        {
            AreaId = BitConverter.ToUInt32(b, o + 4), Argb = BitConverter.ToInt32(b, o + 8), Height = F(12), Edge = F(16),
            Cols = BitConverter.ToInt32(b, o + 20), Rows = BitConverter.ToInt32(b, o + 24), Cell = F(28), X = F(32), Y = F(36), Z = F(40), Minor = b[o + 44],
        };
        if (a.Cols <= 0 || a.Rows <= 0 || a.Cols > 4096 || a.Rows > 4096 || !(a.Cell > 0 && a.Cell < 10000) || !float.IsFinite(a.Height)
            || !float.IsFinite(a.X) || !float.IsFinite(a.Z) || o + 45 + a.Cols * a.Rows > end) return null;
        a.Flags = b.AsSpan(o + 45, a.Cols * a.Rows).ToArray();
        a.Raw = b.AsSpan(o, end - o).ToArray();
        return a;
    }

    /// <summary>Bytes da entrada: a do arquivo se não mexeu; senão nova, sem as ondas (a espuma ficaria na margem antiga).</summary>
    public byte[] Encode()
    {
        if (!Touched && Raw is not null) return Raw;
        var o = new List<byte>(49 + Flags.Length);
        void I(int v) => o.AddRange(BitConverter.GetBytes(v));
        void U(uint v) => o.AddRange(BitConverter.GetBytes(v));
        void F(float v) => o.AddRange(BitConverter.GetBytes(v));
        U(Version); U(AreaId); I(Argb); F(Height); F(Edge); I(Cols); I(Rows); F(Cell); F(X); F(Y); F(Z);
        o.Add(Minor); o.AddRange(Flags); I(0);
        return [.. o];
    }

    /// <summary>Retângulo do servidor (watermap\N.wmap): centro relativo ao centro do pedaço, meias-medidas e nível.</summary>
    public byte[] ServerRect(float pieceCx, float pieceCz)
    {
        var r = new byte[20];
        BitConverter.GetBytes(X - pieceCx).CopyTo(r, 0); BitConverter.GetBytes(Z - pieceCz).CopyTo(r, 4);
        BitConverter.GetBytes(Cols * Cell / 2).CopyTo(r, 8); BitConverter.GetBytes(Rows * Cell / 2).CopyTo(r, 12);
        BitConverter.GetBytes(Height).CopyTo(r, 16);
        return r;
    }
}

/// <summary>
/// Águas do mapa abertas para edição (ferramenta Terreno). A água aparece sozinha onde o chão fica abaixo do nível dentro do
/// retângulo (como o editor oficial: SceneWaterObject::BuildRenderWater), e o servidor recebe o retângulo inteiro
/// (CElementMap::ExpWaterAreaMap). Cada água fica dentro de um pedaço de 1024 m (o gs procura a água por pedaço).
/// </summary>
public sealed class WaterDraft
{
    public List<WaterArea> Areas { get; private set; } = new();
    readonly Dictionary<uint, WaterArea> _orig = new();   // versão do arquivo, por ID
    public TerrainFile Terrain { get; }
    public const int MaxCells = 32;   // células por lado (o pedaço inteiro com células de 32 m)

    WaterDraft(TerrainFile t) => Terrain = t;

    public string EcwldPath => Path.Combine(Terrain.Dir, Terrain.Name + ".ecwld");
    public string EcbsdPath => Path.Combine(Terrain.Dir, Terrain.Name + ".ecbsd");
    public string DatPath(int sub) => Path.Combine(Terrain.Dir, "bsdata", $"{sub + 1}.dat");

    public bool Dirty => Areas.Any(a => a.Touched || a.ExportId == 0) || _orig.Keys.Any(id => Areas.All(a => a.ExportId != id));

    /// <summary>Abre as águas referenciadas pelo .ecwld (uma por ID de exportação). Mapa sem .ecwld/.ecbsd: lista vazia.</summary>
    public static WaterDraft Load(TerrainFile t)
    {
        var w = new WaterDraft(t);
        if (!File.Exists(w.EcwldPath) || !File.Exists(w.EcbsdPath)) return w;
        var ecw = new EcwldFile(File.ReadAllBytes(w.EcwldPath));
        var bsd = File.ReadAllBytes(w.EcbsdPath);
        var dats = new Dictionary<int, (byte[] B, int[] Offs, int Table)?>();
        foreach (var bl in ecw.Blocks)
            foreach (var it in bl.Items[(int)EcwldFile.Kind.Water])
            {
                if (it.Length < 8) continue;
                uint id = EcwldFile.IdOf(it);
                int off = BitConverter.ToInt32(it, 4);
                if (w._orig.ContainsKey(id) || off < 0 || off + 8 > bsd.Length) continue;
                int sub = BitConverter.ToInt32(bsd, off), idx = BitConverter.ToInt32(bsd, off + 4);
                if (!dats.TryGetValue(sub, out var dat)) dats[sub] = dat = ReadDat(w.DatPath(sub));
                if (dat is not { } d || idx < 0 || idx >= d.Offs.Length) continue;
                int o = d.Offs[idx];
                int end = d.Offs.Where(x => x > o).DefaultIfEmpty(d.Table).Min();
                if (WaterArea.Parse(d.B, o, end) is not { } a) continue;
                a.ExportId = id; a.Sub = sub; a.Idx = idx;
                var (pcx, pcz) = w.PieceCenter(sub + 1);
                a.OrigRect = a.ServerRect(pcx, pcz);
                w._orig[id] = a;
                w.Areas.Add(a.Copy());
            }
        return w;
    }

    static (byte[] B, int[] Offs, int Table)? ReadDat(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var b = File.ReadAllBytes(path);
            if (b.Length < 16 || BitConverter.ToUInt32(b, 0) != 0x42584F4D) return null;
            int n = BitConverter.ToInt32(b, b.Length - 4), table = b.Length - 4 - 4 * n;
            if (n < 0 || table < 12) return null;
            return (b, Enumerable.Range(0, n).Select(i => BitConverter.ToInt32(b, table + 4 * i)).ToArray(), table);
        }
        catch (IOException) { return null; }
    }

    public float PieceM => Terrain.CellsPerPiece * Terrain.Cell;
    public (float X, float Z) PieceCenter(int piece)
    {
        int r = (piece - 1) / Terrain.Cols, c = (piece - 1) % Terrain.Cols;
        return (-Terrain.Width / 2 + (c + 0.5f) * PieceM, Terrain.Height / 2 - (r + 0.5f) * PieceM);
    }
    public (float X0, float Z0, float X1, float Z1) PieceRect(int piece)
    {
        var (cx, cz) = PieceCenter(piece);
        return (cx - PieceM / 2, cz - PieceM / 2, cx + PieceM / 2, cz + PieceM / 2);
    }
    public int PieceOf(WaterArea a) => Terrain.PieceAt(a.X, a.Z);

    public List<WaterArea> Snapshot() => [.. Areas.Select(a => a.Copy())];
    public void Restore(List<WaterArea> s) => Areas = [.. s.Select(a => a.Copy())];

    public WaterArea? At(float x, float z) => Areas.Where(a => a.Contains(x, z)).OrderBy(a => a.Cols * a.Rows).FirstOrDefault();

    /// <summary>Recalcula as células visíveis pelo chão (alguma amostra abaixo do nível). true se mudou.</summary>
    public bool RefreshFlags(WaterArea a)
    {
        var nf = new byte[a.Cols * a.Rows];
        float step = Math.Max(Terrain.Cell, a.Cell / 8);
        for (int r = 0; r < a.Rows; r++)
            for (int c = 0; c < a.Cols; c++)
            {
                float x0 = a.Left + c * a.Cell, z1 = a.Top - r * a.Cell;
                bool wet = false;
                for (float dz = 0; dz <= a.Cell + 0.01f && !wet; dz += step)
                    for (float dx = 0; dx <= a.Cell + 0.01f && !wet; dx += step)
                        if (Terrain.HeightAt(MathF.Min(x0 + dx, a.Right - 0.01f), MathF.Max(z1 - dz, a.Bottom + 0.01f)) is float h && h < a.Height) wet = true;
                nf[r * a.Cols + c] = (byte)(wet ? 1 : 0);
            }
        if (nf.AsSpan().SequenceEqual(a.Flags)) return false;
        a.Flags = nf; a.Touched = true;
        return true;
    }

    /// <summary>Depois do pincel de altura: recalcula as águas que cruzam o retângulo mexido. Devolve as que mudaram.</summary>
    public List<WaterArea> RefreshIn(float x0, float z0, float x1, float z1) =>
        Areas.Where(a => a.Right >= x0 && a.Left <= x1 && a.Top >= z0 && a.Bottom <= z1).Where(RefreshFlags).ToList();

    /// <summary>
    /// Água nova no ponto: quadrado de cols × rows células de 32 m alinhado à grade do pedaço e preso dentro dele, nível
    /// dado, cor da água mais perto (ou o azul padrão). null fora do mapa.
    /// </summary>
    public WaterArea? Create(float x, float z, float level, int cols = 8, int rows = 8, int? argb = null)
    {
        int piece = Terrain.PieceAt(x, z);
        if (piece <= 0) return null;
        var near = Areas.OrderBy(a => (a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z)).FirstOrDefault();
        var a = new WaterArea
        {
            Argb = argb ?? near?.Argb ?? unchecked((int)0xFF4F889D), Edge = near?.Edge ?? 3, Height = level, Y = level, Cell = 32,
            Cols = Math.Clamp(cols, 1, MaxCells), Rows = Math.Clamp(rows, 1, MaxCells), Touched = true,
        };
        a.X = x; a.Z = z;
        Fit(a, piece);
        RefreshFlags(a);
        Areas.Add(a);
        return a;
    }

    /// <summary>Encaixa a água na grade de 32 m do pedaço e dentro dele (centro e medidas).</summary>
    public void Fit(WaterArea a, int piece)
    {
        var (px0, pz0, px1, pz1) = PieceRect(piece);
        int maxC = (int)(PieceM / a.Cell);
        a.Cols = Math.Clamp(a.Cols, 1, Math.Min(MaxCells, maxC)); a.Rows = Math.Clamp(a.Rows, 1, Math.Min(MaxCells, maxC));
        float w = a.Cols * a.Cell, h = a.Rows * a.Cell;
        float left = MathF.Round((a.X - w / 2 - px0) / a.Cell) * a.Cell + px0, top = pz1 - MathF.Round((pz1 - (a.Z + h / 2)) / a.Cell) * a.Cell;
        left = Math.Clamp(left, px0, px1 - w); top = Math.Clamp(top, pz0 + h, pz1);
        a.X = left + w / 2; a.Z = top - h / 2;
    }

    /// <summary>Muda largura/comprimento (células) mantendo o centro, dentro do pedaço onde a água está.</summary>
    public void Resize(WaterArea a, int cols, int rows)
    {
        int piece = PieceOf(a);
        a.Cols = cols; a.Rows = rows;
        if (piece > 0) Fit(a, piece);
        a.Flags = new byte[a.Cols * a.Rows];
        a.Touched = true;
        RefreshFlags(a);
    }

    public void SetLevel(WaterArea a, float level) { a.Height = level; a.Y = level; a.Touched = true; RefreshFlags(a); }
    public void SetColor(WaterArea a, int argb) { a.Argb = argb; a.Touched = true; }
    public void Delete(WaterArea a) => Areas.Remove(a);

    /// <summary>Cópia (Ctrl+V) no ponto: mesma cor, medidas e nível relativo ao chão.</summary>
    public WaterArea? Paste(WaterArea src, float x, float z, float level) => Create(x, z, level, src.Cols, src.Rows, src.Argb);

    /// <summary>
    /// Arquivos a gravar: bsdata\N.dat (entradas mexidas e novas no fim, a tabela aponta para elas), .ecbsd (registro de
    /// 8 B das novas), .ecwld (referências das novas em todos os blocos que a água cruza; apagadas saem de todos) e, com a
    /// pasta do servidor, watermap\N.wmap dos pedaços mexidos (troca só os retângulos das águas mexidas; o resto fica).
    /// </summary>
    public Dictionary<string, byte[]> Files(string? serverMapDir, List<string> summary)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Dirty) return d;
        var ecw = new EcwldFile(File.ReadAllBytes(EcwldPath));
        byte[] bsd = File.ReadAllBytes(EcbsdPath);
        var dats = new Dictionary<int, (List<byte> Body, List<int> Offs)>();
        (List<byte> Body, List<int> Offs) Dat(int sub)
        {
            if (dats.TryGetValue(sub, out var f)) return f;
            string path = DatPath(sub);
            byte[] b = File.Exists(path) ? File.ReadAllBytes(path) : [.. Encoding.ASCII.GetBytes("MOXB"), .. BitConverter.GetBytes(2), .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(0)];
            if (b.Length < 16 || BitConverter.ToUInt32(b, 0) != 0x42584F4D) throw new InvalidDataException(string.Format(Loc.T("water.bsBad", "bsdata\\{0}.dat em formato desconhecido; a água não foi salva."), sub + 1));
            int n = BitConverter.ToInt32(b, b.Length - 4), table = b.Length - 4 - 4 * n;
            if (n < 0 || table < 12) throw new InvalidDataException(string.Format(Loc.T("water.bsBad", "bsdata\\{0}.dat em formato desconhecido; a água não foi salva."), sub + 1));
            return dats[sub] = ([.. b[..table]], Enumerable.Range(0, n).Select(i => BitConverter.ToInt32(b, table + 4 * i)).ToList());
        }
        int Append(int sub, byte[] entry, int idx)
        {
            var (body, offs) = Dat(sub);
            int off = body.Count;
            body.AddRange(entry);
            if (idx >= 0 && idx < offs.Count) { offs[idx] = off; return idx; }
            offs.Add(off); return offs.Count - 1;
        }
        var pieces = new Dictionary<int, (List<byte[]> Remove, List<byte[]> Add)>();
        (List<byte[]> Remove, List<byte[]> Add) P(int piece) => pieces.TryGetValue(piece, out var p) ? p : pieces[piece] = (new(), new());
        int changed = 0, added = 0, removed = 0;
        // apagadas: saem dos blocos; o retângulo sai do servidor
        foreach (var (id, o) in _orig)
        {
            if (Areas.Any(a => a.ExportId == id)) continue;
            ecw.Delete(EcwldFile.Kind.Water, id);
            if (o.OrigRect is not null) P(o.Sub + 1).Remove.Add(o.OrigRect);
            removed++;
        }
        uint nextId = ecw.MaxId() + 1;
        foreach (var a in Areas)
        {
            if (a.ExportId != 0 && !a.Touched) continue;
            int piece = PieceOf(a);
            if (piece <= 0) throw new InvalidDataException(Loc.T("water.outside", "Uma água ficou fora do mapa; nada foi salvo."));
            var (pcx, pcz) = PieceCenter(piece);
            if (a.ExportId != 0)
            {
                // mesma entrada (o registro do .ecbsd continua apontando para o índice), bytes novos no fim
                Append(a.Sub, a.Encode(), a.Idx);
                if (_orig.TryGetValue(a.ExportId, out var o) && o.OrigRect is not null) P(o.Sub + 1).Remove.Add(o.OrigRect);
                P(a.Sub + 1).Add.Add(a.ServerRect(PieceCenter(a.Sub + 1).X, PieceCenter(a.Sub + 1).Z));
                changed++;
                continue;
            }
            int sub = piece - 1;
            uint id = nextId++;
            var entry = a.Encode();
            BitConverter.GetBytes(id).CopyTo(entry, 4);
            int idx = Append(sub, entry, -1);
            int off = bsd.Length;
            bsd = [.. bsd, .. BitConverter.GetBytes(sub), .. BitConverter.GetBytes(idx)];
            var blocks = ecw.Blocks.Where(bl => ObjectFormats.BlockArea(ecw, bl) is var r && r.X1 > a.Left && r.X0 < a.Right && r.Z1 > a.Bottom && r.Z0 < a.Top).ToList();
            if (blocks.Count == 0) throw new InvalidDataException(Loc.T("obj.noBlock", "Não há bloco do cenário nesse lugar do mapa."));
            foreach (var bl in blocks)
            {
                var item = new byte[8];
                BitConverter.GetBytes(id).CopyTo(item, 0); BitConverter.GetBytes(off).CopyTo(item, 4);
                bl.Items[(int)EcwldFile.Kind.Water].Add(item);
            }
            P(piece).Add.Add(a.ServerRect(pcx, pcz));
            added++;
        }
        foreach (var (sub, (body, offs)) in dats)
        {
            var o = new List<byte>(body);
            foreach (int x in offs) o.AddRange(BitConverter.GetBytes(x));
            o.AddRange(BitConverter.GetBytes(offs.Count));
            d[DatPath(sub)] = [.. o];
        }
        var ecwOut = ecw.Write();
        _ = new EcwldFile(ecwOut);   // relê antes de gravar
        d[EcwldPath] = ecwOut;
        if (added > 0) d[EcbsdPath] = bsd;
        string? wdir = serverMapDir is null ? null : Path.Combine(serverMapDir, "watermap");
        bool server = wdir is not null && Directory.Exists(wdir);
        if (server)
            foreach (var (piece, (rem, add)) in pieces)
            {
                var path = Path.Combine(wdir!, $"{piece}.wmap");
                d[path] = Wmap(File.Exists(path) ? File.ReadAllBytes(path) : null, rem, add, PieceM);
            }
        summary.Add(string.Format(Loc.T("water.summary", "Água: {0} mudada(s), {1} nova(s), {2} apagada(s)"), changed, added, removed)
            + (server ? "" : " — " + Loc.T("water.noServer", "sem a pasta do servidor: o servidor não fica sabendo da água")));
        return d;
    }

    /// <summary>
    /// .wmap do pedaço: u32 versão (0xCC000001), f largura, f comprimento, i32 n, n × 20 B. Tira os retângulos iguais (0,05 m)
    /// aos de "remove" (um por vez) e acrescenta os de "add"; o que o servidor tinha a mais fica.
    /// </summary>
    public static byte[] Wmap(byte[]? old, IReadOnlyList<byte[]> remove, IReadOnlyList<byte[]> add, float side)
    {
        var rects = new List<byte[]>();
        byte[] head = [.. BitConverter.GetBytes(0xCC000001u), .. BitConverter.GetBytes(side), .. BitConverter.GetBytes(side)];
        if (old is { Length: >= 16 })
        {
            int n = BitConverter.ToInt32(old, 12);
            if (n < 0 || 16 + 20L * n > old.Length) throw new InvalidDataException(Loc.T("water.wmapBad", "watermap do servidor ilegível; a água não foi salva."));
            head = old[..12];
            for (int i = 0; i < n; i++) rects.Add(old.AsSpan(16 + 20 * i, 20).ToArray());
        }
        static bool Same(byte[] a, byte[] b)
        {
            for (int k = 0; k < 5; k++) if (MathF.Abs(BitConverter.ToSingle(a, 4 * k) - BitConverter.ToSingle(b, 4 * k)) > 0.05f) return false;
            return true;
        }
        foreach (var r in remove) { int i = rects.FindIndex(x => Same(x, r)); if (i >= 0) rects.RemoveAt(i); }
        rects.AddRange(add);
        var o = new List<byte>(16 + 20 * rects.Count);
        o.AddRange(head); o.AddRange(BitConverter.GetBytes(rects.Count));
        foreach (var r in rects) o.AddRange(r);
        return [.. o];
    }
}
