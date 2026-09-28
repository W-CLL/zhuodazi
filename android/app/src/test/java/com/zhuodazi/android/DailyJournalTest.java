package com.zhuodazi.android;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;
import java.time.*;
import java.util.*;
import static org.junit.Assert.*;

public class DailyJournalTest {
    private DailyJournalStore store(Map<String, Object> data) { return new DailyJournalStore(SettingsStoreTest.preferences(data)); }
    private OffsetDateTime at(String local) { return OffsetDateTime.parse(local + "+08:00"); }
    private String config() throws Exception {
        JSONArray templates = new JSONArray();
        for (String kind : List.of("weekly", "monthly")) for (int i = 0; i < 3; i++) templates.put(new JSONObject()
            .put("id", kind + i).put("kind", kind).put("enabled", true).put("title", "第" + i + "封")
            .put("body", "{periodStart}/{periodEnd};互动{totalInteractions};答题{quizzesAnswered};正确{quizzesCorrect};心情{moodSummary};下班{offWorkDays};加班{overtimeDays}"));
        return new JSONObject().put("dailySummaries", new JSONObject().put("schemaVersion", 1).put("enabled", true)
            .put("weekly", new JSONObject().put("enabled", true).put("weekday", 5).put("time", "18:00"))
            .put("monthly", new JSONObject().put("enabled", true).put("time", "09:00"))
            .put("templates", templates)).toString();
    }
    private Map<String, Object> summary(DailyJournalStore store, String kind, String now) throws Exception {
        return DailySummaryService.build(config(), store.entries(), kind, 0, at(now));
    }

    @Test public void journalsSurviveRecreationAndDeduplicateOneQuizOccurrence() {
        Map<String, Object> data = new HashMap<>(); DailyJournalStore first = store(data);
        assertTrue(first.recordQuiz("one", "question", true, at("2026-09-24T17:00:00")));
        assertFalse(first.recordQuiz("one", "question", false, at("2026-09-24T17:00:01")));
        DailyJournalStore reopened = store(data);
        assertEquals(1, reopened.entries().size());
        assertEquals(1, DailyJournalStore.stats(reopened.entries()).get("quizzesCorrect"));
    }

    @Test public void allEightExplicitMoodsCountWithoutInferringFromOtherInteractions() {
        DailyJournalStore journal = store(new HashMap<>());
        for (String mood : DailyJournalStore.MOODS) journal.recordMood(mood, mood, at("2026-09-24T17:00:00"));
        journal.recordQuiz("quiz", "q1", false, at("2026-09-24T17:00:00"));
        journal.recordWork("work", "overtime", 0, at("2026-09-24T17:00:00"));
        Map<String,Object> stats = DailyJournalStore.stats(journal.entries());
        assertEquals(8, stats.get("moodRecords")); assertEquals(10, stats.get("totalInteractions"));
        assertEquals(1, stats.get("happyCount")); assertEquals(1, stats.get("moodDays"));
        assertThrows(IllegalArgumentException.class, () -> journal.recordMood("low"));
    }

    @Test public void fridayCutoffIsHalfOpenAndNeverSummarizesAnUnfinishedWeek() throws Exception {
        DailyJournalStore journal = store(new HashMap<>());
        journal.recordMood("before", "happy", at("2026-09-25T17:59:59"));
        journal.recordMood("cutoff", "cry", at("2026-09-25T18:00:00"));
        journal.recordMood("weekend", "calm", at("2026-09-26T12:00:00"));
        assertEquals("waiting", summary(journal, "weekly", "2026-09-25T17:59:59").get("state"));
        Map<String,Object> complete = summary(journal, "weekly", "2026-09-25T18:00:00");
        assertEquals("ready", complete.get("state")); assertTrue(complete.get("body").toString().contains("互动1"));
        Map<String,Object> nextWeek = summary(journal, "weekly", "2026-10-02T18:00:00");
        assertTrue(nextWeek.get("body").toString().contains("互动2"));
        assertTrue(nextWeek.get("body").toString().contains("想哭 1 次"));
    }

    @Test public void monthlyReleaseWaitsUntilNineAndIncludesLeapMonthOnly() throws Exception {
        DailyJournalStore journal = store(new HashMap<>());
        journal.recordMood("jan", "bad", at("2024-01-31T23:59:59"));
        journal.recordMood("feb", "happy", at("2024-02-29T23:59:59"));
        journal.recordMood("march", "okay", at("2024-03-01T00:00:00"));
        Map<String,Object> before = summary(journal, "monthly", "2024-03-01T08:59:59");
        assertEquals("2024-02-01T00:00", before.get("periodEnd"));
        Map<String,Object> released = summary(journal, "monthly", "2024-03-01T09:00:00");
        assertEquals("2024-03-01T00:00", released.get("periodEnd"));
        assertTrue(released.get("body").toString().contains("互动1"));
        assertTrue(released.get("body").toString().contains("开心 1 次"));
    }

