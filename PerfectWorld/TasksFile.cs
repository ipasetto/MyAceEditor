using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma quest de topo do tasks.data (cabeçalho: ID + nome) — usado quando o esquema completo não existe.</summary>
public sealed class QuestEntry
{
    public int Offset { get; init; }
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string OriginalName { get; internal set; } = "";
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
    public TaskIndex Index { get; }
    public bool RoundTripVerified { get; }
    public string? ReadOnlyReason { get; private set; }
    public bool CanWrite => RoundTripVerified && ReadOnlyReason is null;

    public TasksFile(string path, LoadContext? context = null)
        : this(context is null ? File.ReadAllBytes(path) : context.Slice(0, .10).ReadFile(path), context?.Slice(.10, 1)) { }

    public TasksFile(byte[] data, LoadContext? context = null)
    {
        context?.Check();
        Data = data;
        if (data.Length < 12 || BitConverter.ToUInt32(data, 0) is not (Magic or 0))
            throw new InvalidDataException("文件不是有效的 tasks.data（签名无效）。");
        Version = BitConverter.ToInt32(data, 4);
        int n = BitConverter.ToInt32(data, 8);
        if (n < 0 || n > (data.Length - 12) / 4) throw new InvalidDataException($"任务数量无效（{n}）。");
        var offsets = new int[n];
        for (int i = 0; i < n; i++)
        {
            if ((i & 255) == 0) context?.Check();
            offsets[i] = BitConverter.ToInt32(data, 12 + i * 4);
        }

        // 名称模式也不能让任务头覆盖偏移表或其他任务头。
        int headerEnd = 12 + n * 4;
        int[] sortedOffsets = offsets.Order().ToArray();
        for (int i = 0; i < sortedOffsets.Length; i++)
        {
            int off = sortedOffsets[i];
            if (off < headerEnd || off > data.Length - (4 + NameChars * 2)
                || i > 0 && (long)off < (long)sortedOffsets[i - 1] + 4 + NameChars * 2)
                ReadOnlyReason = "任务偏移表包含越界、重复或重叠的任务头，当前只允许查看。";
        }

        for (int i = 0; i < n; i++)
        {
            if ((i & 127) == 0) context?.Report(.1 * i / Math.Max(1, n), "读取任务名称", $"{i:N0}/{n:N0} 个任务");
            int off = offsets[i];
            if (off < headerEnd || off > data.Length - (4 + NameChars * 2)) continue;
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
                    context?.Report(.1 + .82 * i / Math.Max(1, n), "解析任务结构", $"{i:N0}/{n:N0} 个主任务");
                    var schema = new TaskSchema93(data, context?.Token ?? default);
                    var q = schema.Read(offsets[i]);
                    int end = schema.Position, expected = i + 1 < n ? offsets[i + 1] : data.Length;
                    if (end != expected)
                        throw new InvalidDataException($"任务 {q.Id} 结束位置为 {end:N0}，预期为 {expected:N0}");
                    roots.Add(q);
                }
                Roots = roots;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { SchemaError = ex.Message; }
        }
        context?.Check();
        Index = new TaskIndex(Roots, Quests, context?.Slice(.92, .95));
        if (ReadOnlyReason is null)
        {
            try
            {
                RoundTripVerified = BuildCore(context?.Slice(.95, 1)).AsSpan().SequenceEqual(Data);
                if (!RoundTripVerified) ReadOnlyReason = "任务无修改重建与原始文件不一致，当前只允许查看。";
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
            { ReadOnlyReason = "任务写回校验未通过：" + ex.Message; }
        }
        context?.Check();
        context?.Report(1, "任务读取完成", $"{Quests.Count:N0} 个主任务", force: true);
    }

    public IEnumerable<QuestNode> AllNodes() => Index.Nodes;

    /// <summary>Bytes do arquivo com as alterações aplicadas.</summary>
    public byte[] Build(LoadContext? context = null)
    {
        context?.Check();
        if (!CanWrite) throw new InvalidOperationException("当前任务数据为只读，不能保存。" + ReadOnlyReason);
        return BuildCore(context);
    }

    private byte[] BuildCore(LoadContext? context = null)
    {
        context?.Check();
        if (Roots is null)
        {
            var outBytes = (byte[])Data.Clone();
            foreach (var q in Quests.Where(q => q.Changed))
            {
                context?.Check();
                if (q.Name.Length > MaxNameLength) throw new InvalidDataException($"任务 {q.Id} 的名称超过 {MaxNameLength} 个字符。");
                for (int i = 0; i < NameChars; i++)
                    BitConverter.GetBytes((ushort)((i < q.Name.Length && i < NameChars - 1 ? q.Name[i] : '\0') ^ (ushort)q.Id)).CopyTo(outBytes, q.Offset + 4 + i * 2);
            }
            return outBytes;
        }
        using var ms = new MemoryStream(Data.Length + 4096);
        var w = new BinaryWriter(ms);
        w.Write(BitConverter.ToUInt32(Data, 0)); w.Write(Version); w.Write(Roots.Count);
        long table = ms.Position;
        for (int i = 0; i < Roots.Count; i++) w.Write(0);
        var offs = new int[Roots.Count];
        for (int i = 0; i < Roots.Count; i++)
        {
            context?.Check();
            context?.Report(i / (double)Math.Max(1, Roots.Count), "生成任务数据", $"{i:N0}/{Roots.Count:N0} 个主任务");
            offs[i] = (int)ms.Position; TaskSchema93.Write(w, Roots[i]);
        }
        ms.Position = table;
        foreach (var o in offs) w.Write(o);
        return ms.ToArray();
    }

    /// <summary>Backup com data/hora + escrita atômica (mesma regra do elements.data).</summary>
    public string? Save(string path, LoadContext? context = null)
    {
        var bytes = Build(context?.Slice(0, .75));
        string? backup = AtomicFileSave.Write(path, bytes, context?.Slice(.75, 1));
        Data = bytes;
        if (Roots is null)
            foreach (var q in Quests) q.OriginalName = q.Name;
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
