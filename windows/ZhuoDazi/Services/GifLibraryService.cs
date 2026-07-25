namespace ZhuoDazi.Services;

public sealed class GifLibraryService
{
    private readonly Random _random = new();

    public string BuiltInDirectory { get; } = Path.Combine(
        AppContext.BaseDirectory, "resources", "pet-libraries", "yuexinmiao");

    public IReadOnlyList<string> Scan(string? customDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(customDirectory) ? BuiltInDirectory : customDirectory;
        if (!Directory.Exists(directory)) return [];
        try
        {
            return Directory.EnumerateFiles(directory, "*.gif", SearchOption.AllDirectories)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                .Take(500)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public string? Pick(IReadOnlyList<string> files, string? excluded = null)
    {
        if (files.Count == 0) return null;
        var candidates = files.Where(item => !string.Equals(item, excluded, StringComparison.OrdinalIgnoreCase)).ToArray();
        var pool = candidates.Length > 0 ? candidates : files.ToArray();
        return pool[_random.Next(pool.Length)];
    }
}
