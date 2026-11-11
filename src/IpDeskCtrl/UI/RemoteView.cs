using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
                _pinch = 1;
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
            if (_zoom == 0 && _pinch > 1.001)
            {
                dw *= _pinch; dh *= _pinch;
                double zx = dw <= b.Width ? (b.Width - dw) / 2 : Math.Clamp(_zx, b.Width - dw, 0);
                double zy = dh <= b.Height ? (b.Height - dh) / 2 : Math.Clamp(_zy, b.Height - dh, 0);
                return new Rect(zx, zy, dw, dh);
            }
            double x = Math.Max(0, (b.Width - dw) / 2), y = Math.Max(0, (b.Height - dh) / 2);
            return new Rect(x, y, dw, dh);
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            if (_bmp == null) return;
            var d = Dest();
            ctx.DrawImage(_bmp, new Rect(0, 0, _w, _h), d);

            // striscia di scorrimento lungo il bordo destro (telefono e tablet)
            if (_touchMode && Touch.ScrollStrip && Platform.IsMobile)
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(_g == G.Strip ? (byte)90 : (byte)40, 255, 255, 255)),
                    new Rect(Bounds.Width - StripWidth + 8, 8, StripWidth - 14, Math.Max(0, Bounds.Height - 16)), 6);

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
        // Due modi (TouchSettings.Mode):
        //   touchpad  il dito sposta il puntatore di quanto si muove (non lo «teletrasporta»): si punta con precisione
        //             anche con lo schermo rimpicciolito;
        //   diretto   si clicca dove si tocca, come su un tablet.
        // I gesti (tocco, doppio tocco a sinistra o a destra, due e tre dita, pressione lunga, tocca e tieni, scorrimento
        // con uno e due dita) fanno l'azione assegnata nelle impostazioni (profili pronti come in Sofatap).
        // Due dita che si allargano o stringono: zoom dell'immagine qui (non sul PC remoto) e spostamento.
        // Striscia lungo il bordo destro: scorre come la barra di scorrimento (dito in giù = pagina in giù).

        public TouchSettings Touch { get; set; } = new();

        /// <summary>Azione da fare fuori dalla vista: tastiera, dettatura, pannello scorciatoie aperto con un tocco.</summary>
        public event Action<string>? ActionRequested;
        /// <summary>Pannello scorciatoie aperto con la pressione lunga: il dito resta giù e sceglie scorrendo.</summary>
        public event Action? PanelHoldStarted;
        public event Action<double, double>? PanelHoldMoved;
        public event Action? PanelHoldEnded;

        private enum G { None, Pending, Multi, Pointer, Scroll1, Pointer2, Scroll2, Pinch, Strip, Drag, Panel, Consumed }

        private const double Slop = 10, StripWidth = 30;
        private readonly Dictionary<long, Point> _touches = new();
        private bool _touchMode, _moved, _second, _leftHalf;
        private G _g;
        private Point _cur, _downPos, _lastMid, _pinchMid0;
        private long _downAt;
        private int _maxTouches;
        private double _scrollAcc, _spread0, _pinch0;
        private Rect _rect0;
        private DispatcherTimer? _holdTimer, _tapTimer;
        private bool _tapPending, _lastTapImmediate, _tapLeftHalf;
        private long _lastTapAt;
        private Point _lastTapPos;

        // zoom con due dita (solo con «Adatta»): fattore e angolo in alto a sinistra dell'immagine ingrandita
        private double _pinch = 1, _zx, _zy;

        private bool Direct => Touch.Mode == "diretto";
        private double ViewScale => Dest().Width / Math.Max(1, _w);

        /// <summary>Riporta l'immagine intera (fine dello zoom con due dita).</summary>
        public void ResetPinch() { _pinch = 1; InvalidateVisual(); }

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
                _leftHalf = p.X < Bounds.Width / 2;
                _second = _lastTapAt > 0 && now - _lastTapAt <= Touch.DoubleTapMs + 150 && Distance(p, _lastTapPos) < 80;
                if (_second) _tapTimer?.Stop();
                if (Touch.ScrollStrip && Platform.IsMobile && p.X > Bounds.Width - StripWidth)
                {
                    _g = G.Strip; _scrollAcc = 0;
                    return;
                }
                _g = G.Pending;
                if (Direct) PointAt(p);
                StartHold();
            }
            else
            {
                _maxTouches = Math.Max(_maxTouches, _touches.Count);
                _holdTimer?.Stop();
                if (_g is G.Drag or G.Panel or G.Strip or G.Consumed) return;
                if (_g is G.Pending or G.Pointer or G.Scroll1)
                {
                    _g = G.Multi;
                    _lastMid = _pinchMid0 = Mid();
                    _spread0 = Spread();
                    _pinch0 = _pinch;
                    _rect0 = Dest();
                    _scrollAcc = 0;
                }
            }
        }

        private void StartHold()
        {
            _holdTimer?.Stop();
            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Touch.HoldMs) };
            _holdTimer.Tick += (_, _) => { _holdTimer?.Stop(); OnHold(); };
            _holdTimer.Start();
        }

        private void OnHold()
        {
            if (_touches.Count != 1 || _g != G.Pending || _moved) return;
            string a = Touch.Action(_second ? TouchGestures.TapHold : TouchGestures.LongPress);
            if (_second) { _lastTapAt = 0; _tapPending = false; }
            switch (a)
            {
                case TouchActions.Drag:
                    _g = G.Drag;
                    _session!.MouseButton(MouseBtn.Left, true, (int)_cur.X, (int)_cur.Y);
                    break;
                case TouchActions.Shortcuts:
                    _g = G.Panel;
                    PanelHoldStarted?.Invoke();
                    break;
                case TouchActions.None:
                    _g = G.Consumed;
                    break;
                default:
                    _g = G.Consumed;
                    Run(a);
                    break;
            }
        }

        private Point Mid()
        {
            double x = 0, y = 0;
            foreach (var t in _touches.Values) { x += t.X; y += t.Y; }
            return new Point(x / _touches.Count, y / _touches.Count);
        }

        private double Spread()
        {
            var m = Mid();
            double d = 0;
            foreach (var t in _touches.Values) d += Distance(t, m);
            return d / Math.Max(1, _touches.Count);
        }

        private void TouchMoved(long id, Point p)
        {
            if (!_touches.TryGetValue(id, out var last)) return;
            _touches[id] = p;
            if (_touches.Count == 1)
            {
                switch (_g)
                {
                    case G.Strip:
                        _scrollAcc += p.Y - last.Y;
                        while (Math.Abs(_scrollAcc) >= 20)
                        {
                            int dir = Math.Sign(_scrollAcc);
                            _session!.Wheel(0, -dir * 120);          // come la barra di scorrimento: dito in giù = pagina in giù
                            _scrollAcc -= dir * 20;
                        }
                        return;
                    case G.Panel: PanelHoldMoved?.Invoke(p.X - _downPos.X, p.Y - _downPos.Y); return;
                    case G.Drag: MovePointer(p, last); return;
                    case G.Pending:
                        if (Distance(p, _downPos) <= Slop) return;
                        _moved = true;
                        _holdTimer?.Stop();
                        if (_second && Touch.Action(TouchGestures.TapHold) == TouchActions.Drag)
                        {
                            // tocca e poi trascina: trascinamento, come sui touchpad
                            _lastTapAt = 0; _tapPending = false;
                            _g = G.Drag;
                            _session!.MouseButton(MouseBtn.Left, true, (int)_cur.X, (int)_cur.Y);
                            MovePointer(p, last);
                            return;
                        }
                        FlushPendingTap();
                        string a = Touch.Action(TouchGestures.Slide1);
                        _g = a == TouchActions.Move ? G.Pointer : a == TouchActions.Scroll ? G.Scroll1 : G.Consumed;
                        _scrollAcc = 0;
                        if (_g == G.Pointer) MovePointer(p, last);
                        return;
                    case G.Pointer: MovePointer(p, last); return;
                    case G.Scroll1: ScrollBy(p.Y - last.Y); return;
                    default: return;
                }
            }
            if (_g is G.Drag or G.Panel or G.Strip or G.Consumed) return;
            var m = Mid();
            if (_g == G.Multi)
            {
                double spread = Spread();
                if (Touch.PinchZoom && _zoom == 0 && _spread0 > 15 && Math.Abs(spread / _spread0 - 1) > 0.12) { _g = G.Pinch; _moved = true; }
                else if (Distance(m, _pinchMid0) > Slop)
                {
                    _moved = true;
                    string a = Touch.Action(TouchGestures.Slide2);
                    _g = a == TouchActions.Scroll ? G.Scroll2 : a == TouchActions.Move ? G.Pointer2 : G.Consumed;
                    _lastMid = m;
                    return;
                }
                else return;
            }
            switch (_g)
            {
                case G.Pinch: ApplyPinch(m, Spread()); break;
                case G.Scroll2: ScrollBy(m.Y - _lastMid.Y); break;
                case G.Pointer2: MovePointer(m, _lastMid); break;
            }
            _lastMid = m;
        }

        private void TouchReleased(long id)
        {
            _touches.Remove(id);
            if (_touches.Count > 0) return;
            _holdTimer?.Stop();
            long now = Environment.TickCount64;
            long dur = now - _downAt;
            switch (_g)
            {
                case G.Drag:
                    _session!.MouseButton(MouseBtn.Left, false, (int)_cur.X, (int)_cur.Y);
                    break;
                case G.Panel:
                    PanelHoldEnded?.Invoke();
                    break;
                case G.Pending when !_moved && _maxTouches == 1 && dur < Math.Max(Touch.HoldMs, 200):
                    OnTap(now);
                    break;
                case G.Multi when !_moved && dur < 450:
                    Run(Touch.Action(_maxTouches >= 3 ? TouchGestures.ThreeTap : TouchGestures.TwoTap));
                    break;
            }
            _g = G.None;
            InvalidateVisual();
        }

        /// <summary>Tocco singolo: subito, oppure dopo l'intervallo del doppio tocco se il doppio tocco fa altro.</summary>
        private void OnTap(long now)
        {
            if (_second)
            {
                _lastTapAt = 0;
                bool left = _tapLeftHalf;
                string d = Touch.Action(left ? TouchGestures.DoubleLeft : TouchGestures.DoubleRight);
                bool firstSent = !_tapPending && _lastTapImmediate;
                _tapPending = false;
                string tap = Touch.Action(TouchGestures.Tap);
                if (d == TouchActions.Double) { if (firstSent && tap == TouchActions.Left) Click(MouseBtn.Left); else Click(MouseBtn.Left, 2); }
                else if (d == TouchActions.None) { if (!firstSent) Run(tap); Run(tap); }
                else Run(d);
                return;
            }
            string t = Touch.Action(TouchGestures.Tap);
            string dbl = Touch.Action(_leftHalf ? TouchGestures.DoubleLeft : TouchGestures.DoubleRight);
            bool wait = dbl != TouchActions.None && !(dbl == TouchActions.Double && t == TouchActions.Left);
            _lastTapAt = now; _lastTapPos = _downPos; _tapLeftHalf = _leftHalf;
            if (!wait)
            {
                _tapPending = false; _lastTapImmediate = true;
                Run(t);
                return;
            }
            _lastTapImmediate = false;
            _tapPending = true;
            _tapTimer?.Stop();
            _tapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Touch.DoubleTapMs) };
            _tapTimer.Tick += (_, _) => { _tapTimer?.Stop(); FlushPendingTap(); };
            _tapTimer.Start();
        }

        private void FlushPendingTap()
        {
            if (!_tapPending) return;
            _tapPending = false;
            Run(Touch.Action(TouchGestures.Tap));
        }

        private void Click(int button, int count = 1)
        {
            int x = (int)_cur.X, y = (int)_cur.Y;
            for (int i = 0; i < count; i++)
            {
                _session!.MouseButton(button, true, x, y);
                _session.MouseButton(button, false, x, y);
            }
        }

        private void Run(string action)
        {
            if (_session == null) return;
            switch (action)
            {
                case TouchActions.Left: Click(MouseBtn.Left); break;
                case TouchActions.Right: Click(MouseBtn.Right); break;
                case TouchActions.Middle: Click(MouseBtn.Middle); break;
                case TouchActions.Double: Click(MouseBtn.Left, 2); break;
                case TouchActions.Enter: _session.Combo(28); break;
                case TouchActions.Escape: _session.Combo(KeyMap.Escape); break;
                case TouchActions.Keyboard:
                case TouchActions.Dictation:
                case TouchActions.Shortcuts:
                    ActionRequested?.Invoke(action);
                    break;
            }
            InvalidateVisual();
        }

        private void PointAt(Point local)
        {
            var ip = ToImage(local);
            if (ip == null) return;
            _cur = new Point(ip.Value.x, ip.Value.y);
            _session!.MouseMove(ip.Value.x, ip.Value.y);
            InvalidateVisual();
        }

        private void MovePointer(Point p, Point last)
        {
            if (Direct) { PointAt(p); return; }
            double k = 1.4 * Touch.PointerSpeed / Math.Max(0.05, ViewScale);       // un po' più veloce del dito
            _cur = new Point(Math.Clamp(_cur.X + (p.X - last.X) * k, 0, _w - 1), Math.Clamp(_cur.Y + (p.Y - last.Y) * k, 0, _h - 1));
            _session!.MouseMove((int)_cur.X, (int)_cur.Y);
            FollowCursor();
            InvalidateVisual();
        }

        /// <summary>Scorrimento con le dita: naturale = dita in giù, si torna su (come sul telefono).</summary>
        private void ScrollBy(double dy)
        {
            _scrollAcc += dy;
            while (Math.Abs(_scrollAcc) >= 24)
            {
                int dir = Math.Sign(_scrollAcc);
                _session!.Wheel(0, (Touch.NaturalScroll ? dir : -dir) * 120);
                _scrollAcc -= dir * 24;
            }
        }

        /// <summary>Zoom intorno alle dita e spostamento con il loro movimento.</summary>
        private void ApplyPinch(Point mid, double spread)
        {
            var b = Bounds.Size;
            double fit = Scale(b);
            _pinch = Math.Clamp(_pinch0 * spread / Math.Max(1, _spread0), 1, 8);
            double w = _w * fit * _pinch, h = _h * fit * _pinch;
            // punto dell'immagine che stava sotto le dita all'inizio: resta sotto le dita
            double ux = (_pinchMid0.X - _rect0.X) / Math.Max(1, _rect0.Width), uy = (_pinchMid0.Y - _rect0.Y) / Math.Max(1, _rect0.Height);
            _zx = mid.X - ux * w;
            _zy = mid.Y - uy * h;
            InvalidateVisual();
        }

        /// <summary>Con lo zoom, la vista si sposta per tenere il puntatore in vista.</summary>
        private void FollowCursor()
        {
            var d = Dest();
            double sc = ViewScale;
            if (_zoom == 0)
            {
                if (_pinch <= 1.001) return;
                double px = d.X + _cur.X * sc, py = d.Y + _cur.Y * sc, m = 40;
                if (px < m) _zx = d.X + (m - px); else if (px > Bounds.Width - m) _zx = d.X - (px - (Bounds.Width - m)); else _zx = d.X;
                if (py < m) _zy = d.Y + (m - py); else if (py > Bounds.Height - m) _zy = d.Y - (py - (Bounds.Height - m)); else _zy = d.Y;
                return;
            }
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
