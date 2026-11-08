using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

internal sealed partial class WorldView
{
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
