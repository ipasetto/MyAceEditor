using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Pontos de nascimento do servidor: gamed\config\world\npcgen.data (e um por instância, aNN\npcgen.data), PW-KB §S22.
/// Cabeçalho: i32 versão | i32 nCriaturas | i32 nRecursos | [v>=6] i32 nObjetos | [v>=7] i32 nGatilhos.
/// Corpo na mesma ordem. Conferido byte a byte nos 45 arquivos do servidor 1.4.4 (versões 5, 6 e 10).
/// Todos os bytes viram campos, então gravar sem mudar nada devolve o arquivo idêntico.
/// </summary>
public sealed class NpcGenFile
{
    static readonly Encoding Gbk;
    static NpcGenFile() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public int Version { get; set; }
    public List<NpcGenSpawn> Spawns { get; } = new();
    public List<NpcGenResource> Resources { get; } = new();
    public List<NpcGenDynamic> Dynamics { get; } = new();
    public List<NpcGenTrigger> Triggers { get; } = new();

    public static NpcGenFile Read(byte[] d)
    {
        var r = new BinaryReader(new MemoryStream(d));
        var f = new NpcGenFile { Version = r.ReadInt32() };
        int v = f.Version;
        if (v < 1 || v > 20) throw new InvalidDataException($"npcgen.data: versão {v} desconhecida");
        int nc = r.ReadInt32(), nr = r.ReadInt32(), nd = v >= 6 ? r.ReadInt32() : 0, nt = v >= 7 ? r.ReadInt32() : 0;
        if (nc < 0 || nr < 0 || nd < 0 || nt < 0) throw new InvalidDataException("npcgen.data: quantidades inválidas no cabeçalho");
        try
        {
            for (int i = 0; i < nc; i++) f.Spawns.Add(NpcGenSpawn.Read(r, v));
            for (int i = 0; i < nr; i++) f.Resources.Add(NpcGenResource.Read(r, v));
            for (int i = 0; i < nd; i++) f.Dynamics.Add(NpcGenDynamic.Read(r));
            for (int i = 0; i < nt; i++) f.Triggers.Add(NpcGenTrigger.Read(r, v));
        }
        catch (EndOfStreamException) { throw new InvalidDataException("npcgen.data: o arquivo acabou antes do esperado (versão ou formato diferente)"); }
        if (r.BaseStream.Position != d.Length)
            throw new InvalidDataException($"npcgen.data: sobraram {d.Length - r.BaseStream.Position} bytes no fim (formato diferente)");
        return f;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int v = Version;
        w.Write(v); w.Write(Spawns.Count); w.Write(Resources.Count);
        if (v >= 6) w.Write(Dynamics.Count);
        if (v >= 7) w.Write(Triggers.Count);
        foreach (var s in Spawns) s.Write(w, v);
        foreach (var s in Resources) s.Write(w, v);
        if (v >= 6) foreach (var s in Dynamics) s.Write(w);
        if (v >= 7) foreach (var s in Triggers) s.Write(w, v);
        return ms.ToArray();
    }

    internal static string Name(byte[] raw) { int n = Array.IndexOf(raw, (byte)0); return Gbk.GetString(raw, 0, n < 0 ? raw.Length : n); }
    internal static byte[] Name(string s, int size)
    {
        var b = new byte[size];
        var e = Gbk.GetBytes(s);
        Array.Copy(e, b, Math.Min(e.Length, size - 1));
        return b;
    }
}

public struct Vec3
{
    public float X, Y, Z;
    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    internal static Vec3 Read(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    internal readonly void Write(BinaryWriter w) { w.Write(X); w.Write(Y); w.Write(Z); }
    public override readonly string ToString() => $"{X:0.#}, {Y:0.#}, {Z:0.#}";
}

/// <summary>Ponto de nascimento de monstros/NPCs (CreatureSet): 59 B (v&lt;7) ou 71 B + grupos.</summary>
public sealed class NpcGenSpawn
{
    public int Location;            // 0 no chão, 1 livre (3D)
    public Vec3 Position, Direction, Spread;
    public int Type;                // 0 monstros, 1 NPCs
    public int GroupType;
    public byte InitGen = 1, AutoRevive = 1, ValidOnce;
    public int GenId;
    public int Trigger, LifeTime, MaxNum;   // v>=7
    public List<NpcGenCreature> Creatures { get; } = new();

