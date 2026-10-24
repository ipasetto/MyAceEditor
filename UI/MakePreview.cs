using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Prévia do serviço de fabricação do NPC (NPC_MAKE_SERVICE) com a janela do jogo: interfaces\version01\produce_icon.xml
/// desenhada com as texturas do surfaces.pck. As abas Btn_Set1..8 são as páginas (título), a grade U_01..U_32 mostra o
/// item que cada receita produz; ao clicar numa receita aparecem os materiais (Item_01..08 + Txt_no), o item final,
/// o ouro e a habilidade exigida, como no jogo. Duplo clique abre a receita; clique num material abre o item.
/// </summary>
internal sealed class MakePreview : Panel
{
    const string Xml = @"interfaces\version01\produce_icon.xml";
    const float Scale = 1.35f;
    const int Pad = 12;
    static readonly Color Gold = Color.FromArgb(255, 203, 74), Sel = Color.FromArgb(255, 220, 90), TextC = Color.FromArgb(235, 235, 235);

    readonly Func<string?> _elementDir;
    readonly Func<string, Item?> _recipe, _item;
    readonly Func<Item, Image?> _icon;
    readonly Func<int, string?> _skillName;
    readonly Action<Item> _go;
    readonly ToolTip _tip = new() { InitialDelay = 150, AutoPopDelay = 20000 };

    string? _loadedFor;
    PckArchive? _ifc, _surf;
    GameTextures? _tex;
    GameDialog? _dlg;
    Bitmap? _base;

    Item? _svc;
    int _page, _sel = -1;
    readonly Dictionary<Item, Image?> _icons = new(ReferenceEqualityComparer.Instance);
    string _tipText = "";
    int _lastSlot = -1;
    long _lastClick;

    public MakePreview(Func<string?> elementDir, Func<string, Item?> recipe, Func<string, Item?> item, Func<Item, Image?> icon,
        Func<int, string?> skillName, Action<Item> go)
    {
        _elementDir = elementDir; _recipe = recipe; _item = item; _icon = icon; _skillName = skillName; _go = go;
        DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(24, 28, 34);
    }

    void EnsureLoaded()
    {
        var dir = _elementDir();
        if (dir == _loadedFor) return;
        _loadedFor = dir;
        _base?.Dispose(); _base = null; _dlg = null; _tex?.Dispose(); _tex = null; _ifc?.Dispose(); _ifc = null; _surf?.Dispose(); _surf = null;
        if (dir is null) return;
        try
        {
            var ip = Path.Combine(dir, "interfaces.pck"); var sp = Path.Combine(dir, "surfaces.pck");
            if (!File.Exists(ip) || !File.Exists(sp)) return;
            _ifc = new PckArchive(ip); _surf = new PckArchive(sp); _tex = new GameTextures(_surf);
            GameFonts.Load(dir);
            _dlg = GameDialog.Load(_ifc, Xml);
            if (_dlg is not null) _base = GameDraw.Render(_dlg, _tex, outlines: false);
        }
        catch { _dlg = null; }
        AutoScrollMinSize = _dlg is null ? Size.Empty : new Size((int)(_dlg.Size.Width * Scale) + Pad * 2, (int)(_dlg.Size.Height * Scale) + Pad * 2);
    }

    public void ShowService(Item svc)
    {
        EnsureLoaded();
        if (!ReferenceEquals(_svc, svc)) { _page = 0; _sel = -1; }
        _svc = svc; _icons.Clear();
        if (_page >= Pages().Count) _page = 0;
        Invalidate();
    }

    static string? Val(Item it, string key) => it.Fields.TryGetValue(key, out var f) && f.Value is not ("" or "0" or "-1") ? f.Value : null;

    // páginas: título + 32 IDs de receita (nomes de campo das duas formas usadas pelas versões)
    List<(string Title, string?[] Goods)> Pages()
    {
        var list = new List<(string, string?[])>();
        if (_svc is null) return list;
        for (int p = 1; p <= 8; p++)
        {
            var g = new string?[32];
            for (int n = 1; n <= 32; n++) g[n - 1] = Val(_svc, $"pages_{p}_id_goods_{n}") ?? Val(_svc, $"pages_{p}_goods_{n}_id");
            var t = _svc.Fields.TryGetValue($"pages_{p}_page_title", out var tf) ? TasksFile.SplitColor(tf.Value).Text.Trim() : "";
            list.Add((t, g));
        }
        return list;
    }

