using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Screen
{
    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetUserObjectInformation(IntPtr obj, int index, System.Text.StringBuilder? info, int length, out int needed);

        /// <summary>
        /// Nome del desktop che riceve mouse e tastiera: «Default» quando il PC è in uso; «Winlogon» con la schermata
        /// di blocco, di accesso o Ctrl+Alt+Canc; «Screen-saver»… "" se Windows non lo dice (desktop protetto).
        /// </summary>
        public static string InputDesktopName()
        {
            IntPtr d = OpenInputDesktop(0, false, 0x0001 /* DESKTOP_READOBJECTS */);
            if (d == IntPtr.Zero) d = OpenInputDesktop(0, false, 0x02000000 /* MAXIMUM_ALLOWED */);
            if (d == IntPtr.Zero) return "";
            try
            {
                var sb = new System.Text.StringBuilder(64);
                return GetUserObjectInformation(d, 2 /* UOI_NAME */, sb, sb.Capacity * 2, out _) ? sb.ToString() : "";
            }
            finally { CloseDesktop(d); }
        }

        [ThreadStatic] private static IntPtr _threadDesk;
        [ThreadStatic] private static string? _threadDeskName;

        /// <summary>
        /// Solo per il processo di supporto «di sistema»: porta il thread sul desktop che riceve mouse e tastiera
        /// (Default, oppure Winlogon con schermata di blocco, UAC, Ctrl+Alt+Canc). Restituisce il nome del desktop.
        /// </summary>
        public static string FollowInputDesktop()
        {
            string name = InputDesktopName();
            if (name.Length == 0 || name == _threadDeskName) return name;
            IntPtr d = OpenInputDesktop(0, false, 0x10000000 /* GENERIC_ALL */);
            if (d == IntPtr.Zero)
            {
                Core.SessionLog.Write($"AIUTO desktop {name} non apribile (errore {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                return name;
            }
            if (SetThreadDesktop(d))
            {
                if (_threadDesk != IntPtr.Zero) CloseDesktop(_threadDesk);
                _threadDesk = d;
                _threadDeskName = name;
                Core.SessionLog.Write("AIUTO desktop attivo: " + name);
            }
            else
            {
                Core.SessionLog.Write($"AIUTO passaggio al desktop {name} non riuscito (errore {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                CloseDesktop(d);
            }
            return name;
        }

        /// <summary>Desktop su cui si trova ora il thread chiamante ("" se non è mai stato spostato).</summary>
        public static string ThreadDesktopName => _threadDeskName ?? "";

        public const string LockedNote = "Il PC remoto è bloccato (schermata di blocco o di accesso) oppure mostra una finestra di sicurezza (UAC, Ctrl+Alt+Canc): " +
                                         "Windows non la lascia vedere né comandare. Sbloccalo e l'immagine torna da sola.";
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll", SetLastError = true)] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        public const uint SRCCOPY = 0x00CC0020;
        public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    }

    /// <summary>
    /// Windows: GDI (BitBlt dal DC dello schermo virtuale) in pixel fisici, perché il programma è
    /// «per-monitor DPI aware» (app.manifest). Funziona su Windows 32 e 64 bit.
    /// Non si vedono il desktop sicuro (UAC, Ctrl+Alt+Canc) e la schermata di blocco.
    /// </summary>
    public sealed class WindowsScreen : IScreenSource
    {
        private IntPtr _memDc, _dib, _bits, _old;
        private int _w, _h;
        public string Note { get; private set; } = "";

        /// <summary>true nel processo di supporto di sistema: segue anche la schermata di blocco e UAC.</summary>
        public bool FollowInputDesktop { get; init; }

        public List<MonitorInfo> Monitors()
        {
            var list = new List<MonitorInfo>();
            int n = 0;
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Win32.RECT r, IntPtr data) =>
            {
                var mi = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
                bool primary = false;
                string name = $"Schermo {++n}";
                if (Win32.GetMonitorInfo(hMon, ref mi))
                {
                    primary = (mi.dwFlags & 1) != 0;
                    if (!string.IsNullOrEmpty(mi.szDevice)) name = mi.szDevice.Replace(@"\\.\", "");
                }
                list.Add(new MonitorInfo(name, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, primary));
                return true;
            }, IntPtr.Zero);
            if (list.Count == 0)
            {
                list.Add(new MonitorInfo("Schermo", Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
                    Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN), true));
            }
            return list;
        }

        public bool Capture(MonitorInfo area, Frame into)
        {
            // con il PC bloccato il desktop attivo non è quello dell'utente: lo si dice chiaramente
            string desktop = FollowInputDesktop ? Win32.FollowInputDesktop() : Win32.InputDesktopName();
            if (!FollowInputDesktop && (desktop.Length == 0 || !desktop.Equals("Default", StringComparison.OrdinalIgnoreCase)))
            {
                Note = Win32.LockedNote + (desktop.Length > 0 ? $" (desktop attivo: {desktop})" : "");
                return false;
            }

            int w = Math.Max(1, area.W), h = Math.Max(1, area.H);
            IntPtr screen = Win32.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) { Note = "Schermo non disponibile"; return false; }
            try
            {
                if (_memDc == IntPtr.Zero || w != _w || h != _h)
                {
                    FreeDib();
                    _memDc = Win32.CreateCompatibleDC(screen);
                    var bmi = new Win32.BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                        biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0
                    };
                    _dib = Win32.CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
                    if (_dib == IntPtr.Zero) { FreeDib(); return false; }
                    _old = Win32.SelectObject(_memDc, _dib);
                    _w = w; _h = h;
                }
                bool ok = Win32.BitBlt(_memDc, 0, 0, w, h, screen, area.X, area.Y, Win32.SRCCOPY);
                if (!ok && FollowInputDesktop)
                {
                    // dopo un cambio di desktop (UAC, blocco) si rifà tutto da capo una volta
                    FreeDib();
                    _memDc = Win32.CreateCompatibleDC(screen);
                    var bmi = new Win32.BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                        biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0
                    };
                    _dib = Win32.CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
                    if (_dib == IntPtr.Zero) { FreeDib(); return false; }
                    _old = Win32.SelectObject(_memDc, _dib);
                    _w = w; _h = h;
                    ok = Win32.BitBlt(_memDc, 0, 0, w, h, screen, area.X, area.Y, Win32.SRCCOPY);
                }
                if (!ok)
                {
                    Note = $"Cattura dello schermo non riuscita (BitBlt, errore {Marshal.GetLastWin32Error()}) sull'area {area.X},{area.Y} {w}×{h}";
                    return false;
                }
                into.Ensure(w, h);
                Marshal.Copy(_bits, into.Data, 0, into.Data.Length);
                Note = AllBlack(into) && desktop.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    ? "Lo schermo del PC remoto è completamente nero: di solito è una sessione di Desktop remoto di Windows (RDP) rimasta scollegata, " +
                      "che spegne lo schermo vero. Su quel PC accedi di nuovo dalla tastiera, oppure chiudi la sessione RDP."
                    : "";
                return true;
            }
            finally { Win32.ReleaseDC(IntPtr.Zero, screen); }
        }

        /// <summary>Guarda circa 4000 pixel sparsi: tutti a zero = immagine nera.</summary>
        private static bool AllBlack(Frame f)
        {
            int px = f.Width * f.Height;
            int step = Math.Max(1, px / 4000);
            for (int i = 0; i < px; i += step)
            {
                int o = i * 4;
                if ((f.Data[o] | f.Data[o + 1] | f.Data[o + 2]) != 0) return false;
            }
            return true;
        }

        public (int x, int y)? Cursor() => Win32.GetCursorPos(out var p) ? (p.X, p.Y) : null;

        private void FreeDib()
        {
            if (_memDc != IntPtr.Zero && _old != IntPtr.Zero) Win32.SelectObject(_memDc, _old);
            if (_dib != IntPtr.Zero) Win32.DeleteObject(_dib);
            if (_memDc != IntPtr.Zero) Win32.DeleteDC(_memDc);
            _memDc = _dib = _bits = _old = IntPtr.Zero;
            _w = _h = 0;
        }

        public void Dispose() => FreeDib();
    }
}
