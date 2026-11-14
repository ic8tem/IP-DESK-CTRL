using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Desktop
{
    /// <summary>
    /// Rete nel servizio di Windows: il PC è raggiungibile anche prima che qualcuno acceda.
    ///   • identità: chiave in ProgramData protetta con DPAPI del computer (la prima volta arriva dal
    ///     programma dell'utente, così il numero resta quello di prima);
    ///   • porta, broker MQTT, richieste e collegamenti in arrivo: un <see cref="Peer"/> completo;
    ///   • schermo e input: processo di supporto avviato come SYSTEM nella sessione della console attiva
    ///     (anche sulla schermata di accesso);
    ///   • consenso: nessun utente collegato al servizio → entra solo chi è «senza conferma», gli altri
    ///     ricevono «nessuno al PC per accettare»; con un utente → la domanda compare nel suo programma.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ServiceNet
    {
        private static readonly object Lock = new();
        private static Peer? _peer;

        /// <summary>Aggiornamento dal servizio: il visore collegato (se c'è) viene avvisato e potrà rientrare.</summary>
        public static void PrepareUpdate() => _peer?.CurrentHost?.PrepareUpdate();
        private static AppSettings? _s;
        private static CancellationToken _life;
        private static readonly List<Attached> Users = new();

        public static Identity? Me { get; private set; }

        public static string HistoryPath => Path.Combine(AppSettings.Folder, "storico.json");

        /// <summary>All'avvio del servizio: parte subito se la chiave c'è già, altrimenti aspetta il programma.</summary>
        public static void Start(CancellationToken life)
        {
            _life = life;
            KeyStore.MachineScope = true;
            AppSettings.SharedHistoryPath = HistoryPath;
            PrepareSharedFiles();
            var me = Identity.LoadExisting();
            if (me == null) { SessionLog.Write("SERVIZIO nessuna chiave ancora: aspetto il programma IP-DESK-CTRL di un utente"); return; }
            Run(me);
        }

        /// <summary>Chiave passata dal programma dell'utente (solo se il servizio non ne ha ancora una).</summary>
        public static string SetKey(string hex)
        {
            lock (Lock)
            {
                if (Me != null) return "ERR il servizio ha già una chiave";
                Identity me;
                try { me = Identity.FromPkcs8(Convert.FromHexString(hex)); }
                catch { return "ERR chiave non valida"; }
                KeyStore.Save(me.ExportPkcs8());
                RestrictKeyFile();
                SessionLog.Write($"SERVIZIO chiave ricevuta dal programma: numero {Identity.Format(me.Number)}");
                Run(me);
                return "OK";
            }
        }

        private static void Run(Identity me)
        {
            lock (Lock)
            {
                if (Me != null) return;
                Me = me;
                _s = AppSettings.Load();
                Platform.ReceiveFolder = Path.Combine(AppSettings.Folder, "ricevuti");
                WinHelper.InstallServiceHooks(LaunchHelper);
                Platform.Reboot = ServiceCommands.RebootNow;
                Platform.LaunchTrx = LaunchTrx;
                _peer = new Peer(me, _s) { AskAccept = AskAsync };
                _peer.HostStarted += hs => Broadcast($"START {hs.PeerNumber} {(hs.InputAllowed ? 1 : 0)}");
                _peer.HostEnded += (hs, why) => Broadcast($"END {hs.PeerNumber} {why.Replace('\n', ' ')}");
                _peer.Start();
                _life.Register(() => { try { _peer.Dispose(); } catch { } });
                SessionLog.Write($"SERVIZIO rete avviata: numero {Identity.Format(me.Number)}, raggiungibile anche prima dell'accesso");
            }
        }

        // =================================================================== file condivisi

        /// <summary>Storico condiviso scrivibile da tutti gli utenti; chiave leggibile solo da sistema e amministratori.</summary>
        private static void PrepareSharedFiles()
        {
            try
            {
                if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "[]");
                var fi = new FileInfo(HistoryPath);
                var acl = fi.GetAccessControl();
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    FileSystemRights.Modify, AccessControlType.Allow));
                fi.SetAccessControl(acl);
            }
            catch (Exception ex) { ErrorLog.Write("Servizio, storico condiviso: " + ex.Message); }
            RestrictKeyFile();
        }

        private static void RestrictKeyFile()
        {
            try
            {
                if (!File.Exists(KeyStore.FilePath)) return;
                var acl = new FileSecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                new FileInfo(KeyStore.FilePath).SetAccessControl(acl);
            }
            catch (Exception ex) { ErrorLog.Write("Servizio, permessi della chiave: " + ex.Message); }
        }

        // =================================================================== processo di supporto

        [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();

        /// <summary>Avvia il supporto nella sessione della console attiva (utente o schermata di accesso).</summary>
        private static string LaunchHelper()
        {
            uint session = WTSGetActiveConsoleSessionId();
            if (session == 0xFFFFFFFF) return "ERR nessuna sessione sulla console";
            string pipe = "IP-DESK-CTRL-aiuto-" + Guid.NewGuid().ToString("N");
            Launch.AsSystemInSession(session, $"\"{Environment.ProcessPath}\" --helper {pipe} S-1-5-18");
            SessionLog.Write($"SERVIZIO processo di supporto avviato nella sessione {session} della console");
            return "OK " + pipe;
        }

        // =================================================================== utenti collegati al servizio

        private sealed class Attached
        {
            public uint Session;
            public StreamWriter W = null!;
            public readonly ConcurrentDictionary<string, TaskCompletionSource<IncomingDecision>> Asks = new();
            public void Write(string line) { lock (this) { try { W.WriteLine(line); } catch { } } }
        }

        private static void Broadcast(string line)
        {
            List<Attached> list;
            lock (Users) list = Users.ToList();
            foreach (var a in list) a.Write(line);
        }

        /// <summary>Programma dell'utente collegato con «ATTACH»: resta qui finché la pipe è aperta.</summary>
        public static void ServeAttached(NamedPipeServerStream s, StreamReader r, StreamWriter w, uint session)
        {
            var a = new Attached { Session = session, W = w };
            lock (Users) Users.Add(a);
            SessionLog.Write($"SERVIZIO programma dell'utente collegato (sessione {session})");
            try
            {
                a.Write("OK " + (Me?.Number ?? "-"));
                var h = _peer?.CurrentHost;
                if (h != null && h.IsActive) a.Write($"START {h.PeerNumber} {(h.InputAllowed ? 1 : 0)}");
                while (s.IsConnected)
                {
                    string? line = r.ReadLine();
                    if (line == null) break;
                    string[] p = line.Split(' ');
                    switch (p[0])
                    {
                        case "ANS" when p.Length >= 5:
                            if (a.Asks.TryRemove(p[1], out var tcs)) tcs.TrySetResult(new IncomingDecision(p[2] == "1", p[3] == "1", p[4] == "1"));
                            break;
                        case "SET" when p.Length >= 4 && _s != null:
                            _s.AcceptIncoming = p[1] == "1";
                            _s.AllowInput = p[2] == "1";
                            _s.ShareAudio = p[3] == "1";
                            _s.Save();
                            break;
                        case "PWD" when p.Length >= 2 && _s != null:
                            if (p[1] == "-") { _s.PasswordSalt = ""; _s.PasswordHash = ""; }
                            else if (p.Length >= 4 && int.TryParse(p[3], out int it))
                            {
                                _s.PasswordSalt = p[1]; _s.PasswordHash = p[2]; _s.PasswordIterations = it;
                            }
                            _s.Save();
                            SessionLog.Write("SERVIZIO password dell'accesso senza conferma " + (p[1] == "-" ? "tolta" : "aggiornata"));
                            break;
                        case "MQTT" when p.Length >= 2 && _peer != null:
                        {
                            var list = p[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                            if (!list.SequenceEqual(_peer.S.MqttBrokers)) _peer.SetBrokers(list);
                            break;
                        }
                        case "TRXDONE":
                            SessionLog.Write("SERVIZIO IP-TRX-CTRL nella sessione dell'utente: " + string.Join(' ', p.Skip(1)));
                            break;
                        case "KICK":
                            _peer?.CurrentHost?.Close("chiuso dall'utente dell'altro PC");
                            break;
                        case "INPUT" when p.Length >= 2:
                            _peer?.CurrentHost?.SetInputAllowed(p[1] == "1");
                            break;
                        default:
                            ServiceCommands.Handle(p, a.Write);
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (Exception ex) { ErrorLog.Write("Servizio, programma collegato: " + ex); }
            finally
            {
                lock (Users) Users.Remove(a);
                foreach (var t in a.Asks.Values) t.TrySetResult(new IncomingDecision(false, false, false, "nessuno al PC per accettare"));
                SessionLog.Write($"SERVIZIO programma dell'utente scollegato (sessione {session})");
            }
        }

        /// <summary>IP-TRX-CTRL va aperto nella sessione dell'utente alla console: lo fa il suo programma.</summary>
        private static string LaunchTrx()
        {
            uint console = WTSGetActiveConsoleSessionId();
            Attached? a;
            lock (Users) a = Users.FirstOrDefault(u => u.Session == console);
            if (a == null) return "Sul PC remoto non c'è un utente collegato: IP-TRX-CTRL si può avviare solo dopo l'accesso.";
            if (!TrxCtrl.Installed) return "IP-TRX-CTRL non è installato sul PC remoto.";
            a.Write("TRX");
            return "";
        }

        private static int _askId;

        /// <summary>Consenso: lo chiede al programma dell'utente seduto alla console; se non c'è, rifiuta.</summary>
        private static async Task<IncomingDecision> AskAsync(IncomingInfo info, CancellationToken ct)
        {
            uint console = WTSGetActiveConsoleSessionId();
            Attached? a;
            lock (Users) a = Users.FirstOrDefault(u => u.Session == console) ?? Users.FirstOrDefault();
            if (a == null)
            {
                SessionLog.Write($"SERVIZIO {Identity.Format(info.Number)} chiede di entrare ma nessuno è al PC: rifiutato");
                return new IncomingDecision(false, false, false, "nessuno al PC per accettare");
            }
            string id = Interlocked.Increment(ref _askId).ToString();
            var tcs = new TaskCompletionSource<IncomingDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            a.Asks[id] = tcs;
            using var reg = ct.Register(() =>
            {
                a.Write("CANCEL " + id);
                tcs.TrySetResult(new IncomingDecision(false, false, false));
            });
            a.Write($"ASK {id} {Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info)))}");
            try { return await tcs.Task; }
            finally { a.Asks.TryRemove(id, out _); }
        }
    }
}
