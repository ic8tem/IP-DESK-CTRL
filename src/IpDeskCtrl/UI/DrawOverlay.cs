using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Lavagna sul PC comandato: una finestra trasparente in primo piano sopra lo schermo condiviso, con i tratti
    /// rossi disegnati da chi comanda. Sparisce quando chi disegna cancella o esce dalla lavagna, dopo 20 secondi
    /// senza nuovi tratti, a fine sessione o con un clic sopra (così non blocca mai il PC).
    /// </summary>
    public static class DrawOverlay
    {
        private sealed class Canvas : Control
        {
            public readonly List<List<Point>> Strokes = new();

            public override void Render(DrawingContext ctx)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 40, 40)), 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                double w = Bounds.Width, h = Bounds.Height;
                foreach (var s in Strokes)
                {
                    if (s.Count == 1) { ctx.DrawEllipse(pen.Brush, null, new Point(s[0].X * w, s[0].Y * h), 2.5, 2.5); continue; }
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(s[0].X * w, s[0].Y * h), false);
                        for (int i = 1; i < s.Count; i++) c.LineTo(new Point(s[i].X * w, s[i].Y * h));
                        c.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, pen, g);
                }
            }
        }

        private static Window? _win;
        private static Canvas? _canvas;
        private static (int x, int y, int w, int h) _rect;
        private static string _from = "";
        private static DispatcherTimer? _idle;
        private static bool _noTransparency;

        /// <summary>Da chiamare sul thread grafico.</summary>
        public static void Show(DrawEvent e)
        {
            if (Platform.IsMobile) return;
            if (e.Op == DrawEvent.Clear) { Close(); return; }
            if (e.W <= 0 || e.H <= 0 || _noTransparency) return;
            if (_win == null || _rect != (e.X, e.Y, e.W, e.H)) Open(e);
            _from = e.From;
            var c = _canvas!;
            if (e.Op == DrawEvent.NewStroke || c.Strokes.Count == 0) c.Strokes.Add(new List<Point>());
            var cur = c.Strokes[^1];
            for (int i = 0; i + 1 < e.Pts.Length; i += 2) cur.Add(new Point(e.Pts[i], e.Pts[i + 1]));
            while (c.Strokes.Count > 200) c.Strokes.RemoveAt(0);
            c.InvalidateVisual();
            _idle ??= new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => Close());
            _idle.Stop();
            _idle.Start();
        }

        /// <summary>Chiude la lavagna se l'ha aperta quel numero (sessione finita).</summary>
        public static void EndedFor(string number) { if (_from == number) Close(); }

        public static void Close()
        {
            _idle?.Stop();
            var w = _win;
            _win = null; _canvas = null; _from = "";
            try { w?.Close(); } catch { }
        }

        private static void Open(DrawEvent e)
        {
            Close();
            _rect = (e.X, e.Y, e.W, e.H);
            _canvas = new Canvas();
            var w = new Window
            {
                SystemDecorations = SystemDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Content = _canvas,
                Title = "IP-DESK-CTRL · lavagna",
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(e.X, e.Y),
                Width = e.W, Height = e.H,
            };
            void Fit()
            {
                double sc = w.RenderScaling > 0 ? w.RenderScaling : 1;
                w.Position = new PixelPoint(e.X, e.Y);
                w.Width = e.W / sc; w.Height = e.H / sc;
            }
            w.Opened += (_, _) =>
            {
                // senza trasparenza (Linux senza compositore) la finestra coprirebbe lo schermo di nero: niente lavagna
                if (w.ActualTransparencyLevel == WindowTransparencyLevel.None)
                {
                    SessionLog.Write("LAVAGNA non mostrata: il sistema non supporta le finestre trasparenti");
                    _noTransparency = true;
                    Close();
                    return;
                }
                Fit();
            };
            w.ScalingChanged += (_, _) => Fit();
            w.PointerPressed += (_, _) => Close();      // un clic di chi è davanti al PC la toglie subito
            _win = w;
            w.Show();
        }
    }
}
