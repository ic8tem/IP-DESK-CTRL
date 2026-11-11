using System;
using System.Collections.Generic;
using System.Text;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Codice QR scritto da zero (nessuna libreria): modalità byte, correzione d'errore M, versioni 1–10
    /// (fino a 213 byte, ben oltre i 17 di «ipdesk:1234567890»). Segue la norma ISO/IEC 18004:
    /// codifica dei dati, Reed-Solomon su GF(256), blocchi intrecciati, motivi fissi, maschera scelta
    /// con le regole di penalità 1, 2 e 4.
    /// </summary>
    public sealed class QrCode
    {
        private static readonly int[] EccPerBlock = { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        private static readonly int[] Blocks = { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        public int Version { get; }
        public int Size { get; }
        private readonly bool[,] _m;      // [y, x]: true = scuro
        private readonly bool[,] _fn;     // moduli dei motivi fissi

        public bool this[int x, int y] => _m[y, x];

        /// <summary>Testo del QR del numero di un PC: «ipdesk:1234567890».</summary>
        public static string ForNumber(string number) => "ipdesk:" + Identity.Clean(number);

        /// <summary>Numero di 10 cifre da un QR letto («ipdesk:…» o solo cifre); null se non è un numero.</summary>
        public static string? NumberFrom(string? scanned)
        {
            if (string.IsNullOrWhiteSpace(scanned)) return null;
            string s = scanned.Trim();
            if (s.StartsWith("ipdesk:", StringComparison.OrdinalIgnoreCase)) s = s[7..];
            string n = Identity.Clean(s);
            return n.Length == 10 ? n : null;
        }

        public QrCode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int ver = 0;
            for (int v = 1; v <= 10; v++)
                if (4 + (v < 10 ? 8 : 16) + 8 * data.Length <= DataCodewords(v) * 8) { ver = v; break; }
            if (ver == 0) throw new ArgumentException("Testo troppo lungo per il codice QR");
            Version = ver;
            Size = ver * 4 + 17;
            _m = new bool[Size, Size];
            _fn = new bool[Size, Size];

            byte[] codewords = Interleave(EncodeData(data, ver), ver);
            DrawFunctionPatterns();
            DrawCodewords(codewords);

            int best = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                int p = Penalty();
                if (p < bestPenalty) { best = mask; bestPenalty = p; }
                ApplyMask(mask);       // la maschera è uno XOR: applicata due volte si toglie
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        // =================================================================== dati

        private static int RawModules(int v)
        {
            int r = (16 * v + 128) * v + 64;
            if (v >= 2)
            {
                int n = v / 7 + 2;
                r -= (25 * n - 10) * n - 55;
                if (v >= 7) r -= 36;
            }
            return r;
        }

        private static int DataCodewords(int v) => RawModules(v) / 8 - EccPerBlock[v - 1] * Blocks[v - 1];

        private static byte[] EncodeData(byte[] data, int ver)
        {
            int cap = DataCodewords(ver) * 8;
            var bits = new List<bool>(cap);
            void Put(int val, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }
            Put(4, 4);                                  // modalità byte
            Put(data.Length, ver < 10 ? 8 : 16);
            foreach (byte b in data) Put(b, 8);
            Put(0, Math.Min(4, cap - bits.Count));      // terminatore
            Put(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Put(pad, 8);
            byte[] cw = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));
            return cw;
        }

        private static byte[] Interleave(byte[] cw, int ver)
        {
            int nb = Blocks[ver - 1], ecl = EccPerBlock[ver - 1], raw = RawModules(ver) / 8;
            int nShort = nb - raw % nb, shortLen = raw / nb;
            byte[] div = Divisor(ecl);
            var blocks = new List<byte[]>();
            int k = 0;
            for (int i = 0; i < nb; i++)
            {
                int dl = shortLen - ecl + (i < nShort ? 0 : 1);
                byte[] d = new byte[dl];
                Array.Copy(cw, k, d, 0, dl);
                k += dl;
                byte[] e = Remainder(d, div);
                byte[] block = new byte[shortLen + 1];       // i blocchi corti hanno un posto vuoto prima della correzione
                Array.Copy(d, 0, block, 0, dl);
                Array.Copy(e, 0, block, shortLen + 1 - ecl, ecl);
                blocks.Add(block);
            }
            var result = new List<byte>(raw);
            for (int i = 0; i < shortLen + 1; i++)
                for (int j = 0; j < nb; j++)
                    if (i != shortLen - ecl || j >= nShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        // =================================================================== Reed-Solomon

        private static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = ((z << 1) ^ ((z >> 7) * 0x11D)) & 0xFF;
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        private static byte[] Divisor(int degree)
        {
            int[] r = new int[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 2);
            }
            byte[] b = new byte[degree];
            for (int i = 0; i < degree; i++) b[i] = (byte)r[i];
            return b;
        }

        private static byte[] Remainder(byte[] data, byte[] div)
        {
            int[] r = new int[div.Length];
            foreach (byte b in data)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[^1] = 0;
                for (int i = 0; i < div.Length; i++) r[i] ^= Mul(div[i], f);
            }
            byte[] o = new byte[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = (byte)r[i];
            return o;
        }

        // =================================================================== disegno

        private void Set(int x, int y, bool dark)
        {
            _m[y, x] = dark;
            _fn[y, x] = true;
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return Array.Empty<int>();
            int n = Version / 7 + 2;
            int step = (Version * 8 + n * 3 + 5) / (n * 4 - 4) * 2;
            int[] res = new int[n];
            res[0] = 6;
            for (int i = n - 1, pos = Size - 7; i >= 1; i--, pos -= step) res[i] = pos;
            return res;
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
            Finder(3, 3);
            Finder(Size - 4, 3);
            Finder(3, Size - 4);
            int[] ap = AlignmentPositions();
            int na = ap.Length;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < na; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(ap[i] + dx, ap[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormatBits(0);       // riserva il posto; i bit veri dopo la scelta della maschera
            if (Version >= 7)
            {
                int r = Version;
                for (int i = 0; i < 12; i++) r = (r << 1) ^ ((r >> 11) * 0x1F25);
                int bits = (Version << 12) | r;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    Set(a, b, bit);
                    Set(b, a, bit);
                }
            }
        }

        private void Finder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, dist != 2 && dist != 4);
                }
        }

        /// <summary>Livello M (bit 00) e maschera, con BCH(15,5) e lo XOR 0x5412.</summary>
        private void DrawFormatBits(int mask)
        {
            int d = mask;          // M = 00
            int r = d;
            for (int i = 0; i < 10; i++) r = (r << 1) ^ ((r >> 9) * 0x537);
            int bits = ((d << 10) | r) ^ 0x5412;
            bool G(int i) => ((bits >> i) & 1) != 0;
            for (int i = 0; i < 6; i++) Set(8, i, G(i));
            Set(8, 7, G(6));
            Set(8, 8, G(7));
            Set(7, 8, G(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, G(i));
            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, G(i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, G(i));
            Set(8, Size - 8, true);
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool up = ((right + 1) & 2) == 0;
                        int y = up ? Size - 1 - vert : vert;
                        if (!_fn[y, x] && i < data.Length * 8)
                        {
                            _m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        private static bool MaskBit(int mask, int x, int y) => mask switch
        {
            0 => (x + y) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (x + y) % 3 == 0,
            4 => (x / 3 + y / 2) % 2 == 0,
            5 => x * y % 2 + x * y % 3 == 0,
            6 => (x * y % 2 + x * y % 3) % 2 == 0,
            _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
        };

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (!_fn[y, x] && MaskBit(mask, x, y)) _m[y, x] = !_m[y, x];
        }

        private int Penalty()
        {
            int p = 0;
            for (int y = 0; y < Size; y++)
            {
                int run = 1;
                for (int x = 1; x <= Size; x++)
                {
                    if (x < Size && _m[y, x] == _m[y, x - 1]) run++;
                    else { if (run >= 5) p += run - 2; run = 1; }
                }
            }
            for (int x = 0; x < Size; x++)
            {
                int run = 1;
                for (int y = 1; y <= Size; y++)
                {
                    if (y < Size && _m[y, x] == _m[y - 1, x]) run++;
                    else { if (run >= 5) p += run - 2; run = 1; }
                }
            }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = _m[y, x];
                    if (c == _m[y, x + 1] && c == _m[y + 1, x] && c == _m[y + 1, x + 1]) p += 3;
                }
            int dark = 0;
            foreach (bool b in _m) if (b) dark++;
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return p + Math.Max(0, k) * 10;
        }
    }
}
