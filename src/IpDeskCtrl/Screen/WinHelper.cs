using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;
using IpDeskCtrl.Input;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Windows, accesso «di sistema» (UAC, schermata di blocco, programmi da amministratore, Ctrl+Alt+Canc).
    ///
    ///   programma dell'utente ──pipe «IP-DESK-CTRL-servizio»──▶ servizio IP-DESK-CTRL (LocalSystem)
    ///        │                                                     │ avvia, con il proprio token di sistema,
    ///        │                                                     ▼ nella sessione dell'utente
    ///        └──────────pipe privata (solo quell'utente)────▶ processo di supporto «--helper»
    ///                                                        cattura e mouse/tastiera sul desktop attivo,
    ///                                                        compreso il desktop sicuro «Winlogon»
    ///
    /// Il servizio accetta solo richieste dallo stesso eseguibile installato; la pipe del supporto è aperta
    /// solo all'utente che l'ha chiesta e al sistema. Il supporto si chiude quando la sessione finisce.
    /// </summary>
    public static class WinHelper
    {
        public const string ServicePipe = "IP-DESK-CTRL-servizio";
        public const string ServiceName = "IPDeskCtrl";

        // comandi della pipe del supporto: [u8 comando][i32 lunghezza][dati] → risposta [u8 esito][i32 lunghezza][dati]
        public const byte CmdMonitors = 1, CmdCapture = 2, CmdCursor = 3,
                          CmdMove = 10, CmdButton = 11, CmdWheel = 12, CmdKey = 13, CmdText = 14,
                          CmdClipGet = 20, CmdClipSet = 21, CmdClipFiles = 22, CmdClipSetFiles = 23, CmdUserFolder = 24, CmdBye = 99;

        public static HelperClient? Current { get; private set; }

        /// <summary>Collegamento al processo di supporto (lo fa partire il servizio). null se il servizio non c'è.</summary>
        public static HelperClient? Start() => StartWith(() => AskService("HELPER"));

        /// <summary>request: chi fa partire il processo di supporto e risponde «OK nome-della-pipe».</summary>
        public static HelperClient? StartWith(Func<string> request)
        {
            if (!OperatingSystem.IsWindows()) return null;
            Current?.Dispose();
            Current = null;
            try
            {
                string reply = request();
                if (!reply.StartsWith("OK ")) { SessionLog.Write("AIUTO il servizio risponde: " + reply); return null; }
                string pipe = reply[3..].Trim();
                var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
                c.Connect(15000);
                Current = new HelperClient(c);
                SessionLog.Write("AIUTO processo di sistema collegato: UAC, schermata di blocco e programmi da amministratore sono raggiungibili");
                return Current;
            }
            catch (TimeoutException) { SessionLog.Write("AIUTO servizio non installato o non avviato: accesso normale (niente UAC né schermata di blocco)"); }
            catch (Exception ex) { SessionLog.Write("AIUTO non disponibile: " + ex.Message); }
            return null;
        }

        public static void Stop()
        {
            Current?.Dispose();
            Current = null;
        }

        /// <summary>Chiede al servizio Ctrl+Alt+Canc (SendSAS: solo un servizio di sistema può farlo).</summary>
        public static void SecureAttention()
        {
            try { SessionLog.Write("AIUTO Ctrl+Alt+Canc: " + AskService("SAS")); } catch (Exception ex) { SessionLog.Write("AIUTO Ctrl+Alt+Canc non riuscito: " + ex.Message); }
        }

        /// <summary>Una richiesta di una riga al servizio, una riga di risposta.</summary>
        public static string AskService(string request, int timeoutMs = 1500)
        {
            using var svc = new NamedPipeClientStream(".", ServicePipe, PipeDirection.InOut, PipeOptions.None);
            svc.Connect(timeoutMs);
            var w = new StreamWriter(svc, new UTF8Encoding(false)) { AutoFlush = true };
            var r = new StreamReader(svc, Encoding.UTF8);
            w.WriteLine(request);
            return r.ReadLine() ?? "";
        }

        /// <summary>Sul PC: con il servizio, schermo e input passano dal processo di sistema; senza, quelli normali.</summary>
        public static void InstallHooks()
        {
            if (!OperatingSystem.IsWindows()) return;
            Platform.PrepareHost = () => Task.Run(() => { Start(); return ""; });
            Platform.HostEnded = Stop;
            Platform.Screen = () => Current != null ? new HelperScreen(Current) : new WindowsScreen();
            Platform.Input = () => Current != null ? new HelperInput(Current) : new WindowsInput();
        }

        /// <summary>
        /// Nel servizio (rete del servizio, PC raggiungibile anche prima dell'accesso): il processo di supporto
        /// lo avvia direttamente il servizio nella sessione della console attiva, anche sulla schermata di accesso.
        /// </summary>
        public static void InstallServiceHooks(Func<string> launchHelper)
        {
            if (!OperatingSystem.IsWindows()) return;
            Platform.PrepareHost = () => Task.Run(() =>
            {
                StartWith(launchHelper);
                // i file ricevuti vanno in Scaricati/IP-DESK-CTRL dell'utente (non nel profilo di sistema del servizio)
                var hc = Current;
                if (hc != null)
                {
                    var (st, data) = hc.Ask(CmdUserFolder);
                    if (st == 0 && data.Length > 0) Platform.ReceiveFolder = Encoding.UTF8.GetString(data);
                }
                return Current == null ? "Il processo di sistema non è partito: lo schermo del PC remoto potrebbe non essere visibile." : "";
            });
            Platform.HostEnded = Stop;
            Platform.Screen = () => Current != null ? new HelperScreen(Current) : new WindowsScreen();
            Platform.Input = () => Current != null ? new HelperInput(Current) : new WindowsInput();
            // il servizio gira nella sessione 0, che non ha gli appunti dell'utente: passano dal processo di supporto
            Platform.Clipboard = new HelperClipboard();
        }

        // ================================================================== processo di supporto

        /// <summary>
        /// Corpo del processo «--helper &lt;pipe&gt; &lt;SID dell'utente&gt;», avviato dal servizio come SYSTEM nella
        /// sessione dell'utente. Tutto gira su un solo thread, che segue il desktop attivo.
        /// </summary>
        public static int RunHelper(string pipe, string userSid)
        {
            Platform.DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IP-DESK-CTRL");
            try
            {
                var ps = new PipeSecurity();
                ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(userSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                using var server = NamedPipeServerStreamAcl.Create(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 1 << 20, 1 << 20, ps);
                var wait = Task.Run(() => server.WaitForConnection());
                if (!wait.Wait(20000)) return 2;
                if (!SameExecutable(ClientPid(server))) { SessionLog.Write("AIUTO collegamento rifiutato: non viene da IP-DESK-CTRL"); return 3; }

                var screen = new WindowsScreen { FollowInputDesktop = true };
                var input = new WindowsInput();
                var frame = new Frame();
                var br = new BinaryReader(server);
                var bw = new BinaryWriter(server);
                while (true)
                {
                    byte cmd = br.ReadByte();
                    int len = br.ReadInt32();
                    byte[] data = len > 0 ? br.ReadBytes(len) : Array.Empty<byte>();
                    Win32.FollowInputDesktop();
                    var r = new BinaryReader(new MemoryStream(data));
                    switch (cmd)
                    {
                        case CmdMonitors:
                        {
                            var ms = new MemoryStream();
                            var w = new BinaryWriter(ms);
                            var list = screen.Monitors();
                            w.Write(list.Count);
                            foreach (var m in list) { w.Write(m.Name); w.Write(m.X); w.Write(m.Y); w.Write(m.W); w.Write(m.H); w.Write(m.Primary); }
                            Reply(bw, 0, ms.ToArray());
                            break;
                        }
                        case CmdCapture:
                        {
                            var area = new MonitorInfo(r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), false);
                            if (screen.Capture(area, frame))
                            {
                                bw.Write((byte)0);
                                bw.Write(8 + frame.Data.Length);
                                bw.Write(frame.Width); bw.Write(frame.Height);
                                bw.Write(frame.Data);
                                bw.Flush();
                            }
                            else Reply(bw, 1, Encoding.UTF8.GetBytes(screen.Note));
                            break;
                        }
                        case CmdCursor:
                        {
                            var c = screen.Cursor();
                            var ms = new MemoryStream();
                            var w = new BinaryWriter(ms);
                            w.Write(c != null); w.Write(c?.x ?? 0); w.Write(c?.y ?? 0);
                            Reply(bw, 0, ms.ToArray());
                            break;
                        }
                        case CmdMove: input.MoveTo(r.ReadInt32(), r.ReadInt32()); break;
                        case CmdButton: input.Button(r.ReadInt32(), r.ReadBoolean()); break;
                        case CmdWheel: input.Wheel(r.ReadInt32(), r.ReadInt32()); break;
                        case CmdKey: input.Key(r.ReadInt32(), r.ReadBoolean()); break;
                        case CmdText: input.Text(r.ReadString()); break;
                        case CmdClipGet:
                        {
                            string? t = WinClip.GetText();
                            if (t == null) Reply(bw, 1, Array.Empty<byte>());
                            else Reply(bw, 0, Encoding.UTF8.GetBytes(t));
                            break;
                        }
                        case CmdClipSet:
                            Reply(bw, WinClip.SetText(r.ReadString()) ? (byte)0 : (byte)1, Array.Empty<byte>());
                            break;
                        case CmdClipFiles:
                        {
                            var files = WinClip.GetFiles();
                            var ms = new MemoryStream();
                            var w = new BinaryWriter(ms);
                            w.Write(files.Count);
                            foreach (var f in files) w.Write(f);
                            Reply(bw, 0, ms.ToArray());
                            break;
                        }
                        case CmdClipSetFiles:
                        {
                            int n = r.ReadInt32();
                            var list = new List<string>();
                            for (int i = 0; i < n; i++) list.Add(r.ReadString());
                            Reply(bw, WinClip.SetFiles(list) ? (byte)0 : (byte)1, Array.Empty<byte>());
                            break;
                        }
                        case CmdUserFolder:
                        {
                            string f = UserDownloads(userSid);
                            Reply(bw, f.Length > 0 ? (byte)0 : (byte)1, Encoding.UTF8.GetBytes(f));
                            break;
                        }
                        case CmdBye: return 0;
                    }
                }
            }
            catch (EndOfStreamException) { return 0; }
            catch (IOException) { return 0; }
            catch (Exception ex)
            {
                ErrorLog.Write("Processo di supporto: " + ex);
                return 1;
            }
        }

        /// <summary>Scaricati/IP-DESK-CTRL dell'utente (dal suo profilo nel registro: il supporto gira come SYSTEM).</summary>
        private static string UserDownloads(string sid)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
                string? profile = k?.GetValue("ProfileImagePath") as string;
                if (string.IsNullOrEmpty(profile)) return "";
                return Path.Combine(Environment.ExpandEnvironmentVariables(profile), "Downloads", "IP-DESK-CTRL");
            }
            catch { return ""; }
        }

        private static void Reply(BinaryWriter bw, byte status, byte[] data)
        {
            bw.Write(status);
            bw.Write(data.Length);
            bw.Write(data);
            bw.Flush();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);

        public static int ClientPid(NamedPipeServerStream s) => GetNamedPipeClientProcessId(s.SafePipeHandle, out uint pid) ? (int)pid : -1;

        /// <summary>Il processo dall'altra parte della pipe è questo stesso eseguibile?</summary>
        public static bool SameExecutable(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using var p = Process.GetProcessById(pid);
                string? other = p.MainModule?.FileName;
                string? mine = Environment.ProcessPath;
                return other != null && mine != null && string.Equals(Path.GetFullPath(other), Path.GetFullPath(mine), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Appunti di Windows (testo) con le funzioni del sistema, per il processo di supporto: gira come SYSTEM
    /// nella sessione dell'utente, quindi vede gli stessi appunti dei suoi programmi.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WinClip
    {
        private const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 2;
        private static IntPtr _wnd;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);

        // Il thread principale del processo di supporto cambia desktop (UAC, schermata di blocco) e non può avere
        // finestre: gli appunti si usano da un thread a parte, che possiede la finestra invisibile.
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
        private static Thread? _thread;

        private static T OnClipThread<T>(Func<T> f)
        {
            if (_thread == null)
            {
                _thread = new Thread(() => { foreach (var a in _work.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "appunti" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            T result = default!;
            var done = new ManualResetEventSlim();     // non si chiude: il lavoro può finire dopo l'attesa
            _work.Add(() => { try { result = f(); } catch { } finally { done.Set(); } });
            done.Wait(3000);
            return result;
        }

        public static string? GetText() => OnClipThread(GetTextHere);
        public static List<string> GetFiles() => OnClipThread(GetFilesHere) ?? new List<string>();
        public static bool SetFiles(List<string> paths) => OnClipThread(() => SetFilesHere(paths));

        private const uint CF_HDROP = 15;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder? file, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);

        private static List<string> GetFilesHere()
        {
            var list = new List<string>();
            if (!IsClipboardFormatAvailable(CF_HDROP) || !Open()) return list;
            try
            {
                IntPtr h = GetClipboardData(CF_HDROP);
                if (h == IntPtr.Zero) return list;
                uint n = DragQueryFileW(h, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < n; i++)
                {
                    uint len = DragQueryFileW(h, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFileW(h, i, sb, len + 1);
                    list.Add(sb.ToString());
                }
            }
            finally { CloseClipboard(); }
            return list;
        }

        /// <summary>File negli appunti come li mette Esplora file (CF_HDROP + «copia»): pronti per Ctrl+V.</summary>
        private static bool SetFilesHere(List<string> paths)
        {
            if (paths.Count == 0 || !Open()) return false;
            try
            {
                EmptyClipboard();
                string joined = string.Join("\0", paths) + "\0\0";
                int bytes = 20 + joined.Length * 2;
                IntPtr h = GlobalAlloc(GMEM_MOVEABLE | 0x40 /* GMEM_ZEROINIT */, (UIntPtr)bytes);
                if (h == IntPtr.Zero) return false;
                IntPtr p = GlobalLock(h);
                Marshal.WriteInt32(p, 0, 20);        // DROPFILES.pFiles: i nomi cominciano dopo la struttura
                Marshal.WriteInt32(p, 16, 1);        // DROPFILES.fWide: nomi in UTF-16
                Marshal.Copy(joined.ToCharArray(), 0, p + 20, joined.Length);
                GlobalUnlock(h);
                if (SetClipboardData(CF_HDROP, h) == IntPtr.Zero) { GlobalFree(h); return false; }
                uint effect = RegisterClipboardFormatW("Preferred DropEffect");
                IntPtr e = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
                if (e != IntPtr.Zero)
                {
                    IntPtr ep = GlobalLock(e);
                    Marshal.WriteInt32(ep, 1);       // DROPEFFECT_COPY
                    GlobalUnlock(e);
                    if (SetClipboardData(effect, e) == IntPtr.Zero) GlobalFree(e);
                }
                return true;
            }
            finally { CloseClipboard(); }
        }
        public static bool SetText(string text) => OnClipThread(() => SetTextHere(text));

        /// <summary>Finestra invisibile (solo messaggi): serve come proprietaria degli appunti per poterli scrivere.</summary>
        private static IntPtr Owner()
        {
            if (_wnd == IntPtr.Zero) _wnd = CreateWindowExW(0, "STATIC", "IP-DESK-CTRL appunti", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return _wnd;
        }

        private static bool Open()
        {
            var owner = Owner();
            for (int i = 0; i < 10; i++)
            {
                if (OpenClipboard(owner)) return true;
                Thread.Sleep(30);          // un altro programma li sta usando
            }
            return false;
        }

        private static string? GetTextHere()
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !Open()) return null;
            try
            {
                IntPtr h = GetClipboardData(CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(p); }
                finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }

        private static bool SetTextHere(string text)
        {
            if (!Open()) return false;
            try
            {
                EmptyClipboard();
                int bytes = (text.Length + 1) * 2;
                IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (h == IntPtr.Zero) return false;
                IntPtr p = GlobalLock(h);
                Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                Marshal.WriteInt16(p, text.Length * 2, 0);
                GlobalUnlock(h);
                if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero) { GlobalFree(h); return false; }
                return true;
            }
            finally { CloseClipboard(); }
        }
    }

    /// <summary>Servizio: appunti dell'utente attraverso il processo di supporto (solo testo).</summary>
    public sealed class HelperClipboard : IClipboardBridge
    {
        public Task<string?> GetTextAsync() => Task.Run(() =>
        {
            var c = WinHelper.Current;
            if (c == null) return (string?)null;
            var (st, data) = c.Ask(WinHelper.CmdClipGet);
            return st == 0 ? Encoding.UTF8.GetString(data) : null;
        });

        public Task SetTextAsync(string text) => Task.Run(() => { WinHelper.Current?.Ask(WinHelper.CmdClipSet, w => w.Write(text)); });

        public Task<List<string>> GetFilesAsync() => Task.Run(() =>
        {
            var list = new List<string>();
            var c = WinHelper.Current;
            if (c == null) return list;
            var (st, data) = c.Ask(WinHelper.CmdClipFiles);
            if (st != 0 || data.Length < 4) return list;
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) list.Add(r.ReadString());
            return list;
        });

        public Task<bool> SetFilesAsync(List<string> paths) => Task.Run(() =>
        {
            var c = WinHelper.Current;
            if (c == null || paths.Count == 0) return false;
            var (st, _) = c.Ask(WinHelper.CmdClipSetFiles, w => { w.Write(paths.Count); foreach (var p in paths) w.Write(p); });
            return st == 0;
        });
    }

    /// <summary>Lato programma dell'utente: parla con il processo di supporto, una richiesta alla volta.</summary>
    public sealed class HelperClient : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly BinaryReader _r;
        private readonly BinaryWriter _w;
        private readonly object _lock = new();
        public bool Alive { get; private set; } = true;

        public HelperClient(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _r = new BinaryReader(pipe);
            _w = new BinaryWriter(pipe);
        }

        private static byte[] Payload(Action<BinaryWriter> f)
        {
            var ms = new MemoryStream();
            f(new BinaryWriter(ms));
            return ms.ToArray();
        }

        public void Send(byte cmd, Action<BinaryWriter>? f = null)
        {
            if (!Alive) return;
            byte[] p = f == null ? Array.Empty<byte>() : Payload(f);
            lock (_lock)
            {
                try { _w.Write(cmd); _w.Write(p.Length); _w.Write(p); _w.Flush(); }
                catch { Alive = false; }
            }
        }

        /// <summary>Richiesta con risposta: (esito, dati). Esito 255 = processo di supporto perso.</summary>
        public (byte status, byte[] data) Ask(byte cmd, Action<BinaryWriter>? f = null)
        {
            if (!Alive) return (255, Array.Empty<byte>());
            byte[] p = f == null ? Array.Empty<byte>() : Payload(f);
            lock (_lock)
            {
                try
                {
                    _w.Write(cmd); _w.Write(p.Length); _w.Write(p); _w.Flush();
                    byte st = _r.ReadByte();
                    int len = _r.ReadInt32();
                    return (st, len > 0 ? _r.ReadBytes(len) : Array.Empty<byte>());
                }
                catch { Alive = false; return (255, Array.Empty<byte>()); }
            }
        }

        public void Dispose()
        {
            Send(WinHelper.CmdBye);
            Alive = false;
            try { _pipe.Dispose(); } catch { }
        }
    }

    /// <summary>Schermo attraverso il processo di supporto (vede anche UAC e la schermata di blocco).</summary>
    public sealed class HelperScreen : IScreenSource
    {
        private readonly HelperClient _c;
        private WindowsScreen? _fallback;
        public string Note { get; private set; } = "";

        public HelperScreen(HelperClient c) { _c = c; }

        private WindowsScreen Fallback()
        {
            Note = "Il processo di sistema di IP-DESK-CTRL si è chiuso: UAC e schermata di blocco non sono più visibili.";
            return _fallback ??= new WindowsScreen();
        }

        public List<MonitorInfo> Monitors()
        {
            if (!_c.Alive) return Fallback().Monitors();
            var (st, d) = _c.Ask(WinHelper.CmdMonitors);
            if (st != 0) return Fallback().Monitors();
            var r = new BinaryReader(new MemoryStream(d));
            int n = r.ReadInt32();
            var list = new List<MonitorInfo>();
            for (int i = 0; i < n; i++) list.Add(new MonitorInfo(r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean()));
            return list;
        }

        public bool Capture(MonitorInfo area, Frame into)
        {
            if (!_c.Alive) { var f = Fallback(); bool ok = f.Capture(area, into); if (f.Note.Length > 0) Note = f.Note; return ok; }
            var (st, d) = _c.Ask(WinHelper.CmdCapture, w => { w.Write(area.Name); w.Write(area.X); w.Write(area.Y); w.Write(area.W); w.Write(area.H); });
            if (st != 0)
            {
                Note = st == 255 ? "" : Encoding.UTF8.GetString(d);
                return false;
            }
            int width = BitConverter.ToInt32(d, 0), height = BitConverter.ToInt32(d, 4);
            into.Ensure(width, height);
            Buffer.BlockCopy(d, 8, into.Data, 0, Math.Min(into.Data.Length, d.Length - 8));
            Note = "";
            return true;
        }

        public (int x, int y)? Cursor()
        {
            if (!_c.Alive) return _fallback?.Cursor();
            var (st, d) = _c.Ask(WinHelper.CmdCursor);
            if (st != 0 || d.Length < 9) return null;
            return d[0] != 0 ? (BitConverter.ToInt32(d, 1), BitConverter.ToInt32(d, 5)) : null;
        }

        public void Dispose() => _fallback?.Dispose();
    }

    /// <summary>Mouse e tastiera attraverso il processo di supporto; Ctrl+Alt+Canc lo genera il servizio.</summary>
    public sealed class HelperInput : IInputSink
    {
        private readonly HelperClient _c;
        private bool _ctrl, _alt;
        public string Note => _c.Alive ? "" : "Il processo di sistema di IP-DESK-CTRL si è chiuso: mouse e tastiera non arrivano più.";

        public HelperInput(HelperClient c) { _c = c; }

        public void MoveTo(int x, int y) => _c.Send(WinHelper.CmdMove, w => { w.Write(x); w.Write(y); });
        public void Button(int button, bool down) => _c.Send(WinHelper.CmdButton, w => { w.Write(button); w.Write(down); });
        public void Wheel(int dx, int dy) => _c.Send(WinHelper.CmdWheel, w => { w.Write(dx); w.Write(dy); });

        public void Key(int evdev, bool down)
        {
            if (evdev == KeyMap.LeftCtrl || evdev == KeyMap.RightCtrl) _ctrl = down;
            if (evdev == KeyMap.LeftAlt || evdev == KeyMap.RightAlt) _alt = down;
            // Windows ignora il Ctrl+Alt+Canc simulato: lo chiede il servizio con SendSAS
            if (evdev == KeyMap.Delete && down && _ctrl && _alt) { WinHelper.SecureAttention(); return; }
            _c.Send(WinHelper.CmdKey, w => { w.Write(evdev); w.Write(down); });
        }

        public void Text(string text) => _c.Send(WinHelper.CmdText, w => w.Write(text));

        public void Dispose() { }
    }
}
