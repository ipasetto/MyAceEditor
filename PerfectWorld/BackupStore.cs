using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Backups fora do cliente: antes de trocar um arquivo do jogo, a versão atual vai para
/// %LOCALAPPDATA%\MyAceEditor\backups\&lt;pasta do PW&gt;\&lt;caminho dentro dela&gt;.&lt;data&gt;.gz (os .pck/.pkx, que já são
/// comprimidos por dentro, vão sem compactar). Ficam só os <see cref="Keep"/> mais novos de cada arquivo.
/// Antes cada gravação deixava um &lt;arquivo&gt;.&lt;data&gt;.bak ao lado do original, dentro do cliente; <see cref="Migrate"/> leva esses
/// para cá.
/// </summary>
public static class BackupStore
{
    public static int Keep { get; set; } = 5;

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyAceEditor", "backups");

    static readonly Regex OldBak = new(@"^(?<orig>.+?)\.(?<stamp>\d{8}-\d{6})(?:-\d{3})?(?:-\d+)?(?:\.[0-9a-f]{32})?\.bak$", RegexOptions.IgnoreCase);

    /// <summary>Guarda uma cópia do arquivo como está agora; devolve o caminho do backup (null se o arquivo não existe).</summary>
    public static string? Save(string file)
    {
        var full = Path.GetFullPath(file);
        if (!File.Exists(full)) return null;
        var dest = Target(full, DateTime.Now);
        using (var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            Write(src, dest);
        Prune(full);
        return dest;
    }

    /// <summary>
    /// Leva para o armazém um backup que já existe ao lado do original (ex.: o .bak que o File.Replace deixou). Se não der
    /// (aberto por outro programa), fica onde está e o <see cref="Migrate"/> o recolhe depois.
    /// </summary>
    public static string Adopt(string bak, string original, DateTime? when = null)
    {
        try
        {
            var dest = Target(Path.GetFullPath(original), when ?? File.GetLastWriteTime(bak));
            if (Compress(original))
            {
                using (var src = new FileStream(bak, FileMode.Open, FileAccess.Read, FileShare.Read)) Write(src, dest);
                File.Delete(bak);
            }
            else { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Move(bak, dest); }
            Prune(Path.GetFullPath(original));
            return dest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return bak; }
    }

    /// <summary>Recolhe os .bak antigos (do formato que o programa gravava) de uma pasta e subpastas. Devolve (arquivos, bytes liberados).</summary>
    public static (int Files, long Bytes) Migrate(string folder, Action<string>? step = null)
    {
        int n = 0; long bytes = 0;
        if (!Directory.Exists(folder)) return (0, 0);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var bak in Directory.EnumerateFiles(folder, "*.bak", opts).ToList())
        {
            if (OldBak.Match(Path.GetFileName(bak)) is not { Success: true } m) continue;
            var orig = Path.Combine(Path.GetDirectoryName(bak)!, m.Groups["orig"].Value);
            if (!File.Exists(orig)) continue;   // .bak de outro programa ou de arquivo que não existe mais: não é nosso para mexer
            var when = DateTime.TryParseExact(m.Groups["stamp"].Value, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var t) ? t : File.GetLastWriteTime(bak);
            long len = new FileInfo(bak).Length;
            step?.Invoke(Path.GetFileName(bak));
            if (Adopt(bak, orig, when) != bak) { n++; bytes += len; }
        }
        return (n, bytes);
    }

    /// <summary>Tamanho total do armazém em bytes.</summary>
    public static long Size() => Directory.Exists(Root)
        ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    // ---------- interno ----------
    static bool Compress(string original) => Path.GetExtension(original).ToLowerInvariant() is not (".pck" or ".pkx");

