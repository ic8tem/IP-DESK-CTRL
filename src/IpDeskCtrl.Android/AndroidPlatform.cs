using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using IpDeskCtrl.Audio;
using IpDeskCtrl.Core;
using IpDeskCtrl.Input;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Android
{
    /// <summary>Collega il codice comune alle funzioni di Android (schermo, tocchi, audio, permessi).</summary>
    public static class AndroidPlatform
    {
        public static void Install(MainActivity a)
        {
            Platform.IsMobile = true;
            Platform.DataFolder = Path.Combine(a.FilesDir!.AbsolutePath, "IP-DESK-CTRL");
            Platform.ReceiveFolder = Path.Combine((a.GetExternalFilesDir(null) ?? a.FilesDir!).AbsolutePath, "Ricevuti");
            Platform.Screen = () => new AndroidScreen();
            Platform.Input = () => new AndroidInput();
            Platform.AudioCapture = () => new AndroidAudioCapture();
            Platform.AudioPlayer = () => new AndroidAudioPlayer();
            Platform.MicCapture = () => new AndroidMicCapture();
            Platform.ShareText = (subject, text) =>
            {
                try
                {
                    var send = new Intent(Intent.ActionSend);
                    send.SetType("text/plain");
                    send.PutExtra(Intent.ExtraSubject, subject);
                    send.PutExtra(Intent.ExtraText, text);
                    var chooser = Intent.CreateChooser(send, L.T("Condividi il registro"));
                    chooser!.AddFlags(ActivityFlags.NewTask);
                    a.StartActivity(chooser);
                }
                catch (Exception ex) { ErrorLog.Write("Condivisione del registro: " + ex.Message); }
            };
            Platform.Dictate = () => MainActivity.Current?.DictateAsync() ?? Task.FromResult<string?>(null);
            IpDeskCtrl.Core.VolumeControl.Platform = action =>
            {
                try
                {
                    var am = (AudioManager?)a.GetSystemService(Context.AudioService);
                    if (am == null) return L.T("Volume non disponibile");
                    var adj = action == IpDeskCtrl.Core.VolumeControl.Up ? Adjust.Raise : action == IpDeskCtrl.Core.VolumeControl.Down ? Adjust.Lower : Adjust.ToggleMute;
                    am.AdjustStreamVolume(global::Android.Media.Stream.Music, adj, VolumeNotificationFlags.ShowUi);
                    return "";
                }
                catch (Exception ex) { return L.T("Volume non cambiato: ") + ex.Message; }
            };
            Platform.PrepareHost = PrepareHostAsync;
            Platform.HostEnded = () =>
            {
                var act = MainActivity.Current;
                if (act == null) return;
                CaptureService.Stop(act);
                act.KeepAwake(false);
            };
            Platform.FullScreen = on => MainActivity.Current?.SetImmersive(on);
            Platform.ScanQr = () => MainActivity.Current?.ScanQrAsync() ?? Task.FromResult<string?>(null);
            Platform.Open = path =>
            {
                var act = MainActivity.Current;
                if (act == null) return;
                if (path.StartsWith("http://") || path.StartsWith("https://"))
                {
                    // link (nuova versione, APK): browser del telefono
                    act.RunOnUiThread(() =>
                    {
                        try { act.StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(path))); }
                        catch { Toast.MakeText(act, path, ToastLength.Long)?.Show(); }
                    });
                    return;
                }
                act.RunOnUiThread(() => Toast.MakeText(act, L.T("File in: ") + path, ToastLength.Long)?.Show());
            };
        }

        public static void OnProjectionStopped() => SessionLog.Write("ANDROID cattura dello schermo fermata dal sistema o dall'utente");

        /// <summary>Prima di farsi vedere: consenso alla cattura, servizio in primo piano, permesso audio, accessibilità.</summary>
        private static async Task<string> PrepareHostAsync()
        {
            var act = MainActivity.Current;
            if (act == null) return L.T("Sul telefono l'app non è in primo piano: aprila e riprova.");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) await act.RequestAudioPermissionAsync();
            var (code, data) = await act.RequestCaptureAsync();
            if (code != Result.Ok || data == null) return L.T("L'utente del telefono non ha autorizzato la cattura dello schermo.");
            var mp = await CaptureService.StartAsync(act, code, data);
            if (mp == null) return L.T("Sul telefono la cattura dello schermo non è partita.");
            act.KeepAwake(true);
            if (InputService.Current == null)
            {
                act.RunOnUiThread(() =>
                {
                    try { act.StartActivity(new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings)); } catch { }
                    Toast.MakeText(act, L.T("Attiva «IP-DESK-CTRL» tra i servizi di Accessibilità, poi torna all'app"), ToastLength.Long)?.Show();
                });
                return L.T("Sul telefono è spento il servizio di Accessibilità di IP-DESK-CTRL: tocchi e testo arriveranno appena l'utente lo attiva ") +
                       L.T("(Impostazioni › Accessibilità › App installate › IP-DESK-CTRL).");
            }
            return "";
        }

        public static (int w, int h, int dpi) ScreenSize()
        {
            var ctx = global::Android.App.Application.Context;
            var wm = ctx.GetSystemService(Context.WindowService).JavaCast<IWindowManager>()!;
            int dpi = (int)(ctx.Resources?.DisplayMetrics?.DensityDpi ?? DisplayMetricsDensity.Default);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var b = wm.CurrentWindowMetrics.Bounds;
                return (b.Width(), b.Height(), dpi);
            }
            var dm = new DisplayMetrics();
