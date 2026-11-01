using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Um ponto do npcgen onde a criatura nasce (mapa, ponto e o grupo dela dentro do ponto).</summary>
internal sealed record SpawnRow(string Map, NpcGenSpawn Spawn, NpcGenCreature Group);
/// <summary>Quest que usa o item/criatura (como: dá, recebe, matar, prêmio...).</summary>
internal sealed record QuestUse(int Id, string Name, string How);

/// <summary>
/// Onde a criatura nasce (npcgen: quantidade, tempo de renascer, mortes até parar, patrulha, gatilho) e quais quests usam o
/// item/criatura (duplo clique abre a quest). Usado no editor de elements (seções Spawn e Quests) e no Mundo 3D (painel da criatura).
/// Editar quantidade/renascer/mortes chama <see cref="Apply"/>, que grava no npcgen com Ctrl+Z.
/// </summary>
internal sealed class CreatureInfoPanel : UserControl
{
    readonly bool _dark;
    readonly Label _spawnTitle = Caption(), _questTitle = Caption(), _hint = new() { Dock = DockStyle.Bottom, Height = 22 };   // embaixo, junto dos campos
    readonly ListView _spawns = List(), _quests = List();
    // ficha do ponto (padrão UiKit: nome | valor | o que significa), uma linha por campo — nada corta
    readonly TableLayoutPanel _edit = new() { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Padding = new Padding(6, 6, 6, 2) };
    readonly NumericUpDown _amount = Num(1, 1000), _respawn = Num(0, 864000), _min = Num(0, 864000), _died = Num(0, 100000);
    readonly Label _respawnText = Note(), _minText = Note(), _diedText = Note(), _amountText = Note();
    readonly Panel _spawnBox = new() { Dock = DockStyle.Fill }, _questBox = new() { Dock = DockStyle.Fill };
    bool _filling;
    IReadOnlyList<SpawnRow> _rows = Array.Empty<SpawnRow>();

    /// <summary>Grava no npcgen (com desfazer): linha, quantidade, renascer em segundos, mortes até parar.</summary>
    /// <summary>Grava no npcgen (com desfazer): linha, quantidade, renascer (s), mínimo do sorteio (s, 0 = sem), mortes até parar.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<SpawnRow, int, int, int, int>? Apply { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? OpenQuest { get; set; }
    /// <summary>Ir até o ponto (Mundo 3D / aba NPCGen). Null = sem botão.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<SpawnRow>? GoTo { get; set; }

