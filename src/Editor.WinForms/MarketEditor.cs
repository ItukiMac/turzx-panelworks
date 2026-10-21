// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Windows.Forms;
namespace TurzxPanelWorks
{
    public sealed partial class DesignerForm
    {
        private ComboBox marketElement, marketSymbol;
        private NumericUpDown marketInterval;

        private void AddMarketEditor()
        {
            TabPage page = new TabPage("株価・為替"); editorTabs.TabPages.Add(page);
            page.Controls.Add(new Label { Text = "表示する銘柄・通貨ペア", Bounds = new Rectangle(8, 12, 264, 24) });
            marketSymbol = new ComboBox { Bounds = new Rectangle(8, 40, 264, 24), DropDownStyle = ComboBoxStyle.DropDown };
            marketSymbol.Items.AddRange(new object[] { "7203.T", "6758.T", "9984.T", "7974.T", "USD/JPY", "EUR/JPY", "GBP/JPY", "AUD/JPY", "EUR/USD", "AAPL", "MSFT", "NVDA", "GOOGL", "AMZN", "TSLA", "SPY", "QQQ" });
            marketSymbol.Text = "USD/JPY"; page.Controls.Add(marketSymbol);
            page.Controls.Add(new Label { Text = "コードの直接入力もできます。\n例：7203（トヨタ）、AAPL（Apple）\nUSD/JPY（米ドル/円）。APIキーは不要です。", Bounds = new Rectangle(8, 74, 264, 56) });
            page.Controls.Add(new Label { Text = "取得間隔（分）", Bounds = new Rectangle(8, 136, 128, 24) });
            marketInterval = WidgetNumber(page, 144, 134, 128, 1, 1440, 0); marketInterval.Value = 15;
            page.Controls.Add(LayoutButton("この銘柄を追加", 8, 168, 264, delegate
            {
                try
                {
                    string symbol = MarketData.NormalizeSymbol(marketSymbol.Text);
                    var element = new SceneElement { Id = Guid.NewGuid().ToString("N"), Name = symbol, Kind = SceneElementKind.Market, MarketSymbol = symbol, X = 12, Y = 12, Width = 456, Height = 296, RefreshMinutes = (int)marketInterval.Value };
                    scene.Elements.Add(element); HostParameters.AddWidgetDefinitions(scene, element); ReloadElementList(element); MarkDirty(); RefreshMarkets(); marketElement.SelectedItem = element; RenderCanvas();
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "株価・為替"); }
            }));
            page.Controls.Add(new Label { Text = "変更する既存の表示", Bounds = new Rectangle(8, 208, 264, 24) });
            marketElement = new ComboBox { Bounds = new Rectangle(8, 234, 264, 24), DropDownStyle = ComboBoxStyle.DropDownList }; page.Controls.Add(marketElement);
            marketElement.SelectedIndexChanged += delegate
            {
                var element = marketElement.SelectedItem as SceneElement; if (element == null) return;
                marketSymbol.Text = element.MarketSymbol; marketInterval.Value = Math.Max(1, Math.Min(1440, element.RefreshMinutes));
            };
            page.Controls.Add(LayoutButton("選択した表示を変更", 8, 268, 264, delegate
            {
                var element = marketElement.SelectedItem as SceneElement; if (element == null) return;
                try
                {
                    element.MarketSymbol = MarketData.NormalizeSymbol(marketSymbol.Text); element.Name = element.MarketSymbol; element.RefreshMinutes = (int)marketInterval.Value;
                    HostParameters.UpdateDefaults(scene, element); ReloadElementList(element); MarkDirty(); RefreshMarkets(); RenderCanvas();
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "株価・為替"); }
            }));
            page.Controls.Add(new Label { Text = "Yahoo!ファイナンスのチャートを表示。\n登録・APIキーは不要です。\n表示内容・遅延は掲載元に準じます。\n\n取得ON/OFFは「外部表示」で設定します。\n配置は「詳細」で調整して保存してください。", Bounds = new Rectangle(8, 318, 264, 110) });
            page.Enter += delegate { RefreshMarkets(); };
        }
        private void RefreshMarkets()
        {
            SceneElement previous = marketElement.SelectedItem as SceneElement; marketElement.Items.Clear();
            foreach (SceneElement element in scene.Elements) if (element.Kind == SceneElementKind.Market) marketElement.Items.Add(element);
            if (previous != null && marketElement.Items.Contains(previous)) marketElement.SelectedItem = previous;
            else if (marketElement.Items.Count > 0) marketElement.SelectedIndex = 0;
        }
    }
}
