using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

internal sealed partial class WorldView
{
    /// <summary>
    /// Para a captura ("dftlayout", rodar com MYACE_LANG=en/ru/zh): todo botão, rótulo e caixa de marcar do Mundo 3D
    /// (painéis escondidos inclusive) cujo texto não cabe: palavra maior que a largura (quebra no meio) ou texto mais alto
    /// que o controle. Usuário 09/11: "ver se algum campo/botão mais quebra durante as traduções".
    /// </summary>
    internal string LayoutCheck()
    {
        var bad = new List<string>(); int seen = 0;
        void Walk(Control c)
        {
            foreach (Control k in c.Controls) Walk(k);
            if (c is not (Button or Label or CheckBox or RadioButton) || string.IsNullOrWhiteSpace(c.Text) || c.Width <= 0) return;
            seen++;
            var pad = c is Button ? 10 : 2;
            int w = c.Width - pad - (c is CheckBox or RadioButton ? 18 : 0);
            int imgH = c is Button { Image: { } im, TextImageRelation: TextImageRelation.ImageAboveText } ? im.Height + 4 : 0;
            int imgW = c is Button { Image: { } im2, TextImageRelation: TextImageRelation.ImageBeforeText } ? im2.Width + 6 : 0;
            w -= imgW;
            string text = c.Text.Replace("&&", "&");
            string why = "";
            if (c.AutoSize)
            {
                // cresce sozinho: só não pode passar da borda do painel de largura fixa
                if (c.MaximumSize.Width is > 0 and <= 250) { }   // texto de ajuda: quebra na largura máxima
                else if (c.Parent is { AutoSize: false } p && p is not Form && c.Right > p.ClientSize.Width + 1 && p.Width > 0 && !(p is ScrollableControl { AutoScroll: true } && p.ClientSize.Width >= c.Width))
                    why = $"passa da borda ({c.Right} > {p.ClientSize.Width})";
            }
            else
            {
                var word = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(x => TextRenderer.MeasureText(x, c.Font).Width).FirstOrDefault() ?? "";
                int ww = TextRenderer.MeasureText(word, c.Font).Width;
                int th = TextRenderer.MeasureText(text, c.Font, new Size(Math.Max(1, w), 0), TextFormatFlags.WordBreak).Height;
                bool cjk = text.Any(ch => ch >= 0x2E80);   // chinês quebra em qualquer letra: só a altura conta
                if (ww > w && !cjk) why = $"palavra \"{word}\" {ww}px > {w}px";
                else if (th > c.Height - imgH - (c is Button ? 4 : 0)) why = $"altura {th}px > {c.Height - imgH}px";
            }
            if (why.Length > 0) bad.Add($"{c.GetType().Name} {c.Font.Size}pt \"{text.Replace("\r", "").Replace("\n", " / ")}\": {why}");
        }
        Walk(this);
        return $"dftlayout {AppLang.Current}: {seen} controles, {bad.Count} com texto cortado" + (bad.Count > 0 ? Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", bad.Distinct()) : "");
    }

    /// <summary>
    /// Toda pergunta/aviso do Mundo 3D passa por aqui (o nome esconde o System.Windows.Forms.MessageBox dentro da classe).
    /// Na captura de linha de comando (<see cref="Headless"/>) nada aparece na tela: a resposta é a que não grava nada
    /// (Sim em "abandonar?", Não em "salvar antes?") e o texto vai para o log. Antes as capturas ficavam presas na pergunta
    /// de abandonar a edição, aberta na tela do usuário (08/11).
    /// </summary>
    static class MessageBox
    {
        static DialogResult Auto(string text, MessageBoxButtons buttons)
        {
            AppLog.Info("captura: pergunta respondida sozinha: " + text.Replace(Environment.NewLine, " "));
            return buttons switch
            {
                MessageBoxButtons.YesNo => DialogResult.Yes,
                MessageBoxButtons.YesNoCancel => DialogResult.No,
                MessageBoxButtons.OKCancel => DialogResult.OK,
                MessageBoxButtons.RetryCancel => DialogResult.Cancel,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                _ => DialogResult.OK,
            };
        }

        public static DialogResult Show(IWin32Window? owner, string text, string caption = "", MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton def = MessageBoxDefaultButton.Button1)
            => Headless ? Auto(text, buttons) : System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon, def);

        public static DialogResult Show(string text, string caption = "", MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton def = MessageBoxDefaultButton.Button1)
            => Headless ? Auto(text, buttons) : System.Windows.Forms.MessageBox.Show(text, caption, buttons, icon, def);
    }
}
