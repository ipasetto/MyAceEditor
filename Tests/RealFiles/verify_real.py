"""Independent byte-level reference check. This does not execute the C# application.
Usage: python Tests/RealFiles/verify_real.py <elements-directory> [--native] [--report report.json]
--native compiles the bundled independent MPPC checker with g++ into a temporary directory.
Input files are never modified; no game payload is written to the report.
"""
from pathlib import Path
import argparse
import ctypes
import hashlib
import json
import re
import shutil
import struct
import subprocess
import tempfile
import time

SIZES = dict(int=4, int32=4, uint=4, uint32=4, dword=4, float=4, int64=8,
             uint64=8, long=8, ulong=8, double=8, int16=2, uint16=2,
             short=2, ushort=2, word=2, int8=1, uint8=1, sbyte=1, byte=1, char=1, bool=1)


def load_cfg(path):
    lines = [x.strip() for x in path.read_text(encoding='utf-8-sig').split('\n')
             if x.strip() and not x.strip().startswith(('#', '//'))]
    blocks = []
    for at in range(2, len(lines), 4):
        raw = lines[at+2] == 'RAW'
        types = lines[at+3].rstrip(';').split(';')
        size = 0 if raw else sum(SIZES[t] if t in SIZES else int(t.split(':')[1]) for t in types)
        blocks.append(dict(name=lines[at].split(' - ', 1)[1], offset=lines[at+1], raw=raw, size=size, fields=lines[at+2], types=lines[at+3]))
    if len(blocks) != int(lines[0]):
        raise ValueError('CFG block count differs')
    return blocks


