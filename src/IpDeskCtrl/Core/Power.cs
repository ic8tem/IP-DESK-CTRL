using System;
using System.Diagnostics;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Core
{
    /// <summary>Riavvio del PC chiesto dal visore (menu «Tasti» → «Riavvia il PC remoto»).</summary>
    public static class Power
    {
        /// <summary>
        /// Avvia il riavvio. "" = partito, altrimenti il motivo per cui non si può.
        ///   Windows: tramite il servizio se c'è (funziona anche senza diritti), altrimenti shutdown /r /t 5;
        ///   Linux e Raspberry: systemctl reboot; macOS: «System Events» restart con osascript.
        /// </summary>
        public static string Reboot()
        {
            if (Platform.Reboot != null) return Platform.Reboot();
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        string r = WinHelper.AskService("REBOOT");
                        if (r.StartsWith("OK")) return "";
                    }
                    catch { }
                    return Run("shutdown", "/r /t 5 /c \"IP-DESK-CTRL: riavvio chiesto da remoto\"");
                }
                if (OperatingSystem.IsMacOS()) return Run("osascript", "-e \"tell application \\\"System Events\\\" to restart\"");
                if (OperatingSystem.IsLinux()) return Run("systemctl", "reboot");
                return "Il riavvio da remoto non è possibile su questo sistema.";
            }
            catch (Exception ex) { return "Riavvio non riuscito: " + ex.Message; }
        }

        public static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p == null) return $"{exe} non avviato";
            if (!p.WaitForExit(8000)) return "";        // ancora in corso: il riavvio è partito
            string err = p.StandardError.ReadToEnd().Trim();
            return p.ExitCode == 0 ? "" : $"Riavvio non riuscito ({exe}: {(err.Length > 0 ? err : "codice " + p.ExitCode)})";
        }
    }
}
