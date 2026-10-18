"""Source-only Chinese UI/CFG label audit. This does not replace Windows visual QA."""
from pathlib import Path
from collections import Counter
import re
import csv
import json
import hashlib

class CSharpScanner:
    """Lex C# strings/interpolations and compare everything outside display text."""
    def __init__(self,s):
        self.s=s
        self.i=0
        self.strings=[]
        self.starts=[]
    def quoted(self,prefix):
        s=self.s
        verbatim='@' in prefix
        interpolated='$' in prefix
        start=self.i
        self.i+=len(prefix)+1
        expressions=[]
        while self.i<len(s):
            c=s[self.i]
            if c=='"':
                if verbatim and s[self.i:self.i+2]=='""':
                    self.i+=2
                    continue
                self.i+=1
                self.strings.append(s[start:self.i])
                self.starts.append(start)
                return ('STRING',prefix,tuple(sorted(expressions,key=repr)))
            if not verbatim and c=='\\':
                self.i+=2
                continue
            if not verbatim and c in '\r\n':
                raise ValueError(f'Unescaped newline in string at {self.i}')
            if interpolated and c=='{':
                if s[self.i:self.i+2]=='{{':
                    self.i+=2
                    continue
                self.i+=1
                expressions.append(tuple(self.code(stop=True)))
                continue
            if interpolated and c=='}':
                if s[self.i:self.i+2]!='}}':
                    raise ValueError(f'Unescaped closing interpolation brace at {self.i}')
                self.i+=2
                continue
            self.i+=1
        raise ValueError(f'Unterminated string at {start}')
    def code(self,stop=False):
        s=self.s
        tokens=[]
        stack=[]
        while self.i<len(s):
            c=s[self.i]
            if c.isspace():
                self.i+=1
                continue
            if s.startswith('//',self.i):
                end=s.find('\n',self.i)
                self.i=len(s) if end<0 else end+1
                continue
            if s.startswith('/*',self.i):
                end=s.find('*/',self.i+2)
                if end<0: raise ValueError('Unterminated comment')
                self.i=end+2
                continue
            prefix=next((p for p in ('$@','@$','$','@','') if s.startswith(p+'"',self.i)),None)
            if prefix is not None:
                tokens.append(self.quoted(prefix))
                continue
            if c=="'":
                start=self.i
                self.i+=1
                while self.i<len(s):
                    if s[self.i]=='\\': self.i+=2
                    elif s[self.i]=="'":
                        self.i+=1
                        break
                    else: self.i+=1
                else: raise ValueError('Unterminated char literal')
                tokens.append(s[start:self.i])
                continue
            if c=='}' and not stack and stop:
                self.i+=1
                return tokens
            if c in '{([':
                stack.append(c)
            elif c in '})]':
                if not stack or '{(['['})]'.index(c)]!=stack[-1]:
                    raise ValueError(f'Unmatched {c} at {self.i}')
                stack.pop()
            if c.isalnum() or c=='_':
                start=self.i
                self.i+=1
                while self.i<len(s) and (s[self.i].isalnum() or s[self.i]=='_'): self.i+=1
                tokens.append(s[start:self.i])
            else:
                tokens.append(c)
                self.i+=1
        if stack or stop: raise ValueError('Unclosed delimiters')
        return tokens

def dictionary(source, name):
    start = source.index('Dictionary<string, string> ' + name)
    end = source.index('    };', start)
    pairs = re.findall(r'\["([^"\n]+)"\]\s*=\s*"([^"\n]+)"', source[start:end])
    keys = [k.casefold() for k, _ in pairs]
    if len(keys) != len(set(keys)): raise ValueError('Duplicate display dictionary key: ' + name)
    return {k.casefold():v for k,v in pairs}


