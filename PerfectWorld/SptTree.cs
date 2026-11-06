using System.Globalization;
using System.Text;
using OpenTK.Mathematics;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Árvore SpeedTree (.spt do trees.pck) — versão nossa, sem a DLL. Formato e algoritmo: PW-KB §S17 (engenharia reversa
/// da SpeedTreeRT 4.x). Lê o fluxo de tokens até o fim dos níveis de galho e gera tronco e galhos como no runtime:
/// cada nível tem 9 curvas avaliadas na posição relativa do galho no pai; o último nível são os pontos das folhas.
/// </summary>
public sealed class SptFile
{
    public string BarkTexture { get; private set; } = "";
    public float Size { get; private set; } = 30;   // pés
    public float SizeVariance { get; private set; }
    public int Seed { get; private set; } = 1;
    public int FlareSeed { get; private set; } = 1;
    /// <summary>Seção 9000, LOD dos galhos: 9011 níveis, 9012 parte da área que fica na LOD alta, 9013 fuzziness, 9014 retenção dos maiores.</summary>
    public int BranchLods { get; private set; } = 1;
    public float BranchArea { get; private set; } = 1;
    public float BranchFuzziness { get; private set; }
    public float BranchRetention { get; private set; }
    public List<SptLevel> Levels { get; } = new();
    /// <summary>Texturas de folha (4000–4007): âncora e tamanho do cartão (unidades da árvore) e o recorte no mapa composto.</summary>
    public List<SptLeafMap> LeafMaps { get; } = new();
    /// <summary>Frondes (13000): a partir do nível Level o galho vira fita(s) plana(s) em vez de tubo; null = sem frondes.</summary>
    public SptFrond? Frond { get; private set; }
    /// <summary>
    /// Iluminação (8000..8001, "lighting information"): material dos galhos (8003), das frondes (8005) e das folhas (8009),
    /// 13 floats cada = difusa rgb, ambiente rgb, especular rgb, emissiva rgb, brilho; 8006 = ajuste de luz das folhas.
    /// null = arquivo sem a seção.
    /// </summary>
    public float[]? BranchMaterial { get; private set; }
    /// <summary>
    /// Folhas que se amontoam (3007 SpacingTolerance, padrão 0,5; 3008 colisão: 0 nenhuma, 1 só do mesmo galho, 2 da
    /// árvore toda, padrão 2): CBranch::RoomForLeaf descarta a folha cujo ponto cai a menos de espaçamento × lado maior
    /// da textura de outra já posta (caixa nos 3 eixos). É o que deixa a copa aberta e o tronco à mostra.
    /// </summary>
    public float LeafSpacing { get; private set; } = 0.5f;
    public int LeafCollision { get; private set; } = 2;
    /// <summary>
    /// Flores (3000 distância, 3001 nível, 3002 peso; texturas com 4000): a folha é flor se a posição no galho passa da
    /// distância (negativa = antes dela) e um sorteio ≤ peso. Sem textura que não seja flor, a folha que não é flor
    /// some (CBranch::MakeLeaf) — várias árvores do PW só têm "flores".
    /// </summary>
    public float BlossomDistance { get; private set; }
    public int BlossomLevel { get; private set; }
    public float BlossomWeighting { get; private set; }
    public float[]? FrondMaterial { get; private set; }
    public float[]? LeafMaterial { get; private set; }
    public float LeafLightAdjust { get; private set; }

    public SptFile(byte[] b)
    {
        var r = new Reader(b);
        if (r.Int() != 1000 || r.Str() != "__IdvSpt_02_") throw new InvalidDataException("não é um .spt");
        while (!r.End)
        {
            int t = r.Int();
            if (t == 1001) break;
            if (t != 1002) break;   // só a parte geral interessa aqui (folhas/frondes vêm depois)
            for (t = r.Int(); t != 1003 && !r.End; t = r.Int())
                switch (t)
                {
                    case 2000: BarkTexture = r.Str(); break;
                    case 2001 or 2003: r.Float(); break;
                    case 2002: r.Bool(); break;
                    case 2004: r.Int(); break;
                    case 2005: Seed = r.Int(); break;
                    case 2006: Size = r.Float(); break;
                    case 2007: SizeVariance = r.Float(); break;
                    case 1014:
                        r.Int();   // quantidade (o laço vai até 1015)
                        for (int lt = r.Int(); lt == 1016; lt = r.Int()) Levels.Add(SptLevel.Read(r));
                        break;
                    default: throw new InvalidDataException($"token {t} inesperado na parte geral");
                }
            if (r.Int() == 1004) ReadLeaves(r);
            break;
        }
        // coordenadas no mapa composto (10000: 10002 folhas, 10003 billboards, 10004 frondes — n × 4 pares s,t cada); as
        // seções 7000–9000/11000 têm tamanho variável, então procura a abertura "10000 10002" depois do fim da árvore
        int after = r.Pos;
        for (int p = after; p + 8 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 8000 && BitConverter.ToInt32(b, p + 4) is >= 8002 and <= 8009)
            {
                try { ReadLighting(new Reader(b, p + 4)); } catch (Exception) { BranchMaterial = FrondMaterial = LeafMaterial = null; }
                break;
            }
        for (int p = after; p + 12 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 10000 && BitConverter.ToInt32(b, p + 4) == 10002)
            {
                try { ReadTexCoords(new Reader(b, p + 4)); } catch (Exception) { }
                break;
            }
        for (int p = after; p + 8 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 16013) { FlareSeed = BitConverter.ToInt32(b, p + 4); break; }   // semente do rand() dos flares
        for (int p = after; p + 8 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 9000 && BitConverter.ToInt32(b, p + 4) == 9002)
            {
                for (int q = p; q + 8 <= b.Length && q < p + 160; q++)
                    switch (BitConverter.ToInt32(b, q))
                    {
                        case 9011: BranchLods = BitConverter.ToInt32(b, q + 4); break;
                        case 9012: BranchArea = BitConverter.ToSingle(b, q + 4); break;
                        case 9013: BranchFuzziness = BitConverter.ToSingle(b, q + 4); break;
                        case 9014: BranchRetention = BitConverter.ToSingle(b, q + 4); break;
                    }
                break;
            }
        // flares: "16000 16002", 11 pares por nível (CTreeEngine::ParseFlareInfo)
        for (int p = after; p + 8 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 16000 && BitConverter.ToInt32(b, p + 4) == 16002)
            {
                int q = p + 4;
                foreach (var level in Levels)
                {
                    if (q + 88 > b.Length || BitConverter.ToInt32(b, q) != 16002) break;
                    level.SegmentExponent = BitConverter.ToSingle(b, q + 4);
                    level.FlareCount = BitConverter.ToInt32(b, q + 12);
                    level.Flare = new float[9];
                    for (int i = 0; i < 9; i++) level.Flare[i] = BitConverter.ToSingle(b, q + 20 + i * 8);
                    q += 88;
                }
                while (q + 88 <= b.Length && BitConverter.ToInt32(b, q) == 16002) q += 88;
                break;
            }
        // frondes: "13000" seguido de um token da seção
        for (int p = after; p + 8 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 13000 && BitConverter.ToInt32(b, p + 4) is (>= 13002 and <= 13013) or 14007 or 14008)
            {
                try { Frond = SptFrond.Read(new Reader(b, p + 4)); } catch (Exception) { Frond = null; }
                break;
            }
        if (Frond is not null && _frondUv.Count > 0)
            for (int i = 0; i < Frond.Maps.Count && i < _frondUv.Count; i++) Frond.Maps[i].Uv = _frondUv[i];
    }

    readonly List<float[]> _frondUv = new();

    void ReadLighting(Reader r)
    {
        float[] M() { var m = new float[13]; for (int i = 0; i < 13; i++) m[i] = r.Float(); return m; }
        for (int t = r.Int(); t != 8001 && !r.End; t = r.Int())
            switch (t)
            {
                case 8002 or 8004 or 8007 or 8008: r.Int(); break;
                case 8003: BranchMaterial = M(); break;
                case 8005: FrondMaterial = M(); break;
                case 8006: LeafLightAdjust = r.Float(); break;
                case 8009: LeafMaterial = M(); break;
                default: throw new InvalidDataException($"token {t} inesperado na iluminação");
            }
    }

    void ReadTexCoords(Reader r)
    {
        for (int t = r.Int(); t != 10001 && !r.End; t = r.Int())
            switch (t)
            {
                case 10002 or 10003 or 10004:
                {
                    int n = r.Int();
                    for (int i = 0; i < n; i++)
                    {
                        var uv = new float[8];
                        // t no .spt conta de baixo da imagem; a textura carregada conta de cima
                        for (int k = 0; k < 8; k++) uv[k] = k % 2 == 1 ? 1 - r.Float() : r.Float();
                        if (t == 10002 && i < LeafMaps.Count) Array.Copy(uv, LeafMaps[i].Uv, 8);
                        if (t == 10004) _frondUv.Add(uv);
                    }
                    break;
                }
                case 10005: r.Str(); break;
                case 10006 or 10007: r.Bool(); break;
                default: return;
            }
    }

    void ReadLeaves(Reader r)
    {
        for (int t = r.Int(); t != 1005 && !r.End; t = r.Int())
            switch (t)
            {
                case 3004 or 3005 or 3010: r.Float(); break;
                case 3000: BlossomDistance = r.Float(); break;
                case 3001: BlossomLevel = r.Int(); break;
                case 3002: BlossomWeighting = r.Float(); break;
                case 3007: LeafSpacing = r.Float(); break;
                case 3008: LeafCollision = r.Int(); break;
                case 3003 or 3006 or 3009: r.Bool(); break;
                case 1009:
                    if (r.Int() != 1006) throw new InvalidDataException("lista de folhas sem 1006");
                    r.Int();
                    for (int lt = r.Int(); lt == 1007; lt = r.Int())
                    {
                        var m = new SptLeafMap();
                        for (int k = r.Int(); k != 1008; k = r.Int())
                            switch (k)
                            {
                                case 4000: m.Blossom = r.Bool(); break;
                                case 4001 or 4006: r.Vec(); break;
                                case 4002 or 4007: r.Float(); break;
                                case 4003: m.Texture = r.Str(); break;
                                case 4004: m.Origin = r.Vec().Xy; break;
                                case 4005: m.Size = r.Vec().Xy; break;
                                default: throw new InvalidDataException($"token {k} inesperado na folha");
                            }
                        LeafMaps.Add(m);
                    }
                    break;
                default: throw new InvalidDataException($"token {t} inesperado nas folhas");
            }
    }

    internal sealed class Reader
    {
        readonly byte[] _b; int _p;
        public Reader(byte[] b, int pos = 0) { _b = b; _p = pos; }
        public bool End => _p + 4 > _b.Length;
        public int Pos => _p;
        public Vector3 Vec() => new(Float(), Float(), Float());
        public int Int() { int v = BitConverter.ToInt32(_b, _p); _p += 4; return v; }
        public float Float() { float v = BitConverter.ToSingle(_b, _p); _p += 4; return v; }
        public bool Bool() => _b[_p++] != 0;
        public string Str() { int n = Int(); var s = Encoding.Latin1.GetString(_b, _p, n); _p += n; return s.TrimEnd('\0'); }
    }
}

/// <summary>Textura de folha: âncora (0..1 na imagem), tamanho em unidades da árvore e 4 pares (s,t) no mapa composto.</summary>
public sealed class SptLeafMap
{
    public string Texture = "";
    public bool Blossom;   // 4000: flor (SptFile.BlossomDistance)
    public Vector2 Origin = new(0.5f, 0.5f), Size = new(0.1f, 0.1f);
    public readonly float[] Uv = { 1, 1, 0, 1, 0, 0, 1, 0 };
}

/// <summary>
/// Frondes (CFrondEngine): nível onde começam, tipo (0 = lâmina), quantas lâminas giradas em volta do galho (180°/n) e as
/// texturas (14002 nome, 14003 tamanho, 14004 proporção, 14005/14006 giro mín./máx. em graus) com o recorte (10004).
/// Meia largura = comprimento do galho × tamanho × 0,5 × proporção.
/// </summary>
public sealed class SptFrond
{
    public int Level = 1, Type, Blades = 1;
    /// <summary>13007: sem isto ligado os galhos continuam tubos (CBranch::PassesFrondRules).</summary>
    public bool Enabled;
    /// <summary>13010-13013: a LOD mais alta guarda as frondes maiores até somar esta parte da área (CFrondEngine::BuildGuideLods).</summary>
    public float MaxArea = 1, MinArea = 1, Fuzziness, LargeRetention;
    /// <summary>Extrusão (tipo 1): perfil da seção (13005, curva 0..1 do centro à borda) e nº de pontos de cada metade (13006).</summary>
    public SptSpline Profile = SptSpline.Zero;
    public int Segments = 3;
    public readonly List<SptFrondMap> Maps = new();

    internal static SptFrond Read(SptFile.Reader r)
    {
        var f = new SptFrond();
        for (int t = r.Int(); t != 13001 && !r.End; t = r.Int())
            switch (t)
            {
                case 13002: f.Level = r.Int(); break;
                case 13003: f.Type = r.Int(); break;
                case 13004: f.Blades = Math.Max(1, r.Int()); break;
                case 13005: f.Profile = SptSpline.Parse(r.Str()); break;
                case 13006: f.Segments = Math.Max(2, r.Int()); break;
                case 13009 or 14007 or 14008: r.Int(); break;
                case 13007: f.Enabled = r.Bool(); break;
                case 13010: f.MaxArea = r.Float(); break;
                case 13011: f.MinArea = r.Float(); break;
                case 13012: f.Fuzziness = r.Float(); break;
                case 13013: f.LargeRetention = r.Float(); break;
                case 13008:
                {
                    int n = r.Int();
                    for (int i = 0; i < n; i++)
                    {
                        if (r.Int() != 14000) throw new InvalidDataException("fronde sem 14000");
                        var m = new SptFrondMap();
                        for (int k = r.Int(); k != 14001; k = r.Int())
                            switch (k)
                            {
                                case 14002: m.Texture = r.Str(); break;
                                case 14003: m.Size = r.Float(); break;
                                case 14004: m.Aspect = r.Float(); break;
                                case 14005: m.MinAngle = r.Float(); break;
                                case 14006: m.MaxAngle = r.Float(); break;
                                default: throw new InvalidDataException($"token {k} inesperado na textura de fronde");
                            }
                        f.Maps.Add(m);
                    }
                    break;
                }
                default: throw new InvalidDataException($"token {t} inesperado nas frondes");
            }
        return f;
    }
}

public sealed class SptFrondMap
{
    public string Texture = "";
    public float Size = 1, Aspect = 1, MinAngle, MaxAngle;
    public float[] Uv = { 1, 1, 0, 1, 0, 0, 1, 0 };
}

/// <summary>Um nível de galho (tokens 6000–6017). O último nível da árvore são as folhas.</summary>
public sealed class SptLevel
{
    public SptSpline Disturbance = SptSpline.Zero, Gravity = SptSpline.Zero, Flexibility = SptSpline.Zero, FlexScale = SptSpline.Zero,
        Length = SptSpline.Zero, Radius = SptSpline.Zero, Taper = SptSpline.Zero, StartAngle = SptSpline.Zero, GravityProfile = SptSpline.Zero;
    public int Sides = 6, Segments = 1;
    /// <summary>Seção 16000, um bloco por nível: 16002 expoente dos segmentos (progresso = (i/n)^expoente, anéis juntos na base).</summary>
    public float SegmentExponent = 1;
    /// <summary>16003 número de flares; 16004-16012: equilíbrio, influência radial e variação, expoente radial, distância e variação, influência no comprimento e variação, expoente no comprimento.</summary>
    public int FlareCount;
    public float[] Flare = new float[9];
    public float First, Last = 1, Frequency, TileU = 1, TileV = 1;
    public bool AbsoluteU, AbsoluteV;

    internal static SptLevel Read(SptFile.Reader r)
    {
        var l = new SptLevel();
        for (int t = r.Int(); t != 1017; t = r.Int())
            switch (t)
            {
                case 6000: l.Disturbance = SptSpline.Parse(r.Str()); break;
                case 6001: l.Gravity = SptSpline.Parse(r.Str()); break;
                case 6002: l.Flexibility = SptSpline.Parse(r.Str()); break;
                case 6003: l.FlexScale = SptSpline.Parse(r.Str()); break;
                case 6004: l.Length = SptSpline.Parse(r.Str()); break;
                case 6005: l.Radius = SptSpline.Parse(r.Str()); break;
                case 6006: l.Taper = SptSpline.Parse(r.Str()); break;
                case 6007: l.StartAngle = SptSpline.Parse(r.Str()); break;
                case 6008: l.Sides = r.Int(); break;
                case 6009: l.Segments = r.Int(); break;
                case 6010: l.First = r.Float(); break;
                case 6011: l.Last = r.Float(); break;
                case 6012: l.Frequency = r.Float(); break;
                case 6013: l.TileU = r.Float(); break;
                case 6014: l.TileV = r.Float(); break;
                case 6015: l.AbsoluteU = r.Bool(); break;
                case 6016: l.AbsoluteV = r.Bool(); break;
                case 6017: l.GravityProfile = SptSpline.Parse(r.Str()); break;   // 0,5 = reto; >0,5 = ponta sobe
                default: throw new InvalidDataException($"token {t} inesperado no nível de galho");
            }
        return l;
    }
}

/// <summary>
/// Curva do SpeedTree: "BezierSpline min max variação { n  x y tx ty peso … }". Bézier cúbica por trechos
/// (P, P+T·peso, P'−T'·peso, P'), reamostrada em 500 pontos de x igual; valor = (max−min)·y + min ± variação.
/// </summary>
public sealed class SptSpline
{
    public float Min, Max, Variance;
    readonly float[] _table;
    public static readonly SptSpline Zero = new(0, 0, 0, new[] { 0f });

    SptSpline(float min, float max, float var, float[] table) { Min = min; Max = max; Variance = var; _table = table; }

    public static SptSpline Parse(string s)
    {
        var p = s.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
        if (p.Length < 6 || p[0] != "BezierSpline" || p[4] != "{") return Zero;
        float min = F(1), max = F(2), var = F(3);
        int n = int.Parse(p[5], CultureInfo.InvariantCulture);
        var ctrl = new List<Vector2>();   // pontos de controle das cúbicas
        Vector2 lastP = default, lastT = default; float lastW = 0;
        for (int i = 0; i < n && 6 + i * 5 + 4 < p.Length; i++)
        {
            int k = 6 + i * 5;
            var pt = new Vector2(F(k), F(k + 1));
            var tg = new Vector2(F(k + 2), F(k + 3));
            if (tg.LengthSquared > 0) tg = tg.Normalized();
            float w = F(k + 4);
            if (i > 0) { ctrl.Add(lastP + lastT * lastW); ctrl.Add(pt - tg * w); }
            ctrl.Add(pt);
            lastP = pt; lastT = tg; lastW = w;
        }
        if (n < 2) return new SptSpline(min, max, var, new[] { ctrl.Count > 0 ? ctrl[0].Y : 0f });
        // 500 amostras no parâmetro da curva, depois reamostra por x
        const int N = 500;
        var pts = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float u = Math.Clamp(i / (float)N, 0, 1) * (n - 1);
            int seg = Math.Min((int)u, n - 2);
            float f = u - seg;
            Vector2 a = ctrl[seg * 3], b = ctrl[seg * 3 + 1], c = ctrl[seg * 3 + 2], d = ctrl[seg * 3 + 3];
            float g = 1 - f;
            pts[i] = g * g * g * a + 3 * g * g * f * b + 3 * g * f * f * c + f * f * f * d;
        }
        var table = new float[N];
        table[0] = pts[0].Y; table[N - 1] = ctrl[^1].Y;
        int k2 = 0;
        for (int i = 1; i < N - 1; i++)
        {
            float x = i / (float)N;
            for (int j = k2; j < N - 1; j++)
                if (pts[j].X <= x && x < pts[j + 1].X) { k2 = j; break; }
            float dx = pts[k2 + 1].X - pts[k2].X;
            table[i] = dx > 0 ? pts[k2].Y + (pts[k2 + 1].Y - pts[k2].Y) * ((x - pts[k2].X) / dx) : pts[k2].Y;
        }
        return new SptSpline(min, max, var, table);
    }

    /// <summary>Forma da curva (0..1) em t, sem escala nem variação.</summary>
    public float Shape(float t)
    {
        if (_table.Length == 1) return _table[0];
        const float step = 1f / 499;
        int i = (int)(Math.Clamp(t, 0, 1) / step);
        if (i >= 499) return _table[499];
        float f = (t - i * step) / step;
        return _table[i] + (_table[i + 1] - _table[i]) * f;
    }

    /// <summary>Valor em t, com a variação sorteada (sempre consome um número do gerador, como no runtime).</summary>
    public float Eval(float t, SptRandom rng) => (Max - Min) * Shape(t) + Min + rng.Range(-Variance, Variance);

    /// <summary>Perturbação: ± variação × forma (graus).</summary>
    public float Jitter(float t, SptRandom rng)
    {
        float y = Variance * (_table.Length == 1 ? _table[0] : _table[Math.Min((int)(Math.Clamp(t, 0, 1) * 499 + 0.5f), 499)]);   // sem interpolar
        return rng.Range(-y, y);
    }
}

