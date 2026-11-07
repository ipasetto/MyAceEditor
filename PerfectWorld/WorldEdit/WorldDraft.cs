using System.Numerics;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

public enum ObjKind { Building, Model, Tree, Light, Effect, Sound, Critter }

/// <summary>Um objeto do mundo no rascunho: como estava no arquivo (Original) e como está agora (Now).</summary>
public sealed class DraftObject
{
    /// <summary>Chave estável: caminho do .bmd no pacote, ou "ecwld:{Kind}:{id}" para itens do .ecwld.</summary>
    public required string Key { get; init; }
    public ObjKind Kind { get; init; }
    public int Piece { get; init; }
    /// <summary>De onde veio (arquivo do .bmd, .ecm do modelo, .gfx do efeito...), para o cartão de informações.</summary>
    public string Source { get; init; } = "";
    public ObjTransform Original { get; init; }
    ObjTransform? _now;
    public ObjTransform Now { get => _now ?? Original; set => _now = value; }
    public bool Deleted { get; set; }
    /// <summary>Tem colisão no servidor (brushes no map.bht).</summary>
    public bool Solid { get; init; }
    /// <summary>Caixa do desenho no espaço do próprio objeto (para tamanho e contorno).</summary>
    public (Vector3 Min, Vector3 Max) LocalBox { get; init; }
    /// <summary>Motivo de não poder mexer (null = pode).</summary>
    public string? ReadOnlyWhy { get; set; }
    public bool Changed => Deleted || Now != Original;
}

/// <summary>Um pedaço de 1 km do mapa no rascunho (número = WorldTerrain.PieceAt).</summary>
public sealed class DraftPiece(int id)
{
    public int Id { get; } = id;
    public bool Loaded { get; set; }
    /// <summary>Motivo de o pedaço inteiro ser só para olhar (arquivo ruim etc.).</summary>
    public string? ReadOnlyWhy { get; set; }
    public List<DraftObject> Objects { get; init; } = new();
    /// <summary>Objetos novos (etapa 2 em diante).</summary>
    public List<DraftObject> Added { get; init; } = new();
    public bool Dirty => Objects.Any(o => o.Changed) || Added.Count > 0;
}

public abstract record DraftOp(string Key);
public sealed record MoveOp(string Key, ObjTransform To) : DraftOp(Key);
public sealed record DeleteOp(string Key) : DraftOp(Key);
public sealed record RestoreOp(string Key) : DraftOp(Key);

/// <summary>
/// Rascunho da ferramenta Objetos do mundo: os pedaços abertos do mapa e as mudanças, cada uma com o seu desfazer
/// (só a diferença do objeto). Nada aqui grava arquivo: o DraftSaver monta os arquivos no Salvar.
/// </summary>
public sealed class WorldDraft(string map)
{
    readonly record struct Step(string Key, ObjTransform BeforeAt, bool BeforeDeleted, ObjTransform AfterAt, bool AfterDeleted, object? Tag);

    readonly Dictionary<int, DraftPiece> _pieces = new();
    readonly Dictionary<string, DraftObject> _byKey = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Step> _undo = new(), _redo = new();

    public string Map { get; } = map;
    public IReadOnlyDictionary<int, DraftPiece> Pieces => _pieces;
    public bool Dirty => _pieces.Values.Any(p => p.Dirty);
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public event Action? Changed;
    /// <summary>Marca guardada com o passo do topo (a tela usa para a ordem do Ctrl+Z entre ferramentas).</summary>
    public object? UndoTag => _undo.Count > 0 ? _undo[^1].Tag : null;
    public object? RedoTag => _redo.Count > 0 ? _redo[^1].Tag : null;

