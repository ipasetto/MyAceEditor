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
    readonly Dictionary<(int, int), float[]?> _blocks = new();
    readonly HashSet<int> _loadedPieces = new();

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
        if (_blocks.TryGetValue((br, bc), out var h)) return h;
        int piece = br / BlocksPerPiece * Cols + bc / BlocksPerPiece + 1;
        if (_loadedPieces.Add(piece)) LoadPiece(piece);
        _blocks.TryAdd((br, bc), null);
        return _blocks[(br, bc)];
    }

    void LoadPiece(int n)
    {
        var p = Path.Combine(Dir, $"{Name}_{n}.t2bk");
        if (!File.Exists(p)) return;
        var d = File.ReadAllBytes(p);
        if (BitConverter.ToUInt32(d, 0) != 0x54424B42) return;   // "BKBT"
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
            _blocks[(row, col)] = h;
        }
    }
}
