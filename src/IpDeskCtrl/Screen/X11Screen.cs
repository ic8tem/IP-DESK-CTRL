using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Screen
{
    /// <summary>Funzioni di Xlib, XTest e XRandR (Linux e Raspberry Pi con sessione X11).</summary>
    internal static class X11
    {
        private const string Lib = "libX11.so.6", Tst = "libXtst.so.6", Rr = "libXrandr.so.2";

        [DllImport(Lib)] public static extern int XInitThreads();
        [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr d);
        [DllImport(Lib)] public static extern int XDefaultScreen(IntPtr d);
        [DllImport(Lib)] public static extern nuint XDefaultRootWindow(IntPtr d);
        [DllImport(Lib)] public static extern int XDisplayWidth(IntPtr d, int screen);
        [DllImport(Lib)] public static extern int XDisplayHeight(IntPtr d, int screen);
        [DllImport(Lib)] public static extern IntPtr XGetImage(IntPtr d, nuint drawable, int x, int y, uint w, uint h, nuint planeMask, int format);
        [DllImport(Lib)] public static extern int XFree(IntPtr p);
        [DllImport(Lib)] public static extern int XFlush(IntPtr d);
        [DllImport(Lib)] public static extern IntPtr XGetAtomName(IntPtr d, nuint atom);
        [DllImport(Lib)] public static extern int XQueryPointer(IntPtr d, nuint w, out nuint root, out nuint child, out int rx, out int ry, out int wx, out int wy, out uint mask);
        [DllImport(Lib)] public static extern int XSetErrorHandler(IntPtr handler);
        [DllImport(Lib)] public static extern int XSync(IntPtr d, int discard);
        [DllImport(Lib)] public static extern int XDisplayKeycodes(IntPtr d, out int min, out int max);
        [DllImport(Lib)] public static extern IntPtr XGetKeyboardMapping(IntPtr d, byte first, int count, out int keysymsPerKeycode);
        [DllImport(Lib)] public static extern int XChangeKeyboardMapping(IntPtr d, int first, int keysymsPerCode, nuint[] keysyms, int numCodes);

        [DllImport(Tst)] public static extern int XTestFakeMotionEvent(IntPtr d, int screen, int x, int y, nuint delay);
        [DllImport(Tst)] public static extern int XTestFakeButtonEvent(IntPtr d, uint button, int press, nuint delay);
        [DllImport(Tst)] public static extern int XTestFakeKeyEvent(IntPtr d, uint keycode, int press, nuint delay);

        [DllImport(Rr)] public static extern IntPtr XRRGetMonitors(IntPtr d, nuint window, int getActive, out int n);
        [DllImport(Rr)] public static extern void XRRFreeMonitors(IntPtr monitors);

        public const int ZPixmap = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct XImage
        {
            public int width, height, xoffset, format;
            public IntPtr data;
            public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XRRMonitorInfo
        {
            public nuint name;
            public int primary, automatic, noutput, x, y, width, height, mwidth, mheight;
            public IntPtr outputs;
        }

        // un errore X (finestra sparita, area fuori schermo) non deve chiudere il programma
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ErrorHandler(IntPtr d, IntPtr ev);
        private static readonly ErrorHandler Ignore = (_, _) => 0;
        private static bool _init;

        public static void InitThreads()
        {
            if (_init) return;
            _init = true;
            try
            {
                XInitThreads();
                XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(Ignore));
            }
            catch { /* senza libX11 (solo Wayland): niente */ }
        }

        public static IntPtr Open() => XOpenDisplay(IntPtr.Zero);
    }

    /// <summary>Linux/Raspberry Pi con X11: XGetImage sulla finestra radice, monitor da XRandR.</summary>
    public sealed class X11Screen : IScreenSource
    {
        private readonly IntPtr _d;
        private readonly nuint _root;
        private readonly object _lock = new();
        public string Note { get; private set; } = "";

        public X11Screen()
        {
            X11.InitThreads();
            _d = X11.Open();
            if (_d == IntPtr.Zero) Note = "Display X11 non disponibile (manca DISPLAY?)";
            else _root = X11.XDefaultRootWindow(_d);
        }

        public List<MonitorInfo> Monitors()
        {
            var list = new List<MonitorInfo>();
            if (_d == IntPtr.Zero) return list;
            lock (_lock)
            {
                try
                {
                    IntPtr p = X11.XRRGetMonitors(_d, _root, 1, out int n);
                    if (p != IntPtr.Zero)
                    {
                        int size = Marshal.SizeOf<X11.XRRMonitorInfo>();
                        for (int i = 0; i < n; i++)
                        {
                            var m = Marshal.PtrToStructure<X11.XRRMonitorInfo>(p + i * size);
                            string name = $"Schermo {i + 1}";
                            IntPtr an = X11.XGetAtomName(_d, m.name);
                            if (an != IntPtr.Zero) { name = Marshal.PtrToStringAnsi(an) ?? name; X11.XFree(an); }
                            list.Add(new MonitorInfo(name, m.x, m.y, m.width, m.height, m.primary != 0));
                        }
                        X11.XRRFreeMonitors(p);
                    }
                }
                catch { /* XRandR assente: un solo schermo */ }
                if (list.Count == 0)
                {
                    int s = X11.XDefaultScreen(_d);
                    list.Add(new MonitorInfo("Schermo", 0, 0, X11.XDisplayWidth(_d, s), X11.XDisplayHeight(_d, s), true));
                }
            }
            return list;
        }

        public bool Capture(MonitorInfo area, Frame into)
        {
            if (_d == IntPtr.Zero) return false;
            lock (_lock)
            {
                IntPtr img = X11.XGetImage(_d, _root, area.X, area.Y, (uint)Math.Max(1, area.W), (uint)Math.Max(1, area.H), unchecked((nuint)~0UL), X11.ZPixmap);
                if (img == IntPtr.Zero) { Note = "XGetImage non riuscito"; return false; }
                try
                {
                    var xi = Marshal.PtrToStructure<X11.XImage>(img);
                    into.Ensure(xi.width, xi.height);
                    if (xi.bits_per_pixel == 32)
                    {
                        int row = xi.width * 4;
                        for (int y = 0; y < xi.height; y++)
                            Marshal.Copy(xi.data + y * xi.bytes_per_line, into.Data, y * into.Stride, row);
                    }
                    else if (xi.bits_per_pixel == 16)
                    {
                        // RGB565 (alcuni Raspberry)
                        var line = new byte[xi.bytes_per_line];
                        for (int y = 0; y < xi.height; y++)
                        {
                            Marshal.Copy(xi.data + y * xi.bytes_per_line, line, 0, line.Length);
                            int o = y * into.Stride;
                            for (int x = 0; x < xi.width; x++)
                            {
                                int v = line[x * 2] | (line[x * 2 + 1] << 8);
                                into.Data[o++] = (byte)((v & 0x1F) << 3);
                                into.Data[o++] = (byte)(((v >> 5) & 0x3F) << 2);
                                into.Data[o++] = (byte)(((v >> 11) & 0x1F) << 3);
                                into.Data[o++] = 255;
                            }
                        }
                    }
                    else { Note = $"Profondità colore non gestita ({xi.bits_per_pixel} bit)"; return false; }
                    Note = "";
                    return true;
                }
                finally
                {
                    var xi = Marshal.PtrToStructure<X11.XImage>(img);
                    if (xi.data != IntPtr.Zero) X11.XFree(xi.data);
                    X11.XFree(img);
                }
            }
        }

        public (int x, int y)? Cursor()
        {
            if (_d == IntPtr.Zero) return null;
            lock (_lock)
            {
                return X11.XQueryPointer(_d, _root, out _, out _, out int rx, out int ry, out _, out _, out _) != 0 ? (rx, ry) : null;
            }
        }

        public void Dispose()
        {
            lock (_lock) { if (_d != IntPtr.Zero) X11.XCloseDisplay(_d); }
        }
    }
}
