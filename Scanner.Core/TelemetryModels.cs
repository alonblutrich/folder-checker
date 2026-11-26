namespace FolderScanner;

// In-memory, immutable aggregate views. No paths, file content or persistent history.
public sealed record TelemetrySample(double Seconds,int Discovered,int Submitted,int Acknowledged,double? ResponsesPerSecond);
public sealed record TelemetryBucket(string Label,long Count);
public sealed record ScanTelemetrySnapshot(
    TimeSpan Elapsed,
    IReadOnlyList<TelemetrySample> Timeline,
    IReadOnlyList<TelemetryBucket> Extensions,
    IReadOnlyList<TelemetryBucket> SizeHistogram,
    IReadOnlyList<TelemetryBucket> Outcomes,
    long? KnownBytes,int KnownSizeFiles,int UnknownSizeFiles,
    int Discovered,int Submitted,int Acknowledged,int Findings,int Issues,
    bool IsFinal,bool Cancelled,bool Partial,string Phase);
