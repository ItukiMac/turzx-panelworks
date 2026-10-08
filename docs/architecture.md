# Architecture

## Dependency direction

```text
Core <- Rendering <- Editor
Core <- Rendering <- DisplayHost
Core <- Telemetry.Demo/Telemetry.Windows <- Editor/DisplayHost
Transport.Abstractions <- Transport.Usb <- DisplayHost
```

EditorはTransportを参照しません。DisplayHostはシーンを読み取り、描画したフレームを明示的に選択された機器へ渡します。

## Responsibilities

- Core: データモデル、シーン永続化、数式評価、テレメトリ。
- Rendering: シーンからBitmapへの変換。
- Transport.Abstractions: 機器探索・接続・送信の境界。
- Transport.Usb: USBシリアル探索、Rev Aプロトコル、RGB565変換。
- Editor: 編集操作と編集プレビュー。
- DisplayHost: 表示プレビュー、対象選択、複数機器への送信調整。

各USB機器は安定ID、現在のポート、寸法を持つ記述子として扱います。接続時には安定IDを再照合し、選択されていない機器へ送信しません。
