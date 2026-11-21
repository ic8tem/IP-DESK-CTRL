using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Trascinamento dal PC comandato verso chi comanda. Sul PC comandato un file viene trascinato in Esplora file;
    /// quando il puntatore di chi comanda esce dalla finestra del visore, qui si apre per un attimo una finestrella
    /// quasi invisibile sotto il puntatore e il file vi viene «lasciato»: così si sa quali file sono stati trascinati
    /// e si mandano a chi comanda.
    /// </summary>
    public static class DropCatcher
    {
        /// <summary>Da chiamare sul thread grafico. x, y in pixel dello schermo. Finisce al rilascio o dopo 3,5 s.</summary>
        public static Task<List<string>> CatchAsync(int x, int y)
        {
            var tcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pad = new Border { Background = new SolidColorBrush(Color.FromArgb(2, 255, 255, 255)) };
            DragDrop.SetAllowDrop(pad, true);
            void Effects(DragEventArgs e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            pad.AddHandler(DragDrop.DragEnterEvent, (_, e) => Effects(e));
            pad.AddHandler(DragDrop.DragOverEvent, (_, e) => Effects(e));
            pad.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                var paths = (e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>()).Select(i => i.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
                tcs.TrySetResult(paths);
            });
            const int size = 120;
            var w = new Window
            {
                SystemDecorations = SystemDecorations.None, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                CanResize = false, Width = size, Height = size, Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Content = pad, Title = "IP-DESK-CTRL · trascinamento",
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(x - size / 2, y - size / 2),
            };
            w.Opened += (_, _) =>
            {
                double sc = w.RenderScaling > 0 ? w.RenderScaling : 1;
                w.Position = new PixelPoint(x - (int)(size * sc / 2), y - (int)(size * sc / 2));
            };
            w.Show();
            _ = Task.Delay(3500).ContinueWith(_ => tcs.TrySetResult(new List<string>()));
            tcs.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() => { try { w.Close(); } catch { } }));
            return tcs.Task;
        }
    }
}
