using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool _closingForReal;

        /// <summary>Titolo da mostrare (la finestra del PC lo usa).</summary>
        public event Action<string>? TitleChanged;

        public MainView(AppSettings s)
        {
            _s = s;
            Content = Build();
            AttachedToVisualTree += (_, _) => { if (_me == null && _peer == null && !_starting) { _starting = true; _ = StartAsync(); } };
        }

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

            // ---- questo PC
            var copy = Ui.Btn("Copia");
            copy.Click += async (_, _) => { var c = TopLevel.GetTopLevel(this)?.Clipboard; if (_me != null && c != null) await c.SetTextAsync(Identity.Format(_me.Number)); };
            _number.Classes.Add("mono");
            var numRow = new DockPanel();
            DockPanel.SetDock(copy, Dock.Right);
            copy.VerticalAlignment = VerticalAlignment.Center;
            numRow.Children.Add(copy);
            numRow.Children.Add(_number);
            _incomingInput.IsCheckedChanged += (_, _) => _peer?.CurrentHost?.SetInputAllowed(_incomingInput.IsChecked == true);
            var stop = Ui.Btn("Disconnetti");
            stop.Click += (_, _) => _peer?.CurrentHost?.Close("chiuso dall'utente dell'altro PC");
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
                c.IsCheckedChanged += (_, _) => { set(c.IsChecked == true); _s.Save(); };
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
            var about = Ui.Btn("Informazioni e licenze");
            about.Click += (_, _) => Dialogs.About(_me);
            return Ui.Col(8,
                Ui.H2("IMPOSTAZIONI"),
                Chk("Accetta richieste di collegamento a questo PC", _s.AcceptIncoming, v => _s.AcceptIncoming = v),
                Chk("Proponi di consentire mouse e tastiera a chi si collega", _s.AllowInput, v => _s.AllowInput = v),
                Chk("Manda l'audio di questo PC a chi si collega", _s.ShareAudio, v => _s.ShareAudio = v),
                Chk("Ascolta l'audio dei PC a cui ti colleghi", _s.PlayAudio, v => _s.PlayAudio = v),
                Ui.Row(10, new TextBlock { Text = "Tema", VerticalAlignment = VerticalAlignment.Center }, theme),
                Ui.Sub("File ricevuti dagli altri PC: " + Platform.Received, 12),
                Wrap(folder, received, about));
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
                _me = await Task.Run(Identity.LoadOrCreate);
                _number.Text = Identity.Format(_me.Number);
                TitleChanged?.Invoke("IP-DESK-CTRL · " + Identity.Format(_me.Number));
                _peer = new Peer(_me, _s);
                _peer.StatusChanged += () => Dispatcher.UIThread.Post(UpdateStatus);
                _peer.AskAccept = AskAcceptAsync;
                _peer.HostStarted += hs => Dispatcher.UIThread.Post(() => OnHostStarted(hs));
                _peer.HostEnded += (hs, why) => Dispatcher.UIThread.Post(() => OnHostEnded(hs, why));
                _peer.Start();
                _ = PresenceLoopAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                _netStatus.Text = "Errore: " + ex.Message;
                _led.Fill = Brushes.Red;
            }
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
            var remove = new MenuItem { Header = "Togli dallo storico" };
            remove.Click += (_, _) => { _s.History.Remove(h); _s.Save(); RebuildHistory(); };
            var flyout = new MenuFlyout();
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
