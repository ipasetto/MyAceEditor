using MyAceEditor.Model;

namespace MyAceEditor.Changes;

/// <summary>
/// Desfazer/refazer das edições feitas na tela (campo, item novo/clonado/excluído, alterações em lote).
/// Toda mudança passa por aqui; um "passo" pode agrupar várias (Batch), e desfazer volta todas juntas.
/// Restaura o Field original, então o gravador reproduz os bytes originais do registro.
/// </summary>
public sealed class EditHistory
{
    public const int MaxSteps = 5000;

    internal abstract record Op;
    internal sealed record FieldOp(Item Item, string Key, Field Old, Field New) : Op;
    internal sealed record ListOp(ItemList Block, Item Item, int Index, bool Added) : Op;
    /// <summary>Mudança fora do elements.data (quests, loja): o passo guarda como refazer e como desfazer.</summary>
    internal sealed record CustomOp(Action Redo, Action Undo) : Op;

    public sealed class Step(string label)
    {
        public string Label { get; } = label;
        public DateTime When { get; } = DateTime.Now;
        internal List<Op> Ops { get; } = new();
        /// <summary>Itens tocados pelo passo, com o bloco (para a tela sincronizar dono e seleção).</summary>
        public IEnumerable<(ItemList? Block, Item Item)> Touched =>
            Ops.Select(o => o switch { ListOp l => (l.Block, l.Item), FieldOp f => ((ItemList?)null, f.Item), _ => default }).Where(t => t.Item is not null);
        public int Count => Ops.Count;
        /// <summary>O passo troca algum ID (a tela refaz o índice de referências).</summary>
        public bool ChangesIds => Ops.Any(o => o is FieldOp { Key: "ID" });
    }

    readonly List<Step> _undo = new(), _redo = new();
    Step? _open;
    int _depth;

    public event Action? Changed;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;
    /// <summary>Passos que dá para desfazer, do mais recente para o mais antigo.</summary>
    public IReadOnlyList<Step> UndoSteps => Enumerable.Reverse(_undo).ToList();
    public IReadOnlyList<Step> RedoSteps => Enumerable.Reverse(_redo).ToList();

    public void Clear() { _undo.Clear(); _redo.Clear(); _open = null; _depth = 0; Changed?.Invoke(); }

    /// <summary>Agrupa as mudanças até o Dispose num só passo (lotes aninhados entram no de fora).</summary>
    public IDisposable Batch(string label)
    {
        if (_depth++ == 0) _open = new Step(label);
        return new End(this);
    }

    sealed class End(EditHistory h) : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--h._depth == 0 && h._open is { } s) { h._open = null; h.Push(s); }
        }
    }

    void Record(Op op, string label)
    {
        if (_open is not null) { _open.Ops.Add(op); return; }
        var s = new Step(label);
        s.Ops.Add(op);
        Push(s);
    }

    void Push(Step s)
    {
        if (s.Ops.Count == 0) return;
        _undo.Add(s);
        if (_undo.Count > MaxSteps) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void SetField(Item item, string key, Field value, string label)
    {
        var old = item.Fields.TryGetValue(key, out var o) ? o : Field.Of(value.Type, "");
        if (old.Type == value.Type && old.Value == value.Value) return;
        Assign(item, key, value);
        Record(new FieldOp(item, key, old, value), label);
    }

    public void AddItem(ItemList block, Item item, string label)
    {
        block.Items.Add(item);
        Record(new ListOp(block, item, block.Items.Count - 1, Added: true), label);
    }

    public void RemoveItem(ItemList block, Item item, string label)
    {
        int i = block.Items.IndexOf(item);
        if (i < 0) return;
        block.Items.RemoveAt(i);
        Record(new ListOp(block, item, i, Added: false), label);
    }

    /// <summary>Aplica agora e registra uma mudança fora dos campos do elements.data (ex.: IDs em quests e na loja).</summary>
    public void Custom(string label, Action redo, Action undo)
    {
        redo();
        Record(new CustomOp(redo, undo), label);
    }

    static void Assign(Item item, string key, Field value)
    {
        item.Fields[key] = value;
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) item.Name = value.Value;
        if (key.Equals("ID", StringComparison.OrdinalIgnoreCase)) item.Id = value.Value;
    }

    static void Apply(Op op, bool forward)
    {
        switch (op)
        {
            case FieldOp f: Assign(f.Item, f.Key, forward ? f.New : f.Old); break;
            case ListOp l when l.Added == forward:   // refazer inclusão / desfazer exclusão
                l.Block.Items.Insert(Math.Min(l.Index, l.Block.Items.Count), l.Item); break;
            case ListOp l: l.Block.Items.Remove(l.Item); break;
            case CustomOp c: (forward ? c.Redo : c.Undo)(); break;
        }
    }

    public Step? Undo()
    {
        if (_undo.Count == 0) return null;
        var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        for (int i = s.Ops.Count - 1; i >= 0; i--) Apply(s.Ops[i], forward: false);
        _redo.Add(s);
        Changed?.Invoke();
        return s;
    }

    public Step? Redo()
    {
        if (_redo.Count == 0) return null;
        var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        foreach (var op in s.Ops) Apply(op, forward: true);
        _undo.Add(s);
        Changed?.Invoke();
        return s;
    }
}
