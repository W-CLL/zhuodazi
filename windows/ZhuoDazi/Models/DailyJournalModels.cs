using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Models;

public sealed record DailyJournalEntry
{
    [JsonRequired] public string Id { get; init; } = string.Empty;
    [JsonRequired] public string Kind { get; init; } = string.Empty;
    public string? Mood { get; init; }
    public string? ContentId { get; init; }
    public bool? Correct { get; init; }
    public string? Scenario { get; init; }
    public string? Choice { get; init; }
    [JsonRequired] public DateTimeOffset OccurredAt { get; init; }
    [JsonRequired] public DateOnly LocalDate { get; init; }
    public string? Note { get; init; }
}

public sealed record DailyJournalSummary
{
    public int TotalInteractions { get; init; }
    public int QuizzesAnswered { get; init; }
    public int QuizzesCorrect { get; init; }
    public int MoodRecords { get; init; }
    public int MoodDays { get; init; }
    public int InteractionDays { get; init; }
    public IReadOnlyDictionary<string, int> MoodCounts { get; init; }
        = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
}

public sealed record DailyMoodOption(string Code, string Label, string Reply);

public static class DailyMoodCatalog
{
    public static IReadOnlyList<DailyMoodOption> Options { get; } = Array.AsReadOnly<DailyMoodOption>(
    [
        new("happy", "开心", "听起来今天有点甜！这份开心，我陪你收好。"),
        new("okay", "一般", "平平常常也可以，我就在这儿陪你。"),
        new("bad", "糟糕", "今天不太顺也没关系，先让自己喘口气。"),
        new("cry", "想哭", "想哭就不用忍着。给你留个位置，我陪你待一会儿。"),
        new("tired", "疲惫", "辛苦啦。肩膀松一松，能休息一小会儿就歇一下。"),
        new("annoyed", "烦躁", "今天有点烦呀。先不用急着理顺，我陪你缓一缓。"),
        new("calm", "平静", "这样安安静静的时刻，也值得留住。"),
        new("hopeful", "有盼头", "有件事值得期待真好，希望它慢慢向你走来。")
    ]);

    private static readonly DailyMoodOption LegacyLow = new(
        "low", "低落", "先不用硬撑，我在这儿陪你一会儿。");

    public static DailyMoodOption? Find(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return normalized == LegacyLow.Code ? LegacyLow : Options.FirstOrDefault(item => item.Code == normalized);
    }

    public static bool IsValid(string? code) => Find(code) is not null;
    public static string GetLabel(string? code) => Find(code)?.Label ?? "未知心情";
    public static string GetReply(string? code) => Find(code)?.Reply ?? "我在这儿陪着你。";
}