def main():
    root = Path(__file__).resolve().parents[1]
    docs = root/'docs'
    fields = (root/'PerfectWorld/FieldLabels.cs').read_text()
    direct, words = dictionary(fields, 'Map'), dictionary(fields, 'Tokens')
    extra = dictionary((root/'PerfectWorld/FieldLexicon.cs').read_text(), 'Words')
    if words.keys() & extra.keys(): raise ValueError('Supplemental words overlap existing vocabulary')
    words.update(extra)
    blocks = dictionary((root/'PerfectWorld/BlockLabels.cs').read_text(), 'Names')
    keys, block_names, untranslated, examples = set(), set(), Counter(), {}
    for path in sorted((root/'PerfectWorld/cfg').glob('*.cfg')):
        lines = [x.strip() for x in path.read_text(encoding='utf-8-sig').split('\n')
                 if x.strip() and not x.strip().startswith(('#','//'))]
        for i in range(2, len(lines), 4):
            name = re.match(r'^\d+(?:_\d+)?\s*-\s*(.+)$', lines[i])[1]
            block_names.add(name)
            if lines[i+2] == 'RAW': continue
            keys.update(lines[i+2].rstrip(';').split(';'))
    missing_blocks = sorted(n for n in block_names if n.casefold() not in blocks)
    # This mirrors the label routing to measure vocabulary coverage; it never changes a schema.
    for field in sorted(keys):
        if field.casefold() in direct: continue
        if re.match(r'^(addons|rands|uniques)_(\d+)_(id|probability)', field): continue
        spaced = re.sub(r'([a-z0-9])([A-Z])', r'\1_\2', field)
        for part in spaced.split('_'):
            key = part.casefold()
            base = re.sub(r'\d+$','',key)
            if not re.search('[a-z]',key) or key in words or base in words or re.fullmatch(r'v\d+',key): continue
            untranslated[key] += 1; examples.setdefault(key, field)
    retained = []
    for word,count in untranslated.most_common():
        reason = '参数代号，保留原符号' if re.fullmatch('[a-z]',word) else '原始缩写，含义未确认，保留标识'
        retained.append(dict(identifier=word, unique_field_occurrences=count, example=examples[word], reason=reason))
    with (docs/'汉化字段保留标识.tsv').open('w',encoding='utf-8-sig',newline='') as stream:
        writer = csv.writer(stream,delimiter='\t'); writer.writerow(['保留标识','涉及不同字段次数','字段示例','原因'])
        for row in retained: writer.writerow(row.values())

    # Fixed text selection uses actual C# lexical strings, ignoring comments and field/file identifiers.
    literal_rows, missing_fixed = [], []
    sink = re.compile(r'(?:\b(?:Text|Title|Description|PlaceholderText|Filter|ToolTipText)\s*=\s*|'
                      r'\.(?:Columns|DropDownItems)\.Add\(\s*|'
                      r'\b(?:TabPage|M|B|Btn|FootBtn|Title|Info|G|I|F|B)\(\s*)$')
    technical = {'ID','NPC','AI','PCK','CFG','%','∞','X','Y','Z'}
    for path in sorted((root/'UI').glob('*.cs')) + [root/'PerfectWorld/TaskSchema.cs',root/'PerfectWorld/AddonText.cs']:
        source = path.read_text(); scanner = CSharpScanner(source); scanner.code()
        for raw,start in zip(scanner.strings, scanner.starts):
            # Starts are stored at string opening and align with string collection for non-interpolated strings.
            if '$' in raw[:raw.index('"')]: continue
            before = source[max(0,start-90):start]
            if not sink.search(before): continue
            text = raw[raw.index('"')+1:-1]
            chinese = bool(re.search('[\u4e00-\u9fff]',text))
            status = '中文' if chinese else '技术标识/符号' if text in technical or not re.search('[A-Za-zÀ-ÿ]',text) else '待处理外文'
            row = dict(file=str(path.relative_to(root)),line=source.count('\n',0,start)+1,text=text,status=status)
            literal_rows.append(row)
            if status == '待处理外文': missing_fixed.append(row)
    with (docs/'汉化固定界面.tsv').open('w',encoding='utf-8-sig',newline='') as stream:
        writer = csv.writer(stream,delimiter='\t');writer.writerow(['源码','行','固定文字','判定'])
        for row in literal_rows:writer.writerow(row.values())
    baseline = json.loads((root/'Tests/cfg_original_hashes.json').read_text())
    actual_hashes = {p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'PerfectWorld/cfg').glob('*.cfg')}
    ui = (root/'UI/EditorForm.cs').read_text()
    dialog_titles = []
    for match in re.finditer(r'new\s+(OpenFileDialog|SaveFileDialog|FolderBrowserDialog)\s*\{([^{}]*)\}', ui, re.S):
        kind, properties = match.groups()
        good = bool(re.search(r'Title\s*=', properties)) if kind != 'FolderBrowserDialog' else 'UseDescriptionForTitle = true' in properties
        dialog_titles.append(good)

    routes = {
        'find_uses_chinese_label': 'new FieldEntry(k, FieldLabels.Label(k))' in ui,
        'find_preserves_original_key': 'string key = selected.Key;' in ui,
        'status_uses_chinese_field': '条件为 {FieldLabels.Label(key)}' in ui,
        'grid_keeps_original_key_tooltip': '原始字段：' in ui,
        'app_dialog_buttons_are_owned_chinese': 'MessageBox.Show' not in ui and 'ChineseDialogs.Confirm' in ui,
        'layout_name_uses_chinese': 'ElementsFormatLabels.Label(result.Format)' in ui,
        'original_cfg_bytes_unchanged': actual_hashes == baseline,
        'native_dialog_titles_are_explicit': bool(dialog_titles) and all(dialog_titles),
    }
    summary = dict(method='Source text and routing audit only; .NET/WinForms not executed',
                   cfg_files=len(actual_hashes), block_names=len(block_names), missing_block_labels=missing_blocks,
                   field_keys=len(keys), direct_field_labels=len(direct), translated_words=len(words),
                   fixed_literals_reviewed=len(literal_rows), unhandled_fixed_literals=missing_fixed,
                   retained_identifiers=retained,dynamic_routes=routes,
                   native_dialog_note='Windows 原生文件选择器的系统按钮由操作系统语言决定；应用自有标题和确认按钮已用中文。')
    (docs/'汉化审查.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k not in ('retained_identifiers',)},ensure_ascii=False))
    print('Retained original abbreviations/parameter codes:',len(retained))
    return 1 if missing_blocks or missing_fixed or not all(routes.values()) else 0


if __name__ == '__main__': raise SystemExit(main())
