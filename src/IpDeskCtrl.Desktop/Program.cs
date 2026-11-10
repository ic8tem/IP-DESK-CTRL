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
            try
            {
                if (OperatingSystem.IsLinux()) Screen.ScreenSources.InitLinux();
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
