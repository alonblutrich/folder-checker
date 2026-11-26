using System.IO;
using FolderScanner;
using ScannerPresentation;
namespace ResonancePilot;

// Explicit command-line demo session: never enumerates or reads any target directory.
internal sealed class DemoScannerSession:IScannerSession
{
    readonly DemoTelemetrySequence sequence=new();
    public string Scenario {get;set;}="partial";
    public Task<EngineAvailability> DiscoverAsync()=>Task.FromResult(new EngineAvailability("DEMO-NOT-EXECUTABLE","הדגמה בלבד — לא יופעל מנוע"));
    public void SelectionChanged(){}
    public void ReportAccepted(ScanReport report){}
    public void Dispose(){}
    public async Task<ScanReport> RunAsync(string root,string executable,IProgress<ScanProgress> progress,CancellationToken cancellation)
    {
        var mode=Scenario;var last=sequence.Frames[0];
        if(mode=="windowsHandled"){
            await Task.Delay(250,cancellation).ConfigureAwait(false);
            var now=DateTimeOffset.UtcNow;
            var sample=new ScanReport{Root=root,Engine="DEMO — no Defender or files accessed",Simulated=true,Started=now.AddSeconds(-32),Ended=now,EnumerationComplete=true,DiscoveredFiles=2,SubmittedFiles=2,WindowsEvidenceStatus="DEMO display fixture only"};
            string item=Path.Combine(root,"DEMO-Windows-handled.zip");
            sample.Results.Add(new(item,Verdict.Unknown,"תשובת CLI לא מאומתת — נתוני בדיקה בלבד",2,SubmittedToEngine:true,EngineStarted:true,CoverageReason:"DemoUnverifiedEngineOutput",WindowsTreatment:new(sample.RunId,item,"DEMO-detection",1,"DEMO — איום לדוגמה","הסגר",now.AddSeconds(-30),now.AddSeconds(-2),now,"DEMO process","DEMO — לא התקבל מידע חי מ-Windows"),Treatment:"DEMO — דוגמת טיפול מאומת, ללא פעולה בקובץ"));
            sample.Results.Add(new(Path.Combine(root,"DEMO-unverified.txt"),Verdict.Unknown,"אין ראיה מספקת לטיפול — דוגמת בדיקה",2,SubmittedToEngine:true,EngineStarted:true));
            return sample;
        }
        if(mode=="failure"){await Task.Delay(250).ConfigureAwait(false);throw new IOException("כשל DEMO יזום — לא הופעל מנוע ולא נבדקו קבצים.");}
        foreach(var snapshot in sequence.Frames)
        {
            if(cancellation.IsCancellationRequested)break;
            last=snapshot;progress.Report(new(snapshot.Phase,Path.Combine(root,"DEMO-only-file.txt"),snapshot.Discovered,snapshot.Submitted,snapshot.Acknowledged,snapshot.Findings,snapshot.Issues,snapshot));
            // Cancelling stops this simulated queue only, without manufacturing an engine abort.
            await Task.Delay(250).ConfigureAwait(false);
        }
        var report=new ScanReport{Root=root,Engine="DEMO ONLY — no engine execution",Simulated=true,Ended=DateTimeOffset.Now,EnumerationComplete=!cancellation.IsCancellationRequested,CancelRequested=cancellation.IsCancellationRequested,DiscoveredFiles=last.Discovered,SubmittedFiles=last.Submitted,Telemetry=last with{IsFinal=true,Cancelled=cancellation.IsCancellationRequested,Partial=true}};
        // Reconstruct synthetic result counts from the same final snapshot. These paths need not exist.
        int index=0;foreach(var bucket in last.Outcomes)for(int j=0;j<bucket.Count;j++)
        {
            var verdict=bucket.Label switch{"Threat"=>Verdict.Threat,"NoThreatsReported"=>Verdict.NoThreatsReported,"Unknown"=>Verdict.Unknown,_=>Verdict.Error};
            report.Results.Add(new(Path.Combine(root,$"DEMO-item-{++index}.txt"),verdict,"תוצאה מדומה בלבד; אין להתייחס לנתיב זה כממצא מנוע.",SubmittedToEngine:true,EngineStarted:true));
        }
        for(int i=0;i<last.Issues;i++)report.Issues.Add(new(Path.Combine(root,$"DEMO-skipped-{i}.txt"),"דילוג מדומה לבדיקת פרטי שגיאה"));
        return report;
    }
}
