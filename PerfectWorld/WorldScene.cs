using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Cena do mapa no cliente (maps\&lt;mapa&gt;\&lt;mapa&gt;.ecwld, "ECWD"): por enquanto só as árvores (tipo + posição) e a tabela
/// de tipos (.spt SpeedTree + mapa composto das folhas). Layout: PW-KB §S17 (CCommon/EC_WorldFile.h, EC_SceneBlock.cpp).
/// Leitura apenas; o arquivo não é regravado.
/// </summary>
public sealed class WorldScene
{
    public sealed record TreeType(string Spt, string CompositeMap);
    public readonly record struct Tree(int Type, float X, float Y, float Z, bool HasY);

    public int Version { get; }
    public float Width { get; }
    public float Length { get; }
    public List<TreeType> TreeTypes { get; } = new();
    public List<Tree> Trees { get; } = new();

    const uint Ident = ('E' << 24) | ('C' << 16) | ('W' << 8) | 'D';
    static readonly Encoding Gbk;
    static WorldScene() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public WorldScene(string path) : this(File.ReadAllBytes(path)) { }

    public WorldScene(byte[] b)
    {
        if (b.Length < 116 || BitConverter.ToUInt32(b, 0) != Ident) throw new InvalidDataException("não é um .ecwld");
        Version = BitConverter.ToInt32(b, 4);
        Width = BitConverter.ToSingle(b, 8); Length = BitConverter.ToSingle(b, 12);
        int nBlock = BitConverter.ToInt32(b, 24);
        uint treeOff = BitConverter.ToUInt32(b, 36);
        int nTreeType = BitConverter.ToInt32(b, 44);
        // cabeçalho = 56 B + 60 reservados; depois um u32 de offset por bloco
        int p = (int)treeOff;
        for (int i = 0; i < nTreeType; i++) TreeTypes.Add(new TreeType(Str(b, ref p), Str(b, ref p)));
        // tipos de grama (ECWDFILEGRASSTYPE 24 B: bool alfa + 3 de alinhamento, i32 ref. de alfa, tamanho, variação,
        // maciez, distância de visão) + arquivo .mox
        int nGrassType = BitConverter.ToInt32(b, 48);
        p = (int)BitConverter.ToUInt32(b, 40);
        for (int i = 0; i < nGrassType && p > 0 && p + 28 <= b.Length; i++)
        {
            float G(int x) => BitConverter.ToSingle(b, p + x);
            var gt = new GrassType(b[p] != 0, BitConverter.ToInt32(b, p + 4), G(8), G(12), G(16), G(20), "");
            p += 24;
            GrassTypes.Add(gt with { Mox = Str(b, ref p) });
        }
        // bloco: u16 linha, u16 coluna, contagens (5 ints < v4, 8 ints < v7, 9 < v9, 10 a partir da v9); árvores vêm primeiro
        int counts = Version < 4 ? 5 : Version < 7 ? 8 : Version < 9 ? 9 : 10;
        bool hasY = Version >= 5;
        int rec = hasY ? 16 : 12;
        for (int i = 0; i < nBlock; i++)
        {
            int o = (int)BitConverter.ToUInt32(b, 116 + 4 * i);
            if (o <= 0 || o + 4 + 4 * counts > b.Length) continue;
            int n = BitConverter.ToInt32(b, o + 4);
            int t = o + 4 + 4 * counts;
            if (n < 0 || t + (long)n * rec > b.Length) continue;
            for (int k = 0; k < n; k++, t += rec)
                Trees.Add(hasY
                    ? new Tree(BitConverter.ToInt32(b, t), BitConverter.ToSingle(b, t + 4), BitConverter.ToSingle(b, t + 8), BitConverter.ToSingle(b, t + 12), true)
                    : new Tree(BitConverter.ToInt32(b, t), BitConverter.ToSingle(b, t + 4), 0, BitConverter.ToSingle(b, t + 8), false));
            try { ReadModels(b, o, t, counts); } catch (ArgumentOutOfRangeException) { } catch (ArgumentException) { }
        }
    }

