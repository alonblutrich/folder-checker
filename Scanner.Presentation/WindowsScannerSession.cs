using FolderScanner;

namespace ScannerPresentation;

// Preparation only: the isolated design pilot never constructs this session.
public sealed class WindowsScannerSession : IScannerSession,IFileTreatmentSession,IFilePresenceSession
{
    readonly ScanEvidenceRegistry evidence=new(new WindowsScanEvidenceProvider());
    public bool FileExists(string path)=>File.Exists(path);
    public Task<EngineAvailability> DiscoverAsync()=>Task.Run(()=>{
        var path=DefenderEngine.Locate();
        return path is not null&&EngineTrust.Verify(path)
            ?new EngineAvailability(path,new DefenderEngine(path).Description)
            :new EngineAvailability(null,"לא נמצא מנוע עם חתימת Microsoft מאומתת");
    });
    public void SelectionChanged()=>evidence.SelectionChanged();
    public Task<ScanReport> RunAsync(string root,string executable,IProgress<ScanProgress> progress,CancellationToken cancellation)
        =>Task.Run(async()=>{
            var diagnostics=new EngineDiagnosticBuffer();
            var report=await new ScanCoordinator(evidence).RunAsync(root,new DefenderEngine(executable,diagnostics.Observe),progress,cancellation);
            diagnostics.AttachTo(report);
            await ReconcileWindowsTreatmentAsync(report,new WindowsDefenderEvidenceReader(),
                new[]{Environment.ProcessPath??"",executable},progress,cancellation);
            return report;
        });
    public static async Task ReconcileWindowsTreatmentAsync(ScanReport report,IWindowsDefenderEvidenceReader reader,
        string[] processPaths,IProgress<ScanProgress>? progress,CancellationToken cancellation,
        Func<TimeSpan,CancellationToken,Task>? delay=null,TimeSpan? timeBudget=null,Func<string,WindowsPathObservation>? observePath=null)
    {
        var query=WindowsTreatmentReconciliation.CreateQuery(report,processPaths);
        if(query is null)return;
        // Tests may shorten this deadline. No caller can extend the production 45s cap.
        var duration=timeBudget is {} requested&&requested>TimeSpan.Zero&&requested<WindowsTreatmentMatcher.RemediationGrace?requested:WindowsTreatmentMatcher.RemediationGrace;
        using var budget=new CancellationTokenSource(duration);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,budget.Token);
        var evidenceWait=linked.Token;
        delay??=Task.Delay;
        observePath??=WindowsTreatmentReconciliation.ObservePath;
        var detectionIds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var conflicts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DefenderEvidenceBatch? finalBatch=null;
        int[] delays={0,1,2,4,8,16}; // finite 31s backoff, only non-clean candidate files
        try{
            for(int attempt=0;attempt<delays.Length;attempt++){
                evidenceWait.ThrowIfCancellationRequested();
                if(attempt>0)await delay(TimeSpan.FromSeconds(delays[attempt]),evidenceWait);
                progress?.Report(new("מאמתים אם Windows כבר טיפל בממצא — ניתן להפסיק המתנה",query.Paths[0],report.DiscoveredFiles,
                    report.SubmittedFiles,report.EngineAcknowledgedFiles,report.Findings,report.Errors,report.Telemetry));
                var batch=await reader.ReadAsync(query,evidenceWait);
                evidenceWait.ThrowIfCancellationRequested();
                if(!batch.Available){finalBatch=null;report.WindowsEvidenceStatus="Unavailable: "+(batch.Error??"ReadFailed");break;}
                foreach(var path in query.Paths){
                    var ids=batch.Detections.Where(d=>WindowsTreatmentMatcher.Correlates(query,path,d)).Select(d=>d.DetectionId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if(ids.Length>1)conflicts.Add(path);
                    if(ids.Length==1){if(detectionIds.TryGetValue(path,out var original)&&original!=ids[0])conflicts.Add(path);else detectionIds[path]=ids[0];}
                }
                finalBatch=batch with{Detections=batch.Detections.Where(d=>!conflicts.Any(path=>WindowsTreatmentMatcher.Correlates(query,path,d))).ToArray()};
                var unresolved=query.Paths.Where(p=>WindowsTreatmentMatcher.Match(query,p,finalBatch) is null||observePath(p)!=WindowsPathObservation.Missing).ToArray();
                if(unresolved.Length==0)break;
                bool TerminalRejection(string path){
                    if(conflicts.Contains(path))return true;
                    var records=batch.Detections.Where(d=>WindowsTreatmentMatcher.Correlates(query,path,d)).ToArray();
                    return records.Length>1||records.Length==1&&(records[0].ThreatStatusErrorCode!=0||records[0].AdditionalActionsBitMask is >0||records[0].ThreatStatusId is 2 or 4 or 5 or 6 or >=100);
                }
                // Empty provider responses may precede late publication. They are not
                // terminal evidence and retain the same finite per-run wait budget.
                if(unresolved.All(TerminalRejection))break;
            }
            evidenceWait.ThrowIfCancellationRequested();
            if(finalBatch is not null){
                WindowsTreatmentReconciliation.Apply(report,query,finalBatch,observePath,evidenceWait);
                report.WindowsEvidenceStatus=report.WindowsHandledFiles>0?"VerifiedTreatmentWithinRun":"NoVerifiedTreatmentWithinRun";
                if(conflicts.Count>0)report.WindowsEvidenceStatus+="; ConflictingDetectionIds";
            }
        }catch(OperationCanceledException){WindowsTreatmentReconciliation.Revoke(report);if(cancellation.IsCancellationRequested){report.CancelRequested=true;report.WindowsEvidenceStatus="WaitStopped";}else report.WindowsEvidenceStatus="Unavailable: EvidenceTimeBudget";}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){report.WindowsEvidenceStatus="Unavailable: "+ex.GetType().Name;}
        if(cancellation.IsCancellationRequested){WindowsTreatmentReconciliation.Revoke(report);report.CancelRequested=true;report.WindowsEvidenceStatus="WaitStopped";}
    }
    public void ReportAccepted(ScanReport report)=>evidence.ReportReplaced(report);
    public void Dispose()=>evidence.Dispose();
    public string Recycle(Func<ScanReport> current,FileResult finding,IReviewRecycleBackend backend,Func<RecycleConfirmation,bool> confirm)
        =>new EvidenceDeletion(evidence,backend).Execute(current,finding,confirm);
}
