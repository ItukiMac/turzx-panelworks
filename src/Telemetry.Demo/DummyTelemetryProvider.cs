// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace StandaloneDisplay
{
    public sealed class DummyTelemetryProvider : ITelemetryProvider
    {
        private int tick;
        public string Name { get { return "Dummy values"; } }

        public TelemetrySnapshot Capture()
        {
            tick++;
            double wave = (Math.Sin(tick / 3.0) + 1.0) * 0.5;
            double networkWave = (Math.Sin(tick / 2.1 + 1.3) + 1.0) * 0.5;
            double uploadWave = (Math.Sin(tick / 1.7 + 3.0) + 1.0) * 0.5;
            Dictionary<string, object> additionalValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "LHM:/cpu/0/temperature/0", 63.0 },
                { "LHM:/gpu-nvidia/0/temperature/0", 57.0 },
                { "STATS.GPU.ENGINE.GPU0.3D.PERCENTAGE", 44.0 + wave * 38.0 },
                { "STATS.GPU.ENGINE.GPU0.COPY.PERCENTAGE", 8.0 + wave * 16.0 },
                { "STATS.GPU.ENGINE.GPU0.VIDEO_DECODE.PERCENTAGE", 18.0 + wave * 27.0 },
                { "STATS.GPU.ENGINE.GPU0.VIDEO_ENCODE.PERCENTAGE", 4.0 + wave * 11.0 },
                { "STATS.GPU.ENGINE.GPU0.COMPUTE_0.PERCENTAGE", 25.0 + wave * 30.0 }
            };
            int dummyCoreCount = Math.Max(4, Math.Min(8, Environment.ProcessorCount));
            for (int core = 0; core < dummyCoreCount; core++)
                additionalValues["STATS.CPU.CORE." + core + ".PERCENTAGE"] =
                    18.0 + (Math.Sin(tick / 2.4 + core * 0.8) + 1.0) * 38.0;
            return new TelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                CpuLoadPercent = 62 + wave * 23,
                CpuTemperatureC = 58 + wave * 7,
                GpuLoadPercent = 48 + wave * 32,
                GpuTemperatureC = 53 + wave * 6,
                RamLoadPercent = 51,
                RamUsedMb = 12628,
                VolumePercent = 46,
                UploadMbps = 0.15 + uploadWave * 6.5,
                DownloadMbps = 0.8 + networkWave * 84.0,
                DiskUsedPercent = 80,
                SourceDescription = "DUMMY / animated review values",
                AdditionalValues = additionalValues
            };
        }

        public void Dispose() { }
    }
}
