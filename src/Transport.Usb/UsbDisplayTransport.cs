using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace StandaloneDisplay
{
    public sealed class UsbDisplayDiscovery : IDisplayDiscovery
    {
        private const string HardwarePrefix = "USB\\VID_1A86&PID_5722\\";
        public IList<DisplayDevice> FindDevices()
        {
            Dictionary<string, DisplayDevice> found = new Dictionary<string, DisplayDevice>(StringComparer.OrdinalIgnoreCase);
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'"))
            using (ManagementObjectCollection results = searcher.Get())
            foreach (ManagementObject item in results)
            using (item)
            {
                string id = Convert.ToString(item["PNPDeviceID"]);
                string instance = id.StartsWith(HardwarePrefix, StringComparison.OrdinalIgnoreCase) ? id.Substring(HardwarePrefix.Length) : String.Empty;
                if (!instance.StartsWith("USB35INCHIPS", StringComparison.OrdinalIgnoreCase)) continue;
                Match match = Regex.Match(Convert.ToString(item["Name"]), @"\((COM[0-9]+)\)$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                found[id] = new DisplayDevice(id, match.Groups[1].Value.ToUpperInvariant(), "Rev A USB display", 480, 320);
            }
            List<DisplayDevice> devices = new List<DisplayDevice>(found.Values);
            devices.Sort(delegate(DisplayDevice a, DisplayDevice b) { return StringComparer.OrdinalIgnoreCase.Compare(a.StableId, b.StableId); });
            return devices;
        }
    }

    public sealed class UsbDisplaySessionFactory : IDisplaySessionFactory
    {
        private readonly IDisplayDiscovery discovery;
        public UsbDisplaySessionFactory(IDisplayDiscovery discovery) { this.discovery = discovery; }
        public IDisplaySession Connect(DisplayDevice requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            foreach (DisplayDevice current in discovery.FindDevices())
                if (String.Equals(current.StableId, requested.StableId, StringComparison.OrdinalIgnoreCase)) return new UsbDisplaySession(current);
            throw new IOException("The selected display is no longer available.");
        }
    }

    internal sealed class UsbDisplaySession : IDisplaySession
    {
        private SerialPort port;
        public DisplayDevice Device { get; private set; }
        public UsbDisplaySession(DisplayDevice device)
        {
            Device = device;
            port = new SerialPort(device.PortName, 115200, Parity.None, 8, StopBits.One) { DtrEnable = true, RtsEnable = true, ReadTimeout = 1500, WriteTimeout = 3000 };
            try
            {
                port.Open(); port.DiscardInBuffer();
                byte[] hello = { 69, 69, 69, 69, 69, 69 }; port.Write(hello, 0, hello.Length);
                int count = 0;
                try { for (; count < 6; count++) if (port.ReadByte() != 1) throw new IOException("Unexpected display identification response."); }
                catch (TimeoutException) { if (count != 0) throw; }
                finally { port.DiscardInBuffer(); }
            }
            catch { Dispose(); throw; }
        }
        public void SendFrame(Bitmap frame)
        {
            if (port == null || !port.IsOpen) throw new InvalidOperationException("Display session is closed.");
            try { PortStream stream = new PortStream(port); RevAFrameEncoder.WriteFrame(stream, frame); stream.Flush(); }
            catch { Dispose(); throw; }
        }
        public void Dispose() { SerialPort current = port; port = null; if (current != null) current.Dispose(); }

        private sealed class PortStream : Stream
        {
            private readonly SerialPort port; private readonly Stopwatch elapsed = Stopwatch.StartNew();
            public PortStream(SerialPort port) { this.port = port; }
            public override void Write(byte[] buffer, int offset, int count) { long left = 30000 - elapsed.ElapsedMilliseconds; if (left <= 0) throw new TimeoutException(); port.WriteTimeout = (int)Math.Min(3000, left); port.Write(buffer, offset, count); }
            public override void Flush() { while (port.BytesToWrite > 0) { if (elapsed.ElapsedMilliseconds >= 30000) throw new TimeoutException(); Thread.Sleep(10); } }
            public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] b, int o, int c) { throw new NotSupportedException(); } public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long v) { throw new NotSupportedException(); }
        }
    }

    public static class RevAFrameEncoder
    {
        public static byte[] Encode(Bitmap frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            if (frame.Width != 480 || frame.Height != 320) throw new ArgumentException("Frame must be 480 x 320.", "frame");
            byte[] pixels = new byte[307200]; int offset = 0;
            for (int y = 0; y < 320; y++) for (int x = 0; x < 480; x++) { Color c = frame.GetPixel(x, y); int value = ((c.R >> 3) << 11) | ((c.G >> 2) << 5) | (c.B >> 3); pixels[offset++] = (byte)value; pixels[offset++] = (byte)(value >> 8); }
            return pixels;
        }
        public static void WriteFrame(Stream destination, Bitmap frame)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            byte[] pixels = Encode(frame); byte[] orientation = { 0, 0, 0, 0, 0, 121, 102, 1, 224, 1, 64, 0, 0, 0, 0, 0 }; byte[] rectangle = { 0, 0, 7, 125, 63, 197 };
            destination.Write(orientation, 0, 16); destination.Write(rectangle, 0, 6);
            for (int offset = 0; offset < pixels.Length; offset += 3840) destination.Write(pixels, offset, Math.Min(3840, pixels.Length - offset));
        }
    }
}
