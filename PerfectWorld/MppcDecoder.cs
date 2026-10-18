namespace MyAceEditor.PerfectWorld;

/// <summary>MPPC 位流及 PW 的 8 KB 分段封装，不依赖外部 DLL。</summary>
public static class MppcDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> packed, int expectedSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedSize <= 0 || expectedSize > 16 * 1024 * 1024)
            throw new InvalidDataException("MPPC 目标长度无效。");
        // PW 的 uncompress2：长记录由独立的 8192 字节段组成。
        // 每段先有 little-endian ushort；高位表示 MPPC，其余 15 位是输入长度。
        // 长记录也可能使用普通位流；两种路径均必须恰好消费输入并输出指定长度。
        if (expectedSize > 8192)
        {
            try { return DecodeFramed(packed, expectedSize, cancellationToken); }
            catch (InvalidDataException framed)
            {
                try { return DecodePacket(packed, expectedSize, cancellationToken); }
                catch (InvalidDataException flat)
                { throw new InvalidDataException($"8 KB 分段：{framed.Message}；普通位流：{flat.Message}", flat); }
            }
        }
        return DecodePacket(packed, expectedSize, cancellationToken);
    }

    private static byte[] DecodeFramed(ReadOnlySpan<byte> packed, int expectedSize, CancellationToken cancellationToken)
    {
        var output = new byte[expectedSize];
        int input = 0, at = 0;
        while (at < expectedSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packed.Length - input < 2) throw new InvalidDataException("分段长度头截断。");
            int header = packed[input] | (packed[input + 1] << 8); input += 2;
            int count = header & 0x7FFF, size = Math.Min(8192, expectedSize - at);
            if (count == 0 || count > packed.Length - input) throw new InvalidDataException("分段数据长度无效。");
            var chunk = packed.Slice(input, count); input += count;
            if ((header & 0x8000) != 0) DecodePacket(chunk, size, cancellationToken).AsSpan().CopyTo(output.AsSpan(at));
            else
            {
                if (count != size) throw new InvalidDataException("未压缩分段的长度与目标不符。");
                chunk.CopyTo(output.AsSpan(at));
            }
            at += size;
        }
        if (input != packed.Length) throw new InvalidDataException("分段后有未消费的字节。");
        return output;
    }

    private static byte[] DecodePacket(ReadOnlySpan<byte> packed, int expectedSize, CancellationToken cancellationToken)
    {
        var bits = new Bits(packed);
        var output = new byte[expectedSize];
        int at = 0, historyStart = 0, nextCancellationCheck = 0;
        while (at < output.Length)
        {
            if (at >= nextCancellationCheck)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nextCancellationCheck = at + 4096;
            }
            if (bits.Read(1) == 0) { output[at++] = (byte)bits.Read(7); continue; }
            if (bits.Read(1) == 0) { output[at++] = (byte)(0x80 | bits.Read(7)); continue; }
            int distance = bits.Read(1) == 0 ? 320 + bits.Read(13)
                : bits.Read(1) == 0 ? 64 + bits.Read(8) : bits.Read(6);
            // PW 独立块结束标记：不跟随长度；对齐后清空当前块的历史。
            if (distance == 0) { bits.Align(); historyStart = at; continue; }
            int ones = 0;
            while (bits.Read(1) != 0)
                if (++ones > 11) throw new InvalidDataException("MPPC 匹配长度前缀无效。");
            int length = ones == 0 ? 3 : (1 << (ones + 1)) + bits.Read(ones + 1);
            if (distance > 8191 || distance > at - historyStart || length > output.Length - at)
                throw new InvalidDataException("MPPC 引用超出历史或目标记录边界。");
            for (int j = 0; j < length; j++) { output[at] = output[at - distance]; at++; }
        }
        if (bits.Remaining >= 10 && bits.Peek(10) == 0x3C0) { bits.Read(10); bits.Align(); }
        if (bits.Remaining > 31 || !bits.RestIsZero())
            throw new InvalidDataException("MPPC 流含未消费的数据，CFG 记录长度可能不匹配。");
        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    private ref struct Bits
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _at;
        public Bits(ReadOnlySpan<byte> data) { _data = data; _at = 0; }
        public int Remaining => checked(_data.Length * 8 - _at);
        public int Read(int n)
        {
            if (n < 0 || n > 24 || n > Remaining) throw new InvalidDataException("MPPC 位流截断。");
            int value = 0;
            for (int i = 0; i < n; i++, _at++) value = (value << 1) | ((_data[_at / 8] >> (7 - _at % 8)) & 1);
            return value;
        }
        public int Peek(int n) { int at = _at, v = Read(n); _at = at; return v; }
        public void Align()
        {
            int padding = (8 - _at % 8) % 8;
            if (padding > Remaining || (padding > 0 && Read(padding) != 0)) throw new InvalidDataException("MPPC 对齐填充无效。");
        }
        public bool RestIsZero() { while (Remaining > 0) if (Read(1) != 0) return false; return true; }
    }
}
