namespace MyAceEditor.PerfectWorld;

/// <summary>Fluxo de bits MPPC e o encapsulamento em segmentos de 8 KB do PW, sem DLL externa.</summary>
public static class MppcDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> packed, int expectedSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedSize <= 0 || expectedSize > 16 * 1024 * 1024)
            throw new InvalidDataException("Tamanho de destino MPPC inválido.");
        // uncompress2 do PW: registros longos são feitos de segmentos independentes de 8192 bytes.
        // Cada segmento começa com ushort little-endian; o bit alto indica MPPC, os outros 15 são o tamanho de entrada.
        // Registros longos também podem usar fluxo contínuo; os dois caminhos precisam consumir toda a entrada e gerar o tamanho exato.
        if (expectedSize > 8192)
        {
            try { return DecodeFramed(packed, expectedSize, cancellationToken); }
            catch (InvalidDataException framed)
            {
                try { return DecodePacket(packed, expectedSize, cancellationToken); }
                catch (InvalidDataException flat)
                { throw new InvalidDataException($"segmentos de 8 KB: {framed.Message}; fluxo contínuo: {flat.Message}", flat); }
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
            if (packed.Length - input < 2) throw new InvalidDataException("Cabeçalho de segmento truncado.");
            int header = packed[input] | (packed[input + 1] << 8); input += 2;
            int count = header & 0x7FFF, size = Math.Min(8192, expectedSize - at);
            if (count == 0 || count > packed.Length - input) throw new InvalidDataException("Tamanho de segmento inválido.");
            var chunk = packed.Slice(input, count); input += count;
            if ((header & 0x8000) != 0) DecodePacket(chunk, size, cancellationToken).AsSpan().CopyTo(output.AsSpan(at));
            else
            {
                if (count != size) throw new InvalidDataException("Segmento não comprimido com tamanho diferente do destino.");
                chunk.CopyTo(output.AsSpan(at));
            }
            at += size;
        }
        if (input != packed.Length) throw new InvalidDataException("Sobraram bytes após os segmentos.");
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
            // Marca de fim de bloco do PW: sem tamanho; após alinhar, limpa o histórico do bloco.
            if (distance == 0) { bits.Align(); historyStart = at; continue; }
            int ones = 0;
            while (bits.Read(1) != 0)
                if (++ones > 11) throw new InvalidDataException("Prefixo de tamanho MPPC inválido.");
            int length = ones == 0 ? 3 : (1 << (ones + 1)) + bits.Read(ones + 1);
            if (distance > 8191 || distance > at - historyStart || length > output.Length - at)
                throw new InvalidDataException("Referência MPPC fora do histórico ou do registro.");
            for (int j = 0; j < length; j++) { output[at] = output[at - distance]; at++; }
        }
        if (bits.Remaining >= 10 && bits.Peek(10) == 0x3C0) { bits.Read(10); bits.Align(); }
        if (bits.Remaining > 31 || !bits.RestIsZero())
            throw new InvalidDataException("Fluxo MPPC com dados não consumidos; o tamanho de registro do CFG pode estar errado.");
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
            if (n < 0 || n > 24 || n > Remaining) throw new InvalidDataException("Fluxo MPPC truncado.");
            int value = 0;
            for (int i = 0; i < n; i++, _at++) value = (value << 1) | ((_data[_at / 8] >> (7 - _at % 8)) & 1);
            return value;
        }
        public int Peek(int n) { int at = _at, v = Read(n); _at = at; return v; }
        public void Align()
        {
            int padding = (8 - _at % 8) % 8;
            if (padding > Remaining || (padding > 0 && Read(padding) != 0)) throw new InvalidDataException("Preenchimento de alinhamento MPPC inválido.");
        }
        public bool RestIsZero() { while (Remaining > 0) if (Read(1) != 0) return false; return true; }
    }
}
