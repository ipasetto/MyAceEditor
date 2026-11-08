using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Favoritos de uma janela de escolher (JAV), separados por TIPO (item, criatura, árvore, construção...) e por CLIENTE
/// (o mesmo usuário pode editar vários clientes na mesma máquina; pedido do usuário 08/11). Os itens usam os favoritos do
/// editor de elements (mesma lista nos dois lugares); os outros tipos ficam num arquivo de texto por tipo e cliente em
/// %AppData%\MyAceEditor\jav-favorites-&lt;tipo&gt;-&lt;cliente&gt;.txt (um número por linha).
/// </summary>
public sealed class JavFavorites
{
    readonly Func<IReadOnlyList<int>> _ids;
    readonly Func<int, bool> _has;
    readonly Func<int, string?> _toggle;

    /// <summary>Lista ligada a outro dono (ex.: favoritos do elements). toggle devolve null ou o motivo de não ter dado.</summary>
    public JavFavorites(Func<IReadOnlyList<int>> ids, Func<int, bool> has, Func<int, string?> toggle) { _ids = ids; _has = has; _toggle = toggle; }

    public IReadOnlyList<int> Ids => _ids();
    public bool Has(int id) => _has(id);
    /// <summary>Marca ou desmarca; null = deu certo, senão o motivo em palavras simples.</summary>
    public string? Toggle(int id) => _toggle(id);

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor");

    /// <summary>Chave do cliente: pasta element\ (caminho completo, sem diferença de maiúsculas).</summary>
    public static string ClientKey(string elementDir)
        => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(elementDir).TrimEnd('\\', '/').ToLowerInvariant())))[..10];

    /// <summary>Favoritos guardados em arquivo para um tipo ("creature", "tree", "building"...) de um cliente.</summary>
    internal static string PathFor(string kind, string elementDir) => Path.Combine(Dir, $"jav-favorites-{kind}-{ClientKey(elementDir)}.txt");

    public static JavFavorites ForFile(string kind, string elementDir)
    {
        string path = PathFor(kind, elementDir);
        var list = new List<int>();
        try
        {
            if (File.Exists(path))
                foreach (var line in File.ReadAllLines(path))
                    if (int.TryParse(line.Trim(), out int n) && !list.Contains(n)) list.Add(n);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("favoritos da JAV: ler " + path, ex); }
        return new JavFavorites(() => list, list.Contains, id =>
        {
            bool had = list.Remove(id);
            if (!had) list.Add(id);
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = path + ".tmp";   // escrita atômica simples (arquivo de preferência: sem backup no armazém)
                File.WriteAllText(tmp, string.Join(Environment.NewLine, list));
                File.Move(tmp, path, true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (had) list.Add(id); else list.Remove(id);   // não gravou: volta como estava
                AppLog.Error("favoritos da JAV: gravar " + path, ex);
                return Loc.T("is.favSaveFail", "Não deu para guardar o favorito (pasta de configurações sem acesso?).");
            }
        });
    }
}

/// <summary>
/// Recentes de uma janela de escolher (árvore, construção...) por tipo e cliente, o último primeiro (até 30). Itens e
/// criaturas usam os recentes do editor de elements. %AppData%\MyAceEditor\jav-recent-&lt;tipo&gt;-&lt;cliente&gt;.txt.
/// </summary>
public static class JavRecents
{
    const int Keep = 30;
    static string PathFor(string kind, string elementDir) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", $"jav-recent-{kind}-{JavFavorites.ClientKey(elementDir)}.txt");

    public static List<int> Read(string kind, string elementDir)
    {
        var list = new List<int>();
        try
        {
            string p = PathFor(kind, elementDir);
            if (File.Exists(p)) foreach (var line in File.ReadAllLines(p)) if (int.TryParse(line.Trim(), out int n) && !list.Contains(n)) list.Add(n);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("recentes da JAV: ler", ex); }
        return list;
    }

    /// <summary>Põe no topo dos recentes (o que foi posto no mundo). Erro só vai para o log (não atrapalha o trabalho).</summary>
    public static void Add(string kind, string elementDir, int id)
    {
        try
        {
            var list = Read(kind, elementDir);
            list.Remove(id); list.Insert(0, id);
            if (list.Count > Keep) list.RemoveRange(Keep, list.Count - Keep);
            string p = PathFor(kind, elementDir);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p + ".tmp", string.Join(Environment.NewLine, list));
            File.Move(p + ".tmp", p, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("recentes da JAV: gravar", ex); }
    }
}
