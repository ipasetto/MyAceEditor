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
    public sealed record Mesh(string Name, int Texture, float[] Vertices, ushort[] Indices, int Bone = -1, float[]? Weights = null, byte[]? Bones = null);   // vértice: x y z nx ny nz u v; Bone ≥ 0 = rígida; pele: 4 pesos + 4 ossos por vértice
    public List<string> BoneNames { get; } = new();   // v≥9: nomes dos ossos usados (índice da malha rígida → nome no .bon)

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
        if (m.Version >= 9) for (int i = 0; i < nBone; i++) m.BoneNames.Add(Str());
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
            int bone = rigid ? r.ReadInt32() : -1;   // osso da malha rígida
            int tex = r.ReadInt32(); r.ReadInt32();
            int nv = r.ReadInt32(), ni = r.ReadInt32();
            if (nv < 0 || ni < 0 || nv > 1_000_000 || ni > 3_000_000) throw new InvalidDataException();
            var v = new float[nv * 8];
            float[]? w = rigid ? null : new float[nv * 4];
            byte[]? bi = rigid ? null : new byte[nv * 4];
            for (int i = 0; i < nv; i++)
            {
                v[i * 8] = r.ReadSingle(); v[i * 8 + 1] = r.ReadSingle(); v[i * 8 + 2] = r.ReadSingle();
                if (!rigid)
                {
                    // 3 pesos (o 4º = 1 − soma) + 4 índices de osso u8
                    float a = r.ReadSingle(), b = r.ReadSingle(), c = r.ReadSingle();
                    w![i * 4] = a; w[i * 4 + 1] = b; w[i * 4 + 2] = c; w[i * 4 + 3] = Math.Max(0, 1 - a - b - c);
                    r.Read(bi!, i * 4, 4);
                }
                for (int j = 3; j < 8; j++) v[i * 8 + j] = r.ReadSingle();
            }
            var idx = new ushort[ni];
            for (int i = 0; i < ni; i++) idx[i] = r.ReadUInt16();
            m.Meshes.Add(new Mesh(name, tex, v, idx, bone, w, bi));
        }
        return r.BaseStream.Position == d.Length ? m : null;
    }

    /// <summary>
    /// Põe as malhas rígidas (que vêm no espaço do osso) no espaço do modelo, pela pose de ligação do esqueleto.
    /// Sem .bon: se a altura está claramente em Z e nasce em z≈0, gira para Y para cima (raiz do 3ds Max) [?].
    /// </summary>
    public void PlaceRigid(BonFile? bones)
    {
        foreach (var mesh in Meshes)
        {
            if (mesh.Bone < 0) continue;
            int b = mesh.Bone;
            if (bones is not null && BoneNames.Count > b) b = bones.IndexOf(BoneNames[b]);
            if (bones is null || b < 0 || b >= bones.Bones.Count) { StandUp(mesh.Vertices); continue; }
            var abs = bones.Absolute(b);
            var v = mesh.Vertices;
            for (int i = 0; i < v.Length; i += 8)
            {
                var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v[i], v[i + 1], v[i + 2]), abs);
                var n = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(v[i + 3], v[i + 4], v[i + 5]), abs);
                v[i] = p.X; v[i + 1] = p.Y; v[i + 2] = p.Z; v[i + 3] = n.X; v[i + 4] = n.Y; v[i + 5] = n.Z;
            }
        }
    }

    /// <summary>
    /// Malhas com pele na pose padrão do esqueleto (sem animação): cada vértice = Σ peso × (pos × Init × Absoluta do osso).
    /// Só para erguer modelos feitos centrados na origem que o esqueleto põe de pé (catapulta da b30 +1,25 m, bandeiras de
    /// guerra +8,8 m): aplicada quando a malha estava afundada e a pose leva a base para o chão; senão fica como veio.
    /// </summary>
    public void PlaceSkin(BonFile? bones)
    {
        if (bones is null || bones.Bones.Count == 0) return;
        var mats = new System.Numerics.Matrix4x4[bones.Bones.Count];
        for (int b = 0; b < mats.Length; b++) mats[b] = bones.Bones[b].Init * bones.Absolute(b);
        // índice do osso no .ski → no .bon (v9 traz os nomes; v8 usa o índice do esqueleto)
        int Map(int i) => BoneNames.Count > 0 ? (i < BoneNames.Count ? bones.IndexOf(BoneNames[i]) : -1) : (i < mats.Length ? i : -1);
        var map = new int[256];
        for (int i = 0; i < 256; i++) map[i] = Map(i);
        var placed = new List<(Mesh Mesh, float[] V)>();
        float oMin = float.MaxValue, oMax = float.MinValue, nMin = float.MaxValue, nMax = float.MinValue;
        foreach (var mesh in Meshes)
        {
            if (mesh.Bone >= 0 || mesh.Weights is not { } w || mesh.Bones is not { } bi) continue;
            var v = mesh.Vertices; var nv = (float[])v.Clone();
            for (int i = 0, k = 0; i < v.Length; i += 8, k += 4)
            {
                var p = new System.Numerics.Vector3(v[i], v[i + 1], v[i + 2]); var n = new System.Numerics.Vector3(v[i + 3], v[i + 4], v[i + 5]);
                System.Numerics.Vector3 sp = default, sn = default; float sw = 0;
                for (int j = 0; j < 4; j++)
                {
                    float wj = w[k + j];
                    if (wj <= 0 || map[bi[k + j]] is var b && b < 0) continue;
                    sp += wj * System.Numerics.Vector3.Transform(p, mats[b]);
                    sn += wj * System.Numerics.Vector3.TransformNormal(n, mats[b]);
                    sw += wj;
                }
                if (sw > 0.001f) { sp /= sw; sn = System.Numerics.Vector3.Normalize(sn); } else { sp = p; sn = n; }
                nv[i] = sp.X; nv[i + 1] = sp.Y; nv[i + 2] = sp.Z; nv[i + 3] = sn.X; nv[i + 4] = sn.Y; nv[i + 5] = sn.Z;
                oMin = Math.Min(oMin, p.Y); oMax = Math.Max(oMax, p.Y); nMin = Math.Min(nMin, sp.Y); nMax = Math.Max(nMax, sp.Y);
            }
            placed.Add((mesh, nv));
        }
        if (placed.Count == 0) return;
        // só corrige modelo AFUNDADO (mais de 1/4 abaixo da origem) que a pose padrão põe de pé no chão; nos outros a pose
        // padrão não é a mostrada no jogo (lá vale a animação "parado") e pode afundar bichos que estavam certos
        float oh = oMax - oMin, nh = nMax - nMin;
        if (oh <= 0.01f || float.IsNaN(nh) || nh > oh * 1.5f || nh < oh * 0.67f) return;
        if (oMin >= -0.25f * oh || MathF.Abs(nMin) > 0.1f * nh) return;
        foreach (var (mesh, nv) in placed) Array.Copy(nv, mesh.Vertices, nv.Length);
    }

    /// <summary>Junta as malhas de outro skin do mesmo modelo (AddiSkinPath do .ecm); chame depois do PlaceRigid dos dois.</summary>
    public void Append(SkiModel other)
    {
        int t0 = Textures.Count;
        Textures.AddRange(other.Textures);
        foreach (var m in other.Meshes) Meshes.Add(m with { Texture = m.Texture < 0 ? m.Texture : m.Texture + t0, Bone = -1 });
    }

    static void StandUp(float[] v)
    {
        float minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < v.Length; i += 8) { minY = Math.Min(minY, v[i + 1]); maxY = Math.Max(maxY, v[i + 1]); minZ = Math.Min(minZ, v[i + 2]); maxZ = Math.Max(maxZ, v[i + 2]); }
        if (v.Length == 0 || maxZ - minZ < 1.5f * (maxY - minY) || Math.Abs(minZ) > 0.2f * (maxZ - minZ)) return;
        for (int i = 0; i < v.Length; i += 8)
        {
            (v[i + 1], v[i + 2]) = (v[i + 2], -v[i + 1]);       // posição
            (v[i + 4], v[i + 5]) = (v[i + 5], -v[i + 4]);       // normal
        }
    }
}
