# Release notes

## v1.1.1

- Save changed settings at a 60-second interval and flush pending changes on normal plugin shutdown.
- Preserve the existing settings file if a save fails, and avoid rewriting unchanged settings.
- Wait for Discord sender and update tasks to finish when the plugin is disabled or reloaded.
- Add regression tests for save scheduling, recovery after failed saves, and worker shutdown.

## v1.1.0

- HojoringのBeacon・変数変更・combatantクリアなどの内部Echoを転送対象から除外。通常のゲーム内Echoは維持。
- システムメッセージ、システムエラー、エラーメッセージを追加（更新時はOFF）。
- 「チャット種別」専用タブに、グループ別カード・選択数・選択状態の背景色を追加。
- 全種別およびグループ単位の一括ON/OFFを追加。既存設定は引き継ぎ。
- 4言語、狭いペイン、DPI拡大、一括操作、除外ログの回帰テストを追加。

## v1.0.0

- Initial release.
