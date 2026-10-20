// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace TurzxPanelWorks
{
    public interface IFrameSourceFactory : IDisposable
    {
        void BeginCycle();
        IFrameSource Create(string scenePath, string backgroundPath);
    }

    public sealed class SceneFrameSourceFactory : IFrameSourceFactory
    {
        private readonly ITelemetryProvider telemetry;
        private readonly CycleTelemetry shared;
        public SceneFrameSourceFactory(ITelemetryProvider telemetry)
        {
            this.telemetry = telemetry;
            shared = new CycleTelemetry(telemetry);
        }
        public void BeginCycle() { shared.Clear(); }
        public IFrameSource Create(string scenePath, string backgroundPath)
        {
            return new SceneFrameSource(scenePath, new SceneRenderer(backgroundPath), shared);
        }
        public void Dispose() { telemetry.Dispose(); }

        // All scenes in one update observe the same snapshot and capture only once.
        private sealed class CycleTelemetry : ITelemetryProvider
        {
            private readonly ITelemetryProvider source;
            private TelemetrySnapshot snapshot;
            public CycleTelemetry(ITelemetryProvider source) { this.source = source; }
            public string Name { get { return source.Name; } }
            public void Clear() { snapshot = null; }
            public TelemetrySnapshot Capture() { return snapshot ?? (snapshot = source.Capture()); }
            public void Dispose() { } // Owned by the factory, not by an individual scene.
        }
    }
}
