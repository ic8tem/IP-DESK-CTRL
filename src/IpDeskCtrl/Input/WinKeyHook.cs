using System;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Input
{
    /// <summary>
    /// Windows, visore: il tasto Windows e le combinazioni con esso (Win+R, Win+E, Win+D…) li intercetta il sistema
    /// prima che arrivino alla finestra, e aprirebbero il menu Start o «Esegui» di questo PC. Con un gancio di
    /// tastiera a basso livello, mentre la finestra del PC remoto è in primo piano e ha il fuoco, questi tasti
    /// vengono tolti a Windows e mandati al PC remoto. Tutti gli altri tasti seguono la strada normale.
    /// </summary>
    public static class WinKeyHook
    {
        private delegate IntPtr Proc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Proc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

        private static IntPtr _hook;
        private static readonly Proc _proc = Callback;     // il delegato deve restare vivo finché il gancio c'è
        private static IntPtr _hwnd;
        private static Action<int, bool>? _send;
        private static bool _winDown;

        /// <summary>Attiva il gancio per la finestra hwnd: send(evdev, premuto) manda il tasto al PC remoto.</summary>
        public static void Start(IntPtr hwnd, Action<int, bool> send)
        {
            if (!OperatingSystem.IsWindows()) return;
            _hwnd = hwnd; _send = send;
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(null), 0);
        }

        public static void Stop()
        {
            if (!OperatingSystem.IsWindows()) return;
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            if (_winDown) { _winDown = false; try { _send?.Invoke(125, false); } catch { } }
            _send = null;
        }

        private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code >= 0 && _send != null && GetForegroundWindow() == _hwnd)
                {
                    var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool injected = (k.flags & 0x10) != 0;
                    int msg = wParam.ToInt32();
                    bool down = msg is 0x100 or 0x104;          // WM_KEYDOWN, WM_SYSKEYDOWN
                    bool isWin = k.vkCode is 0x5B or 0x5C;
                    if (!injected && (isWin || _winDown))
                    {
                        int scan = (int)k.scanCode | ((k.flags & 1) != 0 ? 0xE000 : 0);
                        int ev = isWin ? (k.vkCode == 0x5B ? 125 : 126) : KeyMap.FromWindows(scan);
                        if (isWin) _winDown = down;
                        if (ev > 0) { _send(ev, down); return (IntPtr)1; }   // a Windows non arriva
                    }
                }
            }
            catch { }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }
}
