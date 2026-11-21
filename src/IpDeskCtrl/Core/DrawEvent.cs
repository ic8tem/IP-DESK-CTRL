using System;
using System.Globalization;
using System.Linq;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Lavagna: un pezzo di tratto disegnato da chi comanda, da mostrare sopra lo schermo del PC comandato.
    /// Op: 0 = continua il tratto, 1 = nuovo tratto, 2 = cancella tutto. Punti in 0..1 rispetto all'area condivisa
    /// (X, Y, W, H in pixel dello schermo del PC comandato).
    /// </summary>
    public sealed record DrawEvent(string From, byte Op, float[] Pts, int X, int Y, int W, int H)
    {
        public const byte Continue = 0, NewStroke = 1, Clear = 2;

        /// <summary>Riga per il tubo tra servizio e programma: «DRAW numero op x y w h p1,p2,…».</summary>
        public string ToLine() =>
            $"DRAW {From} {Op} {X} {Y} {W} {H} " + (Pts.Length == 0 ? "-" : string.Join(",", Pts.Select(p => p.ToString("0.#####", CultureInfo.InvariantCulture))));

        public static DrawEvent? FromLine(string[] p)
        {
            try
            {
                if (p.Length < 8) return null;
                var pts = p[7] == "-" ? Array.Empty<float>() : p[7].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                return new DrawEvent(p[1], byte.Parse(p[2]), pts, int.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]), int.Parse(p[6]));
            }
            catch { return null; }
        }
    }
}
