using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Windows 10 e 11 rallentano i programmi ridotti a icona o coperti (risparmio energetico «EcoQoS»):
    /// la decodifica dei fotogrammi va più piano e il PC remoto crede che la linea sia peggiorata.
    /// Durante i collegamenti si chiede a Windows di non farlo (anche per ffmpeg) e si alza un po' la priorità.
    /// </summary>
    public static class PowerThrottle
    {
        private static int _sessions;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        /// <summary>Niente risparmio energetico per quel processo (velocità e risoluzione dei timer).</summary>
        public static void Disable(IntPtr process)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var s = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0x1 | 0x4, StateMask = 0 };   // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                SetProcessInformation(process, 4 /* ProcessPowerThrottling */, ref s, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
            }
            catch { }
        }

        public static void Disable(Process p) { try { Disable(p.Handle); } catch { } }

        /// <summary>Un collegamento come visore è cominciato.</summary>
        public static void SessionStarted()
        {
            if (Interlocked.Increment(ref _sessions) != 1) return;
            try
            {
                using var me = Process.GetCurrentProcess();
                Disable(me.Handle);
                if (OperatingSystem.IsWindows()) me.PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch { }
        }

        public static void SessionEnded()
        {
            if (Interlocked.Decrement(ref _sessions) != 0) return;
            try
            {
                using var me = Process.GetCurrentProcess();
                if (OperatingSystem.IsWindows()) me.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        }
    }
}
