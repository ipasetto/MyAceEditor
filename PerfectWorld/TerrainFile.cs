using System.IO.Compression;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Alturas do terreno de um mapa do cliente (element\maps\&lt;mapa&gt;), PW-KB §S22.
/// &lt;mapa&gt;.trn2: "TRN2", versão, largura e altura do mundo (m), lado do bloco (m), células por bloco, …,
/// linhas e colunas de pedaços (+0x38/+0x3C), células por pedaço, tamanho da célula (m).
/// &lt;mapa&gt;_N.t2bk (N = linha*colunas + coluna + 1, linha 0 ao norte): "BKBT", versão, nBlocos, flags,
/// offsets[nBlocos]; bloco = i32, i16 linha global, i16 coluna global, f, i32, 4 tamanhos, alturas zlib
/// (33×33 floats em metros, linha cresce para o sul). Conferido com os pontos do npcgen (mediana 0,23 m).
/// </summary>
public sealed class TerrainFile
{
    public string Dir { get; }
    public string Name { get; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public float BlockSize { get; private set; } = 64;
    public int CellsPerBlock { get; private set; } = 32;
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public int CellsPerPiece { get; private set; } = 512;
    public float Cell { get; private set; } = 2;
    public int BlocksPerPiece => CellsPerPiece / CellsPerBlock;

    // bloco (linha, coluna globais) -> alturas 33×33; null = já tentado e sem dados
    // pedaço → blocos; cada pedaço é lido uma vez só, mesmo pedido ao mesmo tempo pelo desenho e pela carga em segundo plano
    readonly Dictionary<int, Lazy<Dictionary<(int, int), float[]>>> _pieces = new();
    readonly object _areaSync = new();

    TerrainFile(string dir, string name) { Dir = dir; Name = name; }

    /// <summary>Abre o mapa pela pasta (ex.: element\maps\world); null se não houver .trn2.</summary>
    public static TerrainFile? Open(string dir)
    {
        var trn = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.trn2").FirstOrDefault() : null;
        if (trn is null) return null;
        var d = File.ReadAllBytes(trn);
        if (d.Length < 0x48 || BitConverter.ToUInt32(d, 0) != 0x54524E32) return null;   // "2NRT" = TRN2
        return new TerrainFile(dir, Path.GetFileNameWithoutExtension(trn))
        {
            Width = BitConverter.ToSingle(d, 8), Height = BitConverter.ToSingle(d, 12),
            BlockSize = BitConverter.ToSingle(d, 16), CellsPerBlock = BitConverter.ToInt32(d, 20),
            Rows = BitConverter.ToInt32(d, 0x38), Cols = BitConverter.ToInt32(d, 0x3C),
            CellsPerPiece = BitConverter.ToInt32(d, 0x40), Cell = BitConverter.ToSingle(d, 0x44),
        };
    }

    /// <summary>Mundo (x, z) → posição na grade de amostras (u para leste, v para o sul).</summary>
    public (float U, float V) ToGrid(float x, float z) => ((x + Width / 2) / Cell, (Height / 2 - z) / Cell);
    public (float X, float Z) FromGrid(float u, float v) => (u * Cell - Width / 2, Height / 2 - v * Cell);

    /// <summary>Altura em metros na posição do mundo (interpolada), ou null fora do mapa.</summary>
    public float? HeightAt(float x, float z)
    {
        var (u, v) = ToGrid(x, z);
        int u0 = (int)MathF.Floor(u), v0 = (int)MathF.Floor(v);
        if (Sample(u0, v0) is not { } a || Sample(u0 + 1, v0) is not { } b || Sample(u0, v0 + 1) is not { } c || Sample(u0 + 1, v0 + 1) is not { } d) return null;
        float fu = u - u0, fv = v - v0;
        return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
    }

    /// <summary>Amostra da grade global (u, v), cada uma a Cell metros.</summary>
    public float? Sample(int u, int v)
    {
        int cpb = CellsPerBlock;
        if (u < 0 || v < 0 || u > Cols * CellsPerPiece || v > Rows * CellsPerPiece) return null;
        int br = Math.Min(v / cpb, Rows * BlocksPerPiece - 1), bc = Math.Min(u / cpb, Cols * BlocksPerPiece - 1);
        var h = Block(br, bc);
        return h?[(v - br * cpb) * (cpb + 1) + (u - bc * cpb)];
    }

    float[]? Block(int br, int bc)
    {
        int piece = br / BlocksPerPiece * Cols + bc / BlocksPerPiece + 1;
        return PieceData(piece).Value.GetValueOrDefault((br, bc));
    }

    Lazy<Dictionary<(int, int), float[]>> PieceData(int n)
    {
        lock (_pieces)
        {
            if (!_pieces.TryGetValue(n, out var l)) _pieces[n] = l = new(() => LoadPiece(n), LazyThreadSafetyMode.ExecutionAndPublication);
            return l;
        }
    }

    /// <summary>Lê as alturas do pedaço (1..Rows×Cols) agora — para a carga em segundo plano.</summary>
    public void Prefetch(int piece) { if (piece >= 1 && piece <= Rows * Cols) _ = PieceData(piece).Value; }

    public bool PieceReady(int piece) { lock (_pieces) return _pieces.TryGetValue(piece, out var l) && l.IsValueCreated; }

    /// <summary>Pedaço (1..Rows×Cols) de uma posição do mundo; 0 fora do mapa.</summary>
    public int PieceAt(float x, float z)
    {
        float pm = CellsPerPiece * Cell;
        int c = (int)MathF.Floor((x + Width / 2) / pm), r = (int)MathF.Floor((Height / 2 - z) / pm);
        return r < 0 || c < 0 || r >= Rows || c >= Cols ? 0 : r * Cols + c + 1;
    }

    // ---------- texturas do chão (camadas + máscaras) ----------
    // <mapa>.trn2: +0x48 offset da lista de texturas (i32 n + GBK), +0x50 offset da lista de nomes dos pedaços (fim da lista).
    // <mapa>_N.t2mk: "BKMT", versão, nÁreas (16 = 4×4 de 256 m, linha 0 ao norte), nCamadas, ?, offsets[nÁreas],
    // camadas[nCamadas] de 28 B (textura na lista do .trn2, ?, lado da máscara, ?, escala u, escala v, ?);
    // área: nCamadasUsadas, (camada, offset absoluto, tamanho) — máscara zlib lado×lado bytes (a 1ª camada é a base, sem máscara).
    public sealed record GroundLayer(string Texture, float UScale, float VScale, byte[]? Mask, int MaskSide);

    List<string>? _texNames;
    readonly Dictionary<int, (byte[] Data, (int Tex, float U, float V, int Side)[] Layers)?> _maskFiles = new();
    readonly Dictionary<(int, int), GroundLayer[]?> _areas = new();
    public const float AreaM = 256;

    public IReadOnlyList<string> TextureNames
    {
        get
        {
            if (_texNames is not null) return _texNames;
            _texNames = new();
            var trn = Path.Combine(Dir, Name + ".trn2");
            if (!File.Exists(trn)) return _texNames;
            var d = File.ReadAllBytes(trn);
            int o = BitConverter.ToInt32(d, 0x48), end = BitConverter.ToInt32(d, 0x50);
            var gbk = System.Text.Encoding.GetEncoding(936);
            while (o + 4 <= Math.Min(end, d.Length))
            {
                int n = BitConverter.ToInt32(d, o);
                if (n <= 0 || o + 4 + n > d.Length) break;
                _texNames.Add(gbk.GetString(d, o + 4, n)); o += 4 + n;
            }
            return _texNames;
        }
    }

    /// <summary>Camadas do chão da área de 256 m (linha/coluna globais a partir do noroeste); null sem dados.</summary>
    public GroundLayer[]? Ground(int areaRow, int areaCol)
    {
        lock (_areaSync) return GroundCore(areaRow, areaCol);
    }

    GroundLayer[]? GroundCore(int areaRow, int areaCol)
    {
        if (_areas.TryGetValue((areaRow, areaCol), out var a)) return a;
        int per = (int)(CellsPerPiece * Cell / AreaM);
        if (areaRow < 0 || areaCol < 0 || areaRow >= Rows * per || areaCol >= Cols * per) return null;
        int piece = areaRow / per * Cols + areaCol / per + 1, local = areaRow % per * per + areaCol % per;
        if (!_maskFiles.TryGetValue(piece, out var mf))
        {
            mf = null;
            var p = Path.Combine(Dir, $"{Name}_{piece}.t2mk");
            if (File.Exists(p))
            {
                var d = File.ReadAllBytes(p);
                if (BitConverter.ToUInt32(d, 0) == 0x544D4B42)   // "BKMT"
                {
                    int nAreas = BitConverter.ToInt32(d, 8), nLayers = BitConverter.ToInt32(d, 12), lo = 0x14 + nAreas * 4;
                    var layers = new (int, float, float, int)[nLayers];
                    for (int i = 0; i < nLayers; i++, lo += 28)
                        layers[i] = (BitConverter.ToInt32(d, lo), BitConverter.ToSingle(d, lo + 16), BitConverter.ToSingle(d, lo + 20), BitConverter.ToInt32(d, lo + 8));
                    mf = (d, layers);
                }
            }
            _maskFiles[piece] = mf;
        }
        GroundLayer[]? result = null;
        if (mf is { } f && local < BitConverter.ToInt32(f.Data, 8))
        {
            var d = f.Data;
            int bo = BitConverter.ToInt32(d, 0x14 + local * 4), n = BitConverter.ToInt32(d, bo);
            var list = new List<GroundLayer>();
            for (int i = 0; i < n; i++)
            {
                int li = BitConverter.ToInt32(d, bo + 4 + i * 12), mo = BitConverter.ToInt32(d, bo + 8 + i * 12), ms = BitConverter.ToInt32(d, bo + 12 + i * 12);
                if (li < 0 || li >= f.Layers.Length) continue;
                var (tex, u, v, side) = f.Layers[li];
                byte[]? mask = null;
                if (ms > 2 && side > 0)
                {
                    using var z = new DeflateStream(new MemoryStream(d, mo + 2, ms - 2), CompressionMode.Decompress);
                    mask = new byte[side * side];
                    int got = 0, r;
                    while (got < mask.Length && (r = z.Read(mask, got, mask.Length - got)) > 0) got += r;
                }
                if (tex >= 0 && tex < TextureNames.Count) list.Add(new GroundLayer(TextureNames[tex], u, v, mask, side));
            }
            result = list.Count > 0 ? list.ToArray() : null;
        }
        return _areas[(areaRow, areaCol)] = result;
    }

    // <mapa>_N.t2lm (luz do dia; .t2lm1 = noite): "T2LM", versão, nÁreas (16), lado (256), ?, tamanho de cada, offsets[nÁreas];
    // cada área de 256 m é um .dds de lado×lado (1 m por pixel)
    readonly Dictionary<int, byte[]?> _lightFiles = new();

    /// <summary>DDS da luz da área de 256 m (linha/coluna globais), ou null.</summary>
    public byte[]? LightmapDds(int areaRow, int areaCol)
    {
        lock (_areaSync) return LightmapCore(areaRow, areaCol);
    }

    byte[]? LightmapCore(int areaRow, int areaCol)
    {
        int per = (int)(CellsPerPiece * Cell / AreaM);
        if (areaRow < 0 || areaCol < 0 || areaRow >= Rows * per || areaCol >= Cols * per) return null;
        int piece = areaRow / per * Cols + areaCol / per + 1, local = areaRow % per * per + areaCol % per;
        if (!_lightFiles.TryGetValue(piece, out var d))
        {
            var p = Path.Combine(Dir, $"{Name}_{piece}.t2lm");
            d = File.Exists(p) ? File.ReadAllBytes(p) : null;
            if (d is not null && BitConverter.ToUInt32(d, 0) != 0x54324C4D) d = null;   // "ML2T"
            _lightFiles[piece] = d;
        }
        if (d is null) return null;
        int n = BitConverter.ToInt32(d, 8);
        if (local >= n) return null;
        int o = BitConverter.ToInt32(d, 0x18 + local * 4), end = local + 1 < n ? BitConverter.ToInt32(d, 0x18 + (local + 1) * 4) : d.Length;
        return o > 0 && end > o && end <= d.Length ? d.AsSpan(o, end - o).ToArray() : null;
    }

    Dictionary<(int, int), float[]> LoadPiece(int n)
    {
        var blocks = new Dictionary<(int, int), float[]>();
        var p = Path.Combine(Dir, $"{Name}_{n}.t2bk");
        if (!File.Exists(p)) return blocks;
        var d = File.ReadAllBytes(p);
        if (BitConverter.ToUInt32(d, 0) != 0x54424B42) return blocks;   // "BKBT"
        int count = BitConverter.ToInt32(d, 8), side = CellsPerBlock + 1;
        for (int i = 0; i < count; i++)
        {
            int o = BitConverter.ToInt32(d, 16 + i * 4);
            int row = BitConverter.ToInt16(d, o + 4), col = BitConverter.ToInt16(d, o + 6), size = BitConverter.ToInt32(d, o + 16);
            // zlib com cabeçalho de 2 bytes: pula para o deflate cru
            using var z = new DeflateStream(new MemoryStream(d, o + 34, size - 2), CompressionMode.Decompress);
            var raw = new byte[side * side * 4];
            int got = 0, r;
            while (got < raw.Length && (r = z.Read(raw, got, raw.Length - got)) > 0) got += r;
            var h = new float[side * side];
            Buffer.BlockCopy(raw, 0, h, 0, raw.Length);
            blocks[(row, col)] = h;
        }
        return blocks;
    }
}
