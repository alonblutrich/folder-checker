namespace FolderScanner;

public sealed record DefenderEvidenceQuery(string RunId, DateTimeOffset Started, DateTimeOffset Ended,
    string[] Paths, string[] ProcessPaths);
public sealed record DefenderDetection(string DetectionId, long ThreatId, string? ThreatName,
    DateTimeOffset InitialDetectionTime, DateTimeOffset? RemediationTime, bool ActionSuccess,
    int ThreatStatusId, int ThreatStatusErrorCode, string ProcessName, string[] Resources, uint? AdditionalActionsBitMask = null);
public sealed record DefenderEvidenceBatch(bool Available, DefenderDetection[] Detections,
    DateTimeOffset ObservedAt, string? Error = null);
public interface IWindowsDefenderEvidenceReader
{
    Task<DefenderEvidenceBatch> ReadAsync(DefenderEvidenceQuery query, CancellationToken cancellation);
}
public sealed record WindowsTreatmentEvidence(string RunId, string Path, string DetectionId, long ThreatId,
    string? ThreatName, string Treatment, DateTimeOffset DetectedAt, DateTimeOffset RemediatedAt,
    DateTimeOffset ObservedAt, string ProcessName, string Source = "Microsoft Defender / MSFT_MpThreatDetection");
public enum WindowsPathObservation { Missing, Present, Unavailable }

public static class WindowsTreatmentMatcher
{
    public static readonly TimeSpan RemediationGrace = TimeSpan.FromSeconds(45);
    public static bool Correlates(DefenderEvidenceQuery query, string path, DefenderDetection detection)
        => query.Ended >= query.Started && query.Paths.Contains(path, StringComparer.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(detection.DetectionId) && detection.ThreatId > 0
        && detection.InitialDetectionTime >= query.Started && detection.InitialDetectionTime <= query.Ended
        && query.ProcessPaths.Contains(detection.ProcessName, StringComparer.OrdinalIgnoreCase)
        && detection.Resources.Any(resource => ExactOuterResource(resource, path));

    // An explicit outer archive resource is evidence about that exact file. An inner-only
    // resource, suffix/prefix match, alternate stream or neighbouring path never qualifies.
    static bool ExactOuterResource(string resource, string path)
        => new[]{"file:_", "containerfile:_", "file:", "containerfile:"}
            .Any(prefix => resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && resource[prefix.Length..].Equals(path, StringComparison.OrdinalIgnoreCase));

    public static WindowsTreatmentEvidence? Match(DefenderEvidenceQuery query, string path, DefenderEvidenceBatch batch)
    {
        if (!batch.Available || batch.Detections.Length > 128) return null;
        var candidates = batch.Detections.Where(d => Correlates(query, path, d)).ToArray();
        // Ambiguous, re-detected, failed, active or contradictory records are not a success.
        if (candidates.Length != 1) return null;
        var d = candidates[0];
        // Initial supported terminal action: quarantine. Other actions remain uncertain.
        if (!d.ActionSuccess || d.ThreatStatusErrorCode != 0 || d.AdditionalActionsBitMask != 0 || d.ThreatStatusId != 3
            || d.RemediationTime is not {} treated || treated < d.InitialDetectionTime
            || treated > query.Ended + RemediationGrace || treated > batch.ObservedAt) return null;
        var treatment = "הסגר";
        return new(query.RunId, path, d.DetectionId, d.ThreatId, d.ThreatName, treatment,
            d.InitialDetectionTime, treated, batch.ObservedAt, d.ProcessName);
    }
}

public static class WindowsTreatmentReconciliation
{
    public static DefenderEvidenceQuery? CreateQuery(ScanReport report, string[] processPaths)
    {
        if (report.Simulated || report.CancelRequested || report.Ended is null) return null;
        var candidates = report.Results.Where(x => x.Verdict != Verdict.NoThreatsReported && WithinRoot(report.Root, x.Path))
            .Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        report.WindowsEvidenceTargetsOmitted = Math.Max(0, candidates.Length - 64);
        var paths = candidates.Take(64).ToArray();
        var processes = processPaths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return paths.Length == 0 || processes.Length == 0 ? null : new(report.RunId, report.Started, report.Ended.Value, paths, processes);
    }
    static bool WithinRoot(string root, string path)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public static WindowsPathObservation ObservePath(string path)
    {
        try { _ = File.GetAttributes(path); return WindowsPathObservation.Present; }
        catch (FileNotFoundException) { return WindowsPathObservation.Missing; }
        catch (DirectoryNotFoundException) { return WindowsPathObservation.Missing; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return WindowsPathObservation.Unavailable; }
    }
    public static int Apply(ScanReport report, DefenderEvidenceQuery query, DefenderEvidenceBatch batch, Func<string,WindowsPathObservation> observePath, CancellationToken cancellation=default)
    {
        if (report.Simulated || report.CancelRequested || query.RunId != report.RunId || query.Started != report.Started || query.Ended != report.Ended) return 0;
        var prepared = new List<(int Index,FileResult Value)>();
        for (int i = 0; i < report.Results.Count; i++)
        {
            var file = report.Results[i];
            if (file.WindowsTreatment is not null || file.Verdict == Verdict.NoThreatsReported || !WithinRoot(report.Root, file.Path)) continue;
            if (WindowsTreatmentMatcher.Match(query, file.Path, batch) is not {} proof) continue;
            // An existing/recreated file or inaccessible path conflicts with confirmed
            // quarantine. Never turn File.Exists(false) / access denied into absence.
            if (observePath(file.Path) != WindowsPathObservation.Missing) continue;
            prepared.Add((i,file with { WindowsTreatment = proof, Treatment = $"Windows אימת טיפול מסוג {proof.Treatment}. לא נדרש טיפול נוסף מהיישום." }));
        }
        // Metadata probes can race a user cancellation. Do not publish any prepared
        // enrichment until every probe and the original report context are checked.
        cancellation.ThrowIfCancellationRequested();
        if (report.CancelRequested || query.RunId != report.RunId || query.Started != report.Started || query.Ended != report.Ended) return 0;
        foreach(var change in prepared)report.Results[change.Index]=change.Value;
        if(prepared.Count>0)RefreshTelemetry(report);
        return prepared.Count;
    }
    public static void Revoke(ScanReport report)
    {
        // Only a just-completed, not-yet-published run is revoked by the session/controller.
        // Original CLI verdict/detail and genuine findings are retained.
        for(int i=0;i<report.Results.Count;i++)if(report.Results[i].WindowsTreatment is not null)
            report.Results[i]=report.Results[i] with{WindowsTreatment=null,Treatment="לא ידוע; לא התבקש טיפול מהיישום"};
        RefreshTelemetry(report);
    }
    static void RefreshTelemetry(ScanReport report)
    {
        if (report.Telemetry is {} snapshot)
        {
            var outcomes = new[]{Verdict.NoThreatsReported, Verdict.Threat, Verdict.Unknown, Verdict.Error}
                .Select(v => new TelemetryBucket(v.ToString(), report.Results.Count(r => (r.WindowsTreatment is not null ? Verdict.Threat : r.Verdict) == v))).ToArray();
            report.Telemetry = snapshot with { Outcomes = Array.AsReadOnly(outcomes), Findings = report.Findings, Issues = report.Errors, Partial = !report.CoverageComplete };
        }
    }
}
