// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace TurzxPanelWorks
{
    public sealed class TelemetrySnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuLoadPercent { get; set; }
        public double? CpuTemperatureC { get; set; }
        public double GpuLoadPercent { get; set; }
        public double? GpuTemperatureC { get; set; }
        public double RamLoadPercent { get; set; }
        public long RamUsedMb { get; set; }
        public double VolumePercent { get; set; }
        public double UploadMbps { get; set; }
        public double DownloadMbps { get; set; }
        public double DiskUsedPercent { get; set; }
        public string SourceDescription { get; set; }
        public Dictionary<string, object> AdditionalValues { get; set; }
    }

    public interface ITelemetryProvider : IDisposable
    {
        string Name { get; }
        TelemetrySnapshot Capture();
    }
}