    @Test public void templateRotationIsStableAndChangesForTheNextCompletedWeek() throws Exception {
        DailyJournalStore journal = store(new HashMap<>());
        journal.recordMood("one", "happy", at("2026-09-24T17:00:00"));
        journal.recordMood("two", "happy", at("2026-09-30T17:00:00"));
        Object first = summary(journal, "weekly", "2026-09-25T18:00:00").get("templateId");
        assertEquals(first, summary(journal, "weekly", "2026-09-26T12:00:00").get("templateId"));
        assertNotEquals(first, summary(journal, "weekly", "2026-10-02T18:00:00").get("templateId"));
    }

    @Test public void noRemoteTemplateNeverProducesInventedSummaryAndUnknownTokenFailsClosed() throws Exception {
        DailyJournalStore journal = store(new HashMap<>());
        journal.recordMood("one", "happy", at("2026-09-24T17:00:00"));
        assertEquals("unconfigured", DailySummaryService.build("{}", journal.entries(), "weekly", 0, at("2026-09-25T18:00:00")).get("state"));
        assertEquals("unconfigured", DailySummaryService.build(config().replace("{moodSummary}", "{unknown}"), journal.entries(), "weekly", 0, at("2026-09-25T18:00:00")).get("state"));
        JSONObject disabled = new JSONObject(config()); disabled.getJSONObject("dailySummaries").put("enabled", false);
        assertEquals("disabled", DailySummaryService.build(disabled.toString(), journal.entries(), "weekly", 0, at("2026-09-25T18:00:00")).get("state"));
    }

    @Test public void workDaysAndOnlyOneExplicitFollowupSurviveRestart() {
        Map<String,Object> data = new HashMap<>(); DailyJournalStore journal = store(data);
        journal.saveRoutine(true, List.of(1, 2, 3, 4, 5), "17:00");
        assertFalse(journal.workPromptDue(at("2026-09-25T16:59:59")));
        assertTrue(journal.workPromptDue(at("2026-09-25T17:00:00")));
        journal.markWorkPrompted(at("2026-09-25T17:00:00"));
        assertFalse(journal.workPromptDue(at("2026-09-25T17:00:01")));
        journal.recordWork("one", "overtime", 30, at("2026-09-25T17:00:00"));
        DailyJournalStore restarted = store(data);
        assertFalse(restarted.workPromptDue(at("2026-09-25T17:29:59")));
        assertTrue(restarted.workPromptDue(at("2026-09-25T17:30:00")));
        restarted.markWorkPrompted(at("2026-09-25T17:30:00"));
        restarted.recordWork("two", "overtime", 60, at("2026-09-25T17:30:00"));
        assertFalse(restarted.workPromptDue(at("2026-09-25T18:30:00")));
        assertFalse(restarted.workPromptDue(at("2026-09-26T18:00:00")));
        assertTrue(restarted.workPromptDue(at("2026-09-28T17:00:00")));
    }

    @Test public void sixOClockIsTodayOnlyAndNoSnoozeMeansNoFollowup() {
        DailyJournalStore journal = store(new HashMap<>()); journal.saveRoutine(true, List.of(5), "17:00");
        journal.recordWork("one", "six", 0, at("2026-09-25T17:00:00"));
        assertFalse(journal.workPromptDue(at("2026-09-25T17:59:59")));
        assertTrue(journal.workPromptDue(at("2026-09-25T18:00:00")));
        journal.recordWork("two", "done", 0, at("2026-09-25T18:00:00"));
        assertFalse(journal.workPromptDue(at("2026-09-25T19:00:00")));
        journal.recordWork("three", "overtime", 0, at("2026-10-02T17:00:00"));
        assertFalse(journal.workPromptDue(at("2026-10-02T20:00:00")));
    }

    @Test public void monthlyStatsAndCalendarUseSelectedMonthAndLatestExplicitMood() {
        DailyJournalStore journal = store(new HashMap<>());
        journal.recordMood("one", "cry", at("2026-08-15T10:00:00"));
        journal.recordMood("two", "happy", at("2026-08-15T20:00:00"));
        journal.recordMood("three", "calm", at("2026-09-24T12:00:00"));
        Map<String,Object> data = journal.snapshot("month", "2026-08", 0, 0, "{}", at("2026-09-24T17:00:00"));
        Map<?,?> stats = (Map<?,?>) data.get("stats");
        assertEquals(2, stats.get("moodRecords")); assertEquals("2026-08-31", stats.get("rangeEnd"));
        Map<?,?> cell = (Map<?,?>) ((List<?>) data.get("calendar")).get(14);
        assertEquals("happy", cell.get("mood")); assertEquals(2, cell.get("moodCount"));
        assertFalse(data.containsKey("entries"));
    }

