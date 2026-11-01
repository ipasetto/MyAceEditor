namespace MyAceEditor.UI;

/// <summary>
/// Painel de baixo do Mundo 3D para monstro (e NPC sem quests): onde este ponto faz nascer, quantidade, tempo de renascer,
/// mortes até parar e as quests que usam a criatura. Mesmo jeito do painel de quests do NPC (WorldQuestPanel).
/// </summary>
internal sealed class WorldCreaturePanel : Panel
{
    const int Full = 300, Header = 44;
    readonly Label _title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11f), AutoEllipsis = true };
    readonly Button _edit, _close;
    readonly ToolTip _tip = new();
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 12 };
    int _target, _token, _id;
    public CreatureInfoPanel Info { get; } = new(dark: true) { Dock = DockStyle.Fill };
    /// <summary>Abrir a criatura no editor de elements (ID).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Action<int>? EditCreature { get; set; }

    public WorldCreaturePanel()
    {
        Dock = DockStyle.Bottom; Height = 0; Visible = false; BackColor = Color.FromArgb(28, 32, 38);
        Button Btn(Action a, string glyph)
        {
            var b = new Button { AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(36, 41, 49), Height = 32, Margin = new Padding(4, 0, 0, 0),
                Image = UiKit.Icon(glyph, Color.White, 16), TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(6, 0, 6, 0), Cursor = Cursors.Hand, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(34, 32) };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90);
            b.Click += (_, _) => a(); return b;
        }
        _edit = Btn(() => { if (_id > 0) EditCreature?.Invoke(_id); }, "");
        _close = Btn(HidePanel, "");
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 6, 0) };
        buttons.Controls.AddRange(new Control[] { _edit, _close });
        var head = new Panel { Dock = DockStyle.Top, Height = Header, BackColor = Color.FromArgb(36, 41, 49) };
        var icon = new PictureBox { Dock = DockStyle.Left, Width = 40, Image = UiKit.Icon("", Color.FromArgb(240, 120, 110), 22), SizeMode = PictureBoxSizeMode.CenterImage };
        head.Controls.Add(_title); head.Controls.Add(icon); head.Controls.Add(buttons);
        Controls.Add(Info); Controls.Add(head);
        Info.Show(spawns: true, quests: true);
        _anim.Tick += (_, _) =>
        {
            int step = Math.Max(8, Math.Abs(_target - Height) / 3);
            Height = _target > Height ? Math.Min(_target, Height + step) : Math.Max(_target, Height - step);
            if (Height == _target) { _anim.Stop(); if (_target == 0) Visible = false; }
        };
        Disposed += (_, _) => { _anim.Dispose(); _tip.Dispose(); };
        ApplyText();
        Loc.Changed += ApplyText; Disposed += (_, _) => Loc.Changed -= ApplyText;
    }

    void ApplyText()
    {
        _edit.Text = Loc.T("wc.edit", "Abrir no editor");
        _tip.SetToolTip(_edit, Loc.T("wc.editTip", "Abre o monstro no editor de elements (nível, drops, modelo...)."));
        _tip.SetToolTip(_close, Loc.T("wq.close", "Fechar"));
    }

    /// <summary>Mostra o ponto escolhido; as quests vêm em segundo plano (a 1ª busca leva alguns segundos).</summary>
    public void ShowCreature(int id, string name, IReadOnlyList<SpawnRow> rows, Func<IReadOnlyList<QuestUse>>? quests)
    {
        _id = id;
        _title.Text = string.Format(Loc.T("wc.title", "{0}   ·   ID {1}"), name, id);
        Info.SetSpawns(Loc.T("wc.spawnTitle", "Este ponto"), rows);
        int token = ++_token;
        if (quests is null) Info.SetQuests(Loc.T("ci.noTasks", "Sem tasks.data aberto."), Array.Empty<QuestUse>());
        else
        {
            Info.SetQuests(Loc.T("ci.questsLoading", "Procurando quests…"), Array.Empty<QuestUse>(), loading: true);
            Task.Run(quests).ContinueWith(t =>
            {
                if (IsDisposed || t.IsFaulted || token != _token) return;
                BeginInvoke(() => { if (token == _token) Info.SetQuests(string.Format(Loc.T("wc.questTitle", "Quests que usam ({0}) · duplo clique abre"), t.Result.Count), t.Result); });
            }, TaskScheduler.Default);
        }
        Visible = true; BringToFront();
        _target = Full; _anim.Start();
    }

    public void HidePanel() { _token++; _id = 0; _target = 0; _anim.Start(); }
}
