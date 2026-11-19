using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Le poche funzioni che cambiano tra .NET 8 (pacchetti normali) e .NET 6 (pacchetti «windows7» per
    /// Windows 7 SP1, 8 e 8.1: .NET 8 lì non parte). Il pacchetto .NET 6 gira solo su Windows.
    /// </summary>
    public static class Compat
    {
#if NET7_0_OR_GREATER
        public const bool Windows7Build = false;
#else
        public const bool Windows7Build = true;
#endif
        /// <summary>Prefisso dei file per Windows nella release: «win» o «win7» (pacchetti .NET 6).</summary>
        public static string WinPrefix => Windows7Build ? "win7" : "win";

        /// <summary>Permessi Unix in ottale (es. 0b110_000_000 = 600); su Windows non fa niente.</summary>
        public static void SetUnixMode(string path, int mode)
        {
#if NET7_0_OR_GREATER
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)mode);
#endif
        }

        /// <summary>AES-GCM con etichetta da tagSize byte (in .NET 6 la dimensione si ricava dall'etichetta).</summary>
        public static AesGcm Gcm(byte[] key, int tagSize)
        {
#if NET8_0_OR_GREATER
            return new AesGcm(key, tagSize);
#else
            return new AesGcm(key);
#endif
        }

        public static Task<string?> ReadLineAsync(TextReader r, CancellationToken ct)
        {
#if NET7_0_OR_GREATER
            return r.ReadLineAsync(ct).AsTask();
#else
            return r.ReadLineAsync().WaitAsync(ct);
#endif
        }
    }
}
