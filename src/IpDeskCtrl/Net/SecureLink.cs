using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Net
{
    /// <summary>Ruolo dichiarato da un capo del collegamento.</summary>
    public enum LinkRole : byte { Viewer = 1, Host = 2 }

    /// <summary>
    /// Collegamento TCP cifrato tra due PC.
    ///
    /// Handshake (uguale per chi apre la connessione TCP e per chi la riceve, così funziona anche il
    /// collegamento inverso):
    ///   1. ognuno manda «IPDK1» | ruolo | chiave pubblica fissa (ECDSA P-256) | chiave effimera (ECDH P-256) | nonce(32)
    ///   2. ognuno firma con la propria chiave fissa l'hash SHA-256 dei due messaggi (prima il visore, poi l'host)
    ///   3. ognuno controlla la firma dell'altro; il visore controlla anche che la chiave dell'host dia il numero
    ///      di 10 cifre che ha chiamato
    ///   4. dal segreto ECDH (con HKDF-SHA256 e l'hash dello scambio) nascono due chiavi AES-256 di sessione,
    ///      una per verso, e una terza per UDP
    /// Segretezza in avanti: le chiavi effimere cambiano a ogni collegamento.
    ///
    /// Frame: lunghezza(4, big endian) | contatore(8) | tag(16) | dati cifrati AES-256-GCM.
    /// Il contatore fa da nonce e deve crescere sempre: un frame ripetuto o riordinato chiude il collegamento.
    /// </summary>
    public sealed class SecureLink : IDisposable
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("IPDK1");
        private const int MaxFrame = 16 * 1024 * 1024;

        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly AesGcm _tx, _rx;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private ulong _txCounter, _rxCounter;
        private int _disposed;

        public LinkRole MyRole { get; }
        public string PeerNumber { get; }
        public byte[] PeerKey { get; }
        public byte[] UdpKey { get; }
        public IPEndPoint Remote { get; }
        public IPEndPoint? Local { get; }
        public long BytesSent, BytesReceived;

        private SecureLink(TcpClient tcp, LinkRole role, byte[] txKey, byte[] rxKey, byte[] udpKey, byte[] peerKey, string peerNumber)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _tcp.SendBufferSize = 1 << 20;
            _tcp.ReceiveBufferSize = 1 << 20;
            _stream = tcp.GetStream();
            _tx = new AesGcm(txKey, Crypto.TagSize);
            _rx = new AesGcm(rxKey, Crypto.TagSize);
            UdpKey = udpKey;
            MyRole = role;
            PeerKey = peerKey;
            PeerNumber = peerNumber;
            Remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
            Local = tcp.Client.LocalEndPoint as IPEndPoint;
        }

        /// <summary>
        /// Handshake su una connessione TCP già aperta.
        /// role: il ruolo di questo PC; null per chi ha RICEVUTO la connessione, che legge prima il saluto
        /// dell'altro e prende il ruolo opposto (chi apre la connessione parla sempre per primo).
        /// expectedNumber: il numero chiamato dal visore (null = qualunque, controlla poi il chiamante).
        /// </summary>
        public static async Task<SecureLink> HandshakeAsync(TcpClient tcp, Identity me, LinkRole? role, string? expectedNumber, CancellationToken ct)
        {
            try
            {
                var s = tcp.GetStream();
                using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hs.CancelAfter(TimeSpan.FromSeconds(15));
                ct = hs.Token;

                using var eph = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                byte[] ephPub = eph.ExportSubjectPublicKeyInfo();
                byte[] nonce = RandomNumberGenerator.GetBytes(32);
                byte[] Hello(LinkRole r) => Crypto.Concat(Magic, new[] { (byte)r }, U16(me.PublicKey.Length), me.PublicKey, U16(ephPub.Length), ephPub, nonce);

                byte[] mine = Array.Empty<byte>();
                if (role != null)
                {
                    mine = Hello(role.Value);
                    await s.WriteAsync(Crypto.Concat(U32(mine.Length), mine), ct);
                }

                int len = ToI32(await ReadExactAsync(s, 4, ct));
                if (len < 16 || len > 4096) throw new IOException("Saluto non valido");
                byte[] theirs = await ReadExactAsync(s, len, ct);
                if (!theirs.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new IOException("Protocollo sconosciuto (non è IP-DESK-CTRL)");
                var peerRole = (LinkRole)theirs[Magic.Length];
                if (peerRole != LinkRole.Viewer && peerRole != LinkRole.Host) throw new IOException("Ruolo sconosciuto");
                if (role == null)
                {
                    role = peerRole == LinkRole.Viewer ? LinkRole.Host : LinkRole.Viewer;
                    mine = Hello(role.Value);
                    await s.WriteAsync(Crypto.Concat(U32(mine.Length), mine), ct);
                }
                if (peerRole == role) throw new IOException("Ruoli incompatibili");
                int o = Magic.Length + 1;
                int lk = ToU16(theirs, o); o += 2;
                if (lk <= 0 || o + lk > theirs.Length) throw new IOException("Saluto non valido");
                byte[] peerKey = theirs.AsSpan(o, lk).ToArray(); o += lk;
                int le = ToU16(theirs, o); o += 2;
                if (le <= 0 || o + le > theirs.Length) throw new IOException("Saluto non valido");
                byte[] peerEph = theirs.AsSpan(o, le).ToArray(); o += le;
                if (theirs.Length - o != 32) throw new IOException("Saluto non valido");

                byte[] viewerHello = role == LinkRole.Viewer ? mine : theirs;
                byte[] hostHello = role == LinkRole.Host ? mine : theirs;
                byte[] transcript = SHA256.HashData(Crypto.Concat(viewerHello, hostHello));

                byte[] sig = me.Sign(Crypto.Concat(Encoding.ASCII.GetBytes("IPDK1-firma-"), new[] { (byte)role.Value }, transcript));
                await s.WriteAsync(Crypto.Concat(U16(sig.Length), sig), ct);
                int ls = ToU16(await ReadExactAsync(s, 2, ct), 0);
                if (ls < 8 || ls > 256) throw new CryptographicException("Firma non valida");
                byte[] peerSig = await ReadExactAsync(s, ls, ct);
                if (!Identity.Verify(peerKey, Crypto.Concat(Encoding.ASCII.GetBytes("IPDK1-firma-"), new[] { (byte)peerRole }, transcript), peerSig))
                    throw new CryptographicException("L'altro PC non ha firmato con la sua chiave");

                string peerNumber = Identity.NumberOf(peerKey);
                if (expectedNumber != null && peerNumber != expectedNumber)
                    throw new CryptographicException($"Il PC che ha risposto ha il numero {Identity.Format(peerNumber)}, non {Identity.Format(expectedNumber)}");

                using var other = ECDiffieHellman.Create();
                other.ImportSubjectPublicKeyInfo(peerEph, out _);
                byte[] shared = eph.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);   // disponibile su tutti i sistemi, Android compreso
                byte[] v2h = Crypto.Hkdf(shared, transcript, "IPDK1 visore->host");
                byte[] h2v = Crypto.Hkdf(shared, transcript, "IPDK1 host->visore");
                byte[] udp = Crypto.Hkdf(shared, transcript, "IPDK1 udp");
                CryptographicOperations.ZeroMemory(shared);

                var link = role == LinkRole.Viewer
                    ? new SecureLink(tcp, role.Value, v2h, h2v, udp, peerKey, peerNumber)
                    : new SecureLink(tcp, role.Value, h2v, v2h, udp, peerKey, peerNumber);
                SessionLog.Write($"SESSIONE {(role == LinkRole.Viewer ? "visore" : "host")} ⇄ {Identity.Format(peerNumber)} ({link.Remote}) · ECDH P-256 · AES-256-GCM · impronta {Crypto.Fingerprint(peerKey)}");
                return link;
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------------ frame

        public async Task SendAsync(byte[] plain, CancellationToken ct = default)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                ulong n = ++_txCounter;
                byte[] packet = new byte[4 + 8 + Crypto.TagSize + plain.Length];
                BinaryPrimitives.WriteInt32BigEndian(packet, 8 + Crypto.TagSize + plain.Length);
                BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(4), n);
                byte[] nonce = new byte[12];
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), n);
                _tx.Encrypt(nonce, plain, packet.AsSpan(4 + 8 + Crypto.TagSize), packet.AsSpan(12, Crypto.TagSize));
                await _stream.WriteAsync(packet, ct);
                Interlocked.Add(ref BytesSent, packet.Length);
            }
            finally { _writeLock.Release(); }
        }

        /// <summary>Restituisce null quando la connessione si chiude.</summary>
        public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
        {
            byte[]? lenBuf = await ReadExactOrEofAsync(_stream, 4, ct);
            if (lenBuf == null) return null;
            int len = ToI32(lenBuf);
            if (len < 8 + Crypto.TagSize || len > MaxFrame) throw new IOException("Frame non valido");
            byte[]? data = await ReadExactOrEofAsync(_stream, len, ct);
            if (data == null) return null;
            ulong n = BinaryPrimitives.ReadUInt64BigEndian(data);
            if (n <= _rxCounter) throw new CryptographicException("Frame ripetuto");
            _rxCounter = n;
            byte[] nonce = new byte[12];
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), n);
            byte[] plain = new byte[len - 8 - Crypto.TagSize];
            _rx.Decrypt(nonce, data.AsSpan(8 + Crypto.TagSize), data.AsSpan(8, Crypto.TagSize), plain);
            Interlocked.Add(ref BytesReceived, 4 + len);
            return plain;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _tcp.Close(); } catch { }
            _tcp.Dispose();
            _tx.Dispose();
            _rx.Dispose();
        }

        // ------------------------------------------------------------------ util

        private static byte[] U16(int v) => new[] { (byte)(v >> 8), (byte)v };
        private static int ToU16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        private static byte[] U32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
        private static int ToI32(byte[] b) => (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];

        private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
            => await ReadExactOrEofAsync(s, count, ct) ?? throw new IOException("Connessione chiusa");

        private static async Task<byte[]?> ReadExactOrEofAsync(Stream s, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
                if (n == 0) return null;
                read += n;
            }
            return buf;
        }
    }
}
