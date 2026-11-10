using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IpDeskCtrl.Core;
using IpDeskCtrl.Input;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Disegna lo schermo remoto e raccoglie mouse e tastiera.
    /// Zoom = 0: l'immagine si adatta alla finestra (mantenendo le proporzioni); altrimenti percentuale fissa
    /// (con le barre di scorrimento della finestra, se serve).
    /// </summary>
    public sealed class RemoteView : Control
    {
        private WriteableBitmap? _bmp;
        private int _w, _h;
        private ViewerSession? _session;
        private int _zoom;
        private bool _pointerInside;
        private Point _lastLocal;

        public RemoteView()
        {
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Arrow);
        }

        public ViewerSession? Session
        {
            get => _session;
            set { _session = value; }
        }

        /// <summary>0 = adatta, altrimenti percentuale.</summary>
        public int Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Clamp(value, 0, 400);
                RenderOptions.SetBitmapInterpolationMode(this, _zoom == 100 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public bool ShowRemoteCursor { get; set; } = true;

        /// <summary>Ctrl+Alt+Invio: passa da finestra a schermo intero (non va al PC remoto).</summary>
        public event Action? FullScreenToggleRequested;
        public PixelSize ImageSize => new(_w, _h);

        /// <summary>Copia l'ultima immagine decodificata nella bitmap (thread dell'interfaccia).</summary>
        public unsafe void UpdateImage()
        {
            var s = _session;
            if (s == null) return;
            var dec = s.Decoder;
            bool resized = false;
            lock (dec.Lock)
            {
                var img = dec.Image;
                if (img.Width <= 0 || img.Height <= 0) return;
                if (_bmp == null || img.Width != _w || img.Height != _h)
                {
                    _bmp?.Dispose();
                    _bmp = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    _w = img.Width; _h = img.Height;
                    resized = true;
                }
                using var fb = _bmp.Lock();
                int row = img.Width * 4;
                fixed (byte* src = img.Data)
                {
                    byte* dst = (byte*)fb.Address;
                    if (fb.RowBytes == img.Stride)
                        Buffer.MemoryCopy(src, dst, (long)fb.RowBytes * img.Height, (long)img.Stride * img.Height);
                    else
                        for (int y = 0; y < img.Height; y++)
                            Buffer.MemoryCopy(src + y * img.Stride, dst + y * fb.RowBytes, fb.RowBytes, row);
                }
            }
            if (resized) InvalidateMeasure();
            InvalidateVisual();
        }

        // ------------------------------------------------------------------ disposizione

        private double Scale(Size avail)
        {
            if (_w == 0 || _h == 0) return 1;
            if (_zoom > 0) return _zoom / 100.0;
            return Math.Max(0.01, Math.Min(avail.Width / _w, avail.Height / _h));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_w == 0) return new Size(0, 0);
            if (_zoom > 0) return new Size(_w * _zoom / 100.0, _h * _zoom / 100.0);
            double aw = double.IsInfinity(availableSize.Width) ? _w : availableSize.Width;
            double ah = double.IsInfinity(availableSize.Height) ? _h : availableSize.Height;
            return new Size(aw, ah);
        }

        private Rect Dest()
        {
            var b = Bounds.Size;
            double sc = Scale(b);
            double dw = _w * sc, dh = _h * sc;
            double x = Math.Max(0, (b.Width - dw) / 2), y = Math.Max(0, (b.Height - dh) / 2);
            return new Rect(x, y, dw, dh);
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            if (_bmp == null) return;
            var d = Dest();
            ctx.DrawImage(_bmp, new Rect(0, 0, _w, _h), d);

            // puntatore del PC remoto (quando lo muove qualcuno di là, o il nostro è fuori dalla finestra)
            var s = _session;
            if (_touchMode)
            {
                double tsc = d.Width / Math.Max(1, _w);
                DrawArrow(ctx, new Point(d.X + _cur.X * tsc, d.Y + _cur.Y * tsc));
            }
            else if (ShowRemoteCursor && s != null && s.RemoteCursor.visible)
            {
                double sc = d.Width / Math.Max(1, _w);
                var p = new Point(d.X + s.RemoteCursor.x * sc, d.Y + s.RemoteCursor.y * sc);
                if (!_pointerInside || Distance(p, _lastLocal) > 6) DrawArrow(ctx, p);
            }
        }

        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static readonly StreamGeometry Arrow = StreamGeometry.Parse("M0,0 L0,16 L4.5,12 L7.5,19 L10,18 L7,11 L12.5,11 Z");

        private static void DrawArrow(DrawingContext ctx, Point p)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(p.X, p.Y)))
                ctx.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.2), Arrow);
        }

        // ------------------------------------------------------------------ mouse

        public (int x, int y)? ImagePoint(Point local) => ToImage(local);

        private (int x, int y)? ToImage(Point local)
        {
            if (_w == 0) return null;
            var d = Dest();
            if (d.Width <= 0 || d.Height <= 0) return null;
            int x = (int)Math.Floor((local.X - d.X) * _w / d.Width);
            int y = (int)Math.Floor((local.Y - d.Y) * _h / d.Height);
            return (Math.Clamp(x, 0, _w - 1), Math.Clamp(y, 0, _h - 1));
        }

        private bool CanInput => _session != null && _session.State == Net.AcceptState.Accepted && _session.InputAllowed && !_session.IsClosed;

        protected override void OnPointerEntered(PointerEventArgs e) { _pointerInside = true; base.OnPointerEntered(e); }
        protected override void OnPointerExited(PointerEventArgs e) { _pointerInside = false; InvalidateVisual(); base.OnPointerExited(e); }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.Pointer.Type == PointerType.Touch) { if (CanInput) TouchMoved(e.Pointer.Id, e.GetPosition(this)); e.Handled = true; return; }
            _lastLocal = e.GetPosition(this);
            if (!CanInput) return;
            var p = ToImage(_lastLocal);
            if (p != null) _session!.MouseMove(p.Value.x, p.Value.y);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            if (!CanInput) return;
            e.Pointer.Capture(this);
            if (e.Pointer.Type == PointerType.Touch) { TouchPressed(e.Pointer.Id, e.GetPosition(this)); e.Handled = true; return; }
            SendButton(e.GetCurrentPoint(this).Properties.PointerUpdateKind, e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!CanInput) return;
            if (e.Pointer.Type == PointerType.Touch) { TouchReleased(e.Pointer.Id); e.Handled = true; return; }
            SendButton(e.GetCurrentPoint(this).Properties.PointerUpdateKind, e.GetPosition(this));
            e.Handled = true;
        }

        private void SendButton(PointerUpdateKind kind, Point local)
        {
            var p = ToImage(local);
            if (p == null) return;
            (int b, bool down) = kind switch
            {
                PointerUpdateKind.LeftButtonPressed => (MouseBtn.Left, true),
                PointerUpdateKind.LeftButtonReleased => (MouseBtn.Left, false),
                PointerUpdateKind.RightButtonPressed => (MouseBtn.Right, true),
                PointerUpdateKind.RightButtonReleased => (MouseBtn.Right, false),
                PointerUpdateKind.MiddleButtonPressed => (MouseBtn.Middle, true),
                PointerUpdateKind.MiddleButtonReleased => (MouseBtn.Middle, false),
                PointerUpdateKind.XButton1Pressed => (MouseBtn.Back, true),
                PointerUpdateKind.XButton1Released => (MouseBtn.Back, false),
                PointerUpdateKind.XButton2Pressed => (MouseBtn.Forward, true),
                PointerUpdateKind.XButton2Released => (MouseBtn.Forward, false),
                _ => (-1, false)
            };
            if (b >= 0) _session!.MouseButton(b, down, p.Value.x, p.Value.y);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (!CanInput) return;
            int dy = (int)Math.Round(e.Delta.Y * 120), dx = (int)Math.Round(-e.Delta.X * 120);
            if (dx != 0 || dy != 0) _session!.Wheel(dx, dy);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ tocco (telefono, tablet, schermi touch)
        //
        // Il dito fa da touchpad: muove il puntatore del PC remoto di quanto si sposta (non lo «teletrasporta»),
        // così si può puntare con precisione anche con lo schermo rimpicciolito.
        //   tocco                      clic sinistro
        //   due tocchi, il secondo tenuto e trascinato    trascinamento (tasto sinistro tenuto)
        //   dito fermo più di 0,6 s    clic destro
        //   tocco con due dita         clic destro
        //   due dita in su o in giù    rotella

        private readonly Dictionary<long, Point> _touches = new();
        private bool _touchMode, _dragging, _moved;
        private Point _cur, _downPos;
        private long _downAt, _lastTapAt;
        private int _maxTouches;
        private double _scrollAcc;
        private Point _lastMid;

        private double ViewScale => Dest().Width / Math.Max(1, _w);

        private void TouchPressed(long id, Point p)
        {
            if (_w == 0) return;
            if (!_touchMode)
            {
                _touchMode = true;
                var rc = _session?.RemoteCursor;
                _cur = rc != null && rc.Value.visible ? new Point(rc.Value.x, rc.Value.y) : new Point(_w / 2.0, _h / 2.0);
            }
            _touches[id] = p;
            long now = Environment.TickCount64;
            if (_touches.Count == 1)
            {
                _downAt = now; _downPos = p; _moved = false; _maxTouches = 1;
                if (now - _lastTapAt < 300)
                {
                    _dragging = true;
                    _session!.MouseButton(MouseBtn.Left, true, (int)_cur.X, (int)_cur.Y);
                }
            }
            else
            {
                _maxTouches = Math.Max(_maxTouches, _touches.Count);
                _lastMid = Mid();
                _scrollAcc = 0;
            }
        }

        private Point Mid()
        {
            double x = 0, y = 0;
            foreach (var t in _touches.Values) { x += t.X; y += t.Y; }
            return new Point(x / _touches.Count, y / _touches.Count);
        }

        private void TouchMoved(long id, Point p)
        {
            if (!_touches.TryGetValue(id, out var last)) return;
            _touches[id] = p;
            if (_touches.Count == 1)
            {
                if (Distance(p, _downPos) > 10) _moved = true;
                if (!_moved) return;
                double k = 1.4 / Math.Max(0.05, ViewScale);       // un po' più veloce del dito
                _cur = new Point(Math.Clamp(_cur.X + (p.X - last.X) * k, 0, _w - 1), Math.Clamp(_cur.Y + (p.Y - last.Y) * k, 0, _h - 1));
                _session!.MouseMove((int)_cur.X, (int)_cur.Y);
                FollowCursor();
                InvalidateVisual();
            }
            else
            {
                var m = Mid();
                _scrollAcc += m.Y - _lastMid.Y;
                if (Distance(m, _lastMid) > 0.5) _moved = true;
                _lastMid = m;
                while (Math.Abs(_scrollAcc) >= 24)
                {
                    int dir = Math.Sign(_scrollAcc);
                    _session!.Wheel(0, dir * 120);       // dita in giù = si risale nel documento, come sul telefono
                    _scrollAcc -= dir * 24;
                }
            }
        }

        private void TouchReleased(long id)
        {
            _touches.Remove(id);
            if (_touches.Count > 0) return;
            long now = Environment.TickCount64;
            int x = (int)_cur.X, y = (int)_cur.Y;
            if (_dragging)
            {
                _dragging = false;
                _session!.MouseButton(MouseBtn.Left, false, x, y);
                _lastTapAt = 0;
            }
            else if (_maxTouches == 1 && !_moved && now - _downAt < 350)
            {
                _session!.MouseButton(MouseBtn.Left, true, x, y);
                _session.MouseButton(MouseBtn.Left, false, x, y);
                _lastTapAt = now;
            }
            else if (!_moved && (_maxTouches == 2 || (_maxTouches == 1 && now - _downAt >= 600)))
            {
                _session!.MouseButton(MouseBtn.Right, true, x, y);
                _session.MouseButton(MouseBtn.Right, false, x, y);
            }
            InvalidateVisual();
        }

        /// <summary>Con lo zoom, la finestra scorre per tenere il puntatore in vista.</summary>
        private void FollowCursor()
        {
            if (_zoom == 0) return;
            var d = Dest();
            double sc = ViewScale;
            this.BringIntoView(new Rect(d.X + _cur.X * sc - 60, d.Y + _cur.Y * sc - 60, 120, 120));
        }

        // ------------------------------------------------------------------ tastiera

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                FullScreenToggleRequested?.Invoke();
                _session?.ReleaseAll();
                e.Handled = true;
                return;
            }
            if (!CanInput) { base.OnKeyDown(e); return; }
            int k = KeyMap.ToEvdev(e.PhysicalKey.ToString(), e.Key.ToString());
            if (k > 0) { _session!.Key(k, true); e.Handled = true; }
            else base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (!CanInput) { base.OnKeyUp(e); return; }
            int k = KeyMap.ToEvdev(e.PhysicalKey.ToString(), e.Key.ToString());
            if (k > 0) { _session!.Key(k, false); e.Handled = true; }
            else base.OnKeyUp(e);
        }

        protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLostFocus(e);
            // tasti rimasti premuti di qua (per esempio Alt+Tab): di là vanno rilasciati
            if (CanInput) _session!.ReleaseAll();
        }
    }
}
