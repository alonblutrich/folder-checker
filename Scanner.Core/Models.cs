namespace FolderScanner;

public enum Verdict { NoThreatsReported, Threat, Unknown, Error }
public enum RunState { CleanAccordingToEngine, Threats, Partial, Cancelled, Failed }
public record FileResult(string Path, Verdict Verdict, string Detail, int? ExitCode = null,
    bool CoverageVerified = false, string Treatment = "לא ידוע; לא התבקש טיפול מהיישום",
    bool SubmittedToEngine = false, bool EngineStarted = false, string? CoverageReason = null,
    WindowsTreatmentEvidence? WindowsTreatment = null);
public record ScanIssue(string Path, string Reason);
public record DeletionEvidenceIssue(string Path, string Stage, string Reason, string? Detail = null, int? NativeErrorCode = null);
public record FileActionResult(string Path,string Outcome,string Detail,DateTimeOffset At);
public sealed class ScanReport
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string Root { get; init; } = "";
    public string Engine { get; init; } = "";
    public bool Simulated { get; init; }
    public DateTimeOffset Started { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? Ended { get; set; }
    public int DiscoveredFiles { get; set; }
    public int SubmittedFiles { get; set; }
    public int AttemptedFiles { get; set; }
    public List<FileResult> Results { get; } = [];
    public List<ScanIssue> Issues { get; } = [];
    // Separate from scan errors/coverage. Evidence failure must never erase a Threat.
    public List<DeletionEvidenceIssue> DeletionEvidenceIssues { get; } = [];
    public int DeletionEvidenceIssuesOmitted { get; internal set; }
    public List<FileActionResult> FileActions { get; } = [];
    // Bounded non-clean command transcripts from this run. Not imported into finding/identity guards.
    public List<EngineTrace> EngineDiagnostics { get; } = [];
    public int EngineDiagnosticsOmitted { get; internal set; }
    public string WindowsEvidenceStatus { get; set; } = "NotQueried";
    public int WindowsEvidenceTargetsOmitted { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ScanTelemetrySnapshot? Telemetry { get; set; }
    public bool CancelRequested { get; set; }
    public bool EngineProcessExited { get; set; } = true;
    public bool? EngineStopConfirmed { get; set; } = null;
    public bool EnumerationComplete { get; set; }
    public bool VideoFolderFound { get; set; }
    public string Execution => CancelRequested ? "WaitStopped" : Ended == null ? "Running" :
        Results.Any(x => x.EngineStarted) ? "Completed" : "Failed";
    public string Coverage => CoverageComplete ? "VerifiedWithinScope" : "Unverified";
    public string[] CoverageReasons => Results.Where(x => x.CoverageReason != null).Select(x => x.CoverageReason!)
        .Concat(Issues.Select(_ => "EnumerationOrPathGap")).Distinct().ToArray();
    public int WindowsHandledFiles => Results.Count(x => x.WindowsTreatment is not null);
    public int Findings => Results.Count(x => x.Verdict == Verdict.Threat || x.WindowsTreatment is not null);
    public int EngineAcknowledgedFiles => Results.Count(x => x.Verdict is Verdict.NoThreatsReported or Verdict.Threat);
    public int Errors => Issues.Count + Results.Count(x => x.WindowsTreatment is null && (x.Verdict is Verdict.Error or Verdict.Unknown));
    public bool CoverageComplete => EnumerationComplete && !CancelRequested && Issues.Count == 0 &&
        Results.Count == DiscoveredFiles && Results.All(x => x.CoverageVerified) && !Simulated;
    public RunState State => CancelRequested ? RunState.Cancelled :
        !EngineProcessExited ? RunState.Failed :
        CoverageComplete ? (Findings > 0 ? RunState.Threats : RunState.CleanAccordingToEngine) :
        !Results.Any(x => x.EngineStarted) ? RunState.Failed : RunState.Partial;
}
public record ScanProgress(string Phase, string Path, int Discovered, int Submitted, int Acknowledged, int Findings, int Errors,
    ScanTelemetrySnapshot? Telemetry = null);
public interface IScannerEngine
{
    string Description { get; }
    bool Simulated { get; }
    Task<FileResult> ScanFileAsync(string path);
}