/// <summary>rand() do C da Microsoft: a speedtreert.dll usa só para os flares, com semente própria (16013).</summary>
public sealed class SptCRand
{
    uint _state;
    public SptCRand(int seed) => _state = (uint)seed;
    public float Next() { _state = _state * 214013 + 2531011; return ((_state >> 16) & 0x7fff) * 3.051851e-05f; }
}

/// <summary>Gerador do SpeedTree: Park–Miller (16807, Schrage) com embaralhamento de 128 posições.</summary>
public sealed class SptRandom
{
    double _state;
    readonly float[] _shuffle = new float[128];

    public SptRandom(int seed) => Seed(seed);

    public void Seed(int seed)
    {
        _state = Math.Max(seed, 1);
        for (int i = 0; i < 128; i++) _shuffle[i] = Next();
    }

    float Next()
    {
        int s = (int)_state;
        int v = s % 127773 * 16807 - s / 127773 * 2836;
        if (v < 1) v += int.MaxValue;
        _state = v;
        return v * 4.656613e-10f;
    }

    public float Uniform()
    {
        int i = Math.Clamp((int)(Next() * 128), 0, 127);
        float r = _shuffle[i];
        _shuffle[i] = Next();
        return r;
    }

    public float Range(float a, float b) => Uniform() * (b - a) + a;
}

