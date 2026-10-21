# アーキテクチャ

この文書は内部の責務分担、更新処理、通信と描画の仕組みを説明します。操作方法は[利用ガイド](user-guide.md)、検証手順は[開発ガイド](development.md)を参照してください。

## 依存関係

矢印の左側が参照される側です。名称はソースコードの構成要素に対応します。

```text
Core <- Rendering <- Editor.WinForms
Core <- Telemetry.Demo / Telemetry.Windows
Rendering + Core + Transport.Abstractions <- Runtime
Transport.Abstractions + Rendering <- Transport.Usb
Runtime + Telemetry.Windows + Transport.Usb <- DisplayHost.WinForms
```

EditorはTransportとRuntimeを参照しません。Renderingには選択枠やマウス操作を含めず、編集時の装飾はEditorのSelectionAdornerが担います。

## 更新処理とリソースの所有

Hostは対象選択とスケジュールを担います。RuntimeのDisplayUpdateCoordinatorが同時実行を1周期に限定し、処理中の要求は蓄積せず破棄します。Timerは周期完了後に設定した間隔で再開します。

DisplayTargetは機器、シーンXML、背景を対応付けます。1周期で同じXMLと背景を使う対象はFrameGroupとしてまとめます。グループごとにSceneFrameSourceがXMLキャッシュ、SceneRendererがグラフ履歴と描画を所有します。SceneFrameSourceFactoryが取得した1つのTelemetrySnapshotをグループ間で共有します。

停止は進行中の周期を待って接続を解放します。終了は描画・送信が完了してからシーンとテレメトリを解放します。TakePreviewで受け取ったBitmapはUIが所有し、差し替え時や終了時に破棄します。それ以外の周期内BitmapはRuntimeが破棄します。

## 負荷を抑える仕組み

- テレメトリ取得は更新全体で1回にまとめます。Windowsの重いセンサー情報は5秒間キャッシュし、CPU・ネットワーク値は各周期で取得します。
- シーンXMLは変更時に再読込します。同じXMLと背景の描画を共有し、回転角度ごとの変換結果をキャッシュします。
- Bitmapの画素をLockBitsで一括読出しし、RGB565へ変換します。
- 機器ごとに直前の成功画像と比較し、同じ画像の再転送を省きます。同じ送信データを共有する対象では比較も共有します。
- 接続はStableIdごとに再利用し、毎フレームのWMI列挙、接続、HELLOを避けます。機器探索は一覧更新時と接続時に行います。
- USB転送は最大4台並列で進めます。機器の失敗は保持し、周期ごとの自動再試行は行いません。

## 通信処理の責務

| 構成要素 | 責務 |
| --- | --- |
| UsbDisplayDiscovery | 対応機器の列挙 |
| UsbDisplaySessionFactory | 選択した個体IDと現在のポートの再照合・接続 |
| UsbDisplaySession | 排他接続、HELLO、期限付きの書込み・排出 |
| RevAFrameEncoder | 方向・矩形ヘッダーとRGB565データのフレーム化 |
| Rgb565Encoder | Bitmapの一括読出しと画素形式変換 |

COM番号だけを基準に接続せず、機器の個体IDを再照合します。部分応答や異なるモデルのHELLOは拒否し、既知の3.5インチ機の完全無応答だけを許容します。30秒の期限はフレームの書込み・排出に適用し、OSによる列挙や接続を含む全操作の厳密な上限ではありません。

## 回転とプレビューの生成

DisplayTargetとDisplayAssignmentは、検証済みの時計回りの回転角度RotationDegreesを保持します。値は0・90・180・270度で、既定値は0度です。

FrameRotationは背景を含む画像全体を回転し、元のフレーム寸法へ縦横比を維持して収め、余白を黒で埋めます。DisplayUpdateCoordinatorは各シーンと背景を1周期に1回描画し、回転角度ごとの変換結果を再利用します。

RGB565出力には指定された回転を保持します。画素変換後、プレビューだけを逆回転して正立させます。90・270度のプレビューは幅と高さを入れ替え、余白を含む縦長の画像になります。USBプロトコルの方向設定は変更しません。

## アプリのアイコン

独自の図形から作成したICOを各アプリのResources/App.icoに保存し、ビルド時にEXEへ埋め込みます。

## テンプレートと簡易編集

CoreのLayoutCatalogが分割領域と各指標のラベル・値・装飾を組み立てます。既定レイアウトも同じ生成処理を利用します。連番XMLは実行ファイル横のlayouts/に不足分だけ生成し、再ビルドで既存ファイルを上書きしません。

