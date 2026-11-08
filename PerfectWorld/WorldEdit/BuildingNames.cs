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

    /// <summary>Nome que aparece: o que o usuário deu à forma, senão o do arquivo (o palpite automático saiu: usuário 08/11).</summary>
    public static string Display(int sig, string fallback) => User(sig) ?? fallback;

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
