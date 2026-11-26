using System;
using System.Collections.Generic;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Freno alle richieste di collegamento che chiedono conferma a chi è al PC:
    ///   • un numero rifiutato (No, o nessuna risposta) non può richiedere per 2 minuti;
    ///   • più di 5 richieste in un minuto da numeri mai visti → per 3 minuti i numeri sconosciuti
    ///     vengono rifiutati da soli (chi è già nella rubrica passa sempre).
    /// Così nessuno può riempire lo schermo di finestre di conferma sperando in un «Sì» per sbaglio.
    /// </summary>
    public static class RequestGuard
    {
        private static readonly object Lock = new();
        private static readonly Dictionary<string, DateTime> RejectedUntil = new();
        private static readonly Queue<DateTime> Unknown = new();
        private static DateTime _blockedUntil;

        public static readonly TimeSpan AfterReject = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan FloodBlock = TimeSpan.FromMinutes(3);
        public const int FloodLimit = 5;

        /// <summary>null = la richiesta può arrivare a chi è al PC; altrimenti il motivo del rifiuto.</summary>
        public static string? Check(string number, bool known)
        {
            var now = DateTime.UtcNow;
            lock (Lock)
            {
                if (RejectedUntil.TryGetValue(number, out var until) && until > now)
                {
                    SessionLog.Write($"SICUREZZA {Identity.Format(number)} richiede di nuovo dopo un rifiuto: respinto da solo");
                    return L.T("troppe richieste: riprova tra qualche minuto");
                }
                if (known) return null;
                while (Unknown.Count > 0 && now - Unknown.Peek() > TimeSpan.FromMinutes(1)) Unknown.Dequeue();
                if (_blockedUntil > now)
                {
                    SessionLog.Write($"SICUREZZA {Identity.Format(number)} (sconosciuto) respinto da solo: troppe richieste da numeri sconosciuti");
                    return L.T("troppe richieste: riprova tra qualche minuto");
                }
                Unknown.Enqueue(now);
                if (Unknown.Count > FloodLimit)
                {
                    _blockedUntil = now + FloodBlock;
                    SessionLog.Write($"SICUREZZA più di {FloodLimit} richieste in un minuto da numeri sconosciuti: per {FloodBlock.TotalMinutes:F0} minuti li respingo da soli");
                    return L.T("troppe richieste: riprova tra qualche minuto");
                }
                return null;
            }
        }

        public static void Rejected(string number)
        {
            lock (Lock) RejectedUntil[number] = DateTime.UtcNow + AfterReject;
        }
    }
}
