package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Base64;

import com.google.crypto.tink.subtle.Ed25519Verify;

import org.json.JSONArray;
import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.regex.Pattern;

final class InteractionContentService {
    private static final String PREFS = "zhuodazi_interaction_content";
    private static final String ITEMS = "items";
    private static final String SHOWN = "shown";
    private static final String EVENTS = "events";
    private static final String LAST_TYPE = "last_type";
    private static final String NEXT_MOOD_AT = "next_mood_at";
    private static final String CATALOG_VERSION = "catalog_version";
    private static final String LAST_SYNC_ERROR = "last_sync_error";
    private static final String LAST_SYNC_AT = "last_sync_at";
    private static final String PUBLIC_KEY_SPKI = DeskPetApi.SIGNING_PUBLIC_KEY_SPKI;
    private static final Set<String> TYPES = new HashSet<>(
        Arrays.asList("joke", "math", "trivia", "riddle", "tip", "care"));
    private static final List<String> REQUEST_TYPES =
        Arrays.asList("joke", "math", "trivia", "riddle", "tip", "care");
    private static final List<String> QUIZ_TYPES = Arrays.asList("math", "trivia", "riddle");
    private static final Pattern ID_PATTERN = Pattern.compile("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$");
    private static final int MAX_SHOWN = 500;
    private static final int MAX_EVENTS = 1000;
    private static final Object PROFILE_LOCK = new Object();
    private static final String PROFILE_DIRTY = "profile_dirty";
    private static final String PROFILE_REVISION = "profile_revision";
    private static final String PROFILE_OWNER = "profile_owner";

    private final Context context;
    private final LicenseService licenses;
    private final SharedPreferences store;
    private final DailyJournalStore journal;
    private final List<String> shown = new ArrayList<>();
    private final List<JSONObject> events = new ArrayList<>();
    private long catalogVersion;

    InteractionContentService(Context context, LicenseService licenses) {
        this.context = context.getApplicationContext();
        this.licenses = licenses;
        store = this.context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        journal = new DailyJournalStore(this.context);
        load();
    }

    synchronized long catalogVersion() { return catalogVersion; }

    synchronized String lastSyncError() { return store.getString(LAST_SYNC_ERROR, ""); }

    synchronized long lastSyncAt() { return store.getLong(LAST_SYNC_AT, 0L); }

    synchronized boolean shouldFlush() { return events.size() >= 10; }

    synchronized boolean isMoodPromptDue() {
        String today = java.time.LocalDate.now().toString();
        return (!today.equals(store.getString("mood_prompt_date", "")) || store.getInt("mood_prompt_count", 0) < 2)
            && System.currentTimeMillis() >= store.getLong(NEXT_MOOD_AT, 0L);
    }

    synchronized void markMoodPrompted(boolean automatic) {
        String today = java.time.LocalDate.now().toString();
        int count = today.equals(store.getString("mood_prompt_date", "")) ? store.getInt("mood_prompt_count", 0) : 0;
        store.edit().putLong(NEXT_MOOD_AT, System.currentTimeMillis() + 180 * 60_000L)
            .putString("mood_prompt_date", today).putInt("mood_prompt_count", count + (automatic ? 1 : 0))
            .putString(LAST_TYPE, "mood").apply();
    }

    synchronized void markContentShown(Item item) {
        if (item == null || !item.online() || shown.contains(item.id)) return;
        shown.add(0, item.id);
        while (shown.size() > MAX_SHOWN) shown.remove(shown.size() - 1);
        store.edit().putString(LAST_TYPE, item.type).apply();
        addEvent("content_shown", null, item.id, null);
        save();
    }

    synchronized void recordMood(String mood) {
        journal.recordMood(mood);
        store.edit().putLong(NEXT_MOOD_AT, System.currentTimeMillis() + 180 * 60_000L).apply();
        if (Arrays.asList("bad", "cry", "tired", "annoyed").contains(mood))
            new SettingsStore(context).putLong(SettingsStore.DAILY_GENTLE_UNTIL, System.currentTimeMillis() + 30 * 60_000L);
        // The legacy analytics API accepts only three values. Never infer or relabel a person's mood.
        if ("happy".equals(mood) || "okay".equals(mood)) addEvent("mood_response", mood, null, null);
        save();
    }

