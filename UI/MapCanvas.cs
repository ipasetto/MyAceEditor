using System.Drawing.Drawing2D;

namespace MyAceEditor.UI;

/// <summary>
/// Mapa com zoom, usado pelas Zonas e pelos Territórios: roda aproxima no ponto do cursor, botões +, − e Tudo, arrastar lugar vazio
/// move, clique sem arrastar seleciona. Não conhece zonas nem territórios: quem usa desenha em PaintOver e converte com ToScreen e ToMap.
/// Coordenadas do mapa são as de WorldSize; linhas e alvos de clique ficam em pixels da tela.
/// </summary>
internal sealed class MapCanvas : Control
{
    public const float MaxZoom = 16;
    const int Button = 40, Margin8 = 14, DragStart = 4;

    Image? _map;
    SizeF _world = new(1024, 768);
    float _zoom = 1;
    PointF _center = new(512, 384);
    bool _pressed, _panning, _clickOnly;
    Point _down;
    PointF _grab;
    int _hot = -1;
    readonly ToolTip _tip = new();

    public MapCanvas()
    {
        DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.FromArgb(24, 27, 33);
        SetStyle(ControlStyles.Selectable, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? Map { get => _map; set { _map = value; Invalidate(); } }
    /// <summary>Tamanho do mapa nas coordenadas de quem usa.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public SizeF WorldSize { get => _world; set { _world = value; FitAll(); } }
    /// <summary>Metros por unidade do mapa, para a escala. 0 esconde a escala.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float MetersPerPixel { get; set; }
    /// <summary>O mapa vem de cabeça para baixo em relação às coordenadas (z do jogo cresce para o norte).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? HitSomething { get; set; }
    public float Zoom => _zoom;

    /// <summary>O que cada cor quer dizer: fica no canto de baixo, à esquerda.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(Color Color, string Text)> Legend { get; set; } = [];
    /// <summary>Espaço embaixo ocupado por uma barra de quem usa.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BottomSpace { get; set; }

    /// <summary>Desenha por cima do mapa, em pixels da tela.</summary>
    public event Action<Graphics>? PaintOver;
    public event Action<MouseEventArgs, PointF>? MapDown, MapMove, MapUp, MapDoubleClick;

    float Fit => Math.Max(1e-3f, Math.Min((Width - 20f) / Math.Max(1, _world.Width), (Height - 20f) / Math.Max(1, _world.Height)));
    new float Scale => Fit * _zoom;

    public PointF ToScreen(PointF p) => new(Width / 2f + (p.X - _center.X) * Scale, Height / 2f + (p.Y - _center.Y) * Scale);
    public PointF ToMap(Point p) => new(_center.X + (p.X - Width / 2f) / Scale, _center.Y + (p.Y - Height / 2f) / Scale);

    public void FitAll() { _zoom = 1; _center = new(_world.Width / 2, _world.Height / 2); Invalidate(); }

    /// <summary>Enquadra este retângulo do mapa, com folga, até o limite do zoom.</summary>
    public void Frame(RectangleF r)
    {
        float want = Math.Min((Width - 20f) / Math.Max(1, r.Width * 1.3f), (Height - 20f) / Math.Max(1, r.Height * 1.3f)) / Fit;
        _zoom = Math.Clamp(want, 1, MaxZoom);
        _center = new(r.X + r.Width / 2, r.Y + r.Height / 2);
        Keep();
        Invalidate();
    }

    /// <summary>Aproxima ou afasta mantendo parado o ponto do mapa que está em "at".</summary>
    public void ZoomAt(Point at, float factor)
    {
        var fixedPoint = ToMap(at);
        _zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
        _center = new(fixedPoint.X - (at.X - Width / 2f) / Scale, fixedPoint.Y - (at.Y - Height / 2f) / Scale);
        Keep();
        Invalidate();
    }

    // o centro não sai do mapa: ele nunca some da tela
    void Keep() => _center = new(Math.Clamp(_center.X, 0, _world.Width), Math.Clamp(_center.Y, 0, _world.Height));

    Rectangle ButtonBox(int i) => new(Margin8, Margin8 + i * Button, Button + 4, Button);
    int ButtonAt(Point p) { for (int i = 0; i < 3; i++) if (ButtonBox(i).Contains(p)) return i; return -1; }

    void Press(int button)
    {
        var middle = new Point(Width / 2, Height / 2);
        if (button == 0) ZoomAt(middle, 1.6f);
        else if (button == 1) ZoomAt(middle, 1 / 1.6f);
        else FitAll();
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ZoomAt(e.Location, e.Delta > 0 ? 1.25f : 0.8f); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && ButtonAt(e.Location) is >= 0 and var b) { Press(b); return; }
        _down = e.Location;
        if (e.Button == MouseButtons.Left && HitSomething?.Invoke(e.Location) != true)
        {
            _pressed = _clickOnly = true; _grab = ToMap(e.Location);   // só vira clique se soltar sem arrastar
            return;
        }
        MapDown?.Invoke(e, ToMap(e.Location));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = ButtonAt(e.Location);
        if (hot != _hot) { _hot = hot; Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        if (_pressed && !_panning && (Math.Abs(e.X - _down.X) > DragStart || Math.Abs(e.Y - _down.Y) > DragStart)) { _panning = true; _clickOnly = false; }
        if (_panning)
        {
            _center = new(_grab.X - (e.X - Width / 2f) / Scale, _grab.Y - (e.Y - Height / 2f) / Scale);
            Keep();
            Invalidate();
            return;
        }
        MapMove?.Invoke(e, ToMap(e.Location));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool click = _clickOnly;
        _pressed = _panning = _clickOnly = false;
        if (click) MapDown?.Invoke(e, ToMap(e.Location));
        MapUp?.Invoke(e, ToMap(e.Location));
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (ButtonAt(e.Location) < 0) MapDoubleClick?.Invoke(e, ToMap(e.Location));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (_map is not null)
        {
            var a = ToScreen(PointF.Empty); var b = ToScreen(new PointF(_world.Width, _world.Height));
            g.InterpolationMode = _zoom > 4 ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_map, a.X, a.Y, b.X - a.X, b.Y - a.Y);
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintOver?.Invoke(g);
        PaintButtons(g);
        PaintScale(g);
        PaintLegend(g);
    }

    void PaintLegend(Graphics g)
    {
        if (Legend.Count == 0) return;
        using var font = new Font("Segoe UI", 8.5f);
        const int Row = 20, Square = 12;
        int width = Legend.Max(l => TextRenderer.MeasureText(l.Text, font).Width) + Square + 22;
        var box = new Rectangle(Margin8, Height - BottomSpace - Margin8 - Legend.Count * Row - 8, width, Legend.Count * Row + 8);
        using var back = new SolidBrush(Color.FromArgb(225, 32, 36, 44)); using var line = new Pen(Color.FromArgb(70, 80, 96));
        g.FillRectangle(back, box); g.DrawRectangle(line, box);
        for (int i = 0; i < Legend.Count; i++)
        {
            int y = box.Y + 4 + i * Row;
            using var fill = new SolidBrush(Legend[i].Color);
            g.FillRectangle(fill, box.X + 8, y + 4, Square, Square);
            g.DrawRectangle(Pens.White, box.X + 8, y + 4, Square, Square);
            TextRenderer.DrawText(g, Legend[i].Text, font, new Point(box.X + Square + 14, y + 2), Color.White);
        }
    }

    void PaintButtons(Graphics g)
    {
        string[] text = ["+", "−", Loc.T("map.all", "Tudo")];
        using var back = new SolidBrush(Color.FromArgb(230, 32, 36, 44)); using var hot = new SolidBrush(Color.FromArgb(240, 50, 62, 80)); using var line = new Pen(Color.FromArgb(70, 80, 96));
        using var big = new Font("Segoe UI", 15f); using var small = new Font("Segoe UI", 8.5f);
        for (int i = 0; i < 3; i++)
        {
            var r = ButtonBox(i);
            g.FillRectangle(i == _hot ? hot : back, r); g.DrawRectangle(line, r);
            TextRenderer.DrawText(g, text[i], i == 2 ? small : big, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    void PaintScale(Graphics g)
    {
        if (MetersPerPixel <= 0) return;
        float metersPerScreen = MetersPerPixel / Scale;
        float want = 110 * metersPerScreen;
        float step = 1;
        foreach (var s in new[] { 1f, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 }) if (s <= want) step = s;
        float w = step / metersPerScreen;
        float x = Width - w - 20, y = Height - 22;
        using var pen = new Pen(Color.White, 2);
        g.DrawLine(pen, x, y, x + w, y); g.DrawLine(pen, x, y - 5, x, y + 5); g.DrawLine(pen, x + w, y - 5, x + w, y + 5);
        using var font = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(g, string.Format(Loc.T("map.meters", "{0} m"), step), font, new Rectangle((int)x, (int)y - 22, (int)w, 18), Color.White, TextFormatFlags.HorizontalCenter);
    }

    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
}
