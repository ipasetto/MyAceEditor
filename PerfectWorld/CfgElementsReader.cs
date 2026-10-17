using System.Text;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Parser do elements.data orientado pelo .cfg (campos nomeados, por versão).
/// Lê as DUAS metades, tratando as seções especiais (blob) e o TALK_PROC (RAW, variável).
/// Produz um Módulo com listas (blocos) e itens (registros) de campos nomeados.
/// </summary>
public sealed class CfgElementsReader
{
    private const int MaxCount = 500_000;
    private readonly byte[] _d;
    private int _pos;

    public uint Version { get; }
    public uint Signature { get; }
    public int Length => _d.Length;
    public int Position => _pos;
    public string? StoppedAt { get; private set; }

    public CfgElementsReader(string path)
    {
        _d = File.ReadAllBytes(path);
        Version = U32(0);
        Signature = U32(4);
        _pos = 8;
    }

    public bool SignatureValid => Signature == 0x4F614E5B;

    private uint U32(int p) => p + 4 <= _d.Length ? BitConverter.ToUInt32(_d, p) : uint.MaxValue;
    private bool TryU32(int p, out uint v) { v = U32(p); return p + 4 <= _d.Length; }

    public Module Parse(ElementsCfg cfg)
    {
        var mod = new Module { Name = "elements.data", Version = (int)Version };
        var layout = new ElementsLayout(_d);
        mod.Source = layout;
        foreach (var block in cfg.Blocks)
        {
            SkipSpecials();
            if (block.Raw) { if (!SkipTalkProc()) { StoppedAt = $"{block.Name} (RAW)"; break; } continue; }
            if (block.Fields.Count == 0) { StoppedAt = $"{block.Name} (tipo desconhecido)"; break; }

            uint count = U32(_pos);
            if (count > MaxCount) { StoppedAt = $"{block.Name} (count {count} @{_pos})"; break; }
            int countPos = _pos;
            _pos += 4;
            if (_pos + (long)count * block.RecordSize > _d.Length) { StoppedAt = $"{block.Name} (ultrapassa)"; break; }

            var list = new ItemList { Name = block.Name, Group = GroupOf(block.Name), Kind = KindOf(block.Name) };
            for (uint r = 0; r < count; r++)
            {
                list.Items.Add(ReadRecord(block, _pos));
                _pos += block.RecordSize;
            }
            layout.Blocks.Add(new ElementsLayout.BlockSpan(block, list, countPos, _pos));
            if (count > 0) mod.Lists.Add(list);
        }
        return mod;
    }

    private Item ReadRecord(CfgBlock block, int start)
    {
        var item = new Item { Fields = new() };
        int p = start;
        string? id = null, name = null;
        foreach (var f in block.Fields)
        {
            var (ftype, val) = ReadField(f, p);
            p += f.Size;
            item.Fields[f.Name] = Field.Of(ftype, val);
            if (id is null && f.Name.Equals("ID", StringComparison.OrdinalIgnoreCase)) id = val;
            if (name is null && f.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = val;
        }
        item.Id = id ?? "";
        item.Name = name ?? "";
        item.Raw = _d.AsSpan(start, block.RecordSize).ToArray();
        return item;
    }

    /// <summary>Decodifica um campo a partir de um buffer (mesma regra da leitura).</summary>
    public static string DecodeField(CfgField f, byte[] buf, int p) => new CfgElementsReader(buf).ReadField(f, p).Item2;

    private CfgElementsReader(byte[] buf) { _d = buf; }

    private (FieldType, string) ReadField(CfgField f, int p)
    {
        string t = f.Type.ToLowerInvariant();
        if (t.StartsWith("wstring:"))
            return (FieldType.String, ReadWStr(p, f.Size));
        if (t.StartsWith("string:"))
            return (FieldType.String, ReadStr(p, f.Size));
        return t switch
        {
            "float" => (FieldType.Float, p + 4 <= _d.Length ? BitConverter.ToSingle(_d, p).ToString("0.######") : "0"),
            "int16" or "word" or "short" => (FieldType.Int, (p + 2 <= _d.Length ? BitConverter.ToInt16(_d, p) : 0).ToString()),
            "int8" or "byte" or "char" or "bool" => (FieldType.Int, (p < _d.Length ? _d[p] : 0).ToString()),
            "int64" or "double" => (FieldType.Int, (p + 8 <= _d.Length ? BitConverter.ToInt64(_d, p) : 0).ToString()),
            _ => (FieldType.Int, (p + 4 <= _d.Length ? BitConverter.ToInt32(_d, p) : 0).ToString()),
        };
    }

    private string ReadWStr(int p, int bytes)
    {
        int end = p; int limit = Math.Min(p + bytes, _d.Length - 1);
        while (end < limit && !(_d[end] == 0 && _d[end + 1] == 0)) end += 2;
        return Encoding.Unicode.GetString(_d, p, Math.Max(0, end - p));
    }

    private string ReadStr(int p, int bytes)
    {
        int end = p; int limit = Math.Min(p + bytes, _d.Length);
        while (end < limit && _d[end] != 0) end++;
        return Gbk.GetString(_d, p, Math.Max(0, end - p)); // string:N = GBK (PWKB §S23)
    }

    private static readonly Encoding Gbk = CreateGbk();
    private static Encoding CreateGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    // Seções especiais (blob): A(4) + len(4) + len bytes + [trailer(4)], disparam quando o count parece absurdo.
    private void SkipSpecials()
    {
        for (int t = 0; t < 6; t++)
        {
            uint c = U32(_pos);
            if (c <= MaxCount) return;
            int? sp = TrySpecial(_pos);
            if (sp is null) return;
            _pos = sp.Value;
        }
    }

    private int? TrySpecial(int p)
    {
        if (p + 8 > _d.Length) return null;
        long len = U32(p + 4);
        foreach (int trailer in new[] { 4, 0 })
        {
            long after = p + 8 + len + trailer;
            if (after > 0 && after <= _d.Length && U32((int)after) <= MaxCount) return (int)after;
        }
        return null;
    }

    // TALK_PROC: count + por item [A(4) + 0x80 header + M(4) + M×(8 + len1(4)+len1*2 + len2(4)+len2*0x88)]
    private bool SkipTalkProc()
    {
        uint count = U32(_pos); _pos += 4;
        if (count > MaxCount) return false;
        for (uint i = 0; i < count; i++)
        {
            _pos += 4 + 0x80;
            uint m = U32(_pos); _pos += 4;
            if (m > MaxCount) return false;
            for (uint j = 0; j < m; j++)
            {
                _pos += 8;
                uint l1 = U32(_pos); _pos += 4 + (int)l1 * 2;
                uint l2 = U32(_pos); _pos += 4 + (int)l2 * 0x88;
                if (_pos > _d.Length) return false;
            }
        }
        return true;
    }

    private static string GroupOf(string n) =>
        n.Contains("NPC") ? "NPCs" : n.Contains("FACE") ? "Faces" : n.Contains("RECIPE") ? "Receitas" :
        n.Contains("PET") ? "Pets" : n.Contains("TASK") ? "Tasks" : n.Contains("MONSTER") ? "Monstros" :
        n.Contains("WEAPON") || n.Contains("ARMOR") || n.Contains("DECORATION") ? "Equipamentos" : "Itens";

    private static string KindOf(string n) =>
        n.Contains("NPC") ? "npc" : n.Contains("MONSTER") ? "npc" : n.Contains("TASK") ? "task" :
        n.EndsWith("_ESSENCE") ? "item" : "res";
}
