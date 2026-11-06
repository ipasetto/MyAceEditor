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
    /// <summary>Lugar da lista do minimapa enquanto o jogador está na zona.</summary>
    public sealed record Mark(string Name, float X, float Y, float Z);

    public sealed record Area(string Name, int Priority, int Interval, int Loop, string Sound, string NightSound, List<string> Music, List<(float X, float Z)> Points, int Domain = 0)
    {
        /// <summary>Só o cliente lê. O nome e a música trocam quando o ID muda; ID 0 não mostra nome; o mesmo ID em várias áreas forma uma zona em partes.</summary>
        public int Id { get; init; }
        /// <summary>Instância para onde vai quem morre ou volta à cidade.</summary>
        public int DstInstance { get; init; }
        /// <summary>Instância em que a área vale.</summary>
        public int SrcInstance { get; init; }
        /// <summary>Até o nível 29, sem nome rosa ou vermelho, ninguém ataca (PVP_PROTECT_LEVEL).</summary>
        public bool PkProtected { get; init; }
        /// <summary>Onde renasce quem morre aqui.</summary>
        public (float X, float Y, float Z) City { get; init; }
        /// <summary>Altura de cada canto; o jogo não usa, mas o arquivo guarda.</summary>
        public List<float> PointY { get; init; } = [];
        public List<Mark> Marks { get; init; } = [];

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
    public int Version { get; private set; } = 7;
    /// <summary>Carimbo: tem de ser igual ao do precinct.sev, senão o jogador vê erro de dados ao entrar (CheckOutInst).</summary>
    public uint Stamp { get; set; }

    /// <summary>Instância com mais áreas: a do mapa, para quem não sabe qual é (alguns mapas guardam áreas de outra origem).</summary>
    public int MainInstance => Areas.GroupBy(a => a.SrcInstance).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? 1;

    /// <summary>
    /// A zona em que o jogador está (CELPrecinctSet::IsPointIn): só valem as áreas da instância, o menor número de prioridade
    /// ganha e, no empate, a que vem depois no arquivo. Null fora de todas.
    /// </summary>
    public Area? At(float x, float z, int instance)
    {
        Area? best = null;
        foreach (var a in Areas)
            if (a.SrcInstance == instance && (best is null || a.Priority <= best.Priority) && a.Contains(x, z)) best = a;
        return best;
    }

    public static PrecinctFile? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try { return Parse(File.ReadAllBytes(path)); }
        catch (InvalidDataException ex) { AppLog.Info("precinct.clt: " + ex.Message); return null; }
    }

    /// <summary>Lê o arquivo inteiro. Lança InvalidDataException se não for um precinct.clt legível.</summary>
    public static PrecinctFile Parse(byte[] b)
    {
        var text = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2) : Encoding.UTF8.GetString(b);
        var t = Tokens(text);
        int p = 0;
        string Next() => p < t.Count ? t[p++] : throw new InvalidDataException("precinct.clt curto");
        int I() => int.Parse(Next(), CultureInfo.InvariantCulture);
        float F() => float.Parse(Next(), CultureInfo.InvariantCulture);
        var f = new PrecinctFile();
        try
        {
            if (Next() != "version") throw new InvalidDataException("sem a linha version");
            int v = f.Version = I();
            f.Stamp = uint.Parse(Next(), CultureInfo.InvariantCulture);
            while (p < t.Count)
            {
                var name = Next();
                int id = v >= 2 ? I() : 0;
                int points = I(), marks = I();
                int prio = I(), dst = I();
                int music = I(), interval = I(), loop = I();
                int src = v >= 4 ? I() : 0;
                int domain = v >= 6 ? I() : 0;
                bool pk = v >= 7 && I() != 0;
                var city = (F(), F(), F());
                var poly = new List<(float, float)>(points);
                var heights = new List<float>(points);
                for (int i = 0; i < points; i++) { float x = F(); heights.Add(F()); poly.Add((x, F())); }
                var places = new List<Mark>(marks);
                for (int i = 0; i < marks; i++) places.Add(new Mark(Next(), F(), F(), F()));
                var sound = Next();
                var list = new List<string>(music);
                for (int i = 0; i < music; i++) list.Add(Next());
                var night = v >= 3 ? Next() : sound;
                f.Areas.Add(new Area(name, prio, interval, loop, sound, night, list, poly, domain)
                    { Id = id, DstInstance = dst, SrcInstance = src, PkProtected = pk, City = city, PointY = heights, Marks = places });
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { throw new InvalidDataException("número inválido no precinct.clt", ex); }
        return f;
    }

    const string Header = "//  Element pricinct file (client version)\n\n";

    /// <summary>
    /// Como o %f do editor oficial: 6 casas, com o empate exato para longe do zero (287.8203125 vira 287.820313; o F6 do .NET dá ...312).
    /// O sinal fica mesmo quando arredonda para zero.
    /// </summary>
    internal static string Num(float v)
    {
        if (Math.Abs(v) >= 1e9f || float.IsNaN(v)) return ((double)v).ToString("F6", CultureInfo.InvariantCulture);
        var exact = decimal.Parse(Math.Abs((double)v).ToString("F20", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var text = Math.Round(exact, 6, MidpointRounding.AwayFromZero).ToString("F6", CultureInfo.InvariantCulture);
        return float.IsNegative(v) ? "-" + text : text;
    }

    /// <summary>O arquivo inteiro no formato oficial: a regravação é igual byte a byte nos 71 mapas do 155. Só versão 7.</summary>
    public byte[] Render()
    {
        if (Version != 7) throw new InvalidOperationException($"precinct.clt versão {Version}");
        static string Text(string s) => s.IndexOfAny(['"', '\n', '\r']) < 0 ? "\"" + s + "\"" : throw new ArgumentException("texto com aspas ou quebra de linha: " + s);
        var o = new StringBuilder(Header).Append("version  7\n").Append(Stamp.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        foreach (var a in Areas)
        {
            o.Append(Text(a.Name)).Append('\n');
            o.Append(FormattableString.Invariant($"{a.Id}  {a.Points.Count}  {a.Marks.Count}  {a.Priority}  {a.DstInstance}  {a.Music.Count}  {a.Interval}  {a.Loop}  {a.SrcInstance}  {a.Domain}  {(a.PkProtected ? 1 : 0)}\n"));
            o.Append($"{Num(a.City.X)}, {Num(a.City.Y)}, {Num(a.City.Z)}\n");
            for (int i = 0; i < a.Points.Count; i++) o.Append($"{Num(a.Points[i].X)}  {Num(i < a.PointY.Count ? a.PointY[i] : 0)}  {Num(a.Points[i].Z)}\n");
            o.Append('\n');
            foreach (var m in a.Marks) o.Append($"{Text(m.Name)}  {Num(m.X)}  {Num(m.Y)}  {Num(m.Z)}\n");
            o.Append('\n');
            o.Append(Text(a.Sound)).Append('\n');
            foreach (var s in a.Music) o.Append(Text(s)).Append('\n');
            o.Append(Text(a.NightSound)).Append("\n\n");
        }
        return [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(o.ToString())];
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
