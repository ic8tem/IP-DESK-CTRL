using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpDeskCtrl.Core;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Schermata principale: il numero di questo PC, il campo per collegarsi a un altro numero, lo storico dei
    /// collegamenti con gli alias e le impostazioni. Ogni collegamento in uscita apre una <see cref="ViewerView"/>
    /// (in una finestra sul PC, sopra la schermata su Android).
    /// </summary>
    public sealed class MainView : UserControl
    {
        private readonly AppSettings _s;
        private Identity? _me;
        private Peer? _peer;
        private ServiceClient? _svc;       // Windows con il servizio: i collegamenti in arrivo li riceve lui
        private readonly Dictionary<string, ViewerView> _viewers = new();
        private Dictionary<string, TimeSpan?> _presence = new();
        private readonly CancellationTokenSource _life = new();

        private readonly TextBlock _number = new() { Text = "··· ··· ····", FontSize = 40, FontWeight = FontWeight.Bold, LetterSpacing = 2 };
        private readonly TextBlock _netStatus = Ui.Sub("Avvio…");
        private readonly TextBlock _netDetail = Ui.Sub("", 12);
        private readonly Ellipse _led = new() { Width = 12, Height = 12, Fill = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _target = new() { Watermark = "123 456 7890", FontSize = 22, MaxLength = 16 };
        private readonly TextBlock _connectInfo = Ui.Sub("");
        private readonly StackPanel _history = new() { Spacing = 8 };
        private readonly Border _incomingBanner = new() { IsVisible = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        private readonly TextBlock _incomingText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold };
        private readonly CheckBox _incomingInput = new() { Content = "Mouse e tastiera" };
        private IDialogHandle? _controlled;
        private readonly Border _update = new() { IsVisible = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        private bool _closingForReal;

        /// <summary>Titolo da mostrare (la finestra del PC lo usa).</summary>
        public event Action<string>? TitleChanged;

        public MainView(AppSettings s)
        {
            _s = s;
            Content = Build();
            AttachedToVisualTree += (_, _) => EnsureStarted();
        }

        /// <summary>Avvio della rete (una volta sola), anche con la finestra nascosta.</summary>
        public void EnsureStarted()
        {
            if (_me == null && _peer == null && !_starting) { _starting = true; _ = StartAsync(); }
        }

        /// <summary>Uscita vera dal programma (sul PC: la X nasconde soltanto). null su Android.</summary>
        public static Action? Quit { get; set; }

        private bool _starting;

        // =================================================================== costruzione

        private Control Build()
        {
            var root = new DockPanel();

            // intestazione
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            title.Children.Add(new TextBlock { Text = "IP-DESK-CTRL", FontSize = 20, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
            if (!Platform.IsMobile) title.Children.Add(new TextBlock { Text = "desktop remoto cifrato · " + AppVersion.Text, VerticalAlignment = VerticalAlignment.Center, Classes = { "sub" } });
            var head = new DockPanel { Margin = new Thickness(20, 14, 20, 6) };
            var ledBox = Ui.Row(8, _led, _netStatus);
            DockPanel.SetDock(ledBox, Dock.Right);
            head.Children.Add(ledBox);
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var col = new StackPanel { Spacing = 14, Margin = new Thickness(20, 6, 20, 20) };
            col.Children.Add(_update);

            // ---- questo PC
            var copy = Ui.Btn("Copia");
            copy.Click += async (_, _) => { var c = TopLevel.GetTopLevel(this)?.Clipboard; if (_me != null && c != null) await c.SetTextAsync(Identity.Format(_me.Number)); };
            _number.Classes.Add("mono");
            var qr = Ui.Btn("QR");
            ToolTip.SetTip(qr, "Mostra il numero come codice QR, da inquadrare con IP-DESK-CTRL sul telefono");
            qr.Click += (_, _) => ShowQr();
            var numButtons = Ui.Row(8, qr, copy);
            var numRow = new DockPanel();
            DockPanel.SetDock(numButtons, Dock.Right);
            numButtons.VerticalAlignment = VerticalAlignment.Center;
            numRow.Children.Add(numButtons);
            numRow.Children.Add(_number);
            _incomingInput.IsCheckedChanged += (_, _) =>
            {
                _peer?.CurrentHost?.SetInputAllowed(_incomingInput.IsChecked == true);
                _svc?.SetInput(_incomingInput.IsChecked == true);
            };
            var stop = Ui.Btn("Disconnetti");
            stop.Click += (_, _) =>
            {
                _peer?.CurrentHost?.Close("chiuso dall'utente dell'altro PC");
                _svc?.Kick();
            };
            _incomingBanner.Background = Themes.B(Themes.Current.TxDim);
            _incomingBanner.Child = Ui.Col(8, _incomingText, Ui.Row(10, _incomingInput, stop));
            col.Children.Add(Ui.Panel(Ui.Col(6,
                Ui.H2("IL NUMERO DI QUESTO PC"),
                numRow,
                Ui.Sub("Dallo a chi deve collegarsi a questo PC: non cambia mai. Ogni volta ti verrà chiesto se accettare."),
                _netDetail,
                _incomingBanner)));

            // ---- collegati
            var go = Ui.Btn("Collegati", true);
            go.MinWidth = 140;
            go.Click += (_, _) => ConnectTo(_target.Text ?? "");
            _target.KeyDown += (_, e) => { if (e.Key == Key.Enter) ConnectTo(_target.Text ?? ""); };
            _target.TextChanged += (_, _) => FormatTarget();
            var tRow = new DockPanel();
            DockPanel.SetDock(go, Dock.Right);
            go.Margin = new Thickness(10, 0, 0, 0);
            tRow.Children.Add(go);
            if (Platform.ScanQr != null)
            {
                var scan = Ui.Btn("Inquadra QR");
                DockPanel.SetDock(scan, Dock.Right);
                scan.Margin = new Thickness(10, 0, 0, 0);
                scan.Click += async (_, _) => await ScanAsync();
                tRow.Children.Add(scan);
            }
            tRow.Children.Add(_target);
            col.Children.Add(Ui.Panel(Ui.Col(8, Ui.H2("COLLEGATI A UN ALTRO PC"), tRow, _connectInfo)));

            // ---- storico
            var refresh = Ui.Btn("Aggiorna stato");
            refresh.Click += (_, _) => _ = RefreshPresenceAsync();
            var hHead = new DockPanel();
            DockPanel.SetDock(refresh, Dock.Right);
            hHead.Children.Add(refresh);
            hHead.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.H2("COLLEGAMENTI PRECEDENTI"), Ui.Sub(Platform.IsMobile ? "Pallino verde: il programma è aperto su quel PC." : "Pallino verde: il programma è aperto su quel PC. Doppio clic per collegarti.", 12) } });
            col.Children.Add(Ui.Panel(Ui.Col(10, hHead, _history)));

            // ---- impostazioni
            col.Children.Add(Ui.Panel(BuildSettings()));

            root.Children.Add(new ScrollViewer { Content = col });
            RebuildHistory();
            return root;
        }

        private Control BuildSettings()
        {
            CheckBox Chk(string text, bool value, Action<bool> set)
            {
                var c = new CheckBox { Content = text, IsChecked = value };
                c.IsCheckedChanged += (_, _) => { set(c.IsChecked == true); _s.Save(); _svc?.SendSettings(_s.AcceptIncoming, _s.AllowInput, _s.ShareAudio); };
                return c;
            }
            var theme = new ComboBox { ItemsSource = Themes.All.Select(t => t.Name).ToList(), SelectedIndex = Math.Max(0, Themes.All.FindIndex(t => t.Key == _s.Theme)), Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            theme.SelectionChanged += (_, _) =>
            {
                if (theme.SelectedIndex < 0) return;
                _s.Theme = Themes.All[theme.SelectedIndex].Key;
                _s.Save();
                Themes.Apply(_s.Theme);
                _incomingBanner.Background = Themes.B(Themes.Current.TxDim);
            };
            var folder = Ui.Btn("Apri cartella dati");
            folder.Click += (_, _) => OpenPath(AppSettings.Folder);
            folder.IsVisible = !Platform.IsMobile;
            var received = Ui.Btn("File ricevuti");
            received.Click += (_, _) => OpenPath(Platform.Received);
            received.IsVisible = !Platform.IsMobile;
            var password = Ui.Btn("Password per l'accesso senza conferma…");
            password.Click += (_, _) => PasswordDialog();
            ToolTip.SetTip(password, "Chi è autorizzato a entrare senza conferma dovrà anche conoscere questa password.");
            var about = Ui.Btn("Informazioni e licenze");
            about.Click += (_, _) => Dialogs.About(_me);
            var touch = Ui.Btn("Tocco e gesti…");
            touch.Click += (_, _) => TouchDialog.Show(_s);
            ToolTip.SetTip(touch, "Telefono, tablet e schermi touch: modo del dito, gesti, velocità del puntatore, barra dei tasti.");
            var quit = Ui.Btn("Esci dal programma");
            quit.Click += (_, _) => Quit?.Invoke();
            quit.IsVisible = Quit != null;
            ToolTip.SetTip(quit, "Chiude davvero IP-DESK-CTRL: questo PC non sarà più raggiungibile finché non lo riavvii. La X della finestra invece lo nasconde soltanto.");
            var support = Ui.Btn("♥ Sostieni il progetto");
            support.Click += (_, _) => OpenPath(AppVersion.SupportUrl);
            ToolTip.SetTip(support, "IP-DESK-CTRL è gratuito e senza pubblicità. Se ti è utile, puoi offrirmi un caffè su Ko-fi.");
            return Ui.Col(8,
                Ui.H2("IMPOSTAZIONI"),
                Chk("Accetta richieste di collegamento a questo PC", _s.AcceptIncoming, v => _s.AcceptIncoming = v),
                Chk("Proponi di consentire mouse e tastiera a chi si collega", _s.AllowInput, v => _s.AllowInput = v),
                Chk("Manda l'audio di questo PC a chi si collega", _s.ShareAudio, v => _s.ShareAudio = v),
                Chk("Ascolta l'audio dei PC a cui ti colleghi", _s.PlayAudio, v => _s.PlayAudio = v),
                Chk("Accetta il microfono di chi si collega (la sua voce arriva qui)", _s.AcceptMic, v => _s.AcceptMic = v),
                MicOutputRow(),
                Chk("Chi si collega può adattare la risoluzione di questo PC alla sua finestra", _s.AllowResize, v => _s.AllowResize = v),
                Chk("Chi si collega può montare qui una sua cartella come disco", _s.AllowDrive, v => _s.AllowDrive = v),
                Ui.Row(10, new TextBlock { Text = "Tema", VerticalAlignment = VerticalAlignment.Center }, theme),
                Ui.Sub("File ricevuti dagli altri PC: " + Platform.Received, 12),
                Wrap(password, touch, folder, received, about, support, quit));
        }

        /// <summary>Uscita dove suonare il microfono di chi si collega (Windows: elenco delle uscite; altrove automatica).</summary>
        private Control MicOutputRow()
        {
            var names = Audio.SystemAudio.OutputNames();
            if (names.Length == 0)
                return Ui.Sub(OperatingSystem.IsLinux()
                    ? "La voce di chi si collega arriva nel microfono virtuale «IP-DESK-CTRL-microfono», creato da solo durante il collegamento."
                    : "Per usare la voce di chi si collega come microfono nei programmi installa BlackHole 2ch; senza, si sente dalle casse.", 12);
            var items = new List<string> { "Automatica (cavo virtuale VB-CABLE se c'è, altrimenti le casse)" };
            items.AddRange(names);
            var box = new ComboBox { ItemsSource = items, MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
            int i = _s.MicOutput.Length == 0 ? 0 : Array.FindIndex(names, n => n.Contains(_s.MicOutput, StringComparison.OrdinalIgnoreCase)) + 1;
            box.SelectedIndex = Math.Max(0, i);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                _s.MicOutput = box.SelectedIndex == 0 ? "" : names[box.SelectedIndex - 1];
                _s.Save();
            };
            ToolTip.SetTip(box, "Con un cavo audio virtuale (VB-CABLE, gratuito) la voce di chi si collega diventa un microfono per i programmi di questo PC, anche per IP-TRX-CTRL.");
            return Ui.Row(10, new TextBlock { Text = "Voce di chi si collega su", VerticalAlignment = VerticalAlignment.Center }, box);
        }

        private static WrapPanel Wrap(params Control[] items)
        {
            var w = new WrapPanel();
            foreach (var c in items) { c.Margin = new Thickness(0, 0, 10, 10); w.Children.Add(c); }
            return w;
        }

        // =================================================================== avvio

        private async Task StartAsync()
        {
            try
            {
                // Windows con il servizio: numero del servizio (la prima volta gli si passa la chiave dell'utente)
                Identity? svcId = OperatingSystem.IsWindows() && !Platform.IsMobile ? await Task.Run(ServiceClient.ServiceIdentity) : null;
                _me = svcId ?? await Task.Run(Identity.LoadOrCreate);
                _number.Text = Identity.Format(_me.Number);
                TitleChanged?.Invoke("IP-DESK-CTRL · " + Identity.Format(_me.Number));
                if (svcId != null) await AttachServiceAsync();
                _peer = new Peer(_me, _s) { ViewerOnly = svcId != null };
                _peer.StatusChanged += () => Dispatcher.UIThread.Post(UpdateStatus);
                _peer.AskAccept = AskAcceptAsync;
                _peer.HostStarted += hs => Dispatcher.UIThread.Post(() => OnHostStarted(hs));
                _peer.HostEnded += (hs, why) => Dispatcher.UIThread.Post(() => OnHostEnded(hs, why));
                _peer.Start();
                _ = PresenceLoopAsync();
                _ = UpdateCheck.LoopAsync(r => Dispatcher.UIThread.Post(() => ShowUpdate(r)), _life.Token);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                _netStatus.Text = "Errore: " + ex.Message;
                _led.Fill = Brushes.Red;
            }
        }

        /// <summary>Storico condiviso con il servizio e collegamento permanente per consenso e sessioni in arrivo.</summary>
        private async Task AttachServiceAsync()
        {
            var mine = _s.History.ToList();
            AppSettings.SharedHistoryPath = System.IO.Path.Combine(ServiceClient.ServiceFolder, "storico.json");
            _s.ReloadShared();
            _s.MergeHistory(mine);
            _svc = await Task.Run(ServiceClient.Attach);
            if (_svc == null) return;
            _svc.AskAccept = AskAcceptAsync;
            _svc.HostStarted += (n, input) => Dispatcher.UIThread.Post(() => OnServiceHostStarted(n, input));
            _svc.HostEnded += (n, why) => Dispatcher.UIThread.Post(() => { _incomingBanner.IsVisible = false; RebuildHistory(); });
            _svc.Lost += () => Dispatcher.UIThread.Post(() => _incomingBanner.IsVisible = false);
            _svc.SendSettings(_s.AcceptIncoming, _s.AllowInput, _s.ShareAudio);
            if (_s.PasswordHash.Length > 0) _svc.Send($"PWD {_s.PasswordSalt} {_s.PasswordHash} {_s.PasswordIterations}");
            SessionLog.Write("SERVIZIO collegato: i collegamenti a questo PC li riceve il servizio di Windows");
        }

        private void OnServiceHostStarted(string number, bool input)
        {
            var h = _s.Find(number);
            string who = (h?.Alias.Length > 0 ? h.Alias + " · " : "") + Identity.Format(number);
            _incomingText.Text = $"{who} è collegato a questo PC e lo vede" + (input ? " e lo comanda." : ".");
            _incomingInput.IsChecked = input;
            _incomingBanner.IsVisible = true;
            RebuildHistory();
        }

        private void UpdateStatus()
        {
            if (_peer == null) return;
            _netStatus.Text = _peer.Ready ? (_peer.Brokers > 0 ? "In linea" : "Broker MQTT non raggiungibili") : _peer.Status;
            _led.Fill = !_peer.Ready ? Brushes.Gray : _peer.Brokers > 0 ? Themes.B(Themes.Current.Ok) : Brushes.OrangeRed;
            _netDetail.Text = _peer.Status + (_peer.Detail.Length > 0 ? " · " + _peer.Detail : "") + $" · broker collegati: {_peer.Brokers}";
        }

        private async Task PresenceLoopAsync()
        {
            await Task.Delay(4000);
            while (!_life.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(UpdateStatus);
                await RefreshPresenceAsync();
                try { await Task.Delay(TimeSpan.FromSeconds(60), _life.Token); } catch { break; }
            }
        }

        private async Task RefreshPresenceAsync()
        {
            var nums = _s.History.Select(h => h.Number).Take(40).ToList();
            if (nums.Count == 0) return;
            try
            {
                var p = await ViewerRendezvous.PresenceAsync(nums, _s.MqttBrokers, _life.Token);
                Dispatcher.UIThread.Post(() => { _presence = p; RebuildHistory(); });
            }
            catch { }
        }

        // =================================================================== numero da chiamare

        private bool _formatting;

        private void FormatTarget()
        {
            if (_formatting) return;
            string clean = Identity.Clean(_target.Text);
            if (clean.Length > 10) clean = clean[..10];
            string f = clean.Length <= 3 ? clean : clean.Length <= 6 ? $"{clean[..3]} {clean[3..]}" : $"{clean[..3]} {clean[3..6]} {clean[6..]}";
            if (f == _target.Text) return;
            _formatting = true;
            _target.Text = f;
            _target.CaretIndex = f.Length;
            _formatting = false;
        }

        public void ConnectTo(string number)
        {
            number = Identity.Clean(number);
            if (_peer == null) { _connectInfo.Text = "Attendi: il programma si sta avviando…"; return; }
            if (!Identity.IsValidNumber(number)) { _connectInfo.Text = "Il numero deve avere 10 cifre."; return; }
            if (number == _me?.Number) { _connectInfo.Text = "Questo è il numero di questo PC."; return; }
            _connectInfo.Text = "";
            if (_viewers.TryGetValue(number, out var existing))
            {
                Shell.Current.ActivateViewer(existing);
                return;
            }
            var v = new ViewerView(_peer, number, _s.Find(number)?.Alias ?? "");
            _viewers[number] = v;
            v.Ended += () => { _viewers.Remove(number); RebuildHistory(); };
            v.HistoryChanged += RebuildHistory;
            Shell.Current.OpenViewer(v);
            v.Start();
        }

        // =================================================================== storico

        private void RebuildHistory()
        {
            _history.Children.Clear();
            if (_s.History.Count == 0)
            {
                _history.Children.Add(Ui.Sub("Ancora nessun collegamento. I PC a cui ti colleghi e quelli che si collegano a te compariranno qui."));
                return;
            }
            foreach (var h in _s.History.OrderByDescending(x => x.LastUtc).ToList())
                _history.Children.Add(HistoryRow(h));
        }

        private Control HistoryRow(HistoryEntry h)
        {
            _presence.TryGetValue(h.Number, out var age);
            bool online = age != null && age.Value < TimeSpan.FromMinutes(8);
            var dot = new Ellipse
            {
                Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                Fill = online ? Themes.B(Themes.Current.Ok) : Themes.B(Themes.Current.Border)
            };
            ToolTip.SetTip(dot, age == null ? "Non in linea (o stato non ancora letto)" : ViewerRendezvous.Age(age.Value));

            string title = h.Alias.Length > 0 ? h.Alias : Identity.Format(h.Number);
            var name = new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 15 };
            string dir = h.Outgoing ? "tu → lui" : "lui → te";
            string when = h.LastUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            var info = Ui.Sub($"{(h.Alias.Length > 0 ? Identity.Format(h.Number) + " · " : "")}{when} · {dir} · {h.Count} {(h.Count == 1 ? "volta" : "volte")}"
                              + (h.LastSeconds.Length > 0 ? $" · ultima durata {h.LastSeconds}" : "")
                              + (h.Os.Length > 0 ? $" · {h.Os}" : "")
                              + (h.AllowUnattended ? " · entra senza conferma" : ""), 12);
            var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, info } };

            var connect = Ui.Btn("Collegati", true);
            connect.Click += (_, _) => ConnectTo(h.Number);
            var alias = Ui.Btn("Alias");
            ToolTip.SetTip(alias, "Dai un nome a questo numero");
            var more = Ui.Btn("···");
            var unattended = new MenuItem { Header = h.AllowUnattended ? "✓ Può entrare in questo PC senza conferma" : "Consenti di entrare in questo PC senza conferma" };
            unattended.Click += (_, _) => { h.AllowUnattended = !h.AllowUnattended; _s.Save(); RebuildHistory(); };
            var wake = new MenuItem { Header = "Accendi (Wake-on-LAN)", IsEnabled = h.Mac.Length == 12 };
            wake.Click += async (_, _) =>
            {
                if (_peer == null) return;
                _connectInfo.Text = "Accensione di " + title + "…";
                _connectInfo.Text = await _peer.WakeAsync(h, _life.Token);
            };
            var remove = new MenuItem { Header = "Togli dallo storico" };
            remove.Click += (_, _) => { _s.History.Remove(h); _s.Save(); RebuildHistory(); };
            var flyout = new MenuFlyout();
            flyout.Items.Add(wake);
            flyout.Items.Add(unattended);
            flyout.Items.Add(remove);
            more.Flyout = flyout;

            var buttons = Ui.Row(6, alias, more, connect);
            buttons.VerticalAlignment = VerticalAlignment.Center;

            Control row;
            if (Platform.IsMobile)
            {
                // telefono: i pulsanti sotto il testo
                var top = new DockPanel();
                DockPanel.SetDock(dot, Dock.Left);
                top.Children.Add(dot);
                top.Children.Add(texts);
                buttons.Margin = new Thickness(24, 8, 0, 0);
                row = new StackPanel { Children = { top, buttons } };
            }
            else
            {
                var dp = new DockPanel();
                DockPanel.SetDock(dot, Dock.Left);
                DockPanel.SetDock(buttons, Dock.Right);
                dp.Children.Add(dot);
                dp.Children.Add(buttons);
                dp.Children.Add(texts);
                row = dp;
            }
            var box = Ui.Inset(row);
            box.DoubleTapped += (_, _) => ConnectTo(h.Number);

            alias.Click += (_, _) =>
            {
                var edit = new TextBox { Text = h.Alias, Watermark = "Nome da ricordare (es. PC della stazione)", MinWidth = 220 };
                void Save()
                {
                    h.Alias = (edit.Text ?? "").Trim();
                    _s.Save();
                    RebuildHistory();
                    foreach (var v in _viewers.Values) v.RefreshTitle();
                }
                edit.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); else if (e.Key == Key.Escape) RebuildHistory(); };
                edit.LostFocus += (_, _) => Save();
                texts.Children.Clear();
                texts.Children.Add(edit);
                texts.Children.Add(Ui.Sub(Identity.Format(h.Number) + " · Invio per salvare, Esc per annullare", 12));
                edit.AttachedToVisualTree += (_, _) => { edit.Focus(); edit.SelectAll(); };
            };
            return box;
        }

        // =================================================================== QR del numero

        private void ShowQr()
        {
            if (_me == null) return;
            var close = Ui.Btn("Chiudi");
            var content = Ui.Col(12,
                Ui.H1("Numero di questo PC"),
                new Border { Width = 280, Height = 280, HorizontalAlignment = HorizontalAlignment.Center, Child = new QrView(QrCode.ForNumber(_me.Number)) },
                new TextBlock { Text = Identity.Format(_me.Number), FontSize = 28, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                Ui.Sub("Inquadralo con «Inquadra QR» di IP-DESK-CTRL sul telefono per collegarti a questo PC.", 12),
                close);
            var h = Shell.Current.ShowDialog("IP-DESK-CTRL · QR del numero", content, 360, false);
            close.Click += (_, _) => h.Close();
        }

        private async Task ScanAsync()
        {
            var f = Platform.ScanQr;
            if (f == null) return;
            string? text = await f();
            if (text == null)
            {
                _connectInfo.Text = "Nessuna app per leggere i QR (o lettura annullata): scrivi il numero a mano.";
                return;
            }
            string? n = QrCode.NumberFrom(text);
            if (n == null) { _connectInfo.Text = "Il QR letto non contiene un numero di IP-DESK-CTRL."; return; }
            _target.Text = n;
            ConnectTo(n);
        }

        // =================================================================== nuova versione

        private void ShowUpdate(ReleaseInfo r)
        {
            bool apk = Platform.IsMobile && r.ApkUrl.Length > 0;
            var open = Ui.Btn(apk ? "Scarica l'APK" : "Vai allo scaricamento", true);
            open.Click += (_, _) => OpenPath(apk ? r.ApkUrl : r.PageUrl);
            var later = Ui.Btn("Più tardi");
            later.Click += (_, _) => _update.IsVisible = false;
            var text = new TextBlock
            {
                Text = $"È disponibile IP-DESK-CTRL {r.Version} (questa è la {AppVersion.Text}). " +
                       "Aggiorna tutti e due i PC: versioni con protocollo diverso non si collegano.",
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold
            };
            _update.Background = Themes.B(Themes.Current.TxDim);
            _update.Child = Ui.Col(8, text, Ui.Row(10, later, open));
            _update.IsVisible = true;
        }

        // =================================================================== password dell'accesso senza conferma

        private void PasswordDialog()
        {
            var p1 = new TextBox { PasswordChar = '•', Watermark = "Nuova password" };
            var p2 = new TextBox { PasswordChar = '•', Watermark = "Ripeti la password" };
            var info = Ui.Sub(_s.PasswordHash.Length > 0
                ? "Una password è già impostata. Puoi cambiarla o toglierla."
                : "Nessuna password: chi è autorizzato entra senza conferma e senza password.", 12);
            var save = Ui.Btn("Salva", true);
            var remove = Ui.Btn("Togli la password");
            var close = Ui.Btn("Chiudi");
            remove.IsEnabled = _s.PasswordHash.Length > 0;
            var content = Ui.Col(10,
                Ui.H1("Password per l'accesso senza conferma"),
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Vale per i numeri dello storico con «entra senza conferma»: prima di entrare devono dare questa password " +
                    "(5 errori bloccano quel numero per 10 minuti). Gli altri continuano a chiedere il permesso a chi è davanti al PC." },
                p1, p2, info, Ui.Row(10, close, remove, save));
            var h = Shell.Current.ShowDialog("IP-DESK-CTRL · password", content, 460, false);
            close.Click += (_, _) => h.Close();
            remove.Click += (_, _) =>
            {
                _s.PasswordSalt = ""; _s.PasswordHash = "";
                _s.Save();
                _svc?.Send("PWD -");
                SessionLog.Write("IMPOSTAZIONI password dell'accesso senza conferma tolta");
                h.Close();
            };
            save.Click += async (_, _) =>
            {
                string a = p1.Text ?? "", b = p2.Text ?? "";
                if (a.Length < 6) { info.Text = "La password deve avere almeno 6 caratteri."; return; }
                if (a != b) { info.Text = "Le due password non sono uguali."; return; }
                save.IsEnabled = false;
                var (salt, hash) = await Task.Run(() => AccessPassword.Make(a));
                _s.PasswordSalt = salt; _s.PasswordHash = hash; _s.PasswordIterations = AccessPassword.DefaultIterations;
                _s.Save();
                _svc?.Send($"PWD {salt} {hash} {_s.PasswordIterations}");
                SessionLog.Write("IMPOSTAZIONI password dell'accesso senza conferma impostata");
                h.Close();
            };
        }

        // =================================================================== richieste in arrivo

        private Task<IncomingDecision> AskAcceptAsync(IncomingInfo info, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<IncomingDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                var h = Dialogs.Incoming(info, _s.AllowInput, d => tcs.TrySetResult(d));
                h.Closed += () => tcs.TrySetResult(new IncomingDecision(false, false, false));
                ct.Register(() => Dispatcher.UIThread.Post(() => { tcs.TrySetResult(new IncomingDecision(false, false, false)); h.Close(); }));
            });
            return tcs.Task;
        }

        private void OnHostStarted(HostSession hs)
        {
            var h = _s.Find(hs.PeerNumber);
            string who = (h?.Alias.Length > 0 ? h.Alias + " · " : "") + Identity.Format(hs.PeerNumber);
            _incomingText.Text = $"{who} è collegato a questo PC e lo vede" + (hs.InputAllowed ? " e lo comanda." : ".");
            _incomingInput.IsChecked = hs.InputAllowed;
            _incomingBanner.IsVisible = true;
            _controlled?.Close();
            _controlled = Shell.Current.ShowCorner(Dialogs.Controlled(hs, who));
            RebuildHistory();
        }

        private void OnHostEnded(HostSession hs, string why)
        {
            _incomingBanner.IsVisible = false;
            _controlled?.Close();
            _controlled = null;
            RebuildHistory();
        }

        // =================================================================== chiusura

        public void Shutdown()
        {
            if (_closingForReal) return;
            _closingForReal = true;
            foreach (var v in _viewers.Values.ToList()) v.Disconnect();
            _life.Cancel();
            _controlled?.Close();
            try { _peer?.Dispose(); } catch { }
            _svc?.Dispose();
            _s.Save();
        }

        public static void OpenPath(string path)
        {
            if (Platform.Open != null) { Platform.Open(path); return; }
            try
            {
                if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else if (OperatingSystem.IsMacOS()) Process.Start("open", $"\"{path}\"");
                else Process.Start("xdg-open", $"\"{path}\"");
            }
            catch { }
        }
    }
}
