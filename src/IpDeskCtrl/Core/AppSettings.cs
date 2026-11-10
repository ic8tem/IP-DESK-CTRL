using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IpDeskCtrl.Core
{
    /// <summary>Un collegamento fatto in passato (verso un altro PC o da un altro PC verso questo).</summary>
    public sealed class HistoryEntry
    {
        public string Number { get; set; } = "";          // 10 cifre, senza spazi
        public string Alias { get; set; } = "";
        public DateTime LastUtc { get; set; }
        public int Count { get; set; }
        public bool Outgoing { get; set; } = true;        // ultimo collegamento: io verso lui (true) o lui verso di me
        public string PeerKey { get; set; } = "";         // chiave pubblica dell'altro PC (esadecimale), fissata al primo collegamento
        public bool AllowUnattended { get; set; }         // può entrare in questo PC senza conferma
        public string LastSeconds { get; set; } = "";     // durata dell'ultimo collegamento, testo
        public string Os { get; set; } = "";              // sistema operativo dell'altro PC
    }

    /// <summary>Impostazioni salvate nella cartella dati dell'utente (IP-DESK-CTRL/settings.json).</summary>
    public sealed class AppSettings
    {
        public string Theme { get; set; } = "chiaro";
        public List<HistoryEntry> History { get; set; } = new();

        // ---------------- questo PC visto da fuori
        public bool AcceptIncoming { get; set; } = true;   // gli altri possono chiedere di collegarsi
        public bool AllowInput { get; set; } = true;       // mouse e tastiera dall'altro PC
        public bool ShareAudio { get; set; } = true;       // manda l'audio del sistema all'altro PC

        // ---------------- visore
        public int ZoomPercent { get; set; } = 0;          // 0 = adatta alla finestra
        public string Quality { get; set; } = "auto";      // auto, alta, media, bassa
        public bool PlayAudio { get; set; } = true;
        public bool ShowRemoteCursor { get; set; } = true;

        // ---------------- rete (gestita in automatico)
        public List<string> MqttBrokers { get; set; } = new() { "broker.hivemq.com:1883", "test.mosquitto.org:1883", "broker.emqx.io:1883" };

        public static string Folder
        {
            get
            {
                if (Platform.DataFolder != null) { Directory.CreateDirectory(Platform.DataFolder); return Platform.DataFolder; }
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "IP-DESK-CTRL");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        private static string FilePath => Path.Combine(Folder, "settings.json");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            AppSettings s;
            try
            {
                s = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                    : new AppSettings();
            }
            catch { s = new AppSettings(); }
            s.History = s.History.Where(h => Identity.IsValidNumber(h.Number)).ToList();
            return s;
        }

        private readonly object _saveLock = new();

        public void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
                    File.Move(tmp, FilePath, true);
                }
                catch { }
            }
        }

        public HistoryEntry? Find(string number)
        {
            lock (_saveLock) return History.FirstOrDefault(h => h.Number == number);
        }

        /// <summary>Aggiorna (o crea) la riga dello storico per un collegamento riuscito.</summary>
        public HistoryEntry Touch(string number, bool outgoing, string peerKeyHex, string os)
        {
            HistoryEntry h;
            lock (_saveLock)
            {
                h = History.FirstOrDefault(x => x.Number == number) ?? new HistoryEntry { Number = number };
                if (!History.Contains(h)) History.Add(h);
                h.LastUtc = DateTime.UtcNow;
                h.Count++;
                h.Outgoing = outgoing;
                if (peerKeyHex.Length > 0) h.PeerKey = peerKeyHex;
                if (os.Length > 0) h.Os = os;
                History = History.OrderByDescending(x => x.LastUtc).ToList();
            }
            Save();
            return h;
        }
    }

    /// <summary>Registro degli errori (errori.log).</summary>
    public static class ErrorLog
    {
        public static void Write(string? text)
        {
            try { File.AppendAllText(Path.Combine(AppSettings.Folder, "errori.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}"); } catch { }
        }
    }

    /// <summary>Registro delle sessioni: collegamenti, porte, broker.</summary>
    public static class SessionLog
    {
        private static readonly object Lock = new();
        public static string FilePath => Path.Combine(AppSettings.Folder, "sessioni.log");

        public static void Write(string line)
        {
            lock (Lock)
            {
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 5_000_000) File.Move(FilePath, FilePath + ".1", true);
                    File.AppendAllText(FilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z | {line}{Environment.NewLine}");
                }
                catch { }
            }
        }
    }
}
