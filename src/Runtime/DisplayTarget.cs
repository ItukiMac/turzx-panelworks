// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace StandaloneDisplay
{
    public sealed class DisplayTarget
    {
        public DisplayDevice Device { get; private set; }
        public string ScenePath { get; private set; }
        public string BackgroundPath { get; private set; }
        public DisplayTarget(DisplayDevice device, string scenePath, string backgroundPath)
        {
            if (device == null) throw new ArgumentNullException("device");
            Device = device;
            ScenePath = scenePath;
            BackgroundPath = backgroundPath;
        }
    }
}
