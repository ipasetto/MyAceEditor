using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Ponto de teleporte. Domain: território do lugar (domain.data), -1 fora de território.</summary>
public sealed record WorldTarget(int Id, string Name, int Tag, float X, float Y, float Z, int Domain)
{
    public int World => Tag;
    public string Position => string.Create(CultureInfo.InvariantCulture, $"{X:0.###},{Y:0.###},{Z:0.###}");
}

/// <summary>
/// Par de arquivos dos pontos de teleporte (CCommon/globaldataman.cpp): cliente maps\world\world_targets.txt (UTF-16,
/// id, nome, tag do mapa, "x,y,z", domínio) e servidor world_targets.sev (i32 n + 24 B: id, tag, x, y, z, domínio). O gs acha
/// o destino só no .sev e o cliente desenha só o que está no .txt: ponto novo entra nos dois, com o mesmo ID.
/// </summary>
public sealed class WorldTargets
{
    public const int MaxNameLength = 31, MaxId = 32767;   // wide_char name[32]; o gs guarda o ponto ativado em 16 bits com sinal (CheckWaypoint)
    static readonly Regex Line = new(@"^\s*(\d+)\s+""([^""]*)""\s+(-?\d+)\s+""([^""]*)""\s+(-?\d+)", RegexOptions.CultureInvariant);

    public List<WorldTarget> Client { get; } = [];
    public List<WorldTarget> Server { get; } = [];
    readonly string _clientPath;
    readonly string? _serverPath;

    WorldTargets(string client, string? server) { _clientPath = client; _serverPath = server; }

    /// <summary>Procura o .txt subindo a partir do elements.data.</summary>
    public static string? FindFile(string elementsPath)
    {
        for (var dir = Path.GetDirectoryName(elementsPath); dir is not null; dir = Path.GetDirectoryName(dir))
            foreach (var c in new[] { Path.Combine(dir, "maps", "world", "world_targets.txt"), Path.Combine(dir, "element", "maps", "world", "world_targets.txt") })
                if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>Só os pontos do cliente, por ID (nome ao lado do campo no editor).</summary>
    public static Dictionary<int, WorldTarget> Load(string file)
    {
        var d = new Dictionary<int, WorldTarget>();
        foreach (var raw in Text(file).Split('\n'))
            if (Parse(raw) is { } p) d[p.Id] = p;
        return d;
    }

    static string Text(string file)
    {
        var b = File.ReadAllBytes(file);
        return b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2)
            : b.Length >= 2 && b[1] == 0 ? Encoding.Unicode.GetString(b) : Encoding.GetEncoding(936).GetString(b);
    }

    public static WorldTargets? Open(string elementDir, string? serverConfigDir)
    {
        var c = Path.Combine(elementDir, "maps", "world", "world_targets.txt");
        if (!File.Exists(c)) return null;
        var s = serverConfigDir is null ? null : Path.Combine(serverConfigDir, "world_targets.sev");
        var t = new WorldTargets(c, s is not null && File.Exists(s) ? s : null);
        foreach (var raw in Text(c).Split('\n'))
            if (Parse(raw) is { } p) t.Client.Add(p);
        if (t._serverPath is { } sp) t.Server.AddRange(ReadServer(File.ReadAllBytes(sp)));
        return t;
    }

    static WorldTarget? Parse(string line)
    {
        if (Line.Match(line) is not { Success: true } m) return null;
        var xyz = m.Groups[4].Value.Split(',');
        if (xyz.Length != 3) return null;
        float F(int i) => float.Parse(xyz[i], CultureInfo.InvariantCulture);
        return new WorldTarget(int.Parse(m.Groups[1].Value), m.Groups[2].Value, int.Parse(m.Groups[3].Value), F(0), F(1), F(2), int.Parse(m.Groups[5].Value));
    }

    public static List<WorldTarget> ReadServer(byte[] b)
    {
        var o = new List<WorldTarget>();
        int n = b.Length >= 4 ? BitConverter.ToInt32(b, 0) : 0;
        if (n < 0 || 4 + n * 24 > b.Length) throw new InvalidDataException("world_targets.sev curto");
        for (int i = 0; i < n; i++)
        {
            int p = 4 + i * 24;
            o.Add(new WorldTarget(BitConverter.ToInt32(b, p), "", BitConverter.ToInt32(b, p + 4), BitConverter.ToSingle(b, p + 8), BitConverter.ToSingle(b, p + 12), BitConverter.ToSingle(b, p + 16), BitConverter.ToInt32(b, p + 20)));
        }
        return o;
    }

