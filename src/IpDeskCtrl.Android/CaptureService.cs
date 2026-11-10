using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;
using Android.OS;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Android
{
    /// <summary>
    /// Servizio in primo piano «cattura schermo» (obbligatorio da Android 10 per MediaProjection): tiene viva
    /// l'app mentre un PC vede questo telefono e mostra la notifica «IP-DESK-CTRL: collegamento attivo».
    /// </summary>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
    public class CaptureService : Service
    {
        private const string Channel = "ipdesk-capture";
        private const int NotificationId = 1;

        public static MediaProjection? Projection { get; private set; }
        private static TaskCompletionSource<MediaProjection?>? _ready;
        private static Result _code;
        private static Intent? _data;

        private sealed class StopCallback : MediaProjection.Callback
        {
            public override void OnStop()
            {
                Projection = null;
                AndroidPlatform.OnProjectionStopped();
            }
        }

        /// <summary>Avvia il servizio con il consenso dato dall'utente e restituisce la MediaProjection.</summary>
        public static Task<MediaProjection?> StartAsync(Context ctx, Result code, Intent data)
        {
            _ready = new TaskCompletionSource<MediaProjection?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _code = code;
            _data = data;
            var i = new Intent(ctx, typeof(CaptureService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O) ctx.StartForegroundService(i); else ctx.StartService(i);
            return _ready.Task;
        }

        public static void Stop(Context ctx)
        {
            try { Projection?.Stop(); } catch { }
            Projection = null;
            try { ctx.StopService(new Intent(ctx, typeof(CaptureService))); } catch { }
        }

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            try
            {
                var nm = (NotificationManager?)GetSystemService(NotificationService);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    nm?.CreateNotificationChannel(new NotificationChannel(Channel, "Collegamento attivo", NotificationImportance.Low));
                var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
                var b = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, Channel) : new Notification.Builder(this);
                var n = b.SetContentTitle("IP-DESK-CTRL: collegamento attivo")
                    .SetContentText("Un PC vede questo dispositivo. Apri l'app per disconnetterlo.")
                    .SetSmallIcon(Resource.Drawable.icon)
                    .SetOngoing(true)
                    .SetContentIntent(open)
                    .Build();
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(NotificationId, n, ForegroundService.TypeMediaProjection);
                else StartForeground(NotificationId, n);

                // il consenso si usa una volta sola, dopo l'avvio in primo piano (obbligatorio da Android 14)
                var data = _data;
                _data = null;
                var mpm = (MediaProjectionManager?)GetSystemService(MediaProjectionService);
                var mp = data == null ? null : mpm?.GetMediaProjection((int)_code, data);
                if (mp != null)
                {
                    mp.RegisterCallback(new StopCallback(), new Handler(Looper.MainLooper!));
                    Projection = mp;
                }
                _ready?.TrySetResult(mp);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Servizio di cattura: " + ex);
                _ready?.TrySetResult(null);
            }
            return StartCommandResult.NotSticky;
        }

        public override void OnDestroy()
        {
            try { Projection?.Stop(); } catch { }
            Projection = null;
            base.OnDestroy();
        }
    }
}
