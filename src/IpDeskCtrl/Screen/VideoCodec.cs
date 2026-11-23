using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Video compresso (H.264, H.265/HEVC, AV1) al posto dei riquadri JPEG, con ffmpeg.
    ///
    /// Sul PC comandato ffmpeg riceve i fotogrammi BGRA e li codifica, preferendo i codificatori hardware della
    /// scheda video (NVIDIA NVENC, Intel Quick Sync, AMD AMF, Media Foundation di Windows, VideoToolbox del Mac);
    /// senza, x264 a bassa latenza. Sul visore un altro ffmpeg decodifica e restituisce BGRA.
    /// A parità di qualità il video consuma molta meno banda dei riquadri ed è più fluido con video e scorrimenti.
    /// Se ffmpeg manca da una delle due parti si usano i riquadri JPEG come prima.
    /// </summary>
    public static class Ffmpeg
    {
        public static readonly string[] Codecs = { "av1", "hevc", "h264" };
        public static readonly Dictionary<string, string> CodecNames = new() { ["h264"] = "H.264", ["hevc"] = "H.265", ["av1"] = "AV1" };

        private static string? _path;
        private static bool _searched;
        private static readonly object _lock = new();

        public static string ExeName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        /// <summary>Cartella dove il programma scarica ffmpeg (Windows).</summary>
        public static string LocalFolder => System.IO.Path.Combine(DataPaths.Root ?? AppSettings.Folder, "ffmpeg");

        /// <summary>ffmpeg trovato (cartella del programma, cartella dati, PATH, Homebrew); null = assente.</summary>
        public static string? Path
        {
            get
            {
                lock (_lock)
                {
                    if (_searched) return _path;
                    _searched = true;
                    if (Platform.IsMobile) return _path = null;
                    var dirs = new List<string> { LocalFolder, AppContext.BaseDirectory };
                    dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
                    dirs.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/snap/bin" });
                    foreach (var d in dirs)
                    {
                        try
                        {
                            string p = System.IO.Path.Combine(d, ExeName);
                            if (File.Exists(p)) return _path = p;
                        }
                        catch { }
                    }
                    return _path = null;
                }
            }
        }

        public static void Rescan() { lock (_lock) { _searched = false; _decoders = null; } _encoders = null; }

        // ------------------------------------------------------------------ decodificatori (visore)

        private static string[]? _decoders;

        /// <summary>Codec che questo PC sa decodificare con ffmpeg (h264, hevc, av1).</summary>
        public static string[] Decoders()
        {
            var cached = _decoders;
            if (cached != null) return cached;
            var list = new List<string>();
            string? exe = Path;
            if (exe != null)
            {
                string o = Run(exe, "-hide_banner -decoders", 8000);
                foreach (var c in Codecs)
                {
                    bool ok = c == "av1"
                        ? o.Contains(" libdav1d ") || o.Contains(" av1 ") || o.Contains(" libaom-av1 ")
                        : o.Contains(" " + c + " ");
                    if (ok) list.Add(c);
                }
            }
            return _decoders = list.ToArray();
        }

        // ------------------------------------------------------------------ codificatori (PC comandato)

        public sealed record Encoder(string Codec, string Name, bool Hardware);

        private static Task<List<Encoder>>? _encoders;

        /// <summary>Codificatori che funzionano davvero su questo PC (prova breve, fatta una volta sola).</summary>
        public static Task<List<Encoder>> EncodersAsync() => _encoders ??= Task.Run(ProbeEncoders);

        private static readonly (string codec, string name, bool hw)[] Candidates =
        {
            ("av1", "av1_nvenc", true), ("av1", "av1_qsv", true), ("av1", "av1_amf", true),
            ("hevc", "hevc_nvenc", true), ("hevc", "hevc_qsv", true), ("hevc", "hevc_amf", true), ("hevc", "hevc_videotoolbox", true), ("hevc", "hevc_mf", true),
            ("h264", "h264_nvenc", true), ("h264", "h264_qsv", true), ("h264", "h264_amf", true), ("h264", "h264_videotoolbox", true), ("h264", "h264_mf", true),
            ("h264", "libx264", false), ("hevc", "libx265", false), ("av1", "libsvtav1", false),
        };

        // ------------------------------------------------------------------ guardia della codifica in hardware

        /// <summary>
        /// Alcuni driver grafici (visto con Intel Quick Sync, hevc_qsv) bloccano tutto il PC dopo un po' di codifica
        /// in hardware: niente più eventi in Windows, serve staccare la corrente. Mentre si codifica in hardware c'è
        /// un file «segnaposto»; se all'avvio il segnaposto c'è ancora e il PC è stato riacceso dopo l'ultimo
        /// aggiornamento del segnaposto, il PC si è spento o bloccato durante la codifica in hardware: per 30 giorni
        /// si usa solo la codifica software (x264), più pesante per il processore ma senza il driver grafico.
        /// </summary>
        public static class HwGuard
        {
            private static string Dir => DataPaths.Root ?? AppSettings.Folder;
            private static string Marker => System.IO.Path.Combine(Dir, "video-hw-in-uso.txt");
            private static string BlockFile => System.IO.Path.Combine(Dir, "video-hw-sospeso.txt");
            private static int _active;
            private static Timer? _touch;

            /// <summary>Codifica in hardware sospesa: motivo da mostrare, null = permessa.</summary>
            public static string? Suspended { get; private set; }

            /// <summary>Da chiamare prima di cercare i codificatori.</summary>
            public static void CheckAtStartup()
            {
                try
                {
                    var boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                    if (File.Exists(Marker))
                    {
                        var last = File.GetLastWriteTimeUtc(Marker);
                        string enc = File.ReadAllText(Marker).Trim();
                        File.Delete(Marker);
                        if (boot > last)
                        {
                            File.WriteAllText(BlockFile, $"{DateTime.UtcNow:o}\n{enc}\nultimo segno {last:o}, riacceso {boot:o}");
                            SessionLog.Write($"VIDEO il PC si è spento o bloccato mentre codificava in hardware con {enc} (ultimo segno {last.ToLocalTime():dd/MM HH:mm}, riacceso {boot.ToLocalTime():dd/MM HH:mm}): per 30 giorni uso solo la codifica software");
                        }
                    }
                    if (File.Exists(BlockFile))
                    {
                        var since = File.GetLastWriteTimeUtc(BlockFile);
                        if (DateTime.UtcNow - since < TimeSpan.FromDays(30))
                        {
                            string enc = File.ReadAllLines(BlockFile).Skip(1).FirstOrDefault() ?? "hardware";
                            Suspended = L.F("codifica con la scheda grafica ({0}) sospesa fino al {1}: il PC si era bloccato mentre la usava", enc, since.AddDays(30).ToLocalTime().ToString("dd/MM"));
                            SessionLog.Write("VIDEO " + Suspended + $" (per riattivarla cancella {BlockFile})");
                        }
                        else File.Delete(BlockFile);
                    }
                }
                catch (Exception ex) { SessionLog.Write("VIDEO guardia della codifica in hardware non leggibile: " + ex.Message); }
            }

            /// <summary>Inizio di una codifica in hardware: segnaposto rinnovato ogni minuto.</summary>
            public static void Begin(string encoder)
            {
                if (Interlocked.Increment(ref _active) != 1) return;
                try { File.WriteAllText(Marker, encoder); } catch { }
                _touch = new Timer(_ => { try { File.SetLastWriteTimeUtc(Marker, DateTime.UtcNow); } catch { } }, null, 60_000, 60_000);
            }

            public static void End()
            {
                if (Interlocked.Decrement(ref _active) > 0) return;
                _active = 0;
                _touch?.Dispose(); _touch = null;
                try { File.Delete(Marker); } catch { }
            }
        }

        private static List<Encoder> ProbeEncoders()
        {
            HwGuard.CheckAtStartup();
            var ok = new List<Encoder>();
            string? exe = Path;
            if (exe == null) return ok;
            string list = Run(exe, "-hide_banner -encoders", 8000);
            foreach (var (codec, name, hw) in Candidates)
            {
                if (!list.Contains(" " + name + " ")) continue;
                if (hw && HwGuard.Suspended != null) continue;
                if (hw && ok.Any(e => e.Codec == codec && e.Hardware)) continue;   // per ogni codec basta un hardware
                // il software si prova sempre: serve da riserva quando la grafica integrata non va usata (più monitor)
                string args = $"-hide_banner -loglevel error -f lavfi -i testsrc2=s=640x360:r=25 -frames:v 8 -an -c:v {name} {Options(name)} -pix_fmt {(hw ? "nv12" : "yuv420p")} -f null -";
                using var p = Start(exe, args, false);
                if (p == null) continue;
                bool exited = p.WaitForExit(15000);
                if (!exited) { try { p.Kill(); } catch { } continue; }
                if (p.ExitCode == 0) ok.Add(new Encoder(codec, name, hw));
            }
            SessionLog.Write("VIDEO codificatori disponibili: " + (ok.Count == 0 ? "nessuno" : string.Join(", ", ok.Select(e => e.Name))));
            return ok;
        }

        /// <summary>Opzioni a bassa latenza per ogni codificatore.</summary>
        public static string Options(string encoder) => encoder switch
        {
            "libx264" => "-preset ultrafast -tune zerolatency",
            "libx265" => "-preset ultrafast -tune zerolatency",
            "libsvtav1" => "-preset 12",
            var e when e.EndsWith("_nvenc") => "-preset p1 -zerolatency 1 -rc cbr",
            var e when e.EndsWith("_qsv") => "-preset veryfast -async_depth 1",
            var e when e.EndsWith("_amf") => "-usage ultralowlatency -quality speed",
            var e when e.EndsWith("_videotoolbox") => "-realtime 1",
            var e when e.EndsWith("_mf") => "-hw_encoding 1 -scenario display_remoting",
            _ => ""
        };

        /// <summary>Formato del flusso grezzo per ffmpeg.</summary>
        public static string Format(string codec) => codec switch { "hevc" => "hevc", "av1" => "obu", _ => "h264" };

        /// <summary>Intel Quick Sync: il motore video della grafica integrata Intel.</summary>
        public static bool IsQuickSync(Encoder e) => e.Name.EndsWith("_qsv", StringComparison.Ordinal);

        /// <summary>
        /// Sceglie il codificatore: «auto» = AV1 o H.265 solo con l'hardware, altrimenti H.264 (hardware o x264);
        /// un codec scelto dal visore = il migliore per quel codec. null = riquadri JPEG.
        /// Grafica integrata Intel (Quick Sync): con più monitor collegati non si usa (la stessa grafica che disegna
        /// i monitor ha bloccato tutto il PC mentre codificava); con un monitor solo, in «auto» si usa solo per H.264,
        /// perché i blocchi sono stati visti con hevc_qsv.
        /// </summary>
        public static Encoder? Choose(List<Encoder> have, string[] viewerCan, string wanted, int monitors = 1)
        {
            var usable = have.Where(e => viewerCan.Contains(e.Codec) && !(monitors >= 2 && IsQuickSync(e))).ToList();
            if (wanted == "jpeg" || usable.Count == 0) return null;
            if (wanted is "h264" or "hevc" or "av1")
                return usable.Where(e => e.Codec == wanted).OrderByDescending(e => e.Hardware).FirstOrDefault();
            foreach (var c in Codecs)
            {
                var hw = usable.FirstOrDefault(e => e.Codec == c && e.Hardware && (c == "h264" || !IsQuickSync(e)));
                if (hw != null) return hw;
            }
            return usable.Where(e => e.Codec == "h264").OrderByDescending(e => e.Hardware).FirstOrDefault();
        }

        /// <summary>True se la grafica integrata Intel c'è ma non si usa perché ci sono più monitor.</summary>
        public static bool QuickSyncSkipped(List<Encoder> have, int monitors) => monitors >= 2 && have.Any(IsQuickSync);

        // ------------------------------------------------------------------ scaricamento (Windows 64 bit e ARM64)

        public static string WindowsBuildUrl =>
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-" +
            (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "winarm64" : "win64") + "-gpl.zip";

        public static bool CanDownload => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;

        /// <summary>Scarica ffmpeg.exe nella cartella dati (circa 150 MB da GitHub, una volta sola). "" = fatto.</summary>
        public static async Task<string> DownloadAsync(Action<string> progress, CancellationToken ct, string? folder = null)
        {
            folder ??= LocalFolder;
            if (!CanDownload) return L.T("Lo scaricamento automatico c'è solo su Windows a 64 bit (x64 o ARM64).");
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipdesk-ffmpeg.zip");
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("IP-DESK-CTRL/" + AppVersion.Text);
                using (var resp = await http.GetAsync(WindowsBuildUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? 0;
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = File.Create(tmp);
                    var buf = new byte[1 << 16];
                    long done = 0, lastShown = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        if (done - lastShown > 4 << 20)
                        {
                            lastShown = done;
                            progress(total > 0 ? L.F("Scarico ffmpeg… {0} %", done * 100 / total) : L.F("Scarico ffmpeg… {0} MB", done >> 20));
                        }
                    }
                }
                progress(L.T("Estraggo ffmpeg…"));
                Directory.CreateDirectory(folder);
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return L.T("Nell'archivio scaricato non c'è ffmpeg.exe.");
                    entry.ExtractToFile(System.IO.Path.Combine(folder, "ffmpeg.exe"), true);
                }
                Rescan();
                SessionLog.Write("VIDEO ffmpeg scaricato in " + folder);
                return "";
            }
            catch (OperationCanceledException) { return L.T("Scaricamento annullato."); }
            catch (Exception ex) { return L.T("ffmpeg non scaricato: ") + ex.Message; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        // ------------------------------------------------------------------ processi

        internal static Process? Start(string exe, string args, bool pipes)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = pipes, RedirectStandardOutput = pipes, RedirectStandardError = true
                });
                if (p != null && pipes) PowerThrottle.Disable(p);      // ffmpeg non va rallentato con la finestra ridotta a icona
                return p;
            }
            catch { return null; }
        }

        private static string Run(string exe, string args, int ms)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                if (p == null) return "";
                var o = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                p.WaitForExit(ms);
                return o.Wait(1000) ? o.Result : "";
            }
            catch { return ""; }
        }
    }

    /// <summary>PC comandato: ffmpeg che codifica i fotogrammi; il flusso esce a blocchi da <see cref="Output"/>.</summary>
    public sealed class VideoEncoder : IDisposable
    {
        private readonly Process _p;
        private readonly Stream _in;
        private readonly Thread _reader;
        public string Codec { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Failed { get; private set; }
        public string Error { get; private set; } = "";

        public int Fps { get; }

        public VideoEncoder(Ffmpeg.Encoder enc, int w, int h, int kbps, int fps, Action<byte[]> output)
        {
            Codec = enc.Codec; Width = w; Height = h;
            Fps = fps = Math.Clamp(fps, 5, 60);
            _hw = enc.Hardware;
            string args = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s {w}x{h} -r {fps} -i - -an " +
                          $"-c:v {enc.Name} {Ffmpeg.Options(enc.Name)} -pix_fmt {(enc.Hardware ? "nv12" : "yuv420p")} " +
                          $"-g {fps * 3} -bf 0 -b:v {kbps}k -maxrate {kbps}k -bufsize {Math.Max(200, kbps / 2)}k " +
                          $"-flush_packets 1 -f {Ffmpeg.Format(enc.Codec)} -";
            _p = Ffmpeg.Start(Ffmpeg.Path!, args, true) ?? throw new IOException(L.T("ffmpeg non avviabile"));
            if (_hw) Ffmpeg.HwGuard.Begin(enc.Name);
            _in = _p.StandardInput.BaseStream;
            var err = _p.StandardError;
            _ = Task.Run(async () => { try { string e = await err.ReadToEndAsync(); if (e.Trim().Length > 0) Error = e.Trim(); } catch { } });
            var outS = _p.StandardOutput.BaseStream;
            _reader = new Thread(() =>
            {
                var buf = new byte[256 * 1024];
                try
                {
                    int n;
                    while ((n = outS.Read(buf, 0, buf.Length)) > 0) output(buf[..n]);
                }
                catch { }
                Failed = true;
            }) { IsBackground = true, Name = L.T("video codifica") };
            _reader.Start();
        }

        /// <summary>Un fotogramma BGRA (Width×Height, righe contigue). false = ffmpeg si è fermato.</summary>
        public bool Write(Frame f)
        {
            if (Failed || _p.HasExited) { Failed = true; return false; }
            try
            {
                int row = Width * 4;
                if (f.Stride == row && f.Width == Width && f.Height >= Height) _in.Write(f.Data, 0, row * Height);
                else for (int y = 0; y < Height; y++) _in.Write(f.Data, y * f.Stride, row);
                _in.Flush();
                return true;
            }
            catch { Failed = true; return false; }
        }

        private readonly bool _hw;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _in.Close(); } catch { }
            try { if (!_p.WaitForExit(500)) _p.Kill(); } catch { }
            _p.Dispose();
            if (_hw) Ffmpeg.HwGuard.End();
        }
    }

    /// <summary>Visore: ffmpeg che decodifica il flusso e restituisce fotogrammi BGRA interi.</summary>
    public sealed class VideoDecoder : IDisposable
    {
        private readonly Process _p;
        private readonly Stream _in;
        private readonly object _wlock = new();
        public int Width { get; }
        public int Height { get; }
        public bool Failed { get; private set; }

        public VideoDecoder(string codec, int w, int h, Action<byte[]> frame)
        {
            Width = w; Height = h;
            // niente «-fflags nobuffer»: con quello i pacchetti letti per riconoscere il flusso venivano buttati, e con
            // loro il primo fotogramma intero (IDR); il decodificatore non trovava i riferimenti («Could not find ref
            // with POC 0») e non usciva nessun fotogramma
            string args = "-hide_banner -loglevel error -flags low_delay -probesize 32 -analyzeduration 0 -hwaccel auto " +
                          $"-f {Ffmpeg.Format(codec)} -i - -an -f rawvideo -pix_fmt bgra -s {w}x{h} -flush_packets 1 -";
            _p = Ffmpeg.Start(Ffmpeg.Path!, args, true) ?? throw new IOException(L.T("ffmpeg non avviabile"));
            _in = _p.StandardInput.BaseStream;
            var err = _p.StandardError;
            _ = Task.Run(async () =>
            {
                // gli avvisi di ffmpeg all'inizio del flusso («Could not find ref with POC», «Error constructing the
                // frame RPS»: fotogrammi arrivati prima del primo completo) sono normali e si ripetono a ogni
                // ripartenza: nel registro solo il conteggio
                int skipped = 0;
                var other = new System.Collections.Generic.List<string>();
                try
                {
                    string? line;
                    while ((line = await err.ReadLineAsync()) != null)
                    {
                        if (line.Contains("Could not find ref with POC") || line.Contains("Error constructing the frame RPS")) skipped++;
                        else if (line.Trim().Length > 0 && other.Count < 20) other.Add(line.Trim());
                    }
                }
                catch { }
                if (other.Count > 0) SessionLog.Write("VIDEO decodifica: " + string.Join("\n    ", other));
                if (skipped > 0) SessionLog.Write($"VIDEO decodifica: {skipped} fotogrammi scartati all'avvio del flusso, in attesa del primo completo (normale)");
            });
            var outS = _p.StandardOutput.BaseStream;
            new Thread(() =>
            {
                int size = w * h * 4;
                try
                {
                    while (true)
                    {
                        var b = new byte[size];
                        int got = 0;
                        while (got < size)
                        {
                            int n = outS.Read(b, got, size - got);
                            if (n <= 0) { Failed = true; return; }
                            got += n;
                        }
                        frame(b);
                    }
                }
                catch { Failed = true; }
            }) { IsBackground = true, Name = L.T("video decodifica") }.Start();
        }

        public void Feed(byte[] data)
        {
            if (Failed) return;
            try
            {
                lock (_wlock) { _in.Write(data, 0, data.Length); _in.Flush(); }
            }
            catch { Failed = true; }
        }

        public void Dispose()
        {
            try { _in.Close(); } catch { }
            try { if (!_p.WaitForExit(300)) _p.Kill(); } catch { }
            _p.Dispose();
        }
    }
}
