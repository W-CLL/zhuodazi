package com.zhuodazi.android;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Random;

final class WordRepository {
    private final Context context;
    private final SettingsStore settings;
    private final Random random = new Random();

    WordRepository(Context context, SettingsStore settings) {
        this.context = context;
        this.settings = settings;
    }

    List<String> packs() {
        List<String> result = new ArrayList<>();
        try {
            String[] names = context.getAssets().list("");
            if (names != null) {
                Arrays.sort(names);
                for (String name : names) {
                    if (isWordPackAsset(name)) result.add(name);
                }
            }
        } catch (Exception ignored) { }
        return result;
    }

    private static boolean isWordPackAsset(String name) {
        if (!name.endsWith(".json") || name.equals("interaction_fallback.json")) return false;
        return !name.contains("/") && !name.contains("\\");
    }

    String displayName(String file) {
        return file.endsWith(".json") ? file.substring(0, file.length() - 5) : file;
    }

    String reaction(String action, String fallback) {
        return reaction(settings.wordPack(), action, fallback);
    }

    String reaction(String pack, String action, String fallback) {
        try (InputStream input = context.getAssets().open(pack);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int count;
            while ((count = input.read(buffer)) >= 0) output.write(buffer, 0, count);
            JSONObject root = new JSONObject(new String(output.toByteArray(), StandardCharsets.UTF_8));
            JSONArray values = root.getJSONObject("reactions").optJSONArray(action);
            if (values == null || values.length() == 0) {
                values = root.getJSONObject("reactions").optJSONArray("idle");
            }
            if (values != null && values.length() > 0) {
                return values.optString(random.nextInt(values.length()), fallback);
            }
        } catch (Exception ignored) { }
        return fallback;
    }
}
