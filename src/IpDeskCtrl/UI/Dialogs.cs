using System;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>Richiesta di collegamento in arrivo: l'utente di questo PC accetta o rifiuta (60 s, poi rifiuto automatico).</summary>
    public sealed class IncomingWindow : Window
    {
        public event Action<IncomingDecision>? Decided;
        private bool _done;

        public IncomingWindow(IncomingInfo info, bool allowInputDefault)
        {
            Title = "IP-DESK-CTRL · richiesta di collegamento";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            string who = (info.Alias.Length > 0 ? info.Alias + " · " : "") + Identity.Format(info.Number);
            var input = new CheckBox { Content = "Consenti mouse e tastiera", IsChecked = allowInputDefault };
            var always = new CheckBox { Content = "Non chiedere più per questo numero (accesso senza conferma)" };
            var countdown = Ui.Sub("", 12);
            var accept = Ui.Btn("Accetta", true);
            var reject = Ui.Btn("Rifiuta");
            accept.MinWidth = 120; reject.MinWidth = 120;
            accept.Click += (_, _) => Decide(new IncomingDecision(true, input.IsChecked == true, always.IsChecked == true));
            reject.Click += (_, _) => Decide(new IncomingDecision(false, false, false));

            var details = $"{(info.PcName.Length > 0 ? info.PcName + " · " : "")}{info.Os} · versione {AppVersion.Parse(info.Version).version}\n" +
                          $"Impronta della chiave: {info.Fingerprint}" + (info.Known ? " (già vista in passato)" : " (prima volta)");
            Content = new Border
            {
                Padding = new Thickness(22),
                Child = Ui.Col(12,
                    Ui.H1("Richiesta di collegamento"),
                    new TextBlock { Text = $"{who} vuole vedere lo schermo di questo PC.", TextWrapping = TextWrapping.Wrap, FontSize = 15 },
                    Ui.Sub(details, 12),
                    input, always,
                    Ui.Row(10, reject, accept),
                    countdown)
            };

            int left = 60;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                left--;
                countdown.Text = $"Rifiuto automatico tra {left} s";
                if (left <= 0) { timer.Stop(); Decide(new IncomingDecision(false, false, false)); }
            };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }

        private void Decide(IncomingDecision d)
        {
            if (_done) return;
            _done = true;
            Decided?.Invoke(d);
            Close();
        }
    }

    /// <summary>Piccola finestra sempre in primo piano mentre questo PC è comandato da un altro.</summary>
    public sealed class ControlledWindow : Window
    {
        public ControlledWindow(HostSession hs, string who)
        {
            Title = "IP-DESK-CTRL · collegamento attivo";
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            Topmost = true;
            ShowActivated = false;
            SystemDecorations = SystemDecorations.BorderOnly;
            var input = new CheckBox { Content = "Mouse e tastiera", IsChecked = hs.InputAllowed };
            input.IsCheckedChanged += (_, _) => hs.SetInputAllowed(input.IsChecked == true);
            var stop = Ui.Btn("Disconnetti", true);
            stop.Click += (_, _) => hs.Close("chiuso dall'utente dell'altro PC");
            var time = Ui.Sub("", 12);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => time.Text = "da " + HostSession.Duration(DateTime.UtcNow - hs.Started) + " · " + hs.Path;
            timer.Start();
            Closed += (_, _) => timer.Stop();
            Content = new Border
            {
                Padding = new Thickness(14, 10),
                Background = Themes.B(Themes.Current.Panel),
                BorderBrush = Themes.B(Themes.Current.Tx),
                BorderThickness = new Thickness(2),
                Child = Ui.Col(6,
                    new TextBlock { Text = "Questo PC è comandato da " + who, FontWeight = FontWeight.Bold },
                    time,
                    Ui.Row(10, input, stop))
            };
            Opened += (_, _) =>
            {
                // in alto a destra dello schermo principale
                var sc = Screens.Primary;
                if (sc != null)
                {
                    var wa = sc.WorkingArea;
                    double scale = sc.Scaling;
                    Position = new PixelPoint(wa.Right - (int)(Bounds.Width * scale) - 20, wa.Y + 20);
                }
            };
        }
    }

    public sealed class AboutWindow : Window
    {
        public AboutWindow(Identity? me)
        {
            Title = "Informazioni e licenze";
            Width = 640; Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            string notices = Read("notices/THIRD-PARTY-NOTICES.txt");
            var text = new TextBox
            {
                Text = notices, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontSize = 12
            };
            var log = Ui.Btn("Apri il registro delle sessioni");
            log.Click += (_, _) => MainWindow.OpenPath(SessionLog.FilePath);
            Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 6, [DockPanel.DockProperty] = Dock.Top, Margin = new Thickness(0, 0, 0, 12),
                        Children =
                        {
                            Ui.H1("IP-DESK-CTRL " + AppVersion.Text),
                            Ui.Sub("Desktop remoto cifrato (ECDH P-256, AES-256-GCM) tra Windows, macOS, Linux e Raspberry Pi, con ritrovo sui broker MQTT."),
                            Ui.Sub("Copyright (C) 2026 Costantino IC8TEM · Sezione ARI «Valle del Liri» · ccerrotta@gmail.com · ic8tem@gmail.com · www.qrz.com/db/ic8tem"),
                            Ui.Sub("Software libero: GNU GPL versione 3 o successiva, senza alcuna garanzia."),
                            Ui.Sub(me == null ? "" : $"Numero di questo PC: {Identity.Format(me.Number)} · impronta della chiave {Crypto.Fingerprint(me.PublicKey)} · {AppVersion.Os}", 12),
                            log
                        }
                    },
                    text
                }
            };
        }

        private static string Read(string name)
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (s == null) return "";
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
            catch { return ""; }
        }
    }
}
