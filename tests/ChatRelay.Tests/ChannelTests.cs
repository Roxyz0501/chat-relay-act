using ChatRelay;
using System;
using System.Linq;
using System.Windows.Forms;

internal static partial class Program
{
    private static void DiagnosticFiltering()
    {
        var diagnostics = new[]{"Hojoring>[EX] Beacon X=24.94 Y=22.44 Z=-0.40 zone=The Goblet","Hojoring>[TL] set VAR['ZONE_NAME'] = The Goblet","Hojoring>[TL] set VAR['ZONE_ID'] = 347","Hojoring>[EX] Clear combatants.","Hojoring>[TL] clear VAR['1B_ORIGIN']"};
        ChatMessage message;
        foreach(var body in diagnostics)
        {
            Check(!ChatParser.TryParse("00|time|0038||"+body+"|hash",out message),"Hojoring network rejected");
            Check(!ChatParser.TryParse("[12:34:56.789] ChatLog 00:0038::"+body,out message),"Hojoring parsed rejected");
            Check(!ChatParser.TryParseEvent(false,"00|time|0038||"+body+"|hash","00:0038::sanitized diagnostic",out message),"fallback cannot bypass exclusion");
        }
        Check(!ChatParser.TryParse("00|time|0038|Hojoring>|[EX] Clear combatants.|hash",out message),"diagnostic sender rejected");
        Check(!ChatParser.TryParse("00|time|0039|| hojoring>[TL] internal|hash",out message),"diagnostic system rejected");
        Check(ChatParser.TryParse("00|time|0038||準備完了|hash",out message),"real Echo preserved");
        Check(ChatParser.TryParse("00:0038::Hojoringについてのメモ",out message),"ordinary Echo mention preserved");
        Check(ChatParser.TryParse("00:0018:Player:Hojoring>[EX] quoted example",out message),"player conversation preserved");
        Check(!ChatParser.TryParseEvent(true,"00:0038::real echo",null,out message),"imports excluded");
        Check(ChatParser.TryParseEvent(false,null,"00:0038::real echo",out message),"parsed fallback supported");
        Check(ChatParser.TryParseEvent(false,"00:0038::original","00:0038::modified",out message) && message.Text=="original","original preferred");
        var old=new PluginSettings { Channels=new[]{14,56} };old.Normalize();Check(old.Channels.SequenceEqual(new[]{14,56}),"new system channels remain opt in");
    }
    private static void ChannelSelection()
    {
        foreach(var lang in new[]{"en","ja","zh-CN","ko"})
        using(var form=new Form { ClientSize=new System.Drawing.Size(820,760),Opacity=0,ShowInTaskbar=false })
        using(var control=new SettingsControl(new PluginSettings { Language=lang,Channels=new[]{14,56} }))
        {
            form.Controls.Add(control);form.Show();FindTabs(control).SelectedIndex=1;Application.DoEvents();
            var selector=Descendants(control).OfType<ChannelSelectionControl>().Single();
            var checkboxes=Descendants(selector).OfType<CheckBox>().ToArray();
            Check(checkboxes.Length==ChatParser.Channels.Length,"every channel displayed once");
            Check(selector.SelectedChannels.SequenceEqual(new[]{14,56}),"existing choices loaded");
            var events=0;control.SettingsChanged+=(s,e)=>events++;
            var buttons=Descendants(selector).OfType<Button>().ToArray();
            buttons.Single(x=>x.Name=="AllOn").PerformClick();
            Check(selector.SelectedChannels.Length==ChatParser.Channels.Length && events==1,"all on one change event");
            buttons.Single(x=>x.Name=="AllOn").PerformClick();Check(events==1,"no-op does not reset relay");
            buttons.Single(x=>x.Name=="GroupLSOff").PerformClick();
            Check(events==2 && selector.SelectedChannels.Contains(14) && !selector.SelectedChannels.Contains(16),"group off affects only group");
            var saved=new PluginSettings();control.ApplyTo(saved);Check(saved.Channels.SequenceEqual(selector.SelectedChannels),"batch stored immediately");
            buttons.Single(x=>x.Name=="AllOff").PerformClick();Check(events==3 && selector.SelectedChannels.Length==0,"all off single change");
            checkboxes.Single(x=>x.Name=="Channel57").Checked=true;Check(events==4 && selector.SelectedChannels.SequenceEqual(new[]{57}),"individual system selection");
            selector.ApplyLanguage(lang=="en"?"ja":"en");Check(events==4 && selector.SelectedChannels.SequenceEqual(new[]{57}),"language preserves selection without events");
            buttons.Single(x=>x.Name=="GroupCWLSOn").PerformClick();Check(events==5 && selector.SelectedChannels.Length==9 && selector.SelectedChannels.Contains(37) && selector.SelectedChannels.Contains(107),"CWLS button selects entire group");
        }
    }
}
