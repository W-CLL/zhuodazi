package com.zhuodazi.android;

import android.content.Context;
import android.content.SharedPreferences;

final class SettingsStore {
    static final String PREFS = "zhuodazi_settings";
    static final String SIZE = "size_dp";
    static final String OPACITY = "opacity";
    static final String MIRRORED = "mirrored";
    static final String MOVEMENT = "random_movement";
    static final String INTERACTIONS = "random_interactions";
    static final String INTERACTION_MODE = "interaction_mode";
    static final String PERSONALITY = "personality";
    static final String RANDOM_PET = "random_pet";
    static final String RANDOM_PET_INTERVAL = "random_pet_interval";
    static final String ACTIVE_PET = "active_pet";
    static final String WORD_PACK = "word_pack";
    static final String START_ON_BOOT = "start_on_boot";
    static final String RUNNING = "service_running";
    static final String POSITION_X = "position_x";
    static final String POSITION_Y = "position_y";
    static final String IMPORTED_PET = "imported_pet";

    private final SharedPreferences values;

    SettingsStore(Context context) {
        values = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    int sizeDp() { return clamp(values.getInt(SIZE, 180), 96, 280); }
    int opacity() { return clamp(values.getInt(OPACITY, 100), 20, 100); }
    boolean mirrored() { return values.getBoolean(MIRRORED, false); }
    boolean movement() { return values.getBoolean(MOVEMENT, true); }
    boolean interactions() { return values.getBoolean(INTERACTIONS, true); }
    boolean randomPet() { return values.getBoolean(RANDOM_PET, true); }
    boolean startOnBoot() { return values.getBoolean(START_ON_BOOT, false); }
    boolean running() { return values.getBoolean(RUNNING, false); }
    String personality() { return values.getString(PERSONALITY, "lively"); }
    String interactionMode() { return values.getString(INTERACTION_MODE, "standard"); }
    String activePet() { return values.getString(ACTIVE_PET, ""); }
    String wordPack() { return values.getString(WORD_PACK, "元气夸夸.json"); }
    int randomPetInterval() { return values.getInt(RANDOM_PET_INTERVAL, 60); }
    int positionX(int fallback) { return values.getInt(POSITION_X, fallback); }
    int positionY(int fallback) { return values.getInt(POSITION_Y, fallback); }

    void putInt(String key, int value) { values.edit().putInt(key, value).apply(); }
    void putBoolean(String key, boolean value) { values.edit().putBoolean(key, value).apply(); }
    void putString(String key, String value) { values.edit().putString(key, value).apply(); }
    void setRunning(boolean running) { values.edit().putBoolean(RUNNING, running).apply(); }
    void savePosition(int x, int y) {
        values.edit().putInt(POSITION_X, x).putInt(POSITION_Y, y).apply();
    }

    static int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(maximum, value));
    }
}
