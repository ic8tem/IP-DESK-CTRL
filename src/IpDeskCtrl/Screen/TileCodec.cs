using System;
using System.Collections.Generic;
using SkiaSharp;

namespace IpDeskCtrl.Screen
{
    /// <summary>Un rettangolo codificato da mandare all'altro PC.</summary>
    public sealed record EncodedTile(int X, int Y, int W, int H, byte Format, byte[] Data);

    /// <summary>Livelli di qualità scelti dal visore.</summary>
    public static class Quality
    {
        public static readonly string[] Keys = { "auto", "alta", "media", "bassa" };
        public static readonly string[] Names = { "Automatica", "Alta", "Media", "Bassa (connessioni lente)" };

        /// <summary>(qualità JPEG del movimento, scala dell'immagine)</summary>
        public static (int q, double scale) Of(string key, int autoLevel) => key switch
        {
            "alta" => (85, 1.0),
            "media" => (65, 1.0),
            "bassa" => (45, 0.5),
            _ => autoLevel switch { 0 => (80, 1.0), 1 => (65, 1.0), 2 => (50, 1.0), _ => (45, 0.5) }
        };
    }

    /// <summary>
    /// Codifica a riquadri, come VNC «Tight»: l'immagine è divisa in riquadri da 64×64; si mandano solo
    /// quelli cambiati, raggruppati in strisce orizzontali, compressi in JPEG (SkiaSharp). Un riquadro
    /// fermo da più di un secondo e mandato a qualità bassa viene rimandato a qualità alta e senza
    /// sottocampionamento del colore, così il testo torna nitido («affinamento»).
    /// </summary>
    public sealed class TileEncoder
    {
        public const int Tile = 64;
        public const byte FormatJpeg = 1;
        private const int RefineQuality = 92;
        private const int MaxRefinePerFrame = 48;

        private byte[] _prev = Array.Empty<byte>();
        private int _w, _h, _cols, _rows;
        private int[] _q = Array.Empty<int>();
        private long[] _changedAt = Array.Empty<long>();

        public void Reset() { _w = 0; _h = 0; }

        public List<EncodedTile> Encode(Frame f, int quality, bool full)
        {
            var outp = new List<EncodedTile>();
            if (f.Width != _w || f.Height != _h)
            {
                _w = f.Width; _h = f.Height;
                _cols = (_w + Tile - 1) / Tile; _rows = (_h + Tile - 1) / Tile;
                _prev = new byte[f.Data.Length];
                _q = new int[_cols * _rows];
                _changedAt = new long[_cols * _rows];
                full = true;
            }
            long now = Environment.TickCount64;
            var changed = new bool[_cols * _rows];
            var refine = new bool[_cols * _rows];
            int refineCount = 0;
            for (int ty = 0; ty < _rows; ty++)
            {
                int y0 = ty * Tile, th = Math.Min(Tile, _h - y0);
                for (int tx = 0; tx < _cols; tx++)
                {
                    int i = ty * _cols + tx;
                    int x0 = tx * Tile, tw = Math.Min(Tile, _w - x0);
                    bool diff = full || !Same(f, x0, y0, tw, th);
                    if (diff) { changed[i] = true; _changedAt[i] = now; }
                    else if (_q[i] < RefineQuality && now - _changedAt[i] > 1000 && refineCount < MaxRefinePerFrame)
                    {
                        refine[i] = true; refineCount++;
                    }
                }
            }
            Emit(f, changed, quality, false, outp);
            Emit(f, refine, RefineQuality, true, outp);
            Buffer.BlockCopy(f.Data, 0, _prev, 0, f.Data.Length);
            return outp;
        }

        private bool Same(Frame f, int x, int y, int w, int h)
        {
            int bytes = w * 4;
            for (int r = 0; r < h; r++)
            {
                int o = (y + r) * f.Stride + x * 4;
                if (!f.Data.AsSpan(o, bytes).SequenceEqual(_prev.AsSpan(o, bytes))) return false;
            }
            return true;
        }

