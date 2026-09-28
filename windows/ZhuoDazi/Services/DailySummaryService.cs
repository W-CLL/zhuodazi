using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

/// <summary>Renders server-authored, plain-text summaries of completed local calendar periods.</summary>
public static class DailySummaryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex Placeholder = new(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedPlaceholders = new(StringComparer.Ordinal)
    {
        "periodStart", "periodEnd", "interactionDays", "totalInteractions", "quizzesAnswered", "quizzesCorrect",
        "moodRecords", "moodDays", "moodSummary", "happyCount", "offWorkDays", "overtimeDays"
    };

    public static DailySummaryConfig? ParseConfiguration(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) return null;
        try
        {
            var config = element.Deserialize<DailySummaryConfig>(JsonOptions);
            return IsValidConfiguration(config) ? config : null;
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static DailySummaryResult Build(DailySummaryConfig? config, IEnumerable<DailyJournalEntry> entries,
        string kind, int offset, DateTimeOffset now)
    {
        if (kind is not ("weekly" or "monthly") || offset < 0)
            return new() { Status = "只能查看已经结束的周总结或月总结。" };
        if (!IsValidConfiguration(config))
            return new() { Status = "日常总结尚未在后台配置，请联网同步后再来看看。" };
        var label = kind == "weekly" ? "周总结" : "月总结";
        if (!config!.Enabled || (kind == "weekly" ? !config.Weekly.Enabled : !config.Monthly.Enabled))
            return new() { Status = $"{label}已在后台关闭。" };

        DailySummaryResult result;
        int periodIndex;
        try
        {
            (result, periodIndex) = CompletedPeriod(config, kind, offset, now);
        }
        catch (ArgumentOutOfRangeException) { return new() { Status = "已经到达可查看的历史范围。" }; }
        catch (OverflowException) { return new() { Status = "已经到达可查看的历史范围。" }; }

        var templates = config.Templates.Where(item => item.Enabled && item.Kind == kind && IsValidTemplate(item))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (templates.Length == 0)
            return result with { Status = $"后台尚未添加可用的{label}模板。" };

        // Journal dates belong to the local clock captured with each entry, even after a time-zone change.
        var start = result.PeriodStart!.Value.DateTime;
        var end = result.PeriodEnd!.Value.DateTime;
        var periodEntries = entries.Where(item => item.OccurredAt.DateTime >= start && item.OccurredAt.DateTime < end).ToArray();
        if (periodEntries.Length == 0)
            return result with { Status = "这个已结束的周期还没有日常记录。" };

        var template = templates[((periodIndex % templates.Length) + templates.Length) % templates.Length];
        var values = BuildValues(periodEntries, start, end, kind);
        return result with
        {
            Available = true,
            Status = "已生成",
            TemplateId = template.Id,
            Title = Render(template.Title, values),
            Body = Render(template.Body, values)
        };
    }

    public static bool IsValidTemplate(DailySummaryTemplate? template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.Id)
            || !Regex.IsMatch(template.Id, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.CultureInvariant)
            || template.Kind is not ("weekly" or "monthly")) return false;
        return IsValidTemplateText(template.Title, 80) && IsValidTemplateText(template.Body, 2000);
    }

    private static bool IsValidTemplateText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum
            || value.Contains('<') || value.Contains('>')
            || value.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'))) return false;
        foreach (Match match in Placeholder.Matches(value))
            if (!AllowedPlaceholders.Contains(match.Groups[1].Value)) return false;
        var remaining = Placeholder.Replace(value, string.Empty);
        return !remaining.Contains('{') && !remaining.Contains('}');
    }

    private static bool IsValidConfiguration(DailySummaryConfig? config)
        => config is { SchemaVersion: 1, Weekly: not null, Monthly: not null, Templates: not null }
            && config.Weekly.Weekday is >= 0 and <= 6
            && ParseTime(config.Weekly.Time, out _) && ParseTime(config.Monthly.Time, out _)
            && config.Templates.Count <= 40
            && config.Templates.All(template => template is not null)
            && config.Templates.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count() == config.Templates.Count;

    private static bool ParseTime(string? value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static (DailySummaryResult Result, int Index) CompletedPeriod(DailySummaryConfig config, string kind, int offset, DateTimeOffset now)
    {
        DateTimeOffset start;
        DateTimeOffset end;
        DateTimeOffset next;
        int index;
        if (kind == "weekly")
        {
            ParseTime(config.Weekly.Time, out var time);
            var daysSinceCutoff = ((int)now.DayOfWeek - config.Weekly.Weekday + 7) % 7;
            var latest = new DateTimeOffset(now.Date.AddDays(-daysSinceCutoff).Add(time.ToTimeSpan()), now.Offset);
            if (latest > now) latest = latest.AddDays(-7);
            next = latest.AddDays(7);
            end = latest.AddDays(checked(-7 * offset));
            start = end.AddDays(-7);
            index = DateOnly.FromDateTime(end.DateTime).DayNumber / 7;
        }
        else
        {
            ParseTime(config.Monthly.Time, out var time);
            var thisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            var release = thisMonth.Add(time.ToTimeSpan());
            var latest = now < release ? thisMonth.AddMonths(-1) : thisMonth;
            next = now < release ? release : thisMonth.AddMonths(1).Add(time.ToTimeSpan());
            end = latest.AddMonths(-offset);
            start = end.AddMonths(-1);
            index = start.Year * 12 + start.Month - 1;
        }
        return (new DailySummaryResult { PeriodStart = start, PeriodEnd = end, NextAvailableAt = next }, index);
    }

    private static Dictionary<string, string> BuildValues(DailyJournalEntry[] entries, DateTime start, DateTime end, string kind)
    {
        var moods = entries.Where(item => item.Kind == "mood").ToArray();
        var counts = moods.GroupBy(item => item.Mood ?? string.Empty).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var moodOrder = DailyMoodCatalog.Options.Select(item => item.Code).Append("low").ToArray();
        var moodSummary = string.Join("、", moodOrder.Where(code => counts.GetValueOrDefault(code) > 0)
            .Select(code => $"{DailyMoodCatalog.GetLabel(code)} {counts[code]} 次"));
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        var format = kind == "weekly" ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["periodStart"] = start.ToString(format, CultureInfo.InvariantCulture),
            ["periodEnd"] = (kind == "monthly" ? end.AddDays(-1) : end).ToString(format, CultureInfo.InvariantCulture),
            ["interactionDays"] = Number(entries.Select(item => item.LocalDate).Distinct().Count()),
            ["totalInteractions"] = Number(entries.Length),
            ["quizzesAnswered"] = Number(entries.Count(item => item.Kind == "quiz")),
            ["quizzesCorrect"] = Number(entries.Count(item => item.Kind == "quiz" && item.Correct == true)),
            ["moodRecords"] = Number(moods.Length),
            ["moodDays"] = Number(moods.Select(item => item.LocalDate).Distinct().Count()),
            ["moodSummary"] = string.IsNullOrEmpty(moodSummary) ? "未记录心情" : moodSummary,
            ["happyCount"] = Number(counts.GetValueOrDefault("happy")),
            ["offWorkDays"] = Number(entries.Where(item => item.Kind == "daily" && item.Scenario is "offwork" or "off_work" && item.Choice == "done").Select(item => item.LocalDate).Distinct().Count()),
            ["overtimeDays"] = Number(entries.Where(item => item.Kind == "daily" && item.Scenario is "offwork" or "off_work" && item.Choice == "overtime").Select(item => item.LocalDate).Distinct().Count())
        };
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> values)
        => Placeholder.Replace(template, match => values[match.Groups[1].Value]);
}
