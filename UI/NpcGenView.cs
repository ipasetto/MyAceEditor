using System.Collections;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Editor do npcgen.data (onde monstros, NPCs e pontos de coleta nascem no mapa), PW-KB §S22.
/// Abre o mundo (gamed\config\world) e as instâncias (gamed\config\aNN) do servidor; lista os pontos com o nome e o
/// ícone vindos do elements; à direita edita posição, raio e os monstros do ponto (quantidade, renascimento, trocar).
/// Desfazer/refazer próprio (Ctrl+Z com a aba aberta). Salvar grava só os arquivos mudados, com backup.
/// </summary>
internal sealed class NpcGenView : UserControl
{
    static readonly Color Bg = Color.FromArgb(231, 236, 242), Fg = Color.FromArgb(30, 39, 51), Muted = Color.FromArgb(100, 112, 128),
        Accent = Color.FromArgb(37, 99, 235);

    public enum Kind { Monster, Npc, Mine }

    readonly Func<int, Image?> _icon;
    readonly Func<int, (string Name, Color Color)?> _info;
    readonly Func<IWin32Window, Kind, int, int?> _pick;
    readonly Func<string?> _elementDir;
    readonly Func<int, string?> _modelOf;   // ID → .ecm do elements (file_model)
    readonly Func<int, string?> _blockOf;   // ID → bloco do elements (NPC em ponto de monstro = guarda)
    readonly Action<int> _editItem;         // abre o registro no editor (aba Elements)