    /// <summary>Modelos .ecm fixos do cenário (paredes, tetos, estátuas de algumas instâncias): CECSceneBlock::LoadECModels.</summary>
    public sealed record SceneModel(string Ecm, string Action, float X, float Y, float Z, float[] Dir, float[] Up);
    public List<SceneModel> Models { get; } = new();
    /// <summary>Ornamentos do bloco: (ID de exportação, offset no bsdata). No bsdata: ECBSDFILEORNAMENT (posição, direção,
    /// cima, 2 bool = 40 B) + nome do .bmd.</summary>
    public List<(uint Id, uint Offset)> OrnamentRefs { get; } = new();
    public List<(uint Id, uint Offset)> AreaRefs { get; } = new();
    public List<(uint Id, uint Offset)> GrassRefs { get; } = new();

    /// <summary>Tipo de grama (.ecwld): malha .mox de grasses.pck, tamanho ± variação, distância em que aparece.</summary>
    public sealed record GrassType(bool AlphaBlend, int AlphaRef, float Size, float SizeVar, float Softness, float Sight, string Mox);
    public List<GrassType> GrassTypes { get; } = new();

    /// <summary>
    /// Área de grama (ECBSDFILEGRASS7 no .ecbsd + mapa de bits no bsdata do sub-terreno): 1 bit por célula, linha de
    /// Cols/8 bytes, bit 7 = coluna mais à esquerda; a origem é o canto (centro − meia área) e as linhas crescem para −Z.
    /// </summary>
    public sealed record GrassArea(int Type, float X, float Y, float Z, float Cell, int Cols, int Rows, byte[] Bits)
    {
        public bool On(int col, int row) => (Bits[row * (Cols >> 3) + (col >> 3)] & (1 << (7 - (col & 7)))) != 0;
    }
    public List<GrassArea> Grass { get; } = new();
    internal string? GrassDebug, GrassFailInfo; int _grassFail;
    internal string GrassStats => $"únicas {GrassRefs.Select(g => g.Id).Distinct().Count()}, falhas {_grassFail} (1a: {GrassFailInfo})";

    /// <summary>Área de ambiente (CECScnBoxArea): caixa orientada + céu de dia (3 texturas: topo e as duas metades do
    /// lado, A3DSkySphere). A área padrão (Default) vale onde nenhuma outra caixa contém a câmera.</summary>
    public sealed record BoxArea(bool Default, float[] Pos, float[] Ext, float[] AxisX, float[] AxisY, float[] AxisZ, string[] Sky)
    {
        /// <summary>Neblina de dia (+68: início, fim, densidade, cor ARGB) e de noite (+100).</summary>
        public float FogStart, FogEnd, FogStartNight, FogEndNight;
        public uint FogColor, FogColorNight;
        /// <summary>Luz direcional (+116 direção, +128 ambiente, +132 cor do sol; noite +136/+140, cores ARGB).</summary>
        public float[] LightDir = { 0, -1, 0 };
        public uint Ambient, SunColor, AmbientNight, SunColorNight;
    }
    public List<BoxArea> Areas { get; } = new();

