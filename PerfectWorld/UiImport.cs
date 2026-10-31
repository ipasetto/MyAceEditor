using System.Text;
using System.Text.RegularExpressions;
using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Leva arquivos de interface para dentro dos pacotes do cliente (interfaces.pck, surfaces.pck, configs.pck...), vindos de
/// uma pasta editada fora (exportada pelo Extrator) ou de outro cliente ("trocar a interface"). Cada arquivo é conferido
/// antes contra o que está no pacote: só é erro o que piora em relação ao arquivo atual (o jogo já tem arquivos fora do
/// padrão e eles funcionam). Regras (PW-KB §S20b/§S20c):
///   .xml — UTF-16 com BOM igual ao atual (UTF-8 é convertido), tags fechando, mesmo nome de janela, nenhum controle do
///          atual pode sumir (o código do jogo pega cada um pelo nome e cai), sem nome repetido; imagem que não existe
///          no surfaces.pck é aviso.
///   .dcf — GBK; toda janela citada tem que existir; tirar janela que o executável procura é erro.
///   tabelas de texto (.stf .htf .txt .ini) — mesma codificação do atual (UTF-8 é convertido); tabela que deixa de ser lida é erro.
///   imagens — têm que abrir; tamanho diferente do atual é aviso.
/// Gravação: PckWriter (backup do pacote inteiro + troca atômica), um pacote por vez.
/// </summary>
public static class UiImport
{
    public enum Level { Ok, Warn, Error }

    public sealed class Item
    {
        public string Package = "";      // caminho do .pck de destino
        public string Path = "";         // caminho dentro do pacote
        public byte[] Data = Array.Empty<byte>();
        public bool IsNew;
        public Level Level;
        public string Why = "";
        public bool Selected;
        internal List<string> MissingImages = new();
    }

    static UiImport() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static readonly string[] Junk = { "thumbs.db", "desktop.ini", ".ds_store" };

