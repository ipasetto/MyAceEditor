namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Comandos "d N" que cada servidor aceita (PW-KB §S35): o painel do GM só mostra o que existe na versão do jogo aberto.
/// A lista vem do gs de cada versão (cmd_type de gplayer_controller::DebugCommandHandler). Versão sem lista = mostra tudo.
/// </summary>
public static class GmCommands
{
    // gs 1.4.4 (v60): comandos do painel que não existem nele; no 1.5.5 todos existem
    static readonly HashSet<int> MissingIn144 = [1991, 10802, 10822, 10837, 10850, 10855, 10857];

    public static bool Supported(int? elementsVersion, int command) =>
        elementsVersion is not (60 or 61) || !MissingIn144.Contains(command);
}
