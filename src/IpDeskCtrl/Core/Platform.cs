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
        /// <summary>Con il servizio di Windows: esegue come l'utente della console (gestione file). null = così com'è.</summary>
        public static Func<Func<(int, byte[])>, (int, byte[])>? AsUser { get; set; }
        /// <summary>Cartella personale dell'utente della console (con il servizio). null = quella di questo processo.</summary>
        public static Func<string?>? UserHome { get; set; }
        /// <summary>
        /// Con il servizio di Windows: adatta la risoluzione dal processo di supporto, che vede gli schermi dell'utente
        /// (il servizio, nella sessione 0, non li vede). Monitor null o w=0: si torna alla risoluzione di prima.
        /// Risposta null = processo di supporto assente: si fa da qui.
        /// </summary>
        public static Func<global::IpDeskCtrl.Screen.MonitorInfo?, int, int, string?>? FitDisplay { get; set; }

        public static Func<IScreenSource>? Screen { get; set; }
        public static Func<IInputSink>? Input { get; set; }
        public static Func<ISystemAudioCapture>? AudioCapture { get; set; }
        public static Func<IAudioPlayer>? AudioPlayer { get; set; }
        /// <summary>Microfono del telefono (visore che parla nel PC remoto).</summary>
        public static Func<ISystemAudioCapture>? MicCapture { get; set; }
        /// <summary>Dettatura vocale (Android: riconoscimento del sistema); null = non disponibile. Restituisce il testo o null.</summary>
        public static Func<Task<string?>>? Dictate { get; set; }

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

        /// <summary>Trascinamento verso chi comanda: finestrella sotto il puntatore (pixel dello schermo), restituisce i file lasciati.</summary>
        public static Func<int, int, Task<System.Collections.Generic.List<string>>>? CatchDrop { get; set; }

        // ---- accesso completo di Windows (servizio, UAC)
        /// <summary>0 = non c'entra (non Windows), 1 = servizio attivo, 2 = servizio assente ma installabile da qui, 3 = assente e non installabile.</summary>
        public static Func<int>? ServiceState { get; set; }
        /// <summary>Sul PC c'è una richiesta UAC aperta (consent.exe).</summary>
        public static Func<bool>? UacPending { get; set; }
        /// <summary>Installa il servizio (UAC per chi è al PC): "" = fatto, altrimenti il motivo.</summary>
        public static Func<Task<string>>? InstallService { get; set; }
        /// <summary>Solo nel servizio: avvia un programma come amministratore nella sessione dell'utente. "" = avviato.</summary>
        public static Func<string, string>? RunAdmin { get; set; }
        /// <summary>Dopo l'installazione del servizio il programma si riavvia per passargli la chiave.</summary>
        public static Action? RestartApp { get; set; }
        /// <summary>Apre una cartella o un file con il programma del sistema.</summary>
        public static Action<string>? Open { get; set; }
        /// <summary>Condivide un testo con le app del telefono (Android: e-mail, WhatsApp…). null = non disponibile.</summary>
        public static Action<string, string>? ShareText { get; set; }

        /// <summary>Appunti del sistema; impostati dall'interfaccia all'avvio.</summary>
        public static IClipboardBridge? Clipboard { get; set; }

        public static string Received
        {
            get
            {
                string? f = ReceiveFolder;
                if (f == null)
                {
                    // nel servizio il profilo «proprio» è quello di sistema (C:\Windows\system32\config\systemprofile),
                    // che l'utente non vede: si usa Scaricati dell'utente collegato a Windows
                    string? home = null;
                    try { home = UserHome?.Invoke(); } catch { }
                    f = Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "IP-DESK-CTRL");
                    if (home != null) ReceiveFolder = f;
                }
                Directory.CreateDirectory(f);
                return f;
            }
        }
    }
}
