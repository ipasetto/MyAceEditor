namespace MyAceEditor.PerfectWorld;

/// <summary>
/// .ecwld para EDITAR (o WorldScene só lê o que o Mundo 3D desenha). Guarda o arquivo como cabeçalho + tabela de blocos +
/// blocos item a item (bytes originais de cada árvore, referência, efeito, modelo EC e som, na ordem do CECSceneBlock,
/// EC_SceneBlock.cpp / EC_WorldFile.h; PW-KB §S17) + o resto (tipos de árvore e grama). Write() monta de novo e, sem
/// mudança, é byte a byte igual ao original (--ecwld-check). Editar = mudar/clonar/apagar itens; contagens, offsets dos
/// blocos e dos tipos de árvore/grama são refeitos na gravação.
/// </summary>
public sealed class EcwldFile
{
    /// <summary>Tipos de item do bloco, na ordem do arquivo.</summary>
    public enum Kind { Tree, Water, Ornament, Box, Grass, Effect, EcModel, Critter, Bezier, Sound }
    // posição do contador de cada tipo no cabeçalho do bloco (0 árvores, 1 água, 2 grama, 3 ornamentos, 4 áreas, 5 efeitos, 6 modelos, 7 bichos, 8 curvas, 9 sons)
    static readonly int[] CountSlot = { 0, 1, 3, 4, 2, 5, 6, 7, 8, 9 };

    public sealed class Block
    {
        public byte[] RowCol = new byte[4];
        public List<byte[]>[] Items = Enumerable.Range(0, 10).Select(_ => new List<byte[]>()).ToArray();
        internal int OrigOffset;
    }

    public int Version { get; private set; }
    /// <summary>Largura e comprimento do mundo e lado do bloco, em metros (cabeçalho ECWDFILEHEADER +8, +12, +16).</summary>
    public float Width => BitConverter.ToSingle(_head, 8);
    public float Length => BitConverter.ToSingle(_head, 12);
    public float BlockSize => BitConverter.ToSingle(_head, 16);
    public List<Block> Blocks { get; } = new();

    /// <summary>Quantos tipos de árvore o mapa tem (cabeçalho +44).</summary>
    public int TreeTypeCount => BitConverter.ToInt32(_head, 44);

    /// <summary>
    /// Acrescenta tipos de árvore (.spt + mapa composto das folhas, caminhos do trees.pck) à lista do mapa: a lista inteira
    /// (velha + nova) vai para o fim do arquivo e o cabeçalho (+36 offset, +44 quantidade) aponta para ela; o resto não
    /// mexe. O cliente lê a lista pelo offset (CECScene::LoadTreeTypeData faz Seek) e não tem limite de tipos (08/11).
    /// Textos em GBK sem 0 no fim, como os do arquivo.
    /// </summary>
    public void AddTreeTypes(IReadOnlyList<(string Spt, string Comp)> types)
    {
        if (types.Count == 0) return;
        int count = TreeTypeCount;
        var table = new List<byte>();
        if (count > 0)
        {
            int p = (int)BitConverter.ToUInt32(_head, 36) - _tailStart;
            if (p < 0 || p > _tail.Length) throw new InvalidDataException(".ecwld: lista de tipos de árvore fora do lugar esperado");
            int start = p;
            for (int i = 0; i < count * 2; i++) { int l = BitConverter.ToInt32(_tail, p); if (l < 0 || p + 4 + l > _tail.Length) throw new InvalidDataException(".ecwld: tipo de árvore ilegível"); p += 4 + l; }
            table.AddRange(_tail[start..p]);
        }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var gbk = System.Text.Encoding.GetEncoding(936);
        foreach (var (spt, comp) in types)
            foreach (var str in new[] { spt, comp })
            {
                var b = gbk.GetBytes(str);
                table.AddRange(BitConverter.GetBytes(b.Length)); table.AddRange(b);
            }
        uint newOff = (uint)(_tailStart + _tail.Length);
        _tail = [.. _tail, .. table];
        BitConverter.GetBytes(newOff).CopyTo(_head, 36);
        BitConverter.GetBytes(count + types.Count).CopyTo(_head, 44);
    }
    byte[] _head = [];        // 116 B do cabeçalho
    int[] _table = [];        // índice do bloco (em Blocks) por entrada da tabela; -1 = offset 0
    byte[] _gap = [], _tail = [];  // entre a tabela e o 1º bloco; depois do último bloco (tipos de árvore e grama)
    int _tailStart;
    int _counts;

