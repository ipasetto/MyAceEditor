using System.Text;
using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

string cfgDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Directory.GetCurrentDirectory(), "PerfectWorld", "cfg");
string temp = Path.Combine(Path.GetTempPath(), "MyAceEditor-cfg-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
int checks = 0;
try
{
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    void Reject<T>(Action action, string message) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Check(rejected, message);
    }
    Check(CfgCatalog.VersionFromName("PW_1.8.0-seledit_v531.cfg") == 531, "v531 不能截断到低 8 位");
    Check(CfgCatalog.VersionFromName("PW_1.4.1_v27_1.cfg") == 27, "同版本后缀");
    Check(CfgCatalog.VersionFromName("PW_1.5.7_v208_0.cfg") == 208, "v208 分支");
    Check(CfgCatalog.VersionFromName("anything_v623.cfg") == 623, "v623");
    Check(MppcDecoder.Decode(Convert.FromHexString("414243F0C0"), 6).AsSpan().SequenceEqual("ABCABC"u8), "RFC 2118 重叠复制向量");
    Check(MppcDecoder.Decode(Convert.FromHexString("B380"), 1).AsSpan().SequenceEqual(new byte[] { 0xE7 }), "RFC 2118 高位字面量");
    Check(MppcDecoder.Decode(Convert.FromHexString("41F00042"), 2).AsSpan().SequenceEqual("AB"u8), "分段对齐标记");
    foreach (int size in new[] { 8193, 16384, 16385 })
    {
        byte[] raw = Enumerable.Range(0, size).Select(i => (byte)(i * 37 + 11)).ToArray();
        Check(MppcDecoder.Decode(FramedPacket(raw), size).AsSpan().SequenceEqual(raw), "长记录 8 KB 压缩段：" + size);
        Check(MppcDecoder.Decode(FramedPacket(raw, rawFrames: true), size).AsSpan().SequenceEqual(raw), "长记录原始段：" + size);
        Check(MppcDecoder.Decode(LiteralPacket(raw), size).AsSpan().SequenceEqual(raw), "长记录普通位流：" + size);
        Reject<InvalidDataException>(() => MppcDecoder.Decode(FramedPacket(raw)[..^2], size), "分段截断必须失败：" + size);
    }
    Reject<InvalidDataException>(() => MppcDecoder.Decode(Convert.FromHexString("F0C0"), 3), "MPPC 非法历史引用应失败");
    Reject<InvalidDataException>(() => MppcDecoder.Decode(new byte[] { 0x80 }, 1), "MPPC 截断应失败");

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        var cancelledContext = new LoadContext(cancellationToken: cancelled.Token);
        Reject<OperationCanceledException>(() => MppcDecoder.Decode("ABC"u8, 3, cancelled.Token), "MPPC 取消应传播");
        Reject<OperationCanceledException>(() => cancelledContext.ReadFile(Path.Combine(temp, "missing.data")), "取消应先于磁盘读取");
        Reject<OperationCanceledException>(() => new PckArchive(Path.Combine(temp, "missing.pck"), cancelled.Token), "资源包取消应传播");
        Reject<OperationCanceledException>(() => new TasksFile(new byte[12], cancelledContext), "任务取消不能被结构回退吞掉");
        Reject<OperationCanceledException>(() => DdsDecoder.Decode(Array.Empty<byte>(), cancelled.Token), "图标像素解码取消应传播");
    }
    var progressValues = new List<LoadProgress>();
    var progress = new LoadContext(new InlineProgress(progressValues.Add), minimumInterval: TimeSpan.Zero);
    progress.Slice(0, .25).Report(1, "第一阶段");
    progress.Slice(.25, 1).Report(.5, "第二阶段");
    progress.Report(0, "阶段切换"); progress.Report(1, "完成");
    Check(progressValues[0].Percent == 25 && progressValues[^1].Percent == 100, "分段进度范围");
    Check(progressValues.Zip(progressValues.Skip(1), (a, b) => a.Percent <= b.Percent).All(x => x), "进度不得倒退");
    string chunkPath = Path.Combine(temp, "chunk.data");
    byte[] chunkBytes = new byte[3 * 1024 * 1024]; File.WriteAllBytes(chunkPath, chunkBytes);
    using (var cancelRead = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "Lendo arquivo" && p.Percent is > 0 and < 100) cancelRead.Cancel(); }), cancelRead.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => context.ReadFile(chunkPath), "分块文件读取可取消");
        using var exclusive = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.None);
        Check(exclusive.Length == chunkBytes.Length, "取消后文件句柄关闭且输入未截断");
    }
    var referenceModule = new Module();
    var referenceList = new ItemList { Name = "TEST_ESSENCE" }; referenceModule.Lists.Add(referenceList);
    for (int i = 0; i < 512; i++) referenceList.Items.Add(new Item
    {
        Id = (i + 1).ToString(), Fields = new()
        {
            ["goods_1_id"] = Field.Of(FieldType.Int, "7"),
            ["goods_1_probability"] = Field.Of(FieldType.Float, "0.5"),
        },
    });
    var reverse = new RefIndex(referenceModule);
    Check(!reverse.IsPrepared && reverse.CachedUsedBy("7").Count == 0, "缓存访问不应触发全表扫描");
    using (var cancelIndex = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "Indexando referências" && p.Percent < 100) cancelIndex.Cancel(); }), cancelIndex.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => reverse.Prepare(context), "引用索引可取消");
        Check(!reverse.IsPrepared && reverse.CachedUsedBy("7").Count == 0, "取消后不能发布部分索引");
    }
    reverse.Prepare();
    Check(reverse.IsPrepared && reverse.CachedUsedBy("7").Count == 512, "引用索引完整计数");
    Check(reverse.CachedUsedBy("7").All(r => r.Probability == "0.5"), "预编译概率字段保持语义");
    referenceList.Items[0].Fields["goods_1_id"].Value = "8"; reverse.Invalidate(); reverse.Prepare();
    Check(reverse.CachedUsedBy("7").Count == 511 && reverse.CachedUsedBy("8").Count == 1, "失效后可重新建立索引");

    var rootQuest = new QuestNode { Id = 20, Name = "主任务" };
    var otherQuest = new QuestNode { Id = 5, Name = "另一个主任务" };
    for (int i = 0; i < 1200; i++) rootQuest.Children.Add(new QuestNode { Id = 1000 + i, Name = "支线 " + i, Parent = rootQuest });
    var taskIndex = new TaskIndex(new[] { rootQuest, otherQuest }, Array.Empty<QuestEntry>());
    Check(taskIndex.Count == 1202 && taskIndex.Nodes.Count == 1202, "任务索引包括全部子任务");
    Check(taskIndex.Search("").Select(TaskIndex.IdOf).SequenceEqual(new[] { 5, 20 }), "无搜索时仅显示已排序主任务");
    Check(ReferenceEquals(taskIndex.Find(1512), rootQuest.Children[512]), "直接按 ID 查找子任务，无须元素数据");
    Check(taskIndex.Search("支线").Length == 1200, "搜索不丢失分页以外的任务");
    rootQuest.Children[512].Name = "任务改名后的新名称";
    Check(taskIndex.Search("改名").Length == 1 && TaskIndex.NameOf(taskIndex.Find(1512)!) == rootQuest.Children[512].Name, "改名后索引读取当前名称");
    Check(taskIndex.Find(-999) is null, "未找到任务 ID 应返回空值");
    var duplicate = new QuestNode { Id = 1512, Name = "重复 ID" };
    var duplicates = new TaskIndex(new[] { rootQuest, duplicate }, Array.Empty<QuestEntry>());
    Check(ReferenceEquals(duplicates.Find(1512), rootQuest.Children[512]), "重复 ID 保持原先先序遍历首项语义");
    var fallbackQuest = new QuestEntry { Id = 3, Name = "仅名称任务" };
    var fallbackIndex = new TaskIndex(null, new[] { fallbackQuest });
    Check(fallbackIndex.Count == 1 && fallbackIndex.Nodes.Count == 0 && ReferenceEquals(fallbackIndex.Find(3), fallbackQuest), "名称模式的任务索引");
    using (var cancelledTaskSearch = new CancellationTokenSource())
    {
        cancelledTaskSearch.Cancel();
        Reject<OperationCanceledException>(() => taskIndex.Search("支线", cancelledTaskSearch.Token), "任务搜索支持提前取消");
        Reject<OperationCanceledException>(() => taskIndex.Search("", cancelledTaskSearch.Token), "空查询也遵循取消");
    }
    using (var cancelTaskIndex = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(_ => cancelTaskIndex.Cancel()), cancelTaskIndex.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => new TaskIndex(new[] { rootQuest }, Array.Empty<QuestEntry>(), context), "建任务索引时取消不返回半成品");
    }
    var emptyTasks = new byte[12];
    BitConverter.GetBytes(TasksFile.Magic).CopyTo(emptyTasks, 0); BitConverter.GetBytes(93).CopyTo(emptyTasks, 4);
    var emptyTaskFile = new TasksFile(emptyTasks);
    Check(emptyTaskFile.Index.Count == 0 && emptyTaskFile.AllNodes().Count() == 0 && emptyTaskFile.Build().AsSpan().SequenceEqual(emptyTasks), "空任务文件索引不影响无修改写回");

    var entries = CfgCatalog.Discover(cfgDirectory);
    Check(entries.Count > 0, "未找到 CFG");
    int plainCases = 0, packedCases = 0, warnings = 0;
    foreach (var entry in entries)
    {
        var cfg = ElementsCfg.Load(entry.Path);
        Check(cfg.Blocks.Count == cfg.DeclaredBlockCount, entry.Path);
        Check(cfg.Blocks[cfg.ConversationListIndex].Raw, "对话索引：" + entry.Path);
        foreach (var block in cfg.Blocks)
            Check(block.Fields.Count == block.Fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count(), "唯一字段键：" + block.Name);
        warnings += cfg.Warnings.Count;
        var format = entry.Version >= 191 ? ElementsReadFormat.SizedLists : ElementsReadFormat.CountOnly;
        var source = MakeFile(cfg, entry.Version, format);
        var reader = new CfgElementsReader(source);
        var module = reader.Parse(cfg);
        Check(reader.Version == entry.Version, "完整版本号：" + entry.Path);
        Check(reader.LastResult is { Complete: true } && reader.Position == source.Length, "模拟文件完整读取：" + entry.Path + " " + reader.StoppedAt);
        Check(module.Lists[0].Items[0].Id == "7", "首记录 ID：" + entry.Path);
        var layout = (ElementsLayout)module.Source!;
        if (cfg.Warnings.Count == 0)
        {
            Check(layout.CanWrite, "无修改写回校验：" + entry.Path);
            Check(ElementsWriter.Build(module).AsSpan().SequenceEqual(source), "字节一致：" + entry.Path);
        }
        else Check(!layout.CanWrite, "不完整 CFG 定义必须只读：" + entry.Path);
        var cut = new CfgElementsReader(source[..^1]); cut.Parse(cfg, format);
        Check(cut.LastResult is { Complete: false }, "截断文件不能显示成功：" + entry.Path);
        plainCases++;
        if (entry.Version >= 191)
        {
            byte[] packed = MakeFile(cfg, entry.Version, ElementsReadFormat.MppcLists);
            var packedReader = new CfgElementsReader(packed);
            var packedModule = packedReader.Parse(cfg, ElementsReadFormat.MppcLists);
            Check(packedReader.LastResult is { Complete: true }, "模拟压缩文件：" + entry.Path + " " + packedReader.StoppedAt);
            Check(packedModule.Lists[0].Items[0].Id == "7", "压缩记录 ID：" + entry.Path);
            Check(!((ElementsLayout)packedModule.Source!).CanWrite, "未验证压缩写回必须只读");
            Reject<InvalidOperationException>(() => ElementsWriter.Build(packedModule), "压缩保存禁止");
            packedCases++;
        }
    }

    // 手工构造混合类型记录，验证 double、uint、byte:N、重复字段和可变列表头保存。
    string customPath = Path.Combine(temp, "fixture_v300.cfg");
    File.WriteAllText(customPath, "3\n2\n001 - TEST\nAUTO\nID;Name;unk;unk;count;value;blob;fraction\nint32;wstring:16;int32;int32;uint;double;byte:3;float\n002 - EMPTY\n0\nID\nint32\n003 - TALK_PROC\n0\nRAW\nbyte:AUTO\n");
    var customCfg = ElementsCfg.Load(customPath);
    byte[] record;
    using (var ms = new MemoryStream())
    {
        using var w = new BinaryWriter(ms);
        w.Write(7); w.Write(Encoding.Unicode.GetBytes("测试".PadRight(8, '\0')));
        w.Write(123); w.Write(456); w.Write(uint.MaxValue); w.Write(1.25d);
        w.Write(new byte[] { 0xAA, 0xBB, 0xCC }); w.Write(0x7FC01234); record = ms.ToArray();
    }
    Check(record.Length == 47, "手工记录长度");
    byte[] typed = MakeFile(customCfg, 300, ElementsReadFormat.SizedLists, record);
    using (var cancelDecode = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "Decodificando dados") cancelDecode.Cancel(); }), cancelDecode.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => new CfgElementsReader(typed).Parse(customCfg, ElementsReadFormat.SizedLists, context), "记录解码取消应传播");
    }
    var typedReader = new CfgElementsReader(typed);
    var typedModule = typedReader.Parse(customCfg);
    Check(typedReader.LastResult is { Complete: true }, "混合类型文件");
    var item = typedModule.Lists[0].Items[0];
    Check(item.Fields["unk"].Value == "123" && item.Fields["unk__2"].Value == "456", "重复字段不能覆盖");
    Check(item.Fields["count"].Value == "4294967295", "uint 无符号读取");
    Check(item.Fields["value"].Value == "1.25", "double 应按浮点读取");
    Check(item.Fields["blob"].Value == "AABBCC", "byte:N 十六进制读取");
    Check(ElementsWriter.Build(typedModule).AsSpan().SequenceEqual(typed), "包括 NaN 原始位的无修改写回");
    using (var cancelVerify = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "Verificando regravação") cancelVerify.Cancel(); }), cancelVerify.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => ElementsWriter.VerifyRoundTrip(typedModule, context), "写回校验取消应传播");
        Check(ElementsWriter.Build(typedModule).AsSpan().SequenceEqual(typed), "取消校验不改变模块和原始字节");
    }
    item.Fields["unk"].Value = "999"; item.Fields["value"].Value = "2.5"; item.Fields["blob"].Value = "010203";
    typedModule.Lists[1].Items.Add(new Item { Id = "8", Fields = new() { ["ID"] = Field.Of(FieldType.Int, "8") } });
    byte[] edited = ElementsWriter.Build(typedModule);
    var again = new CfgElementsReader(edited); var reread = again.Parse(customCfg);
    Check(again.LastResult is { Complete: true }, "修改列表数量后保留 size 头并可重读");
    Check(reread.Lists[0].Items[0].Fields["unk"].Value == "999" && reread.Lists[0].Items[0].Fields["unk__2"].Value == "456", "只修改一个重复字段");
    Check(reread.Lists[0].Items[0].Fields["value"].Value == "2.5", "double 写回");
    Check(reread.Lists[1].Items[0].Id == "8", "空列表添加首记录");

    string dataPath = Path.Combine(temp, "elements.data"); File.WriteAllBytes(dataPath, typed);
    File.Copy(customPath, Path.Combine(temp, "alias_v300_0.cfg"));
    Check(CfgCatalog.Load(dataPath, cfgDirectory: temp).Result.Complete, "相同 CFG 别名不算歧义");
    File.WriteAllText(Path.Combine(temp, "branch_v300_1.cfg"), File.ReadAllText(customPath).Replace("fraction", "different_field"));
    Reject<CfgChoiceRequiredException>(() => CfgCatalog.Load(dataPath, cfgDirectory: temp), "同版本不同语义的 CFG 不得默选");
    var forced = CfgCatalog.Load(dataPath, customPath);
    Check(forced.Result.Complete, "支持显式 CFG");
    if (args.Length > 1)
    {
        var files = Directory.EnumerateFiles(Path.GetFullPath(args[1]), "*.data", SearchOption.AllDirectories).ToArray();
        Check(files.Length > 0, "真实数据目录没有 .data 文件");
        foreach (string file in files)
        {
            var real = CfgCatalog.Load(file, cfgDirectory: cfgDirectory);
            Check(real.Result.Complete && real.Reader.Position == real.Reader.Length, "真实文件读取到 EOF：" + file);
            var realLayout = (ElementsLayout)real.Module.Source!;
            if (realLayout.CanWrite)
                Check(ElementsWriter.Build(real.Module).AsSpan().SequenceEqual(File.ReadAllBytes(file)), "真实文件无修改写回：" + file);
            Console.WriteLine($"真实文件通过：{Path.GetFileName(file)} v{real.Reader.Version}，{real.Result.BlocksRead}/{real.Result.TotalBlocks} 块，{real.Result.Records:N0} 条记录，{real.Result.Length:N0} 字节。");
        }
    }
    Console.WriteLine($"通过：{checks} 项检查；{entries.Count} 个 CFG，{plainCases} 个普通格式模拟文件，{packedCases} 个压缩格式模拟文件，{warnings} 项来源配置警告。\n模拟文件检查与真实数据检查分别统计。");
}
finally { Directory.Delete(temp, recursive: true); }

