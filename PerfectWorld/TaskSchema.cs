using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Tipo primitivo de um campo de quest.</summary>
public enum TF : byte { I32, U8, Bool, F32 }

/// <summary>Campo escalar lido (posição relativa ao início da quest → editável no lugar).</summary>
public sealed record TaskField(string Group, string Name, TF Type, int Offset, int? PreservedValue = null)
{
    public int Size => Type switch { TF.I32 or TF.F32 => 4, _ => 1 };
    /// <summary>Campos de contagem/estrutura (nº de itens, regiões…) não são editáveis: mudá-los desalinha o arquivo.</summary>
    public bool Editable => PreservedValue is null;
}

/// <summary>Uma janela de diálogo (talk_proc → windows → options).</summary>
public sealed class TalkWindow
{
    public int Id, Parent;
    public string Text = "";
    public List<TalkOption> Options = new();
}

/// <summary>Opção de uma janela de diálogo. Raw = 128 bytes originais do texto (preserva o "lixo" após o \0).</summary>
public sealed class TalkOption
{
    public int Param, Id;
    public string Text = "", OriginalText = "";
    public byte[] Raw = Array.Empty<byte>();
}

public sealed class TalkProc
{
    public int IdTalk;
    public string Text = "", OriginalText = "";
    public byte[] Raw = Array.Empty<byte>();
    public List<TalkWindow> Windows = new();
}

/// <summary>
/// Quest lida do tasks.data (qualquer nível). Os bytes do bloco fixo (dados + prêmios) ficam em
/// <see cref="Fixed"/>; campos escalares são editados no lugar. Textos e diálogos são decodificados
/// (UTF-16 XOR ID) e só recodificados se mudarem — sem mudanças, a gravação é byte-idêntica.
/// </summary>
public sealed class QuestNode
{
    public int Id;
    public string Name = "", OriginalName = "";
    public byte[] Fixed = Array.Empty<byte>();
    public byte[] TextRaw = Array.Empty<byte>();                // textos + diálogos como estavam no arquivo
    List<TaskField>? _fields;
    /// <summary>Versões ≠ 93: campos montados do bloco fixo só quando a quest é aberta (TaskLayout; economiza memória).</summary>
    internal Func<QuestNode, List<TaskField>>? FieldsFactory;
    public List<TaskField> Fields { get => _fields ??= FieldsFactory?.Invoke(this) ?? new(); set => _fields = value; }
    public bool FieldsLoaded => _fields is not null;
    /// <summary>Posição do ID e do nome no bloco fixo (v153+: i16 do espaço de IDs antes do ID).</summary>
    public int IdOffset, NameOffset = 4;
    /// <summary>Campo pelo nome do esquema v93 (português) ou pela chave do TaskLayout (m_…).</summary>
    public TaskField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name) ?? (QuestKeys.Layout(name) is { } k ? Fields.FirstOrDefault(f => f.Name == k) : null);
    public string[] Texts = new string[4];                      // descrição, aceitar(ok), recusar(no), tributo
    public TalkProc[] Talks = new TalkProc[5];                  // ao entregar, sem requisito, entregar item, em andamento, prêmio
    public bool TextsChanged;
    /// <summary>Para cada texto de tamanho variável (na ordem do arquivo): se terminava com \0.</summary>
    public List<bool> NullFlags = new();
    public List<QuestNode> Children = new();
    public QuestNode? Parent;

    public static readonly string[] TextNames = { "Descrição", "Texto ao aceitar", "Texto ao recusar", "Tributo" };
    public static readonly string[] TalkNames = { "Diálogo ao entregar a quest", "Diálogo sem requisitos", "Diálogo ao entregar item", "Diálogo em andamento", "Diálogo do prêmio" };

    public string Get(TaskField f) => f.Type switch
    {
        TF.I32 => BitConverter.ToInt32(Fixed, f.Offset).ToString(),
        TF.F32 => BitConverter.ToSingle(Fixed, f.Offset).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        TF.Bool => Fixed[f.Offset] != 0 ? "1" : "0",
        _ => Fixed[f.Offset].ToString(),
    };

    public bool CanSet(TaskField f, string v) => TryEncode(f, v, out _);

    public bool Set(TaskField f, string v)
    {
        if (!f.Editable) return false;
        if (v == Get(f)) return true; // valor exibido sem mudança: preserva os bits originais (NaN, booleano ≠ 1…)
        if (!TryEncode(f, v, out var bytes)) return false;
        bytes.CopyTo(Fixed, f.Offset);
        return true;
    }

    private static bool TryEncode(TaskField f, string v, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!f.Editable) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        switch (f.Type)
        {
            case TF.I32 when int.TryParse(v, out var i): bytes = BitConverter.GetBytes(i); return true;
            case TF.F32 when float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out var x): bytes = BitConverter.GetBytes(x); return true;
            case TF.Bool when v is "1" or "true" or "True" or "sim" or "是": bytes = new byte[] { 1 }; return true;
            case TF.Bool when v is "0" or "false" or "False" or "nao" or "não" or "否": bytes = new byte[] { 0 }; return true;
            case TF.U8 when byte.TryParse(v, out var b): bytes = new byte[] { b }; return true;
        }
        return false;
    }

    public IEnumerable<QuestNode> Descendants() { foreach (var c in Children) { yield return c; foreach (var d in c.Descendants()) yield return d; } }
}

