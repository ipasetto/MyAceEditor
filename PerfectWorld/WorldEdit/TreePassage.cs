using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Árvores na passagem dos monstros (movemap\N.rmap). Estudo 09/11 (usuário: os monstros desviam das árvores originais,
/// não das copiadas): o editor oficial gravou um CÍRCULO FECHADO em volta de cada tronco no .rmap (world, pedaço 38:
/// tronco 69% fechado, 1 m 67%, 2 m 48%, 3 m 16%, 5-8 m ~11% = o normal do terreno; bambu 0/33, carvalhos 90-96%). O
/// map.bht não tem as árvores (1.5.5 sem .tbh), então só o .rmap guarda isso. Aqui: o raio de cada tipo é aprendido das
/// árvores originais do mapa; árvore nova/colada/movida fecha o círculo dela; de onde a árvore saiu, o círculo reabre
/// só onde o chão deixa andar (degrau 1 m), não há parede de construção e uma passagem aberta chega até lá.
/// </summary>
public static class TreePassage
{
    public const float DefaultRadius = 1.5f;   // tipo sem árvore original no mapa (veio de outro mapa)
    static readonly float[] Rings = { 0f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f };

    /// <summary>
    /// Raio fechado de um tipo pelas árvores originais dele: fração fechada a cada distância (8 direções) menos o normal
    /// do terreno (a 8 m); raio = maior distância ainda acima da metade do tronco. 0 = o tipo não fecha (bambu).
    /// </summary>
    public static float Radius(IReadOnlyList<(float X, float Z)> trunks, Func<float, float, bool?> open)
    {
        if (trunks.Count < 3) return DefaultRadius;
        double Frac(float r)
        {
            int n = 0, shut = 0;
            foreach (var (x, z) in trunks)
                for (int k = 0; k < (r == 0 ? 1 : 8); k++)
                {
                    float a = k * MathF.PI / 4;
                    if (open(x + MathF.Cos(a) * r, z + MathF.Sin(a) * r) is bool o) { n++; if (!o) shut++; }
                }
            return n == 0 ? 0 : (double)shut / n;
        }
        double base8 = Frac(8), top = Frac(0) - base8;
        if (top < 0.35) return 0;
        float best = 0;
        foreach (var r in Rings) if (Frac(r) - base8 >= top / 2) best = r;
        return Math.Clamp(best, 0.5f, 4f);
    }

    /// <summary>
    /// Aplica num .rmap de um pedaço: <paramref name="gone"/> = círculos que saíram (árvore apagada ou lugar antigo da
    /// movida), <paramref name="come"/> = círculos novos, <paramref name="stay"/> = árvores que continuam (o círculo delas
    /// não reabre). Devolve o arquivo e quantos metros mudaram.
    /// </summary>
    public static (byte[] Rmap, int Changed) Apply(byte[] rmap, float x0, float zSouth, IReadOnlyList<(float X, float Z, float R)> gone,
        IReadOnlyList<(float X, float Z, float R)> come, IReadOnlyList<(float X, float Z, float R)> stay, Func<float, float, float?> height, BrushSet? walls)
    {
        var (w, _, iw, ih, px) = MoveMapFile.Header(rmap);
        var d = (byte[])rmap.Clone();
        int changed = 0;
        bool Get(int u, int v) => (d[28 + v * w + (u >> 3)] & (1 << (u & 7))) != 0;
        void Set(int u, int v, bool on)
        {
            ref byte b = ref d[28 + v * w + (u >> 3)]; byte m = (byte)(1 << (u & 7));
            bool was = (b & m) != 0; if (was == on) return;
            b = on ? (byte)(b | m) : (byte)(b & ~m); changed++;
        }
        float Cx(int u) => x0 + (u + 0.5f) * px;
        float Cz(int v) => zSouth + (v + 0.5f) * px;
        IEnumerable<(int U, int V)> Disc(float x, float z, float r)
        {
            int u0 = Math.Max(0, (int)MathF.Floor((x - r - x0) / px)), u1 = Math.Min(iw - 1, (int)MathF.Floor((x + r - x0) / px));
            int v0 = Math.Max(0, (int)MathF.Floor((z - r - zSouth) / px)), v1 = Math.Min(ih - 1, (int)MathF.Floor((z + r - zSouth) / px));
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    float dx = Cx(u) - x, dz = Cz(v) - z;
                    if (dx * dx + dz * dz <= r * r) yield return (u, v);
                }
        }
        var keep = new HashSet<(int, int)>();
        foreach (var (x, z, r) in stay.Concat(come)) if (r > 0) foreach (var p in Disc(x, z, r)) keep.Add(p);

        // 1) de onde a árvore saiu: candidatos fechados que nenhuma árvore que fica cobre, de pé (sem parede no corpo)
        var cand = new HashSet<(int U, int V)>();
        foreach (var (x, z, r) in gone)
            if (r > 0)
                foreach (var p in Disc(x, z, r + 0.5f))
                {
                    if (keep.Contains(p) || Get(p.U, p.V)) continue;
                    float cx = Cx(p.U), cz = Cz(p.V);
                    if (height(cx, cz) is not float g) continue;
                    if (walls is not null && walls.Hit(new Vector3(cx, g + 0.5f, cz), new Vector3(cx, g + 1.8f, cz))) continue;
                    cand.Add(p);
                }
        // reabre por alcance: de um vizinho aberto (fora dos candidatos) com degrau até 1 m, espalhando entre os candidatos
        var q = new Queue<(int U, int V)>();
        var reached = new HashSet<(int, int)>();
        (int, int)[] nb = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var p in cand)
        {
            float g = height(Cx(p.U), Cz(p.V)) ?? float.NaN;
            foreach (var (du, dv) in nb)
            {
                int u = p.U + du, v = p.V + dv;
                if (u < 0 || v < 0 || u >= iw || v >= ih || cand.Contains((u, v)) || !Get(u, v)) continue;
                if (height(Cx(u), Cz(v)) is float gn && MathF.Abs(gn - g) <= MoveMapFile.MaxStep) { if (reached.Add(p)) q.Enqueue(p); break; }
            }
        }
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            float g = height(Cx(p.U), Cz(p.V)) ?? float.NaN;
            foreach (var (du, dv) in nb)
            {
                var n = (p.U + du, p.V + dv);
                if (!cand.Contains(n) || reached.Contains(n)) continue;
                if (height(Cx(n.Item1), Cz(n.Item2)) is float gn && MathF.Abs(gn - g) <= MoveMapFile.MaxStep) { reached.Add(n); q.Enqueue(n); }
            }
        }
        foreach (var (u, v) in reached) Set(u, v, true);
        // 2) árvores novas: o círculo fecha
        foreach (var (x, z, r) in come) if (r > 0) foreach (var (u, v) in Disc(x, z, r)) Set(u, v, false);
        return (d, changed);
    }
}
