// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Xml;
namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private const string NewsFixture = "<rss><channel><item><title>一つ目 &amp; テスト</title><link>https://news.yahoo.co.jp/pickup/1</link><pubDate>Tue, 20 Oct 2026 16:00:00 GMT</pubDate></item><item><title>二つ目</title><link>https://news.yahoo.co.jp/pickup/2</link></item><item><title>重複</title><link>https://news.yahoo.co.jp/pickup/2</link></item><item><title>不正</title><link>file:///private</link></item></channel></rss>";
        private static void NewsCarousel()
        {
            NewsItem[] items = NewsFeed.Parse(NewsFixture);
            Check(items.Length == 2 && items[0].Title == "一つ目 & テスト" && items[0].Published == "10/21 01:00", "RSS title decoding, JST date, duplicate and URL filtering");
            foreach (string xml in new[] { "<rss/>", "<!DOCTYPE rss [<!ENTITY x 'bad'>]><rss/>", "<broken" })
            { bool rejected = false; try { NewsFeed.Parse(xml); } catch (XmlException) { rejected = true; } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid/empty/DTD RSS rejected"); }
            DateTime now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), start = now;
            Check(NewsCards.Index(now, start, 10, 2) == 0 && NewsCards.Index(now.AddSeconds(9), start, 10, 2) == 0 && NewsCards.Index(now.AddSeconds(10), start, 10, 2) == 1 && NewsCards.Index(now.AddSeconds(20), start, 10, 2) == 0, "carousel first card, interval boundary and wrap");
            int calls = 0; bool fail = false;
            using (NewsCards cards = new NewsCards(delegate(string url, CancellationToken token) { calls++; if (fail) throw new IOException("outage"); return items; }, delegate { return now; }))
            using (Bitmap bitmap = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                SceneElement element = LayoutCatalog.Widget("ニュース", new Rectangle(0, 0, 480, 320));
                cards.Draw(graphics, element, now, false); Check(calls == 0, "news offline no fetch");
                cards.Draw(graphics, element, now, true); WaitNews(cards); cards.Draw(graphics, element, now, true);
                using (Bitmap successful = (Bitmap)bitmap.Clone())
                {
                    now = now.AddMinutes(10); fail = true; cards.Draw(graphics, element, now, true); WaitNews(cards); cards.Draw(graphics, element, now, true);
                    bool same = true; for (int y = 48; y < 200; y++) for (int x = 16; x < 464; x++) if (bitmap.GetPixel(x,y) != successful.GetPixel(x,y)) same = false;
                    Check(same && calls == 2, "failed news update retains previous headline");
                }
                now = now.AddSeconds(59); cards.Draw(graphics, element, now, true); Check(calls == 2, "news retry backoff");
                now = now.AddSeconds(1); fail = false; cards.Draw(graphics, element, now, true); WaitNews(cards); Check(calls == 3, "news retry recovers");
                Check(element.Clone().SlideSeconds == 10, "news interval clone");
            }
            Check(LayoutCatalog.Template(6).Elements.Exists(delegate(SceneElement e) { return e.Kind == SceneElementKind.News && e.SourceUrl == NewsFeed.YahooUrl; }), "Yahoo sample uses news cards");
        }
        private static void WaitNews(NewsCards cards) { if (!SpinWait.SpinUntil(delegate { return cards.IsIdle; }, 5000)) throw new TimeoutException("News worker"); }
        private static int NewsPreview(string directory)
        {
            Directory.CreateDirectory(directory);
            NewsItem[] items = NewsFeed.Fetch(NewsFeed.YahooUrl, CancellationToken.None);
            using (NewsCards cards = new NewsCards(delegate(string url, CancellationToken token) { return items; }, delegate { return DateTime.UtcNow; }))
            using (Bitmap bitmap = new Bitmap(480, 320))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                SceneElement e = LayoutCatalog.Widget("ニュース", new Rectangle(12, 12, 456, 296));
                DateTime start = DateTime.Now; cards.Draw(graphics, e, start, true); WaitNews(cards);
                for (int i = 0; i < Math.Min(3, items.Length); i++) { graphics.Clear(Color.FromArgb(8, 12, 20)); cards.Draw(graphics, e, start.AddSeconds(i * 10), true); bitmap.Save(Path.Combine(directory, "news-card-" + (i + 1) + ".png"), ImageFormat.Png); }
            }
            Console.WriteLine("PASS: live RSS " + items.Length + " headlines; rendered three successive cards."); return 0;
        }
        private static void PreviewSelection()
        {
            FakeFactory sessions = new FakeFactory();
            using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(new FakeFrameSourceFactory(), sessions, "red-layout", null))
            {
                using (UpdateResult first = Await(runtime.PreviewAsync("red-layout", null, 0))) Check(first.Preview.GetPixel(100,100).ToArgb() == Color.Red.ToArgb(), "preview first selection");
                using (UpdateResult next = Await(runtime.PreviewAsync("blue-layout", null, 0))) Check(next.Preview.GetPixel(100,100).ToArgb() == Color.Blue.ToArgb() && next.Devices.Count == 0, "preview switches immediately without send targets");
                using (UpdateResult rotated = Await(runtime.PreviewAsync("blue-layout", null, 90))) Check(rotated.Preview.Width == 320 && rotated.Preview.Height == 480, "selection preview rotation");
            }
        }
    }
}
