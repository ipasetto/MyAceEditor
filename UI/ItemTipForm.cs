namespace MyAceEditor.UI;

/// <summary>
/// Dica flutuante que segue o mouse: mostra a imagem pronta da dica do editor de elements (EditorForm.TipImage), para a
/// janela de itens, a loja e a fornalha ficarem iguais à lista do editor. Não rouba o foco da janela de baixo.
/// </summary>
internal sealed class ItemTipForm : Form
{
    readonly Bitmap _image;

    public ItemTipForm(Bitmap image)
    {
        _image = image;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(16, 17, 20); ClientSize = image.Size; DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    public void Follow()
    {
        var at = Cursor.Position; var area = Screen.FromPoint(at).WorkingArea;
        Location = new Point(Math.Min(at.X + 18, area.Right - Width - 4), Math.Max(area.Top, Math.Min(at.Y + 12, area.Bottom - Height - 4)));
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.DrawImageUnscaled(_image, 0, 0);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Liga a dica a um controle: abre, acompanha e fecha ao sair. Quem usa só diz qual ID está embaixo do mouse.</summary>
internal sealed class ItemTipHost(Control owner, Func<int, Bitmap?> image) : IDisposable
{
    ItemTipForm? _form;
    int _id;

    public void Hover(int id)
    {
        if (id == _id) { _form?.Follow(); return; }
        Hide();
        _id = id;
        if (id <= 0 || image(id) is not { } bmp) return;
        _form = new ItemTipForm(bmp);
        _form.Follow();
        _form.Show(owner.FindForm());
    }

    public void Hide() { _form?.Close(); _form?.Dispose(); _form = null; _id = 0; }

    public void Dispose() => Hide();
}