/// <summary>Malha gerada: posição, normal, uv; índices de triângulos; pontos das folhas (posição + direção do galho).</summary>
public sealed class SptMesh
{
    public readonly List<Vector3> Pos = new(), Normal = new();
    public readonly List<Vector2> Uv = new();
    public readonly List<int> Index = new();
    public readonly List<(Vector3 Pos, Vector3 Dir)> Leaves = new();
    /// <summary>Textura (índice em SptFile.LeafMaps) de cada folha, sorteada na geração como no runtime.</summary>
    public readonly List<int> LeafMap = new();
    /// <summary>Fitas das frondes (posição, uv no mapa composto, índices), à parte da casca.</summary>
    public readonly List<Vector3> FrondPos = new();
    public readonly List<Vector2> FrondUv = new();
    public readonly List<int> FrondIndex = new();
    public float Height, Scale = 1;   // Scale: unidades da árvore → metros (para o tamanho das folhas)
}

/// <summary>
/// Crescimento dos galhos (CIdvBranch::Compute, PW-KB §S13 "SpeedTreeRT galhos"): galho recursivo com um sistema de eixos
/// por vértice (linhas X, Y, Z no mundo; o galho cresce no X local), no espaço do SpeedTree (Z para cima). Filhos com
/// semente +3 cada. As variações das curvas vêm de um gerador à parte, como os geradores estáticos do runtime.
/// </summary>
public static class SptGrower
{
    /// <summary>Eixos locais no mundo; a direção do galho é X.</summary>
    readonly record struct Frame(Vector3 X, Vector3 Y, Vector3 Z)
    {
        public static readonly Frame Identity = new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

        /// <summary>Gira os três eixos em volta de um eixo do mundo (graus, mão direita).</summary>
        public Frame Turn(Vector3 axis, float deg)
        {
            var q = Quaternion.FromAxisAngle(axis, MathHelper.DegreesToRadians(deg));
            return new(Vector3.Transform(X, q), Vector3.Transform(Y, q), Vector3.Transform(Z, q));
        }

        /// <summary>CRotTransform::RotateYZ em graus, rotação local: +90 aponta o X para −Z (baixo), −90 para cima.</summary>
        public Frame RotateYZ(float a, float b)
        {
            float ra = MathHelper.DegreesToRadians(a), rb = MathHelper.DegreesToRadians(b);
            float ca = MathF.Cos(ra), sa = MathF.Sin(ra), cb = MathF.Cos(rb), sb = MathF.Sin(rb);
            return new(ca * cb * X + ca * sb * Y - sa * Z, -sb * X + cb * Y, sa * cb * X + sa * sb * Y + ca * Z);
        }
    }

