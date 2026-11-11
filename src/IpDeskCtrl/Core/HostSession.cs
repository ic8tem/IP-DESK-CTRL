using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
                await SendAsync(new MsgWriter(Msg.Hello).Str(AppVersion.Tag).Str(AppVersion.Os).Str(Environment.MachineName).Build());
                if (await Task.WhenAny(_helloSeen.Task, Task.Delay(15000, Cts.Token)) != _helloSeen.Task) { Close("l'altro PC non si è presentato"); return; }
                var (proto, _) = AppVersion.Parse(PeerVersion);
                if (proto != AppVersion.Protocol) { await Refuse(AcceptState.Rejected, $"versioni non compatibili ({PeerVersion} / {AppVersion.Tag})"); return; }
                if (!P.S.AcceptIncoming) { await Refuse(AcceptState.Disabled, "su quel PC i collegamenti in arrivo sono disattivati"); return; }
                if (_busy) { await Refuse(AcceptState.Busy, "quel PC è già comandato da un altro"); return; }

                var h = P.S.Find(PeerNumber);
                string keyHex = Convert.ToHexString(Link.PeerKey);
                bool known = h != null && h.PeerKey == keyHex;
                IncomingDecision d;
                if (known && h!.AllowUnattended)
                {
                    d = new IncomingDecision(true, P.S.AllowInput, true);
                    SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} entra senza conferma (accesso consentito in precedenza)");
                }
                else
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
                if (IsClosed) return;
                if (!d.Accept) { await Refuse(AcceptState.Rejected, d.Why.Length > 0 ? d.Why : "richiesta rifiutata"); return; }

                _allowInput = d.AllowInput;
                var entry = P.S.Touch(PeerNumber, false, keyHex, PeerOs);
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
            SessionLog.Write("SCHERMO monitor: " + string.Join(" | ", _monitors.Select((m, i) => m.Label(i) + $" a {m.X},{m.Y}")) + $" · mostro {(_selected < 0 ? "tutti" : (_selected + 1).ToString())}");
        }

        // ------------------------------------------------------------------ messaggi dal visore

        protected override void Handle(MsgReader r)
        {
            switch (r.Type)
            {
                case Msg.Hello:
                    PeerVersion = r.Str(); PeerOs = r.Str(); PeerName = r.Str();
                    _helloSeen.TrySetResult(true);
                    break;
                case Msg.SelectMonitor:
                    _selected = r.I32();
                    if (_selected < -1) _selected = 0;
                    SendMonitors();
                    break;
                case Msg.SetQuality: _quality = r.Str(); _fullNext = true; break;
                case Msg.SetAudio: SetAudio(r.Bool()); break;
                case Msg.Refresh: _fullNext = true; break;
                case Msg.FileFetch: _ = Task.Run(SendClipboardFilesAsync); break;
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

                    // puntatore del PC comandato, in coordinate dell'immagine
                    var c = _screen.Cursor();
                    if (c != null)
                    {
                        int cx = (int)Math.Round((c.Value.x - area.X) * (double)f.Width / Math.Max(1, area.W));
                        int cy = (int)Math.Round((c.Value.y - area.Y) * (double)f.Height / Math.Max(1, area.H));
                        if (cx != lastCx || cy != lastCy)
                        {
                            lastCx = cx; lastCy = cy;
                            bool inside = cx >= 0 && cy >= 0 && cx < f.Width && cy < f.Height;
                            Send(new MsgWriter(Msg.Cursor).I32(cx).I32(cy).Bool(inside).Build());
                        }
                    }
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

        private async Task MaintenanceLoopAsync()
        {
            while (!Cts.IsCancellationRequested)
            {
                try { await Task.Delay(3000, Cts.Token); } catch { break; }
                CheckUdpAlive(TimeSpan.FromSeconds(6));
                if (Environment.TickCount64 - _statAt > 60000)
                {
                    _statAt = Environment.TickCount64;
                    SessionLog.Write($"SCHERMO ultimo minuto: {_statFrames} fotogrammi, {_statTiles} riquadri, {_statBytes / 1024} kB, {_statFail} catture non riuscite · ritardo {_ackMs:F0} ms · {Route}");
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
            _screen?.Dispose();
            try { Platform.HostEnded?.Invoke(); } catch { }
            var h = P.S.Find(PeerNumber);
            if (h != null) { h.LastSeconds = Duration(DateTime.UtcNow - Started); P.S.Save(); }
        }

        public static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min" : $"{t.Seconds} s";

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