    public EcwldFile(byte[] b)
    {
        try { Parse(b); }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { throw new InvalidDataException(".ecwld: formato não entendido (" + ex.Message + ")", ex); }
    }

    void Parse(byte[] b)
    {
        if (b.Length < 116 || BitConverter.ToUInt32(b, 0) != (('E' << 24) | ('C' << 16) | ('W' << 8) | 'D')) throw new InvalidDataException("não é um .ecwld");
        Version = BitConverter.ToInt32(b, 4);
        if (Version < 7) throw new InvalidDataException($".ecwld versão {Version}: edição só a partir da 7");
        _head = b[..116];
        _counts = Version < 9 ? 9 : 10;
        int nBlock = BitConverter.ToInt32(b, 24);
        int tableEnd = 116 + 4 * nBlock;
        var offs = Enumerable.Range(0, nBlock).Select(i => (int)BitConverter.ToUInt32(b, 116 + 4 * i)).ToArray();
        var starts = offs.Where(o => o > 0).Distinct().OrderBy(o => o).ToList();
        var index = new Dictionary<int, int>();
        int p = starts.Count > 0 ? starts[0] : tableEnd;
        if (p < tableEnd) throw new InvalidDataException(".ecwld: bloco dentro da tabela");
        _gap = b[tableEnd..p];
        foreach (var s in starts)
        {
            if (s != p) throw new InvalidDataException($".ecwld: bloco em {s}, esperado em {p} (formato não entendido; sem edição)");
            index[s] = Blocks.Count;
            Blocks.Add(ReadBlock(b, ref p, s));
        }
        _tailStart = p;
        _tail = b[p..];
        _table = offs.Select(o => o > 0 ? index[o] : -1).ToArray();
        foreach (var off in new[] { 36, 40 })   // tipos de árvore e de grama têm que estar no resto
        {
            uint v = BitConverter.ToUInt32(b, off);
            if (v != 0 && v < _tailStart) throw new InvalidDataException(".ecwld: tipos de árvore/grama antes do fim dos blocos");
        }
    }

    Block ReadBlock(byte[] b, ref int p, int start)
    {
        var bl = new Block { OrigOffset = start, RowCol = b[p..(p + 4)] };
        int C(int slot) => BitConverter.ToInt32(b, start + 4 + 4 * slot);
        p += 4 + 4 * _counts;
        int treeSize = Version >= 5 ? 16 : 12, fxSize = Version < 8 ? 48 : Version < 12 ? 52 : 60, sndSize = Version < 10 ? 28 : 32,
            ecmSize = Version >= 17 ? 44 : 40;   // ECWDFILEECMODEL; v17 + escala
        int Str(int at) => 4 + BitConverter.ToInt32(b, at);
        for (int k = 0; k < 10; k++)
        {
            var kind = (Kind)k;
            int n = CountSlot[k] < _counts ? C(CountSlot[k]) : 0;
            if (n < 0 || n > 1_000_000) throw new InvalidDataException($".ecwld: bloco em {start} com {n} itens de {kind}");
            for (int i = 0; i < n; i++)
            {
                int s = p;
                p += kind switch
                {
                    Kind.Tree => treeSize,
                    Kind.Effect => fxSize + Str(p + fxSize),
                    Kind.EcModel => ecmSize + Str(p + ecmSize) is var a ? a + Str(p + a) : 0,
                    Kind.Sound => sndSize + Str(p + sndSize),
                    _ => 8,
                };
                if (p > b.Length) throw new InvalidDataException(".ecwld: item passa do fim");
                bl.Items[k].Add(b[s..p]);
            }
        }
        return bl;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var head = (byte[])_head.Clone();
        int tableEnd = 116 + 4 * _table.Length;
        var blockOff = new int[Blocks.Count];
        int p = tableEnd + _gap.Length;
        var bodies = new List<byte[]>();
        for (int i = 0; i < Blocks.Count; i++)
        {
            var bl = Blocks[i];
            using var bs = new MemoryStream();
            bs.Write(bl.RowCol);
            var counts = new int[_counts];
            for (int k = 0; k < 10; k++) if (CountSlot[k] < _counts) counts[CountSlot[k]] = bl.Items[k].Count;
            foreach (var c in counts) bs.Write(BitConverter.GetBytes(c));
            for (int k = 0; k < 10; k++) foreach (var it in bl.Items[k]) bs.Write(it);
            blockOff[i] = p; p += (int)bs.Length;
            bodies.Add(bs.ToArray());
        }
        // tipos de árvore/grama andam junto com o fim dos blocos
        int delta = p - _tailStart;
        foreach (var off in new[] { 36, 40 })
        {
            uint v = BitConverter.ToUInt32(head, off);
            if (v != 0) BitConverter.GetBytes((uint)(v + delta)).CopyTo(head, off);
        }
        ms.Write(head);
        foreach (var t in _table) ms.Write(BitConverter.GetBytes(t < 0 ? 0u : (uint)blockOff[t]));
        ms.Write(_gap);
        foreach (var body in bodies) ms.Write(body);
        ms.Write(_tail);
        return ms.ToArray();
    }

