// SPDX-License-Identifier: GPL-3.0-or-later
// Rev A behavior adapted from turing-smart-screen-python.
// Copyright (C) 2021 Matthieu Houdebine and contributors.
// Modified for persistent C# display sessions, 2026-10-08. See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace TurzxPanelWorks
{
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
        public void SendFrame(byte[] frame)
        {
            if (port == null || !port.IsOpen) throw new InvalidOperationException("Display session is closed.");
            try { PortStream stream = new PortStream(port); RevAFrameEncoder.WriteEncodedFrame(stream, frame); stream.Flush(); }
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
}
