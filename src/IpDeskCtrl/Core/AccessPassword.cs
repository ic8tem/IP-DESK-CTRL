using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace IpDeskCtrl.Core
{
    /// <summary>Richiesta di password dell'host (sessione in corso).</summary>
    public sealed record PasswordChallenge(byte[] Salt, int Iterations, byte[] Nonce, string Message);

    /// <summary>
    /// Password facoltativa per l'accesso senza conferma.
    ///   • il visore calcola K = PBKDF2-SHA256(password, sale, 200 000 iterazioni) e lo manda nel canale cifrato
    ///     (AES-256-GCM, aperto solo dopo aver verificato che l'altro PC è proprio quel numero): la password
    ///     in chiaro non viaggia mai;
    ///   • l'host salva solo sale e V = SHA-256(K): chi leggesse il file non potrebbe entrare, perché da V
    ///     non si ricava K;
    ///   • 5 errori per numero → quel numero è bloccato per 10 minuti;
    ///   • il visore può ricordarla: tiene il PBKDF2 (non la password) cifrato con una chiave legata alla
    ///     propria identità e al numero dell'altro PC.
    /// </summary>
    public static class AccessPassword
    {
        public const int DefaultIterations = 200_000;
        public const int MaxFailures = 5;
        public static readonly TimeSpan BlockTime = TimeSpan.FromMinutes(10);

        private static readonly ConcurrentDictionary<string, (int fails, DateTime until)> Failures = new();

        /// <summary>Nuova password: (sale, hash) in esadecimale.</summary>
        public static (string salt, string hash) Make(string password, int iterations = DefaultIterations)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            return (Convert.ToHexString(salt), Convert.ToHexString(Verifier(Derive(password, salt, iterations))));
        }

        /// <summary>Quello che l'host conserva: non basta per entrare.</summary>
        public static byte[] Verifier(byte[] key) =>
            SHA256.HashData(Crypto.Concat(Encoding.ASCII.GetBytes("IPDK1-verifica|"), key));

        /// <summary>Controllo dell'host sulla chiave ricevuta.</summary>
        public static bool Check(byte[] received, byte[] storedVerifier) =>
            received.Length == 32 && Crypto.Equal(Verifier(received), storedVerifier);

        public static byte[] Derive(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Math.Clamp(iterations, 10_000, 5_000_000), HashAlgorithmName.SHA256, 32);

        // ------------------------------------------------------------------ blocco dopo 5 errori

        /// <summary>Tempo di blocco rimasto per il numero (null = non bloccato).</summary>
        public static TimeSpan? BlockedFor(string number)
        {
            if (!Failures.TryGetValue(number, out var f) || f.until <= DateTime.UtcNow) return null;
            return f.until - DateTime.UtcNow;
        }

        /// <summary>Un errore in più; restituisce i tentativi rimasti (0 = ora bloccato).</summary>
        public static int Fail(string number)
        {
            var f = Failures.AddOrUpdate(number, _ => (1, DateTime.MinValue), (_, old) => (old.until > DateTime.UtcNow ? old.fails : old.fails + 1, old.until));
            if (f.fails >= MaxFailures)
            {
                Failures[number] = (0, DateTime.UtcNow + BlockTime);
                SessionLog.Write($"SESSIONE {Identity.Format(number)} bloccato per {BlockTime.TotalMinutes:F0} minuti: {MaxFailures} password sbagliate");
                return 0;
            }
            return MaxFailures - f.fails;
        }

        public static void Ok(string number) => Failures.TryRemove(number, out _);

        // ------------------------------------------------------------------ password ricordata dal visore

        // segreto locale di questo utente (non la chiave del PC, che con il servizio non è qui)
        private static byte[]? _secret;
        private static readonly object SecretLock = new();

        private static byte[] LocalSecret()
        {
            lock (SecretLock)
            {
                if (_secret != null) return _secret;
                string path = System.IO.Path.Combine(AppSettings.Folder, "password-ricordate.key");
                byte[]? s = null;
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(path);
                        s = OperatingSystem.IsWindows() ? ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser) : raw;
                    }
                }
                catch { s = null; }
                if (s == null || s.Length != 32)
                {
                    s = RandomNumberGenerator.GetBytes(32);
                    try
                    {
                        System.IO.File.WriteAllBytes(path, OperatingSystem.IsWindows() ? ProtectedData.Protect(s, null, DataProtectionScope.CurrentUser) : s);
                        if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(path, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
                    }
                    catch { }
                }
                return _secret = s;
            }
        }

        private static byte[] LocalKey(Identity me, string number) =>
            Crypto.Hkdf(LocalSecret(), Encoding.ASCII.GetBytes(me.Number + "|" + number), "IPDK1 password ricordata");

        /// <summary>«sale:chiave cifrata» da tenere nello storico.</summary>
        public static string Protect(Identity me, string number, byte[] salt, byte[] key) =>
            Convert.ToHexString(salt) + ":" + Convert.ToBase64String(Crypto.Seal(LocalKey(me, number), key));

        /// <summary>Chiave ricordata per quel sale (null se non c'è, è di un'altra password o è illeggibile).</summary>
        public static byte[]? Unprotect(Identity me, string number, string saved, byte[] salt)
        {
            try
            {
                int i = saved.IndexOf(':');
                if (i <= 0 || saved[..i] != Convert.ToHexString(salt)) return null;
                return Crypto.Open(LocalKey(me, number), Convert.FromBase64String(saved[(i + 1)..]));
            }
            catch { return null; }
        }
    }
}
