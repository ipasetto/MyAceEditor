using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Preferências → PCK Key (pedido da comunidade, usuário 10/11): perfis de chaves dos pacotes .pck. À esquerda a lista
/// (nome + KEY1 em cinza; o de fábrica primeiro, fixo); à direita o cartão do perfil com as 6 chaves em pares e o que cada
/// uma faz; embaixo Copiar/Colar chaves, Testar num pacote e Converter pacote para este perfil; Desfazer/Refazer e Salvar.
/// O MyAce descobre sozinho qual perfil abre cada pacote (PckKeyStore.Detect). As chaves ficam só no PC do usuário.
/// </summary>
internal sealed class PckKeysPage : Panel, PreferencesWindow.ILeavePage
{
    static readonly Color PageBg = Color.FromArgb(244, 246, 249);
    readonly List<PckKeys> _list = new();          // perfis do usuário (sem o de fábrica)
    string _saved;                                   // como está no arquivo (para saber se mudou)
    readonly List<(string Json, int Sel)> _undo = new(), _redo = new();
    string? _lastField; DateTime _lastEdit;
    bool _binding;

    readonly ListBox _profiles = new() { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 42, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly TextBox _name = new() { BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox[] _keys = new TextBox[6];
    readonly Label _banner = new() { AutoSize = false, Height = 44, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 6, 0) };
    readonly Label _status = new() { AutoSize = false, Height = 22, ForeColor = UiKit.Muted };
    readonly Button _new, _copyProfile, _delete, _copyKeys, _pasteKeys, _test, _convert, _undoBtn, _redoBtn, _save;
    readonly ToolTip _tip = new();
    static readonly (string Key, string Pt, string TipKey, string Tip)[] KeyDefs =
    {
        ("pckkey.k1", "KEY1 · posições e tamanhos", "pckkey.k1Tip", "Embaralha a posição da tabela e o tamanho de cada entrada (AFPCK_MASKDWORD)."),
        ("pckkey.k2", "KEY2 · conferência", "pckkey.k2Tip", "Embaralha a conferência do tamanho de cada entrada (AFPCK_CHECKMASK)."),
        ("pckkey.a1", "ASIG1 · marca do fim 1", "pckkey.a1Tip", "Primeira marca do rodapé do pacote (AFPCK_GUARDBYTE0)."),
        ("pckkey.a2", "ASIG2 · marca do fim 2", "pckkey.a2Tip", "Segunda marca do rodapé do pacote (AFPCK_GUARDBYTE1)."),
        ("pckkey.f1", "FSIG1 · marca do começo 1", "pckkey.f1Tip", "Primeira marca no começo do arquivo .pck."),
        ("pckkey.f2", "FSIG2 · marca do começo 2", "pckkey.f2Tip", "Segunda marca no começo do arquivo .pck (só nos pacotes v2)."),
    };

    public PckKeysPage()
    {
        Dock = DockStyle.Fill; BackColor = PageBg; Padding = new Padding(16, 8, 16, 16);
        Disposed += (_, _) => _tip.Dispose();
        foreach (var k in PckKeyStore.User) _list.Add(k);
        _saved = Json();

        Button Btn(string glyph, string text, Action a, bool main = false)
        {
            var b = new Button { Text = "  " + text, AutoSize = false, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = main ? UiKit.Accent : Color.White, ForeColor = main ? Color.White : UiKit.Fg,
                Image = UiKit.Icon(glyph, main ? Color.White : UiKit.Accent, 16), TextImageRelation = TextImageRelation.ImageBeforeText, TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 4, 0), Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = main ? UiKit.Accent : Color.FromArgb(205, 212, 222);
            b.Click += (_, _) => a();
            return b;
        }
        _new = Btn("", Loc.T("pckkey.new", "Novo"), NewProfile);
        _copyProfile = Btn("", Loc.T("pckkey.clone", "Copiar perfil"), CloneProfile);
        _delete = Btn("", Loc.T("pckkey.delete", "Apagar"), DeleteProfile);
        _copyKeys = Btn("", Loc.T("pckkey.copyKeys", "Copiar chaves"), CopyKeys);
        _pasteKeys = Btn("", Loc.T("pckkey.pasteKeys", "Colar chaves"), PasteKeys);
        _test = Btn("", Loc.T("pckkey.test", "Testar num pacote…"), TestPack);
        _convert = Btn("", Loc.T("pckkey.convert", "Converter pacote para este perfil…"), ConvertPack);
        _undoBtn = Btn("", Loc.T("world.undo", "Desfazer (Ctrl+Z)"), () => Undo(false));
        _redoBtn = Btn("", Loc.T("world.redo", "Refazer (Ctrl+Y)"), () => Undo(true));
        _save = Btn("", Loc.T("pckkey.save", "Salvar perfis"), () => SaveProfiles(), main: true);
        _tip.SetToolTip(_copyKeys, Loc.T("pckkey.copyKeysTip", "Copia as 6 chaves deste perfil como texto (KEY1=… KEY2=…)."));
        _tip.SetToolTip(_pasteKeys, Loc.T("pckkey.pasteKeysTip", "Cola as 6 chaves copiadas (de outra ferramenta ou de outro perfil) neste perfil."));
        _tip.SetToolTip(_test, Loc.T("pckkey.testTip", "Escolha um .pck: o MyAce diz qual perfil o abre."));
        _tip.SetToolTip(_convert, Loc.T("pckkey.convertTip", "Regrava um .pck com as chaves deste perfil (o original vira backup). O jogo só abre se o elementclient.exe usar as mesmas chaves."));

        // ---- topo ----
        var title = new Label { Dock = DockStyle.Top, Height = 34, Text = Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiKit.Accent, TextAlign = ContentAlignment.BottomLeft };
        var intro = new Label { Dock = DockStyle.Top, Height = 40, ForeColor = UiKit.Muted, Padding = new Padding(2, 2, 0, 0),
            Text = Loc.T("pckkey.intro", "O MyAce abre os pacotes com as chaves de fábrica do Perfect World ou com um destes perfis (descobre sozinho). Servidor que trocou as chaves: cadastre aqui as do seu cliente.") };

        // ---- esquerda: lista + Novo/Copiar/Apagar ----
        var left = new Panel { Dock = DockStyle.Left, Width = 214, BackColor = PageBg, Padding = new Padding(0, 6, 10, 0) };
        var listBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        listBox.Controls.Add(_profiles); _profiles.Dock = DockStyle.Fill;
        // um embaixo do outro, na largura da lista (lado a lado o "Copiar perfil" quebrava)
        var leftBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 124, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0), BackColor = PageBg };
        _new.Width = _copyProfile.Width = _delete.Width = 204;
        leftBtns.Controls.AddRange(new Control[] { _new, _copyProfile, _delete });
        left.Controls.Add(listBox); left.Controls.Add(leftBtns);
        _profiles.DrawItem += DrawProfile;
        _profiles.SelectedIndexChanged += (_, _) => { if (!_binding) ShowProfile(); };

