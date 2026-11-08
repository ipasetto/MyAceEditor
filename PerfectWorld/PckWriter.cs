using System.IO.Compression;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Grava arquivos dentro de um .pck v2 (sem .pkx): troca os que existem e acrescenta os novos (issue #13).
/// Trabalha numa cópia ao lado: o conteúdo novo entra onde estava a tabela antiga, depois vêm a tabela nova e o
/// rodapé (só posição da tabela e nº de arquivos mudam). A cópia é relida e conferida; então File.Replace a põe no
/// lugar e o original vira o backup (quem estava lendo o original continua lendo o backup). Formato: ENGINE-KB §E4.
/// </summary>
public static class PckWriter
{
    const int Key1 = -1466731422;
    const int Key2Default = unchecked((int)0xF1A43653);   // só para pacote vazio; senão vem da própria tabela
    // v2: rodapé 280 B, entrada 276 B (posição u32 em 260); v3: rodapé 288 B, entrada 288 B (posição i64 em 264),
    // posição da tabela i64 e tamanho do pacote i64 no cabeçalho (PckArchive lê os dois)

    static readonly Encoding Gbk;
    static PckWriter() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    /// <summary>
    /// Grava <paramref name="files"/> (caminho no pacote → conteúdo) e devolve o caminho do backup. Os conteúdos novos
    /// entram no lugar da tabela antiga, seguidos da tabela nova e do rodapé. Pacote dividido (.pck + .pkx…, PW-KB §S3):
    /// a tabela fica no fim da ÚLTIMA parte, então só ela é copiada, gravada e trocada (com backup); o .pck só muda no
    /// cabeçalho (u32 em +4 = tamanho lógico de todas as partes, que o cliente usa para achar a tabela), 4 bytes no lugar.
    /// </summary>
    public static string Write(string pckPath, IReadOnlyDictionary<string, byte[]> files) => Write(pckPath, files, null);

    /// <param name="progress">0..1 do trabalho (a cópia da parte do pacote é quase todo o tempo); vem desta thread.</param>
    public static string Write(string pckPath, IReadOnlyDictionary<string, byte[]> files, Action<double>? progress)
    {
        pckPath = PckArchive.Locate(pckPath) ?? pckPath;
        var parts = PckArchive.PartPaths(pckPath);
        var target = parts.Count > 0 ? parts[^1] : pckPath;                       // arquivo que muda de verdade
        long start = parts.Count > 0 ? new FileInfo(pckPath).Length + parts.Take(parts.Count - 1).Sum(x => new FileInfo(x).Length) : 0;   // onde ele começa no pacote lógico
        var tmp = target + ".tmp";
        CopyWithProgress(target, tmp, progress);
        long newLength;
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long len = fs.Length;
                var tail = ReadAt(fs, len - 8, 8);
                bool v3 = BitConverter.ToInt16(tail, 4) == 3;
                int Footer = v3 ? 288 : 280, EntrySize = v3 ? 288 : 276, countAt = v3 ? 280 : 272;
                var footer = ReadAt(fs, len - Footer, Footer);
                int count = BitConverter.ToInt32(footer, countAt);
                long tableOff = (v3 ? BitConverter.ToInt64(footer, 8) ^ Key1 : (uint)(BitConverter.ToInt32(footer, 8) ^ Key1)) - start;
                if (tableOff < 0) throw new NotSupportedException(UI.Loc.T("pck.splitTable", "Pacote dividido com a tabela fora da última parte: gravar não é suportado."));
                var table = ReadAt(fs, tableOff, checked((int)(len - Footer - tableOff)));

                // entradas cruas (276 B), na ordem do pacote
                var raws = new List<byte[]>(count);
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int pos = 0, key2 = Key2Default;
                for (int i = 0; i < count; i++)
                {
                    int size = BitConverter.ToInt32(table, pos) ^ Key1;
                    if (i == 0) key2 = BitConverter.ToInt32(table, pos + 4) ^ size;
                    var r = table.AsSpan(pos + 8, size).ToArray();
                    pos += 8 + size;
                    raws.Add(size < EntrySize ? Inflate(r, EntrySize) : r);
                    index[PathOf(raws[^1])] = i;
                }

                // conteúdos novos no lugar da tabela antiga (posição lógica = start + posição no arquivo)
                fs.Position = tableOff;
                foreach (var (name, content) in files)
                {
                    var key = Normalize(name);
                    if (!index.TryGetValue(key, out var i))
                    {
                        var raw = new byte[EntrySize];
                        var p = Gbk.GetBytes(key);
                        if (p.Length >= 260) throw new ArgumentException(UI.Loc.T("pck.pathTooLong", "Caminho longo demais para o pacote: ") + key);
                        p.CopyTo(raw, 0);
                        raws.Add(raw); i = raws.Count - 1; index[key] = i;
                    }
                    var data = Deflate(content);
                    if (data.Length >= content.Length) data = content;   // o jogo lê cru quando csize == size
                    long at = start + fs.Position;
                    if (!v3 && at + data.Length > uint.MaxValue) throw new NotSupportedException(UI.Loc.T("pck.over4gb", "Pacote passaria de 4 GB."));
                    if (v3)
                    {
                        BitConverter.TryWriteBytes(raws[i].AsSpan(264), at);
                        BitConverter.TryWriteBytes(raws[i].AsSpan(272), content.Length);
                        BitConverter.TryWriteBytes(raws[i].AsSpan(276), data.Length);
                    }
                    else
                    {
                        BitConverter.TryWriteBytes(raws[i].AsSpan(260), (uint)at);
                        BitConverter.TryWriteBytes(raws[i].AsSpan(264), content.Length);
                        BitConverter.TryWriteBytes(raws[i].AsSpan(268), data.Length);
                    }
                    fs.Write(data);
                }

                // tabela nova + rodapé
                long newTable = start + fs.Position;
                foreach (var raw in raws)
                {
                    var z = Deflate(raw);
                    var b = z.Length < EntrySize ? z : raw;
                    fs.Write(BitConverter.GetBytes(b.Length ^ Key1));
                    fs.Write(BitConverter.GetBytes(b.Length ^ key2));
                    fs.Write(b);
                }
                if (v3) BitConverter.TryWriteBytes(footer.AsSpan(8), newTable ^ Key1);
                else BitConverter.TryWriteBytes(footer.AsSpan(8), (int)(uint)newTable ^ Key1);
                BitConverter.TryWriteBytes(footer.AsSpan(countAt), raws.Count);
                fs.Write(footer);
                fs.SetLength(fs.Position);
                newLength = start + fs.Length;
                if (!v3 && newLength > uint.MaxValue) throw new NotSupportedException(UI.Loc.T("pck.over4gb", "Pacote passaria de 4 GB."));
                if (parts.Count > 0 && fs.Length > 0x7FFFFF00) throw new NotSupportedException(UI.Loc.T("pck.partFull", "A última parte do pacote passaria de 2 GB."));
                if (parts.Count == 0)
                {
                    fs.Position = 4;                                   // cabeçalho: tamanho do pacote (u32 no v2, i64 no v3)
                    fs.Write(v3 ? BitConverter.GetBytes(fs.Length) : BitConverter.GetBytes((uint)fs.Length));
                }
            }

            // pacote único: confere relendo a cópia antes de trocar
            if (parts.Count == 0) Verify(tmp, pckPath, files);

            var stamp = target + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var bak = stamp + ".bak";
            for (int n = 2; File.Exists(bak); n++) bak = $"{stamp}-{n}.bak";   // duas gravações no mesmo segundo
            byte[]? oldHead = null;
            try
            {
                if (parts.Count > 0) oldHead = PatchHeader(pckPath, newLength);   // 4 bytes no lugar; desfeito se a troca falhar
                File.Replace(tmp, target, bak);
            }
            catch (IOException ex)
            {
                if (oldHead is not null) RestoreHeader(pckPath, oldHead);
                throw new IOException(string.Format(UI.Loc.T("pck.replaceLocked", "Não consegui trocar o {0}: está aberto em outro programa (o jogo?). Feche-o e tente de novo."), Path.GetFileName(target)) + "\n\n" + ex.Message, ex);
            }
            if (parts.Count > 0)
            {
                // dividido: confere o pacote inteiro já trocado; se não bater, volta a parte e o cabeçalho
                try { Verify(pckPath, null, files); }
                catch
                {
                    File.Copy(bak, target, true); RestoreHeader(pckPath, oldHead!);
                    throw;
                }
            }
            return BackupStore.Adopt(bak, target);   // leitor aberto no antigo: fica ao lado até o próximo recolhimento
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    /// <summary>Releitura: os gravados iguais e nenhuma entrada perdida em relação ao <paramref name="old"/>.</summary>
    static void Verify(string pck, string? old, IReadOnlyDictionary<string, byte[]> files)
    {
        using var a = new PckArchive(pck);
        foreach (var (name, content) in files)
            if (a.Find(Normalize(name)) is not { } e || !a.Read(e).AsSpan().SequenceEqual(content))
                throw new InvalidDataException(UI.Loc.T("pck.rereadMismatch", "Releitura não confere: ") + name);
        if (old is null) return;
        using var o = new PckArchive(old);
        if (a.Entries.Count < o.Entries.Count) throw new InvalidDataException(UI.Loc.T("pck.rereadLost", "Releitura perdeu entradas."));
    }

    /// <summary>Tamanho lógico no cabeçalho do .pck (u32 em +4); devolve os 4 bytes antigos.</summary>
    static byte[] PatchHeader(string pck, long length)
    {
        using var fs = new FileStream(pck, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var old = ReadAt(fs, 4, 4);
        fs.Position = 4; fs.Write(BitConverter.GetBytes((uint)length)); fs.Flush(true);
        return old;
    }

    static void RestoreHeader(string pck, byte[] old)
    {
        using var fs = new FileStream(pck, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = 4; fs.Write(old); fs.Flush(true);
    }

    static string Normalize(string p) => p.Replace('/', '\\').TrimStart('\\');

    static string PathOf(byte[] raw)
    {
        int nul = Array.IndexOf(raw, (byte)0, 0, 260);
        return Normalize(Gbk.GetString(raw, 0, nul < 0 ? 260 : nul));
    }

    static byte[] ReadAt(FileStream fs, long pos, int count)
    {
        var b = new byte[count];
        fs.Position = pos;
        fs.ReadExactly(b);
        return b;
    }

    static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    static byte[] Inflate(byte[] data, int size)
    {
        var o = new byte[size];
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        z.ReadAtLeast(o, size, false);
        return o;
    }

    /// <summary>Cópia em blocos de 4 MB contando o andamento (0..0,9; o resto é a tabela e a troca).</summary>
    static void CopyWithProgress(string from, string to, Action<double>? progress)
    {
        if (progress is null) { File.Copy(from, to, true); return; }
        using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        long total = Math.Max(1, src.Length), done = 0;
        var buf = new byte[4 << 20];
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); done += n; progress(0.9 * done / total); }
    }
}
