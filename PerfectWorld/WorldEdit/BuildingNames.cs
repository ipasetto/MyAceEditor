using System.Numerics;
using System.Text.Json;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>
/// Nome das construções (o .bmd do jogo não tem descrição: só litmodel_N e texturas com nome em código). Nome automático
/// = palpite pela forma + tamanho + mapa onde mais aparece; o usuário pode trocar. Os nomes do usuário ficam num arquivo
/// ÚNICO do programa (não por cliente), ligados à forma (assinatura da malha, a mesma do catálogo): outro cliente com a
/// mesma construção mostra o mesmo nome (pedido do usuário 08/11). Exportar/importar para trocar listas.
/// </summary>
public static class BuildingNames
{
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "building-names.json");
    static Dictionary<int, string>? _names;

    static Dictionary<int, string> Names
    {
        get
        {
            if (_names is not null) return _names;
            _names = new();
            try
            {
                if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(FilePath)) is { } d) _names = d;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("nomes das construções: ler", ex); }
            return _names;
        }
    }

    /// <summary>Nome dado pelo usuário (null = usa o automático).</summary>
    public static string? User(int sig) => Names.GetValueOrDefault(sig);

    /// <summary>Grava o nome do usuário (vazio = volta ao automático). null = deu certo; senão o motivo.</summary>
    public static string? Set(int sig, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) Names.Remove(sig); else Names[sig] = name.Length > 80 ? name[..80] : name;
        return Save();
    }

    static string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(Names, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("nomes das construções: gravar", ex);
            return Loc.T("names.saveFail", "Não deu para guardar o nome (pasta de configurações sem acesso?).");
        }
    }

    /// <summary>Palpite pela forma (largura, altura, profundidade em metros), no idioma do programa.</summary>
    public static string Guess(Vector3 size)
    {
        float h = size.Y, w = MathF.Max(size.X, size.Z), d = MathF.Min(size.X, size.Z);
        if (MathF.Max(w, h) < 3) return Loc.T("names.small", "Objeto pequeno");
        if (h > 2.5f * w) return h > 12 ? Loc.T("names.tower", "Torre") : Loc.T("names.pole", "Poste ou coluna");
        if (w > 4 * MathF.Max(d, 0.1f) && h < 0.6f * w) return h < 4 ? Loc.T("names.fence", "Cerca ou muro baixo") : Loc.T("names.wall", "Muro");
        if (h < 0.25f * w) return Loc.T("names.floor", "Piso ou plataforma");
        if (w > 30 || h > 25) return Loc.T("names.big", "Construção grande");
        return Loc.T("names.building", "Construção");
    }

    /// <summary>Nome automático: palpite + tamanho + mapa (ex.: "Muro 12×3 m · Cidade do Dragão").</summary>
    public static string Auto(Vector3 size, string? mapLabel)
        => $"{Guess(size)} {MathF.Max(size.X, size.Z):0.#}×{size.Y:0.#} m" + (string.IsNullOrEmpty(mapLabel) ? "" : " · " + mapLabel);

    /// <summary>Nome que aparece: o do usuário ou o automático.</summary>
    public static string Display(int sig, Vector3 size, string? mapLabel) => User(sig) ?? Auto(size, mapLabel);

    /// <summary>Exporta os nomes do usuário (JSON). null = deu certo.</summary>
    public static string? Export(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(Names, new JsonSerializerOptions { WriteIndented = true })); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("nomes: exportar", ex); return ex.Message; }
    }

    /// <summary>Importa uma lista (junta; o nome importado vale por cima). Devolve quantos entraram, ou o motivo do erro.</summary>
    public static (int Count, string? Why) Import(string path)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(path)) ?? new();
            int n = 0;
            foreach (var (k, v) in d) if (!string.IsNullOrWhiteSpace(v)) { Names[k] = v.Trim(); n++; }
            return (n, Save());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("nomes: importar", ex); return (0, Loc.T("names.importFail", "Esse arquivo não é uma lista de nomes do MyAceEditor.")); }
    }
}
