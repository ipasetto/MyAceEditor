using System.Text.Json;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

public sealed record BackupSetEntry(string Original, string Backup);

/// <summary>
/// Um Salvar inteiro de uma ferramenta (ex.: Objetos do mundo grava .ecwld, .ecbsd, map.bht e o litmodels.pck): a cópia de
/// cada arquivo antes de gravar e, no pacote dividido, os 4 bytes antigos do cabeçalho do .pck (u32 em +4).
/// </summary>
public sealed record BackupSet(string Tool, string Map, DateTime When, List<BackupSetEntry> Files, string? PckMain = null, string? PckHeader = null, DateTime? Started = null);

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

    /// <summary>
    /// Sessão do Mundo 3D aberta (usuário 09/11: "consolidar o backup do Mundo 3D em um só"): enquanto a janela está aberta,
    /// todo Salvar dela (objetos, grama, terreno, água, cenário) cai num conjunto só por mapa, com a cópia de cada arquivo de
    /// ANTES da sessão (a 1ª cópia de cada um fica; as seguintes não entram). Restaurar = o mapa como era ao abrir.
    /// </summary>
    public static DateTime? Session { get; set; }
    static readonly HashSet<string> SessionTools = new(StringComparer.Ordinal) { "objects", "terrain", "scene" };

    /// <summary>Grava o conjunto; null = não deu (o Salvar já foi feito, só não haverá o item único no menu).</summary>
    public static string? Save(BackupSet set)
    {
        if (Session is { } started && SessionTools.Contains(set.Tool)) return SaveInSession(set, started);
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

    static string? SaveInSession(BackupSet set, DateTime started)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"{started:yyyyMMdd-HHmmss}-mundo3d-{set.Map}.json");
            BackupSet merged = set with { Tool = "world", Started = started };
            if (File.Exists(path) && JsonSerializer.Deserialize<BackupSet>(File.ReadAllText(path)) is { } old)
            {
                var have = new HashSet<string>(old.Files.Select(f => f.Original), StringComparer.OrdinalIgnoreCase);
                var files = old.Files.Concat(set.Files.Where(f => have.Add(f.Original))).ToList();   // arquivo já guardado: fica a cópia de antes da sessão
                merged = old with { When = set.When, Files = files, PckMain = old.PckMain ?? set.PckMain, PckHeader = old.PckHeader ?? set.PckHeader };
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(merged, Opt));
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            AppLog.Error("backup: gravar o conjunto da sessão do Mundo 3D", ex);
            return null;
        }
    }

    /// <summary>Cópias que algum conjunto ainda usa (o armazém não as apaga ao podar).</summary>
    public static HashSet<string> ReferencedBackups()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(Folder)) return set;
            foreach (var f in Directory.GetFiles(Folder, "*.json"))
                try { if (JsonSerializer.Deserialize<BackupSet>(File.ReadAllText(f)) is { } s) foreach (var e in s.Files) set.Add(Path.GetFullPath(e.Backup)); }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { AppLog.Error("backup: ler " + f, ex); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("backup: listar conjuntos", ex); }
        return set;
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
        var undo = new List<(string Original, string? Before)>();   // arquivos pequenos já voltados e a cópia do estado de antes
        static bool Big(BackupSetEntry x) => !x.Backup.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) && new FileInfo(x.Backup).Length > 256L * 1024 * 1024;
        foreach (var e in set.Files.OrderBy(Big))   // pequenos antes: a parte grande do pacote não tem como desfazer, vai por último
        {
            if (failed.Count > 0) break;   // um falhou: não segue (desfaz os já voltados abaixo)
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
                    bool existed = File.Exists(e.Original);
                    var before = AtomicFileSave.Write(e.Original, BackupStore.Read(e.Backup));   // já guarda o estado atual
                    undo.Add((e.Original, existed ? before : null));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException)
            {
                AppLog.Error("backup: restaurar " + e.Original, ex);
                failed.Add(Path.GetFileName(e.Original));
            }
        }
        if (failed.Count > 0)
        {
            // tudo ou nada: os pequenos que já tinham voltado voltam ao estado de antes (par cliente+servidor não desencontra)
            foreach (var (orig, before) in undo)
                try { if (before is not null) AtomicFileSave.Write(orig, BackupStore.Read(before)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FileSaveException) { AppLog.Error("backup: desfazer " + orig, ex); }
            return string.Format(Loc.T("bak.setFailedNone", "Não deu para restaurar: {0} (aberto em outro programa?). Nada foi restaurado; feche o programa e tente de novo."), string.Join(", ", failed));
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
        // o estado de antes de restaurar vira um conjunto também: restaurar o conjunto errado tem volta (usuário 09/11)
        var prior = undo.Where(u => u.Before is not null).Select(u => new BackupSetEntry(u.Original, u.Before!)).ToList();
        if (prior.Count > 0) Save(new BackupSet("restore", set.Map, DateTime.Now, prior));
        var back = set.Files.Select(f => f.Original).Where(f => !failed.Contains(Path.GetFileName(f))).ToList();
        if (back.Count > 0) NotifyRestored(back);
        return failed.Count == 0 ? null : string.Format(Loc.T("bak.setFailed", "Não deu para restaurar: {0} (aberto em outro programa?). Os outros arquivos voltaram."), string.Join(", ", failed));
    }

    /// <summary>Nome da ferramenta para o menu.</summary>
    public static string ToolName(string tool) => tool switch
    {
        "objects" => Loc.T("tools.objects", "Objetos do mundo"),
        "terrain" => Loc.T("tools.terrain", "Terreno"),
        "restore" => Loc.T("tools.restore", "Antes de restaurar"),
        "world" => Loc.T("tools.world3d", "Mundo 3D"),
        _ => tool,
    };
}
