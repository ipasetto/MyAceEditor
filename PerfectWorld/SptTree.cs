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
    public List<SptLevel> Levels { get; } = new();
    /// <summary>Texturas de folha (4000–4007): âncora e tamanho do cartão (unidades da árvore) e o recorte no mapa composto.</summary>
    public List<SptLeafMap> LeafMaps { get; } = new();
    /// <summary>Frondes (13000): a partir do nível Level o galho vira fita(s) plana(s) em vez de tubo; null = sem frondes.</summary>
    public SptFrond? Frond { get; private set; }

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
        for (int p = after; p + 12 <= b.Length; p++)
            if (BitConverter.ToInt32(b, p) == 10000 && BitConverter.ToInt32(b, p + 4) == 10002)
            {
                try { ReadTexCoords(new Reader(b, p + 4)); } catch (Exception) { }
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
                        for (int k = 0; k < 8; k++) uv[k] = r.Float();
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
                case 3000 or 3002 or 3004 or 3005 or 3007 or 3010: r.Float(); break;
                case 3001 or 3008: r.Int(); break;
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
                                case 4000: r.Bool(); break;
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
                case 13005: r.Str(); break;
                case 13006 or 13009 or 14007 or 14008: r.Int(); break;
                case 13007: r.Bool(); break;
                case 13010 or 13011 or 13012 or 13013: r.Float(); break;
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
    public float Jitter(float t, SptRandom rng) { float y = Variance * Shape(t); return rng.Range(-y, y); }
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
    /// <summary>Fitas das frondes (posição, uv no mapa composto, índices), à parte da casca.</summary>
    public readonly List<Vector3> FrondPos = new();
    public readonly List<Vector2> FrondUv = new();
    public readonly List<int> FrondIndex = new();
    public float Height, Scale = 1;   // Scale: unidades da árvore → metros (para o tamanho das folhas)
}

/// <summary>Crescimento dos galhos (CTreeEngine: galho recursivo, filhos com semente +3 cada).</summary>
public static class SptGrower
{
    /// <summary>Gera a árvore em metros, com Y para cima e a base do tronco na origem.</summary>
    public static SptMesh Build(SptFile f)
    {
        var m = new SptMesh();
        if (f.Levels.Count == 0) return m;
        var rng = new SptRandom(f.Seed);
        float size = Math.Max(1, rng.Range(f.Size - f.SizeVariance, f.Size + f.SizeVariance));
        int seed = f.Seed;
        Grow(f, m, rng, ref seed, 0, Vector3.Zero, Vector3.UnitY, Vector3.UnitX, 0, -1, 1);
        // tamanho da árvore = altura (pés → metros), como SetTreeSize(tamanho × 0,3048) no cliente
        float top = 0;
        foreach (var p in m.Pos) top = Math.Max(top, p.Y);
        foreach (var p in m.FrondPos) top = Math.Max(top, p.Y);
        foreach (var l in m.Leaves) top = Math.Max(top, l.Pos.Y);
        float k = top > 0 ? size * 0.3048f / top : 1;
        for (int i = 0; i < m.Pos.Count; i++) m.Pos[i] *= k;
        for (int i = 0; i < m.FrondPos.Count; i++) m.FrondPos[i] *= k;
        for (int i = 0; i < m.Leaves.Count; i++) m.Leaves[i] = (m.Leaves[i].Pos * k, m.Leaves[i].Dir);
        m.Height = top * k; m.Scale = k;
        return m;
    }

    static Vector3 Rotate(Vector3 v, Vector3 axis, float deg) =>
        Vector3.Transform(v, Quaternion.FromAxisAngle(axis, MathHelper.DegreesToRadians(deg)));

