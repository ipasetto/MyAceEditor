using System.Text.Json;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

public sealed record BackupSetEntry(string Original, string Backup);

/// <summary>
/// Um Salvar inteiro de uma ferramenta (ex.: Objetos do mundo grava .ecwld, .ecbsd, map.bht e o litmodels.pck): a cópia de
/// cada arquivo antes de gravar e, no pacote dividido, os 4 bytes antigos do cabeçalho do .pck (u32 em +4).
/// </summary>
public sealed record BackupSet(string Tool, string Map, DateTime When, List<BackupSetEntry> Files, string? PckMain = null, string? PckHeader = null);

/// <summary>
/// Conjuntos de backup por ferramenta (pedido do usuário 08/11: restaurar arquivo por arquivo é ruim). Ficam em
/// BackupStore.Root\conjuntos\*.json; restaurar volta todos os arquivos do conjunto de uma vez.
/// </summary>
public static class BackupSets
{
    internal static string? FolderOverride;   // só teste

    /// <summary>
    /// Avisa que arquivos voltaram de um backup (conjunto ou arquivo único), para as janelas abertas relerem do disco
    /// (pedido do usuário 08/11: o Mundo 3D seguia mostrando a árvore plantada até fechar e abrir). Vem na thread da tela.
    /// </summary>
    public static event Action<IReadOnlyCollection<string>>? Restored;
    public static void NotifyRestored(IReadOnlyCollection<string> files)
    {
        try { Restored?.Invoke(files); }
        catch (Exception ex) { AppLog.Error("backup: recarregar depois de restaurar", ex); }
    }
    public static string Folder => FolderOverride ?? Path.Combine(BackupStore.Root, "conjuntos");
    const int Keep = 40;
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    /// <summary>Grava o conjunto; null = não deu (o Salvar já foi feito, só não haverá o item único no menu).</summary>
    public static string? Save(BackupSet set)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string slug = new string(set.Tool.Where(char.IsLetterOrDigit).ToArray());
            var path = Path.Combine(Folder, $"{set.When:yyyyMMdd-HHmmss}-{slug}-{set.Map}.json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{set.When:yyyyMMdd-HHmmss}-{slug}-{set.Map}-{n}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(set, Opt));
            foreach (var old in Directory.GetFiles(Folder, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(Keep))
                try { File.Delete(old); } catch (IOException) { }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Error("backup: gravar o conjunto", ex);
            return null;
        }
    }

    /// <summary>Conjuntos gravados, do mais novo para o mais velho (arquivo ilegível ou com cópia que já saiu fica de fora).</summary>
    public static List<BackupSet> List()
    {
        var list = new List<BackupSet>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var f in Directory.GetFiles(Folder, "*.json").OrderByDescending(f => f, StringComparer.Ordinal))
        {
            try { if (JsonSerializer.Deserialize<BackupSet>(File.ReadAllText(f)) is { Files.Count: > 0 } s && s.Files.All(e => File.Exists(e.Backup))) list.Add(s); }   // cópia já podada (o armazém guarda 5 por arquivo): fica fora do menu
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("backup: ler " + f, ex); }
        }
        return list;
    }

    /// <summary>
    /// Volta todos os arquivos do conjunto. Confere antes que todas as cópias existem (faltando uma, nada muda). Arquivos
    /// pequenos ganham uma cópia do estado atual antes de voltar; o pacote volta a parte trocada e os 4 bytes do cabeçalho.
    /// null = tudo voltou; senão, o que não deu, em palavras simples.
    /// </summary>
    public static string? Restore(BackupSet set)
    {
        var missing = set.Files.Where(e => !File.Exists(e.Backup)).Select(e => Path.GetFileName(e.Original)).ToList();
        if (missing.Count > 0) return string.Format(Loc.T("bak.setMissing", "Faltam cópias de: {0}. Nada foi restaurado."), string.Join(", ", missing));
        var failed = new List<string>();
        foreach (var e in set.Files)
        {
            try
            {
                bool big = !e.Backup.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && new FileInfo(e.Backup).Length > 256L * 1024 * 1024;
                if (big)
                {
                    // parte do pacote (até 2 GB, sem compactar): cópia + troca, sem passar pela memória
                    var tmp = e.Original + ".restaurando";
                    File.Copy(e.Backup, tmp, true);
                    File.Replace(tmp, e.Original, null);
                }
                else
                {
                    if (File.Exists(e.Original)) BackupStore.Save(e.Original);
                    AtomicFileSave.Write(e.Original, BackupStore.Read(e.Backup));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException)
            {
                AppLog.Error("backup: restaurar " + e.Original, ex);
                failed.Add(Path.GetFileName(e.Original));
            }
        }
        if (set.PckMain is { } main && set.PckHeader is { Length: 8 } hex)
        {
            try
            {
                using var fs = new FileStream(main, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                fs.Position = 4; fs.Write(Convert.FromHexString(hex)); fs.Flush(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                AppLog.Error("backup: cabeçalho do pacote", ex);
                failed.Add(Path.GetFileName(main));
            }
        }
        var back = set.Files.Select(f => f.Original).Where(f => !failed.Contains(Path.GetFileName(f))).ToList();
        if (back.Count > 0) NotifyRestored(back);
        return failed.Count == 0 ? null : string.Format(Loc.T("bak.setFailed", "Não deu para restaurar: {0} (aberto em outro programa?). Os outros arquivos voltaram."), string.Join(", ", failed));
    }

    /// <summary>Nome da ferramenta para o menu.</summary>
    public static string ToolName(string tool) => tool switch
    {
        "objects" => Loc.T("tools.objects", "Objetos do mundo"),
        "terrain" => Loc.T("tools.terrain", "Terreno"),
        _ => tool,
    };
}
