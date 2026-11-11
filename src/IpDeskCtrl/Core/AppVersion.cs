using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Core
{
    /// <summary>Versione del programma e del protocollo («P1 0.1.0»). PC con protocollo diverso non si parlano.</summary>
    public static class AppVersion
    {
        public const int Protocol = 3;   // 3 = 0.4.x e 0.5.x (UDP affidabile, password, riavvio, Wake-on-LAN)

        /// <summary>
        /// Funzioni aggiunte senza cambiare protocollo (0.5+), annunciate in coda a Hello: ognuno usa quelle che
        /// l'altro dichiara, così la 0.5 resta compatibile con la 0.4.
        ///   mic = microfono del visore riprodotto sull'host · res = risoluzione adattata alla finestra del visore ·
        ///   vol = volume dell'host · drive = cartella del visore come disco dell'host
        /// </summary>
        public const string Caps = "mic res vol drive tunnel";

        /// <summary>Funzioni di questo PC da annunciare, con i codec video che sa decodificare (vdec=h264,hevc,av1).</summary>
        public static string CapsNow()
        {
            var dec = Screen.Ffmpeg.Decoders();
            return dec.Length > 0 ? Caps + " vdec=" + string.Join(",", dec) : Caps;
        }

        public static bool Has(string caps, string what) => (" " + caps + " ").Contains(" " + what + " ");

        /// <summary>Valore di una funzione «nome=valore» (es. vdec); "" se assente.</summary>
        public static string Value(string caps, string name) =>
            caps.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.StartsWith(name + "="))?[(name.Length + 1)..] ?? "";

        /// <summary>
        /// Controllo preliminare: confronta la versione dell'altro dispositivo con questa.
        /// "" = uguali; altrimenti un avviso (le versioni dello stesso protocollo si collegano, ma con meno funzioni).
        /// </summary>
        public static string Compare(string peerTag)
        {
            var (proto, ver) = Parse(peerTag);
            if (proto == 0) return "";
            if (proto != Protocol) return $"Versione non compatibile: l'altro dispositivo ha la {ver} (protocollo {proto}), questo la {Text} (protocollo {Protocol}). Aggiorna tutti e due.";
            if (!System.Version.TryParse(ver, out var other) || !System.Version.TryParse(Text, out var mine) || other == mine) return "";
            return other < mine
                ? $"L'altro dispositivo ha la {ver}, questo la {Text}: si collegano, ma le funzioni nuove ci sono solo aggiornando anche quello."
                : $"L'altro dispositivo ha la {ver}, più nuova di questa ({Text}): si collegano, ma conviene aggiornare questo.";
        }

        public static string Text
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
        }

        public static string Tag => $"P{Protocol} {Text}";

        /// <summary>Pagina per sostenere il progetto (pulsante «Sostieni il progetto»).</summary>
        public const string SupportUrl = "https://ko-fi.com/ic8tem";

        public static (int proto, string version) Parse(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("P")) return (0, "");
            var parts = tag.Trim().Split(' ', 2);
            int p = int.TryParse(parts[0].AsSpan(1), out int n) ? n : 0;
            return (p, parts.Length > 1 ? parts[1].Trim() : "");
        }

        public static string Os
        {
            get
            {
                string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
                if (OperatingSystem.IsWindows()) return $"Windows {(Environment.Is64BitOperatingSystem ? "64" : "32")} bit";
                if (OperatingSystem.IsMacOS()) return $"macOS ({arch})";
                if (OperatingSystem.IsAndroid()) return $"Android ({arch})";
                bool pi = false;
                try { pi = System.IO.File.Exists("/proc/device-tree/model") && System.IO.File.ReadAllText("/proc/device-tree/model").Contains("Raspberry"); } catch { }
                return (pi ? "Raspberry Pi" : "Linux") + $" ({arch})";
            }
        }
    }
}
