using MyAceEditor.Model;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Prévia da loja do NPC (NPC_SELL_SERVICE) como no jogo: até 8 páginas com 32 itens (grade 8 x 4).
/// Passar o mouse mostra o nome; clicar abre o item. Ideia dos editores da comunidade (VAE, painel web).
/// </summary>
internal sealed class ShopPreview : Panel
{
    const int Cell = 42, Cols = 8, Rows = 4, Head = 28, Gap = 16, Pad = 12;
    static readonly Color Slot = Color.FromArgb(38, 44, 54), SlotLine = Color.FromArgb(70, 80, 96), Title = Color.FromArgb(255, 203, 74);

    readonly Func<string, Item?> _resolve;
    readonly Func<Item, Image?> _icon;
    readonly Action<Item> _go;
    readonly ToolTip _tip = new() { InitialDelay = 200 };
    readonly Dictionary<Item, Image?> _icons = new(ReferenceEqualityComparer.Instance);
    readonly List<(Rectangle R, Item It)> _hits = new();
    readonly List<(int No, string Title, Item?[] Goods)> _pages = new();
    readonly Font _headFont = new("Segoe UI Semibold", 10f);
    Item? _hover;

    public ShopPreview(Func<string, Item?> resolve, Func<Item, Image?> icon, Action<Item> go)
    {
        _resolve = resolve; _icon = icon; _go = go;
        DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(24, 28, 34); ForeColor = Color.Gainsboro;
    }

    public void ShowShop(Item shop)
    {
        _pages.Clear(); _icons.Clear(); _hover = null;
        for (int p = 1; p <= 8; p++)
        {
            var goods = new Item?[Cols * Rows];
            for (int g = 1; g <= goods.Length; g++)
                if (shop.Fields.TryGetValue($"pages_{p}_goods_{g}_id", out var f) && f.Value is not ("" or "0" or "-1"))
                    goods[g - 1] = _resolve(f.Value);
            var title = shop.Fields.TryGetValue($"pages_{p}_page_title", out var t) ? TasksFile.SplitColor(t.Value).Text.Trim() : "";
            if (title.Length > 0 || goods.Any(x => x is not null)) _pages.Add((p, title, goods));
        }
        AutoScrollPosition = Point.Empty;
        AutoScrollMinSize = new Size(Pad * 2 + Cols * Cell, Pad + _pages.Count * (Head + Rows * Cell + Gap));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hits.Clear();
        int x0 = Pad + AutoScrollPosition.X, y = Pad + AutoScrollPosition.Y;
        if (_pages.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, Loc.T("shop.empty", "(esta loja não tem itens)"), Font, new Point(Pad, Pad), Color.Gray);
            return;
        }
        using var slot = new SolidBrush(Slot);
        using var line = new Pen(SlotLine);
        foreach (var (no, title, goods) in _pages)
        {
            int filled = goods.Count(g => g is not null);
            TextRenderer.DrawText(e.Graphics, $"{Loc.T("shop.page", "Página")} {no}{(title.Length > 0 ? "  —  " + title : "")}   ({filled}/{goods.Length})",
                _headFont, new Point(x0, y + 4), Title);
            y += Head;
            for (int i = 0; i < goods.Length; i++)
            {
                var r = new Rectangle(x0 + i % Cols * Cell, y + i / Cols * Cell, Cell - 4, Cell - 4);
                e.Graphics.FillRectangle(slot, r);
                e.Graphics.DrawRectangle(line, r);
                if (goods[i] is not { } it) continue;
                if (!_icons.TryGetValue(it, out var img)) _icons[it] = img = _icon(it);
                if (img is not null) e.Graphics.DrawImage(img, Rectangle.Inflate(r, -3, -3));
                if (ReferenceEquals(it, _hover)) { using var hl = new Pen(Title, 2); e.Graphics.DrawRectangle(hl, r); }
                _hits.Add((r, it));
            }
            y += Rows * Cell + Gap;
        }
    }

    Item? HitTest(Point p) => _hits.FirstOrDefault(h => h.R.Contains(p)).It;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var it = HitTest(e.Location);
        if (ReferenceEquals(it, _hover)) return;
        _hover = it;
        _tip.SetToolTip(this, it is null ? "" : $"{TasksFile.SplitColor(it.Name).Text}  (ID {it.Id})");
        Cursor = it is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (HitTest(e.Location) is { } it) _go(it);
    }

    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tip.Dispose(); _headFont.Dispose(); }
        base.Dispose(disposing);
    }
}
