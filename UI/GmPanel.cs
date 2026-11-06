using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Painel do GM (docs/GM-PAINEL.md): programa à parte (MyAceEditor.exe --gm, ou o exe renomeado para MyAceGM.exe) que
/// age no personagem logado no jogo aberto, pelo console do jogo (GameConsole). Só lê o cliente (elements.data para
/// buscar itens/NPCs/monstros, surfaces.pck para ícones e mapa); não usa o servidor nem os dados do editor.
/// Números dos comandos: lista de comandos do console (triviapw) + EC_DlgCmdConsole.cpp (gm_*, d_goto) do source 1.5.3.
/// </summary>
public sealed class GmPanel : Form
{
    static string SettingFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "gm_element.txt");

    readonly TextBox _dir = new() { Dock = DockStyle.Fill };
    readonly ListBox _log = new() { Dock = DockStyle.Bottom, Height = 110, IntegralHeight = false };
    // páginas sem cabeçalho: a navegação lateral (NavBar) troca; o cartão de cima mostra o estado do jogo sempre
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(0, 1) };
    readonly StatusCard _card = new();
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    Module? _data;
    IconAtlas? _icons;

    public GmPanel(string? elementDir)
    {
        Text = Loc.T("gm.title", "Painel do GM — MyAceEditor");
        Width = 980; Height = 720; StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        _dir.Text = elementDir ?? (File.Exists(SettingFile) ? File.ReadAllText(SettingFile).Trim() : "");

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 5, Padding = new Padding(6, 6, 6, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = Loc.T("gm.gameFolder", "Pasta do jogo (element)"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(_dir, 1, 0);
        top.Controls.Add(Btn(Loc.T("gm.browse", "Procurar…"), PickDir), 2, 0);
        top.Controls.Add(Btn(Loc.T("gm.launch", "▶ Abrir o jogo com console"), LaunchGame, blue: true), 3, 0);
        top.Controls.Add(Btn(Loc.T("gm.side", "◧ Lado a lado com o jogo"), ToggleSide), 4, 0);

        // ordem do uso: o próprio personagem, ir a algum lugar, criar coisas, cuidar de jogadores, eventos do mundo;
        // cada aba diz no topo do que precisa (jogo aberto com console / servidor neste PC)
        var needGame = Loc.T("gm.needGame", "Precisa do jogo aberto (▶ Abrir o jogo com console) e logado com o personagem GM. Deixe o console do jogo fechado.");
        _tabs.TabPages.Add(Page("👤 " + Loc.T("gm.tabMe", "Meu personagem"), MeTab(), needGame));
        var mapPage = Page("🗺 " + Loc.T("gm.tabMap2", "Mapa e teleporte"), MapTab(), needGame);
        _tabs.TabPages.Add(mapPage);
        _tabs.TabPages.Add(Page("🎒 " + Loc.T("gm.tabItems", "Itens"), SearchTab(Kind.Item), needGame));
        _tabs.TabPages.Add(Page("👹 " + Loc.T("gm.tabNpcs", "NPCs e monstros"), SearchTab(Kind.Npc), needGame));
        _tabs.TabPages.Add(Page("👥 " + Loc.T("gm.tabPlayers", "Jogadores"), PlayersTab(), Loc.T("gm.needPlayers", "Ações pelo console do jogo (conta GM). Lista de quem está online e aviso geral usam o servidor quando ele roda neste PC.")));
        _tabs.TabPages.Add(Page("🌍 " + Loc.T("gm.tabWorld2", "Mundo e eventos"), WorldTab(), Loc.T("gm.needWorld", "Eventos e aviso geral vão pelo servidor quando ele roda neste PC; sem ele, use o Ctrl+G dentro do jogo.")));
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab == mapPage) LoadMap(); };

        var nav = new NavBar(_tabs);
        string[] glyphs = { "\uE77B", "\uE707", "\uE7B8", "\uE7EE", "\uE716", "\uE774" };   // eu, mapa, itens, monstros, jogadores, mundo
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            var page = _tabs.TabPages[i];
            // o texto da aba traz um emoji na frente: na barra vale o ícone desenhado
            string Name() { var t = page.Text; int k = 0; while (k < t.Length && !char.IsLetterOrDigit(t[k])) k++; return t[k..]; }
            nav.Add(i < glyphs.Length ? glyphs[i] : "\uE7FC", Name, Name, page);
        }
        Controls.Add(_tabs); Controls.Add(nav); Controls.Add(_log); Controls.Add(_card); Controls.Add(top);
        _poll.Tick += (_, _) => UpdateState(); _poll.Start();
        Shown += (_, _) => { ApplyCommandSet(); UpdateState(); LoadData(); };
        FormClosed += (_, _) => _poll.Dispose();
    }

    // ---------- comum ----------
    static TabPage Page(string text, Control c, string? need = null)
    {
        var p = new TabPage(text) { Padding = new Padding(8) };
        c.Dock = DockStyle.Fill; p.Controls.Add(c);
        if (need is not null)
            p.Controls.Add(new Label { Text = "ⓘ " + need, Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(71, 85, 105), BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(6, 5, 0, 0) });
        return p;
    }

    static Button Btn(string text, Action click, bool blue = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 8, 8), BackColor = Color.White };
        UiKit.Decorate(b, text, primary: blue);
        b.Click += (_, _) => click();
        return b;
    }

    // comando que nem todo servidor tem: some quando a versão do jogo aberto não aceita (PW-KB §S35)
    readonly List<(int Command, Control Item)> _versioned = [];
    T Need<T>(int command, T item) where T : Control { _versioned.Add((command, item)); return item; }
    void ApplyCommandSet()
    {
        var exe = Path.Combine(ElementDir, GameConsole.Exe);
        int? version = File.Exists(exe) ? ProgramVersion.ElementsVersion(exe) : null;
        foreach (var (command, item) in _versioned) item.Visible = GmCommands.Supported(version, command);
    }

    static NumericUpDown Num(decimal value, decimal min, decimal max, int width = 90) => new() { Minimum = min, Maximum = max, Value = value, Width = width };

    static FlowLayoutPanel Row(params Control[] cs)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
        foreach (var c in cs) { if (c is Label l) { l.AutoSize = true; l.Margin = new Padding(0, 7, 6, 0); } f.Controls.Add(c); }
        return f;
    }

    static Label L(string t) => new() { Text = t };

    static GroupBox Group(string title, params Control[] rows)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        foreach (var r in rows.Reverse()) g.Controls.Add(r);
        return g;
    }

    static Panel Stack(params Control[] groups)
    {
        var p = new Panel { AutoScroll = true };
        foreach (var g in groups.Reverse()) p.Controls.Add(g);
        return p;
    }

    Rectangle? _before;

    /// <summary>
    /// Opção A do GM: o jogo encosta à esquerda e o painel fica no espaço à direita, sempre por cima, para o clique no jogo
    /// não esconder o painel. Um segundo clique volta como estava. O tamanho do jogo não muda: ele não aceita bem mudar de tamanho aberto.
    /// </summary>
    void ToggleSide()
    {
        if (_before is { } was) { TopMost = false; Bounds = was; _before = null; return; }
        var area = Screen.FromControl(this).WorkingArea;
        if (GameConsole.MoveToCorner(area) is not { } right) { AddLog("⚠ " + Loc.T("gm.sideNoGame", "Abra o jogo primeiro: não achei a janela dele.")); return; }
        const int MinWidth = 460;
        _before = Bounds;
        int left = Math.Min(right, area.Right - MinWidth);
        TopMost = true;
        Bounds = new Rectangle(left, area.Top, area.Right - left, area.Height);
    }

    bool Ask(string text) => MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Manda as linhas ao console do jogo numa abertura só (fora da thread da tela; o jogo fica com o foco).</summary>
    void Run(params string[] lines)
    {
        AddLog("→ " + lines[0] + (lines.Length > 1 ? $"  (× {lines.Length})" : ""));
        Task.Run(() =>
        {
            bool ok;
            lock (_sendLock) ok = GameConsole.Send(lines);
            if (!ok) BeginInvoke(() => AddLog("⚠ " + Loc.T("gm.notSent", "Não enviado: abra o jogo (com console) e entre com o personagem GM.")));
        });
    }
    readonly object _sendLock = new();   // um envio de cada vez (cliques seguidos não misturam as teclas)

    // ---------- servidor neste PC (opcional): aviso geral e lista de quem está online pelo gdeliveryd ----------
    // O aviso do console (gm_broadcast) não chega ao servidor 1.5.5 testado; com o servidor local no ar, vai pelo
    // gdeliveryd como o "Administrar". Um GM em outro PC (sem servidor) continua pelo console.
    static bool LocalServerUp()
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            return c.ConnectAsync(ServerAdmin.Host, ServerAdmin.DeliveryPort).Wait(400) && c.Connected;
        }
        catch { return false; }
    }

    static ServerAdmin LocalAdmin() => new(LocalServer.Current);

    void AddLog(string s) { _log.Items.Add($"{DateTime.Now:HH:mm:ss}  {s}"); _log.TopIndex = Math.Max(0, _log.Items.Count - 1); }

    void UpdateState()
    {
        bool on = GameConsole.Running;
        // posição lida da memória do jogo (GamePosition; builds conhecidos): mostra no cartão e marca no mapa
        var pos = on ? GamePosition.Read() : null;
        var title = on ? Loc.T("gm.cardOn", "Jogo aberto") : Loc.T("gm.cardOff", "Jogo fechado");
        var detail = !on ? Loc.T("gm.cardOffDetail", "Clique em \"Abrir o jogo com console\" e entre com o personagem GM.")
            : pos is { } q ? string.Format(Loc.T("gm.cardPos", "Você está em X {0:0}  Z {1:0}  (altura {2:0}) · os comandos vão para este jogo"), q.X, q.Z, q.Y)
            : Loc.T("gm.cardOnDetail", "Entre no mundo com o personagem GM para usar os comandos.");
        _card.Set(on ? StatusCard.Kind.On : StatusCard.Kind.Off, title, detail);
        (float X, float Z)? now = pos is { } r ? (r.X, r.Z) : null;
        if (_myPos != now) { _myPos = now; _mapPic.Invalidate(); }
    }
    (float X, float Z)? _myPos;

    void PickDir()
    {
        using var d = new FolderBrowserDialog { Description = Loc.T("gm.pickFolder", "Pasta element do cliente (a que tem elementclient.exe)"), SelectedPath = _dir.Text };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _dir.Text = d.SelectedPath; SaveDir(); ApplyCommandSet(); _data = null; _icons = null; foreach (var m in _maps.Values) m?.Image.Dispose(); _maps.Clear(); _map = null; _mapPic.Image = null; _mapPick.Items.Clear(); LoadData();
    }

    void SaveDir() { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingFile)!); File.WriteAllText(SettingFile, _dir.Text.Trim()); } catch (IOException) { } }

    string ElementDir => _dir.Text.Trim();

    void LaunchGame()
    {
        if (!File.Exists(Path.Combine(ElementDir, GameConsole.Exe))) { MessageBox.Show(this, Loc.T("gm.noExe", "Não achei o elementclient.exe nesta pasta."), Text); return; }
        SaveDir();
        try { GameConsole.FocusWhenOpen(GameConsole.Launch(ElementDir)); AddLog(Loc.T("gm.launched2", "Jogo aberto com o console ligado (console:1) e sem parar ao perder o foco (rendernofocus:1). Entre com a sua conta GM.")); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    // ---------- Meu personagem ----------
    Control MeTab()
    {
        var exp = Num(1_000_000, 1, 2_000_000_000, 130); var slots = Num(96, 32, 96); var cult = Num(22, 0, 32);
        var levels = Num(10, 1, 150, 70); var fame = Num(10_000, 0, 2_000_000_000, 120); var spirit = Num(10, 1, 500, 70);
        var tag = Num(1, 0, 999, 70); var x = Num(0, -99999, 99999); var z = Num(0, -99999, 99999); var chi = Num(3, 1, 3, 50);
        var inMap = new CheckBox { Text = Loc.T("gm.sameMap", "no mapa em que estou"), AutoSize = true, Checked = true, Margin = new Padding(0, 6, 8, 0) };
        return Stack(
            Group(Loc.T("gm.gState", "Estado"),
                Row(Btn(Loc.T("gm.invincible", "Invencível liga/desliga"), () => Run("d 205")),
                    Need(10837, Btn(Loc.T("gm.invisibleOn", "Transparente"), () => Run("d 10837 1"))),
                    Need(10837, Btn(Loc.T("gm.invisibleOff", "Visível de novo"), () => Run("d 10837 0"))),
                    Btn(Loc.T("gm.noCooldown", "Skills sem recarga"), () => Run("d 8903 73125")),
                    Btn(Loc.T("gm.myIds", "Meus IDs (personagem, clã, mundo)"), () => Run("d 10852 1")))),
            Group(Loc.T("gm.gLevel", "Nível e progresso"),
                // gs 1.5.5: d 2000 dá a EXP de 1 nível; não há "nível máximo" (o d 10993 da lista não existe nele)
                Row(Btn(Loc.T("gm.levelUp", "+1 nível"), () => Run("d 2000")),
                    L(Loc.T("gm.levels", "Subir")), levels, Btn(Loc.T("gm.levelsGo", "níveis"), () => Run(Enumerable.Repeat("d 2000", (int)levels.Value).ToArray())),
                    Need(10822, Btn(Loc.T("gm.rebirth", "Renascer"), () => { if (Ask(Loc.T("gm.rebirthAsk", "Fazer o renascimento deste personagem (precisa cumprir as condições do jogo)?"))) Run("d 10822"); })),
                    Btn(Loc.T("gm.money", "Moedas (nível × 1 milhão)"), () => Run("d 1988"))),
                Row(L(Loc.T("gm.exp", "EXP")), exp, Btn(Loc.T("gm.give", "Dar"), () => Run($"d 10889 {exp.Value}")),
                    Need(10850, L(Loc.T("gm.fame", "Fama (reputação)"))), Need(10850, fame), Need(10850, Btn(Loc.T("gm.apply", "Aplicar"), () => Run($"d 10850 {fame.Value}")))),
                // o gs não tem comando só de alma: cada d 10889 dá a EXP pedida + nível × 10.000 de alma (playercmd.cpp)
                Row(L(Loc.T("gm.spirit", "Alma: dar")), spirit, Btn(Loc.T("gm.spiritGo", "vezes (cada vez = nível × 10.000)"), () => Run(Enumerable.Repeat("d 10889 1", (int)spirit.Value).ToArray())),
                    L(Loc.T("gm.cultivation", "Cultivo (20-22 deus, 30-32 demônio)")), cult, Btn(Loc.T("gm.apply", "Aplicar"), () => Run($"d 1989 {cult.Value}")))),
            // chi (AP): o máximo vem das quests de cultivo (SetMaxAP, 100 por barra); d 1991 n define o máximo (0..399) e
            // d 1992 soma 100 (playercmd.cpp; os dois existem no gs 1.5.5)
            Group(Loc.T("gm.gChi", "Chi (barras de fúria)"),
                Row(Need(1991, L(Loc.T("gm.chiBars", "Barras liberadas"))), Need(1991, chi), Need(1991, Btn(Loc.T("gm.apply", "Aplicar"), () => Run($"d 1991 {chi.Value * 100}"))),
                    Btn(Loc.T("gm.chiFill", "Encher o chi"), () => Run(Enumerable.Repeat("d 1992", 3).ToArray())))),
            Group(Loc.T("gm.gBag", "Inventário"),
                Row(L(Loc.T("gm.slots", "Espaços")), slots, Btn(Loc.T("gm.apply", "Aplicar"), () => Run($"d 2016 {slots.Value}")),
                    Btn(Loc.T("gm.clearBag", "Limpar inventário"), () => { if (Ask(Loc.T("gm.clearBagAsk", "Apagar TODOS os itens do inventário deste personagem?"))) Run("d 10853"); }))),
            Group(Loc.T("gm.gTeleport", "Teleporte por coordenada (as do d_showpos)"),
                Row(inMap, L(Loc.T("gm.mapTag", "Mapa (tag)")), tag, L("X"), x, L("Z"), z,
                    Btn(Loc.T("gm.go", "Ir"), () => Run(inMap.Checked ? $"d_goto {x.Value} {z.Value}" : $"d 8888 {tag.Value} {x.Value} {z.Value} 0")),
                    Btn(Loc.T("gm.showPos", "Mostrar minha posição"), () => Run("d_showpos")))));
    }

    // ---------- Itens / NPCs e monstros ----------
    enum Kind { Item, Npc }
    readonly Dictionary<Kind, (TextBox Search, ListView List)> _searches = new();
    readonly Dictionary<ListView, List<(Item It, string List)>> _rows = new();

    Control SearchTab(Kind kind)
    {
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = Loc.T("gm.searchHint", "Nome ou ID…") };
        // lista virtual: todos os itens de todas as listas, sem limite (ícone pedido só quando a linha aparece)
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, VirtualMode = true, SmallImageList = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit } };
        _rows[list] = new();
        list.RetrieveVirtualItem += (_, e) =>
        {
            var (it, ln) = _rows[list][e.ItemIndex];
            e.Item = new ListViewItem(it.Id) { ImageIndex = IconOf(list, it) };
            e.Item.SubItems.Add(it.Name); e.Item.SubItems.Add(ln);
        };
        list.Columns.Add(Loc.T("gm.colId", "ID"), 80, HorizontalAlignment.Right); list.Columns.Add(Loc.T("gm.colName", "Nome"), 420); list.Columns.Add(Loc.T("gm.colType", "Tipo"), 220);
        _searches[kind] = (search, list);
        var wait = new System.Windows.Forms.Timer { Interval = 250 };
        wait.Tick += (_, _) => { wait.Stop(); Fill(kind); };
        search.TextChanged += (_, _) => { wait.Stop(); wait.Start(); };
        int? Sel() => list.SelectedIndices.Count > 0 && int.TryParse(_rows[list][list.SelectedIndices[0]].It.Id, out var id) ? id : null;
        var qty = Num(1, 1, 9999, 80); var time = Num(0, 0, 86400, 80);
        Control bottom = kind == Kind.Item
            ? Row(L(Loc.T("gm.qty", "Quantidade")), qty,
                  Btn(Loc.T("gm.dropItem", "Criar no chão, aos meus pés"), () => { if (Sel() is { } id) Run($"d 10800 {id} {qty.Value}"); }, blue: true),
                  Btn(Loc.T("gm.dropTemp", "Criar por 5 minutos"), () => { if (Sel() is { } id) Run($"d 2001 {id}"); }))
            : Row(Btn(Loc.T("gm.goTo", "Ir até ele"), () => { if (Sel() is { } id) Run($"d 8900 {id}"); }, blue: true),
                  L(Loc.T("gm.qty", "Quantidade")), qty, L(Loc.T("gm.lifeSec", "Dura (s, 0 = sempre)")), time,
                  Need(10802, Btn(Loc.T("gm.spawnMob", "Invocar monstro aqui"), () => { if (Sel() is { } id) Run($"d 10802 {id} {qty.Value} {time.Value} 0"); })),
                  Need(10857, Btn(Loc.T("gm.spawnNpc", "Invocar NPC aqui"), () => { if (Sel() is { } id) Run($"d 10857 {id} {qty.Value} {time.Value}"); })));
        bottom.Dock = DockStyle.Bottom;
        var p = new Panel();
        p.Controls.Add(list); p.Controls.Add(search); p.Controls.Add(bottom);
        return p;
    }

    void LoadData()
    {
        var path = Path.Combine(ElementDir, "data", "elements.data");
        if (_data is not null || !File.Exists(path)) return;
        AddLog(Loc.T("gm.loading", "Lendo a lista de itens, NPCs e monstros do cliente…"));
        Task.Run(() =>
        {
            Module? m = null; IconAtlas? ic = null;
            try { m = CfgCatalog.Load(path).Module; } catch (Exception ex) { AppLog.Error("painel do GM: elements.data", ex); }
            try { if (IconAtlas.FindSurfacesPck(path) is { } sp) ic = IconAtlas.Load(sp); } catch (Exception ex) { AppLog.Error("painel do GM: ícones", ex); }
            BeginInvoke(() =>
            {
                _data = m; _icons = ic;
                FillEvents(); FillPlugins();
                AddLog(m is null ? "⚠ " + Loc.T("gm.loadFail", "Não consegui ler o elements.data do cliente.") : Loc.T("gm.loaded", "Lista pronta: busque pelo nome ou ID."));
                Fill(Kind.Item); Fill(Kind.Npc);
            });
        });
    }

    static bool IsNpcList(ItemList l) => l.Name is "MONSTER_ESSENCE" or "NPC_ESSENCE";

    void Fill(Kind kind)
    {
        if (_data is null || !_searches.TryGetValue(kind, out var s)) return;
        var q = s.Search.Text.Trim();
        var rows = _rows[s.List];
        rows.Clear();
        foreach (var l in _data.Lists)
        {
            bool npc = IsNpcList(l);
            if (npc != (kind == Kind.Npc)) continue;
            if (!npc && !l.Items.Any(i => i.Fields.ContainsKey("file_icon"))) continue;   // listas sem ícone não são itens do inventário
            foreach (var it in l.Items)
                if (q.Length == 0 || it.Id == q || it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) rows.Add((it, l.Name));
        }
        s.List.VirtualListSize = 0;   // zera a rolagem antiga; antes de limpar a seleção (a linha antiga pode não existir)
        s.List.SelectedIndices.Clear();
        s.List.VirtualListSize = rows.Count;
        s.List.Invalidate();
    }

    readonly Dictionary<string, int> _iconIdx = new();
    int IconOf(ListView lv, Item it)
    {
        if (_icons is null || !it.Fields.TryGetValue("file_icon", out var f) || string.IsNullOrEmpty(f.Value)) return -1;
        var key = lv.GetHashCode() + "|" + f.Value;
        if (_iconIdx.TryGetValue(key, out var i)) return i;
        var img = _icons.Get(f.Value);
        if (img is null) return _iconIdx[key] = -1;
        lv.SmallImageList!.Images.Add(img);
        return _iconIdx[key] = lv.SmallImageList.Images.Count - 1;
    }

    // ---------- Jogadores ----------
    Control PlayersTab()
    {
        var id = Num(0, 0, int.MaxValue, 120); var secs = Num(3600, 0, 99_999_999, 110);
        var reason = new TextBox { Width = 260, Text = "GM" };
        int Id() => (int)id.Value;
        string R() => "\"" + reason.Text.Replace("\"", "'") + "\"";
        bool Has() { if (Id() > 0) return true; AddLog("⚠ " + Loc.T("gm.needId", "Informe o ID do personagem (use \"Mostrar IDs\" para ver sobre a cabeça).")); return false; }
        // quem está online: só com o servidor neste PC (gm_list_user do console não faz nada no cliente 1.5.3)
        var online = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Height = 150, Width = 620 };
        online.Columns.Add(Loc.T("gm.colName", "Nome"), 260); online.Columns.Add(Loc.T("gm.playerId", "ID do personagem"), 140, HorizontalAlignment.Right); online.Columns.Add(Loc.T("gm.colAccount", "Conta"), 100, HorizontalAlignment.Right);
        online.SelectedIndexChanged += (_, _) => { if (online.SelectedItems.Count > 0 && int.TryParse(online.SelectedItems[0].SubItems[1].Text, out var rid)) id.Value = rid; };
        void Refresh()
        {
            AddLog(Loc.T("gm.onlineReading", "Lendo quem está online…"));
            Task.Run(() =>
            {
                List<ServerAdmin.OnlineUser>? l = null;
                try { if (LocalServerUp()) l = LocalAdmin().Online(); } catch (Exception ex) { AppLog.Error("painel do GM: online", ex); }
                BeginInvoke(() =>
                {
                    if (l is null) { AddLog("⚠ " + Loc.T("gm.onlineNoServer", "A lista precisa do servidor neste PC. Sem ele, use \"Mostrar IDs sobre a cabeça\".")); return; }
                    online.BeginUpdate(); online.Items.Clear();
                    foreach (var u in l.OrderBy(u => u.Name)) online.Items.Add(new ListViewItem(new[] { u.Name, u.RoleId.ToString(), u.UserId.ToString() }));
                    online.EndUpdate();
                    AddLog(string.Format(Loc.T("gm.onlineCount", "{0} personagem(ns) online."), l.Count));
                });
            });
        }
        return Stack(
            Group(Loc.T("gm.gWho", "Personagem"),
                Row(L(Loc.T("gm.playerId", "ID do personagem")), id,
                    Btn(Loc.T("gm.showIds", "Mostrar IDs sobre a cabeça"), () => Run("gm_showid")),
                    Btn(Loc.T("gm.onlineList", "Atualizar quem está online"), Refresh)),
                Row(online)),
            Group(Loc.T("gm.gMove", "Mover"),
                Row(Btn(Loc.T("gm.moveTo", "Ir até o jogador"), () => { if (Has()) Run($"gm_moveto_player {Id()}"); }, blue: true),
                    Btn(Loc.T("gm.callIn", "Puxar o jogador até mim"), () => { if (Has()) Run($"gm_callin_player {Id()}"); }))),
            Group(Loc.T("gm.gPunish", "Punir (tempo em segundos)"),
                Row(L(Loc.T("gm.time", "Tempo")), secs, L(Loc.T("gm.reason", "Motivo")), reason),
                Row(Btn(Loc.T("gm.kick", "Expulsar do jogo"), () => { if (Has()) Run($"gm_kickout_role {Id()} {secs.Value} {R()}"); }),
                    Btn(Loc.T("gm.mute", "Calar"), () => { if (Has()) Run($"gm_shutup_role {Id()} {secs.Value} {R()}"); }),
                    Btn(Loc.T("gm.ban", "Banir (não entra)"), () => { if (Has() && Ask(string.Format(Loc.T("gm.banAsk", "Banir o personagem {0} por {1} s?"), Id(), secs.Value))) Run($"gm_forbid_role 100 {Id()} {secs.Value} {R()}"); }),
                    Btn(Loc.T("gm.noTrade", "Proibir troca"), () => { if (Has()) Run($"gm_forbid_role 102 {Id()} {secs.Value} {R()}"); }))));
    }

    // ---------- Mundo ----------
    Control WorldTab()
    {
        var msg = new TextBox { Width = 520 }; var trig = Num(1, 1, 999999); var secs = Num(120, 10, 86400); var hh = Num(12, 0, 23, 60); var mm = Num(0, 0, 59, 60);
        return Stack(
            Group(Loc.T("gm.gBroadcast", "Aviso para todos"),
                Row(msg, Btn(Loc.T("gm.send", "Enviar"), () => Broadcast(msg), blue: true))),
            Group(Loc.T("gm.gEvents", "Eventos (precisa do servidor neste PC)"),
                Row(L(Loc.T("gm.event", "Evento")), _events,
                    Btn(Loc.T("gm.eventOn", "Ligar"), () => { if (_events.SelectedItem is GmEvent e) Spawner(e.Open, true, e.Name); }, blue: true),
                    Btn(Loc.T("gm.eventOff", "Desligar"), () => { if (_events.SelectedItem is GmEvent e) Spawner(e.Close, false, e.Name); })),
                Row(L(Loc.T("gm.trigger", "Gatilho do npcgen (ID)")), trig,
                    Btn(Loc.T("gm.eventOn", "Ligar"), () => Spawner(new[] { (int)trig.Value }, true, "#" + trig.Value)),
                    Btn(Loc.T("gm.eventOff", "Desligar"), () => Spawner(new[] { (int)trig.Value }, false, "#" + trig.Value)))),
            Group(Loc.T("gm.gPlugins", "Plugins de evento (programa do servidor)"),
                Row(L(Loc.T("gm.plugin", "Plugin")), _plugins,
                    _plFire = Btn(Loc.T("gm.pluginFire", "Chamar agora"), () => PluginCmd("fire"), blue: true),
                    Btn(Loc.T("gm.pluginStop", "Encerrar"), () => PluginCmd("stop")),
                    Btn(Loc.T("gm.pluginReload", "Atualizar lista"), FillPlugins)),
                Row(_plInfo)),
            Group(Loc.T("gm.gMobs", "Monstros"),
                Row(Need(10855, Btn(Loc.T("gm.killAll", "Matar os monstros do mapa"), () => { if (Ask(Loc.T("gm.killAllAsk", "Matar todos os monstros do mapa atual?"))) Run("d 10855 10855"); })))),
            // d_rendernofocus: liga/desliga desenhar o jogo sem o foco da janela (EC_DlgCmdConsole.cpp CCMD_RENDERNOFOCUS; PW-KB §S31)
            Group(Loc.T("gm.gScreen", "Sua tela"),
                Row(Btn(Loc.T("gm.noFocus", "Não parar o jogo sem foco (liga/desliga)"), () => Run("d_rendernofocus")),
                    L(Loc.T("gm.noFocusNote2", "Já vem ligado ao abrir o jogo por aqui (rendernofocus:1); o botão desliga e liga de novo.")))),
            Group(Loc.T("gm.gTime", "Hora do dia (só na sua tela)"),
                Row(hh, L(":"), mm, Btn(Loc.T("gm.apply", "Aplicar"), () => Run($"d_settimeofday {hh.Value},{mm.Value:00}")))),
            Group(Loc.T("gm.gServer", "Servidor"),
                Row(L(Loc.T("gm.restartIn", "Reiniciar em (s)")), secs,
                    Btn(Loc.T("gm.restart", "Reiniciar com contagem"), () => { if (Ask(string.Format(Loc.T("gm.restartAsk", "Reiniciar o servidor em {0} s? Todos os jogadores saem."), secs.Value))) Run($"gm_restart_sev {secs.Value}"); }))));
    }

    // ---------- eventos: gatilhos do npcgen ----------
    // A lista é a do Ctrl+G do jogo (GM_ACTIVITY_CONFIG do elements.data: abrir = liga a lista de gatilhos "openlist",
    // fechar = desliga a "closelist"). O console não tem comando para isso; vai pelo gdeliveryd (ServerAdmin.Trigger).
    sealed record GmEvent(string Name, int[] Open, int[] Close) { public override string ToString() => Name; }
    readonly ComboBox _events = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    readonly ComboBox _plugins = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    // contador do plugin escolhido: "Chamar agora" fica desligado com a espera em vermelho e volta em verde quando pode
    // (estado do programa: #ready/#active/#last; lido a cada 5 s, contado a cada 1 s; usuário 01/11)
    Button _plFire = null!;
    readonly Label _plInfo = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(3, 4, 3, 4) };
    Dictionary<string, string> _plState = new();
    System.Windows.Forms.Timer? _plTimer; int _plTicks; bool _plBusy;
    void PluginTick()
    {
        if (!Visible) return;
        if (++_plTicks % 5 == 1 && !_plBusy)
        {
            _plBusy = true;
            Task.Run(() => { try { return LocalAdmin().PluginsState(); } catch (Exception) { return new Dictionary<string, string>(); } })
                .ContinueWith(t => BeginInvoke(() => { _plState = t.Result; _plBusy = false; }));
        }
        if (_plugins.SelectedItem is not GmPlugin p) { _plInfo.Text = ""; return; }
        bool active = _plState.GetValueOrDefault(p.Id + "#active", "0") == "1";
        var left = DateTime.TryParse(_plState.GetValueOrDefault(p.Id + "#ready", ""), out var ready) ? ready - DateTime.Now : TimeSpan.Zero;
        var last = _plState.GetValueOrDefault(p.Id + "#last", "").Split('|', 3);
        string lastTxt = last.Length == 3 ? "   ·   " + (last[0] == "ok" ? "✓ " : "✗ ") + last[1] + " " + last[2] : "";
        if (left > TimeSpan.Zero)
        {
            _plFire.Enabled = false;
            _plInfo.ForeColor = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(220, 38, 38);
            _plInfo.Text = string.Format(active ? Loc.T("pl.cdActive", "Ativo agora · termina em {0}") : Loc.T("pl.cdWait", "Pode chamar de novo em {0}"), left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss")) + lastTxt;
        }
        else
        {
            _plFire.Enabled = true;
            _plInfo.ForeColor = Color.FromArgb(22, 128, 61);
            _plInfo.Text = Loc.T("pl.cdReady", "✓ Pronto para chamar") + lastTxt;
        }
    }

    // plugins de evento (docs/PLUGINS.md): o GM chama na hora o mesmo evento que o programa sorteia (fica no histórico)
    sealed record GmPlugin(string Id, string Name) { public override string ToString() => Name.Length > 0 ? Name : Id; }   // o "Nome (só desta cópia)" da tela Plugins
    void FillPlugins()
    {
        Task.Run(() => { try { return LocalAdmin().PluginsLoad(); } catch (Exception) { return new List<PluginRule>(); } }).ContinueWith(t => BeginInvoke(() =>
        {
            if (_plTimer is null) { _plTimer = new System.Windows.Forms.Timer { Interval = 1000 }; _plTimer.Tick += (_, _) => PluginTick(); _plTimer.Start(); Disposed += (_, _) => _plTimer.Dispose(); }
            _plugins.Items.Clear();
            foreach (var r in t.Result) _plugins.Items.Add(new GmPlugin(r.Id, r.Name));
            if (_plugins.Items.Count > 0) _plugins.SelectedIndex = 0;
            AddLog(string.Format(Loc.T("gm.pluginsLoaded", "{0} plugin(s) no servidor."), _plugins.Items.Count));
        }));
    }

    void PluginCmd(string cmd)
    {
        if (_plugins.SelectedItem is not GmPlugin p) { AddLog("⚠ " + Loc.T("gm.pluginNone", "Nenhum plugin: crie na área Plugins do editor e clique em Atualizar lista.")); return; }
        Task.Run(() =>
        {
            var a = LocalAdmin();
            if (!a.PluginsRunning()) return Loc.T("gm.pluginOff", "O programa dos plugins está desligado no servidor (área Plugins do editor → Ligar programa).");
            a.PluginsCommand(cmd, p.Id);
            return cmd == "fire" ? string.Format(Loc.T("gm.pluginFired", "{0}: chamado (veja o histórico na área Plugins)."), p.Name)
                : string.Format(Loc.T("gm.pluginStopped", "{0}: encerrado."), p.Name);
        }).ContinueWith(t => BeginInvoke(() => AddLog(t.Result)));
    }

    void FillEvents()
    {
        _events.Items.Clear();
        if (_data?.Lists.FirstOrDefault(l => l.Name == "GM_ACTIVITY_CONFIG") is not { } list) return;
        int[] Ids(Item it, string prefix) => Enumerable.Range(1, 8)
            .Select(i => it.Fields.TryGetValue(prefix + i, out var f) && int.TryParse(f.Value, out var v) ? v : 0).TakeWhile(v => v > 0).ToArray();
        foreach (var it in list.Items)
        {
            if (it.Fields.TryGetValue("disabled", out var d) && d.Value is not ("" or "0")) continue;
            _events.Items.Add(new GmEvent($"{it.Id} - {it.Name}", Ids(it, "openlist_"), Ids(it, "closelist_")));
        }
        if (_events.Items.Count > 0) _events.SelectedIndex = 0;
    }

    void Spawner(int[] ids, bool on, string what)
    {
        if (ids.Length == 0) { AddLog("⚠ " + Loc.T("gm.eventEmpty", "Esse evento não tem gatilhos para isso.")); return; }
        AddLog("→ " + (on ? "active_npc_generator " : "cancel_npc_generator ") + string.Join(", ", ids));
        Task.Run(() =>
        {
            string msg;
            try
            {
                if (!LocalServerUp()) msg = "⚠ " + Loc.T("gm.eventNoServer", "Eventos precisam do servidor neste PC. Sem ele, use o Ctrl+G dentro do jogo.");
                else
                {
                    var adm = LocalAdmin();
                    var maps = ids.SelectMany(id => adm.Trigger(id, on)).Distinct().ToList();
                    msg = maps.Count > 0 ? string.Format(Loc.T("gm.eventDone", "{0}: feito no(s) mapa(s) {1}."), what, string.Join(", ", maps))
                        : "⚠ " + string.Format(Loc.T("gm.eventNone", "{0}: nenhum mapa rodando tem esse gatilho."), what);
                }
            }
            catch (Exception ex) { AppLog.Error("painel do GM: evento", ex); msg = "⚠ " + ex.Message; }
            BeginInvoke(() => AddLog(msg));
        });
    }

    void Broadcast(TextBox msg)
    {
        var text = msg.Text.Trim();
        if (text.Length == 0) return;
        msg.Clear();
        Task.Run(() =>
        {
            bool viaServer = false;
            try { if (LocalServerUp()) { LocalAdmin().Broadcast(text); viaServer = true; } } catch (Exception ex) { AppLog.Error("painel do GM: aviso", ex); }
            BeginInvoke(() =>
            {
                if (viaServer) AddLog("→ " + Loc.T("gm.broadcastSent", "Aviso enviado pelo servidor: ") + text);
                else Run("gm_broadcast \"" + text.Replace("\"", "'") + "\"");
            });
        });
    }

    // ---------- Mapa: mundo ou instância (o mapa da tecla M do jogo); arrastar com o esquerdo, ir até lá com o direito ----------
    // Teleporte = d 8888 <tag> x z 0 (gs playercmd.cpp: 4 inteiros tag, x, z, ctrl_id; com 3 o gs descarta). Mesma tag = salto
    // no mapa; outra tag = troca de mapa (LongJump/PlaneSwitch), então funciona do mundo para a instância e de volta.
    readonly PictureBox _mapPic = new() { SizeMode = PictureBoxSizeMode.AutoSize, Cursor = Cursors.Cross };
    readonly Label _mapInfo = new() { AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
    readonly ComboBox _mapPick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    sealed record MapChoice(int Tag, string Path, string Text) { public override string ToString() => Text; }
    sealed record LoadedMap(Bitmap Image, Func<Point, (float X, float Z)> ToGame, Func<float, float, PointF> ToPixel);
    readonly Dictionary<int, LoadedMap?> _maps = new();
    LoadedMap? _map; int _mapTag = 1;

    Control MapTab()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 52, 64) };
        scroll.Controls.Add(_mapPic);
        _mapInfo.Text = Loc.T("gm.mapHint2", "Arraste com o botão esquerdo; o botão direito leva o seu personagem até o ponto, de qualquer mapa (a instância precisa estar ligada no servidor).");
        _mapPick.SelectedIndexChanged += (_, _) => { if (_mapPick.SelectedItem is MapChoice c) ShowMapOf(c); };
        Point? grab = null; Point grabScroll = default;
        _mapPic.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            grab = _mapPic.PointToScreen(e.Location); grabScroll = new Point(-scroll.AutoScrollPosition.X, -scroll.AutoScrollPosition.Y);
            _mapPic.Cursor = Cursors.SizeAll;
        };
        _mapPic.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { grab = null; _mapPic.Cursor = Cursors.Cross; } };
        _mapPic.MouseMove += (_, e) =>
        {
            if (_map is null) return;
            if (grab is { } g && e.Button == MouseButtons.Left)
            {
                var now = _mapPic.PointToScreen(e.Location);
                scroll.AutoScrollPosition = new Point(grabScroll.X - (now.X - g.X), grabScroll.Y - (now.Y - g.Y));
                return;
            }
            var (x, z) = _map.ToGame(e.Location);
            _mapInfo.Text = string.Format(Loc.T("gm.mapPos2", "X {0:0}  Z {1:0}  (no jogo: {2} {3}) — botão direito para ir"), x, z, ((int)x + 4000) / 10, ((int)z + 5500) / 10);
        };
        // "você está aqui": a posição lida do jogo (vale se você estiver no mapa escolhido)
        _mapPic.Paint += (_, e) =>
        {
            if (_map is null || _myPos is not { } me) return;
            var q = _map.ToPixel(me.X, me.Z);
            if (q.X < 0 || q.Y < 0 || q.X > _map.Image.Width || q.Y > _map.Image.Height) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(Brushes.Red, q.X - 7, q.Y - 7, 14, 14);
            e.Graphics.DrawEllipse(new Pen(Color.White, 2), q.X - 7, q.Y - 7, 14, 14);
        };
        _mapPic.MouseClick += (_, e) =>
        {
            if (_map is null || e.Button != MouseButtons.Right) return;
            var (x, z) = _map.ToGame(e.Location);
            Run($"d 8888 {_mapTag} {x:0} {z:0} 0");
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
        top.Controls.Add(new Label { Text = Loc.T("gm.mapWhich", "Mapa"), AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        top.Controls.Add(_mapPick); top.Controls.Add(_mapInfo);
        var p = new Panel();
        p.Controls.Add(scroll); p.Controls.Add(top);
        return p;
    }

    /// <summary>Preenche a lista de mapas (mundo + instâncias com mapa da tecla M no cliente) e mostra o mundo.</summary>
    void LoadMap()
    {
        if (_mapPick.Items.Count > 0) return;
        var dir = ElementDir;
        Task.Run(() =>
        {
            var list = new List<MapChoice> { new(1, "world", Loc.T("gm.mapWorld", "Mundo")) };
            try
            {
                var names = ServerAdmin.MapNames(dir);
                var surf = Path.Combine(dir, "surfaces.pck");
                if (MapTags.ClientMaps(dir) is { } maps && File.Exists(surf))
                {
                    using var pck = new PckArchive(surf);
                    foreach (var m in maps.Values.Where(m => m.Tag > 1).OrderBy(m => m.Tag))
                        if (pck.Find($@"surfaces\ingame\{m.Path}.dds") is not null)
                            list.Add(new(m.Tag, m.Path, $"{m.Path} — {(names?.GetValueOrDefault(m.Tag) is { Length: > 0 } n ? n : Loc.T("gm.mapInstance", "instância"))} ({m.Tag})"));
                }
            }
            catch (Exception ex) { AppLog.Error("painel do GM: lista de mapas", ex); }
            BeginInvoke(() => { _mapPick.Items.Clear(); _mapPick.Items.AddRange(list.ToArray()); _mapPick.SelectedIndex = 0; });
        });
    }

    void ShowMapOf(MapChoice c)
    {
        if (_maps.TryGetValue(c.Tag, out var have)) { SetMap(c.Tag, have); return; }
        var dir = ElementDir;
        _mapInfo.Text = Loc.T("gm.mapLoading", "Carregando o mapa…");
        Task.Run(() =>
        {
            LoadedMap? lm = null;
            try { lm = c.Tag == 1 ? WorldMapImage(dir) : InstanceMap.Load(dir, c.Tag) is { } im ? new LoadedMap(im.Image, p => im.ToGame(p.X, p.Y), im.ToPixel) : null; }
            catch (Exception ex) { AppLog.Error("painel do GM: mapa", ex); }
            BeginInvoke(() => { _maps[c.Tag] = lm; SetMap(c.Tag, lm); });
        });
    }

    void SetMap(int tag, LoadedMap? m)
    {
        _map = m; _mapTag = tag; _mapPic.Image = m?.Image;
        _mapInfo.Text = m is null ? "⚠ " + Loc.T("gm.mapFail", "Não achei o mapa do mundo neste cliente (maps\\world e surfaces.pck).")
            : Loc.T("gm.mapHint2", "Arraste com o botão esquerdo; o botão direito leva o seu personagem até o ponto, de qualquer mapa (a instância precisa estar ligada no servidor).");
    }

    /// <summary>Mapa pintado do mundo (surfaces\maps\LLC.dds, peças de 1 km a 256 px).</summary>
    static LoadedMap? WorldMapImage(string dir)
    {
        var t = TerrainFile.Open(Path.Combine(dir, "maps", "world"));
        var surf = Path.Combine(dir, "surfaces.pck");
        if (t is null || !File.Exists(surf)) return null;
        float w = t.Width, h = t.Height;
        using var pck = new PckArchive(surf);
        const int tilePx = 256; const float tileM = 1024, mPerPx = tileM / tilePx;
        int rows = (int)Math.Ceiling(h / tileM), cols = (int)Math.Ceiling(w / tileM);
        var bmp = new Bitmap((int)(w / mPerPx), (int)(h / mPerPx));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(40, 52, 64));
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (pck.Find($@"surfaces\maps\{r:00}{c}.dds") is not { } e || DdsDecoder.Decode(pck.Read(e)) is not { } im) continue;
                    using var tile = new Bitmap(im.Width, im.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var bd = tile.LockBits(new Rectangle(0, 0, im.Width, im.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, tile.PixelFormat);
                    System.Runtime.InteropServices.Marshal.Copy(im.Pixels, 0, bd.Scan0, im.Pixels.Length);
                    tile.UnlockBits(bd);
                    g.DrawImage(tile, c * tilePx, r * tilePx, tilePx, tilePx);
                }
        }
        return new LoadedMap(bmp, p => (p.X * mPerPx - w / 2, h / 2 - p.Y * mPerPx), (x, z) => new PointF((x + w / 2) / mPerPx, (h / 2 - z) / mPerPx));
    }
}
