using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Console do jogo aberto (elementclient.exe), para o Painel do GM (docs/GM-PAINEL.md). O console só existe com
/// "console:1" na linha de comando (glb_IsConsoleEnable), abre e fecha com Shift + VK_OEM_3 (LKEY_UI_SHOW_CONSOLE,
/// EC_HostInputFilter.cpp), executa a linha com Enter (EC_DlgCmdConsole.cpp) e fecha com Esc. O texto é digitado como
/// entrada real (SendInput): o jogo vem para a frente e fica com o foco. Nada é gravado nos arquivos do jogo.
/// </summary>
public static class GameConsole
{
    public const string Exe = "elementclient.exe";

    /// <summary>Abre o jogo com o console ligado (mesmos argumentos do eSTART_GEN2.bat dos clientes de teste).</summary>
    public static Process? Launch(string elementDir) =>
        Process.Start(new ProcessStartInfo(Path.Combine(elementDir, Exe), "startbypatcher nocheck console:1") { WorkingDirectory = elementDir, UseShellExecute = false });

    /// <summary>Janela do jogo aberto (a primeira que tiver janela).</summary>
    public static IntPtr Window() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)).Select(p => { try { return p.MainWindowHandle; } catch { return IntPtr.Zero; } })
            .FirstOrDefault(h => h != IntPtr.Zero);

    public static bool Running => Window() != IntPtr.Zero;

    /// <summary>
    /// Abre o console, digita cada linha com Enter e fecha com Esc; o foco fica no jogo. false = jogo não aberto ou não
    /// deu para trazê-lo à frente. O console abre no "apertar" da tecla e o caractere dela (a aspa no teclado ABNT2)
    /// chega depois, já dentro do console: um Backspace o apaga antes da primeira linha.
    /// </summary>
    public static bool Send(IReadOnlyList<string> lines)
    {
        var w = Window();
        if (w == IntPtr.Zero || !Front(w)) return false;
        Thread.Sleep(150);
        Push(new() { Key(VK_SHIFT, false), Key(VK_OEM_3, false), Key(VK_OEM_3, true), Key(VK_SHIFT, true) });
        Thread.Sleep(250);
        if (GetForegroundWindow() != w) return false;
        Push(new() { Key(VK_BACK, false), Key(VK_BACK, true) });
        Thread.Sleep(40);
        foreach (var line in lines)
        {
            var inputs = new List<INPUT>();
            foreach (var ch in line) { inputs.Add(Unicode(ch, false)); inputs.Add(Unicode(ch, true)); }
            Push(inputs);
            Thread.Sleep(60);
            Push(new() { Key(VK_RETURN, false), Key(VK_RETURN, true) });
            Thread.Sleep(120);
        }
        Push(new() { Key(VK_ESCAPE, false), Key(VK_ESCAPE, true) });
        return true;
    }

    // a janela de outro programa só vem à frente se esta thread "participar" da entrada da que está na frente
    static bool Front(IntPtr w)
    {
        if (IsIconic(w)) ShowWindow(w, SW_RESTORE);
        var fg = GetForegroundWindow();
        uint me = GetCurrentThreadId(), other = GetWindowThreadProcessId(fg, out _);
        bool attached = other != me && AttachThreadInput(me, other, true);
        try { BringWindowToTop(w); SetForegroundWindow(w); }
        finally { if (attached) AttachThreadInput(me, other, false); }
        for (int i = 0; i < 10 && GetForegroundWindow() != w; i++) Thread.Sleep(30);
        return GetForegroundWindow() == w;
    }

    static void Push(List<INPUT> l) => SendInput((uint)l.Count, l.ToArray(), Marshal.SizeOf<INPUT>());
    static INPUT Key(ushort vk, bool up) => new() { type = 1, u = new() { ki = new() { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };
    static INPUT Unicode(char c, bool up) => new() { type = 1, u = new() { ki = new() { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } } };

    const ushort VK_SHIFT = 0x10, VK_RETURN = 0x0D, VK_OEM_3 = 0xC0, VK_BACK = 0x08, VK_ESCAPE = 0x1B;
    const uint KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr w);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr w);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr w, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr w);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
