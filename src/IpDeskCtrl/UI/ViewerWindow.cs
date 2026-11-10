using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using IpDeskCtrl.Core;
using IpDeskCtrl.Input;
using IpDeskCtrl.Net;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Finestra che mostra un altro PC. In alto al centro c'è la plancia di comando: finestra / schermo intero,
    /// zoom in percentuale o adattato, scelta del monitor, qualità, audio, tasti speciali, disconnessione.
    /// A schermo intero la plancia si riduce a una linguetta e ricompare portando il mouse in alto.
    /// </summary>
    public sealed class ViewerWindow : Window
    {
        private static readonly int[] Zooms = { 0, 25, 50, 75, 100, 125, 150, 200 };

        private readonly Peer _peer;
        private readonly string _number;
        private string _alias;
        private ViewerSession? _session;
        private CancellationTokenSource _cts = new();

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

        private readonly Button _fullBtn = new();
        private readonly ComboBox _zoom = new() { MinWidth = 110 };
        private readonly ComboBox _monitor = new() { MinWidth = 150, MaxWidth = 260 };
        private readonly ComboBox _quality = new() { MinWidth = 120 };
        private readonly ToggleButton _audio = new() { Content = "Audio" };
        private readonly TextBlock _stats = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro, MinWidth = 150 };
        private bool _toolbarPinned = true;
        private bool _updatingMonitors;
        private int _framePosted;
        private DateTime _hideToolbarAt = DateTime.MaxValue;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

        public event Action? HistoryChanged;

        public ViewerWindow(Peer peer, string number, string alias)
        {
            _peer = peer;
            _number = number;
            _alias = alias;
            Width = 1280; Height = 800; MinWidth = 480; MinHeight = 320;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://IP-DESK-CTRL/Assets/icon.png"))); } catch { }
            RefreshTitle();
            _view.ShowRemoteCursor = peer.S.ShowRemoteCursor;
            Content = Build();
            SetZoom(peer.S.ZoomPercent);
            Opened += (_, _) => _ = ConnectAsync();
            Closed += (_, _) =>
            {
                _timer.Stop();
                _cts.Cancel();
                _session?.Close("finestra chiusa");
            };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            PointerMoved += (_, e) =>
            {
                if (WindowState == WindowState.FullScreen && e.GetPosition(this).Y < 6) ShowToolbar(true);
            };
        }

        public void RefreshTitle()
        {
            _alias = _peer.S.Find(_number)?.Alias ?? _alias;
            Title = (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number) + " · IP-DESK-CTRL";
        }

        // =================================================================== costruzione

        private Control Build()
        {
            var grid = new Grid();
            _viewHost.Children.Add(_view);
            grid.Children.Add(_viewHost);

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
            Item("Stamp", KeyMap.PrintScreen);
            Item("Esc", KeyMap.Escape);
            keys.Flyout = menu;
            ToolTip.SetTip(keys, "Combinazioni che il tuo sistema non lascerebbe passare (Windows non accetta Ctrl+Alt+Canc simulato)");

            var refresh = Small("⟳", "Aggiorna l'immagine");
            refresh.Click += (_, _) => _session?.Refresh();
            var close = new Button { Content = "Disconnetti" };
            close.Classes.Add("accent");
            close.Click += (_, _) => Close();
            var hide = Small("▲", "Nascondi la plancia");
            hide.Click += (_, _) => { _toolbarPinned = false; ShowToolbar(false); };

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var c in new Control[] { _fullBtn, Sep(), minus, _zoom, plus, Sep(), _monitor, _quality, _audio, keys, refresh, Sep(), _stats, close, hide })
            {
                c.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(c);
            }
            _toolbar.Child = bar;
            _toolbar.Background = new SolidColorBrush(Color.FromArgb(225, 24, 28, 36));
            _toolbar.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 70, 80, 96));
            _toolbar.BorderThickness = new Thickness(1, 0, 1, 1);
            _toolbar.CornerRadius = new CornerRadius(0, 0, 12, 12);
            _toolbar.Padding = new Thickness(10, 6);
            _toolbar.HorizontalAlignment = HorizontalAlignment.Center;
            _toolbar.VerticalAlignment = VerticalAlignment.Top;
            _toolbar.PointerEntered += (_, _) => _hideToolbarAt = DateTime.MaxValue;
            _toolbar.PointerExited += (_, _) => { if (!_toolbarPinned || WindowState == WindowState.FullScreen) _hideToolbarAt = DateTime.UtcNow.AddSeconds(1.5); };
            // le scritte della plancia restano chiare con qualunque tema
            _toolbar.Resources["B.Text"] = Brushes.White;
            grid.Children.Add(_toolbar);

            // linguetta quando la plancia è nascosta
            _tab.Child = new TextBlock { Text = "▼  " + Identity.Format(_number), Foreground = Brushes.White, FontSize = 12 };
            _tab.Background = new SolidColorBrush(Color.FromArgb(200, 24, 28, 36));
            _tab.CornerRadius = new CornerRadius(0, 0, 8, 8);
            _tab.Padding = new Thickness(14, 2, 14, 4);
            _tab.HorizontalAlignment = HorizontalAlignment.Center;
            _tab.VerticalAlignment = VerticalAlignment.Top;
            _tab.IsVisible = false;
            _tab.Cursor = new Cursor(StandardCursorType.Hand);
            _tab.PointerEntered += (_, _) => ShowToolbar(true);
            _tab.PointerPressed += (_, _) => { _toolbarPinned = true; ShowToolbar(true); };
            grid.Children.Add(_tab);

            // ---- avvisi del PC remoto (permessi, Wayland…)
            _note.Child = _noteText;
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

        private static Control Sep() => new Border { Width = 1, Height = 26, Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), Margin = new Thickness(4, 0) };

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
            if (WindowState == WindowState.FullScreen)
            {
                WindowState = WindowState.Normal;
                _fullBtn.Content = "Schermo intero";
                _toolbarPinned = true;
                ShowToolbar(true);
            }
            else
            {
                WindowState = WindowState.FullScreen;
                _fullBtn.Content = "Finestra";
                ShowToolbar(true);
                _hideToolbarAt = DateTime.UtcNow.AddSeconds(3);
            }
            _view.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+Alt+Invio: finestra / schermo intero (non arriva al PC remoto)
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                ToggleFullScreen();
                e.Handled = true;
                _session?.ReleaseAll();
                return;
            }
            base.OnKeyDown(e);
        }

        private void ShowToolbar(bool show)
        {
            _toolbar.IsVisible = show;
            _tab.IsVisible = !show;
            if (show && (!_toolbarPinned || WindowState == WindowState.FullScreen)) _hideToolbarAt = DateTime.UtcNow.AddSeconds(3);
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
                _stats.Text = $"{s.Fps:F0} fps · {s.KbitPerSecond / 1000:F1} Mbit/s · {rtt} · {s.Path}\n{img.Width}×{img.Height}" + (s.InputAllowed ? "" : " · solo visione");
            }
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
                b.MinWidth = 120;
                b.Click += (_, _) => action();
                _overlayButtons.Children.Add(b);
            }
            _overlay.IsVisible = true;
        }

        private async Task ConnectAsync()
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string who = (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number);
            Overlay("Collegamento a " + who, "Avvio…", ("Annulla", false, () => Close()));
            try
            {
                var link = await _peer.ConnectAsync(_number, msg => Dispatcher.UIThread.Post(() => { if (!ct.IsCancellationRequested) _overlayText.Text = msg; }), ct);
                if (ct.IsCancellationRequested) { link.Dispose(); return; }
                var s = new ViewerSession(_peer, link);
                _session = s;
                _view.Session = s;
                s.StateChanged += () => Dispatcher.UIThread.Post(() => OnState(s));
                s.MonitorsChanged += () => Dispatcher.UIThread.Post(() => OnMonitors(s));
                s.FrameReady += () =>
                {
                    if (Interlocked.Exchange(ref _framePosted, 1) == 1) return;
                    Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _framePosted, 0); _view.UpdateImage(); }, DispatcherPriority.Render);
                };
                s.CursorMoved += () => Dispatcher.UIThread.Post(() => _view.InvalidateVisual());
                s.NoteChanged += () => Dispatcher.UIThread.Post(() => { _noteText.Text = s.Note; _note.IsVisible = s.Note.Length > 0; });
                s.Closed += reason => Dispatcher.UIThread.Post(() => OnClosed(s, reason));
                _overlayText.Text = "Collegato con cifratura AES-256. Attendo che l'altro PC risponda…";
                s.Run(ct);
            }
            catch (OperationCanceledException) { }
            catch (PeerException ex) { Failed(ex.Message); }
            catch (Exception ex)
            {
                Program.LogError(ex.ToString());
                Failed("Collegamento non riuscito: " + ex.Message);
            }
        }

        private void Failed(string why) =>
            Overlay("Collegamento non riuscito", why, ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, () => Close()));

        private void OnState(ViewerSession s)
        {
            if (s != _session) return;
            switch (s.State)
            {
                case AcceptState.Waiting:
                    Overlay("In attesa di conferma", "Sull'altro PC è comparsa la richiesta: aspetto che l'utente accetti (al massimo un minuto)…", ("Annulla", false, () => Close()));
                    break;
                case AcceptState.Accepted:
                    _overlay.IsVisible = false;
                    _audio.IsChecked = s.AudioOn;
                    HistoryChanged?.Invoke();
                    RefreshTitle();
                    _view.Focus();
                    if (!s.InputAllowed) { _noteText.Text = "L'altro PC ha concesso solo la visione: mouse e tastiera non arrivano."; _note.IsVisible = true; }
                    break;
                case AcceptState.Rejected:
                    Overlay("Collegamento rifiutato", s.StateText.Length > 0 ? s.StateText : "L'utente dell'altro PC non ha accettato.", ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, () => Close()));
                    break;
                case AcceptState.Busy:
                case AcceptState.Disabled:
                    Overlay("Collegamento non possibile", s.StateText, ("Riprova", true, () => _ = ConnectAsync()), ("Chiudi", false, () => Close()));
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
            if (!IsVisible) return;
            if (s.State == AcceptState.Accepted || s.State == AcceptState.Waiting)
                Overlay("Collegamento terminato", reason, ("Ricollegati", true, () => _ = ConnectAsync()), ("Chiudi", false, () => Close()));
        }
    }
}
