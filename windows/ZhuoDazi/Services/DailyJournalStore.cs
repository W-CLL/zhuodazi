using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

/// <summary>Durable local journal. Dates are captured with the entry and never regrouped by the current time zone.</summary>
public sealed class DailyJournalStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly object _sync;
    private IReadOnlyList<DailyJournalEntry> _entries = Array.AsReadOnly<DailyJournalEntry>([]);

    public DailyJournalStore(string dataDirectory, string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var scopeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scopeId))).ToLowerInvariant();
        _filePath = Path.Combine(Path.GetFullPath(dataDirectory), $"daily-journal-{scopeHash}.json");
        _sync = FileLocks.GetOrAdd(_filePath, _ => new object());
        lock (_sync) Adopt(ReadDocument());
    }

    public IReadOnlyList<DailyJournalEntry> Entries
    {
        get { lock (_sync) return _entries; }
    }

    public event Action? Changed;

    public DailyJournalEntry RecordMood(string mood, DateTimeOffset? at = null, string? note = null, string? eventId = null)
        => Add(NewEntry("mood", at, eventId) with { Mood = NormalizeMood(mood), Note = NormalizeNote(note) });

    public DailyJournalEntry RecordQuiz(string contentId, bool correct, DateTimeOffset? at = null, string? eventId = null)
        => Add(NewEntry("quiz", at, eventId) with { ContentId = RequiredText(contentId, nameof(contentId), 128), Correct = correct });

    public DailyJournalEntry RecordDaily(string scenario, string choice, DateTimeOffset? at = null, string? eventId = null)
        => Add(NewEntry("daily", at, eventId) with
        {
            Scenario = RequiredText(scenario, nameof(scenario), 80),
            Choice = RequiredText(choice, nameof(choice), 200)
        });

    public bool UpdateMood(string id, string mood, string? note = null)
    {
        id = RequiredText(id, nameof(id), 128);
        var normalizedMood = NormalizeMood(mood);
        var normalizedNote = NormalizeNote(note);
        bool changed;
        lock (_sync)
        {
            var document = ReadDocument();
            var index = document.Entries.FindIndex(item => item.Id == id);
            if (index < 0) return false;
            var previous = document.Entries[index];
            if (previous.Kind != "mood") throw new InvalidOperationException("只能修改心情记录。");
            var next = previous with { Mood = normalizedMood, Note = normalizedNote };
            changed = next != previous;
            if (changed)
            {
                document.Entries[index] = next;
                SaveDocument(document);
            }
            Adopt(document);
        }
        if (changed) Changed?.Invoke();
        return true;
    }

    public bool Delete(string id)
    {
        id = RequiredText(id, nameof(id), 128);
        lock (_sync)
        {
            var document = ReadDocument();
            if (document.Entries.RemoveAll(item => item.Id == id) == 0) return false;
            // Keep only the identifier so replaying a deleted event cannot restore its content.
            document.DeletedIds.Add(id);
            SaveDocument(document);
            Adopt(document);
        }
        Changed?.Invoke();
        return true;
    }

    public DailyJournalSummary GetSummary(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "结束日期不能早于开始日期。");
        lock (_sync) return Summarize(_entries.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to));
    }

    public DailyJournalSummary GetAllTimeSummary()
    {
        lock (_sync) return Summarize(_entries);
    }

    private DailyJournalEntry Add(DailyJournalEntry entry)
    {
        lock (_sync)
        {
            // Reload inside the per-file lock so another store instance cannot lose an earlier write.
            var document = ReadDocument();
            var existing = document.Entries.FirstOrDefault(item => item.Id == entry.Id);
            if (existing is not null)
            {
                Adopt(document);
                return existing;
            }
            if (document.DeletedIds.Contains(entry.Id))
                throw new InvalidOperationException("这条日常记录已删除，不能重复写入相同事件。");
            document.Entries.Add(entry);
            SaveDocument(document);
            Adopt(document);
        }
        Changed?.Invoke();
        return entry;
    }

    private static DailyJournalEntry NewEntry(string kind, DateTimeOffset? at, string? eventId)
    {
        var timestamp = at ?? DateTimeOffset.Now;
        return new DailyJournalEntry
        {
            Id = eventId is null ? Guid.NewGuid().ToString() : RequiredText(eventId, nameof(eventId), 128),
            Kind = kind,
            OccurredAt = timestamp,
            LocalDate = DateOnly.FromDateTime(timestamp.DateTime)
        };
    }

    private static DailyJournalSummary Summarize(IEnumerable<DailyJournalEntry> source)
    {
        var entries = source.ToArray();
        var moods = entries.Where(item => item.Kind == "mood").ToArray();
        var counts = DailyMoodCatalog.Options.ToDictionary(item => item.Code, _ => 0, StringComparer.Ordinal);
        foreach (var entry in moods)
            counts[entry.Mood!] = counts.GetValueOrDefault(entry.Mood!) + 1;
        return new DailyJournalSummary
        {
            TotalInteractions = entries.Length,
            QuizzesAnswered = entries.Count(item => item.Kind == "quiz"),
            QuizzesCorrect = entries.Count(item => item.Kind == "quiz" && item.Correct == true),
            MoodRecords = moods.Length,
            MoodDays = moods.Select(item => item.LocalDate).Distinct().Count(),
            InteractionDays = entries.Select(item => item.LocalDate).Distinct().Count(),
            MoodCounts = new ReadOnlyDictionary<string, int>(counts)
        };
    }

    private JournalDocument ReadDocument()
    {
        try
        {
            // File.Exists would also hide some access errors and must not decide that a journal is empty.
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = JsonSerializer.Deserialize<JournalDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException("日常记录文件为空或格式无效。");
            ValidateDocument(document);
            return document;
        }
        catch (FileNotFoundException) { return new JournalDocument(); }
        catch (DirectoryNotFoundException) { return new JournalDocument(); }
        catch (JsonException error)
        {
            throw new InvalidDataException("日常记录文件已损坏。原文件已保留，请先备份后恢复。", error);
        }
    }

    private void SaveDocument(JournalDocument document)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Adopt(JournalDocument document)
        => _entries = Array.AsReadOnly(document.Entries.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray());

    private static void ValidateDocument(JournalDocument document)
    {
        if (document.Version != 1 || document.Entries is null || document.DeletedIds is null)
            throw new InvalidDataException("日常记录版本或结构无效，原文件已保留。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            if (entry is null || !IsText(entry.Id, 128) || !ids.Add(entry.Id)
                || entry.LocalDate != DateOnly.FromDateTime(entry.OccurredAt.DateTime)
                || entry.Note?.Length > 2000)
                throw new InvalidDataException("日常记录包含损坏或重复的数据，原文件已保留。");
            var valid = entry.Kind switch
            {
                "mood" => DailyMoodCatalog.IsValid(entry.Mood) && entry.Mood == DailyMoodCatalog.Find(entry.Mood)!.Code,
                "quiz" => IsText(entry.ContentId, 128) && entry.Correct.HasValue,
                "daily" => IsText(entry.Scenario, 80) && IsText(entry.Choice, 200),
                _ => false
            };
            if (!valid) throw new InvalidDataException("日常记录内容无效，原文件已保留。");
        }
        foreach (var id in document.DeletedIds)
            if (!IsText(id, 128) || !ids.Add(id))
                throw new InvalidDataException("日常记录删除标记无效，原文件已保留。");
    }

    private static string NormalizeMood(string mood)
        => DailyMoodCatalog.Find(mood)?.Code ?? throw new ArgumentException("无法识别这个心情选项。", nameof(mood));

    private static string? NormalizeNote(string? note)
    {
        note = note?.Trim();
        if (note?.Length > 2000) throw new ArgumentException("心情备注最多 2000 字。", nameof(note));
        return string.IsNullOrEmpty(note) ? null : note;
    }

    private static string RequiredText(string value, string name, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        value = value.Trim();
        if (!IsText(value, maximum)) throw new ArgumentException($"内容最多 {maximum} 字。", name);
        return value;
    }

    private static bool IsText(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    private sealed class JournalDocument
    {
        [JsonRequired] public int Version { get; set; } = 1;
        [JsonRequired] public List<DailyJournalEntry> Entries { get; set; } = [];
        [JsonRequired] public List<string> DeletedIds { get; set; } = [];
    }
}
