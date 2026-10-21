// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TurzxPanelWorks.OfflineTests")]
namespace TurzxPanelWorks
{
    // One sampler for all Host layouts. Rendering only reads the most recent sample.
    internal sealed class TimedTelemetryProvider : ITelemetryProvider
    {
        private readonly ITelemetryProvider source;
        private readonly Func<int> interval;
        private readonly Func<long> milliseconds;
        private readonly object gate = new object();
        private readonly Timer timer;
        private Task active = Task.FromResult(0);
        private long lastAttempt = Int64.MinValue;
        private bool disposed;
        private TelemetrySnapshot latest = new TelemetrySnapshot { Timestamp = DateTime.MinValue, SourceDescription = "ハードウェア取得中" };
        public string Name { get { return source.Name; } }
        internal TimedTelemetryProvider(ITelemetryProvider source, Func<int> interval)
            : this(source, interval, Stopwatch.StartNew(), true) { }
        private TimedTelemetryProvider(ITelemetryProvider source, Func<int> interval, Stopwatch watch, bool start)
            : this(source, interval, delegate { return watch.ElapsedMilliseconds; }, start) { }
        internal TimedTelemetryProvider(ITelemetryProvider source, Func<int> interval, Func<long> milliseconds, bool start)
        {
            this.source = source; this.interval = interval; this.milliseconds = milliseconds;
            if (start) timer = new Timer(delegate { Poll(); }, null, 0, 50);
        }
        internal bool IsIdle { get { lock (gate) { return active.IsCompleted; } } }
        internal void Poll()
        {
            lock (gate)
            {
                if (disposed || !active.IsCompleted) return;
                long now = milliseconds();
                if (lastAttempt != Int64.MinValue && now - lastAttempt < interval()) return;
                lastAttempt = now;
                active = Task.Run(delegate {
                    try
                    {
                        TelemetrySnapshot sample = source.Capture();
                        if (sample == null) throw new InvalidOperationException("No hardware sample.");
                        lock (gate) { if (!disposed) latest = sample; }
                    }
                    catch { /* Retain the last successful reading; the next interval retries. */ }
                });
            }
        }
        public TelemetrySnapshot Capture() { lock (gate) { return latest; } }
        public void Dispose()
        {
            Task pending;
            lock (gate) { if (disposed) return; disposed = true; pending = active; }
            if (timer != null) timer.Dispose();
            pending.GetAwaiter().GetResult();
            source.Dispose();
        }
    }
}
