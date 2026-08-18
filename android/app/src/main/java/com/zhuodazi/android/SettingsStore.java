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
    static final String PET_HIDDEN = "pet_hidden";
    static final String CLICK_THROUGH = "click_through";
    static final String POSITION_X = "position_x";
    static final String POSITION_Y = "position_y";
    static final String TRIAL_EXPIRES_AT = "trial_expires_at";
    static final String SELECTED_TAB = "selected_tab";
    static final String THEATER_ENABLED = "theater_enabled";
    static final String THEATER_INTERVAL = "theater_interval";
    static final String THEATER_SCRIPTS = "theater_scripts";
    static final String REMINDERS = "reminders";
    static final String XIANYU_URL = "xianyu_url";
    static final String AUTO_CHECK_UPDATES = "auto_check_updates";
    static final String IGNORED_UPDATE_VERSION = "ignored_update_version";

    private final SharedPreferences values;

    SettingsStore(Context context) {
        values = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    int sizeDp() { return clamp(values.getInt(SIZE, 96), 96, 280); }
    int opacity() { return clamp(values.getInt(OPACITY, 100), 20, 100); }
    boolean mirrored() { return values.getBoolean(MIRRORED, false); }
    boolean movement() { return values.getBoolean(MOVEMENT, true); }
    boolean interactions() { return values.getBoolean(INTERACTIONS, true); }
    boolean randomPet() { return values.getBoolean(RANDOM_PET, true); }
    boolean startOnBoot() { return values.getBoolean(START_ON_BOOT, false); }
    boolean running() { return values.getBoolean(RUNNING, false); }
    boolean petHidden() { return values.getBoolean(PET_HIDDEN, false); }
    boolean clickThrough() { return values.getBoolean(CLICK_THROUGH, false); }
    boolean trialActive() { return trialExpiresAt() > System.currentTimeMillis(); }
    String personality() { return values.getString(PERSONALITY, "lively"); }
    String interactionMode() { return values.getString(INTERACTION_MODE, "standard"); }
    String activePet() { return values.getString(ACTIVE_PET, ""); }
    String wordPack() { return values.getString(WORD_PACK, "元气夸夸.json"); }
    int randomPetInterval() { return clamp(values.getInt(RANDOM_PET_INTERVAL, 300), 30, 3600); }
    int positionX(int fallback) { return values.getInt(POSITION_X, fallback); }
    int positionY(int fallback) { return values.getInt(POSITION_Y, fallback); }
    int selectedTab() { return clamp(values.getInt(SELECTED_TAB, 0), 0, 4); }
    long trialExpiresAt() { return values.getLong(TRIAL_EXPIRES_AT, 0L); }
    boolean theaterEnabled() { return values.getBoolean(THEATER_ENABLED, false); }
    int theaterInterval() {
        int value = values.getInt(THEATER_INTERVAL, 300);
        return value == 60 || value == 180 || value == 300 || value == 600 || value == 1800 ? value : 300;
    }
    String theaterScriptsJson() { return values.getString(THEATER_SCRIPTS, "[]"); }
    String remindersJson() { return values.getString(REMINDERS, "[]"); }
    String xianyuUrl() { return values.getString(XIANYU_URL, ""); }
    boolean autoCheckUpdates() { return values.getBoolean(AUTO_CHECK_UPDATES, true); }
    String ignoredUpdateVersion() { return values.getString(IGNORED_UPDATE_VERSION, ""); }

    void putInt(String key, int value) { values.edit().putInt(key, value).apply(); }
    void putLong(String key, long value) { values.edit().putLong(key, value).apply(); }
    void putBoolean(String key, boolean value) { values.edit().putBoolean(key, value).apply(); }
    void putString(String key, String value) { values.edit().putString(key, value).apply(); }
    void setRunning(boolean running) { putBoolean(RUNNING, running); }
    void setPetHidden(boolean hidden) { putBoolean(PET_HIDDEN, hidden); }
    void setClickThrough(boolean clickThrough) { putBoolean(CLICK_THROUGH, clickThrough); }
    void setTrialRemaining(int seconds) {
        long expiresAt = seconds <= 0 ? 0L : System.currentTimeMillis() + seconds * 1000L;
        putLong(TRIAL_EXPIRES_AT, expiresAt);
    }
    void savePosition(int x, int y) {
        values.edit().putInt(POSITION_X, x).putInt(POSITION_Y, y).apply();
    }

    static int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(maximum, value));
    }
}
