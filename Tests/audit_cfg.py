"""Audit actual CFG definitions; this does not certify real elements.data files."""
from pathlib import Path
from collections import Counter
import csv
import re
import sys

project = Path(__file__).resolve().parents[1]
directory = Path(sys.argv[1]) if len(sys.argv) > 1 else project / 'PerfectWorld/cfg'
output = Path(sys.argv[2]) if len(sys.argv) > 2 else project / 'docs/CFG_audit.tsv'
sizes = {'int32':4,'uint32':4,'int':4,'uint':4,'float':4,'dword':4,
         'int64':8,'uint64':8,'long':8,'ulong':8,'double':8,
         'int16':2,'uint16':2,'short':2,'ushort':2,'word':2,
         'int8':1,'uint8':1,'sbyte':1,'byte':1,'char':1,'bool':1}
rows = []
versions = set()
total_blocks = 0
suffixes = 0
repeated = 0
for path in sorted(directory.rglob('*.cfg')):
    version = re.search(r'_v(\d+)(?:[_-].+)?$',path.stem,re.I)
    if not version:
        rows.append([path.name,'','','','CFG_ERROR','版本文件名无法识别']); continue
    v = int(version[1]); versions.add(v)
    try:
        text = path.read_text(encoding='utf-8-sig')
        lines = [x.strip() for x in text.replace('\r','').split('\n') if x.strip() and not x.strip().startswith(('#','//'))]
        count, talk = int(lines[0]), int(lines[1])
        if (len(lines)-2) % 4: raise ValueError('数据块不是完整的四行定义')
        blocks = []; warnings = []; block_names = set()
        for i in range(2,len(lines),4):
            match = re.fullmatch(r'(\d+)(?:_(\d+))?\s*-\s*(.+)',lines[i])
            if not match: raise ValueError(f'数据块标题无效：{lines[i]}')
            name = match[3]; suffixes += bool(match[2])
            if name in block_names: raise ValueError(f'数据块名称重复：{name}')
            block_names.add(name)
            if lines[i+1] != 'AUTO' and not (lines[i+1].isdigit() and int(lines[i+1]) <= 16*1024*1024):
                raise ValueError(f'偏移无效：{name}')
            fields = lines[i+2].rstrip(';').split(';')
            types = lines[i+3].rstrip(';').split(';')
            raw = fields == ['RAW']
            if raw:
                if types != ['byte:AUTO']: raise ValueError(f'RAW 类型错误：{name}')
            else:
                if len(fields) != len(types): warnings.append(f'{name}：{len(fields)} 个字段名 / {len(types)} 个类型；未猜测缺失类型')
                repeated += len(fields) - len(set(fields))
                for t in types:
                    if t in sizes: continue
                    m = re.fullmatch(r'(wstring|string|byte|bytes):(\d+)',t)
                    if not m or not 0 < int(m[2]) <= 16*1024*1024 or (m[1]=='wstring' and int(m[2])%2):
                        raise ValueError(f'不支持类型：{t}')
            blocks.append((name,raw))
        if len(blocks)!=count: raise ValueError(f'声明 {count} 块，实际 {len(blocks)} 块')
        if not (0<=talk<count and blocks[talk][1]): raise ValueError('对话块索引不符')
        total_blocks += len(blocks)
        rows.append([path.name,v,count,len(blocks),'CFG_WARNING' if warnings else 'CFG_OK',' | '.join(warnings)])
    except Exception as ex:
        rows.append([path.name,v,'','','CFG_ERROR',str(ex)])
output.parent.mkdir(parents=True,exist_ok=True)
with output.open('w',encoding='utf-8-sig',newline='') as stream:
    writer=csv.writer(stream,delimiter='\t')
    writer.writerow(['CFG','版本','声明块数','解析块数','CFG结构检查','说明（不代表真实游戏数据已验证）'])
    writer.writerows(rows)
counts=Counter(row[4] for row in rows)
print(f'CFG files={len(rows)}; distinct versions={len(versions)}; range={min(versions)}..{max(versions)}; blocks={total_blocks}')
print(f'Status={dict(counts)}; suffixed block headings={suffixes}; repeated field occurrences={repeated}')
print('This is a CFG structure audit. C# compilation and real game-file validation are separate.')
sys.exit(1 if counts['CFG_ERROR'] else 0)