    /// <summary>Espaço do SpeedTree (Z para cima) → o nosso (Y para cima).</summary>
    static Vector3 Ours(Vector3 v) => new(v.X, v.Z, -v.Y);

    /// <summary>Gera a árvore em metros, com Y para cima e a base do tronco na origem.</summary>
    /// <remarks>
    /// A speedtreert.dll tem UM gerador global: variação das curvas, giros e posições saem da mesma sequência, que volta à
    /// semente a cada filho. Por isso cada .spt dá sempre a mesma árvore e o tronco não entorta (PW-KB §S35).
    /// </remarks>
    public static SptMesh Build(SptFile f)
    {
        var m = new SptMesh();
        if (f.Levels.Count == 0) return m;
        var rng = new SptRandom(f.Seed);
        int seed = f.Seed;
        _leafPts = new List<Vector3>();
        _guides = new List<Guide>();
        _branches = new List<Branch>();
        _flareRand = new SptCRand(f.FlareSeed);
        _unit = f.Size * 0.3048f;
        Grow(f, m, rng, seed, 0, Vector3.Zero, Frame.Identity, Vector3.UnitZ, 0, -1, 1);
        EmitBranches(f, m, rng);
        EmitFronds(f.Frond, m, rng);
        _guides = null;
        _branches = null;
        _leafPts = null;
        // escala do SpeedTree: TODAS as medidas × tamanho do .spt em metros (CTreeEngine::Compute passa fTreeSize ao
        // galho; o cliente faz SetTreeSize(tamanho × 0,3048), EL_Tree.cpp). Conferido 30/10 com print do jogo
        // (ColumnarEnglishGreen1 em 1064,1113): tronco limpo até ~9 m e copa bem alta, como esta escala dá (7 m); a
        // escala antiga (altura = tamanho) deixava as árvores 2,5× menores
        float top = 0;
        foreach (var p in m.Pos) top = Math.Max(top, p.Y);
        foreach (var p in m.FrondPos) top = Math.Max(top, p.Y);
        foreach (var l in m.Leaves) top = Math.Max(top, l.Pos.Y);
        float k = f.Size * 0.3048f;
        for (int i = 0; i < m.Pos.Count; i++) m.Pos[i] *= k;
        for (int i = 0; i < m.FrondPos.Count; i++) m.FrondPos[i] *= k;
        for (int i = 0; i < m.Leaves.Count; i++) m.Leaves[i] = (m.Leaves[i].Pos * k, m.Leaves[i].Dir);
        m.Height = top * k; m.Scale = k;
        return m;
    }

