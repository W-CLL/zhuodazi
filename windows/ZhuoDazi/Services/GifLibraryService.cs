using System.Security.Cryptography;

namespace ZhuoDazi.Services;

public sealed class GifLibraryService
{
    private readonly List<string> _remaining = [];
    private string[] _pool = [];

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
        if (files.Count == 0)
        {
            _pool = [];
            _remaining.Clear();
            return null;
        }

        EnsurePool(files);
        if (TryTakeRandom(excluded, out var selected)) return selected;

        _remaining.AddRange(_pool);
        if (TryTakeRandom(excluded, out selected)) return selected;

        // A one-item library cannot avoid returning the current GIF.
        selected = _remaining[0];
        _remaining.RemoveAt(0);
        return selected;
    }

    private void EnsurePool(IReadOnlyList<string> files)
    {
        var nextPool = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_pool.SequenceEqual(nextPool, StringComparer.OrdinalIgnoreCase)) return;

        _pool = nextPool;
        _remaining.Clear();
        _remaining.AddRange(_pool);
    }

    private bool TryTakeRandom(string? excluded, out string? selected)
    {
        var candidateCount = 0;
        foreach (var item in _remaining)
        {
            if (!string.Equals(item, excluded, StringComparison.OrdinalIgnoreCase)) candidateCount++;
        }

        if (candidateCount == 0)
        {
            selected = null;
            return false;
        }

        var target = RandomNumberGenerator.GetInt32(candidateCount);
        for (var index = 0; index < _remaining.Count; index++)
        {
            if (string.Equals(_remaining[index], excluded, StringComparison.OrdinalIgnoreCase)) continue;
            if (target-- > 0) continue;
            selected = _remaining[index];
            _remaining.RemoveAt(index);
            return true;
        }

        selected = null;
        return false;
    }
}
