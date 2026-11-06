using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Receita de um espaço da fornalha: o que produz, quantos, materiais, preço e tempo; ou uma receita que já existe.</summary>
internal sealed class RecipeWindow : Form
{
    static readonly Color Bg = Color.FromArgb(28, 32, 38), Field = Color.FromArgb(44, 50, 60), Fg = Color.Gainsboro;

    readonly IQuestHost _host;
    readonly ListView _mats = new() { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.None, Width = 470, Height = 190, Margin = new Padding(0, 4, 0, 6) };
    readonly ImageList _icons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    readonly PictureBox _productIcon = new() { Size = new Size(36, 36), SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Field, Margin = new Padding(0, 0, 8, 0) };
    readonly Label _productName = new() { AutoSize = true, ForeColor = Color.White, Margin = new Padding(0, 9, 12, 0), Font = new Font("Segoe UI Semibold", 10f) };
    readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 9999, Value = 1, Width = 80, BackColor = Field, ForeColor = Color.White };
    readonly NumericUpDown _price = new() { Minimum = 0, Maximum = 999999999, Width = 120, BackColor = Field, ForeColor = Color.White, ThousandsSeparator = true };
    readonly NumericUpDown _time = new() { Minimum = 0.1m, Maximum = 500, DecimalPlaces = 1, Value = 2, Width = 80, BackColor = Field, ForeColor = Color.White };
    readonly NumericUpDown _level = new() { Minimum = 0, Maximum = 10, Width = 70, BackColor = Field, ForeColor = Color.White };
    readonly Label _error = new() { AutoSize = true, ForeColor = Color.FromArgb(248, 113, 113), MaximumSize = new Size(470, 0), Margin = new Padding(0, 6, 0, 0) };
    readonly ItemTipHost _tip;
    readonly List<(int Id, int Num)> _list = [];
    int _product;

    public RecipeDraft? Result { get; private set; }

    public RecipeWindow(IQuestHost host, RecipeDraft? had, bool needsSkill = false)
    {
        _host = host; _tip = new ItemTipHost(this, id => host.TipImage(id));
        Text = Loc.T("rw.title", "Receita da fornalha");
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);

        var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18, 14, 18, 22) };
        Label Head(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(96, 165, 250), Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 10, 0, 4) };
        Button Btn(string glyph, string text, Action a, bool main = false)
        {
            var b = new Button { Text = "  " + text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Field, Image = UiKit.Icon(glyph, Color.White, 16),
                ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Margin = new Padding(0, 0, 8, 0) };
            b.FlatAppearance.BorderSize = main ? 0 : 1; b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 92);
            b.Click += (_, _) => a();
            return b;
        }
        FlowLayoutPanel Row() => new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        Label Name(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };

        var use = Row();
        use.Controls.Add(Btn("", Loc.T("rw.useExisting", "Usar uma receita que já existe…"), UseExisting));
        body.Controls.Add(use);

        body.Controls.Add(Head(Loc.T("rw.makes", "1. O que ela faz")));
        var prod = Row();
        prod.Controls.Add(_productIcon); prod.Controls.Add(_productName);
        prod.Controls.Add(Btn("", Loc.T("rw.pickProduct", "Escolher item…"), PickProduct));
        body.Controls.Add(prod);
        var nums = Row();
        nums.Controls.Add(Name(Loc.T("rw.count", "Quantidade"))); nums.Controls.Add(_count);
        body.Controls.Add(nums);

        body.Controls.Add(Head(Loc.T("rw.needs", "2. O que ela gasta")));
        _mats.Columns.Add(Loc.T("is.name", "Nome"), 370); _mats.Columns.Add(Loc.T("rw.qty", "Qtd."), 70);
        _mats.SmallImageList = _icons;
        _mats.MouseMove += (_, e) => _tip.Hover(_mats.GetItemAt(e.X, e.Y)?.Tag is int id ? id : 0);
        _mats.MouseLeave += (_, _) => _tip.Hide();
        body.Controls.Add(_mats);
        var mb = Row();
        mb.Controls.Add(Btn("", Loc.T("rw.addMaterial", "Adicionar material…"), AddMaterial));
        mb.Controls.Add(Btn("", Loc.T("rw.removeMaterial", "Tirar o marcado"), RemoveMaterial));
        body.Controls.Add(mb);
        var cost = Row();
        cost.Controls.Add(Name(Loc.T("rw.price", "Moedas"))); cost.Controls.Add(_price);
        cost.Controls.Add(new Label { Width = 18 });
        cost.Controls.Add(Name(Loc.T("rw.time", "Tempo (s)"))); cost.Controls.Add(_time);
        cost.Margin = new Padding(0, 10, 0, 2);
        body.Controls.Add(cost);
        if (needsSkill)
        {
            var lv = Row();
            lv.Controls.Add(Name(Loc.T("rw.level", "Nível da habilidade"))); lv.Controls.Add(_level);
            body.Controls.Add(lv);
        }
        body.Controls.Add(_error);

        var end = Row();
        end.Margin = new Padding(0, 14, 0, 0);
        end.Controls.Add(Btn("", Loc.T("rw.ok", "Pôr esta receita"), Accept, main: true));
        end.Controls.Add(Btn("", Loc.T("rw.cancel", "Cancelar"), () => { DialogResult = DialogResult.Cancel; }));
        body.Controls.Add(end);
        Controls.Add(body);
        FormClosed += (_, _) => _tip.Dispose();

        if (had is { RecipeId: 0 })
        {
            _product = had.Product; _count.Value = Math.Clamp(had.Count, 1, 9999); _price.Value = Math.Clamp(had.Price, 0, 999999999);
            _time.Value = Math.Clamp((decimal)had.Duration, 0.1m, 500); _list.AddRange(had.Materials);
            _level.Value = Math.Clamp(had.SkillLevel, 0, 10);
        }
        ShowProduct(); ShowMaterials();
    }

    void ShowProduct()
    {
        _productIcon.Image = _product > 0 ? _host.ItemIcon(_product) : null;
        _productName.Text = _product > 0 ? _host.NameOf(_product) ?? _product.ToString() : Loc.T("rw.noProduct", "Nenhum item escolhido");
    }

    void ShowMaterials()
    {
        _mats.BeginUpdate(); _mats.Items.Clear(); _icons.Images.Clear();
        foreach (var (id, num) in _list)
        {
            if (_host.ItemIcon(id) is { } img) _icons.Images.Add(id.ToString(), img);
            _mats.Items.Add(new ListViewItem([_host.NameOf(id) ?? id.ToString(), num.ToString()], id.ToString()) { Tag = id });
        }
        _mats.EndUpdate();
    }

    void PickProduct() { if (_host.PickItemId(this, false) is { } p) { _product = p.Id; ShowProduct(); } }

    void AddMaterial()
    {
        if (_host.PickItemId(this, true) is not { } p) return;
        int at = _list.FindIndex(x => x.Id == p.Id);
        if (at >= 0) _list[at] = (p.Id, p.Quantity); else _list.Add((p.Id, p.Quantity));   // mesmo item de novo: troca a quantidade
        ShowMaterials();
    }

    void RemoveMaterial()
    {
        if (_mats.SelectedItems.Count == 0 || _mats.SelectedItems[0].Tag is not int id) return;
        _list.RemoveAll(x => x.Id == id);
        ShowMaterials();
    }

    void UseExisting()
    {
        if (_host.PickRecipeId(this) is not { } id) return;
        Result = new RecipeDraft { RecipeId = id };
        DialogResult = DialogResult.OK;
    }

    void Accept()
    {
        if (_product <= 0) { _error.Text = Loc.T("rw.needProduct", "Escolha o item que a receita faz."); return; }
        if (_list.Count == 0) { _error.Text = Loc.T("rw.needMaterial", "Adicione pelo menos um material."); return; }
        Result = new RecipeDraft { Product = _product, Count = (int)_count.Value, Price = (int)_price.Value, Duration = (float)_time.Value, SkillLevel = (int)_level.Value, Materials = [.. _list] };
        DialogResult = DialogResult.OK;
    }
}
