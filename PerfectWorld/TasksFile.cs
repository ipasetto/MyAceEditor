using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma quest de topo do tasks.data (cabeçalho: ID + nome) — usado quando o esquema completo não existe.</summary>
public sealed class QuestEntry
{
    public int Offset { get; init; }
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string OriginalName { get; init; } = "";
    public bool Changed => Name != OriginalName;
}

/// <summary>
/// tasks.data (PW-KB §S21): u32 magic 0x93858361 | u32 versão | u32 n | n×u32 offset | corpos.
/// Versão 93: esquema completo (<see cref="TaskSchema93"/>) — árvore de quests, campos, textos e diálogos,
/// gravação reconstruindo a tabela de offsets. Outras versões: só ID/nome das quests de topo.
/// Ver ENGINE-KB §E3c.
/// </summary>
public sealed class TasksFile
{
    public const uint Magic = 0x93858361;
    const int NameChars = 30;

    public byte[] Data { get; private set; }
    public int Version { get; }
    public List<QuestEntry> Quests { get; } = new();
    public List<QuestNode>? Roots { get; }
    public string? SchemaError { get; }
    public bool FullSchema => Roots is not null;

    public TasksFile(string path) : this(File.ReadAllBytes(path)) { }

    public TasksFile(byte[] data)
    {
        Data = data;
        if (data.Length < 12 || BitConverter.ToUInt32(data, 0) is not (Magic or 0))
            throw new InvalidDataException("Não é um tasks.data (assinatura inválida).");
        Version = BitConverter.ToInt32(data, 4);
        int n = BitConverter.ToInt32(data, 8);
        if (n < 0 || 12 + n * 4 > data.Length) throw new InvalidDataException($"Contagem de quests inválida ({n}).");
        var offsets = new int[n];
        for (int i = 0; i < n; i++) offsets[i] = BitConverter.ToInt32(data, 12 + i * 4);

        for (int i = 0; i < n; i++)
        {
            int off = offsets[i];
            if (off < 0 || off + 4 + NameChars * 2 > data.Length) continue;
            int id = BitConverter.ToInt32(data, off);
            var name = TaskSchema93.Xor(data, off + 4, NameChars, id);
            Quests.Add(new QuestEntry { Offset = off, Id = id, Name = name, OriginalName = name });
        }

        if (Version == 93)
        {
            try
            {
                var roots = new List<QuestNode>(n);
                for (int i = 0; i < n; i++)
                {
                    var schema = new TaskSchema93(data);
                    var q = schema.Read(offsets[i]);
                    int end = schema.Position, expected = i + 1 < n ? offsets[i + 1] : data.Length;
                    if (end != expected)
                        throw new InvalidDataException($"quest {q.Id} terminou em {end:N0}, esperado {expected:N0}");
                    roots.Add(q);
                }
                Roots = roots;
            }
            catch (Exception ex) { SchemaError = ex.Message; }
        }
    }

    public IEnumerable<QuestNode> AllNodes() => Roots is null ? Enumerable.Empty<QuestNode>() : Roots.SelectMany(r => new[] { r }.Concat(r.Descendants()));

    /// <summary>Bytes do arquivo com as alterações aplicadas.</summary>
    public byte[] Build()
    {
        if (Roots is null)
        {
            var outBytes = (byte[])Data.Clone();
            foreach (var q in Quests.Where(q => q.Changed))
                for (int i = 0; i < NameChars; i++)
                    BitConverter.GetBytes((ushort)((i < q.Name.Length && i < NameChars - 1 ? q.Name[i] : '\0') ^ (ushort)q.Id)).CopyTo(outBytes, q.Offset + 4 + i * 2);
            return outBytes;
        }
        using var ms = new MemoryStream(Data.Length + 4096);
        var w = new BinaryWriter(ms);
        w.Write(BitConverter.ToUInt32(Data, 0)); w.Write(Version); w.Write(Roots.Count);
        long table = ms.Position;
        for (int i = 0; i < Roots.Count; i++) w.Write(0);
        var offs = new int[Roots.Count];
        for (int i = 0; i < Roots.Count; i++) { offs[i] = (int)ms.Position; TaskSchema93.Write(w, Roots[i]); }
        ms.Position = table;
        foreach (var o in offs) w.Write(o);
        return ms.ToArray();
    }

    /// <summary>Backup com data/hora + escrita atômica (mesma regra do elements.data).</summary>
    public string? Save(string path)
    {
        var bytes = Build();
        string? backup = null;
        if (File.Exists(path))
        {
            backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backup, overwrite: false);
        }
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        Data = bytes;
        return backup;
    }

    public const int MaxNameLength = NameChars - 1;

    /// <summary>Separa o código de cor do PW ("^RRGGBB") do texto.</summary>
    public static (string Text, System.Drawing.Color? Color) SplitColor(string s)
    {
        var m = Regex.Match(s, "^\\^([0-9a-fA-F]{6})");
        if (!m.Success) return (s, null);
        var v = Convert.ToInt32(m.Groups[1].Value, 16);
        return (s[7..], System.Drawing.Color.FromArgb(v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF));
    }
}
