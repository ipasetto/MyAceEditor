using System.Text;
using OpenTK.Mathematics;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Malha estática .mox do Angelica (A3DFrame + A3DMesh, binário "MOXB"; grasses.pck): textos terminados em 0
/// ("VERSION: n" opcional, "FRAME: nome"), i32 nº de quadros + matrizes 4×4, i32 nº de caixas (0 na grama), i32 nº de
/// malhas; malha = "MESH: nome", i32 vértices, i32 índices, i32 quadros, bool LOD, bool arame, propriedades (v1: i32
/// tamanho + bytes; v2: u32), índices u16, [LOD], vértices de 32 B (posição, normal, uv) por quadro, ["DETAILTEXTURE: "
/// + matriz], "TEXTURE: nome" (em &lt;pasta do .mox&gt;\textures\) e o material. PW-KB §S17 (grama). Só leitura.
/// </summary>
public sealed class MoxModel
{
    public sealed record Mesh(Vector3[] Pos, Vector2[] Uv, ushort[] Index, string Texture);
    public List<Mesh> Meshes { get; } = new();

    public static MoxModel? Read(byte[] b)
    {
        try { return Parse(b); }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
    }

    static MoxModel? Parse(byte[] b)
    {
        int p = b.Length >= 4 && Encoding.ASCII.GetString(b, 0, 4) == "MOXB" ? 4 : 0;
        if (p == 0) return null;   // "MOXT" (texto) não aparece na grama
        string Str()
        {
            int e = Array.IndexOf(b, (byte)0, p);
            if (e < 0) throw new InvalidDataException();
            var s = Gbk.GetString(b, p, e - p); p = e + 1;
            return s;
        }
        int I() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
        int ver = 1;
        var s0 = Str();
        if (s0.StartsWith("VERSION: ")) { int.TryParse(s0[9..], out ver); s0 = Str(); }
        if (!s0.StartsWith("FRAME: ")) return null;
        int frames = I(); p += 64 * frames;
        if (I() != 0) return null;   // caixas de colisão: não usadas na grama
        int nMesh = I();
        var m = new MoxModel();
        for (int k = 0; k < nMesh; k++)
        {
            if (k > 0)
            {
                // depois do material vem a próxima malha (o material tem tamanho variável): procura o texto
                int at = IndexOf(b, "MESH: ", p), atV = IndexOf(b, "VERSION: ", p);
                if (atV >= 0 && atV < at) at = atV;
                if (at < 0) break;
                p = at;
            }
            var s = Str(); int mver = 1;
            if (s.StartsWith("VERSION: ")) { int.TryParse(s[9..], out mver); s = Str(); }
            if (!s.StartsWith("MESH: ")) break;
            int nv = I(), ni = I(), nf = I();
            bool lod = b[p] != 0; p += 2;
            p += mver == 1 ? 4 + BitConverter.ToInt32(b, p) : 4;
            var idx = new ushort[ni];
            for (int i = 0; i < ni; i++) idx[i] = BitConverter.ToUInt16(b, p + 2 * i);
            p += 2 * ni;
            if (lod) p += 12 + 2 * nv;
            var pos = new Vector3[nv]; var uv = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                int o = p + 32 * i;
                pos[i] = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
                uv[i] = new Vector2(BitConverter.ToSingle(b, o + 24), BitConverter.ToSingle(b, o + 28));
            }
            p += 32 * nv * Math.Max(1, nf);
            var t = Str();
            if (t.StartsWith("DETAILTEXTURE: ")) { p += 64; t = Str(); }
            m.Meshes.Add(new Mesh(pos, uv, idx, t.StartsWith("TEXTURE: ") ? t[9..] : ""));
        }
        return m.Meshes.Count > 0 ? m : null;
    }

    static int IndexOf(byte[] b, string text, int from)
    {
        var t = Encoding.ASCII.GetBytes(text);
        int i = b.AsSpan(from).IndexOf(t);
        return i < 0 ? -1 : from + i;
    }

    static readonly Encoding Gbk = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
}
