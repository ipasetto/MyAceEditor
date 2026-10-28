namespace MyAceEditor.PerfectWorld;

public sealed class FileSaveException : IOException
{
    public FileSaveException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Temporário único na mesma pasta, releitura bloco a bloco, backup único e substituição; o erro original nunca é escondido.</summary>
public static class AtomicFileSave
{
    public static string? Write(string path, byte[] bytes, LoadContext? context = null)
    {
        string full = Path.GetFullPath(path);
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = null;
        string stage = FieldKinds.Translate("fsave.writeTemp", "Gravando arquivo temporário");
        try
        {
            context?.Check();
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                for (int at = 0; at < bytes.Length;)
                {
                    context?.Check();
                    int count = Math.Min(1024 * 1024, bytes.Length - at);
                    stream.Write(bytes, at, count);
                    at += count;
                    context?.Report(.65 * at / Math.Max(1, bytes.Length), stage, Path.GetFileName(full));
                }
                stream.Flush(flushToDisk: true);
            }
            stage = FieldKinds.Translate("fsave.checkTemp", "Conferindo arquivo temporário");
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != bytes.Length) throw new IOException(FieldKinds.Translate("fsave.sizeMismatch", "O tamanho do arquivo gravado não confere."));
                var buffer = new byte[1024 * 1024];
                int at = 0;
                while (at < bytes.Length)
                {
                    context?.Check();
                    int count = stream.Read(buffer, 0, Math.Min(buffer.Length, bytes.Length - at));
                    if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(bytes.AsSpan(at, count)))
                        throw new IOException(FieldKinds.Translate("fsave.rereadMismatch", "A releitura do temporário difere dos dados a salvar."));
                    at += count;
                    context?.Report(.65 + .25 * at / Math.Max(1, bytes.Length), stage, Path.GetFileName(full));
                }
            }
            context?.Check();
            if (File.Exists(full))
            {
                stage = FieldKinds.Translate("fsave.backup", "Criando backup");
                context?.Report(.90, stage, Path.GetFileName(full), force: true);
                backup = full + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(full, backup, overwrite: false);
                // A última checagem de cancelamento vem antes da substituição; depois dela nunca se reporta cancelamento.
                context?.Check();
                stage = FieldKinds.Translate("fsave.replace", "Substituindo o arquivo");
                try { File.Replace(temp, full, null); }
                catch (NotSupportedException) { File.Move(temp, full, overwrite: true); }
            }
            else
            {
                stage = FieldKinds.Translate("fsave.create", "Criando o arquivo");
                context?.Check();
                File.Move(temp, full);
            }
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            throw new FileSaveException(string.Format(FieldKinds.Translate("fsave.failedAt", "Falha em: {0}.\nDestino: {1}\n"), stage, full) +
                (backup is null ? "" : string.Format(FieldKinds.Translate("fsave.backupAt", "Backup em: {0}\n"), backup)) + string.Format(FieldKinds.Translate("fsave.reason", "Motivo: {0}"), ex.Message), ex);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
