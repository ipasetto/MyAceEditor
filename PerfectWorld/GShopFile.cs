using System.Text;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Loja de cash: gshop.data / gshop1.data (cliente) e gshopsev.data / gshopsev1.data (servidor), PW-KB §S19.
/// Cliente: i32 timestamp | i32 n | n × item | 8 categorias (nome[64] UTF-16 + i32 nSub + nSub × nome[64]).
/// Servidor: i32 timestamp | i32 n | n × item (só os números, mesma ordem).
/// Dois formatos de item (ver <see cref="GShopLayout"/>): 1380/164 B (até ~1.4.x) e 1436/220 B (1.5.x: opções de
/// venda com 36 B e 40 B de números novos no fim, que também vão para o servidor). O formato é descoberto na leitura.
/// Cada item guarda os bytes originais e as propriedades escrevem neles, então salvar sem mudar nada
/// reproduz o arquivo byte a byte.
/// </summary>
public sealed class GShopFile
{
    public const int OptionCount = 4, CategoryCount = 8, NameChars = 64;

    static readonly Encoding Gbk;
    static GShopFile() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Gbk = Encoding.GetEncoding(936); }

    public int Timestamp { get; set; }
    public List<GShopItem> Items { get; } = new();
    public List<GShopCategory> Categories { get; } = new();
    /// <summary>Formato dos itens deste arquivo (itens novos de outro formato são convertidos ao salvar).</summary>
    public GShopLayout Layout { get; private set; } = GShopLayout.V1;

    public static GShopFile ReadClient(byte[] d)
    {
        int n = BitConverter.ToInt32(d, 4);
        // o formato certo é o único em que as 8 categorias terminam exatamente no fim do arquivo
        foreach (var lay in GShopLayout.All)
            if (TryReadClient(d, n, lay) is { } f) return f;
        if (n < 0 || 8L + (long)n * GShopLayout.V1.ClientSize > d.Length) throw new InvalidDataException(UI.Loc.T("gshopf.badCount", "gshop.data: número de itens inválido"));
        throw new InvalidDataException(UI.Loc.T("gshopf.unknownFormat", "gshop.data: formato de item desconhecido (nenhum tamanho conhecido fecha as categorias no fim do arquivo)"));
    }

    static GShopFile? TryReadClient(byte[] d, int n, GShopLayout lay)
    {
        long p = 8 + (long)n * lay.ClientSize;
        if (n < 0 || p > d.Length) return null;
        var cats = new List<GShopCategory>();
        for (int c = 0; c < CategoryCount; c++)
        {
            if (p + 132 > d.Length) return null;
            var cat = new GShopCategory { Name = Str(d, (int)p, NameChars) };
            int ns = BitConverter.ToInt32(d, (int)p + 128); p += 132;
            if (ns < 0 || p + ns * 128L > d.Length) return null;
            for (int s = 0; s < ns; s++, p += 128) cat.Subs.Add(Str(d, (int)p, NameChars));
            cat.Raw = d.AsSpan((int)p - 132 - ns * 128, 132 + ns * 128).ToArray();
            cats.Add(cat);
        }
        if (p != d.Length) return null;
        var f = new GShopFile { Timestamp = BitConverter.ToInt32(d, 0), Layout = lay };
        for (int i = 0; i < n; i++) f.Items.Add(new GShopItem(d.AsSpan(8 + i * lay.ClientSize, lay.ClientSize).ToArray()));
        f.Categories.AddRange(cats);
        return f;
    }

    /// <summary>Junta os números do servidor (gshopsev) aos itens do cliente; devolve as diferenças encontradas.</summary>
    public List<string> AttachServer(byte[] d)
    {
        var diffs = new List<string>();
        int ts = BitConverter.ToInt32(d, 0), n = BitConverter.ToInt32(d, 4), size = Layout.ServerSize;
        if (8L + (long)n * size != d.Length) throw new InvalidDataException(UI.Loc.T("gshopf.serverSize", "gshopsev.data: tamanho não bate com o número de itens"));
        if (ts != Timestamp) diffs.Add($"timestamp diferente (cliente {Timestamp}, servidor {ts})");
        if (n != Items.Count) diffs.Add($"quantidade diferente (cliente {Items.Count}, servidor {n})");
        for (int i = 0; i < Math.Min(n, Items.Count); i++)
        {
            var raw = d.AsSpan(8 + i * size, size).ToArray();
            Items[i].ServerRaw = raw;
            if (!Items[i].ServerMatchesClient) diffs.Add(string.Format(UI.Loc.T("gshopf.serverDiff", "item {0} (loja {1}): números diferentes entre cliente e servidor"), i, Items[i].ShopId));
        }
        return diffs;
    }

    /// <summary>
    /// Itens que o gs recusa (cgame/gs/playermall.cpp): ele lê as opções em ordem e para na primeira com preço ≤ 0;
    /// item sem nenhuma opção válida derruba a loja inteira ("不存在可用的条目索引", erro -31) e o gs não sobe.
    /// </summary>
    public List<string> ServerProblems() =>
        Items.Select((it, i) => (it, i)).Where(x => x.it.GetOption(0).Price <= 0)
            .Select(x => string.Format(UI.Loc.T("gshopf.noPrice", "posição {0}: item {1} \"{2}\" sem preço (a 1ª opção de venda precisa custar pelo menos 0,01)"), x.i + 1, x.it.ItemId, x.it.Name)).ToList();

    public byte[] WriteClient()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Timestamp); w.Write(Items.Count);
        foreach (var it in Items) w.Write(it.In(Layout).ClientRaw);
        foreach (var c in Categories) w.Write(c.ToBytes());
        return ms.ToArray();
    }

    /// <summary>gshopsev: os números de cada item vêm do cliente (os dois lados ficam sempre iguais).</summary>
    public byte[] WriteServer()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Timestamp); w.Write(Items.Count);
        foreach (var it in Items) w.Write(it.In(Layout).BuildServer());
        return ms.ToArray();
    }

    internal static string Str(byte[] d, int at, int chars)
    {
        var s = Encoding.Unicode.GetString(d, at, chars * 2);
        int z = s.IndexOf('\0');
        return z < 0 ? s : s[..z];
    }

    internal static void PutStr(byte[] d, int at, int chars, string v)
    {
        Array.Clear(d, at, chars * 2);
        var b = Encoding.Unicode.GetBytes(v.Length >= chars ? v[..(chars - 1)] : v);
        Buffer.BlockCopy(b, 0, d, at, b.Length);
    }

    internal static string Gbk128(byte[] d, int at)
    {
        int z = Array.IndexOf(d, (byte)0, at, 128);
        return Gbk.GetString(d, at, (z < 0 ? at + 128 : z) - at);
    }

    internal static void PutGbk128(byte[] d, int at, string v)
    {
        Array.Clear(d, at, 128);
        var b = Gbk.GetBytes(v);
        Buffer.BlockCopy(b, 0, d, at, Math.Min(b.Length, 127));
    }
}

