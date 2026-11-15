using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Dove stanno i dati del programma: nella cartella dove è installato, sottocartella «dati».
    ///   dati\sessioni.log        registro unico (sessioni ed errori), scritto dal servizio e da ogni utente
    ///   dati\storico.json        storico dei collegamenti condiviso tra servizio e utenti (Windows con il servizio)
    ///   dati\utenti\&lt;utente&gt;  impostazioni, chiavi e ffmpeg scaricato di ogni utente (cartella solo sua)
    ///   dati\servizio            chiave e file del servizio (solo SYSTEM e amministratori)
    /// L'installatore crea «dati» scrivibile da tutti gli utenti (Windows: Program Files\IP-DESK-CTRL\dati;
    /// Linux: /opt/ip-desk-ctrl/dati con permessi 1777, come /tmp). La versione portatile e l'AppImage usano la
    /// cartella accanto al programma. Non si scrive più in AppData né in ProgramData.
    /// Eccezioni, dove la cartella del programma non è utilizzabile: macOS (il pacchetto .app si sostituisce intero
    /// a ogni aggiornamento), Snap e Flatpak (sola lettura) e Android: lì restano le cartelle del sistema.
    /// </summary>
    public static class DataPaths
    {
        private static readonly object Lock = new();
        private static bool _done;
        private static string? _root;

        /// <summary>Cartella «dati» accanto al programma; null se qui non si può (vedi sopra).</summary>
        public static string? Root
        {
            get
            {
                lock (Lock)
                {
                    if (_done) return _root;
                    _done = true;
                    try { _root = FindRoot(); } catch { _root = null; }
                    return _root;
                }
            }
        }

        private static string? FindRoot()
        {
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()) return null;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"))) return null;
            string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            string baseDir = !string.IsNullOrEmpty(appImage) ? Path.GetDirectoryName(appImage)! : AppContext.BaseDirectory;
            string dir = Path.Combine(baseDir, "dati");
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, $".prova-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }

        /// <summary>Nome dell'utente per la sua sottocartella (senza caratteri non validi).</summary>
        private static string UserName
        {
            get
            {
                string u = Environment.UserName;
                foreach (char c in Path.GetInvalidFileNameChars()) u = u.Replace(c, '_');
                return u.Length > 0 ? u : "utente";
            }
        }

        /// <summary>Cartella dei dati di questo utente (o null se Root non c'è).</summary>
        public static string? UserFolder()
        {
            if (Root == null) return null;
            string d = Path.Combine(Root, "utenti", UserName);
            if (!Directory.Exists(d)) { Directory.CreateDirectory(d); Restrict(d, service: false); }
            return d;
        }

        /// <summary>Cartella del servizio di Windows (solo SYSTEM e amministratori), o null.</summary>
        public static string? ServiceFolder()
        {
            if (Root == null) return null;
            string d = Path.Combine(Root, "servizio");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            Restrict(d, service: true);
            return d;
        }

        /// <summary>Cartella condivisa (registro, storico): Root, altrimenti la cartella dati di sempre.</summary>
        public static string Shared => Root ?? AppSettings.Folder;

        /// <summary>
        /// Windows: la cartella diventa privata (l'utente o il servizio, più SYSTEM e amministratori).
        /// Linux: permessi 700. Così chiavi e password non si leggono da un altro account del PC.
        /// </summary>
        private static void Restrict(string dir, bool service)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var sec = new System.Security.AccessControl.DirectorySecurity();
                    sec.SetAccessRuleProtection(true, false);
                    var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
                    void Allow(System.Security.Principal.IdentityReference who) =>
                        sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(who,
                            System.Security.AccessControl.FileSystemRights.FullControl, inherit,
                            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                    Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null));
                    Allow(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
                    if (!service)
                    {
                        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                        if (me != null) Allow(me);
                    }
                    new DirectoryInfo(dir).SetAccessControl(sec);
                }
                else
                    File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch { }
        }

        // ================================================================ trasloco dalle cartelle di prima

        /// <summary>
        /// Al primo avvio della versione nuova: tutto quello che c'era in AppData (o ~/.config) o in ProgramData
        /// passa nella cartella «dati»; i due registri di prima (sessioni ed errori) entrano nel registro unico,
        /// in ordine di tempo. La cartella vecchia poi si cancella.
        /// </summary>
        public static void Migrate(bool service)
        {
            if (Root == null) return;
            try
            {
                if (service)
                {
                    string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IP-DESK-CTRL");
                    string? dest = ServiceFolder();
                    if (dest != null) Move(old, dest, new Dictionary<string, string> { ["storico.json"] = Path.Combine(Root, "storico.json") });
                }
                else
                {
                    string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IP-DESK-CTRL");
                    string? dest = UserFolder();
                    if (dest != null) Move(old, dest, new Dictionary<string, string>());
                }
                // registri sparsi nella cartella dell'utente o del servizio (versioni intermedie)
                foreach (var d in new[] { service ? ServiceFolder() : UserFolder() })
                    if (d != null) MergeLogs(new[] { Path.Combine(d, "sessioni.log"), Path.Combine(d, "sessioni.log.1"), Path.Combine(d, "errori.log") });
                string legacyErr = Path.Combine(Root, "errori.log");
                if (File.Exists(legacyErr)) MergeLogs(new[] { legacyErr });
            }
            catch (Exception ex) { SessionLog.Write("DATI trasloco non completato: " + ex.Message); }
        }

        private static void Move(string old, string dest, Dictionary<string, string> special)
        {
            if (!Directory.Exists(old) || Path.GetFullPath(old).TrimEnd('\\', '/') == Path.GetFullPath(dest).TrimEnd('\\', '/')) return;
            var logs = new List<string>();
            foreach (var f in Directory.GetFiles(old))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("sessioni.log", StringComparison.OrdinalIgnoreCase) || name.Equals("errori.log", StringComparison.OrdinalIgnoreCase)) { logs.Add(f); continue; }
                if (name is "programma.lock" or "mostra-finestra") { TryDelete(f); continue; }
                string to = special.TryGetValue(name, out var s) ? s : Path.Combine(dest, name);
                try { if (!File.Exists(to)) File.Move(f, to); else File.Delete(f); } catch { }
            }
            foreach (var d in Directory.GetDirectories(old))
            {
                string to = Path.Combine(dest, Path.GetFileName(d));
                try { if (!Directory.Exists(to)) Directory.Move(d, to); else Directory.Delete(d, true); } catch { }
            }
            MergeLogs(logs);
            try { Directory.Delete(old, true); } catch { }
            SessionLog.Write($"DATI spostati da {old} a {dest}");
        }

        private static void TryDelete(string f) { try { File.Delete(f); } catch { } }

        /// <summary>
        /// Registri vecchi dentro il registro unico, ordinati per ora. Le righe di errori.log (ora locale, testo su
        /// più righe) diventano «ERRORE …» in UTC come le altre. I file letti si cancellano.
        /// </summary>
        public static void MergeLogs(IEnumerable<string> sources)
        {
            var src = sources.Where(File.Exists).ToList();
            if (src.Count == 0) return;
            string target = SessionLog.FilePath;
            lock (SessionLog.Lock)
            {
                var entries = new List<(DateTime t, int order, string text)>();
                int order = 0;
                void Read(string path, bool errors)
                {
                    (DateTime t, string text)? cur = null;
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (TryParse(line, errors, out var t, out var text))
                        {
                            if (cur != null) entries.Add((cur.Value.t, order++, cur.Value.text));
                            cur = (t, text);
                        }
                        else if (cur != null) cur = (cur.Value.t, cur.Value.text + "\n    " + line.TrimEnd());
                    }
                    if (cur != null) entries.Add((cur.Value.t, order++, cur.Value.text));
                }
                if (File.Exists(target)) Read(target, false);
                foreach (var s in src) Read(s, Path.GetFileName(s).Equals("errori.log", StringComparison.OrdinalIgnoreCase));
                var lines = entries.OrderBy(e => e.t).ThenBy(e => e.order)
                                   .Select(e => $"{e.t:yyyy-MM-dd HH:mm:ss}Z | {e.text}").ToList();
                File.WriteAllLines(target, lines);
                if (!OperatingSystem.IsWindows()) try { File.SetUnixFileMode(target, (UnixFileMode)0b110_110_110); } catch { }
                foreach (var s in src) if (!Path.GetFullPath(s).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) TryDelete(s);
            }
        }

        private static bool TryParse(string line, bool errors, out DateTime t, out string text)
        {
            t = default; text = "";
            if (line.Length < 20) return false;
            if (!errors && line.Length > 23 && line[19] == 'Z' &&
                DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t))
            {
                text = line.Length > 23 ? line[23..] : "";
                return true;
            }
            if (errors && DateTime.TryParseExact(line[..19], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out t))
            {
                text = "ERRORE " + line[19..].Trim();
                return true;
            }
            return false;
        }
    }
}
