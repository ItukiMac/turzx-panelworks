# Standalone Display

Windows上でダッシュボード画面を編集・表示し、対応するRev A USBディスプレイへ明示的に1フレーム送信するアプリケーションです。

## 設計

- `StandaloneDisplay.Editor.exe`: シーンの編集、プレビュー、保存だけを担当します。USB機器へアクセスしません。
- `StandaloneDisplay.Host.exe`: 保存済みシーンの表示、機器列挙、選択した1台以上のモニタへの送信を担当します。
- `Core`: シーン、数式、テレメトリ契約。
- `Telemetry.Demo` / `Telemetry.Windows`: デモ値とWindows実測値の取得。
- `Rendering`: 480×320フレームの描画。背景画像は任意で、未指定時はコード生成の無地背景を使います。
- `Transport.Abstractions`: 機器記述、探索、接続セッション、複数送信の契約。
- `Transport.Usb`: Rev A機器の探索、排他シリアル接続、RGB565エンコード。

依存方向と安全境界は [`docs/architecture.md`](docs/architecture.md) を参照してください。

## 必要環境

- Windows
- .NET Framework 4.8
- PowerShell 5.1以降

NuGetパッケージや配布バイナリ、専用ドライバー、背景画像をリポジトリへ同梱しません。

## ビルドと検証

```powershell
.\Build.ps1
.\Verify.ps1
```

`Verify.ps1` は数式、シーン保存、背景なし描画、USBフレーム形式、複数ターゲットのルーティングをオフラインで検証します。USB機器は開きません。

## 使用方法

```powershell
.\bin\StandaloneDisplay.Editor.exe [scene.xml] [optional-background.png]
.\bin\StandaloneDisplay.Host.exe [scene.xml] [optional-background.png]
```

Hostの `Scan` で機器を列挙し、送信対象を明示的にチェックして `Send` を押します。複数台を選択した場合は機器ごとに独立した排他セッションを開き、各機器へ1回ずつ送信します。ある機器の失敗を別機器への暗黙の再試行や代替送信には使いません。

## 安全上の制約

- ファームウェア更新、機器リセット、ドライバー導入は実装しません。
- 自動接続、自動再試行、連続送信は行いません。
- 対象は列挙時の安定IDで管理し、接続直前に再照合します。
- 実機利用では、送信完了と画面の目視確認を別々に扱ってください。

## コントリビューションとセキュリティ

変更方法は [`CONTRIBUTING.md`](CONTRIBUTING.md)、脆弱性報告は [`SECURITY.md`](SECURITY.md) を参照してください。ログ、実機の個体識別子、ユーザー名、端末名、絶対パス、秘密情報をIssueやコミットへ含めないでください。

## ライセンス

現時点ではライセンスを付与していません。第三者への公開・配布前に、コードと仕様出典の権利確認を行い、適切なライセンスを選定してください。
