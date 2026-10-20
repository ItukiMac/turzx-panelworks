// SPDX-License-Identifier: GPL-3.0-or-later
// Rev A behavior adapted from turing-smart-screen-python.
// Copyright (C) 2021 Matthieu Houdebine and contributors.
// Modified for the C# frame pipeline, 2026-10-08. See THIRD_PARTY_NOTICES.md.
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
    public static class RevAFrameEncoder
    {
        public static byte[] Encode(Bitmap frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            if (frame.Width != 480 || frame.Height != 320) throw new ArgumentException("Frame must be 480 x 320.", "frame");
            return Rgb565Encoder.Encode(frame);
        }
        public static void WriteFrame(Stream destination, Bitmap frame)
        {
            WriteEncodedFrame(destination, Encode(frame));
        }
        public static void WriteEncodedFrame(Stream destination, byte[] pixels)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            if (pixels == null || pixels.Length != 307200) throw new ArgumentException("Expected a 480 x 320 RGB565 frame.", "pixels");
            byte[] orientation = { 0, 0, 0, 0, 0, 121, 102, 1, 224, 1, 64, 0, 0, 0, 0, 0 }; byte[] rectangle = { 0, 0, 7, 125, 63, 197 };
            destination.Write(orientation, 0, 16); destination.Write(rectangle, 0, 6);
            for (int offset = 0; offset < pixels.Length; offset += 3840) destination.Write(pixels, offset, Math.Min(3840, pixels.Length - offset));
        }
    }
}
