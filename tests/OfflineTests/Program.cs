using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace StandaloneDisplay
{
    internal static class OfflineTests
    {
        private static int failures;
        private static void Check(bool condition, string name) { if (!condition) { failures++; Console.Error.WriteLine("FAIL: " + name); } }
        private static int Main()
        {
            FormulaAndScene(); Rendering(); Encoding(); MultipleDisplays();
            Console.WriteLine(failures == 0 ? "PASS: offline test suite" : "FAILURES: " + failures);
            return failures == 0 ? 0 : 1;
        }
        private static void FormulaAndScene()
        {
            using (DummyTelemetryProvider source = new DummyTelemetryProvider())
            {
                FormulaContext context = FormulaContext.FromSnapshot(source.Capture()); FormulaEngine engine = new FormulaEngine();
                Check(engine.EvaluateNumber("=[CpuLoad]", context) == engine.EvaluateNumber("=[STATS.CPU.PERCENTAGE]", context), "legacy formula alias");
                Check(engine.EvaluateText("=IF([STATS.CPU.PERCENTAGE]>0,\"yes\",\"no\")", context) == "yes", "formula condition");
            }
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".scene.xml");
            try { SceneDocument scene = SceneDocument.CreateDefault(); scene.Name = "Round trip"; scene.Save(path); SceneDocument loaded = SceneDocument.Load(path); Check(loaded.Name == scene.Name && loaded.Elements.Count == scene.Elements.Count, "scene round trip"); }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void Rendering()
        {
            using (DummyTelemetryProvider source = new DummyTelemetryProvider()) using (SceneRenderer renderer = new SceneRenderer(null)) using (Bitmap frame = renderer.Render(SceneDocument.CreateDefault(), source.Capture(), null, false))
                Check(frame.Width == 480 && frame.Height == 320, "render size without bundled asset");
        }
        private static void Encoding()
        {
            using (Bitmap frame = new Bitmap(480, 320)) using (MemoryStream output = new MemoryStream())
            {
                frame.SetPixel(0, 0, Color.Red); frame.SetPixel(1, 0, Color.Lime); frame.SetPixel(2, 0, Color.Blue); RevAFrameEncoder.WriteFrame(output, frame); byte[] data = output.ToArray();
                byte[] header = { 0, 0, 0, 0, 0, 121, 102, 1, 224, 1, 64, 0, 0, 0, 0, 0, 0, 0, 7, 125, 63, 197 };
                Check(data.Length == 307222, "encoded length"); bool same = true; for (int i = 0; i < header.Length; i++) same &= data[i] == header[i]; Check(same, "protocol header");
                Check(data[22] == 0 && data[23] == 248 && data[24] == 224 && data[25] == 7 && data[26] == 31 && data[27] == 0, "RGB565 little endian");
            }
        }
        private static void MultipleDisplays()
        {
            DisplayDevice first = new DisplayDevice("device-a", "PORT_A", "A", 480, 320); DisplayDevice second = new DisplayDevice("device-b", "PORT_B", "B", 480, 320);
            FakeFactory factory = new FakeFactory(); using (Bitmap frame = new Bitmap(480, 320)) { IList<DisplaySendResult> results = new MultiDisplaySender(factory).Send(frame, new[] { first, second }); Check(results.Count == 2 && results[0].Succeeded && results[1].Succeeded, "multiple targets"); Check(factory.Sends["device-a"] == 1 && factory.Sends["device-b"] == 1, "one send per selected target"); }
        }
        private sealed class FakeFactory : IDisplaySessionFactory
        {
            public readonly Dictionary<string, int> Sends = new Dictionary<string, int>();
            public IDisplaySession Connect(DisplayDevice device) { return new FakeSession(device, Sends); }
        }
        private sealed class FakeSession : IDisplaySession
        {
            private readonly Dictionary<string, int> sends; public DisplayDevice Device { get; private set; }
            public FakeSession(DisplayDevice device, Dictionary<string, int> sends) { Device = device; this.sends = sends; }
            public void SendFrame(Bitmap frame) { int count; sends.TryGetValue(Device.StableId, out count); sends[Device.StableId] = count + 1; }
            public void Dispose() { }
        }
    }
}
