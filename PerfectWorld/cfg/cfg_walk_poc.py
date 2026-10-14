import sys,struct,re
cfg=sys.argv[1]; dataf=sys.argv[2]
data=open(dataf,'rb').read(); size=len(data)
def tsize(t):
    t=t.strip()
    if t.startswith('wstring:') or t.startswith('string:'): return int(t.split(':')[1])
    return {'int32':4,'uint32':4,'float':4,'dword':4,'int':4,'int16':2,'word':2,'int8':1,'byte':1,'char':1,'bool':1,'int64':8,'double':8}.get(t)
lines=[l.rstrip('\r\n') for l in open(cfg,encoding='latin1')]
blocks=[]; i=0
while i<len(lines):
    m=re.match(r'^(\d{2,3})\s*-\s*(.+)$', lines[i].strip())
    if m:
        vals=[]; j=i+1
        while j<len(lines) and len(vals)<3:
            if lines[j].strip()!='' : vals.append(lines[j].strip())
            j+=1
        name=m.group(2).strip()
        if len(vals)>=3 and vals[1].upper()=='RAW':
            blocks.append((name,'RAW',None))
        else:
            types=vals[2].split(';') if len(vals)>=3 else []
            sizes=[tsize(t) for t in types]
            blocks.append((name, None if (not sizes or any(s is None for s in sizes)) else sum(sizes), None))
        i=j
    else: i+=1
def u32(p): return struct.unpack_from('<I',data,p)[0] if 0<=p and p+4<=size else -1
MAXC=500000
def try_special(pos):
    if pos+8>size: return None
    ln=u32(pos+4)
    for tr in (4,0):
        after=pos+8+ln+tr
        if 0<after<=size:
            nc=u32(after)
            if 0<=nc<=MAXC: return after
    return None
def read_talkproc(pos):
    cnt=u32(pos); pos+=4
    if cnt<0 or cnt>MAXC: return None
    for _ in range(cnt):
        pos+=4            # A (+0x80 field)
        pos+=0x80         # header (nome "root" etc)
        m=u32(pos); pos+=4  # M sub-itens
        if m<0 or m>MAXC: return None
        for _ in range(m):
            pos+=8
            l1=u32(pos); pos+=4;  pos+=l1*2
            l2=u32(pos); pos+=4;  pos+=l2*0x88
            if pos>size: return None
    return pos
pos=8
for idx,(name,rsz,_) in enumerate(blocks):
    # specials antes do count
    t=0
    while True:
        c=u32(pos)
        if 0<=c<=MAXC: break
        sp=try_special(pos)
        if sp is None or t>6: break
        pos=sp; t+=1
    if rsz=='RAW':
        np=read_talkproc(pos)
        if np is None: print(f"[{idx}] {name} RAW falhou @{pos}"); break
        print(f"[{idx}] {name} RAW: {pos} -> {np} ({np-pos} bytes)"); pos=np; continue
    if rsz is None: print(f"[{idx}] {name} tipo desconhecido"); break
    c=u32(pos)
    if c<0 or c>MAXC: print(f"[{idx}] {name} count inválido {c} @{pos}"); break
    pos+=4+c*rsz
    if pos>size: print(f"[{idx}] {name} ultrapassou @{pos}"); break
print(f"\nprocessados {idx+1}/{len(blocks)} · pos {pos}/{size} · diff {size-pos} {'✓✓✓ EOF EXATO!' if pos==size else ''}")
