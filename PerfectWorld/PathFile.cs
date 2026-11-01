namespace MyAceEditor.PerfectWorld;

/// <summary>
/// path.sev do servidor (caminhos de patrulha; o campo Caminho do npcgen aponta para o ID): CSevBezierMan::Load /
/// CSevBezier::Load (source_server_153 cgame/gs/template/sevbezier.cpp, PW-KB §S22). Cabeçalho {versão, nº}; cada caminho
/// = curvas de Bézier: pontos (posição + direção) e segmentos (2 âncoras, ponto inicial/final, comprimento). Gravação
/// campo a campo, idêntica ao original (--pathsev).
/// </summary>
public sealed class PathFile
{
    public int Version;
    public List<SevPath> Paths { get; } = new();

    public static PathFile Read(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data, false));
        var f = new PathFile { Version = r.ReadInt32() };
        int n = r.ReadInt32();
        if (n < 0 || n > 1_000_000) throw new InvalidDataException($"path.sev: {n} caminhos");
        for (int i = 0; i < n; i++) f.Paths.Add(SevPath.Read(r));
        if (r.BaseStream.Position != data.Length) throw new InvalidDataException($"path.sev: sobraram {data.Length - r.BaseStream.Position} bytes");
        return f;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Version); w.Write(Paths.Count);
        foreach (var p in Paths) p.Write(w);
        w.Flush();
        return ms.ToArray();
    }
}

public sealed class SevPath
{
    public int Version, Id, GlobalId = -1, NextGlobalId = -1;
    public List<(Vec3 Pos, Vec3 Dir)> Points { get; } = new();
    public List<SevSegment> Segments { get; } = new();

    internal static SevPath Read(BinaryReader r)
    {
        var p = new SevPath { Version = r.ReadInt32(), Id = r.ReadInt32() };
        if (p.Version >= 2) { p.GlobalId = r.ReadInt32(); p.NextGlobalId = r.ReadInt32(); }
        int np = r.ReadInt32();
        if (np < 0 || np > 100_000) throw new InvalidDataException($"path.sev: caminho {p.Id} com {np} pontos");
        for (int i = 0; i < np; i++) p.Points.Add((Vec3.Read(r), Vec3.Read(r)));
        int ns = r.ReadInt32();
        if (ns < 0 || ns > 100_000) throw new InvalidDataException($"path.sev: caminho {p.Id} com {ns} segmentos");
        for (int i = 0; i < ns; i++) p.Segments.Add(new SevSegment(Vec3.Read(r), Vec3.Read(r), r.ReadInt32(), r.ReadInt32(), r.ReadSingle()));
        return p;
    }

    internal void Write(BinaryWriter w)
    {
        w.Write(Version); w.Write(Id);
        if (Version >= 2) { w.Write(GlobalId); w.Write(NextGlobalId); }
        w.Write(Points.Count);
        foreach (var (pos, dir) in Points) { pos.Write(w); dir.Write(w); }
        w.Write(Segments.Count);
        foreach (var s in Segments) { s.AnchorHead.Write(w); s.AnchorTail.Write(w); w.Write(s.Head); w.Write(s.Tail); w.Write(s.Length); }
    }

    /// <summary>Pontos do caminho para desenhar: cada segmento é uma Bézier cúbica (ponto inicial, âncoras, ponto final).</summary>
    public List<Vec3> Sample(int perSegment = 12)
    {
        var res = new List<Vec3>();
        foreach (var s in Segments)
        {
            if (s.Head < 0 || s.Head >= Points.Count || s.Tail < 0 || s.Tail >= Points.Count) continue;
            Vec3 a = Points[s.Head].Pos, b = s.AnchorHead, c = s.AnchorTail, d = Points[s.Tail].Pos;
            for (int i = res.Count == 0 ? 0 : 1; i <= perSegment; i++)
            {
                float t = i / (float)perSegment, u = 1 - t;
                float k0 = u * u * u, k1 = 3 * u * u * t, k2 = 3 * u * t * t, k3 = t * t * t;
                res.Add(new Vec3(k0 * a.X + k1 * b.X + k2 * c.X + k3 * d.X, k0 * a.Y + k1 * b.Y + k2 * c.Y + k3 * d.Y, k0 * a.Z + k1 * b.Z + k2 * c.Z + k3 * d.Z));
            }
        }
        if (res.Count == 0) res.AddRange(Points.Select(p => p.Pos));   // sem segmentos: liga os pontos
        return res;
    }
}

public sealed record SevSegment(Vec3 AnchorHead, Vec3 AnchorTail, int Head, int Tail, float Length);
