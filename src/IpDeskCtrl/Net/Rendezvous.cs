using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace IpDeskCtrl.Net
{
    internal static class Mqtt
    {
        public static (string host, int port) Split(string broker)
        {
            string host = broker.Trim();
            int port = 1883;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && int.TryParse(host[(colon + 1)..], out int p)) { port = p; host = host[..colon]; }
            return (host, port);
        }

        public static MqttClientOptions Options(string broker, string clientId, int keepAliveSeconds = 30)
        {
            var (host, port) = Split(broker);
            return new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(keepAliveSeconds))
                .WithTimeout(TimeSpan.FromSeconds(8))
                .Build();
        }

        public static string NewClientId() => "ipdk-" + Guid.NewGuid().ToString("N")[..12];

        public static MqttApplicationMessage Message(string topic, byte[] payload, bool retain) =>
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

        public static MqttClientSubscribeOptions Subscribe(IEnumerable<string> topics)
        {
            var b = new MqttClientSubscribeOptionsBuilder();
            foreach (string t in topics)
                b.WithTopicFilter(f => f.WithTopic(t).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce));
            return b.Build();
        }

        public static async Task SafeDisconnect(IMqttClient c)
        {
            try { if (c.IsConnected) await c.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None); } catch { }
        }

        /// <summary>Canale del numero: deriva dal numero, non lo mostra in chiaro.</summary>
        public static string Base(string number) =>
            "ipdk/" + Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("IPDK1-canale|" + number)), 0, 12).ToLowerInvariant();

        /// <summary>Chiave dei messaggi sul broker: nasconde i contenuti a chi non conosce il numero.</summary>
        public static byte[] Key(string number) => Crypto.Hkdf(Encoding.ASCII.GetBytes(number), Encoding.ASCII.GetBytes("IPDK1"), "IPDK1-mqtt");
    }

    /// <summary>Messaggio firmato: i32 lunghezza | corpo | i32 lunghezza | firma ECDSA del corpo.</summary>
    internal static class Signed
    {
        public static byte[] Wrap(Identity me, byte[] body)
        {
            byte[] sig = me.Sign(body);
            return Crypto.Concat(BitConverter.GetBytes(body.Length), body, BitConverter.GetBytes(sig.Length), sig);
        }

        /// <summary>Corpo e firma; null se la struttura non torna.</summary>
        public static (byte[] body, byte[] sig)? Unwrap(byte[] d)
        {
            if (d.Length < 8) return null;
            int bl = BitConverter.ToInt32(d, 0);
            if (bl <= 0 || 4 + bl + 4 > d.Length) return null;
            int sl = BitConverter.ToInt32(d, 4 + bl);
            if (sl <= 0 || sl > 256 || 4 + bl + 4 + sl != d.Length) return null;
            return (d.AsSpan(4, bl).ToArray(), d.AsSpan(8 + bl, sl).ToArray());
        }
    }

    /// <summary>Indirizzo che ogni PC lascia sui broker (messaggio «retained»), firmato con la sua chiave.</summary>
    /// <remarks>Mac e Broadcast: scheda di rete locale, per accendere il PC con Wake-on-LAN quando è spento.</remarks>
    public sealed record Announce(string Version, byte[] PublicKey, string PublicIp, int Port, string LanIp, int LanPort, bool Upnp,
                                  bool Reachable, long Ticks, string Os, string Mac = "", string Broadcast = "")
    {
        public string Number => Identity.NumberOf(PublicKey);
        public TimeSpan Age => DateTime.UtcNow - new DateTime(Math.Clamp(Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);

        public byte[] Encode(Identity me)
        {
            byte[] body = new MsgWriter(1).Str(Version).I32(PublicKey.Length).Bytes(PublicKey).Str(PublicIp).I32(Port).Str(LanIp).I32(LanPort)
                .Bool(Upnp).Bool(Reachable).I64(Ticks).Str(Os).Str(Mac).Str(Broadcast).Build();
            return Signed.Wrap(me, body);
        }

        public static Announce? Decode(byte[] plain, string expectedNumber)
        {
            try
            {
                var u = Signed.Unwrap(plain);
                if (u == null) return null;
                var (body, sig) = u.Value;
                var r = new MsgReader(body);
                if (r.Type != 1) return null;
                var a = new Announce(r.Str(), r.Bytes(Math.Clamp(r.I32(), 0, 512)), r.Str(), r.I32(), r.Str(), r.I32(), r.Bool(), r.Bool(), r.I64(), r.Str());
                if (!r.AtEnd) a = a with { Mac = r.Str(), Broadcast = r.AtEnd ? "" : r.Str() };
                if (!Identity.Verify(a.PublicKey, body, sig)) return null;
                if (a.Number != expectedNumber) return null;
                return a;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Richiesta del visore all'host, sul broker: «non riesco a raggiungerti, collegati tu a me»
    /// (collegamento inverso), con le porte che il visore riesce a usare in uscita.
    /// </summary>
    public sealed record ConnectRequest(byte[] ViewerKey, string ViewerIp, int ViewerPort, string ViewerLan, int TriedPort,
                                        bool Restricted, List<int> OutboundPorts, long Ticks, int Round)
    {
        public string ViewerNumber => Identity.NumberOf(ViewerKey);

        public byte[] Encode(Identity me)
        {
            var w = new MsgWriter(2).I32(ViewerKey.Length).Bytes(ViewerKey).Str(ViewerIp).I32(ViewerPort).Str(ViewerLan).I32(TriedPort)
                .Bool(Restricted).I32(OutboundPorts.Count);
            foreach (int p in OutboundPorts) w.I32(p);
            byte[] body = w.I64(Ticks).I32(Round).Build();
            return Signed.Wrap(me, body);
        }

        public static ConnectRequest? Decode(byte[] plain)
        {
            try
            {
                var u = Signed.Unwrap(plain);
                if (u == null) return null;
                var (body, sig) = u.Value;
                var r = new MsgReader(body);
                if (r.Type != 2) return null;
                byte[] key = r.Bytes(Math.Clamp(r.I32(), 0, 512));
                string ip = r.Str(); int port = r.I32(); string lan = r.Str(); int tried = r.I32(); bool restr = r.Bool();
                int n = Math.Clamp(r.I32(), 0, 32);
                var ports = new List<int>();
                for (int i = 0; i < n; i++) ports.Add(r.I32());
                long ticks = r.I64(); int round = r.I32();
                if (!Identity.Verify(key, body, sig)) return null;
                return new ConnectRequest(key, ip, port, lan, tried, restr, ports, ticks, round);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Messaggio firmato tra due PC sul broker, per tutto quello che non è indirizzo o collegamento inverso:
    /// corpo = tipo | chiave pubblica di chi scrive | ora (ticks UTC) | dati. Cifrato con la chiave del
    /// canale del destinatario, firmato con la chiave fissa del mittente; più vecchio di 3 minuti = scartato.
    /// </summary>
    public sealed record Note(byte Type, byte[] SenderKey, long Ticks, byte[] Data)
    {
        public const byte UdpOffer = 3, UdpAnswer = 4, WakeRequest = 5;

        public string SenderNumber => Identity.NumberOf(SenderKey);
        public bool Fresh => Math.Abs((DateTime.UtcNow - new DateTime(Math.Clamp(Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc)).TotalMinutes) <= 3;

        public static byte[] Encode(Identity me, byte type, byte[] data)
        {
            byte[] body = new MsgWriter(type).I32(me.PublicKey.Length).Bytes(me.PublicKey).I64(DateTime.UtcNow.Ticks).I32(data.Length).Bytes(data).Build();
            return Signed.Wrap(me, body);
        }

        public static Note? Decode(byte[] plain)
        {
            try
            {
                var u = Signed.Unwrap(plain);
                if (u == null) return null;
                var (body, sig) = u.Value;
                var r = new MsgReader(body);
                if (r.Type < 3) return null;
                byte[] key = r.Bytes(Math.Clamp(r.I32(), 0, 512));
                long ticks = r.I64();
                byte[] data = r.Bytes(Math.Clamp(r.I32(), 0, 64 * 1024));
                if (!Identity.Verify(key, body, sig)) return null;
                return new Note(r.Type, key, ticks, data);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Offerta UDP del visore (sul canale dell'host) e risposta dell'host (sul canale del visore), quando TCP
    /// non passa: candidati (IP:porta), passo del NAT simmetrico, chiave effimera ECDH P-256 per la chiave
    /// AES dei datagrammi, nonce che lega la risposta all'offerta.
    /// </summary>
    public sealed record UdpCandidates(string Candidates, int Delta, byte[] EphemeralKey, byte[] Nonce)
    {
        public byte[] Encode() => new MsgWriter(0).Str(Candidates).I32(Delta).I32(EphemeralKey.Length).Bytes(EphemeralKey).I32(Nonce.Length).Bytes(Nonce).Build();

        public static UdpCandidates? Decode(byte[] data)
        {
            try
            {
                var r = new MsgReader(data);
                string c = r.Str(); int d = r.I32();
                byte[] eph = r.Bytes(Math.Clamp(r.I32(), 0, 512));
                byte[] nonce = r.Bytes(Math.Clamp(r.I32(), 0, 64));
                return nonce.Length == 16 ? new UdpCandidates(c, d, eph, nonce) : null;
            }
            catch { return null; }
        }

        public List<System.Net.IPEndPoint> Endpoints()
        {
            var list = new List<System.Net.IPEndPoint>();
            foreach (var c in Candidates.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (System.Net.IPEndPoint.TryParse(c, out var ep) && ep.Port > 0) list.Add(ep);
            return list;
        }
    }

    /// <summary>
    /// Lato «host» di ogni PC: resta collegato ai broker MQTT, lascia il proprio indirizzo sul canale del
    /// proprio numero e ascolta le richieste di collegamento inverso.
    /// </summary>
    public sealed class HostRendezvous
    {
        private readonly Identity _me;
        private readonly IReadOnlyList<string> _brokers;
        private readonly string _base;
        private readonly byte[] _key;
        private readonly ConcurrentDictionary<string, IMqttClient> _clients = new();
        private volatile Announce? _current;
        private readonly SemaphoreSlim _publishNow = new(0, 1);

        public event Action<ConnectRequest>? RequestReceived;
        public event Action<Note>? NoteReceived;
        public Action<string>? Log;
        /// <summary>false = solo ascolto dei messaggi per questo PC come visore (l'indirizzo lo pubblica il servizio).</summary>
        public bool Publish { get; set; } = true;
        /// <summary>false = le richieste di collegamento inverso non si guardano (le gestisce il servizio).</summary>
        public bool AcceptRequests { get; set; } = true;
        public int ConnectedBrokers => _clients.Values.Count(c => c.IsConnected);

        public HostRendezvous(Identity me, IReadOnlyList<string> brokers)
        {
            _me = me;
            _brokers = brokers;
            _base = Mqtt.Base(me.Number);
            _key = Mqtt.Key(me.Number);
        }

        private void L(string s) => Log?.Invoke("MQTT " + s);

        public void SetAnnounce(Announce a)
        {
            _current = a;
            try { _publishNow.Release(); } catch (SemaphoreFullException) { }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var loops = _brokers.Select(b => Task.Run(() => BrokerLoopAsync(b, ct))).ToList();
            var pub = Task.Run(() => PublishLoopAsync(ct));
            try { await Task.WhenAll(loops.Append(pub)); } catch { }
        }

        private async Task BrokerLoopAsync(string broker, CancellationToken ct)
        {
            int failures = 0;
            while (!ct.IsCancellationRequested)
            {
                var c = new MqttFactory().CreateMqttClient();
                var gone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                c.DisconnectedAsync += _ => { gone.TrySetResult(); return Task.CompletedTask; };
                c.ApplicationMessageReceivedAsync += e =>
                {
                    try
                    {
                        byte[] raw = e.ApplicationMessage.PayloadSegment.ToArray();
                        if (raw.Length == 0 || e.ApplicationMessage.Topic != _base + "/r") return Task.CompletedTask;
                        byte[] plain = Crypto.Open(_key, raw);
                        var note = Note.Decode(plain);
                        if (note != null)
                        {
                            if (note.Fresh) NoteReceived?.Invoke(note);
                            return Task.CompletedTask;
                        }
                        if (!AcceptRequests) return Task.CompletedTask;
                        var req = ConnectRequest.Decode(plain);
                        if (req == null) { L($"{broker}: richiesta non valida scartata"); return Task.CompletedTask; }
                        if (Math.Abs((DateTime.UtcNow - new DateTime(req.Ticks, DateTimeKind.Utc)).TotalMinutes) > 3) return Task.CompletedTask;
                        RequestReceived?.Invoke(req);
                    }
                    catch { }
                    return Task.CompletedTask;
                };
                try
                {
                    using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        t.CancelAfter(TimeSpan.FromSeconds(10));
                        await c.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                        await c.SubscribeAsync(Mqtt.Subscribe(new[] { _base + "/r" }), t.Token);
                    }
                    _clients[broker] = c;
                    failures = 0;
                    L($"{broker}: collegato, in ascolto delle richieste");
                    var a = _current;
                    if (a != null && Publish) await PublishOn(c, a, ct);
                    await Task.WhenAny(gone.Task, Task.Delay(Timeout.Infinite, ct));
                    if (!ct.IsCancellationRequested) L($"{broker}: collegamento perso, riprovo");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failures++;
                    if (failures <= 2) L($"{broker}: non raggiungibile ({ex.Message})");
                }
                catch { }
                finally
                {
                    _clients.TryRemove(broker, out _);
                    if (ct.IsCancellationRequested && c.IsConnected && Publish)
                    {
                        // uscita pulita: tolgo l'indirizzo dal broker, così gli altri vedono «non in linea»
                        try
                        {
                            using var t = new CancellationTokenSource(2000);
                            await c.PublishAsync(Mqtt.Message(_base + "/a", Array.Empty<byte>(), true), t.Token);
                        }
                        catch { }
                    }
                    await Mqtt.SafeDisconnect(c);
                    c.Dispose();
                }
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Max(1, failures))), ct); } catch { break; }
            }
        }

        /// <summary>L'indirizzo viene ripubblicato a ogni cambio e comunque ogni 3 minuti (così l'età dice se il PC è acceso).</summary>
        private async Task PublishLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await _publishNow.WaitAsync(TimeSpan.FromMinutes(3), ct); } catch { break; }
                var a = _current;
                if (a == null || !Publish) continue;
                a = a with { Ticks = DateTime.UtcNow.Ticks };
                _current = a;
                int ok = 0;
                foreach (var c in _clients.Values.ToList())
                    if (await PublishOn(c, a, ct)) ok++;
                L($"indirizzo {a.PublicIp}:{a.Port} (LAN {a.LanIp}) pubblicato su {ok} broker");
            }
        }

        /// <summary>
        /// Manda un messaggio firmato sul canale di un altro numero usando i broker già collegati (più veloce che
        /// aprire collegamenti nuovi); se nessuno è collegato, apre collegamenti al volo.
        /// </summary>
        public async Task<int> SendNoteAsync(string number, byte type, byte[] data, CancellationToken ct)
        {
            string topic = Mqtt.Base(number) + "/r";
            byte[] payload = Crypto.Seal(Mqtt.Key(number), Note.Encode(_me, type, data));
            int ok = 0;
            foreach (var c in _clients.Values.ToList())
            {
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(6000);
                    await c.PublishAsync(Mqtt.Message(topic, payload, false), t.Token);
                    ok++;
                }
                catch { }
            }
            if (ok == 0) ok = await ViewerRendezvous.SendRawAsync(number, payload, _brokers, ct);
            return ok;
        }

        private async Task<bool> PublishOn(IMqttClient c, Announce a, CancellationToken ct)
        {
            try
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(8000);
                await c.PublishAsync(Mqtt.Message(_base + "/a", Crypto.Seal(_key, a.Encode(_me)), true), t.Token);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Lato «visore»: cerca l'indirizzo di un numero e manda le richieste di collegamento inverso.</summary>
    public static class ViewerRendezvous
    {
        /// <summary>Indirizzo più recente del numero tra tutti i broker (interrogati insieme).</summary>
        public static async Task<Announce?> LookupAsync(string number, IReadOnlyList<string> brokers, Action<string>? log, CancellationToken ct)
        {
            string topic = Mqtt.Base(number) + "/a";
            var key = Mqtt.Key(number);
            var tasks = brokers.Select(async broker =>
            {
                using var c = new MqttFactory().CreateMqttClient();
                var got = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                c.ApplicationMessageReceivedAsync += e =>
                {
                    if (e.ApplicationMessage.Topic == topic) got.TrySetResult(e.ApplicationMessage.PayloadSegment.ToArray());
                    return Task.CompletedTask;
                };
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await c.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await c.SubscribeAsync(Mqtt.Subscribe(new[] { topic }), t.Token);
                    var done = await Task.WhenAny(got.Task, Task.Delay(4000, t.Token));
                    if (done != got.Task) { log?.Invoke($"MQTT {broker}: nessun indirizzo per {Identity.Format(number)}"); return null; }
                    byte[] raw = got.Task.Result;
                    if (raw.Length == 0) { log?.Invoke($"MQTT {broker}: {Identity.Format(number)} si è disconnesso"); return null; }
                    var a = Announce.Decode(Crypto.Open(key, raw), number);
                    log?.Invoke(a == null
                        ? $"MQTT {broker}: indirizzo non valido (firma o numero non corrispondono)"
                        : $"MQTT {broker}: {Identity.Format(number)} a {a.PublicIp}:{a.Port} · LAN {a.LanIp}:{a.LanPort} · {Age(a.Age)}");
                    return a;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested) log?.Invoke($"MQTT {broker}: {ex.Message}");
                    return null;
                }
                finally { await Mqtt.SafeDisconnect(c); }
            }).ToList();
            var all = await Task.WhenAll(tasks);
            return all.Where(a => a != null).OrderByDescending(a => a!.Ticks).FirstOrDefault();
        }

        public static Task<int> SendRequestAsync(string number, ConnectRequest req, Identity me, IReadOnlyList<string> brokers, CancellationToken ct) =>
            SendRawAsync(number, Crypto.Seal(Mqtt.Key(number), req.Encode(me)), brokers, ct);

        /// <summary>Pubblica un messaggio già cifrato sul canale «richieste» del numero, su tutti i broker insieme.</summary>
        public static async Task<int> SendRawAsync(string number, byte[] payload, IReadOnlyList<string> brokers, CancellationToken ct)
        {
            string topic = Mqtt.Base(number) + "/r";
            var tasks = brokers.Select(async broker =>
            {
                using var c = new MqttFactory().CreateMqttClient();
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await c.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await c.PublishAsync(Mqtt.Message(topic, payload, false), t.Token);
                    return 1;
                }
                catch { return 0; }
                finally { await Mqtt.SafeDisconnect(c); }
            }).ToList();
            return (await Task.WhenAll(tasks)).Sum();
        }

        /// <summary>Per lo storico: da quanto tempo ogni numero ha aggiornato il suo indirizzo (null = mai / spento).</summary>
        public static async Task<Dictionary<string, TimeSpan?>> PresenceAsync(IReadOnlyList<string> numbers, IReadOnlyList<string> brokers, CancellationToken ct)
        {
            var result = numbers.ToDictionary(n => n, n => (TimeSpan?)null);
            if (numbers.Count == 0) return result;
            var topics = numbers.ToDictionary(n => Mqtt.Base(n) + "/a", n => n);
            foreach (string broker in brokers)
            {
                using var c = new MqttFactory().CreateMqttClient();
                var seen = new ConcurrentDictionary<string, TimeSpan?>();
                c.ApplicationMessageReceivedAsync += e =>
                {
                    try
                    {
                        if (!topics.TryGetValue(e.ApplicationMessage.Topic, out var n)) return Task.CompletedTask;
                        byte[] raw = e.ApplicationMessage.PayloadSegment.ToArray();
                        if (raw.Length == 0) { seen[n] = null; return Task.CompletedTask; }
                        var a = Announce.Decode(Crypto.Open(Mqtt.Key(n), raw), n);
                        seen[n] = a?.Age;
                    }
                    catch { }
                    return Task.CompletedTask;
                };
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await c.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await c.SubscribeAsync(Mqtt.Subscribe(topics.Keys), t.Token);
                    await Task.Delay(2500, t.Token);
                    foreach (var kv in seen) result[kv.Key] = kv.Value;
                    return result;
                }
                catch { if (ct.IsCancellationRequested) break; }
                finally { await Mqtt.SafeDisconnect(c); }
            }
            return result;
        }

        public static string Age(TimeSpan t) =>
            t.TotalMinutes < 1 ? "aggiornato ora"
            : t.TotalMinutes < 60 ? $"aggiornato {(int)t.TotalMinutes} min fa"
            : t.TotalHours < 48 ? $"aggiornato {(int)t.TotalHours} h fa"
            : $"aggiornato {(int)t.TotalDays} giorni fa";
    }
}
