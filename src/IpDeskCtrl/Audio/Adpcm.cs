using System;

namespace IpDeskCtrl.Audio
{
    /// <summary>
    /// Formato dell'audio in rete: 32 kHz, stereo, IMA ADPCM a 4 bit (256 kbit/s), pacchetti da 20 ms.
    /// Ogni pacchetto porta lo stato iniziale del codificatore, quindi un pacchetto perso non rovina i seguenti.
    /// Pacchetto: per canale predittore(i16) + indice(u8), poi i campioni a coppie L/R, due per byte.
    /// </summary>
    public static class Adpcm
    {
        public const int Rate = 32000, Channels = 2, FrameSamples = 640;   // 20 ms per canale

        private static readonly int[] Steps =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
            1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
            7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };
        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        public sealed class State { public int Pred, Index; }

        private static int EncodeOne(State s, int sample)
        {
            int step = Steps[s.Index];
            int diff = sample - s.Pred;
            int code = 0;
            if (diff < 0) { code = 8; diff = -diff; }
            int delta = step >> 3;
            if (diff >= step) { code |= 4; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 2; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 1; delta += step; }
            s.Pred = Math.Clamp((code & 8) != 0 ? s.Pred - delta : s.Pred + delta, -32768, 32767);
            s.Index = Math.Clamp(s.Index + IndexTable[code], 0, 88);
            return code;
        }

        private static int DecodeOne(State s, int code)
        {
            int step = Steps[s.Index];
            int delta = step >> 3;
            if ((code & 4) != 0) delta += step;
            if ((code & 2) != 0) delta += step >> 1;
            if ((code & 1) != 0) delta += step >> 2;
            s.Pred = Math.Clamp((code & 8) != 0 ? s.Pred - delta : s.Pred + delta, -32768, 32767);
            s.Index = Math.Clamp(s.Index + IndexTable[code], 0, 88);
            return s.Pred;
        }

        /// <summary>interleaved: L,R,L,R… (FrameSamples coppie).</summary>
        public static byte[] Encode(short[] interleaved, State l, State r)
        {
            int pairs = interleaved.Length / 2;
            byte[] o = new byte[6 + pairs];
            o[0] = (byte)l.Pred; o[1] = (byte)(l.Pred >> 8); o[2] = (byte)l.Index;
            o[3] = (byte)r.Pred; o[4] = (byte)(r.Pred >> 8); o[5] = (byte)r.Index;
            for (int i = 0; i < pairs; i++)
            {
                int a = EncodeOne(l, interleaved[i * 2]);
                int b = EncodeOne(r, interleaved[i * 2 + 1]);
                o[6 + i] = (byte)(a | (b << 4));
            }
            return o;
        }

        public static short[] Decode(ReadOnlySpan<byte> d)
        {
            if (d.Length < 6) return Array.Empty<short>();
            var l = new State { Pred = (short)(d[0] | d[1] << 8), Index = Math.Clamp((int)d[2], 0, 88) };
            var r = new State { Pred = (short)(d[3] | d[4] << 8), Index = Math.Clamp((int)d[5], 0, 88) };
            int pairs = d.Length - 6;
            var o = new short[pairs * 2];
            for (int i = 0; i < pairs; i++)
            {
                byte b = d[6 + i];
                o[i * 2] = (short)DecodeOne(l, b & 15);
                o[i * 2 + 1] = (short)DecodeOne(r, b >> 4);
            }
            return o;
        }
    }

    /// <summary>Converte un flusso stereo a qualunque frequenza in blocchi da 20 ms a 32 kHz (interpolazione lineare).</summary>
    public sealed class Resampler
    {
        private readonly double _ratio;   // campioni d'ingresso per campione d'uscita
        private double _pos;
        private short _lastL, _lastR;
        private readonly short[] _frame = new short[Adpcm.FrameSamples * 2];
        private int _fill;
        private readonly Action<short[]> _out;

        public Resampler(int inRate, Action<short[]> onFrame)
        {
            _ratio = inRate / (double)Adpcm.Rate;
            _out = onFrame;
        }

        /// <summary>Campioni stereo interleaved alla frequenza d'ingresso.</summary>
        public void Push(ReadOnlySpan<short> stereo)
        {
            int n = stereo.Length / 2;
            while (_pos < n)
            {
                int i = (int)_pos;
                double f = _pos - i;
                short l0 = i == 0 ? _lastL : stereo[(i - 1) * 2], r0 = i == 0 ? _lastR : stereo[(i - 1) * 2 + 1];
                short l1 = stereo[i * 2], r1 = stereo[i * 2 + 1];
                // _pos misura dal campione precedente al blocco: 0 = ultimo del blocco prima
                _frame[_fill++] = (short)(l0 + (l1 - l0) * f);
                _frame[_fill++] = (short)(r0 + (r1 - r0) * f);
                if (_fill == _frame.Length) { _out((short[])_frame.Clone()); _fill = 0; }
                _pos += _ratio;
            }
            _pos -= n;
            if (n > 0) { _lastL = stereo[(n - 1) * 2]; _lastR = stereo[(n - 1) * 2 + 1]; }
        }
    }
}
