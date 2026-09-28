package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;
import org.json.JSONArray;
import org.json.JSONObject;
import java.time.*;
import java.time.temporal.TemporalAdjusters;
import java.util.*;

/** Private, durable journal. Content cache eviction and event upload never delete these entries. */
final class DailyJournalStore {
    static final List<String> MOODS = Arrays.asList("happy", "okay", "bad", "cry", "tired", "annoyed", "calm", "hopeful");
    static final List<String> MOOD_LABELS = Arrays.asList("开心", "一般", "糟糕", "想哭", "疲惫", "烦躁", "平静", "有盼头");
    private static final Object LOCK = new Object();
    private final SharedPreferences store;
    private String cachedEntriesJson;
    private List<Entry> cachedEntries;
    private boolean journalCorrupt;
    private String snapshotKey, snapshotEntriesJson, snapshotConfig;
    private Map<String, Object> cachedSnapshot;
    record Entry(String id, String kind, String mood, String contentId, Boolean correct, String scenario, String choice, OffsetDateTime at) {
        LocalDate date() { return at.toLocalDate(); }
    }

    DailyJournalStore(Context context) { this(context.getSharedPreferences("zhuodazi_daily_journal", Context.MODE_PRIVATE)); }
    DailyJournalStore(SharedPreferences store) { this.store = store; }

    List<Entry> entries() {
        synchronized (LOCK) {
            // SharedPreferences keeps strings in memory. Parse only after a journal write, including writes
            // from the separate service instance, rather than on every Flutter runtime poll.
            String source = store.getString("entries", "[]");
            if (Objects.equals(source, cachedEntriesJson) && cachedEntries != null) return cachedEntries;
            List<Entry> entries = new ArrayList<>();
            journalCorrupt = false;
            try {
                JSONArray data = new JSONArray(source);
                for (int i = 0; i < data.length(); i++) {
                    try {
                        JSONObject row = data.getJSONObject(i);
                        entries.add(new Entry(row.getString("id"), row.getString("kind"), row.optString("mood"),
                            row.optString("contentId"), row.has("correct") ? row.getBoolean("correct") : null,
                            row.optString("scenario", "offwork"), row.optString("choice"), OffsetDateTime.parse(row.getString("at"))));
                    } catch (Exception ignored) { journalCorrupt = true; /* Healthy rows remain readable; writing is blocked. */ }
                }
            } catch (Exception ignored) { journalCorrupt = true; }
            cachedEntriesJson = source;
            cachedEntries = Collections.unmodifiableList(entries);
            return cachedEntries;
        }
    }

    boolean recordMood(String mood) { return recordMood(UUID.randomUUID().toString(), mood, OffsetDateTime.now()); }
    boolean recordMood(String id, String mood, OffsetDateTime now) {
        if (!MOODS.contains(mood)) throw new IllegalArgumentException("请选择一种有效的心情");
        return append(new Entry(id, "mood", mood, "", null, "", "", now));
    }
    boolean recordQuiz(String id, String contentId, boolean correct, OffsetDateTime now) {
        return append(new Entry(id, "quiz", "", contentId, correct, "", "", now));
    }
    boolean recordDaily(String id, String scenario, String choice, OffsetDateTime now) {
        if (!Arrays.asList("work", "joke", "tip", "care").contains(scenario)) throw new IllegalArgumentException("日常互动类型无效");
        if ("work".equals(scenario) && !Arrays.asList("smooth", "busy", "stuck").contains(choice)) throw new IllegalArgumentException("工作选项无效");
        return append(new Entry(id, "daily", "", "work".equals(scenario) ? "" : choice, null, scenario, choice, now));
    }
    boolean recordWork(String id, String choice, int snoozeMinutes, OffsetDateTime now) {
        if (!Arrays.asList("done", "overtime", "six", "rest").contains(choice)) throw new IllegalArgumentException("下班选项无效");
        if (snoozeMinutes != 0 && snoozeMinutes != 30 && snoozeMinutes != 60) throw new IllegalArgumentException("稍后提醒时间无效");
        synchronized (LOCK) {
            boolean added = append(new Entry(id, "daily", "", "", null, "offwork", choice, now));
            if (added) {
                SharedPreferences.Editor edit = store.edit().putString("prompt_date", now.toLocalDate().toString());
                long snooze = 0;
                // One explicit follow-up per day. Changing the choice never creates repeated reminders.
                if (!now.toLocalDate().toString().equals(store.getString("snooze_used_date", ""))) {
                    if ("overtime".equals(choice) && snoozeMinutes > 0) snooze = now.plusMinutes(snoozeMinutes).toInstant().toEpochMilli();
                    if ("six".equals(choice) && now.toLocalTime().isBefore(LocalTime.of(18, 0)))
                        snooze = now.withHour(18).withMinute(0).withSecond(0).withNano(0).toInstant().toEpochMilli();
                    if (snooze > 0) edit.putString("snooze_used_date", now.toLocalDate().toString());
                }
                edit.putLong("snooze_at", snooze).commit();
            }
            return added;
        }
    }

