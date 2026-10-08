using System;
using System.Collections.Generic;
using System.Drawing;

namespace StandaloneDisplay
{
    public sealed class DisplayDevice
    {
        public string StableId { get; private set; }
        public string PortName { get; private set; }
        public string DisplayName { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public DisplayDevice(string stableId, string portName, string displayName, int width, int height)
        {
            if (String.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("Stable ID is required.", "stableId");
            if (String.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Port name is required.", "portName");
            StableId = stableId; PortName = portName;
            DisplayName = String.IsNullOrWhiteSpace(displayName) ? portName : displayName;
            Width = width; Height = height;
        }
        public override string ToString() { return DisplayName + " (" + PortName + ")"; }
    }
    public interface IDisplayDiscovery { IList<DisplayDevice> FindDevices(); }
    public interface IDisplaySession : IDisposable { DisplayDevice Device { get; } void SendFrame(Bitmap frame); }
    public interface IDisplaySessionFactory { IDisplaySession Connect(DisplayDevice device); }

    public sealed class DisplaySendResult
    {
        public DisplayDevice Device { get; private set; }
        public Exception Error { get; private set; }
        public bool Succeeded { get { return Error == null; } }
        public DisplaySendResult(DisplayDevice device, Exception error) { Device = device; Error = error; }
    }

    public sealed class MultiDisplaySender
    {
        private readonly IDisplaySessionFactory factory;
        public MultiDisplaySender(IDisplaySessionFactory factory) { this.factory = factory; }
        public IList<DisplaySendResult> Send(Bitmap frame, IEnumerable<DisplayDevice> targets)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            List<DisplaySendResult> results = new List<DisplaySendResult>();
            foreach (DisplayDevice target in targets)
            {
                try { using (IDisplaySession session = factory.Connect(target)) session.SendFrame(frame); results.Add(new DisplaySendResult(target, null)); }
                catch (Exception error) { results.Add(new DisplaySendResult(target, error)); }
            }
            return results;
        }
    }
}
