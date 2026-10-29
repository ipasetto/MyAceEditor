using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>Tipo de um campo do bloco fixo da quest.</summary>
public enum TT : byte { I32, I16, U16, U32, Bool, U8, F32, I64, U64, F64, Bytes, Str16, Str8 }

/// <summary>
/// Bloco fixo de uma quest decodificado: chave → valor, na ordem do arquivo. Chaves são caminhos
/// ("m_Award_S.m_CandItems[0].m_AwardItems[1].m_ulItemTemplId"); inteiros ficam em long, F32 em float,
/// F64 em double, bytes/textos em byte[]. Chave ausente grava zero — assim um registro vazio vira uma quest nova.
/// </summary>
public sealed class TaskRec
{
    public readonly Dictionary<string, object> V = new();
    public readonly List<(string Key, TT Type)> Order = new();
    /// <summary>Posição de cada campo de <see cref="Order"/> no bloco (só na leitura).</summary>
    public readonly List<int> Pos = new();
    /// <summary>Campos usados como contagem ou condição (mudar desalinha o bloco; só via regravação do registro).</summary>
    public readonly HashSet<string> Structural = new();

    public long Int(string key) => V.TryGetValue(key, out var o) ? Convert.ToInt64(o) : 0;
    public void Set(string key, object value) => V[key] = value;
}

/// <summary>
/// Lê ou grava o bloco fixo com o mesmo código (TaskLayout.Gen.cs): cada chamada lê do arquivo e guarda no
/// <see cref="TaskRec"/>, ou pega do registro e grava. Por isso a gravação segue exatamente a leitura.
/// </summary>
public sealed class TaskIO
{
    readonly BinaryReader? _r;
    readonly BinaryWriter? _w;
    public readonly TaskRec Rec;
    readonly Dictionary<string, object> _derived = new();
    readonly Dictionary<string, int> _seen = new();
    readonly Dictionary<string, string> _last = new();
    string _prefix = "";
    int _ops;
    void Tick(string n) { if (++_ops > 1_000_000) throw new InvalidDataException("laço sem fim em " + _prefix + n); }

    public TaskIO(BinaryReader r) { _r = r; Rec = new TaskRec(); }
    public TaskIO(BinaryWriter w, TaskRec rec) { _w = w; Rec = rec; }

