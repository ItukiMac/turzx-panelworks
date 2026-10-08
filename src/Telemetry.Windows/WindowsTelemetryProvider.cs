using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;

namespace StandaloneDisplay
{
    public sealed class WindowsTelemetryProvider : ITelemetryProvider
    {
        private readonly PerformanceCounter cpuCounter;
        private readonly Dictionary<string, PerformanceCounter> cpuCoreCounters = new Dictionary<string, PerformanceCounter>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PerformanceCounter> gpuEngineCounters = new Dictionary<string, PerformanceCounter>(StringComparer.OrdinalIgnoreCase);
        private DateTime lastGpuCounterRefresh;
        private DateTime previousNetworkTime;
        private long previousBytesSent;
        private long previousBytesReceived;
        private bool hasNetworkBaseline;

        public string Name { get { return "Windows actual values"; } }

        public WindowsTelemetryProvider()
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            try { cpuCounter.NextValue(); } catch { }
            InitializeCpuCoreCounters();
        }

        public TelemetrySnapshot Capture()
        {
            DateTime now = DateTime.Now;
            double cpu = ReadCpu();
            long totalMb;
            long usedMb;
            ReadMemory(out totalMb, out usedMb);
            double ramPercent = totalMb > 0 ? usedMb * 100.0 / totalMb : 0;
            double upload;
            double download;
            ReadNetwork(now, out upload, out download);
            double gpuLoad;
            double? gpuTemp;
            ReadNvidia(out gpuLoad, out gpuTemp);
            Dictionary<string, object> additionalValues = ReadLibreHardwareSensors();
            ReadCpuCoreValues(additionalValues);
            double gpuEngineMaximum = ReadGpuEngineValues(additionalValues);
            if (gpuLoad <= 0 && gpuEngineMaximum > 0) gpuLoad = gpuEngineMaximum;

            return new TelemetrySnapshot
            {
                Timestamp = now,
                CpuLoadPercent = cpu,
                CpuTemperatureC = ReadCpuTemperature(),
                GpuLoadPercent = gpuLoad,
                GpuTemperatureC = gpuTemp,
                RamLoadPercent = Clamp(ramPercent),
                RamUsedMb = usedMb,
                VolumePercent = ReadVolume(),
                UploadMbps = upload,
                DownloadMbps = download,
                DiskUsedPercent = ReadDiskUsedPercent(),
                SourceDescription = "ACTUAL / Windows + optional NVIDIA/LHM sensors",
                AdditionalValues = additionalValues
            };
        }

        private void InitializeCpuCoreCounters()
        {
            try
            {
                PerformanceCounterCategory category = new PerformanceCounterCategory("Processor");
                foreach (string instance in category.GetInstanceNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    if (String.Equals(instance, "_Total", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = "STATS.CPU.CORE." + NormalizePlaceholderPart(instance) + ".PERCENTAGE";
                    PerformanceCounter counter = new PerformanceCounter("Processor", "% Processor Time", instance, true);
                    try { counter.NextValue(); } catch { counter.Dispose(); continue; }
                    cpuCoreCounters[key] = counter;
                }
            }
            catch { }
        }

        private void ReadCpuCoreValues(Dictionary<string, object> values)
        {
            foreach (KeyValuePair<string, PerformanceCounter> pair in cpuCoreCounters)
            {
                try { values[pair.Key] = Clamp(pair.Value.NextValue()); }
                catch { values[pair.Key] = 0.0; }
            }
        }

        private double ReadGpuEngineValues(Dictionary<string, object> values)
        {
            RefreshGpuEngineCounters();
            Dictionary<string, double> totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> typeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, PerformanceCounter> pair in gpuEngineCounters)
            {
                string placeholder;
                string typeAlias;
                if (!TryGetGpuEnginePlaceholder(pair.Key, out placeholder, out typeAlias)) continue;
                double reading;
                try { reading = Math.Max(0, pair.Value.NextValue()); }
                catch { reading = 0; }
                double current;
                totals.TryGetValue(placeholder, out current);
                totals[placeholder] = current + reading;
                typeAliases[placeholder] = typeAlias;
            }
            double maximum = 0;
            Dictionary<string, double> aliases = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> pair in totals)
            {
                double value = Clamp(pair.Value);
                values[pair.Key] = value;
                maximum = Math.Max(maximum, value);
                string alias = typeAliases[pair.Key];
                double aliasValue;
                aliases.TryGetValue(alias, out aliasValue);
                aliases[alias] = Math.Max(aliasValue, value);
            }
            foreach (KeyValuePair<string, double> pair in aliases) values[pair.Key] = pair.Value;
            return maximum;
        }

