using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Triângulos da malha das construções mexidas, no mundo, para a passagem dos monstros (usuário 09/11: passavam pela ponte
/// e por construções com pouca ou nenhuma colisão no map.bht). O editor oficial gerava a passagem com as cascas do modelo,
/// não só com os brushes do servidor; aqui a própria malha faz esse papel: triângulo na altura do corpo é parede e
/// triângulo deitado é piso (anda-se em cima da ponte). Grade de 2 m para achar rápido.
/// </summary>
public sealed class MeshObstacles
{
    const float Cell = 2f;
    readonly List<(Vector3 A, Vector3 B, Vector3 C)> _tris = new();
    readonly Dictionary<(int, int), List<int>> _grid = new();

    public int Count => _tris.Count;

    public void Add(Vector3 a, Vector3 b, Vector3 c)
    {
        if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) return;   // triângulo sem área
        int id = _tris.Count; _tris.Add((a, b, c));
        int i0 = Key(MathF.Min(a.X, MathF.Min(b.X, c.X))), i1 = Key(MathF.Max(a.X, MathF.Max(b.X, c.X)));
        int k0 = Key(MathF.Min(a.Z, MathF.Min(b.Z, c.Z))), k1 = Key(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
        if ((long)(i1 - i0 + 1) * (k1 - k0 + 1) > 4096) return;   // gigante (não deveria haver numa construção): fica de fora
        for (int i = i0; i <= i1; i++) for (int k = k0; k <= k1; k++)
            {
                if (!_grid.TryGetValue((i, k), out var l)) _grid[(i, k)] = l = new();
                l.Add(id);
            }
    }

    static int Key(float v) => (int)MathF.Floor(v / Cell);

    IEnumerable<int> Near(float x0, float z0, float x1, float z1)
    {
        var seen = new HashSet<int>();
        for (int i = Key(x0); i <= Key(x1); i++) for (int k = Key(z0); k <= Key(z1); k++)
                if (_grid.TryGetValue((i, k), out var l)) foreach (int id in l) if (seen.Add(id)) yield return id;
    }

    /// <summary>Algum triângulo encosta na caixa (teste de eixos separadores, Akenine-Möller)?</summary>
    public bool Overlaps(Vector3 mn, Vector3 mx)
    {
        var c = (mn + mx) / 2; var h = (mx - mn) / 2;
        foreach (int id in Near(mn.X, mn.Z, mx.X, mx.Z))
        {
            var (a, b, cc) = _tris[id];
            if (TriBox(a - c, b - c, cc - c, h)) return true;
        }
        return false;
    }

    /// <summary>
    /// Piso mais alto na vertical (x, z) entre <paramref name="top"/> e <paramref name="bottom"/>: triângulo deitado
    /// (|normal.y| &gt; 0,5) que a vertical cruza. null = nenhum.
    /// </summary>
    public float? FloorBelow(float x, float z, float top, float bottom)
    {
        float? best = null;
        foreach (int id in Near(x, z, x, z))
        {
            var (a, b, c) = _tris[id];
            var n = Vector3.Cross(b - a, c - a);
            if (MathF.Abs(n.Y) < 0.5f * n.Length()) continue;
            // baricêntricas no plano XZ
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-9f) continue;
            float l1 = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
            float l2 = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
            float l3 = 1 - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
            float y = l1 * a.Y + l2 * b.Y + l3 * c.Y;
            if (y > top || y < bottom) continue;
            if (best is null || y > best) best = y;
        }
        return best;
    }

    // ---------- triângulo × caixa (centrada na origem, meias-medidas h) ----------
    static bool TriBox(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 h)
    {
        // 1) eixos da caixa
        if (MathF.Max(v0.X, MathF.Max(v1.X, v2.X)) < -h.X || MathF.Min(v0.X, MathF.Min(v1.X, v2.X)) > h.X) return false;
        if (MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y)) < -h.Y || MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y)) > h.Y) return false;
        if (MathF.Max(v0.Z, MathF.Max(v1.Z, v2.Z)) < -h.Z || MathF.Min(v0.Z, MathF.Min(v1.Z, v2.Z)) > h.Z) return false;
        // 2) plano do triângulo
        var e0 = v1 - v0; var e1 = v2 - v1; var e2 = v0 - v2;
        var n = Vector3.Cross(e0, e1);
        float r0 = h.X * MathF.Abs(n.X) + h.Y * MathF.Abs(n.Y) + h.Z * MathF.Abs(n.Z);
        if (MathF.Abs(Vector3.Dot(n, v0)) > r0) return false;
        // 3) 9 eixos aresta × eixo da caixa
        foreach (var e in new[] { e0, e1, e2 })
        {
            if (!AxisOk(new Vector3(0, -e.Z, e.Y), v0, v1, v2, h)) return false;
            if (!AxisOk(new Vector3(e.Z, 0, -e.X), v0, v1, v2, h)) return false;
            if (!AxisOk(new Vector3(-e.Y, e.X, 0), v0, v1, v2, h)) return false;
        }
        return true;
    }

    static bool AxisOk(Vector3 ax, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 h)
    {
        if (ax.LengthSquared() < 1e-12f) return true;
        float p0 = Vector3.Dot(ax, v0), p1 = Vector3.Dot(ax, v1), p2 = Vector3.Dot(ax, v2);
        float r = h.X * MathF.Abs(ax.X) + h.Y * MathF.Abs(ax.Y) + h.Z * MathF.Abs(ax.Z);
        return !(MathF.Min(p0, MathF.Min(p1, p2)) > r || MathF.Max(p0, MathF.Max(p1, p2)) < -r);
    }

    /// <summary>
    /// Triângulos de uma construção (.bmd) levados do lugar gravado no arquivo para <paramref name="to"/>. Malha ilegível =
    /// nada (a passagem fica só com os brushes, como antes).
    /// </summary>
    public void AddBuilding(byte[] bmd, ObjTransform to)
    {
        if (BmdModel.Read(bmd) is not { } m) return;
        var from = ObjectFormats.ReadBmd(bmd);
        var (r, p0, p1) = DraftSaver.Rigid(from, to);
        float k = from.Scale > 0 ? to.Scale / from.Scale : 1;
        Vector3 Map(float[] p) => Vector3.Transform((new Vector3(p[0], p[1], p[2]) - p0) * k, r) + p1;
        var world = BmdModel.WorldVertices(m);
        int baseV = 0;
        foreach (var mesh in m.Meshes)
        {
            int nv = mesh.Vertices.Length / 24;
            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (a >= nv || b >= nv || c >= nv) continue;
                Add(Map(world[baseV + a]), Map(world[baseV + b]), Map(world[baseV + c]));
            }
            baseV += nv;
        }
    }
}
