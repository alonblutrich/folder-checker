using System.Windows.Threading;
namespace ScannerPresentation.Wpf;
public sealed class WpfUiScheduler(Dispatcher dispatcher):IUiScheduler
{
    public void VerifyAccess()=>dispatcher.VerifyAccess();
    public void Post(Action action)=>dispatcher.BeginInvoke(action,DispatcherPriority.Background);
    public Task InvokeAsync(Action action)=>dispatcher.InvokeAsync(action,DispatcherPriority.Normal).Task;
}
