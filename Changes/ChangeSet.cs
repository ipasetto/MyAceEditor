using MyAceEditor.Model;

namespace MyAceEditor.Changes;

/// <summary>
/// O pilar mais importante do design: a IA NUNCA edita direto.
/// Cada operação "preview_*" vira um Change aqui. Nada toca o módulo real
/// até apply_change_set(). discard_change_set() joga tudo fora.
/// </summary>
public sealed class ChangeSet
{
    private readonly List<IChange> _pending = new();

    public IReadOnlyList<IChange> Pending => _pending;
    public bool HasPending => _pending.Count > 0;

    public ChangePreview Stage(IChange change)
    {
        _pending.Add(change);
        return new ChangePreview(_pending.Count, change.Describe());
    }

    /// <summary>Aplica todas as mudanças pendentes ao módulo, em ordem.</summary>
    public ApplyResult Apply(Module module)
    {
        var applied = new List<string>();
        foreach (var c in _pending)
        {
            c.ApplyTo(module);
            applied.Add(c.Describe());
        }
        _pending.Clear();
        return new ApplyResult(applied.Count, applied);
    }

    public int Discard()
    {
        var n = _pending.Count;
        _pending.Clear();
        return n;
    }
}

public interface IChange
{
    string Op { get; }   // "add" | "set" | "rem" — usado pela UI para colorir a operação
    string Describe();
    void ApplyTo(Module module);
}

public sealed record ChangePreview(int PendingCount, string Description);
public sealed record ApplyResult(int AppliedCount, IReadOnlyList<string> Applied);

// ---- Implementações concretas de mudança (adicione mais conforme precisar) ----

public sealed class AddItemChange(string listName, Item item) : IChange
{
    public string Op => "add";
    public string Describe() => $"add item '{item.Name}' (id {item.Id}) to list '{listName}'";
    public void ApplyTo(Module module)
    {
        var list = module.FindListByName(listName)
                   ?? throw new InvalidOperationException($"list '{listName}' not found");
        list.Items.Add(item);
    }
}

public sealed class RemoveItemChange(string listName, string itemId) : IChange
{
    public string Op => "rem";
    public string Describe() => $"remove item {itemId} from list '{listName}'";
    public void ApplyTo(Module module)
    {
        var list = module.FindListByName(listName)
                   ?? throw new InvalidOperationException($"list '{listName}' not found");
        list.Items.RemoveAll(i => i.Id == itemId);
    }
}

public sealed class SetFieldsChange(string listName, string itemId, Dictionary<string, Field> fields) : IChange
{
    public string Op => "set";
    public string Describe() => $"set {fields.Count} field(s) on item {itemId} in '{listName}'";
    public void ApplyTo(Module module)
    {
        var item = module.FindListByName(listName)?.FindById(itemId)
                   ?? throw new InvalidOperationException($"item {itemId} not found in '{listName}'");
        foreach (var (k, v) in fields) item.Fields[k] = v;
    }
}
