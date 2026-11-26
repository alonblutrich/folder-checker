using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FolderScanner;
using ScannerPresentation;

internal static class WindowsTreatmentChecks
{
    public static async Task<int> RunAsync()
    {
        int count=0;
        void Check(bool pass,string message){if(!pass)throw new Exception(message);count++;Console.WriteLine("PASS Windows evidence: "+message);}
        var start=DateTimeOffset.Parse("2026-11-25T16:51:43Z");var end=start.AddSeconds(5);
        const string root=@"C:\SYNTHETIC-OWNED",path=root+@"\sample.zip",process=@"C:\SYNTHETIC-APP\Scanner.Wpf.exe";
        var query=new DefenderEvidenceQuery("synthetic-run",start,end,[path],[process]);
        var detection=new DefenderDetection("synthetic-detection",1,"TEST ONLY — no engine",start.AddSeconds(2),start.AddSeconds(30),true,3,0,process,["containerfile:_"+path],0);
        var batch=new DefenderEvidenceBatch(true,[detection],start.AddSeconds(32));
        var proof=WindowsTreatmentMatcher.Match(query,path,batch);
        Check(proof is{Treatment:"הסגר",RunId:"synthetic-run"},"terminal quarantine matched to exact original window; 28s delayed treatment supported");
        foreach(var invalid in new[]{
            detection with{ActionSuccess=false},detection with{ThreatStatusId=1},detection with{ThreatStatusId=5},detection with{ThreatStatusId=6},
            detection with{ThreatStatusId=2},detection with{ThreatStatusId=4},detection with{ThreatStatusErrorCode=5},
            detection with{AdditionalActionsBitMask=null},detection with{AdditionalActionsBitMask=1},
            detection with{InitialDetectionTime=start.AddTicks(-1)},detection with{InitialDetectionTime=end.AddTicks(1)},
            detection with{ProcessName=process+".other"},detection with{Resources=["containerfile:_"+path+".other"]},
            detection with{Resources=["file:_"+path+"->inner"]},detection with{RemediationTime=null},
            detection with{RemediationTime=start},detection with{RemediationTime=end.AddSeconds(46)},detection with{DetectionId=""}
        })Check(WindowsTreatmentMatcher.Match(query,path,batch with{Detections=[invalid]}) is null,"incomplete, failed, pending or mismatched record rejected");
        Check(WindowsTreatmentMatcher.Match(query,path,batch with{ObservedAt=start.AddSeconds(29)}) is null,"future action cannot be verified before it occurred");
        Check(WindowsTreatmentMatcher.Match(query,path,batch with{Detections=[detection,detection with{DetectionId="second"}]}) is null,"two matching detection records are ambiguous");
        Check(WindowsTreatmentMatcher.Match(query,path,batch with{Available=false}) is null,"access failure never grants treatment success");
        Check(WindowsTreatmentMatcher.Match(query with{Started=start.AddDays(1),Ended=end.AddDays(1)},path,batch) is null,"historical detection cannot become a current scan finding");
        Check(WindowsTreatmentMatcher.Match(query,path,batch with{Detections=[detection with{ThreatName=null}]}) is{ThreatName:null},"unknown threat name is not invented");
        ScanReport Report(int files=1){var r=new ScanReport{RunId=query.RunId,Root=root,Started=start,Ended=end,EnumerationComplete=true,DiscoveredFiles=files,SubmittedFiles=files};for(int i=0;i<files;i++)r.Results.Add(new(i==0?path:Path.Combine(root,$"unknown-{i}.txt"),Verdict.Unknown,"Synthetic unverified CLI output",2,SubmittedToEngine:true,EngineStarted:true,CoverageReason:"UnknownEngineResult"));return r;}
        var report=Report(2);
        var clean=Report();clean.Results[0]=clean.Results[0] with{Verdict=Verdict.NoThreatsReported,CoverageVerified=true};
        Check(WindowsTreatmentReconciliation.CreateQuery(clean,[process]) is null,"clean scan never invokes evidence lookup or delay");
        var demo=Report();demo=new ScanReport{Root=root,Simulated=true,Ended=end};
        Check(WindowsTreatmentReconciliation.CreateQuery(demo,[process]) is null,"demo never queries real Defender history");
        Check(WindowsTreatmentReconciliation.Apply(report,query,batch,_=>WindowsPathObservation.Missing)==1,"verified official evidence plus confirmed missing path enriches one result");
        Check(report.Results[0].Verdict==Verdict.Unknown&&report.Results[0].WindowsTreatment==proof&&report.WindowsHandledFiles==1&&report.Findings==1&&report.Errors==1&&report.EngineAcknowledgedFiles==0&&!report.CoverageComplete&&report.State==RunState.Partial,"independent Windows proof preserves unknown CLI, other unknown file and partial coverage");
        var page=ResultProjection.Page(report);
        Check(page.Items[0].Title=="האיום כבר טופל על ידי Windows"&&page.Items[1].Title=="תוצאה לא מוכרת"&&ResultProjection.PrimaryDetails(new(){Report=report})?.Original==report.Results[0],"Hebrew result and primary details identify handled item while retaining unresolved peer");
        Check(!ResultProjection.Brief(page.Items[0]).Contains('\n')&&!ResultProjection.Brief(page.Items[0]).Contains('\r')&&page.Items[0].Detail.Contains(detection.DetectionId),"result card remains one summary line while full proof is retained for details/export");
        Check(ResultProjection.WindowsSummary(report).Contains("נותר פריט אחד שלא אומת")&&ResultProjection.WindowsSummary(report).Contains("קובץ אחד")&&ResultProjection.WindowsSummary(report).Contains("אין בכך אישור"),"mixed summary names single unverified peer without implying all clear");
        var export=ReportExporter.Serialize(report);
        using(var json=JsonDocument.Parse(export))Check(json.RootElement.GetProperty("report").GetProperty("Results")[0].GetProperty("WindowsTreatment").GetProperty("DetectionId").GetString()==detection.DetectionId,"export carries exact proof and original diagnostic result together");
        foreach(var state in new[]{WindowsPathObservation.Present,WindowsPathObservation.Unavailable})Check(WindowsTreatmentReconciliation.Apply(Report(),query,batch,_=>state)==0,"recreated/present or access-denied path is not treated as absent");
        Check(WindowsTreatmentReconciliation.Apply(Report(),query with{RunId="old-run"},batch,_=>WindowsPathObservation.Missing)==0,"old-run response cannot update a new report");
        var cancelled=Report();cancelled.CancelRequested=true;
        Check(WindowsTreatmentReconciliation.Apply(cancelled,query,batch,_=>WindowsPathObservation.Missing)==0,"cancelled run does not accept late evidence");
        var pending=detection with{ThreatStatusId=1,ActionSuccess=false,RemediationTime=null};
        var reader=new Reader((_,call,_)=>Task.FromResult(call==1?batch with{Detections=[pending]}:batch));
        var late=Report();int delays=0;
        await WindowsScannerSession.ReconcileWindowsTreatmentAsync(late,reader,[process],null,CancellationToken.None,(_,_)=>{delays++;Check(late.WindowsHandledFiles==0,"no success published before terminal late evidence");return Task.CompletedTask;},observePath:_=>WindowsPathObservation.Missing);
        Check(reader.Calls==2&&delays==1&&late.WindowsHandledFiles==1&&reader.Queries.All(q=>q.RunId==query.RunId&&q.Ended==end),"finite late lookup retains original detection window and run");
        var unpublished=new Reader((_,call,_)=>Task.FromResult(call<=3?batch with{Detections=[]}:batch));var publishedLate=Report();
        await WindowsScannerSession.ReconcileWindowsTreatmentAsync(publishedLate,unpublished,[process],null,CancellationToken.None,(_,_)=>Task.CompletedTask,observePath:_=>WindowsPathObservation.Missing);
        Check(unpublished.Calls==4&&publishedLate.WindowsHandledFiles==1,"three empty responses retain finite window for late publication, then terminal proof");
        var conflictReader=new Reader((_,call,_)=>Task.FromResult(batch with{Detections=[call==1?pending:detection with{DetectionId="replaced-detection"}]}));
        var conflicting=Report();
        await WindowsScannerSession.ReconcileWindowsTreatmentAsync(conflicting,conflictReader,[process],null,CancellationToken.None,(_,_)=>Task.CompletedTask,observePath:_=>WindowsPathObservation.Missing);
        Check(conflicting.WindowsHandledFiles==0&&conflicting.WindowsEvidenceStatus.Contains("ConflictingDetectionIds"),"DetectionId replacement across reads fails closed");
        var forever=new Reader((_,_,_)=>Task.FromResult(batch with{Detections=[pending]}));var waiting=Report();
        await WindowsScannerSession.ReconcileWindowsTreatmentAsync(waiting,forever,[process],null,CancellationToken.None,(_,_)=>Task.CompletedTask,observePath:_=>WindowsPathObservation.Missing);
        Check(forever.Calls==6&&waiting.WindowsHandledFiles==0,"pending status has finite attempt cap, never success");
        var blocked=new Reader(async(_,_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return batch;});var many=Report(100);var timer=Stopwatch.StartNew();
        await WindowsScannerSession.ReconcileWindowsTreatmentAsync(many,blocked,[process],null,CancellationToken.None,timeBudget:TimeSpan.FromMilliseconds(60),observePath:_=>WindowsPathObservation.Missing);
        Check(blocked.Calls==1&&blocked.Queries[0].Paths.Length==64&&many.WindowsEvidenceTargetsOmitted==36&&many.WindowsEvidenceStatus.Contains("EvidenceTimeBudget")&&!many.CancelRequested&&timer.Elapsed<TimeSpan.FromSeconds(3),"one global deadline bounds blocked reader for 100 candidates, not per file");
        using(var cancel=new CancellationTokenSource()){
            var cancelReader=new Reader((_,_,_)=>{cancel.Cancel();return Task.FromResult(batch);});var stopped=Report();
            await WindowsScannerSession.ReconcileWindowsTreatmentAsync(stopped,cancelReader,[process],null,cancel.Token,observePath:_=>WindowsPathObservation.Missing);
            Check(stopped.CancelRequested&&stopped.WindowsHandledFiles==0,"cancellation between read and commit suppresses late success");
        }
        using(var cancel=new CancellationTokenSource()){
            int probes=0;var stopped=Report();var terminal=new Reader((_,_,_)=>Task.FromResult(batch));
            await WindowsScannerSession.ReconcileWindowsTreatmentAsync(stopped,terminal,[process],null,cancel.Token,
                observePath:_=>{if(++probes==2)cancel.Cancel();return WindowsPathObservation.Missing;});
            Check(probes==2&&stopped.CancelRequested&&stopped.WindowsHandledFiles==0&&stopped.Results[0].WindowsTreatment is null,"cancellation inside final metadata probe cannot commit any enrichment");
        }
        byte[] Payload(string run="synthetic-run",uint? additional=0)=>JsonSerializer.SerializeToUtf8Bytes(new{runId=run,available=true,detections=new[]{new{detectionId=detection.DetectionId,threatId=detection.ThreatId,threatName=detection.ThreatName,initialDetectionTime=detection.InitialDetectionTime,remediationTime=detection.RemediationTime,actionSuccess=true,threatStatusId=3,threatStatusErrorCode=0,processName=process,resources=detection.Resources,additionalActionsBitMask=additional}}});
        var parsed=WindowsDefenderEvidenceReader.ParseResponse(query,Payload(),batch.ObservedAt);
        Check(parsed.Available&&WindowsTreatmentMatcher.Match(query,path,parsed) is not null,"actual adapter parser consumes typed, bounded, same-run provider payload");
        var noMask=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Payload()).Replace(",\"additionalActionsBitMask\":0",""));
        Check(WindowsDefenderEvidenceReader.ParseResponse(query,noMask,batch.ObservedAt).Detections[0].AdditionalActionsBitMask is null&&WindowsTreatmentMatcher.Match(query,path,WindowsDefenderEvidenceReader.ParseResponse(query,noMask,batch.ObservedAt)) is null,"missing AdditionalActions is never silently zero");
        Check(!WindowsDefenderEvidenceReader.ParseResponse(query,Payload("stale"),batch.ObservedAt).Available&&!WindowsDefenderEvidenceReader.ParseResponse(query,new byte[262145],batch.ObservedAt).Available&&!WindowsDefenderEvidenceReader.ParseResponse(query,Encoding.UTF8.GetBytes("not-json"),batch.ObservedAt).Available,"wrong run, oversized and malformed provider output are unavailable");
        return count;
    }
    sealed class Reader(Func<DefenderEvidenceQuery,int,CancellationToken,Task<DefenderEvidenceBatch>> read):IWindowsDefenderEvidenceReader
    {
        public int Calls;public List<DefenderEvidenceQuery> Queries=[];
        public Task<DefenderEvidenceBatch> ReadAsync(DefenderEvidenceQuery query,CancellationToken cancellation){Calls++;Queries.Add(query);return read(query,Calls,cancellation);}
    }
}
