using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Só monta os dados, sem tocar em controles; ao concluir, a thread da interface assume tudo de uma vez.</summary>
internal sealed record ElementsLoadBundle(CfgMatch Match, RefIndex References, Dictionary<Item, ItemList> Owners,
    IconAtlas? Atlas, string? AtlasPath, GameConfigs? Configs, TasksFile? Tasks, string? TasksPath,
    int TaskCount, IReadOnlyList<string> Warnings)
{
    public static ElementsLoadBundle Read(string path, string? cfgOverride, LoadContext context,
        IconAtlas? previousAtlas, string? previousAtlasPath, TasksFile? previousTasks, string? previousTasksPath, string? preferredBlock,
        bool preserveSession = false, GameConfigs? previousConfigs = null)
    {
        var match = CfgCatalog.Load(path, cfgOverride, context: context.Slice(0, .60));
        var module = match.Module;
        context.Report(.60, Loc.T("load.indexIds", "Indexando IDs"), force: true);
        var refs = new RefIndex(module, context.Slice(.60, .64));
        context.Report(.64, Loc.T("load.indexRefs", "Indexando referências"), force: true);
        refs.Prepare(context.Slice(.64, .82));
        context.Report(.82, Loc.T("load.lists", "Organizando listas"), force: true);
        int total = module.Lists.Sum(l => l.Items.Count), done = 0;
        var owners = new Dictionary<Item, ItemList>(total, ReferenceEqualityComparer.Instance);
        foreach (var list in module.Lists)
            foreach (var item in list.Items)
            {
                if ((done++ & 255) == 0)
                    context.Slice(.82, .85).Report(done / (double)Math.Max(1, total), Loc.T("load.lists", "Organizando listas"), string.Format(Loc.T("load.records", "{0:N0}/{1:N0} registros"), done, total));
                owners[item] = list;
            }
        var initial = module.FindListByName(preferredBlock ?? "WEAPON_ESSENCE") ?? module.Lists.FirstOrDefault();
        var initialKeys = initial?.Items.FirstOrDefault()?.Fields.Keys.ToArray() ?? Array.Empty<string>();
        for (int i = 0; i < initialKeys.Length; i++)
        {
            context.Check();
            if ((i & 127) == 0) context.Slice(.85, .86).Report(i / (double)Math.Max(1, initialKeys.Length), Loc.T("load.labels", "Preparando rótulos dos campos"), string.Format(Loc.T("load.fields", "{0:N0}/{1:N0} campos"), i, initialKeys.Length));
            FieldLabels.Label(initialKeys[i]);
        }
        var warnings = new List<string>();
        IconAtlas? atlas = null; GameConfigs? configs = null; TasksFile? tasks = null;
        string? atlasPath = preserveSession ? previousAtlasPath : IconAtlas.FindSurfacesPck(path), taskPath = null;
        context.Report(.86, Loc.T("load.icons", "Lendo ícones"), Loc.T("load.iconsDetail", "pacotes grandes; ainda dá para cancelar"), indeterminate: true, force: true);
        try { atlas = preserveSession ? previousAtlas : atlasPath == previousAtlasPath && previousAtlas is not null ? previousAtlas : atlasPath is null ? null : IconAtlas.Load(atlasPath, context.Slice(.86, .91)); }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add(Loc.T("load.iconsFail", "Ícones não carregados: ") + ex.Message); }
        context.Check();
        context.Report(.91, Loc.T("load.descriptions", "Lendo descrições"), Loc.T("load.descriptionsDetail", "cores, descrições e atributos adicionais do jogo"), indeterminate: true, force: true);
        try { configs = preserveSession ? previousConfigs : GameConfigs.FindConfigsPck(path) is { } cp ? GameConfigs.Load(cp, context.Slice(.91, .95)) : null; }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add(Loc.T("load.descriptionsFail", "Descrições não carregadas: ") + ex.Message); }
        context.Check();
        context.Report(.95, Loc.T("load.tasks", "Lendo tasks.data da mesma pasta"), force: true);
        string near = Path.Combine(Path.GetDirectoryName(path) ?? "", "tasks.data");
        if (preserveSession) { tasks = previousTasks; taskPath = previousTasksPath; }
        else if (File.Exists(near))
        {
            try
            {
                tasks = near == previousTasksPath && previousTasks is not null ? previousTasks : new TasksFile(near, context.Slice(.95, .99));
                taskPath = near;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add(Loc.T("load.tasksFail", "tasks.data da mesma pasta não carregado: ") + ex.Message); }
        }
        context.Check();
        int taskCount = tasks?.Index.Count ?? 0;
        context.Report(.99, Loc.T("load.ready", "Dados prontos"), Loc.T("load.readyDetail", "trocando para os novos dados…"), force: true);
        return new ElementsLoadBundle(match, refs, owners, atlas, atlasPath, configs, tasks, taskPath, taskCount, warnings);
    }
}
