using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IpDeskCtrl.Net
{
    /// <summary>
    /// IPv6 (dalla 0.5.38). In IPv6 ogni PC ha un indirizzo pubblico suo, senza NAT: il router ha solo un
    /// firewall, che di solito blocca chi entra ma lascia passare la perforazione UDP fatta dai due lati,
    /// e che può aprire un varco su richiesta (PCP). Serve soprattutto quando in IPv4 si è dietro l'IP
    /// condiviso dell'operatore (CGNAT), dove altrimenti resta solo il relè sul broker.
    /// </summary>
    public static class Ipv6
    {
        /// <summary>Indirizzo IPv6 globale con cui questo PC esce su internet (null = nessuna rete IPv6).</summary>
        public static IPAddress? Global()
        {
            if (!Socket.OSSupportsIPv6) return null;
            try
            {
                // nessun pacchetto parte: serve solo a far scegliere al sistema l'indirizzo d'uscita
                using var s = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                s.Connect(IPAddress.Parse("2001:4860:4860::8888"), 53);
                if (s.LocalEndPoint is IPEndPoint ep && IsGlobal(ep.Address)) return new IPAddress(ep.Address.GetAddressBytes());
            }
            catch { }
            return null;
        }

        /// <summary>Indirizzo unicast globale (2000::/3), escluse le reti di transizione (Teredo, 6to4).</summary>
        public static bool IsGlobal(IPAddress a)
        {
            if (a.AddressFamily != AddressFamily.InterNetworkV6 || a.IsIPv4MappedToIPv6) return false;
            if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast || a.IsIPv6Teredo || IPAddress.IsLoopback(a)) return false;
            byte[] b = a.GetAddressBytes();
            if ((b[0] & 0xE0) != 0x20) return false;                 // non 2000::/3
            if (b[0] == 0x20 && b[1] == 0x02) return false;            // 6to4
            return true;
        }

        /// <summary>Router IPv6 (indirizzo link-local con la sua scheda) della scheda che porta l'indirizzo dato.</summary>
        public static IPAddress? Gateway(IPAddress local)
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var ip = ni.GetIPProperties();
                    if (!ip.UnicastAddresses.Any(u => u.Address.Equals(local))) continue;
                    var gw = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetworkV6 && !g.Equals(IPAddress.IPv6Any));
                    if (gw != null) return gw;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Router IPv4 della scheda che porta l'indirizzo locale dato.</summary>
        public static IPAddress? Gateway4(IPAddress local)
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var ip = ni.GetIPProperties();
                    if (!ip.UnicastAddresses.Any(u => u.Address.Equals(local))) continue;
                    var gw = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any));
                    if (gw != null) return gw;
                }
            }
            catch { }
            return null;
        }
    }
}
