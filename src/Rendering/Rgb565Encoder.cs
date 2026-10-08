// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StandaloneDisplay
{
    public static class Rgb565Encoder
    {
        public static byte[] Encode(Bitmap frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            Rectangle area = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData data = frame.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[frame.Width * 4];
                byte[] output = new byte[checked(frame.Width * frame.Height * 2)];
                int offset = 0;
                for (int y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        int value = ((row[x + 2] >> 3) << 11) | ((row[x + 1] >> 2) << 5) | (row[x] >> 3);
                        output[offset++] = (byte)value;
                        output[offset++] = (byte)(value >> 8);
                    }
                }
                return output;
            }
            finally { frame.UnlockBits(data); }
        }
    }
}
