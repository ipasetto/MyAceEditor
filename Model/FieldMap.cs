using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace MyAceEditor.Model;

/// <summary>Nomes dos campos de um bloco, na ordem do arquivo, e a posição de cada um. Uma instância por bloco.</summary>
public sealed class FieldSchema
{
    internal readonly List<string> Keys = new();
    internal readonly Dictionary<string, int> Index = new();
    public FieldSchema() { }
    public FieldSchema(IEnumerable<string> keys) { foreach (var k in keys) Add(k); }
    internal int Add(string key) { int i = Keys.Count; Index.Add(key, i); Keys.Add(key); return i; }
    internal FieldSchema Copy() { var s = new FieldSchema(); foreach (var k in Keys) s.Add(k); return s; }
}

/// <summary>
/// Campos de um registro, com o mesmo comportamento do Dictionary&lt;string, Field&gt; de antes (ordem de inserção,
/// TryGetValue, indexador, Keys, Remove…), mas guardando só um vetor de valores: os nomes ficam num
/// <see cref="FieldSchema"/> compartilhado pelos registros do bloco. Antes cada registro tinha o próprio dicionário
/// (~20 bytes a mais por campo; ~9 milhões de campos num elements 1.5.5). Campo novo que o bloco não tem: o registro
/// passa a ter a própria cópia dos nomes (não muda os outros).
/// </summary>
public sealed class FieldMap : IDictionary<string, Field>, IReadOnlyDictionary<string, Field>
{
    FieldSchema _schema;
    bool _own;
    Field?[] _v;
    int _count;

    public FieldMap() { _schema = new FieldSchema(); _own = true; _v = new Field?[8]; }
    public FieldMap(FieldSchema shared) { _schema = shared; _v = new Field?[shared.Keys.Count]; }
    public FieldMap(IEnumerable<KeyValuePair<string, Field>> items) : this() { foreach (var kv in items) Add(kv.Key, kv.Value); }

    /// <summary>Cópia com campos novos (mesmo tipo e valor), que continua usando os nomes do mesmo bloco.</summary>
    public FieldMap Clone()
    {
        var c = new FieldMap(_own ? _schema.Copy() : _schema) { _own = _own };
        if (c._v.Length < _v.Length) c._v = new Field?[_v.Length];
        for (int i = 0; i < _v.Length; i++) if (_v[i] is { } f) c._v[i] = Field.Of(f.Type, f.Value);
        c._count = _count;
        return c;
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    int Slot(string key) => _schema.Index.TryGetValue(key, out int i) && i < _v.Length && _v[i] is not null ? i : -1;

    public bool ContainsKey(string key) => Slot(key) >= 0;

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out Field value)
    {
        int i = Slot(key);
        value = i >= 0 ? _v[i]! : null;
        return i >= 0;
    }

    public Field this[string key]
    {
        get => TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"Campo '{key}' não existe.");
        set => Put(key, value ?? throw new ArgumentNullException(nameof(value)), replace: true);
    }

    public void Add(string key, Field value) => Put(key, value ?? throw new ArgumentNullException(nameof(value)), replace: false);

    void Put(string key, Field value, bool replace)
    {
        if (!_schema.Index.TryGetValue(key, out int i))
        {
            if (!_own) { _schema = _schema.Copy(); _own = true; }
            i = _schema.Add(key);
        }
        if (i >= _v.Length) Array.Resize(ref _v, Math.Max(i + 1, _v.Length * 2));
        if (_v[i] is not null)
        {
            if (!replace) throw new ArgumentException($"Campo '{key}' repetido.", nameof(key));
        }
        else _count++;
        _v[i] = value;
    }

    public bool Remove(string key)
    {
        int i = Slot(key);
        if (i < 0) return false;
        _v[i] = null; _count--;
        return true;
    }

    public void Clear() { Array.Clear(_v); _count = 0; }

    public IEnumerator<KeyValuePair<string, Field>> GetEnumerator()
    {
        var keys = _schema.Keys;
        for (int i = 0; i < _v.Length && i < keys.Count; i++)
            if (_v[i] is { } f) yield return new KeyValuePair<string, Field>(keys[i], f);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public ICollection<string> Keys => this.Select(kv => kv.Key).ToList();
    public ICollection<Field> Values => this.Select(kv => kv.Value).ToList();
    IEnumerable<string> IReadOnlyDictionary<string, Field>.Keys => this.Select(kv => kv.Key);
    IEnumerable<Field> IReadOnlyDictionary<string, Field>.Values => this.Select(kv => kv.Value);

    public void Add(KeyValuePair<string, Field> item) => Add(item.Key, item.Value);
    public bool Contains(KeyValuePair<string, Field> item) => TryGetValue(item.Key, out var v) && ReferenceEquals(v, item.Value);
    public bool Remove(KeyValuePair<string, Field> item) => Contains(item) && Remove(item.Key);
    public void CopyTo(KeyValuePair<string, Field>[] array, int arrayIndex) { foreach (var kv in this) array[arrayIndex++] = kv; }
}