    Item? Product(Item recipe) => Val(recipe, "targets_1_id_to_make") is { } id ? _item(id) : null;

    // materiais preenchidos em ordem (a receita pode usar slots espalhados, ex. 2, 7 e 11; o jogo mostra em sequência)
    static List<(string Id, string Num)> Materials(Item recipe)
    {
        var list = new List<(string, string)>();
        for (int m = 1; recipe.Fields.ContainsKey($"materials_{m}_id"); m++)
            if (Val(recipe, $"materials_{m}_id") is { } id) list.Add((id, Val(recipe, $"materials_{m}_num") ?? "1"));
        return list;
    }

    Image? IconOf(Item? it)
    {
        if (it is null) return null;
        if (!_icons.TryGetValue(it, out var img)) _icons[it] = img = _icon(it);
        return img;
    }

    Rectangle R(string name) => _dlg?[name]?.Rect ?? Rectangle.Empty;

    Item? SelectedRecipe()
    {
        var pages = Pages();
        return _sel >= 0 && _page < pages.Count && pages[_page].Goods[_sel] is { } rid ? _recipe(rid) : null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (_dlg is null || _base is null)
        {
            TextRenderer.DrawText(g, Loc.T("make.noUi", "Janela do jogo não encontrada (interfaces.pck / surfaces.pck ao lado do elements.data)."), Font, new Point(Pad, Pad), Color.Gray);
            return;
        }
        g.TranslateTransform(Pad + AutoScrollPosition.X, Pad + AutoScrollPosition.Y);
        g.ScaleTransform(Scale, Scale);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(_base, 0, 0, _dlg.Size.Width, _dlg.Size.Height);
        if (_svc is null) return;
        var pages = Pages();

        // habilidade de fabricação do serviço
        if (Val(_svc, "id_make_skill") is { } ms && int.TryParse(ms, out var msi))
            GameDraw.Text(g, _skillName(msi) ?? ms, new Rectangle(R("Txt_CurrentSkillName").X, 34, 58, 20), Gold, 11, align: ContentAlignment.MiddleLeft);

        // abas = páginas
        for (int p = 0; p < 8; p++)
        {
            var r = R($"Btn_Set{p + 1}");
            if (r.IsEmpty) continue;
            bool any = pages[p].Goods.Any(x => x is not null);
            if (p == _page) using (var pen = new Pen(Sel, 2)) g.DrawRectangle(pen, Rectangle.Inflate(r, -1, -1));
            var title = pages[p].Title.Length > 0 ? pages[p].Title : any ? (p + 1).ToString() : "";
            if (title.Length > 0) GameDraw.Text(g, title, Rectangle.Inflate(r, -2, 0), p == _page ? Sel : TextC, 10);
        }

        // receitas da página: ícone do item produzido
        var goods = pages[_page].Goods;
        for (int i = 0; i < 32; i++)
        {
            if (goods[i] is not { } rid) continue;
            var r = R($"U_{i + 1:00}");
            var recipe = _recipe(rid);
            var img = recipe is null ? null : IconOf(Product(recipe));
            if (img is not null) g.DrawImage(img, r);
            else using (var b = new SolidBrush(Color.FromArgb(120, 200, 60, 60))) g.FillRectangle(b, r);   // receita/produto não encontrado
            if (i == _sel) using (var pen = new Pen(Sel, 2)) g.DrawRectangle(pen, Rectangle.Inflate(r, 1, 1));
        }

        // receita selecionada: materiais, produto, ouro e habilidade
        if (SelectedRecipe() is not { } rec) return;
        var mats = Materials(rec);
        for (int m = 1; m <= 8 && m <= mats.Count; m++)
        {
            if (IconOf(_item(mats[m - 1].Id)) is { } mi) g.DrawImage(mi, R($"Item_{m:00}"));
            GameDraw.Text(g, $"x{mats[m - 1].Num}", R($"Txt_no{m}"), TextC, 10);
        }
        var prod = Product(rec);
        if (IconOf(prod) is { } pi) g.DrawImage(pi, R("Item_out"));
        GameDraw.Text(g, prod is null ? "" : TasksFile.SplitColor(prod.Name).Text, new Rectangle(R("Txt_out").X, R("Txt_out").Y - 2, 160, 21), Gold, 10, align: ContentAlignment.MiddleLeft);
        GameDraw.Text(g, "1", R("Txt_Number"), TextC, 10);
        GameDraw.Text(g, Val(rec, "price") ?? "0", R("Txt_Gold"), TextC, 10, align: ContentAlignment.MiddleRight);
        if (Val(rec, "id_skill") is { } sk && int.TryParse(sk, out var ski))
        {
            GameDraw.Text(g, _skillName(ski) ?? sk, new Rectangle(R("Txt_SkillName").X, 358, 64, 20), Gold, 10, align: ContentAlignment.MiddleLeft);
            GameDraw.Text(g, Val(rec, "skill_level") ?? "0", new Rectangle(R("Txt_SkillLevel").X, 358, 30, 20), Gold, 10, align: ContentAlignment.MiddleLeft);
        }
    }

