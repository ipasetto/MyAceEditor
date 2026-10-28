using System.Drawing.Drawing2D;

namespace MyAceEditor.UI;

/// <summary>
/// Bandeiras dos idiomas do programa, desenhadas no código (o Windows não mostra emoji de bandeira).
/// Quadradas e com o desenho centralizado, para o menu não esticar ao escalar a imagem.
/// </summary>
public static class FlagIcons
{
    const int S = 32, W = 30, H = 20;   // imagem 32x32; bandeira 3:2 no meio
    static readonly Dictionary<string, Bitmap> _cache = new();

    public static Image? Get(string code)
    {
        if (_cache.TryGetValue(code, out var bmp)) return bmp;
        Action<Graphics>? draw = code switch { "pt" => Brazil, "en" => Uk, "zh" => China, "ru" => Russia, _ => null };
        if (draw is null) return null;
        bmp = new Bitmap(S, S);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform((S - W) / 2f, (S - H) / 2f);
            g.SetClip(new Rectangle(0, 0, W, H));
            draw(g);
            g.ResetClip();
            using var border = new Pen(Color.FromArgb(90, 0, 0, 0));
            g.DrawRectangle(border, 0, 0, W - 1, H - 1);
        }
        return _cache[code] = bmp;
    }

    static void Brazil(Graphics g)
    {
        g.Clear(Color.FromArgb(0, 156, 59));
        using var y = new SolidBrush(Color.FromArgb(255, 223, 0));
        g.FillPolygon(y, new PointF[] { new(W / 2f, 2), new(W - 2, H / 2f), new(W / 2f, H - 2), new(2, H / 2f) });
        using var b = new SolidBrush(Color.FromArgb(0, 39, 118));
        g.FillEllipse(b, W / 2f - 5.5f, H / 2f - 5.5f, 11, 11);
        using var band = new Pen(Color.White, 1.4f);
        g.DrawArc(band, W / 2f - 9, H / 2f - 3, 18, 14, 215, 110);
    }

    static void Uk(Graphics g)
    {
        g.Clear(Color.FromArgb(1, 33, 105));
        var red = Color.FromArgb(200, 16, 46);
        using (var white = new Pen(Color.White, 5)) { g.DrawLine(white, 0, 0, W, H); g.DrawLine(white, W, 0, 0, H); }
        using (var r = new Pen(red, 1.8f)) { g.DrawLine(r, 0, 0, W, H); g.DrawLine(r, W, 0, 0, H); }
        g.FillRectangle(Brushes.White, W / 2f - 3.5f, 0, 7, H);
        g.FillRectangle(Brushes.White, 0, H / 2f - 3.5f, W, 7);
        using var rb = new SolidBrush(red);
        g.FillRectangle(rb, W / 2f - 2, 0, 4, H);
        g.FillRectangle(rb, 0, H / 2f - 2, W, 4);
    }

    static void China(Graphics g)
    {
        g.Clear(Color.FromArgb(222, 41, 16));
        using var y = new SolidBrush(Color.FromArgb(255, 222, 0));
        Star(g, y, 5.5f, 5.5f, 4f, -90);
        Star(g, y, 11, 2.2f, 1.3f, -60);
        Star(g, y, 13, 4.6f, 1.3f, -80);
        Star(g, y, 13, 7.6f, 1.3f, -100);
        Star(g, y, 11, 9.8f, 1.3f, -120);
    }

    static void Russia(Graphics g)
    {
        g.Clear(Color.White);
        using var b = new SolidBrush(Color.FromArgb(0, 57, 166));
        using var r = new SolidBrush(Color.FromArgb(213, 43, 30));
        g.FillRectangle(b, 0, H / 3f, W, H / 3f);
        g.FillRectangle(r, 0, 2 * H / 3f, W, H / 3f + 1);
    }

    /// <summary>Estrela de 5 pontas com a ponta virada para <paramref name="angle"/> (graus).</summary>
    static void Star(Graphics g, Brush br, float cx, float cy, float r, float angle)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = (angle + i * 36) * Math.PI / 180;
            float rr = i % 2 == 0 ? r : r * 0.4f;
            pts[i] = new PointF(cx + (float)(Math.Cos(a) * rr), cy + (float)(Math.Sin(a) * rr));
        }
        g.FillPolygon(br, pts);
    }
}
