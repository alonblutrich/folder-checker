using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderScanner;
public static class ReportExporter
{
    public static string Serialize(ScanReport report) => JsonSerializer.Serialize(new {
        schemaVersion = 1, appVersion = "0.1.0", scope = "Local ordinary files via ScanType3 DisableRemediation; file exclusions ignored per engine contract; no guarantee for archive/ADS internals or atomic snapshot; no hostile concurrent writers supported",
        privacy = "The app does not upload files. Defender operates under Windows policies.", report
    }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });

    public static void SaveNew(ScanReport report, string path)
    {
        var full = Path.GetFullPath(path);
        PathPolicy.ValidateLocalDirectory(Path.GetDirectoryName(full)!);
        // CreateNew deliberately refuses overwrite, symlinks, and racing existing targets.
        using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false));
        writer.Write(Serialize(report));
    }
}
