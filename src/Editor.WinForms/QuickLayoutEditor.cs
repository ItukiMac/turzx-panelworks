// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
namespace TurzxPanelWorks
{
    public sealed partial class DesignerForm
    {
        private ComboBox quickDivision, templateChoice;
        private readonly List<ComboBox> quickMetrics = new List<ComboBox>();
        private readonly List<Label> quickLabels = new List<Label>();
        private void AddQuickEditor()
        {
            TabPage page = editorTabs.TabPages[0];

            page.Controls.Add(new Label { Text = "① 連番テンプレートから選ぶ", Bounds = new Rectangle(8, 10, 264, 22) });
            templateChoice = new ComboBox { Bounds = new Rectangle(8, 34, 264, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            templateChoice.Items.AddRange(LayoutCatalog.TemplateNames); templateChoice.SelectedIndex = 0;
            int previousTemplate = 0;
            bool restoringTemplate = false;
            templateChoice.SelectedIndexChanged += delegate {
                if (restoringTemplate || templateChoice.SelectedIndex < 0) return;
                if (!ConfirmDiscardChanges()) { restoringTemplate = true; templateChoice.SelectedIndex = previousTemplate; restoringTemplate = false; return; }
                previousTemplate = templateChoice.SelectedIndex;
                LoadDocument(Path.Combine(LayoutCatalog.DirectoryPath, LayoutCatalog.FileNames[previousTemplate]));
            };
            page.Controls.Add(templateChoice);
            page.Controls.Add(LayoutButton("コピーを開く", 8, 64, 264, delegate {
                if (!ConfirmDiscardChanges()) return;
                LoadDocument(Path.Combine(LayoutCatalog.DirectoryPath, LayoutCatalog.FileNames[templateChoice.SelectedIndex]));
            }));
            page.Controls.Add(new Label { Text = "② 分割と各ブロックの内容を決める", Bounds = new Rectangle(8, 104, 264, 22) });
            quickDivision = new ComboBox { Bounds = new Rectangle(8, 130, 264, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            quickDivision.Items.AddRange(LayoutCatalog.LayoutNames); page.Controls.Add(quickDivision);
            for (int i = 0; i < 6; i++)
            {
                Label label = new Label { Bounds = new Rectangle(8, 165 + i * 32, 132, 24) };
                ComboBox choice = new ComboBox { Bounds = new Rectangle(142, 162 + i * 32, 130, 24), DropDownStyle = ComboBoxStyle.DropDownList };
                choice.Items.AddRange(LayoutCatalog.Metrics); choice.SelectedIndex = i % 6;
                quickMetrics.Add(choice); quickLabels.Add(label); page.Controls.Add(label); page.Controls.Add(choice);
            }
            quickDivision.SelectedIndexChanged += delegate { UpdateQuickChoices(); };
            quickDivision.SelectedIndex = 0;
            page.Controls.Add(LayoutButton("この内容で新規作成", 8, 358, 264, delegate { ApplyQuickLayout(); }));
            page.Controls.Add(new Label { Text = "左上から右へ順に配置します。\n作成後は「詳細」で自由に微調整できます。\n再作成では詳細の変更を引き継ぎません。", Bounds = new Rectangle(8, 396, 264, 58) });
            page.Controls.Add(LayoutButton("AI調整用セットを書き出す", 8, 466, 264, delegate { ExportForAi(); }));
            editorTabs.SelectedIndex = 0; SyncQuickChoices();
        }
        private void UpdateQuickChoices()
        {
            if (quickDivision.SelectedIndex < 0) return;
            int count = LayoutCatalog.Blocks(quickDivision.SelectedIndex).Length;
            for (int i = 0; i < quickMetrics.Count; i++)
            {
                quickMetrics[i].Visible = quickLabels[i].Visible = i < count;
                quickLabels[i].Text = "ブロック " + (i + 1) + (quickDivision.SelectedIndex == 0 ? (i == 0 ? "（上段）" : "（下段" + i + "）") : "");
            }
        }
        private void SyncQuickChoices()
        {
            if (quickDivision == null) return;
            bool known = scene.QuickLayout >= 0 && scene.QuickLayout < LayoutCatalog.LayoutNames.Length && scene.QuickMetrics != null;
            quickDivision.SelectedIndex = known ? scene.QuickLayout : 0;
            string[] fallback = { "CLOCK", "CPU", "GPU", "RAM", "UPLOAD", "DOWNLOAD" };
            for (int i = 0; i < quickMetrics.Count; i++)
            {
                string value = known && i < scene.QuickMetrics.Count ? scene.QuickMetrics[i] : fallback[i];
                quickMetrics[i].SelectedItem = Array.IndexOf(LayoutCatalog.Metrics, value) >= 0 ? value : "空白";
            }
        }
        private void ApplyQuickLayout()
        {
            if (!ConfirmDiscardChanges()) return;
            int count = LayoutCatalog.Blocks(quickDivision.SelectedIndex).Length;
            string[] values = new string[count];
            for (int i = 0; i < count; i++) values[i] = Convert.ToString(quickMetrics[i].SelectedItem);
            scene = LayoutCatalog.Create(quickDivision.SelectedIndex, values); scenePath = null; isDirty = true;
            ReloadElementList(null); UpdateLayoutUi(); RenderCanvas();
            status.Text = "新規作成しました。「レイアウト」から名前を付けて保存できます。";
        }
        private void ExportForAi()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "XML・画像・仕様の保存先（新規サブフォルダーを作成）", SelectedPath = LayoutCatalog.DirectoryPath })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string directory = AiLayoutExport.Write(dialog.SelectedPath, scene, backgroundPath);
                    status.Text = "AI調整用セット: " + directory;
                    MessageBox.Show(this, "XML・PNG・調整ガイドをAIに添付してください。\n画像には現在の背景が含まれます。共有前に確認してください。\n\n" + directory, "書き出し完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "書き出しエラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
    public static class AiLayoutExport
    {
        public static string Write(string parent, SceneDocument scene, string backgroundPath)
        {
            string directory = Path.Combine(parent, "ai-layout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            scene.Save(Path.Combine(directory, "layout.scene.xml"));
            using (SceneRenderer renderer = new SceneRenderer(backgroundPath))
            using (DummyTelemetryProvider demo = new DummyTelemetryProvider())
            {
                DateTime start = DateTime.Now;
                for (int i = 0; i < 30; i++)
                {
                    TelemetrySnapshot sample = demo.Capture();
                    sample.Timestamp = start.AddSeconds(i);
                    using (Bitmap image = renderer.Render(scene, sample))
                        if (i == 29) image.Save(Path.Combine(directory, "preview.png"), ImageFormat.Png);
                }
            }
            File.WriteAllText(Path.Combine(directory, "調整ガイド.txt"), Guide(), new UTF8Encoding(false));
            return directory;
        }
        public static string Guide()
        {
            return @"TURZX PanelWorks レイアウト調整セット

依頼文（追記して使ってください）:
添付のlayout.scene.xmlを編集し、読み込み可能なXML全文を返してください。
希望する変更: ［配色・文字サイズ・余白・表示項目など］
変更してほしくない箇所: ［記入］
設置方向: ［0 / 90 / 180 / 270度］

添付ファイル:
layout.scene.xml: 現在の配置と式。未保存の変更も含みます。
preview.png: ダミー値による480×320画像。選択枠は含みません。
この調整ガイド: XML仕様と利用可能な項目。
現在の背景はPNGに含まれます。共有前に確認してください。
背景の変更も依頼する場合は、元画像を別途添付してください。
機器IDや接続設定、保存先パスは自動収集しません。

XML仕様:
DashboardSceneをルートとし、Version=2、Name、Elements/Elementを保持します。
要素はリスト順に描画され、後の要素が前面です。Idは一意にしてください。
Kind: Text（文字）、Bar（0〜100%）、Graph（履歴）、Rectangle（背景矩形）。
追加のKind: Calendar（月間カレンダー）、Weather（天気）、Website（サイト画像）、News（循環ニュースカード）。
WeatherはLocationName・Latitude・Longitude、WebsiteはSourceUrl（http/https）・ViewportWidthを使います。
NewsはSourceUrlにRSS URL、SlideSecondsにカード切替秒数（3〜300、既定10）を保存します。
HostParametersはHostに表示する数値設定の定義です。ParameterのKey・Label・Unit・Target・Property・Minimum・Maximum・Step・Decimals・Value・Scaleを保持してください。
TargetはHost、kind:Newsなどの種類、id:要素IDです。Hostはこの定義から入力欄を生成し、Valueを既定値に使います。
HostParametersを省略した旧XMLは要素から補完します。空のHostParametersは設定項目を表示しません。
RefreshMinutesは取得間隔（1〜1440分）。既定は10分です。
カレンダーは実行日の月を表示します。予定・祝日の連携はありません。
外部表示のプレビューはオンライン取得OFFです。実表示が必要ならEditorのSave PNGで別途保存してください。
X/Yは左上原点のピクセル。X>=0、Y>=0、Width>=4、Height>=4。
X+Width<=480、Y+Height<=320。キャンバスサイズは変更できません。
FontSizeはピクセル単位、Boldはtrue/false。フォントはアプリ側で決まります。
ForeColorArgbは文字色、FillColorArgbは矩形色、BackColorArgbはバーやグラフの背景色。
色は符号付き32ビットARGB整数。バーとグラフの描画色はアプリ側で制御されます。
AlignmentはLeft/Center/Right、VerticalAlignmentはTop/Center/Bottom。
文字は枠外にはみ出す場合があるため、単位まで収まる余白を確保してください。
QuickLayout/QuickMetricsは簡易作成設定。描画はElementsが正本です。
簡易作成を再適用すると詳細変更は引き継がず再生成します。
外部ファイル参照・DTD・スクリプトは追加しないでください。サイトURLはSourceUrlへ設定してください。

式の例:
固定ラベル: =""CPU""
使用率: =FORMAT([STATS.CPU.PERCENTAGE],""0"") & "" %""
時計: =FORMAT([DATE.HOUR],""HH:mm"")
日付: =FORMAT([DATE.DAY],""yyyy/MM/dd"")
通信速度: =NETRATE([STATS.NETWORK.DOWNLOAD])
XML内では & を &amp; にエスケープします。
Excelそのものではありません。既存の式を優先し、任意のExcel関数は仮定しないでください。
未取得値は--、音量は現状0。RAM使用量の元単位はMB、通信速度はMbpsです。

利用可能な基本項目:
" + String.Join("\n", FormulaContext.CompatiblePlaceholders) + @"

追加項目:
STATS.CPU.CORE.<番号>.PERCENTAGE と STATS.GPU.ENGINE.<GPU名>.<エンジン名>.PERCENTAGE
は環境依存です。利用する場合はEditorの実測モードの項目一覧を確認してください。

読み戻し:
返されたXMLを別名の.scene.xmlでbin/layouts/に保存します。
Editorの「レイアウト」→「ファイルを開く」で読み込み、ダミー値と実測値で確認します。
背景画像とHostの機器別回転設定はXMLに含まれません。別途設定してください。
AIへの送信は自動では行いません。ChatGPTやCopilot等に3ファイルを手動で添付できます。
";
        }
    }
}
