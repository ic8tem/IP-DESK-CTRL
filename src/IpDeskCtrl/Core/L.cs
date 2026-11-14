using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Le 36 lingue di YA-MS e LIVETROPOVHF, nello stesso ordine. La chiave di ogni testo è la frase italiana:
    /// i file Lang/{codice}.json (incorporati nell'eseguibile) danno la traduzione. Se manca nella lingua scelta
    /// si usa la lingua madre (pt-br → pt, zh-tw → zh), poi l'inglese, poi l'italiano.
    /// </summary>
    public static class L
    {
        public static readonly (string code, string name)[] All =
        {
            ("it","Italiano"),("en","English"),("de","Deutsch"),("fr","Français"),("es","Español"),("pt","Português (Portugal)"),
            ("pt-br","Português (Brasil)"),("nl","Nederlands"),("sv","Svenska"),("nb","Norsk (bokmål)"),("da","Dansk"),("is","Íslenska"),
            ("et","Eesti"),("lv","Latviešu"),("lt","Lietuvių"),("pl","Polski"),("cs","Čeština"),("hu","Magyar"),("ro","Română"),
            ("el","Ελληνικά"),("ru","Русский"),("uk","Українська"),("tr","Türkçe"),("af","Afrikaans"),("ar","العربية"),("he","עברית"),
            ("ur","اردو"),("hi","हिन्दी"),("bn","বাংলা"),("th","ไทย"),("id","Bahasa Indonesia"),("tl","Tagalog"),("zh","中文 (简体)"),
            ("zh-tw","中文 (繁體)"),("ja","日本語"),("ko","한국어")
        };

        public static bool IsRtl => Current is "ar" or "he" or "ur";

        private static string _current = "it";
        public static string Current
        {
            get => _current;
            set => _current = All.Any(a => a.code == value) ? value : FromSystem();
        }

        /// <summary>Lingua del sistema, se è tra le 36; altrimenti inglese.</summary>
        /// <remarks>
        /// Il programma per PC gira con InvariantGlobalization: lì CultureInfo.CurrentUICulture è sempre la cultura
        /// invariante (che darebbe l'inglese a tutti). La lingua si chiede quindi al sistema: Windows (lingua
        /// dell'interfaccia dell'utente), variabili LANGUAGE/LC_ALL/LC_MESSAGES/LANG su Linux, AppleLanguages su macOS.
        /// </remarks>
        public static string FromSystem()
        {
            try
            {
                foreach (string tag in SystemTags())
                {
                    string code = Map(tag);
                    if (code.Length > 0) return code;
                }
            }
            catch { }
            return "en";
        }

        private static string Map(string tag)
        {
            string name = tag.Trim().Replace('_', '-').ToLowerInvariant();
            int dot = name.IndexOfAny(new[] { '.', '@' });
            if (dot >= 0) name = name[..dot];
            if (name.Length < 2 || name is "c" or "posix" or "iv") return "";
            if (name is "pt-br") return "pt-br";
            if (name is "zh-tw" or "zh-hk" or "zh-mo" || name.StartsWith("zh-hant")) return "zh-tw";
            string two = name.Split('-')[0];
            if (two is "no" or "nn") two = "nb";
            if (two is "fil") two = "tl";
            if (two is "iw") two = "he";
            if (two is "in") two = "id";
            return All.Any(a => a.code == two) ? two : "";
        }

        private static IEnumerable<string> SystemTags()
        {
            if (OperatingSystem.IsWindows())
            {
                var sb = new System.Text.StringBuilder(85);
                if (LCIDToLocaleName(GetUserDefaultUILanguage(), sb, sb.Capacity, 0) > 0) yield return sb.ToString();
            }
            else if (!OperatingSystem.IsAndroid())
            {
                foreach (var v in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
                {
                    string? e = Environment.GetEnvironmentVariable(v);
                    if (!string.IsNullOrWhiteSpace(e)) foreach (var p in e.Split(':')) yield return p;
                }
                if (OperatingSystem.IsMacOS())
                {
                    string mac = "";
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("defaults", "read -g AppleLanguages")
                            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                        if (p != null) { mac = p.StandardOutput.ReadToEnd(); p.WaitForExit(2000); }
                    }
                    catch { }
                    foreach (var part in mac.Split(new[] { '(', ')', ',', '"', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)) yield return part;
                }
            }
            yield return CultureInfo.CurrentUICulture.Name;       // Android e i casi senza InvariantGlobalization
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int LCIDToLocaleName(uint lcid, System.Text.StringBuilder name, int size, uint flags);

        private static string _revLang = "";
        private static List<KeyValuePair<string, string>> _rev = new();

        /// <summary>
        /// Il registro delle sessioni resta in italiano: le frasi già tradotte (avvisi mostrati anche all'utente,
        /// motivi di chiusura…) tornano alla frase italiana prima di essere scritte.
        /// </summary>
        public static string ToItalian(string s)
        {
            string cur = Current;
            if (cur == "it" || s.Length == 0) return s;
            List<KeyValuePair<string, string>> rev;
            lock (Lock)
            {
                if (_revLang != cur)
                {
                    _rev = Load(cur).Where(kv => kv.Value.Length >= 6 && kv.Key.IndexOf('{') < 0 && kv.Value != kv.Key)
                                    .OrderByDescending(kv => kv.Value.Length).ToList();
                    _revLang = cur;
                }
                rev = _rev;
            }
            foreach (var kv in rev)
                if (s.Contains(kv.Value, StringComparison.Ordinal)) s = s.Replace(kv.Value, kv.Key, StringComparison.Ordinal);
            return s;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Cache = new();
        private static readonly object Lock = new();

        private static Dictionary<string, string> Load(string code)
        {
            lock (Lock)
            {
                if (Cache.TryGetValue(code, out var d)) return d;
                d = new Dictionary<string, string>();
                if (code != "it")
                {
                    try
                    {
                        var asm = typeof(L).Assembly;
                        using var s = asm.GetManifestResourceStream("lang/" + code + ".json");
                        if (s != null) d = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? d;
                    }
                    catch { }
                }
                Cache[code] = d;
                return d;
            }
        }

        /// <summary>Traduzione della frase italiana.</summary>
        public static string T(string it)
        {
            if (_current == "it" || string.IsNullOrEmpty(it)) return it;
            if (Load(_current).TryGetValue(it, out var v) && !string.IsNullOrEmpty(v)) return v;
            int dash = _current.IndexOf('-');
            if (dash > 0 && Load(_current[..dash]).TryGetValue(it, out v) && !string.IsNullOrEmpty(v)) return v;
            if (Load("en").TryGetValue(it, out v) && !string.IsNullOrEmpty(v)) return v;
            return it;
        }

        /// <summary>Traduzione con segnaposto {0}, {1}…</summary>
        public static string F(string it, params object?[] args)
        {
            var fmt = T(it);
            try { return string.Format(Culture, fmt, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.InvariantCulture, it, args); } catch { return it; }
            }
        }

        public static CultureInfo Culture
        {
            get
            {
                try { return CultureInfo.GetCultureInfo(_current switch { "nb" => "nb-NO", "tl" => "fil", "pt-br" => "pt-BR", "zh-tw" => "zh-TW", "zh" => "zh-CN", _ => _current }); }
                catch { return CultureInfo.InvariantCulture; }
            }
        }
    }
}
