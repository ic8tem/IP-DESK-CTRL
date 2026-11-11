using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>Disegna un codice QR (nero su bianco, con il margine di 4 moduli richiesto dalla norma).</summary>
    public sealed class QrView : Control
    {
        private readonly QrCode _qr;

        public QrView(string text) { _qr = new QrCode(text); }

        protected override Size MeasureOverride(Size available)
        {
            double s = Math.Min(available.Width, available.Height);
            if (double.IsInfinity(s) || s <= 0) s = 260;
            return new Size(s, s);
        }

        public override void Render(DrawingContext dc)
        {
            double side = Math.Min(Bounds.Width, Bounds.Height);
            if (side <= 0) return;
            int n = _qr.Size + 8;
            double cell = Math.Floor(side / n);
            if (cell < 1) cell = side / n;
            double off = (side - cell * n) / 2;
            dc.FillRectangle(Brushes.White, new Rect(0, 0, side, side));
            for (int y = 0; y < _qr.Size; y++)
                for (int x = 0; x < _qr.Size; x++)
                    if (_qr[x, y]) dc.FillRectangle(Brushes.Black, new Rect(off + (x + 4) * cell, off + (y + 4) * cell, cell, cell));
        }
    }
}
