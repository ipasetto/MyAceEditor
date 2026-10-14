using Microsoft.Web.WebView2.WinForms;

namespace MyAceEditor.Desktop;

/// <summary>Janela nativa que embute a UI web (servida localmente) via WebView2.</summary>
public sealed class MainForm : Form
{
    public MainForm(string url)
    {
        Text = "MyAceEditor";
        Width = 1200;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);

        var web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(web);
        web.Source = new Uri(url);   // inicializa o CoreWebView2 e navega
    }
}
