using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Net;
using IpDeskCtrl.Net.Udp;

namespace IpDeskCtrl.Core
{
    /// <summary>Errore da mostrare all'utente così com'è.</summary>
    public sealed class PeerException : Exception { public PeerException(string m) : base(m) { } }

    /// <summary>Richiesta in arrivo, da far accettare all'utente.</summary>
    public sealed record IncomingInfo(string Number, string Alias, string Os, string PcName, string Version, string Fingerprint, bool Known);
    /// <summary>Why: motivo del rifiuto da mostrare all'altro PC ("" = «richiesta rifiutata»).</summary>
    public sealed record IncomingDecision(bool Accept, bool AllowInput, bool AlwaysAllow, string Why = "");

    /// <summary>
    /// Il «nodo» di questo PC, sempre attivo in entrambi i ruoli, senza che l'utente scelga nulla:
    ///   • come HOST apre una porta (TCP e UDP con lo stesso numero) sul router con UPnP, la verifica
    ///     dall'esterno, lascia indirizzo e porta firmati sul canale MQTT del proprio numero e ascolta;
    ///   • come VISORE cerca il numero sui broker, si collega diretto (internet o rete locale) e, se non ci
    ///     riesce, chiede sul broker il collegamento inverso: è l'host a collegarsi alla porta del visore;
    ///     se anche questo non passa, scambia i candidati UDP sul broker e fa tutto su UDP (perforazione
    ///     del NAT + trasporto affidabile), con lo stesso handshake cifrato.
    /// Ruolo e sicurezza li decide l'handshake di <see cref="SecureLink"/>.
    /// </summary>
    public sealed class Peer : IDisposable
    {
        public Identity Me { get; }
        public AppSettings S { get; }

        private readonly CancellationTokenSource _life = new();
        private PortManager? _pm;
        private TcpListener? _listener;
        private int _extPort, _localPort;
        private bool _reachable;
        private IPAddress? _publicIp;
        private HostRendezvous? _rdv;
        private Task? _rdvTask;
        private OutboundProbe.Result? _outbound;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<SecureLink>> _waitReverse = new();
        private readonly ConcurrentDictionary<string, (string number, TaskCompletionSource<UdpCandidates> tcs)> _waitUdp = new();
        private readonly ConcurrentDictionary<string, DateTime> _seenRequests = new();
        private readonly ConcurrentDictionary<string, bool> _dialing = new();
        private readonly SemaphoreSlim _portLock = new(1, 1);
        private HostSession? _host;
        private readonly object _hostLock = new();

        public string Status { get; private set; } = "Avvio…";
        public string Detail { get; private set; } = "";
        public bool Ready { get; private set; }
        public int Brokers => _rdv?.ConnectedBrokers ?? 0;
        public bool UpnpOk => _pm?.UpnpAvailable == true;
        public bool Reachable => _reachable;
        public int ExternalPort => _extPort;
        public int LocalPort => _localPort;
        public IPAddress? PublicIp => _publicIp;
        public HostSession? CurrentHost { get { lock (_hostLock) return _host; } }
        /// <summary>
        /// Windows con il servizio: questo programma si collega solo ad altri PC. Indirizzo, richieste in arrivo
        /// e collegamenti verso questo PC li gestisce il servizio (che ha lo stesso numero).
        /// </summary>
        public bool ViewerOnly { get; init; }

        public event Action? StatusChanged;
        public event Action<HostSession>? HostStarted;
        public event Action<HostSession, string>? HostEnded;
        /// <summary>Chiede all'utente se accettare; impostato dall'interfaccia.</summary>
        public Func<IncomingInfo, CancellationToken, Task<IncomingDecision>>? AskAccept;

        public Peer(Identity me, AppSettings s)
        {
            Me = me;
            S = s;
        }

        private void SetStatus(string status, string detail = "")
        {
            Status = status;
            Detail = detail;
            StatusChanged?.Invoke();
        }

        private static void Log(string s) => SessionLog.Write(s);

        // =================================================================== avvio

        public void Start() => _ = Task.Run(() => StartAsync(_life.Token));

