// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace TurzxPanelWorks
{
    public sealed class UpdateResult : IDisposable
    {
        public Bitmap Preview { get; private set; }
        public IList<DisplaySendResult> Devices { get; private set; }
        public UpdateResult(Bitmap preview, IList<DisplaySendResult> devices) { Preview = preview; Devices = devices; }
        public Bitmap TakePreview() { Bitmap frame = Preview; Preview = null; return frame; }
        public void Dispose() { if (Preview != null) { Preview.Dispose(); Preview = null; } }
    }

    // One in-flight batch. Busy calls are dropped; device faults require explicit reset.
    public sealed class DisplayUpdateCoordinator : IDisposable
    {
        private sealed class TargetState
        {
            public IDisplaySession Session;
            public byte[] LastSuccessfulFrame;
            public Exception Error;
            public DisplayDevice ConnectedAs;
        }
        private sealed class FrameGroup : IDisposable
        {
            public Bitmap Image;
            public byte[] Pixels;
            public readonly Dictionary<byte[], bool> Comparisons = new Dictionary<byte[], bool>();
            public void Dispose() { if (Image != null) Image.Dispose(); }
        }
        private readonly object gate = new object();
        private readonly IFrameSource singleSource;
        private readonly IFrameSourceFactory sourceFactory;
        private readonly IDisplaySessionFactory factory;
        private readonly string defaultScene, defaultBackground;
        private readonly Dictionary<string, IFrameSource> sources = new Dictionary<string, IFrameSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TargetState> states = new Dictionary<string, TargetState>(StringComparer.OrdinalIgnoreCase);
        private Task<UpdateResult> active;
        private Task maintenance, closeTask;
        private bool closing;

        public DisplayUpdateCoordinator(IFrameSource source, IDisplaySessionFactory factory)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (factory == null) throw new ArgumentNullException("factory");
            singleSource = source;
            this.factory = factory;
        }
        public DisplayUpdateCoordinator(IFrameSourceFactory sourceFactory, IDisplaySessionFactory factory, string defaultScenePath, string defaultBackgroundPath)
        {
            if (sourceFactory == null) throw new ArgumentNullException("sourceFactory");
            if (factory == null) throw new ArgumentNullException("factory");
            this.sourceFactory = sourceFactory;
            this.factory = factory;
            defaultScene = defaultScenePath;
            defaultBackground = defaultBackgroundPath;
        }
        public Task<UpdateResult> RefreshAsync(IList<DisplayDevice> targets)
        {
            if (targets == null) throw new ArgumentNullException("targets");
            List<DisplayTarget> mapped = new List<DisplayTarget>();
            foreach (DisplayDevice device in targets) mapped.Add(new DisplayTarget(device, defaultScene, defaultBackground));
            return RefreshTargetsAsync(mapped);
        }
        public Task<UpdateResult> RefreshTargetsAsync(IList<DisplayTarget> targets)
        {
            if (targets == null) throw new ArgumentNullException("targets");
            lock (gate)
            {
                if (closing || (active != null && !active.IsCompleted) || (maintenance != null && !maintenance.IsCompleted))
                    return Task.FromResult<UpdateResult>(null);
                List<DisplayTarget> selected = new List<DisplayTarget>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DisplayTarget target in targets)
                {
                    if (target == null) throw new ArgumentException("Null display target.", "targets");
                    if (seen.Add(target.Device.StableId)) selected.Add(target);
                }
                active = Task.Run(delegate { return Refresh(selected); });
                return active;
            }
        }

        private string GroupKey(string scene, string background, int rotation = 0)
        {
            string suffix = rotation == 0 ? "" : "\nrotation=" + rotation;
            if (singleSource != null) return "single" + suffix;
            return Path.GetFullPath(scene ?? defaultScene) + "\n" + (String.IsNullOrEmpty(background) ? "" : Path.GetFullPath(background)) + suffix;
        }
        private UpdateResult Refresh(List<DisplayTarget> selected)
        {
            Dictionary<string, FrameGroup> frames = new Dictionary<string, FrameGroup>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (sourceFactory != null) sourceFactory.BeginCycle();
                string firstKey = selected.Count == 0 ? GroupKey(defaultScene, defaultBackground) : GroupKey(selected[0].ScenePath, selected[0].BackgroundPath, selected[0].RotationDegrees);
                if (selected.Count == 0) Capture(frames, firstKey, defaultScene, defaultBackground, false);
                foreach (DisplayTarget target in selected)
                {
                    FrameGroup group = CaptureTarget(frames, target);
                    if (group.Image.Width != target.Device.Width || group.Image.Height != target.Device.Height)
                        throw new ArgumentException("Frame dimensions do not match the selected display.");
                }
                List<string> staleSources = new List<string>();
                foreach (string key in sources.Keys) if (!frames.ContainsKey(key)) staleSources.Add(key);
                foreach (string key in staleSources) { IFrameSource stale = sources[key]; sources.Remove(key); stale.Dispose(); }
                HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DisplayTarget target in selected) ids.Add(target.Device.StableId);
                List<string> removed = new List<string>();
                foreach (string id in states.Keys) if (!ids.Contains(id)) removed.Add(id);
                foreach (string id in removed) { CloseSession(states[id]); states.Remove(id); }
                TargetState[] currentStates = new TargetState[selected.Count];
                for (int i = 0; i < selected.Count; i++)
                {
                    TargetState state;
                    if (!states.TryGetValue(selected[i].Device.StableId, out state))
                    {
                        state = new TargetState();
                        states.Add(selected[i].Device.StableId, state);
                    }
                    currentStates[i] = state;
                }
                DisplaySendResult[] results = new DisplaySendResult[selected.Count];
                bool[] unchangedFrames = new bool[selected.Count];
                for (int i = 0; i < selected.Count; i++)
                {
                    FrameGroup group = frames[GroupKey(selected[i].ScenePath, selected[i].BackgroundPath, selected[i].RotationDegrees)];
                    byte[] previous = currentStates[i].LastSuccessfulFrame;
                    bool same = false;
                    if (previous != null && !group.Comparisons.TryGetValue(previous, out same))
                    {
                        same = SameBytes(previous, group.Pixels);
                        group.Comparisons.Add(previous, same);
                    }
                    unchangedFrames[i] = same;
                }
                Parallel.For(0, selected.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, delegate(int index)
                {
                    DisplayTarget target = selected[index];
                    TargetState state = currentStates[index];
                    byte[] pixels = frames[GroupKey(target.ScenePath, target.BackgroundPath, target.RotationDegrees)].Pixels;
                    if (state.Error != null) { results[index] = new DisplaySendResult(target.Device, state.Error); return; }
                    bool unchanged = unchangedFrames[index];
                    try
                    {
                        if (state.ConnectedAs != null &&
                            (!String.Equals(state.ConnectedAs.PortName, target.Device.PortName, StringComparison.OrdinalIgnoreCase) ||
                             state.ConnectedAs.Width != target.Device.Width || state.ConnectedAs.Height != target.Device.Height))
                            throw new IOException("Device configuration changed. Stop and restart updates to reconnect.");
                        if (!unchanged)
                        {
                            if (state.Session == null) { state.Session = factory.Connect(target.Device); state.ConnectedAs = target.Device; }
                            state.Session.SendFrame(pixels);
                            state.LastSuccessfulFrame = pixels;
                        }
                        results[index] = new DisplaySendResult(target.Device, null, unchanged);
                    }
                    catch (Exception error)
                    {
                        state.Error = error;
                        CloseSession(state);
                        results[index] = new DisplaySendResult(target.Device, error);
                    }
                });
                Bitmap preview = frames[firstKey].Image;
                frames[firstKey].Image = null;
                return new UpdateResult(preview, results);
            }
            catch { ClearTargets(); throw; }
            finally { foreach (FrameGroup group in frames.Values) group.Dispose(); }
        }

        private FrameGroup CaptureTarget(Dictionary<string, FrameGroup> frames, DisplayTarget target)
        {
            string baseKey = GroupKey(target.ScenePath, target.BackgroundPath);
            FrameGroup original = Capture(frames, baseKey, target.ScenePath, target.BackgroundPath, target.RotationDegrees == 0);
            if (target.RotationDegrees == 0) return original;
            string key = GroupKey(target.ScenePath, target.BackgroundPath, target.RotationDegrees);
            FrameGroup rotated;
            if (frames.TryGetValue(key, out rotated)) return rotated;
            rotated = new FrameGroup { Image = FrameRotation.Apply(original.Image, target.RotationDegrees) };
            frames.Add(key, rotated);
            rotated.Pixels = Rgb565Encoder.Encode(rotated.Image);
            return rotated;
        }

        private FrameGroup Capture(Dictionary<string, FrameGroup> frames, string key, string scene, string background, bool encode)
        {
            FrameGroup existing;
            if (frames.TryGetValue(key, out existing))
            {
                if (encode && existing.Pixels == null) existing.Pixels = Rgb565Encoder.Encode(existing.Image);
                return existing;
            }
            IFrameSource source = singleSource;
            bool created = false;
            if (source == null && !sources.TryGetValue(key, out source))
            {
                source = sourceFactory.Create(scene ?? defaultScene, background);
                if (source == null) throw new InvalidOperationException("Frame source factory returned no source.");
                sources.Add(key, source);
                created = true;
            }
            try
            {
                FrameGroup group = new FrameGroup { Image = source.Capture() };
                frames.Add(key, group);
                if (group.Image == null) throw new InvalidOperationException("Frame source returned no image.");
                if (encode) group.Pixels = Rgb565Encoder.Encode(group.Image);
                return group;
            }
            catch
            {
                if (created) { sources.Remove(key); try { source.Dispose(); } catch { } }
                throw;
            }
        }
        private static bool SameBytes(byte[] previous, byte[] next)
        {
            if (previous == null || previous.Length != next.Length) return false;
            for (int i = 0; i < next.Length; i++) if (previous[i] != next[i]) return false;
            return true;
        }
        private static void CloseSession(TargetState state)
        {
            IDisplaySession session = state.Session;
            state.Session = null;
            if (session != null) try { session.Dispose(); } catch (Exception error) { if (state.Error == null) state.Error = error; }
        }
        private void ClearTargets()
        {
            foreach (TargetState state in states.Values) CloseSession(state);
            states.Clear();
        }
        public Task ResetAsync()
        {
            lock (gate)
            {
                if (closing) return closeTask ?? Task.FromResult(0);
                if (maintenance != null && !maintenance.IsCompleted) return maintenance;
                Task pending = active;
                maintenance = Task.Run(async delegate
                {
                    if (pending != null) try { await pending.ConfigureAwait(false); } catch { }
                    ClearTargets();
                });
                return maintenance;
            }
        }
        public Task CloseAsync()
        {
            lock (gate)
            {
                if (closeTask != null) return closeTask;
                closing = true;
                Task pending = active, reset = maintenance;
                closeTask = Task.Run(async delegate
                {
                    if (pending != null) try { await pending.ConfigureAwait(false); } catch { }
                    if (reset != null) try { await reset.ConfigureAwait(false); } catch { }
                    try { ClearTargets(); }
                    finally
                    {
                        List<Exception> errors = new List<Exception>();
                        foreach (IFrameSource source in sources.Values) TryDispose(source, errors);
                        sources.Clear();
                        TryDispose(singleSource, errors);
                        TryDispose(sourceFactory, errors);
                        if (errors.Count > 0) throw new AggregateException("Resource cleanup failed.", errors);
                    }
                });
                return closeTask;
            }
        }
        public void Dispose() { CloseAsync().GetAwaiter().GetResult(); }

        private static void TryDispose(IDisposable resource, List<Exception> errors)
        {
            if (resource == null) return;
            try { resource.Dispose(); } catch (Exception error) { errors.Add(error); }
        }
    }
}
