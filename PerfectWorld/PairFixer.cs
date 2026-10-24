namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Correção automática do par cliente × servidor (issue #26, PW-KB §S7/§S17): o que o login exige igual
/// (versão do elements/tasks, timestamp e itens da loja) é refeito no servidor a partir do CLIENTE, que é o que
/// os jogadores têm. Cada arquivo trocado é gravado de forma atômica, com backup do original.
/// </summary>
public static class PairFixer
{
    public sealed record Fix(string Title, string Detail, bool Recommended, Action Apply);

    public static List<Fix> Plan(string clientElement, string serverHome)
    {
        clientElement = VersionCheck.ClientElement(clientElement);
        serverHome = VersionCheck.ServerHome(serverHome);
        string cData = Path.Combine(clientElement, "data"), sCfg = Path.Combine(serverHome, "gamed", "config");
        var fixes = new List<Fix>();
        if (!Directory.Exists(cData) || !Directory.Exists(sCfg)) return fixes;

        // elements.data e tasks.data: mesmo arquivo nos dois lados; copia o do cliente se a versão (ou o conteúdo) for diferente
        foreach (var (name, why) in new[]
        {
            ("elements.data", "a versão entra na verificação de login"),
            ("tasks.data", "versão e quests precisam ser as mesmas do cliente"),
        })
        {
            var c = Path.Combine(cData, name); var s = Path.Combine(sCfg, name);
            if (!File.Exists(c)) continue;
            if (File.Exists(s) && SameHeader(c, s, name == "tasks.data" ? 12 : 4) && new FileInfo(c).Length == new FileInfo(s).Length) continue;
            fixes.Add(new($"{name}: copiar o do cliente para o servidor",
                File.Exists(s) ? $"Diferentes ({why}). O do servidor fica salvo como backup." : "Não existe no servidor.",
                true, () => AtomicFileSave.Write(s, File.ReadAllBytes(c))));
        }

        // loja: o servidor (gshopsev) é refeito a partir do gshop do cliente, com o mesmo timestamp
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
        {
            var c = Path.Combine(cData, cn); var s = Path.Combine(sCfg, sn);
            if (!File.Exists(c)) continue;
            GShopFile shop;
            try { shop = GShopFile.ReadClient(File.ReadAllBytes(c)); } catch { continue; }
            var rebuilt = shop.WriteServer();
            if (File.Exists(s) && File.ReadAllBytes(s).AsSpan().SequenceEqual(rebuilt)) continue;
            fixes.Add(new($"{sn}: refazer a partir do {cn} do cliente",
                File.Exists(s) ? $"Timestamp ou itens diferentes do cliente ({shop.Items.Count} itens). O atual fica como backup." : $"Não existe no servidor ({shop.Items.Count} itens).",
                true, () => AtomicFileSave.Write(s, rebuilt)));
        }

        // IA dos monstros: opcional (o servidor pode ter uma versão própria de propósito)
        {
            var c = Path.Combine(cData, "aipolicy.data"); var s = Path.Combine(sCfg, "aipolicy.data");
            if (File.Exists(c) && File.Exists(s) && !File.ReadAllBytes(c).AsSpan().SequenceEqual(File.ReadAllBytes(s)))
                fixes.Add(new("aipolicy.data: copiar o do cliente para o servidor", "Diferentes. Só marque se o servidor deve usar a mesma IA do cliente.",
                    false, () => AtomicFileSave.Write(s, File.ReadAllBytes(c))));
        }
        return fixes;
    }

    static bool SameHeader(string a, string b, int n)
    {
        try
        {
            using var fa = File.OpenRead(a); using var fb = File.OpenRead(b);
            var ba = new byte[n]; var bb = new byte[n];
            return fa.Read(ba, 0, n) == n && fb.Read(bb, 0, n) == n && ba.AsSpan().SequenceEqual(bb);
        }
        catch { return false; }
    }
}
