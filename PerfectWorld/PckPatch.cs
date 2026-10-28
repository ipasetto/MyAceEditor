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
            e = a.Find(entryPath) ?? throw new FileNotFoundException(string.Format(UI.Loc.T("pck.notIn", "{0} não está em {1}"), entryPath, pckPath));
        if (content.Length != e.Size) throw new InvalidOperationException(string.Format(UI.Loc.T("pck.sizeMust", "Conteúdo novo precisa ter {0} bytes (tem {1})."), e.Size, content.Length));
        if (e.Offset + e.CSize > new FileInfo(pckPath).Length) throw new InvalidOperationException(UI.Loc.T("pck.inPkx", "Entrada no .pkx: não suportado."));

        byte[] payload;
        if (e.CSize >= e.Size) payload = content;
        else
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) z.Write(content);
            if (ms.Length > e.CSize) throw new InvalidOperationException(string.Format(UI.Loc.T("pck.tooBig", "Compactado ficou com {0} bytes; o espaço é {1}."), ms.Length, e.CSize));
            payload = new byte[e.CSize];
            ms.ToArray().CopyTo(payload, 0);
        }

        // aberto em outro programa (jogo, outro editor): falha aqui, antes de deixar um backup à toa
        try { using (new FileStream(pckPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { } }
        catch (IOException) { throw new IOException(string.Format(UI.Loc.T("pck.locked", "{0} está aberto em outro programa (o jogo ou outro editor). Feche-o e tente de novo."), Path.GetFileName(pckPath))); }
        // duas trocas no mesmo segundo: o backup da primeira já guarda o original
        var bak = pckPath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
        if (!File.Exists(bak)) File.Copy(pckPath, bak, false);
        using (var fs = new FileStream(pckPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = e.Offset;
            fs.Write(payload);
        }
        using var check = new PckArchive(pckPath);
        if (!check.Read(check.Find(entryPath)!).AsSpan().SequenceEqual(content))
            throw new InvalidDataException(UI.Loc.T("pck.rereadRestore", "Releitura não confere; restaure o .bak ao lado do .pck."));
    }
}
