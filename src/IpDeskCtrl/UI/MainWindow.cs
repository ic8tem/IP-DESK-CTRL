using System;
using Avalonia.Controls;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>Finestra principale sul PC: contiene la <see cref="MainView"/>.</summary>
    public sealed class MainWindow : Window
    {
        private readonly MainView _view;

        public MainWindow(AppSettings s)
        {
            Title = "IP-DESK-CTRL";
            Width = 780; Height = 760; MinWidth = 560; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://IpDeskCtrl/Assets/icon.png"))); } catch { }
            Shell.Current = new DesktopShell(this);
            Platform.Clipboard = new AvaloniaClipboard(() => this);
            _view = new MainView(s);
            _view.TitleChanged += t => Title = t;
            Content = _view;
            Closing += (_, _) => _view.Shutdown();
        }

        public void Shutdown() => _view.Shutdown();
    }
}
