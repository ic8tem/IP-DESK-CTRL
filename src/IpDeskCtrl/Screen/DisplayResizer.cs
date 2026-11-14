using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using IpDeskCtrl.Core;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Risoluzione del PC comandato adattata alla finestra del visore (telefono in verticale, finestra piccola…).
    /// Si sceglie, tra le risoluzioni che il monitor supporta, quella con le proporzioni più vicine alla finestra
    /// e dimensione simile; a fine collegamento si rimette quella di prima.
    /// Windows: ChangeDisplaySettingsEx temporaneo (non scritto nel registro). Linux X11: xrandr.
    /// macOS, Wayland e Android: non disponibile (lo si dice al visore).
    /// </summary>
    public sealed class DisplayResizer : IDisposable
    {
        private string? _device;          // monitor cambiato (Windows: \\.\DISPLAY1; Linux: nome dell'uscita)
        private string? _originalMode;    // Linux: modo di prima ("1920x1080")
        private readonly object _lock = new();

        public static bool Supported =>
            OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && !(LinuxSession.IsWayland && !LinuxSession.ForceX11) && WaylandScreen.Which("xrandr"));

        /// <summary>Adatta il monitor alla finestra del visore (w×h pixel). "" = fatto, altrimenti il motivo.</summary>
        public string Fit(MonitorInfo? monitor, int w, int h)
        {
            lock (_lock)
            {
                if (w <= 0 || h <= 0) { Restore(); return ""; }
                if (!Supported) return OperatingSystem.IsMacOS() ? L.T("Sul Mac la risoluzione non si adatta ancora alla finestra.") : L.T("Su questo sistema la risoluzione non si può adattare alla finestra.");
                if (monitor == null || monitor.Name == "Tutti gli schermi") return L.T("Per adattare la risoluzione scegli un solo schermo del PC remoto.");
                try
                {
                    return OperatingSystem.IsWindows() ? FitWindows(monitor, w, h) : FitLinux(monitor, w, h);
                }
                catch (Exception ex) { return L.T("Risoluzione non cambiata: ") + ex.Message; }
            }
        }

        /// <summary>Punteggio: proporzioni prima di tutto, poi dimensione (più piccola dei 2/3 o più grande di 1,5 volte pesa).</summary>
        private static double Score(int mw, int mh, int w, int h)
        {
            double aspect = Math.Abs(Math.Log((double)mw / mh / ((double)w / h)));
            double area = Math.Abs(Math.Log((double)mw * mh / ((double)w * h)));
            return aspect * 4 + area;
        }

        // ------------------------------------------------------------------ Windows

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);

        private const int ENUM_CURRENT_SETTINGS = -1, CDS_FULLSCREEN = 4;
        private const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

        private static DEVMODE NewMode() => new() { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf<DEVMODE>() };

        private string FitWindows(MonitorInfo monitor, int w, int h)
        {
            string dev = @"\\.\" + monitor.Name;
            if (_device != null && _device != dev) Restore();
            var cur = NewMode();
            if (!EnumDisplaySettingsW(dev, ENUM_CURRENT_SETTINGS, ref cur)) return L.T("Monitor ") + monitor.Name + L.T(" non trovato.");
            var modes = new List<DEVMODE>();
            for (int i = 0; ; i++)
            {
                var m = NewMode();
                if (!EnumDisplaySettingsW(dev, i, ref m)) break;
                if (m.dmBitsPerPel == cur.dmBitsPerPel && m.dmPelsWidth >= 640 && m.dmPelsHeight >= 480) modes.Add(m);
            }
            if (modes.Count == 0) return L.T("Il monitor non elenca altre risoluzioni.");
            // a parità di dimensione, la frequenza attuale (o la più alta)
            var best = modes
                .OrderBy(m => Score(m.dmPelsWidth, m.dmPelsHeight, w, h))
                .ThenBy(m => m.dmDisplayFrequency == cur.dmDisplayFrequency ? 0 : 1)
                .ThenByDescending(m => m.dmDisplayFrequency)
                .First();
            if (best.dmPelsWidth == cur.dmPelsWidth && best.dmPelsHeight == cur.dmPelsHeight) return "";
            best.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
            int r = ChangeDisplaySettingsExW(dev, ref best, IntPtr.Zero, CDS_FULLSCREEN, IntPtr.Zero);
            if (r != 0) return L.F("Risoluzione {0}×{1} non accettata dal monitor (codice {2}).", best.dmPelsWidth, best.dmPelsHeight, r);
            _device = dev;
            Core.SessionLog.Write($"SCHERMO risoluzione di {monitor.Name} adattata alla finestra del visore: {best.dmPelsWidth}×{best.dmPelsHeight} (finestra {w}×{h})");
            return "";
        }

        // ------------------------------------------------------------------ Linux (xrandr)

        private string FitLinux(MonitorInfo monitor, int w, int h)
        {
            string q = Run("xrandr", "--query");
            // uscita del monitor: per nome, altrimenti per posizione
            string? output = null, current = null;
            var modes = new List<(int w, int h)>();
            bool inside = false;
            foreach (var line in q.Split('\n'))
            {
                var head = Regex.Match(line, @"^(\S+) connected (?:primary )?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)");
                if (head.Success)
                {
                    inside = head.Groups[1].Value == monitor.Name
                             || (int.Parse(head.Groups[4].Value) == monitor.X && int.Parse(head.Groups[5].Value) == monitor.Y);
                    if (inside) { output = head.Groups[1].Value; current = head.Groups[2].Value + "x" + head.Groups[3].Value; modes.Clear(); }
                    continue;
                }
                if (!char.IsWhiteSpace(line.FirstOrDefault())) { if (output != null && inside) inside = false; continue; }
                if (!inside) continue;
                var m = Regex.Match(line, @"^\s+(\d+)x(\d+)");
                if (m.Success) modes.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
            }
            if (output == null || current == null) return L.T("Uscita video di ") + monitor.Name + L.T(" non trovata con xrandr.");
            if (_device != null && _device != output) Restore();
            var best = modes.Where(m => m.w >= 640 && m.h >= 480).OrderBy(m => Score(m.w, m.h, w, h)).FirstOrDefault();
            if (best.w == 0) return L.T("Il monitor non elenca altre risoluzioni.");
            string target = $"{best.w}x{best.h}";
            if (target == current) return "";
            if (_device == null) _originalMode = current;
            string err = RunErr("xrandr", $"--output {output} --mode {target}");
            if (err.Length > 0) return L.T("Risoluzione non cambiata: ") + err;
            _device = output;
            Core.SessionLog.Write($"SCHERMO risoluzione di {output} adattata alla finestra del visore: {target} (finestra {w}×{h})");
            return "";
        }

        private static string Run(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }

        private static string RunErr(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            string e = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? "" : (e.Trim().Length > 0 ? e.Trim() : "xrandr codice " + p.ExitCode);
        }

        // ------------------------------------------------------------------ ripristino

        /// <summary>Rimette la risoluzione di prima (fine collegamento, opzione spenta, altro monitor).</summary>
        public void Restore()
        {
            lock (_lock)
            {
                var dev = _device;
                _device = null;
                if (dev == null) return;
                try
                {
                    if (OperatingSystem.IsWindows()) ChangeDisplaySettingsExW(dev, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
                    else if (_originalMode != null) RunErr("xrandr", $"--output {dev} --mode {_originalMode}");
                    Core.SessionLog.Write($"SCHERMO risoluzione di {dev} rimessa com'era");
                }
                catch (Exception ex) { Core.ErrorLog.Write("Ripristino della risoluzione: " + ex.Message); }
            }
        }

        public void Dispose() => Restore();
    }
}
