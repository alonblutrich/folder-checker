using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ResonancePilot;
// Retained scene: immutable artwork and cached geometry. Frames only change transforms/opacity.
internal sealed class ResonanceScene : FrameworkElement
{
    readonly Stopwatch clock=new();readonly VisualCollection visuals;
    readonly DrawingVisual artworkVisual=new(),planes=new(),particles=new(),light=new(),orbit=new();
    readonly ContainerVisual layers=new();
    readonly BitmapImage artwork=new(new Uri("pack://application:,,,/Scanner.Wpf;component/Assets/spatial-scanner-core.png",UriKind.Absolute));
    readonly TranslateTransform artShift=new(),planeShift=new(),particleShift=new(),layoutShift=new();
    readonly ScaleTransform layoutScale=new(),lightScale=new();
    readonly RotateTransform orbitTurn=new();
    double phase,lastDraw=-1;bool subscribed,requested,enabled=true,reduced,disposed;Window? host;
    internal bool MotionRequested {get=>requested;set{requested=value;Sync();}}
    internal bool MotionEnabled {get=>enabled;set{enabled=value;Sync();}}
    internal bool ReducedMotion {get=>reduced;set{reduced=value;Sync();}}
    internal bool IsAnimating=>subscribed;
    internal Vector ParallaxOffset=>new(artShift.X,artShift.Y);
    internal void SetDecorativeParallax(double x,double y){if(!enabled||reduced||SystemParameters.HighContrast)return;x=Math.Clamp(x,-.5,.5)*8;y=Math.Clamp(y,-.5,.5)*5;artShift.X=x*.35;artShift.Y=y*.35;layoutShift.X=ActualWidth/2+x;layoutShift.Y=ActualHeight/2+y;}
    internal double Phase=>phase+(clock.IsRunning?clock.Elapsed.TotalSeconds*.34:0);
    internal ResonanceScene()
    {
        ClipToBounds=true;artwork.Freeze();visuals=new(this);visuals.Add(artworkVisual);visuals.Add(layers);layers.Children.Add(orbit);layers.Children.Add(planes);layers.Children.Add(particles);layers.Children.Add(light);
        artworkVisual.Transform=artShift;planes.Transform=planeShift;particles.Transform=particleShift;orbit.Transform=orbitTurn;light.Transform=lightScale;var layout=new TransformGroup();layout.Children.Add(layoutScale);layout.Children.Add(layoutShift);layers.Transform=layout;
        BuildGeometry();Loaded+=OnLoaded;Unloaded+=OnUnloaded;SizeChanged+=(_,_)=>ArrangeScene();IsVisibleChanged+=(_,_)=>Sync();SystemParameters.StaticPropertyChanged+=SettingsChanged;
        MouseMove+=(_,e)=>{if(enabled&&!reduced&&!SystemParameters.HighContrast){var p=e.GetPosition(this);SetDecorativeParallax(p.X/Math.Max(1,ActualWidth)-.5,p.Y/Math.Max(1,ActualHeight)-.5);}};
        MouseLeave+=(_,_)=>ResetParallax();
    }
    protected override int VisualChildrenCount=>visuals.Count;
    protected override Visual GetVisualChild(int index)=>visuals[index];
    void ResetParallax(){artShift.X=artShift.Y=0;layoutShift.X=ActualWidth/2;layoutShift.Y=ActualHeight/2;}
    void ArrangeScene(){if(ActualWidth<=0||ActualHeight<=0)return;double k=Math.Min(ActualWidth/1024,ActualHeight/1536);using(var dc=artworkVisual.RenderOpen()){if(!SystemParameters.HighContrast)dc.DrawImage(artwork,new Rect((ActualWidth-1024*k)/2,(ActualHeight-1536*k)/2,1024*k,1536*k));}double s=Math.Min(ActualWidth/660,ActualHeight/850);layoutScale.ScaleX=layoutScale.ScaleY=s;ResetParallax();}
    void BuildGeometry()
    {
        bool hc=SystemParameters.HighContrast;
        Brush Brush(byte a,byte r,byte g,byte b){if(hc)return SystemColors.WindowTextBrush;var brush=new SolidColorBrush(Color.FromArgb(a,r,g,b));brush.Freeze();return brush;}
        Point P(double x,double y,double z)=>new(x+z*.2,y+z*.23);
        using(var dc=planes.RenderOpen())for(int layer=0;layer<2;layer++){
            double y=layer==0?-75:85;var shape=new StreamGeometry();using(var c=shape.Open()){c.BeginFigure(P(-210,y,-110),true,true);c.LineTo(P(210,y,-110),true,false);c.LineTo(P(210,y,110),true,false);c.LineTo(P(-210,y,110),true,false);}shape.Freeze();dc.DrawGeometry(hc?null:Brush(20,73,197,220),new Pen(Brush(96,105,223,229),1),shape);
            var gridPen=new Pen(Brush(39,112,223,225),.7);for(int j=-3;j<=3;j++)dc.DrawLine(gridPen,P(j*55,y,-110),P(j*55,y,110));
        }
        using(var dc=particles.RenderOpen()){
            var near=Brush(180,170,250,235);var far=Brush(90,99,198,225);
            for(int i=0;i<96;i++){double a=i*2.399963,r=.22+(i%11)/16d,x=Math.Cos(a)*r*100,z=Math.Sin(a)*r*100,y=(i%19-9)*10.5;dc.DrawEllipse(i%3==0?near:far,null,P(x,y,z),i%5==0?2.5:1.2,i%5==0?2.5:1.2);}
        }
        using(var dc=orbit.RenderOpen()){
            var pen=new Pen(Brush(100,102,210,227),1);for(int segment=0;segment<8;segment++){var path=new StreamGeometry();using(var c=path.Open())for(int j=0;j<=12;j++){double a=(segment+j/12d*.64)*Math.PI/4;var p=P(Math.Cos(a)*205,Math.Sin(a)*32,Math.Sin(a)*110);if(j==0)c.BeginFigure(p,false,false);else c.LineTo(p,true,false);}path.Freeze();dc.DrawGeometry(null,pen,path);}
        }
        using(var dc=light.RenderOpen()){
            if(!hc){var wide=new RadialGradientBrush(Color.FromArgb(95,117,251,222),Colors.Transparent);wide.Freeze();dc.DrawEllipse(wide,null,new(0,0),30,112);var core=new RadialGradientBrush(Color.FromArgb(170,229,255,246),Colors.Transparent);core.Freeze();dc.DrawEllipse(core,null,new(0,0),7,79);}
            dc.DrawLine(new Pen(Brush(180,160,245,230),1.3),new(0,-126),new(0,126));
        }
    }
    void ApplyPhase(){double t=Phase;planeShift.Y=Math.Sin(t*.75)*17;particleShift.X=Math.Sin(t*.37)*7;particleShift.Y=Math.Cos(t*.54)*13;orbitTurn.Angle=Math.Sin(t*.27)*3;light.Opacity=.68+Math.Sin(t*.9)*.16;lightScale.ScaleX=1+Math.Sin(t*.8)*.1;}
    void OnLoaded(object sender,RoutedEventArgs e){host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostStateChanged;ArrangeScene();Sync();}
    void OnUnloaded(object sender,RoutedEventArgs e){Stop();if(host!=null)host.StateChanged-=HostStateChanged;host=null;}
    void HostStateChanged(object? sender,EventArgs e)=>Sync();
    void SettingsChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){Dispatcher.BeginInvoke(()=>{if(!disposed){BuildGeometry();ArrangeScene();Sync();}});}
    void Sync(){if(disposed)return;bool animate=requested&&enabled&&!reduced&&SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast&&IsVisible&&IsLoaded&&host?.WindowState!=WindowState.Minimized;if(animate&&!subscribed){clock.Restart();lastDraw=-1;CompositionTarget.Rendering+=Tick;subscribed=true;}else if(!animate){Stop();ResetParallax();}ApplyPhase();}
    void Stop(){if(clock.IsRunning){phase=Phase;clock.Reset();}if(subscribed){CompositionTarget.Rendering-=Tick;subscribed=false;}}
    void Tick(object? sender,EventArgs e){double now=clock.Elapsed.TotalSeconds;if(lastDraw<0||now-lastDraw>=1d/24){lastDraw=now;ApplyPhase();}}
    internal void DisposeScene(){disposed=true;Stop();SystemParameters.StaticPropertyChanged-=SettingsChanged;if(host!=null)host.StateChanged-=HostStateChanged;}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);dc.DrawRectangle(Brushes.Transparent,null,new(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight)));}
}


