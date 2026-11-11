using System;
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
        public const string Caps = "mic res vol drive";

        public static bool Has(string caps, string what) => (" " + caps + " ").Contains(" " + what + " ");

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
