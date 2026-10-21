// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TurzxPanelWorks
{
    public static class LayoutCatalog
    {
        public static readonly string[] LayoutNames = { "時計＋3ブロック", "左右2分割", "4分割（2×2）", "6分割（3×2）" };
        public static readonly string[] Metrics = { "CPU", "GPU", "RAM", "DISK", "UPLOAD", "DOWNLOAD", "CLOCK", "空白", "カレンダー", "天気", "サイト", "ニュース" };
        public static readonly string[] TemplateNames = { "01 時計・システム総合", "02 時計・CPU・GPU・RAM", "03 CPU・GPU・RAM・ディスク4分割", "04 システム・通信6分割", "05 CPU・GPU比較", "06 時計・天気・カレンダー", "07 Yahoo!ニュース" };
        public static readonly string[] FileNames = { "01-clock-system-overview.scene.xml", "02-clock-cpu-gpu-ram.scene.xml", "03-cpu-gpu-ram-disk-grid.scene.xml", "04-system-network-grid.scene.xml", "05-cpu-gpu-comparison.scene.xml", "06-clock-weather-calendar.scene.xml", "07-yahoo-news-carousel.scene.xml" };
        public static string DirectoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts"); } }
        public static string DefaultPath { get { return Path.Combine(DirectoryPath, FileNames[0]); } }

        public static Rectangle[] Blocks(int layout)
        {
            if (layout < 0 || layout >= LayoutNames.Length) throw new ArgumentOutOfRangeException("layout");
            if (layout == 0) return new[] { new Rectangle(12, 12, 456, 142), new Rectangle(12, 166, 144, 142), new Rectangle(168, 166, 144, 142), new Rectangle(324, 166, 144, 142) };
            int columns = layout == 3 ? 3 : 2;
            int rows = layout == 1 ? 1 : 2;
            int width = (480 - 12 * (columns + 1)) / columns;
            int height = (320 - 12 * (rows + 1)) / rows;
            List<Rectangle> blocks = new List<Rectangle>();
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    blocks.Add(new Rectangle(12 + column * (width + 12), 12 + row * (height + 12), width, height));
            return blocks.ToArray();
        }

        public static SceneDocument Template(int index)
        {
            if (index < 0 || index >= TemplateNames.Length) throw new ArgumentOutOfRangeException("index");
            if (index == 0)
            {
                using (Stream stream = typeof(LayoutCatalog).Assembly.GetManifestResourceStream("ClockSystemOverview.scene.xml"))
                    return (SceneDocument)new System.Xml.Serialization.XmlSerializer(typeof(SceneDocument)).Deserialize(stream);
            }
            index--;
            if (index >= 4) return InformationTemplate(index);
            int[] layouts = { 0, 2, 3, 1 };
            string[][] metrics = {
                new[] { "CLOCK", "CPU", "GPU", "RAM" },
                new[] { "CPU", "GPU", "RAM", "DISK" },
                new[] { "CPU", "GPU", "RAM", "DISK", "UPLOAD", "DOWNLOAD" },
                new[] { "CPU", "GPU" }
            };
            SceneDocument scene = Create(layouts[index], metrics[index]);
            scene.Name = TemplateNames[index + 1];
            return scene;
        }

        public static SceneDocument Create(int layout, string[] metrics)
        {
            Rectangle[] blocks = Blocks(layout);
            if (metrics == null || metrics.Length != blocks.Length) throw new ArgumentException("ブロック数と項目数が一致しません。");
            SceneDocument scene = new SceneDocument { Name = LayoutNames[layout], QuickLayout = layout, QuickMetrics = new List<string>(metrics) };
            for (int i = 0; i < blocks.Length; i++)
            {
                if (Array.IndexOf(Metrics, metrics[i]) < 0) throw new ArgumentException("未対応の表示項目です。");
                AddCard(scene, blocks[i], metrics[i], i);
            }
            return scene;
        }

        private static void AddCard(SceneDocument scene, Rectangle r, string metric, int index)
        {
            if (metric == "カレンダー" || metric == "天気" || metric == "サイト" || metric == "ニュース")
            {
                scene.Elements.Add(Widget(metric, r)); return;
            }
            Color accent = metric == "GPU" ? Color.FromArgb(170, 151, 255) : metric == "RAM" ? Color.FromArgb(94, 221, 173) : Color.FromArgb(89, 203, 245);
            string prefix = "Block" + (index + 1) + " " + metric;
            scene.Elements.Add(Element(prefix + " panel", SceneElementKind.Rectangle, r, "", 12, Color.FromArgb(19, 29, 44)));
            scene.Elements.Add(Element(prefix + " accent", SceneElementKind.Rectangle, new Rectangle(r.X, r.Y, 3, r.Height), "", 12, accent));
            if (metric == "空白") return;
            int inset = 12;
            string title = metric == "CLOCK" ? "TIME" : metric;
            scene.Elements.Add(Element(prefix + " title", SceneElementKind.Text, new Rectangle(r.X + inset, r.Y + 12, r.Width - inset * 2, 18), "=\"" + title + "\"", 12, accent));
            string reference = metric == "CPU" ? "STATS.CPU.PERCENTAGE" : metric == "GPU" ? "STATS.GPU.PERCENTAGE" : metric == "RAM" ? "STATS.MEMORY.PERCENTAGE" : "STATS.DISK.USED.PERCENTAGE";
            bool network = metric == "UPLOAD" || metric == "DOWNLOAD";
            bool clock = metric == "CLOCK";
            string formula = clock ? "=FORMAT([DATE.HOUR],\"HH:mm\")" : network ? "=NETRATE([STATS.NETWORK." + metric + "])" : "=FORMAT([" + reference + "],\"0\") & \" %\"";
            int valueTop = r.Y + (r.Height > 200 ? 80 : 38);
            int valueHeight = r.Height > 200 ? 72 : 46;
            float size = clock ? (r.Width > 300 ? 52 : 32) : network ? 18 : (r.Height > 200 ? 44 : 32);
            scene.Elements.Add(Element(prefix + " value", SceneElementKind.Text, new Rectangle(r.X + inset, valueTop, r.Width - inset * 2, valueHeight), formula, size, Color.FromArgb(237, 243, 252)));
            string detail = clock ? "=FORMAT([DATE.DAY],\"yyyy/MM/dd\")" : metric == "CPU" || metric == "GPU" ? "=FORMAT([STATS." + metric + ".TEMPERATURE],\"0\") & \" °C\"" : metric == "RAM" ? "=FORMAT([STATS.MEMORY.USED]/1024,\"0.0\") & \" GB\"" : metric == "DISK" ? "=\"USED\"" : "=\"NETWORK\"";
            scene.Elements.Add(Element(prefix + " detail", SceneElementKind.Text, new Rectangle(r.X + inset, r.Bottom - 46, r.Width - inset * 2, 20), detail, 13, Color.FromArgb(157, 177, 201)));
            if (!clock)
            {
                SceneElement bar = Element(prefix + " " + (network ? "history" : "bar"), network ? SceneElementKind.Graph : SceneElementKind.Bar,
                    new Rectangle(r.X + inset, r.Bottom - 18, r.Width - inset * 2, 6), network ? "=[STATS.NETWORK." + metric + "]" : "=[" + reference + "]", 10, accent);
                scene.Elements.Add(bar);
            }
        }

        public static SceneElement Widget(string metric, Rectangle bounds)
        {
            SceneElement element = Element(metric, metric == "カレンダー" ? SceneElementKind.Calendar : metric == "天気" ? SceneElementKind.Weather : metric == "ニュース" ? SceneElementKind.News : SceneElementKind.Website, bounds, "", 12, Color.White);
            if (element.Kind == SceneElementKind.News) element.SourceUrl = "https://news.yahoo.co.jp/rss/topics/top-picks.xml";
            if (element.Kind == SceneElementKind.Website) element.SourceUrl = "https://news.yahoo.co.jp/";
            return element;
        }
        private static SceneDocument InformationTemplate(int index)
        {
            SceneDocument scene = new SceneDocument { Name = TemplateNames[index + 1] };
            if (index == 4)
            {
                scene.Elements.Add(Element("Header", SceneElementKind.Rectangle, new Rectangle(12, 12, 456, 56), "", 12, Color.FromArgb(19, 29, 44)));
                scene.Elements.Add(Element("時計", SceneElementKind.Text, new Rectangle(24, 20, 126, 36), "=FORMAT([DATE.HOUR],\"HH:mm\")", 30, Color.White));
                scene.Elements.Add(Element("CPU GPU RAM", SceneElementKind.Text, new Rectangle(166, 28, 290, 20), "=\"CPU \" & FORMAT([STATS.CPU.PERCENTAGE],\"0\") & \"%  GPU \" & FORMAT([STATS.GPU.PERCENTAGE],\"0\") & \"%  RAM \" & FORMAT([STATS.MEMORY.PERCENTAGE],\"0\") & \"%\"", 13, Color.FromArgb(89, 203, 245)));
                scene.Elements.Add(Widget("カレンダー", new Rectangle(12, 80, 222, 228)));
                scene.Elements.Add(Widget("天気", new Rectangle(246, 80, 222, 228)));
            }
            else
            {
                scene.Elements.Add(Widget("ニュース", new Rectangle(12, 12, 456, 296)));
            }
            return scene;
        }
        private static SceneElement Element(string name, SceneElementKind kind, Rectangle bounds, string formula, float size, Color color)
        {
            return new SceneElement { Id = Guid.NewGuid().ToString("N"), Name = name, Kind = kind, X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                Formula = formula, FontSize = size, Bold = true, ForeColorArgb = color.ToArgb(), FillColorArgb = color.ToArgb(), BackColorArgb = Color.FromArgb(35, 49, 67).ToArgb(), Alignment = "Left", VerticalAlignment = "Center" };
        }

        public static void EnsureTemplates()
        {
            EnsureTemplates(DirectoryPath);
        }

        public static void EnsureTemplates(string directory)
        {
            Directory.CreateDirectory(directory);
            for (int i = 0; i < FileNames.Length; i++)
            {
                string path = Path.Combine(directory, FileNames[i]);
                if (!File.Exists(path)) Template(i).Save(path);
            }
        }

        public static bool IsTemplate(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string name in FileNames)
                if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(DirectoryPath, name)), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
