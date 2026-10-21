// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private static int failures;
        private static void Check(bool value, string name) { if (!value) { failures++; Console.Error.WriteLine("FAIL: " + name); } }
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--upgrade-template-parameters")
            {
                foreach (string name in LayoutCatalog.FileNames)
                {
                    string path = Path.Combine(args[1], name);
                    if (!File.Exists(path)) continue;
                    SceneDocument scene = SceneDocument.Load(path);
                    if (scene.HostParameters == null) { scene.Save(path); Console.WriteLine("Added XML parameter definitions: " + name); }
                }
                return 0;
            }
            if (args.Length == 0) { Run("XmlHostParameters", XmlHostParameters); Run("MarketQuotes", MarketQuotes); }
            if (args.Length == 2 && args[0] == "--news-preview") return NewsPreview(args[1]);
            if (args.Length == 0) { Run("NewsCarousel", NewsCarousel); Run("PreviewSelection", PreviewSelection); Run("IndependentTiming", IndependentTiming); Run("TimingPersistence", TimingPersistence); Run("ContentTimingOverrides", ContentTimingOverrides); }
            if (args.Length == 1 && args[0] == "--benchmark") return BenchmarkEncoding();
            if (args.Length == 2 && args[0] == "--render-templates") return RenderTemplates(args[1]);
            if (args.Length == 3 && args[0] == "--capture-site")
            {
                try { File.WriteAllBytes(args[2], WebsiteCapture.Capture(args[1], 480, 320, CancellationToken.None)); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error.GetType().FullName); Console.Error.WriteLine(error.Message); return 1; }
            }
            if (args.Length == 2 && args[0] == "--online-widgets")
            {
                try { return OnlineWidgets(args[1]); }
                catch (Exception error) { Console.Error.WriteLine(error.GetType().FullName); Console.Error.WriteLine(error.Message); return 1; }
            }
            Run("WidgetContracts", WidgetContracts); Run("TemplateLayouts", TemplateLayouts); Run("FormulaAndScene", FormulaAndScene); Run("Rendering", Rendering); Run("Encoding", Encoding); Run("RotationBehavior", RotationBehavior); Run("RefreshOptionBounds", RefreshOptionBounds); Run("RefreshOptionRoundTrip", RefreshOptionRoundTrip); Run("SharedTelemetryLifetime", SharedTelemetryLifetime); Run("RuntimeBehavior", RuntimeBehavior); Run("TargetSpecificFrames", TargetSpecificFrames); Run("FaultCleanup", FaultCleanup); Run("BusyAndClose", BusyAndClose); Run("PersistenceFailures", PersistenceFailures); Run("FrameReload", FrameReload); Run("EncoderInputFailures", EncoderInputFailures); Run("CalendarPositions", CalendarPositions); Run("AsyncWeather", AsyncWeather);
            Console.WriteLine(failures == 0 ? "PASS: offline test suite" : "FAILURES: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void WidgetContracts()
        {
            string json = "{\"daily\":{\"time\":[\"2026-10-21\",\"2026-10-22\",\"2026-10-23\"],\"weather_code\":[0,61,null],\"temperature_2m_max\":[24,20,null],\"temperature_2m_min\":[15,12,null],\"precipitation_probability_max\":[10,80,null]}}";
            ForecastDay[] days = WeatherForecast.Parse(json);
            Check(days.Length == 3 && days[0].Condition == "快晴" && days[1].Condition == "雨", "weather codes parsed");
            Check(days[2].High == "--" && days[2].Condition == "未取得", "weather missing values explicit");
            bool rejected = false;
            try { WeatherForecast.Parse("{\"daily\":{\"time\":[]}}"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "incomplete forecast rejected");
            rejected = false; try { WeatherForecast.Url(Double.NaN, 139); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid coordinate rejected");
            foreach (string url in new[] { "file:///C:/private.txt", "javascript:alert(1)", "https://user:password@example.com/" })
            {
                rejected = false; try { WebsiteCapture.ValidateUrl(url); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "unsafe website source rejected");
            }
            Check(WebsiteCapture.ValidateUrl("https://news.yahoo.co.jp/").Host == "news.yahoo.co.jp", "website URL accepted");
            using (Bitmap blank = new Bitmap(480, 320))
            using (Graphics g = Graphics.FromImage(blank))
            using (MemoryStream bytes = new MemoryStream())
            {
                g.Clear(Color.White); blank.Save(bytes, ImageFormat.Png);
                Check(!WebsiteCapture.HasVisibleContent(bytes.ToArray(), 480, 320), "blank website image rejected");
                g.FillRectangle(Brushes.Black, 20, 20, 100, 40); bytes.SetLength(0); blank.Save(bytes, ImageFormat.Png);
                Check(WebsiteCapture.HasVisibleContent(bytes.ToArray(), 480, 320), "nonblank website image accepted");
            }
            SceneElement site = LayoutCatalog.Widget("サイト", new Rectangle(0, 0, 480, 320));
            site.SourceUrl = "https://example.com/"; site.ViewportWidth = 960; site.RefreshMinutes = 5;
            SceneElement copy = site.Clone();
            Check(copy.SourceUrl == site.SourceUrl && copy.ViewportWidth == 960 && copy.RefreshMinutes == 5, "widget clone preserves settings");
            using (DummyTelemetryProvider provider = new DummyTelemetryProvider())
                foreach (DateTime date in new[] { new DateTime(2024, 2, 29), new DateTime(2026, 8, 31) })
                {
                    TelemetrySnapshot snapshot = provider.Capture(); snapshot.Timestamp = date;
                    SceneDocument scene = new SceneDocument(); scene.Elements.Add(LayoutCatalog.Widget("カレンダー", new Rectangle(0, 0, 480, 320)));
                    using (SceneRenderer renderer = new SceneRenderer(null))
                    using (Bitmap image = renderer.Render(scene, snapshot)) Check(image.Width == 480, "calendar leap and six-week rendering");
                }
        }
        private static int OnlineWidgets(string directory)
        {
            Directory.CreateDirectory(directory);
            ForecastDay[] forecast = WeatherForecast.Fetch(35.681, 139.767, CancellationToken.None);
            if (forecast.Length != 3) throw new Exception("Live Tokyo forecast missing.");
            Console.WriteLine("PASS: Tokyo live forecast (" + forecast[0].Date + ")");
            byte[] image = WebsiteCapture.Capture("https://news.yahoo.co.jp/", 480, 320, CancellationToken.None);
            File.WriteAllBytes(Path.Combine(directory, "yahoo-live.png"), image);
            Console.WriteLine("PASS: Yahoo website capture returned PNG; inspect contents visually.");
            using (DummyTelemetryProvider telemetry = new DummyTelemetryProvider())
            using (SceneRenderer renderer = new SceneRenderer(null) { OnlineEnabled = true })
            using (SceneRenderer websiteRenderer = new SceneRenderer(null) { OnlineEnabled = true })
            {
                SceneDocument scene = LayoutCatalog.Template(5);
                SceneDocument website = LayoutCatalog.Template(6);
                // Explicit online diagnostic only; normal tests never enable network.
                for (int i = 0; i < 25; i++)
                {
                    using (Bitmap frame = renderer.Render(scene, telemetry.Capture()))
                        if (i == 24) frame.Save(Path.Combine(directory, "calendar-weather-live.png"), ImageFormat.Png);
                    using (Bitmap frame = websiteRenderer.Render(website, telemetry.Capture()))
                        if (i == 24) frame.Save(Path.Combine(directory, "website-layout-live.png"), ImageFormat.Png);
                    Thread.Sleep(500);
                }
            }
            return 0;
        }
        private static void TemplateLayouts()
        {
            string directory = Path.Combine(Path.GetTempPath(), "layouts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (DummyTelemetryProvider provider = new DummyTelemetryProvider())
                {
                    TelemetrySnapshot snapshot = provider.Capture();
                    snapshot.CpuLoadPercent = snapshot.GpuLoadPercent = snapshot.RamLoadPercent = 100;
                    snapshot.CpuTemperatureC = snapshot.GpuTemperatureC = null;
                    snapshot.RamUsedMb = 131072;
                    snapshot.UploadMbps = snapshot.DownloadMbps = 1000;
                    FormulaEngine formulas = new FormulaEngine();
                    for (int layout = 0; layout < LayoutCatalog.LayoutNames.Length; layout++)
                        foreach (string metric in LayoutCatalog.Metrics)
                        {
                            int count = LayoutCatalog.Blocks(layout).Length;
                            string[] values = new string[count];
                            for (int i = 0; i < count; i++) values[i] = metric;
                            SceneDocument scene = LayoutCatalog.Create(layout, values);
                            HashSet<string> ids = new HashSet<string>();
                            int labels = 0;
                            foreach (SceneElement element in scene.Elements)
                            {
                                Check(ids.Add(element.Id), "unique element id");
                                Check(element.X >= 0 && element.Y >= 0 && element.Width > 0 && element.Height > 0 && element.Bounds.Right <= 480 && element.Bounds.Bottom <= 320, "template bounds");
                                if (element.Kind == SceneElementKind.Text)
                                {
                                    Check(formulas.EvaluateText(element.Formula, FormulaContext.FromSnapshot(snapshot)) != "#ERROR", "template formula " + metric);
                                    if (element.Name.EndsWith(" title")) labels++;
                                }
                            }
                            Check(labels == (Array.IndexOf(new[] { "空白", "カレンダー", "天気", "サイト", "ニュース" }, metric) >= 0 ? 0 : count), "every metric has a title");
                            string path = Path.Combine(directory, "roundtrip.scene.xml");
                            scene.Save(path);
                            SceneDocument loaded = SceneDocument.Load(path);
                            Check(loaded.QuickLayout == layout && loaded.QuickMetrics.Count == count && loaded.QuickMetrics[0] == metric, "quick settings round trip");
                            using (SceneRenderer renderer = new SceneRenderer(null))
                            using (Bitmap image = renderer.Render(loaded, snapshot)) Check(image.Width == 480 && image.Height == 320, "grid renders");
                        }
                }
                string oldPath = Path.Combine(directory, "legacy.xml");
                File.WriteAllText(oldPath, "<DashboardScene><Version>2</Version><Name>Legacy</Name><Elements /></DashboardScene>");
                SceneDocument legacy = SceneDocument.Load(oldPath);
                Check(legacy.QuickLayout == -1 && legacy.Elements.Count == 0, "legacy layout still loads");
                LayoutCatalog.EnsureTemplates(directory);
                for (int i = 0; i < LayoutCatalog.FileNames.Length; i++)
                    Check(SceneDocument.Load(Path.Combine(directory, LayoutCatalog.FileNames[i])).Name == LayoutCatalog.TemplateNames[i], "numbered template available");
                string template = Path.Combine(directory, LayoutCatalog.FileNames[0]);
                SceneDocument custom = SceneDocument.Load(template); custom.Name = "Custom"; custom.Save(template);
                byte[] before = File.ReadAllBytes(template);
                LayoutCatalog.EnsureTemplates(directory);
                Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(template)), "existing template not overwritten");
            }
            finally { Directory.Delete(directory, true); }
        }

        private static int RenderTemplates(string directory)
        {
            Directory.CreateDirectory(directory);
            using (Bitmap gallery = new Bitmap(1000, ((LayoutCatalog.FileNames.Length + 1) / 2) * 368 + 8))
            using (Graphics graphics = Graphics.FromImage(gallery))
            using (Font font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.Clear(Color.FromArgb(8, 12, 20));
                for (int i = 0; i < LayoutCatalog.FileNames.Length; i++)
                    using (DummyTelemetryProvider provider = new DummyTelemetryProvider())
                    using (SceneRenderer renderer = new SceneRenderer(null))
                    {
                        SceneDocument scene = SceneDocument.Load(Path.Combine(LayoutCatalog.DirectoryPath, LayoutCatalog.FileNames[i]));
                        for (int frame = 0; frame < 30; frame++)
                        {
                            TelemetrySnapshot snapshot = provider.Capture();
                            snapshot.Timestamp = new DateTime(2026, 10, 21, 10, 8, 0).AddSeconds(frame);
                            using (Bitmap image = renderer.Render(scene, snapshot))
                                if (frame == 29)
                                {
                                    image.Save(Path.Combine(directory, Path.GetFileNameWithoutExtension(LayoutCatalog.FileNames[i]) + ".png"), ImageFormat.Png);
                                    int x = 12 + (i % 2) * 496, y = 12 + (i / 2) * 368;
                                    graphics.DrawString(LayoutCatalog.TemplateNames[i], font, Brushes.White, x, y);
                                    graphics.DrawImageUnscaled(image, x, y + 28);
                                }
                        }
                    }
                gallery.Save(Path.Combine(directory, "templates.png"), ImageFormat.Png);
            }
            Console.WriteLine("PASS: rendered numbered template previews");
            return 0;
        }
        private static void RotationBehavior()
        {
            using (Bitmap frame = new Bitmap(480, 320))
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.FillRectangle(Brushes.Red, 0, 0, 240, 160);
                    g.FillRectangle(Brushes.Lime, 240, 0, 240, 160);
                    g.FillRectangle(Brushes.Blue, 0, 160, 240, 160);
                    g.FillRectangle(Brushes.White, 240, 160, 240, 160);
                }
                Color[][] expected = {
                    new[] { Color.Red, Color.Lime, Color.Blue, Color.White },
                    new[] { Color.Blue, Color.Red, Color.White, Color.Lime },
                    new[] { Color.White, Color.Blue, Color.Lime, Color.Red },
                    new[] { Color.Lime, Color.White, Color.Red, Color.Blue }
                };
                int[] angles = { 0, 90, 180, 270 };
                for (int i = 0; i < angles.Length; i++)
                    using (Bitmap rotated = FrameRotation.Apply(frame, angles[i]))
                    {
                        Check(rotated.Width == 480 && rotated.Height == 320, "rotation keeps output size");
                        int left = i % 2 == 0 ? 60 : 180, right = i % 2 == 0 ? 420 : 300;
                        Color[] actual = { rotated.GetPixel(left, 60), rotated.GetPixel(right, 60), rotated.GetPixel(left, 260), rotated.GetPixel(right, 260) };
                        for (int j = 0; j < 4; j++) Check(actual[j].ToArgb() == expected[i][j].ToArgb(), "clockwise quadrant mapping " + angles[i]);
                        if (i % 2 != 0) Check(rotated.GetPixel(0, 160).ToArgb() == Color.Black.ToArgb(), "portrait output letterboxed");
                    }
                Check(frame.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "rotation preserves source");
                Check(ThrowsOutOfRange(delegate { using (Bitmap ignored = FrameRotation.Apply(frame, 45)) { } }), "invalid rotation rejected");
            }
            FakeFrameSourceFactory sources = new FakeFrameSourceFactory();
            FakeFactory sessions = new FakeFactory();
            DisplayDevice a = Device("rotation-a"), b = Device("rotation-b");
            using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(sources, sessions, "red-layout", null))
            {
                // Rotated target first exercises late encoding of the shared unrotated frame.
                DisplayTarget[] targets = { new DisplayTarget(a, "red-layout", null, 90), new DisplayTarget(b, "red-layout", null) };
                using (UpdateResult result = Await(runtime.RefreshTargetsAsync(targets)))
                {
                    Check(AllSucceeded(result.Devices, 2), "mixed rotations send");
                    Check(result.Preview.Width == 320 && result.Preview.Height == 480, "portrait preview aspect");
                    Check(sessions.FirstPixel("rotation-a") == 0 && sessions.FirstPixel("rotation-b") == 0xF800, "per-target rotation reaches transport");
                }
                Check(sources.TotalCaptures == 1, "mixed rotations share one source capture");
                using (UpdateResult result = Await(runtime.RefreshTargetsAsync(targets)))
                    Check(result.Devices[0].Skipped && result.Devices[1].Skipped, "unchanged rotated frames skipped");
                using (UpdateResult result = Await(runtime.RefreshTargetsAsync(new[] { new DisplayTarget(a, "red-layout", null), targets[1] })))
                    Check(!result.Devices[0].Skipped && result.Devices[1].Skipped, "rotation change retransmits only changed output");
            }
            Check(ThrowsOutOfRange(delegate { new DisplayTarget(a, null, null, -90); }), "target validates rotation");
            Check(ThrowsOutOfRange(delegate { new DisplayAssignment().RotationDegrees = 45; }), "settings validate rotation");
            using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(new QuadrantFrameSource(), new FakeFactory()))
                foreach (int angle in new[] { 0, 90, 180, 270 })
                    using (UpdateResult result = Await(runtime.RefreshTargetsAsync(new[] { new DisplayTarget(a, null, null, angle) })))
                    {
                        bool portrait = angle == 90 || angle == 270;
                        Bitmap image = result.Preview;
                        Check(image.Width == (portrait ? 320 : 480) && image.Height == (portrait ? 480 : 320), "preview dimensions " + angle);
                        int top = portrait ? 180 : 60, bottom = portrait ? 300 : 260;
                        Check(image.GetPixel(60, top).ToArgb() == Color.Red.ToArgb(), "preview top stays top " + angle);
                        Check(image.GetPixel(60, bottom).ToArgb() == Color.Blue.ToArgb(), "preview bottom stays bottom " + angle);
                        if (portrait) Check(image.GetPixel(160, 0).ToArgb() == Color.Black.ToArgb(), "upright preview padding");
                    }
        }

        private sealed class QuadrantFrameSource : IFrameSource
        {
            public Bitmap Capture()
            {
                Bitmap image = new Bitmap(480, 320);
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.FillRectangle(Brushes.Red, 0, 0, 480, 160);
                    g.FillRectangle(Brushes.Blue, 0, 160, 480, 160);
                }
                return image;
            }
            public void Dispose() { }
        }

        private static int BenchmarkEncoding()
        {
            using (Bitmap frame = new Bitmap(480, 320, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                        frame.SetPixel(x, y, Color.FromArgb(255, (x * 17 + y * 3) & 255, (x * 5 + y * 11) & 255, (x * 13 + y * 7) & 255));
                byte[] expected = EncodeWithGetPixel(frame); byte[] actual = RevAFrameEncoder.Encode(frame);
                if (!SameBytes(expected, actual)) { Console.Error.WriteLine("FAIL: benchmark encoders differ during warmup"); return 1; }
                EncodeWithGetPixel(frame); RevAFrameEncoder.Encode(frame);
                List<double> oldTimes = new List<double>(), newTimes = new List<double>();
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    Stopwatch watch = Stopwatch.StartNew(); expected = EncodeWithGetPixel(frame); watch.Stop(); oldTimes.Add(watch.Elapsed.TotalMilliseconds);
                    watch.Restart(); actual = RevAFrameEncoder.Encode(frame); watch.Stop(); newTimes.Add(watch.Elapsed.TotalMilliseconds);
                    if (!SameBytes(expected, actual)) { Console.Error.WriteLine("FAIL: benchmark encoders differ at iteration " + iteration); return 1; }
                }
                oldTimes.Sort(); newTimes.Sort();
                Console.WriteLine("GetPixel median ms: " + Median(oldTimes).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("LockBits median ms: " + Median(newTimes).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("PASS: 10 measured iterations produced identical RGB565 bytes.");
                return 0;
            }
        }

        private static byte[] EncodeWithGetPixel(Bitmap frame)
        {
            byte[] result = new byte[frame.Width * frame.Height * 2]; int offset = 0;
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    Color color = frame.GetPixel(x, y); int packed = ((color.R >> 3) << 11) | ((color.G >> 2) << 5) | (color.B >> 3);
                    result[offset++] = (byte)packed; result[offset++] = (byte)(packed >> 8);
                }
            return result;
        }
        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
        private static double Median(List<double> values) { return (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0; }

        private static void FormulaAndScene()
        {
            using (DummyTelemetryProvider source = new DummyTelemetryProvider())
            {
                FormulaContext context = FormulaContext.FromSnapshot(source.Capture()); FormulaEngine engine = new FormulaEngine();
                Check(engine.EvaluateNumber("=[CpuLoad]", context) == engine.EvaluateNumber("=[STATS.CPU.PERCENTAGE]", context), "legacy formula alias");
                Check(engine.EvaluateText("=IF([STATS.CPU.PERCENTAGE]>0,\"yes\",\"no\")", context) == "yes", "formula condition");
            }
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".scene.xml");
            try { SceneDocument scene = SceneDocument.CreateDefault(); scene.Name = "Round trip"; scene.Save(path); SceneDocument loaded = SceneDocument.Load(path); Check(loaded.Name == scene.Name && loaded.Elements.Count == scene.Elements.Count, "scene round trip"); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void Rendering()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            System.Reflection.Assembly editor = System.Reflection.Assembly.ReflectionOnlyLoadFrom(Path.Combine(directory, "TurzxPanelWorks.Editor.exe"));
            foreach (System.Reflection.AssemblyName reference in editor.GetReferencedAssemblies())
                Check(reference.Name.IndexOf("Transport", StringComparison.OrdinalIgnoreCase) < 0 && reference.Name.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase) < 0, "editor assembly excludes transport/runtime");
            foreach (System.Reflection.AssemblyName reference in typeof(SceneRenderer).Assembly.GetReferencedAssemblies())
                Check(reference.Name != "System.Windows.Forms", "renderer excludes editing UI");
            using (DummyTelemetryProvider source = new DummyTelemetryProvider()) using (SceneRenderer renderer = new SceneRenderer(null)) using (Bitmap frame = renderer.Render(SceneDocument.CreateDefault(), source.Capture()))
                Check(frame.Width == 480 && frame.Height == 320, "render size without bundled asset");
        }

        private static void Encoding()
        {
            foreach (PixelFormat format in new[] { PixelFormat.Format24bppRgb, PixelFormat.Format32bppArgb })
            using (Bitmap frame = new Bitmap(480, 320, format))
            using (MemoryStream output = new MemoryStream())
            {
                frame.SetPixel(0, 0, Color.Red); frame.SetPixel(1, 0, Color.Lime); frame.SetPixel(2, 0, Color.Blue);
                frame.SetPixel(0, 1, Color.White); frame.SetPixel(479, 319, Color.FromArgb(255, 8, 4, 16));
                byte[] pixels = RevAFrameEncoder.Encode(frame);
                Check(pixels.Length == 307200, format + " encoded length");
                Check(pixels[0] == 0 && pixels[1] == 248 && pixels[2] == 224 && pixels[3] == 7 && pixels[4] == 31 && pixels[5] == 0, format + " first-row order");
                Check(pixels[960] == 255 && pixels[961] == 255, format + " second-row order");
                Check(pixels[pixels.Length - 2] == 34 && pixels[pixels.Length - 1] == 8, format + " final-pixel order");
                RevAFrameEncoder.WriteFrame(output, frame); byte[] data = output.ToArray();
                byte[] header = { 0, 0, 0, 0, 0, 121, 102, 1, 224, 1, 64, 0, 0, 0, 0, 0, 0, 0, 7, 125, 63, 197 };
                Check(data.Length == 307222, format + " framed length"); bool same = true; for (int i = 0; i < header.Length; i++) same &= data[i] == header[i]; Check(same, format + " protocol header");
            }

            int stride = 480 * 3; IntPtr storage = Marshal.AllocHGlobal(stride * 320);
            try
            {
                using (Bitmap negativeStride = new Bitmap(480, 320, -stride, PixelFormat.Format24bppRgb, IntPtr.Add(storage, stride * 319)))
                {
                    negativeStride.SetPixel(0, 0, Color.Red); negativeStride.SetPixel(0, 1, Color.Lime); negativeStride.SetPixel(479, 319, Color.Blue);
                    byte[] pixels = RevAFrameEncoder.Encode(negativeStride);
                    Check(pixels[0] == 0 && pixels[1] == 248, "negative stride first row");
                    Check(pixels[960] == 224 && pixels[961] == 7, "negative stride second row");
                    Check(pixels[pixels.Length - 2] == 31 && pixels[pixels.Length - 1] == 0, "negative stride final row");
                }
            }
            finally { Marshal.FreeHGlobal(storage); }
        }

        private static void RefreshOptionBounds()
        {
            Check(new RefreshOptions().IntervalMilliseconds == 1000, "default refresh interval");
            RefreshOptions low = new RefreshOptions(); low.IntervalMilliseconds = 250; Check(low.IntervalMilliseconds == 250, "minimum refresh interval");
            RefreshOptions high = new RefreshOptions(); high.IntervalMilliseconds = 60000; Check(high.IntervalMilliseconds == 60000, "maximum refresh interval");
            Check(ThrowsOutOfRange(delegate { new RefreshOptions().IntervalMilliseconds = 249; }), "reject interval below minimum");
            Check(ThrowsOutOfRange(delegate { new RefreshOptions().IntervalMilliseconds = 60001; }), "reject interval above maximum");
        }
        private static bool ThrowsOutOfRange(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }

        private static void RefreshOptionRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".options.xml");
            try
            {
                RefreshOptions saved = new RefreshOptions { IntervalMilliseconds = 2750 };
                saved.Assignments.Add(new DisplayAssignment { StableId = "display-one", ScenePath = "layouts/one.scene.xml", BackgroundPath = "backgrounds/one.png", RotationDegrees = 270 });
                saved.Assignments.Add(new DisplayAssignment { StableId = "display-two", ScenePath = "layouts/two.scene.xml", BackgroundPath = null });
                saved.Save(path); RefreshOptions loaded = RefreshOptions.Load(path);
                Check(loaded.IntervalMilliseconds == 2750 && loaded.Assignments.Count == 2, "refresh options interval and assignment count round trip");
                Check(loaded.Assignments[0].RotationDegrees == 270 && loaded.Assignments[1].RotationDegrees == 0, "rotation settings round trip");
                string legacy = File.ReadAllText(path).Replace("<RotationDegrees>270</RotationDegrees>", "").Replace("<RotationDegrees>0</RotationDegrees>", "");
                File.WriteAllText(path, legacy);
                Check(RefreshOptions.Load(path).Assignments[0].RotationDegrees == 0, "old settings default to zero rotation");
                Check(loaded.Assignments[0].StableId == "display-one" && loaded.Assignments[0].ScenePath == "layouts/one.scene.xml" && loaded.Assignments[0].BackgroundPath == "backgrounds/one.png", "refresh assignment fields round trip");
                Check(loaded.Assignments[1].StableId == "display-two" && loaded.Assignments[1].ScenePath == "layouts/two.scene.xml" && loaded.Assignments[1].BackgroundPath == null, "refresh assignment null background round trip");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void SharedTelemetryLifetime()
        {
            CountingTelemetry telemetry = new CountingTelemetry(); SceneFrameSourceFactory factory = new SceneFrameSourceFactory(telemetry);
            factory.BeginCycle();
            using (IFrameSource first = factory.Create("missing-one.scene.xml", null))
            using (IFrameSource second = factory.Create("missing-two.scene.xml", null))
            using (Bitmap firstFrame = first.Capture())
            using (Bitmap secondFrame = second.Capture())
                Check(firstFrame.Width == 480 && secondFrame.Width == 480 && telemetry.CaptureCount == 1, "scene sources share one telemetry capture per cycle");
            factory.Dispose();
            Check(telemetry.DisposeCount == 1, "scene source factory owns and disposes telemetry once");
        }

        private static void RuntimeBehavior()
        {
            DisplayDevice a = Device("device-a"), b = Device("device-b"), c = Device("device-c");
            MutableFrameSource source = new MutableFrameSource(Color.Red, 480, 320); FakeFactory factory = new FakeFactory();
            using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(source, factory))
            {
                DisplayDevice duplicateA = new DisplayDevice("device-a", "OTHER_PORT", "duplicate", 480, 320);
                using (UpdateResult initial = Await(runtime.RefreshAsync(new[] { a, duplicateA, b })))
                    Check(initial != null && initial.Preview.Width == 480 && AllSucceeded(initial.Devices, 2), "initial multi-target refresh deduplicates stable IDs");
                Check(factory.ConnectCount("device-a") == 1 && factory.SendCount("device-a") == 1, "one connection and send");

                using (UpdateResult unchanged = Await(runtime.RefreshAsync(new[] { a, b })))
                    Check(unchanged.Devices[0].Skipped && unchanged.Devices[1].Skipped, "unchanged pixels skipped");
                Check(factory.ConnectCount("device-a") == 1 && factory.SendCount("device-a") == 1, "unchanged frame reuses session without send");

                using (UpdateResult added = Await(runtime.RefreshAsync(new[] { a, b, c })))
                    Check(added.Devices[0].Skipped && added.Devices[1].Skipped && !added.Devices[2].Skipped && added.Devices[2].Succeeded, "new target receives unchanged frame");

                source.Color = Color.Blue;
                using (UpdateResult changed = Await(runtime.RefreshAsync(new[] { a, b, c })))
                    Check(AllSucceeded(changed.Devices, 3) && !changed.Devices[0].Skipped, "changed pixels sent to all");
                Check(factory.ConnectCount("device-a") == 1 && factory.SendCount("device-a") == 2, "session reused across changed frames");

                factory.FailSendIds.Add("device-b"); source.Color = Color.Lime;
                using (UpdateResult failed = Await(runtime.RefreshAsync(new[] { a, b, c })))
                    Check(failed.Devices[0].Succeeded && failed.Devices[1].Error != null && failed.Devices[2].Succeeded, "failure isolated by device");
                int failedSends = factory.SendCount("device-b"); factory.FailSendIds.Remove("device-b"); source.Color = Color.Yellow;
                using (UpdateResult latched = Await(runtime.RefreshAsync(new[] { a, b, c })))
                    Check(latched.Devices[1].Error != null && factory.SendCount("device-b") == failedSends, "failed device not automatically retried");

                Await(runtime.ResetAsync());
                using (UpdateResult recovered = Await(runtime.RefreshAsync(new[] { a, b, c })))
                    Check(recovered.Devices[1].Succeeded && !recovered.Devices[1].Skipped, "reset clears failure and frame state");
                Check(factory.ConnectCount("device-b") == 2 && factory.DisposalCount("device-a") == 1, "reset recreates sessions");
            }

            MutableFrameSource invalidSource = new MutableFrameSource(Color.Black, 320, 240); FakeFactory invalidFactory = new FakeFactory();
            using (DisplayUpdateCoordinator invalid = new DisplayUpdateCoordinator(invalidSource, invalidFactory))
            {
                bool rejected = false;
                try { using (UpdateResult ignored = Await(invalid.RefreshAsync(new[] { a }))) { } } catch (AggregateException error) { if (!(error.InnerException is ArgumentException)) throw; rejected = true; } catch (ArgumentException) { rejected = true; }
                Check(rejected, "invalid dimensions rejected");
                Check(invalidFactory.TotalSends == 0, "invalid dimensions send zero frames");
            }
        }

        private static void BusyAndClose()
        {
            DisplayDevice device = Device("device-blocked"); BlockingFrameSource source = new BlockingFrameSource(); FakeFactory factory = new FakeFactory();
            DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(source, factory);
            Task<UpdateResult> active = runtime.RefreshAsync(new[] { device });
            Check(source.Entered.WaitOne(3000), "blocked refresh entered capture");
            Task<UpdateResult> busy = runtime.RefreshAsync(new[] { device });
            Check(busy.Wait(3000) && busy.Result == null, "overlapping refresh returns null");
            Task close = runtime.CloseAsync(); Check(!close.Wait(100), "close waits for in-flight refresh"); source.Release.Set();
            Check(active.Wait(10000) && close.Wait(10000), "close completes after refresh");
            if (active.Status == TaskStatus.RanToCompletion && active.Result != null) active.Result.Dispose();
            Check(source.DisposeCount == 1 && factory.DisposalCount("device-blocked") == 1, "close disposes dependencies once");
            Check(Await(runtime.RefreshAsync(new[] { device })) == null, "refresh after close returns null");
            Await(runtime.CloseAsync()); Check(source.DisposeCount == 1, "close is idempotent"); runtime.Dispose();
        }

        private static void FaultCleanup()
        {
            DisplayDevice device = Device("fault-device"); ToggleNullFrameSource nullSource = new ToggleNullFrameSource(); FakeFactory sessions = new FakeFactory();
            DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(nullSource, sessions);
            using (UpdateResult initial = Await(runtime.RefreshAsync(new[] { device }))) Check(initial.Devices[0].Succeeded, "fault cleanup setup sends initial frame");
            nullSource.ReturnNull = true; bool rejected = false;
            try { using (UpdateResult ignored = Await(runtime.RefreshAsync(new[] { device }))) { } }
            catch (AggregateException error) { rejected = error.InnerException is InvalidOperationException || error.InnerException is ArgumentException; }
            catch (InvalidOperationException) { rejected = true; }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "null frame capture rejected with a domain exception");
            Check(sessions.DisposalCount("fault-device") == 1, "batch fault disposes existing sessions");
            try { Await(runtime.CloseAsync()); } catch (AggregateException) { } runtime.Dispose();

            ThrowingDisposeSourceFactory throwing = new ThrowingDisposeSourceFactory(); FakeFactory otherSessions = new FakeFactory();
            DisplayUpdateCoordinator cleanup = new DisplayUpdateCoordinator(throwing, otherSessions, "default-layout", null);
            using (UpdateResult populated = Await(cleanup.RefreshTargetsAsync(new[]
            {
                new DisplayTarget(Device("cleanup-a"), "cleanup-one", null), new DisplayTarget(Device("cleanup-b"), "cleanup-two", null)
            }))) Check(AllSucceeded(populated.Devices, 2), "throwing-dispose cleanup setup");
            try { Await(cleanup.CloseAsync()); } catch (AggregateException) { }
            Check(throwing.SourceDisposeCount("cleanup-one") == 1 && throwing.SourceDisposeCount("cleanup-two") == 1, "all frame sources disposed after one source throws");
            Check(throwing.DisposeCount == 1, "frame source factory disposed after source dispose throws");
        }

        private static void TargetSpecificFrames()
        {
            DisplayDevice a = Device("layout-a"), b = Device("layout-b");
            FakeFrameSourceFactory sources = new FakeFrameSourceFactory(); FakeFactory sessions = new FakeFactory();
            DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(sources, sessions, "default-layout", null);
            using (UpdateResult different = Await(runtime.RefreshTargetsAsync(new[]
            {
                new DisplayTarget(a, "red-layout", null), new DisplayTarget(b, "blue-layout", null)
            })))
            {
                Check(AllSucceeded(different.Devices, 2), "different target layouts succeed");
                Check(sessions.FirstPixel("layout-a") == 0xF800 && sessions.FirstPixel("layout-b") == 0x001F, "different target layouts produce different RGB565 payloads");
            }
            Check(sources.BeginCycleCount == 1 && sources.CaptureCount("red-layout") == 1 && sources.CaptureCount("blue-layout") == 1, "one begin and capture per unique layout");

            int beforeShared = sources.TotalCaptures;
            using (UpdateResult shared = Await(runtime.RefreshTargetsAsync(new[]
            {
                new DisplayTarget(a, "shared-layout", null), new DisplayTarget(b, "shared-layout", null)
            })))
                Check(AllSucceeded(shared.Devices, 2), "shared target layout succeeds");
            Check(sources.BeginCycleCount == 2 && sources.TotalCaptures == beforeShared + 1, "same layout captured once within cycle");
            Check(sessions.FirstPixel("layout-a") == 0x07E0 && sessions.FirstPixel("layout-b") == 0x07E0, "shared layout payload delivered to both targets");
            Check(sources.DisposalCount("red-layout") == 1 && sources.DisposalCount("blue-layout") == 1, "unused layout sources evicted");

            int beforeBlue = sources.CaptureCount("blue-layout"); int sendsA = sessions.SendCount("layout-a"), sendsB = sessions.SendCount("layout-b");
            using (UpdateResult changed = Await(runtime.RefreshTargetsAsync(new[]
            {
                new DisplayTarget(a, "blue-layout", null), new DisplayTarget(b, "blue-layout", null)
            })))
                Check(AllSucceeded(changed.Devices, 2), "target layout change succeeds");
            Check(sources.BeginCycleCount == 3 && sources.CaptureCount("blue-layout") == beforeBlue + 1, "layout cache cleared between cycles");
            Check(sources.CreateCount("blue-layout") == 2, "evicted layout source recreated when selected again");
            Check(sessions.SendCount("layout-a") == sendsA + 1 && sessions.SendCount("layout-b") == sendsB + 1, "layout change updates each target");

            Await(runtime.CloseAsync()); runtime.Dispose();
            Check(sources.DisposeCount == 1, "frame source factory disposed once");
        }

        private static bool AllSucceeded(IList<DisplaySendResult> values, int count) { if (values == null || values.Count != count) return false; foreach (DisplaySendResult value in values) if (!value.Succeeded) return false; return true; }
        private static DisplayDevice Device(string id) { return new DisplayDevice(id, "PORT_" + id, id, 480, 320); }
        private static T Await<T>(Task<T> task) { task.Wait(); return task.Result; }
        private static void Await(Task task) { task.Wait(); }

        private sealed class MutableFrameSource : IFrameSource
        {
            public Color Color; private readonly int width, height; public int DisposeCount;
            public MutableFrameSource(Color color, int width, int height) { Color = color; this.width = width; this.height = height; }
            public Bitmap Capture() { Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb); using (Graphics graphics = Graphics.FromImage(result)) graphics.Clear(Color); return result; }
            public void Dispose() { DisposeCount++; }
        }
        private sealed class ToggleNullFrameSource : IFrameSource
        {
            public bool ReturnNull; public int DisposeCount;
            public Bitmap Capture() { return ReturnNull ? null : new Bitmap(480, 320, PixelFormat.Format32bppArgb); }
            public void Dispose() { DisposeCount++; }
        }
        private sealed class CountingTelemetry : ITelemetryProvider
        {
            public int CaptureCount, DisposeCount; public string Name { get { return "counting"; } }
            public TelemetrySnapshot Capture() { CaptureCount++; return new TelemetrySnapshot { Timestamp = new DateTime(2020, 1, 2, 3, 4, 5), AdditionalValues = new Dictionary<string, object>() }; }
            public void Dispose() { DisposeCount++; }
        }
        private sealed class BlockingFrameSource : IFrameSource
        {
            public readonly ManualResetEvent Entered = new ManualResetEvent(false), Release = new ManualResetEvent(false); public int DisposeCount;
            public Bitmap Capture() { Entered.Set(); Release.WaitOne(); return new Bitmap(480, 320, PixelFormat.Format32bppArgb); }
            public void Dispose() { DisposeCount++; Entered.Dispose(); Release.Dispose(); }
        }
        private sealed class FakeFactory : IDisplaySessionFactory
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, int> connects = new Dictionary<string, int>(), sends = new Dictionary<string, int>(), disposals = new Dictionary<string, int>();
            private readonly Dictionary<string, int> firstPixels = new Dictionary<string, int>();
            public readonly HashSet<string> FailSendIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int TotalSends { get { lock (sync) { int total = 0; foreach (int value in sends.Values) total += value; return total; } } }
            public int ConnectCount(string id) { lock (sync) return Value(connects, id); } public int SendCount(string id) { lock (sync) return Value(sends, id); } public int DisposalCount(string id) { lock (sync) return Value(disposals, id); }
            public int FirstPixel(string id) { lock (sync) return Value(firstPixels, id); }
            public IDisplaySession Connect(DisplayDevice device) { lock (sync) Increment(connects, device.StableId); return new FakeSession(device, this); }
            private static int Value(Dictionary<string, int> values, string id) { int value; return values.TryGetValue(id, out value) ? value : 0; }
            private static void Increment(Dictionary<string, int> values, string id) { values[id] = Value(values, id) + 1; }
            private sealed class FakeSession : IDisplaySession
            {
                private readonly FakeFactory owner; public DisplayDevice Device { get; private set; }
                public FakeSession(DisplayDevice device, FakeFactory owner) { Device = device; this.owner = owner; }
                public void SendFrame(byte[] rgb565) { lock (owner.sync) { Increment(owner.sends, Device.StableId); owner.firstPixels[Device.StableId] = rgb565[0] | (rgb565[1] << 8); if (owner.FailSendIds.Contains(Device.StableId)) throw new IOException("synthetic send failure"); } }
                public void Dispose() { lock (owner.sync) Increment(owner.disposals, Device.StableId); }
            }
        }

        private sealed class FakeFrameSourceFactory : IFrameSourceFactory
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, int> captures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, int> creates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, int> disposals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int BeginCycleCount, DisposeCount;
            public int TotalCaptures { get { lock (sync) { int total = 0; foreach (int value in captures.Values) total += value; return total; } } }
            public int CaptureCount(string layout) { lock (sync) { int value; return captures.TryGetValue(layout, out value) ? value : 0; } }
            public int CreateCount(string layout) { lock (sync) { int value; return creates.TryGetValue(layout, out value) ? value : 0; } }
            public int DisposalCount(string layout) { lock (sync) { int value; return disposals.TryGetValue(layout, out value) ? value : 0; } }
            public void BeginCycle() { BeginCycleCount++; }
            public IFrameSource Create(string scenePath, string backgroundPath) { lock (sync) { int count; creates.TryGetValue(scenePath, out count); creates[scenePath] = count + 1; } return new LayoutFrameSource(this, scenePath); }
            public void Dispose() { DisposeCount++; }
            private Color ColorFor(string layout)
            {
                if (String.Equals(layout, "red-layout", StringComparison.OrdinalIgnoreCase)) return Color.Red;
                if (String.Equals(layout, "blue-layout", StringComparison.OrdinalIgnoreCase)) return Color.Blue;
                return Color.Lime;
            }
            private sealed class LayoutFrameSource : IFrameSource
            {
                private readonly FakeFrameSourceFactory owner; private readonly string layout;
                public LayoutFrameSource(FakeFrameSourceFactory owner, string layout) { this.owner = owner; this.layout = layout; }
                public Bitmap Capture()
                {
                    lock (owner.sync) { int count; owner.captures.TryGetValue(layout, out count); owner.captures[layout] = count + 1; }
                    Bitmap frame = new Bitmap(480, 320, PixelFormat.Format32bppArgb); using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(owner.ColorFor(layout)); return frame;
                }
                public void Dispose() { lock (owner.sync) { int count; owner.disposals.TryGetValue(layout, out count); owner.disposals[layout] = count + 1; } }
            }
        }

        private sealed class ThrowingDisposeSourceFactory : IFrameSourceFactory
        {
            private readonly object sync = new object(); private readonly Dictionary<string, int> disposals = new Dictionary<string, int>(); public int DisposeCount;
            public void BeginCycle() { }
            public IFrameSource Create(string scenePath, string backgroundPath) { return new ThrowingSource(this, scenePath); }
            public int SourceDisposeCount(string scene) { lock (sync) { int value; return disposals.TryGetValue(scene, out value) ? value : 0; } }
            public void Dispose() { DisposeCount++; }
            private sealed class ThrowingSource : IFrameSource
            {
                private readonly ThrowingDisposeSourceFactory owner; private readonly string scene;
                public ThrowingSource(ThrowingDisposeSourceFactory owner, string scene) { this.owner = owner; this.scene = scene; }
                public Bitmap Capture() { return new Bitmap(480, 320, PixelFormat.Format32bppArgb); }
                public void Dispose() { lock (owner.sync) { int count; owner.disposals.TryGetValue(scene, out count); owner.disposals[scene] = count + 1; } throw new IOException("synthetic dispose failure"); }
            }
        }
    }
}
