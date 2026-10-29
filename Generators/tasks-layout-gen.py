import re, sys, os, glob
R = sys.argv[1]   # RAE tasks folder
OUT = sys.argv[2]
src = {os.path.basename(f)[:-3]: open(f, encoding='utf-8-sig').read() for f in glob.glob(os.path.join(R, '*.cs'))}

def bodies(text, header_re):
    """yield (name, params, body_lines) for methods whose header matches."""
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        m = re.match(header_re, lines[i].strip())
        if m:
            depth = 0; body = []; j = i + 1
            while True:
                s = lines[j].strip()
                if s == '{': depth += 1
                if s == '}': depth -= 1
                body.append(lines[j]); j += 1
                if depth == 0: break
            yield m, body[1:-1]
            i = j
        else:
            i += 1

SKIP = {'talk_proc', 'window', 'option', 'SPECIAL_PARAMETERS', 'RANDOM_DIALOG_GROUP_DATA'}
READS = {'Int32': 'I32', 'Int16': 'I16', 'UInt16': 'U16', 'UInt32': 'U32', 'Boolean': 'Bool', 'Byte': 'U8',
         'Single': 'F32', 'Int64': 'I64', 'UInt64': 'U64', 'Double': 'F64', 'Bytes': 'Bytes'}
LV = r'((?:[A-Za-z_]\w*(?:\[\w+\])*\.)*[A-Za-z_]\w*(?:\[\w+\])*)'
sizes = {}      # (base, depth) -> expr
problems = []

# property declarations "= new T[N]" give fixed sizes
for name, text in src.items():
    for m in re.finditer(r'public \w+(\[\])+ (\w+) \{ get; set; \} = (?:new \w+\[|\w+\.New\()(\w+)[\])]', text):
        sizes.setdefault((m.group(2), 0), m.group(3))

def name_of(lv, param):
    if param and lv.startswith(param + '.'): lv = lv[len(param) + 1:]
    idx = re.findall(r'\[(\w+)\]', lv)
    base = re.sub(r'\[\w+\]', '', lv)
    s = re.sub(r'\[(\w+)\]', lambda m: '[%s]' % m.group(1) if m.group(1).isdigit() else '[{%s}]' % m.group(1), lv)
    interp = any(not k.isdigit() for k in idx)
    return ('$"%s"' if interp else '"%s"') % s, base.split('.')[-1], len(idx)

def expr(e, param):
    e = e.replace('GlobalData.version', 'version').replace('GlobalFunctions.', 'TaskVer.')
    def length(m):
        lv = m.group(1)
        _, base, d = name_of(lv, param)
        if (base, d) not in sizes: problems.append('size? ' + lv); return '0'
        return '(' + sizes[(base, d)] + ')'
    e = re.sub(r'((?:\w+\.)?\w+(?:\[\w+\])*)\.Length', length, e)
    def ref(m):
        n, _, _ = name_of(m.group(0), param)
        return 'v.N(%s)' % n
    pre = (re.escape(param) + r'\.') if param else ''
    e = re.sub(r'(?<![\w."])(?:%s)?(?:m_|x_)\w+(?:\[\w+\])*' % pre, ref, e)
    return e

def cond(e, param):
    e = expr(e, param)
    # v.N(...) usado como booleano
    e = re.sub(r'(^|\(|&& |\|\| )!(v\.N\("[^"]*"\))(?=$|\)| &&| \|\|)', r'\1\2 == 0', e)
    e = re.sub(r'(^|\(|&& |\|\| )(v\.N\((?:\$?"[^"]*")\))(?=$|\)| &&| \|\|)', r'\1\2 != 0', e)
    return e

