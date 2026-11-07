using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Receita de um espaço da fornalha, na ordem da janela Fabricar do jogo: o que sai, os materiais em espaços com a quantidade
/// logo abaixo, o que o jogador precisa ter e o custo. Ou uma receita que já existe.
/// </summary>
internal sealed class RecipeWindow : Form
{
    public const int Slots = 8;
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Card = Color.FromArgb(36, 41, 49), Field = Color.FromArgb(44, 50, 60), Fg = Color.Gainsboro, Line = Color.FromArgb(70, 78, 92);

    readonly IQuestHost _host;
    readonly Button _productSlot = SlotButton(56);
    readonly Label _productName = new() { AutoSize = true, MaximumSize = new Size(390, 0), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 4, 0, 2) };
    readonly NumericUpDown _count = Number(1, 9999, 80);
    readonly NumericUpDown _price = Number(0, 999_999_999, 130);
    readonly NumericUpDown _time = new() { Minimum = 0.1m, Maximum = 500, DecimalPlaces = 1, Value = 2, Width = 80, BackColor = Field, ForeColor = Color.White };
    readonly NumericUpDown _level = Number(0, 10, 70);
    readonly ComboBox _skill = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, BackColor = Field, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    readonly Label _needsNote = new() { AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.Silver, Margin = new Padding(0, 4, 0, 0) };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(248, 113, 113), MaximumSize = new Size(470, 0), Margin = new Padding(0, 6, 0, 0) };
    readonly Button[] _matSlots = new Button[Slots];
    readonly NumericUpDown[] _matNums = new NumericUpDown[Slots];
    readonly int[] _mats = new int[Slots];
    readonly ItemTipHost _tip;
    readonly List<int> _skills = [0];
    int _product;

    public RecipeDraft? Result { get; private set; }
    /// <summary>Profissão escolhida: uma por fornalha, vale para todas as receitas dela.</summary>
    public int Skill => _skills[Math.Max(0, _skill.SelectedIndex)];

    static NumericUpDown Number(int min, int max, int width) => new() { Minimum = min, Maximum = max, Value = min, Width = width, BackColor = Field, ForeColor = Color.White, ThousandsSeparator = max > 9999 };

    static Button SlotButton(int size)
    {
        var b = new Button { Size = new Size(size, size), FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Color.Silver, Font = new Font("Segoe UI", 16f), Text = "+", Margin = Padding.Empty, Cursor = Cursors.Hand, TabStop = false, BackgroundImageLayout = ImageLayout.Stretch };
        b.FlatAppearance.BorderColor = Line;
        return b;
    }

    public RecipeWindow(IQuestHost host, RecipeDraft? had, int skill = 0)
    {
        _host = host; _tip = new ItemTipHost(this, id => host.TipImage(id));
        Text = Loc.T("rw.title", "Receita da fornalha");
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);

        var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 14, 16, 22) };
        FlowLayoutPanel Section(string title)
        {
            var c = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Card, Padding = new Padding(12, 8, 12, 12), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(484, 0) };
            c.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 0, 0, 6) });
            body.Controls.Add(c);
            return c;
        }
        Button Btn(string glyph, string text, Action a, bool main = false)
        {
            var b = new Button { Text = "  " + text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Field, Image = UiKit.Icon(glyph, Color.White, 16),
                ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Margin = new Padding(0, 0, 8, 0) };
            b.FlatAppearance.BorderSize = main ? 0 : 1; b.FlatAppearance.BorderColor = Line;
            b.Click += (_, _) => a();
            return b;
        }
        FlowLayoutPanel Row() => new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        Label Name(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };

        var makes = Section(Loc.T("rw.makes", "1. O que ela faz"));
        var prod = Row();
        var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(12, 0, 0, 0) };
        var count = Row();
        count.Controls.Add(Name(Loc.T("rw.count2", "Quantos saem por tentativa"))); count.Controls.Add(_count);
        side.Controls.Add(_productName); side.Controls.Add(count);
        prod.Controls.Add(_productSlot); prod.Controls.Add(side);
        makes.Controls.Add(prod);
        _productSlot.Click += (_, _) => PickProduct();
        _productSlot.MouseEnter += (_, _) => _tip.Hover(_product);
        _productSlot.MouseLeave += (_, _) => _tip.Hide();

        var needs = Section(Loc.T("rw.needs2", "2. Materiais: clique num espaço e ajuste a quantidade embaixo"));
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 4, Margin = Padding.Empty };
        for (int i = 0; i < Slots; i++)
        {
            int at = i;
            _matSlots[i] = SlotButton(50);
            _matSlots[i].Margin = new Padding(26, 4, 26, 2);
            _matNums[i] = Number(1, 9999, 90);
            _matNums[i].Margin = new Padding(6, 0, 6, 6);
            _matNums[i].Visible = false;
            _matSlots[i].MouseDown += (_, e) => { _tip.Hide(); if (e.Button == MouseButtons.Right) SetMaterial(at, 0); else PickMaterial(at); };
            _matSlots[i].MouseEnter += (_, _) => _tip.Hover(_mats[at]);
            _matSlots[i].MouseLeave += (_, _) => _tip.Hide();
            grid.Controls.Add(_matSlots[i], i % 4, i / 4 * 2);
            grid.Controls.Add(_matNums[i], i % 4, i / 4 * 2 + 1);
        }
        needs.Controls.Add(grid);
        needs.Controls.Add(new Label { Text = Loc.T("rw.matHint", "Botão direito tira o material."), AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(0, 2, 0, 0) });

        var req = Section(Loc.T("rw.requires", "3. O que o jogador precisa ter"));
        _skill.Items.Add(Loc.T("gs.noSkill", "Nada"));
        foreach (var (id, name) in host.MakeSkills()) { _skill.Items.Add(name); _skills.Add(id); }
        _skill.SelectedIndex = Math.Max(0, _skills.IndexOf(skill));
        _skill.SelectedIndexChanged += (_, _) => ShowNeeds();
        var who = Row();
        who.Controls.Add(Name(Loc.T("rw.skill", "Profissão"))); who.Controls.Add(_skill);
        who.Controls.Add(new Label { Width = 14 });
        who.Controls.Add(Name(Loc.T("rw.levelShort", "Nível"))); who.Controls.Add(_level);
        req.Controls.Add(who); req.Controls.Add(_needsNote);

        var costs = Section(Loc.T("rw.cost", "4. Custo de fabricar"));
        var cost = Row();
        cost.Controls.Add(Name(Loc.T("rw.price", "Moedas"))); cost.Controls.Add(_price);
        cost.Controls.Add(new Label { Width = 18 });
        cost.Controls.Add(Name(Loc.T("rw.time", "Tempo (s)"))); cost.Controls.Add(_time);
        costs.Controls.Add(cost);
        body.Controls.Add(_error);

        var end = Row();
        end.Margin = new Padding(0, 10, 0, 0);
        end.Controls.Add(Btn("", Loc.T("rw.ok", "Pôr esta receita"), Accept, main: true));
        end.Controls.Add(Btn("", Loc.T("rw.useExisting", "Usar uma receita que já existe…"), UseExisting));
        end.Controls.Add(Btn("", Loc.T("rw.cancel", "Cancelar"), () => { DialogResult = DialogResult.Cancel; }));
        body.Controls.Add(end);
        Controls.Add(body);
        FormClosed += (_, _) => _tip.Dispose();

        if (had is { RecipeId: 0 })
        {
            _product = had.Product; _count.Value = Math.Clamp(had.Count, 1, 9999); _price.Value = Math.Clamp(had.Price, 0, 999999999);
            _time.Value = Math.Clamp((decimal)had.Duration, 0.1m, 500);
            _level.Value = Math.Clamp(had.SkillLevel, 0, 10);
            for (int i = 0; i < Slots && i < had.Materials.Count; i++) SetMaterial(i, had.Materials[i].Id, had.Materials[i].Num);
        }
        ShowProduct(); ShowNeeds();
    }

    void ShowNeeds()
    {
        bool any = Skill > 0;
        _level.Enabled = any;
        _needsNote.Text = any ? Loc.T("rw.skillNote", "A profissão vale para toda esta fornalha. O nível é só desta receita.")
            : Loc.T("rw.noSkillNote", "Qualquer jogador pode fabricar aqui.");
    }

    void ShowProduct()
    {
        _productSlot.BackgroundImage = _product > 0 ? _host.ItemIcon(_product) : null;
        _productSlot.Text = _product > 0 ? "" : "+";
        _productName.Text = _product > 0 ? _host.NameOf(_product) ?? _product.ToString() : Loc.T("rw.noProduct2", "Clique no espaço para escolher o item");
    }

    void PickProduct() { if (_host.PickItemId(this, false) is { } p) { _product = p.Id; ShowProduct(); } }

    void PickMaterial(int at) { if (_host.PickItemId(this, false) is { } p) SetMaterial(at, p.Id, Math.Max(1, (int)_matNums[at].Value)); }

    void SetMaterial(int at, int id, int num = 1)
    {
        _mats[at] = id;
        _matSlots[at].BackgroundImage = id > 0 ? _host.ItemIcon(id) : null;
        _matSlots[at].Text = id > 0 ? "" : "+";
        _matNums[at].Value = Math.Clamp(num, 1, 9999);
        _matNums[at].Visible = id > 0;
    }

    void UseExisting()
    {
        if (_host.PickRecipeId(this) is not { } id) return;
        Result = new RecipeDraft { RecipeId = id };
        DialogResult = DialogResult.OK;
    }

    void Accept()
    {
        var list = Enumerable.Range(0, Slots).Where(i => _mats[i] > 0).Select(i => (_mats[i], (int)_matNums[i].Value)).ToList();
        if (_product <= 0) { _error.Text = Loc.T("rw.needProduct", "Escolha o item que a receita faz."); return; }
        if (list.Count == 0) { _error.Text = Loc.T("rw.needMaterial", "Adicione pelo menos um material."); return; }
        if (list.Select(x => x.Item1).Distinct().Count() != list.Count) { _error.Text = Loc.T("rw.sameMaterial", "O mesmo material está em dois espaços: use um só e aumente a quantidade."); return; }
        Result = new RecipeDraft { Product = _product, Count = (int)_count.Value, Price = (int)_price.Value, Duration = (float)_time.Value, SkillLevel = Skill > 0 ? (int)_level.Value : 0, Materials = list };
        DialogResult = DialogResult.OK;
    }
}
