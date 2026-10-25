using System.Collections.Concurrent;
using System.Diagnostics;

namespace MyAceEditor.UI;

/// <summary>
/// Carga do Mundo 3D em segundo plano: um thread lê arquivos, descomprime e decodifica (alturas, máscaras, texturas,
/// modelos, construções); o resultado volta para o thread da tela, que só envia à placa de vídeo — com limite de tempo
/// por quadro, para andar e teleportar sem travar. Pedidos "urgentes" (perto da câmera) passam na frente.
/// Os .pck não aceitam leituras simultâneas: quem lê de um pck no thread da tela usa lock(pck).
/// </summary>
internal sealed class WorldLoader : IDisposable
{
    readonly ConcurrentQueue<Action> _urgent = new(), _normal = new();
    readonly ConcurrentQueue<Action> _done = new();
    readonly SemaphoreSlim _signal = new(0);
    readonly Thread _thread;
    volatile bool _stop;
    int _pending;

    public WorldLoader()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Mundo 3D: carga", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    void Run()
    {
        while (!_stop)
        {
            _signal.Wait(200);
            while (!_stop && (_urgent.TryDequeue(out var job) || _normal.TryDequeue(out job)))
            {
                try { job(); } catch { /* um arquivo ruim não derruba a carga */ }
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    /// <summary>Faz work em segundo plano e then (com o resultado) no thread da tela, em Pump.</summary>
    public void Work<T>(Func<T> work, Action<T> then, bool urgent = false)
    {
        Interlocked.Increment(ref _pending);
        (urgent ? _urgent : _normal).Enqueue(() => { var r = work(); _done.Enqueue(() => then(r)); });
        _signal.Release();
    }

    /// <summary>Aplica resultados prontos por até ms milissegundos; true se ainda há trabalho em andamento.</summary>
    public bool Pump(double ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ms && _done.TryDequeue(out var a))
            try { a(); } catch { }
        return !_done.IsEmpty || Volatile.Read(ref _pending) > 0;
    }

    public void Dispose() { _stop = true; _signal.Release(); }
}
