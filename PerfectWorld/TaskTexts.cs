using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Um texto do registro de quest. Fixo = N caracteres no lugar (nome 30, título de diálogo e opção 64: depois do \0 pode
/// haver lixo do editor de origem, preservado); Contado = i32 nº de caracteres + texto (os 4 textos da quest e o da
/// janela de diálogo; o \0 do fim entra no nº — às vezes "\0\0" — e é mantido).
/// </summary>
public sealed class TaskText
{
    public string Text = "";
    internal string Orig = "";
    internal int At, Chars;          // posição e nº de caracteres no arquivo lido
    internal bool Counted;
    internal int Tail;               // contado: caracteres depois do texto (o \0 e o que vier depois dele)
    public bool Changed => Text != Orig;
}

/// <summary>Textos de uma quest (e das subquests) achados no registro: nome, 4 textos e 5 diálogos.</summary>
public sealed class TaskTextNode
{
    public int Id, Start, TextStart, End;
    public TaskText Name = new();
    public TaskText[] Texts = new TaskText[4];      // descrição, aceitar, recusar, prêmio
    public TextTalk[] Talks = new TextTalk[5];      // entregar, sem requisito, entregar item, executar, prêmio
    public List<TaskTextNode> Children = new();
    internal bool Changed => Name.Changed || Texts.Any(t => t.Changed) || Talks.Any(t => t.Title.Changed || t.Windows.Any(w => w.Text.Changed || w.Options.Any(o => o.Text.Changed)));
}
public sealed class TextTalk { public int Id; public TaskText Title = new(); public List<TextWindow> Windows = new(); }
public sealed class TextWindow { public int Id, Parent; public TaskText Text = new(); public List<TextOption> Options = new(); }
public sealed class TextOption { public int Param, Id; public TaskText Text = new(); }

/// <summary>
/// Textos do tasks.data em QUALQUER versão (PW-KB §S21). A quest é: [ID (+2 B antes na v153+) | nome 30 car. | bloco fixo
/// da versão] + 4 textos (i32 nº de caracteres + UTF-16) + 5 diálogos (i32 id, título 64 car., i32 nº de janelas;
/// janela: i32 id, i32 pai, i32 nº de car., texto, i32 nº de opções; opção: i32 param, texto 64 car., i32 id)
/// + i32 nº de subquests + subquests. Tudo XOR (ushort)ID a partir da v52. Só o bloco fixo muda com a versão; aqui ele
/// não é lido: o bloco de textos é procurado depois do nome e só vale se a quest inteira (subquests incluídas) fechar
/// exatamente no offset da próxima. Gravar reescreve só os textos alterados; o resto sai byte a byte igual.
/// </summary>
public static class TaskTexts
{
    public sealed record Result(int Version, int TableStart, int[] Offsets, List<TaskTextNode> Roots);

    public static IEnumerable<TaskTextNode> All(IEnumerable<TaskTextNode> n) => n.SelectMany(x => new[] { x }.Concat(All(x.Children)));

    public static Result Read(byte[] d, bool skipMissing = false, int max = int.MaxValue)
    {
        if (d.Length < 12 || BitConverter.ToUInt32(d, 0) is not (TasksFile.Magic or 0))
            throw new InvalidDataException(FieldKinds.Translate("tasks.badSignature", "Não é um tasks.data (assinatura inválida)."));
        int ver = BitConverter.ToInt32(d, 4), n = BitConverter.ToInt32(d, 8);
        int table = ver >= 223 ? 16 : 12;
        if (n < 0 || table + (long)n * 4 > d.Length) throw new InvalidDataException($"Contagem de quests inválida ({n}).");
        var offs = new int[n];
        for (int i = 0; i < n; i++) offs[i] = BitConverter.ToInt32(d, table + i * 4);
        var sorted = offs.Order().ToArray();
        var reader = new Reader(d, ver);
        var roots = new List<TaskTextNode>(n);
        for (int i = 0; i < Math.Min(n, max); i++)
        {
            int k = Array.BinarySearch(sorted, offs[i]);
            int end = k + 1 < sorted.Length ? sorted[k + 1] : d.Length;
            var q = reader.Quest(offs[i], end);
            if (q == null && skipMissing) continue;
            roots.Add(q
                ?? throw new InvalidDataException(string.Format(FieldKinds.Translate("tasks.textsNotFound", "Não achei os textos da quest no offset {0:N0} (versão {1})."), offs[i], ver)));
        }
        return new(ver, table, offs, roots);
    }

    /// <summary>Arquivo com os textos alterados; a tabela de offsets é refeita. Sem alterações sai idêntico.</summary>
    public static byte[] Build(byte[] d, Result r)
    {
        using var ms = new MemoryStream(d.Length + 4096);
        int head = r.TableStart + r.Offsets.Length * 4;
        ms.Write(d, 0, head);
        var newOffs = new int[r.Offsets.Length];
        int last = head;
        foreach (var i in Enumerable.Range(0, r.Offsets.Length).OrderBy(i => r.Offsets[i]))
        {
            var q = r.Roots[i];
            if (q.Start > last) ms.Write(d, last, q.Start - last);
            newOffs[i] = (int)ms.Position;
            WriteNode(ms, d, r.Version, q);
            last = q.End;
        }
        if (last < d.Length) ms.Write(d, last, d.Length - last);
        var o = ms.ToArray();
        for (int i = 0; i < newOffs.Length; i++) BitConverter.GetBytes(newOffs[i]).CopyTo(o, r.TableStart + i * 4);
        return o;
    }

    static bool AnyChanged(TaskTextNode q) => q.Changed || q.Children.Any(AnyChanged);

    static void WriteNode(Stream s, byte[] d, int ver, TaskTextNode q)
    {
        if (!AnyChanged(q)) { s.Write(d, q.Start, q.End - q.Start); return; }
        ushort key = Key(ver, q.Id);
        var fixedPart = d.AsSpan(q.Start, q.TextStart - q.Start).ToArray();
        if (q.Name.Changed) Encode(q.Name, key).CopyTo(fixedPart, q.Name.At - q.Start);
        s.Write(fixedPart);
        var w = new BinaryWriter(s, Encoding.Unicode, leaveOpen: true);
        foreach (var t in q.Texts) Put(w, d, t, key);
        foreach (var tk in q.Talks)
        {
            w.Write(tk.Id); Put(w, d, tk.Title, key); w.Write(tk.Windows.Count);
            foreach (var win in tk.Windows)
            {
                w.Write(win.Id); w.Write(win.Parent); Put(w, d, win.Text, key); w.Write(win.Options.Count);
                foreach (var op in win.Options) { w.Write(op.Param); Put(w, d, op.Text, key); w.Write(op.Id); }
            }
        }
        w.Write(q.Children.Count);
        w.Flush();
        foreach (var c in q.Children) WriteNode(s, d, ver, c);
    }

    /// <summary>Texto como no arquivo: sem mudança, os bytes originais; com mudança, codificado de novo.</summary>
    static void Put(BinaryWriter w, byte[] d, TaskText t, ushort key)
    {
        if (!t.Changed) { w.Write(d, t.Counted ? t.At - 4 : t.At, t.Chars * 2 + (t.Counted ? 4 : 0)); return; }
        var b = Encode(t, key);
        if (t.Counted) w.Write(b.Length / 2);
        w.Write(b);
    }

    static byte[] Encode(TaskText t, ushort key)
    {
        string text = t.Text;
        int len;
        if (t.Counted) len = text.Length + t.Tail;
        else { if (text.Length > t.Chars - 1) text = text[..(t.Chars - 1)]; len = t.Chars; }
        var b = new byte[len * 2];
        for (int i = 0; i < len; i++) BitConverter.GetBytes((ushort)((i < text.Length ? text[i] : '\0') ^ key)).CopyTo(b, i * 2);
        return b;
    }

    static ushort Key(int ver, int id) => ver >= 52 ? unchecked((ushort)id) : (ushort)0;

    /// <summary>
    /// Quantos textos mudam ao trazer os de <paramref name="src"/> (e aplica, se não for simulação). Casa a quest pelo ID
    /// (subquests incluídas, em qualquer lugar da árvore), a janela de diálogo pelo ID e a opção pela posição; texto vazio
    /// no outro não apaga o daqui. O que só existe num dos lados fica como está.
    /// </summary>
    public static int Translate(Result dst, Result src, bool dryRun)
    {
        var map = All(src.Roots).GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());
        int n = 0;
        void Take(TaskText cur, TaskText other) { if (other.Text.Length == 0 || other.Text == cur.Text) return; n++; if (!dryRun) cur.Text = other.Text; }
        foreach (var q in All(dst.Roots))
        {
            if (!map.TryGetValue(q.Id, out var s)) continue;
            Take(q.Name, s.Name);
            for (int i = 0; i < 4; i++) Take(q.Texts[i], s.Texts[i]);
            for (int t = 0; t < 5; t++)
            {
                Take(q.Talks[t].Title, s.Talks[t].Title);
                var wins = s.Talks[t].Windows.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
                foreach (var w in q.Talks[t].Windows)
                {
                    if (!wins.TryGetValue(w.Id, out var sw)) continue;
                    Take(w.Text, sw.Text);
                    for (int o = 0; o < w.Options.Count && o < sw.Options.Count; o++) Take(w.Options[o].Text, sw.Options[o].Text);
                }
            }
        }
        return n;
    }

    // ---------- leitura ----------
    sealed class Reader
    {
        readonly byte[] _d; readonly int _ver; int _p;
        bool _strict;
        public Reader(byte[] d, int ver) { _d = d; _ver = ver; }

        /// <summary>
        /// Quest que começa em <paramref name="start"/> e termina exatamente em <paramref name="end"/>. Primeiro sem aceitar
        /// o caractere igual à chave (zero cru: é o que distingue lixo do bloco fixo de texto); se não fechar, aceitando.
        /// </summary>
        public TaskTextNode? Quest(int start, int end)
        {
            _strict = true;
            if (Candidates(start, end).FirstOrDefault(x => x.End == end) is { } q) return q;
            _strict = false;
            return Candidates(start, end).FirstOrDefault(x => x.End == end);
        }

        /// <summary>Leituras possíveis da quest em <paramref name="start"/> (cada lugar onde o bloco de textos fecha), em ordem.</summary>
        IEnumerable<TaskTextNode> Candidates(int start, int limit)
        {
            int idAt = start + (_ver >= 153 ? 2 : 0);
            if (idAt + 64 > limit) yield break;
            int id = BitConverter.ToInt32(_d, idAt);
            if (id <= 0) yield break;
            ushort key = Key(_ver, id);
            if (Fixed(idAt + 4, 30, key) is not { } name) yield break;
            const int minBlock = 4 * 4 + 5 * 136 + 4;
            for (int p = idAt + 64; p + minBlock <= limit; p++)
            {
                _p = p;
                var q = Block(key, limit, out int subs);
                if (q is null) continue;
                q.Id = id; q.Start = start; q.TextStart = p; q.Name = name;
                foreach (var done in Children(q, subs, 0, _p, limit)) yield return done;
            }
        }

        IEnumerable<TaskTextNode> Children(TaskTextNode q, int subs, int i, int at, int limit)
        {
            if (i == subs) { q.End = at; yield return q; yield break; }
            foreach (var c in Candidates(at, limit))
            {
                q.Children.Add(c);
                foreach (var done in Children(q, subs, i + 1, c.End, limit)) yield return done;
                q.Children.RemoveAt(q.Children.Count - 1);
            }
        }

        TaskTextNode? Block(ushort key, int limit, out int subs)
        {
            subs = 0;
            var q = new TaskTextNode();
            for (int i = 0; i < 4; i++) if (Counted(key, limit, 16384, free: true) is { } t) q.Texts[i] = t; else return null;
            for (int t = 0; t < 5; t++)
            {
                var tk = new TextTalk();
                if (!I32(limit, out tk.Id) || _p + 128 > limit || Fixed(_p, 64, key) is not { } title) return null;
                tk.Title = title; _p += 128;
                if (!I32(limit, out int nw) || nw < 0 || nw > 256) return null;
                for (int w = 0; w < nw; w++)
                {
                    var win = new TextWindow();
                    if (!I32(limit, out win.Id) || !I32(limit, out win.Parent) || Counted(key, limit, 32768, free: false) is not { } text) return null;
                    win.Text = text;
                    if (!I32(limit, out int no) || no < 0 || no > 64) return null;
                    for (int o = 0; o < no; o++)
                    {
                        var op = new TextOption();
                        if (!I32(limit, out op.Param) || _p + 128 > limit || Fixed(_p, 64, key) is not { } ot) return null;
                        op.Text = ot; _p += 128;
                        if (!I32(limit, out op.Id)) return null;
                        win.Options.Add(op);
                    }
                    tk.Windows.Add(win);
                }
                q.Talks[t] = tk;
            }
            return I32(limit, out subs) && subs >= 0 && subs <= 1000 ? q : null;
        }

        bool I32(int limit, out int v)
        {
            if (_p + 4 > limit) { v = 0; return false; }
            v = BitConverter.ToInt32(_d, _p); _p += 4; return true;
        }

        static bool Ok(char c) => c >= ' ' || c is '\r' or '\n' or '\t';

        /// <summary>
        /// i32 nº + texto. Os 4 textos da quest (free) só têm \0 no fim (às vezes mais de um, ex. "\0\0" no PWI); o da janela vai até o \0.
        /// Todo caractere antes do fim tem de ser legível.
        /// </summary>
        TaskText? Counted(ushort key, int limit, int max, bool free)
        {
            if (!I32(limit, out int n) || n < 0 || n > max || _p + n * 2 > limit) return null;
            var sb = new StringBuilder(n); bool ended = false;
            for (int i = 0; i < n; i++)
            {
                char c = (char)(BitConverter.ToUInt16(_d, _p + i * 2) ^ key);
                if (ended) { if (free && c != '\0') return null; continue; }   // depois do \0: só \0 (textos da quest) ou o que for (janela)
                if (c == '\0') { ended = true; continue; }
                if (!Ok(c) || (_strict && key != 0 && c == key)) return null;   // c == chave: zero cru do arquivo, não é texto
                sb.Append(c);
            }
            var t = new TaskText { Text = sb.ToString(), Orig = sb.ToString(), At = _p, Chars = n, Counted = true, Tail = n - sb.Length };
            _p += n * 2;
            return t;
        }

        /// <summary>
        /// N caracteres no lugar: legível até o \0, que é obrigatório (o que vem depois é ignorado e preservado). Sem o \0
        /// não é texto: zeros do bloco fixo, cifrados, viram o caractere da chave repetido e pareceriam um título.
        /// </summary>
        TaskText? Fixed(int at, int n, ushort key)
        {
            for (int i = 0; i < n; i++)
            {
                char c = (char)(BitConverter.ToUInt16(_d, at + i * 2) ^ key);
                if (c == '\0')
                {
                    var s = Encoding.Unicode.GetString(_d, at, i * 2);
                    var chars = s.ToCharArray();
                    for (int k = 0; k < chars.Length; k++) chars[k] = (char)(chars[k] ^ key);
                    var text = new string(chars);
                    return new TaskText { Text = text, Orig = text, At = at, Chars = n };
                }
                if (!Ok(c) || (_strict && key != 0 && c == key)) return null;   // c == chave: zero cru do arquivo, não é texto
            }
            return null;
        }
    }
}
