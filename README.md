# TURZX PanelWorks

TURZX製Rev A・480×320 USBディスプレイ向けの非公式Windowsアプリです。Editorでダッシュボードを編集し、Hostで機器ごとの画面と継続表示を管理します。メーカーによる公式提供、公認、提携を示すものではありません。

## はじめに

Windows、.NET Framework 4.8、PowerShell 5.1以降で、リポジトリのルートから実行します。

```powershell
.\build\Verify.ps1
.\bin\TurzxPanelWorks.Editor.exe
.\bin\TurzxPanelWorks.Host.exe
```

## フォルダー構成

| 場所 | 内容 |
| --- | --- |
| `src/` | アプリ本体とライブラリ。各アプリのアイコンは `Resources/App.ico` |
| `tests/` | オフライン試験・WinFormsスモーク試験 |
| `build/` | ビルド・検証スクリプト |
| `docs/` | 利用方法、設計、開発規約、第三者帰属 |
| `bin/` | 生成物（Git管理外）。依存DLLは `lib/` |

## ドキュメント

- [利用ガイド](docs/user-guide.md)
- [アーキテクチャ](docs/architecture.md)
- [開発・検証と安全性](docs/development.md)
- [第三者の著作権・ライセンス表示](docs/THIRD_PARTY_NOTICES.md)

GPL-3.0-or-laterで提供します。ライセンス全文は [LICENSE](LICENSE) を参照してください。
