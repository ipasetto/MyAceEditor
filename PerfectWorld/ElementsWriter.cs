using System.Globalization;
using System.Text;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public sealed class ElementsLayout
{
    public sealed record BlockSpan(CfgBlock Block, ItemList List, int CountPos, int EndPos, int DataPos = -1, int StoredRecordSize = 0)
    {
        public Item[] OriginalOrder { get; init; } = Array.Empty<Item>();
        public int RecordSize => StoredRecordSize > 0 ? StoredRecordSize : Block.RecordSize;
        public int RecordsAt => DataPos >= 0 ? DataPos : CountPos + 4;
    }
    public byte[] Original { get; }
    public List<BlockSpan> Blocks { get; } = new();
    public List<(string Name, int Start, int End)> OpaqueSections { get; } = new();
    public List<string> Warnings { get; } = new();
    public string ConfigPath { get; set; } = "";
    public ElementsReadFormat Format { get; set; }
    public bool Complete { get; set; }
    public string? Failure { get; set; }
    public bool RoundTripVerified { get; set; }
    public bool CanWrite => Complete && Format != ElementsReadFormat.MppcLists && Warnings.Count == 0 && RoundTripVerified;
    public string ReadOnlyReason => !Complete ? "未完整读取：" + Failure
        : Format == ElementsReadFormat.MppcLists ? "MPPC 压缩格式当前仅开放读取，尚未验证压缩写回。"
        : Warnings.Count > 0 ? string.Join("\n", Warnings)
        : !RoundTripVerified ? "无修改写回验证未通过。" : "";
    public ElementsLayout(byte[] original) => Original = original;
}

