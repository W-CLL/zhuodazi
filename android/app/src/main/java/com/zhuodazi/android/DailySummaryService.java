package com.zhuodazi.android;

import org.json.JSONArray;
import org.json.JSONObject;
import java.time.*;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.*;
import java.util.regex.*;

/** Only server-authored templates are rendered; an unfinished period is never summarized. */
final class DailySummaryService {
    private static final Set<String> TOKENS = new HashSet<>(Arrays.asList("periodStart", "periodEnd", "interactionDays",
        "totalInteractions", "quizzesAnswered", "quizzesCorrect", "moodRecords", "moodDays", "moodSummary", "happyCount", "offWorkDays", "overtimeDays"));
    private static final Pattern TOKEN = Pattern.compile("\\{([A-Za-z][A-Za-z0-9]*)\\}");
    private static final DateTimeFormatter WEEK_FORMAT = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm");

    static Map<String, Object> build(String remote, List<DailyJournalStore.Entry> all, String kind, int offset, OffsetDateTime now) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("state", "unconfigured"); result.put("title", "weekly".equals(kind) ? "周总结" : "月总结");
        result.put("body", "日常总结尚未在后台配置，请联网同步后再来看看。");
        result.put("message", result.get("body"));
        result.put("canNext", offset > 0); result.put("periodStart", ""); result.put("periodEnd", ""); result.put("nextDue", "");
        if (offset < 0 || offset > 1200 || !("weekly".equals(kind) || "monthly".equals(kind))) return result;
        try {
            JSONObject config = new JSONObject(remote).optJSONObject("dailySummaries");
            if (!validConfig(config)) return result;
            JSONObject schedule = config.getJSONObject(kind);
            if (!config.getBoolean("enabled") || !schedule.getBoolean("enabled")) {
                result.put("state", "disabled"); result.put("body", "这项总结已在管理后台关闭。"); result.put("message", result.get("body")); return result;
            }
            LocalDateTime start, end, next;
            LocalDateTime localNow = now.toLocalDateTime();
            int index;
            if ("weekly".equals(kind)) {
                int weekday = schedule.getInt("weekday");
                int daysBack = Math.floorMod(now.getDayOfWeek().getValue() % 7 - weekday, 7);
                LocalDateTime latest = now.toLocalDate().minusDays(daysBack).atTime(LocalTime.parse(schedule.getString("time")));
                if (latest.isAfter(localNow)) latest = latest.minusWeeks(1);
                next = latest.plusWeeks(1); end = latest.minusWeeks(offset); start = end.minusWeeks(1);
                index = (int) (ChronoUnit.DAYS.between(LocalDate.of(1, 1, 1), end.toLocalDate()) / 7);
            } else {
                LocalDateTime month = now.toLocalDate().withDayOfMonth(1).atStartOfDay();
                LocalDateTime release = month.toLocalDate().atTime(LocalTime.parse(schedule.getString("time")));
                LocalDateTime latest = localNow.isBefore(release) ? month.minusMonths(1) : month;
                next = localNow.isBefore(release) ? release : release.plusMonths(1);
                end = latest.minusMonths(offset); start = end.minusMonths(1);
                index = start.getYear() * 12 + start.getMonthValue() - 1;
            }
            result.put("periodStart", start.toString());
            result.put("periodEnd", end.toString());
            result.put("nextDue", next.toString());
            List<JSONObject> templates = new ArrayList<>();
            JSONArray candidates = config.getJSONArray("templates");
            for (int i = 0; i < candidates.length(); i++) {
                JSONObject candidate = candidates.getJSONObject(i);
                if (candidate.getBoolean("enabled") && kind.equals(candidate.getString("kind")) && validTemplate(candidate)) templates.add(candidate);
            }
            if (templates.isEmpty()) { result.put("body", "后台尚未添加可用的总结模板。"); result.put("message", result.get("body")); return result; }
            templates.sort(Comparator.comparing(value -> value.optString("id")));
            List<DailyJournalStore.Entry> rows = new ArrayList<>();
            // Preserve the local clock captured at recording time, matching desktop after a timezone change.
            for (DailyJournalStore.Entry row : all) if (!row.at().toLocalDateTime().isBefore(start) && row.at().toLocalDateTime().isBefore(end)) rows.add(row);
            if (rows.isEmpty()) {
                result.put("state", "waiting"); result.put("body", "这个已结束的周期还没有互动。新的总结会在结算后生成。"); result.put("message", result.get("body")); return result;
            }
            Map<String, Object> stats = DailyJournalStore.stats(rows);
            Map<String, String> values = new HashMap<>();
            for (String token : TOKENS) if (stats.containsKey(token)) values.put(token, String.valueOf(stats.get(token)));
            values.put("periodStart", "weekly".equals(kind) ? WEEK_FORMAT.format(start) : start.toLocalDate().toString());
            values.put("periodEnd", "weekly".equals(kind) ? WEEK_FORMAT.format(end) : end.minusDays(1).toLocalDate().toString());
            @SuppressWarnings("unchecked") Map<String, Integer> counts = (Map<String, Integer>) stats.get("moodCounts");
            List<String> moodSummary = new ArrayList<>();
            for (int i = 0; i < DailyJournalStore.MOODS.size(); i++) {
                int count = counts.get(DailyJournalStore.MOODS.get(i));
                if (count > 0) moodSummary.add(DailyJournalStore.MOOD_LABELS.get(i) + " " + count + " 次");
            }
            values.put("moodSummary", moodSummary.isEmpty() ? "未记录心情" : String.join("、", moodSummary));
            JSONObject template = templates.get(Math.floorMod(index, templates.size()));
            result.put("state", "ready"); result.put("templateId", template.getString("id"));
            result.put("message", "已生成");
            result.put("title", render(template.getString("title"), values)); result.put("body", render(template.getString("body"), values));
        } catch (Exception ignored) { /* Retain a clearly unconfigured state for an unsupported schema. */ }
        return result;
    }

    private static boolean validConfig(JSONObject config) throws Exception {
        if (config == null || config.optInt("schemaVersion") != 1 || !(config.opt("enabled") instanceof Boolean)) return false;
        for (String kind : Arrays.asList("weekly", "monthly")) {
            JSONObject schedule = config.optJSONObject(kind);
            if (schedule == null || !(schedule.opt("enabled") instanceof Boolean) || !validTime(schedule.optString("time"))) return false;
        }
        int weekday = config.getJSONObject("weekly").optInt("weekday", -1);
        if (weekday < 0 || weekday > 6) return false;
        JSONArray templates = config.optJSONArray("templates");
        if (templates == null || templates.length() > 40) return false;
        Set<String> ids = new HashSet<>();
        for (int i = 0; i < templates.length(); i++) {
            JSONObject item = templates.optJSONObject(i);
            if (item == null || !ids.add(item.optString("id")) || !validTemplate(item)) return false;
        }
        return true;
    }
    static boolean validTime(String time) { return time != null && time.matches("(?:[01]\\d|2[0-3]):[0-5]\\d"); }
    private static boolean validTemplate(JSONObject item) {
        return item.optString("id").matches("[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}")
            && Arrays.asList("weekly", "monthly").contains(item.optString("kind")) && item.opt("enabled") instanceof Boolean
            && validText(item.optString("title"), 80) && validText(item.optString("body"), 2000);
    }
    private static boolean validText(String text, int max) {
        if (text.trim().isEmpty() || text.length() > max || text.matches("(?s).*[<>\\x00-\\x08\\x0b\\x0c\\x0e-\\x1f\\x7f].*")) return false;
        Matcher matcher = TOKEN.matcher(text);
        while (matcher.find()) if (!TOKENS.contains(matcher.group(1))) return false;
        String remainder = TOKEN.matcher(text).replaceAll("");
        return !remainder.contains("{") && !remainder.contains("}");
    }
    private static String render(String text, Map<String, String> values) {
        Matcher matcher = TOKEN.matcher(text); StringBuffer output = new StringBuffer();
        while (matcher.find()) matcher.appendReplacement(output, Matcher.quoteReplacement(values.getOrDefault(matcher.group(1), "0")));
        matcher.appendTail(output); return output.toString();
    }
}
