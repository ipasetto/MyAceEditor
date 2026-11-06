namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Atlas Geográfico (item 12758, TASKDICE que dá a quest 6940). A quest tem grupos de subquests "Encontrar ...", em ordem e de
/// espera zero: ler o Atlas conclui todas de uma vez, e o prêmio m_ulNewRelayStation de cada uma ativa um ponto
/// (TaskProcess SetNewRelayStation). Pôr um ponto no Atlas = mais uma subquest dessas, copiada de uma que já existe.
/// </summary>
public static class TeleportAtlas
{
    public const int AtlasItem = 12758;
    const string Station = "m_Award_S.m_ulNewRelayStation";

    static TaskRec Rec(QuestNode q, int version) => TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(q.Fixed, false)), version);

    static void Write(QuestNode q, TaskRec rec, int version)
    {
        using var ms = new MemoryStream();
        TaskLayout.WriteFixed(new BinaryWriter(ms), version, rec);
        q.Fixed = ms.ToArray();
        q.ResetFields(version);
    }

    /// <summary>Subquests que dão ponto, abaixo da raiz.</summary>
    static IEnumerable<QuestNode> Leaves(QuestNode root) => root.Children.Count == 0 ? [root] : root.Children.SelectMany(Leaves);

    public static QuestNode? Find(TasksFile t, int rootId, int point) =>
        t.Index.Find(rootId) is QuestNode root ? Leaves(root).FirstOrDefault(q => Rec(q, t.Version).Int(Station) == point) : null;

    /// <summary>Cria a subquest do ponto; null se a quest do Atlas não existe ou não tem um modelo para copiar.</summary>
    public static QuestNode? Add(TasksFile t, int rootId, string name, int point)
    {
        if (t.Index.Find(rootId) is not QuestNode root) return null;
        var proto = Leaves(root).LastOrDefault(q => Rec(q, t.Version).Int(Station) > 0);
        if (proto?.Parent is not { } group) return null;
        var last = group.Children[^1];
        var q = t.AddChild(group, proto, t.NextFreeId());
        var title = "^ffffffEncontrar " + name;
        q.Name = title.Length > TasksFile.MaxNameLength ? title[..TasksFile.MaxNameLength] : title;   // os do jogo também saem cortados
        var rec = Rec(q, t.Version);
        rec.V["m_ulParent"] = (long)group.Id; rec.V["m_ulPrevSibling"] = (long)last.Id; rec.V["m_ulNextSibling"] = 0L; rec.V["m_ulFirstChild"] = 0L;
        rec.V[Station] = (long)point;
        Write(q, rec, t.Version);
        var before = Rec(last, t.Version);
        before.V["m_ulNextSibling"] = (long)q.Id;
        Write(last, before, t.Version);
        return q;
    }

    /// <summary>Tira a subquest e religa os vizinhos.</summary>
    public static void Remove(TasksFile t, QuestNode q)
    {
        if (q.Parent is not { } group) return;
        int at = group.Children.IndexOf(q);
        var prev = at > 0 ? group.Children[at - 1] : null;
        var next = at + 1 < group.Children.Count ? group.Children[at + 1] : null;
        if (prev is not null) { var r = Rec(prev, t.Version); r.V["m_ulNextSibling"] = (long)(next?.Id ?? 0); Write(prev, r, t.Version); }
        if (next is not null) { var r = Rec(next, t.Version); r.V["m_ulPrevSibling"] = (long)(prev?.Id ?? 0); Write(next, r, t.Version); }
        if (prev is null) { var r = Rec(group, t.Version); r.V["m_ulFirstChild"] = (long)(next?.Id ?? 0); Write(group, r, t.Version); }
        t.RemoveChild(group, q);
    }

    /// <summary>Devolve a subquest removida ao lugar dela (Ctrl+Z).</summary>
    public static void Restore(TasksFile t, QuestNode group, QuestNode q, int at)
    {
        t.InsertChild(group, q, at);
        var prev = at > 0 ? group.Children[at - 1] : null;
        var next = at + 1 < group.Children.Count ? group.Children[at + 1] : null;
        if (prev is not null) { var r = Rec(prev, t.Version); r.V["m_ulNextSibling"] = (long)q.Id; Write(prev, r, t.Version); }
        if (next is not null) { var r = Rec(next, t.Version); r.V["m_ulPrevSibling"] = (long)q.Id; Write(next, r, t.Version); }
        if (prev is null) { var r = Rec(group, t.Version); r.V["m_ulFirstChild"] = (long)q.Id; Write(group, r, t.Version); }
    }
}
