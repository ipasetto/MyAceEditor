using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Protocolo interno do servidor (GNET, PW-KB §S8): cada pacote = CompactUINT(tipo) + Octets(dados); inteiros em
/// big-endian; Octets = CompactUINT(tamanho) + bytes; vetor = CompactUINT(n) + itens; textos em UTF-16LE dentro de Octets.
/// Rpc = xid (u32, bit 31 = pedido) + argumento; a resposta volta com o mesmo tipo e o xid sem o bit 31.
/// É o que o iweb do pacote usa nas portas 29100 (gdeliveryd) e 29400 (gamedbd), sem criptografia.
/// </summary>
public sealed class GnetWriter
{
    readonly MemoryStream _m = new();
    public byte[] ToArray() => _m.ToArray();
    public GnetWriter Byte(byte v) { _m.WriteByte(v); return this; }
    public GnetWriter Int(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); _m.Write(b); return this; }
    public GnetWriter UInt(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); _m.Write(b); return this; }
    public GnetWriter Compact(uint x)
    {
        Span<byte> b = stackalloc byte[5];
        if (x < 0x80) { _m.WriteByte((byte)x); }
        else if (x < 0x4000) { BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)(x | 0x8000)); _m.Write(b[..2]); }
        else if (x < 0x20000000) { BinaryPrimitives.WriteUInt32BigEndian(b, x | 0xC0000000); _m.Write(b[..4]); }
        else { b[0] = 0xE0; BinaryPrimitives.WriteUInt32BigEndian(b[1..], x); _m.Write(b); }
        return this;
    }
    public GnetWriter Octets(ReadOnlySpan<byte> v) { Compact((uint)v.Length); _m.Write(v); return this; }
    public GnetWriter Text(string s) => Octets(Encoding.Unicode.GetBytes(s));
}

public sealed class GnetReader
{
    readonly byte[] _d; int _p;
    public GnetReader(byte[] d, int start = 0) { _d = d; _p = start; }
    public bool End => _p >= _d.Length;
    public byte Byte() => _d[_p++];
    public int Int() { var v = BinaryPrimitives.ReadInt32BigEndian(_d.AsSpan(_p)); _p += 4; return v; }
    public uint UInt() { var v = BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(_p)); _p += 4; return v; }
    public ushort UShort() { var v = BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(_p)); _p += 2; return v; }
    public float Float() => BitConverter.Int32BitsToSingle(Int());
    public uint Compact()
    {
        switch (_d[_p] & 0xE0)
        {
            case 0xE0: _p++; return UInt();
            case 0xC0: return UInt() & 0x1FFFFFFF;
            case 0x80: case 0xA0: return UShort() & 0x7FFFu;
            default: return _d[_p++];
        }
    }
    public byte[] Octets() { int n = (int)Compact(); var v = _d.AsSpan(_p, n).ToArray(); _p += n; return v; }
    public string Text() => Encoding.Unicode.GetString(Octets()).TrimEnd('\0');
    public void Skip(int n) => _p += n;
}

/// <summary>Uma conexão TCP com um serviço do servidor. Não é segura entre threads: uma operação por vez.</summary>
public sealed class GnetConnection : IDisposable
{
    readonly TcpClient _tcp = new();
    readonly NetworkStream _s;
    readonly List<byte> _buf = new();
    static int _xid;

    public GnetConnection(string host, int port, int timeoutMs = 4000)
    {
        if (!_tcp.ConnectAsync(host, port).Wait(timeoutMs)) throw new IOException($"Porta {port} não respondeu.");
        _s = _tcp.GetStream();
        _s.ReadTimeout = timeoutMs;
    }

    public void Send(uint type, byte[] data)
    {
        var w = new GnetWriter().Compact(type).Octets(data).ToArray();
        _s.Write(w);
    }

    /// <summary>Espera o próximo pacote do tipo pedido (ignora os outros, ex.: avisos que o gdeliveryd manda ao conectar).</summary>
    public GnetReader Receive(uint type, Func<GnetReader, bool>? match = null, int timeoutMs = 6000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        var chunk = new byte[64 * 1024];
        while (true)
        {
            while (TryTake(out var t, out var data))
            {
                if (t != type) continue;
                if (match is null || match(new GnetReader(data))) return new GnetReader(data);
            }
            if (Environment.TickCount64 > until) throw new TimeoutException($"O servidor não respondeu (pacote {type}).");
            int n;
            try { n = _s.Read(chunk); }
            catch (IOException) when (Environment.TickCount64 < until) { continue; }
            if (n <= 0) throw new IOException("O servidor fechou a conexão.");
            _buf.AddRange(chunk.AsSpan(0, n).ToArray());
        }
    }

    bool TryTake(out uint type, out byte[] data)
    {
        type = 0; data = Array.Empty<byte>();
        if (_buf.Count == 0) return false;
        try
        {
            var arr = _buf.ToArray();
            var r = new GnetReader(arr);
            type = r.Compact();
            int n = (int)r.Compact();
            int head = HeadLen(arr);
            if (arr.Length < head + n) return false;
            data = arr.AsSpan(head, n).ToArray();
            _buf.RemoveRange(0, head + n);
            return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException) { return false; }
    }

    static int Len(byte b) => (b & 0xE0) switch { 0xE0 => 5, 0xC0 => 4, 0x80 or 0xA0 => 2, _ => 1 };
    static int HeadLen(byte[] a) { int t = Len(a[0]); return t + Len(a[t]); }

    /// <summary>Rpc: envia argumento com xid novo e devolve o leitor já depois do xid da resposta.</summary>
    public GnetReader Rpc(uint type, Action<GnetWriter> argument, int timeoutMs = 6000)
    {
        uint xid = (uint)Interlocked.Increment(ref _xid) & 0x7FFFFFFF;
        var w = new GnetWriter().UInt(xid | 0x80000000);
        argument(w);
        Send(type, w.ToArray());
        var r = Receive(type, x => x.UInt() == xid, timeoutMs);
        r.UInt();
        return r;
    }

    public void Dispose() { _s?.Dispose(); _tcp.Dispose(); }
}
