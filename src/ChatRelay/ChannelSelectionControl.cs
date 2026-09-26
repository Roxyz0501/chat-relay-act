using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ChatRelay
{
    internal sealed class ChannelSelectionControl : UserControl
    {
        private readonly Dictionary<int, CheckBox> checks = new Dictionary<int, CheckBox>();
        private readonly List<Group> groups = new List<Group>();
        private readonly Button allOn = MakeButton("AllOn"), allOff = MakeButton("AllOff");
        private readonly Label summary = new Label { AutoSize = true };
        private readonly Label description = new Label { ForeColor = Color.FromArgb(103,116,137) };
        private readonly Panel toolbar = new Panel { Dock = DockStyle.Top, BackColor = Color.White };
        private readonly Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly Panel content = new Panel();
        private string language = "en";
        private bool batching, arranging;
        internal event EventHandler SelectionChanged;
        internal int[] SelectedChannels => ChatParser.Channels.Where(x => checks[x].Checked).ToArray();

        internal ChannelSelectionControl(int[] selected)
        {
            Dock = DockStyle.Fill; BackColor = Color.FromArgb(244,247,251); Font = new Font("Yu Gothic UI",9f);
            toolbar.Controls.AddRange(new Control[]{allOn,allOff,summary});
            content.Controls.Add(description);scroll.Controls.Add(content);Controls.Add(scroll);Controls.Add(toolbar);
            AddGroup("GroupNearby",new[]{10,11,30});
            AddGroup("GroupParty",new[]{12,13,14,15,32,36});
            AddGroup("GroupCommunity",new[]{24,27});
            AddGroup("GroupLS",Enumerable.Range(16,8).ToArray());
            AddGroup("GroupCWLS",new[]{37,101,102,103,104,105,106,107});
            AddGroup("GroupSystem",new[]{56,57,58,60});
            batching=true;var selection=new HashSet<int>(selected??new int[0]);foreach(var pair in checks)pair.Value.Checked=selection.Contains(pair.Key);batching=false;
            allOn.Click+=delegate{SelectChannels(ChatParser.Channels,true);};allOff.Click+=delegate{SelectChannels(ChatParser.Channels,false);};
            scroll.ClientSizeChanged+=delegate{Arrange();};SizeChanged+=delegate{Arrange();};
            ApplyLanguage("en");
        }
        private void AddGroup(string key,int[] channels)
        {
            var group=new Group{Key=key,Channels=channels,Card=new Panel{BackColor=Color.White},Title=new Label{AutoSize=true,ForeColor=Color.FromArgb(34,44,61)},On=MakeButton(key+"On"),Off=MakeButton(key+"Off")};
            group.Card.Controls.AddRange(new Control[]{group.Title,group.On,group.Off});
            foreach(var channel in channels)
            {
                var check=new CheckBox{Name="Channel"+channel,ForeColor=Color.FromArgb(48,61,80),Cursor=Cursors.Hand,UseVisualStyleBackColor=false,Padding=new Padding(10,4,8,4)};
                checks.Add(channel,check);group.Card.Controls.Add(check);PaintSelection(check);
                check.CheckedChanged+=delegate{PaintSelection(check);if(!batching){RefreshCounts();SelectionChanged?.Invoke(this,EventArgs.Empty);}};
            }
            group.On.Click+=delegate{SelectChannels(channels,true);};group.Off.Click+=delegate{SelectChannels(channels,false);};groups.Add(group);content.Controls.Add(group.Card);
        }
        private static void PaintSelection(CheckBox check){check.BackColor=check.Checked?Color.FromArgb(225,236,248):Color.FromArgb(247,249,252);}
        internal void SelectChannels(IEnumerable<int> channels,bool enabled)
        {
            var changed=false;batching=true;
            try{foreach(var id in channels){CheckBox check;if(checks.TryGetValue(id,out check)&&check.Checked!=enabled){check.Checked=enabled;changed=true;}}}
            finally{batching=false;}
            RefreshCounts();if(changed)SelectionChanged?.Invoke(this,EventArgs.Empty);
        }
        internal void ApplyLanguage(string value)
        {
            language=value;Font=new Font(Localization.FontFamily(value),9f);
            allOn.Text=T("AllOn");allOff.Text=T("AllOff");description.Text=T("ChannelHint");
            foreach(var pair in checks)pair.Value.Text=T(ChatParser.ChannelKey(pair.Key));
            foreach(var group in groups){group.On.Text=T("GroupOn");group.Off.Text=T("GroupOff");group.Title.Font=new Font(Font,FontStyle.Bold);}
            RefreshCounts();Arrange();
        }
        private void RefreshCounts()
        {
            summary.Text=T("SelectedCount",SelectedChannels.Length,checks.Count);
            foreach(var group in groups)group.Title.Text=T(group.Key)+"  "+group.Channels.Count(x=>checks[x].Checked)+" / "+group.Channels.Length;
            Arrange();
        }
        private void Arrange()
        {
            if(arranging||groups.Count==0||scroll.ClientSize.Width<=0)return;arranging=true;
            try
            {
                int pad=12,gap=8,height=Math.Max(36,Font.Height+18);
                int buttonWidth=Math.Max(84,Math.Max(allOn.PreferredSize.Width,allOff.PreferredSize.Width));
                allOn.SetBounds(pad,8,buttonWidth,height);allOff.SetBounds(pad+buttonWidth+gap,8,buttonWidth,height);
                var summaryX=pad+(buttonWidth+gap)*2;
                bool wrap=summaryX+summary.PreferredWidth+pad>ClientSize.Width;
                summary.Location=new Point(wrap?pad:summaryX,wrap?height+16:8+(height-summary.PreferredHeight)/2);
                toolbar.Height=wrap?height+24+summary.PreferredHeight:height+16;
                // Reserve scrollbar space to keep wrapping stable as content height changes.
                var width=Math.Max(260,scroll.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
                content.Width=width;
                var inner=Math.Max(220,width-2*pad);
                var descHeight=TextRenderer.MeasureText(description.Text,description.Font,new Size(inner,int.MaxValue),TextFormatFlags.WordBreak).Height+8;
                description.SetBounds(pad,pad,inner,descHeight);
                var y=pad+descHeight+gap;
                foreach(var group in groups)
                {
                    var cardWidth=inner;var itemArea=cardWidth-2*pad;
                    var titleHeight=group.Title.PreferredHeight;var titleWidth=group.Title.PreferredWidth;
                    var bw=Math.Max(84,Math.Max(group.On.PreferredSize.Width,group.Off.PreferredSize.Width));
                    var separate=titleWidth+bw*2+gap*2>itemArea;
                    group.Title.Location=new Point(pad,pad+(separate?0:(height-titleHeight)/2));
                    var by=separate?pad+titleHeight+gap:pad;
                    var bx=separate?pad:cardWidth-pad-bw*2-gap;
                    group.On.SetBounds(bx,by,bw,height);group.Off.SetBounds(bx+bw+gap,by,bw,height);
                    var itemTop=by+height+gap;
                    var minimum=group.Channels.Max(id=>TextRenderer.MeasureText(checks[id].Text,checks[id].Font).Width+48);
                    var columns=Math.Max(1,itemArea/Math.Max(220,minimum));
                    var itemWidth=(itemArea-(columns-1)*gap)/columns;
                    for(int i=0;i<group.Channels.Length;i++)checks[group.Channels[i]].SetBounds(pad+i%columns*(itemWidth+gap),itemTop+i/columns*(height+6),itemWidth,height);
                    var rows=(group.Channels.Length+columns-1)/columns;
                    var cardHeight=itemTop+rows*(height+6)-6+pad;
                    group.Card.SetBounds(pad,y,cardWidth,cardHeight);y+=cardHeight+10;
                }
                content.Height=y+pad;
            }
            finally{arranging=false;}
        }
        private string T(string key,params object[] args)=>Localization.Get(language,key,args);
        private static Button MakeButton(string name)
        {
            var button=new Button{Name=name,Padding=new Padding(8,3,8,3),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(244,247,251),ForeColor=Color.FromArgb(48,61,80),Cursor=Cursors.Hand};
            button.FlatAppearance.BorderColor=Color.FromArgb(171,181,196);button.FlatAppearance.MouseOverBackColor=Color.FromArgb(225,236,248);return button;
        }
        private sealed class Group{internal string Key;internal int[] Channels;internal Panel Card;internal Label Title;internal Button On,Off;}
    }
}