    public static byte[] WriteServer(IReadOnlyList<WorldTarget> list)
    {
        var b = new byte[4 + list.Count * 24];
        BitConverter.TryWriteBytes(b, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i]; var s = b.AsSpan(4 + i * 24);
            BitConverter.TryWriteBytes(s, t.Id); BitConverter.TryWriteBytes(s[4..], t.Tag);
            BitConverter.TryWriteBytes(s[8..], t.X); BitConverter.TryWriteBytes(s[12..], t.Y); BitConverter.TryWriteBytes(s[16..], t.Z);
            BitConverter.TryWriteBytes(s[20..], t.Domain);
        }
        return b;
    }

    public static string ClientLine(WorldTarget t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.Id}\t\"{t.Name}\"\t{t.Tag}\t\"{t.X:0.000},{t.Y:0.000},{t.Z:0.000}\"\t{t.Domain}\r\n");

    /// <summary>Texto do .txt com o BOM, na ordem dos pontos.</summary>
    public byte[] ClientBytes()
    {
        var sb = new StringBuilder();
        foreach (var t in Client) sb.Append(ClientLine(t));
        return [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(sb.ToString())];
    }

    public int NextId() => Math.Max(Client.Select(t => t.Id).DefaultIfEmpty(1000).Max(), Server.Select(t => t.Id).DefaultIfEmpty(0).Max()) + 1;

    /// <summary>Por que o nome não serve; null se serve.</summary>
    public static string? BadName(string name) =>
        name.Trim().Length == 0 ? UI.Loc.T("tp.noName", "Dê um nome ao ponto.")
        : name.Length > MaxNameLength ? string.Format(UI.Loc.T("tp.longName", "Nome com mais de {0} letras: o jogo corta."), MaxNameLength)
        : name.Contains('"') ? UI.Loc.T("tp.quote", "O nome não pode ter aspas.") : null;

    public WorldTarget Add(string name, int tag, float x, float y, float z, int domain)
    {
        int id = NextId();
        if (id > MaxId) throw new InvalidOperationException(UI.Loc.T("tp.noId", "Não há mais ID livre para pontos de teleporte."));
        var t = new WorldTarget(id, name.Trim(), tag, x, y, z, domain);
        Client.Add(t);
        if (_serverPath is not null) Server.Add(t);
        return t;
    }

    /// <summary>Troca nome e lugar do ponto nos dois arquivos, sem mudar o ID.</summary>
    public WorldTarget Update(WorldTarget t)
    {
        int c = Client.FindIndex(x => x.Id == t.Id), s = Server.FindIndex(x => x.Id == t.Id);
        if (c >= 0) Client[c] = t;
        if (s >= 0) Server[s] = t;
        return t;
    }

    public void Remove(int id) { Client.RemoveAll(t => t.Id == id); Server.RemoveAll(t => t.Id == id); }

    /// <summary>Foto das duas listas, para o Ctrl+Z.</summary>
    public sealed record Snapshot(List<WorldTarget> Client, List<WorldTarget> Server);

    public Snapshot Take() => new([.. Client], [.. Server]);

    public void Restore(Snapshot s)
    {
        Client.Clear(); Client.AddRange(s.Client);
        Server.Clear(); Server.AddRange(s.Server);
    }

    /// <summary>Pontos que só um dos dois arquivos tem: o gs não acha o destino, ou o cliente não desenha.</summary>
    public (List<int> OnlyClient, List<int> OnlyServer) Differences()
    {
        var c = Client.Select(t => t.Id).ToHashSet(); var s = Server.Select(t => t.Id).ToHashSet();
        return (_serverPath is null ? [] : [.. c.Except(s)], [.. s.Except(c)]);
    }

    public bool HasServerFile => _serverPath is not null;

    /// <summary>
    /// Grava o par tudo ou nada: monta os dois antes, cópia de segurança dos dois, escrita atômica; se o do servidor
    /// falhar, o do cliente volta da cópia (antes ficava o cliente novo com o servidor velho: destinos desencontrados).
    /// </summary>
    public void Save()
    {
        var cb = ClientBytes();
        var sb = _serverPath is null ? null : WriteServer(Server);
        var cbak = AtomicFileSave.Write(_clientPath, cb);   // a gravação atômica já faz a cópia de segurança e devolve onde
        if (_serverPath is null || sb is null) return;
        try { AtomicFileSave.Write(_serverPath, sb); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (cbak is not null) try { AtomicFileSave.Write(_clientPath, BackupStore.Read(cbak)); } catch (Exception rex) when (rex is IOException or UnauthorizedAccessException or InvalidDataException) { AppLog.Error("world_targets: voltar o cliente", rex); }
            throw;
        }
    }
}
