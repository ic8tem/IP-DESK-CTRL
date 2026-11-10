using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IpDeskCtrl.Core;
using IpDeskCtrl.UI;

namespace IpDeskCtrl
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            Resources["MonoFont"] = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, Roboto Mono, monospace");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // un errore in un pulsante o nel disegno non deve chiudere il programma: lo registra e prosegue
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception.ToString());
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write(e.Exception.ToString());
                e.SetObserved();
            };
            var s = AppSettings.Load();
            Themes.Apply(s.Theme);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                var main = new MainWindow(s);
                desktop.MainWindow = main;
                desktop.ShutdownRequested += (_, _) => main.Shutdown();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            {
                // Android: una sola vista con la schermata principale e, sopra, collegamenti e dialoghi
                var root = new SingleViewShell();
                Shell.Current = root;
                Platform.Clipboard = new AvaloniaClipboard(() => root.Top);
                root.SetMain(new MainView(s));
                single.MainView = root;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
