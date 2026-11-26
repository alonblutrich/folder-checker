namespace FolderScanner;
public sealed record ObjectIdentity(ulong Volume,string FileId128,string Sha256,uint Links,long Length);
public sealed record AncestorIdentity(string Path,uint Attributes);
public sealed record EvidenceSnapshot(string Path,ObjectIdentity File,IReadOnlyList<AncestorIdentity> Ancestors)
{
 public bool Matches(EvidenceSnapshot other)=>string.Equals(Path,other.Path,StringComparison.OrdinalIgnoreCase)&&File==other.File&&Ancestors.SequenceEqual(other.Ancestors);
}
public interface IScanEvidenceProvider { EvidenceSnapshot Capture(string path); }

// Snapshots, not proof of continuous immutability. Stable-folder assumption is mandatory.
public sealed class ScanEvidenceRegistry(IScanEvidenceProvider provider,int maximumFindings=64):IDisposable
{
 readonly object gate=new();
 readonly Dictionary<FileResult,EvidenceSnapshot> findings=new(ReferenceEqualityComparer.Instance);
 readonly HashSet<FileResult> claimed=new(ReferenceEqualityComparer.Instance);
 ScanReport? current;long generation;bool disposed;
 public int RetainedCount {get{lock(gate)return findings.Count;}}
 public void BeginRun(ScanReport report){lock(gate){ThrowDisposed();Clear();current=report;}}
 public void SelectionChanged()=>Invalidate();
 public void ReportReplaced(ScanReport report){lock(gate){if(!ReferenceEquals(current,report))Clear();}}
 public void OperationFinished(FileResult finding){lock(gate)findings.Remove(finding);}
 public void Invalidate(){lock(gate)Clear();}
 void Clear(){generation++;current=null;findings.Clear();claimed.Clear();}
 public bool ClaimOperation(FileResult finding){lock(gate)return !disposed&&findings.ContainsKey(finding)&&claimed.Add(finding);}
 public bool WasAttempted(FileResult finding){lock(gate)return claimed.Contains(finding);}
 void ThrowDisposed(){if(disposed)throw new ObjectDisposedException(nameof(ScanEvidenceRegistry));}
 static bool ExpectedFailure(Exception ex)=>ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
 void RecordIssue(ScanReport report,long epoch,string path,string stage,string reason,Exception? error=null)
 {
  lock(gate)
  {
   if(disposed||epoch!=generation||!ReferenceEquals(current,report))return;
   int? nativeCode=null;
   for(Exception? currentError=error;currentError!=null;currentError=currentError.InnerException)
    if(currentError is System.ComponentModel.Win32Exception win32){nativeCode=win32.NativeErrorCode;break;}
   if(report.DeletionEvidenceIssues.Count<64)report.DeletionEvidenceIssues.Add(new(path,stage,reason,error?.Message,nativeCode));
   else report.DeletionEvidenceIssuesOmitted++;
  }
 }
 public PendingEvidence? CaptureBeforeScan(ScanReport report,string path)
 {
  long epoch;
  lock(gate){ThrowDisposed();if(!ReferenceEquals(current,report)||report.Simulated||findings.Count>=maximumFindings)return null;epoch=generation;}
  EvidenceSnapshot snapshot;
  try{snapshot=provider.Capture(path);}
  catch(Exception ex)when(ExpectedFailure(ex)){RecordIssue(report,epoch,path,"BeforeScanCapture","CaptureUnavailable",ex);return null;}
  lock(gate){if(disposed||epoch!=generation||!ReferenceEquals(current,report))return null;return new(this,report,epoch,snapshot);}
 }
 bool Bind(ScanReport report,long epoch,FileResult finding,EvidenceSnapshot before)
 {
  lock(gate)
  {
   if(disposed||epoch!=generation||!ReferenceEquals(current,report)||report.Simulated||finding.Verdict!=Verdict.Threat||
    !finding.EngineStarted||!finding.SubmittedToEngine||!report.Results.Any(x=>ReferenceEquals(x,finding))||
    findings.Count>=maximumFindings||!string.Equals(finding.Path,before.Path,StringComparison.OrdinalIgnoreCase)||findings.ContainsKey(finding))return false;
   if(!before.Matches(provider.Capture(before.Path))){RecordIssue(report,epoch,before.Path,"AfterScanCapture","EvidenceChanged");return false;}
   findings.Add(finding,before);return true;
  }
 }
 public bool TryGetValidated(ScanReport report,FileResult finding,out EvidenceSnapshot? evidence)
 {
  lock(gate)
  {
   evidence=null;
   if(disposed||!ReferenceEquals(current,report)||report.Ended==null||report.CancelRequested||!report.EngineProcessExited||
    report.Simulated||!report.Results.Any(x=>ReferenceEquals(x,finding))||!findings.TryGetValue(finding,out var scanned))return false;
   try{if(scanned.Matches(provider.Capture(scanned.Path))){evidence=scanned;return true;}}
   catch(Exception ex)when(ExpectedFailure(ex)){RecordIssue(report,generation,finding.Path,"EligibilityRecheck","CaptureUnavailable",ex);}
   findings.Remove(finding);return false;
  }
 }
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;Clear();}}
 public sealed class PendingEvidence:IDisposable
 {
  readonly ScanEvidenceRegistry owner;readonly ScanReport report;readonly long epoch;EvidenceSnapshot? before;
  internal PendingEvidence(ScanEvidenceRegistry owner,ScanReport report,long epoch,EvidenceSnapshot before)=>(this.owner,this.report,this.epoch,this.before)=(owner,report,epoch,before);
  public bool Bind(FileResult result)
  {
   var snapshot=Interlocked.Exchange(ref before,null);if(snapshot==null)return false;
   try{return owner.Bind(report,epoch,result,snapshot);}
   catch(Exception ex)when(ExpectedFailure(ex)){owner.RecordIssue(report,epoch,result.Path,"AfterScanCapture","CaptureUnavailable",ex);return false;}
  }
  public void Dispose()=>Interlocked.Exchange(ref before,null);
 }
}
