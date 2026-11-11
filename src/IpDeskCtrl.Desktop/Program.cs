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
            if (OperatingSystem.IsWindows() && args.Length > 0)
            {
                // servizio di sistema e processo di supporto (UAC, schermata di blocco): niente finestre
                if (args[0] == "--service") { WinService.RunAsService(); return; }
                if (args[0] == "--helper" && args.Length >= 3) { Environment.Exit(Screen.WinHelper.RunHelper(args[1], args[2])); return; }
            }
            try
            {
                if (OperatingSystem.IsLinux()) Screen.ScreenSources.InitLinux();
                if (OperatingSystem.IsWindows()) Screen.WinHelper.InstallHooks();
                // già avviato (magari nascosto vicino all'orologio): gli si chiede di mostrare la finestra e si esce
                if (!UI.SingleInstance.Acquire()) return;
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
