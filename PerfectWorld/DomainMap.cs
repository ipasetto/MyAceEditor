using System.Drawing;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Território do mapa de guerra. Vértices e centro são pixels da imagem GuildMap.dds, não coordenadas do mundo.</summary>
public sealed class DomainArea
{
    public string Name = "";
    public byte[] NameRaw = [];
    public int Id, Type, Reward;
    public Point Center;
    public List<Point> Vertices = [];
    public List<int> Faces = [];
    public List<int> Neighbors = [];

    public DomainArea Copy() => new() { Name = Name, NameRaw = NameRaw, Id = Id, Type = Type, Reward = Reward, Center = Center, Vertices = [.. Vertices], Faces = [.. Faces], Neighbors = [.. Neighbors] };

    public bool Contains(Point p)
    {
        bool inside = false;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            var (a, b) = (Vertices[i], Vertices[j]);
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (float)(p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}

/// <summary>
/// data\domain.data do cliente (PW-KB §S6): ["MOXB"] i32 n; por território: nome 16 x u16, id, tipo, prêmio, centro, vértices, triângulos,
/// vizinhos. Depois os horários de batalha, que ficam como estão. O gdeliveryd lê o par domain.sev (id, tipo, prêmio, vizinhos e horários).
/// </summary>
public sealed class DomainMap
{
    public const int NameChars = 16;
    byte[] _head = [], _tail = [];
    public List<DomainArea> Areas { get; private set; } = [];

    public static DomainMap Parse(byte[] b)
    {
        var f = new DomainMap();
        int p = b.Length >= 4 && b[0] == 'M' && b[1] == 'O' && b[2] == 'X' && b[3] == 'B' ? 4 : 0;
        f._head = b[..p];
        int I() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
        int n = I();
        if (n < 0 || n > 10000) throw new InvalidDataException("domain.data");
        for (int i = 0; i < n; i++)
        {
            var a = new DomainArea { NameRaw = b[p..(p + NameChars * 2)] };
            a.Name = Encoding.Unicode.GetString(a.NameRaw).Split('\0')[0];
            p += NameChars * 2;
            a.Id = I(); a.Type = I(); a.Reward = I();
            a.Center = new Point(I(), I());
            for (int v = I(); v > 0; v--) a.Vertices.Add(new Point(I(), I()));
            for (int t = I() * 3; t > 0; t--) a.Faces.Add(I());
            for (int v = I(); v > 0; v--) a.Neighbors.Add(I());
            f.Areas.Add(a);
        }
        f._tail = b[p..];
        return f;
    }

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(_head);
        w.Write(Areas.Count);
        foreach (var a in Areas)
        {
            w.Write(NameBytes(a));
            w.Write(a.Id); w.Write(a.Type); w.Write(a.Reward); w.Write(a.Center.X); w.Write(a.Center.Y);
            w.Write(a.Vertices.Count);
            foreach (var v in a.Vertices) { w.Write(v.X); w.Write(v.Y); }
            w.Write(a.Faces.Count / 3);
            foreach (int t in a.Faces) w.Write(t);
            w.Write(a.Neighbors.Count);
            foreach (int v in a.Neighbors) w.Write(v);
        }
        w.Write(_tail);
        return ms.ToArray();
    }

    static byte[] NameBytes(DomainArea a)
    {
        if (a.NameRaw.Length == NameChars * 2 && Encoding.Unicode.GetString(a.NameRaw).Split('\0')[0] == a.Name) return a.NameRaw;
        var o = new byte[NameChars * 2];
        Encoding.Unicode.GetBytes(a.Name, 0, Math.Min(a.Name.Length, NameChars - 1), o, 0);
        return o;
    }

    public int NextId() => Areas.Select(a => a.Id).DefaultIfEmpty(0).Max() + 1;

    public List<DomainArea> Snapshot() => Areas.Select(a => a.Copy()).ToList();
    public void Restore(List<DomainArea> s) => Areas = s.Select(a => a.Copy()).ToList();

    /// <summary>Triângulos do polígono, por recorte de orelhas. Vazio se o polígono se cruza.</summary>
    public static List<int> Triangulate(IReadOnlyList<Point> v)
    {
        var o = new List<int>();
        var idx = Enumerable.Range(0, v.Count).ToList();
        long Area2() { long s = 0; for (int i = 0, j = v.Count - 1; i < v.Count; j = i++) s += (long)v[j].X * v[i].Y - (long)v[i].X * v[j].Y; return s; }
        long sign = Math.Sign(Area2());
        if (sign == 0) return o;
        static long Cross(Point a, Point b, Point c) => (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
        for (int guard = v.Count * v.Count; idx.Count > 3 && guard > 0; guard--)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count && !cut; i++)
            {
                int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                if (Math.Sign(Cross(v[a], v[b], v[c])) != sign) continue;
                bool empty = true;
                foreach (int k in idx)
                {
                    if (k == a || k == b || k == c) continue;
                    if (Math.Sign(Cross(v[a], v[b], v[k])) == sign && Math.Sign(Cross(v[b], v[c], v[k])) == sign && Math.Sign(Cross(v[c], v[a], v[k])) == sign) { empty = false; break; }
                }
                if (!empty) continue;
                o.AddRange([a, b, c]);
                idx.RemoveAt(i);
                cut = true;
            }
            if (!cut) return [];
        }
        if (idx.Count == 3) o.AddRange(idx);
        return o;
    }

