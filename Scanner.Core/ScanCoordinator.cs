using System.Diagnostics;
namespace FolderScanner;

public sealed class ScanCoordinator(ScanEvidenceRegistry? evidence = null)
{
    public async Task<ScanReport> RunAsync(string root, IScannerEngine engine, IProgress<ScanProgress>? progress,
        CancellationToken cancellation)
    {
        var report = new ScanReport { Root = Path.GetFullPath(root), Engine = engine.Description, Simulated = engine.Simulated };
        evidence?.BeginRun(report);
        var clock=Stopwatch.StartNew();
        var telemetry=new ScanTelemetryCollector();
        TimeSpan? lastPublished=null;
        void Publish(string phase,string path,bool force=false)
        {
            if(progress==null)return;
            var elapsed=clock.Elapsed;
            if(!force&&lastPublished.HasValue&&elapsed-lastPublished.Value<TimeSpan.FromMilliseconds(250))return;
            lastPublished=elapsed;
            progress.Report(new(phase,path,report.DiscoveredFiles,report.SubmittedFiles,
                report.EngineAcknowledgedFiles,report.Findings,report.Errors,report.Telemetry??telemetry.Snapshot(elapsed,phase)));
        }
        void AddIssue(string path,string reason){report.Issues.Add(new(path,reason));telemetry.ObserveIssue(clock.Elapsed);}
        try
        {
            PathPolicy.ValidateLocalDirectory(report.Root);
            report.VideoFolderFound = IsVideoFolder(report.Root);
            var stack = new Stack<string>(); stack.Push(report.Root);
            while (stack.Count > 0 && !cancellation.IsCancellationRequested)
            {
                var dir = stack.Pop();
                try
                {
                    PathPolicy.ValidateAncestors(dir);
                    // Lazy enumeration avoids retaining a whole user tree in memory.
                    foreach (var item in Directory.EnumerateFileSystemEntries(dir))
                    {
                        if (cancellation.IsCancellationRequested) break;
                        Publish("איסוף ובדיקה", item);
                        try
                        {
                            PathPolicy.ValidateAncestors(item);
                            var attributes = File.GetAttributes(item);
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                            { AddIssue(item, "דולג: קישור, נקודת חיבור או קובץ לא מקומי"); continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            { report.VideoFolderFound |= IsVideoFolder(item); stack.Push(item); continue; }
                            report.DiscoveredFiles++;
                            report.AttemptedFiles++;
                            long? knownSize=null;
                            try{knownSize=new FileInfo(item).Length;}
                            catch(Exception metadataError)when(metadataError is IOException or UnauthorizedAccessException or ArgumentException){ }
                            telemetry.ObserveDiscovered(item,knownSize,clock.Elapsed);
                            Publish("ממתין למנוע — ביטול יעצור את התור לאחר תשובת הקובץ הפעיל", item);
                            using var scanEvidence = evidence?.CaptureBeforeScan(report, item);
                            if (cancellation.IsCancellationRequested) break;
                            var result = await engine.ScanFileAsync(item);
                            report.Results.Add(result);
                            telemetry.ObserveResult(result,clock.Elapsed);
                            scanEvidence?.Bind(result);
                            if (result.SubmittedToEngine) report.SubmittedFiles++;
                            Publish("התקבלה תשובת מנוע", item);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                        { AddIssue(item, ex.Message); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { AddIssue(dir, ex.Message); }
            }
            report.EnumerationComplete = !cancellation.IsCancellationRequested;
            // Empty enumeration is not proof of engine availability or coverage.
            if (report.SubmittedFiles == 0) AddIssue(report.Root, "לא התקבלה סריקת קובץ מהמנוע; גם תיקייה ריקה אינה מקבלת אישור נקי");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { AddIssue(report.Root, ex.Message); }
        finally
        {
            report.CancelRequested = cancellation.IsCancellationRequested;
            report.Ended = DateTimeOffset.Now;
            if(report.CancelRequested) evidence?.Invalidate();
            report.Telemetry=telemetry.Complete(report,clock.Elapsed);
            Publish(report.CancelRequested?"הסריקה הסתיימה לאחר הפסקת המתנה":"התקבל דוח סופי",report.Root,true);
        }
        return report;
    }
    private static bool IsVideoFolder(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
        .Equals("ALON BLUTRICH", StringComparison.OrdinalIgnoreCase);
}

public static class PathPolicy
{
    public static void ValidateLocalDirectory(string path)
    {
        if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\") || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("מותרת תיקייה מקומית עם אות כונן בלבד");
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.DriveType != DriveType.Fixed) throw new ArgumentException("נתמכים כוננים מקומיים קבועים בלבד");
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("התיקייה אינה זמינה");
        ValidateAncestors(path);
    }
    public static void ValidateAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.GetAttributes(current) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                throw new IOException("קישור או תוכן לא מקומי בנתיב — דולג");
            current = Path.GetDirectoryName(current);
        }
    }
}
