using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>Una finestra di dialogo aperta (finestra vera sul PC, riquadro sopra l'app su Android).</summary>
    public interface IDialogHandle
    {
        void Close();
        event Action? Closed;
    }

    /// <summary>
    /// Come l'interfaccia mostra le sue parti: sul PC ogni collegamento ha la sua finestra; su Android
    /// c'è una sola vista e i collegamenti e i dialoghi si sovrappongono alla schermata principale.
    /// </summary>
    public interface IShell
    {
        TopLevel? Top { get; }
        void OpenViewer(ViewerView v);
        void CloseViewer(ViewerView v);
        void ActivateViewer(ViewerView v);
        void SetViewerTitle(ViewerView v, string title);
        void SetFullScreen(ViewerView v, bool on);
        bool IsFullScreen(ViewerView v);
        IDialogHandle ShowDialog(string title, Control content, double width, bool topmost);
        /// <summary>Riquadro piccolo in un angolo (questo PC è comandato); null dove non serve.</summary>
        IDialogHandle? ShowCorner(Control content);
    }

    public static class Shell
    {
        public static IShell Current { get; set; } = null!;
    }

    // ===================================================================== appunti, uguali ovunque

    /// <summary>Appunti attraverso Avalonia, sempre dal thread dell'interfaccia.</summary>
    public sealed class AvaloniaClipboard : IClipboardBridge
    {
        private readonly Func<TopLevel?> _top;
        public AvaloniaClipboard(Func<TopLevel?> top) { _top = top; }

        private Task<T> Ui<T>(Func<Task<T>> f) => Dispatcher.UIThread.InvokeAsync(f);

        public Task<string?> GetTextAsync() => Ui(async () =>
        {
            var c = _top()?.Clipboard;
            return c == null ? null : await c.GetTextAsync();
        });

        public Task SetTextAsync(string text) => Ui(async () =>
        {
            var c = _top()?.Clipboard;
            if (c != null) await c.SetTextAsync(text);
            return true;
        });

        public Task<List<string>> GetFilesAsync() => Ui(async () =>
        {
            var list = new List<string>();
            var c = _top()?.Clipboard;
            if (c == null) return list;
            try
            {
                var formats = await c.GetFormatsAsync();
                if (formats == null || !formats.Contains(DataFormats.Files)) return list;
                if (await c.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items)
                    foreach (var i in items)
                        if (i.TryGetLocalPath() is string p) list.Add(p);
            }
            catch { }
            return list;
        });

        public Task<bool> SetFilesAsync(List<string> paths) => Ui(async () =>
        {
            var top = _top();
            if (top?.Clipboard == null) return false;
            try
            {
                var items = new List<IStorageItem>();
                foreach (string p in paths)
                {
                    IStorageItem? it = System.IO.Directory.Exists(p)
                        ? await top.StorageProvider.TryGetFolderFromPathAsync(new Uri(p))
                        : await top.StorageProvider.TryGetFileFromPathAsync(new Uri(p));
                    if (it != null) items.Add(it);
                }
                if (items.Count == 0) return false;
                var data = new DataObject();
                data.Set(DataFormats.Files, items);
                await top.Clipboard.SetDataObjectAsync(data);
                return true;
            }
            catch { return false; }
        });
    }

    // ===================================================================== PC: finestre

    public sealed class DesktopShell : IShell
    {
        private readonly Window _main;
        private readonly Dictionary<ViewerView, Window> _windows = new();

        public DesktopShell(Window main) { _main = main; }

        public TopLevel? Top => _main;

        public void OpenViewer(ViewerView v)
        {
            var w = new Window
            {
                Width = 1280, Height = 800, MinWidth = 480, MinHeight = 320,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = v,
                Title = "IP-DESK-CTRL"
            };
            try { w.Icon = _main.Icon; } catch { }
            _windows[v] = w;
            w.Closed += (_, _) => { _windows.Remove(v); v.OnClosedByShell(); };
            w.Show();
        }

        public void CloseViewer(ViewerView v) { if (_windows.TryGetValue(v, out var w)) w.Close(); }
        public void ActivateViewer(ViewerView v) { if (_windows.TryGetValue(v, out var w)) w.Activate(); }
        public void SetViewerTitle(ViewerView v, string title) { if (_windows.TryGetValue(v, out var w)) w.Title = title; }

        public void SetFullScreen(ViewerView v, bool on)
        {
            if (_windows.TryGetValue(v, out var w)) w.WindowState = on ? WindowState.FullScreen : WindowState.Normal;
        }

        public bool IsFullScreen(ViewerView v) => _windows.TryGetValue(v, out var w) && w.WindowState == WindowState.FullScreen;

        private sealed class WinHandle : IDialogHandle
        {
            private readonly Window _w;
            public event Action? Closed;
            public WinHandle(Window w) { _w = w; w.Closed += (_, _) => Closed?.Invoke(); }
            public void Close() => _w.Close();
        }

        public IDialogHandle ShowDialog(string title, Control content, double width, bool topmost)
        {
            var w = new Window
            {
                Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
                Topmost = topmost, WindowStartupLocation = topmost ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Content = new Border { Padding = new Thickness(22), Child = content }
            };
            var h = new WinHandle(w);
            if (topmost) { w.Show(); w.Activate(); } else w.Show(_main);
            return h;
        }

        public IDialogHandle? ShowCorner(Control content)
        {
            var w = new Window
            {
                Title = "IP-DESK-CTRL · collegamento attivo", SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
                Topmost = true, ShowActivated = false, SystemDecorations = SystemDecorations.BorderOnly, Content = content
            };
            w.Opened += (_, _) =>
            {
                var sc = w.Screens.Primary;
                if (sc != null) w.Position = new PixelPoint(sc.WorkingArea.Right - (int)(w.Bounds.Width * sc.Scaling) - 20, sc.WorkingArea.Y + 20);
            };
            var h = new WinHandle(w);
            w.Show();
            return h;
        }
    }

    // ===================================================================== Android: una sola vista

    /// <summary>Radice dell'app su Android: schermata principale, collegamenti aperti sopra, dialoghi in cima.</summary>
    public sealed class SingleViewShell : Grid, IShell
    {
        private readonly Panel _viewers = new() { IsVisible = false };
        private readonly Panel _dialogs = new() { IsVisible = false, Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
        private readonly List<ViewerView> _stack = new();

        public SingleViewShell()
        {
            _viewers.Background = Brushes.Black;
            Children.Add(_viewers);
            Children.Add(_dialogs);
        }

        public void SetMain(Control main) => Children.Insert(0, main);

        public TopLevel? Top => TopLevel.GetTopLevel(this);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // tasto Indietro di Android: chiude il dialogo, poi il collegamento in primo piano
            var top = TopLevel.GetTopLevel(this);
            if (top != null) top.BackRequested += (_, a) =>
            {
                if (_dialogs.Children.Count > 0 && _dialogs.Children[^1] is Control d && d.Tag is DialogHandle h && h.Cancelable) { h.Close(); a.Handled = true; }
                else if (_stack.Count > 0) { _stack[^1].Disconnect(); a.Handled = true; }
            };
        }

        private void ShowTopViewer()
        {
            _viewers.Children.Clear();
            if (_stack.Count > 0) _viewers.Children.Add(_stack[^1]);
            _viewers.IsVisible = _stack.Count > 0;
            if (_stack.Count == 0) Platform.FullScreen?.Invoke(false);
        }

        public void OpenViewer(ViewerView v) { _stack.Add(v); ShowTopViewer(); }

        public void CloseViewer(ViewerView v)
        {
            if (!_stack.Remove(v)) return;
            ShowTopViewer();
            v.OnClosedByShell();
        }

        public void ActivateViewer(ViewerView v) { if (_stack.Remove(v)) { _stack.Add(v); ShowTopViewer(); } }
        public void SetViewerTitle(ViewerView v, string title) { }

        private bool _full;
        public void SetFullScreen(ViewerView v, bool on) { _full = on; Platform.FullScreen?.Invoke(on); }
        public bool IsFullScreen(ViewerView v) => _full;

        private sealed class DialogHandle : IDialogHandle
        {
            private readonly SingleViewShell _s;
            private readonly Control _c;
            public bool Cancelable;
            public event Action? Closed;
            public DialogHandle(SingleViewShell s, Control c) { _s = s; _c = c; }
            public void Close()
            {
                if (!_s._dialogs.Children.Remove(_c)) return;
                _s._dialogs.IsVisible = _s._dialogs.Children.Count > 0;
                Closed?.Invoke();
            }
        }

        public IDialogHandle ShowDialog(string title, Control content, double width, bool topmost)
        {
            var box = new Border
            {
                Background = Themes.B(Themes.Current.Panel), CornerRadius = new CornerRadius(14), Padding = new Thickness(20),
                Margin = new Thickness(12), MaxWidth = Math.Min(width, 560), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new ScrollViewer { Content = content }
            };
            var h = new DialogHandle(this, box) { Cancelable = !topmost };
            box.Tag = h;
            _dialogs.Children.Add(box);
            _dialogs.IsVisible = true;
            return h;
        }

        public IDialogHandle? ShowCorner(Control content) => null;   // su Android c'è la notifica e il riquadro nella schermata principale
    }
}
