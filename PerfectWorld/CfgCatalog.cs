using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public sealed record CfgEntry(string Path, int Version);
public sealed record CfgMatch(string ConfigPath, CfgElementsReader Reader, Module Module, ElementsProbe Result)
{
    public string Report => $"文件版本：v{Reader.Version}（原始头 0x{Reader.RawVersion:X8}）\nCFG：{ConfigPath}\n" +
        $"布局：{ElementsFormatLabels.Label(Result.Format)}\n完整读取：{(Result.Complete ? "是" : "否")}\n数据块：{Result.BlocksRead}/{Result.TotalBlocks}\n" +
        $"记录：{Result.Records:N0}\n位置：{Result.Position:N0}/{Result.Length:N0}\n" +
        (Module.Source is ElementsLayout layout ? $"允许保存：{(layout.CanWrite ? "是" : "否")}\n{layout.ReadOnlyReason}" : "");
}
public sealed class CfgChoiceRequiredException : InvalidOperationException
{
    public IReadOnlyList<string> Paths { get; }
    public CfgChoiceRequiredException(IEnumerable<string> paths)
        : base("同一版本有多个结构均可匹配的 CFG，请根据游戏分支手动选择。") => Paths = paths.ToList();
}

/// <summary>按完整低 16 位版本号匹配；同版本分支逐一试读，绝不取第一个文件或相邻版本。</summary>
public static class CfgCatalog
{
    private static readonly Regex VersionName = new(@"_v(\d+)(?:[_-].+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static int? VersionFromName(string path)
    {
        var m = VersionName.Match(System.IO.Path.GetFileNameWithoutExtension(path));
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 0 && n <= ushort.MaxValue ? n : null;
    }
    public static IReadOnlyList<CfgEntry> Discover(string directory) => !Directory.Exists(directory) ? Array.Empty<CfgEntry>() :
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(p => System.IO.Path.GetExtension(p).Equals(".cfg", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: System.IO.Path.GetFullPath(p), Version: VersionFromName(p)))
            .Where(x => x.Version.HasValue).Select(x => new CfgEntry(x.Path, x.Version!.Value))
            .OrderBy(x => x.Version).ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();

    private static IEnumerable<CfgEntry> DefaultEntries()
    {
        var dirs = new[] {
            System.IO.Path.Combine(AppContext.BaseDirectory, "PerfectWorld", "cfg"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "cfg"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "configs"),
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "PerfectWorld", "cfg"),
        };
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(Discover)
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
    }

    public static CfgMatch Load(string dataPath, string? explicitCfg = null, string? cfgDirectory = null, LoadContext? context = null)
    {
        context?.Check();
        var reader = new CfgElementsReader(dataPath, context?.Slice(0, .08));
        context?.Report(.08, "匹配 CFG", $"数据版本 v{reader.Version}", force: true);
        if (explicitCfg is not null)
        {
            var cfg = ElementsCfg.Load(explicitCfg);
            var module = reader.Parse(cfg, context?.Slice(.08, 1));
            if (VersionFromName(explicitCfg) is int expected && expected != reader.Version && module.Source is ElementsLayout layout)
                layout.Warnings.Add($"手选 CFG 的版本 v{expected} 与数据版本 v{reader.Version} 不同，已设为只读。");
            return new CfgMatch(cfg.SourcePath, reader, module, reader.LastResult!);
        }
        var entries = (cfgDirectory is null ? DefaultEntries() : Discover(cfgDirectory))
            .Where(x => x.Version == reader.Version).OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
        if (entries.Count == 0) throw new InvalidDataException($"没有找到完整版本号 v{reader.Version} 对应的 CFG。请将配置放入 PerfectWorld\\cfg，或手动选择 CFG。");
        var candidates = new List<(ElementsCfg Cfg, ElementsProbe Probe)>();
        var messages = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int candidate = 0; candidate < entries.Count; candidate++)
        {
            context?.Check();
            var entry = entries[candidate];
            try
            {
                var cfg = ElementsCfg.Load(entry.Path);
                if (!seen.Add(cfg.SchemaKey)) continue; // 完全相同的配置别名不构成歧义。
                var scope = context?.Slice(.08 + .12 * candidate / entries.Count, .08 + .12 * (candidate + 1.0) / entries.Count);
                var best = reader.Probe(cfg, scope).OrderByDescending(x => x.Complete).ThenBy(x => x.HasUnmappedBytes)
                    .ThenByDescending(x => x.BlocksRead).ThenByDescending(x => x.Position).First();
                candidates.Add((cfg, best));
                messages.Add($"{System.IO.Path.GetFileName(entry.Path)}：{ElementsFormatLabels.Label(best.Format)}，{best.BlocksRead}/{best.TotalBlocks} 块，{best.Position}/{best.Length} 字节；{best.Error ?? "到达文件末尾"}");
            }
            catch (InvalidDataException ex) { messages.Add($"{System.IO.Path.GetFileName(entry.Path)}：{ex.Message}"); }
        }
        var complete = candidates.Where(x => x.Probe.Complete).ToList();
        if (complete.Any(x => !x.Probe.HasUnmappedBytes)) complete = complete.Where(x => !x.Probe.HasUnmappedBytes).ToList();
        if (complete.Count > 1) throw new CfgChoiceRequiredException(complete.Select(x => x.Cfg.SourcePath));
        if (complete.Count == 0) throw new InvalidDataException($"版本 v{reader.Version} 的候选 CFG 均未完整匹配。\n" +
            string.Join("\n", messages) + "\n可手选 CFG 查看部分数据；部分读取不会被标记为成功，也不能保存。");
        var selected = complete[0];
        var result = reader.Parse(selected.Cfg, selected.Probe.Format, context?.Slice(.20, 1));
        if (!reader.LastResult!.Complete) throw new InvalidDataException("完整解码失败：" + reader.StoppedAt);
        return new CfgMatch(selected.Cfg.SourcePath, reader, result, reader.LastResult!);
    }

    public static string Audit(string directory)
    {
        var sb = new StringBuilder("CFG\t版本\t声明块数\t解析块数\t状态\t说明\n");
        var entries = Discover(directory);
        if (entries.Count == 0) throw new InvalidDataException("指定目录没有可识别版本号的 CFG 文件。");
        foreach (var entry in entries)
        {
            try
            {
                var cfg = ElementsCfg.Load(entry.Path);
                sb.AppendLine($"{System.IO.Path.GetFileName(entry.Path)}\t{entry.Version}\t{cfg.DeclaredBlockCount}\t{cfg.Blocks.Count}\t" +
                    (cfg.Warnings.Count == 0 ? "CFG_OK" : "CFG_WARNING") + "\t" + string.Join(" | ", cfg.Warnings));
            }
            catch (InvalidDataException ex) { sb.AppendLine($"{System.IO.Path.GetFileName(entry.Path)}\t{entry.Version}\t\t\tCFG_ERROR\t{ex.Message.Replace('\t', ' ')}"); }
        }
        return sb.ToString();
    }
}
