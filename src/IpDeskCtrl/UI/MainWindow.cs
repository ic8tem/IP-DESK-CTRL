using System;
using Avalonia.Controls;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Finestra principale sul PC: contiene la <see cref="MainView"/>.
    /// La X in alto a destra la nasconde soltanto: il programma resta attivo (PC raggiungibile, collegamenti aperti)
    /// e si riapre dall'icona vicino all'orologio o riavviandolo. Per uscire davvero: «Esci da IP-DESK-CTRL».
    /// </summary>
    public sealed class MainWindow : Window
    {
        private readonly MainView _view;
        private readonly AppSettings _s;
        private bool _quitting;

        public event Action<string>? TitleUpdated;

        public MainWindow(AppSettings s)
        {
            _s = s;
            Title = "IP-DESK-CTRL";
            Width = 780; Height = 760; MinWidth = 560; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://IpDeskCtrl/Assets/icon.png"))); } catch { }
            Shell.Current = new DesktopShell(this);
            Platform.Clipboard = OperatingSystem.IsWindows()
                ? new Screen.WindowsClipboard(new AvaloniaClipboard(() => this))
                : new AvaloniaClipboard(() => this);
            _view = new MainView(s);
            _view.TitleChanged += t => { Title = t; TitleUpdated?.Invoke(t); };
            Content = _view;
            Closing += (_, e) =>
            {
                // solo la X (o Alt+F4) nasconde; spegnimento del PC, disconnessione e «Esci» chiudono davvero
                if (!_quitting && e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }
                _view.Shutdown();
            };
        }

        /// <summary>Avvia la rete anche se la finestra non viene mostrata (avvio nascosto all'accesso).</summary>
        public void StartHidden() => _view.EnsureStarted();

        public void ShowMain()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void HideToTray()
        {
            Hide();
            SessionLog.Write("FINESTRA nascosta: il programma resta attivo");
            if (_s.HideNoticeShown) return;
            _s.HideNoticeShown = true;
            _s.Save();
            var ok = Ui.Btn("Ho capito", true);
            var content = Ui.Col(10,
                Ui.H1("IP-DESK-CTRL resta attivo"),
                Ui.Sub("La finestra è nascosta, ma il programma continua a funzionare: questo PC resta raggiungibile col suo numero e i collegamenti aperti non si chiudono."),
                Ui.Sub(OperatingSystem.IsMacOS()
                    ? "Per riaprirlo usa l'icona di IP-DESK-CTRL nella barra dei menu in alto, oppure riavvialo. Per chiuderlo davvero: icona › «Esci da IP-DESK-CTRL»."
                    : "Per riaprirlo fai clic sull'icona di IP-DESK-CTRL vicino all'orologio, oppure riavvialo dal menu o dal desktop. Per chiuderlo davvero: clic destro sull'icona › «Esci da IP-DESK-CTRL», oppure Impostazioni › «Esci dal programma»."),
                ok);
            var h = Shell.Current.ShowDialog("IP-DESK-CTRL", content, 460, true);
            ok.Click += (_, _) => h.Close();
        }

        /// <summary>Uscita vera: chiude collegamenti e rete.</summary>
        public void Quit()
        {
            _quitting = true;
            _view.Shutdown();
            try { Close(); } catch { }
        }

        public void Shutdown() => _view.Shutdown();
    }
}