/// <summary>
/// Esquema do tasks.data versão 93 (PKG144), escrito a partir do estudo do layout (PW-KB §S21, ENGINE-KB §E3c).
/// Corpo de cada quest: [58 fases do bloco fixo, incluindo arrays variáveis e prêmios] [4 textos pwstr]
/// [5 talk_proc] [i32 nSub + subquests].
/// </summary>
public sealed class TaskSchema93
{
    const int TimetableMax = 24, PremTaskMax = 20, OccupMax = 10, MutexMax = 5, SkillLevN = 4, AwardScales = 5;

    readonly byte[] _d;
    int _p, _base;
    QuestNode _q = null!;
    string _group = "";

    readonly CancellationToken _token;
    readonly int _version;
    public TaskSchema93(byte[] data, CancellationToken cancellationToken = default, int version = 93) { _d = data; _token = cancellationToken; _version = version; }

    /// <summary>Versões com bloco fixo pelo TaskLayout (todas as conhecidas; v223+ tem cabeçalho diferente, ainda sem arquivo para provar).</summary>
    public static bool Supports(int version) => version == 93 || version < 223 && TaskVer.Known.Contains(version);

    public QuestNode Read(int offset, QuestNode? parent = null, int depth = 0)
    {
        _token.ThrowIfCancellationRequested();
        if (depth > 64) throw new InvalidDataException("Subquests aninhadas demais.");
        var q = new QuestNode { Parent = parent };
        var saveQ = _q; _q = q; _base = offset; _p = offset;

        if (_version == 93) ReadFixed();
        else LayoutFixed(q);
        q.Fixed = _d.AsSpan(offset, _p - offset).ToArray();
        q.Id = BitConverter.ToInt32(q.Fixed, q.IdOffset);
        q.Name = q.OriginalName = Xor(q.Fixed, q.NameOffset, 30, q.Id);

        int textStart = _p;
        for (int i = 0; i < 4; i++) q.Texts[i] = PwStr(q.Id);
        for (int i = 0; i < 5; i++) q.Talks[i] = Talk(q.Id);
        q.TextRaw = _d.AsSpan(textStart, _p - textStart).ToArray();

        int nSub = I();
        if (nSub < 0 || nSub > 1000) throw new InvalidDataException($"Quest {q.Id}: nº de subquests inválido ({nSub}).");
        for (int i = 0; i < nSub; i++)
        {
            int at = _p;
            var child = Read(at, q, depth + 1);   // recursão usa o mesmo cursor
            q.Children.Add(child);
        }
        _q = saveQ;
        if (saveQ is not null) _base = OffsetOf(saveQ);
        return q;
    }

    // a base de campos é relativa a cada quest; durante a recursão guardamos por quest
    readonly Dictionary<QuestNode, int> _bases = new(ReferenceEqualityComparer.Instance);
    int OffsetOf(QuestNode q) => _bases.TryGetValue(q, out var b) ? b : _base;

    public int Position => _p;

