using System.Runtime.InteropServices;
using System.Text;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>
/// Música e som de fundo do Mundo 3D, como o jogo: a área do precinct.clt onde a câmera está escolhe as músicas (com o
/// intervalo de silêncio dela) e o som ambiente de dia ou de noite. Toca os .mp3 do cliente pelo MCI do Windows.
/// </summary>
internal sealed class WorldMusic : IDisposable
{
    const string Song = "pwsong", Ambient = "pwamb";

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder? answer, int size, IntPtr callback);

    readonly string _element;
    readonly PrecinctFile? _file;
    PrecinctFile.Area? _area;
    int _next;
    long _waitUntil;
    bool _songOpen, _muted;
    string _ambientNow = "";

    static string MuteFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyAceEditor", "world_mute.txt");

    public WorldMusic(string elementDir, string mapDir)
    {
        _element = elementDir;
        _file = PrecinctFile.Read(Path.Combine(mapDir, "precinct.clt"));
        _muted = File.Exists(MuteFile);
    }

    /// <summary>Diagnóstico: o Windows abre este mp3? Abre e fecha sem tocar.</summary>
    public static bool CanOpen(string file)
    {
        bool ok = mciSendString($"open \"{file}\" type mpegvideo alias pwtest", null, 0, IntPtr.Zero) == 0;
        mciSendString("close pwtest", null, 0, IntPtr.Zero);
        return ok;
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            try { if (value) File.WriteAllText(MuteFile, "1"); else File.Delete(MuteFile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Info("world_mute: " + ex.Message); }
            if (value) StopAll(); else { _area = null; _waitUntil = 0; }
        }
    }

    /// <summary>Chamado cerca de 1 vez por segundo com a posição da câmera (x e z do jogo).</summary>
    public void Update(float x, float z, bool night)
    {
        if (_muted || _file is null) return;
        var area = _file.At(x, z);
        if (!ReferenceEquals(area, _area))
        {
            bool sameList = area is not null && _area is not null && area.Music.SequenceEqual(_area.Music);
            _area = area;
            if (!sameList) { CloseSong(); _next = 0; _waitUntil = 0; }
        }
        PlayAmbient(area is null ? "" : night ? area.NightSound : area.Sound);
        if (area is null || area.Music.Count == 0) return;
        if (_songOpen)
        {
            var mode = new StringBuilder(32);
            mciSendString($"status {Song} mode", mode, mode.Capacity, IntPtr.Zero);
            if (mode.ToString() == "playing") return;
            CloseSong();
            _waitUntil = Environment.TickCount64 + Math.Max(0, area.Interval) * 1000L;   // silêncio entre as músicas da área
            if (area.Loop == 0 && _next >= area.Music.Count) return;
        }
        if (Environment.TickCount64 < _waitUntil) return;
        var file = Resolve(area.Music[_next % area.Music.Count]);
        _next++;
        if (file is null) return;
        if (mciSendString($"open \"{file}\" type mpegvideo alias {Song}", null, 0, IntPtr.Zero) != 0) return;
        _songOpen = true;
        mciSendString($"setaudio {Song} volume to 450", null, 0, IntPtr.Zero);
        mciSendString($"play {Song}", null, 0, IntPtr.Zero);
    }

    string? Resolve(string name)
    {
        if (name.Length == 0) return null;
        foreach (var f in new[] { Path.Combine(_element, name), Path.Combine(_element, "music", name) })
            if (File.Exists(f)) return f;
        return null;
    }

    void PlayAmbient(string sound)
    {
        if (sound == _ambientNow) return;
        mciSendString($"close {Ambient}", null, 0, IntPtr.Zero);
        _ambientNow = sound;
        if (Resolve(sound) is not { } file) return;
        if (mciSendString($"open \"{file}\" type mpegvideo alias {Ambient}", null, 0, IntPtr.Zero) != 0) return;
        mciSendString($"setaudio {Ambient} volume to 300", null, 0, IntPtr.Zero);
        mciSendString($"play {Ambient} repeat", null, 0, IntPtr.Zero);
    }

    void CloseSong() { if (_songOpen) mciSendString($"close {Song}", null, 0, IntPtr.Zero); _songOpen = false; }

    void StopAll() { CloseSong(); mciSendString($"close {Ambient}", null, 0, IntPtr.Zero); _ambientNow = ""; }

    public void Dispose() => StopAll();
}