        private async Task StartAsync(CancellationToken ct)
        {
            try
            {
                SessionLog.Write($"AVVIO IP-DESK-CTRL {AppVersion.Text} · {AppVersion.Os} · numero {Identity.Format(Me.Number)}");
                _rdv = new HostRendezvous(Me, S.MqttBrokers) { Log = Log, Publish = !ViewerOnly, AcceptRequests = !ViewerOnly };
                _rdv.RequestReceived += r => _ = Task.Run(() => OnRequestAsync(r, ct));
                _rdv.NoteReceived += n => _ = Task.Run(() => OnNoteAsync(n, ct));
                var rdv = _rdv;
                _rdvTask = Task.Run(() => rdv.RunAsync(ct));

                SetStatus("Apro la porta sul router…");
                await _portLock.WaitAsync(ct);
                try { await OpenPortAsync(null, ct); }
                finally { _portLock.Release(); }
                Ready = true;
                Announce();
                ShowNetworkStatus();

                _ = Task.Run(async () =>
                {
                    _outbound = await OutboundProbe.RunAsync(Log, ct);
                }, ct);
                _ = Task.Run(() => WatchAsync(ct), ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                SetStatus("Errore di rete: " + ex.Message);
            }
        }

        private void ShowNetworkStatus()
        {
            string where = _publicIp == null ? "IP pubblico sconosciuto" : $"IP {_publicIp}";
            if (ViewerOnly) { SetStatus("Pronto: i collegamenti a questo PC li riceve il servizio di Windows", where); return; }
            if (_extPort == 0) SetStatus("Pronto solo per collegarti ad altri PC", "nessuna porta in ascolto");
            else if (_reachable) SetStatus("Pronto: raggiungibile da internet", $"{where} · porta {_extPort}" + (UpnpOk ? " (UPnP)" : ""));
            else SetStatus("Pronto (porta non raggiungibile da internet: si useranno il collegamento inverso e la rete locale)",
                           $"{where} · porta {_extPort}" + (UpnpOk ? " (UPnP)" : " · UPnP non disponibile sul router"));
        }

        /// <summary>Ogni 5 minuti controlla l'IP pubblico; se cambia lo ripubblica.</summary>
        private async Task WatchAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch { break; }
                try
                {
                    var ip = _pm != null ? await _pm.RefreshExternalIpAsync(ct) : await PortManager.GetPublicIpAsync(ct);
                    if (ip != null && !ip.Equals(_publicIp))
                    {
                        Log($"PORTE IP pubblico cambiato: {_publicIp} → {ip}");
                        _publicIp = ip;
                        Announce();
                        ShowNetworkStatus();
                    }
                }
                catch { }
            }
        }

        // =================================================================== porta

        /// <summary>
        /// Apre la porta: con UPnP una porta casuale (TCP e UDP con lo stesso numero) verificata dall'esterno
        /// (ifconfig.co, portchecker.io); se non è raggiungibile ne prova un'altra (fino a 6). Senza UPnP usa
        /// una porta locale: funziona con DMZ, inoltro manuale o sulla stessa rete locale.
        /// preferred: porte esterne da provare per prime (rete del visore che lascia uscire solo 443, 80…).
        /// </summary>
        private async Task OpenPortAsync(List<int>? preferred, CancellationToken ct)
        {
            await ClosePortAsync();
            _pm ??= new PortManager { Log = Log };
            if (!_pm.UpnpAvailable) await _pm.DiscoverAsync(ct);
            _publicIp = _pm.ExternalIp ?? await PortManager.GetPublicIpAsync(ct);
            bool upnp = _pm.UpnpAvailable;

            if (preferred is { Count: > 0 } && upnp)
            {
                foreach (int ext in preferred)
                {
                    int inner = _pm.PickLocalPort();
                    if (inner == 0 || !await _pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, ext, inner, "IP-DESK-CTRL")) continue;
                    if (!Listen(inner)) { await _pm.ClosePortAsync(ext); continue; }
                    await _pm.OpenFixedAsync(Open.Nat.Protocol.Udp, ext, inner, "IP-DESK-CTRL (UDP)");
                    bool? reach = await PortProbe.TcpReachableAsync(ext, Log, ct);
                    if (reach == false) { StopListen(); await _pm.ClosePortAsync(ext); continue; }
                    _extPort = ext; _localPort = inner; _reachable = true;
                    Log($"PORTE porta esterna {ext} (scelta per la rete del visore) → porta {inner} di questo PC");
                    return;
                }
                Log("PORTE nessuna delle porte richieste dal visore è apribile: torno a una porta casuale");
            }

