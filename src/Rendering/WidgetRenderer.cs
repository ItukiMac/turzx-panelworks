// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TurzxPanelWorks.OfflineTests")]
namespace TurzxPanelWorks
{
    public sealed class ForecastDay
    {
        public string Date, Condition, High, Low, Rain;
    }
    public static class WeatherForecast
    {
        public static string Url(double latitude, double longitude)
        {
            if (Double.IsNaN(latitude) || Double.IsInfinity(latitude) || latitude < -90 || latitude > 90 || Double.IsNaN(longitude) || Double.IsInfinity(longitude) || longitude < -180 || longitude > 180)
                throw new ArgumentException("緯度・経度の範囲が不正です。");
            return "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Asia%2FTokyo&forecast_days=3";
        }
        public static ForecastDay[] Fetch(double latitude, double longitude, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url(latitude, longitude));
            request.Timeout = 10000; request.ReadWriteTimeout = 10000; request.AllowAutoRedirect = false;
            request.UserAgent = "TURZX-PanelWorks/1.0";
            using (token.Register(delegate { request.Abort(); }))
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[4096]; int count;
                while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (buffer.Length + count > 1024 * 1024) throw new IOException("天気データが大きすぎます。");
                    buffer.Write(chunk, 0, count);
                }
                return Parse(Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }
        public static ForecastDay[] Parse(string json)
        {
            Dictionary<string, object> root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("daily")) throw new InvalidDataException("天気予報データがありません。");
            Dictionary<string, object> daily = root["daily"] as Dictionary<string, object>;
            if (daily == null) throw new InvalidDataException("天気予報の形式が不正です。");
            object[] dates = Values(daily, "time"), codes = Values(daily, "weather_code"), high = Values(daily, "temperature_2m_max"), low = Values(daily, "temperature_2m_min"), rain = Values(daily, "precipitation_probability_max");
            int count = Math.Min(3, dates.Length);
            if (count == 0 || codes.Length < count || high.Length < count || low.Length < count || rain.Length < count) throw new InvalidDataException("天気予報の項目が不足しています。");
            ForecastDay[] result = new ForecastDay[count];
            for (int i = 0; i < count; i++)
            {
                DateTime date;
                if (!DateTime.TryParseExact(Convert.ToString(dates[i], CultureInfo.InvariantCulture), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new InvalidDataException("予報の日付が不正です。");
                result[i] = new ForecastDay { Date = date.ToString("M/d"), Condition = codes[i] == null ? "未取得" : Condition(Convert.ToInt32(codes[i], CultureInfo.InvariantCulture)), High = Number(high[i]), Low = Number(low[i]), Rain = Number(rain[i]) };
            }
            return result;
        }
        private static object[] Values(Dictionary<string, object> data, string key)
        {
            object value; if (!data.TryGetValue(key, out value) || !(value is object[])) throw new InvalidDataException("天気予報の項目がありません: " + key);
            return (object[])value;
        }
        private static string Number(object value) { return value == null ? "--" : Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture); }
        public static string Condition(int code)
        {
            if (code == 0) return "快晴"; if (code >= 1 && code <= 2) return "晴れ"; if (code == 3) return "曇り";
            if (code == 45 || code == 48) return "霧"; if (code >= 51 && code <= 57) return "霧雨";
            if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return "雨";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "雪";
            if (code >= 95 && code <= 99) return "雷雨"; return "不明";
        }
    }

    internal sealed class WidgetRenderer : IDisposable
    {
        private sealed class Entry
        {
            public bool Busy; public DateTime Attempt, Updated; public string Error; public ForecastDay[] Forecast; public byte[] Image;
        }
        private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        private readonly object sync = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private static readonly SemaphoreSlim requests = new SemaphoreSlim(2);
        private static readonly SemaphoreSlim browsers = new SemaphoreSlim(1);
        private bool disposed;
        private readonly NewsCards news = new NewsCards();

        private readonly Func<double, double, CancellationToken, ForecastDay[]> fetchWeather;
        private readonly Func<string, int, int, CancellationToken, byte[]> captureSite;
        private readonly Func<DateTime> utcNow;
        internal WidgetRenderer() : this(WeatherForecast.Fetch, delegate { return DateTime.UtcNow; }) { }
        internal WidgetRenderer(Func<double, double, CancellationToken, ForecastDay[]> fetchWeather, Func<DateTime> utcNow, Func<string, int, int, CancellationToken, byte[]> captureSite = null)
        { this.fetchWeather = fetchWeather; this.utcNow = utcNow; this.captureSite = captureSite ?? WebsiteCapture.Capture; }
        internal bool IsIdle { get { lock (sync) { foreach (Entry entry in cache.Values) if (entry.Busy) return false; return true; } } }
        public bool OnlineEnabled { get; set; }
        public ContentTiming Timing { get; set; }
        public bool Draw(Graphics graphics, SceneElement element, DateTime now)
        {
            if (element.Kind != SceneElementKind.Calendar && element.Kind != SceneElementKind.Weather && element.Kind != SceneElementKind.Website && element.Kind != SceneElementKind.News && element.Kind != SceneElementKind.Market) return false;
            if (element.Width <= 0 || element.Height <= 0) return true;
            System.Drawing.Drawing2D.GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(element.Bounds, System.Drawing.Drawing2D.CombineMode.Intersect);
                return DrawCore(graphics, element, now);
            }
            finally { graphics.Restore(state); }
        }
        private bool DrawCore(Graphics graphics, SceneElement element, DateTime now)
        {
            if (element.Kind == SceneElementKind.Market)
            {
                try
                {
                    SceneElement page = element.Copy(); page.Kind = SceneElementKind.Website;
                    page.SourceUrl = MarketData.PageUrl(element.MarketSymbol);
                    Panel(graphics, element.Bounds);
                    page.Y += 24; page.Height = Math.Max(1, page.Height - 24);
                    DrawCore(graphics, page, now);
                    Line(graphics, element.MarketSymbol + "  /  Yahoo! Finance", new Rectangle(element.X + 6, element.Y + 2, Math.Max(1, element.Width - 12), 20), 11, Color.White);
                    return true;
                }
                catch (ArgumentException) { Panel(graphics, element.Bounds); Line(graphics, "銘柄を選択してください", element.Bounds, 12, Color.Salmon); return true; }
            }
            if (element.Kind == SceneElementKind.News) { news.Draw(graphics, element, now, OnlineEnabled, Timing); return true; }
            if (element.Kind == SceneElementKind.Calendar) { DrawCalendar(graphics, element.Bounds, now); return true; }
            if (element.Kind != SceneElementKind.Weather && element.Kind != SceneElementKind.Website && element.Kind != SceneElementKind.News && element.Kind != SceneElementKind.Market) return false;
            bool weather = element.Kind == SceneElementKind.Weather;
            string url;
            try { url = weather ? WeatherForecast.Url(element.Latitude, element.Longitude) : WebsiteCapture.ValidateUrl(element.SourceUrl).AbsoluteUri; }
            catch (Exception error) { Panel(graphics, element.Bounds); Line(graphics, error.Message, element.Bounds, 12, Color.Salmon); return true; }
            int width = Math.Max(320, Math.Min(1920, element.ViewportWidth));
            int height = Math.Max(200, Math.Min(1600, (int)((long)width * Math.Max(1, element.Height - 20) / Math.Max(1, element.Width))));
            string key = (weather ? "weather:" : "site:") + url + ":" + width + ":" + height;
            lock (sync)
            {
                if (disposed) return true;
                Entry entry;
                if (!cache.TryGetValue(key, out entry))
                {
                    if (cache.Count >= 16) { Panel(graphics, element.Bounds); Line(graphics, "外部表示は最大16種類です", element.Bounds, 12, Color.Salmon); return true; }
                    entry = new Entry(); cache.Add(key, entry);
                }
                TimeSpan interval = TimeSpan.FromMinutes(Timing == null ? Math.Max(1, Math.Min(1440, element.RefreshMinutes)) : weather ? Timing.WeatherMinutes : Timing.WebsiteMinutes);
                if (OnlineEnabled && !entry.Busy && utcNow() - entry.Attempt >= (entry.Error == null ? interval : TimeSpan.FromMinutes(1)))
                {
                    entry.Busy = true; entry.Attempt = utcNow();
                    double latitude = element.Latitude, longitude = element.Longitude;
                    Task.Run(delegate { Update(entry, weather, url, latitude, longitude, width, height); });
                }
                if (weather) DrawWeather(graphics, element, entry);
                else DrawWebsite(graphics, element.Bounds, entry);
            }
            return true;
        }
        private void Update(Entry entry, bool weather, string url, double latitude, double longitude, int width, int height)
        {
            bool requestHeld = false, browserHeld = false;
            try
            {
                requests.Wait(lifetime.Token); requestHeld = true;
                if (!OnlineEnabled) return;
                ForecastDay[] forecast = null; byte[] image = null;
                if (weather) forecast = fetchWeather(latitude, longitude, lifetime.Token);
                else { browsers.Wait(lifetime.Token); browserHeld = true; image = captureSite(url, width, height, lifetime.Token); }
                lock (sync) { if (!disposed) { entry.Forecast = forecast; entry.Image = image; entry.Updated = DateTime.Now; entry.Error = null; } }
            }
            catch (Exception error) { lock (sync) { if (!disposed) entry.Error = error.Message; } }
            finally
            {
                if (browserHeld) browsers.Release(); if (requestHeld) requests.Release();
                lock (sync) { entry.Busy = false; }
            }
        }
        private string Status(Entry entry)
        {
            string time = entry.Updated == DateTime.MinValue ? "未取得" : entry.Updated.ToString("M/d HH:mm");
            if (!OnlineEnabled) return "オンライン取得OFF / " + time;
            if (entry.Error != null) return "更新失敗 / " + time;
            return (entry.Busy ? "取得中 / " : "更新 ") + time;
        }
        private void DrawWeather(Graphics g, SceneElement e, Entry entry)
        {
            Rectangle r = e.Bounds; Panel(g, r);
            Line(g, (e.LocationName ?? "") + " の天気", new Rectangle(r.X + 8, r.Y + 5, r.Width - 16, 24), 15, Color.FromArgb(89, 203, 245));
            if (entry.Forecast == null)
                Line(g, entry.Error == null ? "オンライン取得をONにすると\n3日間の予報を表示します" : entry.Error, new Rectangle(r.X + 8, r.Y + 35, r.Width - 16, Math.Max(18, r.Height - 80)), 12, Color.LightGray);
            else
            {
                int rows = r.Height < 120 ? 1 : entry.Forecast.Length;
                int rowHeight = Math.Max(20, (r.Height - 72) / rows);
                for (int i = 0; i < rows; i++)
                {
                    ForecastDay day = entry.Forecast[i]; int y = r.Y + 30 + i * rowHeight;
                    string text = day.Date + " " + day.Condition + "  " + day.High + "/" + day.Low + "°C";
                    if (r.Width >= 320) text += "  降水 " + day.Rain + "%";
                    Line(g, text, new Rectangle(r.X + 8, y, r.Width - 16, Math.Min(24, rowHeight)), r.Width >= 220 ? 14 : 10, Color.White);
                    if (r.Width < 320 && rowHeight >= 34) Line(g, "降水確率 " + day.Rain + "%", new Rectangle(r.X + 8, y + 22, r.Width - 16, 16), 11, Color.LightGray);
                }
            }
            Line(g, Status(entry), new Rectangle(r.X + 8, r.Bottom - 38, r.Width - 16, 16), 9, Color.Silver);
            Line(g, "Open-Meteo · CC BY 4.0", new Rectangle(r.X + 8, r.Bottom - 20, r.Width - 16, 16), 9, Color.Silver);
        }
        private void DrawWebsite(Graphics g, Rectangle r, Entry entry)
        {
            Panel(g, r);
            Rectangle content = new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height - 20));
            if (entry.Image != null)
            {
                using (MemoryStream stream = new MemoryStream(entry.Image))
                using (Image image = Image.FromStream(stream))
                {
                    float scale = Math.Min(content.Width / (float)image.Width, content.Height / (float)image.Height);
                    g.DrawImage(image, new RectangleF(content.X, content.Y, image.Width * scale, image.Height * scale));
                }
            }
            else Line(g, entry.Error ?? "サイト画像は未取得です。\nオンライン取得をONにしてください。", content, 13, Color.LightGray);
            Line(g, Status(entry), new Rectangle(r.X + 6, r.Bottom - 19, r.Width - 12, 18), 10, Color.Silver);
        }
        internal static void DrawCalendar(Graphics g, Rectangle r, DateTime now)
        {
            Panel(g, r);
            float font = Math.Max(7, Math.Min(15, r.Width / 18f));
            Line(g, now.ToString("yyyy年 M月"), new Rectangle(r.X + 8, r.Y + 5, r.Width - 16, 24), font + 1, Color.FromArgb(89, 203, 245));
            string[] weekdays = { "日", "月", "火", "水", "木", "金", "土" };
            int cellW = Math.Max(1, (r.Width - 12) / 7), cellH = Math.Max(1, (r.Height - 34) / 7);
            for (int i = 0; i < 7; i++) Line(g, weekdays[i], new Rectangle(r.X + 6 + i * cellW, r.Y + 30, cellW, cellH), font, i == 0 ? Color.Salmon : i == 6 ? Color.SkyBlue : Color.Silver);
            int start = (int)new DateTime(now.Year, now.Month, 1).DayOfWeek;
            for (int day = 1; day <= DateTime.DaysInMonth(now.Year, now.Month); day++)
            {
                int position = start + day - 1, col = position % 7, row = position / 7;
                Rectangle cell = new Rectangle(r.X + 6 + col * cellW, r.Y + 30 + (row + 1) * cellH, cellW, cellH);
                if (day == now.Day) using (SolidBrush brush = new SolidBrush(Color.FromArgb(42, 91, 116))) g.FillRectangle(brush, cell);
                Line(g, day.ToString(), cell, font, col == 0 ? Color.Salmon : col == 6 ? Color.SkyBlue : Color.White);
            }
        }
        private static void Panel(Graphics g, Rectangle r) { using (SolidBrush b = new SolidBrush(Color.FromArgb(19, 29, 44))) g.FillRectangle(b, r); }
        private static void Line(Graphics g, string text, Rectangle r, float size, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (Font font = new Font("Yu Gothic UI", size, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, r, format);
        }
        public void Dispose()
        {
            lock (sync) { if (disposed) return; disposed = true; cache.Clear(); }
            lifetime.Cancel(); news.Dispose();
            // Workers may still observe the token during cancellation; let the source be collected with them.
        }
    }
}
