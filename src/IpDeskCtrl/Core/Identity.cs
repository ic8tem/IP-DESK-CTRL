using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Identità fissa di questo PC: una coppia di chiavi ECDSA P-256 creata al primo avvio e il numero di
    /// 10 cifre che ne deriva. Il numero non cambia finché resta la chiave (anche dopo gli aggiornamenti).
    ///
    /// Numero = PBKDF2-SHA256(chiave pubblica, 100 000 iterazioni) ridotto a 10 cifre. Chi si collega a un
    /// numero controlla che la chiave pubblica dell'altro PC dia proprio quel numero: per impersonarlo
    /// bisognerebbe trovare un'altra chiave con lo stesso numero, cioè in media 10 miliardi di tentativi da
    /// 100 000 iterazioni ciascuno. In più la chiave viene ricordata al primo collegamento (storico).
    /// </summary>
    public sealed class Identity
    {
        private const int Iterations = 100_000;
        private static readonly byte[] Salt = Encoding.ASCII.GetBytes("IP-DESK-CTRL numero v1");
        private static readonly ConcurrentDictionary<string, string> Cache = new();

        public ECDsa Key { get; }
        public byte[] PublicKey { get; }
        public string Number { get; }

        private Identity(ECDsa key)
        {
            Key = key;
            PublicKey = key.ExportSubjectPublicKeyInfo();
            Number = NumberOf(PublicKey);
        }

        /// <summary>Identità da una chiave privata PKCS#8 (per esempio quella del servizio di Windows).</summary>
        public static Identity FromPkcs8(byte[] pkcs8)
        {
            var k = ECDsa.Create();
            k.ImportPkcs8PrivateKey(pkcs8, out _);
            return new Identity(k);
        }

        public byte[] ExportPkcs8() => Key.ExportPkcs8PrivateKey();

        /// <summary>Solo la chiave già salvata (null se non c'è o è illeggibile): il servizio non ne crea di nuove.</summary>
        public static Identity? LoadExisting()
        {
            byte[]? pkcs8 = KeyStore.Load();
            if (pkcs8 == null) return null;
            try { return FromPkcs8(pkcs8); }
            catch (Exception ex) { ErrorLog.Write("Chiave di questo PC illeggibile: " + ex.Message); return null; }
        }

        public static Identity LoadOrCreate()
        {
            byte[]? pkcs8 = KeyStore.Load();
            if (pkcs8 != null)
            {
                try
                {
                    var k = ECDsa.Create();
                    k.ImportPkcs8PrivateKey(pkcs8, out _);
                    return new Identity(k);
                }
                catch (Exception ex) { ErrorLog.Write("Chiave di questo PC illeggibile, ne creo una nuova: " + ex.Message); }
            }
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            KeyStore.Save(key.ExportPkcs8PrivateKey());
            SessionLog.Write("IDENTITÀ creata una nuova chiave per questo PC");
            return new Identity(key);
        }

        /// <summary>Numero di 10 cifre di una chiave pubblica (SubjectPublicKeyInfo).</summary>
        public static string NumberOf(byte[] publicKey)
        {
            string k = Convert.ToHexString(SHA256.HashData(publicKey));
            return Cache.GetOrAdd(k, _ =>
            {
                byte[] d = Rfc2898DeriveBytes.Pbkdf2(publicKey, Salt, Iterations, HashAlgorithmName.SHA256, 8);
                ulong v = BitConverter.ToUInt64(d, 0) % 10_000_000_000UL;
                return v.ToString("D10");
            });
        }

        public byte[] Sign(byte[] data) => Key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
        {
            try
            {
                using var k = ECDsa.Create();
                k.ImportSubjectPublicKeyInfo(publicKey, out _);
                return k.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            catch { return false; }
        }

        /// <summary>«1234567890» → «123 456 7890».</summary>
        public static string Format(string number)
        {
            string n = Clean(number);
            return n.Length == 10 ? $"{n[..3]} {n[3..6]} {n[6..]}" : number;
        }

        /// <summary>Tiene solo le cifre.</summary>
        public static string Clean(string? s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        public static bool IsValidNumber(string? s) => Clean(s).Length == 10;
    }

    /// <summary>
    /// Chiave privata di questo PC.
    /// Windows: DPAPI dell'utente (del computer per il servizio, <see cref="MachineScope"/>). macOS: Portachiavi (comando security).
    /// Linux/Raspberry: file cifrato con chiave legata a macchina e utente, permessi 600.
    /// </summary>
    public static class KeyStore
    {
        private const string Service = "IP-DESK-CTRL";
        private const string Account = "identity";
        private static string KeyPath => Path.Combine(AppSettings.Folder, "identity.key");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IP-DESK-CTRL-identity-v1");
        /// <summary>Windows, servizio: chiave protetta con DPAPI del computer (cartella ProgramData).</summary>
        public static bool MachineScope { get; set; }
        public static string FilePath => KeyPath;
        private static DataProtectionScope Scope => MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;

        public static byte[]? Load()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return File.Exists(KeyPath) ? ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, Scope) : null;
                if (OperatingSystem.IsMacOS())
                {
                    string hex = Run("security", $"find-generic-password -s {Service} -a {Account} -w").Trim();
                    if (hex.Length > 0) return Convert.FromHexString(hex);
                    // riserva: file come su Linux (se il Portachiavi non è disponibile)
                }
                return File.Exists(KeyPath) ? Crypto.Open(LocalKey(), File.ReadAllBytes(KeyPath)) : null;
            }
            catch { return null; }
        }

        public static void Save(byte[] key)
        {
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(KeyPath, ProtectedData.Protect(key, Entropy, Scope));
                return;
            }
            if (OperatingSystem.IsMacOS())
            {
                Run("security", $"add-generic-password -U -s {Service} -a {Account} -w {Convert.ToHexString(key)}");
                if (Run("security", $"find-generic-password -s {Service} -a {Account} -w").Trim().Length > 0) return;
            }
            File.WriteAllBytes(KeyPath, Crypto.Seal(LocalKey(), key));
            try { File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }

        private static byte[] LocalKey()
        {
            string machine = "";
            foreach (string p in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                try { if (File.Exists(p)) { machine = File.ReadAllText(p).Trim(); break; } } catch { }
            }
            byte[] ikm = Encoding.UTF8.GetBytes(machine + "|" + Environment.UserName + "|" + Environment.MachineName);
            return Crypto.Hkdf(ikm, Entropy, "ip-desk-ctrl-local-key");
        }

        private static string Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0 ? o : "";
            }
            catch { return ""; }
        }
    }
}
