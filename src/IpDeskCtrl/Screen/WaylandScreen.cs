using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Linux con Wayland (Raspberry Pi OS Bookworm con labwc o wayfire, Sway…): cattura con «grim»,
    /// che funziona sui compositori basati su wlroots. Elenco dei monitor da «wlr-randr --json».
    /// È più lenta di X11 (alcuni fotogrammi al secondo). GNOME e KDE su Wayland non permettono a grim
    /// di catturare: lì serve la sessione «Xorg» al login, oppure IPDK_X11=1 per le sole finestre X.
    /// </summary>
    public sealed class WaylandScreen : IScreenSource
    {
        private readonly bool _hasGrim;
        private List<MonitorInfo>? _monitors;
        private DateTime _monitorsAt;
        public string Note { get; private set; } = "";

        public WaylandScreen()
        {
            _hasGrim = Which("grim");
            if (!_hasGrim) Note = "Sessione Wayland: per vedere lo schermo serve il programma «grim» (sudo apt install grim) " +
                                  "oppure la sessione X11 (sul Raspberry: sudo raspi-config › Advanced Options › Wayland › X11).";
        }

        public List<MonitorInfo> Monitors()
        {
            if (_monitors != null && DateTime.UtcNow - _monitorsAt < TimeSpan.FromSeconds(10)) return _monitors;
            var list = new List<MonitorInfo>();
            try
            {
                string json = Run("wlr-randr", "--json");
                if (json.Length > 0)
                {
                    using var doc = JsonDocument.Parse(json);
                    foreach (var o in doc.RootElement.EnumerateArray())
                    {
                        if (o.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) continue;
                        string name = o.TryGetProperty("name", out var n) ? n.GetString() ?? "Schermo" : "Schermo";
                        int x = 0, y = 0, w = 0, h = 0;
                        double scale = o.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 1.0;
                        if (o.TryGetProperty("position", out var pos)) { x = pos.GetProperty("x").GetInt32(); y = pos.GetProperty("y").GetInt32(); }
                        if (o.TryGetProperty("modes", out var modes))
                            foreach (var m in modes.EnumerateArray())
                                if (m.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.True)
                                { w = (int)Math.Round(m.GetProperty("width").GetInt32() / scale); h = (int)Math.Round(m.GetProperty("height").GetInt32() / scale); }
                        if (w > 0 && h > 0) list.Add(new MonitorInfo(name, x, y, w, h, list.Count == 0));
                    }
                }
            }
            catch { }
            if (list.Count == 0) list.Add(new MonitorInfo("Schermo", 0, 0, 1920, 1080, true));
            _monitors = list;
            _monitorsAt = DateTime.UtcNow;
            return list;
        }

        public bool Capture(MonitorInfo area, Frame into)
        {
            if (!_hasGrim) return false;
            try
            {
                var psi = new ProcessStartInfo("grim")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                psi.ArgumentList.Add("-t"); psi.ArgumentList.Add("ppm");
                psi.ArgumentList.Add("-g"); psi.ArgumentList.Add($"{area.X},{area.Y} {area.W}x{area.H}");
                psi.ArgumentList.Add("-");
                using var p = Process.Start(psi)!;
                using var ms = new MemoryStream();
                p.StandardOutput.BaseStream.CopyTo(ms);
                p.WaitForExit(3000);
                if (p.ExitCode != 0)
                {
                    Note = "grim non riesce a catturare lo schermo: " + p.StandardError.ReadToEnd().Trim() +
                           " (con GNOME o KDE su Wayland scegli la sessione Xorg al login)";
                    return false;
                }
                if (!ParsePpm(ms.GetBuffer(), (int)ms.Length, into)) { Note = "Immagine di grim non riconosciuta"; return false; }
                Note = "";
                return true;
            }
            catch (Exception ex)
            {
                Note = "Cattura con grim non riuscita: " + ex.Message;
                return false;
            }
        }

        /// <summary>PPM binario (P6, 8 bit per canale) → BGRA.</summary>
        private static bool ParsePpm(byte[] b, int len, Frame into)
        {
            int pos = 0;
            string Token()
            {
                while (pos < len)
                {
                    if (b[pos] == '#') { while (pos < len && b[pos] != '\n') pos++; }
                    else if (char.IsWhiteSpace((char)b[pos])) pos++;
                    else break;
                }
                var sb = new StringBuilder();
                while (pos < len && !char.IsWhiteSpace((char)b[pos])) sb.Append((char)b[pos++]);
                return sb.ToString();
            }
            if (Token() != "P6") return false;
            if (!int.TryParse(Token(), out int w) || !int.TryParse(Token(), out int h) || !int.TryParse(Token(), out int max)) return false;
            pos++;   // un solo spazio dopo il valore massimo
            if (max != 255 || w <= 0 || h <= 0 || pos + w * h * 3 > len) return false;
            into.Ensure(w, h);
            var d = into.Data;
            int o = 0;
            for (int i = 0; i < w * h; i++)
            {
                d[o] = b[pos + 2]; d[o + 1] = b[pos + 1]; d[o + 2] = b[pos]; d[o + 3] = 255;
                o += 4; pos += 3;
            }
            return true;
        }

        public (int x, int y)? Cursor() => null;    // Wayland non dice dov'è il puntatore

        public void Dispose() { }

        internal static bool Which(string exe)
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':'))
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, exe))) return true;
            return false;
        }

        private static string Run(string exe, string args)
        {
            try
            {
                if (!Which(exe)) return "";
                var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return o;
            }
            catch { return ""; }
        }
    }
}
