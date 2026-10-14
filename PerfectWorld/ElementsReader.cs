using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Leitor do formato binário elements.data (engine Angelica / Perfect World).
///
/// Formato confirmado empiricamente no elements.data v0x3000003C (PW 1.4.4):
///   Header:  uint32 version · uint32 signature (0x4F614E5B = "[NaO")
///   Depois:  uma sequência de BLOCOS. Cada bloco = uint32 count + count registros.
///            O layout de cada registro é fixo por tipo de bloco (ver ElementsSchema).
///
/// Esta é a fundação: lê o header e qualquer bloco cujo schema seja conhecido.
/// Cobrir todos os ~60 blocos do elements.data é incremental — basta adicionar
/// definições em ElementsSchema conforme forem mapeadas.
/// </summary>
public sealed class ElementsReader : IDisposable
{
    public const uint ExpectedSignature = 0x4F614E5B;

    private readonly BinaryReader _r;
    public uint Version { get; private set; }
    public uint Signature { get; private set; }

    public ElementsReader(string path)
    {
        _r = new BinaryReader(File.OpenRead(path));
        Version = _r.ReadUInt32();
        Signature = _r.ReadUInt32();
    }

    public bool SignatureValid => Signature == ExpectedSignature;
    public long Position => _r.BaseStream.Position;
    public long Length => _r.BaseStream.Length;

    /// <summary>Lê um bloco (uint count + count registros) conforme o schema dado.</summary>
    public PwBlockData ReadBlock(PwBlock block)
    {
        uint count = _r.ReadUInt32();
        var records = new List<Dictionary<string, object>>((int)Math.Min(count, 1_000_000));
        for (uint i = 0; i < count; i++)
            records.Add(ReadRecord(block));
        return new PwBlockData(block.Name, count, records);
    }

    /// <summary>Pula um bloco sem materializar os registros (usa o tamanho fixo do schema).</summary>
    public (uint count, long bytes) SkipBlock(PwBlock block)
    {
        uint count = _r.ReadUInt32();
        long bytes = (long)count * block.RecordSize;
        _r.BaseStream.Seek(bytes, SeekOrigin.Current);
        return (count, bytes);
    }

    private Dictionary<string, object> ReadRecord(PwBlock block)
    {
        var rec = new Dictionary<string, object>(block.Fields.Count);
        foreach (var f in block.Fields)
        {
            object v = f.Type switch
            {
                PwType.Int32 => _r.ReadInt32(),
                PwType.UInt32 => _r.ReadUInt32(),
                PwType.Float => _r.ReadSingle(),
                PwType.WStr => ReadWString(f.Count),
                PwType.Bytes => _r.ReadBytes(f.Count),
                _ => throw new InvalidOperationException($"Tipo desconhecido: {f.Type}")
            };
            rec[f.Name] = v;
        }
        return rec;
    }

    /// <summary>Lê uma string UTF-16 de tamanho fixo (count caracteres), cortando no primeiro NUL.</summary>
    private string ReadWString(int chars)
    {
        var bytes = _r.ReadBytes(chars * 2);
        int end = 0;
        while (end < bytes.Length && !(bytes[end] == 0 && bytes[end + 1] == 0)) end += 2;
        return Encoding.Unicode.GetString(bytes, 0, end);
    }

    /// <summary>
    /// Percorre TODOS os blocos usando a tabela de tamanhos recuperada por RE.
    /// Cada registro é lido como { int id; wchar name[32]; bytes resto } — suficiente
    /// para listar/identificar tudo; os demais campos ficam em Raw para refinamento.
    /// </summary>
    public IReadOnlyList<PwWalkedBlock> WalkAll(IReadOnlyList<int> recordSizes)
    {
        var blocks = new List<PwWalkedBlock>(recordSizes.Count);
        foreach (var size in recordSizes)
        {
            if (Position + 4 > Length) break;
            uint count = _r.ReadUInt32();
            bool truncated = false;
            var recs = new List<PwGenericRecord>((int)Math.Min(count, 1_000_000));
            for (uint i = 0; i < count; i++)
            {
                if (Position + size > Length) { truncated = true; break; }
                var raw = _r.ReadBytes(size);
                int id = BitConverter.ToInt32(raw, 0);
                int nameChars = Math.Min(32, (size - 4) / 2);
                string name = NameFrom(raw, 4, nameChars);
                recs.Add(new PwGenericRecord(id, name, raw));
            }
            blocks.Add(new PwWalkedBlock(size, count, recs));
            if (truncated) break;
        }
        return blocks;
    }

    private static string NameFrom(byte[] buf, int offset, int chars)
    {
        int end = offset;
        int limit = offset + chars * 2;
        while (end < limit && !(buf[end] == 0 && buf[end + 1] == 0)) end += 2;
        return Encoding.Unicode.GetString(buf, offset, end - offset);
    }

    public void Dispose() => _r.Dispose();
}

public sealed record PwBlockData(string Name, uint Count, IReadOnlyList<Dictionary<string, object>> Records);
public sealed record PwGenericRecord(int Id, string Name, byte[] Raw);
public sealed record PwWalkedBlock(int RecordSize, uint Count, IReadOnlyList<PwGenericRecord> Records);
