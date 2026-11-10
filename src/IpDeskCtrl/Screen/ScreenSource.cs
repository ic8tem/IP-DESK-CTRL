using System;
using System.Collections.Generic;
using System.Linq;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Un monitor (o l'unione di tutti). Le coordinate sono quelle del desktop usate per mouse e tastiera:
    /// pixel fisici su Windows e Linux, punti su macOS.
    /// </summary>
    public sealed record MonitorInfo(string Name, int X, int Y, int W, int H, bool Primary)
    {
        public static MonitorInfo Union(IReadOnlyList<MonitorInfo> list)
        {
            if (list.Count == 0) return new MonitorInfo("Tutti gli schermi", 0, 0, 1, 1, true);
            int x0 = list.Min(m => m.X), y0 = list.Min(m => m.Y);
            int x1 = list.Max(m => m.X + m.W), y1 = list.Max(m => m.Y + m.H);
            return new MonitorInfo("Tutti gli schermi", x0, y0, x1 - x0, y1 - y0, false);
        }

        public string Label(int index) => $"{index + 1}: {Name} · {W}×{H}" + (Primary ? " · principale" : "");
    }

    /// <summary>Immagine BGRA a 32 bit, dall'alto in basso.</summary>
    public sealed class Frame
    {
        public int Width, Height, Stride;
        public byte[] Data = Array.Empty<byte>();

        public void Ensure(int w, int h)
        {
            Width = w; Height = h; Stride = w * 4;
            if (Data.Length != Stride * h) Data = new byte[Stride * h];
        }
    }

    /// <summary>Cattura dello schermo, una per sistema operativo.</summary>
    public interface IScreenSource : IDisposable
    {
        List<MonitorInfo> Monitors();

        /// <summary>
        /// Cattura l'area indicata (un monitor o l'unione). L'immagine può essere più grande dell'area
        /// in coordinate (schermi Retina): la corrispondenza la fa chi usa il frame.
        /// </summary>
        bool Capture(MonitorInfo area, Frame into);

        /// <summary>Posizione del puntatore in coordinate del desktop, se nota.</summary>
        (int x, int y)? Cursor();

        /// <summary>Avviso per l'utente remoto (permessi mancanti, limiti del sistema), "" se va tutto bene.</summary>
        string Note { get; }
    }

    public static class ScreenSources
    {
        public static IScreenSource Create()
        {
            if (OperatingSystem.IsWindows()) return new WindowsScreen();
            if (OperatingSystem.IsMacOS()) return new MacScreen();
            if (LinuxSession.IsWayland && !LinuxSession.ForceX11) return new WaylandScreen();
            return new X11Screen();
        }
    }

    public static class LinuxSession
    {
        public static bool IsWayland =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

        /// <summary>IPDK_X11=1 forza la cattura X11 anche in una sessione Wayland (utile con XWayland a schermo intero).</summary>
        public static bool ForceX11 => Environment.GetEnvironmentVariable("IPDK_X11") == "1";
    }
}
