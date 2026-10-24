using System.Globalization;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor da loja de cash com a MESMA tela do jogo: desenha a janela qshop.xml do interfaces.pck com as texturas
/// do surfaces.pck (categorias, subcategorias, 8 itens por página, figura grande e descrição à esquerda).
/// Clique navega como no jogo; duplo clique no nome de um item ou de uma categoria edita ali mesmo; o painel
/// da direita edita o resto. Salvar grava cliente (gshop) e servidor (gshopsev) juntos, com o mesmo timestamp.
/// </summary>
internal sealed class GShopView : UserControl
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128),
        Accent = Color.FromArgb(37, 99, 235), Gold = Color.FromArgb(255, 203, 74), Cream = Color.FromArgb(255, 234, 170);

    readonly Func<string?> _elementDir;
    readonly Func<int, Image?> _itemIcon;
    readonly Func<int, (string Name, Color Color)?> _itemInfo;
    readonly Func<IWin32Window, int, int?> _pickItem;

    // dois pares: 0 = Loja (gshop/gshopsev), 1 = Butique de Eventos (gshop1/gshopsev1)
    readonly GShopFile?[] _shops = new GShopFile?[2];
    readonly string?[] _clientPath = new string?[2], _serverPath = new string?[2];
    readonly bool[] _dirty = new bool[2];
    int _which, _main, _sub = -1, _scroll;
    GShopItem? _sel;

    PckArchive? _interfacesPck, _surfacesPck;
    GameTextures? _tex;
    GameDialog? _dlg, _cell;

    readonly GamePanel _game;
    readonly Label _info = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Padding = new Padding(4, 4, 0, 0) };
    readonly FlowLayoutPanel _bar = new() { Dock = DockStyle.Top, Height = 38, BackColor = Bg, WrapContents = false, Padding = new Padding(2, 4, 0, 0) };
    readonly Panel _edit = new() { Dock = DockStyle.Right, Width = 410, BackColor = Color.White, Padding = new Padding(10), AutoScroll = true };
    TextBox? _inline;
    bool _binding;

    // painel de edição
    readonly TextBox _name = new() { MaxLength = GShopItem.NameChars - 1 }, _surface = new(), _desc = new() { Multiline = true, Height = 120, ScrollBars = ScrollBars.Vertical, MaxLength = GShopItem.DescChars - 1 };
    readonly NumericUpDown _itemId = Num(0, int.MaxValue), _amount = Num(0, int.MaxValue), _giftId = Num(0, int.MaxValue), _giftAmount = Num(0, int.MaxValue), _giftDur = Num(0, int.MaxValue);
    readonly Label _itemName = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(330, 0) };
    readonly ComboBox _cat = new() { DropDownStyle = ComboBoxStyle.DropDownList }, _subCb = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown[] _price = new NumericUpDown[4], _status = new NumericUpDown[4];
    readonly ComboBox[] _dur = new ComboBox[4];

    static NumericUpDown Num(decimal min, decimal max, int dec = 0) => new() { Minimum = min, Maximum = max, DecimalPlaces = dec, Width = 110, ThousandsSeparator = true };

    public GShopView(Func<string?> elementDir, Func<int, Image?> itemIcon, Func<int, (string, Color)?> itemInfo, Func<IWin32Window, int, int?> pickItem)
    {
        _elementDir = elementDir; _itemIcon = itemIcon; _itemInfo = itemInfo; _pickItem = pickItem;
        BackColor = Bg; Dock = DockStyle.Fill;
        _game = new GamePanel(this) { Dock = DockStyle.Fill };
        BuildBar(); BuildEditor();
        Controls.Add(_game); Controls.Add(_edit); Controls.Add(_info); Controls.Add(_bar);
        VisibleChanged += (_, _) => { if (Visible && _shops[0] is null) TryOpenGame(); };
    }

    // ---------- arquivos ----------
    void BuildBar()
    {
        Button B(string key, string pt, Action a, int w = 120)
        {
            var b = new Button { Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Fg };
            b.Click += (_, _) => a();
            AppLang.Changed += () => b.Text = Loc.T(key, pt);
            b.Text = Loc.T(key, pt);
            _bar.Controls.Add(b);
            return b;
        }
        B("gshop.open", "Abrir pasta…", OpenFolder);
        var save = B("gshop.save", "Salvar loja", Save);
        save.BackColor = Accent; save.ForeColor = Color.White;
        _bar.Controls.Add(new Label { Width = 14 });
        B("gshop.new", "＋ Novo item", NewItem);
        B("gshop.clone", "Clonar", CloneItem, 90);
        B("gshop.delete", "Excluir", DeleteItem, 90);
        B("gshop.up", "◀ Antes", () => MoveItem(-1), 90);
        B("gshop.down", "Depois ▶", () => MoveItem(+1), 90);
        _bar.Controls.Add(new Label { Width = 14 });
        B("gshop.undo", "↶ Desfazer", () => Undo(), 100);
        B("gshop.redo", "↷ Refazer", () => Redo(), 100);
    }

    /// <summary>A pasta do jogo mudou (elements.data aberto): abre a loja dela se não houver mudança pendente.</summary>
    public void GameFolderChanged()
    {
        if (_dirty.Any(d => d)) return;
        if (_elementDir() is { } dir) Open(dir);
    }

    void TryOpenGame()
    {
        var dir = _elementDir();
        if (dir is not null) Open(dir);
        else _info.Text = Loc.T("gshop.noGame", "Abra a pasta do Perfect World (aba Elements) ou use \"Abrir pasta…\" com o gshop.data.");
    }

    void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("gshop.pickDir", "Pasta element do cliente (ou a pasta que tem o gshop.data)") };
        if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.SelectedPath);
    }

    /// <summary>Aceita a pasta element do cliente ou a pasta com os .data. O servidor (gshopsev) é procurado ao lado.</summary>
    void Open(string dir)
    {
        if (_dirty.Any(d => d) && MessageBox.Show(this, Loc.T("gshop.discard", "Há mudanças na loja não salvas. Descartar?"), "GShop", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var data = Directory.Exists(Path.Combine(dir, "data")) ? Path.Combine(dir, "data") : dir;
        var element = Directory.Exists(Path.Combine(dir, "data")) ? dir : Path.GetDirectoryName(dir) ?? dir;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var c = Path.Combine(data, i == 0 ? "gshop.data" : "gshop1.data");
                _shops[i] = File.Exists(c) ? GShopFile.ReadClient(File.ReadAllBytes(c)) : null;
                _clientPath[i] = File.Exists(c) ? c : null;
                var sv = ServerFile(data, i == 0 ? "gshopsev.data" : "gshopsev1.data");
                _serverPath[i] = sv;
                var diffs = _shops[i] is { } f && sv is not null ? f.AttachServer(File.ReadAllBytes(sv)) : new();
                if (diffs.Count > 0)
                    MessageBox.Show(this, string.Format(Loc.T("gshop.pairDiff", "{0} e {1} não estão iguais:\n{2}\n\nAo salvar, o servidor é refeito a partir do cliente."), Path.GetFileName(c), Path.GetFileName(sv), string.Join("\n", diffs.Take(8))), "GShop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _dirty[i] = false;
            }
            // tela do jogo: interfaces.pck (qshop.xml) + texturas do surfaces.pck
            _interfacesPck?.Dispose(); _surfacesPck?.Dispose(); _tex?.Dispose();
            _interfacesPck = TryPck(Path.Combine(element, "interfaces.pck"));
            _surfacesPck = TryPck(Path.Combine(element, "surfaces.pck"));
            _tex = _surfacesPck is null ? null : new GameTextures(_surfacesPck);
            GameFonts.Load(element);
            _dlg = _interfacesPck is null ? null : GameDialog.Load(_interfacesPck, @"interfaces\version01\qshop.xml");
            _cell = _interfacesPck is null ? null : GameDialog.Load(_interfacesPck, @"interfaces\version01\qshopitemdefault0.xml");
            FillSurfaceList();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("gshop.openFail", "Não consegui abrir a loja"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        _which = _shops[0] is not null ? 0 : 1; _main = 0; _sub = -1; _scroll = 0;
        _undo.Clear(); _redo.Clear();
        SelectItem(VisibleItems().FirstOrDefault());
        UpdateInfo(); _game.Invalidate();
    }

    static PckArchive? TryPck(string p) { try { return File.Exists(p) ? new PckArchive(p) : null; } catch { return null; } }

    // gshopsev ao lado do cliente; senão a pasta de servidor lembrada (gamed/config)
    string? ServerFile(string clientData, string name)
    {
        var here = Path.Combine(clientData, name);
        if (File.Exists(here)) return here;
        var dir = LoadSetting("gshop_server");
        return dir is not null && File.Exists(Path.Combine(dir, name)) ? Path.Combine(dir, name) : null;
    }

    void UpdateInfo()
    {
        var f = _shops[_which];
        if (f is null) { _info.Text = Loc.T("gshop.none", "Nenhuma loja aberta."); return; }
        _info.Text = string.Format(Loc.T("gshop.info", "{0} · {1} itens · servidor: {2}{3}"),
            _clientPath[_which], f.Items.Count,
            _serverPath[_which] ?? Loc.T("gshop.noServer", "(gshopsev não encontrado — escolha em Salvar)"),
            _dirty[_which] ? "  ·  " + Loc.T("gshop.unsaved", "não salvo") : "");
    }

    void Save()
    {
        var f = _shops[_which];
        if (f is null || _clientPath[_which] is not { } cp) return;
        if (_serverPath[_which] is null)
        {
            using var dlg = new FolderBrowserDialog { Description = Loc.T("gshop.pickServer", "Pasta do servidor com o gshopsev.data (home/gamed/config)") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SaveSetting("gshop_server", dlg.SelectedPath);
            _serverPath[_which] = Path.Combine(dlg.SelectedPath, _which == 0 ? "gshopsev.data" : "gshopsev1.data");
        }
        try
        {
            // o jogo compara o timestamp do cliente com o do servidor: os dois recebem o mesmo, novo
            f.Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            AtomicFileSave.Write(cp, f.WriteClient());
            AtomicFileSave.Write(_serverPath[_which]!, f.WriteServer());
            _dirty[_which] = false;
            UpdateInfo();
            MessageBox.Show(this, string.Format(Loc.T("gshop.saved", "Loja salva:\n{0}\n{1}\n\nCliente e servidor com o mesmo timestamp. Envie o gshopsev ao servidor e o gshop aos jogadores."), cp, _serverPath[_which]), "GShop");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("gshop.saveFail", "Falha ao salvar a loja"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    static string SettingFile(string k) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", k + ".txt");
    static string? LoadSetting(string k) { try { return File.Exists(SettingFile(k)) ? File.ReadAllText(SettingFile(k)).Trim() : null; } catch { return null; } }
    static void SaveSetting(string k, string v) { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingFile(k))!); File.WriteAllText(SettingFile(k), v); } catch { } }

    // ---------- desfazer / refazer (foto da loja antes de cada mudança) ----------
    readonly List<(int Which, byte[] Data, int Sel)> _undo = new(), _redo = new();
    string? _lastKey; DateTime _lastPush;

    /// <summary>Guarda o estado antes de mudar. Digitação seguida no mesmo campo vira um passo só.</summary>
    internal void Snapshot(string key)
    {
        if (_binding || _shops[_which] is not { } f) return;
        var now = DateTime.Now;
        if (key == _lastKey && (now - _lastPush).TotalSeconds < 2) { _lastPush = now; return; }
        _undo.Add((_which, f.WriteClient(), _sel?.ShopId ?? -1));
        if (_undo.Count > 60) _undo.RemoveAt(0);
        _redo.Clear(); _lastKey = key; _lastPush = now;
    }

    public bool Undo() => Swap(_undo, _redo);
    public bool Redo() => Swap(_redo, _undo);

    bool Swap(List<(int Which, byte[] Data, int Sel)> from, List<(int Which, byte[] Data, int Sel)> to)
    {
        if (from.Count == 0 || _shops[from[^1].Which] is not { } cur) return false;
        var (w, data, sel) = from[^1]; from.RemoveAt(from.Count - 1);
        to.Add((w, cur.WriteClient(), _sel?.ShopId ?? -1));
        var back = GShopFile.ReadClient(data);
        _shops[w] = back; _which = w; _dirty[w] = true; _lastKey = null;
        SelectItem(back.Items.FirstOrDefault(i => i.ShopId == sel) ?? VisibleItems().FirstOrDefault());
        UpdateInfo(); _game.Invalidate();
        return true;
    }

    void Touch() { _dirty[_which] = true; UpdateInfo(); _game.Invalidate(); }

    // ---------- itens visíveis (categoria/subcategoria atuais) ----------
    IEnumerable<GShopItem> VisibleItems() => _shops[_which]?.Items.Where(i => i.MainType == _main && (_sub < 0 || i.SubType == _sub)) ?? Enumerable.Empty<GShopItem>();

    void NewItem()
    {
        if (_shops[_which] is not { } f) return;
        Snapshot(Guid.NewGuid().ToString());
        var it = GShopItem.New(f.Items.Count == 0 ? 0 : f.Items.Max(i => i.ShopId) + 1);
        it.MainType = _main; it.SubType = Math.Max(0, _sub); it.Name = Loc.T("gshop.newName", "Novo item");
        it.SetOption(0, new GShopOption(100, 0, 0, 0, -1, 0, 0, 0));
        f.Items.Add(it);
        SelectItem(it); ScrollTo(it); Touch();
    }

    void CloneItem()
    {
        if (_shops[_which] is not { } f || _sel is null) return;
        Snapshot(Guid.NewGuid().ToString());
        var it = _sel.Clone(f.Items.Max(i => i.ShopId) + 1);
        f.Items.Insert(f.Items.IndexOf(_sel) + 1, it);
        SelectItem(it); ScrollTo(it); Touch();
    }

    void DeleteItem()
    {
        if (_shops[_which] is not { } f || _sel is null) return;
        if (MessageBox.Show(this, string.Format(Loc.T("gshop.confirmDelete", "Excluir \"{0}\" da loja?"), _sel.Name), "GShop", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        Snapshot(Guid.NewGuid().ToString());
        var vis = VisibleItems().ToList(); int k = vis.IndexOf(_sel);
        f.Items.Remove(_sel);
        vis = VisibleItems().ToList();
        SelectItem(vis.Count == 0 ? null : vis[Math.Min(k, vis.Count - 1)]); Touch();
    }

    // troca de lugar com o vizinho visível (a ordem do arquivo é a ordem na tela do jogo)
    void MoveItem(int dir)
    {
        if (_shops[_which] is not { } f || _sel is null) return;
        Snapshot(Guid.NewGuid().ToString());
        var vis = VisibleItems().ToList(); int k = vis.IndexOf(_sel) + dir;
        if (k < 0 || k >= vis.Count) return;
        int a = f.Items.IndexOf(_sel), b = f.Items.IndexOf(vis[k]);
        (f.Items[a], f.Items[b]) = (f.Items[b], f.Items[a]);
        ScrollTo(_sel); Touch();
    }

    void ScrollTo(GShopItem it)
    {
        int k = VisibleItems().ToList().IndexOf(it);
        if (k < 0) return;
        int row = k / 2;
        if (row < _scroll) _scroll = row;
        if (row > _scroll + 3) _scroll = row - 3;
    }

    // ---------- seleção e painel de edição ----------
    void SelectItem(GShopItem? it)
    {
        _sel = it; _binding = true;
        try
        {
            _edit.Enabled = it is not null;
            var f = _shops[_which];
            _cat.Items.Clear(); _subCb.Items.Clear();
            if (f is not null) foreach (var c in f.Categories) _cat.Items.Add(c.Name);
            if (it is null) return;
            _name.Text = it.Name; _itemId.Value = Math.Max(0, it.ItemId); _amount.Value = Math.Max(0, it.Amount);
            _surface.Text = it.Surface; _desc.Text = it.Description.Replace("\\r", "\r\n");   // no arquivo a quebra são as letras \r
            if (it.MainType >= 0 && it.MainType < _cat.Items.Count) _cat.SelectedIndex = it.MainType;
            FillSubs(); if (it.SubType >= 0 && it.SubType < _subCb.Items.Count) _subCb.SelectedIndex = it.SubType;
            for (int k = 0; k < 4; k++)
            {
                var o = it.GetOption(k);
                _price[k].Value = Math.Clamp(o.Price / 100m, _price[k].Minimum, _price[k].Maximum);
                _dur[k].Text = DurationText(o.Duration, o.IsEmpty);
                _status[k].Value = Math.Clamp(o.Status, 0, 99);
            }
            _giftId.Value = Math.Max(0, it.GiftId); _giftAmount.Value = Math.Max(0, it.GiftAmount); _giftDur.Value = Math.Max(0, it.GiftDuration);
            UpdateItemName();
        }
        finally { _binding = false; _game.Invalidate(); }
    }

    void FillSubs()
    {
        _subCb.Items.Clear();
        if (_shops[_which] is { } f && _cat.SelectedIndex >= 0) foreach (var s in f.Categories[_cat.SelectedIndex].Subs) _subCb.Items.Add(s);
    }

    void UpdateItemName() => _itemName.Text = _itemInfo((int)_itemId.Value) is { } x ? $"→ {x.Name}" : Loc.T("gshop.itemMissing", "(ID não existe no elements.data)");

    static readonly (int Secs, string Key, string Pt)[] Durations =
    {
        (0, "gshop.permanent", "Permanente"), (3600, "gshop.d1h", "1 hora"), (86400, "gshop.d1", "1 dia"), (259200, "gshop.d3", "3 dias"),
        (604800, "gshop.d7", "7 dias"), (1296000, "gshop.d15", "15 dias"), (2592000, "gshop.d30", "30 dias"), (7776000, "gshop.d90", "90 dias"),
    };

    static string DurationText(int secs, bool empty = false)
    {
        if (empty) return Loc.T("gshop.unused", "(sem uso)");
        foreach (var (s, k, pt) in Durations) if (s == secs) return Loc.T(k, pt);
        return secs % 86400 == 0 ? string.Format(Loc.T("gshop.days", "{0} dias"), secs / 86400) : string.Format(Loc.T("gshop.secs", "{0} s"), secs);
    }

    static int? ParseDuration(string t)
    {
        foreach (var (s, k, pt) in Durations) if (t == Loc.T(k, pt)) return s;
        if (t == Loc.T("gshop.unused", "(sem uso)")) return null;
        var digits = new string(t.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var n)) return null;
        return t.Contains('d') ? n * 86400 : t.Contains('h') ? n * 3600 : n;
    }

    void BuildEditor()
    {
        var tl = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string key, string pt, Control c)
        {
            var l = new Label { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0) };
            l.Text = Loc.T(key, pt); AppLang.Changed += () => l.Text = Loc.T(key, pt);
            c.Dock = DockStyle.Fill;
            tl.Controls.Add(l); tl.Controls.Add(c);
        }
        void Head(string key, string pt)
        {
            var l = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 10), ForeColor = Fg, Padding = new Padding(0, 10, 0, 2) };
            l.Text = Loc.T(key, pt); AppLang.Changed += () => l.Text = Loc.T(key, pt);
            tl.Controls.Add(l); tl.SetColumnSpan(l, 2);
        }
        Head("gshop.hItem", "Item da loja");
        Row("gshop.name", "Nome", _name);
        var pick = new Button { Text = "…", Width = 30, FlatStyle = FlatStyle.Flat };
        var idRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        idRow.Controls.Add(_itemId); idRow.Controls.Add(pick);
        Row("gshop.itemId", "Item (ID)", idRow);
        tl.Controls.Add(new Label()); tl.Controls.Add(_itemName);
        Row("gshop.amount", "Quantidade", _amount);
        Row("gshop.cat", "Categoria", _cat);
        Row("gshop.sub", "Subcategoria", _subCb);
        Row("gshop.surface", "Figura", _surface);
        Head("gshop.hDesc", "Descrição (^RRGGBB muda a cor)");
        var colors = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        foreach (var (hex, c) in new[] { ("ffffff", Color.White), ("ffcb4a", Gold), ("00ff00", Color.LimeGreen), ("00b4ff", Color.DeepSkyBlue), ("ff7800", Color.DarkOrange), ("ff0000", Color.Red), ("c800ff", Color.MediumOrchid), ("969696", Color.Gray) })
        {
            var b = new Button { Width = 26, Height = 22, BackColor = c, FlatStyle = FlatStyle.Flat, Tag = hex };
            b.Click += (_, _) => { _desc.SelectedText = "^" + hex; _desc.Focus(); };
            colors.Controls.Add(b);
        }
        tl.Controls.Add(colors); tl.SetColumnSpan(colors, 2);
        tl.Controls.Add(_desc); tl.SetColumnSpan(_desc, 2); _desc.Dock = DockStyle.Fill;
        Head("gshop.hOptions", "Opções de venda (preço em ouro, duração, selo)");
        for (int k = 0; k < 4; k++)
        {
            _price[k] = Num(0, 21474836, 2); _price[k].Width = 96;
            _dur[k] = new ComboBox { Width = 96 }; foreach (var (_, key, pt) in Durations) _dur[k].Items.Add(Loc.T(key, pt));
            _status[k] = Num(0, 99); _status[k].Width = 46;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.Add(_price[k]); row.Controls.Add(_dur[k]); row.Controls.Add(_status[k]);
            var l = new Label { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0), Text = $"{k + 1}." };
            tl.Controls.Add(l); tl.Controls.Add(row);
            int kk = k;
            _price[k].ValueChanged += (_, _) => EditOption(kk);
            _dur[k].TextChanged += (_, _) => EditOption(kk);
            _status[k].ValueChanged += (_, _) => EditOption(kk);
        }
        Head("gshop.hGift", "Presente junto");
        Row("gshop.giftId", "Item (ID)", _giftId);
        Row("gshop.giftAmount", "Quantidade", _giftAmount);
        Row("gshop.giftDur", "Duração (s)", _giftDur);
        _edit.Controls.Add(tl);

        _name.TextChanged += (_, _) => Edit(it => it.Name = _name.Text);
        _itemId.ValueChanged += (_, _) => { Edit(it => it.ItemId = (int)_itemId.Value); UpdateItemName(); };
        pick.Click += (_, _) => { if (_sel is not null && _pickItem(FindForm()!, _sel.ItemId) is { } id) _itemId.Value = id; };
        _amount.ValueChanged += (_, _) => Edit(it => it.Amount = (int)_amount.Value);
        _cat.SelectedIndexChanged += (_, _) => { if (_binding) return; FillSubs(); if (_subCb.Items.Count > 0) _subCb.SelectedIndex = 0; Edit(it => it.MainType = _cat.SelectedIndex); };
        _subCb.SelectedIndexChanged += (_, _) => Edit(it => it.SubType = _subCb.SelectedIndex);
        _surface.TextChanged += (_, _) => Edit(it => it.Surface = _surface.Text);
        _desc.TextChanged += (_, _) => Edit(it => it.Description = _desc.Text.Replace("\r\n", "\\r"));
        _giftId.ValueChanged += (_, _) => Edit(it => it.GiftId = (int)_giftId.Value);
        _giftAmount.ValueChanged += (_, _) => Edit(it => it.GiftAmount = (int)_giftAmount.Value);
        _giftDur.ValueChanged += (_, _) => Edit(it => it.GiftDuration = (int)_giftDur.Value);
    }

    readonly AutoCompleteStringCollection _surfaces = new();
    void FillSurfaceList()
    {
        _surfaces.Clear();
        if (_tex is null) return;
        foreach (var p in _tex.List("百宝阁")) _surfaces.Add("Surfaces" + p["surfaces".Length..]);
        _surface.AutoCompleteMode = AutoCompleteMode.SuggestAppend; _surface.AutoCompleteSource = AutoCompleteSource.CustomSource; _surface.AutoCompleteCustomSource = _surfaces;
    }

    void Edit(Action<GShopItem> a, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (_binding || _sel is null) return;
        Snapshot($"campo{line}:{_sel.ShopId}");
        a(_sel); Touch();
    }

    void EditOption(int k) => EditOpt(k, it =>
    {
        var o = it.GetOption(k);
        var d = ParseDuration(_dur[k].Text);
        it.SetOption(k, d is null && _price[k].Value == 0
            ? new GShopOption(0, 0, 0, 0, o.ControlType == 0 ? 0 : o.ControlType, 0, 0, 0)
            : o with { Price = (int)Math.Round(_price[k].Value * 100), Duration = d ?? o.Duration, Status = (int)_status[k].Value, ControlType = o.IsEmpty ? -1 : o.ControlType });
    });

    void EditOpt(int k, Action<GShopItem> a)
    {
        if (_binding || _sel is null) return;
        Snapshot($"opcao{k}:{_sel.ShopId}");
        a(_sel); Touch();
    }

    // ---------- edição direto na tela (duplo clique) ----------
    void InlineEdit(Rectangle screenRect, string text, int maxLen, Action<string> apply)
    {
        _inline?.Dispose();
        _inline = new TextBox { Bounds = screenRect, Text = text, MaxLength = maxLen, Font = new Font("Segoe UI", 10), BorderStyle = BorderStyle.FixedSingle };
        bool done = false;
        void Commit(bool ok) { if (done) return; done = true; if (ok) apply(_inline!.Text); var t = _inline; _inline = null; t?.Dispose(); _game.Focus(); }
        _inline.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Commit(true); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) { Commit(false); e.SuppressKeyPress = true; } };
        _inline.LostFocus += (_, _) => Commit(true);
        _game.Controls.Add(_inline);
        _inline.Focus(); _inline.SelectAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tex?.Dispose(); _interfacesPck?.Dispose(); _surfacesPck?.Dispose(); }
        base.Dispose(disposing);
    }

    // =====================================================================================
    // Painel que desenha a janela do jogo (coordenadas do qshop.xml, escaladas para caber)
    // =====================================================================================
    sealed class GamePanel : Panel
    {
        readonly GShopView _v;
        float _s = 1; PointF _o;
        public GamePanel(GShopView v) { _v = v; DoubleBuffered = true; BackColor = Color.FromArgb(24, 26, 32); }

        GameDialog? D => _v._dlg;
        Size Win => D?.Size is { Width: > 0 } s ? s : new Size(787, 581);

        // posições do jogo; se o qshop.xml não estiver disponível, usa as do 1.4.4
        Rectangle R(string name, Rectangle fallback) => D?[name]?.Rect is { Width: > 0 } r ? r : fallback;
        Rectangle MainBtn(int i) => R($"Btn_MainType{i}", new Rectangle(57 + i * 84, 101, 81, 33));
        Rectangle SubBtn(int i) => i < 0 ? R("Btn_SubType-1", new Rectangle(674, 143, 53, 22)) : R($"Btn_SubType{i}", new Rectangle(256 + i * 52, 143, 53, 22));
        Rectangle Cell(int i) => R($"Lab_Dialog{i}", new Rectangle(258 + i % 2 * 223, 169 + i / 2 * 94, 226, 93));
        Rectangle TabBtn(int i) => i == 0 ? R("Btn_QShop", new Rectangle(87, 72, 58, 29)) : R("Btn_BackShop", new Rectangle(141, 72, 73, 29));

        Rectangle ToScreen(Rectangle r) => Rectangle.Round(new RectangleF(_o.X + r.X * _s, _o.Y + r.Y * _s, r.Width * _s, r.Height * _s));
        Point ToGame(Point p) => new((int)((p.X - _o.X) / _s), (int)((p.Y - _o.Y) / _s));

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        Image? Img(string? path, int frames = 1) => _v._tex?.Get(path, frames);
        string? ImgOf(string ctl, int k = 0) => D?[ctl]?.Images is { } l && l.Count > k ? l[k] : null;

        // Texto em coordenadas do jogo. Graphics.DrawString respeita a escala da tela (o TextRenderer não).
        static Font GameFont(float size, bool bold) => new("Microsoft YaHei UI", Math.Max(8, size * 1.05f), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        static readonly Color Outline = Color.FromArgb(10, 20, 30);

        // mesmas fontes do cliente e reserva p/ acentos do editor de interface; aqui o texto diminui para caber (nomes vêm do arquivo)
        void Text(Graphics g, string t, Rectangle r, Color c, float size, bool bold = false, ContentAlignment align = ContentAlignment.MiddleCenter)
            => GameDraw.Text(g, t, r, c, size, bold, align);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var win = Win;
            _s = Math.Min((Width - 10f) / win.Width, (Height - 10f) / win.Height);
            _o = new PointF((Width - win.Width * _s) / 2, (Height - win.Height * _s) / 2);
            g.TranslateTransform(_o.X, _o.Y); g.ScaleTransform(_s, _s);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            var f = _v._shops[_v._which];
            if (Img(D?.Frame) is { } frame) g.DrawImage(frame, new Rectangle(Point.Empty, win));
            else using (var b = new SolidBrush(Color.FromArgb(28, 40, 58))) g.FillRectangle(b, new Rectangle(Point.Empty, win));
            GameDraw.Text(g, D?["titletxt"]?.Text ?? "LOJA", R("titletxt", new Rectangle(291, 37, 206, 35)), Gold, 24, false, ContentAlignment.MiddleCenter, D?["titletxt"]?.FontName);   // fonte do título do jogo (隶二)

            // abas Loja / Butique de Eventos (gshop / gshop1)
            for (int i = 0; i < 2; i++)
            {
                var ctl = i == 0 ? "Btn_QShop" : "Btn_BackShop";
                var r = TabBtn(i);
                var img = Img(ImgOf(ctl, i == _v._which ? 1 : 0));
                if (img is not null) g.DrawImage(img, r); else using (var b = new SolidBrush(i == _v._which ? Color.SteelBlue : Color.SlateGray)) g.FillRectangle(b, r);
                Text(g, D?[ctl]?.Text ?? (i == 0 ? "Loja" : "Eventos"), r, _v._shops[i] is null ? Color.Gray : Cream, 9, true);
            }
            if (f is null) { Text(g, Loc.T("gshop.none", "Nenhuma loja aberta."), new Rectangle(258, 300, 450, 40), Cream, 14); return; }

            // categorias (8 botões do jogo) com os nomes do gshop.data
            for (int i = 0; i < Math.Min(8, f.Categories.Count); i++)
            {
                var r = MainBtn(i); var ctl = $"Btn_MainType{i}";
                var img = Img(ImgOf(ctl, i == _v._main ? 1 : 0));
                if (img is not null) g.DrawImage(img, r); else using (var b = new SolidBrush(i == _v._main ? Color.FromArgb(70, 110, 160) : Color.FromArgb(40, 60, 90))) g.FillRectangle(b, r);
                // o botão tem o ícone à esquerda (TextOffsetX=12 no XML): o texto ocupa o espaço depois dele
                var to = D?[ctl] is not null ? new Rectangle(r.X + 26, r.Y + 2, r.Width - 28, r.Height - 2) : r;
                Text(g, f.Categories[i].Name, to, Cream, 9, true);
            }
            // subcategorias + "Tudo"
            var subs = _v._main < f.Categories.Count ? f.Categories[_v._main].Subs : new List<string>();
            for (int i = -1; i < Math.Min(8, subs.Count); i++)
            {
                var r = SubBtn(i);
                var img = Img(ImgOf($"Btn_SubType{i}", i == _v._sub ? 1 : 0) ?? ImgOf("Btn_SubType0", i == _v._sub ? 1 : 0));
                if (img is not null) g.DrawImage(img, r); else using (var b = new SolidBrush(i == _v._sub ? Color.FromArgb(70, 110, 160) : Color.FromArgb(40, 60, 90))) g.FillRectangle(b, r);
                Text(g, i < 0 ? D?["Btn_SubType-1"]?.Text ?? "Tudo" : subs[i], r, Cream, 9);
            }

            // 8 células: 2 colunas x 4 linhas, rolando de 2 em 2
            var vis = _v.VisibleItems().ToList();
            if (f.Items.Count == 0)
                Text(g, Loc.T("gshop.emptyShop", "Esta loja está vazia neste arquivo. Use \"+ Novo item\" para adicionar."), new Rectangle(258, 300, 450, 40), Cream, 12);
            int rows = (vis.Count + 1) / 2;
            _v._scroll = Math.Clamp(_v._scroll, 0, Math.Max(0, rows - 4));
            for (int i = 0; i < 8; i++)
            {
                int k = _v._scroll * 2 + i;
                if (k >= vis.Count) break;
                DrawCell(g, Cell(i), vis[k], vis[k] == _v._sel);
            }
            // barra de rolagem simples no lugar da do jogo
            var sr = R("Scl_Item", new Rectangle(713, 169, 14, 375));
            using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(b, sr);
            if (rows > 4)
            {
                int h = Math.Max(20, sr.Height * 4 / rows), y = sr.Y + (sr.Height - h) * _v._scroll / Math.Max(1, rows - 4);
                using var b = new SolidBrush(Color.FromArgb(200, 160, 130, 70)); g.FillRectangle(b, sr.X + 2, y, sr.Width - 4, h);
            }

            // lado esquerdo: figura grande, nome e descrição do item escolhido (como no jogo)
            if (_v._sel is { } s)
            {
                if (Img(s.Surface) is { } big) g.DrawImage(big, R("Img_Item", new Rectangle(89, 162, 128, 128)));
                Text(g, s.Name, new Rectangle(R("Img_Item", new Rectangle(89, 162, 128, 128)).X - 40, R("Txt_ItemName", new Rectangle(150, 144, 0, 0)).Y - 8, 208, 18), Gold, 12, true);
                var dr = R("Txt_ItemDesc", new Rectangle(71, 315, 160, 180));
                DrawRich(g, s.Description, dr);
            }
            Text(g, D?["cash"]?.Text ?? "Total:", R("cash", new Rectangle(68, 507, 42, 16)), Gold, 10, true, ContentAlignment.MiddleLeft);
            Text(g, string.Format(Loc.T("gshop.count", "{0} itens"), vis.Count), R("Txt_Cash", new Rectangle(113, 507, 120, 15)), Color.White, 10, true, ContentAlignment.MiddleLeft);
        }

        void DrawCell(Graphics g, Rectangle r, GShopItem it, bool selected)
        {
            var c = _v._cell;
            Rectangle C(string n, Rectangle fb) { var x = c?[n]?.Rect is { } q && (q.Width > 0 || q.X > 0 || q.Y > 0) ? q : fb; return new Rectangle(r.X + x.X, r.Y + x.Y, x.Width, x.Height); }
            var frame = Img(selected ? ImgOf("Img_Highlight") : ImgOf("Img_Normal")) ?? Img(c?.Frame);
            if (frame is not null) g.DrawImage(frame, r); else using (var b = new SolidBrush(selected ? Color.FromArgb(60, 90, 130) : Color.FromArgb(35, 50, 75))) g.FillRectangle(b, r);
            // ícone do item (atlas do jogo) na moldura do slot
            var ir = C("Pic_Goods", new Rectangle(17, 44, 32, 32));
            if (Img(c?["Pic_Goods"]?.Images.FirstOrDefault()) is { } slot) g.DrawImage(slot, Rectangle.Inflate(ir, 2, 2));
            if (_v._itemIcon(it.ItemId) is { } icon) g.DrawImage(icon, ir);
            // nome centrado, tempo, preço
            var nr = C("Txt_Itemname", new Rectangle(132, 10, 0, 0));
            Text(g, it.Name, new Rectangle(r.X + 50, nr.Y - 2, r.Width - 56, 18), Gold, 12, false);
            var o = it.GetOption(0);
            var tr = C("Txt_Time", new Rectangle(66, 38, 0, 0));
            Text(g, DurationText(o.Duration), new Rectangle(tr.X, tr.Y, 80, 16), Color.White, 10, true, ContentAlignment.MiddleLeft);
            var pr = C("Txt_Price", new Rectangle(216, 38, 0, 0));
            Text(g, string.Format(Loc.T("gshop.price", "Preço: {0}"), (o.Price / 100m).ToString("#,0.##", CultureInfo.CurrentCulture)), new Rectangle(pr.X - 110, pr.Y, 110, 16), Color.White, 10, true, ContentAlignment.MiddleRight);
            // selos: novo / quente / desconto (status) e presente
            string? badge = o.Status switch { 1 => "img_new", 3 => "img_hot", _ => null };
            if (badge is not null && Img(c?[badge]?.Images.FirstOrDefault(), 3) is { } bi) g.DrawImage(bi, C(badge, new Rectangle(15, 14, 27, 8)));
            if (it.GiftId != 0 && Img(c?["Img_Bg03"]?.Images.FirstOrDefault()) is { } gi) g.DrawImage(gi, C("Img_Bg03", new Rectangle(55, 59, 50, 24)));
            if (Img(c?["Btn_Buy"]?.Images.FirstOrDefault()) is { } buy)
            {
                var br = C("Btn_Buy", new Rectangle(167, 61, 50, 24));
                g.DrawImage(buy, br);
                Text(g, c?["Btn_Buy"]?.Text ?? "Comprar", Rectangle.Inflate(br, 6, 0), Color.FromArgb(255, 251, 202), 8.5f);
            }
            // mais de uma opção de venda: o jogo mostra botões de tempo; aqui um aviso discreto
            int more = Enumerable.Range(1, 3).Count(k => !it.GetOption(k).IsEmpty);
            if (more > 0) Text(g, $"+{more}", new Rectangle(r.X + 118, r.Y + 63, 44, 18), Cream, 9, true);
        }

        // descrição com as cores ^RRGGBB e quebras \r, dentro do retângulo do jogo
        void DrawRich(Graphics g, string desc, Rectangle area)
        {
            using var f = GameFont(11, false);
            using var sf = StringFormat.GenericTypographic;
            float x = area.X, y = area.Y, lineH = f.GetHeight(g) + 2, space = g.MeasureString(" ", f, PointF.Empty, sf).Width + 2;
            // o jogo grava a quebra de linha como as letras \r
            var text = desc.Replace("\\r", "\r").Replace("\r\n", "\r").Replace('\n', '\r');
            foreach (var (run, color) in GameConfigs.Runs(text, Color.White))
            {
                using var b = new SolidBrush(color);
                var lines = run.Split('\n');   // Runs já troca \r por \n
                for (int li = 0; li < lines.Length; li++)
                {
                    if (li > 0) { x = area.X; y += lineH; }
                    foreach (var w in lines[li].Split(' '))
                    {
                        if (w.Length == 0) { x += space; continue; }
                        var wsz = g.MeasureString(w, f, PointF.Empty, sf).Width;
                        if (x + wsz > area.Right && x > area.X) { x = area.X; y += lineH; }
                        if (y + lineH > area.Bottom) return;
                        g.DrawString(w, f, b, x, y, sf);
                        x += wsz + space;
                    }
                    x -= space;   // o espaço depois da última palavra da linha não conta
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _v._scroll += e.Delta > 0 ? -1 : 1;
            Invalidate();
        }

        (string Kind, int Index)? Hit(Point screen)
        {
            var p = ToGame(screen);
            if (_v._shops[_v._which] is not { } f) return null;
            for (int i = 0; i < 2; i++) if (TabBtn(i).Contains(p)) return ("tab", i);
            for (int i = 0; i < Math.Min(8, f.Categories.Count); i++) if (MainBtn(i).Contains(p)) return ("main", i);
            var subs = _v._main < f.Categories.Count ? f.Categories[_v._main].Subs.Count : 0;
            for (int i = -1; i < Math.Min(8, subs); i++) if (SubBtn(i).Contains(p)) return ("sub", i);
            for (int i = 0; i < 8; i++) if (Cell(i).Contains(p)) return ("cell", i);
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            switch (Hit(e.Location))
            {
                case ("tab", var i) when _v._shops[i] is not null: _v._which = i; _v._main = 0; _v._sub = -1; _v._scroll = 0; _v.SelectItem(_v.VisibleItems().FirstOrDefault()); _v.UpdateInfo(); break;
                case ("main", var i): _v._main = i; _v._sub = -1; _v._scroll = 0; _v.SelectItem(_v.VisibleItems().FirstOrDefault()); break;
                case ("sub", var i): _v._sub = i; _v._scroll = 0; _v.SelectItem(_v.VisibleItems().FirstOrDefault()); break;
                case ("cell", var i):
                    var vis = _v.VisibleItems().ToList(); int k = _v._scroll * 2 + i;
                    if (k < vis.Count) _v.SelectItem(vis[k]);
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_v._shops[_v._which] is not { } f) return;
            switch (Hit(e.Location))
            {
                case ("main", var i):
                    _v.InlineEdit(ToScreen(MainBtn(i)), f.Categories[i].Name, GShopFile.NameChars - 1, t => { _v.Snapshot(Guid.NewGuid().ToString()); f.Categories[i].Name = t; _v.Touch(); _v.SelectItem(_v._sel); });
                    break;
                case ("sub", var i) when i >= 0:
                    var cat = f.Categories[_v._main];
                    _v.InlineEdit(ToScreen(SubBtn(i)), cat.Subs[i], GShopFile.NameChars - 1, t => { _v.Snapshot(Guid.NewGuid().ToString()); cat.Subs[i] = t; _v.Touch(); _v.SelectItem(_v._sel); });
                    break;
                case ("cell", _) when _v._sel is { } it:
                    // nome no topo da célula; preço no canto direito
                    var p = ToGame(e.Location);
                    int ci = Enumerable.Range(0, 8).First(x => Cell(x).Contains(p));
                    var cell = Cell(ci);
                    if (p.Y - cell.Y < 32)
                        _v.InlineEdit(ToScreen(new Rectangle(cell.X + 50, cell.Y + 6, cell.Width - 56, 22)), it.Name, GShopItem.NameChars - 1, t => { _v._name.Text = t; });
                    else if (p.X - cell.X > 110 && p.Y - cell.Y < 58)
                        _v.InlineEdit(ToScreen(new Rectangle(cell.X + 116, cell.Y + 34, 100, 22)), (it.GetOption(0).Price / 100m).ToString("0.##", CultureInfo.CurrentCulture), 12,
                            t => { if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out var v) && v >= 0) _v._price[0].Value = Math.Min(v, _v._price[0].Maximum); });
                    else _v._name.Focus();
                    break;
            }
        }
    }
}