    synchronized void recordJoke(Item item, String occurrenceId) {
        if (item == null || !journal.recordDaily(occurrenceId, "joke", item.id, java.time.OffsetDateTime.now())) return;
        if (item != null && item.online()) {
            addEvent("joke_revealed", null, item.id, null);
            save();
        }
    }

    synchronized void recordQuiz(Item item, boolean correct, String occurrenceId) {
        if (item == null || !journal.recordQuiz(occurrenceId, item.id, correct, java.time.OffsetDateTime.now())) return;
        if (item != null && item.online()) {
            addEvent("quiz_answered", null, item.id, correct);
            save();
        }
    }

    Item fetchNextOnline(boolean quizOnly) throws Exception {
        try {
            Item item = requestNextOnline(quizOnly);
            store.edit().putString(LAST_SYNC_ERROR, "")
                .putLong(LAST_SYNC_AT, System.currentTimeMillis()).apply();
            return item;
        } catch (Exception error) {
            String message = error.getMessage();
            store.edit().putString(LAST_SYNC_ERROR,
                message == null || message.trim().isEmpty() ? "线上内容同步失败" : message.trim()).apply();
            throw error;
        }
    }

    synchronized void recordAcknowledgment(Item item, String occurrenceId) {
        if (item != null && ("tip".equals(item.type) || "care".equals(item.type)))
            journal.recordDaily(occurrenceId, item.type, item.id, java.time.OffsetDateTime.now());
    }

    void markProfileEdited() {
        synchronized (SettingsStore.class) {
            store.edit().putBoolean(PROFILE_DIRTY, true)
                .putString(PROFILE_OWNER, profileOwner())
                .putLong(PROFILE_REVISION, store.getLong(PROFILE_REVISION, 0L) + 1).apply();
        }
    }

    private String profileOwner() {
        return licenses.visitOwner();
    }

    void syncProfile() throws Exception {
        if (!licenses.hasPremiumAccess()) return;
        synchronized (PROFILE_LOCK) {
            SettingsStore settings = new SettingsStore(context);
            if (!settings.remoteDefaultsApplied()) {
                try {
                    JSONObject remote = NetworkClient.json(context, "GET", DeskPetApi.SITE_SETTINGS, null,
                        licenses, NetworkClient.Auth.NONE);
                    settings.applyRemoteConfig(remote);
                } catch (Exception ignored) {
                    // Existing account preferences remain available during a settings outage.
                }
            }
            final long revision;
            final boolean dirty;
            final LicenseService session = licenses.snapshot();
            final String owner = session.visitOwner();
            final JSONObject body;
            synchronized (SettingsStore.class) {
                revision = store.getLong(PROFILE_REVISION, 0L);
                dirty = store.getBoolean(PROFILE_DIRTY, false) && owner.equals(store.getString(PROFILE_OWNER, ""));
                body = dirty ? new JSONObject().put("mode", settings.interactionMode()).put("promptsEnabled", settings.interactions()) : null;
            }
            JSONObject envelope = NetworkClient.json(context, dirty ? "PATCH" : "GET", DeskPetApi.INTERACTION_PROFILE,
                body, session, NetworkClient.Auth.PREMIUM);
            JSONObject profile = envelope.optJSONObject("profile");
            if (profile == null) throw new IllegalStateException("互动设置同步失败，请稍后重试");
            synchronized (SettingsStore.class) {
                if (revision != store.getLong(PROFILE_REVISION, 0L) || !owner.equals(profileOwner())) return;
                String mode = profile.optString("mode", "standard");
                if (!Arrays.asList("quiet", "standard", "lively").contains(mode)) mode = "standard";
                settings.putString(SettingsStore.INTERACTION_MODE, mode);
                settings.putBoolean(SettingsStore.INTERACTIONS, profile.optBoolean("promptsEnabled", true));
                store.edit().putBoolean(PROFILE_DIRTY, false).putString(PROFILE_OWNER, owner).apply();
            }
        }
    }

