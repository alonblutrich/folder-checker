using System.ComponentModel;
using FolderScanner;

namespace ScannerPresentation;

public enum ScannerPage { Home, Running, Results }
public sealed record EngineAvailability(string? Executable, string Description);
public sealed record ViewIssue(string Path, string Reason);
public sealed record ScannerViewState
{
    public ScannerPage Page { get; init; }
    public string Target { get; init; } = "";
    public bool Discovering { get; init; }
    public bool Running { get; init; }
    public bool Recycling { get; init; }
    public bool CancellationPending { get; init; }
    public EngineAvailability Engine { get; init; } = new(null,"המנוע טרם נבדק");
    public string Phase { get; init; } = "";
    public string CurrentPath { get; init; } = "";
    public TimeSpan Elapsed { get; init; }
    public ScanProgress? Progress { get; init; }
    public ScanTelemetrySnapshot? Snapshot { get; init; }
    public ScanReport? Report { get; init; }
    public ViewIssue? Failure { get; init; }
    public string Feedback { get; init; } = "";
    public string VideoNotice { get; init; } = "";
    public bool CanStart => !Running && !Recycling && !Discovering && Engine.Executable is not null && Target.Length>0;
    public bool CanSelect => !Running&&!Recycling;
    public bool CanExport => !Running&&!Recycling && Report is not null;
    public bool ShowDetailsInitially => Failure is not null || Report is { } r && (r.Findings>0 || r.Errors>0 || r.State==RunState.Failed);
    public bool CanShowVideo => Report?.VideoFolderFound==true;
}

// Implemented by the WPF Dispatcher at integration; no UI framework is referenced here.
public interface IUiScheduler
{
    void VerifyAccess();
    void Post(Action action);
    Task InvokeAsync(Action action);
}

public interface IScannerSession : IDisposable
{
    Task<EngineAvailability> DiscoverAsync();
    void SelectionChanged();
    Task<ScanReport> RunAsync(string root,string executable,IProgress<ScanProgress> progress,CancellationToken cancellation);
    void ReportAccepted(ScanReport report);
}
public interface IFileTreatmentSession
{
    string Recycle(Func<ScanReport> current,FileResult finding,IReviewRecycleBackend backend,Func<RecycleConfirmation,bool> confirm);
}
public interface IFilePresenceSession { bool FileExists(string path); }

// All state publication is on the supplied UI scheduler; engine work never owns UI controls.
public sealed class ScannerController : INotifyPropertyChanged, IDisposable
{
    public const string TributeVideoUrl="https://www.youtube.com/watch?v=793xevkkPTs";
    readonly IUiScheduler ui;
    readonly IScannerSession session;
    ScannerViewState state=new();
    long generation;
    bool disposed;
    CancellationTokenSource? active;
    public ScannerController(IUiScheduler ui,IScannerSession session)=>(this.ui,this.session)=(ui,session);
    public ScannerViewState State=>state;
    public event PropertyChangedEventHandler? PropertyChanged;
    // Application supplies the browser launcher; selection itself performs no external action.
    public event Action<string>? VideoRequested;
    void Publish(ScannerViewState value){ui.VerifyAccess();state=value;PropertyChanged?.Invoke(this,new(nameof(State)));}
    void Guard(){ui.VerifyAccess();ObjectDisposedException.ThrowIf(disposed,this);}

    public bool SelectTarget(string target,bool userInitiated=false)
    {
        Guard();if(!state.CanSelect)return false;
        session.SelectionChanged();generation++;
        Publish(new(){Target=target,Engine=state.Engine,Discovering=state.Discovering});
        if(userInitiated && Path.GetFileName(Path.TrimEndingDirectorySeparator(target)).Equals("ALON BLUTRICH",StringComparison.OrdinalIgnoreCase))
        {
            DispatchVideo();
        }
        return true;
    }

    void DispatchVideo()
    {
        var version=generation;
        string notice;
        try
        {
            var launch=VideoRequested??throw new InvalidOperationException("No video launcher");
            launch(TributeVideoUrl);
            notice="נשלחה בקשה לפתיחת הסרטון בדפדפן. אם לא הופיע, בדקו את הדפדפן.";
        }
        catch{notice="לא ניתן לפתוח את הסרטון בדפדפן. ניתן להמשיך בבדיקה.";}
        // A callback cannot attach its notice to a different selection or disposed window.
        if(!disposed&&generation==version)Publish(state with{VideoNotice=notice});
    }
    public async Task DiscoverAsync()
    {
        Guard();if(state.Running||state.Recycling||state.Discovering)return;
        Publish(state with{Discovering=true,Engine=new(null,"בודק התקנה וחתימת Microsoft…")});
        EngineAvailability result;
        try{result=await session.DiscoverAsync().ConfigureAwait(false);}
        catch(Exception ex){result=new(null,"בדיקת המנוע נכשלה: "+ex.Message);}
        await ui.InvokeAsync(()=>{if(!disposed)Publish(state with{Discovering=false,Engine=result});}).ConfigureAwait(false);
    }

