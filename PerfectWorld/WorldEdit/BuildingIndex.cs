using System.Numerics;
using System.Text.Json;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Um .bmd lido para o índice: forma (assinatura), caixa local, colisão. Offset/CSize = carimbo no pacote.</summary>
public sealed record IndexedBmd(string Path, long Offset, int CSize, int Sig, float[] Min, float[] Max, bool Solid, bool CollideOnly);

/// <summary>Uma forma de construção num mapa: exemplo (caminho no litmodels.pck), tamanho e quantas vezes aparece.</summary>
public sealed record BuildingShape(int Sig, string Sample, Vector3 Min, Vector3 Max, bool Solid, int Count)
{
    public Vector3 Size => Max - Min;
}

/// <summary>
/// Índice das construções de TODOS os mapas e instâncias do cliente (catálogo da pílula Construções; desenho em
/// docs/superpowers/specs/2026-11-08-catalogo-todos-os-mapas-design.md). Só entram .bmd com referência no .ecwld do mapa
/// (órfão o jogo não mostra) e que não são só de colisão. Cada .bmd lido fica num cache por cliente (caminho + posição e
/// tamanho no pacote): depois da primeira vez só relê o que mudou. Leitura em vários núcleos (um leitor do pacote por
/// núcleo); arquivo que não abre fica fora e vai para o log.
/// </summary>
public sealed class BuildingIndex
{
    /// <summary>Formas por mapa (pasta do mapa, ex.: "world", "a05").</summary>
    public Dictionary<string, List<BuildingShape>> ByMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Files { get; private set; }
    public int Failed { get; private set; }

    /// <summary>Número da forma que não muda entre sessões (o mesmo da lista "Por perto" do Mundo 3D).</summary>
    public static int Sig(int floats, int indices, float sx, float sy, float sz)
    {
        unchecked
        {
            int h = (int)2166136261;
            foreach (int v in new[] { floats, indices, (int)MathF.Round(sx * 10), (int)MathF.Round(sy * 10), (int)MathF.Round(sz * 10) }) h = (h ^ v) * 16777619;
            return h & 0x7FFFFFFF;
        }
    }

    /// <summary>Lê um .bmd para o índice; null = não é construção desenhável ou não abriu.</summary>
    public static IndexedBmd? Read(string path, long offset, int csize, byte[] d)
    {
        if (BmdModel.Read(d) is not { } m || m.Meshes.Count == 0) return null;
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        int nv = 0, ni = 0;
        foreach (var mesh in m.Meshes)
        {
            nv += mesh.Vertices.Length / 24; ni += mesh.Indices.Length;
            for (int o = 0; o + 12 <= mesh.Vertices.Length; o += 24)
            {
                var v = new Vector3(BitConverter.ToSingle(mesh.Vertices, o), BitConverter.ToSingle(mesh.Vertices, o + 4), BitConverter.ToSingle(mesh.Vertices, o + 8));
                mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
            }
        }
        if (nv == 0) return null;
        var size = mx - mn;
        bool solid = (BmdModel.CdBrushes(d)?.Count ?? 0) > 0;
        return new IndexedBmd(path, offset, csize, Sig(nv * 3, ni, size.X, size.Y, size.Z), [mn.X, mn.Y, mn.Z], [mx.X, mx.Y, mx.Z], solid, m.CollideOnly);
    }

