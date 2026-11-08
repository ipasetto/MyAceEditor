namespace MyAceEditor.UI;

/// <summary>
/// Barra de andamento de um salvar: fundo escuro, faixa azul enchendo e o texto com a porcentagem no meio. Value 0..100.
/// Usada embaixo do Mundo 3D em todo salvar (mesmo os rápidos aparecem por um instante, para o usuário ver que gravou).
/// </summary>
internal sealed class SaveProgressBar : Control
{
    int _value;
    string _label = "";
    public SaveProgressBar() { DoubleBuffered = true; BackColor = Color.FromArgb(24, 28, 34); ForeColor = Color.White; Font = new Font("Segoe UI Semibold", 8.5f); }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value { get => _value; set { int v = Math.Clamp(value, 0, 100); if (v != _value) { _value = v; Invalidate(); } } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Label { get => _label; set { _label = value ?? ""; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var r = new Rectangle(6, 4, Width - 12, Height - 8);
        using (var track = new SolidBrush(Color.FromArgb(48, 54, 64))) g.FillRectangle(track, r);
        int w = (int)(r.Width * _value / 100.0);
        if (w > 0) using (var fill = new SolidBrush(UiKit.Accent)) g.FillRectangle(fill, new Rectangle(r.X, r.Y, w, r.Height));
        TextRenderer.DrawText(g, $"{_label}  {_value}%", Font, r, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
