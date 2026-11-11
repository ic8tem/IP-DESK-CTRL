using System;
using System.IO;
using System.Threading.Tasks;
using IpDeskCtrl.Audio;
using IpDeskCtrl.Input;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Punti in cui una piattaforma (Android) sostituisce le parti scritte per i PC.
    /// Sul desktop restano tutti null e si usano Windows, macOS, X11 o Wayland.
    /// </summary>
    public static class Platform
    {
        public static bool IsMobile { get; set; }
        public static string? DataFolder { get; set; }
        /// <summary>Cartella dove salvare i file ricevuti (null = Scaricati/IP-DESK-CTRL).</summary>
        public static string? ReceiveFolder { get; set; }

        public static Func<IScreenSource>? Screen { get; set; }
        public static Func<IInputSink>? Input { get; set; }
        public static Func<ISystemAudioCapture>? AudioCapture { get; set; }
        public static Func<IAudioPlayer>? AudioPlayer { get; set; }

        /// <summary>
        /// Prima di farsi vedere: permessi del sistema (Android: consenso alla cattura dello schermo).
        /// Restituisce "" se va bene, altrimenti l'avviso da mostrare all'altro PC.
        /// </summary>
        public static Func<Task<string>>? PrepareHost { get; set; }
        /// <summary>Fine della sessione in arrivo (Android: chiude la cattura e il servizio).</summary>
        public static Action? HostEnded { get; set; }
        /// <summary>Schermo intero dell'intera app (Android: modalità immersiva).</summary>
        public static Action<bool>? FullScreen { get; set; }
        /// <summary>Lettura di un codice QR con la fotocamera (Android); null = non disponibile.</summary>
        public static Func<Task<string?>>? ScanQr { get; set; }
        /// <summary>Avvio di IP-TRX-CTRL ("" = avviato); null = <see cref="TrxCtrl.Launch"/> normale.</summary>
        public static Func<string>? LaunchTrx { get; set; }
        /// <summary>Riavvio del PC ("" = partito); null = <see cref="Power.Reboot"/> normale.</summary>
        public static Func<string>? Reboot { get; set; }
        /// <summary>Apre una cartella o un file con il programma del sistema.</summary>
        public static Action<string>? Open { get; set; }

        /// <summary>Appunti del sistema; impostati dall'interfaccia all'avvio.</summary>
        public static IClipboardBridge? Clipboard { get; set; }

        public static string Received
        {
            get
            {
                string f = ReceiveFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "IP-DESK-CTRL");
                Directory.CreateDirectory(f);
                return f;
            }
        }
    }
}
