// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace TurzxPanelWorks
{
    public sealed class DisplayTarget
    {
        public DisplayDevice Device { get; private set; }
        public string ScenePath { get; private set; }
        public string BackgroundPath { get; private set; }
        public int RotationDegrees { get; private set; }
        public DisplayTarget(DisplayDevice device, string scenePath, string backgroundPath, int rotationDegrees = 0)
        {
            if (device == null) throw new ArgumentNullException("device");
            if (rotationDegrees != 0 && rotationDegrees != 90 && rotationDegrees != 180 && rotationDegrees != 270) throw new ArgumentOutOfRangeException("rotationDegrees");
            RotationDegrees = rotationDegrees;
            Device = device;
            ScenePath = scenePath;
            BackgroundPath = backgroundPath;
        }
    }
}
