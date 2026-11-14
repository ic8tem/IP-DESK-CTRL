using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Net;
using IpDeskCtrl.Net.Udp;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Parte comune delle due sessioni: canale TCP cifrato, ricezione, ping e collegamento UDP
    /// (perforazione del NAT come in IP-TRX-CTRL: STUN, scambio dei candidati sul canale cifrato,
    /// «bussate» reciproche; si usa UDP solo quando entrambi confermano di ricevere).
    /// </summary>
    public abstract partial class SessionBase
    {
        protected readonly Peer P;
        public SecureLink Link { get; }
        protected CancellationTokenSource Cts = new();
        private int _closed;

        private UdpLink? _udp;
        private bool _udpMine, _udpPeer;
        private List<IPEndPoint>? _peerCands;
        private int _peerDelta;
        private readonly SemaphoreSlim _udpLock = new(1, 1);

        public string PeerNumber => Link.PeerNumber;
        public string PeerOs { get; protected set; } = "";
        public string PeerName { get; protected set; } = "";
        public string PeerVersion { get; protected set; } = "";
        public DateTime Started { get; } = DateTime.UtcNow;
        public double RttMs { get; private set; } = double.NaN;
        public bool UdpActive => _udpMine && _udpPeer && _udp?.Remote != null;
        /// <summary>Strada del collegamento come la mostra la plancia: «TCP», «UDP + TCP» o «solo UDP».</summary>
        public string Route => Link.IsUdp ? "solo UDP" : UdpActive ? "UDP + TCP" : "TCP";
        public bool IsClosed => _closed != 0;

        public event Action<string>? Closed;

        protected SessionBase(Peer p, SecureLink link)
        {
            P = p;
            Link = link;
            if (link.UdpTransport != null)
            {
                // collegamento «solo UDP»: audio e mouse usano lo stesso socket, già perforato da entrambi i lati
                var shared = link.UdpTransport;
                shared.DatagramReceived += OnDatagram;
                shared.AudioReceived += OnUdpAudio;
                _udp = shared;
                _udpMine = _udpPeer = true;
                UdpLog(L.T("collegamento solo UDP: audio e mouse sullo stesso socket"));
            }
        }

        protected UdpLink? Udp => UdpActive ? _udp : null;

        protected async Task SendAsync(byte[] msg)
        {
            try { await Link.SendAsync(msg, Cts.Token); }
            catch (Exception) when (!IsClosed) { Close(L.T("collegamento interrotto")); }
            catch { }
        }

        protected void Send(byte[] msg) => _ = SendAsync(msg);

        /// <summary>Ciclo di ricezione; finisce quando il collegamento si chiude.</summary>
        protected async Task ReceiveLoopAsync()
        {
            string reason = L.T("collegamento chiuso dall'altro PC");
            try
            {
                while (!Cts.IsCancellationRequested)
                {
                    byte[]? m = await Link.ReceiveAsync(Cts.Token);
                    if (m == null || m.Length == 0) break;
                    var r = new MsgReader(m);
                    switch (r.Type)
                    {
                        case Msg.Ping: Send(new MsgWriter(Msg.Pong).I64(r.I64()).Build()); break;
                        case Msg.Pong: RttMs = (DateTime.UtcNow.Ticks - r.I64()) / 10000.0; break;
                        case Msg.Bye: reason = r.Str(); Cts.Cancel(); break;
                        case Msg.UdpOffer: { string c = r.Str(); int d = r.I32(); _ = Task.Run(() => OnUdpOfferAsync(c, d)); break; }
                        case Msg.UdpOn: _udpPeer = r.U8() == 1; UdpLog(L.F("l'altro PC {0} su UDP", (_udpPeer ? "riceve" : L.T("non riceve")))); break;
                        case Msg.Clipboard: OnRemoteClipboard(r.Str()); break;
                        case Msg.FileOffer:
                        case Msg.FileChunk:
                        case Msg.FileDone:
                        case Msg.FileCancel:
                            await OnFileMessageAsync(r);
                            break;
                        default: Handle(r); break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!IsClosed) { reason = L.T("collegamento interrotto (") + ex.Message + ")"; }
            catch { }
            Close(reason);
        }

        protected abstract void Handle(MsgReader r);

        protected async Task PingLoopAsync()
        {
            while (!Cts.IsCancellationRequested)
            {
                await SendAsync(new MsgWriter(Msg.Ping).I64(DateTime.UtcNow.Ticks).Build());
                try { await Task.Delay(2000, Cts.Token); } catch { break; }
            }
        }

        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try
            {
                using var t = new CancellationTokenSource(1000);
                Link.SendAsync(new MsgWriter(Msg.Bye).Str(reason).Build(), t.Token).Wait(1000);
            }
            catch { }
            Cts.Cancel();
            AbortTransfers();
            try { OnClosing(); } catch (Exception ex) { ErrorLog.Write(ex.ToString()); }
            var u = Interlocked.Exchange(ref _udp, null);
            if (u != null && u != Link.UdpTransport) u.Dispose();    // quello del collegamento «solo UDP» lo chiude Link
            Link.Dispose();
            SessionLog.Write($"SESSIONE fine con {Identity.Format(PeerNumber)}: {reason}");
            Closed?.Invoke(reason);
        }

        protected virtual void OnClosing() { }

        protected virtual void OnDatagram(byte[] m) { }
        protected virtual void OnUdpAudio(uint seq, byte[] data) { }

        // ------------------------------------------------------------------ UDP

        private void UdpLog(string s) => SessionLog.Write("UDP " + s);

        /// <summary>Prepara il socket UDP e manda i propri candidati all'altro PC.</summary>
        protected async Task OfferUdpAsync(int localPort, IPEndPoint? upnpEndpoint)
        {
            await _udpLock.WaitAsync();
            try
            {
                if (_udp != null || IsClosed) return;
                UdpLink link;
                try { link = new UdpLink(Link.UdpKey, localPort); }
                catch { link = new UdpLink(Link.UdpKey, 0); }
                link.Log += SessionLog.Write;
                link.DatagramReceived += OnDatagram;
                link.AudioReceived += OnUdpAudio;
                _udp = link;
                var cands = new List<string>();
                var nat = await Stun.DiscoverAsync(link, Stun.DefaultServers, SessionLog.Write, Cts.Token);
                if (nat.Public != null) cands.Add(nat.Public.ToString());
                if (upnpEndpoint != null && upnpEndpoint.Port > 0 && link.LocalPort == localPort) cands.Add(upnpEndpoint.ToString());
                string lan = Peer.LanIp();
                if (lan.Length > 0) cands.Add($"{lan}:{link.LocalPort}");
                cands = cands.Distinct().ToList();
                UdpLog(L.F("NAT {0} · candidati {1}", nat.Type, string.Join(" ", cands)));
                await SendAsync(new MsgWriter(Msg.UdpOffer).Str(string.Join(";", cands)).I32(nat.PortDelta).Build());
            }
            catch (Exception ex) { UdpLog(L.T("errore: ") + ex.Message); }
            finally { _udpLock.Release(); }
            if (_peerCands != null) await PunchAsync();
        }

        private async Task OnUdpOfferAsync(string cands, int delta)
        {
            var list = new List<IPEndPoint>();
            foreach (var c in cands.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (IPEndPoint.TryParse(c, out var ep)) list.Add(ep);
            _peerCands = list;
            _peerDelta = delta;
            UdpLog(L.F("candidati dell'altro PC: {0}", cands));
            if (_udp == null) await OfferUdpAsync(0, null);
            else await PunchAsync();
        }

        private async Task PunchAsync()
        {
            var link = _udp;
            var cands = _peerCands;
            if (link == null || cands == null || cands.Count == 0) return;
            bool ok = await link.PunchAsync(cands, _peerDelta, TimeSpan.FromSeconds(10), Cts.Token);
            _udpMine = ok;
            UdpLog(ok ? L.F("perforazione riuscita verso {0}", link.Remote) : L.T("perforazione non riuscita: resta solo TCP"));
            await SendAsync(new MsgWriter(Msg.UdpOn).U8((byte)(ok ? 1 : 0)).Build());
        }

        /// <summary>Se da UDP non arriva più nulla da un po', lo si dichiara spento e si torna a TCP.</summary>
        protected void CheckUdpAlive(TimeSpan silence)
        {
            var u = _udp;
            if (u == null || !_udpMine) return;
            if (DateTime.UtcNow - u.LastReceived > silence)
            {
                _udpMine = false;
                UdpLog(L.F("nessun pacchetto UDP da {0:F0} s: torno a TCP", silence.TotalSeconds));
                Send(new MsgWriter(Msg.UdpOn).U8(0).Build());
            }
        }
    }
}