#pragma warning disable CA1422
            wm.DefaultDisplay!.GetRealMetrics(dm);
#pragma warning restore CA1422
            return (dm.WidthPixels, dm.HeightPixels, dpi);
        }
    }

    // ===================================================================== schermo

    /// <summary>
    /// Cattura con MediaProjection su un VirtualDisplay che scrive in un ImageReader (RGBA). L'immagine è
    /// ridotta a 1280 punti sul lato lungo (basta e alleggerisce la rete); i tocchi usano i pixel veri.
    /// </summary>
    public sealed class AndroidScreen : IScreenSource
    {
        private VirtualDisplay? _vd;
        private ImageReader? _reader;
        private int _tw, _th;
        private byte[]? _last;
        private int _lastW, _lastH;
        private readonly object _lock = new();
        public string Note { get; private set; } = "";

        public List<MonitorInfo> Monitors()
        {
            var (w, h, _) = AndroidPlatform.ScreenSize();
            return new List<MonitorInfo> { new(L.T("Schermo"), 0, 0, w, h, true) };
        }

        public bool Capture(MonitorInfo area, Frame into)
        {
            lock (_lock)
            {
                var mp = CaptureService.Projection;
                if (mp == null) { Note = L.T("Sul telefono la cattura dello schermo è ferma (non autorizzata o interrotta)."); return false; }
                var (rw, rh, dpi) = AndroidPlatform.ScreenSize();
                double k = Math.Min(1.0, 1280.0 / Math.Max(rw, rh));
                int tw = Math.Max(2, (int)(rw * k) & ~1), th = Math.Max(2, (int)(rh * k) & ~1);
                try
                {
                    if (_reader == null || tw != _tw || th != _th)
                    {
                        var old = _reader;
                        _reader = ImageReader.NewInstance(tw, th, (global::Android.Graphics.ImageFormatType)1 /* RGBA_8888 */, 2);
                        if (_vd == null)
                            _vd = mp.CreateVirtualDisplay("ip-desk-ctrl", tw, th, dpi, (global::Android.Views.DisplayFlags)16 /* VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR */, _reader.Surface, null, null);
                        else
                        {
                            // da Android 14 il VirtualDisplay si crea una volta sola: alla rotazione lo si ridimensiona
                            _vd.Resize(tw, th, dpi);
                            _vd.Surface = _reader.Surface;
                        }
                        old?.Close();
                        _tw = tw; _th = th;
                        _last = null;
                    }
                }
                catch (Exception ex) { Note = L.T("Cattura dello schermo non disponibile: ") + ex.Message; return false; }

                for (int wait = 0; wait < 10; wait++)
                {
                    var img = _reader.AcquireLatestImage();
                    if (img != null)
                    {
                        try { Copy(img, into); }
                        finally { img.Close(); }
                        Note = "";
                        return true;
                    }
                    // nessun fotogramma nuovo: lo schermo non è cambiato, si rimanda l'ultimo
                    if (_last != null)
                    {
                        into.Ensure(_lastW, _lastH);
                        Buffer.BlockCopy(_last, 0, into.Data, 0, _last.Length);
                        return true;
                    }
                    Thread.Sleep(50);
                }
                return false;
            }
        }

        private void Copy(Image img, Frame into)
        {
            var plane = img.GetPlanes()![0];
            int w = img.Width, h = img.Height, rs = plane.RowStride, ps = plane.PixelStride;
            IntPtr addr = plane.Buffer!.GetDirectBufferAddress();
            into.Ensure(w, h);
            var row = new byte[rs];
            var d = into.Data;
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(addr + y * rs, row, 0, Math.Min(rs, w * ps));
                int o = y * into.Stride;
                for (int x = 0; x < w; x++)
                {
                    int i = x * ps;
                    d[o] = row[i + 2]; d[o + 1] = row[i + 1]; d[o + 2] = row[i]; d[o + 3] = 255;   // RGBA → BGRA
                    o += 4;
                }
            }
            if (_last == null || _last.Length != d.Length) _last = new byte[d.Length];
            Buffer.BlockCopy(d, 0, _last, 0, d.Length);
            _lastW = w; _lastH = h;
        }

        public (int x, int y)? Cursor() => null;

        public void Dispose()
        {
            lock (_lock)
            {
                try { _vd?.Release(); } catch { }
                try { _reader?.Close(); } catch { }
                _vd = null; _reader = null;
            }
        }
    }

    // ===================================================================== tocchi e testo

    /// <summary>
    /// Servizio di Accessibilità: l'unico modo previsto da Android per toccare lo schermo per conto di
    /// un altro (va attivato a mano dall'utente in Impostazioni › Accessibilità).
    /// </summary>
    [Service(Label = "IP-DESK-CTRL", Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
    [IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
    [MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service")]
    public class InputService : AccessibilityService
    {
        public static InputService? Current { get; private set; }

        protected override void OnServiceConnected()
        {
            base.OnServiceConnected();
            Current = this;
        }

        public override bool OnUnbind(Intent? intent)
        {
            Current = null;
            return base.OnUnbind(intent);
        }

        public override void OnAccessibilityEvent(AccessibilityEvent? e) { }
        public override void OnInterrupt() { }
    }

    /// <summary>
    /// Mouse e tastiera del PC → gesti sul telefono:
    ///   clic = tocco; tenuto fermo = tocco lungo; trascinato = scorrimento lungo il percorso;
    ///   tasto destro / Esc = Indietro; tasto centrale / Win = Home; rotella = scorrimento;
    ///   testo e Backspace = scritti nel campo di testo che ha il fuoco; Invio = azione della tastiera.
    /// </summary>
    public sealed class AndroidInput : IInputSink
    {
        private float _x, _y;
        private bool _down;
        private long _t0;
        private readonly List<(float x, float y)> _path = new();
        public string Note { get; private set; } = "";

        private InputService? Svc()
        {
            var s = InputService.Current;
            Note = s == null ? L.T("Sul telefono il servizio di Accessibilità di IP-DESK-CTRL è spento: tocchi e testo non arrivano.") : "";
            return s;
        }

        public void MoveTo(int x, int y)
        {
            _x = Math.Max(0, x); _y = Math.Max(0, y);
            if (_down) _path.Add((_x, _y));
        }

        public void Button(int button, bool down)
        {
            var s = Svc();
            if (s == null) return;
            if (button == MouseBtn.Left)
            {
                if (down) { _down = true; _t0 = System.Environment.TickCount64; _path.Clear(); _path.Add((_x, _y)); return; }
                if (!_down) return;
                _down = false;
                long dt = System.Environment.TickCount64 - _t0;
                var (sx, sy) = _path[0];
                double moved = Math.Sqrt((_x - sx) * (_x - sx) + (_y - sy) * (_y - sy));
                if (moved < 15) Gesture(s, new[] { (sx, sy) }, dt > 500 ? 700 : 60);
                else Gesture(s, _path.ToArray(), Math.Clamp(dt, 100, 3000));
                return;
            }
            if (down) return;
            switch (button)
            {
                case MouseBtn.Right: case MouseBtn.Back: s.PerformGlobalAction(GlobalAction.Back); break;
                case MouseBtn.Middle: s.PerformGlobalAction(GlobalAction.Home); break;
                case MouseBtn.Forward: s.PerformGlobalAction(GlobalAction.Recents); break;
            }
        }

        public void Wheel(int dx, int dy)
        {
            var s = Svc();
            if (s == null) return;
            var (w, h, _) = AndroidPlatform.ScreenSize();
            float ddy = dy / 120f * h / 6f, ddx = dx / 120f * w / 6f;
            float x1 = Math.Clamp(_x + ddx, 1, w - 2), y1 = Math.Clamp(_y + ddy, 1, h - 2);
            Gesture(s, new[] { (_x, _y), (x1, y1) }, 250);
        }

        private static void Gesture(InputService s, (float x, float y)[] pts, long ms)
        {
            var path = new global::Android.Graphics.Path();
            path.MoveTo(pts[0].x, pts[0].y);
            if (pts.Length == 1) path.LineTo(pts[0].x, pts[0].y);
            for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i].x, pts[i].y);
            var g = new GestureDescription.Builder().AddStroke(new GestureDescription.StrokeDescription(path, 0, Math.Max(1, ms))).Build();
            s.DispatchGesture(g, null, null);
        }

        public void Key(int evdev, bool down)
        {
            if (!down) return;
            var s = Svc();
            if (s == null) return;
            switch (evdev)
            {
                case KeyMap.Escape: s.PerformGlobalAction(GlobalAction.Back); break;
                case KeyMap.LeftMeta: case KeyMap.RightMeta: s.PerformGlobalAction(GlobalAction.Home); break;
                case 14: EditText(s, t => t.Length > 0 ? t[..^1] : t); break;     // Backspace
                case 28:                                                         // Invio
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                        Focused(s)?.PerformAction((global::Android.Views.Accessibility.Action)AccessibilityNodeInfo.AccessibilityAction.ActionImeEnter!.Id);
                    break;
            }
        }

        public void Text(string text)
        {
            var s = Svc();
            if (s != null) EditText(s, t => t + text);
        }

        private static AccessibilityNodeInfo? Focused(InputService s) => s.FindFocus(NodeFocus.Input);

        private void EditText(InputService s, Func<string, string> change)
        {
            var n = Focused(s);
            if (n == null || !n.Editable) { Note = L.T("Sul telefono non c'è un campo di testo selezionato: toccalo prima."); return; }
            string cur = n.Text?.ToString() ?? "";
            if (n.ShowingHintText) cur = "";
            var args = new Bundle();
            args.PutCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence, new Java.Lang.String(change(cur)));
            n.PerformAction((global::Android.Views.Accessibility.Action)0x200000 /* ACTION_SET_TEXT */, args);
        }

        public void Dispose() { }
    }

    // ===================================================================== audio

    /// <summary>Audio che il telefono sta suonando (Android 10+, non le app che lo vietano).</summary>
    public sealed class AndroidAudioCapture : ISystemAudioCapture
    {
        private AudioRecord? _rec;
        private Thread? _t;
        private volatile bool _run;
        public string Note { get; private set; } = "";

        public bool Start(Action<short[]> onFrame)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Q) { Note = L.T("Per mandare l'audio del telefono serve Android 10 o successivo."); return false; }
            var mp = CaptureService.Projection;
            if (mp == null) { Note = L.T("Audio del telefono non disponibile (cattura non autorizzata)."); return false; }
            try
            {
                var cfg = new AudioPlaybackCaptureConfiguration.Builder(mp)
                    .AddMatchingUsage(AudioUsageKind.Media)
                    .AddMatchingUsage(AudioUsageKind.Game)
                    .AddMatchingUsage(AudioUsageKind.Unknown)
                    .Build();
                var fmt = new AudioFormat.Builder()
                    .SetEncoding(Encoding.Pcm16bit)!
                    .SetSampleRate(Adpcm.Rate)!
                    .SetChannelMask(ChannelOut.Stereo)!
                    .Build();
                _rec = new AudioRecord.Builder()
                    .SetAudioFormat(fmt!)
                    .SetBufferSizeInBytes(Adpcm.FrameSamples * 4 * 8)
                    .SetAudioPlaybackCaptureConfig(cfg)
                    .Build();
                _rec.StartRecording();
                _run = true;
                var rec = _rec;
                _t = new Thread(() =>
                {
                    var buf = new short[Adpcm.FrameSamples * 2];
                    while (_run)
                    {
                        int got = 0;
                        while (got < buf.Length && _run)
                        {
                            int n = rec.Read(buf, got, buf.Length - got);
                            if (n <= 0) { Thread.Sleep(10); continue; }
                            got += n;
                        }
                        if (_run) onFrame((short[])buf.Clone());
                    }
                }) { IsBackground = true, Name = L.T("audio telefono") };
                _t.Start();
                return true;
            }
            catch (Exception ex)
            {
                Note = L.T("Audio del telefono non catturabile (serve il permesso Microfono): ") + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            _run = false;
            try { _rec?.Stop(); } catch { }
            try { _rec?.Release(); } catch { }
            _rec = null;
        }
    }

    /// <summary>Microfono del telefono, mandato al PC remoto (il telefono come visore che parla di là).</summary>
    public sealed class AndroidMicCapture : ISystemAudioCapture
    {
        private AudioRecord? _rec;
        private volatile bool _run;
        public string Note { get; private set; } = "";

        public bool Start(Action<short[]> onFrame)
        {
            var act = MainActivity.Current;
            if (act != null && act.CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != global::Android.Content.PM.Permission.Granted)
            {
                _ = act.RequestAudioPermissionAsync();
                Note = L.T("Concedi a IP-DESK-CTRL il permesso «Microfono», poi riaccendi il microfono.");
                return false;
            }
            try
            {
                int min = AudioRecord.GetMinBufferSize(Adpcm.Rate, ChannelIn.Mono, Encoding.Pcm16bit);
                _rec = new AudioRecord(AudioSource.VoiceCommunication, Adpcm.Rate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(min, Adpcm.FrameSamples * 2 * 8));
                if (_rec.State != global::Android.Media.State.Initialized) { Note = L.T("Microfono del telefono non disponibile."); _rec.Release(); _rec = null; return false; }
                _rec.StartRecording();
                _run = true;
                var rec = _rec;
                new Thread(() =>
                {
                    var mono = new short[Adpcm.FrameSamples];
                    while (_run)
                    {
                        int got = 0;
                        while (got < mono.Length && _run)
                        {
                            int n = rec.Read(mono, got, mono.Length - got);
                            if (n <= 0) { Thread.Sleep(10); continue; }
                            got += n;
                        }
                        if (!_run) break;
                        var st = new short[mono.Length * 2];
                        for (int i = 0; i < mono.Length; i++) st[i * 2] = st[i * 2 + 1] = mono[i];
                        onFrame(st);
                    }
                }) { IsBackground = true, Name = "microfono" }.Start();
                return true;
            }
            catch (Exception ex) { Note = L.T("Microfono del telefono non disponibile: ") + ex.Message; return false; }
        }

        public void Dispose()
        {
            _run = false;
            try { _rec?.Stop(); } catch { }
            try { _rec?.Release(); } catch { }
            _rec = null;
        }
    }

    public sealed class AndroidAudioPlayer : IAudioPlayer
    {
        private readonly AudioTrack? _track;
        private readonly BlockingCollection<short[]> _q = new(new ConcurrentQueue<short[]>());
        public string Note { get; } = "";

        public AndroidAudioPlayer()
        {
            try
            {
                int min = AudioTrack.GetMinBufferSize(Adpcm.Rate, ChannelOut.Stereo, Encoding.Pcm16bit);
                _track = new AudioTrack.Builder()
                    .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!)
                    .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit)!.SetSampleRate(Adpcm.Rate)!.SetChannelMask(ChannelOut.Stereo)!.Build()!)
                    .SetBufferSizeInBytes(Math.Max(min, Adpcm.FrameSamples * 4 * 6))
                    .SetTransferMode(AudioTrackMode.Stream)
                    .Build();
                _track.Play();
                var t = _track;
                new Thread(() =>
                {
                    try { foreach (var f in _q.GetConsumingEnumerable()) t.Write(f, 0, f.Length); } catch { }
                }) { IsBackground = true, Name = L.T("audio remoto") }.Start();
            }
            catch (Exception ex) { Note = L.T("Uscita audio non disponibile: ") + ex.Message; }
        }

        public void Play(short[] s)
        {
            if (_track == null) return;
            while (_q.Count > 15) _q.TryTake(out _);
            _q.Add(s);
        }

        public void Dispose()
        {
            _q.CompleteAdding();
            try { _track?.Stop(); } catch { }
            try { _track?.Release(); } catch { }
        }
    }
}
