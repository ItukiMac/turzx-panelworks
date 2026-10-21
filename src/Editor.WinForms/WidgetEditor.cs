// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Windows.Forms;
namespace TurzxPanelWorks
{
    public sealed partial class DesignerForm
    {
        private ComboBox widgetChoice;
        private TextBox widgetUrl, widgetLocation;
        private NumericUpDown widgetLatitude, widgetLongitude, widgetInterval, widgetViewport, widgetSlide;
        private CheckBox onlineWidgets;
        private Label widgetSizeLabel;
        private bool updatingWidgets;
        private void AddWidgetEditor()
        {
            TabPage page = new TabPage("外部表示"); editorTabs.TabPages.Add(page);
            onlineWidgets = new CheckBox { Text = "外部情報をオンライン取得", Bounds = new Rectangle(8, 10, 264, 24) };
            onlineWidgets.Checked = renderer.OnlineEnabled;
            onlineWidgets.CheckedChanged += delegate { renderer.OnlineEnabled = onlineWidgets.Checked; RefreshData(); };
            page.Controls.Add(onlineWidgets);
            page.Controls.Add(new Label { Text = "Editorの取得ON/OFFです。\nHostは対象レイアウトの外部情報を取得します。", Bounds = new Rectangle(8, 38, 264, 36) });
            page.Controls.Add(LayoutButton("暦", 8, 80, 60, delegate { AddWidget("カレンダー"); }));
            page.Controls.Add(LayoutButton("天気", 74, 80, 60, delegate { AddWidget("天気"); }));
            page.Controls.Add(LayoutButton("サイト", 140, 80, 60, delegate { AddWidget("サイト"); }));
            page.Controls.Add(LayoutButton("ニュース", 206, 80, 66, delegate { AddWidget("ニュース"); }));
            page.Controls.Add(new Label { Text = "設定する要素", Bounds = new Rectangle(8, 118, 264, 20) });
            widgetChoice = new ComboBox { Bounds = new Rectangle(8, 140, 264, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            widgetChoice.SelectedIndexChanged += delegate { if (!updatingWidgets) ShowWidgetSettings(); };
            page.Controls.Add(widgetChoice);
            page.Controls.Add(new Label { Text = "サイト／ニュースRSS URL", Bounds = new Rectangle(8, 172, 264, 20) });
            widgetUrl = new TextBox { Bounds = new Rectangle(8, 194, 264, 24) }; page.Controls.Add(widgetUrl);
            page.Controls.Add(new Label { Text = "天気の地域名", Bounds = new Rectangle(8, 224, 264, 20) });
            widgetLocation = new TextBox { Bounds = new Rectangle(8, 246, 264, 24) }; page.Controls.Add(widgetLocation);
            page.Controls.Add(new Label { Text = "緯度", Bounds = new Rectangle(8, 278, 48, 20) });
            page.Controls.Add(new Label { Text = "経度", Bounds = new Rectangle(142, 278, 48, 20) });
            widgetLatitude = WidgetNumber(page, 8, 300, 126, -90, 90, 3);
            widgetLongitude = WidgetNumber(page, 142, 300, 130, -180, 180, 3);
            page.Controls.Add(new Label { Text = "取得間隔（分）", Bounds = new Rectangle(8, 334, 130, 20) });
            widgetInterval = WidgetNumber(page, 142, 332, 130, 1, 1440, 0);
            widgetSizeLabel = new Label { Text = "サイト表示幅（px）", Bounds = new Rectangle(8, 370, 130, 20) };
            page.Controls.Add(widgetSizeLabel);
            widgetViewport = WidgetNumber(page, 142, 368, 130, 320, 1920, 0);
            widgetSlide = WidgetNumber(page, 142, 368, 130, 3, 300, 0);
            page.Controls.Add(LayoutButton("選択要素に設定を反映", 8, 408, 264, delegate { ApplyWidgetSettings(); }));
            page.Controls.Add(new Label { Text = "配置とサイズは「詳細」で変更できます。\nサイトは閲覧専用。通常ブラウザーの\nログイン状態は使用しません。", Bounds = new Rectangle(8, 450, 264, 54) });
            RefreshWidgetChoices();
        }
        private static NumericUpDown WidgetNumber(TabPage page, int x, int y, int width, decimal min, decimal max, int digits)
        {
            NumericUpDown box = new NumericUpDown { Bounds = new Rectangle(x, y, width, 24), Minimum = min, Maximum = max, DecimalPlaces = digits };
            page.Controls.Add(box); return box;
        }
        private void AddWidget(string type)
        {
            SceneElement element = LayoutCatalog.Widget(type, new Rectangle(12, 12, 456, 296));
            scene.Elements.Add(element); HostParameters.AddWidgetDefinitions(scene, element); ReloadElementList(element); MarkDirty(); RefreshWidgetChoices();
            widgetChoice.SelectedItem = element; RenderCanvas();
        }
        private void RefreshWidgetChoices()
        {
            if (widgetChoice == null) return;
            SceneElement previous = widgetChoice.SelectedItem as SceneElement;
            updatingWidgets = true; widgetChoice.Items.Clear();
            foreach (SceneElement e in scene.Elements)
                if (e.Kind == SceneElementKind.Calendar || e.Kind == SceneElementKind.Weather || e.Kind == SceneElementKind.Website || e.Kind == SceneElementKind.News) widgetChoice.Items.Add(e);
            if (previous != null && widgetChoice.Items.Contains(previous)) widgetChoice.SelectedItem = previous;
            else if (widgetChoice.Items.Count > 0) widgetChoice.SelectedIndex = 0;
            updatingWidgets = false; ShowWidgetSettings();
        }
        private void ShowWidgetSettings()
        {
            SceneElement e = widgetChoice.SelectedItem as SceneElement;
            bool weather = e != null && e.Kind == SceneElementKind.Weather, web = e != null && e.Kind == SceneElementKind.Website;
            bool news = e != null && e.Kind == SceneElementKind.News;
            widgetUrl.Enabled = web || news; widgetViewport.Enabled = web; widgetSlide.Enabled = news;
            widgetViewport.Visible = !news; widgetSlide.Visible = news;
            widgetSizeLabel.Text = news ? "カード切替（秒）" : "サイト表示幅（px）";
            widgetLocation.Enabled = widgetLatitude.Enabled = widgetLongitude.Enabled = weather;
            widgetInterval.Enabled = weather || web || news;
            widgetSlide.Value = e == null ? 10 : Math.Max(3, Math.Min(300, e.SlideSeconds));
            widgetUrl.Text = e == null ? "" : e.SourceUrl ?? "";
            widgetLocation.Text = e == null ? "" : e.LocationName ?? "";
            widgetLatitude.Value = e == null || Double.IsNaN(e.Latitude) || Double.IsInfinity(e.Latitude) ? 0 : (decimal)Math.Max(-90, Math.Min(90, e.Latitude));
            widgetLongitude.Value = e == null || Double.IsNaN(e.Longitude) || Double.IsInfinity(e.Longitude) ? 0 : (decimal)Math.Max(-180, Math.Min(180, e.Longitude));
            widgetInterval.Value = e == null ? 10 : Math.Max(1, Math.Min(1440, e.RefreshMinutes));
            widgetViewport.Value = e == null ? 480 : Math.Max(320, Math.Min(1920, e.ViewportWidth));
        }
        private void ApplyWidgetSettings()
        {
            SceneElement e = widgetChoice.SelectedItem as SceneElement; if (e == null) return;
            try
            {
                if (e.Kind == SceneElementKind.Website || e.Kind == SceneElementKind.News) e.SourceUrl = WebsiteCapture.ValidateUrl(widgetUrl.Text.Trim()).AbsoluteUri;
                e.LocationName = widgetLocation.Text.Trim(); e.Latitude = (double)widgetLatitude.Value; e.Longitude = (double)widgetLongitude.Value;
                e.SlideSeconds = (int)widgetSlide.Value; e.RefreshMinutes = (int)widgetInterval.Value; e.ViewportWidth = (int)widgetViewport.Value;
                HostParameters.UpdateDefaults(scene, e);
                MarkDirty(); RenderCanvas(); status.Text = "外部表示の設定を反映しました。レイアウトを保存してください。";
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "外部表示の設定", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
