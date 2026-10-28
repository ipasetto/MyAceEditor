using System.Globalization;
using System.Text;
using MyAceEditor.Model;

namespace MyAceEditor.PerfectWorld;

public enum ElementsReadFormat { CountOnly, SizedLists, MppcLists }
public static class ElementsFormatLabels
{
    public static string Label(ElementsReadFormat format) => format switch
    {
        ElementsReadFormat.CountOnly => FieldKinds.Translate("fmt.countOnly", "listas simples"),
        ElementsReadFormat.SizedLists => FieldKinds.Translate("fmt.sized", "listas com tamanho de registro"),
        ElementsReadFormat.MppcLists => FieldKinds.Translate("fmt.mppc", "listas comprimidas MPPC"),
        _ => FieldKinds.Translate("fmt.unknown", "layout desconhecido"),
    };
}
public sealed record ElementsProbe(ElementsReadFormat Format, bool Complete, int BlocksRead,
    int TotalBlocks, int Position, int Length, long Records, bool HasUnmappedBytes, string? Error);

/// <summary>Lê pelos offsets do CFG e cabeçalhos reais das listas; só é completo se todas as listas passarem e terminar exatamente no EOF.</summary>
public sealed class CfgElementsReader
{
    private const int MaxCount = 1_000_000;
    private readonly byte[] _d;
    private int _pos, _blocksRead;
    private long _records;
    private bool _unmapped;
    public uint RawVersion { get; }
    public uint Version => RawVersion & 0xFFFF;
    public uint Signature => RawVersion >> 16;
    public uint? Timestamp => _d.Length >= 8 ? BitConverter.ToUInt32(_d, 4) : null;
    public bool SignatureValid => Signature == 0x3000;
    public int Length => _d.Length;
    public int Position => _pos;
    public string? StoppedAt { get; private set; }
    public ElementsProbe? LastResult { get; private set; }
    /// <summary>Último recurso (CfgCatalog): ajusta o tamanho de registro de listas simples que não fecham com o CFG.</summary>
    public bool AutoResize { get; set; }
    public CfgElementsReader(string path, LoadContext? context = null) : this(context is null ? File.ReadAllBytes(path) : context.ReadFile(path)) { }
    public CfgElementsReader(byte[] data)
    {
        if (data.Length < 4) throw new InvalidDataException(FieldKinds.Translate("cfg.shortHeader", "elements.data com cabeçalho menor que 4 bytes."));
        _d = data; RawVersion = BitConverter.ToUInt32(data, 0); _pos = 4;
    }

    public IReadOnlyList<ElementsProbe> Probe(ElementsCfg cfg, LoadContext? context = null)
    {
        var formats = Version >= 191
            ? new[] { ElementsReadFormat.SizedLists, ElementsReadFormat.MppcLists, ElementsReadFormat.CountOnly }
            : new[] { ElementsReadFormat.CountOnly };
        var result = new List<ElementsProbe>();
        for (int i = 0; i < formats.Length; i++)
        {
            context?.Check();
            Read(cfg, formats[i], materialize: false, context?.Slice(i / (double)formats.Length, (i + 1.0) / formats.Length));
            result.Add(LastResult!);
        }
        return result;
    }

    public Module Parse(ElementsCfg cfg, LoadContext? context = null)
    {
        var best = Probe(cfg, context?.Slice(0, .15)).OrderByDescending(x => x.Complete).ThenBy(x => x.HasUnmappedBytes)
            .ThenByDescending(x => x.BlocksRead).ThenByDescending(x => x.Position).First();
        return Parse(cfg, best.Format, context?.Slice(.15, 1));
    }
    public Module Parse(ElementsCfg cfg, ElementsReadFormat format, LoadContext? context = null) => Read(cfg, format, materialize: true, context);

