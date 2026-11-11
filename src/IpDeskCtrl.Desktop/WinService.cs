using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Desktop
{
    /// <summary>
    /// Servizio di Windows «IPDeskCtrl» (LocalSystem), installato dal setup se l'utente lo sceglie.
    /// Non tocca la rete: aspetta sulla pipe locale le richieste del programma IP-DESK-CTRL dell'utente e
    ///   HELPER → avvia il processo di supporto come SYSTEM nella sessione di chi chiede (vede UAC e blocco);
    ///   SAS    → Ctrl+Alt+Canc (SendSAS, permesso solo ai servizi).
    /// Accetta solo richieste dallo stesso eseguibile installato.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WinService : ServiceBase
    {
        private readonly CancellationTokenSource _cts = new();

        public WinService() { ServiceName = WinHelper.ServiceName; CanStop = true; }

        public static void RunAsService()
        {
            Platform.DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IP-DESK-CTRL");
            Run(new WinService());
        }

        protected override void OnStart(string[] args)
        {
            SessionLog.Write($"SERVIZIO avviato ({AppVersion.Text})");
            new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "pipe del servizio" }.Start();
        }

        protected override void OnStop()
        {
            _cts.Cancel();
            SessionLog.Write("SERVIZIO fermato");
        }

        private void Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    var ps = new PipeSecurity();
                    ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    server = NamedPipeServerStreamAcl.Create(WinHelper.ServicePipe, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, ps);
                    server.WaitForConnectionAsync(ct).Wait(ct);
                    var s = server;
                    server = null;
                    _ = Task.Run(() => Serve(s));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    ErrorLog.Write("Servizio, pipe: " + ex);
                    Thread.Sleep(1000);
                }
                finally { server?.Dispose(); }
            }
        }

        private static void Serve(NamedPipeServerStream s)
        {
            using (s)
            {
                try
                {
                    var r = new StreamReader(s, Encoding.UTF8);
                    var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true };
                    string req = (r.ReadLine() ?? "").Trim();
                    int pid = WinHelper.ClientPid(s);
                    if (!WinHelper.SameExecutable(pid))
                    {
                        SessionLog.Write($"SERVIZIO richiesta «{req}» rifiutata: il processo {pid} non è IP-DESK-CTRL");
                        w.WriteLine("ERR solo IP-DESK-CTRL può usare il servizio");
                        return;
                    }
                    switch (req)
                    {
                        case "HELPER":
                        {
                            string pipe = "IP-DESK-CTRL-aiuto-" + Guid.NewGuid().ToString("N");
                            string sid = UserSid(pid);
                            uint session = SessionOf(pid);
                            string exe = Environment.ProcessPath!;
                            Launch.AsSystemInSession(session, $"\"{exe}\" --helper {pipe} {sid}");
                            SessionLog.Write($"SERVIZIO processo di supporto avviato nella sessione {session} per {sid}");
                            w.WriteLine("OK " + pipe);
                            break;
                        }
                        case "SAS":
                            SendSAS(false);
                            w.WriteLine("OK");
                            break;
                        default:
                            w.WriteLine("ERR richiesta sconosciuta");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Servizio, richiesta: " + ex);
                    try { new StreamWriter(s) { AutoFlush = true }.WriteLine("ERR " + ex.Message); } catch { }
                }
            }
        }

        [DllImport("sas.dll")] private static extern void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);
        [DllImport("kernel32.dll")] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private static uint SessionOf(int pid) => ProcessIdToSessionId((uint)pid, out uint s) ? s : throw new Win32Exception();

        private static string UserSid(int pid)
        {
            using var p = Process.GetProcessById(pid);
            if (!OpenProcessToken(p.Handle, 0x0008 /* TOKEN_QUERY */, out IntPtr tok)) throw new Win32Exception();
            try
            {
                using var id = new WindowsIdentity(tok);
                return id.User!.Value;
            }
            finally { CloseHandle(tok); }
        }
    }

    /// <summary>Avvio di un processo con il token di sistema del servizio, ma nella sessione dell'utente.</summary>
    [SupportedOSPlatform("windows")]
    internal static class Launch
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs, int impersonation, int type, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetTokenInformation(IntPtr token, int cls, ref uint value, int length);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(IntPtr token, string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
            uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        public static void AsSystemInSession(uint session, string commandLine)
        {
            const uint TOKEN_ALL = 0xF01FF;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL, out IntPtr tok)) throw new Win32Exception();
            IntPtr dup = IntPtr.Zero;
            try
            {
                if (!DuplicateTokenEx(tok, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, 2 /* SecurityImpersonation */, 1 /* TokenPrimary */, out dup))
                    throw new Win32Exception();
                if (!SetTokenInformation(dup, 12 /* TokenSessionId */, ref session, 4)) throw new Win32Exception();
                var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
                var cmd = new StringBuilder(commandLine);
                if (!CreateProcessAsUser(dup, null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x08000000 /* CREATE_NO_WINDOW */ | 0x00000400 /* UNICODE_ENVIRONMENT */,
                        IntPtr.Zero, Path.GetDirectoryName(Environment.ProcessPath), ref si, out var pi))
                    throw new Win32Exception();
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
            finally
            {
                if (dup != IntPtr.Zero) CloseHandle(dup);
                CloseHandle(tok);
            }
        }
    }
}