    // ---------- primitivas (registram campos escalares do bloco fixo) ----------
    int I(string? name = null, bool editable = true) { _token.ThrowIfCancellationRequested(); int v = BitConverter.ToInt32(_d, _p); Rec(name, TF.I32, editable ? null : v); _p += 4; return v; }
    bool B(string? name = null, bool editable = true) { bool v = _d[_p] != 0; Rec(name, TF.Bool, editable ? null : _d[_p]); _p += 1; return v; }
    byte U8(string? name = null) { byte v = _d[_p]; Rec(name, TF.U8); _p += 1; return v; }
    void F(string? name = null) { Rec(name, TF.F32); _p += 4; }
    void Skip(int n) { _token.ThrowIfCancellationRequested(); _p += n; if (_p > _d.Length) throw new EndOfStreamException(); }
    void Rec(string? name, TF t, int? preservedValue = null) { if (name is not null && _q is not null) _q.Fields.Add(new TaskField(_group, name, t, _p - _bases.GetValueOrDefault(_q, _base), preservedValue)); }
    void G(string g) { _group = g; }
    void Tm(string name) { foreach (var n in new[] { "ano", "mês", "dia", "hora", "min", "dia da semana" }) I($"{name} ({n})"); }
    void Zone(string name) { F($"{name} X"); F($"{name} Y"); F($"{name} Z"); }
    void Kv(string name) { I($"{name}: tipo esq."); I($"{name}: valor esq."); I($"{name}: operador"); I($"{name}: tipo dir."); I($"{name}: valor dir."); }

    void LayoutFixed(QuestNode q)
    {
        using var ms = new MemoryStream(_d, _p, _d.Length - _p, false);
        TaskLayout.ReadFixed(new BinaryReader(ms), _version);
        _p += (int)ms.Position;
        if (_version >= 153) { q.IdOffset = 2; q.NameOffset = 6; }
        int version = _version;
        q.FieldsFactory = n => LayoutFields(n.Fixed, version);
    }

    /// <summary>Campos escalares do bloco fixo (chaves do TaskLayout). Contagens e condições ficam protegidas.</summary>
    public static List<TaskField> LayoutFields(byte[] fixedBytes, int version)
    {
        var rec = TaskLayout.ReadFixed(new BinaryReader(new MemoryStream(fixedBytes, false)), version);
        var list = new List<TaskField>(rec.Order.Count);
        for (int i = 0; i < rec.Order.Count; i++)
        {
            var (k, t) = rec.Order[i];
            TF? tf = t switch { TT.I32 => TF.I32, TT.Bool => TF.Bool, TT.U8 => TF.U8, TT.F32 => TF.F32, _ => null };
            if (tf is null || k.StartsWith("_gap.")) continue;
            int off = rec.Pos[i], dot = k.IndexOf('.');
            string group = dot > 0 ? k[..dot] : FieldKinds.Translate("tasks.groupMain", "Principal");
            int? keep = rec.Structural.Contains(k) || k == "m_ID" ? (tf == TF.I32 ? BitConverter.ToInt32(fixedBytes, off) : fixedBytes[off]) : null;
            list.Add(new TaskField(group, k, tf.Value, off, keep));
        }
        return list;
    }

