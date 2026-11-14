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
        public static string LocalFolder => System.IO.Path.Combine(AppSettings.Folder, "ffmpeg");

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

        private static List<Encoder> ProbeEncoders()
        {
            var ok = new List<Encoder>();
            string? exe = Path;
            if (exe == null) return ok;
            string list = Run(exe, "-hide_banner -encoders", 8000);
            foreach (var (codec, name, hw) in Candidates)
            {
                if (!list.Contains(" " + name + " ")) continue;
                if (ok.Any(e => e.Codec == codec && e.Hardware)) continue;     // per ogni codec basta un hardware
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

        /// <summary>
        /// Sceglie il codificatore: «auto» = AV1 o H.265 solo con l'hardware, altrimenti H.264 (hardware o x264);
        /// un codec scelto dal visore = il migliore per quel codec. null = riquadri JPEG.
        /// </summary>
        public static Encoder? Choose(List<Encoder> have, string[] viewerCan, string wanted)
        {
            var usable = have.Where(e => viewerCan.Contains(e.Codec)).ToList();
            if (wanted == "jpeg" || usable.Count == 0) return null;
            if (wanted is "h264" or "hevc" or "av1")
                return usable.Where(e => e.Codec == wanted).OrderByDescending(e => e.Hardware).FirstOrDefault();
            foreach (var c in Codecs)
            {
                var hw = usable.FirstOrDefault(e => e.Codec == c && e.Hardware);
                if (hw != null) return hw;
            }
            return usable.FirstOrDefault(e => e.Codec == "h264");
        }

        // ------------------------------------------------------------------ scaricamento (Windows 64 bit e ARM64)

        public static string WindowsBuildUrl =>
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-" +
            (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "winarm64" : "win64") + "-gpl.zip";

        public static bool CanDownload => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;

        /// <summary>Scarica ffmpeg.exe nella cartella dati (circa 150 MB da GitHub, una volta sola). "" = fatto.</summary>
        public static async Task<string> DownloadAsync(Action<string> progress, CancellationToken ct, string? folder = null)
        {
            folder ??= LocalFolder;
            if (!CanDownload) return "Lo scaricamento automatico c'è solo su Windows a 64 bit (x64 o ARM64).";
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
                            progress(total > 0 ? $"Scarico ffmpeg… {done * 100 / total} %" : $"Scarico ffmpeg… {done >> 20} MB");
                        }
                    }
                }
                progress("Estraggo ffmpeg…");
                Directory.CreateDirectory(folder);
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) return "Nell'archivio scaricato non c'è ffmpeg.exe.";
                    entry.ExtractToFile(System.IO.Path.Combine(folder, "ffmpeg.exe"), true);
                }
                Rescan();
                SessionLog.Write("VIDEO ffmpeg scaricato in " + folder);
                return "";
            }
            catch (OperationCanceledException) { return "Scaricamento annullato."; }
            catch (Exception ex) { return "ffmpeg non scaricato: " + ex.Message; }
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

        public VideoEncoder(Ffmpeg.Encoder enc, int w, int h, int kbps, Action<byte[]> output)
        {
            Codec = enc.Codec; Width = w; Height = h;
            string args = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s {w}x{h} -r 25 -i - -an " +
                          $"-c:v {enc.Name} {Ffmpeg.Options(enc.Name)} -pix_fmt {(enc.Hardware ? "nv12" : "yuv420p")} " +
                          $"-g 75 -bf 0 -b:v {kbps}k -maxrate {kbps}k -bufsize {Math.Max(200, kbps / 2)}k " +
                          $"-flush_packets 1 -f {Ffmpeg.Format(enc.Codec)} -";
            _p = Ffmpeg.Start(Ffmpeg.Path!, args, true) ?? throw new IOException("ffmpeg non avviabile");
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
            }) { IsBackground = true, Name = "video codifica" };
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

        public void Dispose()
        {
            try { _in.Close(); } catch { }
            try { if (!_p.WaitForExit(500)) _p.Kill(); } catch { }
            _p.Dispose();
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
            string args = "-hide_banner -loglevel error -fflags nobuffer -flags low_delay -probesize 32 -analyzeduration 0 -hwaccel auto " +
                          $"-f {Ffmpeg.Format(codec)} -i - -an -f rawvideo -pix_fmt bgra -s {w}x{h} -flush_packets 1 -";
            _p = Ffmpeg.Start(Ffmpeg.Path!, args, true) ?? throw new IOException("ffmpeg non avviabile");
            _in = _p.StandardInput.BaseStream;
            var err = _p.StandardError;
            _ = Task.Run(async () => { try { string e = await err.ReadToEndAsync(); if (e.Trim().Length > 0) SessionLog.Write("VIDEO decodifica: " + e.Trim()); } catch { } });
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
            }) { IsBackground = true, Name = "video decodifica" }.Start();
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
