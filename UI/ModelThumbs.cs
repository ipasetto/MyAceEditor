using System.Collections.Concurrent;

namespace MyAceEditor.UI;

/// <summary>
/// Miniaturas dos modelos 3D para as listas (monstros, NPCs e o que mais não tiver ícone no jogo): desenhadas em
/// segundo plano por um ModelPreview só deste thread; cada uma pronta volta para a tela por done(ecm, imagem).
/// Os pedidos mais recentes (linhas visíveis agora) são feitos primeiro.
/// </summary>
internal sealed class ModelThumbs : IDisposable
{
    readonly ConcurrentStack<string> _queue = new();
    readonly SemaphoreSlim _signal = new(0);
    readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);
    readonly ModelPreview _preview;
    readonly Control _ui;
    readonly Action<string, Image?> _done;
    readonly int _size;
    volatile bool _stop;

    public ModelThumbs(string elementDir, Control ui, int size, Action<string, Image?> done)
    {
        _preview = new ModelPreview(elementDir); _ui = ui; _size = size; _done = done;
        new Thread(Run) { IsBackground = true, Name = "Miniaturas", Priority = ThreadPriority.BelowNormal }.Start();
    }

    public void Request(string ecm)
    {
        lock (_asked) if (!_asked.Add(ecm)) return;
        _queue.Push(ecm); _signal.Release();
    }

    void Run()
    {
        while (!_stop)
        {
            _signal.Wait(500);
            while (!_stop && _queue.TryPop(out var ecm))
            {
                Image? img = null;
                try { if (_preview.Render(ecm, _size, 0, transparent: true) is { } src) img = new Bitmap(src); } catch { img = null; }
                try { if (!_ui.IsDisposed) _ui.BeginInvoke(() => _done(ecm, img)); } catch { }
            }
        }
    }

    public void Dispose() { _stop = true; _signal.Release(); _preview.Dispose(); }
}
