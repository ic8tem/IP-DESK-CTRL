using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Android
{
    [Activity(
        Label = "IP-DESK-CTRL",
        Theme = "@style/IpDeskTheme",
        Icon = "@drawable/icon",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        public static MainActivity? Current { get; private set; }

        private const int RequestCapture = 4711, RequestPerms = 4712, RequestScan = 4713, RequestSpeech = 4714;
        private TaskCompletionSource<string?>? _scan;
        private TaskCompletionSource<string?>? _speech;
        private TaskCompletionSource<(Result code, Intent? data)>? _capture;
        private TaskCompletionSource<bool>? _perms;
        private WifiManager.MulticastLock? _multicast;

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            AndroidPlatform.Install(this);
            return base.CustomizeAppBuilder(builder).WithInterFont();
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            Current = this;
            base.OnCreate(savedInstanceState);
            try
            {
                // UPnP: senza questo blocco Android scarta le risposte multicast del router
                var wifi = (WifiManager?)ApplicationContext?.GetSystemService(WifiService);
                _multicast = wifi?.CreateMulticastLock("ip-desk-ctrl");
                _multicast?.SetReferenceCounted(false);
                _multicast?.Acquire();
            }
            catch (Exception ex) { ErrorLog.Write("Multicast: " + ex.Message); }
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, RequestPerms + 1);
        }

        protected override void OnDestroy()
        {
            try { _multicast?.Release(); } catch { }
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        /// <summary>Durante un collegamento lo schermo non si spegne.</summary>
        public void KeepAwake(bool on)
        {
            RunOnUiThread(() =>
            {
                if (on) Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
                else Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            });
        }

        /// <summary>Modalità immersiva: niente barra di stato e di navigazione.</summary>
        public void SetImmersive(bool on)
        {
            RunOnUiThread(() =>
            {
                var w = Window;
                if (w == null) return;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    var c = w.InsetsController;
                    if (c == null) return;
                    if (on)
                    {
                        c.Hide(WindowInsets.Type.SystemBars());
                        c.SystemBarsBehavior = 2;   // BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
                    }
                    else c.Show(WindowInsets.Type.SystemBars());
                }
                else
                {
#pragma warning disable CA1422
                    w.DecorView.SystemUiVisibility = on
                        ? (StatusBarVisibility)(SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable)
                        : (StatusBarVisibility)SystemUiFlags.Visible;
#pragma warning restore CA1422
                }
            });
        }

        // ------------------------------------------------------------------ permessi

        /// <summary>Chiede il permesso del microfono (serve per catturare l'audio del telefono).</summary>
        public Task<bool> RequestAudioPermissionAsync()
        {
            if (CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) == Permission.Granted) return Task.FromResult(true);
            _perms = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunOnUiThread(() => RequestPermissions(new[] { global::Android.Manifest.Permission.RecordAudio }, RequestPerms));
            return _perms.Task;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == RequestPerms)
                _perms?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }

        /// <summary>Chiede a Android il consenso a catturare lo schermo (finestra di sistema).</summary>
        public Task<(Result code, Intent? data)> RequestCaptureAsync()
        {
            _capture = new TaskCompletionSource<(Result, Intent?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunOnUiThread(() =>
            {
                try
                {
                    var mpm = (global::Android.Media.Projection.MediaProjectionManager?)GetSystemService(MediaProjectionService);
                    var intent = mpm?.CreateScreenCaptureIntent();
                    if (intent == null) { _capture.TrySetResult((Result.Canceled, null)); return; }
                    StartActivityForResult(intent, RequestCapture);
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Cattura schermo: " + ex);
                    _capture.TrySetResult((Result.Canceled, null));
                }
            });
            return _capture.Task;
        }

        /// <summary>
        /// Lettura di un QR con un'app esterna (lettore compatibile ZXing, «com.google.zxing.client.android.SCAN»).
        /// null = nessuna app installata o lettura annullata: si scrive il numero a mano.
        /// </summary>
        public Task<string?> ScanQrAsync()
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _scan = tcs;
            RunOnUiThread(() =>
            {
                try
                {
                    var intent = new Intent("com.google.zxing.client.android.SCAN");
                    intent.PutExtra("SCAN_MODE", "QR_CODE_MODE");
                    intent.PutExtra("PROMPT_MESSAGE", "Inquadra il QR del numero di IP-DESK-CTRL");
                    StartActivityForResult(intent, RequestScan);
                }
                catch (ActivityNotFoundException)
                {
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Lettura QR: " + ex.Message);
                    tcs.TrySetResult(null);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Dettatura: riconoscimento vocale del telefono (finestra del sistema, lingua del telefono).
        /// null = annullata o nessun servizio di riconoscimento installato.
        /// </summary>
        public Task<string?> DictateAsync()
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _speech = tcs;
            RunOnUiThread(() =>
            {
                try
                {
                    var intent = new Intent(global::Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
                    intent.PutExtra(global::Android.Speech.RecognizerIntent.ExtraLanguageModel, global::Android.Speech.RecognizerIntent.LanguageModelFreeForm);
                    intent.PutExtra(global::Android.Speech.RecognizerIntent.ExtraPrompt, "Parla: il testo va al PC remoto");
                    StartActivityForResult(intent, RequestSpeech);
                }
                catch (ActivityNotFoundException) { tcs.TrySetResult(null); }
                catch (Exception ex) { ErrorLog.Write("Dettatura: " + ex.Message); tcs.TrySetResult(null); }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Mouse collegato al telefono o al tablet (Bluetooth, USB, Samsung DeX): Android trasforma il tasto destro
        /// in «Indietro», che chiuderebbe il collegamento. Lo si ferma qui: il clic destro arriva già come pulsante.
        /// </summary>
        public override bool DispatchKeyEvent(KeyEvent? e)
        {
            if (e != null && e.KeyCode == Keycode.Back && (e.Source & InputSourceType.Mouse) == InputSourceType.Mouse) return true;
            return base.DispatchKeyEvent(e);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == RequestSpeech)
            {
                var list = resultCode == Result.Ok ? data?.GetStringArrayListExtra(global::Android.Speech.RecognizerIntent.ExtraResults) : null;
                _speech?.TrySetResult(list != null && list.Count > 0 ? list[0] : null);
            }
            if (requestCode == RequestCapture) _capture?.TrySetResult((resultCode, data));
            if (requestCode == RequestScan) _scan?.TrySetResult(resultCode == Result.Ok ? data?.GetStringExtra("SCAN_RESULT") : null);
        }
    }
}
