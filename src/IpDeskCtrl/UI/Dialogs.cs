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
            var input = new CheckBox { Content = L.T("Consenti mouse, tastiera, appunti e file"), IsChecked = allowInputDefault && !info.Observer, IsVisible = !info.Observer };
            var always = new CheckBox { Content = L.T("Non chiedere più per questo numero (accesso senza conferma)") };
            var countdown = Ui.Sub("", 12);
            var accept = Ui.Btn(L.T("Accetta"), true);
            var reject = Ui.Btn(L.T("Rifiuta"));
            accept.MinWidth = 120; reject.MinWidth = 120;
            string details = L.F("{0}{1} · versione {2}\n", (info.PcName.Length > 0 ? info.PcName + " · " : ""), info.Os, AppVersion.Parse(info.Version).version) +
                             L.F("Impronta della chiave: {0}", info.Fingerprint) + (info.Known ? L.T(" (già vista in passato)") : L.T(" (prima volta)"));
            var content = Ui.Col(12,
                Ui.H1(L.T("Richiesta di collegamento")),
                new TextBlock
                {
                    Text = info.Observer
                        ? L.F("{0} vuole guardare lo schermo di questo PC (solo visione) mentre lo comanda {1}.", who, Identity.Format(info.Controller))
                        : L.F("{0} vuole vedere lo schermo di questo {1}.", who, (Platform.IsMobile ? L.T("dispositivo") : "PC")),
                    TextWrapping = TextWrapping.Wrap, FontSize = 15
                },
                Ui.Sub(details, 12),
                input, always,
                Ui.Row(10, reject, accept),
                countdown);

            var h = Shell.Current.ShowDialog(L.T("IP-DESK-CTRL · richiesta di collegamento"), content, 500, true);
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
                countdown.Text = L.F("Rifiuto automatico tra {0} s", left);
                if (left <= 0) Decide(new IncomingDecision(false, false, false));
            };
            timer.Start();
            h.Closed += () => timer.Stop();
            return h;
        }

        /// <summary>Riquadro mentre questo PC è comandato da un altro.</summary>
        public static Control Controlled(HostSession hs, string who)
        {
            var input = new CheckBox { Content = L.T("Mouse e tastiera"), IsChecked = hs.InputAllowed };
            input.IsCheckedChanged += (_, _) => hs.SetInputAllowed(input.IsChecked == true);
            var stop = Ui.Btn(L.T("Disconnetti"), true);
            stop.Click += (_, _) => { foreach (var o in hs.Node.Observers) o.Close(L.T("chiuso dall'utente dell'altro PC")); hs.Close(L.T("chiuso dall'utente dell'altro PC")); };
            var time = Ui.Sub("", 12);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                if (hs.IsClosed) { timer.Stop(); return; }
                int obs = hs.Node.Observers.Count;
                time.Text = L.T("da ") + HostSession.Duration(DateTime.UtcNow - hs.Started) + " · " + hs.Route + (obs > 0 ? L.F(" · {0} guardano", obs) : "");
            };
            timer.Start();
            return new Border
            {
                Padding = new Thickness(14, 10),
                Background = Themes.B(Themes.Current.Panel),
                BorderBrush = Themes.B(Themes.Current.Tx),
                BorderThickness = new Thickness(2),
                Child = Ui.Col(6,
                    new TextBlock { Text = L.T("Questo PC è comandato da ") + who, FontWeight = FontWeight.Bold },
                    time,
                    Ui.Row(10, input, stop))
            };
        }

        public static void About(Identity? me)
        {
            string notices = Read("notices/THIRD-PARTY-NOTICES.txt");
            var text = new TextBox { Text = notices, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontSize = 12, Height = 260 };
            var log = Ui.Btn(L.T("Apri il registro delle sessioni"));
            log.Click += (_, _) => MainView.OpenPath(SessionLog.FilePath);
            log.IsVisible = !Platform.IsMobile;
            var share = Ui.Btn(L.T("Condividi il registro"));
            share.IsVisible = Platform.ShareText != null;
            share.Click += (_, _) =>
            {
                string text = "";
                try
                {
                    text = System.IO.File.Exists(SessionLog.FilePath) ? System.IO.File.ReadAllText(SessionLog.FilePath) : "";
                    string err = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SessionLog.FilePath) ?? "", "errori.log");
                    if (System.IO.File.Exists(err)) text += "\n\n===== errori.log =====\n" + System.IO.File.ReadAllText(err);
                }
                catch (Exception ex) { text = L.T("Registro non leggibile: ") + ex.Message; }
                if (text.Length > 200_000) text = "…" + text[^200_000..];
                Platform.ShareText?.Invoke(L.T("IP-DESK-CTRL ") + AppVersion.Text + L.T(" · registro"), text.Length > 0 ? text : L.T("(registro vuoto)"));
            };
            var support = Ui.Btn(L.T("♥ Sostieni il progetto"));
            support.Click += (_, _) => MainView.OpenPath(AppVersion.SupportUrl);
            var close = Ui.Btn(L.T("Chiudi"), true);
            var content = Ui.Col(8,
                Ui.H1(L.T("IP-DESK-CTRL ") + AppVersion.Text),
                Ui.Sub(L.T("Desktop remoto cifrato (ECDH P-256, AES-256-GCM) tra Windows, macOS, Linux, Raspberry Pi e Android, con ritrovo sui broker MQTT.")),
                Ui.Sub(L.T("Copyright (C) 2026 Costantino IC8TEM · Sezione ARI «Valle del Liri» · ccerrotta@gmail.com · ic8tem@gmail.com · www.qrz.com/db/ic8tem")),
                Ui.Sub(L.T("Software libero: GNU GPL versione 3 o successiva, senza alcuna garanzia.")),
                Ui.Sub(L.T("Gratuito e senza pubblicità: se ti è utile puoi sostenerlo su ko-fi.com/ic8tem.")),
                Ui.Sub(me == null ? "" : L.F("Numero di questo dispositivo: {0} · impronta della chiave {1} · {2}", Identity.Format(me.Number), Crypto.Fingerprint(me.PublicKey), AppVersion.Os), 12),
                text,
                Ui.Row(10, log, share, support, close));
            var h = Shell.Current.ShowDialog(L.T("Informazioni e licenze"), content, 640, false);
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
