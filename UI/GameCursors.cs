using System.Runtime.InteropServices;

namespace MyAceEditor.UI;

/// <summary>
/// Cursores do próprio jogo (element\cursors: normal.ani, talk.cur, attack.cur, pick.cur). O Windows lê .ani e .cur pelo
/// LoadCursorFromFile; o Cursor do WinForms sozinho não abre .ani. Sem o arquivo, fica o cursor do Windows.
/// </summary>
internal sealed class GameCursors : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursorFromFile(string file);
    [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);

    readonly List<IntPtr> _handles = [];
    public Cursor Normal { get; }
    public Cursor Talk { get; }
    public Cursor Attack { get; }
    public Cursor Gather { get; }

    public GameCursors(string? elementDir)
    {
        Cursor Load(string file, Cursor fallback)
        {
            if (elementDir is null) return fallback;
            var path = Path.Combine(elementDir, "cursors", file);
            if (!File.Exists(path)) return fallback;
            var h = LoadCursorFromFile(path);
            if (h == IntPtr.Zero) return fallback;
            _handles.Add(h);
            return new Cursor(h);
        }
        Normal = Load("normal.ani", Cursors.Default);
        Talk = Load("talk.cur", Normal);
        Attack = Load("attack.cur", Normal);
        Gather = Load("pick.cur", Normal);
    }

    public void Dispose() { foreach (var h in _handles) DestroyCursor(h); _handles.Clear(); }
}
