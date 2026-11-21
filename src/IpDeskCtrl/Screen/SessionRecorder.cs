using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Screen
{
    /// <summary>
    /// Registrazione della sessione sul visore: l'immagine del PC remoto, così come arriva, va in un file MP4
    /// (H.264, 8 fotogrammi al secondo) con ffmpeg. Se cambia la risoluzione si chiude il file e se ne apre un altro
    /// (…_parte2.mp4). Solo l'immagine, senza audio.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private const int Fps = 8;
        private readonly TileDecoder _dec;
        private readonly string _baseName;
        private readonly CancellationTokenSource _cts = new();
        private Process? _p;
        private Stream? _in;
        private int _w, _h, _part;
        private byte[] _buf = Array.Empty<byte>();

        /// <summary>File in corso (l'ultimo, se ce ne sono più di uno).</summary>
        public string File { get; private set; } = "";
        public DateTime Started { get; } = DateTime.Now;
        public string Error { get; private set; } = "";

        /// <summary>Cartella delle registrazioni: Video\IP-DESK-CTRL (o la cartella personale).</summary>
        public static string Folder
        {
            get
            {
                string v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (v.Length == 0) v = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(v, "IP-DESK-CTRL");
            }
        }

        public static bool Available => !Platform.IsMobile && Ffmpeg.Path != null;

        private SessionRecorder(TileDecoder dec, string who)
        {
            _dec = dec;
            Directory.CreateDirectory(Folder);
            _baseName = Path.Combine(Folder, $"{who.Replace(' ', '-')}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
        }

        /// <summary>Avvia la registrazione; null se ffmpeg manca o non parte (motivo in <paramref name="error"/>).</summary>
        public static SessionRecorder? Start(TileDecoder dec, string who, out string error)
        {
            error = "";
            if (!Available) { error = L.T("per registrare serve ffmpeg su questo PC"); return null; }
            try
            {
                var r = new SessionRecorder(dec, who);
                _ = Task.Run(r.LoopAsync);
                SessionLog.Write($"REGISTRAZIONE iniziata: {r._baseName}.mp4");
                return r;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private async Task LoopAsync()
        {
            var tick = TimeSpan.FromMilliseconds(1000.0 / Fps);
            var sw = Stopwatch.StartNew();
            long n = 0;
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    int w, h;
                    lock (_dec.Lock)
                    {
                        var img = _dec.Image;
                        w = img.Width; h = img.Height;
                        if (w > 0 && h > 0)
                        {
                            int len = w * 4 * h;
                            if (_buf.Length != len) _buf = new byte[len];
                            if (img.Stride == w * 4) Buffer.BlockCopy(img.Data, 0, _buf, 0, len);
                            else for (int y = 0; y < h; y++) Buffer.BlockCopy(img.Data, y * img.Stride, _buf, y * w * 4, w * 4);
                        }
                    }
                    if (w > 0 && h > 0)
                    {
                        if (w != _w || h != _h) Open(w, h);
                        if (_in != null) await _in.WriteAsync(_buf, 0, w * 4 * h, _cts.Token);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    SessionLog.Write("REGISTRAZIONE interrotta: " + ex.Message);
                    break;
                }
                n++;
                var wait = TimeSpan.FromTicks(tick.Ticks * n) - sw.Elapsed;
                if (wait > TimeSpan.Zero) { try { await Task.Delay(wait, _cts.Token); } catch { break; } }
            }
            CloseCurrent();
        }

        private void Open(int w, int h)
        {
            CloseCurrent();
            _w = w; _h = h; _part++;
            File = _baseName + (_part > 1 ? $"_parte{_part}" : "") + ".mp4";
            string args = $"-hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s {w}x{h} -r {Fps} -i - " +
                          "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -preset veryfast -crf 26 -pix_fmt yuv420p " +
                          $"-movflags +faststart -y \"{File}\"";
            var p = Process.Start(new ProcessStartInfo(Ffmpeg.Path!, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
            }) ?? throw new IOException(L.T("ffmpeg non parte"));
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) SessionLog.Write("REGISTRAZIONE ffmpeg: " + e.Data); };
            p.BeginErrorReadLine();
            _p = p;
            _in = p.StandardInput.BaseStream;
            if (_part > 1) SessionLog.Write($"REGISTRAZIONE risoluzione cambiata ({w}×{h}): continuo in {File}");
        }

        private void CloseCurrent()
        {
            var p = _p; _p = null;
            var s = _in; _in = null;
            if (p == null) return;
            try { s?.Flush(); s?.Dispose(); } catch { }
            try { if (!p.WaitForExit(15000)) p.Kill(); } catch { }
            p.Dispose();
        }

        /// <summary>Ferma e chiude il file (attende che ffmpeg finisca di scriverlo).</summary>
        public void Dispose()
        {
            if (_cts.IsCancellationRequested) return;
            _cts.Cancel();
            SessionLog.Write($"REGISTRAZIONE fermata dopo {(int)(DateTime.Now - Started).TotalMinutes} min: {File}");
        }
    }
}