    /// <summary>Lê as áreas do .ecbsd (arquivo BSD da cena): ECBSDFILEBOXAREA11 (224 B, versão ≥ 11) + 6 textos de céu
    /// (dia 0..2, noite 0..2) + música. EC_ScnBoxArea.cpp.</summary>
    public void LoadAreas(string ecbsdPath)
    {
        if (!File.Exists(ecbsdPath)) return;
        var d = File.ReadAllBytes(ecbsdPath);
        if (d.Length < 12 || BitConverter.ToInt32(d, 8) < 11) return;   // versões antigas têm outra struct (sem suporte ainda)
        foreach (var (id, off) in AreaRefs.DistinctBy(a => a.Id))
        {
            int p = (int)off;
            if (p <= 0 || p + 224 > d.Length) continue;
            float[] V(int o) => new[] { BitConverter.ToSingle(d, p + o), BitConverter.ToSingle(d, p + o + 4), BitConverter.ToSingle(d, p + o + 8) };
            bool def = BitConverter.ToInt32(d, p) != 0;
            var area = (Pos: V(8), Ext: V(20), X: V(32), Y: V(44), Z: V(56));
            int q = p + 224;
            try
            {
                var sky = new[] { Str(d, ref q), Str(d, ref q), Str(d, ref q) };
                try { sky = [.. sky, Str(d, ref q), Str(d, ref q), Str(d, ref q)]; } catch (ArgumentException) { }   // 3..5 = céu de noite
                float F(int o) => BitConverter.ToSingle(d, p + o);
                uint U(int o) => BitConverter.ToUInt32(d, p + o);
                Areas.Add(new BoxArea(def, area.Pos, area.Ext, area.X, area.Y, area.Z, sky)
                {
                    FogStart = F(68), FogEnd = F(72), FogColor = U(80), FogStartNight = F(100), FogEndNight = F(104), FogColorNight = U(112),
                    LightDir = V(116), Ambient = U(128), SunColor = U(132), AmbientNight = U(136), SunColorNight = U(140),
                });
            }
            catch (ArgumentException) { }
        }
    }

    // depois das árvores, na ordem do CECSceneBlock (EC_SceneBlock.cpp, v ≥ 7): água, ornamentos, áreas e grama são
    // referências de 8 B ao bsdata; efeitos são struct + 1 texto; modelos EC são ECWDFILEECMODEL (40 B) + arquivo + ação
    void ReadModels(byte[] b, int o, int t, int counts)
    {
        if (Version < 7 || counts < 8) return;
        int C(int k) => BitConverter.ToInt32(b, o + 4 + 4 * k);   // 0 árvores, 1 água, 2 grama, 3 ornamentos, 4 áreas, 5 efeitos, 6 modelos EC
        int nWater = C(1), nGrass = C(2), nOrn = C(3), nBox = C(4), nFx = C(5), nEcm = C(6);
        for (int k = 0; k < nGrass && t + 8 * (nWater + nOrn + nBox + k) + 8 <= b.Length; k++)
        {
            int r = t + 8 * (nWater + nOrn + nBox + k);
            GrassRefs.Add((BitConverter.ToUInt32(b, r), BitConverter.ToUInt32(b, r + 4)));
        }
        for (int k = 0; k < nBox && t + 8 * (nWater + nOrn + k) + 8 <= b.Length; k++)
        {
            int r = t + 8 * (nWater + nOrn + k);
            AreaRefs.Add((BitConverter.ToUInt32(b, r), BitConverter.ToUInt32(b, r + 4)));
        }
        // referências dos ornamentos (construções .bmd): posição/direção ficam no bsdata, no offset dado
        for (int k = 0; k < nOrn && t + 8 * (nWater + k) + 8 <= b.Length; k++)
        {
            int r = t + 8 * (nWater + k);
            OrnamentRefs.Add((BitConverter.ToUInt32(b, r), BitConverter.ToUInt32(b, r + 4)));
        }
        if (nEcm <= 0 || nEcm > 100000 || nFx < 0 || nFx > 100000) return;
        int p = t + 8 * (nWater + nOrn + nBox + nGrass);
        int fxSize = Version < 8 ? 48 : Version < 12 ? 52 : 60;   // ECWDFILEEFFECT6/8/12 (com o alinhamento do bool)
        for (int k = 0; k < nFx; k++) { p += fxSize; Str(b, ref p); }
        for (int k = 0; k < nEcm; k++)
        {
            float F(int x) => BitConverter.ToSingle(b, p + x);
            var pos = (F(4), F(8), F(12));
            var dir = new[] { F(16), F(20), F(24) }; var up = new[] { F(28), F(32), F(36) };
            p += 40;
            var file = Str(b, ref p); var act = Str(b, ref p);
            if (file.Length > 0 && float.IsFinite(pos.Item1) && float.IsFinite(pos.Item2) && float.IsFinite(pos.Item3))
                Models.Add(new SceneModel(file, act, pos.Item1, pos.Item2, pos.Item3, dir, up));
        }
    }