        // ---- direita: cartão do perfil ----
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 10, 14, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, BackColor = Color.White, Padding = new Padding(0, 4, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Label Cap(string t) => new() { Text = t, AutoSize = true, ForeColor = UiKit.Fg, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 8, 0, 2) };
        var nameCap = Cap(Loc.T("pckkey.name", "Nome do perfil"));
        grid.Controls.Add(nameCap, 0, 0); grid.SetColumnSpan(nameCap, 2);
        _name.Dock = DockStyle.Fill; _name.Margin = new Padding(0, 0, 8, 2);
        grid.Controls.Add(_name, 0, 1); grid.SetColumnSpan(_name, 2);
        for (int i = 0; i < 6; i++)
        {
            int col = i % 2, row = 2 + i / 2 * 2;
            var cap = Cap(Loc.T(KeyDefs[i].Key, KeyDefs[i].Pt));
            _tip.SetToolTip(cap, Loc.T(KeyDefs[i].TipKey, KeyDefs[i].Tip));
            var tb = _keys[i] = new TextBox { BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 2), Font = new Font("Consolas", 10f) };
            _tip.SetToolTip(tb, Loc.T(KeyDefs[i].TipKey, KeyDefs[i].Tip) + Environment.NewLine + Loc.T("pckkey.numTip", "Número com sinal (-1466731422) ou hexadecimal (0xA8937462)."));
            grid.Controls.Add(cap, col, row); grid.Controls.Add(tb, col, row + 1);
            int idx = i; tb.TextChanged += (_, _) => FieldChanged("k" + idx);
        }
        _name.TextChanged += (_, _) => FieldChanged("name");
        var cardBtns = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 12, 0, 0), BackColor = Color.White };
        foreach (var b in new[] { _copyKeys, _pasteKeys }) { b.Width = 150; cardBtns.Controls.Add(b); }
        cardBtns.SetFlowBreak(_pasteKeys, true);
        foreach (var b in new[] { _test, _convert }) { b.Width = 306; cardBtns.Controls.Add(b); cardBtns.SetFlowBreak(b, true); }
        card.Controls.Add(cardBtns); card.Controls.Add(grid); card.Controls.Add(_banner);
        _banner.Dock = DockStyle.Top;

        // ---- embaixo: estado, desfazer/refazer, salvar (com espaço livre depois) ----
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, WrapContents = false, Padding = new Padding(0, 10, 0, 0), BackColor = PageBg };
        _undoBtn.Width = 150; _redoBtn.Width = 150; _save.Width = 150;
        bottom.Controls.AddRange(new Control[] { _undoBtn, _redoBtn, _save });
        _status.Dock = DockStyle.Bottom;

        var body = new Panel { Dock = DockStyle.Fill, BackColor = PageBg, Padding = new Padding(0, 4, 0, 0) };
        body.Controls.Add(card); body.Controls.Add(left);
        Controls.Add(body); Controls.Add(_status); Controls.Add(bottom); Controls.Add(intro); Controls.Add(title);

        // botão nunca menor que o texto (traduções longas, ex.: russo): ícone + folgas + texto
        foreach (var b in new[] { _copyKeys, _pasteKeys, _undoBtn, _redoBtn, _save, _test, _convert })
            b.Width = Math.Max(b.Width, TextRenderer.MeasureText(b.Text.Trim(), b.Font).Width + (b.Image?.Width ?? 0) + b.Padding.Horizontal + 26);

        FillList(0);
        // Ctrl+Z / Ctrl+Y / Ctrl+S nesta página
        foreach (Control c in new Control[] { this, _profiles, _name }.Concat(_keys)) c.KeyDown += Keys_;
    }

    void Keys_(object? s, KeyEventArgs e)
    {
        if (!e.Control) return;
        if (e.KeyCode == Keys.Z) { Undo(false); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Y) { Undo(true); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.S) { SaveProfiles(); e.SuppressKeyPress = true; }
    }

    string Json() => System.Text.Json.JsonSerializer.Serialize(_list);
    public bool Dirty => Json() != _saved;
    PckKeys? Current => _profiles.SelectedIndex <= 0 ? null : _list[_profiles.SelectedIndex - 1];   // 0 = de fábrica

    void FillList(int sel)
    {
        _binding = true;
        _profiles.Items.Clear();
        _profiles.Items.Add(PckKeys.Default);
        foreach (var k in _list) _profiles.Items.Add(k);
        _profiles.SelectedIndex = Math.Clamp(sel, 0, _profiles.Items.Count - 1);
        _binding = false;
        ShowProfile();
    }

    void DrawProfile(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var k = (PckKeys)_profiles.Items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Color.FromArgb(232, 240, 254) : Color.White)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (sel) using (var bar = new SolidBrush(UiKit.Accent)) e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        string name = e.Index == 0 ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : k.Name;
        using var f1 = new Font("Segoe UI Semibold", 9.5f); using var f2 = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, name, f1, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4, e.Bounds.Width - 14, 18), UiKit.Fg, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, "KEY1 " + k.Key1, f2, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 22, e.Bounds.Width - 14, 16), UiKit.Muted, TextFormatFlags.EndEllipsis);
    }

    void ShowProfile()
    {
        var k = _profiles.SelectedIndex <= 0 ? PckKeys.Default : _list[_profiles.SelectedIndex - 1];
        bool fixedOne = _profiles.SelectedIndex <= 0;
        _binding = true;
        _name.Text = fixedOne ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : k.Name;
        int[] v = { k.Key1, k.Key2, k.Asig1, k.Asig2, k.Fsig1, k.Fsig2 };
        for (int i = 0; i < 6; i++) { _keys[i].Text = v[i].ToString(System.Globalization.CultureInfo.InvariantCulture); _keys[i].ReadOnly = fixedOne; _keys[i].BackColor = fixedOne ? Color.FromArgb(246, 247, 249) : Color.White; }
        _name.ReadOnly = fixedOne; _name.BackColor = _keys[0].BackColor;
        _binding = false;
        _banner.Text = fixedOne ? "★  " + Loc.T("pckkey.defaultBanner", "Chaves de fábrica do Perfect World: valem para quase todos os clientes e não se editam.")
            : "✎  " + Loc.T("pckkey.userBanner", "Perfil seu: as chaves precisam ser as mesmas do elementclient.exe do seu cliente.");
        _banner.BackColor = fixedOne ? Color.FromArgb(253, 243, 214) : Color.FromArgb(232, 240, 254);
        _banner.ForeColor = UiKit.Fg;
        _delete.Enabled = _pasteKeys.Enabled = !fixedOne;
        _convert.Enabled = true;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        _undoBtn.Enabled = _undo.Count > 0; _redoBtn.Enabled = _redo.Count > 0;
        _save.Enabled = Dirty && Validate(out _) is null;
    }

    /// <summary>Snapshot antes de mudar (digitação seguida no mesmo campo vira um passo só).</summary>
    void Remember(string field)
    {
        bool merge = field == _lastField && (DateTime.Now - _lastEdit).TotalSeconds < 1.5 && field.Length > 0;
        if (!merge) { _undo.Add((Json(), _profiles.SelectedIndex)); if (_undo.Count > 200) _undo.RemoveAt(0); }
        _redo.Clear(); _lastField = field; _lastEdit = DateTime.Now;
    }

    void FieldChanged(string field)
    {
        if (_binding || _profiles.SelectedIndex <= 0) return;
        int i = _profiles.SelectedIndex - 1;
        var cur = _list[i];
        var nums = _keys.Select(t => PckKeys.ParseNumber(t.Text)).ToArray();
        for (int k = 0; k < 6; k++) _keys[k].BackColor = nums[k] is null ? Color.FromArgb(253, 226, 226) : Color.White;
        if (nums.Any(n => n is null)) { _status.ForeColor = UiKit.Warn; _status.Text = Loc.T("pckkey.badNumber", "Número inválido: use um número com sinal (-1466731422) ou hexadecimal (0xA8937462)."); UpdateButtons(); return; }
        var next = new PckKeys(_name.Text.Trim(), nums[0]!.Value, nums[1]!.Value, nums[2]!.Value, nums[3]!.Value, nums[4]!.Value, nums[5]!.Value);
        if (next == cur) return;
        Remember(field);
        _list[i] = next;
        _profiles.Invalidate();
        _status.ForeColor = UiKit.Muted; _status.Text = Loc.T("pckkey.unsaved", "Alterado: clique em Salvar perfis para valer.");
        UpdateButtons();
    }

    /// <summary>null = tudo certo; senão o motivo (nome vazio ou repetido).</summary>
    string? Validate(out int bad)
    {
        bad = -1;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Loc.T("pckkey.defaultName", "Perfect World (padrão)"), PckKeys.Default.Name };
        for (int i = 0; i < _list.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(_list[i].Name)) { bad = i; return Loc.T("pckkey.noName", "Todo perfil precisa de um nome."); }
            if (!names.Add(_list[i].Name)) { bad = i; return string.Format(Loc.T("pckkey.dupName", "Já existe um perfil chamado \"{0}\"."), _list[i].Name); }
        }
        return null;
    }

    void NewProfile()
    {
        Remember("");
        int n = 1; string name;
        do name = string.Format(Loc.T("pckkey.newName", "Minhas chaves {0}"), n++); while (_list.Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        _list.Add(PckKeys.Default with { Name = name });
        FillList(_list.Count);
        _status.ForeColor = UiKit.Muted; _status.Text = Loc.T("pckkey.newHint", "Perfil novo com as chaves de fábrica: troque pelas do seu cliente (ou Colar chaves) e salve.");
        _name.Focus(); _name.SelectAll();
    }

    void CloneProfile()
    {
        var src = _profiles.SelectedIndex <= 0 ? PckKeys.Default : _list[_profiles.SelectedIndex - 1];
        Remember("");
        string baseName = (_profiles.SelectedIndex <= 0 ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : src.Name) + " " + Loc.T("pckkey.copySuffix", "(cópia)");
        string name = baseName; int n = 2;
        while (_list.Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = baseName + " " + n++;
        _list.Add(src with { Name = name });
        FillList(_list.Count);
    }

    void DeleteProfile()
    {
        if (Current is not { } k) return;
        if (MessageBox.Show(FindForm(), string.Format(Loc.T("pckkey.deleteAsk", "Apagar o perfil \"{0}\"? Pacotes com essas chaves deixam de abrir no MyAce (Ctrl+Z desfaz antes de salvar)."), k.Name),
                Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Remember("");
        int at = _profiles.SelectedIndex;
        _list.RemoveAt(at - 1);
        FillList(at - 1);
        _status.Text = Loc.T("pckkey.unsaved", "Alterado: clique em Salvar perfis para valer.");
        UpdateButtons();
    }

    void CopyKeys()
    {
        var k = _profiles.SelectedIndex <= 0 ? PckKeys.Default : _list[_profiles.SelectedIndex - 1];
        try { Clipboard.SetText(k.ToText()); _status.ForeColor = UiKit.Ok; _status.Text = Loc.T("pckkey.copied", "Chaves copiadas."); }
        catch (System.Runtime.InteropServices.ExternalException ex) { AppLog.Error("chaves do pck: copiar", ex); _status.ForeColor = UiKit.Warn; _status.Text = Loc.T("pckkey.clipFail", "A área de transferência está ocupada por outro programa; tente de novo."); }
    }

    void PasteKeys()
    {
        if (Current is not { } k) return;
        string text;
        try { text = Clipboard.GetText(); }
        catch (System.Runtime.InteropServices.ExternalException ex) { AppLog.Error("chaves do pck: colar", ex); _status.ForeColor = UiKit.Warn; _status.Text = Loc.T("pckkey.clipFail", "A área de transferência está ocupada por outro programa; tente de novo."); return; }
        if (PckKeys.FromText(k.Name, text) is not { } got)
        { _status.ForeColor = UiKit.Warn; _status.Text = Loc.T("pckkey.pasteBad", "O texto copiado não tem as 6 chaves (KEY1, KEY2, ASIG1, ASIG2, FSIG1, FSIG2)."); return; }
        Remember("");
        _list[_profiles.SelectedIndex - 1] = got;
        FillList(_profiles.SelectedIndex);
        _status.ForeColor = UiKit.Muted; _status.Text = Loc.T("pckkey.pasted", "Chaves coladas: clique em Salvar perfis para valer.");
        UpdateButtons();
    }

    void Undo(bool redo)
    {
        var from = redo ? _redo : _undo; var to = redo ? _undo : _redo;
        if (from.Count == 0) return;
        to.Add((Json(), _profiles.SelectedIndex));
        var (json, sel) = from[^1]; from.RemoveAt(from.Count - 1);
        _list.Clear(); _list.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<PckKeys>>(json) ?? []);
        _lastField = null;
        FillList(sel);
        _status.ForeColor = UiKit.Muted; _status.Text = redo ? Loc.T("pckkey.redone", "Refeito.") : Loc.T("pckkey.undone", "Desfeito (Ctrl+Y refaz).");
    }

    /// <summary>Grava os perfis; true = gravou (ou nada a gravar).</summary>
    internal bool SaveProfiles(bool quiet = false)
    {
        if (!Dirty) return true;
        if (Validate(out int bad) is { } why)
        {
            if (bad >= 0) FillList(bad + 1);
            _status.ForeColor = UiKit.Warn; _status.Text = why;
            if (!quiet) MessageBox.Show(FindForm(), why, Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (PckKeyStore.Save(_list) is { } err)
        {
            _status.ForeColor = UiKit.Warn; _status.Text = err;
            if (!quiet) MessageBox.Show(FindForm(), err, Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _saved = Json();
        _status.ForeColor = UiKit.Ok; _status.Text = Loc.T("pckkey.saved", "Perfis salvos.");
        UpdateButtons();
        if (!quiet && !PreferencesWindow.Quiet)
            SavedDialog.Show(FindForm()!, string.Format(Loc.T("pckkey.savedDetail", "{0} perfil(is) de chaves salvo(s). O MyAce passa a abrir os pacotes com essas chaves (o de fábrica continua valendo)."), _list.Count));
        return true;
    }

    string? PickPack(string title)
    {
        using var d = new OpenFileDialog { Title = title, Filter = Loc.T("pckkey.filter", "Pacotes Angelica (*.pck)|*.pck") };
        return d.ShowDialog(FindForm()) == DialogResult.OK ? d.FileName : null;
    }

    void TestPack()
    {
        if (Dirty && !AskSaveFirst()) return;
        if (PickPack(Loc.T("pckkey.testTitle", "Escolha um pacote para testar")) is not { } f) return;
        try
        {
            var probe = PckKeyStore.ReadProbe(f);
            var k = PckKeyStore.Detect(probe);
            var strict = PckKeyConvert.StrictMatch(probe);
            string name(PckKeys x) => x.IsDefault ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : x.Name;
            string msg = k is null ? Loc.T("pckkey.testNone", "Nenhum perfil abre este pacote. Cadastre as chaves do cliente dele (Novo ou Colar chaves).")
                : strict is not null ? string.Format(Loc.T("pckkey.testOk", "Este pacote abre com: {0} (todas as marcas batem)."), name(strict))
                : string.Format(Loc.T("pckkey.testLoose", "Este pacote abre com: {0} (só a marca do começo bate; para converter, as do fim também precisam bater)."), name(k));
            _status.ForeColor = k is null ? UiKit.Warn : UiKit.Ok; _status.Text = msg;
            MessageBox.Show(FindForm(), Path.GetFileName(f) + Environment.NewLine + Environment.NewLine + msg, Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, k is null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            AppLog.Error("chaves do pck: testar " + f, ex);
            MessageBox.Show(FindForm(), Loc.T("pckkey.readFail", "Não consegui ler esse arquivo (aberto em outro programa ou não é um pacote)."), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    bool AskSaveFirst()
    {
        var a = MessageBox.Show(FindForm(), Loc.T("pckkey.saveFirst", "Há mudanças nos perfis ainda não salvas. Salvar agora?"), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return a == DialogResult.Yes && SaveProfiles();
    }

    void ConvertPack()
    {
        if (Dirty && !AskSaveFirst()) return;
        var to = _profiles.SelectedIndex <= 0 ? PckKeys.Default : _list[_profiles.SelectedIndex - 1];
        string toName = _profiles.SelectedIndex <= 0 ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : to.Name;
        if (PickPack(Loc.T("pckkey.convertTitle", "Escolha o pacote para converter")) is not { } f) return;
        PckKeys? from;
        try { from = PckKeyConvert.StrictMatch(PckKeyStore.ReadProbe(f)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { AppLog.Error("chaves do pck: converter " + f, ex); MessageBox.Show(FindForm(), Loc.T("pckkey.readFail", "Não consegui ler esse arquivo (aberto em outro programa ou não é um pacote)."), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (from is null) { MessageBox.Show(FindForm(), Loc.T("pckkey.notStrict", "As marcas deste pacote não batem por inteiro com nenhum perfil cadastrado: não dá para converter com segurança."), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (from.SameKeys(to)) { MessageBox.Show(FindForm(), Loc.T("pckkey.sameKeys", "O pacote já usa essas chaves."), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string fromName = from.IsDefault ? Loc.T("pckkey.defaultName", "Perfect World (padrão)") : from.Name;
        var ask = string.Format(Loc.T("pckkey.convertAsk", "Converter {0} das chaves \"{1}\" para \"{2}\"?"), Path.GetFileName(f), fromName, toName) + Environment.NewLine + Environment.NewLine
            + "⚠ " + Loc.T("pckkey.exeWarn", "O jogo só abre o pacote convertido se o elementclient.exe do cliente usar as mesmas chaves. Com o .exe original, o cliente não abre mais esse pacote. O MyAce não mexe no .exe.") + Environment.NewLine + Environment.NewLine
            + Loc.T("pckkey.convertNote", "Os arquivos dentro do pacote não mudam. O original vira backup; para voltar, converta de novo para o perfil anterior. Feche o jogo antes.");
        if (MessageBox.Show(FindForm(), ask, Loc.T("pckkey.convert", "Converter pacote para este perfil…").TrimEnd('…'), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var old = Cursor.Current; Cursor.Current = Cursors.WaitCursor; Enabled = false;
        _status.ForeColor = UiKit.Accent; _status.Text = Loc.T("pckkey.converting", "Convertendo… (pacote grande leva alguns segundos)"); _status.Refresh();
        try
        {
            PckKeyConvert.Convert(f, to);
            _status.ForeColor = UiKit.Ok; _status.Text = Loc.T("pckkey.converted", "Pacote convertido.");
            SavedDialog.Show(FindForm()!, string.Format(Loc.T("pckkey.convertedDetail", "{0} agora usa as chaves \"{1}\". O original ficou no backup. Para o jogo abrir, o elementclient.exe precisa usar as mesmas chaves."), Path.GetFileName(f), toName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            AppLog.Error("chaves do pck: converter " + f, ex);
            _status.ForeColor = UiKit.Warn; _status.Text = ex.Message;
            MessageBox.Show(FindForm(), Loc.T("pckkey.convertFail", "Não deu para converter; o pacote ficou como estava.") + Environment.NewLine + Environment.NewLine + ex.Message, Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Enabled = true; Cursor.Current = old; }
    }

    /// <summary>Fechar a janela com perfis não salvos: Salvar / Descartar / Cancelar. false = não fecha.</summary>
    public bool ConfirmLeave()
    {
        if (!Dirty) return true;
        if (PreferencesWindow.Quiet) return true;
        var a = MessageBox.Show(FindForm(), Loc.T("pckkey.leaveAsk", "Os perfis de chaves têm mudanças não salvas. Salvar antes de fechar?"), Loc.T("pckkey.title", "Chaves dos pacotes (.pck)"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return a == DialogResult.No || a == DialogResult.Yes && SaveProfiles();
    }

    /// <summary>Teste ("--prefs-shot … pck"): novo perfil, edição, desfazer, refazer, colar (sem gravar).</summary>
    internal string SelfTest()
    {
        var r = new List<string>();
        int n0 = _list.Count;
        NewProfile(); r.Add($"novo={_list.Count == n0 + 1}");
        _keys[0].Text = "0x12345678"; bool edited = _list[^1].Key1 == 0x12345678;
        _keys[1].Text = "abc"; bool flagged = _keys[1].BackColor != Color.White && _list[^1].Key2 == PckKeys.Default.Key2;
        _keys[1].Text = PckKeys.Default.Key2.ToString();
        Undo(false); bool undoKey = _list.Count == n0 + 1 && _list[^1].Key1 == PckKeys.Default.Key1;
        Undo(true); bool redoKey = _list[^1].Key1 == 0x12345678;
        Undo(false); Undo(false); bool undoNew = _list.Count == n0;
        r.Add($"editar={edited} número inválido marcado={flagged} Ctrl+Z={undoKey} Ctrl+Y={redoKey} desfez o novo={undoNew}");
        return string.Join("; ", r);
    }
}
