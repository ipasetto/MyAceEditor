using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyAceEditor.PerfectWorld;

/// <summary>
/// Posição do personagem lida da memória do elementclient aberto (só leitura), como o "Get Coordinates" do sNPCedit e o
/// ProjectAtlas (PW-KB §S22): cadeia de ponteiros por build do executável, depois 3 floats (x, altura, z). A cadeia muda a
/// cada build; tenta as conhecidas e só aceita um ponto dentro dos limites de um mapa do jogo.
/// </summary>
public static class GamePosition
{
    // offsets.txt (UTF-16, "Name=<build>"): base, elos..., pos X/Y/Z. Cliente 32 bits, endereço absoluto.
    static readonly (string Build, uint[] Chain, uint Pos)[] Known =
    {
        ("PWI 1.5.5 v155/v156", new uint[] { 0x00E444A4, 0x1C, 0x34 }, 0x3C),
        ("PWI 1.5.3 v145", new uint[] { 0x00DA433C, 0x1C, 0x28 }, 0x3C),
        ("PWI 1.5.1 v101 V2", new uint[] { 0x00C7D20C, 0x2C, 0x1420 }, 0x180),
        ("PWI 1.5.1 v101", new uint[] { 0x00C76DCC, 0x2C, 0x1420 }, 0x180),
        ("PWI 1.5.0 v88", new uint[] { 0x00C392CC, 0x34, 0x1340 }, 0x180),
        ("PWI 1.4.8 v85", new uint[] { 0x00C0CDEC, 0x34, 0x131C }, 0x180),
        ("PWI 1.4.6 v80", new uint[] { 0x00BBC9CC, 0x34, 0x12B4 }, 0x17C),
        ("PWI 1.4.6 v70", new uint[] { 0x00B9029C, 0x34, 0x11D8 }, 0x17C),
        ("PWI 1.4.5 v670", new uint[] { 0x00B4F5FC, 0x34, 0x1174 }, 0x17C),
        ("PWI 1.4.5 v650", new uint[] { 0x00B3B904, 0x34, 0x116C }, 0x17C),
        ("PWI 1.4.5 v615", new uint[] { 0x00A52AAC, 0x34, 0x1134 }, 0x17C),
        ("PWI 1.4.4 v606", new uint[] { 0x00B29184, 0x34, 0x10E8 }, 0x17C),
        ("CN 1.3.6 v101", new uint[] { 0x0092764C, 0x20, 0x0C30 }, 0x17C),
    };

    static int _hit = -1;   // a cadeia que funcionou da última vez vem primeiro

    /// <summary>(x, altura, z) do personagem no jogo aberto; null sem jogo, fora do mundo (tela de login) ou build desconhecido.</summary>
    public static (float X, float Y, float Z, string Build)? Read()
    {
        var proc = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameConsole.Exe)).FirstOrDefault();
        if (proc is null) return null;
        var h = OpenProcess(0x0010 /* PROCESS_VM_READ */, false, proc.Id);
        if (h == IntPtr.Zero) return null;
        try
        {
            var order = Enumerable.Range(0, Known.Length).OrderBy(i => i == _hit ? 0 : 1);
            foreach (var i in order)
            {
                var (build, chain, pos) = Known[i];
                uint p = 0; bool ok = true;
                foreach (var link in chain) if (ReadU32(h, p + link) is { } v && v != 0) p = v; else { ok = false; break; }
                if (!ok || ReadF32(h, p + pos) is not { } x || ReadF32(h, p + pos + 4) is not { } y || ReadF32(h, p + pos + 8) is not { } z) continue;
                // mapa do jogo: |x| ≤ ~4.6 km, |z| ≤ ~5.7 km (mundo 8192 × 11264), altura 0..1000; zero exato = ponteiro errado
                if (!float.IsFinite(x) || !float.IsFinite(z) || Math.Abs(x) > 6000 || Math.Abs(z) > 7000 || y < -100 || y > 2000 || x == 0 && z == 0) continue;
                _hit = i;
                return (x, y, z, build);
            }
            return null;
        }
        finally { CloseHandle(h); }
    }

    static uint? ReadU32(IntPtr h, uint addr) { var b = new byte[4]; return ReadProcessMemory(h, (IntPtr)addr, b, 4, out var n) && n == 4 ? BitConverter.ToUInt32(b) : null; }
    static float? ReadF32(IntPtr h, uint addr) { var b = new byte[4]; return ReadProcessMemory(h, (IntPtr)addr, b, 4, out var n) && n == 4 ? BitConverter.ToSingle(b) : null; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