    void snoozeWork(int minutes, OffsetDateTime now) {
        if (minutes != 30 && minutes != 60) throw new IllegalArgumentException("稍后提醒时间无效");
        synchronized (LOCK) {
            String today = now.toLocalDate().toString();
            if (today.equals(store.getString("snooze_used_date", "")) || !now.plusMinutes(minutes).toLocalDate().equals(now.toLocalDate())) return;
            store.edit().putString("snooze_used_date", today).putString("prompt_date", today)
                .putLong("snooze_at", now.plusMinutes(minutes).toInstant().toEpochMilli()).commit();
        }
    }

    private boolean append(Entry entry) {
        synchronized (LOCK) {
            List<Entry> rows = new ArrayList<>(entries());
            if (journalCorrupt) throw new IllegalStateException("日常数据暂时无法完整读取，原始数据已保留。为保护历史，暂不能新增互动记录。");
            if (rows.stream().anyMatch(row -> row.id.equals(entry.id))) return false;
            rows.add(entry);
            JSONArray data = new JSONArray();
            try {
                for (Entry row : rows) {
                    JSONObject object = new JSONObject().put("id", row.id).put("kind", row.kind).put("mood", row.mood)
                        .put("contentId", row.contentId).put("scenario", row.scenario).put("choice", row.choice).put("at", row.at.toString());
                    if (row.correct != null) object.put("correct", row.correct);
                    data.put(object);
                }
            } catch (Exception error) { throw new IllegalStateException("暂时无法保存这次互动", error); }
            if (!store.edit().putString("entries", data.toString()).commit()) throw new IllegalStateException("暂时无法保存这次互动");
            return true;
        }
    }

    static Map<String, Object> stats(List<Entry> rows) {
        Map<String, Object> result = new LinkedHashMap<>();
        Map<String, Integer> counts = new LinkedHashMap<>();
        MOODS.forEach(mood -> counts.put(mood, 0));
        Set<LocalDate> days = new HashSet<>(), moodDays = new HashSet<>(), offWorkDays = new HashSet<>(), overtimeDays = new HashSet<>();
        int quizzes = 0, correct = 0, moods = 0;
        for (Entry row : rows) {
            days.add(row.date());
            if ("quiz".equals(row.kind)) { quizzes++; if (Boolean.TRUE.equals(row.correct)) correct++; }
            if ("mood".equals(row.kind) && MOODS.contains(row.mood)) {
                moods++; moodDays.add(row.date()); counts.put(row.mood, counts.get(row.mood) + 1);
            }
            if ("daily".equals(row.kind) && "offwork".equals(row.scenario) && "done".equals(row.choice)) offWorkDays.add(row.date());
            if ("daily".equals(row.kind) && "offwork".equals(row.scenario) && "overtime".equals(row.choice)) overtimeDays.add(row.date());
        }
        result.put("totalInteractions", rows.size()); result.put("quizzesAnswered", quizzes); result.put("quizzesCorrect", correct);
        result.put("moodRecords", moods); result.put("moodDays", moodDays.size()); result.put("interactionDays", days.size());
        result.put("moodCounts", counts); result.put("happyCount", counts.get("happy"));
        result.put("offWorkDays", offWorkDays.size()); result.put("overtimeDays", overtimeDays.size());
        return result;
    }

    Map<String, Object> snapshot(String period, String monthText, int weeklyOffset, int monthlyOffset, String config, OffsetDateTime now) {
        synchronized (LOCK) {
            String source = store.getString("entries", "[]");
            String key = period + "/" + monthText + "/" + weeklyOffset + "/" + monthlyOffset + "/" + now.withSecond(0).withNano(0)
                + "/" + store.getBoolean("routine_enabled", false) + "/" + store.getInt("routine_days", 62) + "/" + store.getString("routine_time", "17:00");
            if (cachedSnapshot != null && key.equals(snapshotKey) && Objects.equals(source, snapshotEntriesJson) && Objects.equals(config, snapshotConfig)) return cachedSnapshot;
            cachedSnapshot = buildSnapshot(period, monthText, weeklyOffset, monthlyOffset, config, now);
            snapshotKey = key; snapshotEntriesJson = source; snapshotConfig = config;
            return cachedSnapshot;
        }
    }