    void ReadFixed()
    {
        _bases[_q] = _base;
        // 01
        G("Básico"); I("ID", editable: false); Skip(60); var hasSign = B("Tem assinatura", editable: false); I(null); I("Tipo"); I("Tempo limite (s)");
        // 02
        G("Tempo"); B("Falha ao sair do jogo"); B("Falha em data absoluta"); Tm("Data de falha"); B("Item não sai do inventário");
        B("Tempo absoluto"); int timetable = I("Nº de horários", editable: false); for (int i = 0; i < TimetableMax; i++) U8(i < timetable ? $"Tipo do horário {i + 1}" : null);
        I(null); I(null); I("Frequência disponível"); I("Nº de vezes");
        // 03
        G("Básico"); B("Escolher uma subquest"); B("Subquest aleatória"); B("Subquests em ordem"); B("Pai falha junto"); B("Pai conclui junto");
        B("Pode desistir"); B("Pode refazer"); B("Pode refazer após falha"); B("Limpar ao desistir"); B("Gravar no histórico"); B("Falha ao morrer");
        I("Máx. de jogadores"); G("Entrega"); B("Entregar em zona"); I("Mundo de entrega");
        // 04-06
        int delvRegions = I("Nº de regiões de entrega", editable: false); I(null);
        B("Falha ao entrar na região"); I("Mundo (entrar)"); int enterRegions = I("Nº de regiões (entrar)", editable: false); I(null);
        B("Falha ao sair da região"); I("Mundo (sair)"); int leaveRegions = I("Nº de regiões (sair)", editable: false); I(null);
        // 07-08
        G("Ao aceitar"); B("Teleportar"); I("Mundo do teleporte"); Zone("Destino"); I("Controle de monstros"); B("Ativar controlador");
        G("Básico"); B("Entregar automaticamente"); B("Mostrar em UI exclusiva"); B("Notificar servidor"); B("Ativar ao morrer"); B("Limpar itens adquiridos");
        I("Nível sugerido"); B("Mostrar aviso"); B("Quest principal"); G("Entrega"); I("NPC que entrega"); I("NPC do prêmio");
        // 10
        G("Básico"); B("Quest de habilidade"); B("Pode procurar"); B("Mostrar direção"); B("Casamento");
        int changeKeys = I("Nº de chaves alteradas", editable: false); I(null); I(null); I(null);
        B("Falha ao trocar de cena"); B("Oculta"); B("Entregar habilidade"); I("ID da habilidade entregue"); I("Nível da habilidade entregue");
        B("Efeito ao concluir"); B("Altera ranking PQ"); B("Comparar item e inventário"); I("Espaços de inventário");
        // 12-13
        G("PQ / contribuição"); B("Quest PQ"); int pqExp = I("Nº de expressões PQ", editable: false); I(null); I(null); B("Sub-quest PQ"); B("Limpar contribuição");
        int monContrib = I("Nº de monstros (contribuição)", editable: false); I(null);
        G("Requisitos"); I("Quests registradas necessárias"); B("Mostrar por quests registradas"); I("Contribuição de facção"); B("Mostrar por contribuição");
        B("Limite por conta"); I("Limite de vezes por conta");
        // 18-24
        I("Nível mínimo"); I("Nível máximo"); B("Mostrar por nível");
        int premItems = I("Nº de itens exigidos", editable: false); I(null); B("Mostrar por itens");
        G("Itens dados"); int givenItems = I("Nº de itens dados", editable: false); I("Itens comuns dados"); I("Itens de quest dados"); I(null);
        G("Requisitos"); I("Depósito"); B("Mostrar por depósito"); I("Reputação mín."); I("Reputação máx."); B("Mostrar por reputação");
        I("Nº de quests anteriores"); for (int i = 0; i < PremTaskMax; i++) I($"Quest anterior {i + 1}");
        B("Mostrar por quest anterior"); I("Mín. de quests anteriores"); I("Período"); B("Mostrar por período");
        I("Facção"); I("Cargo na facção"); B("Mostrar por facção"); I("Gênero"); B("Mostrar por gênero");
        I("Nº de classes"); for (int i = 0; i < OccupMax; i++) I($"Classe {i + 1}"); B("Mostrar por classe"); B("Exige cônjuge"); B("Mostrar por cônjuge");
        B("Somente GM"); B("Bloquear usuário"); B("Mostrar por RMB"); I("RMB mín."); I("RMB máx."); B("Tempo de personagem"); B("Mostrar por tempo de personagem");
        I("Início (tempo de personagem)"); I("Fim (tempo de personagem)"); Tm("Fim do personagem"); I("Tempo de personagem maior que");
        I("Quest conjunta"); I("Condição da quest conjunta"); I("Nº de quests exclusivas"); for (int i = 0; i < MutexMax; i++) I($"Quest exclusiva {i + 1}");
        for (int i = 0; i < SkillLevN; i++) I($"Nível de habilidade {i + 1}");
        U8("Tipo de quest dinâmica"); I("Prêmio especial");
        // 25-27
        G("Equipe"); B("Em equipe"); B("Recebe pela equipe"); B("Quest compartilhada"); B("Conclusão compartilhada"); B("Verificar companheiro"); F("Distância do companheiro");
        B("Todos falham"); B("Falha se o líder falhar"); B("Conclui se o líder concluir"); F("Distância para concluir"); B("Falha ao sair da equipe"); B("Verificar membros ao receber");
        F("Distância dos membros ao receber"); B("Contar pela posição dos membros"); F("Distância para contar"); B("Todos concluem"); B("Só casais"); B("Classes distintas");
        int teamMems = I("Nº de requisitos de membros", editable: false); I(null); B("Mostrar por equipe");
        G("Requisitos"); B("Comparar chaves (requisito)"); I("E/OU (requisito)"); Kv("Chave 1 (requisito)"); Kv("Chave 2 (requisito)");
        // 32-37
        G("Conclusão"); I("Método de conclusão"); I("Tipo de conclusão"); int monWanted = I("Nº de monstros a matar", editable: false); I(null);
        int itemsWanted = I("Nº de itens a coletar", editable: false); I(null); I("Ouro exigido"); I("Contribuição de facção exigida"); I("Exp. de facção exigida");
        I("NPC a proteger"); I("Tempo de proteção"); I("NPC em movimento"); I("Destino do NPC");
        I(null); int reachSites = I("Nº de locais a alcançar", editable: false); I("Mundo (alcançar)"); I("Tempo de espera");
        I(null); int leaveSites = I("Nº de locais a sair", editable: false); I("Mundo (sair do local)");
        B("Comparar chaves (conclusão)"); I("E/OU (conclusão)"); Kv("Chave 1 (conclusão)"); Kv("Chave 2 (conclusão)");
        int expCnt = I("Nº de expressões", editable: false); I(null); I(null);
        int taskChars = I("Nº de textos de quest", editable: false); I(null);
        // 43
        G("Prêmios"); I("Tipo de prêmio (sucesso)"); I("Tipo de prêmio (falha)"); for (int i = 0; i < 6; i++) I(null);
        G("Árvore"); I("Pai"); I("Irmão anterior"); I("Próximo irmão"); I("Primeiro filho"); G("Básico"); B("Quest de biblioteca"); F("Prob. da biblioteca");
        // 45-54: partes variáveis
        G(""); if (hasSign) Skip(60);
        Skip(timetable * 2 * 24);
        Skip(changeKeys * 9);
        Skip(pqExp * (64 + 64 * 8)); Skip(monContrib * 16);
        Skip((delvRegions + enterRegions + leaveRegions) * 24);
        G("Itens exigidos"); for (int i = 0; i < premItems; i++) Item($"Item exigido {i + 1}");
        G("Itens dados"); for (int i = 0; i < givenItems; i++) Item($"Item dado {i + 1}");
        Skip(teamMems * 32);
        G("Monstros a matar"); for (int i = 0; i < monWanted; i++) Monster($"Monstro {i + 1}");
        G("Itens a coletar"); for (int i = 0; i < itemsWanted; i++) Item($"Item a coletar {i + 1}");
        G("");
        Skip(expCnt * (64 + 64 * 8)); Skip(taskChars * 128);
        Skip((reachSites + leaveSites) * 24);
        // 58: prêmios (sucesso, falha) + por razão + por itens
        Award("Prêmio (sucesso)"); Award("Prêmio (falha)");
        for (int k = 0; k < 2; k++) { int sc = I(); Skip(AwardScales * 4); for (int i = 0; i < sc; i++) Award(); }
        for (int k = 0; k < 2; k++) { int sc = I(); Skip(4 + AwardScales * 4); for (int i = 0; i < sc; i++) Award(); }
    }

