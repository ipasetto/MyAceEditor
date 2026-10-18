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
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "读取文件" && p.Percent is > 0 and < 100) cancelRead.Cancel(); }), cancelRead.Token, TimeSpan.Zero);
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
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "建立引用索引" && p.Percent < 100) cancelIndex.Cancel(); }), cancelIndex.Token, TimeSpan.Zero);
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

    string atomicPath = Path.Combine(temp, "atomic.data");
    byte[] firstSave = "first save"u8.ToArray(), secondSave = "second save"u8.ToArray(), thirdSave = "third save"u8.ToArray();
    Check(AtomicFileSave.Write(atomicPath, firstSave) is null && File.ReadAllBytes(atomicPath).AsSpan().SequenceEqual(firstSave), "新文件保存及回读");
    string staleTemp = atomicPath + ".tmp"; File.WriteAllText(staleTemp, "other operation");
    string backupOne = AtomicFileSave.Write(atomicPath, secondSave)!;
    string backupTwo = AtomicFileSave.Write(atomicPath, thirdSave)!;
    Check(backupOne != backupTwo && File.Exists(backupOne) && File.Exists(backupTwo), "连续保存不发生备份重名");
    Check(File.ReadAllBytes(backupOne).AsSpan().SequenceEqual(firstSave) && File.ReadAllBytes(backupTwo).AsSpan().SequenceEqual(secondSave), "每份备份保留对应写入前内容");
    Check(File.ReadAllBytes(atomicPath).AsSpan().SequenceEqual(thirdSave) && File.ReadAllText(staleTemp) == "other operation", "唯一临时文件不会覆盖固定名残留");
    using (var cancelWrite = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "写入临时文件") cancelWrite.Cancel(); }), cancelWrite.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => AtomicFileSave.Write(atomicPath, new byte[2 * 1024 * 1024], context), "提交前取消应传播");
        Check(File.ReadAllBytes(atomicPath).AsSpan().SequenceEqual(thirdSave), "取消临时写入不改原文件");
    }
    Check(!Directory.EnumerateFiles(temp, "atomic.data.*.tmp").Any(), "成功或取消后清理唯一临时文件");
    string directoryTarget = Path.Combine(temp, "directory-target"); Directory.CreateDirectory(directoryTarget);
    Reject<FileSaveException>(() => AtomicFileSave.Write(directoryTarget, firstSave), "目标为文件夹时应报告具体保存错误");
    Check(Directory.Exists(directoryTarget) && !Directory.EnumerateFiles(temp, "directory-target.*.tmp").Any(), "写入失败保留目标并清理临时文件");
    if (OperatingSystem.IsWindows())
    {
        using var locked = new FileStream(atomicPath, FileMode.Open, FileAccess.Read, FileShare.None);
        Reject<FileSaveException>(() => AtomicFileSave.Write(atomicPath, firstSave), "Windows文件占用不得强行覆盖");
    }
    string savedTaskPath = Path.Combine(temp, "tasks.data"); File.WriteAllBytes(savedTaskPath, emptyTasks);
    string taskBackupOne = emptyTaskFile.Save(savedTaskPath)!;
    string taskBackupTwo = emptyTaskFile.Save(savedTaskPath)!;
    Check(taskBackupOne != taskBackupTwo && File.Exists(taskBackupOne) && File.Exists(taskBackupTwo), "任务连续保存生成不同备份");
    Check(new TasksFile(File.ReadAllBytes(savedTaskPath)).Build().AsSpan().SequenceEqual(emptyTasks), "任务保存后重读保持字节一致");
    var nameOnlyBytes = new byte[80];
    BitConverter.GetBytes(TasksFile.Magic).CopyTo(nameOnlyBytes, 0);
    BitConverter.GetBytes(1000).CopyTo(nameOnlyBytes, 4); BitConverter.GetBytes(1).CopyTo(nameOnlyBytes, 8);
    BitConverter.GetBytes(16).CopyTo(nameOnlyBytes, 12); BitConverter.GetBytes(45).CopyTo(nameOnlyBytes, 16);
    const string originalQuestName = "原任务";
    for (int i = 0; i < 30; i++) BitConverter.GetBytes((ushort)((i < originalQuestName.Length ? originalQuestName[i] : '\0') ^ 45)).CopyTo(nameOnlyBytes, 20 + i * 2);
    var nameOnlyFile = new TasksFile(nameOnlyBytes);
    nameOnlyFile.Quests[0].Name = "新任务";
    nameOnlyFile.Save(savedTaskPath);
    Check(new TasksFile(File.ReadAllBytes(savedTaskPath)).Quests[0].Name == "新任务", "名称模式任务修改保存后可重读");
    Check(!nameOnlyFile.Quests[0].Changed, "名称模式提交后更新编辑基线");
    nameOnlyFile.Quests[0].Name = originalQuestName;
    nameOnlyFile.Save(savedTaskPath);
    Check(new TasksFile(File.ReadAllBytes(savedTaskPath)).Quests[0].Name == originalQuestName, "不重读同一模型，恢复最初名称后仍正确保存");
    byte[] beforeFailedTaskSave = (byte[])nameOnlyFile.Data.Clone();
    nameOnlyFile.Quests[0].Name = "尚未保存";
    Reject<FileSaveException>(() => nameOnlyFile.Save(directoryTarget), "任务目标为目录时保存失败");
    Check(nameOnlyFile.Data.AsSpan().SequenceEqual(beforeFailedTaskSave) && nameOnlyFile.Quests[0].OriginalName == originalQuestName
        && nameOnlyFile.Quests[0].Changed, "保存失败不前移基线或丢失待保存名称");
    byte[] beforeLongName = File.ReadAllBytes(savedTaskPath);
    nameOnlyFile.Quests[0].Name = new string('A', TasksFile.MaxNameLength + 1);
    Reject<InvalidDataException>(() => nameOnlyFile.Save(savedTaskPath), "超长任务名称不得静默截断");
    Check(File.ReadAllBytes(savedTaskPath).AsSpan().SequenceEqual(beforeLongName), "超长名称失败不改变目标");
    using (var invalidQuestBytes = new MemoryStream())
    using (var invalidQuestWriter = new BinaryWriter(invalidQuestBytes))
        Reject<InvalidDataException>(() => TaskSchema93.Write(invalidQuestWriter, new QuestNode { Name = new string('A', 30) }), "完整结构任务名称同样拒绝超长");

    byte[] invalidTaskOffset = (byte[])nameOnlyBytes.Clone();
    BitConverter.GetBytes(int.MaxValue).CopyTo(invalidTaskOffset, 12);
    var invalidOffsetTasks = new TasksFile(invalidTaskOffset);
    Check(!invalidOffsetTasks.CanWrite && invalidOffsetTasks.ReadOnlyReason is not null, "越界任务偏移保持只读，不因加法溢出误通过");
    Reject<InvalidOperationException>(() => invalidOffsetTasks.Save(savedTaskPath), "越界任务偏移不得保存");
    byte[] emptyWithTail = emptyTasks.Concat(new byte[] { 0xAB }).ToArray();
    var tailTasks = new TasksFile(emptyWithTail);
    Check(tailTasks.FullSchema && !tailTasks.CanWrite && !tailTasks.RoundTripVerified, "无修改任务重建会丢失尾部时禁止写回");
    Reject<InvalidOperationException>(() => tailTasks.Save(savedTaskPath), "任务字节往返不一致不得覆盖文件");

    // 合成零值v93任务，长度由读取器取得；不是用户真实游戏数据。
    byte[] blankTaskBody = new byte[16384];
    var blankTaskSchema = new TaskSchema93(blankTaskBody);
    blankTaskSchema.Read(0);
    byte[] fullTaskBytes = new byte[16 + blankTaskSchema.Position];
    BitConverter.GetBytes(TasksFile.Magic).CopyTo(fullTaskBytes, 0); BitConverter.GetBytes(93).CopyTo(fullTaskBytes, 4);
    BitConverter.GetBytes(1).CopyTo(fullTaskBytes, 8); BitConverter.GetBytes(16).CopyTo(fullTaskBytes, 12);
    var fullTaskFile = new TasksFile(fullTaskBytes);
    Check(fullTaskFile.FullSchema && fullTaskFile.CanWrite && fullTaskFile.Build().AsSpan().SequenceEqual(fullTaskBytes), "非空v93合成任务必须逐字节通过无修改写回");
    var fullQuest = fullTaskFile.Roots![0];
    var countField = fullQuest.Fields.Single(f => f.Name == "交付区域数量");
    var idField = fullQuest.Fields.Single(f => f.Name == "ID");
    var boolField = fullQuest.Fields.Single(f => f.Name == "允许放弃");
    var floatField = fullQuest.Fields.Single(f => f.Name == "队友距离");
    Check(!countField.Editable && !idField.Editable && !fullQuest.Set(countField, "1") && !fullQuest.Set(idField, "2"), "任务变长数量与ID不能作为普通标量修改");
    const int tinyFloatBits = 0x2F800001;
    BitConverter.GetBytes(tinyFloatBits).CopyTo(fullQuest.Fixed, floatField.Offset);
    Check(BitConverter.SingleToInt32Bits(float.Parse(fullQuest.Get(floatField), System.Globalization.CultureInfo.InvariantCulture)) == tinyFloatBits, "任务浮点显示保留可往返精度，不把小数显示成0");
    const int taskNanBits = 0x7FC01234;
    BitConverter.GetBytes(taskNanBits).CopyTo(fullQuest.Fixed, floatField.Offset);
    Check(fullQuest.Set(floatField, fullQuest.Get(floatField)) && BitConverter.ToInt32(fullQuest.Fixed, floatField.Offset) == taskNanBits, "不改动任务浮点文本时保留NaN原始位");
    Check(fullQuest.Set(boolField, "1") && !fullQuest.Set(boolField, "非法布尔值") && fullQuest.Get(boolField) == "1", "非法布尔值不得静默变成0");
    string fullTaskTarget = Path.Combine(temp, "tasks-full93.data");
    fullQuest.Name = "完整任务";
    fullTaskFile.Save(fullTaskTarget);
    var fullTaskReload = new TasksFile(File.ReadAllBytes(fullTaskTarget));
    Check(fullTaskReload.FullSchema && fullTaskReload.CanWrite && fullTaskReload.Roots![0].Name == "完整任务"
        && fullTaskReload.Roots[0].Get(fullTaskReload.Roots[0].Fields.Single(f => f.Name == "允许放弃")) == "1", "完整任务改名及标量保存后可重读");
    byte[] fullTaskBeforeFailure = File.ReadAllBytes(fullTaskTarget);
    BitConverter.GetBytes(1).CopyTo(fullQuest.Fixed, countField.Offset);
    Reject<InvalidDataException>(() => fullTaskFile.Save(fullTaskTarget), "直接篡改结构数量也在写入前拒绝");
    Check(File.ReadAllBytes(fullTaskTarget).AsSpan().SequenceEqual(fullTaskBeforeFailure), "结构字段错误不替换原文件");
    BitConverter.GetBytes(0).CopyTo(fullQuest.Fixed, countField.Offset);
    fullQuest.TextsChanged = true;
    fullQuest.Talks[0].Text = new string('A', 64);
    Reject<InvalidDataException>(() => fullTaskFile.Save(fullTaskTarget), "超长固定对话不得静默截断");
    Check(File.ReadAllBytes(fullTaskTarget).AsSpan().SequenceEqual(fullTaskBeforeFailure), "对话编码失败不替换原文件");
    fullQuest.Talks[0].Text = "对话标题";
    fullQuest.Talks[0].Windows.Add(new TalkWindow { Id = 1, Text = "对话内容", Options = new() { new TalkOption { Id = 2, Text = "选项" } } });
    fullTaskFile.Save(fullTaskTarget);
    var talkReload = new TasksFile(File.ReadAllBytes(fullTaskTarget));
    Check(talkReload.FullSchema && talkReload.CanWrite && talkReload.Roots![0].Talks[0].Windows[0].Options[0].Text == "选项", "改动对话树保存后可重新解析");

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
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "解码数据") cancelDecode.Cancel(); }), cancelDecode.Token, TimeSpan.Zero);
        Reject<OperationCanceledException>(() => new CfgElementsReader(typed).Parse(customCfg, ElementsReadFormat.SizedLists, context), "记录解码取消应传播");
    }
    var typedReader = new CfgElementsReader(typed);
    var typedModule = typedReader.Parse(customCfg);
    Check(typedReader.LastResult is { Complete: true }, "混合类型文件");
    var item = typedModule.Lists[0].Items[0];
    string invalidTarget = Path.Combine(temp, "invalid-edit.data"); File.WriteAllBytes(invalidTarget, typed);
    item.Fields["count"].Value = "invalid-number";
    try { ElementsWriter.Save(typedModule, invalidTarget); throw new Exception("非法数字不应保存成功"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("TEST") && ex.Message.Contains("count") && ex.Message.Contains("7"), "字段保存错误应标明块/ID/字段"); }
    Check(File.ReadAllBytes(invalidTarget).AsSpan().SequenceEqual(typed) && !Directory.EnumerateFiles(temp, "invalid-edit.data.*.bak").Any(), "编码错误不替换原文件或生成备份");
    item.Fields["count"].Value = "4294967295";
    var zeroRecord = CfgElementsReader.ReadRecord(customCfg.Blocks[0], new byte[record.Length]);
    Check(zeroRecord.Fields["blob"].Value == "000000" && zeroRecord.Fields["value"].Value == "0", "新建记录包含完整零值布局及定长字节字段");
    zeroRecord.Id = zeroRecord.Fields["ID"].Value = "8";
    zeroRecord.Name = zeroRecord.Fields["Name"].Value = "新增";
    typedModule.Lists[0].Items.Add(zeroRecord);
    ElementsWriter.Save(typedModule, invalidTarget);
    var savedNewItem = new CfgElementsReader(invalidTarget).Parse(customCfg);
    Check(savedNewItem.Lists[0].Items[1].Id == "8" && savedNewItem.Lists[0].Items[1].Fields["blob"].Value == "000000", "完整零值新记录保存后ID和字节字段正确");
    typedModule.Lists[0].Items.Remove(zeroRecord);
    Check(item.Fields["unk"].Value == "123" && item.Fields["unk__2"].Value == "456", "重复字段不能覆盖");
    Check(item.Fields["count"].Value == "4294967295", "uint 无符号读取");
    Check(item.Fields["value"].Value == "1.25", "double 应按浮点读取");
    Check(item.Fields["blob"].Value == "AABBCC", "byte:N 十六进制读取");
    Check(ElementsWriter.Build(typedModule).AsSpan().SequenceEqual(typed), "包括 NaN 原始位的无修改写回");
    using (var cancelVerify = new CancellationTokenSource())
    {
        var context = new LoadContext(new InlineProgress(p => { if (p.Stage == "验证写回") cancelVerify.Cancel(); }), cancelVerify.Token, TimeSpan.Zero);
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
