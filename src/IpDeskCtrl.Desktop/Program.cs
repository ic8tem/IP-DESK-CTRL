using System;
using Avalonia;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject?.ToString());
            // per lo script di aggiornamento: stampa solo la versione ed esce
            if (args.Length > 0 && args[0] == "--versione") { Console.WriteLine(AppVersion.Text); return; }
            if (OperatingSystem.IsWindows() && args.Length > 0)
            {
                // servizio di sistema e processo di supporto (UAC, schermata di blocco): niente finestre
                if (args[0] == "--service") { WinService.RunAsService(); return; }
                // chiesto da chi comanda il PC (UAC accettata da chi è al PC): installa il servizio
                if (args[0] == ServiceSetup.Arg) { Environment.Exit(ServiceSetup.InstallHere()); return; }
                if (args[0] == "--helper" && args.Length >= 3) { Environment.Exit(Screen.WinHelper.RunHelper(args[1], args[2])); return; }
                // dall'installatore: scarica ffmpeg nella cartella del programma (lo trovano sia l'utente sia il servizio)
                if (args[0] == "--scarica-ffmpeg")
                {
                    string dir = args.Length > 1 ? args[1] : AppContext.BaseDirectory;
                    string err = Screen.Ffmpeg.DownloadAsync(_ => { }, System.Threading.CancellationToken.None, dir).GetAwaiter().GetResult();
                    if (err.Length > 0) ErrorLog.Write("ffmpeg dall'installatore: " + err);
                    Environment.Exit(err.Length == 0 ? 0 : 1);
                    return;
                }
            }
            // mai come amministratore: i privilegi li usa solo il servizio (vedi Unelevate)
            if (Unelevate.RelaunchIfElevated(args)) return;
            try
            {
                if (OperatingSystem.IsLinux()) Screen.ScreenSources.InitLinux();
                if (OperatingSystem.IsWindows())
                {
                    Screen.WinHelper.InstallHooks();
                    Core.Platform.ServiceState = ServiceSetup.State;
                    Core.Platform.UacPending = ServiceSetup.UacPending;
                    Core.Platform.InstallService = ServiceSetup.InstallAsync;
                    Core.Platform.RestartApp = () =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--riavvio") { UseShellExecute = false }); }
                        catch (Exception ex) { Core.SessionLog.Write("SERVIZIO riavvio del programma non riuscito: " + ex.Message); return; }
                        Environment.Exit(0);
                    };
                }
                // già avviato (magari nascosto vicino all'orologio): gli si chiede di mostrare la finestra e si esce
                bool restart = Array.IndexOf(args, "--riavvio") >= 0 || Array.IndexOf(args, Core.Updater.AfterUpdateArg) >= 0;
                bool got = UI.SingleInstance.Acquire();
                // riavvio (lingua cambiata, aggiornamento): si aspetta che il programma vecchio si chiuda
                for (int i = 0; !got && restart && i < 40; i++) { System.Threading.Thread.Sleep(250); got = UI.SingleInstance.Acquire(); }
                if (!got) return;
                Core.DataPaths.Migrate(service: false);   // AppData → cartella del programma
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                throw;
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
