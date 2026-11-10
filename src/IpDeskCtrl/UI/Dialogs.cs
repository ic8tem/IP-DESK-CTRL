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
    /// <summary>Dialoghi, mostrati dalla shell come finestre (PC) o riquadri sopra l'app (Android).</summary>
    public static class Dialogs
    {
        /// <summary>Richiesta di collegamento in arrivo: accetta o rifiuta (60 s, poi rifiuto automatico).</summary>
        public static IDialogHandle Incoming(IncomingInfo info, bool allowInputDefault, Action<IncomingDecision> decided)
        {
            string who = (info.Alias.Length > 0 ? info.Alias + " · " : "") + Identity.Format(info.Number);
            var input = new CheckBox { Content = "Consenti mouse, tastiera, appunti e file", IsChecked = allowInputDefault };
            var always = new CheckBox { Content = "Non chiedere più per questo numero (accesso senza conferma)" };
            var countdown = Ui.Sub("", 12);
            var accept = Ui.Btn("Accetta", true);
            var reject = Ui.Btn("Rifiuta");
            accept.MinWidth = 120; reject.MinWidth = 120;
            string details = $"{(info.PcName.Length > 0 ? info.PcName + " · " : "")}{info.Os} · versione {AppVersion.Parse(info.Version).version}\n" +
                             $"Impronta della chiave: {info.Fingerprint}" + (info.Known ? " (già vista in passato)" : " (prima volta)");
            var content = Ui.Col(12,
                Ui.H1("Richiesta di collegamento"),
                new TextBlock { Text = $"{who} vuole vedere lo schermo di questo {(Platform.IsMobile ? "dispositivo" : "PC")}.", TextWrapping = TextWrapping.Wrap, FontSize = 15 },
                Ui.Sub(details, 12),
                input, always,
                Ui.Row(10, reject, accept),
                countdown);

            var h = Shell.Current.ShowDialog("IP-DESK-CTRL · richiesta di collegamento", content, 500, true);
            bool done = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void Decide(IncomingDecision d)
            {
                if (done) return;
                done = true;
                timer.Stop();
                decided(d);
                h.Close();
            }
            accept.Click += (_, _) => Decide(new IncomingDecision(true, input.IsChecked == true, always.IsChecked == true));
            reject.Click += (_, _) => Decide(new IncomingDecision(false, false, false));
            int left = 60;
            timer.Tick += (_, _) =>
            {
                left--;
                countdown.Text = $"Rifiuto automatico tra {left} s";
                if (left <= 0) Decide(new IncomingDecision(false, false, false));
            };
            timer.Start();
            h.Closed += () => timer.Stop();
            return h;
        }

        /// <summary>Riquadro mentre questo PC è comandato da un altro.</summary>
        public static Control Controlled(HostSession hs, string who)
        {
            var input = new CheckBox { Content = "Mouse e tastiera", IsChecked = hs.InputAllowed };
            input.IsCheckedChanged += (_, _) => hs.SetInputAllowed(input.IsChecked == true);
            var stop = Ui.Btn("Disconnetti", true);
            stop.Click += (_, _) => hs.Close("chiuso dall'utente dell'altro PC");
            var time = Ui.Sub("", 12);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                if (hs.IsClosed) { timer.Stop(); return; }
                time.Text = "da " + HostSession.Duration(DateTime.UtcNow - hs.Started) + " · " + hs.Route;
            };
            timer.Start();
            return new Border
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
        }

        public static void About(Identity? me)
        {
            string notices = Read("notices/THIRD-PARTY-NOTICES.txt");
            var text = new TextBox { Text = notices, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontSize = 12, Height = 260 };
            var log = Ui.Btn("Apri il registro delle sessioni");
            log.Click += (_, _) => MainView.OpenPath(SessionLog.FilePath);
            log.IsVisible = !Platform.IsMobile;
            var close = Ui.Btn("Chiudi", true);
            var content = Ui.Col(8,
                Ui.H1("IP-DESK-CTRL " + AppVersion.Text),
                Ui.Sub("Desktop remoto cifrato (ECDH P-256, AES-256-GCM) tra Windows, macOS, Linux, Raspberry Pi e Android, con ritrovo sui broker MQTT."),
                Ui.Sub("Copyright (C) 2026 Costantino IC8TEM · Sezione ARI «Valle del Liri» · ccerrotta@gmail.com · ic8tem@gmail.com · www.qrz.com/db/ic8tem"),
                Ui.Sub("Software libero: GNU GPL versione 3 o successiva, senza alcuna garanzia."),
                Ui.Sub(me == null ? "" : $"Numero di questo dispositivo: {Identity.Format(me.Number)} · impronta della chiave {Crypto.Fingerprint(me.PublicKey)} · {AppVersion.Os}", 12),
                text,
                Ui.Row(10, log, close));
            var h = Shell.Current.ShowDialog("Informazioni e licenze", content, 640, false);
            close.Click += (_, _) => h.Close();
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
