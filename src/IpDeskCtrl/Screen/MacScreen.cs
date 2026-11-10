using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Screen
{
    internal static class Mac
    {
        public const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        public const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        public const string AS = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        [StructLayout(LayoutKind.Sequential)] public struct CGPoint { public double X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct CGSize { public double W, H; }
        [StructLayout(LayoutKind.Sequential)] public struct CGRect { public CGPoint O; public CGSize S; }

        [DllImport(CG)] public static extern int CGGetActiveDisplayList(uint max, [Out] uint[] ids, out uint count);
        [DllImport(CG)] public static extern uint CGMainDisplayID();
        [DllImport(CG)] public static extern CGRect CGDisplayBounds(uint display);
        [DllImport(CG)] public static extern IntPtr CGDisplayCreateImage(uint display);
        [DllImport(CG)] public static extern nuint CGImageGetWidth(IntPtr img);
        [DllImport(CG)] public static extern nuint CGImageGetHeight(IntPtr img);
        [DllImport(CG)] public static extern void CGImageRelease(IntPtr img);
        [DllImport(CG)] public static extern IntPtr CGColorSpaceCreateDeviceRGB();
        [DllImport(CG)] public static extern void CGColorSpaceRelease(IntPtr cs);
        [DllImport(CG)] public static extern IntPtr CGBitmapContextCreate(IntPtr data, nuint w, nuint h, nuint bpc, nuint bpr, IntPtr cs, uint info);
        [DllImport(CG)] public static extern void CGContextDrawImage(IntPtr ctx, CGRect rect, IntPtr img);
        [DllImport(CG)] public static extern void CGContextRelease(IntPtr ctx);
        [DllImport(CG)] public static extern void CGContextClearRect(IntPtr ctx, CGRect rect);
        [DllImport(CG)] public static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CG)] public static extern CGPoint CGEventGetLocation(IntPtr ev);
        [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CGPreflightScreenCaptureAccess();
        [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CGRequestScreenCaptureAccess();
        [DllImport(CF)] public static extern void CFRelease(IntPtr p);

        // BGRX: kCGImageAlphaNoneSkipFirst | kCGBitmapByteOrder32Little
        public const uint BitmapBgrx = 6 | (2 << 12);

        public static void OpenSettings(string pane)
        {
            try { Process.Start(new ProcessStartInfo("open", $"\"x-apple.systempreferences:com.apple.preference.security?{pane}\"") { UseShellExecute = false }); } catch { }
        }
    }

    /// <summary>
    /// macOS: CGDisplayCreateImage per ogni schermo. Le coordinate del mouse sono in punti; l'immagine
    /// di uno schermo Retina è in pixel (il doppio). «Tutti gli schermi» viene composto in punti.
    /// Serve il permesso «Registrazione schermo» (Impostazioni di Sistema › Privacy e sicurezza).
    /// </summary>
    public sealed class MacScreen : IScreenSource
    {
        private readonly Dictionary<string, uint> _ids = new();
        private bool _asked;
        public string Note { get; private set; } = "";

        private readonly object _lock = new();

        public List<MonitorInfo> Monitors() { lock (_lock) return MonitorsCore(); }

        private List<MonitorInfo> MonitorsCore()
        {
            var list = new List<MonitorInfo>();
            _ids.Clear();
            var ids = new uint[16];
            if (Mac.CGGetActiveDisplayList(16, ids, out uint n) != 0) n = 0;
            uint main = Mac.CGMainDisplayID();
            for (int i = 0; i < n; i++)
            {
                var b = Mac.CGDisplayBounds(ids[i]);
                string name = $"Schermo {i + 1}";
                _ids[name] = ids[i];
                list.Add(new MonitorInfo(name, (int)b.O.X, (int)b.O.Y, (int)b.S.W, (int)b.S.H, ids[i] == main));
            }
            if (list.Count == 0)
            {
                var b = Mac.CGDisplayBounds(main);
                _ids["Schermo 1"] = main;
                list.Add(new MonitorInfo("Schermo 1", (int)b.O.X, (int)b.O.Y, (int)b.S.W, (int)b.S.H, true));
            }
            return list;
        }

        private void CheckPermission()
        {
            if (_asked) return;
            _asked = true;
            try
            {
                if (!Mac.CGPreflightScreenCaptureAccess())
                {
                    Mac.CGRequestScreenCaptureAccess();
                    Note = "Su questo Mac manca il permesso «Registrazione schermo» per IP-DESK-CTRL: si vede solo lo sfondo. " +
                           "Impostazioni di Sistema › Privacy e sicurezza › Registrazione schermo, poi riavvia il programma.";
                }
            }
            catch { }
        }

        public bool Capture(MonitorInfo area, Frame into) { lock (_lock) return CaptureCore(area, into); }

        private bool CaptureCore(MonitorInfo area, Frame into)
        {
            CheckPermission();
            if (_ids.Count == 0) MonitorsCore();
            if (_ids.TryGetValue(area.Name, out uint single))
            {
                IntPtr img = Mac.CGDisplayCreateImage(single);
                if (img == IntPtr.Zero) return false;
                try
                {
                    int w = (int)Mac.CGImageGetWidth(img), h = (int)Mac.CGImageGetHeight(img);
                    into.Ensure(w, h);
                    return Draw(into, new[] { (img, new Mac.CGRect { S = new Mac.CGSize { W = w, H = h } }) });
                }
                finally { Mac.CGImageRelease(img); }
            }

            // tutti gli schermi, in punti
            into.Ensure(Math.Max(1, area.W), Math.Max(1, area.H));
            var parts = new List<(IntPtr, Mac.CGRect)>();
            try
            {
                foreach (var kv in _ids)
                {
                    var b = Mac.CGDisplayBounds(kv.Value);
                    IntPtr img = Mac.CGDisplayCreateImage(kv.Value);
                    if (img == IntPtr.Zero) continue;
                    double y = area.H - (b.O.Y - area.Y) - b.S.H;     // il contesto ha l'origine in basso a sinistra
                    parts.Add((img, new Mac.CGRect { O = new Mac.CGPoint { X = b.O.X - area.X, Y = y }, S = b.S }));
                }
                return parts.Count > 0 && Draw(into, parts);
            }
            finally { foreach (var (img, _) in parts) Mac.CGImageRelease(img); }
        }

        private static bool Draw(Frame into, IEnumerable<(IntPtr img, Mac.CGRect rect)> parts)
        {
            var h = GCHandle.Alloc(into.Data, GCHandleType.Pinned);
            IntPtr cs = Mac.CGColorSpaceCreateDeviceRGB();
            try
            {
                IntPtr ctx = Mac.CGBitmapContextCreate(h.AddrOfPinnedObject(), (nuint)into.Width, (nuint)into.Height, 8, (nuint)into.Stride, cs, Mac.BitmapBgrx);
                if (ctx == IntPtr.Zero) return false;
                try
                {
                    Mac.CGContextClearRect(ctx, new Mac.CGRect { S = new Mac.CGSize { W = into.Width, H = into.Height } });
                    foreach (var (img, rect) in parts) Mac.CGContextDrawImage(ctx, rect, img);
                }
                finally { Mac.CGContextRelease(ctx); }
                return true;
            }
            finally
            {
                Mac.CGColorSpaceRelease(cs);
                h.Free();
            }
        }

        public (int x, int y)? Cursor()
        {
            IntPtr ev = Mac.CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) return null;
            try { var p = Mac.CGEventGetLocation(ev); return ((int)p.X, (int)p.Y); }
            finally { Mac.CFRelease(ev); }
        }

        public void Dispose() { }
    }
}
