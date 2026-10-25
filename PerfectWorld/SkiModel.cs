using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Malhas de um modelo do jogo (.ski do models.pck), para desenhar parado (pose de ligação, sem ossos).
/// "MOXBIKSA" | versão, nPele, nRígidas, 2×tipo de vértice, nTexturas, nMateriais, nOssos, ?, ? | 60 B |
/// [v≥9 nomes dos ossos] | texturas (i32 n + GBK) | materiais ("MATERIAL: " + 1 B + 17 floats + 1 B) |
/// malha com pele: nome, textura, material, nVértices, nÍndices, vértices de 48 B (pos, 3 pesos, ossos, normal, uv), índices u16 |
/// malha rígida: nome, osso, textura, material, nVértices, nÍndices, vértices de 32 B (pos, normal, uv), índices u16.
/// </summary>
public sealed class SkiModel
{
    public sealed record Mesh(string Name, int Texture, float[] Vertices, ushort[] Indices);   // vértice: x y z nx ny nz u v

    public int Version { get; private set; }
    public List<string> Textures { get; } = new();
    public List<Mesh> Meshes { get; } = new();

    static readonly Encoding Gbk;
    static SkiModel() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    /// <summary>Lê; null se não for .ski ou se o formato não bater até o fim.</summary>
    public static SkiModel? Read(byte[] d) { try { return Parse(d); } catch { return null; } }

    static SkiModel? Parse(byte[] d)
    {
        if (d.Length < 108 || Encoding.ASCII.GetString(d, 0, 8) != "MOXBIKSA") return null;
        var r = new BinaryReader(new MemoryStream(d));
        r.BaseStream.Position = 8;
        var m = new SkiModel { Version = r.ReadInt32() };
        int nSkin = r.ReadInt32(), nRigid = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
        int nTex = r.ReadInt32(), nMat = r.ReadInt32(), nBone = r.ReadInt32();
        r.BaseStream.Position = 108;
        string Str() { int n = r.ReadInt32(); if (n < 0 || n > 4096) throw new InvalidDataException(); return Gbk.GetString(r.ReadBytes(n)); }
        if (m.Version >= 9) for (int i = 0; i < nBone; i++) Str();
        for (int i = 0; i < nTex; i++) m.Textures.Add(Str());
        for (int i = 0; i < nMat; i++)
        {
            if (Encoding.ASCII.GetString(r.ReadBytes(10)) != "MATERIAL: ") throw new InvalidDataException();
            r.BaseStream.Position += 1 + 68 + 1;
        }
        for (int k = 0; k < nSkin + nRigid; k++)
        {
            bool rigid = k >= nSkin;
            var name = Str();
            if (rigid) r.ReadInt32();   // osso
            int tex = r.ReadInt32(); r.ReadInt32();
            int nv = r.ReadInt32(), ni = r.ReadInt32();
            if (nv < 0 || ni < 0 || nv > 1_000_000 || ni > 3_000_000) throw new InvalidDataException();
            var v = new float[nv * 8];
            for (int i = 0; i < nv; i++)
            {
                v[i * 8] = r.ReadSingle(); v[i * 8 + 1] = r.ReadSingle(); v[i * 8 + 2] = r.ReadSingle();
                if (!rigid) r.BaseStream.Position += 16;   // 3 pesos + índices dos ossos
                for (int j = 3; j < 8; j++) v[i * 8 + j] = r.ReadSingle();
            }
            var idx = new ushort[ni];
            for (int i = 0; i < ni; i++) idx[i] = r.ReadUInt16();
            m.Meshes.Add(new Mesh(name, tex, v, idx));
        }
        return r.BaseStream.Position == d.Length ? m : null;
    }
}
