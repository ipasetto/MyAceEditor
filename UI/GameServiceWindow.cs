using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela do jogo no centro do Mundo 3D para montar a loja (interfaces\version01\shop.xml) ou a fornalha
/// (produce_icon.xml): abas Btn_Set1..8 = páginas, U_01..U_32 = espaços. Clique escolhe, botão direito tira, passar o mouse
/// mostra a dica do item. Trabalha num rascunho com Ctrl+Z próprio; nada entra no elements.data até o Concluir.
/// </summary>
internal sealed class GameServiceWindow : Panel
{
    const float Scale = 1.25f;
    const int Pad = 14, Foot = 108;
    static readonly Color Gold = Color.FromArgb(255, 203, 74), Sel = Color.FromArgb(255, 220, 90), TextC = Color.FromArgb(235, 235, 235);

    readonly IQuestHost _host;
    readonly bool _forge;
    readonly PckArchive? _ifc, _surf;
    readonly GameTextures? _tex;
    readonly GameDialog? _dlg;
    readonly Bitmap? _base;
    readonly Dictionary<int, Image?> _icons = [];
    readonly ItemTipHost _tip;
    readonly TextBox _title = new() { Width = 180, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly Label _hint = new() { AutoSize = false, ForeColor = Color.Silver };
    readonly List<object> _undo = [], _redo = [];
    int _page, _sel = -1;
    bool _binding;

    public ShopDraft Shop { get; private set; } = new();
    public MakeDraft Make { get; private set; } = new();
    /// <summary>Algo mudou no rascunho (o painel da ferramenta atualiza a contagem).</summary>
    public event Action? Changed;

    public GameServiceWindow(string elementDir, bool forge, IQuestHost host)
    {
        _forge = forge; _host = host; _tip = new ItemTipHost(this, id => host.TipImage(id));
        DoubleBuffered = true; BackColor = Color.FromArgb(24, 28, 34); BorderStyle = BorderStyle.FixedSingle;
        try
        {
            string ip = Path.Combine(elementDir, "interfaces.pck"), sp = Path.Combine(elementDir, "surfaces.pck");
            if (File.Exists(ip) && File.Exists(sp))
            {
                _ifc = new PckArchive(ip); _surf = new PckArchive(sp); _tex = new GameTextures(_surf);
                GameFonts.Load(elementDir);
                _dlg = GameDialog.Load(_ifc, forge ? @"interfaces\version01\produce_icon.xml" : @"interfaces\version01\shop.xml");
                if (_dlg is not null)
                {
                    // textos de exemplo do arquivo da janela ficariam por baixo do que desenhamos
                    foreach (var (name, c) in _dlg.Controls.ToList())
                        if (c.Text is { Length: > 0 } && (name.StartsWith("Btn_Set", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Txt_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Lab_Bind", StringComparison.OrdinalIgnoreCase) || c.Color is { R: > 200, G: < 60 }))
                            _dlg.Controls[name] = c with { Text = null };
                    _base = GameDraw.Render(_dlg, _tex, outlines: false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("janela do jogo", ex); }
        var size = _dlg?.Size ?? new Size(420, 360);
        Size = new Size((int)(size.Width * Scale) + Pad * 2, (int)(size.Height * Scale) + Pad * 2 + Foot);

        int y = Height - Foot + 4;
        var pageName = new Label { Text = Loc.T("gs.pageName", "Nome da aba"), AutoSize = true, ForeColor = Color.Silver, Location = new Point(Pad, y + 4) };
        _title.Location = new Point(Pad + 86, y);
        _title.TextChanged += (_, _) => { if (_binding) return; Remember(); Titles[_page] = _title.Text; Invalidate(); Changed?.Invoke(); };
        _hint.Location = new Point(Pad, y + 32); _hint.Size = new Size(Width - Pad * 2, 20);
        _hint.Text = forge ? Loc.T("gs.hintMake", "Clique num espaço para pôr uma receita. Botão direito tira.")
            : Loc.T("gs.hintShop", "Clique num espaço para pôr um item à venda. Botão direito tira.");
        Button Btn(string glyph, string text, Action a, int x, int w, bool main = false)
        {
            var b = new Button { Text = "  " + text, Location = new Point(x, y + 56), Size = new Size(w, 30), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = main ? UiKit.Accent : Color.FromArgb(44, 50, 60),
                Image = UiKit.Icon(glyph, Color.White, 16), ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText };
            b.FlatAppearance.BorderSize = main ? 0 : 1; b.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 92);
            b.Click += (_, _) => a();
            return b;
        }
        Controls.Add(pageName); Controls.Add(_title); Controls.Add(_hint);
        Controls.Add(Btn("", Loc.T("gs.undo", "Desfazer"), () => Undo(), Pad, 104));
        Controls.Add(Btn("", Loc.T("gs.redo", "Refazer"), () => Redo(), Pad + 110, 104));
        Controls.Add(Btn("", Loc.T("gs.done", "Pronto"), () => Visible = false, Width - Pad - 110, 110, main: true));
        BindTitle();
    }

    string[] Titles => _forge ? Make.Titles : Shop.Titles;

    public void Load(ShopDraft shop) { Shop = shop; _undo.Clear(); _redo.Clear(); _page = 0; _sel = -1; BindTitle(); Invalidate(); }
    public void Load(MakeDraft make) { Make = make; _undo.Clear(); _redo.Clear(); _page = 0; _sel = -1; BindTitle(); Invalidate(); }

    void BindTitle() { _binding = true; _title.Text = Titles[_page]; _binding = false; }

    object Snapshot() => _forge ? Make.Copy() : Shop.Copy();
    void Restore(object s) { if (s is MakeDraft m) Make = m; else if (s is ShopDraft d) Shop = d; _sel = -1; BindTitle(); Invalidate(); Changed?.Invoke(); }
    void Remember() { _undo.Add(Snapshot()); if (_undo.Count > 100) _undo.RemoveAt(0); _redo.Clear(); }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Snapshot()); var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Restore(s);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Snapshot()); var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Restore(s);
        return true;
    }

    Rectangle R(string name) => _dlg?[name]?.Rect ?? Rectangle.Empty;
    Point ToDialog(Point p) => new((int)((p.X - Pad) / Scale), (int)((p.Y - Pad) / Scale));
    int SlotAt(Point d) { for (int i = 0; i < NpcBuilder.Slots; i++) if (R($"U_{i + 1:00}").Contains(d)) return i; return -1; }
    int IdIn(int slot) => slot < 0 ? 0 : _forge ? Make.Goods[_page][slot] is { } r ? ProductOf(r) : 0 : Shop.Goods[_page][slot];
    int ProductOf(RecipeDraft r) => r.RecipeId > 0 ? _host.RecipeProduct(r.RecipeId) : r.Product;

    Image? IconOf(int id)
    {
        if (id <= 0) return null;
        if (!_icons.TryGetValue(id, out var img)) _icons[id] = img = _host.ItemIcon(id);
        return img;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (_dlg is null || _base is null)
        {
            TextRenderer.DrawText(g, Loc.T("make.noUi", "Janela do jogo não encontrada (interfaces.pck / surfaces.pck ao lado do elements.data)."), Font, new Rectangle(Pad, Pad, Width - Pad * 2, 60), Color.Gainsboro, TextFormatFlags.WordBreak);
            return;
        }
        var state = g.Save();
        g.TranslateTransform(Pad, Pad);
        g.ScaleTransform(Scale, Scale);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(_base, 0, 0, _dlg.Size.Width, _dlg.Size.Height);
        for (int p = 0; p < NpcBuilder.Pages; p++)
        {
            var r = R($"Btn_Set{p + 1}");
            if (r.IsEmpty) continue;
            if (p == _page) using (var pen = new Pen(Sel, 2)) g.DrawRectangle(pen, Rectangle.Inflate(r, -1, -1));
            var title = Titles[p].Length > 0 ? Titles[p] : (p + 1).ToString();
            GameDraw.Text(g, title, Rectangle.Inflate(r, -2, 0), p == _page ? Sel : TextC, 10);
        }
        for (int i = 0; i < NpcBuilder.Slots; i++)
        {
            var r = R($"U_{i + 1:00}");
            if (r.IsEmpty) continue;
            if (IconOf(IdIn(i)) is { } img) g.DrawImage(img, r);
            if (i == _sel) using (var pen = new Pen(Sel, 2)) g.DrawRectangle(pen, Rectangle.Inflate(r, 1, 1));
        }
        if (_forge && _sel >= 0 && Make.Goods[_page][_sel] is { } rec) DrawRecipe(g, rec);
        g.Restore(state);
    }

    void DrawRecipe(Graphics g, RecipeDraft rec)
    {
        var mats = rec.RecipeId > 0 ? _host.RecipeMaterials(rec.RecipeId) : rec.Materials;
        for (int m = 1; m <= 8 && m <= mats.Count; m++)
        {
            if (IconOf(mats[m - 1].Id) is { } mi) g.DrawImage(mi, R($"Item_{m:00}"));
            GameDraw.Text(g, $"x{mats[m - 1].Num}", R($"Txt_no{m}"), TextC, 10);
        }
        int prod = ProductOf(rec);
        if (IconOf(prod) is { } pi) g.DrawImage(pi, R("Item_out"));
        GameDraw.Text(g, _host.NameOf(prod) ?? "", new Rectangle(R("Txt_out").X, R("Txt_out").Y - 2, 160, 21), Gold, 10);
        GameDraw.Text(g, rec.Price.ToString(), R("Txt_Gold"), TextC, 10, align: ContentAlignment.MiddleRight);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_dlg is null) return;
        var d = ToDialog(e.Location);
        for (int p = 0; p < NpcBuilder.Pages; p++)
            if (R($"Btn_Set{p + 1}").Contains(d)) { _page = p; _sel = -1; BindTitle(); Invalidate(); return; }
        int s = SlotAt(d);
        if (s < 0) return;
        _tip.Hide();
        if (e.Button == MouseButtons.Right) { ClearSlot(s); return; }
        if (e.Button != MouseButtons.Left) return;
        if (_forge) EditRecipe(s); else PickItem(s);
    }

    void ClearSlot(int s)
    {
        if (IdIn(s) == 0 && (!_forge || Make.Goods[_page][s] is null)) return;
        Remember();
        if (_forge) Make.Goods[_page][s] = null; else Shop.Goods[_page][s] = 0;
        if (_sel == s) _sel = -1;
        Invalidate(); Changed?.Invoke();
    }

    void PickItem(int s)
    {
        if (_host.PickItemId(this, false) is not { } picked) return;
        Remember();
        Shop.Goods[_page][s] = picked.Id;
        Invalidate(); Changed?.Invoke();
    }

    void EditRecipe(int s)
    {
        var had = Make.Goods[_page][s];
        if (had is not null && _sel != s) { _sel = s; Invalidate(); return; }   // 1º clique mostra os materiais, o 2º edita
        using var w = new RecipeWindow(_host, had?.Copy());
        if (w.ShowDialog(FindForm()) != DialogResult.OK) return;
        Remember();
        Make.Goods[_page][s] = w.Result;
        _sel = s;
        Invalidate(); Changed?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dlg is null) return;
        var d = ToDialog(e.Location);
        int id = IdIn(SlotAt(d));
        if (id == 0 && _forge && _sel >= 0 && Make.Goods[_page][_sel] is { } rec)
        {
            var mats = rec.RecipeId > 0 ? _host.RecipeMaterials(rec.RecipeId) : rec.Materials;
            for (int m = 1; m <= 8 && m <= mats.Count; m++) if (R($"Item_{m:00}").Contains(d)) id = mats[m - 1].Id;
            if (R("Item_out").Contains(d)) id = ProductOf(rec);
        }
        _tip.Hover(id);
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _tip.Hide(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) _tip.Hide(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tip.Dispose(); _base?.Dispose(); _tex?.Dispose(); _ifc?.Dispose(); _surf?.Dispose(); }
        base.Dispose(disposing);
    }
}
