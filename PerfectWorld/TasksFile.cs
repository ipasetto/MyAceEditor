using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma quest de topo do tasks.data (só cabeçalho: ID + nome).</summary>
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
/// Cada corpo começa com u32 ID + nome de 30 caracteres UTF-16 (60 bytes) embaralhado com XOR do
/// próprio ID (ushort). O restante do corpo (bloco fixo por versão, textos, diálogos, subquests)
/// ainda não é interpretado — é preservado byte a byte. Ver ENGINE-KB §E3c.
/// </summary>
public sealed class TasksFile
{
    public const uint Magic = 0x93858361;
    const int NameChars = 30;

    public byte[] Data { get; private set; }
    public int Version { get; }
    public List<QuestEntry> Quests { get; } = new();

    public TasksFile(string path) : this(File.ReadAllBytes(path)) { }

    public TasksFile(byte[] data)
    {
        Data = data;
        if (data.Length < 12 || BitConverter.ToUInt32(data, 0) is not (Magic or 0))
            throw new InvalidDataException("Não é um tasks.data (assinatura inválida).");
        Version = BitConverter.ToInt32(data, 4);
        int n = BitConverter.ToInt32(data, 8);
        if (n < 0 || 12 + n * 4 > data.Length) throw new InvalidDataException($"Contagem de quests inválida ({n}).");
        for (int i = 0; i < n; i++)
        {
            int off = BitConverter.ToInt32(data, 12 + i * 4);
            if (off < 0 || off + 4 + NameChars * 2 > data.Length) continue;
            int id = BitConverter.ToInt32(data, off);
            var name = DecodeName(data, off + 4, id);
            Quests.Add(new QuestEntry { Offset = off, Id = id, Name = name, OriginalName = name });
        }
    }

    static string DecodeName(byte[] d, int p, int id)
    {
        var sb = new StringBuilder(NameChars);
        ushort key = unchecked((ushort)id);
        for (int i = 0; i < NameChars; i++)
        {
            char c = (char)(BitConverter.ToUInt16(d, p + i * 2) ^ key);
            if (c == '\0') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static void EncodeName(byte[] d, int p, int id, string name)
    {
        ushort key = unchecked((ushort)id);
        for (int i = 0; i < NameChars; i++)
        {
            ushort c = i < name.Length && i < NameChars - 1 ? name[i] : (ushort)0;  // sempre termina em \0
            BitConverter.GetBytes((ushort)(c ^ key)).CopyTo(d, p + i * 2);
        }
    }

    /// <summary>Bytes do arquivo com os nomes alterados aplicados (o resto fica idêntico).</summary>
    public byte[] Build()
    {
        var outBytes = (byte[])Data.Clone();
        foreach (var q in Quests.Where(q => q.Changed))
            EncodeName(outBytes, q.Offset + 4, q.Id, q.Name);
        return outBytes;
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