    // ---------- edição por ID (efeitos, modelos EC e sons têm ID de exportação; o mesmo objeto pode estar em vários blocos) ----------

    /// <summary>Posição (x, y, z) dentro do item: árvore +4 (v5+), efeito +8, modelo EC e som +4.</summary>
    static int PosAt(Kind k) => k switch { Kind.Tree => 4, Kind.Effect => 8, _ => 4 };

    public static uint IdOf(byte[] item) => BitConverter.ToUInt32(item, 0);

    public IEnumerable<(Block Block, int Index)> Find(Kind k, uint id)
    {
        foreach (var bl in Blocks)
            for (int i = 0; i < bl.Items[(int)k].Count; i++)
                if (IdOf(bl.Items[(int)k][i]) == id) yield return (bl, i);
    }

    /// <summary>Move o objeto (todas as cópias com o mesmo ID). false se não achou.</summary>
    public bool Move(Kind k, uint id, float x, float y, float z)
    {
        bool any = false;
        foreach (var (bl, i) in Find(k, id).ToList())
        {
            var it = bl.Items[(int)k][i];
            int a = PosAt(k);
            BitConverter.GetBytes(x).CopyTo(it, a); BitConverter.GetBytes(y).CopyTo(it, a + 4); BitConverter.GetBytes(z).CopyTo(it, a + 8);
            any = true;
        }
        return any;
    }

    /// <summary>Apaga o objeto de todos os blocos. Quantas cópias saíram.</summary>
    public int Delete(Kind k, uint id)
    {
        int n = 0;
        foreach (var bl in Blocks) n += bl.Items[(int)k].RemoveAll(it => IdOf(it) == id);
        return n;
    }

    /// <summary>Maior ID de exportação do arquivo (efeitos, modelos EC, sons e referências ao .ecbsd), para o clone ter ID novo.</summary>
    public uint MaxId() => Blocks.SelectMany(bl => new[] { Kind.Water, Kind.Ornament, Kind.Box, Kind.Grass, Kind.Effect, Kind.EcModel, Kind.Critter, Kind.Bezier, Kind.Sound }
        .SelectMany(k => bl.Items[(int)k])).Select(IdOf).DefaultIfEmpty(0u).Max();

    /// <summary>Clona o objeto com ID novo, deslocado (dx, dz), nos mesmos blocos do original. Devolve o ID novo (0 se não achou).</summary>
    public uint Clone(Kind k, uint id, float dx, float dz)
    {
        var hits = Find(k, id).ToList();
        if (hits.Count == 0) return 0;
        uint nid = MaxId() + 1;
        foreach (var (bl, i) in hits)
        {
            var it = (byte[])bl.Items[(int)k][i].Clone();
            BitConverter.GetBytes(nid).CopyTo(it, 0);
            int a = PosAt(k);
            BitConverter.GetBytes(BitConverter.ToSingle(it, a) + dx).CopyTo(it, a);
            BitConverter.GetBytes(BitConverter.ToSingle(it, a + 8) + dz).CopyTo(it, a + 8);
            bl.Items[(int)k].Add(it);
        }
        return nid;
    }
}
