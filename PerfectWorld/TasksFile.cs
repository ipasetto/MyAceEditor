using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>Uma quest de topo do tasks.data (cabeçalho: ID + nome) — usado quando o esquema completo não existe.</summary>
public sealed class QuestEntry
{
    public int Offset { get; init; }
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string OriginalName { get; internal set; } = "";
    public bool Changed => Name != OriginalName;
}

/// <summary>
/// tasks.data (PW-KB §S21): u32 magic 0x93858361 | u32 versão | u32 n | n×u32 offset | corpos.
/// Versão 93: esquema completo (<see cref="TaskSchema93"/>, nomes em português); demais versões conhecidas até v222: bloco
/// fixo pelo <see cref="TaskLayout"/> (chaves m_…) — árvore, campos, textos e diálogos, gravação reconstruindo a tabela de
/// offsets. Outras versões: só ID/nome das quests de topo.
/// Ver ENGINE-KB §E3c.
/// </summary>
public sealed class TasksFile
{
    public const uint Magic = 0x93858361;
    const int NameChars = 30;

    public byte[] Data { get; private set; }
    public int Version { get; }
    public List<QuestEntry> Quests { get; } = new();
    public List<QuestNode>? Roots { get; }
    public string? SchemaError { get; }
    public bool FullSchema => Roots is not null;
    public TaskIndex Index { get; private set; }
    public bool RoundTripVerified { get; }
    public string? ReadOnlyReason { get; private set; }
    public bool CanWrite => RoundTripVerified && ReadOnlyReason is null;

    public TasksFile(string path, LoadContext? context = null)
        : this(context is null ? File.ReadAllBytes(path) : context.Slice(0, .10).ReadFile(path), context?.Slice(.10, 1)) { }

