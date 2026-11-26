using FolderScanner;

namespace ScannerPresentation;

public enum EntryKind { File, Threat, Issue }
public enum EntryFilter { All, Attention, Threats, Issues }
public sealed record ResultEntry(string Path,string Title,string Detail,EntryKind Kind,FileResult? Original=null);
public sealed record ResultPage(IReadOnlyList<ResultEntry> Items,int Matching,int Offset,int PageSize);

public static class ResultProjection
{
    public static string Brief(ResultEntry entry)=>entry.Detail.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(line=>!string.IsNullOrWhiteSpace(line))?.Trim()??"";
    public static string WindowsSummary(ScanReport report)
    {
        string handled=report.WindowsHandledFiles==1?"אומת שקובץ אחד הועבר להסגר בידי Windows. ":$"אומת ש־{report.WindowsHandledFiles} קבצים הועברו להסגר בידי Windows. ";
        string unverified=report.Errors==1?"נותר פריט אחד שלא אומת. ":report.Errors>1?$"נותרו {report.Errors} פריטים לא מאומתים. ":"";
        return handled+unverified+(report.Findings>report.WindowsHandledFiles?"נותרו גם דיווחי איום נוספים. ":"")+"אין בכך אישור שהסריקה נקייה.";
    }
    // Bind a bounded page to WPF; never build a visual or text object for every 24K row.
    public static ResultPage Page(ScanReport report,EntryFilter filter=EntryFilter.Attention,string search="",int offset=0,int pageSize=30)
    {
        offset=Math.Max(0,offset);pageSize=Math.Clamp(pageSize,1,100);
        var rows=new List<ResultEntry>(pageSize);int count=0;
        bool Accept(EntryKind kind,string path,string detail)=>
            (filter==EntryFilter.All||filter==EntryFilter.Attention&&kind!=EntryKind.File||filter==EntryFilter.Threats&&kind==EntryKind.Threat||filter==EntryFilter.Issues&&kind==EntryKind.Issue)
            &&(search.Length==0||path.Contains(search,StringComparison.OrdinalIgnoreCase)||detail.Contains(search,StringComparison.OrdinalIgnoreCase));
        foreach(var item in report.Results)
        {
            var kind=Kind(item);
            if(!Accept(kind,item.Path,item.Detail))continue;
            if(count>=offset&&rows.Count<pageSize)rows.Add(FromFile(item,report.WindowsEvidenceStatus));
            count++;
        }
        foreach(var issue in report.Issues)
        {
            if(!Accept(EntryKind.Issue,issue.Path,issue.Reason))continue;
            if(count>=offset&&rows.Count<pageSize)rows.Add(new(issue.Path,"שגיאה / דילוג",issue.Reason,EntryKind.Issue));
            count++;
        }
        if(report.State==RunState.Failed&&report.Results.Count==0&&report.Issues.Count==0&&Accept(EntryKind.Issue,report.Root,"הבדיקה לא הושלמה"))
        {
            if(count>=offset&&rows.Count<pageSize)rows.Add(new(report.Root,"הבדיקה לא הושלמה","לא התקבל אישור לכיסוי. בדקו את המנוע ואת הגישה לתיקייה.",EntryKind.Issue));
            count++;
        }
        return new(rows.AsReadOnly(),count,offset,pageSize);
    }
    public static ResultEntry? PrimaryDetails(ScannerViewState state)
    {
        if(state.Failure is {} failure)return new(failure.Path,"פרטי הכשל",failure.Reason,EntryKind.Issue);
        if(state.Report is not {} report)return null;
        var threat=report.Results.FirstOrDefault(x=>Kind(x)==EntryKind.Threat);
        return threat is not null?FromFile(threat,report.WindowsEvidenceStatus):Page(report,EntryFilter.Issues,pageSize:1).Items.FirstOrDefault();
    }
    static EntryKind Kind(FileResult item)=>item.WindowsTreatment is not null||item.Verdict==Verdict.Threat?EntryKind.Threat:item.Verdict is Verdict.Error or Verdict.Unknown?EntryKind.Issue:EntryKind.File;
    static ResultEntry FromFile(FileResult value,string status)=>new(value.Path,value.WindowsTreatment is not null?"האיום כבר טופל על ידי Windows":value.Verdict switch{
        Verdict.Threat=>"Defender דיווח על איום",Verdict.NoThreatsReported=>"לא דווחו איומים לפי המנוע",Verdict.Error=>"שגיאה",_=>"תוצאה לא מוכרת"},
        (value.WindowsTreatment is {} proof?$"טיפול מאומת: {proof.Treatment}"+(string.IsNullOrWhiteSpace(proof.ThreatName)?"":$" · {proof.ThreatName}")+
            $"\r\nמקור: {proof.Source}\r\nזיהוי: {proof.DetectionId}\r\nריצה: {proof.RunId}\r\nזוהה: {proof.DetectedAt:O}\r\nטופל: {proof.RemediatedAt:O}\r\nתשובת פקודת הסריקה בנפרד: ":status!="NotQueried"?$"לא התקבל אימות מספק לכך ש־Windows השלים טיפול.\r\nבדיקת ראיות: {status}\r\n":"")+value.Detail+"\r\nטיפול: "+value.Treatment+(value.CoverageReason is null?"":"\r\nפער כיסוי: "+value.CoverageReason),Kind(value),value);
}