class Bits:
    def __init__(self, data): self.data, self.at = data, 0
    def read(self, n):
        if n < 0 or self.at+n > len(self.data)*8: raise ValueError('MPPC truncated')
        value = 0
        for _ in range(n):
            value = value << 1 | (self.data[self.at//8] >> (7-self.at%8) & 1)
            self.at += 1
        return value
    def align(self):
        if self.read((-self.at) % 8): raise ValueError('MPPC padding is nonzero')


def packet(data, size):
    bits, out, history = Bits(data), bytearray(), 0
    while len(out) < size:
        if bits.read(1) == 0: out.append(bits.read(7)); continue
        if bits.read(1) == 0: out.append(128 | bits.read(7)); continue
        distance = 320+bits.read(13) if bits.read(1) == 0 else 64+bits.read(8) if bits.read(1) == 0 else bits.read(6)
        if distance == 0: bits.align(); history = len(out); continue
        ones = 0
        while bits.read(1):
            ones += 1
            if ones > 11: raise ValueError('MPPC length prefix')
        length = 3 if ones == 0 else (1 << (ones+1))+bits.read(ones+1)
        if distance > 8191 or distance > len(out)-history or length > size-len(out):
            raise ValueError('MPPC invalid reference')
        for _ in range(length): out.append(out[-distance])
    if len(data)*8-bits.at >= 10:
        saved = bits.at
        if bits.read(10) == 0x3c0: bits.align()
        else: bits.at = saved
    remaining = len(data)*8-bits.at
    if remaining > 31 or bits.read(remaining): raise ValueError('MPPC trailing data')
    return out


def decode(data, size):
    if size > 8192:
        try:
            at, out = 0, bytearray()
            while len(out) < size:
                header, = struct.unpack_from('<H', data, at); at += 2
                count, wanted = header & 0x7fff, min(8192, size-len(out))
                if not count or count > len(data)-at: raise ValueError('Frame length')
                part = data[at:at+count]; at += count
                raw = packet(part, wanted) if header & 0x8000 else part
                if len(raw) != wanted: raise ValueError('Raw frame length')
                out += raw
            if at != len(data): raise ValueError('Framed trailing data')
            return out
        except (ValueError, struct.error): pass
    return packet(data, size)


def check(data, blocks, layout, decoder, full=True):
    at, rows, records, dialogs, empty = 4, [], 0, 0, 0
    def skip(count):
        nonlocal at
        if count < 0 or count > len(data)-at: raise ValueError(f'Boundary at {at}: {count} bytes')
        at += count
    def u32():
        nonlocal at
        skip(4)
        return struct.unpack_from('<I', data, at-4)[0]
    def count():
        n = u32()
        if n > 1_000_000: raise ValueError(f'Count {n} exceeds limit')
        return n
    def exporter():
        for _ in range(4):
            if len(data)-at < 8: return
            tag = struct.unpack_from('<I', data, at)[0]
            if tag not in (0xab7689dd, 0xee35679f): return
            u32(); skip(u32())
            if tag == 0xab7689dd: skip(4)
    for i, block in enumerate(blocks):
        try:
            if block['offset'] == 'AUTO':
                if i and layout == 'MPPC': exporter()
                else:
                    u32(); skip(u32())
                    if i and block['name'] == 'SKILLTOME_SUB_TYPE': skip(4)
            else: skip(int(block['offset']))
            if not i and layout == 'MPPC': exporter()
            if block['raw']:
                if block['name'] != 'TALK_PROC': raise ValueError('Unsupported RAW block')
                n = count(); dialogs += n
                for _ in range(n):
                    skip(132); windows = count()
                    for _ in range(windows):
                        skip(8); skip(u32()*2); skip(count()*136)
                continue
            if layout == 'SIZED': u32()
            count_at = at; n = count(); empty += n == 0
            if layout == 'MPPC':
                if n:
                    table = at; skip(n*6); total = u32(); payload = at; skip(total); consumed = 0
                    for row in range(n):
                        ident, size = struct.unpack_from('<IH', data, table+row*6)
                        if not size or size > total-consumed: raise ValueError('Index size invalid')
                        if full or row in (0, n-1):
                            raw = decoder(data[payload+consumed:payload+consumed+size], block['size'])
                            if struct.unpack_from('<I', raw)[0] != ident: raise ValueError('Decoded ID differs')
                        consumed += size
                    if consumed != total: raise ValueError('Index total differs')
            else:
                size = u32() if layout == 'SIZED' else block['size']
                if size != block['size'] and (n or size != 0): raise ValueError('CFG record size differs')
                skip(n*size)
                rows.append((count_at, n, at))
            records += n
        except (ValueError, struct.error) as ex:
            raise ValueError(f'Block {i+1}/{len(blocks)} {block["name"]}, offset {at}: {ex}') from ex
    if at != len(data): raise ValueError(f'Trailing {len(data)-at} bytes')
    # Rebuild plaintext count/record spans, retaining headers and RAW gaps; no output file.
    identical = None
    if layout != 'MPPC' and full:
        rebuilt, copied = bytearray(), 0
        for start, n, end in rows:
            rebuilt += data[copied:start] + struct.pack('<I', n) + data[start+4:end]
            copied = end
        rebuilt += data[copied:]
        identical = rebuilt == data
        if not identical: raise ValueError('Plain span reconstruction differs')
    return dict(blocks=len(blocks), records=records, dialogs=dialogs, empty_lists=empty,
                bytes=at, eof=True, reference_span_roundtrip=identical)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('data_directory', type=Path)
    parser.add_argument('--cfg', type=Path, default=Path(__file__).resolve().parents[2]/'PerfectWorld/cfg')
    parser.add_argument('--native', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    files = sorted(args.data_directory.rglob('*.data'))
    if not files: parser.error('No .data files found')
    results, native_stats = [], dict(records=0, framed_records=0, raw_frames=0)
    with tempfile.TemporaryDirectory(prefix='myace-reference-') as work:
        decoder = decode
        if args.native:
            if not shutil.which('g++'): parser.error('--native requires g++')
            library = Path(work)/'mppc-reference.so'
            subprocess.run(['g++', '-O2', '-fPIC', '-shared', str(Path(__file__).with_name('mppc_reference.cpp')), '-o', str(library)], check=True)
            codec = ctypes.CDLL(str(library))
            codec.mppc.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int]
            def decoder(data, size):
                out, error = ctypes.create_string_buffer(size), ctypes.create_string_buffer(256)
                result = codec.mppc(data, len(data), out, size, error, 256)
                if result < 0: raise ValueError(error.value.decode())
                native_stats['records'] += 1
                if result % 2:
                    native_stats['framed_records'] += 1
                    native_stats['raw_frames'] += (result-1)//2
                return out.raw
        for file in files:
            started = time.monotonic(); data = file.read_bytes()
            row = dict(file=file.name, sha256=hashlib.sha256(data).hexdigest(), length=len(data))
            before = native_stats.copy()
            try:
                version = struct.unpack_from('<H', data)[0]; row['version'] = version
                candidates, seen, errors = [], set(), []
                for cfg_path in sorted(args.cfg.rglob('*.cfg')):
                    match = re.search(r'_v(\d+)(?:[_-].+)?$', cfg_path.stem)
                    if not match or int(match[1]) != version: continue
                    blocks = load_cfg(cfg_path)
                    key = json.dumps(blocks, sort_keys=True)
                    if key in seen: continue
                    seen.add(key)
                    formats = ('SIZED', 'MPPC', 'COUNT') if version >= 191 else ('COUNT',)
                    for layout in formats:
                        try:
                            check(data, blocks, layout, decode, full=False)
                            candidates.append((cfg_path, blocks, layout)); break
                        except (ValueError, struct.error) as ex: errors.append(str(ex))
                if len(candidates) != 1:
                    raise ValueError(f'{len(candidates)} matching CFG candidates. ' + ' | '.join(errors[-3:]))
                cfg_path, blocks, layout = candidates[0]
                row.update(check(data, blocks, layout, decoder))
                row.update(cfg=cfg_path.name, layout=layout, status='PASS')
            except (ValueError, struct.error) as ex: row.update(status='FAIL', error=str(ex))
            row['seconds'] = round(time.monotonic()-started, 3)
            if args.native: row['native_decode'] = {k:native_stats[k]-before[k] for k in native_stats}
            results.append(row); print(json.dumps(row, ensure_ascii=False), flush=True)
        report = dict(method='Independent Python/C++ reference; C# application not executed', files=results)
        if args.report: args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    return 0 if all(row['status'] == 'PASS' for row in results) else 1


if __name__ == '__main__': raise SystemExit(main())
