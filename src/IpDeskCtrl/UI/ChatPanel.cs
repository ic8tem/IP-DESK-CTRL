using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Chat tra chi comanda e chi è davanti al PC: cronologia, casella di scrittura, Invio per mandare.
    /// Nel visore sta sul lato destro sopra l'immagine; sul PC comandato si apre in una finestra.
    /// </summary>
    public sealed class ChatPanel : Border
    {
        private readonly StackPanel _lines = new() { Spacing = 6 };
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input = new() { Watermark = L.T("Scrivi un messaggio e premi Invio"), AcceptsReturn = false };
        private readonly Action<string> _send;
        private readonly string _them;

        /// <summary>Chiesta la chiusura col pulsante ✕ (null = nessun pulsante).</summary>
        public event Action? CloseRequested;

        public ChatPanel(string them, Action<string> send, bool closeButton)
        {
            _them = them;
            _send = send;
            Background = new SolidColorBrush(Color.FromArgb(235, 24, 28, 36));
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(10);

            var title = new TextBlock { Text = L.F("Chat con {0}", them), FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (closeButton)
            {
                var x = new Button { Content = "✕", Padding = new Thickness(8, 2) };
                x.Click += (_, _) => CloseRequested?.Invoke();
                DockPanel.SetDock(x, Dock.Right);
                head.Children.Add(x);
            }
            head.Children.Add(title);

            _scroll = new ScrollViewer { Content = _lines, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var go = new Button { Content = L.T("Invia"), Margin = new Thickness(6, 0, 0, 0) };
            go.Classes.Add("accent");
            go.Click += (_, _) => Submit();
            _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Submit(); } };
            var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(go, Dock.Right);
            bottom.Children.Add(go);
            bottom.Children.Add(_input);

            var root = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(head);
            root.Children.Add(bottom);
            root.Children.Add(_scroll);
            Child = root;
        }

        public void FocusInput() => _input.Focus();

        private void Submit()
        {
            string t = (_input.Text ?? "").Trim();
            if (t.Length == 0) return;
            if (t.Length > 4000) t = t[..4000];
            _input.Text = "";
            _send(t);
            Add(true, t);
        }

        /// <summary>Aggiunge una riga: mine = scritta da qui.</summary>
        public void Add(bool mine, string text)
        {
            var who = new TextBlock
            {
                Text = (mine ? L.T("Tu") : _them) + "  " + DateTime.Now.ToString("HH:mm"),
                FontSize = 11,
                Foreground = new SolidColorBrush(mine ? Color.FromRgb(140, 200, 255) : Color.FromRgb(255, 200, 120)),
            };
            var body = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
            _lines.Children.Add(new StackPanel { Spacing = 1, HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 300, Children = { who, body } });
            while (_lines.Children.Count > 300) _lines.Children.RemoveAt(0);
            _scroll.ScrollToEnd();
        }

        // ------------------------------------------------------------------ finestre sul PC comandato

        private static readonly Dictionary<string, (Window win, ChatPanel panel)> _windows = new();

        /// <summary>
        /// Mostra (o riporta davanti) la finestra di chat verso quel numero e, se c'è, aggiunge il messaggio ricevuto.
        /// Da chiamare sul thread grafico.
        /// </summary>
        public static void ShowWindow(Window? owner, string number, Action<string> send, string? received)
        {
            if (!_windows.TryGetValue(number, out var w))
            {
                var panel = new ChatPanel(Identity.Format(number), send, false) { CornerRadius = new CornerRadius(0) };
                var win = new Window
                {
                    Title = L.F("Chat con {0}", Identity.Format(number)) + " · IP-DESK-CTRL",
                    Width = 380, Height = 460, MinWidth = 280, MinHeight = 260,
                    Content = panel,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                };
                win.Closed += (_, _) => _windows.Remove(number);
                w = (win, panel);
                _windows[number] = w;
                win.Show();
            }
            if (received != null) w.panel.Add(false, received);
            if (!w.win.IsVisible) w.win.Show();
            if (w.win.WindowState == WindowState.Minimized) w.win.WindowState = WindowState.Normal;
            w.win.Activate();
            w.panel.FocusInput();
        }

        /// <summary>Chiude la finestra di chat di quel numero (sessione finita).</summary>
        public static void CloseWindow(string number)
        {
            if (_windows.TryGetValue(number, out var w)) w.panel.Add(false, L.T("(collegamento chiuso)"));
        }
    }
}
