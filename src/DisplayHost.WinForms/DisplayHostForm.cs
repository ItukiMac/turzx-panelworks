// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    public sealed partial class DisplayHostForm : Form
    {
        private readonly string scenePath, backgroundPath;
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StandaloneDisplay", "refresh.xml");
        private readonly IDisplayDiscovery discovery = new UsbDisplayDiscovery();
        private readonly DataGridView devices = new DataGridView();
        private readonly PictureBox preview = new PictureBox();
        private readonly TextBox status = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, TabStop = false };
        private readonly Button scan = new Button(), once = new Button(), start = new Button(), stop = new Button();
        private readonly Timer timer = new Timer();
        private readonly Timer previewTimer = new Timer { Interval = 1000 };
        private Task previewPending = Task.FromResult(0);
        private bool previewBusy;
        private int previewRevision;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly RefreshOptions options;
        private DisplayUpdateCoordinator runtime;
        private Task pendingUi = Task.FromResult(0);
        private bool busy, running, closing, readyToClose, inTray;

        public DisplayHostForm(string scenePath, string backgroundPath)
        {
            this.scenePath = scenePath;
            this.backgroundPath = backgroundPath;
            try { options = RefreshOptions.Load(settingsPath); }
            catch { options = new RefreshOptions(); }
            Text = "TURZX PanelWorks Host";
            Icon = AppLifetime.LoadIcon(typeof(DisplayHostForm).Assembly);
            ClientSize = new Size(1180, 536);
            tray.Icon = Icon;
            tray.Text = "TURZX PanelWorks Host";
            tray.ContextMenuStrip = trayMenu;
            trayMenu.Items.Add("開く", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add("終了", null, delegate { if (!closing) Close(); });
            tray.DoubleClick += delegate { RestoreFromTray(); };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && !closing && !inTray)
                {
                    inTray = true;
                    ShowInTaskbar = false;
                    Hide();
                    tray.Visible = true;
                }
            };
            VisibleChanged += delegate { if (Visible && inTray) { inTray = false; tray.Visible = false; } };
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
            DataGridViewComboBoxColumn rotation = new DataGridViewComboBoxColumn { Name = "Rotation", HeaderText = "回転(°)", FillWeight = 75, ValueType = typeof(int) };
            rotation.Items.AddRange(0, 90, 180, 270);
            devices.Columns.Add(rotation);
            devices.CurrentCellDirtyStateChanged += delegate { if (devices.IsCurrentCellDirty) devices.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            devices.CellValueChanged += delegate { PreviewSelectionChanged(); };
            devices.SelectionChanged += delegate { PreviewSelectionChanged(); };
            devices.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            { if (args.RowIndex >= 0 && (args.ColumnIndex == 2 || args.ColumnIndex == 3)) ChooseFile(args.ColumnIndex == 3); };
            Button chooseScene = new Button(), chooseBackground = new Button(), clearBackground = new Button();
            SetupButton(chooseScene, "レイアウト選択", 632, 244);
            SetupButton(chooseBackground, "背景選択", 764, 244);
            SetupButton(clearBackground, "背景なし", 896, 244);
            chooseScene.Click += delegate { ChooseFile(false); };
            chooseBackground.Click += delegate { ChooseFile(true); };
            clearBackground.Click += delegate { if (!running && !busy && devices.CurrentRow != null) SetPath(devices.CurrentRow, 3, null); };
            Label hint = new Label { Text = "行を選び、レイアウト・背景・時計回りの回転角を指定します。", Left = 632, Top = 282, Width = 532 };
            Controls.Add(hint);
            InitializeParameterPanel();
            Controls.Add(new Label { Text = "取得とカード切替は画面送信とは別に動きます。\n実機への反映は次の画面送信時です。設定は終了時に保存します。\n選択中XMLの設定だけを表示。同じキーの設定値は共有します。", Bounds = new Rectangle(16, 438, 460, 72) });
            SetupButton(scan, "機器一覧", 632, 446);
            SetupButton(once, "1回更新", 764, 446);
            SetupButton(start, "更新開始", 896, 446);
            SetupButton(stop, "停止・切断", 1028, 446);
            scan.Click += async delegate { await RunUiAsync(ScanAsync); };
            once.Click += async delegate { await RunUiAsync(UpdateAsync); };
            start.Click += async delegate { running = true; await RunUiAsync(UpdateAsync); };
            stop.Click += async delegate { await StopAsync(); };
            status.SetBounds(632, 490, 532, 30);
            status.Text = "読み込み中";
            Controls.Add(preview); Controls.Add(devices); Controls.Add(status);
            timer.Interval = options.IntervalMilliseconds;
            timer.Tick += async delegate
            {
                timer.Stop();
                if (!running || closing) return;
                if ((inTray || WindowState == FormWindowState.Minimized) && SelectedTargets().Count == 0) { ArmTimer(); return; }
                await RunUiAsync(UpdateAsync);
            };
            Shown += async delegate { await RunUiAsync(InitializeAsync); };
            previewTimer.Tick += delegate { RequestPreview(); };
            FormClosing += OnClosing;
            SetControls();
        }

        private void SetupButton(Button button, string label, int x, int y)
        {
            button.Text = label; button.SetBounds(x, y, 124, 30); Controls.Add(button);
        }
        private void RestoreFromTray()
        {
            if (!closing) AppLifetime.Restore(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
                timer.Dispose();
                previewTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private async Task InitializeAsync()
        {
            runtime = await Task.Run(delegate
            {
                return new DisplayUpdateCoordinator(
                    new SceneFrameSourceFactory(new WindowsTelemetryProvider { HeavyRefreshMilliseconds = 0 }, true, options),
                    new UsbDisplaySessionFactory(discovery), scenePath, backgroundPath);
            });
            if (closing) return;
            await ScanAsync();
            await UpdateAsync();
            previewTimer.Start();
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
                try { await previewPending; await action(); } catch (Exception error) { failure = error; }
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
                int index = devices.Rows.Add(selected.Contains(device.StableId), device.PortName, "", "", saved == null ? 0 : saved.RotationDegrees);
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
                StringBuilder message = new StringBuilder();
                if (selected.Count > 0) message.AppendLine("プレビュー: " + selected[0].Device.PortName + " / " + Path.GetFileName(selected[0].ScenePath));
                foreach (DisplaySendResult device in result.Devices)
                    message.AppendLine(device.Device.PortName + ": " + (device.Error != null ? "停止: " + device.Error.Message : device.Skipped ? "変更なし" : "転送完了"));
                status.Text = selected.Count == 0 ? "プレビュー更新（USB送信先なし）" : message.ToString();
            }
            await RefreshPreviewAsync();
        }

        private void PreviewSelectionChanged()
        {
            RefreshParameterControls();
            previewRevision++;
            Image previous = preview.Image; preview.Image = null;
            if (previous != null) previous.Dispose();
            if (runtime != null && !busy && !closing) status.Text = "選択したレイアウトを読み込み中…";
            RequestPreview();
        }

        private void RequestPreview()
        {
            RefreshParameterControls();
            if (runtime == null || closing || busy || previewBusy || (inTray && (!running || SelectedTargets().Count == 0))) return;
            previewPending = RefreshPreviewAsync();
        }

        private async Task RefreshPreviewAsync()
        {
            if (runtime == null || closing || previewBusy) return;
            previewBusy = true;
            int revision = previewRevision;
            DataGridViewRow row = devices.CurrentRow;
            string selectedScene = row == null ? scenePath : (string)row.Cells[2].Tag ?? scenePath;
            string selectedBackground = row == null ? backgroundPath : (string)row.Cells[3].Tag;
            int rotation = row == null ? 0 : Convert.ToInt32(row.Cells[4].Value);
            try
            {
                using (UpdateResult result = await runtime.PreviewAsync(selectedScene, selectedBackground, rotation, running ? SelectedTargets() : null))
                {
                    if (result != null && !closing && revision == previewRevision)
                    {
                        Image previous = preview.Image; preview.Image = result.TakePreview();
                        if (previous != null) previous.Dispose();
                    }
                }
            }
            catch (Exception error) { if (!closing && revision == previewRevision) status.Text = "プレビュー読込エラー: " + error.Message; }
            finally { previewBusy = false; }
            if (!closing && revision != previewRevision) RequestPreview();
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
            previewTimer.Stop();
            SetControls();
            await pendingUi;
            await previewPending;
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
                if (!background) dialog.InitialDirectory = LayoutCatalog.DirectoryPath;
                dialog.Filter = background ? "Images|*.png;*.jpg;*.bmp" : "Scene XML|*.xml";
                if (dialog.ShowDialog(this) == DialogResult.OK) SetPath(devices.CurrentRow, background ? 3 : 2, dialog.FileName);
            }
        }

        private List<DisplayTarget> SelectedTargets()
        {
            List<DisplayTarget> selected = new List<DisplayTarget>();
            foreach (DataGridViewRow row in devices.Rows)
                if (Convert.ToBoolean(row.Cells[0].Value))
                    selected.Add(new DisplayTarget((DisplayDevice)row.Tag, (string)row.Cells[2].Tag, (string)row.Cells[3].Tag, Convert.ToInt32(row.Cells[4].Value)));
            return selected;
        }

        private void StoreAssignments()
        {
            if (options.Assignments == null) options.Assignments = new List<DisplayAssignment>();
            foreach (DataGridViewRow row in devices.Rows)
            {
                string id = ((DisplayDevice)row.Tag).StableId;
                options.Assignments.RemoveAll(delegate(DisplayAssignment assignment) { return String.Equals(assignment.StableId, id, StringComparison.OrdinalIgnoreCase); });
                options.Assignments.Add(new DisplayAssignment { StableId = id, ScenePath = (string)row.Cells[2].Tag, BackgroundPath = (string)row.Cells[3].Tag, RotationDegrees = Convert.ToInt32(row.Cells[4].Value) });
            }
        }
    }
}
