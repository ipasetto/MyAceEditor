using System.IO.Compression;
using System.Text;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld.WorldEdit;

/// <summary>Um tipo de grama/flor de algum mapa: os 24 B do ECWDFILEGRASSTYPE, o .mox e os mapas que usam.</summary>
public sealed record GrassKind(byte[] Props, string Mox, List<string> Maps)
{
    public WorldScene.GrassType Type => new(Props[0] != 0, BitConverter.ToInt32(Props, 4), BitConverter.ToSingle(Props, 8), BitConverter.ToSingle(Props, 12),
        BitConverter.ToSingle(Props, 16), BitConverter.ToSingle(Props, 20), Mox);

    /// <summary>Número estável (favoritos/recentes): .mox + tamanho.</summary>
    public int Sig
    {
        get
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in Mox.ToLowerInvariant()) h = (h ^ c) * 16777619;
                foreach (byte b in Props) h = (h ^ b) * 16777619;
                return h & 0x7FFFFFFF;
            }
        }
    }

    /// <summary>Tipos de grama de TODOS os mapas (lista do cabeçalho de cada .ecwld, +40/+48). Mapa que não abre fica fora.</summary>
    public static List<GrassKind> All(string elementDir)
    {
        var byKey = new Dictionary<string, GrassKind>(StringComparer.OrdinalIgnoreCase);
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
                int p = (int)BitConverter.ToUInt32(b, 40), n = BitConverter.ToInt32(b, 48);
                for (int i = 0; i < n; i++)
                {
                    if (p + 28 > b.Length) throw new InvalidDataException("tipo de grama fora do arquivo");
                    var props = b[p..(p + 24)]; p += 24;
                    int l = BitConverter.ToInt32(b, p); if (l < 0 || p + 4 + l > b.Length) throw new InvalidDataException("tipo de grama ilegível");
                    string mox = gbk.GetString(b, p + 4, l).TrimEnd((char)0); p += 4 + l;
                    string key = mox + "|" + Convert.ToHexString(props);
                    if (!byKey.TryGetValue(key, out var k)) byKey[key] = k = new GrassKind(props, mox, []);
                    if (!k.Maps.Contains(map, StringComparer.OrdinalIgnoreCase)) k.Maps.Add(map);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { AppLog.Info($"tipos de grama: {map}: {ex.Message}"); }
        }
        return [.. byKey.Values];
    }
}

/// <summary>
/// Rascunho da grama e das flores (pincel da ferramenta Objetos do mundo; desenho em
/// docs/superpowers/specs/2026-11-08-grama-design.md). Mexe direto nos mapas de bits das áreas da cena (o desenho do Mundo 3D
/// mostra na hora) e guarda o antes/depois de cada pincelada para o Ctrl+Z/Ctrl+Y. Área nova (onde o tipo ainda não tem
/// área) = uma por bloco do cenário, células de 0,5 m, como as do jogo. Nada grava até o Salvar (GrassSaver).
/// </summary>
public sealed class GrassDraft(WorldScene scene, IReadOnlyList<(float X0, float X1, float Z0, float Z1)> blocks, Func<float, float, int> pieceAt, Func<float, float, float?> heightAt)
{
    public WorldScene Scene { get; } = scene;
    public int FileAreas { get; } = scene.Grass.Count;          // áreas que vieram do arquivo (as seguintes são novas)
    public int FileTypes { get; } = scene.GrassTypes.Count;     // tipos que vieram do arquivo
    /// <summary>Os 24 B dos tipos acrescentados pelo editor (índice − FileTypes), para gravar na lista do mapa.</summary>
    public List<byte[]> NewTypeProps { get; } = new();
    const float Cell = 0.5f;

    sealed class Stroke
    {
        public readonly Dictionary<WorldScene.GrassArea, byte[]> Before = new();
        public Dictionary<WorldScene.GrassArea, byte[]> After = new();
        public int AreasBefore, AreasAfter;
        public readonly List<WorldScene.GrassArea> Added = new();
    }
    readonly List<Stroke> _undo = new(), _redo = new();
    Stroke? _cur;
    readonly HashSet<WorldScene.GrassArea> _touched = new();   // áreas do arquivo que mudaram alguma vez (para saber o que gravar)

    public bool Dirty => Scene.Grass.Count > FileAreas || _undo.Count > 0 && _touched.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    /// <summary>Áreas do arquivo com bits diferentes do arquivo (para gravar).</summary>
    public IEnumerable<WorldScene.GrassArea> ChangedFileAreas => _touched.Where(a => Scene.Grass.IndexOf(a) is var i && i >= 0 && i < FileAreas);
    public IEnumerable<WorldScene.GrassArea> NewAreas => Scene.Grass.Skip(FileAreas);

