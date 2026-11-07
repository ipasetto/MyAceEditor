using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>Block deixa o Salvar zona cinza, com o motivo. Ask pergunta antes de salvar.</summary>
public enum ZoneProblemKind { Block, Ask }

/// <param name="Corner">Canto que a tela marca em vermelho; -1 quando não é de canto.</param>
public sealed record ZoneProblem(ZoneProblemKind Kind, string Key, string Text, int Corner = -1);

/// <param name="ShapeTouched">A pessoa mexeu na forma: só assim a borda cruzada barra (94 zonas originais têm isso).</param>
/// <param name="MusicExists">O arquivo de música existe no cliente.</param>
/// <param name="DomainExists">O território existe no domain.data.</param>
/// <param name="HasGround">Há terreno nesse x, z.</param>
public sealed record ZoneContext(bool IsNew, bool ShapeTouched, Func<string, bool> MusicExists, Func<int, bool> DomainExists, Func<float, float, bool> HasGround);

/// <summary>Regras da zona que a pessoa mexeu (docs/REGRAS-DO-JOGO.md, seção Zonas). Zona não mexida não passa por aqui.</summary>
public static class ZoneRules
{
    public const int MaxName = 48;

    public static List<ZoneProblem> Check(PrecinctFile file, PrecinctFile.Area area, ZoneContext ctx)
    {
        var list = new List<ZoneProblem>();
        void Block(string key, string text, int corner = -1) => list.Add(new(ZoneProblemKind.Block, key, Loc.T(key, text), corner));
        // ID 0 nunca mostra nome: área antiga sem nome continua valendo
        if (area.Name.Trim().Length == 0 && (ctx.IsNew || area.Id != 0)) Block("zone.noName", "Dê um nome para a zona.");
        if (area.Points.Count < 3) Block("zone.fewCorners", "Uma zona precisa de pelo menos 3 cantos.");
        else if (ctx.ShapeTouched && Crosses(area.Points, out int corner)) Block("zone.crossed", "A borda cruza a si mesma: arraste o canto marcado.", corner);
        bool noCity = area.City is { X: 0, Y: 0, Z: 0 };
        // cidade noutra instância fica noutro mapa: o terreno deste não diz nada sobre ela
        if (ctx.IsNew && noCity || !noCity && area.DstInstance == area.SrcInstance && !ctx.HasGround(area.City.X, area.City.Z)) Block("zone.noCity", "Escolha onde o jogador renasce.");
        if (area.Domain > 0 && !ctx.DomainExists(area.Domain)) Block("zone.noDomain", "Este território foi excluído: escolha outro.");
        foreach (var m in area.Music.Where(m => !ctx.MusicExists(m)))
            list.Add(new(ZoneProblemKind.Ask, "zone.noMusic", string.Format(Loc.T("zone.noMusic", "O arquivo {0} não existe no cliente. Sem ele a zona fica muda."), m)));
        return list;
    }

    /// <summary>A borda cruza a si mesma; corner = canto que começa o trecho cruzado.</summary>
    public static bool Crosses(IReadOnlyList<(float X, float Z)> pts, out int corner)
    {
        static double Side((float X, float Z) a, (float X, float Z) b, (float X, float Z) c) => ((double)b.X - a.X) * ((double)c.Z - a.Z) - ((double)b.Z - a.Z) * ((double)c.X - a.X);
        int n = pts.Count;
        for (int i = 0; i < n; i++)
            for (int k = i + 2; k < n; k++)
            {
                if (i == 0 && k == n - 1) continue;   // vizinhos pelo fechamento
                var (a, b, c, d) = (pts[i], pts[(i + 1) % n], pts[k], pts[(k + 1) % n]);
                if (Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0) { corner = k; return true; }
            }
        corner = -1;
        return false;
    }

    /// <summary>Maior ID do mapa mais 1. Só precisa ser único dentro do mapa: o cliente só procura no mapa atual.</summary>
    public static int FreeId(PrecinctFile file) => file.Areas.Count == 0 ? 1 : Math.Max(1, file.Areas.Max(a => a.Id) + 1);

    /// <summary>Parte do que tinha zona e ficou sem, numa grade de 200 × 200 sobre as áreas da instância.</summary>
    public static float Holes(PrecinctFile before, PrecinctFile after, int instance)
    {
        var corners = before.Areas.Where(a => a.SrcInstance == instance).SelectMany(a => a.Points).ToList();
        if (corners.Count == 0) return 0;
        float x0 = corners.Min(p => p.X), x1 = corners.Max(p => p.X), z0 = corners.Min(p => p.Z), z1 = corners.Max(p => p.Z);
        const int Grid = 200;
        int had = 0, lost = 0;
        for (int i = 0; i < Grid; i++)
            for (int k = 0; k < Grid; k++)
            {
                float x = x0 + (x1 - x0) * (i + 0.5f) / Grid, z = z0 + (z1 - z0) * (k + 0.5f) / Grid;
                if (before.At(x, z, instance) is null) continue;
                had++;
                if (after.At(x, z, instance) is null) lost++;
            }
        return had == 0 ? 0 : (float)lost / had;
    }
}