            int tries = upnp ? 6 : 1;
            for (int i = 1; i <= tries && !ct.IsCancellationRequested; i++)
            {
                int p = upnp ? await _pm.OpenRandomPortAsync("IP-DESK-CTRL", ct) : _pm.PickLocalPort();
                if (p == 0) break;
                if (!Listen(p)) { if (upnp) await _pm.ClosePortAsync(p); continue; }
                if (upnp) await _pm.OpenUdpAsync(p, "IP-DESK-CTRL (UDP)");
                if (!upnp) Log($"PORTE router senza UPnP: uso la porta locale {p} (va bene con DMZ, inoltro manuale o sulla rete locale)");
                bool? reach = await PortProbe.TcpReachableAsync(p, Log, ct);
                if (reach != false || i == tries)
                {
                    _extPort = p; _localPort = p; _reachable = reach != false;
                    if (!_reachable) Log($"PORTE la porta {p} non è raggiungibile da internet: la tengo per la rete locale e il collegamento inverso");
                    return;
                }
                Log($"PORTE la porta {p} è aperta sul router ma non raggiungibile da internet: ne provo un'altra ({i}/{tries})");
                StopListen();
                await _pm.ClosePortAsync(p);
            }
            if (_extPort == 0)
            {
                int p = _pm.PickLocalPort();
                if (p > 0 && Listen(p)) { _extPort = p; _localPort = p; _reachable = false; }
            }
        }

        private bool Listen(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                _listener = l;
                _ = Task.Run(() => AcceptLoopAsync(l, _life.Token));
                return true;
            }
            catch (Exception ex)
            {
                Log($"PORTE ascolto sulla porta {port} non riuscito: {ex.Message}");
                return false;
            }
        }

        private void StopListen()
        {
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        private async Task ClosePortAsync()
        {
            StopListen();
            if (_pm != null && _extPort > 0) await _pm.ClosePortAsync(_extPort);
            _extPort = 0; _localPort = 0; _reachable = false;
        }

        private void Announce()
        {
            if (_rdv == null) return;
            string lan = LanIp();
            var (mac, bc) = WakeOnLan.LocalInfo(lan);
            _rdv.SetAnnounce(new Net.Announce(AppVersion.Tag, Me.PublicKey, _publicIp?.ToString() ?? "", _extPort, lan, _localPort,
                UpnpOk, _reachable, DateTime.UtcNow.Ticks, AppVersion.Os, mac, bc));
        }

        /// <summary>IP di rete locale della scheda che porta a internet.</summary>
        public static string LanIp()
        {
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 53);   // nessun pacchetto parte: serve solo a scegliere la scheda
                if (s.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
            }
            catch { }
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address) && PortManager.IsPrivateOrCgnat(ua.Address))
                            return ua.Address.ToString();
                }
            }
            catch { }
            return "";
        }

        // =================================================================== connessioni in arrivo

        private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await l.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var link = await SecureLink.HandshakeAsync(tcp, Me, null, null, ct);
                        if (link.MyRole == LinkRole.Host && ViewerOnly)
                        {
                            Log($"SESSIONE {Identity.Format(link.PeerNumber)} si è collegato al programma invece che al servizio: chiuso");
                            link.Dispose();
                        }
                        else if (link.MyRole == LinkRole.Host) StartHost(link);
                        else if (_waitReverse.TryRemove(link.PeerNumber, out var tcs) && tcs.TrySetResult(link)) { }
                        else
                        {
                            Log($"PORTE collegamento inverso inatteso da {Identity.Format(link.PeerNumber)}: chiuso");
                            link.Dispose();
                        }
                    }
                    catch (Exception ex) { Log($"SESSIONE handshake in arrivo non riuscito ({ex.Message})"); }
                }, ct);
            }
        }

        private void StartHost(SecureLink link)
        {
            HostSession? busy;
            lock (_hostLock) busy = _host;
            var hs = new HostSession(this, link, busy != null && busy.IsActive);
            if (busy == null || !busy.IsActive)
            {
                lock (_hostLock) _host = hs;
                hs.Activated += () => HostStarted?.Invoke(hs);
                hs.Closed += reason =>
                {
                    lock (_hostLock) if (_host == hs) _host = null;
                    HostEnded?.Invoke(hs, reason);
                };
            }
            hs.Run(_life.Token);
        }

        internal Task<IncomingDecision> AskAsync(IncomingInfo info, CancellationToken ct)
        {
            var f = AskAccept;
            return f == null ? Task.FromResult(new IncomingDecision(false, false, false)) : f(info, ct);
        }

        /// <summary>Richiesta di collegamento inverso letta sul broker.</summary>
        private async Task OnRequestAsync(ConnectRequest req, CancellationToken ct)
        {
            string viewer = req.ViewerNumber;
            if (viewer == Me.Number) return;
            string key = viewer + ":" + req.Ticks;
            if (!_seenRequests.TryAdd(key, DateTime.UtcNow)) return;      // arriva da più broker
            foreach (var kv in _seenRequests) if (DateTime.UtcNow - kv.Value > TimeSpan.FromMinutes(10)) _seenRequests.TryRemove(kv.Key, out _);
            if (!_dialing.TryAdd(viewer, true)) return;
            try
            {
                Log($"PORTE {Identity.Format(viewer)} chiede il collegamento inverso (giro {req.Round}): {req.ViewerIp}:{req.ViewerPort} · LAN {req.ViewerLan}"
                    + (req.Restricted ? $" · la sua rete fa uscire solo le porte {string.Join(", ", req.OutboundPorts)}" : ""));

                // rete del visore filtrata: riapro la mia porta su una di quelle che escono da lì
                if (req.Restricted && req.OutboundPorts.Count > 0 && UpnpOk && !req.OutboundPorts.Contains(_extPort))
                {
                    await _portLock.WaitAsync(ct);
                    try
                    {
                        if (!req.OutboundPorts.Contains(_extPort)) { await OpenPortAsync(req.OutboundPorts, ct); Announce(); ShowNetworkStatus(); }
                    }
                    finally { _portLock.Release(); }
                }

                var targets = new List<IPEndPoint>();
                if (req.ViewerPort > 0 && IPAddress.TryParse(req.ViewerIp, out var vip)) targets.Add(new IPEndPoint(vip, req.ViewerPort));
                if (IPEndPoint.TryParse(req.ViewerLan, out var lan) && lan.Port > 0)
                {
                    // stessa casa (stesso IP pubblico): prima la rete locale
                    if (_publicIp != null && req.ViewerIp == _publicIp.ToString()) targets.Insert(0, lan); else targets.Add(lan);
                }
                foreach (var ep in targets)
                {
                    if (ct.IsCancellationRequested) return;
                    var tcp = new TcpClient(AddressFamily.InterNetwork);
                    try
                    {
                        using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            t.CancelAfter(5000);
                            await tcp.ConnectAsync(ep.Address, ep.Port, t.Token);
                        }
                        var link = await SecureLink.HandshakeAsync(tcp, Me, LinkRole.Host, null, ct);
                        if (link.PeerNumber != viewer) { link.Dispose(); continue; }
                        Log($"PORTE collegamento inverso riuscito verso {Identity.Format(viewer)} ({ep})");
                        StartHost(link);
                        return;
                    }
                    catch (Exception ex)
                    {
                        tcp.Dispose();
                        Log($"PORTE collegamento inverso verso {ep} non riuscito ({ex.Message})");
                    }
                }

                // il visore non raggiunge la mia porta e io non raggiungo la sua: cambio porta
                if (req.TriedPort > 0 && req.TriedPort == _extPort && req.Round >= 2 && UpnpOk)
                {
                    await _portLock.WaitAsync(ct);
                    try
                    {
                        Log("PORTE il visore non raggiunge questa porta: ne apro un'altra");
                        await OpenPortAsync(null, ct);
                        Announce();
                        ShowNetworkStatus();
                    }
                    finally { _portLock.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("PORTE richiesta non gestita: " + ex.Message); }
            finally { _dialing.TryRemove(viewer, out _); }
        }

        // =================================================================== messaggi firmati dal broker

        private async Task OnNoteAsync(Note note, CancellationToken ct)
        {
            if (note.SenderNumber == Me.Number) return;
            try
            {
                switch (note.Type)
                {
                    case Note.UdpOffer:
                        if (!ViewerOnly) await OnUdpOfferAsync(note, ct);
                        break;
                    case Note.WakeRequest:
                        if (!ViewerOnly) OnWakeRequest(note);
                        break;
                    case Note.UdpAnswer:
                    {
                        var ans = UdpCandidates.Decode(note.Data);
                        if (ans == null) return;
                        if (_waitUdp.TryGetValue(Convert.ToHexString(ans.Nonce), out var w) && w.number == note.SenderNumber)
                            w.tcs.TrySetResult(ans);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("MQTT messaggio non gestito: " + ex.Message); }
        }

        // =================================================================== Wake-on-LAN

        private readonly ConcurrentDictionary<string, Net.Announce> _lastSeen = new();

        /// <summary>Ricorda nello storico MAC, broadcast e IP pubblico dell'altro PC (letti dal suo indirizzo sui broker).</summary>
        public void RememberWake(HistoryEntry h)
        {
            if (!_lastSeen.TryGetValue(h.Number, out var a)) return;
            bool changed = false;
            if (a.Mac.Length > 0 && a.Mac != h.Mac) { h.Mac = a.Mac; changed = true; }
            if (a.Broadcast.Length > 0 && a.Broadcast != h.Broadcast) { h.Broadcast = a.Broadcast; changed = true; }
            if (a.PublicIp.Length > 0 && a.PublicIp != h.PublicIp) { h.PublicIp = a.PublicIp; changed = true; }
            if (changed) S.Save();
        }

        /// <summary>
        /// Accende un PC dello storico: dalla stessa rete manda direttamente il pacchetto magico; da fuori chiede
        /// (messaggio firmato sul broker) ai PC accesi della stessa rete — stesso IP pubblico — di mandarlo.
        /// Lo fanno solo se questo PC è «senza conferma» nel loro storico. Restituisce la frase da mostrare.
        /// </summary>
        public async Task<string> WakeAsync(HistoryEntry target, CancellationToken ct)
        {
            if (target.Mac.Length != 12) return "Il MAC di quel PC non è noto: collegati una volta mentre è acceso con questa versione.";
            if (_publicIp != null && target.PublicIp == _publicIp.ToString())
            {
                WakeOnLan.Send(target.Mac, target.Broadcast);
                Log($"WOL pacchetto magico per {Identity.Format(target.Number)} mandato sulla rete locale ({target.Mac})");
                return "Pacchetto di accensione mandato sulla rete locale. Se il PC è predisposto si accende entro un minuto.";
            }
            var rdv = _rdv;
            if (rdv == null || target.PublicIp.Length == 0) return "Non so in che rete si trova quel PC.";
            var others = S.History.Where(h => h.Number != target.Number && h.PublicIp == target.PublicIp).Take(10).ToList();
            if (others.Count == 0) return "Nessun altro PC dello storico è nella stessa rete di quello da accendere: il pacchetto si può mandare solo da lì.";
            var looks = await Task.WhenAll(others.Select(h => ViewerRendezvous.LookupAsync(h.Number, S.MqttBrokers, null, ct)));
            var awake = looks.Where(a => a != null && a.Age < TimeSpan.FromMinutes(8) && a.PublicIp == target.PublicIp).Select(a => a!).ToList();
            if (awake.Count == 0) return "Nessun PC acceso nella stessa rete di quello da accendere.";
            byte[] data = new MsgWriter(0).Str(target.Number).Str(target.Mac).Str(target.Broadcast).Build();
            foreach (var a in awake) await rdv.SendNoteAsync(a.Number, Note.WakeRequest, data, ct);
            Log($"WOL chiesto a {awake.Count} PC della stessa rete di accendere {Identity.Format(target.Number)}");
            return $"Chiesto a {awake.Count} PC accesi della stessa rete di mandare il pacchetto di accensione " +
                   "(lo fanno solo se questo PC può entrare lì senza conferma).";
        }

        /// <summary>Un altro PC chiede di accendere un PC di questa rete: solo se è «senza conferma» qui.</summary>
        private void OnWakeRequest(Note note)
        {
            string from = note.SenderNumber;
            var h = S.Find(from);
            if (h == null || !h.AllowUnattended || h.PeerKey != Convert.ToHexString(note.SenderKey))
            {
                Log($"WOL richiesta di {Identity.Format(from)} ignorata: non è autorizzato a entrare senza conferma");
                return;
            }
            var r = new MsgReader(note.Data);
            string number = r.Str(), mac = r.Str(), bc = r.Str();
            bool ok = WakeOnLan.Send(mac, bc);
            Log($"WOL {Identity.Format(from)} chiede di accendere {Identity.Format(number)}: " + (ok ? $"pacchetto mandato ({mac})" : "MAC non valido"));
        }

        // =================================================================== solo UDP

        /// <summary>Socket UDP nuovo (chiave ancora da stabilire), indirizzo pubblico da STUN e candidati.</summary>
        private static async Task<(UdpLink link, string cands, int delta)> PrepareUdpAsync(CancellationToken ct)
        {
            var link = new UdpLink(null, 0);
            try
            {
                link.Log += Log;
                var nat = await Stun.DiscoverAsync(link, Stun.DefaultServers, Log, ct);
                var cands = new List<string>();
                if (nat.Public != null) cands.Add(nat.Public.ToString());
                string lan = LanIp();
                if (lan.Length > 0) cands.Add($"{lan}:{link.LocalPort}");
                Log($"UDP NAT {nat.Type} · candidati {string.Join(" ", cands)}");
                return (link, string.Join(";", cands.Distinct()), nat.PortDelta);
            }
            catch
            {
                link.Dispose();
                throw;
            }
        }

        /// <summary>Chiave AES dei datagrammi dallo scambio ECDH effimero fatto sul broker (messaggi firmati).</summary>
        private static byte[] UdpKeyFrom(ECDiffieHellman eph, byte[] peerEph, byte[] nonce)
        {
            using var other = ECDiffieHellman.Create();
            other.ImportSubjectPublicKeyInfo(peerEph, out _);
            byte[] shared = eph.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
            byte[] key = Crypto.Hkdf(shared, nonce, "IPDK1 udp-mqtt");
            CryptographicOperations.ZeroMemory(shared);
            return key;
        }

        /// <summary>Lato host: il visore propone il collegamento solo UDP. Si risponde con i propri candidati e si bussa.</summary>
        private async Task OnUdpOfferAsync(Note note, CancellationToken ct)
        {
            string viewer = note.SenderNumber;
            var offer = UdpCandidates.Decode(note.Data);
            var rdv = _rdv;
            if (offer == null || rdv == null) return;
            if (!_seenRequests.TryAdd("udp:" + Convert.ToHexString(offer.Nonce), DateTime.UtcNow)) return;   // arriva da più broker
            Log($"UDP {Identity.Format(viewer)} chiede il collegamento solo UDP · candidati {offer.Candidates} · passo NAT {offer.Delta}");
            UdpLink? link = null;
            try
            {
                var (l, cands, delta) = await PrepareUdpAsync(ct);
                link = l;
                using var eph = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                link.SetKey(UdpKeyFrom(eph, offer.EphemeralKey, offer.Nonce));
                var ans = new UdpCandidates(cands, delta, eph.ExportSubjectPublicKeyInfo(), offer.Nonce);
                int sent = await rdv.SendNoteAsync(viewer, Note.UdpAnswer, ans.Encode(), ct);
                Log($"UDP risposta consegnata a {sent} broker: busso verso il visore");
                if (!await link.PunchAsync(offer.Endpoints(), offer.Delta, TimeSpan.FromSeconds(15), ct))
                {
                    link.Dispose();
                    return;
                }
                var sl = await SecureLink.HandshakeAsync(link, Me, LinkRole.Host, viewer, ct);
                link = null;
                Log($"UDP collegamento solo UDP riuscito con {Identity.Format(viewer)} ({sl.Remote})");
                StartHost(sl);
            }
            catch (Exception ex)
            {
                link?.Dispose();
                if (!ct.IsCancellationRequested) Log($"UDP collegamento solo UDP con {Identity.Format(viewer)} non riuscito ({ex.Message})");
            }
        }

        /// <summary>Lato visore: offerta sul broker, attesa della risposta, perforazione e handshake sul flusso UDP.</summary>
        private async Task<SecureLink?> TryUdpAsync(string number, Action<string> progress, CancellationToken ct)
        {
            var rdv = _rdv;
            if (rdv == null) return null;
            progress("TCP non passa tra i due PC: provo il collegamento solo UDP (perforazione del NAT)…");
            UdpLink? link = null;
            string? key = null;
            try
            {
                var (l, cands, delta) = await PrepareUdpAsync(ct);
                link = l;
                using var eph = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                byte[] nonce = RandomNumberGenerator.GetBytes(16);
                key = Convert.ToHexString(nonce);
                var tcs = new TaskCompletionSource<UdpCandidates>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waitUdp[key] = (number, tcs);
                var offer = new UdpCandidates(cands, delta, eph.ExportSubjectPublicKeyInfo(), nonce);
                int sent = await rdv.SendNoteAsync(number, Note.UdpOffer, offer.Encode(), ct);
                Log($"UDP offerta consegnata a {sent} broker, attendo i candidati dell'altro PC");
                if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15), ct)) != tcs.Task)
                {
                    Log("UDP l'altro PC non ha risposto all'offerta");
                    link.Dispose();
                    return null;
                }
                var ans = tcs.Task.Result;
                Log($"UDP candidati dell'altro PC: {ans.Candidates} · passo NAT {ans.Delta}");
                link.SetKey(UdpKeyFrom(eph, ans.EphemeralKey, nonce));
                progress("Perforazione UDP in corso…");
                if (!await link.PunchAsync(ans.Endpoints(), ans.Delta, TimeSpan.FromSeconds(12), ct))
                {
                    link.Dispose();
                    return null;
                }
                progress("Canale UDP aperto: handshake cifrato…");
                var sl = await SecureLink.HandshakeAsync(link, Me, LinkRole.Viewer, number, ct);
                link = null;
                Log($"UDP collegamento solo UDP riuscito verso {Identity.Format(number)} ({sl.Remote})");
                return sl;
            }
            catch (CryptographicException ex)
            {
                link?.Dispose();
                throw new PeerException("Collegamento rifiutato per motivi di sicurezza: " + ex.Message);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                link?.Dispose();
                Log("UDP collegamento solo UDP non riuscito: " + ex.Message);
                return null;
            }
            catch
            {
                link?.Dispose();
                throw;
            }
            finally
            {
                if (key != null) _waitUdp.TryRemove(key, out _);
            }
        }

        // =================================================================== collegamento verso un altro PC

        /// <summary>
        /// Collegamento al numero indicato. progress riceve le frasi da mostrare.
        /// Ordine: indirizzo dai broker → diretto TCP (internet, poi rete locale) → collegamento inverso TCP
        /// (fino a 2 giri) → solo UDP (candidati scambiati sul broker, perforazione, trasporto affidabile).
        /// </summary>
        public async Task<SecureLink> ConnectAsync(string number, Action<string> progress, CancellationToken ct)
        {
            number = Identity.Clean(number);
            if (!Identity.IsValidNumber(number)) throw new PeerException("Il numero deve avere 10 cifre.");
            if (number == Me.Number) throw new PeerException("Questo è il numero di questo PC.");
            for (int w = 0; !Ready && w < 60 && !ct.IsCancellationRequested; w++)
            {
                if (w == 0) progress("Preparo la rete di questo PC…");
                await Task.Delay(500, ct);
            }

            progress("Cerco " + Identity.Format(number) + " sui broker MQTT…");
            var a = await ViewerRendezvous.LookupAsync(number, S.MqttBrokers, Log, ct);
            if (a != null) _lastSeen[number] = a;
            if (a == null)
                throw new PeerException($"Il numero {Identity.Format(number)} non risulta in linea: il programma su quel PC è chiuso o non raggiunge internet, oppure il numero è sbagliato.");
            var (proto, ver) = AppVersion.Parse(a.Version);
            if (proto != AppVersion.Protocol)
                throw new PeerException($"L'altro PC usa la versione {ver} (protocollo {proto}), questo la {AppVersion.Text} (protocollo {AppVersion.Protocol}): aggiorna tutti e due.");
            if (a.Age > TimeSpan.FromMinutes(8))
                progress($"Ultimo segnale dell'altro PC: {ViewerRendezvous.Age(a.Age)}. Potrebbe essere spento; provo lo stesso…");

            var link = await TryDirectAsync(a, number, progress, ct);
            if (link != null) return link;

            // collegamento inverso: chiedo all'altro PC di collegarsi a me
            var tcs = new TaskCompletionSource<SecureLink>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waitReverse[number] = tcs;
            try
            {
                const int rounds = 2;
                for (int round = 1; round <= rounds && !ct.IsCancellationRequested; round++)
                {
                    progress($"Collegamento diretto non riuscito: chiedo all'altro PC di collegarsi a questo (giro {round} di {rounds})…");
                    var o = _outbound;
                    var req = new ConnectRequest(Me.PublicKey, _publicIp?.ToString() ?? "", _extPort,
                        LanIp().Length > 0 && _localPort > 0 ? $"{LanIp()}:{_localPort}" : "", a.Port,
                        o is { Known: true, Restricted: true }, o?.Allowed ?? new List<int>(), DateTime.UtcNow.Ticks, round);
                    int sent = await ViewerRendezvous.SendRequestAsync(number, req, Me, S.MqttBrokers, ct);
                    Log($"PORTE giro {round}: richiesta di collegamento inverso consegnata a {sent} broker");

                    var wait = Task.Delay(TimeSpan.FromSeconds(20), ct);
                    var recheck = Task.Delay(TimeSpan.FromSeconds(9), ct);
                    var first = await Task.WhenAny(tcs.Task, recheck);
                    if (first == tcs.Task) return tcs.Task.Result;
                    // l'altro PC potrebbe aver cambiato porta per noi
                    var a2 = await ViewerRendezvous.LookupAsync(number, S.MqttBrokers, null, ct);
                    if (a2 != null && (a2.Port != a.Port || a2.PublicIp != a.PublicIp))
                    {
                        a = a2;
                        progress($"L'altro PC ha aperto la porta {a.Port}: riprovo il collegamento diretto…");
                        link = await TryDirectAsync(a, number, progress, ct);
                        if (link != null) return link;
                    }
                    if (await Task.WhenAny(tcs.Task, wait) == tcs.Task) return tcs.Task.Result;
                }
            }
            catch (OperationCanceledException) { throw; }
            finally
            {
                _waitReverse.TryRemove(number, out _);
                // un collegamento inverso arrivato all'ultimo momento non resta appeso
                if (tcs.Task.IsCompletedSuccessfully && ct.IsCancellationRequested) tcs.Task.Result.Dispose();
            }
            if (ct.IsCancellationRequested) throw new OperationCanceledException();

            link = await TryUdpAsync(number, progress, ct);
            if (link != null) return link;

            if (ct.IsCancellationRequested) throw new OperationCanceledException();
            throw new PeerException("Nessuna strada tra i due PC: né il collegamento diretto, né quello inverso, né quello solo UDP sono riusciti. " +
                                    "Attiva UPnP sul router di almeno uno dei due PC (meglio su entrambi), oppure inoltra a mano la porta sul router. " +
                                    "Con IP condiviso dell'operatore (CGNAT) su entrambi i lati il collegamento diretto non è possibile.");
        }

        private async Task<SecureLink?> TryDirectAsync(Net.Announce a, string number, Action<string> progress, CancellationToken ct)
        {
            var targets = new List<(IPEndPoint ep, string what)>();
            if (IPAddress.TryParse(a.PublicIp, out var pip) && a.Port > 0) targets.Add((new IPEndPoint(pip, a.Port), "internet"));
            if (IPAddress.TryParse(a.LanIp, out var lip) && a.LanPort > 0)
            {
                var lan = (new IPEndPoint(lip, a.LanPort), "rete locale");
                // stesso IP pubblico: molti router non lasciano rientrare dall'indirizzo pubblico di casa
                if (_publicIp != null && a.PublicIp == _publicIp.ToString()) targets.Insert(0, lan); else targets.Add(lan);
            }
            foreach (var (ep, what) in targets)
            {
                ct.ThrowIfCancellationRequested();
                progress($"Mi collego a {Identity.Format(number)} via {what} ({ep})…");
                var tcp = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        t.CancelAfter(6000);
                        await tcp.ConnectAsync(ep.Address, ep.Port, t.Token);
                    }
                    var link = await SecureLink.HandshakeAsync(tcp, Me, LinkRole.Viewer, number, ct);
                    Log($"PORTE collegamento diretto riuscito verso {Identity.Format(number)} via {what} ({ep})");
                    return link;
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    tcp.Dispose();
                    throw new PeerException("Collegamento rifiutato per motivi di sicurezza: " + ex.Message);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    tcp.Dispose();
                    Log($"PORTE {ep} ({what}) non raggiungibile: {ex.Message}");
                }
            }
            return null;
        }

        // =================================================================== chiusura

        public void Dispose()
        {
            try { CurrentHost?.Close("programma chiuso sull'altro PC"); } catch { }
            _life.Cancel();
            StopListen();
            try { _rdvTask?.Wait(3000); } catch { }     // toglie l'indirizzo dai broker
            try
            {
                var pm = _pm;
                if (pm != null) Task.Run(() => pm.CloseAllAsync()).Wait(4000);
            }
            catch { }
        }
    }
}
