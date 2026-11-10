using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IpDeskCtrl.Core
{
    public static class Crypto
    {
        public const int KeySize = 32;      // AES-256
        public const int NonceSize = 12;
        public const int TagSize = 16;

        public static byte[] Hkdf(byte[] ikm, byte[] salt, string info, int length = KeySize)
            => HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, Encoding.UTF8.GetBytes(info));

        public static bool Equal(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

        public static byte[] Concat(params byte[][] parts)
        {
            byte[] r = new byte[parts.Sum(p => p.Length)];
            int o = 0;
            foreach (var p in parts) { p.CopyTo(r, o); o += p.Length; }
            return r;
        }

        /// <summary>AES-256-GCM con nonce casuale. Formato: nonce(12) | tag(16) | testo cifrato.</summary>
        public static byte[] Seal(byte[] key, byte[] plain)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ct = new byte[plain.Length];
            byte[] tag = new byte[TagSize];
            using (var g = new AesGcm(key, TagSize)) g.Encrypt(nonce, plain, ct, tag);
            return Concat(nonce, tag, ct);
        }

        public static byte[] Open(byte[] key, byte[] sealedData)
        {
            if (sealedData.Length < NonceSize + TagSize) throw new CryptographicException("Pacchetto troppo corto");
            var nonce = sealedData.AsSpan(0, NonceSize);
            var tag = sealedData.AsSpan(NonceSize, TagSize);
            var ct = sealedData.AsSpan(NonceSize + TagSize);
            byte[] plain = new byte[ct.Length];
            using (var g = new AesGcm(key, TagSize)) g.Decrypt(nonce, ct, tag, plain);
            return plain;
        }

        /// <summary>Impronta leggibile di una chiave pubblica (per confronto a voce).</summary>
        public static string Fingerprint(byte[] publicKey)
        {
            byte[] f = SHA256.HashData(publicKey);
            return string.Join(" ", Enumerable.Range(0, 4).Select(i => Convert.ToHexString(f, i * 2, 2)));
        }
    }
}
