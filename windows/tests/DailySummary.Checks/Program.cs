using System.Net;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class Program
{
    private static int _assertions;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"deskpet-summary-checks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CheckWeeklyPeriods();
            CheckMonthlyPeriods();
            CheckActualRecords(Path.Combine(directory, "journal"));
            CheckTemplatesAndRotation();
            CheckConfiguration();
            await CheckRemoteConfiguration(Path.Combine(directory, "remote"));
            Console.WriteLine($"Daily summary checks passed ({_assertions} assertions). No app or external API was started.");
        }
        finally
        {
            DeskPetHttp.HandlerFactory = null;
            RemoteConfigService.PreviewSiteSettingsUri = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DailySummaryConfig Config() => new()
    {
        Enabled = true,
        Weekly = new() { Enabled = true, Weekday = 5, Time = "18:00" },
        Monthly = new() { Enabled = true, Time = "09:00" },
        Templates =
        [
            new() { Id = "weekly-a", Kind = "weekly", Enabled = true, Title = "后台周报 A", Body = "{periodStart}|{periodEnd}|{totalInteractions}" },
            new() { Id = "weekly-b", Kind = "weekly", Enabled = true, Title = "后台周报 B", Body = "{periodStart}|{periodEnd}|{totalInteractions}" },
            new() { Id = "monthly-a", Kind = "monthly", Enabled = true, Title = "后台月报 A", Body = "{periodStart}|{periodEnd}|{totalInteractions}" },
            new() { Id = "monthly-b", Kind = "monthly", Enabled = true, Title = "后台月报 B", Body = "{periodStart}|{periodEnd}|{totalInteractions}" }
        ]
    };

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value);
    private static DailyJournalEntry Mood(string time, string mood = "happy") => new()
    {
        Id = Guid.NewGuid().ToString(), Kind = "mood", Mood = mood,
        OccurredAt = At(time), LocalDate = DateOnly.FromDateTime(At(time).DateTime)
    };

    private static void CheckWeeklyPeriods()
    {
        var entries = new[]
        {
            Mood("2026-09-18T17:59:59+08:00"), Mood("2026-09-18T18:00:00+08:00"),
            Mood("2026-09-19T12:00:00+08:00"), Mood("2026-09-25T17:59:59+08:00"),
            Mood("2026-09-25T18:00:00+08:00")
        };
        var before = DailySummaryService.Build(Config(), entries, "weekly", 0, At("2026-09-25T17:59:59+08:00"));
        Require(before.PeriodStart == At("2026-09-11T18:00:00+08:00") && before.PeriodEnd == At("2026-09-18T18:00:00+08:00"),
            "Friday just before cutoff still shows the previous completed week.");
        Require(before.Body.EndsWith("|1") && before.NextAvailableAt == At("2026-09-25T18:00:00+08:00"),
            "The prior completed week excludes its exclusive end and provides the next cutoff.");
        var due = DailySummaryService.Build(Config(), entries, "weekly", 0, At("2026-09-25T18:00:00+08:00"));
        Require(due.Available && due.Body.EndsWith("|3") && due.PeriodStart == before.PeriodEnd,
            "At cutoff the new completed week includes its start and weekend, but excludes cutoff itself.");
        Require(due.NextAvailableAt == At("2026-10-02T18:00:00+08:00"), "The next available time advances exactly one week.");
        var saturday = DailySummaryService.Build(Config(), entries, "weekly", 0, At("2026-09-26T12:00:00+08:00"));
        Require(saturday.Body == due.Body && saturday.TemplateId == due.TemplateId, "The weekend does not regenerate another weekly period.");
        var previous = DailySummaryService.Build(Config(), entries, "weekly", 1, At("2026-09-25T18:00:00+08:00"));
        Require(previous.Body == before.Body && previous.TemplateId == before.TemplateId, "Past offset 1 consistently returns the earlier period.");
        var onlyCurrent = new[] { Mood("2026-09-24T12:00:00+08:00") };
        var waiting = DailySummaryService.Build(Config(), onlyCurrent, "weekly", 0, At("2026-09-24T13:00:00+08:00"));
        Require(!waiting.Available && waiting.Title == "" && waiting.Body == "" && waiting.Status.Contains("没有日常记录")
                && waiting.NextAvailableAt == At("2026-09-25T18:00:00+08:00"), "The first week is not summarized early or filled with example copy.");
        Require(!DailySummaryService.Build(Config(), entries, "weekly", -1, At("2026-09-25T18:00:00+08:00")).Available,
            "Negative offsets never expose a future period.");
        Require(!DailySummaryService.Build(Config(), entries, "weekly", int.MaxValue, At("2026-09-25T18:00:00+08:00")).Available,
            "An excessive past offset fails safely.");
        var sundayConfig = Config() with { Weekly = new() { Enabled = true, Weekday = 0, Time = "20:30" } };
        var sunday = DailySummaryService.Build(sundayConfig, entries, "weekly", 0, At("2026-09-27T20:30:00+08:00"));
        Require(sunday.PeriodEnd == At("2026-09-27T20:30:00+08:00") && sunday.PeriodStart == At("2026-09-20T20:30:00+08:00"),
            "A backend change to Sunday and its clock time changes the cutoff without a client release.");
    }

    private static void CheckMonthlyPeriods()
    {
        var entries = new[]
        {
            Mood("2026-08-31T23:59:59+08:00"), Mood("2026-09-01T00:00:00+08:00"),
            Mood("2026-09-30T23:59:59+08:00"), Mood("2026-10-01T00:00:00+08:00")
        };
        var before = DailySummaryService.Build(Config(), entries, "monthly", 0, At("2026-10-01T08:59:59+08:00"));
        Require(before.PeriodStart == At("2026-08-01T00:00:00+08:00") && before.Body.EndsWith("|1"),
            "Before the first-day release time the latest available report still covers August.");
        Require(before.NextAvailableAt == At("2026-10-01T09:00:00+08:00"), "A monthly report waits for its configured release time.");
        var due = DailySummaryService.Build(Config(), entries, "monthly", 0, At("2026-10-01T09:00:00+08:00"));
        Require(due.PeriodStart == At("2026-09-01T00:00:00+08:00") && due.PeriodEnd == At("2026-10-01T00:00:00+08:00")
            && due.Body == "2026-09-01|2026-09-30|2", "Monthly statistics cover the complete natural month, excluding the first-day release morning.");
        Require(due.NextAvailableAt == At("2026-11-01T09:00:00+08:00"), "The next report release uses the next month's first day.");
        var january = DailySummaryService.Build(Config(), [Mood("2026-12-31T23:59:59+08:00")], "monthly", 0, At("2027-01-01T09:00:00+08:00"));
        Require(january.Available && january.PeriodStart == At("2026-12-01T00:00:00+08:00"), "New year correctly reports the preceding December.");
        var leap = DailySummaryService.Build(Config(), [Mood("2024-02-29T23:59:59+08:00")], "monthly", 0, At("2024-03-01T09:00:00+08:00"));
        Require(leap.Available && leap.Body == "2024-02-01|2024-02-29|1", "Leap day belongs to the completed February.");
        var changedTime = Config() with { Monthly = new() { Enabled = true, Time = "12:15" } };
        var midday = DailySummaryService.Build(changedTime, entries, "monthly", 0, At("2026-10-01T12:14:00+08:00"));
        Require(midday.PeriodEnd == At("2026-09-01T00:00:00+08:00") && midday.NextAvailableAt == At("2026-10-01T12:15:00+08:00"),
            "Changing the monthly time in backend affects availability.");
    }

    private static void CheckActualRecords(string directory)
    {
        var store = new DailyJournalStore(directory, "summary-check");
        var edited = store.RecordMood("happy", At("2026-09-20T10:00:00+14:00"));
        store.RecordMood("happy", At("2026-09-20T11:00:00-12:00"));
        store.RecordMood("low", At("2026-09-20T12:00:00+08:00"));
        store.RecordQuiz("quiz-1", true, At("2026-09-21T12:00:00+08:00"));
        store.RecordQuiz("quiz-2", false, At("2026-09-21T13:00:00+08:00"));
        store.RecordDaily("offwork", "done", At("2026-09-21T18:00:00+08:00"));
        store.RecordDaily("offwork", "done", At("2026-09-21T18:01:00+08:00"));
        store.RecordDaily("offwork", "overtime", At("2026-09-22T18:00:00+08:00"));
        store.RecordDaily("offwork", "overtime", At("2026-09-22T18:01:00+08:00"));
        store.RecordDaily("offwork", "six", At("2026-09-23T17:00:00+08:00"));
        var template = Config().Templates[0] with
        {
            Body = "{interactionDays}|{totalInteractions}|{quizzesAnswered}|{quizzesCorrect}|{moodRecords}|{moodDays}|{moodSummary}|{happyCount}|{offWorkDays}|{overtimeDays}"
        };
        var config = Config() with { Templates = [template] };
        var now = At("2026-09-25T18:00:00+08:00");
        var summary = DailySummaryService.Build(config, store.Entries, "weekly", 0, now);
        Require(summary.Body == "4|10|2|1|3|1|开心 2 次、低落 1 次|2|1|1",
            "Counts differ from days; quizzes are separate; legacy low stays its own mood; 18:00 rescheduling is not classified as overtime.");
        store.UpdateMood(edited.Id, "cry");
        var afterEdit = DailySummaryService.Build(config, store.Entries, "weekly", 0, now);
        Require(afterEdit.TemplateId == summary.TemplateId && afterEdit.Body.Contains("开心 1 次、想哭 1 次、低落 1 次"),
            "Editing a mood updates real statistics without changing the period's selected template.");
        store.Delete(edited.Id);
        var afterDelete = DailySummaryService.Build(config, new DailyJournalStore(directory, "summary-check").Entries, "weekly", 0, now);
        Require(afterDelete.Body.StartsWith("4|9|2|1|2|1|"), "Deleted journal entries disappear after restart and from summary totals.");
        Require(DailySummaryService.Build(config, new DailyJournalStore(directory, "other-account").Entries, "weekly", 0, now).Body == "",
            "An account with no records receives no invented summary.");
        var zones = new[] { Mood("2026-09-18T18:00:00+14:00"), Mood("2026-09-25T17:59:59-12:00") };
        Require(DailySummaryService.Build(Config(), zones, "weekly", 0, now).Body.EndsWith("|2"),
            "Summary grouping preserves each entry's recorded local clock across later time-zone changes.");
    }

    private static void CheckTemplatesAndRotation()
    {
        var entries = Enumerable.Range(0, 90).Select(day => Mood(At("2026-07-01T12:00:00+08:00").AddDays(day).ToString("o"))).ToArray();
        var config = Config();
        var now = At("2026-09-25T18:00:00+08:00");
        var current = DailySummaryService.Build(config, entries, "weekly", 0, now);
        var prior = DailySummaryService.Build(config, entries, "weekly", 1, now);
        Require(current.TemplateId != prior.TemplateId, "Adjacent weeks rotate when multiple enabled templates are available.");
        var reordered = config with { Templates = config.Templates.Reverse().ToArray() };
        Require(DailySummaryService.Build(reordered, entries, "weekly", 0, now).Body == current.Body
            && DailySummaryService.Build(reordered, entries, "weekly", 0, now).TemplateId == current.TemplateId,
            "Backend array ordering and refresh cannot change the selected weekly template.");
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(config, JsonOptions));
        var restarted = DailySummaryService.ParseConfiguration(serialized.RootElement);
        Require(DailySummaryService.Build(restarted, entries, "weekly", 0, now).TemplateId == current.TemplateId,
            "Selection remains stable after serialization and client restart.");
        var month = DailySummaryService.Build(config, entries, "monthly", 0, At("2026-10-01T09:00:00+08:00"));
        var priorMonth = DailySummaryService.Build(config, entries, "monthly", 1, At("2026-10-01T09:00:00+08:00"));
        Require(month.TemplateId != priorMonth.TemplateId, "Adjacent months rotate their monthly templates.");
        foreach (var invalid in new[] { "{unknown}", "{{happyCount}}", "{happyCount", "<script>alert(1)</script>", "unsafe\u0000text" })
        {
            var bad = config.Templates[0] with { Body = invalid };
            Require(!DailySummaryService.IsValidTemplate(bad), "Unknown fields, malformed braces, HTML, and controls are invalid plain-text templates.");
            var invalidResult = DailySummaryService.Build(config with { Templates = [bad] }, entries, "weekly", 0, now);
            Require(!invalidResult.Available && invalidResult.Body == "", "Invalid templates cannot produce fallback prose or execute anything.");
        }
        Require(!DailySummaryService.IsValidTemplate(config.Templates[0] with { Id = "../template" }), "Template IDs use the backend ID whitelist.");
        Require(!DailySummaryService.IsValidTemplate(config.Templates[0] with { Title = new string('x', 81) })
            && !DailySummaryService.IsValidTemplate(config.Templates[0] with { Body = new string('x', 2001) }), "Client template bounds match the backend.");
        var disabled = config with { Templates = config.Templates.Select(item => item with { Enabled = false }).ToArray() };
        Require(!DailySummaryService.Build(disabled, entries, "weekly", 0, now).Available, "Disabled templates never render.");
    }

    private static void CheckConfiguration()
    {
        foreach (var value in new[] { "null", "{}", "{\"schemaVersion\":\"1\"}", "{\"schemaVersion\":2}", "{\"schemaVersion\":1,\"weekly\":null}", "{\"schemaVersion\":1,\"templates\":null}" })
        {
            using var document = JsonDocument.Parse(value);
            Require(DailySummaryService.ParseConfiguration(document.RootElement) is null, "Malformed or unsupported remote configuration is unavailable without throwing.");
        }
        var now = At("2026-09-25T18:00:00+08:00");
        Require(DailySummaryService.Build(null, [], "weekly", 0, now).Status.Contains("尚未在后台配置"), "Missing backend settings show a clear setup status.");
        Require(DailySummaryService.Build(Config() with { Enabled = false }, [], "weekly", 0, now).Status.Contains("关闭"), "The overall backend switch is respected.");
        Require(DailySummaryService.Build(Config() with { Weekly = new() }, [], "weekly", 0, now).Status.Contains("关闭"), "The per-kind backend switch is respected.");
        Require(!DailySummaryService.Build(Config() with { Weekly = new() { Enabled = true, Weekday = 7 } }, [], "weekly", 0, now).Available,
            "An out-of-range weekday cannot be silently interpreted.");
    }

    private static async Task CheckRemoteConfiguration(string directory)
    {
        RemoteConfigService.PreviewSiteSettingsUri = new Uri("http://127.0.0.1:18764/api/public/site-settings");
        var settings = new SettingsStore(directory);
        var remote = new RemoteConfigService(settings);
        Require(remote.Current.DailySummaries is null, "A fresh install does not include hardcoded client summary templates.");
        var json = JsonSerializer.Serialize(new { dailySummaries = Config(), announcement = "remote announcement" }, JsonOptions);
        Uri? requested = null;
        DeskPetHttp.HandlerFactory = () => new ResponseHandler(request =>
        {
            requested = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        Require(await remote.RefreshAsync() && remote.Current.DailySummaries?.Templates.Count == 4
            && requested == RemoteConfigService.PreviewSiteSettingsUri, "The loopback preview endpoint supplies real parsed summary settings.");
        Require(new RemoteConfigService(settings).Current.DailySummaries?.Templates.Count == 4,
            "Downloaded backend settings survive a client restart.");
        Require(!File.Exists(Path.Combine(directory, "remote-config.json")), "Local preview configuration never overwrites the production cache.");
        DeskPetHttp.HandlerFactory = () => new ResponseHandler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent(new string('x', 1024 * 1024 + 1)) });
        Require(!await remote.RefreshAsync() && remote.Current.DailySummaries?.Templates.Count == 4,
            "An oversized settings response is rejected without discarding the last valid configuration.");
        DeskPetHttp.HandlerFactory = () => new ResponseHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{}") });
        Require(await remote.RefreshAsync() && remote.Current.DailySummaries is null,
            "Removing summary settings on the backend clears them rather than preserving stale built-in copy.");
        foreach (var url in new[] { "https://example.com/api/public/site-settings", "file:///tmp/settings", "http://user@localhost/settings" })
        {
            var rejected = false;
            try { RemoteConfigService.PreviewSiteSettingsUri = new Uri(url); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Only loopback HTTP(S) endpoints without credentials can override production settings.");
        }
    }

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}

namespace ZhuoDazi.Services
{
    // RemoteConfig only needs a data directory; keeping this test double avoids a WPF GIF decoder dependency.
    internal sealed class SettingsStore(string dataDirectory)
    {
        public string DataDirectory { get; } = dataDirectory;
    }
}
