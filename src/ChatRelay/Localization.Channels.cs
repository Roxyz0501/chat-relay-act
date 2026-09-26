namespace ChatRelay
{
    public static partial class Localization
    {
        private static void AddChannelResources()
        {
            foreach(var pair in Table("Channels=Chat channels|AllOn=Select all|AllOff=Clear all|GroupOn=All on|GroupOff=All off|SelectedCount={0} / {1} selected|GroupNearby=Nearby chat|GroupParty=Tell / party|GroupCommunity=Community|GroupLS=Linkshells|GroupCWLS=Cross-world linkshells|GroupSystem=Echo / system|Channel57=System messages|Channel58=System errors|Channel60=Error messages|ChannelHint=Choose which channels to forward. Changes apply immediately. Hojoring diagnostic messages are excluded.|Hint=Save the webhook, select channels in the Channels tab, then enable forwarding. Tests use the saved webhook. Imported logs are ignored; changing settings discards pending messages.")) R[English][pair.Key]=pair.Value;
            foreach(var pair in Table("Channels=チャット種別|AllOn=すべてON|AllOff=すべてOFF|GroupOn=一括ON|GroupOff=一括OFF|SelectedCount={1}種別中 {0}種別を選択|GroupNearby=周囲のチャット|GroupParty=Tell・パーティ|GroupCommunity=コミュニティ|GroupLS=リンクシェル|GroupCWLS=クロスワールドリンクシェル|GroupSystem=Echo・システム|Channel57=システムメッセージ|Channel58=システムエラー|Channel60=エラーメッセージ|ChannelHint=転送する種別を選択します。変更は即時反映されます。Hojoringの内部ログは除外します。|Hint=Webhookを保存し、「チャット種別」タブで対象を選んで転送を有効にしてください。テストは保存済みURLを使用します。インポートログは対象外で、設定変更時は送信待ちを破棄します。")) R[Japanese][pair.Key]=pair.Value;
            foreach(var pair in Table("Channels=聊天频道|AllOn=全部开启|AllOff=全部关闭|GroupOn=本组全开|GroupOff=本组全关|SelectedCount=已选择 {0} / {1} 个频道|GroupNearby=附近聊天|GroupParty=私聊与小队|GroupCommunity=社区|GroupLS=通讯贝|GroupCWLS=跨服通讯贝|GroupSystem=回声与系统|Channel57=系统消息|Channel58=系统错误|Channel60=错误消息|ChannelHint=选择要转发的频道，更改立即生效。自动排除Hojoring内部日志。|Hint=保存Webhook，在聊天频道选项卡中选择频道，然后启用转发。测试使用已保存的URL。不转发导入日志，更改设置时丢弃待发消息。")) R[Chinese][pair.Key]=pair.Value;
            foreach(var pair in Table("Channels=채팅 채널|AllOn=모두 켜기|AllOff=모두 끄기|GroupOn=그룹 켜기|GroupOff=그룹 끄기|SelectedCount={0} / {1}개 선택|GroupNearby=주변 채팅|GroupParty=귓속말 / 파티|GroupCommunity=커뮤니티|GroupLS=링크셸|GroupCWLS=서버 초월 링크셸|GroupSystem=혼잣말 / 시스템|Channel57=시스템 메시지|Channel58=시스템 오류|Channel60=오류 메시지|ChannelHint=전송할 채널을 선택하세요. 변경 사항은 즉시 적용되며 Hojoring 내부 로그는 제외됩니다.|Hint=Webhook을 저장하고 채팅 채널 탭에서 대상을 선택한 다음 전송을 켜세요. 테스트는 저장된 URL을 사용합니다. 가져온 로그는 제외하며 설정 변경 시 대기 메시지를 버립니다.")) R[Korean][pair.Key]=pair.Value;
        }
    }
}