public static class ElementsWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Encoding Gbk;
    static ElementsWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
    }
    public static bool VerifyRoundTrip(Module module, LoadContext? context = null)
    {
        if (module.Source is not ElementsLayout layout || !layout.Complete || layout.Format == ElementsReadFormat.MppcLists) return false;
        try
        {
            context?.Check();
            byte[] rebuilt = BuildCore(layout, context);
            context?.Check();
            bool same = rebuilt.AsSpan().SequenceEqual(layout.Original);
            context?.Report(1, "验证写回", same ? "无修改重建与原文件一致" : "无修改重建不一致", force: true);
            return same;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or EncoderFallbackException) { return false; }
    }
    public static byte[] Build(Module module, LoadContext? context = null)
    {
        if (module.Source is not ElementsLayout layout) throw new InvalidOperationException("此模块缺少 elements.data 原始布局。");
        if (!layout.CanWrite) throw new InvalidOperationException("当前数据为只读，不能保存。" + layout.ReadOnlyReason);
        return BuildCore(layout, context);
    }
    private static byte[] BuildCore(ElementsLayout layout, LoadContext? context = null)
    {
        byte[] src = layout.Original;
        using var ms = new MemoryStream(src.Length);
        int copied = 0;
        long done = 0, total = layout.Blocks.Sum(b => (long)b.List.Items.Count);
        foreach (var span in layout.Blocks)
        {
            context?.Check();
            if (span.CountPos < copied || span.RecordsAt < span.CountPos + 4 || span.EndPos > src.Length)
                throw new InvalidDataException("保存布局的偏移无效。");
            ms.Write(src, copied, span.CountPos - copied);
            ms.Write(BitConverter.GetBytes(span.List.Items.Count));
            // v191+ 的记录长度字段位于 count 与首记录之间，必须原样保留。
            if (layout.Format == ElementsReadFormat.SizedLists && span.List.Items.Count > 0
                && BitConverter.ToUInt32(src, span.CountPos + 4) == 0)
                ms.Write(BitConverter.GetBytes(span.RecordSize));
            else ms.Write(src, span.CountPos + 4, span.RecordsAt - span.CountPos - 4);
            foreach (var item in span.List.Items)
            {
                if ((done & 127) == 0)
                {
                    context?.Check();
                    context?.Report(done / (double)Math.Max(1, total), "验证写回", $"{done:N0}/{total:N0} 条记录");
                }
                ms.Write(EncodeRecord(span.Block, item, span.RecordSize, context)); done++;
            }
            copied = span.EndPos;
        }
        ms.Write(src, copied, src.Length - copied);
        return ms.ToArray();
    }
    public static string? Save(Module module, string path, LoadContext? context = null)
    {
        byte[] bytes = Build(module, context?.Slice(0, .75));
        return AtomicFileSave.Write(path, bytes, context?.Slice(.75, 1));
    }
    private static byte[] EncodeRecord(CfgBlock block, Item item, int recordSize, LoadContext? context = null)
    {
        if (item.Raw is { } existing && existing.Length != recordSize) throw new InvalidDataException("记录长度与目标列表不一致。");
        var raw = item.Raw is null ? new byte[recordSize] : (byte[])item.Raw.Clone();
        int at = 0, ordinal = 0;
        foreach (var field in block.Fields)
        {
            if ((ordinal++ & 255) == 0) context?.Check();
            try
            {
                if (item.Fields.TryGetValue(field.Name, out var value)
                    && (item.Raw is null || CfgElementsReader.DecodeField(field, raw, at) != value.Value))
                    EncodeField(field, value.Value, raw, at);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or EncoderFallbackException)
            { throw new InvalidDataException($"{block.Name} · ID {item.Id} · 字段 {field.Name}：{ex.Message}", ex); }
            at += field.Size;
        }
        return raw;
    }
    private static void EncodeField(CfgField field, string value, byte[] raw, int at)
    {
        string t = field.Type.ToLowerInvariant();
        if (t.StartsWith("wstring:") || t.StartsWith("string:"))
        {
            bool unicode = t.StartsWith("wstring:");
            byte[] bytes = unicode ? Encoding.Unicode.GetBytes(value) : Gbk.GetBytes(value);
            int limit = field.Size - (unicode ? 2 : 1);
            // 超长文本不能静默截断，以免切开 GBK 字符或 UTF-16 代理对。
            if (bytes.Length > limit) throw new InvalidDataException($"{field.Name}：文本超过可用的 {limit} 字节。");
            Array.Clear(raw, at, field.Size); bytes.CopyTo(raw, at); return;
        }
        if (t.StartsWith("byte:") || t.StartsWith("bytes:"))
        {
            byte[] bytes = Convert.FromHexString(value.Replace("-", "").Replace(" ", ""));
            if (bytes.Length != field.Size) throw new InvalidDataException($"{field.Name}：需要 {field.Size} 字节的十六进制数据。");
            bytes.CopyTo(raw, at); return;
        }
        byte[] data = t switch
        {
            "float" => BitConverter.GetBytes(float.Parse(value.Replace(',', '.'), NumberStyles.Float, Inv)),
            "double" => BitConverter.GetBytes(double.Parse(value.Replace(',', '.'), NumberStyles.Float, Inv)),
            "int64" or "long" => BitConverter.GetBytes(long.Parse(value, Inv)),
            "uint64" or "ulong" => BitConverter.GetBytes(ulong.Parse(value, Inv)),
            "uint32" or "uint" or "dword" => BitConverter.GetBytes(uint.Parse(value, Inv)),
            "int16" or "short" => BitConverter.GetBytes(short.Parse(value, Inv)),
            "uint16" or "word" or "ushort" => BitConverter.GetBytes(ushort.Parse(value, Inv)),
            "int8" or "sbyte" => new[] { unchecked((byte)sbyte.Parse(value, Inv)) },
            "uint8" or "byte" or "char" or "bool" => new[] { byte.Parse(value, Inv) },
            "int32" or "int" => BitConverter.GetBytes(int.TryParse(value, NumberStyles.Integer, Inv, out int signed) ? signed : unchecked((int)uint.Parse(value, Inv))),
            _ => throw new InvalidDataException($"不支持字段类型：{t}"),
        };
        data.CopyTo(raw, at);
    }
}
