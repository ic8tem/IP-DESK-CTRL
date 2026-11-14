using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IpDeskCtrl.Core
{
    /// <summary>File di una release su GitHub (digest = «sha256:…» calcolato da GitHub).</summary>
    public sealed record ReleaseAsset(string Name, string Url, string Digest);

    /// <summary>
    /// Aggiornamento dal programma aperto: scarica il file giusto per questo sistema dalla release di GitHub,
    /// controlla l'impronta SHA-256 pubblicata da GitHub e lo installa da solo; poi il programma si chiude e si
    /// riapre con --dopo-aggiornamento (i collegamenti aperti si riprendono, vedi <see cref="ResumeState"/>).
    ///   Windows installato: setup silenzioso (dal servizio, se c'è: niente richiesta UAC, va bene anche da remoto);
    ///   Windows portatile: lo zip sopra la cartella;  Linux .deb: pkexec apt-get;  Linux portatile: tar sopra la cartella;
    ///   macOS: .app dal .dmg al posto di quella vecchia;  Android: l'APK si apre nel browser.
    /// </summary>
    public static class Updater
    {
        public enum Kind { None, WindowsSetup, WindowsPortable, LinuxDeb, LinuxRpm, LinuxSlackware, LinuxArch, LinuxAppImage, LinuxSnap, LinuxPortable, MacApp, Android }

        public const string AfterUpdateArg = "--dopo-aggiornamento";

        public static Kind Detect()
        {
            if (Platform.IsMobile) return Kind.Android;
            string exe = Environment.ProcessPath ?? "";
            string dir = AppContext.BaseDirectory;
            if (OperatingSystem.IsWindows()) return File.Exists(Path.Combine(dir, "unins000.exe")) ? Kind.WindowsSetup : Kind.WindowsPortable;
            if (OperatingSystem.IsMacOS()) return exe.Contains(".app/Contents/MacOS/") ? Kind.MacApp : Kind.None;
            if (OperatingSystem.IsLinux())
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"))) return Kind.LinuxAppImage;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP")) && exe.StartsWith("/snap/")) return Kind.LinuxSnap;
                if (!exe.StartsWith("/opt/ip-desk-ctrl/")) return CanWrite(dir) ? Kind.LinuxPortable : Kind.None;
                // installato da pacchetto: quale gestore lo ha messo lì
                if (Directory.Exists("/var/lib/dpkg/info") && File.Exists("/var/lib/dpkg/info/ip-desk-ctrl.list")) return Kind.LinuxDeb;
                if (Directory.Exists("/var/log/packages") && Directory.EnumerateFiles("/var/log/packages", "ip-desk-ctrl-*").Any()) return Kind.LinuxSlackware;
                if (Directory.Exists("/var/lib/pacman/local") && Directory.EnumerateDirectories("/var/lib/pacman/local", "ip-desk-ctrl-*").Any()) return Kind.LinuxArch;
                if (File.Exists("/usr/bin/rpm") || File.Exists("/bin/rpm")) return Kind.LinuxRpm;
                return Kind.LinuxDeb;
            }
            return Kind.None;
        }

        /// <summary>La cartella del programma è scrivibile (una portatile in una cartella di sistema non lo è: solo avviso).</summary>
        /// <summary>Script di aggiornamento dei pacchetti Linux che polkit lascia girare senza password.</summary>
        public const string RootHelper = "/opt/ip-desk-ctrl/aggiorna-root.sh";

        private static bool CanWrite(string dir)
        {
            try
            {
                string t = Path.Combine(dir, ".prova-scrittura-" + Environment.ProcessId);
                File.WriteAllText(t, "");
                File.Delete(t);
                return true;
            }
            catch { return false; }
        }

        private static string Rid() => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64",
        };

        /// <summary>Nome del file della release per questo sistema.</summary>
        public static string AssetName(string ver, Kind k)
        {
            string rid = Rid();
            return k switch
            {
                Kind.WindowsSetup => string.Format(System.Globalization.CultureInfo.InvariantCulture, "IP-DESK-CTRL-{0}-win-{1}-setup.exe", ver, (rid is "x86" or "arm64" ? rid : "x64")),
                Kind.WindowsPortable => string.Format(System.Globalization.CultureInfo.InvariantCulture, "IP-DESK-CTRL-{0}-win-{1}-portatile.zip", ver, (rid is "x86" or "arm64" ? rid : "x64")),
                Kind.LinuxDeb => $"ip-desk-ctrl_{ver}_{(rid == "arm64" ? "arm64" : rid == "arm" ? "armhf" : "amd64")}.deb",
                Kind.LinuxRpm => $"ip-desk-ctrl-{ver}-1.{(rid == "arm64" ? "aarch64" : rid == "arm" ? "armv7hl" : "x86_64")}.rpm",
                Kind.LinuxSlackware => $"ip-desk-ctrl-{ver}-{(rid == "arm64" ? "aarch64" : rid == "arm" ? "arm" : "x86_64")}-1_ic8tem.txz",
                Kind.LinuxArch => $"ip-desk-ctrl-{ver.Replace('-', '_')}-1-{(rid == "arm64" ? "aarch64" : "x86_64")}.pkg.tar.zst",
                Kind.LinuxAppImage => string.Format(System.Globalization.CultureInfo.InvariantCulture, "IP-DESK-CTRL-{0}-{1}.AppImage", ver, (rid == "arm64" ? "aarch64" : "x86_64")),
                Kind.LinuxSnap => $"ip-desk-ctrl_{ver}_{(rid == "arm64" ? "arm64" : "amd64")}.snap",
                // Alpine e le altre distribuzioni con musl hanno il loro eseguibile
                Kind.LinuxPortable => $"IP-DESK-CTRL-{ver}-linux-{(RuntimeInformation.RuntimeIdentifier.Contains("musl") ? "musl-" : "")}{rid}-portatile.tar.gz",
                Kind.MacApp => string.Format(System.Globalization.CultureInfo.InvariantCulture, "IP-DESK-CTRL-{0}-osx-{1}.dmg", ver, (rid == "arm64" ? "arm64" : "x64")),
                Kind.Android => string.Format(System.Globalization.CultureInfo.InvariantCulture, "IP-DESK-CTRL-{0}-android.apk", ver),
                _ => "",
            };
        }

        public static ReleaseAsset? FindAsset(ReleaseInfo r) =>
            r.Assets.FirstOrDefault(a => a.Name.Equals(AssetName(r.Version, Detect()), StringComparison.OrdinalIgnoreCase));

        /// <summary>Cosa vedrà l'utente durante l'installazione (detto prima di cominciare).</summary>
        public static string HowItGoes(Kind k, bool service) => k switch
        {
            Kind.WindowsSetup => service
                ? L.T("Il servizio di IP-DESK-CTRL installa la nuova versione senza chiedere nulla, anche se il PC è comandato da remoto.")
                : L.T("Windows chiederà il permesso di amministratore (UAC): chi è al PC deve rispondere sì."),
            Kind.LinuxDeb or Kind.LinuxRpm or Kind.LinuxSlackware or Kind.LinuxArch when File.Exists(RootHelper) => "Si installa senza password: il pacchetto nuovo lo scarica e lo verifica il sistema da GitHub.",
            Kind.LinuxDeb or Kind.LinuxRpm or Kind.LinuxSlackware or Kind.LinuxArch or Kind.LinuxSnap => L.T("Il sistema chiederà la password di amministratore per installare il pacchetto."),
            Kind.LinuxAppImage => L.T("Il file .AppImage viene sostituito con quello nuovo."),
            Kind.MacApp => L.T("Se l'app è in Applicazioni e serve, macOS chiederà la password."),
            Kind.Android => L.T("Si apre il browser per scaricare l'APK: poi toccalo per installarlo sopra questa versione."),
            _ => "",
        };

        // ------------------------------------------------------------------ scaricamento con controllo SHA-256

        public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "ip-desk-ctrl-aggiornamento");

        /// <summary>Scarica il file e controlla l'impronta. (percorso, sha256 esadecimale, errore).</summary>
        public static async Task<(string path, string sha, string error)> DownloadAsync(ReleaseAsset a, Action<double, string> progress, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                string path = Path.Combine(DownloadFolder, a.Name);
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("IP-DESK-CTRL/" + AppVersion.Text);
                using var resp = await http.GetAsync(a.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                using var sha = SHA256.Create();
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                await using (var dst = File.Create(path))
                {
                    var buf = new byte[1 << 16];
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        progress(total > 0 ? (double)done / total : 0, L.F("{0} / {1} MB", done >> 20, total >> 20));
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (a.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && !a.Digest[7..].Equals(hex, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    return ("", "", L.T("il file scaricato non corrisponde a quello pubblicato (impronta SHA-256 diversa): riprova più tardi"));
                }
                SessionLog.Write($"AGGIORNAMENTI scaricato {a.Name} (sha256 {hex}" + (a.Digest.Length > 0 ? ", uguale a quella di GitHub)" : ")"));
                return (path, hex, "");
            }
            catch (OperationCanceledException) { return ("", "", L.T("scaricamento annullato")); }
            catch (Exception ex) { return ("", "", L.T("scaricamento non riuscito: ") + ex.Message); }
        }

        // ------------------------------------------------------------------ installazione

        /// <summary>
        /// Avvia l'installazione. "" = avviata: il programma deve chiudersi subito (chi installa aspetta che esca).
        /// </summary>
        public static string Install(Kind k, string file, string sha, bool viaService, string version = "")
        {
            try
            {
                int pid = Environment.ProcessId;
                string exe = Environment.ProcessPath ?? "";
                string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                switch (k)
                {
                    case Kind.WindowsSetup:
                        if (viaService)
                        {
                            string r = Screen.WinHelper.AskService($"UPDATE {sha} {file}", 5000);
                            return r.StartsWith("OK") ? "" : "il servizio non ha installato l'aggiornamento: " + r;
                        }
                        try
                        {
                            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AGGIORNA") { UseShellExecute = true, Verb = "runas" });
                            return "";
                        }
                        catch (System.ComponentModel.Win32Exception) { return L.T("permesso di amministratore non concesso: aggiornamento annullato"); }
                    case Kind.WindowsPortable:
                    {
                        string ps = Path.Combine(DownloadFolder, "aggiorna.ps1");
                        File.WriteAllText(ps, $@"
$ErrorActionPreference = 'SilentlyContinue'
while (Get-Process -Id {pid}) {{ Start-Sleep -Milliseconds 300 }}
$tmp = Join-Path $env:TEMP 'ip-desk-ctrl-nuovo'
Remove-Item $tmp -Recurse -Force
Expand-Archive -Path '{Q(file)}' -DestinationPath $tmp -Force
$src = (Get-ChildItem $tmp -Recurse -Filter 'IP-DESK-CTRL.exe' | Select-Object -First 1).DirectoryName
Copy-Item -Path (Join-Path $src '*') -Destination '{Q(dir)}' -Recurse -Force
Start-Process -FilePath '{Q(exe)}' -ArgumentList '{AfterUpdateArg}'
", new UTF8Encoding(true));
                        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps}\"") { UseShellExecute = false, CreateNoWindow = true });
                        return "";
                    }
                    // pacchetti di sistema con lo script verificato: senza password (regola polkit del pacchetto)
                    case Kind.LinuxDeb or Kind.LinuxRpm or Kind.LinuxArch or Kind.LinuxSlackware when File.Exists(RootHelper) && version.Length > 0:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
pkexec {RootHelper} '{S(version)}' '{S(System.IO.Path.GetFileName(file))}'
nohup /opt/ip-desk-ctrl/IP-DESK-CTRL {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxDeb:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
pkexec apt-get install -y --allow-downgrades '{S(file)}' || pkexec dpkg -i '{S(file)}'
nohup /opt/ip-desk-ctrl/IP-DESK-CTRL {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxRpm:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
if command -v dnf >/dev/null 2>&1; then pkexec dnf install -y '{S(file)}'
elif command -v zypper >/dev/null 2>&1; then pkexec zypper --non-interactive install --allow-unsigned-rpm '{S(file)}'
else pkexec rpm -Uvh --replacepkgs '{S(file)}'; fi
nohup /opt/ip-desk-ctrl/IP-DESK-CTRL {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxSlackware:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
pkexec /sbin/upgradepkg --install-new --reinstall '{S(file)}'
nohup /opt/ip-desk-ctrl/IP-DESK-CTRL {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxArch:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
pkexec pacman -U --noconfirm '{S(file)}'
nohup /opt/ip-desk-ctrl/IP-DESK-CTRL {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxSnap:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
pkexec snap install --dangerous --classic '{S(file)}'
nohup /snap/bin/ip-desk-ctrl {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.LinuxAppImage:
                    {
                        string img = Environment.GetEnvironmentVariable("APPIMAGE") ?? "";
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
cp -f '{S(file)}' '{S(img)}.nuovo' && chmod +x '{S(img)}.nuovo' && mv -f '{S(img)}.nuovo' '{S(img)}'
nohup '{S(img)}' {AfterUpdateArg} >/dev/null 2>&1 &
");
                    }
                    case Kind.LinuxPortable:
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
tmp=$(mktemp -d)
tar -xzf '{S(file)}' -C ""$tmp""
src=$(dirname ""$(find ""$tmp"" -name IP-DESK-CTRL -type f | head -n 1)"")
cp -rf ""$src""/. '{S(dir)}'/
chmod +x '{S(exe)}'
rm -rf ""$tmp""
nohup '{S(exe)}' {AfterUpdateArg} >/dev/null 2>&1 &
");
                    case Kind.MacApp:
                    {
                        int i = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                        string bundle = exe[..(i + 4)];
                        return RunScript($@"
while kill -0 {pid} 2>/dev/null; do sleep 0.3; done
mnt=$(mktemp -d)
hdiutil attach -nobrowse -quiet -mountpoint ""$mnt"" '{S(file)}'
app=$(find ""$mnt"" -maxdepth 1 -name '*.app' | head -n 1)
if rm -rf '{S(bundle)}' 2>/dev/null && ditto ""$app"" '{S(bundle)}'; then :; else
  osascript -e ""do shell script \""rm -rf '{S(bundle)}' && ditto '$app' '{S(bundle)}'\"" with administrator privileges""
fi
hdiutil detach -quiet ""$mnt""
open '{S(bundle)}' --args {AfterUpdateArg}
");
                    }
                }
                return L.T("aggiornamento automatico non disponibile per questa installazione");
            }
            catch (Exception ex) { return L.T("installazione non avviata: ") + ex.Message; }
        }

        private static string Q(string s) => s.Replace("'", "''");                 // PowerShell
        private static string S(string s) => s.Replace("'", "'\\''");              // sh

        private static string RunScript(string body)
        {
            string sh = Path.Combine(DownloadFolder, "aggiorna.sh");
            File.WriteAllText(sh, "#!/bin/sh\n" + body.Replace("\r", ""));
            Process.Start(new ProcessStartInfo("/bin/sh", $"\"{sh}\"") { UseShellExecute = false });
            return "";
        }
    }

    /// <summary>
    /// Collegamenti da riprendere dopo l'aggiornamento (o dopo che l'altra parte si è aggiornata).
    /// Chi si aggiorna lo dice prima all'altro (<see cref="Net.Msg.Resume"/>): se l'altro è il PC comandato, lascerà
    /// rientrare senza conferma quel numero per 5 minuti; se è il visore, aspetta e si ricollega da solo.
    /// </summary>
    public static class ResumeState
    {
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        /// <summary>Questo PC (comandato) lascia rientrare il numero per qualche minuto, con il permesso di prima.</summary>
        public static void Grant(AppSettings s, string number, bool input)
        {
            lock (s)
            {
                s.ResumeGrants.RemoveAll(g => g.StartsWith(number + "|") || Expired(g));
                s.ResumeGrants.Add($"{number}|{(input ? 1 : 0)}|{DateTime.UtcNow.Add(Window).Ticks}");
            }
            s.Save();
        }

        /// <summary>Il numero può rientrare senza conferma? (una volta sola)</summary>
        public static bool Take(AppSettings s, string number, out bool input)
        {
            input = false;
            lock (s)
            {
                var g = s.ResumeGrants.FirstOrDefault(x => x.StartsWith(number + "|") && !Expired(x));
                s.ResumeGrants.RemoveAll(x => x.StartsWith(number + "|") || Expired(x));
                if (g == null) return false;
                input = g.Split('|')[1] == "1";
            }
            s.Save();
            return true;
        }

        private static bool Expired(string g)
        {
            var p = g.Split('|');
            return p.Length < 3 || !long.TryParse(p[2], out long t) || DateTime.UtcNow.Ticks > t;
        }
    }
}
