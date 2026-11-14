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
        private readonly TextBlock _netStatus = Ui.Sub(L.T("Avvio…"));
        private readonly TextBlock _netDetail = Ui.Sub("", 12);
        private readonly Ellipse _led = new() { Width = 12, Height = 12, Fill = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _target = new() { Watermark = "123 456 7890", FontSize = 22, MaxLength = 16 };
        private readonly TextBlock _connectInfo = Ui.Sub("");
        private readonly StackPanel _history = new() { Spacing = 8 };
        private readonly Border _incomingBanner = new() { IsVisible = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        private readonly TextBlock _incomingText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold };
        private readonly CheckBox _incomingInput = new() { Content = L.T("Mouse e tastiera") };
        private IDialogHandle? _controlled;
        private readonly Button _newVersion = new() { Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center };
        private ReleaseInfo? _latest;
        private ReleaseInfo? _autoPending;      // aggiornamento automatico in attesa che si chiudano i collegamenti
        private DispatcherTimer? _autoWait;
        private int _svcHosts;                  // collegamenti in arrivo tenuti dal servizio di Windows

        /// <summary>Collegamenti aperti adesso, nei due versi.</summary>
        private int OpenSessions => _viewers.Count + (_peer?.CurrentHost?.IsActive == true ? 1 : 0) + _svcHosts;
        private readonly Border _update = new() { IsVisible = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(14) };
        private bool _closingForReal;

        /// <summary>Titolo da mostrare (la finestra del PC lo usa).</summary>
        public event Action<string>? TitleChanged;

        public MainView(AppSettings s)
        {
            FlowDirection = L.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;   // arabo, ebraico, urdu
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
            if (!Platform.IsMobile) title.Children.Add(new TextBlock { Text = L.T("desktop remoto cifrato · ") + AppVersion.Text, VerticalAlignment = VerticalAlignment.Center, Classes = { "sub" } });
            var head = new DockPanel { Margin = new Thickness(20, 14, 20, 6) };
            // avviso di nuova versione, in alto a destra accanto allo stato
            _newVersion.Classes.Add("accent");
            _newVersion.IsVisible = false;
            _newVersion.Click += (_, _) => { if (_latest != null) ShowUpdatePopup(_latest, auto: false); };
            // in alto: guida e Ko-fi
            var help = Ui.Btn(L.T("? Guida"));
            help.Click += (_, _) => OpenPath(AppVersion.HelpUrl);
            ToolTip.SetTip(help, L.T("Apre la guida di IP-DESK-CTRL (wiki su GitHub)"));
            var kofi = Ui.Btn(L.T("♥ Ko-fi"));
            kofi.Click += (_, _) => OpenPath(AppVersion.SupportUrl);
            ToolTip.SetTip(kofi, L.T("IP-DESK-CTRL è gratuito e senza pubblicità. Se ti è utile, puoi offrirmi un caffè su Ko-fi."));
            help.Padding = kofi.Padding = new Thickness(10, 4);
            if (Platform.IsMobile) { help.Content = "?"; kofi.Content = "♥"; }
            help.VerticalAlignment = kofi.VerticalAlignment = VerticalAlignment.Center;
            var ledBox = Ui.Row(10, help, kofi, _newVersion, Ui.Row(8, _led, _netStatus));
            DockPanel.SetDock(ledBox, Dock.Right);
            head.Children.Add(ledBox);
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var col = new StackPanel { Spacing = 14, Margin = new Thickness(20, 6, 20, 20) };
            col.Children.Add(_update);

            // ---- questo PC
            var copy = Ui.Btn(L.T("Copia"));
            copy.Click += async (_, _) => { var c = TopLevel.GetTopLevel(this)?.Clipboard; if (_me != null && c != null) await c.SetTextAsync(Identity.Format(_me.Number)); };
            _number.Classes.Add("mono");
            var qr = Ui.Btn("QR");
            ToolTip.SetTip(qr, L.T("Mostra il numero come codice QR, da inquadrare con IP-DESK-CTRL sul telefono"));
            qr.Click += (_, _) => ShowQr();
            var numButtons = Ui.Row(8, qr, copy);
            // il numero si rimpicciolisce per stare tutto nella larghezza (telefono, finestra stretta)
            var numFit = new Viewbox { Child = _number, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
            Control numRow;
            if (Platform.IsMobile)
                numRow = Ui.Col(8, numFit, numButtons);       // telefono: numero intero su una riga, pulsanti sotto
            else
            {
                var dock = new DockPanel();
                DockPanel.SetDock(numButtons, Dock.Right);
                numButtons.VerticalAlignment = VerticalAlignment.Center;
                numButtons.Margin = new Thickness(10, 0, 0, 0);
                dock.Children.Add(numButtons);
                dock.Children.Add(numFit);
                numRow = dock;
            }
            _incomingInput.IsCheckedChanged += (_, _) =>
            {
                _peer?.CurrentHost?.SetInputAllowed(_incomingInput.IsChecked == true);
                _svc?.SetInput(_incomingInput.IsChecked == true);
            };
            var stop = Ui.Btn(L.T("Disconnetti"));
            stop.Click += (_, _) =>
            {
                _peer?.CurrentHost?.Close(L.T("chiuso dall'utente dell'altro PC"));
                _svc?.Kick();
            };
            _incomingBanner.Background = Themes.B(Themes.Current.TxDim);
            _incomingBanner.Child = Ui.Col(8, _incomingText, Ui.Row(10, _incomingInput, stop));
            col.Children.Add(Ui.Panel(Ui.Col(6,
                Ui.H2(Platform.IsMobile ? L.T("IL NUMERO DI QUESTO DISPOSITIVO") : L.T("IL NUMERO DI QUESTO PC")),
                numRow,
                Ui.Sub(L.F("Dallo a chi deve collegarsi a questo {0}: non cambia mai. Ogni volta ti verrà chiesto se accettare.", (Platform.IsMobile ? L.T("dispositivo") : "PC"))),
                _netDetail,
                _incomingBanner)));

            // ---- collegati
            var go = Ui.Btn(L.T("Collegati"), true);
            go.MinWidth = 140;
            go.Click += (_, _) => ConnectTo(_target.Text ?? "");
            _target.KeyDown += (_, e) => { if (e.Key == Key.Enter) ConnectTo(_target.Text ?? ""); };
            _target.TextChanged += (_, _) => FormatTarget();
            Control tRow;
            if (Platform.IsMobile)
            {
                // telefono: campo del numero su tutta la larghezza, pulsanti sotto
                var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(Platform.ScanQr != null ? "*,10,*" : "*") };
                if (Platform.ScanQr != null)
                {
                    var scan = Ui.Btn(L.T("Inquadra QR"));
                    scan.HorizontalAlignment = HorizontalAlignment.Stretch;
                    scan.Click += async (_, _) => await ScanAsync();
                    buttons.Children.Add(scan);
                    Grid.SetColumn(go, 2);
                }
                go.MinWidth = 0;
                go.HorizontalAlignment = HorizontalAlignment.Stretch;
                buttons.Children.Add(go);
                _target.HorizontalAlignment = HorizontalAlignment.Stretch;
                tRow = Ui.Col(10, _target, buttons);
            }
            else
            {
                var dock = new DockPanel();
                DockPanel.SetDock(go, Dock.Right);
                go.Margin = new Thickness(10, 0, 0, 0);
                dock.Children.Add(go);
                if (Platform.ScanQr != null)
                {
                    var scan = Ui.Btn(L.T("Inquadra QR"));
                    DockPanel.SetDock(scan, Dock.Right);
                    scan.Margin = new Thickness(10, 0, 0, 0);
                    scan.Click += async (_, _) => await ScanAsync();
                    dock.Children.Add(scan);
                }
                dock.Children.Add(_target);
                tRow = dock;
            }
            col.Children.Add(Ui.Panel(Ui.Col(8, Ui.H2(L.T("COLLEGATI A UN ALTRO PC")), tRow, _connectInfo)));

            // ---- storico
            var refresh = Ui.Btn(L.T("Aggiorna stato"));
            refresh.Click += (_, _) => _ = RefreshPresenceAsync();
            var hTitle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.H2(L.T("COLLEGAMENTI PRECEDENTI")), Ui.Sub(Platform.IsMobile ? L.T("Pallino verde: il programma è aperto su quel PC.") : L.T("Pallino verde: il programma è aperto su quel PC. Doppio clic per collegarti."), 12) } };
            Control hHead;
            if (Platform.IsMobile) hHead = Ui.Col(8, hTitle, refresh);       // telefono: il pulsante sotto, il titolo non si taglia
            else
            {
                var dock = new DockPanel();
                DockPanel.SetDock(refresh, Dock.Right);
                dock.Children.Add(refresh);
                dock.Children.Add(hTitle);
                hHead = dock;
            }
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
            var folder = Ui.Btn(L.T("Apri cartella dati"));
            folder.Click += (_, _) => OpenPath(AppSettings.Folder);
            folder.IsVisible = !Platform.IsMobile;
            var received = Ui.Btn(L.T("File ricevuti"));
            received.Click += (_, _) => OpenPath(Platform.Received);
            received.IsVisible = !Platform.IsMobile;
            var password = Ui.Btn(L.T("Password per l'accesso senza conferma…"));
            password.Click += (_, _) => PasswordDialog();
            ToolTip.SetTip(password, L.T("Chi è autorizzato a entrare senza conferma dovrà anche conoscere questa password."));
            var about = Ui.Btn(L.T("Informazioni e licenze"));
            about.Click += (_, _) => Dialogs.About(_me);
            var touch = Ui.Btn(L.T("Tocco e gesti…"));
            touch.Click += (_, _) => TouchDialog.Show(_s);
            ToolTip.SetTip(touch, L.T("Telefono, tablet e schermi touch: modo del dito, gesti, velocità del puntatore, barra dei tasti."));
            var quit = Ui.Btn(L.T("Esci dal programma"));
            quit.Click += (_, _) => Quit?.Invoke();
            quit.IsVisible = Quit != null;
            ToolTip.SetTip(quit, L.T("Chiude davvero IP-DESK-CTRL: questo PC non sarà più raggiungibile finché non lo riavvii. La X della finestra invece lo nasconde soltanto."));
            return Ui.Col(8,
                Ui.H2("IMPOSTAZIONI"),
                Chk(L.T("Accetta richieste di collegamento a questo PC"), _s.AcceptIncoming, v => _s.AcceptIncoming = v),
                Chk(L.T("Proponi di consentire mouse e tastiera a chi si collega"), _s.AllowInput, v => _s.AllowInput = v),
                Chk(L.T("Manda l'audio di questo PC a chi si collega"), _s.ShareAudio, v => _s.ShareAudio = v),
                Chk(L.T("Ascolta l'audio dei PC a cui ti colleghi"), _s.PlayAudio, v => _s.PlayAudio = v),
                Chk(L.T("Accetta il microfono di chi si collega (la sua voce arriva qui)"), _s.AcceptMic, v => _s.AcceptMic = v),
                MicOutputRow(),
                Chk(L.T("Chi si collega può adattare la risoluzione di questo PC alla sua finestra"), _s.AllowResize, v => _s.AllowResize = v),
                Chk(L.T("Chi si collega può montare qui una sua cartella come disco"), _s.AllowDrive, v => _s.AllowDrive = v),
                Chk(L.T("Chi si collega può aprire tunnel di porte attraverso questo PC (rigctld, flrig, web della radio…)"), _s.AllowTunnel, v => _s.AllowTunnel = v),
                LanguageRow(),
                DockRow(),
                BrokersRow(),
                Chk(L.T("Aggiornamenti automatici: ogni 30 minuti cerca una nuova versione e la installa da sola (i collegamenti riprendono)"), _s.AutoUpdate, v => _s.AutoUpdate = v),
                Chk(L.T("Mentre uno comanda, altri possono chiedere di guardare (solo visione, al massimo 4)"), _s.AllowObservers, v => _s.AllowObservers = v),
                VideoRow(),
                Ui.Row(10, new TextBlock { Text = L.T("Tema"), VerticalAlignment = VerticalAlignment.Center }, theme),
                Ui.Sub(L.T("File ricevuti dagli altri PC: ") + Platform.Received, 12),
                Wrap(password, touch, folder, received, about, quit));
        }

        /// <summary>Video compresso: stato di ffmpeg e, su Windows 64 bit, scaricamento con un clic.</summary>
        private Control VideoRow()
        {
            if (Platform.IsMobile) return new Panel();
            var text = Ui.Sub("", 12);
            var get = Ui.Btn(L.T("Scarica ffmpeg (circa 150 MB)"));
            void Show()
            {
                string? p = Screen.Ffmpeg.Path;
                get.IsVisible = p == null && Screen.Ffmpeg.CanDownload;
                text.Text = p != null
                    ? L.T("Video compresso H.264 / H.265 / AV1 attivo (ffmpeg: ") + p + L.T("). Si usa quando anche l'altro PC ha ffmpeg; i codificatori della scheda video si provano da soli.")
                    : OperatingSystem.IsWindows() ? L.T("Video compresso (H.264, H.265, AV1): serve ffmpeg, che si scarica qui sotto una volta sola. Senza, lo schermo viaggia a riquadri JPEG come prima.")
                    : OperatingSystem.IsMacOS() ? L.T("Video compresso (H.264, H.265, AV1): installa ffmpeg con «brew install ffmpeg». Senza, lo schermo viaggia a riquadri JPEG.")
                    : L.T("Video compresso (H.264, H.265, AV1): installa ffmpeg con «sudo apt install ffmpeg». Senza, lo schermo viaggia a riquadri JPEG.");
            }
            get.Click += async (_, _) =>
            {
                get.IsEnabled = false;
                string err = await Screen.Ffmpeg.DownloadAsync(m => Dispatcher.UIThread.Post(() => text.Text = m), _life.Token);
                get.IsEnabled = true;
                Show();
                if (err.Length > 0) text.Text = err;
                else _ = Screen.Ffmpeg.EncodersAsync();
            };
            Show();
            return Ui.Col(4, text, get);
        }

        /// <summary>Uscita dove suonare il microfono di chi si collega (Windows: elenco delle uscite; altrove automatica).</summary>
        private Control MicOutputRow()
        {
            var names = Audio.SystemAudio.OutputNames();
            if (names.Length == 0)
                return Ui.Sub(OperatingSystem.IsLinux()
                    ? L.T("La voce di chi si collega arriva nel microfono virtuale «IP-DESK-CTRL-microfono», creato da solo durante il collegamento.")
                    : L.T("Per usare la voce di chi si collega come microfono nei programmi installa BlackHole 2ch; senza, si sente dalle casse."), 12);
            var items = new List<string> { L.T("Automatica (cavo virtuale VB-CABLE se c'è, altrimenti le casse)") };
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
            ToolTip.SetTip(box, L.T("Con un cavo audio virtuale (VB-CABLE, gratuito) la voce di chi si collega diventa un microfono per i programmi di questo PC, anche per IP-TRX-CTRL."));
            return Ui.Row(10, new TextBlock { Text = L.T("Voce di chi si collega su"), VerticalAlignment = VerticalAlignment.Center }, box);
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
                TitleChanged?.Invoke(L.T("IP-DESK-CTRL · ") + Identity.Format(_me.Number));
                if (svcId != null) await AttachServiceAsync();
                _peer = new Peer(_me, _s) { ViewerOnly = svcId != null };
                _peer.StatusChanged += () => Dispatcher.UIThread.Post(UpdateStatus);
                _peer.AskAccept = AskAcceptAsync;
                _peer.HostStarted += hs => Dispatcher.UIThread.Post(() => OnHostStarted(hs));
                _peer.HostEnded += (hs, why) => Dispatcher.UIThread.Post(() => OnHostEnded(hs, why));
                _peer.Start();
                _ = PresenceLoopAsync();
                _ = UpdateCheck.LoopAsync(r => Dispatcher.UIThread.Post(() => ShowUpdate(r)), _life.Token);
                ResumeAfterUpdate();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                _netStatus.Text = L.T("Errore: ") + ex.Message;
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
            _svc.HostStarted += (n, input) => Dispatcher.UIThread.Post(() => { _svcHosts++; OnServiceHostStarted(n, input); });
            _svc.HostEnded += (n, why) => Dispatcher.UIThread.Post(() => { _svcHosts = Math.Max(0, _svcHosts - 1); _incomingBanner.IsVisible = false; RebuildHistory(); });
            _svc.Lost += () => Dispatcher.UIThread.Post(() => { _svcHosts = 0; _incomingBanner.IsVisible = false; });
            _svc.SendSettings(_s.AcceptIncoming, _s.AllowInput, _s.ShareAudio);
            if (_s.PasswordHash.Length > 0) _svc.Send($"PWD {_s.PasswordSalt} {_s.PasswordHash} {_s.PasswordIterations}");
            _svc.Send("MQTT " + string.Join(",", _s.MqttBrokers));   // il servizio usa gli stessi broker del programma
            SessionLog.Write("SERVIZIO collegato: i collegamenti a questo PC li riceve il servizio di Windows");
        }

        private void OnServiceHostStarted(string number, bool input)
        {
            var h = _s.Find(number);
            string who = (h?.Alias.Length > 0 ? h.Alias + " · " : "") + Identity.Format(number);
            _incomingText.Text = L.F("{0} è collegato a questo PC e lo vede", who) + (input ? L.T(" e lo comanda.") : ".");
            _incomingInput.IsChecked = input;
            _incomingBanner.IsVisible = true;
            RebuildHistory();
        }

        private void UpdateStatus()
        {
            if (_peer == null) return;
            _netStatus.Text = _peer.Ready ? (_peer.Brokers > 0 ? L.T("In linea") : L.T("Broker MQTT non raggiungibili")) : _peer.Status;
            _led.Fill = !_peer.Ready ? Brushes.Gray : _peer.Brokers > 0 ? Themes.B(Themes.Current.Ok) : Brushes.OrangeRed;
            _netDetail.Text = _peer.Status + (_peer.Detail.Length > 0 ? " · " + _peer.Detail : "") + L.F(" · broker collegati: {0}", _peer.Brokers);
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
            if (_peer == null) { _connectInfo.Text = L.T("Attendi: il programma si sta avviando…"); return; }
            if (!Identity.IsValidNumber(number)) { _connectInfo.Text = L.T("Il numero deve avere 10 cifre."); return; }
            if (number == _me?.Number) { _connectInfo.Text = L.T("Questo è il numero di questo PC."); return; }
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
                _history.Children.Add(Ui.Sub(L.T("Ancora nessun collegamento. I PC a cui ti colleghi e quelli che si collegano a te compariranno qui.")));
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
            ToolTip.SetTip(dot, age == null ? L.T("Non in linea (o stato non ancora letto)") : ViewerRendezvous.Age(age.Value));

            string title = h.Alias.Length > 0 ? h.Alias : Identity.Format(h.Number);
            var name = new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 15 };
            string dir = h.Outgoing ? L.T("tu → lui") : L.T("lui → te");
            string when = h.LastUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            var info = Ui.Sub($"{(h.Alias.Length > 0 ? Identity.Format(h.Number) + " · " : "")}{when} · {dir} · {h.Count} {(h.Count == 1 ? "volta" : "volte")}"
                              + (h.LastSeconds.Length > 0 ? L.F(" · ultima durata {0}", h.LastSeconds) : "")
                              + (h.Os.Length > 0 ? $" · {h.Os}" : "")
                              + (h.AllowUnattended ? L.T(" · entra senza conferma") : ""), 12);
            var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, info } };
            // controllo preliminare delle versioni (ultima vista sui broker, altrimenti quella dell'ultimo collegamento)
            string ver = ViewerRendezvous.Versions.TryGetValue(h.Number, out var seenVer) ? seenVer : h.Version;
            if (ver.Length > 0)
            {
                var (_, verText) = AppVersion.Parse(ver);
                string warn = AppVersion.Compare(ver);
                info.Text += L.T(" · versione ") + verText;
                if (warn.Length > 0)
                    texts.Children.Add(new TextBlock { Text = "⚠ " + warn, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange });
            }

            var connect = Ui.Btn(L.T("Collegati"), true);
            connect.Click += (_, _) => ConnectTo(h.Number);
            var alias = Ui.Btn(L.T("Alias"));
            ToolTip.SetTip(alias, L.T("Dai un nome a questo numero"));
            var more = Ui.Btn("···");
            var unattended = new MenuItem { Header = h.AllowUnattended ? L.T("✓ Può entrare in questo PC senza conferma") : L.T("Consenti di entrare in questo PC senza conferma") };
            unattended.Click += (_, _) => { h.AllowUnattended = !h.AllowUnattended; _s.Save(); RebuildHistory(); };
            var wake = new MenuItem { Header = L.T("Accendi (Wake-on-LAN)"), IsEnabled = h.Mac.Length == 12 };
            wake.Click += async (_, _) =>
            {
                if (_peer == null) return;
                _connectInfo.Text = L.T("Accensione di ") + title + "…";
                _connectInfo.Text = await _peer.WakeAsync(h, _life.Token);
            };
            var remove = new MenuItem { Header = L.T("Togli dallo storico") };
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
                var edit = new TextBox { Text = h.Alias, Watermark = L.T("Nome da ricordare (es. PC della stazione)"), MinWidth = 220 };
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
                texts.Children.Add(Ui.Sub(Identity.Format(h.Number) + L.T(" · Invio per salvare, Esc per annullare"), 12));
                edit.AttachedToVisualTree += (_, _) => { edit.Focus(); edit.SelectAll(); };
            };
            return box;
        }

        // =================================================================== QR del numero

        private void ShowQr()
        {
            if (_me == null) return;
            var close = Ui.Btn(L.T("Chiudi"));
            var content = Ui.Col(12,
                Ui.H1(L.T("Numero di questo PC")),
                new Border { Width = 280, Height = 280, HorizontalAlignment = HorizontalAlignment.Center, Child = new QrView(QrCode.ForNumber(_me.Number)) },
                new TextBlock { Text = Identity.Format(_me.Number), FontSize = 28, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                Ui.Sub(L.T("Inquadralo con «Inquadra QR» di IP-DESK-CTRL sul telefono per collegarti a questo PC."), 12),
                close);
            var h = Shell.Current.ShowDialog(L.T("IP-DESK-CTRL · QR del numero"), content, 360, false);
            close.Click += (_, _) => h.Close();
        }

        private async Task ScanAsync()
        {
            var f = Platform.ScanQr;
            if (f == null) return;
            string? text = await f();
            if (text == null)
            {
                _connectInfo.Text = L.T("Nessuna app per leggere i QR (o lettura annullata): scrivi il numero a mano.");
                return;
            }
            string? n = QrCode.NumberFrom(text);
            if (n == null) { _connectInfo.Text = L.T("Il QR letto non contiene un numero di IP-DESK-CTRL."); return; }
            _target.Text = n;
            ConnectTo(n);
        }

        // =================================================================== nuova versione

        // =================================================================== aggiornamento dal programma aperto

        private IDialogHandle? _updateDialog;
        private CancellationTokenSource? _updateCts;

        /// <summary>Riapre i collegamenti che erano aperti prima dell'aggiornamento di questo PC.</summary>
        private void ResumeAfterUpdate()
        {
            if (_s.ResumeUntil < DateTime.UtcNow.Ticks || _s.ResumeViewers.Count == 0) { if (_s.ResumeViewers.Count > 0) { _s.ResumeViewers.Clear(); _s.Save(); } return; }
            var list = _s.ResumeViewers.ToList();
            _s.ResumeViewers.Clear();
            _s.ResumeUntil = 0;
            _s.Save();
            SessionLog.Write($"AGGIORNAMENTI riprendo {list.Count} collegamenti dopo l'aggiornamento a {AppVersion.Text}");
            // si lascia il tempo di pubblicarsi sui broker
            DispatcherTimer.RunOnce(() => { foreach (var n in list) ConnectTo(n); }, TimeSpan.FromSeconds(4));
        }

        /// <summary>
        /// Popup della nuova versione con il programma aperto: scarica, controlla e installa da solo; prima avvisa
        /// gli altri PC collegati, così i collegamenti riprendono dopo il riavvio del programma.
        /// </summary>
        private void ShowUpdatePopup(ReleaseInfo r, bool auto)
        {
            if (_updateDialog != null) return;
            var kind = Updater.Detect();
            var asset = Updater.FindAsset(r);
            bool service = kind == Updater.Kind.WindowsSetup && _svc != null;
            if (kind == Updater.Kind.None || asset == null) return;

            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, IsVisible = false };
            var status = Ui.Sub("", 12);
            int sessions = OpenSessions;
            var go = Ui.Btn(kind == Updater.Kind.Android ? L.T("Scarica l'APK") : L.T("Aggiorna ora"), true);
            var later = Ui.Btn(L.T("Più tardi"));
            var notes = Ui.Btn(L.T("Novità"));
            var countdown = Ui.Sub("", 12);
            var content = Ui.Col(10,
                Ui.H1(L.F("È uscita IP-DESK-CTRL {0}", r.Version)),
                Ui.Sub(L.F("Ora hai la {0}. ", AppVersion.Text) + (kind == Updater.Kind.Android
                    ? L.T("Si apre il browser per scaricare l'APK.")
                    : L.T("La scarico e la installo da solo: il programma si chiude e si riapre.") +
                      (sessions > 0 ? L.F(" I collegamenti aperti ({0}) riprendono da soli entro qualche decina di secondi.", sessions) : ""))),
                Ui.Sub(Updater.HowItGoes(kind, service), 12),
                countdown,
                bar, status,
                Ui.Row(10, go, later, notes));
            _updateDialog = Shell.Current.ShowDialog(L.T("IP-DESK-CTRL · aggiornamento"), content, 480, false);
            _updateDialog.Closed += () => _updateDialog = null;
            notes.Click += (_, _) => OpenPath(r.PageUrl);
            DispatcherTimer? timer = null;
            later.Click += (_, _) =>
            {
                timer?.Stop();
                // «Più tardi» su un'installazione automatica: per questa versione non riparte da sola, resta l'avviso in alto
                if (auto) { _s.SkipAutoVersion = r.Version; _s.Save(); }
                _updateCts?.Cancel(); _updateDialog?.Close(); _updateDialog = null;
            };
            _updateDialog.Closed += () => timer?.Stop();
            go.Click += async (_, _) =>
            {
                if (kind == Updater.Kind.Android) { OpenPath(asset.Url); _updateDialog?.Close(); _updateDialog = null; return; }
                go.IsEnabled = false;
                bar.IsVisible = true;
                _updateCts = new CancellationTokenSource();
                var (path, sha, err) = await Updater.DownloadAsync(asset,
                    (f, t) => Dispatcher.UIThread.Post(() => { bar.Value = f; status.Text = L.T("Scarico… ") + t; }), _updateCts.Token);
                if (err.Length > 0) { status.Text = L.T("Aggiornamento non riuscito: ") + err; go.IsEnabled = true; return; }
                status.Text = L.T("Avviso i PC collegati e installo…");
                // chi è collegato lo sa prima: il PC comandato lascerà rientrare, il visore si ricollegherà
                try
                {
                    _peer?.CurrentHost?.PrepareUpdate();
                    var open = _viewers.Keys.ToList();
                    foreach (var v in _viewers.Values) v.PrepareUpdate();
                    _s.ResumeViewers = open;
                    _s.ResumeUntil = DateTime.UtcNow.Add(ResumeState.Window).Ticks;
                    _s.Save();
                }
                catch (Exception ex) { ErrorLog.Write("Aggiornamento, avviso ai collegati: " + ex.Message); }
                await Task.Delay(600);           // i messaggi partono prima di chiudere
                string why = await Task.Run(() => Updater.Install(kind, path, sha, service, r.Version));
                if (why.Length > 0)
                {
                    status.Text = L.T("Aggiornamento non riuscito: ") + why;
                    _s.ResumeViewers.Clear(); _s.ResumeUntil = 0; _s.Save();
                    go.IsEnabled = true;
                    return;
                }
                SessionLog.Write($"AGGIORNAMENTI installo la {r.Version}: il programma si chiude e si riapre");
                status.Text = L.T("Installo: il programma si chiude e si riapre da solo…");
                await Task.Delay(400);
                if (Quit != null) Quit(); else Environment.Exit(0);
            };

            if (auto && kind != Updater.Kind.Android)
            {
                // installazione automatica: un minuto per fermarla con «Più tardi»
                int left = 60;
                countdown.Text = L.F("Aggiornamento automatico: parte da solo tra {0} s.", left);
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, _) =>
                {
                    left--;
                    if (left > 0) { countdown.Text = L.F("Aggiornamento automatico: parte da solo tra {0} s.", left); return; }
                    timer.Stop();
                    if (OpenSessions > 0)
                    {
                        // nel frattempo si è collegato qualcuno: si aspetta che finisca
                        _updateDialog?.Close(); _updateDialog = null;
                        ShowUpdate(r);
                        return;
                    }
                    countdown.Text = L.T("Aggiornamento automatico in corso.");
                    SessionLog.Write($"AGGIORNAMENTI installazione automatica della {r.Version}");
                    if (go.IsEnabled) go.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
            }
        }

        /// <summary>Lingua del programma: automatica (quella del sistema) o una delle 36. Vale dal riavvio.</summary>
        private Control LanguageRow()
        {
            var names = new List<string> { L.T("Automatica (lingua del sistema)") };
            names.AddRange(L.All.Select(a => a.name));
            var box = new ComboBox
            {
                ItemsSource = names, MinWidth = 220,
                SelectedIndex = _s.Language.Length == 0 ? 0 : Math.Max(0, Array.FindIndex(L.All, a => a.code == _s.Language) + 1)
            };
            var note = Ui.Sub("", 12);
            var restart = Ui.Btn(L.T("Riavvia ora"));
            restart.IsVisible = false;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                string code = box.SelectedIndex == 0 ? "" : L.All[box.SelectedIndex - 1].code;
                if (code == _s.Language) return;
                _s.Language = code;
                _s.Save();
                note.Text = L.T("La lingua cambia quando riavvii il programma.");
                restart.IsVisible = !Platform.IsMobile && Environment.ProcessPath != null;
            };
            restart.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--riavvio") { UseShellExecute = false });
                    if (Quit != null) Quit(); else Environment.Exit(0);
                }
                catch (Exception ex) { note.Text = ex.Message; }
            };
            return Ui.Col(6, Ui.Row(10, new TextBlock { Text = L.T("Lingua"), VerticalAlignment = VerticalAlignment.Center }, box, restart), note);
        }

        /// <summary>Elenco dei broker MQTT per il ritrovo, modificabile: uno per riga, anche cifrati (mqtts://host:8883).</summary>
        private Control BrokersRow()
        {
            var box = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 80, MinWidth = 280,
                Text = string.Join("\n", _s.MqttBrokers),
                Watermark = L.T("host:porta, uno per riga")
            };
            var save = Ui.Btn(L.T("Usa questi broker"));
            var reset = Ui.Btn(L.T("Predefiniti"));
            var note = Ui.Sub(L.T("Uno per riga: host:porta (1883 in chiaro, 8883 cifrato) oppure mqtts://host:porta. Tutti e due i PC devono avere almeno un broker in comune. Valgono subito."), 12);
            save.Click += (_, _) =>
            {
                var list = (box.Text ?? "").Split('\n', '\r', ',', ' ', ';')
                    .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var bad = list.FirstOrDefault(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^((mqtts?|ssl|tcp)://)?[A-Za-z0-9.\-]+(:\d{1,5})?/?$"));
                if (bad != null) { note.Text = L.F("«{0}» non è un indirizzo valido (esempio: broker.hivemq.com:1883).", bad); return; }
                if (list.Count == 0) list = AppSettings.DefaultBrokers.ToList();
                box.Text = string.Join("\n", list);
                _peer.SetBrokers(list);
                _svc?.Send("MQTT " + string.Join(",", list));
                note.Text = L.F("Broker in uso: {0}. Mi ricollego…", list.Count);
            };
            reset.Click += (_, _) => box.Text = string.Join("\n", AppSettings.DefaultBrokers);
            return Ui.Col(6, Ui.Lbl(L.T("BROKER MQTT (RITROVO DEI PC)")), box, Ui.Row(10, save, reset), note);
        }

        /// <summary>Dove sta la plancia nella finestra del collegamento (sotto c'è la fascia arancione degli avvisi).</summary>
        private Control DockRow()
        {
            string[] keys = { "sopra", "sinistra", "destra" };
            var box = new ComboBox
            {
                ItemsSource = new[] { L.T("In alto"), L.T("A sinistra"), L.T("A destra") },
                SelectedIndex = Math.Max(0, Array.IndexOf(keys, _s.ToolbarDock)),
                MinWidth = 140
            };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                _s.ToolbarDock = keys[box.SelectedIndex];
                _s.Save();
                ViewerView.RaiseDockChanged();
            };
            var row = Ui.Row(10, new TextBlock { Text = L.T("Plancia del collegamento (sempre a scomparsa):"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, box);
            row.IsVisible = !Platform.IsMobile;
            return row;
        }

        private void ShowUpdate(ReleaseInfo r)
        {
            bool newOne = _latest?.Version != r.Version;
            _latest = r;
            _newVersion.Content = L.F("Nuova versione {0}", r.Version);
            ToolTip.SetTip(_newVersion, L.F("È uscita IP-DESK-CTRL {0} (questa è la {1}). Clic per aggiornare.", r.Version, AppVersion.Text));
            _newVersion.IsVisible = true;

            var kind = Updater.Detect();
            bool canInstall = kind != Updater.Kind.None && Updater.FindAsset(r) != null;
            if (canInstall)
            {
                _update.IsVisible = false;
                bool auto = _s.AutoUpdate && kind != Updater.Kind.Android && _s.SkipAutoVersion != r.Version;
                if (auto && OpenSessions > 0)
                {
                    // con collegamenti aperti non si interrompe nessuno: si installa appena si chiude l'ultimo
                    if (_autoPending?.Version != r.Version) SessionLog.Write($"AGGIORNAMENTI la {r.Version} si installerà da sola appena si chiudono i collegamenti aperti");
                    _autoPending = r;
                    ToolTip.SetTip(_newVersion, L.F("È uscita IP-DESK-CTRL {0} (questa è la {1}). ", r.Version, AppVersion.Text) +
                                                L.T("Si installa da sola appena si chiudono i collegamenti aperti; clic per aggiornare subito."));
                    if (_autoWait == null)
                    {
                        _autoWait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                        _autoWait.Tick += (_, _) =>
                        {
                            if (_autoPending == null) { _autoWait!.Stop(); _autoWait = null; return; }
                            if (OpenSessions > 0) return;
                            var p = _autoPending;
                            _autoPending = null;
                            _autoWait!.Stop(); _autoWait = null;
                            ShowUpdatePopup(p, auto: true);
                        };
                        _autoWait.Start();
                    }
                    return;
                }
                // con l'aggiornamento automatico ci si riprova a ogni controllo; senza, il riquadro compare una volta per versione
                if (auto || newOne) ShowUpdatePopup(r, auto);
                return;
            }
            if (!newOne) return;
            bool apk = Platform.IsMobile && r.ApkUrl.Length > 0;
            var open = Ui.Btn(apk ? L.T("Scarica l'APK") : L.T("Vai allo scaricamento"), true);
            open.Click += (_, _) => OpenPath(apk ? r.ApkUrl : r.PageUrl);
            var later = Ui.Btn(L.T("Più tardi"));
            later.Click += (_, _) => _update.IsVisible = false;
            var text = new TextBlock
            {
                Text = L.F("È disponibile IP-DESK-CTRL {0} (questa è la {1}). ", r.Version, AppVersion.Text) +
                       L.T("Aggiorna tutti e due i PC: versioni con protocollo diverso non si collegano."),
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold
            };
            _update.Background = Themes.B(Themes.Current.TxDim);
            _update.Child = Ui.Col(8, text, Ui.Row(10, later, open));
            _update.IsVisible = true;
        }

        // =================================================================== password dell'accesso senza conferma

        private void PasswordDialog()
        {
            var p1 = new TextBox { PasswordChar = '•', Watermark = L.T("Nuova password") };
            var p2 = new TextBox { PasswordChar = '•', Watermark = L.T("Ripeti la password") };
            var info = Ui.Sub(_s.PasswordHash.Length > 0
                ? L.T("Una password è già impostata. Puoi cambiarla o toglierla.")
                : L.T("Nessuna password: chi è autorizzato entra senza conferma e senza password."), 12);
            var save = Ui.Btn(L.T("Salva"), true);
            var remove = Ui.Btn(L.T("Togli la password"));
            var close = Ui.Btn(L.T("Chiudi"));
            remove.IsEnabled = _s.PasswordHash.Length > 0;
            var content = Ui.Col(10,
                Ui.H1(L.T("Password per l'accesso senza conferma")),
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L.T("Vale per i numeri dello storico con «entra senza conferma»: prima di entrare devono dare questa password ") +
                    L.T("(5 errori bloccano quel numero per 10 minuti). Gli altri continuano a chiedere il permesso a chi è davanti al PC.") },
                p1, p2, info, Ui.Row(10, close, remove, save));
            var h = Shell.Current.ShowDialog(L.T("IP-DESK-CTRL · password"), content, 460, false);
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
                if (a.Length < 6) { info.Text = L.T("La password deve avere almeno 6 caratteri."); return; }
                if (a != b) { info.Text = L.T("Le due password non sono uguali."); return; }
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
            _incomingText.Text = L.F("{0} è collegato a questo PC e lo vede", who) + (hs.InputAllowed ? L.T(" e lo comanda.") : ".");
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
