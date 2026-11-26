using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FolderScanner;

public record EngineTrace(string Path, DateTimeOffset Started, DateTimeOffset Ended, int ExitCode, string StandardOutput, string StandardError, bool Truncated);

public sealed class DefenderEngine(string executable, Action<EngineTrace>? diagnosticObserver = null) : IScannerEngine
{
    public string Description => $"Microsoft Defender | {FileVersionInfo.GetVersionInfo(executable).FileVersion} | {executable}";
    public bool Simulated => false;
    public static string? Locate()
    {
        var platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");
        try
        {
            var candidate = Directory.GetDirectories(platform).Where(x => Version.TryParse(Path.GetFileName(x).Split('-')[0], out _))
                .OrderByDescending(x => Version.Parse(Path.GetFileName(x).Split('-')[0]))
                .Select(x => Path.Combine(x, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
            if (candidate != null) return candidate;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public async Task<FileResult> ScanFileAsync(string path)
    {
        bool started = false;
        try
        {
            // Inspect metadata only: opening file content here can trigger real-time protection
            // before Defender receives the scan request. This is not an atomic snapshot.
            PathPolicy.ValidateAncestors(path);
            var beforeInfo = new FileInfo(path);
            var before = (beforeInfo.Length, beforeInfo.LastWriteTimeUtc);
            using var engineLease = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!EngineTrust.Verify(executable)) return new(path, Verdict.Error, "חתימת מנוע Microsoft אינה מאומתת");
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                return new(path, Verdict.Unknown, "קישור או קובץ לא מקומי — לא נבדק");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var arg in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("לא ניתן להפעיל את המנוע");
            started = true;
            var startedAt = DateTimeOffset.Now;
            var stdout = ReadBounded(process.StandardOutput);
            var stderr = ReadBounded(process.StandardError);
            // Custom scans have no verified scoped cancellation. Never kill or issue global -Cancel.
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;
            diagnosticObserver?.Invoke(new(path, startedAt, DateTimeOffset.Now, process.ExitCode,
                output.Text, error.Text, output.Truncated || error.Truncated));
            var parsed = Parse(path, process.ExitCode, output.Text, error.Text, output.Truncated || error.Truncated);
            try
            {
                PathPolicy.ValidateAncestors(path);
                var afterInfo = new FileInfo(path);
                if (before == (afterInfo.Length, afterInfo.LastWriteTimeUtc)) return parsed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            // A real positive engine finding survives a subsequent disappearance or metadata change.
            return parsed with { Verdict = parsed.Verdict == Verdict.Threat ? Verdict.Threat : Verdict.Unknown,
                Detail = parsed.Detail + " הקובץ השתנה או אינו נגיש לאחר הסריקה; הכיסוי חלקי.",
                CoverageVerified = false, CoverageReason = "FileChangedOrUnavailable" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new(path, Verdict.Error, ex.Message, EngineStarted: started, SubmittedToEngine: started); }
    }

    private static async Task<(string Text, bool Truncated)> ReadBounded(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; bool truncated = false; int n;
        while ((n = await reader.ReadAsync(buffer)) > 0)
        {
            var take = Math.Min(n, 65536 - text.Length);
            if (take > 0) text.Append(buffer, 0, take);
            if (take < n) truncated = true;
        }
        return (text.ToString(), truncated);
    }

    public static FileResult Parse(string path, int code, string stdout, string stderr, bool truncated = false)
    {
        // Exact English success transcript observed on 4.18.26100.4. Other locales fail closed.
        var lines = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (!truncated && code == 0 && string.IsNullOrWhiteSpace(stderr) && lines.SequenceEqual(new[] {
            "Scan starting...", "Scan finished.", $"Scanning {path} found no threats." }))
            return new(path, Verdict.NoThreatsReported,
                "Defender לא דיווח על איומים בקובץ שנבדק. ScanType3 + DisableRemediation; החרגות קבצים אינן חלות.", code,
                CoverageVerified: true, SubmittedToEngine: true, EngineStarted: true);
        // Grammar based on the documented researcher transcript, not a local EICAR integration.
        // Preserve a strong positive finding even when additional output leaves coverage uncertain.
        var summary = new Regex(@"^Scanning " + Regex.Escape(path) + @" found ([1-9][0-9]*) threats\.$", RegexOptions.CultureInvariant);
        if (!truncated && code == 2 && lines.Length >= 7 && lines[0] == "Scan starting..." && lines[1] == "Scan finished." &&
            summary.IsMatch(lines[2]) && lines.Any(x => x.Trim() == "LIST OF DETECTED THREATS") &&
            lines.Any(x => x.Trim() == "Threat information") &&
            lines.Any(x => Regex.IsMatch(x, @"^[ \t]*Threat[ \t]*:[ \t]*\S.+$")) &&
            lines.Any(x => Regex.IsMatch(x, @"^[ \t]*Resources[ \t]*:[ \t]*1 total[ \t]*$")) &&
            lines.Any(x => Regex.IsMatch(x, @"^[ \t]*file[ \t]*:[ \t]*" + Regex.Escape(path) + "$")))
            return new(path, Verdict.Threat, "Defender דיווח על איום בקובץ זה. כיסוי פלט האיום טרם אומת מקומית; הסריקה חלקית.",
                code, SubmittedToEngine: true, EngineStarted: true, CoverageReason: "ThreatTranscriptIntegrationUnverified");
        return new(path, code is 0 or 2 ? Verdict.Unknown : Verdict.Error,
            truncated ? "פלט המנוע חרג מהמגבלה; אין תוצאה מאומתת" :
            $"לא התקבל חוזה פלט מאומת (קוד {code}). אין להסיק שלא נמצאו איומים; עיינו ב-Windows Security.", code,
            SubmittedToEngine: true, EngineStarted: true, CoverageReason: "UnknownEngineResult");
    }
}