    // ---------------------------------------------------------------- origens
    /// <summary>
    /// Arquivos de uma pasta que diferem do pacote. Aceita a pasta do Extrator (&lt;pasta&gt;\interfaces\...) ou a própria
    /// pasta de dentro (…\interfaces).
    /// </summary>
    public static List<Item> FromFolder(string pckPath, string folder, Action<string>? step = null)
    {
        var raw = new List<(string Path, byte[] Data)>();
        using (var pck = new PckArchive(pckPath))
        {
            var top = TopFolder(pck);
            bool folderIsTop = top is not null && System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')).Equals(top, StringComparison.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (Junk.Contains(System.IO.Path.GetFileName(f).ToLowerInvariant())) continue;
                var rel = System.IO.Path.GetRelativePath(folder, f);
                if (folderIsTop) rel = top + "\\" + rel;
                else if (top is not null && !rel.StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    if (pck.Find(top + "\\" + rel) is not null) rel = top + "\\" + rel;
                    else if (pck.Find(rel) is null) continue;   // fora do pacote: ignora
                }
                var data = File.ReadAllBytes(f);
                if (pck.Find(rel) is { } e && pck.Read(e).AsSpan().SequenceEqual(data)) continue;
                raw.Add((rel, data));
            }
        }
        step?.Invoke("");
        return Check(pckPath, raw);
    }

    /// <summary>
    /// "Trocar a interface": arquivos do pacote de mesmo nome de outro cliente que diferem deste, mais as imagens que as
    /// janelas novas usam e que o surfaces.pck daqui não tem (vindas do surfaces.pck de lá).
    /// </summary>
    public static List<Item> FromClient(string pckPath, string otherPck)
    {
        var raw = new List<(string Path, byte[] Data)>();
        using (var mine = new PckArchive(pckPath))
        using (var other = new PckArchive(otherPck))
            foreach (var e in other.Entries)
            {
                var data = other.Read(e);
                if (mine.Find(e.Path) is { } m && mine.Read(m).AsSpan().SequenceEqual(data)) continue;
                raw.Add((e.Path, data));
            }
        var items = Check(pckPath, raw, fromGame: true);
        // imagens que faltam (só para pacotes com janelas)
        var mySurf = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pckPath)!, "surfaces.pck");
        var otherSurf = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(otherPck)!, "surfaces.pck");
        if (!items.Any(i => IsXml(i.Path)) || !File.Exists(mySurf) || !File.Exists(otherSurf) || SamePath(mySurf, pckPath)) return items;
        using var ms = new PckArchive(mySurf);
        using var os = new PckArchive(otherSurf);
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // imagem → 1ª janela que pede
        foreach (var it in items.Where(i => IsXml(i.Path)))
            foreach (var img in Images(Text(it.Data)))
                if (ms.Find(SurfacePath(img)) is null && os.Find(SurfacePath(img)) is not null) wanted.TryAdd(SurfacePath(img), it.Path);
        foreach (var (img, by) in wanted)
        {
            var data = os.Read(os.Find(img)!);
            var (lv, why) = CheckImage(img, data, null, fromGame: true);
            items.Add(new Item
            {
                Package = mySurf, Path = img, Data = data, IsNew = true, Level = lv, Selected = lv != Level.Error,
                Why = lv == Level.Ok ? string.Format(Loc.T("uim.neededBy", "imagem nova, usada por {0}"), System.IO.Path.GetFileName(by)) : why,
            });
        }
        // a janela deixa de reclamar da imagem que vem junto
        foreach (var it in items.Where(i => i.Level == Level.Warn && i.MissingImages.Count > 0))
            if (it.MissingImages.All(i => wanted.ContainsKey(SurfacePath(i))))
            { it.Level = Level.Ok; it.Why = Loc.T("uim.withImgs", "alterado (as imagens que faltam vêm junto)"); it.Selected = true; }
        return items;
    }

    // ---------------------------------------------------------------- gravação
    /// <summary>Grava os itens marcados, pacote por pacote. Devolve uma linha por pacote (quantos arquivos e o backup).</summary>
    public static List<string> Apply(IEnumerable<Item> items)
    {
        var log = new List<string>();
        foreach (var g in items.Where(i => i.Selected && i.Level != Level.Error).GroupBy(i => i.Package, StringComparer.OrdinalIgnoreCase))
        {
            var files = g.ToDictionary(i => i.Path, i => i.Data, StringComparer.OrdinalIgnoreCase);
            var bak = PckWriter.Write(g.Key, files);
            log.Add(string.Format(Loc.T("uim.written", "{0}: {1} arquivo(s) gravados (backup {2})"), System.IO.Path.GetFileName(g.Key), files.Count, System.IO.Path.GetFileName(bak)));
        }
        return log;
    }

    // ---------------------------------------------------------------- conferência
    sealed class Ctx
    {
        public PckArchive Pck = null!;
        public PckArchive? Surfaces;
        public HashSet<string> Incoming = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExeWindows = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExeNames = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Missing = new();   // imagens que faltam no último XML conferido
        public bool FromGame;                  // arquivos de outro cliente (já rodam lá): formato trocado vira aviso
        public bool Exists(string path) => Incoming.Contains(path) || Pck.Find(path) is not null;
    }

    static List<Item> Check(string pckPath, List<(string Path, byte[] Data)> raw, bool fromGame = false)
    {
        var dir = System.IO.Path.GetDirectoryName(pckPath)!;
        var surf = System.IO.Path.Combine(dir, "surfaces.pck");
        using var pck = new PckArchive(pckPath);
        using var surfaces = File.Exists(surf) && !SamePath(surf, pckPath) ? new PckArchive(surf) : null;
        var ctx = new Ctx { Pck = pck, Surfaces = SamePath(surf, pckPath) ? pck : surfaces, ExeWindows = ClientDialogs.ExeWindows(dir), ExeNames = ClientDialogs.ExeNames(dir), FromGame = fromGame };
        foreach (var r in raw) ctx.Incoming.Add(r.Path);
        var list = new List<Item>();
        foreach (var (path, data0) in raw.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            var old = pck.Find(path) is { } e ? pck.Read(e) : null;
            var data = data0;
            (Level, string) res;
            ctx.Missing = new();
            try { res = CheckOne(path, ref data, old, ctx); }
            catch (Exception ex) { res = (Level.Error, ex.Message); }
            list.Add(new Item { Package = pckPath, Path = path, Data = data, IsNew = old is null, Level = res.Item1, Why = res.Item2, Selected = res.Item1 != Level.Error, MissingImages = ctx.Missing });
        }
        return list;
    }

    static bool IsXml(string p) => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    static readonly string[] ImageExt = { ".dds", ".tga", ".bmp", ".jpg", ".jpeg", ".png" };
    static readonly string[] TextExt = { ".stf", ".htf", ".txt", ".ini", ".gcf" };

    static (Level, string) CheckOne(string path, ref byte[] data, byte[]? old, Ctx ctx)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ImageExt.Contains(ext)) return CheckImage(path, data, old, ctx.FromGame);
        if (ext == ".dcf") return CheckDcf(data, old, ctx);
        string? conv = null;
        if (IsXml(path) || TextExt.Contains(ext)) conv = FixEncoding(ref data, old, xmlDefault: IsXml(path));
        if (conv == "bad") return (Level.Error, Loc.T("uim.encoding", "codificação diferente da do jogo (salve como Unicode / UTF-16 LE, igual ao arquivo original)"));
        var (lv, why) = IsXml(path) ? CheckXml(data, old, ctx)
            : ext is ".stf" or ".txt" && old is not null && StringTable.Parse(old) is { } ot ? CheckTable(data, ot)
            : (Level.Ok, old is null ? Loc.T("uim.new", "arquivo novo") : Loc.T("uim.changed", "alterado"));
        if (conv is not null && lv == Level.Ok) return (Level.Warn, conv);
        return (lv, conv is null ? why : why + " · " + conv);
    }

    // UTF-16 LE como o original; UTF-8 (o que editores costumam salvar) vira UTF-16. Devolve aviso, "bad" ou null.
    static string? FixEncoding(ref byte[] data, byte[]? old, bool xmlDefault)
    {
        bool oldBom = old is { Length: >= 2 } && old[0] == 0xFF && old[1] == 0xFE;
        bool oldU16 = oldBom || old is { Length: >= 2 } && old[1] == 0 || old is null && xmlDefault;
        if (!oldU16) return null;   // arquivo do jogo em GBK/ANSI: fica como veio
        bool bom = data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE;
        bool u16 = bom || data.Length >= 2 && data[1] == 0;
        bool wantBom = oldBom || old is null;
        if (u16)
        {
            if (bom == wantBom) return null;
            data = bom ? data[2..] : new byte[] { 0xFF, 0xFE }.Concat(data).ToArray();
            return Loc.T("uim.bomFixed", "marca BOM ajustada como no original");
        }
        string text;
        try { text = new UTF8Encoding(false, true).GetString(data); } catch { return "bad"; }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var body = Encoding.Unicode.GetBytes(text);
        data = wantBom ? new byte[] { 0xFF, 0xFE }.Concat(body).ToArray() : body;
        return Loc.T("uim.fromUtf8", "convertido de UTF-8 para UTF-16 (como o jogo lê)");
    }

    static string Text(byte[] b) =>
        b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE ? Encoding.Unicode.GetString(b, 2, b.Length - 2 & ~1)
        : b.Length >= 2 && b[1] == 0 ? Encoding.Unicode.GetString(b, 0, b.Length & ~1) : Encoding.GetEncoding(936).GetString(b);

    static readonly Regex Tag = new(@"<(/?)([A-Za-z_][\w]*)((?:[^>""]|""[^""]*"")*?)(/?)>", RegexOptions.Compiled);
    static readonly Regex CtlName = new(@"<(?!DIALOG\b)[A-Z]+\s+Name=""([^""]*)""", RegexOptions.Compiled);
    static readonly Regex DlgName = new(@"<DIALOG\b(?:[^>""]|""[^""]*"")*?\bName=""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex AutoName = new(@"^Control_\d+$", RegexOptions.Compiled);
    static readonly Regex FileRef =new(@"\bFileName=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>Primeira tag que não fecha (linha), ou null.</summary>
    static string? Unbalanced(string t)
    {
        var stack = new Stack<(string Name, int At)>();
        foreach (Match m in Tag.Matches(t))
        {
            if (m.Groups[4].Value == "/") continue;                       // <X ... />
            var name = m.Groups[2].Value;
            if (m.Groups[1].Value == "") { stack.Push((name, m.Index)); continue; }
            if (stack.Count == 0) return string.Format(Loc.T("uim.tagLine", "</{0}> sem abertura na linha {1}"), name, Line(t, m.Index));
            if (!stack.Peek().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return string.Format(Loc.T("uim.tagMismatch", "<{0}> da linha {1} não fecha (achei </{2}> na linha {3})"), stack.Peek().Name, Line(t, stack.Peek().At), name, Line(t, m.Index));
            stack.Pop();
        }
        return stack.Count > 0 ? string.Format(Loc.T("uim.tagOpen", "<{0}> sem fechamento (linha {1})"), stack.Peek().Name, Line(t, stack.Peek().At)) : null;
    }

    static int Line(string t, int at) => 1 + t.AsSpan(0, at).Count('\n');

    static IEnumerable<string> Images(string xml) => FileRef.Matches(xml).Select(m => m.Groups[1].Value).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    static string SurfacePath(string img)
    {
        var rel = img.Replace('/', '\\').TrimStart('\\');
        return rel.StartsWith("surfaces\\", StringComparison.OrdinalIgnoreCase) ? rel : "surfaces\\" + rel;
    }

    static (Level, string) CheckXml(byte[] data, byte[]? old, Ctx ctx)
    {
        var t = Text(data);
        var o = old is null ? null : Text(old);
        if (Unbalanced(t) is { } bad && (o is null || Unbalanced(o) is null)) return (Level.Error, Loc.T("uim.badXml", "XML mal formado: ") + bad);
        if (!new UiDocument(data).ToBytes().AsSpan().SequenceEqual(data)) return (Level.Error, Loc.T("uim.notUtf16", "não é texto UTF-16"));
        var notes = new List<string>();
        var oldDlg = o is null ? null : DlgName.Match(o) is { Success: true } od ? od.Groups[1].Value : null;
        var newDlg = DlgName.Match(t) is { Success: true } nd ? nd.Groups[1].Value : null;
        if (oldDlg is not null && !string.Equals(oldDlg, newDlg, StringComparison.Ordinal))
            return (Level.Error, string.Format(Loc.T("uim.dlgName", "nome da janela mudou ({0} → {1}): o jogo procura pelo nome"), oldDlg, newDlg ?? "?"));
        var names = CtlName.Matches(t).Select(m => m.Groups[1].Value).ToList();
        var dup = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var oldDup = o is null ? new HashSet<string>() : CtlName.Matches(o).Select(m => m.Groups[1].Value).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dup.RemoveAll(oldDup.Contains);
        // o 1.7.x roda com nome repetido (backshop.xml): aviso, não erro
        if (dup.Count > 0) notes.Add(Loc.T("uim.dupNames", "nome de controle repetido: ") + string.Join(", ", dup.Take(5)));
        if (o is not null)
        {
            var have = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var gone = CtlName.Matches(o).Select(m => m.Groups[1].Value).Where(n => !have.Contains(n)).Distinct().ToList();
            var msg = string.Format(Loc.T("uim.ctlGone", "{0} controle(s) somem ({1}): o jogo procura cada um pelo nome e pode fechar; esconda com x = -2000 em vez de apagar"),
                gone.Count, string.Join(", ", gone.Take(5)) + (gone.Count > 5 ? "…" : ""));
            // Control_123456 = nome automático do editor da Angelica; só é seguro tirar se o executável não o cita (alguns cita)
            if (gone.Count > 0 && gone.All(n => AutoName.IsMatch(n) && !ctx.ExeNames.Contains(n))) notes.Add(msg + " " + Loc.T("uim.autoNames", "(só nomes automáticos Control_n que o executável não cita)"));
            else if (gone.Count > 0) return (Level.Error, msg);
        }
        if (ctx.Surfaces is { } s)
        {
            var missing = Images(t).Where(i => s.Find(SurfacePath(i)) is null && !ctx.Incoming.Contains(SurfacePath(i))).ToList();
            var oldMissing = o is null ? new HashSet<string>() : Images(o).Where(i => s.Find(SurfacePath(i)) is null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            missing.RemoveAll(oldMissing.Contains);
            ctx.Missing = missing;
            if (missing.Count > 0) notes.Insert(0, Loc.T("uim.missingImgs", "imagens que não existem no surfaces.pck: ") + string.Join(", ", missing.Take(4)) + (missing.Count > 4 ? $" (+{missing.Count - 4})" : ""));
        }
        if (notes.Count > 0) return (Level.Warn, string.Join(" · ", notes));
        return (Level.Ok, old is null ? Loc.T("uim.new", "arquivo novo") : Loc.T("uim.changed", "alterado"));
    }

    static (Level, string) CheckTable(byte[] data, StringTable old)
    {
        if (StringTable.Parse(data) is not { } t) return (Level.Error, Loc.T("uim.tableBad", "a tabela de textos não é mais lida (aspas ou formato \"índice<TAB>\"texto\"\")"));
        if (t.Entries.Count != old.Entries.Count)
            return (Level.Warn, string.Format(Loc.T("uim.tableCount", "{0} textos (antes {1})"), t.Entries.Count, old.Entries.Count));
        return (Level.Ok, Loc.T("uim.changed", "alterado"));
    }

    static (Level, string) CheckDcf(byte[] data, byte[]? old, Ctx ctx)
    {
        static List<string> Files(byte[] b) => Encoding.GetEncoding(936).GetString(b).Split('\n')
            .Select(l => l.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 0 && p[0].EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0]).ToList();
        if (data.Length >= 2 && (data[1] == 0 || data[0] == 0xFF && data[1] == 0xFE)) return (Level.Error, Loc.T("uim.dcfEnc", ".dcf tem que ser texto GBK/ANSI, não Unicode"));
        var now = Files(data);
        var before = old is null ? new List<string>() : Files(old);
        var missing = now.Where(f => !ctx.Exists(@"interfaces\" + f)).Except(before.Where(f => !ctx.Exists(@"interfaces\" + f)), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) return (Level.Error, Loc.T("uim.dcfMissing", "janelas que não existem no pacote: ") + string.Join(", ", missing.Take(4)));
        var removed = before.Except(now, StringComparer.OrdinalIgnoreCase).ToList();
        var needed = removed.Where(f => ctx.Pck.Find(@"interfaces\" + f) is { } e && ClientDialogs.DialogNameOf(ctx.Pck.Read(e)) is { } n && ctx.ExeWindows.Contains(n)).ToList();
        if (needed.Count > 0) return (Level.Error, Loc.T("uim.dcfNeeded", "tira do .dcf janelas que o executável usa (o jogo cai): ") + string.Join(", ", needed.Take(4)));
        if (removed.Count > 0) return (Level.Warn, string.Format(Loc.T("uim.dcfRemoved", "{0} janela(s) saem do .dcf: {1}"), removed.Count, string.Join(", ", removed.Take(4))));
        return (Level.Ok, string.Format(Loc.T("uim.dcfOk", "{0} janelas"), now.Count));
    }

    /// <summary>Formato pelo conteúdo (assinatura), ou null se não reconhece (TGA não tem assinatura).</summary>
    public static string? Sniff(byte[] d) =>
        d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? ".jpg"
        : d.Length >= 8 && d[0] == 0x89 && d[1] == (byte)'P' && d[2] == (byte)'N' && d[3] == (byte)'G' ? ".png"
        : d.Length >= 2 && d[0] == (byte)'B' && d[1] == (byte)'M' ? ".bmp"
        : d.Length >= 4 && d[0] == (byte)'D' && d[1] == (byte)'D' && d[2] == (byte)'S' && d[3] == (byte)' ' ? ".dds"
        : d.Length >= 6 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F' ? ".gif"
        : null;

    static (Level, string) CheckImage(string path, byte[] data, byte[]? old, bool fromGame = false)
    {
        var ext = System.IO.Path.GetExtension(path);
        // conteúdo de um formato com nome de outro (ex.: PNG salvo como .jpg): o jogo escolhe o leitor pela extensão [I]
        var real = Sniff(data);
        var want = ext.ToLowerInvariant() == ".jpeg" ? ".jpg" : ext.ToLowerInvariant();
        bool sameAsOld = old is not null && Sniff(old) == real;
        if ((real is not null && real != want || real is null && want != ".tga") && !sameAsOld)
            return (fromGame ? Level.Warn : Level.Error, string.Format(Loc.T("uim.imgFormat", "o conteúdo é {0}, mas o nome termina em {1}: converta para {1} (as telas de carregamento convertem sozinhas)"),
                real?.TrimStart('.').ToUpperInvariant() ?? "?", ext));
        using var img = GameTextures.Decode(data, ext);
        if (img is null) return (Level.Error, Loc.T("uim.imgBad", "a imagem não abre (formato diferente da extensão?)"));
        if (old is not null && GameTextures.Decode(old, ext) is { } o)
            using (o)
                if (o.Size != img.Size) return (Level.Warn, string.Format(Loc.T("uim.imgSize", "tamanho {0}×{1} (antes {2}×{3})"), img.Width, img.Height, o.Width, o.Height));
        return (Level.Ok, $"{img.Width}×{img.Height}");
    }

    static string? TopFolder(PckArchive pck) =>
        pck.Entries.Select(e => e.Path.Split('\\')[0]).GroupBy(s => s, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

    static bool SamePath(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