    private Map<String, Object> buildSnapshot(String period, String monthText, int weeklyOffset, int monthlyOffset, String config, OffsetDateTime now) {
        List<Entry> all = entries();
        LocalDate today = now.toLocalDate();
        YearMonth month;
        try { month = YearMonth.parse(monthText); } catch (Exception error) { month = YearMonth.from(now); }
        if (month.isAfter(YearMonth.from(now))) month = YearMonth.from(now);
        LocalDate start = "week".equals(period) ? today.with(TemporalAdjusters.previousOrSame(DayOfWeek.MONDAY))
            : "all".equals(period) ? all.stream().map(Entry::date).min(LocalDate::compareTo).orElse(today) : month.atDay(1);
        LocalDate end = "month".equals(period) && month.isBefore(YearMonth.from(now)) ? month.atEndOfMonth() : today;
        List<Entry> selected = new ArrayList<>();
        for (Entry row : all) if (!row.date().isBefore(start) && !row.date().isAfter(end)) selected.add(row);
        List<Map<String, Object>> calendar = new ArrayList<>();
        for (int day = 1; day <= month.lengthOfMonth(); day++) {
            LocalDate date = month.atDay(day); String mood = ""; int count = 0;
            OffsetDateTime latest = null;
            for (Entry row : all) if ("mood".equals(row.kind) && row.date().equals(date)) {
                count++; if (latest == null || row.at.isAfter(latest)) { latest = row.at; mood = row.mood; }
            }
            Map<String, Object> cell = new LinkedHashMap<>(); cell.put("date", date.toString()); cell.put("mood", mood); cell.put("moodCount", count);
            calendar.add(cell);
        }
        Map<String, Object> result = new LinkedHashMap<>();
        Map<String, Object> periodStats = stats(selected); periodStats.put("rangeStart", start.toString()); periodStats.put("rangeEnd", end.toString());
        result.put("today", today.toString()); result.put("stats", periodStats); result.put("allTime", stats(all));
        result.put("storageWarning", journalCorrupt ? "日常数据暂时无法完整读取，原始数据已保留，部分统计可能不完整。" : "");
        result.put("calendar", calendar); result.put("month", month.toString()); result.put("routine", routine());
        result.put("weekly", DailySummaryService.build(config, all, "weekly", weeklyOffset, now));
        result.put("monthly", DailySummaryService.build(config, all, "monthly", monthlyOffset, now));
        return result;
    }

    Map<String, Object> routine() {
        synchronized (LOCK) {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("enabled", store.getBoolean("routine_enabled", false));
            value.put("time", store.getString("routine_time", "17:00"));
            List<Integer> workdays = new ArrayList<>();
            int mask = store.getInt("routine_days", 62);
            for (int day = 0; day <= 6; day++) if ((mask & (1 << day)) != 0) workdays.add(day);
            value.put("workdays", workdays);
            return value;
        }
    }
    void saveRoutine(boolean enabled, List<?> days, String time) {
        if (time == null || !time.matches("(?:[01]\\d|2[0-3]):[0-5]\\d")) throw new IllegalArgumentException("下班时间应为 HH:mm");
        int mask = 0;
        if (days == null) throw new IllegalArgumentException("请选择工作日");
        for (Object day : days) {
            if (!(day instanceof Number) || ((Number) day).intValue() < 0 || ((Number) day).intValue() > 6)
                throw new IllegalArgumentException("工作日无效");
            mask |= 1 << ((Number) day).intValue();
        }
        if (enabled && mask == 0) throw new IllegalArgumentException("至少选择一个工作日");
        synchronized (LOCK) {
            store.edit().putBoolean("routine_enabled", enabled).putInt("routine_days", mask).putString("routine_time", time).putLong("snooze_at", 0).commit();
        }
    }
    boolean workPromptDue(OffsetDateTime now) {
        synchronized (LOCK) {
            long snooze = store.getLong("snooze_at", 0);
            String today = now.toLocalDate().toString();
            if (snooze > 0 && today.equals(store.getString("prompt_date", ""))) {
                long late = now.toInstant().toEpochMilli() - snooze;
                return late >= 0 && late <= 30 * 60_000L;
            }
            if (!store.getBoolean("routine_enabled", false)) return false;
            int mask = store.getInt("routine_days", 62);
            if ((mask & (1 << (now.getDayOfWeek().getValue() % 7))) == 0) return false;
            if (today.equals(store.getString("prompt_date", ""))) return false;
            long late = Duration.between(LocalTime.parse(store.getString("routine_time", "17:00")), now.toLocalTime()).toMillis();
            return late >= 0 && late <= 30 * 60_000L;
        }
    }
    void markWorkPrompted(OffsetDateTime now) {
        synchronized (LOCK) { store.edit().putString("prompt_date", now.toLocalDate().toString()).putLong("snooze_at", 0).commit(); }
    }
    boolean workContext(OffsetDateTime now) {
        synchronized (LOCK) {
            return store.getBoolean("routine_enabled", false)
                && (store.getInt("routine_days", 62) & (1 << (now.getDayOfWeek().getValue() % 7))) != 0
                && !now.toLocalDate().toString().equals(store.getString("prompt_date", ""))
                && now.getHour() >= 9 && !now.toLocalTime().isAfter(LocalTime.parse(store.getString("routine_time", "17:00")));
        }
    }
    boolean workCheckInDue(OffsetDateTime now) {
        synchronized (LOCK) {
            return workContext(now) && now.getHour() >= 10
                && !now.toLocalDate().toString().equals(store.getString("work_checkin_date", ""))
                && !now.toLocalTime().isAfter(LocalTime.parse(store.getString("routine_time", "17:00")).minusMinutes(15));
        }
    }
    void markWorkCheckInPrompted(OffsetDateTime now) {
        synchronized (LOCK) { store.edit().putString("work_checkin_date", now.toLocalDate().toString()).commit(); }
    }
}
