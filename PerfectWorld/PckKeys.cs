using System.Text.Json;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Chaves de um pacote Angelica (.pck/.pkx). São números que o próprio elementclient.exe traz dentro dele
/// (AngelicaFile AFilePackMan.cpp): o jogo só abre o pacote se as chaves do .exe forem as mesmas do arquivo.
/// <list type="bullet">
/// <item>Key1 = AFPCK_MASKDWORD: embaralha a posição da tabela e o tamanho de cada entrada da tabela.</item>
/// <item>Key2 = AFPCK_CHECKMASK: embaralha a conferência do tamanho (gravada como tamanho ^ Key1 ^ Key2).</item>
/// <item>Asig1/Asig2 = AFPCK_GUARDBYTE0/1: marcas do rodapé (+0 e +268 no v2, +272 no v3).</item>
/// <item>Fsig1/Fsig2 = marcas do começo do arquivo (SAFEFILEHEADER tag1 em +0; tag2 em +8 só no v2).</item>
/// </list>
/// Formato: ENGINE-KB §E4 / PW-KB §S3. Pedido da comunidade (10/11): servidores que trocam as chaves para proteger os
/// pacotes e outros jogos da mesma engine.
/// </summary>
public sealed record PckKeys(string Name, int Key1, int Key2, int Asig1, int Asig2, int Fsig1, int Fsig2)
{
    /// <summary>As chaves de fábrica do Perfect World (todos os clientes que temos, 1.3.6 a 1.8.x).</summary>
    public static readonly PckKeys Default = new("Perfect World", unchecked((int)0xA8937462), 0x59374231,
        unchecked((int)0xFDFDFEEE), unchecked((int)0xF00DBEEF), 0x4DCA23EF, 0x56A089B7);

    /// <summary>O que vai gravado ao lado do tamanho de cada entrada da tabela (tamanho ^ isto).</summary>
    public int CheckXor => Key1 ^ Key2;

    /// <summary>As mesmas 6 chaves (o nome não conta).</summary>
    public bool SameKeys(PckKeys o) => Key1 == o.Key1 && Key2 == o.Key2 && Asig1 == o.Asig1 && Asig2 == o.Asig2 && Fsig1 == o.Fsig1 && Fsig2 == o.Fsig2;

    public bool IsDefault => SameKeys(Default);

    /// <summary>As 6 chaves em texto (Copiar chaves / Colar chaves): "KEY1=… KEY2=… ASIG1=… ASIG2=… FSIG1=… FSIG2=…".</summary>
    public string ToText() => $"KEY1={Key1} KEY2={Key2} ASIG1={Asig1} ASIG2={Asig2} FSIG1={Fsig1} FSIG2={Fsig2}";

    /// <summary>Lê as 6 chaves de um texto (nomes KEY1..FSIG2 em qualquer ordem, números com sinal ou 0x…); null = faltou alguma.</summary>
    public static PckKeys? FromText(string name, string text)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(KEY1|KEY2|ASIG1|ASIG2|FSIG1|FSIG2)\s*[=:]\s*(-?0x[0-9a-fA-F]+|-?\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (ParseNumber(m.Groups[2].Value) is int v) map[m.Groups[1].Value] = v;
        string[] need = { "KEY1", "KEY2", "ASIG1", "ASIG2", "FSIG1", "FSIG2" };
        return need.All(map.ContainsKey) ? new PckKeys(name, map["KEY1"], map["KEY2"], map["ASIG1"], map["ASIG2"], map["FSIG1"], map["FSIG2"]) : null;
    }

    /// <summary>Número com sinal ("-1466731422") ou hexadecimal ("0xA8937462"); null = não é número de 32 bits.</summary>
    public static int? ParseNumber(string s)
    {
        s = s.Trim();
        bool neg = s.StartsWith('-'); if (neg) s = s[1..];
        long v;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        { if (!long.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out v) || v > uint.MaxValue) return null; }
        else if (!long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v)) return null;
        if (neg) v = -v;
        return v is >= int.MinValue and <= uint.MaxValue ? unchecked((int)v) : null;
    }
}

