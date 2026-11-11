using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Legame con IP-TRX-CTRL (controllo remoto della radio, stesso autore): se è installato sul PC remoto la
    /// plancia mostra «Radio (IP-TRX-CTRL)», che lo avvia lì e, se c'è, anche su questo PC.
    /// Percorsi dei suoi installatori: Windows «Programmi\IP-TRX-CTRL» (o per utente in
    /// AppData\Local\Programs), Linux /opt/ip-trx-ctrl, macOS /Applications/IP-TRX-CTRL.app.
    /// </summary>
    public static class TrxCtrl
    {
        private const string Name = "IP-TRX-CTRL";

        public static string? FindPath()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var dirs = new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                    };
                    return dirs.Where(d => d.Length > 0).Select(d => Path.Combine(d, Name, Name + ".exe")).FirstOrDefault(File.Exists);
                }
                if (OperatingSystem.IsMacOS())
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    return new[] { "/Applications/" + Name + ".app", Path.Combine(home, "Applications", Name + ".app") }.FirstOrDefault(Directory.Exists);
                }
                if (OperatingSystem.IsLinux())
                    return new[] { "/opt/ip-trx-ctrl/" + Name, "/usr/bin/ip-trx-ctrl", "/usr/local/bin/ip-trx-ctrl" }.FirstOrDefault(File.Exists);
            }
            catch { }
            return null;
        }

        public static bool Installed => FindPath() != null;

        /// <summary>Avvia IP-TRX-CTRL (se è già aperto non fa nulla). "" = fatto, altrimenti il motivo.</summary>
        public static string Launch()
        {
            string? p = FindPath();
            if (p == null) return "IP-TRX-CTRL non è installato su questo PC.";
            try
            {
                if (Process.GetProcessesByName(Name).Length > 0) return "";
                if (OperatingSystem.IsMacOS()) Process.Start("open", $"-a \"{p}\"");
                else Process.Start(new ProcessStartInfo(p) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(p) ?? "" });
                SessionLog.Write("IP-TRX-CTRL avviato: " + p);
                return "";
            }
            catch (Exception ex) { return "IP-TRX-CTRL non si avvia: " + ex.Message; }
        }
    }
}