    static void Grow(SptFile f, SptMesh m, SptRandom rng, ref int seed, int level, Vector3 pos, Vector3 dir, Vector3 side,
        float rel, float maxRadius, float scale)
    {
        var L = f.Levels[level];
        float length = L.Length.Eval(rel, rng) * scale;
        float roll = rng.Range(-180, 180);
        float gravity = L.Gravity.Eval(rel, rng);
        L.Flexibility.Eval(rel, rng);   // vento: não usado aqui, mas consome o gerador como no runtime
        float radius = L.Radius.Eval(rel, rng) * scale;
        float baseR = L.Taper.Eval(0, rng) * radius;
        if (maxRadius > 0 && baseR > maxRadius * 0.85f) { radius *= maxRadius * 0.85f / baseR; baseR = maxRadius * 0.85f; }
        if (length <= 0 || radius <= 0) return;

        int segs = Math.Max(1, L.Segments), sides = Math.Max(3, L.Sides);
        float tileV = L.AbsoluteV ? L.TileV : length / scale * L.TileV;
        float tileU = L.AbsoluteU ? L.TileU : L.TileU * radius * MathF.Tau;

        // eixo do galho: segmentos com perturbação e gravidade
        var centers = new Vector3[segs + 1]; var dirs = new Vector3[segs + 1]; var radii = new float[segs + 1];
        centers[0] = pos; dirs[0] = dir; radii[0] = baseR;
        var d = dir; var s = side;
        for (int i = 1; i <= segs; i++)
        {
            float t = i / (float)segs;
            var perp = Vector3.Cross(d, s); if (perp.LengthSquared < 1e-6f) perp = Vector3.Cross(d, Vector3.UnitZ);
            d = Rotate(d, s.Normalized(), L.Disturbance.Jitter(t, rng));
            d = Rotate(d, perp.Normalized(), L.Disturbance.Jitter(t, rng));
            // gravidade: dobra em volta de (direção × cima), mais forte no galho deitado; o perfil (6017) diz o sentido
            float sgn = -2 * (L.GravityProfile.Eval(t, rng) - 0.5f);
            var bendAxis = Vector3.Cross(d, Vector3.UnitY);
            if (level > 0 && bendAxis.LengthSquared > 1e-6f)
            {
                float fromUp = MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(d, Vector3.UnitY), -1, 1)));
                float horiz = 1 - MathF.Abs(fromUp - 90) / 90;
                d = Rotate(d, bendAxis.Normalized(), -gravity * horiz * sgn * 90 / segs);
            }
            s = Vector3.Cross(Vector3.Cross(d, s), d); if (s.LengthSquared < 1e-6f) s = Vector3.Cross(d, Vector3.UnitZ); s.Normalize();
            centers[i] = centers[i - 1] + d * (length / segs);
            dirs[i] = d;
            radii[i] = MathF.Min(L.Taper.Eval(t, rng) * radius, baseR);
        }

        bool leafLevel = level >= f.Levels.Count - 1;
        bool frond = !leafLevel && f.Frond is { Type: 0, Maps.Count: > 0 } fr0 && level >= fr0.Level;
        if (frond) Frond(f.Frond!, m, rng, centers, dirs, side, length, (m.FrondIndex.Count / 6) % 2 == 1);
        else if (!leafLevel)
        {
            // anéis de vértices (sides + 1 para fechar a costura da textura)
            int first = m.Pos.Count;
            var ss = side;
            for (int i = 0; i <= segs; i++)
            {
                var a = dirs[i];
                ss = Vector3.Cross(Vector3.Cross(a, ss), a); if (ss.LengthSquared < 1e-6f) ss = Vector3.Cross(a, Vector3.UnitZ); ss.Normalize();
                var bb = Vector3.Cross(a, ss);
                for (int j = 0; j <= sides; j++)
                {
                    float ang = j / (float)sides * MathF.Tau;
                    var n = ss * MathF.Cos(ang) + bb * MathF.Sin(ang);
                    m.Pos.Add(centers[i] + n * radii[i]); m.Normal.Add(n);
                    m.Uv.Add(new Vector2(j / (float)sides * tileU, i / (float)segs * tileV));
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = first + i * (sides + 1) + j, b = a + sides + 1;
                    m.Index.Add(a); m.Index.Add(b); m.Index.Add(a + 1);
                    m.Index.Add(a + 1); m.Index.Add(b); m.Index.Add(b + 1);
                }
        }

        // filhos do próximo nível (ou pontos de folha, se o próximo é o último)
        if (level + 1 >= f.Levels.Count) return;
        var C = f.Levels[level + 1];
        int count = (int)(L.Frequency * length / scale);   // frequência = filhos por unidade de comprimento do pai
        bool childIsLeaf = level + 1 >= f.Levels.Count - 1;
        for (int c = 0; c < count; c++)
        {
            seed += 3;
            rng.Seed(seed);
            float at = c == 0 && !childIsLeaf
                ? rng.Range(Lerp(L.First, L.Last, 0.85f), Lerp(L.First, L.Last, 0.95f))
                : rng.Range(L.First, L.Last);
            float childRel = L.Last != L.First ? (at - L.First) / (L.Last - L.First) : 1;
            float u = at * segs; int k = Math.Min((int)u, segs - 1); float fr = u - k;
            var p = Vector3.Lerp(centers[k], centers[k + 1], fr);
            var pd = Vector3.Lerp(dirs[k], dirs[k + 1], fr).Normalized();
            float pr = Lerp(radii[k], radii[k + 1], fr);
            // direção do filho: ângulo inicial a partir do eixo do pai, girado em volta dele
            var axis = Vector3.Cross(pd, side); if (axis.LengthSquared < 1e-6f) axis = Vector3.Cross(pd, Vector3.UnitZ); axis.Normalize();
            float start = C.StartAngle.Eval(childRel, rng);
            var cd = Rotate(Rotate(pd, axis, start), pd, rng.Range(-180, 180) + roll);
            if (childIsLeaf) { m.Leaves.Add((p, cd)); continue; }
            Grow(f, m, rng, ref seed, level + 1, p, cd.Normalized(), pd, childRel, pr, scale);
        }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Fronde em lâminas (CFrondEngine, tipo 0): n fitas pelo eixo do galho, a i-ésima girada i·180°/n + giro sorteado da
    /// textura (negativo em galhos alternados); meia largura = comprimento × tamanho × 0,5 × proporção; s atravessa a fita,
    /// t vai da base (embaixo na imagem) à ponta.
    /// </summary>
    static void Frond(SptFrond fr, SptMesh m, SptRandom rng, Vector3[] centers, Vector3[] dirs, Vector3 side, float length, bool odd)
    {
        var map = fr.Maps[(int)MathF.Round(rng.Range(0, 100000)) % fr.Maps.Count];
        float half = length * map.Size * 0.5f * map.Aspect;
        float tilt = rng.Range(map.MinAngle, map.MaxAngle) * (odd ? -1 : 1);
        var uv = map.Uv;
        float s0 = MathF.Min(MathF.Min(uv[0], uv[2]), MathF.Min(uv[4], uv[6])), s1 = MathF.Max(MathF.Max(uv[0], uv[2]), MathF.Max(uv[4], uv[6]));
        float t0 = MathF.Min(MathF.Min(uv[1], uv[3]), MathF.Min(uv[5], uv[7])), t1 = MathF.Max(MathF.Max(uv[1], uv[3]), MathF.Max(uv[5], uv[7]));
        int n = centers.Length;
        float step = 180f / fr.Blades;
        for (int bl = 0; bl < fr.Blades; bl++)
        {
            int first = m.FrondPos.Count;
            var ss = side;
            for (int i = 0; i < n; i++)
            {
                var a = dirs[i];
                ss = Vector3.Cross(Vector3.Cross(a, ss), a); if (ss.LengthSquared < 1e-6f) ss = Vector3.Cross(a, Vector3.UnitZ); ss.Normalize();
                var across = Rotate(ss, a, bl * step + tilt);
                float tt = Lerp(t1, t0, i / (float)(n - 1));
                m.FrondPos.Add(centers[i] - across * half); m.FrondUv.Add(new Vector2(s0, tt));
                m.FrondPos.Add(centers[i] + across * half); m.FrondUv.Add(new Vector2(s1, tt));
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
