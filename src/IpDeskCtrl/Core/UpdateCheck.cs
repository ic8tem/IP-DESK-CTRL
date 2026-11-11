using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IpDeskCtrl.Core
{
    /// <summary>Ultima versione pubblicata su GitHub.</summary>
    public sealed record ReleaseInfo(string Version, string PageUrl, string ApkUrl, List<ReleaseAsset> Assets);

    /// <summary>
    /// Controllo delle nuove versioni: all'avvio e poi ogni 12 ore chiede a GitHub l'ultima release
    /// (api.github.com, senza account). Se è più nuova di questa, la schermata principale lo dice.
    /// </summary>
    public static class UpdateCheck
    {
        private const string Api = "https://api.github.com/repos/ic8tem/IP-DESK-CTRL/releases/latest";
        public const string ReleasesPage = "https://github.com/ic8tem/IP-DESK-CTRL/releases/latest";

        public static async Task<ReleaseInfo?> LatestAsync(CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("IP-DESK-CTRL/" + AppVersion.Text);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await http.GetStringAsync(Api, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
                var list = new List<ReleaseAsset>();
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    foreach (var a in assets.EnumerateArray())
                        list.Add(new ReleaseAsset(
                            a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "",
                            a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : ""));
                string apk = list.FirstOrDefault(x => x.Url.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))?.Url ?? "";
                string v = tag.TrimStart('v', 'V').Trim();
                return v.Length == 0 ? null : new ReleaseInfo(v, page, apk, list);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                SessionLog.Write("AGGIORNAMENTI controllo non riuscito: " + ex.Message);
                return null;
            }
        }

        /// <summary>a è più nuova di b («0.4.1» &gt; «0.4.0»).</summary>
        public static bool IsNewer(string a, string b) =>
            Version.TryParse(Normalize(a), out var va) && Version.TryParse(Normalize(b), out var vb) && va > vb;

        private static string Normalize(string v)
        {
            string core = new string(v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            return core.Contains('.') ? core : core + ".0";
        }

        /// <summary>Controllo subito e poi ogni 12 ore; found riceve solo le versioni più nuove di questa.</summary>
        public static async Task LoopAsync(Action<ReleaseInfo> found, CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(8), ct); } catch { return; }
            while (!ct.IsCancellationRequested)
            {
                var r = await LatestAsync(ct);
                if (r != null && IsNewer(r.Version, AppVersion.Text))
                {
                    SessionLog.Write($"AGGIORNAMENTI disponibile la versione {r.Version} (questa è la {AppVersion.Text})");
                    found(r);
                }
                try { await Task.Delay(TimeSpan.FromHours(12), ct); } catch { break; }
            }
        }
    }
}
