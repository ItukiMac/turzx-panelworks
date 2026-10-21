// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    public sealed partial class DesignerForm : Form
    {
        private string scenePath;
        private readonly string backgroundPath;
        private SceneDocument scene;
        private readonly SceneRenderer renderer;
        private ITelemetryProvider provider;
        private TelemetrySnapshot snapshot;
        private readonly PictureBox canvas;
        private readonly ListBox elementList;
        private readonly ComboBox dataMode;
        private readonly TextBox nameBox;
        private readonly TextBox formulaBox;
        private readonly ComboBox placeholderBox;
        private readonly NumericUpDown xBox, yBox, widthBox, heightBox, fontBox;
        private readonly ComboBox alignmentBox;
        private readonly Label formulaResult;
        private readonly Label status;
        private readonly Label help;
        private readonly ComboBox previewScaleBox;
        private readonly NumericUpDown refreshIntervalBox;
        private readonly TabControl editorTabs;
        private readonly TextBox layoutNameBox;
        private readonly Label layoutPathLabel;
        private readonly ListBox layoutFiles;
        private readonly System.Collections.Generic.List<Control> bottomToolbar = new System.Collections.Generic.List<Control>();
        private readonly Timer timer;
        private SceneElement selected;
        private bool applyingProperties;
        private bool dragging;
        private ResizeHandle resizeHandle;
        private Point dragStart;
        private Rectangle dragOriginal;
        private float previewScaleFactor = 1.0f;
        private int rightPanelX = 754;
        private bool isDirty;
        private bool updatingLayoutUi;

        public DesignerForm(string backgroundPath, string documentPath)
        {
            this.backgroundPath = backgroundPath;
            scenePath = documentPath;
            scene = SceneDocument.LoadOrDefault(scenePath);
            if (scene.Version < 2 || String.IsNullOrWhiteSpace(scene.Name)) scene.Name = LayoutNameFromPath(scenePath);
            scene.Version = Math.Max(2, scene.Version);
            if (LayoutCatalog.IsTemplate(scenePath)) scenePath = null;
            renderer = new SceneRenderer(backgroundPath) { OnlineEnabled = LayoutCatalog.IsTemplate(documentPath) };
            provider = new DummyTelemetryProvider();

            Text = "TURZX PanelWorks Editor";
            Icon = AppLifetime.LoadIcon(typeof(DesignerForm).Assembly);
            ClientSize = new Size(1060, 690);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            canvas = new PictureBox
            {
                Location = new Point(16, 16), Size = new Size(720, 480),
                BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Default
            };
            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseUp += CanvasMouseUp;

            elementList = new ListBox { Location = new Point(754, 16), Size = new Size(288, 178) };
            elementList.SelectedIndexChanged += delegate { SelectElement(elementList.SelectedItem as SceneElement); };

            Button addText = ButtonAt("+ Text", 754, 202, 66, delegate { AddElement(SceneElementKind.Text); });
            Button addBar = ButtonAt("+ Bar", 825, 202, 66, delegate { AddElement(SceneElementKind.Bar); });
            Button addGraph = ButtonAt("+ Graph", 896, 202, 66, delegate { AddElement(SceneElementKind.Graph); });
            Button addBox = ButtonAt("+ Box", 896, 202, 66, delegate { AddElement(SceneElementKind.Rectangle); });
            Button duplicate = ButtonAt("複製", 967, 202, 75, delegate { DuplicateElement(); });
            Button delete = ButtonAt("削除", 967, 236, 75, delegate { DeleteElement(); });

            LabelAt("名前", 754, 242, 50);
            nameBox = TextAt(806, 238, 151);
            nameBox.TextChanged += PropertyChanged;

            LabelAt("X", 754, 277, 16); xBox = NumberAt(773, 273, 62, 0, 480);
            LabelAt("Y", 842, 277, 16); yBox = NumberAt(861, 273, 62, 0, 320);
            LabelAt("W", 930, 277, 18); widthBox = NumberAt(951, 273, 91, 4, 480);
            LabelAt("H", 754, 311, 18); heightBox = NumberAt(773, 307, 62, 4, 320);
            LabelAt("Font", 842, 311, 35); fontBox = NumberAt(880, 307, 62, 5, 160);
            alignmentBox = new ComboBox
            {
                Location = new Point(951, 307), Size = new Size(91, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            alignmentBox.Items.AddRange(new object[]
            {
                new AlignmentOption("左上", "Left", "Top"), new AlignmentOption("上中央", "Center", "Top"),
                new AlignmentOption("右上", "Right", "Top"), new AlignmentOption("左中央", "Left", "Center"),
                new AlignmentOption("中央", "Center", "Center"), new AlignmentOption("右中央", "Right", "Center"),
                new AlignmentOption("左下", "Left", "Bottom"), new AlignmentOption("下中央", "Center", "Bottom"),
                new AlignmentOption("右下", "Right", "Bottom")
            });
            alignmentBox.SelectedIndexChanged += AlignmentChanged;

            LabelAt("式（Excel風）", 754, 382, 110);
            formulaBox = new TextBox { Location = new Point(754, 404), Size = new Size(288, 68), Multiline = true, ScrollBars = ScrollBars.Vertical };
            formulaBox.TextChanged += PropertyChanged;

            placeholderBox = new ComboBox { Location = new Point(754, 480), Width = 215, DropDownWidth = 440, DropDownStyle = ComboBoxStyle.DropDownList };
            placeholderBox.Items.AddRange(FormulaContext.CompatiblePlaceholders);
            placeholderBox.SelectedIndex = 0;
            Button insert = ButtonAt("挿入", 974, 478, 68, delegate { InsertPlaceholder(); });
            formulaResult = new Label { Location = new Point(754, 515), Size = new Size(288, 37), AutoEllipsis = true, ForeColor = Color.DimGray };

            dataMode = new ComboBox { Location = new Point(16, 516), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
            dataMode.Items.AddRange(new object[] { "Dummy values", "Actual Windows values" });
            dataMode.SelectedIndex = 0;
            dataMode.SelectedIndexChanged += delegate { ChangeProvider(); };
            Button refresh = ButtonAt("Refresh", 200, 514, 82, delegate { RefreshData(); });
            Button savePng = ButtonAt("Save PNG", 288, 514, 90, delegate { SavePng(); });

            Label refreshIntervalLabel = new Label { Text = "更新(秒)", Location = new Point(390, 520), Size = new Size(62, 22) };
            refreshIntervalBox = new NumericUpDown
            {
                Location = new Point(454, 516), Width = 72, DecimalPlaces = 2,
                Increment = 0.25m, Minimum = 0.25m, Maximum = 60m, Value = 1m
            };
            refreshIntervalBox.ValueChanged += delegate { timer.Interval = (int)(refreshIntervalBox.Value * 1000m); };

            help = new Label
            {
                Location = new Point(16, 558), Size = new Size(1026, 38),
                Text = "操作: ドラッグ=移動 / 白ハンドル=サイズ変更 / 矢印=1px / Shift+矢印=10px / 式と配置は「詳細」タブ"
            };
            status = new Label { Location = new Point(16, 646), Size = new Size(1026, 24), AutoEllipsis = true };

            Label previewScaleLabel = new Label { Text = "Preview", Location = new Point(610, 520), Size = new Size(58, 22) };
            previewScaleBox = new ComboBox { Location = new Point(670, 516), Width = 76, DropDownStyle = ComboBoxStyle.DropDownList };
            previewScaleBox.Items.AddRange(new object[] { "1.0x", "1.25x", "1.5x", "1.75x", "2.0x" });

            Controls.Add(canvas); Controls.Add(elementList); Controls.Add(addText); Controls.Add(addBar); Controls.Add(addGraph); Controls.Add(addBox);
            Controls.Add(duplicate); Controls.Add(delete); Controls.Add(nameBox); Controls.Add(xBox); Controls.Add(yBox);
            Controls.Add(widthBox); Controls.Add(heightBox); Controls.Add(fontBox); Controls.Add(alignmentBox);
            Controls.Add(formulaBox); Controls.Add(placeholderBox); Controls.Add(insert); Controls.Add(formulaResult);
            Controls.Add(dataMode); Controls.Add(refresh); Controls.Add(savePng);
            Controls.Add(refreshIntervalLabel); Controls.Add(refreshIntervalBox);
            Controls.Add(previewScaleLabel); Controls.Add(previewScaleBox); Controls.Add(help); Controls.Add(status);

            editorTabs = new TabControl { Location = new Point(rightPanelX, 16), Size = new Size(296, 536), Tag = "RightProperty" };
            TabPage elementsPage = new TabPage("詳細");
            TabPage layoutsPage = new TabPage("レイアウト");
            editorTabs.TabPages.Add(new TabPage("かんたん"));
            editorTabs.TabPages.Add(elementsPage); editorTabs.TabPages.Add(layoutsPage);

            elementList.Parent = elementsPage; elementList.Location = new Point(6, 8); elementList.Size = new Size(272, 108);
            Control[] elementButtons = { addText, addBar, addGraph, addBox, duplicate, delete };
            foreach (Control button in elementButtons) button.Parent = elementsPage;
            addText.SetBounds(6, 124, 64, 30); addBar.SetBounds(74, 124, 64, 30);
            addGraph.SetBounds(142, 124, 64, 30); addBox.SetBounds(210, 124, 64, 30);
            duplicate.SetBounds(6, 160, 132, 30); delete.SetBounds(142, 160, 132, 30);

            Panel propertiesPanel = new Panel
            {
                Location = new Point(6, 198), Size = new Size(272, 302),
                AutoScroll = false, BorderStyle = BorderStyle.FixedSingle
            };
            elementsPage.Controls.Add(propertiesPanel);

            System.Collections.Generic.List<Control> propertyControls = new System.Collections.Generic.List<Control>();
            foreach (Control control in Controls)
                if (control.Left >= rightPanelX && control.Top < 558) propertyControls.Add(control);
            foreach (Control control in propertyControls)
            {
                int relativeX = control.Left - rightPanelX;
                int relativeY = control.Top - 228;
                control.Parent = propertiesPanel;
                control.Location = new Point(relativeX, relativeY);
            }
            widthBox.Width = 50;
            propertiesPanel.Controls.Add(new Label { Text = "表示位置", Location = new Point(0, 112), Size = new Size(60, 22) });
            alignmentBox.SetBounds(64, 108, 120, 24);
            foreach (Control control in propertiesPanel.Controls)
                if (control is Label && String.Equals(control.Text, "式（Excel風）", StringComparison.Ordinal))
                    control.SetBounds(0, 142, 110, 22);
            formulaBox.SetBounds(0, 164, 250, 54);
            placeholderBox.SetBounds(0, 224, 170, 24); placeholderBox.DropDownWidth = 440;
            insert.SetBounds(176, 222, 74, 29);
            formulaResult.SetBounds(0, 258, 250, 34);

            layoutsPage.Controls.Add(new Label { Text = "レイアウト名", Location = new Point(8, 12), Size = new Size(110, 22) });
            layoutNameBox = new TextBox { Location = new Point(8, 36), Size = new Size(264, 24) };
            layoutNameBox.TextChanged += LayoutNameChanged;
            layoutsPage.Controls.Add(layoutNameBox);
            layoutsPage.Controls.Add(new Label { Text = "保存先", Location = new Point(8, 70), Size = new Size(80, 22) });
            layoutPathLabel = new Label { Location = new Point(8, 92), Size = new Size(264, 42), AutoEllipsis = true, ForeColor = Color.DimGray };
            layoutsPage.Controls.Add(layoutPathLabel);
            layoutsPage.Controls.Add(LayoutButton("新規", 8, 140, 126, delegate { NewLayout(); }));
            layoutsPage.Controls.Add(LayoutButton("ファイルを開く", 146, 140, 126, delegate { OpenLayoutFile(); }));
            layoutsPage.Controls.Add(LayoutButton("上書き保存", 8, 176, 126, delegate { SaveLayout(false); }));
            layoutsPage.Controls.Add(LayoutButton("名前を付けて保存", 146, 176, 126, delegate { SaveLayout(true); }));
            layoutsPage.Controls.Add(new Label { Text = "同じフォルダーのレイアウト", Location = new Point(8, 216), Size = new Size(220, 22) });
            layoutFiles = new ListBox { Location = new Point(8, 240), Size = new Size(264, 166) };
            layoutFiles.DoubleClick += delegate { OpenSelectedLayout(); };
            layoutsPage.Controls.Add(layoutFiles);
            layoutsPage.Controls.Add(LayoutButton("選択して開く", 8, 414, 126, delegate { OpenSelectedLayout(); }));
            layoutsPage.Controls.Add(LayoutButton("一覧を更新", 146, 414, 126, delegate { RefreshLayoutFiles(); }));
            layoutsPage.Controls.Add(LayoutButton("既定内容に戻す", 8, 450, 264, delegate { ResetScene(); }));
            Controls.Add(editorTabs);
            editorTabs.SelectedIndexChanged += delegate { RenderCanvas(); };
            AddQuickEditor();
            AddWidgetEditor();
            AddMarketEditor();

            bottomToolbar.Add(dataMode); bottomToolbar.Add(refresh); bottomToolbar.Add(savePng);
            bottomToolbar.Add(refreshIntervalLabel); bottomToolbar.Add(refreshIntervalBox);
            bottomToolbar.Add(previewScaleLabel); bottomToolbar.Add(previewScaleBox);
            previewScaleBox.SelectedIndexChanged += delegate { ApplyPreviewScale(); };
            previewScaleBox.SelectedItem = "1.0x";

            KeyDown += DesignerKeyDown;
            FormClosing += DesignerFormClosing;
            timer = new Timer { Interval = 1000, Enabled = true };
            timer.Tick += delegate
            {
                if (WindowState != FormWindowState.Minimized && !dragging && !formulaBox.Focused) RefreshData();
            };
            ReloadElementList(null);
            UpdateLayoutUi();
            Shown += delegate { RefreshData(); };
        }

        private Button LayoutButton(string text, int x, int y, int width, EventHandler action)
        {
            Button button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 30) };
            button.Click += action;
            return button;
        }

        private Button ButtonAt(string text, int x, int y, int width, EventHandler action)
        {
            Button button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 29) };
            button.Click += action;
            return button;
        }

        private void LabelAt(string text, int x, int y, int width)
        {
            Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 22) });
        }

        private TextBox TextAt(int x, int y, int width) { return new TextBox { Location = new Point(x, y), Width = width }; }

        private NumericUpDown NumberAt(int x, int y, int width, int minimum, int maximum)
        {
            NumericUpDown box = new NumericUpDown { Location = new Point(x, y), Width = width, Minimum = minimum, Maximum = maximum };
            box.ValueChanged += PropertyChanged;
            return box;
        }

        private void ApplyPreviewScale()
        {
            string selectedScale = Convert.ToString(previewScaleBox.SelectedItem);
            float scale;
            if (String.IsNullOrEmpty(selectedScale)
                || !Single.TryParse(selectedScale.TrimEnd('x'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out scale)) scale = 1.0f;
            previewScaleFactor = Math.Max(1.0f, Math.Min(2.0f, scale));
            canvas.Size = new Size((int)Math.Round(SceneRenderer.CanvasWidth * previewScaleFactor),
                (int)Math.Round(SceneRenderer.CanvasHeight * previewScaleFactor));

            int nextRightPanelX = canvas.Right + 18;
            int shift = nextRightPanelX - rightPanelX;
            if (shift != 0)
            {
                foreach (Control control in Controls)
                    if (String.Equals(Convert.ToString(control.Tag), "RightProperty", StringComparison.Ordinal)) control.Left += shift;
                rightPanelX = nextRightPanelX;
            }

            int toolbarTop = Math.Max(canvas.Bottom, editorTabs.Bottom) + 8;
            foreach (Control control in bottomToolbar)
            {
                int offset = control == dataMode || control == previewScaleBox ? 0 : 2;
                control.Top = toolbarTop + offset;
            }
            help.Top = toolbarTop + 34;
            help.Height = 24;
            status.Top = help.Bottom + 4;
            int requiredWidth = Math.Max(editorTabs.Right + 18, previewScaleBox.Right + 18);
            help.Width = requiredWidth - help.Left - 16;
            help.AutoEllipsis = true;
            status.Width = requiredWidth - status.Left - 16;
            ClientSize = new Size(requiredWidth, status.Bottom + 8);
            RenderCanvas();
        }

        private void ReloadElementList(SceneElement toSelect)
        {
            RefreshWidgetChoices();
            applyingProperties = true;
            elementList.Items.Clear();
            foreach (SceneElement element in scene.Elements) elementList.Items.Add(element);
            if (toSelect != null) elementList.SelectedItem = toSelect;
            applyingProperties = false;
            SelectElement(toSelect);
        }

        private void SelectElement(SceneElement element)
        {
            if (applyingProperties) return;
            selected = element;
            applyingProperties = true;
            bool enabled = element != null;
            nameBox.Enabled = formulaBox.Enabled = xBox.Enabled = yBox.Enabled = widthBox.Enabled = heightBox.Enabled = fontBox.Enabled = enabled;
            alignmentBox.Enabled = enabled && element.Kind == SceneElementKind.Text;
            if (enabled)
            {
                nameBox.Text = element.Name;
                formulaBox.Text = element.Formula ?? String.Empty;
                xBox.Value = Clamp(element.X, xBox.Minimum, xBox.Maximum);
                yBox.Value = Clamp(element.Y, yBox.Minimum, yBox.Maximum);
                widthBox.Value = Clamp(element.Width, widthBox.Minimum, widthBox.Maximum);
                heightBox.Value = Clamp(element.Height, heightBox.Minimum, heightBox.Maximum);
                fontBox.Value = Clamp((decimal)element.FontSize, fontBox.Minimum, fontBox.Maximum);
                SetAlignmentSelection(String.IsNullOrEmpty(element.Alignment) ? "Left" : element.Alignment,
                    String.IsNullOrEmpty(element.VerticalAlignment) ? "Center" : element.VerticalAlignment);
            }
            applyingProperties = false;
            RenderCanvas();
        }

        private static decimal Clamp(decimal value, decimal minimum, decimal maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }

        private void PropertyChanged(object sender, EventArgs args)
        {
            if (applyingProperties || selected == null) return;
            selected.Name = nameBox.Text;
            selected.Formula = formulaBox.Text;
            selected.X = (int)xBox.Value; selected.Y = (int)yBox.Value;
            selected.Width = (int)widthBox.Value; selected.Height = (int)heightBox.Value;
            selected.FontSize = (float)fontBox.Value;
            if (selected.Kind == SceneElementKind.Text && (sender == fontBox || sender == formulaBox)) EnsureSelectedTextFits();
            ClampElement(selected);
            UpdatePropertyNumbers();
            elementList.Refresh();
            MarkDirty();
            RenderCanvas();
        }

        private void AlignmentChanged(object sender, EventArgs args)
        {
            AlignmentOption option = alignmentBox.SelectedItem as AlignmentOption;
            if (applyingProperties || selected == null || selected.Kind != SceneElementKind.Text || option == null) return;
            selected.Alignment = option.Horizontal;
            selected.VerticalAlignment = option.Vertical;
            MarkDirty();
            RenderCanvas();
        }

        private void SetAlignmentSelection(string horizontal, string vertical)
        {
            alignmentBox.SelectedIndex = -1;
            for (int index = 0; index < alignmentBox.Items.Count; index++)
            {
                AlignmentOption option = alignmentBox.Items[index] as AlignmentOption;
                if (option != null && String.Equals(option.Horizontal, horizontal, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(option.Vertical, vertical, StringComparison.OrdinalIgnoreCase))
                { alignmentBox.SelectedIndex = index; break; }
            }
        }

        private void EnsureSelectedTextFits()
        {
            if (selected == null || selected.Kind != SceneElementKind.Text || snapshot == null) return;
            FormulaEngine engine = new FormulaEngine();
            string text = engine.EvaluateText(selected.Formula, FormulaContext.FromSnapshot(snapshot));
            FontStyle style = selected.Bold ? FontStyle.Bold : FontStyle.Regular;
            using (Font font = new Font("Bahnschrift", Math.Max(5, selected.FontSize), style, GraphicsUnit.Pixel))
            using (Bitmap measureBitmap = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(measureBitmap))
            {
                SizeF measured = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                selected.Width = Math.Max(selected.Width, Math.Min(SceneRenderer.CanvasWidth, (int)Math.Ceiling(measured.Width + 6)));
                float safeLineHeight = Math.Max(measured.Height + 4, font.GetHeight(graphics) + 6);
                selected.Height = Math.Max(selected.Height, Math.Min(SceneRenderer.CanvasHeight, (int)Math.Ceiling(safeLineHeight)));
            }
        }

        private void AddElement(SceneElementKind kind)
        {
            string name = kind == SceneElementKind.Text ? "New text"
                : kind == SceneElementKind.Bar ? "New bar"
                : kind == SceneElementKind.Graph ? "New graph" : "New box";
            SceneElement element = new SceneElement
            {
                Id = Guid.NewGuid().ToString("N"), Name = name,
                Kind = kind, X = 190, Y = 140, Width = kind == SceneElementKind.Text ? 120 : 100,
                Height = kind == SceneElementKind.Text ? 32 : kind == SceneElementKind.Graph ? 36 : 16,
                Formula = kind == SceneElementKind.Text ? "=\"New text\"" : "=50", FontSize = 20, Bold = true,
                ForeColorArgb = Color.White.ToArgb(), FillColorArgb = Color.FromArgb(0, 226, 239).ToArgb(),
                BackColorArgb = Color.FromArgb(45, 55, 72).ToArgb(), Alignment = "Center", VerticalAlignment = "Center"
            };
            scene.Elements.Add(element);
            ReloadElementList(element);
            SaveScene();
        }

        private void DuplicateElement()
        {
            if (selected == null) return;
            SceneElement copy = selected.Clone();
            ClampElement(copy);
            scene.Elements.Add(copy);
            ReloadElementList(copy);
            SaveScene();
        }

        private void DeleteElement()
        {
            if (selected == null) return;
            int index = scene.Elements.IndexOf(selected);
            scene.Elements.Remove(selected);
            SceneElement next = scene.Elements.Count == 0 ? null : scene.Elements[Math.Max(0, Math.Min(index, scene.Elements.Count - 1))];
            ReloadElementList(next);
            SaveScene();
        }

        private void InsertPlaceholder()
        {
            if (selected == null || placeholderBox.SelectedItem == null) return;
            string name = Convert.ToString(placeholderBox.SelectedItem);
            int legacy = name.IndexOf(" (legacy)", StringComparison.Ordinal);
            if (legacy >= 0) name = name.Substring(0, legacy);
            string insertion = "[" + name + "]";
            int start = formulaBox.SelectionStart;
            formulaBox.Text = formulaBox.Text.Insert(start, insertion);
            formulaBox.SelectionStart = start + insertion.Length;
            formulaBox.Focus();
        }

        private void CanvasMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left || editorTabs.SelectedIndex != 1) return;
            Point point = ToLogical(args.Location);
            resizeHandle = selected == null ? ResizeHandle.None : SelectionAdorner.HitTest(selected.Bounds, point);
            if (resizeHandle == ResizeHandle.None)
            {
                SceneElement hit = scene.HitTest(point);
                if (hit == null) { elementList.ClearSelected(); SelectElement(null); return; }
                elementList.SelectedItem = hit;
                selected = hit;
            }
            dragging = selected != null;
            if (dragging)
            {
                dragStart = point;
                dragOriginal = selected.Bounds;
                canvas.Capture = true;
            }
        }

        private void CanvasMouseMove(object sender, MouseEventArgs args)
        {
            Point point = ToLogical(args.Location);
            if (!dragging || selected == null)
            {
                ResizeHandle hover = selected == null ? ResizeHandle.None : SelectionAdorner.HitTest(selected.Bounds, point);
                canvas.Cursor = CursorFor(hover);
                return;
            }
            int dx = point.X - dragStart.X;
            int dy = point.Y - dragStart.Y;
            Rectangle next = dragOriginal;
            if (resizeHandle == ResizeHandle.None) { next.X += dx; next.Y += dy; }
            else ResizeRectangle(ref next, resizeHandle, dx, dy);
            selected.X = next.X; selected.Y = next.Y; selected.Width = Math.Max(4, next.Width); selected.Height = Math.Max(4, next.Height);
            ClampElement(selected);
            UpdatePropertyNumbers();
            RenderCanvas();
        }

        private void CanvasMouseUp(object sender, MouseEventArgs args)
        {
            if (!dragging) return;
            dragging = false; canvas.Capture = false; SaveScene();
        }

        private static void ResizeRectangle(ref Rectangle rectangle, ResizeHandle handle, int dx, int dy)
        {
            if (handle == ResizeHandle.NorthWest || handle == ResizeHandle.West || handle == ResizeHandle.SouthWest) { rectangle.X += dx; rectangle.Width -= dx; }
            if (handle == ResizeHandle.NorthEast || handle == ResizeHandle.East || handle == ResizeHandle.SouthEast) rectangle.Width += dx;
            if (handle == ResizeHandle.NorthWest || handle == ResizeHandle.North || handle == ResizeHandle.NorthEast) { rectangle.Y += dy; rectangle.Height -= dy; }
            if (handle == ResizeHandle.SouthWest || handle == ResizeHandle.South || handle == ResizeHandle.SouthEast) rectangle.Height += dy;
            if (rectangle.Width < 4) rectangle.Width = 4;
            if (rectangle.Height < 4) rectangle.Height = 4;
        }

        private void ClampElement(SceneElement element)
        {
            element.Width = Math.Max(4, Math.Min(SceneRenderer.CanvasWidth, element.Width));
            element.Height = Math.Max(4, Math.Min(SceneRenderer.CanvasHeight, element.Height));
            element.X = Math.Max(0, Math.Min(SceneRenderer.CanvasWidth - element.Width, element.X));
            element.Y = Math.Max(0, Math.Min(SceneRenderer.CanvasHeight - element.Height, element.Y));
        }

        private void UpdatePropertyNumbers()
        {
            if (selected == null) return;
            applyingProperties = true;
            xBox.Value = Clamp(selected.X, xBox.Minimum, xBox.Maximum); yBox.Value = Clamp(selected.Y, yBox.Minimum, yBox.Maximum);
            widthBox.Value = Clamp(selected.Width, widthBox.Minimum, widthBox.Maximum); heightBox.Value = Clamp(selected.Height, heightBox.Minimum, heightBox.Maximum);
            applyingProperties = false;
        }

        private Point ToLogical(Point point) { return new Point((int)Math.Round(point.X / previewScaleFactor), (int)Math.Round(point.Y / previewScaleFactor)); }

        private static Cursor CursorFor(ResizeHandle handle)
        {
            if (handle == ResizeHandle.NorthWest || handle == ResizeHandle.SouthEast) return Cursors.SizeNWSE;
            if (handle == ResizeHandle.NorthEast || handle == ResizeHandle.SouthWest) return Cursors.SizeNESW;
            if (handle == ResizeHandle.North || handle == ResizeHandle.South) return Cursors.SizeNS;
            if (handle == ResizeHandle.East || handle == ResizeHandle.West) return Cursors.SizeWE;
            return Cursors.Default;
        }

        private void DesignerKeyDown(object sender, KeyEventArgs args)
        {
            if (selected == null || editorTabs.SelectedIndex != 1 || formulaBox.Focused || nameBox.Focused) return;
            if (args.KeyCode == Keys.Delete) { DeleteElement(); args.Handled = true; return; }
            int step = args.Shift ? 10 : 1;
            if (args.KeyCode == Keys.Left) selected.X -= step; else if (args.KeyCode == Keys.Right) selected.X += step;
            else if (args.KeyCode == Keys.Up) selected.Y -= step; else if (args.KeyCode == Keys.Down) selected.Y += step; else return;
            ClampElement(selected); UpdatePropertyNumbers(); RenderCanvas(); SaveScene(); args.Handled = true; args.SuppressKeyPress = true;
        }

        private void ChangeProvider()
        {
            provider.Dispose();
            provider = dataMode.SelectedIndex == 1 ? (ITelemetryProvider)new WindowsTelemetryProvider() : new DummyTelemetryProvider();
            RefreshData();
        }

        private void RefreshData()
        {
            try
            {
                snapshot = provider.Capture();
                RefreshPlaceholderChoices();
                RenderCanvas();
                status.Text = provider.Name + " | editor preview";
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private void RefreshPlaceholderChoices()
        {
            List<string> choices = new List<string>(FormulaContext.CompatiblePlaceholders);
            List<string> detected = new List<string>();
            if (snapshot != null && snapshot.AdditionalValues != null)
            {
                foreach (string name in snapshot.AdditionalValues.Keys)
                {
                    if (name.StartsWith("STATS.CPU.CORE.", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("STATS.GPU.ENGINE.", StringComparison.OrdinalIgnoreCase))
                        detected.Add(name);
                }
            }
            detected.Sort(StringComparer.OrdinalIgnoreCase);
            choices.AddRange(detected);
            bool unchanged = placeholderBox.Items.Count == choices.Count;
            if (unchanged)
                for (int index = 0; index < choices.Count; index++)
                    if (!String.Equals(Convert.ToString(placeholderBox.Items[index]), choices[index], StringComparison.OrdinalIgnoreCase)) { unchanged = false; break; }
            if (unchanged) return;

            string previous = Convert.ToString(placeholderBox.SelectedItem);
            placeholderBox.BeginUpdate();
            placeholderBox.Items.Clear();
            placeholderBox.Items.AddRange(choices.ToArray());
            int previousIndex = String.IsNullOrEmpty(previous) ? -1 : placeholderBox.FindStringExact(previous);
            placeholderBox.SelectedIndex = previousIndex >= 0 ? previousIndex : 0;
            placeholderBox.EndUpdate();
        }

        private void RenderCanvas()
        {
            if (snapshot == null) return;
            Bitmap next = renderer.Render(scene, snapshot);
            if (selected != null && editorTabs != null && editorTabs.SelectedIndex == 1) SelectionAdorner.Draw(next, selected.Bounds);
            Image old = canvas.Image; canvas.Image = next; if (old != null) old.Dispose();
            if (selected != null)
            {
                FormulaEngine engine = new FormulaEngine();
                formulaResult.Text = "結果: " + engine.EvaluateText(selected.Formula, FormulaContext.FromSnapshot(snapshot));
            }
            else formulaResult.Text = String.Empty;
        }

        private void SaveScene()
        {
            MarkDirty();
            if (String.IsNullOrEmpty(scenePath))
            {
                status.Text = "未保存のレイアウトです。「レイアウト」タブから保存してください。";
                return;
            }
            SaveDocument(scenePath);
        }

        private void ResetScene()
        {
            SceneDocument defaults = SceneDocument.CreateDefault();
            scene = defaults;
            SyncQuickChoices();
            UpdateLayoutUi();
            ReloadElementList(scene.Elements.Count > 0 ? scene.Elements[0] : null);
            MarkDirty();
            status.Text = "既定内容へ戻しました。保存するまで元のファイルは変更されません。";
            RenderCanvas();
        }

        private void LayoutNameChanged(object sender, EventArgs args)
        {
            if (updatingLayoutUi) return;
            scene.Name = layoutNameBox.Text;
            MarkDirty();
        }

        private void MarkDirty()
        {
            isDirty = true;
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            string name = String.IsNullOrWhiteSpace(scene.Name) ? "Untitled" : scene.Name.Trim();
            Text = "TURZX PanelWorks Editor — " + name + (isDirty ? " *" : String.Empty);
        }

        private void UpdateLayoutUi()
        {
            updatingLayoutUi = true;
            layoutNameBox.Text = String.IsNullOrWhiteSpace(scene.Name) ? "Untitled" : scene.Name;
            layoutPathLabel.Text = String.IsNullOrEmpty(scenePath) ? "未保存" : Path.GetFullPath(scenePath);
            layoutPathLabel.Tag = layoutPathLabel.Text;
            updatingLayoutUi = false;
            UpdateWindowTitle();
            RefreshLayoutFiles();
        }

        private void NewLayout()
        {
            if (!ConfirmDiscardChanges()) return;
            scene = SceneDocument.CreateDefault();
            scene.Name = "新しいレイアウト";
            SyncQuickChoices();
            scenePath = null;
            isDirty = true;
            ReloadElementList(scene.Elements.Count > 0 ? scene.Elements[0] : null);
            UpdateLayoutUi();
            editorTabs.SelectedIndex = 2;
            status.Text = "新しい未保存レイアウトを作成しました。";
            RenderCanvas();
        }

        private void OpenLayoutFile()
        {
            if (!ConfirmDiscardChanges()) return;
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Dashboard layout (*.scene.xml)|*.scene.xml|XML files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "レイアウトを開く",
                InitialDirectory = CurrentLayoutDirectory()
            })
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadDocument(dialog.FileName);
        }

        private void OpenSelectedLayout()
        {
            LayoutFileItem item = layoutFiles.SelectedItem as LayoutFileItem;
            if (item == null) return;
            if (!ConfirmDiscardChanges()) return;
            LoadDocument(item.Path);
        }

        private void LoadDocument(string path)
        {
            try
            {
                SceneDocument loaded = SceneDocument.Load(path);
                scene = loaded;
                scenePath = Path.GetFullPath(path);
                if (scene.Version < 2 || String.IsNullOrWhiteSpace(scene.Name)) scene.Name = LayoutNameFromPath(scenePath);
                scene.Version = Math.Max(2, scene.Version);
                if (LayoutCatalog.IsTemplate(scenePath)) { scenePath = null; renderer.OnlineEnabled = true; if (onlineWidgets != null) onlineWidgets.Checked = true; }
                isDirty = false;
                SyncQuickChoices();
                ReloadElementList(scene.Elements.Count > 0 ? scene.Elements[0] : null);
                UpdateLayoutUi();
                status.Text = String.IsNullOrEmpty(scenePath) ? "テンプレートのコピーを開きました。別名で保存してください。" : "読込: " + scenePath;
                RenderCanvas();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "レイアウト読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveLayout(bool saveAs)
        {
            string target = scenePath;
            if (saveAs || String.IsNullOrEmpty(target))
            {
                using (SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = "Dashboard layout (*.scene.xml)|*.scene.xml|XML files (*.xml)|*.xml",
                    Title = "レイアウトに名前を付けて保存",
                    InitialDirectory = CurrentLayoutDirectory(),
                    FileName = SafeFileName(String.IsNullOrWhiteSpace(layoutNameBox.Text) ? "layout" : layoutNameBox.Text) + ".scene.xml",
                    DefaultExt = "scene.xml",
                    AddExtension = true,
                    OverwritePrompt = true
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    target = dialog.FileName;
                }
            }
            SaveDocument(target);
        }

        private bool SaveDocument(string path)
        {
            try
            {
                scene.Name = String.IsNullOrWhiteSpace(layoutNameBox.Text) ? LayoutNameFromPath(path) : layoutNameBox.Text.Trim();
                scene.Version = Math.Max(2, scene.Version);
                scene.Save(path);
                scenePath = Path.GetFullPath(path);
                isDirty = false;
                UpdateLayoutUi();
                status.Text = "保存: " + scenePath;
                return true;
            }
            catch (Exception error)
            {
                status.Text = "保存エラー: " + error.Message;
                MessageBox.Show(this, error.Message, "レイアウト保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool ConfirmDiscardChanges()
        {
            if (!isDirty) return true;
            DialogResult answer = MessageBox.Show(this, "現在のレイアウトには未保存の変更があります。保存しますか？",
                "未保存の変更", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.No) return true;
            SaveLayout(false);
            return !isDirty;
        }

        private void DesignerFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!ConfirmDiscardChanges()) args.Cancel = true;
        }

        private void RefreshLayoutFiles()
        {
            if (layoutFiles == null) return;
            string selectedPath = (layoutFiles.SelectedItem as LayoutFileItem) == null ? null : ((LayoutFileItem)layoutFiles.SelectedItem).Path;
            layoutFiles.BeginUpdate();
            layoutFiles.Items.Clear();
            string directory = CurrentLayoutDirectory();
            if (Directory.Exists(directory))
                foreach (string file in SortedLayoutFiles(directory))
                    layoutFiles.Items.Add(new LayoutFileItem(file));
            for (int index = 0; index < layoutFiles.Items.Count; index++)
            {
                LayoutFileItem item = (LayoutFileItem)layoutFiles.Items[index];
                if (String.Equals(item.Path, scenePath ?? selectedPath, StringComparison.OrdinalIgnoreCase))
                { layoutFiles.SelectedIndex = index; break; }
            }
            layoutFiles.EndUpdate();
        }

        private static string[] SortedLayoutFiles(string directory)
        {
            string[] files = Directory.GetFiles(directory, "*.scene.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private string CurrentLayoutDirectory()
        {
            if (!String.IsNullOrEmpty(scenePath)) return Path.GetDirectoryName(Path.GetFullPath(scenePath));
            string directory = LayoutCatalog.DirectoryPath;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            return directory;
        }

        private static string SafeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return String.IsNullOrWhiteSpace(name) ? "layout" : name.Trim();
        }

        private static string LayoutNameFromPath(string path)
        {
            if (String.IsNullOrEmpty(path)) return "新しいレイアウト";
            string name = Path.GetFileName(path);
            return name.EndsWith(".scene.xml", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - ".scene.xml".Length) : Path.GetFileNameWithoutExtension(name);
        }

        private sealed class LayoutFileItem
        {
            public readonly string Path;
            public LayoutFileItem(string path) { Path = System.IO.Path.GetFullPath(path); }
            public override string ToString() { return LayoutNameFromPath(Path); }
        }

        private sealed class AlignmentOption
        {
            public readonly string Horizontal;
            public readonly string Vertical;
            private readonly string label;
            public AlignmentOption(string label, string horizontal, string vertical)
            { this.label = label; Horizontal = horizontal; Vertical = vertical; }
            public override string ToString() { return label; }
        }

        private void SavePng()
        {
            if (snapshot == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "display-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    using (Bitmap image = renderer.Render(scene, snapshot)) image.Save(dialog.FileName, ImageFormat.Png);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (canvas.Image != null) canvas.Image.Dispose(); provider.Dispose(); renderer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