    private Module Read(ElementsCfg cfg, ElementsReadFormat format, bool materialize, LoadContext? context = null)
    {
        context?.Check();
        var parsing = materialize ? context?.Slice(0, .84) : context;
        string stage = materialize ? FieldKinds.Translate("load.decoding", "Decodificando dados") : FieldKinds.Translate("load.matchCfg", "Casando CFG");
        _pos = 4; _blocksRead = 0; _records = 0; _unmapped = false; StoppedAt = null;
        var mod = new Module { Name = "elements.data", Version = unchecked((int)RawVersion) };
        var layout = new ElementsLayout(_d) { ConfigPath = cfg.SourcePath, Format = format };
        layout.Warnings.AddRange(cfg.Warnings);
        mod.Source = layout;
        string current = "cabeçalho";
        try
        {
            for (int index = 0; index < cfg.Blocks.Count; index++)
            {
                context?.Check();
                var block = cfg.Blocks[index]; current = $"[{index + 1}/{cfg.Blocks.Count}] {block.Name}";
                parsing?.Report(_pos / (double)Length, stage, current);
                ReadOffset(block, index, format);
                if (index == 0 && format == ElementsReadFormat.MppcLists) SkipExporterSegments();
                if (block.Raw)
                {
                    int begin = _pos;
                    if (block.Name.Equals("TALK_PROC", StringComparison.OrdinalIgnoreCase)) ReadTalkProc(context);
                    else if (index == cfg.Blocks.Count - 1) _pos = Length;
                    else throw new InvalidDataException("Regra de tamanho desconhecida para bloco RAW que não é o último.");
                    layout.OpaqueSections.Add((block.Name, begin, _pos));
                    _blocksRead++; continue;
                }
                if (block.Fields.Count == 0) throw new InvalidDataException("Bloco sem definição de campos.");
                int countPos = _pos, recordSize = block.RecordSize;
                if (format == ElementsReadFormat.SizedLists) { TakeU32(); countPos = _pos; }
                int count = ReadCount();
                if (format == ElementsReadFormat.SizedLists)
                {
                    uint stored = TakeU32();
                    if (stored == 0 && count == 0) stored = (uint)recordSize;
                    if (stored == 0 || stored > 16 * 1024 * 1024)
                        throw new InvalidDataException($"Tamanho de registro da lista ({stored}) inválido (CFG define {recordSize}).");
                    if (stored != recordSize)
                    {
                        // o arquivo diz o tamanho real: a descrição se ajusta (campos finais saem ou viram "bytes extras")
                        layout.Adjustments.Add(string.Format(FieldKinds.Translate("cfg.recordSize", "{0}: registro de {1} bytes no arquivo, CFG define {2}."), block.Name, stored, recordSize));
                        block = ResizeBlock(block, (int)stored);
                        recordSize = (int)stored;
                    }
                }
                else if (format == ElementsReadFormat.CountOnly && count > 0 && AutoResize)
                {
                    // listas simples não guardam o tamanho do registro: se o do CFG não fecha (IDs absurdos ou próxima
                    // lista sem sentido), procura o tamanho real perto dele. Ex.: NPC_SELL_SERVICE de um v61 BR.
                    int stride = DetectStride(cfg, index, block, _pos, count);
                    if (stride > 0 && stride != recordSize)
                    {
                        layout.Adjustments.Add(string.Format(FieldKinds.Translate("cfg.recordSize", "{0}: registro de {1} bytes no arquivo, CFG define {2}."), block.Name, stride, recordSize));
                        block = ResizeBlock(block, stride);
                        recordSize = stride;
                    }
                }
                var list = new ItemList { Name = block.Name, Group = GroupOf(block.Name), Kind = KindOf(block.Name) };
                if (materialize) list.Items.EnsureCapacity(count);
                int dataPos = _pos;
                if (format == ElementsReadFormat.MppcLists)
                {
                    // Lista comprimida vazia tem só count=0, sem índice nem tamanho de payload.
                    // Ler um uint a mais tomaria o count da próxima lista como tamanho e desalinharia o arquivo inteiro.
                    if (count > 0)
                    {
                        Need(checked((long)count * 6 + 4));
                        int tablePos = _pos;
                        Skip((long)count * 6);
                        uint total = TakeU32();
                        Need(total);
                        int payload = _pos;
                        long sum = 0;
                        for (int row = 0; row < count; row++)
                        {
                            if ((row & 127) == 0)
                            {
                                context?.Check();
                                parsing?.Report((payload + sum) / (double)Length, stage, $"{current} · {row:N0}/{count:N0} registros");
                            }
                            uint id = BitConverter.ToUInt32(_d, tablePos + row * 6);
                            int size = BitConverter.ToUInt16(_d, tablePos + row * 6 + 4);
                            if (size == 0 || sum + size > total) throw new InvalidDataException($"Tamanho inválido no índice comprimido {row}.");
                            if (materialize || row == 0 || row == count - 1)
                            {
                                byte[] raw;
                                try { raw = MppcDecoder.Decode(_d.AsSpan(payload + (int)sum, size), recordSize, context?.Token ?? default); }
                                catch (InvalidDataException ex) { throw new InvalidDataException($"Falha MPPC no registro {row} (ID {id}): {ex.Message}", ex); }
                                if (block.Fields[0].SourceName.Equals("ID", StringComparison.OrdinalIgnoreCase) && block.Fields[0].Size == 4
                                    && BitConverter.ToUInt32(raw, 0) != id)
                                    throw new InvalidDataException($"ID {id} do índice comprimido difere do ID do registro decodificado.");
                                if (materialize) list.Items.Add(ReadRecord(block, raw, context));
                            }
                            sum += size;
                        }
                        if (sum != total) throw new InvalidDataException($"Soma do índice comprimido ({sum}) difere do tamanho do bloco ({total}).");
                        Skip(total);
                    }
                }
                else
                {
                    Need(checked((long)count * recordSize));
                    if (materialize)
                        for (int row = 0; row < count; row++)
                        {
                            if ((row & 127) == 0)
                            {
                                context?.Check();
                                parsing?.Report(_pos / (double)Length, stage, $"{current} · {row:N0}/{count:N0} registros");
                            }
                            list.Items.Add(ReadRecord(block, _d.AsSpan(_pos, recordSize).ToArray(), context)); _pos += recordSize;
                        }
                    else Skip((long)count * recordSize);
                }
                _records += count;
                if (materialize)
                {
                    layout.Blocks.Add(new ElementsLayout.BlockSpan(block, list, countPos, _pos, dataPos, recordSize)
                        { OriginalOrder = list.Items.ToArray() });
                    // Listas vazias também são mantidas, para que o primeiro registro possa ser adicionado.
                    mod.Lists.Add(list);
                }
                _blocksRead++;
            }
            if (_pos != Length) StoppedAt = string.Format(FieldKinds.Translate("cfg.leftover", "CFG processado, mas sobraram {0:N0} bytes (offset {1:N0})."), Length - _pos, _pos);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        { StoppedAt = $"{current}, offset {_pos:N0}: {ex.Message}"; }
        layout.Complete = _blocksRead == cfg.Blocks.Count && _pos == Length && StoppedAt is null;
        layout.Failure = StoppedAt;
        if (!SignatureValid) layout.Warnings.Add(string.Format(FieldKinds.Translate("cfg.signature", "Marca de cabeçalho 0x{0:X4}, não a usual 0x3000; validado só pela estrutura."), Signature));
        if (materialize && layout.Complete && format != ElementsReadFormat.MppcLists && layout.Warnings.Count == 0)
        {
            context?.Report(.84, FieldKinds.Translate("load.verifyRewrite", "Verificando regravação"), FieldKinds.Translate("load.verifyRewriteDetail", "comparando byte a byte a reconstrução sem alterações"), force: true);
            layout.RoundTripVerified = ElementsWriter.VerifyRoundTrip(mod, context?.Slice(.84, 1));
            if (!layout.RoundTripVerified) layout.Warnings.Add(FieldKinds.Translate("cfg.rewriteDiffers", "A regravação sem alterações difere do original; salvar foi desativado."));
        }
        LastResult = new ElementsProbe(format, layout.Complete, _blocksRead, cfg.Blocks.Count, _pos, Length, _records, _unmapped, StoppedAt);
        context?.Report(1, stage, $"{_blocksRead}/{cfg.Blocks.Count} blocos", force: true);
        return mod;
    }

    private void ReadOffset(CfgBlock block, int index, ElementsReadFormat format)
    {
        if (block.Offset != "AUTO") { Skip(int.Parse(block.Offset, CultureInfo.InvariantCulture)); return; }
        if (index > 0 && format == ElementsReadFormat.MppcLists)
        { SkipExporterSegments(); return; } // o formato comprimido antecipa os dois segmentos de exportação.
        TakeU32(); // primeiro AUTO: timestamp; demais AUTO: marcas de exportação.
        uint length = TakeU32(); Skip(length);
        if (index > 0 && block.Name.Equals("SKILLTOME_SUB_TYPE", StringComparison.OrdinalIgnoreCase)) Skip(4);
    }
    private void SkipExporterSegments()
    {
        for (int i = 0; i < 4 && Length - _pos >= 8; i++)
        {
            uint tag = BitConverter.ToUInt32(_d, _pos);
            if (tag is not (0xAB7689DD or 0xEE35679F)) return;
            TakeU32(); uint length = TakeU32(); Skip(length);
            if (tag == 0xAB7689DD) Skip(4);
        }
    }
    private void ReadTalkProc(LoadContext? context)
    {
        int count = ReadCount();
        for (int i = 0; i < count; i++)
        {
            context?.Check();
            Skip(4 + 128); int windows = ReadCount();
            for (int j = 0; j < windows; j++)
            {
                context?.Check();
                Skip(8); uint chars = TakeU32(); Skip(checked((long)chars * 2));
                int choices = ReadCount(); Skip(checked((long)choices * 136));
            }
        }
    }
    // ---------- ajuste automático do tamanho de registro (listas simples, até a v190) ----------
    // O arquivo não diz o tamanho dos registros; o CFG da comunidade pode não bater com uma variante (servidor que
    // acrescentou/tirou campos mantendo a versão). Um tamanho é aceito se os IDs de amostra dos registros forem
    // plausíveis e distintos e o cabeçalho da lista seguinte fizer sentido; senão, testa tamanhos vizinhos (o mais
    // perto do CFG primeiro). 0 = nenhum tamanho fecha (o erro de sempre aparece adiante).
    private const int MaxStrideDelta = 8192;

    private int DetectStride(ElementsCfg cfg, int index, CfgBlock block, int dataPos, int count)
    {
        int s0 = block.RecordSize;
        // 1ª volta exigente (as 3 listas seguintes também fecham com o CFG); 2ª, só a seguinte (duas listas erradas em sequência)
        foreach (int depth in new[] { 3, 1 })
        {
            if (StrideOk(cfg, index, block, dataPos, count, s0, depth)) return s0;
            for (int d = 1; d <= MaxStrideDelta; d++)
            {
                if (StrideOk(cfg, index, block, dataPos, count, s0 + d, depth)) return s0 + d;
                if (s0 - d >= 4 && StrideOk(cfg, index, block, dataPos, count, s0 - d, depth)) return s0 - d;
            }
        }
        return 0;
    }

    private bool StrideOk(ElementsCfg cfg, int index, CfgBlock block, int dataPos, int count, int stride, int depth)
    {
        long end = dataPos + (long)count * stride;
        if (end > Length) return false;
        if (!IdsOk(block, dataPos, count, stride)) return false;
        return NextOk(cfg, index, end, depth);
    }

    private bool IdsOk(CfgBlock block, long dataPos, int count, int stride)
    {
        if (!HasId(block) || count == 0) return true;
        int samples = Math.Min(count, 256);
        var seen = new HashSet<int>();
        for (int k = 0; k < samples; k++)
        {
            long row = (long)k * count / samples;
            int id = BitConverter.ToInt32(_d, (int)(dataPos + row * stride));
            if (!IdOk(id) || !seen.Add(id)) return false;
        }
        return IdOk(BitConverter.ToInt32(_d, (int)(dataPos + (long)(count - 1) * stride)));   // o último também
    }

    // simula as próximas `depth` listas com os tamanhos do CFG: cabeçalho plausível e IDs dos registros plausíveis
    private bool NextOk(ElementsCfg cfg, int index, long pos, int depth)
    {
        if (depth <= 0) return true;
        if (index + 1 >= cfg.Blocks.Count) return pos == Length;
        var nb = cfg.Blocks[index + 1];
        long p = pos;
        if (nb.Offset == "AUTO")
        {
            if (p + 8 > Length) return false;
            uint len = BitConverter.ToUInt32(_d, (int)p + 4); p += 8;
            if (len > Length - p) return false;
            p += len;
            if (nb.Name.Equals("SKILLTOME_SUB_TYPE", StringComparison.OrdinalIgnoreCase)) p += 4;
        }
        else p += int.Parse(nb.Offset, CultureInfo.InvariantCulture);
        if (p + 4 > Length) return false;
        uint c = BitConverter.ToUInt32(_d, (int)p);
        if (nb.Raw) return c < 200_000;                       // diálogos: fim da simulação
        if (c > MaxCount) return false;
        long end = p + 4 + (long)c * nb.RecordSize;
        if (end > Length || !IdsOk(nb, p + 4, (int)c, nb.RecordSize)) return false;
        return NextOk(cfg, index + 1, end, depth - 1);
    }

    private static bool HasId(CfgBlock b) => b.Fields.Count > 0 && b.Fields[0].Size == 4 && b.Fields[0].SourceName.Equals("ID", StringComparison.OrdinalIgnoreCase);
    private static bool IdOk(int id) => id > 0 && id < 50_000_000;

    /// <summary>Mesmo bloco com o tamanho real: campos que não cabem saem; a sobra vira "bytes_extras" (byte:N, regravado igual).</summary>
    internal static CfgBlock ResizeBlock(CfgBlock b, int size)
    {
        var nb = new CfgBlock { Number = b.Number, NumberText = b.NumberText, Name = b.Name, Offset = b.Offset, Raw = b.Raw };
        int sum = 0;
        foreach (var f in b.Fields)
        {
            if (sum + f.Size > size) break;
            nb.Fields.Add(f); sum += f.Size;
        }
        if (sum < size)
        {
            string name = "bytes_extras";
            for (int i = 2; nb.Fields.Any(f => f.Name == name); i++) name = "bytes_extras__" + i;
            nb.Fields.Add(new CfgField(name, $"byte:{size - sum}", size - sum, "bytes_extras"));
        }
        return nb;
    }

    private int ReadCount()
    {
        uint n = TakeU32();
        if (n > MaxCount) throw new InvalidDataException($"Quantidade de registros {n} acima do limite {MaxCount:N0}.");
        return (int)n;
    }
    private uint TakeU32() { Need(4); uint v = BitConverter.ToUInt32(_d, _pos); _pos += 4; return v; }
    private void Need(long count)
    {
        if (count < 0 || count > Length - (long)_pos)
            throw new InvalidDataException($"Precisa de {count:N0} bytes, restam {Length - _pos:N0}.");
    }
    private void Skip(long count) { Need(count); _pos += checked((int)count); }

    internal static Item ReadRecord(CfgBlock block, byte[] raw, LoadContext? context = null)
    {
        var item = new Item { Raw = raw, Id = "", Fields = new Dictionary<string, Field>(block.Fields.Count) };
        int p = 0, ordinal = 0; bool gotName = false;
        foreach (var field in block.Fields)
        {
            if ((ordinal++ & 255) == 0) context?.Check();
            var (type, value) = Decode(field, raw, p); p += field.Size;
            item.Fields.Add(field.Name, Field.Of(type, value));
            if (item.Id.Length == 0 && field.SourceName.Equals("ID", StringComparison.OrdinalIgnoreCase)) item.Id = value;
            if (!gotName && field.SourceName.Equals("Name", StringComparison.OrdinalIgnoreCase)) { item.Name = value; gotName = true; }
        }
        return item;
    }
    public static string DecodeField(CfgField field, byte[] raw, int p) => Decode(field, raw, p).Value;
    private static (FieldType Type, string Value) Decode(CfgField field, byte[] raw, int p)
    {
        if (p < 0 || field.Size > raw.Length - p) throw new InvalidDataException("Campo ultrapassa o limite do registro.");
        string t = field.Type.ToLowerInvariant();
        if (t.StartsWith("wstring:") || t.StartsWith("string:"))
        {
            string text = t.StartsWith("wstring:") ? Encoding.Unicode.GetString(raw, p, field.Size) : Gbk.GetString(raw, p, field.Size);
            int nul = text.IndexOf('\0');
            return (FieldType.String, nul < 0 ? text : text[..nul]);
        }
        if (t.StartsWith("byte:") || t.StartsWith("bytes:")) return (FieldType.String, Convert.ToHexString(raw.AsSpan(p, field.Size)));
        var inv = CultureInfo.InvariantCulture;
        return t switch
        {
            "float" => (FieldType.Float, BitConverter.ToSingle(raw, p).ToString("R", inv)),
            "double" => (FieldType.Float, BitConverter.ToDouble(raw, p).ToString("R", inv)),
            "int64" or "long" => (FieldType.Int, BitConverter.ToInt64(raw, p).ToString(inv)),
            "uint64" or "ulong" => (FieldType.Int, BitConverter.ToUInt64(raw, p).ToString(inv)),
            "int16" or "short" => (FieldType.Int, BitConverter.ToInt16(raw, p).ToString(inv)),
            "uint16" or "word" or "ushort" => (FieldType.Int, BitConverter.ToUInt16(raw, p).ToString(inv)),
            "int8" or "sbyte" => (FieldType.Int, unchecked((sbyte)raw[p]).ToString(inv)),
            "uint8" or "byte" or "char" or "bool" => (FieldType.Int, raw[p].ToString(inv)),
            "uint32" or "uint" or "dword" => (FieldType.Int, BitConverter.ToUInt32(raw, p).ToString(inv)),
            "int32" or "int" => (FieldType.Int, BitConverter.ToInt32(raw, p).ToString(inv)),
            _ => throw new InvalidDataException($"Tipo de campo não suportado: {t}"),
        };
    }
    private static readonly Encoding Gbk = CreateGbk();
    private static Encoding CreateGbk() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936); }
    private static string GroupOf(string n) => n.Contains("NPC") ? "NPC" : n.Contains("FACE") ? "Aparência" : n.Contains("RECIPE") ? "Receitas" :
        n.Contains("PET") ? "Mascotes" : n.Contains("TASK") ? "Quests" : n.Contains("MONSTER") ? "Monstros" :
        n.Contains("WEAPON") || n.Contains("ARMOR") || n.Contains("DECORATION") ? "Equipamentos" : "Itens";
    private static string KindOf(string n) => n.Contains("NPC") || n.Contains("MONSTER") ? "npc" : n.Contains("TASK") ? "task" : n.EndsWith("_ESSENCE") ? "item" : "res";
}
