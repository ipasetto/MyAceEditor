using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Ações de arquivos de interface usadas pelo editor de interface e pelo Extrator PCK: exportar um pacote para uma pasta,
/// importar de volta com conferência (UiImport), trocar com o pacote de outro cliente e as telas de carregamento.
/// Devolvem true quando algum pacote foi regravado (quem chama reabre o que tinha aberto).
/// </summary>
internal static class UiTools
{
    static string Title => Loc.T("uit.title", "Arquivos de interface");

    /// <summary>Exporta o pacote inteiro (caminhos do pacote mantidos) para editar fora.</summary>
    public static void Export(IWin32Window owner, string pckPath)
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("uit.exportTo", "Pasta para exportar (os caminhos do pacote são mantidos; depois use \"Importar pasta…\")"), SelectedPath = pckPath + ".files" };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        Cursor.Current = Cursors.WaitCursor;
        using var pck = new PckArchive(pckPath);
        var (done, failed) = PckView.ExtractTo(pck, dlg.SelectedPath, pck.Entries.ToList(), CancellationToken.None);
        Cursor.Current = Cursors.Default;
        if (MessageBox.Show(owner, string.Format(Loc.T("uit.exported", "{0} arquivos exportados ({1} falhas) para\n{2}\n\nEdite (XML em UTF-16, imagens no mesmo tamanho) e use \"Importar pasta…\": tudo é conferido antes de gravar.\n\nAbrir a pasta?"),
                done - failed, failed, dlg.SelectedPath), Title, MessageBoxButtons.YesNo) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.SelectedPath) { UseShellExecute = true });
    }

    /// <summary>Importa uma pasta editada fora; mostra a revisão e grava os marcados.</summary>
    public static bool Import(IWin32Window owner, string pckPath, string? folder = null)
    {
        if (folder is null)
        {
            using var dlg = new FolderBrowserDialog { Description = Loc.T("uit.importFrom", "Pasta com os arquivos editados (a exportada, ou a pasta .files)"), SelectedPath = Directory.Exists(pckPath + ".files") ? pckPath + ".files" : "" };
            if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
            folder = dlg.SelectedPath;
        }
        if (!Directory.Exists(folder)) { MessageBox.Show(owner, Loc.T("ui.packNone", "A pasta .files não tem nada diferente do pacote."), Title); return false; }
        List<UiImport.Item> items;
        Cursor.Current = Cursors.WaitCursor;
        try { items = UiImport.FromFolder(pckPath, folder); }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        finally { Cursor.Current = Cursors.Default; }
        if (items.Count == 0) { MessageBox.Show(owner, string.Format(Loc.T("uit.same", "Nada diferente do {0} nessa pasta."), Path.GetFileName(pckPath)), Title); return false; }
        return Review(owner, Loc.T("uit.importTitle", "Importar pasta"),
            string.Format(Loc.T("uit.importIntro", "Arquivos de {0} diferentes do {1}. Erros não podem ser gravados; avisos podem (confira). Backup do pacote antes de gravar."), folder, Path.GetFileName(pckPath)), items);
    }

    /// <summary>Troca (tudo ou parte) pelo pacote de mesmo nome de outro cliente, com as imagens que as janelas novas usam.</summary>
    public static bool Swap(IWin32Window owner, string pckPath)
    {
        var name = Path.GetFileName(pckPath);
        using var dlg = new OpenFileDialog { Title = string.Format(Loc.T("uit.swapPick", "{0} do outro cliente"), name), Filter = $"{name}|{name}|*.pck|*.pck" };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(pckPath), StringComparison.OrdinalIgnoreCase)) return false;
        List<UiImport.Item> items;
        Cursor.Current = Cursors.WaitCursor;
        try { items = UiImport.FromClient(pckPath, dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        finally { Cursor.Current = Cursors.Default; }
        if (items.Count == 0) { MessageBox.Show(owner, Loc.T("uit.swapSame", "Os dois pacotes são iguais."), Title); return false; }
        var (same, why) = ClientTranslator.SameProgram(Path.GetDirectoryName(pckPath)!, Path.GetDirectoryName(dlg.FileName)!);
        return Review(owner, Loc.T("uit.swapTitle", "Trocar interface com outro cliente"),
            string.Format(Loc.T("uit.swapIntro", "Arquivos de {0} diferentes deste cliente ({1}). Para trocar só algumas janelas, desmarque o resto ou filtre e use \"Só os filtrados\". Janela que perderia controles é bloqueada (o jogo cairia)."),
                dlg.FileName, same ? why : Loc.T("ct.notRecommended", "NÃO recomendado: ") + why), items);
    }

    static bool Review(IWin32Window owner, string title, string intro, List<UiImport.Item> items)
    {
        using var rev = new UiImportDialog(title, intro, items);
        if (rev.ShowDialog(owner) != DialogResult.OK || rev.Result.Count == 0) return false;
        MessageBox.Show(owner, string.Join("\n", rev.Result) + "\n\n" + Loc.T("uit.restart", "Feche e abra o jogo para ver a mudança."), title);
        return true;
    }

    /// <summary>Telas de carregamento do cliente da pasta element.</summary>
    public static bool LoadingScreens(IWin32Window owner, string? elementDir)
    {
        if (elementDir is null || !File.Exists(Path.Combine(elementDir, "surfaces.pck")))
        { MessageBox.Show(owner, Loc.T("uit.noSurfaces", "surfaces.pck não encontrado na pasta do jogo."), Title); return false; }
        using var d = new LoadingScreensDialog(elementDir);
        d.ShowDialog(owner);
        return true;
    }
}
