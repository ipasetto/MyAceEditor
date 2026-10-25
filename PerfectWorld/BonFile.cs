using System.Numerics;
using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Esqueleto de um modelo (.bon do models.pck), sem animação — para pôr no lugar as malhas rígidas do .ski
/// (que vêm no espaço do osso). "MOXBEKSA" | i32 versão (5) | i32 nOssos | i32 nJuntas | … até 0x64 |
/// osso: i32 n + nome GBK, i32 ?, i32 pai (-1 = raiz), i32 ?, i32 nFilhos, matriz relativa ao pai (16 floats, vetor-linha),
/// matriz inicial (16 floats), filhos[nFilhos]. Conferido em esqueletos de 1 e 24 ossos (fecha no início das juntas).
/// </summary>
public sealed class BonFile
{
    public sealed record Bone(string Name, int Parent, Matrix4x4 Relative, Matrix4x4 Init);   // Init = espaço do modelo → osso na pose de ligação

    public List<Bone> Bones { get; } = new();

    static readonly Encoding Gbk;
    static BonFile() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public static BonFile? Read(byte[] d)
    {
        try
        {
            if (d.Length < 0x64 || Encoding.ASCII.GetString(d, 0, 8) != "MOXBEKSA") return null;
            int n = BitConverter.ToInt32(d, 12);
            if (n < 0 || n > 4096) return null;
            var b = new BonFile();
            int o = 0x64;
            for (int i = 0; i < n; i++)
            {
                int len = BitConverter.ToInt32(d, o);
                var name = Gbk.GetString(d, o + 4, len); o += 4 + len;
                int parent = BitConverter.ToInt32(d, o + 4), kids = BitConverter.ToInt32(d, o + 12); o += 16;
                Matrix4x4 M(int at)
                {
                    var m = new float[16];
                    for (int k = 0; k < 16; k++) m[k] = BitConverter.ToSingle(d, at + k * 4);
                    return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                }
                b.Bones.Add(new Bone(name, parent, M(o), M(o + 64)));
                o += 128 + kids * 4;
            }
            return b;
        }
        catch { return null; }
    }

    /// <summary>Matriz do osso no espaço do modelo (relativa × pai × avô …).</summary>
    public Matrix4x4 Absolute(int bone)
    {
        var m = Matrix4x4.Identity;
        for (int i = bone, guard = 0; i >= 0 && i < Bones.Count && guard < 256; i = Bones[i].Parent, guard++) m *= Bones[i].Relative;
        return m;
    }

    /// <summary>
    /// Pose padrão do esqueleto aplicada a uma malha com pele = Init × Absoluta de cada osso. Quando é a MESMA para todos
    /// os ossos, o esqueleto só desloca o modelo inteiro (ex.: catapulta da b30, centro na origem, erguida 1,25 m pela raiz)
    /// e dá para aplicar sem animação; quando varia (humanoides), a pose padrão não é a de ligação e fica como está.
    /// </summary>
    public Matrix4x4? CommonSkinOffset()
    {
        if (Bones.Count == 0) return null;
        Matrix4x4? first = null;
        for (int i = 0; i < Bones.Count; i++)
        {
            var p = Bones[i].Init * Absolute(i);
            if (first is not { } f) { first = p; continue; }
            if (!Near(p, f)) return null;
        }
        return first is { } r && !Near(r, Matrix4x4.Identity) ? r : null;
    }

    static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        var d = a - b;
        return MathF.Abs(d.M11) + MathF.Abs(d.M12) + MathF.Abs(d.M13) + MathF.Abs(d.M21) + MathF.Abs(d.M22) + MathF.Abs(d.M23)
             + MathF.Abs(d.M31) + MathF.Abs(d.M32) + MathF.Abs(d.M33) < 0.03f
            && MathF.Abs(d.M41) + MathF.Abs(d.M42) + MathF.Abs(d.M43) < 0.03f;
    }

    public int IndexOf(string name) => Bones.FindIndex(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
