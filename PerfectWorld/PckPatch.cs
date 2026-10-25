using System.IO.Compression;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Troca o conteúdo de UM arquivo dentro do .pck sem refazer o pacote (gravar .pck inteiro ainda não existe, issue #13):
/// o conteúdo novo tem o MESMO tamanho; guardado cru = sobrescreve no lugar; compactado = zlib novo que caiba no espaço
/// antigo, completado com zeros (o zlib do jogo e o nosso leitor param no fim do fluxo). Backup do .pck antes; confere
/// relendo. Entrada que cai no .pkx não é suportada.
/// </summary>
public static class PckPatch
{
    public static void ReplaceSameSize(string pckPath, string entryPath, byte[] content)
    {
        PckEntry e;
        using (var a = new PckArchive(pckPath))
            e = a.Find(entryPath) ?? throw new FileNotFoundException($"{entryPath} não está em {pckPath}");
        if (content.Length != e.Size) throw new InvalidOperationException($"Conteúdo novo precisa ter {e.Size} bytes (tem {content.Length}).");
        if (e.Offset + e.CSize > new FileInfo(pckPath).Length) throw new InvalidOperationException("Entrada no .pkx: não suportado.");

        byte[] payload;
        if (e.CSize >= e.Size) payload = content;
        else
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(content);
            if (ms.Length > e.CSize) throw new InvalidOperationException($"Compactado ficou com {ms.Length} bytes; o espaço é {e.CSize}.");
            payload = new byte[e.CSize];
            ms.ToArray().CopyTo(payload, 0);
        }

        File.Copy(pckPath, pckPath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", false);
        using (var fs = new FileStream(pckPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = e.Offset;
            fs.Write(payload);
        }
        using var check = new PckArchive(pckPath);
        if (!check.Read(check.Find(entryPath)!).AsSpan().SequenceEqual(content))
            throw new InvalidDataException("Releitura não confere; restaure o .bak ao lado do .pck.");
    }
}