簡易設定はSceneDocumentのQuickLayoutとQuickMetricsに保存します。省略された旧XMLも読み込めます。描画の正本は引き続きElementsで、簡易設定は再作成時の入力としてのみ利用します。簡易作成は現在のファイルから切り離した新規文書を作るため、既存の詳細配置を無断で上書きしません。

EditorのQuickLayoutEditorが簡易UIとAI用書き出しを担当します。AI用プレビューは独立したSceneRendererとダミー値で生成し、編集中の実測グラフ履歴を流用しません。XML、プレビュー、仕様をローカルへ保存するのみで、外部サービスへの通信は行いません。

## カレンダーとオンライン表示

SceneElementKindのCalendar、Weather、WebsiteをWidgetRendererが描画します。天気は要素のLocationName・Latitude・Longitude、サイトはSourceUrl・ViewportWidth、双方の更新間隔はRefreshMinutesに保存します。既存XMLにこれらがなくても読み込めます。

カレンダーはTelemetrySnapshotの日付から毎月の曜日配置を計算します。天気はOpen-Meteoの日別APIをTLS 1.2で取得し、天気コードを日本語へ変換します。描画呼び出しは非同期の取得を起動するだけで、ネットワーク応答を待ちません。取得キャッシュはレンダラーごとに最大16種類、同時取得は全体で2件、ブラウザー取得は1件です。

WebsiteCaptureは専用プロファイルでChromeまたはEdgeをヘッドレス起動し、ローカルのDevTools接続からページを読み込みます。描画完了を待って画像化し、白紙を除外します。取得には25秒のキャンセル期限を設け、kill-on-closeのWindowsジョブで専用のプロセス群を管理します。通常のブラウザーやそのプロファイルには接続しません。

SceneRendererは既定でオンライン取得を無効にします。Editorはテンプレート選択またはチェック操作、HostのSceneFrameSourceFactoryは実稼働用の生成時に有効化します。オフライン試験とAI用書き出しは無効のままです。レンダラーの破棄時に取得をキャンセルします。

NewsはNewsCardsでRSSを非同期取得し、SlideSeconds（既定10秒）で見出しを循環します。XMLのDTDは禁止、取得上限1MiB、最大30記事、キャッシュ最大16URLです。更新失敗時は直前のデータを維持します。HostのPreviewAsyncは送信セッションを開かず、選択行を描画します。選択の世代番号で遅れて届く旧プレビューを破棄し、送信とプレビューの処理を直列化します。

## Hostの取得・描画・送信の間隔

SceneDocument.HostParametersが数値設定の定義を持ち、HostParameterEditorはその一覧から入力欄を生成します。RefreshOptions.ParameterValuesはKeyと変更値の一覧を保存します。SceneFrameSourceはHostParameters.Applyで要素のコピーへ値を適用し、既存の描画・取得処理へ渡します。要素のID・座標を保持してコピーするため、レイアウトXMLやグラフ履歴を書き換えず、取得キャッシュも維持できます。旧6項目の保存値は互換読み込みに残します。

HostのSceneFrameSourceFactoryはTimedTelemetryProviderを1つ所有します。単調増加時計でハードウェア取得の開始間隔を確認し、同時取得を1件に制限します。描画側は取得済みスナップショットだけを参照し、同じ描画周期では全レイアウトが同じスナップショットを共有します。終了時は取得処理を待ってから実プロバイダーを破棄します。WindowsTelemetryProviderの重い値の内部5秒キャッシュはHostでは無効にし、Hostの取得間隔に従います。GPUカウンターの構成再検出は引き続き別管理です。

SceneRendererは取得時刻と表示時刻を別々に受け取り、グラフ履歴には取得時刻、時計・カレンダー・カード切替には表示時刻を使います。Hostの1秒周期のPreviewAsyncは送信対象の外部情報も更新判定し、USB送信間隔が長い場合やトレイ格納中も取得を進めます。USB送信が完了するまで描画周期が待つ場合はあります。物理画面の更新はUSB送信時にのみ発生します。

設定先はHostの送信・ハードウェア間隔、またはkind:/id:で指定した要素の公開数値プロパティです。数値以外のプロパティや任意コードの実行は許可しません。Host UIにはテンプレート番号・種類別の入力欄分岐はありません。XMLに定義がない場合の互換補完のみCore.HostParameters.Inferが行います。定義あり・明示的な空・旧形式の未定義を区別します。
