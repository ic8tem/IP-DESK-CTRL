using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IpDeskCtrl.Net
{
    /// <summary>
    /// Wake-on-LAN: «pacchetto magico» (6 × FF e 16 volte il MAC) in broadcast UDP sulle porte 9 e 7.
    /// Funziona solo dalla stessa rete locale del PC spento: da fuori lo manda per noi un altro PC acceso
    /// di quella rete (vedi Peer.WakeAsync), e solo se il PC spento ha la scheda e il BIOS predisposti.
    /// </summary>
    public static class WakeOnLan
    {
        /// <summary>MAC (12 cifre esadecimali) e indirizzo di broadcast della scheda con quell'IP locale.</summary>
        public static (string mac, string broadcast) LocalInfo(string lanIp)
        {
            try
            {
                if (!IPAddress.TryParse(lanIp, out var ip)) return ("", "");
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (!ua.Address.Equals(ip)) continue;
                        string mac = ni.GetPhysicalAddress().ToString();
                        if (mac.Length != 12 || mac.All(c => c == '0')) mac = "";
                        string bc = "";
                        byte[] a = ip.GetAddressBytes(), m = ua.IPv4Mask?.GetAddressBytes() ?? Array.Empty<byte>();
                        if (m.Length == 4 && m.Any(b => b != 0))
                        {
                            byte[] b = new byte[4];
                            for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                            bc = new IPAddress(b).ToString();
                        }
                        return (mac, bc);
                    }
                }
            }
            catch { }
            return ("", "");
        }

        /// <summary>Manda il pacchetto magico (broadcast della rete indicata e 255.255.255.255). false = MAC non valido.</summary>
        public static bool Send(string mac, string broadcast)
        {
            string hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length != 12) return false;
            byte[] m = Convert.FromHexString(hex);
            byte[] packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++) Buffer.BlockCopy(m, 0, packet, 6 + i * 6, 6);
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            foreach (string target in new[] { broadcast, "255.255.255.255" }.Where(t => t.Length > 0).Distinct())
            {
                if (!IPAddress.TryParse(target, out var ip)) continue;
                foreach (int port in new[] { 9, 7 })
                {
                    try { udp.Send(packet, packet.Length, new IPEndPoint(ip, port)); } catch { }
                }
            }
            return true;
        }
    }
}
