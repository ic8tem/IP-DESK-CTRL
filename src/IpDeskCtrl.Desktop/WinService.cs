using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
    /// Tiene la rete del PC (vedi <see cref="ServiceNet"/>: numero, porta, MQTT, collegamenti in arrivo, anche
    /// prima dell'accesso) e risponde sulla pipe locale al programma IP-DESK-CTRL degli utenti:
    ///   HELPER     → avvia il processo di supporto come SYSTEM nella sessione di chi chiede (vede UAC e blocco);
    ///   SAS        → Ctrl+Alt+Canc (SendSAS, permesso solo ai servizi);
    ///   IDENTITY   → chiave PUBBLICA del PC («NEED» se il servizio non ne ha ancora una); SETKEY → la riceve
    ///                (una volta sola); SIGN → firma per conto del programma (la chiave privata non esce mai);
    ///   ATTACH     → collegamento permanente del programma: domande di consenso, sessioni in arrivo;
    ///   REBOOT     → riavvio del PC (chiesto da un visore autorizzato).
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
            _ = Task.Run(() =>
            {
                try { ServiceNet.Start(_cts.Token); }
                catch (Exception ex) { ErrorLog.Write("Servizio, avvio della rete: " + ex); }
            });
            new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "pipe del servizio" }.Start();
            _ = Task.Run(AfterUpdate);
        }

        private static string MarkerPath => Path.Combine(Platform.DataFolder ?? AppSettings.Folder, "aggiornamento.marker");

        /// <summary>
        /// Aggiornamento fatto dal servizio: il nuovo servizio riapre IP-DESK-CTRL nella sessione dell'utente che
        /// l'aveva chiesto (con --dopo-aggiornamento, così riprende i collegamenti).
        /// </summary>
        private void AfterUpdate()
        {
            try
            {
                if (!File.Exists(MarkerPath)) return;
                var p = File.ReadAllText(MarkerPath).Split('|');
                File.Delete(MarkerPath);
                if (p.Length < 2 || !uint.TryParse(p[0], out uint session) || !long.TryParse(p[1], out long ticks)) return;
                if (DateTime.UtcNow.Ticks - ticks > TimeSpan.FromMinutes(15).Ticks) return;
                Thread.Sleep(3000);
                string exe = Environment.ProcessPath!;
                Launch.AsUserInSession(session, $"\"{exe}\" {Updater.AfterUpdateArg}");
                SessionLog.Write($"SERVIZIO aggiornato a {AppVersion.Text}: IP-DESK-CTRL riaperto nella sessione {session}");
            }
            catch (Exception ex) { ErrorLog.Write("Servizio, riapertura dopo l'aggiornamento: " + ex.Message); }
        }

        /// <summary>
        /// Installa un aggiornamento chiesto dal programma. Il file si copia nella cartella del servizio e si accetta
        /// solo se è un installatore della release di GitHub con la stessa impronta SHA-256 dichiarata da GitHub.
        /// </summary>
        private static string Update(string args, int pid)
        {
            int sp = args.IndexOf(' ');
            if (sp <= 0) return "ERR richiesta non valida";
            string sha = args[..sp].Trim().ToLowerInvariant(), file = args[(sp + 1)..].Trim();
            string name = Path.GetFileName(file);
            if (!name.StartsWith("IP-DESK-CTRL-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                return "ERR file non valido";
            string dir = Path.Combine(Platform.DataFolder ?? AppSettings.Folder, "aggiornamento");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, name);
            File.Copy(file, copy, true);
            string hex;
            using (var f = File.OpenRead(copy)) hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)).ToLowerInvariant();
            if (hex != sha) return "ERR impronta diversa";
            var rel = UpdateCheck.LatestAsync(CancellationToken.None).GetAwaiter().GetResult();
            var asset = rel?.Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (asset == null || !asset.Digest.Equals("sha256:" + hex, StringComparison.OrdinalIgnoreCase))
                return "ERR il file non è l'installatore pubblicato su GitHub";
            File.WriteAllText(MarkerPath, $"{SessionOf(pid)}|{DateTime.UtcNow.Ticks}");
            try { ServiceNet.PrepareUpdate(); Thread.Sleep(300); } catch { }
            Process.Start(new ProcessStartInfo(copy, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AGGIORNA-SERVIZIO") { UseShellExecute = false, WorkingDirectory = dir });
            SessionLog.Write($"SERVIZIO aggiornamento a {rel!.Version} avviato ({name})");
            return "OK";
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
                    server = NamedPipeServerStreamAcl.Create(WinHelper.ServicePipe, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, ps);
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
                        SessionLog.Write($"SERVIZIO richiesta «{(req.Length > 20 ? req[..20] + "…" : req)}» rifiutata: il processo {pid} non è IP-DESK-CTRL");
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
                        case "PING":
                            w.WriteLine($"OK {AppVersion.Protocol} {ServiceNet.Me?.Number ?? "-"}");
                            break;
                        case "IDENTITY":
                            // solo la chiave pubblica: la privata non esce mai dal servizio
                            w.WriteLine(ServiceNet.Me != null ? "OK " + Convert.ToHexString(ServiceNet.Me.PublicKey) : "NEED");
                            break;
                        case "ATTACH":
                            ServiceNet.ServeAttached(s, r, w, SessionOf(pid));
                            break;
                        case var g when g.StartsWith("SIGN "):
                        {
                            var me = ServiceNet.Me;
                            byte[] data;
                            try { data = Convert.FromHexString(g[5..].Trim()); } catch { w.WriteLine("ERR dati non validi"); break; }
                            if (me == null) w.WriteLine("ERR il servizio non ha ancora una chiave");
                            else if (data.Length == 0 || data.Length > 65536) w.WriteLine("ERR dimensione non valida");
                            else w.WriteLine("OK " + Convert.ToHexString(me.Sign(data)));
                            break;
                        }
                        case var u when u.StartsWith("UPDATE "):
                            w.WriteLine(Update(u[7..], pid));
                            break;
                        case var k when k.StartsWith("SETKEY "):
                            w.WriteLine(ServiceNet.SetKey(k[7..].Trim()));
                            break;
                        default:
                            if (!ServiceCommands.Handle(req.Split(' '), w.WriteLine)) w.WriteLine("ERR richiesta sconosciuta");
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

    /// <summary>Comandi del servizio che non riguardano la rete (riavvio del PC…). true = gestito.</summary>
    [SupportedOSPlatform("windows")]
    internal static class ServiceCommands
    {
        public static bool Handle(string[] p, Action<string> reply)
        {
            switch (p[0])
            {
                case "REBOOT":
                {
                    string err = RebootNow();
                    reply(err.Length == 0 ? "OK" : "ERR " + err);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Riavvio fatto dal servizio (ha sempre il diritto di spegnere il PC).</summary>
        public static string RebootNow()
        {
            SessionLog.Write("SERVIZIO riavvio del PC chiesto da remoto");
            return Power.Run("shutdown", "/r /f /t 5 /c \"IP-DESK-CTRL: riavvio chiesto da remoto\"");
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
        [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint session, out IntPtr token);
        [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr env);

        /// <summary>Avvia un programma come l'utente collegato a quella sessione (non come SYSTEM).</summary>
        public static void AsUserInSession(uint session, string commandLine)
        {
            IntPtr tok = IntPtr.Zero, env = IntPtr.Zero;
            // dopo l'aggiornamento l'utente potrebbe non essere ancora pronto: si riprova per un minuto
            for (int i = 0; i < 30 && !WTSQueryUserToken(session, out tok); i++) Thread.Sleep(2000);
            if (tok == IntPtr.Zero) throw new Win32Exception();
            try
            {
                CreateEnvironmentBlock(out env, tok, false);
                var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
                var cmd = new StringBuilder(commandLine);
                if (!CreateProcessAsUser(tok, null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x00000400 /* UNICODE_ENVIRONMENT */,
                        env, Path.GetDirectoryName(Environment.ProcessPath), ref si, out var pi))
                    throw new Win32Exception();
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
            finally
            {
                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
                CloseHandle(tok);
            }
        }

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
