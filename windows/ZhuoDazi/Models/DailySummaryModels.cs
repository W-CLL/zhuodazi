namespace ZhuoDazi.Models;

public sealed record DailySummaryConfig
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; }
    public DailySummaryWeeklySchedule Weekly { get; init; } = new();
    public DailySummaryMonthlySchedule Monthly { get; init; } = new();
    public IReadOnlyList<DailySummaryTemplate> Templates { get; init; } = [];
}

public sealed record DailySummaryWeeklySchedule
{
    public bool Enabled { get; init; }
    // Sunday = 0, Monday = 1, ... Friday = 5, Saturday = 6.
    public int Weekday { get; init; } = 5;
    public string Time { get; init; } = "18:00";
}

public sealed record DailySummaryMonthlySchedule
{
    public bool Enabled { get; init; }
    public string Time { get; init; } = "09:00";
}

public sealed record DailySummaryTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
}

public sealed record DailySummaryResult
{
    public bool Available { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    // Both ranges are half-open: [PeriodStart, PeriodEnd). Monthly ranges use midnight.
    public DateTimeOffset? PeriodStart { get; init; }
    public DateTimeOffset? PeriodEnd { get; init; }
    public DateTimeOffset? NextAvailableAt { get; init; }
    public string TemplateId { get; init; } = string.Empty;
}
