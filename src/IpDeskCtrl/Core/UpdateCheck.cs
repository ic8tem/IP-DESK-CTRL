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
    /// Controllo delle nuove versioni: all'avvio e poi ogni 5 minuti guarda su GitHub qual è l'ultima release.
    /// Il controllo frequente legge solo il rimando della pagina «latest» (github.com, nessun limite di richieste);
    /// i dettagli (file da scaricare) si chiedono ad api.github.com solo quando c'è davvero una versione nuova,
    /// così non si consumano le 60 richieste all'ora concesse senza account.
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

        /// <summary>Ogni quanto si guarda se è uscita una nuova versione.</summary>
        public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
        public static int EveryMinutes => (int)Every.TotalMinutes;

        /// <summary>
        /// Versione dell'ultima release letta dal rimando di github.com/…/releases/latest → …/releases/tag/vX.Y.Z,
        /// senza usare l'API. null = non si sa (rete, GitHub): allora si chiede all'API.
        /// </summary>
        public static async Task<string?> LatestTagAsync(CancellationToken ct)
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("IP-DESK-CTRL/" + AppVersion.Text);
                using var req = new HttpRequestMessage(HttpMethod.Head, ReleasesPage);
                using var resp = await http.SendAsync(req, ct);
                string? loc = resp.Headers.Location?.ToString();
                if (loc == null) return null;
                int i = loc.LastIndexOf("/tag/", StringComparison.Ordinal);
                if (i < 0) return null;
                string v = Uri.UnescapeDataString(loc[(i + 5)..]).TrimStart('v', 'V').Trim();
                return v.Length == 0 ? null : v;
            }
            catch { return null; }
        }

        /// <summary>Controllo subito e poi ogni 5 minuti; found riceve solo le versioni più nuove di questa.</summary>
        public static async Task LoopAsync(Action<ReleaseInfo> found, CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(8), ct); } catch { return; }
            string told = "";
            while (!ct.IsCancellationRequested)
            {
                // prima il controllo leggero; l'API solo se c'è una versione nuova (o se il controllo leggero non risponde)
                string? tag = await LatestTagAsync(ct);
                var r = tag != null && !IsNewer(tag, AppVersion.Text) ? null : await LatestAsync(ct);
                if (r != null && IsNewer(r.Version, AppVersion.Text))
                {
                    if (r.Version != told) SessionLog.Write($"AGGIORNAMENTI disponibile la versione {r.Version} (questa è la {AppVersion.Text})");
                    told = r.Version;
                    found(r);
                }
                try { await Task.Delay(Every, ct); } catch { break; }
            }
        }
    }
}
