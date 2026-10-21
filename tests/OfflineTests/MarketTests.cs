// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
namespace TurzxPanelWorks
{
    internal static partial class OfflineTests
    {
        private static void MarketQuotes()
        {
            Check(MarketData.PageUrl("7203") == "https://finance.yahoo.co.jp/quote/7203.T/chart#panelworks-chart", "Japanese stock code suffix");
            Check(MarketData.PageUrl(" aapl ") == "https://finance.yahoo.co.jp/quote/AAPL/chart#panelworks-chart", "US stock code normalization");
            Check(MarketData.PageUrl("USD/JPY") == "https://finance.yahoo.co.jp/quote/USDJPY%3DFX#panelworks-chart", "forex public page mapping");
            foreach(string invalid in new[] { "AAPL&apikey=bad", "https://example.com", "../secret", "" })
            { bool bad = false; try { MarketData.PageUrl(invalid); } catch(ArgumentException) { bad=true; } Check(bad,"invalid market symbol rejected"); }
            var element = new SceneElement { Id = "market-test", Name = "USD/JPY", Kind = SceneElementKind.Market, MarketSymbol = "USD/JPY", Width = 456, Height = 296, RefreshMinutes = 15 };
            Check(element.Clone().MarketSymbol == element.MarketSymbol, "market clone preserves symbol");
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var scene = new SceneDocument(); scene.Elements.Add(element); scene.Save(path);
                var loaded = SceneDocument.Load(path);
                Check(loaded.Elements[0].MarketSymbol == "USD/JPY" && loaded.Elements[0].RefreshMinutes == 15, "market XML roundtrip");
                Check(HostParameters.Available(loaded).Exists(delegate(HostParameter p) { return p.Key == "market.refresh"; }), "market refresh exposed to Host");
                using(var renderer = new SceneRenderer(null)) using(var demo = new DummyTelemetryProvider()) using(var image = renderer.Render(loaded, demo.Capture()))
                    Check(image.Width == 480 && image.Height == 320, "market public page offline rendering");
            }
            finally { File.Delete(path); }
        }
    }
}