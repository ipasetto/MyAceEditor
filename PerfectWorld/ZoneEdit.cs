using Area = MyAceEditor.PerfectWorld.PrecinctFile.Area;

namespace MyAceEditor.PerfectWorld;

/// <summary>O que a ferramenta Zonas faz com uma área, sem tela. As listas são copiadas: o estado de antes fica intacto para o Ctrl+Z.</summary>
public static class ZoneEdit
{
    /// <summary>Canto na nova posição; ele para na borda do mapa (halfW, halfH = metade do mapa).</summary>
    public static Area MoveCorner(Area a, int i, float x, float z, float halfW, float halfH)
    {
        var points = new List<(float X, float Z)>(a.Points) { [i] = (Math.Clamp(x, -halfW, halfW), Math.Clamp(z, -halfH, halfH)) };
        return a with { Points = points };
    }

    public static Area AddCorner(Area a, int after, float x, float y, float z)
    {
        var points = new List<(float X, float Z)>(a.Points);
        var heights = new List<float>(a.PointY);
        points.Insert(after + 1, (x, z));
        if (after + 1 <= heights.Count) heights.Insert(after + 1, y);
        return a with { Points = points, PointY = heights };
    }

    public static Area RemoveCorner(Area a, int i)
    {
        var points = new List<(float X, float Z)>(a.Points);
        var heights = new List<float>(a.PointY);
        points.RemoveAt(i);
        if (i < heights.Count) heights.RemoveAt(i);
        return a with { Points = points, PointY = heights };
    }

    /// <summary>Por cima: prioridade 0 e fim do arquivo, porque no empate ganha a que vem depois. Devolve o novo índice.</summary>
    public static int OnTop(PrecinctFile f, int index)
    {
        var a = f.Areas[index] with { Priority = 0 };
        f.Areas.RemoveAt(index);
        f.Areas.Add(a);
        return f.Areas.Count - 1;
    }

    /// <summary>Por baixo: o maior número das vizinhas mais 1, porque o menor número ganha.</summary>
    public static void Below(PrecinctFile f, int index, IEnumerable<int> neighbors)
    {
        var list = neighbors.ToList();
        if (list.Count > 0) f.Areas[index] = f.Areas[index] with { Priority = list.Max(n => f.Areas[n].Priority) + 1 };
    }

    /// <summary>Índices das partes da zona: mesmo ID e mesma origem. ID 0 não forma zona.</summary>
    public static List<int> Parts(PrecinctFile f, int index)
    {
        var a = f.Areas[index];
        if (a.Id == 0) return [index];
        return [.. Enumerable.Range(0, f.Areas.Count).Where(i => f.Areas[i].Id == a.Id && f.Areas[i].SrcInstance == a.SrcInstance)];
    }

    /// <summary>Muda todas as partes da zona de uma vez.</summary>
    public static void SetForParts(PrecinctFile f, int index, Func<Area, Area> change)
    {
        foreach (int i in Parts(f, index)) f.Areas[i] = change(f.Areas[i]);
    }

    /// <summary>Áreas da mesma instância que encostam nesta. Partes da mesma zona não contam.</summary>
    public static List<int> Neighbors(PrecinctFile f, int index)
    {
        var a = f.Areas[index];
        var mine = Parts(f, index);
        return [.. Enumerable.Range(0, f.Areas.Count).Where(i => !mine.Contains(i) && f.Areas[i].SrcInstance == a.SrcInstance && Touch(a, f.Areas[i]))];
    }

    static bool Touch(Area a, Area b)
    {
        if (a.Points.Count < 3 || b.Points.Count < 3) return false;
        if (a.Points.Any(p => b.Contains(p.X, p.Z)) || b.Points.Any(p => a.Contains(p.X, p.Z))) return true;
        for (int i = 0; i < a.Points.Count; i++)
            for (int k = 0; k < b.Points.Count; k++)
                if (Cross(a.Points[i], a.Points[(i + 1) % a.Points.Count], b.Points[k], b.Points[(k + 1) % b.Points.Count])) return true;
        return false;
    }

    static bool Cross((float X, float Z) a, (float X, float Z) b, (float X, float Z) c, (float X, float Z) d)
    {
        static double Side((float X, float Z) p, (float X, float Z) q, (float X, float Z) r) => ((double)q.X - p.X) * ((double)r.Z - p.Z) - ((double)q.Z - p.Z) * ((double)r.X - p.X);
        return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
    }

    /// <summary>Nome da zona; quem era ID 0 ganha ID, porque ID 0 nunca mostra nome.</summary>
    public static void Rename(PrecinctFile f, int index, string name, bool allParts)
    {
        if (f.Areas[index].Id == 0 && name.Trim().Length > 0) f.Areas[index] = f.Areas[index] with { Id = ZoneRules.FreeId(f) };
        if (allParts) SetForParts(f, index, a => a with { Name = name });
        else f.Areas[index] = f.Areas[index] with { Name = name };
    }

    /// <summary>
    /// Zona nova no fim do arquivo, por cima do que havia ali: ID livre, origem da instância, prioridade 0. Música, cidade e território
    /// vêm da zona de baixo, e a pessoa só muda o que quiser. Devolve o índice.
    /// </summary>
    public static int New(PrecinctFile f, string name, IEnumerable<(float X, float Y, float Z)> corners, int instance)
    {
        var list = corners.ToList();
        var center = (X: list.Average(p => p.X), Z: list.Average(p => p.Z));
        var below = f.At((float)center.X, (float)center.Z, instance);
        f.Areas.Add(new Area(name, 0, below?.Interval ?? 0, below?.Loop ?? 1, below?.Sound ?? "", below?.NightSound ?? "", [.. below?.Music ?? []], [.. list.Select(p => (p.X, p.Z))], below?.Domain ?? 0)
            { Id = ZoneRules.FreeId(f), SrcInstance = instance, DstInstance = below?.DstInstance ?? instance, City = below?.City ?? default, PkProtected = below?.PkProtected ?? false, PointY = [.. list.Select(p => p.Y)] });
        return f.Areas.Count - 1;
    }

    /// <summary>Mais uma parte da mesma zona, com o que a zona já tem. Devolve o índice.</summary>
    public static int NewPart(PrecinctFile f, int index, IEnumerable<(float X, float Y, float Z)> corners)
    {
        var list = corners.ToList();
        f.Areas.Add(f.Areas[index] with { Points = [.. list.Select(p => (p.X, p.Z))], PointY = [.. list.Select(p => p.Y)], Marks = [] });
        return f.Areas.Count - 1;
    }
}