/// <summary>
/// Perfis de chaves do usuário (%AppData%\MyAceEditor\pck-keys.json; só no PC dele, nunca no log nem no repositório) e a
/// detecção de qual perfil abre um pacote. O perfil de fábrica vem sempre primeiro e não fica no arquivo.
/// </summary>
public static class PckKeyStore
{
    internal static string? FileOverride;   // só teste
    static string FilePath => FileOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "pck-keys.json");
    static List<PckKeys>? _user;
    static readonly object Gate = new();

    /// <summary>Perfis do usuário (sem o de fábrica).</summary>
    public static IReadOnlyList<PckKeys> User { get { lock (Gate) return _user ??= Load(); } }

    /// <summary>De fábrica primeiro, depois os do usuário.</summary>
    public static IReadOnlyList<PckKeys> All { get { lock (Gate) return [PckKeys.Default, .. (_user ??= Load())]; } }

    static List<PckKeys> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<PckKeys>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("chaves do pck: ler os perfis", ex);   // nunca as chaves em si no log
            return [];
        }
    }

    /// <summary>Grava os perfis do usuário (troca atômica). null = gravou; senão o motivo em palavras simples.</summary>
    public static string? Save(IEnumerable<PckKeys> user)
    {
        var list = user.ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
            lock (Gate) _user = list;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("chaves do pck: gravar os perfis", ex);
            return UI.Loc.T("pckkey.saveFail", "Não consegui gravar os perfis de chaves (pasta sem permissão ou arquivo aberto). Nada mudou.");
        }
    }

    /// <summary>Esquece o que foi lido (teste ou arquivo trocado por fora).</summary>
    internal static void Reload() { lock (Gate) _user = null; }

    // ---------- detecção ----------

    /// <summary>Começo (12 B do .pck principal), rodapé e versão de um pacote, para escolher o perfil.</summary>
    internal readonly record struct Probe(byte[] Head, byte[] Footer, int Version, long Length);

    internal static int GuardAt(int version) => version == 3 ? 272 : 268;

    /// <summary>
    /// Perfil que abre o pacote: 1º um que bate em TODAS as marcas (começo e rodapé); senão um cuja marca do começo bate
    /// (é só isso que o leitor conferia antes — o de fábrica primeiro, então os pacotes de sempre abrem do mesmo jeito).
    /// A posição da tabela tem de cair dentro do arquivo. null = nenhum perfil serve.
    /// </summary>
    internal static PckKeys? Detect(Probe p, IReadOnlyList<PckKeys>? candidates = null)
    {
        var list = candidates ?? All;
        int tag1 = BitConverter.ToInt32(p.Head, 0), tag2 = BitConverter.ToInt32(p.Head, 8);
        int g0 = BitConverter.ToInt32(p.Footer, 0), g1 = BitConverter.ToInt32(p.Footer, GuardAt(p.Version));
        bool TableOk(PckKeys k)
        {
            long off = p.Version == 3 ? BitConverter.ToInt64(p.Footer, 8) ^ k.Key1 : (uint)(BitConverter.ToInt32(p.Footer, 8) ^ k.Key1);
            return off > 0 && off <= p.Length - p.Footer.Length;
        }
        foreach (var k in list)
            if (k.Fsig1 == tag1 && (p.Version == 3 || k.Fsig2 == tag2) && k.Asig1 == g0 && k.Asig2 == g1 && TableOk(k)) return k;
        foreach (var k in list)
            if (k.Fsig1 == tag1 && TableOk(k)) return k;
        return null;
    }

    /// <summary>Lê começo e rodapé do pacote (com as partes .pkx) e escolhe o perfil.</summary>
    public static PckKeys? Detect(string pckPath, IReadOnlyList<PckKeys>? candidates = null) => Detect(ReadProbe(pckPath), candidates);

    internal static Probe ReadProbe(string pckPath)
    {
        pckPath = PckArchive.Locate(pckPath) ?? pckPath;
        var parts = PckArchive.PartPaths(pckPath);
        var last = parts.Count > 0 ? parts[^1] : pckPath;
        byte[] head = new byte[12];
        long length = new FileInfo(pckPath).Length + parts.Sum(x => new FileInfo(x).Length);
        using (var fs = new FileStream(pckPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) fs.ReadExactly(head);
        using var ls = new FileStream(last, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var tail = new byte[8]; ls.Position = ls.Length - 8; ls.ReadExactly(tail);
        int version = BitConverter.ToInt16(tail, 4) == 3 ? 3 : 2;
        int foot = version == 3 ? 288 : 280;
        var footer = new byte[foot]; ls.Position = ls.Length - foot; ls.ReadExactly(footer);
        return new Probe(head, footer, version, length);
    }
}

/// <summary>
/// Converte um pacote para outro perfil de chaves (usuário 10/11: "assinar os meus pacotes com outra chave"). Só mudam
/// as marcas do começo e do rodapé, a posição da tabela e os dois números ao lado de cada entrada da tabela; os arquivos
/// dentro do pacote ficam iguais (por isso é rápido mesmo com um .pkx de vários GB). Trabalha numa cópia, relê com o
/// perfil novo, confere todas as entradas e só então troca (o original vira backup). O jogo só abre o resultado se o
/// elementclient.exe tiver as mesmas chaves.
/// </summary>
public static class PckKeyConvert
{
    /// <summary>
    /// Converte <paramref name="pckPath"/> para <paramref name="to"/>. Devolve o caminho do backup da parte trocada.
    /// Exige que o pacote bata em todas as marcas de um perfil conhecido (senão não dá para saber a chave com certeza).
    /// </summary>
    public static string Convert(string pckPath, PckKeys to, Action<double>? progress = null)
    {
        pckPath = PckArchive.Locate(pckPath) ?? pckPath;
        var probe = PckKeyStore.ReadProbe(pckPath);
        var from = StrictMatch(probe) ?? throw new InvalidDataException(UI.Loc.T("pckkey.notStrict", "As marcas deste pacote não batem por inteiro com nenhum perfil cadastrado: não dá para converter com segurança."));
        if (from.SameKeys(to)) throw new InvalidOperationException(UI.Loc.T("pckkey.sameKeys", "O pacote já usa essas chaves."));
        var parts = PckArchive.PartPaths(pckPath);
        var target = parts.Count > 0 ? parts[^1] : pckPath;
        long start = parts.Count > 0 ? new FileInfo(pckPath).Length + parts.Take(parts.Count - 1).Sum(x => new FileInfo(x).Length) : 0;
        int v = probe.Version, footSize = v == 3 ? 288 : 280;
        List<string> before;
        using (var a = new PckArchive(pckPath, keys: from)) before = a.Entries.Select(e => $"{e.Path}|{e.Offset}|{e.Size}|{e.CSize}").ToList();

        var tmp = target + ".chaves.tmp";
        Copy(target, tmp, progress);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long len = fs.Length;
                var footer = new byte[footSize]; fs.Position = len - footSize; fs.ReadExactly(footer);
                long table = (v == 3 ? BitConverter.ToInt64(footer, 8) ^ from.Key1 : (uint)(BitConverter.ToInt32(footer, 8) ^ from.Key1)) - start;
                int count = BitConverter.ToInt32(footer, v == 3 ? 280 : 272);
                if (table < 0 || table > len - footSize) throw new InvalidDataException(UI.Loc.T("pckkey.tableOut", "A tabela do pacote não está onde as chaves dizem; nada mudou."));
                // tabela: [tamanho ^ Key1][tamanho ^ Key1 ^ Key2][entrada]; só os dois números mudam
                long pos = table;
                var two = new byte[8];
                for (int i = 0; i < count; i++)
                {
                    fs.Position = pos; fs.ReadExactly(two);
                    int size = BitConverter.ToInt32(two, 0) ^ from.Key1;
                    if (size <= 0 || pos + 8 + size > len - footSize) throw new InvalidDataException(UI.Loc.T("pckkey.tableBad", "Uma entrada da tabela não confere com as chaves; nada mudou."));
                    BitConverter.TryWriteBytes(two.AsSpan(0), size ^ to.Key1);
                    BitConverter.TryWriteBytes(two.AsSpan(4), size ^ to.CheckXor);
                    fs.Position = pos; fs.Write(two);
                    pos += 8 + size;
                }
                // rodapé: marcas e posição da tabela
                BitConverter.TryWriteBytes(footer.AsSpan(0), to.Asig1);
                BitConverter.TryWriteBytes(footer.AsSpan(PckKeyStore.GuardAt(v)), to.Asig2);
                if (v == 3) BitConverter.TryWriteBytes(footer.AsSpan(8), (table + start) ^ to.Key1);
                else BitConverter.TryWriteBytes(footer.AsSpan(8), (int)(uint)(table + start) ^ to.Key1);
                fs.Position = len - footSize; fs.Write(footer);
                if (parts.Count == 0) WriteHead(fs, v, to);   // pacote único: o começo está nesta cópia
                fs.Flush(true);
            }
            progress?.Invoke(0.95);

            var stamp = target + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var bak = stamp + ".bak";
            for (int n = 2; File.Exists(bak); n++) bak = $"{stamp}-{n}.bak";
            byte[]? oldHead = null;
            if (parts.Count == 0) Check(tmp, null, to, before);   // pacote único: confere antes de trocar
            try
            {
                if (parts.Count > 0) oldHead = PatchHead(pckPath, v, to);   // começo do .pck principal, no lugar (12 B)
                File.Replace(tmp, target, bak);
            }
            catch (IOException ex)
            {
                if (oldHead is not null) RestoreHead(pckPath, oldHead);
                throw new IOException(string.Format(UI.Loc.T("pck.replaceLocked", "Não consegui trocar o {0}: está aberto em outro programa (o jogo?). Feche-o e tente de novo."), Path.GetFileName(target)) + "\n\n" + ex.Message, ex);
            }
            if (parts.Count > 0)
            {
                try { Check(pckPath, null, to, before); }
                catch { File.Copy(bak, target, true); RestoreHead(pckPath, oldHead!); throw; }
            }
            progress?.Invoke(1);
            return BackupStore.Adopt(bak, target);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    /// <summary>Perfil cadastrado que bate em todas as marcas (null = nenhum).</summary>
    internal static PckKeys? StrictMatch(PckKeyStore.Probe p)
    {
        int tag1 = BitConverter.ToInt32(p.Head, 0), tag2 = BitConverter.ToInt32(p.Head, 8);
        int g0 = BitConverter.ToInt32(p.Footer, 0), g1 = BitConverter.ToInt32(p.Footer, PckKeyStore.GuardAt(p.Version));
        return PckKeyStore.All.FirstOrDefault(k => k.Fsig1 == tag1 && (p.Version == 3 || k.Fsig2 == tag2) && k.Asig1 == g0 && k.Asig2 == g1);
    }

    /// <summary>Releitura com o perfil novo: as mesmas entradas, nas mesmas posições e tamanhos.</summary>
    static void Check(string pck, string? _, PckKeys keys, List<string> before)
    {
        using var a = new PckArchive(pck, keys: keys);
        var after = a.Entries.Select(e => $"{e.Path}|{e.Offset}|{e.Size}|{e.CSize}").ToList();
        if (!after.SequenceEqual(before)) throw new InvalidDataException(UI.Loc.T("pckkey.recheck", "A releitura com as chaves novas não confere; nada mudou."));
    }

    static void WriteHead(FileStream fs, int v, PckKeys to)
    {
        fs.Position = 0; fs.Write(BitConverter.GetBytes(to.Fsig1));
        if (v != 3) { fs.Position = 8; fs.Write(BitConverter.GetBytes(to.Fsig2)); }
    }

    static byte[] PatchHead(string pck, int v, PckKeys to)
    {
        using var fs = new FileStream(pck, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var old = new byte[12]; fs.ReadExactly(old);
        WriteHead(fs, v, to); fs.Flush(true);
        return old;
    }

    static void RestoreHead(string pck, byte[] old)
    {
        using var fs = new FileStream(pck, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = 0; fs.Write(old); fs.Flush(true);
    }

    static void Copy(string from, string to, Action<double>? progress)
    {
        using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        using var dst = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        long total = Math.Max(1, src.Length), done = 0;
        var buf = new byte[4 << 20]; int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); done += n; progress?.Invoke(0.9 * done / total); }
    }
}