    internal static NpcGenSpawn Read(BinaryReader r, int v)
    {
        var s = new NpcGenSpawn { Location = r.ReadInt32() };
        int n = r.ReadInt32();
        if (n < 0 || n > 10000) throw new InvalidDataException($"npcgen.data: ponto com {n} grupos");
        s.Position = Vec3.Read(r); s.Direction = Vec3.Read(r); s.Spread = Vec3.Read(r);
        s.Type = r.ReadInt32(); s.GroupType = r.ReadInt32();
        s.InitGen = r.ReadByte(); s.AutoRevive = r.ReadByte(); s.ValidOnce = r.ReadByte();
        s.GenId = r.ReadInt32();
        if (v >= 7) { s.Trigger = r.ReadInt32(); s.LifeTime = r.ReadInt32(); s.MaxNum = r.ReadInt32(); }
        for (int i = 0; i < n; i++) s.Creatures.Add(NpcGenCreature.Read(r, v));
        return s;
    }

    internal void Write(BinaryWriter w, int v)
    {
        w.Write(Location); w.Write(Creatures.Count);
        Position.Write(w); Direction.Write(w); Spread.Write(w);
        w.Write(Type); w.Write(GroupType);
        w.Write(InitGen); w.Write(AutoRevive); w.Write(ValidOnce);
        w.Write(GenId);
        if (v >= 7) { w.Write(Trigger); w.Write(LifeTime); w.Write(MaxNum); }
        foreach (var c in Creatures) c.Write(w, v);
    }

    public NpcGenSpawn Clone()
    {
        var c = new NpcGenSpawn
        {
            Location = Location, Position = Position, Direction = Direction, Spread = Spread, Type = Type, GroupType = GroupType,
            InitGen = InitGen, AutoRevive = AutoRevive, ValidOnce = ValidOnce, GenId = GenId, Trigger = Trigger, LifeTime = LifeTime, MaxNum = MaxNum,
        };
        c.Creatures.AddRange(Creatures.Select(x => x.Clone()));
        return c;
    }
}

/// <summary>Grupo de um ponto (CreatureGroup): 60 B, 64 B a partir da v11.</summary>
public sealed class NpcGenCreature
{
    public int Id, Amount = 1, Respawn = 30, DiedTimes, Aggressive;
    public float OffsetWater, OffsetTerrain;
    public int Faction, FactionHelper, FactionAccept;
    public byte NeedHelp, DefFaction, DefFactionHelper, DefFactionAccept;
    public int PathId, PathLoop, Speed, DeadTime;
    public int RefreshLower;        // v>=11

    internal static NpcGenCreature Read(BinaryReader r, int v)
    {
        var c = new NpcGenCreature
        {
            Id = r.ReadInt32(), Amount = r.ReadInt32(), Respawn = r.ReadInt32(), DiedTimes = r.ReadInt32(), Aggressive = r.ReadInt32(),
            OffsetWater = r.ReadSingle(), OffsetTerrain = r.ReadSingle(),
            Faction = r.ReadInt32(), FactionHelper = r.ReadInt32(), FactionAccept = r.ReadInt32(),
            NeedHelp = r.ReadByte(), DefFaction = r.ReadByte(), DefFactionHelper = r.ReadByte(), DefFactionAccept = r.ReadByte(),
            PathId = r.ReadInt32(), PathLoop = r.ReadInt32(), Speed = r.ReadInt32(), DeadTime = r.ReadInt32(),
        };
        if (v >= 11) c.RefreshLower = r.ReadInt32();
        return c;
    }

    internal void Write(BinaryWriter w, int v)
    {
        w.Write(Id); w.Write(Amount); w.Write(Respawn); w.Write(DiedTimes); w.Write(Aggressive);
        w.Write(OffsetWater); w.Write(OffsetTerrain);
        w.Write(Faction); w.Write(FactionHelper); w.Write(FactionAccept);
        w.Write(NeedHelp); w.Write(DefFaction); w.Write(DefFactionHelper); w.Write(DefFactionAccept);
        w.Write(PathId); w.Write(PathLoop); w.Write(Speed); w.Write(DeadTime);
        if (v >= 11) w.Write(RefreshLower);
    }

    public NpcGenCreature Clone() => (NpcGenCreature)MemberwiseClone();
}

/// <summary>Ponto de coleta (ResourceSet): 31 B (v&lt;10) ou 42 B + grupos de 20 B.</summary>
public sealed class NpcGenResource
{
    public Vec3 Position;
    public float SpreadX, SpreadZ;
    public byte InitGen = 1, AutoRevive = 1, ValidOnce;
    public int GenId;
    public byte Dir1, Dir2, Rotation; public int Trigger, MaxNum;   // v>=10
    public List<NpcGenResourceItem> Items { get; } = new();

