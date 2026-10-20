# Architecture

## Dependencies

```text
Core <- Rendering <- Editor.WinForms
Core <- Telemetry.Demo / Telemetry.Windows
Rendering + Core + Transport.Abstractions <- Runtime
Transport.Abstractions + Rendering <- Transport.Usb
Runtime + Telemetry.Windows + Transport.Usb <- DisplayHost.WinForms
```

EditorはTransport/Runtimeを参照しません。Renderingには選択枠やマウス操作を含めず、編集時の装飾はEditorのSelectionAdornerが担います。

## Update ownership

Hostは対象選択とスケジュールを担います。RuntimeのDisplayUpdateCoordinatorが同時実行を1周期に限定し、忙しい場合は要求を蓄積せず破棄します。Timerは周期完了後に設定した間隔で再開します。

DisplayTargetは機器、シーンXML、背景を対応付けます。1周期で同じXML＋背景はFrameGroupとしてまとめます。グループごとにSceneFrameSourceがXMLキャッシュ、SceneRendererがグラフ履歴と描画を所有します。グループ間ではSceneFrameSourceFactoryの1つのTelemetrySnapshotを共有します。

グループ単位でRGB565を1回生成し、対象ごとの最後に成功した画像と比較します。同じpayloadを共有していた複数対象は比較も共有します。接続はStableIdごとに再利用し、最大4件の機器送信を並行実行します。機器の失敗は保持され、周期ごとの自動再試行はありません。

停止は進行中の周期を待って接続を解放します。終了は描画・送信が完了してからシーン、テレメトリを解放します。UIがTakePreviewしたBitmapだけはUIが所有し、差替え/終了時に破棄します。それ以外の周期内BitmapはRuntimeが破棄します。

## Transport responsibilities

- UsbDisplayDiscovery: 対応機器の列挙。
- UsbDisplaySessionFactory: 選択したIDと現在のポートを再照合して接続。
- UsbDisplaySession: 排他接続、HELLO、期限付きの書込み/排出。
- RevAFrameEncoder: 方向・矩形ヘッダーとRGB565 payloadのフレーム化。
- Rgb565Encoder: Bitmapの一括読出しと画素形式変換。

## Verification

通常のVerifyでは偽機器と固定画像を使い、異なる内容の振り分け、グループ共有、同一画面省略、負stride変換、機器障害の隔離、停止/終了時の寿命を検証します。UI smokeではフォームの構築と描画だけを行い、ライブ計測・USB列挙を開始しません。

## Host rotation

DisplayTarget / DisplayAssignment carry a validated clockwise RotationDegrees (0/90/180/270, default 0). FrameRotation rotates the full image and fits it into the original frame dimensions with black padding. The coordinator captures each scene/background once per cycle and caches transformed frames by rotation. Preview and RGB565 output use the same transformed image. The USB protocol remains unchanged.
