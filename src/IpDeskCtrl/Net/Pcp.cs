using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IpDeskCtrl.Net
{
    /// <summary>
    /// PCP, Port Control Protocol (RFC 6887): il successore di NAT-PMP. Con una richiesta MAP al router
    /// (UDP 5351) si chiede di aprire una porta in IPv4 o un varco nel firewall in IPv6. Lo usano alcuni router
    /// recenti (per esempio i FRITZ!Box per IPv6); se il router non risponde non succede niente.
    /// </summary>
    public static class Pcp
    {
        public const byte Tcp = 6, Udp = 17;
        private const int Port = 5351;

        public sealed record Result(bool Ok, int ResultCode, IPAddress? ExternalIp, int ExternalPort, uint Lifetime);

        /// <param name="gateway">router (IPv6 link-local con la scheda, o IPv4)</param>
        /// <param name="client">indirizzo di questo PC sulla stessa famiglia del router</param>
        public static async Task<Result?> MapAsync(IPAddress gateway, IPAddress client, byte protocol, int internalPort, int suggestedPort, uint lifetime, CancellationToken ct)
        {
            byte[] req = new byte[60];
            req[0] = 2;                       // versione
            req[1] = 1;                       // MAP, richiesta
            BinaryPrimitives.WriteUInt32BigEndian(req.AsSpan(4), lifetime);
            Mapped(client).CopyTo(req, 8);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            nonce.CopyTo(req, 24);
            req[36] = protocol;
            BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(40), (ushort)internalPort);
            BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(42), (ushort)suggestedPort);
            // indirizzo esterno suggerito: «qualunque» (:: in IPv6, ::ffff:0.0.0.0 in IPv4)
            if (client.AddressFamily == AddressFamily.InterNetwork) { req[54] = 0xFF; req[55] = 0xFF; }

            try
            {
                using var udp = new UdpClient(gateway.AddressFamily);
                udp.Client.Bind(new IPEndPoint(client.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
                var to = new IPEndPoint(gateway, Port);
                int wait = 250;
                for (int i = 0; i < 4 && !ct.IsCancellationRequested; i++, wait *= 2)
                {
                    await udp.SendAsync(req, req.Length, to);
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(wait);
                    try
                    {
                        while (true)
                        {
                            var r = await udp.ReceiveAsync(t.Token);
                            byte[] d = r.Buffer;
                            if (d.Length < 60 || d[0] != 2 || d[1] != 0x81 || !d.AsSpan(24, 12).SequenceEqual(nonce)) continue;
                            int code = d[3];
                            uint life = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
                            int ext = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(42));
                            var extIp = new IPAddress(d.AsSpan(44, 16).ToArray());
                            if (extIp.IsIPv4MappedToIPv6) extIp = extIp.MapToIPv4();
                            return new Result(code == 0, code, extIp, ext, life);
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                }
            }
            catch (SocketException) { }
            return null;   // il router non parla PCP
        }

        private static byte[] Mapped(IPAddress a) =>
            (a.AddressFamily == AddressFamily.InterNetwork ? a.MapToIPv6() : a).GetAddressBytes();

        public static string Describe(int code) => code switch
        {
            0 => "ok",
            1 => "versione non supportata",
            2 => "non autorizzato",
            3 => "richiesta malformata",
            4 => "operazione non supportata",
            7 => "rifiutato dalla politica del router",
            8 => "risorse esaurite",
            11 => "porta già in uso",
            _ => "errore " + code,
        };
    }
}
