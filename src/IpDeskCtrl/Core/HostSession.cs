using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Audio;
using IpDeskCtrl.Input;
using IpDeskCtrl.Net;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Questo PC visto e comandato da un altro: chiede il consenso, poi manda schermo e audio e
    /// riceve mouse e tastiera.
    /// </summary>
    public sealed class HostSession : SessionBase
    {
        private readonly bool _busy;
        private volatile bool _accepted;
        private bool _allowInput;
        private readonly TaskCompletionSource<bool> _helloSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile TaskCompletionSource<byte[]>? _authReply;
        private enum PwResult { Ok, Cancel, Blocked }

        private IScreenSource? _screen;
        private IInputSink? _input;
        private readonly PressedTracker _pressed = new();
        private ISystemAudioCapture? _audio;
        private volatile bool _audioWanted;
        private uint _audioSeq;
        private readonly Adpcm.State _aL = new(), _aR = new();

        private List<MonitorInfo> _monitors = new();
        private volatile int _selected = -2;          // -2 = principale (ancora da scegliere), -1 = tutti
        private volatile MonitorInfo? _area;
        private volatile int _frameW = 1, _frameH = 1;
        private volatile string _quality = "auto";
        private volatile bool _fullNext = true;
        private int _autoLevel;
        private int _frameId, _lastAcked;
        private readonly Dictionary<int, long> _sentAt = new();
        private double _ackMs = 50;
        private string _lastNote = "";

        // 0.5: microfono del visore, risoluzione adattata, cartella condivisa
        public string PeerCaps { get; private set; } = "";
        private IAudioPlayer? _mic;
        private readonly object _micLock = new();
        private readonly DisplayResizer _resizer = new();
        private int _fitW, _fitH;
        private DriveClient? _drive;
        private DavServer? _dav;
        private string _davWhere = "";
        private TunnelHub? _tunnels;

        // video compresso
        private volatile string _codecWanted = "auto";
        private VideoEncoder? _video;
        private int _videoGen, _videoWritten, _videoAcked, _videoGenAcked;
        private volatile bool _videoRestart = true;
        private long _videoBlockedSince;
        private bool _videoOff;          // ffmpeg fallito: riquadri fino a nuova scelta
        private volatile int _viewW, _viewH;     // spazio della finestra del visore in pixel veri (0 = immagine intera)
        private bool _lanFast;                   // rete locale veloce: più fotogrammi al secondo
        private string _fitLogged = "";
        private double _fitScale = 1;
        private long _fitChangedAt;
        private volatile bool _viewerHidden;     // finestra del visore ridotta a icona: un fotogramma al secondo, qualità ferma
        private long _lastHiddenFrame;
        // schermo fermo: si cattura meno spesso (meno lavoro per la grafica nelle sessioni di ore)
        private long _lastChangeAt = Environment.TickCount64, _lastInputAt, _lastCaptureAt;
        private byte[] _prevRaw = Array.Empty<byte>();
        private int _prevW, _prevH, _idleLevel;

        /// <summary>
        /// Questo PC sta per aggiornarsi: il visore lo sa e si ricollega da solo; il suo numero rientra senza conferma
        /// (con lo stesso permesso) per 5 minuti.
        /// </summary>
        public void PrepareUpdate()
        {
            if (!_accepted) return;
            ResumeState.Grant(P.S, PeerNumber, _allowInput);
            if (AppVersion.Has(PeerCaps, "resume")) Send(new MsgWriter(Msg.Resume).U8(0).I32((int)ResumeState.Window.TotalSeconds).Build());
            SessionLog.Write($"AGGIORNAMENTI {Identity.Format(PeerNumber)} potrà rientrare senza conferma per 5 minuti");
        }

        public bool IsActive => _accepted && !IsClosed;
        public bool InputAllowed => _allowInput;
        public event Action? Activated;

        public HostSession(Peer p, SecureLink link, bool busy) : base(p, link) { _busy = busy; }

        /// <summary>Guarda soltanto mentre un altro comanda: niente mouse, tastiera, file, tunnel, microfono…</summary>
        public bool Observer { get; init; }
        public string ControllerNumber { get; init; } = "";
        /// <summary>Il nodo di questo PC (per sapere chi altro è collegato).</summary>
        public Peer Node => P;

        // il processo di supporto di Windows (UAC, schermata di blocco) serve finché c'è almeno un collegamento
        private static int _prepared;
        private bool _didPrepare;

        public void Run(CancellationToken life)
        {
            life.Register(() => Close(L.T("programma chiuso sull'altro PC")));
            _ = Task.Run(ReceiveLoopAsync);
            _ = Task.Run(StartAsync);
        }

        private async Task StartAsync()
        {
            try
            {
                await SendAsync(new MsgWriter(Msg.Hello).Str(AppVersion.Tag).Str(AppVersion.Os).Str(Environment.MachineName).Bool(TrxCtrl.Installed).Str(AppVersion.CapsNow()).Build());
                if (await Task.WhenAny(_helloSeen.Task, Task.Delay(15000, Cts.Token)) != _helloSeen.Task) { Close(L.T("l'altro PC non si è presentato")); return; }
                var (proto, _) = AppVersion.Parse(PeerVersion);
                if (proto != AppVersion.Protocol) { await Refuse(AcceptState.Rejected, L.F("versioni non compatibili ({0} / {1})", PeerVersion, AppVersion.Tag)); return; }
                if (!P.S.AcceptIncoming) { await Refuse(AcceptState.Disabled, L.T("su quel PC i collegamenti in arrivo sono disattivati")); return; }
                if (_busy) { await Refuse(AcceptState.Busy, L.T("quel PC è già comandato da un altro")); return; }

                var h = P.S.Find(PeerNumber);
                string keyHex = Convert.ToHexString(Link.PeerKey);
                bool known = h != null && h.PeerKey == keyHex;
                IncomingDecision? d = null;
                if (known && ResumeState.Take(P.S, PeerNumber, out bool resumeInput))
                {
                    d = new IncomingDecision(true, resumeInput, false);
                    SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} rientra senza conferma dopo un aggiornamento");
                }
                else if (known && h!.AllowUnattended)
                {
                    var pw = P.S.PasswordHash.Length > 0 ? await CheckPasswordAsync() : PwResult.Ok;
                    if (IsClosed) return;
                    if (pw == PwResult.Blocked)
                    {
                        int min = (int)Math.Ceiling((AccessPassword.BlockedFor(PeerNumber) ?? AccessPassword.BlockTime).TotalMinutes);
                        await Refuse(AcceptState.Rejected, L.F("troppe password sbagliate: riprova tra {0} minuti", min));
                        return;
                    }
                    if (pw == PwResult.Ok)
                    {
                        d = new IncomingDecision(true, P.S.AllowInput, true);
                        SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} entra senza conferma (accesso consentito in precedenza" +
                                         (P.S.PasswordHash.Length > 0 ? L.T(", password giusta)") : ")"));
                    }
                }
                if (d == null)
                {
                    await SendAsync(new MsgWriter(Msg.Accept).U8((byte)AcceptState.Waiting).Str("").Build());
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(Cts.Token);
                    t.CancelAfter(TimeSpan.FromSeconds(60));
                    try
                    {
                        d = await P.AskAsync(new IncomingInfo(PeerNumber, h?.Alias ?? "", PeerOs, PeerName, PeerVersion,
                            Crypto.Fingerprint(Link.PeerKey), known) { Observer = Observer, Controller = ControllerNumber }, t.Token);
                    }
                    catch (OperationCanceledException) { d = new IncomingDecision(false, false, false); }
                }
                if (IsClosed || d == null) return;
                if (!d.Accept) { await Refuse(AcceptState.Rejected, d.Why.Length > 0 ? d.Why : L.T("richiesta rifiutata")); return; }

                _allowInput = d.AllowInput;
                var entry = P.S.Touch(PeerNumber, false, keyHex, PeerOs);
                entry.Version = PeerVersion;
                if (d.AlwaysAllow && !entry.AllowUnattended) { entry.AllowUnattended = true; P.S.Save(); }
                _accepted = true;
                await SendAsync(new MsgWriter(Msg.Accept).U8((byte)AcceptState.Accepted).Str(_allowInput ? "" : L.T("solo visione")).Build());
                Activated?.Invoke();
                if (Observer) Note(L.F("Su questo PC c'è anche {0}: mouse e tastiera li usate insieme.", Identity.Format(ControllerNumber)));

                _didPrepare = true;
                if (Interlocked.Increment(ref _prepared) == 1 && Platform.PrepareHost != null)
                {
                    string why = await Platform.PrepareHost();
                    if (why.Length > 0) Note(why);
                }
                _screen = ScreenSources.Create();
                if (_allowInput) _input = InputSinks.Create();
                SendMonitors();
                var capture = new Thread(CaptureLoop) { IsBackground = true, Name = L.T("cattura schermo"), Priority = ThreadPriority.AboveNormal };
                capture.Start();
                _ = Task.Run(PingLoopAsync);
                _ = Ffmpeg.EncodersAsync();          // prova dei codificatori video in sottofondo (una volta sola)
                _ = Task.Run(MaintenanceLoopAsync);
                _ = Task.Run(() => SendAccess(true));
                _ = Task.Run(ClipboardLoopAsync);
                _ = Task.Run(ClipboardFilesLoopAsync);
                var ext = P.PublicIp != null && P.ExternalPort > 0 ? new IPEndPoint(P.PublicIp, P.ExternalPort) : null;
                await OfferUdpAsync(P.LocalPort, ext);
            }
            catch (Exception ex) when (!IsClosed)
            {
                ErrorLog.Write(ex.ToString());
                Close(L.T("errore sul PC comandato: ") + ex.Message);
            }
            catch { }
        }

        /// <summary>
        /// Password dell'accesso senza conferma: richiesta nel canale cifrato, il visore manda la chiave derivata,
        /// l'host la confronta con il verificatore salvato. Errori contati per
        /// numero (5 → 10 minuti di blocco). Annulla = si chiede a chi è davanti al PC.
        /// </summary>
        private async Task<PwResult> CheckPasswordAsync()
        {
            byte[] salt, stored;
            try { salt = Convert.FromHexString(P.S.PasswordSalt); stored = Convert.FromHexString(P.S.PasswordHash); }
            catch { return PwResult.Ok; }          // password salvata male: come se non ci fosse
            string msg = "";
            while (!IsClosed)
            {
                if (AccessPassword.BlockedFor(PeerNumber) != null) return PwResult.Blocked;
                byte[] nonce = RandomNumberGenerator.GetBytes(32);
                var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _authReply = tcs;
                await SendAsync(new MsgWriter(Msg.AuthChallenge).I32(salt.Length).Bytes(salt).I32(P.S.PasswordIterations)
                    .I32(nonce.Length).Bytes(nonce).Str(msg).Build());
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2), Cts.Token)) != tcs.Task) return PwResult.Cancel;
                byte[] proof = tcs.Task.Result;
                if (proof.Length == 0)
                {
                    SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} non ha messo la password: chiedo a chi è al PC");
                    return PwResult.Cancel;
                }
                if (AccessPassword.Check(proof, stored))
                {
                    AccessPassword.Ok(PeerNumber);
                    return PwResult.Ok;
                }
                int left = AccessPassword.Fail(PeerNumber);
                SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} password sbagliata (tentativi rimasti: {left})");
                if (left <= 0) return PwResult.Blocked;
                msg = left == 1 ? L.T("Password sbagliata: resta un solo tentativo.") : L.F("Password sbagliata: restano {0} tentativi.", left);
            }
            return PwResult.Cancel;
        }

        private async Task Refuse(AcceptState s, string why)
        {
            await SendAsync(new MsgWriter(Msg.Accept).U8((byte)s).Str(why).Build());
            await Task.Delay(300);
            Close(why);
        }

        private void SendMonitors()
        {
            if (_screen == null) return;
            _monitors = _screen.Monitors();
            if (_selected == -2 || _selected >= _monitors.Count)
                _selected = Math.Max(0, _monitors.FindIndex(m => m.Primary));
            var w = new MsgWriter(Msg.Monitors).I32(_monitors.Count);
            foreach (var m in _monitors) w.Str(m.Name).I32(m.X).I32(m.Y).I32(m.W).I32(m.H).Bool(m.Primary);
            Send(w.I32(_selected).Build());
            _area = _selected < 0 ? MonitorInfo.Union(_monitors) : _monitors[_selected];
            _fullNext = true;
            if (_fitW > 0 && _changingMode == 0) _ = Task.Run(() => FitResolution(_fitW, _fitH));
            SessionLog.Write("SCHERMO monitor: " + string.Join(" | ", _monitors.Select((m, i) => m.Label(i) + $" a {m.X},{m.Y}")) + $" · mostro {(_selected < 0 ? "tutti" : (_selected + 1).ToString())}");
        }

        // ------------------------------------------------------------------ messaggi dal visore

        protected override void Handle(MsgReader r)
        {
            switch (r.Type)
            {
                case Msg.Hello:
                    PeerVersion = r.Str(); PeerOs = r.Str(); PeerName = r.Str();
                    if (!r.AtEnd) r.Bool();
                    if (!r.AtEnd) PeerCaps = r.Str();
                    _helloSeen.TrySetResult(true);
                    break;
                case Msg.SetMic: if (_accepted) SetMic(r.Bool()); break;
                case Msg.Audio: if (_accepted) { r.U32(); PlayMic(r.Rest()); } break;
                case Msg.Resize:
                {
                    int w = r.I32(), h = r.I32();
                    if (_accepted) _ = Task.Run(() => RequestResize(w, h));
                    break;
                }
                case Msg.Recording:
                {
                    bool on = r.U8() == 1;
                    Recording = on;
                    SessionLog.Write(on ? $"REGISTRAZIONE: {Identity.Format(PeerNumber)} sta registrando questa sessione in un video"
                                        : $"REGISTRAZIONE: {Identity.Format(PeerNumber)} ha smesso di registrare");
                    P.RaiseRecording(this, on);
                    break;
                }
                case Msg.Draw:
                {
                    if (!_accepted) break;
                    byte op = r.U8();
                    int n = Math.Min((int)r.U16(), 2000);
                    var pts = new float[n * 2];
                    for (int i = 0; i < pts.Length; i++) pts[i] = r.U16() / 65535f;
                    var a = _area;
                    if (a == null) break;
                    if (op == DrawEvent.Clear) SessionLog.Write($"LAVAGNA {Identity.Format(PeerNumber)} cancella i disegni");
                    else if (op == DrawEvent.NewStroke && !_drawLogged) { _drawLogged = true; SessionLog.Write($"LAVAGNA {Identity.Format(PeerNumber)} disegna sullo schermo"); }
                    P.RaiseDraw(new DrawEvent(PeerNumber, op, pts, a.X, a.Y, a.W, a.H));
                    break;
                }
                case Msg.Chat:
                {
                    string text = r.Str();
                    if (_accepted && text.Length > 0)
                    {
                        if (text.Length > 4000) text = text[..4000];
                        SessionLog.Write($"CHAT da {Identity.Format(PeerNumber)}: {text}");
                        P.RaiseChat(this, text);
                    }
                    break;
                }
                case Msg.Volume:
                {
                    int a = r.U8();
                    if (!_accepted || !_allowInput) { Note(L.T("Hai solo la visione di questo PC: il volume non si può cambiare.")); break; }
                    _ = Task.Run(() => { string err = VolumeControl.Change(a); if (err.Length > 0) Note(err); });
                    break;
                }
                case Msg.DriveOpen:
                {
                    string name = r.Str(); bool ro = r.Bool();
                    if (_accepted) _ = Task.Run(() => OpenDrive(name, ro));
                    break;
                }
                case Msg.DriveClose: _ = Task.Run(() => CloseDrive("tolta dal visore")); break;
                case Msg.DriveResp: _drive?.OnResponse(r); break;
                case Msg.FsReq:
                {
                    int id = r.I32(); byte op = r.U8(); string path = r.Str(), path2 = r.Str(); long pos = r.I64();
                    byte[] data = r.Bytes(Math.Clamp(r.I32(), 0, 4 << 20));
                    bool ok = _accepted && _allowInput && !Platform.IsMobile;
                    _ = Task.Run(() =>
                    {
                        var (status, reply) = ok ? RemoteFiles.Handle(op, path, path2, pos, data) : (DriveOp.Forbidden, Array.Empty<byte>());
                        if (ok && op is DriveOp.Delete or DriveOp.Move or DriveOp.MkDir)
                            SessionLog.Write($"FILE {Identity.Format(PeerNumber)}: {(op == DriveOp.Delete ? "eliminato" : op == DriveOp.Move ? "rinominato" : "nuova cartella")} {path}" + (op == DriveOp.Move ? " → " + path2 : "") + (status == DriveOp.Ok ? "" : $" (non riuscito, {status})"));
                        Send(new MsgWriter(Msg.FsResp).I32(id).I32(status).I32(reply.Length).Bytes(reply).Build());
                    });
                    break;
                }
                case Msg.Resume:
                {
                    int who = r.U8();
                    if (who == 1 && _accepted)
                    {
                        // il visore si aggiorna: quando torna, rientra senza conferma con lo stesso permesso
                        ResumeState.Grant(P.S, PeerNumber, _allowInput);
                        SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} si aggiorna: potrà rientrare senza conferma per 5 minuti");
                    }
                    break;
                }
                case Msg.Visible:
                {
                    bool v = r.Bool();
                    if (v && _viewerHidden) { _fullNext = true; _videoRestart = true; }
                    _viewerHidden = !v;
                    break;
                }
                case Msg.SetCodec: _codecWanted = r.Str(); _videoOff = false; _videoRestart = true; _fullNext = true; break;
                case Msg.VideoAck:
                {
                    int gen = r.I32(), frames = r.I32();
                    if (gen == _videoGen) { _videoAcked = Math.Max(_videoAcked, frames); _videoGenAcked = gen; }
                    break;
                }
                case Msg.TunnelOpen:
                {
                    int id = r.I32(); string host = r.Str(); int port = r.I32();
                    if (_accepted) _ = Task.Run(() => OpenTunnelAsync(id, host, port));
                    break;
                }
                case Msg.TunnelData: { int id = r.I32(); _tunnels?.OnData(id, r.Rest()); break; }
                case Msg.TunnelClose: _tunnels?.OnClose(r.I32()); break;
                case Msg.SelectMonitor:
                    _selected = r.I32();
                    if (_selected < -1) _selected = 0;
                    SendMonitors();
                    break;
                case Msg.SetQuality: _quality = r.Str(); _fullNext = true; break;
                case Msg.ViewSize: _viewW = Math.Clamp(r.I32(), 0, 16384); _viewH = Math.Clamp(r.I32(), 0, 16384); break;
                case Msg.SetAudio: SetAudio(r.Bool()); break;
                case Msg.Refresh: _fullNext = true; break;
                case Msg.FileFetch: _ = Task.Run(SendClipboardFilesAsync); break;
                case Msg.Reboot: _ = Task.Run(RebootAsync); break;
                case Msg.InstallService: _ = Task.Run(InstallServiceAsync); break;
                case Msg.DragOut: _ = Task.Run(DragOutAsync); break;
                case Msg.RunAdmin: { string what = r.Str(); _ = Task.Run(() => RunAdmin(what)); break; }
                case Msg.StartTrx: _ = Task.Run(StartTrx); break;
                case Msg.AuthResponse: _authReply?.TrySetResult(r.Bytes(Math.Clamp(r.I32(), 0, 64))); break;
                case Msg.FrameAck:
                {
                    int id = r.I32();
                    lock (_sentAt)
                    {
                        if (_sentAt.Remove(id, out long t0)) _ackMs = _ackMs * 0.8 + (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency * 0.2;
                        _lastAcked = Math.Max(_lastAcked, id);
                    }
                    break;
                }
                default:
                    if (_accepted) HandleInput(r);
                    break;
            }
        }

        private void HandleInput(MsgReader r)
        {
            _lastInputAt = Environment.TickCount64;      // chi comanda sta lavorando: cattura subito a piena velocità
            var sink = _input;
            if (sink == null || !_allowInput) return;
            try
            {
                switch (r.Type)
                {
                    case Msg.MouseMove: { var (x, y) = Map(r.I32(), r.I32()); sink.MoveTo(x, y); _mouseX = x; _mouseY = y; break; }
                    case Msg.MouseButton:
                    {
                        int b = r.U8(); bool down = r.Bool();
                        var (x, y) = Map(r.I32(), r.I32());
                        sink.MoveTo(x, y);
                        _mouseX = x; _mouseY = y;
                        sink.Button(b, down);
                        _pressed.Button(b, down);
                        break;
                    }
                    case Msg.Wheel: sink.Wheel(r.I32(), r.I32()); break;
                    case Msg.Key:
                    {
                        int k = r.U16(); bool down = r.Bool(); sink.Key(k, down); _pressed.Key(k, down);
                        if (k is Input.KeyMap.LeftCtrl or Input.KeyMap.RightCtrl) _ctrlDown = down;
                        else if (down && _ctrlDown && k is 45 or 46 or 47) _ = Task.Run(() => LogClipKeyAsync(k));
                        break;
                    }
                    case Msg.ReleaseAll: _pressed.ReleaseAll(sink); break;
                    case Msg.Text: sink.Text(r.Str()); break;
                }
                if (sink.Note.Length > 0) Note(sink.Note);
            }
            catch (Exception ex) { Note(L.T("Mouse e tastiera non disponibili: ") + ex.Message); }
        }

        protected override bool FilesAllowed => _accepted && _allowInput;

        private void StartTrx()
        {
            if (!_accepted || !_allowInput) { Note(L.T("Hai solo la visione di questo PC: IP-TRX-CTRL non è stato avviato.")); return; }
            string err = Platform.LaunchTrx != null ? Platform.LaunchTrx() : TrxCtrl.Launch();
            SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} avvia IP-TRX-CTRL: " + (err.Length == 0 ? "avviato" : err));
            Note(err.Length == 0 ? L.T("IP-TRX-CTRL avviato sul PC remoto.") : err);
        }

        /// <summary>Riavvio chiesto dal visore: solo se può comandare questo PC.</summary>
        // ------------------------------------------------------------------ accesso completo di Windows

        private (int svc, bool uac, bool admin) _access = (-1, false, false);

        /// <summary>Stato del servizio e della UAC per la plancia del visore: si manda solo quando cambia.</summary>
        private void SendAccess(bool force = false)
        {
            if (!AppVersion.Has(PeerCaps, "access") || Platform.ServiceState == null) return;
            int svc;
            bool uac;
            try { svc = Platform.ServiceState(); uac = Platform.UacPending?.Invoke() == true; }
            catch { return; }
            bool admin = Platform.RunAdmin != null && P.S.AllowAdminRun && _allowInput;
            var now = (svc, uac, admin);
            if (!force && now == _access) return;
            if (uac && !_access.uac) SessionLog.Write("UAC richiesta di permesso aperta sul PC" + (svc == 1 ? "" : " (senza servizio: chi comanda non la vede)"));
            _access = now;
            Send(new MsgWriter(Msg.HostAccess).U8((byte)svc).U8((byte)(uac ? 1 : 0)).U8((byte)(admin ? 1 : 0)).Build());
        }

        private async Task InstallServiceAsync()
        {
            if (!_accepted || !_allowInput) { Note(L.T("Installazione del servizio rifiutata: hai solo la visione di questo PC.")); SendAccess(true); return; }
            if (Platform.InstallService == null) { Note(L.T("Il servizio non si può installare da qui.")); SendAccess(true); return; }
            SessionLog.Write($"SERVIZIO {Identity.Format(PeerNumber)} chiede di installare il servizio di Windows (accesso completo)");
            Note(L.T("Chi è al PC remoto deve rispondere Sì alla richiesta di Windows (UAC) per installare il servizio…"));
            string err = await Platform.InstallService();
            if (err.Length > 0)
            {
                Note(L.T("Servizio non installato: ") + err);
                SessionLog.Write("SERVIZIO non installato: " + err);
                SendAccess(true);
                return;
            }
            SessionLog.Write("SERVIZIO installato su richiesta di " + Identity.Format(PeerNumber) + ": il programma si riavvia per passargli il collegamento");
            Send(new MsgWriter(Msg.HostAccess).U8(1).U8(0).U8(0).Build());
            Note(L.T("Servizio installato: il programma si riavvia, il collegamento riprende da solo."));
            await Task.Delay(1500);
            Close(L.T("servizio installato, il programma si riavvia"));
            Platform.RestartApp?.Invoke();
        }

        private void RunAdmin(string what)
        {
            if (!_accepted || !_allowInput) { Note(L.T("Rifiutato: hai solo la visione di questo PC.")); return; }
            if (Platform.RunAdmin == null) { Note(L.T("«Avvia come amministratore» funziona solo con il servizio di Windows installato.")); return; }
            if (!P.S.AllowAdminRun) { Note(L.T("Chi è al PC remoto non ha permesso di avviare programmi come amministratore (Impostazioni).")); return; }
            SessionLog.Write($"AMMINISTRATORE {Identity.Format(PeerNumber)} avvia «{what}» come amministratore senza UAC");
            string err = Platform.RunAdmin(what);
            if (err.Length > 0) { Note(L.T("Non avviato: ") + err); SessionLog.Write("AMMINISTRATORE non avviato: " + err); }
            else Note(L.F("«{0}» avviato come amministratore.", what));
        }

        private async Task RebootAsync()
        {
            if (!_accepted || !_allowInput) { Note(L.T("Riavvio rifiutato: hai solo la visione di questo PC.")); return; }
            SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} chiede il riavvio del PC");
            string err = Power.Reboot();
            if (err.Length > 0) { Note(err); SessionLog.Write("SESSIONE " + err); return; }
            await Task.Delay(500);
            Close(L.T("riavvio del PC in corso"));
        }

        /// <summary>
        /// File trascinati sulla finestra del visore: sono negli appunti di questo PC; si incollano dove sono
        /// stati lasciati (clic nel punto e Ctrl+V, Cmd+V su macOS), come un trascinamento vero.
        /// </summary>
        protected override void OnFilesReceived(TransferPurpose purpose, string folder, List<string> topLevel, bool onClipboard)
        {
            string what = topLevel.Count == 1 ? L.T("1 elemento") : L.F("{0} elementi", topLevel.Count);
            // file messi negli appunti da noi: non vanno riannunciati al visore (tornerebbero indietro)
            if (onClipboard) _clipFiles = new List<string>(topLevel);
            if (purpose == TransferPurpose.PasteKeys && onClipboard && _input != null && _allowInput && !Platform.IsMobile)
            {
                try
                {
                    int mod = OperatingSystem.IsMacOS() ? Input.KeyMap.LeftMeta : Input.KeyMap.LeftCtrl;
                    _input.Key(mod, true); _input.Key(47, true); _input.Key(47, false); _input.Key(mod, false);
                    Note(L.F("{0} copiati sul PC remoto (in {1}) e incollati.", what, folder));
                    return;
                }
                catch { }
            }
            if (purpose == TransferPurpose.Drop && onClipboard && _input != null && _allowInput && !Platform.IsMobile)
            {
                try
                {
                    _input.Button(Input.MouseBtn.Left, true);
                    _input.Button(Input.MouseBtn.Left, false);
                    Thread.Sleep(150);
                    int mod = OperatingSystem.IsMacOS() ? Input.KeyMap.LeftMeta : Input.KeyMap.LeftCtrl;
                    _input.Key(mod, true); _input.Key(47, true); _input.Key(47, false); _input.Key(mod, false);
                    SessionLog.Write($"FILE trascinati da {Identity.Format(PeerNumber)}: clic dove sono stati lasciati e Ctrl+V");
                    Note(L.F("{0} copiati sul PC remoto (in {1}) e incollati dove li hai lasciati.", what, folder));
                    return;
                }
                catch (Exception ex) { SessionLog.Write("FILE trascinati: incolla non riuscito: " + ex.Message); }
            }
            else if (purpose == TransferPurpose.Drop)
                SessionLog.Write("FILE trascinati: non incollati " + (!onClipboard ? "(appunti non disponibili)" : _input == null ? "(mouse e tastiera non disponibili)" : !_allowInput ? "(mouse e tastiera non consentiti)" : ""));
            Note(L.F("{0} copiati sul PC remoto in {1}", what, folder) + (onClipboard ? L.T(": sono negli appunti, incollali con Ctrl+V dove vuoi.") : "."));
        }

        private List<string> _clipFiles = new();
        private bool _ctrlDown;

        /// <summary>
        /// Ctrl+C / Ctrl+X / Ctrl+V arrivati da chi comanda: nel registro cosa c'era negli appunti di questo PC
        /// prima e dopo, così si vede se la copia è avvenuta e se andrà verso il visore.
        /// </summary>
        private async Task LogClipKeyAsync(int k)
        {
            string what = k == 46 ? "Ctrl+C (copia)" : k == 45 ? "Ctrl+X (taglia)" : "Ctrl+V (incolla)";
            var clip = Platform.Clipboard;
            string extra = Platform.UacPending?.Invoke() == true ? " · c'è una richiesta UAC aperta" : "";
            if (clip == null) { ClipLog($"{PeerLabel} preme {what}: questo PC non ha appunti accessibili{extra}"); return; }
            async Task<(string? text, List<string> files, string err)> Snap()
            {
                try { return (await clip.GetTextAsync(), await clip.GetFilesAsync(), ""); }
                catch (Exception ex) { return (null, new List<string>(), ex.Message); }
            }
            static string Desc((string? text, List<string> files, string err) s) =>
                s.err.Length > 0 ? "non leggibili (" + s.err + ")"
                : s.files.Count > 0 ? $"{s.files.Count} file/cartelle (il primo: {System.IO.Path.GetFileName(s.files[0].TrimEnd('/', '\\'))})"
                : !string.IsNullOrEmpty(s.text) ? $"testo di {s.text.Length} caratteri"
                : "vuoti";
            var before = await Snap();
            if (k == 47)
            {
                ClipLog($"{PeerLabel} preme {what} sul PC: negli appunti ci sono {Desc(before)}{extra}");
                return;
            }
            ClipLog($"{PeerLabel} preme {what} sul PC: prima negli appunti {Desc(before)}{extra}");
            await Task.Delay(1500);
            var after = await Snap();
            bool changed = after.err.Length == 0 && (after.text != before.text || !SamePaths(after.files, before.files));
            if (!changed)
            {
                ClipLog($"dopo {what} gli appunti del PC non sono cambiati ({Desc(after)}): la finestra attiva non ha copiato niente " +
                        "(niente selezionato, finestra di un programma da amministratore senza il servizio, oppure schermata protetta di UAC o di blocco)");
                return;
            }
            string route = !FilesAllowed ? "non vanno a chi comanda: mouse e tastiera non consentiti"
                : after.files.Count > 0 ? "li annuncio a chi comanda, che li prende da solo fino a 2 GB"
                : "il testo va negli appunti di chi comanda entro un secondo";
            ClipLog($"dopo {what} negli appunti del PC ci sono {Desc(after)}: {route}");
        }

        /// <summary>Ogni 2 secondi: se negli appunti di questo PC ci sono file, lo dice al visore (che può prenderli).</summary>
        private async Task ClipboardFilesLoopAsync()
        {
            var clip = Platform.Clipboard;
            if (clip == null) return;
            while (!Cts.IsCancellationRequested)
            {
                try { await Task.Delay(2000, Cts.Token); } catch { break; }
                if (!FilesAllowed) continue;
                try
                {
                    var files = await clip.GetFilesAsync();
                    if (SamePaths(files, _clipFiles)) continue;
                    _clipFiles = files;
                    var items = files.Count > 0 ? FromPaths(files) : new List<TransferItem>();
                    string first = files.Count > 0 ? System.IO.Path.GetFileName(files[0].TrimEnd('/', '\\')) : "";
                    await SendAsync(new MsgWriter(Msg.FilesAvailable).I32(files.Count).I64(items.Sum(i => i.Size)).Str(first).Build());
                    if (files.Count > 0) ClipLog($"sul PC ci sono {files.Count} file/cartelle negli appunti ({first}, {items.Sum(i => i.Size) / 1024} kB): annunciati a {PeerLabel}");
                }
                catch (Exception ex) { ClipLog("file negli appunti del PC non leggibili: " + ex.Message, "files"); }
            }
        }

        internal static bool SamePaths(List<string> a, List<string> b) =>
            a.Count == b.Count && a.Select(Norm).OrderBy(x => x).SequenceEqual(b.Select(Norm).OrderBy(x => x));

        private static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

        private async Task SendClipboardFilesAsync()
        {
            if (!FilesAllowed) return;
            var clip = Platform.Clipboard;
            var files = clip == null ? new List<string>() : await clip.GetFilesAsync();
            if (files.Count == 0) { Note(L.T("Negli appunti del PC remoto non ci sono più file.")); return; }
            await SendFilesAsync(FromPaths(files), TransferPurpose.Fetch);
        }

        protected override void OnDatagram(byte[] m)
        {
            try { if (m.Length > 0 && m[0] == Msg.MouseMove && _accepted) HandleInput(new MsgReader(m)); } catch { }
        }

        /// <summary>Coordinate dell'immagine → coordinate del desktop.</summary>
        private (int x, int y) Map(int px, int py)
        {
            var a = _area;
            if (a == null) return (px, py);
            int x = a.X + (int)Math.Round(px * (double)a.W / Math.Max(1, _frameW));
            int y = a.Y + (int)Math.Round(py * (double)a.H / Math.Max(1, _frameH));
            return (Math.Clamp(x, a.X, a.X + a.W - 1), Math.Clamp(y, a.Y, a.Y + a.H - 1));
        }

        /// <summary>Messaggio di chat a chi comanda questo PC.</summary>
        public void SendChat(string text)
        {
            if (text.Length == 0 || IsClosed) return;
            SessionLog.Write($"CHAT a {Identity.Format(PeerNumber)}: {text}");
            Send(new MsgWriter(Msg.Chat).Str(text).Build());
        }

        private bool _drawLogged;
        private bool _hwNoted;
        private int _mouseX, _mouseY;

        /// <summary>
        /// Chi comanda ha trascinato qualcosa fuori dalla finestra del visore: sotto il puntatore si apre una finestrella
        /// che riceve il «lascia» di Esplora file, poi i file vanno a chi comanda (Scaricati e appunti).
        /// </summary>
        private async Task DragOutAsync()
        {
            var sink = _input;
            int x = _mouseX, y = _mouseY;
            if (!_accepted || !_allowInput || sink == null) return;
            if (Platform.CatchDrop == null)
            {
                sink.Button(Input.MouseBtn.Left, false); _pressed.Button(Input.MouseBtn.Left, false);
                SessionLog.Write("TRASCINAMENTO verso chi comanda non disponibile su questo PC");
                return;
            }
            SessionLog.Write($"TRASCINAMENTO {PeerLabel} trascina fuori dalla finestra (puntatore a {x},{y}): raccolgo cosa viene lasciato");
            var catching = Platform.CatchDrop(x, y);
            await Task.Delay(500);
            // il trascinamento di Esplora file segue il mouse: qualche passo sopra la finestrella, poi si lascia
            for (int i = 0; i < 8; i++) { sink.MoveTo(x + (i % 2) * 2, y + (i % 2) * 2); await Task.Delay(50); }
            sink.Button(Input.MouseBtn.Left, false);
            _pressed.Button(Input.MouseBtn.Left, false);
            List<string> paths;
            try { paths = await Task.WhenAny(catching, Task.Delay(2500)) == catching ? catching.Result : new List<string>(); } catch (Exception ex) { paths = new List<string>(); SessionLog.Write("TRASCINAMENTO raccolta non riuscita: " + ex.Message); }
            if (paths.Count == 0)
            {
                SessionLog.Write("TRASCINAMENTO lasciato senza file (non era un trascinamento di file, o il programma non li consegna)");
                return;
            }
            SessionLog.Write($"TRASCINAMENTO {paths.Count} file lasciati ({System.IO.Path.GetFileName(paths[0].TrimEnd('/', '\\'))}…): li mando a {PeerLabel}");
            await SendFilesAsync(FromPaths(paths), TransferPurpose.Fetch);
        }
        /// <summary>Chi comanda sta registrando la sessione (avviso per chi è davanti al PC).</summary>
        public bool Recording { get; private set; }
        private readonly Dictionary<string, long> _noteLogged = new();

        private void Note(string text)
        {
            if (text == _lastNote) return;
            _lastNote = text;
            // due avvisi che si alternano (PC bloccato / processo di sistema chiuso) finivano nel registro due volte al
            // secondo: ogni testo al massimo una volta al minuto
            long now = Environment.TickCount64;
            if (text.Length > 0 && (!_noteLogged.TryGetValue(text, out long last) || now - last > 60_000))
            {
                _noteLogged[text] = now;
                SessionLog.Write("AVVISO " + text);
            }
            Send(new MsgWriter(Msg.ScreenNote).Str(text).Build());
        }

        // contatori per il registro (una riga al minuto durante la sessione)
        private int _statFrames, _statFail, _statTiles;
        private long _statBytes;
        private string _lastCaptureNote = "";
        private long _statAt = Environment.TickCount64;

        // ------------------------------------------------------------------ schermo

        private void CaptureLoop()
        {
            var raw = new Frame();
            var half = new Frame();
            var enc = new TileEncoder();
            int lastCx = int.MinValue, lastCy = int.MinValue;
            long lastAutoChange = 0, goodSince = 0, blockedSince = 0;
            var sw = Stopwatch.StartNew();
            while (!Cts.IsCancellationRequested)
            {
                long t0 = sw.ElapsedMilliseconds;
                try
                {
                    // al massimo due fotogrammi in viaggio: niente code che crescono sulle linee lente
                    int inFlight;
                    lock (_sentAt) inFlight = _frameId - _lastAcked;
                    if (inFlight >= 2)
                    {
                        if (blockedSince == 0) blockedSince = t0;
                        if (t0 - blockedSince < 3000) { Thread.Sleep(5); continue; }
                        // nessuna conferma da 3 s: riparto da capo con un'immagine completa
                        lock (_sentAt) { _lastAcked = _frameId; _sentAt.Clear(); }
                        _fullNext = true;
                    }
                    blockedSince = 0;

                    // finestra del visore ridotta a icona: un fotogramma al secondo (banda e processore a riposo)
                    if ((_viewerHidden || Link.IsRelay) && t0 - _lastHiddenFrame < (_viewerHidden ? 1000 : 250)) { Thread.Sleep(20); continue; }
                    _lastHiddenFrame = t0;

                    var area = _area;
                    if (area == null || _screen == null) { Thread.Sleep(50); continue; }
                    int idleWait = IdleInterval();
                    if (idleWait > 0 && Environment.TickCount64 - _lastCaptureAt < idleWait && !_fullNext) { Thread.Sleep(10); continue; }
                    _lastCaptureAt = Environment.TickCount64;
                    if (!_screen.Capture(area, raw))
                    {
                        _statFail++;
                        if (_screen.Note.Length > 0) Note(_screen.Note);
                        Thread.Sleep(500);
                        continue;
                    }
                    if (_screen.Note.Length > 0) Note(_screen.Note);
                    else if (_lastNote.Length > 0 && _lastNote == _lastCaptureNote) Note("");
                    _lastCaptureNote = _screen.Note;

                    // qualità automatica: si guarda quanto ci mette un fotogramma ad arrivare
                    // (ferma mentre la finestra del visore non si vede: Windows rallenta i programmi ridotti a icona e
                    //  le conferme arrivano tardi anche se la linea è buona)
                    if (_quality == "auto" && !_viewerHidden)
                    {
                        long now = sw.ElapsedMilliseconds;
                        if (_ackMs > 450 && _autoLevel < 3 && now - lastAutoChange > 3000) { _autoLevel++; lastAutoChange = now; _fullNext = true; goodSince = now; }
                        else if (_ackMs < 150) { if (goodSince == 0) goodSince = now; if (_autoLevel > 0 && now - goodSince > 4000 && now - lastAutoChange > 4000) { _autoLevel--; lastAutoChange = now; goodSince = now; } }
                        else goodSince = 0;
                    }
                    if (FrameChanged(raw)) _lastChangeAt = Environment.TickCount64;
                    var (q, scale) = Quality.Of(_quality, _autoLevel);
                    Frame f = ScaleForViewer(raw, half, scale);
                    int interval = FrameInterval();

                    if (_fullNext && _video != null) _videoRestart = true;      // «Aggiorna»: nuovo fotogramma chiave
                    if (SendVideo(f, _quality))
                    {
                        _fullNext = false;
                        CursorMoved(area, f, ref lastCx, ref lastCy);
                        long elv = sw.ElapsedMilliseconds - t0;
                        Thread.Sleep((int)Math.Max(3, interval - elv));
                        continue;
                    }
                    bool full = _fullNext;
                    _fullNext = false;
                    if (f.Width != _frameW || f.Height != _frameH) { _frameW = f.Width; _frameH = f.Height; full = true; }
                    var tiles = enc.Encode(f, q, full);
                    long encMs = sw.ElapsedMilliseconds - t0;

                    if (tiles.Count > 0)
                    {
                        int id;
                        lock (_sentAt) { id = ++_frameId; _sentAt[id] = Stopwatch.GetTimestamp(); }
                        Link.SendAsync(new MsgWriter(Msg.FrameStart).I32(id).I32(f.Width).I32(f.Height).Bool(full).Build(), Cts.Token).Wait(Cts.Token);
                        foreach (var t in tiles)
                            Link.SendAsync(new MsgWriter(Msg.Tile).I32(id).I32(t.X).I32(t.Y).I32(t.W).I32(t.H).U8(t.Format).I32(t.Data.Length).Bytes(t.Data).Build(), Cts.Token).Wait(Cts.Token);
                        Link.SendAsync(new MsgWriter(Msg.FrameEnd).I32(id).I32((int)encMs).Build(), Cts.Token).Wait(Cts.Token);
                        _statFrames++; _statTiles += tiles.Count;
                        foreach (var t in tiles) _statBytes += t.Data.Length;
                        if (id == 1) SessionLog.Write($"SCHERMO primo fotogramma {f.Width}×{f.Height} ({area.Name} {area.W}×{area.H} a {area.X},{area.Y}) · {tiles.Count} riquadri · {encMs} ms");
                    }

                    CursorMoved(area, f, ref lastCx, ref lastCy);
                }
                catch (OperationCanceledException) { break; }
                catch (AggregateException) when (Cts.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    ErrorLog.Write("Cattura: " + ex);
                    Thread.Sleep(500);
                }
                // 25 fotogrammi al secondo al massimo (50-60 in rete locale; meno se cattura e codifica sono lente)
                long el = sw.ElapsedMilliseconds - t0;
                Thread.Sleep((int)Math.Max(3, FrameInterval() - el));
            }
        }

        private void CursorMoved(MonitorInfo area, Frame f, ref int lastCx, ref int lastCy)
        {
            int cx = lastCx, cy = lastCy;
            CursorUpdate(area, f, ref lastCx, ref lastCy);
            if (cx != lastCx || cy != lastCy) _lastChangeAt = Environment.TickCount64;
        }

        /// <summary>
        /// Schermo fermo e nessun comando da chi guarda: dopo 3 s si cattura 5 volte al secondo, dopo 30 s 2 volte.
        /// Basta un movimento del mouse (di chi comanda o sul PC), un tasto o un cambio dello schermo per tornare
        /// subito alla velocità piena. La cattura (BitBlt) fa lavorare la grafica anche con la codifica software:
        /// in sessioni di ore a schermo fermo era lavoro inutile (blocco del PC IW2DNI del 22/11 con x264).
        /// </summary>
        private int IdleInterval()
        {
            long idle = Environment.TickCount64 - Math.Max(_lastChangeAt, _lastInputAt);
            int level = idle > 30000 ? 2 : idle > 3000 ? 1 : 0;
            if (level != _idleLevel)
            {
                if (level == 2) SessionLog.Write("SCHERMO fermo da 30 s: cattura rallentata a 2 volte al secondo");
                else if (_idleLevel == 2 && level == 0) SessionLog.Write("SCHERMO di nuovo in movimento: cattura a piena velocità");
                _idleLevel = level;
            }
            return level switch { 2 => 500, 1 => 200, _ => 0 };
        }

        /// <summary>Confronta la cattura con la precedente (memcmp: meno di 1 ms per uno schermo Full HD).</summary>
        private bool FrameChanged(Frame raw)
        {
            int len = raw.Stride * raw.Height;
            if (len <= 0 || len > raw.Data.Length) return true;
            var cur = raw.Data.AsSpan(0, len);
            if (raw.Width == _prevW && raw.Height == _prevH && _prevRaw.Length == len && cur.SequenceEqual(_prevRaw)) return false;
            if (_prevRaw.Length != len) _prevRaw = new byte[len];
            cur.CopyTo(_prevRaw);
            _prevW = raw.Width; _prevH = raw.Height;
            return true;
        }

        /// <summary>
        /// Millisecondi tra un fotogramma e l'altro: 40 (25 al secondo) di solito; 16 (60) con «Rete locale»;
        /// 20 (50) in automatico quando il collegamento è diretto e il ping sta sotto i 12 ms (fino a 30 ms resta).
        /// </summary>
        private const int HwMinInterval = 33;
        private const int HwMaxKbps = 12000;

        private int FrameInterval()
        {
            int i;
            if (_quality == "rapida") i = 16;
            else if (_quality != "auto" || Link.IsRelay || _viewerHidden) { _lanFast = false; i = 40; }
            else
            {
                double rtt = RttMs;
                if (!double.IsNaN(rtt)) { if (rtt < 12) _lanFast = true; else if (rtt > 30) _lanFast = false; }
                i = _lanFast ? 20 : 40;
            }
            // codifica con la scheda video: al massimo 30 al secondo (i blocchi totali con hevc_qsv delle 0.5.2x)
            return _video is { Hardware: true } ? Math.Max(i, HwMinInterval) : i;
        }

        /// <summary>
        /// Rimpicciolisce l'immagine quando la finestra del visore è più piccola dello schermo (meno banda e meno
        /// lavoro per entrambi, stessa resa a video) o quando la qualità chiede metà risoluzione. Mai ingrandita;
        /// sotto il 20 % di risparmio non si tocca, così il testo resta nitido.
        /// </summary>
        private Frame ScaleForViewer(Frame raw, Frame tmp, double qualityScale)
        {
            double s = qualityScale;
            int vw = _viewW, vh = _viewH;
            if (vw >= 160 && vh >= 120 && raw.Width > 0 && raw.Height > 0)
                s = Math.Min(s, Math.Min((double)vw / raw.Width, (double)vh / raw.Height));
            s = Math.Floor(s * 32) / 32;                     // a passi: ridimensionando la finestra non cambia a ogni pixel
            // ogni cambio di misura riavvia il codificatore video: al massimo uno ogni 5 s (la scheda video non
            // va riaperta di continuo), salvo il ritorno all'immagine intera chiesto con lo zoom
            long now = Environment.TickCount64;
            if (s >= 0.8 && _fitScale >= 0.8) s = _fitScale;                                   // resta intera
            else if (s != _fitScale && s < 0.8 && now - _fitChangedAt < 5000) s = _fitScale;   // aspetta
            if (s != _fitScale) { _fitScale = s; _fitChangedAt = now; }
            string log = s < 0.8 ? $"{raw.Width}×{raw.Height} → {(int)(raw.Width * s)}×{(int)(raw.Height * s)}" : "";
            if (log != _fitLogged)
            {
                _fitLogged = log;
                SessionLog.Write(log.Length > 0 ? "SCHERMO adattato alla finestra di chi guarda: " + log : "SCHERMO a risoluzione piena");
            }
            if (s >= 0.8) return raw;
            if (Math.Abs(s - 0.5) < 0.001) { TileEncoder.Half(raw, tmp); return tmp; }
            TileEncoder.Resize(raw, tmp, Math.Max(2, (int)(raw.Width * s) & ~1), Math.Max(2, (int)(raw.Height * s) & ~1));
            return tmp;
        }

        /// <summary>Puntatore del PC comandato, in coordinate dell'immagine.</summary>
        private void CursorUpdate(MonitorInfo area, Frame f, ref int lastCx, ref int lastCy)
        {
            var c = _screen?.Cursor();
            if (c == null) return;
            int cx = (int)Math.Round((c.Value.x - area.X) * (double)f.Width / Math.Max(1, area.W));
            int cy = (int)Math.Round((c.Value.y - area.Y) * (double)f.Height / Math.Max(1, area.H));
            if (cx == lastCx && cy == lastCy) return;
            lastCx = cx; lastCy = cy;
            bool inside = cx >= 0 && cy >= 0 && cx < f.Width && cy < f.Height;
            Send(new MsgWriter(Msg.Cursor).I32(cx).I32(cy).Bool(inside).Build());
        }

        private async Task MaintenanceLoopAsync()
        {
            while (!Cts.IsCancellationRequested)
            {
                try { await Task.Delay(3000, Cts.Token); } catch { break; }
                CheckUdpAlive(TimeSpan.FromSeconds(6));
                SendAccess();
                if (Environment.TickCount64 - _statAt > 60000)
                {
                    _statAt = Environment.TickCount64;
                    long vb = Interlocked.Exchange(ref _statBytesVideo, 0);
                    SessionLog.Write($"SCHERMO ultimo minuto: {_statFrames} fotogrammi, " + (_video != null ? $"video {Ffmpeg.CodecNames[_video.Codec]} {vb / 1024} kB" : $"{_statTiles} riquadri, {_statBytes / 1024} kB") +
                                     L.F(", {0} catture non riuscite · ritardo {1:F0} ms · {2}", _statFail, _ackMs, Route));
                    _statFrames = 0; _statTiles = 0; _statBytes = 0; _statFail = 0;
                }
                // monitor collegati o staccati
                try
                {
                    var now = _screen?.Monitors();
                    if (now != null && (now.Count != _monitors.Count || !now.TrueForAll(m => _monitors.Contains(m)))) SendMonitors();
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------ audio

        private void SetAudio(bool on)
        {
            _audioWanted = on && P.S.ShareAudio;
            if (_audioWanted && _audio == null && _accepted)
            {
                var a = SystemAudio.CreateCapture();
                if (a.Start(OnAudioFrame)) { _audio = a; if (a.Note.Length > 0) Note(a.Note); }
                else { if (a.Note.Length > 0) Note(a.Note); a.Dispose(); }
            }
            else if (!_audioWanted && _audio != null)
            {
                _audio.Dispose();
                _audio = null;
            }
        }

        private void OnAudioFrame(short[] stereo)
        {
            if (!_audioWanted || IsClosed) return;
            byte[] enc = Adpcm.Encode(stereo, _aL, _aR);
            uint seq = ++_audioSeq;
            var udp = Udp;
            if (udp != null) udp.SendAudio(enc);
            // in TCP l'audio non può aspettare dietro ai fotogrammi: se la coda è lunga si salta
            else Send(new MsgWriter(Msg.Audio).U32(seq).Bytes(enc).Build());
        }

        protected override void OnClosing()
        {
            _accepted = false;
            if (_input != null) { _pressed.ReleaseAll(_input); _input.Dispose(); }
            _audio?.Dispose();
            lock (_micLock) { _mic?.Dispose(); _mic = null; }
            _resizer.Dispose();
            if (Platform.FitDisplay != null && _fitW > 0) try { Platform.FitDisplay(null, 0, 0); } catch { }
            CloseDrive(L.T("collegamento chiuso"));
            _tunnels?.CloseAll();
            _video?.Dispose();
            _video = null;
            _screen?.Dispose();
            if (_didPrepare && Interlocked.Decrement(ref _prepared) == 0) { try { Platform.HostEnded?.Invoke(); } catch { } }
            var h = P.S.Find(PeerNumber);
            if (h != null) { h.LastSeconds = Duration(DateTime.UtcNow - Started); P.S.Save(); }
        }

        public static string Duration(TimeSpan t) => t.TotalHours >= 1 ? L.F("{0} h {1} min", (int)t.TotalHours, t.Minutes) : t.TotalMinutes >= 1 ? L.F("{0} min", (int)t.TotalMinutes) : $"{t.Seconds} s";

        // ------------------------------------------------------------------ microfono del visore

        private void SetMic(bool on)
        {
            lock (_micLock)
            {
                if (!on || !P.S.AcceptMic)
                {
                    _mic?.Dispose();
                    _mic = null;
                    if (on) Note(L.T("Questo PC non accetta il microfono di chi si collega (Impostazioni)."));
                    return;
                }
                if (_mic != null) return;
                _mic = SystemAudio.CreateMicOutput(P.S.MicOutput);
                string dev = _mic.Device;
                SessionLog.Write($"AUDIO microfono di {Identity.Format(PeerNumber)} su " + (dev.Length > 0 ? dev : "uscita predefinita"));
                Note(_mic.Note.Length > 0 ? _mic.Note
                    : dev.Length > 0 ? L.F("La tua voce arriva sul PC remoto nel microfono virtuale «{0}»: sceglilo come microfono nei programmi di là.", dev)
                    : L.T("La tua voce si sente dalle casse del PC remoto. Per usarla come microfono nei programmi di là serve un cavo audio virtuale (VB-CABLE su Windows, BlackHole su Mac)."));
            }
        }

        private void PlayMic(byte[] adpcm)
        {
            var m = _mic;
            if (m == null) return;
            var pcm = Adpcm.Decode(adpcm);
            if (pcm.Length > 0) m.Play(pcm);
        }

        protected override void OnUdpAudio(uint seq, byte[] data) { if (_accepted) PlayMic(data); }

        // ------------------------------------------------------------------ risoluzione adattata alla finestra del visore

        private int _changingMode;

        private void RequestResize(int w, int h)
        {
            if (w > 0 && (!_allowInput || !P.S.AllowResize))
            {
                Note(!_allowInput ? L.T("Hai solo la visione di questo PC: la risoluzione resta quella di prima.") : L.T("Questo PC non permette di cambiargli la risoluzione (Impostazioni)."));
                return;
            }
            _fitW = Math.Clamp(w, 0, 8192);
            _fitH = Math.Clamp(h, 0, 8192);
            FitResolution(_fitW, _fitH);
        }

        private void FitResolution(int w, int h)
        {
            if (Interlocked.Exchange(ref _changingMode, 1) == 1) return;
            try
            {
                MonitorInfo? mon = _selected >= 0 && _selected < _monitors.Count ? _monitors[_selected] : null;
                string err = Platform.FitDisplay?.Invoke(mon, w, h) ?? _resizer.Fit(mon, w, h);
                if (err.Length > 0) Note(err);
                Thread.Sleep(800);              // il desktop si ridisegna: poi si rimanda l'elenco dei monitor
                SendMonitors();
            }
            finally { Interlocked.Exchange(ref _changingMode, 0); }
        }

        // ------------------------------------------------------------------ cartella condivisa

        private void OpenDrive(string name, bool readOnly)
        {
            CloseDrive("");
            if (!_allowInput || !P.S.AllowDrive)
            {
                Send(new MsgWriter(Msg.DriveState).Bool(false).Str(!_allowInput ? L.T("hai solo la visione di questo PC") : L.T("questo PC non accetta cartelle condivise (Impostazioni)")).Build());
                return;
            }
            if (Platform.IsMobile)
            {
                Send(new MsgWriter(Msg.DriveState).Bool(false).Str(L.T("su Android la cartella condivisa non si può montare: usa «File ▾»")).Build());
                return;
            }
            try
            {
                _drive = new DriveClient(SendAsync);
                _dav = new DavServer(_drive, name, readOnly);
                var (ok, where) = DriveMount.Mount(_dav);
                if (!ok)
                {
                    SessionLog.Write("DISCO cartella condivisa non montata: " + where);
                    Send(new MsgWriter(Msg.DriveState).Bool(false).Str(where).Build());
                    _dav.Dispose(); _dav = null; _drive = null;
                    return;
                }
                _davWhere = where;
                SessionLog.Write($"DISCO cartella «{name}» di {Identity.Format(PeerNumber)} montata in {where}" + (readOnly ? " (sola lettura)" : ""));
                Send(new MsgWriter(Msg.DriveState).Bool(true).Str(where).Build());
            }
            catch (Exception ex)
            {
                Send(new MsgWriter(Msg.DriveState).Bool(false).Str(ex.Message).Build());
                CloseDrive("");
            }
        }

        // ------------------------------------------------------------------ tunnel di porte

        private async Task OpenTunnelAsync(int id, string host, int port)
        {
            string why = !_allowInput ? L.T("hai solo la visione di questo PC")
                : !P.S.AllowTunnel ? L.T("questo PC non accetta tunnel di porte (Impostazioni)")
                : port is <= 0 or > 65535 ? L.T("porta non valida") : "";
            if (why.Length == 0)
            {
                var c = new TcpClient();
                try
                {
                    using var t = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    await c.ConnectAsync(host, port, t.Token);
                    _tunnels ??= new TunnelHub(SendAsync);
                    await SendAsync(new MsgWriter(Msg.TunnelOk).I32(id).Bool(true).Str("").Build());
                    _tunnels.Attach(id, c);
                    SessionLog.Write($"TUNNEL {Identity.Format(PeerNumber)} → {host}:{port} aperto");
                    return;
                }
                catch (Exception ex)
                {
                    c.Dispose();
                    why = L.F("{0}:{1} non risponde dal PC remoto ({2})", host, port, (ex is OperationCanceledException ? L.T("tempo scaduto") : ex.Message));
                }
            }
            SessionLog.Write($"TUNNEL {Identity.Format(PeerNumber)} → {host}:{port} rifiutato: {why}");
            await SendAsync(new MsgWriter(Msg.TunnelOk).I32(id).Bool(false).Str(why).Build());
        }

        // ------------------------------------------------------------------ video compresso

        /// <summary>
        /// Prova a mandare il fotogramma come video. false = si usano i riquadri (ffmpeg assente da una parte,
        /// codec «jpeg» scelto, codificatore fermo).
        /// </summary>
        private bool SendVideo(Frame f, string quality)
        {
            if (_videoOff || _codecWanted == "jpeg") { StopVideo(); return false; }
            string[] can = AppVersion.Value(PeerCaps, "vdec").Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (can.Length == 0 || Ffmpeg.Path == null) return false;
            var encTask = Ffmpeg.EncodersAsync();
            if (!encTask.IsCompleted) return false;           // prova dei codificatori in corso: intanto riquadri
            int w = f.Width & ~1, h = f.Height & ~1;           // i codec vogliono misure pari
            int fps = 1000 / FrameInterval();
            if (_video != null && _video.Hardware) fps = Math.Min(fps, 1000 / HwMinInterval);
            if (_video == null || _videoRestart || _video.Failed || _video.Width != w || _video.Height != h || _video.Fps != fps)
            {
                if (_video != null && _video.Failed && !_videoRestart)
                {
                    SessionLog.Write("VIDEO codificatore fermo: " + _video.Error);
                    Note("Video compresso non disponibile (" + (_video.Error.Length > 0 ? _video.Error.Split('\n')[0] : "ffmpeg si è fermato") + "): torno ai riquadri.");
                    StopVideo();
                    _videoOff = true;
                    return false;
                }
                StopVideo();
                int nMon = Math.Max(1, _monitors.Count);
                var enc = Ffmpeg.Choose(encTask.Result, can, _codecWanted, nMon);
                // solo nel registro (prima compariva come avviso a ogni collegamento: informazione, non c'è niente da fare)
                if (Ffmpeg.HwGuard.Suspended != null && !_hwNoted) { _hwNoted = true; SessionLog.Write("VIDEO " + Ffmpeg.HwGuard.Suspended); }
                else if (Ffmpeg.QuickSyncSkipped(encTask.Result, nMon) && !_hwNoted)
                {
                    _hwNoted = true;
                    SessionLog.Write("VIDEO " + L.F("grafica integrata Intel ({0}) non usata con {1} monitor collegati: codifica software", "Quick Sync", nMon));
                }
                if (enc == null)
                {
                    if (_codecWanted is "h264" or "hevc" or "av1") Note(L.F("{0} non disponibile su uno dei due PC: uso i riquadri.", Ffmpeg.CodecNames[_codecWanted]));
                    _videoOff = true;
                    return false;
                }
                int kbps = quality switch { "rapida" => 20000, "alta" => 12000, "media" => 4000, "bassa" => 1500, _ => _lanFast ? 10000 : 6000 };
                if (enc.Hardware) { fps = Math.Min(fps, 1000 / HwMinInterval); kbps = Math.Min(kbps, HwMaxKbps); }
                int gen = ++_videoGen;
                _videoWritten = 0; _videoAcked = 0;
                Link.SendAsync(new MsgWriter(Msg.VideoStart).I32(gen).Str(enc.Codec).I32(w).I32(h).Str(enc.Name).Build(), Cts.Token).Wait(Cts.Token);
                try
                {
                    _video = new VideoEncoder(enc, w, h, kbps, fps, data =>
                    {
                        try { Link.SendAsync(new MsgWriter(Msg.VideoData).I32(gen).Bytes(data).Build(), Cts.Token).Wait(Cts.Token); } catch { }
                        Interlocked.Add(ref _statBytesVideo, data.Length);
                    });
                }
                catch (Exception ex)
                {
                    Note(L.T("Video compresso non avviabile: ") + ex.Message);
                    _videoOff = true;
                    return false;
                }
                _videoRestart = false;
                _frameW = w; _frameH = h;
                SessionLog.Write($"VIDEO {Ffmpeg.CodecNames[enc.Codec]} con {enc.Name} ({(enc.Hardware ? "hardware" : "software")}) {w}×{h} a {kbps} kbit/s, {fps} fotogrammi/s");
            }
            // al massimo 4 fotogrammi in viaggio (codifica, rete, decodifica): niente ritardo che cresce
            if (_videoWritten - _videoAcked >= 4)
            {
                if (_videoBlockedSince == 0) _videoBlockedSince = Environment.TickCount64;
                else if (Environment.TickCount64 - _videoBlockedSince > 4000)
                {
                    _videoBlockedSince = 0;
                    // il visore non ha mai restituito un fotogramma: dopo due prove si torna ai riquadri
                    _videoDead = _videoAcked == 0 ? _videoDead + 1 : 0;
                    if (_videoDead >= 2)
                    {
                        SessionLog.Write("VIDEO il visore non decodifica il flusso: torno ai riquadri JPEG");
                        Note(L.T("Il PC remoto non riesce a decodificare il video compresso: uso i riquadri."));
                        StopVideo();
                        _videoOff = true;
                        _videoDead = 0;
                        return false;
                    }
                    _videoRestart = true;
                }
                return true;
            }
            _videoBlockedSince = 0;
            if (_video!.Write(f)) { _videoWritten++; _statFrames++; }
            return true;
        }

        private long _statBytesVideo;
        private int _videoDead;

        private void StopVideo()
        {
            _video?.Dispose();
            _video = null;
        }

        private void CloseDrive(string why)
        {
            var dav = Interlocked.Exchange(ref _dav, null);
            if (dav == null) return;
            if (_davWhere.Length > 0) DriveMount.Unmount(dav, _davWhere);
            dav.Dispose();
            _drive?.CancelAll();
            _drive = null;
            if (why.Length > 0) SessionLog.Write("DISCO cartella condivisa smontata: " + why);
            if (why == "tolta dal visore") Send(new MsgWriter(Msg.DriveState).Bool(false).Str("").Build());
            _davWhere = "";
        }

        /// <summary>L'utente di questo PC toglie (o ridà) il permesso di usare mouse e tastiera durante la sessione.</summary>
        public void SetInputAllowed(bool on)
        {
            if (on == _allowInput) return;
            _allowInput = on;
            if (on && _input == null) _input = InputSinks.Create();
            if (!on && _input != null) _pressed.ReleaseAll(_input);
            Note(on ? "" : L.T("L'utente dell'altro PC ha disattivato mouse e tastiera: ora puoi solo guardare."));
        }
    }
}
