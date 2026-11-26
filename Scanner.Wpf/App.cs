using System.Windows;
namespace ResonancePilot;
internal static class Entry
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--software-rendering"))System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        bool capture=args.Contains("--dev-capture"),demo=capture||args.Contains("--demo");
        var app=new Application();var window=new ScannerWindow(demo);
        if(capture){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-30000;window.Top=-30000;window.ShowInTaskbar=false;window.Loaded+=async(_,_)=>{try{await window.CaptureDevelopmentAsync();}catch(Exception ex){System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"capture-error.txt"),ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};}
        app.Run(window);
    }
}

