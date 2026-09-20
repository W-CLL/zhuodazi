package com.zhuodazi.android;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;

final class WordRepository {
    private static final String ASSET_DIRECTORY = "word-packs";
    private static final String DEFAULT_PACK = "互联网嘴替.json";
    private final SettingsStore settings;
    private final Random random = new Random();
    private final Map<String, Map<String, List<String>>> bundledPacks = new LinkedHashMap<>();

    WordRepository(Context context, SettingsStore settings) {
        this.settings = settings;
        // Device vendors may add unrelated JSON assets at the APK root. Only our
        // dedicated directory and validated dialogue documents form the catalog.
        try {
            String[] names = context.getAssets().list(ASSET_DIRECTORY);
            if (names != null) {
                Arrays.sort(names);
                for (String name : names) {
                    if (!isWordPackAsset(name)) continue;
                    try (InputStream input = context.getAssets().open(ASSET_DIRECTORY + "/" + name)) {
                        JSONObject root = new JSONObject(new String(
                            NetworkClient.readLimited(input, 256 * 1024), StandardCharsets.UTF_8));
                        Map<String, List<String>> reactions = readReactions(root);
                        if (reactions != null) bundledPacks.put(name, reactions);
                    } catch (Exception ignored) { }
                }
            }
        } catch (Exception ignored) { }
        restoreValidSelection();
    }

    List<String> packs() {
        restoreValidSelection();
        return new ArrayList<>(bundledPacks.keySet());
    }

    private void restoreValidSelection() {
        if (!bundledPacks.containsKey(settings.wordPack())) {
            String replacement = bundledPacks.containsKey(DEFAULT_PACK) ? DEFAULT_PACK
                : bundledPacks.isEmpty() ? "" : bundledPacks.keySet().iterator().next();
            if (!replacement.equals(settings.wordPack())) settings.putString(SettingsStore.WORD_PACK, replacement);
        }
    }

    private static boolean isWordPackAsset(String name) {
        return name.endsWith(".json") && !name.contains("/") && !name.contains("\\");
    }

    private static Map<String, List<String>> readReactions(JSONObject root) {
        JSONObject source = root.optJSONObject("reactions");
        if (source == null) return null;
        Map<String, List<String>> reactions = new LinkedHashMap<>();
        for (Iterator<String> keys = source.keys(); keys.hasNext();) {
            String action = keys.next();
            JSONArray values = source.optJSONArray(action);
            if (values == null) return null;
            List<String> lines = new ArrayList<>();
            for (int index = 0; index < values.length(); index++) {
                Object value = values.opt(index);
                if (!(value instanceof String) || ((String) value).trim().isEmpty()) return null;
                lines.add(((String) value).trim());
            }
            if (!lines.isEmpty()) reactions.put(action, lines);
        }
        return reactions.containsKey("idle") ? reactions : null;
    }

    String displayName(String file) {
        return file.endsWith(".json") ? file.substring(0, file.length() - 5) : file;
    }

    String reaction(String action, String fallback) {
        restoreValidSelection();
        return reaction(settings.wordPack(), action, fallback);
    }

    String reaction(String pack, String action, String fallback) {
        Map<String, List<String>> reactions = bundledPacks.get(pack);
        if (reactions == null) return fallback;
        List<String> values = reactions.get(action);
        if (values == null) values = reactions.get("idle");
        return values == null || values.isEmpty() ? fallback : values.get(random.nextInt(values.size()));
    }
}
