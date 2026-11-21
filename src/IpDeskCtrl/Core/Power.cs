using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
                    // 1) il servizio: ha sempre il diritto di riavviare, qualunque sia l'account dell'utente
                    string svc;
                    try
                    {
                        svc = WinHelper.AskService("REBOOT");
                        if (svc.StartsWith("OK")) { SessionLog.Write("RIAVVIO affidato al servizio di Windows"); return ""; }
                    }
                    catch (Exception ex) { svc = ex is TimeoutException ? "servizio non installato o non avviato" : ex.Message; }
                    SessionLog.Write("RIAVVIO senza il servizio (" + svc + "): provo con i diritti dell'utente");
                    // 2) shutdown.exe con i diritti dell'utente (di solito concessi a chi è seduto al PC)
                    string err = Run("shutdown", "/r /f /t 5 /c \"IP-DESK-CTRL: riavvio chiesto da remoto\"");
                    if (err.Length == 0) { SessionLog.Write("RIAVVIO avviato con shutdown.exe"); return ""; }
                    SessionLog.Write("RIAVVIO " + err);
                    // 3) direttamente con le API di Windows, attivando il privilegio di arresto se l'account lo ha
                    string api = WinReboot();
                    if (api.Length == 0) { SessionLog.Write("RIAVVIO avviato con ExitWindowsEx"); return ""; }
                    SessionLog.Write("RIAVVIO ExitWindowsEx: " + api);
                    return L.T("Windows non concede il riavvio all'account di questo PC (utente senza diritti di arresto o criterio dell'organizzazione). Con il servizio «accesso completo» il riavvio funziona sempre: installalo da Tasti ▾.");
                }
                if (OperatingSystem.IsMacOS()) return Run("osascript", "-e \"tell application \\\"System Events\\\" to restart\"");
                if (OperatingSystem.IsLinux()) return Run("systemctl", "reboot");
                return "Il riavvio da remoto non è possibile su questo sistema.";
            }
            catch (Exception ex) { return "Riavvio non riuscito: " + ex.Message; }
        }

        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES p, int len, IntPtr prev, IntPtr retLen);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ExitWindowsEx(uint flags, uint reason);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        /// <summary>Riavvio con ExitWindowsEx (EWX_REBOOT | EWX_FORCEIFHUNG). "" = avviato.</summary>
        private static string WinReboot()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* ADJUST_PRIVILEGES | QUERY */, out IntPtr tok)) return "token non apribile";
            try
            {
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid)) return "privilegio di arresto sconosciuto";
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 /* SE_PRIVILEGE_ENABLED */ };
                AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                if (Marshal.GetLastWin32Error() == 1300) return "l'account non ha il privilegio di arresto (ERROR_NOT_ALL_ASSIGNED)";
                return ExitWindowsEx(0x02 | 0x10, 0x80000000 | 0x00040000 /* pianificato, manutenzione */) ? "" : "errore " + Marshal.GetLastWin32Error();
            }
            finally { CloseHandle(tok); }
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
