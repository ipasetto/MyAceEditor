namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Onde a câmera do Mundo 3D começa em mapas cuja entrada calculada (portal/1º NPC) não bate com o jogo: posições
/// conferidas pelo usuário no cliente PWI 1.5.5 em 30/10/2026 (X, altura, Z do Mundo 3D). Os outros mapas usam a entrada
/// calculada (RegionFile.EntranceOf), que ele marcou como certa.
/// </summary>
public static class CameraStarts
{
    static readonly Dictionary<string, (float X, float Y, float Z)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["world"] = (1090.03f, 240f, 1033.48f),   // usuário 30/10: meio do mapa
        ["a05"] = (-176.98f, 340f, 128.91f),
        ["a12"] = (-445.92f, 465f, -375.14f),
        ["a26"] = (338.76f, 5f, -169.58f),   // usuário 30/10
        ["a39"] = (-341.32f, 35f, 191.57f),
        ["a41"] = (-196.21f, 35f, 313.15f),
        ["a49"] = (-235.2f, 90f, 168.29f),
        ["a61"] = (-768.26f, 70f, -260.42f),
        ["a62"] = (-252.83f, 310f, -304.56f),
        ["a66"] = (65.54f, 35f, -93.08f),
    };

    public static (float X, float Y, float Z)? Of(string map) => Known.TryGetValue(map, out var p) ? p : null;
}
