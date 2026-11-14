using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Audio;
using IpDeskCtrl.Net;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Core
{
    /// <summary>Questo PC guarda e comanda un altro PC.</summary>
    public sealed class ViewerSession : SessionBase
    {
        public TileDecoder Decoder { get; } = new();
        public List<MonitorInfo> Monitors { get; private set; } = new();
        public int SelectedMonitor { get; private set; }
        public AcceptState State { get; private set; } = AcceptState.Waiting;
        public string StateText { get; private set; } = "";
        public string Note { get; private set; } = "";
        public bool InputAllowed { get; private set; } = true;
        public (int x, int y, bool visible) RemoteCursor { get; private set; }
        public double Fps { get; private set; }
        public double KbitPerSecond { get; private set; }

        public event Action? StateChanged;
        /// <summary>L'host chiede la password dell'accesso senza conferma (e quella ricordata non c'è o è sbagliata).</summary>
        public event Action<PasswordChallenge>? PasswordRequested;
        private string? _pendingSaved;            // password da ricordare (o "" = da dimenticare) quando l'host accetta
        public event Action? MonitorsChanged;
        public event Action? FrameReady;          // dal thread di rete
        public event Action? CursorMoved;
        public event Action? NoteChanged;
        /// <summary>File negli appunti del PC remoto (numero, byte, primo nome).</summary>
        public event Action<int, long, string>? RemoteFilesChanged;
        /// <summary>File arrivati dal PC remoto: cartella, elementi, già negli appunti.</summary>
        public event Action<string, List<string>, bool>? FilesReceived;
        public (int count, long bytes, string first) RemoteFiles { get; private set; }

        private IAudioPlayer? _player;
        private bool _audioOn;
        private int _frames;
        private long _lastBytes;
        private DateTime _lastStat = DateTime.UtcNow;
        private (int x, int y) _pendingMove;
        private int _movePending;

        public ViewerSession(Peer p, SecureLink link) : base(p, link) { _tunnelHub = new TunnelHub(SendAsync); }

        public void Run(CancellationToken life)
        {
            life.Register(() => Close(L.T("programma chiuso")));
            _ = Task.Run(ReceiveLoopAsync);
            _ = Task.Run(async () =>
            {
                await SendAsync(new MsgWriter(Msg.Hello).Str(AppVersion.Tag).Str(AppVersion.Os).Str(Environment.MachineName).Bool(false).Str(AppVersion.CapsNow()).Build());
                _ = Task.Run(PingLoopAsync);
                _ = Task.Run(StatsLoopAsync);
            });
        }

        protected override void Handle(MsgReader r)
        {
            switch (r.Type)
            {
                case Msg.Hello:
                    PeerVersion = r.Str(); PeerOs = r.Str(); PeerName = r.Str();
                    PeerHasTrx = !r.AtEnd && r.Bool();
                    PeerCaps = r.AtEnd ? "" : r.Str();
                    break;
                case Msg.DriveReq:
                {
                    int id = r.I32(); byte op = r.U8(); string path = r.Str(), path2 = r.Str(); long pos = r.I64();
                    byte[] data = r.Bytes(Math.Clamp(r.I32(), 0, 4 << 20));
                    var share = _share;
                    _ = Task.Run(async () =>
                    {
                        var (status, reply) = share != null ? share.Handle(op, path, path2, pos, data) : (DriveOp.NotFound, Array.Empty<byte>());
                        await SendAsync(new MsgWriter(Msg.DriveResp).I32(id).I32(status).I32(reply.Length).Bytes(reply).Build());
                    });
                    break;
                }
                case Msg.Resume:
                    if (r.U8() == 0) { UpdateRequested = true; SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} si aggiorna: mi ricollego appena torna"); }
                    break;
                case Msg.VideoStart:
                {
                    int gen = r.I32(); string codec = r.Str(); int w = r.I32(), h = r.I32(); string enc = r.AtEnd ? "" : r.Str();
                    StartVideo(gen, codec, w, h, enc);
                    break;
                }
                case Msg.VideoData:
                {
                    int gen = r.I32();
                    var d = _vdec;
                    if (gen != _vgen || d == null) break;
                    if (d.Failed) { VideoFailed(L.T("il decodificatore ffmpeg si è fermato")); break; }
                    d.Feed(r.Rest());
                    break;
                }
                case Msg.TunnelOk:
                {
                    int id = r.I32(); bool ok = r.Bool(); string why = r.Str();
                    if (_tunnelWait.TryRemove(id, out var t)) t.TrySetResult((ok, why));
                    break;
                }
                case Msg.TunnelData: { int id = r.I32(); _tunnelHub.OnData(id, r.Rest()); break; }
                case Msg.TunnelClose: _tunnelHub.OnClose(r.I32()); break;
                case Msg.DriveState:
                {
                    bool on = r.Bool(); string where = r.Str();
                    if (!on) _share = null;
                    DriveMounted = on ? where : "";
                    DriveChanged?.Invoke(on, where);
                    break;
                }
                case Msg.Accept:
                {
                    State = (AcceptState)r.U8();
                    StateText = r.Str();
                    if (State == AcceptState.Accepted)
                    {
                        InputAllowed = StateText.Length == 0;
                        var h = P.S.Touch(PeerNumber, true, Convert.ToHexString(Link.PeerKey), PeerOs);
                        h.Version = PeerVersion;
                        if (_pendingSaved != null) { h.SavedPassword = _pendingSaved; P.S.Save(); }
                        P.RememberWake(h);
                        Send(new MsgWriter(Msg.SetQuality).Str(P.S.Quality).Build());
                        if (PeerHas("tunnel") || AppVersion.Value(PeerCaps, "vdec").Length > 0 || PeerHas("mic")) Send(new MsgWriter(Msg.SetCodec).Str(P.S.VideoCodec).Build());
                        SetAudio(P.S.PlayAudio);
                        _ = Task.Run(ClipboardLoopAsync);
                        if (!_throttleOff) { _throttleOff = true; PowerThrottle.SessionStarted(); }
                    }
                    StateChanged?.Invoke();
                    break;
                }
                case Msg.AuthChallenge:
                {
                    var c = new PasswordChallenge(r.Bytes(Math.Clamp(r.I32(), 0, 64)), r.I32(), r.Bytes(Math.Clamp(r.I32(), 0, 64)), r.Str());
                    var saved = P.S.Find(PeerNumber)?.SavedPassword ?? "";
                    byte[]? key = c.Message.Length == 0 && saved.Length > 0 ? AccessPassword.Unprotect(P.Me, PeerNumber, saved, c.Salt) : null;
                    if (key != null)
                    {
                        SessionLog.Write($"SESSIONE {Identity.Format(PeerNumber)} chiede la password: uso quella ricordata");
                        Send(new MsgWriter(Msg.AuthResponse).I32(32).Bytes(key).Build());
                    }
                    else if (PasswordRequested != null) PasswordRequested(c);
                    else Send(new MsgWriter(Msg.AuthResponse).I32(0).Build());
                    break;
                }
                case Msg.Monitors:
                {
                    int n = Math.Clamp(r.I32(), 0, 32);
                    var list = new List<MonitorInfo>();
                    for (int i = 0; i < n; i++) list.Add(new MonitorInfo(r.Str(), r.I32(), r.I32(), r.I32(), r.I32(), r.Bool()));
                    Monitors = list;
                    SelectedMonitor = r.I32();
                    MonitorsChanged?.Invoke();
                    break;
                }
                case Msg.FrameStart:
                {
                    r.I32();
                    int w = r.I32(), h = r.I32();
                    Decoder.Start(w, h);
                    break;
                }
                case Msg.Tile:
                {
                    r.I32();
                    int x = r.I32(), y = r.I32(), w = r.I32(), h = r.I32();
                    byte fmt = r.U8();
                    byte[] data = r.Bytes(r.I32());
                    Decoder.Apply(x, y, w, h, fmt, data);
                    break;
                }
                case Msg.FrameEnd:
                {
                    int id = r.I32();
                    Interlocked.Increment(ref _frames);
                    FrameReady?.Invoke();
                    Send(new MsgWriter(Msg.FrameAck).I32(id).Build());
                    break;
                }
                case Msg.Cursor:
                    RemoteCursor = (r.I32(), r.I32(), r.Bool());
                    CursorMoved?.Invoke();
                    break;
                case Msg.ScreenNote:
                    Note = r.Str();
                    if (Note.Contains("solo guardare")) InputAllowed = false;
                    if (Note.Contains("iavvio")) RebootRequested = false;      // riavvio rifiutato o non riuscito
                    NoteChanged?.Invoke();
                    break;
                case Msg.FilesAvailable:
                    RemoteFiles = (r.I32(), r.I64(), r.Str());
                    RemoteFilesChanged?.Invoke(RemoteFiles.count, RemoteFiles.bytes, RemoteFiles.first);
                    break;
                case Msg.Audio:
                {
                    r.U32();
                    PlayAudio(r.Rest());
                    break;
                }
            }
        }

        protected override void OnUdpAudio(uint seq, byte[] data) => PlayAudio(data);

        private void PlayAudio(byte[] adpcm)
        {
            if (!_audioOn) return;
            var pcm = Adpcm.Decode(adpcm);
            var p = _player;
            if (pcm.Length == 0 || p == null) return;
            p.Play(pcm);
            // l'uscita audio può sparire o tornare durante il collegamento: l'avviso segue
            if (p.Note != _playerNote)
            {
                bool wasAudioNote = Note == _playerNote && _playerNote.Length > 0;
                _playerNote = p.Note;
                if (p.Note.Length > 0) { Note = p.Note; NoteChanged?.Invoke(); }
                else if (wasAudioNote) { Note = ""; NoteChanged?.Invoke(); }
            }
        }

        private string _playerNote = "";

        public bool AudioOn => _audioOn;

        public void SetAudio(bool on)
        {
            _audioOn = on;
            if (on && _player == null)
            {
                _player = SystemAudio.CreatePlayer();
                _playerNote = _player.Note;
                if (_player.Note.Length > 0) { Note = _player.Note; NoteChanged?.Invoke(); }
            }
            if (!on) { _player?.Dispose(); _player = null; }
            Send(new MsgWriter(Msg.SetAudio).Bool(on).Build());
        }

        public void SelectMonitor(int index) => Send(new MsgWriter(Msg.SelectMonitor).I32(index).Build());

        // ------------------------------------------------------------------ 0.5: funzioni annunciate dall'altro PC

        /// <summary>Funzioni del PC remoto (vedi <see cref="AppVersion.Caps"/>); vuoto = 0.4.</summary>
        public string PeerCaps { get; private set; } = "";
        public bool PeerHas(string what) => AppVersion.Has(PeerCaps, what);

        // microfono di qui verso il PC remoto
        private ISystemAudioCapture? _micCap;
        private readonly Adpcm.State _mL = new(), _mR = new();
        private uint _micSeq;
        public bool MicOn => _micCap != null;

        /// <summary>Accende o spegne il microfono verso il PC remoto. "" = fatto, altrimenti il motivo.</summary>
        public string SetMic(bool on)
        {
            if (!on)
            {
                _micCap?.Dispose();
                _micCap = null;
                Send(new MsgWriter(Msg.SetMic).Bool(false).Build());
                return "";
            }
            if (!PeerHas("mic")) return L.T("Il PC remoto ha una versione che non riceve il microfono: aggiornalo alla 0.5.");
            if (_micCap != null) return "";
            var cap = SystemAudio.CreateMic();
            if (!cap.Start(OnMicFrame)) { string why = cap.Note; cap.Dispose(); return why.Length > 0 ? why : L.T("Microfono non disponibile."); }
            _micCap = cap;
            Send(new MsgWriter(Msg.SetMic).Bool(true).Build());
            return "";
        }

        private void OnMicFrame(short[] stereo)
        {
            if (_micCap == null || IsClosed) return;
            byte[] enc = Adpcm.Encode(stereo, _mL, _mR);
            uint seq = ++_micSeq;
            var udp = Udp;
            if (udp != null) udp.SendAudio(enc);
            else Send(new MsgWriter(Msg.Audio).U32(seq).Bytes(enc).Build());
        }

        /// <summary>Volume del PC remoto: 0 giù, 1 su, 2 muto.</summary>
        public void Volume(int action)
        {
            if (PeerHas("vol")) Send(new MsgWriter(Msg.Volume).U8((byte)action).Build());
            else Combo(action == 1 ? 115 : action == 0 ? 114 : 113);    // 0.4: tasti multimediali
        }

        /// <summary>Chiede al PC remoto la risoluzione più adatta a w×h pixel (0, 0 = quella di prima).</summary>
        public void RequestResize(int w, int h) { if (PeerHas("res")) Send(new MsgWriter(Msg.Resize).I32(w).I32(h).Build()); }

        // ------------------------------------------------------------------ video compresso

        private VideoDecoder? _vdec;
        private volatile int _vgen;
        private int _vframes;
        /// <summary>Codec in uso («H.264 (h264_nvenc)»), "" = riquadri JPEG.</summary>
        public string VideoInfo { get; private set; } = "";

        /// <summary>Codec chiesto al PC remoto: auto, h264, hevc, av1, jpeg.</summary>
        public void SetCodec(string codec) => Send(new MsgWriter(Msg.SetCodec).Str(codec).Build());

        private void StartVideo(int gen, string codec, int w, int h, string enc)
        {
            _vdec?.Dispose();
            _vdec = null;
            _vgen = gen;
            _vframes = 0;
            if (Screen.Ffmpeg.Path == null) { VideoFailed(L.T("ffmpeg non c'è su questo dispositivo")); return; }
            try
            {
                _vdec = new VideoDecoder(codec, w, h, frame =>
                {
                    if (gen != _vgen) return;
                    Decoder.SetFrame(frame, w, h);
                    int n = Interlocked.Increment(ref _vframes);
                    Interlocked.Increment(ref _frames);
                    FrameReady?.Invoke();
                    Send(new MsgWriter(Msg.VideoAck).I32(gen).I32(n).Build());
                });
                VideoInfo = (Screen.Ffmpeg.CodecNames.TryGetValue(codec, out var nm) ? nm : codec) + (enc.Length > 0 ? $" ({enc})" : "");
                SessionLog.Write($"VIDEO ricevo {VideoInfo} {w}×{h}");
            }
            catch (Exception ex) { VideoFailed(ex.Message); }
        }

        private void VideoFailed(string why)
        {
            _vdec?.Dispose();
            _vdec = null;
            _vgen = -1;
            VideoInfo = "";
            SessionLog.Write("VIDEO decodifica non possibile: " + why + " (torno ai riquadri)");
            Send(new MsgWriter(Msg.SetCodec).Str("jpeg").Build());
        }

        // ------------------------------------------------------------------ tunnel di porte

        private readonly TunnelHub _tunnelHub;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<(bool ok, string why)>> _tunnelWait = new();
        private readonly List<TunnelListener> _listeners = new();
        private int _tunnelId;
        /// <summary>Avvisi dei tunnel (porta occupata, indirizzo che non risponde…).</summary>
        public event Action<string>? TunnelMessage;

        public IReadOnlyList<TunnelSpec> ActiveTunnels { get { lock (_listeners) return _listeners.Select(l => l.Spec).ToList(); } }
        public int TunnelConnections => _tunnelHub.Count;

        /// <summary>Apre in ascolto 127.0.0.1:porta locale. "" = fatto, altrimenti il motivo.</summary>
        public string StartTunnel(TunnelSpec spec)
        {
            if (!PeerHas("tunnel")) return L.T("Il PC remoto ha una versione senza tunnel di porte: aggiornalo alla 0.5.");
            if (!InputAllowed) return L.T("Hai solo la visione del PC remoto: i tunnel non sono permessi.");
            lock (_listeners)
            {
                if (_listeners.Any(l => l.Spec.LocalPort == spec.LocalPort)) return L.F("La porta {0} è già usata da un altro tunnel.", spec.LocalPort);
                try { _listeners.Add(new TunnelListener(spec, OnTunnelClientAsync)); }
                catch (Exception ex) { return L.F("La porta {0} di questo dispositivo è occupata ({1}): scegline un'altra.", spec.LocalPort, ex.Message); }
            }
            SessionLog.Write($"TUNNEL 127.0.0.1:{spec.LocalPort} → {spec.Host}:{spec.Port} sul PC remoto" + (spec.Name.Length > 0 ? $" ({spec.Name})" : ""));
            return "";
        }

        public void StopTunnel(int localPort)
        {
            lock (_listeners)
            {
                foreach (var l in _listeners.Where(l => l.Spec.LocalPort == localPort).ToList()) { l.Dispose(); _listeners.Remove(l); }
            }
        }

        private async Task OnTunnelClientAsync(TunnelSpec spec, System.Net.Sockets.TcpClient client)
        {
            int id = Interlocked.Increment(ref _tunnelId);
            var tcs = new TaskCompletionSource<(bool ok, string why)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tunnelWait[id] = tcs;
            await SendAsync(new MsgWriter(Msg.TunnelOpen).I32(id).Str(spec.Host).I32(spec.Port).Build());
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(12)));
            _tunnelWait.TryRemove(id, out _);
            var (ok, why) = done == tcs.Task ? tcs.Task.Result : (false, L.T("il PC remoto non ha risposto"));
            if (!ok)
            {
                client.Dispose();
                TunnelMessage?.Invoke(L.F("Tunnel {0} → {1}:{2}: {3}.", spec.LocalPort, spec.Host, spec.Port, why));
                return;
            }
            _tunnelHub.Attach(id, client);
        }

        // cartella condivisa come disco del PC remoto
        private volatile DriveShare? _share;
        public string DriveMounted { get; private set; } = "";
        public string SharedFolder => _share?.Root ?? "";
        /// <summary>Montata (dove) o no (motivo, "" = tolta).</summary>
        public event Action<bool, string>? DriveChanged;

        public void ShareFolder(string folder, bool readOnly)
        {
            _share = new DriveShare(folder, readOnly);
            Send(new MsgWriter(Msg.DriveOpen).Str(_share.Name).Bool(readOnly).Build());
        }

        public void StopSharing()
        {
            _share = null;
            DriveMounted = "";
            Send(new MsgWriter(Msg.DriveClose).Str("").Build());
        }
        public void SetQuality(string q) => Send(new MsgWriter(Msg.SetQuality).Str(q).Build());
        public void Refresh() => Send(new MsgWriter(Msg.Refresh).Build());

        // ------------------------------------------------------------------ ingresso

        /// <summary>Il movimento del mouse va per UDP se c'è (al massimo 100 al secondo, conta l'ultima posizione).</summary>
        public void MouseMove(int x, int y)
        {
            _pendingMove = (x, y);
            if (Interlocked.Exchange(ref _movePending, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(10);
                Interlocked.Exchange(ref _movePending, 0);
                var (mx, my) = _pendingMove;
                byte[] m = new MsgWriter(Msg.MouseMove).I32(mx).I32(my).Build();
                var udp = Udp;
                if (udp != null) udp.SendDatagram(m); else await SendAsync(m);
            });
        }

        public void MouseButton(int button, bool down, int x, int y) =>
            Send(new MsgWriter(Msg.MouseButton).U8((byte)button).Bool(down).I32(x).I32(y).Build());

        public void Wheel(int dx, int dy) => Send(new MsgWriter(Msg.Wheel).I32(dx).I32(dy).Build());

        public void Key(int evdev, bool down)
        {
            if (evdev <= 0 || evdev > 65535) return;
            Send(new MsgWriter(Msg.Key).U16((ushort)evdev).Bool(down).Build());
        }

        public void ReleaseAll() => Send(new MsgWriter(Msg.ReleaseAll).Build());

        /// <summary>Testo scritto con la tastiera del telefono: arriva come caratteri, qualunque sia la tastiera di là.</summary>
        public void Text(string text) { if (text.Length > 0) Send(new MsgWriter(Msg.Text).Str(text).Build()); }

        /// <summary>Chiede al PC remoto i file che ha negli appunti.</summary>
        public void FetchRemoteFiles() => Send(new MsgWriter(Msg.FileFetch).Build());

        protected override bool FilesAllowed => State == AcceptState.Accepted && InputAllowed;

        protected override void OnFilesReceived(TransferPurpose purpose, string folder, List<string> topLevel, bool onClipboard) =>
            FilesReceived?.Invoke(folder, topLevel, onClipboard);

        /// <summary>Risposta alla richiesta di password (null = annulla: decide chi è davanti all'altro PC).</summary>
        public async Task AnswerPasswordAsync(PasswordChallenge c, string? password, bool remember)
        {
            if (password == null)
            {
                await SendAsync(new MsgWriter(Msg.AuthResponse).I32(0).Build());
                return;
            }
            byte[] key = await Task.Run(() => AccessPassword.Derive(password, c.Salt, c.Iterations));
            _pendingSaved = remember ? AccessPassword.Protect(P.Me, PeerNumber, c.Salt, key) : "";
            await SendAsync(new MsgWriter(Msg.AuthResponse).I32(32).Bytes(key).Build());
        }

        /// <summary>true dopo «Riavvia il PC remoto»: alla chiusura la plancia aspetta il PC e si ricollega.</summary>
        public bool RebootRequested { get; private set; }
        /// <summary>Il PC remoto ha detto che si aggiorna: il collegamento si riprende da solo.</summary>
        public bool UpdateRequested { get; private set; }
        private bool _throttleOff;

        /// <summary>Questo dispositivo sta per aggiornarsi: il PC remoto lo lascerà rientrare senza conferma.</summary>
        public void PrepareUpdate()
        {
            if (State == AcceptState.Accepted && PeerHas("resume")) Send(new MsgWriter(Msg.Resume).U8(1).I32(300).Build());
        }

        private bool _visible = true;
        /// <summary>Finestra ridotta a icona o nascosta: il PC remoto manda un fotogramma al secondo e ferma la qualità.</summary>
        public void SetVisible(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;
            if (PeerHas("visible")) Send(new MsgWriter(Msg.Visible).Bool(visible).Build());
        }
        /// <summary>Sul PC remoto è installato IP-TRX-CTRL.</summary>
        public bool PeerHasTrx { get; private set; }

        public void StartTrx() => Send(new MsgWriter(Msg.StartTrx).Build());

        public void Reboot()
        {
            RebootRequested = true;
            Send(new MsgWriter(Msg.Reboot).Build());
        }

        /// <summary>Combinazione di tasti premuti in ordine e rilasciati al contrario.</summary>
        public void Combo(params int[] keys)
        {
            foreach (int k in keys) Key(k, true);
            for (int i = keys.Length - 1; i >= 0; i--) Key(keys[i], false);
        }

        private async Task StatsLoopAsync()
        {
            while (!Cts.IsCancellationRequested)
            {
                try { await Task.Delay(1000, Cts.Token); } catch { break; }
                var now = DateTime.UtcNow;
                double s = Math.Max(0.1, (now - _lastStat).TotalSeconds);
                Fps = Interlocked.Exchange(ref _frames, 0) / s;
                long b = Interlocked.Read(ref Link.BytesReceived);
                KbitPerSecond = (b - _lastBytes) * 8 / 1000.0 / s;
                _lastBytes = b;
                _lastStat = now;
                if (_audioOn) CheckUdpAlive(TimeSpan.FromSeconds(4));
            }
        }

        protected override void OnClosing()
        {
            _player?.Dispose();
            _player = null;
            _micCap?.Dispose();
            _micCap = null;
            _share = null;
            _vdec?.Dispose();
            _vdec = null;
            if (_throttleOff) { _throttleOff = false; PowerThrottle.SessionEnded(); }
            lock (_listeners) { foreach (var l in _listeners) l.Dispose(); _listeners.Clear(); }
            _tunnelHub.CloseAll();
            var h = P.S.Find(PeerNumber);
            if (h != null && State == AcceptState.Accepted) { h.LastSeconds = HostSession.Duration(DateTime.UtcNow - Started); P.S.Save(); }
        }
    }
}