public sealed class GShopCategory
{
    public string Name { get; set; } = "";
    public List<string> Subs { get; } = new();
    internal byte[]? Raw;

    internal byte[] ToBytes()
    {
        var b = new byte[132 + Subs.Count * 128];
        // sem mudança: bytes originais (o lixo depois do \0 também é preservado)
        if (Raw is not null && Raw.Length == b.Length && GShopFile.Str(Raw, 0, 64) == Name
            && Enumerable.Range(0, Subs.Count).All(i => GShopFile.Str(Raw, 132 + i * 128, 64) == Subs[i]))
            return Raw;
        GShopFile.PutStr(b, 0, 64, Name);
        BitConverter.TryWriteBytes(b.AsSpan(128), Subs.Count);
        for (int i = 0; i < Subs.Count; i++) GShopFile.PutStr(b, 132 + i * 128, 64, Subs[i]);
        return b;
    }
}

/// <summary>Opção de venda (até 4 por item): preço em centavos de ouro, prazo e regras.</summary>
public readonly record struct GShopOption(int Price, int EndTime, int Duration, int StartTime, int ControlType, int Day, int Status, int Flags)
{
    public bool IsEmpty => Price == 0 && Duration == 0 && EndTime == 0 && StartTime == 0;
}

/// <summary>
/// Formato do registro de item. Comum aos dois: loja, tipo, subtipo (0..12), figura GBK[128] (12), item e quantidade
/// (140, 144), 4 opções de venda (148), descrição UTF-16[512], nome UTF-16[32] e presente/log_price (16 B).
/// V2 (1.5.x) tem opções de 36 B (um int a mais no fim de cada) e 40 B de números no fim do registro; provado contra
/// o gshopsev de 220 B do mesmo servidor (servidor = [0,12) + [item, descrição) + [presente, fim)).
/// </summary>
public sealed record GShopLayout(int ClientSize, int OptionSize, int ServerSize)
{
    public static readonly GShopLayout V1 = new(1380, 32, 164), V2 = new(1436, 36, 220);
    public static readonly GShopLayout[] All = { V1, V2 };

    public int Desc => 148 + GShopFile.OptionCount * OptionSize;
    public int Name => Desc + GShopItem.DescChars * 2;
    public int Gift => Name + GShopItem.NameChars * 2;

    public static GShopLayout Of(int clientSize) => All.FirstOrDefault(l => l.ClientSize == clientSize) ?? throw new ArgumentException(UI.Loc.T("gshopf.badRecord", "registro de loja com tamanho errado"));
}