    // ITEM_WANTED v93 (17 B): id, comum, quantidade, chance, validade
    void Item(string n) { I($"{n}: ID"); B($"{n}: comum"); I($"{n}: quantidade"); F($"{n}: chance"); I($"{n}: validade (s)"); }

    // MONSTER_WANTED v93 (30 B): monstro, quantidade, item que cai, qtd, comum, chance, nível do matador, DPH, DPS
    void Monster(string n)
    {
        I($"{n}: ID"); I($"{n}: quantidade"); I($"{n}: item que cai"); I($"{n}: qtd. do item"); B($"{n}: item comum"); F($"{n}: chance do item");
        B($"{n}: nível do matador"); I($"{n}: DPH"); I($"{n}: DPS");
    }

    // AWARD_DATA v93. Com nome (sucesso/falha) os valores viram campos editáveis; sem nome (prêmios por razão/itens) só pula.
    void Award(string? group = null)
    {
        if (group is null)
        {
            Skip(7 * 4 + 6 * 4);                              // ouro, exp, nova quest, SP, reputação, período, estação; armazéns/inventário
            Skip(4 + 4 + 12 + 4 + 4 * 1 + 4);                 // fúria, mundo, ponto, ctrl monstros, 4 bools, canal
        }
        else
        {
            G(group);
            I("Ouro"); I("Experiência"); I("Nova quest"); I("Espírito (SP)"); I("Reputação"); I("Novo período"); I("Estação de transporte");
            I("Banco"); I("Banco 2"); I("Banco 3"); I("Banco 4"); I("Inventário"); I("Inventário do mascote");
            I("Limite de fúria"); I("Mundo do teleporte"); Zone("Teleporte"); I("Controle de monstros"); B("Ativar controlador");
            B("Prêmio pelo nível"); B("Divórcio"); B("Enviar mensagem"); I("Canal da mensagem");
        }
        int cand = I(group is null ? null : "Nº de grupos de itens", editable: false); Skip(4); int summoned = I(); Skip(4); Skip(2);
        Skip(4 + 1 + 4 + 4 + 8 * 4);                          // dividendo, habilidade, contribuições
        int ranking = I(); Skip(4); int keys = I(); Skip(12);
        Skip(1 + 4 + 4); int display = I(); Skip(4); int exp = I(); Skip(8); int chars = I(); Skip(4);
        for (int i = 0; i < cand; i++)
        {
            if (group is null) { Skip(1); int n0 = I(); Skip(n0 * 17); continue; }
            B($"Grupo {i + 1}: sortear um"); int n = I($"Grupo {i + 1}: nº de itens", editable: false);
            for (int j = 0; j < n; j++) Item($"Prêmio {i + 1}.{j + 1}");
        }
        if (summoned > 0) Skip(1 + 4 + 1 + summoned * 16);
        if (ranking > 0) Skip(1 + ranking * 21);
        Skip(keys * 9); Skip(display * 4); Skip(exp * (64 + 64 * 8)); Skip(chars * 128);
    }

