using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IpDeskCtrl.Core;
using IpDeskCtrl.Input;
using IpDeskCtrl.Net;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Vista di un altro PC (finestra sul PC, schermata intera su Android). In alto al centro c'è la plancia di
    /// comando: finestra / schermo intero, zoom in percentuale o adattato, monitor, qualità, audio, tastiera
    /// (telefono), tasti speciali, file, disconnessione. A schermo intero la plancia si riduce a una linguetta.
    /// I file trascinati sopra la vista vanno al PC remoto e vengono incollati dove sono stati lasciati.
    /// </summary>
    public sealed class ViewerView : UserControl
    {
        private static readonly int[] Zooms = { 0, 25, 50, 75, 100, 125, 150, 200 };
        private const string Sentinel = "  ";

        private readonly Peer _peer;
        private readonly string _number;
        private string _alias;
        private ViewerSession? _session;
        private CancellationTokenSource _cts = new();
        private bool _ended;
        private CancellationTokenSource? _rebootCts;     // attesa del PC remoto dopo il riavvio
        private readonly Button _radio = new() { Content = "Radio (IP-TRX-CTRL)" };

        private readonly RemoteView _view = new();
        private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly Panel _viewHost = new();
        private readonly Border _toolbar = new();
        private readonly Border _tab = new();
        private readonly Border _overlay = new();
        private readonly TextBlock _overlayTitle = new() { FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        private readonly TextBlock _overlayText = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 520 };
        private readonly StackPanel _overlayButtons = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Border _note = new() { IsVisible = false };
        private readonly TextBlock _noteText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, MaxWidth = 700 };
        private readonly Button _noteOpen = new() { Content = "Apri cartella", IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Border _transfer = new() { IsVisible = false };
        private readonly TextBlock _transferText = new() { Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        private readonly ProgressBar _transferBar = new() { Width = 300, Minimum = 0, Maximum = 100 };

        private readonly Button _fullBtn = new();
        private readonly ComboBox _zoom = new() { MinWidth = 100 };
        private readonly ComboBox _monitor = new() { MinWidth = 140, MaxWidth = 260 };
        private readonly ComboBox _quality = new() { MinWidth = 120 };
        private readonly ToggleButton _audio = new() { Content = "Audio" };
        private readonly ToggleButton _mic = new() { Content = "Microfono" };
        private readonly ComboBox _codec = new() { MinWidth = 120 };
        private static readonly string[] CodecKeys = { "auto", "h264", "hevc", "av1", "jpeg" };
        private static readonly string[] CodecLabels = { "Video automatico", "H.264", "H.265", "AV1", "Riquadri JPEG" };
        private readonly Button _tunnelBtn = new() { Content = "Tunnel ▾" };
        private readonly ToggleButton _fitRes = new() { Content = "Risoluzione adatta" };
        private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
        private (int w, int h) _lastResize;

        // pannello scorciatoie (pressione lunga o tocco)
        private readonly Border _panel = new() { IsVisible = false };
        private readonly StackPanel _panelKeys = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        private readonly ScrollViewer _panelScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        private int _panelIndex, _panelStart;
        private bool _panelHold, _panelCancel;
        private static int _panelLast;
        private readonly Button _files = new() { Content = "File ▾" };
        private readonly TextBlock _stats = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro, MinWidth = 150 };

        // tastiera del telefono
        private readonly TextBox _kbd = new() { Width = 2, Height = 2, Opacity = 0.01, Text = Sentinel, AcceptsReturn = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        private readonly Border _keysBar = new() { IsVisible = false };
        private readonly ToggleButton _ctrl = new() { Content = "Ctrl" }, _alt = new() { Content = "Alt" }, _win = new() { Content = "Win" }, _shift = new() { Content = "Maiusc" };
        private bool _kbdBusy;

        private bool _toolbarPinned = true;
        private bool _updatingMonitors;
        private int _framePosted;
        private DateTime _hideToolbarAt = DateTime.MaxValue;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

        public event Action? HistoryChanged;
        /// <summary>La vista è stata chiusa (finestra chiusa o Disconnetti).</summary>
        public event Action? Ended;

        public ViewerView(Peer peer, string number, string alias)
        {
            _peer = peer;
            _number = number;
            _alias = alias;
            Background = Brushes.Black;
            _view.ShowRemoteCursor = peer.S.ShowRemoteCursor;
            _view.FullScreenToggleRequested += ToggleFullScreen;
            peer.S.TouchInput.Normalize();
            _view.Touch = peer.S.TouchInput;
            _view.ActionRequested += OnTouchAction;
            _view.PanelHoldStarted += () => OpenPanel(true);
            _view.PanelHoldMoved += PanelMove;
            _view.PanelHoldEnded += PanelRelease;
            _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); SendResize(); };
            _viewHost.SizeChanged += (_, _) => { if (_fitRes.IsChecked == true) { _resizeTimer.Stop(); _resizeTimer.Start(); } };
            Content = Build();
            SetZoom(peer.S.ZoomPercent);
            _timer.Tick += (_, _) => Tick();
            PointerMoved += (_, e) =>
            {
                if (Shell.Current.IsFullScreen(this) && e.GetPosition(this).Y < 8) ShowToolbar(true);
            };
        }

        public void Start()
        {
            RefreshTitle();
            _timer.Start();
            _ = ConnectAsync();
        }

        /// <summary>Chiusura dall'utente (pulsante Disconnetti, tasto Indietro): chiude il collegamento e la vista.</summary>
        public void Disconnect()
        {
            _rebootCts?.Cancel();
            _cts.Cancel();
            _session?.Close("chiuso dall'altro PC");
            Shell.Current.CloseViewer(this);
        }

        /// <summary>La shell ha tolto la vista (finestra chiusa).</summary>
        public void OnClosedByShell()
        {
            if (_ended) return;
            _ended = true;
            _timer.Stop();
            _rebootCts?.Cancel();
            _cts.Cancel();
            _session?.Close("finestra chiusa sull'altro PC");
            Ended?.Invoke();
        }

        public void RefreshTitle()
        {
            _alias = _peer.S.Find(_number)?.Alias ?? _alias;
            Shell.Current.SetViewerTitle(this, (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number) + " · IP-DESK-CTRL");
        }

        // =================================================================== costruzione

        private Control Build()
        {
            var grid = new Grid();
            _viewHost.Children.Add(_view);
            grid.Children.Add(_viewHost);

            // trascinare file sulla vista = mandarli al PC remoto, dove sono stati lasciati
            DragDrop.SetAllowDrop(_view, true);
            _view.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None; });
            _view.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(e));

            // ---- plancia di comando, in alto al centro
            _fullBtn.Content = "Schermo intero";
            _fullBtn.Click += (_, _) => ToggleFullScreen();

            _zoom.ItemsSource = Zooms.Select(z => z == 0 ? "Adatta" : z + " %").ToList();
            _zoom.SelectionChanged += (_, _) => { if (_zoom.SelectedIndex >= 0) SetZoom(Zooms[_zoom.SelectedIndex], fromCombo: true); };
            var minus = Small("−", "Riduci");
            minus.Click += (_, _) => StepZoom(-1);
            var plus = Small("+", "Ingrandisci");
            plus.Click += (_, _) => StepZoom(+1);

            _monitor.SelectionChanged += (_, _) =>
            {
                if (_updatingMonitors || _session == null || _monitor.SelectedIndex < 0) return;
                int n = _session.Monitors.Count;
                _session.SelectMonitor(_monitor.SelectedIndex >= n ? -1 : _monitor.SelectedIndex);
            };
            ToolTip.SetTip(_monitor, "Quale schermo del PC remoto vedere");

            _quality.ItemsSource = Quality.Names.ToList();
            _quality.SelectedIndex = Math.Max(0, Array.IndexOf(Quality.Keys, _peer.S.Quality));
            _quality.SelectionChanged += (_, _) =>
            {
                if (_quality.SelectedIndex < 0) return;
                _peer.S.Quality = Quality.Keys[_quality.SelectedIndex];
                _peer.S.Save();
                _session?.SetQuality(_peer.S.Quality);
            };
            ToolTip.SetTip(_quality, "Automatica: si adatta alla velocità della connessione");

            _audio.IsChecked = _peer.S.PlayAudio;
            _audio.IsCheckedChanged += (_, _) => _session?.SetAudio(_audio.IsChecked == true);
            ToolTip.SetTip(_audio, "Ascolta l'audio del PC remoto");

            _mic.IsChecked = false;
            _mic.IsVisible = false;
            _mic.IsCheckedChanged += (_, _) => ToggleMic();
            ToolTip.SetTip(_mic, "Manda il microfono di qui al PC remoto (per parlare nei suoi programmi, anche con la radio)");

            _fitRes.IsVisible = false;
            _fitRes.IsCheckedChanged += (_, _) =>
            {
                if (_peer.S.FitResolution != (_fitRes.IsChecked == true)) { _peer.S.FitResolution = _fitRes.IsChecked == true; _peer.S.Save(); }
                if (_fitRes.IsChecked == true) SendResize();
                else { _lastResize = (0, 0); _session?.RequestResize(0, 0); }
            };
            ToolTip.SetTip(_fitRes, "Il PC remoto passa alla risoluzione più adatta a questa finestra (a fine collegamento torna com'era)");

            _codec.ItemsSource = CodecLabels;
            _codec.SelectedIndex = Math.Max(0, Array.IndexOf(CodecKeys, _peer.S.VideoCodec));
            _codec.IsVisible = false;
            _codec.SelectionChanged += (_, _) =>
            {
                if (_codec.SelectedIndex < 0) return;
                _peer.S.VideoCodec = CodecKeys[_codec.SelectedIndex];
                _peer.S.Save();
                _session?.SetCodec(_peer.S.VideoCodec);
            };
            ToolTip.SetTip(_codec, "Video compresso (H.264, H.265, AV1 con ffmpeg, meglio se con la scheda video) oppure riquadri JPEG. Automatico: il migliore che hanno tutti e due i PC.");

            _tunnelBtn.IsVisible = false;
            _tunnelBtn.Click += (_, _) => ShowTunnels();
            ToolTip.SetTip(_tunnelBtn, "Tunnel di porte: rigctld, flrig, l'interfaccia web della radio… del PC remoto raggiungibili da qui");

            var gestures = Small("✋", "Tocco e gesti");
            gestures.IsVisible = Platform.IsMobile;
            gestures.Click += (_, _) => TouchDialog.Show(_peer.S, RebuildKeysBar);

            var keys = new Button { Content = "Tasti ▾" };
            var menu = new MenuFlyout();
            void Item(string text, params int[] combo)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, _) => { _session?.Combo(combo); _view.Focus(); };
                menu.Items.Add(mi);
            }
            Item("Ctrl+Alt+Canc", KeyMap.LeftCtrl, KeyMap.LeftAlt, KeyMap.Delete);
            Item("Alt+Tab", KeyMap.LeftAlt, KeyMap.Tab);
            Item("Tasto Windows / Cmd", KeyMap.LeftMeta);
            Item("Alt+F4", KeyMap.LeftAlt, KeyMap.F4);
            Item("Ctrl+C", KeyMap.LeftCtrl, 46);
            Item("Ctrl+V", KeyMap.LeftCtrl, 47);
            Item("Stamp", KeyMap.PrintScreen);
            Item("Esc", KeyMap.Escape);
            menu.Items.Add(new Separator());
            void Vol(string text, int action)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, _) => { _session?.Volume(action); _view.Focus(); };
                menu.Items.Add(mi);
            }
            Vol("Volume del PC remoto +", VolumeControl.Up);
            Vol("Volume del PC remoto −", VolumeControl.Down);
            Vol("Volume del PC remoto: muto sì/no", VolumeControl.Mute);
            var panelItem = new MenuItem { Header = "Pannello scorciatoie" };
            panelItem.Click += (_, _) => OpenPanel(false);
            menu.Items.Add(panelItem);
            menu.Items.Add(new Separator());
            var reboot = new MenuItem { Header = "Riavvia il PC remoto…" };
            reboot.Click += (_, _) => AskReboot();
            menu.Items.Add(reboot);
            keys.Flyout = menu;
            ToolTip.SetTip(keys, "Combinazioni che il tuo sistema non lascerebbe passare. Ctrl+Alt+Canc su Windows richiede il servizio «accesso completo» sul PC remoto.");

            _files.Click += (_, _) => ShowFilesMenu();
            ToolTip.SetTip(_files, "Manda file al PC remoto o prendi quelli copiati lì. Puoi anche trascinarli sulla finestra.");

            var keyboard = new Button { Content = "Tastiera" };
            keyboard.Click += (_, _) => ToggleKeyboard();
            keyboard.IsVisible = Platform.IsMobile;

            _radio.IsVisible = false;
            ToolTip.SetTip(_radio, "Avvia IP-TRX-CTRL sul PC remoto e, se c'è, anche su questo");
            _radio.Click += (_, _) =>
            {
                var s = _session;
                if (s == null) return;
                s.StartTrx();
                if (Platform.IsMobile || !TrxCtrl.Installed) ShowNote("Chiesto l'avvio di IP-TRX-CTRL sul PC remoto.");
                else
                {
                    string err = TrxCtrl.Launch();
                    ShowNote(err.Length == 0 ? "IP-TRX-CTRL avviato sul PC remoto e su questo." : "IP-TRX-CTRL chiesto sul PC remoto; su questo: " + err);
                }
            };

            var refresh = Small("⟳", "Aggiorna l'immagine");
            refresh.Click += (_, _) => _session?.Refresh();
            var close = new Button { Content = "Disconnetti" };
            close.Classes.Add("accent");
            close.Click += (_, _) => Disconnect();
            var hide = Small("▲", "Nascondi la plancia");
            hide.Click += (_, _) => { _toolbarPinned = false; ShowToolbar(false); };

            var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in new Control[] { _fullBtn, minus, _zoom, plus, _monitor, _quality, _codec, _fitRes, _audio, _mic, keyboard, gestures, keys, _files, _tunnelBtn, _radio, refresh, _stats, close, hide })
            {
                c.VerticalAlignment = VerticalAlignment.Center;
                c.Margin = new Thickness(3);
                bar.Children.Add(c);
            }
            _stats.IsVisible = !Platform.IsMobile;
            _toolbar.Child = bar;
            _toolbar.Background = new SolidColorBrush(Color.FromArgb(225, 24, 28, 36));
            _toolbar.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 80, 96));
            _toolbar.BorderThickness = new Thickness(1, 0, 1, 1);
            _toolbar.CornerRadius = new CornerRadius(0, 0, 12, 12);
            _toolbar.Padding = new Thickness(8, 4);
            _toolbar.HorizontalAlignment = HorizontalAlignment.Center;
            _toolbar.VerticalAlignment = VerticalAlignment.Top;
            _toolbar.PointerEntered += (_, _) => _hideToolbarAt = DateTime.MaxValue;
            _toolbar.PointerExited += (_, _) => { if (!_toolbarPinned || Shell.Current.IsFullScreen(this)) _hideToolbarAt = DateTime.UtcNow.AddSeconds(1.5); };
            grid.Children.Add(_toolbar);

            // linguetta quando la plancia è nascosta
            _tab.Child = new TextBlock { Text = "▼  " + Identity.Format(_number), Foreground = Brushes.White, FontSize = 12 };
            _tab.Background = new SolidColorBrush(Color.FromArgb(200, 24, 28, 36));
            _tab.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _tab.Padding = new Thickness(14, Platform.IsMobile ? 8 : 2, 14, Platform.IsMobile ? 8 : 4);
            _tab.HorizontalAlignment = HorizontalAlignment.Center;
            _tab.VerticalAlignment = VerticalAlignment.Top;
            _tab.IsVisible = false;
            _tab.Cursor = new Cursor(StandardCursorType.Hand);
            _tab.PointerEntered += (_, _) => { if (!Platform.IsMobile) ShowToolbar(true); };
            _tab.PointerPressed += (_, _) => { _toolbarPinned = true; ShowToolbar(true); };
            grid.Children.Add(_tab);

            // ---- barra dei tasti speciali sopra la tastiera del telefono
            grid.Children.Add(_kbd);
            BuildKeysBar();
            grid.Children.Add(_keysBar);

            // ---- pannello scorciatoie
            BuildPanel();
            grid.Children.Add(_panel);

            // ---- trasferimento di file
            _transfer.Child = new StackPanel { Spacing = 4, Children = { _transferText, _transferBar } };
            _transfer.Background = new SolidColorBrush(Color.FromArgb(220, 24, 28, 36));
            _transfer.CornerRadius = new CornerRadius(8);
            _transfer.Padding = new Thickness(12, 8);
            _transfer.Margin = new Thickness(16);
            _transfer.HorizontalAlignment = HorizontalAlignment.Left;
            _transfer.VerticalAlignment = VerticalAlignment.Bottom;
            grid.Children.Add(_transfer);

            // ---- avvisi del PC remoto (permessi, Wayland…) e file ricevuti
            _noteOpen.Click += (_, _) => MainView.OpenPath(Platform.Received);
            _note.Child = new StackPanel { Children = { _noteText, _noteOpen } };
            _note.Background = new SolidColorBrush(Color.FromArgb(220, 120, 60, 0));
            _note.CornerRadius = new CornerRadius(8);
            _note.Padding = new Thickness(12, 8);
            _note.Margin = new Thickness(0, 0, 0, 16);
            _note.HorizontalAlignment = HorizontalAlignment.Center;
            _note.VerticalAlignment = VerticalAlignment.Bottom;
            _note.PointerPressed += (_, _) => _note.IsVisible = false;
            grid.Children.Add(_note);

            // ---- pannello di collegamento / errori
            _overlay.Child = new Border
            {
                Background = Themes.B(Themes.Current.Panel),
                BorderBrush = Themes.B(Themes.Current.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(28),
                Margin = new Thickness(12),
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel { Spacing = 14, Children = { _overlayTitle, _overlayText, _overlayButtons } }
            };
            grid.Children.Add(_overlay);
            return grid;
        }

        private static Button Small(string text, string tip)
        {
            var b = new Button { Content = text, MinWidth = 38, Padding = new Thickness(8, 4) };
            ToolTip.SetTip(b, tip);
            return b;
        }

        private void ShowNote(string text, bool openFolder = false)
        {
            _noteText.Text = text;
            _noteOpen.IsVisible = openFolder && !Platform.IsMobile;
            _note.IsVisible = text.Length > 0;
        }

        // =================================================================== zoom e schermo intero

        private void SetZoom(int z, bool fromCombo = false)
        {
            _view.Zoom = z;
            _viewHost.Children.Clear();
            _scroll.Content = null;
            if (z == 0) _viewHost.Children.Add(_view);
            else { _scroll.Content = _view; _viewHost.Children.Add(_scroll); }
            int i = Array.IndexOf(Zooms, z);
            if (!fromCombo && i >= 0 && _zoom.SelectedIndex != i) _zoom.SelectedIndex = i;
            if (_peer.S.ZoomPercent != z) { _peer.S.ZoomPercent = z; _peer.S.Save(); }
            _view.Focus();
        }

        private void StepZoom(int dir)
        {
            int cur = _view.Zoom;
            if (cur == 0)
            {
                // da «adatta» si parte dalla percentuale più vicina a quella mostrata
                var img = _view.ImageSize;
                double eff = img.Width > 0 ? Math.Min(_viewHost.Bounds.Width / img.Width, _viewHost.Bounds.Height / img.Height) * 100 : 100;
                cur = Zooms.Skip(1).OrderBy(z => Math.Abs(z - eff)).First();
                SetZoom(cur);
                return;
            }
            int i = Array.IndexOf(Zooms, cur);
            int next = Math.Clamp(i + dir, 1, Zooms.Length - 1);
            SetZoom(Zooms[next]);
        }

        private void ToggleFullScreen()
        {
            bool on = !Shell.Current.IsFullScreen(this);
            Shell.Current.SetFullScreen(this, on);
            _fullBtn.Content = on ? "Finestra" : "Schermo intero";
            if (on) { ShowToolbar(true); _hideToolbarAt = DateTime.UtcNow.AddSeconds(3); }
            else { _toolbarPinned = true; ShowToolbar(true); }
            _view.Focus();
        }

        private void ShowToolbar(bool show)
        {
            _toolbar.IsVisible = show;
            _tab.IsVisible = !show;
            if (show && (!_toolbarPinned || Shell.Current.IsFullScreen(this))) _hideToolbarAt = DateTime.UtcNow.AddSeconds(Platform.IsMobile ? 5 : 3);
            else if (show) _hideToolbarAt = DateTime.MaxValue;
        }

        private void Tick()
        {
            if (_toolbar.IsVisible && DateTime.UtcNow > _hideToolbarAt && !_toolbar.IsPointerOver)
            {
                _hideToolbarAt = DateTime.MaxValue;
                ShowToolbar(false);
            }
            var s = _session;
            if (s != null && s.State == AcceptState.Accepted && !s.IsClosed)
            {
                string rtt = double.IsNaN(s.RttMs) ? "—" : $"{s.RttMs:F0} ms";
                var img = _view.ImageSize;
                _stats.Text = $"{s.Fps:F0} fps · {s.KbitPerSecond / 1000:F1} Mbit/s · {rtt} · {s.Route}\n{img.Width}×{img.Height} · {(s.VideoInfo.Length > 0 ? s.VideoInfo : "riquadri")}" + (s.InputAllowed ? "" : " · solo visione");
                int tc = s.ActiveTunnels.Count;
                _tunnelBtn.Content = tc > 0 ? $"Tunnel ▾ ({tc})" : "Tunnel ▾";
                var rf = s.RemoteFiles;
                _files.Content = rf.count > 0 ? $"File ▾ ({rf.count})" : "File ▾";
            }
        }

        // =================================================================== file

        private void ShowFilesMenu()
        {
            var s = _session;
            if (s == null || s.State != AcceptState.Accepted) return;
            var menu = new MenuFlyout();
            void Add(string text, bool enabled, Action a)
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled };
                mi.Click += (_, _) => a();
                menu.Items.Add(mi);
            }
            var rf = s.RemoteFiles;
            Add(rf.count > 0
                    ? $"Prendi qui i file copiati sul PC remoto ({rf.count}{(rf.count == 1 ? ": " + rf.first : "")}, {Size(rf.bytes)})"
                    : "Prendi qui i file copiati sul PC remoto (copiali prima con Ctrl+C)",
                rf.count > 0 && s.InputAllowed, () => s.FetchRemoteFiles());
            if (!Platform.IsMobile)
                Add("Manda i file copiati su questo PC (incollali di là con Ctrl+V)", s.InputAllowed, () => _ = SendLocalClipboardAsync());
            Add("Scegli file da mandare…", s.InputAllowed, () => _ = PickAndSendAsync());
            if (!Platform.IsMobile) Add("Apri la cartella dei file ricevuti", true, () => MainView.OpenPath(Platform.Received));
            if (s.PeerHas("drive"))
            {
                menu.Items.Add(new Separator());
                if (s.SharedFolder.Length > 0)
                    Add($"Togli la cartella condivisa ({System.IO.Path.GetFileName(s.SharedFolder.TrimEnd('/', '\\'))}{(s.DriveMounted.Length > 0 ? " → " + s.DriveMounted : "")})", true, () => s.StopSharing());
                else
                {
                    Add("Condividi una cartella come disco del PC remoto…", s.InputAllowed, () => _ = ShareFolderAsync(false));
                    Add("Condividi una cartella in sola lettura…", s.InputAllowed, () => _ = ShareFolderAsync(true));
                }
            }
            if (!s.InputAllowed) Add("(solo visione: i file non sono permessi)", false, () => { });
            menu.ShowAt(_files);
        }

        private static string Size(long b) => b >= 1 << 30 ? $"{b / (double)(1 << 30):F1} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):F1} MB" : $"{Math.Max(1, b / 1024)} kB";

        private async Task SendLocalClipboardAsync()
        {
            var s = _session;
            var clip = Platform.Clipboard;
            if (s == null || clip == null) return;
            var files = await clip.GetFilesAsync();
            if (files.Count == 0) { ShowNote("Negli appunti di questo PC non ci sono file: copiali prima (Ctrl+C in Esplora file o nel Finder)."); return; }
            await Task.Run(() => s.SendFilesAsync(SessionBase.FromPaths(files), TransferPurpose.Paste));
        }

        private async Task PickAndSendAsync()
        {
            var s = _session;
            var top = TopLevel.GetTopLevel(this);
            if (s == null || top == null) return;
            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "File da mandare al PC remoto" });
            if (picked.Count == 0) return;
            var items = new List<TransferItem>();
            foreach (var f in picked)
            {
                long size = 0;
                try { size = (long)((await f.GetBasicPropertiesAsync()).Size ?? 0); } catch { }
                var file = f;
                items.Add(new TransferItem(f.Name, size, async () => await file.OpenReadAsync()));
            }
            await Task.Run(() => s.SendFilesAsync(items, TransferPurpose.Paste));
        }

        private void OnDrop(DragEventArgs e)
        {
            var s = _session;
            if (s == null || s.State != AcceptState.Accepted || !s.InputAllowed) return;
            var paths = (e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>()).Select(i => i.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
            if (paths.Count == 0) return;
            // il puntatore del PC remoto va dove sono stati lasciati i file: lì verranno incollati
            var p = e.GetPosition(_view);
            var img = _view.ImagePoint(p);
            if (img != null) s.MouseMove(img.Value.x, img.Value.y);
            _ = Task.Run(() => s.SendFilesAsync(SessionBase.FromPaths(paths), TransferPurpose.Drop));
        }

        private void OnTransfer(TransferProgress t)
        {
            _transfer.IsVisible = true;
            _transferText.Text = t.Message;
            _transferBar.Value = t.Total > 0 ? t.Done * 100.0 / t.Total : (t.Finished ? 100 : 0);
            if (t.Finished)
            {
                var stop = DateTime.UtcNow.AddSeconds(6);
                DispatcherTimer.RunOnce(() => { if (DateTime.UtcNow >= stop.AddSeconds(-0.5)) _transfer.IsVisible = false; }, TimeSpan.FromSeconds(6));
            }
        }

        // =================================================================== tastiera del telefono

        private void BuildKeysBar()
        {
            RebuildKeysBar();
            _keysBar.Background = new SolidColorBrush(Color.FromArgb(235, 24, 28, 36));
            _keysBar.VerticalAlignment = VerticalAlignment.Bottom;
            _keysBar.Padding = new Thickness(4);

            _kbd.TextChanged += (_, _) => OnKbdText();
            _kbd.KeyDown += (_, e) =>
            {
                var s = _session;
                if (s == null) return;
                if (e.Key == Key.Enter) { Tap(28); e.Handled = true; }
                else if (e.Key == Key.Back && (_kbd.Text ?? "").Length <= Sentinel.Length) { Tap(14); e.Handled = true; }
            };
        }

        /// <summary>Barra dei tasti sopra la tastiera del telefono, con i tasti scelti in «Tocco e gesti».</summary>
        private void RebuildKeysBar()
        {
            var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (string id in _peer.S.TouchInput.Keys)
            {
                if (!SpecialKeys.ById.TryGetValue(id, out var k)) continue;
                if (k.Kind == "mod")
                {
                    var t = id switch { "ctrl" => _ctrl, "alt" => _alt, "win" => _win, _ => _shift };
                    t.Margin = new Thickness(2);
                    if (t.Parent is Panel old) old.Children.Remove(t);
                    bar.Children.Add(t);
                    continue;
                }
                if (k.Kind == "dict" && Platform.Dictate == null) continue;
                Button b = k.Repeat ? new RepeatButton { Delay = 400, Interval = 70 } : new Button();
                b.Content = k.Label;
                b.MinWidth = 44;
                b.Margin = new Thickness(2);
                var key = k;
                b.Click += (_, _) => { SendSpecial(key); _kbd.Focus(); };
                bar.Children.Add(b);
            }
            var done = new Button { Content = "Chiudi tastiera", Margin = new Thickness(2) };
            done.Click += (_, _) => ToggleKeyboard();
            bar.Children.Add(done);
            _keysBar.Child = bar;
        }

        /// <summary>Un tasto speciale (barra o pannello scorciatoie), con i modificatori attivi.</summary>
        private void SendSpecial(SpecialKey k)
        {
            var s = _session;
            if (s == null) return;
            switch (k.Kind)
            {
                case "vol+": s.Volume(VolumeControl.Up); return;
                case "vol-": s.Volume(VolumeControl.Down); return;
                case "mute": s.Volume(VolumeControl.Mute); return;
                case "dict": _ = DictateAsync(); return;
                case "combo": s.Combo(k.Keys); return;
                case "mod": return;
            }
            WithMods(() => s.Combo(k.Keys));
        }

        // =================================================================== gesti, pannello scorciatoie, dettatura

        private void OnTouchAction(string action)
        {
            switch (action)
            {
                case TouchActions.Keyboard: ToggleKeyboard(); break;
                case TouchActions.Dictation: _ = DictateAsync(); break;
                case TouchActions.Shortcuts: OpenPanel(false); break;
            }
        }

        private async Task DictateAsync()
        {
            var s = _session;
            if (s == null) return;
            if (Platform.Dictate == null) { ShowNote("La dettatura c'è solo sul telefono: qui usa la tastiera."); return; }
            string? text = await Platform.Dictate();
            if (string.IsNullOrWhiteSpace(text)) { ShowNote("Dettatura annullata o nessun servizio di riconoscimento vocale sul telefono."); return; }
            s.Text(text);
            _view.Focus();
        }

        private void BuildPanel()
        {
            foreach (string id in SpecialKeys.Panel)
            {
                var k = SpecialKeys.ById[id];
                var b = new Button { Content = k.Label, MinWidth = 64, MinHeight = 48 };
                b.Click += (_, _) => { _panelLast = _panelKeys.Children.IndexOf(b); SendSpecial(k); ClosePanel(); };
                _panelKeys.Children.Add(b);
            }
            var close = new Button { Content = "✕", MinWidth = 48, MinHeight = 48 };
            close.Click += (_, _) => ClosePanel();
            _panelScroll.Content = _panelKeys;
            _panel.Child = new DockPanel { Children = { close, _panelScroll } };
            DockPanel.SetDock(close, Dock.Right);
            _panel.Background = new SolidColorBrush(Color.FromArgb(235, 24, 28, 36));
            _panel.CornerRadius = new CornerRadius(12);
            _panel.Padding = new Thickness(6);
            _panel.Margin = new Thickness(12, 0, 12, 24);
            _panel.HorizontalAlignment = HorizontalAlignment.Center;
            _panel.VerticalAlignment = VerticalAlignment.Bottom;
        }

        private void OpenPanel(bool hold)
        {
            _panelHold = hold;
            _panelCancel = false;
            _panelStart = _panelIndex = Math.Clamp(_panelLast, 0, SpecialKeys.Panel.Length - 1);
            _panel.IsVisible = true;
            Highlight();
        }

        private void PanelMove(double dx, double dy)
        {
            if (!_panelHold) return;
            _panelCancel = Math.Abs(dy) > 96;
            _panelIndex = Math.Clamp(_panelStart + (int)Math.Round(dx / 48), 0, SpecialKeys.Panel.Length - 1);
            Highlight();
        }

        private void PanelRelease()
        {
            if (!_panelHold) return;
            _panelHold = false;
            if (_panelCancel) { ClosePanel(); return; }
            if (_panelIndex == _panelStart) { Highlight(); return; }      // sollevato senza scorrere: resta aperto per i tocchi
            _panelLast = _panelIndex;
            SendSpecial(SpecialKeys.ById[SpecialKeys.Panel[_panelIndex]]);
            ClosePanel();
        }

        private void Highlight()
        {
            for (int i = 0; i < _panelKeys.Children.Count; i++)
            {
                var c = _panelKeys.Children[i];
                bool on = i == _panelIndex && (_panelHold || i == _panelLast);
                if (on && !c.Classes.Contains("accent")) c.Classes.Add("accent");
                if (!on) c.Classes.Remove("accent");
                if (on) c.BringIntoView();
            }
            _panel.Opacity = _panelCancel ? 0.5 : 1;
        }

        private void ClosePanel()
        {
            _panel.IsVisible = false;
            _panelHold = false;
            _view.Focus();
        }

        // =================================================================== microfono, risoluzione, cartella condivisa

        private void ToggleMic()
        {
            var s = _session;
            if (s == null) return;
            bool on = _mic.IsChecked == true;
            if (on == s.MicOn) return;
            string err = s.SetMic(on);
            if (err.Length > 0) { ShowNote(err); _mic.IsChecked = false; return; }
            if (_peer.S.SendMic != on) { _peer.S.SendMic = on; _peer.S.Save(); }
        }

        /// <summary>Dimensione della vista in pixel veri → al PC remoto (dopo un attimo di pausa nel ridimensionare).</summary>
        private void SendResize()
        {
            var s = _session;
            if (s == null || s.State != AcceptState.Accepted || _fitRes.IsChecked != true) return;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int w = (int)Math.Round(_viewHost.Bounds.Width * scale), h = (int)Math.Round(_viewHost.Bounds.Height * scale);
            if (w < 200 || h < 200 || (w, h) == _lastResize) return;
            _lastResize = (w, h);
            s.RequestResize(w, h);
        }

        // =================================================================== tunnel di porte

        private HistoryEntry? Entry => _peer.S.Find(_number);

        private List<TunnelSpec> SavedTunnels() =>
            (Entry?.Tunnels ?? new List<string>()).Select(TunnelSpec.Parse).Where(t => t != null).Cast<TunnelSpec>().ToList();

        private void SaveTunnels(List<TunnelSpec> list)
        {
            var e = Entry;
            if (e == null) return;
            e.Tunnels = list.Select(t => t.ToString()).ToList();
            _peer.S.Save();
        }

        private void StartSavedTunnels(ViewerSession s)
        {
            var e = Entry;
            if (e == null || !e.TunnelsAuto) return;
            var errors = new List<string>();
            foreach (var t in SavedTunnels())
            {
                string err = s.StartTunnel(t);
                if (err.Length > 0) errors.Add(err);
            }
            if (errors.Count > 0) ShowNote(string.Join("\n", errors));
        }

        private void ShowTunnels()
        {
            var s = _session;
            if (s == null) return;
            var list = new StackPanel { Spacing = 6 };
            void Refresh()
            {
                list.Children.Clear();
                var saved = SavedTunnels();
                var active = s.ActiveTunnels;
                if (saved.Count == 0) list.Children.Add(Ui.Sub("Nessun tunnel. Aggiungine uno qui sotto: un programma di qui che si collega a 127.0.0.1:porta locale raggiunge l'indirizzo indicato, visto dal PC remoto."));
                foreach (var t in saved)
                {
                    bool on = active.Any(a => a.LocalPort == t.LocalPort);
                    var onOff = Ui.Btn(on ? "Ferma" : "Avvia");
                    var spec = t;
                    onOff.Click += (_, _) =>
                    {
                        if (s.ActiveTunnels.Any(a => a.LocalPort == spec.LocalPort)) s.StopTunnel(spec.LocalPort);
                        else { string err = s.StartTunnel(spec); if (err.Length > 0) ShowNote(err); }
                        Refresh();
                    };
                    var del = Ui.Btn("Togli");
                    del.Click += (_, _) =>
                    {
                        s.StopTunnel(spec.LocalPort);
                        SaveTunnels(SavedTunnels().Where(x => x.LocalPort != spec.LocalPort).ToList());
                        Refresh();
                    };
                    var btns = Ui.Row(6, onOff, del);
                    DockPanel.SetDock(btns, Dock.Right);
                    var label = new TextBlock
                    {
                        Text = (on ? "● " : "○ ") + $"127.0.0.1:{t.LocalPort} → {t.Host}:{t.Port}" + (t.Name.Length > 0 ? $"  ({t.Name})" : ""),
                        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0)
                    };
                    list.Children.Add(new DockPanel { Children = { btns, label } });
                }
            }
            Refresh();

            var preset = new ComboBox { ItemsSource = new[] { "Scegli un modello…" }.Concat(TunnelSpec.Presets.Select(p => $"{p.Name}: {p.Host}:{p.Port}")).ToList(), SelectedIndex = 0, MinWidth = 260 };
            var local = new TextBox { Watermark = "Porta di qui", Width = 110 };
            var host = new TextBox { Watermark = "Indirizzo dal PC remoto", Text = "127.0.0.1", Width = 190 };
            var port = new TextBox { Watermark = "Porta", Width = 90 };
            var name = new TextBox { Watermark = "Nome (facoltativo)", Width = 190 };
            preset.SelectionChanged += (_, _) =>
            {
                if (preset.SelectedIndex <= 0) return;
                var p = TunnelSpec.Presets[preset.SelectedIndex - 1];
                local.Text = p.LocalPort.ToString(); host.Text = p.Host; port.Text = p.Port.ToString(); name.Text = p.Name;
            };
            var add = Ui.Btn("Aggiungi e avvia", true);
            add.Click += (_, _) =>
            {
                if (!int.TryParse(local.Text, out int lp) || lp is <= 0 or > 65535 || !int.TryParse(port.Text, out int rp) || rp is <= 0 or > 65535 || string.IsNullOrWhiteSpace(host.Text))
                { ShowNote("Scrivi la porta di qui, l'indirizzo e la porta (1–65535)."); return; }
                var spec = new TunnelSpec(lp, host.Text!.Trim(), rp, (name.Text ?? "").Trim().Replace("|", " "));
                var saved = SavedTunnels().Where(x => x.LocalPort != lp).ToList();
                saved.Add(spec);
                SaveTunnels(saved);
                string err = s.StartTunnel(spec);
                if (err.Length > 0) ShowNote(err);
                Refresh();
            };
            var auto = new CheckBox { Content = "Avvia questi tunnel da soli a ogni collegamento con questo PC", IsChecked = Entry?.TunnelsAuto ?? true };
            auto.IsCheckedChanged += (_, _) => { var e = Entry; if (e != null) { e.TunnelsAuto = auto.IsChecked == true; _peer.S.Save(); } };
            var close = Ui.Btn("Chiudi");
            var content = new ScrollViewer
            {
                MaxHeight = 620,
                Content = Ui.Col(10,
                    Ui.H1("Tunnel di porte"),
                    Ui.Sub("Per la radio: un programma di qui (WSJT-X, log, rotore…) si collega a 127.0.0.1:porta e arriva a rigctld, flrig o all'interfaccia web della radio del PC remoto. Tutto passa nel collegamento cifrato.", 12),
                    list,
                    Ui.Lbl("NUOVO TUNNEL"),
                    preset,
                    new WrapPanel { Children = { local, new TextBlock { Text = " → ", VerticalAlignment = VerticalAlignment.Center }, host, new TextBlock { Text = " : ", VerticalAlignment = VerticalAlignment.Center }, port } },
                    name,
                    auto,
                    Ui.Row(10, add, close))
            };
            var h = Shell.Current.ShowDialog("IP-DESK-CTRL · tunnel di porte", content, 620, false);
            close.Click += (_, _) => h.Close();
        }

        private async Task ShareFolderAsync(bool readOnly)
        {
            var s = _session;
            var top = TopLevel.GetTopLevel(this);
            if (s == null || top == null) return;
            string? path = null;
            if (Platform.IsMobile) path = Platform.Received;       // su Android: la cartella dei file ricevuti
            else
            {
                var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Cartella da far vedere al PC remoto come disco", AllowMultiple = false });
                path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
            }
            if (string.IsNullOrEmpty(path)) return;
            _peer.S.SharedFolder = path;
            _peer.S.SharedReadOnly = readOnly;
            _peer.S.Save();
            s.ShareFolder(path, readOnly);
            ShowNote("Monto la cartella sul PC remoto…");
        }

        private void ToggleKeyboard()
        {
            bool show = !_keysBar.IsVisible;
            _keysBar.IsVisible = show;
            if (show) { _kbd.Text = Sentinel; _kbd.CaretIndex = Sentinel.Length; _kbd.Focus(); }
            else _view.Focus();
        }

        private bool AnyMod => _ctrl.IsChecked == true || _alt.IsChecked == true || _win.IsChecked == true;

        /// <summary>Preme i modificatori attivi intorno all'azione, poi li spegne (tasti «a una botta»).</summary>
        private void WithMods(Action a)
        {
            var s = _session;
            if (s == null) return;
            var mods = new List<int>();
            if (_ctrl.IsChecked == true) mods.Add(KeyMap.LeftCtrl);
            if (_alt.IsChecked == true) mods.Add(KeyMap.LeftAlt);
            if (_win.IsChecked == true) mods.Add(KeyMap.LeftMeta);
            if (_shift.IsChecked == true) mods.Add(KeyMap.LeftShift);
            foreach (int m in mods) s.Key(m, true);
            a();
            for (int i = mods.Count - 1; i >= 0; i--) s.Key(mods[i], false);
            _ctrl.IsChecked = _alt.IsChecked = _win.IsChecked = _shift.IsChecked = false;
        }

        /// <summary>
        /// La tastiera del telefono manda testo, non tasti: il campo nascosto parte con due spazi; quello che
        /// si aggiunge va all'altro PC come testo, quello che si cancella diventa Backspace.
        /// </summary>
        private void OnKbdText()
        {
            if (_kbdBusy) return;
            var s = _session;
            string t = _kbd.Text ?? "";
            string added = "";
            int deleted = 0;
            if (t.StartsWith(Sentinel)) added = t[Sentinel.Length..];
            else if (t.Length < Sentinel.Length) deleted = Sentinel.Length - t.Length;
            else added = t.TrimStart();
            if (s != null)
            {
                for (int i = 0; i < deleted; i++) { s.Key(14, true); s.Key(14, false); }
                if (added.Length > 0)
                {
                    if (AnyMod || _shift.IsChecked == true)
                    {
                        // con un modificatore si mandano tasti (Ctrl+C, Alt+F…), non testo
                        foreach (char ch in added)
                        {
                            var probe = new KeyProbe();
                            AsciiKeys.Type(probe, ch.ToString().ToLowerInvariant());
                            if (probe.Key > 0) WithMods(() => { s.Key(probe.Key, true); s.Key(probe.Key, false); });
                        }
                    }
                    else s.Text(added);
                }
            }
            _kbdBusy = true;
            _kbd.Text = Sentinel;
            _kbd.CaretIndex = Sentinel.Length;
            _kbdBusy = false;
        }

        /// <summary>Raccoglie il primo tasto che <see cref="AsciiKeys"/> premerebbe (per lettere e cifre con un modificatore).</summary>
        private sealed class KeyProbe : IInputSink
        {
            public int Key;
            public void MoveTo(int x, int y) { }
            public void Button(int button, bool down) { }
            public void Wheel(int dx, int dy) { }
            void IInputSink.Key(int evdev, bool down) { if (down && Key == 0 && evdev != KeyMap.LeftShift) Key = evdev; }
            public void Text(string text) { }
            public string Note => "";
            public void Dispose() { }
        }

        // =================================================================== collegamento

        private void Overlay(string title, string text, params (string label, bool accent, Action action)[] buttons)
        {
            _overlayTitle.Text = title;
            _overlayText.Text = text;
            _overlayButtons.Children.Clear();
            foreach (var (label, accent, action) in buttons)
            {
                var b = Ui.Btn(label, accent);
                b.MinWidth = 110;
                b.Click += (_, _) => action();
                _overlayButtons.Children.Add(b);
            }
            _overlay.IsVisible = true;
        }

        /// <param name="quiet">true = tentativo dopo il riavvio: in caso di errore non mostra nulla e restituisce false</param>
        private async Task<bool> ConnectAsync(bool quiet = false)
        {
            if (!quiet) _rebootCts?.Cancel();
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string who = (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number);
            if (quiet) _overlayText.Text = "Il PC remoto è tornato in linea: mi ricollego…";
            else Overlay("Collegamento a " + who, "Avvio…", ("Annulla", false, Disconnect));
            try
            {
                var link = await _peer.ConnectAsync(_number, msg => Dispatcher.UIThread.Post(() => { if (!ct.IsCancellationRequested) _overlayText.Text = msg; }), ct);
                if (ct.IsCancellationRequested) { link.Dispose(); return false; }
                var s = new ViewerSession(_peer, link);
                _session = s;
                _view.Session = s;
                s.StateChanged += () => Dispatcher.UIThread.Post(() => OnState(s));
                s.PasswordRequested += c => Dispatcher.UIThread.Post(() => AskPassword(s, c));
                s.MonitorsChanged += () => Dispatcher.UIThread.Post(() => OnMonitors(s));
                s.FrameReady += () =>
                {
                    if (Interlocked.Exchange(ref _framePosted, 1) == 1) return;
                    Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _framePosted, 0); _view.UpdateImage(); }, DispatcherPriority.Render);
                };
                s.CursorMoved += () => Dispatcher.UIThread.Post(() => _view.InvalidateVisual());
                s.NoteChanged += () => Dispatcher.UIThread.Post(() => ShowNote(s.Note));
                s.TransferChanged += t => Dispatcher.UIThread.Post(() => OnTransfer(t));
                s.FilesReceived += (folder, items, onClip) => Dispatcher.UIThread.Post(() =>
                    ShowNote((items.Count == 1 ? "1 elemento ricevuto" : $"{items.Count} elementi ricevuti") + $" in {folder}" +
                             (onClip ? (Platform.IsMobile ? "." : ": sono negli appunti, incollali con Ctrl+V dove vuoi.") : "."), true));
                s.TunnelMessage += m => Dispatcher.UIThread.Post(() => ShowNote(m));
                s.DriveChanged += (on, where) => Dispatcher.UIThread.Post(() =>
                    ShowNote(on ? $"Cartella condivisa: sul PC remoto è il disco {where}."
                        : where.Length > 0 ? "Cartella non montata sul PC remoto: " + where + "." : "Cartella condivisa tolta."));
                s.Closed += reason => Dispatcher.UIThread.Post(() => OnClosed(s, reason));
                _overlayText.Text = "Collegato con cifratura AES-256. Attendo che l'altro PC risponda…";
                s.Run(ct);
                return true;
            }
            catch (OperationCanceledException) { }
            catch (PeerException ex) { if (!quiet) Failed(ex.Message); }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                if (!quiet) Failed("Collegamento non riuscito: " + ex.Message);
            }
            return false;
        }

        // ------------------------------------------------------------------ password dell'accesso senza conferma

        private void AskPassword(ViewerSession s, PasswordChallenge c)
        {
            if (s != _session) return;
            var box = new TextBox { PasswordChar = '•', Watermark = "Password", MinWidth = 240 };
            var remember = new CheckBox { Content = "Ricorda per questo PC", IsChecked = true };
            void Submit()
            {
                string pw = box.Text ?? "";
                if (pw.Length == 0) return;
                _overlayButtons.Children.Clear();
                _overlayText.Text = "Controllo la password…";
                _ = s.AnswerPasswordAsync(c, pw, remember.IsChecked == true);
            }
            Overlay("Password richiesta",
                c.Message.Length > 0 ? c.Message : "Questo PC chiede una password per entrare senza conferma. Con «Chiedi a chi è al PC» decide invece la persona davanti al PC remoto.",
                ("Chiedi a chi è al PC", false, () => { _overlayButtons.Children.Clear(); _overlayText.Text = "Attendo…"; _ = s.AnswerPasswordAsync(c, null, false); }),
                ("Entra", true, Submit));
            _overlayButtons.Children.Insert(0, new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { box, remember } });
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
            box.AttachedToVisualTree += (_, _) => box.Focus();
        }

        // ------------------------------------------------------------------ riavvio del PC remoto

        private void AskReboot()
        {
            var s = _session;
            if (s == null || s.State != AcceptState.Accepted) return;
            string who = (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number);
            Overlay("Riavviare il PC remoto?",
                $"{who} verrà riavviato: i programmi aperti lì si chiudono senza salvare. Appena il PC torna in linea mi ricollego da solo.",
                ("Riavvia", true, () =>
                {
                    _overlay.IsVisible = false;
                    s.Reboot();
                    ShowNote("Riavvio chiesto: aspetto che il PC remoto si spenga…");
                }),
                ("Annulla", false, () => { _overlay.IsVisible = false; _view.Focus(); }));
        }

        /// <summary>Dopo il riavvio: controlla sui broker ogni 15 s (per 10 minuti) e si ricollega appena il PC ripubblica l'indirizzo.</summary>
        private async Task WaitRebootAsync()
        {
            _rebootCts?.Cancel();
            var cts = new CancellationTokenSource();
            _rebootCts = cts;
            var ct = cts.Token;
            var until = DateTime.UtcNow.AddMinutes(10);
            Overlay("Riavvio in corso", "Riavvio in corso, mi ricollego appena torna…", ("Annulla", false, Disconnect));
            try
            {
                // indirizzo di prima del riavvio (o nessuno, se il PC l'ha già tolto chiudendosi)
                var before = await ViewerRendezvous.LookupAsync(_number, _peer.S.MqttBrokers, null, ct);
                long beforeTicks = before?.Ticks ?? 0;
                int attempt = 0;
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    attempt++;
                    int left = Math.Max(0, (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes));
                    _overlayText.Text = $"Riavvio in corso, mi ricollego appena torna (controllo n. {attempt}, ancora {left} min)…";
                    var a = await ViewerRendezvous.LookupAsync(_number, _peer.S.MqttBrokers, null, ct);
                    if (a == null || a.Ticks <= beforeTicks || a.Age > TimeSpan.FromMinutes(5)) continue;
                    if (await ConnectAsync(true)) { _rebootCts = null; return; }
                    if (ct.IsCancellationRequested) return;
                    _overlayTitle.Text = "Riavvio in corso";
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { ErrorLog.Write(ex.ToString()); }
            if (ct.IsCancellationRequested || _ended) return;
            Overlay("Il PC remoto non è tornato", "Dopo 10 minuti il PC remoto non risulta ancora in linea.",
                ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));
        }

        private void Failed(string why) =>
            Overlay("Collegamento non riuscito", why, ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));

        private void OnState(ViewerSession s)
        {
            if (s != _session) return;
            switch (s.State)
            {
                case AcceptState.Waiting:
                    Overlay("In attesa di conferma", "Sull'altro PC è comparsa la richiesta: aspetto che l'utente accetti (al massimo un minuto)…", ("Annulla", false, Disconnect));
                    break;
                case AcceptState.Accepted:
                    _overlay.IsVisible = false;
                    _radio.IsVisible = s.PeerHasTrx && s.InputAllowed;
                    _audio.IsChecked = s.AudioOn;
                    _mic.IsVisible = s.PeerHas("mic");
                    _codec.IsVisible = !Platform.IsMobile && Ffmpeg.Path != null && s.PeerCaps.Length > 0;
                    _tunnelBtn.IsVisible = s.PeerHas("tunnel") && s.InputAllowed;
                    if (_tunnelBtn.IsVisible) StartSavedTunnels(s);
                    _fitRes.IsVisible = s.PeerHas("res") && s.InputAllowed;
                    if (_mic.IsVisible && _peer.S.SendMic && !s.MicOn) _mic.IsChecked = true;
                    if (_fitRes.IsVisible && _peer.S.FitResolution)
                    {
                        _lastResize = (0, 0);
                        if (_fitRes.IsChecked == true) SendResize(); else _fitRes.IsChecked = true;
                    }
                    HistoryChanged?.Invoke();
                    RefreshTitle();
                    _view.Focus();
                    string verWarn = AppVersion.Compare(s.PeerVersion);
                    if (!s.InputAllowed) ShowNote("L'altro PC ha concesso solo la visione: mouse, tastiera e file non arrivano.");
                    else if (verWarn.Length > 0) ShowNote(verWarn);
                    else if (Platform.IsMobile) ShowNote(_peer.S.TouchInput.Mode == "diretto"
                        ? "Tocco diretto: si clicca dove tocchi. Due dita: rotella; allargale per ingrandire. Gesti e tasti: pulsante ✋."
                        : "Il dito fa da touchpad. Gesti, tempi e velocità si cambiano col pulsante ✋; due dita allargate = zoom.");
                    break;
                case AcceptState.Rejected:
                    Overlay("Collegamento rifiutato", s.StateText.Length > 0 ? s.StateText : "L'utente dell'altro PC non ha accettato.", ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));
                    break;
                case AcceptState.Busy:
                case AcceptState.Disabled:
                    Overlay("Collegamento non possibile", s.StateText, ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));
                    break;
            }
        }

        private void OnMonitors(ViewerSession s)
        {
            if (s != _session) return;
            _updatingMonitors = true;
            var items = s.Monitors.Select((m, i) => m.Label(i)).ToList();
            if (s.Monitors.Count > 1) items.Add("Tutti gli schermi");
            _monitor.ItemsSource = items;
            _monitor.SelectedIndex = s.SelectedMonitor < 0 ? items.Count - 1 : Math.Min(s.SelectedMonitor, items.Count - 1);
            _monitor.IsEnabled = s.Monitors.Count > 1;
            _updatingMonitors = false;
        }

        private void OnClosed(ViewerSession s, string reason)
        {
            if (s != _session) return;
            HistoryChanged?.Invoke();
            if (_ended) return;
            if (s.RebootRequested) { _ = WaitRebootAsync(); return; }
            if (s.State == AcceptState.Accepted || s.State == AcceptState.Waiting)
                Overlay("Collegamento terminato", reason, ("Ricollegati", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));
        }
    }
}
