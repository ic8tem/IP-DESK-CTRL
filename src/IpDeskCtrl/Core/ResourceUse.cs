using System;
using System.Diagnostics;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Processore e memoria usati dal programma, per il registro (una riga al minuto): processore in % di tutto
    /// il PC dall'ultima misura, memoria occupata (working set) e memoria gestita da .NET. L'eventuale ffmpeg
    /// è un programma a parte e si conta da sé (anche lui riportato, se c'è).
    /// </summary>
    public static class ResourceUse
    {
        private static TimeSpan _lastCpu;
        private static long _lastAt;
        private static readonly object Lock = new();

        public static string Sample()
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                var cpu = p.TotalProcessorTime;
                long now = Stopwatch.GetTimestamp();
                double pct;
                lock (Lock)
                {
                    double wall = _lastAt == 0 ? 0 : (now - _lastAt) / (double)Stopwatch.Frequency;
                    pct = wall > 0 ? (cpu - _lastCpu).TotalSeconds / wall / Environment.ProcessorCount * 100 : 0;
                    _lastCpu = cpu; _lastAt = now;
                }
                string s = $"processore {pct:F1}% · memoria {p.WorkingSet64 / 1048576} MB (gestita {GC.GetTotalMemory(false) / 1048576} MB)";
                long ff = 0;
                foreach (var f in Process.GetProcessesByName("ffmpeg"))
                    using (f) { try { ff += f.WorkingSet64; } catch { } }
                if (ff > 0) s += $" · ffmpeg {ff / 1048576} MB";
                return s;
            }
            catch { return ""; }
        }
    }
}
