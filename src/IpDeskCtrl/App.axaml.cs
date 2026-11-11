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
                // la X nasconde soltanto: si esce dall'icona vicino all'orologio o da Impostazioni › Esci dal programma
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                var main = new MainWindow(s);
                void Quit()
                {
                    Tray.Remove();
                    main.Quit();
                    desktop.Shutdown();
                }
                MainView.Quit = Quit;
                Tray.Install(this, main, Quit);
                SingleInstance.Watch(main.ShowMain);
                bool hidden = Array.Exists(desktop.Args ?? Array.Empty<string>(), a => a == "--nascosto");
                if (hidden) main.StartHidden();      // avvio all'accesso: solo l'icona, niente finestra
                else desktop.MainWindow = main;
                desktop.ShutdownRequested += (_, _) => main.Shutdown();
                try
                {
                    // macOS: clic sull'icona del Dock con la finestra nascosta
                    var act = this.TryGetFeature<IActivatableLifetime>();
                    if (act != null) act.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) main.ShowMain(); };
                }
                catch (Exception ex) { ErrorLog.Write("Riapertura dal Dock: " + ex.Message); }
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
