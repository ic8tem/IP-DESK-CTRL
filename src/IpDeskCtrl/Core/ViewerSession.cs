using System;
using System.Collections.Generic;
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

        public ViewerSession(Peer p, SecureLink link) : base(p, link) { }

        public void Run(CancellationToken life)
        {
            life.Register(() => Close("programma chiuso"));
            _ = Task.Run(ReceiveLoopAsync);
            _ = Task.Run(async () =>
            {
                await SendAsync(new MsgWriter(Msg.Hello).Str(AppVersion.Tag).Str(AppVersion.Os).Str(Environment.MachineName).Build());
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
                    break;
                case Msg.Accept:
                {
                    State = (AcceptState)r.U8();
                    StateText = r.Str();
                    if (State == AcceptState.Accepted)
                    {
                        InputAllowed = StateText.Length == 0;
                        var h = P.S.Touch(PeerNumber, true, Convert.ToHexString(Link.PeerKey), PeerOs);
                        if (_pendingSaved != null) { h.SavedPassword = _pendingSaved; P.S.Save(); }
                        Send(new MsgWriter(Msg.SetQuality).Str(P.S.Quality).Build());
                        SetAudio(P.S.PlayAudio);
                        _ = Task.Run(ClipboardLoopAsync);
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
                        Send(new MsgWriter(Msg.AuthResponse).I32(32).Bytes(AccessPassword.Proof(key, c.Nonce)).Build());
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
            await SendAsync(new MsgWriter(Msg.AuthResponse).I32(32).Bytes(AccessPassword.Proof(key, c.Nonce)).Build());
        }

        /// <summary>true dopo «Riavvia il PC remoto»: alla chiusura la plancia aspetta il PC e si ricollega.</summary>
        public bool RebootRequested { get; private set; }

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
            var h = P.S.Find(PeerNumber);
            if (h != null && State == AcceptState.Accepted) { h.LastSeconds = HostSession.Duration(DateTime.UtcNow - Started); P.S.Save(); }
        }
    }
}