    /// <summary>Tipo do editor para um tipo de grama (já existe = o número dele; senão entra na lista da cena).</summary>
    public int TypeFor(GrassKind k)
    {
        for (int i = 0; i < Scene.GrassTypes.Count; i++)
        {
            var t = Scene.GrassTypes[i];
            if (t.Mox.Equals(k.Mox, StringComparison.OrdinalIgnoreCase) && t.Size == k.Type.Size && t.SizeVar == k.Type.SizeVar && t.Sight == k.Type.Sight) return i;
        }
        Scene.GrassTypes.Add(k.Type);
        NewTypeProps.Add(k.Props);
        return Scene.GrassTypes.Count - 1;
    }

    public void BeginStroke() { _cur = new Stroke { AreasBefore = Scene.Grass.Count }; }

    /// <summary>Fim da pincelada: vira um passo de Ctrl+Z (nada mudou = nenhum passo). Devolve as áreas mexidas.</summary>
    public bool EndStroke()
    {
        if (_cur is null) return false;
        var s = _cur; _cur = null;
        s.AreasAfter = Scene.Grass.Count;
        if (s.Before.Count == 0 && s.Added.Count == 0) return false;
        s.After = s.Before.Keys.ToDictionary(a => a, a => (byte[])a.Bits.Clone());
        _undo.Add(s); _redo.Clear();
        return true;
    }

    void Touch(WorldScene.GrassArea a)
    {
        if (_cur is not null && !_cur.Before.ContainsKey(a) && !_cur.Added.Contains(a)) _cur.Before[a] = (byte[])a.Bits.Clone();
        if (Scene.Grass.IndexOf(a) is var i && i >= 0 && i < FileAreas) _touched.Add(a);
    }

    static (float X0, float Z1) Corner(WorldScene.GrassArea g) => (g.X - g.Cols * g.Cell / 2, g.Z + g.Rows * g.Cell / 2);

    static bool Covers(WorldScene.GrassArea g, float x, float z)
    {
        var (x0, z1) = Corner(g);
        return x >= x0 && x < x0 + g.Cols * g.Cell && z <= z1 && z > z1 - g.Rows * g.Cell;
    }

    /// <summary>
    /// Pinta o tipo num círculo (centro x, z; raio em metros): liga as células com a chance da densidade (0..1). Onde o tipo
    /// não tem área, cria uma por bloco. Devolve as áreas mexidas (o desenho refaz só elas).
    /// </summary>
    public HashSet<WorldScene.GrassArea> Paint(float cx, float cz, float radius, int type, float density, int seed)
    {
        var touched = new HashSet<WorldScene.GrassArea>();
        if (type < 0 || type >= Scene.GrassTypes.Count || radius <= 0) return touched;
        // áreas novas nos blocos que o círculo pega e que ainda não têm área desse tipo
        foreach (var bl in blocks)
        {
            if (cx + radius < bl.X0 || cx - radius > bl.X1 || cz + radius < bl.Z0 || cz - radius > bl.Z1) continue;
            float bx = (bl.X0 + bl.X1) / 2, bz = (bl.Z0 + bl.Z1) / 2;
            if (Scene.Grass.Any(g => g.Type == type && Covers(g, bx, bz))) continue;
            int cols = Math.Max(8, (int)MathF.Round((bl.X1 - bl.X0) / Cell) / 8 * 8), rows = Math.Max(8, (int)MathF.Round((bl.Z1 - bl.Z0) / Cell));
            int piece = pieceAt(bx, bz);
            if (piece <= 0) continue;
            var na = new WorldScene.GrassArea(type, bx, heightAt(bx, bz) ?? 0, bz, Cell, cols, rows, new byte[cols / 8 * rows], -1, piece - 1, -1, 0);
            Scene.Grass.Add(na);
            _cur?.Added.Add(na);
        }
        var rnd = new Random(seed);
        foreach (var g in Scene.Grass.Where(g => g.Type == type))
            if (SetCells(g, cx, cz, radius, on: true, density, rnd)) touched.Add(g);
        return touched;
    }

    /// <summary>Borracha: apaga a grama de QUALQUER tipo no círculo.</summary>
    public HashSet<WorldScene.GrassArea> Erase(float cx, float cz, float radius)
    {
        var touched = new HashSet<WorldScene.GrassArea>();
        foreach (var g in Scene.Grass)
            if (SetCells(g, cx, cz, radius, on: false, 1, null)) touched.Add(g);
        return touched;
    }

