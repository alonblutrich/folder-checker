using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace ResonancePilot;

// Development layout evidence only. Never represents independent or real desktop UI usage.
internal sealed partial class ScannerWindow
{
    internal async Task CaptureDevelopmentAsync()
    {
        string output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","development-renders"));Directory.CreateDirectory(output);
        var evidence=new List<object>();
        while(controller.State.Discovering)await Task.Delay(20);
        foreach(var size in new[]{new Size(960,760),new Size(1280,900),new Size(1920,1080)}){Width=size.Width;Height=size.Height;await Settle();Save("home-"+size.Width);}
        controller.SelectTarget(@"C:\DEMO-ONLY\תיקייה ארוכה לבדיקת קריאות ובקרות");
        Width=960;Height=760;await Settle();Save("selected-960");
        var run=controller.StartAsync();await Task.Delay(650);await Settle();Save("running-960");Width=1280;Height=900;await Settle();Save("running-1280");Width=1920;Height=1080;await Settle();Save("running-1920");
        await run;await Settle();Save("partial-1920");Width=1280;Height=900;await Settle();Save("partial-1280");Width=960;Height=760;await Settle();Save("partial-960");
        var detail=ScannerPresentation.ResultProjection.PrimaryDetails(controller.State)!;
        using(var frame=new DisposableDetails(new ScannerPresentation.Wpf.ReadOnlyDetailsWindow(detail,isDemo:true){Owner=this,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-30000,Top=-30000}))
        {
            frame.Window.Show();await Settle();var content=(FrameworkElement)frame.Window.Content;content.UpdateLayout();var target=new RenderTargetBitmap((int)frame.Window.ActualWidth,(int)frame.Window.ActualHeight,96,96,PixelFormats.Pbgra32);target.Render(frame.Window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));using var file=File.Create(Path.Combine(output,"details.png"));png.Save(file);
        }
        demoSession!.Scenario="failure";controller.SelectTarget(@"C:\DEMO-ONLY\תיקיית כשל מדומה");await controller.StartAsync();Width=960;Height=760;await Settle();Save("failed-960");Width=1920;Height=1080;await Settle();Save("failed-1920");
        File.WriteAllText(Path.Combine(output,"evidence.json"),JsonSerializer.Serialize(new{Scope="Own normal WPF window on isolated execution desktop, offscreen self-render. Controller uses injected DEMO session; not real UI input/engine test/acceptance.",BinarySHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ScannerWindow).Assembly.Location))),WpfPresentationSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ScannerPresentation.Wpf.ResultsView).Assembly.Location))),ControllerSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ScannerPresentation.ScannerController).Assembly.Location))),Steps=evidence},new JsonSerializerOptions{WriteIndented=true}));
        void Save(string name){root.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(root);var image=new RenderTargetBitmap((int)Math.Round(root.ActualWidth*dpi.DpiScaleX),(int)Math.Round(root.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);evidence.Add(new{Name=name,Outer=new{Width,Height},Client=new{root.ActualWidth,root.ActualHeight},Dpi=dpi.PixelsPerInchX,Page=controller.State.Page.ToString(),ReportSimulated=controller.State.Report?.Simulated});}
        static async Task Settle(){await Task.Delay(60);await Dispatcher.Yield(DispatcherPriority.Render);}
    }
    sealed class DisposableDetails(Window window):IDisposable{internal Window Window=>window;public void Dispose()=>window.Close();}
}


