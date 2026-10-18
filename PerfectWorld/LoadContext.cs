using System.Diagnostics;

namespace MyAceEditor.PerfectWorld;

public sealed record LoadProgress(int Percent, string Stage, string Detail = "", bool Indeterminate = false);

/// <summary>可组合的阶段进度和取消；统一限频，防止进度消息淹没界面队列。</summary>
public sealed class LoadContext
{
    private sealed class Shared
    {
        public IProgress<LoadProgress>? Sink;
        public long LastTick;
        public int LastPercent;
        public double Interval;
    }
    private readonly Shared _shared;
    private readonly double _start, _width;
    public CancellationToken Token { get; }
    public LoadContext(IProgress<LoadProgress>? progress = null, CancellationToken cancellationToken = default,
        TimeSpan? minimumInterval = null)
    {
        _shared = new Shared { Sink = progress, Interval = (minimumInterval ?? TimeSpan.FromMilliseconds(80)).TotalSeconds };
        _start = 0; _width = 1; Token = cancellationToken;
    }
    private LoadContext(Shared shared, CancellationToken token, double start, double width)
    { _shared = shared; Token = token; _start = start; _width = width; }
    public LoadContext Slice(double from, double to)
    {
        if (from < 0 || to > 1 || to < from) throw new ArgumentOutOfRangeException(nameof(from));
        return new LoadContext(_shared, Token, _start + _width * from, _width * (to - from));
    }
    public void Check() => Token.ThrowIfCancellationRequested();
    public void Report(double fraction, string stage, string detail = "", bool indeterminate = false, bool force = false)
    {
        Check();
        if (_shared.Sink is null) return;
        long now = Stopwatch.GetTimestamp();
        int percent = Math.Clamp((int)Math.Round((_start + _width * Math.Clamp(fraction, 0, 1)) * 100), 0, 100);
        LoadProgress update;
        lock (_shared)
        {
            if (!force && (now - _shared.LastTick) / (double)Stopwatch.Frequency < _shared.Interval) return;
            _shared.LastTick = now;
            _shared.LastPercent = Math.Max(_shared.LastPercent, percent);
            update = new LoadProgress(_shared.LastPercent, stage, detail, indeterminate);
        }
        _shared.Sink.Report(update);
    }
    public byte[] ReadFile(string path)
    {
        Check();
        Report(0, "读取文件", Path.GetFileName(path), force: true);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue) throw new InvalidDataException("此文件超过当前读取器允许的大小。");
        var data = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        int done = 0;
        while (done < data.Length)
        {
            Check();
            int read = stream.Read(data, done, Math.Min(1024 * 1024, data.Length - done));
            if (read == 0) throw new EndOfStreamException("文件内容不完整，读取提前结束。");
            done += read;
            Report(done / (double)Math.Max(1, data.Length), "读取文件", $"{done:N0} / {data.Length:N0} 字节");
        }
        Check();
        Report(1, "读取文件", $"{data.Length:N0} 字节", force: true);
        return data;
    }
}
