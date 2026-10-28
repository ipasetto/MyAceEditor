using MyAceEditor.UI;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Correção automática do par cliente × servidor (issue #26, PW-KB §S7/§S17): o que o login exige igual
/// (versão do elements/tasks, timestamp e itens da loja) é refeito no servidor a partir do CLIENTE, que é o que
/// os jogadores têm. Cada arquivo trocado é gravado de forma atômica, com backup do original.
/// </summary>
public static class PairFixer
{
    public sealed record Fix(string Title, string Detail, bool Recommended, Action Apply)
    {
        /// <summary>Troca de programas do servidor local: quem aplica pergunta antes backup e banco novo.</summary>
        public LocalServer.ProgramSwap? Swap { get; init; }
    }

    /// <param name="localServer">servidor local (aba Servidor); com ele, programas de outra versão instalados no Linux também são corrigidos</param>
    public static List<Fix> Plan(string clientElement, string serverHome, Func<LocalServer>? localServer = null)
    {
        clientElement = VersionCheck.ClientElement(clientElement);
        serverHome = VersionCheck.ServerHome(serverHome);
        string cData = Path.Combine(clientElement, "data"), sCfg = Path.Combine(serverHome, "gamed", "config");
        var fixes = new List<Fix>();
        // servidor local com programas de outra versão que a do cliente (instalado com outro pacote): o glinkd recusa o
        // login ("versão baixa"). Troca para o pacote da versão do cliente — para cima ou para baixo — e inicia.
        // Backup e banco novo são perguntados antes (Fix.Swap); depois os dados do cliente vão junto.
        if (localServer is not null && Path.GetDirectoryName(serverHome.TrimEnd('\\', '/')) is { } serverFiles
            && LocalServer.SwapFor(clientElement, serverFiles, out _) is { } swap)
            fixes.Add(new(string.Format(Loc.T(swap.Downgrade ? "fix.programsDownTitle" : "fix.programsUpTitle",
                    swap.Downgrade ? "Servidor local: voltar os programas para a versão do cliente (v{0} → v{1})" : "Servidor local: atualizar os programas para a versão do cliente (v{0} → v{1})"), swap.From, swap.To),
                string.Format(Loc.T("fix.programsDetail", "Os programas instalados são de outra versão e o login é recusado com \"versão do cliente baixa\". Instala os de {0}, envia os dados do cliente e inicia. Antes, pergunta se guarda a instalação atual."), swap.Package),
                true, () =>
                {
                    var r = localServer().SendAll(swap.Package, clientElement, null, swap);
                    if (r.Code != 0) throw new InvalidOperationException(r.Output.Trim().Split('\n').LastOrDefault() ?? "");
                }) { Swap = swap });
        // cliente: janelas que o jogo procura e o interfaces.pck não carrega (o jogo cai ao usá-las)
        if (ClientDialogs.Find(clientElement) is { Count: > 0 } dlgs)
            fixes.Add(new(string.Format(Loc.T("fix.dialogsTitle", "interfaces.pck: carregar {0} janela(s) que o jogo procura"), dlgs.Count),
                string.Join(", ", dlgs.Select(d => d.Dialog)) + Loc.T("fix.dialogsDetail", ". Estão no pacote, mas fora da lista (.dcf); sem elas o jogo cai (ex.: fechar o inventário pelo X). Backup do .pck."),
                true, () => ClientDialogs.Fix(clientElement, dlgs)));
        if (!Directory.Exists(cData) || !Directory.Exists(sCfg)) return fixes;

        // elements.data e tasks.data: mesmo arquivo nos dois lados; copia o do cliente se a versão (ou o conteúdo) for diferente
        foreach (var (name, why) in new[]
        {
            ("elements.data", Loc.T("fix.whyElements", "a versão entra na verificação de login")),
            ("tasks.data", Loc.T("fix.whyTasks", "versão e quests precisam ser as mesmas do cliente")),
        })
        {
            var c = Path.Combine(cData, name); var s = Path.Combine(sCfg, name);
            if (!File.Exists(c)) continue;
            // conteúdo inteiro: mudar HP/drops de um monstro não muda versão nem tamanho (antes só cabeçalho + tamanho)
            if (File.Exists(s) && SameContent(c, s)) continue;
            fixes.Add(new(string.Format(Loc.T("fix.copyTitle", "{0}: copiar o do cliente para o servidor"), name),
                File.Exists(s) ? string.Format(Loc.T("fix.copyDiff", "Diferentes ({0}). O do servidor fica salvo como backup."), why) : Loc.T("fix.notOnServer", "Não existe no servidor."),
                true, () => AtomicFileSave.Write(s, File.ReadAllBytes(c))));
        }

        // cópias por mapa (gamed/config/aNN/elements.data, tasks.data): sobra de outra versão; se o gs.conf de um mapa as
        // usar, a instância cai. Ficam iguais às do cliente (o original vira backup).
        var stale = new List<(string Copy, string Main)>();
        foreach (var name in new[] { "elements.data", "tasks.data" })
        {
            var c = Path.Combine(cData, name);
            if (!File.Exists(c)) continue;
            foreach (var copy in Directory.EnumerateFiles(sCfg, name, SearchOption.AllDirectories))
                if (!string.Equals(Path.GetDirectoryName(copy)?.TrimEnd('\\', '/'), Path.GetFullPath(sCfg).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) && !SameContent(c, copy))
                    stale.Add((copy, c));
        }
        if (stale.Count > 0)
            fixes.Add(new(string.Format(Loc.T("fix.mapCopiesTitle", "Cópias por mapa: igualar {0} arquivo(s) aos do cliente"), stale.Count),
                string.Join(", ", stale.Select(x => Path.GetRelativePath(sCfg, x.Copy))) + Loc.T("fix.mapCopiesDetail", ". São de outra versão; um mapa que as use cai ao abrir. Os atuais ficam como backup."),
                true, () => { foreach (var (copy, main) in stale) AtomicFileSave.Write(copy, File.ReadAllBytes(main)); }));

        // loja: o servidor (gshopsev) é refeito a partir do gshop do cliente, com o mesmo timestamp
        foreach (var (cn, sn) in new[] { ("gshop.data", "gshopsev.data"), ("gshop1.data", "gshopsev1.data") })
        {
            var c = Path.Combine(cData, cn); var s = Path.Combine(sCfg, sn);
            if (!File.Exists(c)) continue;
            GShopFile shop;
            try { shop = GShopFile.ReadClient(File.ReadAllBytes(c)); } catch { continue; }
            var rebuilt = shop.WriteServer();
            if (File.Exists(s) && File.ReadAllBytes(s).AsSpan().SequenceEqual(rebuilt)) continue;
            fixes.Add(new(string.Format(Loc.T("fix.shopTitle", "{0}: refazer a partir do {1} do cliente"), sn, cn),
                File.Exists(s) ? string.Format(Loc.T("fix.shopDiff", "Timestamp ou itens diferentes do cliente ({0} itens). O atual fica como backup."), shop.Items.Count)
                               : string.Format(Loc.T("fix.shopMissing", "Não existe no servidor ({0} itens)."), shop.Items.Count),
                true, () => AtomicFileSave.Write(s, rebuilt)));
        }

        // IA dos monstros: opcional (o servidor pode ter uma versão própria de propósito)
        {
            var c = Path.Combine(cData, "aipolicy.data"); var s = Path.Combine(sCfg, "aipolicy.data");
            if (File.Exists(c) && File.Exists(s) && !File.ReadAllBytes(c).AsSpan().SequenceEqual(File.ReadAllBytes(s)))
                fixes.Add(new(string.Format(Loc.T("fix.copyTitle", "{0}: copiar o do cliente para o servidor"), "aipolicy.data"), Loc.T("fix.aiDiff", "Diferentes. Só marque se o servidor deve usar a mesma IA do cliente."),
                    false, () => AtomicFileSave.Write(s, File.ReadAllBytes(c))));
        }
        return fixes;
    }

    static bool SameContent(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using var fa = File.OpenRead(a); using var fb = File.OpenRead(b);
            var ba = new byte[1 << 20]; var bb = new byte[1 << 20];
            while (true)
            {
                int na = fa.ReadAtLeast(ba, ba.Length, false), nb = fb.ReadAtLeast(bb, bb.Length, false);
                if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                if (na == 0) return true;
            }
        }
        catch { return false; }
    }
}
