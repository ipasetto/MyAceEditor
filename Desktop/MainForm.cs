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
        // Inicializa, limpa o cache e navega com parâmetro anti-cache (evita UI antiga).
        web.CoreWebView2InitializationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess) return;
            try { await web.CoreWebView2.Profile.ClearBrowsingDataAsync(); } catch { }
            web.CoreWebView2.Navigate($"{url}/?v={DateTime.UtcNow.Ticks}");
        };
        _ = web.EnsureCoreWebView2Async();
    }
}