    public CreatureInfoPanel(bool dark)
    {
        _dark = dark;
        var bg = dark ? Color.FromArgb(28, 32, 38) : Color.White;
        var fg = dark ? Color.Gainsboro : UiKit.Fg;
        BackColor = bg; ForeColor = fg; Font = new Font("Segoe UI", 9.5f);
        _hint.ForeColor = dark ? Color.Silver : UiKit.Muted; _hint.Padding = new Padding(4, 2, 0, 0);
        // dica quebra em linhas: a altura acompanha a largura (antes cortava no fim)
        void FitHint() { int w = Math.Max(100, _hint.Width - 8); _hint.Height = TextRenderer.MeasureText(_hint.Text, _hint.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + 6; }
        _hint.Resize += (_, _) => FitHint(); _hint.TextChanged += (_, _) => FitHint();
        foreach (var lv in new[] { _spawns, _quests })
        {
            lv.BackColor = dark ? Color.FromArgb(22, 25, 30) : Color.White; lv.ForeColor = fg;
            if (dark) lv.HandleCreated += (_, _) => SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
        }
        foreach (var t in new[] { _spawnTitle, _questTitle }) t.ForeColor = dark ? Color.White : UiKit.Accent;
        _spawns.Columns.Add("", 70); _spawns.Columns.Add("", 150); _spawns.Columns.Add("", 40); _spawns.Columns.Add("", 80); _spawns.Columns.Add("", 70); _spawns.Columns.Add("", 110);
        _quests.Columns.Add("", 70); _quests.Columns.Add("", 260); _quests.Columns.Add("", 200);

        Label L(Func<string> t) { var l = new Label { AutoSize = true, Margin = new Padding(0, 6, 8, 0), ForeColor = dark ? Color.Silver : UiKit.Muted }; Bind(() => l.Text = t()); return l; }
        _edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); _edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); _edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Label name, NumericUpDown n, Label note) { int r = _edit.RowCount++; _edit.Controls.Add(name, 0, r); _edit.Controls.Add(n, 1, r); _edit.Controls.Add(note, 2, r); }
        Row(L(() => Loc.T("ci.amount", "Quantidade")), _amount, _amountText);
        Row(L(() => Loc.T("ci.respawn", "Renascer (s)")), _respawn, _respawnText);
        Row(L(() => Loc.T("ci.min", "Mínimo (s)")), _min, _minText);
        Bind(() =>
        {
            _amountText.Text = Loc.T("ci.amountNote", "quantos nascem juntos neste ponto");
            _minText.Text = Loc.T("ci.minNote2", "0 = sempre o mesmo tempo. Maior (e menor que Renascer) = a cada morte o jogo sorteia um tempo entre 15 s + mínimo e 15 s + renascer.");
        });
        if (dark) foreach (var n in new[] { _amount, _respawn, _min, _died }) { n.BackColor = Color.FromArgb(40, 46, 56); n.ForeColor = Color.White; }
        foreach (var n in new[] { _amount, _respawn, _min, _died }) n.ValueChanged += (_, _) => Commit();
        _spawnBox.Controls.Add(_spawns); _spawnBox.Controls.Add(_edit); _spawnBox.Controls.Add(_hint); _spawnBox.Controls.Add(_spawnTitle);
        _questBox.Controls.Add(_quests); _questBox.Controls.Add(_questTitle);
        Controls.Add(_spawnBox); Controls.Add(_questBox);

        _spawns.SelectedIndexChanged += (_, _) => LoadSelected();
        _spawns.ItemActivate += (_, _) => { if (Selected is { } r) GoTo?.Invoke(r); };
        _quests.ItemActivate += (_, _) => { if (_quests.SelectedItems.Count > 0 && _quests.SelectedItems[0].Tag is QuestUse q) OpenQuest?.Invoke(q.Id); };
        foreach (var lv in new[] { _spawns, _quests })
            lv.Resize += (_, _) => { int other = lv.Columns.Cast<ColumnHeader>().Where(c => c.Index != 1).Sum(c => c.Width); lv.Columns[1].Width = Math.Max(120, lv.ClientSize.Width - other - 4); };
        Bind(ApplyText);
    }

    void Bind(Action a) { a(); Loc.Changed += a; Disposed += (_, _) => Loc.Changed -= a; }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string? appName, string? idList);

    static Label Caption() => new() { Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 10.5f), TextAlign = ContentAlignment.MiddleLeft };
    static ListView List() => new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    static NumericUpDown Num(int min, int max) => new() { Minimum = min, Maximum = max, Width = 86, Margin = new Padding(0, 2, 8, 2), ThousandsSeparator = false };
    static Label Note() => new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = Color.Gray };

    SpawnRow? Selected => _spawns.SelectedItems.Count > 0 ? _spawns.SelectedItems[0].Tag as SpawnRow : null;

    /// <summary>Qual parte aparece: só onde nasce, só quests, ou as duas (Mundo 3D).</summary>
    public void Show(bool spawns, bool quests)
    {
        _spawnBox.Visible = spawns; _questBox.Visible = quests;
        _questBox.Dock = spawns ? DockStyle.Bottom : DockStyle.Fill;
        _questBox.Height = spawns ? Math.Max(140, Height / 3) : _questBox.Height;
    }

    void ApplyText()
    {
        string[] sc = { Loc.T("ci.map", "Mapa"), Loc.T("ci.pos", "Posição (x, y, z)"), Loc.T("ci.amountShort", "Qtd"), Loc.T("ci.respawnShort", "Renascer"),
            Loc.T("ci.minShort", "Mínimo"), Loc.T("ci.extra", "Patrulha / gatilho") };   // "mortes até parar" saiu: o gs 1.5.x não usa (usuário 01/11)
        for (int i = 0; i < sc.Length; i++) _spawns.Columns[i].Text = sc[i];
        _quests.Columns[0].Text = Loc.T("wq.colId", "ID"); _quests.Columns[1].Text = Loc.T("wq.colName", "Quest"); _quests.Columns[2].Text = Loc.T("ci.how", "Como usa");
        _hint.Text = GoTo is null ? Loc.T("ci.hintEdit2", "Escolha um ponto e mude abaixo: grava no npcgen do mapa (Ctrl+Z desfaz). Não existe chance de nascer: quando o tempo acaba ele sempre nasce; o único sorteio do jogo é o do tempo (Mínimo).")
            : Loc.T("ci.hintGo2", "Escolha um ponto e mude abaixo (grava no npcgen, Ctrl+Z desfaz); duplo clique leva até ele. Não existe chance de nascer: quando o tempo acaba ele sempre nasce; o único sorteio é o do tempo (Mínimo).");
    }

    /// <summary>
    /// Tempo real de renascer: o gs soma 15 s (config.h BASE_REBORN_TIME) ao valor; com "mínimo" (RefreshLower) entre o
    /// mínimo e o valor, sorteia (npcgenerator.cpp AddSpawnData). Valor negativo = -valor + 3 s.
    /// </summary>
    public static string RealRespawn(PerfectWorld.NpcGenCreature g)
    {
        int up = g.Respawn >= 0 ? 15 + g.Respawn : -g.Respawn + 3;
        int low = g.Respawn >= 0 && g.RefreshLower > 0 && g.RefreshLower < g.Respawn ? 15 + g.RefreshLower : up;
        return "= " + (low < up ? string.Format(Loc.T("ci.realRange", "{0} a {1} no jogo (sorteado)"), Time(low), Time(up)) : string.Format(Loc.T("ci.real", "{0} no jogo"), Time(up)));
    }

    /// <summary>Tempo legível: 600 → "10 min".</summary>
    public static string Time(int s) => s <= 0 ? Loc.T("ci.instant", "na hora") : s < 60 ? $"{s} s" : s < 3600 ? $"{s / 60} min" + (s % 60 > 0 ? $" {s % 60} s" : "") : $"{s / 3600} h" + (s % 3600 / 60 > 0 ? $" {s % 3600 / 60} min" : "");

    public void SetSpawns(string title, IReadOnlyList<SpawnRow> rows)
    {
        var keep = Selected;
        _rows = rows;
        _spawnTitle.Text = title;
        _spawns.BeginUpdate(); _spawns.Items.Clear();
        foreach (var r in rows)
        {
            var g = r.Group; var s = r.Spawn;
            var extra = new List<string>();
            if (g.PathId > 0) extra.Add(string.Format(Loc.T("ci.path", "patrulha {0}"), g.PathId));
            if (s.Trigger > 0) extra.Add(string.Format(Loc.T("ci.trigger", "gatilho {0}"), s.Trigger));
            if (g.Aggressive != 0) extra.Add(Loc.T("ci.aggro", "agressivo"));
            var it = new ListViewItem(new[] { r.Map, $"{s.Position.X:0}, {s.Position.Y:0}, {s.Position.Z:0}", g.Amount.ToString(), Time(g.Respawn),
                g.RefreshLower > 0 && g.RefreshLower < g.Respawn ? Time(g.RefreshLower) : "—", string.Join(" · ", extra) }) { Tag = r };
            if (s.Trigger > 0) it.ToolTipText = Loc.T("ci.triggerTip", "Só nasce quando o gatilho (evento do mapa) está ligado.");
            _spawns.Items.Add(it);
            if (keep is not null && ReferenceEquals(keep.Group, g)) it.Selected = true;
        }
        if (rows.Count == 0) _spawns.Items.Add(new ListViewItem(new[] { "", Loc.T("ci.noSpawn", "(não nasce em nenhum mapa do servidor)") }) { ForeColor = Color.Gray });
        else if (_spawns.SelectedItems.Count == 0) _spawns.Items[0].Selected = true;
        _spawns.EndUpdate();
        LoadSelected();
    }

    public void SetQuests(string title, IReadOnlyList<QuestUse> quests, bool loading = false)
    {
        _questTitle.Text = title;
        _quests.BeginUpdate(); _quests.Items.Clear();
        foreach (var q in quests.OrderBy(q => q.Id))
            _quests.Items.Add(new ListViewItem(new[] { q.Id.ToString(), q.Name, q.How }) { Tag = q, ToolTipText = Loc.T("wq.tipOpen", "Clique duas vezes para abrir a quest.") });
        if (quests.Count == 0 && !loading) _quests.Items.Add(new ListViewItem(new[] { "", Loc.T("ci.noQuest", "(nenhuma quest usa)") }) { ForeColor = Color.Gray });
        _quests.EndUpdate();
    }

    void LoadSelected()
    {
        _filling = true;
        try
        {
            var r = Selected;
            _edit.Enabled = r is not null && Apply is not null;
            if (r is null) { _respawnText.Text = ""; return; }
            _amount.Value = Math.Clamp(r.Group.Amount, (int)_amount.Minimum, (int)_amount.Maximum);
            _respawn.Value = Math.Clamp(r.Group.Respawn, (int)_respawn.Minimum, (int)_respawn.Maximum);
            _died.Value = Math.Clamp(r.Group.DiedTimes, (int)_died.Minimum, (int)_died.Maximum);
            _min.Value = Math.Clamp(r.Group.RefreshLower, (int)_min.Minimum, (int)_min.Maximum);
            _respawnText.Text = RealRespawn(r.Group);
        }
        finally { _filling = false; }
    }

    void Commit()
    {
        if (_filling || Selected is not { } r || Apply is null) return;
        int a = (int)_amount.Value, s = (int)_respawn.Value, m = (int)_min.Value, d = (int)_died.Value;
        if (a == r.Group.Amount && s == r.Group.Respawn && m == r.Group.RefreshLower && d == r.Group.DiedTimes) return;
        Apply(r, a, s, m, d);
        _respawnText.Text = RealRespawn(r.Group);
        if (_spawns.SelectedItems.Count > 0)
        {
            var it = _spawns.SelectedItems[0];
            it.SubItems[2].Text = a.ToString(); it.SubItems[3].Text = Time(s); it.SubItems[4].Text = m > 0 && m < s ? Time(m) : "—";
        }
    }
}
