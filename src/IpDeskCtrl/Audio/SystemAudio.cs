using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Audio
{
    /// <summary>Cattura dell'audio che il PC comandato sta suonando (non del microfono).</summary>
    public interface ISystemAudioCapture : IDisposable
    {
        /// <summary>onFrame riceve blocchi da 20 ms a 32 kHz stereo interleaved.</summary>
        bool Start(Action<short[]> onFrame);
        string Note { get; }
    }

    /// <summary>Riproduzione sul PC del visore.</summary>
    public interface IAudioPlayer : IDisposable
    {
        void Play(short[] stereo32k);
        string Note { get; }
    }

    public static class SystemAudio
    {
        public static ISystemAudioCapture CreateCapture()
        {
            if (Core.Platform.AudioCapture != null) return Core.Platform.AudioCapture();
            if (OperatingSystem.IsWindows()) return new WasapiLoopback();
            if (OperatingSystem.IsMacOS()) return new MacAudio.Capture();
            return new PulseCapture();
        }

        public static IAudioPlayer CreatePlayer()
        {
            if (Core.Platform.AudioPlayer != null) return Core.Platform.AudioPlayer();
            if (OperatingSystem.IsWindows()) return new WaveOutPlayer();
            if (OperatingSystem.IsMacOS()) return new MacAudio.Player();
            return new PulsePlayer();
        }
    }

    // ===================================================================== Windows (NAudio)

    /// <summary>Windows: WASAPI in «loopback» sull'uscita predefinita, cioè quello che si sente dalle casse.</summary>
    public sealed class WasapiLoopback : ISystemAudioCapture
    {
        private NAudio.Wave.WasapiLoopbackCapture? _cap;
        private Resampler? _rs;
        public string Note { get; private set; } = "";

        public bool Start(Action<short[]> onFrame)
        {
            try
            {
                var cap = new NAudio.Wave.WasapiLoopbackCapture();
                var fmt = cap.WaveFormat;
                var rs = new Resampler(fmt.SampleRate, onFrame);
                _rs = rs;
                int ch = fmt.Channels;
                bool isFloat = fmt.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat
                               || (fmt.Encoding == NAudio.Wave.WaveFormatEncoding.Extensible && fmt.BitsPerSample == 32);
                cap.DataAvailable += (_, e) =>
                {
                    try
                    {
                        int bps = fmt.BitsPerSample / 8;
                        int frames = e.BytesRecorded / (bps * ch);
                        var st = new short[frames * 2];
                        for (int i = 0; i < frames; i++)
                        {
                            int o = i * ch * bps;
                            short l = Sample(e.Buffer, o, isFloat, bps);
                            short r = ch > 1 ? Sample(e.Buffer, o + bps, isFloat, bps) : l;
                            st[i * 2] = l; st[i * 2 + 1] = r;
                        }
                        rs.Push(st);
                    }
                    catch { }
                };
                cap.StartRecording();
                _cap = cap;
                Note = "";
                return true;
            }
            catch (Exception ex)
            {
                Note = "Audio di Windows non catturabile: " + ex.Message;
                return false;
            }
        }

        private static short Sample(byte[] b, int o, bool isFloat, int bps)
        {
            if (isFloat && bps == 4) return (short)Math.Clamp(BitConverter.ToSingle(b, o) * 32767f, -32768f, 32767f);
            if (bps == 2) return BitConverter.ToInt16(b, o);
            if (bps == 3) return (short)(b[o + 1] | (b[o + 2] << 8));
            if (bps == 4) return (short)(BitConverter.ToInt32(b, o) >> 16);
            return 0;
        }

        public void Dispose()
        {
            try { _cap?.StopRecording(); } catch { }
            try { _cap?.Dispose(); } catch { }
            _cap = null;
        }
    }

    /// <summary>
    /// Windows: riproduce l'audio del PC remoto sull'uscita predefinita con WASAPI (si adatta da solo alla
    /// frequenza della scheda); se non va prova le uscite waveOut una per una. Se il PC non ha uscite attive
    /// (casse o cuffie scollegate, sessione di Desktop remoto) riprova ogni 5 secondi e lo dice chiaramente.
    /// Se l'uscita sparisce durante il collegamento (cuffie USB staccate) si riapre da sola.
    /// </summary>
    public sealed class WaveOutPlayer : IAudioPlayer
    {
        private NAudio.Wave.IWavePlayer? _out;
        private readonly NAudio.Wave.BufferedWaveProvider _buf;
        private readonly object _lock = new();
        private long _retryAt;
        private bool _disposed;
        public string Note { get; private set; } = "";

        public WaveOutPlayer()
        {
            _buf = new NAudio.Wave.BufferedWaveProvider(new NAudio.Wave.WaveFormat(Adpcm.Rate, 16, Adpcm.Channels))
            {
                BufferDuration = TimeSpan.FromSeconds(1),
                DiscardOnBufferOverflow = true
            };
            Open();
        }

        private void Open()
        {
            lock (_lock)
            {
                if (_disposed || _out != null) return;
                string why = "";
                // 1) WASAPI sull'uscita predefinita
                try
                {
                    var w = new NAudio.Wave.WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100);
                    w.Init(_buf);
                    w.PlaybackStopped += OnStopped;
                    w.Play();
                    _out = w;
                    Note = "";
                    Core.SessionLog.Write("AUDIO riproduzione con WASAPI sull'uscita predefinita");
                    return;
                }
                catch (Exception ex) { why = ex.Message; }
                // 2) waveOut: prima il mappatore di Windows, poi ogni uscita
                for (int dev = -1; dev < OutDevices(); dev++)
                {
                    try
                    {
                        var w = new NAudio.Wave.WaveOutEvent { DeviceNumber = dev, DesiredLatency = 120 };
                        w.Init(_buf);
                        w.PlaybackStopped += OnStopped;
                        w.Play();
                        _out = w;
                        Note = "";
                        Core.SessionLog.Write($"AUDIO riproduzione con waveOut, uscita {dev}");
                        return;
                    }
                    catch (Exception ex) { why = ex.Message; }
                }
                Note = OutDevices() == 0
                    ? "Su questo PC non c'è un'uscita audio attiva (casse o cuffie scollegate o disattivate): l'audio del PC remoto partirà appena ne colleghi una."
                    : "Uscita audio di questo PC non apribile (" + why + "): riprovo tra poco.";
                Core.SessionLog.Write("AUDIO " + Note);
                _retryAt = Environment.TickCount64 + 5000;
            }
        }

        [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern int waveOutGetNumDevs();

        private static int OutDevices()
        {
            try { return waveOutGetNumDevs(); } catch { return 0; }
        }

        private void OnStopped(object? sender, NAudio.Wave.StoppedEventArgs e)
        {
            if (e.Exception == null) return;
            // uscita persa (dispositivo staccato o cambiato): la richiudo e la riapro al prossimo pacchetto
            Core.SessionLog.Write("AUDIO uscita persa: " + e.Exception.Message);
            lock (_lock)
            {
                try { _out?.Dispose(); } catch { }
                _out = null;
                _retryAt = Environment.TickCount64 + 1000;
            }
        }

        public void Play(short[] s)
        {
            if (_out == null)
            {
                if (Environment.TickCount64 < _retryAt) return;
                Open();
                if (_out == null) return;
            }
            // ritardo che cresce (rete a scatti): si butta via e si riparte
            if (_buf.BufferedDuration > TimeSpan.FromMilliseconds(350)) _buf.ClearBuffer();
            var b = new byte[s.Length * 2];
            Buffer.BlockCopy(s, 0, b, 0, b.Length);
            _buf.AddSamples(b, 0, b.Length);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                try { _out?.Stop(); } catch { }
                try { _out?.Dispose(); } catch { }
                _out = null;
            }
        }
    }

    // ===================================================================== Linux (PulseAudio / PipeWire)

    /// <summary>
    /// Linux e Raspberry Pi: «parec» sul monitor dell'uscita predefinita (PulseAudio o PipeWire con
    /// pipewire-pulse, presenti di serie su Raspberry Pi OS, Ubuntu, Debian e Fedora).
    /// </summary>
    public sealed class PulseCapture : ISystemAudioCapture
    {
        private Process? _p;
        private Thread? _t;
        public string Note { get; private set; } = "";

        public bool Start(Action<short[]> onFrame)
        {
            if (!WaylandScreen.Which("parec"))
            {
                Note = "Per mandare l'audio serve «parec» (sudo apt install pulseaudio-utils)";
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo("parec", $"-d @DEFAULT_MONITOR@ --format=s16le --rate={Adpcm.Rate} --channels=2 --latency-msec=20 --raw")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                _p = Process.Start(psi)!;
                var p = _p;
                _t = new Thread(() =>
                {
                    var s = p.StandardOutput.BaseStream;
                    var buf = new byte[Adpcm.FrameSamples * 4];
                    try
                    {
                        while (true)
                        {
                            int got = 0;
                            while (got < buf.Length)
                            {
                                int n = s.Read(buf, got, buf.Length - got);
                                if (n <= 0) return;
                                got += n;
                            }
                            var frame = new short[Adpcm.FrameSamples * 2];
                            Buffer.BlockCopy(buf, 0, frame, 0, buf.Length);
                            onFrame(frame);
                        }
                    }
                    catch { }
                }) { IsBackground = true, Name = "parec" };
                _t.Start();
                Note = "";
                return true;
            }
            catch (Exception ex)
            {
                Note = "Audio non catturabile: " + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            try { if (_p != null && !_p.HasExited) _p.Kill(); } catch { }
            _p?.Dispose();
            _p = null;
        }
    }

    public sealed class PulsePlayer : IAudioPlayer
    {
        private readonly Process? _p;
        private readonly BlockingCollection<short[]> _q = new(new ConcurrentQueue<short[]>());
        private readonly Thread? _t;
        public string Note { get; } = "";

        public PulsePlayer()
        {
            string exe, args;
            if (WaylandScreen.Which("pacat")) { exe = "pacat"; args = $"--playback --format=s16le --rate={Adpcm.Rate} --channels=2 --latency-msec=80 --raw"; }
            else if (WaylandScreen.Which("aplay")) { exe = "aplay"; args = $"-q -t raw -f S16_LE -r {Adpcm.Rate} -c 2 -B 100000"; }
            else { Note = "Per sentire l'audio serve «pacat» (sudo apt install pulseaudio-utils) o «aplay»"; return; }
            try
            {
                _p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                });
                var p = _p!;
                _t = new Thread(() =>
                {
                    var s = p.StandardInput.BaseStream;
                    try
                    {
                        foreach (var f in _q.GetConsumingEnumerable())
                        {
                            var b = new byte[f.Length * 2];
                            Buffer.BlockCopy(f, 0, b, 0, b.Length);
                            s.Write(b, 0, b.Length);
                            s.Flush();
                        }
                    }
                    catch { }
                }) { IsBackground = true, Name = "pacat" };
                _t.Start();
            }
            catch (Exception ex) { Note = "Uscita audio non disponibile: " + ex.Message; }
        }

        public void Play(short[] s)
        {
            if (_p == null) return;
            while (_q.Count > 15) _q.TryTake(out _);      // oltre 300 ms di ritardo si butta via
            _q.Add(s);
        }

        public void Dispose()
        {
            _q.CompleteAdding();
            try { if (_p != null && !_p.HasExited) _p.Kill(); } catch { }
            _p?.Dispose();
        }
    }

    // ===================================================================== macOS (AudioQueue)

    /// <summary>
    /// macOS non permette di catturare direttamente l'audio del sistema: serve un dispositivo virtuale come
    /// BlackHole (gratuito, open source). Se è presente, il programma registra da lì; per sentire anche sul Mac
    /// si crea un «dispositivo a uscita multipla» (Configurazione MIDI Audio) con casse + BlackHole.
    /// </summary>
    public static class MacAudio
    {
        private const string CA = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
        private const string AT = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
        private const uint Utf8 = 0x08000100;

        private static uint FourCC(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

        [StructLayout(LayoutKind.Sequential)] private struct Address { public uint Selector, Scope, Element; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ASBD
        {
            public double SampleRate;
            public uint FormatID, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct AQBuffer
        {
            public uint Capacity;
            public IntPtr Data;
            public uint ByteSize;
            public IntPtr UserData;
            public uint PacketDescriptionCapacity;
            public IntPtr PacketDescriptions;
            public uint PacketDescriptionCount;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InputCallback(IntPtr user, IntPtr aq, IntPtr buffer, IntPtr startTime, uint numPackets, IntPtr descs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OutputCallback(IntPtr user, IntPtr aq, IntPtr buffer);

        [DllImport(CA)] private static extern int AudioObjectGetPropertyDataSize(uint id, ref Address a, uint qs, IntPtr q, out uint size);
        [DllImport(CA)] private static extern int AudioObjectGetPropertyData(uint id, ref Address a, uint qs, IntPtr q, ref uint size, IntPtr data);
        [DllImport(Mac.CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string s, uint enc);
        [DllImport(Mac.CF)] private static extern byte CFStringGetCString(IntPtr cf, byte[] buf, nint size, uint enc);
        [DllImport(AT)] private static extern int AudioQueueNewInput(ref ASBD fmt, InputCallback cb, IntPtr user, IntPtr runLoop, IntPtr mode, uint flags, out IntPtr aq);
        [DllImport(AT)] private static extern int AudioQueueNewOutput(ref ASBD fmt, OutputCallback cb, IntPtr user, IntPtr runLoop, IntPtr mode, uint flags, out IntPtr aq);
        [DllImport(AT)] private static extern int AudioQueueAllocateBuffer(IntPtr aq, uint bytes, out IntPtr buffer);
        [DllImport(AT)] private static extern int AudioQueueEnqueueBuffer(IntPtr aq, IntPtr buffer, uint numDescs, IntPtr descs);
        [DllImport(AT)] private static extern int AudioQueueStart(IntPtr aq, IntPtr startTime);
        [DllImport(AT)] private static extern int AudioQueueStop(IntPtr aq, byte immediate);
        [DllImport(AT)] private static extern int AudioQueueDispose(IntPtr aq, byte immediate);
        [DllImport(AT)] private static extern int AudioQueueSetProperty(IntPtr aq, uint id, ref IntPtr data, uint size);

        private static readonly int DataSizeOffset = Marshal.OffsetOf<AQBuffer>(nameof(AQBuffer.ByteSize)).ToInt32();
        private const uint FrameBytes = Adpcm.FrameSamples * 4;

        private static ASBD Format() => new()
        {
            SampleRate = Adpcm.Rate,
            FormatID = FourCC("lpcm"),
            FormatFlags = 4 | 8,  // intero con segno | impacchettato
            BytesPerPacket = 4, FramesPerPacket = 1, BytesPerFrame = 4, ChannelsPerFrame = 2, BitsPerChannel = 16
        };

        private static string CfToString(IntPtr cf)
        {
            if (cf == IntPtr.Zero) return "";
            var buf = new byte[512];
            string s = CFStringGetCString(cf, buf, buf.Length, Utf8) != 0 ? System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0') : "";
            Mac.CFRelease(cf);
            return s;
        }

        private static string StringProp(uint id, string sel)
        {
            var a = new Address { Selector = FourCC(sel), Scope = FourCC("glob"), Element = 0 };
            uint size = (uint)IntPtr.Size;
            IntPtr p = Marshal.AllocHGlobal(IntPtr.Size);
            try { return AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, p) == 0 ? CfToString(Marshal.ReadIntPtr(p)) : ""; }
            finally { Marshal.FreeHGlobal(p); }
        }

        /// <summary>UID del primo dispositivo d'ingresso che si chiama come un cavo audio virtuale (BlackHole, Loopback, Soundflower).</summary>
        private static string? FindLoopbackUid()
        {
            var a = new Address { Selector = FourCC("dev#"), Scope = FourCC("glob"), Element = 0 };
            if (AudioObjectGetPropertyDataSize(1, ref a, 0, IntPtr.Zero, out uint size) != 0 || size == 0) return null;
            IntPtr p = Marshal.AllocHGlobal((int)size);
            try
            {
                if (AudioObjectGetPropertyData(1, ref a, 0, IntPtr.Zero, ref size, p) != 0) return null;
                for (int i = 0; i < size / 4; i++)
                {
                    uint id = (uint)Marshal.ReadInt32(p, i * 4);
                    string name = StringProp(id, "lnam");
                    if (new[] { "BlackHole", "Loopback", "Soundflower" }.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        string uid = StringProp(id, "uid ");
                        if (uid.Length > 0) return uid;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(p); }
            return null;
        }

        public sealed class Capture : ISystemAudioCapture
        {
            private IntPtr _q;
            private InputCallback? _cb;
            private Action<short[]>? _on;
            public string Note { get; private set; } = "";

            public bool Start(Action<short[]> onFrame)
            {
                try
                {
                    string? uid = FindLoopbackUid();
                    if (uid == null)
                    {
                        Note = "Per mandare l'audio del Mac installa BlackHole 2ch (gratuito) e crea un «dispositivo a uscita multipla» con casse + BlackHole";
                        return false;
                    }
                    _on = onFrame;
                    _cb = OnInput;
                    var fmt = Format();
                    if (AudioQueueNewInput(ref fmt, _cb, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _q) != 0) { Note = "Ingresso audio non apribile"; return false; }
                    IntPtr cf = CFStringCreateWithCString(IntPtr.Zero, uid, Utf8);
                    AudioQueueSetProperty(_q, FourCC("aqcd"), ref cf, (uint)IntPtr.Size);
                    Mac.CFRelease(cf);
                    for (int i = 0; i < 4; i++)
                        if (AudioQueueAllocateBuffer(_q, FrameBytes, out IntPtr b) == 0) AudioQueueEnqueueBuffer(_q, b, 0, IntPtr.Zero);
                    if (AudioQueueStart(_q, IntPtr.Zero) != 0) { Note = "Permesso «Microfono» negato per IP-DESK-CTRL"; return false; }
                    Note = "";
                    return true;
                }
                catch (Exception ex) { Note = "CoreAudio: " + ex.Message; return false; }
            }

            private void OnInput(IntPtr user, IntPtr aq, IntPtr buffer, IntPtr startTime, uint numPackets, IntPtr descs)
            {
                try
                {
                    var b = Marshal.PtrToStructure<AQBuffer>(buffer);
                    var f = new short[Adpcm.FrameSamples * 2];
                    Marshal.Copy(b.Data, f, 0, Math.Min((int)b.ByteSize / 2, f.Length));
                    _on?.Invoke(f);
                }
                catch { }
                if (_q != IntPtr.Zero) AudioQueueEnqueueBuffer(aq, buffer, 0, IntPtr.Zero);
            }

            public void Dispose()
            {
                var q = _q; _q = IntPtr.Zero;
                try { if (q != IntPtr.Zero) { AudioQueueStop(q, 1); AudioQueueDispose(q, 1); } } catch { }
            }
        }

        public sealed class Player : IAudioPlayer
        {
            private IntPtr _q;
            private readonly OutputCallback _cb;
            private readonly ConcurrentQueue<short[]> _queue = new();
            private readonly short[] _silence = new short[Adpcm.FrameSamples * 2];
            public string Note { get; } = "";

            public Player()
            {
                _cb = OnOutput;
                try
                {
                    var fmt = Format();
                    if (AudioQueueNewOutput(ref fmt, _cb, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _q) != 0) { Note = "Uscita audio non apribile"; return; }
                    for (int i = 0; i < 4; i++)
                        if (AudioQueueAllocateBuffer(_q, FrameBytes, out IntPtr b) == 0) OnOutput(IntPtr.Zero, _q, b);
                    AudioQueueStart(_q, IntPtr.Zero);
                }
                catch (Exception ex) { Note = "CoreAudio: " + ex.Message; }
            }

            public void Play(short[] s)
            {
                while (_queue.Count > 15) _queue.TryDequeue(out _);
                _queue.Enqueue(s);
            }

            private void OnOutput(IntPtr user, IntPtr aq, IntPtr buffer)
            {
                try
                {
                    var f = _queue.TryDequeue(out var x) ? x : _silence;
                    var b = Marshal.PtrToStructure<AQBuffer>(buffer);
                    Marshal.Copy(f, 0, b.Data, Math.Min(f.Length, (int)FrameBytes / 2));
                    Marshal.WriteInt32(buffer, DataSizeOffset, (int)FrameBytes);
                    AudioQueueEnqueueBuffer(aq, buffer, 0, IntPtr.Zero);
                }
                catch { }
            }

            public void Dispose()
            {
                var q = _q; _q = IntPtr.Zero;
                try { if (q != IntPtr.Zero) { AudioQueueStop(q, 1); AudioQueueDispose(q, 1); } } catch { }
            }
        }
    }
}
