// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace TurzxPanelWorks
{
    public enum SceneElementKind { Text, Bar, Graph, Rectangle }

    public sealed class SceneElement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SceneElementKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Formula { get; set; }
        public float FontSize { get; set; }
        public bool Bold { get; set; }
        public int ForeColorArgb { get; set; }
        public int FillColorArgb { get; set; }
        public int BackColorArgb { get; set; }
        public string Alignment { get; set; }
        public string VerticalAlignment { get; set; }

        [XmlIgnore]
        public Rectangle Bounds { get { return new Rectangle(X, Y, Width, Height); } }

        public SceneElement Clone()
        {
            return new SceneElement
            {
                Id = Guid.NewGuid().ToString("N"), Name = Name + " copy", Kind = Kind,
                X = X + 8, Y = Y + 8, Width = Width, Height = Height, Formula = Formula,
                FontSize = FontSize, Bold = Bold, ForeColorArgb = ForeColorArgb,
                FillColorArgb = FillColorArgb, BackColorArgb = BackColorArgb, Alignment = Alignment,
                VerticalAlignment = VerticalAlignment
            };
        }

        public override string ToString() { return Name + "  [" + Kind + "]"; }
    }

    [XmlRoot("DashboardScene")]
    public sealed class SceneDocument
    {
        public int Version { get; set; }
        public string Name { get; set; }
        [XmlArray("Elements"), XmlArrayItem("Element")]
        public List<SceneElement> Elements { get; set; }

        public SceneDocument()
        {
            Version = 2;
            Name = "Untitled";
            Elements = new List<SceneElement>();
        }

        public SceneElement HitTest(Point point)
        {
            for (int index = Elements.Count - 1; index >= 0; index--)
                if (Elements[index].Bounds.Contains(point)) return Elements[index];
            return null;
        }

        public void BringToFront(SceneElement element)
        {
            if (element == null || !Elements.Remove(element)) return;
            Elements.Add(element);
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            XmlSerializer serializer = new XmlSerializer(typeof(SceneDocument));
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
            using (XmlWriter writer = XmlWriter.Create(path, settings)) serializer.Serialize(writer, this);
        }

        public static SceneDocument LoadOrDefault(string path)
        {
            if (!File.Exists(path)) return CreateDefault();
            try
            {
                return Load(path);
            }
            catch { return CreateDefault(); }
        }

        public static SceneDocument Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(SceneDocument));
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                SceneDocument scene = serializer.Deserialize(reader) as SceneDocument;
                if (scene == null) throw new InvalidDataException("レイアウトファイルを読み込めませんでした。");
                if (scene.Elements == null) scene.Elements = new List<SceneElement>();
                return scene;
            }
        }

        public static SceneDocument CreateDefault()
        {
            SceneDocument scene = new SceneDocument();
            scene.Name = "Default";
            scene.Elements.Add(Text("Clock", 10, 8, 460, 119, "=FORMAT([DATE.HOUR],\"HH:mm\")", 94, "Center"));
            scene.Elements.Add(Text("Date", 10, 145, 460, 37, "=FORMAT([DATE.DAY],\"yyyy/M/d\")", 25, "Center"));
            scene.Elements.Add(Text("CPU temperature", 83, 204, 80, 22, "=FORMAT([STATS.CPU.TEMPERATURE],\"0\") & \" °C\"", 14, "Right"));
            scene.Elements.Add(Text("CPU load", 61, 220, 102, 31, "=FORMAT([STATS.CPU.PERCENTAGE],\"0\") & \" %\"", 23, "Right"));
            scene.Elements.Add(Bar("CPU bar", 25, 244, 130, 10, "=[STATS.CPU.PERCENTAGE]"));
            scene.Elements.Add(Text("GPU temperature", 237, 204, 78, 22, "=FORMAT([STATS.GPU.TEMPERATURE],\"0\") & \" °C\"", 14, "Right"));
            scene.Elements.Add(Text("GPU load", 215, 220, 100, 31, "=FORMAT([STATS.GPU.PERCENTAGE],\"0\") & \" %\"", 23, "Right"));
            scene.Elements.Add(Bar("GPU bar", 179, 244, 128, 10, "=[STATS.GPU.PERCENTAGE]"));
            scene.Elements.Add(Text("RAM used", 390, 204, 72, 22, "=FORMAT([STATS.MEMORY.USED]/1024,\"0.0\") & \" GB\"", 14, "Right"));
            scene.Elements.Add(Text("RAM load", 368, 220, 94, 31, "=FORMAT([STATS.MEMORY.PERCENTAGE],\"0\") & \" %\"", 23, "Right"));
            scene.Elements.Add(Bar("RAM bar", 332, 244, 122, 10, "=[STATS.MEMORY.PERCENTAGE]"));
            scene.Elements.Add(Text("Volume", 94, 268, 58, 22, "=FORMAT([STATS.AUDIO.VOLUME],\"0\")", 14, "Right"));
            scene.Elements.Add(Bar("Volume bar", 25, 285, 64, 8, "=[STATS.AUDIO.VOLUME]"));
            scene.Elements.Add(Graph("Upload history", 181, 283, 43, 8, "=[STATS.NETWORK.UPLOAD]"));
            scene.Elements.Add(Text("Upload", 174, 292, 72, 15, "=NETRATE([STATS.NETWORK.UPLOAD])", 14, "Center"));
            scene.Elements.Add(Graph("Download history", 258, 283, 43, 8, "=[STATS.NETWORK.DOWNLOAD]"));
            scene.Elements.Add(Text("Download", 251, 292, 72, 15, "=NETRATE([STATS.NETWORK.DOWNLOAD])", 14, "Center"));
            scene.Elements.Add(Text("Disk used", 397, 268, 53, 22, "=FORMAT([STATS.DISK.USED.PERCENTAGE],\"0\") & \" %\"", 14, "Right"));
            scene.Elements.Add(Bar("Disk bar", 336, 305, 102, 8, "=[STATS.DISK.USED.PERCENTAGE]"));
            return scene;
        }

        public static SceneDocument CreateDemo()
        {
            SceneDocument scene = new SceneDocument();
            scene.Name = "Comprehensive Demo";
            scene.Elements.Add(Box("Demo background", 4, 4, 472, 312, Color.FromArgb(238, 3, 7, 16)));
            scene.Elements.Add(Box("Header panel", 8, 8, 464, 40, Color.FromArgb(225, 14, 20, 40)));
            scene.Elements.Add(Text("Demo clock", 16, 10, 145, 34, "=FORMAT([DATE.HOUR],\"HH:mm\")", 30, "Left"));
            scene.Elements.Add(Text("Demo date", 165, 12, 142, 28, "=FORMAT([DATE.DAY],\"yyyy/M/d\")", 16, "Center"));
            SceneElement systemStatus = Text("Formula status", 312, 12, 150, 28,
                "=IF([STATS.CPU.PERCENTAGE]>=80,\"HIGH LOAD\",\"SYSTEM OK\")", 14, "Right");
            systemStatus.ForeColorArgb = Color.FromArgb(0, 232, 165).ToArgb();
            scene.Elements.Add(systemStatus);

            scene.Elements.Add(Box("CPU panel", 8, 54, 228, 126, Color.FromArgb(220, 10, 17, 31)));
            scene.Elements.Add(Box("CPU core divider", 123, 99, 1, 74, Color.FromArgb(255, 48, 65, 92)));
            scene.Elements.Add(Text("CPU heading", 14, 58, 112, 16, "=\"CPU TOTAL + CORES\"", 11, "Left"));
            scene.Elements.Add(Text("CPU demo temperature", 148, 58, 78, 16,
                "=FORMAT([STATS.CPU.TEMPERATURE],\"0\") & \" °C\"", 11, "Right"));
            scene.Elements.Add(Text("CPU demo total", 14, 76, 72, 22,
                "=FORMAT([STATS.CPU.PERCENTAGE],\"0\") & \" %\"", 19, "Left"));
            scene.Elements.Add(Bar("CPU demo total bar", 90, 83, 136, 9, "=[STATS.CPU.PERCENTAGE]"));
            for (int core = 0; core < 8; core++)
            {
                int column = core / 4;
                int row = core % 4;
                int x = 14 + column * 114;
                int y = 101 + row * 18;
                string reference = "[STATS.CPU.CORE." + core + ".PERCENTAGE]";
                scene.Elements.Add(Text("CPU core " + core + " label", x, y, 20, 14, "=\"C" + core + "\"", 9, "Left"));
                scene.Elements.Add(Bar("CPU core " + core + " bar", x + 22, y + 5, 49, 6, "=" + reference));
                scene.Elements.Add(Text("CPU core " + core + " value", x + 74, y, 31, 14,
                    "=FORMAT(" + reference + ",\"0\")", 9, "Right"));
            }

            scene.Elements.Add(Box("GPU panel", 244, 54, 228, 126, Color.FromArgb(220, 10, 17, 31)));
            scene.Elements.Add(Text("GPU heading", 250, 58, 124, 16, "=\"GPU ENGINES\"", 11, "Left"));
            scene.Elements.Add(Text("GPU demo temperature", 388, 58, 74, 16,
                "=FORMAT([STATS.GPU.TEMPERATURE],\"0\") & \" °C\"", 11, "Right"));
            scene.Elements.Add(Text("GPU demo total", 250, 76, 72, 22,
                "=FORMAT([STATS.GPU.PERCENTAGE],\"0\") & \" %\"", 19, "Left"));
            scene.Elements.Add(Bar("GPU demo total bar", 326, 83, 136, 9, "=[STATS.GPU.PERCENTAGE]"));
            string[] gpuLabels = { "3D", "COPY", "DECODE", "ENCODE", "COMPUTE" };
            string[] gpuReferences =
            {
                "STATS.GPU.ENGINE.GPU0.3D.PERCENTAGE", "STATS.GPU.ENGINE.GPU0.COPY.PERCENTAGE",
                "STATS.GPU.ENGINE.GPU0.VIDEO_DECODE.PERCENTAGE", "STATS.GPU.ENGINE.GPU0.VIDEO_ENCODE.PERCENTAGE",
                "STATS.GPU.ENGINE.GPU0.COMPUTE_0.PERCENTAGE"
            };
            for (int index = 0; index < gpuLabels.Length; index++)
            {
                int y = 100 + index * 15;
                string reference = "[" + gpuReferences[index] + "]";
                scene.Elements.Add(Text("GPU " + gpuLabels[index] + " label", 250, y, 48, 13, "=\"" + gpuLabels[index] + "\"", 8, "Left"));
                scene.Elements.Add(Bar("GPU " + gpuLabels[index] + " bar", 300, y + 4, 120, 6, "=" + reference));
                scene.Elements.Add(Text("GPU " + gpuLabels[index] + " value", 423, y, 39, 13,
                    "=FORMAT(" + reference + ",\"0\")", 8, "Right"));
            }

            scene.Elements.Add(Box("Resources panel", 8, 186, 228, 64, Color.FromArgb(220, 10, 17, 31)));
            scene.Elements.Add(Box("Resources divider 1", 84, 190, 1, 52, Color.FromArgb(255, 48, 65, 92)));
            scene.Elements.Add(Box("Resources divider 2", 157, 190, 1, 52, Color.FromArgb(255, 48, 65, 92)));
            string[] resourceLabels = { "RAM", "DISK", "VOL" };
            string[] resourceValues =
            {
                "=FORMAT([STATS.MEMORY.USED]/1024,\"0.0\") & \"G\"",
                "=FORMAT([STATS.DISK.USED.PERCENTAGE],\"0\") & \"%\"",
                "=FORMAT([STATS.AUDIO.VOLUME],\"0\") & \"%\""
            };
            string[] resourceBars = { "=[STATS.MEMORY.PERCENTAGE]", "=[STATS.DISK.USED.PERCENTAGE]", "=[STATS.AUDIO.VOLUME]" };
            for (int index = 0; index < 3; index++)
            {
                int x = 14 + index * 73;
                scene.Elements.Add(Text(resourceLabels[index] + " demo label", x, 191, 66, 13, "=\"" + resourceLabels[index] + "\"", 9, "Center"));
                scene.Elements.Add(Text(resourceLabels[index] + " demo value", x, 207, 66, 20, resourceValues[index], 14, "Center"));
                scene.Elements.Add(Bar(resourceLabels[index] + " demo bar", x, 232, 66, 7, resourceBars[index]));
            }

            scene.Elements.Add(Box("Network panel", 244, 186, 228, 64, Color.FromArgb(220, 10, 17, 31)));
            scene.Elements.Add(Box("Network divider", 356, 190, 1, 52, Color.FromArgb(255, 48, 65, 92)));
            scene.Elements.Add(Text("Upload demo label", 250, 191, 100, 13, "=\"UPLOAD\"", 9, "Center"));
            scene.Elements.Add(Text("Upload demo value", 250, 205, 100, 17, "=NETRATE([STATS.NETWORK.UPLOAD])", 11, "Center"));
            scene.Elements.Add(Graph("Upload demo history", 250, 226, 100, 14, "=[STATS.NETWORK.UPLOAD]"));
            scene.Elements.Add(Text("Download demo label", 362, 191, 100, 13, "=\"DOWNLOAD\"", 9, "Center"));
            scene.Elements.Add(Text("Download demo value", 362, 205, 100, 17, "=NETRATE([STATS.NETWORK.DOWNLOAD])", 11, "Center"));
            scene.Elements.Add(Graph("Download demo history", 362, 226, 100, 14, "=[STATS.NETWORK.DOWNLOAD]"));

            scene.Elements.Add(Box("Formula panel", 8, 256, 464, 56, Color.FromArgb(225, 14, 20, 40)));
            scene.Elements.Add(Box("Footer divider", 104, 259, 1, 46, Color.FromArgb(255, 48, 65, 92)));
            scene.Elements.Add(Text("CPU average label", 14, 260, 82, 14, "=\"CPU AVG\"", 9, "Left"));
            scene.Elements.Add(Text("CPU average value", 14, 276, 82, 26,
                "=FORMAT([STATS.CPU.PERCENTAGE],\"0\") & \" %\"", 16, "Left"));
            scene.Elements.Add(Text("Formula demo label", 112, 260, 170, 14, "=\"PEAK LOAD / STATUS\"", 9, "Left"));
            scene.Elements.Add(Text("Formula demo value", 112, 276, 170, 26,
                "=FORMAT(MAX([STATS.CPU.PERCENTAGE],[STATS.GPU.PERCENTAGE]),\"0\") & \" %  \" & IF(MAX([STATS.CPU.PERCENTAGE],[STATS.GPU.PERCENTAGE])>=80,\"HOT\",\"OK\")", 16, "Left"));
            return scene;
        }

        private static SceneElement Text(string name, int x, int y, int width, int height, string formula, float size, string alignment)
        {
            return new SceneElement
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, Kind = SceneElementKind.Text,
                X = x, Y = y, Width = width, Height = height, Formula = formula,
                FontSize = size, Bold = true, ForeColorArgb = Color.White.ToArgb(),
                FillColorArgb = Color.Cyan.ToArgb(), BackColorArgb = Color.Transparent.ToArgb(), Alignment = alignment,
                VerticalAlignment = "Center"
            };
        }

        private static SceneElement Bar(string name, int x, int y, int width, int height, string formula)
        {
            SceneElement element = Text(name, x, y, width, height, formula, 10, "Left");
            element.Kind = SceneElementKind.Bar;
            element.FillColorArgb = Color.FromArgb(0, 226, 239).ToArgb();
            element.BackColorArgb = Color.FromArgb(45, 55, 72).ToArgb();
            return element;
        }

        private static SceneElement Graph(string name, int x, int y, int width, int height, string formula)
        {
            SceneElement element = Bar(name, x, y, width, height, formula);
            element.Kind = SceneElementKind.Graph;
            return element;
        }

        private static SceneElement Box(string name, int x, int y, int width, int height, Color color)
        {
            return new SceneElement
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, Kind = SceneElementKind.Rectangle,
                X = x, Y = y, Width = width, Height = height, Formula = String.Empty,
                FontSize = 10, Bold = false, ForeColorArgb = Color.White.ToArgb(),
                FillColorArgb = color.ToArgb(), BackColorArgb = Color.Transparent.ToArgb(),
                Alignment = "Left", VerticalAlignment = "Top"
            };
        }
    }
}
