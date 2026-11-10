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

        private const int RequestCapture = 4711, RequestPerms = 4712;
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

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == RequestCapture) _capture?.TrySetResult((resultCode, data));
        }
    }
}
