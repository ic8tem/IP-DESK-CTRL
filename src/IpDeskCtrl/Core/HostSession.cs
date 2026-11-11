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

        public bool IsActive => _accepted && !IsClosed;
        public bool InputAllowed => _allowInput;
        public event Action? Activated;

        public HostSession(Peer p, SecureLink link, bool busy) : base(p, link) { _busy = busy; }

        public void Run(CancellationToken life)
        {
            life.Register(() => Close("programma chiuso sull'altro PC"));
            _ = Task.Run(ReceiveLoopAsync);
            _ = Task.Run(StartAsync);
        }

        private async Task StartAsync()
        {
            try
            {
                await SendAsync(new MsgWriter(Msg.Hello).Str(AppVersion.Tag).Str(AppVersion.Os).Str(Environment.MachineName).Bool(TrxCtrl.Installed).Str(AppVersion.CapsNow()).Build());
                if (await Task.WhenAny(_helloSeen.Task, Task.Delay(15000, Cts.Token)) != _helloSeen.Task) { Close("l'altro PC non si è presentato"); return; }
                var (proto, _) = AppVersion.Parse(PeerVersion);
                if (proto != AppVersion.Protocol) { await Refuse(AcceptState.Rejected, $"versioni non compatibili ({PeerVersion} / {AppVersion.Tag})"); return; }
                if (!P.S.AcceptIncoming) { await Refuse(AcceptState.Disabled, "su quel PC i collegamenti in arrivo sono disattivati"); return; }
                if (_busy) { await Refuse(AcceptState.Busy, "quel PC è già comandato da un altro"); return; }

                var h = P.S.Find(PeerNumber);
                string keyHex = Convert.ToHexString(Link.PeerKey);
                bool known = h != null && h.PeerKey == keyHex;
                IncomingDecision? d = null;
                if (known && h!.AllowUnattended)
                {
                    var pw = P.S.PasswordHash.Length > 0 ? await CheckPasswordAsync() : PwResult.Ok;
                    if (IsClosed) return;
                    if (pw == PwResult.Blocked)
                    {
                        int min = (int)Math.Ceiling((AccessPassword.BlockedFor(PeerNumber) ?? AccessPassword.BlockTime).TotalMinutes);
                        await Refuse(AcceptState.Rejected, $"troppe password sbagliate: riprova tra {min} minuti");
                        return;
                    }
                    if (pw == PwResult.Ok)
                    {
                        d = new IncomingDecision(true, P.S.AllowInput, true);
                        SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} entra senza conferma (accesso consentito in precedenza" +
                                         (P.S.PasswordHash.Length > 0 ? ", password giusta)" : ")"));
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
                            Crypto.Fingerprint(Link.PeerKey), known), t.Token);
                    }
                    catch (OperationCanceledException) { d = new IncomingDecision(false, false, false); }
                }
                if (IsClosed || d == null) return;
                if (!d.Accept) { await Refuse(AcceptState.Rejected, d.Why.Length > 0 ? d.Why : "richiesta rifiutata"); return; }

                _allowInput = d.AllowInput;
                var entry = P.S.Touch(PeerNumber, false, keyHex, PeerOs);
                entry.Version = PeerVersion;
                if (d.AlwaysAllow && !entry.AllowUnattended) { entry.AllowUnattended = true; P.S.Save(); }
                _accepted = true;
                await SendAsync(new MsgWriter(Msg.Accept).U8((byte)AcceptState.Accepted).Str(_allowInput ? "" : "solo visione").Build());
                Activated?.Invoke();

                if (Platform.PrepareHost != null)
                {
                    string why = await Platform.PrepareHost();
                    if (why.Length > 0) Note(why);
                }
                _screen = ScreenSources.Create();
                if (_allowInput) _input = InputSinks.Create();
                SendMonitors();
                var capture = new Thread(CaptureLoop) { IsBackground = true, Name = "cattura schermo", Priority = ThreadPriority.AboveNormal };
                capture.Start();
                _ = Task.Run(PingLoopAsync);
                _ = Ffmpeg.EncodersAsync();          // prova dei codificatori video in sottofondo (una volta sola)
                _ = Task.Run(MaintenanceLoopAsync);
                _ = Task.Run(ClipboardLoopAsync);
                _ = Task.Run(ClipboardFilesLoopAsync);
                var ext = P.PublicIp != null && P.ExternalPort > 0 ? new IPEndPoint(P.PublicIp, P.ExternalPort) : null;
                await OfferUdpAsync(P.LocalPort, ext);
            }
            catch (Exception ex) when (!IsClosed)
            {
                ErrorLog.Write(ex.ToString());
                Close("errore sul PC comandato: " + ex.Message);
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
                msg = left == 1 ? "Password sbagliata: resta un solo tentativo." : $"Password sbagliata: restano {left} tentativi.";
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
                case Msg.Volume:
                {
                    int a = r.U8();
                    if (!_accepted || !_allowInput) { Note("Hai solo la visione di questo PC: il volume non si può cambiare."); break; }
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
                case Msg.SetAudio: SetAudio(r.Bool()); break;
                case Msg.Refresh: _fullNext = true; break;
                case Msg.FileFetch: _ = Task.Run(SendClipboardFilesAsync); break;
                case Msg.Reboot: _ = Task.Run(RebootAsync); break;
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
            var sink = _input;
            if (sink == null || !_allowInput) return;
            try
            {
                switch (r.Type)
                {
                    case Msg.MouseMove: { var (x, y) = Map(r.I32(), r.I32()); sink.MoveTo(x, y); break; }
                    case Msg.MouseButton:
                    {
                        int b = r.U8(); bool down = r.Bool();
                        var (x, y) = Map(r.I32(), r.I32());
                        sink.MoveTo(x, y);
                        sink.Button(b, down);
                        _pressed.Button(b, down);
                        break;
                    }
                    case Msg.Wheel: sink.Wheel(r.I32(), r.I32()); break;
                    case Msg.Key: { int k = r.U16(); bool down = r.Bool(); sink.Key(k, down); _pressed.Key(k, down); break; }
                    case Msg.ReleaseAll: _pressed.ReleaseAll(sink); break;
                    case Msg.Text: sink.Text(r.Str()); break;
                }
                if (sink.Note.Length > 0) Note(sink.Note);
            }
            catch (Exception ex) { Note("Mouse e tastiera non disponibili: " + ex.Message); }
        }

        protected override bool FilesAllowed => _accepted && _allowInput;

        private void StartTrx()
        {
            if (!_accepted || !_allowInput) { Note("Hai solo la visione di questo PC: IP-TRX-CTRL non è stato avviato."); return; }
            string err = Platform.LaunchTrx != null ? Platform.LaunchTrx() : TrxCtrl.Launch();
            SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} avvia IP-TRX-CTRL: " + (err.Length == 0 ? "avviato" : err));
            Note(err.Length == 0 ? "IP-TRX-CTRL avviato sul PC remoto." : err);
        }

        /// <summary>Riavvio chiesto dal visore: solo se può comandare questo PC.</summary>
        private async Task RebootAsync()
        {
            if (!_accepted || !_allowInput) { Note("Riavvio rifiutato: hai solo la visione di questo PC."); return; }
            SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} chiede il riavvio del PC");
            string err = Power.Reboot();
            if (err.Length > 0) { Note(err); SessionLog.Write("SESSIONE " + err); return; }
            await Task.Delay(500);
            Close("riavvio del PC in corso");
        }

        /// <summary>
        /// File trascinati sulla finestra del visore: sono negli appunti di questo PC; si incollano dove sono
        /// stati lasciati (clic nel punto e Ctrl+V, Cmd+V su macOS), come un trascinamento vero.
        /// </summary>
        protected override void OnFilesReceived(TransferPurpose purpose, string folder, List<string> topLevel, bool onClipboard)
        {
            string what = topLevel.Count == 1 ? "1 elemento" : $"{topLevel.Count} elementi";
            if (purpose == TransferPurpose.Drop && onClipboard && _input != null && _allowInput && !Platform.IsMobile)
            {
                try
                {
                    _input.Button(Input.MouseBtn.Left, true);
                    _input.Button(Input.MouseBtn.Left, false);
                    Thread.Sleep(150);
                    int mod = OperatingSystem.IsMacOS() ? Input.KeyMap.LeftMeta : Input.KeyMap.LeftCtrl;
                    _input.Key(mod, true); _input.Key(47, true); _input.Key(47, false); _input.Key(mod, false);
                    Note($"{what} copiati sul PC remoto (in {folder}) e incollati dove li hai lasciati.");
                    return;
                }
                catch { }
            }
            Note($"{what} copiati sul PC remoto in {folder}" + (onClipboard ? ": sono negli appunti, incollali con Ctrl+V dove vuoi." : "."));
        }

        private List<string> _clipFiles = new();

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
                    if (files.SequenceEqual(_clipFiles)) continue;
                    _clipFiles = files;
                    var items = files.Count > 0 ? FromPaths(files) : new List<TransferItem>();
                    string first = files.Count > 0 ? System.IO.Path.GetFileName(files[0].TrimEnd('/', '\\')) : "";
                    await SendAsync(new MsgWriter(Msg.FilesAvailable).I32(files.Count).I64(items.Sum(i => i.Size)).Str(first).Build());
                }
                catch { }
            }
        }

        private async Task SendClipboardFilesAsync()
        {
            if (!FilesAllowed) return;
            var clip = Platform.Clipboard;
            var files = clip == null ? new List<string>() : await clip.GetFilesAsync();
            if (files.Count == 0) { Note("Negli appunti del PC remoto non ci sono più file."); return; }
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

        private void Note(string text)
        {
            if (text == _lastNote) return;
            _lastNote = text;
            if (text.Length > 0) SessionLog.Write("AVVISO " + text);
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

                    var area = _area;
                    if (area == null || _screen == null) { Thread.Sleep(50); continue; }
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
                    if (_quality == "auto")
                    {
                        long now = sw.ElapsedMilliseconds;
                        if (_ackMs > 450 && _autoLevel < 3 && now - lastAutoChange > 3000) { _autoLevel++; lastAutoChange = now; _fullNext = true; goodSince = now; }
                        else if (_ackMs < 150) { if (goodSince == 0) goodSince = now; if (_autoLevel > 0 && now - goodSince > 8000 && now - lastAutoChange > 8000) { _autoLevel--; lastAutoChange = now; goodSince = now; } }
                        else goodSince = 0;
                    }
                    var (q, scale) = Quality.Of(_quality, _autoLevel);
                    Frame f = raw;
                    if (scale < 0.75) { TileEncoder.Half(raw, half); f = half; }

                    if (_fullNext && _video != null) _videoRestart = true;      // «Aggiorna»: nuovo fotogramma chiave
                    if (SendVideo(f, _quality))
                    {
                        _fullNext = false;
                        CursorUpdate(area, f, ref lastCx, ref lastCy);
                        long elv = sw.ElapsedMilliseconds - t0;
                        Thread.Sleep((int)Math.Max(5, 40 - elv));
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

                    CursorUpdate(area, f, ref lastCx, ref lastCy);
                }
                catch (OperationCanceledException) { break; }
                catch (AggregateException) when (Cts.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    ErrorLog.Write("Cattura: " + ex);
                    Thread.Sleep(500);
                }
                // circa 25 fotogrammi al secondo al massimo (meno se cattura e codifica sono lente)
                long el = sw.ElapsedMilliseconds - t0;
                Thread.Sleep((int)Math.Max(5, 40 - el));
            }
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
                if (Environment.TickCount64 - _statAt > 60000)
                {
                    _statAt = Environment.TickCount64;
                    long vb = Interlocked.Exchange(ref _statBytesVideo, 0);
                    SessionLog.Write($"SCHERMO ultimo minuto: {_statFrames} fotogrammi, " + (_video != null ? $"video {Ffmpeg.CodecNames[_video.Codec]} {vb / 1024} kB" : $"{_statTiles} riquadri, {_statBytes / 1024} kB") +
                                     $", {_statFail} catture non riuscite · ritardo {_ackMs:F0} ms · {Route}");
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
                if (a.Start(OnAudioFrame)) _audio = a;
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
            CloseDrive("collegamento chiuso");
            _tunnels?.CloseAll();
            _video?.Dispose();
            _video = null;
            _screen?.Dispose();
            try { Platform.HostEnded?.Invoke(); } catch { }
            var h = P.S.Find(PeerNumber);
            if (h != null) { h.LastSeconds = Duration(DateTime.UtcNow - Started); P.S.Save(); }
        }

        public static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min" : $"{t.Seconds} s";

        // ------------------------------------------------------------------ microfono del visore

        private void SetMic(bool on)
        {
            lock (_micLock)
            {
                if (!on || !P.S.AcceptMic)
                {
                    _mic?.Dispose();
                    _mic = null;
                    if (on) Note("Questo PC non accetta il microfono di chi si collega (Impostazioni).");
                    return;
                }
                if (_mic != null) return;
                _mic = SystemAudio.CreateMicOutput(P.S.MicOutput);
                string dev = _mic.Device;
                SessionLog.Write($"AUDIO microfono di {Identity.Format(PeerNumber)} su " + (dev.Length > 0 ? dev : "uscita predefinita"));
                Note(_mic.Note.Length > 0 ? _mic.Note
                    : dev.Length > 0 ? $"La tua voce arriva sul PC remoto nel microfono virtuale «{dev}»: sceglilo come microfono nei programmi di là."
                    : "La tua voce si sente dalle casse del PC remoto. Per usarla come microfono nei programmi di là serve un cavo audio virtuale (VB-CABLE su Windows, BlackHole su Mac).");
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
                Note(!_allowInput ? "Hai solo la visione di questo PC: la risoluzione resta quella di prima." : "Questo PC non permette di cambiargli la risoluzione (Impostazioni).");
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
                string err = _resizer.Fit(mon, w, h);
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
                Send(new MsgWriter(Msg.DriveState).Bool(false).Str(!_allowInput ? "hai solo la visione di questo PC" : "questo PC non accetta cartelle condivise (Impostazioni)").Build());
                return;
            }
            if (Platform.IsMobile)
            {
                Send(new MsgWriter(Msg.DriveState).Bool(false).Str("su Android la cartella condivisa non si può montare: usa «File ▾»").Build());
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
            string why = !_allowInput ? "hai solo la visione di questo PC"
                : !P.S.AllowTunnel ? "questo PC non accetta tunnel di porte (Impostazioni)"
                : port is <= 0 or > 65535 ? "porta non valida" : "";
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
                    why = $"{host}:{port} non risponde dal PC remoto ({(ex is OperationCanceledException ? "tempo scaduto" : ex.Message)})";
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
            if (_video == null || _videoRestart || _video.Failed || _video.Width != w || _video.Height != h)
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
                var enc = Ffmpeg.Choose(encTask.Result, can, _codecWanted);
                if (enc == null)
                {
                    if (_codecWanted is "h264" or "hevc" or "av1") Note($"{Ffmpeg.CodecNames[_codecWanted]} non disponibile su uno dei due PC: uso i riquadri.");
                    _videoOff = true;
                    return false;
                }
                int kbps = quality switch { "alta" => 12000, "media" => 4000, "bassa" => 1500, _ => 6000 };
                int gen = ++_videoGen;
                _videoWritten = 0; _videoAcked = 0;
                Link.SendAsync(new MsgWriter(Msg.VideoStart).I32(gen).Str(enc.Codec).I32(w).I32(h).Str(enc.Name).Build(), Cts.Token).Wait(Cts.Token);
                try
                {
                    _video = new VideoEncoder(enc, w, h, kbps, data =>
                    {
                        try { Link.SendAsync(new MsgWriter(Msg.VideoData).I32(gen).Bytes(data).Build(), Cts.Token).Wait(Cts.Token); } catch { }
                        Interlocked.Add(ref _statBytesVideo, data.Length);
                    });
                }
                catch (Exception ex)
                {
                    Note("Video compresso non avviabile: " + ex.Message);
                    _videoOff = true;
                    return false;
                }
                _videoRestart = false;
                _frameW = w; _frameH = h;
                SessionLog.Write($"VIDEO {Ffmpeg.CodecNames[enc.Codec]} con {enc.Name} ({(enc.Hardware ? "hardware" : "software")}) {w}×{h} a {kbps} kbit/s");
            }
            // al massimo 4 fotogrammi in viaggio (codifica, rete, decodifica): niente ritardo che cresce
            if (_videoWritten - _videoAcked >= 4)
            {
                if (_videoBlockedSince == 0) _videoBlockedSince = Environment.TickCount64;
                else if (Environment.TickCount64 - _videoBlockedSince > 4000) { _videoRestart = true; _videoBlockedSince = 0; }
                return true;
            }
            _videoBlockedSince = 0;
            if (_video!.Write(f)) { _videoWritten++; _statFrames++; }
            return true;
        }

        private long _statBytesVideo;

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
            Note(on ? "" : "L'utente dell'altro PC ha disattivato mouse e tastiera: ora puoi solo guardare.");
        }
    }
}
