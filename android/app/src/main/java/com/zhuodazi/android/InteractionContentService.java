package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Base64;

import com.google.crypto.tink.subtle.Ed25519Verify;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Random;
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
    private static final List<String> LEGACY_REQUEST_TYPES =
        Arrays.asList("joke", "math", "trivia");
    private static final Pattern ID_PATTERN = Pattern.compile("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$");
    private static final int MAX_CACHE = 90;
    private static final int MAX_SHOWN = 500;
    private static final int MAX_EVENTS = 1000;

    private final Context context;
    private final LicenseService licenses;
    private final SharedPreferences store;
    private final Random random = new Random();
    private final List<Item> items = new ArrayList<>();
    private final List<String> shown = new ArrayList<>();
    private final List<JSONObject> events = new ArrayList<>();
    private String lastType = "";
    private long catalogVersion;

    InteractionContentService(Context context, LicenseService licenses) {
        this.context = context.getApplicationContext();
        this.licenses = licenses;
        store = this.context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        load();
        if (items.isEmpty()) resetFallback();
    }

    synchronized int cachedCount() { return items.size(); }

    synchronized int onlineCount() {
        int count = 0;
        for (Item item : items) if (item.online()) count++;
        return count;
    }

    synchronized long catalogVersion() { return catalogVersion; }

    synchronized String lastSyncError() { return store.getString(LAST_SYNC_ERROR, ""); }

    synchronized long lastSyncAt() { return store.getLong(LAST_SYNC_AT, 0L); }

    synchronized boolean needsRefill() { return licenses.hasPremiumAccess() && onlineCount() < 18; }

    synchronized boolean shouldFlush() { return events.size() >= 10; }

    synchronized boolean isMoodPromptDue() {
        return System.currentTimeMillis() >= store.getLong(NEXT_MOOD_AT, 0L);
    }

    synchronized void markMoodPrompted() {
        long minutes = 180L + random.nextInt(181);
        store.edit().putLong(NEXT_MOOD_AT, System.currentTimeMillis() + minutes * 60_000L)
            .putString(LAST_TYPE, "mood").apply();
        lastType = "mood";
    }

    synchronized Item takeNextContent() {
        if (items.isEmpty()) resetFallback();
        if (items.isEmpty()) return null;
        List<Item> candidates = new ArrayList<>();
        for (Item item : items) if (!item.type.equals(lastType)) candidates.add(item);
        if (candidates.isEmpty()) candidates.addAll(items);
        Item selected = candidates.get(random.nextInt(candidates.size()));
        items.remove(selected);
        shown.remove(selected.id);
        shown.add(0, selected.id);
        while (shown.size() > MAX_SHOWN) shown.remove(shown.size() - 1);
        lastType = selected.type;
        if (selected.online()) addEvent("content_shown", null, selected.id, null);
        save();
        return selected;
    }

    synchronized void recordMood(String mood) {
        if (!("happy".equals(mood) || "okay".equals(mood) || "low".equals(mood))) return;
        addEvent("mood_response", mood, null, null);
        save();
    }

    synchronized void recordJoke(Item item) {
        if (item != null && item.online()) {
            addEvent("joke_revealed", null, item.id, null);
            save();
        }
    }

    synchronized void recordQuiz(Item item, boolean correct) {
        if (item != null && item.online()) {
            addEvent("quiz_answered", null, item.id, correct);
            save();
        }
    }

    int refillOnline() throws Exception {
        try {
            int added = performRefillOnline();
            store.edit().putString(LAST_SYNC_ERROR, "")
                .putLong(LAST_SYNC_AT, System.currentTimeMillis()).apply();
            return added;
        } catch (Exception error) {
            String message = error.getMessage();
            store.edit().putString(LAST_SYNC_ERROR,
                message == null || message.trim().isEmpty() ? "线上内容同步失败" : message.trim()).apply();
            throw error;
        }
    }

    private int performRefillOnline() throws Exception {
        if (!licenses.hasPremiumAccess()) throw new IllegalStateException("体验或正式激活后可同步线上互动内容");
        JSONArray exclusions = new JSONArray();
        synchronized (this) {
            Set<String> unique = new HashSet<>();
            for (Item item : items) if (item.online()) unique.add(item.id);
            for (String id : shown) if (!id.startsWith("local:")) unique.add(id);
            for (String id : unique) {
                if (exclusions.length() >= 500) break;
                exclusions.put(id);
            }
        }
        JSONObject envelope;
        try {
            envelope = requestBatch(REQUEST_TYPES, exclusions);
        } catch (Exception error) {
            if (!isUnsupportedTypeError(error)) throw error;
            envelope = requestBatch(LEGACY_REQUEST_TYPES, exclusions);
        }
        JSONObject payload = verifyEnvelope(envelope, "batch");
        int added = applyPayload(payload);
        try { flushEvents(); } catch (Exception ignored) { }
        return added;
    }

    private JSONObject requestBatch(List<String> types, JSONArray exclusions) throws Exception {
        JSONObject body = new JSONObject()
            .put("types", new JSONArray(types))
            .put("limit", 30)
            .put("excludeIds", exclusions);
        return NetworkClient.json(context, "POST", DeskPetApi.CONTENT_BATCH, body,
            licenses, NetworkClient.Auth.PREMIUM);
    }

    private boolean isUnsupportedTypeError(Exception error) {
        String message = error.getMessage();
        return message != null && message.contains("内容类型无效");
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

    private synchronized int applyPayload(JSONObject payload) throws Exception {
        long incomingVersion = payload.optLong("catalogVersion", -1L);
        if (incomingVersion < catalogVersion) return 0;
        Set<String> disabled = new HashSet<>();
        JSONArray disabledIds = payload.optJSONArray("disabledIds");
        if (disabledIds != null) {
            for (int i = 0; i < disabledIds.length(); i++) disabled.add(disabledIds.getString(i));
        }
        items.removeIf(item -> disabled.contains(item.id));
        Map<String, Item> existing = new HashMap<>();
        for (Item item : items) existing.put(item.id, item);
        int added = 0;
        JSONArray incoming = payload.getJSONArray("items");
        if (incoming.length() > 5000) throw new IllegalStateException("互动内容数量无效");
        for (int i = 0; i < incoming.length(); i++) {
            Item item = Item.fromJson(incoming.getJSONObject(i));
            if (!item.valid() || disabled.contains(item.id) || shown.contains(item.id)) continue;
            Item old = existing.get(item.id);
            if (old == null) {
                items.add(item);
                existing.put(item.id, item);
                added++;
            } else if (item.revision > old.revision) {
                items.set(items.indexOf(old), item);
                existing.put(item.id, item);
            }
        }
        while (items.size() > MAX_CACHE) {
            int localIndex = -1;
            for (int i = 0; i < items.size(); i++) {
                if (!items.get(i).online()) { localIndex = i; break; }
            }
            items.remove(localIndex >= 0 ? localIndex : 0);
        }
        catalogVersion = incomingVersion;
        save();
        return added;
    }

    private synchronized void resetFallback() {
        try (InputStream input = context.getAssets().open("interaction_fallback.json")) {
            byte[] data = NetworkClient.readLimited(input, 256 * 1024);
            JSONArray fallback = new JSONObject(new String(data, StandardCharsets.UTF_8)).getJSONArray("items");
            for (int i = 0; i < fallback.length(); i++) {
                Item item = Item.fromJson(fallback.getJSONObject(i));
                if (item.valid() && items.stream().noneMatch(current -> current.id.equals(item.id))) items.add(item);
            }
            save();
        } catch (Exception ignored) { }
    }

    private synchronized void load() {
        try {
            JSONArray storedItems = new JSONArray(store.getString(ITEMS, "[]"));
            for (int i = 0; i < storedItems.length(); i++) {
                Item item = Item.fromJson(storedItems.getJSONObject(i));
                if (item.valid()) items.add(item);
            }
            JSONArray storedShown = new JSONArray(store.getString(SHOWN, "[]"));
            for (int i = 0; i < storedShown.length() && shown.size() < MAX_SHOWN; i++)
                shown.add(storedShown.getString(i));
            JSONArray storedEvents = new JSONArray(store.getString(EVENTS, "[]"));
            for (int i = 0; i < storedEvents.length() && events.size() < MAX_EVENTS; i++)
                events.add(storedEvents.getJSONObject(i));
            lastType = store.getString(LAST_TYPE, "");
            catalogVersion = store.getLong(CATALOG_VERSION, 0L);
        } catch (Exception error) {
            items.clear();
            shown.clear();
            events.clear();
        }
    }

    private synchronized void save() {
        JSONArray storedItems = new JSONArray();
        for (Item item : items) storedItems.put(item.toJson());
        JSONArray storedShown = new JSONArray();
        for (String id : shown) storedShown.put(id);
        JSONArray storedEvents = new JSONArray();
        for (JSONObject event : events) storedEvents.put(event);
        store.edit().putString(ITEMS, storedItems.toString()).putString(SHOWN, storedShown.toString())
            .putString(EVENTS, storedEvents.toString()).putString(LAST_TYPE, lastType)
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
