using ChatRelay;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static partial class Program
{
    private const string FirstChannel = "111111111111111111", SecondChannel = "222222222222222222";
    private const string BotId = "333333333333333333";
    private const string Portrait = "https://img2.finalfantasyxiv.com/f/test.jpg";
    private static string Card(string name, string world, string url) =>
        "<a href=\"/lodestone/character/123/\" class=\"entry__link\"><div class=\"entry__chara__face\"><img src=\"" + url +
        "\"></div><p class=\"entry__name\">" + name + "</p><p class=\"entry__world\"><i></i>" + world + " [DC]</p></a>";

    private sealed class BotHandler : HttpMessageHandler
    {
        internal int Reads, Creates;
        internal readonly List<string> Destinations = new List<string>();
        internal readonly List<string> Payloads = new List<string>();
        internal bool BadAuth;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri.AbsolutePath;
            string body;
            bool execute = path.StartsWith("/api/v10/webhooks/");
            BadAuth |= execute ? request.Headers.Authorization != null : request.Headers.Authorization?.Scheme != "Bot";
            if (path.EndsWith("users/@me")) { Reads++; body = "{\"id\":\"" + BotId + "\"}"; }
            else if (execute)
            {
                Destinations.Add(path.Split('/')[4]);
                Payloads.Add(await request.Content.ReadAsStringAsync()); body = "{}";
            }
            else
            {
                var id = path.Split('/')[4];
                var hook = new { id, token = "abcdefghijklmnopqrstuvwxyz123456", channel_id = id, name = "FFXIV Chat Relay", type = 1, user = new { id = BotId } };
                var json = new JavaScriptSerializer();
                if (request.Method == HttpMethod.Get) { Reads++; body = id == FirstChannel ? "[]" : json.Serialize(new[]{hook}); }
                else { Creates++; body = json.Serialize(hook); }
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
    private sealed class PortraitHandler : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Card("Sample Hero","Bahamut",Portrait)) });
        }
    }
    private static async Task BotRelayTests()
    {
        var players = new PlayerDirectory();
        players.Observe("[12:34:56.789] ChangePrimaryPlayer 02:10000001:Local Hero");
        players.Observe("[12:34:56.789] AddCombatant 03:10000001:Local Hero:01:64:0:01:Ifrit:");
        players.Observe("03|time|10000002|Sample Hero|01|64|0|02|Bahamut|0");
        var emote = new ChatMessage { Channel=29, Sender="", Text="Sample Heroは手を振った。" };
        var actor = players.Resolve(emote,null);
        Check(actor.Display=="Sample Hero@Bahamut","emote actor from text and combatant world");
        Check(players.Resolve(new ChatMessage{Channel=12,Sender="Sample Hero"},"Bahamut").Display=="Local Hero@Ifrit","outgoing tell uses self");
        Check(new PlayerDirectory().Resolve(new ChatMessage{Channel=12,Sender="Other Person"},"Ifrit","Local Hero").Name=="Local Hero","ACT identity after hot reload");
        Check(players.Resolve(new ChatMessage{Channel=29,Text="You wave to Sample Hero."},"Bahamut").Display=="Local Hero@Ifrit","self emote");
        Check(players.Resolve(new ChatMessage{Channel=29,Text="Sample Heroに手を振った。"},"Bahamut")==null,"target is not treated as the actor");
        Check(players.Resolve(new ChatMessage{Channel=14,Sender="Sample Hero@Ifrit"},"Bahamut").World=="Ifrit","explicit world beats cache");
        Check(players.Resolve(new ChatMessage{Channel=14,Sender="Sample HeroBahamut"},null).World=="Bahamut","merged world");
        players.Observe("03|time|10000003|Sample Hero|01|64|0|03|Ifrit|0");
        Check(string.IsNullOrEmpty(players.Resolve(emote,"Bahamut").World),"ambiguous player suppressed");
        Check(players.Resolve(new ChatMessage{Channel=57,Sender="Sample Hero"},null)==null,"system has no portrait");
        var cards = Card("Sample Hero","Ifrit","https://img2.finalfantasyxiv.com/wrong") +
            Card("Sample Heroine","Bahamut","https://img2.finalfantasyxiv.com/wrong") + Card("Sample Hero","Bahamut",Portrait);
        Check(PortraitLookup.Parse(cards,actor)==Portrait,"exact name and world matching");
        Check(PortraitLookup.Parse(Card("Sample Hero","Bahamut","https://evil.test/img"),actor)==null,"non Lodestone image rejected");
        var portraits = new PortraitHandler(); var handler = new BotHandler();
        var plugin = new ChatRelayPlugin();
        var members = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var config = new PluginSettings { Language="en", Enabled=true, BotMode=true, HomeWorld="Bahamut",
            DefaultChannelId=FirstChannel, Channels=new[]{14,29,57}, Routes=new[]{new ChannelRoute { Kind=57, ChannelId=SecondChannel }} };
        var pluginType = plugin.GetType();
        pluginType.GetField("settings",members).SetValue(plugin,config);
        pluginType.GetMethod("Configure",members).Invoke(plugin,null);
        var onLog = pluginType.GetMethod("LogLineRead",members);
        Action<string,bool> log = (line, imported) => onLog.Invoke(plugin,new object[]{imported,
            new Advanced_Combat_Tracker.LogLineEventArgs(line,0,DateTime.UtcNow,"",false)});
        using(var sender = new RelaySender(null,handler,"test.bot.token",portraits))
        {
            pluginType.GetField("sender",members).SetValue(plugin,sender);
            log("00|time|000E|Sample Hero|imported|hash",true);
            log("00|time|000A|Sample Hero|ignored say|hash",false);
            log("00|time|000E|Sample Hero|party|hash",false);
            log("00|time|001D||Sample Heroは手を振った。|hash",false);
            log("00|time|0039||system|hash",false);
            for(int i=0;i<180 && sender.Sent<3 && sender.Dropped==0;i++) await Task.Delay(50);
            Check(sender.Sent==3 && sender.Dropped==0,"all three posts delivered");
            Check(handler.Destinations.SequenceEqual(new[]{FirstChannel,FirstChannel,SecondChannel}),"shared and distinct destinations");
            Check(handler.Reads==3 && handler.Creates==1 && portraits.Reads==1,"one identity read, two listings, one create, one portrait lookup");
            Check(!handler.Payloads.Any(x => x.Contains("imported") || x.Contains("ignored say")),"imports and unselected chat not delivered");
            Check(!handler.BadAuth,"bot auth limited to management");
            var first = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(handler.Payloads[0]);
            Check((string)first["username"]==actor.Display && (string)first["avatar_url"]==Portrait,"player posts with portrait");
            Check(!handler.Payloads[2].Contains("avatar_url") && !handler.Payloads[2].Contains("username"),"system uses default webhook identity");
        }
        ((IDisposable)pluginType.GetField("updates",members).GetValue(plugin)).Dispose();
        var s = new PluginSettings { BotMode=true, DefaultChannelId=FirstChannel, Routes=new[]{new ChannelRoute{Kind=14,ChannelId=SecondChannel}} };
        s.SetBotToken("sample.test.value"); var contents=SettingsPersistence.Serialize(s);
        Check(!contents.Contains("sample.test.value") && s.ReadBotToken()=="sample.test.value","encrypted bot secret");
        Check(BotWebhooks.IsChannelId(FirstChannel) && !BotWebhooks.IsChannelId("https://discord.com/"),"channel ID validation");
    }
}
