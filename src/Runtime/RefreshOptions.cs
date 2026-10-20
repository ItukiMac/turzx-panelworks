// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace TurzxPanelWorks
{
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
        private int interval = 1000;
        public List<DisplayAssignment> Assignments { get; set; }
        public RefreshOptions() { Assignments = new List<DisplayAssignment>(); }
        public int IntervalMilliseconds
        {
            get { return interval; }
            set
            {
                if (value < 250 || value > 60000) throw new ArgumentOutOfRangeException("value", "Update interval must be between 250 and 60000 ms.");
                interval = value;
            }
        }

        public static RefreshOptions Load(string path)
        {
            if (!File.Exists(path)) return new RefreshOptions();
            using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return (RefreshOptions)new XmlSerializer(typeof(RefreshOptions)).Deserialize(reader);
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true }))
                new XmlSerializer(typeof(RefreshOptions)).Serialize(writer, this);
        }
    }
}