    /// <summary>
    /// Áreas de grama (CECScene::LoadGrassArea): ECBSDFILEGRASS7 (36 B) no .ecbsd; o mapa de bits está no bsdata do
    /// sub-terreno, entrada iDataIndex: pedaço (u32 tamanho comprimido, u32 original) + dados, zlib se o arquivo diz.
    /// </summary>
    public void LoadGrass(string ecbsdPath)
    {
        if (!File.Exists(ecbsdPath) || GrassRefs.Count == 0) return;
        var d = File.ReadAllBytes(ecbsdPath);
        if (d.Length < 12 || BitConverter.ToInt32(d, 8) < 7) return;
        var dir = Path.Combine(Path.GetDirectoryName(ecbsdPath)!, "bsdata");
        var files = new Dictionary<int, byte[]?>();
        foreach (var (_, off) in GrassRefs.DistinctBy(g => g.Id))
        {
            int p = (int)off;
            if (p <= 0 || p + 36 > d.Length) continue;
            float F(int o) => BitConverter.ToSingle(d, p + o);
            int I(int o) => BitConverter.ToInt32(d, p + o);
            int type = I(0), cols = I(20), rows = I(24), sub = I(28), idx = I(32);
            GrassDebug ??= FormattableString.Invariant($"1a ref: off {p} tipo {type} centro {F(4):0},{F(8):0},{F(12):0} célula {F(16)} {cols}x{rows} sub {sub} idx {idx}");
            if (cols <= 0 || rows <= 0 || cols % 8 != 0 || cols * rows > 1 << 24) continue;
            if (BitsOf(dir, files, sub, idx, cols * rows / 8) is { } bits)
                Grass.Add(new GrassArea(type, F(4), F(8), F(12), F(16), cols, rows, bits));
            else { _grassFail++; GrassFailInfo ??= $"sub {sub} idx {idx} {cols}x{rows}"; }
        }
    }

    // bsdata\N.dat: "MOXB" + versão (2) + cabeçalho (versão, bool comprimido); fim = tabela de n offsets + i32 n
    // (offsets contados do começo do arquivo; i32 comprimido no byte 8)
    static byte[]? BitsOf(string dir, Dictionary<int, byte[]?> files, int sub, int idx, int size)
    {
        if (!files.TryGetValue(sub, out var f))
        {
            var path = Path.Combine(dir, $"{sub + 1}.dat");   // sub-terreno 0 = 1.dat
            files[sub] = f = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        if (f is null || f.Length < 20 || BitConverter.ToUInt32(f, 0) != 0x42584F4D) return null;
        int ver = BitConverter.ToInt32(f, 4);
        int n = BitConverter.ToInt32(f, f.Length - 4);
        long table = f.Length - 4 - 4L * n;
        if (idx < 0 || idx >= n || table < 8) return null;
        int o = BitConverter.ToInt32(f, (int)table + idx * 4);
        if (ver < 2) return o + size <= f.Length ? f.AsSpan(o, size).ToArray() : null;
        bool comp = f[8] != 0;
        if (o + 8 > f.Length || BitConverter.ToInt32(f, o + 4) != size) return null;
        int cs = BitConverter.ToInt32(f, o);
        if (cs <= 0 || o + 8 + cs > f.Length) return null;
        var raw = f.AsSpan(o + 8, comp ? cs : size).ToArray();
        return comp ? PckArchive.Inflate(raw, size) : raw;
    }

    static string Str(byte[] b, ref int p)
    {
        int n = BitConverter.ToInt32(b, p); p += 4;
        var s = Gbk.GetString(b, p, n); p += n;
        return s.TrimEnd('\0');
    }
}
