namespace FolderScanner;

// Single scan-worker writer. Only fixed aggregates and a bounded event history are retained.
public sealed class ScanTelemetryCollector
{
    readonly int maximum;
    readonly List<TelemetrySample> samples;
    readonly long[] extensions = new long[7], sizes = new long[6], outcomes = new long[4];
    static readonly string[] ExtensionLabels = [".txt/.md", ".pdf", ".jpg/.png/.jpeg", ".zip/.7z/.rar", ".exe/.dll", "ללא סיומת", "Other"];
    static readonly string[] SizeLabels = ["0 B", "1 B–<1 KiB", "1–<64 KiB", "64 KiB–<1 MiB", "1–<16 MiB", ">=16 MiB"];
    static readonly string[] OutcomeLabels = ["NoThreatsReported", "Threat", "Unknown", "Error"];
    int discovered, submitted, acknowledged, findings, issues, known, unknown;
    long bytes;
    bool bytesOverflow;
    TimeSpan lastElapsed;
    ScanTelemetrySnapshot? final;

    public ScanTelemetryCollector(int maxSamples = 192)
    {
        if (maxSamples < 3) throw new ArgumentOutOfRangeException(nameof(maxSamples), "At least first, latest and final samples are required.");
        maximum = maxSamples;
        samples = new List<TelemetrySample>(Math.Min(maxSamples, 192));
    }

    public void ObserveDiscovered(string path, long? sizeBytes, TimeSpan elapsed)
    {
        if (final != null) return;
        discovered = Increment(discovered);
        // No path is retained, and suffix classification never opens the file.
        var suffix = Path.GetExtension(path).ToLowerInvariant();
        int category = suffix switch
        {
            ".txt" or ".md" => 0, ".pdf" => 1, ".jpg" or ".png" or ".jpeg" => 2,
            ".zip" or ".7z" or ".rar" => 3, ".exe" or ".dll" => 4, "" => 5, _ => 6
        };
        extensions[category]++;
        if (sizeBytes is long length && length >= 0)
        {
            known = Increment(known);
            sizes[length == 0 ? 0 : length < 1024 ? 1 : length < 65536 ? 2 : length < 1048576 ? 3 : length < 16777216 ? 4 : 5]++;
            if (!bytesOverflow)
            {
                if (length > long.MaxValue - bytes) bytesOverflow = true;
                else bytes += length;
            }
        }
        else unknown = Increment(unknown);
        AddEvent(elapsed);
    }

    public void ObserveResult(FileResult result, TimeSpan elapsed)
    {
        if (final != null) return;
        outcomes[OutcomeIndex(result.Verdict)]++;
        if (result.SubmittedToEngine) submitted = Increment(submitted);
        if (result.Verdict is Verdict.NoThreatsReported or Verdict.Threat) acknowledged = Increment(acknowledged);
        if (result.Verdict == Verdict.Threat) findings = Increment(findings);
        AddEvent(elapsed);
    }

    public void ObserveIssue(TimeSpan elapsed)
    {
        if (final != null) return;
        issues = Increment(issues);
        AddEvent(elapsed);
    }

    public ScanTelemetrySnapshot Snapshot(TimeSpan elapsed, string phase) => final ?? CreateSnapshot(Normalize(elapsed), phase, false, false, false);

    public ScanTelemetrySnapshot Complete(ScanReport report, TimeSpan elapsed)
    {
        if (final != null) return final;
        discovered = report.DiscoveredFiles;
        submitted = report.SubmittedFiles;
        acknowledged = findings = 0;
        issues = report.Issues.Count;
        Array.Clear(outcomes);
        foreach (var result in report.Results)
        {
            outcomes[OutcomeIndex(result.Verdict)]++;
            if (result.Verdict is Verdict.NoThreatsReported or Verdict.Threat) acknowledged++;
            if (result.Verdict == Verdict.Threat) findings++;
        }
        AddEvent(elapsed);
        return final = CreateSnapshot(lastElapsed, report.State.ToString(), true, report.CancelRequested, !report.CoverageComplete);
    }

    static int Increment(int value) => value == int.MaxValue ? value : value + 1;
    static int OutcomeIndex(Verdict verdict) => verdict switch
    {
        Verdict.NoThreatsReported => 0, Verdict.Threat => 1, Verdict.Unknown => 2, _ => 3
    };
    TimeSpan Normalize(TimeSpan elapsed) => elapsed < lastElapsed ? lastElapsed : elapsed;

    void AddEvent(TimeSpan elapsed)
    {
        lastElapsed = Normalize(elapsed);
        // Decimation keeps real observations, the first event, and the previous latest.
        // Recalculate rates when copying, using the new retained time intervals.
        if (samples.Count == maximum)
        {
            int destination = 1;
            for (int source = 2; source < samples.Count - 1; source += 2)
                samples[destination++] = samples[source];
            samples[destination++] = samples[^1];
            samples.RemoveRange(destination, samples.Count - destination);
            // With capacity three, keep first + latest before appending the final/event.
        }
        samples.Add(new(lastElapsed.TotalSeconds, discovered, submitted, acknowledged, null));
    }

    ScanTelemetrySnapshot CreateSnapshot(TimeSpan elapsed, string phase, bool isFinal, bool cancelled, bool partial)
    {
        var timeline = samples.ToArray();
        for (int i = 1; i < timeline.Length; i++)
        {
            var previous = timeline[i - 1];
            double seconds = timeline[i].Seconds - previous.Seconds;
            int delta = timeline[i].Acknowledged - previous.Acknowledged;
            timeline[i] = timeline[i] with { ResponsesPerSecond = seconds > 0 && delta >= 0 ? delta / seconds : null };
        }
        return new(elapsed, Array.AsReadOnly(timeline), Buckets(ExtensionLabels, extensions),
            Buckets(SizeLabels, sizes), Buckets(OutcomeLabels, outcomes),
            known == 0 || bytesOverflow ? null : bytes, known, unknown,
            discovered, submitted, acknowledged, findings, issues, isFinal, cancelled, partial, phase);
    }

    static IReadOnlyList<TelemetryBucket> Buckets(string[] labels, long[] counts) =>
        Array.AsReadOnly(labels.Select((label, i) => new TelemetryBucket(label, counts[i])).ToArray());
}
