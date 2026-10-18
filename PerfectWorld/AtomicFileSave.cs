namespace MyAceEditor.PerfectWorld;

public sealed class FileSaveException : IOException
{
    public FileSaveException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>同目录唯一临时文件、逐块回读、唯一备份、替换；失败不掩盖原始原因。</summary>
public static class AtomicFileSave
{
    public static string? Write(string path, byte[] bytes, LoadContext? context = null)
    {
        string full = Path.GetFullPath(path);
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = null;
        string stage = "写入临时文件";
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
            stage = "校验临时文件";
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != bytes.Length) throw new IOException("写入后的文件长度不一致。");
                var buffer = new byte[1024 * 1024];
                int at = 0;
                while (at < bytes.Length)
                {
                    context?.Check();
                    int count = stream.Read(buffer, 0, Math.Min(buffer.Length, bytes.Length - at));
                    if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(bytes.AsSpan(at, count)))
                        throw new IOException("临时文件回读内容与待保存数据不一致。");
                    at += count;
                    context?.Report(.65 + .25 * at / Math.Max(1, bytes.Length), stage, Path.GetFileName(full));
                }
            }
            context?.Check();
            if (File.Exists(full))
            {
                stage = "创建备份";
                context?.Report(.90, stage, Path.GetFileName(full), force: true);
                backup = full + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(full, backup, overwrite: false);
                // 最后一个取消检查在替换前；替换完成后不得再报告为取消。
                context?.Check();
                stage = "替换目标文件";
                try { File.Replace(temp, full, null); }
                catch (NotSupportedException) { File.Move(temp, full, overwrite: true); }
            }
            else
            {
                stage = "创建目标文件";
                context?.Check();
                File.Move(temp, full);
            }
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            throw new FileSaveException($"{stage}失败。\n目标：{full}\n" +
                (backup is null ? "" : $"备份位置：{backup}\n") + $"原因：{ex.Message}", ex);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