    // ---------- textos ----------
    string PwStr(int key)
    {
        int n = BitConverter.ToInt32(_d, _p); _p += 4;
        if (n < 0 || _p + n * 2 > _d.Length) throw new InvalidDataException($"Texto inválido ({n}).");
        _q.NullFlags.Add(n > 0 && (BitConverter.ToUInt16(_d, _p + (n - 1) * 2) ^ unchecked((ushort)key)) == 0);
        var s = Xor(_d, _p, n, key, stopAtNull: false); _p += n * 2; return s;
    }

    TalkProc Talk(int key)
    {
        var t = new TalkProc { IdTalk = BitConverter.ToInt32(_d, _p) }; _p += 4;
        t.Text = t.OriginalText = Xor(_d, _p, 64, key); t.Raw = _d.AsSpan(_p, 128).ToArray(); _p += 128;
        int nw = BitConverter.ToInt32(_d, _p); _p += 4;
        if (nw < 0 || nw > 4096) throw new InvalidDataException($"Nº de janelas inválido ({nw}).");
        for (int i = 0; i < nw; i++)
        {
            var w = new TalkWindow { Id = BitConverter.ToInt32(_d, _p), Parent = BitConverter.ToInt32(_d, _p + 4) }; _p += 8;
            int len = BitConverter.ToInt32(_d, _p); _p += 4;
            if (len < 0 || _p + len * 2 > _d.Length) throw new InvalidDataException($"Texto de diálogo inválido ({len}).");
            _q.NullFlags.Add(len > 0 && (BitConverter.ToUInt16(_d, _p + (len - 1) * 2) ^ unchecked((ushort)key)) == 0);
            w.Text = Xor(_d, _p, len, key, stopAtNull: false); _p += len * 2;
            int no = BitConverter.ToInt32(_d, _p); _p += 4;
            if (no < 0 || no > 4096) throw new InvalidDataException($"Nº de opções inválido ({no}).");
            for (int j = 0; j < no; j++)
            {
                int param = BitConverter.ToInt32(_d, _p); _p += 4;
                var txt = Xor(_d, _p, 64, key); var raw = _d.AsSpan(_p, 128).ToArray(); _p += 128;
                int id = BitConverter.ToInt32(_d, _p); _p += 4;
                w.Options.Add(new TalkOption { Param = param, Id = id, Text = txt, OriginalText = txt, Raw = raw });
            }
            t.Windows.Add(w);
        }
        return t;
    }

