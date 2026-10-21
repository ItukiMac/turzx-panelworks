// SPDX-License-Identifier: GPL-3.0-or-later
using System;
namespace TurzxPanelWorks
{
    // Immutable Host overrides; null preserves the layout's own settings in Editor.
    public sealed class ContentTiming
    {
        public readonly int NewsMinutes, WeatherMinutes, WebsiteMinutes, SlideSeconds;
        public ContentTiming(int newsMinutes, int weatherMinutes, int websiteMinutes, int slideSeconds)
        {
            if (newsMinutes < 1 || newsMinutes > 1440 || weatherMinutes < 1 || weatherMinutes > 1440 ||
                websiteMinutes < 1 || websiteMinutes > 1440 || slideSeconds < 3 || slideSeconds > 300)
                throw new ArgumentOutOfRangeException("timing");
            NewsMinutes = newsMinutes; WeatherMinutes = weatherMinutes; WebsiteMinutes = websiteMinutes; SlideSeconds = slideSeconds;
        }
    }
}
