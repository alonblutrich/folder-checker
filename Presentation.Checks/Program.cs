using System.Collections.Concurrent;
using FolderScanner;
using ScannerPresentation;

var ui=new TestUi();var fake=new FakeSession();using var controller=new ScannerController(ui,fake);
int checks=0,video=0,notifications=0;
void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
controller.PropertyChanged+=(_,_)=>{ui.VerifyAccess();notifications++;};
controller.VideoRequested+=url=>{Check(url==ScannerController.TributeVideoUrl,"fixed video URL");video++;};
Check(!controller.State.CanStart,"start unavailable before verified engine");
controller.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",true);Check(video==1,"one video request for one explicit matching selection");
Check(controller.State.VideoNotice.Contains("נשלחה בקשה"),"successful launcher reports dispatch only");
using(var noLauncher=new ScannerController(ui,new FakeSession()))
{
 noLauncher.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",true);
 Check(noLauncher.State.VideoNotice.StartsWith("לא ניתן"),"missing launcher never claims success");
 noLauncher.SelectTarget(@"C:\DEMO ONLY\ordinary",true);
 Check(noLauncher.State.VideoNotice.Length==0,"ordinary folder clears video notice");
}
using(var failedLauncher=new ScannerController(ui,new FakeSession()))
{
 int calls=0;failedLauncher.VideoRequested+=_=>{calls++;throw new IOException("synthetic launch failure");};
 failedLauncher.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",true);
 Check(calls==1&&failedLauncher.State.VideoNotice.StartsWith("לא ניתן"),"failed launcher reports failure without retry");
 failedLauncher.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",false);
 Check(calls==1&&failedLauncher.State.VideoNotice.Length==0,"programmatic selection neither launches nor retains notice");
}
using(var refreshSession=new FakeSession())
using(var refreshController=new ScannerController(ui,refreshSession))
{
 int calls=0;refreshController.VideoRequested+=_=>calls++;
 refreshController.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",true);
 var refresh=refreshController.DiscoverAsync();refreshSession.Discovery.SetResult(new("FAKE-NOT-EXECUTABLE","test"));ui.Complete(refresh);
 Check(calls==1&&refreshController.State.VideoNotice.Contains("נשלחה בקשה"),"engine refresh preserves notice without retrigger");
}
using(var changedSelection=new ScannerController(ui,new FakeSession()))
{
 changedSelection.VideoRequested+=_=>changedSelection.SelectTarget(@"C:\DEMO ONLY\ordinary");
 changedSelection.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH",true);
 Check(changedSelection.State.VideoNotice.Length==0,"launcher cannot publish notice for a replaced selection");
}
controller.SelectTarget(@"C:\DEMO ONLY\ALON BLUTRICH\child",true);Check(video==1,"child folder does not match tribute selection");
var discover=controller.DiscoverAsync();Check(controller.State.Discovering&&!controller.State.CanStart,"discovery disables start");
fake.Discovery.SetResult(new("FAKE-NOT-EXECUTABLE","Fake test engine"));ui.Complete(discover);Check(controller.State.CanStart,"discovery completion published on UI");
var first=controller.StartAsync();var oldProgress=fake.Progress!;
Check(controller.State.Page==ScannerPage.Running&&controller.State.Report is null,"starting clears prior report");
Check(!controller.SelectTarget(@"C:\OTHER")&&!controller.StartAsync().GetAwaiter().GetResult(),"selection and duplicate start refused while running");
oldProgress.Report(new("first","file1",4,3,2,0,0));ui.Drain();Check(controller.State.Progress?.Discovered==4,"progress delivered through UI scheduler");
Check(!controller.CanClose()&&controller.State.CancellationPending&&fake.Cancellation.IsCancellationRequested,"closing requests queue cancellation and stays open");
oldProgress.Report(new("late","file2",50,49,48,0,0));ui.Drain();Check(controller.State.Progress?.Discovered==4,"pending cancellation suppresses progress repaint");
var cancelled=new ScanReport{Root=controller.State.Target,Ended=DateTimeOffset.Now,CancelRequested=true,EngineProcessExited=true};
fake.Run!.SetResult(cancelled);ui.Complete(first);Check(ReferenceEquals(controller.State.Report,cancelled)&&!controller.State.Running,"cancelled run retains actual final report");
oldProgress.Report(new("stale","file3",999,999,999,0,0));ui.Drain();Check(controller.State.Page==ScannerPage.Results&&controller.State.Progress is null,"queued progress cannot overwrite final report");
var second=controller.RepeatAsync();Check(controller.State.Running&&controller.State.Report is null,"repeat clears previous result while starting same target");
oldProgress.Report(new("old-run","old",999,999,999,0,0));ui.Drain();Check(controller.State.Progress is null,"prior epoch ignored during new run");
bool disposeRefused=false;try{controller.Dispose();}catch(InvalidOperationException){disposeRefused=true;}Check(disposeRefused&&!fake.Disposed,"active session cannot be disposed or closed");
fake.Run!.SetException(new IOException("fake engine failure"));ui.Complete(second);
Check(controller.State.Report is null&&controller.State.Failure?.Reason=="fake engine failure"&&controller.State.ShowDetailsInitially,"failure shows exact details without synthetic successful report");
Check(!controller.State.CanExport,"failed exception cannot export old report");
controller.SelectTarget(@"C:\DEMO ONLY\another");Check(controller.State.Failure is null&&controller.State.Snapshot is null&&controller.State.Page==ScannerPage.Home,"selection clears failure and telemetry");
var third=controller.StartAsync();var report=new ScanReport{Root=controller.State.Target,Ended=DateTimeOffset.Now,EnumerationComplete=true};report.Issues.Add(new("missing","access denied"));fake.Run!.SetResult(report);ui.Complete(third);
Check(controller.State.ShowDetailsInitially&&controller.State.CanExport,"partial report defaults to details and preserves export eligibility");
Check(notifications>0&&fake.Accepted==2,"completed reports accepted once each");
Check(ResultProjection.PrimaryDetails(controller.State)?.Path=="missing","partial primary detail opens exact skipped item");
var large=new ScanReport{Root=@"C:\DEMO ONLY",Ended=DateTimeOffset.Now,EnumerationComplete=true};
for(int i=0;i<24000;i++)large.Results.Add(new($"file-{i}",i==12001?Verdict.Threat:Verdict.NoThreatsReported,"fake-only detail"));
var page=ResultProjection.Page(large,EntryFilter.All,offset:23990);
Check(page.Matching==24000&&page.Items.Count==10&&page.Items[0].Path=="file-23990","24K results retain bounded final page and exact count");
Check(ResultProjection.Page(large,EntryFilter.Threats).Items.Single().Path=="file-12001","threat filter preserves exact source result");
Check(ReferenceEquals(ResultProjection.PrimaryDetails(new(){Report=large})!.Original,large.Results[12001]),"primary finding retains original reference for guarded action integration");
fake.Discovery=new(TaskCreationOptions.RunContinuationsAsynchronously);var refreshing=controller.DiscoverAsync();
int runsBefore=fake.RunCalls,selectionsBefore=fake.SelectionCalls;
Check(!controller.RepeatAsync().GetAwaiter().GetResult()&&ReferenceEquals(controller.State.Report,report)&&fake.RunCalls==runsBefore&&fake.SelectionCalls==selectionsBefore,"repeat during discovery refuses without clearing report or evidence");
fake.Discovery.SetResult(new(null,"fake engine missing"));ui.Complete(refreshing);
Check(!controller.RepeatAsync().GetAwaiter().GetResult()&&ReferenceEquals(controller.State.Report,report)&&controller.State.CanExport&&fake.RunCalls==runsBefore&&fake.SelectionCalls==selectionsBefore,"repeat without engine preserves exact report, export and evidence");
var foreignFinding=new FileResult("foreign",Verdict.Threat,"fake",SubmittedToEngine:true,EngineStarted:true);
Check(!controller.CanOfferTreatment(foreignFinding)&&controller.RequestTreatment(foreignFinding,()=>throw new Exception("Backend must not be created"),_=>throw new Exception("No confirmation expected"))=="Refused","foreign finding refused before backend or confirmation");
fake.Discovery=new(TaskCreationOptions.RunContinuationsAsynchronously);var available=controller.DiscoverAsync();fake.Discovery.SetResult(new("FAKE","Fake"));ui.Complete(available);
var treatmentRun=controller.StartAsync();var treatmentReport=new ScanReport{Root=controller.State.Target,Ended=DateTimeOffset.Now};treatmentReport.Results.Add(foreignFinding);fake.Run!.SetResult(treatmentReport);ui.Complete(treatmentRun);
Check(controller.CanOfferTreatment(foreignFinding),"current completed non-simulated fake finding eligible for guard test only");
fake.FilePresent=false;Check(!controller.CanOfferTreatment(foreignFinding),"absent source is not offered additional UI treatment");fake.FilePresent=true;
var alreadyHandled=foreignFinding with{WindowsTreatment=new(treatmentReport.RunId,foreignFinding.Path,"synthetic-only",1,"test","הסגר",DateTimeOffset.Now,DateTimeOffset.Now,DateTimeOffset.Now,"test")};
treatmentReport.Results.Add(alreadyHandled);Check(!controller.CanOfferTreatment(alreadyHandled),"verified Windows treatment never offers another recycle action");
var outcome=controller.RequestTreatment(foreignFinding,()=>new FakeBackend(),request=>{Check(controller.State.Recycling&&!controller.State.CanStart&&!controller.State.CanExport&&!controller.CanClose()&&!controller.SelectTarget("other"),"confirmation keeps selection/start/export/close locked");return false;});
Check(outcome=="Cancelled"&&fake.TreatmentCalls==1&&!controller.State.Recycling&&treatmentReport.FileActions.Count==1&&!controller.CanOfferTreatment(foreignFinding),"cancelled fake confirmation records one outcome and prevents automatic retry");
controller.SelectTarget(@"C:\DEMO ONLY\cancelled-late-result");var lateArrival=controller.StartAsync();controller.RequestCancellation();
var lateWindowsReport=new ScanReport{Root=controller.State.Target,Ended=DateTimeOffset.UtcNow};
lateWindowsReport.Results.Add(alreadyHandled with{Verdict=Verdict.Unknown});fake.Run!.SetResult(lateWindowsReport);ui.Complete(lateArrival);
Check(controller.State.Report==lateWindowsReport&&lateWindowsReport.CancelRequested&&lateWindowsReport.WindowsHandledFiles==0,"pending UI cancellation revokes Windows enrichment before publishing late session report");
// Diagnostic inputs here are synthetic command transcripts, never live Defender/EICAR.
var diagnostics=new EngineDiagnosticBuffer();
var at=DateTimeOffset.UtcNow;
var cleanPath=@"C:\OWNED-SYNTHETIC\clean.txt";
diagnostics.Observe(new(cleanPath,at,at,0,$"Scan starting...\nScan finished.\nScanning {cleanPath} found no threats.\n","",false));
Check(diagnostics.Entries.Count==0,"verified clean transcript is not retained as diagnostic noise");
var unknown=new EngineTrace(@"C:\OWNED-SYNTHETIC\archive.zip",at,at,2,"Unknown archive grammar; not proof of a threat","",false);
diagnostics.Observe(unknown);
Check(diagnostics.Entries.Single()==unknown&&DefenderEngine.Parse(unknown.Path,2,unknown.StandardOutput,"").Verdict==Verdict.Unknown,"diagnostic preserves unknown evidence without promoting code2 to Threat");
diagnostics.Observe(unknown with{StandardOutput=new string('o',10000),StandardError=new string('e',3000)});
var bounded=diagnostics.Entries[1];
Check(bounded.StandardOutput.Length==8192&&bounded.StandardError.Length==2048&&bounded.Truncated,"diagnostic records explicit output bounds and truncation");
diagnostics.Observe(unknown with{Truncated=true});diagnostics.Observe(unknown);diagnostics.Observe(unknown);
Check(diagnostics.Entries.Count==4&&diagnostics.Omitted==1&&diagnostics.Entries[2].Truncated,"per-run diagnostic entry cap and original truncation are retained");
var diagnosticReport=new ScanReport();diagnostics.AttachTo(diagnosticReport);
var exported=System.Text.Json.JsonSerializer.Serialize(diagnosticReport);
Check(diagnosticReport.EngineDiagnostics.Count==4&&diagnosticReport.EngineDiagnosticsOmitted==1&&diagnosticReport.Results.Count==0&&diagnosticReport.FileActions.Count==0&&exported.Contains("Unknown archive grammar"),"report export retains bounded evidence under its RunId without creating findings or actions");
var nextDiagnosticRun=new EngineDiagnosticBuffer();
Check(nextDiagnosticRun.Entries.Count==0&&nextDiagnosticRun.Omitted==0,"diagnostic buffer is fresh for each run");
checks+=WindowsTreatmentChecks.RunAsync().GetAwaiter().GetResult();
Console.WriteLine($"TOTAL {checks} focused managed checks; synthetic sessions/transcripts only, no discovery/engine/file mutation/browser/native recycle executed.");