    /// <summary>Territórios que se encostam: um vértice de um a poucos pixels da borda do outro.</summary>
    public void ComputeNeighbors(int touch = 5)
    {
        static float Dist(Point p, Point a, Point b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            float t = len == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
            float x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return MathF.Sqrt(x * x + y * y);
        }
        bool Touch(DomainArea a, DomainArea b) => a.Vertices.Any(p =>
        {
            for (int i = 0, j = b.Vertices.Count - 1; i < b.Vertices.Count; j = i++)
                if (Dist(p, b.Vertices[i], b.Vertices[j]) <= touch) return true;
            return false;
        });
        foreach (var a in Areas) a.Neighbors = Areas.Where(b => b != a && (Touch(a, b) || Touch(b, a))).Select(b => b.Id).ToList();
    }
}

/// <summary>domain.sev do gdeliveryd: i32 n; por território id, tipo, prêmio, vizinhos; depois os horários, que ficam como estão.</summary>
public sealed class DomainServer
{
    public sealed record Entry(int Id, int Type, int Reward, List<int> Neighbors);
    public List<Entry> Entries { get; } = [];
    byte[] _tail = [];

    public static DomainServer Parse(byte[] b)
    {
        var f = new DomainServer();
        int p = 0;
        int I() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
        int n = I();
        if (n < 0 || n > 10000) throw new InvalidDataException("domain.sev");
        for (int i = 0; i < n; i++)
        {
            int id = I(), type = I(), reward = I();
            var nb = new List<int>();
            for (int k = I(); k > 0; k--) nb.Add(I());
            f.Entries.Add(new Entry(id, type, reward, nb));
        }
        f._tail = b[p..];
        return f;
    }

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Entries.Count);
        foreach (var e in Entries)
        {
            w.Write(e.Id); w.Write(e.Type); w.Write(e.Reward); w.Write(e.Neighbors.Count);
            foreach (int v in e.Neighbors) w.Write(v);
        }
        w.Write(_tail);
        return ms.ToArray();
    }

    /// <summary>Deixa o servidor igual ao mapa do cliente, na mesma ordem.</summary>
    public void Match(DomainMap map)
    {
        Entries.Clear();
        Entries.AddRange(map.Areas.Select(a => new Entry(a.Id, a.Type, a.Reward, [.. a.Neighbors])));
    }
}
