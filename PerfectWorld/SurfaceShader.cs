using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Superfície .sdr do motor: quando a textura de uma malha tem ".sdr.", o jogo carrega shaders\nome.sdr (shaders.pck) no lugar
/// dela, com a textura do 1º estágio, o alfa dela e a rolagem (água que corre, lava). Fonte: A3DTextureMan::LoadTextureFromFile.
/// </summary>
public sealed record SurfaceShader(string Texture, float ScrollU, float ScrollV)
{
    static SurfaceShader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static readonly Regex TextureLine = new(@"^\s*texture\s+TEX_\w+\s+(?:\d+\s+)?""?([^""\r\n]+?)""?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    static readonly Regex ScrollLine = new(@"^\s*tcscroll\s+(\S+)\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>Nome do shader ("瀑布") de uma textura de malha ("Building\textures\d\瀑布.sdr.tga"); null se não for superfície.</summary>
    public static string? NameOf(string texture)
    {
        int at = texture.IndexOf(".sdr", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : texture[..at].Replace('/', '\\').Split('\\')[^1];
    }

    /// <summary>Lê o .sdr de texto (GBK). Null se não tiver textura no 1º estágio.</summary>
    public static SurfaceShader? Read(byte[] data)
    {
        var text = Encoding.GetEncoding(936).GetString(data);
        var tex = TextureLine.Match(text);
        if (!tex.Success) return null;
        var file = tex.Groups[1].Value.Trim();
        var scroll = ScrollLine.Match(text, tex.Index);
        // o 1º estágio termina no próximo "texture"; rolagem de outro estágio não vale para ele
        var next = TextureLine.Match(text, tex.Index + tex.Length);
        bool own = scroll.Success && (!next.Success || scroll.Index < next.Index);
        return new SurfaceShader(file.Contains('\\') ? file : @"shaders\textures\" + file, own ? Number(scroll.Groups[1].Value) : 0, own ? Number(scroll.Groups[2].Value) : 0);
    }

    static float Number(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
