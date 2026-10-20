// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TurzxPanelWorks
{
    public static class FrameRotation
    {
        // Clockwise rotation, preserving the fixed output dimensions and aspect ratio.
        public static Bitmap Apply(Bitmap source, int degrees)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (degrees != 0 && degrees != 90 && degrees != 180 && degrees != 270)
                throw new ArgumentOutOfRangeException("degrees");
            Bitmap rotated = (Bitmap)source.Clone();
            if (degrees == 0) return rotated;
            try
            {
                rotated.RotateFlip(degrees == 90 ? RotateFlipType.Rotate90FlipNone :
                    degrees == 180 ? RotateFlipType.Rotate180FlipNone : RotateFlipType.Rotate270FlipNone);
                if (rotated.Width == source.Width && rotated.Height == source.Height)
                {
                    Bitmap result = rotated;
                    rotated = null;
                    return result;
                }
                Bitmap fitted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                try
                {
                    double scale = Math.Min((double)source.Width / rotated.Width, (double)source.Height / rotated.Height);
                    int width = Math.Max(1, (int)Math.Round(rotated.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(rotated.Height * scale));
                    using (Graphics graphics = Graphics.FromImage(fitted))
                    {
                        graphics.Clear(Color.Black);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        using (ImageAttributes attributes = new ImageAttributes())
                        {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(rotated, new Rectangle((source.Width - width) / 2, (source.Height - height) / 2, width, height),
                                0, 0, rotated.Width, rotated.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    return fitted;
                }
                catch { fitted.Dispose(); throw; }
            }
            finally { if (rotated != null) rotated.Dispose(); }
        }
    }
}
