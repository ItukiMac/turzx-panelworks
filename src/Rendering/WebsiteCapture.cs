// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace TurzxPanelWorks
{
    // A separate browser profile and a kill-on-close job keep capture processes owned by this app.
    public static class WebsiteCapture
    {
        public static Uri ValidateUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("http/httpsのURLを指定してください（認証情報は含めません）。");
            return uri;
        }
        public static string FindBrowser()
        {
            string[] paths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (string path in paths) if (File.Exists(path)) return path;
            throw new InvalidOperationException("サイト表示にはMicrosoft EdgeまたはGoogle Chromeが必要です。");
        }
        private static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        public static byte[] Capture(string url, int width, int height, CancellationToken cancellation)
        {
            string normalized = ValidateUrl(url).AbsoluteUri;
            if (width < 320 || width > 1920 || height < 200 || height > 1600) throw new ArgumentOutOfRangeException("width");
            cancellation.ThrowIfCancellationRequested();
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "web");
            string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (BrowserJob job = new BrowserJob())
                using (Process process = new Process())
                {
                    string profile = Path.Combine(directory, "profile");
                    process.StartInfo = new ProcessStartInfo(FindBrowser(),
                        "--headless=new --disable-gpu --no-first-run --no-default-browser-check --hide-scrollbars --disable-background-networking " +
                        "--disable-extensions --disable-sync --disable-breakpad --disable-crash-reporter --remote-debugging-address=127.0.0.1 --remote-debugging-port=0 " +
                        "--user-data-dir=" + Quote(profile) + " --window-size=" + width + "," + height + " about:blank")
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = directory };
                    process.StartInfo.EnvironmentVariables["TEMP"] = directory;
                    process.StartInfo.EnvironmentVariables["TMP"] = directory;
                    if (!process.Start()) throw new IOException("ブラウザーを起動できませんでした。");
                    try { job.Assign(process); }
                    catch { if (!process.HasExited) process.Kill(); throw; }
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        timeout.CancelAfter(25000);
                        CancellationToken token = timeout.Token;
                        string portFile = Path.Combine(profile, "DevToolsActivePort");
                        int port = 0;
                        while (port == 0)
                        {
                            token.ThrowIfCancellationRequested();
                            if (process.HasExited) throw new IOException("ブラウザーが終了しました。");
                            if (File.Exists(portFile))
                            {
                                string[] lines = File.ReadAllLines(portFile);
                                if (lines.Length > 0) Int32.TryParse(lines[0], out port);
                            }
                            Thread.Sleep(50);
                        }
                        string debugger = null;
                        while (debugger == null)
                        {
                            token.ThrowIfCancellationRequested();
                            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json/list");
                            request.Proxy = null; request.Timeout = 2000;
                            using (WebResponse response = request.GetResponse())
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                object[] targets = (object[])new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
                                foreach (object target in targets)
                                {
                                    Dictionary<string, object> item = (Dictionary<string, object>)target;
                                    if (Convert.ToString(item["type"]) == "page")
                                    {
                                        debugger = Convert.ToString(item["webSocketDebuggerUrl"]); break;
                                    }
                                }
                            }
                            if (debugger == null) Thread.Sleep(100);
                        }
                        using (DevTools client = new DevTools(debugger, token))
                        {
                            client.Call("Page.enable", new { });
                            client.Call("Emulation.setDeviceMetricsOverride", new { width = width, height = height, deviceScaleFactor = 1, mobile = false });
                            Dictionary<string, object> navigation = client.Call("Page.navigate", new { url = normalized });
                            if (navigation.ContainsKey("errorText")) throw new IOException(Convert.ToString(navigation["errorText"]));
                            Stopwatch wait = Stopwatch.StartNew();
                            while (wait.ElapsedMilliseconds < 15000)
                            {
                                Dictionary<string, object> result = client.Call("Runtime.evaluate", new { expression = "document.readyState", returnByValue = true });
                                Dictionary<string, object> value = (Dictionary<string, object>)result["result"];
                                if (value.ContainsKey("value") && Convert.ToString(value["value"]) == "complete") break;
                                if (token.WaitHandle.WaitOne(200)) token.ThrowIfCancellationRequested();
                            }
                            if (token.WaitHandle.WaitOne(2000)) token.ThrowIfCancellationRequested();
                            string scroll = "window.scrollTo(0, 0)";
                            Uri pageUri = new Uri(normalized);
                            if (pageUri.Host == "finance.yahoo.co.jp" && pageUri.AbsolutePath.StartsWith("/quote/", StringComparison.Ordinal))
                                scroll = "(() => { const h = Array.from(document.querySelectorAll('h2')).find(e => e.textContent.trim() && !e.textContent.includes('ポートフォリオ')); if(h) window.scrollTo(0, h.getBoundingClientRect().top + window.scrollY - 8); })()";
                            client.Call("Runtime.evaluate", new { expression = scroll, returnByValue = true });
                            if (token.WaitHandle.WaitOne(300)) token.ThrowIfCancellationRequested();
                            if (pageUri.Host == "finance.yahoo.co.jp" && pageUri.Fragment == "#panelworks-chart")
                            {
                                Dictionary<string, object> rect = null;
                                Stopwatch chartWait = Stopwatch.StartNew();
                                while (chartWait.ElapsedMilliseconds < 8000)
                                {
                                    var found = client.Call("Runtime.evaluate", new { expression = "(() => { const e=document.querySelector('[class*=InteractiveChart__chart]'); if(!e) return null; e.scrollIntoView(); const r=e.getBoundingClientRect(); if(!e.querySelector('canvas,svg') || r.width<100 || r.height<100) return null; return {x:r.left+window.scrollX,y:r.top+window.scrollY,width:r.width,height:r.height,scale:1}; })()", returnByValue = true });
                                    var result = (Dictionary<string, object>)found["result"];
                                    object value;
                                    if (result.TryGetValue("value", out value)) rect = value as Dictionary<string, object>;
                                    if (rect != null) break;
                                    if (token.WaitHandle.WaitOne(300)) token.ThrowIfCancellationRequested();
                                }
                                if (rect == null) throw new IOException("チャートが読み込めませんでした。次の更新で再試行します。");
                                if (token.WaitHandle.WaitOne(1500)) token.ThrowIfCancellationRequested();
                                client.Call("Runtime.evaluate", new { expression = "window.scrollTo(0,0)", returnByValue = true });
                                if (token.WaitHandle.WaitOne(300)) token.ThrowIfCancellationRequested();
                                var chart = client.Call("Page.captureScreenshot", new { format = "png", captureBeyondViewport = true, fromSurface = true, clip = rect });
                                byte[] raw = Convert.FromBase64String(Convert.ToString(chart["data"]));
                                using (var sourceStream = new MemoryStream(raw))
                                using (var source = Image.FromStream(sourceStream))
                                using (var output = new Bitmap(width, height))
                                using (var graphics = Graphics.FromImage(output))
                                using (var buffer = new MemoryStream())
                                {
                                    graphics.Clear(Color.FromArgb(8, 12, 20));
                                    double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
                                    int w = (int)(source.Width * scale), h = (int)(source.Height * scale);
                                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    graphics.DrawImage(source, new Rectangle((width-w)/2, (height-h)/2, w, h));
                                    DarkenChart(output, new Rectangle((width-w)/2, (height-h)/2, w, h));
                                    output.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
                                    return buffer.ToArray();
                                }
                            }
                            Dictionary<string, object> screenshot = client.Call("Page.captureScreenshot", new { format = "png", captureBeyondViewport = false, fromSurface = true });
                            byte[] bytes = Convert.FromBase64String(Convert.ToString(screenshot["data"]));
                            if (!HasVisibleContent(bytes, width, height)) throw new IOException("ページが白紙のため更新できません。URLや表示幅を確認してください。");
                            return bytes;
                        }
                    }
                }
            }
            finally
            {
                // Only this capture's freshly allocated directory is removed; locked files are left alone.
                string resolved = Path.GetFullPath(directory);
                if (resolved.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try { Directory.Delete(resolved, true); break; }
                        catch (IOException) { if (attempt < 9) Thread.Sleep(100); }
                        catch (UnauthorizedAccessException) { break; }
                    }
            }
        }
        internal static void DarkenChart(Bitmap image, Rectangle area)
        {
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    Color color = image.GetPixel(x, y);
                    int max = Math.Max(color.R, Math.Max(color.G, color.B));
                    int min = Math.Min(color.R, Math.Min(color.G, color.B));
                    if (max - min < 40)
                    {
                        int light = (color.R + color.G + color.B) / 3;
                        int value = (255 - light) * 220 / 255;
                        image.SetPixel(x, y, Color.FromArgb(8 + value, 12 + value, 20 + value));
                    }
                    else
                    {
                        // Preserve rising/falling and indicator hues; lift dark colors for contrast.
                        double gain = max < 180 ? 180.0 / Math.Max(1, max) : 1;
                        image.SetPixel(x, y, Color.FromArgb((int)(color.R * gain), (int)(color.G * gain), (int)(color.B * gain)));
                    }
                }
        }
        public static bool HasVisibleContent(byte[] bytes, int width, int height)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Bitmap image = new Bitmap(stream))
            {
                if (image.Width != width || image.Height != height) return false;
                Color origin = image.GetPixel(0, 0); int varied = 0;
                for (int y = 0; y < height; y += 4)
                    for (int x = 0; x < width; x += 4)
                    {
                        Color pixel = image.GetPixel(x, y);
                        if (Math.Abs(pixel.R - origin.R) + Math.Abs(pixel.G - origin.G) + Math.Abs(pixel.B - origin.B) > 30) varied++;
                    }
                return varied >= 16;
            }
        }
        private sealed class DevTools : IDisposable
        {
            private readonly ClientWebSocket socket = new ClientWebSocket();
            private readonly CancellationToken token;
            private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            private int id;
            public DevTools(string url, CancellationToken token)
            {
                this.token = token; socket.Options.Proxy = null;
                socket.ConnectAsync(new Uri(url), token).GetAwaiter().GetResult();
            }
            public Dictionary<string, object> Call(string method, object parameters)
            {
                int requestId = ++id;
                byte[] command = Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> { { "id", requestId }, { "method", method }, { "params", parameters } }));
                socket.SendAsync(new ArraySegment<byte>(command), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
                while (true)
                {
                    using (MemoryStream message = new MemoryStream())
                    {
                        byte[] buffer = new byte[16384]; WebSocketReceiveResult received;
                        do
                        {
                            received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                            if (received.MessageType == WebSocketMessageType.Close) throw new IOException("ブラウザー接続が終了しました。");
                            if (message.Length + received.Count > 16 * 1024 * 1024) throw new IOException("ブラウザー応答が大きすぎます。");
                            message.Write(buffer, 0, received.Count);
                        } while (!received.EndOfMessage);
                        Dictionary<string, object> response = json.DeserializeObject(Encoding.UTF8.GetString(message.ToArray())) as Dictionary<string, object>;
                        if (response == null || !response.ContainsKey("id") || Convert.ToInt32(response["id"]) != requestId) continue;
                        if (response.ContainsKey("error")) throw new IOException("ブラウザー描画コマンドに失敗しました。");
                        return (Dictionary<string, object>)response["result"];
                    }
                }
            }
            public void Dispose() { socket.Abort(); socket.Dispose(); }
        }
        private sealed class BrowserJob : IDisposable
        {
            private IntPtr handle;
            public BrowserJob()
            {
                handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                ExtendedLimits limits = new ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
                int size = Marshal.SizeOf(typeof(ExtendedLimits)); IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(limits, buffer, false);
                    if (!SetInformationJobObject(handle, 9, buffer, (uint)size)) { Dispose(); throw new System.ComponentModel.Win32Exception(); }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            public void Assign(Process process)
            {
                if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception();
            }
            public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
            [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
            [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
            [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr data, uint length);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