    /// <summary>UTF-16 com XOR (ushort)chave; para no \0 (campos de tamanho fixo).</summary>
    public static string Xor(byte[] d, int p, int chars, int key, bool stopAtNull = true)
    {
        var sb = new StringBuilder(chars);
        ushort k = unchecked((ushort)key);
        for (int i = 0; i < chars; i++)
        {
            char c = (char)(BitConverter.ToUInt16(d, p + i * 2) ^ k);
            if (c == '\0') { if (stopAtNull) break; else continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------- gravação dos textos (só quando mudaram) ----------
    public static byte[] EncodeTexts(QuestNode q)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void Fixed(string s, int chars)
        {
            if (s.Length >= chars) throw new InvalidDataException($"Quest {q.Id}: título ou opção de diálogo passa de {chars - 1} caracteres.");
            for (int i = 0; i < chars; i++) w.Write((ushort)((i < s.Length ? s[i] : '\0') ^ (ushort)q.Id));
        }
        // texto vazio = tamanho 0; terminador \0 só se o original tinha (NullFlags, na ordem do arquivo)
        int flag = 0;
        void Pw(string s)
        {
            bool nul = flag < q.NullFlags.Count && q.NullFlags[flag]; flag++;
            if (s.Length == 0 && !nul) { w.Write(0); return; }
            w.Write(s.Length + (nul ? 1 : 0));
            foreach (var c in s) w.Write((ushort)(c ^ (ushort)q.Id));
            if (nul) w.Write((ushort)(0 ^ (ushort)q.Id));
        }
        foreach (var t in q.Texts) Pw(t);
        foreach (var tp in q.Talks)
        {
            w.Write(tp.IdTalk);
            if (tp.Text == tp.OriginalText && tp.Raw.Length == 128) w.Write(tp.Raw); else Fixed(tp.Text, 64);
            w.Write(tp.Windows.Count);
            foreach (var win in tp.Windows)
            {
                w.Write(win.Id); w.Write(win.Parent); Pw(win.Text); w.Write(win.Options.Count);
                foreach (var o in win.Options)
                {
                    w.Write(o.Param);
                    if (o.Text == o.OriginalText && o.Raw.Length == 128) w.Write(o.Raw); else Fixed(o.Text, 64);
                    w.Write(o.Id);
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>Serializa a quest (e subquests) — bloco fixo + textos (originais se não mudaram) + filhos.</summary>
    public static void Write(BinaryWriter w, QuestNode q)
    {
        foreach (var f in q.FieldsLoaded ? q.Fields.Where(f => !f.Editable) : Enumerable.Empty<TaskField>())
        {
            int current = f.Type == TF.I32 ? BitConverter.ToInt32(q.Fixed, f.Offset) : q.Fixed[f.Offset];
            if (current != f.PreservedValue)
                throw new InvalidDataException($"Quest {q.Id}: o campo estrutural \"{f.Name}\" não pode ser alterado diretamente.");
        }
        if (q.Fixed.Length >= q.IdOffset + 4 && BitConverter.ToInt32(q.Fixed, q.IdOffset) != q.Id)
            throw new InvalidDataException($"Quest {q.Id}: o ID difere da estrutura original e não pode ser alterado diretamente.");
        var fixedBytes = (byte[])q.Fixed.Clone();
        if (q.Name != q.OriginalName && q.Name.Length > TasksFile.MaxNameLength)
            throw new InvalidDataException($"O nome da quest {q.Id} passa de {TasksFile.MaxNameLength} caracteres.");
        if (q.Name != q.OriginalName)
            for (int i = 0; i < 30; i++)
                BitConverter.GetBytes((ushort)((i < q.Name.Length && i < 29 ? q.Name[i] : '\0') ^ (ushort)q.Id)).CopyTo(fixedBytes, q.NameOffset + i * 2);
        w.Write(fixedBytes);
        w.Write(q.TextsChanged ? EncodeTexts(q) : q.TextRaw);
        w.Write(q.Children.Count);
        foreach (var c in q.Children) Write(w, c);
    }
}

/// <summary>Nome do campo no esquema v93 (português) → chave do TaskLayout (m_…), para o código que procura campos pelo nome.</summary>
public static class QuestKeys
{
    static readonly Dictionary<string, string> Map = new()
    {
        ["ID"] = "m_ID", ["Tipo"] = "m_ulType", ["Pai"] = "m_ulParent", ["Irmão anterior"] = "m_ulPrevSibling", ["Próximo irmão"] = "m_ulNextSibling",
        ["Primeiro filho"] = "m_ulFirstChild", ["NPC que entrega"] = "m_ulDelvNPC", ["NPC do prêmio"] = "m_ulAwardNPC",
        ["Nível mínimo"] = "m_ulPremise_Lev_Min", ["Nível máximo"] = "m_ulPremise_Lev_Max", ["Método de conclusão"] = "m_enumMethod",
        ["Tipo de conclusão"] = "m_enumFinishType",
    };
    public static string? Layout(string v93Name) => Map.GetValueOrDefault(v93Name);
}
