using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public sealed record CfgEntry(string Path, int Version);
public sealed record CfgMatch(string ConfigPath, CfgElementsReader Reader, Module Module, ElementsProbe Result)
{
    public string Report => $"Versão do arquivo: v{Reader.Version} (cabeçalho bruto 0x{Reader.RawVersion:X8})\nCFG: {ConfigPath}\n" +
        $"Layout: {ElementsFormatLabels.Label(Result.Format)}\nLeitura completa: {(Result.Complete ? "sim" : "não")}\nBlocos: {Result.BlocksRead}/{Result.TotalBlocks}\n" +
        $"Registros: {Result.Records:N0}\nPosição: {Result.Position:N0}/{Result.Length:N0}\n" +
        (Module.Source is ElementsLayout layout ? $"Salvar permitido: {(layout.CanWrite ? "sim" : "não")}\n{layout.ReadOnlyReason}" : "");
}
public sealed class CfgChoiceRequiredException : InvalidOperationException
{
    public IReadOnlyList<string> Paths { get; }
    public CfgChoiceRequiredException(IEnumerable<string> paths)
        : base("Há mais de um CFG compatível com esta versão; escolha manualmente conforme o ramo do jogo.") => Paths = paths.ToList();
}

/// <summary>Casa pelo número de versão completo (16 bits baixos); ramos da mesma versão são testados um a um, nunca pega o primeiro arquivo nem versão vizinha.</summary>
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
        context?.Report(.08, "Casando CFG", $"versão dos dados v{reader.Version}", force: true);
        if (explicitCfg is not null)
        {
            var cfg = ElementsCfg.Load(explicitCfg);
            var module = reader.Parse(cfg, context?.Slice(.08, 1));
            if (VersionFromName(explicitCfg) is int expected && expected != reader.Version && module.Source is ElementsLayout layout)
                layout.Warnings.Add($"O CFG escolhido é v{expected}, mas os dados são v{reader.Version}; aberto como somente leitura.");
            return new CfgMatch(cfg.SourcePath, reader, module, reader.LastResult!);
        }
        var entries = (cfgDirectory is null ? DefaultEntries() : Discover(cfgDirectory))
            .Where(x => x.Version == reader.Version).OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
        if (entries.Count == 0) throw new InvalidDataException($"Nenhum CFG para a versão v{reader.Version}. Coloque o arquivo em PerfectWorld\\cfg ou escolha um CFG manualmente.");
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
                if (!seen.Add(cfg.SchemaKey)) continue; // aliases idênticos não contam como ambiguidade.
                var scope = context?.Slice(.08 + .12 * candidate / entries.Count, .08 + .12 * (candidate + 1.0) / entries.Count);
                var best = reader.Probe(cfg, scope).OrderByDescending(x => x.Complete).ThenBy(x => x.HasUnmappedBytes)
                    .ThenByDescending(x => x.BlocksRead).ThenByDescending(x => x.Position).First();
                candidates.Add((cfg, best));
                messages.Add($"{System.IO.Path.GetFileName(entry.Path)}: {ElementsFormatLabels.Label(best.Format)}, {best.BlocksRead}/{best.TotalBlocks} blocos, {best.Position}/{best.Length} bytes; {best.Error ?? "chegou ao fim do arquivo"}");
            }
            catch (InvalidDataException ex) { messages.Add($"{System.IO.Path.GetFileName(entry.Path)}: {ex.Message}"); }
        }
        var complete = candidates.Where(x => x.Probe.Complete).ToList();
        if (complete.Any(x => !x.Probe.HasUnmappedBytes)) complete = complete.Where(x => !x.Probe.HasUnmappedBytes).ToList();
        if (complete.Count > 1) throw new CfgChoiceRequiredException(complete.Select(x => x.Cfg.SourcePath));
        if (complete.Count == 0) throw new InvalidDataException($"Nenhum CFG candidato da versão v{reader.Version} casou por completo.\n" +
            string.Join("\n", messages) + "\nEscolha um CFG manualmente para ver os dados parciais; leitura parcial não conta como sucesso e não pode ser salva.");
        var selected = complete[0];
        var result = reader.Parse(selected.Cfg, selected.Probe.Format, context?.Slice(.20, 1));
        if (!reader.LastResult!.Complete) throw new InvalidDataException("Falha na decodificação completa: " + reader.StoppedAt);
        return new CfgMatch(selected.Cfg.SourcePath, reader, result, reader.LastResult!);
    }

    public static string Audit(string directory)
    {
        var sb = new StringBuilder("CFG\tversão\tblocos declarados\tblocos lidos\tstatus\tobservação\n");
        var entries = Discover(directory);
        if (entries.Count == 0) throw new InvalidDataException("A pasta indicada não tem CFG com número de versão reconhecível.");
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
