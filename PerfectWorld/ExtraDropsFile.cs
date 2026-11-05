using MyAceEditor.UI;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// extra_drops.sev (só servidor, gamed/config): drops extras por grupo de monstros (PW-KB §S34; fonte
/// cgame/gs/template/extra_drop_table.cpp). Mesmo layout do 1.3.6 ao 1.7.6. Tabela não editada regrava idêntica.
/// </summary>
public sealed class ExtraDropsFile
{
    public const int Slots = 256, NameBytes = 128, CountSlots = 8;
    public const int TypeReplace = 1, TypeAdd = 2;

    public sealed class Table
    {
        public byte[] RawName = new byte[NameBytes];
        public int Type = TypeAdd;
        public List<uint> Monsters = [];
        public uint[] Items = new uint[Slots];
        public float[] Chances = new float[Slots];
        public float[] CountChances = new float[CountSlots];

        static Encoding Gbk { get { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936); } }

        public string Name
        {
            get { int n = Array.IndexOf(RawName, (byte)0); return Gbk.GetString(RawName, 0, n < 0 ? NameBytes : n); }
            set
            {
                var b = Gbk.GetBytes(value);
                RawName = new byte[NameBytes];
                Array.Copy(b, RawName, Math.Min(b.Length, NameBytes - 1));
            }
        }

        public int ItemCount => Items.Count(i => i != 0);
        public double ItemSum => Enumerable.Range(0, Slots).Where(i => Items[i] != 0).Sum(i => (double)Chances[i]);
        public double CountSum => CountChances.Sum(c => (double)c);
    }

    public uint Version = 1;
    public List<Table> Tables = [];

    public static ExtraDropsFile Parse(byte[] b)
    {
        var f = new ExtraDropsFile { Version = BitConverter.ToUInt32(b, 0) };
        if (f.Version != 1) throw new InvalidDataException(string.Format(Loc.T("xd.badVersion", "Versão {0} do extra_drops.sev não é conhecida."), f.Version));
        int count = BitConverter.ToInt32(b, 4), p = 8;
        for (int t = 0; t < count; t++)
        {
            var table = new Table();
            int monsters = BitConverter.ToInt32(b, p); p += 4;
            for (int i = 0; i < monsters; i++, p += 4) table.Monsters.Add(BitConverter.ToUInt32(b, p));
            for (int i = 0; i < Slots; i++, p += 8) { table.Items[i] = BitConverter.ToUInt32(b, p); table.Chances[i] = BitConverter.ToSingle(b, p + 4); }
            Array.Copy(b, p, table.RawName, 0, NameBytes); p += NameBytes;
            table.Type = BitConverter.ToInt32(b, p); p += 4;
            for (int i = 0; i < CountSlots; i++, p += 4) table.CountChances[i] = BitConverter.ToSingle(b, p);
            f.Tables.Add(table);
        }
        if (p != b.Length) throw new InvalidDataException(Loc.T("xd.badSize", "O extra_drops.sev tem tamanho diferente do esperado."));
        return f;
    }

    public byte[] ToBytes()
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(Version); w.Write(Tables.Count);
        foreach (var t in Tables)
        {
            w.Write(t.Monsters.Count);
            foreach (var id in t.Monsters) w.Write(id);
            for (int i = 0; i < Slots; i++) { w.Write(t.Items[i]); w.Write(t.Chances[i]); }
            w.Write(t.RawName); w.Write(t.Type);
            foreach (var c in t.CountChances) w.Write(c);
        }
        w.Flush();
        return m.ToArray();
    }

    /// <summary>Problemas que impedem salvar: o gs sorteia pelas somas e por IDs que existam (regra de item que funciona).</summary>
    public List<string> Problems(Func<uint, bool>? monsterExists, Func<uint, bool>? itemExists)
    {
        var list = new List<string>();
        foreach (var t in Tables)
        {
            if (t.Monsters.Count == 0) list.Add(string.Format(Loc.T("xd.noMonsters", "{0}: sem monstros."), t.Name));
            if (t.ItemCount == 0) list.Add(string.Format(Loc.T("xd.noItems", "{0}: sem itens."), t.Name));
            else if (Math.Abs(t.ItemSum - 1) > 0.001) list.Add(string.Format(Loc.T("xd.itemSum", "{0}: as chances dos itens precisam somar 100%."), t.Name));
            if (Math.Abs(t.CountSum - 1) > 0.001) list.Add(string.Format(Loc.T("xd.countSum", "{0}: as chances de quantidade precisam somar 100%."), t.Name));
            if (monsterExists is not null)
                foreach (var id in t.Monsters.Where(id => !monsterExists(id)).Take(3))
                    list.Add(string.Format(Loc.T("xd.badMonster", "{0}: monstro {1} não existe no elements.data."), t.Name, id));
            if (itemExists is not null)
                foreach (var id in t.Items.Where(id => id != 0 && !itemExists(id)).Take(3))
                    list.Add(string.Format(Loc.T("xd.badItem", "{0}: item {1} não existe no elements.data."), t.Name, id));
        }
        return list;
    }
}
