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
    // continuações lógicas do .pck: .pkx, .pkx1, .pkx2… (o 180 tem 3 partes)
    readonly List<FileStream> _parts = new();
    readonly Dictionary<string, PckEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }
    public int Version { get; }
    public IReadOnlyList<PckEntry> Entries { get; }

    long Length => _pck.Length + _parts.Sum(p => p.Length);

    /// <summary>
    /// Arquivo de cada parte de um pacote. Clientes da comunidade (155, 180) trazem as partes com um número no nome:
    /// litmodels-020.pck + litmodels-013.pkx + litmodels-019.pkx1 (o elementclient monta "%s-%d" e ".pkx%d"). Vale o nome
    /// sem número; senão o único "nome-N.ext" da pasta. null = não existe.
    /// </summary>
    public static string? Locate(string path)
    {
        if (File.Exists(path)) return path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir)) return null;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        var hits = Directory.EnumerateFiles(dir, name + "-*" + ext)
            .Where(f => System.IO.Path.GetExtension(f).Equals(ext, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f)[(name.Length + 1)..], out _)).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>O pacote existe (com ou sem número no nome)?</summary>
    public static bool Exists(string pckPath) => Locate(pckPath) is not null;

    public PckArchive(string pckPath, CancellationToken cancellationToken = default)
    {
        _token = cancellationToken; _token.ThrowIfCancellationRequested();
        pckPath = Locate(pckPath) ?? pckPath;
        FilePath = pckPath;
        // ReadWrite: o jogo (elementclient) mantém os .pck abertos; com FileShare.Read o editor não abria nada com o jogo aberto
        _pck = new FileStream(pckPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            // nome-base sem o número ("litmodels-020.pck" → litmodels) para achar .pkx, .pkx1…
            var dir = System.IO.Path.GetDirectoryName(pckPath) ?? "";
            var baseName = System.IO.Path.GetFileNameWithoutExtension(pckPath);
            int dash = baseName.LastIndexOf('-');
            if (dash > 0 && int.TryParse(baseName[(dash + 1)..], out _) && !File.Exists(System.IO.Path.Combine(dir, baseName + ".pkx"))) baseName = baseName[..dash];
            for (int i = 0; i < 16; i++)
            {
                var part = Locate(System.IO.Path.Combine(dir, baseName + ".pkx" + (i == 0 ? "" : i.ToString())));
                if (part is null) break;
                _parts.Add(new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            }

            var head = ReadAt(0, 4);
            if (BitConverter.ToInt32(head) != FSig1)
                throw new InvalidDataException(string.Format(FieldKinds.Translate("pck.invalid", "Não é um pacote Angelica válido: {0}"), pckPath));

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
        catch { foreach (var x in _parts) x.Dispose(); _pck.Dispose(); throw; }
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

    internal static byte[] Inflate(byte[] data, int size, CancellationToken token = default)
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

    /// <summary>Lê no endereço lógico de .pck + .pkx + .pkx1… (pode cruzar as fronteiras).</summary>
    byte[] ReadAt(long pos, int count)
    {
        var buf = new byte[count];
        int done = 0;
        while (done < count)
        {
            _token.ThrowIfCancellationRequested();
            long p = pos + done;
            FileStream s = _pck; long local = p;
            if (p >= _pck.Length)
            {
                local = p - _pck.Length; s = null!;
                foreach (var part in _parts) { if (local < part.Length) { s = part; break; } local -= part.Length; }
                if (s is null) throw new EndOfStreamException();
            }
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
        foreach (var x in _parts) x.Dispose();
    }
}