    internal static NpcGenResource Read(BinaryReader r, int v)
    {
        var s = new NpcGenResource { Position = Vec3.Read(r), SpreadX = r.ReadSingle(), SpreadZ = r.ReadSingle() };
        int n = r.ReadInt32();
        if (n < 0 || n > 10000) throw new InvalidDataException($"npcgen.data: recurso com {n} grupos");
        s.InitGen = r.ReadByte(); s.AutoRevive = r.ReadByte(); s.ValidOnce = r.ReadByte();
        s.GenId = r.ReadInt32();
        if (v >= 10) { s.Dir1 = r.ReadByte(); s.Dir2 = r.ReadByte(); s.Rotation = r.ReadByte(); s.Trigger = r.ReadInt32(); s.MaxNum = r.ReadInt32(); }
        for (int i = 0; i < n; i++)
            s.Items.Add(new NpcGenResourceItem { Type = r.ReadInt32(), Id = r.ReadInt32(), Respawn = r.ReadInt32(), Amount = r.ReadInt32(), HeightOffset = r.ReadSingle() });
        return s;
    }

    internal void Write(BinaryWriter w, int v)
    {
        Position.Write(w); w.Write(SpreadX); w.Write(SpreadZ); w.Write(Items.Count);
        w.Write(InitGen); w.Write(AutoRevive); w.Write(ValidOnce);
        w.Write(GenId);
        if (v >= 10) { w.Write(Dir1); w.Write(Dir2); w.Write(Rotation); w.Write(Trigger); w.Write(MaxNum); }
        foreach (var i in Items) { w.Write(i.Type); w.Write(i.Id); w.Write(i.Respawn); w.Write(i.Amount); w.Write(i.HeightOffset); }
    }

    public NpcGenResource Clone()
    {
        var c = new NpcGenResource
        {
            Position = Position, SpreadX = SpreadX, SpreadZ = SpreadZ, InitGen = InitGen, AutoRevive = AutoRevive, ValidOnce = ValidOnce,
            GenId = GenId, Dir1 = Dir1, Dir2 = Dir2, Rotation = Rotation, Trigger = Trigger, MaxNum = MaxNum,
        };
        c.Items.AddRange(Items.Select(x => x.Clone()));
        return c;
    }
}

public sealed class NpcGenResourceItem
{
    public int Type, Id, Respawn, Amount;
    public float HeightOffset;
    public NpcGenResourceItem Clone() => (NpcGenResourceItem)MemberwiseClone();
}

/// <summary>Objeto do cenário ligado/desligado por gatilho (24 B).</summary>
public sealed class NpcGenDynamic
{
    public int Id; public Vec3 Position; public byte Dir1, Dir2, Rotation; public int Trigger; public byte Scale;

    internal static NpcGenDynamic Read(BinaryReader r) => new()
    { Id = r.ReadInt32(), Position = Vec3.Read(r), Dir1 = r.ReadByte(), Dir2 = r.ReadByte(), Rotation = r.ReadByte(), Trigger = r.ReadInt32(), Scale = r.ReadByte() };

    internal void Write(BinaryWriter w) { w.Write(Id); Position.Write(w); w.Write(Dir1); w.Write(Dir2); w.Write(Rotation); w.Write(Trigger); w.Write(Scale); }
}

/// <summary>Gatilho (agenda por data/hora): 195 B (v7) ou 199 B (v>=8, com duração).</summary>
public sealed class NpcGenTrigger
{
    public int Id, GmId;
    public byte[] NameRaw = new byte[128];
    public byte AutoStart; public int WaitStart, WaitStop;
    public byte NoStartSchedule, NoStopSchedule;
    public int[] Start = new int[6], Stop = new int[6];   // ano, mês, dia da semana, dia, hora, minuto (-1 = qualquer)
    public int Duration;            // v>=8

    public string Name { get => NpcGenFile.Name(NameRaw); set => NameRaw = NpcGenFile.Name(value, 128); }

    internal static NpcGenTrigger Read(BinaryReader r, int v)
    {
        var t = new NpcGenTrigger { Id = r.ReadInt32(), GmId = r.ReadInt32(), NameRaw = r.ReadBytes(128), AutoStart = r.ReadByte(), WaitStart = r.ReadInt32(), WaitStop = r.ReadInt32(),
            NoStartSchedule = r.ReadByte(), NoStopSchedule = r.ReadByte() };
        for (int i = 0; i < 6; i++) t.Start[i] = r.ReadInt32();
        for (int i = 0; i < 6; i++) t.Stop[i] = r.ReadInt32();
        if (v >= 8) t.Duration = r.ReadInt32();
        return t;
    }

    internal void Write(BinaryWriter w, int v)
    {
        w.Write(Id); w.Write(GmId); w.Write(NameRaw); w.Write(AutoStart); w.Write(WaitStart); w.Write(WaitStop);
        w.Write(NoStartSchedule); w.Write(NoStopSchedule);
        foreach (var x in Start) w.Write(x);
        foreach (var x in Stop) w.Write(x);
        if (v >= 8) w.Write(Duration);
    }
}
