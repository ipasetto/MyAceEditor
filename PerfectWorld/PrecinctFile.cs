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
    public sealed record Area(string Name, int Priority, int Interval, int Loop, string Sound, string NightSound, List<string> Music, List<(float X, float Z)> Points)
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
                if (v >= 6) I();
                if (v >= 7) I();
                F(); F(); F();
                var poly = new List<(float, float)>(points);
                for (int i = 0; i < points; i++) { float x = F(); F(); poly.Add((x, F())); }
                for (int i = 0; i < marks; i++) { Next(); F(); F(); F(); }
                var sound = Next();
                var list = new List<string>(music);
                for (int i = 0; i < music; i++) list.Add(Next());
                var night = v >= 3 ? Next() : sound;
                f.Areas.Add(new Area(name, prio, interval, loop, sound, night, list, poly));
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException) { AppLog.Info("precinct.clt: " + ex.Message); }
        return f;
    }

    static List<string> Tokens(string s)
    {
        var o = new List<string>();
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
                o.Add(s[(i + 1)..e]); i = e + 1;
                continue;
            }
            int st = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',') i++;
            o.Add(s[st..i]);
        }
        return o;
    }
}
