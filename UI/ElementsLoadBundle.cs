using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>只构造数据，不访问控件。成功返回后由界面线程统一接管。</summary>
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
        context.Report(.60, "建立 ID 索引", force: true);
        var refs = new RefIndex(module, context.Slice(.60, .64));
        context.Report(.64, "建立引用索引", force: true);
        refs.Prepare(context.Slice(.64, .82));
        context.Report(.82, "整理数据列表", force: true);
        int total = module.Lists.Sum(l => l.Items.Count), done = 0;
        var owners = new Dictionary<Item, ItemList>(total, ReferenceEqualityComparer.Instance);
        foreach (var list in module.Lists)
            foreach (var item in list.Items)
            {
                if ((done++ & 255) == 0)
                    context.Slice(.82, .85).Report(done / (double)Math.Max(1, total), "整理数据列表", $"{done:N0}/{total:N0} 条记录");
                owners[item] = list;
            }
        var initial = module.FindListByName(preferredBlock ?? "WEAPON_ESSENCE") ?? module.Lists.FirstOrDefault();
        var initialKeys = initial?.Items.FirstOrDefault()?.Fields.Keys.ToArray() ?? Array.Empty<string>();
        for (int i = 0; i < initialKeys.Length; i++)
        {
            context.Check();
            if ((i & 127) == 0) context.Slice(.85, .86).Report(i / (double)Math.Max(1, initialKeys.Length), "准备中文字段", $"{i:N0}/{initialKeys.Length:N0} 个字段");
            FieldLabels.Label(initialKeys[i]);
        }
        var warnings = new List<string>();
        IconAtlas? atlas = null; GameConfigs? configs = null; TasksFile? tasks = null;
        string? atlasPath = preserveSession ? previousAtlasPath : IconAtlas.FindSurfacesPck(path), taskPath = null;
        context.Report(.86, "读取图标资源", "大资源包处理期间窗口仍可取消", indeterminate: true, force: true);
        try { atlas = preserveSession ? previousAtlas : atlasPath == previousAtlasPath && previousAtlas is not null ? previousAtlas : atlasPath is null ? null : IconAtlas.Load(atlasPath, context.Slice(.86, .91)); }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add("图标资源未加载：" + ex.Message); }
        context.Check();
        context.Report(.91, "读取物品描述", "正在加载游戏资源中的颜色、描述和附加属性", indeterminate: true, force: true);
        try { configs = preserveSession ? previousConfigs : GameConfigs.FindConfigsPck(path) is { } cp ? GameConfigs.Load(cp, context.Slice(.91, .95)) : null; }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add("描述资源未加载：" + ex.Message); }
        context.Check();
        context.Report(.95, "读取同目录任务", force: true);
        string near = Path.Combine(Path.GetDirectoryName(path) ?? "", "tasks.data");
        if (preserveSession) { tasks = previousTasks; taskPath = previousTasksPath; }
        else if (File.Exists(near))
        {
            try
            {
                tasks = near == previousTasksPath && previousTasks is not null ? previousTasks : new TasksFile(near, context.Slice(.95, .99));
                taskPath = near;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { warnings.Add("同目录任务未加载：" + ex.Message); }
        }
        context.Check();
        int taskCount = tasks?.Index.Count ?? 0;
        context.Report(.99, "数据准备完成", "正在切换到新数据…", force: true);
        return new ElementsLoadBundle(match, refs, owners, atlas, atlasPath, configs, tasks, taskPath, taskCount, warnings);
    }
}