sealed class TestUi:IUiScheduler
{
 readonly int thread=Environment.CurrentManagedThreadId;readonly ConcurrentQueue<Action> queue=new();
 public void VerifyAccess(){if(Environment.CurrentManagedThreadId!=thread)throw new Exception("Cross-thread UI access");}
 public void Post(Action action)=>queue.Enqueue(action);
 public Task InvokeAsync(Action action){var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Post(()=>{try{action();done.SetResult();}catch(Exception ex){done.SetException(ex);}});return done.Task;}
 public void Drain(){VerifyAccess();while(queue.TryDequeue(out var action))action();}
 public void Complete(Task task){var until=DateTime.UtcNow.AddSeconds(5);while(!task.IsCompleted){Drain();if(DateTime.UtcNow>until)throw new TimeoutException();Thread.Sleep(1);}Drain();task.GetAwaiter().GetResult();}
}
sealed class FakeSession:IScannerSession,IFileTreatmentSession,IFilePresenceSession
{
 public bool FilePresent=true;public bool FileExists(string path)=>FilePresent;
 public TaskCompletionSource<EngineAvailability> Discovery=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public TaskCompletionSource<ScanReport>? Run;public IProgress<ScanProgress>? Progress;public CancellationToken Cancellation;public bool Disposed;public int Accepted,RunCalls,SelectionCalls,TreatmentCalls;
 public Task<EngineAvailability> DiscoverAsync()=>Discovery.Task;
 public void SelectionChanged()=>SelectionCalls++;
 public Task<ScanReport> RunAsync(string root,string executable,IProgress<ScanProgress> progress,CancellationToken cancellation){RunCalls++;Run=new(TaskCreationOptions.RunContinuationsAsynchronously);Progress=progress;Cancellation=cancellation;return Run.Task;}
 public void ReportAccepted(ScanReport report)=>Accepted++;
 public string Recycle(Func<ScanReport> current,FileResult finding,IReviewRecycleBackend backend,Func<RecycleConfirmation,bool> confirm){TreatmentCalls++;return confirm(new(current().RunId,finding.Path,"fake confirmation"))?"Refused":"Cancelled";}
 public void Dispose()=>Disposed=true;
}
sealed class FakeBackend:IReviewRecycleBackend{public RecycleReceipt RecycleOne(EvidenceSnapshot expected,Func<bool> stillMatches)=>throw new Exception("No recycle may be executed in these checks");}
