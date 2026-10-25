using System.IO.Compression;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma entrada da tabela de arquivos do .pck.</summary>
public sealed record PckEntry(string Path, long Offset, int Size, int CSize);

/// <summary>
/// Leitor nativo de pacotes Angelica (.pck + .pkx). Formato: ENGINE-KB §E4 / PWKB §S3.
/// [cabeçalho 12B][blobs][tabela][rodapé 280B v2 | 288B v3]; .pkx = continuação lógica do .pck.
/// </summary>
public sealed class PckArchive : IDisposable
{
    const int Key1 = -1466731422;
    const int FSig1 = 0x4DCA23EF;

    static readonly Encoding Gbk;
    static PckArchive()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936);
    }

    readonly CancellationToken _token;
    readonly FileStream _pck;
    readonly FileStream? _pkx;
    readonly Dictionary<string, PckEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }
    public int Version { get; }
    public IReadOnlyList<PckEntry> Entries { get; }

    long Length => _pck.Length + (_pkx?.Length ?? 0);

    public PckArchive(string pckPath, CancellationToken cancellationToken = default)
    {
        _token = cancellationToken; _token.ThrowIfCancellationRequested();
        FilePath = pckPath;
        // ReadWrite: o jogo (elementclient) mantém os .pck abertos; com FileShare.Read o editor não abria nada com o jogo aberto
        _pck = new FileStream(pckPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            var pkxPath = System.IO.Path.ChangeExtension(pckPath, ".pkx");
            if (File.Exists(pkxPath))
                _pkx = new FileStream(pkxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            var head = ReadAt(0, 4);
            if (BitConverter.ToInt32(head) != FSig1)
                throw new InvalidDataException($"Não é um pacote Angelica válido: {pckPath}");

            var tail = ReadAt(Length - 8, 8);
            int count = BitConverter.ToInt32(tail, 0);
            Version = BitConverter.ToInt16(tail, 4) == 3 ? 3 : 2;

            int footer = Version == 3 ? 288 : 280;
            int entrySize = Version == 3 ? 288 : 276;
            long tableOffset = Version == 3
                ? BitConverter.ToInt64(ReadAt(Length - 280, 8)) ^ Key1
                : (uint)(BitConverter.ToUInt32(ReadAt(Length - 272, 4)) ^ Key1);

            var table = ReadAt(tableOffset, checked((int)(Length - tableOffset - footer)));
            var entries = new List<PckEntry>(count);
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0) _token.ThrowIfCancellationRequested();
                int size = BitConverter.ToInt32(table, pos) ^ Key1;
                pos += 8; // tamanho ^ KEY_1, tamanho ^ KEY_2 (ignorado)
                var raw = table.AsSpan(pos, size).ToArray();
                pos += size;
                if (size < entrySize) raw = Inflate(raw, entrySize, _token);
                var e = ParseEntry(raw, Version);
                entries.Add(e);
                _byPath[e.Path] = e;
            }
            Entries = entries;
        }
        catch { _pkx?.Dispose(); _pck.Dispose(); throw; }
    }

    static PckEntry ParseEntry(byte[] b, int version)
    {
        int nul = Array.IndexOf(b, (byte)0, 0, 260);
        var path = Gbk.GetString(b, 0, nul < 0 ? 260 : nul).Replace('/', '\\');
        return version == 3
            ? new PckEntry(path, BitConverter.ToInt64(b, 264), BitConverter.ToInt32(b, 272), BitConverter.ToInt32(b, 276))
            : new PckEntry(path, BitConverter.ToUInt32(b, 260), BitConverter.ToInt32(b, 264), BitConverter.ToInt32(b, 268));
    }

    public PckEntry? Find(string path) =>
        _byPath.TryGetValue(path.Replace('/', '\\').TrimStart('\\'), out var e) ? e : null;

    /// <summary>Conteúdo descompactado de uma entrada.</summary>
    public byte[] Read(PckEntry e)
    {
        var data = ReadAt(e.Offset, e.CSize);
        return e.CSize < e.Size ? Inflate(data, e.Size, _token) : data;
    }

    static byte[] Inflate(byte[] data, int size, CancellationToken token = default)
    {
        var outBuf = new byte[size];
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        int read = 0, n;
        while (read < size)
        {
            token.ThrowIfCancellationRequested();
            n = z.Read(outBuf, read, Math.Min(128 * 1024, size - read));
            if (n == 0) break;
            read += n;
        }
        return outBuf;
    }

    /// <summary>Lê no endereço lógico do par .pck+.pkx (pode cruzar a fronteira).</summary>
    byte[] ReadAt(long pos, int count)
    {
        var buf = new byte[count];
        int done = 0;
        while (done < count)
        {
            _token.ThrowIfCancellationRequested();
            long p = pos + done;
            var (s, local) = p < _pck.Length ? (_pck, p) : (_pkx ?? throw new EndOfStreamException(), p - _pck.Length);
            s.Position = local;
            int n = s.Read(buf, done, (int)Math.Min(1024 * 1024, Math.Min(count - done, s.Length - local)));
            if (n <= 0) throw new EndOfStreamException();
            done += n;
        }
        return buf;
    }

    public void Dispose()
    {
        _pck.Dispose();
        _pkx?.Dispose();
    }
}
