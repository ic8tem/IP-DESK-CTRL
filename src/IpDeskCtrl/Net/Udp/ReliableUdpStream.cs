using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IpDeskCtrl.Net.Udp
{
    /// <summary>
    /// Flusso di byte affidabile e ordinato sopra <see cref="UdpLink"/>, per quando TCP non passa tra i due PC
    /// (entrambi dietro NAT senza UPnP). Sopra ci gira lo stesso handshake cifrato di <see cref="SecureLink"/>.
    ///
    ///   STREAM_DATA  seq(u32) | dati (al massimo 1150 byte: sta in un datagramma anche su reti con MTU basso)
    ///   STREAM_ACK   prossimo atteso(u32) | mappa(u64): bit i = ricevuto il segmento «atteso + 1 + i»
    ///   STREAM_FIN   seq finale(u32): dopo l'ultimo byte il flusso è chiuso
    ///
    /// Ritrasmissione con RTO adattivo (RFC 6298, 100 ms – 3 s), ritrasmissione rapida dopo 3 ACK che
    /// saltano un segmento, finestra di congestione (partenza lenta, poi crescita lineare, dimezzata a ogni
    /// perdita). Il collegamento è dichiarato morto dopo 25 s senza pacchetti o 40 tentativi dello stesso segmento.
    /// </summary>
    public sealed class ReliableUdpStream : Stream
    {
        public const int Mss = 1150;
        private const int MaxBuffered = 4 * 1024 * 1024;   // oltre, Write aspetta (contropressione)
        private const int RecvWindow = 4096;               // segmenti fuori ordine tenuti da parte
        private const int MaxTries = 40;
        private const int BurstPerTick = 128;              // segmenti nuovi al massimo ogni giro (circa 5 ms)
        private static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(25);

        private sealed class Seg
        {
            public byte[] Packet = Array.Empty<byte>();     // seq | dati, pronto da spedire
            public int Length;
            public bool Sent;
            public long SentAt, Deadline;                  // ms dello Stopwatch
            public int Tries, Skips;
        }

        private readonly UdpLink _link;
        private readonly object _lock = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
        private readonly SemaphoreSlim _space = new(0, int.MaxValue);
        private readonly SemaphoreSlim _readable = new(0, int.MaxValue);

        // invio
        private readonly Dictionary<uint, Seg> _segs = new();
        private uint _nextSeq = 1, _una = 1, _nextToSend = 1, _recover = 1;
        private int _buffered;
        private double _cwnd = 16, _ssthresh = 1024;
        private double _srtt = -1, _rttvar, _rto = 400;

        // ricezione
        private uint _rcvNext = 1;
        private readonly Dictionary<uint, byte[]> _ooo = new();
        private readonly Queue<byte[]> _ready = new();
        private byte[]? _cur;
        private int _curPos;
        private uint? _finAt;
        private bool _eof;

        private volatile string? _dead;
        private int _disposed;

        public long Retransmits;
        public event Action<string>? Log;

        public ReliableUdpStream(UdpLink link)
        {
            _link = link;
            _link.StreamPacket += OnPacket;
            _ = Task.Run(SenderLoopAsync);
        }

        private void L(string s) => Log?.Invoke("UDP flusso " + s);

        /// <summary>a viene prima di b (con il giro dei numeri a 32 bit).</summary>
        private static bool Before(uint a, uint b) => (int)(a - b) < 0;

        private static void PutU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static uint GetU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        // =================================================================== ricezione dei pacchetti

        private void OnPacket(byte type, byte[] p)
        {
            try
            {
                switch (type)
                {
                    case UdpLink.StreamData: OnData(p); break;
                    case UdpLink.StreamAck: OnAck(p); break;
                    case UdpLink.StreamFin:
                        if (p.Length >= 4)
                        {
                            lock (_lock)
                            {
                                _finAt = GetU32(p, 0);
                                if (_rcvNext == _finAt) _eof = true;
                            }
                            _readable.Release();
                        }
                        break;
                }
            }
            catch (Exception ex) { L("pacchetto scartato: " + ex.Message); }
        }

        private void OnData(byte[] p)
        {
            if (p.Length < 4) return;
            uint seq = GetU32(p, 0);
            bool readable = false;
            byte[] ack;
            lock (_lock)
            {
                if (Before(seq, _rcvNext)) { }                       // doppione: si riconferma e basta
                else if (seq == _rcvNext)
                {
                    _ready.Enqueue(p.AsSpan(4).ToArray());
                    _rcvNext++;
                    while (_ooo.Remove(_rcvNext, out var next)) { _ready.Enqueue(next); _rcvNext++; }
                    if (_finAt != null && _rcvNext == _finAt) _eof = true;
                    readable = true;
                }
                else if (seq - _rcvNext < RecvWindow && !_ooo.ContainsKey(seq))
                    _ooo[seq] = p.AsSpan(4).ToArray();

                ack = new byte[12];
                PutU32(ack, 0, _rcvNext);
                ulong bits = 0;
                if (_ooo.Count > 0)
                    for (int i = 0; i < 64; i++)
                        if (_ooo.ContainsKey(_rcvNext + 1 + (uint)i)) bits |= 1UL << i;
                for (int i = 0; i < 8; i++) ack[4 + i] = (byte)(bits >> (56 - 8 * i));
            }
            _link.SendStream(UdpLink.StreamAck, ack);
            if (readable) _readable.Release();
        }

        private void OnAck(byte[] p)
        {
            if (p.Length < 12) return;
            uint una = GetU32(p, 0);
            ulong bits = 0;
            for (int i = 0; i < 8; i++) bits = bits << 8 | p[4 + i];
            long now = _clock.ElapsedMilliseconds;
            int freed = 0;
            List<byte[]>? fast = null;
            lock (_lock)
            {
                if (Before(_nextToSend, una)) return;                 // conferma di qualcosa mai spedito: falsa
                // conferma cumulativa
                for (uint s = _una; Before(s, una); s++)
                    if (_segs.Remove(s, out var seg)) { Acked(seg, now); freed++; }
                // conferme selettive
                uint highest = una;
                for (int i = 0; i < 64; i++)
                {
                    if ((bits & (1UL << i)) == 0) continue;
                    uint s = una + 1 + (uint)i;
                    if (!Before(s, _nextToSend)) break;
                    if (_segs.Remove(s, out var seg)) { Acked(seg, now); freed++; }
                    highest = s;
                }
                // ritrasmissione rapida: segmenti saltati da 3 conferme
                for (uint s = una; Before(s, highest); s++)
                {
                    if (!_segs.TryGetValue(s, out var seg) || !seg.Sent) continue;
                    if (++seg.Skips != 3) continue;
                    if (!Before(s, _recover))
                    {
                        _ssthresh = Math.Max(_cwnd / 2, 4);
                        _cwnd = _ssthresh;
                        _recover = _nextToSend;
                    }
                    seg.Tries++;
                    seg.SentAt = now;
                    seg.Deadline = now + (long)_rto;
                    Interlocked.Increment(ref Retransmits);
                    (fast ??= new List<byte[]>()).Add(seg.Packet);
                }
                while (_una != _nextToSend && !_segs.ContainsKey(_una)) _una++;
            }
            if (fast != null) foreach (var pk in fast) _link.SendStream(UdpLink.StreamData, pk);
            if (freed > 0) { _space.Release(); _wake.Release(); }
        }

        /// <summary>Segmento confermato (sotto lock): misura il tempo di andata e ritorno e allarga la finestra.</summary>
        private void Acked(Seg seg, long now)
        {
            _buffered -= seg.Length;
            if (seg.Tries == 1)
            {
                double r = Math.Max(1, now - seg.SentAt);
                if (_srtt < 0) { _srtt = r; _rttvar = r / 2; }
                else
                {
                    _rttvar = 0.75 * _rttvar + 0.25 * Math.Abs(_srtt - r);
                    _srtt = 0.875 * _srtt + 0.125 * r;
                }
                _rto = Math.Clamp(_srtt + Math.Max(10, 4 * _rttvar), 100, 3000);
            }
            if (_cwnd < _ssthresh) _cwnd += 1;
            else _cwnd += 1 / _cwnd;
            if (_cwnd > 2048) _cwnd = 2048;
        }

        // =================================================================== invio

        private async Task SenderLoopAsync()
        {
            var ct = _cts.Token;
            var toSend = new List<byte[]>();
            while (!ct.IsCancellationRequested)
            {
                try { await _wake.WaitAsync(5, ct); } catch { break; }
                long now = _clock.ElapsedMilliseconds;
                toSend.Clear();
                string? fail = null;
                lock (_lock)
                {
                    bool timedOut = false;
                    int inflight = 0;
                    for (uint s = _una; s != _nextToSend; s++)
                    {
                        if (!_segs.TryGetValue(s, out var seg)) continue;
                        inflight++;
                        if (now < seg.Deadline) continue;
                        if (seg.Tries >= MaxTries) { fail = $"segmento {s} non confermato dopo {MaxTries} tentativi"; break; }
                        seg.Tries++;
                        seg.SentAt = now;
                        timedOut = true;
                        toSend.Add(seg.Packet);
                        Interlocked.Increment(ref Retransmits);
                    }
                    if (timedOut)
                    {
                        _ssthresh = Math.Max(_cwnd / 2, 4);
                        _cwnd = Math.Max(2, _cwnd / 2);
                        _rto = Math.Min(_rto * 2, 3000);
                        _recover = _nextToSend;
                    }
                    foreach (var kv in _segs)
                        if (kv.Value.Sent && kv.Value.Deadline <= now) kv.Value.Deadline = now + (long)_rto;
                    int burst = 0;
                    while (fail == null && _nextToSend != _nextSeq && inflight < (int)_cwnd && burst < BurstPerTick)
                    {
                        if (_segs.TryGetValue(_nextToSend, out var seg))
                        {
                            seg.Sent = true;
                            seg.Tries = 1;
                            seg.SentAt = now;
                            seg.Deadline = now + (long)_rto;
                            toSend.Add(seg.Packet);
                            inflight++;
                            burst++;
                        }
                        _nextToSend++;
                    }
                }
                foreach (var pk in toSend) _link.SendStream(UdpLink.StreamData, pk);
                if (fail == null && DateTime.UtcNow - _link.LastReceived > DeadAfter)
                    fail = $"nessun pacchetto UDP da {DeadAfter.TotalSeconds:F0} s";
                if (fail != null) { Kill(fail); break; }
            }
        }

        private void Kill(string why)
        {
            if (_dead != null) return;
            _dead = why;
            L("interrotto: " + why);
            _readable.Release();
            _space.Release();
        }

        private void Enqueue(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                byte[] pk = new byte[4 + count];
                PutU32(pk, 0, _nextSeq);
                Buffer.BlockCopy(data, offset, pk, 4, count);
                _segs[_nextSeq] = new Seg { Packet = pk, Length = count };
                _nextSeq++;
                _buffered += count;
            }
            _wake.Release();
        }

        private bool Full { get { lock (_lock) return _buffered > MaxBuffered; } }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            byte[] data = buffer.ToArray();
            int o = 0;
            while (o < data.Length)
            {
                while (Full)
                {
                    if (_dead != null) throw new IOException("Collegamento UDP interrotto: " + _dead);
                    await _space.WaitAsync(200, ct);
                }
                if (_dead != null) throw new IOException("Collegamento UDP interrotto: " + _dead);
                if (_disposed != 0) throw new ObjectDisposedException(nameof(ReliableUdpStream));
                int n = Math.Min(Mss, data.Length - o);
                Enqueue(data, o, n);
                o += n;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), ct).AsTask();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

        // =================================================================== lettura

        /// <summary>Copia i dati pronti (sotto lock); -1 = niente di pronto, 0 = fine del flusso.</summary>
        private int TryRead(Memory<byte> buffer)
        {
            lock (_lock)
            {
                if (_cur == null && _ready.Count > 0) { _cur = _ready.Dequeue(); _curPos = 0; }
                if (_cur != null)
                {
                    int n = Math.Min(buffer.Length, _cur.Length - _curPos);
                    _cur.AsSpan(_curPos, n).CopyTo(buffer.Span);
                    _curPos += n;
                    if (_curPos >= _cur.Length) _cur = null;
                    return n;
                }
                if (_eof) return 0;
            }
            if (_dead != null) throw new IOException("Collegamento UDP interrotto: " + _dead);
            if (_disposed != 0) return 0;
            return -1;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length == 0) return 0;
            while (true)
            {
                int n = TryRead(buffer);
                if (n >= 0) return n;
                await _readable.WaitAsync(1000, ct);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(new Memory<byte>(buffer, offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

        // =================================================================== Stream

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Tempo medio di andata e ritorno misurato dal trasporto (ms; NaN se non ancora misurato).</summary>
        public double SmoothedRttMs { get { lock (_lock) return _srtt < 0 ? double.NaN : _srtt; } }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) { base.Dispose(disposing); return; }
            if (disposing)
            {
                // lascia partire gli ultimi dati (per esempio il saluto di chiusura) per al massimo un secondo
                var sw = Stopwatch.StartNew();
                while (_dead == null && sw.ElapsedMilliseconds < 1000)
                {
                    lock (_lock) if (_segs.Count == 0) break;
                    Thread.Sleep(10);
                }
                uint fin;
                lock (_lock) fin = _nextSeq;
                byte[] f = new byte[4];
                PutU32(f, 0, fin);
                for (int i = 0; i < 3; i++) _link.SendStream(UdpLink.StreamFin, f);
                _link.StreamPacket -= OnPacket;
                _cts.Cancel();
                _readable.Release();
                _space.Release();
            }
            base.Dispose(disposing);
        }
    }
}
