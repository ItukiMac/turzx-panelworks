// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private sealed class SampleProbe : ITelemetryProvider
        {
            public int Captures, Disposals;
            public bool Fail;
            public ManualResetEventSlim Entered, Release, Second;
            public string Name { get { return "timing probe"; } }
            public TelemetrySnapshot Capture()
            {
                int count = Interlocked.Increment(ref Captures);
                if (Entered != null) Entered.Set();
                if (Release != null) Release.Wait();
                if (count >= 2 && Second != null) Second.Set();
                if (Fail) throw new IOException("test acquisition failure");
                return new TelemetrySnapshot { Timestamp = new DateTime(2026, 10, 21).AddSeconds(count), CpuLoadPercent = count };
            }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }
        private static void WaitSample(TimedTelemetryProvider sampler)
        { if (!SpinWait.SpinUntil(delegate { return sampler.IsIdle; }, 5000)) throw new TimeoutException("Hardware sample"); }
        private static void IndependentTiming()
        {
            long now = 0; int interval = 1000; SampleProbe source = new SampleProbe();
            using (TimedTelemetryProvider sampler = new TimedTelemetryProvider(source, delegate { return interval; }, delegate { return now; }, false))
            {
                sampler.Poll(); WaitSample(sampler);
                for (int i = 0; i < 100; i++) sampler.Capture();
                Check(source.Captures == 1, "render reads never acquire hardware");
                now = 999; sampler.Poll(); Check(source.Captures == 1, "hardware interval lower boundary");
                now = 1000; sampler.Poll(); WaitSample(sampler); Check(source.Captures == 2, "hardware sample at due time");
                interval = 5000; now = 2000; sampler.Poll(); Check(source.Captures == 2, "longer hardware interval applied");
                now = 6000; source.Fail = true; sampler.Poll(); WaitSample(sampler);
                Check(sampler.Capture().CpuLoadPercent == 2, "acquisition failure retains last sample");
                interval = 250; now = 6250; source.Fail = false; sampler.Poll(); WaitSample(sampler);
                Check(source.Captures == 4 && sampler.Capture().CpuLoadPercent == 4, "shorter interval and acquisition recovery");
                TelemetrySnapshot sample = sampler.Capture();
                DateTime displayTime = sample.Timestamp.AddMinutes(5);
                FormulaContext context = FormulaContext.FromSnapshot(sample, displayTime);
                Check((DateTime)context["DATE.HOUR"] == displayTime && (double)context["STATS.CPU.PERCENTAGE"] == 4 && sample.Timestamp != displayTime, "clock advances without changing hardware timestamp or values");
            }
            Check(source.Disposals == 1, "sampler owns provider disposal");
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            {
                SampleProbe blocked = new SampleProbe { Entered = entered, Release = release };
                TimedTelemetryProvider sampler = new TimedTelemetryProvider(blocked, delegate { return 250; }, delegate { return now; }, false);
                sampler.Poll(); Check(entered.Wait(5000), "slow acquisition starts");
                now += 5000; sampler.Poll(); Check(blocked.Captures == 1, "slow hardware calls never overlap");
                Task close = Task.Run(delegate { sampler.Dispose(); });
                Check(blocked.Disposals == 0, "provider remains alive during in-flight sample");
                release.Set(); Check(close.Wait(5000) && blocked.Disposals == 1, "sampler close drains in-flight sample");
            }
            using (ManualResetEventSlim second = new ManualResetEventSlim())
            {
                SampleProbe background = new SampleProbe { Second = second };
                using (TimedTelemetryProvider sampler = new TimedTelemetryProvider(background, delegate { return 250; }))
                    Check(second.Wait(5000), "hardware timer acquires without rendering or USB");
            }
        }
        private static void TimingPersistence()
        {
            string path = Path.Combine(Path.GetTempPath(), "timing-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                RefreshOptions saved = new RefreshOptions { IntervalMilliseconds = 3000, HardwareIntervalMilliseconds = 750, NewsRefreshMinutes = 2, WeatherRefreshMinutes = 30, WebsiteRefreshMinutes = 5, NewsSlideSeconds = 7 };
                saved.Save(path); RefreshOptions loaded = RefreshOptions.Load(path);
                Check(loaded.IntervalMilliseconds == 3000 && loaded.HardwareIntervalMilliseconds == 750 && loaded.NewsRefreshMinutes == 2 && loaded.WeatherRefreshMinutes == 30 && loaded.WebsiteRefreshMinutes == 5 && loaded.NewsSlideSeconds == 7, "six independent timing values round trip");
                File.WriteAllText(path, "<RefreshOptions><IntervalMilliseconds>4000</IntervalMilliseconds></RefreshOptions>");
                loaded = RefreshOptions.Load(path);
                Check(loaded.IntervalMilliseconds == 4000 && loaded.HardwareIntervalMilliseconds == 1000 && loaded.NewsRefreshMinutes == 10 && loaded.WeatherRefreshMinutes == 10 && loaded.WebsiteRefreshMinutes == 10 && loaded.NewsSlideSeconds == 10, "legacy settings keep send interval and default new timing");
                Check(ThrowsOutOfRange(delegate { loaded.HardwareIntervalMilliseconds = 249; }) && ThrowsOutOfRange(delegate { loaded.HardwareIntervalMilliseconds = 60001; }), "hardware timing bounds");
                Check(ThrowsOutOfRange(delegate { loaded.NewsRefreshMinutes = 0; }) && ThrowsOutOfRange(delegate { loaded.WeatherRefreshMinutes = 1441; }) && ThrowsOutOfRange(delegate { loaded.WebsiteRefreshMinutes = 0; }), "content fetch timing bounds");
                Check(ThrowsOutOfRange(delegate { loaded.NewsSlideSeconds = 2; }) && ThrowsOutOfRange(delegate { loaded.NewsSlideSeconds = 301; }), "news slide timing bounds");
            }
            finally { File.Delete(path); }
        }
        private static bool SameHeadline(Bitmap left, Bitmap right)
        {
            for (int y = 48; y < 200; y++) for (int x = 16; x < 464; x++) if (left.GetPixel(x, y) != right.GetPixel(x, y)) return false;
            return true;
        }
        private static void ContentTimingOverrides()
        {
            DateTime now = new DateTime(2026, 10, 21), start = now;
            int calls = 0;
            NewsItem[] items = NewsFeed.Parse(NewsFixture);
            ContentTiming timing = new ContentTiming(1, 2, 3, 3);
            SceneElement news = LayoutCatalog.Widget("ニュース", new Rectangle(0, 0, 480, 320));
            using (NewsCards cards = new NewsCards(delegate(string url, CancellationToken token) { calls++; return items; }, delegate { return now; }))
            using (Bitmap bitmap = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                cards.Draw(graphics, news, now, true, timing); WaitNews(cards); cards.Draw(graphics, news, now, true, timing);
                using (Bitmap first = (Bitmap)bitmap.Clone())
                {
                    now = start.AddSeconds(3); cards.Draw(graphics, news, now, true, timing);
                    Check(!SameHeadline(bitmap, first) && calls == 1, "Host slide override changes card without fetching");
                    now = start.AddSeconds(6); cards.Draw(graphics, news, now, true, timing);
                    Check(SameHeadline(bitmap, first), "Host slide override wraps at six seconds");
                }
                now = start.AddSeconds(59); cards.Draw(graphics, news, now, true, timing); Check(calls == 1, "news Host fetch interval lower boundary");
                now = start.AddMinutes(1); cards.Draw(graphics, news, now, true, timing); WaitNews(cards); Check(calls == 2, "Host news fetch overrides XML interval");
                now = start.AddSeconds(90);
                cards.Draw(graphics, news, now, true, new ContentTiming(1, 2, 3, 90));
                using (Bitmap expected = new Bitmap(480, 320))
                using (Graphics expectedGraphics = Graphics.FromImage(expected))
                {
                    NewsCards.Paint(expectedGraphics, news.Bounds, items, 1, "", now, 90);
                    Check(SameHeadline(bitmap, expected), "fetching more often than slides does not restart carousel");
                }
                Check(news.RefreshMinutes == 10 && news.SlideSeconds == 10, "Host overrides never modify scene XML values");
            }
            int weatherCalls = 0;
            using (WidgetRenderer renderer = new WidgetRenderer(delegate(double lat, double lon, CancellationToken token) { weatherCalls++; return new[] { new ForecastDay { Date = "10/21", Condition = "晴れ", High = "20", Low = "10", Rain = "0" } }; }, delegate { return now; }))
            using (Bitmap bitmap = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                SceneElement weather = LayoutCatalog.Widget("天気", new Rectangle(0, 0, 480, 320));
                renderer.OnlineEnabled = true; renderer.Timing = timing;
                renderer.Draw(graphics, weather, now); AwaitIdle(renderer);
                now = now.AddMinutes(1); renderer.Draw(graphics, weather, now); Check(weatherCalls == 1, "weather interval remains independent of news");
                now = now.AddMinutes(1); renderer.Draw(graphics, weather, now); AwaitIdle(renderer); Check(weatherCalls == 2, "Host weather interval overrides layout");
            }
            FakeFrameSourceFactory sources = new FakeFrameSourceFactory();
            int websiteCalls = 0;
            using (Bitmap image = new Bitmap(480, 320))
            using (MemoryStream bytes = new MemoryStream())
            {
                image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                using (WidgetRenderer renderer = new WidgetRenderer(null, delegate { return now; }, delegate(string url, int width, int height, CancellationToken token) { websiteCalls++; return bytes.ToArray(); }))
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    SceneElement website = LayoutCatalog.Widget("サイト", new Rectangle(0, 0, 480, 320));
                    renderer.OnlineEnabled = true; renderer.Timing = timing;
                    renderer.Draw(graphics, website, now); AwaitIdle(renderer);
                    now = now.AddMinutes(2); renderer.Draw(graphics, website, now); Check(websiteCalls == 1, "website fetch remains independent of news and weather");
                    now = now.AddMinutes(1); renderer.Draw(graphics, website, now); AwaitIdle(renderer); Check(websiteCalls == 2, "Host website interval overrides layout without launching browser in tests");
                }
            }
            using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(sources, new FakeFactory(), "red-layout", null))
            using (UpdateResult result = Await(runtime.PreviewAsync("red-layout", null, 0, new[] { new DisplayTarget(Device("background"), "blue-layout", null) })))
                Check(sources.CaptureCount("red-layout") == 1 && sources.CaptureCount("blue-layout") == 1 && result.Devices.Count == 0, "content tick updates other enabled layouts without USB send");
        }
    }
}
