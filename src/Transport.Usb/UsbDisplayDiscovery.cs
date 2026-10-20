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
}
