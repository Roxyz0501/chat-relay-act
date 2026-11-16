namespace ChatRelay
{
    public static partial class Localization
    {
        private static void AddBotResources()
        {
            foreach (var pair in Table("Bot=Bot|BotMode=Use Discord bot|BotToken=Bot token|CommonChannel=Default channel ID|HomeWorld=Fallback home world|Faces=Use player names and Lodestone portraits|RoutingHint=Set optional channel IDs below. Blank rows use the default. Enable chat types in the Channels tab.|OverrideChannel=Override channel ID|ApplyBot=Apply|TestRoute=Test selected route|Applied=Settings applied.|InvalidChannel=Enter a valid Discord channel ID.|InvalidWorld=Enter the English world name.|BotSetup=Check bot token, channel ID and Manage Webhooks permission.|Channel28=Custom emote|Channel29=Standard emote|Hint=Select chat types and enable forwarding. Set up a webhook here or configure a bot in the Bot tab.")) R[English][pair.Key] = pair.Value;
            foreach (var pair in Table("Bot=Bot連携|BotMode=Discord Botを使う|BotToken=Botトークン|CommonChannel=共通チャンネルID|HomeWorld=補完用ホームワールド|Faces=プレイヤー名とLodestoneの顔アイコンで投稿|RoutingHint=個別の転送先を空欄にすると共通チャンネルを使います。転送する種別は「チャット種別」で選択してください。|OverrideChannel=個別のチャンネルID|ApplyBot=設定を反映|TestRoute=選択行にテスト送信|Applied=設定を反映しました。|InvalidChannel=DiscordのチャンネルIDを確認してください。|InvalidWorld=ワールド名は英字で入力してください。|BotSetup=Botトークン、転送先ID、Webhookの管理権限を確認してください。|Channel28=カスタムエモート|Channel29=定型エモート|Hint=Webhookか「Bot連携」で転送先を設定し、「チャット種別」で対象を選んで転送を有効にしてください。")) R[Japanese][pair.Key] = pair.Value;
            foreach (var pair in Table("Bot=Bot|BotMode=使用 Discord 机器人|BotToken=机器人令牌|CommonChannel=默认频道 ID|HomeWorld=备用所属服务器|Faces=使用玩家姓名和 Lodestone 头像|RoutingHint=下表留空时使用默认频道。请在聊天频道选项卡中启用聊天类型。|OverrideChannel=指定频道 ID|ApplyBot=应用设置|TestRoute=测试所选频道|Applied=设置已应用。|InvalidChannel=请输入有效的 Discord 频道 ID。|InvalidWorld=请输入英文服务器名称。|BotSetup=请检查机器人令牌、频道 ID 和管理 Webhook 权限。|Channel28=自定义情感动作|Channel29=标准情感动作|Hint=在此设置 Webhook 或在 Bot 选项卡设置机器人，选择聊天频道并启用转发。")) R[Chinese][pair.Key] = pair.Value;
            foreach (var pair in Table("Bot=봇 연동|BotMode=Discord 봇 사용|BotToken=봇 토큰|CommonChannel=기본 채널 ID|HomeWorld=기본 출신 서버|Faces=플레이어 이름과 Lodestone 얼굴로 게시|RoutingHint=아래 칸을 비우면 기본 채널을 사용합니다. 채팅 채널 탭에서 전송할 종류를 선택하세요.|OverrideChannel=개별 채널 ID|ApplyBot=설정 적용|TestRoute=선택한 행 테스트|Applied=설정을 적용했습니다.|InvalidChannel=Discord 채널 ID를 확인하세요.|InvalidWorld=영문 서버 이름을 입력하세요.|BotSetup=봇 토큰, 채널 ID, 웹후크 관리 권한을 확인하세요.|Channel28=사용자 감정 표현|Channel29=일반 감정 표현|Hint=여기에서 웹후크 또는 봇 연동 탭에서 봇을 설정하고 채팅 종류를 선택한 후 전송을 켜세요.")) R[Korean][pair.Key] = pair.Value;
        }
    }
}
