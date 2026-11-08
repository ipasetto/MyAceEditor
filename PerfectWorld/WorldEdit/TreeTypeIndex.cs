using System.Text;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Um tipo de árvore (caminhos do trees.pck) e os mapas que o usam.</summary>
public sealed record TreeKind(string Spt, string Comp, List<string> Maps)
{
    /// <summary>Número estável do tipo (favoritos da JAV): não muda entre mapas nem sessões.</summary>
    public int Sig
    {
        get
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in (Spt + "|" + Comp).ToLowerInvariant()) h = (h ^ c) * 16777619;
                return h & 0x7FFFFFFF;
            }
        }
    }
}

/// <summary>
/// Tipos de árvore de TODOS os mapas do cliente (lista do cabeçalho de cada .ecwld: CECScene::LoadTreeTypeData). Leve
/// (só o cabeçalho e a lista); mapa que não abre fica de fora (log).
/// </summary>
public static class TreeTypeIndex
{
    public static List<TreeKind> Build(string elementDir)
    {
        var byKey = new Dictionary<string, TreeKind>(StringComparer.OrdinalIgnoreCase);
        string mapsDir = Path.Combine(elementDir, "maps");
        if (!Directory.Exists(mapsDir)) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding(936);
        foreach (var dir in Directory.GetDirectories(mapsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string map = Path.GetFileName(dir), f = Path.Combine(dir, map + ".ecwld");
            if (!File.Exists(f)) continue;
            try
            {
                var b = File.ReadAllBytes(f);
                if (b.Length < 116) continue;
                int off = (int)BitConverter.ToUInt32(b, 36), n = BitConverter.ToInt32(b, 44);
                int p = off;
                for (int i = 0; i < n; i++)
                {
                    string S() { int l = BitConverter.ToInt32(b, p); if (l < 0 || p + 4 + l > b.Length) throw new InvalidDataException("tipo de árvore ilegível"); var s = gbk.GetString(b, p + 4, l).TrimEnd((char)0); p += 4 + l; return s; }
                    string spt = S(), comp = S();
                    string key = spt + "|" + comp;
                    if (!byKey.TryGetValue(key, out var k)) byKey[key] = k = new TreeKind(spt, comp, []);
                    if (!k.Maps.Contains(map, StringComparer.OrdinalIgnoreCase)) k.Maps.Add(map);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { AppLog.Info($"tipos de árvore: {map}: {ex.Message}"); }
        }
        return [.. byKey.Values];
    }
}
