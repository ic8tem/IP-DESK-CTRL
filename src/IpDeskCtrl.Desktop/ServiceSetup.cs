using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using IpDeskCtrl.Core;

namespace IpDeskCtrl
{
    /// <summary>
    /// Servizio di Windows installato a distanza: chi comanda lo chiede, chi è al PC risponde Sì alla UAC una volta,
    /// e da lì UAC, schermata di blocco e programmi da amministratore si vedono e si comandano.
    /// Solo per IP-DESK-CTRL installato con l'installatore in Programmi: il servizio gira come SYSTEM e non deve mai
    /// avviare un eseguibile che un utente normale può sostituire (versione portatile, installazione per utente).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ServiceSetup
    {
        public const string Arg = "--installa-servizio";

        private static string Dir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        /// <summary>Installato con l'installatore come amministratore (in Programmi): il servizio si può aggiungere.</summary>
        public static bool CanInstall
        {
            get
            {
                if (Updater.Detect() != Updater.Kind.WindowsSetup) return false;
                string[] roots =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                };
                return roots.Any(r => r.Length > 0 && Dir.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>1 servizio attivo, 2 assente ma installabile, 3 assente e non installabile.</summary>
        public static int State() =>
            Screen.WinHelper.Current?.Alive == true ? 1 : CanInstall ? 2 : 3;

        /// <summary>Lato programma: rilancia sé stesso come amministratore (UAC) per installare il servizio.</summary>
        public static async Task<string> InstallAsync()
        {
            if (!CanInstall)
                return L.T("serve IP-DESK-CTRL installato con l'installatore come amministratore (non la versione portatile né quella per un solo utente)");
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Arg) { UseShellExecute = true, Verb = "runas" });
                if (p == null) return L.T("non è partito");
                var wait = p.WaitForExitAsync();
                if (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMinutes(3))) != wait) return L.T("nessuna risposta alla richiesta di Windows entro 3 minuti");
                return p.ExitCode == 0 ? "" : L.F("sc.exe ha risposto con l'errore {0}", p.ExitCode);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return L.T("chi è al PC ha risposto No alla richiesta di Windows (UAC)");
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Processo da amministratore (--installa-servizio): crea, descrive e avvia il servizio come l'installatore.</summary>
        public static int InstallHere()
        {
            if (!CanInstall) return 2;
            string exe = Path.Combine(Dir, "IP-DESK-CTRL.exe");
            int Sc(string a)
            {
                using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), a) { UseShellExecute = false, CreateNoWindow = true });
                p!.WaitForExit(30000);
                return p.ExitCode;
            }
            int rc = Sc($"create IPDeskCtrl binPath= \"\\\"{exe}\\\" --service\" start= auto DisplayName= \"IP-DESK-CTRL accesso completo\"");
            if (rc == 1073) rc = Sc($"config IPDeskCtrl binPath= \"\\\"{exe}\\\" --service\" start= auto");     // c'era già
            if (rc != 0) { SessionLog.Write($"SERVIZIO sc create non riuscito ({rc})"); return rc; }
            Sc("description IPDeskCtrl \"Permette a IP-DESK-CTRL di mostrare e comandare le richieste UAC, la schermata di blocco e i programmi da amministratore durante i collegamenti accettati.\"");
            try
            {
                // Ctrl+Alt+Canc generato dal servizio (SendSAS)
                using var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                k.SetValue("SoftwareSASGeneration", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception ex) { SessionLog.Write("SERVIZIO SoftwareSASGeneration non impostato: " + ex.Message); }
            rc = Sc("start IPDeskCtrl");
            SessionLog.Write("SERVIZIO installato dal programma" + (rc == 0 || rc == 1056 ? " e avviato" : $", avvio non riuscito ({rc})"));
            return rc == 0 || rc == 1056 ? 0 : rc;
        }

        /// <summary>Richiesta UAC aperta: consent.exe è in esecuzione.</summary>
        public static bool UacPending()
        {
            try
            {
                var ps = Process.GetProcessesByName("consent");
                foreach (var p in ps) p.Dispose();
                return ps.Length > 0;
            }
            catch { return false; }
        }
    }
}