    // arquivos do servidor: nome curto ("world", "a01"…) → caminho; carregados sob demanda
    readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);   // world primeiro, depois as instâncias
    readonly Dictionary<string, NpcGenFile> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    string? _configDir, _cur;
    Kind _kind = Kind.Monster;
    List<int> _rows = new();          // índices em Spawns (monstros/NPCs) ou Resources (coleta)
    bool _binding;

    // quebra linha quando a janela é estreita (antes o botão "Mundo 3D" ficava cortado)
    readonly FlowLayoutPanel _bar = new() { Dock = DockStyle.Top, Height = 38, BackColor = Bg, WrapContents = true, Padding = new Padding(2, 4, 0, 2) };
    bool _barSizing;   // altura acompanha as linhas (AutoSize com quebra travava a abertura do programa)
    readonly Label _info2 = new() { Dock = DockStyle.Top, Height = 22, ForeColor = Muted, Padding = new Padding(4, 4, 0, 0) };
    readonly ComboBox _fileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly ComboBox _kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly TextBox _search = new() { Width = 170 };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, HideSelection = false, MultiSelect = false, GridLines = true };
    readonly ImageList _icons = new() { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
    readonly Dictionary<int, int> _iconIndex = new();
    readonly System.Windows.Forms.Timer _searchWait = new() { Interval = 250 };

    // editor do ponto
    readonly Panel _edit = new() { Dock = DockStyle.Right, Width = 470, BackColor = Color.White, Padding = new Padding(10), AutoScroll = true };
    readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Fg };
    readonly NumericUpDown _px = Num(true), _py = Num(true), _pz = Num(true), _sx = Num(true), _sy = Num(true), _sz = Num(true), _trigger = Num(false), _maxNum = Num(false);
    readonly ComboBox _location = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly CheckBox _autoRevive = new() { AutoSize = true }, _initGen = new() { AutoSize = true }, _validOnce = new() { AutoSize = true };
    readonly DataGridView _grid = new()
    {
        Width = 440, Height = 220, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, MultiSelect = false,
    };
    readonly Label _gridHint = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(440, 0) };

    static NumericUpDown Num(bool dec) => new()
    {
        Minimum = dec ? -100000 : int.MinValue, Maximum = dec ? 100000 : int.MaxValue, DecimalPlaces = dec ? 2 : 0, Width = 100, ThousandsSeparator = !dec,
    };

    public NpcGenView(Func<int, Image?> icon, Func<int, (string, Color)?> info, Func<IWin32Window, Kind, int, int?> pick, Func<string?> elementDir, Func<int, string?> modelOf,
        Func<int, string?> blockOf, Action<int> editItem)
    {
        _blockOf = blockOf; _editItem = editItem;
        _icon = icon; _info = info; _pick = pick; _elementDir = elementDir; _modelOf = modelOf;
        BackColor = Bg; Dock = DockStyle.Fill;
        BuildBar(); BuildList(); BuildEditor();
        var split = new Panel { Dock = DockStyle.Fill };
        split.Controls.Add(_list); split.Controls.Add(_edit);
        Controls.Add(split); Controls.Add(_info2); Controls.Add(_bar);
        VisibleChanged += (_, _) => { if (Visible && _configDir is null) TryOpenServer(); };
        AppLang.Changed += () => { FillKindBox(); UpdateInfo(); ShowPoint(); };
    }

    // ---------- barra ----------
    void BuildBar()
    {
        Button B(string key, string pt, Action a, int w = 110)
        {
            var b = new Button { Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Fg };
            b.Click += (_, _) => a();
            AppLang.Changed += () => b.Text = Loc.T(key, pt);
            b.Text = Loc.T(key, pt);
            _bar.Controls.Add(b);
            return b;
        }
        B("npcgen.open", "Abrir pasta…", OpenFolder, 110);
        var save = B("npcgen.save", "Salvar", Save, 90);
        save.BackColor = Accent; save.ForeColor = Color.White;
        _bar.Controls.Add(new Label { Width = 8 });
        _bar.Controls.Add(_fileBox);
        _bar.Controls.Add(_kindBox);
        _bar.Controls.Add(_search);
        _bar.Controls.Add(new Label { Width = 8 });
        B("npcgen.new", "＋ Ponto", NewPoint, 90);
        B("npcgen.clone", "Clonar", ClonePoint, 80);
        B("npcgen.delete", "Excluir", DeletePoint, 80);
        _bar.Controls.Add(new Label { Width = 8 });
        B("gshop.undo", "↶ Desfazer", () => Undo(), 100);
        B("gshop.redo", "↷ Refazer", () => Redo(), 100);
        _bar.Controls.Add(new Label { Width = 8 });
        B("npcgen.world", "🌐 Mundo 3D", OpenWorld, 110);
        FillKindBox();
        _bar.Layout += (_, _) =>
        {
            if (_barSizing || _bar.Controls.Count == 0) return;
            int h = _bar.Controls.Cast<Control>().Max(c => c.Bottom) + _bar.Padding.Bottom + 2;
            if (h != _bar.Height) { _barSizing = true; _bar.Height = h; _barSizing = false; }
        };
        _fileBox.SelectedIndexChanged += (_, _) => { if (!_binding && _fileBox.SelectedItem is string s) SelectFile(s); };
        _kindBox.SelectedIndexChanged += (_, _) => { if (_binding) return; _kind = (Kind)_kindBox.SelectedIndex; Refill(); };
        _search.PlaceholderText = Loc.T("npcgen.search", "Procurar nome ou ID");
        _searchWait.Tick += (_, _) => { _searchWait.Stop(); Refill(); };
        _search.TextChanged += (_, _) => { _searchWait.Stop(); _searchWait.Start(); };
    }

    void FillKindBox()
    {
        _binding = true;
        int sel = (int)_kind;
        _kindBox.Items.Clear();
        _kindBox.Items.AddRange(new object[] { Loc.T("npcgen.kindMonster", "Pontos de monstro"), Loc.T("npcgen.kindNpc", "Pontos de NPC"), Loc.T("npcgen.kindMine", "Coletáveis") });
        _kindBox.SelectedIndex = sel;
        _search.PlaceholderText = Loc.T("npcgen.search", "Procurar nome ou ID");
        _binding = false;
    }

    // ---------- arquivos ----------
    /// <summary>Pasta gamed\config do servidor (lembrada; senão procurada a partir da pasta do servidor da aba Verificação).</summary>
    void TryOpenServer()
    {
        var dir = LoadSetting("npcgen_server");
        if (dir is null || !Directory.Exists(dir))
            dir = FindConfig(LoadSetting("check_server"));
        if (dir is not null) Open(dir);
        else _info2.Text = Loc.T("npcgen.noServer", "Use \"Abrir pasta…\" e escolha a pasta do servidor (home, gamed\\config ou a pasta com o npcgen.data).");
    }

    static string? FindConfig(string? start)
    {
        if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) return null;
        foreach (var c in new[] { start, Path.Combine(start, "config"), Path.Combine(start, "gamed", "config"), Path.Combine(start, "home", "gamed", "config") })
        {
            if (File.Exists(Path.Combine(c, "world", "npcgen.data"))) return c;
            if (File.Exists(Path.Combine(c, "npcgen.data")) && Path.GetFileName(c).Equals("world", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(c);
        }
        if (File.Exists(Path.Combine(start, "npcgen.data"))) return start;
        return null;
    }

    void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("npcgen.pickDir", "Pasta do servidor (home, gamed\\config ou a pasta com o npcgen.data)") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (FindConfig(dlg.SelectedPath) is not { } dir)
        {
            MessageBox.Show(this, Loc.T("npcgen.notFound", "Não achei o npcgen.data nessa pasta."), "NPCGen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Open(dir);
    }

    void Open(string dir)
    {
        if (_dirty.Count > 0 && MessageBox.Show(this, Loc.T("npcgen.discard", "Há mudanças no npcgen não salvas. Descartar?"), "NPCGen", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _paths.Clear(); _files.Clear(); _dirty.Clear(); _undo.Clear(); _redo.Clear();
        // mundo (world) e instâncias (aNN, bNN…); se a pasta já é a do arquivo, só ele
        if (File.Exists(Path.Combine(dir, "world", "npcgen.data"))) _paths["world"] = Path.Combine(dir, "world", "npcgen.data");
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(d => d))
            if (File.Exists(Path.Combine(sub, "npcgen.data")) && !Path.GetFileName(sub).Equals("world", StringComparison.OrdinalIgnoreCase))
                _paths[Path.GetFileName(sub)] = Path.Combine(sub, "npcgen.data");
        if (_paths.Count == 0 && File.Exists(Path.Combine(dir, "npcgen.data"))) _paths[Path.GetFileName(dir)] = Path.Combine(dir, "npcgen.data");
        _configDir = dir;
        SaveSetting("npcgen_server", dir);
        _binding = true;
        _fileBox.Items.Clear();
        foreach (var k in _paths.Keys) _fileBox.Items.Add(k);
        _binding = false;
        if (_fileBox.Items.Count > 0) _fileBox.SelectedIndex = 0;
        else { _cur = null; Refill(); }
    }

    NpcGenFile? Load(string name)
    {
        if (_files.TryGetValue(name, out var f)) return f;
        if (!_paths.TryGetValue(name, out var p)) return null;
        try { return _files[name] = NpcGenFile.Read(File.ReadAllBytes(p)); }
        catch (Exception ex) { MessageBox.Show(this, $"{p}\n\n{ex.Message}", Loc.T("npcgen.openFail", "Não consegui abrir o npcgen.data"), MessageBoxButtons.OK, MessageBoxIcon.Error); return null; }
    }

    void SelectFile(string name)
    {
        _cur = Load(name) is null ? null : name;
        Refill();
    }

    NpcGenFile? Cur => _cur is null ? null : _files.GetValueOrDefault(_cur);

    /// <summary>Todos os npcgen do servidor (mundo e instâncias), para quem troca IDs fora da aba (Mudar ID).</summary>
    public IEnumerable<(string Name, NpcGenFile File)> AllFiles()
    {
        if (_configDir is null) TryOpenServer();
        foreach (var k in _paths.Keys.ToList())
            if (Load(k) is { } f) yield return (k, f);
    }

    /// <summary>Um arquivo foi alterado por fora (Mudar ID): marca como não salvo e redesenha.</summary>
    public void ExternalEdit(string name)
    {
        _dirty.Add(name);
        if (name == _cur) { _list.Invalidate(); ShowPoint(); }
        UpdateInfo();
    }

    public bool HasUnsaved => _dirty.Count > 0;

    void UpdateInfo()
    {
        if (Cur is not { } f) { _info2.Text = _configDir is null ? _info2.Text : Loc.T("npcgen.none", "Nenhum npcgen aberto."); return; }
        _info2.Text = string.Format(Loc.T("npcgen.info", "{0} · versão {1} · {2} pontos de monstro/NPC · {3} de coleta · {4} gatilhos · mostrando {5}{6}"),
            _paths[_cur!], f.Version, f.Spawns.Count, f.Resources.Count, f.Triggers.Count, _rows.Count,
            _dirty.Count > 0 ? "  ·  " + string.Format(Loc.T("npcgen.unsaved", "{0} arquivo(s) não salvo(s)"), _dirty.Count) : "");
    }

    void Save() { if (_dirty.Count == 0) MessageBox.Show(this, Loc.T("npcgen.nothing", "Nada mudou no npcgen."), "NPCGen"); else SaveAll(false); }

    /// <summary>Grava todos os npcgen alterados (Salvar tudo / salvar automático); devolve os arquivos gravados.</summary>
    public List<string> SaveAll(bool quiet)
    {
        var saved = new List<string>();
        if (_dirty.Count == 0) return saved;
        try
        {
            foreach (var k in _dirty.ToList())
            {
                AtomicFileSave.Write(_paths[k], _files[k].Write());
                _dirty.Remove(k); saved.Add(_paths[k]);
            }
            if (!quiet) MessageBox.Show(this, string.Format(Loc.T("npcgen.saved", "Salvo (com backup):\n{0}\n\nCopie para o servidor e reinicie o mapa (gs) para valer."), string.Join("\n", saved)), "NPCGen");
        }
        catch (Exception ex) { if (!quiet) MessageBox.Show(this, ex.Message, Loc.T("npcgen.saveFail", "Falha ao salvar o npcgen"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        UpdateInfo();
        return saved;
    }

    static string SettingFile(string k) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", k + ".txt");
    static string? LoadSetting(string k) { try { return File.Exists(SettingFile(k)) ? File.ReadAllText(SettingFile(k)).Trim() : null; } catch { return null; } }
    static void SaveSetting(string k, string v) { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingFile(k))!); File.WriteAllText(SettingFile(k), v); } catch { } }

    // ---------- lista ----------
    void BuildList()
    {
        _list.SmallImageList = _icons;
        void Col(string key, string pt, int w, HorizontalAlignment a = HorizontalAlignment.Left)
        {
            var c = new ColumnHeader { Width = w, TextAlign = a };
            AppLang.Changed += () => c.Text = Loc.T(key, pt);
            c.Text = Loc.T(key, pt);
            _list.Columns.Add(c);
        }
        Col("npcgen.colName", "Monstro / NPC", 260);
        Col("npcgen.colAmount", "Qtde", 55, HorizontalAlignment.Right);
        Col("npcgen.colRespawn", "Renasce (s)", 80, HorizontalAlignment.Right);
        Col("npcgen.colPos", "Posição (X, altura, Z)", 180);
        Col("npcgen.colSpread", "Raio", 70, HorizontalAlignment.Right);
        Col("npcgen.colTrigger", "Gatilho", 60, HorizontalAlignment.Right);
        Col("npcgen.colIndex", "Nº", 55, HorizontalAlignment.Right);
        _list.RetrieveVirtualItem += (_, e) => e.Item = RowItem(e.ItemIndex);
        _list.SelectedIndexChanged += (_, _) => ShowPoint();
        // com o Mundo 3D aberto, duplo clique leva a câmera até o ponto
        _list.DoubleClick += (_, _) => { if (_world is { IsDisposed: false } w && Cur is { } f && SelectedIndex() is { } i) { w.GoTo(ListOf(f)[i]!); w.Activate(); } };
    }

    (int Id, int Amount, int Respawn, int Groups)? First(int i)
    {
        if (Cur is not { } f) return null;
        if (_kind == Kind.Mine)
        {
            var r = f.Resources[i];
            return r.Items.Count == 0 ? (0, 0, 0, 0) : (r.Items[0].Id, r.Items.Sum(x => x.Amount), r.Items[0].Respawn, r.Items.Count);
        }
        var s = f.Spawns[i];
        return s.Creatures.Count == 0 ? (0, 0, 0, 0) : (s.Creatures[0].Id, s.Creatures.Sum(x => x.Amount), s.Creatures[0].Respawn, s.Creatures.Count);
    }

    string NameOf(int id) => id == 0 ? "—" : _info(id) is { } h ? $"{h.Name}  ({id})" : $"? ({id})";

    int IconIdx(int id)
    {
        if (_iconIndex.TryGetValue(id, out var k)) return k;
        var img = id > 0 ? _icon(id) : null;
        k = -1;
        if (img is not null) { _icons.Images.Add(img); k = _icons.Images.Count - 1; }
        return _iconIndex[id] = k;
    }

    ListViewItem RowItem(int row)
    {
        if (Cur is not { } f || row >= _rows.Count) return new ListViewItem("");
        int i = _rows[row];
        var x = First(i)!.Value;
        Vec3 pos; string spread; int trig;
        if (_kind == Kind.Mine) { var r = f.Resources[i]; pos = r.Position; spread = $"{r.SpreadX:0.#}×{r.SpreadZ:0.#}"; trig = r.Trigger; }
        else { var s = f.Spawns[i]; pos = s.Position; spread = $"{s.Spread.X:0.#}×{s.Spread.Z:0.#}"; trig = s.Trigger; }
        var name = NameOf(x.Id) + (x.Groups > 1 ? "  " + string.Format(Loc.T("npcgen.more", "+{0}"), x.Groups - 1) : "");
        var it = new ListViewItem(name, IconIdx(x.Id));
        it.SubItems.Add(x.Amount.ToString());
        it.SubItems.Add(x.Respawn.ToString());
        it.SubItems.Add(pos.ToString());
        it.SubItems.Add(spread);
        it.SubItems.Add(trig == 0 ? "" : trig.ToString());
        it.SubItems.Add(i.ToString());
        return it;
    }

    void Refill(int? keep = null)
    {
        int? prev = keep ?? SelectedIndex();
        _rows = new();
        if (Cur is { } f)
        {
            var q = _search.Text.Trim();
            bool Match(IEnumerable<int> ids) => q.Length == 0 || ids.Any(id => id.ToString() == q || (_info(id) is { } h && h.Name.Contains(q, StringComparison.OrdinalIgnoreCase)));
            if (_kind == Kind.Mine)
            {
                for (int i = 0; i < f.Resources.Count; i++) if (Match(f.Resources[i].Items.Select(x => x.Id))) _rows.Add(i);
            }
            else
            {
                int type = _kind == Kind.Npc ? 1 : 0;
                for (int i = 0; i < f.Spawns.Count; i++) if (f.Spawns[i].Type == type && Match(f.Spawns[i].Creatures.Select(x => x.Id))) _rows.Add(i);
            }
        }
        _list.VirtualListSize = _rows.Count;
        _list.SelectedIndices.Clear();
        int at = prev is { } p ? _rows.IndexOf(p) : -1;
        if (at < 0 && _rows.Count > 0) at = 0;
        if (at >= 0) { _list.SelectedIndices.Add(at); _list.EnsureVisible(at); }
        _list.Invalidate();
        ShowPoint(); UpdateInfo();
        if (_world is { IsDisposed: false }) _world.RefreshMarkers();
    }

    int? SelectedIndex() => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

    // ---------- editor do ponto ----------
    void BuildEditor()
    {
        int y = 8;
        void Add(Control c, int x, int w = 0) { c.Location = new Point(x + 12, y);  /* 12 px da borda */ if (w > 0) c.Width = w; _edit.Controls.Add(c); }
        Label L(string key, string pt) { var l = new Label { AutoSize = true, ForeColor = Muted }; AppLang.Changed += () => l.Text = Loc.T(key, pt); l.Text = Loc.T(key, pt); return l; }
        Add(_title, 0); y += 34;
        Add(L("npcgen.pos", "Posição  X / altura / Z"), 0); y += 20;
        Add(_px, 0); Add(_py, 110); Add(_pz, 220); y += 32;
        Add(L("npcgen.spread", "Raio de nascimento  X / altura / Z"), 0); y += 20;
        Add(_sx, 0); Add(_sy, 110); Add(_sz, 220); y += 32;
        Add(L("npcgen.location", "Nasce"), 0); Add(L("npcgen.trigger", "Gatilho"), 160); Add(L("npcgen.maxNum", "Máx. vivos"), 270); y += 20;
        Add(_location, 0); Add(_trigger, 160); Add(_maxNum, 270); y += 32;
        Add(_autoRevive, 0); y += 24; Add(_initGen, 0); y += 24; Add(_validOnce, 0); y += 32;
        Add(L("npcgen.group", "Quem nasce aqui"), 0); y += 20;
        Add(_grid, 0); y += 226;
        var bar = new FlowLayoutPanel { Location = new Point(12, y), Width = 440, Height = 34, WrapContents = false };
        Button B(string key, string pt, Action a, int w)
        {
            var b = new Button { Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Fg };
            b.Click += (_, _) => a();
            AppLang.Changed += () => b.Text = Loc.T(key, pt);
            b.Text = Loc.T(key, pt);
            bar.Controls.Add(b);
            return b;
        }
        B("npcgen.addMob", "＋ Adicionar", AddCreature, 110);
        B("npcgen.swapMob", "Trocar…", () => SwapCreature(_grid.CurrentRow?.Index ?? -1), 90);
        B("npcgen.delMob", "Remover", RemoveCreature, 90);
        _edit.Controls.Add(bar); y += 38;
        Add(_gridHint, 0);
        AppLang.Changed += () => _gridHint.Text = Loc.T("npcgen.gridHint", "Duplo clique no nome troca o monstro. Quantidade e renascimento (em segundos) editam na própria tabela.");
        _gridHint.Text = Loc.T("npcgen.gridHint", "Duplo clique no nome troca o monstro. Quantidade e renascimento (em segundos) editam na própria tabela.");

        AppLang.Changed += () => { _autoRevive.Text = Loc.T("npcgen.autoRevive", "Renasce sozinho"); _initGen.Text = Loc.T("npcgen.initGen", "Nasce quando o mapa abre"); _validOnce.Text = Loc.T("npcgen.validOnce", "Nasce uma vez só"); };
        _autoRevive.Text = Loc.T("npcgen.autoRevive", "Renasce sozinho"); _initGen.Text = Loc.T("npcgen.initGen", "Nasce quando o mapa abre"); _validOnce.Text = Loc.T("npcgen.validOnce", "Nasce uma vez só");

        var img = new DataGridViewImageColumn { Width = 26, ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true };
        img.DefaultCellStyle.NullValue = null;   // monstro sem ícone: célula vazia (não o "x" padrão)
        _grid.Columns.Add(img);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 190, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 60 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 70 });
        void Heads()
        {
            _grid.Columns[1].HeaderText = Loc.T("npcgen.colName", "Monstro / NPC");
            _grid.Columns[2].HeaderText = Loc.T("npcgen.colAmount", "Qtde");
            _grid.Columns[3].HeaderText = Loc.T("npcgen.colRespawn", "Renasce (s)");
            _grid.Columns[4].HeaderText = Loc.T("npcgen.colPath", "Caminho");
        }
        Heads(); AppLang.Changed += Heads;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex <= 1) SwapCreature(e.RowIndex); };
        _grid.CellEndEdit += (_, e) => GridEdited(e.RowIndex, e.ColumnIndex);

        foreach (var n in new[] { _px, _py, _pz, _sx, _sy, _sz, _trigger, _maxNum }) n.ValueChanged += (_, _) => PointEdited(n);
        _location.SelectedIndexChanged += (_, _) => PointEdited(_location);
        foreach (var c in new[] { _autoRevive, _initGen, _validOnce }) c.CheckedChanged += (_, _) => PointEdited(c);
    }

    void ShowPoint()
    {
        _binding = true;
        try
        {
            var f = Cur; int? idx = SelectedIndex();
            _edit.Enabled = f is not null && idx is not null;
            _grid.Rows.Clear();
            if (f is null || idx is not { } i) { _title.Text = ""; return; }
            bool mine = _kind == Kind.Mine;
            _location.Items.Clear();
            _location.Items.AddRange(new object[] { Loc.T("npcgen.ground", "No chão"), Loc.T("npcgen.free", "Livre (ar/água)") });
            _sy.Enabled = !mine; _location.Enabled = !mine;
            if (mine)
            {
                var r = f.Resources[i];
                _title.Text = string.Format(Loc.T("npcgen.titleMine", "Ponto de coleta nº {0}"), i);
                Set(_px, r.Position.X); Set(_py, r.Position.Y); Set(_pz, r.Position.Z);
                Set(_sx, r.SpreadX); Set(_sy, 0); Set(_sz, r.SpreadZ);
                Set(_trigger, r.Trigger); Set(_maxNum, r.MaxNum);
                _location.SelectedIndex = 0;
                _autoRevive.Checked = r.AutoRevive != 0; _initGen.Checked = r.InitGen != 0; _validOnce.Checked = r.ValidOnce != 0;
                foreach (var x in r.Items) _grid.Rows.Add(IconImg(x.Id), NameOf(x.Id), x.Amount, x.Respawn, "");
            }
            else
            {
                var s = f.Spawns[i];
                _title.Text = string.Format(_kind == Kind.Npc ? Loc.T("npcgen.titleNpc", "Ponto de NPC nº {0}") : Loc.T("npcgen.titleMob", "Ponto de monstro nº {0}"), i);
                Set(_px, s.Position.X); Set(_py, s.Position.Y); Set(_pz, s.Position.Z);
                Set(_sx, s.Spread.X); Set(_sy, s.Spread.Y); Set(_sz, s.Spread.Z);
                Set(_trigger, s.Trigger); Set(_maxNum, s.MaxNum);
                _location.SelectedIndex = s.Location is 0 or 1 ? s.Location : 0;
                _autoRevive.Checked = s.AutoRevive != 0; _initGen.Checked = s.InitGen != 0; _validOnce.Checked = s.ValidOnce != 0;
                foreach (var c in s.Creatures) _grid.Rows.Add(IconImg(c.Id), NameOf(c.Id), c.Amount, c.Respawn, c.PathId);
            }
            _grid.Columns[4].Visible = !mine;
        }
        finally { _binding = false; }
    }

    Image? IconImg(int id) => IconIdx(id) is var k && k >= 0 ? _icons.Images[k] : null;

    static void Set(NumericUpDown n, decimal v) => n.Value = Math.Clamp(Math.Round(v, n.DecimalPlaces), n.Minimum, n.Maximum);
    static void Set(NumericUpDown n, float v) => Set(n, float.IsFinite(v) ? (decimal)v : 0m);
    static void Set(NumericUpDown n, int v) => Set(n, (decimal)v);

    // um campo do ponto mudou
    void PointEdited(Control src)
    {
        if (_binding || Cur is not { } f || SelectedIndex() is not { } i) return;
        Snapshot("pt:" + src.GetHashCode());
        float F(NumericUpDown n) => (float)n.Value;
        byte B(CheckBox c) => (byte)(c.Checked ? 1 : 0);
        if (_kind == Kind.Mine)
        {
            var r = f.Resources[i];
            // só grava o que mudou: um float que não foi tocado continua com os mesmos bits
            if (src == _px) r.Position.X = F(_px);
            if (src == _py) r.Position.Y = F(_py);
            if (src == _pz) r.Position.Z = F(_pz);
            if (src == _sx) r.SpreadX = F(_sx);
            if (src == _sz) r.SpreadZ = F(_sz);
            if (src == _trigger) r.Trigger = (int)_trigger.Value;
            if (src == _maxNum) r.MaxNum = (int)_maxNum.Value;
            if (src == _autoRevive) r.AutoRevive = B(_autoRevive);
            if (src == _initGen) r.InitGen = B(_initGen);
            if (src == _validOnce) r.ValidOnce = B(_validOnce);
        }
        else
        {
            var s = f.Spawns[i];
            if (src == _px) s.Position.X = F(_px);
            if (src == _py) s.Position.Y = F(_py);
            if (src == _pz) s.Position.Z = F(_pz);
            if (src == _sx) s.Spread.X = F(_sx);
            if (src == _sy) s.Spread.Y = F(_sy);
            if (src == _sz) s.Spread.Z = F(_sz);
            if (src == _trigger) s.Trigger = (int)_trigger.Value;
            if (src == _maxNum) s.MaxNum = (int)_maxNum.Value;
            if (src == _location) s.Location = _location.SelectedIndex;
            if (src == _autoRevive) s.AutoRevive = B(_autoRevive);
            if (src == _initGen) s.InitGen = B(_initGen);
            if (src == _validOnce) s.ValidOnce = B(_validOnce);
        }
        Touch();
    }

    void GridEdited(int row, int col)
    {
        if (_binding || Cur is not { } f || SelectedIndex() is not { } i || row < 0) return;
        var txt = _grid.Rows[row].Cells[col].Value?.ToString()?.Trim() ?? "";
        if (!int.TryParse(txt, out var v) || (col == 2 && v < 0))
        {
            ShowPoint();
            return;
        }
        Snapshot($"grid:{row}:{col}:{DateTime.Now.Ticks}");
        if (_kind == Kind.Mine)
        {
            var x = f.Resources[i].Items[row];
            if (col == 2) x.Amount = v; else if (col == 3) x.Respawn = v;
        }
        else
        {
            var c = f.Spawns[i].Creatures[row];
            if (col == 2) c.Amount = v; else if (col == 3) c.Respawn = v; else if (col == 4) c.PathId = v;
        }
        Touch();
    }

    void SwapCreature(int row)
    {
        if (Cur is not { } f || SelectedIndex() is not { } i || row < 0) return;
        int cur = _kind == Kind.Mine ? f.Resources[i].Items[row].Id : f.Spawns[i].Creatures[row].Id;
        if (_pick(PickOwner, _kind, cur) is not { } id || id == cur) return;
        Snapshot("swap");
        if (_kind == Kind.Mine) f.Resources[i].Items[row].Id = id; else f.Spawns[i].Creatures[row].Id = id;
        Touch(); ShowPoint();
        if (row < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[row].Cells[1];
    }

    void AddCreature()
    {
        if (Cur is not { } f || SelectedIndex() is not { } i) return;
        if (_pick(PickOwner, _kind, 0) is not { } id) return;
        Snapshot("add");
        if (_kind == Kind.Mine)
        {
            var r = f.Resources[i];
            var c = r.Items.LastOrDefault()?.Clone() ?? new NpcGenResourceItem { Amount = 1, Respawn = 30 };
            c.Id = id;
            r.Items.Add(c);
        }
        else
        {
            var s = f.Spawns[i];
            // copia o último do ponto (agressividade, facção…), só troca o ID
            var c = s.Creatures.LastOrDefault()?.Clone() ?? new NpcGenCreature();
            c.Id = id;
            s.Creatures.Add(c);
        }
        Touch(); ShowPoint();
    }

    void RemoveCreature()
    {
        if (Cur is not { } f || SelectedIndex() is not { } i || _grid.CurrentRow is not { } gr) return;
        int n = _kind == Kind.Mine ? f.Resources[i].Items.Count : f.Spawns[i].Creatures.Count;
        if (n <= 1) { MessageBox.Show(this, Loc.T("npcgen.lastOne", "O ponto precisa de pelo menos um. Para tirar o ponto inteiro use \"Excluir\"."), "NPCGen"); return; }
        Snapshot("remove");
        if (_kind == Kind.Mine) f.Resources[i].Items.RemoveAt(gr.Index); else f.Spawns[i].Creatures.RemoveAt(gr.Index);
        Touch(); ShowPoint();
    }

    // ---------- mundo 3D ----------
    /// <summary>Abre o Mundo 3D do arquivo aberto (ou leva a câmera ao ponto selecionado, se já aberto).</summary>
    public void OpenWorld() => OpenWorld(false);

    // fromSwitch: veio da troca de mapa no 3D — a câmera vai para a entrada, não para o ponto selecionado da lista
    void OpenWorld(bool fromSwitch)
    {
        if (Cur is not { } f || _cur is null) return;
        var dir = _elementDir() is { } el ? Path.Combine(el, "maps", _cur) : null;
        if (dir is null || TerrainFile.Open(dir) is not { } t)
        {
            MessageBox.Show(this, string.Format(Loc.T("world.noMap", "Não achei o terreno do mapa \"{0}\" no cliente (element\\maps\\{0}). Abra a pasta do jogo na aba Elements."), _cur), "NPCGen");
            return;
        }
        object? Sel() => SelectedIndex() is { } i ? ListOf(f)[i] : null;
        // sem seleção: mundo = centro do mapa visto de cima; instância = entrada (portais dos region.sev do servidor;
        // sem eles, o 1º NPC, que costuma ser o teleporte da entrada)
        var selected = fromSwitch ? null : Sel();
        Vec3? overview = null;
        if (selected is null)
            overview = _cur.Equals("world", StringComparison.OrdinalIgnoreCase) ? new Vec3(0, 0, 0)
                : (_configDir is null ? null : RegionFile.EntranceOf(_configDir, _cur))
                  ?? (f.Spawns.FirstOrDefault(s => s.Type == 1) ?? f.Spawns.FirstOrDefault())?.Position ?? new Vec3(0, 0, 0);
        var start = selected switch { NpcGenSpawn s => s.Position, NpcGenResource r => r.Position, _ => overview ?? default };
        // já aberto (mesmo mapa): só leva a câmera ao ponto selecionado
        if (_world is { IsDisposed: false } open && open.Tag as string == _cur)
        {
            if (SelectedIndex() is { } si) open.GoTo(ListOf(f)[si]!);
            open.Activate();
            return;
        }
        if (_world is { IsDisposed: false }) _world.Close();
        var w = _world = new WorldView(t, new OpenTK.Mathematics.Vector3(start.X, start.Y, start.Z), () =>
            {
                var list = new List<WorldView.Marker>(f.Spawns.Count + f.Resources.Count);
                // cor do nome (legenda): verde = evento com data/hora, lilás = gatilho sem agenda (fase de DG, GM),
                // vermelho = guarda (NPC em ponto de monstro), ciano = coletável
                var triggers = f.Triggers.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
                // gatilho que liga sozinho com o mapa e sem agenda = sempre presente (cidades); agenda = evento; senão = fase/GM
                Color TriggerColor(int id, Color normal) => id == 0 || !triggers.TryGetValue(id, out var tg) ? normal
                    : tg.NoStartSchedule == 0 && tg.Start.Any(x => x > 0) ? Color.LimeGreen
                    : tg.AutoStart != 0 ? normal : Color.Violet;
                foreach (var s in f.Spawns)
                {
                    int id0 = s.Creatures.FirstOrDefault()?.Id ?? 0;
                    var label = TriggerColor(s.Trigger, s.Type == 0 && _blockOf(id0) == "NPC_ESSENCE" ? Color.OrangeRed : Color.White);
                    list.Add(new(s.Position.X, s.Position.Y, s.Position.Z, s.Type == 1 ? Color.DeepSkyBlue : Color.Red, NameOf(id0), s,
                        _modelOf(id0), MathF.Atan2(s.Direction.X, s.Direction.Z) * 180 / MathF.PI, label, s.Location == 0, id0));
                }
                foreach (var r in f.Resources)
                {
                    int id0 = r.Items.FirstOrDefault()?.Id ?? 0;
                    list.Add(new(r.Position.X, r.Position.Y, r.Position.Z, Color.LimeGreen, NameOf(id0), r, _modelOf(id0), 0,
                        TriggerColor(r.Trigger, Color.Cyan), true, id0));
                }
                return list;
            }, Sel, SelectObject,
            (g, kind) =>
            {
                _pickOwner = _world;
                try { SetKind((Kind)kind); NewPoint(new Vec3(g.X, g.Y, g.Z)); }
                finally { _pickOwner = null; _world?.Activate(); }
            },
            (o, g) => { SelectObject(o); Snapshot("move3d:" + DateTime.Now.Ticks); if (o is NpcGenSpawn s) s.Position = new Vec3(g.X, g.Y, g.Z); else if (o is NpcGenResource r) r.Position = new Vec3(g.X, g.Y, g.Z); Touch(); ShowPoint(); });
        w.Tag = _cur;
        w.CloneHere = (o, g) =>
        {
            SelectObject(o);
            if (Cur is not { } cf || SelectedIndex() is not { } ci) return;
            var copy = CloneOf(ListOf(cf)[ci]!);
            if (copy is NpcGenSpawn cs) cs.Position = new Vec3(g.X, g.Y, g.Z); else if (copy is NpcGenResource cr) cr.Position = new Vec3(g.X, g.Y, g.Z);
            InsertPoint(ci + 1, copy);
        };
        w.EditPoint = o => { SelectObject(o); int id = o is NpcGenSpawn es ? es.Creatures.FirstOrDefault()?.Id ?? 0 : ((NpcGenResource)o).Items.FirstOrDefault()?.Id ?? 0; if (id > 0) _editItem(id); };
        w.UndoAction = Undo; w.RedoAction = Redo;
        w.CreateNewHere = (g, kind) =>
        {
            if (CreateCreature is null) return;
            _pickOwner = w;
            try
            {
                if (CreateCreature(w, (Kind)kind) is not int id) return;
                SetKind((Kind)kind); NewPoint(new Vec3(g.X, g.Y, g.Z), id);
            }
            finally { _pickOwner = null; w.Activate(); w.RefreshMarkers(); }
        };
        w.DeletePoint = o => { SelectObject(o); DeletePoint(); };
        w.Maps = _paths.Keys.ToList();
        w.SwitchMap = name => { if (_paths.ContainsKey(name)) { _fileBox.SelectedItem = name; OpenWorld(true); } };
        if (overview is { } ov) w.Overview(new OpenTK.Mathematics.Vector3(ov.X, ov.Y, ov.Z), _cur.Equals("world", StringComparison.OrdinalIgnoreCase));
        if (_configDir is not null) w.WaterDir = Path.Combine(_configDir, _cur, "watermap");
        w.FormClosed += (_, _) => { if (_world == w) _world = null; };
        // sem dono: "Editar no software" consegue trazer o editor para a frente; fecha junto com o editor
        if (FindForm() is { } host) host.FormClosed += (_, _) => { if (!w.IsDisposed) w.Close(); };
        w.Show();
    }

    WorldView? _world;

    /// <summary>Assistente do editor: cria um monstro/NPC novo no elements e devolve o ID (null = cancelado).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<IWin32Window, Kind, int?>? CreateCreature { get; set; }
    IWin32Window? _pickOwner;   // seletor aberto pelo Mundo 3D: fica na frente dele (e não traz o editor)
    IWin32Window PickOwner => _pickOwner ?? FindForm() ?? (IWin32Window)this;

    void SetKind(Kind k)
    {
        if (k == _kind) return;
        _kind = k; _binding = true; _kindBox.SelectedIndex = (int)k; _binding = false;
        Refill(-1);
    }

    // seleciona na lista o ponto clicado no mundo (troca o filtro se precisar)
    void SelectObject(object o)
    {
        if (Cur is not { } f) return;
        Kind k = o is NpcGenResource ? Kind.Mine : ((NpcGenSpawn)o).Type == 1 ? Kind.Npc : Kind.Monster;
        int idx = o is NpcGenResource r ? f.Resources.IndexOf(r) : f.Spawns.IndexOf((NpcGenSpawn)o);
        if (idx < 0) return;
        if (k != _kind) { _kind = k; _binding = true; _kindBox.SelectedIndex = (int)k; _binding = false; }
        if (_search.Text.Length > 0) { _binding = true; _search.Text = ""; _binding = false; }
        Refill(idx);
    }

    // ---------- pontos ----------
    IList ListOf(NpcGenFile f) => _kind == Kind.Mine ? f.Resources : f.Spawns;
    object CloneOf(object o) => o is NpcGenSpawn s ? s.Clone() : ((NpcGenResource)o).Clone();

    void NewPoint() => NewPoint(null);

    // at: posição escolhida no mundo 3D (senão nasce no lugar do selecionado); givenId: já escolhido (assistente)
    void NewPoint(Vec3? at, int? givenId = null)
    {
        if (Cur is not { } f) return;
        if ((givenId ?? _pick(PickOwner, _kind, 0)) is not { } id) return;
        object p;
        var near = SelectedIndex() is { } si ? ListOf(f)[si] : null;   // nasce no mesmo lugar do selecionado
        if (_kind == Kind.Mine)
        {
            var r = near is NpcGenResource nr ? nr.Clone() : new NpcGenResource { SpreadX = 5, SpreadZ = 5 };
            r.Items.Clear(); r.Items.Add(new NpcGenResourceItem { Id = id, Amount = 1, Respawn = 30 });
            r.Trigger = 0;
            if (at is { } a1) r.Position = a1;
            p = r;
        }
        else
        {
            var s = near is NpcGenSpawn ns ? ns.Clone() : new NpcGenSpawn { Type = _kind == Kind.Npc ? 1 : 0, Direction = new Vec3(0, 0, 1) };
            var c = s.Creatures.FirstOrDefault()?.Clone() ?? new NpcGenCreature();
            c.Id = id; c.Amount = 1; if (c.Respawn <= 0) c.Respawn = 30;
            s.Creatures.Clear(); s.Creatures.Add(c);
            s.Trigger = 0;
            if (at is { } a2) { s.Position = a2; s.Location = 0; }   // clicado no chão: nasce no chão
            p = s;
        }
        InsertPoint(ListOf(f).Count, p);
    }

    void ClonePoint()
    {
        if (Cur is not { } f || SelectedIndex() is not { } i) return;
        InsertPoint(i + 1, CloneOf(ListOf(f)[i]!));
    }

    void InsertPoint(int at, object p)
    {
        var f = Cur!;
        Push(_undo, new Op(_cur!, _kind == Kind.Mine, OpKind.Remove, at, null));
        _redo.Clear();
        ListOf(f).Insert(at, p);
        _dirty.Add(_cur!);
        Refill(at);
    }

    void DeletePoint()
    {
        if (Cur is not { } f || SelectedIndex() is not { } i) return;
        var l = ListOf(f);
        Push(_undo, new Op(_cur!, _kind == Kind.Mine, OpKind.Insert, i, l[i]));
        _redo.Clear();
        l.RemoveAt(i);
        _dirty.Add(_cur!);
        int next = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : 0;
        Refill(-1);
        if (_rows.Count > 0) { _list.SelectedIndices.Clear(); int k = Math.Min(next, _rows.Count - 1); _list.SelectedIndices.Add(k); _list.EnsureVisible(k); }
    }

    void Touch() { if (_cur is not null) _dirty.Add(_cur); UpdateInfo(); _list.Invalidate(); _world?.MarkDirty(); }

    // ---------- desfazer / refazer ----------
    // cada passo diz como voltar: trocar o ponto pela cópia, inserir de volta ou remover
    enum OpKind { Replace, Insert, Remove }
    sealed record Op(string File, bool Mine, OpKind Kind, int Index, object? Obj);
    readonly List<Op> _undo = new(), _redo = new();
    string? _lastKey; DateTime _lastPush;

    static void Push(List<Op> l, Op o) { l.Add(o); if (l.Count > 100) l.RemoveAt(0); }

    /// <summary>Guarda o ponto antes de mudar. Mudanças seguidas no mesmo campo viram um passo só.</summary>
    void Snapshot(string key)
    {
        if (Cur is not { } f || SelectedIndex() is not { } i) return;
        var now = DateTime.Now;
        if (key == _lastKey && (now - _lastPush).TotalSeconds < 2) { _lastPush = now; return; }
        Push(_undo, new Op(_cur!, _kind == Kind.Mine, OpKind.Replace, i, CloneOf(ListOf(f)[i]!)));
        _redo.Clear(); _lastKey = key; _lastPush = now;
    }

    public bool Undo() => Apply(_undo, _redo);
    public bool Redo() => Apply(_redo, _undo);

    bool Apply(List<Op> from, List<Op> to)
    {
        if (from.Count == 0) return false;
        var op = from[^1]; from.RemoveAt(from.Count - 1);
        var f = Load(op.File)!;
        IList l = op.Mine ? f.Resources : f.Spawns;
        switch (op.Kind)
        {
            case OpKind.Replace: Push(to, op with { Obj = l[op.Index] }); l[op.Index] = op.Obj; break;
            case OpKind.Insert: Push(to, op with { Kind = OpKind.Remove, Obj = null }); l.Insert(op.Index, op.Obj); break;
            case OpKind.Remove: Push(to, op with { Kind = OpKind.Insert, Obj = l[op.Index] }); l.RemoveAt(op.Index); break;
        }
        _dirty.Add(op.File); _lastKey = null;
        // mostra o ponto que voltou
        if (op.File != _cur) { _binding = true; _fileBox.SelectedItem = op.File; _binding = false; _cur = op.File; }
        if (op.Mine != (_kind == Kind.Mine) || (!op.Mine && op.Kind != OpKind.Remove && l.Count > op.Index && ((NpcGenSpawn)l[op.Index]!).Type != (_kind == Kind.Npc ? 1 : 0)))
        {
            _kind = op.Mine ? Kind.Mine : l.Count > op.Index && ((NpcGenSpawn)l[op.Index]!).Type == 1 ? Kind.Npc : Kind.Monster;
            _binding = true; _kindBox.SelectedIndex = (int)_kind; _binding = false;
        }
        Refill(Math.Min(op.Index, l.Count - 1));
        return true;
    }
}