    private Item requestNextOnline(boolean quizOnly) throws Exception {
        if (!licenses.hasPremiumAccess()) throw new IllegalStateException("体验或正式激活后可获取线上互动内容");
        List<String> types = new ArrayList<>(quizOnly ? QUIZ_TYPES : REQUEST_TYPES);
        Collections.shuffle(types);
        String lastType = store.getString(LAST_TYPE, "");
        if (types.remove(lastType)) types.add(lastType);
        JSONArray exclusions = new JSONArray();
        synchronized (this) {
            for (String id : shown) if (!id.startsWith("local:")) exclusions.put(id);
        }
        JSONObject envelope = requestBatch(types, exclusions);
        JSONObject payload = verifyEnvelope(envelope, "batch");
        return readRequestedItem(payload, types);
    }

    private JSONObject requestBatch(List<String> types, JSONArray exclusions) throws Exception {
        JSONObject body = new JSONObject()
            .put("types", new JSONArray(types))
            .put("limit", 1)
            .put("excludeIds", exclusions);
        return NetworkClient.json(context, "POST", DeskPetApi.CONTENT_BATCH, body,
            licenses, NetworkClient.Auth.PREMIUM);
    }

    void flushEvents() throws Exception {
        JSONArray batch = new JSONArray();
        int count;
        synchronized (this) {
            count = Math.min(50, events.size());
            for (int i = 0; i < count; i++) batch.put(events.get(i));
        }
        if (count == 0 || !licenses.hasPremiumAccess()) return;
        NetworkClient.json(context, "POST", DeskPetApi.INTERACTION_EVENTS,
            new JSONObject().put("events", batch), licenses, NetworkClient.Auth.PREMIUM);
        synchronized (this) {
            for (int i = 0; i < count && !events.isEmpty(); i++) events.remove(0);
            save();
        }
    }

    private JSONObject verifyEnvelope(JSONObject envelope, String expectedKind) throws Exception {
        if (!"ed25519".equalsIgnoreCase(envelope.optString("signatureAlgorithm")))
            throw new IllegalStateException("互动内容签名算法无效");
        byte[] payload = Base64.decode(envelope.getString("signedPayload"), Base64.DEFAULT);
        byte[] signature = Base64.decode(envelope.getString("signature"), Base64.DEFAULT);
        if (payload.length == 0 || payload.length > 16 * 1024 * 1024 || signature.length != 64)
            throw new IllegalStateException("互动内容签名格式无效");
        String actualHash = hex(MessageDigest.getInstance("SHA-256").digest(payload));
        if (!actualHash.equalsIgnoreCase(envelope.optString("sha256")))
            throw new IllegalStateException("互动内容校验失败");
        byte[] publicKeyBytes = Base64.decode(PUBLIC_KEY_SPKI, Base64.DEFAULT);
        verifyEd25519(publicKeyBytes, payload, signature);
        JSONObject document = new JSONObject(new String(payload, StandardCharsets.UTF_8));
        if (document.optInt("schemaVersion") != 1 || !expectedKind.equals(document.optString("kind")))
            throw new IllegalStateException("互动内容目录格式无效");
        return document;
    }

    private synchronized Item readRequestedItem(JSONObject payload, List<String> requestedTypes) throws Exception {
        long incomingVersion = payload.optLong("catalogVersion", -1L);
        if (incomingVersion < catalogVersion) throw new IllegalStateException("互动内容目录已过期，请重试");
        Set<String> disabled = new HashSet<>();
        JSONArray disabledIds = payload.optJSONArray("disabledIds");
        if (disabledIds != null) {
            for (int i = 0; i < disabledIds.length(); i++) disabled.add(disabledIds.getString(i));
        }
        JSONArray incoming = payload.getJSONArray("items");
        if (incoming.length() > 1) throw new IllegalStateException("互动内容数量无效");
        catalogVersion = incomingVersion;
        save();
        if (incoming.length() == 0) return null;
        Item item = Item.fromJson(incoming.getJSONObject(0));
        if (!item.valid() || !item.online() || !requestedTypes.contains(item.type)
            || disabled.contains(item.id) || shown.contains(item.id)) return null;
        return item;
    }

