using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Tunnel di porte: un programma di qui si collega a 127.0.0.1:porta e si ritrova collegato a un indirizzo
    /// raggiungibile dal PC remoto (per la radio: rigctld di Hamlib, flrig, l'interfaccia web della radio…).
    /// Tutto passa nel canale cifrato del collegamento; sul PC remoto la richiesta va accettata da chi lo comanda
    /// (permesso di mouse e tastiera) e dalle sue impostazioni.
    /// </summary>
    public sealed record TunnelSpec(int LocalPort, string Host, int Port, string Name)
    {
        public override string ToString() => $"{LocalPort}|{Host}|{Port}|{Name}";

        public static TunnelSpec? Parse(string s)
        {
            var p = s.Split('|');
            if (p.Length < 3 || !int.TryParse(p[0], out int lp) || !int.TryParse(p[2], out int rp)) return null;
            return new TunnelSpec(lp, p[1], rp, p.Length > 3 ? p[3] : "");
        }

        /// <summary>Tunnel pronti per i programmi della radio e simili.</summary>
        public static readonly TunnelSpec[] Presets =
        {
            new(4532, "127.0.0.1", 4532, "rigctld (Hamlib)"),
            new(4533, "127.0.0.1", 4533, "rotctld (rotore, Hamlib)"),
            new(12345, "127.0.0.1", 12345, "flrig"),
            new(7362, "127.0.0.1", 7362, "fldigi (XML-RPC)"),
            new(8080, "192.168.1.1", 80, "Interfaccia web (radio, router…)"),
        };
    }

    /// <summary>Le connessioni aperte di un tunnel, da una parte o dall'altra del collegamento.</summary>
    public sealed class TunnelHub
    {
        private sealed class Conn
        {
            public TcpClient Client = null!;
            public Channel<byte[]> Out = Channel.CreateUnbounded<byte[]>();
            public CancellationTokenSource Cts = new();
        }

        private readonly Func<byte[], Task> _send;
        private readonly ConcurrentDictionary<int, Conn> _conns = new();
        public int Count => _conns.Count;

        public TunnelHub(Func<byte[], Task> send) { _send = send; }

        /// <summary>Collega una connessione TCP al numero di tunnel: quello che arriva va all'altra parte e viceversa.</summary>
        public void Attach(int id, TcpClient client)
        {
            var c = new Conn { Client = client };
            _conns[id] = c;
            client.NoDelay = true;
            var stream = client.GetStream();
            _ = Task.Run(async () =>
            {
                var buf = new byte[32 * 1024];
                try
                {
                    int n;
                    while ((n = await stream.ReadAsync(buf, c.Cts.Token)) > 0)
                        await _send(new MsgWriter(Msg.TunnelData).I32(id).Bytes(buf, 0, n).Build());
                }
                catch { }
                if (_conns.TryRemove(id, out _))
                {
                    try { await _send(new MsgWriter(Msg.TunnelClose).I32(id).Build()); } catch { }
                    Drop(c);
                }
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var d in c.Out.Reader.ReadAllAsync(c.Cts.Token))
                        await stream.WriteAsync(d, c.Cts.Token);
                }
                catch { }
            });
        }

        public void OnData(int id, byte[] data)
        {
            if (_conns.TryGetValue(id, out var c)) c.Out.Writer.TryWrite(data);
        }

        public void OnClose(int id)
        {
            if (_conns.TryRemove(id, out var c)) Drop(c);
        }

        private static void Drop(Conn c)
        {
            c.Out.Writer.TryComplete();
            // si lascia il tempo di scrivere gli ultimi dati, poi si chiude
            _ = Task.Delay(500).ContinueWith(_ => { c.Cts.Cancel(); try { c.Client.Close(); } catch { } });
        }

        public void CloseAll()
        {
            foreach (var id in _conns.Keys.ToList()) OnClose(id);
        }
    }

    /// <summary>Visore: ascolta su 127.0.0.1:porta e apre un tunnel verso il PC remoto per ogni connessione.</summary>
    public sealed class TunnelListener : IDisposable
    {
        private readonly TcpListener _l;
        private readonly CancellationTokenSource _cts = new();
        public TunnelSpec Spec { get; }
        public int Connections;

        public TunnelListener(TunnelSpec spec, Func<TunnelSpec, TcpClient, Task> onClient)
        {
            Spec = spec;
            _l = new TcpListener(IPAddress.Loopback, spec.LocalPort);
            _l.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient c;
                    try { c = await _l.AcceptTcpClientAsync(_cts.Token); }
                    catch { break; }
                    Interlocked.Increment(ref Connections);
                    _ = Task.Run(() => onClient(spec, c));
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _l.Stop(); } catch { }
        }
    }
}
