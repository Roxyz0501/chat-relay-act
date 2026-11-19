# Chat Relay for ACT

FFXIVのチャットとエモートをDiscordへ転送するACTプラグインです。Discord Botと連携すると、種別ごとの転送とプレイヤーの顔アイコンを使えます。
English / 日本語 / 简体中文 / 한국어。Windows / .NET Framework 4.8。

## 導入

1. [Releases](https://github.com/Roxyz0501/chat-relay-act/releases)から `ChatRelay-vX.Y.Z.zip` と同版の `.sha256` を取得します。
2. SHA-256を確認し、ZIPのブロックを解除して専用フォルダーへ展開します。
3. ACTのPluginsで `ChatRelay.dll` を追加します。FFXIV ACT Pluginも有効にしてください。
4. 単一Webhookで使う場合は、Discordの送信先テキストチャンネルでWebhookを作成し、そのURLをChat Relayに貼り付けて「Webhookを保存」を押します。
5. 「テストメッセージを送信」で送信先を確認し、「チャット種別」タブで対象を選択して、設定タブの「チャット転送を有効にする」をONにします。

### Bot連携と顔アイコン

1. Discord Developer PortalでアプリのBotを作成します。InstallationでGuild Installの `bot` スコープを選び、サーバーに追加します。送信先の通常テキストチャンネルでBotに「チャンネルを見る」「ウェブフックの管理」を許可します。BotトークンはACTの伏字入力欄に入力します。
2. ACTの「Bot連携」で「Discord Botを使う」を選び、Botトークンと共通チャンネルIDを入力します。IDはDiscordの開発者モードを有効にして、チャンネルの右クリックメニューからコピーできます。
3. たとえば共通先を雑談用にして、Partyと定型エモートだけ別のIDにすると、その2種別だけ別の部屋に届きます。同じIDを複数行に指定できます。共通先を空欄にする場合は、転送する全種別に個別IDを設定してください。
4. 「設定を反映」を押し、一覧の行を選んで「選択行にテスト送信」で届く場所を確認します。「チャット種別」で対象を選び、「設定」で転送を有効にします。
5. 「発言者名を含める」と「プレイヤー名とLodestoneの顔アイコンで投稿」を有効にすると、Botが管理するWebhookが発言者名と顔で投稿します。ワールドはログ上の明示表記やプレイヤー情報から取得します。情報が欠ける場合は「補完用ホームワールド」に英語名（例 `Bahamut`）を入力できます。顔画像が見つからない場合は標準アイコンで本文を届けます。

Botはチャンネルごとに専用Webhookを作成・再利用します。Botトークンは現WindowsユーザーのDPAPIで暗号化して保存します。起動直後の表示や設定変更ではDiscord APIを呼び出しません。Lodestoneはワールドとキャラクター名が完全一致した検索カードの画像を使います。画像本体はDiscordが取得します。プラグインの検索キャッシュは最大512人で、成功24時間・該当なし15分です。設定変更・終了時にメモリキャッシュを破棄します。ACTログが発言者を特定できない場合はWebhook既定名で投稿します。

参考にしたXIVLauncher向けプラグインの機能は[DiscordChatWebhook](https://github.com/Valiice/DiscordChatWebhook)を参照してください。投稿名とアイコンの仕様は[Discord公式Webhook API](https://docs.discord.com/developers/resources/webhook)にあります。

Webhookは通常のテキストチャンネル用です。フォーラムやスレッド用の追加パラメーターには対応していません。
初期状態は転送OFF・種別未選択です。テスト送信は転送OFFでも固定のテスト文を送ります。「設定」のテストは共通先、「Bot連携」のテストは選択行の転送先を使います。
Webhook URLをチャットやGitHubへ貼り付ける必要はありません。

## 転送対象と動作

Say / Shout / Yell、カスタムエモート・定型エモート、Tell送信・受信、Party、Alliance、クロスワールドParty、FC、LS 1～8、CWLS 1～8、ビギナーチャンネル、PvPチーム、Echo、システムメッセージ（0039）、システムエラー（003A）、エラーメッセージ（003C）を個別に選択できます。
種別はグループ別カードで表示し、選択中は背景色とチェックで区別します。全体の「すべてON／OFF」とグループごとの「一括ON／OFF」を用意しています。一括操作も設定保存・反映は1回だけ行います。転送自体の有効／無効は変更しません。
既存の種別選択は引き継ぎ、追加したシステム種別は未選択で開始します。
戦闘ログ・インポートした過去ログは送信しません。
Echoおよびシステム種別では、本文または名前欄が `Hojoring>` で始まる内部ログを除外します。通常のゲーム内 `/echo` は転送対象です。これは既知の診断ログ接頭辞による除外であり、任意の外部プラグインがゲームと同じ形式で注入するログの出所を完全に識別するものではありません。
ACTが新しく読み取ったチャットだけを処理するため、ゲームやFFXIV ACT Pluginの動作状態によって取得できない場合があります。
Tell送信時の名前欄は、FFXIVのログ上の相手名になる場合があります。

- 単一Webhookモードでは全種別を同じ転送先へ、Botモードでは種別の設定先へ転送します。Discordからゲームへの返信機能はありません。
- 発言者名の表示をOFFにできます。チャット本文は転送されるため、転送先の閲覧者を確認してください。
- `allowed_mentions.parse` を空にし、メンション通知を抑止します。Markdownはエスケープし、リンクの埋め込みを抑止します。
- 長文はUnicodeサロゲートペアを保ちながら分割します。
- ACTのログ処理をブロックせず、順序を保持して送信します。キューは最大200件、通常は約2.1秒以上の送信間隔です。
- Discordの429では `retry_after` を尊重し、最大4回まで試行します。300秒を超える場合は待機後にそのメッセージを破棄します（待機上限24時間）。
- 通信エラーやその他のHTTPエラーは重複投稿防止のため自動再送しません。破棄・失敗数を設定画面に表示します。
- 設定変更、転送OFF、プラグイン停止では待機中の送信を取り消します。既にDiscordに受理された投稿は取り消せません。
- 設定変更で送信カウンターはリセットされます。チャット本文や送信キューをディスクへ保存しません。

WebhookはWindows DPAPI（現在のWindowsユーザー）で暗号化してACTのConfigへ保存し、入力欄も伏字にします。他のWindowsユーザーへConfigをコピーした場合はURLを再設定してください。
許可する送信先はDiscord公式のHTTPS Webhook URLのみです。リダイレクトは追跡しません。

## 自動言語・支援

初回はOSのUI言語から選択し、以後は設定画面のユーザー選択を保存します。翻訳がないキーは英語へフォールバックします。
支援は完全に任意です。支援しなくても全機能が使え、機能差はありません。
[Ko-fiでRoxyz0501の開発を支援](https://ko-fi.com/roxyz0501)。支援タブのボタンを押したときだけブラウザを開きます。

## 安全な更新

専用の「更新」タブで現在版、最新版、状態、更新内容を確認できます。起動時確認は既定ONで無効化可能です。「今すぐ確認」「更新する」「後で」を用意しています。更新タブには作者、現在版、GitHubリポジトリへのリンクも表示します。
公開GitHub Releasesの安定版のみをSemVerで比較します。GitHubトークンは不要です。通常機能は更新確認失敗時も継続します。

利用者が更新を選んだ場合だけダウンロードします。HTTPS・GitHubホスト・リリースパス・asset名・SHA-256・ZIP内容・DLLとUpdaterのアセンブリ名／会社名／バージョンを検証します。ZIPのディレクトリ逸脱と想定外ファイルを拒否し、展開サイズは1ファイル50 MiB、合計60 MiB以下に制限します。検証済みパッケージはユーザーのローカルデータ領域に保管し、同じ版は再利用します。
更新先はACTのプラグイン登録情報から取得し、取得不能時は更新を中止します。shadow copyの場所へ上書きしません。
検証済みDLLとUpdaterを準備し、ACT終了後にUpdaterが登録先DLLを自動置換します。更新前に現行DLLのバックアップを作成し、置換失敗時は復元します。ACT終了後にUpdaterは終了します。適用後にACTを再起動してください。

## 開発・Release

```powershell
./build.ps1 -ActPath 'path/to/Advanced Combat Tracker.exe'
```

ビルド、偽HTTPハンドラーを使う送信テスト、4言語UI、Config、暗号化、ログ解析、更新パッケージの検証・キャッシュ・失敗時後片付けテスト、ZIP検証を実行します。Discordへの実投稿はテストしません。
`artifacts/tests` に4言語・各タブの描画結果を生成します。ACT実機・ゲーム接続の検証は別途必要です。

任意の実Release統合テスト:

```powershell
$env:CHATRELAY_UPDATE_INTEGRATION = '1'
./tests/ChatRelay.Tests/bin/Release/net48/ChatRelay.Tests.exe
```

プラグインとUpdaterのcsprojのVersionを揃えて更新し、ビルド成功後に `vX.Y.Z` タグをpushしてください。GitHub ActionsがACT公式配布をビルド依存として取得し、ビルド・テスト・配布物検証を行います。成功時のみ安定版Releaseを公開します。外部ActionはコミットSHA固定です。

必須assets: `ChatRelay-vX.Y.Z.zip`、`ChatRelay-vX.Y.Z.sha256`。
マニフェストは `64桁SHA-256  ChatRelay-vX.Y.Z.zip` の形式です。
ZIP直下には `ChatRelay.dll`、`ChatRelay.Updater.exe`、`README.md` の3ファイルだけを含めます。ACT本体・ログ・Config・資格情報は含めません。

仕様参照: [Discord Webhook](https://discord.com/developers/docs/resources/webhook)、[Discord rate limits](https://discord.com/developers/docs/topics/rate-limits)、[cactbot LogGuide](https://github.com/OverlayPlugin/cactbot/blob/main/docs/LogGuide.md)、[Dalamud chat type definitions](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Text/XivChatType.cs)。

ライセンスは未指定です。
