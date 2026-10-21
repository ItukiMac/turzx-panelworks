// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace TurzxPanelWorks
{
    internal static class WinFormsSmoke
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().FullName);
                Console.Error.WriteLine(error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 2 && args[0] == "--instance-probe")
                using (SingleInstance instance = new SingleInstance(args[1])) return instance.IsOwner ? 0 : 23;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--host-settings-preview")
            {
                using (DisplayHostForm host = new DisplayHostForm(LayoutCatalog.DefaultPath, null))
                using (SceneRenderer renderer = new SceneRenderer(null))
                using (DummyTelemetryProvider telemetry = new DummyTelemetryProvider())
                using (Bitmap bitmap = new Bitmap(host.Width, host.Height))
                {
                    CreateHandles(host);
                    PictureBox preview = (PictureBox)typeof(DisplayHostForm).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                    preview.Image = renderer.Render(LayoutCatalog.Template(0), telemetry.Capture());
                    host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    preview.Image.Dispose(); preview.Image = null;
                }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--news-editor-preview")
            {
                using (DesignerForm form = new DesignerForm(null, LayoutCatalog.DefaultPath))
                {
                    CreateHandles(form);
                    ((ComboBox)EditorField(form, "templateChoice")).SelectedIndex = 6;
                    if (!((CheckBox)EditorField(form, "onlineWidgets")).Checked) throw new Exception("News template did not start automatically.");
                    for (int i = 0; i < 20; i++) { Application.DoEvents(); EditorCall(form, "RefreshData"); System.Threading.Thread.Sleep(250); }
                    PictureBox canvas = (PictureBox)EditorField(form, "canvas");
                    canvas.Image.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine("PASS: news selected and rendered without extra action.");
                return 0;
            }
            CheckSingleInstance();
            CheckQuickWorkflow();
            CheckHostSelection();
            CheckDynamicHostParameters();
            string scene = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "scene.xml");
            using (DesignerForm editor = new DesignerForm(null, scene))
            using (DisplayHostForm host = new DisplayHostForm(scene, null))
            {
                if (editor.Icon == null || host.Icon == null) throw new Exception("Application icons missing.");
                using (Icon editorExe = Icon.ExtractAssociatedIcon(typeof(DesignerForm).Assembly.Location))
                using (Icon hostExe = Icon.ExtractAssociatedIcon(typeof(DisplayHostForm).Assembly.Location))
                    if (editorExe == null || hostExe == null) throw new Exception("Executable icons missing.");
                typeof(DesignerForm).GetMethod("RefreshData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                // Do not Show: the Host's live telemetry/discovery lifecycle is never started.
                foreach (Form form in new Form[] { editor, host })
                {
                    CreateHandles(form);
                    form.PerformLayout();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        if (args.Length == 1)
                        {
                            Directory.CreateDirectory(args[0]);
                            bitmap.Save(Path.Combine(args[0], form == host ? "host.png" : "editor.png"));
                        }
                    }
                }
                DataGridView table = null;
                foreach (Control control in host.Controls) if (control is DataGridView) table = (DataGridView)control;
                if (table == null || table.Columns.Count != 5) throw new Exception("Per-display assignment controls missing.");
                DataGridViewComboBoxColumn rotation = table.Columns["Rotation"] as DataGridViewComboBoxColumn;
                if (rotation == null || rotation.Items.Count != 4) throw new Exception("Rotation choices missing.");
                for (int i = 0; i < 4; i++)
                    if ((int)rotation.Items[i] != i * 90) throw new Exception("Invalid rotation choice.");
                // Never Show Host: doing so would start live discovery.
                host.WindowState = FormWindowState.Minimized;
                typeof(Control).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { EventArgs.Empty });
                NotifyIcon tray = (NotifyIcon)typeof(DisplayHostForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                if (!tray.Visible || host.ShowInTaskbar || host.Visible) throw new Exception("Host did not minimize to tray: tray=" + tray.Visible + " taskbar=" + host.ShowInTaskbar + " visible=" + host.Visible + " state=" + host.WindowState);
                host.WindowState = FormWindowState.Normal;
                typeof(Control).GetMethod("OnResize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, new object[] { EventArgs.Empty });
                // Handle recreation may normalize WindowState while hidden; the tray must remain reachable.
                if (!tray.Visible) throw new Exception("Hidden Host lost its tray icon during handle recreation.");
                foreach (Control control in editor.Controls)
                    if (control.Right > editor.ClientSize.Width || control.Bottom > editor.ClientSize.Height)
                        throw new Exception("Editor outside: " + control.GetType().Name + " " + control.Text + " " + control.Bounds + " client=" + editor.ClientSize);
                foreach (Control control in host.Controls)
                    if (control.Right > host.ClientSize.Width || control.Bottom > host.ClientSize.Height)
                        throw new Exception("Host control is outside the client area.");
            }
            Console.WriteLine("PASS: Editor/Host construction, layout and paint (no live discovery or USB).");
            return 0;
        }

        private static object EditorField(DesignerForm editor, string name)
        {
            return typeof(DesignerForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
        }
        private static object EditorCall(DesignerForm editor, string name, params object[] args)
        {
            return typeof(DesignerForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, args);
        }
        private static void CheckQuickWorkflow()
        {
            string directory = Path.Combine(Path.GetTempPath(), "quick-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string original = Path.Combine(directory, "original.scene.xml");
                SceneDocument.CreateDefault().Save(original);
                string unchanged = File.ReadAllText(original);
                using (DesignerForm editor = new DesignerForm(null, original))
                {
                    CreateHandles(editor); editor.PerformLayout();
                    TabControl tabs = (TabControl)EditorField(editor, "editorTabs");
                    if (tabs.SelectedIndex != 0 || tabs.TabPages.Count != 5 || tabs.TabPages[1].Text != "詳細") throw new Exception("Quick editor not default or detailed editor missing: " + tabs.SelectedIndex + "/" + tabs.TabPages.Count + "/" + tabs.TabPages[0].Text + "/" + tabs.TabPages[1].Text);
                    ((ComboBox)EditorField(editor, "quickDivision")).SelectedIndex = 3;
                    EditorCall(editor, "ApplyQuickLayout");
                    SceneDocument scene = (SceneDocument)EditorField(editor, "scene");
                    if (scene.QuickLayout != 3 || scene.QuickMetrics.Count != 6 || EditorField(editor, "scenePath") != null) throw new Exception("Quick creation did not create an unsaved six-block scene.");
                    if (File.ReadAllText(original) != unchanged) throw new Exception("Quick creation overwrote original layout.");
                    string saved = Path.Combine(directory, "custom.scene.xml");
                    if (!(bool)EditorCall(editor, "SaveDocument", saved)) throw new Exception("Quick layout save failed.");
                    tabs.SelectedIndex = 1;
                    SceneElement label = scene.Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.Text; });
                    EditorCall(editor, "ReloadElementList", label);
                    ((TextBox)EditorField(editor, "formulaBox")).Text = "=\"CUSTOM LABEL\"";
                    EditorCall(editor, "SaveDocument", saved);
                    SceneDocument updated = SceneDocument.Load(saved);
                    if (!updated.Elements.Exists(delegate(SceneElement e) { return e.Formula == "=\"CUSTOM LABEL\""; })) throw new Exception("Detailed formula edit did not survive save.");
                    string exported = AiLayoutExport.Write(directory, scene, null);
                    SceneDocument imported = SceneDocument.Load(Path.Combine(exported, "layout.scene.xml"));
                    if (imported.Elements.Count != scene.Elements.Count || !File.Exists(Path.Combine(exported, "調整ガイド.txt"))) throw new Exception("AI export incomplete.");
                    using (Bitmap preview = new Bitmap(Path.Combine(exported, "preview.png")))
                        if (preview.Width != 480 || preview.Height != 320) throw new Exception("AI preview dimensions incorrect.");
                    EditorCall(editor, "LoadDocument", LayoutCatalog.DefaultPath);
                    ((ComboBox)EditorField(editor, "templateChoice")).SelectedIndex = 1;
                    if (((SceneDocument)EditorField(editor, "scene")).Name != LayoutCatalog.TemplateNames[1]) throw new Exception("Template dropdown did not load immediately.");
                    if (EditorField(editor, "scenePath") != null) throw new Exception("Template was not opened as a copy.");
                    EditorCall(editor, "RefreshData");
                    for (int tab = 0; tab < tabs.TabPages.Count; tab++)
                    {
                        tabs.SelectedIndex = tab;
                        CreateHandles(tabs.TabPages[tab]);
                        foreach (Control control in tabs.TabPages[tab].Controls)
                            if (control.Right > tabs.TabPages[tab].ClientSize.Width || control.Bottom > tabs.TabPages[tab].ClientSize.Height)
                                throw new Exception("Tab control outside page: " + control.Text);
                    }
                    if (!((CheckBox)EditorField(editor, "onlineWidgets")).Checked) throw new Exception("Template selection did not enable online preview.");
                    ((CheckBox)EditorField(editor, "onlineWidgets")).Checked = false;
                    TabPage marketPage = null;
                    foreach (TabPage page in tabs.TabPages) if (page.Text == "株価・為替") marketPage = page;
                    if (marketPage == null) throw new Exception("Market selection tab missing.");
                    tabs.SelectedTab = marketPage;
                    ((ComboBox)EditorField(editor, "marketSymbol")).Text = "USD/JPY";
                    foreach (Control control in marketPage.Controls)
                        if (control is Button && control.Text == "この銘柄を追加") typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });
                    SceneElement market = ((SceneDocument)EditorField(editor, "scene")).Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.Market; });
                    if (market == null || market.MarketSymbol != "USD/JPY") throw new Exception("Market preset not added.");
                    ((ComboBox)EditorField(editor, "marketSymbol")).Text = "MSFT";
                    ((NumericUpDown)EditorField(editor, "marketInterval")).Value = 30;
                    foreach (Control control in marketPage.Controls)
                        if (control is Button && control.Text == "選択した表示を変更") typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });
                    EditorCall(editor, "SaveDocument", saved);
                    market = SceneDocument.Load(saved).Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.Market; });
                    if (market == null || market.MarketSymbol != "MSFT" || market.RefreshMinutes != 30) throw new Exception("Market selection not saved.");
                    EditorCall(editor, "AddWidget", "天気");
                    ((TextBox)EditorField(editor, "widgetLocation")).Text = "東京テスト";
                    ((NumericUpDown)EditorField(editor, "widgetLatitude")).Value = 35.681m;
                    ((NumericUpDown)EditorField(editor, "widgetLongitude")).Value = 139.767m;
                    ((NumericUpDown)EditorField(editor, "widgetInterval")).Value = 15;
                    EditorCall(editor, "ApplyWidgetSettings");
                    EditorCall(editor, "SaveDocument", saved);
                    SceneElement weather = SceneDocument.Load(saved).Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.Weather; });
                    if (weather == null || weather.LocationName != "東京テスト" || weather.RefreshMinutes != 15 || weather.Longitude != 139.767)
                        throw new Exception("Weather UI settings did not persist.");
                    EditorCall(editor, "AddWidget", "サイト");
                    ((TextBox)EditorField(editor, "widgetUrl")).Text = "https://example.com/";
                    ((NumericUpDown)EditorField(editor, "widgetViewport")).Value = 960;
                    EditorCall(editor, "ApplyWidgetSettings");
                    EditorCall(editor, "SaveDocument", saved);
                    SceneElement web = SceneDocument.Load(saved).Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.Website; });
                    if (web == null || web.SourceUrl != "https://example.com/" || web.ViewportWidth != 960) throw new Exception("Website UI settings did not persist.");
                    EditorCall(editor, "AddWidget", "ニュース");
                    ((NumericUpDown)EditorField(editor, "widgetSlide")).Value = 7;
                    EditorCall(editor, "ApplyWidgetSettings");
                    EditorCall(editor, "SaveDocument", saved);
                    SceneElement news = SceneDocument.Load(saved).Elements.Find(delegate(SceneElement e) { return e.Kind == SceneElementKind.News; });
                    if (news == null || news.SlideSeconds != 7 || news.SourceUrl != NewsFeed.YahooUrl) throw new Exception("News card settings did not persist.");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private sealed class NoUsbFactory : IDisplaySessionFactory
        {
            public int Calls;
            public IDisplaySession Connect(DisplayDevice device) { Calls++; throw new Exception("Preview must not connect USB."); }
        }
        private static void CheckHostSelection()
        {
            string directory = Path.Combine(Path.GetTempPath(), "host-selection-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string red = Path.Combine(directory, "red.xml"), blue = Path.Combine(directory, "blue.xml");
                SceneDocument scene = new SceneDocument();
                scene.Elements.Add(new SceneElement { Kind = SceneElementKind.Rectangle, Width = 480, Height = 320, FillColorArgb = Color.Red.ToArgb() });
                scene.Save(red); scene.Elements[0].FillColorArgb = Color.Blue.ToArgb(); scene.Save(blue);
                NoUsbFactory sessions = new NoUsbFactory();
                using (DisplayUpdateCoordinator runtime = new DisplayUpdateCoordinator(new SceneFrameSourceFactory(new DummyTelemetryProvider()), sessions, red, null))
                using (DisplayHostForm host = new DisplayHostForm(red, null))
                {
                    CreateHandles(host);
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    System.Collections.Generic.Dictionary<string, NumericUpDown> inputs = ParameterInputs(host);
                    if (inputs.Count != 1 || !inputs.ContainsKey("host.send")) throw new Exception("Unrelated hardware/news inputs shown for solid layout.");
                    inputs["host.send"].Value = 3;
                    RefreshOptions options = (RefreshOptions)typeof(DisplayHostForm).GetField("options", flags).GetValue(host);
                    if (options.IntervalMilliseconds != 3000) throw new Exception("XML send control did not update Host.");
                    DataGridView grid = (DataGridView)typeof(DisplayHostForm).GetField("devices", flags).GetValue(host);
                    PictureBox preview = (PictureBox)typeof(DisplayHostForm).GetField("preview", flags).GetValue(host);
                    int index = grid.Rows.Add(false, "TEST", "red", "none", 0);
                    grid.Rows[index].Cells[2].Tag = red;
                    grid.Rows[index].Tag = new DisplayDevice("test", "TEST", "Test", 480, 320);
                    typeof(DisplayHostForm).GetField("runtime", flags).SetValue(host, runtime);
                    typeof(DisplayHostForm).GetMethod("PreviewSelectionChanged", flags).Invoke(host, null);
                    WaitPreview(preview, Color.Red);
                    typeof(DisplayHostForm).GetMethod("SetPath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { grid.Rows[index], 2, blue });
                    WaitPreview(preview, Color.Blue);
                    if (sessions.Calls != 0) throw new Exception("Selection preview connected USB.");
                    if ((bool)typeof(DisplayHostForm).GetField("running", flags).GetValue(host)) throw new Exception("Selection started transmission.");
                    if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private static System.Collections.Generic.Dictionary<string, NumericUpDown> ParameterInputs(DisplayHostForm host)
        {
            return (System.Collections.Generic.Dictionary<string, NumericUpDown>)typeof(DisplayHostForm).GetField("parameterInputs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
        }
        private static void CheckDynamicHostParameters()
        {
            string directory = Path.Combine(Path.GetTempPath(), "parameter-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string newsPath = Path.Combine(directory, "news.xml"), clockPath = Path.Combine(directory, "clock.xml");
                SceneDocument scene = LayoutCatalog.Template(6);
                scene.Save(newsPath);
                HostParameter slide = scene.HostParameters.Find(delegate(HostParameter p) { return p.Key == "news.slide"; });
                slide.Label = "記事切替"; slide.Minimum = 5; slide.Maximum = 120; slide.Value = 17;
                scene.Save(newsPath); LayoutCatalog.Template(0).Save(clockPath);
                using (DisplayHostForm host = new DisplayHostForm(newsPath, null))
                {
                    CreateHandles(host);
                    BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    RefreshOptions options = new RefreshOptions();
                    typeof(DisplayHostForm).GetField("options", flags).SetValue(host, options);
                    typeof(DisplayHostForm).GetMethod("RefreshParameterControls", flags).Invoke(host, new object[] { true });
                    var inputs = ParameterInputs(host);
                    if (inputs.Count != 3 || inputs.ContainsKey("host.hardware") || inputs.ContainsKey("weather.refresh")) throw new Exception("News XML settings not filtered.");
                    NumericUpDown input = inputs["news.slide"];
                    if (input.Value != 17 || input.Minimum != 5 || input.Maximum != 120 || ((HostParameter)input.Tag).Label != "記事切替") throw new Exception("Control does not reflect XML metadata.");
                    input.Value = 23;
                    if (options.ResolveParameter(slide) != 23) throw new Exception("Dynamic input not stored.");
                    scene.HostParameters.Add(new HostParameter { Key = "card.width", Label = "カード幅", Unit = "px", Target = "kind:News", Property = "Width", Minimum = 300, Maximum = 456, Value = 420 });
                    scene.Save(newsPath);
                    typeof(DisplayHostForm).GetMethod("RequestPreview", flags).Invoke(host, null);
                    inputs = ParameterInputs(host);
                    if (!inputs.ContainsKey("card.width") || inputs["card.width"].Value != 420) throw new Exception("XML-only parameter addition not discovered.");
                    inputs["card.width"].Value = 400;
                    if (HostParameters.Apply(scene, options.ResolveParameter).Elements[0].Width != 400) throw new Exception("Generic numeric binding not applied.");
                    DataGridView grid = (DataGridView)typeof(DisplayHostForm).GetField("devices", flags).GetValue(host);
                    int row = grid.Rows.Add(false, "TEST", "", "", 0);
                    MethodInfo setPath = typeof(DisplayHostForm).GetMethod("SetPath", BindingFlags.Static | BindingFlags.NonPublic);
                    setPath.Invoke(null, new object[] { grid.Rows[row], 2, clockPath });
                    inputs = ParameterInputs(host);
                    if (inputs.Count != 2 || !inputs.ContainsKey("host.hardware") || inputs.ContainsKey("news.slide")) throw new Exception("Layout switch left unrelated settings visible.");
                    setPath.Invoke(null, new object[] { grid.Rows[row], 2, newsPath });
                    inputs = ParameterInputs(host);
                    if (inputs["news.slide"].Value != 23 || inputs["card.width"].Value != 400) throw new Exception("Dynamic setting values lost when switching layouts.");
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void WaitPreview(PictureBox preview, Color expected)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                Bitmap image = preview.Image as Bitmap;
                if (image != null && image.GetPixel(200, 200).ToArgb() == expected.ToArgb()) return;
                System.Threading.Thread.Sleep(5);
            }
            throw new Exception("Host selection did not update preview without waiting for send timer.");
        }
        private static void CheckSingleInstance()
        {
            string role = "Smoke." + Guid.NewGuid().ToString("N");
            using (SingleInstance first = new SingleInstance(role))
            {
                if (!first.IsOwner) throw new Exception("First instance rejected.");
                if (Probe(role) != 23) throw new Exception("Duplicate process was allowed.");
                if (!first.ConsumeActivation()) throw new Exception("Duplicate process did not request activation.");
                if (Probe(role + ".Other") != 0) throw new Exception("Different app role was blocked.");
            }
            if (Probe(role) != 0) throw new Exception("Instance lock was not released.");
            using (Form form = new Form())
            {
                form.ShowInTaskbar = false;
                form.WindowState = FormWindowState.Minimized;
                AppLifetime.Restore(form);
                if (!form.Visible || !form.ShowInTaskbar || form.WindowState != FormWindowState.Normal)
                    throw new Exception("Existing window restoration failed.");
            }
        }

        private static int Probe(string role)
        {
            using (Process child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                "--instance-probe " + role) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!child.WaitForExit(10000)) throw new Exception("Instance probe timed out.");
                return child.ExitCode;
            }
        }

        private static void CreateHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }
    }
}
