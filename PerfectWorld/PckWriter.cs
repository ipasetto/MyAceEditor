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
    const int EntrySize = 276, Footer = 280;

    static readonly Encoding Gbk;
    static PckWriter() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    /// <summary>Grava <paramref name="files"/> (caminho no pacote → conteúdo) e devolve o caminho do backup.</summary>
    public static string Write(string pckPath, IReadOnlyDictionary<string, byte[]> files)
    {
        if (File.Exists(Path.ChangeExtension(pckPath, ".pkx"))) throw new NotSupportedException(UI.Loc.T("pck.splitUnsupported", "Pacote dividido em .pck + .pkx: gravar ainda não é suportado."));
        var tmp = pckPath + ".tmp";
        File.Copy(pckPath, tmp, true);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long len = fs.Length;
                var tail = ReadAt(fs, len - 8, 8);
                if (BitConverter.ToInt16(tail, 4) == 3) throw new NotSupportedException(UI.Loc.T("pck.v3Unsupported", "Pacote v3: gravar ainda não é suportado."));
                var footer = ReadAt(fs, len - Footer, Footer);
                int count = BitConverter.ToInt32(footer, 272);
                long tableOff = (uint)(BitConverter.ToInt32(footer, 8) ^ Key1);
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

                // conteúdos novos no lugar da tabela antiga
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
                    if (fs.Position + data.Length > uint.MaxValue) throw new NotSupportedException(UI.Loc.T("pck.over4gb", "Pacote passaria de 4 GB."));
                    BitConverter.TryWriteBytes(raws[i].AsSpan(260), (uint)fs.Position);
                    BitConverter.TryWriteBytes(raws[i].AsSpan(264), content.Length);
                    BitConverter.TryWriteBytes(raws[i].AsSpan(268), data.Length);
                    fs.Write(data);
                }

                // tabela nova + rodapé
                long newTable = fs.Position;
                foreach (var raw in raws)
                {
                    var z = Deflate(raw);
                    var b = z.Length < EntrySize ? z : raw;
                    fs.Write(BitConverter.GetBytes(b.Length ^ Key1));
                    fs.Write(BitConverter.GetBytes(b.Length ^ key2));
                    fs.Write(b);
                }
                BitConverter.TryWriteBytes(footer.AsSpan(8), (int)(uint)newTable ^ Key1);
                BitConverter.TryWriteBytes(footer.AsSpan(272), raws.Count);
                fs.Write(footer);
                fs.SetLength(fs.Position);
                if (fs.Length > uint.MaxValue) throw new NotSupportedException(UI.Loc.T("pck.over4gb", "Pacote passaria de 4 GB."));
                fs.Position = 4;                                   // cabeçalho: tamanho do pacote
                fs.Write(BitConverter.GetBytes((uint)fs.Length));
            }

            // confere relendo a cópia: os gravados iguais e nenhuma entrada perdida
            using (var a = new PckArchive(tmp))
            {
                foreach (var (name, content) in files)
                    if (a.Find(Normalize(name)) is not { } e || !a.Read(e).AsSpan().SequenceEqual(content))
                        throw new InvalidDataException(UI.Loc.T("pck.rereadMismatch", "Releitura não confere: ") + name);
                using var old = new PckArchive(pckPath);
                if (a.Entries.Count < old.Entries.Count) throw new InvalidDataException(UI.Loc.T("pck.rereadLost", "Releitura perdeu entradas."));
            }

            var stamp = pckPath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var bak = stamp + ".bak";
            for (int n = 2; File.Exists(bak); n++) bak = $"{stamp}-{n}.bak";   // duas gravações no mesmo segundo
            try { File.Replace(tmp, pckPath, bak); }
            catch (IOException ex) { throw new IOException(string.Format(UI.Loc.T("pck.replaceLocked", "Não consegui trocar o {0}: está aberto em outro programa (o jogo?). Feche-o e tente de novo."), Path.GetFileName(pckPath)) + "\n\n" + ex.Message, ex); }
            return bak;
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
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
}
