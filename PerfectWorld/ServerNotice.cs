namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Arquivo gravado fora do editor principal (Mundo 3D: zonas, territórios, teleportes, caminhos, construções) que o servidor ainda
/// não tem. O editor principal ouve e acende o "Salvar e enviar ao servidor".
/// </summary>
public static class ServerNotice
{
    public static event Action<string>? Saved;

    /// <summary>Nome curto para a lista do que falta enviar (ex.: "world\precinct.sev").</summary>
    public static void Notify(string file) => Saved?.Invoke(file);
}
