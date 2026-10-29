using System.IO.Compression;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Log de erros do programa (para quem testa mandar para nós): um arquivo por dia em %APPDATA%\MyAceEditor\logs,
/// guardando os últimos 14 dias. Entra o que deu errado — exceções não tratadas, erros mostrados ao usuário, linhas
/// ##ERRO do servidor local — e marcos (abrir pasta, salvar, enviar). Nunca grava senha, chave ou conteúdo de arquivo.
/// Export junta os logs e um resumo do ambiente num .zip.
/// </summary>
public static class AppLog
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "logs");
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message) => Write("ERRO ", message);
    public static void Error(string context, Exception ex) => Write("ERRO ", context + ": " + ex);

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, $"myace-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {level}{message.Replace("\r\n", "\n").Replace("\n", "\n    ")}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* log nunca derruba o programa */ }
    }

    /// <summary>Apaga logs com mais de 14 dias.</summary>
    public static void Prune()
    {
        try { foreach (var f in Directory.EnumerateFiles(Dir, "myace-*.log")) if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f); }
        catch { }
    }

    /// <summary>Exceções que escapariam: viram linha no log (a da tela continua mostrando a mensagem de sempre).</summary>
    public static void Hook()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Error("exceção não tratada", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { Error("tarefa em segundo plano", e.Exception); e.SetObserved(); };
    }

    /// <summary>Zip com os logs e um resumo do ambiente (versão, Windows, idioma, versões dos arquivos abertos).</summary>
    public static void Export(string zipPath, string environment)
    {
        lock (Gate)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            if (Directory.Exists(Dir))
                foreach (var f in Directory.EnumerateFiles(Dir, "myace-*.log").OrderBy(f => f))
                {
                    using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var dst = zip.CreateEntry(Path.GetFileName(f)).Open();
                    src.CopyTo(dst);
                }
            using var w = new StreamWriter(zip.CreateEntry("ambiente.txt").Open(), Encoding.UTF8);
            w.Write(environment);
        }
    }
}