def conv(body, param, fname):
    out = []
    for raw in body:
        ind = raw[:len(raw) - len(raw.lstrip())].replace('\t', '    ')
        s = raw.strip()
        s = re.sub(r'\bver\b', 'version', s)
        def emit(x): out.append(ind + x)
        if s in ('{', '}', 'else', 'return;', '', 'break;'): emit(s); continue
        if 'ChckPrm' in s or 'ApplyCombinedSwitchesParameters' in s: continue
        m = re.match(r'^(if|else if|while) \((.*)\)$', s)
        if m: emit('%s (%s)' % (m.group(1), cond(m.group(2), param))); continue
        m = re.match(r'^for \(int (\w+) = (\w+); \1 < (.+); \1\+\+\)$', s)
        if m: emit('for (int %s = %s; %s < %s; %s++)' % (m.group(1), m.group(2), m.group(1), expr(m.group(3), param), m.group(1))); continue
        m = re.match(r'^%s = br\.Read(\w+)\((.*)\);$' % LV, s)
        if m and m.group(2) in READS:
            n, _, _ = name_of(m.group(1), param)
            a = (', ' + expr(m.group(3), param)) if m.group(3) else ''
            emit('v.%s(%s%s);' % (READS[m.group(2)], n, a)); continue
        m = re.match(r'^%s = GlobalFunctions\.Read(NameChars|Chars)\((\w+), br\);$' % LV, s)
        if m:
            n, _, _ = name_of(m.group(1), param)
            emit('v.%s(%s, %s);' % ('Str16' if m.group(2) == 'NameChars' else 'Str8', n, expr(m.group(3), param))); continue
        m = re.match(r'^SPECIAL_PARAMETERS\.Read\(version, br, (\w+), (\w+), m_ID\);$', s)
        if m: emit('v.In("%s", () => L_SPECIAL_PARAMETERS(v, version, (int)%s));' % (m.group(2), expr(m.group(1), param))); continue
        m = re.match(r'^RANDOM_DIALOG_GROUP_DATA\.Read\(version, br, (.+), (.+), %s\);$' % LV, s)
        if m:
            n, _, _ = name_of(m.group(3), param)
            emit('v.In(%s, () => L_RANDOM_DIALOG_GROUP_DATA(v, version, (int)%s, (int)%s));' % (n, expr(m.group(1), param), expr(m.group(2), param))); continue
        m = re.match(r'^(\w+)\.Read(\d*)\(version, br, %s\);$' % LV, s)
        if m:
            cls = m.group(1)
            n, _, _ = name_of(m.group(3), param)
            emit('v.In(%s, () => L_%s%s(v, version));' % (n, cls, m.group(2))); continue
        m = re.match(r'^int (\w+) = (.+);$', s)
        if m: emit('int %s = (int)(%s);' % (m.group(1), expr(m.group(2), param))); continue
        m = re.match(r'^(Read\w+)\(version, br, (\w+)\);$', s)
        if m: emit('A_%s(v, version);' % m.group(1)); continue
        m = re.match(r'^%s = new \w+\[(.+?)\](\[\])*;$' % LV, s)
        if m:
            _, base, d = name_of(m.group(1), param)
            sizes[(base, d)] = expr(m.group(2), param); continue
        m = re.match(r'^%s = new [\w.]+\(\);$' % LV, s)
        if m: continue
        m = re.match(r'^%s = (.+);$' % LV, s)
        if m and 'br.' not in s:
            n, _, _ = name_of(m.group(1), param)
            emit('v.Set(%s, %s); // derivado (não está no arquivo)' % (n, expr(m.group(2), param))); continue
        problems.append('%s: %s' % (fname, s)); emit('#error ' + s)
    return out

fns = []
fx = src['ATaskTemplFixedData']
for m, body in bodies(fx, r'^private void logic_read_task_data_struct_(\d+)\(int (?:version|ver), BinaryReader br\)$'):
    fns.append(('static void F%s(TaskIO v, int version)' % m.group(1), conv(body, None, 'F' + m.group(1))))
order = sorted(int(m.group(1)) for m, _ in bodies(fx, r'^private void logic_read_task_data_struct_(\d+)\('))
for name, text in sorted(src.items()):
    if name in SKIP: continue
    for m, body in bodies(text, r'^internal static void Read(\d*)\(int version, BinaryReader br, (\w+) (\w+)\)$'):
        fns.append(('static void L_%s%s(TaskIO v, int version)' % (m.group(2), m.group(1)), conv(body, m.group(3), name)))
    for m, body in bodies(text, r'^private static void (Read\w+)\(int version, BinaryReader br, (\w+) (\w+)\)$'):
        fns.append(('static void A_%s(TaskIO v, int version)' % m.group(1), conv(body, m.group(3), name)))

# Correções do formato de referência (provadas nos tasks.data do disco; ver PW-KB §S21)
PATCHES = {
    'F10': [('            if (version >= 63 && version < 135)', [
        '            if (version >= 59 && version < 135) // falta no RAE: contagem das chaves da quest (v93-129 provado)',
        '            {',
        '                v.I32("m_ulChangeKeyCnt");',
        '                if (version < 176)',
        '                {',
        '                    v.I32("m_plChangeKeyPointer");',
        '                    v.I32("m_plChangeKeyValuePointer");',
        '                    v.I32("m_pbChangeTypePointer");',
        '                }',
        '            }'])],
}
for i, (h, b) in enumerate(fns):
    key = re.search(r'void (\w+)\(', h).group(1)
    for anchor, ins in PATCHES.get(key, []):
        j = next(k for k, l in enumerate(b) if l.strip() == anchor.strip())
        pad = b[j][:len(b[j]) - len(b[j].lstrip())]
        b[j:j] = [pad + l[12:] for l in ins]
params = re.findall(r'new TaskParameterInfo\(TaskParameters\.(\w+), (\d+), (\d+)\)', src['GlobalData'])
with open(OUT, 'w', encoding='utf-8') as f:
    f.write('// <auto-generated> Gerado de tasks/ATaskTemplFixedData.cs (formato de referência) por Generators/tasks-layout-gen.py. Não editar à mão:\n')
    f.write('// ajustes vão em TaskLayout.cs.</auto-generated>\n#nullable disable\nnamespace MyAceEditor.PerfectWorld;\n\npublic static partial class TaskLayout\n{\n')
    f.write('    static void Fixed(TaskIO v, int version)\n    {\n')
    for n in order: f.write('        F%02d(v, version);\n' % n if n < 10 else '        F%d(v, version);\n' % n)
    f.write('    }\n\n')
    for h, b in fns:
        f.write('    ' + h + '\n    {\n')
        for l in b: f.write('    ' + l + '\n')
        f.write('    }\n\n')
    f.write('    /// <summary>Chaves de quest que viram bits em CombinedSwitches (v135+): nome, versão mínima, máxima (0 = sem limite).</summary>\n')
    f.write('    public static readonly (string Name, int Min, int Max)[] Switches =\n    {\n')
    for p in params: f.write('        ("%s", %s, %s),\n' % p)
    f.write('    };\n}\n')
print('\n'.join(problems))
print(len(fns), 'functions', file=sys.stderr)
