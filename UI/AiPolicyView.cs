using System.Globalization;
using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Aba "AI Policy" (PW-KB §S18). Esquerda: políticas e triggers. Meio: o trigger aberto (condições em árvore, operações com alvo).
/// Grade: campos do nó selecionado, editável. Baixo: SIGNIFICADO em linguagem natural (idioma do programa), com falas/nomes
/// traduzidos, nomes de monstros do elements.data e "usada por". Só edita se o arquivo foi lido por inteiro (AiPolicyFile.Ok).
/// Traduções são só de exibição (cache à parte): nunca alteram o aipolicy.data.
/// </summary>
/// <summary>Item do seletor de idioma-alvo: mostra o nome da língua no próprio idioma.</summary>
sealed record TargetItem(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed class AiPolicyView : UserControl
{
    sealed record NodeRef(object Obj, AiTrigger Trig);

    static string L(string pt, string en) => AppLang.T(pt, en);

    readonly TreeView _policies = new() { Dock = DockStyle.Fill, HideSelection = false };
    readonly TreeView _detail = new() { Dock = DockStyle.Fill, HideSelection = false };
    readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly TextBox _meaning = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font("Segoe UI", 10), BorderStyle = BorderStyle.None };
    readonly TextBox _report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Visible = false, WordWrap = false };
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly ComboBox _newOp = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    readonly Label _lblNewOp = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly Label _lblLang = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly Button _open = new() { AutoSize = true };
    readonly Button _save = new() { AutoSize = true, Enabled = false };
    readonly Button _add = new() { AutoSize = true, Enabled = false };
    readonly Button _del = new() { AutoSize = true, Enabled = false };
    readonly Button _clone = new() { AutoSize = true, Enabled = false };
    readonly TextBox _find = new() { Width = 200 };
    readonly Button _findNext = new() { AutoSize = true };
    readonly Button _trSel = new() { AutoSize = true, Enabled = false };
    readonly Button _trAll = new() { AutoSize = true, Enabled = false };
    readonly CheckBox _orig = new() { AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
    readonly Label _lblTarget = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    readonly ComboBox _tgt = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };

    /// <summary>Idioma em que os TEXTOS DO JOGO são traduzidos (independe do idioma do programa). Padrão: português.</summary>
    public string TargetLang { get; private set; } = "pt";
    static string TargetFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "translate_lang.txt");

    readonly AiTranslator _tr = new(new TranslationCache(), new GoogleFreeTranslator());
    readonly AiContext _ctx = new();
    readonly Dictionary<int, string> _monsters = new();
    readonly Dictionary<int, List<(int Id, string Name)>> _usedBy = new();
    CancellationTokenSource? _cts;

    AiPolicyFile? _file;
    string? _path;
    bool _dirty;
    AiTrigger? _current;
    readonly List<Func<string, string?>> _setters = new();   // um por linha da grade; devolve erro ou null

    public AiPolicyView(Color bg, Color fg, Color treeBg, Color treeFg, Color accent)
    {
        BackColor = bg; Dock = DockStyle.Fill;

        _ctx.MonsterName = id => _monsters.TryGetValue(id, out var n) ? n : null;
        _ctx.Translate = body => _orig.Checked ? null : _tr.TryGet(body, TargetLang);
        try { var c = File.ReadAllText(TargetFile).Trim(); if (AppLang.All.Any(a => a.Code == c)) TargetLang = c; } catch { }
        foreach (var (code, name) in AppLang.All) _tgt.Items.Add(new TargetItem(code, name));
        _tgt.SelectedIndex = Math.Max(0, AppLang.All.ToList().FindIndex(a => a.Code == TargetLang));
        _tgt.SelectedIndexChanged += (_, _) =>
        {
            if (_tgt.SelectedItem is not TargetItem ti) return;
            TargetLang = ti.Code;
            try { Directory.CreateDirectory(Path.GetDirectoryName(TargetFile)!); File.WriteAllText(TargetFile, TargetLang); } catch { }
            RefreshLabels(); if (_current != null) ShowFields(_detail.SelectedNode); RefreshMeaning(); ApplyTexts();
        };
        _ctx.UsedBy = UsedByOf;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0), BackColor = bg, WrapContents = false };
        _lblNewOp.ForeColor = fg; _lblLang.ForeColor = fg; _orig.ForeColor = fg;
        top.Controls.AddRange(new Control[] { _open, _save, _lblNewOp, _newOp, _add, _del, _clone });
        var top2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 2, 8, 0), BackColor = bg, WrapContents = false };
        // busca na 2ª linha: na 1ª, no tamanho normal da janela, o "Próximo" ficava cortado
        top2.Controls.AddRange(new Control[] { _find, _findNext, new Label { Width = 12 }, _trSel, _trAll, _lblTarget, _tgt, _orig, _lblLang });

        foreach (var tv in new[] { _policies, _detail }) { tv.BackColor = treeBg; tv.ForeColor = treeFg; tv.Font = new Font("Segoe UI", 10); tv.BorderStyle = BorderStyle.None; }
        _meaning.BackColor = treeBg; _meaning.ForeColor = treeFg;
        _grid.Columns.Add("campo", ""); _grid.Columns.Add("valor", "");
        _grid.Columns[0].ReadOnly = true; _grid.Columns[0].FillWeight = 35;
        _grid.Font = new Font("Segoe UI", 10);

        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 0, 8, 0), BackColor = bg };
        _status.ForeColor = fg; statusBar.Controls.Add(_status);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        lower.Panel1.Controls.Add(_grid);
        lower.Panel2.Controls.Add(_meaning);
        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(_detail);
        right.Panel2.Controls.Add(lower);
        var main = new SplitContainer { Dock = DockStyle.Fill };
        main.Panel1.Controls.Add(_policies);
        main.Panel2.Controls.Add(right);
        main.Panel2.Controls.Add(_report);

        Controls.Add(main); Controls.Add(top2); Controls.Add(top); Controls.Add(statusBar);
        _report.BringToFront(); _report.Visible = false;

        // distâncias dos divisores só depois que o controle tem tamanho de verdade
        Load += (_, _) =>
        {
            try
            {
                main.SplitterDistance = 320;
                right.SplitterDistance = Math.Max(100, right.Height * 36 / 100);
                lower.SplitterDistance = Math.Max(80, lower.Height * 50 / 100);
            }
            catch { /* tamanho ainda pequeno: fica no padrão */ }
        };

        _open.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "aipolicy.data|aipolicy.data|Todos|*.*" }; if (d.ShowDialog() == DialogResult.OK) LoadFile(d.FileName); };
        _save.Click += (_, _) => SaveFile();
        _policies.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is AiTrigger t) ShowTrigger(t);
            else if (e.Node?.Tag is AiPolicy) ClearDetail();
            RefreshMeaning(); UpdateButtons();
        };
        _detail.AfterSelect += (_, e) => { ShowFields(e.Node); RefreshMeaning(); UpdateButtons(); };
        _grid.CellEndEdit += OnCellEdit;
        _add.Click += (_, _) => AddOp();
        _del.Click += (_, _) => DeleteSelected();
        _clone.Click += (_, _) => CloneSelected();
        _findNext.Click += (_, _) => FindNext();
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        _trSel.Click += async (_, _) => await TranslateSelection();
        _trAll.Click += async (_, _) => await TranslateAll();
        _orig.CheckedChanged += (_, _) => { RefreshLabels(); if (_current != null) ShowFields(_detail.SelectedNode); RefreshMeaning(); };

        AppLang.Changed += OnLangChanged;
        ApplyTexts();
        SetStatus(L("Abra o aipolicy.data (pasta data do cliente ou config do servidor).", "Open aipolicy.data (client data folder or server config)."));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { AppLang.Changed -= OnLangChanged; _cts?.Cancel(); }
        base.Dispose(disposing);
    }

    // ---------- idioma ----------

    void ApplyTexts()
    {
        _open.Text = L("Abrir aipolicy.data…", "Open aipolicy.data…");
        _save.Text = L("Salvar (com backup)", "Save (with backup)");
        _lblNewOp.Text = L("  Nova operação:", "  New operation:");
        _add.Text = "+ " + L("Operação", "Operation");
        _del.Text = L("Excluir operação/trigger", "Delete operation/trigger");
        _clone.Text = L("Clonar política/trigger", "Clone policy/trigger");
        _find.PlaceholderText = L("Buscar: ID ou nome", "Search: ID or name");
        _findNext.Text = L("Próximo", "Next");
        _lblTarget.Text = L("Traduzir para:", "Translate to:");
        _trSel.Text = _cts is null ? L("Traduzir seleção", "Translate selection") : L("Cancelar tradução", "Cancel translation");
        _trAll.Text = _cts is null ? L("Traduzir tudo", "Translate all") : L("Cancelar tradução", "Cancel translation");
        _orig.Text = L("Mostrar original / nomes técnicos", "Show original / technical names");
        _lblLang.Text = L($"   Idioma do programa: {AppLang.NameOf(AppLang.Current)} (menu Idioma)", $"   Program language: {AppLang.NameOf(AppLang.Current)} (Language menu)");
        _grid.Columns[0].HeaderText = L("Campo", "Field");
        _grid.Columns[1].HeaderText = L("Valor", "Value");
        int keep = Math.Max(0, _newOp.SelectedIndex);
        _newOp.Items.Clear();
        foreach (var k in AiSchema.KnownOps) _newOp.Items.Add($"{k} {AiMeaning.OpTitle(k)}");
        if (_newOp.Items.Count > 0) _newOp.SelectedIndex = Math.Min(keep, _newOp.Items.Count - 1);
    }

    void OnLangChanged()
    {
        if (IsDisposed) return;
        ApplyTexts();
        if (_file is not { Ok: true }) return;
        RefreshLabels();
        if (_current != null) ShowTrigger(_current);
        RefreshMeaning();
    }

    // ---------- vínculos com o elements.data ----------

    IReadOnlyList<(int Id, string Name)> UsedByOf(int policyId)
    {
        if (_usedBy.TryGetValue(policyId, out var l)) return l;
        return Array.Empty<(int Id, string Name)>();
    }

    /// <summary>Chamado pelo EditorForm quando um elements.data é carregado: nomes de monstros e "quem usa cada política".</summary>
    public void SetElements(Module? m)
    {
        _monsters.Clear(); _usedBy.Clear();
        var list = m?.FindListByName("MONSTER_ESSENCE");
        if (list != null)
            foreach (var it in list.Items)
            {
                if (!int.TryParse(it.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
                _monsters[id] = it.Name;
                if (it.Fields.TryGetValue("id_strategy", out var f) &&
                    int.TryParse(f.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) && pid > 0)
                {
                    if (!_usedBy.TryGetValue(pid, out var users)) _usedBy[pid] = users = new List<(int Id, string Name)>();
                    users.Add((id, it.Name));
                }
            }
        if (_file is { Ok: true }) { RefreshLabels(); RefreshMeaning(); }
    }

    // ---------- arquivo ----------

    // Último aipolicy.data aberto: carregado sozinho ao abrir o programa (em segundo plano).
    static string RememberFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "aipolicy_path.txt");
    bool _rememberTried;

    public void LoadRememberedIfNeeded()
    {
        if (_rememberTried || _file is not null) return;
        _rememberTried = true;
        try { var p = File.ReadAllText(RememberFile).Trim(); if (File.Exists(p)) _ = LoadFileAsync(p, quiet: true); } catch { }
    }

    public void LoadFile(string path) => _ = LoadFileAsync(path, quiet: false);

    async Task LoadFileAsync(string path, bool quiet)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SetStatus(L("Carregando aipolicy.data…", "Loading aipolicy.data…"));
        AiPolicyFile parsed; bool rt;
        try { (parsed, rt) = await Task.Run(() => { var f = new AiPolicyFile(path); return (f, f.Ok && f.RoundTripIdentical()); }); }
        catch (Exception ex)
        {
            SetStatus(L("Falha ao ler o aipolicy.data.", "Failed to read aipolicy.data."));
            if (!quiet) MessageBox.Show(this, ex.Message, "AI Policy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _file = parsed; _path = path; _dirty = false;
        try { Directory.CreateDirectory(Path.GetDirectoryName(RememberFile)!); File.WriteAllText(RememberFile, path); } catch { }
        ShowFile(path, sw.ElapsedMilliseconds, rt);
    }

    void ShowFile(string path, long parseMs, bool rt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _policies.Nodes.Clear(); ClearDetail(); _meaning.Text = "";
        if (!_file.Ok)
        {
            _report.Text = "Leitura INCOMPLETA — edição desativada para não corromper o arquivo.\r\n\r\n" + _file.Error!.Replace("\n", "\r\n") +
                "\r\n\r\nO tamanho de cada operação não está gravado no arquivo (PW-KB §S18). Complemente o layout do tipo indicado em " +
                "aipolicy_layouts.json (ao lado do .exe) e abra de novo. Exemplo:\r\n{ \"ops\": { \"17\": \"i:uMonsterID i:uNum\" } }";
            _report.Visible = true; UpdateButtons();
            SetStatus($"{Path.GetFileName(path)}: leitura incompleta — veja o relatório.");
            return;
        }
        _report.Visible = false;
        _policies.BeginUpdate();
        foreach (var p in _file.Policies)
        {
            var pn = new TreeNode(PolicyLabel(p)) { Tag = p };
            foreach (var t in p.Triggers) pn.Nodes.Add(new TreeNode(TriggerLabel(t)) { Tag = t });
            _policies.Nodes.Add(pn);
        }
        _policies.EndUpdate();
        UpdateButtons();
        SetStatus($"{Path.GetFileName(path)}: versão {_file.Version} · {_file.Policies.Count:N0} políticas · round-trip {(rt ? "IDÊNTICO ✓" : "DIFERENTE ✗ (não salve!)")} · " +
                  L($"carregado em {(parseMs + sw.ElapsedMilliseconds) / 1000.0:0.0} s", $"loaded in {(parseMs + sw.ElapsedMilliseconds) / 1000.0:0.0} s"));
        if (!rt) _save.Enabled = false;
    }

    void ClearDetail()
    {
        _detail.Nodes.Clear(); _grid.Rows.Clear(); _setters.Clear(); _current = null;
    }

    // ---------- rótulos ----------

    string PolicyLabel(AiPolicy p)
    {
        int users = _ctx.UsedBy(p.Id).Count;
        return L($"Política {p.Id}  ({p.Triggers.Count} triggers", $"Policy {p.Id}  ({p.Triggers.Count} triggers")
               + (users > 0 ? L($" · {users} monstro(s)", $" · {users} monster(s)") : "") + ")";
    }

    string TriggerLabel(AiTrigger t) =>
        $"[{t.Id}] {AiMeaning.TriggerName(t, _ctx, _orig.Checked)}{(t.Active == 0 ? L("  (inativo)", "  (inactive)") : "")}";

    string CondLabel(AiCondNode n) => _orig.Checked
        ? $"{n.Type} {AiSchema.CondName(n.Type)}{(n.Param.Length > 0 ? "  " + CondParamText(n) : "")}"
        : AiMeaning.CondLabel(n);

    static string CondParamText(AiCondNode n) => n.Param.Length == 4 && n.Type is 1 or 3 or 27
        ? BitConverter.ToSingle(n.Param, 0).ToString("0.###", CultureInfo.InvariantCulture)
        : n.Param.Length % 4 == 0 ? string.Join(", ", Enumerable.Range(0, n.Param.Length / 4).Select(i => BitConverter.ToInt32(n.Param, i * 4))) : AiPolicyFile.Hex(n.Param, 0, n.Param.Length);

    string OpLabel(AiOp o, int i, AiTrigger t)
    {
        var lay = AiSchema.Op(o.Type);
        string guess = lay?.Guess == true ? " [?]" : "";
        if (_orig.Checked) return $"{i}: {o.Type} {AiSchema.OpName(o.Type)}{guess}";
        var s = AiMeaning.OpSentence(o, t.Version, _ctx);
        return $"{i}: {(s.Length > 110 ? s[..109] + "…" : s)}{guess}";
    }

    string TargetLabel(AiTarget a) => _orig.Checked
        ? $"alvo: {a.Type} {AiSchema.TargetName(a.Type)}"
        : L("alvo: ", "target: ") + AiMeaning.TargetText(a);

    string CondGroupLabel() => L("SE (condição)", "IF (condition)");
    string OpsGroupLabel(AiTrigger t) => L($"ENTÃO (operações: {t.Ops.Count})", $"THEN (operations: {t.Ops.Count})");
    string PropsLabel() => L("Gatilho (propriedades)", "Trigger (properties)");

    void RefreshLabels()
    {
        _policies.BeginUpdate();
        foreach (TreeNode pn in _policies.Nodes)
        {
            if (pn.Tag is AiPolicy p) pn.Text = PolicyLabel(p);
            foreach (TreeNode tn in pn.Nodes) if (tn.Tag is AiTrigger t) tn.Text = TriggerLabel(t);
        }
        _policies.EndUpdate();
        RelabelDetail(_detail.Nodes);
    }

    void RelabelDetail(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            switch (n.Tag)
            {
                case "cond": n.Text = CondGroupLabel(); break;
                case "ops": if (_current != null) n.Text = OpsGroupLabel(_current); break;
                case NodeRef { Obj: AiTrigger } : n.Text = PropsLabel(); break;
                case NodeRef { Obj: AiCondNode c }: n.Text = CondLabel(c); break;
                case NodeRef { Obj: AiOp o } r: n.Text = OpLabel(o, n.Index, r.Trig); break;
                case NodeRef { Obj: AiTarget a }: n.Text = TargetLabel(a); break;
            }
            RelabelDetail(n.Nodes);
        }
    }

    // ---------- árvore do trigger ----------

    void ShowTrigger(AiTrigger t)
    {
        _current = t;
        _detail.BeginUpdate(); _detail.Nodes.Clear();
        _detail.Nodes.Add(new TreeNode(PropsLabel()) { Tag = new NodeRef(t, t) });
        var cond = new TreeNode(CondGroupLabel()) { Tag = "cond" };
        cond.Nodes.Add(CondNode(t.Root, t));
        _detail.Nodes.Add(cond);
        var ops = new TreeNode(OpsGroupLabel(t)) { Tag = "ops" };
        for (int i = 0; i < t.Ops.Count; i++) ops.Nodes.Add(OpNode(t.Ops[i], i, t));
        _detail.Nodes.Add(ops);
        _detail.ExpandAll(); _detail.EndUpdate();
        _detail.SelectedNode = _detail.Nodes[0];
    }

    TreeNode CondNode(AiCondNode n, AiTrigger t)
    {
        var tn = new TreeNode(CondLabel(n)) { Tag = new NodeRef(n, t) };
        if (n.Left != null) tn.Nodes.Add(CondNode(n.Left, t));
        if (n.Right != null) tn.Nodes.Add(CondNode(n.Right, t));
        return tn;
    }

    TreeNode OpNode(AiOp o, int i, AiTrigger t)
    {
        var tn = new TreeNode(OpLabel(o, i, t)) { Tag = new NodeRef(o, t) };
        tn.Nodes.Add(new TreeNode(TargetLabel(o.Target)) { Tag = new NodeRef(o.Target, t) });
        return tn;
    }

    // ---------- significado ----------

    void RefreshMeaning()
    {
        if (_file is not { Ok: true }) { _meaning.Text = ""; return; }
        if (_current is null)
        {
            _meaning.Text = _policies.SelectedNode?.Tag is AiPolicy p ? AiMeaning.PolicySummary(p, _ctx) : "";
            return;
        }
        object obj = _detail.SelectedNode?.Tag is NodeRef nr ? nr.Obj : _current;
        _meaning.Text = AiMeaning.Explain(obj, _current, _ctx);
    }

    // ---------- grade de campos ----------

    void Row(string name, string value, Func<string, string?> set, bool readOnly = false)
    {
        int r = _grid.Rows.Add(name, value);
        _grid.Rows[r].Cells[1].ReadOnly = readOnly;
        _setters.Add(set);
    }

    void HintRow(string text) => Row("      ↳", text, _ => null, readOnly: true);

    void TranslationRow(string raw)
    {
        var body = AiChannel.Split(raw).Body;
        if (!_orig.Checked && _tr.TryGet(body, TargetLang) is { } tr) Row("      ↳ " + L("tradução", "translation"), tr, _ => null, readOnly: true);
    }

    void ShowFields(TreeNode? node)
    {
        _grid.Rows.Clear(); _setters.Clear();
        if (node?.Tag is not NodeRef tag) return;
        switch (tag.Obj)
        {
            case AiTrigger t:
                Row("ID", t.Id.ToString(), v => Try(() => t.Id = int.Parse(v, CultureInfo.InvariantCulture)));
                Row(L("dwVersion (define o layout das operações)", "dwVersion (sets the operation layout)"), t.Version.ToString(), _ => null, readOnly: true);
                Row(L("Ativo (0/1)", "Active (0/1)"), t.Active.ToString(), v => Try(() => t.Active = (byte)(int.Parse(v) != 0 ? 1 : 0)));
                Row(L("Executa (0/1)", "Run (0/1)"), t.Run.ToString(), v => Try(() => t.Run = (byte)(int.Parse(v) != 0 ? 1 : 0)));
                Row(L("Ataque válido (0/1)", "Attack valid (0/1)"), t.AttackValid.ToString(), v => Try(() => t.AttackValid = (byte)(int.Parse(v) != 0 ? 1 : 0)));
                Row(L("Nome (GBK, até 127 bytes)", "Name (GBK, up to 127 bytes)"), t.Name, v => Try(() => t.Name = v));
                TranslationRow(t.Name);
                break;
            case AiCondNode c:
                Row($"{L("Tipo", "Type")} ({AiSchema.CondName(c.Type)})", c.Type.ToString(), v => Try(() => c.Type = int.Parse(v, CultureInfo.InvariantCulture)));
                HintRow(AiMeaning.CondTitle(c.Type));
                Row(L("Parâmetro (hex; mesmo tamanho)", "Parameter (hex; same size)"), AiPolicyFile.Hex(c.Param, 0, c.Param.Length), v => Try(() =>
                {
                    var b = v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToByte(x, 16)).ToArray();
                    if (b.Length != c.Param.Length) throw new InvalidDataException($"O parâmetro tem {c.Param.Length} bytes; informe exatamente {c.Param.Length}.");
                    c.Param = b;
                }));
                if (c.Param.Length % 4 == 0)
                    for (int k = 0; k < c.Param.Length / 4; k++)
                    {
                        int kk = k;
                        Row($"  int32 #{k}  /  float", $"{BitConverter.ToInt32(c.Param, k * 4)}", v => Try(() =>
                        {
                            var nb = (byte[])c.Param.Clone();
                            // aceita inteiro ou decimal (decimal vira float32)
                            var bytes = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) ? BitConverter.GetBytes(iv)
                                : BitConverter.GetBytes(float.Parse(v, CultureInfo.InvariantCulture));
                            bytes.CopyTo(nb, kk * 4); c.Param = nb;
                        }));
                    }
                break;
            case AiOp o:
                Row(L("Tipo", "Type"), $"{o.Type} {AiSchema.OpName(o.Type)}", _ => null, readOnly: true);
                HintRow(AiMeaning.OpTitle(o.Type));
                var lay = AiSchema.Op(o.Type);
                if (lay is null) { Row(L("Parâmetro (hex)", "Parameter (hex)"), AiPolicyFile.Hex(o.Param, 0, o.Param.Length), _ => null, readOnly: true); break; }
                FieldRows(lay, tag.Trig.Version, () => o.Param, b => o.Param = b);
                if (lay.Guess) Row(L("(layout inferido, não provado)", "(inferred layout, not proven)"), "", _ => null, readOnly: true);
                break;
            case AiTarget a:
                Row(L("Tipo do alvo (0..18)", "Target type (0..18)"), a.Type.ToString(), v => Try(() =>
                {
                    int nt = int.Parse(v, CultureInfo.InvariantCulture);
                    var nl = AiSchema.Target(nt) ?? throw new InvalidDataException($"Alvo {nt} sem layout (aipolicy_layouts.json).");
                    a.Type = nt; a.Param = nl.Encode(tag.Trig.Version, nl.Zero(tag.Trig.Version));
                }));
                HintRow(AiMeaning.TargetText(a));
                if (AiSchema.Target(a.Type) is { } tl) FieldRows(tl, tag.Trig.Version, () => a.Param, b => a.Param = b);
                break;
        }
    }

    void FieldRows(AiLayout lay, int ver, Func<byte[]> get, Action<byte[]> put)
    {
        var fields = lay.Active(ver);
        var vals = lay.Decode(ver, get());
        for (int k = 0; k < fields.Count; k++)
        {
            int kk = k; var f = fields[k];
            if (f.Kind == '3') continue;   // padding
            Row(AiMeaning.FieldLabel(f.Name), ValueText(vals[k]), v => Try(() =>
            {
                var cur = lay.Decode(ver, get());
                cur[kk] = f.Kind switch
                {
                    'i' => int.Parse(v, CultureInfo.InvariantCulture),
                    'f' => (object)float.Parse(v, CultureInfo.InvariantCulture),
                    'b' => (byte)(int.Parse(v, CultureInfo.InvariantCulture) != 0 ? 1 : 0),
                    _ => v
                };
                put(lay.Encode(ver, cur));
            }));
            if (vals[k] is int iv && AiMeaning.Hint(f.Name, iv, _ctx) is { } hint) HintRow(hint);
            if ((f.Kind == 't' || f.Kind == 'a') && vals[k] is string sv) TranslationRow(sv);
        }
    }

    static string ValueText(object v) => v switch { float f => f.ToString("R", CultureInfo.InvariantCulture), byte[] b => AiPolicyFile.Hex(b, 0, b.Length), _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" };

    static string? Try(Action a)
    {
        try { a(); return null; } catch (Exception ex) { return ex.Message; }
    }

    void OnCellEdit(object? s, DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex != 1 || e.RowIndex < 0 || e.RowIndex >= _setters.Count || _file is not { Ok: true }) return;
        var text = Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value) ?? "";
        var err = _setters[e.RowIndex](text);
        if (err != null) MessageBox.Show(this, err, L("Valor inválido", "Invalid value"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else MarkDirty();
        var node = _detail.SelectedNode;
        ShowFields(node);                       // recarrega os valores já normalizados
        if (node?.Tag is NodeRef tg)
        {
            node.Text = tg.Obj switch
            {
                AiCondNode c => CondLabel(c),
                AiOp o => OpLabel(o, node.Index, tg.Trig),
                AiTarget a => TargetLabel(a),
                _ => node.Text
            };
            if (tg.Obj is AiTrigger t && _policies.SelectedNode is { } pn) pn.Text = TriggerLabel(t);
        }
        RefreshMeaning();
    }

    // ---------- operações ----------

    void AddOp()
    {
        if (_current is null || _file is not { Ok: true } || _newOp.SelectedItem is not string s) return;
        int type = int.Parse(s.Split(' ')[0]);
        var lay = AiSchema.Op(type); var tl = AiSchema.Target(7);
        if (lay is null || tl is null) return;
        int ver = _current.Version;
        var op = new AiOp { Type = type, Param = lay.Encode(ver, lay.Zero(ver)), Target = new AiTarget { Type = 7, Param = tl.Encode(ver, tl.Zero(ver)) } };
        int at = _detail.SelectedNode?.Tag is NodeRef { Obj: AiOp sel } ? _current.Ops.IndexOf(sel) + 1 : _current.Ops.Count;
        _current.Ops.Insert(at, op);
        MarkDirty(); ShowTrigger(_current);
    }

    void DeleteSelected()
    {
        if (_file is not { Ok: true }) return;
        if (_detail.SelectedNode?.Tag is NodeRef { Obj: AiOp op } && _current is { } t)
        {
            t.Ops.Remove(op); MarkDirty(); ShowTrigger(t);
        }
        else if (_policies.SelectedNode is { Tag: AiTrigger tr, Parent.Tag: AiPolicy pol } pn &&
                 MessageBox.Show(this, $"Excluir o trigger {tr.Id}? Outros triggers que o ativam por ID ficarão apontando para o vazio.", "AI Policy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            pol.Triggers.Remove(tr); pn.Remove();
            ClearDetail(); MarkDirty();
        }
        UpdateButtons();
    }

    // Clonar: política (novo ID = maior + 1, no fim da lista) ou trigger (novo ID na mesma política, logo depois).
    void CloneSelected()
    {
        if (_file is not { Ok: true }) return;
        switch (_policies.SelectedNode)
        {
            case { Tag: AiTrigger tr, Parent: { Tag: AiPolicy pol } pn }:
            {
                var copy = tr.Clone(pol.Triggers.Max(x => x.Id) + 1);
                pol.Triggers.Insert(pol.Triggers.IndexOf(tr) + 1, copy);
                var node = new TreeNode(TriggerLabel(copy)) { Tag = copy };
                pn.Nodes.Insert(pn.Nodes.IndexOf(_policies.SelectedNode) + 1, node);
                _policies.SelectedNode = node; MarkDirty();
                SetStatus(L($"Trigger {tr.Id} clonado como {copy.Id}", $"Trigger {tr.Id} cloned as {copy.Id}"));
                break;
            }
            case { Tag: AiPolicy p }:
            {
                var copy = p.Clone(_file.Policies.Max(x => x.Id) + 1);
                _file.Policies.Add(copy);
                var pn = new TreeNode(PolicyLabel(copy)) { Tag = copy };
                foreach (var t in copy.Triggers) pn.Nodes.Add(new TreeNode(TriggerLabel(t)) { Tag = t });
                _policies.Nodes.Add(pn); _policies.SelectedNode = pn; MarkDirty();
                SetStatus(L($"Política {p.Id} clonada como {copy.Id} (no fim da lista). Para um monstro usar, ponha esse ID no campo de IA dele.",
                            $"Policy {p.Id} cloned as {copy.Id} (end of list). Set this ID in a monster's AI field to use it."));
                break;
            }
        }
        UpdateButtons();
    }

    // Busca por ID de política/trigger ou parte do nome do trigger, a partir do item selecionado (Próximo continua).
    void FindNext()
    {
        var q = _find.Text.Trim();
        if (q.Length == 0 || _file is not { Ok: true }) return;
        var all = new List<TreeNode>();
        foreach (TreeNode pn in _policies.Nodes) { all.Add(pn); foreach (TreeNode tn in pn.Nodes) all.Add(tn); }
        int start = _policies.SelectedNode is { } sel ? all.IndexOf(sel) + 1 : 0;
        bool Hit(TreeNode n) => n.Tag switch
        {
            AiPolicy p => p.Id.ToString() == q,
            AiTrigger t => t.Id.ToString() == q || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || n.Text.Contains(q, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        for (int k = 0; k < all.Count; k++)
        {
            var n = all[(start + k) % all.Count];
            if (!Hit(n)) continue;
            n.EnsureVisible(); _policies.SelectedNode = n; _policies.Focus();
            return;
        }
        SetStatus(L($"Nada encontrado para \"{q}\"", $"Nothing found for \"{q}\""));
    }

    // ---------- tradução ----------

    /// <summary>Textos do escopo atual: operação selecionada > trigger aberto > política selecionada.</summary>
    IEnumerable<string> ScopeTexts(out string label)
    {
        label = "";
        if (_file is not { Ok: true }) return Enumerable.Empty<string>();
        if (_current is null)
        {
            if (_policies.SelectedNode?.Tag is AiPolicy p) { label = L($"política {p.Id}", $"policy {p.Id}"); return AiMeaning.PolicyTexts(p); }
            return Enumerable.Empty<string>();
        }
        if (_detail.SelectedNode?.Tag is NodeRef { Obj: AiOp op }) { label = L("operação", "operation"); return AiMeaning.OpTexts(op, _current.Version); }
        label = L($"trigger {_current.Id}", $"trigger {_current.Id}");
        return AiMeaning.TriggerTexts(_current);
    }

    /// <summary>Corpos (sem prefixo de canal) que estão em outra escrita e ainda não têm tradução (cache/glossário).</summary>
    List<string> Pending(IEnumerable<string> raws)
    {
        var lang = TargetLang;
        return raws.Select(r => AiChannel.Split(r).Body)
                   .Where(b => TextScript.NeedsTranslation(b, lang))
                   .Distinct()
                   .Where(b => _tr.TryGet(b, lang) is null)
                   .ToList();
    }

    Task TranslateSelection()
    {
        if (_cts != null) { _cts.Cancel(); return Task.CompletedTask; }
        var texts = ScopeTexts(out var label);
        return RunTranslation(Pending(texts), label, askAbove: 40);
    }

    Task TranslateAll()
    {
        if (_cts != null) { _cts.Cancel(); return Task.CompletedTask; }
        if (_file is not { Ok: true }) return Task.CompletedTask;
        return RunTranslation(Pending(AiMeaning.FileTexts(_file)), L("arquivo inteiro", "whole file"), askAbove: 0);
    }

    async Task RunTranslation(List<string> todo, string scope, int askAbove)
    {
        var lang = TargetLang;
        if (todo.Count == 0)
        {
            AfterTranslation();
            SetStatus(L($"Nada a traduzir em {scope}: já traduzido (cache/glossário) ou já está em {AppLang.NameOf(lang)}.",
                        $"Nothing to translate in {scope}: already translated (cache/glossary) or already in {AppLang.NameOf(lang)}."));
            return;
        }
        if (todo.Count > askAbove &&
            MessageBox.Show(this,
                L($"Traduzir {todo.Count:N0} texto(s) distintos para {AppLang.NameOf(lang)} usando {_tr.ProviderName}?\n\n" +
                  "Os textos serão enviados à internet. O resultado fica salvo em cache (não será pedido de novo) e " +
                  "NÃO altera o aipolicy.data.",
                  $"Translate {todo.Count:N0} distinct text(s) to {AppLang.NameOf(lang)} using {_tr.ProviderName}?\n\n" +
                  "The texts will be sent over the internet. The result is cached (not requested again) and " +
                  "does NOT change aipolicy.data."),
                "AI Policy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _cts = new CancellationTokenSource();
        ApplyTexts(); UpdateButtons();
        var progress = new Progress<TranslateProgress>(p =>
            SetStatus(p.WaitSeconds > 0
                ? L($"Limite de uso do serviço: aguardando {p.WaitSeconds} s para continuar ({p.Done:N0} / {p.Total:N0})…",
                    $"Service rate limit: waiting {p.WaitSeconds} s before continuing ({p.Done:N0} / {p.Total:N0})…")
                : L($"Traduzindo… {p.Done:N0} / {p.Total:N0}", $"Translating… {p.Done:N0} / {p.Total:N0}")));
        try
        {
            var r = await _tr.TranslateAsync(todo, lang, progress, _cts.Token);
            string msg = L($"Tradução ({scope}): {r.Translated:N0} ok", $"Translation ({scope}): {r.Translated:N0} ok");
            if (r.Failed > 0) msg += L($" · {r.Failed:N0} falharam", $" · {r.Failed:N0} failed");
            if (r.NotTried > 0) msg += L($" · {r.NotTried:N0} pendentes (serviço fora do ar: clique de novo para continuar)",
                                         $" · {r.NotTried:N0} pending (service down: click again to resume)");
            if (r.LastError != null && (r.Failed > 0 || r.NotTried > 0)) msg += " · " + r.LastError;
            SetStatus(msg);
        }
        catch (OperationCanceledException) { SetStatus(L("Tradução cancelada (o que já foi traduzido ficou em cache).", "Translation cancelled (what was already translated stays cached).")); }
        catch (Exception ex) { SetStatus(L("Erro na tradução: ", "Translation error: ") + ex.Message); }
        finally
        {
            _cts?.Dispose(); _cts = null;
            ApplyTexts(); UpdateButtons();
            AfterTranslation();
        }
    }

    void AfterTranslation()
    {
        if (_file is not { Ok: true }) return;
        RefreshLabels();
        if (_current != null) ShowFields(_detail.SelectedNode);
        RefreshMeaning();
    }

    // ---------- arquivo ----------

    void SaveFile()
    {
        if (_file is not { Ok: true } || _path is null) return;
        try
        {
            var bk = _file.Save(_path);
            _dirty = false; _save.Enabled = false;
            SetStatus($"Salvo: {Path.GetFileName(_path)}" + (bk is null ? "" : $" · backup {Path.GetFileName(bk)}") + " — grave o MESMO arquivo no cliente (data/) e no servidor (gamed/config/).");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Erro ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Salvar tudo / Salvar e enviar ao servidor.</summary>
    public bool HasUnsaved => _dirty;
    public string? FilePath => _file is { Ok: true } ? _path : null;
    public string? SaveIfDirty() { if (!_dirty) return null; SaveFile(); return _dirty ? null : _path; }

    void MarkDirty() { _dirty = true; UpdateButtons(); SetStatus(L("Alterado — não salvo.", "Changed — not saved.")); }

    void UpdateButtons()
    {
        bool ok = _file is { Ok: true };
        _save.Enabled = ok && _dirty;
        _add.Enabled = ok && _current != null;
        _del.Enabled = ok && _current != null;
        _clone.Enabled = ok && _policies.SelectedNode?.Tag is AiPolicy or AiTrigger;
        _trSel.Enabled = ok && (_cts != null || _current != null || _policies.SelectedNode?.Tag is AiPolicy);
        _trAll.Enabled = ok;
    }

    void SetStatus(string s) => _status.Text = s;
}
