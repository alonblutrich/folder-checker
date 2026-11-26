using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FolderScanner;
using ScannerPresentation.Wpf;
namespace ResonancePilot;
internal sealed class TelemetryDeck : Grid
{
    readonly DemoMeasurement[] charts=Enumerable.Range(0,5).Select(i=>new DemoMeasurement(i)).ToArray();
    readonly Grid sceneHost=new(){Margin=new(4,0,4,0)};
    internal TelemetryDeck(){FlowDirection=FlowDirection.LeftToRight;ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new(1.16,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});for(int i=0;i<6;i++)RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});foreach(var c in charts){c.FlowDirection=FlowDirection.RightToLeft;Children.Add(c);}SetColumn(sceneHost,1);SetRowSpan(sceneHost,6);Children.Add(sceneHost);Place(charts[0],0,0,2);Place(charts[1],0,2,2);Place(charts[4],0,4,2);Place(charts[2],2,0,3);Place(charts[3],2,3,3);}
    void Place(UIElement c,int col,int row,int rows){SetColumn(c,col);SetRow(c,row);SetRowSpan(c,rows);}
    internal void AttachScene(FrameworkElement scene){if(scene.Parent is Panel parent)parent.Children.Remove(scene);sceneHost.Children.Clear();sceneHost.Children.Add(scene);}
    internal void SetSnapshot(ScanTelemetrySnapshot? snapshot){foreach(var chart in charts)chart.SetSnapshot(snapshot);}
}
internal sealed class DemoMeasurement : Grid
{
    readonly TextBlock title=new(){FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=Brushes.WhiteSmoke},value=new(){FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(157,235,220))};
    readonly Plot plot;readonly int kind;
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()=>new ChartPeer(this);
    sealed class ChartPeer(DemoMeasurement owner):System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner){protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore()=>System.Windows.Automation.Peers.AutomationControlType.Custom;protected override string GetClassNameCore()=>"DemoTelemetryChart";}
    internal DemoMeasurement(int kind){this.kind=kind;ScannerTheme.ApplyResources(Resources);value.FlowDirection=FlowDirection.LeftToRight;value.Foreground=ScannerTheme.Accent;if(kind==0)value.FontSize=26;title.Foreground=ScannerTheme.Ink;Margin=new(4);Background=ScannerTheme.Surface;RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});var edge=new Border{BorderBrush=ScannerTheme.Rule,BorderThickness=new(1),CornerRadius=new(4),IsHitTestVisible=false};SetRowSpan(edge,2);Children.Add(edge);var head=new Grid(){Margin=new(10,5,10,2),Background=ScannerTheme.HeaderSurface};head.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});string[] labels=["קצב תשובות / שנ׳","קבצים מצטברים","סיומות · קבצים","גדלים · מטא־נתונים","תוצאות · קבצים"];title.Text=labels[kind];System.Windows.Automation.AutomationProperties.SetName(this,labels[kind]);System.Windows.Automation.AutomationProperties.SetAutomationId(this,"telemetryChart"+kind);head.Children.Add(title);SetColumn(value,1);head.Children.Add(value);Children.Add(head);plot=new(kind){Margin=new(6,2,6,5)};SetRow(plot,1);Children.Add(plot);}
    internal void SetSnapshot(ScanTelemetrySnapshot? s){plot.Snapshot=s;if(s is null){value.Text="—";System.Windows.Automation.AutomationProperties.SetHelpText(this,"ממתין לנתוני הריצה");plot.InvalidateVisual();return;}value.Text=kind==0?(s.Timeline.LastOrDefault()?.ResponsesPerSecond?.ToString("0.#")??"—"):kind==1?s.Discovered.ToString():kind==2?s.Discovered.ToString():kind==3?FormatBytes(s.KnownBytes):s.Outcomes.Sum(x=>x.Count).ToString();System.Windows.Automation.AutomationProperties.SetHelpText(this,$"זמן {s.Elapsed.TotalSeconds:0.##}; נמצאו {s.Discovered}; נשלחו {s.Submitted}; תשובות מוכרות {s.Acknowledged}; גודל ידוע {s.KnownSizeFiles}; גודל חסר {s.UnknownSizeFiles}; "+string.Join("; ",(kind==2?s.Extensions:kind==3?s.SizeHistogram:s.Outcomes).Select(b=>$"{b.Label}: {b.Count}")));plot.InvalidateVisual();}
    static string FormatBytes(long? v)=>!v.HasValue?"—":v>=1048576?$"{v/1048576d:0.#} MiB":$"{v/1024d:0.#} KiB";
    sealed class Plot : FrameworkElement
    {
        readonly int kind;internal ScanTelemetrySnapshot? Snapshot;static readonly Brush Ink=ScannerTheme.Ink,Mint=ScannerTheme.Accent,Amber=new SolidColorBrush(Color.FromRgb(237,187,120)),Blue=new SolidColorBrush(Color.FromRgb(134,178,234));
        internal Plot(int kind){this.kind=kind;FlowDirection=FlowDirection.LeftToRight;ClipToBounds=true;}
        protected override void OnRender(DrawingContext dc){base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<60||h<40)return;var s=Snapshot;if(s==null){Text(dc,"ממתין לנתונים",new(12,15),w-24);return;}var rule=new Pen(ScannerTheme.Rule,.7);
            if(kind<2){double left=30,right=w-14,top=8,bottom=h-(kind==1?35:22);var data=s.Timeline;if(data.Count==0)return;double maxY=kind==0?Math.Max(1,data.Max(x=>x.ResponsesPerSecond??0)):Math.Max(1,s.Discovered),maxX=Math.Max(.001,Math.Max(s.Elapsed.TotalSeconds,data.Max(x=>x.Seconds)));for(int i=0;i<3;i++){double y=top+(bottom-top)*i/2;dc.DrawLine(rule,new(left,y),new(right,y));Text(dc,(maxY*(2-i)/2).ToString("0.#"),new(1,y-7),28);}
                void Line(Func<TelemetrySample,double?> selector,Brush color,DashStyle? dash=null){var path=new StreamGeometry();using(var c=path.Open()){bool start=true;foreach(var p in data){var n=selector(p);if(!n.HasValue){start=true;continue;}var v=new Point(left+p.Seconds/maxX*(right-left),bottom-n.Value/maxY*(bottom-top));if(start){c.BeginFigure(v,false,false);start=false;}else c.LineTo(v,true,false);}}var pen=new Pen(color,1.5);if(dash!=null)pen.DashStyle=dash;dc.DrawGeometry(null,pen,path);}
                if(kind==0)Line(x=>x.ResponsesPerSecond,Mint);else{Line(x=>x.Discovered,Mint);Line(x=>x.Submitted,Blue,DashStyles.Dash);Line(x=>x.Acknowledged,Amber,DashStyles.Dot);}
                Text(dc,"0",new(left,bottom+2),30);Text(dc,$"{maxX:0.##} שנ׳",new(Math.Max(left,right-65),bottom+2),70);
                if(kind==1){Text(dc,"נמצאו",new(26,h-16),60,Mint);Text(dc,"נשלחו¹",new(93,h-16),65,Blue);Text(dc,"תשובות",new(168,h-16),65,Amber);}
            }else if(kind==4){
                double total=s.Outcomes.Sum(x=>x.Count);if(total<=0){Text(dc,"ממתין לתוצאות",new(18,20),w-36);return;}double radius=Math.Min(42,h*.36),cx=radius+18,cy=h*.48;Brush[] colors=[Mint,Amber,Blue,new SolidColorBrush(Color.FromRgb(220,132,172))];string[] names=["לא דווח איום","דיווח איום","לא מוכר","שגיאה"];double start=-90;
                for(int i=0;i<s.Outcomes.Count;i++){double angle=s.Outcomes[i].Count/total*360;if(angle>.1){double end=start+Math.Max(.1,angle-3);var path=new StreamGeometry();using(var c=path.Open()){Point A(double a)=>new(cx+radius*Math.Cos(a*Math.PI/180),cy+radius*Math.Sin(a*Math.PI/180));c.BeginFigure(A(start),false,false);c.ArcTo(A(end),new Size(radius,radius),0,end-start>180,SweepDirection.Clockwise,true,false);}dc.DrawGeometry(null,new Pen(colors[i],12),path);}start+=angle;Text(dc,$"{names[i]}  {s.Outcomes[i].Count}",new(cx+radius+24,5+i*23),Math.Max(1,w-cx-radius-30),colors[i]);}
                var number=new FormattedText(total.ToString("0"),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),24,Ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(number,new(cx-number.Width/2,cy-17));
            }else if(kind==3){
                var buckets=s.SizeHistogram;double left=26,right=w-12,top=16,bottom=h-42,max=Math.Max(1,buckets.Max(x=>x.Count)),step=(right-left)/buckets.Count;string[] sizeLabels=["0 B","<1\nKiB","<64\nKiB","<1\nMiB","<16\nMiB","16+\nMiB"];
                dc.DrawLine(rule,new(left,bottom),new(right,bottom));for(int i=0;i<buckets.Count;i++){double bh=(bottom-top)*buckets[i].Count/max,x=left+i*step+3;var fill=new LinearGradientBrush(Color.FromRgb(142,172,224),Color.FromRgb(40,72,111),90);dc.DrawRoundedRectangle(fill,null,new(x,bottom-bh,Math.Max(2,step-9),bh),2,2);Text(dc,buckets[i].Count.ToString(),new(x,bottom-bh-16),step);Text(dc,sizeLabels[i],new(x-3,bottom+3),step+3);}
            }else{var buckets=kind==2?s.Extensions:kind==3?s.SizeHistogram:s.Outcomes;double max=Math.Max(1,buckets.Max(x=>x.Count)),row=Math.Min(24,(h-4)/buckets.Count),labelWidth=kind==3?76:80,left=36,right=w-labelWidth-5;for(int i=0;i<buckets.Count;i++){var b=buckets[i];double y=i*row+2;string label=b.Label switch{"NoThreatsReported"=>"לא דווח איום","Threat"=>"דיווח איום","Unknown"=>"לא מוכר","Error"=>"שגיאה",_=>b.Label};Text(dc,b.Count.ToString(),new(4,y),30);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28,45,57)),null,new(left,y+5,Math.Max(1,right-left),5));Brush color=kind==4&&i==1?Amber:i%2==0?Mint:Blue;dc.DrawRectangle(color,null,new(left,y+5,Math.Max(0,(right-left)*b.Count/max),5));Text(dc,label,new(right+6,y),labelWidth);}}
        }
        void Text(DrawingContext dc,string text,Point p,double width,Brush? color=null){var ft=new FormattedText(text,CultureInfo.GetCultureInfo("he-IL"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),13.5,color??Ink,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(1,width),Trimming=TextTrimming.CharacterEllipsis};dc.DrawText(ft,p);}
    }
}










