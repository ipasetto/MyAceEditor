namespace MyAceEditor.PerfectWorld;

/// <summary>
/// region.sev do servidor (gamed\config\&lt;mapa&gt;\region.sev), PW-KB §S22: regiões (PK/santuário) e caixas de transporte
/// (portais). Cabeçalho: u32 versão (até 5) | i32 nRegiões | [v&gt;=2] i32 nTransportes | [v&gt;=4] u32 data. Depois,
/// registros misturados, cada um começando com i32 tipo: 0 = região (i32 nPontos + nPontos × 3 floats);
/// outro = transporte: i32 instância destino | [v&gt;=3] i32 instância de origem | [v&gt;=5] i32 nível mínimo |
/// posição, metade do tamanho e destino (3 floats cada). "Instância" = tag do gs.conf (1 = mundo, 105 = a05…).
/// </summary>
public sealed class RegionFile
{
    public sealed record Transport(int TargetTag, int SourceTag, int MinLevel, Vec3 Position, Vec3 HalfSize, Vec3 Target);

    public int Version { get; private set; }
    public int RegionCount { get; private set; }
    public List<Transport> Transports { get; } = new();

    public static RegionFile? Load(string path)
    {
        try { return File.Exists(path) ? Read(File.ReadAllBytes(path)) : null; } catch { return null; }
    }

    public static RegionFile Read(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        var f = new RegionFile { Version = r.ReadInt32() };
        int v = f.Version;
        if (v < 1 || v > 5) throw new InvalidDataException(string.Format(UI.Loc.T("regionf.version", "region.sev: versão {0} desconhecida"), v));
        int nr = r.ReadInt32(), nt = v >= 2 ? r.ReadInt32() : 0;
        if (v >= 4) r.ReadUInt32();
        f.RegionCount = nr;
        int seenR = 0;
        while (seenR < nr || f.Transports.Count < nt)
        {
            if (r.ReadInt32() == 0)
            {
                int n = r.ReadInt32();
                r.BaseStream.Seek(12L * n, SeekOrigin.Current);
                seenR++;
            }
            else
            {
                int target = r.ReadInt32(), source = v >= 3 ? r.ReadInt32() : 1, level = v >= 5 ? r.ReadInt32() : 1;
                f.Transports.Add(new Transport(target, source, level, Vec3.Read(r), Vec3.Read(r), Vec3.Read(r)));
            }
        }
        return f;
    }

    /// <summary>
    /// Onde se chega ao entrar no mapa: a tag do mapa vem dos portais de saída dele (origem) e o ponto, do destino de um
    /// portal de outro mapa que leva a essa tag (o do mundo primeiro). Quem só entra por NPC teleportador não tem esse
    /// portal: vale então o portal de saída do próprio mapa, que costuma ficar junto da entrada. null sem region.sev.
    /// </summary>
    public static Vec3? EntranceOf(string configDir, string map)
    {
        var own = Load(Path.Combine(configDir, map, "region.sev"));
        if (own?.Transports.FirstOrDefault(t => t.SourceTag > 1)?.SourceTag is not int tag) return null;
        var dirs = Directory.GetDirectories(configDir).Where(d => !Path.GetFileName(d).Equals(map, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => Path.GetFileName(d).Equals("world", StringComparison.OrdinalIgnoreCase));
        foreach (var d in dirs)
            if (Load(Path.Combine(d, "region.sev"))?.Transports.FirstOrDefault(t => t.TargetTag == tag) is { } t) return t.Target;
        // saída mais perto do centro (mapas com várias cópias lado a lado, como a a26, têm uma saída em cada)
        return own.Transports.Where(t => t.TargetTag != tag).OrderBy(t => t.Position.X * t.Position.X + t.Position.Z * t.Position.Z).FirstOrDefault()?.Position;
    }
}