    @Test public void routineValidationRejectsBadTimesAndDays() {
        DailyJournalStore journal = store(new HashMap<>());
        assertThrows(IllegalArgumentException.class, () -> journal.saveRoutine(true, List.of(1), "25:00"));
        assertThrows(IllegalArgumentException.class, () -> journal.saveRoutine(true, List.of(7), "17:00"));
        assertThrows(IllegalArgumentException.class, () -> journal.saveRoutine(true, List.of(), "17:00"));
        assertThrows(IllegalArgumentException.class, () -> journal.recordWork("one", "overtime", 15, at("2026-09-25T17:00:00")));
    }

    @Test public void corruptJournalNeverGetsOverwrittenByTheNextAnswer() {
        Map<String,Object> data = new HashMap<>(); data.put("entries", "[{broken historical data");
        DailyJournalStore journal = store(data);
        assertTrue(journal.entries().isEmpty());
        assertThrows(IllegalStateException.class, () -> journal.recordMood("one", "happy", at("2026-09-25T17:00:00")));
        assertEquals("[{broken historical data", data.get("entries"));
        assertFalse(journal.snapshot("month", "2026-09", 0, 0, "{}", at("2026-09-25T17:00:00")).get("storageWarning").toString().isEmpty());
    }

    @Test public void expiredRemindersDoNotReplayAfterBusyTimeAndExplicitSnoozeWorksWithoutRoutine() {
        DailyJournalStore journal = store(new HashMap<>());
        journal.saveRoutine(true, List.of(5), "17:00");
        assertTrue(journal.workPromptDue(at("2026-09-25T17:30:00")));
        assertFalse(journal.workPromptDue(at("2026-09-25T17:30:01")));
        journal.saveRoutine(false, List.of(5), "17:00");
        journal.recordWork("one", "overtime", 30, at("2026-09-25T17:00:00"));
        assertTrue(journal.workPromptDue(at("2026-09-25T17:30:00")));
        assertFalse(journal.workPromptDue(at("2026-09-25T18:00:01")));
    }

    @Test public void workCheckInIsAtMostOncePerWorkdayAndNeverInfersMood() {
        DailyJournalStore journal = store(new HashMap<>()); journal.saveRoutine(true, List.of(5), "17:00");
        assertFalse(journal.workCheckInDue(at("2026-09-25T09:59:59")));
        assertTrue(journal.workCheckInDue(at("2026-09-25T10:00:00")));
        journal.markWorkCheckInPrompted(at("2026-09-25T10:00:00"));
        assertFalse(journal.workCheckInDue(at("2026-09-25T12:00:00")));
        journal.recordDaily("work", "work", "stuck", at("2026-09-25T10:00:00"));
        journal.recordDaily("joke", "joke", "q1", at("2026-09-25T10:00:00"));
        journal.recordDaily("tip", "tip", "q2", at("2026-09-25T10:00:00"));
        assertFalse(journal.recordDaily("tip", "tip", "q2", at("2026-09-25T10:00:00")));
        assertEquals(3, DailyJournalStore.stats(journal.entries()).get("totalInteractions"));
        assertEquals(0, DailyJournalStore.stats(journal.entries()).get("moodRecords"));
        assertFalse(journal.workCheckInDue(at("2026-09-26T12:00:00")));
        assertFalse(journal.workCheckInDue(at("2026-10-02T16:46:00")));
    }

    @Test public void runtimePollingReusesSnapshotUntilDataOrCutoffChanges() {
        Map<String,Object> data = new HashMap<>(); DailyJournalStore journal = store(data);
        Map<String,Object> first = journal.snapshot("month", "", 0, 0, "{}", at("2026-09-25T17:59:10"));
        assertSame(first, journal.snapshot("month", "", 0, 0, "{}", at("2026-09-25T17:59:11")));
        store(data).recordMood("one", "happy", at("2026-09-25T17:59:12"));
        Map<String,Object> changed = journal.snapshot("month", "", 0, 0, "{}", at("2026-09-25T17:59:13"));
        assertNotSame(first, changed);
        assertEquals(1, ((Map<?,?>)changed.get("stats")).get("moodRecords"));
        assertNotSame(changed, journal.snapshot("month", "", 0, 0, "{}", at("2026-09-25T18:00:00")));
    }
}
