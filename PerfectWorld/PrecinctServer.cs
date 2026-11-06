namespace MyAceEditor.PerfectWorld;

/// <summary>
/// config\&lt;mapa&gt;\precinct.sev, a metade binária das zonas (gs: city_region.cpp): u32 versão, i32 áreas, [v5] u32 carimbo; por área
/// nº de cantos, prioridade, destino, [v4] origem, [v6] território, [v7] bool proteção PK, cidade e cantos. Mesma ordem do precinct.clt.
/// </summary>
public sealed class PrecinctServer
{
    public sealed record ServerArea(int Priority, int Dst, int Src, int Domain, bool Pk, (float X, float Y, float Z) City, List<(float X, float Y, float Z)> Points);

    public uint Version { get; private init; } = 7;
    public uint Stamp { get; set; }
    public List<ServerArea> Areas { get; } = [];

    /// <summary>Lê o arquivo inteiro. Lança InvalidDataException se ele acabar antes da hora ou sobrar coisa.</summary>
    public static PrecinctServer Parse(byte[] d)
    {
        using var r = new BinaryReader(new MemoryStream(d));
        try
        {
            uint version = r.ReadUInt32();
            int count = r.ReadInt32();
            if (count < 0 || version > 7) throw new InvalidDataException("precinct.sev com cabeçalho estranho");
            var f = new PrecinctServer { Version = version, Stamp = version >= 5 ? r.ReadUInt32() : 0 };
            (float, float, float) Point() => (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < count; i++)
            {
                int points = r.ReadInt32(), priority = r.ReadInt32(), dst = r.ReadInt32();
                int src = version >= 4 ? r.ReadInt32() : 0, domain = version >= 6 ? r.ReadInt32() : 0;
                bool pk = version >= 7 && r.ReadByte() != 0;
                var city = Point();
                if (points < 0 || points > d.Length / 12) throw new InvalidDataException("precinct.sev com cantos demais");
                var list = new List<(float, float, float)>(points);
                for (int k = 0; k < points; k++) list.Add(Point());
                f.Areas.Add(new ServerArea(priority, dst, src, domain, pk, city, list));
            }
            if (r.BaseStream.Position != d.Length) throw new InvalidDataException("precinct.sev com sobra no fim");
            return f;
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("precinct.sev curto", ex); }
    }

    public byte[] Render()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void Point((float X, float Y, float Z) p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
        w.Write(Version); w.Write(Areas.Count);
        if (Version >= 5) w.Write(Stamp);
        foreach (var a in Areas)
        {
            w.Write(a.Points.Count); w.Write(a.Priority); w.Write(a.Dst);
            if (Version >= 4) w.Write(a.Src);
            if (Version >= 6) w.Write(a.Domain);
            if (Version >= 7) w.Write((byte)(a.Pk ? 1 : 0));
            Point(a.City);
            foreach (var p in a.Points) Point(p);
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>O .sev do jeito que o editor grava: versão 7, mesma ordem do .clt, com as áreas de ID 0.</summary>
    public static PrecinctServer From(PrecinctFile clt, uint stamp)
    {
        var f = new PrecinctServer { Version = 7, Stamp = stamp };
        foreach (var a in clt.Areas)
            f.Areas.Add(new ServerArea(a.Priority, a.DstInstance, a.SrcInstance, a.Domain, a.PkProtected, a.City,
                [.. a.Points.Select((p, i) => (p.X, i < a.PointY.Count ? a.PointY[i] : 0f, p.Z))]));
        return f;
    }
}
