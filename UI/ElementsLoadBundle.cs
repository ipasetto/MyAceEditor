using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Só monta os dados, sem tocar em controles; ao concluir, a thread da interface assume tudo de uma vez.</summary>
internal sealed record ElementsLoadBundle(CfgMatch Match, RefIndex References, Dictionary<Item, ItemList> Owners,
    IconAtlas? Atlas, string? AtlasPath, GameConfigs? Configs, TasksFile? Tasks, string? TasksPath,
    int TaskCount, IReadOnlyList<string> Warnings)
{
    /// <summary>Tempos da última abertura, por etapa (para o log e o --load-bench).</summary>
    public static string LastTimes { get; private set; } = "";
    /// <summary>--load-bench: mede também a memória de cada etapa (força coleta; só para medir).</summary>
    public static bool MemTrace;

    public static ElementsLoadBundle Read(string path, string? cfgOverride, LoadContext context,
        IconAtlas? previousAtlas, string? previousAtlasPath, TasksFile? previousTasks, string? previousTasksPath, string? preferredBlock,
        bool preserveSession = false, GameConfigs? previousConfigs = null)
    {
        // tempo de cada etapa (log e --load-bench): base para otimizar com número, não com palpite
        long mem0 = MemTrace ? GC.GetTotalMemory(true) : 0;
        var sw = System.Diagnostics.Stopwatch.StartNew(); var times = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Lap(string name) { times.Enqueue($"{name} {sw.ElapsedMilliseconds} ms" + (MemTrace ? $" (+{(GC.GetTotalMemory(true) - mem0) / 1048576} MB)" : "")); if (MemTrace) mem0 = GC.GetTotalMemory(true); sw.Restart(); }
        var warnings = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // ícones, descrições e tasks.data não dependem do elements: vão em paralelo com ele (antes, em fila: ~8 s → ~5 s).
        // Sem barra de progresso própria (a barra segue o elements); o cancelamento vale para todos.
        // Medindo memória (--load-bench mem) ficam em fila, senão a conta de cada etapa se mistura.
        var quiet = new LoadContext(null, context.Token);
        string? atlasPath = preserveSession ? previousAtlasPath : IconAtlas.FindSurfacesPck(path);
        string near = Path.Combine(Path.GetDirectoryName(path) ?? "", "tasks.data");
        T? Side<T>(string name, Func<T?> load, string failKey, string failPt) where T : class
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            try { return load(); }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Enqueue(Loc.T(failKey, failPt) + ex.Message); return null; }
            finally { times.Enqueue($"{name} {t0.ElapsedMilliseconds} ms (paralelo)"); }
        }
        Func<IconAtlas?> loadAtlas = () => preserveSession ? previousAtlas : atlasPath == previousAtlasPath && previousAtlas is not null ? previousAtlas : atlasPath is null ? null : IconAtlas.Load(atlasPath, quiet);
        Func<GameConfigs?> loadConfigs = () => preserveSession ? previousConfigs : GameConfigs.FindConfigsPck(path) is { } cp ? GameConfigs.Load(cp, quiet) : null;
        Func<TasksFile?> loadTasks = () => preserveSession ? previousTasks : !File.Exists(near) ? null
            : near == previousTasksPath && previousTasks is not null ? previousTasks : new TasksFile(near, quiet);
        var tAtlas = MemTrace ? null : Task.Run(() => Side("icones", loadAtlas, "load.iconsFail", "Ícones não carregados: "));
        var tConfigs = MemTrace ? null : Task.Run(() => Side("configs", loadConfigs, "load.descriptionsFail", "Descrições não carregadas: "));
        var tTasks = MemTrace ? null : Task.Run(() => Side("tasks", loadTasks, "load.tasksFail", "tasks.data da mesma pasta não carregado: "));

        var match = CfgCatalog.Load(path, cfgOverride, context: context.Slice(0, .60));
        Lap("elements");
        var module = match.Module;
        context.Report(.60, Loc.T("load.indexIds", "Indexando IDs"), force: true);
        var refs = new RefIndex(module, context.Slice(.60, .64));
        context.Report(.64, Loc.T("load.indexRefs", "Indexando referências"), force: true);
        Lap("ids");
        refs.Prepare(context.Slice(.64, .82));
        Lap("refs");
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
        Lap("listas");

        // o que ainda falta dos paralelos (em geral só o tasks.data)
        context.Report(.86, Loc.T("load.side", "Lendo ícones, descrições e quests"), Loc.T("load.iconsDetail", "pacotes grandes; ainda dá para cancelar"), indeterminate: true, force: true);
        var atlas = tAtlas is null ? Side("icones", loadAtlas, "load.iconsFail", "Ícones não carregados: ") : tAtlas.GetAwaiter().GetResult();
        if (MemTrace) Lap("icones");
        var configs = tConfigs is null ? Side("configs", loadConfigs, "load.descriptionsFail", "Descrições não carregadas: ") : tConfigs.GetAwaiter().GetResult();
        if (MemTrace) Lap("configs");
        var tasks = tTasks is null ? Side("tasks", loadTasks, "load.tasksFail", "tasks.data da mesma pasta não carregado: ") : tTasks.GetAwaiter().GetResult();
        if (MemTrace) Lap("tasks");
        string? taskPath = preserveSession ? previousTasksPath : tasks is null ? null : near;
        Lap("espera");
        LastTimes = string.Join(" · ", times);
        AppLog.Info("abertura: " + LastTimes);
        context.Check();
        int taskCount = tasks?.Index.Count ?? 0;
        context.Report(.99, Loc.T("load.ready", "Dados prontos"), Loc.T("load.readyDetail", "trocando para os novos dados…"), force: true);
        return new ElementsLoadBundle(match, refs, owners, atlas, atlasPath, configs, tasks, taskPath, taskCount, warnings.ToList());
    }
}