    // posição do mouse em coordenadas da janela do jogo
    Point ToDialog(Point p) => new((int)((p.X - Pad - AutoScrollPosition.X) / Scale), (int)((p.Y - Pad - AutoScrollPosition.Y) / Scale));

    int SlotAt(Point d) { for (int i = 0; i < 32; i++) if (R($"U_{i + 1:00}").Contains(d)) return i; return -1; }

    // MouseDown (não MouseClick): neste painel o MouseClick nem sempre dispara e o clique "não respondia"
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _dlg is null || _svc is null) return;
        var d = ToDialog(e.Location);
        // duplo clique detectado aqui: este painel não recebe a mensagem de duplo clique do Windows
        int ds = SlotAt(d);
        bool dbl = ds >= 0 && ds == _lastSlot && Environment.TickCount64 - _lastClick <= SystemInformation.DoubleClickTime;
        _lastSlot = ds; _lastClick = Environment.TickCount64;
        if (dbl)
        {
            _lastSlot = -1;
            if (Pages()[_page].Goods[ds] is { } drid && _recipe(drid) is { } drec) _go(drec);
            return;
        }
        for (int p = 0; p < 8; p++) if (R($"Btn_Set{p + 1}").Contains(d)) { _page = p; _sel = -1; Invalidate(); return; }
        int s = SlotAt(d);
        if (s >= 0) { _sel = Pages()[_page].Goods[s] is null ? -1 : s; Invalidate(); return; }
        if (SelectedRecipe() is not { } rec) return;
        var mats = Materials(rec);
        for (int m = 1; m <= 8 && m <= mats.Count; m++)
            if (R($"Item_{m:00}").Contains(d) && _item(mats[m - 1].Id) is { } mat) { _go(mat); return; }
        if (R("Item_out").Contains(d) && Product(rec) is { } prod) _go(prod);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dlg is null || _svc is null) return;
        var d = ToDialog(e.Location);
        string t = "";
        // abas: o botão do jogo é estreito, o título inteiro aparece ao passar o mouse
        for (int p = 0; p < 8; p++)
            if (R($"Btn_Set{p + 1}").Contains(d) && Pages()[p] is var pg && (pg.Title.Length > 0 || pg.Goods.Any(x => x is not null)))
                t = string.Format(Loc.T("make.page", "Página {0}: {1} ({2} receitas)"), p + 1, pg.Title, pg.Goods.Count(x => x is not null));
        int s = SlotAt(d);
        if (s >= 0 && Pages()[_page].Goods[s] is { } rid)
        {
            var rec = _recipe(rid);
            if (rec is null) t = string.Format(Loc.T("make.missing", "Receita {0} não encontrada"), rid);
            else
            {
                var prod = Product(rec);
                var mats = Materials(rec).Select(x => $"  {x.Num} × {(_item(x.Id) is { } mi ? TasksFile.SplitColor(mi.Name).Text : x.Id)}");
                t = $"{TasksFile.SplitColor(prod?.Name ?? rec.Name).Text}\n{Loc.T("make.recipe", "Receita")} {rec.Id}: {TasksFile.SplitColor(rec.Name).Text}\n"
                    + string.Join("\n", mats) + "\n" + Loc.T("make.hint", "Clique: ver materiais · duplo clique: abrir a receita");
            }
        }
        if (t == _tipText) return;
        _tipText = t;
        // dica comum (SetToolTip): a aberta com Show() engolia o clique seguinte (abas e receitas "não respondiam")
        _tip.SetToolTip(this, t.Length == 0 ? null : t);
    }

    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { _tip.SetToolTip(this, null); _tipText = ""; } }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _base?.Dispose(); _tex?.Dispose(); _ifc?.Dispose(); _surf?.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
