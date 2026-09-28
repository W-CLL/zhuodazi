using System.Text.Json.Nodes;
using ZhuoDazi.Models;
using ZhuoDazi.Services;

internal static class Program
{
    private static int _assertions;

    private static void Main()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"zhuodazi-daily-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            CheckCatalog();
            CheckWeekAndMonthBoundaries(Path.Combine(testRoot, "dates"));
            CheckEditingDeletingAndIdempotency(Path.Combine(testRoot, "editing"));
            CheckScopeIsolation(Path.Combine(testRoot, "accounts"));
            CheckSharedScopeWrites(Path.Combine(testRoot, "shared"));
            CheckCorruptFilePreserved(Path.Combine(testRoot, "corrupt"));
            CheckWriteFailures(Path.Combine(testRoot, "errors"));
            CheckFrequencyPresets();
            CheckDailyRoutineSchedule();
            Console.WriteLine($"Daily journal checks passed ({_assertions} assertions). No app or production API was started.");
        }
        finally { Directory.Delete(testRoot, recursive: true); }
    }

    private static void CheckCatalog()
    {
        Require(DailyMoodCatalog.Options.Count == 8, "There must be eight new mood options.");
        Require(DailyMoodCatalog.Options.Select(item => item.Code).Distinct().Count() == 8, "Mood option identifiers must be unique.");
        Require(DailyMoodCatalog.Options.All(item => !string.IsNullOrWhiteSpace(item.Label) && !string.IsNullOrWhiteSpace(item.Reply)),
            "Every mood option needs a label and a response.");
        Require(DailyMoodCatalog.Find("low")?.Label == "低落" && DailyMoodCatalog.Options.All(item => item.Code != "low"),
            "Legacy low remains readable without becoming a new button.");
    }

    private static void CheckWeekAndMonthBoundaries(string directory)
    {
        var store = new DailyJournalStore(directory, "dates");
        store.RecordMood("bad", DateTimeOffset.Parse("2026-08-31T23:59:00-12:00"));
        store.RecordMood("happy", DateTimeOffset.Parse("2026-09-01T00:01:00+14:00"));
        store.RecordMood("happy", DateTimeOffset.Parse("2026-09-01T21:00:00+14:00"));
        store.RecordQuiz("september-last", true, DateTimeOffset.Parse("2026-09-30T23:59:00-12:00"));
        store.RecordMood("cry", DateTimeOffset.Parse("2026-10-01T00:01:00+14:00"));
        var september = store.GetSummary(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Require(september.TotalInteractions == 3 && september.MoodRecords == 2 && september.QuizzesAnswered == 1,
            "A month includes both boundary dates and uses each recorded local date, not UTC.");
        Require(september.MoodDays == 1 && september.InteractionDays == 2 && september.MoodCounts["happy"] == 2,
            "Multiple mood responses on one day count as records but only one mood day.");

        store.RecordDaily("workday", "done", DateTimeOffset.Parse("2026-09-20T23:59:00-07:00"));
        store.RecordMood("tired", DateTimeOffset.Parse("2026-09-21T00:01:00+09:00"));
        store.RecordQuiz("week-first", false, DateTimeOffset.Parse("2026-09-21T10:00:00+09:00"));
        store.RecordDaily("off_work", "overtime", DateTimeOffset.Parse("2026-09-27T23:59:00-07:00"));
        store.RecordMood("hopeful", DateTimeOffset.Parse("2026-09-28T00:01:00+09:00"));
        var week = store.GetSummary(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27));
        Require(week.TotalInteractions == 3 && week.InteractionDays == 2 && week.MoodDays == 1,
            "Weekly summaries include Monday through Sunday without shifting offset timestamps across the boundary.");
        Require(week.QuizzesAnswered == 1 && week.QuizzesCorrect == 0 && week.MoodCounts["tired"] == 1,
            "Daily responses do not count as quizzes or mood records.");
        Require(store.GetSummary(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31)).TotalInteractions == 0,
            "An empty period must remain empty.");
        Throws<ArgumentOutOfRangeException>(() => store.GetSummary(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)),
            "Reversed date intervals must be rejected.");

        var old = store.RecordMood("calm", DateTimeOffset.Parse("2024-02-29T22:00:00+08:00"));
        var restored = new DailyJournalStore(directory, "dates");
        Require(restored.Entries.Count == 11 && restored.Entries.Single(item => item.Id == old.Id).LocalDate == new DateOnly(2024, 2, 29),
            "Old months and the captured local date must survive restarting, without rolling retention.");
        var restoredWeek = restored.GetSummary(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27));
        Require(restoredWeek.TotalInteractions == week.TotalInteractions && restoredWeek.MoodDays == week.MoodDays,
            "Persisting and reloading must not change calendar grouping.");
    }

    private static void CheckEditingDeletingAndIdempotency(string directory)
    {
        var store = new DailyJournalStore(directory, "editing");
        var changes = 0;
        store.Changed += () => changes++;
        var mood = store.RecordMood("happy", DateTimeOffset.Parse("2026-09-24T10:30:00+08:00"), "  first note  ", "stable-mood");
        Require(changes == 1 && mood.Note == "first note", "Saving a mood emits one change and normalizes its note.");
        var duplicate = store.RecordMood("happy", DateTimeOffset.Parse("2026-09-25T12:00:00+08:00"), eventId: "stable-mood");
        Require(duplicate == mood && changes == 1 && store.Entries.Count == 1, "Replayed event identifiers must preserve the original record and count once.");
        var quiz = store.RecordQuiz("same-question", true, eventId: "stable-quiz");
        store.RecordQuiz("same-question", true, eventId: "stable-quiz");
        store.RecordQuiz("same-question", false, eventId: "other-answer");
        store.RecordDaily("off_work", "done", eventId: "stable-daily");
        store.RecordDaily("off_work", "done", eventId: "stable-daily");
        Require(changes == 4 && store.Entries.Count == 4, "Idempotency is by event ID, while intentionally answering the same question again counts separately.");

        var snapshot = store.Entries;
        Require(store.UpdateMood(mood.Id, "cry", "changed"), "An existing mood can be edited.");
        var edited = store.Entries.Single(item => item.Id == mood.Id);
        Require(edited.Id == mood.Id && edited.OccurredAt == mood.OccurredAt && edited.LocalDate == mood.LocalDate,
            "Editing a mood preserves its identity, time, and calendar date.");
        Require(snapshot.Single(item => item.Id == mood.Id).Mood == "happy" && edited.Mood == "cry", "Published entries must be immutable snapshots.");
        var summary = store.GetAllTimeSummary();
        Require(summary.TotalInteractions == 4 && summary.MoodRecords == 1 && summary.MoodCounts["happy"] == 0 && summary.MoodCounts["cry"] == 1,
            "Editing updates mood buckets without adding an interaction.");
        Require(summary.QuizzesAnswered == 2 && summary.QuizzesCorrect == 1, "Only quiz answers determine quiz statistics.");
        Require(store.UpdateMood(mood.Id, "cry", "changed") && changes == 5, "An unchanged edit does not announce another change.");
        Require(!store.UpdateMood("missing", "happy") && !store.Delete("missing") && changes == 5, "Missing edits and deletions have no effect.");
        Throws<InvalidOperationException>(() => store.UpdateMood(quiz.Id, "happy"), "Quiz records must not be transformed into moods.");
        Throws<ArgumentException>(() => store.RecordMood("not-a-mood"), "Unknown mood codes must be rejected.");

        Require(store.Delete(mood.Id) && !store.Delete(mood.Id), "Deleting is idempotent.");
        summary = new DailyJournalStore(directory, "editing").GetAllTimeSummary();
        Require(summary.TotalInteractions == 3 && summary.MoodRecords == 0 && summary.MoodDays == 0 && changes == 6,
            "Deletion persists and removes the original record from all summaries.");
        Throws<InvalidOperationException>(() => store.RecordMood("happy", eventId: mood.Id), "A replay must not resurrect a deliberately deleted record.");
        var legacy = store.RecordMood("low");
        Require(store.GetAllTimeSummary().MoodCounts["low"] == 1 && new DailyJournalStore(directory, "editing").Entries.Any(item => item.Id == legacy.Id),
            "A legacy low mood remains its own bucket across persistence.");
    }

    private static void CheckScopeIsolation(string directory)
    {
        const string firstScope = "account/private:alice@example.com";
        var first = new DailyJournalStore(directory, firstScope);
        first.RecordMood("happy", eventId: "same-event-id");
        var second = new DailyJournalStore(directory, "account/bob");
        Require(second.GetAllTimeSummary().TotalInteractions == 0, "Another account cannot see the first account's journal.");
        second.RecordMood("bad", eventId: "same-event-id");
        Require(new DailyJournalStore(directory, firstScope).GetAllTimeSummary().MoodCounts["happy"] == 1
            && new DailyJournalStore(directory, "account/bob").GetAllTimeSummary().MoodCounts["bad"] == 1,
            "An event identifier is isolated within its account scope.");
        var files = Directory.GetFiles(directory);
        Require(files.Length == 2 && files.All(path => Path.GetFileName(path).Length == "daily-journal-".Length + 64 + ".json".Length),
            "Scope files use fixed SHA-256 filenames.");
        Require(files.All(path => !Path.GetFileName(path).Contains("alice", StringComparison.OrdinalIgnoreCase)
            && !File.ReadAllText(path).Contains(firstScope, StringComparison.Ordinal)), "The raw scope identifier is not exposed in journal filenames or data.");
    }

    private static void CheckSharedScopeWrites(string directory)
    {
        var first = new DailyJournalStore(directory, "same-scope");
        var second = new DailyJournalStore(directory, "same-scope");
        Parallel.For(0, 20, index => (index % 2 == 0 ? first : second).RecordQuiz($"question-{index}", index % 2 == 0, eventId: $"event-{index}"));
        var loaded = new DailyJournalStore(directory, "same-scope");
        Require(loaded.GetAllTimeSummary().QuizzesAnswered == 20 && loaded.GetAllTimeSummary().QuizzesCorrect == 10,
            "Two store instances must not overwrite each other's additions.");
        Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "Successful atomic writes leave no temporary files.");
    }

    private static void CheckCorruptFilePreserved(string directory)
    {
        var store = new DailyJournalStore(directory, "corrupt");
        store.RecordMood("happy");
        var path = Directory.GetFiles(directory, "*.json").Single();
        var original = File.ReadAllText(path);
        File.WriteAllText(path, "{ broken");
        Throws<InvalidDataException>(() => new DailyJournalStore(directory, "corrupt"), "Malformed data must be reported instead of silently treated as an empty journal.");
        Throws<InvalidDataException>(() => store.RecordMood("bad"), "An existing instance must not overwrite a file that became corrupt.");
        Require(File.ReadAllText(path) == "{ broken" && store.Entries.Count == 1, "Corrupt disk data is preserved and failed writes do not change memory.");

        foreach (var invalid in new[] { "{}", "null", "{\"version\":2,\"entries\":[],\"deletedIds\":[]}" })
        {
            File.WriteAllText(path, invalid);
            Throws<InvalidDataException>(() => new DailyJournalStore(directory, "corrupt"), "Missing fields and unknown versions must not be silently rewritten.");
            Require(File.ReadAllText(path) == invalid, "Invalid data must remain untouched.");
        }
        var duplicate = JsonNode.Parse(original)!;
        duplicate["entries"]!.AsArray().Add(duplicate["entries"]![0]!.DeepClone());
        File.WriteAllText(path, duplicate.ToJsonString());
        Throws<InvalidDataException>(() => new DailyJournalStore(directory, "corrupt"), "Duplicate persisted event IDs must not inflate statistics.");
        File.WriteAllText(path, original);
        Require(new DailyJournalStore(directory, "corrupt").Entries.Count == 1, "Restoring the original file recovers the journal.");
    }

    private static void CheckWriteFailures(string directory)
    {
        Directory.CreateDirectory(directory);
        var blockedDirectory = Path.Combine(directory, "blocked");
        var store = new DailyJournalStore(blockedDirectory, "blocked");
        var changes = 0;
        store.Changed += () => changes++;
        File.WriteAllText(blockedDirectory, "occupied by a file");
        Throws<IOException>(() => store.RecordMood("happy"), "An unwritable storage path must return an error.");
        Require(store.Entries.Count == 0 && changes == 0, "A failed persistence operation must not report success in memory or notifications.");

        if (!OperatingSystem.IsWindows()) return;
        var lockedDirectory = Path.Combine(directory, "locked");
        var lockedStore = new DailyJournalStore(lockedDirectory, "locked");
        var mood = lockedStore.RecordMood("happy");
        var file = Directory.GetFiles(lockedDirectory, "*.json").Single();
        var before = File.ReadAllText(file);
        var lockedChanges = 0;
        lockedStore.Changed += () => lockedChanges++;
        using (var hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ThrowsStorageError(() => lockedStore.UpdateMood(mood.Id, "bad"), "Atomic replacement errors must propagate to the caller.");
            Require(lockedStore.Entries.Single().Mood == "happy" && lockedChanges == 0, "A failed edit must keep the previous in-memory entry.");
        }
        Require(File.ReadAllText(file) == before && Directory.GetFiles(lockedDirectory, "*.tmp").Length == 0,
            "A failed atomic write keeps the original file intact and cleans its temporary file.");
    }

    private static void CheckFrequencyPresets()
    {
        Require(new AppSettings().DailyFrequencyPreset == "relaxed", "New settings default to the 15–40 minute preset.");
        foreach (var (preset, minimum, maximum) in new[]
        {
            ("eager", 5, 15), ("frequent", 10, 30), ("relaxed", 15, 40),
            ("legacy-lively", 10, 30), ("legacy-standard", 30, 60), ("legacy-quiet", 60, 120)
        })
        {
            Require(InteractionScheduler.DailyBounds(preset) == (minimum, maximum), $"{preset} must preserve its documented inclusive interval.");
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var delay = InteractionScheduler.NextDailyDelay(preset).TotalMinutes;
                Require(delay >= minimum && delay <= maximum && delay == Math.Truncate(delay), $"{preset} delay must stay inside its minute bounds.");
            }
        }
        Require(InteractionScheduler.DailyBounds("invalid") == (15, 40), "Unknown new presets safely use relaxed timing.");
    }

    private static void CheckDailyRoutineSchedule()
    {
        var monday = new DateOnly(2026, 9, 21);
        Require(DailyRoutineSchedule.WeekStart(monday) == monday, "Monday is the first day of its own week.");
        Require(DailyRoutineSchedule.WeekStart(new DateOnly(2026, 9, 27)) == monday, "Sunday belongs to the week that started six days earlier.");
        Require(DailyRoutineSchedule.WeekStart(new DateOnly(2026, 10, 1)) == new DateOnly(2026, 9, 28), "A week may begin in the preceding month.");

        var now = DateTimeOffset.Parse("2026-09-24T17:00:00+08:00");
        var routine = new DailyRoutineSettings { Enabled = true, OffWorkTime = "17:00", WorkDays = [1, 2, 3, 4, 5] };
        Require(DailyRoutineSchedule.ScheduledTime(routine, now) == now, "A reminder uses the configured local clock and offset.");
        Require(!DailyRoutineSchedule.IsDue(routine, now.AddSeconds(-1)) && DailyRoutineSchedule.IsDue(routine, now),
            "A workday reminder becomes due at its scheduled time, not before.");
        Require(DailyRoutineSchedule.IsDue(routine, now.AddMinutes(30)) && !DailyRoutineSchedule.IsDue(routine, now.AddMinutes(30).AddSeconds(1)),
            "A late reminder expires after its inclusive 30-minute window.");
        Require(!DailyRoutineSchedule.IsDue(new DailyRoutineSettings { Enabled = false }, now.AddHours(1)), "Disabled schedules do not trigger spontaneous reminders.");
        Require(!DailyRoutineSchedule.IsDue(new DailyRoutineSettings { Enabled = true }, DateTimeOffset.Parse("2026-09-26T18:00:00+08:00")),
            "Unselected weekend days do not trigger workday reminders.");

        DailyRoutineSchedule.MarkShown(routine, now);
        Require(routine.PromptCount == 1 && routine.FinishedToday && !DailyRoutineSchedule.IsDue(routine, now.AddMinutes(1)),
            "Once shown, even closing the card must prevent an unsolicited repeat that day.");
        DailyRoutineSchedule.Snooze(routine, now, now.AddHours(1));
        Require(!routine.FinishedToday && !DailyRoutineSchedule.IsDue(routine, now.AddMinutes(59)) && DailyRoutineSchedule.IsDue(routine, now.AddHours(1)),
            "An explicit snooze reopens the schedule only at the requested time.");
        DailyRoutineSchedule.MarkShown(routine, now.AddHours(1));
        DailyRoutineSchedule.Snooze(routine, now.AddHours(1), now.AddHours(2));
        Require(!DailyRoutineSchedule.IsDue(routine, now.AddHours(2)) && routine.PromptCount == 2,
            "A second snooze cannot exceed two automatic cards in a day.");
        routine.FinishedToday = true;
        Require(!DailyRoutineSchedule.IsDue(routine, now.AddHours(2)), "Finishing the workday blocks pending reminders.");
        DailyRoutineSchedule.BeginDay(routine, now.AddDays(1));
        Require(!routine.FinishedToday && routine.PromptCount == 0 && routine.NextReminderAt is null
            && routine.StateDate == new DateOnly(2026, 9, 25), "Starting a new local day clears yesterday's pending and finished state.");
        Require(DailyRoutineSchedule.IsDue(routine, now.AddDays(1)), "The next configured workday can trigger again.");
        Throws<ArgumentException>(() => DailyRoutineSchedule.Snooze(routine, now, now), "Snoozes require a future time.");
        Throws<ArgumentException>(() => DailyRoutineSchedule.Snooze(routine, now, now.AddDays(1)), "Snoozes cannot silently carry into tomorrow.");
    }

    private static void ThrowsStorageError(Action action, string message)
    {
        _assertions++;
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        _assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
}
