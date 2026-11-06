using System.Globalization;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// maps\&lt;mapa&gt;\precinct.clt (CCommon/EL_Precinct.cpp, CELPrecinct::Load): áreas do mapa com música e som de fundo.
/// Texto UTF-16: "version N", carimbo e, por área: nome; id, pontos, marcas, prioridade, instância, músicas, intervalo, laço,
/// [origem v4] [domínio v6] [pk v7]; cidade x y z; pontos; marcas; som; músicas; [som de noite v3].
/// </summary>
public sealed class PrecinctFile
{
    public sealed record Area(string Name, int Priority, int Interval, int Loop, string Sound, string NightSound, List<string> Music, List<(float X, float Z)> Points, int Domain = 0)
    {
        /// <summary>Ponto dentro do polígono da área (CELPrecinct::IsPointIn), em x e z do jogo.</summary>
        public bool Contains(float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
            {
                var (xi, zi) = Points[i]; var (xj, zj) = Points[j];
                if (zi > z != zj > z && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            return inside;
        }
    }

    public List<Area> Areas { get; } = [];

    /// <summary>Área de maior prioridade no ponto; null fora de todas.</summary>
    public Area? At(float x, float z)
    {
        Area? best = null;
        foreach (var a in Areas)
            if ((best is null || a.Priority > best.Priority) && a.Contains(x, z)) best = a;
        return best;
    }

    public static PrecinctFile? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var b = File.ReadAllBytes(path);
        var text = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2) : Encoding.UTF8.GetString(b);
        var t = Tokens(text);
        int p = 0;
        string Next() => p < t.Count ? t[p++] : throw new InvalidDataException("precinct.clt curto");
        int I() => int.Parse(Next(), CultureInfo.InvariantCulture);
        float F() => float.Parse(Next(), CultureInfo.InvariantCulture);
        var f = new PrecinctFile();
        try
        {
            if (Next() != "version") return null;
            int v = I();
            Next();   // carimbo
            while (p < t.Count)
            {
                var name = Next();
                int points, marks;
                if (v >= 2) { I(); }
                points = I(); marks = I();
                int prio = I(); I();
                int music = I(), interval = I(), loop = I();
                if (v >= 4) I();
                int domain = v >= 6 ? I() : 0;
                if (v >= 7) I();
                F(); F(); F();
                var poly = new List<(float, float)>(points);
                for (int i = 0; i < points; i++) { float x = F(); F(); poly.Add((x, F())); }
                for (int i = 0; i < marks; i++) { Next(); F(); F(); F(); }
                var sound = Next();
                var list = new List<string>(music);
                for (int i = 0; i < music; i++) list.Add(Next());
                var night = v >= 3 ? Next() : sound;
                f.Areas.Add(new Area(name, prio, interval, loop, sound, night, list, poly, domain));
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException) { AppLog.Info("precinct.clt: " + ex.Message); }
        return f;
    }

    /// <summary>
    /// Nomes das regiões (o que o minimapa mostra) e das marcas pelo ID da área, vindos do precinct.clt de outro cliente.
    /// O resto do arquivo fica como está. Chinês do outro não troca texto que já não é chinês.
    /// </summary>
    public static byte[]? Translate(byte[] mine, byte[] other, out int count)
    {
        count = 0;
        static string? Txt(byte[] b) => b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1) : null;
        if (Txt(mine) is not { } a || Txt(other) is not { } o) return null;
        var theirs = Names(o).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.ToList());   // o ID repete em alguns mapas: casa pela ordem dentro do ID
        var edits = new List<(int Start, int Len, string Text)>();
        static bool Cjk(string s) => s.Any(c => c is >= '⺀' and <= '鿿');
        void Put((string Text, int Start, int Len) mineTok, string to)
        {
            if (to.Length == 0 || to == mineTok.Text || Cjk(to) && !Cjk(mineTok.Text)) return;
            edits.Add((mineTok.Start, mineTok.Len, "\"" + to + "\""));
        }
        foreach (var same in Names(a).GroupBy(n => n.Id))
        {
            if (!theirs.TryGetValue(same.Key, out var list) || list.Count != same.Count()) continue;
            foreach (var (n, t) in same.Zip(list))
            {
                Put(n.Name, t.Name.Text);
                if (n.Marks.Count == t.Marks.Count) for (int i = 0; i < n.Marks.Count; i++) Put(n.Marks[i], t.Marks[i].Text);
            }
        }
        if (edits.Count == 0) return null;
        var sb = new StringBuilder(a);
        foreach (var (start, len, text) in edits.OrderByDescending(e => e.Start)) { sb.Remove(start, len); sb.Insert(start, text); }
        count = edits.Count;
        return [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(sb.ToString())];
    }

    sealed record NameRow(int Id, (string Text, int Start, int Len) Name, List<(string Text, int Start, int Len)> Marks);

    static List<NameRow> Names(string text)
    {
        var o = new List<NameRow>();
        var t = Spans(text);
        int p = 0;
        var next = () => p < t.Count ? t[p++] : throw new InvalidDataException("precinct.clt curto");
        int I() => int.Parse(next().Text, CultureInfo.InvariantCulture);
        try
        {
            if (next().Text != "version") return o;
            int v = I();
            if (v < 2) return o;   // sem ID não dá para casar
            next();
            while (p < t.Count)
            {
                var name = next();
                int id = I(), points = I(), marks = I();
                I(); I();
                int music = I();
                I(); I();
                if (v >= 4) I();
                if (v >= 6) I();
                if (v >= 7) I();
                p += 3 + points * 3;
                var list = new List<(string, int, int)>();
                for (int i = 0; i < marks; i++) { list.Add(next()); p += 3; }
                p += 1 + music + (v >= 3 ? 1 : 0);
                o.Add(new NameRow(id, name, list));
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException) { AppLog.Info("precinct.clt: " + ex.Message); }
        return o;
    }

    static List<string> Tokens(string s) => Spans(s).Select(x => x.Text).ToList();

    static List<(string Text, int Start, int Len)> Spans(string s)
    {
        var o = new List<(string, int, int)>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (char.IsWhiteSpace(c) || c == ',' || c == '﻿') { i++; continue; }
            if (c == '"')
            {
                int e = s.IndexOf('"', i + 1);
                if (e < 0) break;
                o.Add((s[(i + 1)..e], i, e + 1 - i)); i = e + 1;
                continue;
            }
            int st = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',') i++;
            o.Add((s[st..i], st, i - st));
        }
        return o;
    }
}