    /// <summary>Diagnóstico: TL_GAPS="campo:bytes,..." lê bytes extras antes do campo (testar hipóteses de layout).</summary>
    static readonly Dictionary<string, int> Gaps = (Environment.GetEnvironmentVariable("TL_GAPS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(g => g.Split(':')).ToDictionary(g => g[0], g => int.Parse(g[1]));

    string Key(string n)
    {
        Tick(n);
        if (Gaps.Count > 0 && Gaps.TryGetValue(_prefix + n, out int gap) && !_seen.ContainsKey(_prefix + n))
        {
            var gk = "_gap." + _prefix + n;
            if (_r != null) { Rec.Pos.Add((int)_r.BaseStream.Position); Rec.V[gk] = _r.ReadBytes(gap); Rec.Order.Add((gk, TT.Bytes)); }
            else _w!.Write(Rec.V.TryGetValue(gk, out var g) ? (byte[])g : new byte[gap]);
        }
        if (_r != null && _r.BaseStream.Position >= _r.BaseStream.Length) throw new EndOfStreamException("fim do arquivo no campo " + _prefix + n);
        var full = _prefix + n;
        _derived.Remove(full); // vale a última atribuição, como no RAE (v182+: m_ulMonsterNum derivado e depois lido)
        if (_seen.TryGetValue(full, out int c)) { _seen[full] = ++c; var k = full + "#" + c; _last[full] = k; return k; }
        _seen[full] = 1;
        _last.Remove(full);
        return full;
    }

    long Int(string n, TT t)
    {
        var k = Key(n);
        if (_r != null)
        {
            long x = t switch
            {
                TT.I32 => _r.ReadInt32(), TT.I16 => _r.ReadInt16(), TT.U16 => _r.ReadUInt16(), TT.U32 => _r.ReadUInt32(),
                TT.I64 => _r.ReadInt64(), TT.U64 => (long)_r.ReadUInt64(), _ => _r.ReadByte(),
            };
            Rec.V[k] = x; Rec.Order.Add((k, t)); Rec.Pos.Add((int)_r.BaseStream.Position - Size(t));
            return x;
        }
        long y = Rec.Int(k);
        switch (t)
        {
            case TT.I32: _w!.Write((int)y); break;
            case TT.I16: _w!.Write((short)y); break;
            case TT.U16: _w!.Write((ushort)y); break;
            case TT.U32: _w!.Write((uint)y); break;
            case TT.I64: _w!.Write(y); break;
            case TT.U64: _w!.Write((ulong)y); break;
            default: _w!.Write((byte)y); break;
        }
        return y;
    }

    static int Size(TT t) => t switch { TT.I32 or TT.U32 => 4, TT.I16 or TT.U16 => 2, TT.I64 or TT.U64 => 8, _ => 1 };

    public long I32(string n) => Int(n, TT.I32);
    public long I16(string n) => Int(n, TT.I16);
    public long U16(string n) => Int(n, TT.U16);
    public long U32(string n) => Int(n, TT.U32);
    public long I64(string n) => Int(n, TT.I64);
    public long U64(string n) => Int(n, TT.U64);
    public long U8(string n) => Int(n, TT.U8);
    /// <summary>Booleano guardado como byte (preserva valores ≠ 0/1).</summary>
    public long Bool(string n) => Int(n, TT.Bool);

    public void F32(string n)
    {
        var k = Key(n);
        if (_r != null) { Rec.V[k] = _r.ReadSingle(); Rec.Order.Add((k, TT.F32)); Rec.Pos.Add((int)_r.BaseStream.Position - 4); return; }
        _w!.Write(Rec.V.TryGetValue(k, out var o) ? Convert.ToSingle(o) : 0f);
    }

    public void F64(string n)
    {
        var k = Key(n);
        if (_r != null) { Rec.V[k] = _r.ReadDouble(); Rec.Order.Add((k, TT.F64)); Rec.Pos.Add((int)_r.BaseStream.Position - 8); return; }
        _w!.Write(Rec.V.TryGetValue(k, out var o) ? Convert.ToDouble(o) : 0d);
    }

    void Raw(string n, int bytes, TT t)
    {
        var k = Key(n);
        if (_r != null) { Rec.Pos.Add((int)_r.BaseStream.Position); Rec.V[k] = _r.ReadBytes(bytes); Rec.Order.Add((k, t)); return; }
        var b = Rec.V.TryGetValue(k, out var o) ? (byte[])o : Array.Empty<byte>();
        if (b.Length >= bytes) _w!.Write(b, 0, bytes);
        else { _w!.Write(b); _w.Write(new byte[bytes - b.Length]); }
    }

    public void Bytes(string n, long count) => Raw(n, (int)count, TT.Bytes);
    /// <summary>Texto UTF-16 de tamanho fixo (n caracteres), guardado cru (o nome vem cifrado com o ID).</summary>
    public void Str16(string n, long chars) => Raw(n, (int)chars * 2, TT.Str16);
    public void Str8(string n, long chars) => Raw(n, (int)chars, TT.Str8);

    /// <summary>Valor já lido (ou derivado) no escopo atual — usado nas contagens e condições.</summary>
    public long N(string n)
    {
        Tick(n);
        var full = _prefix + n;
        if (_derived.TryGetValue(full, out var d)) return Convert.ToInt64(d);
        var key = _last.TryGetValue(full, out var k) ? k : full;
        Rec.Structural.Add(key);
        return Rec.Int(key);
    }

    /// <summary>Bit de uma chave em CombinedSwitches (v135+): a posição depende de quais chaves existem na versão.</summary>
    public long Switch(int version, int param)
    {
        var name = TaskLayout.SwitchParams[param];
        int idx = 0;
        foreach (var s in TaskLayout.Switches)
        {
            if (!TaskVer.Has(version, s)) continue;
            if (s.Name == name)
            {
                var full = _prefix + "CombinedSwitches";
                var b = Rec.V.TryGetValue(_last.TryGetValue(full, out var k) ? k : full, out var o) ? (byte[])o : Array.Empty<byte>();
                return idx / 8 < b.Length && ((b[idx / 8] >> (idx % 8)) & 1) != 0 ? 1 : 0;
            }
            idx++;
        }
        return 0;
    }

    /// <summary>Valor que o RAE calcula para o modelo dele; não existe no arquivo.</summary>
    public void Set(string n, long value) { Tick(n); _derived[_prefix + n] = value; }

    public void In(string n, Action body)
    {
        var old = _prefix;
        _prefix = old + n + ".";
        body();
        _prefix = old;
    }
}

/// <summary>Limites por versão do tasks.data (tamanhos de listas fixas).</summary>
public static class TaskVer
{
    public static int GetMaxTimetableSize(int v) => v < 69 ? 12 : 24;
    public static int GetMaxTaskRegion(int v) => v < 175 ? 8 : 16;
    public static int GetMaxPremTaskCount(int v) => v < 90 ? 5 : 20;
    public static int GetMaxOccupations(int v) => v < 89 ? 8 : v < 120 ? 10 : v < 147 ? 12 : v < 187 ? 14 : v < 195 ? 15 : v < 216 ? 16 : v < 240 ? 17 : 18;
    public static int GetMaxTeamMemWanted(int v) => v < 89 ? 8 : v < 120 ? 10 : v < 147 ? 12 : v < 195 ? 15 : v < 216 ? 16 : v < 240 ? 17 : 18;
    public static int GetMaxPlayerWanted(int v) => v < 135 ? 3 : 6;
    public static int GetMaxMonsterWanted(int v) => v < 135 ? 3 : 6;
    public static long GetMaxMonsterUnwanted(int v, long value) => value < 6 ? value : 6;
    public static int GetMaxAwardScales(int v) => v < 140 ? 5 : 6;
    public static int GetMaxMutexTasks(int v) => v < 215 ? 5 : 20;
    public static int GetMaxNeededTasks(int v) => v < 215 ? 5 : 20;
    public static int GetMaxAwardCandidates(int v) => v < 89 ? 8 : v < 120 ? 10 : v < 164 ? 12 : 999;
    public static bool Has(int v, (string Name, int Min, int Max) s) => v >= s.Min && (s.Max == 0 || v <= s.Max);
    public static int GetCombinedSwitches(int v) => (TaskLayout.Switches.Count(s => Has(v, s)) + 7) / 8;

    /// <summary>Versões de tasks.data conhecidas (formato de referência).</summary>
    public static readonly int[] Known =
    {
        43, 52, 53, 54, 55, 56, 57, 59, 60, 63, 64, 68, 69, 75, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93,
        94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119,
        120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 135, 137, 139, 140, 146, 147, 153, 156, 157, 158, 161, 162, 163,
        164, 167, 175, 176, 177, 178, 180, 181, 182, 183, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 198, 215, 216,
        217, 218, 219, 220, 223, 225, 226, 227, 228, 229, 232, 234, 236, 237, 238, 239, 240, 241,
    };
}

/// <summary>Quest completa: bloco fixo decodificado + textos/diálogos (crus) + subquests.</summary>
public sealed class TaskQuest
{
    public TaskRec Fixed = new();
    public byte[] Texts = Array.Empty<byte>();
    public List<TaskQuest> Subs = new();
    public int Id => (int)Fixed.Int("m_ID");
}

/// <summary>Layout do tasks.data em todas as versões conhecidas: leitura e gravação exatas da quest inteira.</summary>
public static partial class TaskLayout
{
    /// <summary>Último registro em leitura (diagnóstico de erro de layout).</summary>
    public static TaskRec? LastRead;

    /// <summary>Só o bloco fixo (diagnóstico: comparar com o início dos textos).</summary>
    public static TaskRec ReadFixed(BinaryReader r, int version)
    {
        var io = new TaskIO(r);
        Fixed(io, version);
        return io.Rec;
    }

    public static TaskQuest Read(BinaryReader r, int version)
    {
        var io = new TaskIO(r);
        LastRead = io.Rec;
        Fixed(io, version);
        var q = new TaskQuest { Fixed = io.Rec };
        long start = r.BaseStream.Position;
        for (int i = 0; i < 4; i++) { int n = r.ReadInt32(); if (n > 0) r.BaseStream.Position += n * 2L; }
        for (int t = 0; t < 5; t++)
        {
            r.BaseStream.Position += 4 + 128;
            int nw = r.ReadInt32();
            for (int w = 0; w < nw; w++)
            {
                r.BaseStream.Position += 8;
                int len = r.ReadInt32();
                r.BaseStream.Position += len * 2L;
                int no = r.ReadInt32();
                r.BaseStream.Position += no * (4 + 128 + 4L);
            }
        }
        long end = r.BaseStream.Position;
        r.BaseStream.Position = start;
        q.Texts = r.ReadBytes((int)(end - start));
        int subs = r.ReadInt32();
        if (subs < 0 || subs > 10000) throw new InvalidDataException($"quest {q.Id}: {subs} subquests");
        for (int i = 0; i < subs; i++) q.Subs.Add(Read(r, version));
        return q;
    }

    public static void WriteFixed(BinaryWriter w, int version, TaskRec rec) => Fixed(new TaskIO(w, rec), version);

    public static void Write(BinaryWriter w, int version, TaskQuest q)
    {
        Fixed(new TaskIO(w, q.Fixed), version);
        w.Write(q.Texts);
        w.Write(q.Subs.Count);
        foreach (var s in q.Subs) Write(w, version, s);
    }

    static void L_SPECIAL_PARAMETERS(TaskIO v, int version, int count)
    {
        long items = 0, tasks = 0;
        for (int i = 0; i < count; i++)
        {
            long type = v.I32($"ParameterTypes[{i}]");
            if (type is >= 0 and <= 14)
            {
                long x = v.I32($"Param{type}");
                if (type == 3) items = x;
                if (type == 6) tasks = x;
            }
            if (version >= 176) v.I32($"unk176[{i}]");
        }
        for (int j = 0; j < items; j++) v.I32($"UseItemIDs[{j}]");
        if (tasks <= 0) return;
        for (int k = 0; k < tasks; k++) v.I32($"CompleteTaskIDs[{k}]");
        if (version >= 153) for (int l = 0; l < tasks; l++) v.I32($"CompleteTaskIDsSpace[{l}]");
    }

    static void L_RANDOM_DIALOG_GROUP_DATA(TaskIO v, int version, int groups, int perGroup)
    {
        for (int i = 0; i < groups; i++)
            for (int j = 0; j < perGroup; j++) v.Str16($"TextGroups[{i}][{j}]", 100);
    }
}
