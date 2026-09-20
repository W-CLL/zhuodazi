using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZhuoDazi.Services;

// A receipt stays pending until presentation finishes. Completed receipts remain as short-lived
// tombstones so a failed HTTP acknowledge can be retried without replaying the same delivery.
internal sealed class CompanionInboxStore
{
    private readonly object _gate = new();
    private readonly string _manifest;
    public string Identity { get; }
    public string DirectoryPath { get; }

    internal CompanionInboxStore(string root, string identity)
    {
        Identity = identity;
        DirectoryPath = Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant());
        _manifest = Path.Combine(DirectoryPath, "inbox.json");
    }

    internal string GifPath(string id) => Path.Combine(DirectoryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant() + ".gif");

    internal bool Contains(string id)
    {
        lock (_gate) return Load().Any(e => e.Visit.Id == id && (e.Completed || File.Exists(e.Visit.FilePath)));
    }

    internal bool IsCompleted(string id)
    {
        lock (_gate) return Load().Any(e => e.Visit.Id == id && e.Completed);
    }

    internal IReadOnlyList<CompanionVisit> Pending()
    {
        lock (_gate) return Load().Where(e => !e.Completed && File.Exists(e.Visit.FilePath)).OrderBy(e => e.ReceivedAt).Select(e => e.Visit).ToArray();
    }

    internal void SaveDownloaded(CompanionVisit visit)
    {
        lock (_gate)
        {
            var entries = Load();
            if (entries.Any(e => e.Visit.Id == visit.Id && (e.Completed || File.Exists(e.Visit.FilePath)))) return;
            entries.RemoveAll(e => e.Visit.Id == visit.Id);
            entries.Add(new Entry(visit, DateTimeOffset.UtcNow, false, null));
            Save(entries);
        }
    }

    internal void Complete(string id)
    {
        lock (_gate)
        {
            var entries = Load();
            var index = entries.FindIndex(e => e.Visit.Id == id);
            if (index < 0) return;
            var entry = entries[index];
            entries[index] = entry with { Completed = true, CompletedAt = DateTimeOffset.UtcNow };
            Save(entries); // The completion is durable before deleting the GIF.
            try { File.Delete(entry.Visit.FilePath); } catch { }
        }
    }

    internal void ImportPendingFrom(CompanionInboxStore source)
    {
        if (source.Identity == Identity) return;
        var pending = source.Pending();
        lock (_gate)
        {
            var entries = Load();
            Directory.CreateDirectory(DirectoryPath);
            foreach (var visit in pending)
            {
                if (entries.Any(e => e.Visit.Id == visit.Id && (e.Completed || File.Exists(e.Visit.FilePath)))) continue;
                entries.RemoveAll(e => e.Visit.Id == visit.Id);
                var destination = GifPath(visit.Id);
                File.Copy(visit.FilePath, destination, true);
                entries.Add(new Entry(visit with { FilePath = destination }, DateTimeOffset.UtcNow, false, null));
            }
            Save(entries);
        }
        foreach (var visit in pending) source.Complete(visit.Id);
    }

    private List<Entry> Load()
    {
        string serialized;
        try { serialized = File.ReadAllText(_manifest); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        try
        {
            var root = Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar;
            var entries = JsonSerializer.Deserialize<List<Entry>>(serialized)
                ?? throw new InvalidDataException("来访记录不完整，原文件已保留。");
            if (entries.Any(e => e?.Visit is null || string.IsNullOrWhiteSpace(e.Visit.Id) || string.IsNullOrWhiteSpace(e.Visit.FilePath)))
                throw new InvalidDataException("来访记录不完整，原文件已保留。");
            return entries
                .Where(e => Path.GetFullPath(e.Visit.FilePath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            // Never treat an unreadable manifest as an empty inbox: the next successful download
            // would otherwise overwrite already acknowledged but not-yet-presented deliveries.
            throw new InvalidDataException("来访记录暂时无法读取，原文件已保留。", error);
        }
    }

    private void Save(List<Entry> entries)
    {
        entries.RemoveAll(e => e.Completed && e.CompletedAt < DateTimeOffset.UtcNow.AddDays(-2));
        Directory.CreateDirectory(DirectoryPath);
        var temporary = _manifest + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
        File.Move(temporary, _manifest, true);
    }

    private sealed record Entry(CompanionVisit Visit, DateTimeOffset ReceivedAt, bool Completed, DateTimeOffset? CompletedAt);
}
