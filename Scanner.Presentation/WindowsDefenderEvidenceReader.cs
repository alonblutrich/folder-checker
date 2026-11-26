using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FolderScanner;

namespace ScannerPresentation;

// Read-only, local provider observation, never a remediation command. Interpretation of
// action/status and run correlation remains WindowsTreatmentMatcher's responsibility.
public sealed class WindowsDefenderEvidenceReader : IWindowsDefenderEvidenceReader
{
    const int MaximumTargets = 64, MaximumRecords = 128, MaximumOutputBytes = 256 * 1024;
    const string RequestVariable = "FOLDER_SCANNER_DEFENDER_QUERY";
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    static readonly string[] ResourcePrefixes = ["file:_", "containerfile:_", "file:", "containerfile:"];
    static readonly JsonSerializerOptions RequestJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Static source is reviewable here. Only JSON data travels in the child environment;
    // target paths never become PowerShell source or WQL. No remote session is created.
    const string Script = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $WarningPreference = 'Stop'
        $InformationPreference = 'SilentlyContinue'
        Set-StrictMode -Version Latest
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        try {
            Import-Module (Join-Path $PSHOME 'Modules\CimCmdlets\CimCmdlets.psd1') -ErrorAction Stop
            $q = $env:FOLDER_SCANNER_DEFENDER_QUERY | ConvertFrom-Json -ErrorAction Stop
            if (@($q.paths).Count -lt 1 -or @($q.paths).Count -gt 64 -or
                @($q.processPaths).Count -lt 1 -or @($q.processPaths).Count -gt 64) { throw 'Scope limit' }
            $culture = [Globalization.CultureInfo]::InvariantCulture
            $start = [DateTimeOffset]::Parse($q.started, $culture)
            $end = [DateTimeOffset]::Parse($q.ended, $culture)
            if ($end -lt $start) { throw 'Time window' }
            $resources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($path in $q.paths) {
                foreach ($prefix in @('file:_','containerfile:_','file:','containerfile:')) {
                    [void]$resources.Add($prefix + $path)
                }
            }
            $processes = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($path in $q.processPaths) { [void]$processes.Add($path) }
            $from = $start.UtcDateTime.ToString('yyyyMMddHHmmss.ffffff', $culture) + '+000'
            $until = $end.UtcDateTime.ToString('yyyyMMddHHmmss.ffffff', $culture) + '+000'
            $filter = "InitialDetectionTime >= '$from' AND InitialDetectionTime <= '$until'"
            $rows = [Collections.Generic.List[object]]::new()
            $seen = 0
            # InitialDetectionTime is filtered at the provider, not an all-history dump.
            # Bound inspected provider records as well as returned matching records.
            CimCmdlets\Get-CimInstance -Namespace 'Root\Microsoft\Windows\Defender' -ClassName 'MSFT_MpThreatDetection' -Filter $filter -OperationTimeoutSec 5 -Property DetectionID,ThreatID,ProcessName,Resources,InitialDetectionTime,RemediationTime,ActionSuccess,ThreatStatusID,ThreatStatusErrorCode,AdditionalActionsBitMask -ErrorAction Stop | ForEach-Object {
                $d = $_
                $seen++
                if ($seen -gt 128) { throw 'Record limit' }
                if (-not $processes.Contains([string]$d.ProcessName)) { return }
                $matching = [Collections.Generic.List[string]]::new()
                $resourceCount = 0
                foreach ($resource in $d.Resources) {
                    $resourceCount++
                    if ($resourceCount -gt 128) { throw 'Resource limit' }
                    if ($resource -is [string] -and $resources.Contains($resource)) { $matching.Add($resource) }
                }
                if ($matching.Count -eq 0) { return }
                if ($null -eq $d.InitialDetectionTime -or $null -eq $d.ThreatID -or
                    $null -eq $d.ActionSuccess -or $null -eq $d.ThreatStatusID -or
                    $null -eq $d.ThreatStatusErrorCode -or $d.ActionSuccess -isnot [bool]) { throw 'Incomplete provider data' }
                $detected = [DateTimeOffset]$d.InitialDetectionTime
                if ($detected -lt $start -or $detected -gt $end) { throw 'Provider time filter mismatch' }
                $remediated = $null
                if ($null -ne $d.RemediationTime) { $remediated = ([DateTimeOffset]$d.RemediationTime).ToUniversalTime().ToString('o', $culture) }
                $additionalActions = $null
                if ($null -ne $d.AdditionalActionsBitMask) { $additionalActions = [uint32]$d.AdditionalActionsBitMask }
                $rows.Add([pscustomobject]@{
                    detectionId = [string]$d.DetectionID
                    threatId = [long]$d.ThreatID
                    threatName = $null
                    initialDetectionTime = $detected.ToUniversalTime().ToString('o', $culture)
                    remediationTime = $remediated
                    actionSuccess = [bool]$d.ActionSuccess
                    threatStatusId = [int]$d.ThreatStatusID
                    threatStatusErrorCode = [int]$d.ThreatStatusErrorCode
                    processName = [string]$d.ProcessName
                    resources = $matching.ToArray()
                    additionalActionsBitMask = $additionalActions
                })
            }
            # Names are looked up only for IDs of already time/resource/process-filtered rows.
            if ($rows.Count -gt 0) {
                $ids = [Collections.Generic.HashSet[long]]::new()
                foreach ($row in $rows) {
                    if ($row.threatId -le 0) { throw 'Invalid threat ID' }
                    [void]$ids.Add($row.threatId)
                }
                $idFilters = foreach ($id in $ids) { 'ThreatID = ' + $id.ToString($culture) }
                $names = @{}
                $nameCount = 0
                CimCmdlets\Get-CimInstance -Namespace 'Root\Microsoft\Windows\Defender' -ClassName 'MSFT_MpThreat' -Filter ($idFilters -join ' OR ') -OperationTimeoutSec 5 -Property ThreatID,ThreatName -ErrorAction Stop | ForEach-Object {
                    $nameCount++
                    if ($nameCount -gt 128 -or -not $ids.Contains([long]$_.ThreatID)) { throw 'Name lookup scope' }
                    $name = [string]$_.ThreatName
                    if ($name.Length -gt 512) { throw 'Name limit' }
                    $names[([long]$_.ThreatID).ToString($culture)] = $name
                }
                foreach ($row in $rows) {
                    $key = $row.threatId.ToString($culture)
                    if ($names.ContainsKey($key)) { $row.threatName = $names[$key] }
                }
            }
            $payload = [pscustomobject]@{ runId = [string]$q.runId; available = $true; detections = $rows.ToArray() } | ConvertTo-Json -Depth 6 -Compress
            if ([Text.Encoding]::UTF8.GetByteCount($payload) -gt 262144) { throw 'Output limit' }
            [Console]::Out.Write($payload)
            exit 0
        } catch {
            # Do not leak unrelated provider records, targets or raw provider error text.
            [Console]::Error.Write('Defender evidence unavailable')
            exit 1
        }
        """;

    public async Task<DefenderEvidenceBatch> ReadAsync(DefenderEvidenceQuery query, CancellationToken cancellation)
    {
        if (!ValidQuery(query)) return Unavailable("InvalidOrExcessiveScope");
        if (!OperatingSystem.IsWindows()) return Unavailable("WindowsProviderRequired");
        if (cancellation.IsCancellationRequested) return Unavailable("Cancelled");
        // Freeze caller-owned arrays before any asynchronous operation.
        query = query with { Paths = [.. query.Paths], ProcessPaths = [.. query.ProcessPaths] };
        string request = JsonSerializer.Serialize(query, RequestJson);
        // Windows limits a single environment variable. Refuse instead of silently
        // dropping long paths or targets to make a request fit.
        if (request.Length > 24000) return Unavailable("RequestTooLarge");
        string executable = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(executable)) return Unavailable("WindowsPowerShellUnavailable");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Environment.SystemDirectory
        };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(arg);
        start.Environment[RequestVariable] = request;
        start.Environment["PSModulePath"] = Path.Combine(Path.GetDirectoryName(executable)!, "Modules");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Deadline);
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };
        Task? pending = null;
        try
        {
            if (!process.Start()) return Unavailable("ProviderProcessUnavailable");
            var stdout = ReadBoundedAsync(process.StandardOutput.BaseStream, MaximumOutputBytes, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError.BaseStream, 8192, deadline.Token);
            pending = Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
            await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || stderr.Result.Length != 0) return Unavailable("ProviderUnavailable");
            var result = ParseResponse(query, stdout.Result, DateTimeOffset.UtcNow);
            return deadline.IsCancellationRequested || clock.Elapsed > Deadline
                ? Unavailable(cancellation.IsCancellationRequested ? "Cancelled" : "ProviderTimeout") : result;
        }
        catch (OperationCanceledException)
        { return Unavailable(cancellation.IsCancellationRequested ? "Cancelled" : "ProviderTimeout"); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        { return Unavailable("ProviderUnavailableOrOutputLimit"); }
        finally
        {
            // Only this read-only helper process is stopped. Never stop Defender, a
            // scanner process, its descendants or a shared protection operation.
            try { if (process.Id != 0 && !process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            if (pending != null)
                _ = pending.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    // Pure parsing used by ReadAsync and managed checks; it never starts a process.
    public static DefenderEvidenceBatch ParseResponse(DefenderEvidenceQuery query, byte[] payload, DateTimeOffset observedAt)
    {
        DefenderEvidenceBatch Fail() => new(false, [], observedAt, "InvalidProviderPayload");
        if (!ValidQuery(query) || payload == null || payload.Length == 0 || payload.Length > MaximumOutputBytes) return Fail();
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            if (root.GetProperty("runId").GetString() != query.RunId || !root.GetProperty("available").GetBoolean()) return Fail();
            var rows = root.GetProperty("detections");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > MaximumRecords) return Fail();
            var resources = new HashSet<string>(query.Paths.SelectMany(path => ResourcePrefixes.Select(prefix => prefix + path)), StringComparer.OrdinalIgnoreCase);
            var processes = new HashSet<string>(query.ProcessPaths, StringComparer.OrdinalIgnoreCase);
            var detections = new List<DefenderDetection>();
            foreach (var row in rows.EnumerateArray())
            {
                string id = row.GetProperty("detectionId").GetString() ?? "";
                long threatId = row.GetProperty("threatId").GetInt64();
                string? name = row.GetProperty("threatName").GetString();
                DateTimeOffset detected = row.GetProperty("initialDetectionTime").GetDateTimeOffset();
                var remediation = row.GetProperty("remediationTime");
                DateTimeOffset? treated = remediation.ValueKind == JsonValueKind.Null ? null : remediation.GetDateTimeOffset();
                bool success = row.GetProperty("actionSuccess").GetBoolean();
                int status = row.GetProperty("threatStatusId").GetInt32(), error = row.GetProperty("threatStatusErrorCode").GetInt32();
                string processName = row.GetProperty("processName").GetString() ?? "";
                var rawResources = row.GetProperty("resources");
                uint? additionalActions = row.TryGetProperty("additionalActionsBitMask", out var mask) && mask.ValueKind != JsonValueKind.Null
                    ? mask.GetUInt32() : null;
                if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || threatId <= 0 || name?.Length > 512 ||
                    detected < query.Started || detected > query.Ended || !processes.Contains(processName) ||
                    rawResources.ValueKind != JsonValueKind.Array || rawResources.GetArrayLength() is < 1 or > 128) return Fail();
                var matchingResources = new List<string>();
                foreach (var resource in rawResources.EnumerateArray())
                {
                    string? value = resource.GetString();
                    if (value == null || !resources.Contains(value)) return Fail();
                    matchingResources.Add(value);
                }
                detections.Add(new(id, threatId, name, detected, treated, success, status, error, processName, matchingResources.ToArray(), additionalActions));
            }
            return new(true, detections.ToArray(), observedAt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        { return Fail(); }
    }

    static bool ValidQuery(DefenderEvidenceQuery? query)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.RunId) || query.RunId.Length > 128 || query.Ended < query.Started ||
            query.Paths == null || query.ProcessPaths == null || query.Paths.Length is < 1 or > MaximumTargets || query.ProcessPaths.Length is < 1 or > MaximumTargets) return false;
        static bool OrdinaryLocalPath(string? path) => path is { Length: >= 4 and <= 32767 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
            !path[2..].Contains(':') && !path.Contains('\0') && !path.Contains('\r') && !path.Contains('\n');
        return query.Paths.All(OrdinaryLocalPath) && query.ProcessPaths.All(OrdinaryLocalPath);
    }

    static DefenderEvidenceBatch Unavailable(string error) => new(false, [], DateTimeOffset.UtcNow, error);
    static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellation)
    {
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum) throw new InvalidDataException("Provider output exceeded limit.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
}
