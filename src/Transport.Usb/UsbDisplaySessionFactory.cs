// SPDX-License-Identifier: GPL-3.0-or-later
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
    public sealed class UsbDisplaySessionFactory : IDisplaySessionFactory
    {
        private readonly IDisplayDiscovery discovery;
        public UsbDisplaySessionFactory(IDisplayDiscovery discovery) { this.discovery = discovery; }
        public IDisplaySession Connect(DisplayDevice requested)
        {
            if (requested == null) throw new ArgumentNullException("requested");
            foreach (DisplayDevice current in discovery.FindDevices())
                if (String.Equals(current.StableId, requested.StableId, StringComparison.OrdinalIgnoreCase) && String.Equals(current.PortName, requested.PortName, StringComparison.OrdinalIgnoreCase)) return new UsbDisplaySession(current);
            throw new IOException("The selected display is no longer available.");
        }
    }
}
