using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Construção do cenário (litmodels.pck: litmodels\&lt;mapa&gt;\&lt;pedaço&gt;\litmodel_N.bmd), já iluminada (cor nos vértices).
/// "MOXB" | u32 versão | escala 3f | direção 3f | cima 3f | posição no mundo 3f | u32 nMalhas |
/// malha: u32 versão, nome[64], textura[256], nVértices, nTriângulos, vértices de 24 B (pos local 3f, cor BGRA, uv 2f),
/// índices u16 ×3 por triângulo, normais 3f, cores de dia e de noite (BGRA, 1 por vértice) e dados extras de tamanho
/// variável — a próxima malha é achada pela versão. Desenho usa a cor de dia (a do vértice pode ser outra, ex.: a19).
/// Versão 0x80000001 (metade dos arquivos do 1.4.4): 1 byte + 0x10000002 depois da versão, resto 5 bytes adiante, malhas 0x10000005.
/// </summary>
public sealed class BmdModel
{
    public sealed record Mesh(string Texture, byte[] Vertices, ushort[] Indices);   // vértice 24 B como no arquivo

    public float Scale;
    public float[] Dir = new float[3], Up = new float[3], Pos = new float[3];
    public List<Mesh> Meshes { get; } = new();
    public int Declared;

    static readonly Encoding Gbk;
    static BmdModel() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public static BmdModel? Read(byte[] d)
    {
        try { return Parse(d); } catch { return null; }
    }

    static BmdModel? Parse(byte[] d)
    {
        if (d.Length < 0x3C || Encoding.ASCII.GetString(d, 0, 4) != "MOXB") return null;
        // versão 0x80000001: 1 byte + a versão antiga (0x10000002) na frente; o resto igual, 5 bytes adiante
        int sh = BitConverter.ToUInt32(d, 4) == 0x80000001 ? 5 : 0;
        var m = new BmdModel { Scale = BitConverter.ToSingle(d, 8 + sh) };
        for (int i = 0; i < 3; i++)
        {
            m.Dir[i] = BitConverter.ToSingle(d, 0x14 + sh + i * 4);
            m.Up[i] = BitConverter.ToSingle(d, 0x20 + sh + i * 4);
            m.Pos[i] = BitConverter.ToSingle(d, 0x2C + sh + i * 4);
        }
        m.Declared = BitConverter.ToInt32(d, 0x38 + sh);
        if (m.Declared <= 0 || m.Declared > 10000) return m;
        int o = 0x3C + sh;
        uint ver = BitConverter.ToUInt32(d, o);
        for (int k = 0; k < m.Declared && o > 0 && o + 4 + 64 + 256 + 8 <= d.Length; k++)
        {
            int tex = o + 4 + 64;
            int nv = BitConverter.ToInt32(d, tex + 256), nf = BitConverter.ToInt32(d, tex + 260);
            // malha 0x10000006 (região dos abissais, pedaço 70 do mundo 1.4.4): 1 byte a mais antes dos vértices;
            // sem pular, tudo saía deslocado e virava triângulos infinitos que cobriam a tela de preto/azul
            int extra = BitConverter.ToUInt32(d, o) >= 0x10000006 ? 1 : 0;
            int vo = tex + 264 + extra, io = vo + nv * 24, end = io + nf * 6;
            if (nv <= 0 || nf <= 0 || nv > 65536 || end > d.Length) break;
            int z = Array.IndexOf(d, (byte)0, tex, 256);
            var texName = Gbk.GetString(d, tex, (z < 0 ? tex + 256 : z) - tex);
            var verts = new byte[nv * 24];
            Buffer.BlockCopy(d, vo, verts, 0, verts.Length);
            // depois dos índices: normais (nv × 12) e as cores de DIA e de NOITE (nv × 4 cada; A3DLitMesh). A cor dentro do
            // vértice nem sempre é a iluminada (a19: tons verde/roxo); a de dia é a que o jogo mostra. Só usa se for opaca.
            int day = end + nv * 12;
            if (day + nv * 8 <= d.Length)
            {
                int opaque = 0;
                for (int i = 0; i < nv; i++) if (d[day + i * 4 + 3] == 0xFF) opaque++;
                if (opaque >= nv * 9 / 10)
                    for (int i = 0; i < nv; i++) Buffer.BlockCopy(d, day + i * 4, verts, i * 24 + 12, 3);   // BGR; alfa do vértice fica
            }
            var idx = new ushort[nf * 3];
            Buffer.BlockCopy(d, io, idx, 0, idx.Length * 2);
            // rede de segurança: formato que ainda não conhecemos não pode virar triângulo do tamanho do mundo
            bool sane = true;
            for (int i = 0; i < nv && sane; i++)
                for (int c = 0; c < 3; c++)
                {
                    float v = BitConverter.ToSingle(verts, i * 24 + c * 4);
                    if (!float.IsFinite(v) || Math.Abs(v) > 100_000) { sane = false; break; }
                }
            if (sane && idx.All(x => x < nv)) m.Meshes.Add(new Mesh(texName, verts, idx));
            o = k + 1 < m.Declared ? NextMesh(d, end + nv * 12, ver) : -1;
        }
        return m;
    }

    // próxima malha: mesma versão, nome legível (ou vazio) e contagens plausíveis
    static int NextMesh(byte[] d, int from, uint ver)
    {
        for (int o = Math.Max(from, 0); o + 4 + 64 + 256 + 8 <= d.Length; o++)
        {
            if (BitConverter.ToUInt32(d, o) != ver) continue;
            int tex = o + 4 + 64;
            int nv = BitConverter.ToInt32(d, tex + 256), nf = BitConverter.ToInt32(d, tex + 260);
            if (nv <= 0 || nf <= 0 || nv > 65536 || tex + 264 + (ver >= 0x10000006 ? 1 : 0) + nv * 24 + nf * 6 > d.Length) continue;
            if (d[tex] < 0x20) continue;   // textura começa com texto
            return o;
        }
        return -1;
    }
}