    static void Write(Stream src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".tmp";
        using (var outFile = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (dest.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                using (var gz = new GZipStream(outFile, CompressionLevel.Optimal, leaveOpen: true)) src.CopyTo(gz);
            else src.CopyTo(outFile);
            outFile.Flush(flushToDisk: true);
        }
        File.Move(tmp, dest, overwrite: true);
    }

    /// <summary>
    /// Pasta do PW = a que contém "element" (cliente) ou "gamed" (servidor); o backup guarda o caminho dentro dela, com o
    /// nome dela + um código curto do caminho completo (dois clientes com o mesmo nome não se misturam).
    /// </summary>
    static string Target(string full, DateTime when)
    {
        string? root = null;
        for (var d = Path.GetDirectoryName(full); d is not null; d = Path.GetDirectoryName(d))
            if (Path.GetFileName(d).Equals("element", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(d).Equals("gamed", StringComparison.OrdinalIgnoreCase))
            { root = Path.GetDirectoryName(d); break; }
        root ??= Path.GetDirectoryName(full)!;
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8];
        var name = Path.GetFileName(root.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = root.Replace(":", "").Trim('\\', '/');
        var rel = Path.GetRelativePath(root, full);
        var stamp = when.ToString("yyyyMMdd-HHmmss");
        var baseName = Path.Combine(Root, $"{Safe(name)}-{hash}", rel) + "." + stamp;
        var ext = Compress(full) ? ".gz" : ".bak";
        var dest = baseName + ext;
        for (int k = 2; File.Exists(dest); k++) dest = $"{baseName}-{k}{ext}";   // duas gravações no mesmo segundo
        return dest;
    }

    /// <summary>Backups guardados de um arquivo (mais novo primeiro): caminho e data/hora.</summary>
    public static List<(string Path, DateTime When)> ListFor(string file)
    {
        var full = Path.GetFullPath(file);
        var dir = Path.GetDirectoryName(Target(full, DateTime.Now))!;
        var list = new List<(string, DateTime)>();
        if (!Directory.Exists(dir)) return list;
        var prefix = Path.GetFileName(full) + ".";
        foreach (var f in Directory.EnumerateFiles(dir, prefix + "*"))
            if (Regex.Match(Path.GetFileName(f)[prefix.Length..], @"^(\d{8}-\d{6})(?:-\d+)?\.(?:gz|bak)$", RegexOptions.IgnoreCase) is { Success: true } m
                && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when))
                list.Add((f, when));
        return list.OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Pasta do armazém de uma raiz (pasta acima de element\ ou gamed\), igual à que <see cref="Target"/> usa.</summary>
    public static string FolderOfRoot(string root)
    {
        root = Path.GetFullPath(root).TrimEnd('\\', '/');
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8];
        var name = Path.GetFileName(root);
        if (string.IsNullOrEmpty(name)) name = root.Replace(":", "").Trim('\\', '/');
        return Path.Combine(Root, $"{Safe(name)}-{hash}");
    }

    /// <summary>
    /// Os <paramref name="n"/> backups mais novos de qualquer arquivo destas raízes (cliente e servidor: elements, tasks,
    /// path.sev, npcgen, pacotes...), com o arquivo original de cada um.
    /// </summary>
    public static List<(string Bak, string Original, DateTime When)> Recent(IEnumerable<string> roots, int n)
    {
        var list = new List<(string, string, DateTime)>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = FolderOfRoot(root);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (Regex.Match(Path.GetFileName(f), @"^(?<n>.+?)\.(?<s>\d{8}-\d{6})(?:-\d+)?\.(?:gz|bak)$", RegexOptions.IgnoreCase) is { Success: true } m
                    && DateTime.TryParseExact(m.Groups["s"].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when))
                {
                    var rel = Path.GetRelativePath(dir, Path.GetDirectoryName(f)!);
                    list.Add((f, Path.GetFullPath(Path.Combine(root, rel == "." ? "" : rel, m.Groups["n"].Value)), when));
                }
        }
        return list.OrderByDescending(x => x.Item3).Take(n).ToList();
    }

    /// <summary>Nome do arquivo original de um backup ("elements.data.20261101-172944.gz" -> "elements.data").</summary>
    public static string? OriginalName(string bak) =>
        Regex.Match(Path.GetFileName(bak), @"^(?<n>.+?)\.\d{8}-\d{6}(?:-\d+)?\.(?:gz|bak)$", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups["n"].Value : null;

    /// <summary>Conteúdo do backup (.gz descompactado; .bak como está).</summary>
    public static byte[] Read(string bak)
    {
        if (!bak.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(bak);
        using var gz = new System.IO.Compression.GZipStream(File.OpenRead(bak), System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream(); gz.CopyTo(ms); return ms.ToArray();
    }

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    static void Prune(string full)
    {
        var dir = Path.GetDirectoryName(Target(full, DateTime.Now))!;
        if (!Directory.Exists(dir)) return;
        var prefix = Path.GetFileName(full) + ".";
        // nome = <arquivo>.<yyyyMMdd-HHmmss>[-n].(gz|bak): mais novo = maior data e, no mesmo segundo, maior n
        var old = Directory.EnumerateFiles(dir, prefix + "*")
            .Select(f => (f, m: Regex.Match(Path.GetFileName(f)[prefix.Length..], @"^(\d{8}-\d{6})(?:-(\d+))?\.(?:gz|bak)$", RegexOptions.IgnoreCase)))
            .Where(x => x.m.Success)
            .OrderByDescending(x => x.m.Groups[1].Value, StringComparer.Ordinal).ThenByDescending(x => x.m.Groups[2].Success ? int.Parse(x.m.Groups[2].Value) : 1)
            .Select(x => x.f).ToList();
        // nunca apaga (usuário 09/11: "o backup se perdeu"): a cópia MAIS VELHA (o arquivo como era antes da 1a gravação
        // do MyAce) nem a que um conjunto de backup ainda usa (senão o conjunto some do menu Restaurar)
        var keepAlways = BackupSets.ReferencedBackups();
        if (old.Count > 0) keepAlways.Add(old[^1]);
        foreach (var f in old.Skip(Keep))
            if (!keepAlways.Contains(f) || new FileInfo(f).Length > 256L * 1024 * 1024)   // parte grande de pacote: só as mais novas
                try { File.Delete(f); } catch (IOException) { }
    }
}
