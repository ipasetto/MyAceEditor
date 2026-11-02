using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Telas de carregamento do cliente (LoadingScreens): miniaturas por conjunto, trocar uma tela por outra imagem (ajustada ao
/// tamanho de cada arquivo), acrescentar uma tela nova, exportar todas. Nada vai para o jogo até "Gravar no surfaces.pck"
/// (PckWriter: backup + troca atômica).
/// </summary>
internal sealed class LoadingScreensDialog : Form
{
    static readonly Color Muted = Color.FromArgb(100, 112, 128), Accent = Color.FromArgb(37, 99, 235);
    const int ThumbW = 192, ThumbH = 120;

    readonly string _elementDir, _surfPath;
    readonly ListView _lv = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(40, 44, 52), ForeColor = Color.White };
    readonly ImageList _thumbs = new() { ImageSize = new Size(ThumbW, ThumbH), ColorDepth = ColorDepth.Depth24Bit };
    readonly PictureBox _pic = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 76, ForeColor = Muted, Padding = new Padding(6) };
    readonly Button _save = new() { Width = 220, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
    readonly Dictionary<string, byte[]> _pending = new(StringComparer.OrdinalIgnoreCase);   // caminho → bytes novos
    PckArchive _surf;
    string _family = "classic";
    List<LoadingScreens.Screen> _list = new();

    public LoadingScreensDialog(string elementDir)
    {
        _elementDir = elementDir; _surfPath = Path.Combine(elementDir, "surfaces.pck");
        _surf = new PckArchive(_surfPath);
        Text = Loc.T("ls.title", "Telas de carregamento"); Width = 1200; Height = 760; StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9);
        _lv.LargeImageList = _thumbs;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(4, 6, 0, 0) };
        Button B(string key, string pt, Action a, int w = 170) { var b = new Button { Text = Loc.T(key, pt), Width = w, Height = 28, FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => a(); top.Controls.Add(b); return b; }
        B("ls.replace", "Trocar pela imagem…", Replace);
        B("ls.add", "Acrescentar tela…", Add);
        B("ls.export", "Exportar todas…", Export, 140);
        top.Controls.Add(_save);
        _save.Click += (_, _) => Save();

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 640 };
        split.Panel1.Controls.Add(_lv);
        split.Panel2.Controls.Add(_pic); split.Panel2.Controls.Add(_info);
        Controls.Add(split); Controls.Add(top);
        _lv.SelectedIndexChanged += (_, _) => ShowSel();
        _lv.DoubleClick += (_, _) => Replace();
        FormClosing += (_, e) =>
        {
            if (_pending.Count > 0 && MessageBox.Show(this, Loc.T("ls.discard", "Há telas trocadas que ainda não foram gravadas. Fechar e descartar?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
        };
        FormClosed += (_, _) => { _surf.Dispose(); _thumbs.Dispose(); };
        Reload();
    }

    byte[]? Bytes(string path) => _pending.TryGetValue(path, out var b) ? b : _surf.Find(path) is { } e ? _surf.Read(e) : null;

    void Reload()
    {
        _family = LoadingScreens.Family(_elementDir, _surf);
        _list = LoadingScreens.List(_surf, _family);
        // telas novas ainda não gravadas
        foreach (var p in _pending.Keys)
            if (LoadingScreens.Parse(p) is var (set, n) && !_list.Any(s => s.Set == set && s.N == n))
                _list.Add(new LoadingScreens.Screen { Set = set, N = n, Files = LoadingScreens.PathsFor(set, n) });
        _list = _list.OrderBy(s => s.Set == "background" ? 2 : s.Set is "classic" or "1600" ? 0 : 1).ThenBy(s => s.Set).ThenBy(s => s.N).ToList();
        _lv.BeginUpdate(); _lv.Items.Clear(); _lv.Groups.Clear();
        foreach (var im in _thumbs.Images.Cast<Image>().ToList()) im.Dispose();
        _thumbs.Images.Clear();
        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var s in _list)
        {
            if (!groups.TryGetValue(s.Set, out var g))
            {
                string name = s.Set switch
                {
                    "classic" => Loc.T("ls.gClassic", "Telas (1024×768, 1280×1024 e 1280×720 — o jogo sorteia)"),
                    "1600" => Loc.T("ls.g1600", "Telas 1600×1024 (o jogo sorteia)"),
                    "background" => Loc.T("ls.gBack", "Fundo do login"),
                    _ => string.Format(Loc.T("ls.gMap", "Telas do mapa {0}"), s.Set["loading\\".Length..]),
                };
                groups[s.Set] = g = new ListViewGroup(name);
                _lv.Groups.Add(g);
            }
            _thumbs.Images.Add(Thumb(s));
            _lv.Items.Add(new ListViewItem(s.Label + (s.Files.Any(_pending.ContainsKey) ? " *" : ""), _thumbs.Images.Count - 1) { Tag = s, Group = g });
        }
        _lv.EndUpdate();
        _save.Text = string.Format(Loc.T("ls.save", "Gravar no surfaces.pck ({0})"), _pending.Count);
        _save.Enabled = _pending.Count > 0;
        _info.Text = string.Format(Loc.T("ls.family", "Este cliente usa as telas {0}. Duplo clique troca a imagem."),
            _family == "1600" ? "1600-N.jpg + loading\\<mapa>\\N.jpg" : "载入1024/1280/16-9-N.jpg");
    }

    Image Thumb(LoadingScreens.Screen s)
    {
        var bmp = new Bitmap(ThumbW, ThumbH);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        if (Bytes(s.Files[0]) is { } b && GameTextures.Decode(b, ".jpg") is { } img)
            using (img)
            {
                float k = Math.Min((float)ThumbW / img.Width, (float)ThumbH / img.Height);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, (ThumbW - img.Width * k) / 2, (ThumbH - img.Height * k) / 2, img.Width * k, img.Height * k);
            }
        return bmp;
    }

    LoadingScreens.Screen? Sel => _lv.SelectedItems.Count > 0 ? _lv.SelectedItems[0].Tag as LoadingScreens.Screen : null;

    void ShowSel()
    {
        _pic.Image?.Dispose(); _pic.Image = null;
        if (Sel is not { } s) return;
        var sizes = new List<string>();
        foreach (var f in s.Files)
            if (Bytes(f) is { } b && GameTextures.Decode(b, ".jpg") is { } img)
            {
                sizes.Add($"{Path.GetFileName(f)} {img.Width}×{img.Height}");
                if (_pic.Image is null) _pic.Image = img; else img.Dispose();
            }
            else sizes.Add(Path.GetFileName(f) + " " + Loc.T("ls.missing", "(falta)"));
        _info.Text = string.Join("   ", sizes) + (s.Files.Any(_pending.ContainsKey) ? "\n" + Loc.T("ls.pending", "trocada, falta gravar") : "");
    }

    static Image? Pick(IWin32Window owner)
    {
        using var dlg = new OpenFileDialog { Filter = Loc.T("ls.filter", "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.tga;*.dds") };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        var img = GameTextures.Decode(File.ReadAllBytes(dlg.FileName), Path.GetExtension(dlg.FileName));
        if (img is null) MessageBox.Show(owner, Loc.T("uim.imgBad", "a imagem não abre (formato diferente da extensão?)"), Loc.T("ls.title", "Telas de carregamento"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return img;
    }

    // tamanho de cada arquivo: o atual; para um N novo, o do N=1 do mesmo conjunto
    Size SizeOf(string path, LoadingScreens.Screen? like)
    {
        var src = Bytes(path) ?? (like is null ? null : Bytes(like.Files.FirstOrDefault(f => Path.GetFileName(f).Split('-')[0] == Path.GetFileName(path).Split('-')[0]) ?? like.Files[0]));
        if (src is not null && GameTextures.Decode(src, ".jpg") is { } img) using (img) return img.Size;
        return new Size(1600, 1024);
    }

    void Replace()
    {
        if (Sel is not { } s) return;
        using var img = Pick(this);
        if (img is null) return;
        foreach (var f in s.Files) _pending[f] = LoadingScreens.Fit(img, SizeOf(f, null));
        Reload(); Reselect(s.Set, s.N);
    }

    void Add()
    {
        var sets = _list.Select(s => s.Set).Where(s => s != "background").Distinct().ToList();
        if (sets.Count == 0) sets.Add(_family == "1600" ? "1600" : "classic");
        var set = sets[0];
        if (sets.Count > 1)
        {
            // escolher o conjunto (telas gerais ou de um mapa)
            using var f = new Form { Text = Loc.T("ls.add", "Acrescentar tela…"), Width = 380, Height = 150, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 12, Top = 14, Width = 340 };
            cb.Items.AddRange(sets.Cast<object>().ToArray()); cb.SelectedIndex = 0;
            var ok = new Button { Text = "OK", Left = 272, Top = 60, Width = 80, DialogResult = DialogResult.OK };
            f.Controls.Add(cb); f.Controls.Add(ok); f.AcceptButton = ok;
            if (f.ShowDialog(this) != DialogResult.OK) return;
            set = (string)cb.SelectedItem!;
        }
        using var img = Pick(this);
        if (img is null) return;
        int n = LoadingScreens.NextN(_list, set);
        var like = _list.FirstOrDefault(s => s.Set == set);
        foreach (var p in LoadingScreens.PathsFor(set, n)) _pending[p] = LoadingScreens.Fit(img, SizeOf(p, like));
        if (set == "1600" && LoadingScreens.ProcbarWith(Bytes(@"surfaces\procbar.ini"), n) is { } ini) _pending[@"surfaces\procbar.ini"] = ini;
        Reload(); Reselect(set, n);
    }

    void Reselect(string set, int n)
    {
        foreach (ListViewItem it in _lv.Items)
            if (it.Tag is LoadingScreens.Screen s && s.Set == set && s.N == n) { it.Selected = true; it.EnsureVisible(); break; }
    }

    void Export()
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("ls.exportTo", "Pasta para as telas (os caminhos do pacote são mantidos)") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int k = 0;
        foreach (var f in _list.SelectMany(s => s.Files))
            if (Bytes(f) is { } b) { var dest = Path.Combine(dlg.SelectedPath, f); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.WriteAllBytes(dest, b); k++; }
        MessageBox.Show(this, string.Format(Loc.T("ls.exported", "{0} imagens exportadas. Depois de editar, use \"Importar pasta…\" no Extrator PCK (surfaces.pck) para levar de volta."), k), Text);
    }

    void Save()
    {
        if (_pending.Count == 0) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            _surf.Dispose();
            var bak = PckWriter.Write(_surfPath, _pending);
            _pending.Clear();
            _surf = new PckArchive(_surfPath);
            Reload();
            MessageBox.Show(this, string.Format(Loc.T("ls.saved", "Telas gravadas no surfaces.pck.\nBackup do pacote anterior: {0}"), Path.GetFileName(bak)), Text);
        }
        catch (Exception ex)
        {
            if (!File.Exists(_surfPath)) throw;
            _surf = new PckArchive(_surfPath);
            MessageBox.Show(this, ex.Message, Loc.T("ui.saveFail", "Falha ao salvar"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }
}