    public async Task<bool> StartAsync()
    {
        Guard();if(!state.CanStart)return false;
        var run=++generation;var root=state.Target;var executable=state.Engine.Executable!;
        var cancellation=new CancellationTokenSource();active=cancellation;
        Publish(state with{Page=ScannerPage.Running,Running=true,CancellationPending=false,Report=null,Failure=null,
            Progress=null,Snapshot=null,CurrentPath="",Elapsed=TimeSpan.Zero,Phase="מכין את הסריקה",Feedback=""});
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var progress=new ForwardProgress(p=>ui.Post(()=>{
            if(disposed||generation!=run||!state.Running||state.CancellationPending)return;
            Publish(state with{Progress=p,Snapshot=p.Telemetry,CurrentPath=p.Path,Phase=p.Phase,Elapsed=clock.Elapsed});
        }));
        try
        {
            var report=await session.RunAsync(root,executable,progress,cancellation.Token).ConfigureAwait(false);
            await ui.InvokeAsync(()=>{
                if(disposed||generation!=run)return;
                if(state.CancellationPending){WindowsTreatmentReconciliation.Revoke(report);report.CancelRequested=true;report.WindowsEvidenceStatus="WaitStopped";}
                session.ReportAccepted(report);
                // Advance epoch before accepting any queued final progress callbacks.
                generation++;
                Publish(state with{Page=ScannerPage.Results,Running=false,CancellationPending=false,Report=report,
                    Snapshot=report.Telemetry,Progress=null,CurrentPath="",Elapsed=clock.Elapsed,
                    Phase=report.State==RunState.Failed?"הבדיקה נכשלה":"הבדיקה הסתיימה"});
            }).ConfigureAwait(false);
        }
        catch(Exception ex)
        {
            await ui.InvokeAsync(()=>{
                if(disposed||generation!=run)return;
                generation++;
                Publish(state with{Page=ScannerPage.Results,Running=false,CancellationPending=false,Report=null,Snapshot=null,
                    Progress=null,CurrentPath="",Elapsed=clock.Elapsed,Phase="לא ניתן היה להשלים את הבדיקה",
                    Failure=new(root,ex.Message)});
            }).ConfigureAwait(false);
        }
        finally
        {
            clock.Stop();
            await ui.InvokeAsync(()=>{if(ReferenceEquals(active,cancellation))active=null;cancellation.Dispose();}).ConfigureAwait(false);
        }
        return true;
    }

    public bool RequestCancellation()
    {
        Guard();if(!state.Running||state.CancellationPending)return false;
        active?.Cancel();
        Publish(state with{CancellationPending=true,Phase="ההמתנה הופסקה. ממתינים לחזרת הקובץ הפעיל; לא נשלחה הוראת עצירה ל־Defender."});
        return true;
    }
    public bool CanClose()
    {
        Guard();if(state.Recycling)return false;if(!state.Running)return true;RequestCancellation();return false;
    }
    public Task<bool> RepeatAsync()
    {
        Guard();if(!state.CanStart||state.Report is null||state.Report.Root.Length==0)return Task.FromResult(false);
        var root=state.Report.Root;SelectTarget(root);return StartAsync();
    }
    public void ExportToNewFile(string path)
    {
        Guard();if(!state.CanExport)throw new InvalidOperationException("No completed current report");
        // Existing core CreateNew/path safeguards remain the only export implementation.
        ReportExporter.SaveNew(state.Report!,path);
        Publish(state with{Feedback="הדוח נשמר ב־"+path});
    }
    public void RequestVideo()
    {
        Guard();if(state.CanShowVideo)DispatchVideo();
    }
    public bool CanOfferTreatment(FileResult finding)=>session is IFileTreatmentSession&&!state.Running&&!state.Recycling&&
        state.Report is {Simulated:false,Ended:not null,CancelRequested:false,EngineProcessExited:true} report&&
        finding is {Verdict:Verdict.Threat,EngineStarted:true,SubmittedToEngine:true,WindowsTreatment:null}&&
        (session is not IFilePresenceSession presence||presence.FileExists(finding.Path))&&
        report.Results.Any(x=>ReferenceEquals(x,finding))&&!report.FileActions.Any(x=>string.Equals(x.Path,finding.Path,StringComparison.OrdinalIgnoreCase));
    public string RequestTreatment(FileResult finding,Func<IReviewRecycleBackend> backendFactory,Func<RecycleConfirmation,bool> confirm)
    {
        Guard();if(!CanOfferTreatment(finding))return "Refused";
        var report=state.Report!;Publish(state with{Recycling=true});string result;
        try{result=((IFileTreatmentSession)session).Recycle(()=>state.Report??throw new InvalidOperationException("No current report"),finding,backendFactory(),confirm);}
        catch{result="FailedClosed";}
        string message=result switch{
            "RecycledVerified"=>"הקובץ הועבר לסל המחזור ואומתה קבלה תואמת. תוצאת הסריקה נשמרת כהיסטוריה.",
            "Cancelled"=>"הפעולה בוטלה באישור; לא התבקשה העברה לסל המחזור.",
            "ContextUnavailable"=>"לא בוצעה העברה: נדרש שולחן עבודה אינטראקטיבי רגיל. לא שונתה סביבת ההפעלה.",
            "AlreadyAttempted"=>"הניסיון לממצא זה כבר נוצל. יש לסרוק מחדש לפני בקשה חדשה.",
            "Refused"=>"לא בוצעה העברה: זהות היעד אינה מאומתת, היעד השתנה או שאין ראיות סריקה מתאימות.",
            "Unverified"=>"לא ניתן לאמת שהקובץ הועבר לסל. אין אישור לטיפול ואין לנסות שוב אוטומטית.",
            _=>"הפעולה נכשלה; אין אישור שהקובץ הועבר לסל. לא נעשה שימוש במחיקה קבועה כחלופה."};
        report.FileActions.Add(new(finding.Path,result,message,DateTimeOffset.Now));
        Publish(state with{Recycling=false,Feedback=message});return result;
    }
    public void Dispose()
    {
        ui.VerifyAccess();if(disposed)return;
        if(state.Running||state.Recycling)throw new InvalidOperationException("Active run/action must finish before disposal");
        disposed=true;generation++;session.Dispose();
    }
    sealed class ForwardProgress(Action<ScanProgress> publish):IProgress<ScanProgress>{public void Report(ScanProgress value)=>publish(value);}
}
