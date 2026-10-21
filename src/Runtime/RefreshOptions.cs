// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace TurzxPanelWorks
{
    public sealed class HostParameterValue
    {
        [XmlAttribute] public string Key { get; set; }
        [XmlAttribute] public decimal Value { get; set; }
    }
    public sealed class DisplayAssignment
    {
        public string StableId { get; set; }
        public string ScenePath { get; set; }
        public string BackgroundPath { get; set; }
        private int rotationDegrees;
        public int RotationDegrees
        {
            get { return rotationDegrees; }
            set
            {
                if (value != 0 && value != 90 && value != 180 && value != 270) throw new ArgumentOutOfRangeException("value");
                rotationDegrees = value;
            }
        }
    }

    public sealed class RefreshOptions
    {
        private readonly object parameterGate = new object();
        private readonly HashSet<string> legacyValues = new HashSet<string>();
        [XmlArray("ParameterValues"), XmlArrayItem("Value")]
        public List<HostParameterValue> ParameterValues { get; set; }
        private int interval = 1000;
        private volatile int hardwareInterval = 1000, newsMinutes = 10, weatherMinutes = 10, websiteMinutes = 10, cardSeconds = 10;
        public int HardwareIntervalMilliseconds { get { return hardwareInterval; } set { Range(value, 250, 60000); hardwareInterval = value; Remember("host.hardware"); } }
        public int NewsRefreshMinutes { get { return newsMinutes; } set { Range(value, 1, 1440); newsMinutes = value; Remember("news.refresh"); } }
        public int WeatherRefreshMinutes { get { return weatherMinutes; } set { Range(value, 1, 1440); weatherMinutes = value; Remember("weather.refresh"); } }
        public int WebsiteRefreshMinutes { get { return websiteMinutes; } set { Range(value, 1, 1440); websiteMinutes = value; Remember("website.refresh"); } }
        public int NewsSlideSeconds { get { return cardSeconds; } set { Range(value, 3, 300); cardSeconds = value; Remember("news.slide"); } }
        private static void Range(int value, int minimum, int maximum) { if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException("value"); }
        public List<DisplayAssignment> Assignments { get; set; }
        public RefreshOptions() { Assignments = new List<DisplayAssignment>(); ParameterValues = new List<HostParameterValue>(); }
        private void Remember(string key) { lock (parameterGate) legacyValues.Add(key); }
        private bool WasSet(string key) { lock (parameterGate) return legacyValues.Contains(key); }
        public bool ShouldSerializeHardwareIntervalMilliseconds() { return WasSet("host.hardware"); }
        public bool ShouldSerializeNewsRefreshMinutes() { return WasSet("news.refresh"); }
        public bool ShouldSerializeWeatherRefreshMinutes() { return WasSet("weather.refresh"); }
        public bool ShouldSerializeWebsiteRefreshMinutes() { return WasSet("website.refresh"); }
        public bool ShouldSerializeNewsSlideSeconds() { return WasSet("news.slide"); }
        public bool ShouldSerializeIntervalMilliseconds() { return WasSet("host.send"); }
        public int IntervalMilliseconds
        {
            get { return interval; }
            set
            {
                if (value < 250 || value > 60000) throw new ArgumentOutOfRangeException("value", "Update interval must be between 250 and 60000 ms.");
                interval = value;
                Remember("host.send");
            }
        }
        public decimal ResolveParameter(HostParameter definition)
        {
            lock (parameterGate)
            {
                HostParameterValue saved = ParameterValues.Find(delegate(HostParameterValue value) { return value.Key == definition.Key; });
                if (saved != null) return definition.Clamp(saved.Value);
                if (legacyValues.Contains(definition.Key))
                {
                    decimal value = definition.Key == "host.send" ? interval / definition.Scale : definition.Key == "host.hardware" ? hardwareInterval / definition.Scale :
                        definition.Key == "news.refresh" ? newsMinutes : definition.Key == "weather.refresh" ? weatherMinutes :
                        definition.Key == "website.refresh" ? websiteMinutes : cardSeconds;
                    return definition.Clamp(value);
                }
                return definition.Value;
            }
        }
        public void SetParameter(HostParameter definition, decimal value)
        {
            if (value < definition.Minimum || value > definition.Maximum) throw new ArgumentOutOfRangeException("value");
            lock (parameterGate)
            {
                ParameterValues.RemoveAll(delegate(HostParameterValue saved) { return saved.Key == definition.Key; });
                ParameterValues.Add(new HostParameterValue { Key = definition.Key, Value = value });
                ApplyHostParameter(definition);
            }
        }
        public void ApplyHostParameter(HostParameter definition)
        {
            if (definition.Target != "Host") return;
            int value = Decimal.ToInt32(ResolveParameter(definition) * definition.Scale);
            if (definition.Property == "IntervalMilliseconds") IntervalMilliseconds = value;
            else if (definition.Property == "HardwareIntervalMilliseconds") HardwareIntervalMilliseconds = value;
        }

        public static RefreshOptions Load(string path)
        {
            if (!File.Exists(path)) return new RefreshOptions();
            using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                RefreshOptions result = (RefreshOptions)new XmlSerializer(typeof(RefreshOptions)).Deserialize(reader);
                if (result.ParameterValues == null) result.ParameterValues = new List<HostParameterValue>();
                HashSet<string> keys = new HashSet<string>();
                foreach (HostParameterValue value in result.ParameterValues)
                    if (value == null || String.IsNullOrWhiteSpace(value.Key) || !keys.Add(value.Key)) throw new InvalidDataException("Host設定値のキーが不正です。");
                return result;
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
                new XmlSerializer(typeof(RefreshOptions)).Serialize(writer, this);
        }
    }
}
