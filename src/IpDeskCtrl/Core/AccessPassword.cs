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
    ///   • l'host salva solo sale e PBKDF2-SHA256 (200 000 iterazioni) della password;
    ///   • nel canale cifrato manda sale, iterazioni e un nonce casuale; il visore risponde con
    ///     HMAC-SHA256(PBKDF2(password), nonce): la password non viaggia mai, nemmeno cifrata;
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
            return (Convert.ToHexString(salt), Convert.ToHexString(Derive(password, salt, iterations)));
        }

        public static byte[] Derive(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Math.Clamp(iterations, 10_000, 5_000_000), HashAlgorithmName.SHA256, 32);

        public static byte[] Proof(byte[] key, byte[] nonce) =>
            HMACSHA256.HashData(key, Crypto.Concat(Encoding.ASCII.GetBytes("IPDK1-password|"), nonce));

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

        private static byte[] LocalKey(Identity me, string number) =>
            Crypto.Hkdf(me.ExportPkcs8(), Encoding.ASCII.GetBytes(number), "IPDK1 password ricordata");

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
