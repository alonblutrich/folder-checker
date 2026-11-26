namespace FolderScanner;
public sealed record RecycleReceipt(bool Recycled,bool Aborted,bool SourceAbsent,ObjectIdentity? RecycledIdentity,string? Refusal=null);
public interface IReviewRecycleBackend
{
 RecycleReceipt RecycleOne(EvidenceSnapshot expected,Func<bool> stillMatches);
}
public sealed record RecycleConfirmation(string RunId,string Path,string Message);

// Constructor takes the scan owner's registry, never a UI/imported snapshot.
public sealed class EvidenceDeletion(ScanEvidenceRegistry registry,IReviewRecycleBackend backend)
{
 public string Execute(Func<ScanReport> current,FileResult finding,Func<RecycleConfirmation,bool> confirm)
 {
  if(!registry.ClaimOperation(finding))return registry.WasAttempted(finding)?"AlreadyAttempted":"Refused";
  bool operationRequested=false;
  try
  {
   var report=current();
   if(!registry.TryGetValidated(report,finding,out var scanned))return "Refused";
   bool StillMatches()=>ReferenceEquals(current(),report)&&registry.TryGetValidated(report,finding,out var now)&&scanned!.Matches(now!);
   var request=new RecycleConfirmation(report.RunId,scanned!.Path,
    "להעביר לסל המחזור את הקובץ הבא בלבד?\n"+scanned.Path+
    "\nהתיקייה חייבת להישאר יציבה עד סיום הפעולה. המדידות אינן מוכיחות שלא חל שינוי ביניהן.");
   if(!confirm(request))return "Cancelled";
   if(!StillMatches())return "Refused";
   operationRequested=true;
   var receipt=backend.RecycleOne(scanned,StillMatches);
   if(receipt.Refusal=="InteractiveDesktopUnavailable")return "ContextUnavailable";
   return receipt.Recycled&&!receipt.Aborted&&receipt.SourceAbsent&&receipt.RecycledIdentity==scanned.File?"RecycledVerified":"Unverified";
  }
  catch{return operationRequested?"Unverified":"FailedClosed";}
  finally{registry.OperationFinished(finding);}
 }
}
