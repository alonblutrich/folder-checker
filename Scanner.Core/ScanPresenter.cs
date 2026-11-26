namespace FolderScanner;

public record ResultRow(string Path, string Verdict, string Detail);
public record ScanPresentation(string Summary, string Counters, IReadOnlyList<ResultRow> Rows, string CareInstructions);

public static class ScanPresenter
{
    public const string CareInstructions = "אם דווח איום או שהתקבלה תוצאה לא מוכרת: אל תפתחו ואל תריצו את הקובץ. " +
        "הנתיב המלא מופיע ברשימת התוצאות וניתן להעתיקו באמצעות Ctrl+C.\r\n" +
        "פתחו Windows Security > הגנה מפני וירוסים ואיומים (Virus & threat protection) > אפשרויות סריקה (Scan options) > " +
        "סריקה מותאמת אישית (Custom scan). בחרו את התיקייה שמכילה את הנתיב והפעילו סריקה. " +
        "סריקה זו עשויה לבצע פעולות הגנה בהתאם להגדרות Windows.\r\n" +
        "אם Windows מציג איום, בדקו שהנתיב תואם ובחרו בפעולה המומלצת של Defender, כגון הסגר, ואשרו ב-Windows. " +
        "בדקו את התוצאה גם בהיסטוריית ההגנה (Protection history). אל תבחרו באפשרות להתיר את הקובץ ואל תוסיפו החרגה לצורך טיפול.\r\n" +
        "ממצאי הסריקה של יישום זה עם DisableRemediation עשויים לא להופיע בהיסטוריית ההגנה. " +
        "פתיחת Windows Security אינה אישור שהאיום טופל; היישום אינו משנה את הקובץ או מסמן אותו כטופל.";

    public static ScanPresentation Present(ScanReport r)
    {
        string summary = r.State switch {
            RunState.CleanAccordingToEngine => "הסריקה הסתיימה — Defender לא דיווח על איומים בקבצים שנבדקו. תוצאה זו אינה מבטיחה שאין קבצים מזיקים.",
            RunState.Cancelled => "ההמתנה הופסקה; ייתכן ש-Defender ממשיך לסרוק ברקע. התור הופסק; תהליך הקובץ הפעיל חזר. תוצאות חלקיות.",
            RunState.Failed => "הסריקה נכשלה. לא ניתן לקבוע את מצב התיקייה.",
            RunState.Threats => "הסריקה הושלמה בתחום המאומת.",
            _ => "הרצה: הסתיימה. הסריקה חלקית; כיסוי הבדיקה לא אושר במלואו. " +
                (r.Findings > 0 ? "Defender דיווח על איום; עיינו בנתיב ובהוראות הטיפול." :
                 r.Results.Any(x => x.Verdict is Verdict.Unknown or Verdict.Error) ?
                 "יש תוצאות לא מוכרות או שגיאות; אין להסיק שלא נמצאו איומים." :
                 "Defender לא דיווח על איומים בפריטים שעבורם התקבלה תוצאת סריקה.") };
        if (r.Findings > 0) summary = $"נמצאו {r.Findings} קבצים עם דיווח איום — " + summary;
        if (r.Simulated) summary = "הדגמה — ללא סריקת מנוע אמיתית. " + summary;
        var rows = r.Results.Select(x => new ResultRow(x.Path, x.Verdict switch {
            Verdict.NoThreatsReported => "לא דווחו איומים לפי המנוע", Verdict.Threat => "Defender דיווח על איום",
            Verdict.Error => "שגיאה", _ => "תוצאה לא מוכרת" }, x.Detail))
            .Concat(r.Issues.Select(x => new ResultRow(x.Path, "שגיאה / דילוג", x.Reason))).ToArray();
        return new(summary,
            $"נמצאו: {r.DiscoveredFiles} | נשלחו למנוע: {r.SubmittedFiles} | תוצאות מנוע מוכרות: {r.EngineAcknowledgedFiles} | קבצים עם דיווח איום: {r.Findings} | שגיאות/אי־ודאות: {r.Errors}",
            rows, CareInstructions);
    }
}
