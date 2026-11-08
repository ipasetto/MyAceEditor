using System.Numerics;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Uma luz da cena (SCENELIGHT, 112 B). Diffuse/Ambient = r, g, b, força (a26 usa 2,0 / 0,4). Raw = os 112 B lidos.</summary>
public sealed record SceneLight(Vector3 Pos, float Range, Vector3 Atten, Vector4 Diffuse, Vector4 Ambient, uint Flags, byte[]? Raw = null)
{
    public const uint NightOnly = 1;   // SCENELIGHT_NIGHT_ONLY (EC_SceneLights.h): acende só à noite (cor × fator dia/noite)
    public bool Night => (Flags & NightOnly) != 0;
}

/// <summary>
/// maps\&lt;mapa&gt;\scenelights.dat (EC_SceneLights.cpp CECSceneLights::Load): u32 0x80000001, i32 N, N × 112 B (posição,
/// alcance, atenuação, cor, ambiente, caixa A3DAABB = posição ± alcance, flags). Só cliente. No jogo iluminam os modelos com
/// pele (personagens, NPCs, monstros) perto; o chão e as construções já vêm com a luz assada. Luz sem mudança grava os
/// bytes lidos (o arquivo regrava idêntico).
/// </summary>
public static class SceneLightsFile
{
    public const uint Version = 0x80000001;
    const int Size = 112;

    public static List<SceneLight> Read(byte[] b)
    {
        if (b.Length < 8 || BitConverter.ToUInt32(b, 0) != Version) throw new InvalidDataException("scenelights.dat: versão desconhecida");
        int n = BitConverter.ToInt32(b, 4);
        if (n < 0 || 8 + (long)n * Size != b.Length) throw new InvalidDataException("scenelights.dat: tamanho não bate com a quantidade de luzes");
        var list = new List<SceneLight>(n);
        for (int i = 0; i < n; i++)
        {
            int o = 8 + i * Size;
            float F(int k) => BitConverter.ToSingle(b, o + k);
            list.Add(new SceneLight(new Vector3(F(0), F(4), F(8)), F(12), new Vector3(F(16), F(20), F(24)), new Vector4(F(28), F(32), F(36), F(40)), new Vector4(F(44), F(48), F(52), F(56)),
                BitConverter.ToUInt32(b, o + 108), b[o..(o + Size)]));
        }
        return list;
    }

    public static byte[] Write(IReadOnlyList<SceneLight> lights)
    {
        var b = new byte[8 + lights.Count * Size];
        BitConverter.GetBytes(Version).CopyTo(b, 0);
        BitConverter.GetBytes(lights.Count).CopyTo(b, 4);
        for (int i = 0; i < lights.Count; i++)
        {
            var l = lights[i]; int o = 8 + i * Size;
            if (l.Raw is { Length: Size } raw && Same(raw, l)) { raw.CopyTo(b, o); continue; }   // sem mudança: os bytes lidos
            void F(int k, float v) => BitConverter.GetBytes(v).CopyTo(b, o + k);
            F(0, l.Pos.X); F(4, l.Pos.Y); F(8, l.Pos.Z); F(12, l.Range);
            F(16, l.Atten.X); F(20, l.Atten.Y); F(24, l.Atten.Z);
            F(28, l.Diffuse.X); F(32, l.Diffuse.Y); F(36, l.Diffuse.Z); F(40, l.Diffuse.W);
            F(44, l.Ambient.X); F(48, l.Ambient.Y); F(52, l.Ambient.Z); F(56, l.Ambient.W);
            // caixa A3DAABB: centro, meia medida, mínimo, máximo (posição ± alcance, como em todas as luzes do jogo)
            var e = new Vector3(l.Range);
            F(60, l.Pos.X); F(64, l.Pos.Y); F(68, l.Pos.Z); F(72, e.X); F(76, e.Y); F(80, e.Z);
            F(84, l.Pos.X - l.Range); F(88, l.Pos.Y - l.Range); F(92, l.Pos.Z - l.Range);
            F(96, l.Pos.X + l.Range); F(100, l.Pos.Y + l.Range); F(104, l.Pos.Z + l.Range);
            BitConverter.GetBytes(l.Flags).CopyTo(b, o + 108);
        }
        return b;
    }

    /// <summary>Os bytes lidos ainda descrevem esta luz (nada mudou nos campos).</summary>
    static bool Same(byte[] r, SceneLight l)
    {
        float F(int k) => BitConverter.ToSingle(r, k);
        return F(0) == l.Pos.X && F(4) == l.Pos.Y && F(8) == l.Pos.Z && F(12) == l.Range && F(16) == l.Atten.X && F(20) == l.Atten.Y && F(24) == l.Atten.Z
            && F(28) == l.Diffuse.X && F(32) == l.Diffuse.Y && F(36) == l.Diffuse.Z && F(40) == l.Diffuse.W
            && F(44) == l.Ambient.X && F(48) == l.Ambient.Y && F(52) == l.Ambient.Z && F(56) == l.Ambient.W && BitConverter.ToUInt32(r, 108) == l.Flags;
    }
}
