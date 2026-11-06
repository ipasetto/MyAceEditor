using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela de quests do jogo (Win_Quest, interfaces\version01\quest.xml + texturas do surfaces.pck) só com a quest em edição,
/// para ver no Resumo como ela aparece para o jogador.
/// </summary>
internal sealed class QuestPreview : Panel
{
    public sealed record Step(string Name, string Description, string Goal);
    public sealed record Data(string Name, string Description, string Goal, IReadOnlyList<Step> Steps, long Gold, long Exp, long Spirit, IReadOnlyList<Image?> Items);

    readonly PckArchive _interfaces, _surfaces;
    readonly GameTextures _tex;
    readonly GameDialog _dialog;
    Data? _data;

    QuestPreview(PckArchive interfaces, PckArchive surfaces, GameDialog dialog)
    {
        _interfaces = interfaces; _surfaces = surfaces; _dialog = dialog; _tex = new GameTextures(surfaces);
        DoubleBuffered = true; Size = dialog.Size; BackColor = Color.FromArgb(24, 27, 33);
    }

    /// <summary>Null se o cliente não tem a janela (pacotes faltando ou outro formato).</summary>
    public static QuestPreview? Create(string element)
    {
        PckArchive? ui = null, sf = null;
        try
        {
            var uiPath = Path.Combine(element, "interfaces.pck"); var sfPath = Path.Combine(element, "surfaces.pck");
            if (!File.Exists(uiPath) || !File.Exists(sfPath)) return null;
            ui = new PckArchive(uiPath); sf = new PckArchive(sfPath);
            GameFonts.Load(element);
            var dialog = GameSkin.Load(ui, element, "quest.xml");
            if (dialog is not null) return new QuestPreview(ui, sf, dialog);
        }
        catch (Exception ex) { AppLog.Error("janela de quest do jogo", ex); }
        ui?.Dispose(); sf?.Dispose();
        return null;
    }

    public void Set(Data data) { _data = data; _row = Math.Min(_row, data.Steps.Count); Invalidate(); }

    const int RowHeight = 22;
    static readonly Color Ink = Color.FromArgb(58, 36, 18);   // texto sobre o pergaminho, como no jogo
    int _row;   // 0 = a quest; 1.. = etapas

    Rectangle Area(string name, Rectangle fallback) => _dialog[name]?.Rect ?? fallback;
    Rectangle Tree => Area("Tv_Quest", new Rectangle(13, 64, 184, 306));

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var tree = Tree;
        if (_data is null || !tree.Contains(e.Location)) return;
        int row = (e.Y - tree.Y - 4) / RowHeight;
        if (row >= 0 && row <= _data.Steps.Count) { _row = row; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool reward = _data is { } r && (r.Gold > 0 || r.Exp > 0 || r.Spirit > 0 || r.Items.Count > 0);
        // sem prêmio o jogo não mostra o bloco de recompensa
        // só o que a quest mostra: rodapé e rótulos de exemplo da janela ficam de fora, senão um texto cai em cima do outro (teste 11)
        var icons = Area("Img_Item0", new Rectangle(213, 328, 32, 32));
        bool Noise(GameDialog.Control c) => c.Name is "Img_New" or "Btn_GotoNPC" or "Btn_FinishTask" or "Btn_TreasureMap"
            || c.Rect.Y >= icons.Bottom && c.Rect.Height < 60
            || c.Tag == "LABEL" && c.Rect.Y + c.Rect.Height > icons.Y
            || !reward && (c.Name.StartsWith("Img_Item") || c.Name == "Txt_ItemAward" || c.Name == "Txt_BaseAward" || c.Tag == "LABEL" && c.Rect.Y > 280 && c.Rect.Y < 320);
        GameDraw.Dialog(g, _dialog, _tex, c => Noise(c) ? new Rectangle(-5000, 0, 0, 0) : GameDraw.Box(c));
        if (_data is not { } d) return;
        using var font = new Font("Segoe UI", 9f);
        void Write(string text, Rectangle box, Color c) => TextRenderer.DrawText(g, text, font, box, c, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var tree = Tree;
        void Row(int index, int indent, string text, Color c)
        {
            var box = new Rectangle(tree.X + 4 + indent, tree.Y + 4 + index * RowHeight, tree.Width - 8 - indent, RowHeight);
            if (index == _row) using (var sel = new SolidBrush(Color.FromArgb(0, 84, 166))) g.FillRectangle(sel, box);
            TextRenderer.DrawText(g, text, font, box, index == _row ? Color.White : c, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        Row(0, 0, "⊟ " + (d.Name.Length > 0 ? d.Name : Loc.T("qp.noName", "(quest sem nome)")), Color.FromArgb(255, 200, 90));
        for (int i = 0; i < d.Steps.Count; i++) Row(i + 1, 20, "▪ " + d.Steps[i].Name, Color.FromArgb(120, 220, 230));

        var step = _row > 0 && _row <= d.Steps.Count ? d.Steps[_row - 1] : null;
        var text = step is { Description.Length: > 0 } ? step.Description : d.Description;
        Write(text, Rectangle.Inflate(Area("Txt_Content", new Rectangle(211, 46, 325, 151)), -2, -2), Ink);
        Write(step?.Goal ?? d.Goal, Rectangle.Inflate(Area("Txt_QuestItem", new Rectangle(211, 205, 325, 81)), -2, -2), Ink);
        if (!reward) return;
        var parts = new List<string>();
        if (d.Gold > 0) parts.Add(string.Format(Loc.T("qp.gold", "Ouro {0:N0}"), d.Gold));
        if (d.Exp > 0) parts.Add(string.Format(Loc.T("qp.exp", "EXP {0:N0}"), d.Exp));
        if (d.Spirit > 0) parts.Add(string.Format(Loc.T("qp.spirit", "Espírito {0:N0}"), d.Spirit));
        Write(string.Join("   ", parts), Area("Txt_BaseAward", new Rectangle(211, 309, 325, 15)), Color.White);
        for (int i = 0; i < d.Items.Count && i < 8; i++)
            if (d.Items[i] is { } icon) g.DrawImage(icon, Area("Img_Item" + i, new Rectangle(213 + i * 40, 328, 32, 32)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tex.Dispose(); _interfaces.Dispose(); _surfaces.Dispose(); }
        base.Dispose(disposing);
    }
}
