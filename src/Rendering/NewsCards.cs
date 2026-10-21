// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace TurzxPanelWorks
{
    public sealed class NewsItem
    {
        public string Title, Link, Published;
    }
    public static class NewsFeed
    {
        public const string YahooUrl = "https://news.yahoo.co.jp/rss/topics/top-picks.xml";
        public static NewsItem[] Parse(string xml)
        {
            XmlDocument document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 })) document.Load(reader);
            List<NewsItem> items = new List<NewsItem>(); HashSet<string> links = new HashSet<string>();
            foreach (XmlNode node in document.SelectNodes("/rss/channel/item"))
            {
                XmlNode title = node.SelectSingleNode("title"), link = node.SelectSingleNode("link"), published = node.SelectSingleNode("pubDate");
                if (title == null || link == null || String.IsNullOrWhiteSpace(title.InnerText)) continue;
                Uri uri; if (!Uri.TryCreate(link.InnerText.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length > 0 || !links.Add(uri.AbsoluteUri)) continue;
                DateTimeOffset date;
                items.Add(new NewsItem { Title = title.InnerText.Trim(), Link = uri.AbsoluteUri, Published = published != null && DateTimeOffset.TryParse(published.InnerText, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date.ToOffset(TimeSpan.FromHours(9)).ToString("M/d HH:mm") : "" });
                if (items.Count == 30) break;
            }
            if (items.Count == 0) throw new InvalidDataException("ニュースの見出しがありません。");
            return items.ToArray();
        }
        public static NewsItem[] Fetch(string url, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(WebsiteCapture.ValidateUrl(url));
            request.Timeout = request.ReadWriteTimeout = 10000; request.AllowAutoRedirect = false;
            request.UserAgent = "TURZX-PanelWorks/1.0";
            using (token.Register(delegate { request.Abort(); }))
            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (MemoryStream data = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); if (data.Length + count > 1048576) throw new IOException("RSSが大きすぎます。"); data.Write(buffer, 0, count); }
                return Parse(System.Text.Encoding.UTF8.GetString(data.ToArray()));
            }
        }
    }
    internal sealed class NewsCards : IDisposable
    {
        private sealed class Entry { public NewsItem[] Items; public bool Busy; public string Error; public DateTime Attempt, Updated, Started; }
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        private readonly object sync = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Func<string, CancellationToken, NewsItem[]> fetch;
        private readonly Func<DateTime> clock;
        private bool disposed;
        internal NewsCards() : this(NewsFeed.Fetch, delegate { return DateTime.UtcNow; }) { }
        internal NewsCards(Func<string, CancellationToken, NewsItem[]> fetch, Func<DateTime> clock) { this.fetch = fetch; this.clock = clock; }
        internal bool IsIdle { get { lock (sync) { foreach (Entry e in cache.Values) if (e.Busy) return false; return true; } } }
        internal static int Index(DateTime now, DateTime start, int seconds, int count)
        { return count <= 0 ? 0 : (int)(Math.Max(0, (now - start).TotalSeconds) / Math.Max(3, Math.Min(300, seconds)) % count); }
        internal void Draw(Graphics g, SceneElement element, DateTime now, bool online, ContentTiming timing = null)
        {
            lock (sync)
            {
                if (disposed) return;
                Entry entry; string url;
                try { url = WebsiteCapture.ValidateUrl(element.SourceUrl).AbsoluteUri; }
                catch (ArgumentException) { Paint(g, element.Bounds, null, 0, "RSS URLが不正です", now, 0); return; }
                if (!cache.TryGetValue(url, out entry)) { if (cache.Count >= 16) { Paint(g, element.Bounds, null, 0, "ニュースは最大16種類です", now, 0); return; } entry = new Entry(); cache.Add(url, entry); }
                int refreshMinutes = timing == null ? Math.Max(1, Math.Min(1440, element.RefreshMinutes)) : timing.NewsMinutes;
                int slideSeconds = timing == null ? element.SlideSeconds : timing.SlideSeconds;
                if (online && !entry.Busy && clock() - entry.Attempt >= TimeSpan.FromMinutes(entry.Error == null ? refreshMinutes : 1))
                {
                    entry.Busy = true; entry.Attempt = clock();
                    Task.Run(delegate {
                        try { NewsItem[] items = fetch(url, lifetime.Token); if (items == null || items.Length == 0) throw new InvalidDataException("ニュースがありません。"); lock (sync) { if (!disposed) { entry.Items = items; entry.Error = null; entry.Updated = clock(); } } }
                        catch (Exception error) { lock (sync) { if (!disposed) entry.Error = error.Message; } }
                        finally { lock (sync) { entry.Busy = false; } }
                    });
                }
                if (entry.Items != null && entry.Started == DateTime.MinValue) entry.Started = now;
                string status = !online ? "取得OFF" : entry.Error != null ? "更新失敗・前回の情報" : entry.Busy ? "取得中" : "更新 " + entry.Updated.ToLocalTime().ToString("HH:mm");
                int index = Index(now, entry.Started, slideSeconds, entry.Items == null ? 0 : entry.Items.Length);
                Paint(g, element.Bounds, entry.Items, index, status, now, slideSeconds);
            }
        }
        internal static void Paint(Graphics g, Rectangle r, NewsItem[] items, int index, string status, DateTime now, int seconds)
        {
            using (SolidBrush panel = new SolidBrush(Color.FromArgb(19, 29, 44))) g.FillRectangle(panel, r);
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(255, 85, 105))) g.FillRectangle(accent, r.X, r.Y, 4, r.Height);
            Text(g, "NEWS  /  " + (items == null ? "--" : (index + 1) + " / " + items.Length), new Rectangle(r.X + 16, r.Y + 12, r.Width - 32, 26), 16, Color.FromArgb(255, 115, 130));
            Text(g, items == null ? "ニュースを取得すると\n見出しが順番に切り替わります" : items[index].Title, new Rectangle(r.X + 16, r.Y + 48, r.Width - 32, Math.Max(20, r.Height - 116)), Math.Max(14, Math.Min(28, r.Width / 16f)), Color.White);
            string attribution = items == null ? "RSSニュース" : new Uri(items[index].Link).Host == "news.yahoo.co.jp" ? "Yahoo!ニュース" : new Uri(items[index].Link).Host;
            Text(g, attribution + (items == null ? "" : "  " + items[index].Published), new Rectangle(r.X + 16, r.Bottom - 62, r.Width - 32, 22), 13, Color.Silver);
            Text(g, status + (seconds > 0 ? "  /  " + Math.Max(3, Math.Min(300, seconds)) + "秒で次へ" : ""), new Rectangle(r.X + 16, r.Bottom - 32, r.Width - 32, 20), 11, Color.Silver);
        }
        private static void Text(Graphics g, string text, Rectangle r, float size, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (Font font = new Font("Yu Gothic UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center }) g.DrawString(text, font, brush, r, format);
        }
        public void Dispose() { lock (sync) { if (disposed) return; disposed = true; cache.Clear(); } lifetime.Cancel(); }
    }
}
