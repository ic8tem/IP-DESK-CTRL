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
            keys.Flyout = menu;
            ToolTip.SetTip(keys, "Combinazioni che il tuo sistema non lascerebbe passare (Windows non accetta Ctrl+Alt+Canc simulato)");

            _files.Click += (_, _) => ShowFilesMenu();
            ToolTip.SetTip(_files, "Manda file al PC remoto o prendi quelli copiati lì. Puoi anche trascinarli sulla finestra.");

            var keyboard = new Button { Content = "Tastiera" };
            keyboard.Click += (_, _) => ToggleKeyboard();
            keyboard.IsVisible = Platform.IsMobile;

            var refresh = Small("⟳", "Aggiorna l'immagine");
            refresh.Click += (_, _) => _session?.Refresh();
            var close = new Button { Content = "Disconnetti" };
            close.Classes.Add("accent");
            close.Click += (_, _) => Disconnect();
            var hide = Small("▲", "Nascondi la plancia");
            hide.Click += (_, _) => { _toolbarPinned = false; ShowToolbar(false); };

            var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in new Control[] { _fullBtn, minus, _zoom, plus, _monitor, _quality, _audio, keyboard, keys, _files, refresh, _stats, close, hide })
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
                _stats.Text = $"{s.Fps:F0} fps · {s.KbitPerSecond / 1000:F1} Mbit/s · {rtt} · {s.Route}\n{img.Width}×{img.Height}" + (s.InputAllowed ? "" : " · solo visione");
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
            var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            Button K(string text, Action a) { var b = new Button { Content = text, MinWidth = 44, Margin = new Thickness(2) }; b.Click += (_, _) => { a(); _kbd.Focus(); }; return b; }
            void Tap(int k) { var s = _session; if (s == null) return; WithMods(() => { s.Key(k, true); s.Key(k, false); }); }
            foreach (var t in new[] { _ctrl, _alt, _win, _shift }) { t.Margin = new Thickness(2); bar.Children.Add(t); }
            bar.Children.Add(K("Esc", () => Tap(KeyMap.Escape)));
            bar.Children.Add(K("Tab", () => Tap(KeyMap.Tab)));
            bar.Children.Add(K("←", () => Tap(105)));
            bar.Children.Add(K("↑", () => Tap(103)));
            bar.Children.Add(K("↓", () => Tap(108)));
            bar.Children.Add(K("→", () => Tap(106)));
            bar.Children.Add(K("Canc", () => Tap(KeyMap.Delete)));
            bar.Children.Add(K("Home", () => Tap(102)));
            bar.Children.Add(K("Fine", () => Tap(107)));
            var done = new Button { Content = "Chiudi tastiera", Margin = new Thickness(2) };
            done.Click += (_, _) => ToggleKeyboard();
            bar.Children.Add(done);
            _keysBar.Child = bar;
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

        private async Task ConnectAsync()
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            string who = (_alias.Length > 0 ? _alias + " · " : "") + Identity.Format(_number);
            Overlay("Collegamento a " + who, "Avvio…", ("Annulla", false, Disconnect));
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
                s.NoteChanged += () => Dispatcher.UIThread.Post(() => ShowNote(s.Note));
                s.TransferChanged += t => Dispatcher.UIThread.Post(() => OnTransfer(t));
                s.FilesReceived += (folder, items, onClip) => Dispatcher.UIThread.Post(() =>
                    ShowNote((items.Count == 1 ? "1 elemento ricevuto" : $"{items.Count} elementi ricevuti") + $" in {folder}" +
                             (onClip ? (Platform.IsMobile ? "." : ": sono negli appunti, incollali con Ctrl+V dove vuoi.") : "."), true));
                s.Closed += reason => Dispatcher.UIThread.Post(() => OnClosed(s, reason));
                _overlayText.Text = "Collegato con cifratura AES-256. Attendo che l'altro PC risponda…";
                s.Run(ct);
            }
            catch (OperationCanceledException) { }
            catch (PeerException ex) { Failed(ex.Message); }
            catch (Exception ex)
            {
                ErrorLog.Write(ex.ToString());
                Failed("Collegamento non riuscito: " + ex.Message);
            }
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
                    _audio.IsChecked = s.AudioOn;
                    HistoryChanged?.Invoke();
                    RefreshTitle();
                    _view.Focus();
                    if (!s.InputAllowed) ShowNote("L'altro PC ha concesso solo la visione: mouse, tastiera e file non arrivano.");
                    else if (Platform.IsMobile) ShowNote("Il dito fa da touchpad: tocco = clic, dito fermo = clic destro, due tocchi e trascina = trascinamento, due dita su e giù = rotella.");
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
            if (s.State == AcceptState.Accepted || s.State == AcceptState.Waiting)
                Overlay("Collegamento terminato", reason, ("Ricollegati", true, () => _ = ConnectAsync()), ("Chiudi", false, Disconnect));
        }
    }
}