    static string CachePath(string elementDir) => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor",
        $"building-index-{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(elementDir).TrimEnd('\\', '/').ToLowerInvariant())))[..10]}.json");

    /// <summary>
    /// Monta o índice (segundo plano). progress(feitos, total) vem de qualquer thread. Cancelar = devolve null.
    /// Erro de leitura de um arquivo não para o índice; erro no pacote inteiro devolve null (log).
    /// </summary>
    public static BuildingIndex? Build(string elementDir, Action<int, int>? progress = null, CancellationToken ct = default, int threads = 0)
    {
        string pckPath = System.IO.Path.Combine(elementDir, "litmodels.pck");
        string mapsDir = System.IO.Path.Combine(elementDir, "maps");
        if (!File.Exists(pckPath) || !Directory.Exists(mapsDir)) return null;
        try
        {
            // referências de cada mapa (só o que o jogo mostra)
            var refs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.GetDirectories(mapsDir))
            {
                string map = System.IO.Path.GetFileName(dir);
                string ecw = System.IO.Path.Combine(dir, map + ".ecwld");
                if (!File.Exists(ecw) || !File.Exists(System.IO.Path.ChangeExtension(ecw, ".ecbsd"))) continue;
                if (ObjectFormats.ReferencedBmds(ecw, map) is { Count: > 0 } set) refs[map] = set;
            }
            // cache por arquivo
            var cache = new Dictionary<string, IndexedBmd>(StringComparer.OrdinalIgnoreCase);
            string cachePath = CachePath(elementDir);
            try
            {
                if (File.Exists(cachePath) && JsonSerializer.Deserialize<List<IndexedBmd>>(File.ReadAllText(cachePath)) is { } old)
                    foreach (var e in old) cache[e.Path] = e;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { AppLog.Error("índice de construções: cache ilegível (refaz)", ex); }

            var entries = new Dictionary<string, PckEntry>(StringComparer.OrdinalIgnoreCase);
            using (var pck = new PckArchive(pckPath))
                foreach (var e in pck.Entries) if (e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)) entries[e.Path] = e;
            var wanted = refs.Values.SelectMany(s => s).Distinct(StringComparer.OrdinalIgnoreCase).Where(entries.ContainsKey).ToList();
            var todo = wanted.Where(p => !(cache.TryGetValue(p, out var c) && c.CSize == entries[p].CSize)).ToList();   // a forma de um caminho não muda com mover/girar (só o cabeçalho e a colisão): o tamanho basta; regravar o pacote muda posições
            int done = wanted.Count - todo.Count, failed = 0, total = wanted.Count;
            progress?.Invoke(done, total);
            var fresh = new System.Collections.Concurrent.ConcurrentBag<IndexedBmd>();
            var skip = new System.Collections.Concurrent.ConcurrentBag<string>();
            if (todo.Count > 0)
            {
                int n = threads > 0 ? threads : Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
                Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = n, CancellationToken = ct },
                    () => new PckArchive(pckPath),
                    (path, _, reader) =>
                    {
                        var e = entries[path];
                        try
                        {
                            if (Read(path, e.Offset, e.CSize, reader.Read(e)) is { } ib) fresh.Add(ib); else skip.Add(path);
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
                        {
                            Interlocked.Increment(ref failed);
                            AppLog.Info($"índice de construções: {path}: {ex.Message}");
                        }
                        int k = Interlocked.Increment(ref done);
                        if (k % 200 == 0) progress?.Invoke(k, total);
                        return reader;
                    },
                    reader => reader.Dispose());
            }
            foreach (var ib in fresh) cache[ib.Path] = ib;
            foreach (var p in skip) cache[p] = new IndexedBmd(p, entries[p].Offset, entries[p].CSize, 0, [0, 0, 0], [0, 0, 0], false, true);   // não desenhável: lembra para não reler
            // grava o cache (só o que existe hoje)
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
                var keep = cache.Values.Where(c => entries.ContainsKey(c.Path)).ToList();
                string tmp = cachePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(keep));
                File.Move(tmp, cachePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error("índice de construções: gravar o cache", ex); }

            var idx = new BuildingIndex { Files = total, Failed = failed };
            foreach (var (map, set) in refs)
            {
                var shapes = new Dictionary<int, BuildingShape>();
                foreach (var p in set)
                {
                    if (!cache.TryGetValue(p, out var c) || c.CollideOnly || c.Sig == 0) continue;
                    shapes[c.Sig] = shapes.TryGetValue(c.Sig, out var s) ? s with { Count = s.Count + 1 }
                        : new BuildingShape(c.Sig, c.Path, new Vector3(c.Min[0], c.Min[1], c.Min[2]), new Vector3(c.Max[0], c.Max[1], c.Max[2]), c.Solid, 1);
                }
                if (shapes.Count > 0) idx.ByMap[map] = shapes.Values.OrderByDescending(s => s.Count).ThenBy(s => s.Sample, StringComparer.OrdinalIgnoreCase).ToList();
            }
            progress?.Invoke(total, total);
            return idx;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("índice de construções", ex);
            return null;
        }
    }
}
