// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StandaloneDisplay
{
    public sealed class DisplayHostForm : Form
    {
        private readonly string scenePath, backgroundPath;
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StandaloneDisplay", "refresh.xml");
        private readonly IDisplayDiscovery discovery = new UsbDisplayDiscovery();
        private readonly DataGridView devices = new DataGridView();
        private readonly PictureBox preview = new PictureBox();
        private readonly Label status = new Label();
        private readonly NumericUpDown interval = new NumericUpDown();
        private readonly Button scan = new Button(), once = new Button(), start = new Button(), stop = new Button();
        private readonly Timer timer = new Timer();
        private readonly RefreshOptions options;
        private DisplayUpdateCoordinator runtime;
        private Task pendingUi = Task.FromResult(0);
        private bool busy, running, closing, readyToClose;

        public DisplayHostForm(string scenePath, string backgroundPath)
        {
            this.scenePath = scenePath;
            this.backgroundPath = backgroundPath;
            try { options = RefreshOptions.Load(settingsPath); }
            catch { options = new RefreshOptions(); }
            Text = "Standalone Display Host";
            ClientSize = new Size(1180, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            preview.SetBounds(16, 16, 600, 400);
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.BackColor = Color.Black;
            devices.SetBounds(632, 16, 532, 220);
            devices.AllowUserToAddRows = false;
            devices.AllowUserToDeleteRows = false;
            devices.RowHeadersVisible = false;
            devices.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            devices.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            devices.MultiSelect = false;
            devices.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "送信", FillWeight = 35 });
            devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "機器", ReadOnly = true, FillWeight = 60 });
            devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Scene", HeaderText = "レイアウト", ReadOnly = true, FillWeight = 160 });
            devices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Background", HeaderText = "背景", ReadOnly = true, FillWeight = 120 });
            devices.CurrentCellDirtyStateChanged += delegate { if (devices.IsCurrentCellDirty) devices.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            devices.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            { if (args.RowIndex >= 0 && args.ColumnIndex >= 2) ChooseFile(args.ColumnIndex == 3); };
            Button chooseScene = new Button(), chooseBackground = new Button(), clearBackground = new Button();
            SetupButton(chooseScene, "レイアウト選択", 632, 244);
            SetupButton(chooseBackground, "背景選択", 764, 244);
            SetupButton(clearBackground, "背景なし", 896, 244);
            chooseScene.Click += delegate { ChooseFile(false); };
            chooseBackground.Click += delegate { ChooseFile(true); };
            clearBackground.Click += delegate { if (!running && !busy && devices.CurrentRow != null) SetPath(devices.CurrentRow, 3, null); };
            Label hint = new Label { Text = "行を選び、モニタごとにレイアウトと背景を割り当てます。", Left = 632, Top = 282, Width = 532 };
            Controls.Add(hint);
            Label caption = new Label { Text = "更新間隔（完了後の待機秒）", Left = 632, Top = 314, Width = 260 };
            interval.SetBounds(896, 310, 120, 28);
            interval.Minimum = 0.25m;
            interval.Maximum = 60;
            interval.DecimalPlaces = 2;
            interval.Increment = 0.25m;
            interval.Value = options.IntervalMilliseconds / 1000m;
            interval.ValueChanged += delegate { options.IntervalMilliseconds = (int)(interval.Value * 1000); timer.Interval = options.IntervalMilliseconds; };
            SetupButton(scan, "機器一覧", 632, 346);
            SetupButton(once, "1回更新", 764, 346);
            SetupButton(start, "更新開始", 896, 346);
            SetupButton(stop, "停止・切断", 1028, 346);
            scan.Click += async delegate { await RunUiAsync(ScanAsync); };
            once.Click += async delegate { await RunUiAsync(UpdateAsync); };
            start.Click += async delegate { running = true; await RunUiAsync(UpdateAsync); };
            stop.Click += async delegate { await StopAsync(); };
            status.SetBounds(632, 390, 532, 152);
            status.Text = "読み込み中";
            Controls.Add(preview); Controls.Add(devices); Controls.Add(caption); Controls.Add(interval); Controls.Add(status);
            timer.Interval = options.IntervalMilliseconds;
            timer.Tick += async delegate
            {
                timer.Stop();
                if (!running || closing) return;
                if (WindowState == FormWindowState.Minimized && SelectedTargets().Count == 0) { ArmTimer(); return; }
                await RunUiAsync(UpdateAsync);
            };
            Shown += async delegate { await RunUiAsync(InitializeAsync); };
            FormClosing += OnClosing;
            SetControls();
        }

        private void SetupButton(Button button, string label, int x, int y)
        {
            button.Text = label; button.SetBounds(x, y, 124, 30); Controls.Add(button);
        }

        private async Task InitializeAsync()
        {
            runtime = await Task.Run(delegate
            {
                return new DisplayUpdateCoordinator(
                    new SceneFrameSourceFactory(new WindowsTelemetryProvider()),
                    new UsbDisplaySessionFactory(discovery), scenePath, backgroundPath);
            });
            if (closing) return;
            await ScanAsync();
            await UpdateAsync();
        }

        private Task RunUiAsync(Func<Task> action)
        {
            if (busy || closing) return Task.FromResult(0);
            busy = true;
            timer.Stop();
            SetControls();
            pendingUi = PerformAsync(action);
            return pendingUi;
        }

        private async Task PerformAsync(Func<Task> action)
        {
            try
            {
                Exception failure = null;
                try { await action(); } catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    running = false;
                    if (runtime != null) await runtime.ResetAsync();
                    if (!closing) status.Text = "更新停止: " + failure.Message;
                }
            }
            finally { busy = false; if (!closing) { SetControls(); ArmTimer(); } }
        }

        private void ArmTimer()
        {
            if (running && !closing && !busy) { timer.Interval = options.IntervalMilliseconds; timer.Start(); }
        }

        private void SetControls()
        {
            bool enabled = runtime != null && !busy && !closing;
            scan.Enabled = enabled;
            once.Enabled = enabled;
            start.Enabled = enabled && !running;
            stop.Enabled = runtime != null && !closing;
            devices.Enabled = enabled && !running;
        }

        private async Task ScanAsync()
        {
            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayTarget target in SelectedTargets()) selected.Add(target.Device.StableId);
            StoreAssignments();
            IList<DisplayDevice> found = await Task.Run(delegate { return discovery.FindDevices(); });
            if (closing) return;
            devices.Rows.Clear();
            foreach (DisplayDevice device in found)
            {
                DisplayAssignment saved = options.Assignments.Find(delegate(DisplayAssignment value) { return String.Equals(value.StableId, device.StableId, StringComparison.OrdinalIgnoreCase); });
                int index = devices.Rows.Add(selected.Contains(device.StableId), device.PortName, "", "");
                DataGridViewRow row = devices.Rows[index];
                row.Tag = device;
                SetPath(row, 2, saved == null ? scenePath : saved.ScenePath);
                SetPath(row, 3, saved == null ? backgroundPath : saved.BackgroundPath);
            }
            status.Text = found.Count + " 台。送信先を選択してください。";
        }

        private async Task UpdateAsync()
        {
            List<DisplayTarget> selected = SelectedTargets();
            using (UpdateResult result = await runtime.RefreshTargetsAsync(selected))
            {
                if (result == null || closing) return;
                Image previous = preview.Image;
                preview.Image = result.TakePreview();
                if (previous != null) previous.Dispose();
                StringBuilder message = new StringBuilder();
                if (selected.Count > 0) message.AppendLine("プレビュー: " + selected[0].Device.PortName + " / " + Path.GetFileName(selected[0].ScenePath));
                foreach (DisplaySendResult device in result.Devices)
                    message.AppendLine(device.Device.PortName + ": " + (device.Error != null ? "停止: " + device.Error.Message : device.Skipped ? "変更なし" : "転送完了"));
                status.Text = selected.Count == 0 ? "プレビュー更新（USB送信先なし）" : message.ToString();
            }
        }

        private async Task StopAsync()
        {
            if (closing) return;
            running = false;
            timer.Stop();
            stop.Enabled = false;
            await pendingUi;
            if (closing) return;
            await RunUiAsync(async delegate { await runtime.ResetAsync(); status.Text = "停止・切断済み。再開時に対象を再照合します。"; });
        }

        private async void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (readyToClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            running = false;
            timer.Stop();
            SetControls();
            await pendingUi;
            Exception cleanupError = null;
            try { if (runtime != null) await runtime.CloseAsync(); }
            catch (Exception error) { cleanupError = error; }
            try
            {
                StoreAssignments();
                options.Save(settingsPath);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "終了処理"); }
            finally
            {
                timer.Dispose();
                if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
                if (cleanupError != null) MessageBox.Show(this, cleanupError.Message, "終了処理");
                readyToClose = true;
                Close();
            }
        }

        private static void SetPath(DataGridViewRow row, int column, string path)
        {
            row.Cells[column].Tag = path;
            row.Cells[column].Value = String.IsNullOrEmpty(path) ? "なし" : Path.GetFileName(path);
            row.Cells[column].ToolTipText = path ?? "";
        }

        private void ChooseFile(bool background)
        {
            if (busy || running || closing || devices.CurrentRow == null) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = background ? "Images|*.png;*.jpg;*.bmp" : "Scene XML|*.xml";
                if (dialog.ShowDialog(this) == DialogResult.OK) SetPath(devices.CurrentRow, background ? 3 : 2, dialog.FileName);
            }
        }

        private List<DisplayTarget> SelectedTargets()
        {
            List<DisplayTarget> selected = new List<DisplayTarget>();
            foreach (DataGridViewRow row in devices.Rows)
                if (Convert.ToBoolean(row.Cells[0].Value))
                    selected.Add(new DisplayTarget((DisplayDevice)row.Tag, (string)row.Cells[2].Tag, (string)row.Cells[3].Tag));
            return selected;
        }

        private void StoreAssignments()
        {
            if (options.Assignments == null) options.Assignments = new List<DisplayAssignment>();
            foreach (DataGridViewRow row in devices.Rows)
            {
                string id = ((DisplayDevice)row.Tag).StableId;
                options.Assignments.RemoveAll(delegate(DisplayAssignment assignment) { return String.Equals(assignment.StableId, id, StringComparison.OrdinalIgnoreCase); });
                options.Assignments.Add(new DisplayAssignment { StableId = id, ScenePath = (string)row.Cells[2].Tag, BackgroundPath = (string)row.Cells[3].Tag });
            }
        }
    }
}
