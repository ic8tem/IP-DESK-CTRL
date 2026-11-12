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
        public string Version { get; set; } = "";         // versione dell'altro PC all'ultimo collegamento («P3 0.5.0»)
        public string SavedPassword { get; set; } = "";
        public string Mac { get; set; } = "";             // Wake-on-LAN: scheda di rete dell'altro PC
        public string Broadcast { get; set; } = "";       // Wake-on-LAN: broadcast della sua rete locale
        public List<string> Tunnels { get; set; } = new();   // tunnel di porte verso quel PC (TunnelSpec: porta|indirizzo|porta|nome)
        public bool TunnelsAuto { get; set; } = true;         // aprili da soli a ogni collegamento
        public string PublicIp { get; set; } = "";        // IP pubblico dell'altro PC (stessa rete = stesso IP)   // password di quel PC ricordata (sale:PBKDF2 cifrato, vedi AccessPassword)
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
        public string PasswordSalt { get; set; } = "";     // password per l'accesso senza conferma ("" = nessuna)
        public string PasswordHash { get; set; } = "";
        public int PasswordIterations { get; set; } = AccessPassword.DefaultIterations;
        public bool AcceptMic { get; set; } = true;        // riproduce qui il microfono di chi si collega
        public string MicOutput { get; set; } = "";        // uscita per quel microfono ("" = cavo virtuale se c'è, altrimenti predefinita)
        public bool AllowResize { get; set; } = true;      // chi si collega può adattare la risoluzione alla sua finestra
        public bool AllowDrive { get; set; } = true;       // chi si collega può montare qui una sua cartella come disco
        public bool AllowTunnel { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;       // le nuove versioni si scaricano e si installano da sole
        public string SkipAutoVersion { get; set; } = "";  // versione per cui si è scelto «Più tardi»: niente installazione da sola
        public bool AllowObservers { get; set; } = true;   // mentre uno comanda, altri possono chiedere di guardare (solo visione)
        public List<string> ResumeGrants { get; set; } = new();   // numero|input|scadenza: rientrano senza conferma dopo un aggiornamento
        public List<string> ResumeViewers { get; set; } = new();  // collegamenti da riaprire dopo l'aggiornamento di questo PC
        public long ResumeUntil { get; set; }                      // fino a quando (ticks UTC)      // chi si collega può aprire tunnel di porte attraverso questo PC

        // ---------------- visore
        public int ZoomPercent { get; set; } = 0;          // 0 = adatta alla finestra
        public string Quality { get; set; } = "auto";      // auto, alta, media, bassa
        public bool PlayAudio { get; set; } = true;
        public bool ShowRemoteCursor { get; set; } = true;
        public bool HideNoticeShown { get; set; }           // avviso «resta attivo» già mostrato alla prima chiusura
        public bool SendMic { get; set; }                   // manda il microfono di qui al PC remoto
        public bool FitResolution { get; set; }             // chiedi al PC remoto la risoluzione della finestra
        public string VideoCodec { get; set; } = "auto";    // auto, h264, hevc, av1, jpeg (riquadri)
        public string SharedFolder { get; set; } = "";      // ultima cartella condivisa come disco
        public bool SharedReadOnly { get; set; }
        public TouchSettings TouchInput { get; set; } = new();   // telefono e tablet: gesti, velocità, barra dei tasti

        // ---------------- rete (gestita in automatico)
        public List<string> MqttBrokers { get; set; } = new() { "broker.hivemq.com:1883", "test.mosquitto.org:1883", "broker.emqx.io:1883" };

        /// <summary>
        /// Windows con il servizio: lo storico sta in ProgramData (storico.json), condiviso tra il servizio, che
        /// riceve i collegamenti, e il programma di ogni utente. null = storico dentro settings.json.
        /// </summary>
        public static string? SharedHistoryPath { get; set; }
        private DateTime _sharedStamp;

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
            s.ReloadShared();
            return s;
        }

        /// <summary>Rilegge lo storico condiviso se un altro processo l'ha cambiato.</summary>
        public void ReloadShared()
        {
            string? p = SharedHistoryPath;
            if (p == null) return;
            lock (_saveLock)
            {
                try
                {
                    if (!File.Exists(p)) return;
                    var stamp = File.GetLastWriteTimeUtc(p);
                    if (stamp == _sharedStamp) return;
                    var list = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(p));
                    _sharedStamp = stamp;
                    if (list != null) History = list.Where(h => Identity.IsValidNumber(h.Number)).OrderByDescending(h => h.LastUtc).ToList();
                }
                catch { }
            }
        }

        /// <summary>Porta nello storico condiviso le righe di uno storico personale (le più recenti vincono).</summary>
        public void MergeHistory(IEnumerable<HistoryEntry> mine)
        {
            lock (_saveLock)
            {
                foreach (var h in mine)
                {
                    var cur = History.FirstOrDefault(x => x.Number == h.Number);
                    if (cur == null) History.Add(h);
                    else
                    {
                        if (cur.Alias.Length == 0) cur.Alias = h.Alias;
                        if (cur.PeerKey.Length == 0) cur.PeerKey = h.PeerKey;
                        cur.AllowUnattended |= h.AllowUnattended;
                        if (h.LastUtc > cur.LastUtc) { cur.LastUtc = h.LastUtc; cur.Outgoing = h.Outgoing; }
                        cur.Count = Math.Max(cur.Count, h.Count);
                    }
                }
                History = History.OrderByDescending(x => x.LastUtc).ToList();
            }
            Save();
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
                string? p = SharedHistoryPath;
                if (p != null)
                {
                    // scritto sul posto: il file conserva i permessi dati dal servizio a tutti gli utenti
                    try
                    {
                        File.WriteAllText(p, JsonSerializer.Serialize(History, JsonOpts));
                        _sharedStamp = File.GetLastWriteTimeUtc(p);
                    }
                    catch { }
                }
            }
        }

        public HistoryEntry? Find(string number)
        {
            ReloadShared();
            lock (_saveLock) return History.FirstOrDefault(h => h.Number == number);
        }

        /// <summary>Aggiorna (o crea) la riga dello storico per un collegamento riuscito.</summary>
        public HistoryEntry Touch(string number, bool outgoing, string peerKeyHex, string os)
        {
            HistoryEntry h;
            ReloadShared();
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
                    // il servizio e il processo di supporto scrivono nello stesso file: se è occupato si riprova
                    byte[] data = System.Text.Encoding.UTF8.GetBytes($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z | {line}{Environment.NewLine}");
                    for (int i = 0; i < 20; i++)
                    {
                        try
                        {
                            using var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                            fs.Write(data, 0, data.Length);
                            break;
                        }
                        catch (IOException) { System.Threading.Thread.Sleep(15); }
                    }
                }
                catch { }
            }
        }
    }
}
