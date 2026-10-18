using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Name é a chave única de edição; SourceName guarda o nome original do campo no CFG.</summary>
public sealed record CfgField(string Name, string Type, int Size, string? OriginalName = null)
{
    public string SourceName => OriginalName ?? Name;
}

public sealed class CfgBlock
{
    public int Number;
    public string NumberText = "";
    public string Name = "";
    public string Offset = "0";
    public bool Raw;
    public List<CfgField> Fields = new();
    public int RecordSize => checked(Fields.Sum(f => f.Size));
}

/// <summary>Lê CFG da comunidade; a ordem é a do arquivo, nunca reordenar pelo número exibido.</summary>
public sealed class ElementsCfg
{
    public string SourcePath { get; private set; } = "";
    public int DeclaredBlockCount { get; private set; }
    public int ConversationListIndex { get; private set; }
    public List<CfgBlock> Blocks = new();
    public List<string> Warnings { get; } = new();
    public string SchemaKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Blocks.Select(b => $"{b.Name}|{b.Offset}|{b.Raw}|" +
            string.Join(";", b.Fields.Select(f => $"{f.Name}:{f.Type}")))))));

    public static int TypeSize(string type)
    {
        string t = type.Trim().ToLowerInvariant();
        int colon = t.IndexOf(':');
        if (colon > 0 && t[..colon] is "wstring" or "string" or "byte" or "bytes")
            return int.TryParse(t[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && n > 0 && n <= 16 * 1024 * 1024 && (t[..colon] != "wstring" || n % 2 == 0) ? n : -1;
        return t switch
        {
            "int32" or "uint32" or "dword" or "int" or "uint" or "float" => 4,
            "int16" or "uint16" or "word" or "short" or "ushort" => 2,
            "int8" or "uint8" or "byte" or "sbyte" or "char" or "bool" => 1,
            "int64" or "uint64" or "long" or "ulong" or "double" => 8,
            _ => -1,
        };
    }

    public static ElementsCfg Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        var lines = text.Replace("\r", "").Split('\n')
            .Select((value, index) => (Text: value.Trim(), Line: index + 1))
            .Where(x => x.Text.Length > 0 && !x.Text.StartsWith('#') && !x.Text.StartsWith("//"))
            .ToList();
        if (lines.Count < 2 || !int.TryParse(lines[0].Text, out int count) || count < 1 || count > 10000
            || !int.TryParse(lines[1].Text, out int talkIndex))
            throw new InvalidDataException($"{Path.GetFileName(path)}: cabeçalho de CFG inválido.");
        var cfg = new ElementsCfg { SourcePath = Path.GetFullPath(path), DeclaredBlockCount = count, ConversationListIndex = talkIndex };
        var names = new HashSet<string>(StringComparer.Ordinal);
        var header = new Regex(@"^(\d+)(?:_(\d+))?\s*-\s*(.+)$", RegexOptions.CultureInvariant);
        for (int i = 2; i < lines.Count; i += 4)
        {
            var m = header.Match(lines[i].Text);
            if (!m.Success || i + 3 >= lines.Count)
                throw new InvalidDataException($"{Path.GetFileName(path)}, linha {lines[i].Line}: definição de bloco incompleta.");
            var block = new CfgBlock { Number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                NumberText = m.Groups[1].Value + (m.Groups[2].Success ? "_" + m.Groups[2].Value : ""),
                Name = m.Groups[3].Value.Trim(), Offset = lines[i + 1].Text.ToUpperInvariant() };
            if (!names.Add(block.Name)) throw new InvalidDataException($"Nome de bloco duplicado no CFG: {block.Name}");
            if (block.Offset != "AUTO" && (!int.TryParse(block.Offset, out int offset) || offset < 0 || offset > 16 * 1024 * 1024))
                throw new InvalidDataException($"{block.Name}: offset inválido ({block.Offset}).");
            block.Raw = lines[i + 2].Text.Equals("RAW", StringComparison.OrdinalIgnoreCase);
            if (block.Raw)
            {
                if (!lines[i + 3].Text.Equals("byte:AUTO", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{block.Name}: definição RAW não suportada.");
            }
            else
            {
                string[] fields = lines[i + 2].Text.TrimEnd(';').Split(';');
                string[] types = lines[i + 3].Text.TrimEnd(';').Split(';');
                if (fields.Length != types.Length)
                    cfg.Warnings.Add($"{block.Name}: {fields.Length} nomes de campo e {types.Length} tipos; lido só pelos tipos declarados, sem adivinhar os ausentes.");
                var used = new HashSet<string>(StringComparer.Ordinal);
                for (int k = 0; k < types.Length; k++)
                {
                    string type = types[k].Trim().ToLowerInvariant();
                    int size = TypeSize(type);
                    if (size < 0) throw new InvalidDataException($"{block.Name} campo {k + 1}: tipo não suportado \"{type}\".");
                    string source = k < fields.Length ? fields[k].Trim() : $"field_{k + 1}";
                    if (source.Length == 0) source = $"field_{k + 1}";
                    string key = source;
                    int suffix = 2;
                    while (!used.Add(key)) key = $"{source}__{suffix++}";
                    block.Fields.Add(new CfgField(key, type, size, source));
                }
                if (block.RecordSize <= 0 || block.RecordSize > 16 * 1024 * 1024)
                    throw new InvalidDataException($"{block.Name}: tamanho de registro inválido.");
            }
            cfg.Blocks.Add(block);
        }
        if (cfg.Blocks.Count != count) throw new InvalidDataException($"CFG declara {count} blocos, mas tem {cfg.Blocks.Count}.");
        if (talkIndex < 0 || talkIndex >= count || !cfg.Blocks[talkIndex].Raw)
            throw new InvalidDataException("Índice da lista de diálogos do CFG não bate com o bloco RAW.");
        return cfg;
    }
}
