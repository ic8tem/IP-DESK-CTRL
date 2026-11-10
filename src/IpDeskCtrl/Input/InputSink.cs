using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Input
{
    /// <summary>Pulsanti del mouse come viaggiano in rete.</summary>
    public static class MouseBtn { public const int Left = 0, Right = 1, Middle = 2, Back = 3, Forward = 4; }

    /// <summary>Mouse e tastiera simulati sul PC comandato. Coordinate del desktop (vedi <see cref="MonitorInfo"/>).</summary>
    public interface IInputSink : IDisposable
    {
        void MoveTo(int x, int y);
        void Button(int button, bool down);
        void Wheel(int dx, int dy);              // 120 = uno scatto
        void Key(int evdev, bool down);
        /// <summary>Scrive testo Unicode così com'è (tastiera del telefono), indipendente dalla disposizione dei tasti.</summary>
        void Text(string text);
        string Note { get; }
    }

    /// <summary>Per i sistemi che non sanno scrivere Unicode: lettere, cifre e pochi segni sulla posizione dei tasti.</summary>
    public static class AsciiKeys
    {
        public static void Type(IInputSink sink, string text)
        {
            foreach (char ch in text)
            {
                int k; bool shift = false;
                if (ch >= 'a' && ch <= 'z') k = Letter(ch);
                else if (ch >= 'A' && ch <= 'Z') { k = Letter(char.ToLowerInvariant(ch)); shift = true; }
                else if (ch >= '1' && ch <= '9') k = 2 + (ch - '1');
                else if (ch == '0') k = 11;
                else if (ch == ' ') k = 57;
                else if (ch == '\n' || ch == '\r') k = 28;
                else if (ch == '\t') k = 15;
                else if (ch == '.') k = 52;
                else if (ch == ',') k = 51;
                else continue;
                if (shift) sink.Key(KeyMap.LeftShift, true);
                sink.Key(k, true); sink.Key(k, false);
                if (shift) sink.Key(KeyMap.LeftShift, false);
            }
        }

        private static int Letter(char c) => "qwertyuiop".IndexOf(c) is int a and >= 0 ? 16 + a
            : "asdfghjkl".IndexOf(c) is int b and >= 0 ? 30 + b
            : 44 + "zxcvbnm".IndexOf(c);
    }

    public static class InputSinks
    {
        public static IInputSink Create()
        {
            if (Core.Platform.Input != null) return Core.Platform.Input();
            if (OperatingSystem.IsWindows()) return new WindowsInput();
            if (OperatingSystem.IsMacOS()) return new MacInput();
            if (LinuxSession.IsWayland && !LinuxSession.ForceX11) return new UinputInput();
            return new X11Input();
        }
    }

    /// <summary>Tiene traccia di tasti e pulsanti premuti, per poterli rilasciare tutti a fine sessione.</summary>
    public sealed class PressedTracker
    {
        private readonly HashSet<int> _keys = new(), _buttons = new();
        public void Key(int k, bool down) { lock (_keys) { if (down) _keys.Add(k); else _keys.Remove(k); } }
        public void Button(int b, bool down) { lock (_buttons) { if (down) _buttons.Add(b); else _buttons.Remove(b); } }

        public void ReleaseAll(IInputSink sink)
        {
            int[] keys, buttons;
            lock (_keys) { keys = new List<int>(_keys).ToArray(); _keys.Clear(); }
            lock (_buttons) { buttons = new List<int>(_buttons).ToArray(); _buttons.Clear(); }
            foreach (int k in keys) try { sink.Key(k, false); } catch { }
            foreach (int b in buttons) try { sink.Button(b, false); } catch { }
        }
    }

    // ===================================================================== Windows

    public sealed class WindowsInput : IInputSink
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);

        private const uint MOVE = 0x0001, LDOWN = 0x0002, LUP = 0x0004, RDOWN = 0x0008, RUP = 0x0010, MDOWN = 0x0020, MUP = 0x0040,
                           XDOWN = 0x0080, XUP = 0x0100, WHEEL = 0x0800, HWHEEL = 0x1000, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;
        private const uint KEY_EXTENDED = 0x0001, KEY_UP = 0x0002, KEY_SCANCODE = 0x0008;

        public string Note { get; private set; } = "";

        private void Send(params INPUT[] ins)
        {
            if (SendInput((uint)ins.Length, ins, Marshal.SizeOf<INPUT>()) == 0)
                Note = "Windows ha bloccato mouse e tastiera (finestra di un programma avviato come amministratore o schermata di sicurezza)";
            else Note = "";
        }

        private static INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0) =>
            new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };

        public void MoveTo(int x, int y)
        {
            int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int vw = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN)), vh = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
            int nx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
            int ny = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
            Send(Mouse(MOVE | ABSOLUTE | VIRTUALDESK, nx, ny));
        }

        public void Button(int button, bool down)
        {
            switch (button)
            {
                case MouseBtn.Left: Send(Mouse(down ? LDOWN : LUP)); break;
                case MouseBtn.Right: Send(Mouse(down ? RDOWN : RUP)); break;
                case MouseBtn.Middle: Send(Mouse(down ? MDOWN : MUP)); break;
                case MouseBtn.Back: Send(Mouse(down ? XDOWN : XUP, data: 1)); break;
                case MouseBtn.Forward: Send(Mouse(down ? XDOWN : XUP, data: 2)); break;
            }
        }

        public void Wheel(int dx, int dy)
        {
            if (dy != 0) Send(Mouse(WHEEL, data: unchecked((uint)dy)));
            if (dx != 0) Send(Mouse(HWHEEL, data: unchecked((uint)dx)));
        }

        public void Key(int evdev, bool down)
        {
            if (evdev == 119)   // Pausa: sequenza speciale, meglio col codice virtuale
            {
                Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x13, dwFlags = down ? 0u : KEY_UP } } });
                return;
            }
            int sc = KeyMap.ToWindows(evdev);
            if (sc < 0) return;
            uint flags = KEY_SCANCODE | (down ? 0u : KEY_UP) | ((sc & 0xFF00) == 0xE000 ? KEY_EXTENDED : 0u);
            Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = (ushort)(sc & 0xFF), dwFlags = flags } } });
        }

        private const uint KEY_UNICODE = 0x0004;

        public void Text(string text)
        {
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r') { Key(28, true); Key(28, false); continue; }
                Send(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEY_UNICODE } } },
                     new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEY_UNICODE | KEY_UP } } });
            }
        }

        public void Dispose() { }
    }

    // ===================================================================== Linux X11

    public sealed class X11Input : IInputSink
    {
        private readonly IntPtr _d;
        private readonly object _lock = new();
        public string Note { get; private set; } = "";

        public X11Input()
        {
            X11.InitThreads();
            _d = X11.Open();
            if (_d == IntPtr.Zero) Note = "Display X11 non disponibile: mouse e tastiera non comandabili";
        }

        private void Do(Action a)
        {
            if (_d == IntPtr.Zero) return;
            lock (_lock)
            {
                try { a(); X11.XFlush(_d); Note = ""; }
                catch (DllNotFoundException) { Note = "Manca la libreria XTest (sudo apt install libxtst6)"; }
            }
        }

        public void MoveTo(int x, int y) => Do(() => X11.XTestFakeMotionEvent(_d, -1, x, y, 0));

        public void Button(int button, bool down)
        {
            uint b = button switch { MouseBtn.Left => 1u, MouseBtn.Middle => 2u, MouseBtn.Right => 3u, MouseBtn.Back => 8u, MouseBtn.Forward => 9u, _ => 0u };
            if (b > 0) Do(() => X11.XTestFakeButtonEvent(_d, b, down ? 1 : 0, 0));
        }

        private int _accY, _accX;

        public void Wheel(int dx, int dy)
        {
            _accY += dy; _accX += dx;
            Do(() =>
            {
                while (Math.Abs(_accY) >= 120)
                {
                    uint b = _accY > 0 ? 4u : 5u;
                    X11.XTestFakeButtonEvent(_d, b, 1, 0); X11.XTestFakeButtonEvent(_d, b, 0, 0);
                    _accY -= Math.Sign(_accY) * 120;
                }
                while (Math.Abs(_accX) >= 120)
                {
                    uint b = _accX > 0 ? 7u : 6u;
                    X11.XTestFakeButtonEvent(_d, b, 1, 0); X11.XTestFakeButtonEvent(_d, b, 0, 0);
                    _accX -= Math.Sign(_accX) * 120;
                }
            });
        }

        // con XKB i codici dei tasti X sono quelli evdev + 8
        public void Key(int evdev, bool down) { if (evdev > 0) Do(() => X11.XTestFakeKeyEvent(_d, (uint)(evdev + 8), down ? 1 : 0, 0)); }

        private int _scratch = -1;

        /// <summary>
        /// Testo Unicode: come xdotool, si assegna per un attimo il carattere a un tasto libero della mappa
        /// della tastiera, lo si preme e poi si toglie.
        /// </summary>
        public void Text(string text)
        {
            if (_d == IntPtr.Zero) return;
            lock (_lock)
            {
                try
                {
                    if (_scratch < 0) _scratch = FindScratch();
                    if (_scratch <= 0) { Note = "Nessun tasto libero per scrivere il testo"; return; }
                    foreach (int cp in Codepoints(text))
                    {
                        if (cp == '\n' || cp == '\r') { X11.XTestFakeKeyEvent(_d, 36, 1, 0); X11.XTestFakeKeyEvent(_d, 36, 0, 0); X11.XSync(_d, 0); continue; }
                        nuint ks = cp >= 0x20 && cp <= 0xFF ? (nuint)cp : (nuint)(0x01000000 | cp);
                        X11.XChangeKeyboardMapping(_d, _scratch, 1, new[] { ks }, 1);
                        X11.XSync(_d, 0);
                        System.Threading.Thread.Sleep(8);
                        X11.XTestFakeKeyEvent(_d, (uint)_scratch, 1, 0);
                        X11.XTestFakeKeyEvent(_d, (uint)_scratch, 0, 0);
                        X11.XSync(_d, 0);
                        System.Threading.Thread.Sleep(8);
                    }
                    X11.XChangeKeyboardMapping(_d, _scratch, 1, new nuint[] { 0 }, 1);
                    X11.XFlush(_d);
                }
                catch (Exception ex) { Note = "Testo non scrivibile: " + ex.Message; }
            }
        }

        private int FindScratch()
        {
            X11.XDisplayKeycodes(_d, out int min, out int max);
            IntPtr map = X11.XGetKeyboardMapping(_d, (byte)min, max - min + 1, out int per);
            if (map == IntPtr.Zero) return 0;
            try
            {
                int size = IntPtr.Size;
                for (int kc = max; kc >= min; kc--)
                {
                    bool empty = true;
                    for (int j = 0; j < per && empty; j++)
                        if (Marshal.ReadIntPtr(map, ((kc - min) * per + j) * size) != IntPtr.Zero) empty = false;
                    if (empty) return kc;
                }
                return 0;
            }
            finally { X11.XFree(map); }
        }

        private static IEnumerable<int> Codepoints(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                int cp = char.ConvertToUtf32(s, i);
                if (char.IsHighSurrogate(s[i])) i++;
                yield return cp;
            }
        }

        public void Dispose() { lock (_lock) { if (_d != IntPtr.Zero) X11.XCloseDisplay(_d); } }
    }

    // ===================================================================== Linux Wayland (uinput)

    /// <summary>
    /// Wayland non permette di simulare il mouse a un programma qualunque: si crea un dispositivo virtuale
    /// del kernel (/dev/uinput) con puntatore assoluto, rotella e tasti. Il pacchetto .deb installa la regola
    /// udev che dà il permesso all'utente collegato al PC.
    /// </summary>
    public sealed unsafe class UinputInput : IInputSink
    {
        [DllImport("libc", SetLastError = true)] private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
        [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, byte* buf, nint count);
        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int ioctl_int(int fd, nuint req, int arg);
        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)] private static extern int ioctl_ptr(int fd, nuint req, byte* arg);

        private const nuint UI_SET_EVBIT = 0x40045564, UI_SET_KEYBIT = 0x40045565, UI_SET_RELBIT = 0x40045566, UI_SET_ABSBIT = 0x40045567,
                            UI_ABS_SETUP = 0x401c5504, UI_DEV_SETUP = 0x405c5503, UI_DEV_CREATE = 0x5501, UI_DEV_DESTROY = 0x5502;
        private const int EV_SYN = 0, EV_KEY = 1, EV_REL = 2, EV_ABS = 3, REL_HWHEEL = 6, REL_WHEEL = 8, ABS_X = 0, ABS_Y = 1;
        private const int BTN_LEFT = 0x110, BTN_RIGHT = 0x111, BTN_MIDDLE = 0x112, BTN_SIDE = 0x113, BTN_EXTRA = 0x114;

        private int _fd = -1;
        private int _w = 1920, _h = 1080, _ox, _oy;
        private readonly object _lock = new();
        public string Note { get; private set; } = "";

        public UinputInput()
        {
            var mons = new WaylandScreen().Monitors();
            var all = MonitorInfo.Union(mons);
            _ox = all.X; _oy = all.Y; _w = Math.Max(2, all.W); _h = Math.Max(2, all.H);
            _fd = open("/dev/uinput", 1 | 0x800 /* O_WRONLY | O_NONBLOCK */);
            if (_fd < 0)
            {
                Note = "Sessione Wayland: mouse e tastiera richiedono l'accesso a /dev/uinput. Installa il pacchetto .deb (che aggiunge la regola udev) " +
                       "e riavvia, oppure usa la sessione X11.";
                return;
            }
            ioctl_int(_fd, UI_SET_EVBIT, EV_KEY); ioctl_int(_fd, UI_SET_EVBIT, EV_REL); ioctl_int(_fd, UI_SET_EVBIT, EV_ABS); ioctl_int(_fd, UI_SET_EVBIT, EV_SYN);
            for (int k = 1; k < 256; k++) ioctl_int(_fd, UI_SET_KEYBIT, k);
            foreach (int b in new[] { BTN_LEFT, BTN_RIGHT, BTN_MIDDLE, BTN_SIDE, BTN_EXTRA }) ioctl_int(_fd, UI_SET_KEYBIT, b);
            ioctl_int(_fd, UI_SET_RELBIT, REL_WHEEL); ioctl_int(_fd, UI_SET_RELBIT, REL_HWHEEL);
            ioctl_int(_fd, UI_SET_ABSBIT, ABS_X); ioctl_int(_fd, UI_SET_ABSBIT, ABS_Y);
            AbsSetup(ABS_X, _w - 1); AbsSetup(ABS_Y, _h - 1);
            byte* setup = stackalloc byte[92];
            new Span<byte>(setup, 92).Clear();
            *(ushort*)(setup + 0) = 0x06;      // BUS_VIRTUAL
            *(ushort*)(setup + 2) = 0x1209;    // vendor
            *(ushort*)(setup + 4) = 0x4950;    // product
            *(ushort*)(setup + 6) = 1;
            byte[] name = System.Text.Encoding.ASCII.GetBytes("IP-DESK-CTRL input");
            for (int i = 0; i < name.Length; i++) setup[8 + i] = name[i];
            if (ioctl_ptr(_fd, UI_DEV_SETUP, setup) < 0 || ioctl_int(_fd, UI_DEV_CREATE, 0) < 0)
            {
                Note = "Impossibile creare il dispositivo di ingresso virtuale (kernel senza uinput?)";
                close(_fd); _fd = -1;
            }
        }

        private void AbsSetup(int code, int max)
        {
            byte* s = stackalloc byte[28];
            new Span<byte>(s, 28).Clear();
            *(ushort*)s = (ushort)code;
            *(int*)(s + 4 + 8) = max;          // value, minimum, MAXIMUM, fuzz, flat, resolution
            ioctl_ptr(_fd, UI_ABS_SETUP, s);
        }

        private void Emit(int type, int code, int value)
        {
            int size = IntPtr.Size * 2 + 8;     // struct input_event: timeval + u16 + u16 + s32
            byte* ev = stackalloc byte[size];
            new Span<byte>(ev, size).Clear();
            int o = IntPtr.Size * 2;
            *(ushort*)(ev + o) = (ushort)type;
            *(ushort*)(ev + o + 2) = (ushort)code;
            *(int*)(ev + o + 4) = value;
            write(_fd, ev, size);
        }

        private void Sync() => Emit(EV_SYN, 0, 0);

        public void MoveTo(int x, int y)
        {
            if (_fd < 0) return;
            lock (_lock) { Emit(EV_ABS, ABS_X, Math.Clamp(x - _ox, 0, _w - 1)); Emit(EV_ABS, ABS_Y, Math.Clamp(y - _oy, 0, _h - 1)); Sync(); }
        }

        public void Button(int button, bool down)
        {
            if (_fd < 0) return;
            int b = button switch { MouseBtn.Left => BTN_LEFT, MouseBtn.Right => BTN_RIGHT, MouseBtn.Middle => BTN_MIDDLE, MouseBtn.Back => BTN_SIDE, _ => BTN_EXTRA };
            lock (_lock) { Emit(EV_KEY, b, down ? 1 : 0); Sync(); }
        }

        public void Wheel(int dx, int dy)
        {
            if (_fd < 0) return;
            lock (_lock)
            {
                if (dy != 0) Emit(EV_REL, REL_WHEEL, Math.Sign(dy) * Math.Max(1, Math.Abs(dy) / 120));
                if (dx != 0) Emit(EV_REL, REL_HWHEEL, Math.Sign(dx) * Math.Max(1, Math.Abs(dx) / 120));
                Sync();
            }
        }

        public void Key(int evdev, bool down)
        {
            if (_fd < 0 || evdev <= 0 || evdev > 255) return;
            lock (_lock) { Emit(EV_KEY, evdev, down ? 1 : 0); Sync(); }
        }

        public void Text(string text) => AsciiKeys.Type(this, text);

        public void Dispose()
        {
            if (_fd < 0) return;
            ioctl_int(_fd, UI_DEV_DESTROY, 0);
            close(_fd); _fd = -1;
        }
    }

    // ===================================================================== macOS

    public sealed class MacInput : IInputSink
    {
        [DllImport(Mac.CG)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr src, uint type, Mac.CGPoint pos, uint button);
        [DllImport(Mac.CG)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr src, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
        [DllImport(Mac.CG)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr src, uint units, uint count, int w1, int w2, int w3);
        [DllImport(Mac.CG)] private static extern void CGEventPost(uint tap, IntPtr ev);
        [DllImport(Mac.CG)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
        [DllImport(Mac.CG)] private static extern void CGEventSetIntegerValueField(IntPtr ev, uint field, long value);
        [DllImport(Mac.AS)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
        [DllImport(Mac.CG)] private static extern void CGEventKeyboardSetUnicodeString(IntPtr ev, nuint len, ushort[] chars);

        private const uint LeftDown = 1, LeftUp = 2, RightDown = 3, RightUp = 4, Moved = 5, LeftDrag = 6, RightDrag = 7,
                           OtherDown = 25, OtherUp = 26, OtherDrag = 27;
        private const ulong FShift = 0x20000, FCtrl = 0x40000, FAlt = 0x80000, FCmd = 0x100000;

        private Mac.CGPoint _pos;
        private readonly bool[] _down = new bool[5];
        private ulong _flags;
        private int _clicks;
        private long _lastDownAt;
        private Mac.CGPoint _lastDownPos;
        private bool _checked;
        public string Note { get; private set; } = "";

        private void Check()
        {
            if (_checked) return;
            _checked = true;
            try
            {
                if (!AXIsProcessTrusted())
                {
                    Note = "Su questo Mac manca il permesso «Accessibilità» per IP-DESK-CTRL: mouse e tastiera dall'altro PC non funzionano. " +
                           "Impostazioni di Sistema › Privacy e sicurezza › Accessibilità.";
                    Mac.OpenSettings("Privacy_Accessibility");
                }
            }
            catch { }
        }

        private static void Post(IntPtr ev) { if (ev == IntPtr.Zero) return; CGEventPost(0, ev); Mac.CFRelease(ev); }

        public void MoveTo(int x, int y)
        {
            Check();
            _pos = new Mac.CGPoint { X = x, Y = y };
            uint type = _down[MouseBtn.Left] ? LeftDrag : _down[MouseBtn.Right] ? RightDrag : _down[MouseBtn.Middle] ? OtherDrag : Moved;
            uint btn = _down[MouseBtn.Right] ? 1u : _down[MouseBtn.Middle] ? 2u : 0u;
            IntPtr ev = CGEventCreateMouseEvent(IntPtr.Zero, type, _pos, btn);
            if (ev != IntPtr.Zero) CGEventSetFlags(ev, _flags);
            Post(ev);
        }

        public void Button(int button, bool down)
        {
            Check();
            if (button < 0 || button > 4) return;
            _down[button] = down;
            if (down)
            {
                long now = Environment.TickCount64;
                bool near = Math.Abs(_pos.X - _lastDownPos.X) < 5 && Math.Abs(_pos.Y - _lastDownPos.Y) < 5;
                _clicks = now - _lastDownAt < 450 && near ? Math.Min(_clicks + 1, 3) : 1;
                _lastDownAt = now; _lastDownPos = _pos;
            }
            (uint type, uint btn) = button switch
            {
                MouseBtn.Left => (down ? LeftDown : LeftUp, 0u),
                MouseBtn.Right => (down ? RightDown : RightUp, 1u),
                MouseBtn.Middle => (down ? OtherDown : OtherUp, 2u),
                MouseBtn.Back => (down ? OtherDown : OtherUp, 3u),
                _ => (down ? OtherDown : OtherUp, 4u)
            };
            IntPtr ev = CGEventCreateMouseEvent(IntPtr.Zero, type, _pos, btn);
            if (ev == IntPtr.Zero) return;
            CGEventSetIntegerValueField(ev, 1 /* kCGMouseEventClickState */, _clicks);
            CGEventSetFlags(ev, _flags);
            Post(ev);
        }

        public void Wheel(int dx, int dy)
        {
            Check();
            try
            {
                IntPtr ev = CGEventCreateScrollWheelEvent2(IntPtr.Zero, 1 /* righe */, 2, dy / 40, dx / 40, 0);
                Post(ev);
            }
            catch (EntryPointNotFoundException) { Note = "La rotella richiede macOS 13 o successivo"; }
        }

        public void Key(int evdev, bool down)
        {
            Check();
            ulong f = evdev switch
            {
                KeyMap.LeftShift or KeyMap.RightShift => FShift,
                KeyMap.LeftCtrl or KeyMap.RightCtrl => FCtrl,
                KeyMap.LeftAlt or KeyMap.RightAlt => FAlt,
                KeyMap.LeftMeta or KeyMap.RightMeta => FCmd,
                _ => 0
            };
            if (f != 0) _flags = down ? _flags | f : _flags & ~f;
            int vk = KeyMap.ToMac(evdev);
            if (vk < 0) return;
            IntPtr ev = CGEventCreateKeyboardEvent(IntPtr.Zero, (ushort)vk, down);
            if (ev == IntPtr.Zero) return;
            CGEventSetFlags(ev, _flags);
            Post(ev);
        }

        public void Text(string text)
        {
            Check();
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r') { Key(28, true); Key(28, false); continue; }
                foreach (bool down in new[] { true, false })
                {
                    IntPtr ev = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, down);
                    if (ev == IntPtr.Zero) continue;
                    CGEventKeyboardSetUnicodeString(ev, 1, new[] { (ushort)c });
                    Post(ev);
                }
            }
        }

        public void Dispose() { }
    }
}
