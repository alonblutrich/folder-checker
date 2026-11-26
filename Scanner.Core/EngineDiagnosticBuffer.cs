namespace FolderScanner;

// Per-run diagnostic evidence, not a second source of verdicts or treatment eligibility.
// Kept in memory and included only when the user exports the report; no automatic log files.
public sealed class EngineDiagnosticBuffer
{
    public const int MaximumEntries = 4, MaximumOutputCharacters = 8192, MaximumErrorCharacters = 2048;
    readonly List<EngineTrace> entries = [];
    public IReadOnlyList<EngineTrace> Entries => entries.AsReadOnly();
    public int Omitted { get; private set; }

    public void AttachTo(ScanReport report)
    {
        report.EngineDiagnostics.AddRange(entries);
        report.EngineDiagnosticsOmitted += Omitted;
    }

    public void Observe(EngineTrace trace)
    {
        if (DefenderEngine.Parse(trace.Path, trace.ExitCode, trace.StandardOutput, trace.StandardError, trace.Truncated)
            .Verdict == Verdict.NoThreatsReported) return;
        if (entries.Count == MaximumEntries) { Omitted++; return; }
        entries.Add(trace with {
            StandardOutput = Limit(trace.StandardOutput, MaximumOutputCharacters),
            StandardError = Limit(trace.StandardError, MaximumErrorCharacters),
            Truncated = trace.Truncated || trace.StandardOutput.Length > MaximumOutputCharacters || trace.StandardError.Length > MaximumErrorCharacters
        });
    }

    static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
