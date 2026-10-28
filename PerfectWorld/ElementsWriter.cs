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
    /// <summary>Tamanhos de registro ajustados ao arquivo (CFG de outra variante da mesma versão); não impedem salvar.</summary>
    public List<string> Adjustments { get; } = new();
    public string ConfigPath { get; set; } = "";
    public ElementsReadFormat Format { get; set; }
    public bool Complete { get; set; }
    public string? Failure { get; set; }
    public bool RoundTripVerified { get; set; }
    public bool CanWrite => Complete && Format != ElementsReadFormat.MppcLists && Warnings.Count == 0 && RoundTripVerified;
    public string ReadOnlyReason => !Complete ? FieldKinds.Translate("save.incomplete", "Leitura incompleta: ") + Failure
        : Format == ElementsReadFormat.MppcLists ? FieldKinds.Translate("save.mppc", "Formato comprimido MPPC: só leitura por enquanto, a regravação comprimida ainda não foi validada.")
        : Warnings.Count > 0 ? string.Join("\n", Warnings)
        : !RoundTripVerified ? FieldKinds.Translate("save.verifyFailed", "A verificação de regravação sem alterações falhou.") : "";
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
            context?.Report(1, FieldKinds.Translate("load.verifyRewrite", "Verificando regravação"), same ? FieldKinds.Translate("load.rewriteSame", "reconstrução idêntica ao original") : FieldKinds.Translate("load.rewriteDiffers", "reconstrução difere do original"), force: true);
            return same;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or EncoderFallbackException) { return false; }
    }
    public static byte[] Build(Module module, LoadContext? context = null)
    {
        if (module.Source is not ElementsLayout layout) throw new InvalidOperationException(FieldKinds.Translate("save.noLayout", "Este módulo não tem o layout original do elements.data."));
        if (!layout.CanWrite) throw new InvalidOperationException(FieldKinds.Translate("save.readOnly", "Os dados estão em somente leitura e não podem ser salvos. ") + layout.ReadOnlyReason);
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
                throw new InvalidDataException("Offset inválido no layout salvo.");
            ms.Write(src, copied, span.CountPos - copied);
            ms.Write(BitConverter.GetBytes(span.List.Items.Count));
            // v191+: o campo de tamanho do registro fica entre count e o primeiro registro e precisa ser preservado.
            if (layout.Format == ElementsReadFormat.SizedLists && span.List.Items.Count > 0
                && BitConverter.ToUInt32(src, span.CountPos + 4) == 0)
                ms.Write(BitConverter.GetBytes(span.RecordSize));
            else ms.Write(src, span.CountPos + 4, span.RecordsAt - span.CountPos - 4);
            foreach (var item in span.List.Items)
            {
                if ((done & 127) == 0)
                {
                    context?.Check();
                    context?.Report(done / (double)Math.Max(1, total), FieldKinds.Translate("load.verifyRewrite", "Verificando regravação"), string.Format(FieldKinds.Translate("load.records", "{0:N0}/{1:N0} registros"), done, total));
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
        if (item.Raw is { } existing && existing.Length != recordSize) throw new InvalidDataException("Tamanho do registro difere da lista de destino.");
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
            { throw new InvalidDataException(string.Format(FieldKinds.Translate("save.fieldError", "{0} · ID {1} · campo {2}: {3}"), block.Name, item.Id, field.Name, ex.Message), ex); }
            at += field.Size;
        }
        return raw;
    }
    internal static void EncodeField(CfgField field, string value, byte[] raw, int at)
    {
        string t = field.Type.ToLowerInvariant();
        if (t.StartsWith("wstring:") || t.StartsWith("string:"))
        {
            bool unicode = t.StartsWith("wstring:");
            byte[] bytes = unicode ? Encoding.Unicode.GetBytes(value) : Gbk.GetBytes(value);
            int limit = field.Size - (unicode ? 2 : 1);
            // Texto longo demais não é truncado em silêncio, para não cortar caractere GBK ou par substituto UTF-16.
            if (bytes.Length > limit) throw new InvalidDataException(string.Format(FieldKinds.Translate("save.textTooLong", "{0}: texto excede os {1} bytes disponíveis."), field.Name, limit));
            Array.Clear(raw, at, field.Size); bytes.CopyTo(raw, at); return;
        }
        if (t.StartsWith("byte:") || t.StartsWith("bytes:"))
        {
            byte[] bytes = Convert.FromHexString(value.Replace("-", "").Replace(" ", ""));
            if (bytes.Length != field.Size) throw new InvalidDataException(string.Format(FieldKinds.Translate("save.hexSize", "{0}: são necessários {1} bytes em hexadecimal."), field.Name, field.Size));
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
            _ => throw new InvalidDataException($"Tipo de campo não suportado: {t}"),
        };
        data.CopyTo(raw, at);
    }
}
