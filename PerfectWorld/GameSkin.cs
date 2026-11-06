using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Qual janela o cliente aberto usa de verdade. userdata\systemsettings.ini diz o tema (Theme, 0 = clássica, padrão do jogo) e o
/// .dcf do tema lista as janelas: ingame.dcf aponta para a raiz de interfaces\, ingame-v1.dcf para version01\
/// (CECUIManager::GetUIDcf). Tudo que diz "como no jogo" passa por aqui, nunca por um caminho fixo.
/// </summary>
public static class GameSkin
{
    public static int Theme(string elementDir)
    {
        var f = Path.Combine(elementDir, "userdata", "systemsettings.ini");
        try
        {
            if (File.Exists(f))
                foreach (var line in File.ReadLines(f))
                    if (line.Split('=') is { Length: 2 } kv && kv[0].Trim().Equals("Theme", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1].Trim(), out var t)) return t;
        }
        catch (IOException ex) { AppLog.Info("systemsettings.ini: " + ex.Message); }
        return 0;
    }

    /// <summary>Caminho da janela no interfaces.pck pelo tema do cliente; o outro tema e os caminhos conhecidos ficam de reserva.</summary>
    public static string? PathOf(PckArchive interfaces, string elementDir, string file)
    {
        int theme = Theme(elementDir);
        foreach (var dcf in theme == 0 ? new[] { "ingame.dcf", "ingame-v1.dcf" } : ["ingame-v1.dcf", "ingame.dcf"])
            if (Listed(interfaces, dcf, file) is { } hit && interfaces.Find(hit) is not null) return hit;
        foreach (var p in new[] { @"interfaces\" + file, @"interfaces\version01\" + file })
            if (interfaces.Find(p) is not null) return p;
        return null;
    }

    static string? Listed(PckArchive interfaces, string dcf, string file)
    {
        if (interfaces.Find(@"interfaces\" + dcf) is not { } e) return null;
        var b = interfaces.Read(e);
        var text = b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1) : Encoding.UTF8.GetString(b);
        foreach (var line in text.Split('\n'))
        {
            var name = line.Trim().Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (name is not null && Path.GetFileName(name).Equals(file, StringComparison.OrdinalIgnoreCase)) return @"interfaces\" + name;
        }
        return null;
    }

    public static GameDialog? Load(PckArchive interfaces, string elementDir, string file) =>
        PathOf(interfaces, elementDir, file) is { } p ? GameDialog.Load(interfaces, p) : null;
}
