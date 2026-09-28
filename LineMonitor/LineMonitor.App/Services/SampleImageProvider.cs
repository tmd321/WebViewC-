using System.IO;

namespace LineMonitor.App.Services;

/// <summary>
/// Feeds the Mock data source real-looking defect thumbnails by scanning the sample
/// image folders on the Desktop (Test2/Test3), so the defect grid isn't empty during
/// UI development before any DB connection exists.
/// </summary>
public static class SampleImageProvider
{
    private static readonly Lazy<IReadOnlyList<string>> Files = new(() =>
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var roots = new[] { Path.Combine(desktop, "Test2"), Path.Combine(desktop, "Test3") };

        return roots
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.jpg"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    });

    public static string? GetSample(int index)
    {
        var files = Files.Value;
        return files.Count == 0 ? null : files[index % files.Count];
    }
}
