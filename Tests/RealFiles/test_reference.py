"""Regression tests for the independent reference reader (not C# execution)."""
from pathlib import Path
import struct
import tempfile
import subprocess
import ctypes
import re
import verify_real as ref


def literal(data):
    value, count, out = 0, 0, bytearray()
    for byte in data:
        width, bits = (9, 0x100 | (byte & 0x7f)) if byte >= 128 else (8, byte)
        value = (value << width) | bits; count += width
        while count >= 8:
            count -= 8; out.append((value >> count) & 255)
    if count: out.append((value << (8-count)) & 255)
    return bytes(out)


def framed(data, raw=False):
    out = bytearray()
    for at in range(0, len(data), 8192):
        part = data[at:at+8192]; encoded = part if raw else literal(part)
        out += struct.pack('<H', len(encoded) | (0 if raw else 0x8000)) + encoded
    return out


def make(blocks, version, layout):
    out = bytearray(struct.pack('<I', 0x30000000 | version))
    # Nonempty records on both sides of empty lists catch the empty-list alignment bug.
    last = max(i for i,b in enumerate(blocks) if not b['raw'] and b['size'] <= 16000)
    for i,b in enumerate(blocks):
        if b['offset'] == 'AUTO':
            if not i: out += struct.pack('<II', 0x12345678, 0)
            elif layout != 'MPPC':
                tag = 0xab7689dd if b['name'] == 'SKILLTOME_SUB_TYPE' else 0xee35679f
                out += struct.pack('<II', tag, 0)
                if tag == 0xab7689dd: out += b'\0'*4
        else: out += b'\0'*int(b['offset'])
        if not i and layout == 'MPPC': out += struct.pack('<IIIII', 0xab7689dd, 0, 0, 0xee35679f, 0)
        if b['raw']: out += struct.pack('<I', 0); continue
        n = int(i in (0, last))
        raw = struct.pack('<I', 7) + bytes(b['size']-4) if n else b''
        if layout == 'SIZED': out += struct.pack('<I', 777)
        out += struct.pack('<I', n)
        if layout == 'SIZED': out += struct.pack('<I', b['size'] if n else 0)
        if layout == 'MPPC':
            if n:
                packed = bytes(framed(raw)) if len(raw)>8192 else literal(raw)
                out += struct.pack('<IHI', 7, len(packed), len(packed)) + packed
        else: out += raw
    return bytes(out)


def main():
    checks = 0
    with tempfile.TemporaryDirectory(prefix='myace-reference-tests-') as temp:
        library = Path(temp)/'codec.so'
        subprocess.run(['g++', '-O2', '-Wall', '-Wextra', '-Werror', '-fPIC', '-shared',
                        str(Path(__file__).with_name('mppc_reference.cpp')), '-o', str(library)], check=True)
        codec = ctypes.CDLL(str(library))
        codec.mppc.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int]
        def native(data, size):
            out, error = ctypes.create_string_buffer(size), ctypes.create_string_buffer(256)
            result = codec.mppc(bytes(data), len(data), out, size, error, 256)
            if result < 0: raise ValueError(error.value.decode())
            return out.raw
        vectors = [(bytes.fromhex('414243F0C0'), b'ABCABC'), (bytes.fromhex('B380'), b'\xe7'),
                   (bytes.fromhex('41F00042'), b'AB')]
        for packed,raw in vectors:
            assert ref.decode(packed, len(raw)) == native(packed, len(raw)) == raw; checks += 1
        for size in (8193, 16384, 16385):
            raw = bytes((i*37+11) & 255 for i in range(size))
            for packed in (framed(raw), framed(raw, True), literal(raw)):
                assert ref.decode(packed, size) == native(packed, size) == raw; checks += 1
            for decoder in (ref.decode, native):
                try: decoder(framed(raw)[:-2], size)
                except ValueError: checks += 1
                else: raise AssertionError('Truncated frame was accepted')
        for decoder in (ref.decode, native):
            for packed,size in ((bytes.fromhex('F0C0'),3),(b'\x80',1)):
                try: decoder(packed,size)
                except ValueError: checks += 1
                else: raise AssertionError('Bad packet was accepted')
        directory = Path(__file__).resolve().parents[2]/'PerfectWorld/cfg'
        plain, compressed = 0,0
        for path in sorted(directory.glob('*.cfg')):
            version = int(re.search(r'_v(\d+)',path.stem)[1]); blocks = ref.load_cfg(path)
            layout = 'SIZED' if version >= 191 else 'COUNT'
            formats = (layout, 'MPPC') if version >= 191 else (layout,)
            for fmt in formats:
                data = make(blocks, version, fmt)
                assert ref.check(data, blocks, fmt, native)['eof']; checks += 1
                try: ref.check(data[:-1], blocks, fmt, native)
                except ValueError: checks += 1
                else: raise AssertionError('Truncated file was accepted: '+path.name)
                if fmt == 'MPPC': compressed += 1
                else: plain += 1
        print(f'PASS independent reference: {checks} checks, {plain} plaintext fixtures, {compressed} MPPC fixtures. C# not executed.')


if __name__ == '__main__': main()