static byte[] MakeFile(ElementsCfg cfg, int version, ElementsReadFormat format, byte[]? firstRecord = null)
{
    using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    w.Write(0x30000000u | (uint)version);
    for (int index = 0; index < cfg.Blocks.Count; index++)
    {
        var block = cfg.Blocks[index];
        if (block.Offset == "AUTO")
        {
            if (index == 0) { w.Write(0x12345678u); w.Write(0); }
            else if (format != ElementsReadFormat.MppcLists)
            { w.Write(block.Name == "SKILLTOME_SUB_TYPE" ? 0xAB7689DDu : 0xEE35679Fu); w.Write(0); if (block.Name == "SKILLTOME_SUB_TYPE") w.Write(0); }
        }
        else w.Write(new byte[int.Parse(block.Offset)]);
        if (index == 0 && format == ElementsReadFormat.MppcLists)
        { w.Write(0xAB7689DDu); w.Write(0); w.Write(0); w.Write(0xEE35679Fu); w.Write(0); }
        if (block.Raw) { w.Write(0); continue; }
        int count = index == 0 ? 1 : 0;
        byte[] raw = index == 0 ? firstRecord ?? new byte[block.RecordSize] : Array.Empty<byte>();
        if (count > 0 && firstRecord is null) BitConverter.GetBytes(7).CopyTo(raw, 0);
        if (format == ElementsReadFormat.SizedLists) w.Write(777);
        w.Write(count);
        if (format == ElementsReadFormat.SizedLists) w.Write(count > 0 ? block.RecordSize : 0);
        if (format == ElementsReadFormat.MppcLists)
        {
            byte[] packed = count > 0 ? LiteralPacket(raw) : Array.Empty<byte>();
            if (count > 0) { w.Write(7u); w.Write(checked((ushort)packed.Length)); }
            // 空列表没有 payload 长度；下一字节就是下一列表或 RAW 对话段。
            if (count > 0) { w.Write(packed.Length); w.Write(packed); }
        }
        else w.Write(raw);
    }
    return ms.ToArray();
}

static byte[] LiteralPacket(byte[] data)
{
    // 测试专用的字面量编码器，不用于游戏数据写回。
    var bits = new List<int>();
    foreach (byte b in data)
    {
        if (b >= 128) { bits.Add(1); bits.Add(0); for (int k = 6; k >= 0; k--) bits.Add((b >> k) & 1); }
        else for (int k = 7; k >= 0; k--) bits.Add((b >> k) & 1);
    }
    var result = new byte[(bits.Count + 7) / 8];
    for (int i = 0; i < bits.Count; i++) result[i / 8] |= (byte)(bits[i] << (7 - i % 8));
    return result;
}

static byte[] FramedPacket(byte[] data, bool rawFrames = false)
{
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    for (int at = 0; at < data.Length; at += 8192)
    {
        byte[] raw = data.AsSpan(at, Math.Min(8192, data.Length - at)).ToArray();
        byte[] part = rawFrames ? raw : LiteralPacket(raw);
        writer.Write(checked((ushort)(part.Length | (rawFrames ? 0 : 0x8000)))); writer.Write(part);
    }
    return stream.ToArray();
}

sealed class InlineProgress(Action<LoadProgress> report) : IProgress<LoadProgress>
{
    public void Report(LoadProgress value) => report(value);
}
