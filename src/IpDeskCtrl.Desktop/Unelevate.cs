using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Desktop
{
    /// <summary>
    /// Il programma dell'utente non deve girare come amministratore: tutto quello che richiede i privilegi
    /// (UAC, schermata di blocco, programmi da amministratore, Ctrl+Alt+Canc) lo fa il servizio di Windows.
    /// Un programma avviato come amministratore, invece, non riceve i file trascinati da Esplora risorse (Windows lo
    /// impedisce tra livelli di privilegio diversi). Se ci si trova avviati così (clic su «Esegui come
    /// amministratore», riapertura da un installatore…), ci si riavvia come l'utente normale con il gettone di
    /// Esplora risorse e si esce.
    /// </summary>
    public static class Unelevate
    {
        public const string Arg = "--non-amministratore";

        /// <summary>true = è partita la copia senza privilegi: questa deve uscire.</summary>
        public static bool RelaunchIfElevated(string[] args)
        {
            if (!OperatingSystem.IsWindows() || args.Contains(Arg)) return false;
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                if (id.IsSystem || !new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return false;
                // senza il servizio (versione portatile) i privilegi servono al programma stesso per comandare UAC e
                // programmi da amministratore: lì chi lo avvia come amministratore lo fa apposta
                bool service;
                try { service = System.ServiceProcess.ServiceController.GetServices().Any(s => s.ServiceName == "IPDeskCtrl"); }
                catch { service = false; }
                if (!service) return false;

                IntPtr shell = GetShellWindow();
                if (shell == IntPtr.Zero) { SessionLog.Write("AVVIO come amministratore e senza Esplora risorse: resto così"); return false; }
                GetWindowThreadProcessId(shell, out uint pid);
                IntPtr proc = OpenProcess(0x0400 /* QUERY_INFORMATION */, false, pid);
                if (proc == IntPtr.Zero) throw new Win32Exception();
                IntPtr tok = IntPtr.Zero, dup = IntPtr.Zero;
                try
                {
                    if (!OpenProcessToken(proc, 0x0002 /* DUPLICATE */, out tok)) throw new Win32Exception();
                    if (!DuplicateTokenEx(tok, 0x018B /* ASSIGN_PRIMARY|DUPLICATE|QUERY|ADJUST_DEFAULT|ADJUST_SESSIONID */, IntPtr.Zero, 2, 1, out dup))
                        throw new Win32Exception();
                    string exe = Environment.ProcessPath!;
                    var cmd = new StringBuilder("\"" + exe + "\"");
                    foreach (var a in args) cmd.Append(' ').Append(a.Contains(' ') ? "\"" + a + "\"" : a);
                    cmd.Append(' ').Append(Arg);
                    var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                    if (!CreateProcessWithTokenW(dup, 0, null, cmd, 0x00000400 /* UNICODE_ENVIRONMENT */, IntPtr.Zero,
                            System.IO.Path.GetDirectoryName(exe), ref si, out var pi))
                        throw new Win32Exception();
                    CloseHandle(pi.hThread);
                    CloseHandle(pi.hProcess);
                    SessionLog.Write("AVVIO come amministratore: mi riavvio come utente normale (i privilegi li usa solo il servizio; così funziona anche il trascinamento dei file)");
                    return true;
                }
                finally
                {
                    if (dup != IntPtr.Zero) CloseHandle(dup);
                    if (tok != IntPtr.Zero) CloseHandle(tok);
                    CloseHandle(proc);
                }
            }
            catch (Exception ex)
            {
                SessionLog.Write("AVVIO come amministratore: riavvio come utente normale non riuscito (" + ex.Message + "), resto così");
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs, int impersonation, int type, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? app, StringBuilder cmd, int flags, IntPtr env,
            string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    }
}