    public TasksFile(byte[] data, LoadContext? context = null)
    {
        context?.Check();
        Data = data;
        if (data.Length < 12 || BitConverter.ToUInt32(data, 0) is not (Magic or 0))
            throw new InvalidDataException(FieldKinds.Translate("tasks.badSignature", "Não é um tasks.data (assinatura inválida)."));
        Version = BitConverter.ToInt32(data, 4);
        int n = BitConverter.ToInt32(data, 8);
        if (n < 0 || n > (data.Length - 12) / 4) throw new InvalidDataException($"Contagem de quests inválida ({n}).");
        var offsets = new int[n];
        for (int i = 0; i < n; i++)
        {
            if ((i & 255) == 0) context?.Check();
            offsets[i] = BitConverter.ToInt32(data, 12 + i * 4);
        }

        // Mesmo no modo só-nomes, um cabeçalho de quest não pode invadir a tabela de offsets nem outro cabeçalho.
        int headerEnd = 12 + n * 4;
        int[] sortedOffsets = offsets.Order().ToArray();
        for (int i = 0; i < sortedOffsets.Length; i++)
        {
            int off = sortedOffsets[i];
            if (off < headerEnd || off > data.Length - (4 + NameChars * 2)
                || i > 0 && (long)off < (long)sortedOffsets[i - 1] + 4 + NameChars * 2)
                ReadOnlyReason = FieldKinds.Translate("tasks.badOffsets", "A tabela de offsets das quests tem cabeçalhos fora do arquivo, repetidos ou sobrepostos; aberto só para leitura.");
        }

        for (int i = 0; i < n; i++)
        {
            if ((i & 127) == 0) context?.Report(.1 * i / Math.Max(1, n), FieldKinds.Translate("tasks.readingNames", "Lendo nomes das quests"), string.Format(FieldKinds.Translate("load.quests", "{0:N0}/{1:N0} quests"), i, n));
            int off = offsets[i];
            if (off < headerEnd || off > data.Length - (4 + NameChars * 2)) continue;
            int idAt = Version >= 153 ? 2 : 0;   // v153+: i16 do espaço de IDs antes do ID
            if (off + idAt + 4 + NameChars * 2 > data.Length) continue;
            int id = BitConverter.ToInt32(data, off + idAt);
            var name = TaskSchema93.Xor(data, off + idAt + 4, NameChars, id);
            Quests.Add(new QuestEntry { Offset = off, Id = id, Name = name, OriginalName = name });
        }

        if (TaskSchema93.Supports(Version))
        {
            try
            {
                var roots = new List<QuestNode>(n);
                for (int i = 0; i < n; i++)
                {
                    context?.Report(.1 + .82 * i / Math.Max(1, n), FieldKinds.Translate("tasks.parsing", "Analisando quests"), string.Format(FieldKinds.Translate("tasks.mainCount", "{0:N0}/{1:N0} quests principais"), i, n));
                    var schema = new TaskSchema93(data, context?.Token ?? default, Version);
                    var q = schema.Read(offsets[i]);
                    int end = schema.Position, expected = i + 1 < n ? offsets[i + 1] : data.Length;
                    if (end != expected)
                        throw new InvalidDataException($"quest {q.Id} terminou em {end:N0}, esperado {expected:N0}");
                    roots.Add(q);
                }
                Roots = roots;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { SchemaError = ex.Message; }
        }
        context?.Check();
        Index = new TaskIndex(Roots, Quests, context?.Slice(.92, .95));
        if (ReadOnlyReason is null)
        {
            try
            {
                RoundTripVerified = BuildCore(context?.Slice(.95, 1)).AsSpan().SequenceEqual(Data);
                if (!RoundTripVerified) ReadOnlyReason = FieldKinds.Translate("tasks.rewriteDiffers", "A regravação das quests sem alterações difere do original; aberto só para leitura.");
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
            { ReadOnlyReason = FieldKinds.Translate("tasks.verifyFailed", "A verificação de regravação das quests falhou: ") + ex.Message; }
        }
        context?.Check();
        context?.Report(1, FieldKinds.Translate("tasks.read", "Quests lidas"), string.Format(FieldKinds.Translate("tasks.mainTotal", "{0:N0} quests principais"), Quests.Count), force: true);
    }

    public IEnumerable<QuestNode> AllNodes() => Index.Nodes;

    // ---------- quests por NPC (Mundo 3D) ----------

    Dictionary<int, List<QuestNode>>? _byDelvNpc, _byAwardNpc;

    /// <summary>Quests cujo campo "NPC que dá" (m_ulDelvNPC) ou "NPC que recebe" (m_ulAwardNPC) é o NPC.</summary>
    public IReadOnlyList<QuestNode> QuestsOfNpc(int npcId, bool gives)
    {
        if (Roots is null) return Array.Empty<QuestNode>();
        _byDelvNpc ??= ByScalar("m_ulDelvNPC");
        _byAwardNpc ??= ByScalar("m_ulAwardNPC");
        return (gives ? _byDelvNpc : _byAwardNpc).TryGetValue(npcId, out var l) ? l : Array.Empty<QuestNode>();
    }

    /// <summary>
    /// Índice valor → quests de um campo escalar. Os campos antes das listas variáveis ficam na mesma posição em todas as
    /// quests da versão; a posição vem da 1ª quest e é conferida em cada uma pelo tamanho do bloco (senão monta os campos).
    /// </summary>
    Dictionary<int, List<QuestNode>> ByScalar(string key)
    {
        var map = new Dictionary<int, List<QuestNode>>();
        int? off = Index.Nodes.FirstOrDefault()?.Field(key)?.Offset;
        if (off is null) return map;
        foreach (var q in Index.Nodes)
        {
            int at = q.Fixed.Length >= off.Value + 4 ? off.Value : q.Field(key)?.Offset ?? -1;
            if (at < 0) continue;
            int v = BitConverter.ToInt32(q.Fixed, at);
            if (v == 0) continue;
            if (!map.TryGetValue(v, out var l)) map[v] = l = new();
            l.Add(q);
        }
        return map;
    }

    /// <summary>Esquece os índices por NPC (depois de editar, criar ou excluir quests).</summary>
    public void InvalidateNpcIndex() { _byDelvNpc = null; _byAwardNpc = null; }

    /// <summary>Bytes do arquivo com as alterações aplicadas.</summary>
    public byte[] Build(LoadContext? context = null)
    {
        context?.Check();
        if (!CanWrite) throw new InvalidOperationException(FieldKinds.Translate("tasks.readOnly", "As quests estão em somente leitura e não podem ser salvas. ") + ReadOnlyReason);
        return BuildCore(context);
    }

    private byte[] BuildCore(LoadContext? context = null)
    {
        context?.Check();
        if (Roots is null)
        {
            var outBytes = (byte[])Data.Clone();
            foreach (var q in Quests.Where(q => q.Changed))
            {
                context?.Check();
                if (q.Name.Length > MaxNameLength) throw new InvalidDataException(string.Format(FieldKinds.Translate("tasks.nameTooLong", "O nome da quest {0} passa de {1} caracteres."), q.Id, MaxNameLength));
                for (int i = 0; i < NameChars; i++)
                    BitConverter.GetBytes((ushort)((i < q.Name.Length && i < NameChars - 1 ? q.Name[i] : '\0') ^ (ushort)q.Id)).CopyTo(outBytes, q.Offset + (Version >= 153 ? 6 : 4) + i * 2);
            }
            return outBytes;
        }
        using var ms = new MemoryStream(Data.Length + 4096);
        var w = new BinaryWriter(ms);
        w.Write(BitConverter.ToUInt32(Data, 0)); w.Write(Version); w.Write(Roots.Count);
        long table = ms.Position;
        for (int i = 0; i < Roots.Count; i++) w.Write(0);
        var offs = new int[Roots.Count];
        for (int i = 0; i < Roots.Count; i++)
        {
            context?.Check();
            context?.Report(i / (double)Math.Max(1, Roots.Count), FieldKinds.Translate("tasks.building", "Gerando tasks.data"), string.Format(FieldKinds.Translate("tasks.mainCount", "{0:N0}/{1:N0} quests principais"), i, Roots.Count));
            offs[i] = (int)ms.Position; TaskSchema93.Write(w, Roots[i]);
        }
        ms.Position = table;
        foreach (var o in offs) w.Write(o);
        return ms.ToArray();
    }

    /// <summary>Backup com data/hora + escrita atômica (mesma regra do elements.data).</summary>
    public string? Save(string path, LoadContext? context = null)
    {
        var bytes = Build(context?.Slice(0, .75));
        string? backup = AtomicFileSave.Write(path, bytes, context?.Slice(.75, 1));
        Data = bytes;
        if (Roots is null)
            foreach (var q in Quests) q.OriginalName = q.Name;
        return backup;
    }

    public const int MaxNameLength = NameChars - 1;

    // ---------- criar (clonar) e excluir quests (issue #16) ----------

    /// <summary>
    /// Copia uma quest principal (com as subquests) logo depois dela, com IDs novos (maior ID + 1...). O ID é a chave
    /// dos textos (XOR), então nome, textos e diálogos são recodificados; os vínculos da árvore (pai, irmãos, primeiro
    /// filho) passam a apontar para as cópias. Textos internos do bloco fixo (assinatura, textos de quest) não são
    /// recodificados [?].
    /// </summary>
    public QuestNode CloneRoot(QuestNode src)
    {
        if (Roots is null) throw new InvalidOperationException(FieldKinds.Translate("tasks.cloneNeedsSchema", "Clonar exige o esquema completo do tasks.data."));
        int next = Index.Nodes.Max(n => n.Id) + 1;
        var map = new Dictionary<int, int>();
        void Plan(QuestNode q) { map[q.Id] = next++; foreach (var c in q.Children) Plan(c); }
        Plan(src);
        var copy = CloneNode(src, null, map);
        // a cópia é principal: sem pai e sem irmãos
        foreach (var name in new[] { "Pai", "Irmão anterior", "Próximo irmão" }) SetLink(copy, name, 0);
        int at = Roots.IndexOf(src);
        Roots.Insert(at < 0 ? Roots.Count : at + 1, copy);
        Index = new TaskIndex(Roots, Quests);
        return copy;
    }

    /// <summary>Remove uma quest principal (e as subquests dela).</summary>
    public bool DeleteRoot(QuestNode q)
    {
        if (Roots is null || !Roots.Remove(q)) return false;
        Index = new TaskIndex(Roots, Quests);
        return true;
    }

    static QuestNode CloneNode(QuestNode s, QuestNode? parent, Dictionary<int, int> map)
    {
        int id = map[s.Id];
        var q = new QuestNode
        {
            Id = id, Parent = parent, Fixed = (byte[])s.Fixed.Clone(), TextRaw = (byte[])s.TextRaw.Clone(),
            Fields = s.Fields.Select(f => f.Name is "ID" or "m_ID" && f.Offset == s.IdOffset ? f with { PreservedValue = id } : f).ToList(),   // ID protegido = o novo
            IdOffset = s.IdOffset, NameOffset = s.NameOffset,
            Name = s.Name, OriginalName = "\u0001", TextsChanged = true,   // nome e textos sempre regravados com a chave nova
            NullFlags = new List<bool>(s.NullFlags),
        };
        BitConverter.GetBytes(id).CopyTo(q.Fixed, q.IdOffset);
        Array.Copy(s.Texts, q.Texts, s.Texts.Length);
        for (int i = 0; i < s.Talks.Length; i++)
        {
            var t = s.Talks[i];
            q.Talks[i] = new TalkProc
            {
                IdTalk = t.IdTalk, Text = t.Text, OriginalText = "\u0001", Raw = Array.Empty<byte>(),
                Windows = t.Windows.Select(w => new TalkWindow
                {
                    Id = w.Id, Parent = w.Parent, Text = w.Text,
                    Options = w.Options.Select(o => new TalkOption { Param = o.Param, Id = o.Id, Text = o.Text, OriginalText = "\u0001", Raw = Array.Empty<byte>() }).ToList(),
                }).ToList(),
            };
        }
        // vínculos da árvore que apontam para quests copiadas passam a apontar para as cópias
        foreach (var name in new[] { "Pai", "Irmão anterior", "Próximo irmão", "Primeiro filho" })
            if (q.Field(name) is { } f && map.TryGetValue(BitConverter.ToInt32(q.Fixed, f.Offset), out var nid))
                BitConverter.GetBytes(nid).CopyTo(q.Fixed, f.Offset);
        foreach (var c in s.Children) q.Children.Add(CloneNode(c, q, map));
        return q;
    }

    static void SetLink(QuestNode q, string name, int value)
    {
        if (q.Field(name) is { } f) BitConverter.GetBytes(value).CopyTo(q.Fixed, f.Offset);
    }

    /// <summary>Separa o código de cor do PW ("^RRGGBB") do texto.</summary>
    public static (string Text, System.Drawing.Color? Color) SplitColor(string s)
    {
        var m = Regex.Match(s, "^\\^([0-9a-fA-F]{6})");
        if (!m.Success) return (s, null);
        var v = Convert.ToInt32(m.Groups[1].Value, 16);
        return (s[7..], System.Drawing.Color.FromArgb(v >> 16 & 0xFF, v >> 8 & 0xFF, v & 0xFF));
    }
}