        /// <summary>Riquadri marcati → strisce di riquadri consecutivi sulla stessa riga → un JPEG per striscia.</summary>
        private void Emit(Frame f, bool[] mark, int quality, bool fine, List<EncodedTile> outp)
        {
            for (int ty = 0; ty < _rows; ty++)
            {
                int tx = 0;
                while (tx < _cols)
                {
                    if (!mark[ty * _cols + tx]) { tx++; continue; }
                    int start = tx;
                    while (tx < _cols && mark[ty * _cols + tx] && tx - start < 16) tx++;
                    int x0 = start * Tile, y0 = ty * Tile;
                    int w = Math.Min(tx * Tile, _w) - x0, h = Math.Min(Tile, _h - y0);
                    byte[]? jpg = Jpeg(f, x0, y0, w, h, quality, fine);
                    if (jpg == null) continue;
                    outp.Add(new EncodedTile(x0, y0, w, h, FormatJpeg, jpg));
                    for (int k = start; k < tx; k++) _q[ty * _cols + k] = quality;
                }
            }
        }

        private static unsafe byte[]? Jpeg(Frame f, int x, int y, int w, int h, int quality, bool fine)
        {
            fixed (byte* p = f.Data)
            {
                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var pix = new SKPixmap(info, (IntPtr)(p + y * f.Stride + x * 4), f.Stride);
                var opt = new SKJpegEncoderOptions(quality,
                    fine ? SKJpegEncoderDownsample.Downsample444 : SKJpegEncoderDownsample.Downsample420,
                    SKJpegEncoderAlphaOption.Ignore);
                using var data = pix.Encode(opt);
                return data?.ToArray();
            }
        }

        /// <summary>Dimezza l'immagine (media di 2×2 pixel) per le connessioni lente.</summary>
        public static void Half(Frame src, Frame dst)
        {
            int w = Math.Max(1, src.Width / 2), h = Math.Max(1, src.Height / 2);
            dst.Ensure(w, h);
            var s = src.Data; var d = dst.Data;
            for (int y = 0; y < h; y++)
            {
                int r0 = (y * 2) * src.Stride, r1 = Math.Min(y * 2 + 1, src.Height - 1) * src.Stride, o = y * dst.Stride;
                for (int x = 0; x < w; x++)
                {
                    int a = r0 + x * 8, b = a + 4, c = r1 + x * 8, e = c + 4;
                    if (x * 2 + 1 >= src.Width) { b = a; e = c; }
                    d[o] = (byte)((s[a] + s[b] + s[c] + s[e] + 2) >> 2);
                    d[o + 1] = (byte)((s[a + 1] + s[b + 1] + s[c + 1] + s[e + 1] + 2) >> 2);
                    d[o + 2] = (byte)((s[a + 2] + s[b + 2] + s[c + 2] + s[e + 2] + 2) >> 2);
                    d[o + 3] = 255;
                    o += 4;
                }
            }
        }
    }

    /// <summary>Lato visore: decodifica i riquadri in un'immagine BGRA completa.</summary>
    public sealed class TileDecoder
    {
        public readonly object Lock = new();
        public Frame Image { get; } = new();
        public int Version;     // cresce a ogni cambio di dimensione

        public void Start(int w, int h)
        {
            lock (Lock)
            {
                if (Image.Width == w && Image.Height == h) return;
                Image.Ensure(w, h);
                Array.Clear(Image.Data);
                Version++;
            }
        }

        public unsafe bool Apply(int x, int y, int w, int h, byte format, byte[] data)
        {
            if (format != TileEncoder.FormatJpeg) return false;
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            if (codec == null) return false;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var tmp = new byte[info.Width * info.Height * 4];
            fixed (byte* t = tmp)
            {
                var res = codec.GetPixels(info, (IntPtr)t);
                if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput) return false;
            }
            lock (Lock)
            {
                int cw = Math.Min(Math.Min(w, info.Width), Image.Width - x);
                int ch = Math.Min(Math.Min(h, info.Height), Image.Height - y);
                if (cw <= 0 || ch <= 0 || x < 0 || y < 0) return false;
                for (int r = 0; r < ch; r++)
                    Buffer.BlockCopy(tmp, r * info.Width * 4, Image.Data, (y + r) * Image.Stride + x * 4, cw * 4);
            }
            return true;
        }
    }
}
