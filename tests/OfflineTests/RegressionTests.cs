// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Threading;

namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private static void Run(string name, Action test)
        {
            int before = failures;
            try { test(); } catch (Exception error) { failures++; Console.Error.WriteLine(name + ": " + error); }
            Console.WriteLine((before == failures ? "PASS: " : "FAIL: ") + name);
        }
        private static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, name);
        }
        private static void PersistenceFailures()
        {
            string dir = Path.Combine(Path.GetTempPath(), "persistence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "input.xml");
                Check(RefreshOptions.Load(path).IntervalMilliseconds == 1000, "missing settings default");
                foreach (string xml in new[] { "<broken", "<!DOCTYPE DashboardScene [<!ENTITY x 'expanded'>]><DashboardScene><Name>&x;</Name></DashboardScene>" })
                {
                    File.WriteAllText(path, xml);
                    Reject(delegate { SceneDocument.Load(path); }, "strict scene rejects malformed XML/DTD");
                    Check(SceneDocument.LoadOrDefault(path).Elements.Count > 0, "explicit fallback remains usable");
                }
                foreach (string xml in new[] { "<broken", "<!DOCTYPE RefreshOptions><RefreshOptions/>", "<RefreshOptions><IntervalMilliseconds>1</IntervalMilliseconds></RefreshOptions>", "<RefreshOptions><Assignments><DisplayAssignment><RotationDegrees>45</RotationDegrees></DisplayAssignment></Assignments></RefreshOptions>" })
                {
                    File.WriteAllText(path, xml);
                    Reject(delegate { RefreshOptions.Load(path); }, "settings reject invalid XML/DTD/value");
                }
            }
            finally { Directory.Delete(dir, true); }
        }
        private static void FrameReload()
        {
            string dir = Path.Combine(Path.GetTempPath(), "reload-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "scene.xml");
            try
            {
                SceneDocument scene = new SceneDocument();
                scene.Elements.Add(new SceneElement { Kind = SceneElementKind.Rectangle, Width = 480, Height = 320, FillColorArgb = Color.Red.ToArgb() });
                scene.Save(path);
                using (SceneFrameSource source = new SceneFrameSource(path, new SceneRenderer(null), new DummyTelemetryProvider()))
                {
                    using (Bitmap frame = source.Capture()) Check(frame.GetPixel(200, 200).ToArgb() == Color.Red.ToArgb(), "initial layout rendered");
                    DateTime stamp = File.GetLastWriteTimeUtc(path);
                    scene.Elements[0].FillColorArgb = Color.Blue.ToArgb(); scene.Save(path); File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
                    using (Bitmap frame = source.Capture()) Check(frame.GetPixel(200, 200).ToArgb() == Color.Blue.ToArgb(), "changed layout reloaded");
                    File.WriteAllText(path, "<broken"); File.SetLastWriteTimeUtc(path, stamp.AddSeconds(4));
                    Reject(delegate { using (Bitmap ignored = source.Capture()) { } }, "invalid edit stops frame");
                    Reject(delegate { using (Bitmap ignored = source.Capture()) { } }, "invalid edit does not fall back to cached frame");
                    scene.Save(path); File.SetLastWriteTimeUtc(path, stamp.AddSeconds(6));
                    using (Bitmap frame = source.Capture()) Check(frame.GetPixel(200, 200).ToArgb() == Color.Blue.ToArgb(), "corrected layout recovers");
                }
            }
            finally { Directory.Delete(dir, true); }
        }
        private static void EncoderInputFailures()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                foreach (byte[] pixels in new[] { null, new byte[0], new byte[307199], new byte[307201] })
                {
                    bool rejected = false;
                    try { RevAFrameEncoder.WriteEncodedFrame(stream, pixels); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && stream.Length == 0, "invalid payload writes no bytes");
                }
            }
            using (Bitmap wrong = new Bitmap(320, 480))
            {
                bool rejected = false; try { RevAFrameEncoder.Encode(wrong); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "portrait input must be rotated before USB encoding");
            }
        }
        private static void CalendarPositions()
        {
            using (Bitmap image = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                WidgetRenderer.DrawCalendar(graphics, new Rectangle(0, 0, 480, 320), new DateTime(2024, 2, 29));
                Check(image.GetPixel(271, 231).ToArgb() == Color.FromArgb(42, 91, 116).ToArgb(), "leap day highlighted Thursday fifth row");
                WidgetRenderer.DrawCalendar(graphics, new Rectangle(0, 0, 480, 320), new DateTime(2026, 8, 31));
                Check(image.GetPixel(73, 271).ToArgb() == Color.FromArgb(42, 91, 116).ToArgb(), "August 31 highlighted Monday sixth row");
                Check(image.GetPixel(271, 231).ToArgb() != Color.FromArgb(42, 91, 116).ToArgb(), "previous month highlight removed");
            }
        }
        private static void AwaitIdle(WidgetRenderer renderer)
        { if (!SpinWait.SpinUntil(delegate { return renderer.IsIdle; }, 5000)) throw new TimeoutException("Widget worker did not finish"); }
        private static void AsyncWeather()
        {
            DateTime now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc); int calls = 0; bool fail = false;
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            using (Bitmap image = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(image))
            using (WidgetRenderer renderer = new WidgetRenderer(delegate(double lat, double lon, CancellationToken token) {
                Interlocked.Increment(ref calls); entered.Set(); release.Wait(token);
                if (fail) throw new IOException("simulated outage");
                return new[] { new ForecastDay { Date = "10/21", Condition = "晴れ", High = "24", Low = "15", Rain = "10" } };
            }, delegate { return now; }))
            {
                SceneElement weather = LayoutCatalog.Widget("天気", new Rectangle(0, 0, 480, 320)); weather.RefreshMinutes = 10;
                renderer.Draw(graphics, weather, now); Check(calls == 0 && renderer.IsIdle, "offline mode starts no fetch");
                renderer.OnlineEnabled = true; renderer.Draw(graphics, weather, now);
                Check(entered.Wait(5000), "background fetch started");
                for (int i = 0; i < 20; i++) renderer.Draw(graphics, weather, now);
                Check(calls == 1, "drawing stays responsive and coalesces pending fetch");
                release.Set(); AwaitIdle(renderer);
                now = now.AddMinutes(9); renderer.Draw(graphics, weather, now); Check(calls == 1, "no early refresh");
                fail = true; now = now.AddMinutes(1); renderer.Draw(graphics, weather, now); AwaitIdle(renderer); Check(calls == 2, "scheduled failure completed");
                now = now.AddSeconds(59); renderer.Draw(graphics, weather, now); Check(calls == 2, "failure retry respects backoff");
                fail = false; now = now.AddSeconds(1); renderer.Draw(graphics, weather, now); AwaitIdle(renderer); Check(calls == 3, "retry recovers after one minute");
            }
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim cancelled = new ManualResetEventSlim())
            using (Bitmap image = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                WidgetRenderer renderer = new WidgetRenderer(delegate(double lat, double lon, CancellationToken token) {
                    entered.Set(); token.WaitHandle.WaitOne(); cancelled.Set(); token.ThrowIfCancellationRequested(); return null;
                }, delegate { return now; });
                try { renderer.OnlineEnabled = true; renderer.Draw(graphics, LayoutCatalog.Widget("天気", new Rectangle(0, 0, 480, 320)), now); Check(entered.Wait(5000), "cancellable fetch started"); }
                finally { renderer.Dispose(); }
                Check(cancelled.Wait(5000), "dispose cancels in-flight fetch"); AwaitIdle(renderer);
            }
        }
    }
}
