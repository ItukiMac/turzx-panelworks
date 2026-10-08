using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Collections.Generic;

namespace StandaloneDisplay
{
    public enum ResizeHandle { None, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West }

    public sealed class SceneRenderer : IDisposable
    {
        public const int CanvasWidth = 480;
        public const int CanvasHeight = 320;
        private readonly Bitmap background;
        private readonly FormulaEngine formulas = new FormulaEngine();
        private readonly Dictionary<string, List<double>> histories = new Dictionary<string, List<double>>();
        private readonly Dictionary<string, DateTime> historyTimestamps = new Dictionary<string, DateTime>();

        public SceneRenderer(string backgroundPath)
        {
            if (!String.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            {
                using (Image image = Image.FromFile(backgroundPath))
                    background = new Bitmap(image, CanvasWidth, CanvasHeight);
            }
            else
            {
                background = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(background)) graphics.Clear(Color.FromArgb(8, 12, 20));
            }
        }

        public Bitmap Render(SceneDocument scene, TelemetrySnapshot snapshot, SceneElement selected, bool showSelection)
        {
            Bitmap frame = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb);
            FormulaContext context = FormulaContext.FromSnapshot(snapshot);
            UpdateHistories(scene, context, snapshot.Timestamp);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.DrawImageUnscaled(background, 0, 0);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                foreach (SceneElement element in scene.Elements) DrawElement(graphics, element, context);
                if (showSelection && selected != null) DrawSelection(graphics, selected.Bounds);
            }
            return frame;
        }

        private void DrawElement(Graphics graphics, SceneElement element, FormulaContext context)
        {
            Rectangle bounds = element.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (element.Kind == SceneElementKind.Rectangle)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(element.FillColorArgb))) graphics.FillRectangle(brush, bounds);
                return;
            }
            if (element.Kind == SceneElementKind.Bar)
            {
                double value = Math.Max(0, Math.Min(100, formulas.EvaluateNumber(element.Formula, context)));
                using (SolidBrush back = new SolidBrush(Color.FromArgb(element.BackColorArgb))) graphics.FillRectangle(back, bounds);
                int width = (int)Math.Round(bounds.Width * value / 100.0);
                if (width > 0)
                {
                    Rectangle fillBounds = new Rectangle(bounds.X, bounds.Y, width, bounds.Height);
                    using (LinearGradientBrush brush = CreateLoadGradient(bounds))
                        graphics.FillRectangle(brush, fillBounds);
                }
                return;
            }
            if (element.Kind == SceneElementKind.Graph)
            {
                DrawHistoryGraph(graphics, element, bounds);
                return;
            }

            string text = formulas.EvaluateText(element.Formula, context);
            FontStyle style = element.Bold ? FontStyle.Bold : FontStyle.Regular;
            using (Font font = CreateFont(Math.Max(5, element.FontSize), style))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(element.ForeColorArgb)))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                RectangleF inkBounds = MeasureInkBounds(text, font, format);
                PointF origin = GetAlignedTextOrigin(bounds, inkBounds, element.Alignment, element.VerticalAlignment);
                graphics.DrawString(text, font, brush, origin, format);
            }
        }

        private static RectangleF MeasureInkBounds(string text, Font font, StringFormat format)
        {
            if (String.IsNullOrEmpty(text)) return RectangleF.Empty;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, format);
                return path.GetBounds();
            }
        }

        private static PointF GetAlignedTextOrigin(Rectangle bounds, RectangleF inkBounds, string alignment, string verticalAlignment)
        {
            float inkWidth = Math.Min(CanvasWidth, inkBounds.Width);
            float inkHeight = Math.Min(CanvasHeight, inkBounds.Height);
            float x;
            if (String.Equals(alignment, "Right", StringComparison.OrdinalIgnoreCase)) x = bounds.Right - inkWidth;
            else if (String.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase)) x = bounds.Left + (bounds.Width - inkWidth) / 2f;
            else x = bounds.Left;
            float y;
            if (String.Equals(verticalAlignment, "Top", StringComparison.OrdinalIgnoreCase)) y = bounds.Top;
            else if (String.Equals(verticalAlignment, "Bottom", StringComparison.OrdinalIgnoreCase)) y = bounds.Bottom - inkHeight;
            else y = bounds.Top + (bounds.Height - inkHeight) / 2f;
            x = Math.Max(0, Math.Min(CanvasWidth - inkWidth, x));
            y = Math.Max(0, Math.Min(CanvasHeight - inkHeight, y));
            return new PointF(x - inkBounds.Left, y - inkBounds.Top);
        }

        private void UpdateHistories(SceneDocument scene, FormulaContext context, DateTime timestamp)
        {
            foreach (SceneElement element in scene.Elements)
            {
                if (element.Kind != SceneElementKind.Graph) continue;
                DateTime previous;
                if (historyTimestamps.TryGetValue(element.Id, out previous) && previous == timestamp) continue;
                List<double> values;
                if (!histories.TryGetValue(element.Id, out values))
                {
                    values = new List<double>();
                    histories[element.Id] = values;
                }
                values.Add(Math.Max(0, formulas.EvaluateNumber(element.Formula, context)));
                while (values.Count > Math.Max(12, element.Width)) values.RemoveAt(0);
                historyTimestamps[element.Id] = timestamp;
            }
        }

        private void DrawHistoryGraph(Graphics graphics, SceneElement element, Rectangle bounds)
        {
            using (SolidBrush back = new SolidBrush(Color.FromArgb(element.BackColorArgb))) graphics.FillRectangle(back, bounds);
            List<double> values;
            if (!histories.TryGetValue(element.Id, out values) || values.Count == 0) return;
            double maximum = 1;
            foreach (double value in values) maximum = Math.Max(maximum, value);
            maximum *= 1.1;
            PointF[] points = new PointF[values.Count];
            float step = values.Count <= 1 ? 0 : bounds.Width / (float)(values.Count - 1);
            for (int index = 0; index < values.Count; index++)
            {
                float x = bounds.Left + index * step;
                float y = bounds.Bottom - 1 - (float)(values[index] / maximum * Math.Max(1, bounds.Height - 2));
                points[index] = new PointF(x, y);
            }
            if (points.Length > 1)
            {
                PointF[] area = new PointF[points.Length + 2];
                area[0] = new PointF(points[0].X, bounds.Bottom);
                Array.Copy(points, 0, area, 1, points.Length);
                area[area.Length - 1] = new PointF(points[points.Length - 1].X, bounds.Bottom);
                using (LinearGradientBrush fill = new LinearGradientBrush(bounds,
                    Color.FromArgb(145, 0, 225, 235), Color.FromArgb(25, 0, 225, 235), LinearGradientMode.Vertical))
                    graphics.FillPolygon(fill, area);
                using (Pen line = new Pen(Color.FromArgb(0, 235, 245), 1.2f)) graphics.DrawLines(line, points);
            }
            else
            {
                using (Pen line = new Pen(Color.FromArgb(0, 235, 245), 1.2f))
                    graphics.DrawLine(line, bounds.Left, points[0].Y, bounds.Right, points[0].Y);
            }
        }

        private static LinearGradientBrush CreateLoadGradient(Rectangle bounds)
        {
            LinearGradientBrush brush = new LinearGradientBrush(bounds,
                Color.FromArgb(0, 205, 235), Color.FromArgb(255, 45, 80), LinearGradientMode.Horizontal);
            ColorBlend blend = new ColorBlend
            {
                Colors = new[]
                {
                    Color.FromArgb(0, 205, 235),
                    Color.FromArgb(0, 232, 165),
                    Color.FromArgb(240, 220, 35),
                    Color.FromArgb(255, 145, 20),
                    Color.FromArgb(255, 45, 80)
                },
                Positions = new[] { 0f, 0.45f, 0.68f, 0.84f, 1f }
            };
            brush.InterpolationColors = blend;
            return brush;
        }

        private static Font CreateFont(float pixels, FontStyle style)
        {
            string[] names = { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI" };
            foreach (string name in names)
            {
                Font font = new Font(name, pixels, style, GraphicsUnit.Pixel);
                if (String.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase)) return font;
                font.Dispose();
            }
            return new Font(FontFamily.GenericSansSerif, pixels, style, GraphicsUnit.Pixel);
        }

        private static void DrawSelection(Graphics graphics, Rectangle bounds)
        {
            using (Pen pen = new Pen(Color.FromArgb(255, 230, 190, 0), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                graphics.DrawRectangle(pen, bounds);
            }
            foreach (Rectangle handle in GetHandleRectangles(bounds))
            {
                graphics.FillRectangle(Brushes.White, handle);
                graphics.DrawRectangle(Pens.Black, handle);
            }
        }

        public static ResizeHandle HitTestHandle(Rectangle bounds, Point point)
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

        public void Dispose() { background.Dispose(); }
    }
}
