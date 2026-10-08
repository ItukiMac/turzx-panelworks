// SPDX-License-Identifier: GPL-3.0-or-later
using System.Drawing;
using System.Drawing.Drawing2D;

namespace StandaloneDisplay
{
    internal enum ResizeHandle { None, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West }

    internal static class SelectionAdorner
    {
        public static void Draw(Bitmap frame, Rectangle bounds)
        {
            using (Graphics graphics = Graphics.FromImage(frame))
            using (Pen pen = new Pen(Color.FromArgb(255, 230, 190, 0), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                graphics.DrawRectangle(pen, bounds);
                foreach (Rectangle handle in GetHandleRectangles(bounds))
                {
                    graphics.FillRectangle(Brushes.White, handle);
                    graphics.DrawRectangle(Pens.Black, handle);
                }
            }
        }

        public static ResizeHandle HitTest(Rectangle bounds, Point point)
        {
            Rectangle[] handles = GetHandleRectangles(bounds);
            ResizeHandle[] values = { ResizeHandle.NorthWest, ResizeHandle.North, ResizeHandle.NorthEast, ResizeHandle.East,
                ResizeHandle.SouthEast, ResizeHandle.South, ResizeHandle.SouthWest, ResizeHandle.West };
            for (int index = 0; index < handles.Length; index++)
            {
                Rectangle area = handles[index];
                area.Inflate(3, 3);
                if (area.Contains(point)) return values[index];
            }
            return ResizeHandle.None;
        }

        private static Rectangle[] GetHandleRectangles(Rectangle bounds)
        {
            const int size = 6;
            int half = size / 2;
            int middleX = bounds.Left + bounds.Width / 2;
            int middleY = bounds.Top + bounds.Height / 2;
            return new[]
            {
                new Rectangle(bounds.Left-half,bounds.Top-half,size,size), new Rectangle(middleX-half,bounds.Top-half,size,size),
                new Rectangle(bounds.Right-half,bounds.Top-half,size,size), new Rectangle(bounds.Right-half,middleY-half,size,size),
                new Rectangle(bounds.Right-half,bounds.Bottom-half,size,size), new Rectangle(middleX-half,bounds.Bottom-half,size,size),
                new Rectangle(bounds.Left-half,bounds.Bottom-half,size,size), new Rectangle(bounds.Left-half,middleY-half,size,size)
            };
        }
    }
}
