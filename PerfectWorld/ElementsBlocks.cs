namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Tabela de blocos do elements.data v0x3000003C (Perfect World 1.4.4),
/// recuperada por engenharia reversa do parser do cliente (elementclient.exe,
/// função FUN_0071a310): cada bloco é "uint count + count registros" e o
/// tamanho do registro é fixo. A ordem abaixo é a ordem exata de leitura.
///
/// Os nomes dos campos dentro de cada registro ainda estão sendo mapeados;
/// por ora todo registro é lido como { int id; wchar name[32]; bytes resto }.
/// </summary>
public static class ElementsBlocks
{
    public const uint Version = 0x3000003C;

    // Tamanho (em bytes) do registro de cada bloco, na ordem de leitura.
    public static readonly int[] RecordSizes =
    {
        84, 68, 356, 1420, 68, 72, 1128, 68, 72, 1168,
        68, 68, 376, 68, 68, 368, 68, 364, 68, 624,
        68, 348, 524, 488, 348, 348, 352, 68, 436, 84,
        200, 1092, 1116, 1228, 72, 68,
    };
}
