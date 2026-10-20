// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace TurzxPanelWorks
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
    // The RGB565 payload is read-only and is shared between targets in one update.
    public interface IDisplaySession : IDisposable { DisplayDevice Device { get; } void SendFrame(byte[] rgb565); }
    public interface IDisplaySessionFactory { IDisplaySession Connect(DisplayDevice device); }

    public sealed class DisplaySendResult
    {
        public DisplayDevice Device { get; private set; }
        public Exception Error { get; private set; }
        public bool Succeeded { get { return Error == null; } }
        public bool Skipped { get; private set; }
        public DisplaySendResult(DisplayDevice device, Exception error) : this(device, error, false) { }
        public DisplaySendResult(DisplayDevice device, Exception error, bool skipped) { Device = device; Error = error; Skipped = skipped; }
    }

}
