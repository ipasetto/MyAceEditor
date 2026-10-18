namespace MyAceEditor.PerfectWorld;

/// <summary>一次建立任务 ID 和搜索索引；名称从原模型读取，改名后无需重建。</summary>
public sealed class TaskIndex
{
    private readonly Dictionary<int, object> _byId = new();
    private readonly object[] _roots, _all;
    public IReadOnlyList<QuestNode> Nodes { get; }
    public int Count => _all.Length;

    public TaskIndex(IReadOnlyList<QuestNode>? roots, IReadOnlyList<QuestEntry> names, LoadContext? context = null)
    {
        context?.Check();
        context?.Report(0, "建立任务索引", force: true);
        var rows = new List<object>();
        var nodes = new List<QuestNode>();
        if (roots is null) rows.AddRange(names);
        else
        {
            var pending = new Stack<QuestNode>();
            var seen = new HashSet<QuestNode>(ReferenceEqualityComparer.Instance);
            for (int i = roots.Count - 1; i >= 0; i--) pending.Push(roots[i]);
            while (pending.Count > 0)
            {
                context?.Check();
                var node = pending.Pop();
                if (!seen.Add(node)) continue;
                nodes.Add(node); rows.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--) pending.Push(node.Children[i]);
            }
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if ((i & 127) == 0) context?.Report(i / (double)Math.Max(1, rows.Count), "建立任务索引", $"{i:N0}/{rows.Count:N0} 个任务");
            _byId.TryAdd(IdOf(rows[i]), rows[i]);
        }
        Nodes = nodes.ToArray();
        _roots = (roots is null ? names.Cast<object>() : roots.Cast<object>()).OrderBy(IdOf).ToArray();
        _all = rows.OrderBy(IdOf).ToArray();
        context?.Check();
        context?.Report(1, "建立任务索引", $"{rows.Count:N0} 个任务", force: true);
    }

    public object? Find(int id) => _byId.GetValueOrDefault(id);
    public static int IdOf(object row) => row switch { QuestNode node => node.Id, QuestEntry entry => entry.Id, _ => 0 };
    public static string NameOf(object row) => row switch { QuestNode node => node.Name, QuestEntry entry => entry.Name, _ => "" };

    public object[] Search(string query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (query.Length == 0) return _roots;
        var result = new List<object>();
        for (int i = 0; i < _all.Length; i++)
        {
            if ((i & 127) == 0) token.ThrowIfCancellationRequested();
            var row = _all[i];
            if (NameOf(row).Contains(query, StringComparison.OrdinalIgnoreCase) || IdOf(row).ToString().Contains(query)) result.Add(row);
        }
        token.ThrowIfCancellationRequested();
        return result.ToArray();
    }
}
