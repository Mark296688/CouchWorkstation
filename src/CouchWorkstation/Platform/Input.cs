using System.Runtime.InteropServices;

namespace CouchWorkstation.Platform;

/// <summary>Synthetic mouse/keyboard input via SendInput.</summary>
public static class Input
{
    /// <summary>Moves the cursor to an absolute position in physical virtual-desktop pixels.</summary>
    public static void MoveTo(int x, int y)
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi.dx = (int)Math.Round((x - left) * 65535.0 / Math.Max(1, width - 1));
        input.U.mi.dy = (int)Math.Round((y - top) * 65535.0 / Math.Max(1, height - 1));
        input.U.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        Send(input);
    }

    public static void LeftButton(bool down)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi.dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
        Send(input);
    }

    public readonly record struct Key(ushort Vk, ushort Scan, bool Extended);

    /// <summary>Presses (or releases, in reverse order) a key combination in one atomic SendInput call.</summary>
    public static void Keys(IReadOnlyList<Key> keys, bool down)
    {
        var inputs = new INPUT[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            var k = down ? keys[i] : keys[keys.Count - 1 - i];
            inputs[i].type = INPUT_KEYBOARD;
            inputs[i].U.ki.wVk = k.Vk;
            inputs[i].U.ki.wScan = k.Scan;
            inputs[i].U.ki.dwFlags = (down ? 0u : KEYEVENTF_KEYUP) | (k.Extended ? KEYEVENTF_EXTENDEDKEY : 0u);
        }
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Log.Warn($"SendInput (keys) was blocked, error {Marshal.GetLastWin32Error()}");
    }

    public static ushort ScanCode(ushort vk) => (ushort)MapVirtualKey(vk, 0);

    public static bool IsKeyDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void Send(INPUT input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
            Log.Debug($"SendInput (mouse) was blocked, error {Marshal.GetLastWin32Error()} (elevated window in foreground?)");
    }

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}
