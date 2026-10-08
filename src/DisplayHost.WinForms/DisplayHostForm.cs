using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StandaloneDisplay
{
    public sealed class DisplayHostForm : Form
    {
        private readonly string scenePath;
        private readonly SceneRenderer renderer;
        private readonly ITelemetryProvider telemetry = new WindowsTelemetryProvider();
        private readonly IDisplayDiscovery discovery = new UsbDisplayDiscovery();
        private readonly CheckedListBox devices = new CheckedListBox();
        private readonly PictureBox preview = new PictureBox();
        private readonly Label status = new Label();
        private Bitmap frame;
        private bool sending;

        public DisplayHostForm(string scenePath, string backgroundPath)
        {
            this.scenePath = scenePath; renderer = new SceneRenderer(backgroundPath);
            Text = "Standalone Display Host"; ClientSize = new Size(1000, 520); StartPosition = FormStartPosition.CenterScreen;
            preview.SetBounds(16, 16, 720, 480); preview.SizeMode = PictureBoxSizeMode.Zoom; preview.BackColor = Color.Black;
            devices.SetBounds(748, 16, 236, 300); devices.CheckOnClick = true;
            Button refresh = new Button { Text = "Scan", Left = 748, Top = 326, Width = 112 }; refresh.Click += async delegate { await RefreshDevices(); };
            Button send = new Button { Text = "Send", Left = 872, Top = 326, Width = 112 }; send.Click += async delegate { await SendSelected(); };
            status.SetBounds(748, 366, 236, 138); status.Text = "Ready";
            Controls.Add(preview); Controls.Add(devices); Controls.Add(refresh); Controls.Add(send); Controls.Add(status);
            Shown += async delegate { Render(); await RefreshDevices(); };
            FormClosed += delegate { if (frame != null) frame.Dispose(); renderer.Dispose(); telemetry.Dispose(); };
        }

        private void Render()
        {
            SceneDocument scene = SceneDocument.LoadOrDefault(scenePath); TelemetrySnapshot snapshot = telemetry.Capture();
            Bitmap next = renderer.Render(scene, snapshot, null, false); Bitmap previous = frame; frame = next; preview.Image = next; if (previous != null) previous.Dispose();
        }

        private async Task RefreshDevices()
        {
            if (sending) return; status.Text = "Scanning";
            try { IList<DisplayDevice> found = await Task.Run(delegate { return discovery.FindDevices(); }); devices.Items.Clear(); foreach (DisplayDevice device in found) devices.Items.Add(device, false); status.Text = found.Count + " found"; }
            catch (Exception error) { status.Text = error.Message; }
        }

        private async Task SendSelected()
        {
            if (sending || frame == null) return;
            List<DisplayDevice> selected = new List<DisplayDevice>(); foreach (object item in devices.CheckedItems) selected.Add((DisplayDevice)item);
            if (selected.Count == 0) { status.Text = "Select target"; return; }
            sending = true; status.Text = "Sending";
            try
            {
                Render(); Bitmap current = frame;
                IList<DisplaySendResult> results = await Task.Run(delegate { return new MultiDisplaySender(new UsbDisplaySessionFactory(discovery)).Send(current, selected); });
                int succeeded = 0; foreach (DisplaySendResult result in results) if (result.Succeeded) succeeded++;
                status.Text = succeeded + "/" + results.Count + " sent";
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { sending = false; }
        }
    }
}
