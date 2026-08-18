package com.zhuodazi.android;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

final class ReminderStore {
    static final int MAX_REMINDERS = 20;
    static final int MAX_MESSAGE = 40;

    static final class Reminder {
        final String id;
        boolean enabled;
        long at;
        final String message;
        final String emotion;
        final String expressionPetId;
        final boolean repeatDaily;

        Reminder(String id, boolean enabled, long at, String message, String emotion,
                 String expressionPetId, boolean repeatDaily) {
            this.id = id;
            this.enabled = enabled;
            this.at = at;
            this.message = message;
            this.emotion = emotion;
            this.expressionPetId = expressionPetId;
            this.repeatDaily = repeatDaily;
        }

        JSONObject toJson() {
            JSONObject value = new JSONObject();
            try {
                value.put("id", id);
                value.put("enabled", enabled);
                value.put("at", at);
                value.put("message", message);
                value.put("emotion", emotion);
                value.put("expressionPetId", expressionPetId == null ? "" : expressionPetId);
                value.put("repeatDaily", repeatDaily);
            } catch (Exception ignored) { }
            return value;
        }

        Map<String, Object> toMap() {
            Map<String, Object> value = new LinkedHashMap<>();
            value.put("id", id);
            value.put("enabled", enabled);
            value.put("at", at);
            value.put("message", message);
            value.put("emotion", emotion);
            value.put("expressionPetId", expressionPetId == null ? "" : expressionPetId);
            value.put("repeatDaily", repeatDaily);
            return value;
        }
    }

    private final SettingsStore settings;

    ReminderStore(SettingsStore settings) {
        this.settings = settings;
    }

    List<Reminder> reminders() {
        List<Reminder> result = new ArrayList<>();
        try {
            JSONArray items = new JSONArray(settings.remindersJson());
            for (int index = 0; index < items.length() && result.size() < MAX_REMINDERS; index++) {
                Reminder reminder = fromJson(items.optJSONObject(index));
                if (reminder != null) result.add(reminder);
            }
        } catch (Exception ignored) { }
        return result;
    }

    Reminder save(String id, boolean enabled, long at, String message, String emotion,
                  String expressionPetId, boolean repeatDaily) {
        String cleanMessage = clean(message, MAX_MESSAGE);
        if (cleanMessage.isEmpty()) throw new IllegalArgumentException("请填写提醒内容。");
        if (at <= 0) throw new IllegalArgumentException("请选择提醒时间。");
        String cleanEmotion = normalizeEmotion(emotion);
        String cleanExpression = expressionPetId == null ? "" : expressionPetId.trim();
        List<Reminder> current = reminders();
        int existing = indexOf(current, id);
        if (existing < 0 && current.size() >= MAX_REMINDERS) {
            throw new IllegalStateException("最多只能添加 20 个提醒。");
        }
        String storedId = existing >= 0 ? current.get(existing).id : "reminder-" + UUID.randomUUID();
        Reminder reminder = new Reminder(storedId, enabled, at, cleanMessage, cleanEmotion,
            cleanExpression, repeatDaily);
        if (existing >= 0) current.set(existing, reminder);
        else current.add(reminder);
        persist(current);
        return reminder;
    }

    void delete(String id) {
        if (id == null || id.trim().isEmpty()) return;
        List<Reminder> current = reminders();
        current.removeIf(item -> id.equals(item.id));
        persist(current);
    }

    List<Reminder> fireDue(long now) {
        List<Reminder> current = reminders();
        List<Reminder> due = new ArrayList<>();
        boolean changed = false;
        for (Reminder reminder : current) {
            if (!reminder.enabled || reminder.at > now) continue;
            due.add(reminder);
            changed = true;
            if (reminder.repeatDaily) {
                long next = reminder.at;
                while (next <= now) next += 86_400_000L;
                reminder.at = next;
            } else {
                reminder.enabled = false;
            }
        }
        if (changed) persist(current);
        return due;
    }

    void normalizePast(long now) {
        List<Reminder> current = reminders();
        boolean changed = false;
        for (Reminder reminder : current) {
            if (!reminder.enabled || reminder.at > now) continue;
            changed = true;
            if (reminder.repeatDaily) {
                long next = reminder.at;
                while (next <= now) next += 86_400_000L;
                reminder.at = next;
            } else {
                reminder.enabled = false;
            }
        }
        if (changed) persist(current);
    }

    private void persist(List<Reminder> reminders) {
        JSONArray items = new JSONArray();
        for (Reminder reminder : reminders) items.put(reminder.toJson());
        settings.putString(SettingsStore.REMINDERS, items.toString());
    }

    private static int indexOf(List<Reminder> reminders, String id) {
        if (id == null || id.trim().isEmpty()) return -1;
        for (int index = 0; index < reminders.size(); index++) {
            if (id.equals(reminders.get(index).id)) return index;
        }
        return -1;
    }

    private static Reminder fromJson(JSONObject item) {
        if (item == null) return null;
        String message = clean(item.optString("message"), MAX_MESSAGE);
        long at = item.optLong("at", 0L);
        if (message.isEmpty() || at <= 0) return null;
        String id = clean(item.optString("id"), 80);
        if (id.isEmpty()) id = "reminder-" + UUID.randomUUID();
        return new Reminder(
            id,
            item.optBoolean("enabled", true),
            at,
            message,
            normalizeEmotion(item.optString("emotion", "happy")),
            clean(item.optString("expressionPetId"), 80),
            item.optBoolean("repeatDaily", false));
    }

    private static String normalizeEmotion(String emotion) {
        return switch (emotion == null ? "" : emotion) {
            case "cheer", "shy", "surprised", "angry", "confused", "sad", "sleepy", "calm" -> emotion;
            default -> "happy";
        };
    }

    private static String clean(String value, int limit) {
        if (value == null) return "";
        String cleaned = value.replace('\u00a0', ' ').trim().replaceAll("\\s+", " ");
        return cleaned.length() <= limit ? cleaned : cleaned.substring(0, limit);
    }
}