    static Vector3 Rotate(Vector3 v, Vector3 axis, float deg) =>
        Vector3.Transform(v, Quaternion.FromAxisAngle(axis, MathHelper.DegreesToRadians(deg)));

    /// <summary>
    /// Gravidade num vértice: gira os eixos em volta de (direção × baixo) por p·g·a·horiz graus, com a = ângulo entre a
    /// direção e baixo, horiz = 1 − |90 − a|/90 e p = −2·(perfil(t) − 0,5). Positivo puxa para baixo, negativo endireita.
    /// </summary>
    static Frame Gravity(Frame tm, SptLevel L, float gravity, float t, SptRandom rng)
    {
        var down = -Vector3.UnitZ;
        var d = tm.X.Normalized();
        float a = MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(d, down), -1, 1)));
        float horiz = 1 - MathF.Abs(90 - a) / 90;
        float p = -2 * (L.GravityProfile.Eval(t, rng) - 0.5f);
        var axis = Vector3.Cross(d, down);
        return axis.LengthSquared < 1e-8f ? tm : tm.Turn(axis.Normalized(), p * gravity * a * horiz);
    }

    /// <summary>
    /// Um galho: pos/parent/axis no espaço do SpeedTree. parent = eixos do vértice do pai (identidade no tronco); axis = eixo
    /// do giro sorteado (no tronco Z, no filho a direção do pai).
    /// </summary>
    static void Grow(SptFile f, SptMesh m, SptRandom rng, int seed, int level, Vector3 pos, Frame parent, Vector3 axis,
        float rel, float maxRadius, float scale)
    {
        var L = f.Levels[level];
        // sorteios na ordem do runtime: comprimento, giro, ângulo inicial, gravidade, raio, flexibilidade (vento, não usado)
        float length = L.Length.Eval(rel, rng) * scale;
        float roll = rng.Range(-180, 180);
        float start = L.StartAngle.Eval(rel, rng);
        float gravity = L.Gravity.Eval(rel, rng);
        float radius = L.Radius.Eval(rel, rng) * scale;
        L.Flexibility.Eval(rel, rng);
        float baseR = L.Taper.Eval(0, rng) * radius;   // 1º raio: área da fronde
        // o 1º raio não passa de 0,85 do raio do pai naquele ponto, e o limite vira o raio do galho (CBranch::Compute)
        if (maxRadius > 0 && maxRadius * ParentRadiusFactor < baseR) baseR = radius = maxRadius * ParentRadiusFactor;
        if (length <= 0 || radius <= 0) return;

        int segs = Math.Max(1, L.Segments), sides = Math.Max(3, L.Sides);
        float tileV = L.AbsoluteV ? L.TileV : length / scale * L.TileV;
        float tileU = L.AbsoluteU ? L.TileU : L.TileU * radius * MathF.Tau;

        // vértice 0: eixos do pai, giro em volta do eixo, ângulo inicial + perturbação, gravidade
        var tms = new Frame[segs + 1]; var pts = new Vector3[segs + 1]; var radii = new float[segs + 1];
        var tm = parent.Turn(axis.Normalized(), roll);
        float d1 = L.Disturbance.Jitter(0, rng), d2 = L.Disturbance.Jitter(0, rng);
        tm = tm.RotateYZ(start + d2, d1);
        radii[0] = radius * L.Taper.Eval(0, rng);
        L.FlexScale.Eval(0, rng);
        tm = Gravity(tm, L, gravity, 0, rng);
        tms[0] = tm; pts[0] = pos;
        float run = 0;
        var runs = new float[segs + 1]; var ts = new float[segs + 1];
        for (int i = 1; i <= segs; i++)
        {
            float t = MathF.Pow(i / (float)segs, L.SegmentExponent);   // CBranch::Compute: anéis pelo expoente, não em passos iguais
            float step = t * length - run; run += step;
            runs[i] = run; ts[i] = t;
            radii[i] = radius * L.Taper.Eval(t, rng);
            L.FlexScale.Eval(t, rng);
            tm = Gravity(tm, L, gravity, t, rng);
            d1 = L.Disturbance.Jitter(t, rng); d2 = L.Disturbance.Jitter(t, rng);
            tm = tm.RotateYZ(d2, d1);
            pts[i] = pts[i - 1] + tms[i - 1].X * step;   // avança na direção do vértice anterior
            tms[i] = tm;
        }
        var centers = pts.Select(Ours).ToArray();
        var dirs = tms.Select(x => Ours(x.X).Normalized()).ToArray();
        var side = Ours(tms[0].Y).Normalized();

        bool leafLevel = level >= f.Levels.Count - 1;
        bool frond = !leafLevel && f.Frond is { Enabled: true, Type: 0 or 1, Maps.Count: > 0 } fr0 && level >= fr0.Level;
        if (frond) AddGuide(f.Frond!, rng, pts, centers, dirs, tms, side, baseR);
        else if (!leafLevel)
        {
            // o galho só vira malha depois de escolher quais ficam na LOD alta (EmitBranches)
            float area = 0;
            for (int i = 0; i < segs; i++)
                area += (radii[i] + radii[i + 1]) * _unit * FastSqrt(((pts[i + 1] - pts[i]) * _unit).LengthSquared);
            _branches!.Add(new Branch(centers, tms, radii, ts, sides, tileU, tileV, MakeFlares(L, _flareRand!), area));
        }

        // filhos do próximo nível (ou pontos de folha, se o próximo é o último)
        if (level + 1 >= f.Levels.Count) return;
        var C = f.Levels[level + 1];
        int count = (int)(L.Frequency * length / scale);   // frequência = filhos por unidade de comprimento do pai
        bool childIsLeaf = level + 1 >= f.Levels.Count - 1;
        var local = new List<Vector3>();   // folhas deste galho (colisão 1)
        var leafPts = _leafPts ??= new List<Vector3>();
        for (int c = 0; c < count; c++)
        {
            // galho: semente +3 a cada filho; folhas seguem o mesmo gerador sem ressemear
            if (!childIsLeaf) { seed += 3; rng.Seed(seed); }
            float at = c == 0 && !childIsLeaf
                ? rng.Range(Lerp(L.First, L.Last, 0.85f), Lerp(L.First, L.Last, 0.95f))
                : rng.Range(L.First, L.Last);
            float childRel = L.Last != L.First ? (at - L.First) / (L.Last - L.First) : 1;
            // lugar no pai pela distância percorrida (CBranch::FillBranch): os anéis não têm passo igual
            float along = at * length; int k = 0;
            for (int i = 1; i <= segs; i++) if (along < runs[i]) { k = i - 1; break; }
            float fr = (along - runs[k]) / MathF.Max(runs[k + 1] - runs[k], 1e-6f);
            var p = Vector3.Lerp(pts[k], pts[k + 1], fr);
            float pr = Lerp(radii[k], radii[k + 1], fr);
            if (childIsLeaf)
            {
                // CIdvBranch::ComputeBud: broto de um segmento (comprimento do nível / segmentos) girado 60° do pai; a folha
                // fica na ponta do broto
                float bud = C.Length.Eval(childRel, rng) * scale / Math.Max(1, C.Segments);
                var lf = tms[k].Turn(tms[k].X.Normalized(), rng.Range(-180, 180)).RotateYZ(60, 0);
                var ld = lf.X.Normalized();
                // CBranch::MakeLeaf: textura sorteada entre as que não são flor; RoomForLeaf; só a folha aceita sorteia
                // o grupo de balanço e a variação de cor
                var lp = p + ld * bud;
                int map = ChooseLeafMap(f, rng, f.BlossomLevel == 0 ? childRel : rel);
                if (map < 0) continue;
                if (f.LeafCollision != 0)
                {
                    var sz = f.LeafMaps[map].Size;
                    float room = MathF.Max(sz.X, sz.Y) * f.LeafSpacing;
                    var near = f.LeafCollision == 1 ? local : leafPts;
                    bool crowded = false;
                    for (int q = near.Count - 1; q >= 0 && !crowded; q--)
                    {
                        var o = near[q];
                        crowded = MathF.Abs(lp.X - o.X) < room && MathF.Abs(lp.Y - o.Y) < room && MathF.Abs(lp.Z - o.Z) < room;
                    }
                    if (crowded) continue;
                }
                rng.Range(0, 10000); rng.Range(0, 1);
                leafPts.Add(lp); local.Add(lp);
                m.Leaves.Add((Ours(lp), Ours(ld))); m.LeafMap.Add(map);
                continue;
            }
            rng.Seed(seed); rng.Range(0, 100);   // o runtime ressemeia e descarta um número antes do filho
            Grow(f, m, rng, seed, level + 1, p, tms[k], tms[k].X, childRel, pr, scale);
        }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Raiz aproximada que a speedtreert.dll usa em comprimentos e áreas.</summary>
    static float FastSqrt(float x) => BitConverter.Int32BitsToSingle((BitConverter.SingleToInt32Bits(x) >> 1) + 0x1fc00000);

    sealed record Branch(Vector3[] Centers, Frame[] Tms, float[] Radii, float[] Ts, int Sides, float TileU, float TileV, Flare[] Flares, float Area);

    /// <summary>
    /// Galhos da LOD alta: o jogo desenha de perto só os maiores até somar 9012 da área (FUN_100286f0). Cada galho fora da
    /// retenção gasta um sorteio de fuzziness, e é isso que deixa o gerador no ponto certo para as frondes.
    /// </summary>
    static void EmitBranches(SptFile f, SptMesh m, SptRandom rng)
    {
        var all = _branches!;
        if (all.Count == 0) return;
        float total = 0, largest = 0;
        foreach (var b in all) { total += b.Area; largest = MathF.Max(largest, b.Area); }
        float limit = (1 - f.BranchRetention) * largest;
        var rest = new List<(Branch B, float Key)>(); var saved = new List<Branch>();
        foreach (var b in all)
            if (b.Area <= limit)
            {
                float u = rng.Range(0, f.BranchFuzziness);
                rest.Add((b, MathF.Max(u * largest + (1 - u) * b.Area, 0)));
            }
            else saved.Add(b);
        saved.Reverse();
        float target = (f.BranchLods < 2 ? 1 : f.BranchArea) * total, sum = 0;
        foreach (var b in saved.Concat(rest.OrderByDescending(r => r.Key).Select(r => r.B)))
        {
            if (target <= sum) break;
            sum += b.Area;
            AddRings(m, b);
        }
    }

    /// <summary>Anéis de vértices (sides + 1 para fechar a costura da textura), nos eixos do próprio vértice (FUN_10005400).</summary>
    static void AddRings(SptMesh m, Branch b)
    {
        int first = m.Pos.Count, sides = b.Sides, segs = b.Centers.Length - 1;
        for (int i = 0; i <= segs; i++)
        {
            var ay = Ours(b.Tms[i].Y); var az = Ours(b.Tms[i].Z);
            for (int j = 0; j <= sides; j++)
            {
                float u = j / (float)sides, ang = u * MathF.Tau;
                var n = ay * MathF.Cos(ang) + az * MathF.Sin(ang);
                m.Pos.Add(b.Centers[i] + n * (b.Radii[i] * (1 + FlarePush(b.Flares, ang, b.Ts[i])))); m.Normal.Add(n);
                m.Uv.Add(new Vector2(u * b.TileU, b.Ts[i] * b.TileV));
            }
        }
        for (int i = 0; i < segs; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = first + i * (sides + 1) + j, c = a + sides + 1;
                m.Index.Add(a); m.Index.Add(c); m.Index.Add(a + 1);
                m.Index.Add(a + 1); m.Index.Add(c); m.Index.Add(c + 1);
            }
    }

    readonly record struct Flare(float Angle, float Spread, float SpreadExp, float Reach, float ReachExp, float Distance);

    /// <summary>Flares de um galho: saliências na base (CBranch, FUN_100058b0 da speedtreert.dll).</summary>
    static Flare[] MakeFlares(SptLevel L, SptCRand r)
    {
        if (L.FlareCount <= 0) return Array.Empty<Flare>();
        const float Tau = MathF.PI * 2;
        var v = L.Flare; var o = new Flare[L.FlareCount];
        float start = r.Next(), slice = Tau / L.FlareCount;
        for (int i = 0; i < o.Length; i++)
        {
            float min = slice * v[0];
            float ang = ((slice - min) * r.Next() + min) * i + start * Tau;
            if (ang > Tau) ang -= Tau;
            float a = v[1] - v[2], spread = ((v[1] + v[2] - a) * r.Next() + a) * (MathF.PI / 180);
            float c = v[6] - v[7], reach = (v[6] + v[7] - c) * r.Next() + c;
            float d = v[4] - v[5], dist = (v[4] + v[5] - d) * r.Next() + d;
            o[i] = new Flare(ang, spread, v[3], reach, v[8], dist);
        }
        return o;
    }

    /// <summary>Quanto os flares empurram o anel neste ângulo e nesta altura do galho (0 = nada).</summary>
    static float FlarePush(Flare[] flares, float ang, float t)
    {
        float sum = 0;
        foreach (var f in flares)
        {
            float a = ang, c = f.Angle;
            if (MathF.Abs(a - c) > MathF.PI) { if (a < c) a += MathF.Tau; else c += MathF.Tau; }
            float diff = MathF.Abs(a - c), left = f.Reach - t;
            if (diff < f.Spread && left > 0)
                sum += MathF.Pow(1 - diff / f.Spread, f.SpreadExp) * MathF.Pow(left / f.Reach, f.ReachExp) * f.Distance;
        }
        return sum;
    }

    [ThreadStatic] static List<Vector3>? _leafPts;   // folhas já postas na árvore em geração (colisão 2)

    /// <summary>
    /// Textura da folha (CBranch::MakeLeaf/IsBlossom): cada textura conta 2 vezes (normal e espelhada); flor ou não pela
    /// posição no galho e pelo peso; -1 = folha descartada (não é flor e a árvore só tem flores).
    /// </summary>
    static int ChooseLeafMap(SptFile f, SptRandom rng, float percent)
    {
        var bloss = new List<int>(); var plain = new List<int>();
        for (int i = 0; i < f.LeafMaps.Count; i++) (f.LeafMaps[i].Blossom ? bloss : plain).Add(i);
        bool isBlossom = false;
        if (bloss.Count > 0)
        {
            float d = f.BlossomDistance;
            isBlossom = (d < 0 ? percent < -d : percent > d) && rng.Range(0, 1) <= f.BlossomWeighting;
        }
        var list = isBlossom ? bloss : plain;
        if (list.Count == 0) return -1;
        int idx = (int)rng.Range(0, isBlossom ? 100000 : 1000000) % (2 * list.Count);
        return list[idx / 2];
    }

    /// <summary>
    /// Fronde em extrusão (CFrondEngine tipo 1, FUN_10025990/FUN_10024aa0): a seção é o perfil 13005 espelhado — n pontos de
    /// uma borda ao centro e n−1 de volta (2n−1 no total): lado = ±s·w, altura = (perfil(s) − perfil(0))·w, s = 1 … 0 … 1 —
    /// girada pelo giro sorteado e arrastada pelo eixo do galho; s da textura atravessa a seção, t vai da base à ponta.
    /// </summary>
    /// <summary>
    /// Fronde extrudada (FUN_1000bde0 e FUN_1000cda0): perfil espelhado, cada ponto sorteia a variação da curva. O ponto fica
    /// na média dos eixos deste vértice e do anterior, girados pela inclinação.
    /// </summary>
    static void Extrude(SptFrond fr, SptMesh m, Guide g, SptRandom rng, float s0, float s1, float t0, float t1)
    {
        int n = fr.Segments, cols = 2 * n - 1, rows = g.Centers.Length;
        float w = g.Half, baseZ = fr.Profile.Eval(0, rng) * w;
        var prof = new (float Y, float Z)[cols];
        for (int i = 0; i < n; i++)
        {
            float a = 1 - i / (float)(n - 1);
            prof[i] = (-a * w, fr.Profile.Eval(a, rng) * w - baseZ);
            prof[cols - 1 - i] = (a * w, prof[i].Item2);
        }
        float rad = MathHelper.DegreesToRadians(g.Tilt), ca = MathF.Cos(rad), sa = MathF.Sin(rad);
        (Vector3 Y, Vector3 Z) Axes(Frame t) { var y = Ours(t.Y); var z = Ours(t.Z); return (y * ca + z * sa, z * ca - y * sa); }
        int first = m.FrondPos.Count;
        var grid = new Vector3[rows * cols];
        for (int i = 0; i < rows; i++)
        {
            var (ay, az) = Axes(g.Tms[i]);
            var (by, bz) = Axes(g.Tms[i == 0 ? 0 : i - 1]);
            for (int j = 0; j < cols; j++)
                grid[i * cols + j] = g.Centers[i] + (ay * prof[j].Y + az * prof[j].Z + by * prof[j].Y + bz * prof[j].Z) * 0.5f;
        }
        var run = new float[rows * cols];
        for (int j = 0; j < cols; j++)
            for (int i = 1; i < rows; i++)
                run[i * cols + j] = run[(i - 1) * cols + j] + (grid[i * cols + j] - grid[(i - 1) * cols + j]).Length;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                float v = run[i * cols + j] / MathF.Max(run[(rows - 1) * cols + j], 1e-6f) * 0.999f;
                m.FrondPos.Add(grid[i * cols + j]);
                m.FrondUv.Add(new Vector2(Lerp(s0, s1, j / (float)(cols - 1)), Lerp(t1, t0, v)));
            }
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols - 1; j++)
            {
                int a = first + i * cols + j, b = a + cols;
                m.FrondIndex.Add(a); m.FrondIndex.Add(b); m.FrondIndex.Add(a + 1);
                m.FrondIndex.Add(a + 1); m.FrondIndex.Add(b); m.FrondIndex.Add(b + 1);
            }
    }

    /// <summary>
    /// Fronde em lâminas (CFrondEngine, tipo 0): n fitas pelo eixo do galho, a i-ésima girada i·180°/n + giro sorteado da
    /// textura (negativo em galhos alternados); meia largura = comprimento × tamanho × 0,5 × proporção; s atravessa a fita,
    /// t vai da base (embaixo na imagem) à ponta.
    /// </summary>
    sealed record Guide(SptFrondMap Map, Vector3[] Centers, Vector3[] Dirs, Frame[] Tms, Vector3 Side, float Half, float Tilt, float Area);
    [ThreadStatic] static List<Guide>? _guides;
    [ThreadStatic] static List<Branch>? _branches;
    [ThreadStatic] static SptCRand? _flareRand;
    [ThreadStatic] static float _unit;
    const float ParentRadiusFactor = 0.85f;

    /// <summary>Fim de uma guia de fronde (FUN_1000a2a0): comprimento pela raiz aproximada, textura sorteada, inclinação alternada.</summary>
    static void AddGuide(SptFrond fr, SptRandom rng, Vector3[] pts, Vector3[] centers, Vector3[] dirs, Frame[] tms, Vector3 side, float firstRadius)
    {
        var g = _guides!;
        float len = 0;
        for (int i = 0; i + 1 < pts.Length; i++) len += FastSqrt(((pts[i + 1] - pts[i]) * _unit).LengthSquared);
        var map = fr.Maps[(int)rng.Range(0, 100000) % fr.Maps.Count];
        float tilt = rng.Range(map.MinAngle, map.MaxAngle) * (g.Count % 2 == 0 ? -1 : 1);
        g.Add(new Guide(map, centers, dirs, tms, side, len / _unit * map.Size * 0.5f * map.Aspect, tilt, len * firstRadius * _unit));
    }

    /// <summary>Frondes da LOD alta: as maiores até passar de MaxArea da área somada, com a chave de fuzziness (FUN_1000d450).</summary>
    static void EmitFronds(SptFrond? fr, SptMesh m, SptRandom rng)
    {
        if (fr is null || _guides is not { Count: > 0 } all) return;
        float total = 0, largest = 0;
        foreach (var g in all) { total += g.Area; largest = MathF.Max(largest, g.Area); }
        float limit = (1 - fr.LargeRetention) * largest, square = largest * largest;
        var rest = new List<(Guide G, float Key)>(); var saved = new List<Guide>();
        foreach (var g in all)
            if (g.Area <= limit)
            {
                float u = rng.Range(0, fr.Fuzziness);
                rest.Add((g, u * square + (1 - u) * g.Area));
            }
            else saved.Add(g);
        saved.Reverse();
        float target = fr.MaxArea * total, sum = 0;
        if (target <= 0) return;
        foreach (var g in saved.Concat(rest.OrderByDescending(r => r.Key).Select(r => r.G)))
        {
            if (sum > target) break;
            sum += g.Area;
            Frond(fr, m, g, rng);
        }
    }

    static void Frond(SptFrond fr, SptMesh m, Guide g, SptRandom rng)
    {
        var (map, centers, dirs, side, half, tilt) = (g.Map, g.Centers, g.Dirs, g.Side, g.Half, g.Tilt);
        var uv = map.Uv;
        float s0 = MathF.Min(MathF.Min(uv[0], uv[2]), MathF.Min(uv[4], uv[6])), s1 = MathF.Max(MathF.Max(uv[0], uv[2]), MathF.Max(uv[4], uv[6]));
        float t0 = MathF.Min(MathF.Min(uv[1], uv[3]), MathF.Min(uv[5], uv[7])), t1 = MathF.Max(MathF.Max(uv[1], uv[3]), MathF.Max(uv[5], uv[7]));
        int n = centers.Length;
        if (fr.Type == 1) { Extrude(fr, m, g, rng, s0, s1, t0, t1); return; }
        // cada lâmina nos eixos do ponto: o último usa os do anterior e os do meio ficam na média dos dois (FUN_1000ab90)
        float step = 180f / fr.Blades;
        var cross = new Vector3[n]; var plus = new Vector3[n]; var minus = new Vector3[n];
        for (int bl = 0; bl < fr.Blades; bl++)
        {
            int first = m.FrondPos.Count;
            float deg = MathHelper.DegreesToRadians(bl * step + tilt), ca = MathF.Cos(deg), sa = MathF.Sin(deg);
            for (int i = 0; i < n; i++)
            {
                var t = g.Tms[i >= n - 1 ? i - 1 : i];
                cross[i] = Ours(t.Y) * ca + Ours(t.Z) * sa;
                var w = i == n - 1 ? cross[i - 1] : i == 0 ? cross[0] : (cross[i - 1] + cross[i]) * 0.5f;
                plus[i] = centers[i] + w * half; minus[i] = centers[i] - w * half;
            }
            float runP = 0, runM = 0, lenP = 0, lenM = 0;
            for (int i = 1; i < n; i++) { lenP += (plus[i] - plus[i - 1]).Length; lenM += (minus[i] - minus[i - 1]).Length; }
            for (int i = 0; i < n; i++)
            {
                if (i > 0) { runP += (plus[i] - plus[i - 1]).Length; runM += (minus[i] - minus[i - 1]).Length; }
                float vp = i == n - 1 ? 1 : runP / MathF.Max(lenP, 1e-6f) * 0.999f, vm = i == n - 1 ? 1 : runM / MathF.Max(lenM, 1e-6f) * 0.999f;
                m.FrondPos.Add(minus[i]); m.FrondUv.Add(new Vector2(s0, Lerp(t1, t0, vm)));
                m.FrondPos.Add(plus[i]); m.FrondUv.Add(new Vector2(s1, Lerp(t1, t0, vp)));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = first + i * 2;
                m.FrondIndex.Add(a); m.FrondIndex.Add(a + 2); m.FrondIndex.Add(a + 1);
                m.FrondIndex.Add(a + 1); m.FrondIndex.Add(a + 2); m.FrondIndex.Add(a + 3);
            }
        }
    }
}
