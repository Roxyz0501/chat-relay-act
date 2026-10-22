using ChatRelay;
using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static partial class Program
{
    private static int assertions;
    private static string artifacts;
    private const string Endpoint = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789";
    [STAThread]
    private static int Main()
    {
        try
        {
            artifacts = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests"); Directory.CreateDirectory(artifacts);
            Parsing(); DiagnosticFiltering(); Persistence(); SettingsSaveScheduling(); Languages(); UpdateSafety(); Sending().GetAwaiter().GetResult(); ChannelSelection(); RenderSettings();
            if (Environment.GetEnvironmentVariable("CHATRELAY_UPDATE_INTEGRATION") == "1")
            using (var service = new UpdateService())
            {
                var result = service.CheckAsync(new Version(0,0,0), CancellationToken.None).GetAwaiter().GetResult();
                Check(result.Status == UpdateCheckStatus.UpdateAvailable, "live release found");
                var package = service.DownloadAndVerifyAsync(result.Release,CancellationToken.None).GetAwaiter().GetResult();
                Check(File.Exists(package.StagedDllPath) && File.Exists(package.StagedUpdaterPath),"live package verified");
            }
            Console.WriteLine("PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Parsing()
    {
        ChatMessage message;
        foreach (var channel in ChatParser.Channels)
        {
            Check(ChatParser.TryParse("00|2026-09-27T00:00:00+09:00|"+channel.ToString("X4")+"|Player|こんにちは|hash",out message) && message.Channel == channel && message.Text == "こんにちは", "channel "+channel);
            Check(ChatParser.TryParse("[12:34:56.789] ChatLog 00:"+channel.ToString("X4")+":Player:hello:world",out message) && message.Text == "hello:world", "parsed channel "+channel);
        }
        Check(ChatParser.TryParse("00|time|0818|Player|a|b|hash",out message) && message.Channel == 24 && message.Text == "a|b", "flags and delimiters");
        Check(ChatParser.TryParse("00|time|0839||System message|hash",out message) && message.Channel==57,"system supported with flags");
        Check(!ChatParser.TryParse("21|time|0018|Player|attack|hash",out message),"combat excluded");
        Check(!ChatParser.TryParse("00|time|ZZZZ|Player|hello|hash",out message),"invalid code");
        Check(!ChatParser.TryParse("00|time|0018",out message),"truncated log");
        var original = new string('a',1899)+"😀"+new string('b',2300);
        var parts = ChatParser.Split(original).ToArray(); Check(string.Concat(parts)==original && parts.All(x=>x.Length<=1900 && !char.IsHighSurrogate(x[x.Length-1])),"Unicode splitting");
        message = new ChatMessage { Channel=24, Sender="Private Name",Text="**bold** @everyone" };
        Check(!ChatParser.Format(message,"en",false).Contains("Private Name"),"name hiding");
        Check(ChatParser.Format(message,"en",true).Contains("\\*\\*"),"markdown escaped");
    }
    private static void Persistence()
    {
        var settings = new PluginSettings(); Check(!settings.Enabled && settings.Channels.Length==0 && settings.CheckUpdatesOnStartup,"safe defaults");
        Check(settings.InitializeLanguageIfMissing(new CultureInfo("ja-JP")) && settings.Language=="ja","first OS language");
        Check(!settings.InitializeLanguageIfMissing(new CultureInfo("ko-KR")) && settings.Language=="ja","preserve user language");
        settings.SetWebhook(Endpoint); Check(settings.ReadWebhook()==Endpoint && !settings.ProtectedWebhook.Contains("discord"),"encrypted webhook");
        var path=Path.Combine(artifacts,"config.xml"); settings.Save(path); Check(!File.ReadAllText(path).Contains(Endpoint),"no plaintext secret in config");
        Check(PluginSettings.Load(path).ReadWebhook()==Endpoint,"DPAPI roundtrip");
        settings.ProtectedWebhook="broken"; Check(settings.ReadWebhook()=="","corrupt secret safe");
        File.WriteAllText(path,"<PluginSettings><Language>ko</Language></PluginSettings>"); var old=PluginSettings.Load(path); Check(old.Language=="ko" && old.CheckUpdatesOnStartup && old.Channels.Length==0,"old config defaults");
        string error; var clicks=0; Check(SettingsControl.TryOpenSupportLink(info=>{clicks++;Check(info.FileName==SettingsControl.SupportUrl && info.UseShellExecute,"support target");return null;},out error) && clicks==1,"explicit support click");
        Check(!SettingsControl.TryOpenSupportLink(info=>{throw new IOException();},out error),"support failure contained");
    }
    private static void SettingsSaveScheduling()
    {
        var root=Path.Combine(artifacts,"persistence-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"settings.xml");var settings=new PluginSettings{Language="ja"};
        var persistence=new SettingsPersistence(settings,path,null);var now=new DateTime(2026,10,22,0,0,0,DateTimeKind.Utc);var schedule=new SettingsSaveCoordinator(persistence,now);
        Check(!schedule.Poll(now.AddSeconds(59))&&persistence.SuccessfulWrites==0&&!File.Exists(path)&&!File.Exists(path+".tmp"),"no early or unchanged file write");
        settings.Enabled=true;settings.IncludeNames=false;settings.Enabled=false;
        Check(!schedule.Poll(now.AddSeconds(59))&&persistence.SuccessfulWrites==0,"rapid edits wait for interval");
        Check(schedule.Poll(now.AddSeconds(60))&&persistence.SuccessfulWrites==1,"latest state saved at 60 seconds");
        var bytes=File.ReadAllBytes(path);Check(!schedule.Poll(now.AddSeconds(120))&&persistence.SuccessfulWrites==1,"unchanged check performs zero writes");
        settings.IncludeNames=true;settings.IncludeNames=false;Check(!schedule.Flush()&&persistence.SuccessfulWrites==1,"reverted settings skip final write");
        settings.Language="ko";Check(schedule.Flush()&&persistence.SuccessfulWrites==2,"shutdown flush saves latest settings");
        Check(PluginSettings.Load(path).Language=="ko","latest config reloads");

        var original=File.ReadAllBytes(path);var failingStore=new AtomicSettingsFileStore{BeforeCommit=()=>{throw new IOException("simulated commit failure");}};
        settings.Language="zh-CN";var failed=new SettingsPersistence(settings,path,SettingsPersistence.Serialize(PluginSettings.Load(path)),failingStore);
        Throws(()=>failed.SaveIfChanged(),"failed save reported");
        Check(failed.HasChanges&&File.ReadAllBytes(path).SequenceEqual(original)&&!File.Exists(path+".tmp"),"failed save preserves prior file and dirty state");
        failingStore.BeforeCommit=null;Check(failed.SaveIfChanged()&&!failed.HasChanges&&PluginSettings.Load(path).Language=="zh-CN","failed save recovers");
    }
    private static void Languages()
    {
        Check(Localization.MapCulture(new CultureInfo("fr-FR"))=="en","unknown OS fallback");
        Check(Localization.MapCulture(new CultureInfo("zh-TW"))=="zh-CN","Chinese mapping");
        foreach(var lang in new[]{"en","ja","zh-CN","ko"})
        {
            foreach(var key in new[]{"Enabled","IncludeNames","Channels","InvalidWebhook","SupportHeading","Updates","Prepared","Counts","SendFailed"}) Check(Localization.Get(lang,key)!=key,"translation "+lang+key);
            Check(Localization.Get(lang,"FallbackProbe")=="English fallback","missing-key fallback "+lang);
        }
    }
    private static async Task Sending()
    {
        Uri endpoint; Check(RelaySender.TryEndpoint(Endpoint,out endpoint),"valid endpoint");
        foreach(var bad in new[]{Endpoint.Replace("https:","http:"),Endpoint.Replace("discord.com","discord.com.evil.test"),Endpoint+"?thread_id=1",Endpoint+"#secret","https://discord.com/api/webhooks/1/no","https://user@discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyz0123456789"}) Check(!RelaySender.TryEndpoint(bad,out endpoint),"reject unsafe endpoint");
        RelaySender.TryEndpoint(Endpoint,out endpoint);
        var json = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(RelaySender.Payload("@everyone <@123>"));
        var allowed=(Dictionary<string,object>)json["allowed_mentions"]; Check(((System.Collections.IEnumerable)allowed["parse"]).Cast<object>().Count()==0,"mentions disabled");
        Check(RelaySender.RetryDelay("{\"retry_after\":2.5}")==2.5,"fractional retry delay");
        var fake=new FakeHandler();
        using(var sender=new RelaySender(endpoint,fake))
        {
            sender.Enqueue("test"); await Until(()=>sender.Sent==1); Check(fake.Calls==2 && fake.Body.Contains("allowed_mentions"),"429 then success");
            Check(fake.RequestUri.Query=="?wait=true","wait for confirmed delivery");
        }
        var blocked=new FakeHandler { Block=true };
        var canceled=new RelaySender(endpoint,blocked); canceled.Enqueue("first"); await Until(()=>blocked.Calls>0);
        for(int i=0;i<250;i++) canceled.Enqueue("queued"); Check(canceled.Dropped>=50,"bounded queue");
        var stop=Task.Run(()=>canceled.Stop()); await Until(()=>blocked.Canceled); await stop;
        Check(!canceled.Enqueue("late"),"disabled sender rejects"); Check(stop.IsCompleted,"sender worker fully stopped before return");
        var failed=new FakeHandler { Fail=true };
        using(var sender=new RelaySender(endpoint,failed)) { sender.Enqueue("test"); await Until(()=>sender.Dropped==1); Check(sender.StatusKey=="SendFailed" && failed.Calls==1,"HTTP failure no blind retry"); }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        internal volatile int Calls; internal string Body; internal Uri RequestUri; internal bool Block, Fail; internal volatile bool Canceled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Calls); RequestUri=request.RequestUri; Body=await request.Content.ReadAsStringAsync();
            if(Block) { try { await Task.Delay(Timeout.Infinite,token); } catch(OperationCanceledException) { Canceled=true; throw; } }
            return new HttpResponseMessage(Fail?HttpStatusCode.Unauthorized:Calls==1?(HttpStatusCode)429:HttpStatusCode.OK) { Content=new StringContent(Calls==1?"{\"retry_after\":0.01}":"{}") };
        }
    }
    private static async Task Until(Func<bool> condition) { for(int i=0;i<150;i++){if(condition())return;await Task.Delay(20);}throw new Exception("Timed out"); }
    private static void RenderSettings()
    {
        foreach(var lang in new[]{"en","ja","zh-CN","ko"})
        using(var form=new Form{ClientSize=new Size(820,760),ShowInTaskbar=false,Opacity=0})
        using(var control=new SettingsControl(new PluginSettings{Language=lang,CheckUpdatesOnStartup=false,Channels=new[]{14,24,37,56,57}}))
        {
            form.Controls.Add(control); form.Show(); Application.DoEvents(); form.PerformLayout();
            var tabs=FindTabs(control); Check(tabs.TabCount==4,"four tabs");
            for(int i=0;i<4;i++){tabs.SelectedIndex=i;Application.DoEvents();control.PerformLayout();using(var bitmap=new Bitmap(820,760)){control.DrawToBitmap(bitmap,new Rectangle(0,0,820,760));bitmap.Save(Path.Combine(artifacts,"settings-"+lang+"-"+i+".png"));}}
            form.ClientSize=new Size(420,560); form.PerformLayout(); Check(control.Width<=420,"narrow pane");
            tabs.SelectedIndex=1;Application.DoEvents();
            foreach(var checkbox in Descendants(control).OfType<CheckBox>().Where(x=>x.Name.StartsWith("Channel"))) Check(checkbox.Height>=checkbox.Font.Height+12 && checkbox.Right<=checkbox.Parent.ClientSize.Width,"channel fits narrow card");
            var combo=Descendants(control).OfType<ComboBox>().Single(); combo.SelectedIndex=(combo.SelectedIndex+1)%4;
            Check(tabs.TabPages[3].Text==Localization.Get(combo.SelectedIndex==0?"en":combo.SelectedIndex==1?"ja":combo.SelectedIndex==2?"zh-CN":"ko","Support"),"live language change");
            tabs.SelectedIndex=1; Application.DoEvents();
            using(var bitmap=new Bitmap(420,560)){control.DrawToBitmap(bitmap,new Rectangle(0,0,420,560));bitmap.Save(Path.Combine(artifacts,"channels-narrow-"+lang+".png"));}
            control.Scale(new SizeF(1.5f,1.5f)); Check(control.IsDisposed==false,"DPI scaling");
            foreach(var checkbox in Descendants(control).OfType<CheckBox>().Where(x=>x.Name.StartsWith("Channel"))) Check(checkbox.Height>=checkbox.Font.Height+12,"scaled channel height");
        }
    }
    private static IEnumerable<Control> Descendants(Control root) { foreach(Control c in root.Controls){yield return c;foreach(var x in Descendants(c))yield return x;} }
    private static TabControl FindTabs(Control root) => Descendants(root).OfType<TabControl>().Single();
    private static void Throws(Action action,string name) {try{action();}catch{Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    private static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);assertions++;}
}
