// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private static void XmlHostParameters()
        {
            string directory = Path.Combine(Path.GetTempPath(), "xml-parameters-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SceneDocument news = LayoutCatalog.Template(6);
                string path = Path.Combine(directory, "news.xml"); news.Save(path);
                SceneDocument loaded = SceneDocument.Load(path);
                List<HostParameter> definitions = HostParameters.Available(loaded);
                Check(definitions.Count == 3 && definitions.Exists(delegate(HostParameter p) { return p.Key == "news.slide"; }) &&
                    !definitions.Exists(delegate(HostParameter p) { return p.Key == "host.hardware" || p.Key == "weather.refresh"; }), "XML news layout exposes only send/fetch/slide");
                HostParameter slide = definitions.Find(delegate(HostParameter p) { return p.Key == "news.slide"; });
                slide.Label = "記事の切替"; slide.Minimum = 5; slide.Maximum = 120; slide.Value = 17;
                loaded.Save(path); loaded = SceneDocument.Load(path);
                slide = HostParameters.Available(loaded).Find(delegate(HostParameter p) { return p.Key == "news.slide"; });
                RefreshOptions settings = new RefreshOptions();
                Check(slide.Label == "記事の切替" && settings.ResolveParameter(slide) == 17, "XML label/range/default preserved and used");
                settings.SetParameter(slide, 23);
                string optionsPath = Path.Combine(directory, "settings.xml"); settings.Save(optionsPath);
                settings = RefreshOptions.Load(optionsPath);
                SceneDocument applied = HostParameters.Apply(loaded, settings.ResolveParameter);
                Check(applied.Elements[0].SlideSeconds == 23 && loaded.Elements[0].SlideSeconds == 10, "saved generic parameter applies to render copy only");
                HostParameter width = new HostParameter { Key = "card.width", Label = "カード幅", Unit = "px", Target = "kind:News", Property = "Width", Minimum = 100, Maximum = 456, Value = 400 };
                loaded.HostParameters.Add(width);
                loaded.Save(path); loaded = SceneDocument.Load(path);
                applied = HostParameters.Apply(loaded, settings.ResolveParameter);
                Check(applied.Elements[0].Width == 400 && loaded.Elements[0].Width == 456, "new XML numeric property works without Host UI binding code");
                settings.SetParameter(width, 420);
                settings.Save(optionsPath); settings = RefreshOptions.Load(optionsPath);
                Check(HostParameters.Apply(loaded, settings.ResolveParameter).Elements[0].Width == 420, "custom XML parameter persisted and reapplied");
                using (SceneFrameSource source = new SceneFrameSource(path, new SceneRenderer(null), new DummyTelemetryProvider(), settings))
                {
                    settings.SetParameter(width, 320);
                    using (Bitmap frame = source.Capture()) Check(frame.GetPixel(350, 15).ToArgb() == Color.FromArgb(8, 12, 20).ToArgb(), "frame source uses XML numeric binding");
                    settings.SetParameter(width, 420);
                    using (Bitmap frame = source.Capture()) Check(frame.GetPixel(350, 15).ToArgb() == Color.FromArgb(19, 29, 44).ToArgb(), "generic value change reaches cached frame source immediately");
                    Check(SceneDocument.Load(path).Elements[0].Width == 456, "Host parameter changes do not rewrite layout file");
                }
                loaded.Elements.Clear();
                Check(HostParameters.Available(loaded).Count == 1, "parameters for absent elements hidden");
                File.WriteAllText(path, "<DashboardScene><Elements><Element><Kind>News</Kind><Width>480</Width><Height>320</Height><RefreshMinutes>4</RefreshMinutes><SlideSeconds>12</SlideSeconds></Element></Elements></DashboardScene>");
                SceneDocument legacy = SceneDocument.Load(path);
                List<HostParameter> inferred = HostParameters.Available(legacy);
                Check(inferred.Count == 3 && inferred.Find(delegate(HostParameter p) { return p.Key == "news.refresh"; }).Value == 4, "legacy XML infers relevant parameters from element values");
                legacy.HostParameters = new List<HostParameter>(); legacy.Save(path);
                Check(HostParameters.Available(SceneDocument.Load(path)).Count == 0, "explicit empty XML parameters stay empty");
                RefreshOptions oldSettings = new RefreshOptions { NewsSlideSeconds = 30, NewsRefreshMinutes = 2 };
                Check(oldSettings.ResolveParameter(slide) == 30, "old saved timing values migrate into generic parameter lookup");
                oldSettings.SetParameter(slide, slide.Value);
                Check(oldSettings.ResolveParameter(slide) == 17, "XML default reset supersedes legacy setting");
                bool invalid = false;
                try { HostParameters.Validate(new List<HostParameter> { width, width }); } catch (InvalidDataException) { invalid = true; }
                Check(invalid, "duplicate XML setting keys rejected");
                width.Property = "SourceUrl"; invalid = false;
                try { HostParameters.Validate(new List<HostParameter> { width }); } catch (InvalidDataException) { invalid = true; }
                Check(invalid, "non-numeric binding rejected");
                width.Property = "Width"; width.Step = .1m; invalid = false;
                try { HostParameters.Validate(new List<HostParameter> { width }); } catch (InvalidDataException) { invalid = true; }
                Check(invalid, "fractional step for integer binding rejected");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
