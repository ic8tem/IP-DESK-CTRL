using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace IpDeskCtrl.Net
{
    /// <summary>
    /// Ultima strada quando tra i due PC non passa niente (CGNAT da entrambe le parti, firewall chiusi): i byte del
    /// collegamento cifrato passano dal broker MQTT, su due argomenti usa e getta. Il broker vede solo dati cifrati
    /// end-to-end (la stretta di mano di <see cref="SecureLink"/> verifica le chiavi dei due PC come sempre).
    /// È lenta e i broker pubblici limitano il traffico: il PC comandato manda immagini più leggere.
    /// </summary>
    public sealed class MqttRelayStream : Stream
    {
        private const int Chunk = 48 * 1024;
        private readonly IMqttClient _c;
        private readonly string _outTopic;
        private readonly Channel<byte[]> _in = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly Dictionary<long, byte[]> _early = new();
        private byte[]? _cur;
        private int _curPos;
        private long _sendSeq, _recvSeq;
        private volatile bool _closed;
        // lato visore: si scrive solo dopo il primo pacchetto del PC comandato, che così è di sicuro in ascolto
        private readonly TaskCompletionSource _peerHeard = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Broker { get; }

        private MqttRelayStream(IMqttClient c, string broker, string outTopic)
        {
            _c = c; Broker = broker; _outTopic = outTopic;
        }

        /// <summary>
        /// Apre il relè: session = identificativo casuale scambiato sul broker, viewer = lato visore.
        /// Prova i broker in ordine (o solo quello indicato) e restituisce il primo che risponde.
        /// </summary>
        public static async Task<MqttRelayStream> OpenAsync(IEnumerable<string> brokers, string session, bool viewer, CancellationToken ct)
        {
            Exception? last = null;
            foreach (var b in brokers)
            {
                var c = new MqttFactory().CreateMqttClient();
                try
                {
                    string baseTopic = "ipdk/relay/" + session;
                    var s = new MqttRelayStream(c, b, baseTopic + (viewer ? "/v" : "/h"));
                    if (!viewer) s._peerHeard.TrySetResult();
                    string inTopic = baseTopic + (viewer ? "/h" : "/v");
                    c.ApplicationMessageReceivedAsync += e =>
                    {
                        if (e.ApplicationMessage.Topic == inTopic) s.OnPacket(e.ApplicationMessage.PayloadSegment.ToArray());
                        return Task.CompletedTask;
                    };
                    c.DisconnectedAsync += _ => { s.Fail(); return Task.CompletedTask; };
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await Mqtt.ConnectAsync(c, b, Mqtt.NewClientId(), t.Token, 20);
                    await c.SubscribeAsync(Mqtt.Subscribe(new[] { inTopic }), t.Token);
                    return s;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    last = ex;
                    try { c.Dispose(); } catch { }
                }
            }
            ct.ThrowIfCancellationRequested();
            throw new IOException("nessun broker raggiungibile per il relè" + (last != null ? ": " + last.Message : ""));
        }

        // ogni pacchetto: 8 byte di numero progressivo + dati; i doppioni (QoS 1) si scartano, l'ordine si ricompone
        private void OnPacket(byte[] p)
        {
            if (p.Length < 8) return;
            long seq = BitConverter.ToInt64(p, 0);
            _peerHeard.TrySetResult();
            byte[] data = p.AsSpan(8).ToArray();
            lock (_early)
            {
                if (seq < _recvSeq) return;
                _early[seq] = data;
                while (_early.Remove(_recvSeq, out var next))
                {
                    _recvSeq++;
                    if (next.Length == 0) { _in.Writer.TryComplete(); return; }
                    _in.Writer.TryWrite(next);
                }
                if (_early.Count > 4096) Fail();
            }
        }

        private void Fail() { _closed = true; _in.Writer.TryComplete(); _peerHeard.TrySetException(new IOException("relè chiuso")); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (_cur == null || _curPos >= _cur.Length)
            {
                if (!await _in.Reader.WaitToReadAsync(ct)) return 0;
                if (_in.Reader.TryRead(out var b)) { _cur = b; _curPos = 0; }
            }
            int n = Math.Min(buffer.Length, _cur.Length - _curPos);
            _cur.AsMemory(_curPos, n).CopyTo(buffer);
            _curPos += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (_closed) throw new IOException("relè chiuso");
            if (data.Length == 0) return;
            await _peerHeard.Task.WaitAsync(ct);
            await _sendLock.WaitAsync(ct);
            try
            {
                for (int off = 0; off < data.Length; off += Chunk)
                {
                    int n = Math.Min(Chunk, data.Length - off);
                    var p = new byte[8 + n];
                    BitConverter.GetBytes(_sendSeq++).CopyTo(p, 0);
                    data.Slice(off, n).CopyTo(p.AsMemory(8));
                    await _c.PublishAsync(Mqtt.Message(_outTopic, p, false), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fail(); throw new IOException("relè interrotto: " + ex.Message, ex); }
            finally { _sendLock.Release(); }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override void Flush() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                // pacchetto vuoto = fine del flusso per l'altro lato
                try { if (_c.IsConnected) _c.PublishAsync(Mqtt.Message(_outTopic, BitConverter.GetBytes(_sendSeq++), false)).Wait(1500); } catch { }
                Fail();
                try { _c.DisconnectAsync().Wait(1500); } catch { }
                try { _c.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
