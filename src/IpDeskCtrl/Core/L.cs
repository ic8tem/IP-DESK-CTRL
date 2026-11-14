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
        public static string FromSystem()
        {
            try
            {
                var c = CultureInfo.CurrentUICulture;
                string name = c.Name.ToLowerInvariant();
                if (name is "pt-br") return "pt-br";
                if (name is "zh-tw" or "zh-hk" or "zh-mo" || name.StartsWith("zh-hant")) return "zh-tw";
                string two = c.TwoLetterISOLanguageName.ToLowerInvariant();
                if (two is "no" or "nn") two = "nb";
                if (two == "fil") two = "tl";
                return All.Any(a => a.code == two) ? two : "en";
            }
            catch { return "en"; }
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