        private void RefreshGpuEngineCounters()
        {
            DateTime now = DateTime.UtcNow;
            if (gpuEngineCounters.Count > 0 && (now - lastGpuCounterRefresh).TotalSeconds < 5) return;
            lastGpuCounterRefresh = now;
            try
            {
                PerformanceCounterCategory category = new PerformanceCounterCategory("GPU Engine");
                HashSet<string> active = new HashSet<string>(category.GetInstanceNames(), StringComparer.OrdinalIgnoreCase);
                foreach (string instance in active)
                {
                    if (instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase) < 0 || gpuEngineCounters.ContainsKey(instance)) continue;
                    PerformanceCounter counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                    try { counter.NextValue(); }
                    catch { counter.Dispose(); continue; }
                    gpuEngineCounters[instance] = counter;
                }
                foreach (string stale in gpuEngineCounters.Keys.Where(name => !active.Contains(name)).ToArray())
                {
                    gpuEngineCounters[stale].Dispose();
                    gpuEngineCounters.Remove(stale);
                }
            }
            catch { }
        }

        private static bool TryGetGpuEnginePlaceholder(string instance, out string placeholder, out string typeAlias)
        {
            placeholder = null;
            typeAlias = null;
            int physicalMarker = instance.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
            int engineTypeMarker = instance.LastIndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (physicalMarker < 0 || engineTypeMarker < 0) return false;
            int physicalStart = physicalMarker + 6;
            int physicalEnd = instance.IndexOf('_', physicalStart);
            int engineMarker = instance.LastIndexOf("_eng_", engineTypeMarker - 1, StringComparison.OrdinalIgnoreCase);
            if (physicalEnd < 0 || physicalEnd > engineMarker || engineMarker < 0) return false;
            int engineStart = engineMarker + 5;
            int engineEnd = instance.IndexOf('_', engineStart);
            if (engineEnd < 0 || engineEnd > engineTypeMarker) return false;
            string physical = NormalizePlaceholderPart(instance.Substring(physicalStart, physicalEnd - physicalStart));
            string engineNumber = NormalizePlaceholderPart(instance.Substring(engineStart, engineEnd - engineStart));
            string engineType = NormalizePlaceholderPart(instance.Substring(engineTypeMarker + 9));
            if (physical.Length == 0 || engineNumber.Length == 0 || engineType.Length == 0) return false;
            string prefix = "STATS.GPU.ENGINE.GPU" + physical + "." + engineType;
            placeholder = prefix + ".ENGINE" + engineNumber + ".PERCENTAGE";
            typeAlias = prefix + ".PERCENTAGE";
            return true;
        }

        private static string NormalizePlaceholderPart(string value)
        {
            char[] result = value.ToUpperInvariant().ToCharArray();
            for (int index = 0; index < result.Length; index++)
                if (!Char.IsLetterOrDigit(result[index])) result[index] = '_';
            return new string(result).Trim('_');
        }

        private static Dictionary<string, object> ReadLibreHardwareSensors()
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            ReadSensorNamespace("root\\LibreHardwareMonitor", "LHM:", values);
            ReadSensorNamespace("root\\OpenHardwareMonitor", "OHM:", values);
            return values;
        }

        private static void ReadSensorNamespace(string namespacePath, string prefix, Dictionary<string, object> values)
        {
            try
            {
                ManagementScope scope = new ManagementScope(namespacePath);
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT Identifier,Name,Value FROM Sensor")))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string identifier = Convert.ToString(item["Identifier"], CultureInfo.InvariantCulture);
                        string name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture);
                        object rawValue = item["Value"];
                        if (!String.IsNullOrWhiteSpace(identifier)) values[prefix + identifier] = rawValue;
                        if (!String.IsNullOrWhiteSpace(name)) values[prefix + "NAME:" + name] = rawValue;
                    }
                }
            }
            catch { }
        }

        private double ReadCpu()
        {
            try { return Clamp(cpuCounter.NextValue()); }
            catch { return 0; }
        }

        private static void ReadMemory(out long totalMb, out long usedMb)
        {
            totalMb = 0;
            usedMb = 0;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        long totalKb = Convert.ToInt64(item["TotalVisibleMemorySize"], CultureInfo.InvariantCulture);
                        long freeKb = Convert.ToInt64(item["FreePhysicalMemory"], CultureInfo.InvariantCulture);
                        totalMb = totalKb / 1024;
                        usedMb = Math.Max(0, (totalKb - freeKb) / 1024);
                        return;
                    }
                }
            }
            catch { }
        }

        private void ReadNetwork(DateTime now, out double uploadMbps, out double downloadMbps)
        {
            long sent = 0;
            long received = 0;
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPv4InterfaceStatistics stats = adapter.GetIPv4Statistics();
                    sent += stats.BytesSent;
                    received += stats.BytesReceived;
                }
            }
            catch { }

            uploadMbps = 0;
            downloadMbps = 0;
            if (hasNetworkBaseline)
            {
                double seconds = Math.Max(0.001, (now - previousNetworkTime).TotalSeconds);
                uploadMbps = Math.Max(0, sent - previousBytesSent) * 8.0 / seconds / 1000000.0;
                downloadMbps = Math.Max(0, received - previousBytesReceived) * 8.0 / seconds / 1000000.0;
            }
            previousNetworkTime = now;
            previousBytesSent = sent;
            previousBytesReceived = received;
            hasNetworkBaseline = true;
        }

        private static void ReadNvidia(out double load, out double? temperature)
        {
            load = 0;
            temperature = null;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(start))
                {
                    string line = process.StandardOutput.ReadLine();
                    process.WaitForExit(1500);
                    if (!String.IsNullOrWhiteSpace(line))
                    {
                        string[] parts = line.Split(',');
                        double parsed;
                        if (parts.Length > 0 && Double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) load = Clamp(parsed);
                        if (parts.Length > 1 && Double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) temperature = parsed;
                    }
                }
            }
            catch { }
        }

        private static double? ReadCpuTemperature()
        {
            double? value = ReadTemperatureNamespace("root\\LibreHardwareMonitor", "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name LIKE '%CPU%'");
            if (value.HasValue) return value;
            value = ReadTemperatureNamespace("root\\OpenHardwareMonitor", "SELECT Value FROM Sensor WHERE SensorType='Temperature' AND Name LIKE '%CPU%'");
            if (value.HasValue) return value;
            try
            {
                ManagementScope scope = new ManagementScope("root\\WMI");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        double kelvinTenths = Convert.ToDouble(item["CurrentTemperature"], CultureInfo.InvariantCulture);
                        double celsius = kelvinTenths / 10.0 - 273.15;
                        if (celsius > 0 && celsius < 120) return celsius;
                    }
                }
            }
            catch { }
            return null;
        }

        private static double? ReadTemperatureNamespace(string scopePath, string query)
        {
            try
            {
                ManagementScope scope = new ManagementScope(scopePath);
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
                {
                    foreach (ManagementObject item in searcher.Get()) return Convert.ToDouble(item["Value"], CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return null;
        }

        private static double ReadVolume()
        {
            // Audio telemetry is deliberately optional. The core build has no bundled
            // third-party audio binary; hosts may add an adapter if they need it.
            return 0;
        }

        private static double ReadDiskUsedPercent()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady || drive.TotalSize <= 0) return 0;
                return Clamp((1.0 - drive.TotalFreeSpace / (double)drive.TotalSize) * 100.0);
            }
            catch { return 0; }
        }

        private static double Clamp(double value) { return Math.Max(0, Math.Min(100, value)); }

        public void Dispose()
        {
            cpuCounter.Dispose();
            foreach (PerformanceCounter counter in cpuCoreCounters.Values) counter.Dispose();
            foreach (PerformanceCounter counter in gpuEngineCounters.Values) counter.Dispose();
        }
    }
}
