using System.Text;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Extrator de .pck nativo (sem sPCK): abre o pacote (com o .pkx ao lado, se houver), lista e filtra os
/// arquivos, mostra prévia de imagem (.dds/.tga/.png/.bmp/.jpg) e de texto, e extrai selecionados ou tudo
/// para uma pasta, mantendo as subpastas.
/// </summary>
internal sealed class PckView : Panel
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Surface = Color.White, Fg = Color.FromArgb(30, 39, 51),
        Muted = Color.FromArgb(100, 112, 128), Accent = Color.FromArgb(37, 99, 235);

    readonly Func<string?> _elementDir;
    readonly ComboBox _known = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly TextBox _filter = new() { Width = 260, PlaceholderText = "" };
    readonly Label _info = new() { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0) };
    readonly ListView _lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, HideSelection = false, BorderStyle = BorderStyle.None };
    readonly PictureBox _pic = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 44, 52) };
    readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f), Visible = false };
    readonly Button _extractSel = new(), _extractAll = new(), _open = new(), _cancel = new();
    readonly ProgressBar _bar = new() { Dock = DockStyle.Bottom, Height = 6, Visible = false };

    PckArchive? _pck;
    string? _dest;   // pasta de destino; padrão = <pacote>.files ao lado do .pck (como o sPCK)
    readonly Label _destLabel = new() { AutoSize = true, ForeColor = Muted, Padding = new Padding(10, 7, 0, 0), MaximumSize = new Size(420, 0) };
    readonly Button _destChange = new();
    readonly Button _editUi = new();
    InterfaceView? _editor;
    Control[] _listMode = Array.Empty<Control>();

    /// <summary>Editor de janelas aberto (para o Ctrl+Z da janela principal ir para ele).</summary>
    public InterfaceView? Editor => _editor is { Visible: true } ? _editor : null;
    List<PckEntry> _view = new();
    CancellationTokenSource? _work;

    public PckView(Func<string?> elementDir)
    {
        _elementDir = elementDir;
        Dock = DockStyle.Fill; BackColor = Bg; Padding = new Padding(10);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Bg };
        Button B(Button b, Action a, int w = 150) { b.Width = w; b.Height = 28; b.FlatStyle = FlatStyle.Flat; b.BackColor = Surface; b.ForeColor = Fg; b.Click += (_, _) => a(); return b; }
        top.Controls.Add(_known);
        top.Controls.Add(B(_open, OpenOther, 130));
        top.Controls.Add(new Label { Width = 14 });
        top.Controls.Add(_filter);
        top.Controls.Add(B(_extractSel, () => Extract(selectedOnly: true), 190));
        top.Controls.Add(B(_extractAll, () => Extract(selectedOnly: false), 150));
        top.Controls.Add(B(_cancel, () => _work?.Cancel(), 90));
        top.Controls.Add(B(_editUi, OpenEditor, 170));
        _editUi.BackColor = Color.FromArgb(255, 244, 214);
        top.Controls.Add(_destLabel);
        top.Controls.Add(B(_destChange, ChangeDest, 110));
        _cancel.Visible = false;
        _extractSel.BackColor = Accent; _extractSel.ForeColor = Color.White;

        _lv.Columns.Add("", 520); _lv.Columns.Add("", 100, HorizontalAlignment.Right); _lv.Columns.Add("", 100, HorizontalAlignment.Right);
        _lv.RetrieveVirtualItem += (_, e) =>
        {
            // ao trocar de pacote a lista pode pedir uma linha da contagem antiga
            if (e.ItemIndex >= _view.Count) { e.Item = new ListViewItem(new[] { "", "", "" }); return; }
            var x = _view[e.ItemIndex];
            e.Item = new ListViewItem(new[] { x.Path, Human(x.Size), Human(x.CSize) });
        };
        _lv.SelectedIndexChanged += (_, _) => Preview();
        _lv.VirtualItemsSelectionRangeChanged += (_, _) => UpdateButtons();

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760, BackColor = Bg };
        split.Panel1.Controls.Add(_lv);
        split.Panel2.Controls.Add(_pic); split.Panel2.Controls.Add(_text);
        Controls.Add(split); Controls.Add(_bar); Controls.Add(_info); Controls.Add(top);
        _listMode = new Control[] { split, _bar, _info, top };
        _info.Dock = DockStyle.Bottom;

        _known.SelectedIndexChanged += (_, _) => { if (_known.SelectedItem is string f && f.Contains(':')) Open(Path.Combine(_elementDir() ?? "", f.Split(':')[0].Trim())); };
        _filter.TextChanged += (_, _) => ApplyFilter();
        VisibleChanged += (_, _) => { if (Visible) FillKnown(); };
        AppLang.Changed += Texts;
        Texts();
    }

    void Texts()
    {
        _open.Text = Loc.T("pck.open", "Abrir outro .pck…");
        _filter.PlaceholderText = Loc.T("pck.filter", "Filtrar (ex.: .dds, configs\\, skillstr)");
        _extractAll.Text = Loc.T("pck.extractAll", "Extrair tudo");
        _destChange.Text = Loc.T("pck.changeDest", "Mudar pasta…");
        _cancel.Text = Loc.T("dlg.cancel", "Cancelar");
        _lv.Columns[0].Text = Loc.T("pck.colPath", "Arquivo"); _lv.Columns[1].Text = Loc.T("pck.colSize", "Tamanho"); _lv.Columns[2].Text = Loc.T("pck.colPacked", "Compactado");
        UpdateButtons(); UpdateInfo();
    }

    static string Human(long b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : b >= 1024 ? $"{b / 1024.0:0.0} KB" : $"{b} B";

    // Pacotes da pasta element do jogo aberto (configs, surfaces, interfaces...).
    void FillKnown()
    {
        var dir = _elementDir();
        var files = dir is not null && Directory.Exists(dir) ? Directory.GetFiles(dir, "*.pck").Select(Path.GetFileName).OrderBy(x => x).ToList() : new();
        var keep = _known.SelectedItem as string;
        _known.BeginUpdate(); _known.Items.Clear();
        _known.Items.Add(files.Count > 0 ? Loc.T("pck.choose", "Pacotes do jogo aberto…") : Loc.T("pck.noGame", "(abra a pasta do PW para listar os .pck)"));
        foreach (var f in files) _known.Items.Add($"{f} : {Human(new FileInfo(Path.Combine(dir!, f!)).Length)}");
        _known.SelectedIndex = keep is not null && _known.Items.Contains(keep) ? _known.Items.IndexOf(keep) : 0;
        _known.EndUpdate();
    }

    void OpenOther()
    {
        using var dlg = new OpenFileDialog { Filter = "Angelica (*.pck)|*.pck|*.*|*.*", InitialDirectory = _elementDir() ?? "" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
    }

    void Open(string path)
    {
        if (!File.Exists(path)) return;
        Cursor = Cursors.WaitCursor;
        try
        {
            _pck?.Dispose(); _pck = null;
            _view = new(); _lv.VirtualListSize = 0;
            _pck = new PckArchive(path);
            _dest = path + ".files";
            _dialogCount = InterfaceView.CountDialogs(_pck);
            ApplyFilter();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("pck.openFail", "Não consegui abrir o pacote"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; }
    }

    void ApplyFilter()
    {
        var f = _filter.Text.Trim();
        _view = _pck is null ? new() : _pck.Entries.Where(e => f.Length == 0 || e.Path.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _lv.SelectedIndices.Clear();
        _lv.VirtualListSize = _view.Count;
        _lv.Invalidate();
        UpdateButtons(); UpdateInfo();
    }

    void UpdateInfo() => _info.Text = _pck is null
        ? Loc.T("pck.hint", "Escolha um pacote acima. Duplo clique extrai o arquivo; a prévia mostra imagens e textos.")
        : string.Format(Loc.T("pck.info", "{0} · versão {1} · {2:N0} arquivos ({3:N0} no filtro) · {4}"),
            Path.GetFileName(_pck.FilePath), _pck.Version, _pck.Entries.Count, _view.Count, Human(_view.Sum(e => (long)e.Size)));

    // ---------- editor de janelas (pacotes com XML de interface) ----------
    int _dialogCount;

    void OpenEditor()
    {
        if (_pck is null || _dialogCount == 0) return;
        if (_editor is null)
        {
            _editor = new InterfaceView(CloseEditor) { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_editor);
        }
        foreach (var c in _listMode) c.Visible = false;
        _editor.Visible = true; _editor.BringToFront();
        _editor.LoadPack(_pck.FilePath);
    }

    void CloseEditor()
    {
        if (_editor is not null) _editor.Visible = false;
        foreach (var c in _listMode) c.Visible = true;
    }

    /// <summary>
    /// Outra pasta do PW foi aberta: a lista de pacotes passa a ser a dela e o pacote aberto (e o editor de interface)
    /// troca para o arquivo de mesmo nome do jogo novo; se ele não existir lá, o pacote é fechado.
    /// </summary>
    public void GameFolderChanged()
    {
        var dir = _elementDir();
        var name = _pck is null ? null : Path.GetFileName(_pck.FilePath);
        bool editing = _editor is { Visible: true };
        CloseEditor();
        if (name is not null)
        {
            var same = dir is null ? null : Path.Combine(dir, name);
            if (same is not null && File.Exists(same)) Open(same);
            else { _pck?.Dispose(); _pck = null; _dialogCount = 0; ApplyFilter(); }
        }
        FillKnown();
        if (editing && _pck is not null) OpenEditor();
    }

    /// <summary>Diagnóstico (--snapshot ... ui:arquivo.xml): abre o interfaces.pck do jogo no editor e a janela pedida.</summary>
    internal void OpenUiForSnapshot(string file)
    {
        var dir = _elementDir();
        if (dir is null) return;
        Open(Path.Combine(dir, "interfaces.pck"));
        OpenEditor();
        _editor?.OpenForSnapshot(file);
    }

    void ChangeDest()
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("pck.dest", "Pasta de destino (as subpastas do pacote são mantidas)"), SelectedPath = _dest ?? "" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { _dest = dlg.SelectedPath; UpdateButtons(); }
    }

    void UpdateButtons()
    {
        int n = _lv.SelectedIndices.Count;
        _destLabel.Text = _dest is null ? "" : Loc.T("pck.to", "Destino: ") + _dest;
        _destChange.Visible = _pck is not null;
        _editUi.Visible = _dialogCount > 0;
        _editUi.Text = string.Format(Loc.T("pck.editUi", "✎ Editar interfaces ({0})"), _dialogCount);
        _extractSel.Text = string.Format(Loc.T("pck.extractSel", "Extrair selecionados ({0})"), n);
        _extractSel.Enabled = n > 0 && _work is null;
        _extractAll.Enabled = _view.Count > 0 && _work is null;
    }

    void Preview()
    {
        UpdateButtons();
        if (_pck is null || _work is not null || _lv.SelectedIndices.Count != 1) return;   // o pacote não é lido por duas threads
        var e = _view[_lv.SelectedIndices[0]];
        var ext = Path.GetExtension(e.Path).ToLowerInvariant();
        if (e.Size > 16 << 20) return;   // prévia só de arquivos pequenos
        try
        {
            var data = _pck.Read(e);
            Image? img = null;
            if (ext == ".dds" && DdsDecoder.Decode(data) is { } d)
            {
                var bmp = new Bitmap(d.Width, d.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bits = bmp.LockBits(new Rectangle(0, 0, d.Width, d.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(d.Pixels, 0, bits.Scan0, d.Pixels.Length);
                bmp.UnlockBits(bits);
                img = bmp;
            }
            else if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif")
                img = Image.FromStream(new MemoryStream(data));
            _pic.Image?.Dispose(); _pic.Image = img;
            bool text = img is null && ext is ".txt" or ".ini" or ".cfg" or ".xml" or ".stf" or ".htm" or ".conf" or ".sdr" or ".gfx" or ".ecm" or ".smd";
            _text.Visible = text; _pic.Visible = !text;
            if (text) _text.Text = DecodeText(data);
        }
        catch { _pic.Image = null; }
    }

    // Textos do cliente: UTF-16 (com ou sem BOM) ou GBK.
    static string DecodeText(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 4 && b[1] == 0 && b[3] == 0) return Encoding.Unicode.GetString(b);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        return Encoding.GetEncoding(936).GetString(b);
    }

    void Extract(bool selectedOnly)
    {
        if (_pck is null || _work is not null) return;
        var list = selectedOnly ? _lv.SelectedIndices.Cast<int>().Select(i => _view[i]).ToList() : _view.ToList();
        if (list.Count == 0) return;
        if (_dest is null) return;
        var root = Path.GetFullPath(_dest);   // criada se não existir
        // como o sPCK: o caminho inteiro do pacote vai para dentro de <pacote>.files (interfaces.pck.files\interfaces\version01\...)
        var strip = "";
        var pck = _pck;
        _work = new CancellationTokenSource();
        var token = _work.Token;
        _bar.Visible = true; _bar.Value = 0; _bar.Maximum = list.Count; _cancel.Visible = true; UpdateButtons();
        int done = 0, failed = 0;
        Task.Run(() =>
        {
            (done, failed) = ExtractTo(pck, root, list, token, strip: strip, progress: n => { if ((n & 31) == 0 || n == list.Count) BeginInvoke(() => _bar.Value = Math.Min(n, _bar.Maximum)); });
        }).ContinueWith(_ => BeginInvoke(() =>
        {
            _work?.Dispose(); _work = null;
            _bar.Visible = false; _cancel.Visible = false; UpdateButtons();
            var msg = string.Format(Loc.T("pck.done", "{0} de {1} arquivo(s) extraído(s) em {2}"), done - failed, list.Count, root)
                + (failed > 0 ? string.Format(Loc.T("pck.failed", " · {0} falharam"), failed) : "")
                + (token.IsCancellationRequested ? Loc.T("pck.cancelled", " · cancelado") : "");
            _info.Text = msg;
            if (MessageBox.Show(this, msg + "\n\n" + Loc.T("pck.openFolder", "Abrir a pasta?"), "PCK", MessageBoxButtons.YesNo) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root) { UseShellExecute = true });
        }));
    }

    /// <summary>Extrai as entradas para <paramref name="root"/> mantendo as subpastas; nunca grava fora dela.</summary>
    internal static (int Done, int Failed) ExtractTo(PckArchive pck, string root, IReadOnlyList<PckEntry> list, CancellationToken token, Action<int>? progress = null, string strip = "")
    {
        root = Path.GetFullPath(root);
        int done = 0, failed = 0;
        foreach (var e in list)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                // caminho com .. ou absoluto dentro do pacote não sai da pasta escolhida
                var rel = strip.Length > 0 && e.Path.StartsWith(strip, StringComparison.OrdinalIgnoreCase) ? e.Path[strip.Length..] : e.Path;
                var target = Path.GetFullPath(Path.Combine(root, rel.TrimStart('\\', '/')));
                if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) failed++;
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, pck.Read(e));
                }
            }
            catch { failed++; }
            done++;
            progress?.Invoke(done);   // fora do ?.: com progress nulo o ++ não rodaria
        }
        return (done, failed);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { AppLang.Changed -= Texts; _work?.Cancel(); _pck?.Dispose(); _pic.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
