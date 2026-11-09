using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// "Pegada" de uma malha no chão para a passagem dos monstros (usuário 09/11: monstros passavam pelas raízes das árvores
/// grandes, pela ponte e por algumas construções). Pega os triângulos da malha na faixa do corpo (do pé até 2 m), corta
/// cada um nessa faixa, marca os quadradinhos de 0,25 m que a parte cortada cobre (vista de cima) e alarga pela folga.
/// O resultado são pontos relativos ao pé do objeto (x, z do jogo) que o .rmap fecha.
/// </summary>
public static class MeshFootprint
{
    public const float Grid = 0.25f;     // lado do quadradinho da pegada
    public const float Band = 2.0f;      // altura do corpo: peça de construção abaixo disso barra o monstro
    public const float TreeHalf = 0.5f;  // árvore: o tronco (com as raízes) do pé até a metade da altura; galhos não barram (usuário 09/11)
    public const float TreeMargin = 0.5f;

    /// <summary>Pontos da pegada (centro dos quadradinhos) relativos à origem dos triângulos.</summary>
    public static List<(float Dx, float Dz)> Of(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> tris, float lo, float hi, float margin)
    {
        var cells = new HashSet<(int, int)>();
        foreach (var (a, b, c) in tris)
        {
            // só por onde a parte cortada passa de verdade (a caixa de um galho fino na diagonal virava um quadrado enorme)
            if (Slab(a, b, c, lo, hi) is not { Count: > 0 } poly) continue;
            float x0 = poly.Min(p => p.X), x1 = poly.Max(p => p.X), z0 = poly.Min(p => p.Z), z1 = poly.Max(p => p.Z);
            int i0 = (int)MathF.Floor(x0 / Grid), i1 = (int)MathF.Floor(x1 / Grid);
            int k0 = (int)MathF.Floor(z0 / Grid), k1 = (int)MathF.Floor(z1 / Grid);
            if ((long)(i1 - i0 + 1) * (k1 - k0 + 1) > 40000) continue;   // triângulo enorme e chato (chão de construção): não é obstáculo
            // contorno: vértices e pontos das arestas a cada meio quadradinho
            for (int e = 0; e < poly.Count; e++)
            {
                var p = poly[e]; var q = poly[(e + 1) % poly.Count];
                float len = MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
                int steps = Math.Max(1, (int)MathF.Ceiling(len / (Grid / 2)));
                for (int st = 0; st <= steps; st++)
                {
                    float f = st / (float)steps;
                    cells.Add(((int)MathF.Floor((p.X + (q.X - p.X) * f) / Grid), (int)MathF.Floor((p.Z + (q.Z - p.Z) * f) / Grid)));
                }
            }
            // miolo: centro do quadradinho dentro do polígono (convexo, visto de cima)
            for (int i = i0; i <= i1; i++)
                for (int k = k0; k <= k1; k++)
                    if (Inside(poly, (i + 0.5f) * Grid, (k + 0.5f) * Grid)) cells.Add((i, k));
        }
        // folga: todo quadradinho até 'margin' de um marcado
        int g = (int)MathF.Ceiling(margin / Grid);
        var grown = new HashSet<(int, int)>(cells);
        if (g > 0)
            foreach (var (i, k) in cells)
                for (int di = -g; di <= g; di++) for (int dk = -g; dk <= g; dk++)
                        if (di * di + dk * dk <= g * g) grown.Add((i + di, k + dk));
        return grown.Select(c => ((c.Item1 + 0.5f) * Grid, (c.Item2 + 0.5f) * Grid)).ToList();
    }

    /// <summary>Parte do triângulo entre as alturas lo e hi (polígono convexo); null = fora da faixa.</summary>
    internal static List<Vector3>? Slab(Vector3 a, Vector3 b, Vector3 c, float lo, float hi)
    {
        if (MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) < lo || MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) > hi) return null;
        var poly = new List<Vector3> { a, b, c };
        poly = Clip(poly, p => p.Y - lo);    // acima de lo
        poly = Clip(poly, p => hi - p.Y);    // abaixo de hi
        return poly.Count == 0 ? null : poly;
    }

    /// <summary>Ponto (x, z) dentro do polígono convexo visto de cima (qualquer sentido de giro).</summary>
    static bool Inside(List<Vector3> poly, float x, float z)
    {
        int sign = 0;
        for (int e = 0; e < poly.Count; e++)
        {
            var p = poly[e]; var q = poly[(e + 1) % poly.Count];
            float cr = (q.X - p.X) * (z - p.Z) - (q.Z - p.Z) * (x - p.X);
            if (MathF.Abs(cr) < 1e-9f) continue;
            int sg = cr > 0 ? 1 : -1;
            if (sign == 0) sign = sg; else if (sg != sign) return false;
        }
        return sign != 0;
    }

    /// <summary>Sutherland–Hodgman com um plano: fica o lado com d(p) &gt;= 0.</summary>
    static List<Vector3> Clip(List<Vector3> poly, Func<Vector3, float> d)
    {
        var r = new List<Vector3>(poly.Count + 2);
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            float dp = d(p), dq = d(q);
            if (dp >= 0) r.Add(p);
            if ((dp >= 0) != (dq >= 0)) r.Add(p + (q - p) * (dp / (dp - dq)));
        }
        return r;
    }

    static readonly Dictionary<string, List<(float Dx, float Dz)>> _trees = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pegada do tronco e das raízes de um tipo de árvore (casca da nossa versão do SpeedTree, sem folhas nem frondes),
    /// do pé até a metade da altura da árvore, com folga de 0,5 m; galhos não barram (o monstro passa por baixo). Lida do trees.pck uma vez por tipo. Vazia = não deu (o círculo continua valendo).
    /// </summary>
    public static List<(float Dx, float Dz)> Tree(string elementDir, string spt)
    {
        lock (_trees)
            if (_trees.TryGetValue(spt, out var hit)) return hit;
        var foot = new List<(float, float)>();
        try
        {
            var pckPath = Path.Combine(elementDir, "trees.pck");
            if (PckArchive.Exists(pckPath))
            {
                using var pck = new PckArchive(pckPath);
                if (pck.Find(spt.Replace('/', '\\').ToLowerInvariant()) is { } e)
                {
                    var m = SptGrower.Build(new SptFile(pck.Read(e)));
                    // espaço da árvore: Y para cima, Z do desenho = −Z do jogo
                    Vector3 G(OpenTK.Mathematics.Vector3 p) => new(p.X, p.Y, -p.Z);
                    // só o tronco (com as raízes salientes): galho baixo que pende os monstros passam por baixo, como no original
                    var ix = m.TrunkIndex;
                    var tris = new List<(Vector3, Vector3, Vector3)>(ix.Count / 3);
                    for (int t = 0; t + 2 < ix.Count; t += 3) tris.Add((G(m.Pos[ix[t]]), G(m.Pos[ix[t + 1]]), G(m.Pos[ix[t + 2]])));
                    foot = Of(tris, -0.5f, MathF.Max(1, m.Height * TreeHalf), TreeMargin);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
        {
            AppLog.Error("passagem: pegada da árvore " + spt, ex);
        }
        lock (_trees) _trees[spt] = foot;
        return foot;
    }
}