    bool SetCells(WorldScene.GrassArea g, float cx, float cz, float r, bool on, float density, Random? rnd)
    {
        var (x0, z1) = Corner(g);
        int c0 = Math.Max(0, (int)MathF.Floor((cx - r - x0) / g.Cell)), c1 = Math.Min(g.Cols - 1, (int)MathF.Floor((cx + r - x0) / g.Cell));
        int r0 = Math.Max(0, (int)MathF.Floor((z1 - (cz + r)) / g.Cell)), r1 = Math.Min(g.Rows - 1, (int)MathF.Floor((z1 - (cz - r)) / g.Cell));
        if (c0 > c1 || r0 > r1) return false;
        bool changed = false;
        int stride = g.Cols >> 3;
        for (int row = r0; row <= r1; row++)
            for (int col = c0; col <= c1; col++)
            {
                float x = x0 + (col + 0.5f) * g.Cell, z = z1 - (row + 0.5f) * g.Cell;
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r * r) continue;
                int i = row * stride + (col >> 3); byte bit = (byte)(1 << (7 - (col & 7)));
                bool isOn = (g.Bits[i] & bit) != 0;
                if (on == isOn) continue;
                if (on && rnd is not null && rnd.NextDouble() > density) continue;
                if (!changed) { Touch(g); changed = true; }
                if (on) g.Bits[i] |= bit; else g.Bits[i] &= (byte)~bit;
            }
        return changed;
    }

    /// <summary>Ctrl+Z da grama. Devolve as áreas mexidas (null = nada a desfazer).</summary>
    public HashSet<WorldScene.GrassArea>? Undo()
    {
        if (_undo.Count == 0) return null;
        var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(s);
        var touched = new HashSet<WorldScene.GrassArea>(s.Before.Keys);
        foreach (var (a, bits) in s.Before) bits.CopyTo(a.Bits, 0);
        foreach (var a in s.Added) { Scene.Grass.Remove(a); touched.Add(a); }
        return touched;
    }

    public HashSet<WorldScene.GrassArea>? Redo()
    {
        if (_redo.Count == 0) return null;
        var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(s);
        foreach (var a in s.Added) Scene.Grass.Add(a);
        foreach (var (a, bits) in s.After) bits.CopyTo(a.Bits, 0);
        return [.. s.After.Keys, .. s.Added];
    }

    /// <summary>Quantos tufos ligados nas áreas novas e mudadas (resumo do Salvar).</summary>
    public int Tufts(IEnumerable<WorldScene.GrassArea> areas) => areas.Sum(a => a.Bits.Sum(b => System.Numerics.BitOperations.PopCount(b)));
}