    public DraftObject? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Entra (ou troca) um pedaço lido. Um pedaço com mudança não é trocado.</summary>
    public void Put(DraftPiece piece)
    {
        if (_pieces.TryGetValue(piece.Id, out var old))
        {
            if (old.Dirty) return;
            foreach (var o in old.Objects.Concat(old.Added)) _byKey.Remove(o.Key);
        }
        _pieces[piece.Id] = piece;
        foreach (var o in piece.Objects.Concat(piece.Added))
        {
            if (piece.ReadOnlyWhy is not null) o.ReadOnlyWhy ??= piece.ReadOnlyWhy;
            _byKey[o.Key] = o;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Solta os pedaços que não estão em <paramref name="wanted"/> e não têm mudança (pedaço sujo fica até o Salvar);
    /// os passos de desfazer/refazer dos objetos soltos saem junto (não teriam mais em quem agir).
    /// </summary>
    public void Retain(IReadOnlyCollection<int> wanted)
    {
        var drop = _pieces.Values.Where(p => !wanted.Contains(p.Id) && !p.Dirty).ToList();
        if (drop.Count == 0) return;
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in drop)
        {
            _pieces.Remove(p.Id);
            foreach (var o in p.Objects.Concat(p.Added)) { _byKey.Remove(o.Key); gone.Add(o.Key); }
        }
        _undo.RemoveAll(s => gone.Contains(s.Key));
        _redo.RemoveAll(s => gone.Contains(s.Key));
        Changed?.Invoke();
    }

    /// <summary>Aplica a mudança. null = aplicada; senão, o motivo da recusa em palavras simples (nada muda).</summary>
    public string? Apply(DraftOp op, object? tag = null)
    {
        if (Find(op.Key) is not { } o) return Loc.T("obj.notFound", "Esse objeto não está mais carregado.");
        if (o.ReadOnlyWhy is not null) return o.ReadOnlyWhy;
        var before = (o.Now, o.Deleted);
        switch (op)
        {
            case MoveOp m:
                if (o.Deleted) return Loc.T("obj.isDeleted", "Esse objeto foi apagado; desfaça (Ctrl+Z) para mexer nele.");
                if (Invalid(m.To) is { } bad) return bad;
                if (m.To == o.Now) return null;
                o.Now = m.To; break;
            case DeleteOp:
                if (o.Deleted) return null;
                o.Deleted = true; break;
            case RestoreOp:
                if (!o.Deleted) return null;
                o.Deleted = false; break;
            default: return Loc.T("obj.unknownOp", "Mudança desconhecida.");
        }
        _undo.Add(new Step(o.Key, before.Now, before.Deleted, o.Now, o.Deleted, tag));
        _redo.Clear();
        Changed?.Invoke();
        return null;
    }

    public bool Undo() => Move(_undo, _redo, back: true);
    public bool Redo() => Move(_redo, _undo, back: false);

    bool Move(List<Step> from, List<Step> to, bool back)
    {
        if (from.Count == 0) return false;
        var s = from[^1]; from.RemoveAt(from.Count - 1);
        if (Find(s.Key) is { } o)
        {
            o.Now = back ? s.BeforeAt : s.AfterAt;
            o.Deleted = back ? s.BeforeDeleted : s.AfterDeleted;
        }
        to.Add(s);
        Changed?.Invoke();
        return true;
    }

    static string? Invalid(ObjTransform t)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        if (!Finite(t.Pos) || !Finite(t.Dir) || !Finite(t.Up)) return Loc.T("obj.badNumber", "Número inválido na posição ou no giro.");
        if (!(t.Scale > 0 && t.Scale < 1000)) return Loc.T("obj.badScale", "A escala tem que ser maior que zero.");
        if (t.Dir.LengthSquared() < 1e-6f || t.Up.LengthSquared() < 1e-6f) return Loc.T("obj.badTurn", "Giro inválido (direção zerada).");
        if (MathF.Abs(t.Pos.X) > 1e6f || MathF.Abs(t.Pos.Y) > 1e6f || MathF.Abs(t.Pos.Z) > 1e6f) return Loc.T("obj.farAway", "Posição longe demais do mapa.");
        return null;
    }
}