    private synchronized void load() {
        try {
            JSONArray storedShown = new JSONArray(store.getString(SHOWN, "[]"));
            for (int i = 0; i < storedShown.length() && shown.size() < MAX_SHOWN; i++)
                shown.add(storedShown.getString(i));
            JSONArray storedEvents = new JSONArray(store.getString(EVENTS, "[]"));
            for (int i = 0; i < storedEvents.length() && events.size() < MAX_EVENTS; i++)
                events.add(storedEvents.getJSONObject(i));
            catalogVersion = store.getLong(CATALOG_VERSION, 0L);
        } catch (Exception error) {
            shown.clear();
            events.clear();
        }
        // Upgrade from batch caching without discarding displayed IDs, pending events, or journal data.
        store.edit().remove(ITEMS).apply();
    }

    private synchronized void save() {
        JSONArray storedShown = new JSONArray();
        for (String id : shown) storedShown.put(id);
        JSONArray storedEvents = new JSONArray();
        for (JSONObject event : events) storedEvents.put(event);
        store.edit().remove(ITEMS).putString(SHOWN, storedShown.toString())
            .putString(EVENTS, storedEvents.toString())
            .putLong(CATALOG_VERSION, catalogVersion).apply();
    }

    private void addEvent(String type, String mood, String contentId, Boolean correct) {
        JSONObject event = new JSONObject();
        try {
            event.put("eventId", UUID.randomUUID().toString());
            event.put("type", type);
            event.put("occurredAt", Instant.now().toString());
            if (mood != null) event.put("mood", mood);
            if (contentId != null) event.put("contentId", contentId);
            if (correct != null) event.put("correct", correct);
            events.add(event);
            while (events.size() > MAX_EVENTS) events.remove(0);
        } catch (Exception ignored) { }
    }

    private static String hex(byte[] data) {
        StringBuilder result = new StringBuilder(data.length * 2);
        for (byte value : data) result.append(String.format("%02x", value & 0xff));
        return result.toString();
    }

    private static void verifyEd25519(byte[] publicKeyBytes, byte[] payload,
                                      byte[] signature) throws Exception {
        if (publicKeyBytes.length != 44)
            throw new IllegalStateException("互动内容公钥格式无效");
        byte[] rawPublicKey = Arrays.copyOfRange(publicKeyBytes, 12, 44);
        try {
            new Ed25519Verify(rawPublicKey).verify(signature, payload);
        } catch (Exception error) {
            throw new IllegalStateException("互动内容签名验证失败", error);
        }
    }

    static final class Item {
        final String id;
        final String type;
        final int revision;
        final String prompt;
        final String answer;
        final String explanation;
        final List<String> choices;

        Item(String id, String type, int revision, String prompt, String answer,
             String explanation, List<String> choices) {
            this.id = id;
            this.type = type;
            this.revision = revision;
            this.prompt = prompt;
            this.answer = answer;
            this.explanation = explanation;
            this.choices = choices;
        }

        boolean online() { return !id.startsWith("local:"); }

        boolean valid() {
            if (!ID_PATTERN.matcher(id).matches() || !TYPES.contains(type) || revision < 1
                || prompt.length() < 2 || prompt.length() > 500 || answer.isEmpty() || answer.length() > 500
                || explanation.length() > 1000 || choices.size() > 6) return false;
            if (choices.isEmpty()) return true;
            return choices.size() >= 2 && choices.contains(answer);
        }

        JSONObject toJson() {
            JSONObject value = new JSONObject();
            try {
                value.put("id", id).put("type", type).put("revision", revision)
                    .put("prompt", prompt).put("answer", answer).put("explanation", explanation)
                    .put("choices", new JSONArray(choices));
            } catch (Exception ignored) { }
            return value;
        }

        static Item fromJson(JSONObject value) throws Exception {
            List<String> choices = new ArrayList<>();
            JSONArray sourceChoices = value.optJSONArray("choices");
            if (sourceChoices != null) {
                for (int i = 0; i < sourceChoices.length(); i++) choices.add(sourceChoices.getString(i));
            }
            return new Item(value.getString("id"), value.getString("type"), value.optInt("revision", 1),
                value.getString("prompt"), value.getString("answer"),
                value.optString("explanation", ""), choices);
        }
    }
}