/// <summary>
/// Grava o rascunho da grama no plano do Salvar dos Objetos do mundo (tudo ou nada junto com eles): bits das áreas mudadas e
/// das novas acrescentados no fim do bsdata\N.dat do sub-terreno (as entradas velhas e as que não são de grama ficam como
/// estão; a tabela do fim aponta para as novas), registro novo de área (ECBSDFILEGRASS7, 36 B) no fim do .ecbsd, referência
/// nova (8 B) no bloco que contém a área e os tipos novos na lista do .ecwld.
/// </summary>
public static class GrassSaver
{
    public static void Apply(SavePlan plan, GrassDraft g, DraftSources src)
    {
        var changed = g.ChangedFileAreas.ToList();
        var added = g.NewAreas.Where(a => a.Bits.Any(b => b != 0)).ToList();   // área nova vazia (tudo apagado) não entra
        if (changed.Count == 0 && added.Count == 0) return;
        string mapDir = Path.GetDirectoryName(src.EcwldPath)!;
        string bsDir = Path.Combine(mapDir, "bsdata");
        byte[] ecwB = plan.Files.TryGetValue(src.EcwldPath, out var pe) ? pe : File.ReadAllBytes(src.EcwldPath);
        byte[] bsd = plan.Files.TryGetValue(src.EcbsdPath, out var pb) ? pb : File.ReadAllBytes(src.EcbsdPath);
        var ecw = new EcwldFile(ecwB);
        // bsdata por sub-terreno: entradas novas no fim, antes da tabela
        var files = new Dictionary<int, (List<byte> Body, List<int> Offs)>();
        (List<byte> Body, List<int> Offs) FileOf(int sub)
        {
            if (files.TryGetValue(sub, out var f)) return f;
            string path = Path.Combine(bsDir, $"{sub + 1}.dat");
            byte[] d = plan.Files.TryGetValue(path, out var pd) ? pd : File.Exists(path) ? File.ReadAllBytes(path) : [.. Encoding.ASCII.GetBytes("MOXB"), .. BitConverter.GetBytes(2), .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(0)];
            if (d.Length < 16 || BitConverter.ToUInt32(d, 0) != 0x42584F4D || BitConverter.ToInt32(d, 4) < 2) throw new InvalidDataException(string.Format(Loc.T("grass.bsBad", "bsdata\\{0}.dat em formato desconhecido; a grama não foi salva."), sub + 1));
            if (BitConverter.ToInt32(d, 8) == 0) throw new InvalidDataException(string.Format(Loc.T("grass.bsRaw", "bsdata\\{0}.dat sem compactação (não suportado ainda); a grama não foi salva."), sub + 1));
            int n = BitConverter.ToInt32(d, d.Length - 4);
            int table = d.Length - 4 - 4 * n;
            if (n < 0 || table < 12) throw new InvalidDataException(string.Format(Loc.T("grass.bsBad", "bsdata\\{0}.dat em formato desconhecido; a grama não foi salva."), sub + 1));
            var offs = Enumerable.Range(0, n).Select(i => BitConverter.ToInt32(d, table + 4 * i)).ToList();
            files[sub] = f = ([.. d[..table]], offs);
            return f;
        }
        int Append(int sub, byte[] bits, int idx)
        {
            var (body, offs) = FileOf(sub);
            byte[] z;
            using (var ms = new MemoryStream())
            {
                using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, true)) zs.Write(bits);
                z = ms.ToArray();
            }
            int off = body.Count;
            body.AddRange(BitConverter.GetBytes(z.Length)); body.AddRange(BitConverter.GetBytes(bits.Length)); body.AddRange(z);
            if (idx >= 0 && idx < offs.Count) { offs[idx] = off; return idx; }
            offs.Add(off); return offs.Count - 1;
        }
        foreach (var a in changed) Append(a.Sub, a.Bits, a.Idx);   // mesma entrada, dados novos no fim; o registro do .ecbsd não muda
        // tipos novos que as áreas usam (na ordem), renumerados depois dos do arquivo
        var typeMap = new Dictionary<int, int>();
        var newTypes = new List<(byte[], string)>();
        foreach (var a in added.Where(a => a.Type >= g.FileTypes).OrderBy(a => a.Type))
        {
            if (typeMap.ContainsKey(a.Type)) continue;
            int k = a.Type - g.FileTypes;
            if (k < 0 || k >= g.NewTypeProps.Count) throw new InvalidDataException(Loc.T("grass.noType", "Grama nova sem tipo; nada foi salvo."));
            typeMap[a.Type] = ecw.GrassTypeCount + newTypes.Count;
            newTypes.Add((g.NewTypeProps[k], g.Scene.GrassTypes[a.Type].Mox));
        }
        if (newTypes.Count > 0) ecw.AddGrassTypes(newTypes);
        uint nextId = ecw.MaxId() + 1;
        foreach (var a in added)
        {
            int idx = Append(a.Sub, a.Bits, -1);
            var rec = new byte[36];
            BitConverter.GetBytes(typeMap.TryGetValue(a.Type, out int nt) ? nt : a.Type).CopyTo(rec, 0);
            BitConverter.GetBytes(a.X).CopyTo(rec, 4); BitConverter.GetBytes(a.Y).CopyTo(rec, 8); BitConverter.GetBytes(a.Z).CopyTo(rec, 12);
            BitConverter.GetBytes(a.Cell).CopyTo(rec, 16); BitConverter.GetBytes(a.Cols).CopyTo(rec, 20); BitConverter.GetBytes(a.Rows).CopyTo(rec, 24);
            BitConverter.GetBytes(a.Sub).CopyTo(rec, 28); BitConverter.GetBytes(idx).CopyTo(rec, 32);
            int off = bsd.Length;
            bsd = [.. bsd, .. rec];
            var block = DraftSaver.BlockAt(ecw, new System.Numerics.Vector3(a.X, a.Y, a.Z)) ?? throw new InvalidDataException(Loc.T("obj.noBlock", "Não há bloco do cenário nesse lugar do mapa."));
            var item = new byte[8];
            BitConverter.GetBytes(nextId++).CopyTo(item, 0); BitConverter.GetBytes(off).CopyTo(item, 4);
            block.Items[(int)EcwldFile.Kind.Grass].Add(item);
        }
        foreach (var (sub, (body, offs)) in files)
        {
            var outB = new List<byte>(body);
            foreach (int o in offs) outB.AddRange(BitConverter.GetBytes(o));
            outB.AddRange(BitConverter.GetBytes(offs.Count));
            plan.Files[Path.Combine(bsDir, $"{sub + 1}.dat")] = [.. outB];
        }
        var ecwOut = ecw.Write();
        _ = new EcwldFile(ecwOut);   // relê
        plan.Files[src.EcwldPath] = ecwOut;
        if (added.Count > 0) plan.Files[src.EcbsdPath] = bsd;
        plan.Summary.Add(string.Format(Loc.T("grass.summary", "Grama: {0} área(s) mudada(s), {1} nova(s), {2} tufo(s)"), changed.Count, added.Count, g.Tufts(changed.Concat(added))));
    }
}
