using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Janela de quests do jogo (Win_Quest, interfaces\version01\quest.xml + texturas do surfaces.pck) só com a quest em edição,
/// para ver no Resumo como ela aparece para o jogador.
/// </summary>
internal sealed class QuestPreview : Panel
{
    public sealed record Data(string Name, string Description, IReadOnlyList<string> Steps, long Gold, long Exp, long Spirit, IReadOnlyList<Image?> Items);

    readonly PckArchive _interfaces, _surfaces;
    readonly GameTextures _tex;
    readonly GameDialog _dialog;
    Data? _data;

    QuestPreview(PckArchive interfaces, PckArchive surfaces, GameDialog dialog)
    {
        _interfaces = interfaces; _surfaces = surfaces; _dialog = dialog; _tex = new GameTextures(surfaces);
        DoubleBuffered = true; Size = dialog.Size; BackColor = Color.Black;
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
            var dialog = GameDialog.Load(ui, @"interfaces\version01\quest.xml") ?? GameDialog.Load(ui, @"interfaces\quest.xml");
            if (dialog is not null) return new QuestPreview(ui, sf, dialog);
        }
        catch (Exception ex) { AppLog.Error("janela de quest do jogo", ex); }
        ui?.Dispose(); sf?.Dispose();
        return null;
    }

    public void Set(Data data) { _data = data; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        GameDraw.Dialog(g, _dialog, _tex, GameDraw.Box);
        if (_data is not { } d) return;
        using var font = new Font("Segoe UI", 9f);
        Rectangle Area(string name, Rectangle fallback) => _dialog[name]?.Rect ?? fallback;
        void Write(string text, Rectangle r, Color c) => TextRenderer.DrawText(g, text, font, r, c, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var list = Area("Tv_Quest", new Rectangle(13, 64, 184, 306));
        Write(d.Name.Length > 0 ? d.Name : Loc.T("qp.noName", "(quest sem nome)"), new Rectangle(list.X + 4, list.Y + 4, list.Width - 8, 34), Color.FromArgb(255, 220, 90));
        Write(d.Description, Rectangle.Inflate(Area("Txt_Content", new Rectangle(211, 46, 325, 151)), -2, -2), Color.White);
        Write(string.Join("\n", d.Steps), Rectangle.Inflate(Area("Txt_QuestItem", new Rectangle(211, 205, 325, 81)), -2, -2), Color.FromArgb(120, 230, 150));
        Write(string.Format(Loc.T("qp.award", "Ouro {0:N0}   EXP {1:N0}   Espírito {2:N0}"), d.Gold, d.Exp, d.Spirit), Area("Txt_BaseAward", new Rectangle(211, 309, 325, 15)), Color.White);
        for (int i = 0; i < d.Items.Count && i < 4; i++)
            if (d.Items[i] is { } icon) g.DrawImage(icon, Area("Img_Item" + i, new Rectangle(213 + i * 40, 328, 32, 32)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tex.Dispose(); _interfaces.Dispose(); _surfaces.Dispose(); }
        base.Dispose(disposing);
    }
}
