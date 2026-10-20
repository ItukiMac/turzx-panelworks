// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;

namespace TurzxPanelWorks
{
    public interface IFrameSource : IDisposable { Bitmap Capture(); }

    public sealed class SceneFrameSource : IFrameSource
    {
        private readonly string scenePath;
        private readonly SceneRenderer renderer;
        private readonly ITelemetryProvider telemetry;
        private SceneDocument scene;
        private DateTime writeTime;
        private long length;

        public SceneFrameSource(string scenePath, SceneRenderer renderer, ITelemetryProvider telemetry)
        {
            this.scenePath = scenePath;
            this.renderer = renderer;
            this.telemetry = telemetry;
        }

        public Bitmap Capture()
        {
            FileInfo file = new FileInfo(scenePath);
            DateTime currentTime = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue;
            long currentLength = file.Exists ? file.Length : 0;
            if (scene == null || currentTime != writeTime || currentLength != length)
            {
                // Invalid edits stop the update; never silently send a replacement layout.
                SceneDocument next = file.Exists ? SceneDocument.Load(scenePath) : SceneDocument.CreateDefault();
                scene = next;
                writeTime = currentTime;
                length = currentLength;
            }
            return renderer.Render(scene, telemetry.Capture());
        }

        public void Dispose() { try { renderer.Dispose(); } finally { telemetry.Dispose(); } }
    }
}