public sealed class GShopItem
{
    // deslocamentos fixos no registro do cliente; os que dependem do formato vêm de Layout
    const int OShop = 0, OMain = 4, OSub = 8, OSurface = 12, OId = 140, OAmount = 144, OOptions = 148;
    public const int DescChars = 512, NameChars = 32;

    public byte[] ClientRaw { get; }
    public byte[]? ServerRaw { get; internal set; }
    public GShopLayout Layout { get; }

    public GShopItem(byte[] clientRaw) { Layout = GShopLayout.Of(clientRaw.Length); ClientRaw = clientRaw; }

    public static GShopItem New(int shopId) => new(new byte[GShopLayout.V1.ClientSize]) { ShopId = shopId, Amount = 1 };

    public GShopItem Clone(int shopId) => new((byte[])ClientRaw.Clone()) { ShopId = shopId };

    /// <summary>
    /// O mesmo item no formato do arquivo (item novo criado no formato antigo entrando numa loja 1.5.x, por exemplo).
    /// Campos que só existem no formato novo ficam zerados.
    /// </summary>
    internal GShopItem In(GShopLayout to)
    {
        if (to == Layout) return this;
        var b = new byte[to.ClientSize];
        Buffer.BlockCopy(ClientRaw, 0, b, 0, OOptions);
        for (int k = 0; k < GShopFile.OptionCount; k++)
            Buffer.BlockCopy(ClientRaw, OOptions + k * Layout.OptionSize, b, OOptions + k * to.OptionSize, 32);
        Buffer.BlockCopy(ClientRaw, Layout.Desc, b, to.Desc, (DescChars + NameChars) * 2 + 16);   // descrição, nome, presente, log_price
        return new GShopItem(b);
    }

    int I(int o) => BitConverter.ToInt32(ClientRaw, o);
    void I(int o, int v) => BitConverter.TryWriteBytes(ClientRaw.AsSpan(o), v);

    public int ShopId { get => I(OShop); set => I(OShop, value); }
    public int MainType { get => I(OMain); set => I(OMain, value); }
    public int SubType { get => I(OSub); set => I(OSub, value); }
    /// <summary>Figura grande da loja (surfaces\...), GBK.</summary>
    public string Surface { get => GShopFile.Gbk128(ClientRaw, OSurface); set => GShopFile.PutGbk128(ClientRaw, OSurface, value); }
    public int ItemId { get => I(OId); set => I(OId, value); }
    public int Amount { get => I(OAmount); set => I(OAmount, value); }
    public string Description { get => GShopFile.Str(ClientRaw, Layout.Desc, DescChars); set => GShopFile.PutStr(ClientRaw, Layout.Desc, DescChars, value); }
    public string Name { get => GShopFile.Str(ClientRaw, Layout.Name, NameChars); set => GShopFile.PutStr(ClientRaw, Layout.Name, NameChars, value); }
    public int GiftId { get => I(Layout.Gift); set => I(Layout.Gift, value); }
    public int GiftAmount { get => I(Layout.Gift + 4); set => I(Layout.Gift + 4, value); }
    public int GiftDuration { get => I(Layout.Gift + 8); set => I(Layout.Gift + 8, value); }
    public int LogPrice { get => I(Layout.Gift + 12); set => I(Layout.Gift + 12, value); }

    public GShopOption GetOption(int k)
    {
        int o = OOptions + k * Layout.OptionSize;
        return new(I(o), I(o + 4), I(o + 8), I(o + 12), I(o + 16), I(o + 20), I(o + 24), I(o + 28));
    }

    public void SetOption(int k, GShopOption v)
    {
        int o = OOptions + k * Layout.OptionSize;
        I(o, v.Price); I(o + 4, v.EndTime); I(o + 8, v.Duration); I(o + 12, v.StartTime);
        I(o + 16, v.ControlType); I(o + 20, v.Day); I(o + 24, v.Status); I(o + 28, v.Flags);
    }

    /// <summary>Registro do servidor = números do cliente: loja, tipo, subtipo, item, qtd, opções, presente (e o fim novo do V2).</summary>
    internal byte[] BuildServer()
    {
        var b = new byte[Layout.ServerSize];
        int ids = Layout.Desc - OId, tail = Layout.ClientSize - Layout.Gift;
        Buffer.BlockCopy(ClientRaw, OShop, b, 0, 12);              // shop, main, sub
        Buffer.BlockCopy(ClientRaw, OId, b, 12, ids);              // id, amount, opções
        Buffer.BlockCopy(ClientRaw, Layout.Gift, b, 12 + ids, tail);   // presente + log_price (+ números novos do V2)
        return b;
    }

    public bool ServerMatchesClient => ServerRaw is null || BuildServer().AsSpan().SequenceEqual(ServerRaw);
}
